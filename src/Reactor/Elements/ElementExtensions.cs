using System;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Text;
using Windows.UI.Text;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;

namespace Microsoft.UI.Reactor;

/// <summary>
/// Fluent modifier extension methods for elements.
/// Modifiers are stored inline on Element.Modifiers, preserving the concrete type
/// through the entire fluent chain. This means .Set() works after any modifier:
///
///   Text("Hello")
///       .Bold()
///       .Margin(16)
///       .HAlign(HorizontalAlignment.Center)
///       .Set(tb => tb.TextWrapping = TextWrapping.Wrap)  // still TextBlockElement!
///
/// The Set() extension gives strongly-typed native property access:
///   Button("Click", onClick)
///       .Set(b => b.FlowDirection = FlowDirection.RightToLeft)
/// </summary>
public static partial class ElementExtensions
{
    // ════════════════════════════════════════════════════════════════
    //  Layout modifiers (stored inline on Element.Modifiers)
    // ════════════════════════════════════════════════════════════════

    public static T Margin<T>(this T el, double uniform) where T : Element =>
        Modify(el, new ElementModifiers { Margin = new Thickness(uniform) });

    // Two-argument spacing follows the same order everywhere in Reactor:
    // horizontal FIRST, then vertical. This keeps `.Margin(...)`,
    // `.Padding(...)`, and `.FlexPadding(...)` aligned and matches the mental
    // model of Thickness(left/right, top/bottom).
    public static T Margin<T>(this T el, double horizontal, double vertical) where T : Element =>
        Modify(el, new ElementModifiers { Margin = new Thickness(horizontal, vertical, horizontal, vertical) });

    // Default values on the per-side overload let callers name only the sides
    // they want (`.Margin(top: 10)`, `.Margin(left: 8, right: 8)`). Existing
    // positional callers are unaffected — overload resolution still binds
    // `.Margin(10)` to the uniform overload and `.Margin(10, 20)` to the
    // horizontal/vertical overload because those signatures are more specific
    // (fewer parameters → preferred match). Mirrors WPF Thickness defaults:
    // unspecified sides are zero.
    public static T Margin<T>(this T el, double left = 0.0, double top = 0.0, double right = 0.0, double bottom = 0.0) where T : Element =>
        Modify(el, new ElementModifiers { Margin = new Thickness(left, top, right, bottom) });

    public static T Padding<T>(this T el, double uniform) where T : Element =>
        Modify(el, new ElementModifiers { Padding = new Thickness(uniform) });

    // Same ordering as Margin above — horizontal first, then vertical.
    public static T Padding<T>(this T el, double horizontal, double vertical) where T : Element =>
        Modify(el, new ElementModifiers { Padding = new Thickness(horizontal, vertical, horizontal, vertical) });

    // Same defaulting story as Margin above — `.Padding(top: 8)` etc. are
    // valid; existing 1-arg / 2-arg / 4-arg positional call shapes still bind
    // to the more-specific overloads.
    public static T Padding<T>(this T el, double left = 0.0, double top = 0.0, double right = 0.0, double bottom = 0.0) where T : Element =>
        Modify(el, new ElementModifiers { Padding = new Thickness(left, top, right, bottom) });

    // ── Logical (BiDi-aware) layout modifiers ───────────────────────
    // InlineStart = left in LTR, right in RTL. InlineEnd = right in LTR, left in RTL.
    // Resolved at mount/update time based on FlowDirection.

    public static T MarginInlineStart<T>(this T el, double value) where T : Element =>
        Modify(el, new ElementModifiers { MarginInlineStart = value });

    public static T MarginInlineEnd<T>(this T el, double value) where T : Element =>
        Modify(el, new ElementModifiers { MarginInlineEnd = value });

    public static T PaddingInlineStart<T>(this T el, double value) where T : Element =>
        Modify(el, new ElementModifiers { PaddingInlineStart = value });

    public static T PaddingInlineEnd<T>(this T el, double value) where T : Element =>
        Modify(el, new ElementModifiers { PaddingInlineEnd = value });

    public static T BorderInlineStart<T>(this T el, Thickness thickness) where T : Element =>
        Modify(el, new ElementModifiers { BorderInlineStart = thickness });

    public static T Width<T>(this T el, double width) where T : Element =>
        Modify(el, new ElementModifiers { Width = width });

    public static T Height<T>(this T el, double height) where T : Element =>
        Modify(el, new ElementModifiers { Height = height });

    public static T Size<T>(this T el, double width, double height) where T : Element =>
        Modify(el, new ElementModifiers { Width = width, Height = height });

    public static T MinWidth<T>(this T el, double w) where T : Element =>
        Modify(el, new ElementModifiers { MinWidth = w });

    public static T MinHeight<T>(this T el, double h) where T : Element =>
        Modify(el, new ElementModifiers { MinHeight = h });

    public static T MaxWidth<T>(this T el, double w) where T : Element =>
        Modify(el, new ElementModifiers { MaxWidth = w });

    public static T MaxHeight<T>(this T el, double h) where T : Element =>
        Modify(el, new ElementModifiers { MaxHeight = h });

    // ── Alignment ───────────────────────────────────────────────────

    public static T HAlign<T>(this T el, HorizontalAlignment alignment) where T : Element =>
        Modify(el, new ElementModifiers { HorizontalAlignment = alignment });

    public static T VAlign<T>(this T el, VerticalAlignment alignment) where T : Element =>
        Modify(el, new ElementModifiers { VerticalAlignment = alignment });

    // Long-form aliases matching the WinUI/WPF FrameworkElement property names.
    // The agent's first-instinct write is `.HorizontalAlignment(...)`; making
    // that compile saves a fix-loop without forcing a rename of the short forms.
    // Parameter types are fully qualified because the method names shadow the
    // enum names in this scope.
    public static T HorizontalAlignment<T>(this T el, Microsoft.UI.Xaml.HorizontalAlignment alignment) where T : Element =>
        Modify(el, new ElementModifiers { HorizontalAlignment = alignment });

    public static T VerticalAlignment<T>(this T el, Microsoft.UI.Xaml.VerticalAlignment alignment) where T : Element =>
        Modify(el, new ElementModifiers { VerticalAlignment = alignment });

    public static T Center<T>(this T el) where T : Element =>
        Modify(el, new ElementModifiers
        {
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
        });

    // ── Theme override ───────────────────────────────────────────────

    /// <summary>
    /// Sets <see cref="FrameworkElement.RequestedTheme"/> on this element's
    /// control, forcing the subtree to render in a specific theme variant.
    /// <para>
    /// <b>Dark sidebar:</b> <c>VStack(children).RequestedTheme(ElementTheme.Dark)</c>
    /// </para>
    /// <para>
    /// <b>Restore system default:</b> <c>panel.RequestedTheme(ElementTheme.Default)</c>
    /// </para>
    /// </summary>
    public static T RequestedTheme<T>(this T el, ElementTheme theme) where T : Element =>
        Modify(el, new ElementModifiers { RequestedTheme = theme });

    // ── Visibility ──────────────────────────────────────────────────

    public static T Visible<T>(this T el, bool isVisible) where T : Element =>
        Modify(el, new ElementModifiers { IsVisible = isVisible });

    public static T Opacity<T>(this T el, double opacity) where T : Element =>
        Modify(el, new ElementModifiers { Opacity = opacity });

    public static T Scale<T>(this T el, global::System.Numerics.Vector3 scale) where T : Element =>
        Modify(el, new ElementModifiers { Scale = scale });

    public static T Scale<T>(this T el, float uniform) where T : Element =>
        Modify(el, new ElementModifiers { Scale = new global::System.Numerics.Vector3(uniform, uniform, 1f) });

    public static T Rotation<T>(this T el, float degrees) where T : Element =>
        Modify(el, new ElementModifiers { Rotation = degrees });

    public static T CenterPoint<T>(this T el, global::System.Numerics.Vector3 center) where T : Element =>
        Modify(el, new ElementModifiers { CenterPoint = center });

    // ── Typography (any Control or TextBlock) ─────────────────────
    // These set font properties via ElementModifiers, so they work on ANY element
    // (buttons, borders wrapping text, etc.) — not just TextBlockElement.

    /// <summary>
    /// Sets the font family on any FrameworkElement that supports it (Control, TextBlock).
    /// For TextBlockElement-specific chaining that preserves the TextBlockElement return type,
    /// use the TextBlockElement.FontFamily() overload instead.
    /// </summary>
    public static T FontFamily<T>(this T el, string family) where T : Element =>
        Modify(el, new ElementModifiers { FontFamily = WinRTCache.GetFontFamily(family) });

    public static T FontFamily<T>(this T el, Microsoft.UI.Xaml.Media.FontFamily family) where T : Element =>
        Modify(el, new ElementModifiers { FontFamily = family });

    /// <summary>
    /// Sets the font size on any FrameworkElement that supports it (Control, TextBlock).
    /// For TextBlockElement-specific chaining, use the TextBlockElement.FontSize() overload.
    /// </summary>
    public static T FontSize<T>(this T el, double size) where T : Element =>
        Modify(el, new ElementModifiers { FontSize = size });

    /// <summary>
    /// Sets the font weight on any FrameworkElement that supports it (Control, TextBlock).
    /// </summary>
    public static T FontWeight<T>(this T el, global::Windows.UI.Text.FontWeight weight) where T : Element =>
        Modify(el, new ElementModifiers { FontWeight = weight });

    // ── Declarative event handlers ──────────────────────────────────
    // Unlike OnMount(), these re-attach on every update, so closures always
    // capture fresh state. The reconciler detaches the previous handler before
    // attaching the new one.

