using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
using ReactorSourceMap = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap;

namespace Microsoft.UI.Reactor.Core;

// Reconciler partial: ReactorAttached.StateProperty (ReactorState): element tags, list state, detach, validation withdrawal.
public sealed partial class Reconciler
{
    // ════════════════════════════════════════════════════════════════════
    //  ReactorAttached.StateProperty  (ReactorState)
    // ════════════════════════════════════════════════════════════════════
    //
    // A single attached DP carrying a ReactorState wrapper that bundles
    //   (a) the current Reactor Element for this native element, and
    //   (b) the per-element ModifierEventHandlerState (current user handlers +
    //       stable trampoline delegates).
    //
    // The DP lives on the native DependencyObject, so every C# RCW pointing
    // at the same native element observes the same ReactorState — fixing the
    // duplicate-RCW event-subscription bug where ConditionalWeakTable keyed
    // per-RCW would return a different ModifierEventHandlerState for each wrapper
    // and attach duplicate trampolines on the native event source.
    //
    // Lifecycle:
    //   - Pool return / ListView recycle: ClearElementTag clears both element identities
    //     only, preserving state.Events. Attached trampolines survive; next
    //     rent/realize flows through Ensure*Subscribed which refreshes
    //     state.Current* rather than attaching duplicates. Re-attaching on
    //     every rent would re-introduce the bug this module exists to fix.
    //   - Permanent unmount (XamlHost/XamlPage — not pooled, may stay rooted
    //     by app code): DetachReactorState nulls Element, clears Current*,
    //     drops state.Events. Any orphan trampoline still on the native event
    //     source becomes a no-op if it fires.
    internal sealed class ReactorState
    {
        public Element? Element;
        // Target controls keep their own element for event dispatch. Flyout decorators
        // share that control, but need a separate identity for callbacks and teardown.
        internal Element? DecoratorElement;
        // Shared-control child roots with transform or opacity declarations to restore.
        internal List<(Element Parent, Element Target)>? ModifierTargets;
        // The .Ref cells the modifiers pointed at this control, whichever layer declared them: a
        // layer that mounts no control (a keyed Memo, a ModifiedElement) isn't on the element
        // chain unmount walks, so unmount clears these instead (#1659). An ElementRef, or a
        // List<ElementRef> when layers declare different refs.
        internal object? ProducerRefs;
        // The exit-bearing .Transition() a transparent keyed Memo declared over this control (#1663).
        // The control's tag is the Memo's factory output, so the exit paths can't reach the Memo's
        // own declaration through it. With nested keyed Memos the outermost one that declares an
        // exit owns the slot; if it later drops its exit under the same key, an inner Memo's exit
        // isn't restored, because the inner Memo isn't re-diffed while the outer key holds.
        internal Animation.ElementTransition? MemoTransition;
        // The keyed Memo owning MemoTransition wraps the flyout decorator in DecoratorElement, rather
        // than the flyout wrapping the Memo, so its exit comes before the decorator chain's.
        internal bool MemoWrapsDecorator;
        // Nested flyout updates share their target control. Only the outermost patches its native flyout.
        internal int ActiveFlyoutUpdates;
        // Composition and animation bookkeeping, allocated by the first composition feature the
        // control uses and never replaced (see CompositionState).
        internal CompositionState? Composition;
        // A navigation transition's completion can still reach the control after it leaves the tree.
        internal bool PoolExcluded;
        // A connected-animation source: an excluded return still drops the element it hosted.
        internal bool ReleaseWhenExcluded;
        internal ElementTheme? RealizationTheme;
        public ModifierEventHandlerState? Modifiers;
        // Native identity keeps teardown alive when WinRT replaces a collected managed wrapper.
        public Action<FrameworkElement>? UnmountAction;
        // Opaque identity scopes callbacks to a reconciler without retaining that reconciler.
        public object? UnmountActionOwner;
        // Per-native-element echo-suppress counter consumed by ChangeEchoSuppressor.
        // Lives here (not in a CWT-by-RCW) so that BeginSuppress on one managed
        // wrapper and ShouldSuppress on a different wrapper for the same native
        // DependencyObject see the same counter. See ChangeEchoSuppressor.cs.
        public int EchoSuppressCount;
        // §8.2 — depth of the active setter-suppression scope. While > 0, any
        // change event on this control is dropped without consuming a token
        // from EchoSuppressCount. See ApplySetters / ChangeEchoSuppressor.
        public int EchoSuppressScopeDepth;
        // Spec 047 §8 value-diff echo suppression. A programmatic controlled
        // write on a migrated single-value, exact-comparable round-trip (e.g.
        // ComboBox.SelectedIndex, ToggleSwitch.IsOn, RichEditBox.Text) arms this
        // one-shot predicate with the value it just wrote; the change-event
        // trampoline drops the single matching echo (readback satisfies the
        // predicate) via ChangeEchoSuppressor.ShouldSuppressEcho, then clears it.
        // Replaces the causal counter on those paths. The counter
        // (EchoSuppressCount / EchoSuppressScopeDepth) is RETAINED as the
        // fallback for paths value-diff cannot model: coercion (Slider/NumberBox
        // Min/Max), collection batch (CalendarView), the setter scope, the public
        // ReactorBinding.WriteSuppressed API, and double-valued controls.
        //
        // Single pending slot — correct only because migrated controls have
        // exactly one controlled round-trip property. Most raise its event
        // synchronously inside the write. RichEditBox and AutoSuggestBox raise
        // it later (AutoSuggestBox debounced, possibly merging writes); a newer
        // write overwrites the arm, and the next event consumes it and is
        // dropped only if its readback matches, so a late, merged or missing
        // echo can't swallow a real edit. Reset everywhere the counter is reset
        // (pool return / ClearCurrentEventHandlers / DetachReactorState) so a
        // stale arm can't suppress the first real event of a later lifecycle.
        public Func<object?, bool>? PendingEchoMatch;
        // Issue #1262 — per-native-element validation lifecycle bindings: a mounted
        // ValidationRule's producer identity, a FormField editor's blur binding, and
        // the FormField root's pointer at its current editor's binding. Stored here
        // rather than in a ConditionalWeakTable keyed by UIElement for the same reason
        // as EchoSuppressCount: WinRT can project two managed RCWs over one native
        // DependencyObject, and an update that saw a different wrapper would create a
        // fresh producer while the old one could never be retired — leaving a stale
        // validation message behind. Typed as object so Core does not depend on the
        // V1 composite lifecycle's private binding types.
        public object? ValidationRuleBinding;
        public object? ValidationTouchBinding;
        public object? ValidationRootBinding;
        // The (context, field) a FormField's attached validators last produced a
        // synchronous verdict for, so the contribution can be withdrawn when it moves
        // field, moves context, or stops being produced at all (issue #1262).
        public object? ValidationAttachedBinding;
        // Issue #986 — the AutomationId a deferred LabeledBy resolution is still
        // waiting to bind. ApplyAccessibilityModifiers can only resolve LabeledBy
        // once the element is in the visual tree, so an unresolved request parks a
        // Loaded handler. That handler outlives the render that created it, so it
        // must not write blindly: by the time Loaded fires the modifier may have
        // been dropped (reset arm) or retargeted at a different id, and a blind
        // write would stomp the reset and re-pin the stale label forever. The
        // handler therefore compares its captured id against this slot and no-ops
        // on a mismatch. Cleared on resolve, on unset, and everywhere the echo
        // state is cleared (pool return / ClearCurrentEventHandlers /
        // DetachReactorState) so a stale arm can't fire into a later lifecycle.
        // A Loaded resolution that still finds no target leaves it set: the request
        // is genuinely still pending, which ReactorDiagnostics.GetReferenceEdges reports.
        public string? PendingLabeledBy;
        // Spec 042 Phase 1 — keyed-list reconciliation state. Set when the
        // host element is a templated items control (ListView/GridView/
        // ItemsRepeater). The internal ObservableCollection<ReactorRow> in
        // ListState is what WinUI binds to so incremental insert/remove/move
        // ops translate into container-level animations rather than full
        // re-realization.
        public Internal.ReactorListState? ListState;
        // Flat item state shares the native control's existing attached state.
        public V1Protocol.ItemsHostState? ItemsHostState;

