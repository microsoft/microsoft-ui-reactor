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
    /// A frame is refused when the preview host reports a render error, and also when it
    /// cannot report one: an absent field is not evidence of a clean render.
    /// </summary>
    [Fact]
    public void Status_render_error_is_read_and_a_missing_field_fails_closed()
    {
        Assert.Null(ScreenshotCapture.ParseRenderError("""{"building":false,"renderError":null}"""));
        Assert.Equal("RootRender in Alert: NullReferenceException: boom",
            ScreenshotCapture.ParseRenderError("""{"renderError":"RootRender in Alert: NullReferenceException: boom"}"""));
        Assert.Throws<InvalidOperationException>(() => ScreenshotCapture.ParseRenderError("""{"building":false}"""));
        Assert.Throws<InvalidOperationException>(() => ScreenshotCapture.ParseRenderError("""{"renderError":42}"""));
    }

    /// <summary>
    /// Every way the status check can refuse a frame carries the diagnostic code, and the
    /// Component&lt;TProps&gt; hint only appears for a null reference.
    /// </summary>
    [Theory]
    [InlineData(200, """{"renderError":null}""", null, false)]
    [InlineData(200, """{"renderError":"RootRender in Alert: NullReferenceException: boom"}""", "boom", true)]
    [InlineData(200, """{"renderError":"Reconcile: ArgumentOutOfRangeException: index"}""", "index", false)]
    [InlineData(200, """{"renderError":"Pending: the component switch has not rendered yet."}""", "Pending", false)]
    [InlineData(200, """{"building":false}""", "does not report", false)]
    [InlineData(200, "not json", "could not check", false)]
    [InlineData(500, "{}", "could not check", false)]
    public async Task Render_error_check_refuses_with_the_code(int status, string body, string? expected, bool hint)
    {
        using var http = new HttpClient(new StubHandler((HttpStatusCode)status, body));

        var refusal = await ScreenshotCapture.CheckRenderError(http, 4242, "Alert", TimeSpan.FromSeconds(5));

        if (expected is null)
        {
            Assert.Null(refusal);
            return;
        }
        Assert.NotNull(refusal);
        Assert.StartsWith(ScreenshotCapture.RenderErrorDiagnosticCode + ":", refusal);
        Assert.Contains(expected, refusal);
        Assert.Equal(hint, refusal.Contains("Component<TProps>", StringComparison.Ordinal));
    }

    /// <summary>
    /// Capture waits while the switch is pending and stops as soon as it is not, without
    /// waiting out the timeout.
    /// </summary>
    [Fact]
    public async Task Capture_waits_for_a_pending_switch_to_render()
    {
        const string pending = """{"renderError":"Pending: the component switch has not rendered yet."}""";
        using var handler = new StubHandler(HttpStatusCode.OK, pending, pending, """{"renderError":null}""");
        using var http = new HttpClient(handler, disposeHandler: false);

        var sw = global::System.Diagnostics.Stopwatch.StartNew();
        await ScreenshotCapture.WaitForSwitchRendered(http, 4242, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(10));

        Assert.Equal(3, handler.Requests);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"waited {sw.Elapsed.TotalSeconds:F1}s for a switch that rendered");
    }

    /// <summary>A switch that never renders is bounded by the timeout.</summary>
    [Fact]
    public async Task Capture_stops_waiting_for_a_switch_at_the_timeout()
    {
        using var handler = new StubHandler(HttpStatusCode.OK, """{"renderError":"Pending: the component switch has not rendered yet."}""");
        using var http = new HttpClient(handler, disposeHandler: false);

        var sw = global::System.Diagnostics.Stopwatch.StartNew();
        await ScreenshotCapture.WaitForSwitchRendered(http, 4242, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(10));

        Assert.True(handler.Requests > 1);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"waited {sw.Elapsed.TotalSeconds:F1}s against a 0.3s timeout");
    }

    /// <summary>Answers <c>/status</c> with each body in turn, repeating the last.</summary>
    private sealed class StubHandler(HttpStatusCode status, params string[] bodies) : HttpMessageHandler
    {
        private readonly List<HttpResponseMessage> _responses = [];

        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(ScreenshotCapture.StatusUrl(4242), request.RequestUri!.ToString());
            var body = bodies[Math.Min(Requests++, bodies.Length - 1)];
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            _responses.Add(response);
            return Task.FromResult(response);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var response in _responses) response.Dispose();
                _responses.Clear();
            }
            base.Dispose(disposing);
        }
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
