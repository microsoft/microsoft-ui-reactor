using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Captures frames from the WinUI preview window and serves them over a local HTTP endpoint.
/// Uses Win32 PrintWindow for reliable capture of WinUI 3 content.
/// Designed for integration with a VS Code extension that displays a live thumbnail.
/// </summary>
internal sealed class PreviewCaptureServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Window _window;
    private readonly DispatcherQueueTimer _captureTimer;
    private readonly IntPtr _hwnd;

    private byte[] _latestFrame = [];
    private bool _disposed;
    private int _captureErrorCount;
    /// <summary>Per-launch bearer token. TASK-018.</summary>
    private readonly string _authToken;
    /// <summary>Concurrency gate. TASK-024.</summary>
    private readonly SemaphoreSlim _dispatchGate = new(initialCount: 16, maxCount: 16);
    /// <summary>Active reader counter. When zero, capture timer pauses. TASK-025.</summary>
    private int _activeReaders;
    /// <summary>Hard cap on POST body bytes. TASK-023.</summary>
    private const int MaxBodyBytes = 4 * 1024 * 1024;
    /// <summary>The TcpListener kept alive across the FindFreePort -&gt;
    /// HttpListener.Start handoff to close the TOCTOU. TASK-026.</summary>
    private TcpListener? _portHolder;

    public int Port { get; }
    public int Fps { get; }
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

        _captureTimer = _dispatcherQueue.CreateTimer();
        _captureTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / fps);
        _captureTimer.Tick += OnCaptureTimerTick;
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
        _captureTimer.Stop();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
    }

    // -- Frame Capture (UI thread) -----------------------------------------------

    private void OnCaptureTimerTick(DispatcherQueueTimer timer, object args)
    {
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
                case "/preview":
                    HandleSwitchComponent(ctx.Request, response);
                    break;
                default:
                    response.StatusCode = 404;
                    response.Close();
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
        if (origin.StartsWith("vscode-webview://", StringComparison.OrdinalIgnoreCase)) return true;
        if (origin.StartsWith("http://127.0.0.1", StringComparison.Ordinal)) return true;
        if (origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (origin.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void ServeFrame(HttpListenerResponse response)
    {
        // TASK-025: lazy-start the capture timer on the first reader so an
        // idle preview doesn't spin PrintWindow at 10 fps. We do NOT stop on
        // idle: each request increments+decrements _activeReaders so quickly
        // that a Start/Stop pair queued on the dispatcher cancels itself out
        // before any tick fires, and the docs-pipeline client (which polls
        // /frame in serial) never received a single frame. The CPU cost of
        // the timer running idle until Dispose is negligible at <=10 fps.
        Interlocked.Increment(ref _activeReaders);
        try { _dispatcherQueue.TryEnqueue(() => { if (!_disposed) _captureTimer.Start(); }); } catch { }
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
        var json = $"{{\"building\":false,\"fps\":{Fps},\"port\":{Port}}}";
        var bytes = Encoding.UTF8.GetBytes(json);

        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        response.Headers.Add("Cache-Control", "no-store");
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.Close();
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

[global::System.Text.Json.Serialization.JsonSerializable(typeof(PreviewComponentsPayload))]
[global::System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNamingPolicy = global::System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
internal partial class PreviewJsonContext : global::System.Text.Json.Serialization.JsonSerializerContext
{
}
