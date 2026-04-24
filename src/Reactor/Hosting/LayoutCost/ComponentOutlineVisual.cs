using System.Numerics;
using Microsoft.UI.Composition;
using WColor = global::Windows.UI.Color;

namespace Microsoft.UI.Reactor.Hosting.LayoutCost;

/// <summary>
/// A hollow rectangle outline drawn around a Component's subtree bounds.
/// Built from four thin <see cref="SpriteVisual"/>s (top/bottom/left/right
/// edges) so we stay on the flat-sprite Composition path — no
/// <c>ShapeVisual</c> stroke shaping required.
/// </summary>
/// <remarks>
/// Mirrors <see cref="MeterVisual"/>'s in-place mutation pattern: never
/// recreates visuals, just resizes / repositions on each flush.
/// </remarks>
internal sealed class ComponentOutlineVisual
{
    /// <summary>Outline stroke width in DIPs.</summary>
    public const float Thickness = 2f;

    private readonly ContainerVisual _root;
    private readonly SpriteVisual _top;
    private readonly SpriteVisual _bottom;
    private readonly SpriteVisual _left;
    private readonly SpriteVisual _right;
    private readonly CompositionColorBrush _brush;

    public ComponentOutlineVisual(Compositor compositor, WColor color)
    {
        _brush = compositor.CreateColorBrush(color);

        _top = compositor.CreateSpriteVisual(); _top.Brush = _brush;
        _bottom = compositor.CreateSpriteVisual(); _bottom.Brush = _brush;
        _left = compositor.CreateSpriteVisual(); _left.Brush = _brush;
        _right = compositor.CreateSpriteVisual(); _right.Brush = _brush;

        _root = compositor.CreateContainerVisual();
        _root.Children.InsertAtTop(_top);
        _root.Children.InsertAtTop(_bottom);
        _root.Children.InsertAtTop(_left);
        _root.Children.InsertAtTop(_right);
    }

    public Visual Root => _root;

    /// <summary>Place the outline at (x, y) with the given size in canvas coords.</summary>
    public void SetBounds(float x, float y, float w, float h)
    {
        _root.Offset = new Vector3(x, y, 0);

        // Clamp thickness to half the smaller dimension so tiny rects
        // don't paint as solid blocks.
        float t = global::System.Math.Min(Thickness, global::System.Math.Max(1f, global::System.Math.Min(w, h) * 0.5f));

        _top.Offset = Vector3.Zero;
        _top.Size = new Vector2(w, t);

        _bottom.Offset = new Vector3(0, global::System.Math.Max(h - t, 0), 0);
        _bottom.Size = new Vector2(w, t);

        _left.Offset = new Vector3(0, t, 0);
        _left.Size = new Vector2(t, global::System.Math.Max(h - 2 * t, 0));

        _right.Offset = new Vector3(global::System.Math.Max(w - t, 0), t, 0);
        _right.Size = new Vector2(t, global::System.Math.Max(h - 2 * t, 0));
    }

    public void SetColor(WColor color)
    {
        if (_brush.Color != color) _brush.Color = color;
    }

    public void Show() => _root.IsVisible = true;
    public void Hide() => _root.IsVisible = false;

    public void Dispose()
    {
        _top.Dispose();
        _bottom.Dispose();
        _left.Dispose();
        _right.Dispose();
        _root.Dispose();
        _brush.Dispose();
    }
}
