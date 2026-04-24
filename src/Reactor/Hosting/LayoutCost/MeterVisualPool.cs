using System.Collections.Generic;
using Microsoft.UI.Composition;

namespace Microsoft.UI.Reactor.Hosting.LayoutCost;

/// <summary>
/// Keeps one <see cref="MeterVisual"/> per Component identity so re-render
/// does not churn Composition objects. Visuals that haven't been touched in
/// a flush are hidden; visuals for unmounted Components are disposed on
/// <see cref="Reap"/>.
/// </summary>
internal sealed class MeterVisualPool
{
    private readonly Compositor _compositor;
    private readonly ContainerVisual _container;
    private readonly Dictionary<ComponentIdentity, MeterVisual> _byId = new();
    private readonly HashSet<ComponentIdentity> _livingThisFlush = new();

    public MeterVisualPool(Compositor compositor, ContainerVisual container)
    {
        _compositor = compositor;
        _container = container;
    }

    /// <summary>Begin a new flush cycle — resets the "seen this frame" set.</summary>
    public void BeginFlush()
    {
        _livingThisFlush.Clear();
    }

    /// <summary>Return a meter visual for <paramref name="id"/>, creating one if absent.</summary>
    public MeterVisual GetOrCreate(ComponentIdentity id)
    {
        if (!_byId.TryGetValue(id, out var visual))
        {
            visual = new MeterVisual(_compositor);
            _byId[id] = visual;
            _container.Children.InsertAtTop(visual.Root);
        }
        _livingThisFlush.Add(id);
        visual.Show();
        return visual;
    }

    /// <summary>
    /// End a flush — hide any visuals that weren't touched this cycle. They
    /// are kept in the pool so the next flush that revives them does zero
    /// allocations; <see cref="Reap"/> actually frees them.
    /// </summary>
    public void EndFlush()
    {
        foreach (var kv in _byId)
        {
            if (!_livingThisFlush.Contains(kv.Key))
                kv.Value.Hide();
        }
    }

    /// <summary>Dispose and drop any visuals for Components that unmounted.</summary>
    public void Reap(IReadOnlySet<ComponentIdentity> alive)
    {
        if (_byId.Count == 0) return;
        List<ComponentIdentity>? dead = null;
        foreach (var kv in _byId)
        {
            if (!alive.Contains(kv.Key))
                (dead ??= new()).Add(kv.Key);
        }
        if (dead is null) return;
        foreach (var id in dead)
        {
            if (_byId.TryGetValue(id, out var v))
            {
                _container.Children.Remove(v.Root);
                v.Dispose();
                _byId.Remove(id);
            }
        }
    }

    public void Dispose()
    {
        foreach (var v in _byId.Values)
            v.Dispose();
        _byId.Clear();
        _livingThisFlush.Clear();
    }

    public int LiveCount => _byId.Count;
}