    public static T OnSizeChanged<T>(this T el, Action<object, SizeChangedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnSizeChanged = handler });

    public static T OnPointerPressed<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnPointerPressed = handler });

    public static T OnPointerMoved<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnPointerMoved = handler });

    public static T OnPointerReleased<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnPointerReleased = handler });

    public static T OnTapped<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnTapped = handler });

    public static T OnKeyDown<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnKeyDown = handler });

    public static T OnPointerEntered<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnPointerEntered = handler });

    public static T OnPointerExited<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnPointerExited = handler });

    public static T OnPointerCanceled<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnPointerCanceled = handler });

    public static T OnPointerCaptureLost<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnPointerCaptureLost = handler });

    public static T OnPointerWheelChanged<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnPointerWheelChanged = handler });

    public static T OnDoubleTapped<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnDoubleTapped = handler });

    public static T OnRightTapped<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnRightTapped = handler });

    public static T OnHolding<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.HoldingRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnHolding = handler });

    public static T OnKeyUp<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnKeyUp = handler });

    public static T OnPreviewKeyDown<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnPreviewKeyDown = handler });

    public static T OnPreviewKeyUp<T>(this T el, Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnPreviewKeyUp = handler });

    public static T OnCharacterReceived<T>(this T el, Action<UIElement, Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnCharacterReceived = handler });

    public static T OnGotFocus<T>(this T el, Action<object, RoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnGotFocus = handler });

    public static T OnLostFocus<T>(this T el, Action<object, RoutedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnLostFocus = handler });

    // ── Gesture recognizers (spec 027 Tier 3) ───────────────────────

    /// <summary>
    /// Attaches a pan (single-finger translation) gesture recognizer. The reconciler
    /// wires <see cref="UIElement.ManipulationDelta"/> and computes
    /// <see cref="UIElement.ManipulationMode"/> based on the chosen <paramref name="axis"/>
    /// and <paramref name="withInertia"/> flag. <paramref name="minimumDistance"/> gates
    /// callbacks until the cumulative translation exceeds that distance — on first
    /// crossing, <paramref name="onBegan"/> fires once with <see cref="Microsoft.UI.Reactor.Input.GesturePhase.Began"/>,
    /// then a <see cref="Microsoft.UI.Reactor.Input.GesturePhase.Changed"/> follows.
    /// </summary>
    public static T OnPan<T>(this T el,
        Action<Microsoft.UI.Reactor.Input.PanGesture> onChanged,
        Action<Microsoft.UI.Reactor.Input.PanGesture>? onEnded = null,
        Action<Microsoft.UI.Reactor.Input.PanGesture>? onBegan = null,
        Action<Microsoft.UI.Reactor.Input.PanGesture>? onCancelled = null,
        double minimumDistance = 0.0,
        Microsoft.UI.Reactor.Input.PanAxis axis = Microsoft.UI.Reactor.Input.PanAxis.Both,
        bool withInertia = false) where T : Element =>
        Modify(el, new ElementModifiers
        {
            Pan = new Microsoft.UI.Reactor.Input.PanGestureConfig(onChanged)
            {
                OnBegan = onBegan,
                OnEnded = onEnded,
                OnCancelled = onCancelled,
                MinimumDistance = minimumDistance,
                Axis = axis,
                WithInertia = withInertia,
            },
        });

    /// <summary>Attaches a pinch (two-finger scale) gesture recognizer.</summary>
    public static T OnPinch<T>(this T el,
        Action<Microsoft.UI.Reactor.Input.PinchGesture> onChanged,
        Action<Microsoft.UI.Reactor.Input.PinchGesture>? onEnded = null,
        Action<Microsoft.UI.Reactor.Input.PinchGesture>? onBegan = null,
        Action<Microsoft.UI.Reactor.Input.PinchGesture>? onCancelled = null,
        bool withInertia = false) where T : Element =>
        Modify(el, new ElementModifiers
        {
            Pinch = new Microsoft.UI.Reactor.Input.PinchGestureConfig(onChanged)
            {
                OnBegan = onBegan,
                OnEnded = onEnded,
                OnCancelled = onCancelled,
                WithInertia = withInertia,
            },
        });

    /// <summary>Attaches a rotate (two-finger twist) gesture recognizer.</summary>
    public static T OnRotate<T>(this T el,
        Action<Microsoft.UI.Reactor.Input.RotateGesture> onChanged,
        Action<Microsoft.UI.Reactor.Input.RotateGesture>? onEnded = null,
        Action<Microsoft.UI.Reactor.Input.RotateGesture>? onBegan = null,
        Action<Microsoft.UI.Reactor.Input.RotateGesture>? onCancelled = null,
        bool withInertia = false) where T : Element =>
        Modify(el, new ElementModifiers
        {
            Rotate = new Microsoft.UI.Reactor.Input.RotateGestureConfig(onChanged)
            {
                OnBegan = onBegan,
                OnEnded = onEnded,
                OnCancelled = onCancelled,
                WithInertia = withInertia,
            },
        });

    /// <summary>
    /// Attaches a long-press gesture recognizer (spec 027 Tier 3 Part 2). Touch and pen
    /// route through <see cref="UIElement.Holding"/> (<c>IsHoldingEnabled = true</c> is auto-set).
    /// Mouse input is ignored by default — WinUI's <c>Holding</c> event does not raise for
    /// mouse pointers. Pass <paramref name="enableMouseEmulation"/> <c>true</c> to arm a
    /// dispatcher timer on <see cref="UIElement.PointerPressed"/> that fires after
    /// <paramref name="minimumDuration"/> and cancels on motion &gt; <paramref name="cancelDistance"/>,
    /// pointer release, or capture loss.
    /// </summary>
    /// <example>card.OnLongPress(g => ShowContextMenu(g.Position))</example>
    public static T OnLongPress<T>(this T el,
        Action<Microsoft.UI.Reactor.Input.LongPressGesture> onTriggered,
        TimeSpan? minimumDuration = null,
        double cancelDistance = 10.0,
        bool enableMouseEmulation = false) where T : Element =>
        Modify(el, new ElementModifiers
        {
            LongPress = new Microsoft.UI.Reactor.Input.LongPressGestureConfig(onTriggered)
            {
                MinimumDuration = minimumDuration ?? TimeSpan.FromMilliseconds(500),
                CancelDistance = cancelDistance,
                EnableMouseEmulation = enableMouseEmulation,
            },
        });

    /// <summary>
    /// Zero-argument convenience overload for long-press. Use when you don't need the
    /// gesture snapshot (Position, Duration, Phase).
    /// </summary>
    public static T OnLongPress<T>(this T el,
        Action onTriggered,
        TimeSpan? minimumDuration = null,
        double cancelDistance = 10.0,
        bool enableMouseEmulation = false) where T : Element =>
        el.OnLongPress(_ => onTriggered(), minimumDuration, cancelDistance, enableMouseEmulation);

    /// <summary>
    /// Zero-argument convenience overload for double-tap. Equivalent to
    /// <c>.OnDoubleTapped((_, _) =&gt; handler())</c>.
    /// </summary>
    public static T OnDoubleTap<T>(this T el, Action handler) where T : Element =>
        el.OnDoubleTapped((_, _) => handler());

    /// <summary>
    /// Position-aware convenience overload for double-tap. Hands back the tap position
    /// in element-local space.
    /// </summary>
    public static T OnDoubleTap<T>(this T el, Action<global::Windows.Foundation.Point> handler) where T : Element =>
        el.OnDoubleTapped((s, e) => handler(e.GetPosition(s as UIElement)));

    // ── Drag-and-drop (spec 027 Tier 6 / Phase 6a) ──────────────────

    /// <summary>
    /// Typed drag source. Auto-sets <see cref="UIElement.CanDrag"/> so the element reports
    /// as draggable. <paramref name="getPayload"/> is called each time a drag starts;
    /// the returned value is wrapped in a typed-payload <see cref="Microsoft.UI.Reactor.Input.DragData"/>
    /// keyed by <typeparamref name="TPayload"/>. Use <paramref name="allowedOperations"/> to
    /// declare which final operations (Copy/Move/Link) the source will accept.
    /// <paramref name="onEnd"/> fires after <c>DropCompleted</c> with the final negotiated
    /// operation (or <see cref="Microsoft.UI.Reactor.Input.DragOperations.None"/> on cancel).
    /// </summary>
    public static T OnDragStart<T, TPayload>(this T el,
        Func<TPayload> getPayload,
        Microsoft.UI.Reactor.Input.DragOperations? allowedOperations = null,
        Action<Microsoft.UI.Reactor.Input.DragEndContext>? onEnd = null) where T : Element =>
        Modify(el, new ElementModifiers
        {
            DragSource = new Microsoft.UI.Reactor.Input.DragSourceConfig(
                () => Microsoft.UI.Reactor.Input.DragData.Typed(getPayload()))
            {
                AllowedOperations = allowedOperations,
                OnEnd = onEnd,
            },
        });

    /// <summary>
    /// Raw drag source — the caller builds the <see cref="Microsoft.UI.Reactor.Input.DragData"/>
    /// directly. Useful when advertising multiple formats at once (Phase 6b) or attaching
    /// additional metadata.
    /// </summary>
    public static T OnDragStart<T>(this T el,
        Func<Microsoft.UI.Reactor.Input.DragData> getData,
        Microsoft.UI.Reactor.Input.DragOperations? allowedOperations = null,
        Action<Microsoft.UI.Reactor.Input.DragEndContext>? onEnd = null) where T : Element =>
        Modify(el, new ElementModifiers
        {
            DragSource = new Microsoft.UI.Reactor.Input.DragSourceConfig(getData)
            {
                AllowedOperations = allowedOperations,
                OnEnd = onEnd,
            },
        });

    /// <summary>
    /// Gates an attached <c>.OnDragStart</c> — when <paramref name="canDrag"/> returns false,
    /// the drag is cancelled in <c>DragStarting</c> before any UI feedback appears. Merge with
    /// an existing <see cref="Microsoft.UI.Reactor.Input.DragSourceConfig"/> so previously-set
    /// allowed ops / onEnd are preserved.
    /// </summary>
    public static T DraggableWhen<T>(this T el, Func<bool> canDrag) where T : Element
    {
        var existing = el.Modifiers?.DragSource;
        var cfg = existing is not null
            ? existing with { CanDrag = canDrag }
            : new Microsoft.UI.Reactor.Input.DragSourceConfig(() => new Microsoft.UI.Reactor.Input.DragData()) { CanDrag = canDrag };
        return Modify(el, new ElementModifiers { DragSource = cfg });
    }

    /// <summary>
    /// Typed drop target. Auto-sets <see cref="UIElement.AllowDrop"/>. The handler is invoked
    /// when a drag with a matching typed payload is dropped on this element; the accepted
    /// operation is set to the intersection of <paramref name="acceptedOps"/> and the source's
    /// allowed operations (preferring Move &gt; Copy &gt; Link).
    /// </summary>
    public static T OnDrop<T, TPayload>(this T el,
        Action<TPayload> onDrop,
        Microsoft.UI.Reactor.Input.DragOperations acceptedOps = Microsoft.UI.Reactor.Input.DragOperations.All) where T : Element
    {
        var existing = el.Modifiers?.DropTarget ?? new Microsoft.UI.Reactor.Input.DropTargetConfig();
        var typedCallback = new Action<Microsoft.UI.Reactor.Input.DragTargetArgs>(args =>
        {
            if (args.Data.TryGetTypedPayload<TPayload>(out var payload))
            {
                onDrop(payload);
                // Auto-accept if caller didn't already set.
                if (args.AcceptedOperation == Microsoft.UI.Reactor.Input.DragOperations.None)
                {
                    args.AcceptedOperation = Microsoft.UI.Reactor.Input.DragOperationNegotiation.Negotiate(
                        args.AllowedOperations, acceptedOps);
                }
            }
        });
        var cfg = existing with
        {
            TypedDrop = typedCallback,
            AcceptedOperations = acceptedOps,
        };
        return Modify(el, new ElementModifiers { DropTarget = cfg });
    }

    /// <summary>Raw drop handler — receives the full <see cref="Microsoft.UI.Reactor.Input.DragTargetArgs"/>
    /// so multi-format targets can inspect available formats and accept operation manually.</summary>
    public static T OnDrop<T>(this T el,
        Action<Microsoft.UI.Reactor.Input.DragTargetArgs> onDrop,
        Microsoft.UI.Reactor.Input.DragOperations acceptedOps = Microsoft.UI.Reactor.Input.DragOperations.All) where T : Element
    {
        var existing = el.Modifiers?.DropTarget ?? new Microsoft.UI.Reactor.Input.DropTargetConfig();
        var cfg = existing with
        {
            OnDrop = onDrop,
            AcceptedOperations = acceptedOps,
        };
        return Modify(el, new ElementModifiers { DropTarget = cfg });
    }

    /// <summary>DragEnter callback — caller updates <see cref="Microsoft.UI.Reactor.Input.DragTargetArgs.UIOverride"/>
    /// to customize the drop indicator, or sets <see cref="Microsoft.UI.Reactor.Input.DragTargetArgs.AcceptedOperation"/>
    /// to override default negotiation.</summary>
    public static T OnDragEnter<T>(this T el, Action<Microsoft.UI.Reactor.Input.DragTargetArgs> handler) where T : Element
    {
        var existing = el.Modifiers?.DropTarget ?? new Microsoft.UI.Reactor.Input.DropTargetConfig();
        return Modify(el, new ElementModifiers { DropTarget = existing with { OnDragEnter = handler } });
    }

    /// <summary>DragOver callback — fires repeatedly as the pointer moves. Use for hover highlighting
    /// that depends on position within the target.</summary>
    public static T OnDragOver<T>(this T el, Action<Microsoft.UI.Reactor.Input.DragTargetArgs> handler) where T : Element
    {
        var existing = el.Modifiers?.DropTarget ?? new Microsoft.UI.Reactor.Input.DropTargetConfig();
        return Modify(el, new ElementModifiers { DropTarget = existing with { OnDragOver = handler } });
    }

    /// <summary>DragLeave callback — fires when the drag exits the target without dropping.</summary>
    public static T OnDragLeave<T>(this T el, Action<Microsoft.UI.Reactor.Input.DragTargetArgs> handler) where T : Element
    {
        var existing = el.Modifiers?.DropTarget ?? new Microsoft.UI.Reactor.Input.DropTargetConfig();
        return Modify(el, new ElementModifiers { DropTarget = existing with { OnDragLeave = handler } });
    }

    // ── Decoration ──────────────────────────────────────────────────

    public static T ToolTip<T>(this T el, string tip) where T : Element =>
        Modify(el, new ElementModifiers { ToolTip = tip });

    // ── Flyout / Context / Rich ToolTip attachments ─────────────
    public static T WithFlyout<T>(this T el, Element flyout) where T : Element =>
        Modify(el, new ElementModifiers { AttachedFlyout = flyout });

    public static T WithContextFlyout<T>(this T el, Element contextFlyout) where T : Element =>
        Modify(el, new ElementModifiers { ContextFlyout = contextFlyout });

    public static T WithToolTip<T>(this T el, Element tooltip) where T : Element =>
        Modify(el, new ElementModifiers { RichToolTip = tooltip });

    // ── Theme / Style ───────────────────────────────────────────────

    /// <summary>
    /// Apply a named WinUI Style to the element's control at mount/update time.
    /// Style is on FrameworkElement — works on any element.
    /// Usage: Text("Hello").ApplyStyle("BodyTextBlockStyle")
    /// </summary>
    public static T ApplyStyle<T>(this T el, string styleName) where T : Element =>
        el.OnMount(fe => fe.Style = (Style)Application.Current.Resources[styleName]);

    // ════════════════════════════════════════════════════════════════
    //  Sugar extensions (typed, return concrete element type)
    // ════════════════════════════════════════════════════════════════

    // ── Text sugar ──────────────────────────────────────────────────

    public static TextBlockElement Bold(this TextBlockElement el) =>
        el with { Weight = Microsoft.UI.Text.FontWeights.Bold };

    public static TextBlockElement SemiBold(this TextBlockElement el) =>
        el with { Weight = Microsoft.UI.Text.FontWeights.SemiBold };

    public static TextBlockElement FontSize(this TextBlockElement el, double size) =>
        el with { FontSize = size };

    public static TextBlockElement FontStyle(this TextBlockElement el, global::Windows.UI.Text.FontStyle style) =>
        el with { FontStyle = style };

    public static TextBlockElement TextWrapping(this TextBlockElement el, TextWrapping wrapping = Microsoft.UI.Xaml.TextWrapping.Wrap) =>
        el with { TextWrapping = wrapping };

    public static TextBlockElement TextAlignment(this TextBlockElement el, TextAlignment alignment) =>
        el with { TextAlignment = alignment };

    public static TextBlockElement TextTrimming(this TextBlockElement el, TextTrimming trimming) =>
        el with { TextTrimming = trimming };

    public static TextBlockElement Selectable(this TextBlockElement el, bool selectable = true) =>
        el with { IsTextSelectionEnabled = selectable };

    public static TextBlockElement FontFamily(this TextBlockElement el, string family) =>
        el with { FontFamily = WinRTCache.GetFontFamily(family) };

    public static TextBlockElement FontFamily(this TextBlockElement el, Microsoft.UI.Xaml.Media.FontFamily family) =>
        el with { FontFamily = family };

    /// <summary>Sets the line height in pixels (overrides the proportional WinUI default).</summary>
    public static TextBlockElement LineHeight(this TextBlockElement el, double height) =>
        el with { LineHeight = height };

    /// <summary>Maximum number of lines to render before truncating per <c>TextTrimming</c>. <c>0</c> = no limit.</summary>
    public static TextBlockElement MaxLines(this TextBlockElement el, int maxLines) =>
        el with { MaxLines = maxLines };

    /// <summary>Extra spacing between characters, in units of 1/1000em.</summary>
    public static TextBlockElement CharacterSpacing(this TextBlockElement el, int spacing) =>
        el with { CharacterSpacing = spacing };

    /// <summary>Underline / strikethrough decorations. Combine with bitwise OR.</summary>
    public static TextBlockElement TextDecorations(this TextBlockElement el, global::Windows.UI.Text.TextDecorations decorations) =>
        el with { TextDecorations = decorations };

    // ── RichTextBlock sugar ────────────────────────────────────────────

    /// <summary>Maximum number of lines before trimming. <c>0</c> = no limit.</summary>
    public static RichTextBlockElement MaxLines(this RichTextBlockElement el, int maxLines) =>
        el with { MaxLines = maxLines };

    /// <summary>Line height in pixels (overrides the WinUI proportional default).</summary>
    public static RichTextBlockElement LineHeight(this RichTextBlockElement el, double height) =>
        el with { LineHeight = height };

    /// <summary>Horizontal alignment of text within the block.</summary>
    public static RichTextBlockElement TextAlignment(this RichTextBlockElement el, TextAlignment alignment) =>
        el with { TextAlignment = alignment };

    /// <summary>How overflowing text is truncated.</summary>
    public static RichTextBlockElement TextTrimming(this RichTextBlockElement el, TextTrimming trimming) =>
        el with { TextTrimming = trimming };

    /// <summary>Extra spacing between characters, in units of 1/1000em.</summary>
    public static RichTextBlockElement CharacterSpacing(this RichTextBlockElement el, int spacing) =>
        el with { CharacterSpacing = spacing };

    // ── RichEditBox sugar ───────────────────────────────────────────────

    /// <summary>Enable or disable built-in spell-check.</summary>
    public static RichEditBoxElement IsSpellCheckEnabled(this RichEditBoxElement el, bool enabled = true) =>
        el with { IsSpellCheckEnabled = enabled };

    /// <summary>Maximum number of characters allowed. <c>0</c> = no limit.</summary>
    public static RichEditBoxElement MaxLength(this RichEditBoxElement el, int maxLength) =>
        el with { MaxLength = maxLength };

    /// <summary>How text wraps within the editor.</summary>
    public static RichEditBoxElement TextWrapping(this RichEditBoxElement el, TextWrapping wrapping = Microsoft.UI.Xaml.TextWrapping.Wrap) =>
        el with { TextWrapping = wrapping };

    /// <summary>Whether Enter inserts a newline (vs committing input).</summary>
    public static RichEditBoxElement AcceptsReturn(this RichEditBoxElement el, bool accepts = true) =>
        el with { AcceptsReturn = accepts };

    /// <summary>Brush used to render the selection highlight.</summary>
    public static RichEditBoxElement SelectionHighlightColor(this RichEditBoxElement el, Microsoft.UI.Xaml.Media.SolidColorBrush brush) =>
        el with { SelectionHighlightColor = brush };

    // ── TextField sugar ────────────────────────────────────────────────

    public static TextFieldElement ReadOnly(this TextFieldElement el, bool readOnly = true) =>
        el with { IsReadOnly = readOnly };

    public static TextFieldElement AcceptsReturn(this TextFieldElement el, bool accepts = true) =>
        el with { AcceptsReturn = accepts };

    public static TextFieldElement TextWrapping(this TextFieldElement el, TextWrapping wrapping = Microsoft.UI.Xaml.TextWrapping.Wrap) =>
        el with { TextWrapping = wrapping };

    /// <summary>Maximum number of characters allowed in the box. Use <c>0</c> for no limit.</summary>
    public static TextFieldElement MaxLength(this TextFieldElement el, int maxLength) =>
        el with { MaxLength = maxLength };

    /// <summary>Enable or disable built-in spell-check.</summary>
    public static TextFieldElement IsSpellCheckEnabled(this TextFieldElement el, bool enabled = true) =>
        el with { IsSpellCheckEnabled = enabled };

    /// <summary>Forces input to upper/lower-case as the user types.</summary>
    public static TextFieldElement CharacterCasing(this TextFieldElement el, CharacterCasing casing) =>
        el with { CharacterCasing = casing };

    /// <summary>Sets horizontal text alignment within the box.</summary>
    public static TextFieldElement TextAlignment(this TextFieldElement el, TextAlignment alignment) =>
        el with { TextAlignment = alignment };

    /// <summary>Sets the help/description text rendered below the box.</summary>
    public static TextFieldElement Description(this TextFieldElement el, string description) =>
        el with { Description = description };

    // ── Path sugar ─────────────────────────────────────────────────────

    public static PathElement StrokeDashArray(this PathElement el, params double[] dashes)
    {
        var dc = new Microsoft.UI.Xaml.Media.DoubleCollection();
        foreach (var d in dashes) dc.Add(d);
        return el with { StrokeDashArray = dc };
    }

    /// <summary>Cap rendered at the start of an open stroke.</summary>
    public static PathElement StrokeStartLineCap(this PathElement el, PenLineCap cap) =>
        el with { StrokeStartLineCap = cap };

    /// <summary>Cap rendered at the end of an open stroke.</summary>
    public static PathElement StrokeEndLineCap(this PathElement el, PenLineCap cap) =>
        el with { StrokeEndLineCap = cap };

    /// <summary>Join style between two connected stroke segments.</summary>
    public static PathElement StrokeLineJoin(this PathElement el, PenLineJoin join) =>
        el with { StrokeLineJoin = join };

    /// <summary>Maximum extent of a miter join relative to half the stroke thickness.</summary>
    public static PathElement StrokeMiterLimit(this PathElement el, double limit) =>
        el with { StrokeMiterLimit = limit };

    /// <summary>Cap rendered on dashes when <c>StrokeDashArray</c> is set.</summary>
    public static PathElement StrokeDashCap(this PathElement el, PenLineCap cap) =>
        el with { StrokeDashCap = cap };

    /// <summary>Distance into the dash pattern at which to begin drawing.</summary>
    public static PathElement StrokeDashOffset(this PathElement el, double offset) =>
        el with { StrokeDashOffset = offset };

    /// <summary>How interior regions are determined for fills.</summary>
    public static PathElement FillRule(this PathElement el, FillRule rule) =>
        el with { FillRule = rule };

    // ── Image extras (spec §10) ─────────────────────────────────────

    /// <summary>Nine-grid (slice) values for resolution-independent corner stretching.</summary>
    public static ImageElement NineGrid(this ImageElement el, Thickness nineGrid) =>
        el with { NineGrid = nineGrid };

    // ── PipsPager extras (spec §12) ─────────────────────────────────

    /// <summary>Whether the selected index wraps around the ends.</summary>
    public static PipsPagerElement WrapMode(this PipsPagerElement el, PipsPagerWrapMode mode) =>
        el with { WrapMode = mode };

    /// <summary>Maximum number of visible pips.</summary>
    public static PipsPagerElement MaxVisiblePips(this PipsPagerElement el, int max) =>
        el with { MaxVisiblePips = max };

    /// <summary>When the previous button shows.</summary>
    public static PipsPagerElement PreviousButtonVisibility(this PipsPagerElement el, PipsPagerButtonVisibility visibility) =>
        el with { PreviousButtonVisibility = visibility };

    /// <summary>When the next button shows.</summary>
    public static PipsPagerElement NextButtonVisibility(this PipsPagerElement el, PipsPagerButtonVisibility visibility) =>
        el with { NextButtonVisibility = visibility };

    // ── RefreshContainer extras (spec §12) ──────────────────────────

    /// <summary>Direction the user pulls to trigger refresh.</summary>
    public static RefreshContainerElement PullDirection(this RefreshContainerElement el, RefreshPullDirection direction) =>
        el with { PullDirection = direction };

    // ── ParallaxView extras (spec §12) ──────────────────────────────

    /// <summary>Source UIElement that drives the parallax (typically a ScrollViewer / ListView).</summary>
    public static ParallaxViewElement Source(this ParallaxViewElement el, UIElement source) =>
        el with { Source = source };

    /// <summary>Vertical-axis source offset (in pixels) at which parallax begins.</summary>
    public static ParallaxViewElement VerticalSourceStartOffset(this ParallaxViewElement el, double offset) =>
        el with { VerticalSourceStartOffset = offset };

    /// <summary>Vertical-axis source offset (in pixels) at which parallax ends.</summary>
    public static ParallaxViewElement VerticalSourceEndOffset(this ParallaxViewElement el, double offset) =>
        el with { VerticalSourceEndOffset = offset };

    // ── IsEnabled (on Control — works on buttons, inputs, etc.) ────

    public static T Disabled<T>(this T el, bool disabled = true) where T : Element =>
        Modify(el, new ElementModifiers { IsEnabled = !disabled });

    /// <summary>
    /// Keeps the button keyboard-focusable while presenting it as disabled
    /// (visually dimmed, click suppressed). Use for submit buttons gated on
    /// validation: a true <c>.Disabled(true)</c> removes the button from tab
    /// order, which combined with commit-on-blur inputs like NumberBox/
    /// DatePicker produces a focus trap where Tab skips a Submit that is
    /// *about* to become valid. Conceptually the Fluent UI React
    /// <c>disabledFocusable</c> / ARIA <c>aria-disabled</c> pattern; UIA still
    /// sees the button as enabled (a custom AutomationPeer override for full
    /// AT "unavailable" reporting is a tracked follow-up).
    /// </summary>
    public static ButtonElement DisabledFocusable(this ButtonElement el, bool disabled = true) =>
        el with { IsDisabledFocusable = disabled };

    // ── Background (Panel, Control, Border) ────────────────────────

    /// <summary>
    /// Sets the background from a color string. Allocates a new SolidColorBrush per call.
    /// On hot render paths, prefer the <see cref="Background{T}(T, Brush)"/> overload with a cached brush.
    /// </summary>
    public static T Background<T>(this T el, string color) where T : Element =>
        Modify(el, new ElementModifiers { Background = BrushHelper.Parse(color) });

    public static T Background<T>(this T el, Brush brush) where T : Element =>
        Modify(el, new ElementModifiers { Background = brush });

    /// <summary>
    /// Sets the background from a WinUI theme resource. Resolves at render time
    /// and adapts when the theme changes (Light ↔ Dark).
    /// Usage: <c>VStack(children).Background(Theme.CardBackground)</c>
    /// </summary>
    public static T Background<T>(this T el, ThemeRef theme) where T : Element =>
        ModifyTheme(el, "Background", theme);

    // ── Foreground (Control, TextBlock) ──────────────────────────

    /// <summary>
    /// Sets the foreground from a color string. Allocates a new SolidColorBrush per call.
    /// On hot render paths, prefer the <see cref="Foreground{T}(T, Brush)"/> overload with a cached brush.
    /// </summary>
    public static T Foreground<T>(this T el, string color) where T : Element =>
        Modify(el, new ElementModifiers { Foreground = BrushHelper.Parse(color) });

    public static T Foreground<T>(this T el, Brush brush) where T : Element =>
        Modify(el, new ElementModifiers { Foreground = brush });

    /// <summary>
    /// Sets the foreground from a WinUI theme resource. Resolves at render time
    /// and adapts when the theme changes (Light ↔ Dark).
    /// Usage: <c>Text("Hello").Foreground(Theme.PrimaryText)</c>
    /// </summary>
    public static T Foreground<T>(this T el, ThemeRef theme) where T : Element =>
        ModifyTheme(el, "Foreground", theme);

    // ── CornerRadius (on Control and Border) ────────────────────────

    public static T CornerRadius<T>(this T el, double radius) where T : Element =>
        Modify(el, new ElementModifiers { CornerRadius = new Microsoft.UI.Xaml.CornerRadius(radius) });

    public static T CornerRadius<T>(this T el, double topLeft, double topRight, double bottomRight, double bottomLeft) where T : Element =>
        Modify(el, new ElementModifiers { CornerRadius = new Microsoft.UI.Xaml.CornerRadius(topLeft, topRight, bottomRight, bottomLeft) });

    // ── Border brush/thickness (on Control and Border) ─────────────

    /// <summary>
    /// Sets the border from a color string. Allocates a new SolidColorBrush per call.
    /// On hot render paths, prefer the <see cref="WithBorder{T}(T, Brush, double)"/> overload with a cached brush.
    /// </summary>
    public static T WithBorder<T>(this T el, string color, double thickness = 1) where T : Element =>
        Modify(el, new ElementModifiers { BorderBrush = BrushHelper.Parse(color), BorderThickness = new Thickness(thickness) });

    public static T WithBorder<T>(this T el, Brush brush, double thickness = 1) where T : Element =>
        Modify(el, new ElementModifiers { BorderBrush = brush, BorderThickness = new Thickness(thickness) });

    /// <summary>
    /// Sets the border from a WinUI theme resource. Resolves at render time
    /// and adapts when the theme changes (Light ↔ Dark).
    /// Usage: <c>VStack(children).WithBorder(Theme.CardStroke)</c>
    /// </summary>
    public static T WithBorder<T>(this T el, ThemeRef theme, double thickness = 1) where T : Element =>
        ModifyTheme(el with { Modifiers = el.Modifiers is not null
            ? el.Modifiers.Merge(new ElementModifiers { BorderThickness = new Thickness(thickness) })
            : new ElementModifiers { BorderThickness = new Thickness(thickness) } },
            "BorderBrush", theme);

    // ── Lightweight Styling (per-control resource overrides) ────────

    /// <summary>
    /// Configures per-control resource overrides via WinUI's lightweight styling
    /// mechanism. Overrides are injected into <see cref="FrameworkElement.Resources"/>
    /// so the control's <see cref="Microsoft.UI.Xaml.VisualStateManager"/> picks them
    /// up automatically — hover, pressed, and disabled states all respect the overrides
    /// without requiring a custom template.
    /// <para>
    /// <b>Brand-colored button:</b>
    /// <code>
    /// Button("Submit").Resources(r => r
    ///     .Set("ButtonBackground", "#0078D4")
    ///     .Set("ButtonBackgroundPointerOver", "#106EBE")
    ///     .Set("ButtonBackgroundPressed", "#005A9E"))
    /// </code>
    /// </para>
    /// <para>
    /// <b>Scoped cascading:</b> resources set on a parent panel cascade to child
    /// controls, matching WinUI's resource lookup behavior.
    /// </para>
    /// </summary>
    public static T Resources<T>(this T el, Action<Microsoft.UI.Reactor.Elements.ResourceBuilder> configure) where T : Element
    {
        var builder = new Microsoft.UI.Reactor.Elements.ResourceBuilder();
        configure(builder);
        return el with { ResourceOverrides = builder.Build() };
    }

    // ── Flex sugar ──────────────────────────────────────────────────

    public static FlexElement FlexPadding(this FlexElement el, double uniform) =>
        el with { FlexPadding = new Thickness(uniform) };

    public static FlexElement FlexPadding(this FlexElement el, double horizontal, double vertical) =>
        el with { FlexPadding = new Thickness(horizontal, vertical, horizontal, vertical) };

    public static FlexElement FlexPadding(this FlexElement el, double left, double top, double right, double bottom) =>
        el with { FlexPadding = new Thickness(left, top, right, bottom) };

    // ── HyperlinkButton sugar ───────────────────────────────────────

    /// <summary>
    /// Sets the navigation target for the hyperlink. Pairs naturally with
    /// the <c>HyperlinkButton(Command)</c> factory when a command-driven
    /// hyperlink also needs an external URI.
    /// </summary>
    public static HyperlinkButtonElement NavigateUri(this HyperlinkButtonElement el, Uri uri) =>
        el with { NavigateUri = uri };

    // ── Stack sugar ─────────────────────────────────────────────────

    public static StackElement Spacing(this StackElement el, double spacing) =>
        el with { Spacing = spacing };

    // ── TextField sugar ─────────────────────────────────────────────

    public static TextFieldElement Header(this TextFieldElement el, string header) =>
        el with { Header = header };

    // ── ComboBox sugar ──────────────────────────────────────────────

    public static ComboBoxElement Placeholder(this ComboBoxElement el, string text) =>
        el with { PlaceholderText = text };

    public static ComboBoxElement Editable(this ComboBoxElement el, bool editable = true) =>
        el with { IsEditable = editable };

    public static ComboBoxElement Header(this ComboBoxElement el, string header) =>
        el with { Header = header };

    // ── NumberBox sugar ─────────────────────────────────────────────

    public static NumberBoxElement Range(this NumberBoxElement el, double min, double max) =>
        el with { Minimum = min, Maximum = max };

    public static NumberBoxElement SpinButtons(this NumberBoxElement el, NumberBoxSpinButtonPlacementMode placement = NumberBoxSpinButtonPlacementMode.Inline) =>
        el with { SpinButtonPlacement = placement };

    /// <summary>Sets a custom number formatter (currency, percent, scientific, etc.).</summary>
    public static NumberBoxElement NumberFormatter(this NumberBoxElement el, global::Windows.Globalization.NumberFormatting.INumberFormatter2 formatter) =>
        el with { NumberFormatter = formatter };

    /// <summary>Whether the user can type arithmetic expressions (e.g. <c>2*3+1</c>) that resolve on commit.</summary>
    public static NumberBoxElement AcceptsExpression(this NumberBoxElement el, bool accepts = true) =>
        el with { AcceptsExpression = accepts };

    /// <summary>Controls how invalid input is handled on commit.</summary>
    public static NumberBoxElement ValidationMode(this NumberBoxElement el, NumberBoxValidationMode mode) =>
        el with { ValidationMode = mode };

    /// <summary>Sets the help/description text rendered below the box.</summary>
    public static NumberBoxElement Description(this NumberBoxElement el, string description) =>
        el with { Description = description };

    // ── Slider sugar ────────────────────────────────────────────────

    public static SliderElement StepFrequency(this SliderElement el, double step) =>
        el with { StepFrequency = step };

    public static SliderElement Header(this SliderElement el, string header) =>
        el with { Header = header };

    /// <summary>Sets the slider orientation (horizontal or vertical).</summary>
    public static SliderElement Orientation(this SliderElement el, Orientation orientation) =>
        el with { Orientation = orientation };

    /// <summary>Sets the interval between tick marks. Pair with <c>.TickPlacement</c> to make ticks visible.</summary>
    public static SliderElement TickFrequency(this SliderElement el, double frequency) =>
        el with { TickFrequency = frequency };

    /// <summary>Controls where tick marks render relative to the slider track.</summary>
    public static SliderElement TickPlacement(this SliderElement el, TickPlacement placement) =>
        el with { TickPlacement = placement };

    /// <summary>Whether the thumb snaps to ticks or step values during drag.</summary>
    public static SliderElement SnapsTo(this SliderElement el, SliderSnapsTo snapsTo) =>
        el with { SnapsTo = snapsTo };

    /// <summary>Whether the floating value tooltip appears while dragging the thumb.</summary>
    public static SliderElement ThumbToolTip(this SliderElement el, bool enabled = true) =>
        el with { IsThumbToolTipEnabled = enabled };

    // ── ToggleSwitch sugar ──────────────────────────────────────────

    public static ToggleSwitchElement Header(this ToggleSwitchElement el, string header) =>
        el with { Header = header };

    // ── ColorPicker sugar ───────────────────────────────────────────

    /// <summary>Whether the alpha (transparency) channel is editable.</summary>
    public static ColorPickerElement AlphaEnabled(this ColorPickerElement el, bool enabled = true) =>
        el with { IsAlphaEnabled = enabled };

    /// <summary>Whether the "More" disclosure button is shown to expand additional inputs.</summary>
    public static ColorPickerElement MoreButtonVisible(this ColorPickerElement el, bool visible = true) =>
        el with { IsMoreButtonVisible = visible };

    /// <summary>Whether the 2D hue/saturation spectrum is shown.</summary>
    public static ColorPickerElement ColorSpectrumVisible(this ColorPickerElement el, bool visible = true) =>
        el with { IsColorSpectrumVisible = visible };

    /// <summary>Whether the lightness/value slider is shown.</summary>
    public static ColorPickerElement ColorSliderVisible(this ColorPickerElement el, bool visible) =>
        el with { IsColorSliderVisible = visible };

    /// <summary>Whether the per-channel numeric text inputs (RGB / HSV) are shown.</summary>
    public static ColorPickerElement ColorChannelTextInputVisible(this ColorPickerElement el, bool visible) =>
        el with { IsColorChannelTextInputVisible = visible };

    /// <summary>Whether the hex code text input is shown.</summary>
    public static ColorPickerElement HexInputVisible(this ColorPickerElement el, bool visible) =>
        el with { IsHexInputVisible = visible };

    // ── ToggleButton sugar ──────────────────────────────────────────

    /// <summary>Enable the three-state cycle. Pair with <c>.CheckedState(...)</c> and the <c>.CheckedStateChanged(...)</c> event fluent.</summary>
    public static ToggleButtonElement IsThreeState(this ToggleButtonElement el, bool isThreeState = true) =>
        el with { IsThreeState = isThreeState };

    /// <summary>Sets the three-state value (<c>null</c> = indeterminate). Active only when <c>IsThreeState</c> is true.</summary>
    public static ToggleButtonElement CheckedState(this ToggleButtonElement el, bool? state) =>
        el with { CheckedState = state, IsThreeState = true };

    // ── PasswordBox sugar ───────────────────────────────────────────

    /// <summary>Maximum number of characters allowed. <c>0</c> = no limit.</summary>
    public static PasswordBoxElement MaxLength(this PasswordBoxElement el, int maxLength) =>
        el with { MaxLength = maxLength };

    /// <summary>Optional label rendered above the box.</summary>
    public static PasswordBoxElement Header(this PasswordBoxElement el, string header) =>
        el with { Header = header };

    /// <summary>Controls how the reveal button behaves.</summary>
    public static PasswordBoxElement PasswordRevealMode(this PasswordBoxElement el, PasswordRevealMode mode) =>
        el with { PasswordRevealMode = mode };

    /// <summary>Character displayed in place of the entered password.</summary>
    public static PasswordBoxElement PasswordChar(this PasswordBoxElement el, string passwordChar) =>
        el with { PasswordChar = passwordChar };

    // ── AutoSuggestBox sugar ────────────────────────────────────────

    /// <summary>Optional label rendered above the box.</summary>
    public static AutoSuggestBoxElement Header(this AutoSuggestBoxElement el, string header) =>
        el with { Header = header };

    /// <summary>Icon rendered in the trailing query slot.</summary>
    public static AutoSuggestBoxElement QueryIcon(this AutoSuggestBoxElement el, IconData icon) =>
        el with { QueryIcon = icon };

    /// <summary>Programmatically open or close the suggestion list.</summary>
    public static AutoSuggestBoxElement IsSuggestionListOpen(this AutoSuggestBoxElement el, bool open = true) =>
        el with { IsSuggestionListOpen = open };

    // ── ComboBox additional sugar (spec §3.7) ───────────────────────

    /// <summary>Maximum pixel height of the open drop-down.</summary>
    public static ComboBoxElement MaxDropDownHeight(this ComboBoxElement el, double height) =>
        el with { MaxDropDownHeight = height };

    /// <summary>Help text rendered below the box.</summary>
    public static ComboBoxElement Description(this ComboBoxElement el, string description) =>
        el with { Description = description };

    // ── RatingControl sugar ─────────────────────────────────────────

    public static RatingControlElement MaxRating(this RatingControlElement el, int max) =>
        el with { MaxRating = max };

    public static RatingControlElement ReadOnly(this RatingControlElement el, bool readOnly = true) =>
        el with { IsReadOnly = readOnly };

    /// <summary>Promotes the existing <c>Caption</c> init property to a fluent.</summary>
    public static RatingControlElement Caption(this RatingControlElement el, string caption) =>
        el with { Caption = caption };

    /// <summary>Star value shown when the rating is unset. Use a negative number to disable the placeholder.</summary>
    public static RatingControlElement PlaceholderValue(this RatingControlElement el, double value) =>
        el with { PlaceholderValue = value };

    /// <summary>Integer rating to assume when the user first interacts.</summary>
    public static RatingControlElement InitialSetValue(this RatingControlElement el, int value) =>
        el with { InitialSetValue = value };

    // ── CalendarDatePicker sugar (spec §4.1) ────────────────────────

    /// <summary>Display format string for the picker's text. See WinUI's <c>DateFormat</c> reference.</summary>
    public static CalendarDatePickerElement DateFormat(this CalendarDatePickerElement el, string format) =>
        el with { DateFormat = format };

    /// <summary>Highlight today's date in the popup.</summary>
    public static CalendarDatePickerElement IsTodayHighlighted(this CalendarDatePickerElement el, bool highlighted = true) =>
        el with { IsTodayHighlighted = highlighted };

    /// <summary>Programmatically open or close the calendar popup.</summary>
    public static CalendarDatePickerElement IsCalendarOpen(this CalendarDatePickerElement el, bool open = true) =>
        el with { IsCalendarOpen = open };

    /// <summary>Show month/year group label headers in the popup.</summary>
    public static CalendarDatePickerElement IsGroupLabelVisible(this CalendarDatePickerElement el, bool visible = true) =>
        el with { IsGroupLabelVisible = visible };

    // ── DatePicker sugar (spec §4.1) ────────────────────────────────

    /// <summary>Display format string for the day column.</summary>
    public static DatePickerElement DayFormat(this DatePickerElement el, string format) =>
        el with { DayFormat = format };

    /// <summary>Display format string for the month column.</summary>
    public static DatePickerElement MonthFormat(this DatePickerElement el, string format) =>
        el with { MonthFormat = format };

    /// <summary>Display format string for the year column.</summary>
    public static DatePickerElement YearFormat(this DatePickerElement el, string format) =>
        el with { YearFormat = format };

    /// <summary>Layout direction of the picker.</summary>
    public static DatePickerElement Orientation(this DatePickerElement el, Orientation orientation) =>
        el with { Orientation = orientation };

    // ── CalendarView sugar (spec §4.1) ──────────────────────────────

    /// <summary>Earliest selectable date.</summary>
    public static CalendarViewElement MinDate(this CalendarViewElement el, DateTimeOffset date) =>
        el with { MinDate = date };

    /// <summary>Latest selectable date.</summary>
    public static CalendarViewElement MaxDate(this CalendarViewElement el, DateTimeOffset date) =>
        el with { MaxDate = date };

    /// <summary>Day of the week that starts each row.</summary>
    public static CalendarViewElement FirstDayOfWeek(this CalendarViewElement el, global::Windows.Globalization.DayOfWeek day) =>
        el with { FirstDayOfWeek = day };

    /// <summary>How many week rows to display in month mode (2–8).</summary>
    public static CalendarViewElement NumberOfWeeksInView(this CalendarViewElement el, int weeks) =>
        el with { NumberOfWeeksInView = weeks };

    /// <summary>Initial display mode (Month / Year / Decade).</summary>
    public static CalendarViewElement DisplayMode(this CalendarViewElement el, CalendarViewDisplayMode mode) =>
        el with { DisplayMode = mode };

    // ── ColorPicker (spec §3.11 spectrum + bounds) ──────────────────

    /// <summary>Shape of the 2D color spectrum (Box or Ring).</summary>
    public static ColorPickerElement ColorSpectrumShape(this ColorPickerElement el, ColorSpectrumShape shape) =>
        el with { ColorSpectrumShape = shape };

    /// <summary>Inclusive hue bounds (0–359).</summary>
    public static ColorPickerElement HueRange(this ColorPickerElement el, int minHue, int maxHue) =>
        el with { MinHue = minHue, MaxHue = maxHue };

    /// <summary>Inclusive saturation bounds (0–100).</summary>
    public static ColorPickerElement SaturationRange(this ColorPickerElement el, int minSaturation, int maxSaturation) =>
        el with { MinSaturation = minSaturation, MaxSaturation = maxSaturation };

    /// <summary>Inclusive value/brightness bounds (0–100).</summary>
    public static ColorPickerElement ValueRange(this ColorPickerElement el, int minValue, int maxValue) =>
        el with { MinValue = minValue, MaxValue = maxValue };

    // ── InfoBar sugar ───────────────────────────────────────────────

    public static InfoBarElement Severity(this InfoBarElement el, InfoBarSeverity severity) =>
        el with { Severity = severity };

    public static InfoBarElement Closable(this InfoBarElement el, bool closable = true) =>
        el with { IsClosable = closable };

    /// <summary>Custom icon source (overrides the severity-default icon).</summary>
    public static InfoBarElement IconSource(this InfoBarElement el, IconData icon) =>
        el with { IconSource = icon };

    /// <summary>Custom rich content rendered below the message (overload to the message string).</summary>
    public static InfoBarElement Content(this InfoBarElement el, Element content) =>
        el with { Content = content };

    // ── Expander extra sugar (HeaderTemplate, ContentTransitions) ───

    /// <summary>
    /// Sets an Element header (overrides the string <c>Header</c> from the
    /// factory). Mirrors WinUI's <c>HeaderTemplate</c> slot.
    /// </summary>
    public static ExpanderElement HeaderTemplate(this ExpanderElement el, Element header) =>
        el with { HeaderTemplate = header };

    /// <summary>Custom <c>TransitionCollection</c> applied to the expanding content area.</summary>
    public static ExpanderElement ContentTransitions(this ExpanderElement el, Microsoft.UI.Xaml.Media.Animation.TransitionCollection transitions) =>
        el with { ContentTransitions = transitions };

    // ── SplitView extra sugar (PaneBackground, LightDismissOverlayMode) ──

    /// <summary>Pane background brush.</summary>
    public static SplitViewElement PaneBackground(this SplitViewElement el, Brush brush) =>
        el with { PaneBackground = brush };

    /// <summary>
    /// Pane background bound to a WinUI theme resource (light/dark adaptive).
    /// Resolves via the same ThemeBindings pipeline that powers
    /// <c>Background(ThemeRef)</c>.
    /// </summary>
    public static SplitViewElement PaneBackground(this SplitViewElement el, ThemeRef theme) =>
        ModifyTheme(el, "PaneBackground", theme);

    /// <summary>How the light-dismiss overlay reacts to taps in Overlay mode.</summary>
    public static SplitViewElement LightDismissOverlayMode(this SplitViewElement el, LightDismissOverlayMode mode) =>
        el with { LightDismissOverlayMode = mode };

    // ── ListView / GridView (spec §8 — incremental loading + container style) ──

    /// <summary>Style applied to each generated <c>ListViewItem</c> container.</summary>
    public static ListViewElement ItemContainerStyle(this ListViewElement el, Style style) =>
        el with { ItemContainerStyle = style };

    /// <summary>Controls when incremental data sources fetch the next page.</summary>
    public static ListViewElement IncrementalLoadingTrigger(this ListViewElement el, IncrementalLoadingTrigger trigger) =>
        el with { IncrementalLoadingTrigger = trigger };

    /// <summary>Style applied to each generated <c>GridViewItem</c> container.</summary>
    public static GridViewElement ItemContainerStyle(this GridViewElement el, Style style) =>
        el with { ItemContainerStyle = style };

    /// <summary>Controls when incremental data sources fetch the next page.</summary>
    public static GridViewElement IncrementalLoadingTrigger(this GridViewElement el, IncrementalLoadingTrigger trigger) =>
        el with { IncrementalLoadingTrigger = trigger };

    // ── ContentDialog (spec §9 button-enabled + opening lifecycle) ──

    /// <summary>Enables/disables the primary button while the dialog is open.</summary>
    public static ContentDialogElement IsPrimaryButtonEnabled(this ContentDialogElement el, bool enabled = true) =>
        el with { IsPrimaryButtonEnabled = enabled };

    /// <summary>Enables/disables the secondary button while the dialog is open.</summary>
    public static ContentDialogElement IsSecondaryButtonEnabled(this ContentDialogElement el, bool enabled = true) =>
        el with { IsSecondaryButtonEnabled = enabled };

    // ── Flyout (spec §9 show-mode + animations + pass-through) ──────

    /// <summary>How the flyout reacts to outside clicks (Auto / Standard / Transient / TransientWithDismissOnPointerMoveAway).</summary>
    public static FlyoutElement ShowMode(this FlyoutElement el, FlyoutShowMode mode) =>
        el with { ShowMode = mode };

    /// <summary>Whether the flyout animates on open/close.</summary>
    public static FlyoutElement AreOpenCloseAnimationsEnabled(this FlyoutElement el, bool enabled = true) =>
        el with { AreOpenCloseAnimationsEnabled = enabled };

    /// <summary>Element whose input passes through the light-dismiss overlay.</summary>
    public static FlyoutElement OverlayInputPassThroughElement(this FlyoutElement el, Element passThrough) =>
        el with { OverlayInputPassThroughElement = passThrough };

    // ── TeachingTip (spec §9 icon + hero + placement) ───────────────

    /// <summary>Custom icon source rendered in the tip's leading slot.</summary>
    public static TeachingTipElement IconSource(this TeachingTipElement el, IconData icon) =>
        el with { IconSource = icon };

    /// <summary>Optional "hero" Element (image / banner) rendered above the title.</summary>
    public static TeachingTipElement HeroContent(this TeachingTipElement el, Element hero) =>
        el with { HeroContent = hero };

    /// <summary>Extra margin around the tip when placed relative to its target.</summary>
    public static TeachingTipElement PlacementMargin(this TeachingTipElement el, Thickness margin) =>
        el with { PlacementMargin = margin };

    /// <summary>Preferred placement edge.</summary>
    public static TeachingTipElement PreferredPlacement(this TeachingTipElement el, TeachingTipPlacementMode placement) =>
        el with { PreferredPlacement = placement };

    // ── WrapGrid attached-prop fluents ──────────────────────────────

    /// <summary>
    /// Sets <c>VariableSizedWrapGrid.ColumnSpan</c> on this child. Only
    /// meaningful when the element is a child of a <see cref="WrapGridElement"/>.
    /// </summary>
    public static T WrapGridColumnSpan<T>(this T el, int columnSpan) where T : Element
    {
        var existing = el.GetAttached<WrapGridAttached>();
        return (T)el.SetAttached(new WrapGridAttached(
            RowSpan: existing?.RowSpan ?? 1,
            ColumnSpan: columnSpan));
    }

    /// <summary>
    /// Sets <c>VariableSizedWrapGrid.RowSpan</c> on this child. Only meaningful
    /// when the element is a child of a <see cref="WrapGridElement"/>.
    /// </summary>
    public static T WrapGridRowSpan<T>(this T el, int rowSpan) where T : Element
    {
        var existing = el.GetAttached<WrapGridAttached>();
        return (T)el.SetAttached(new WrapGridAttached(
            RowSpan: rowSpan,
            ColumnSpan: existing?.ColumnSpan ?? 1));
    }

    // ── NavigationView sugar ────────────────────────────────────────

    public static NavigationViewElement PaneDisplayMode(this NavigationViewElement el, NavigationViewPaneDisplayMode mode) =>
        el with { PaneDisplayMode = mode };

    public static NavigationViewElement PaneTitle(this NavigationViewElement el, string title) =>
        el with { PaneTitle = title };

    /// <summary>Sets the AutoSuggestBox rendered at the top of the pane.</summary>
    public static NavigationViewElement AutoSuggestBox(this NavigationViewElement el, AutoSuggestBoxElement box) =>
        el with { AutoSuggestBox = box };

    /// <summary>Sets the element rendered at the bottom of the pane (below menu items).</summary>
    public static NavigationViewElement PaneFooter(this NavigationViewElement el, Element footer) =>
        el with { PaneFooter = footer };

    /// <summary>Sets the custom element rendered between the AutoSuggestBox and the menu items.</summary>
    public static NavigationViewElement PaneCustomContent(this NavigationViewElement el, Element content) =>
        el with { PaneCustomContent = content };

    /// <summary>Sets the width of the pane when expanded.</summary>
    public static NavigationViewElement OpenPaneLength(this NavigationViewElement el, double length) =>
        el with { OpenPaneLength = length };

    /// <summary>Sets the window width below which the pane collapses to compact mode.</summary>
    public static NavigationViewElement CompactModeThresholdWidth(this NavigationViewElement el, double width) =>
        el with { CompactModeThresholdWidth = width };

    /// <summary>Sets the window width at which the pane auto-expands.</summary>
    public static NavigationViewElement ExpandedModeThresholdWidth(this NavigationViewElement el, double width) =>
        el with { ExpandedModeThresholdWidth = width };

    /// <summary>
    /// Auto-syncs this NavigationView with a NavigationHandle: sets <c>SelectedTag</c>
    /// from the current route, wires <c>OnSelectedTagChanged</c> to navigate,
    /// <c>OnBackRequested</c> to <c>GoBack</c>, and <c>IsBackEnabled</c> to <c>CanGoBack</c>.
    /// </summary>
    /// <param name="el">The NavigationView element to configure.</param>
    /// <param name="nav">The navigation handle obtained from <c>UseNavigation</c>.</param>
    /// <param name="routeToTag">Maps a route to its NavigationViewItem tag. Return null for routes without a corresponding menu item.</param>
    /// <param name="tagToRoute">Maps a NavigationViewItem tag back to a route for <c>OnSelectedTagChanged</c>.</param>
    public static NavigationViewElement WithNavigation<TRoute>(
        this NavigationViewElement el,
        Navigation.NavigationHandle<TRoute> nav,
        Func<TRoute, string?> routeToTag,
        Func<string, TRoute> tagToRoute) where TRoute : notnull
    => el with
    {
        SelectedTag = routeToTag(nav.CurrentRoute),
        IsBackEnabled = nav.CanGoBack,
        OnSelectedTagChanged = tag =>
        {
            if (tag is not null)
            {
                var route = tagToRoute(tag);
                if (!EqualityComparer<TRoute>.Default.Equals(route, nav.CurrentRoute))
                    nav.Navigate(route);
            }
        },
        OnBackRequested = () => nav.GoBack(),
    };

    // ── TitleBar sugar ──────────────────────────────────────────────

    public static TitleBarElement Subtitle(this TitleBarElement el, string subtitle) =>
        el with { Subtitle = subtitle };

    /// <summary>Shows or hides the back button on the title bar.</summary>
    public static TitleBarElement BackButtonVisible(this TitleBarElement el, bool visible) =>
        el with { IsBackButtonVisible = visible };

    /// <summary>Enables or disables the back button (when visible).</summary>
    public static TitleBarElement BackButtonEnabled(this TitleBarElement el, bool enabled) =>
        el with { IsBackButtonEnabled = enabled };

    /// <summary>Shows or hides the pane toggle (hamburger) button on the title bar.</summary>
    public static TitleBarElement PaneToggleButtonVisible(this TitleBarElement el, bool visible) =>
        el with { IsPaneToggleButtonVisible = visible };

    /// <summary>Sets the centered content slot of the title bar (typically a search box).</summary>
    public static TitleBarElement Content(this TitleBarElement el, Element content) =>
        el with { Content = content };

    /// <summary>Sets the trailing-edge content slot (typically a profile / settings element).</summary>
    public static TitleBarElement RightHeader(this TitleBarElement el, Element rightHeader) =>
        el with { RightHeader = rightHeader };

    /// <summary>Sets the icon shown in the leading slot. Pass a <see cref="SymbolIconData"/>, <see cref="FontIconData"/>, <see cref="ImageIconData"/>, or <see cref="BitmapIconData"/>.</summary>
    public static TitleBarElement Icon(this TitleBarElement el, IconData icon) =>
        el with { Icon = icon };

    /// <summary>Convenience overload — sets the icon to a bundled image / .ico via a Uri string (e.g. <c>"ms-appx:///Assets/AppIcon.ico"</c>).</summary>
    public static TitleBarElement Icon(this TitleBarElement el, string imageUri) =>
        el with { Icon = new ImageIconData(new Uri(imageUri)) };

    /// <summary>
    /// Auto-syncs this TitleBar's back button with a NavigationHandle: sets
    /// <c>IsBackButtonVisible</c> and <c>IsBackButtonEnabled</c> from <c>CanGoBack</c>,
    /// and wires <c>OnBackRequested</c> to <c>GoBack</c>.
    /// </summary>
    public static TitleBarElement WithNavigation<TRoute>(
        this TitleBarElement el,
        Navigation.NavigationHandle<TRoute> nav) where TRoute : notnull
    => el with
    {
        IsBackButtonVisible = nav.CanGoBack,
        IsBackButtonEnabled = nav.CanGoBack,
        OnBackRequested = () => nav.GoBack(),
    };

    public static TitleBarElement Set(this TitleBarElement el, Action<WinUI.TitleBar> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // ── ExpanderElement sugar ───────────────────────────────────────

    public static ExpanderElement Direction(this ExpanderElement el, ExpandDirection dir) =>
        el with { ExpandDirection = dir };

    // ── Expander sugar ──────────────────────────────────────────────

    // ── RepeatButton sugar ──────────────────────────────────────────

    public static RepeatButtonElement Delay(this RepeatButtonElement el, int delay) =>
        el with { Delay = delay };

    public static RepeatButtonElement Interval(this RepeatButtonElement el, int interval) =>
        el with { Interval = interval };

    // ── ProgressRing sugar ──────────────────────────────────────────

    public static ProgressRingElement Active(this ProgressRingElement el, bool active = true) =>
        el with { IsActive = active };

    // ── PersonPicture sugar ─────────────────────────────────────────

    public static PersonPictureElement DisplayName(this PersonPictureElement el, string name) =>
        el with { DisplayName = name };

    public static PersonPictureElement Initials(this PersonPictureElement el, string initials) =>
        el with { Initials = initials };

    // ── ListView / GridView sugar ───────────────────────────────────

    public static ListViewElement SelectionMode(this ListViewElement el, ListViewSelectionMode mode) =>
        el with { SelectionMode = mode };

    public static GridViewElement SelectionMode(this GridViewElement el, ListViewSelectionMode mode) =>
        el with { SelectionMode = mode };

    // ── TabView sugar ───────────────────────────────────────────────

    public static TabViewElement ShowAddButton(this TabViewElement el, bool visible = true) =>
        el with { IsAddTabButtonVisible = visible };

    /// <summary>Controls how tab widths are sized (Equal, SizeToContent, Compact).</summary>
    public static TabViewElement TabWidthMode(this TabViewElement el, TabViewWidthMode mode) =>
        el with { TabWidthMode = mode };

    /// <summary>Controls when the per-tab close button is visible (Auto, OnPointerOver, Always).</summary>
    public static TabViewElement CloseButtonOverlayMode(this TabViewElement el, TabViewCloseButtonOverlayMode mode) =>
        el with { CloseButtonOverlayMode = mode };

    /// <summary>Whether tabs can be dragged out (e.g. to detach into a new window).</summary>
    public static TabViewElement CanDragTabs(this TabViewElement el, bool canDrag = true) =>
        el with { CanDragTabs = canDrag };

    /// <summary>Whether tabs can be reordered within the strip via drag.</summary>
    public static TabViewElement CanReorderTabs(this TabViewElement el, bool canReorder = true) =>
        el with { CanReorderTabs = canReorder };

    /// <summary>Whether tabs from another TabView can be dropped onto this one.</summary>
    public static TabViewElement AllowDropTabs(this TabViewElement el, bool allow = true) =>
        el with { AllowDropTabs = allow };

    /// <summary>Sets the element rendered at the leading edge of the tab strip.</summary>
    public static TabViewElement TabStripHeader(this TabViewElement el, Element header) =>
        el with { TabStripHeader = header };

    /// <summary>Sets the element rendered at the trailing edge of the tab strip.</summary>
    public static TabViewElement TabStripFooter(this TabViewElement el, Element footer) =>
        el with { TabStripFooter = footer };

    // ── Key ─────────────────────────────────────────────────────────

    public static T WithKey<T>(this T el, string key) where T : Element =>
        el with { Key = key };

    /// <summary>
    /// Assigns the stable identity from an <see cref="IReactorKeyed"/> data
    /// item directly to a hand-built element — equivalent to
    /// <c>el.WithKey(item.Key)</c> but lets call sites avoid hoisting the
    /// item to read <c>.Key</c>. (spec 042 §5)
    /// </summary>
    /// <remarks>
    /// Typical use: <c>FlexColumn(items.Select(item =&gt; TextBlock(item.Name).WithKey(item)))</c>.
    /// The <c>TKey</c> parameter is independent of the element type so the
    /// usual fluent inference (return the element type) is preserved.
    /// </remarks>
    public static T WithKey<T, TKey>(this T el, TKey item)
        where T : Element
        where TKey : IReactorKeyed
    {
        ArgumentNullException.ThrowIfNull(item);
        return el with { Key = item.Key };
    }

    // ════════════════════════════════════════════════════════════════
    //  Set() — strongly-typed native property access per element type
    //
    //  Usage:  Button("Go", onClick).Set(b => b.FlowDirection = FlowDirection.RightToLeft)
    //
    //  The lambda parameter is the actual WinUI control type, giving you
    //  full IntelliSense and compile-time type checking for every property.
    //  Setters are applied at both mount and update (idempotent property sets).
    // ════════════════════════════════════════════════════════════════

    // Text
    public static TextBlockElement Set(this TextBlockElement el, Action<WinUI.TextBlock> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static RichTextBlockElement Set(this RichTextBlockElement el, Action<WinUI.RichTextBlock> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static RichEditBoxElement Set(this RichEditBoxElement el, Action<WinUI.RichEditBox> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Buttons
    public static ButtonElement Set(this ButtonElement el, Action<WinUI.Button> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static HyperlinkButtonElement Set(this HyperlinkButtonElement el, Action<WinUI.HyperlinkButton> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static RepeatButtonElement Set(this RepeatButtonElement el, Action<WinPrim.RepeatButton> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static ToggleButtonElement Set(this ToggleButtonElement el, Action<WinPrim.ToggleButton> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static DropDownButtonElement Set(this DropDownButtonElement el, Action<WinUI.DropDownButton> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static SplitButtonElement Set(this SplitButtonElement el, Action<WinUI.SplitButton> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static ToggleSplitButtonElement Set(this ToggleSplitButtonElement el, Action<WinUI.ToggleSplitButton> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Input
    public static TextFieldElement Set(this TextFieldElement el, Action<WinUI.TextBox> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static PasswordBoxElement Set(this PasswordBoxElement el, Action<WinUI.PasswordBox> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static NumberBoxElement Set(this NumberBoxElement el, Action<WinUI.NumberBox> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static AutoSuggestBoxElement Set(this AutoSuggestBoxElement el, Action<WinUI.AutoSuggestBox> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static CheckBoxElement Set(this CheckBoxElement el, Action<WinUI.CheckBox> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static RadioButtonElement Set(this RadioButtonElement el, Action<WinUI.RadioButton> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static RadioButtonsElement Set(this RadioButtonsElement el, Action<WinUI.RadioButtons> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static ComboBoxElement Set(this ComboBoxElement el, Action<WinUI.ComboBox> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static SliderElement Set(this SliderElement el, Action<WinUI.Slider> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static ToggleSwitchElement Set(this ToggleSwitchElement el, Action<WinUI.ToggleSwitch> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static RatingControlElement Set(this RatingControlElement el, Action<WinUI.RatingControl> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static ColorPickerElement Set(this ColorPickerElement el, Action<WinUI.ColorPicker> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Date/Time
    public static CalendarDatePickerElement Set(this CalendarDatePickerElement el, Action<WinUI.CalendarDatePicker> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static DatePickerElement Set(this DatePickerElement el, Action<WinUI.DatePicker> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static TimePickerElement Set(this TimePickerElement el, Action<WinUI.TimePicker> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Progress
    public static ProgressElement Set(this ProgressElement el, Action<WinUI.ProgressBar> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static ProgressRingElement Set(this ProgressRingElement el, Action<WinUI.ProgressRing> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Media
    public static ImageElement Set(this ImageElement el, Action<WinUI.Image> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static PersonPictureElement Set(this PersonPictureElement el, Action<WinUI.PersonPicture> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static WebView2Element Set(this WebView2Element el, Action<WinUI.WebView2> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Layout / Containers
    public static FlexElement Set(this FlexElement el, Action<Layout.FlexPanel> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static WrapGridElement Set(this WrapGridElement el, Action<WinUI.VariableSizedWrapGrid> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static StackElement Set(this StackElement el, Action<WinUI.StackPanel> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static GridElement Set(this GridElement el, Action<WinUI.Grid> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static ScrollViewElement Set(this ScrollViewElement el, Action<WinUI.ScrollViewer> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static BorderElement Set(this BorderElement el, Action<WinUI.Border> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static ExpanderElement Set(this ExpanderElement el, Action<WinUI.Expander> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static SplitViewElement Set(this SplitViewElement el, Action<WinUI.SplitView> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static ViewboxElement Set(this ViewboxElement el, Action<WinUI.Viewbox> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static ViewboxElement Stretch(this ViewboxElement el, Stretch stretch) =>
        el with { Stretch = stretch };

    public static ViewboxElement StretchDirection(this ViewboxElement el, StretchDirection direction) =>
        el with { StretchDirection = direction };

    public static CanvasElement Set(this CanvasElement el, Action<WinUI.Canvas> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Navigation
    public static NavigationViewElement Set(this NavigationViewElement el, Action<WinUI.NavigationView> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static TabViewElement Set(this TabViewElement el, Action<WinUI.TabView> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static BreadcrumbBarElement Set(this BreadcrumbBarElement el, Action<WinUI.BreadcrumbBar> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static PivotElement Set(this PivotElement el, Action<WinUI.Pivot> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Collections
    public static ListViewElement Set(this ListViewElement el, Action<WinUI.ListView> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static GridViewElement Set(this GridViewElement el, Action<WinUI.GridView> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static TreeViewElement Set(this TreeViewElement el, Action<WinUI.TreeView> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static FlipViewElement Set(this FlipViewElement el, Action<WinUI.FlipView> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Dialogs / Overlays
    public static ContentDialogElement Set(this ContentDialogElement el, Action<WinUI.ContentDialog> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static FlyoutElement Set(this FlyoutElement el, Action<WinUI.Flyout> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static TeachingTipElement Set(this TeachingTipElement el, Action<WinUI.TeachingTip> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static InfoBarElement Set(this InfoBarElement el, Action<WinUI.InfoBar> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static InfoBadgeElement Set(this InfoBadgeElement el, Action<WinUI.InfoBadge> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Menus
    public static MenuBarElement Set(this MenuBarElement el, Action<WinUI.MenuBar> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static CommandBarElement Set(this CommandBarElement el, Action<WinUI.CommandBar> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static MenuFlyoutElement Set(this MenuFlyoutElement el, Action<WinUI.MenuFlyout> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Shapes
    public static RectangleElement Set(this RectangleElement el, Action<WinShapes.Rectangle> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static EllipseElement Set(this EllipseElement el, Action<WinShapes.Ellipse> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static LineElement Set(this LineElement el, Action<WinShapes.Line> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static PathElement Set(this PathElement el, Action<WinShapes.Path> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Additional layout
    public static RelativePanelElement Set(this RelativePanelElement el, Action<WinUI.RelativePanel> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Additional media
    public static MediaPlayerElementElement Set(this MediaPlayerElementElement el, Action<WinUI.MediaPlayerElement> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static AnimatedVisualPlayerElement Set(this AnimatedVisualPlayerElement el, Action<WinUI.AnimatedVisualPlayer> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Additional collections
    public static SemanticZoomElement Set(this SemanticZoomElement el, Action<WinUI.SemanticZoom> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static ListBoxElement Set(this ListBoxElement el, Action<WinUI.ListBox> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Additional navigation
    public static SelectorBarElement Set(this SelectorBarElement el, Action<WinUI.SelectorBar> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static PipsPagerElement Set(this PipsPagerElement el, Action<WinUI.PipsPager> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static AnnotatedScrollBarElement Set(this AnnotatedScrollBarElement el, Action<WinUI.AnnotatedScrollBar> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Additional overlays / containers
    public static PopupElement Set(this PopupElement el, Action<WinPrim.Popup> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static RefreshContainerElement Set(this RefreshContainerElement el, Action<WinUI.RefreshContainer> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static CommandBarFlyoutElement Set(this CommandBarFlyoutElement el, Action<WinUI.CommandBarFlyout> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Additional date/time
    public static CalendarViewElement Set(this CalendarViewElement el, Action<WinUI.CalendarView> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // SwipeControl
    public static SwipeControlElement Set(this SwipeControlElement el, Action<WinUI.SwipeControl> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // AnimatedIcon
    public static AnimatedIconElement Set(this AnimatedIconElement el, Action<WinUI.AnimatedIcon> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // ParallaxView
    public static ParallaxViewElement Set(this ParallaxViewElement el, Action<WinUI.ParallaxView> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // MapControl
    public static MapControlElement Set(this MapControlElement el, Action<WinUI.MapControl> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Frame
    public static FrameElement Set(this FrameElement el, Action<WinUI.Frame> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // ItemsView
    public static ItemsViewElement<T> Set<T>(this ItemsViewElement<T> el, Action<WinUI.ItemsView> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // Typed templated collections
    public static TemplatedListViewElement<T> Set<T>(this TemplatedListViewElement<T> el, Action<WinUI.ListView> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static TemplatedGridViewElement<T> Set<T>(this TemplatedGridViewElement<T> el, Action<WinUI.GridView> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    public static TemplatedFlipViewElement<T> Set<T>(this TemplatedFlipViewElement<T> el, Action<WinUI.FlipView> configure) =>
        el with { Setters = [.. el.Setters, configure] };

    // ── Shape convenience modifiers ─────────────────────────────────

    public static RectangleElement Fill(this RectangleElement el, Brush brush) =>
        el with { Fill = brush };

    public static EllipseElement Fill(this EllipseElement el, Brush brush) =>
        el with { Fill = brush };

    public static PathElement Fill(this PathElement el, Brush brush) =>
        el with { Fill = brush };

    public static LineElement Stroke(this LineElement el, Brush brush) =>
        el with { Stroke = brush };

    public static LineElement StrokeThickness(this LineElement el, double thickness) =>
        el with { StrokeThickness = thickness };

    public static PathElement Stroke(this PathElement el, Brush brush) =>
        el with { Stroke = brush };

    public static PathElement StrokeThickness(this PathElement el, double thickness) =>
        el with { StrokeThickness = thickness };

    // ── Popup convenience modifiers ─────────────────────────────────

    public static PopupElement LightDismiss(this PopupElement el, bool enabled = true) =>
        el with { IsLightDismissEnabled = enabled };

    public static PopupElement Offset(this PopupElement el, double horizontal, double vertical) =>
        el with { HorizontalOffset = horizontal, VerticalOffset = vertical };

    // Virtualized collections (LazyVStack / LazyHStack)
    // .Set() targets the outer ScrollViewer; .SetRepeater() targets the inner ItemsRepeater
    public static LazyVStackElement<T> Set<T>(this LazyVStackElement<T> el, Action<WinUI.ScrollViewer> configure) =>
        el with { ScrollViewerSetters = [.. el.ScrollViewerSetters, configure] };

    public static LazyVStackElement<T> SetRepeater<T>(this LazyVStackElement<T> el, Action<WinUI.ItemsRepeater> configure) =>
        el with { RepeaterSetters = [.. el.RepeaterSetters, configure] };

    public static LazyHStackElement<T> Set<T>(this LazyHStackElement<T> el, Action<WinUI.ScrollViewer> configure) =>
        el with { ScrollViewerSetters = [.. el.ScrollViewerSetters, configure] };

    public static LazyHStackElement<T> SetRepeater<T>(this LazyHStackElement<T> el, Action<WinUI.ItemsRepeater> configure) =>
        el with { RepeaterSetters = [.. el.RepeaterSetters, configure] };

    // ════════════════════════════════════════════════════════════════
    //  Transitions (first-class, applied by reconciler)
    // ════════════════════════════════════════════════════════════════

    // ── Theme transitions (ChildrenTransitions / ItemContainerTransitions) ──

    /// <summary>
    /// Sets theme transitions declaratively. The reconciler applies ChildrenTransitions
    /// on panels, ChildTransitions on borders, ContentTransitions on content controls.
    /// Works on any element type.
    /// </summary>
    public static T WithTransitions<T>(this T el, params Transition[] transitions) where T : Element =>
        el with { ThemeTransitions = (el.ThemeTransitions ?? new()) with { Children = transitions } };

    /// <summary>
    /// Sets ItemContainerTransitions declaratively on ListView, GridView, etc.
    /// </summary>
    public static T ItemContainerTransitions<T>(this T el, params Transition[] transitions) where T : Element =>
        el with { ThemeTransitions = (el.ThemeTransitions ?? new()) with { ItemContainer = transitions } };

    // ── Implicit transitions (Opacity, Rotation, Scale, Translation, Background) ──

    /// <summary>
    /// Adds an implicit ScalarTransition on Opacity.
    /// Applied by the reconciler after .Set() callbacks — always safe to combine.
    /// </summary>
    public static T OpacityTransition<T>(this T el, TimeSpan? duration = null) where T : Element
    {
        var t = new ScalarTransition();
        if (duration.HasValue) t.Duration = duration.Value;
        return el with { ImplicitTransitions = (el.ImplicitTransitions ?? new()) with { Opacity = t } };
    }

    /// <summary>
    /// Adds an implicit ScalarTransition on Rotation.
    /// </summary>
    public static T RotationTransition<T>(this T el, TimeSpan? duration = null) where T : Element
    {
        var t = new ScalarTransition();
        if (duration.HasValue) t.Duration = duration.Value;
        return el with { ImplicitTransitions = (el.ImplicitTransitions ?? new()) with { Rotation = t } };
    }

    /// <summary>
    /// Adds an implicit Vector3Transition on Scale.
    /// Pass a pre-configured transition to set Components for axis-specific animation.
    /// </summary>
    public static T ScaleTransition<T>(this T el, Vector3Transition? transition = null) where T : Element =>
        el with { ImplicitTransitions = (el.ImplicitTransitions ?? new()) with { Scale = transition ?? new Vector3Transition() } };

    /// <summary>
    /// Adds an implicit Vector3Transition on Translation.
    /// Pass a pre-configured transition to set Components for axis-specific animation.
    /// </summary>
    public static T TranslationTransition<T>(this T el, Vector3Transition? transition = null) where T : Element =>
        el with { ImplicitTransitions = (el.ImplicitTransitions ?? new()) with { Translation = transition ?? new Vector3Transition() } };

    /// <summary>
    /// Adds an implicit BrushTransition on Background.
    /// Only available on Grid and Stack (VStack/HStack) — WinUI only supports
    /// BackgroundTransition on Grid, StackPanel, and ContentPresenter.
    /// </summary>
    public static GridElement BackgroundTransition(this GridElement el, TimeSpan? duration = null)
    {
        var t = new BrushTransition();
        if (duration.HasValue) t.Duration = duration.Value;
        return el with { ImplicitTransitions = (el.ImplicitTransitions ?? new()) with { Background = t } };
    }

    /// <inheritdoc cref="BackgroundTransition(GridElement, TimeSpan?)"/>
    public static StackElement BackgroundTransition(this StackElement el, TimeSpan? duration = null)
    {
        var t = new BrushTransition();
        if (duration.HasValue) t.Duration = duration.Value;
        return el with { ImplicitTransitions = (el.ImplicitTransitions ?? new()) with { Background = t } };
    }

    // ════════════════════════════════════════════════════════════════
    //  Layout animations (Composition-layer implicit animations on Offset/Size)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Enables smooth layout animation: when WinUI repositions this element
    /// (e.g., list reorder, grid reflow), it animates from the old position to the
    /// new position via the Composition layer. Default duration: 300ms.
    /// Elements should have stable keys (.WithKey()) for the reconciler to match
    /// them across reorders.
    /// </summary>
    public static T LayoutAnimation<T>(this T el) where T : Element =>
        el with { LayoutAnimation = new LayoutAnimationConfig() };

    /// <summary>
    /// Enables layout animation with a custom duration.
    /// </summary>
    public static T LayoutAnimation<T>(this T el, TimeSpan duration) where T : Element =>
        el with { LayoutAnimation = new LayoutAnimationConfig { Duration = duration } };

    /// <summary>
    /// Enables layout animation with spring physics for a natural, bouncy feel.
    /// </summary>
    public static T SpringLayoutAnimation<T>(this T el,
        float dampingRatio = 0.6f, float period = 0.08f) where T : Element =>
        el with { LayoutAnimation = new LayoutAnimationConfig
        {
            UseSpring = true,
            DampingRatio = dampingRatio,
            Period = period
        } };

    /// <summary>
    /// Enables layout animation with a fully custom configuration.
    /// </summary>
    public static T LayoutAnimation<T>(this T el, LayoutAnimationConfig config) where T : Element =>
        el with { LayoutAnimation = config };

    // ════════════════════════════════════════════════════════════════
    //  Connected animations (cross-container transitions)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Declares this element as a participant in a connected animation.
    /// When the element is unmounted (e.g., parent container changes type),
    /// the reconciler captures its visual snapshot. When a new element with the
    /// same key is mounted, the snapshot animates to the new element's position.
    /// Both source and destination must use the same key string.
    /// </summary>
    public static T ConnectedAnimation<T>(this T el, string key) where T : Element =>
        el with { ConnectedAnimationKey = key };

    // ════════════════════════════════════════════════════════════════
    //  ScrollView zoom/scroll modifiers
    // ════════════════════════════════════════════════════════════════

    public static ScrollViewElement ZoomMode(this ScrollViewElement el, WinUI.ZoomMode mode) =>
        el with { ZoomMode = mode };

    public static ScrollViewElement HorizontalScrollMode(this ScrollViewElement el, WinUI.ScrollMode mode) =>
        el with { HorizontalScrollMode = mode };

    public static ScrollViewElement VerticalScrollMode(this ScrollViewElement el, WinUI.ScrollMode mode) =>
        el with { VerticalScrollMode = mode };

    // ════════════════════════════════════════════════════════════════
    //  AutomationProperties / ElementSoundMode / OnMount
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets AutomationProperties.Name on the element's control.
    /// Usage: Button("Go", onClick).AutomationName("Navigate forward")
    /// </summary>
    public static T AutomationName<T>(this T el, string name) where T : Element =>
        Modify(el, new ElementModifiers { AutomationName = name });

    /// <summary>
    /// Sets AutomationProperties.AutomationId on the element's control.
    /// Provides a stable identifier for UI Automation / test tools (FlaUI, WinAppDriver).
    /// Usage: Button("Go", onClick).AutomationId("GoButton")
    /// </summary>
    public static T AutomationId<T>(this T el, string id) where T : Element =>
        Modify(el, new ElementModifiers { AutomationId = id });

    /// <summary>
    /// Sets ElementSoundMode on the element's control.
    /// Usage: Button("Play", onClick).SoundMode(ElementSoundMode.Off)
    /// </summary>
    public static T SoundMode<T>(this T el, ElementSoundMode mode) where T : Element =>
        Modify(el, new ElementModifiers { ElementSoundMode = mode });

    /// <summary>
    /// Runs an action once when the element is first mounted (not on re-renders).
    /// Use this instead of .Set() when attaching event handlers to avoid accumulation.
    /// Usage: Button("Go", null).OnMount(fe => { ((Button)fe).Click += ...; })
    /// </summary>
    public static T OnMount<T>(this T el, Action<FrameworkElement> action) where T : Element =>
        Modify(el, new ElementModifiers { OnMountAction = action });

    /// <summary>
    /// Like <see cref="OnMount"/>, but composes with any mount action already on the
    /// element instead of overwriting it. Useful for framework code that wants to
    /// apply defensive defaults (e.g. <c>IsHitTestVisible = false</c>) to a
    /// caller-supplied <see cref="Element"/> without silently dropping the caller's
    /// own mount-time wiring. The pre-existing action runs first, then the new one.
    /// </summary>
    public static T OnMountAdd<T>(this T el, Action<FrameworkElement> action) where T : Element
    {
        var existing = el.Modifiers?.OnMountAction;
        Action<FrameworkElement> combined = existing is null
            ? action
            : fe => { existing(fe); action(fe); };
        return Modify(el, new ElementModifiers { OnMountAction = combined });
    }

    // ════════════════════════════════════════════════════════════════
    //  Accessibility — Tier 1 (inline on ElementModifiers)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets AutomationProperties.HeadingLevel (Level1–Level9).
    /// Screen reader users navigate by headings, like HTML h1–h6.
    /// </summary>
    /// <example>Text("Settings").HeadingLevel(AutomationHeadingLevel.Level1)</example>
    public static T HeadingLevel<T>(this T el, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel level) where T : Element =>
        Modify(el, new ElementModifiers { HeadingLevel = level });

    /// <summary>
    /// Sets UIElement.IsTabStop — whether the element participates in Tab navigation.
    /// Works on any element type (Panel, Control, etc.) in WinUI 3.
    /// </summary>
    /// <example>Border(content).IsTabStop(false)</example>
    public static T IsTabStop<T>(this T el, bool isTabStop = true) where T : Element =>
        Modify(el, new ElementModifiers { IsTabStop = isTabStop });

    /// <summary>
    /// Sets Control.TabIndex — Tab order position. Lower values receive focus first.
    /// </summary>
    /// <example>Button("Submit").TabIndex(1)</example>
    public static T TabIndex<T>(this T el, int index) where T : Element =>
        Modify(el, new ElementModifiers { TabIndex = index });

    /// <summary>
    /// Sets UIElement.AccessKey — the Alt+Key shortcut (underlined hint shown on Alt press).
    /// When used on a button bound to a <see cref="Command"/>, this per-site access key
    /// overrides <see cref="Command.AccessKey"/> (per-site override always wins).
    /// </summary>
    /// <example>Button("File", onClick).AccessKey("F")</example>
    public static T AccessKey<T>(this T el, string key) where T : Element =>
        Modify(el, new ElementModifiers { AccessKey = key });

    /// <summary>
    /// Sets UIElement.XYFocusKeyboardNavigation — enables directional (Xbox-style)
    /// focus navigation with arrow keys or gamepad DPad.
    /// </summary>
    /// <example>Grid(tiles).XYFocusKeyboardNavigation(XYFocusKeyboardNavigationMode.Enabled)</example>
    public static T XYFocusKeyboardNavigation<T>(this T el, Microsoft.UI.Xaml.Input.XYFocusKeyboardNavigationMode mode) where T : Element =>
        Modify(el, new ElementModifiers { XYFocusKeyboardNavigation = mode });

    /// <summary>
    /// Handler for UIElement.AccessKeyDisplayRequested — fires when the access-key
    /// bubble should appear (e.g., user pressed Alt). Use to customize the visual.
    /// </summary>
    public static T AccessKeyDisplayRequested<T>(this T el, Action handler) where T : Element =>
        Modify(el, new ElementModifiers { OnAccessKeyDisplayRequested = (_, _) => handler() });

    /// <summary>
    /// Handler for UIElement.AccessKeyDisplayRequested with full event args.
    /// </summary>
    public static T AccessKeyDisplayRequested<T>(this T el, Action<UIElement, Microsoft.UI.Xaml.Input.AccessKeyDisplayRequestedEventArgs> handler) where T : Element =>
        Modify(el, new ElementModifiers { OnAccessKeyDisplayRequested = handler });

    /// <summary>
    /// Binds this element to an imperative <see cref="Microsoft.UI.Reactor.Input.ElementRef"/>.
    /// Obtain the ref from <c>ctx.UseElementFocus()</c> (or construct one manually) and use
    /// <see cref="Microsoft.UI.Reactor.Input.FocusManager.Focus"/> to imperatively focus the
    /// referenced element after mount.
    /// </summary>
    /// <example>
    /// var (inputRef, requestFocus) = ctx.UseElementFocus();
    /// return TextField(value, setValue).Ref(inputRef);
    /// </example>
    public static T Ref<T>(this T el, Microsoft.UI.Reactor.Input.ElementRef target) where T : Element =>
        Modify(el, new ElementModifiers { Ref = target });

    /// <summary>
    /// Strongly-typed overload of <see cref="Ref{T}(T, Microsoft.UI.Reactor.Input.ElementRef)"/>.
    /// Accepts a typed <see cref="Microsoft.UI.Reactor.Input.ElementRef{TElement}"/> and forwards
    /// to the untyped form via the implicit conversion.
    /// </summary>
    /// <remarks>
    /// Spec 033 §3. Behaviorally identical to passing the inner untyped ref; the typed
    /// overload exists so callers see the typed surface in IntelliSense and so the
    /// reconciler's DEBUG mismatch-assert can fire on a wrong-type binding.
    /// </remarks>
    public static T Ref<T, TElement>(this T el, Microsoft.UI.Reactor.Input.ElementRef<TElement> target)
        where T : Element
        where TElement : Microsoft.UI.Xaml.FrameworkElement =>
        Modify(el, new ElementModifiers { Ref = target });

    // ════════════════════════════════════════════════════════════════
    //  Accessibility — Tier 2/3 (lazy AccessibilityModifiers sub-record)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets AutomationProperties.HelpText — supplemental text read by screen readers
    /// after the Name. Analogous to SwiftUI's .accessibilityHint().
    /// </summary>
    /// <example>TextField(email, setEmail).HelpText("Enter your work email address")</example>
    public static T HelpText<T>(this T el, string text) where T : Element =>
        ModifyA11y(el, new AccessibilityModifiers { HelpText = text });

    /// <summary>
    /// Sets AutomationProperties.FullDescription — extended description for complex elements.
    /// </summary>
    /// <example>Chart(...).FullDescription("Bar chart showing Q1 revenue by region")</example>
    public static T FullDescription<T>(this T el, string desc) where T : Element =>
        ModifyA11y(el, new AccessibilityModifiers { FullDescription = desc });

    /// <summary>
    /// Sets AutomationProperties.LandmarkType (Main, Navigation, Search, Form, Custom).
    /// Screen readers announce landmarks and let users jump between them.
    /// </summary>
    /// <example>VStack(children).Landmark(AutomationLandmarkType.Main)</example>
    public static T Landmark<T>(this T el, Microsoft.UI.Xaml.Automation.Peers.AutomationLandmarkType type) where T : Element =>
        ModifyA11y(el, new AccessibilityModifiers { LandmarkType = type });

    /// <summary>
    /// Sets AutomationProperties.AccessibilityView (Content, Control, Raw).
    /// Use Raw to hide decorative elements from screen readers.
    /// </summary>
    /// <example>Image(decorativeUri).AccessibilityView(AccessibilityView.Raw)</example>
    public static T AccessibilityView<T>(this T el, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView view) where T : Element =>
        ModifyA11y(el, new AccessibilityModifiers { AccessibilityView = view });

    /// <summary>
    /// Hides element from screen readers entirely.
    /// Shorthand for .AccessibilityView(AccessibilityView.Raw).
    /// </summary>
    /// <example>Icon(decorativeGlyph).AccessibilityHidden()</example>
    public static T AccessibilityHidden<T>(this T el) where T : Element =>
        ModifyA11y(el, new AccessibilityModifiers { AccessibilityView = Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw });

    /// <summary>
    /// Sets AutomationProperties.IsRequiredForForm. Screen readers announce "required".
    /// </summary>
    /// <example>TextField(name, setName).Required()</example>
    public static T Required<T>(this T el) where T : Element =>
        ModifyA11y(el, new AccessibilityModifiers { IsRequiredForForm = true });

    /// <summary>
    /// Sets AutomationProperties.LiveSetting. Screen readers announce content changes.
    /// Polite = queued after current speech. Assertive = interrupts immediately.
    /// </summary>
    /// <example>Text(statusMessage).LiveRegion(AutomationLiveSetting.Polite)</example>
    public static T LiveRegion<T>(this T el, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting mode = Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite) where T : Element =>
        ModifyA11y(el, new AccessibilityModifiers { LiveSetting = mode });

    /// <summary>
    /// Sets AutomationProperties.PositionInSet and SizeOfSet (e.g., "item 3 of 10").
    /// </summary>
    /// <example>ListItem(text).PositionInSet(3, 10)</example>
    public static T PositionInSet<T>(this T el, int position, int size) where T : Element =>
        ModifyA11y(el, new AccessibilityModifiers { PositionInSet = position, SizeOfSet = size });

    /// <summary>
    /// Sets AutomationProperties.Level — hierarchical depth (e.g., tree node depth).
    /// </summary>
    /// <example>TreeItem(text).HierarchyLevel(2)</example>
    public static T HierarchyLevel<T>(this T el, int level) where T : Element =>
        ModifyA11y(el, new AccessibilityModifiers { Level = level });

    /// <summary>
    /// Sets AutomationProperties.ItemStatus — status string announced by screen readers.
    /// </summary>
    /// <example>MailFolder("Inbox").ItemStatus("3 unread")</example>
    public static T ItemStatus<T>(this T el, string status) where T : Element =>
        ModifyA11y(el, new AccessibilityModifiers { ItemStatus = status });

    /// <summary>
    /// Associates this element with a labelling element via its AutomationId.
    /// The reconciler resolves the reference at mount time.
    /// </summary>
    /// <example>TextField(email, setEmail).LabeledBy("EmailLabel")</example>
    public static T LabeledBy<T>(this T el, string labelAutomationId) where T : Element =>
        ModifyA11y(el, new AccessibilityModifiers { LabeledBy = labelAutomationId });

    /// <summary>
    /// Sets UIElement.TabFocusNavigation — how Tab navigates within a container.
    /// Local = cycle within container. Once = enter once then leave. Cycle = loop forever.
    /// </summary>
    /// <example>ToolBar(buttons).TabNavigation(KeyboardNavigationMode.Once)</example>
    public static T TabNavigation<T>(this T el, Microsoft.UI.Xaml.Input.KeyboardNavigationMode mode) where T : Element =>
        ModifyA11y(el, new AccessibilityModifiers { TabFocusNavigation = mode });

    // ════════════════════════════════════════════════════════════════
    //  Composite component semantics
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Wraps this element in a SemanticPanel that provides custom automation
    /// semantics to screen readers. Use this for composite components that need
    /// to describe their role and value (e.g., a star rating widget built from
    /// Image elements that should announce as "slider, 3 of 5 stars").
    ///
    /// Analogous to SwiftUI's .accessibilityRepresentation {} and Compose's
    /// Modifier.semantics { role = Role.Slider }.
    /// </summary>
    /// <example>
    /// StarRating(value: 3, max: 5)
    ///     .Semantics(role: "slider", value: "3 of 5 stars",
    ///                rangeValue: 3, rangeMin: 0, rangeMax: 5)
    /// </example>
    public static SemanticElement Semantics<T>(this T el,
        string? role = null,
        string? value = null,
        double? rangeMin = null,
        double? rangeMax = null,
        double? rangeValue = null,
        bool isReadOnly = true) where T : Element
    {
        return new SemanticElement(el, new SemanticDescription(role, value, rangeMin, rangeMax, rangeValue, isReadOnly));
    }

    // ════════════════════════════════════════════════════════════════
    //  ThemeShadow / Translation modifiers
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets the Translation property (Vector3) on the element's control.
    /// Commonly used with ThemeShadow for z-depth effects.
    /// Routes through AnimationHelper so WithAnimation scopes animate the change.
    /// </summary>
    public static T Translation<T>(this T el, float x, float y, float z) where T : Element =>
        Modify(el, new ElementModifiers { Translation = new global::System.Numerics.Vector3(x, y, z) });

    // ════════════════════════════════════════════════════════════════
    //  Compositor property animation (.Animate() modifier)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Enables implicit compositor animation on visual property changes.
    /// All visual property changes (Opacity, Scale, Rotation, Translation, CenterPoint)
    /// will animate using the specified curve.
    /// </summary>
    public static T Animate<T>(this T el, Microsoft.UI.Reactor.Animation.Curve curve,
        Microsoft.UI.Reactor.Animation.AnimateProperty properties = Microsoft.UI.Reactor.Animation.AnimateProperty.All) where T : Element =>
        el with { AnimationConfig = new Microsoft.UI.Reactor.Animation.AnimationConfig(curve, properties) };

    // ════════════════════════════════════════════════════════════════
    //  Enter/exit transitions (.Transition() modifier)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Enables enter/exit transitions when this element is conditionally rendered.
    /// Enter: animates from initial state to visible. Exit: animates out before unmount.
    /// </summary>
    public static T Transition<T>(this T el, Microsoft.UI.Reactor.Animation.Transition transition,
        Microsoft.UI.Reactor.Animation.Curve? curve = null) where T : Element =>
        el with { ElementTransition = new Microsoft.UI.Reactor.Animation.ElementTransition(transition, curve) };

    // ════════════════════════════════════════════════════════════════
    //  Interaction states (.InteractionStates() modifier)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Declares zero-reconcile interaction state visual changes (hover, pressed, focused).
    /// The reconciler registers pointer event handlers that drive compositor animations
    /// and direct brush swaps — no state variables or re-renders needed.
    /// </summary>
    public static T InteractionStates<T>(this T el,
        Func<Microsoft.UI.Reactor.Animation.InteractionStatesBuilder, Microsoft.UI.Reactor.Animation.InteractionStatesBuilder> configure,
        Microsoft.UI.Reactor.Animation.Curve? curve = null) where T : Element
    {
        var builder = configure(new Microsoft.UI.Reactor.Animation.InteractionStatesBuilder());
        var config = builder.Build();
        if (curve is not null)
            config = config with { Curve = curve };
        return el with { InteractionStates = config };
    }

    // ════════════════════════════════════════════════════════════════
    //  Staggered children animation (.Stagger() modifier)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Adds incrementing animation delays to children in a container.
    /// Child N gets N * delay applied to its compositor animations.
    /// </summary>
    public static T Stagger<T>(this T el, TimeSpan delay,
        Microsoft.UI.Reactor.Animation.Curve? curve = null) where T : Element =>
        el with { StaggerConfig = new Microsoft.UI.Reactor.Animation.StaggerConfig(delay, curve) };

    // ════════════════════════════════════════════════════════════════
    //  Keyframe animation (.Keyframes() modifier)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Attaches a trigger-based keyframe animation. The animation plays when
    /// the trigger value changes between renders.
    /// </summary>
    public static T Keyframes<T>(this T el, string name, object? trigger,
        Func<Microsoft.UI.Reactor.Animation.KeyframeBuilder, Microsoft.UI.Reactor.Animation.KeyframeBuilder> configure) where T : Element
    {
        var builder = configure(new Microsoft.UI.Reactor.Animation.KeyframeBuilder());
        var def = builder.Build();
        var entry = new Microsoft.UI.Reactor.Animation.KeyframeEntry(name, trigger, def);

        var existing = el.KeyframeAnimations;
        var entries = existing is not null
            ? [.. existing, entry]
            : new[] { entry };
        return el with { KeyframeAnimations = entries };
    }

    // ════════════════════════════════════════════════════════════════
    //  Scroll-linked expression animation (.ScrollLinked() modifier)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Attaches scroll-linked expression animations driven by a ScrollViewer.
    /// Expressions run on the compositor at display refresh rate with zero managed code.
    /// </summary>
    public static T ScrollLinked<T>(this T el,
        Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer,
        Func<Microsoft.UI.Reactor.Animation.ScrollAnimationBuilder, Microsoft.UI.Reactor.Animation.ScrollAnimationBuilder> configure) where T : Element
    {
        var builder = configure(new Microsoft.UI.Reactor.Animation.ScrollAnimationBuilder());
        var expressions = builder.Build();
        return el with { ScrollAnimation = new Microsoft.UI.Reactor.Animation.ScrollAnimationConfig(scrollViewer, expressions) };
    }

    // ════════════════════════════════════════════════════════════════
    //  Internal
    // ════════════════════════════════════════════════════════════════

    // <snippet:modifier-chain>
    private static T Modify<T>(T el, ElementModifiers mods) where T : Element =>
        el with { Modifiers = el.Modifiers is not null ? el.Modifiers.Merge(mods) : mods };

    private static T ModifyA11y<T>(T el, AccessibilityModifiers a11y) where T : Element
    {
        var existing = el.Modifiers?.Accessibility;
        var merged = existing is not null ? existing.Merge(a11y) : a11y;
        return Modify(el, new ElementModifiers { Accessibility = merged });
    }

    private static T ModifyTheme<T>(T el, string property, ThemeRef theme) where T : Element
    {
        var bindings = el.ThemeBindings is not null
            ? new Dictionary<string, ThemeRef>(el.ThemeBindings) { [property] = theme }
            : new Dictionary<string, ThemeRef> { [property] = theme };
        return el with { ThemeBindings = bindings };
    }
    // </snippet:modifier-chain>
}
