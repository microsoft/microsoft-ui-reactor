using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Pure-CLR coverage for <see cref="WindowRegistry"/>'s new spec-036 §10 surface
/// (back-reference to <see cref="ReactorWindow"/> + <see cref="WindowRegistry.ResolveReactorWindow"/>).
/// The live attach path needs a real WinUI window and is exercised by the
/// Phase-9 selftest matrix.
/// </summary>
public class WindowRegistrySnapshotTests
{
    [Fact]
    public void Snapshot_OnEmptyRegistry_IsEmptyList()
    {
        var reg = new WindowRegistry("test-build");
        Assert.Empty(reg.Snapshot());
    }

    [Fact]
    public void ResolveReactorWindow_UnknownId_ReturnsNull()
    {
        var reg = new WindowRegistry("test-build");
        Assert.Null(reg.ResolveReactorWindow("does-not-exist"));
    }

    [Fact]
    public void Resolve_UnknownId_ReturnsNull()
    {
        var reg = new WindowRegistry("test-build");
        Assert.Null(reg.Resolve("does-not-exist"));
    }

    [Fact]
    public void Detach_OnNullWindow_IsNoOp()
    {
        var reg = new WindowRegistry("test-build");
        // Idempotent / null-tolerant — the WindowClosed handler wires this from
        // a static event whose handler may be invoked after the window object
        // has already been GC'd through a WeakReference race.
        reg.Detach(null!);
        Assert.Empty(reg.Snapshot());
    }

    [Fact]
    public void TryDefault_OnEmptyRegistry_ReturnsNullAndEmptyIds()
    {
        var reg = new WindowRegistry("test-build");
        var w = reg.TryDefault(out var ids);
        Assert.Null(w);
        Assert.Empty(ids);
    }
}
