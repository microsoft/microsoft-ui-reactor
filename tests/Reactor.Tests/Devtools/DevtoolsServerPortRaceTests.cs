using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// The preview capture server probes a free port when it's constructed and binds it
/// in <c>Start</c>. Another process can take the port in between: with several
/// selftest hosts running at once, devtools fixtures failed with
/// <c>HttpListenerException</c> (32). Each test here hands
/// the server a probed port that a <see cref="PortThief"/> already holds.
/// </summary>
[Collection("ConsoleTests")] // Start() writes the endpoint banners to Console.Out.
public sealed class DevtoolsServerPortRaceTests
{
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
    public async Task CaptureServer_ConcurrentStart_WaitsForTheFirstStartToBind()
    {
        using var thief = PortThief.HoldWithSocket();
        using var probe = new BlockingProbe(thief.Port);
#pragma warning disable IL2026
        using var server = PreviewCaptureServer.CreateForTests("test-token", probe.Next);
#pragma warning restore IL2026

        await AssertSecondCallWaitsForTheBind(server.Start, server.Start, probe);

        Assert.NotEqual(thief.Port, server.Port);
    }

    [Fact]
    public async Task CaptureServer_DisposeDuringStart_WaitsThenReleasesThePort()
    {
        using var thief = PortThief.HoldWithSocket();
        using var probe = new BlockingProbe(thief.Port);
#pragma warning disable IL2026
        var server = PreviewCaptureServer.CreateForTests("test-token", probe.Next);
#pragma warning restore IL2026

        await AssertSecondCallWaitsForTheBind(server.Start, server.Dispose, probe);

        AssertPortIsFree(server.Port);
    }

    /// <summary>
    /// Parks <paramref name="start"/> inside its bind (the probe blocks while it
    /// retries past the held port), runs <paramref name="other"/> concurrently, and
    /// asserts <paramref name="other"/> doesn't return until the bind completes.
    /// </summary>
    private static async Task AssertSecondCallWaitsForTheBind(Action start, Action other, BlockingProbe probe)
    {
        var first = Task.Factory.StartNew(start, TaskCreationOptions.LongRunning);
        Assert.True(probe.Entered.Wait(TimeSpan.FromSeconds(30)), "Start never reached its retry");

        var second = Task.Factory.StartNew(other, TaskCreationOptions.LongRunning);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted, "returned while Start was still binding");

        probe.Release.Set();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// The constructor's probe gets <c>first</c> (a held port); the retry's probe
    /// blocks until <see cref="Release"/> is set, then returns a real free port.
    /// </summary>
    private sealed class BlockingProbe(int first) : IDisposable
    {
        private int _calls;

        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();

        public int Next()
        {
            if (Interlocked.Increment(ref _calls) == 1) return first;
            Entered.Set();
            Release.Wait(TimeSpan.FromSeconds(30));
            return LoopbackHttpListener.ProbeFreePort();
        }

        // Unblocks a parked Start if an assertion failed before Release was set.
        public void Dispose() => Release.Set();
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
