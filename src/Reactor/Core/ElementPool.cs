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
public sealed partial class ElementPool : IDisposable
{
    /// <summary>
    /// Records that the element's composition visual was handed out. The marker lives on the
    /// native control, so every managed wrapper sees it, and it is never cleared.
    /// </summary>
    internal static void MarkVisualTaken(UIElement element)
        => Reconciler.GetOrCreateCompositionState(element).VisualTaken = true;

    internal static bool IsVisualTaken(UIElement element)
        => Reconciler.GetCompositionState(element) is { VisualTaken: true };

    /// <summary>
    /// Keeps the control out of the pool for good. A pending navigation completion may still reach it.
    /// With <paramref name="releaseOnReturn"/>, a return still drops the element and handlers it
    /// hosted, without touching its pixels.
    /// </summary>
    internal static void ExcludeFromPool(UIElement element, bool releaseOnReturn = false)
    {
        var state = Reconciler.GetOrCreateReactorState(element);
        state.PoolExcluded = true;
        state.ReleaseWhenExcluded |= releaseOnReturn;
    }

    private const int MaxPerType = 32;

    /// <summary>
    /// When false, TryRent always returns null and Return is a no-op.
    /// Useful for scenarios like the live previewer where recycled controls
    /// with stale property state can cause visual glitches.
    /// </summary>
    public bool Enabled { get; set; } = true;

    // PoolableTypes, the exact types the pool recycles, is generated into ElementPool.Generated.cs
    // from ModifierSchema.PoolableTypeNames, which the analyzer reads too. Membership is exact-type:
    // CheckBox and RelativePanel pass the Control and Panel gates ApplyModifiers dispatches on, but
    // they aren't pooled. A type added there needs a CleanElement arm for its own content and state.

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
        // A renter returned before it loaded must not stay on the window's layout list.
        if (state?.Composition is { } composition) StopOpacitySync(element, composition);
        if (state?.PoolExcluded == true)
        {
            if (state.ReleaseWhenExcluded)
            {
                Reconciler.ClearElementTag(element);
                Reconciler.ClearCurrentEventHandlers(element);
            }
            return;
        }
        var lanes = state?.Composition?.VisualTaken == true ? _visualPools : _pools;

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
        // the comment block in ModifierEvents.cs above ModifierEventHandlerState.
        Reconciler.ClearCurrentEventHandlers(fe);
        fe.Tag = null;
        // Resources keys the previous renter's .Resources(...) overrides wrote. Mount applies
        // overrides only for an element that declares some, so without this a renter that
        // declares none would inherit them. Keys set outside Reactor are left alone.
        ThemeBindingApplier.RemoveManagedResourceKeys(fe);
        // Spec 067 layer 5: every DP a common modifier writes on a pooled type is reset by the
        // Reset*Modifiers helpers generated into ElementPool.Generated.cs from each receiver's pool
        // declaration in the modifier schema. ApplyModifiers releases a modifier only on a set → unset
        // transition, and Mount runs it with no old modifiers, so without them a renter that declares
        // none would keep the previous renter's local value, which outranks every Style setter.
        // The resets are ClearValue, not writes of the DP default, so the next renter sees its default
        // style again (#952). IsTabStop is reset on the element, not under a Control narrowing, since
        // ApplyModifiers writes it ungated (`fe.IsTabStop = …`, #162, #985). Each helper is called where
        // its resets used to be hand-written, so their order against the steps here is unchanged.
        ResetLayoutModifiers(fe);
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
        ResetThemeAndFlyoutModifiers(fe);
        // Issue #1662: only user code writes Clip, and no renter's mount writes it back, so a
        // .Set(c => c.Clip = …) would otherwise clip the next renter too, in either lane.
        fe.ClearValue(UIElement.ClipProperty);

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
        ThemeBindingApplier.ClearThemeBindings(fe);
        if (fe.Style is not null)
            fe.ClearValue(FrameworkElement.StyleProperty);
        ResetStateModifiers(fe);

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
                if (themedBorder.BackgroundTransition is not null) themedBorder.BackgroundTransition = null;
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
                // Border's modifier-backed DPs are reset by ResetStateModifiers above.
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
                // FontSize gets the WinUI default written back rather than cleared: it inherits, so a
                // clear would let a recycled TextBlock pick up an ancestor's size and re-flow its text.
                ResetTextBlockSizeAndWeight(fe);
                tb.ClearValue(TextBlock.FontStyleProperty);
                tb.ClearValue(TextBlock.TextWrappingProperty);
                tb.ClearValue(TextBlock.TextAlignmentProperty);
                tb.ClearValue(TextBlock.TextTrimmingProperty);
                tb.ClearValue(TextBlock.IsTextSelectionEnabledProperty);
                ResetTextBlockFontFamily(fe);
                break;
            case WinUI.RichTextBlock rtb:
                Reconciler.CancelInlineUiExtentPin(rtb);
                rtb.Blocks.Clear();
                ResetRichTextBlockForeground(fe);
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
    // its subchannels ("Offset.Y", ElementPoolLanes_LayoutOffset). StopAnimation leaves the last
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
        foreach (var channel in TakenVisualChannels)
            visual.StopAnimation(channel);
        if (hasTranslation) visual.StopAnimation("Translation");
        // A .TransformOrigin's expression stopped with CenterPoint above; its property-set entries
        // can't be removed, so they go to the inactive state (spec 066 §8.2, §7.3 step 4).
        Animation.TransformOriginBinding.ResetForPool(visual);
        // ClearElementTag already released the Reactor claims (ImplicitAnimationSlots.ReleaseAll);
        // this drops keys no claim owns, for example ones user code installed. Before
        // any write below, so none of them starts an implicit animation.
        visual.ImplicitAnimations = null;

