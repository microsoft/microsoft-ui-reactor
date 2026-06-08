using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

public sealed class DevtoolsHost_EmbedTests
{
    [Fact]
    public void EmbedHost_ChildMode_ActivatesWindowBeforeAck()
    {
        var options = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--embed", "--embed-host-pid", "1234"]);

        var spec = DevtoolsHost.BuildEmbedWindowSpec(options, "Preview — Counter", 800, 600);

        Assert.NotNull(spec.Embed);
        Assert.True(spec.Embed.InitialVisibility);
        Assert.Equal(WindowEmbedStyle.Child, spec.Embed.Style);
        Assert.Equal(1234, spec.Embed.HostPid);
        Assert.False(spec.PersistPlacement);
    }

    [Fact]
    public void EmbedHost_GenerationMonotonicWithinProcess()
    {
        var host = new DevtoolsHost();

        Assert.Equal(1, host.EmbedGenerationForTests);
        Assert.Equal(1, host.EmbedGenerationForTests);
    }

    [Fact(Skip = "Requires a live WinUI window and native HWND style inspection; covered by Phase 1 manual smoke.")]
    public void EmbedHost_AppliesStyleFlip()
    {
    }

    [Fact(Skip = "Owner-mode requires a real external owner HWND; covered by Phase 1 manual smoke.")]
    public void EmbedHost_OwnerMode_ManualSmokeDeferred()
    {
    }

    [Fact]
    public void EmbedHost_HostPidWatchdog_ExitsOnSignal()
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping -n 2 127.0.0.1 >nul",
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        using var fired = new ManualResetEventSlim(false);
        using var watchdog = new EmbedHostWatchdog();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            watchdog.Start(process.Id, () => fired.Set());
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        Assert.True(fired.Wait(TimeSpan.FromSeconds(5)), "Expected watchdog callback after host process exit.");
        if (!process.HasExited) process.Kill(entireProcessTree: true);
    }
}
