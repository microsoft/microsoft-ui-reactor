using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Windows.UI.Text;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;

namespace Microsoft.UI.Reactor.Core;

// ════════════════════════════════════════════════════════════════════════
//  Base types
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// A lightweight, immutable description of a UI node (the "virtual DOM").
/// Elements are cheap to create and diff — they never touch real controls directly.
/// </summary>
// <snippet:element-record>
public abstract record Element
{
    /// <summary>
    /// Optional key for stable identity across re-renders (like React's key prop).
    /// When set, the reconciler uses it to match elements across list reorderings.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    /// Layout modifiers (margin, padding, size, alignment, etc.) applied to this element.
    /// Set via fluent extension methods: Text("hi").Margin(10).Width(200)
    /// Modifiers are stored inline so the concrete element type is preserved through chaining.
    /// </summary>
    public ElementModifiers? Modifiers { get; init; }
// </snippet:element-record>

    /// <summary>
    /// Outer margin shim that routes to <see cref="Modifiers"/>. Lets
    /// <c>el with { Margin = new Thickness(8) }</c> work directly on a record
    /// initializer (where extension methods are not visible). Identical
    /// semantics to <c>.Margin(...)</c>.
    /// </summary>
    public Thickness? Margin
    {
        get => Modifiers?.Margin;
        init => Modifiers = Modifiers is null
            ? new ElementModifiers { Margin = value }
            : Modifiers with { Margin = value };
    }

    /// <summary>
    /// Inner padding shim that routes to <see cref="Modifiers"/>. Lets
    /// <c>el with { Padding = new Thickness(8) }</c> work directly on a record
    /// initializer (where extension methods are not visible). Identical
    /// semantics to <c>.Padding(...)</c>.
    /// </summary>
    public Thickness? Padding
    {
        get => Modifiers?.Padding;
        init => Modifiers = Modifiers is null
            ? new ElementModifiers { Padding = value }
            : Modifiers with { Padding = value };
    }

    /// <summary>
    /// Cross-cutting extras (attached properties, transitions, theme bindings,
    /// animation configs, resource overrides, context values) bucketed into a
    /// lazy sub-record (spec 047 §4.4). The common case — a leaf with none of
    /// these — leaves this slot null, so the 14 fields below cost one reference
    /// instead of 14 inline slots on every <see cref="Element"/>.
    /// The setter is <c>internal</c> (PR #455 CR item #6): ordinary code uses
    /// the field shim properties (Attached, ThemeBindings, AnimationConfig, …)
    /// below and never observes this slot. Keeping the setter off the public
    /// surface removes an initializer-ordering footgun — a public
    /// <c>with { Extensions = X }</c> after a shim write would have silently
    /// discarded the shim. The engine and perf-critical internal paths may
    /// still set it directly.
    /// </summary>
    /// <remarks>Spec 047 §4.4.</remarks>
    public ElementExtras? Extensions { get; internal init; }

    /// <summary>
    /// Collapses an all-null <see cref="ElementExtras"/> back to a null
    /// <see cref="Extensions"/> slot (PR #455 CR item #2). The bucketed shim
    /// setters below route through this so that writing a bucketed field to
    /// <c>null</c> never materializes (or keeps) a non-null empty bucket — an
    /// empty bucket is <em>not</em> <c>Equals</c> to <c>null</c>, which would
    /// otherwise break the synthesized record equality between an extras-free
    /// element and one that had a field set to null (e.g.
    /// <c>(x with { Attached = null }) == x</c>).
    /// </summary>
    private static ElementExtras? NormalizeExtras(ElementExtras extras)
        => extras.IsEmpty ? null : extras;

    /// <summary>
    /// Attached properties from parent containers (Grid.Row, Canvas.Left, etc.).
    /// Set via fluent extension methods: Text("hi").Grid(row: 1, column: 2)
    /// Stored as a type-keyed dictionary so each provider defines its own data record.
    /// </summary>
    public IReadOnlyDictionary<Type, object>? Attached
    {
        get => Extensions?.Attached;
        init => Extensions = value is null && Extensions is null ? null
            : NormalizeExtras(Extensions is null ? new ElementExtras { Attached = value } : Extensions with { Attached = value });
    }

    /// <summary>
    /// Implicit transitions (opacity, scale, rotation, translation, background).
    /// Set via fluent extension methods: Rectangle().WithOpacityTransition()
    /// Applied by the reconciler after mount/update, so they are always present when
    /// property values are set via .Set() callbacks.
    /// </summary>
    public ImplicitTransitions? ImplicitTransitions
    {
        get => Extensions?.ImplicitTransitions;
        init => Extensions = value is null && Extensions is null ? null
            : NormalizeExtras(Extensions is null ? new ElementExtras { ImplicitTransitions = value } : Extensions with { ImplicitTransitions = value });
    }

    /// <summary>
    /// Theme transitions (children, item container).
    /// Set via fluent extension methods: VStack(children).WithThemeTransitions(...)
    /// </summary>
    public ThemeTransitions? ThemeTransitions
    {
        get => Extensions?.ThemeTransitions;
        init => Extensions = value is null && Extensions is null ? null
            : NormalizeExtras(Extensions is null ? new ElementExtras { ThemeTransitions = value } : Extensions with { ThemeTransitions = value });
    }

    /// <summary>
    /// Theme-resource bindings for brush properties (Background, Foreground, BorderBrush).
    /// When set, the reconciler resolves from WinUI theme resources instead of using local values.
    /// Set via fluent extension methods: Text("hi").Background(Theme.Accent)
    /// </summary>
    public IReadOnlyDictionary<string, ThemeRef>? ThemeBindings
    {
        get => Extensions?.ThemeBindings;
        init => Extensions = value is null && Extensions is null ? null
            : NormalizeExtras(Extensions is null ? new ElementExtras { ThemeBindings = value } : Extensions with { ThemeBindings = value });
    }

    /// <summary>
    /// Composition-layer layout animation configuration.
    /// When set, the reconciler attaches implicit animations to the element's Visual
    /// so that layout-driven position (and optionally size) changes animate smoothly.
    /// Set via fluent extension methods: Border(child).LayoutAnimation()
    /// </summary>
    public LayoutAnimationConfig? LayoutAnimation
    {
        get => Extensions?.LayoutAnimation;
        init => Extensions = value is null && Extensions is null ? null
            : NormalizeExtras(Extensions is null ? new ElementExtras { LayoutAnimation = value } : Extensions with { LayoutAnimation = value });
    }

    /// <summary>
    /// Compositor property animation configuration (.Animate() modifier).
    /// When set, the reconciler creates ImplicitAnimationCollection entries on the
    /// element's Visual for Opacity/Scale/Rotation/Offset/CenterPoint.
    /// </summary>
    public Microsoft.UI.Reactor.Animation.AnimationConfig? AnimationConfig
    {
        get => Extensions?.AnimationConfig;
        init => Extensions = value is null && Extensions is null ? null
            : NormalizeExtras(Extensions is null ? new ElementExtras { AnimationConfig = value } : Extensions with { AnimationConfig = value });
    }

    /// <summary>
    /// Element enter/exit transition configuration (.Transition() modifier).
    /// When set, the reconciler animates mount (enter) and unmount (exit) with
    /// compositor animations, deferring removal until exit animation completes.
    /// </summary>
    public Microsoft.UI.Reactor.Animation.ElementTransition? ElementTransition
    {
        get => Extensions?.ElementTransition;
        init => Extensions = value is null && Extensions is null ? null
            : NormalizeExtras(Extensions is null ? new ElementExtras { ElementTransition = value } : Extensions with { ElementTransition = value });
    }

    /// <summary>
    /// Interaction states configuration (.InteractionStates() modifier).
    /// When set, the reconciler registers pointer event handlers that drive
    /// zero-reconcile visual state transitions (hover, pressed, focused).
    /// </summary>
    public Microsoft.UI.Reactor.Animation.InteractionStatesConfig? InteractionStates
    {
        get => Extensions?.InteractionStates;
        init => Extensions = value is null && Extensions is null ? null
            : NormalizeExtras(Extensions is null ? new ElementExtras { InteractionStates = value } : Extensions with { InteractionStates = value });
    }

    /// <summary>
    /// Stagger configuration for container children (.Stagger() modifier).
    /// When set, child animations (enter, layout, property) have incrementing
    /// DelayTime = childIndex * staggerDelay.
    /// </summary>
    public Microsoft.UI.Reactor.Animation.StaggerConfig? StaggerConfig
    {
        get => Extensions?.StaggerConfig;
        init => Extensions = value is null && Extensions is null ? null
            : NormalizeExtras(Extensions is null ? new ElementExtras { StaggerConfig = value } : Extensions with { StaggerConfig = value });
    }

    /// <summary>
    /// Keyframe animation definitions (.Keyframes() modifier).
    /// Trigger-based: plays when the trigger value changes between renders.
    /// </summary>
    public Microsoft.UI.Reactor.Animation.KeyframeEntry[]? KeyframeAnimations
    {
        get => Extensions?.KeyframeAnimations;
        init => Extensions = value is null && Extensions is null ? null
            : NormalizeExtras(Extensions is null ? new ElementExtras { KeyframeAnimations = value } : Extensions with { KeyframeAnimations = value });
    }

    /// <summary>
    /// Scroll-linked expression animation configuration (.ScrollLinked() modifier).
    /// Expression animations run on the compositor, driven by ScrollViewer position.
    /// </summary>
    public Microsoft.UI.Reactor.Animation.ScrollAnimationConfig? ScrollAnimation
    {
        get => Extensions?.ScrollAnimation;
        init => Extensions = value is null && Extensions is null ? null
            : NormalizeExtras(Extensions is null ? new ElementExtras { ScrollAnimation = value } : Extensions with { ScrollAnimation = value });
    }

    /// <summary>
    /// Connected animation key for cross-container transitions.
    /// When set, the reconciler automatically captures a visual snapshot on unmount
    /// (via ConnectedAnimationService.PrepareToAnimate) and starts the animation on
    /// mount if a prepared animation with the same key exists.
    /// Set via fluent extension method: Border(child).ConnectedAnimation("hero")
    /// </summary>
    public string? ConnectedAnimationKey
    {
        get => Extensions?.ConnectedAnimationKey;
        init => Extensions = value is null && Extensions is null ? null
            : NormalizeExtras(Extensions is null ? new ElementExtras { ConnectedAnimationKey = value } : Extensions with { ConnectedAnimationKey = value });
    }

    /// <summary>
    /// Per-control resource overrides (lightweight styling). When set, the reconciler
    /// injects these into <see cref="FrameworkElement.Resources"/> so that the control's
    /// VisualStateManager picks them up for hover/pressed/disabled states.
    /// Set via fluent extension: <c>Button("Go").Resources(r => r.Set("ButtonBackground", "#0078D4"))</c>
    /// </summary>
    public Microsoft.UI.Reactor.Elements.ResourceOverrides? ResourceOverrides
    {
        get => Extensions?.ResourceOverrides;
        init => Extensions = value is null && Extensions is null ? null
            : NormalizeExtras(Extensions is null ? new ElementExtras { ResourceOverrides = value } : Extensions with { ResourceOverrides = value });
    }

    /// <summary>
    /// Context values provided to this element's subtree via .Provide().
    /// The reconciler pushes these onto the context scope when entering
    /// this element's subtree and pops them when leaving.
    /// </summary>
    public IReadOnlyDictionary<ContextBase, object?>? ContextValues
    {
        get => Extensions?.ContextValues;
        init => Extensions = value is null && Extensions is null ? null
            : NormalizeExtras(Extensions is null ? new ElementExtras { ContextValues = value } : Extensions with { ContextValues = value });
    }

    /// <summary>
    /// Gets the attached property data of the specified type, or null if not set.
    /// </summary>
    internal T? GetAttached<T>() where T : class =>
        Attached is not null && Attached.TryGetValue(typeof(T), out var val) ? (T)val : null;

    /// <summary>
    /// Returns a copy of this element with the given attached property data set.
    /// Used by Grid/Canvas/RelativePanel extension methods.
    /// </summary>
    internal Element SetAttached(object data)
    {
        var key = data.GetType();
        var existing = Attached;
        // #155 — the overwhelmingly common case is a single attached value per
        // element (e.g. one .Grid(row, column) per cell, re-applied every render).
        // Store it in a tiny single-entry dictionary instead of allocating a full
        // Dictionary<Type, object> (bucket + entry arrays) for every cell, every
        // frame. A real Dictionary is only materialized when an element actually
        // carries two or more distinct attached types (rare).
        if (existing is null || (existing.Count == 1 && existing.ContainsKey(key)))
        {
            return this with { Attached = new SingleAttachedDictionary(key, data) };
        }
        var dict = new Dictionary<Type, object>(existing.Count + 1);
        foreach (var kv in existing) dict[kv.Key] = kv.Value;
        dict[key] = data;
        return this with { Attached = dict };
    }

    /// <summary>
    /// Minimal immutable single-entry <see cref="IReadOnlyDictionary{TKey,TValue}"/>
    /// used by <see cref="SetAttached"/> for the common one-attached-value case
    /// (spec 047 §4.4 hot path). Avoids the per-cell <see cref="Dictionary{TKey,TValue}"/>
    /// allocation while remaining a drop-in for every consumer
    /// (<see cref="GetAttached{T}"/>, <see cref="AttachedEqual"/>), which only ever
    /// call <c>Count</c>, <c>TryGetValue</c>, <c>ContainsKey</c>, or enumerate.
    /// </summary>
    internal sealed class SingleAttachedDictionary : IReadOnlyDictionary<Type, object>
    {
        private readonly Type _key;
        private readonly object _value;
        internal SingleAttachedDictionary(Type key, object value)
        {
            _key = key;
            _value = value;
        }
        public object this[Type key] => key == _key ? _value : throw new KeyNotFoundException();
        internal Type SingleKey => _key;
        internal object SingleValue => _value;
        public IEnumerable<Type> Keys { get { yield return _key; } }
        public IEnumerable<object> Values { get { yield return _value; } }
        public int Count => 1;
        public bool ContainsKey(Type key) => key == _key;
        public bool TryGetValue(Type key, out object value)
        {
            if (key == _key)
            {
                value = _value;
                return true;
            }
            value = null!;
            return false;
        }
        public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
        {
            yield return new KeyValuePair<Type, object>(_key, _value);
        }
        global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Convenience: implicitly convert a string to a TextBlockElement.
    /// Allows writing: VStack("Hello", "World") instead of VStack(Text("Hello"), Text("World"))
    /// </summary>
    public static implicit operator Element(string text) => Microsoft.UI.Reactor.Factories.TextBlock(text);

    // ════════════════════════════════════════════════════════════════════════
    //  Fast structural comparison for reconciler short-circuit
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// True if this element exposes any non-null event-handler delegate (OnClick,
    /// OnChanged, etc.). Two roles:
    ///
    /// 1. When the reconciler takes a skip fast-path, it must still refresh the
    ///    control's Tag so the event trampoline dispatches into the current
    ///    render's closure rather than a stale one. Handler-free elements don't
    ///    need the Tag refresh — their controls never fire into Reactor code.
    ///
    /// 2. Callback *presence* is part of the skip invariant: when
    ///    <c>oldEl.HasCallbacks != newEl.HasCallbacks</c>, skipping is unsafe
    ///    because <see cref="ShallowEquals"/> intentionally ignores delegate
    ///    identity — a null→non-null transition wouldn't trigger the lazy-wire
    ///    path in UpdateXxx, so the WinRT event would never be subscribed.
    ///    The skip fast-paths therefore guard on this equality.
    ///
    /// Override on each callback-bearing leaf.
    /// </summary>
    internal virtual bool HasCallbacks => false;

    /// <summary>
    /// Per-type override hook for <see cref="OwnPropsEqual"/> (the dev highlight
    /// overlay's "modified" determination). Returns <c>true</c>/<c>false</c> when
    /// this element type wants to author its own own-props comparison, or
    /// <c>null</c> to defer to the core switch. This lets a control family (e.g.
    /// Docking) declare its own structural-equality semantics without the core
    /// statically naming the subsystem's element types. <paramref name="other"/>
    /// is guaranteed to be the same runtime type as <c>this</c>.
    /// </summary>
    internal virtual bool? OwnPropsEqualOverride(Element other) => null;

    /// <summary>
    /// Returns true if two elements are structurally identical AND the child can be
    /// completely skipped during reconciliation (no need to call Update at all).
    /// This is stricter than ShallowEquals: elements with ThemeBindings or
    /// theme-reactive <see cref="ResourceOverrides"/> (<c>ThemeRefs</c>) must still
    /// go through Update so the themed values can be re-resolved against the current
    /// effective theme, and a change in callback *presence* must run Update so the
    /// lazy-wire path can subscribe to the WinRT event on a null→non-null transition.
    /// IMPORTANT: keep in sync with the ShallowEquals fast-path in Reconciler.Update().
    /// Note: when both elements have a null <see cref="Extensions"/> bucket (spec 047
    /// §4.4 — the common no-extras leaf), ShallowEquals skips the Attached /
    /// ThemeBindings / ContextValues structural compares entirely, since all 14
    /// bucketed fields are provably null on both sides.
    /// </summary>
    /// <remarks>
    /// Theme-sensitivity gate (issue #675, narrowed per #758): the ONLY theme input
    /// that blocks the cheap child-skip arms is a <c>ResourceOverrides.ThemeRefs</c>
    /// override — it resolves to a CONCRETE brush at reconcile
    /// (<c>ApplyResourceOverrides</c> → <c>fe.Resources[...]</c>), which does NOT
    /// re-resolve on an effective-theme change unless <c>Update</c> re-runs, so a
    /// child carrying one must decline the skip and route through <c>Update</c>
    /// (its element-level shallow-skip re-resolves the ThemeRef). <c>ThemeBindings</c>
    /// deliberately do NOT block the skip: <c>.Foreground(Theme.X)</c> compiles to a
    /// <c>{ThemeResource}</c> Style setter that WinUI re-resolves NATIVELY on the
    /// control's effective-theme change (app theme OR an ancestor <c>RequestedTheme</c>)
    /// — self-healing whether or not Reactor re-applies it, so re-running
    /// <c>ApplyThemeBindings</c> on a skipped child is redundant (it re-applies the same
    /// content-addressed cached Style). The theme predicate is SHARED with
    /// <see cref="Core.ChildDiffHints.IsThemeSensitive"/> (the child-diff-hint / container
    /// fast-path gate) so the two arms can never desync.
    /// </remarks>
    internal static bool CanSkipUpdate(Element oldEl, Element newEl)
        => ShallowEquals(oldEl, newEl)
            && !ChildDiffHints.IsThemeSensitive(newEl)
            && oldEl.HasCallbacks == newEl.HasCallbacks;

    /// <summary>
    /// Fast structural comparison that avoids the pitfalls of record Equals
    /// (Dictionary reference equality, Action[] reference equality, delegate equality).
    /// Returns true only when the two elements are provably identical for rendering purposes.
    /// Conservative: returns false for unknown element types.
    /// </summary>
    internal static bool ShallowEquals(Element a, Element b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.GetType() != b.GetType()) return false;
        if (!ModifiersEqual(a.Modifiers, b.Modifiers)) return false;

        // Spec 047 §4.4 fast-path: when neither element carries an Extras bucket,
        // all 14 bucketed fields (Attached, ThemeBindings, ContextValues, …) are
        // provably null on both sides, so the three structural compares below are
        // guaranteed to pass. Skip them (and their null-deref property GETs) for
        // the common no-extras leaf.
        if (!(a.Extensions is null && b.Extensions is null))
        {
            if (!AttachedEqual(a.Attached, b.Attached)) return false;
            if (!ThemeBindingsEqual(a.ThemeBindings, b.ThemeBindings)) return false;
            // Issue #675 — compare ResourceOverrides for symmetry with ThemeBindings: a
            // dropped/changed override while otherwise shallow-equal must decline the skip
            // so ApplyResourceOverrides on the full-Update path strips/re-applies it.
            if (!ResourceOverridesEqual(a.ResourceOverrides, b.ResourceOverrides)) return false;
            if (!ContextValuesEqual(a.ContextValues, b.ContextValues)) return false;
        }

        return (a, b) switch
        {
            (TextBlockElement ta, TextBlockElement tb) =>
                ta.Content == tb.Content
                && ta.FontSize == tb.FontSize
                && ta.Weight == tb.Weight
                && ta.FontStyle == tb.FontStyle
                && ta.HorizontalAlignment == tb.HorizontalAlignment
                && SettersEqual(ta.Setters, tb.Setters),

            // Callbacks (OnClick, OnChanged, etc.) are intentionally not compared:
            // dispatch goes through the Tag trampoline, and the Update.cs skip path
            // refreshes Tag when HasCallbacks is true. So identity of the delegate
            // on the element is irrelevant to dispatch correctness — only presence
            // mattered historically (and presence is still captured by HasCallbacks).
            (ButtonElement ba, ButtonElement bb) =>
                ba.Label == bb.Label
                && ba.IsEnabled == bb.IsEnabled
                // IsDisabledFocusable is a second Button enabled-state input (it coerces
                // IsEnabled=true + Opacity=0.4 in the descriptor). Like the command-derived
                // IsEnabled folded into CommandsEqual (issue #637 M1), an isolated flip must
                // break equality so the descriptor re-applies the focusable-dim vs hard-disabled
                // state — otherwise the live control goes stale on the fast-path skip.
                && ba.IsDisabledFocusable == bb.IsDisabledFocusable
                && ba.ContentElement is null && bb.ContentElement is null
                && CommandBindings.CommandsEqual(ba.Command, bb.Command)
                && SettersEqual(ba.Setters, bb.Setters),

            (HyperlinkButtonElement ha, HyperlinkButtonElement hb) =>
                ha.Content == hb.Content
                && ha.NavigateUri == hb.NavigateUri
                && CommandBindings.CommandsEqual(ha.Command, hb.Command)
                && SettersEqual(ha.Setters, hb.Setters),

            (RepeatButtonElement ra, RepeatButtonElement rb) =>
                ra.Label == rb.Label
                && ra.Delay == rb.Delay
                && ra.Interval == rb.Interval
                && CommandBindings.CommandsEqual(ra.Command, rb.Command)
                && SettersEqual(ra.Setters, rb.Setters),

            (ToggleButtonElement ta, ToggleButtonElement tb) =>
                ta.Label == tb.Label
                && ta.IsChecked == tb.IsChecked
                && CommandBindings.CommandsEqual(ta.Command, tb.Command)
                && SettersEqual(ta.Setters, tb.Setters),

            // issue #153 (L1) — extend the command fast-path arm to Split / ToggleSplit so all six
            // command-capable buttons memoize consistently. Flyout uses reference-equality (matches
            // the descriptor's ElementReferenceComparer); null == null lets flyout-less command split
            // buttons fast-path too.
            (SplitButtonElement sa, SplitButtonElement sb) =>
                sa.Label == sb.Label
                && ReferenceEquals(sa.Flyout, sb.Flyout)
                && CommandBindings.CommandsEqual(sa.Command, sb.Command)
                && SettersEqual(sa.Setters, sb.Setters),

            (ToggleSplitButtonElement tsa, ToggleSplitButtonElement tsb) =>
                tsa.Label == tsb.Label
                && tsa.IsChecked == tsb.IsChecked
                && ReferenceEquals(tsa.Flyout, tsb.Flyout)
                && CommandBindings.CommandsEqual(tsa.Command, tsb.Command)
                && SettersEqual(tsa.Setters, tsb.Setters),

            (SliderElement sa, SliderElement sb) =>
                sa.Value == sb.Value
                && sa.Min == sb.Min
                && sa.Max == sb.Max
                && sa.StepFrequency == sb.StepFrequency
                && sa.Header == sb.Header
                && SettersEqual(sa.Setters, sb.Setters),

            (ToggleSwitchElement ta, ToggleSwitchElement tb) =>
                ta.IsOn == tb.IsOn
                && ta.OnContent == tb.OnContent
                && ta.OffContent == tb.OffContent
                && ta.Header == tb.Header
                && SettersEqual(ta.Setters, tb.Setters),

            (CheckBoxElement ca, CheckBoxElement cb) =>
                ca.IsChecked == cb.IsChecked
                && ca.Label == cb.Label
                && ca.IsThreeState == cb.IsThreeState
                && ca.CheckedState == cb.CheckedState
                && SettersEqual(ca.Setters, cb.Setters),

            (RadioButtonElement ra, RadioButtonElement rb) =>
                ra.Label == rb.Label
                && ra.IsChecked == rb.IsChecked
                && ra.GroupName == rb.GroupName
                && SettersEqual(ra.Setters, rb.Setters),

            (ComboBoxElement ca, ComboBoxElement cb) =>
                ReferenceEquals(ca.Items, cb.Items)
                && ca.SelectedIndex == cb.SelectedIndex
                && ca.PlaceholderText == cb.PlaceholderText
                && ca.Header == cb.Header
                && ca.IsEditable == cb.IsEditable
                && ReferenceEquals(ca.ItemElements, cb.ItemElements)
                && SettersEqual(ca.Setters, cb.Setters),

            (TextBoxElement ta, TextBoxElement tb) =>
                ta.Value == tb.Value
                && ta.PlaceholderText == tb.PlaceholderText
                && ta.Header == tb.Header
                && ta.IsReadOnly == tb.IsReadOnly
                && ta.AcceptsReturn == tb.AcceptsReturn
                && ta.TextWrapping == tb.TextWrapping
                && ta.SelectionStart == tb.SelectionStart
                && ta.SelectionLength == tb.SelectionLength
                && SettersEqual(ta.Setters, tb.Setters),

            (NumberBoxElement na, NumberBoxElement nb) =>
                na.Value == nb.Value
                && na.Minimum == nb.Minimum
                && na.Maximum == nb.Maximum
                && na.SmallChange == nb.SmallChange
                && na.LargeChange == nb.LargeChange
                && na.Header == nb.Header
                && na.PlaceholderText == nb.PlaceholderText
                && na.SpinButtonPlacement == nb.SpinButtonPlacement
                && SettersEqual(na.Setters, nb.Setters),

            (PasswordBoxElement pa, PasswordBoxElement pb) =>
                pa.Password == pb.Password
                && pa.PlaceholderText == pb.PlaceholderText
                && SettersEqual(pa.Setters, pb.Setters),

            (ProgressElement pa, ProgressElement pb) =>
                pa.Value == pb.Value
                && pa.Minimum == pb.Minimum
                && pa.Maximum == pb.Maximum
                && pa.ShowError == pb.ShowError
                && pa.ShowPaused == pb.ShowPaused
                && SettersEqual(pa.Setters, pb.Setters),

            (ProgressRingElement pa, ProgressRingElement pb) =>
                pa.Value == pb.Value
                && pa.Minimum == pb.Minimum
                && pa.Maximum == pb.Maximum
                && pa.IsActive == pb.IsActive
                && SettersEqual(pa.Setters, pb.Setters),

            (ImageElement ia, ImageElement ib) =>
                ia.Source == ib.Source
                && SettersEqual(ia.Setters, ib.Setters),

            (RectangleElement ra, RectangleElement rb) =>
                SettersEqual(ra.Setters, rb.Setters),

            (EllipseElement ea, EllipseElement eb) =>
                SettersEqual(ea.Setters, eb.Setters),

            // Chart primitives — emitted in bulk by D3Charts. Without these arms,
            // every Path/Line in a chart falls through to UpdatePath/UpdateLine on
            // every parent render even when chart data is unchanged, so D3Charts.Brush
            // re-allocations cause every WinUI Path/Line property to be reassigned.
            (PathElement pa, PathElement pb) =>
                string.Equals(pa.PathDataString, pb.PathDataString, StringComparison.Ordinal)
                && (pa.PathDataString is not null || ReferenceEquals(pa.Data, pb.Data))
                && BrushesEqual(pa.Fill, pb.Fill)
                && BrushesEqual(pa.Stroke, pb.Stroke)
                && pa.StrokeThickness == pb.StrokeThickness
                && ReferenceEquals(pa.StrokeDashArray, pb.StrokeDashArray)
                && TransformsEqual(pa.RenderTransform, pb.RenderTransform)
                && pa.Setters.Length == 0 && pb.Setters.Length == 0,

            (LineElement la, LineElement lb) =>
                la.X1 == lb.X1 && la.Y1 == lb.Y1 && la.X2 == lb.X2 && la.Y2 == lb.Y2
                && BrushesEqual(la.Stroke, lb.Stroke)
                && la.StrokeThickness == lb.StrokeThickness
                && la.Setters.Length == 0 && lb.Setters.Length == 0,

            (RichTextBlockElement ra, RichTextBlockElement rb) =>
                ra.Text == rb.Text
                && ra.FontSize == rb.FontSize
                && FontFamiliesEqual(ra.FontFamily, rb.FontFamily)
                && ra.FontWeight == rb.FontWeight
                && ra.FontStyle == rb.FontStyle
                && ra.FontStretch == rb.FontStretch
                && BrushesEqual(ra.Foreground, rb.Foreground)
                && ra.IsTextSelectionEnabled == rb.IsTextSelectionEnabled
                && ra.TextWrapping == rb.TextWrapping
                && ra.MaxLines == rb.MaxLines
                && ra.LineHeight == rb.LineHeight
                && ParagraphsEqual(ra.Paragraphs, rb.Paragraphs)
                && ra.TextAlignment == rb.TextAlignment
                && ra.HorizontalTextAlignment == rb.HorizontalTextAlignment
                && ra.TextTrimming == rb.TextTrimming
                && ra.CharacterSpacing == rb.CharacterSpacing
                && ra.TextDecorations == rb.TextDecorations
                && ra.LineStackingStrategy == rb.LineStackingStrategy
                && ra.TextIndent == rb.TextIndent
                && ra.TextLineBounds == rb.TextLineBounds
                && ra.TextReadingOrder == rb.TextReadingOrder
                && ra.IsTextScaleFactorEnabled == rb.IsTextScaleFactorEnabled
                && ra.IsColorFontEnabled == rb.IsColorFontEnabled
                && ra.OpticalMarginAlignment == rb.OpticalMarginAlignment
                && BrushesEqual(ra.SelectionHighlightColor, rb.SelectionHighlightColor)
                && SettersEqual(ra.Setters, rb.Setters),

            // Container elements: compare own props + children by reference.
            // Same children reference = truly unchanged subtree = safe to skip entirely.
            // Different children reference = fall through to UpdateXxx which recurses.
            (StackElement sa, StackElement sb) =>
                sa.Orientation == sb.Orientation
                && sa.Spacing == sb.Spacing
                && sa.HorizontalAlignment == sb.HorizontalAlignment
                && sa.VerticalAlignment == sb.VerticalAlignment
                && ReferenceEquals(sa.Children, sb.Children)
                && SettersEqual(sa.Setters, sb.Setters),

            (BorderElement ba, BorderElement bb) =>
                BrushesEqual(ba.Background, bb.Background)
                && BrushesEqual(ba.BorderBrush, bb.BorderBrush)
                && ba.CornerRadius == bb.CornerRadius
                && ba.BorderThickness == bb.BorderThickness
                && ReferenceEquals(ba.Child, bb.Child)
                && SettersEqual(ba.Setters, bb.Setters),

            // ItemContainer is the ItemsView item-root wrapper. Selection
            // state is framework-driven (so the Reactor element's
            // IsSelected stays at its declared default across re-renders
            // unless the user explicitly drives it), making this skip
            // path the common case during selection-triggered reconciles.
            (ItemContainerElement ica, ItemContainerElement icb) =>
                ica.IsSelected == icb.IsSelected
                && ReferenceEquals(ica.Child, icb.Child)
                && SettersEqual(ica.Setters, icb.Setters),

            (GridElement ga, GridElement gb) =>
                ga.RowSpacing == gb.RowSpacing
                && ga.ColumnSpacing == gb.ColumnSpacing
                && ReferenceEquals(ga.Definition, gb.Definition)
                && ReferenceEquals(ga.Children, gb.Children)
                && SettersEqual(ga.Setters, gb.Setters),

            (ScrollViewerElement sva, ScrollViewerElement svb) =>
                sva.Orientation == svb.Orientation
                && sva.HorizontalScrollBarVisibility == svb.HorizontalScrollBarVisibility
                && sva.VerticalScrollBarVisibility == svb.VerticalScrollBarVisibility
                && sva.HorizontalScrollMode == svb.HorizontalScrollMode
                && sva.VerticalScrollMode == svb.VerticalScrollMode
                && sva.ZoomMode == svb.ZoomMode
                && ReferenceEquals(sva.Child, svb.Child)
                && SettersEqual(sva.Setters, svb.Setters),

            (ScrollViewElement sva, ScrollViewElement svb) =>
                sva.ContentOrientation == svb.ContentOrientation
                && sva.HorizontalScrollBarVisibility == svb.HorizontalScrollBarVisibility
                && sva.VerticalScrollBarVisibility == svb.VerticalScrollBarVisibility
                && sva.HorizontalScrollMode == svb.HorizontalScrollMode
                && sva.VerticalScrollMode == svb.VerticalScrollMode
                && sva.ZoomMode == svb.ZoomMode
                && sva.MinZoomFactor == svb.MinZoomFactor
                && sva.MaxZoomFactor == svb.MaxZoomFactor
                && sva.HorizontalAnchorRatio == svb.HorizontalAnchorRatio
                && sva.VerticalAnchorRatio == svb.VerticalAnchorRatio
                && ReferenceEquals(sva.Child, svb.Child)
                && SettersEqual(sva.Setters, svb.Setters),

            (FlexElement fa, FlexElement fb) =>
                fa.Direction == fb.Direction
                && fa.JustifyContent == fb.JustifyContent
                && fa.AlignItems == fb.AlignItems
                && fa.AlignContent == fb.AlignContent
                && fa.Wrap == fb.Wrap
                && fa.ColumnGap == fb.ColumnGap
                && fa.RowGap == fb.RowGap
                && fa.FlexPadding == fb.FlexPadding
                && ReferenceEquals(fa.Children, fb.Children)
                && SettersEqual(fa.Setters, fb.Setters),

            (CanvasElement ca, CanvasElement cb) =>
                ca.Width == cb.Width
                && ca.Height == cb.Height
                && BrushesEqual(ca.Background, cb.Background)
                && ReferenceEquals(ca.Children, cb.Children)
                && ca.Setters.Length == 0 && cb.Setters.Length == 0,

            (EmptyElement, EmptyElement) => true,

            // ErrorBoundary contains delegates — always update
            (ErrorBoundaryElement, ErrorBoundaryElement) => false,

            // Conservative: unknown element types always update
            _ => false,
        };
    }

    /// <summary>
    /// Like ShallowEquals but for container types, ignores child/children references.
    /// Returns true when the element's own WinUI-mapped properties are unchanged,
    /// meaning the only reason Update was entered is to recurse into children.
    /// Used by the highlight overlay to avoid marking containers yellow when only
    /// their children changed (the children themselves will be individually captured).
    /// Conservative: returns false for unknown/non-container types (assume props changed).
    /// </summary>
    internal static bool OwnPropsEqual(Element a, Element b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.GetType() != b.GetType()) return false;

        // Per-type override hook: control families (e.g. Docking) declare their
        // own structural-equality semantics without the core statically naming
        // the subsystem's element types.
        if (a.OwnPropsEqualOverride(b) is { } overridden) return overridden;

        return (a, b) switch
        {
            // Container types: same checks as ShallowEquals minus Children/Child refs
            (StackElement sa, StackElement sb) =>
                sa.Orientation == sb.Orientation
                && sa.Spacing == sb.Spacing
                && sa.HorizontalAlignment == sb.HorizontalAlignment
                && sa.VerticalAlignment == sb.VerticalAlignment
                && SettersEqual(sa.Setters, sb.Setters),

            (Core.GridElement ga, Core.GridElement gb) =>
                ga.RowSpacing == gb.RowSpacing
                && ga.ColumnSpacing == gb.ColumnSpacing
                && ReferenceEquals(ga.Definition, gb.Definition)
                && SettersEqual(ga.Setters, gb.Setters),

            (BorderElement ba, BorderElement bb) =>
                BrushesEqual(ba.Background, bb.Background)
                && BrushesEqual(ba.BorderBrush, bb.BorderBrush)
                && ba.CornerRadius == bb.CornerRadius
                && ba.Padding == bb.Padding
                && ba.BorderThickness == bb.BorderThickness
                && SettersEqual(ba.Setters, bb.Setters),

            // ItemContainer: own props (excluding Child) match when
            // IsSelected and Setters agree. The reconcile-highlight gate
            // checks this to avoid marking every realized item yellow
            // when the only changes are inside the user-supplied subtree.
            (ItemContainerElement ica, ItemContainerElement icb) =>
                ica.IsSelected == icb.IsSelected
                && SettersEqual(ica.Setters, icb.Setters),

            (ScrollViewerElement sva, ScrollViewerElement svb) =>
                sva.Orientation == svb.Orientation
                && sva.HorizontalScrollBarVisibility == svb.HorizontalScrollBarVisibility
                && sva.VerticalScrollBarVisibility == svb.VerticalScrollBarVisibility
                && sva.HorizontalScrollMode == svb.HorizontalScrollMode
                && sva.VerticalScrollMode == svb.VerticalScrollMode
                && sva.ZoomMode == svb.ZoomMode
                && SettersEqual(sva.Setters, svb.Setters),

            (ScrollViewElement sva, ScrollViewElement svb) =>
                sva.ContentOrientation == svb.ContentOrientation
                && sva.HorizontalScrollBarVisibility == svb.HorizontalScrollBarVisibility
                && sva.VerticalScrollBarVisibility == svb.VerticalScrollBarVisibility
                && sva.HorizontalScrollMode == svb.HorizontalScrollMode
                && sva.VerticalScrollMode == svb.VerticalScrollMode
                && sva.ZoomMode == svb.ZoomMode
                && sva.MinZoomFactor == svb.MinZoomFactor
                && sva.MaxZoomFactor == svb.MaxZoomFactor
                && sva.HorizontalAnchorRatio == svb.HorizontalAnchorRatio
                && sva.VerticalAnchorRatio == svb.VerticalAnchorRatio
                && SettersEqual(sva.Setters, svb.Setters),

            (FlexElement fa, FlexElement fb) =>
                fa.Direction == fb.Direction
                && fa.JustifyContent == fb.JustifyContent
                && fa.AlignItems == fb.AlignItems
                && fa.AlignContent == fb.AlignContent
                && fa.Wrap == fb.Wrap
                && fa.ColumnGap == fb.ColumnGap
                && fa.RowGap == fb.RowGap
                && fa.FlexPadding == fb.FlexPadding
                && SettersEqual(fa.Setters, fb.Setters),

            (CanvasElement ca, CanvasElement cb) =>
                SettersEqual(ca.Setters, cb.Setters),

            (WrapGridElement wa, WrapGridElement wb) =>
                wa.Orientation == wb.Orientation
                && wa.ItemWidth == wb.ItemWidth
                && wa.ItemHeight == wb.ItemHeight
                && wa.MaximumRowsOrColumns == wb.MaximumRowsOrColumns
                && SettersEqual(wa.Setters, wb.Setters),

            (RelativePanelElement ra, RelativePanelElement rb) =>
                SettersEqual(ra.Setters, rb.Setters),

            (ViewboxElement va, ViewboxElement vb) =>
                SettersEqual(va.Setters, vb.Setters),

            // Structural wrappers that only contain children
            (NavigationHostElement, NavigationHostElement) => true,
            (CommandHostElement, CommandHostElement) => true,
            (PopupElement pa, PopupElement pb) =>
                pa.IsOpen == pb.IsOpen
                && pa.IsLightDismissEnabled == pb.IsLightDismissEnabled,

            // TitleBar: own-props check (ignore Content/RightHeader slots which
            // recurse as children). Without this, TitleBar flashes yellow on
            // every reconcile even when only descendants changed.
            (TitleBarElement ta, TitleBarElement tb) =>
                ta.Title == tb.Title
                && ta.Subtitle == tb.Subtitle
                && ta.IsBackButtonVisible == tb.IsBackButtonVisible
                && ta.IsBackButtonEnabled == tb.IsBackButtonEnabled
                && ta.IsPaneToggleButtonVisible == tb.IsPaneToggleButtonVisible
                && SettersEqual(ta.Setters, tb.Setters),

            // Pure composition wrappers — they never write their own WinUI
            // properties; their rendered output is diffed separately. Returning
            // true here prevents the overlay from flashing the entire content
            // block every time the component re-renders.
            (ComponentElement, ComponentElement) => true,
            (FuncElement, FuncElement) => true,
            (MemoElement, MemoElement) => true,
            (ModifiedElement, ModifiedElement) => true,
            (GroupElement, GroupElement) => true,
            (ErrorBoundaryElement, ErrorBoundaryElement) => true,

            // MenuFlyout attaches a flyout to its Target but doesn't have its
            // own WinUI props that change across renders.
            (MenuFlyoutElement, MenuFlyoutElement) => true,
            (ContentFlyoutElement, ContentFlyoutElement) => true,
            (MenuFlyoutContentElement, MenuFlyoutContentElement) => true,
            (FlyoutElement, FlyoutElement) => true,

            // Collection-style elements: compare own props only (SelectedIndex,
            // mode flags, header). Item/children arrays are compared separately
            // in ShallowEquals via ReferenceEquals — a fresh items array does
            // NOT mean own props changed, so the highlight overlay should not
            // light up the ComboBox/ListView/etc. when only the authored items
            // projection allocated a new array.
            (ComboBoxElement ca, ComboBoxElement cb) =>
                ca.SelectedIndex == cb.SelectedIndex
                && ca.PlaceholderText == cb.PlaceholderText
                && ca.Header == cb.Header
                && ca.IsEditable == cb.IsEditable
                && SettersEqual(ca.Setters, cb.Setters),

            (ListViewElement la, ListViewElement lb) =>
                la.SelectedIndex == lb.SelectedIndex
                && la.SelectionMode == lb.SelectionMode
                && la.Header == lb.Header
                && SettersEqual(la.Setters, lb.Setters),

            (GridViewElement ga, GridViewElement gb) =>
                ga.SelectedIndex == gb.SelectedIndex
                && ga.SelectionMode == gb.SelectionMode
                && ga.Header == gb.Header
                && SettersEqual(ga.Setters, gb.Setters),

            (FlipViewElement fa, FlipViewElement fb) =>
                fa.SelectedIndex == fb.SelectedIndex
                && SettersEqual(fa.Setters, fb.Setters),

            (PivotElement pa, PivotElement pb) =>
                pa.SelectedIndex == pb.SelectedIndex
                && pa.Title == pb.Title
                && SettersEqual(pa.Setters, pb.Setters),

            (TabViewElement ta, TabViewElement tb) =>
                ta.SelectedIndex == tb.SelectedIndex
                && ta.IsAddTabButtonVisible == tb.IsAddTabButtonVisible
                && SettersEqual(ta.Setters, tb.Setters),

            (TreeViewElement ta, TreeViewElement tb) =>
                ta.SelectionMode == tb.SelectionMode
                && ta.CanDragItems == tb.CanDragItems
                && ta.AllowDrop == tb.AllowDrop
                && ta.CanReorderItems == tb.CanReorderItems
                && SettersEqual(ta.Setters, tb.Setters),

            (SelectorBarElement sa, SelectorBarElement sb) =>
                sa.SelectedIndex == sb.SelectedIndex
                && SettersEqual(sa.Setters, sb.Setters),

            (ListBoxElement la, ListBoxElement lb) =>
                la.SelectedIndex == lb.SelectedIndex
                && SettersEqual(la.Setters, lb.Setters),

            (RadioButtonsElement ra, RadioButtonsElement rb) =>
                ra.SelectedIndex == rb.SelectedIndex
                && ra.Header == rb.Header
                && SettersEqual(ra.Setters, rb.Setters),

            (BreadcrumbBarElement ba, BreadcrumbBarElement bb) =>
                SettersEqual(ba.Setters, bb.Setters),

            // Templated (data-driven) collections: own props are the WinUI
            // properties UpdateTemplatedXxx writes back. Items + ViewBuilder
            // are not own props — they drive child reconcile but don't write
            // properties on the parent control. Without this case, the typed
            // ListView<T>/GridView<T>/FlipView<T> falls through to false and
            // the highlight overlay flashes the whole list on every parent
            // re-render (because OwnPropsEqual returning false is the gate
            // for ReconcileHighlightOverlay's "modified" tag).
            (TemplatedListElementBase ta, TemplatedListElementBase tb) =>
                ta.GetSelectedIndex() == tb.GetSelectedIndex()
                && ta.GetSelectionMode() == tb.GetSelectionMode()
                && ta.GetHeader() == tb.GetHeader()
                && ta.GetIsItemClickEnabled() == tb.GetIsItemClickEnabled()
                && !ta.HasSetters && !tb.HasSetters,

            // Templated hierarchical TreeView — same rationale as the
            // templated lists above: Items/selectors/ViewBuilder are factory
            // inputs that drive child reconcile, not parent-control props.
            (TemplatedTreeViewElementBase tta, TemplatedTreeViewElementBase ttb) =>
                tta.GetSelectionMode() == ttb.GetSelectionMode()
                && tta.GetCanDragItems() == ttb.GetCanDragItems()
                && tta.GetAllowDrop() == ttb.GetAllowDrop()
                && tta.GetCanReorderItems() == ttb.GetCanReorderItems()
                && !tta.HasSetters && !ttb.HasSetters,

            // Lazy (virtualized) stacks: same rationale — Items/ViewBuilder
            // are factory inputs, not control properties.
            (LazyStackElementBase la, LazyStackElementBase lb) =>
                la.Orientation == lb.Orientation
                && la.Spacing == lb.Spacing
                && la.EstimatedItemSize == lb.EstimatedItemSize
                && SettersEqual(la.ScrollViewerSetters, lb.ScrollViewerSetters)
                && SettersEqual(la.RepeaterSetters, lb.RepeaterSetters),

            // Non-container / leaf types: return false → always captured
            _ => false,
        };
    }

    /// <summary>
    /// Structural comparison of RichTextParagraph arrays.
    /// Compares each paragraph's inlines using record equality.
    /// </summary>
    private static bool ParagraphsEqual(RichTextParagraph[]? a, RichTextParagraph[]? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!ParagraphEqual(a[i], b[i])) return false;
        }
        return true;
    }

    /// <summary>
    /// Structural comparison of a single RichTextParagraph (inline-by-inline record equality).
    /// </summary>
    internal static bool ParagraphEqual(RichTextParagraph a, RichTextParagraph b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (!ParagraphPropertiesEqual(a, b)) return false;
        var ai = a.Inlines;
        var bi = b.Inlines;
        if (ai.Length != bi.Length) return false;
        for (int j = 0; j < ai.Length; j++)
        {
            if (!RichTextInlineEqual(ai[j], bi[j])) return false;
        }
        return true;
    }

    private static bool ParagraphPropertiesEqual(RichTextParagraph a, RichTextParagraph b)
        => a.Margin == b.Margin
            && a.TextIndent == b.TextIndent
            && a.TextAlignment == b.TextAlignment
            && a.HorizontalTextAlignment == b.HorizontalTextAlignment
            && a.LineHeight == b.LineHeight
            && a.LineStackingStrategy == b.LineStackingStrategy
            && a.FontSize == b.FontSize
            && string.Equals(a.FontFamily, b.FontFamily, StringComparison.Ordinal)
            && a.FontWeight == b.FontWeight
            && a.FontStyle == b.FontStyle
            && a.FontStretch == b.FontStretch
            && BrushesEqual(a.Foreground, b.Foreground)
            && a.CharacterSpacing == b.CharacterSpacing
            && a.TextDecorations == b.TextDecorations
            && a.IsTextScaleFactorEnabled == b.IsTextScaleFactorEnabled
            && string.Equals(a.Language, b.Language, StringComparison.Ordinal);

    private static bool RichTextInlineTextPropertiesEqual(RichTextInline a, RichTextInline b)
        => a.FontSize == b.FontSize
            && string.Equals(a.FontFamily, b.FontFamily, StringComparison.Ordinal)
            && a.FontWeight == b.FontWeight
            && a.FontStyle == b.FontStyle
            && a.FontStretch == b.FontStretch
            && BrushesEqual(a.Foreground, b.Foreground)
            && a.CharacterSpacing == b.CharacterSpacing
            && a.TextDecorations == b.TextDecorations
            && a.IsTextScaleFactorEnabled == b.IsTextScaleFactorEnabled
            && string.Equals(a.Language, b.Language, StringComparison.Ordinal);

    private static bool RichTextInlineEqual(RichTextInline a, RichTextInline b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.GetType() != b.GetType()) return false;
        if (!RichTextInlineTextPropertiesEqual(a, b)) return false;

        return (a, b) switch
        {
            (RichTextRun ra, RichTextRun rb) =>
                ra.Text == rb.Text
                && ra.IsBold == rb.IsBold
                && ra.IsItalic == rb.IsItalic
                && ra.IsStrikethrough == rb.IsStrikethrough
                && ra.FlowDirection == rb.FlowDirection,

            (RichTextHyperlink ha, RichTextHyperlink hb) =>
                ha.Text == hb.Text
                && ha.NavigateUri == hb.NavigateUri
                && ReferenceEquals(ha.OnClick, hb.OnClick)
                && ha.UnderlineStyle == hb.UnderlineStyle
                && ha.IsTabStop == hb.IsTabStop
                && ha.TabIndex == hb.TabIndex,

            (RichTextLineBreak, RichTextLineBreak) => true,

            (RichTextInlineUIContainer ia, RichTextInlineUIContainer ib) =>
                ReferenceEquals(ia.Child, ib.Child)
                && ReferenceEquals(ia.Factory, ib.Factory),

            _ => a.Equals(b),
        };
    }

    /// <summary>
    /// Structural brush comparison. BrushHelper.Parse caches the parsed Color
    /// but returns a fresh SolidColorBrush instance on every call (Brushes have
    /// thread affinity), so ReferenceEquals always fails for ".Background("#x")"
    /// style fluent chains. Unwrap the underlying Color for the common
    /// SolidColorBrush case and fall back to ReferenceEquals for everything else.
    /// </summary>
    private static bool BrushesEqual(Brush? a, Brush? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a is SolidColorBrush sa && b is SolidColorBrush sb)
            return sa.Color == sb.Color && sa.Opacity == sb.Opacity;
        return false;
    }

    /// <summary>
    /// Structural transform comparison. D3PathTranslated allocates a fresh
    /// TranslateTransform on every render even when X/Y match, so reference
    /// equality always fails for the common chart case. Unwrap TranslateTransform
    /// and fall back to ReferenceEquals for everything else.
    /// </summary>
    private static bool TransformsEqual(Transform? a, Transform? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a is TranslateTransform ta && b is TranslateTransform tb)
            return ta.X == tb.X && ta.Y == tb.Y;
        return false;
    }

    private static bool FontFamiliesEqual(Microsoft.UI.Xaml.Media.FontFamily? a, Microsoft.UI.Xaml.Media.FontFamily? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return a.Source == b.Source;
    }

    /// <summary>
    /// Conservative equality for two <c>Setters</c> arrays (the imperative
    /// <c>.Set(x =&gt; …)</c> escape hatch). Returns true only when the arrays are the
    /// <em>same instance</em> (a memoized/unchanged element) or <em>both empty</em>
    /// (no imperative writes to apply). It deliberately does <b>not</b> compare
    /// element-wise by delegate identity.
    /// <para>
    /// Why not element-wise identity: a setter is an apply-time imperative write
    /// whose effect can read mutable external state (statics, singletons, captured
    /// mutable objects). Non-capturing lambdas and method groups are cached by the
    /// C# compiler to a single static delegate, so identity-equality across renders
    /// does <b>not</b> imply the write is unnecessary. Skipping a reference-stable
    /// setter would strand a stale value on the control — a regression versus the
    /// documented contract that <c>.Set</c> re-applies on every update. Setters
    /// therefore keep the element on the Update path whenever any are present
    /// (mirroring the <c>!HasSetters</c> guards used by the templated/lazy element
    /// arms). Handler modifiers differ and <em>are</em> safe to compare by identity
    /// (FLAGSHIP-1): they dispatch later through
    /// <c>ModifierEventHandlerState.Current*</c> and read fresh state at fire time,
    /// rather than being re-applied imperatively during Update.
    /// </para>
    /// </summary>
    internal static bool SettersEqual<T>(T[]? a, T[]? b) where T : class
    {
        if (ReferenceEquals(a, b)) return true;
        return (a is null || a.Length == 0) && (b is null || b.Length == 0);
    }

    /// <summary>
    /// Compare two ElementModifiers for rendering equivalence.
    /// Brushes and FontFamily are compared structurally because fluent helpers
    /// (<c>.Background("#color")</c>, <c>.FontFamily("Segoe UI")</c>) allocate
    /// fresh instances on every render even when the underlying values match.
    /// Ignores OnMountAction (fires once at mount, gated on oldM is null, so a later
    /// change is inert), but compares OnUnmountAction by reference — the latest
    /// teardown is re-registered on every Update and fires at unmount, so a changed
    /// teardown closure must decline the skip.
    /// </summary>
    internal static bool ModifiersEqual(ElementModifiers? a, ElementModifiers? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        // #159 — hoist the Layout/Visual sub-record reads once instead of
        // dereferencing a.Layout?.X / a.Visual?.X ~20 times through the shim
        // properties. A null bucket is normalized to a shared empty sentinel so
        // the field-level semantics match the historical per-shim comparison
        // (a null Layout reads as all-null fields).
        var al = a.Layout ?? _emptyLayout;
        var bl = b.Layout ?? _emptyLayout;
        // LayoutModifiers is entirely value-typed, so the synthesized record
        // value-equality is exact — and also covers the logical-inset /
        // RequestedTheme fields an explicit field list historically forgot,
        // keeping the skip path faithful to a full Update.
        if (!ReferenceEquals(al, bl) && !al.Equals(bl)) return false;

        // Visual holds Brush fields that must compare by value (Color/Opacity),
        // not by record reference-equality, so it is compared field-by-field.
        if (!VisualModifiersEqual(a.Visual, b.Visual)) return false;

        return a.IsEnabled == b.IsEnabled
            && a.ElementSoundMode == b.ElementSoundMode
            && a.ToolTip == b.ToolTip
            && a.AutomationName == b.AutomationName
            && a.AutomationId == b.AutomationId
            && a.FontSize == b.FontSize
            && a.FontWeight == b.FontWeight
            && FontFamiliesEqual(a.FontFamily, b.FontFamily)
            // FLAGSHIP-1 — modifier event handlers dispatch through the
            // reconciler's per-element ModifierEventHandlerState.Current* fields,
            // which are refreshed ONLY on the non-skip Update path
            // (ApplyEventHandlers). Skipping is therefore safe exactly when every
            // callback-bearing slot is reference-equal: the stale Current* IS the
            // new delegate, so dispatch is identical. Reference-stable handlers
            // (non-capturing lambdas, method groups, memoized closures) now take
            // the skip path; per-render capturing closures still force Update so
            // their fresh captures are wired. This replaces the old blanket
            // "any-handler-present ⇒ never skip" rule and also closes the latent
            // hole where non-listed routed handlers (PointerEntered, GotFocus, …)
            // were ignored entirely. Gesture / drag-drop slots are deliberately
            // NOT compared here (see ModifierCallbacksEqual) so behavior matches
            // the historical diff exactly.
            && ModifierCallbacksEqual(a, b)
            // Skip RichToolTip, AttachedFlyout, ContextFlyout — rare, conservative false
            && a.RichToolTip is null && b.RichToolTip is null
            && a.AttachedFlyout is null && b.AttachedFlyout is null
            && a.ContextFlyout is null && b.ContextFlyout is null
            // Accessibility Tier 1
            && a.HeadingLevel == b.HeadingLevel
            && a.IsDragRegion == b.IsDragRegion
            && a.IsTabStop == b.IsTabStop
            && a.IsHitTestVisible == b.IsHitTestVisible
            && a.TabIndex == b.TabIndex
            && a.AccessKey == b.AccessKey
            && ReferenceEquals(a.XYFocusUpRef, b.XYFocusUpRef)
            && ReferenceEquals(a.XYFocusDownRef, b.XYFocusDownRef)
            && ReferenceEquals(a.XYFocusLeftRef, b.XYFocusLeftRef)
            && ReferenceEquals(a.XYFocusRightRef, b.XYFocusRightRef)
            // Imperative ref slot (.Ref). A ref-only change (add/remove/swap)
            // must force Update so ApplyModifiers clears the old cell and sets the
            // new one — otherwise the shallow-skip path strands a stale ElementRef.
            && ReferenceEquals(a.Ref, b.Ref)
            // Imperative teardown slot (.OnUnmount). ApplyModifiers re-registers the
            // latest OnUnmountAction (Reconciler._onUnmountActions) on every non-skip
            // Update, and that captured delegate is what fires at unmount — so a
            // changed teardown closure must decline the skip or the stale (first-render)
            // action is stranded and runs in place of the current one. Unlike
            // OnMountAction (fired once at mount, gated on oldM is null, so a later
            // change is inert and is intentionally ignored), OnUnmountAction has live
            // update-path semantics. ReferenceEquals mirrors the handler/.Ref treatment:
            // both null ⇒ equal (no teardown either render ⇒ zero skip-rate cost for
            // plain leaves), a reference-stable teardown still skips, a fresh closure
            // forces Update so the latest teardown is registered.
            && ReferenceEquals(a.OnUnmountAction, b.OnUnmountAction)
            // Accessibility Tier 2/3. AccessibilityModifiers is a record of
            // scalar/string fields, but every fluent helper (.AccessibilityView,
            // .LiveRegion, .ItemStatus, …) allocates a fresh instance per render
            // — so reference equality always fails for elements that set any
            // accessibility modifier, even when the values are unchanged.
            // Falsely missing this match cascades into the reconcile-highlight
            // overlay, which paints those elements as "modified" every render.
            // Use record value-equality instead.
            && AccessibilityEqual(a.Accessibility, b.Accessibility);
    }

    // Shared empty buckets so a null Layout/Visual can be compared field-for-field
    // against a set one without allocating (and without 20+ null-conditional shims).
    private static readonly LayoutModifiers _emptyLayout = new();
    private static readonly VisualModifiers _emptyVisual = new();

    /// <summary>
    /// Compare the Visual buckets by value. Brush fields (Background / Foreground /
    /// BorderBrush) are compared with <see cref="BrushesEqual"/> (Color + Opacity)
    /// because fluent helpers allocate a fresh <see cref="SolidColorBrush"/> per
    /// render; everything else is a value type compared with <c>==</c>.
    /// </summary>
    private static bool VisualModifiersEqual(VisualModifiers? a, VisualModifiers? b)
    {
        if (ReferenceEquals(a, b)) return true;
        a ??= _emptyVisual;
        b ??= _emptyVisual;
        return a.Opacity == b.Opacity
            && a.CornerRadius == b.CornerRadius
            && a.BorderThickness == b.BorderThickness
            && a.Scale == b.Scale
            && a.Rotation == b.Rotation
            && a.Translation == b.Translation
            && a.CenterPoint == b.CenterPoint
            && BrushesEqual(a.Background, b.Background)
            && BrushesEqual(a.Foreground, b.Foreground)
            && BrushesEqual(a.BorderBrush, b.BorderBrush);
    }

    /// <summary>
    /// FLAGSHIP-1 — true when every <b>routed-input</b> event-handler slot is
    /// reference-equal between <paramref name="a"/> and <paramref name="b"/>.
    /// These 21 handlers dispatch through the reconciler's
    /// <c>ModifierEventHandlerState.Current*</c> fields, refreshed only on the
    /// non-skip Update path, so the skip path is sound iff the delegate identity
    /// is unchanged. Reference equality (not presence) is the safe predicate:
    /// a stale Current* that equals the new delegate dispatches identically,
    /// while a freshly captured closure forces Update so its captures are wired.
    ///
    /// The gesture (Pan/Pinch/Rotate/LongPress) and drag-drop (DragSource/
    /// DropTarget) slots are deliberately NOT compared here. They dispatch through
    /// separate per-element gesture/drag state, and the historical diff never
    /// compared them — so a per-render gesture closure stays skip-eligible exactly
    /// as before. Comparing them would force Update where the framework previously
    /// skipped, re-arming an in-flight gesture mid-interaction (e.g. re-registering
    /// a long-press handler between its Began and Ended phases, so the released
    /// callback fires against a refreshed closure and double-dispatches). Excluding
    /// them keeps the skip-path perf lever intact (grid cells use only routed
    /// handlers). The latent staleness this creates — a skipped element keeping a
    /// previous-render gesture/drag closure — is closed on the skip path itself by
    /// <see cref="Reconciler.RefreshGestureDragStateOnSkip"/> (#721), which refreshes
    /// the cached dispatch closures WITHOUT re-subscribing the trampolines, so the
    /// latest closure fires while no in-flight gesture is re-armed.
    /// </summary>
    private static bool ModifierCallbacksEqual(ElementModifiers a, ElementModifiers b)
    {
        return ReferenceEquals(a.OnSizeChanged, b.OnSizeChanged)
            && ReferenceEquals(a.OnPointerPressed, b.OnPointerPressed)
            && ReferenceEquals(a.OnPointerMoved, b.OnPointerMoved)
            && ReferenceEquals(a.OnPointerReleased, b.OnPointerReleased)
            && ReferenceEquals(a.OnPointerEntered, b.OnPointerEntered)
            && ReferenceEquals(a.OnPointerExited, b.OnPointerExited)
            && ReferenceEquals(a.OnPointerCanceled, b.OnPointerCanceled)
            && ReferenceEquals(a.OnPointerCaptureLost, b.OnPointerCaptureLost)
            && ReferenceEquals(a.OnPointerWheelChanged, b.OnPointerWheelChanged)
            && ReferenceEquals(a.OnTapped, b.OnTapped)
            && ReferenceEquals(a.OnDoubleTapped, b.OnDoubleTapped)
            && ReferenceEquals(a.OnRightTapped, b.OnRightTapped)
            && ReferenceEquals(a.OnHolding, b.OnHolding)
            && ReferenceEquals(a.OnKeyDown, b.OnKeyDown)
            && ReferenceEquals(a.OnKeyUp, b.OnKeyUp)
            && ReferenceEquals(a.OnPreviewKeyDown, b.OnPreviewKeyDown)
            && ReferenceEquals(a.OnPreviewKeyUp, b.OnPreviewKeyUp)
            && ReferenceEquals(a.OnCharacterReceived, b.OnCharacterReceived)
            && ReferenceEquals(a.OnGotFocus, b.OnGotFocus)
            && ReferenceEquals(a.OnLostFocus, b.OnLostFocus)
            && ReferenceEquals(a.OnAccessKeyDisplayRequested, b.OnAccessKeyDisplayRequested);
    }

    private static bool AccessibilityEqual(AccessibilityModifiers? a, AccessibilityModifiers? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        return a.Equals(b);
    }

    /// <summary>
    /// Compare two Attached property dictionaries by content.
    /// Common case: both have a single GridAttached entry (a record with structural equality).
    /// </summary>
    internal static bool AttachedEqual(IReadOnlyDictionary<Type, object>? a, IReadOnlyDictionary<Type, object>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;

        // #155 hot path — one attached value per element (a single .Grid per cell,
        // re-applied each render ⇒ SingleAttachedDictionary on both sides). Compare
        // the lone slot directly: `foreach` over the IReadOnlyDictionary would
        // allocate an IEnumerator<KeyValuePair> every diff (SingleAttachedDictionary
        // enumerates via `yield`), reintroducing the per-cell allocation #155 exists
        // to remove. Count is already known equal, so a single TryGetValue on the
        // other side is a complete comparison.
        if (a is SingleAttachedDictionary sa)
            return b.TryGetValue(sa.SingleKey, out var sav) && Equals(sa.SingleValue, sav);
        if (b is SingleAttachedDictionary sb)
            return a.TryGetValue(sb.SingleKey, out var sbv) && Equals(sb.SingleValue, sbv);

        foreach (var (key, valA) in a)
        {
            if (!b.TryGetValue(key, out var valB)) return false;
            if (!Equals(valA, valB)) return false; // GridAttached is a record — Equals works
        }
        return true;
    }

    internal static bool ThemeBindingsEqual(IReadOnlyDictionary<string, ThemeRef>? a, IReadOnlyDictionary<string, ThemeRef>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;

        foreach (var (key, valA) in a)
        {
            if (!b.TryGetValue(key, out var valB)) return false;
            if (valA.ResourceKey != valB.ResourceKey) return false;
        }
        return true;
    }

    internal static bool ContextValuesEqual(IReadOnlyDictionary<ContextBase, object?>? a, IReadOnlyDictionary<ContextBase, object?>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;

        foreach (var (key, valA) in a)
        {
            if (!b.TryGetValue(key, out var valB)) return false;
            if (!Equals(valA, valB)) return false;
        }
        return true;
    }

    // Issue #675 — structural equality for per-control ResourceOverrides (lightweight
    // styling). ShallowEquals must compare these for the SAME reason it compares
    // ThemeBindings (line ~417): without it, an element that DROPS or CHANGES a
    // ResourceOverrides entry while otherwise shallow-equal would be skipped and the
    // stale resolved brush (or stale literal) would survive in fe.Resources[key] — the
    // remove/change side of the contract. Returning false here routes the element through
    // full Update, where ApplyResourceOverrides(old, new) strips the dropped managed key
    // and applies the new value. This is symmetric with the ThemeBindings transition-away
    // handling (ShallowEquals catches it + ClearThemeBindings on the Update path). Gated
    // behind the no-extras fast-path in ShallowEquals, so override-free cells pay nothing.
    // The CanSkipUpdate ThemeRefs gate is still required for the STEADY-STATE case (same
    // overrides, effective theme changed → ShallowEquals stays true → the gate forces the
    // re-resolve); the two are complementary.
    internal static bool ResourceOverridesEqual(
        Microsoft.UI.Reactor.Elements.ResourceOverrides? a,
        Microsoft.UI.Reactor.Elements.ResourceOverrides? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        // Literals: compare by VALUE. Brushes are re-parsed to fresh instances each render
        // (e.g. .Set("X", "#0078D4")), so reference compare would false-decline the skip for
        // an UNCHANGED literal-override cell — use BrushesEqual; other literal kinds (double,
        // CornerRadius) are value types whose Equals is correct.
        var la = a.Literals;
        var lb = b.Literals;
        if (la.Count != lb.Count) return false;
        foreach (var (key, valA) in la)
        {
            if (!lb.TryGetValue(key, out var valB)) return false;
            if (!ResourceLiteralEqual(valA, valB)) return false;
        }

        // ThemeRefs: compare by key + ResourceKey (same semantics as ThemeBindingsEqual).
        var ta = a.ThemeRefs;
        var tb = b.ThemeRefs;
        if (ta.Count != tb.Count) return false;
        foreach (var (key, valA) in ta)
        {
            if (!tb.TryGetValue(key, out var valB)) return false;
            if (valA.ResourceKey != valB.ResourceKey) return false;
        }
        return true;
    }

    private static bool ResourceLiteralEqual(object? a, object? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a is Brush ba && b is Brush bb) return BrushesEqual(ba, bb);
        return a.Equals(b);
    }
}

/// <summary>
/// An element that renders nothing (used for conditional rendering).
/// </summary>
public record EmptyElement : Element
{
    public static readonly EmptyElement Instance = new();
}

/// <summary>
/// A transparent grouping element (like React's Fragment). Does not introduce
/// any layout container — its children are flattened into the parent.
/// Produced by <c>ForEach</c> and <c>Group()</c> in the DSL.
/// </summary>
public record GroupElement(Element[] Children) : Element;

/// <summary>
/// Catches render errors in its child subtree and displays fallback UI.
/// Like React's ErrorBoundary — catches errors during rendering, not event handlers.
/// When the ErrorBoundary re-renders, it retries the child (error recovery).
/// </summary>
public record ErrorBoundaryElement(Element Child, Func<Exception, Element> Fallback) : Element;

/// <summary>
/// Wraps any element with layout modifiers (margin, alignment, size, etc.).
/// Kept for backward compatibility. New code stores modifiers inline on Element.Modifiers.
/// </summary>
public record ModifiedElement(Element Inner, ElementModifiers WrappedModifiers) : Element;

/// <summary>
/// Wraps a Component class so it can participate in the element tree.
/// Created automatically by Component&lt;T&gt;() factory method.
/// </summary>
public record ComponentElement(
    [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    Type ComponentType,
    object? Props = null) : Element
{
    // Factory creates the component instance without reflection. Stored as a field
    // so it does not participate in record equality (two ComponentElements for the
    // same Type/Props are equal regardless of factory identity).
    internal Func<Component>? _factory;

    internal Component CreateInstance() =>
        _factory is not null ? _factory() : (Component)Activator.CreateInstance(ComponentType)!;
}

/// <summary>
/// Strongly-typed <see cref="ComponentElement"/> that exposes <see cref="Props"/>
/// as <typeparamref name="TProps"/> instead of <c>object?</c>, so callers can use
/// a record <c>with</c>-expression to produce a modified copy of the element
/// with updated props:
/// <code>
/// var grid = DataGrid&lt;Foo&gt;(source, columns);
/// var taller = grid with { Props = grid.Props with { RowHeight = 60 } };
/// </code>
/// The typed <see cref="Props"/> is a thin view over the base
/// <see cref="ComponentElement.Props"/> slot — there is no second storage field,
/// so the reconciler (which reads <c>base.Props</c>) always sees the same value
/// as the typed accessor on the cloned record.
/// </summary>
public record ComponentElement<TProps> : ComponentElement
{
    public ComponentElement(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
        Type ComponentType,
        TProps Props) : base(ComponentType, Props) { }

    public new TProps Props
    {
        get => (TProps)base.Props!;
        init => base.Props = value;
    }
}

/// <summary>
/// A component defined inline via a render function (like a React function component).
/// </summary>
public record FuncElement(Func<RenderContext, Element> RenderFunc) : Element;

/// <summary>
/// A memoized function component. Skips re-render when Dependencies haven't changed.
/// null Dependencies = render once on mount + self-triggered state changes only.
/// </summary>
public record MemoElement(Func<RenderContext, Element> RenderFunc, object?[]? Dependencies = null) : Element;

/// <summary>
/// Issue #327 — an opt-in, typed keyed memo wrapper produced by the
/// <c>Memo(key, factory)</c> factory (see <see cref="Microsoft.UI.Reactor.Factories"/>).
/// The author asserts <see cref="Factory"/> is a <b>pure function of <see cref="MemoKey"/></b>:
/// when a virtualized <see cref="ElementFactory{T}"/> recycles a container and asks for the
/// same key again, the factory-scoped LRU returns the previously-built inner
/// <see cref="Element"/> instance unchanged instead of rebuilding+diffing the subtree.
/// Returning the <em>same</em> instance lets <c>Element.ShallowEquals</c> hit its
/// <c>ReferenceEquals</c> fast-path so the per-row reconcile descent is skipped entirely.
///
/// <para><b>Purity contract.</b> The key must capture <em>every</em> input the factory reads.
/// Closing over unkeyed mutable state (a <c>UseState</c> cell, an external counter, the theme)
/// without folding it into the key will serve stale content — that is the author's
/// responsibility. Widen the key to a tuple (e.g. <c>Memo((item.Id, isSelected), …)</c>) to
/// capture extra inputs. <see cref="MemoKey"/> is compared with
/// <see cref="object.Equals(object)"/> / <see cref="object.GetHashCode"/>, so value keys
/// (ints, strings, records, value tuples) dedupe by value — this is what makes the int-index
/// VirtualList path hit the cache where the reference-identity <c>_viewBuilderCache</c> cannot.</para>
///
/// <para><b>Used outside a virtualized factory</b> (e.g. a plain <c>VStack</c> child), the
/// reconciler treats it as a <em>transparent, keyed</em> wrapper: a re-render with the same
/// <see cref="MemoKey"/> is a no-op (by the purity contract the factory output is identical to
/// the mounted inner, so there is nothing to diff), and a CHANGED key replaces the inner
/// (unmount + fresh mount of the new <see cref="Factory"/> output). The old factory is never
/// re-invoked at update time, so it is always safe to drop a <c>Memo(key, …)</c> anywhere a
/// normal element is expected. The cross-recycle cache benefit only applies on the
/// <see cref="ElementFactory{T}"/> recycle path.</para>
///
/// <para>The positional parameter is named <c>MemoKey</c> (not <c>Key</c>) so it does not clash
/// with the inherited <see cref="Element.Key"/> string used for keyed reconciliation.</para>
/// </summary>
public sealed record KeyedMemoElement(object MemoKey, Func<Element> Factory) : Element;

// ════════════════════════════════════════════════════════════════════════
//  Semantic wrapper for composite accessibility
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Describes the semantic role, value, and range of a composite component
/// for assistive technology. Used with the .Semantics() modifier.
/// </summary>
public record SemanticDescription(
    string? Role = null,
    string? Value = null,
    double? RangeMin = null,
    double? RangeMax = null,
    double? RangeValue = null,
    bool IsReadOnly = true);

/// <summary>
/// Wraps a child element in a SemanticPanel that provides custom automation
/// semantics to screen readers. This solves the problem where Reactor components
/// can't override OnCreateAutomationPeer().
/// </summary>
// Spec 058 §15 (P5.24) — SemanticPanel accessibility wrapper. Both props are bespoke: Child is a
// Children-backed SingleContent (Clear + Add, not a Content prop → overwrite d.Children), and the
// 6 control props are projected from the nested Semantics (SemanticDescription) record. Replaces
// the hand-written SemanticDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(global::Microsoft.UI.Reactor.Accessibility.SemanticPanel))]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Child")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Semantics")]
public partial record SemanticElement(Element Child, SemanticDescription Semantics) : Element
{
    internal Action<global::Microsoft.UI.Reactor.Accessibility.SemanticPanel>[] Setters { get; init; } = [];

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<SemanticElement, global::Microsoft.UI.Reactor.Accessibility.SemanticPanel> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<SemanticElement, global::Microsoft.UI.Reactor.Accessibility.SemanticPanel> d)
    {
        d.Children = new global::Microsoft.UI.Reactor.Core.V1Protocol.SingleContent<SemanticElement, global::Microsoft.UI.Reactor.Accessibility.SemanticPanel>(
            GetChild: static e => e.Child,
            SetChild: static (panel, ui) =>
            {
                panel.Children.Clear();
                if (ui is not null) panel.Children.Add(ui);
            })
        {
            GetCurrentChild = static panel => panel.Children.Count > 0 ? panel.Children[0] : null,
        };
        return d
            .OneWay(
                get: static e => e.Semantics.Role,
                set: static (c, v) => c.SemanticRole = v)
            .OneWay(
                get: static e => e.Semantics.Value,
                set: static (c, v) => c.SemanticValue = v)
            .OneWay(
                get: static e => e.Semantics.RangeMin ?? 0.0,
                set: static (c, v) => c.RangeMinimum = v)
            .OneWay(
                get: static e => e.Semantics.RangeMax ?? 0.0,
                set: static (c, v) => c.RangeMaximum = v)
            .OneWay(
                get: static e => e.Semantics.RangeValue ?? 0.0,
                set: static (c, v) => c.RangeValue = v)
            .OneWay(
                get: static e => e.Semantics.IsReadOnly,
                set: static (c, v) => c.IsReadOnly = v);
    }
}

/// <summary>
/// Cross-cutting "extras" bucket for <see cref="Element"/> (spec 047 §4.4).
/// Holds the 14 rarely-set fields (attached properties, transitions, theme
/// bindings, animation configs, resource overrides, context values) that used
/// to sit inline on every element. Bucketing them into a lazy sub-record lets
/// the common case (a leaf with none of these) pay one reference slot instead
/// of 14. The fields are exposed on <see cref="Element"/> via get/init shim
/// properties that read from / write into this record, so call sites see no
/// API change. Value-equality (default record) so equality helpers and
/// <c>with</c>-writers behave exactly as before. Unlike
/// <see cref="ElementModifiers"/> nothing merges Extras, so there is no Merge.
///
/// <para><b>Construction-cost tradeoff (spec 047 §4.4 / §11.6):</b> the common
/// no-extras leaf now saves a reference slot (the §4.4 win), but each
/// extra-setting fluent call (<c>.Grid()</c>, <c>.Background(Theme…)</c>,
/// <c>.Animate()</c>, <c>.Provide()</c>, …) clones this sub-record <em>in
/// addition to</em> the <see cref="Element"/> clone — so an element that sets
/// several extras does N small <c>ElementExtras</c> allocations during
/// construction where the inline layout did zero. Net win for the common case;
/// watch the §11.6 byte-gate M2/M3 (callback/modifier-heavy leaves) when it is
/// measured on the baseline box, as those are the construction-cost-sensitive
/// scenarios.</para>
/// </summary>
/// <remarks>Spec 047 §4.4.</remarks>
public record ElementExtras
{
    /// <summary>
    /// Attached properties from parent containers (Grid.Row, Canvas.Left, etc.).
    /// Set via fluent extension methods: Text("hi").Grid(row: 1, column: 2)
    /// Stored as a type-keyed dictionary so each provider defines its own data record.
    /// </summary>
    public IReadOnlyDictionary<Type, object>? Attached { get; init; }

    /// <summary>
    /// Implicit transitions (opacity, scale, rotation, translation, background).
    /// Set via fluent extension methods: Rectangle().WithOpacityTransition()
    /// Applied by the reconciler after mount/update, so they are always present when
    /// property values are set via .Set() callbacks.
    /// </summary>
    public ImplicitTransitions? ImplicitTransitions { get; init; }

    /// <summary>
    /// Theme transitions (children, item container).
    /// Set via fluent extension methods: VStack(children).WithThemeTransitions(...)
    /// </summary>
    public ThemeTransitions? ThemeTransitions { get; init; }

    /// <summary>
    /// Theme-resource bindings for brush properties (Background, Foreground, BorderBrush).
    /// When set, the reconciler resolves from WinUI theme resources instead of using local values.
    /// Set via fluent extension methods: Text("hi").Background(Theme.Accent)
    /// </summary>
    public IReadOnlyDictionary<string, ThemeRef>? ThemeBindings { get; init; }

    /// <summary>
    /// Composition-layer layout animation configuration.
    /// When set, the reconciler attaches implicit animations to the element's Visual
    /// so that layout-driven position (and optionally size) changes animate smoothly.
    /// Set via fluent extension methods: Border(child).LayoutAnimation()
    /// </summary>
    public LayoutAnimationConfig? LayoutAnimation { get; init; }

    /// <summary>
    /// Compositor property animation configuration (.Animate() modifier).
    /// When set, the reconciler creates ImplicitAnimationCollection entries on the
    /// element's Visual for Opacity/Scale/Rotation/Offset/CenterPoint.
    /// </summary>
    public Microsoft.UI.Reactor.Animation.AnimationConfig? AnimationConfig { get; init; }

    /// <summary>
    /// Element enter/exit transition configuration (.Transition() modifier).
    /// When set, the reconciler animates mount (enter) and unmount (exit) with
    /// compositor animations, deferring removal until exit animation completes.
    /// </summary>
    public Microsoft.UI.Reactor.Animation.ElementTransition? ElementTransition { get; init; }

    /// <summary>
    /// Interaction states configuration (.InteractionStates() modifier).
    /// When set, the reconciler registers pointer event handlers that drive
    /// zero-reconcile visual state transitions (hover, pressed, focused).
    /// </summary>
    public Microsoft.UI.Reactor.Animation.InteractionStatesConfig? InteractionStates { get; init; }

    /// <summary>
    /// Stagger configuration for container children (.Stagger() modifier).
    /// When set, child animations (enter, layout, property) have incrementing
    /// DelayTime = childIndex * staggerDelay.
    /// </summary>
    public Microsoft.UI.Reactor.Animation.StaggerConfig? StaggerConfig { get; init; }

    /// <summary>
    /// Keyframe animation definitions (.Keyframes() modifier).
    /// Trigger-based: plays when the trigger value changes between renders.
    /// </summary>
    public Microsoft.UI.Reactor.Animation.KeyframeEntry[]? KeyframeAnimations { get; init; }

    /// <summary>
    /// Scroll-linked expression animation configuration (.ScrollLinked() modifier).
    /// Expression animations run on the compositor, driven by ScrollViewer position.
    /// </summary>
    public Microsoft.UI.Reactor.Animation.ScrollAnimationConfig? ScrollAnimation { get; init; }

    /// <summary>
    /// Connected animation key for cross-container transitions.
    /// When set, the reconciler automatically captures a visual snapshot on unmount
    /// (via ConnectedAnimationService.PrepareToAnimate) and starts the animation on
    /// mount if a prepared animation with the same key exists.
    /// Set via fluent extension method: Border(child).ConnectedAnimation("hero")
    /// </summary>
    public string? ConnectedAnimationKey { get; init; }

    /// <summary>
    /// Per-control resource overrides (lightweight styling). When set, the reconciler
    /// injects these into <see cref="FrameworkElement.Resources"/> so that the control's
    /// VisualStateManager picks them up for hover/pressed/disabled states.
    /// Set via fluent extension: <c>Button("Go").Resources(r => r.Set("ButtonBackground", "#0078D4"))</c>
    /// </summary>
    public Microsoft.UI.Reactor.Elements.ResourceOverrides? ResourceOverrides { get; init; }

    /// <summary>
    /// Context values provided to this element's subtree via .Provide().
    /// The reconciler pushes these onto the context scope when entering
    /// this element's subtree and pops them when leaving.
    /// </summary>
    public IReadOnlyDictionary<ContextBase, object?>? ContextValues { get; init; }

    /// <summary>
    /// True when every bucketed field is null. The <see cref="Element"/> shim
    /// setters use this (via <c>Element.NormalizeExtras</c>) to collapse an
    /// all-null bucket back to a null <c>Extensions</c> slot, so record
    /// equality between an extras-free element and one whose only "extra" is a
    /// field explicitly set to null stays symmetric (PR #455 CR item #2).
    /// </summary>
    internal bool IsEmpty =>
        Attached is null
        && ImplicitTransitions is null
        && ThemeTransitions is null
        && ThemeBindings is null
        && LayoutAnimation is null
        && AnimationConfig is null
        && ElementTransition is null
        && InteractionStates is null
        && StaggerConfig is null
        && KeyframeAnimations is null
        && ScrollAnimation is null
        && ConnectedAnimationKey is null
        && ResourceOverrides is null
        && ContextValues is null;
}

public record ElementModifiers
{
    // ── Bucketed sub-records (spec 034 §A) ──────────────────────────
    // Layout / Visual fields are stored in dedicated sub-records so that the
    // common case (a cell that sets only Foreground + Padding) allocates two
    // small bucket records instead of bloating the parent ElementModifiers.
    // Public properties for moved fields (Padding, Margin, Width, …,
    // Foreground, Background, …) stay on ElementModifiers as get/init shims
    // that read from / write into the appropriate bucket — call sites see no
    // API change.
    /// <summary>
    /// Layout-bucket sub-record. Set directly only in perf-critical inner
    /// loops; ordinary code uses the field shim properties (Padding, Margin,
    /// Width, …) and never observes this slot.
    /// </summary>
    /// <remarks>Spec 034 §A.</remarks>
    public LayoutModifiers? Layout { get; init; }
    /// <summary>
    /// Visual-bucket sub-record. Set directly only in perf-critical inner
    /// loops; ordinary code uses the field shim properties (Foreground,
    /// Background, BorderBrush, …) and never observes this slot.
    /// </summary>
    /// <remarks>Spec 034 §A.</remarks>
    public VisualModifiers? Visual { get; init; }

    public Thickness? Margin
    {
        get => Layout?.Margin;
        init => Layout = Layout is null ? new LayoutModifiers { Margin = value } : Layout with { Margin = value };
    }
    public Thickness? Padding
    {
        get => Layout?.Padding;
        init => Layout = Layout is null ? new LayoutModifiers { Padding = value } : Layout with { Padding = value };
    }
    public double? Width
    {
        get => Layout?.Width;
        init => Layout = Layout is null ? new LayoutModifiers { Width = value } : Layout with { Width = value };
    }
    public double? Height
    {
        get => Layout?.Height;
        init => Layout = Layout is null ? new LayoutModifiers { Height = value } : Layout with { Height = value };
    }
    public double? MinWidth
    {
        get => Layout?.MinWidth;
        init => Layout = Layout is null ? new LayoutModifiers { MinWidth = value } : Layout with { MinWidth = value };
    }
    public double? MinHeight
    {
        get => Layout?.MinHeight;
        init => Layout = Layout is null ? new LayoutModifiers { MinHeight = value } : Layout with { MinHeight = value };
    }
    public double? MaxWidth
    {
        get => Layout?.MaxWidth;
        init => Layout = Layout is null ? new LayoutModifiers { MaxWidth = value } : Layout with { MaxWidth = value };
    }
    public double? MaxHeight
    {
        get => Layout?.MaxHeight;
        init => Layout = Layout is null ? new LayoutModifiers { MaxHeight = value } : Layout with { MaxHeight = value };
    }
    public HorizontalAlignment? HorizontalAlignment
    {
        get => Layout?.HorizontalAlignment;
        init => Layout = Layout is null ? new LayoutModifiers { HorizontalAlignment = value } : Layout with { HorizontalAlignment = value };
    }
    public VerticalAlignment? VerticalAlignment
    {
        get => Layout?.VerticalAlignment;
        init => Layout = Layout is null ? new LayoutModifiers { VerticalAlignment = value } : Layout with { VerticalAlignment = value };
    }
    public double? Opacity
    {
        get => Visual?.Opacity;
        init => Visual = Visual is null ? new VisualModifiers { Opacity = value } : Visual with { Opacity = value };
    }
    public global::System.Numerics.Vector3? Scale
    {
        get => Visual?.Scale;
        init => Visual = Visual is null ? new VisualModifiers { Scale = value } : Visual with { Scale = value };
    }
    public float? Rotation
    {
        get => Visual?.Rotation;
        init => Visual = Visual is null ? new VisualModifiers { Rotation = value } : Visual with { Rotation = value };
    }
    public global::System.Numerics.Vector3? Translation
    {
        get => Visual?.Translation;
        init => Visual = Visual is null ? new VisualModifiers { Translation = value } : Visual with { Translation = value };
    }
    public global::System.Numerics.Vector3? CenterPoint
    {
        get => Visual?.CenterPoint;
        init => Visual = Visual is null ? new VisualModifiers { CenterPoint = value } : Visual with { CenterPoint = value };
    }
    public bool? IsVisible
    {
        get => Layout?.IsVisible;
        init => Layout = Layout is null ? new LayoutModifiers { IsVisible = value } : Layout with { IsVisible = value };
    }
    public string? ToolTip { get; init; }
    public Element? RichToolTip { get; init; }
    public Element? AttachedFlyout { get; init; }
    public Element? ContextFlyout { get; init; }
    public Brush? Background
    {
        get => Visual?.Background;
        init => Visual = Visual is null ? new VisualModifiers { Background = value } : Visual with { Background = value };
    }
    public Brush? Foreground
    {
        get => Visual?.Foreground;
        init => Visual = Visual is null ? new VisualModifiers { Foreground = value } : Visual with { Foreground = value };
    }
    public bool? IsEnabled { get; init; }
    public Microsoft.UI.Xaml.CornerRadius? CornerRadius
    {
        get => Visual?.CornerRadius;
        init => Visual = Visual is null ? new VisualModifiers { CornerRadius = value } : Visual with { CornerRadius = value };
    }
    public Brush? BorderBrush
    {
        get => Visual?.BorderBrush;
        init => Visual = Visual is null ? new VisualModifiers { BorderBrush = value } : Visual with { BorderBrush = value };
    }
    public Thickness? BorderThickness
    {
        get => Visual?.BorderThickness;
        init => Visual = Visual is null ? new VisualModifiers { BorderThickness = value } : Visual with { BorderThickness = value };
    }
    public string? AutomationName { get; init; }
    public string? AutomationId { get; init; }
    public ElementSoundMode? ElementSoundMode { get; init; }
    public Action<FrameworkElement>? OnMountAction { get; init; }
    public Action<FrameworkElement>? OnUnmountAction { get; init; }
    // Update-time counterpart to OnMountAction: runs on every in-place update
    // (oldM is not null) after children have been reconciled. Internal-only — the
    // framework uses it (chart label a11y, issue #162) to re-assert a side effect
    // over descendants realized during an update; not yet part of the public DSL.
    internal Action<FrameworkElement>? OnUpdateAction { get; init; }

    // ── Typography (applies to any Control or TextBlock) ────────────
    public FontFamily? FontFamily { get; init; }
    public double? FontSize { get; init; }
    public FontWeight? FontWeight { get; init; }

    // ── Declarative event handlers (re-attached on every update) ────
    public Action<object, SizeChangedEventArgs>? OnSizeChanged { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerPressed { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerMoved { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerReleased { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerEntered { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerExited { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerCanceled { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerCaptureLost { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs>? OnPointerWheelChanged { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs>? OnTapped { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs>? OnDoubleTapped { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs>? OnRightTapped { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.HoldingRoutedEventArgs>? OnHolding { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? OnKeyDown { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? OnKeyUp { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? OnPreviewKeyDown { get; init; }
    public Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs>? OnPreviewKeyUp { get; init; }
    public Action<UIElement, Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs>? OnCharacterReceived { get; init; }
    public Action<object, RoutedEventArgs>? OnGotFocus { get; init; }
    public Action<object, RoutedEventArgs>? OnLostFocus { get; init; }

    // ── Declarative gesture recognizers (spec 027 Tier 3) ───────────
    // Drive a single ManipulationStarted/Delta/Completed subscription per element.
    public Microsoft.UI.Reactor.Input.PanGestureConfig? Pan { get; init; }
    public Microsoft.UI.Reactor.Input.PinchGestureConfig? Pinch { get; init; }
    public Microsoft.UI.Reactor.Input.RotateGestureConfig? Rotate { get; init; }
    public Microsoft.UI.Reactor.Input.LongPressGestureConfig? LongPress { get; init; }

    // ── Drag-and-drop (spec 027 Tier 6 — Phase 6a typed in-process) ─
    public Microsoft.UI.Reactor.Input.DragSourceConfig? DragSource { get; init; }
    public Microsoft.UI.Reactor.Input.DropTargetConfig? DropTarget { get; init; }

    // ── Logical (BiDi-aware) layout properties ──────────────────────
    // These resolve to physical left/right based on FlowDirection at mount/update time.
    // InlineStart = left in LTR, right in RTL. InlineEnd = right in LTR, left in RTL.
    public double? MarginInlineStart
    {
        get => Layout?.MarginInlineStart;
        init => Layout = Layout is null ? new LayoutModifiers { MarginInlineStart = value } : Layout with { MarginInlineStart = value };
    }
    public double? MarginInlineEnd
    {
        get => Layout?.MarginInlineEnd;
        init => Layout = Layout is null ? new LayoutModifiers { MarginInlineEnd = value } : Layout with { MarginInlineEnd = value };
    }
    public double? PaddingInlineStart
    {
        get => Layout?.PaddingInlineStart;
        init => Layout = Layout is null ? new LayoutModifiers { PaddingInlineStart = value } : Layout with { PaddingInlineStart = value };
    }
    public double? PaddingInlineEnd
    {
        get => Layout?.PaddingInlineEnd;
        init => Layout = Layout is null ? new LayoutModifiers { PaddingInlineEnd = value } : Layout with { PaddingInlineEnd = value };
    }
    public Thickness? BorderInlineStart
    {
        get => Layout?.BorderInlineStart;
        init => Layout = Layout is null ? new LayoutModifiers { BorderInlineStart = value } : Layout with { BorderInlineStart = value };
    }

    // ── Theme override ───────────────────────────────────────────────
    /// <summary>
    /// Sets <see cref="FrameworkElement.RequestedTheme"/> on the control,
    /// forcing a subtree to render in a specific theme variant (e.g., dark
    /// sidebar in a light app). Applied before ThemeRef bindings resolve so
    /// that theme resources pick up the correct variant.
    /// </summary>
    public ElementTheme? RequestedTheme
    {
        get => Layout?.RequestedTheme;
        init => Layout = Layout is null ? new LayoutModifiers { RequestedTheme = value } : Layout with { RequestedTheme = value };
    }

    /// <summary>
    /// When set, writes the <c>Microsoft.UI.Xaml.Controls.TitleBar.IsDragRegion</c>
    /// attached property on this element's control (WinApp SDK ≥ 2.1.3):
    /// <c>true</c> = draggable, <c>false</c> = clickable. Unset leaves the title bar
    /// to decide (interactive controls are auto-excluded from the drag region).
    /// Inert on elements that are not inside a TitleBar. See spec 059.
    /// </summary>
    public bool? IsDragRegion { get; init; }

    // ── Accessibility — Tier 1 (inline, commonly needed for WCAG AA) ─
    public Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel? HeadingLevel { get; init; }
    public bool? IsTabStop { get; init; }
    public bool? IsHitTestVisible { get; init; }
    public int? TabIndex { get; init; }
    public string? AccessKey { get; init; }
    public Microsoft.UI.Xaml.Input.XYFocusKeyboardNavigationMode? XYFocusKeyboardNavigation { get; init; }
    public Microsoft.UI.Reactor.Input.ElementRef? XYFocusUpRef { get; init; }
    public Microsoft.UI.Reactor.Input.ElementRef? XYFocusDownRef { get; init; }
    public Microsoft.UI.Reactor.Input.ElementRef? XYFocusLeftRef { get; init; }
    public Microsoft.UI.Reactor.Input.ElementRef? XYFocusRightRef { get; init; }
    public Action<UIElement, Microsoft.UI.Xaml.Input.AccessKeyDisplayRequestedEventArgs>? OnAccessKeyDisplayRequested { get; init; }

    /// <summary>
    /// Imperative ref slot (spec 027 Tier 5). The reconciler writes the mounted
    /// <see cref="FrameworkElement"/> into <see cref="Microsoft.UI.Reactor.Input.ElementRef._current"/>
    /// so <c>FocusManager.Focus(ref)</c> (and future ref-based imperative APIs) can target it.
    /// </summary>
    public Microsoft.UI.Reactor.Input.ElementRef? Ref { get; init; }

    // ── SystemBackdrop (spec 033 §6) ────────────────────────────────
    /// <summary>
    /// Declarative system backdrop (Mica / Acrylic) for the host window.
    /// Read by <c>ReactorHost</c> from the root tree's modifiers and applied to
    /// the owning <c>Window.SystemBackdrop</c>. Ignored by <c>ReactorHostControl</c>
    /// that does not own its window.
    /// </summary>
    public BackdropChoice? Backdrop { get; init; }

    // ── Accessibility — Tier 2/3 (lazy sub-record, zero allocation unless used) ─
    public AccessibilityModifiers? Accessibility { get; init; }

    public ElementModifiers Merge(ElementModifiers other)
    {
        // Merge buckets at sub-record level. Naming a shim'd property inside
        // the with { } here would clone the bucket once per moved field
        // (each shim's init re-runs); write Layout / Visual once instead.
        var mergedLayout = other.Layout is not null
            ? (Layout is not null ? Layout.Merge(other.Layout) : other.Layout)
            : Layout;
        var mergedVisual = other.Visual is not null
            ? (Visual is not null ? Visual.Merge(other.Visual) : other.Visual)
            : Visual;

        return this with
        {
            Layout = mergedLayout,
            Visual = mergedVisual,
            ToolTip = other.ToolTip ?? ToolTip,
            RichToolTip = other.RichToolTip ?? RichToolTip,
            AttachedFlyout = other.AttachedFlyout ?? AttachedFlyout,
            ContextFlyout = other.ContextFlyout ?? ContextFlyout,
            IsEnabled = other.IsEnabled ?? IsEnabled,
            AutomationName = other.AutomationName ?? AutomationName,
            AutomationId = other.AutomationId ?? AutomationId,
            ElementSoundMode = other.ElementSoundMode ?? ElementSoundMode,
            OnMountAction = other.OnMountAction ?? OnMountAction,
            OnUnmountAction = other.OnUnmountAction ?? OnUnmountAction,
            OnUpdateAction = other.OnUpdateAction ?? OnUpdateAction,
            FontFamily = other.FontFamily ?? FontFamily,
            FontSize = other.FontSize ?? FontSize,
            FontWeight = other.FontWeight ?? FontWeight,
            OnSizeChanged = other.OnSizeChanged ?? OnSizeChanged,
            OnPointerPressed = other.OnPointerPressed ?? OnPointerPressed,
            OnPointerMoved = other.OnPointerMoved ?? OnPointerMoved,
            OnPointerReleased = other.OnPointerReleased ?? OnPointerReleased,
            OnPointerEntered = other.OnPointerEntered ?? OnPointerEntered,
            OnPointerExited = other.OnPointerExited ?? OnPointerExited,
            OnPointerCanceled = other.OnPointerCanceled ?? OnPointerCanceled,
            OnPointerCaptureLost = other.OnPointerCaptureLost ?? OnPointerCaptureLost,
            OnPointerWheelChanged = other.OnPointerWheelChanged ?? OnPointerWheelChanged,
            OnTapped = other.OnTapped ?? OnTapped,
            OnDoubleTapped = other.OnDoubleTapped ?? OnDoubleTapped,
            OnRightTapped = other.OnRightTapped ?? OnRightTapped,
            OnHolding = other.OnHolding ?? OnHolding,
            OnKeyDown = other.OnKeyDown ?? OnKeyDown,
            OnKeyUp = other.OnKeyUp ?? OnKeyUp,
            OnPreviewKeyDown = other.OnPreviewKeyDown ?? OnPreviewKeyDown,
            OnPreviewKeyUp = other.OnPreviewKeyUp ?? OnPreviewKeyUp,
            OnCharacterReceived = other.OnCharacterReceived ?? OnCharacterReceived,
            OnGotFocus = other.OnGotFocus ?? OnGotFocus,
            OnLostFocus = other.OnLostFocus ?? OnLostFocus,
            Pan = other.Pan ?? Pan,
            Pinch = other.Pinch ?? Pinch,
            Rotate = other.Rotate ?? Rotate,
            LongPress = other.LongPress ?? LongPress,
            DragSource = other.DragSource ?? DragSource,
            DropTarget = other.DropTarget ?? DropTarget,
            HeadingLevel = other.HeadingLevel ?? HeadingLevel,
            IsDragRegion = other.IsDragRegion ?? IsDragRegion,
            IsTabStop = other.IsTabStop ?? IsTabStop,
            IsHitTestVisible = other.IsHitTestVisible ?? IsHitTestVisible,
            TabIndex = other.TabIndex ?? TabIndex,
            AccessKey = other.AccessKey ?? AccessKey,
            XYFocusKeyboardNavigation = other.XYFocusKeyboardNavigation ?? XYFocusKeyboardNavigation,
            XYFocusUpRef = other.XYFocusUpRef ?? XYFocusUpRef,
            XYFocusDownRef = other.XYFocusDownRef ?? XYFocusDownRef,
            XYFocusLeftRef = other.XYFocusLeftRef ?? XYFocusLeftRef,
            XYFocusRightRef = other.XYFocusRightRef ?? XYFocusRightRef,
            OnAccessKeyDisplayRequested = other.OnAccessKeyDisplayRequested ?? OnAccessKeyDisplayRequested,
            Ref = other.Ref ?? Ref,
            Backdrop = other.Backdrop ?? Backdrop,
            Accessibility = other.Accessibility is not null
                ? (Accessibility is not null ? Accessibility.Merge(other.Accessibility) : other.Accessibility)
                : Accessibility,
        };
    }
}

/// <summary>
/// Layout-related modifiers (sizing, alignment, spacing, visibility, theme,
/// logical-direction insets). Stored as a lazy sub-record on
/// <see cref="ElementModifiers"/> so that the common case of a few fields
/// set allocates a small bucket rather than bloating the parent record.
/// Public properties on <see cref="ElementModifiers"/> (Padding, Margin,
/// Width, …) read from / write into this sub-record transparently — most
/// callers never see this type.
/// </summary>
/// <remarks>
/// Spec 034 §A. The field set may grow but won't shrink — additions are
/// always backwards-compatible.
/// </remarks>
public record LayoutModifiers
{
    public Thickness? Margin { get; init; }
    public Thickness? Padding { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public double? MinWidth { get; init; }
    public double? MinHeight { get; init; }
    public double? MaxWidth { get; init; }
    public double? MaxHeight { get; init; }
    public HorizontalAlignment? HorizontalAlignment { get; init; }
    public VerticalAlignment? VerticalAlignment { get; init; }
    public bool? IsVisible { get; init; }
    public double? MarginInlineStart { get; init; }
    public double? MarginInlineEnd { get; init; }
    public double? PaddingInlineStart { get; init; }
    public double? PaddingInlineEnd { get; init; }
    public Thickness? BorderInlineStart { get; init; }
    public ElementTheme? RequestedTheme { get; init; }

    /// <summary>
    /// Merge <paramref name="other"/> into this record, preferring
    /// <paramref name="other"/>'s set fields and falling back to ours.
    /// Mirrors <see cref="ElementModifiers.Merge"/>.
    /// </summary>
    /// <remarks>Spec 034 §A.</remarks>
    public LayoutModifiers Merge(LayoutModifiers other) => this with
    {
        Margin = other.Margin ?? Margin,
        Padding = other.Padding ?? Padding,
        Width = other.Width ?? Width,
        Height = other.Height ?? Height,
        MinWidth = other.MinWidth ?? MinWidth,
        MinHeight = other.MinHeight ?? MinHeight,
        MaxWidth = other.MaxWidth ?? MaxWidth,
        MaxHeight = other.MaxHeight ?? MaxHeight,
        HorizontalAlignment = other.HorizontalAlignment ?? HorizontalAlignment,
        VerticalAlignment = other.VerticalAlignment ?? VerticalAlignment,
        IsVisible = other.IsVisible ?? IsVisible,
        MarginInlineStart = other.MarginInlineStart ?? MarginInlineStart,
        MarginInlineEnd = other.MarginInlineEnd ?? MarginInlineEnd,
        PaddingInlineStart = other.PaddingInlineStart ?? PaddingInlineStart,
        PaddingInlineEnd = other.PaddingInlineEnd ?? PaddingInlineEnd,
        BorderInlineStart = other.BorderInlineStart ?? BorderInlineStart,
        RequestedTheme = other.RequestedTheme ?? RequestedTheme,
    };
}

/// <summary>
/// Visual-related modifiers (brushes, borders, transforms, opacity).
/// Stored as a lazy sub-record on <see cref="ElementModifiers"/> in the
/// same pattern as <see cref="LayoutModifiers"/>. Public properties on
/// <see cref="ElementModifiers"/> (Foreground, Background, BorderBrush, …)
/// shim through.
/// </summary>
/// <remarks>
/// Spec 034 §A. The field set may grow but won't shrink — additions are
/// always backwards-compatible.
/// </remarks>
public record VisualModifiers
{
    public Brush? Background { get; init; }
    public Brush? Foreground { get; init; }
    public Brush? BorderBrush { get; init; }
    public Thickness? BorderThickness { get; init; }
    public Microsoft.UI.Xaml.CornerRadius? CornerRadius { get; init; }
    public double? Opacity { get; init; }
    public global::System.Numerics.Vector3? Scale { get; init; }
    public float? Rotation { get; init; }
    public global::System.Numerics.Vector3? Translation { get; init; }
    public global::System.Numerics.Vector3? CenterPoint { get; init; }

    /// <summary>
    /// Merge <paramref name="other"/> into this record, preferring
    /// <paramref name="other"/>'s set fields and falling back to ours.
    /// </summary>
    /// <remarks>Spec 034 §A.</remarks>
    public VisualModifiers Merge(VisualModifiers other) => this with
    {
        Background = other.Background ?? Background,
        Foreground = other.Foreground ?? Foreground,
        BorderBrush = other.BorderBrush ?? BorderBrush,
        BorderThickness = other.BorderThickness ?? BorderThickness,
        CornerRadius = other.CornerRadius ?? CornerRadius,
        Opacity = other.Opacity ?? Opacity,
        Scale = other.Scale ?? Scale,
        Rotation = other.Rotation ?? Rotation,
        Translation = other.Translation ?? Translation,
        CenterPoint = other.CenterPoint ?? CenterPoint,
    };
}

/// <summary>
/// Advanced accessibility properties (WCAG Tier 2/3). Stored as a lazy sub-record
/// on ElementModifiers to avoid allocating storage for elements that don't need
/// advanced accessibility annotations. All fluent extension methods create/merge
/// this record automatically — developers never need to construct it directly.
/// </summary>
public record AccessibilityModifiers
{
    /// <summary>AutomationProperties.HelpText — supplemental description read after the Name.</summary>
    public string? HelpText { get; init; }

    /// <summary>AutomationProperties.FullDescription — extended description for complex elements.</summary>
    public string? FullDescription { get; init; }

    /// <summary>AutomationProperties.LandmarkType — landmark region (Main, Navigation, Search, Form).</summary>
    public Microsoft.UI.Xaml.Automation.Peers.AutomationLandmarkType? LandmarkType { get; init; }

    /// <summary>AutomationProperties.AccessibilityView — UIA tree visibility (Content, Control, Raw).</summary>
    public Microsoft.UI.Xaml.Automation.Peers.AccessibilityView? AccessibilityView { get; init; }

    /// <summary>AutomationProperties.IsRequiredForForm — screen readers announce "required".</summary>
    public bool? IsRequiredForForm { get; init; }

    /// <summary>AutomationProperties.LiveSetting — live region announcement mode (Polite, Assertive).</summary>
    public Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting? LiveSetting { get; init; }

    /// <summary>AutomationProperties.PositionInSet — ordinal position (1-based) in a group.</summary>
    public int? PositionInSet { get; init; }

    /// <summary>AutomationProperties.SizeOfSet — total count in the group.</summary>
    public int? SizeOfSet { get; init; }

    /// <summary>AutomationProperties.Level — hierarchical depth (e.g., tree node level).</summary>
    public int? Level { get; init; }

    /// <summary>AutomationProperties.ItemStatus — status string (e.g., "3 unread").</summary>
    public string? ItemStatus { get; init; }

    /// <summary>AutomationProperties.LabeledBy target AutomationId — resolved by the reconciler.</summary>
    public string? LabeledBy { get; init; }

    /// <summary>AutomationProperties.LabeledBy target resolved from an ElementRef.</summary>
    public Microsoft.UI.Reactor.Input.ElementRef? LabeledByRef { get; init; }

    /// <summary>AutomationProperties.DescribedBy targets resolved from ElementRefs.</summary>
    public IReadOnlyList<Microsoft.UI.Reactor.Input.ElementRef>? DescribedByRefs { get; init; }

    /// <summary>AutomationProperties.FlowsTo targets resolved from ElementRefs.</summary>
    public IReadOnlyList<Microsoft.UI.Reactor.Input.ElementRef>? FlowsToRefs { get; init; }

    /// <summary>AutomationProperties.FlowsFrom targets resolved from ElementRefs.</summary>
    public IReadOnlyList<Microsoft.UI.Reactor.Input.ElementRef>? FlowsFromRefs { get; init; }

    /// <summary>UIElement.TabFocusNavigation — Tab behavior within a container (Local, Once, Cycle).</summary>
    public Microsoft.UI.Xaml.Input.KeyboardNavigationMode? TabFocusNavigation { get; init; }

    public AccessibilityModifiers Merge(AccessibilityModifiers other)
    {
        return this with
        {
            HelpText = other.HelpText ?? HelpText,
            FullDescription = other.FullDescription ?? FullDescription,
            LandmarkType = other.LandmarkType ?? LandmarkType,
            AccessibilityView = other.AccessibilityView ?? AccessibilityView,
            IsRequiredForForm = other.IsRequiredForForm ?? IsRequiredForForm,
            LiveSetting = other.LiveSetting ?? LiveSetting,
            PositionInSet = other.PositionInSet ?? PositionInSet,
            SizeOfSet = other.SizeOfSet ?? SizeOfSet,
            Level = other.Level ?? Level,
            ItemStatus = other.ItemStatus ?? ItemStatus,
            LabeledBy = other.LabeledBy ?? LabeledBy,
            LabeledByRef = other.LabeledByRef ?? LabeledByRef,
            DescribedByRefs = other.DescribedByRefs ?? DescribedByRefs,
            FlowsToRefs = other.FlowsToRefs ?? FlowsToRefs,
            FlowsFromRefs = other.FlowsFromRefs ?? FlowsFromRefs,
            TabFocusNavigation = other.TabFocusNavigation ?? TabFocusNavigation,
        };
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Transition data records (stored on Element base, applied by Reconciler)
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Declarative implicit transition configuration for a UIElement.
/// Each property maps to a WinUI implicit transition property on UIElement/Panel.
/// Null means "don't set this transition".
/// </summary>
public record ImplicitTransitions
{
    public ScalarTransition? Opacity { get; init; }
    public ScalarTransition? Rotation { get; init; }
    public Vector3Transition? Scale { get; init; }
    public Vector3Transition? Translation { get; init; }
    public BrushTransition? Background { get; init; }
}

/// <summary>
/// Declarative theme transition configuration.
/// Children applies to Panel.ChildrenTransitions / Border.ChildTransitions / ContentControl.ContentTransitions.
/// ItemContainer applies to ItemsControl.ItemContainerTransitions.
/// The reconciler picks the correct property based on control type.
/// </summary>
public record ThemeTransitions
{
    public Microsoft.UI.Xaml.Media.Animation.Transition[]? Children { get; init; }
    public Microsoft.UI.Xaml.Media.Animation.Transition[]? ItemContainer { get; init; }
}
// Note: Transition is in Microsoft.UI.Xaml.Media.Animation (not imported by default in Element.cs)

/// <summary>
/// Configuration for Composition-layer layout animations.
/// When applied to an element, the reconciler sets up implicit animations on the element's
/// Visual so that layout-driven Offset (position) and optionally Size changes animate smoothly.
/// Runs entirely on the Composition thread — zero managed-code involvement during animation.
///
/// Limitations:
/// - Hit-testing uses the final layout position, not the animated visual position.
/// - Elements must have stable keys (.WithKey()) for the reconciler to match them across reorders.
/// - Size animation is cosmetic: content does not re-layout during the Size animation.
/// - Only handles position changes for persistent elements; use theme transitions for enter/exit.
/// </summary>
public record LayoutAnimationConfig
{
    /// <summary>Duration of the layout animation. Default: 300ms.</summary>
    public TimeSpan Duration { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>When true, use a spring natural motion animation instead of linear keyframes.</summary>
    public bool UseSpring { get; init; }

    /// <summary>Spring damping ratio (0..1). Only used when UseSpring is true. Default: 0.6.</summary>
    public float DampingRatio { get; init; } = 0.6f;

    /// <summary>Spring period in seconds. Only used when UseSpring is true. Default: 0.08.</summary>
    public float Period { get; init; } = 0.08f;

    /// <summary>Animate Offset (position) changes. Default: true.</summary>
    public bool AnimateOffset { get; init; } = true;

    /// <summary>Animate Size changes. Default: false (content won't re-layout during animation).</summary>
    public bool AnimateSize { get; init; }
}

// Reactor reuses WinUI types directly — no shadow enums.
// See: Microsoft.UI.Xaml (Thickness, HorizontalAlignment, VerticalAlignment)
//      Microsoft.UI.Xaml.Controls (Orientation, InfoBarSeverity, ExpandDirection, etc.)
//      Microsoft.UI.Xaml.Controls.Primitives (FlyoutPlacementMode)
//      global::Windows.UI.Text (FontWeight, FontWeights)

// ════════════════════════════════════════════════════════════════════════
//  Supporting data records (non-Element, used as structured params)
// ════════════════════════════════════════════════════════════════════════

public record GridDefinition(string[] Columns, string[] Rows)
{
    /// <summary>
    /// Construct a <see cref="GridDefinition"/> from the strongly-typed
    /// <see cref="GridSize"/> form. Track strings are produced via
    /// <see cref="GridSize.ToString"/> using <c>CultureInfo.InvariantCulture</c>.
    /// Spec 033 §1.
    /// </summary>
    /// <exception cref="global::System.ArgumentNullException">Thrown when either array is null.</exception>
    public GridDefinition(GridSize[] columns, GridSize[] rows)
        : this(ToStrings(columns), ToStrings(rows))
    {
    }

    private static string[] ToStrings(GridSize[] sizes)
    {
        if (sizes is null) throw new global::System.ArgumentNullException(nameof(sizes));
        var result = new string[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
            result[i] = sizes[i].ToString();
        return result;
    }
}

/// <summary>Attached property data for Grid children. Set via .Grid(row:, column:) extension.</summary>
public record GridAttached(int Row = 0, int Column = 0, int RowSpan = 1, int ColumnSpan = 1);

/// <summary>
/// Attached property data for VariableSizedWrapGrid children. Set via
/// <c>.WrapGridColumnSpan(int)</c> / <c>.WrapGridRowSpan(int)</c> extensions.
/// </summary>
public record WrapGridAttached(int RowSpan = 1, int ColumnSpan = 1);

/// <summary>Attached property data for Canvas children. Set via .Canvas(left:, top:) extension.</summary>
/// <remarks>
/// <see cref="AnchorX"/> / <see cref="AnchorY"/> are 0..1 fractions of the element's
/// rendered size that are subtracted from <see cref="Left"/>/<see cref="Top"/> after
/// layout. Anchor 0,0 (default) keeps the legacy top-left positioning. Anchor 0.5,0.5
/// centers the element on (Left, Top); 1,1 anchors at bottom-right. Useful for
/// chart labels and other elements that need to align around a logical point
/// rather than their top-left corner.
/// </remarks>
public record CanvasAttached(double Left = 0, double Top = 0)
{
    /// <summary>Horizontal anchor as a 0..1 fraction of the element's rendered width.</summary>
    public double AnchorX { get; init; }

    /// <summary>Vertical anchor as a 0..1 fraction of the element's rendered height.</summary>
    public double AnchorY { get; init; }
}

/// <summary>Attached property data for Flex children. Set via .Flex(grow:, shrink:, ...) extension.</summary>
public record FlexAttached(
    double Grow = 0,
    double Shrink = 1,
    double? Basis = null,
    double? MinWidth = null,
    double? MinHeight = null,
    Layout.FlexAlign? AlignSelf = null,
    Layout.FlexPositionType Position = Layout.FlexPositionType.Relative,
    double? Left = null,
    double? Top = null,
    double? Right = null,
    double? Bottom = null
);

/// <summary>Attached property data for RelativePanel children. Set via .RelativePanel(...) extension.</summary>
public record RelativePanelAttached(string Name)
{
    public string? RightOf { get; init; }
    public string? Below { get; init; }
    public string? LeftOf { get; init; }
    public string? Above { get; init; }
    public string? AlignLeftWith { get; init; }
    public string? AlignRightWith { get; init; }
    public string? AlignTopWith { get; init; }
    public string? AlignBottomWith { get; init; }
    public string? AlignHorizontalCenterWith { get; init; }
    public string? AlignVerticalCenterWith { get; init; }
    public bool AlignLeftWithPanel { get; init; }
    public bool AlignRightWithPanel { get; init; }
    public bool AlignTopWithPanel { get; init; }
    public bool AlignBottomWithPanel { get; init; }
    public bool AlignHorizontalCenterWithPanel { get; init; }
    public bool AlignVerticalCenterWithPanel { get; init; }
}

public record NavigationViewItemData(string Content, string? Icon = null, string? Tag = null)
{
    public NavigationViewItemData[]? Children { get; init; }
    public bool IsHeader { get; init; }
    public IconData? IconElement { get; init; }
}

public record TabViewItemData(string Header, Element Content)
{
    public string? Icon { get; init; }
    public bool IsClosable { get; init; } = true;

    /// <summary>
    /// Spec 045 §2.2 — when true, the reconciler renders a small "pin"
    /// affordance in the tab header chrome alongside the close X. Used
    /// by the docking pipeline for ToolWindow tabs whose CanAutoHide=true.
    /// </summary>
    public bool IsPinnable { get; init; }

    /// <summary>
    /// Indicates whether the pin button should render as already-pinned
    /// (glyph &#xE840;) or unpinned (&#xE842;). Only consulted when
    /// IsPinnable is true.
    /// </summary>
    public bool IsPinned { get; init; }

    /// <summary>
    /// Tooltip + AT name applied to the pin button when IsPinnable is
    /// true. Caller supplies the localized string.
    /// </summary>
    public string? PinAutomationName { get; init; }

    /// <summary>
    /// Stable AutomationId for the pin button. Selftests address the
    /// per-tab pin via this id.
    /// </summary>
    public string? PinAutomationId { get; init; }

    /// <summary>
    /// Invoked when the user clicks the pin button. Caller is
    /// responsible for routing through the docking model
    /// (DockHostModel.PinToSide / Hide).
    /// </summary>
    public Action? OnPinRequested { get; init; }
}

public record PivotItemData(string Header, Element Content);

public record BreadcrumbBarItemData(string Label, object? Tag = null);

public record TreeViewNodeData(string Content, TreeViewNodeData[]? Children = null)
{
    public bool IsExpanded { get; init; }

    /// <summary>
    /// Optional Reactor element to render as the node's visual content.
    /// When null, a TextBlock showing Content is rendered.
    /// </summary>
    /// <remarks>
    /// Deprecated. WinUI's node-mode <c>TreeView</c> stringifies node content
    /// and cannot host a pre-built <c>UIElement</c>, so rich per-node visuals
    /// must come from a template (a <c>data → Element</c> function), never an
    /// element instance. Use the typed, data-driven
    /// <c>UI.TreeView&lt;T&gt;(items, keySelector, childrenSelector, viewBuilder)</c>
    /// — the hierarchical peer of <c>ListView&lt;T&gt;</c> — instead. The legacy
    /// path stays functional for back-compat but renders blank under
    /// virtualization recycling.
    /// </remarks>
    [Obsolete("Use the typed UI.TreeView<T>(items, keySelector, childrenSelector, viewBuilder) overload (the hierarchical peer of ListView<T>); a pre-built Element cannot be hosted in a node-mode TreeViewNode. See issue #447.")]
    public Element? ContentElement { get; init; }
}

public record MenuBarItemData(string Title, MenuFlyoutItemBase[] Items);

public abstract record MenuFlyoutItemBase;
public record MenuFlyoutItemData(string Text, Action? OnClick = null, string? Icon = null) : MenuFlyoutItemBase
{
    public bool IsEnabled { get; init; } = true;
    public IconData? IconElement { get; init; }
    public KeyboardAcceleratorData[]? KeyboardAccelerators { get; init; }
    public string? AccessKey { get; init; }
    public string? Description { get; init; }
}
public record MenuFlyoutSeparatorData() : MenuFlyoutItemBase;
public record MenuFlyoutSubItemData(string Text, MenuFlyoutItemBase[] Items, string? Icon = null) : MenuFlyoutItemBase
{
    public IconData? IconElement { get; init; }
}
public record ToggleMenuFlyoutItemData(string Text, bool IsChecked = false, Action<bool>? OnIsCheckedChanged = null, string? Icon = null) : MenuFlyoutItemBase
{
    public IconData? IconElement { get; init; }
}
public record RadioMenuFlyoutItemData(string Text, string GroupName, bool IsChecked = false, Action? OnClick = null, string? Icon = null) : MenuFlyoutItemBase
{
    public IconData? IconElement { get; init; }
}

// Keyboard accelerator data
public record KeyboardAcceleratorData(global::Windows.System.VirtualKey Key, global::Windows.System.VirtualKeyModifiers Modifiers = global::Windows.System.VirtualKeyModifiers.None);

// Icon data hierarchy — used to set icons on menu items, app bar buttons, etc.
public abstract record IconData;
public record SymbolIconData(string Symbol) : IconData;
public record FontIconData(string Glyph, string? FontFamily = null, double? FontSize = null) : IconData;
public record BitmapIconData(global::System.Uri Source, bool ShowAsMonochrome = true) : IconData;
public record PathIconData(string Data) : IconData;
public record ImageIconData(global::System.Uri Source) : IconData;

/// <summary>
/// Standalone icon element that can be placed in the element tree.
/// Wraps an <see cref="IconData"/> and mounts to the corresponding
/// native <see cref="WinUI.IconElement"/> subtype.
/// </summary>
public partial record IconElement(IconData Data) : Element
{
    internal Action<WinUI.IconElement>[] Setters { get; init; } = [];
}

public abstract record AppBarItemBase;
public record AppBarButtonData(string Label, Action? OnClick = null, string? Icon = null) : AppBarItemBase
{
    public bool IsEnabled { get; init; } = true;
    public IconData? IconElement { get; init; }
    public KeyboardAcceleratorData[]? KeyboardAccelerators { get; init; }
    public string? AccessKey { get; init; }
    public string? Description { get; init; }
}
public record AppBarToggleButtonData(string Label, bool IsChecked = false, Action<bool>? OnIsCheckedChanged = null, string? Icon = null) : AppBarItemBase
{
    public IconData? IconElement { get; init; }
}
public record AppBarSeparatorData() : AppBarItemBase;

/// <summary>
/// Scopes keyboard accelerators from a set of commands to a subtree.
/// Accelerators are only active when the host or its descendants have focus.
/// </summary>
public record CommandHostElement(Command[] Commands, Element Child) : Element;

// ════════════════════════════════════════════════════════════════════════
//  Text elements
// ════════════════════════════════════════════════════════════════════════

[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.TextBlock), ClearValueOnUnset = true)]  // spec 058 §15 (P5.7); nullable styling props use the dp ClearValue channel (issue #522 recycle reset)
[global::Microsoft.UI.Reactor.Wrappers.WrapAlias("Content", "Text")]
[global::Microsoft.UI.Reactor.Wrappers.WrapAlias("Weight", "FontWeight")]
public partial record TextBlockElement(string Content) : Element
{
    public double? FontSize { get; init; }
    public FontWeight? Weight { get; init; }
    public global::Windows.UI.Text.FontStyle? FontStyle { get; init; }
    public HorizontalAlignment? HorizontalAlignment { get; init; }
    public TextWrapping? TextWrapping { get; init; }
    public TextAlignment? TextAlignment { get; init; }
    public TextTrimming? TextTrimming { get; init; }
    public bool? IsTextSelectionEnabled { get; init; }
    public Microsoft.UI.Xaml.Media.FontFamily? FontFamily { get; init; }
    /// <summary>Line height in pixels. <c>null</c> uses the WinUI default (proportional to FontSize).</summary>
    public double? LineHeight { get; init; }
    /// <summary>Maximum number of lines to render before truncating per <see cref="TextTrimming"/>. <c>0</c> (default) = no limit.</summary>
    public int MaxLines { get; init; }
    /// <summary>Extra spacing between characters, in units of 1/1000em. Defaults to <c>0</c> (no extra spacing).</summary>
    public int CharacterSpacing { get; init; }
    /// <summary>Bitmask of underline / strikethrough decorations. Default <c>None</c>.</summary>
    public global::Windows.UI.Text.TextDecorations TextDecorations { get; init; } = global::Windows.UI.Text.TextDecorations.None;
    internal Action<WinUI.TextBlock>[] Setters { get; init; } = [];

    /// <summary>
    /// EXP-2: Bitmask diff — compare two TextBlockElement instances (pure C#, no COM interop)
    /// and return which properties actually changed. Callers only touch WinUI for set bits.
    /// </summary>
    internal static TextPropChanged DiffProps(TextBlockElement old, TextBlockElement cur)
    {
        var diff = TextPropChanged.None;
        if (old.Content != cur.Content) diff |= TextPropChanged.Content;
        if (old.FontSize != cur.FontSize) diff |= TextPropChanged.FontSize;
        if (old.Weight != cur.Weight) diff |= TextPropChanged.Weight;
        if (old.FontStyle != cur.FontStyle) diff |= TextPropChanged.FontStyle;
        if (old.HorizontalAlignment != cur.HorizontalAlignment) diff |= TextPropChanged.HorizontalAlignment;
        if (old.TextWrapping != cur.TextWrapping) diff |= TextPropChanged.TextWrapping;
        if (old.TextAlignment != cur.TextAlignment) diff |= TextPropChanged.TextAlignment;
        if (old.TextTrimming != cur.TextTrimming) diff |= TextPropChanged.TextTrimming;
        if (old.IsTextSelectionEnabled != cur.IsTextSelectionEnabled) diff |= TextPropChanged.IsTextSelectionEnabled;
        if (old.FontFamily != cur.FontFamily) diff |= TextPropChanged.FontFamily;
        if (old.LineHeight != cur.LineHeight) diff |= TextPropChanged.LineHeight;
        if (old.MaxLines != cur.MaxLines) diff |= TextPropChanged.MaxLines;
        if (old.CharacterSpacing != cur.CharacterSpacing) diff |= TextPropChanged.CharacterSpacing;
        if (old.TextDecorations != cur.TextDecorations) diff |= TextPropChanged.TextDecorations;
        if (old.Setters.Length != cur.Setters.Length) diff |= TextPropChanged.Setters;
        else if (cur.Setters.Length > 0) diff |= TextPropChanged.Setters; // can't compare delegates
        return diff;
    }
}

[Flags]
internal enum TextPropChanged : ushort
{
    None                = 0,
    Content             = 1 << 0,
    FontSize            = 1 << 1,
    Weight              = 1 << 2,
    FontStyle           = 1 << 3,
    HorizontalAlignment = 1 << 4,
    TextWrapping        = 1 << 5,
    TextAlignment       = 1 << 6,
    TextTrimming        = 1 << 7,
    IsTextSelectionEnabled = 1 << 8,
    FontFamily          = 1 << 9,
    Setters             = 1 << 10,
    LineHeight          = 1 << 11,
    MaxLines            = 1 << 12,
    CharacterSpacing    = 1 << 13,
    TextDecorations     = 1 << 14,
}

// Spec 058 §15 (P5.8) — most styling props auto-map (nullable → dp ClearValue channel
// via ClearValueOnUnset). Text/Paragraphs are a bespoke block-list build/diff
// (ImperativeBridged) and Padding (a base Element modifier) are wired in Customize.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.RichTextBlock), ClearValueOnUnset = true)]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Text")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Paragraphs")]
public partial record RichTextBlockElement(string Text) : Element
{
    public double? FontSize { get; init; }
    public Microsoft.UI.Xaml.Media.FontFamily? FontFamily { get; init; }
    public global::Windows.UI.Text.FontWeight? FontWeight { get; init; }
    public global::Windows.UI.Text.FontStyle? FontStyle { get; init; }
    public global::Windows.UI.Text.FontStretch? FontStretch { get; init; }
    public Brush? Foreground { get; init; }
    public RichTextParagraph[]? Paragraphs { get; init; }
    public bool IsTextSelectionEnabled { get; init; }
    public TextWrapping? TextWrapping { get; init; }
    /// <summary>Maximum number of lines before trimming per <see cref="TextTrimming"/>. <c>0</c> (default) = no limit.</summary>
    public int MaxLines { get; init; }
    /// <summary>Line height in pixels. <c>null</c> uses the WinUI default (proportional to FontSize).</summary>
    public double? LineHeight { get; init; }
    /// <summary>Horizontal alignment of text within the block. <c>null</c> uses the WinUI default (Left).</summary>
    public TextAlignment? TextAlignment { get; init; }
    /// <summary>Horizontal text alignment using the newer WinUI property. <c>null</c> uses the WinUI default.</summary>
    public TextAlignment? HorizontalTextAlignment { get; init; }
    /// <summary>How overflowing text is truncated. <c>null</c> uses the WinUI default (None).</summary>
    public TextTrimming? TextTrimming { get; init; }
    /// <summary>Extra spacing between characters, in units of 1/1000em. Defaults to <c>0</c>.</summary>
    public int CharacterSpacing { get; init; }
    public global::Windows.UI.Text.TextDecorations? TextDecorations { get; init; }
    public LineStackingStrategy? LineStackingStrategy { get; init; }
    public double? TextIndent { get; init; }
    public TextLineBounds? TextLineBounds { get; init; }
    public TextReadingOrder? TextReadingOrder { get; init; }
    public bool? IsTextScaleFactorEnabled { get; init; }
    public bool? IsColorFontEnabled { get; init; }
    public OpticalMarginAlignment? OpticalMarginAlignment { get; init; }
    public Microsoft.UI.Xaml.Media.SolidColorBrush? SelectionHighlightColor { get; init; }
    internal Action<WinUI.RichTextBlock>[] Setters { get; init; } = [];

    // Text/Paragraphs: incremental block-list build/diff (preserves Route A inline
    // UI children across renders — issue #480). Padding: the standard Element.Padding
    // modifier mapped to RichTextBlock.Padding via the dp ClearValue channel.
    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<RichTextBlockElement, WinUI.RichTextBlock> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<RichTextBlockElement, WinUI.RichTextBlock> d)
        => d.ImperativeBridged(
                mount:  static (ctx, c, e) => ctx.Reconciler.RebuildRichTextBlocks(e, c, ctx.RequestRerender),
                update: static (ctx, c, prev, next) => ctx.Reconciler.UpdateRichTextBlocks(c, prev, next, ctx.RequestRerender))
            .OneWay(
                get: static e => e.Padding.HasValue ? e.Padding.Value : global::Microsoft.UI.Reactor.Optional<Thickness>.Unset,
                set: static (c, v) => c.Padding = v,
                dp:  WinUI.RichTextBlock.PaddingProperty);
}

// Rich text inline content types
public record RichTextParagraph(RichTextInline[] Inlines)
{
    public Thickness? Margin { get; init; }
    public double? TextIndent { get; init; }
    public TextAlignment? TextAlignment { get; init; }
    public TextAlignment? HorizontalTextAlignment { get; init; }
    public double? LineHeight { get; init; }
    public LineStackingStrategy? LineStackingStrategy { get; init; }
    public double? FontSize { get; init; }
    public string? FontFamily { get; init; }
    public global::Windows.UI.Text.FontWeight? FontWeight { get; init; }
    public global::Windows.UI.Text.FontStyle? FontStyle { get; init; }
    public global::Windows.UI.Text.FontStretch? FontStretch { get; init; }
    public Brush? Foreground { get; init; }
    public int? CharacterSpacing { get; init; }
    public global::Windows.UI.Text.TextDecorations? TextDecorations { get; init; }
    public bool? IsTextScaleFactorEnabled { get; init; }
    public string? Language { get; init; }
}

public abstract record RichTextInline
{
    public double? FontSize { get; init; }
    public string? FontFamily { get; init; }
    public global::Windows.UI.Text.FontWeight? FontWeight { get; init; }
    public global::Windows.UI.Text.FontStyle? FontStyle { get; init; }
    public global::Windows.UI.Text.FontStretch? FontStretch { get; init; }
    public Brush? Foreground { get; init; }
    public int? CharacterSpacing { get; init; }
    public global::Windows.UI.Text.TextDecorations? TextDecorations { get; init; }
    public bool? IsTextScaleFactorEnabled { get; init; }
    public string? Language { get; init; }
}

public record RichTextRun(string Text) : RichTextInline
{
    public bool IsBold { get; init; }
    public bool IsItalic { get; init; }
    public bool IsStrikethrough { get; init; }
    public FlowDirection? FlowDirection { get; init; }
}

/// <summary>
/// Inline hyperlink fragment inside a <see cref="RichTextBlockElement"/>.
///
/// <para><b>Two modes</b> (issue #479):
/// <list type="bullet">
///   <item><b>Navigate mode</b> — when <see cref="OnClick"/> is <c>null</c>
///   the WinUI <c>Hyperlink</c> is mounted with <see cref="NavigateUri"/>
///   set and clicks open the URI via the platform launcher.</item>
///   <item><b>Click mode</b> — when <see cref="OnClick"/> is non-null the
///   delegate fires on click and the WinUI <c>NavigateUri</c> is left
///   unset, so the platform does not navigate. Use this for clickable
///   inline fragments (open a menu, enter edit mode, dispatch an action)
///   without escaping to a hosted native subtree.</item>
/// </list></para>
/// </summary>
public record RichTextHyperlink(string Text, Uri NavigateUri) : RichTextInline
{
    /// <summary>
    /// Optional click handler. When non-null, fires on every click and
    /// suppresses navigation (the underlying WinUI <c>Hyperlink</c> is
    /// mounted with no <c>NavigateUri</c>). When null, the inline behaves
    /// as a pure navigation link using <see cref="NavigateUri"/>.
    /// </summary>
    public Action? OnClick { get; init; }
    public Microsoft.UI.Xaml.Documents.UnderlineStyle? UnderlineStyle { get; init; }
    public bool? IsTabStop { get; init; }
    public int? TabIndex { get; init; }
}

public record RichTextLineBreak() : RichTextInline;

/// <summary>
/// Mirrors WinUI's <c>Microsoft.UI.Xaml.Documents.InlineUIContainer</c> — an
/// inline that embeds a UIElement inside a flowing <see cref="RichTextBlockElement"/>.
///
/// <para><b>Two embedding routes</b> (issue #480):
/// <list type="bullet">
///   <item><b>Route A — Reactor element (<see cref="Child"/>):</b> the
///   embedded UI is described declaratively as any other Reactor
///   <see cref="Element"/> (e.g. <c>Button("+", () =&gt; ...)</c>) and is mounted
///   through the reconciler. The descriptor uses an incremental rich-text
///   update path: re-renders of the owning <c>RichTextBlock</c> preserve
///   the existing inline UIElement and reconcile the Reactor child via
///   the standard child-reconciliation pipeline, so embedded interactive
///   controls (sliders, buttons, etc.) keep their drag focus and
///   component state across renders. A structural change to the
///   surrounding paragraph tree (different paragraph count, mismatched
///   inline shape) falls back to a full rebuild that tears down and
///   remounts the child along with the rest of the block.</item>
///   <item><b>Route B — imperative native factory (<see cref="Factory"/>):</b>
///   the embedded UI is produced by a caller-supplied lambda returning a
///   raw WinUI <see cref="Microsoft.UI.Xaml.FrameworkElement"/>. Useful as
///   an escape hatch for native controls that have no Reactor element
///   counterpart. The factory is opaque to the reconciler, so the
///   incremental path re-invokes it only when the delegate identity
///   itself changes (capture-equal lambdas are treated as unchanged) —
///   pass a stable delegate reference if you want the native UIElement
///   to survive renders.</item>
/// </list></para>
///
/// <para>Exactly one of <see cref="Child"/> / <see cref="Factory"/> should
/// be non-null. If both are null the inline expands to a zero-size empty
/// container; if both are set, <see cref="Child"/> wins.</para>
/// </summary>
public record RichTextInlineUIContainer : RichTextInline
{
    /// <summary>Reactor element to mount as the inline UI (Route A).</summary>
    public Element? Child { get; init; }
    /// <summary>Imperative factory producing a native <see cref="FrameworkElement"/>
    /// (Route B). Invoked once per rebuild of the owning <c>RichTextBlock</c>.</summary>
    public Func<FrameworkElement>? Factory { get; init; }
}

// ════════════════════════════════════════════════════════════════════════
//  Button elements
// ════════════════════════════════════════════════════════════════════════

// Spec 058 §15 (P5.22) — fully bespoke (everything in Customize). Polymorphic content: a guarded
// SingleContent over ContentElement (overwrites the auto unguarded one so a Label string isn't
// clobbered) + Label OneWayConditional gated on ContentElement is null. IsEnabled gated on
// !IsDisabledFocusable; IsDisabledFocusable coerces IsEnabled=true + Opacity. Click suppresses when
// IsDisabledFocusable (Excluded so it isn't auto-surfaced). Replaces ButtonDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.Button), Exclude = new[] { "Click" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Label")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("IsEnabled")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("IsDisabledFocusable")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Command")]
public partial record ButtonElement(string Label, Action? OnClick = null) : Element
{
    public bool IsEnabled { get; init; } = true;
    /// <summary>
    /// When true, the button is visually dimmed and its <c>OnClick</c> handler
    /// is suppressed, but it stays keyboard-focusable and reachable via Tab.
    /// Use for submit buttons gated on validation so users can discover them
    /// and the disable state doesn't trap keyboard navigation through commit-
    /// on-blur inputs. Conceptually equivalent to Fluent UI React's
    /// <c>disabledFocusable</c> / ARIA's <c>aria-disabled</c>. UIA still
    /// reports the button as enabled — full assistive-tech "unavailable"
    /// reporting requires a custom <c>ButtonAutomationPeer</c> and is tracked
    /// as a follow-up.
    /// </summary>
    public bool IsDisabledFocusable { get; init; }
    public Element? ContentElement { get; init; }
    /// <summary>
    /// The <see cref="Command"/> bound to this button via the <c>Button(Command)</c> factory,
    /// the <c>.Command()</c> modifier, or a direct <c>new ButtonElement(cmd.Label) { Command = cmd }</c> /
    /// <c>with { Command = cmd }</c> record-init (issue #153, made fully uniform in issue #637).
    /// Carries the command so the reconciler can compare it field-aware in
    /// <see cref="Element.ShallowEquals"/> and apply its metadata + enabled state through a
    /// descriptor entry — no per-render <see cref="Setters"/> lambda. <b>Every</b> binding path is
    /// now equivalent: dispatch (Execute/ExecuteAsync) is invoked by the click trampoline when
    /// <see cref="OnClick"/> is null, <see cref="Command.IsEnabled"/> is folded into the
    /// IsEnabled descriptor entry (see <see cref="EffectiveIsEnabled"/>), and the command counts
    /// as a callback source for <see cref="HasCallbacks"/>. The <c>init</c> accessor is public:
    /// a bare record-init invokes the command on click exactly like the factory/modifier.
    /// </summary>
    public Command? Command { get; init; }
    internal Action<WinUI.Button>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => global::Microsoft.UI.Reactor.Core.CommandBindings.EffectiveCallback(OnClick, Command) is not null;

    /// <summary>
    /// The IsEnabled value written to the live control (gated on <c>!IsDisabledFocusable</c>):
    /// the record <see cref="IsEnabled"/> AND the bound command's <see cref="Command.IsEnabled"/>.
    /// Folding the command's enabled state in <em>here</em> — rather than baking it into the
    /// record <see cref="IsEnabled"/> field in the factory/modifier — makes every binding path
    /// (factory, <c>.Command()</c> modifier, and a bare <c>new ButtonElement(cmd.Label) { Command = cmd }</c>)
    /// drive IsEnabled identically, while the <c>!IsDisabledFocusable</c> coercion (a
    /// disabled-focusable button stays <c>IsEnabled=true</c>, reachable via Tab) is preserved
    /// exactly by the unchanged <c>shouldWrite</c> gate and IsDisabledFocusable handler (issue #637).
    /// </summary>
    internal bool EffectiveIsEnabled => IsEnabled && (Command?.IsEnabled ?? true);

    private static readonly global::Microsoft.UI.Xaml.RoutedEventHandler __ClickTrampoline = (s, _) =>
    {
        if (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag((WinUI.Button)s!) is ButtonElement live)
        {
            if (live.IsDisabledFocusable) return;
            if (live.OnClick is not null) live.OnClick();
            else if (live.Command is not null) global::Microsoft.UI.Reactor.Core.CommandBindings.Invoke(live.Command);
        }
    };

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ButtonElement, WinUI.Button> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ButtonElement, WinUI.Button> d)
    {
        // Guarded polymorphic content: SetChild only writes a non-null mounted child, so the
        // string Label written by the gated OneWayConditional below is never clobbered.
        d.Children = new global::Microsoft.UI.Reactor.Core.V1Protocol.SingleContent<ButtonElement, WinUI.Button>(
            GetChild: static e => e.ContentElement,
            SetChild: static (c, ui) => { if (ui is not null) c.Content = ui; })
        {
            GetCurrentChild = static c => c.Content as global::Microsoft.UI.Xaml.UIElement,
        };
        return d
            .OneWayConditional(
                get:         static e => e.Label,
                set:         static (c, v) => c.Content = v,
                shouldWrite: static e => e.ContentElement is null)
            .OneWayConditional(
                get:         static e => e.EffectiveIsEnabled,
                set:         static (c, v) => c.IsEnabled = v,
                shouldWrite: static e => !e.IsDisabledFocusable)
            .OneWay<bool>(
                get: static e => e.IsDisabledFocusable,
                set: static (c, v) =>
                {
                    if (v)
                    {
                        c.IsEnabled = true;
                        c.Opacity = 0.4;
                    }
                    else
                    {
                        c.ClearValue(global::Microsoft.UI.Xaml.UIElement.OpacityProperty);
                    }
                })
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.ButtonEventPayload, global::Microsoft.UI.Xaml.RoutedEventHandler>(
                subscribe:        static (c, h) => c.Click += h,
                callbackPresent:  static e => global::Microsoft.UI.Reactor.Core.CommandBindings.EffectiveCallback(e.OnClick, e.Command),
                trampoline:       __ClickTrampoline,
                slotIsNull:       static p => p.ClickTrampoline is null,
                setSlot:          static (p, h) => p.ClickTrampoline = h)
            // issue #153 — command metadata (tooltip / accelerator / access key) applied
            // field-aware. applyIsEnabled:false: the IsEnabled OneWayConditional above already
            // drives the control (gated on !IsDisabledFocusable) via EffectiveIsEnabled, so the
            // command must not clobber the disabled-focusable coercion. Re-applied only when the
            // Command changes in a rendered field (delegates ignored — CommandModuloDelegatesComparer).
            .OneWayCommand(static e => e.Command, applyIsEnabled: false);
    }
}

// Spec 058 §15 (P5.4). Click is Excluded + hand-coded (issue #637) so a button bound only via the
// typed Command property still dispatches: the trampoline invokes OnClick when present, else the
// command. (Was an auto-surfaced Click→OnClick before #637.)
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.HyperlinkButton), Exclude = new[] { "Click" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapAlias("Content", "Content")]  // Content as value (not child slot)
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Command")]
public partial record HyperlinkButtonElement(string Content, Uri? NavigateUri = null, Action? OnClick = null) : Element
{
    /// <summary>The command bound via <c>HyperlinkButton(Command)</c> / <c>.Command()</c> / a direct
    /// <c>new HyperlinkButtonElement(cmd.Label) { Command = cmd }</c> record-init (issue #153; made uniform in
    /// issue #637). All paths dispatch + apply IsEnabled identically. See <see cref="ButtonElement.Command"/>.</summary>
    public Command? Command { get; init; }
    internal Action<WinUI.HyperlinkButton>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => global::Microsoft.UI.Reactor.Core.CommandBindings.EffectiveCallback(OnClick, Command) is not null;

    private static readonly global::Microsoft.UI.Xaml.RoutedEventHandler __ClickTrampoline = (s, _) =>
    {
        if (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag((WinUI.HyperlinkButton)s!) is HyperlinkButtonElement live)
        {
            if (live.OnClick is not null) live.OnClick();
            else if (live.Command is not null) global::Microsoft.UI.Reactor.Core.CommandBindings.Invoke(live.Command);
        }
    };

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<HyperlinkButtonElement, WinUI.HyperlinkButton> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<HyperlinkButtonElement, WinUI.HyperlinkButton> d)
        => d.HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.HyperlinkButtonEventPayload, global::Microsoft.UI.Xaml.RoutedEventHandler>(
                subscribe:        static (c, h) => c.Click += h,
                callbackPresent:  static e => global::Microsoft.UI.Reactor.Core.CommandBindings.EffectiveCallback(e.OnClick, e.Command),
                trampoline:       __ClickTrampoline,
                slotIsNull:       static p => p.ClickTrampoline is null,
                setSlot:          static (p, h) => p.ClickTrampoline = h)
            .OneWayCommand(static e => e.Command);  // issue #153 — command metadata + IsEnabled applied field-aware
}

// Spec 058 §15 (P5.4). Click Excluded + hand-coded (issue #637) — see HyperlinkButtonElement.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinPrim.RepeatButton), Exclude = new[] { "Click" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapAlias("Label", "Content")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Command")]
public partial record RepeatButtonElement(string Label, Action? OnClick = null) : Element
{
    public int Delay { get; init; } = 250;
    public int Interval { get; init; } = 50;
    /// <summary>The command bound via <c>RepeatButton(Command)</c> / <c>.Command()</c> / a direct
    /// <c>new RepeatButtonElement(cmd.Label) { Command = cmd }</c> record-init (issue #153; made uniform in
    /// issue #637). All paths dispatch + apply IsEnabled identically. See <see cref="ButtonElement.Command"/>.</summary>
    public Command? Command { get; init; }
    internal Action<WinPrim.RepeatButton>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => global::Microsoft.UI.Reactor.Core.CommandBindings.EffectiveCallback(OnClick, Command) is not null;

    private static readonly global::Microsoft.UI.Xaml.RoutedEventHandler __ClickTrampoline = (s, _) =>
    {
        if (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag((WinPrim.RepeatButton)s!) is RepeatButtonElement live)
        {
            if (live.OnClick is not null) live.OnClick();
            else if (live.Command is not null) global::Microsoft.UI.Reactor.Core.CommandBindings.Invoke(live.Command);
        }
    };

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<RepeatButtonElement, WinPrim.RepeatButton> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<RepeatButtonElement, WinPrim.RepeatButton> d)
        => d.HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.RepeatButtonEventPayload, global::Microsoft.UI.Xaml.RoutedEventHandler>(
                subscribe:        static (c, h) => c.Click += h,
                callbackPresent:  static e => global::Microsoft.UI.Reactor.Core.CommandBindings.EffectiveCallback(e.OnClick, e.Command),
                trampoline:       __ClickTrampoline,
                slotIsNull:       static p => p.ClickTrampoline is null,
                setSlot:          static (p, h) => p.ClickTrampoline = h)
            .OneWayCommand(static e => e.Command);  // issue #153 — command metadata + IsEnabled applied field-aware
}

// Spec 058 §15 (P5.22) — Label→Content ([WrapAlias]). IsThreeState/IsChecked/CheckedState +
// Click are bespoke in Customize: IsThreeState is written BEFORE IsChecked (ordering invariant),
// IsChecked's source is CheckedState in 3-state mode else IsChecked, and Click reads back the
// control value firing both callbacks. No event auto-surfaces (no OnClick). Replaces ToggleButtonDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinPrim.ToggleButton))]
[global::Microsoft.UI.Reactor.Wrappers.WrapAlias("Label", "Content")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("IsThreeState")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("IsChecked")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("CheckedState")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Command")]
public partial record ToggleButtonElement(string Label, bool IsChecked = false, Action<bool>? OnIsCheckedChanged = null) : Element
{
    /// <summary>
    /// Enable the three-state cycle (true → false → null → true). Pair with
    /// <see cref="CheckedState"/> and <see cref="OnCheckedStateChanged"/>; the
    /// non-nullable <see cref="IsChecked"/> primary is ignored in this mode.
    /// Mirrors the established <c>CheckBoxElement</c> precedent.
    /// </summary>
    public bool IsThreeState { get; init; }
    /// <summary>Three-state value (<c>null</c> = indeterminate). Active only when <see cref="IsThreeState"/> is true.</summary>
    public bool? CheckedState { get; init; }
    /// <summary>Three-state change handler. Receives <c>null</c> for indeterminate.</summary>
    public Action<bool?>? OnCheckedStateChanged { get; init; }
    /// <summary>The command bound via <c>ToggleButton(Command)</c> / <c>.Command()</c> / a direct
    /// <c>new ToggleButtonElement(cmd.Label) { Command = cmd }</c> record-init (issue #153; made uniform in
    /// issue #637). All paths fire the command on each toggle + apply IsEnabled identically.
    /// See <see cref="ButtonElement.Command"/>.</summary>
    public Command? Command { get; init; }
    internal Action<WinPrim.ToggleButton>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnIsCheckedChanged is not null || global::Microsoft.UI.Reactor.Core.CommandBindings.EffectiveCallback(OnCheckedStateChanged, Command) is not null;

    private static readonly global::Microsoft.UI.Xaml.RoutedEventHandler __ClickTrampoline = (s, _) =>
    {
        var t = (WinPrim.ToggleButton)s!;
        if (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(t) is not ToggleButtonElement live) return;
        if (live.OnIsCheckedChanged is not null || live.OnCheckedStateChanged is not null)
        {
            live.OnIsCheckedChanged?.Invoke(t.IsChecked ?? false);
            live.OnCheckedStateChanged?.Invoke(t.IsChecked);
        }
        else if (live.Command is not null)
        {
            global::Microsoft.UI.Reactor.Core.CommandBindings.Invoke(live.Command);
        }
    };

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ToggleButtonElement, WinPrim.ToggleButton> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ToggleButtonElement, WinPrim.ToggleButton> d)
        => d.OneWay(
                get: static e => e.IsThreeState,
                set: static (c, v) => c.IsThreeState = v)
            .OneWay<bool?>(
                get: static e => e.IsThreeState ? e.CheckedState : e.IsChecked,
                set: static (c, v) => c.IsChecked = v)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.ToggleButtonEventPayload, global::Microsoft.UI.Xaml.RoutedEventHandler>(
                subscribe:        static (c, h) => c.Click += h,
                callbackPresent:  static e => (Delegate?)e.OnIsCheckedChanged ?? global::Microsoft.UI.Reactor.Core.CommandBindings.EffectiveCallback(e.OnCheckedStateChanged, e.Command),
                trampoline:       __ClickTrampoline,
                slotIsNull:       static p => p.ClickTrampoline is null,
                setSlot:          static (p, h) => p.ClickTrampoline = h)
            .OneWayCommand(static e => e.Command);  // issue #153 — command metadata + IsEnabled applied field-aware
}

// Spec 058 §15 (P5.22) — Label→Content ([WrapAlias], which also suppresses the auto content slot
// so Flyout isn't mis-mapped to Content). Flyout is a [WrapManual] OneWayBridged (CreateFlyoutForDescriptor
// + reference-identity comparer). Children stay None. Replaces DropDownButtonDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.DropDownButton))]
[global::Microsoft.UI.Reactor.Wrappers.WrapAlias("Label", "Content")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Flyout")]
public partial record DropDownButtonElement(string Label, Element? Flyout = null) : Element
{
    internal Action<WinUI.DropDownButton>[] Setters { get; init; } = [];

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<DropDownButtonElement, WinUI.DropDownButton> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<DropDownButtonElement, WinUI.DropDownButton> d)
        => d.OneWayBridged<Element?>(
            get:         static e => e.Flyout,
            set:         static (c, v, rec, rr) => c.Flyout = rec.CreateFlyoutForDescriptor(v, rr),
            shouldWrite: static e => e.Flyout is not null,
            comparer:    global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors.ElementReferenceComparer.Instance);
}

// Spec 058 §15 (P5.22) — Label→Content ([WrapAlias], suppresses the auto content slot). Click
// (HandCodedEvent, Excluded so it isn't auto-surfaced) + Flyout (OneWayBridged) in Customize.
// Children stay None. Replaces SplitButtonDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.SplitButton), Exclude = new[] { "Click" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapAlias("Label", "Content")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Flyout")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Command")]
public partial record SplitButtonElement(string Label, Action? OnClick = null, Element? Flyout = null) : Element
{
    /// <summary>The command bound via <c>SplitButton(Command)</c> / a direct
    /// <c>new SplitButtonElement(cmd.Label) { Command = cmd }</c> record-init (issue #153; made uniform in
    /// issue #637). All paths dispatch the primary action + apply IsEnabled identically.
    /// See <see cref="ButtonElement.Command"/>.</summary>
    public Command? Command { get; init; }
    internal Action<WinUI.SplitButton>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => global::Microsoft.UI.Reactor.Core.CommandBindings.EffectiveCallback(OnClick, Command) is not null;

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.SplitButton, WinUI.SplitButtonClickEventArgs> __ClickTrampoline = (s, _) =>
    {
        if (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(s) is SplitButtonElement live)
        {
            if (live.OnClick is not null) live.OnClick();
            else if (live.Command is not null) global::Microsoft.UI.Reactor.Core.CommandBindings.Invoke(live.Command);
        }
    };

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<SplitButtonElement, WinUI.SplitButton> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<SplitButtonElement, WinUI.SplitButton> d)
        => d.HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.SplitButtonEventPayload, global::Windows.Foundation.TypedEventHandler<WinUI.SplitButton, WinUI.SplitButtonClickEventArgs>>(
                subscribe:        static (c, h) => c.Click += h,
                callbackPresent:  static e => global::Microsoft.UI.Reactor.Core.CommandBindings.EffectiveCallback(e.OnClick, e.Command),
                trampoline:       __ClickTrampoline,
                slotIsNull:       static p => p.ClickTrampoline is null,
                setSlot:          static (p, h) => p.ClickTrampoline = h)
            .OneWayBridged<Element?>(
                get:         static e => e.Flyout,
                set:         static (c, v, rec, rr) => c.Flyout = rec.CreateFlyoutForDescriptor(v, rr),
                shouldWrite: static e => e.Flyout is not null,
                comparer:    global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors.ElementReferenceComparer.Instance)
            .OneWayCommand(static e => e.Command);  // issue #153 — command metadata + IsEnabled applied field-aware
}

// Spec 058 §15 (P5.22) — Label→Content ([WrapAlias], suppresses the auto content slot). IsChecked
// (controlled via IsCheckedChanged — Excluded + handled in Customize) + Flyout (OneWayBridged).
// Children stay None. Replaces ToggleSplitButtonDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.ToggleSplitButton), Exclude = new[] { "IsCheckedChanged" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapAlias("Label", "Content")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("IsChecked")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Flyout")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Command")]
public partial record ToggleSplitButtonElement(string Label, Optional<bool> IsChecked = default, Action<bool>? OnIsCheckedChanged = null, Element? Flyout = null) : Element
{
    /// <summary>The command bound via <c>ToggleSplitButton(Command)</c> / a direct
    /// <c>new ToggleSplitButtonElement(cmd.Label) { Command = cmd }</c> record-init (issue #153; made uniform in
    /// issue #637). All paths fire the command on each toggle + apply IsEnabled identically.
    /// See <see cref="ButtonElement.Command"/>.</summary>
    public Command? Command { get; init; }
    internal Action<WinUI.ToggleSplitButton>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnIsCheckedChanged is not null || global::Microsoft.UI.Reactor.Core.CommandBindings.Invokable(Command) is not null;

    // issue #637 — non-null gate marker for a command-only binding (Command set, no
    // OnIsCheckedChanged). The HandCodedControlled `callback` selector is only null-checked
    // (to gate subscription + value-diff arming) and is NEVER invoked, so a shared static
    // sentinel adds zero per-event and per-reconcile allocation. Actual dispatch — including the
    // OnIsCheckedChanged-wins-over-Command precedence — lives in __IsCheckedChangedTrampoline.
    private static readonly Action<bool> __CommandGate = static _ => { };

    // issue #637 — static IsCheckedChanged trampoline (replaces the per-toggle `new Action<bool>`
    // the prior `.Controlled` callback allocated for command-only bindings). Reads the live element
    // off the attached ReactorState at fire time; honors the value-diff echo arm so a programmatic
    // controlled write does not re-enter the callback. Dispatch precedence matches the rest of the
    // command-capable elements: a user OnIsCheckedChanged wins, else the typed Command is invoked.
    private static readonly global::Windows.Foundation.TypedEventHandler<
        WinUI.ToggleSplitButton, WinUI.ToggleSplitButtonIsCheckedChangedEventArgs> __IsCheckedChangedTrampoline = (s, _) =>
    {
        var tsb = (WinUI.ToggleSplitButton)s!;
        var isChecked = tsb.IsChecked;
        if (!global::Microsoft.UI.Reactor.Core.Reconciler.TryGetReactorState(tsb, out var state)) return;
        if (global::Microsoft.UI.Reactor.Core.ChangeEchoSuppressor.ShouldSuppressEcho(state, isChecked)) return;
        if (state.Element is not ToggleSplitButtonElement live) return;
        if (live.OnIsCheckedChanged is not null) { live.OnIsCheckedChanged(isChecked); return; }
        var cmd = live.Command;
        if (global::Microsoft.UI.Reactor.Core.CommandBindings.Invokable(cmd) is not null)
            global::Microsoft.UI.Reactor.Core.CommandBindings.Invoke(cmd!);
    };

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ToggleSplitButtonElement, WinUI.ToggleSplitButton> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ToggleSplitButtonElement, WinUI.ToggleSplitButton> d)
        => d.HandCodedControlled<global::Microsoft.UI.Reactor.Core.V1Protocol.ToggleSplitButtonEventPayload, bool, global::Windows.Foundation.TypedEventHandler<WinUI.ToggleSplitButton, WinUI.ToggleSplitButtonIsCheckedChangedEventArgs>>(
                get:         static e => e.IsChecked,
                set:         static (c, v) => c.IsChecked = v,
                readBack:    static c => c.IsChecked,
                subscribe:   static (c, h) => c.IsCheckedChanged += h,
                // issue #637 — gate subscription on a callback OR an invokable Command (mirrors
                // HasCallbacks). The returned delegate is only null-checked, never called; dispatch
                // and the OnIsCheckedChanged-vs-Command precedence live in __IsCheckedChangedTrampoline.
                callback:    static e => e.OnIsCheckedChanged
                    ?? (global::Microsoft.UI.Reactor.Core.CommandBindings.Invokable(e.Command) is not null ? __CommandGate : null),
                trampoline:  __IsCheckedChangedTrampoline,
                slotIsNull:  static p => p.IsCheckedChangedTrampoline is null,
                setSlot:     static (p, h) => p.IsCheckedChangedTrampoline = h,
                valueDiffEcho: true)
            .OneWayBridged<Element?>(
                get:         static e => e.Flyout,
                set:         static (c, v, rec, rr) => c.Flyout = rec.CreateFlyoutForDescriptor(v, rr),
                shouldWrite: static e => e.Flyout is not null,
                comparer:    global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors.ElementReferenceComparer.Instance)
            .OneWayCommand(static e => e.Command);  // issue #153 — command metadata + IsEnabled applied field-aware
}

// ════════════════════════════════════════════════════════════════════════
//  Input elements
// ════════════════════════════════════════════════════════════════════════

/// <summary>Text box element. <c>Value</c> defaults to <see cref="Optional{T}.Unset"/> so user text is not overwritten unless a value is explicitly provided.</summary>
// Spec 058 §15 (P5.13) — Value→Text is a deferred controlled value (suppress-counter
// echo); AcceptsReturn/TextWrapping must precede the Text write (single-line \r\n strip);
// SelectionChanged is a 3-arg state-reading event. All four are handled in Customize
// (Customize entries are emitted first, preserving the order). The rest auto-map.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.TextBox), Exclude = new[] { "SelectionChanged" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Value")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("AcceptsReturn")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("TextWrapping")]
public partial record TextBoxElement(
    Optional<string> Value = default,
    Action<string>? OnChanged = null,
    string? PlaceholderText = null
) : Element
{
    public string? Header { get; init; }
    public bool? IsReadOnly { get; init; }
    public bool? AcceptsReturn { get; init; }
    public TextWrapping? TextWrapping { get; init; }
    /// <summary>Fires when the text selection changes. Receives (selectedText, selectionStart, selectionLength).</summary>
    public Action<string, int, int>? OnSelectionChanged { get; init; }
    /// <summary>Caret / selection start position. Set this to control where the caret sits after a text update.</summary>
    public int? SelectionStart { get; init; }
    /// <summary>Selection length. Set alongside SelectionStart to control the selection range.</summary>
    public int? SelectionLength { get; init; }
    /// <summary>Maximum number of characters allowed. <c>0</c> (default) means no limit.</summary>
    public int MaxLength { get; init; }
    /// <summary>Whether built-in spell-check is enabled. Defaults to the WinUI default (true).</summary>
    public bool? IsSpellCheckEnabled { get; init; }
    /// <summary>Forces input to upper/lower-case as the user types. Defaults to <c>Normal</c> (no transform).</summary>
    public CharacterCasing CharacterCasing { get; init; } = CharacterCasing.Normal;
    /// <summary>Horizontal text alignment within the box. Defaults to <c>Left</c>.</summary>
    public TextAlignment TextAlignment { get; init; } = TextAlignment.Left;
    /// <summary>Help text rendered below the box. WinUI 3 1.2+ feature.</summary>
    public string? Description { get; init; }
    internal Action<WinUI.TextBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnChanged is not null || OnSelectionChanged is not null;

    private static readonly WinUI.TextChangedEventHandler __TextChangedTrampoline = (s, _) =>
    {
        var tb = (WinUI.TextBox)s!;
        if (!global::Microsoft.UI.Reactor.Core.Reconciler.TryGetReactorState(tb, out var state)) return;
        if (global::Microsoft.UI.Reactor.Core.ChangeEchoSuppressor.ShouldSuppress(state)) return;
        (state.Element as TextBoxElement)?.OnChanged?.Invoke(tb.Text);
    };

    private static readonly global::Microsoft.UI.Xaml.RoutedEventHandler __SelectionChangedTrampoline = (s, _) =>
    {
        var tb = (WinUI.TextBox)s!;
        (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(tb) as TextBoxElement)
            ?.OnSelectionChanged?.Invoke(tb.SelectedText, tb.SelectionStart, tb.SelectionLength);
    };

    // AcceptsReturn/TextWrapping BEFORE Text (single-line strips embedded \r\n on the
    // Text assignment); Value→Text deferred HandCodedControlled; SelectionChanged
    // fire-only HandCodedEvent reading control selection state. Reused verbatim from the
    // hand-written descriptor (one shared TextBoxEventPayload across both events).
    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<TextBoxElement, WinUI.TextBox> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<TextBoxElement, WinUI.TextBox> d)
        => d.OneWayConditional(
                get:         static e => e.AcceptsReturn,
                set:         static (c, v) => c.AcceptsReturn = v!.Value,
                shouldWrite: static e => e.AcceptsReturn.HasValue)
            .OneWayConditional(
                get:         static e => e.TextWrapping,
                set:         static (c, v) => c.TextWrapping = v!.Value,
                shouldWrite: static e => e.TextWrapping.HasValue)
            .HandCodedControlled<global::Microsoft.UI.Reactor.Core.V1Protocol.TextBoxEventPayload, string, WinUI.TextChangedEventHandler>(
                get:         static e => e.Value,
                set:         static (c, v) => c.Text = v,
                readBack:    static c => c.Text,
                subscribe:   static (c, h) => c.TextChanged += h,
                callback:    static e => e.OnChanged,
                trampoline:  __TextChangedTrampoline,
                slotIsNull:  static p => p.TextChangedTrampoline is null,
                setSlot:     static (p, h) => p.TextChangedTrampoline = h)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.TextBoxEventPayload, global::Microsoft.UI.Xaml.RoutedEventHandler>(
                subscribe:        static (c, h) => c.SelectionChanged += h,
                callbackPresent:  static e => e.OnSelectionChanged,
                trampoline:       __SelectionChangedTrampoline,
                slotIsNull:       static p => p.SelectionChangedTrampoline is null,
                setSlot:          static (p, h) => p.SelectionChangedTrampoline = h);
}

/// <summary>Password box element. <c>Password</c> defaults to <see cref="Optional{T}.Unset"/> so user input is not overwritten unless a value is explicitly provided.</summary>
// Spec 058 §15 (P5.12) — Password uses the deferred / suppress-counter echo channel
// (HandCodedControlled) via [WrapControlled(Deferred=true)]; the rest auto-map.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.PasswordBox))]
[global::Microsoft.UI.Reactor.Wrappers.WrapControlled("Password", Deferred = true)]
public partial record PasswordBoxElement(
    Optional<string> Password = default,
    Action<string>? OnPasswordChanged = null,
    string? PlaceholderText = null
) : Element
{
    /// <summary>Maximum number of characters allowed. <c>0</c> (default) = no limit.</summary>
    public int MaxLength { get; init; }
    /// <summary>Optional label rendered above the box.</summary>
    public string? Header { get; init; }
    /// <summary>How the reveal button behaves. Defaults to <c>Peek</c> (matches WinUI).</summary>
    public PasswordRevealMode PasswordRevealMode { get; init; } = PasswordRevealMode.Peek;
    /// <summary>Character displayed in place of the entered password (default '●' bullet).</summary>
    public string? PasswordChar { get; init; }
    internal Action<WinUI.PasswordBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnPasswordChanged is not null;
}

/// <summary>Number box element. <c>Value</c> defaults to <see cref="Optional{T}.Unset"/> so the control owns its value until explicitly set.</summary>
/// <summary>NumberBox element. <c>Value</c> defaults to <see cref="Optional{T}.Unset"/>.</summary>
// Spec 058 §15 (P5.14) — Value is bespoke: deferred suppress-counter HandCodedControlled
// echo (ValueChanged) PLUS a per-keystroke .Immediate observation (TextProperty + Loaded →
// inner TextBox). Minimum/Maximum use CoercingOneWay (drop coercion-driven ValueChanged
// echoes). All three + the Immediate live in Customize (Min/Max BEFORE Value — coercion
// ordering invariant). SpinButtonPlacement→SpinButtonPlacementMode via [WrapAlias]; the
// other 9 props auto-map (incl. NumberFormatter — an INumberFormatter2 interface value).
// ValueChanged is Excluded (handled bespokely, would otherwise auto-surface as a
// fire-forget event and collide).
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.NumberBox), Exclude = new[] { "ValueChanged" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapAlias("SpinButtonPlacement", "SpinButtonPlacementMode")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Value")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Minimum")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Maximum")]
public partial record NumberBoxElement(
    Optional<double> Value = default,
    Action<double>? OnValueChanged = null,
    string? Header = null
) : Element
{
    public double Minimum { get; init; } = double.MinValue;
    public double Maximum { get; init; } = double.MaxValue;
    public string? PlaceholderText { get; init; }
    public NumberBoxSpinButtonPlacementMode SpinButtonPlacement { get; init; } = NumberBoxSpinButtonPlacementMode.Hidden;
    public double SmallChange { get; init; } = 1;
    public double LargeChange { get; init; } = 10;
    /// <summary>Custom number formatter (e.g. currency, percent). Null uses WinUI's default DecimalFormatter.</summary>
    public global::Windows.Globalization.NumberFormatting.INumberFormatter2? NumberFormatter { get; init; }
    /// <summary>Whether the user can type arithmetic expressions (e.g. <c>2*3+1</c>) that resolve on commit.</summary>
    public bool AcceptsExpression { get; init; }
    /// <summary>How invalid input is treated. Defaults to <c>InvalidInputOverwritten</c> (matches WinUI default).</summary>
    public NumberBoxValidationMode ValidationMode { get; init; } = NumberBoxValidationMode.InvalidInputOverwritten;
    /// <summary>Help text rendered below the box. WinUI 3 1.2+ feature.</summary>
    public string? Description { get; init; }
    internal Action<WinUI.NumberBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnValueChanged is not null;

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.NumberBox, WinUI.NumberBoxValueChangedEventArgs>
        __ValueChangedTrampoline = (s, _) =>
        {
            var box = (WinUI.NumberBox)s!;
            if (!global::Microsoft.UI.Reactor.Core.Reconciler.TryGetReactorState(box, out var state)) return;
            if (global::Microsoft.UI.Reactor.Core.ChangeEchoSuppressor.ShouldSuppress(state)) return;
            (state.Element as NumberBoxElement)?.OnValueChanged?.Invoke(box.Value);
        };

    // Min/Max BEFORE Value (Customize entries emit first) so a fresh in-range Value isn't
    // coerced by a stale range — the hand-coded arm's ordering invariant. CoercingOneWay
    // drops coercion-driven ValueChanged echoes; HandCodedControlled is the deferred
    // suppress-counter Value channel; .Immediate adds per-keystroke observation.
    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<NumberBoxElement, WinUI.NumberBox> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<NumberBoxElement, WinUI.NumberBox> d)
        => d.CoercingOneWay(
                get:               static e => e.Minimum,
                set:               static (c, v) => c.Minimum = v,
                coercesController: static (c, newMin) => c.Value < newMin)
            .CoercingOneWay(
                get:               static e => e.Maximum,
                set:               static (c, v) => c.Maximum = v,
                coercesController: static (c, newMax) => c.Value > newMax)
            .HandCodedControlled<global::Microsoft.UI.Reactor.Core.V1Protocol.NumberBoxEventPayload, double,
                global::Windows.Foundation.TypedEventHandler<WinUI.NumberBox, WinUI.NumberBoxValueChangedEventArgs>>(
                get:         static e => e.Value,
                set:         static (c, v) => c.Value = v,
                readBack:    static c => c.Value,
                subscribe:   static (c, h) => c.ValueChanged += h,
                callback:    static e => e.OnValueChanged,
                trampoline:  __ValueChangedTrampoline,
                slotIsNull:  static p => p.ValueChangedTrampoline is null,
                setSlot:     static (p, h) => p.ValueChangedTrampoline = h)
            .Immediate<global::Microsoft.UI.Reactor.Core.V1Protocol.NumberBoxEventPayload>(
                callbackGate:       static e => e.OnValueChanged,
                observeProperty:    WinUI.NumberBox.TextProperty,
                observeCallback:    global::Microsoft.UI.Reactor.Core.Reconciler.NumberBoxImmediateTextChanged,
                observeSlotIsNull:  static p => p.ImmediateTextChangedCallback is null,
                setObserveSlot:     static (p, h) => p.ImmediateTextChangedCallback = h,
                loadedHook:         global::Microsoft.UI.Reactor.Core.Reconciler.NumberBoxLoadedEnsureImmediateTextBox);
}

/// <summary>Auto-suggest box element. <c>Text</c> defaults to <see cref="Optional{T}.Unset"/> so typed text is not overwritten unless explicitly set.</summary>
// Spec 058 §15 (P5.13) — Text/QuerySubmitted/SuggestionChosen are bespoke (UserInput-
// filtered deferred Text echo, typed-arg projections, ToString coercion) sharing one
// AutoSuggestBoxEventPayload; Suggestions→ItemsSource and QueryIcon→IconResolver are
// bespoke too — all handled in Customize. PlaceholderText/Header/IsSuggestionListOpen auto-map.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.AutoSuggestBox), Exclude = new[] { "TextChanged", "QuerySubmitted", "SuggestionChosen" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Text")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Suggestions")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("QueryIcon")]
public partial record AutoSuggestBoxElement(
    Optional<string> Text = default,
    Action<string>? OnTextChanged = null,
    Action<string>? OnQuerySubmitted = null,
    Action<string>? OnSuggestionChosen = null
) : Element
{
    public string[] Suggestions { get; init; } = [];
    public string? PlaceholderText { get; init; }
    /// <summary>Optional label rendered above the box.</summary>
    public string? Header { get; init; }
    /// <summary>Icon rendered in the trailing query slot (e.g. a Search symbol).</summary>
    public IconData? QueryIcon { get; init; }
    /// <summary>Programmatically open or close the suggestion list. Defaults to <c>false</c>.</summary>
    public bool IsSuggestionListOpen { get; init; }
    internal Action<WinUI.AutoSuggestBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnTextChanged is not null || OnQuerySubmitted is not null || OnSuggestionChosen is not null;

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxTextChangedEventArgs>
        __TextChangedTrampoline = (s, args) =>
        {
            if (args.Reason != WinUI.AutoSuggestionBoxTextChangeReason.UserInput) return;
            var asb = (WinUI.AutoSuggestBox)s!;
            if (!global::Microsoft.UI.Reactor.Core.Reconciler.TryGetReactorState(asb, out var state)) return;
            if (global::Microsoft.UI.Reactor.Core.ChangeEchoSuppressor.ShouldSuppress(state)) return;
            (state.Element as AutoSuggestBoxElement)?.OnTextChanged?.Invoke(asb.Text);
        };

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxQuerySubmittedEventArgs>
        __QuerySubmittedTrampoline = (s, args) =>
            (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag((WinUI.AutoSuggestBox)s!) as AutoSuggestBoxElement)?.OnQuerySubmitted?.Invoke(args.QueryText);

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxSuggestionChosenEventArgs>
        __SuggestionChosenTrampoline = (s, args) =>
            (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag((WinUI.AutoSuggestBox)s!) as AutoSuggestBoxElement)?.OnSuggestionChosen?.Invoke(args.SelectedItem?.ToString() ?? "");

    // Suggestions BEFORE Text (items in place before any controlled Text echo); all
    // reused verbatim from the hand-written descriptor (shared AutoSuggestBoxEventPayload).
    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<AutoSuggestBoxElement, WinUI.AutoSuggestBox> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<AutoSuggestBoxElement, WinUI.AutoSuggestBox> d)
        => d.OneWayConditional(
                get:         static e => e.Suggestions,
                set:         static (c, v) => c.ItemsSource = v,
                shouldWrite: static e => e.Suggestions.Length > 0)
            .HandCodedControlled<global::Microsoft.UI.Reactor.Core.V1Protocol.AutoSuggestBoxEventPayload, string,
                global::Windows.Foundation.TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxTextChangedEventArgs>>(
                get:         static e => e.Text,
                set:         static (c, v) => c.Text = v,
                readBack:    static c => c.Text,
                subscribe:   static (c, h) => c.TextChanged += h,
                callback:    static e => e.OnTextChanged,
                trampoline:  __TextChangedTrampoline,
                slotIsNull:  static p => p.TextChangedTrampoline is null,
                setSlot:     static (p, h) => p.TextChangedTrampoline = h)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.AutoSuggestBoxEventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxQuerySubmittedEventArgs>>(
                subscribe:        static (c, h) => c.QuerySubmitted += h,
                callbackPresent:  static e => e.OnQuerySubmitted,
                trampoline:       __QuerySubmittedTrampoline,
                slotIsNull:       static p => p.QuerySubmittedTrampoline is null,
                setSlot:          static (p, h) => p.QuerySubmittedTrampoline = h)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.AutoSuggestBoxEventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxSuggestionChosenEventArgs>>(
                subscribe:        static (c, h) => c.SuggestionChosen += h,
                callbackPresent:  static e => e.OnSuggestionChosen,
                trampoline:       __SuggestionChosenTrampoline,
                slotIsNull:       static p => p.SuggestionChosenTrampoline is null,
                setSlot:          static (p, h) => p.SuggestionChosenTrampoline = h)
            .OneWayConditional(
                get:         static e => e.QueryIcon,
                set:         static (c, v) => c.QueryIcon = global::Microsoft.UI.Reactor.Core.V1Protocol.IconResolver.ResolveIconForDescriptor(v),
                shouldWrite: static e => e.QueryIcon is not null);
}

/// <summary>Check box element. <c>IsChecked</c> defaults to <see cref="Optional{T}.Unset"/> so the control owns its checked state until explicitly set.</summary>
// Spec 058 §15 (P5.22) — Label→Content ([WrapAlias]), IsThreeState auto. IsChecked is the
// multi-event controlled value (Checked/Unchecked/Indeterminate) with a dual callback
// (OnCheckedStateChanged bool? OR OnIsCheckedChanged bool when HasValue); CheckedState folds
// into that callback. Both bespoke → Customize (RadioButton precedent). Replaces CheckBoxDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.CheckBox))]
[global::Microsoft.UI.Reactor.Wrappers.WrapAlias("Label", "Content")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("IsChecked")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("CheckedState")]
public partial record CheckBoxElement(
    Optional<bool?> IsChecked = default,
    Action<bool>? OnIsCheckedChanged = null,
    string? Label = null
) : Element
{
    public bool IsThreeState { get; init; }
    public bool? CheckedState { get; init; }
    public Action<bool?>? OnCheckedStateChanged { get; init; }
    internal Action<WinUI.CheckBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnIsCheckedChanged is not null || OnCheckedStateChanged is not null;

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<CheckBoxElement, WinUI.CheckBox> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<CheckBoxElement, WinUI.CheckBox> d)
        => d.Controlled<bool?, global::Microsoft.UI.Xaml.RoutedEventArgs>(
            get:         static e => e.IsChecked,
            set:         static (c, v) => c.IsChecked = v,
            subscribe:   static (fe, h) =>
            {
                var cb = (WinUI.CheckBox)fe;
                cb.Checked       += (s, e) => h(s, e);
                cb.Unchecked     += (s, e) => h(s, e);
                cb.Indeterminate += (s, e) => h(s, e);
            },
            unsubscribe: static (fe, h) => { /* trampolines live for control lifetime — see CWT gate in PropEntry */ },
            callback:    static e => GetCheckedCallback(e),
            readBack:    static c => c.IsChecked);

    private static Action<bool?>? GetCheckedCallback(CheckBoxElement element)
    {
        if (element.OnCheckedStateChanged is not null)
            return element.OnCheckedStateChanged;
        if (element.OnIsCheckedChanged is null)
            return null;
        return value =>
        {
            if (value.HasValue)
                element.OnIsCheckedChanged(value.Value);
        };
    }
}

/// <summary>Radio button element. <c>IsChecked</c> defaults to <see cref="Optional{T}.Unset"/> so the control owns its checked state until explicitly set.</summary>
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.RadioButton))]  // spec 058 §15 (P5.4)
[global::Microsoft.UI.Reactor.Wrappers.WrapAlias("Label", "Content")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("IsChecked")]  // record bool vs control bool? — needs readBack ?? false
public partial record RadioButtonElement(
    string Label,
    Optional<bool> IsChecked = default,
    Action<bool>? OnIsCheckedChanged = null,
    string? GroupName = null
) : Element
{
    internal Action<WinUI.RadioButton>[] Setters { get; init; } = [];

    // IsChecked is controlled via Checked + Unchecked; the record models it as a
    // non-nullable bool, the control as bool? — so readBack bridges null → false.
    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<RadioButtonElement, WinUI.RadioButton> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<RadioButtonElement, WinUI.RadioButton> d)
        => d.Controlled<bool, global::Microsoft.UI.Xaml.RoutedEventArgs>(
            get:         static e => e.IsChecked,
            set:         static (c, v) => c.IsChecked = v,
            subscribe:   static (fe, h) =>
            {
                var rb = (WinUI.RadioButton)fe;
                rb.Checked   += (s, e) => h(s, e);
                rb.Unchecked += (s, e) => h(s, e);
            },
            unsubscribe: static (fe, h) => { },
            callback:    static e => e.OnIsCheckedChanged,
            readBack:    static c => c.IsChecked ?? false);
    internal override bool HasCallbacks => OnIsCheckedChanged is not null;
}

/// <summary>RadioButtons element. <c>SelectedIndex</c> defaults to <see cref="Optional{T}.Unset"/>; use <c>Optional.Of(-1)</c> to force no selection.</summary>
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.RadioButtons))]  // spec 058 §15 (P5.4)
[global::Microsoft.UI.Reactor.Wrappers.WrapControlled("SelectedIndex", Events = new[] { "SelectionChanged" })]
public partial record RadioButtonsElement(
    string[] Items,
    Optional<int> SelectedIndex = default,
    Action<int>? OnSelectedIndexChanged = null
) : Element
{
    public string? Header { get; init; }
    internal Action<WinUI.RadioButtons>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnSelectedIndexChanged is not null;
}

/// <summary>ComboBox element. <c>SelectedIndex</c> defaults to <see cref="Optional{T}.Unset"/>; use <c>Optional.Of(-1)</c> to force no selection.</summary>
// Spec 058 §15 (P5.21) — items control. Header/IsEditable/Description auto-map. The bespoke
// parts (all in Customize): the dual-source ItemsHost (ItemElements Element[] takes precedence
// over Items string[] — overwrites the auto single-source strategy), the value-diff SelectedIndex
// echo, the 2 DropDown events, PlaceholderText (?? "" clear-on-null), and MaxDropDownHeight (NaN
// sentinel). The 3 control events are Excluded. Replaces the hand-written ComboBoxDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.ComboBox), Exclude = new[] { "SelectionChanged", "DropDownOpened", "DropDownClosed" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("SelectedIndex")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("PlaceholderText")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("MaxDropDownHeight")]
public partial record ComboBoxElement(
    string[] Items,
    Optional<int> SelectedIndex = default,
    Action<int>? OnSelectedIndexChanged = null
) : Element
{
    public string? PlaceholderText { get; init; }
    public string? Header { get; init; }
    public bool IsEditable { get; init; }
    public Element[]? ItemElements { get; init; }
    /// <summary>Maximum pixel height of the open drop-down. <c>NaN</c> (default) uses the WinUI default.</summary>
    public double MaxDropDownHeight { get; init; } = double.NaN;
    /// <summary>Help text rendered below the box. WinUI 3 1.2+ feature.</summary>
    public string? Description { get; init; }
    /// <summary>Raised when the user opens the drop-down list.</summary>
    public Action? OnDropDownOpened { get; init; }
    /// <summary>Raised when the drop-down list closes (either by selection or dismissal).</summary>
    public Action? OnDropDownClosed { get; init; }
    internal Action<WinUI.ComboBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks =>
        OnSelectedIndexChanged is not null
        || OnDropDownOpened is not null
        || OnDropDownClosed is not null;

    private static readonly WinUI.SelectionChangedEventHandler __SelectionChangedTrampoline = (s, _) =>
    {
        var cb = (WinUI.ComboBox)s!;
        if (!global::Microsoft.UI.Reactor.Core.Reconciler.TryGetReactorState(cb, out var state)) return;
        if (global::Microsoft.UI.Reactor.Core.ChangeEchoSuppressor.ShouldSuppressEcho(state, cb.SelectedIndex)) return;
        (state.Element as ComboBoxElement)
            ?.OnSelectedIndexChanged?.Invoke(cb.SelectedIndex);
    };

    private static readonly global::System.EventHandler<object> __DropDownOpenedTrampoline = (s, _) =>
        (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag((WinUI.ComboBox)s!) as ComboBoxElement)?.OnDropDownOpened?.Invoke();

    private static readonly global::System.EventHandler<object> __DropDownClosedTrampoline = (s, _) =>
        (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag((WinUI.ComboBox)s!) as ComboBoxElement)?.OnDropDownClosed?.Invoke();

    // Dual-source items (ItemElements precedence — overwrites the generator's auto single-source
    // ItemsHost), value-diff SelectedIndex echo, 2 DropDown events, plus the ?? "" and NaN-sentinel
    // value props. Reproduced verbatim from the deleted descriptor.
    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ComboBoxElement, WinUI.ComboBox> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ComboBoxElement, WinUI.ComboBox> d)
    {
        d.Children = new global::Microsoft.UI.Reactor.Core.V1Protocol.ItemsHost<ComboBoxElement, WinUI.ComboBox>(
            GetItems: static e => e.ItemElements is not null
                ? (global::System.Collections.Generic.IReadOnlyList<object>)e.ItemElements
                : (global::System.Collections.Generic.IReadOnlyList<object>)e.Items,
            GetCollection: static c => c.Items);
        return d
            .HandCodedControlled<global::Microsoft.UI.Reactor.Core.V1Protocol.ComboBoxEventPayload, int, WinUI.SelectionChangedEventHandler>(
                get:         static e => e.SelectedIndex,
                set:         static (c, v) => c.SelectedIndex = v,
                readBack:    static c => c.SelectedIndex,
                subscribe:   static (c, h) => c.SelectionChanged += h,
                callback:    static e => e.OnSelectedIndexChanged,
                trampoline:  __SelectionChangedTrampoline,
                slotIsNull:  static p => p.SelectionChangedTrampoline is null,
                setSlot:     static (p, h) => p.SelectionChangedTrampoline = h,
                valueDiffEcho: true)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.ComboBoxEventPayload, global::System.EventHandler<object>>(
                subscribe:        static (c, h) => c.DropDownOpened += h,
                callbackPresent:  static e => e.OnDropDownOpened,
                trampoline:       __DropDownOpenedTrampoline,
                slotIsNull:       static p => p.DropDownOpenedTrampoline is null,
                setSlot:          static (p, h) => p.DropDownOpenedTrampoline = h)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.ComboBoxEventPayload, global::System.EventHandler<object>>(
                subscribe:        static (c, h) => c.DropDownClosed += h,
                callbackPresent:  static e => e.OnDropDownClosed,
                trampoline:       __DropDownClosedTrampoline,
                slotIsNull:       static p => p.DropDownClosedTrampoline is null,
                setSlot:          static (p, h) => p.DropDownClosedTrampoline = h)
            .OneWay(
                get: static e => e.PlaceholderText ?? "",
                set: static (c, v) => c.PlaceholderText = v)
            .OneWayConditional(
                get:         static e => e.MaxDropDownHeight,
                set:         static (c, v) => c.MaxDropDownHeight = v,
                shouldWrite: static e => !double.IsNaN(e.MaxDropDownHeight));
    }
}

/// <summary>Slider element. <c>Value</c> defaults to <see cref="Optional{T}.Unset"/> so the control owns its value until explicitly set.</summary>
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.Slider))]  // spec 058 §15 (P5.4)
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Min")]   // CoercingOneWay → Minimum
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Max")]   // CoercingOneWay → Maximum
public partial record SliderElement(
    Optional<double> Value = default,
    double Min = 0,
    double Max = 100,
    Action<double>? OnValueChanged = null
) : Element
{
    // Min/Max coerce: written BEFORE Value (Customize entries come first), so the
    // initial Value lands against the correct range. Value auto-pairs (controlled
    // via ValueChanged) in the generated auto entries.
    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<SliderElement, WinUI.Slider> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<SliderElement, WinUI.Slider> d)
        => d.CoercingOneWay(
                get:                static e => e.Min,
                set:                static (c, v) => c.Minimum = v,
                coercesController:  static (c, newMin) => c.Value < newMin)
            .CoercingOneWay(
                get:                static e => e.Max,
                set:                static (c, v) => c.Maximum = v,
                coercesController:  static (c, newMax) => c.Value > newMax);

    public double StepFrequency { get; init; } = 1;
    public string? Header { get; init; }
    /// <summary>Slider orientation. Defaults to <c>Orientation.Horizontal</c>.</summary>
    public Orientation Orientation { get; init; } = Orientation.Horizontal;
    /// <summary>Interval between tick marks on the slider's track. Defaults to <c>0</c> (no ticks).</summary>
    public double TickFrequency { get; init; }
    /// <summary>Where tick marks render relative to the track. Defaults to <c>TickPlacement.Inline</c>.</summary>
    public TickPlacement TickPlacement { get; init; } = TickPlacement.Inline;
    /// <summary>Whether the thumb snaps to ticks or step values during drag.</summary>
    public SliderSnapsTo SnapsTo { get; init; } = SliderSnapsTo.StepValues;
    /// <summary>Whether the floating value tooltip appears while dragging the thumb. Defaults to <c>true</c>.</summary>
    public bool IsThumbToolTipEnabled { get; init; } = true;
    internal Action<WinUI.Slider>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnValueChanged is not null;
}

/// <summary>Toggle switch element. <c>IsOn</c> defaults to <see cref="Optional{T}.Unset"/> so the control owns its state until explicitly set.</summary>
public partial record ToggleSwitchElement(
    Optional<bool> IsOn = default,
    Action<bool>? OnIsOnChanged = null,
    string? OnContent = null,
    string? OffContent = null
) : Element
{
    public string? Header { get; init; }
    internal Action<WinUI.ToggleSwitch>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnIsOnChanged is not null;
}

// Spec 058 §15 (P5.4) — descriptor-only migration. IsOn is controlled via the
// non-conventional Toggled event (no IsOnChanged); OnContent/OffContent/Header
// are one-way.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.ToggleSwitch))]
[global::Microsoft.UI.Reactor.Wrappers.WrapControlled("IsOn", ChangedEvent = "Toggled")]
public partial record ToggleSwitchElement;

/// <summary>Rating control element. <c>Value</c> defaults to <see cref="Optional{T}.Unset"/> so the control owns its rating until explicitly set.</summary>
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.RatingControl))]  // spec 058 §15 (P5.4)
public partial record RatingControlElement(
    Optional<double> Value = default,
    Action<double>? OnValueChanged = null
) : Element
{
    public int MaxRating { get; init; } = 5;
    public bool IsReadOnly { get; init; }
    public string? Caption { get; init; }
    /// <summary>Star value shown when the rating is unset. Defaults to -1 (no placeholder).</summary>
    public double PlaceholderValue { get; init; } = -1;
    /// <summary>Integer rating to assume when the user first interacts. Defaults to 1. (WinUI's <c>InitialSetValue</c> is int.)</summary>
    public int InitialSetValue { get; init; } = 1;
    internal Action<WinUI.RatingControl>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnValueChanged is not null;
}

/// <summary>Color picker element. <c>Color</c> defaults to <see cref="Optional{T}.Unset"/> so the control owns its color until explicitly set.</summary>
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.ColorPicker))]  // spec 058 §15 (P5.4)
public partial record ColorPickerElement(
    Optional<global::Windows.UI.Color> Color = default,
    Action<global::Windows.UI.Color>? OnColorChanged = null
) : Element
{
    public bool IsAlphaEnabled { get; init; }
    public bool IsMoreButtonVisible { get; init; }
    public bool IsColorSpectrumVisible { get; init; } = true;
    public bool IsColorSliderVisible { get; init; } = true;
    public bool IsColorChannelTextInputVisible { get; init; } = true;
    public bool IsHexInputVisible { get; init; } = true;
    /// <summary>Shape of the 2D color spectrum (Box or Ring). Defaults to <c>Box</c>.</summary>
    public ColorSpectrumShape ColorSpectrumShape { get; init; } = ColorSpectrumShape.Box;
    /// <summary>Minimum hue (0–359). Defaults to 0.</summary>
    public int MinHue { get; init; }
    /// <summary>Maximum hue (0–359). Defaults to 359.</summary>
    public int MaxHue { get; init; } = 359;
    /// <summary>Minimum saturation (0–100). Defaults to 0.</summary>
    public int MinSaturation { get; init; }
    /// <summary>Maximum saturation (0–100). Defaults to 100.</summary>
    public int MaxSaturation { get; init; } = 100;
    /// <summary>Minimum value/brightness (0–100). Defaults to 0.</summary>
    public int MinValue { get; init; }
    /// <summary>Maximum value/brightness (0–100). Defaults to 100.</summary>
    public int MaxValue { get; init; } = 100;
    internal Action<WinUI.ColorPicker>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnColorChanged is not null;
}

// ════════════════════════════════════════════════════════════════════════
//  Date / Time elements
// ════════════════════════════════════════════════════════════════════════

/// <summary>Calendar date picker element. <c>Date</c> defaults to <see cref="Optional{T}.Unset"/>; use an explicit <c>null</c> value to assert no date.</summary>
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.CalendarDatePicker))]  // spec 058 §15 (P5.4)
public partial record CalendarDatePickerElement(
    Optional<DateTimeOffset?> Date = default,
    Action<DateTimeOffset?>? OnDateChanged = null
) : Element
{
    public string? PlaceholderText { get; init; }
    public string? Header { get; init; }
    public DateTimeOffset? MinDate { get; init; }
    public DateTimeOffset? MaxDate { get; init; }
    /// <summary>Display format string for the picker's text (see WinUI <c>DateFormat</c> reference).</summary>
    public string? DateFormat { get; init; }
    /// <summary>Highlight today's date in the popup. Defaults to <c>true</c> (matches WinUI).</summary>
    public bool IsTodayHighlighted { get; init; } = true;
    /// <summary>Programmatically open or close the calendar popup.</summary>
    public bool IsCalendarOpen { get; init; }
    /// <summary>Show month/year group label headers in the popup. Defaults to <c>true</c>.</summary>
    public bool IsGroupLabelVisible { get; init; } = true;
    internal Action<WinUI.CalendarDatePicker>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnDateChanged is not null;
}

/// <summary>Date picker element. <c>Date</c> defaults to <see cref="Optional{T}.Unset"/> so the control owns its date until explicitly set.</summary>
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.DatePicker))]  // spec 058 §15 (P5.4)
public partial record DatePickerElement(
    Optional<DateTimeOffset> Date = default,
    Action<DateTimeOffset>? OnDateChanged = null
) : Element
{
    public string? Header { get; init; }
    public DateTimeOffset? MinYear { get; init; }
    public DateTimeOffset? MaxYear { get; init; }
    public bool DayVisible { get; init; } = true;
    public bool MonthVisible { get; init; } = true;
    public bool YearVisible { get; init; } = true;
    /// <summary>Display format string for the day column. <c>null</c> uses the WinUI default.</summary>
    public string? DayFormat { get; init; }
    /// <summary>Display format string for the month column. <c>null</c> uses the WinUI default.</summary>
    public string? MonthFormat { get; init; }
    /// <summary>Display format string for the year column. <c>null</c> uses the WinUI default.</summary>
    public string? YearFormat { get; init; }
    /// <summary>Layout direction of the picker. Defaults to <c>Horizontal</c>.</summary>
    public Orientation Orientation { get; init; } = Orientation.Horizontal;
    internal Action<WinUI.DatePicker>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnDateChanged is not null;
}

/// <summary>Time picker element. <c>Time</c> defaults to <see cref="Optional{T}.Unset"/> so the control owns its time until explicitly set.</summary>
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.TimePicker), Exclude = new[] { "ClockIdentifier" })]  // spec 058 §15 (P5.4); ClockIdentifier (int on record vs string on control) is intentionally unmapped by the hand-written descriptor
public partial record TimePickerElement(
    Optional<TimeSpan> Time = default,
    Action<TimeSpan>? OnTimeChanged = null
) : Element
{
    public string? Header { get; init; }
    public int MinuteIncrement { get; init; } = 1;
    public int ClockIdentifier { get; init; } = 12;
    internal Action<WinUI.TimePicker>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnTimeChanged is not null;
}

// ════════════════════════════════════════════════════════════════════════
//  Progress elements
// ════════════════════════════════════════════════════════════════════════

public partial record ProgressElement(double? Value = null) : Element  // null = indeterminate
{
    public bool IsIndeterminate => Value is null;
    public double Minimum { get; init; } = 0;
    public double Maximum { get; init; } = 100;
    public bool ShowError { get; init; }
    public bool ShowPaused { get; init; }
    internal Action<WinUI.ProgressBar>[] Setters { get; init; } = [];
}

// Spec 058 §15 (P5.4) — descriptor-only migration (replaces ProgressBarDescriptor).
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.ProgressBar))]
public partial record ProgressElement;

public partial record ProgressRingElement(double? Value = null) : Element
{
    public bool IsIndeterminate => Value is null;
    public double Minimum { get; init; } = 0;
    public double Maximum { get; init; } = 100;
    public bool IsActive { get; init; } = true;
    internal Action<WinUI.ProgressRing>[] Setters { get; init; } = [];
}

// Spec 058 §15 (P5.4) — descriptor-only migration. The hand-written
// ProgressRingDescriptor was deleted; the generator emits the descriptor from
// this record (non-nullable IsActive/Minimum/Maximum/IsIndeterminate → unconditional
// OneWay; nullable Value → OneWayConditional).
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.ProgressRing))]
public partial record ProgressRingElement;

// ════════════════════════════════════════════════════════════════════════
//  Media elements
// ════════════════════════════════════════════════════════════════════════

// Spec 058 §15 (P5.6) — Source needs bespoke string→Uri→BitmapImage/SvgImageSource
// parsing ([WrapManual]); ImageFailed is a typed event projecting ErrorMessage to
// Action<string> ([WrapEvent]). Stretch is string on the record vs Stretch enum on
// the control (unmapped by the legacy descriptor → Exclude). Width/Height/NineGrid +
// the parameterless ImageOpened auto-map.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.Image), Exclude = new[] { "Stretch" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Source")]
[global::Microsoft.UI.Reactor.Wrappers.WrapEvent("ImageFailed", Arg = "ErrorMessage")]
public partial record ImageElement(string Source) : Element
{
    public double? Width { get; init; }
    public double? Height { get; init; }
    public string? Stretch { get; init; }
    /// <summary>Raised after the image source loads successfully (marshalled to UI thread).</summary>
    public Action? OnImageOpened { get; init; }
    /// <summary>Raised when the image fails to load. Receives the failure message.</summary>
    public Action<string>? OnImageFailed { get; init; }
    /// <summary>Nine-grid (slice) values for resolution-independent corner stretching.</summary>
    public Thickness? NineGrid { get; init; }
    internal Action<WinUI.Image>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnImageOpened is not null || OnImageFailed is not null;

    // Source is a string parsed to a Uri then to BitmapImage (or SvgImageSource for
    // .svg); malformed URIs are swallowed (leave Source empty) — legacy parity.
    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ImageElement, WinUI.Image> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ImageElement, WinUI.Image> d)
        => d.OneWay(
            get: static e => e.Source,
            set: static (c, v) =>
            {
                try
                {
                    var uri = new Uri(v, UriKind.RelativeOrAbsolute);
                    c.Source = v.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                        ? new global::Microsoft.UI.Xaml.Media.Imaging.SvgImageSource(uri)
                        : new global::Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri);
                }
                catch (UriFormatException)
                {
                    // Mirror legacy: leave source empty rather than crashing.
                }
            });
}

public partial record PersonPictureElement() : Element
{
    public string? DisplayName { get; init; }
    public string? Initials { get; init; }
    public string? ProfilePicture { get; init; }
    public bool IsGroup { get; init; }
    public int BadgeNumber { get; init; }
    internal Action<WinUI.PersonPicture>[] Setters { get; init; } = [];
}

// Spec 058 §15 (P5.4) — descriptor-only migration. ProfilePicture needs a bespoke
// string→BitmapImage(Uri) conversion the generator can't infer, so it is handled
// via [WrapManual] + the Customize hook; the rest auto-map.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.PersonPicture))]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("ProfilePicture")]
public partial record PersonPictureElement
{
    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<PersonPictureElement, WinUI.PersonPicture> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<PersonPictureElement, WinUI.PersonPicture> d)
        => d.OneWayConditional<string>(
            static e => e.ProfilePicture!,
            static (c, v) => c.ProfilePicture = new global::Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new global::System.Uri(v, global::System.UriKind.RelativeOrAbsolute)),
            static e => e.ProfilePicture is not null);
}

// Spec 058 §15 (P5.16) — Source auto-maps but all 4 events are bespoke typed-arg
// trampolines (Uri parse, read control.Source, try/catch payload, parameterless), so
// they live in Customize as HandCodedEvents on the shared WebView2EventPayload. The 4
// control events are Excluded (would otherwise auto-surface as fire-forget and mismatch
// the Action<Uri>/Action<string>/Action callback shapes). Source is [WrapManual] only to
// trigger the Customize hook (it is the sole value prop).
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.WebView2), Exclude = new[] { "NavigationStarting", "NavigationCompleted", "WebMessageReceived", "CoreWebView2Initialized" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Source")]
public partial record WebView2Element(Uri? Source = null) : Element
{
    public Action<Uri>? OnNavigationStarting { get; init; }
    public Action<Uri>? OnNavigationCompleted { get; init; }

    /// <summary>
    /// Raised when the hosted page posts a message via
    /// <c>window.chrome.webview.postMessage(...)</c>. The callback receives the
    /// JSON payload as a string.
    ///
    /// Threading: messages dispatch on the UI thread (the WinUI WebView2 raises
    /// <c>WebMessageReceived</c> via the control's dispatcher), so the handler
    /// is safe to mutate component state from directly.
    /// </summary>
    public Action<string>? OnWebMessageReceived { get; init; }

    /// <summary>
    /// Raised once <c>CoreWebView2</c> initialization completes — the earliest
    /// point at which features like <c>AddScriptToExecuteOnDocumentCreatedAsync</c>
    /// or <c>AddHostObjectToScript</c> become available. Fires on the UI thread.
    /// </summary>
    public Action? OnCoreWebView2Initialized { get; init; }

    internal Action<WinUI.WebView2>[] Setters { get; init; } = [];
    internal override bool HasCallbacks =>
        OnNavigationStarting is not null
        || OnNavigationCompleted is not null
        || OnWebMessageReceived is not null
        || OnCoreWebView2Initialized is not null;

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.WebView2, global::Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs>
        __NavigationStartingTrampoline = (s, args) =>
        {
            if (global::System.Uri.TryCreate(args.Uri, global::System.UriKind.RelativeOrAbsolute, out var uri))
                (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(s) as WebView2Element)?.OnNavigationStarting?.Invoke(uri);
        };

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.WebView2, global::Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs>
        __NavigationCompletedTrampoline = (s, _) =>
            (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(s) as WebView2Element)?.OnNavigationCompleted?.Invoke(s.Source);

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.WebView2, global::Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs>
        __WebMessageReceivedTrampoline = (s, args) =>
        {
            if (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(s) is not WebView2Element { OnWebMessageReceived: { } handler }) return;
            string payload;
            try { payload = args.TryGetWebMessageAsString(); }
            catch { payload = args.WebMessageAsJson; }
            handler(payload);
        };

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.WebView2, WinUI.CoreWebView2InitializedEventArgs>
        __CoreInitializedTrampoline = (s, _) =>
            (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(s) as WebView2Element)?.OnCoreWebView2Initialized?.Invoke();

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<WebView2Element, WinUI.WebView2> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<WebView2Element, WinUI.WebView2> d)
        => d.HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.WebView2EventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.WebView2, global::Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs>>(
                subscribe:        static (c, h) => c.NavigationStarting += h,
                callbackPresent:  static e => e.OnNavigationStarting,
                trampoline:       __NavigationStartingTrampoline,
                slotIsNull:       static p => p.NavigationStartingTrampoline is null,
                setSlot:          static (p, h) => p.NavigationStartingTrampoline = h)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.WebView2EventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.WebView2, global::Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs>>(
                subscribe:        static (c, h) => c.NavigationCompleted += h,
                callbackPresent:  static e => e.OnNavigationCompleted,
                trampoline:       __NavigationCompletedTrampoline,
                slotIsNull:       static p => p.NavigationCompletedTrampoline is null,
                setSlot:          static (p, h) => p.NavigationCompletedTrampoline = h)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.WebView2EventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.WebView2, global::Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs>>(
                subscribe:        static (c, h) => c.WebMessageReceived += h,
                callbackPresent:  static e => e.OnWebMessageReceived,
                trampoline:       __WebMessageReceivedTrampoline,
                slotIsNull:       static p => p.WebMessageReceivedTrampoline is null,
                setSlot:          static (p, h) => p.WebMessageReceivedTrampoline = h)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.WebView2EventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.WebView2, WinUI.CoreWebView2InitializedEventArgs>>(
                subscribe:        static (c, h) => c.CoreWebView2Initialized += h,
                callbackPresent:  static e => e.OnCoreWebView2Initialized,
                trampoline:       __CoreInitializedTrampoline,
                slotIsNull:       static p => p.CoreInitializedTrampoline is null,
                setSlot:          static (p, h) => p.CoreInitializedTrampoline = h)
            .OneWayConditional(
                get:         static e => e.Source,
                set:         static (c, v) => c.Source = v!,
                shouldWrite: static e => e.Source is not null);
}

// ════════════════════════════════════════════════════════════════════════
//  Rich text elements
// ════════════════════════════════════════════════════════════════════════

/// <summary>Rich edit box element. <c>Text</c> defaults to <see cref="Optional{T}.Unset"/> so user text is not overwritten unless explicitly set.</summary>
// Spec 058 §15 (P5.13) — Text is a bespoke document-backed deferred controlled value
// (Document.SetText/GetText + suppress-counter echo) handled via [WrapManual] + Customize;
// the remaining props auto-map.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.RichEditBox), Exclude = new[] { "TextChanged" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Text")]
public partial record RichEditBoxElement(
    Optional<string> Text = default
) : Element
{
    public bool IsReadOnly { get; init; }
    public string? Header { get; init; }
    public string? PlaceholderText { get; init; }
    public Action<string>? OnTextChanged { get; init; }
    /// <summary>Whether built-in spell-check is enabled. Defaults to the WinUI default (true).</summary>
    public bool? IsSpellCheckEnabled { get; init; }
    /// <summary>Maximum number of characters allowed. <c>0</c> (default) = no limit.</summary>
    public int MaxLength { get; init; }
    /// <summary>How text wraps within the box. Defaults to <c>Wrap</c>.</summary>
    public TextWrapping TextWrapping { get; init; } = TextWrapping.Wrap;
    /// <summary>Whether Enter inserts a newline (vs committing). Defaults to <c>true</c>.</summary>
    public bool AcceptsReturn { get; init; } = true;
    /// <summary>Brush used to render the selection highlight. <c>null</c> = WinUI default (accent).</summary>
    public Microsoft.UI.Xaml.Media.SolidColorBrush? SelectionHighlightColor { get; init; }
    internal Action<WinUI.RichEditBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnTextChanged is not null;

    // Text round-trips through the RichEditBox document (not a Text DP): the set
    // writes Document.SetText (guarded on non-empty, manual BeginSuppress because
    // SetText fires TextChanged asynchronously), readBack/trampoline read
    // Document.GetText trimming a trailing \r. Reused verbatim from the hand-written
    // descriptor so the deferred suppress-counter echo behaviour is identical.
    private static readonly global::Microsoft.UI.Xaml.RoutedEventHandler __TextChangedTrampoline = (s, _) =>
    {
        var r = (WinUI.RichEditBox)s!;
        if (!global::Microsoft.UI.Reactor.Core.Reconciler.TryGetReactorState(r, out var state)) return;
        if (global::Microsoft.UI.Reactor.Core.ChangeEchoSuppressor.ShouldSuppress(state)) return;
        r.Document.GetText(global::Microsoft.UI.Text.TextGetOptions.None, out var text);
        (state.Element as RichEditBoxElement)
            ?.OnTextChanged?.Invoke(text?.TrimEnd('\r') ?? "");
    };

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<RichEditBoxElement, WinUI.RichEditBox> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<RichEditBoxElement, WinUI.RichEditBox> d)
        => d.HandCodedControlled<global::Microsoft.UI.Reactor.Core.V1Protocol.RichEditBoxEventPayload, string, global::Microsoft.UI.Xaml.RoutedEventHandler>(
            get:         static e => e.Text,
            set:         static (c, v) =>
            {
                if (!string.IsNullOrEmpty(v))
                {
                    global::Microsoft.UI.Reactor.Core.ChangeEchoSuppressor.BeginSuppress(c);
                    c.Document.SetText(global::Microsoft.UI.Text.TextSetOptions.None, v);
                }
            },
            readBack:    static c =>
            {
                c.Document.GetText(global::Microsoft.UI.Text.TextGetOptions.None, out var text);
                return text?.TrimEnd('\r') ?? "";
            },
            subscribe:   static (c, h) => c.TextChanged += h,
            callback:    static e => e.OnTextChanged,
            trampoline:  __TextChangedTrampoline,
            slotIsNull:  static p => p.TextChangedTrampoline is null,
            setSlot:     static (p, h) => p.TextChangedTrampoline = h);
}

// ════════════════════════════════════════════════════════════════════════
//  Layout / Container elements
// ════════════════════════════════════════════════════════════════════════

// Spec 058 §15 (P5.19) — attached-property panel: Orientation auto-maps; the per-child
// VariableSizedWrapGrid.SetRowSpan/SetColumnSpan strategy ([WrapPanelChildren]) and the three
// sentinel-guarded props (MaximumRowsOrColumns ≥ 0, ItemWidth/ItemHeight non-NaN, [WrapManual])
// live in PanelAttachedHooks.cs. Replaces the hand-written WrapGridDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.VariableSizedWrapGrid))]
[global::Microsoft.UI.Reactor.Wrappers.WrapPanelChildren(PerChild = "ApplyWrapGridAttached")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("MaximumRowsOrColumns")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("ItemWidth")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("ItemHeight")]
public partial record WrapGridElement(
Element[] Children
) : Element
{
    public int MaximumRowsOrColumns { get; init; } = -1;
    public Orientation Orientation { get; init; } = Orientation.Horizontal;
    public double ItemWidth { get; init; } = double.NaN;
    public double ItemHeight { get; init; } = double.NaN;
    internal Action<WinUI.VariableSizedWrapGrid>[] Setters { get; init; } = [];
}

[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.StackPanel))]  // spec 058 §15 (P5.4)
public partial record StackElement(
    Orientation Orientation,
    Element[] Children
) : Element
{
    /// <summary>
    /// Spacing between children, in DIPs.
    /// </summary>
    /// <remarks>
    /// Reactor default is <c>8</c> — a deliberate deviation from WinUI's
    /// <c>StackPanel.Spacing</c> default of <c>0</c>. Reactor's call shape
    /// (<c>VStack(a, b, c)</c>) almost always wants whitespace between siblings;
    /// the 8 DIP default produces visually correct output for the
    /// zero-argument call. Set to <c>0</c> explicitly for legacy WinUI
    /// behavior. (spec 039 §0.4 / §16)
    /// </remarks>
    public double Spacing { get; init; } = 8;
    public HorizontalAlignment? HorizontalAlignment { get; init; }
    public VerticalAlignment? VerticalAlignment { get; init; }
    internal Action<WinUI.StackPanel>[] Setters { get; init; } = [];
}

// Spec 058 §15 (P5.19) — attached-property panel: RowSpacing/ColumnSpacing auto-map; the
// per-child Grid.SetRow/SetColumn strategy ([WrapPanelChildren]) and the Definition rebuild
// ([WrapManual]) live in PanelAttachedHooks.cs. Replaces the hand-written GridDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.Grid))]
[global::Microsoft.UI.Reactor.Wrappers.WrapPanelChildren(PerChild = "ApplyGridAttached")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Definition")]
public partial record GridElement(
GridDefinition Definition,
Element[] Children
) : Element
{
    public double RowSpacing { get; init; }
    public double ColumnSpacing { get; init; }
    internal Action<WinUI.Grid>[] Setters { get; init; } = [];
}

// Spec 058 §15 (P5.25) — FlexPanel. The 7 flex enum/gap props auto-map; per-child flex attached
// props use [WrapPanelChildren] (the capability built in P5.19). FlexPadding (non-nullable Thickness)
// is [WrapManual]. Replaces FlexPanelDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(Layout.FlexPanel))]
[global::Microsoft.UI.Reactor.Wrappers.WrapPanelChildren(PerChild = "ApplyFlexChildAttached")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("FlexPadding")]
public partial record FlexElement(Element[] Children) : Element
{
    public Layout.FlexDirection Direction { get; init; } = Layout.FlexDirection.Row;
    public Layout.FlexJustify JustifyContent { get; init; } = Layout.FlexJustify.FlexStart;
    public Layout.FlexAlign AlignItems { get; init; } = Layout.FlexAlign.Stretch;
    public Layout.FlexAlign AlignContent { get; init; } = Layout.FlexAlign.FlexStart;
    public Layout.FlexWrap Wrap { get; init; } = Layout.FlexWrap.NoWrap;
    public double ColumnGap { get; init; }
    public double RowGap { get; init; }
    public Thickness FlexPadding { get; init; }
    internal Action<Layout.FlexPanel>[] Setters { get; init; } = [];

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<FlexElement, Layout.FlexPanel> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<FlexElement, Layout.FlexPanel> d)
        => d.OneWay(
            get: static e => e.FlexPadding,
            set: static (c, v) => c.FlexPadding = v);

    private static void ApplyFlexChildAttached(Layout.FlexPanel panel, Microsoft.UI.Xaml.UIElement ctrl, Element child)
    {
        var fa = child.GetAttached<FlexAttached>();
        // Always apply — reset to defaults when no FlexAttached so stale values from pool-rented
        // or reconciler-reused controls are cleared.
        Layout.FlexPanel.SetGrow(ctrl, fa?.Grow ?? 0);
        Layout.FlexPanel.SetShrink(ctrl, fa?.Shrink ?? 1);
        if (fa is { Basis: { } basis }) Layout.FlexPanel.SetBasis(ctrl, basis);
        else ctrl.ClearValue(Layout.FlexPanel.BasisProperty);
        if (fa is { MinWidth: { } minWidth }) Layout.FlexPanel.SetMinWidth(ctrl, minWidth);
        else ctrl.ClearValue(Layout.FlexPanel.FlexMinWidthProperty);
        if (fa is { MinHeight: { } minHeight }) Layout.FlexPanel.SetMinHeight(ctrl, minHeight);
        else ctrl.ClearValue(Layout.FlexPanel.FlexMinHeightProperty);
        if (fa is { AlignSelf: { } alignSelf }) Layout.FlexPanel.SetAlignSelf(ctrl, alignSelf);
        else ctrl.ClearValue(Layout.FlexPanel.AlignSelfProperty);
        Layout.FlexPanel.SetPosition(ctrl, fa?.Position ?? Layout.FlexPositionType.Relative);
        if (fa is { Left: { } left }) Layout.FlexPanel.SetLeft(ctrl, left);
        else ctrl.ClearValue(Layout.FlexPanel.LeftProperty);
        if (fa is { Top: { } top }) Layout.FlexPanel.SetTop(ctrl, top);
        else ctrl.ClearValue(Layout.FlexPanel.TopProperty);
        if (fa is { Right: { } right }) Layout.FlexPanel.SetRight(ctrl, right);
        else ctrl.ClearValue(Layout.FlexPanel.RightProperty);
        if (fa is { Bottom: { } bottom }) Layout.FlexPanel.SetBottom(ctrl, bottom);
        else ctrl.ClearValue(Layout.FlexPanel.BottomProperty);
    }
}

// Spec 058 §15 (P5.9) — Child→Content (content-from-record), 5 non-nullable enum
// scroll props auto-map, ViewChanged is a typed whole-args event ([WrapEvent]).
// Orientation is a bespoke convenience with no ScrollViewer control property — the
// hand-written descriptor never mapped it, so it stays unmapped (Exclude).
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.ScrollViewer), Exclude = new[] { "Orientation" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapEvent("ViewChanged")]
public partial record ScrollViewerElement(Element Child) : Element
{
    public Orientation Orientation { get; init; } = Orientation.Vertical;
    public ScrollBarVisibility HorizontalScrollBarVisibility { get; init; } = ScrollBarVisibility.Auto;
    public ScrollBarVisibility VerticalScrollBarVisibility { get; init; } = ScrollBarVisibility.Auto;
    public WinUI.ScrollMode HorizontalScrollMode { get; init; } = WinUI.ScrollMode.Auto;
    public WinUI.ScrollMode VerticalScrollMode { get; init; } = WinUI.ScrollMode.Auto;
    public WinUI.ZoomMode ZoomMode { get; init; } = WinUI.ZoomMode.Disabled;

    /// <summary>
    /// Raised when the scroll view's offset or zoom factor changes. The args
    /// expose <c>IsIntermediate</c> for callers who want to debounce until the
    /// scroll settles.
    /// </summary>
    public Action<WinUI.ScrollViewerViewChangedEventArgs>? OnViewChanged { get; init; }

    internal Action<WinUI.ScrollViewer>[] Setters { get; init; } = [];

    internal override bool HasCallbacks => OnViewChanged is not null;
}

/// <summary>
/// Maps to the modern <see cref="Microsoft.UI.Xaml.Controls.ScrollView"/>
/// (InteractionTracker-backed, derives from <c>FrameworkElement</c>), as
/// opposed to <see cref="ScrollViewerElement"/>, which targets the classic
/// <c>ScrollViewer</c>. Issue #348.
///
/// Exposes capabilities that exist only on the new control:
/// <c>ContentOrientation</c>, <c>HorizontalAnchorRatio</c> /
/// <c>VerticalAnchorRatio</c>, and the <c>Scrolling*</c> enum surface.
/// </summary>
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.ScrollView))]  // spec 058 §15 (P5.4)
public partial record ScrollViewElement(Element Child) : Element
{
    public WinUI.ScrollingContentOrientation ContentOrientation { get; init; } = WinUI.ScrollingContentOrientation.Vertical;
    public WinUI.ScrollingScrollBarVisibility HorizontalScrollBarVisibility { get; init; } = WinUI.ScrollingScrollBarVisibility.Auto;
    public WinUI.ScrollingScrollBarVisibility VerticalScrollBarVisibility { get; init; } = WinUI.ScrollingScrollBarVisibility.Auto;
    public WinUI.ScrollingScrollMode HorizontalScrollMode { get; init; } = WinUI.ScrollingScrollMode.Auto;
    public WinUI.ScrollingScrollMode VerticalScrollMode { get; init; } = WinUI.ScrollingScrollMode.Auto;
    public WinUI.ScrollingZoomMode ZoomMode { get; init; } = WinUI.ScrollingZoomMode.Disabled;
    public double MinZoomFactor { get; init; } = 0.1;
    public double MaxZoomFactor { get; init; } = 10.0;
    public double HorizontalAnchorRatio { get; init; } = 0.0;
    public double VerticalAnchorRatio { get; init; } = 0.0;

    /// <summary>
    /// Raised after the view (offset or zoom factor) changes. The modern
    /// control reports only the settled value — there is no intermediate flag
    /// like the classic <c>ScrollViewer.ViewChanged</c>.
    /// </summary>
    public Action? OnViewChanged { get; init; }

    internal Action<WinUI.ScrollView>[] Setters { get; init; } = [];

    internal override bool HasCallbacks => OnViewChanged is not null;
}

/// <summary>Border element — a single-child container with brush/corner/thickness chrome.</summary>
// Spec 058 §15 (P5.18) — Child is the single-content slot ([WrapContent]); CornerRadius and
// BorderThickness are ergonomic double scalars converted to the WinUI structs ([WrapConvert]);
// Background/BorderBrush auto-map (Brush is a supported reference). No events. Replaces the
// hand-coded BorderHandler + the parallel hand-written BorderDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.Border))]
[global::Microsoft.UI.Reactor.Wrappers.WrapContent("Child")]
[global::Microsoft.UI.Reactor.Wrappers.WrapConvert("CornerRadius")]
[global::Microsoft.UI.Reactor.Wrappers.WrapConvert("BorderThickness")]
public partial record BorderElement(Element? Child) : Element
{
    public double? CornerRadius { get; init; }
    public Brush? Background { get; init; }
    public Brush? BorderBrush { get; init; }
    public double? BorderThickness { get; init; }
    internal Action<WinUI.Border>[] Setters { get; init; } = [];
}

/// <summary>Expander element. <c>IsExpanded</c> defaults to <see cref="Optional{T}.Unset"/> so the control owns its expansion state until explicitly set.</summary>
// Spec 058 §15 (P5.23) — ExpandDirection auto. Content + HeaderTemplate are both Element-typed so
// auto content-slot detection is ambiguous → overwrite d.Children with SingleContent(Content) in
// Customize. HeaderTemplate (ImperativeBridged, Element header wins), gated string Header, IsExpanded
// (counter-echo HandCodedControlled over Expanding + Collapsed event) are bespoke. ContentTransitions
// (TransitionCollection) is Excluded (legacy gap — escape-hatched via setter). Replaces ExpanderDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.Expander), Exclude = new[] { "ContentTransitions" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Content")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Header")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("HeaderTemplate")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("IsExpanded")]
public partial record ExpanderElement(
    string Header,
    Element Content,
    Optional<bool> IsExpanded = default,
    Action<bool>? OnIsExpandedChanged = null
) : Element
{
    public ExpandDirection ExpandDirection { get; init; } = ExpandDirection.Down;
    /// <summary>Custom Element header (overrides the string <see cref="Header"/>).</summary>
    public Element? HeaderTemplate { get; init; }
    /// <summary>Custom <c>TransitionCollection</c> applied to the expanding content area.</summary>
    public Microsoft.UI.Xaml.Media.Animation.TransitionCollection? ContentTransitions { get; init; }
    internal Action<WinUI.Expander>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnIsExpandedChanged is not null;

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.Expander, WinUI.ExpanderExpandingEventArgs>
        __ExpandingTrampoline = (s, _) =>
        {
            if (!global::Microsoft.UI.Reactor.Core.Reconciler.TryGetReactorState(s, out var state)) return;
            if (global::Microsoft.UI.Reactor.Core.ChangeEchoSuppressor.ShouldSuppress(state)) return;
            (state.Element as ExpanderElement)?.OnIsExpandedChanged?.Invoke(true);
        };

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.Expander, WinUI.ExpanderCollapsedEventArgs>
        __CollapsedTrampoline = (s, _) =>
        {
            if (!global::Microsoft.UI.Reactor.Core.Reconciler.TryGetReactorState(s, out var state)) return;
            if (global::Microsoft.UI.Reactor.Core.ChangeEchoSuppressor.ShouldSuppress(state)) return;
            (state.Element as ExpanderElement)?.OnIsExpandedChanged?.Invoke(false);
        };

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ExpanderElement, WinUI.Expander> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ExpanderElement, WinUI.Expander> d)
    {
        d.Children = new global::Microsoft.UI.Reactor.Core.V1Protocol.SingleContent<ExpanderElement, WinUI.Expander>(
            GetChild: static el => el.Content,
            SetChild: static (ctrl, ui) => ctrl.Content = ui)
        {
            GetCurrentChild = static ctrl => ctrl.Content as global::Microsoft.UI.Xaml.UIElement,
        };
        return d
            .ImperativeBridged(
                mount: static (ctx, c, e) =>
                {
                    if (e.HeaderTemplate is null) return;
                    var mounted = ctx.MountChild(e.HeaderTemplate);
                    if (mounted is not null) c.Header = mounted;
                },
                update: static (ctx, c, oldEl, newEl) =>
                {
                    if (oldEl.HeaderTemplate is null && newEl.HeaderTemplate is null) return;
                    var existing = c.Header as global::Microsoft.UI.Xaml.UIElement;
                    var next = ctx.Reconciler.ReconcileV1Child(
                        oldEl.HeaderTemplate, newEl.HeaderTemplate, existing, ctx.RequestRerender);
                    if (!ReferenceEquals(existing, next))
                        c.Header = next;
                })
            .OneWayConditional(
                get:         static e => e.Header ?? string.Empty,
                set:         static (c, v) => c.Header = v,
                shouldWrite: static e => e.HeaderTemplate is null)
            .HandCodedControlled<global::Microsoft.UI.Reactor.Core.V1Protocol.ExpanderEventPayload, bool,
                global::Windows.Foundation.TypedEventHandler<WinUI.Expander, WinUI.ExpanderExpandingEventArgs>>(
                get:         static e => e.IsExpanded,
                set:         static (c, v) => c.IsExpanded = v,
                readBack:    static c => c.IsExpanded,
                subscribe:   static (c, h) => c.Expanding += h,
                callback:    static e => e.OnIsExpandedChanged,
                trampoline:  __ExpandingTrampoline,
                slotIsNull:  static p => p.ExpandingTrampoline is null,
                setSlot:     static (p, h) => p.ExpandingTrampoline = h)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.ExpanderEventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.Expander, WinUI.ExpanderCollapsedEventArgs>>(
                subscribe:        static (c, h) => c.Collapsed += h,
                callbackPresent:  static e => e.OnIsExpandedChanged,
                trampoline:       __CollapsedTrampoline,
                slotIsNull:       static p => p.CollapsedTrampoline is null,
                setSlot:          static (p, h) => p.CollapsedTrampoline = h);
    }
}

// Spec 058 §15 (P5.23) — IsPaneOpen/OpenPaneLength/CompactPaneLength/DisplayMode/
// LightDismissOverlayMode auto-map. Pane+Content are both Element-typed (ambiguous auto content) →
// overwrite d.Children with NamedSlots. PaneBackground (reference-comparer) + the twin
// PaneOpening/PaneClosing events (OnPaneOpenChanged) are bespoke. Replaces SplitViewDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.SplitView))]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Pane")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Content")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("PaneBackground")]
public partial record SplitViewElement(
    Element? Pane = null,
    Element? Content = null
) : Element
{
    public bool IsPaneOpen { get; init; } = true;
    public double OpenPaneLength { get; init; } = 320;
    public double CompactPaneLength { get; init; } = 48;
    public SplitViewDisplayMode DisplayMode { get; init; } = SplitViewDisplayMode.Overlay;
    public Action<bool>? OnPaneOpenChanged { get; init; }
    /// <summary>Brush behind the pane. Pair with the <c>.PaneBackground(ThemeRef)</c> overload for theme-aware backgrounds.</summary>
    public Brush? PaneBackground { get; init; }
    /// <summary>How the light-dismiss overlay reacts to taps in Overlay mode. Defaults to <c>Auto</c>.</summary>
    public LightDismissOverlayMode LightDismissOverlayMode { get; init; } = LightDismissOverlayMode.Auto;
    internal Action<WinUI.SplitView>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnPaneOpenChanged is not null;

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.SplitView, object>
        __PaneOpeningTrampoline = (s, _) =>
            (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(s) as SplitViewElement)?.OnPaneOpenChanged?.Invoke(true);

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.SplitView, WinUI.SplitViewPaneClosingEventArgs>
        __PaneClosingTrampoline = (s, _) =>
            (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(s) as SplitViewElement)?.OnPaneOpenChanged?.Invoke(false);

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<SplitViewElement, WinUI.SplitView> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<SplitViewElement, WinUI.SplitView> d)
    {
        d.Children = new global::Microsoft.UI.Reactor.Core.V1Protocol.NamedSlots<SplitViewElement, WinUI.SplitView>(new[]
        {
            new global::Microsoft.UI.Reactor.Core.V1Protocol.NamedSlot<SplitViewElement, WinUI.SplitView>(
                Name: "Pane",
                GetChild: static e => e.Pane,
                SetChild: static (c, ui) => c.Pane = ui)
            {
                GetCurrentChild = static c => c.Pane as global::Microsoft.UI.Xaml.UIElement,
            },
            new global::Microsoft.UI.Reactor.Core.V1Protocol.NamedSlot<SplitViewElement, WinUI.SplitView>(
                Name: "Content",
                GetChild: static e => e.Content,
                SetChild: static (c, ui) => c.Content = ui as global::Microsoft.UI.Xaml.UIElement)
            {
                GetCurrentChild = static c => c.Content as global::Microsoft.UI.Xaml.UIElement,
            },
        });
        return d
            .OneWayConditional(
                get:         static e => e.PaneBackground,
                set:         static (c, v) => c.PaneBackground = v!,
                shouldWrite: static e => e.PaneBackground is not null,
                comparer:    SplitViewBrushReferenceComparer.Instance)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.SplitViewEventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.SplitView, object>>(
                subscribe:        static (c, h) => c.PaneOpening += h,
                callbackPresent:  static e => e.OnPaneOpenChanged,
                trampoline:       __PaneOpeningTrampoline,
                slotIsNull:       static p => p.PaneOpeningTrampoline is null,
                setSlot:          static (p, h) => p.PaneOpeningTrampoline = h)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.SplitViewEventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.SplitView, WinUI.SplitViewPaneClosingEventArgs>>(
                subscribe:        static (c, h) => c.PaneClosing += h,
                callbackPresent:  static e => e.OnPaneOpenChanged,
                trampoline:       __PaneClosingTrampoline,
                slotIsNull:       static p => p.PaneClosingTrampoline is null,
                setSlot:          static (p, h) => p.PaneClosingTrampoline = h);
    }

    private sealed class SplitViewBrushReferenceComparer : global::System.Collections.Generic.IEqualityComparer<Brush?>
    {
        public static readonly SplitViewBrushReferenceComparer Instance = new();
        public bool Equals(Brush? x, Brush? y) => ReferenceEquals(x, y);
        public int GetHashCode(Brush obj) => global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

public partial record ViewboxElement(Element Child) : Element
{
    public Stretch? Stretch { get; init; }
    public StretchDirection? StretchDirection { get; init; }
    internal Action<WinUI.Viewbox>[] Setters { get; init; } = [];
}

// Spec 058 §15 (P5.3) — descriptor-only migration. The hand-written
// ViewboxDescriptor/ViewboxDescriptorHandler were deleted; the generator now
// emits the ControlDescriptor + Pattern-A registration from this record.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.Viewbox))]
public partial record ViewboxElement;

[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.Canvas))]  // spec 058 §15 (P5.4)
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Children")]  // bespoke per-child Canvas.Left/Top + anchor recompute strategy
public partial record CanvasElement(Element[] Children) : Element
{
    public double? Width { get; init; }
    public double? Height { get; init; }
    public Brush? Background { get; init; }
    internal Action<WinUI.Canvas>[] Setters { get; init; } = [];

    // Children carries bespoke per-child attached-position machinery (Canvas.Left/Top
    // + AnchorX/AnchorY post-layout recompute); the auto panel strategy can't express
    // it, so we swap in the hand-written strategy holder. Value props auto-map.
    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<CanvasElement, WinUI.Canvas> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<CanvasElement, WinUI.Canvas> d)
    {
        d.Children = global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors.CanvasChildrenStrategy.Strategy;
        return d;
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Navigation elements
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Renders the content for the current route of a <see cref="Navigation.NavigationHandle{TRoute}"/>.
/// Created via <c>NavigationHost&lt;TRoute&gt;(nav, routeMap)</c> in the DSL.
/// The reconciler uses a Grid container so outgoing/incoming pages can overlap during transitions (Phase 4).
/// </summary>
public record NavigationHostElement(
    object NavigationHandle,
    Func<object, Element> RouteMap
) : Element
{
    public Navigation.NavigationTransition Transition { get; init; } = Navigation.NavigationTransition.Default;
    public Navigation.NavigationCacheMode CacheMode { get; init; } = Navigation.NavigationCacheMode.Disabled;
    public int CacheSize { get; init; } = 10;
}

// Spec 058 §15 (P5.23) — IsPaneOpen/PaneDisplayMode/IsBackEnabled/IsSettingsVisible/PaneTitle
// auto-map. The 5 NamedSlots (Header/AutoSuggestBox/PaneFooter/PaneCustomContent/Content), the
// MenuItems+SelectedTag menu reconciler (.Imperative), the 3 NaN-sentinel pane widths, and the
// SelectionChanged/BackRequested events are bespoke — in Element.NavigationView.cs. BackRequested
// is Excluded (auto-surfaces). Replaces NavigationViewDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.NavigationView), Exclude = new[] { "BackRequested" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("MenuItems")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("SelectedTag")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Header")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("AutoSuggestBox")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("PaneFooter")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("PaneCustomContent")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Content")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("OpenPaneLength")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("CompactModeThresholdWidth")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("ExpandedModeThresholdWidth")]
public partial record NavigationViewElement(
    NavigationViewItemData[] MenuItems,
    Element? Content = null
) : Element
{
    public string? SelectedTag { get; init; }
    public Action<string?>? OnSelectedTagChanged { get; init; }
    public bool IsPaneOpen { get; init; } = true;
    public NavigationViewPaneDisplayMode PaneDisplayMode { get; init; } = NavigationViewPaneDisplayMode.Auto;
    public bool IsBackEnabled { get; init; }
    public Action? OnBackRequested { get; init; }
    public Element? Header { get; init; }
    public bool IsSettingsVisible { get; init; } = true;
    public string? PaneTitle { get; init; }
    /// <summary>AutoSuggestBox rendered at the top of the pane. Mirrors <c>NavigationView.AutoSuggestBox</c>.</summary>
    public AutoSuggestBoxElement? AutoSuggestBox { get; init; }
    /// <summary>Element rendered at the bottom of the pane, below all menu items.</summary>
    public Element? PaneFooter { get; init; }
    /// <summary>Custom element rendered between the AutoSuggestBox and the menu items.</summary>
    public Element? PaneCustomContent { get; init; }
    /// <summary>Width of the pane when expanded. <c>NaN</c> uses the WinUI default (320).</summary>
    public double OpenPaneLength { get; init; } = double.NaN;
    /// <summary>Window width below which the pane collapses to compact mode. <c>NaN</c> uses the WinUI default (640).</summary>
    public double CompactModeThresholdWidth { get; init; } = double.NaN;
    /// <summary>Window width at which the pane auto-expands. <c>NaN</c> uses the WinUI default (1008).</summary>
    public double ExpandedModeThresholdWidth { get; init; } = double.NaN;
    internal Action<WinUI.NavigationView>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnSelectedTagChanged is not null || OnBackRequested is not null;
}

// Spec 058 §15 (P5.23) — Title/Subtitle/IsBackButtonVisible/IsBackButtonEnabled/
// IsPaneToggleButtonVisible/AutoRefreshDragRegions auto-map. Content+RightHeader (NamedSlots → overwrite d.Children),
// Icon (Icon→IconSource via IconResolver transform), the window.SetTitleBar registration
// (.Imperative) and the BackRequested/PaneToggleRequested events (Excluded) are bespoke.
// Replaces TitleBarDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.TitleBar), Exclude = new[] { "BackRequested", "PaneToggleRequested" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Content")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("RightHeader")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Icon")]
public partial record TitleBarElement(
    string Title
) : Element
{
    public string? Subtitle { get; init; }
    public bool IsBackButtonVisible { get; init; }
    public bool IsBackButtonEnabled { get; init; }
    public Action? OnBackRequested { get; init; }
    public bool IsPaneToggleButtonVisible { get; init; }
    public Action? OnPaneToggleRequested { get; init; }
    /// <summary>
    /// When <c>true</c>, the WinUI <c>TitleBar</c> re-derives its drag regions on
    /// every layout pass (WinApp SDK ≥ 2.1.3). Useful when <see cref="Content"/>
    /// changes across renders. Default <c>false</c> (matches the control default;
    /// interactive controls are still auto-excluded from the drag region). See spec 059.
    /// </summary>
    public bool AutoRefreshDragRegions { get; init; }
    public Element? Content { get; init; }
    public Element? RightHeader { get; init; }
    /// <summary>
    /// Icon shown in the title bar's leading slot. Mirrors WinUI 3
    /// <c>TitleBar.IconSource</c>. Pass a <see cref="SymbolIconData"/> /
    /// <see cref="FontIconData"/> for built-in glyphs, or
    /// <see cref="ImageIconData"/> / <see cref="BitmapIconData"/> for a
    /// bundled <c>.ico</c> / image (e.g. <c>new ImageIconData(new
    /// Uri("ms-appx:///Assets/AppIcon.ico"))</c>).
    /// </summary>
    public IconData? Icon { get; init; }
    internal Action<WinUI.TitleBar>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnBackRequested is not null || OnPaneToggleRequested is not null;

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.TitleBar, object>
        __BackRequestedTrampoline = (s, _) =>
            (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(s) as TitleBarElement)?.OnBackRequested?.Invoke();

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.TitleBar, object>
        __PaneToggleRequestedTrampoline = (s, _) =>
            (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(s) as TitleBarElement)?.OnPaneToggleRequested?.Invoke();

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<TitleBarElement, WinUI.TitleBar> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<TitleBarElement, WinUI.TitleBar> d)
    {
        d.Children = new global::Microsoft.UI.Reactor.Core.V1Protocol.NamedSlots<TitleBarElement, WinUI.TitleBar>(new[]
        {
            new global::Microsoft.UI.Reactor.Core.V1Protocol.NamedSlot<TitleBarElement, WinUI.TitleBar>(
                Name: "Content",
                GetChild: static e => e.Content,
                SetChild: static (c, ui) => c.Content = ui)
            {
                GetCurrentChild = static c => c.Content as global::Microsoft.UI.Xaml.UIElement,
            },
            new global::Microsoft.UI.Reactor.Core.V1Protocol.NamedSlot<TitleBarElement, WinUI.TitleBar>(
                Name: "RightHeader",
                GetChild: static e => e.RightHeader,
                SetChild: static (c, ui) => c.RightHeader = ui)
            {
                GetCurrentChild = static c => c.RightHeader as global::Microsoft.UI.Xaml.UIElement,
            },
        });
        return d
            .OneWay(
                get: static e => e.Icon,
                set: static (c, v) => c.IconSource = global::Microsoft.UI.Reactor.Core.V1Protocol.IconResolver.ResolveIconSource(v))
            .Imperative(
                mount: static (c, _) => RegisterWindowTitleBar(c),
                update: static (_, _, _) => { })
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.TitleBarEventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.TitleBar, object>>(
                subscribe:        static (c, h) => c.BackRequested += h,
                callbackPresent:  static e => e.OnBackRequested,
                trampoline:       __BackRequestedTrampoline,
                slotIsNull:       static p => p.BackRequestedTrampoline is null,
                setSlot:          static (p, h) => p.BackRequestedTrampoline = h)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.TitleBarEventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.TitleBar, object>>(
                subscribe:        static (c, h) => c.PaneToggleRequested += h,
                callbackPresent:  static e => e.OnPaneToggleRequested,
                trampoline:       __PaneToggleRequestedTrampoline,
                slotIsNull:       static p => p.PaneToggleRequestedTrampoline is null,
                setSlot:          static (p, h) => p.PaneToggleRequestedTrampoline = h);
    }

    // Issue #511 / PR #455 regression: ExtendsContentIntoTitleBar must flip BEFORE the WinUI
    // TitleBar's own Loaded handler runs UpdatePaddingsForCaptionButtons(); apply synchronously
    // in the Imperative mount (mirrors the legacy MountTitleBar path).
    private static void RegisterWindowTitleBar(WinUI.TitleBar titleBar)
    {
        if (global::Microsoft.UI.Reactor.ReactorApp.ActiveHostInternal is { } host)
        {
            var owningWindow = host.OwningWindow;
            // Record that a WinUI TitleBar control is mounted in this window —
            // including the explicit-false case below where we skip SetTitleBar.
            // The control corrupts the heap on teardown when the window is NOT in
            // content-extended mode, so the window flips ExtendsContentIntoTitleBar
            // back to true just before native close. (issue #537)
            owningWindow?.MarkTitleBarControlPresent();

            var explicitValue = owningWindow?.Spec.ExtendsContentIntoTitleBar;
            if (explicitValue == false) return;
            if (explicitValue is null)
                host.Window.ExtendsContentIntoTitleBar = true;
            host.Window.SetTitleBar(titleBar);
        }
    }
}

// Spec 058 §15 (P5.25) — IsAddTabButtonVisible/TabWidthMode/CloseButtonOverlayMode/CanDragTabs/
// CanReorderTabs/AllowDropTabs auto-map. Tabs (TabItemsHost), SelectedIndex (value-diff),
// TabStripHeader/Footer (ImperativeBridged), and the 4 drag/close/add events are bespoke — in
// Element.TabView.cs. SelectionChanged does NOT auto-surface (callback OnSelectedIndexChanged).
// Replaces TabViewDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.TabView), Exclude = new[] { "TabCloseRequested", "AddTabButtonClick", "TabDragStarting", "TabDragCompleted" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Tabs")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("SelectedIndex")]
[global::Microsoft.UI.Reactor.Wrappers.WrapElementSlot("TabStripHeader")]
[global::Microsoft.UI.Reactor.Wrappers.WrapElementSlot("TabStripFooter")]
public partial record TabViewElement(
    TabViewItemData[] Tabs
) : Element
{
    /// <summary>Selected tab index. Defaults to <see cref="Optional{T}.Unset"/>; use <c>Optional.Of(-1)</c> to force no selection.</summary>
    public Optional<int> SelectedIndex { get; init; } = default;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    public Action<int>? OnTabCloseRequested { get; init; }
    public Action? OnAddTabButtonClick { get; init; }
    public bool IsAddTabButtonVisible { get; init; }
    /// <summary>How tab widths are sized. Defaults to <c>Equal</c> (matches WinUI default).</summary>
    public TabViewWidthMode TabWidthMode { get; init; } = TabViewWidthMode.Equal;
    /// <summary>Controls when the per-tab close button is visible. Defaults to <c>Auto</c>.</summary>
    public TabViewCloseButtonOverlayMode CloseButtonOverlayMode { get; init; } = TabViewCloseButtonOverlayMode.Auto;
    /// <summary>Whether tabs can be dragged out (to a window).</summary>
    public bool CanDragTabs { get; init; }
    /// <summary>Whether tabs can be reordered within the strip.</summary>
    public bool CanReorderTabs { get; init; }
    /// <summary>Whether tabs from another TabView can be dropped onto this one.</summary>
    public bool AllowDropTabs { get; init; }
    /// <summary>Element rendered at the leading edge of the tab strip.</summary>
    public Element? TabStripHeader { get; init; }
    /// <summary>Element rendered at the trailing edge of the tab strip.</summary>
    public Element? TabStripFooter { get; init; }
    /// <summary>
    /// Fires when the user starts dragging a tab. <c>tabIndex</c> is the index
    /// of the dragged tab in <see cref="Tabs"/>. Used by spec 045 §2.4 docking
    /// drag pipeline to start a <c>DockDragSession</c>.
    /// </summary>
    public Action<int>? OnTabDragStarting { get; init; }
    /// <summary>
    /// Fires when a tab drag finishes — either landed on another TabView
    /// (<c>wasOutside == false</c>) or was dropped outside any TabView
    /// (<c>wasOutside == true</c>, used by §2.4 to trigger tear-out).
    /// </summary>
    public Action<int, bool>? OnTabDragCompleted { get; init; }
    internal Action<WinUI.TabView>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnSelectedIndexChanged is not null
        || OnTabCloseRequested is not null
        || OnAddTabButtonClick is not null
        || OnTabDragStarting is not null
        || OnTabDragCompleted is not null;
}

// Spec 058 §15 (P5.25) — BreadcrumbBar. Items (projected to ItemsSource as a label list) and the
// ItemClicked event (reads el.Items[Index]) are bespoke → [WrapManual] + Customize. ItemClicked is
// Excluded (auto-surfaces). Replaces BreadcrumbBarDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.BreadcrumbBar), Exclude = new[] { "ItemClicked" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Items")]
public partial record BreadcrumbBarElement(
    BreadcrumbBarItemData[] Items,
    Action<BreadcrumbBarItemData>? OnItemClicked = null
) : Element
{
    internal Action<WinUI.BreadcrumbBar>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnItemClicked is not null;

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.BreadcrumbBar, WinUI.BreadcrumbBarItemClickedEventArgs>
        __ItemClickedTrampoline = (s, args) =>
        {
            var bar = (WinUI.BreadcrumbBar)s!;
            if (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(bar) is not BreadcrumbBarElement el) return;
            if (args.Index >= 0 && args.Index < el.Items.Length)
                el.OnItemClicked?.Invoke(el.Items[args.Index]);
        };

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<BreadcrumbBarElement, WinUI.BreadcrumbBar> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<BreadcrumbBarElement, WinUI.BreadcrumbBar> d)
        => d.OneWay<BreadcrumbBarItemData[]>(
                get: static e => e.Items,
                set: static (c, items) => c.ItemsSource = global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Select(items, static i => i.Label)))
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.BreadcrumbBarEventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.BreadcrumbBar, WinUI.BreadcrumbBarItemClickedEventArgs>>(
                subscribe:        static (c, h) => c.ItemClicked += h,
                callbackPresent:  static e => e.OnItemClicked,
                trampoline:       __ItemClickedTrampoline,
                slotIsNull:       static p => p.ItemClickedTrampoline is null,
                setSlot:          static (p, h) => p.ItemClickedTrampoline = h);
}

// Spec 058 §15 (P5.21) — items control. Title auto-maps (OneWayConditional). The TabItemsHost
// (each PivotItemData → a PivotItem container; overwrites the auto single-source ItemsHost) and
// the value-diff SelectedIndex echo are bespoke in Customize. SelectionChanged is Excluded.
// Reuses FlipViewEventPayload. Replaces the hand-written PivotDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.Pivot), Exclude = new[] { "SelectionChanged" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("SelectedIndex")]
public partial record PivotElement(
PivotItemData[] Items
) : Element
{
/// <summary>Selected pivot item index. Defaults to <see cref="Optional{T}.Unset"/>; use <c>Optional.Of(-1)</c> to force no selection.</summary>
public Optional<int> SelectedIndex { get; init; } = default;
public Action<int>? OnSelectedIndexChanged { get; init; }
public string? Title { get; init; }
internal Action<WinUI.Pivot>[] Setters { get; init; } = [];
internal override bool HasCallbacks => OnSelectedIndexChanged is not null;

private static readonly WinUI.SelectionChangedEventHandler __SelectionChangedTrampoline = (s, _) =>
{
    var p = (WinUI.Pivot)s!;
    if (!global::Microsoft.UI.Reactor.Core.Reconciler.TryGetReactorState(p, out var state)) return;
    if (global::Microsoft.UI.Reactor.Core.ChangeEchoSuppressor.ShouldSuppressEcho(state, p.SelectedIndex)) return;
    (state.Element as PivotElement)?.OnSelectedIndexChanged?.Invoke(p.SelectedIndex);
};

private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<PivotElement, WinUI.Pivot> Customize(
    global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<PivotElement, WinUI.Pivot> d)
{
    d.Children = new global::Microsoft.UI.Reactor.Core.V1Protocol.TabItemsHost<PivotElement, WinUI.Pivot, PivotItemData>(
        GetItems:        static e => e.Items,
        GetCollection:   static c => c.Items,
        GetContent:      static item => item.Content,
        CreateContainer: static (item, mounted) => new WinUI.PivotItem
        {
            Header = item.Header,
            Content = mounted,
        },
        UpdateContainer: static (oldItem, newItem, container) =>
        {
            if (container is WinUI.PivotItem pi && pi.Header as string != newItem.Header)
                pi.Header = newItem.Header;
        });
    return d
        .HandCodedControlled<global::Microsoft.UI.Reactor.Core.V1Protocol.FlipViewEventPayload, int, WinUI.SelectionChangedEventHandler>(
            get:         static e => e.SelectedIndex,
            set:         static (c, v) => c.SelectedIndex = v,
            readBack:    static c => c.SelectedIndex,
            subscribe:   static (c, h) => c.SelectionChanged += h,
            callback:    static e => e.OnSelectedIndexChanged,
            trampoline:  __SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h,
            valueDiffEcho: true);
}
}

// ════════════════════════════════════════════════════════════════════════
//  Collection elements (simple, no item templating)
// ════════════════════════════════════════════════════════════════════════

public record ListViewElement(
    Element[] Items
) : Element
{
    /// <summary>Selected item index. Defaults to <see cref="Optional{T}.Unset"/>; use <c>Optional.Of(-1)</c> to force no selection.</summary>
    public Optional<int> SelectedIndex { get; init; } = default;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    public Action<int>? OnItemClick { get; init; }
    public ListViewSelectionMode SelectionMode { get; init; } = ListViewSelectionMode.Single;
    public string? Header { get; init; }
    /// <summary>Style applied to each generated <c>ListViewItem</c> container (e.g. for padding, hover background).</summary>
    public Style? ItemContainerStyle { get; init; }
    /// <summary>Controls when incremental data sources fetch the next page. Defaults to <c>Edge</c>.</summary>
    public IncrementalLoadingTrigger IncrementalLoadingTrigger { get; init; } = IncrementalLoadingTrigger.Edge;
    /// <summary>
    /// Multi-select snapshot callback. Receives the FULL list of currently
    /// selected indices (snapshot semantics, matching <see cref="CalendarViewElement.OnSelectedDatesChanged"/>).
    /// Use this in Multiple / Extended selection modes — <see cref="OnSelectedIndexChanged"/>
    /// only carries the focused single index. Not raised on initial mount.
    /// </summary>
    public Action<IReadOnlyList<int>>? OnSelectionChanged { get; init; }
    internal Action<WinUI.ListView>[] Setters { get; init; } = [];
    internal override bool HasCallbacks =>
        OnSelectedIndexChanged is not null
        || OnItemClick is not null
        || OnSelectionChanged is not null;
}

public record GridViewElement(
    Element[] Items
) : Element
{
    /// <summary>Selected item index. Defaults to <see cref="Optional{T}.Unset"/>; use <c>Optional.Of(-1)</c> to force no selection.</summary>
    public Optional<int> SelectedIndex { get; init; } = default;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    public Action<int>? OnItemClick { get; init; }
    public ListViewSelectionMode SelectionMode { get; init; } = ListViewSelectionMode.Single;
    public string? Header { get; init; }
    /// <summary>Style applied to each generated <c>GridViewItem</c> container.</summary>
    public Style? ItemContainerStyle { get; init; }
    /// <summary>Controls when incremental data sources fetch the next page. Defaults to <c>Edge</c>.</summary>
    public IncrementalLoadingTrigger IncrementalLoadingTrigger { get; init; } = IncrementalLoadingTrigger.Edge;
    /// <summary>
    /// Multi-select snapshot callback. See <see cref="ListViewElement.OnSelectionChanged"/>.
    /// </summary>
    public Action<IReadOnlyList<int>>? OnSelectionChanged { get; init; }
    internal Action<WinUI.GridView>[] Setters { get; init; } = [];
    internal override bool HasCallbacks =>
        OnSelectedIndexChanged is not null
        || OnItemClick is not null
        || OnSelectionChanged is not null;
}

// Spec 058 §15 (P5.25) — TreeView. SelectionMode/CanDragItems/CanReorderItems auto-map. Nodes
// (hierarchical TreeChildren strategy → overwrite d.Children), AllowDrop (base UIElement prop), and
// the ItemInvoked/Expanding events (Excluded) are bespoke. Replaces TreeViewDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.TreeView), Exclude = new[] { "ItemInvoked", "Expanding" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Nodes")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("AllowDrop")]
public partial record TreeViewElement(
    TreeViewNodeData[] Nodes
) : Element
{
    public Action<TreeViewNodeData>? OnItemInvoked { get; init; }
    public Action<TreeViewNodeData>? OnExpanding { get; init; }
    public TreeViewSelectionMode SelectionMode { get; init; } = TreeViewSelectionMode.Single;
    public bool CanDragItems { get; init; }
    public bool AllowDrop { get; init; }
    public bool CanReorderItems { get; init; }
    internal Action<WinUI.TreeView>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnItemInvoked is not null || OnExpanding is not null;

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.TreeView, WinUI.TreeViewItemInvokedEventArgs>
        __ItemInvokedTrampoline = (s, args) =>
        {
            var t = (WinUI.TreeView)s!;
            if (args.InvokedItem is WinUI.TreeViewNode tvn && tvn.Content is TreeViewNodeData nodeData)
                (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(t) as TreeViewElement)?.OnItemInvoked?.Invoke(nodeData);
        };

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.TreeView, WinUI.TreeViewExpandingEventArgs>
        __ExpandingTrampoline = (s, args) =>
        {
            var t = (WinUI.TreeView)s!;
            if (args.Node.Content is TreeViewNodeData nodeData)
                (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(t) as TreeViewElement)?.OnExpanding?.Invoke(nodeData);
        };

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<TreeViewElement, WinUI.TreeView> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<TreeViewElement, WinUI.TreeView> d)
    {
        d.Children = new global::Microsoft.UI.Reactor.Core.V1Protocol.TreeChildren<TreeViewElement, WinUI.TreeView>(static e => e.Nodes);
        return d
            .OneWay(
                get: static e => e.AllowDrop,
                set: static (c, v) => c.AllowDrop = v)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.TreeViewEventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.TreeView, WinUI.TreeViewItemInvokedEventArgs>>(
                subscribe:        static (c, h) => c.ItemInvoked += h,
                callbackPresent:  static e => e.OnItemInvoked,
                trampoline:       __ItemInvokedTrampoline,
                slotIsNull:       static p => p.ItemInvokedTrampoline is null,
                setSlot:          static (p, h) => p.ItemInvokedTrampoline = h)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.TreeViewEventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.TreeView, WinUI.TreeViewExpandingEventArgs>>(
                subscribe:        static (c, h) => c.Expanding += h,
                callbackPresent:  static e => e.OnExpanding,
                trampoline:       __ExpandingTrampoline,
                slotIsNull:       static p => p.ExpandingTrampoline is null,
                setSlot:          static (p, h) => p.ExpandingTrampoline = h);
    }
}

[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.FlipView))]  // spec 058 §15 (P5.4)
[global::Microsoft.UI.Reactor.Wrappers.WrapControlled("SelectedIndex", Events = new[] { "SelectionChanged" })]
public partial record FlipViewElement(
    Element[] Items
) : Element
{
    /// <summary>Selected item index. Defaults to <see cref="Optional{T}.Unset"/>; use <c>Optional.Of(-1)</c> to force no selection.</summary>
    public Optional<int> SelectedIndex { get; init; } = default;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    internal Action<WinUI.FlipView>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnSelectedIndexChanged is not null;
}

// ════════════════════════════════════════════════════════════════════════
//  Dialog / Overlay elements
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Declarative content dialog. Set IsOpen to true to show.
/// OnClosed fires with the result when the user dismisses the dialog.
/// </summary>
public record ContentDialogElement(
    string Title,
    Element Content,
    string PrimaryButtonText = "OK"
) : Element
{
    public bool IsOpen { get; init; }
    public string? SecondaryButtonText { get; init; }
    public string? CloseButtonText { get; init; }
    public ContentDialogButton DefaultButton { get; init; } = ContentDialogButton.Primary;
    public Action<ContentDialogResult>? OnClosed { get; init; }
    /// <summary>Enables/disables the primary button while the dialog is open. Defaults to <c>true</c>.</summary>
    public bool IsPrimaryButtonEnabled { get; init; } = true;
    /// <summary>Enables/disables the secondary button while the dialog is open. Defaults to <c>true</c>.</summary>
    public bool IsSecondaryButtonEnabled { get; init; } = true;
    /// <summary>Raised after the dialog finishes opening.</summary>
    public Action? OnOpened { get; init; }
    internal Action<WinUI.ContentDialog>[] Setters { get; init; } = [];
    internal override bool HasCallbacks =>
        OnClosed is not null || OnOpened is not null;
}

/// <summary>
/// A flyout attached to another element. Wrap the target element.
/// </summary>
public record FlyoutElement(
    Element Target,
    Element FlyoutContent
) : Element
{
    public bool IsOpen { get; init; }
    public FlyoutPlacementMode Placement { get; init; } = FlyoutPlacementMode.Auto;
    public Action? OnOpened { get; init; }
    public Action? OnClosed { get; init; }
    /// <summary>How the flyout reacts to clicks outside its bounds (Auto / Standard / Transient / TransientWithDismissOnPointerMoveAway).</summary>
    public FlyoutShowMode ShowMode { get; init; } = FlyoutShowMode.Auto;
    /// <summary>Whether the flyout animates on open/close. Defaults to <c>true</c>.</summary>
    public bool AreOpenCloseAnimationsEnabled { get; init; } = true;
    /// <summary>Element whose input is passed through the light-dismiss overlay (lets the user interact with one element behind the flyout).</summary>
    public Element? OverlayInputPassThroughElement { get; init; }
    internal Action<WinUI.Flyout>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnOpened is not null || OnClosed is not null;
}

/// <summary>
/// Describes a content flyout (used as a slot value on buttons or as a modifier attachment).
/// NOT independently mountable — the reconciler recognizes it in flyout slots.
/// </summary>
public record ContentFlyoutElement(Element Content) : Element
{
    public FlyoutPlacementMode Placement { get; init; } = FlyoutPlacementMode.Auto;
}

/// <summary>
/// Describes a menu flyout (used as a slot value on buttons or as a modifier attachment).
/// NOT independently mountable — the reconciler recognizes it in flyout slots.
/// </summary>
public record MenuFlyoutContentElement(MenuFlyoutItemBase[] Items) : Element
{
    public FlyoutPlacementMode Placement { get; init; } = FlyoutPlacementMode.Auto;
}

// Spec 058 §15 (P5.23) — Title/IsOpen/PreferredPlacement/ActionButtonContent/CloseButtonContent
// (string→object) auto-map. Content+HeroContent are both Element-typed → overwrite d.Children with
// NamedSlots. Subtitle (?? "" clear-on-null), Target (.Reference ElementRef→FrameworkElement),
// IconSource (reference comparer), PlacementMargin (non-nullable Thickness) are bespoke. The
// ActionButtonClick + Closed events are Excluded. Replaces TeachingTipDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.TeachingTip), Exclude = new[] { "ActionButtonClick", "Closed" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Content")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("HeroContent")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Subtitle")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Target")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("IconSource")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("PlacementMargin")]
public partial record TeachingTipElement(
    string Title,
    string? Subtitle = null
) : Element
{
    public bool IsOpen { get; init; }
    public Element? Content { get; init; }
    public string? ActionButtonContent { get; init; }
    public Action? OnActionButtonClick { get; init; }
    public string? CloseButtonContent { get; init; }
    public Action? OnClosed { get; init; }
    /// <summary>
    /// Control the tip anchors to, referenced by <c>ElementRef</c> so it resolves regardless of mount order.
    /// </summary>
    public Microsoft.UI.Reactor.Input.ElementRef? Target { get; init; }
    /// <summary>Custom icon source rendered in the tip's leading slot.</summary>
    public IconData? IconSource { get; init; }
    /// <summary>Optional "hero" Element (image / banner) rendered above the title.</summary>
    public Element? HeroContent { get; init; }
    /// <summary>Extra margin around the tip when placed relative to its target.</summary>
    public Thickness PlacementMargin { get; init; }
    /// <summary>Preferred placement edge. Defaults to <c>Auto</c>.</summary>
    public TeachingTipPlacementMode PreferredPlacement { get; init; } = TeachingTipPlacementMode.Auto;
    internal Action<WinUI.TeachingTip>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnActionButtonClick is not null || OnClosed is not null;

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.TeachingTip, object>
        __ActionButtonClickTrampoline = (s, _) =>
            (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(s) as TeachingTipElement)?.OnActionButtonClick?.Invoke();

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.TeachingTip, WinUI.TeachingTipClosedEventArgs>
        __ClosedTrampoline = (s, _) =>
            (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(s) as TeachingTipElement)?.OnClosed?.Invoke();

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<TeachingTipElement, WinUI.TeachingTip> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<TeachingTipElement, WinUI.TeachingTip> d)
    {
        d.Children = new global::Microsoft.UI.Reactor.Core.V1Protocol.NamedSlots<TeachingTipElement, WinUI.TeachingTip>(new[]
        {
            new global::Microsoft.UI.Reactor.Core.V1Protocol.NamedSlot<TeachingTipElement, WinUI.TeachingTip>(
                Name: "Content",
                GetChild: static e => e.Content,
                SetChild: static (c, ui) => c.Content = ui)
            {
                GetCurrentChild = static c => c.Content as global::Microsoft.UI.Xaml.UIElement,
            },
            new global::Microsoft.UI.Reactor.Core.V1Protocol.NamedSlot<TeachingTipElement, WinUI.TeachingTip>(
                Name: "HeroContent",
                GetChild: static e => e.HeroContent,
                SetChild: static (c, ui) => c.HeroContent = ui)
            {
                GetCurrentChild = static c => c.HeroContent as global::Microsoft.UI.Xaml.UIElement,
            },
        });
        return d
            .OneWay(
                get: static e => e.Subtitle ?? string.Empty,
                set: static (c, v) => c.Subtitle = v)
            .OneWay(
                get: static e => e.PlacementMargin,
                set: static (c, v) => c.PlacementMargin = v)
            .Reference(
                get: static e => e.Target,
                set: static (c, fe) => c.Target = fe)
            .OneWayConditional(
                get:         static e => e.IconSource,
                set:         static (c, v) => c.IconSource = global::Microsoft.UI.Reactor.Core.V1Protocol.IconResolver.ResolveIconSource(v),
                shouldWrite: static e => e.IconSource is not null,
                comparer:    TeachingTipIconReferenceComparer.Instance)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.TeachingTipEventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.TeachingTip, object>>(
                subscribe:        static (c, h) => c.ActionButtonClick += h,
                callbackPresent:  static e => e.OnActionButtonClick,
                trampoline:       __ActionButtonClickTrampoline,
                slotIsNull:       static p => p.ActionButtonClickTrampoline is null,
                setSlot:          static (p, h) => p.ActionButtonClickTrampoline = h)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.TeachingTipEventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.TeachingTip, WinUI.TeachingTipClosedEventArgs>>(
                subscribe:        static (c, h) => c.Closed += h,
                callbackPresent:  static e => e.OnClosed,
                trampoline:       __ClosedTrampoline,
                slotIsNull:       static p => p.ClosedTrampoline is null,
                setSlot:          static (p, h) => p.ClosedTrampoline = h);
    }

    private sealed class TeachingTipIconReferenceComparer : global::System.Collections.Generic.IEqualityComparer<IconData?>
    {
        public static readonly TeachingTipIconReferenceComparer Instance = new();
        public bool Equals(IconData? x, IconData? y) => ReferenceEquals(x, y);
        public int GetHashCode(IconData obj) => global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Status / Info elements
// ════════════════════════════════════════════════════════════════════════

// Spec 058 §15 (P5.20) — Severity/IsOpen/IsClosable + the Content slot auto-map; Title/Message
// stay Manual to preserve the `?? ""` clear-on-null OneWay (an OneWayConditional would leave a
// stale title on pooled reuse); IconSource (IconResolver + reference comparer), ActionButtonContent
// (dynamic Button + Click) and the Closed event are bespoke. Closed is Excluded (OnClosed would
// otherwise auto-surface it). Replaces the hand-written InfoBarDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.InfoBar), Exclude = new[] { "Closed" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Title")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Message")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("IconSource")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("ActionButtonContent")]
public partial record InfoBarElement(
    string? Title = null,
    string? Message = null
) : Element
{
    public InfoBarSeverity Severity { get; init; } = InfoBarSeverity.Informational;
    public bool IsOpen { get; init; } = true;
    public bool IsClosable { get; init; } = true;
    public string? ActionButtonContent { get; init; }
    public Action? OnActionButtonClick { get; init; }
    public Action? OnClosed { get; init; }
    /// <summary>Custom icon source. When set, overrides the severity-based icon.</summary>
    public IconData? IconSource { get; init; }
    /// <summary>Custom rich content rendered below the message (e.g. links, buttons, an embedded form).</summary>
    public Element? Content { get; init; }
    internal override bool HasCallbacks => OnActionButtonClick is not null || OnClosed is not null;
    internal Action<WinUI.InfoBar>[] Setters { get; init; } = [];

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.InfoBar, WinUI.InfoBarClosedEventArgs>
        __ClosedTrampoline = (s, _) =>
            (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(s) as InfoBarElement)?.OnClosed?.Invoke();

    // Title/Message preserve the clear-on-null OneWay; IconSource resolves via IconResolver
    // (reference-compared); ActionButtonContent builds an inner Button wiring Click through the
    // live element tag; Closed is the dismissal event. Verbatim from the deleted descriptor.
    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<InfoBarElement, WinUI.InfoBar> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<InfoBarElement, WinUI.InfoBar> d)
        => d.OneWay(
                get: static e => e.Title ?? string.Empty,
                set: static (c, v) => c.Title = v)
            .OneWay(
                get: static e => e.Message ?? string.Empty,
                set: static (c, v) => c.Message = v)
            .OneWayConditional(
                get:         static e => e.IconSource,
                set:         static (c, v) => c.IconSource = global::Microsoft.UI.Reactor.Core.V1Protocol.IconResolver.ResolveIconSource(v),
                shouldWrite: static e => e.IconSource is not null,
                comparer:    InfoBarIconReferenceComparer.Instance)
            .OneWayBridged<string?>(
                get:         static e => e.ActionButtonContent,
                set:         static (c, v, _, _) =>
                {
                    if (v is null)
                    {
                        c.ActionButton = null;
                        return;
                    }
                    var btn = new WinUI.Button { Content = v };
                    var infoBar = c;
                    btn.Click += (_, _) =>
                        (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(infoBar) as InfoBarElement)
                            ?.OnActionButtonClick?.Invoke();
                    c.ActionButton = btn;
                },
                shouldWrite: static e => e.ActionButtonContent is not null)
            .HandCodedEvent<global::Microsoft.UI.Reactor.Core.V1Protocol.InfoBarEventPayload,
                global::Windows.Foundation.TypedEventHandler<WinUI.InfoBar, WinUI.InfoBarClosedEventArgs>>(
                subscribe:        static (c, h) => c.Closed += h,
                callbackPresent:  static e => e.OnClosed,
                trampoline:       __ClosedTrampoline,
                slotIsNull:       static p => p.ClosedTrampoline is null,
                setSlot:          static (p, h) => p.ClosedTrampoline = h);

    private sealed class InfoBarIconReferenceComparer : global::System.Collections.Generic.IEqualityComparer<IconData?>
    {
        public static readonly InfoBarIconReferenceComparer Instance = new();
        public bool Equals(IconData? x, IconData? y) => ReferenceEquals(x, y);
        public int GetHashCode(IconData obj)
            => global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.InfoBadge), Exclude = new[] { "Icon" })]  // spec 058 §15 (P5.4); Icon is string on the record vs IconElement on the control — intentionally unmapped (legacy parity)
public partial record InfoBadgeElement() : Element
{
    public int? Value { get; init; }
    public string? Icon { get; init; }
    internal Action<WinUI.InfoBadge>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  Menu elements
// ════════════════════════════════════════════════════════════════════════

public record MenuBarElement(MenuBarItemData[] Items) : Element
{
    internal Action<WinUI.MenuBar>[] Setters { get; init; } = [];
}

public record CommandBarElement(
    AppBarItemBase[]? PrimaryCommands = null,
    AppBarItemBase[]? SecondaryCommands = null
) : Element
{
    public CommandBarDefaultLabelPosition DefaultLabelPosition { get; init; } = CommandBarDefaultLabelPosition.Bottom;
    public bool IsOpen { get; init; }
    public Element? Content { get; init; }
    internal Action<WinUI.CommandBar>[] Setters { get; init; } = [];
}

public record MenuFlyoutElement(
    Element Target,
    MenuFlyoutItemBase[] Items
) : Element
{
    internal Action<WinUI.MenuFlyout>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  Templated collection elements (data-driven ListView/GridView/FlipView)
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Which WinUI control type a templated list element targets.
/// </summary>
public enum TemplatedControlKind { ListView, GridView, FlipView }

/// <summary>
/// Abstract base for data-driven items controls. Non-generic so the reconciler
/// can match on a single type in its switch expression (same pattern as LazyStackElementBase).
/// </summary>
public abstract record TemplatedListElementBase : Element, global::Microsoft.UI.Reactor.Core.Internal.IItemViewSource, global::Microsoft.UI.Reactor.Core.Internal.IKeyedItemSource
{
    public abstract TemplatedControlKind ControlKind { get; }
    public abstract int ItemCount { get; }
    public abstract int GetSelectedIndex();
    internal virtual Optional<int> GetControlledSelectedIndex() => GetSelectedIndex();
    public abstract ListViewSelectionMode GetSelectionMode();
    public abstract string? GetHeader();
    public abstract bool GetIsItemClickEnabled();
    public abstract Element BuildItemView(int index);
    /// <summary>
    /// Projects the user's data item at <paramref name="index"/> through
    /// the typed peer's <c>KeySelector</c> to produce the stable identity
    /// string consumed by spec 042's keyed-list reconciliation pipeline.
    /// Phase 1: ListView/GridView/LazyVStack/LazyHStack only — FlipView
    /// pre-mounts so it does not participate.
    /// </summary>
    internal abstract string GetKeyAt(int index);
    // §14 Phase 3 close-out — bridge the internal abstract to the public
    // IKeyedItemSource contract via explicit interface implementation so
    // the abstract stays internal (spec 042 decision) while descriptor
    // ports can still read keys through the IKeyedItemSource handle.
    string global::Microsoft.UI.Reactor.Core.Internal.IKeyedItemSource.GetKeyAt(int index) => GetKeyAt(index);
    public abstract void InvokeSelectionChanged(int index);
    public abstract void InvokeItemClick(int index);
    public abstract void ApplyControlSetters(object control);
    /// <summary>
    /// True when programmatic setter actions (.Set(...)) have been attached.
    /// Used by <see cref="Element.OwnPropsEqual"/> to suppress the reconcile-highlight
    /// short-circuit so the overlay correctly tags the control as modified
    /// (and ApplyControlSetters keeps running on every reconcile pass).
    /// Virtual + default-false so external types deriving from this public
    /// abstract record don't break — only Reactor's own derived records that
    /// expose Setters need to override.
    /// </summary>
    internal virtual bool HasSetters => false;

    /// <summary>
    /// Snapshot-style multi-select callback. Default no-op; typed peers
    /// (TemplatedListView/TemplatedGridView) override to materialize and
    /// invoke <c>OnSelectionChanged</c> with the typed items.
    /// </summary>
    internal virtual void InvokeMultiSelectionChanged(IReadOnlyList<int> indices) { }

    /// <summary>True when the derived peer has wired a typed multi-select callback.</summary>
    internal virtual bool HasMultiSelectionCallback => false;
}

/// <summary>
/// Spec 047 §14 Phase 3 close-out — empty marker intermediate that lets
/// the v1 handler registry route every closed
/// <c>TemplatedListViewElement&lt;T&gt;</c> through a single base-derived
/// descriptor registration without an open-generic resolver. Adds no
/// fields and no overrides, so record equality on the leaf type
/// <c>TemplatedListViewElement&lt;T&gt;</c> is unchanged.
/// </summary>
public abstract record TemplatedListViewElementBase : TemplatedListElementBase
{
    public sealed override TemplatedControlKind ControlKind => TemplatedControlKind.ListView;
}

/// <summary>
/// Spec 047 §14 Phase 3 close-out — see
/// <see cref="TemplatedListViewElementBase"/>.
/// </summary>
public abstract record TemplatedGridViewElementBase : TemplatedListElementBase
{
    public sealed override TemplatedControlKind ControlKind => TemplatedControlKind.GridView;
}

/// <summary>
/// Spec 047 §14 Phase 3 close-out — see
/// <see cref="TemplatedListViewElementBase"/>. FlipView descriptor port
/// is carved to Phase 4 because FlipView does not support
/// <c>ContainerContentChanging</c> (pre-mounts items via a different
/// shape); the marker is reserved so the symmetry is visible in the
/// element hierarchy.
/// </summary>
public abstract record TemplatedFlipViewElementBase : TemplatedListElementBase
{
    public sealed override TemplatedControlKind ControlKind => TemplatedControlKind.FlipView;
}

public record TemplatedListViewElement<T>(
    IReadOnlyList<T> Items,
    Func<T, string> KeySelector,
    Func<T, int, Element> ViewBuilder
) : TemplatedListViewElementBase
{
    public int SelectedIndex { get; init; } = -1;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    public Action<T>? OnItemClick { get; init; }
    public ListViewSelectionMode SelectionMode { get; init; } = ListViewSelectionMode.Single;
    public string? Header { get; init; }
    /// <summary>
    /// Multi-select snapshot callback for the typed peer. Receives the full list
    /// of currently selected items (not just indices). Snapshot semantics — not
    /// raised on initial mount.
    /// </summary>
    public Action<IReadOnlyList<T>>? OnSelectionChanged { get; init; }
    internal Action<WinUI.ListView>[] Setters { get; init; } = [];

    public override int ItemCount => Items.Count;
    public override int GetSelectedIndex() => SelectedIndex;
    public override ListViewSelectionMode GetSelectionMode() => SelectionMode;
    public override string? GetHeader() => Header;
    public override bool GetIsItemClickEnabled() => OnItemClick is not null;
    public override Element BuildItemView(int index) => ViewBuilder(Items[index], index);
    internal override string GetKeyAt(int index) => KeySelector(Items[index]);
    public override void InvokeSelectionChanged(int index) => OnSelectedIndexChanged?.Invoke(index);
    public override void InvokeItemClick(int index) =>
        OnItemClick?.Invoke(index >= 0 && index < Items.Count ? Items[index] : default!);
    public override void ApplyControlSetters(object control) =>
        Reconciler.ApplySetters(Setters, (WinUI.ListView)control);
    /// <summary>Snapshot-style multi-select callback. Materializes the typed items from the given indices.</summary>
    internal override void InvokeMultiSelectionChanged(IReadOnlyList<int> indices)
    {
        if (OnSelectionChanged is null) return;
        var selected = new List<T>(indices.Count);
        foreach (var i in indices)
            if (i >= 0 && i < Items.Count) selected.Add(Items[i]);
        OnSelectionChanged(selected);
    }
    internal override bool HasMultiSelectionCallback => OnSelectionChanged is not null;
    internal override bool HasCallbacks =>
        OnSelectedIndexChanged is not null || OnItemClick is not null || OnSelectionChanged is not null;
    internal override bool HasSetters => Setters.Length > 0;
}

public record TemplatedGridViewElement<T>(
    IReadOnlyList<T> Items,
    Func<T, string> KeySelector,
    Func<T, int, Element> ViewBuilder
) : TemplatedGridViewElementBase
{
    public int SelectedIndex { get; init; } = -1;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    public Action<T>? OnItemClick { get; init; }
    public ListViewSelectionMode SelectionMode { get; init; } = ListViewSelectionMode.Single;
    public string? Header { get; init; }
    /// <summary>
    /// Multi-select snapshot callback for the typed peer (see
    /// <see cref="TemplatedListViewElement{T}.OnSelectionChanged"/>).
    /// </summary>
    public Action<IReadOnlyList<T>>? OnSelectionChanged { get; init; }
    internal Action<WinUI.GridView>[] Setters { get; init; } = [];

    public override int ItemCount => Items.Count;
    public override int GetSelectedIndex() => SelectedIndex;
    public override ListViewSelectionMode GetSelectionMode() => SelectionMode;
    public override string? GetHeader() => Header;
    public override bool GetIsItemClickEnabled() => OnItemClick is not null;
    public override Element BuildItemView(int index) => ViewBuilder(Items[index], index);
    internal override string GetKeyAt(int index) => KeySelector(Items[index]);
    public override void InvokeSelectionChanged(int index) => OnSelectedIndexChanged?.Invoke(index);
    public override void InvokeItemClick(int index) =>
        OnItemClick?.Invoke(index >= 0 && index < Items.Count ? Items[index] : default!);
    public override void ApplyControlSetters(object control) =>
        Reconciler.ApplySetters(Setters, (WinUI.GridView)control);
    /// <summary>Snapshot-style multi-select callback. Materializes the typed items from the given indices.</summary>
    internal override void InvokeMultiSelectionChanged(IReadOnlyList<int> indices)
    {
        if (OnSelectionChanged is null) return;
        var selected = new List<T>(indices.Count);
        foreach (var i in indices)
            if (i >= 0 && i < Items.Count) selected.Add(Items[i]);
        OnSelectionChanged(selected);
    }
    internal override bool HasMultiSelectionCallback => OnSelectionChanged is not null;
    internal override bool HasCallbacks =>
        OnSelectedIndexChanged is not null || OnItemClick is not null || OnSelectionChanged is not null;
    internal override bool HasSetters => Setters.Length > 0;
}

public record TemplatedFlipViewElement<T>(
    IReadOnlyList<T> Items,
    Func<T, string> KeySelector,
    Func<T, int, Element> ViewBuilder
) : TemplatedFlipViewElementBase
{
    /// <summary>Selected item index. Defaults to <see cref="Optional{T}.Unset"/>; use <c>Optional.Of(-1)</c> to force no selection.</summary>
    public Optional<int> SelectedIndex { get; init; } = default;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    internal Action<WinUI.FlipView>[] Setters { get; init; } = [];

    public override int ItemCount => Items.Count;
    public override int GetSelectedIndex() => SelectedIndex.GetValueOrDefault(0);
    internal override Optional<int> GetControlledSelectedIndex() => SelectedIndex;
    public override ListViewSelectionMode GetSelectionMode() => ListViewSelectionMode.Single;
    public override string? GetHeader() => null;
    public override bool GetIsItemClickEnabled() => false;
    public override Element BuildItemView(int index) => ViewBuilder(Items[index], index);
    // FlipView pre-mounts all items so it does not participate in the
    // keyed-list ObservableCollection delta channel; return a positional
    // synthetic key so any external consumer that asks still gets a value.
    internal override string GetKeyAt(int index) =>
        KeySelector is not null && (uint)index < (uint)Items.Count
            ? KeySelector(Items[index])
            : $"__flip_{index}";
    public override void InvokeSelectionChanged(int index) => OnSelectedIndexChanged?.Invoke(index);
    public override void InvokeItemClick(int index) { }
    public override void ApplyControlSetters(object control) =>
        Reconciler.ApplySetters(Setters, (WinUI.FlipView)control);
    internal override bool HasCallbacks => OnSelectedIndexChanged is not null;
    internal override bool HasSetters => Setters.Length > 0;
}

// ════════════════════════════════════════════════════════════════════════
//  Templated (data-driven) hierarchical TreeView
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Abstract non-generic base for the typed, data-driven <c>TreeView</c>.
/// Non-generic so the reconciler can match a single type in its switch
/// expression (same type-erasure pattern as <see cref="TemplatedListElementBase"/>).
///
/// <para>This is the hierarchical peer of <see cref="TemplatedListViewElement{T}"/>:
/// the developer supplies their own data items, a key selector, a children
/// selector (the hierarchy), and a <c>viewBuilder</c> (<c>data → Element</c>,
/// the WinUI <c>ItemTemplate</c> equivalent). It exists because WinUI's
/// node-mode <c>TreeView</c> stringifies <c>TreeViewNode.Content</c> and
/// cannot host a pre-built <c>UIElement</c> — rich per-node visuals must come
/// from a template, never an element instance (the root cause of issue #447).</para>
///
/// <para>The base exposes object-erased accessors; the generic leaf casts back
/// to <c>T</c>. Reference-type <c>T</c> flows through the covariant
/// <see cref="IReadOnlyList{T}"/> → <c>IReadOnlyList&lt;object&gt;</c>
/// conversion; value-type <c>T</c> is boxed once via the leaf's projection
/// helper.</para>
/// </summary>
public abstract record TemplatedTreeViewElementBase : Element
{
    /// <summary>The root data items (object-erased), in document order.</summary>
    public abstract IReadOnlyList<object> GetRoots();
    /// <summary>The children of <paramref name="item"/>, or null for a leaf.</summary>
    public abstract IReadOnlyList<object>? GetChildren(object item);
    /// <summary>The stable identity string for <paramref name="item"/> (the keyed-diff key).</summary>
    public abstract string GetKey(object item);
    /// <summary>Builds the per-node view (the <c>ItemTemplate</c> equivalent).</summary>
    public abstract Element BuildView(object item);
    /// <summary>Whether <paramref name="item"/>'s node should start expanded.</summary>
    public abstract bool GetIsExpanded(object item);
    /// <summary>Dispatches <c>OnItemInvoked</c> with the developer's own <c>T</c>.</summary>
    public abstract void InvokeItemInvoked(object item);
    /// <summary>Dispatches <c>OnExpanding</c> with the developer's own <c>T</c>.</summary>
    public abstract void InvokeExpanding(object item);

    public abstract TreeViewSelectionMode GetSelectionMode();
    public abstract bool GetCanDragItems();
    public abstract bool GetAllowDrop();
    public abstract bool GetCanReorderItems();
    public abstract void ApplyControlSetters(object control);

    /// <summary>
    /// True when programmatic setter actions (.Set(...)) are attached. Used by
    /// <see cref="Element.OwnPropsEqual"/> to suppress the reconcile-highlight
    /// short-circuit (same rationale as <see cref="TemplatedListElementBase.HasSetters"/>).
    /// </summary>
    internal virtual bool HasSetters => false;
}

/// <summary>
/// Typed, data-driven <c>TreeView</c>. The hierarchical peer of
/// <see cref="TemplatedListViewElement{T}"/>. See
/// <see cref="TemplatedTreeViewElementBase"/>.
/// </summary>
public record TemplatedTreeViewElement<T>(
    IReadOnlyList<T> Items,
    Func<T, string> KeySelector,
    Func<T, IReadOnlyList<T>?> ChildrenSelector,
    Func<T, Element> ViewBuilder
) : TemplatedTreeViewElementBase
{
    /// <summary>Invoked with the developer's <c>T</c> when a node is clicked/invoked.</summary>
    public Action<T>? OnItemInvoked { get; init; }
    /// <summary>Invoked with the developer's <c>T</c> just before a node expands.</summary>
    public Action<T>? OnExpanding { get; init; }
    /// <summary>Per-item initial-expansion selector. Defaults to collapsed.</summary>
    public Func<T, bool>? IsExpanded { get; init; }
    public TreeViewSelectionMode SelectionMode { get; init; } = TreeViewSelectionMode.Single;
    public bool CanDragItems { get; init; }
    public bool AllowDrop { get; init; }
    public bool CanReorderItems { get; init; }
    internal Action<WinUI.TreeView>[] Setters { get; init; } = [];

    public override IReadOnlyList<object> GetRoots() => Project(Items);
    public override IReadOnlyList<object>? GetChildren(object item)
    {
        var children = ChildrenSelector((T)item);
        return children is null ? null : Project(children);
    }
    public override string GetKey(object item) => KeySelector((T)item);
    public override Element BuildView(object item) => ViewBuilder((T)item);
    public override bool GetIsExpanded(object item) => IsExpanded?.Invoke((T)item) ?? false;
    public override void InvokeItemInvoked(object item) => OnItemInvoked?.Invoke((T)item);
    public override void InvokeExpanding(object item) => OnExpanding?.Invoke((T)item);

    public override TreeViewSelectionMode GetSelectionMode() => SelectionMode;
    public override bool GetCanDragItems() => CanDragItems;
    public override bool GetAllowDrop() => AllowDrop;
    public override bool GetCanReorderItems() => CanReorderItems;
    public override void ApplyControlSetters(object control) =>
        Reconciler.ApplySetters(Setters, (WinUI.TreeView)control);

    internal override bool HasCallbacks => OnItemInvoked is not null || OnExpanding is not null;
    internal override bool HasSetters => Setters.Length > 0;

    /// <summary>
    /// Object-erases the source list. Reference-type <c>T</c> reuses the same
    /// instance through covariance (no copy); value-type <c>T</c> is boxed into
    /// a fresh <c>object[]</c>. Identity-stable mapping back to <c>T</c> is via
    /// <see cref="GetKey"/> (a string), not object reference, so the per-call
    /// boxing of value types is harmless.
    /// </summary>
    private static IReadOnlyList<object> Project(IReadOnlyList<T> source)
    {
        if (source is IReadOnlyList<object> covariant) return covariant;
        var boxed = new object[source.Count];
        for (int i = 0; i < source.Count; i++) boxed[i] = source[i]!;
        return boxed;
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Virtualized collection elements (backed by ItemsRepeater)
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Abstract base for virtualized lazy stacks. Non-generic so the reconciler
/// can match on a single type in its switch expression.
///
/// <para>Spec 047 §14 Phase 3 finish — Port (6): also implements
/// <see cref="Internal.IKeyedItemSource"/> and
/// <see cref="IItemsRepeaterFactorySource"/> so the descriptor-driven
/// G2 port (<c>LazyStackDescriptor</c>) can flow Lazy*Stack through
/// <see cref="Reconciler.BindErasedKeyedItemsSource"/>'s ItemsRepeater
/// arm — same realization plumbing as the hand-coded
/// <c>MountLazyStack</c> / <c>UpdateLazyStack</c> bodies.</para>
/// </summary>
public abstract record LazyStackElementBase : Element, Internal.IKeyedItemSource, IItemsRepeaterFactorySource
{
    public abstract Orientation Orientation { get; }
    public abstract double Spacing { get; init; }
    public abstract double EstimatedItemSize { get; init; }
    public abstract object GetItemsSource();
    /// <summary>Total number of items in the source list.</summary>
    public abstract int ItemCount { get; }
    /// <summary>
    /// Projects the user's data item at <paramref name="index"/> through
    /// the typed peer's <c>KeySelector</c> to produce the stable identity
    /// string consumed by spec 042's keyed-list reconciliation pipeline.
    /// </summary>
    internal abstract string GetKeyAt(int index);
    /// <summary>
    /// §14 Phase 3 finish — Port (6): build the per-item Element subtree
    /// for index N. Same shape as <c>TemplatedListElementBase.BuildItemView</c>;
    /// the descriptor binder reads this through the
    /// <see cref="Internal.IItemViewSource"/> contract (bridged via
    /// <see cref="Internal.IKeyedItemSource"/>) when the factory realizes
    /// a container.
    /// </summary>
    public abstract Element BuildItemView(int index);
    // IKeyedItemSource explicit bridge — forward to the existing internal
    // GetKeyAt without exposing it publicly.
    string Internal.IKeyedItemSource.GetKeyAt(int index) => GetKeyAt(index);
    public abstract IElementFactory CreateFactory(Reconciler reconciler, Action requestRerender, ElementPool? pool);
    /// <summary>
    /// Update an existing factory's items and viewBuilder in place, avoiding
    /// ItemsRepeater re-realization. Returns true if the factory was updated.
    /// </summary>
    public abstract bool TryUpdateFactory(IElementFactory existingFactory);
    /// <summary>
    /// Spec 042 Phase 1: hand the factory the host's <see cref="Internal.ReactorListState"/>
    /// so its element-tracking dictionary can be keyed by stable
    /// <see cref="Internal.ReactorRow.Key"/> instead of by realized index.
    /// Insertions at non-tail positions used to shift every entry's effective
    /// index — keying by string makes the mapping reorder-stable.
    /// </summary>
    internal abstract void AttachListStateToFactory(IElementFactory factory, Internal.ReactorListState listState);
    // IItemsRepeaterFactorySource bridge — same internal abstract surface
    // is exposed under the interface contract so the descriptor binder
    // (which only knows about the interface) can call it.
    void IItemsRepeaterFactorySource.AttachListStateToFactory(IElementFactory factory, Internal.ReactorListState listState)
        => AttachListStateToFactory(factory, listState);
    /// <summary>
    /// After updating the factory in place, reconcile all realized items
    /// with the new viewBuilder output (property diffs only, no collection changes).
    /// </summary>
    public abstract void RefreshRealizedItems(IElementFactory factory, WinUI.ItemsRepeater repeater);
    /// <summary>
    /// §14 Phase 3 finish — Port (6): set <see cref="WinUI.ItemsRepeater.Layout"/>
    /// to a <see cref="WinUI.StackLayout"/> with this element's
    /// <see cref="Orientation"/> + <see cref="Spacing"/>. Mirrors the
    /// inline assignment in legacy <c>MountLazyStack</c>
    /// (Reconciler.Mount.cs ~:3148) plus the in-place Spacing update from
    /// <c>UpdateLazyStack</c> (Reconciler.Update.cs ~:3109). Reuses the
    /// existing <c>StackLayout</c> when both orientation and spacing match
    /// — avoids re-allocating a Layout on every Update.
    /// </summary>
    void IItemsRepeaterFactorySource.ConfigureLayout(WinUI.ItemsRepeater repeater)
    {
        if (repeater.Layout is WinUI.StackLayout existing && existing.Orientation == Orientation)
        {
            // Epsilon compare per the spec-047 Phase 3-final fixture
            // convention (b0910016) — CodeQL flags `!=` on double, and the
            // engine never needs to react to sub-nanometer Spacing changes.
            if (Math.Abs(existing.Spacing - Spacing) > 1e-9) existing.Spacing = Spacing;
            return;
        }
        repeater.Layout = new WinUI.StackLayout
        {
            Orientation = Orientation,
            Spacing = Spacing,
        };
    }
    internal Action<WinUI.ScrollViewer>[] ScrollViewerSetters { get; init; } = [];
    internal Action<WinUI.ItemsRepeater>[] RepeaterSetters { get; init; } = [];
}

public record LazyVStackElement<T>(
    IReadOnlyList<T> Items,
    Func<T, string> KeySelector,
    Func<T, int, Element> ViewBuilder
) : LazyStackElementBase
{
    public override Orientation Orientation => Orientation.Vertical;
    public override double Spacing { get; init; } = 8;
    public override double EstimatedItemSize { get; init; } = 40;
    public override int ItemCount => Items.Count;
    internal override string GetKeyAt(int index) => KeySelector(Items[index]);
    public override Element BuildItemView(int index) => ViewBuilder(Items[index], index);

    public override object GetItemsSource() =>
        Enumerable.Range(0, Items.Count).ToList();

    public override IElementFactory CreateFactory(Reconciler reconciler, Action requestRerender, ElementPool? pool) =>
        new ElementFactory<T>(Items, ViewBuilder, reconciler, requestRerender, pool);

    public override bool TryUpdateFactory(IElementFactory existingFactory)
    {
        if (existingFactory is ElementFactory<T> f) { f.UpdateInPlace(Items, ViewBuilder); return true; }
        return false;
    }

    public override void RefreshRealizedItems(IElementFactory factory, WinUI.ItemsRepeater repeater)
    {
        if (factory is ElementFactory<T> f) f.RefreshRealizedItems(repeater);
    }

    internal override void AttachListStateToFactory(IElementFactory factory, Internal.ReactorListState listState)
    {
        if (factory is ElementFactory<T> f) f.AttachListState(listState);
    }
}

public record LazyHStackElement<T>(
    IReadOnlyList<T> Items,
    Func<T, string> KeySelector,
    Func<T, int, Element> ViewBuilder
) : LazyStackElementBase
{
    public override Orientation Orientation => Orientation.Horizontal;
    public override double Spacing { get; init; } = 8;
    public override double EstimatedItemSize { get; init; } = 100;
    public override int ItemCount => Items.Count;
    internal override string GetKeyAt(int index) => KeySelector(Items[index]);
    public override Element BuildItemView(int index) => ViewBuilder(Items[index], index);

    public override object GetItemsSource() =>
        Enumerable.Range(0, Items.Count).ToList();

    public override IElementFactory CreateFactory(Reconciler reconciler, Action requestRerender, ElementPool? pool) =>
        new ElementFactory<T>(Items, ViewBuilder, reconciler, requestRerender, pool);

    public override bool TryUpdateFactory(IElementFactory existingFactory)
    {
        if (existingFactory is ElementFactory<T> f) { f.UpdateInPlace(Items, ViewBuilder); return true; }
        return false;
    }

    public override void RefreshRealizedItems(IElementFactory factory, WinUI.ItemsRepeater repeater)
    {
        if (factory is ElementFactory<T> f) f.RefreshRealizedItems(repeater);
    }

    internal override void AttachListStateToFactory(IElementFactory factory, Internal.ReactorListState listState)
    {
        if (factory is ElementFactory<T> f) f.AttachListState(listState);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  ItemsRepeater<T>  (spec 047 §14 Phase 3 finish — Port (7))
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Spec 047 §14 Phase 3 finish — Port (7). Non-generic intermediate base
/// for <see cref="ItemsRepeaterElement{T}"/>. Same role as
/// <see cref="LazyStackElementBase"/>: lets the reconciler match on a
/// single type AND lets the descriptor's
/// <see cref="Reconciler.RegisterHandlerForDerivedTypes"/> registration
/// catch every closed-T variant. Implements
/// <see cref="Internal.IKeyedItemSource"/> +
/// <see cref="IItemsRepeaterFactorySource"/> for the Engine (1)
/// ItemsRepeater arm in <see cref="Reconciler.BindErasedKeyedItemsSource"/>.
///
/// <para><b>Distinct from <see cref="LazyStackElementBase"/>:</b> no
/// hard-coded <see cref="WinUI.StackLayout"/>. The element exposes a
/// nullable <see cref="Layout"/> property — the descriptor / legacy mount
/// arm assigns it directly when non-null, otherwise leaves the
/// ItemsRepeater on its default (Stack vertical). No
/// <see cref="WinUI.ScrollViewer"/> wrapping either — the rendered
/// <see cref="UIElement"/> is the bare <see cref="WinUI.ItemsRepeater"/>.
/// Authors who need scrolling host the element inside their own
/// <c>ScrollViewer</c> / <c>ScrollView</c> / <c>RefreshContainer</c>.</para>
/// </summary>
public abstract record ItemsRepeaterElementBase : Element, Internal.IKeyedItemSource, IItemsRepeaterFactorySource
{
    /// <summary>Total number of items in the source list.</summary>
    public abstract int ItemCount { get; }
    /// <summary>Per-index Element factory — same shape as
    /// <see cref="LazyStackElementBase.BuildItemView"/>.</summary>
    public abstract Element BuildItemView(int index);
    /// <summary>Stable identity projection for spec 042's keyed-list diff.</summary>
    internal abstract string GetKeyAt(int index);

    string Internal.IKeyedItemSource.GetKeyAt(int index) => GetKeyAt(index);

    /// <summary>Optional WinUI <see cref="WinUI.Layout"/>. Null = leave
    /// the ItemsRepeater on its default layout (which itself defaults to
    /// vertical <see cref="WinUI.StackLayout"/>). Authors typically pass
    /// a <c>UniformGridLayout</c> or <c>LinedFlowLayout</c> instance
    /// configured up-front; the engine reuses it across renders by
    /// reference identity (no per-update Layout allocation).</summary>
    public WinUI.Layout? Layout { get; init; }

    internal Action<WinUI.ItemsRepeater>[] RepeaterSetters { get; init; } = [];

    public abstract IElementFactory CreateFactory(Reconciler reconciler, Action requestRerender, ElementPool? pool);
    public abstract bool TryUpdateFactory(IElementFactory existingFactory);
    internal abstract void AttachListStateToFactory(IElementFactory factory, Internal.ReactorListState listState);
    public abstract void RefreshRealizedItems(IElementFactory factory, WinUI.ItemsRepeater repeater);

    void IItemsRepeaterFactorySource.AttachListStateToFactory(IElementFactory factory, Internal.ReactorListState listState)
        => AttachListStateToFactory(factory, listState);

    /// <summary>Assign the author-supplied <see cref="Layout"/> when
    /// non-null and not already in place; otherwise leave the
    /// ItemsRepeater on whatever default layout it constructed
    /// itself with (vertical StackLayout). Reference-equality reuse —
    /// passing the same Layout instance every render is a no-op.</summary>
    void IItemsRepeaterFactorySource.ConfigureLayout(WinUI.ItemsRepeater repeater)
    {
        if (Layout is null) return;
        if (!ReferenceEquals(repeater.Layout, Layout))
            repeater.Layout = Layout;
    }
}

/// <summary>
/// Spec 047 §14 Phase 3 finish — Port (7). Typed peer of
/// <see cref="ItemsRepeaterElementBase"/>. The lambdas mirror the
/// <see cref="LazyVStackElement{T}"/> / <see cref="LazyHStackElement{T}"/>
/// shape (Items + KeySelector + ViewBuilder) so authors moving from
/// Lazy*Stack to a custom-layout ItemsRepeater find the surface
/// identical.
/// </summary>
public record ItemsRepeaterElement<T>(
    IReadOnlyList<T> Items,
    Func<T, string> KeySelector,
    Func<T, int, Element> ViewBuilder
) : ItemsRepeaterElementBase
{
    public override int ItemCount => Items.Count;
    public override Element BuildItemView(int index) => ViewBuilder(Items[index], index);
    internal override string GetKeyAt(int index) => KeySelector(Items[index]);

    public override IElementFactory CreateFactory(Reconciler reconciler, Action requestRerender, ElementPool? pool) =>
        new ElementFactory<T>(Items, ViewBuilder, reconciler, requestRerender, pool);

    public override bool TryUpdateFactory(IElementFactory existingFactory)
    {
        if (existingFactory is ElementFactory<T> f) { f.UpdateInPlace(Items, ViewBuilder); return true; }
        return false;
    }

    public override void RefreshRealizedItems(IElementFactory factory, WinUI.ItemsRepeater repeater)
    {
        if (factory is ElementFactory<T> f) f.RefreshRealizedItems(repeater);
    }

    internal override void AttachListStateToFactory(IElementFactory factory, Internal.ReactorListState listState)
    {
        if (factory is ElementFactory<T> f) f.AttachListState(listState);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Shape elements
// ════════════════════════════════════════════════════════════════════════

// Spec 058 §15 (P5.4) — FULL wrapper generation: the record body (props + Setters),
// the descriptor, the registration cctor, and a static factory are all generated.
// RegisterAssembly = false because WinUI's built-in control assembly has no
// IXamlMetadataProvider (the call throws in the headless test host).
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(WinShapes.Rectangle),
    AutoDiscover = false, RegisterAssembly = false,
    Include = new[] { "Fill", "Stroke", "StrokeThickness", "RadiusX", "RadiusY" })]
public partial record RectangleElement;

// Spec 058 §15 (P5.4) — FULL wrapper generation (see RectangleElement above).
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorWrapper(typeof(WinShapes.Ellipse),
    AutoDiscover = false, RegisterAssembly = false,
    Include = new[] { "Fill", "Stroke", "StrokeThickness" })]
public partial record EllipseElement;

public partial record LineElement() : Element
{
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
    public Brush? Stroke { get; init; }
    public double StrokeThickness { get; init; } = 1;
    internal Action<WinShapes.Line>[] Setters { get; init; } = [];
}

// Spec 058 §15 (P5.4) — descriptor-only migration.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinShapes.Line))]
public partial record LineElement;

// Spec 058 §15 (P5.23) — Shape leaf. Fill/Stroke/StrokeThickness/RenderTransform/line caps/join/
// miter/offset auto-map. Bespoke in Customize: Data (3-strategy .Imperative: XamlReader → pre-built
// Geometry → PathDataParser; PathDataString + Data), FillRule (propagated onto the inner
// PathGeometry), StrokeDashArray (DoubleCollection — unsupported value type). Replaces PathDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinShapes.Path))]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Data")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("PathDataString")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("FillRule")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("StrokeDashArray")]
public partial record PathElement() : Element
{
    /// <summary>
    /// Pre-parsed WinUI Geometry. When null, the reconciler resolves from <see cref="PathDataString"/>.
    /// Callers that construct PathElement directly (not via D3Path) can set this for non-SVG geometries.
    /// </summary>
    public Geometry? Data { get; init; }
    /// <summary>
    /// The original SVG path data string. When set, geometry is parsed lazily by the reconciler —
    /// only when mounting or when the string changes between renders. This avoids expensive
    /// PathDataParser.Parse + COM Geometry creation on every tree build.
    /// </summary>
    public string? PathDataString { get; init; }
    public Brush? Fill { get; init; }
    public Brush? Stroke { get; init; }
    public double StrokeThickness { get; init; } = 1;
    public Microsoft.UI.Xaml.Media.DoubleCollection? StrokeDashArray { get; init; }
    public Transform? RenderTransform { get; init; }
    /// <summary>Cap rendered at the start of an open stroke. Defaults to <c>Flat</c>.</summary>
    public PenLineCap StrokeStartLineCap { get; init; } = PenLineCap.Flat;
    /// <summary>Cap rendered at the end of an open stroke. Defaults to <c>Flat</c>.</summary>
    public PenLineCap StrokeEndLineCap { get; init; } = PenLineCap.Flat;
    /// <summary>Join style between two connected stroke segments. Defaults to <c>Miter</c>.</summary>
    public PenLineJoin StrokeLineJoin { get; init; } = PenLineJoin.Miter;
    /// <summary>Maximum extent of a miter join relative to half the stroke thickness. Defaults to 10.</summary>
    public double StrokeMiterLimit { get; init; } = 10;
    /// <summary>Cap rendered on dashes when <see cref="StrokeDashArray"/> is set. Defaults to <c>Flat</c>.</summary>
    public PenLineCap StrokeDashCap { get; init; } = PenLineCap.Flat;
    /// <summary>Distance into the dash pattern at which to begin drawing. Defaults to 0.</summary>
    public double StrokeDashOffset { get; init; }
    /// <summary>How interior regions are determined for fills. Defaults to <c>EvenOdd</c>.</summary>
    public FillRule FillRule { get; init; } = FillRule.EvenOdd;
    internal Action<WinShapes.Path>[] Setters { get; init; } = [];

    // Data has two source surfaces (pre-built Geometry vs SVG string) + 3-way branching that the
    // value-comparer fast path can't express; FillRule writes onto the inner PathGeometry (gated on
    // the LIVE control's Data); StrokeDashArray is a DoubleCollection. Verbatim from the descriptor.
    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<PathElement, WinShapes.Path> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<PathElement, WinShapes.Path> d)
        => d.Imperative(
                mount: static (c, e) => WriteData(c, e),
                update: static (c, oldEl, newEl) =>
                {
                    bool pathChanged = newEl.PathDataString is null
                        ? newEl.Data is not null
                        : !string.Equals(newEl.PathDataString, oldEl.PathDataString, global::System.StringComparison.Ordinal);
                    if (!pathChanged) return;
                    WriteData(c, newEl);
                })
            .OneWay(
                get: static e => e.FillRule,
                set: static (c, v) =>
                {
                    if (c.Data is global::Microsoft.UI.Xaml.Media.PathGeometry pg && pg.FillRule != v) pg.FillRule = v;
                })
            .OneWayConditional(
                get:         static e => e.StrokeDashArray,
                set:         static (c, v) => c.StrokeDashArray = v,
                shouldWrite: static e => e.StrokeDashArray is not null);

    private static void WriteData(WinShapes.Path c, PathElement e)
    {
        global::System.Exception? xamlReaderError = null;
        string? attemptedXaml = null;

        if (e.PathDataString is { Length: > 0 } pds)
        {
            try
            {
                var safe = global::System.Net.WebUtility.HtmlEncode(pds);
                attemptedXaml =
                    "<Path xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Data=\""
                    + safe + "\" />";
                if (global::Microsoft.UI.Xaml.Markup.XamlReader.Load(attemptedXaml) is WinShapes.Path loaded
                    && loaded.Data is not null)
                {
                    c.Data = loaded.Data;
                    return;
                }
            }
            catch (global::System.Exception ex)
            {
                xamlReaderError = ex;
            }
        }

        if (e.Data is not null)
        {
            try { c.Data = e.Data; }
            catch (global::System.Exception ex)
            {
                var xamlNote = xamlReaderError is not null
                    ? $" XamlReader.Load also failed: {xamlReaderError.GetType().Name}: {xamlReaderError.Message}. Attempted XAML: {attemptedXaml}"
                    : " (XamlReader.Load returned non-Path or wasn't attempted)";
                throw new global::System.ArgumentException(
                    $"Path.Data rejected by WinUI. PathDataString={e.PathDataString ?? "(null)"}; "
                    + $"DataType={e.Data.GetType().Name}; inner={ex.Message}.{xamlNote}", ex);
            }
            return;
        }

        if (e.PathDataString is { Length: > 0 } pdsFallback)
        {
            global::System.Exception? parserError = null;
            try { c.Data = PathDataParser.Parse(pdsFallback); }
            catch (global::System.Exception ex) { parserError = ex; }

            if (parserError is not null)
            {
                var xamlNote = xamlReaderError is not null
                    ? $"XamlReader.Load failed: {xamlReaderError.GetType().Name}: {xamlReaderError.Message}. Attempted XAML: {attemptedXaml}. "
                    : "XamlReader.Load returned non-Path. ";
                throw new global::System.ArgumentException(
                    $"Could not mount PathElement from PathDataString='{pdsFallback}'. "
                    + xamlNote
                    + $"PathDataParser.Parse also failed: {parserError.GetType().Name}: {parserError.Message}.",
                    parserError);
            }
        }
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Additional layout elements
// ════════════════════════════════════════════════════════════════════════

// Spec 058 §15 (P5.19) — attached-property panel with NO value props: the entire control is
// the two-pass sibling-name attached-DP strategy ([WrapPanelChildren(AfterAll=...)] →
// PanelAttachedHooks.cs). No [WrapManual]/Customize needed. Replaces the hand-written
// RelativePanelDescriptor — a full descriptor reduced to one attribute + the bespoke hook.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.RelativePanel))]
[global::Microsoft.UI.Reactor.Wrappers.WrapPanelChildren(AfterAll = "ApplyRelativePanelAttachedProps")]
public partial record RelativePanelElement(Element[] Children) : Element
{
    internal Action<WinUI.RelativePanel>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  Additional media elements
// ════════════════════════════════════════════════════════════════════════

// Spec 058 §15 (P5.18) — AreTransportControlsEnabled/AutoPlay auto-map; Source is a
// mount-only .Initial (string → MediaSource.CreateFromUri) and the MediaPlayer event
// subscription (MediaOpened/MediaEnded/MediaFailed on the inner MediaPlayer, marshalled to
// the UI thread) is a mount-only .Imperative — both bespoke, so handled in Customize. The
// events live on the inner MediaPlayer (not the control) so the generator never auto-surfaces
// them. Replaces the hand-written MediaPlayerElementDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.MediaPlayerElement))]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Source")]
public partial record MediaPlayerElementElement(string? Source = null) : Element
{
    public bool AreTransportControlsEnabled { get; init; } = true;
    public bool AutoPlay { get; init; }

    /// <summary>
    /// Raised when the underlying <c>MediaPlayer</c> finishes opening the
    /// source. Marshalled to the element's UI thread; may fire after the
    /// element has unmounted (the handler is safe to ignore in that case).
    /// </summary>
    public Action? OnMediaOpened { get; init; }

    /// <summary>
    /// Raised when playback reaches the end of the source. Marshalled to the
    /// UI thread.
    /// </summary>
    public Action? OnMediaEnded { get; init; }

    /// <summary>
    /// Raised when the underlying <c>MediaPlayer</c> fails to open or play.
    /// Receives the failure error message as a string. Marshalled to the UI
    /// thread.
    /// </summary>
    public Action<string>? OnMediaFailed { get; init; }

    internal Action<WinUI.MediaPlayerElement>[] Setters { get; init; } = [];

    // Source (mount-only MediaSource conversion) + the inner-MediaPlayer event wiring are
    // bespoke; reproduced verbatim from the deleted MediaPlayerElementDescriptor.
    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<MediaPlayerElementElement, WinUI.MediaPlayerElement> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<MediaPlayerElementElement, WinUI.MediaPlayerElement> d)
        => d.Imperative(
                mount:  static (c, _) => SubscribeMediaPlayerEvents(c),
                update: static (_, _, _) => { })
            .Initial(
                get: static e => e.Source,
                set: static (c, v) =>
                {
                    if (v is not null && global::System.Uri.TryCreate(v, global::System.UriKind.RelativeOrAbsolute, out var uri))
                        c.Source = global::Windows.Media.Core.MediaSource.CreateFromUri(uri);
                });

    private static void SubscribeMediaPlayerEvents(WinUI.MediaPlayerElement control)
    {
        var player = control.MediaPlayer;
        if (player is null) return;
        player.MediaOpened += (_, _) => DispatchToElement(control, static el => el.OnMediaOpened?.Invoke());
        player.MediaEnded += (_, _) => DispatchToElement(control, static el => el.OnMediaEnded?.Invoke());
        player.MediaFailed += (_, args) =>
        {
            var message = args.ErrorMessage ?? args.Error.ToString();
            DispatchToElement(control, el => el.OnMediaFailed?.Invoke(message));
        };
    }

    private static void DispatchToElement(global::Microsoft.UI.Xaml.FrameworkElement control, global::System.Action<MediaPlayerElementElement> body)
    {
        var dispatcher = control.DispatcherQueue;
        if (dispatcher is null) return;
        dispatcher.TryEnqueue(() =>
        {
            if (global::Microsoft.UI.Reactor.Core.Reconciler.GetElementTag(control) is MediaPlayerElementElement element) body(element);
        });
    }
}

public partial record AnimatedVisualPlayerElement() : Element
{
    public bool AutoPlay { get; init; }
    internal Action<WinUI.AnimatedVisualPlayer>[] Setters { get; init; } = [];
}

// Spec 058 §15 (P5.4) — descriptor-only migration.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.AnimatedVisualPlayer))]
public partial record AnimatedVisualPlayerElement;

// ════════════════════════════════════════════════════════════════════════
//  Additional collection elements
// ════════════════════════════════════════════════════════════════════════

// Spec 058 §15 (P5.25) — SemanticZoom. The 2 named view slots (ZoomedInView/ZoomedOutView, both
// Element so auto content-detection is ambiguous) are bespoke NamedSlots casting to
// ISemanticZoomInformation → [WrapManual] both + overwrite d.Children. Replaces SemanticZoomDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.SemanticZoom))]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("ZoomedInView")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("ZoomedOutView")]
public partial record SemanticZoomElement(Element ZoomedInView, Element ZoomedOutView) : Element
{
    internal Action<WinUI.SemanticZoom>[] Setters { get; init; } = [];

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<SemanticZoomElement, WinUI.SemanticZoom> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<SemanticZoomElement, WinUI.SemanticZoom> d)
    {
        d.Children = new global::Microsoft.UI.Reactor.Core.V1Protocol.NamedSlots<SemanticZoomElement, WinUI.SemanticZoom>(new[]
        {
            new global::Microsoft.UI.Reactor.Core.V1Protocol.NamedSlot<SemanticZoomElement, WinUI.SemanticZoom>(
                Name: "ZoomedInView",
                GetChild: static e => e.ZoomedInView,
                SetChild: static (c, ui) =>
                {
                    if (ui is WinUI.ISemanticZoomInformation info) c.ZoomedInView = info;
                })
            {
                GetCurrentChild = static c => c.ZoomedInView as global::Microsoft.UI.Xaml.UIElement,
            },
            new global::Microsoft.UI.Reactor.Core.V1Protocol.NamedSlot<SemanticZoomElement, WinUI.SemanticZoom>(
                Name: "ZoomedOutView",
                GetChild: static e => e.ZoomedOutView,
                SetChild: static (c, ui) =>
                {
                    if (ui is WinUI.ISemanticZoomInformation info) c.ZoomedOutView = info;
                })
            {
                GetCurrentChild = static c => c.ZoomedOutView as global::Microsoft.UI.Xaml.UIElement,
            },
        });
        return d;
    }
}

// Spec 058 §15 (P5.21) — items control. Items (string[]) auto-maps to the ItemsHost. SelectedIndex
// is bespoke: counter-echo HandCodedControlled whose trampoline twin-invokes OnSelectedIndexChanged
// + the multi-select OnSelectionChanged snapshot. SelectionChanged is Excluded. Replaces ListBoxDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.ListBox), Exclude = new[] { "SelectionChanged" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("SelectedIndex")]
public partial record ListBoxElement(string[] Items) : Element
{
    /// <summary>Selected item index. Defaults to <see cref="Optional{T}.Unset"/>; use <c>Optional.Of(-1)</c> to force no selection.</summary>
    public Optional<int> SelectedIndex { get; init; } = default;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    /// <summary>
    /// Multi-select snapshot callback. Receives the FULL list of currently
    /// selected indices. Use this in multi-select selection modes.
    /// </summary>
    public Action<IReadOnlyList<int>>? OnSelectionChanged { get; init; }
    internal Action<WinUI.ListBox>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnSelectedIndexChanged is not null || OnSelectionChanged is not null;

    private static readonly Action<int> __NoOpSelectedIndexChanged = static _ => { };

    private static readonly WinUI.SelectionChangedEventHandler __SelectionChangedTrampoline = (s, _) =>
    {
        var lb = (WinUI.ListBox)s!;
        if (!global::Microsoft.UI.Reactor.Core.Reconciler.TryGetReactorState(lb, out var state)) return;
        if (global::Microsoft.UI.Reactor.Core.ChangeEchoSuppressor.ShouldSuppress(state)) return;
        if (state.Element is not ListBoxElement el) return;
        el.OnSelectedIndexChanged?.Invoke(lb.SelectedIndex);
        if (el.OnSelectionChanged is { } h)
        {
            var snapshot = new List<int>(lb.SelectedItems.Count);
            for (int i = 0; i < lb.SelectedItems.Count; i++)
            {
                var idx = lb.Items.IndexOf(lb.SelectedItems[i]);
                if (idx >= 0) snapshot.Add(idx);
            }
            h(snapshot);
        }
    };

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ListBoxElement, WinUI.ListBox> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ListBoxElement, WinUI.ListBox> d)
        => d.HandCodedControlled<global::Microsoft.UI.Reactor.Core.V1Protocol.ListBoxEventPayload, int, WinUI.SelectionChangedEventHandler>(
            get:         static e => e.SelectedIndex,
            set:         static (c, v) => c.SelectedIndex = v,
            readBack:    static c => c.SelectedIndex,
            subscribe:   static (c, h) => c.SelectionChanged += h,
            callback:    static e =>
                e.OnSelectedIndexChanged is not null
                    ? e.OnSelectedIndexChanged
                    : (e.OnSelectionChanged is not null ? __NoOpSelectedIndexChanged : null),
            trampoline:  __SelectionChangedTrampoline,
            slotIsNull:  static p => p.SelectionChangedTrampoline is null,
            setSlot:     static (p, h) => p.SelectionChangedTrampoline = h);
}

// ════════════════════════════════════════════════════════════════════════
//  Additional navigation elements
// ════════════════════════════════════════════════════════════════════════

// Spec 058 §15 (P5.25) — SelectorBar. Items (builds SelectorBarItems with a structural comparer)
// and SelectedIndex (value-diff HandCodedControlled, with the Optional.Of(-1) force-clear sentinel)
// are bespoke → [WrapManual] + Customize. SelectionChanged does NOT auto-surface (callback is
// OnSelectedIndexChanged). Replaces SelectorBarDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.SelectorBar))]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Items")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("SelectedIndex")]
public partial record SelectorBarElement(SelectorBarItemData[] Items) : Element
{
    /// <summary>Selected item index. Defaults to <see cref="Optional{T}.Unset"/>; use <c>Optional.Of(-1)</c> to force no selection.</summary>
    public Optional<int> SelectedIndex { get; init; } = default;
    public Action<int>? OnSelectedIndexChanged { get; init; }
    internal Action<WinUI.SelectorBar>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnSelectedIndexChanged is not null;

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.SelectorBar, WinUI.SelectorBarSelectionChangedEventArgs>
        __SelectionChangedTrampoline = (s, _) =>
        {
            var bar = (WinUI.SelectorBar)s!;
            var idx = bar.Items.IndexOf(bar.SelectedItem);
            if (!global::Microsoft.UI.Reactor.Core.Reconciler.TryGetReactorState(bar, out var state)) return;
            if (global::Microsoft.UI.Reactor.Core.ChangeEchoSuppressor.ShouldSuppressEcho(state, idx)) return;
            if (state.Element is not SelectorBarElement el) return;
            el.OnSelectedIndexChanged?.Invoke(idx);
        };

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<SelectorBarElement, WinUI.SelectorBar> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<SelectorBarElement, WinUI.SelectorBar> d)
        => d.OneWay<SelectorBarItemData[]>(
                get: static e => e.Items,
                set: static (c, items) =>
                {
                    c.Items.Clear();
                    foreach (var item in items)
                    {
                        var sbi = new WinUI.SelectorBarItem { Text = item.Text };
                        if (item.Icon is not null)
                            sbi.Icon = global::Microsoft.UI.Reactor.Core.V1Protocol.IconResolver.ResolveIconForDescriptor(new SymbolIconData(item.Icon));
                        c.Items.Add(sbi);
                    }
                },
                comparer: SelectorBarItemsComparer.Instance)
            .HandCodedControlled<global::Microsoft.UI.Reactor.Core.V1Protocol.SelectorBarEventPayload, int,
                global::Windows.Foundation.TypedEventHandler<WinUI.SelectorBar, WinUI.SelectorBarSelectionChangedEventArgs>>(
                get:         static e => e.SelectedIndex,
                set:         static (c, v) =>
                {
                    if (v < 0)
                    {
                        if (c.SelectedItem is not null) c.SelectedItem = null;
                    }
                    else if (v < c.Items.Count)
                    {
                        var desired = c.Items[v];
                        if (!ReferenceEquals(c.SelectedItem, desired)) c.SelectedItem = desired;
                    }
                },
                readBack:    static c => c.Items.IndexOf(c.SelectedItem),
                subscribe:   static (c, h) => c.SelectionChanged += h,
                callback:    static e => e.OnSelectedIndexChanged,
                trampoline:  __SelectionChangedTrampoline,
                slotIsNull:  static p => p.SelectionChangedTrampoline is null,
                setSlot:     static (p, h) => p.SelectionChangedTrampoline = h,
                valueDiffEcho: true);

    private sealed class SelectorBarItemsComparer : global::System.Collections.Generic.IEqualityComparer<SelectorBarItemData[]>
    {
        public static readonly SelectorBarItemsComparer Instance = new();
        public bool Equals(SelectorBarItemData[]? a, SelectorBarItemData[]? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!a[i].Equals(b[i])) return false;
            return true;
        }
        public int GetHashCode(SelectorBarItemData[] obj) => obj.Length;
    }
}

public record SelectorBarItemData(string Text, string? Icon = null);

[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.PipsPager))]  // spec 058 §15 (P5.4)
[global::Microsoft.UI.Reactor.Wrappers.WrapControlled("SelectedPageIndex", ChangedEvent = "SelectedIndexChanged")]
public partial record PipsPagerElement(int NumberOfPages) : Element
{
    /// <summary>Selected page index. Defaults to <see cref="Optional{T}.Unset"/>; use <c>Optional.Of(-1)</c> to force no page selection.</summary>
    public Optional<int> SelectedPageIndex { get; init; } = default;
    public Action<int>? OnSelectedPageIndexChanged { get; init; }
    /// <summary>Whether the selected index wraps around the ends. Defaults to <c>None</c>.</summary>
    public PipsPagerWrapMode WrapMode { get; init; } = PipsPagerWrapMode.None;
    /// <summary>Maximum number of visible pips. Defaults to 5 (matches WinUI).</summary>
    public int MaxVisiblePips { get; init; } = 5;
    /// <summary>When the previous button shows. Defaults to <c>Collapsed</c>.</summary>
    public PipsPagerButtonVisibility PreviousButtonVisibility { get; init; } = PipsPagerButtonVisibility.Collapsed;
    /// <summary>When the next button shows. Defaults to <c>Collapsed</c>.</summary>
    public PipsPagerButtonVisibility NextButtonVisibility { get; init; } = PipsPagerButtonVisibility.Collapsed;
    internal Action<WinUI.PipsPager>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnSelectedPageIndexChanged is not null;
}

public partial record AnnotatedScrollBarElement() : Element
{
    internal Action<WinUI.AnnotatedScrollBar>[] Setters { get; init; } = [];
}

// Spec 058 §15 (P5.4) — descriptor-only migration.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.AnnotatedScrollBar))]
public partial record AnnotatedScrollBarElement;

// ════════════════════════════════════════════════════════════════════════
//  Additional overlay / container elements
// ════════════════════════════════════════════════════════════════════════

public record PopupElement(Element Child) : Element
{
    public bool IsOpen { get; init; }
    public bool IsLightDismissEnabled { get; init; } = true;
    public double HorizontalOffset { get; init; }
    public double VerticalOffset { get; init; }
    public Action? OnOpened { get; init; }
    public Action? OnClosed { get; init; }
    internal Action<WinPrim.Popup>[] Setters { get; init; } = [];
}

[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.RefreshContainer))]  // spec 058 §15 (P5.4)
public partial record RefreshContainerElement(Element Content) : Element
{
    public Action? OnRefreshRequested { get; init; }
    /// <summary>Direction the user pulls to trigger refresh. Defaults to <c>TopToBottom</c>.</summary>
    public RefreshPullDirection PullDirection { get; init; } = RefreshPullDirection.TopToBottom;
    internal Action<WinUI.RefreshContainer>[] Setters { get; init; } = [];
    internal override bool HasCallbacks => OnRefreshRequested is not null;
}

public record CommandBarFlyoutElement(
    Element Target,
    AppBarItemBase[]? PrimaryCommands = null,
    AppBarItemBase[]? SecondaryCommands = null
) : Element
{
    public FlyoutPlacementMode Placement { get; init; } = FlyoutPlacementMode.Auto;
    internal Action<WinUI.CommandBarFlyout>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  Additional date/time elements
// ════════════════════════════════════════════════════════════════════════

// Spec 058 §15 (P5.23) — SelectionMode/IsGroupLabelVisible/IsOutOfScopeEnabled/CalendarIdentifier/
// NumberOfWeeksInView/DisplayMode auto-map. Bespoke in Customize: Language (IsWellFormed gate +
// base FrameworkElement prop), MinDate/MaxDate/FirstDayOfWeek (nullable → non-nullable .Value),
// SelectedDates (CollectionDiffControlled keyed by UtcTicks). SelectedDatesChanged Excluded.
// Replaces CalendarViewDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.CalendarView), Exclude = new[] { "SelectedDatesChanged" })]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Language")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("MinDate")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("MaxDate")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("FirstDayOfWeek")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("SelectedDates")]
public partial record CalendarViewElement() : Element
{
    public CalendarViewSelectionMode SelectionMode { get; init; } = CalendarViewSelectionMode.Single;
    public bool IsGroupLabelVisible { get; init; } = true;
    public bool IsOutOfScopeEnabled { get; init; } = true;
    public string? CalendarIdentifier { get; init; }
    public string? Language { get; init; }
    /// <summary>Earliest selectable date. <c>null</c> = WinUI default (~100 years back).</summary>
    public DateTimeOffset? MinDate { get; init; }
    /// <summary>Latest selectable date. <c>null</c> = WinUI default (~100 years ahead).</summary>
    public DateTimeOffset? MaxDate { get; init; }
    /// <summary>Day of the week that starts each row. <c>null</c> = locale default.</summary>
    public global::Windows.Globalization.DayOfWeek? FirstDayOfWeek { get; init; }
    /// <summary>How many week rows to display in month mode (2–8). Defaults to 6.</summary>
    public int NumberOfWeeksInView { get; init; } = 6;
    /// <summary>Initial display mode (Month / Year / Decade). Defaults to <c>Month</c>.</summary>
    public CalendarViewDisplayMode DisplayMode { get; init; } = CalendarViewDisplayMode.Month;

    /// <summary>
    /// Initial selection. Bind for declarative selection on mount; subsequent
    /// programmatic updates re-apply only when the list reference differs.
    /// Combine with <see cref="OnSelectedDatesChanged"/> for two-way binding
    /// in multi-select mode.
    /// </summary>
    public IReadOnlyList<DateTimeOffset>? SelectedDates { get; init; }

    /// <summary>
    /// Raised when the user changes the selection. Receives a snapshot of the
    /// full selection (not just added/removed dates) — easier to bind into
    /// component state without diffing. Not raised on the initial declarative
    /// selection applied at mount.
    /// </summary>
    public Action<IReadOnlyList<DateTimeOffset>>? OnSelectedDatesChanged { get; init; }

    internal Action<WinUI.CalendarView>[] Setters { get; init; } = [];

    private static readonly global::Windows.Foundation.TypedEventHandler<WinUI.CalendarView, WinUI.CalendarViewSelectedDatesChangedEventArgs>
        __SelectedDatesChangedTrampoline = static (s, _) =>
        {
            var c = (WinUI.CalendarView)s!;
            if (!global::Microsoft.UI.Reactor.Core.Reconciler.TryGetReactorState(c, out var state)) return;
            if (global::Microsoft.UI.Reactor.Core.ChangeEchoSuppressor.ShouldSuppress(state)) return;
            if (state.Element is CalendarViewElement el && el.OnSelectedDatesChanged is { } h)
                h(global::System.Linq.Enumerable.ToArray(c.SelectedDates));
        };

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<CalendarViewElement, WinUI.CalendarView> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<CalendarViewElement, WinUI.CalendarView> d)
        => d.OneWayConditional(
                get:         static e => e.Language,
                set:         static (c, v) => c.Language = v!,
                shouldWrite: static e => e.Language is not null
                                         && global::Windows.Globalization.Language.IsWellFormed(e.Language))
            .OneWayConditional(
                get:         static e => e.MinDate,
                set:         static (c, v) => c.MinDate = v!.Value,
                shouldWrite: static e => e.MinDate.HasValue)
            .OneWayConditional(
                get:         static e => e.MaxDate,
                set:         static (c, v) => c.MaxDate = v!.Value,
                shouldWrite: static e => e.MaxDate.HasValue)
            .OneWayConditional(
                get:         static e => e.FirstDayOfWeek,
                set:         static (c, v) => c.FirstDayOfWeek = v!.Value,
                shouldWrite: static e => e.FirstDayOfWeek.HasValue)
            .CollectionDiffControlled<
                global::Microsoft.UI.Reactor.Core.V1Protocol.CalendarViewEventPayload,
                DateTimeOffset,
                long,
                global::Windows.Foundation.TypedEventHandler<WinUI.CalendarView, WinUI.CalendarViewSelectedDatesChangedEventArgs>>(
                get:             static e => e.SelectedDates ?? global::System.Array.Empty<DateTimeOffset>(),
                getVector:       static c => c.SelectedDates,
                key:             static dt => dt.UtcTicks,
                subscribe:       static (c, h) => c.SelectedDatesChanged += h,
                callbackPresent: static e => e.OnSelectedDatesChanged,
                trampoline:      __SelectedDatesChangedTrampoline,
                slotIsNull:      static p => p.SelectedDatesChangedTrampoline is null,
                setSlot:         static (p, h) => p.SelectedDatesChangedTrampoline = h);
}

// ════════════════════════════════════════════════════════════════════════
//  SwipeControl
// ════════════════════════════════════════════════════════════════════════

public record SwipeItemData(
    string Text,
    Action? OnInvoked = null,
    Microsoft.UI.Xaml.Controls.IconSource? IconSource = null,
    Microsoft.UI.Xaml.Media.Brush? Background = null,
    Microsoft.UI.Xaml.Media.Brush? Foreground = null,
    Microsoft.UI.Xaml.Controls.SwipeBehaviorOnInvoked BehaviorOnInvoked = Microsoft.UI.Xaml.Controls.SwipeBehaviorOnInvoked.Auto);

// Spec 058 §15 (P5.25) — SwipeControl. Content auto-maps (SingleContent). LeftItems/RightItems +
// their modes build SwipeItems collections via a bespoke .Imperative → [WrapManual] + Customize.
// Replaces SwipeControlDescriptor.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.SwipeControl))]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("LeftItems")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("RightItems")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("LeftItemsMode")]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("RightItemsMode")]
public partial record SwipeControlElement(Element Content) : Element
{
    public SwipeItemData[]? LeftItems { get; init; }
    public SwipeItemData[]? RightItems { get; init; }
    public Microsoft.UI.Xaml.Controls.SwipeMode LeftItemsMode { get; init; } = Microsoft.UI.Xaml.Controls.SwipeMode.Reveal;
    public Microsoft.UI.Xaml.Controls.SwipeMode RightItemsMode { get; init; } = Microsoft.UI.Xaml.Controls.SwipeMode.Reveal;
    internal Action<WinUI.SwipeControl>[] Setters { get; init; } = [];

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<SwipeControlElement, WinUI.SwipeControl> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<SwipeControlElement, WinUI.SwipeControl> d)
        => d.Imperative(
            mount: static (c, e) => ApplySwipeItems(c, e, force: true),
            update: static (c, o, n) => ApplySwipeItems(c, n,
                force: !ReferenceEquals(o.LeftItems, n.LeftItems)
                    || !ReferenceEquals(o.RightItems, n.RightItems)
                    || o.LeftItemsMode != n.LeftItemsMode
                    || o.RightItemsMode != n.RightItemsMode));

    private static void ApplySwipeItems(WinUI.SwipeControl control, SwipeControlElement element, bool force)
    {
        if (!force) return;
        control.LeftItems = CreateSwipeItems(element.LeftItems, element.LeftItemsMode);
        control.RightItems = CreateSwipeItems(element.RightItems, element.RightItemsMode);
    }

    private static WinUI.SwipeItems? CreateSwipeItems(SwipeItemData[]? data, WinUI.SwipeMode mode)
    {
        if (data is not { Length: > 0 }) return null;
        var items = new WinUI.SwipeItems { Mode = mode };
        foreach (var entry in data) items.Add(CreateSwipeItem(entry));
        return items;
    }

    private static WinUI.SwipeItem CreateSwipeItem(SwipeItemData data)
    {
        var item = new WinUI.SwipeItem
        {
            Text = data.Text,
            BehaviorOnInvoked = data.BehaviorOnInvoked,
        };
        if (data.IconSource is not null) item.IconSource = data.IconSource;
        if (data.Background is not null) item.Background = data.Background;
        if (data.Foreground is not null) item.Foreground = data.Foreground;
        if (data.OnInvoked is not null) item.Invoked += (_, _) => data.OnInvoked();
        return item;
    }
}

// ════════════════════════════════════════════════════════════════════════
//  AnimatedIcon
// ════════════════════════════════════════════════════════════════════════

// Spec 058 §15 (P5.6) — Source is object on the record, cast to IAnimatedVisualSource2
// (silent no-op otherwise) — bespoke set, handled via [WrapManual]. FallbackIconSource auto-maps.
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.AnimatedIcon))]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Source")]
public partial record AnimatedIconElement() : Element
{
    public object? Source { get; init; }
    public IconSource? FallbackIconSource { get; init; }
    internal Action<WinUI.AnimatedIcon>[] Setters { get; init; } = [];

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<AnimatedIconElement, WinUI.AnimatedIcon> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<AnimatedIconElement, WinUI.AnimatedIcon> d)
        => d.OneWayConditional(
            get:         static e => e.Source,
            set:         static (c, v) => { if (v is WinUI.IAnimatedVisualSource2 src) c.Source = src; },
            shouldWrite: static e => e.Source is not null);
}

// ════════════════════════════════════════════════════════════════════════
//  ParallaxView
// ════════════════════════════════════════════════════════════════════════

[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.ParallaxView))]  // spec 058 §15 (P5.6)
[global::Microsoft.UI.Reactor.Wrappers.WrapContent("Child")]  // ParallaxView displays Child, not the inherited ContentControl.Content
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("Source")]  // Source is UIElement (unsupported value type) — mapped explicitly (reference-equality gate)
public partial record ParallaxViewElement(Element Child) : Element
{
    public double VerticalShift { get; init; }
    public double HorizontalShift { get; init; }
    /// <summary>Source UIElement that drives the parallax (typically a ScrollViewer / ListView). <c>null</c> uses the nearest scroller.</summary>
    public UIElement? Source { get; init; }
    /// <summary>Vertical-axis source offset (in pixels) at which parallax begins. Defaults to 0.</summary>
    public double VerticalSourceStartOffset { get; init; }
    /// <summary>Vertical-axis source offset (in pixels) at which parallax ends. Defaults to 0 (auto).</summary>
    public double VerticalSourceEndOffset { get; init; }
    internal Action<WinUI.ParallaxView>[] Setters { get; init; } = [];

    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ParallaxViewElement, WinUI.ParallaxView> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<ParallaxViewElement, WinUI.ParallaxView> d)
        => d.OneWayConditional(
            get:         static e => e.Source,
            set:         static (c, v) => c.Source = v!,
            shouldWrite: static e => e.Source is not null);
}

// ════════════════════════════════════════════════════════════════════════
//  MapControl
// ════════════════════════════════════════════════════════════════════════

[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.MapControl))]  // spec 058 §15 (P5.4)
public partial record MapControlElement() : Element
{
    public string? MapServiceToken { get; init; }
    public double ZoomLevel { get; init; } = 1;
    internal Action<WinUI.MapControl>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  Frame
// ════════════════════════════════════════════════════════════════════════

// Spec 058 §15 (P5.6) — navigation is mount-only (Navigate once), modeled via a
// [WrapManual] Customize `.Initial` projecting SourcePageType + NavigationParameter.
// The three navigation events use [WrapEvent] typed projections; NavigationFailed
// is a MULTI-arg projection (SourcePageType + Exception).
[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.Frame))]
[global::Microsoft.UI.Reactor.Wrappers.WrapManual("SourcePageType")]
[global::Microsoft.UI.Reactor.Wrappers.WrapEvent("Navigated", Arg = "SourcePageType")]
[global::Microsoft.UI.Reactor.Wrappers.WrapEvent("Navigating", Arg = "SourcePageType")]
[global::Microsoft.UI.Reactor.Wrappers.WrapEvent("NavigationFailed", Args = new[] { "SourcePageType", "Exception" })]
public partial record FrameElement() : Element
{
    public Type? SourcePageType { get; init; }
    public object? NavigationParameter { get; init; }

    /// <summary>Raised after a successful navigation. Receives the new <c>SourcePageType</c>.</summary>
    public Action<Type>? OnNavigated { get; init; }

    /// <summary>Raised before navigation begins. Receives the target <c>SourcePageType</c>. Cancellation is not supported via this fluent — use <c>.Set(...)</c> to wire the raw <c>Navigating</c> event for that.</summary>
    public Action<Type>? OnNavigating { get; init; }

    /// <summary>Raised when a navigation fails. Receives the target <c>SourcePageType</c> and the failure exception.</summary>
    public Action<Type, Exception>? OnNavigationFailed { get; init; }

    internal Action<WinUI.Frame>[] Setters { get; init; } = [];

    // Mount-only navigation: re-running on Update would re-navigate on every
    // record-with. A single .Initial entry projects both SourcePageType +
    // NavigationParameter so the set lambda has both pieces.
    private static partial global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<FrameElement, WinUI.Frame> Customize(
        global::Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<FrameElement, WinUI.Frame> d)
        => d.Initial<(Type? pageType, object? param)>(
            get: static e => (e.SourcePageType, e.NavigationParameter),
            set: static (c, v) =>
            {
                if (v.pageType is not null) c.Navigate(v.pageType, v.param);
            });
}

// ════════════════════════════════════════════════════════════════════════
//  ItemContainer — required wrapper for ItemsView item realizations.
//  ItemsView's selection / focus / animation infrastructure assumes its
//  ItemTemplate produces ItemContainer roots (see
//  microsoft-ui-xaml-lift/controls/dev/ItemsView/ItemsView.cpp:317). The
//  inner ItemsRepeater enters an infinite measure cycle on non-container
//  roots. A user's <see cref="ItemsViewElement{T}"/> viewBuilder therefore
//  must return an <see cref="ItemContainerElement"/> at the root —
//  enforced at mount time with a clear exception rather than the hang
//  WinUI would otherwise hit.
// ════════════════════════════════════════════════════════════════════════

[global::Microsoft.UI.Reactor.Wrappers.GenerateReactorDescriptor(typeof(WinUI.ItemContainer))]  // spec 058 §15 (P5.6)
[global::Microsoft.UI.Reactor.Wrappers.WrapContent("Child")]  // ItemContainer's content slot is Child
public partial record ItemContainerElement(Element? Child) : Element
{
    /// <summary>Selection state as exposed by <c>ItemContainer.IsSelected</c>.</summary>
    public bool IsSelected { get; init; }
    internal Action<WinUI.ItemContainer>[] Setters { get; init; } = [];
}

// ════════════════════════════════════════════════════════════════════════
//  ItemsView
// ════════════════════════════════════════════════════════════════════════

public enum ItemsViewLayoutKind
{
    StackLayout,
    LinedFlowLayout,
    UniformGridLayout,
}

/// <summary>
/// Non-generic base so <see cref="Reconciler"/> can pattern-match an
/// ItemsView element without knowing the user's item type. Mirrors the
/// <see cref="LazyStackElementBase"/> / <see cref="TemplatedListElementBase"/>
/// shape: virtual hooks for factory creation, in-place update,
/// per-row reconcile, and event callback dispatch.
/// </summary>
public abstract record ItemsViewElementBase : Element, global::Microsoft.UI.Reactor.Core.Internal.IKeyedItemSource
{
    int global::Microsoft.UI.Reactor.Core.Internal.IItemViewSource.ItemCount => ItemCount;
    Element global::Microsoft.UI.Reactor.Core.Internal.IItemViewSource.BuildItemView(int index) => BuildItemViewAt(index);
    string global::Microsoft.UI.Reactor.Core.Internal.IKeyedItemSource.GetKeyAt(int index) => GetKeyAt(index);

    public ItemsViewLayoutKind LayoutKind { get; init; } = ItemsViewLayoutKind.StackLayout;
    public ItemsViewSelectionMode SelectionMode { get; init; } = ItemsViewSelectionMode.Single;
    public bool IsItemInvokedEnabled { get; init; }
    /// <summary>Total number of items in the source list.</summary>
    public abstract int ItemCount { get; }
    /// <summary>Stable key for the item at <paramref name="index"/>.</summary>
    internal abstract string GetKeyAt(int index);
    public abstract IElementFactory CreateFactory(Reconciler reconciler, Action requestRerender, ElementPool? pool);
    public abstract bool TryUpdateFactory(IElementFactory existingFactory);
    public abstract void RefreshRealizedItems(IElementFactory factory, WinUI.ItemsRepeater repeater);
    internal abstract void AttachListStateToFactory(IElementFactory factory, Internal.ReactorListState listState);
    internal abstract Element BuildItemViewAt(int index);
    /// <summary>Dispatch an <c>ItemInvoked</c> event to the typed callback.</summary>
    public abstract void InvokeItemInvoked(int index);
    /// <summary>Dispatch a <c>SelectionChanged</c> snapshot to the typed callback.</summary>
    public abstract void InvokeSelectionChanged(IReadOnlyList<int> indices);
    /// <summary>
    /// Synchronously validate that the user's viewBuilder returns an
    /// <see cref="ItemContainerElement"/> root. Called by
    /// <c>MountItemsView</c> before the factory is handed to WinUI, so
    /// the exception lands on the user's call stack instead of deep
    /// inside the dispatcher-driven realize loop (where it would either
    /// crash the process or hang the framework's measure pass).
    /// No-op on empty <see cref="ItemCount"/>.
    /// </summary>
    internal abstract void PreflightFirstItem();
    internal Action<WinUI.ItemsView>[] Setters { get; init; } = [];
}

public record ItemsViewElement<T>(
    IReadOnlyList<T> Items,
    Func<T, string> KeySelector,
    Func<T, int, Element> ViewBuilder
) : ItemsViewElementBase
{
    public Action<T>? OnItemInvoked { get; init; }
    /// <summary>
    /// Multi-select snapshot callback. Receives the full list of currently
    /// selected items. Use this when <see cref="ItemsViewElementBase.SelectionMode"/>
    /// is Multiple or Extended.
    /// </summary>
    public Action<IReadOnlyList<T>>? OnSelectionChanged { get; init; }

    public override int ItemCount => Items.Count;

    internal override string GetKeyAt(int index) =>
        (uint)index < (uint)Items.Count
            ? (KeySelector(Items[index]) ?? $"__iv_{index}")
            : $"__iv_{index}";

    /// <summary>
    /// Wraps the user-supplied viewBuilder with a guard that asserts the
    /// returned root is an <see cref="ItemContainerElement"/>. WinUI's
    /// ItemsView hangs in an infinite measure cycle if the factory hands
    /// back non-ItemContainer roots (see
    /// <c>microsoft-ui-xaml-lift/controls/dev/ItemsView/ItemsView.cpp:317</c>),
    /// so converting that into a clear <see cref="global::System.InvalidOperationException"/>
    /// at the call site saves users a baffling debugging session.
    /// </summary>
    private Element GuardedViewBuilder(T item, int index)
    {
        var built = ViewBuilder(item, index);
        if (built is not ItemContainerElement)
        {
            throw new global::System.InvalidOperationException(
                $"ItemsView viewBuilder at index {index} returned {built.GetType().Name}; " +
                $"ItemsView requires an ItemContainer root — wrap with ItemContainer(...).");
        }
        return built;
    }

    internal override void PreflightFirstItem()
    {
        if (Items.Count == 0) return;
        // Just invoke the guard; any non-container root throws.
        _ = GuardedViewBuilder(Items[0], 0);
    }

    internal override Element BuildItemViewAt(int index) => GuardedViewBuilder(Items[index], index);

    public override IElementFactory CreateFactory(Reconciler reconciler, Action requestRerender, ElementPool? pool) =>
        new ElementFactory<T>(Items, GuardedViewBuilder, reconciler, requestRerender, pool);

    public override bool TryUpdateFactory(IElementFactory existingFactory)
    {
        if (existingFactory is ElementFactory<T> f) { f.UpdateInPlace(Items, GuardedViewBuilder); return true; }
        return false;
    }

    public override void RefreshRealizedItems(IElementFactory factory, WinUI.ItemsRepeater repeater)
    {
        if (factory is ElementFactory<T> f) f.RefreshRealizedItems(repeater);
    }

    internal override void AttachListStateToFactory(IElementFactory factory, Internal.ReactorListState listState)
    {
        if (factory is ElementFactory<T> f) f.AttachListState(listState);
    }

    public override void InvokeItemInvoked(int index)
    {
        if (OnItemInvoked is null) return;
        if ((uint)index < (uint)Items.Count) OnItemInvoked(Items[index]);
    }

    public override void InvokeSelectionChanged(IReadOnlyList<int> indices)
    {
        if (OnSelectionChanged is null) return;
        if (indices.Count == 0) { OnSelectionChanged(global::System.Array.Empty<T>()); return; }
        var picked = new List<T>(indices.Count);
        for (int i = 0; i < indices.Count; i++)
        {
            var idx = indices[i];
            if ((uint)idx < (uint)Items.Count) picked.Add(Items[idx]);
        }
        OnSelectionChanged(picked);
    }

    internal override bool HasCallbacks =>
        OnItemInvoked is not null || OnSelectionChanged is not null;
}
