namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest;

/// <summary>
/// Base class for all self-test fixtures. Each fixture mounts UI, runs checks, and reports TAP results.
/// </summary>
internal abstract class SelfTestFixtureBase
{
    protected Harness H { get; }

    protected SelfTestFixtureBase(Harness harness) => H = harness;

    // 30 s default chosen empirically from stress-run timing data: the
    // Framerate.* fixture family completes in ~7 s on a dev box, and
    // CI VMs under contention run 2-4x slower, putting fixture work at
    // ~28 s on the worst tick. Previously several fixtures had explicit
    // 30 s overrides (PR #397, #399); raising the default folds them in
    // and prevents new sister fixtures from inheriting a budget that
    // doesn't survive CI variance. The host-level HangWatchdogLoop at
    // 60 s (SelfTestRunner.cs) continues to catch true dispatcher hangs.
    public virtual TimeSpan FixtureTimeout => TimeSpan.FromSeconds(30);

    public abstract Task RunAsync();
}
