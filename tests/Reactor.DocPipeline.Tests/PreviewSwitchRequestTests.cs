using System.Net;
using System.Net.Sockets;
using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Covers the HTTP request the capture client still makes to the in-app preview host:
/// the component switch (<c>POST /preview</c>). Frames come from winapp now
/// (<see cref="WinAppCapture"/>); the hold-out for a painted frame is covered by
/// <c>WinAppCaptureTests</c>.
/// </summary>
/// <remarks>
/// Driven over a real loopback socket rather than a mocked <c>HttpClient</c>. A raw
/// <see cref="TcpListener"/> is used rather than <c>HttpListener</c> so the test needs no
/// URL ACL reservation on Windows.
/// </remarks>
public class PreviewSwitchRequestTests
{
    private readonly ITestOutputHelper _output;

    public PreviewSwitchRequestTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The capture client must address the server by IP literal, not by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the regression that broke every socket test in this class on CI
    /// while passing locally, and the reason it deserves a deterministic oracle
    /// rather than trust in the fixtures: they only discriminate on a machine
    /// where <c>localhost</c> resolves to <c>::1</c> first and the IPv6 connect
    /// stalls. There, every request is cancelled inside the doomed attempt and
    /// nothing reaches the IPv4 listener — which is also what production does,
    /// since <c>PreviewCaptureServer</c> binds <c>http://127.0.0.1:{port}/</c>
    /// and nothing else.
    /// </para>
    /// <para>
    /// The assertion is on the property, not the spelling: the host must parse
    /// as an <see cref="IPAddress"/>, which is exactly the condition under which
    /// no name resolution happens. It fails for <c>localhost</c>, for any other
    /// hostname, and for a future change back to one.
    /// </para>
    /// </remarks>
    [Fact]
    public void Preview_url_addresses_the_server_by_ip_literal()
    {
        var uri = new global::System.Uri(ScreenshotCapture.PreviewUrl(4242));

        Assert.True(IPAddress.TryParse(uri.Host, out var ip),
            $"host '{uri.Host}' is a name, so the request depends on how it resolves — " +
            "the failure mode is a stalled IPv6 attempt that eats the whole deadline");
        Assert.Equal(IPAddress.Loopback, ip);
        Assert.Equal(4242, uri.Port);
        Assert.Equal("/preview", uri.AbsolutePath);
    }

    /// <summary>
    /// The fixtures in this file bind <see cref="IPAddress.Loopback"/>, so the
    /// address the client uses has to be one they are reachable on. Stated as a
    /// test because "the client and the fixture agree" is otherwise an
    /// assumption that only fails somewhere else, on a different machine.
    /// </summary>
    [Fact]
    public void Fixtures_bind_the_address_the_capture_client_uses()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var bound = ((IPEndPoint)probe.LocalEndpoint).Address;
        probe.Stop();

        Assert.Equal(bound, IPAddress.Parse(ScreenshotCapture.CaptureHost));
    }

    /// <summary>
    /// The component switch must be bounded by its own timeout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The component switch shares the <see cref="HttpClient"/> and needs its own
    /// bound: the switch runs <em>once per screenshot</em>,
    /// so an unbounded one multiplies <see cref="HttpClient"/>'s 100-second default
    /// by the size of the manifest.
    /// </para>
    /// <para>
    /// The premise guard carries the same weight it does above. A connection
    /// refused outright throws the same exception type just as quickly and would
    /// satisfy the timing assertion while testing nothing — the request has to be
    /// genuinely in flight and abandoned. <c>stall.Accepted &gt; 0</c> is what
    /// separates those two, and the elapsed time is what fails if the token is
    /// removed: the call then waits out the 100-second default instead.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_stalled_component_switch_does_not_outlive_its_timeout()
    {
        using var stall = new StallingListener();
        using var http = new HttpClient();
        var timeout = TimeSpan.FromMilliseconds(500);

        var sw = global::System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ScreenshotCapture.SwitchComponent(http, stall.Port, "Demo", timeout));
        sw.Stop();

        _output.WriteLine($"elapsed={sw.Elapsed.TotalSeconds:F2}s accepted={stall.Accepted} " +
                          $"(timeout={timeout.TotalSeconds:F2}s, HttpClient default={http.Timeout.TotalSeconds:F0}s)");

        Assert.True(stall.Accepted > 0,
            "the listener was never reached, so nothing was ever stalled — this asserts nothing");
        // Must be read before Dispose cancels the token — see StallingListener for why
        Assert.Null(stall.Fault);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
            $"the timeout did not bound the request: {sw.Elapsed.TotalSeconds:F1}s elapsed " +
            $"against a {timeout.TotalSeconds:F1}s timeout");
    }

    /// <summary>
    /// Accepts connections and answers nothing, holding the socket open. The
    /// sockets are kept referenced rather than dropped so the OS cannot reset
    /// the connection and hand the client a fast failure instead of the stall.
    /// </summary>
    private sealed class StallingListener : global::System.IDisposable
    {
        private readonly TcpListener _listener;
        private readonly List<TcpClient> _held = [];
        private readonly CancellationTokenSource _cts = new();
        private int _accepted;
        private Exception? _fault;

        public StallingListener()
        {
            // Loopback-only, mirroring PreviewCaptureServer, which binds 127.0.0.1 only.
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoop);
        }

        public int Port { get; }

        public int Accepted => Volatile.Read(ref _accepted);

        /// <summary>
        /// First exception the accept loop hit that was <em>not</em> explained by
        /// teardown, or null. The loop
        /// runs detached on a discarded task, so a genuine socket or IO fault had
        /// nowhere to surface and the test would have reported only the downstream
        /// symptom — an accepted count that is mysteriously short — with no trace
        /// of the cause. The test asserts this is null, which is what makes the
        /// shutdown catch safe to keep quiet.
        /// </summary>
        public Exception? Fault => Volatile.Read(ref _fault);

        private async Task AcceptLoop()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    lock (_held) _held.Add(client);
                    Interlocked.Increment(ref _accepted);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException
                                          or global::System.IO.IOException
                                          or global::System.ObjectDisposedException)
            {
                // Exactly what Dispose() produces: cancelling the token and then
                // stopping the listener races the pending accept. Silent only
                // when teardown explains it — outside teardown the same exception
                // is a real transport failure, and the previous filter
                // (`when (_cts.IsCancellationRequested)`) let it escape into a
                // discarded task instead, where it was lost rather than swallowed.
                if (!_cts.IsCancellationRequested)
                    Interlocked.CompareExchange(ref _fault, ex, null);
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            lock (_held)
            {
                foreach (var c in _held) c.Dispose();
                _held.Clear();
            }
            _cts.Dispose();
        }
    }

}