        // Layout owns Offset and writes it only when the position changes, so a stopped animation's
        // last value, or a direct write through the visual, would stay. Put the layout offset back,
        // unconditionally: a stopped expression's last value lands after this, so the getter can't
        // tell yet, and this write follows the stop in the same batch. The control has usually left
        // the tree by now, where ActualOffset reads zero, so the renter gets its own layout offset
        // once it is laid out (SyncOpacityWhenLoaded). Size is left alone, because a fresh handoff
        // visual doesn't reliably carry ActualSize.
        visual.Offset = fe.ActualOffset;
        Reconciler.GetOrCreateCompositionState(fe).PoolResetOffset = fe.ActualOffset;

        // XAML's own opacity comes back when the renter loads (SyncOpacityWhenLoaded).
        visual.Opacity = 1;
        visual.IsVisible = true;
        visual.Scale = Vector3.One;
        visual.RotationAngle = 0;
        visual.RotationAxis = Vector3.UnitZ;
        visual.CenterPoint = Vector3.Zero;
        visual.AnchorPoint = Vector2.Zero;
        visual.Orientation = Quaternion.Identity;
        visual.TransformMatrix = Matrix4x4.Identity;
        // Layout never writes the relative adjustments. Unconditional, like Offset: a stopped
        // expression's last value lands after the stop.
        visual.RelativeOffsetAdjustment = Vector3.Zero;
        visual.RelativeSizeAdjustment = Vector2.Zero;
        visual.Clip = null;
        if (hasTranslation) visual.Properties.InsertVector3("Translation", Vector3.Zero);
        ElementCompositionPreview.SetIsTranslationEnabled(fe, false);
        // The Translation facade stays writable after the handoff, and a user .Set may have
        // written it next to a Reactor feature.
        if (fe.Translation != Vector3.Zero) fe.Translation = Vector3.Zero;
        Reconciler.ForgetCompositionState(fe);
    }

    /// <summary>
    /// Finishes a visual-lane renter's reset once it is in the tree and laid out.
    /// <list type="bullet">
    /// <item>The reset runs after the control left the tree, where <c>ActualOffset</c> reads zero,
    /// and layout writes the visual's <c>Offset</c> only when the position changes, so a renter at
    /// the previous renter's non-zero position would draw at its parent's origin. The visual gets
    /// the layout offset back, unless a feature animates <c>Offset</c> by then.</item>
    /// <item>XAML pushes <c>UIElement.Opacity</c> to the visual only when it differs from its last
    /// push, so after the reset's identity write a renter whose opacity (a style's, a modifier's or
    /// the default) equals the previous renter's would never see it. The visual gets XAML's value,
    /// unless a Reactor feature owns the visual's opacity by then.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The first layout pass that finds the renter in the live tree runs it, before the frame that
    /// shows the renter. <c>Loaded</c> alone isn't enough: WinUI can raise it late, or not at all for
    /// a control re-added while its <c>Unloaded</c> is still pending. <c>Loaded</c> stays as the
    /// trigger for a renter in a popup, whose tree doesn't reach the window's content.
    /// </remarks>
    internal static void SyncOpacityWhenLoaded(FrameworkElement fe)
    {
        var state = Reconciler.GetOrCreateCompositionState(fe);
        if (state.OpacitySyncLayoutHandler is not null) return;
        EventHandler<object> onLayout = (_, _) =>
        {
            if (IsInLiveTree(fe)) SyncRenterOpacity(fe);
        };
        state.OpacitySyncLayoutHandler = onLayout;
        Interlocked.Increment(ref s_pendingOpacitySyncs);
        fe.LayoutUpdated += onLayout;
        fe.Loaded += OnRenterLoaded;
    }

    // Renters still waiting, so an unmount that has none to cancel pays no attached-state read.
    private static int s_pendingOpacitySyncs;

    /// <summary>
    /// Cancels a renter's pending sync when it unmounts, whichever path removes it (a navigation
    /// cancelled before attach, a disabled pool), so it doesn't stay on the window's layout list.
    /// </summary>
    internal static void CancelOpacitySync(FrameworkElement fe)
    {
        if (Volatile.Read(ref s_pendingOpacitySyncs) == 0) return;
        if (Reconciler.GetCompositionState(fe) is { } state)
            StopOpacitySync(fe, state);
    }

    /// <summary>Whether the renter still waits for <see cref="SyncOpacityWhenLoaded"/>.</summary>
    internal static bool IsOpacitySyncPending(FrameworkElement fe)
        => Reconciler.GetCompositionState(fe) is { OpacitySyncLayoutHandler: not null };

    private static void StopOpacitySync(FrameworkElement fe, Reconciler.CompositionState state)
    {
        if (state.OpacitySyncLayoutHandler is not { } onLayout) return;
        state.OpacitySyncLayoutHandler = null;
        Interlocked.Decrement(ref s_pendingOpacitySyncs);
        fe.LayoutUpdated -= onLayout;
        fe.Loaded -= OnRenterLoaded;
    }

    internal static bool IsInLiveTree(FrameworkElement fe)
    {
        if (fe.XamlRoot?.Content is not { } root) return false;
        for (DependencyObject? node = fe; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, root)) return true;
        return false;
    }

    private static void OnRenterLoaded(object sender, RoutedEventArgs e) => SyncRenterOpacity((FrameworkElement)sender);

    private static void SyncRenterOpacity(FrameworkElement fe)
    {
        if (fe.GetValue(Reconciler.ReactorAttached.StateProperty) is not Reconciler.ReactorState { Composition: { } state } reactorState) return;
        StopOpacitySync(fe, state);
        if (reactorState.PoolExcluded) return;
        var visual = ElementCompositionPreview.GetElementVisual(fe);
        // The reset ran after the control left the tree, where ActualOffset reads zero, and layout
        // writes Offset only when the position changes. A renter at the previous renter's non-zero
        // position would otherwise draw at its parent's origin. Placing it isn't a move, so a
        // renter's implicit Offset key (.LayoutAnimation()) must not animate it.
        // Only while the visual still holds what the reset wrote: anything else is a write since
        // then (the renter's own, or XAML's for a new position) and stands.
        var layoutOffset = fe.ActualOffset;
        if (state.PoolResetOffset is { } resetOffset && visual.Offset == resetOffset && resetOffset != layoutOffset
            && visual.TryGetAnimationController("Offset") is null)
        {
            WriteWithoutImplicitKey(visual, "Offset", () => visual.Offset = layoutOffset);
            OffsetSyncWritesForTests++;
        }
        state.PoolResetOffset = null;

        if ((state.ModifierProperties & Animation.AnimateProperty.Opacity) != 0) return;
        // Something wrote the visual since the reset (navigation hiding a page, user code), or
        // animates it now, and owns it.
        if (visual.Opacity != 1f || visual.TryGetAnimationController("Opacity") is not null) return;
        float opacity = (float)fe.Opacity;
        if (opacity == 1f) return;
        // XAML would have shown this value without a transition, so a renter's implicit Opacity key
        // (.Animate()) must not fade to it.
        WriteWithoutImplicitKey(visual, "Opacity", () => visual.Opacity = opacity);
        OpacitySyncWritesForTests++;
    }

    /// <summary>
    /// Runs a visual write with the implicit animation for <paramref name="key"/> held back. The same
    /// animation goes back afterwards, so its claim (spec 066 §6) holds.
    /// </summary>
    private static void WriteWithoutImplicitKey(Visual visual, string key, Action write)
    {
        ICompositionAnimationBase? held = null;
        if (visual.ImplicitAnimations is { } implicits && implicits.TryGetValue(key, out var animation))
        {
            held = animation;
            implicits.Remove(key);
        }
        try { write(); }
        finally { if (held is not null) visual.ImplicitAnimations![key] = held; }
    }

    // Test-only seam (InternalsVisibleTo Reactor.AppTests.Host): counts the opacity writes the
    // sync made. XAML may push a styled opacity itself when a control re-enters the tree, so the
    // visual's final value can't tell whether the sync wrote.
    internal static int OpacitySyncWritesForTests;

    // Test-only seam, same visibility: counts the layout-offset writes the sync made.
    internal static int OffsetSyncWritesForTests;

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
