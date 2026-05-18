using Microsoft.UI.Reactor.Hosting.Etw;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Hosting.Etw;

public class LayoutEtwConsumerTests
{
    [Fact]
    public void Dispose_WithoutStart_DoesNotThrow()
    {
        using var c = new LayoutEtwConsumer();
        // intentionally no Start — Dispose should be safe.
        Assert.False(c.IsRunning);
    }

    [Fact]
    public void DoubleStart_IsIdempotent()
    {
        // This test tolerates either successful start or IsUnavailable because
        // the test host may not be a Performance Log Users member. We only
        // assert there is no exception and that a second Start is a no-op.
        using var c = new LayoutEtwConsumer();
        c.Start();
        bool firstRunning = c.IsRunning;
        c.Start();
        Assert.Equal(firstRunning, c.IsRunning);
        c.Stop();
        Assert.False(c.IsRunning);
    }

    [Fact]
    public void DisposeAfterFailedStart_LeavesIsRunningFalse()
    {
        // We can't reliably *force* a failure on all machines, so this test
        // just asserts the post-Dispose invariant regardless of whether the
        // session was created.
        var c = new LayoutEtwConsumer();
        c.Start();
        c.Dispose();
        Assert.False(c.IsRunning);
    }

    // ══════════════════════════════════════════════════════════════
    //  Initial state — no ETW session touched. Pins the pre-Start
    //  invariants every caller can rely on.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Initial_State_Is_Idle_With_Zero_Counters()
    {
        // Bug shape: a regression that initialised counters via
        // Interlocked.Read from null-backed fields would NRE the
        // first time anyone polled the diagnostic getters.
        using var c = new LayoutEtwConsumer();
        Assert.False(c.IsRunning);
        Assert.False(c.IsUnavailable);
        Assert.Null(c.UnavailableReason);
        Assert.Equal(0, c.EventsSeenAll);
        Assert.Equal(0, c.EventsSeenForUs);
        Assert.Equal(0, c.EventsMatchedTaskOpcode);
        Assert.Equal(0, c.EventsEmitted);
    }

    [Fact]
    public void SessionNamePrefix_Const_Matches_Documented_Shape()
    {
        // Other code uses the const directly to match orphan sessions
        // — a rename would silently break crash-recovery cleanup.
        Assert.Equal("Reactor.LayoutCost.", LayoutEtwConsumer.SessionNamePrefix);
    }

    [Fact]
    public void XamlProviderGuid_Matches_Documented_Manifest()
    {
        // The GUID is pulled from dxaml/xcp/plat/win/desktop/
        // Microsoft-Windows-XAML-ETW.man. Pin: a regression that
        // changed the GUID would silently consume events from a
        // *different* provider with zero session-level diagnostics.
        Assert.Equal(
            new Guid("531A35AB-63CE-4BCF-AA98-F88C7A89E455"),
            LayoutEtwConsumer.XamlProviderGuid);
    }

    // ══════════════════════════════════════════════════════════════
    //  Stop / Dispose without Start — must be a no-op, not throw.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Stop_Without_Start_Is_Silent_NoOp()
    {
        // The `if (!_running && _session is null) return;` early-out.
        // A regression that dropped this would NRE on the
        // SafeDisposeSession + Thread.Join paths.
        var c = new LayoutEtwConsumer();
        // Must not throw.
        c.Stop();
        Assert.False(c.IsRunning);
    }

    [Fact]
    public void Dispose_Idempotent_When_Called_Twice()
    {
        var c = new LayoutEtwConsumer();
        c.Dispose();
        // The `if (_disposed) return;` guard. Must not throw on second call.
        c.Dispose();
        Assert.False(c.IsRunning);
    }

    // ══════════════════════════════════════════════════════════════
    //  IsProcessAlive — private static helper. Pin via reflection
    //  because the orphan-session cleanup relies on its correctness:
    //  closing a still-running process's session would race the live
    //  consumer; failing to close a dead one would leak the slot.
    // ══════════════════════════════════════════════════════════════

    private static bool InvokeIsProcessAlive(int pid)
    {
        var mi = typeof(LayoutEtwConsumer).GetMethod(
            "IsProcessAlive",
            global::System.Reflection.BindingFlags.Static |
            global::System.Reflection.BindingFlags.NonPublic)!;
        return (bool)mi.Invoke(null, new object?[] { pid })!;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-12345)]
    public void IsProcessAlive_NonPositive_Pid_Returns_False(int pid)
    {
        // The `pid <= 0` early-out. Pin: a regression that called
        // GetProcessById(0) would throw — 0 is reserved (System Idle).
        Assert.False(InvokeIsProcessAlive(pid));
    }

    [Fact]
    public void IsProcessAlive_Current_Process_Returns_True()
    {
        // The test runner is alive by construction. Pin the happy path.
        var me = global::System.Diagnostics.Process.GetCurrentProcess().Id;
        Assert.True(InvokeIsProcessAlive(me));
    }

    [Fact]
    public void IsProcessAlive_Bogus_High_Pid_Returns_False()
    {
        // Bug shape: an attacker plants a stale orphan session pointing
        // at a recycled PID. The "alive" check via Process.GetProcessById
        // throws ArgumentException for non-existent PIDs — pin that the
        // catch arm returns false (so we close the orphan).
        // Use a PID well outside any plausible live process range.
        Assert.False(InvokeIsProcessAlive(int.MaxValue - 1));
    }
}
