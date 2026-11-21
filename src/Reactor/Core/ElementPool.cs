using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Per-CLR-type pool (cap 32 per lane) that recycles unmounted WinUI FrameworkElement instances.
/// </summary>
/// <remarks>
/// Each type has two lanes (spec 066 §7.2). The plain lane holds controls whose composition
/// visual was never taken. The visual lane holds controls whose visual was taken, reset to a
/// fresh control's pixels by <see cref="ResetTakenVisual"/>. Such a control still refuses the
/// facade transforms and the XAML implicit transitions, so only an element that takes the
/// visual itself and has no setters rents from it.
/// </remarks>
public sealed class ElementPool : IDisposable
{
    /// <summary>
    /// Records that the element's composition visual was handed out. The marker lives on the
    /// native control, so every managed wrapper sees it, and it is never cleared.
    /// </summary>
    internal static void MarkVisualTaken(UIElement element)
        => Reconciler.GetOrCreateReactorState(element).VisualTaken = true;

    internal static bool IsVisualTaken(UIElement element)
        => element.GetValue(Reconciler.ReactorAttached.StateProperty) is Reconciler.ReactorState { VisualTaken: true };

    /// <summary>Keeps the control out of the pool for good. A pending navigation completion may still reach it.</summary>
    internal static void ExcludeFromPool(UIElement element)
        => Reconciler.GetOrCreateReactorState(element).PoolExcluded = true;

    private const int MaxPerType = 32;

    /// <summary>
    /// When false, TryRent always returns null and Return is a no-op.
    /// Useful for scenarios like the live previewer where recycled controls
    /// with stale property state can cause visual glitches.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The exact runtime types <see cref="TryRent(Type, bool)"/> and <see cref="Return"/> recycle.
    /// Membership is tested with <see cref="HashSet{T}.Contains"/> on
    /// <see cref="object.GetType"/>, so it is an <em>exact</em>-type set: a subclass of a
    /// listed type is not poolable. <c>CheckBox</c> and <c>RelativePanel</c> are the cases
    /// that catch people out — they pass the <c>Control</c> and <c>Panel</c> gates
    /// <c>ApplyModifiers</c> dispatches on, but they are not here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This list is mirrored by name in <c>ModifierTable.PoolableTypeNames</c>
    /// (namespace <c>Microsoft.UI.Reactor.Analyzers</c>, assembly <c>Reactor.Analyzers</c>),
    /// whose <c>ModifierTable.IsPoolableTypeName</c> membership test <c>PoolResetSetAnalyzer</c>
    /// uses to decide whether <c>REACTOR_POOL_001</c> (the receiver is recycled, so the write
    /// is unwound on pool return) or <c>REACTOR_MOD_002</c>
    /// (the receiver is not recycled, so the write is never unwound — it costs the element's
    /// structural skip, not the value) describes a <c>.Set</c> write. The analyzer targets
    /// <c>netstandard2.0</c> and cannot reference this assembly, hence the copy.
    /// </para>
    /// <para>
    /// Adding or removing a type here without updating that mirror fails
    /// <c>Analyzer_Poolable_Type_Mirror_Matches_ElementPool</c>, and adding one that
    /// <see cref="CleanElement"/> does not reset fails
    /// <c>Every_Poolable_Gated_Receiver_Is_Released_By_CleanElement</c>.
    /// </para>
    /// </remarks>
    private static readonly HashSet<Type> PoolableTypes = new()
    {
        typeof(TextBlock),
        typeof(WinUI.RichTextBlock),
        typeof(WinUI.StackPanel),
        typeof(WinUI.Grid),
        typeof(WinUI.Border),
        typeof(WinUI.ScrollViewer),
        typeof(WinUI.Canvas),
        typeof(WinUI.Viewbox),
        typeof(WinUI.ProgressBar),
        typeof(WinUI.ProgressRing),
        typeof(WinUI.Image),
        typeof(WinUI.InfoBadge),
        // Interactive controls — safe to pool because the Tag-based event pattern
        // reads the current element from Tag at invocation time, so recycled controls
        // automatically dispatch to the new element's callbacks after SetElementTag.
        typeof(WinUI.Button),
        typeof(TextBox),
        typeof(WinUI.ToggleSwitch),
    };

