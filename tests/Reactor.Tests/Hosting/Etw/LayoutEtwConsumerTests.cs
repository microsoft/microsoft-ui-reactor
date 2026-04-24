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
}