        // Spec 047 §14 Phase 3 close-out — descriptor-side per-index view
        // source. Populated by Reconciler.BindKeyedItemsSource when a
        // TemplatedItems<TItem,TElement,TControl> strategy is in effect.
        // HandleTemplatedContainerContentChanging and RefreshRealizedContainers
        // prefer this over the legacy TemplatedListElementBase fallback so
        // descriptor-driven controls never need to inherit from the legacy
        // abstract base.
        public Internal.IItemViewSource? ItemViewSource;

        // Provider stack snapshot for a virtualized host whose rows are realized
        // from WinUI layout callbacks outside the reconciler traversal (see
        // ContextCapture). Lives here rather than in a wrapper-keyed CWT so the
        // realization callback finds it through any managed wrapper of the control.
        public ContextCapture? ContextCapture;
        // Spec 047 §9.2 / §14 Phase 1 (1.7) — per-control event payload box.
        // Holds a strongly-typed payload struct (e.g. ToggleSwitchEventPayload)
        // discriminated by HandlerType. Populated for ported V1 controls and
        // for the control-intrinsic events (Button.Click, NumberBox immediate
        // inner-TextBox) that migrated off the routed ModifierEventHandlerState.
        // PRESERVED across pool rent/return per the Q18 contract (issue #114) —
        // trampolines stay subscribed for the control's lifetime; a stale type
        // is never observable post-rent because GetOrCreateControlEventPayload
        // re-creates the box on a HandlerType mismatch.
        public object? ControlEventState;

        /// <summary>
        /// Spec 057 reference-edge subscriptions owned by this control when it
        /// is a referrer. A control may carry many same-typed reference edges
        /// (RefNode has six) and may also need the single
        /// <see cref="ControlEventState"/> slot for controlled props
        /// (e.g. TeachingTip), so reference edges live in their own bag keyed by
        /// reference-entry slot index. Edges are torn down on unmount before pool
        /// return; they are not preserved across rent/return cycles.
        /// </summary>
        public ReferenceEdgeBag? ReferenceEdges;

        /// <summary>
        /// The <c>Resources</c> keys this control's <c>ResourceOverrides</c> wrote, so an
        /// update that drops a key can strip it (see <c>ApplyResourceOverrides</c>).
        /// Stored here rather than in a <c>ConditionalWeakTable</c> keyed by
        /// <see cref="FrameworkElement"/> for the same reason as
        /// <see cref="EchoSuppressCount"/>: once a control's managed wrapper is collected,
        /// WinRT projects a new one over the same native object, and a wrapper-keyed table
        /// then reports no managed keys, so a dropped override stays in <c>fe.Resources</c>.
        /// <c>ElementPool.CleanElement</c> strips these keys when the control returns to the
        /// pool, so no renter inherits them.
        /// </summary>
        public HashSet<string>? ManagedResourceKeys;