    private readonly Dictionary<Type, Stack<FrameworkElement>> _pools = new();
    private readonly Dictionary<Type, Stack<FrameworkElement>> _visualPools = new();
    // Controls on a stack, so a second Return of the same control is a no-op. The stack holds the
    // wrapper, so its identity is stable for as long as the control is pooled.
    private readonly HashSet<FrameworkElement> _pooled = new(ReferenceEqualityComparer.Instance);

    // A scratch panel used to force WinUI to fully process parent detachment.
    // Adding then removing from this panel ensures WinUI's internal parent
    // tracking is cleared before the element goes into the pool.
    private WinUI.StackPanel? _scratchPanel;

    /// <summary>
    /// Force WinUI to fully release an element's internal parent state by
    /// round-tripping it through a scratch panel. Returns false if the element
    /// is broken (can't be re-parented) and should not be pooled.
    /// </summary>
    private bool ForceDetach(FrameworkElement element)
    {
        try
        {
            _scratchPanel ??= new WinUI.StackPanel();
            _scratchPanel.Children.Add(element);
            _scratchPanel.Children.Remove(element);
            return true;
        }
        catch (global::System.Runtime.InteropServices.COMException)
        {
            // Element has broken WinUI internal state — not safe to pool.
            return false;
        }
        catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
        {
            // No WinUI thread (e.g. unit tests) — skip validation, allow pooling.
            return true;
        }
    }

    /// <summary>
    /// Try to rent an element of the given type from the plain lane.
    /// Returns null if the lane is empty or the type is not poolable.
    /// </summary>
    public FrameworkElement? TryRent(Type type) => TryRent(type, visualLane: false);

    /// <summary>
    /// Rents from the visual lane first when <paramref name="visualLane"/> is true, then from the
    /// plain lane. The caller decides the lane from the declarations being mounted (spec 066 §7.2).
    /// </summary>
    // <snippet:pool-rent>
    internal FrameworkElement? TryRent(Type type, bool visualLane)
    {
        if (!Enabled) return null;
        if (!PoolableTypes.Contains(type)) return null;
        if (visualLane && _visualPools.TryGetValue(type, out var taken) && taken.Count > 0) return Rented(taken.Pop());
        if (!_pools.TryGetValue(type, out var stack) || stack.Count == 0) return null;
        return Rented(stack.Pop());
    }
    // </snippet:pool-rent>

    private FrameworkElement Rented(FrameworkElement element)
    {
        _pooled.Remove(element);
        return element;
    }

    /// <summary>
    /// Return an element to the pool after unmount. Cleans it first.
    /// Silently drops if the type is not poolable or its lane is full.
    /// </summary>
    // <snippet:pool-return>
    public void Return(FrameworkElement element)
    {
        if (!Enabled) return;
        var type = element.GetType();
        if (!PoolableTypes.Contains(type)) return;
        // A handler may return its control through UnmountContext.ReturnControl and the engine
        // return it again. Pushing it twice would hand one control to two renters.
        if (_pooled.Contains(element)) return;

        // The lane follows the native control's marker, whichever wrapper reached it (#1643).
        var state = element.GetValue(Reconciler.ReactorAttached.StateProperty) as Reconciler.ReactorState;
        if (state?.PoolExcluded == true) return;
        var lanes = state?.VisualTaken == true ? _visualPools : _pools;

        if (!lanes.TryGetValue(type, out var stack))
        {
            stack = new Stack<FrameworkElement>();
            lanes[type] = stack;
        }

        if (stack.Count >= MaxPerType) return;
        // </snippet:pool-return>

        // Detach from parent before pooling — WinUI doesn't allow an element in two parents.
        // Use FrameworkElement.Parent (works even for detached trees, unlike VisualTreeHelper).
        DetachFromParent(element);

        // Force WinUI to fully process the detachment by round-tripping through a
        // scratch panel. Without this, WinUI's internal parent tracking may retain
        // stale state that causes COMException when the element is re-parented later.
        // If the round-trip fails, the element is broken and must not be pooled.
        if (!ForceDetach(element))
        {
            return;
        }

        try
        {
            CleanElement(element);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or ArgumentException)
        {
            // Issue #1614: WinUI refused a transform reset (RenderTransform or a facade is
            // still locked, for example by a user .Set write), or a visual reset failed. The
            // control can't be made interchangeable with a fresh one, so drop it rather than
            // fail the reconcile that is unmounting it.
            return;
        }
        stack.Push(element);
        _pooled.Add(element);
    }

