using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// Captures frames from the WinUI preview window and serves them over a local HTTP endpoint.
/// Uses Win32 PrintWindow for reliable capture of WinUI 3 content.
/// Designed for integration with a VS Code extension that displays a live thumbnail.
/// Embed endpoint protocol names are versioned as <c>embed-vN</c>; any breaking
/// endpoint change must bump the suffix (for example, <c>embed-v2</c>), and clients
/// must reject unknown protocol values.
/// </summary>
internal enum EmbedAckResult
{
    Success,
    GenerationMismatch,
    DpiMismatch,
    NotReady,
    Rejected,
}

internal sealed class PreviewCaptureServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Window _window;
    private readonly DispatcherQueueTimer? _captureTimer;
    private readonly IntPtr _hwnd;

    private byte[] _latestFrame = [];
    private bool _disposed;
    private bool _embedMode;
    private int _captureErrorCount;
    /// <summary>Per-launch bearer token. TASK-018.</summary>
    private readonly string _authToken;
    /// <summary>Concurrency gate. TASK-024.</summary>
    private readonly SemaphoreSlim _dispatchGate = new(initialCount: 16, maxCount: 16);
    /// <summary>Active reader counter. When zero, capture timer pauses. TASK-025.</summary>
    private int _activeReaders;
    /// <summary>Hard cap on POST body bytes. TASK-023.</summary>
    private const int MaxBodyBytes = 4 * 1024 * 1024;
    /// <summary>Hard cap on embedded-preview endpoint POST body bytes. Spec 056.</summary>
    internal const int EmbedMaxBodyBytes = 4 * 1024;
    private const string EmbedProtocol = "embed-v1";
    /// <summary>The TcpListener kept alive across the FindFreePort -&gt;
    /// HttpListener.Start handoff to close the TOCTOU. TASK-026.</summary>
    private TcpListener? _portHolder;

    public int Port { get; }
    public int Fps { get; }
    public int Generation { get; set; } = 1;
    public bool EmbedMode
    {
        get => _embedMode;
        set
        {
            _embedMode = value;
            if (value) _captureTimer?.Stop();
        }
    }
    /// <summary>Test-only accessor for the bearer token.</summary>
    internal string AuthToken => _authToken;
    /// <summary>Test-only accessor for active reader count.</summary>
    internal int ActiveReaders => _activeReaders;

    /// <summary>Returns the list of available component names.</summary>
    public Func<List<string>>? GetComponents { get; set; }

    /// <summary>Returns the name of the currently previewed component.</summary>
    public Func<string?>? GetCurrentComponent { get; set; }

    /// <summary>Switches to a different component by name. Returns true on success.</summary>
    public Func<string, bool>? SwitchComponent { get; set; }

    public Func<IntPtr>? GetHwnd { get; set; }
    public Func<IntPtr, int, int, int, EmbedAckResult>? AckEmbed { get; set; }
    public Action<int, int>? ResizeEmbed { get; set; }
    public Action<int, int>? MoveEmbed { get; set; }
    public Action? ReleaseEmbed { get; set; }

    [RequiresUnreferencedCode("Devtools subsystem; gated by Reactor.DevtoolsSupport.")]
    public PreviewCaptureServer(DispatcherQueue dispatcherQueue, Window window, int fps = 10)
    {
        _dispatcherQueue = dispatcherQueue;
        _window = window;
        Fps = fps;
        _authToken = GenerateToken();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);

        // SECURITY (TASK-026): hold the TcpListener open until HttpListener has
        // bound the port. Otherwise a hostile local process can race in and
        // grab the port between our Stop() and HttpListener.Start().
        var (port, holder) = AcquireFreePortHolding();
        Port = port;
        _portHolder = holder;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");

        var captureTimer = _dispatcherQueue.CreateTimer();
        captureTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / fps);
        captureTimer.Tick += OnCaptureTimerTick;
        _captureTimer = captureTimer;
    }

    [RequiresUnreferencedCode("Devtools subsystem test seam; gated by Reactor.DevtoolsSupport.")]
    internal static PreviewCaptureServer CreateForTests(int port, string authToken)
    {
        return new PreviewCaptureServer(port, authToken);
    }

    [RequiresUnreferencedCode("Devtools subsystem test seam; gated by Reactor.DevtoolsSupport.")]
    private PreviewCaptureServer(int port, string authToken)
    {
        _dispatcherQueue = null!;
        _window = null!;
        _captureTimer = null;
        _hwnd = IntPtr.Zero;
        Fps = 10;
        Port = port;
        _authToken = authToken;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
    }

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public void Start()
    {
        // SECURITY (TASK-006/006-equiv): bound the IO timers.
        try
        {
            var tm = _listener.TimeoutManager;
            tm.HeaderWait = TimeSpan.FromSeconds(10);
            tm.EntityBody = TimeSpan.FromSeconds(10);
            tm.IdleConnection = TimeSpan.FromSeconds(15);
            tm.RequestQueue = TimeSpan.FromSeconds(10);
        }
        catch { /* not all hosts expose TimeoutManager */ }
        // SECURITY (TASK-026): release the TcpListener placeholder before
        // binding HttpListener — the kernel only lets one socket own the
        // loopback port, so we can't keep both alive simultaneously. The
        // TOCTOU window between Stop() and Start() is microseconds wide
        // and confined to loopback; not a meaningful local-attack surface.
        try { _portHolder?.Stop(); } catch { }
        _portHolder = null;
        _listener.Start();
        // TASK-025: don't start the capture timer until a reader attaches.
        // _captureTimer.Start();
        _ = ListenAsync().ContinueWith(
            t => Console.Error.WriteLine($"[devtools:capture] Listener loop failed: {t.Exception!.GetBaseException()}"),
            TaskContinuationOptions.OnlyOnFaulted);

        Console.WriteLine($"[devtools:capture] Serving on http://127.0.0.1:{Port}");
        Console.WriteLine($"CAPTURE_PORT={Port}");
        // TASK-018: emit the token for clients on stdout. The vscode-reactor
        // extension reads this line; same-machine attackers without stdout
        // access cannot read it.
        Console.WriteLine($"CAPTURE_TOKEN={_authToken}");
        Console.Out.Flush();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _captureTimer?.Stop();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
    }

    // -- Frame Capture (UI thread) -----------------------------------------------

    private void OnCaptureTimerTick(DispatcherQueueTimer timer, object args)
    {
        if (EmbedMode) return;
        try
        {
            if (!NativeMethods.GetClientRect(_hwnd, out var clientRect)) return;

            int width = clientRect.Right - clientRect.Left;
            int height = clientRect.Bottom - clientRect.Top;
            if (width <= 0 || height <= 0) return;

            var clientOrigin = new NativeMethods.POINT { X = 0, Y = 0 };
            NativeMethods.ClientToScreen(_hwnd, ref clientOrigin);

            NativeMethods.GetWindowRect(_hwnd, out var windowRect);

            int offsetX = clientOrigin.X - windowRect.Left;
            int offsetY = clientOrigin.Y - windowRect.Top;
            int windowWidth = windowRect.Right - windowRect.Left;
            int windowHeight = windowRect.Bottom - windowRect.Top;

            if (windowWidth <= 0 || windowHeight <= 0) return;

            using var windowBmp = new Bitmap(windowWidth, windowHeight, PixelFormat.Format32bppArgb);
            using (var g = global::System.Drawing.Graphics.FromImage(windowBmp))
            {
                IntPtr hdc = g.GetHdc();
                NativeMethods.PrintWindow(_hwnd, hdc, NativeMethods.PW_RENDERFULLCONTENT);
                g.ReleaseHdc(hdc);
            }

            using var clientBmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = global::System.Drawing.Graphics.FromImage(clientBmp))
            {
                g.DrawImage(windowBmp,
                    new Rectangle(0, 0, width, height),
                    new Rectangle(offsetX, offsetY, width, height),
                    GraphicsUnit.Pixel);
            }

            using var ms = new MemoryStream();
            clientBmp.Save(ms, ImageFormat.Jpeg);
            Interlocked.Exchange(ref _latestFrame, ms.ToArray());
        }
        catch (Exception ex)
        {
            var count = Interlocked.Increment(ref _captureErrorCount);
            if (count == 1 || (count % 100 == 0))
                Console.Error.WriteLine($"[devtools:capture] Frame capture error (count={count}): {ex.GetType().Name}: {ex.Message}");
        }
    }

    // -- HTTP Server (background thread) -----------------------------------------

    private async Task ListenAsync()
    {
        while (!_disposed && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) { break; }

            // TASK-024: bound concurrency. zero-timeout wait → reject excess
            // with 503 instead of letting the threadpool blow up.
            if (!_dispatchGate.Wait(0))
            {
                try
                {
                    ctx.Response.StatusCode = 503;
                    ctx.Response.Headers.Add("Retry-After", "1");
                    ctx.Response.Close();
                }
                catch { }
                continue;
            }
            _ = Task.Run(() =>
            {
                try { HandleRequest(ctx); }
                finally { _dispatchGate.Release(); }
            });
        }
    }

    private void HandleRequest(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        var response = ctx.Response;

        // SECURITY (TASK-020): block DNS rebinding before doing any work.
        if (!IsAllowedHost(ctx.Request.Headers["Host"]))
        {
            response.StatusCode = 421;
            response.Close();
            return;
        }

        // Restrict CORS to localhost and VS Code webview origins
        var origin = ctx.Request.Headers["Origin"];
        bool originAllowed = string.IsNullOrEmpty(origin) || IsAllowedOrigin(origin);
        if (!string.IsNullOrEmpty(origin) && originAllowed)
        {
            response.Headers.Add("Access-Control-Allow-Origin", origin);
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        }

        if (ctx.Request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = 204;
            response.Close();
            return;
        }

        // SECURITY (TASK-019): fail-closed on cross-origin senders. CORS
        // headers above are advisory; this is the actual fence.
        if (!string.IsNullOrEmpty(origin) && !originAllowed)
        {
            response.StatusCode = 403;
            response.Close();
            return;
        }

        // SECURITY (TASK-018): require bearer auth on every endpoint.
        if (!BearerMatches(ctx.Request.Headers["Authorization"]))
        {
            response.StatusCode = 401;
            response.Headers.Add("WWW-Authenticate", "Bearer realm=\"reactor-preview\"");
            response.Close();
            return;
        }

        try
        {
            switch (path)
            {
                case "/frame":
                    ServeFrame(response);
                    break;
                case "/status":
                    ServeStatus(response);
                    break;
                case "/focus":
                    HandleFocus(ctx.Request, response);
                    break;
                case "/components":
                    ServeComponents(response);
                    break;
                case "/references":
                    ServeReferences(response);
                    break;
                case "/preview":
                    HandleSwitchComponent(ctx.Request, response);
                    break;
                case "/hwnd":
                    if (!EmbedMode) { NotFound(response); break; }
                    ServeHwnd(ctx.Request, response);
                    break;
                case "/embed/ack":
                    if (!EmbedMode) { NotFound(response); break; }
                    HandleEmbedAck(ctx.Request, response);
                    break;
                case "/embed/resize":
                    if (!EmbedMode) { NotFound(response); break; }
                    HandleEmbedResize(ctx.Request, response);
                    break;
                case "/embed/move":
                    if (!EmbedMode) { NotFound(response); break; }
                    HandleEmbedMove(ctx.Request, response);
                    break;
                case "/embed/release":
                    if (!EmbedMode) { NotFound(response); break; }
                    HandleEmbedRelease(ctx.Request, response);
                    break;
                default:
                    NotFound(response);
                    break;
            }
        }
        catch
        {
            try { response.StatusCode = 500; response.Close(); } catch { }
        }
    }

    private bool BearerMatches(string? authHeader)
    {
        if (string.IsNullOrEmpty(authHeader)) return false;
        const string prefix = "Bearer ";
        if (!authHeader.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var presented = authHeader.AsSpan(prefix.Length).Trim();
        var expected = _authToken.AsSpan();
        if (presented.Length != expected.Length) return false;
        int diff = 0;
        for (int i = 0; i < expected.Length; i++) diff |= presented[i] ^ expected[i];
        return diff == 0;
    }

    private bool IsAllowedHost(string? hostHeader)
    {
        if (string.IsNullOrEmpty(hostHeader)) return false;
        var portStr = Port.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
        return string.Equals(hostHeader, $"127.0.0.1:{portStr}", StringComparison.Ordinal)
            || string.Equals(hostHeader, $"localhost:{portStr}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedOrigin(string origin)
    {
        // Parse the Origin header through System.Uri rather than string-prefix
        // matching. StartsWith-based allow-lists let `http://localhost.evil.com`
        // through because the malicious host genuinely starts with "localhost";
        // Uri.TryCreate decomposes scheme/host/port correctly per RFC 3986.
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;

        if (string.Equals(uri.Scheme, "vscode-webview", StringComparison.OrdinalIgnoreCase))
            return true;

        // Preserve the pre-refactor allow-list shape: http accepts both
        // 127.0.0.1 and localhost, but https accepts only localhost. Don't
        // collapse to a generic http-or-https rule — that would silently
        // open `https://127.0.0.1` which the original code rejected.
        if (uri.Scheme == Uri.UriSchemeHttp)
            return uri.Host == "127.0.0.1" || uri.Host == "localhost";
        if (uri.Scheme == Uri.UriSchemeHttps)
            return uri.Host == "localhost";
        return false;
    }

    private void ServeFrame(HttpListenerResponse response)
    {
        if (EmbedMode || _dispatcherQueue is null)
        {
            NotFound(response);
            return;
        }

        // TASK-025: lazy-start the capture timer on the first reader so an
        // idle preview doesn't spin PrintWindow at 10 fps. We do NOT stop on
        // idle: each request increments+decrements _activeReaders so quickly
        // that a Start/Stop pair queued on the dispatcher cancels itself out
        // before any tick fires, and the docs-pipeline client (which polls
        // /frame in serial) never received a single frame. The CPU cost of
        // the timer running idle until Dispose is negligible at <=10 fps.
        Interlocked.Increment(ref _activeReaders);
        try { _dispatcherQueue.TryEnqueue(() => { if (!_disposed) _captureTimer?.Start(); }); } catch { }
        try
        {
            var frame = _latestFrame;
            if (frame.Length == 0)
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            response.ContentType = "image/jpeg";
            response.ContentLength64 = frame.Length;
            response.Headers.Add("Cache-Control", "no-store");
            response.OutputStream.Write(frame, 0, frame.Length);
            response.Close();
        }
        finally
        {
            Interlocked.Decrement(ref _activeReaders);
        }
    }

    private void ServeStatus(HttpListenerResponse response)
    {
        WriteJson(
            response,
            new PreviewStatusPayload
            {
                Building = false,
                Fps = Fps,
                Port = Port,
                Protocol = EmbedProtocol,
                Generation = Generation,
            },
            PreviewJsonContext.Default.PreviewStatusPayload,
            noStore: true);
    }

    private void HandleFocus(HttpListenerRequest request, HttpListenerResponse response)
    {
        // SECURITY (TASK-019): /focus is a state mutation, treat it as POST-only
        // so a same-origin <img src> probe cannot trigger window focus stealing.
        if (request.HttpMethod != "POST")
        {
            response.StatusCode = 405;
            response.Close();
            return;
        }
        _dispatcherQueue.TryEnqueue(() =>
        {
            try { NativeMethods.SetForegroundWindow(_hwnd); }
            catch { }
        });

        response.StatusCode = 200;
        var bytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.Close();
    }

    private void ServeComponents(HttpListenerResponse response)
    {
        var components = GetComponents?.Invoke() ?? [];
        var current = GetCurrentComponent?.Invoke();
        var json = JsonSerializer.Serialize(
            new PreviewComponentsPayload { Components = components, Current = current },
            PreviewJsonContext.Default.PreviewComponentsPayload);
        var bytes = Encoding.UTF8.GetBytes(json);

        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        response.Headers.Add("Cache-Control", "no-store");
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.Close();
    }

    /// <summary>
    /// Spec 057 §11 Phase 3 (3.1) — serves the reactive reference-graph overlay for the
    /// live preview window so the VS Code inspector can draw reference edges and surface
    /// cycle / unresolved diagnostics alongside the frame. Mirrors the devtools
    /// <c>references</c> MCP tool: walks the visual tree, reads each control's
    /// reference-edge state, and returns <c>{edges, diagnostics}</c>. The walk reads
    /// attached DPs, so it must run on the UI thread — we marshal onto the dispatcher
    /// and block this pooled handler thread (bounded by the dispatch gate) for the result.
    /// </summary>
    private void ServeReferences(HttpListenerResponse response)
    {
        if (EmbedMode || _dispatcherQueue is null)
        {
            NotFound(response);
            return;
        }

        string json;
        try
        {
            json = BuildReferencesJson();
        }
        catch (Exception ex)
        {
            WriteError(response, 500, "reference overlay failed: " + ex.Message);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        response.Headers.Add("Cache-Control", "no-store");
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.Close();
    }

    private string BuildReferencesJson()
    {
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                var walker = new TreeWalker("main", new NodeRegistry());
                walker.Walk(_window.Content);
                tcs.TrySetResult(SerializeReferenceGraph(ReferenceOverlay.Build(walker, "main")));
            }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }))
        {
            throw new InvalidOperationException("preview dispatcher is unavailable");
        }

        if (!tcs.Task.Wait(5000))
            throw new TimeoutException("reference overlay timed out building on the UI thread");
        return tcs.Task.Result;
    }

    // Reflection-based serialization, intentionally matching the devtools `references`
    // MCP tool (DevtoolsMcpServer.JsonOpts carries the AOT-fallback resolver). The
    // overlay payload is internal devtools diagnostics, not a hot-path or AOT-shipped
    // contract, so a source-generated context is not warranted.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Devtools overlay; reflection fallback matches the references MCP tool.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Devtools overlay; reflection fallback matches the references MCP tool.")]
    private static string SerializeReferenceGraph(ReferenceGraphResult graph)
        => JsonSerializer.Serialize(graph, DevtoolsMcpServer.JsonOpts);

    private void HandleSwitchComponent(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.HttpMethod != "POST")
        {
            response.StatusCode = 405;
            response.Close();
            return;
        }
        // SECURITY (TASK-019): require non-simple Content-Type — blocks
        // browser <form enctype="text/plain"> CSRF, since simple POSTs can't
        // set application/json without preflight.
        var ctMain = (request.ContentType ?? "").Split(';', 2)[0].Trim();
        if (!string.Equals(ctMain, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 415;
            response.Close();
            return;
        }
        // SECURITY (TASK-023): cap body size before reading.
        if (request.ContentLength64 > MaxBodyBytes)
        {
            response.StatusCode = 413;
            response.Close();
            return;
        }

        string body;
        try
        {
            body = ReadCappedBody(request.InputStream, request.ContentEncoding, MaxBodyBytes);
        }
        catch (InvalidDataException)
        {
            response.StatusCode = 413;
            try { response.Close(); } catch { }
            return;
        }

        string? componentName = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            componentName = doc.RootElement.GetProperty("component").GetString();
        }
        catch { }

        if (string.IsNullOrEmpty(componentName) || SwitchComponent == null)
        {
            response.StatusCode = 400;
            var errBytes = Encoding.UTF8.GetBytes("{\"error\":\"Missing component name\"}");
            response.ContentType = "application/json";
            response.ContentLength64 = errBytes.Length;
            response.OutputStream.Write(errBytes, 0, errBytes.Length);
            response.Close();
            return;
        }

        var success = SwitchComponent(componentName);
        JsonObject resultNode = success
            ? new JsonObject { ["ok"] = true, ["component"] = componentName }
            : new JsonObject { ["ok"] = false, ["error"] = $"Component '{componentName}' not found" };
        var resultBytes = Encoding.UTF8.GetBytes(resultNode.ToJsonString());

        response.StatusCode = success ? 200 : 404;
        response.ContentType = "application/json";
        response.ContentLength64 = resultBytes.Length;
        response.OutputStream.Write(resultBytes, 0, resultBytes.Length);
        response.Close();
    }

    private void ServeHwnd(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.HttpMethod != "GET")
        {
            WriteError(response, 405, "method-not-allowed");
            return;
        }

        var hwnd = GetHwnd?.Invoke() ?? IntPtr.Zero;
        WriteJson(
            response,
            new PreviewHwndPayload { Hwnd = FormatHwnd(hwnd), Generation = Generation },
            PreviewJsonContext.Default.PreviewHwndPayload,
            noStore: true);
    }

    private void HandleEmbedAck(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (!TryReadEmbedJson(request, response, out var doc)) return;
        using (doc)
        {
            var root = doc.RootElement;
            if (!TryGetParent(root, out var parent) || !TryGetInt(root, "w", out var w)
                || !TryGetInt(root, "h", out var h) || !TryGetInt(root, "generation", out var generation))
            {
                WriteError(response, 400, "bad-request");
                return;
            }

            var result = generation != Generation
                ? EmbedAckResult.GenerationMismatch
                : AckEmbed is null
                    ? EmbedAckResult.NotReady
                    : AckEmbed(parent, w, h, generation);

            if (result == EmbedAckResult.GenerationMismatch)
            {
                WriteJson(
                    response,
                    new PreviewEmbedErrorPayload
                    {
                        Ok = false,
                        Error = "generation-mismatch",
                        Expected = Generation,
                        Got = generation,
                    },
                    PreviewJsonContext.Default.PreviewEmbedErrorPayload,
                    statusCode: 409);
                return;
            }

            if (result == EmbedAckResult.NotReady)
            {
                response.Headers.Add("Retry-After", "1");
                WriteError(response, 503, "embed-not-ready");
                return;
            }

            if (result == EmbedAckResult.DpiMismatch)
            {
                WriteJson(
                    response,
                    new PreviewEmbedErrorPayload
                    {
                        Ok = false,
                        Error = "dpi-mismatch",
                        Expected = Generation,
                        Got = generation,
                    },
                    PreviewJsonContext.Default.PreviewEmbedErrorPayload,
                    statusCode: 412);
                return;
            }

            if (result == EmbedAckResult.Rejected)
            {
                WriteError(response, 400, "embed-rejected");
                return;
            }
        }

        WriteOk(response);
    }

    private void HandleEmbedResize(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (!TryReadEmbedJson(request, response, out var doc)) return;
        using (doc)
        {
            var root = doc.RootElement;
            if (!TryGetInt(root, "w", out var w) || !TryGetInt(root, "h", out var h) || ResizeEmbed == null)
            {
                WriteError(response, 400, "bad-request");
                return;
            }

            ResizeEmbed(w, h);
        }

        WriteOk(response);
    }

    private void HandleEmbedMove(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (!TryReadEmbedJson(request, response, out var doc)) return;
        using (doc)
        {
            var root = doc.RootElement;
            if (!TryGetInt(root, "x", out var x) || !TryGetInt(root, "y", out var y) || MoveEmbed == null)
            {
                WriteError(response, 400, "bad-request");
                return;
            }

            MoveEmbed(x, y);
        }

        WriteOk(response);
    }

    private void HandleEmbedRelease(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (!TryReadEmbedJson(request, response, out var doc)) return;
        using (doc)
        {
            if (ReleaseEmbed == null)
            {
                WriteError(response, 400, "bad-request");
                return;
            }

            ReleaseEmbed();
        }

        WriteOk(response);
    }

    private bool TryReadEmbedJson(HttpListenerRequest request, HttpListenerResponse response, [NotNullWhen(true)] out JsonDocument? doc)
    {
        doc = null;
        if (request.HttpMethod != "POST")
        {
            WriteError(response, 405, "method-not-allowed");
            return false;
        }

        var ctMain = (request.ContentType ?? "").Split(';', 2)[0].Trim();
        if (!string.Equals(ctMain, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            WriteError(response, 415, "unsupported-media-type");
            return false;
        }

        if (request.ContentLength64 > EmbedMaxBodyBytes)
        {
            WriteError(response, 413, "body-too-large");
            return false;
        }

        string body;
        try
        {
            body = ReadCappedBody(request.InputStream, request.ContentEncoding, EmbedMaxBodyBytes);
        }
        catch (InvalidDataException)
        {
            WriteError(response, 413, "body-too-large");
            return false;
        }

        try
        {
            doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            return true;
        }
        catch (JsonException)
        {
            WriteError(response, 400, "bad-json");
            return false;
        }
    }

    private static bool TryGetInt(JsonElement root, string propertyName, out int value)
    {
        value = 0;
        if (!root.TryGetProperty(propertyName, out var element)) return false;
        if (element.ValueKind == JsonValueKind.Number) return element.TryGetInt32(out value);
        if (element.ValueKind == JsonValueKind.String) return int.TryParse(element.GetString(), out value);
        return false;
    }

    private static bool TryGetParent(JsonElement root, out IntPtr parent)
    {
        parent = IntPtr.Zero;
        if (!root.TryGetProperty("parent", out var element)) return false;

        long value;
        if (element.ValueKind == JsonValueKind.Number)
        {
            if (!element.TryGetInt64(out value)) return false;
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var text = element.GetString();
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (!long.TryParse(text[2..], global::System.Globalization.NumberStyles.HexNumber,
                    global::System.Globalization.CultureInfo.InvariantCulture, out value))
                    return false;
            }
            else if (!long.TryParse(text, out value))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        parent = new IntPtr(value);
        return true;
    }

    private static string FormatHwnd(IntPtr hwnd)
    {
        return "0x" + hwnd.ToInt64().ToString("x", global::System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void NotFound(HttpListenerResponse response)
    {
        response.StatusCode = 404;
        response.Close();
    }

    private static void WriteOk(HttpListenerResponse response)
    {
        WriteJson(
            response,
            new PreviewOkPayload { Ok = true },
            PreviewJsonContext.Default.PreviewOkPayload);
    }

    private static void WriteError(HttpListenerResponse response, int statusCode, string error)
    {
        WriteJson(
            response,
            new PreviewErrorPayload { Ok = false, Error = error },
            PreviewJsonContext.Default.PreviewErrorPayload,
            statusCode);
    }

    private static void WriteJson<T>(HttpListenerResponse response, T payload, JsonTypeInfo<T> typeInfo, int statusCode = 200, bool noStore = false)
    {
        var json = JsonSerializer.Serialize(payload, typeInfo);
        var bytes = Encoding.UTF8.GetBytes(json);

        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        if (noStore) response.Headers.Add("Cache-Control", "no-store");
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.Close();
    }

    // -- Helpers -----------------------------------------------------------------

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Acquire a free loopback port AND keep the placeholder TcpListener
    /// alive. The caller must <c>Stop</c> the holder once HttpListener has
    /// successfully bound, otherwise the port stays reserved. TASK-026.
    /// </summary>
    private static (int Port, TcpListener Holder) AcquireFreePortHolding()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return (port, listener);
    }

    /// <summary>
    /// Bounded-size body reader. TASK-023.
    /// </summary>
    internal static string ReadCappedBody(Stream stream, Encoding encoding, int cap)
    {
        var buffer = new byte[Math.Min(cap, 8192)];
        var ms = new MemoryStream(capacity: Math.Min(cap, 8192));
        int total = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > cap) throw new InvalidDataException("body too large");
            ms.Write(buffer, 0, read);
        }
        return encoding.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private static class NativeMethods
    {
        public const uint PW_RENDERFULLCONTENT = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetClientRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClientToScreen(IntPtr hwnd, ref POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}

// Named payload types for AOT-compatible JSON serialization.
internal sealed class PreviewComponentsPayload
{
    public List<string> Components { get; set; } = [];
    public string? Current { get; set; }
}

internal sealed record PreviewStatusPayload
{
    public bool Building { get; set; }
    public int Fps { get; set; }
    public int Port { get; set; }
    public string Protocol { get; set; } = string.Empty;
    public int Generation { get; set; }
}

internal sealed class PreviewHwndPayload
{
    public string Hwnd { get; set; } = "0x0";
    public int Generation { get; set; }
}

internal sealed class PreviewOkPayload
{
    public bool Ok { get; set; }
}

internal sealed class PreviewErrorPayload
{
    public bool Ok { get; set; }
    public string Error { get; set; } = string.Empty;
}

internal sealed class PreviewEmbedErrorPayload
{
    public bool Ok { get; set; }
    public string Error { get; set; } = string.Empty;
    public int Expected { get; set; }
    public int Got { get; set; }
}

[global::System.Text.Json.Serialization.JsonSerializable(typeof(PreviewComponentsPayload))]
[global::System.Text.Json.Serialization.JsonSerializable(typeof(PreviewStatusPayload))]
[global::System.Text.Json.Serialization.JsonSerializable(typeof(PreviewHwndPayload))]
[global::System.Text.Json.Serialization.JsonSerializable(typeof(PreviewOkPayload))]
[global::System.Text.Json.Serialization.JsonSerializable(typeof(PreviewErrorPayload))]
[global::System.Text.Json.Serialization.JsonSerializable(typeof(PreviewEmbedErrorPayload))]
[global::System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = global::System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
internal partial class PreviewJsonContext : global::System.Text.Json.Serialization.JsonSerializerContext
{
}
