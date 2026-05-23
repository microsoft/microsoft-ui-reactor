namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest;

/// <summary>
/// Base class for all self-test fixtures. Each fixture mounts UI, runs checks, and reports TAP results.
/// </summary>
internal abstract class SelfTestFixtureBase
{
    protected Harness H { get; }

    protected SelfTestFixtureBase(Harness harness) => H = harness;

    public virtual TimeSpan FixtureTimeout => TimeSpan.FromSeconds(15);

    public abstract Task RunAsync();
}
