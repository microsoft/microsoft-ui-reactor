using Microsoft.UI.Reactor.Hosting;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the <see cref="HotReloadService.ConsumeUpdatePending"/> Volatile.Read
/// fast-path (#182). The consume runs at the top of every render; the fast-path
/// avoids the full-barrier Interlocked.Exchange when no hot-reload update is
/// pending (the overwhelmingly common case), while still consuming exactly once
/// when an update fires.
/// </summary>
public class HotReloadConsumeUpdatePendingTests
{
    [Fact]
    public void ConsumeUpdatePending_FastPath_FalseWhenIdle_ConsumesOnceWhenPending()
    {
        // Drain any state left by a prior interaction so the test is order- and
        // host-state-independent.
        while (HotReloadService.ConsumeUpdatePending()) { }

        // Fast-path: nothing pending → false, and the flag stays clear.
        Assert.False(HotReloadService.ConsumeUpdatePending());
        Assert.False(HotReloadService.UpdatePending);

        // Simulate a metadata update. With no active host in a unit test this
        // only flips the pending flag (RequestRender is a no-op). A null/empty
        // type list models a whole-assembly reload.
        HotReloadService.UpdateApplication(null);
        Assert.True(HotReloadService.UpdatePending);

        // First consume observes pending → returns true and clears it.
        Assert.True(HotReloadService.ConsumeUpdatePending());

        // Subsequent consume takes the fast-path again → false (consumed once).
        Assert.False(HotReloadService.ConsumeUpdatePending());
        Assert.False(HotReloadService.UpdatePending);
    }
}