    /// <summary>
    /// Remove an element from its current parent so it can be safely re-parented.
    /// Uses FrameworkElement.Parent which works even for detached trees
    /// (unlike VisualTreeHelper.GetParent which requires a live visual tree).
    /// </summary>
    private static void DetachFromParent(FrameworkElement element)
    {
        var parent = element.Parent;
        switch (parent)
        {
            case WinUI.Panel panel:
                panel.Children.Remove(element);
                break;
            case WinUI.Border border when ReferenceEquals(border.Child, element):
                border.Child = null;
                break;
            case WinUI.ScrollViewer sv when ReferenceEquals(sv.Content, element):
                sv.Content = null;
                break;
            case WinUI.ContentControl cc when ReferenceEquals(cc.Content, element):
                cc.Content = null;
                break;
            case WinUI.UserControl uc when ReferenceEquals(uc.Content, element):
                uc.Content = null;
                break;
        }
    }

    /// <summary>
    /// Empties all per-type stacks and releases the scratch panel.
    /// Called from <see cref="Reconciler.Dispose"/> to release pooled elements.
    /// </summary>
    public void Clear()
    {
        foreach (var stack in _pools.Values)
            stack.Clear();
        _pools.Clear();
        foreach (var stack in _visualPools.Values)
            stack.Clear();
        _visualPools.Clear();
        _pooled.Clear();
        _scratchPanel = null;
    }

