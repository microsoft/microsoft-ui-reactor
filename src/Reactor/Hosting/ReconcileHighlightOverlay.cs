using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Draws diagonal-striped overlay rectangles over UIElements that were mounted (red, 45°)
/// or modified (yellow, 135°) during a reconcile pass. Uses the Composition visual layer
/// to avoid creating XAML elements (which would themselves show up as reconcile churn).
/// Each overlay fades out over <see cref="FadeDurationMs"/> milliseconds.
/// </summary>
internal sealed class ReconcileHighlightOverlay
{
    private const float MountedOpacity = 0.45f;
    private const float ModifiedOpacity = 0.35f;
    private const int FadeDurationMs = 600;
    private const float StripeWidth = 5f;

    private static readonly global::Windows.UI.Color MountedColor =
        global::Windows.UI.Color.FromArgb(255, 220, 40, 40);   // red at 45°
    private static readonly global::Windows.UI.Color ModifiedColor =
        global::Windows.UI.Color.FromArgb(255, 240, 200, 20);  // yellow at 135°

    private readonly Canvas _overlayCanvas;
    private ContainerVisual? _container;
    private Compositor? _compositor;
    private CompositionBrush? _mountedBrush;
    private CompositionBrush? _modifiedBrush;

    public ReconcileHighlightOverlay(Canvas overlayCanvas)
    {
        _overlayCanvas = overlayCanvas;
    }

    /// <summary>
    /// Shows highlight overlays for the given mounted/modified elements.
    /// Positions are computed relative to <paramref name="host"/>.
    /// Call this from a post-layout callback so elements have final bounds.
    /// </summary>
    public void Show(
        UIElement host,
        IReadOnlyList<UIElement> mounted,
        IReadOnlyList<UIElement> modified)
    {
        EnsureCompositor();
        if (_compositor is null || _container is null) return;

        _mountedBrush ??= CreateStripeBrush(MountedColor, 45f);
        _modifiedBrush ??= CreateStripeBrush(ModifiedColor, 135f);

        foreach (var element in mounted)
            TryAddHighlight(host, element, _mountedBrush, MountedOpacity);

        foreach (var element in modified)
            TryAddHighlight(host, element, _modifiedBrush, ModifiedOpacity);
    }

    private void TryAddHighlight(UIElement host, UIElement target, CompositionBrush brush, float opacity)
    {
        if (_compositor is null || _container is null) return;

        // Skip elements with no layout or not in the visual tree
        if (target is not FrameworkElement fe) return;
        if (fe.ActualWidth <= 0 || fe.ActualHeight <= 0) return;

        try
        {
            var transform = target.TransformToVisual(host);
            var position = transform.TransformPoint(default);

            var sprite = _compositor.CreateSpriteVisual();
            sprite.Size = new Vector2((float)fe.ActualWidth, (float)fe.ActualHeight);
            sprite.Offset = new Vector3((float)position.X, (float)position.Y, 0);
            sprite.Opacity = opacity;
            sprite.Brush = brush;

            _container.Children.InsertAtTop(sprite);

            // Animate opacity to 0 then remove
            var fadeAnim = _compositor.CreateScalarKeyFrameAnimation();
            fadeAnim.InsertKeyFrame(0f, opacity);
            fadeAnim.InsertKeyFrame(1f, 0f);
            fadeAnim.Duration = TimeSpan.FromMilliseconds(FadeDurationMs);

            var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            sprite.StartAnimation("Opacity", fadeAnim);
            batch.End();
            batch.Completed += (_, _) =>
            {
                _container.Children.Remove(sprite);
                sprite.Dispose();
            };
        }
        catch (ArgumentException)
        {
            // TransformToVisual throws if target is in a different visual tree (popup/flyout)
        }
    }

    /// <summary>
    /// Creates a repeating diagonal-stripe brush. The gradient tiles with
    /// <see cref="CompositionGradientExtendMode.Wrap"/> and is rotated to
    /// the requested angle (e.g. 45° or 135°).
    /// </summary>
    private CompositionBrush CreateStripeBrush(global::Windows.UI.Color color, float angleDegrees)
    {
        var brush = _compositor!.CreateLinearGradientBrush();
        brush.MappingMode = CompositionMappingMode.Absolute;
        brush.ExtendMode = CompositionGradientExtendMode.Wrap;

        // Vertical gradient over one period (stripe + gap), then rotate
        float period = StripeWidth * 2f;
        brush.StartPoint = new Vector2(0, 0);
        brush.EndPoint = new Vector2(0, period);

        var transparent = global::Windows.UI.Color.FromArgb(0, color.R, color.G, color.B);
        brush.ColorStops.Add(_compositor.CreateColorGradientStop(0f, color));
        brush.ColorStops.Add(_compositor.CreateColorGradientStop(0.5f, color));
        brush.ColorStops.Add(_compositor.CreateColorGradientStop(0.5f, transparent));
        brush.ColorStops.Add(_compositor.CreateColorGradientStop(1f, transparent));

        float radians = angleDegrees * MathF.PI / 180f;
        brush.TransformMatrix = Matrix3x2.CreateRotation(radians);

        return brush;
    }

    private void EnsureCompositor()
    {
        if (_compositor is not null) return;

        var visual = ElementCompositionPreview.GetElementVisual(_overlayCanvas);
        _compositor = visual.Compositor;
        _container = _compositor.CreateContainerVisual();
        ElementCompositionPreview.SetElementChildVisual(_overlayCanvas, _container);
    }
}