        /// <summary>
        /// For a templated <c>FlipView</c>: per logical item, the element it mounted or was last
        /// updated to and whether it realized a control. Updates diff against it (as a templated
        /// ListView diffs against the element tagged on its container) and map logical items to
        /// <c>Items</c> entries through it: an item whose view is Empty has no entry.
        /// </summary>
        public List<(Element Element, bool HasControl)>? MountedItemElements;

        /// <summary>
        /// <c>componentId</c> (<c>ReactorEventSource.ComponentRendered</c>) of the component
        /// whose wrapper this control is; 0 when none. Backs
        /// <c>ReactorTrace.TryGetComponentId</c>. Stored here rather than in a
        /// <c>ConditionalWeakTable</c> for the same duplicate-RCW reason as
        /// <see cref="EchoSuppressCount"/>: an inspector that reaches the control through
        /// the visual tree may hold a different managed wrapper than the reconciler did.
        /// Cleared when the component unmounts, before the wrapper can be pooled.
        /// </summary>
        public long ComponentDiagnosticId;

        // Warning counts follow the native control's lifetime, including pool reuse.
        public global::System.Collections.Concurrent.ConcurrentDictionary<long, int>? KeyedDiagnosticLedger;

        // Issue #965 — what the last BiDi inline overlay of each Thickness used as its base and
        // what it wrote. See ApplyModifiers.
        public InlineOverlayRecord? InlineOverlays;
    }

    /// <summary>
    /// Issue #965 — the base and the written value of the last inline overlay per Thickness
    /// property. With no declared physical value the overlay takes its unowned edges from the
    /// control, but on an update the control still carries the previous overlay, so reading it
    /// back would re-adopt the old inline edge (<c>.PaddingInlineStart(8)</c> →
    /// <c>.PaddingInlineEnd(4)</c> gave <c>8,0,4,0</c>). While the control still holds exactly
    /// what was written, the recorded base is the right one; any other value was written by
    /// someone else (a setter, app code, an outer layer) and is used as is.
    /// <para>Only the element that owns the control keeps a record. A flyout decorator or keyed
    /// memo applies its modifiers to another element's control after that element's own, so the
    /// live value it sees may be a write from this pass that merely equals its previous output;
    /// those layers read the live value.</para>
    /// </summary>
    internal sealed class InlineOverlayRecord
    {
        public InlineOverlay? Margin;
        public InlineOverlay? Padding;
        public InlineOverlay? Border;
    }

    /// <summary>
    /// One inline overlay: its base and the value it wrote.
    /// </summary>
    internal readonly record struct InlineOverlay(Thickness Base, Thickness Written);

    internal static class ReactorAttached
    {
        public static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached(
                "ReactorState",
                typeof(ReactorState),
                typeof(ReactorAttached),
                new PropertyMetadata(null));
    }

    internal static ReactorState GetOrCreateReactorState(UIElement fe)
    {
        if (fe.GetValue(ReactorAttached.StateProperty) is ReactorState state)
            return state;
        state = new ReactorState();
        fe.SetValue(ReactorAttached.StateProperty, state);
        return state;
    }

    private static InlineOverlayRecord? GetInlineOverlays(FrameworkElement fe)
        => (fe.GetValue(ReactorAttached.StateProperty) as ReactorState)?.InlineOverlays;

    private static InlineOverlayRecord GetOrCreateInlineOverlays(FrameworkElement fe)
        => GetOrCreateReactorState(fe).InlineOverlays ??= new InlineOverlayRecord();

    /// <summary>
    /// Issue #965 — the base for the edges an inline overlay does not own, given the control's
    /// live value. If the control still holds exactly what the previous overlay wrote, that is
    /// our own output, not a base, so the previous overlay's base is used instead.
    /// </summary>
    internal static Thickness UnoverlaidBase(Thickness live, InlineOverlay? previous)
        => previous is { } p && p.Written == live ? p.Base : live;

    /// <summary>
    /// Issue #207 — single attached-DP read for change-event trampolines.
    /// Returns the control's <see cref="ReactorState"/> (or <c>null</c>) with one
    /// <c>GetValue(StateProperty)</c> COM-interop read so a handler can check
    /// suppression (<see cref="ChangeEchoSuppressor.ShouldSuppress(ReactorState)"/>)
    /// and resolve the live element (<see cref="ReactorState.Element"/>) without a
    /// second DP read via <see cref="GetElementTag(FrameworkElement)"/>. On hot
    /// paths (TextChanged per keystroke, Slider.ValueChanged during drag) this
    /// halves the per-event DP traffic.
    /// </summary>
    internal static bool TryGetReactorState(FrameworkElement fe, [NotNullWhen(true)] out ReactorState? state)
    {
        state = fe.GetValue(ReactorAttached.StateProperty) as ReactorState;
        return state is not null;
    }

