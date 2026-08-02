using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Covers <see cref="ScreenshotCapture.PollForFrame"/>'s
/// <c>requireContent</c> hold-out.
/// </summary>
/// <remarks>
/// <para>
/// This is the branch that turns issue #989's mechanism from a corrupt commit
/// into a correct capture: the capture server starts its frame timer lazily and
/// a cold WinUI window's first delivered frame is routinely the unpainted
/// surface. Accepting it — which is what the code did before — writes a
/// solid-white PNG over a good committed screenshot and exits 0.
/// </para>
/// <para>
/// Driven over a real loopback socket rather than a mocked <c>HttpClient</c>
/// because the thing under test is a polling loop over HTTP responses; a fake
/// that returns byte arrays directly would skip the status-code and
/// empty-body handling that sit in the same loop. A raw
/// <see cref="TcpListener"/> is used rather than <c>HttpListener</c> so the
/// test needs no URL ACL reservation on Windows.
/// </para>
/// </remarks>
public class PollForFrameTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    private readonly ITestOutputHelper _output;

    public PollForFrameTests(ITestOutputHelper output) => _output = output;

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
    [Theory]
    [InlineData("frame")]
    [InlineData("preview")]
    public void Capture_urls_address_the_server_by_ip_literal(string endpoint)
    {
        var url = endpoint == "frame"
            ? ScreenshotCapture.FrameUrl(4242)
            : ScreenshotCapture.PreviewUrl(4242);
        var uri = new global::System.Uri(url);

        Assert.True(IPAddress.TryParse(uri.Host, out var ip),
            $"host '{uri.Host}' is a name, so the request depends on how it resolves — " +
            "the failure mode is a stalled IPv6 attempt that eats the whole deadline");
        Assert.Equal(IPAddress.Loopback, ip);
        Assert.Equal(4242, uri.Port);
        Assert.Equal($"/{endpoint}", uri.AbsolutePath);
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
    /// The whole point of the hold-out: a blank first frame must not be what
    /// gets written. Without <c>requireContent</c> the same server returns the
    /// blank frame, which is asserted below as the differential control — so
    /// this pair fails if the branch is deleted in either direction.
    /// </summary>
    [Fact]
    public async Task Blank_frames_are_skipped_until_a_painted_one_arrives()
    {
        var blank = SolidPng(60, 40, Color.White);
        var painted = PaintedPng(60, 40);

        using var server = new FrameServer([blank, blank, painted]);
        using var http = new HttpClient();

        var got = await ScreenshotCapture.PollForFrame(http, server.Port, Deadline, requireContent: true);

        _output.WriteLine($"accepted={server.Accepted} served={server.Served}");
        Assert.Null(server.Fault);
        Assert.Equal(painted, got);
        Assert.NotEqual(blank, got);
        // Exactly three requests: two blanks held out, then the painted frame.
        // Keyed on requests, not connections, so a stray probe connection
        // cannot shift the sequence.
        Assert.Equal(3, server.Served);
    }

    /// <summary>
    /// Differential control for the test above. Same server, same frames, one
    /// different argument — opposite answer. If the hold-out were removed the
    /// two tests would agree, and this one is what notices.
    /// </summary>
    [Fact]
    public async Task Without_the_hold_out_the_first_frame_wins_even_when_blank()
    {
        var blank = SolidPng(60, 40, Color.White);
        var painted = PaintedPng(60, 40);

        using var server = new FrameServer([blank, blank, painted]);
        using var http = new HttpClient();

        var got = await ScreenshotCapture.PollForFrame(http, server.Port, Deadline, requireContent: false);

        Assert.Null(server.Fault);
        Assert.Equal(blank, got);
    }

    /// <summary>
    /// When every frame is blank the last one is still returned, so the caller
    /// hits <c>BlankFrameException</c> — an accurate "the window never painted"
    /// — rather than an empty array reported as "no frame produced", which
    /// would send the reader looking at the transport instead of the app.
    /// </summary>
    [Fact]
    public async Task A_deadline_of_only_blank_frames_returns_the_last_blank_frame()
    {
        var blank = SolidPng(60, 40, Color.White);

        using var server = new FrameServer([blank]);
        using var http = new HttpClient();

        var got = await ScreenshotCapture.PollForFrame(
            http, server.Port, TimeSpan.FromMilliseconds(600), requireContent: true);

        Assert.Null(server.Fault);
        Assert.Equal(blank, got);
        Assert.NotEmpty(got);
        Assert.Throws<BlankFrameException>(
            () => ImageProcessor.Process(got, ImageProcessor.ParseCropMode("content")));
    }

    /// <summary>
    /// The other half of the deadline story, and the one the doc comment used
    /// to elide: when the server never produced a frame at all, the result is
    /// empty and the caller reports "no frame produced".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comment on <c>requireContent</c> claimed the poller returns the last
    /// blank frame "rather than a misleading 'no frame produced'", which reads
    /// as a guarantee that this path cannot occur. It can: <c>lastBytes</c> is
    /// only ever assigned from a 200 with a non-empty body, so a server stuck
    /// on 204 leaves it empty. The behaviour is right — "no frame produced" is
    /// the accurate answer when none was — but the sentence was broader than
    /// the code, so it is now pinned rather than described.
    /// </para>
    /// <para>
    /// Non-vacuous against its sibling above: that test and this one differ
    /// only in whether the server ever emits a body, and they assert opposite
    /// results. A poller that returned empty unconditionally would fail that
    /// one; a poller that synthesised a frame would fail this one.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_deadline_with_no_frame_at_all_returns_empty()
    {
        using var server = new FrameServer([global::System.Array.Empty<byte>()]);
        using var http = new HttpClient();

        var got = await ScreenshotCapture.PollForFrame(
            http, server.Port, TimeSpan.FromMilliseconds(600), requireContent: true);

        Assert.Null(server.Fault);
        Assert.Empty(got);

        // Premise guard: an empty result is also what a server that was never
        // reached would produce, and that would be a transport failure rather
        // than the case under test.
        _output.WriteLine($"served={server.Served}");
        Assert.True(server.Served > 0, "the poller never reached the server — this asserts nothing about the 204 path");
    }

    /// <summary>
    /// The deadline has to bound the whole call, not just the gaps between
    /// polls. A server that accepts the connection and never answers used to
    /// leave the request bounded only by <see cref="HttpClient"/>'s 100-second
    /// default — twenty times the 5-second deadline capture actually passes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle is elapsed time, and it can come out the other way by a wide
    /// margin: unguarded this call takes ~100 s, guarded it takes ~0.5 s, and
    /// the bound below sits an order of magnitude from each. So the assertion
    /// turns on the per-request token and nothing about machine speed — no
    /// plausible scheduling delay closes a 100× gap.
    /// </para>
    /// <para>
    /// The premise guard matters more than usual here. An empty result is also
    /// what a connection refused outright would produce, and that would satisfy
    /// the timing assertion trivially while testing nothing — the request has
    /// to actually be *in flight* and abandoned for this to mean anything.
    /// Asserting the listener accepted the connection is what separates the two.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_stalled_server_does_not_outlive_the_deadline()
    {
        using var stall = new StallingListener();
        using var http = new HttpClient();
        var deadline = TimeSpan.FromMilliseconds(500);

        var sw = global::System.Diagnostics.Stopwatch.StartNew();
        var got = await ScreenshotCapture.PollForFrame(
            http, stall.Port, deadline, requireContent: true);
        sw.Stop();

        _output.WriteLine($"elapsed={sw.Elapsed.TotalSeconds:F2}s accepted={stall.Accepted} " +
                          $"(deadline={deadline.TotalSeconds:F2}s, HttpClient default={http.Timeout.TotalSeconds:F0}s)");

        Assert.True(stall.Accepted > 0,
            "the listener was never reached, so nothing was ever stalled — this asserts nothing");
        // Must be read before Dispose cancels the token — see StallingListener for why
        Assert.Null(stall.Fault);
        Assert.Empty(got);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10),
            $"the deadline did not bound the request: {sw.Elapsed.TotalSeconds:F1}s elapsed " +
            $"against a {deadline.TotalSeconds:F1}s deadline");
    }

    /// <summary>
    /// The same bound, on the other request the capture client makes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ScreenshotCapture.PollForFrame"/> was given a per-request
    /// deadline; the component switch shares the <see cref="HttpClient"/> and was
    /// not. That matters more, not less: the switch runs <em>once per screenshot</em>,
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
            // Loopback-only, mirroring PreviewCaptureServer — see FrameServer.
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoop);
        }

        public int Port { get; }

        public int Accepted => Volatile.Read(ref _accepted);

        /// <summary>
        /// First exception the accept loop hit that was <em>not</em> explained by
        /// teardown, or null. Same reasoning as <c>FrameServer.Fault</c>: the loop
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

    /// <summary>
    /// A 204 is the real capture server's "timer hasn't started yet" reply
    /// (<c>PreviewCaptureServer</c> sends it whenever <c>_latestFrame</c> is
    /// empty) and must not be mistaken for a frame.
    /// </summary>
    /// <remarks>
    /// This pins the production-realistic shape, which the fixture could not
    /// even emit before — it answered every request 200. It does <em>not</em>
    /// isolate the <c>StatusCode == OK</c> branch: a 204 carries no body by
    /// definition, so deleting that check leaves the empty response to be
    /// rejected by the <c>bytes.Length &gt; 0</c> arm and the poller behaves
    /// identically. <see cref="An_error_status_carrying_a_body_is_not_returned_as_a_frame"/>
    /// is the test that separates them.
    /// </remarks>
    [Fact]
    public async Task No_content_responses_are_not_treated_as_frames()
    {
        var painted = PaintedPng(60, 40);

        using var server = new FrameServer([[], [], painted]);
        using var http = new HttpClient();

        var got = await ScreenshotCapture.PollForFrame(http, server.Port, Deadline, requireContent: true);

        Assert.Null(server.Fault);
        Assert.Equal(painted, got);
        // The point of this test over the empty-200 one: a 204 really went out.
        Assert.Equal([204, 204, 200], server.Statuses);
    }

    /// <summary>
    /// A 200 carrying a zero-length body is not something the real server
    /// produces — it writes the frame only after checking the length — so this
    /// covers the defensive <c>bytes.Length &gt; 0</c> arm against a server
    /// that claims success and sends nothing.
    /// </summary>
    [Fact]
    public async Task Empty_200_body_is_not_treated_as_a_frame()
    {
        var painted = PaintedPng(60, 40);

        using var server = new FrameServer([[], [], painted], emptyAsNoContent: false);
        using var http = new HttpClient();

        var got = await ScreenshotCapture.PollForFrame(http, server.Port, Deadline, requireContent: true);

        Assert.Null(server.Fault);
        Assert.Equal(painted, got);
        // No 204 anywhere: this is the arm that catches a server claiming
        // success and sending nothing, which is a different defect.
        Assert.Equal([200, 200, 200], server.Statuses);
    }

    /// <summary>
    /// An error status must never be read as a frame even though it carries a
    /// body: <c>PreviewCaptureServer</c> answers 401/403/404/503 through
    /// <c>WriteError</c>, which writes a JSON payload, so "has bytes" is true
    /// for every one of them and only the status distinguishes them from a
    /// real capture.
    /// </summary>
    /// <remarks>
    /// Deliberately polls with <c>requireContent: false</c>, which returns the
    /// first non-empty body. That is what makes this test discriminating:
    /// delete the <c>StatusCode == OK</c> guard and the poller hands back the
    /// error JSON, which then reaches <c>ImageProcessor</c> as if it were image
    /// bytes. With <c>requireContent: true</c> the content scan would reject
    /// the JSON for its own reasons and the assertion would pass either way.
    /// </remarks>
    [Fact]
    public async Task An_error_status_carrying_a_body_is_not_returned_as_a_frame()
    {
        var errorJson = Encoding.UTF8.GetBytes("{\"ok\":false,\"error\":\"unauthorized\"}");
        var painted = PaintedPng(60, 40);

        using var server = new FrameServer(
            [new Reply(403, errorJson), new Reply(403, errorJson), new Reply(200, painted)]);
        using var http = new HttpClient();

        var got = await ScreenshotCapture.PollForFrame(http, server.Port, Deadline);

        Assert.Null(server.Fault);
        Assert.Equal(painted, got);
    }

    private static byte[] SolidPng(int w, int h, Color color)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.Clear(color);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] PaintedPng(int w, int h)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            using var ink = new SolidBrush(Color.FromArgb(20, 20, 20));
            g.FillRectangle(ink, w / 4, h / 4, w / 2, h / 2);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>A scripted HTTP reply: the status line to send and the body to send with it.</summary>
    private readonly record struct Reply(int Status, byte[] Body);

    /// <summary>
    /// Minimal HTTP/1.1 server that answers each <c>GET</c> with the next body in
    /// a scripted sequence, sticking on the last one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A body slot is consumed only once a complete request head (<c>\r\n\r\n</c>)
    /// has actually been read. An earlier version keyed the response on the
    /// accepted-connection ordinal instead, and any connection that carried no
    /// request silently shifted the whole sequence by one. That made the fixture
    /// report "the poller accepted a blank frame" when the poller had done
    /// nothing wrong. Keying on a parsed request removes the ambiguity: a
    /// connection with no request gets no body and advances nothing.
    /// </para>
    /// <para>
    /// Binds <see cref="IPAddress.Loopback"/> — IPv4, loopback only — because
    /// that is exactly what <c>PreviewCaptureServer</c> binds in production. The
    /// fidelity is the point: a fixture that accepts addresses production does
    /// not cannot observe a client/server address mismatch, which is the defect
    /// that took this class from green to seven failures. Making the fixture
    /// dual-stack would turn those failures green while leaving real captures
    /// broken on the same machine.
    /// </para>
    /// <para>
    /// There is no loopback-only dual-stack bind to reach for as a compromise:
    /// <c>DualMode</c> maps IPv4 only under <c>IPv6Any</c>, which binds
    /// <c>::</c> — every interface — so the fixture would accept connections
    /// from the network. Measured, not assumed. The address mismatch belongs to
    /// the client, and that is where it is fixed.
    /// </para>
    /// </remarks>
    private sealed class FrameServer : global::System.IDisposable
    {
        private readonly TcpListener _listener;
        private readonly IReadOnlyList<Reply> _replies;
        private readonly CancellationTokenSource _cts = new();
        private int _served;
        private int _accepted;
        private Exception? _fault;
        private readonly ConcurrentQueue<int> _statuses = new();

        /// <param name="emptyAsNoContent">
        /// When true (the default) an empty body slot is answered with
        /// <c>204 No Content</c>, matching <c>PreviewCaptureServer</c>, which
        /// replies 204 whenever no frame has been captured yet and only ever
        /// sends 200 with real bytes. Pass false to answer <c>200</c> with a
        /// zero-length body — a response the real server cannot produce, used
        /// to cover the poller's defensive length check.
        /// </param>
        public FrameServer(IReadOnlyList<byte[]> bodies, bool emptyAsNoContent = true)
            : this(Script(bodies, emptyAsNoContent))
        {
        }

        public FrameServer(IReadOnlyList<Reply> replies)
        {
            _replies = replies;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(AcceptLoop);
        }

        private static IReadOnlyList<Reply> Script(IReadOnlyList<byte[]> bodies, bool emptyAsNoContent)
        {
            var replies = new Reply[bodies.Count];
            for (var i = 0; i < bodies.Count; i++)
                replies[i] = new Reply(
                    bodies[i].Length == 0 && emptyAsNoContent ? 204 : 200, bodies[i]);
            return replies;
        }

        private static string ReasonPhrase(int status) => status switch
        {
            200 => "OK",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "Not Found",
            503 => "Service Unavailable",
            _ => "Status",
        };

        public int Port { get; }

        /// <summary>Number of complete requests answered, not connections accepted.</summary>
        public int Served => Volatile.Read(ref _served);

        /// <summary>
        /// Connections accepted. Reported alongside <see cref="Served"/> so a
        /// future failure shows its own input: if these differ, the client
        /// opened a connection that carried no request and the old
        /// ordinal-keyed fixture would have mis-served the sequence.
        /// </summary>
        public int Accepted => Volatile.Read(ref _accepted);

        /// <summary>
        /// Status codes actually written to the wire, in order.
        /// </summary>
        /// <remarks>
        /// Exists so a test that claims to cover a status code has to show one.
        /// <see cref="No_content_responses_are_not_treated_as_frames"/> and
        /// <see cref="Empty_200_body_is_not_treated_as_a_frame"/> are otherwise
        /// byte-identical apart from the <c>emptyAsNoContent</c> argument, and
        /// the poller answers both the same way — a 204 carries no body, so the
        /// <c>bytes.Length &gt; 0</c> arm rejects it whether or not the status
        /// check exists. Without this, breaking the 204 mapping in
        /// <see cref="Script"/> would leave both tests green, both sending 200,
        /// and the one named for 204 still appearing to guard it. Asserting the
        /// wire shape is what makes the two tests different from each other.
        /// </remarks>
        public IReadOnlyList<int> Statuses => [.. _statuses];

        /// <summary>
        /// First exception the accept loop hit that was <em>not</em> explained by
        /// teardown, or null. The loop runs detached on a background task, so
        /// without this a genuine socket/IO fault would be swallowed and the test
        /// would report only the downstream symptom — a served count that is
        /// mysteriously short — with no trace of the cause. Tests assert this is
        /// null, which is what makes the shutdown catches safe to keep quiet.
        /// </summary>
        public Exception? Fault => Volatile.Read(ref _fault);

        private async Task AcceptLoop()
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    using var stream = client.GetStream();
                    Interlocked.Increment(ref _accepted);

                    if (!await ReadRequestHead(stream)) continue;

                    var index = Interlocked.Increment(ref _served) - 1;
                    var reply = _replies[global::System.Math.Min(index, _replies.Count - 1)];
                    var body = reply.Body;
                    _statuses.Enqueue(reply.Status);

                    // A 204 carries no body and, per RFC 7230 section 3.3.2,
                    // no Content-Length — so the status line and Connection
                    // header are the whole response. Error statuses mirror the
                    // real server, which answers them through WriteError with a
                    // JSON payload.
                    var header = Encoding.ASCII.GetBytes(
                        reply.Status == 204
                            ? "HTTP/1.1 204 No Content\r\n" +
                              "Connection: close\r\n\r\n"
                            : $"HTTP/1.1 {reply.Status} {ReasonPhrase(reply.Status)}\r\n" +
                              $"Content-Type: {(reply.Status == 200 ? "image/png" : "application/json")}\r\n" +
                              $"Content-Length: {body.Length}\r\n" +
                              "Connection: close\r\n\r\n");
                    await stream.WriteAsync(header, _cts.Token);
                    if (body.Length > 0) await stream.WriteAsync(body, _cts.Token);
                    await stream.FlushAsync(_cts.Token);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException
                                          or global::System.IO.IOException
                                          or global::System.ObjectDisposedException)
            {
                // These four are exactly what Dispose() produces: cancelling the
                // token, then stopping the listener, races the pending accept and
                // any in-flight write. Silent only when teardown explains them —
                // outside teardown the same exception means a real transport
                // failure, and the loop is detached, so it is recorded for the
                // test to assert on rather than dropped.
                if (!_cts.IsCancellationRequested)
                    Interlocked.CompareExchange(ref _fault, ex, null);
            }
        }

        /// <summary>
        /// Reads until the end of the request head. False when the peer closed
        /// without sending one, which must not consume a body slot.
        /// </summary>
        private async Task<bool> ReadRequestHead(NetworkStream stream)
        {
            var buf = new byte[1024];
            var head = new StringBuilder();
            while (!_cts.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buf, _cts.Token);
                if (read == 0) return false;
                head.Append(Encoding.ASCII.GetString(buf, 0, read));
                if (head.ToString().Contains("\r\n\r\n", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }
}
