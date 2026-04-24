using System.Collections.Generic;

namespace Microsoft.UI.Reactor.Hosting.LayoutCost;

/// <summary>
/// Fallback attribution by geometry. Maps each <c>ElementId</c> to its last
/// known root-relative rect (from <c>ArrangeElementEnd</c>), and answers
/// "which Component's subtree contains this point?" for events whose
/// <see cref="PointerMap"/> lookup misses.
/// </summary>
/// <remarks>
/// <para>Multi-root Components (a Component that renders several authored
/// siblings) are handled by unioning the per-root rects — see
/// <see cref="SetComponentBounds"/>. Depth at mount time breaks ties: among
/// Components whose bounds contain the point, the deepest wins (innermost
/// in the Component tree).</para>
/// <para>Known v1 limitation: a popup/flyout whose rect escapes its parent's
/// bounds is attributed by screen position, not ownership. Tracked in the
/// spec's Known-limitation log.</para>
/// </remarks>
internal sealed class SpatialIndex
{
    private readonly Dictionary<ulong, (float x, float y, float w, float h)> _elementRects = new();
    private readonly Dictionary<ComponentIdentity, (float x, float y, float w, float h, int depth)> _componentBounds = new();

    public void RecordElementRect(ulong elementId, float x, float y, float w, float h)
    {
        _elementRects[elementId] = (x, y, w, h);
    }

    public bool TryGetElementRect(ulong elementId, out (float x, float y, float w, float h) rect)
        => _elementRects.TryGetValue(elementId, out rect);

    public void SetComponentBounds(ComponentIdentity id, int depth, float x, float y, float w, float h)
    {
        _componentBounds[id] = (x, y, w, h, depth);
    }

    public void RemoveComponent(ComponentIdentity id) => _componentBounds.Remove(id);

    public void ForgetElement(ulong elementId) => _elementRects.Remove(elementId);

    public IReadOnlyDictionary<ComponentIdentity, (float x, float y, float w, float h, int depth)> ComponentBounds
        => _componentBounds;

    /// <summary>
    /// Returns the tightest-fitting Component whose bounds contain
    /// <paramref name="pointX"/>, <paramref name="pointY"/>, or <c>null</c>
    /// if none do (caller should fall back to the chrome bucket).
    /// </summary>
    /// <remarks>
    /// Tiebreak is <b>smallest rect area wins</b>, not depth. Depth is only
    /// reliably tracked for Components that mount while
    /// <c>ShowLayoutCost</c> is on; any Component back-filled at flag-flip
    /// time has depth 0, making a depth-based tiebreak useless. Area-based
    /// containment picks the most specific bucket regardless of how the
    /// rollup came into existence.
    /// </remarks>
    public ComponentIdentity? AttributeByPoint(float pointX, float pointY)
    {
        ComponentIdentity? best = null;
        double bestArea = double.PositiveInfinity;
        foreach (var kv in _componentBounds)
        {
            var r = kv.Value;
            if (pointX < r.x || pointY < r.y) continue;
            if (pointX > r.x + r.w || pointY > r.y + r.h) continue;
            double area = (double)r.w * r.h;
            if (area < bestArea)
            {
                best = kv.Key;
                bestArea = area;
            }
        }
        return best;
    }
}
