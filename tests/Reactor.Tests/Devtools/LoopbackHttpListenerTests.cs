using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// The probe-then-bind race the devtools HTTP listeners had: a port probed as
/// free can be taken by another process before <see cref="HttpListener"/> binds
/// it. These tests take the probed port deterministically, with a socket or a
/// listener that stands in for the other process, instead of waiting for a
/// real collision.
/// </summary>
public sealed class LoopbackHttpListenerTests
{
    [Fact]
    public async Task Unpinned_PortHeldBySocket_BindsAFreshPortAndServes()
    {
        using var thief = PortThief.HoldWithSocket();
        var probes = 0;

        var (listener, port) = LoopbackHttpListener.Start(
            thief.Port,
            pinned: false,
            probePort: () => { probes++; return LoopbackHttpListener.ProbeFreePort(); });

        using (listener)
        {
            Assert.NotEqual(thief.Port, port);
            // At least one re-probe; more only if another process also took a probed port.
            Assert.True(probes >= 1, $"expected a re-probe after the taken port, got {probes}");
            Assert.Equal(HttpStatusCode.NoContent, await RoundTripAsync(listener, port));
        }
    }

    [Fact]
    public async Task Unpinned_PortHeldByAnotherHttpListener_BindsAFreshPortAndServes()
    {
        using var thief = PortThief.HoldWithHttpListener();

        var (listener, port) = LoopbackHttpListener.Start(thief.Port, pinned: false);

        using (listener)
        {
            Assert.NotEqual(thief.Port, port);
            Assert.Equal(HttpStatusCode.NoContent, await RoundTripAsync(listener, port));
        }
    }

    [Fact]
    public void Pinned_PortHeld_ThrowsWithoutMoving()
    {
        using var thief = PortThief.HoldWithSocket();

        var ex = Assert.Throws<LoopbackPortUnavailableException>(() => LoopbackHttpListener.Start(
            thief.Port,
            pinned: true,
            probePort: () => throw new InvalidOperationException("a pinned port must never be re-probed")));

        Assert.True(ex.Pinned);
        Assert.Equal(thief.Port, ex.Port);
        Assert.Equal(1, ex.Attempts);
        Assert.Contains(thief.Port.ToString(), ex.Message);
        var inner = Assert.IsType<HttpListenerException>(ex.InnerException);
        Assert.Equal(LoopbackHttpListener.ErrorSharingViolation, inner.ErrorCode);
    }

    [Fact]
    public void Unpinned_EveryPortHeld_GivesUpAfterMaxAttempts()
    {
        using var thieves = PortThief.HoldWithSockets(LoopbackHttpListener.MaxAttempts);
        var next = 1;

        var ex = Assert.Throws<LoopbackPortUnavailableException>(() => LoopbackHttpListener.Start(
            thieves.Ports[0],
            pinned: false,
            probePort: () => thieves.Ports[next++]));

        Assert.False(ex.Pinned);
        Assert.Equal(LoopbackHttpListener.MaxAttempts, ex.Attempts);
        Assert.Equal(thieves.Ports[^1], ex.Port);
        Assert.Equal(LoopbackHttpListener.MaxAttempts, next);
    }

    [Fact]
    public void Configure_RunsOnEveryAttempt_IncludingTheOneThatBinds()
    {
        using var thief = PortThief.HoldWithSocket();
        var configured = new List<HttpListener>();

        var (listener, _) = LoopbackHttpListener.Start(thief.Port, pinned: false, configure: configured.Add);

        using (listener)
        {
            Assert.True(configured.Count >= 2, $"expected the taken port's attempt and a retry, got {configured.Count}");
            Assert.NotSame(listener, configured[0]);
            Assert.Same(listener, configured[^1]);
        }
    }

    [Theory]
    [InlineData(32, true)]   // ERROR_SHARING_VIOLATION
    [InlineData(183, true)]  // ERROR_ALREADY_EXISTS
    [InlineData(5, false)]   // ERROR_ACCESS_DENIED: not a port another process won
    [InlineData(87, false)]  // ERROR_INVALID_PARAMETER
    public void IsPortInUse_MatchesOnlyThePortTakenErrors(int errorCode, bool expected)
    {
        Assert.Equal(expected, LoopbackHttpListener.IsPortInUse(new HttpListenerException(errorCode)));
    }

    private static async Task<HttpStatusCode> RoundTripAsync(HttpListener listener, int port)
    {
        var serve = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            ctx.Response.StatusCode = (int)HttpStatusCode.NoContent;
            ctx.Response.Close();
        });
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        using var response = await http.GetAsync($"http://127.0.0.1:{port}/", TestContext.Current.CancellationToken);
        await serve;
        return response.StatusCode;
    }
}

/// <summary>
/// Holds loopback ports the way another process would: listening sockets
/// (HttpListener fails with ERROR_SHARING_VIOLATION) or another HttpListener
/// registration (ERROR_ALREADY_EXISTS).
/// </summary>
internal sealed class PortThief : IDisposable
{
    private readonly List<Socket> _sockets;
    private readonly HttpListener? _listener;

    private PortThief(IReadOnlyList<int> ports, List<Socket> sockets, HttpListener? listener)
    {
        Ports = ports;
        _sockets = sockets;
        _listener = listener;
    }

    /// <summary>The first (usually only) held port.</summary>
    public int Port => Ports[0];

    public IReadOnlyList<int> Ports { get; }

    public static PortThief HoldWithSocket() => HoldWithSockets(1);

    public static PortThief HoldWithSockets(int count)
    {
        var sockets = new List<Socket>(count);
        for (int i = 0; i < count; i++)
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            sockets.Add(socket);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            socket.Listen();
        }
        return new PortThief([.. sockets.Select(s => ((IPEndPoint)s.LocalEndPoint!).Port)], sockets, null);
    }

    public static PortThief HoldWithHttpListener()
    {
        var (listener, port) = LoopbackHttpListener.Start(LoopbackHttpListener.ProbeFreePort(), pinned: false);
        return new PortThief([port], [], listener);
    }

    public void Dispose()
    {
        foreach (var socket in _sockets) socket.Dispose();
        _listener?.Close();
    }
}