    // ── Typed TreeView<T> container hosting ──────────────────────────────
    //
    // The typed TreeView hosts each node's view imperatively (the WinUI
    // ItemTemplate-equivalent), exactly like the typed ListView: the
    // ItemTemplate is an empty ContentControl shell, and we populate each
    // realized container's ContentControl from the node's data item on
    // realization (and tear it down on recycle). This is what makes
    // expand/collapse robust — a fresh view is mounted into whichever pooled
    // container WinUI realizes the node into, so no element is ever stuck
    // parented to a stale (recycled) container. node.Content holds the
    // developer's data item (the boxed T) — WinUI-aligned, and read back by
    // the ItemInvoked / Expanding trampolines.
    //
    // Caches the internal TreeViewList (template part "ListControl", a public
    // ListView subclass) per TreeView so Update can reconcile realized
    // containers, and so the ContainerContentChanging subscription is attached
    // exactly once.
    internal readonly global::System.Runtime.CompilerServices.ConditionalWeakTable<WinUI.TreeView, WinUI.ListView> _typedTreeListControls = new();

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.3) — promoted from internal. Associates a
    /// control with its current Reactor element via the
    /// <see cref="ReactorAttached.StateProperty"/> attached DP so that
    /// trampolines can resolve the live element on dispatch.
    /// Provisional API; see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    public static void SetElementTag(FrameworkElement control, Element? element)
    {
        if (control.GetValue(ReactorAttached.StateProperty) is ReactorState state)
        {
            state.Element = element;
            if (element is null)
            {
                if (state.DecoratorElement is not null) V1Protocol.OverlayLifecycle.ForgetFlyoutOwner(control);
                state.DecoratorElement = null;
                state.ModifierTargets = null;
                state.MemoTransition = null;
                state.MemoWrapsDecorator = false;
            }
            return;
        }
        if (element is null) return; // nothing to store
        state = new ReactorState { Element = element };
        control.SetValue(ReactorAttached.StateProperty, state);
    }

    /// <summary>Stores a flyout decorator without replacing its target's event identity.</summary>
    internal static void SetDecoratorElementTag(FrameworkElement control, Element element)
        => GetOrCreateReactorState(control).DecoratorElement = element;

    internal void RefreshSkippedChildState(FrameworkElement control, Element oldElement, Element newElement)
    {
        if (newElement.HasCallbacks || IsFlyoutDecorator(newElement))
            RefreshElementTagOnSkip(control, oldElement, newElement);
        else if (CallSiteChangedOnSkip(oldElement, newElement))
            RefreshElementTagOnSkip(control, oldElement, newElement);
        if (global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported
            && Diagnostics.ReactorSourcePublisher.IsEnabled
            && CallSiteChangedOnSkip(oldElement, newElement))
            PublishSourceOnSkip(control, newElement);
        GestureDispatch.RefreshGestureDragStateOnSkip(control, oldElement.Modifiers, newElement.Modifiers);
    }

    /// <summary>
    /// Spec 047 §4.4 follow-up — allocation-gated element tagging for the V1
    /// dispatch adapter. Refreshes an existing <see cref="ReactorState"/>'s
    /// element pointer cheaply, but only <em>allocates</em> a new
    /// <see cref="ReactorState"/> (+ attached-DP write) for elements whose
    /// downstream code actually reads the element back via
    /// <see cref="GetElementTag(FrameworkElement)"/>.
    ///
    /// <para>Three things require a live element pointer on the control:</para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Event trampolines</b> — <see cref="Element.HasCallbacks"/>.
    ///     Routed-input modifiers (<c>.OnPointerPressed</c> etc.) dispatch
    ///     through <see cref="ModifierEventHandlerState"/>'s <c>Current*</c>
    ///     fields refreshed each render by <c>ApplyEventHandlers</c> and do
    ///     <em>not</em> resolve via the tag, so they are intentionally not
    ///     part of the gate.
    ///   </item>
    ///   <item>
    ///     <b>Keyed child reconciliation</b> — <see cref="Element.Key"/>.
    ///     <see cref="ChildReconciler"/> reads the child's tag to extract the
    ///     stable key during keyed reorders (see <c>ChildReconciler.cs:303</c>
    ///     and <c>:392</c>). A keyed callback-free element (e.g.
    ///     <c>Border(...).WithKey("a")</c>) loses its identity across reorders
    ///     without the tag.
    ///   </item>
    ///   <item>
    ///     <b>Element-extras readback</b> — <see cref="Element.Extensions"/>.
    ///     Several bucketed extras have engine paths that read the live
    ///     element via the tag — exit transitions
    ///     (<c>ElementTransition</c>), connected-animation snapshots
    ///     (<c>ConnectedAnimationKey</c>), <c>InteractionStates</c>,
    ///     <c>KeyframeAnimations</c>, and friends. The bucket being null is
    ///     spec 047 §4.4's "truly inert leaf" signal, so we tag whenever any
    ///     extra is present rather than enumerating each feature.
    ///   </item>
    /// </list>
    ///
    /// <para>Callback-free display leaves with no key and no extras
    /// (TextBlock / Border / StackPanel / Image — the bulk of a real tree)
    /// never dispatch into Reactor code and have nothing reading the tag, so
    /// they stay untagged. This mirrors the legacy reconciler discipline. The
    /// pre-§4.5 V1 adapter tagged <em>every</em> control unconditionally,
    /// allocating a ReactorState per leaf mount — the per-render byte
    /// regression this restores the savings for.</para>
    /// </summary>
    internal static void SetElementTagIfNeeded(FrameworkElement control, Element element)
    {
        if (control.GetValue(ReactorAttached.StateProperty) is ReactorState state)
        {
            // State already allocated (callback-bearing control, pooled reuse,
            // or echo/setter scope) — refresh the live element with no alloc.
            state.Element = element;
            return;
        }
        if (!NeedsTag(element)) return;
        control.SetValue(
            ReactorAttached.StateProperty,
            new ReactorState { Element = element });
    }

    /// <summary>
    /// V1 adapter variant. <paramref name="ownsTeardown"/> says the element's handler does
    /// teardown the generic unmount walk (<see cref="ForEachReactorChildControl(UIElement, Action{UIElement})"/>)
    /// does not, such as NavigationView's <c>PaneHeader</c> or NavigationHost's route
    /// subscription and page cache. Unmount dispatches to that
    /// handler through the tag, so in <see cref="SkipsCallSiteOnlyTags"/> mode a stamped
    /// element keeps it, exactly as every stamped element is tagged outside that mode.
    /// </summary>
    internal static void SetElementTagIfNeeded(FrameworkElement control, Element element, bool ownsTeardown)
    {
        if (ownsTeardown && SkipsCallSiteOnlyTags && element.Extensions is not null
            && control.GetValue(ReactorAttached.StateProperty) is not ReactorState)
        {
            control.SetValue(ReactorAttached.StateProperty, new ReactorState { Element = element });
            return;
        }
        SetElementTagIfNeeded(control, element);
    }

    /// <summary>
    /// Predicate companion to <see cref="SetElementTagIfNeeded(FrameworkElement, Element)"/>. Returns
    /// true when downstream code will read the element back through
    /// <see cref="GetElementTag(FrameworkElement)"/> — see the helper's
    /// summary for the three categories.
    /// </summary>
    /// <remarks>
    /// Spec 010 deliberately adds NO arm here for ordinary elements. A source-mapped
    /// element carries its <see cref="Element.CallSite"/> in the <see cref="Element.Extensions"/>
    /// bucket, so it already satisfies the <c>Extensions is not null</c> test
    /// above and is tagged without further help. An arm keyed on the source-map
    /// flag would only ever tag <em>unstamped</em> elements — which by
    /// definition have no location to read back — while re-introducing exactly
    /// the per-leaf <c>ReactorState</c> allocation PR #468 removed.
    ///
    /// <para>The one exception is component boundaries (<see cref="ComponentElement"/>,
    /// <see cref="FuncElement"/>, <see cref="MemoElement"/>). Their Border wrapper is the
    /// only realized control that marks where a component starts, so an inspector needs
    /// the tag to find the boundary — and the component type / render function — even
    /// when the element carries no location (built while mapping was off, by a factory
    /// the generator does not reach, or constructed directly). This costs one
    /// <c>ReactorState</c> per component, never per leaf, and only while
    /// <see cref="global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled"/> is on: the type test runs
    /// first, so leaves never read the flag.</para>
    /// </remarks>
    internal static bool NeedsTag(Element element) =>
        element.HasCallbacks
        || element.Key is not null
        || (element.Extensions is { } extras && (!SkipsCallSiteOnlyTags || !extras.IsBehaviorallyEmpty))
        || HasReferenceModifiers(element)
        || (IsComponentBoundary(element) && global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);

    /// <summary>
    /// True for the elements the reconciler mounts behind a component Border wrapper.
    /// <c>KeyedMemoElement</c> is deliberately absent: it is transparent and mounts its
    /// factory output directly, with no wrapper of its own.
    /// </summary>
    private static bool IsComponentBoundary(Element element) =>
        element is ComponentElement or FuncElement or MemoElement;

    /// <summary>
    /// Native AOT in diagnostics mode: an element whose ONLY extra is its call site is not
    /// tagged. That tag (a <see cref="ReactorState"/> plus its attached DP, ~1.2 KB native
    /// per control) exists so a managed inspector can read call sites, and no managed agent
    /// can load into a Native AOT process. The control keeps exactly the state an unstamped
    /// build gives it (every functional reader already handles that), and
    /// <c>ReactorSourceMap.GetSource</c> resolves its location from the published
    /// <see cref="Diagnostics.ReactorDiagnostics.SourceProperty"/> value instead.
    /// Guarded on the feature switch first, so a build without it folds this to false.
    /// </summary>
    internal static bool SkipsCallSiteOnlyTags =>
        global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported
        && Diagnostics.ReactorSourcePublisher.SkipsCallSiteOnlyTags;

    /// <summary>Identifies the three built-in target-wrapping flyout element shapes.</summary>
    internal static bool IsFlyoutDecorator(Element element) =>
        element is FlyoutElement or MenuFlyoutElement or CommandBarFlyoutElement;

    /// <summary>
    /// Refreshes a skipped element's current identity. Target-wrapping decorators keep
    /// their own slot so refreshing one cannot replace its target's event identity.
    /// Other elements retain the usual tag-allocation and teardown-ownership rules.
    /// </summary>
    internal void RefreshElementTagOnSkip(FrameworkElement control, Element? oldEl, Element newEl, bool tagOnly = false)
    {
        if (IsFlyoutDecorator(newEl)
            && _v1Handlers.TryGet(newEl.GetType(), out var entry) && entry.PreservesTargetTag)
        {
            var state = GetOrCreateReactorState(control);
            if (state.ActiveFlyoutUpdates != 0) return;
            // A skipped source view can be unkeyed even after its list row published an item key.
            if (!tagOnly && ReferenceEquals(oldEl, newEl) && newEl.Key is null
                && state.DecoratorElement?.Key is { } itemKey)
                newEl = newEl with { Key = itemKey };
            state.DecoratorElement = newEl;
        }
        else
            SetElementTagIfNeeded(control, newEl,
                SkipsCallSiteOnlyTags && _v1Handlers.TryGet(newEl.GetType(), out var teardownEntry)
                && teardownEntry is IV1TeardownOwner owner && owner.OwnsTeardown(control));
    }

    /// <summary>
    /// Spec 010 — did the source location change across a shallow skip?
    ///
    /// <para><see cref="Element.ShallowEquals"/> deliberately ignores
    /// <see cref="Element.CallSite"/>, so two structurally identical
    /// callback-free leaves rendered from DIFFERENT lines (the two arms of a
    /// conditional, say) compare equal and take the skip path. That is the right
    /// call for rendering — nothing about the control needs to change — but the
    /// control's back-pointer would keep naming the branch that is no longer
    /// live, and <c>ReactorSourceMap.GetSource</c> would confidently report the
    /// wrong line. Same class of problem as the #721 gesture/drag slots, and
    /// handled the same way: refresh on skip.</para>
    ///
    /// <para>Compares the EFFECTIVE call site — the one <c>GetSource</c> would report —
    /// which for a target-wrapping decorator is its innermost target's, not its own. A
    /// stable <c>Flyout(...)</c> whose target switches between two otherwise-equal
    /// elements built on different lines has an unchanged <c>CallSite</c> of its own, so
    /// comparing only the outer element would take the skip and leave the control
    /// reporting the target line that is no longer live.</para>
    ///
    /// <para>Costs one nullable-struct compare when source mapping is off, where
    /// both sides are null and this is always false — so no control fetch and no
    /// DependencyProperty write on the flag-off path. The decorator unwrap runs only
    /// when one side actually is a decorator.</para>
    /// </summary>
    internal static bool CallSiteChangedOnSkip(Element oldEl, Element newEl)
    {
        if (oldEl.CallSite != newEl.CallSite) return true;

        var oldTarget = global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.DecoratorTarget(oldEl);
        var newTarget = global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.DecoratorTarget(newEl);
        if (oldTarget is null && newTarget is null) return false;

        return EffectiveCallSite(oldTarget ?? oldEl) != EffectiveCallSite(newTarget ?? newEl);
    }

    /// <summary>The call site <c>ReactorSourceMap.GetSource</c> would report for an element.</summary>
    /// <remarks>
    /// Shares <c>ReactorSourceMap.UnwrapDecorators</c> with <c>GetSource</c> so the two
    /// cannot disagree, and so this path is cycle-safe: a third-party
    /// <c>GetSourceTarget</c> that returns its own element would otherwise hang
    /// reconciliation here, not merely the inspector.
    /// </remarks>
    private static SourceLocation? EffectiveCallSite(Element element)
        => global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.UnwrapDecorators(element)?.CallSite;

    /// <summary>
    /// True when the element carries any reactive reference modifier — the imperative
    /// <c>.Ref</c> producer slot, an <c>XYFocus*</c> edge, or an accessibility
    /// relationship edge. Such elements are tagged so the unmount path reaches them (CR-001).
    /// The producer ref itself is cleared from the refs <c>ApplyModifiers</c> recorded on the
    /// control's <see cref="ReactorState"/>, whichever layer declared it (#1659).
    /// </summary>
    private static bool HasReferenceModifiers(Element element)
    {
        var m = element.Modifiers;
        if (m is null) return false;
        if (m.Ref is not null
            || m.XYFocusUpRef is not null || m.XYFocusDownRef is not null
            || m.XYFocusLeftRef is not null || m.XYFocusRightRef is not null
            || m.ToolTipPlacementTargetRef is not null)
            return true;

        var a = m.Accessibility;
        return a is not null
            && (a.LabeledByRef is not null || a.DescribedByRefs is not null
                || a.FlowsToRefs is not null || a.FlowsFromRefs is not null);
    }

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.3) — promoted from internal. Retrieves the
    /// element associated with a control, or null.
    /// Provisional API; see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    /// <remarks>
    /// For the built-in Flyout, MenuFlyout, and CommandBarFlyout decorators,
    /// this reads the target's tag; decorator identity is stored separately.
    /// </remarks>
    public static Element? GetElementTag(UIElement control) =>
        control is FrameworkElement fe
            ? (fe.GetValue(ReactorAttached.StateProperty) as ReactorState)?.Element
            : null;

    /// <summary>
    /// Spec 047 §14 Phase 1 (1.3) — promoted from internal.
    /// Provisional API; see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    /// <remarks>
    /// For the built-in Flyout, MenuFlyout, and CommandBarFlyout decorators,
    /// this reads the target's tag; decorator identity is stored separately.
    /// </remarks>
    public static Element? GetElementTag(FrameworkElement fe) =>
        (fe.GetValue(ReactorAttached.StateProperty) as ReactorState)?.Element;

    /// <summary>
    /// Gets the outer element mounted on a control, including a flyout decorator
    /// whose target keeps its own element tag for control events.
    /// </summary>
    internal static Element? GetMountedElement(UIElement control)
    {
        if (control is not FrameworkElement fe
            || fe.GetValue(ReactorAttached.StateProperty) is not ReactorState state)
            return null;
        return state.DecoratorElement ?? state.Element;
    }

    /// <summary>Walks the supplied or mounted decorator chain, returning the first non-null value.</summary>
    internal static T? FindMountedElementValue<T>(UIElement control, Func<Element, T?> select, Element? current = null) where T : class
    {
        current ??= GetMountedElement(control);
        for (var depth = 0; current is not null; depth++)
        {
            if (select(current) is { } value) return value;
            if (depth == ReactorSourceMap.MaxDecoratorDepth) break;
            current = ReactorSourceMap.DecoratorTarget(current);
        }
        return null;
    }

    // ────────────────────────────────────────────────────────────────────
    // Spec 042 Phase 1 — keyed-list reconciliation state accessors.
    // Stored on the same attached ReactorState as Element/Events so we do
    // not pay for a second DependencyProperty on every realized container.
    // Only mounted templated items controls populate this.
    // ────────────────────────────────────────────────────────────────────

    internal static Internal.ReactorListState? GetListState(DependencyObject control)
    {
        if (control is FrameworkElement fe
            && fe.GetValue(ReactorAttached.StateProperty) is ReactorState state)
            return state.ListState;
        return null;
    }

    internal static void SetListState(FrameworkElement control, Internal.ReactorListState listState)
    {
        if (control.GetValue(ReactorAttached.StateProperty) is ReactorState state)
        {
            state.ListState = listState;
            return;
        }
        state = new ReactorState { ListState = listState };
        control.SetValue(ReactorAttached.StateProperty, state);
    }

    // ── §14 Phase 3 close-out: per-control descriptor view source ──────

    internal static Internal.IItemViewSource? GetItemViewSource(DependencyObject control)
    {
        if (control is FrameworkElement fe
            && fe.GetValue(ReactorAttached.StateProperty) is ReactorState state)
            return state.ItemViewSource;
        return null;
    }

    internal static void SetItemViewSource(FrameworkElement control, Internal.IItemViewSource viewSource)
    {
        if (control.GetValue(ReactorAttached.StateProperty) is ReactorState state)
        {
            state.ItemViewSource = viewSource;
            return;
        }
        state = new ReactorState { ItemViewSource = viewSource };
        control.SetValue(ReactorAttached.StateProperty, state);
    }

    /// <summary>
    /// Clears the control and decorator element pointers while preserving event wiring. Call on
    /// pool return — attached trampolines stay valid so that rent/re-mount
    /// reuses the same subscriptions (re-attaching would re-introduce the
    /// duplicate-subscription bug).
    /// </summary>
    internal static void ClearElementTag(FrameworkElement fe)
    {
        if (fe.GetValue(ReactorAttached.StateProperty) is ReactorState state)
        {
            if (state.DecoratorElement is not null) V1Protocol.OverlayLifecycle.ForgetFlyoutOwner(fe);
            state.Element = null;
            state.DecoratorElement = null;
            state.ModifierTargets = null;
            state.MemoTransition = null;
            state.MemoWrapsDecorator = false;
            if (state.Composition?.ImplicitClaims is not null) Animation.ImplicitAnimationSlots.ReleaseAll(fe);
        }
        ClearDetachedProperties(fe);
    }

    /// <summary>
    /// Drops the published source and modifier ownership attached to a native control when it
    /// leaves Reactor's ownership (pool return, recycle, detach), even without a ReactorState.
    /// </summary>
    private static void ClearDetachedProperties(FrameworkElement fe)
    {
        ForegroundModifier.ClearApplied(fe);
        if (global::Microsoft.UI.Reactor.Hosting.ReactorFeatures.DevtoolsSupported && Diagnostics.ReactorSourcePublisher.IsEnabled)
            fe.ClearValue(Diagnostics.ReactorDiagnostics.SourceProperty);
    }

    /// <summary>
    /// Drops non-owning item refresh snapshots when a control leaves the tree.
    /// ItemsHostState also owns child teardown, so ItemsHostReconciliation.Unbind
    /// clears it after reaching those children, not in this pre-unmount helper.
    /// </summary>
    internal static void ClearRefreshSnapshots(FrameworkElement fe)
    {
        if (fe.GetValue(ReactorAttached.StateProperty) is ReactorState state)
            ClearRefreshSnapshots(state);
    }

    internal static void ClearRefreshSnapshots(ReactorState state)
    {
        state.MountedItemElements = null;
    }

    /// <summary>
    /// Clears the Current* user-handler delegates from the per-element
    /// ModifierEventHandlerState while leaving the trampoline subscription on the
    /// underlying control. Call from <see cref="ElementPool.CleanElement"/>
    /// so the next rent doesn't fire the previous component's captured
    /// rerender closure. TASK-060.
    /// Also resets the echo-suppress counter so a stranded BeginSuppress
    /// (e.g. a write whose change-event got swallowed) can't suppress a
    /// real user event after the element is rented to a new owner.
    /// </summary>
    internal static void ClearCurrentEventHandlers(FrameworkElement fe)
    {
        if (fe.GetValue(ReactorAttached.StateProperty) is ReactorState state)
        {
            state.Modifiers?.ClearCurrentHandlers();
            state.EchoSuppressCount = 0;
            state.EchoSuppressScopeDepth = 0;
            state.PendingEchoMatch = null;
            state.PendingLabeledBy = null;
            // A pooled host must not hand its provider values (and the objects they
            // reference) to an unrelated renter. Empty the capture before dropping it:
            // the host's ElementFactory aliases the same instance.
            state.ContextCapture?.Clear();
            state.ContextCapture = null;
        }
    }

    /// <summary>
    /// Fully detaches reactor state from a control: nulls the Element and
    /// clears the ModifierEventHandlerState's Current* delegates so any already-
    /// attached trampoline on the native event source becomes a no-op if it
    /// fires. Use when the control leaves reactor's ownership for good
    /// (XamlHost / XamlPage unmount) but may remain alive because the app
    /// holds a reference — without this, captured closures stay reachable
    /// and stale reactor callbacks can still fire.
    /// </summary>
    /// <summary>
    /// Spec 047 §14 Phase 1 (1.3) — promoted from internal.
    /// Provisional API; see <c>REACTOR_V1_PREVIEW</c>.
    /// </summary>
    public static void DetachReactorState(FrameworkElement fe)
    {
        ClearDetachedProperties(fe);
        if (fe.GetValue(ReactorAttached.StateProperty) is not ReactorState state)
            return;
        if (state.DecoratorElement is not null) V1Protocol.OverlayLifecycle.ForgetFlyoutOwner(fe);
        ClearTrackedProducerRefs(fe);
        ReferenceEdgeWiring.TeardownReferenceEdges(fe);
        // A detached component wrapper must stop answering for its ComponentRendered id
        // (both directions), like the unmount path does before pooling.
        if (state.ComponentDiagnosticId != 0)
            Diagnostics.ComponentRenderControls.Registry.Forget(state.ComponentDiagnosticId, fe);
        state.Element = null;
        state.DecoratorElement = null;
        state.ModifierTargets = null;
        state.MemoTransition = null;
        state.MemoWrapsDecorator = false;
        if (state.Composition is { } composition)
        {
            if (composition.ImplicitClaims is not null) Animation.ImplicitAnimationSlots.ReleaseAll(fe);
            // A pending opacity restart holds a layout handler and its reconciler (#1669).
            CancelOpacityRestart(composition);
        }
        state.UnmountAction = null;
        state.UnmountActionOwner = null;
        state.Modifiers?.ClearCurrentHandlers();
        state.Modifiers = null;
        state.EchoSuppressCount = 0;
        state.EchoSuppressScopeDepth = 0;
        state.PendingEchoMatch = null;
        state.PendingLabeledBy = null;
        // Item maps describe the items this control showed; a detached control has none.
        ClearRefreshSnapshots(state);
        // Issue #1262 — a retired control must not keep a ValidationContext (or a
        // pending async rule) alive through a binding the normal FormField /
        // ValidationRule unmount path never got to clear. Neutralize before dropping
        // the slot: the binding object is captured by a once-per-lifetime LostFocus
        // handler that outlives detach, and a pending async rule reads its context
        // when it resolves.
        (state.ValidationTouchBinding as IValidationBindingReset)?.Reset();
        (state.ValidationRootBinding as IValidationBindingReset)?.Reset();
        (state.ValidationRuleBinding as IValidationBindingReset)?.Reset();
        (state.ValidationAttachedBinding as IValidationBindingReset)?.Reset();
        state.ValidationTouchBinding = null;
        state.ValidationRootBinding = null;
        state.ValidationRuleBinding = null;
        state.ValidationAttachedBinding = null;
        // A retired host's native subtree (and its ItemTemplate factory, which aliases
        // this capture) can outlive Reactor ownership — e.g. a nested list inside a
        // parked repeater row — so release the provider values, not just the reference.
        state.ContextCapture?.Clear();
        state.ContextCapture = null;
    }

    /// <summary>
    /// Lets <see cref="DetachReactorState"/> neutralize a validation binding without
    /// Core depending on the V1 composite lifecycle's private binding types.
    /// </summary>
    internal interface IValidationBindingReset
    {
        void Reset();
    }

    /// <summary>
    /// Issue #1262 — withdraws the validation verdict a control carried, on its way out
    /// of the tree.
    /// <para>
    /// Teardown runs down two parallel paths: <see cref="UnmountRecursive"/> for an
    /// ordinary unmount and <c>UnmountAndCollect</c> for the pooling traversal that
    /// child removal uses. Only the second one runs for a conditionally removed poolable
    /// control, so this lives in one helper both call rather than in either of them.
    /// </para>
    /// <para>
    /// The binding retracts only while it still owns the slot, so an incoming control
    /// that already replaced the verdict is not disturbed by the outgoing one; and
    /// <c>Reset</c> drops the context reference, so a pooled control does not keep a
    /// <c>ValidationContext</c> alive for its next renter.
    /// </para>
    /// </summary>
    private static void WithdrawAttachedValidation(UIElement control)
    {
        if (control is FrameworkElement fe
            && fe.GetValue(ReactorAttached.StateProperty) is ReactorState state)
        {
            (state.ValidationAttachedBinding as IValidationBindingReset)?.Reset();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Lazy event wiring for poolable types
    // ════════════════════════════════════════════════════════════════════
    //
    // Non-poolable controls are created fresh on every Mount, so lazy wiring
    // works off the (oldEl, newEl) diff alone — wire at Mount when the handler
    // is non-null, wire at Update when it transitions null → non-null.
    //
    // Poolable controls (Button, TextBox, ToggleSwitch) are different: the pool
    // intentionally retains WinRT event subscriptions across rent/return (see
    // ElementPool.CleanElement), but clears Tag. So "Tag is null" on rent does
    // NOT mean "unwired" — the control may already have a trampoline attached.
    // We need a per-control dedupe key that survives pool cycles to avoid
    // double-subscription.
    //
    // All poolable wiring dedupes through ModifierEventHandlerState (attached on
    // ReactorAttached.StateProperty, which is a DependencyProperty keyed by
    // native DO identity). Two managed RCWs over the same native DependencyObject
    // resolve to the same ModifierEventHandlerState, so the *Trampoline-is-not-null
    // check correctly short-circuits. This was previously split between an
    // unsafe managed-FE-keyed CWT (Button, TextBox, Image, ScrollViewer) and
    // ModifierEventHandlerState (ToggleSwitch); issue #114 unified everything onto
    // ModifierEventHandlerState.
}
