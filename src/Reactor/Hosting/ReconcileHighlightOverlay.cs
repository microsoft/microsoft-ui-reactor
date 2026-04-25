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
/// Designed for best-effort display under high update cadence — caps sprites and
/// uses a single scoped batch per flush to avoid swamping the compositor.
/// </summary>
internal sealed class ReconcileHighlightOverlay : IDisposable
{
    private const float MountedOpacity = 0.22f;
    private const float ModifiedOpacity = 0.17f;
    private const int FadeDurationMs = 600;
    private const float StripeWidth = 5f;

    /// <summary>Max sprites to add per flush call (excess elements are dropped).</summary>
    private const int MaxSpritesPerFlush = 200;

    /// <summary>Max live sprites in the container — skip adding more if exceeded.</summary>
    private const int MaxLiveSprites = 500;

    private static readonly global::Windows.UI.Color MountedColor =
        global::Windows.UI.Color.FromArgb(255, 220, 40, 40);   // red at 45°
    private static readonly global::Windows.UI.Color ModifiedColor =
        global::Windows.UI.Color.FromArgb(255, 240, 200, 20);  // yellow at 135°

    private readonly Canvas _overlayCanvas;
    private readonly ContainerVisual _parentContainer;
    private readonly Compositor _compositor;
    private readonly ContainerVisual _container;
    private CompositionBrush? _mountedBrush;
    private CompositionBrush? _modifiedBrush;
    private ScalarKeyFrameAnimation? _fadeMountedAnim;
    private ScalarKeyFrameAnimation? _fadeModifiedAnim;

    /// <summary>
    /// Ctor takes both the Canvas (for hit-testing / size queries) and the
    /// shared parent <see cref="ContainerVisual"/> owned by
    /// <see cref="OverlayHostWiring"/>. This overlay creates its own
    /// sub-container as a child of <paramref name="parentContainer"/>, so
    /// every dev overlay can paint into the same Canvas without fighting
    /// for the single <c>SetElementChildVisual</c> slot.
    /// </summary>
    public ReconcileHighlightOverlay(Canvas overlayCanvas, ContainerVisual parentContainer)
    {
        _overlayCanvas = overlayCanvas;
        _parentContainer = parentContainer;
        _compositor = parentContainer.Compositor;
        _container = _compositor.CreateContainerVisual();
        _parentContainer.Children.InsertAtTop(_container);
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
        // Back-pressure: if too many sprites are already animating, skip this flush entirely
        if (_container.Children.Count >= MaxLiveSprites) return;

        _mountedBrush ??= CreateStripeBrush(MountedColor, 45f);
        _modifiedBrush ??= CreateStripeBrush(ModifiedColor, 135f);
        _fadeMountedAnim ??= CreateFadeAnimation(MountedOpacity);
        _fadeModifiedAnim ??= CreateFadeAnimation(ModifiedOpacity);

        int budget = MaxSpritesPerFlush;

        // Single scoped batch for ALL sprites in this flush — avoids per-sprite batch overhead.
        // CompositionScopedBatch is IDisposable; we dispose it from the Completed handler
        // (or from the catch path below) so long debugging sessions don't leak COM/resource
        // pressure one batch at a time.
        var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        var container = _container;
        bool disposeInCompleted = false;

        try
        {
            for (int i = 0; i < mounted.Count && budget > 0; i++)
            {
                if (TryAddHighlight(host, mounted[i], _mountedBrush, MountedOpacity, _fadeMountedAnim))
                    budget--;
            }

            for (int i = 0; i < modified.Count && budget > 0; i++)
            {
                if (TryAddHighlight(host, modified[i], _modifiedBrush, ModifiedOpacity, _fadeModifiedAnim))
                    budget--;
            }

            // When all animations in this batch complete, bulk-remove the sprites.
            batch.Completed += (_, _) =>
            {
                try
                {
                    // Remove sprites that have fully faded (opacity ≈ 0).
                    // Walk in reverse to safely remove while iterating.
                    for (int i = container.Children.Count - 1; i >= 0; i--)
                    {
                        var child = container.Children.ElementAt(i);
                        if (child.Opacity <= 0.001f)
                        {
                            container.Children.Remove(child);
                            child.Dispose();
                        }
                    }
                }
                finally
                {
                    batch.Dispose();
                }
            };

            disposeInCompleted = true;
            batch.End();
        }
        catch
        {
            if (!disposeInCompleted) batch.Dispose();
            throw;
        }
    }

    private bool TryAddHighlight(UIElement host, UIElement target, CompositionBrush brush,
        float opacity, ScalarKeyFrameAnimation fadeAnim)
    {
        if (target is not FrameworkElement fe) return false;
        if (fe.ActualWidth <= 0 || fe.ActualHeight <= 0) return false;

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
            sprite.StartAnimation("Opacity", fadeAnim);
            return true;
        }
        catch (ArgumentException)
        {
            // TransformToVisual throws if target is in a different visual tree (popup/flyout)
            return false;
        }
    }

    private ScalarKeyFrameAnimation CreateFadeAnimation(float fromOpacity)
    {
        var anim = _compositor.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(0f, fromOpacity);
        anim.InsertKeyFrame(1f, 0f);
        anim.Duration = TimeSpan.FromMilliseconds(FadeDurationMs);
        return anim;
    }

    /// <summary>
    /// Creates a repeating diagonal-stripe brush. The gradient tiles with
    /// <see cref="CompositionGradientExtendMode.Wrap"/> and is rotated to
    /// the requested angle (e.g. 45° or 135°).
    /// </summary>
    private CompositionBrush CreateStripeBrush(global::Windows.UI.Color color, float angleDegrees)
    {
        var brush = _compositor.CreateLinearGradientBrush();
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

    public void Dispose()
    {
        // Dispose any in-flight sprite children before tearing down the
        // container itself. They hold animations + brushes that the
        // compositor would otherwise leak across long debug sessions.
        try
        {
            for (int i = _container.Children.Count - 1; i >= 0; i--)
            {
                var child = _container.Children.ElementAt(i);
                try { _container.Children.Remove(child); } catch { }
                try { child.Dispose(); } catch { }
            }
        }
        catch { }
        try { _parentContainer.Children.Remove(_container); } catch { }
        try { _container.Dispose(); } catch { }
        try { _mountedBrush?.Dispose(); } catch { }
        try { _modifiedBrush?.Dispose(); } catch { }
        try { _fadeMountedAnim?.Dispose(); } catch { }
        try { _fadeModifiedAnim?.Dispose(); } catch { }
    }
}
