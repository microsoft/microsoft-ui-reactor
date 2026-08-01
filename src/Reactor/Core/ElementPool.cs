using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Per-CLR-type pool (cap 32) that recycles unmounted WinUI FrameworkElement instances.
/// V1: pools only non-interactive controls (no event handlers to worry about).
/// </summary>
public sealed class ElementPool : IDisposable
{
    /// <summary>
    /// Tracks UIElements that have had GetElementVisual() called on them.
    /// These elements permanently lose the ability to use XAML implicit transition APIs
    /// (OpacityTransition, ScaleTransition, etc.), so they must not be pooled — a future
    /// user of the element might need those APIs.
    /// </summary>
    private static readonly ConditionalWeakTable<UIElement, object> _compositorTainted = new();

    /// <summary>
    /// Marks a UIElement as having been accessed via GetElementVisual().
    /// Called by reconciler code that touches the composition Visual.
    /// </summary>
    internal static void MarkCompositorTainted(UIElement element)
    {
        _compositorTainted.AddOrUpdate(element, true);
    }

    internal static bool IsCompositorTainted(UIElement element)
    {
        return _compositorTainted.TryGetValue(element, out _);
    }

    private const int MaxPerType = 32;

    /// <summary>
    /// When false, TryRent always returns null and Return is a no-op.
    /// Useful for scenarios like the live previewer where recycled controls
    /// with stale property state can cause visual glitches.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The exact runtime types <see cref="TryRent"/> and <see cref="Return"/> recycle.
    /// Membership is tested with <see cref="HashSet{T}.Contains"/> on
    /// <see cref="object.GetType"/>, so it is an <em>exact</em>-type set: a subclass of a
    /// listed type is not poolable. <c>CheckBox</c> and <c>RelativePanel</c> are the cases
    /// that catch people out — they pass the <c>Control</c> and <c>Panel</c> gates
    /// <c>ApplyModifiers</c> dispatches on, but they are not here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This list is mirrored by name in <c>ModifierTable.PoolableTypeNameSet</c>
    /// (namespace <c>Microsoft.UI.Reactor.Analyzers</c>, assembly <c>Reactor.Analyzers</c>),
    /// which <c>PoolResetSetAnalyzer</c> uses to decide whether <c>REACTOR_POOL_001</c> (the
    /// receiver is recycled, so the write is unwound on pool return) or <c>REACTOR_MOD_002</c>
    /// (the receiver is not recycled, so the write is merely dropped by the next render)
    /// describes a <c>.Set</c> write. The analyzer targets <c>netstandard2.0</c> and cannot
    /// reference this assembly, hence the copy.
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
    /// Try to rent an element of the given type from the pool.
    /// Returns null if the pool is empty or the type is not poolable.
    /// </summary>
    // <snippet:pool-rent>
    public FrameworkElement? TryRent(Type type)
    {
        if (!Enabled) return null;
        if (!PoolableTypes.Contains(type)) return null;
        if (!_pools.TryGetValue(type, out var stack) || stack.Count == 0) return null;
        var item = stack.Pop();
        return item;
    }
    // </snippet:pool-rent>

    /// <summary>
    /// Return an element to the pool after unmount. Cleans it first.
    /// Silently drops if the type is not poolable or the pool is full.
    /// </summary>
    // <snippet:pool-return>
    public void Return(FrameworkElement element)
    {
        if (!Enabled) return;
        var type = element.GetType();
        if (!PoolableTypes.Contains(type)) return;

        // Don't pool elements that had GetElementVisual() called — they permanently
        // lose XAML implicit transition API access (OpacityTransition, etc.).
        if (IsCompositorTainted(element)) return;

        if (!_pools.TryGetValue(type, out var stack))
        {
            stack = new Stack<FrameworkElement>();
            _pools[type] = stack;
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

        CleanElement(element);
        stack.Push(element);
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
        _scratchPanel = null;
    }

    /// <summary>
    /// Reset an element to a clean state suitable for reuse.
    /// </summary>
    internal static void CleanElement(FrameworkElement fe)
    {
        // Common properties
        Reconciler.ClearElementTag(fe);
        // SECURITY (TASK-060): clear the Current* user-handler delegates on
        // pool return so a pooled control can't fire the previous component's
        // captured rerender closure into the next mount. The underlying
        // trampoline subscription stays attached — that's intentional, see
        // the comment block in Reconciler.cs above ModifierEventHandlerState.
        Reconciler.ClearCurrentEventHandlers(fe);
        fe.Tag = null;
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
        // Gated on Style being non-null so non-themed controls (the common
        // case) skip the redundant COM call.
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
        fe.ClearValue(UIElement.IsHitTestVisibleProperty);
        if (fe is Control tabStopControl)
            tabStopControl.ClearValue(Control.IsTabStopProperty);

        // Issue #985: six common modifiers are written by ApplyModifiers onto receivers
        // this method never reset, so a pooled control handed its previous renter's local
        // Padding / CornerRadius / BorderThickness / BorderBrush / Background / IsEnabled
        // to the next one — and a local value outranks every Style setter (same precedence
        // trap as #952, but caused by a *missing* reset rather than a wrong-shaped one).
        //
        // These live here, in the FE-common region, and not in the type-specific arms
        // below: the pool/analyzer consistency invariants (PoolResetSetConsistencyTests)
        // stop scanning at the type dispatch, so a clear placed after it is invisible to
        // them and ModifierTable would keep claiming the property is not pool-reset.
        // Placement is the fix, not an implementation detail.
        //
        // The chain mirrors ApplyModifiers' receiver types for the receivers that are actually
        // pooled (Reconciler.cs): Padding → Control | Border | Grid | StackPanel | TextBlock,
        // CornerRadius → Control | Border | Grid | StackPanel, BorderBrush/BorderThickness →
        // Control | Border, Background → Panel | Control | Border, IsEnabled → Control.
        // The Padding and CornerRadius gates also admit RelativePanel, which gets no clear
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
        // The consistency invariant will not remind you: it checks reset ⇒ marked, never
        // marked ⇒ reset on every gated receiver (see #1017).
        if (fe is Control resetControl)
        {
            resetControl.ClearValue(Control.PaddingProperty);
            resetControl.ClearValue(Control.CornerRadiusProperty);
            resetControl.ClearValue(Control.BorderThicknessProperty);
            resetControl.ClearValue(Control.BorderBrushProperty);
            resetControl.ClearValue(Control.BackgroundProperty);
            resetControl.ClearValue(Control.IsEnabledProperty);
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
            }
            else if (resetPanel is WinUI.StackPanel resetStack)
            {
                resetStack.ClearValue(WinUI.StackPanel.PaddingProperty);
                resetStack.ClearValue(WinUI.StackPanel.CornerRadiusProperty);
            }
        }
        else if (fe is TextBlock resetText)
        {
            // TextBlock's padding reset predates #985 (it arrived with #950) but lived in
            // the case arm below, where no scanner could see it — so ModifierTable's claim
            // that Padding is pool-reset on TextBlock was the one receiver in the gate that
            // nothing verified. Deleting the line used to break no test. Now it does.
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

    public void Dispose()
    {
        foreach (var stack in _pools.Values)
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
    }
}
