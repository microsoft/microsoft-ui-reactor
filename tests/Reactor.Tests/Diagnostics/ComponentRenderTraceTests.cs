using Microsoft.UI.Reactor.Core.Diagnostics;
using Xunit;
using R = Microsoft.UI.Reactor.Core.Diagnostics.ComponentRenderTrace.Reasons;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// Headless half of <c>ComponentRendered</c>: the reason classification and the
/// componentId ↔ control registry. The reconciler wiring that feeds them needs live
/// WinUI controls and is covered by the <c>ComponentRendered_*</c> selftest fixture.
/// </summary>
public sealed class ComponentRenderTraceTests
{
    [Theory]
    // forced wins over everything, split by whether a hot-reload pass is open
    [InlineData(true, true, true, R.Props, R.HotReload)]
    [InlineData(true, false, false, null, R.Forced)]
    [InlineData(true, false, true, R.Context, R.Forced)]
    // a subtree re-render request
    [InlineData(false, false, true, R.Props, R.State)]
    [InlineData(false, true, true, null, R.HotReload)]
    // reached from the parent: whatever the memo gate found, else "parent"
    [InlineData(false, false, false, R.Props, R.Props)]
    [InlineData(false, false, false, R.Context, R.Context)]
    [InlineData(false, false, false, R.Parent, R.Parent)]
    [InlineData(false, false, false, null, R.Parent)]
    public void ClassifyUpdate(bool forced, bool hotReloadPass, bool selfTriggered, string? memoReason, string expected)
        => Assert.Equal(expected, ComponentRenderTrace.ClassifyUpdate(forced, hotReloadPass, selfTriggered, memoReason));

    [Theory]
    [InlineData(true, true, true, R.Mount)]
    [InlineData(false, true, true, R.HotReload)]
    [InlineData(false, false, true, R.Forced)]
    [InlineData(false, false, false, R.State)]
    public void ClassifyRoot(bool first, bool hotReload, bool forcePending, string expected)
        => Assert.Equal(expected, ComponentRenderTrace.ClassifyRoot(first, hotReload, forcePending));

    [Fact]
    public void Reasons_AreTheDocumentedWireTokens()
    {
        // Consumers bind to these strings; renaming one is a breaking change.
        Assert.Equal(
            new[] { "mount", "state", "props", "context", "parent", "hotReload", "forced" },
            new[] { R.Mount, R.State, R.Props, R.Context, R.Parent, R.HotReload, R.Forced });
    }

    [Fact]
    public void NextId_IsNeverZeroAndNeverRepeats()
    {
        var a = ComponentRenderTrace.NextId();
        var b = ComponentRenderTrace.NextId();
        Assert.NotEqual(0, a);
        Assert.True(b > a);
    }

    [Fact]
    public void Registry_ResolvesBothDirections()
    {
        var registry = NewRegistry();
        var wrapper = new object();

        registry.Track(7, wrapper, mapControlToId: true);

        Assert.Same(wrapper, registry.Resolve(7));
        Assert.True(registry.TryGetId(wrapper, out var id));
        Assert.Equal(7, id);
        Assert.Null(registry.Resolve(8));
        Assert.Null(registry.Resolve(0));
    }

    [Fact]
    public void Registry_RootMapping_DoesNotStealTheReverseLookup()
    {
        // A host's root content control is often a child component's wrapper; the child
        // keeps control → id, the root only gets id → control.
        var registry = NewRegistry();
        var shared = new object();

        registry.Track(10, shared, mapControlToId: true);   // child component
        registry.Track(11, shared, mapControlToId: false);  // host root

        Assert.Same(shared, registry.Resolve(11));
        Assert.True(registry.TryGetId(shared, out var id));
        Assert.Equal(10, id);
    }

    [Fact]
    public void Registry_Forget_DropsBothDirections_AndARecycledWrapperIsNotMisattributed()
    {
        var registry = NewRegistry();
        var wrapper = new object();
        registry.Track(20, wrapper, mapControlToId: true);

        registry.Forget(20, wrapper);   // unmount; the wrapper may now be pooled

        Assert.Null(registry.Resolve(20));
        Assert.False(registry.TryGetId(wrapper, out _));

        registry.Track(21, wrapper, mapControlToId: true);   // reused by a new component
        Assert.True(registry.TryGetId(wrapper, out var id));
        Assert.Equal(21, id);
    }

    [Fact]
    public void Registry_Forget_LeavesANewerOwnerOfTheControlAlone()
    {
        var registry = NewRegistry();
        var wrapper = new object();
        registry.Track(30, wrapper, mapControlToId: true);
        registry.Track(31, wrapper, mapControlToId: true);   // adopted by a fresh node

        registry.Forget(30, wrapper);

        Assert.True(registry.TryGetId(wrapper, out var id));
        Assert.Equal(31, id);
    }

    [Fact]
    public void Registry_NullControl_ClearsIdToControl()
    {
        var registry = NewRegistry();
        registry.Track(40, new object(), mapControlToId: false);

        registry.Track(40, null, mapControlToId: false);

        Assert.Null(registry.Resolve(40));
    }

    [Fact]
    public void Registry_Retarget_ClearsThePreviousControlsReverseSlot()
    {
        // TryAdoptRealizedReplacement moves an id from the replacement wrapper onto the
        // realized one; the discarded wrapper must stop reporting that id.
        var registry = NewRegistry();
        var replacement = new object();
        var realized = new object();
        registry.Track(60, replacement, mapControlToId: true);

        registry.Track(60, realized, mapControlToId: true);

        Assert.Same(realized, registry.Resolve(60));
        Assert.True(registry.TryGetId(realized, out var id));
        Assert.Equal(60, id);
        Assert.False(registry.TryGetId(replacement, out _));
    }

    [Fact]
    public void Registry_Prune_SweepsCollectedControls()
    {
        var registry = NewRegistry();
        var alive = new object();
        var doomed = new object();
        registry.Track(50, alive, mapControlToId: true);
        registry.Track(51, doomed, mapControlToId: true);

        // Stands in for the control being collected, without forcing a GC.
        registry.ExpireForTests(51);
        Assert.Equal(2, registry.CountForTests);
        registry.PruneForTests();

        Assert.Equal(1, registry.CountForTests);
        Assert.Same(alive, registry.Resolve(50));
        Assert.Null(registry.Resolve(51));
    }

    // Headless stand-in for the runtime's ReactorState slot: one id per object identity.
    private static ComponentControlRegistry<object> NewRegistry()
    {
        var slots = new global::System.Runtime.CompilerServices.ConditionalWeakTable<object, global::System.Runtime.CompilerServices.StrongBox<long>>();
        return new ComponentControlRegistry<object>(
            c => slots.TryGetValue(c, out var box) ? box.Value : 0,
            (c, id) => slots.GetValue(c, static _ => new global::System.Runtime.CompilerServices.StrongBox<long>()).Value = id);
    }
}
