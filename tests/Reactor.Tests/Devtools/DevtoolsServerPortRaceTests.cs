using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// The devtools servers probe a free port when they're constructed and bind it
/// in <c>Start</c>. Another process can take the port in between: with several
/// selftest hosts running at once, <c>Devtools_TreeSummary</c> and its sibling
/// fixtures failed with <c>HttpListenerException</c> (32). Each test here hands
/// the server a probed port that a <see cref="PortThief"/> already holds.
/// </summary>
[Collection("ConsoleTests")] // Start() writes the endpoint banners to Console.Out.
public sealed class DevtoolsServerPortRaceTests
{
    [Fact]
    public async Task McpServer_ProbedPortTakenBeforeStart_ServesOnAFreshPort()
    {
        using var thief = PortThief.HoldWithSocket();
        using var server = new DevtoolsMcpServer(null!, null!, probePort: ProbesStartingWith(thief.Port));
        Assert.Equal(thief.Port, server.Port);

        server.Start();

        Assert.NotEqual(thief.Port, server.Port);
        // The Host-header check compares against Port, so a 200 here also proves
        // Port was updated to the bound port before the first request was served.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{server.Port}/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.AuthToken);
        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var schema = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal($"http://127.0.0.1:{server.Port}/mcp", schema.RootElement.GetProperty("endpoint").GetString());
    }

    [Fact]
    public void McpServer_PinnedPortTaken_ThrowsWithoutMovingAndTheHostExits43()
    {
        using var thief = PortThief.HoldWithSocket();
        using var server = new DevtoolsMcpServer(
            null!,
            null!,
            preferredPort: thief.Port,
            probePort: () => throw new InvalidOperationException("a pinned port must never be re-probed"));

        var ex = Assert.Throws<LoopbackPortUnavailableException>(server.Start);

        Assert.True(ex.Pinned);
        Assert.Equal(thief.Port, ex.Port);
        Assert.Equal(thief.Port, server.Port);
        Assert.Equal(DevtoolsHost.McpPortUnavailableExitCode, DevtoolsHost.ExitCodeForMcpStartFailure(ex));
    }

    [Fact]
    public void ExitCodeForMcpStartFailure_KeepsRunningForEverythingButAPinnedPortInUse()
    {
        var inUse = new HttpListenerException(LoopbackHttpListener.ErrorSharingViolation);

        Assert.Equal(
            DevtoolsHost.McpPortUnavailableExitCode,
            DevtoolsHost.ExitCodeForMcpStartFailure(new LoopbackPortUnavailableException(9000, pinned: true, attempts: 1, inUse)));
        Assert.Null(DevtoolsHost.ExitCodeForMcpStartFailure(
            new LoopbackPortUnavailableException(9000, pinned: false, attempts: LoopbackHttpListener.MaxAttempts, inUse)));
        Assert.Null(DevtoolsHost.ExitCodeForMcpStartFailure(new HttpListenerException(5)));
        Assert.Null(DevtoolsHost.ExitCodeForMcpStartFailure(new ObjectDisposedException(nameof(DevtoolsMcpServer))));
    }

    [Fact]
    public void McpServer_StartAfterDispose_ThrowsAndBindsNothing()
    {
        var probed = 0;
        var server = new DevtoolsMcpServer(null!, null!, probePort: () => { probed++; return LoopbackHttpListener.ProbeFreePort(); });
        server.Dispose();

        Assert.Throws<ObjectDisposedException>(server.Start);
        Assert.Equal(1, probed); // only the constructor's probe; Start never tried to bind
    }

    [Fact]
    public void McpServer_SecondStart_IsANoOpAndDisposeReleasesThePort()
    {
        int port;
        using (var server = new DevtoolsMcpServer(null!, null!))
        {
            server.Start();
            port = server.Port;
            server.Start();
            Assert.Equal(port, server.Port);
        }

        AssertPortIsFree(port);
    }

    [Fact]
    public void CaptureServer_SecondStart_IsANoOpAndDisposeReleasesThePort()
    {
        int port;
#pragma warning disable IL2026
        using (var server = PreviewCaptureServer.CreateForTests("test-token"))
#pragma warning restore IL2026
        {
            server.Start();
            port = server.Port;
            server.Start();
            Assert.Equal(port, server.Port);
        }

        AssertPortIsFree(port);
    }

    /// <summary>Pins <paramref name="port"/>: fails if any listener the server bound is still open.</summary>
    private static void AssertPortIsFree(int port)
    {
        var (listener, _) = LoopbackHttpListener.Start(port, pinned: true);
        listener.Close();
    }

    [Fact]
    public async Task CaptureServer_ProbedPortTakenBeforeStart_ServesOnAFreshPort()
    {
        const string token = "test-token";
        using var thief = PortThief.HoldWithSocket();
#pragma warning disable IL2026
        using var server = PreviewCaptureServer.CreateForTests(token, ProbesStartingWith(thief.Port));
#pragma warning restore IL2026
        Assert.Equal(thief.Port, server.Port);

        server.Start();

        Assert.NotEqual(thief.Port, server.Port);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{server.Port}/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var status = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(server.Port, status.RootElement.GetProperty("port").GetInt32());
    }

    /// <summary>The first probe returns <paramref name="first"/>; later probes find real free ports.</summary>
    private static Func<int> ProbesStartingWith(int first)
    {
        var handedOut = false;
        return () =>
        {
            if (handedOut) return LoopbackHttpListener.ProbeFreePort();
            handedOut = true;
            return first;
        };
    }
}