    /// <summary>
    /// Reset an element to a clean state suitable for reuse.
    /// </summary>
    internal static void CleanElement(FrameworkElement fe)
    {
        // Common properties
        Reconciler.ClearElementTag(fe);
        // Refresh snapshots hold the previous renter's element graphs and describe its mount.
        Reconciler.ClearRefreshSnapshots(fe);
        // SECURITY (TASK-060): clear the Current* user-handler delegates on
        // pool return so a pooled control can't fire the previous component's
        // captured rerender closure into the next mount. The underlying
        // trampoline subscription stays attached — that's intentional, see
        // the comment block in Reconciler.cs above ModifierEventHandlerState.
        Reconciler.ClearCurrentEventHandlers(fe);
        fe.Tag = null;
        // Resources keys the previous renter's .Resources(...) overrides wrote. Mount applies
        // overrides only for an element that declares some, so without this a renter that
        // declares none would inherit them. Keys set outside Reactor are left alone.
        Reconciler.RemoveManagedResourceKeys(fe);
        // Reset via ClearValue, not by assigning the DP default: a local value outranks
        // every Style setter, so writing e.g. HorizontalAlignment.Stretch would hand the
        // next renter a control that can never show its default style's alignment
        // (issue #952). ClearValue is what makes a recycled control indistinguishable
        // from a freshly-constructed one, which is the pool's whole contract.
        fe.ClearValue(FrameworkElement.MarginProperty);
        fe.ClearValue(FrameworkElement.WidthProperty);
        fe.ClearValue(FrameworkElement.HeightProperty);
        fe.ClearValue(FrameworkElement.MinWidthProperty);
        fe.ClearValue(FrameworkElement.MinHeightProperty);
        fe.ClearValue(FrameworkElement.MaxWidthProperty);
        fe.ClearValue(FrameworkElement.MaxHeightProperty);
        fe.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
        fe.ClearValue(FrameworkElement.VerticalAlignmentProperty);
        fe.ClearValue(UIElement.OpacityProperty);
        fe.ClearValue(UIElement.VisibilityProperty);
        // Spec 066 §7.3: a control whose visual was taken goes to the visual lane, reset to a
        // fresh control's pixels. Its Rotation, Scale, CenterPoint, RotationAxis and
        // TransformMatrix facades refuse every write, even identity, so they are skipped below.
        bool visualTaken = IsVisualTaken(fe);
        if (visualTaken) ResetTakenVisual(fe);
        // Issue #1614: WinUI treats RenderTransform and the facade transforms (Rotation, Scale,
        // Translation, CenterPoint, RotationAxis, TransformMatrix) as mutually exclusive, and
        // touching one while the other is engaged throws UnauthorizedAccessException. Reset the
        // facades so a recycled control can't hand its previous renter's transform to the next
        // one. They are plain properties, not DPs, so the reset assigns the identity value. Each
        // write is guarded: an untransformed control pays no extra write, and one using
        // RenderTransform (whose facades must read identity) is never written through a facade.
        if (!visualTaken)
        {
            if (fe.Rotation != 0f) fe.Rotation = 0f;
            if (fe.Scale != Vector3.One) fe.Scale = Vector3.One;
            if (fe.Translation != Vector3.Zero) fe.Translation = Vector3.Zero;
            if (fe.CenterPoint != Vector3.Zero) fe.CenterPoint = Vector3.Zero;
            if (fe.RotationAxis != Vector3.UnitZ) fe.RotationAxis = Vector3.UnitZ;
            if (!fe.TransformMatrix.IsIdentity) fe.TransformMatrix = Matrix4x4.Identity;
            // Reactor never sets the XAML implicit transitions (spec 066 §5), so only a user .Set
            // leaves one here (#1624). Any of them locks RenderTransform for good, so the clear
            // below then throws and Return drops the control (§7.4).
            if (fe.OpacityTransition is not null) fe.OpacityTransition = null;
            if (fe.ScaleTransition is not null) fe.ScaleTransition = null;
            if (fe.RotationTransition is not null) fe.RotationTransition = null;
            if (fe.TranslationTransition is not null) fe.TranslationTransition = null;
        }
        // The facades must be released first: RenderTransform rejects a write while one is
        // engaged. If it is still locked (a transform transition was attached at some point),
        // the clear throws and Return drops the control.
        fe.ClearValue(FrameworkElement.RenderTransformProperty);
        fe.ClearValue(FrameworkElement.FlowDirectionProperty);
        // A context flyout is a live object graph owned by the previous renter's component.
        // Without this, right-clicking a recycled control shows the *previous* element's menu.
        fe.ClearValue(UIElement.ContextFlyoutProperty);

        // Issue #522 defense-in-depth — the in-place Update path clears the
        // synthesized themed Style when an element transitions ThemeBindings
        // set → unset, but a full unmount (the element is removed entirely,
        // not transitioned) does not. Without clearing here, a control that
        // was themed would carry that Style into the pool and the next
        // unrelated element to rent it would inherit the prior brushes.
        // ClearThemeBindings also drops Reactor's ownership marker, so the
        // pooled control holds no reference to the previous renter's themed
        // Style. Gated on Style being non-null so non-themed controls (the
        // common case) skip the redundant COM call.
        Reconciler.ClearThemeBindings(fe);
        if (fe.Style is not null)
            fe.ClearValue(FrameworkElement.StyleProperty);

        // Clear accessibility / automation properties so pooled controls don't
        // carry stale UIA state (Name, LabeledBy, LiveSetting, etc.) into reuse.
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.NameProperty);
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.AutomationIdProperty);
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.HelpTextProperty);
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.FullDescriptionProperty);
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.LandmarkTypeProperty);
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.AccessibilityViewProperty);
        // Issue #162: chart label/tick subtree-hiding (and any other code path) sets
        // IsHitTestVisible / IsTabStop imperatively on poolable controls. The pool already
        // resets AccessibilityView above; it must also reset these two so a control hidden
        // inside a custom label can't return to the pool non-tabbable / non-hit-testable and
        // silently poison the next unrelated renter that doesn't re-set them.
        //
        // Both are cleared on `fe`, not under an `is Control` narrowing. WinUI 3 declares
        // IsTabStop on UIElement and ApplyModifiers writes it ungated (`fe.IsTabStop = …`),
        // so `.IsTabStop(false)` reaches every element — including the poolable non-Controls
        // TextBlock, RichTextBlock, Grid, StackPanel, Border, Canvas, Viewbox and Image.
        // Clearing it only for Control left every one of those able to carry a stale tab stop
        // into its next renter, which is the #985 shape; the narrower Control.IsTabStopProperty
        // also names the same underlying DP, so this is a widening, not a retarget.
        fe.ClearValue(UIElement.IsHitTestVisibleProperty);
        fe.ClearValue(UIElement.IsTabStopProperty);

        // Issue #985: six common modifiers are written by ApplyModifiers onto receivers
        // this method never reset, so a pooled control handed its previous renter's local
        // Padding / CornerRadius / BorderThickness / BorderBrush / Background / IsEnabled
        // to the next one — and a local value outranks every Style setter (same precedence
        // trap as #952, but caused by a *missing* reset rather than a wrong-shaped one).
        //
        // These live here, in the FE-common region, rather than in the type-specific arms
        // below because that is where they belong: the chain mirrors ApplyModifiers' own
        // receiver dispatch, so one clear covers every receiver the modifier can be written
        // to. It is no longer a workaround. Until issue #1193 the consistency invariants
        // stopped scanning at the type dispatch, so a clear placed after it was invisible to
        // them and ModifierTable could keep claiming the property was not pool-reset — which
        // is why #985 and #950 moved clears up here in the first place. CleanElementScan now
        // reads the whole method and attributes each reset to its receiver, so placement is
        // free again and a type-specific property can be cleared in its own arm.
        //
        // The chain mirrors ApplyModifiers' receiver types for the receivers that are actually
        // pooled (Reconciler.cs): Padding → Control | Border | Grid | StackPanel | TextBlock,
        // CornerRadius / BorderBrush / BorderThickness → Control | Border | Grid | StackPanel,
        // Background → Panel | Control | Border, IsEnabled → Control.
        // Both content-alignment properties also belong to Control (#1013).
        // These border-box modifier gates also admit RelativePanel, which gets no clear
        // here because RelativePanel is not in PoolableTypes — it is never returned to the
        // pool, so there is no local value for a later renter to inherit. Canvas is the
        // opposite case and is covered: it is poolable, and the only gate that admits it is
        // Background, which the Panel arm clears for every Panel.
        //
        // Two claims live here and only the first is structural. Dispatch: Control, Border,
        // Panel and TextBlock are pairwise disjoint, so `else if` selects exactly one arm —
        // true regardless of what the gates say. Coverage is not: each arm clears a
        // per-property allow-list, and the Panel arm clears Background for every Panel but
        // Padding and CornerRadius only for Grid and StackPanel, because Panel itself
        // declares neither. Any other poolable Panel subclass added to one of those gates
        // lands in this arm and gets no clear — the disjointness sentence stays true while
        // the guarantee it implies quietly narrows. Widening a gate obliges you to widen the
        // arm, and that is not hypothetical: #1003 widened Padding/CornerRadius to the
        // concrete panels and this arm had to grow with it in the same merge.
        // Two tests in ModifierUnsetClearValueTests now make that reminder mechanical rather
        // than a matter of vigilance, and each was measured firing alone: widening a
        // poolResetGate the pool does not honour trips
        // Every_Pool_Reset_Gate_Matches_The_Poolable_Intersection, and widening controlGate
        // and poolResetGate together without growing this arm trips
        // Every_Poolable_Gated_Receiver_Is_Released_By_CleanElement. Both range over
        // gate ∩ PoolableTypes — the reachable subset of #1017, which remains open for the
        // non-poolable receivers a gate may also admit.
        if (fe is Control resetControl)
        {
            resetControl.ClearValue(Control.PaddingProperty);
            resetControl.ClearValue(Control.CornerRadiusProperty);
            resetControl.ClearValue(Control.BorderThicknessProperty);
            resetControl.ClearValue(Control.BorderBrushProperty);
            resetControl.ClearValue(Control.BackgroundProperty);
            resetControl.ClearValue(Control.IsEnabledProperty);
            resetControl.ClearValue(Control.HorizontalContentAlignmentProperty);
            resetControl.ClearValue(Control.VerticalContentAlignmentProperty);
        }
        else if (fe is WinUI.Border resetBorder)
        {
            resetBorder.ClearValue(WinUI.Border.PaddingProperty);
            resetBorder.ClearValue(WinUI.Border.CornerRadiusProperty);
            resetBorder.ClearValue(WinUI.Border.BorderThicknessProperty);
            resetBorder.ClearValue(WinUI.Border.BorderBrushProperty);
            resetBorder.ClearValue(WinUI.Border.BackgroundProperty);
        }
        else if (fe is WinUI.Panel resetPanel)
        {
            // Panel itself declares only Background; Padding and CornerRadius are declared by
            // the concrete panel types, so they nest under this arm instead of forming a
            // second receiver chain lower down. Both forms run and both sit above the type
            // dispatch, but one chain keeps the receiver gating in a single place and avoids
            // declaring resetStack twice in two scopes that must be kept in agreement.
            resetPanel.ClearValue(WinUI.Panel.BackgroundProperty);
            if (resetPanel is WinUI.Grid resetGrid)
            {
                resetGrid.ClearValue(WinUI.Grid.PaddingProperty);
                resetGrid.ClearValue(WinUI.Grid.CornerRadiusProperty);
                resetGrid.ClearValue(WinUI.Grid.BorderThicknessProperty);
                resetGrid.ClearValue(WinUI.Grid.BorderBrushProperty);
            }
            else if (resetPanel is WinUI.StackPanel resetStack)
            {
                resetStack.ClearValue(WinUI.StackPanel.PaddingProperty);
                resetStack.ClearValue(WinUI.StackPanel.CornerRadiusProperty);
                resetStack.ClearValue(WinUI.StackPanel.BorderThicknessProperty);
                resetStack.ClearValue(WinUI.StackPanel.BorderBrushProperty);
            }
        }
        else if (fe is TextBlock resetText)
        {
            // TextBlock's padding reset predates #985 (it arrived with #950) but lived in
            // the case arm below, where no scanner could reach it — so ModifierTable's claim
            // that Padding is pool-reset on TextBlock was the one receiver in the gate that
            // nothing verified. Deleting the line used to break no test. Now it does, and
            // since #1193 it would be verified in the case arm too.
            resetText.ClearValue(TextBlock.PaddingProperty);
        }

        // spec 059: clear the TitleBar.IsDragRegion attached prop so a pooled
        // control marked .IsDragRegion(...) can't poison the next renter.
        fe.ClearValue(WinUI.TitleBar.IsDragRegionProperty);
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.IsRequiredForFormProperty);
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.LiveSettingProperty);
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.PositionInSetProperty);
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.SizeOfSetProperty);
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.LevelProperty);
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.ItemStatusProperty);
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.LabeledByProperty);
        // Spec 057 CR-002: also clear the relationship-list automation properties so a
        // pooled control doesn't carry stale DescribedBy/FlowsTo/FlowsFrom targets, and
        // drop any XYFocus navigation references for the same reason.
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.DescribedByProperty);
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.FlowsToProperty);
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.FlowsFromProperty);
        fe.ClearValue(FrameworkElement.XYFocusUpProperty);
        fe.ClearValue(FrameworkElement.XYFocusDownProperty);
        fe.ClearValue(FrameworkElement.XYFocusLeftProperty);
        fe.ClearValue(FrameworkElement.XYFocusRightProperty);
        // Same reasoning for the ToolTipService attached properties: the in-place
        // Update path clears them on a set → unset transition, but a full unmount
        // does not, so a pooled control would carry a stale tooltip (and its
        // placement / placement target) into the next unrelated renter.
        fe.ClearValue(WinUI.ToolTipService.ToolTipProperty);
        fe.ClearValue(WinUI.ToolTipService.PlacementProperty);
        fe.ClearValue(WinUI.ToolTipService.PlacementTargetProperty);
        fe.ClearValue(Microsoft.UI.Xaml.Automation.AutomationProperties.HeadingLevelProperty);
        fe.ClearValue(UIElement.AccessKeyProperty);

        // Clear flex attached properties so pooled controls don't carry stale
        // Grow/Shrink/Basis values into their next parent FlexPanel.
        fe.ClearValue(Layout.FlexPanel.GrowProperty);
        fe.ClearValue(Layout.FlexPanel.ShrinkProperty);
        fe.ClearValue(Layout.FlexPanel.BasisProperty);
        fe.ClearValue(Layout.FlexPanel.FlexMinWidthProperty);
        fe.ClearValue(Layout.FlexPanel.FlexMinHeightProperty);
        fe.ClearValue(Layout.FlexPanel.AlignSelfProperty);
        fe.ClearValue(Layout.FlexPanel.PositionProperty);
        fe.ClearValue(Layout.FlexPanel.LeftProperty);
        fe.ClearValue(Layout.FlexPanel.TopProperty);
        fe.ClearValue(Layout.FlexPanel.RightProperty);
        fe.ClearValue(Layout.FlexPanel.BottomProperty);

        // Issue #1639: .ThemeTransitions(...) writes these, and a renter that declares none would
        // otherwise animate its children with the previous renter's transitions. Cleared before
        // the type-specific arms below empty the children, so the removal doesn't play them.
        // Issue #1624: the same for .BackgroundTransition(), in both lanes (spec 066 §5.6).
        switch (fe)
        {
            case WinUI.Panel themedPanel:
                themedPanel.ClearValue(WinUI.Panel.ChildrenTransitionsProperty);
                if (themedPanel.BackgroundTransition is not null) themedPanel.BackgroundTransition = null;
                break;
            case WinUI.Border themedBorder:
                themedBorder.ClearValue(WinUI.Border.ChildTransitionsProperty);
                break;
            case WinUI.ContentControl themedContent:
                themedContent.ClearValue(WinUI.ContentControl.ContentTransitionsProperty);
                break;
        }
        // Type-specific cleanup
        switch (fe)
        {
            case WinUI.Panel panel:
                panel.Children.Clear();
                break;
            case WinUI.Border border:
                border.Child = null;
                // Border's five modifier-backed DPs are cleared in the FE-common block
                // above (issue #985) so the consistency scan can see them.
                break;
            case WinUI.ScrollViewer sv:
                sv.Content = null;
                Reconciler.ClearRichTextScrollAnchor(sv);
                break;
            case WinUI.Viewbox vb:
                vb.Child = null;
                vb.ClearValue(WinUI.Viewbox.StretchProperty);
                vb.ClearValue(WinUI.Viewbox.StretchDirectionProperty);
                break;
            case TextBlock tb:
                tb.Text = "";
                // Deliberately NOT ClearValue, unlike the arms above: TextBlock.FontSize
                // participates in property inheritance, so clearing it lets a recycled
                // TextBlock pick up an ancestor's font size instead of the WinUI default
                // and silently re-flow text. Whether the pool should adopt inheritance
                // here is a real question, but it is a rendering change independent of
                // issue #952 (FontSize's ApplyModifiers unset arm already clears), so it
                // is left for its own change.
                //
                // These nine were invisible to the pool/analyzer consistency invariants until
                // issue #1193 widened CleanElementScan past this dispatch — they are declared on
                // TextBlock alone, so unlike Padding below they could not be relocated into the
                // FE-common chain to be seen. Each now has a ModifierTable row, and the scan
                // attributes them to TextBlock rather than to FrameworkElement, which is what
                // keeps the same write on a RichTextBlock from inheriting a claim about an arm
                // that only clears Blocks.
                tb.FontSize = 14; // WinUI default
                tb.ClearValue(TextBlock.FontWeightProperty);
                tb.ClearValue(TextBlock.FontStyleProperty);
                tb.ClearValue(TextBlock.TextWrappingProperty);
                tb.ClearValue(TextBlock.TextAlignmentProperty);
                tb.ClearValue(TextBlock.TextTrimmingProperty);
                tb.ClearValue(TextBlock.IsTextSelectionEnabledProperty);
                tb.ClearValue(TextBlock.FontFamilyProperty);
                // Padding moved to the FE-common chain above (issue #985) so the pool ⇄
                // analyzer consistency scan can see it — ModifierTable's Padding row names
                // TextBlock in its control gate, and a clear the scan cannot reach is a
                // claim nothing verifies.
                break;
            case WinUI.RichTextBlock rtb:
                Reconciler.CancelInlineUiExtentPin(rtb);
                rtb.Blocks.Clear();
                break;
            case WinUI.ProgressBar pb:
                pb.IsIndeterminate = false;
                pb.Value = 0;
                pb.Minimum = 0;
                pb.Maximum = 100;
                pb.ShowError = false;
                pb.ShowPaused = false;
                break;
            case WinUI.ProgressRing pr:
                pr.IsIndeterminate = false;
                pr.IsActive = true;
                pr.Value = 0;
                pr.Minimum = 0;
                pr.Maximum = 100;
                break;
            case WinUI.Image img:
                img.Source = null;
                break;
            case WinUI.InfoBadge badge:
                badge.Value = -1; // WinUI default (hidden)
                break;

            // Interactive controls — reset transient state so no state leaks between uses.
            // Event handlers are NOT removed: the Tag-based pattern reads the current
            // element from Tag at invocation time, so stale closures are harmless.
            case WinUI.Button button:
                button.Content = null;
                button.Flyout = null;
                VisualStateManager.GoToState(button, "Normal", false);
                break;
            case TextBox textBox:
                textBox.ClearValue(TextBox.TextProperty);
                textBox.PlaceholderText = "";
                textBox.Header = null;
                textBox.IsReadOnly = false;
                textBox.AcceptsReturn = false;
                textBox.ClearValue(TextBox.TextWrappingProperty);
                VisualStateManager.GoToState(textBox, "Normal", false);
                break;
            case WinUI.ToggleSwitch toggle:
                toggle.ClearValue(WinUI.ToggleSwitch.IsOnProperty);
                toggle.OnContent = null;
                toggle.OffContent = null;
                toggle.Header = null;
                VisualStateManager.GoToState(toggle, "Normal", false);
                break;
        }
    }

    // Spec 066 §7.3 step 1: every animatable Visual property, since a .ScrollLinked expression can
    // target any of them and a keyed Memo's isn't stopped at unmount. Stopping a property also stops
    // its subchannels ("Offset.Y", ElementPoolLanes_MemoScrollLinked). StopAnimation leaves the last
    // animated value, so step 3 writes identity after it.
    private static readonly string[] TakenVisualChannels =
    [
        "Opacity", "Scale", "RotationAngle", "RotationAngleInDegrees", "RotationAxis", "CenterPoint",
        "Offset", "Size", "Orientation", "AnchorPoint", "TransformMatrix",
        "RelativeOffsetAdjustment", "RelativeSizeAdjustment",
    ];

    /// <summary>
    /// The spec 066 §7.3 reset list: makes a control whose visual was taken pixel-identical to a
    /// fresh one. The visual itself stays taken. Offset and Size keep the values layout last wrote.
    /// </summary>
    private static void ResetTakenVisual(FrameworkElement fe)
    {
        var visual = ElementCompositionPreview.GetElementVisual(fe);
        // Translation is a property-set entry that exists only once translation was enabled;
        // stopping or writing a missing entry throws ArgumentException (#1641).
        bool hasTranslation = visual.Properties.TryGetVector3("Translation", out _) == CompositionGetValueStatus.Succeeded;
        // Layout owns Offset and Size and writes them only when they change, so a stopped animation's
        // last value would stay. What the getter returns before the stop is what layout last wrote.
        var layoutOffset = visual.Offset;
        var layoutSize = visual.Size;
        foreach (var channel in TakenVisualChannels)
            visual.StopAnimation(channel);
        if (hasTranslation) visual.StopAnimation("Translation");
        if (visual.Offset != layoutOffset) visual.Offset = layoutOffset;
        if (visual.Size != layoutSize) visual.Size = layoutSize;

        // ClearElementTag already released the Reactor claims (ImplicitAnimationSlots.ReleaseAll);
        // this drops keys no claim owns, for example ones user code installed.
        visual.ImplicitAnimations = null;

        visual.Opacity = 1;
        visual.IsVisible = true;
        visual.Scale = Vector3.One;
        visual.RotationAngle = 0;
        visual.RotationAxis = Vector3.UnitZ;
        visual.CenterPoint = Vector3.Zero;
        visual.AnchorPoint = Vector2.Zero;
        visual.Orientation = Quaternion.Identity;
        visual.TransformMatrix = Matrix4x4.Identity;
        // Layout writes Offset and Size but never the relative adjustments, so user code or an
        // expression may have left them.
        if (visual.RelativeOffsetAdjustment != Vector3.Zero) visual.RelativeOffsetAdjustment = Vector3.Zero;
        if (visual.RelativeSizeAdjustment != Vector2.Zero) visual.RelativeSizeAdjustment = Vector2.Zero;
        visual.Clip = null;
        if (hasTranslation) visual.Properties.InsertVector3("Translation", Vector3.Zero);
        ElementCompositionPreview.SetIsTranslationEnabled(fe, false);
        // The Translation facade stays writable after the handoff, and a user .Set may have
        // written it next to a Reactor feature.
        if (fe.Translation != Vector3.Zero) fe.Translation = Vector3.Zero;
        // A brush transition coexists with the handoff, so a visual-lane renter would inherit it.
        // Spec 066 layer 2 clears it in both lanes.
        switch (fe)
        {
            case WinUI.Panel panel when panel.BackgroundTransition is not null: panel.BackgroundTransition = null; break;
            case WinUI.Border border when border.BackgroundTransition is not null: border.BackgroundTransition = null; break;
        }

        Reconciler.ForgetCompositionState(fe);
    }

    public void Dispose()
    {
        foreach (var stack in _pools.Values.Concat(_visualPools.Values))
        {
            while (stack.Count > 0)
            {
                var element = stack.Pop();
                if (element is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
        _pools.Clear();
        _visualPools.Clear();
        _pooled.Clear();
    }
}
