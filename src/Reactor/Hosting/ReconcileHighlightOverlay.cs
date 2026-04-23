using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Draws semi-transparent overlay rectangles over UIElements that were mounted (red)
/// or modified (yellow) during a reconcile pass. Uses the Composition visual layer
/// to avoid creating XAML elements (which would themselves show up as reconcile churn).
/// Each overlay fades out over <see cref="FadeDurationMs"/> milliseconds.
/// </summary>
internal sealed class ReconcileHighlightOverlay
{
    private const float MountedOpacity = 0.35f;
    private const float ModifiedOpacity = 0.25f;
    private const int FadeDurationMs = 600;

    private static readonly global::Windows.UI.Color MountedColor =
        global::Windows.UI.Color.FromArgb(255, 220, 40, 40);   // red
    private static readonly global::Windows.UI.Color ModifiedColor =
        global::Windows.UI.Color.FromArgb(255, 240, 200, 20);  // yellow

    private readonly Canvas _overlayCanvas;
    private ContainerVisual? _container;
    private Compositor? _compositor;

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

        foreach (var element in mounted)
            TryAddHighlight(host, element, MountedColor, MountedOpacity);

        foreach (var element in modified)
            TryAddHighlight(host, element, ModifiedColor, ModifiedOpacity);
    }

    private void TryAddHighlight(UIElement host, UIElement target, global::Windows.UI.Color color, float opacity)
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
            sprite.Brush = _compositor.CreateColorBrush(color);

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

    private void EnsureCompositor()
    {
        if (_compositor is not null) return;

        var visual = ElementCompositionPreview.GetElementVisual(_overlayCanvas);
        _compositor = visual.Compositor;
        _container = _compositor.CreateContainerVisual();
        ElementCompositionPreview.SetElementChildVisual(_overlayCanvas, _container);
    }
}
