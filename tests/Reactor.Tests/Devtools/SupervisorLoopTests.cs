using Microsoft.UI.Reactor.Cli.Devtools;
using DevtoolsHost = Microsoft.UI.Reactor.Hosting.Devtools.DevtoolsHost;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// The <c>mur devtools</c> respawn loop. The supervisor probes the MCP port and
/// the child binds it only after <c>dotnet run</c> builds (and again after each
/// reload rebuild), so another process can take the port in between. The child
/// then exits with <see cref="DevtoolsSupervisor.McpPortUnavailableExitCode"/>.
/// </summary>
public sealed class SupervisorLoopTests
{
    [Fact]
    public void AutoPickedPortTakenBeforeTheChildBinds_IsReplacedAndTheChildRelaunched()
    {
        var run = Supervise(null, [DevtoolsSupervisor.McpPortUnavailableExitCode, 0], probes: [50001, 50002]);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal([50001, 50002], run.LaunchedPorts);
        Assert.Contains("MCP port 50001 was taken by another process", run.Output);
        Assert.Contains("using MCP port 50002 across reloads instead", run.Output);
    }

    [Fact]
    public void UserPinnedPortTakenBeforeTheChildBinds_FailsClearlyWithoutMoving()
    {
        var run = Supervise(9000, [DevtoolsSupervisor.McpPortUnavailableExitCode], probes: []);

        Assert.Equal(DevtoolsSupervisor.McpPortUnavailableExitCode, run.ExitCode);
        Assert.Equal([9000], run.LaunchedPorts);
        Assert.Contains("MCP port 9000 (--mcp-port) is in use by another process", run.Error);
    }

    [Fact]
    public void AutoPickedPortTakenEveryTime_GivesUpAfterMaxConsecutiveConflicts()
    {
        var max = DevtoolsSupervisor.MaxConsecutivePortConflicts;
        var run = Supervise(
            null,
            Enumerable.Repeat(DevtoolsSupervisor.McpPortUnavailableExitCode, max).ToArray(),
            probes: Enumerable.Range(50001, max).ToArray());

        Assert.Equal(DevtoolsSupervisor.McpPortUnavailableExitCode, run.ExitCode);
        Assert.Equal(Enumerable.Range(50001, max), run.LaunchedPorts);
        Assert.Contains("Giving up", run.Error);
    }

    [Fact]
    public void Reload_KeepsThePort_AndASuccessfulRunResetsTheConflictCount()
    {
        var max = DevtoolsSupervisor.MaxConsecutivePortConflicts;
        // One conflict, a run that ends in a reload, then max - 1 more conflicts:
        // never max in a row, so the supervisor keeps going.
        int[] exitCodes =
        [
            DevtoolsSupervisor.McpPortUnavailableExitCode,
            DevtoolsSupervisor.ReloadExitCode,
            .. Enumerable.Repeat(DevtoolsSupervisor.McpPortUnavailableExitCode, max - 1),
            0,
        ];
        var run = Supervise(null, exitCodes, probes: Enumerable.Range(50001, max + 1).ToArray());

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(1, run.Rebuilds);
        // The child that requested the reload ran on 50002; the reload relaunches on it.
        Assert.Equal(50002, run.LaunchedPorts[1]);
        Assert.Equal(50002, run.LaunchedPorts[2]);
        Assert.Equal(50001 + max, run.LaunchedPorts[^1]);
    }

    [Fact]
    public void ReloadWithFailedBuild_Returns2WithoutRelaunching()
    {
        var run = Supervise(9000, [DevtoolsSupervisor.ReloadExitCode], probes: [], buildSucceeds: false);

        Assert.Equal(2, run.ExitCode);
        Assert.Equal([9000], run.LaunchedPorts);
        Assert.Equal(1, run.Rebuilds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void OtherExitCodes_Propagate(int exitCode)
    {
        var run = Supervise(null, [exitCode], probes: [50001]);

        Assert.Equal(exitCode, run.ExitCode);
        Assert.Equal([50001], run.LaunchedPorts);
    }

    [Fact]
    public void ExitCodes_MatchTheDevtoolsChild()
    {
        Assert.Equal(DevtoolsHost.DevtoolsReloadExitCode, DevtoolsSupervisor.ReloadExitCode);
        Assert.Equal(DevtoolsHost.McpPortUnavailableExitCode, DevtoolsSupervisor.McpPortUnavailableExitCode);
    }

    private sealed record Run(int ExitCode, List<int> LaunchedPorts, int Rebuilds, string Output, string Error);

    private static Run Supervise(int? userPinnedPort, int[] exitCodes, int[] probes, bool buildSucceeds = true)
    {
        var exits = new Queue<int>(exitCodes);
        var probed = new Queue<int>(probes);
        var launched = new List<int>();
        var rebuilds = 0;
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = DevtoolsSupervisor.Supervise(
            userPinnedPort,
            launchChild: port => { launched.Add(port); return exits.Dequeue(); },
            rebuild: () => { rebuilds++; return buildSucceeds; },
            probePort: () => probed.Dequeue(),
            output,
            error);

        Assert.Empty(exits); // every scripted child exit was consumed
        return new Run(exitCode, launched, rebuilds, output.ToString(), error.ToString());
    }
}
