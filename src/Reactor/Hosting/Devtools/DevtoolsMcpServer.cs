using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// MCP server exposed on a loopback HTTP endpoint. Accepts JSON-RPC 2.0 POSTs at
/// <c>/mcp</c>. One method per MCP tool; <c>tools/list</c> returns the inventory
/// and <c>tools/call</c> dispatches by name. Spec §6, §17 Phase 2.
/// </summary>
internal sealed class DevtoolsMcpServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Window _window;
    private readonly McpToolRegistry _tools;
    private readonly string _buildTag;
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly DevtoolsLogger? _logger;
    private readonly McpTransport _transport;
    private readonly string? _projectIdentifier;
    private string? _lockfilePath;
    private StdioMcpLoop? _stdioLoop;
    private bool _disposed;
    /// <summary>
    /// Per-launch random bearer token. Anyone who can read the lockfile (this
    /// user's tempdir) authenticates; anyone who can't is rejected. TASK-001.
    /// </summary>
    private readonly string _authToken;
    /// <summary>
    /// Hard cap on accepted concurrent dispatches. Any request past this gets
    /// 503; without it, a single noisy client can saturate the threadpool.
    /// TASK-007.
    /// </summary>
    private readonly SemaphoreSlim _dispatchGate = new(initialCount: 16, maxCount: 16);

    public int Port { get; }
    public McpToolRegistry Tools => _tools;
    public string BuildTag => _buildTag;
    public DispatcherQueue DispatcherQueue => _dispatcherQueue;
    public Window Window => _window;
    public McpTransport Transport => _transport;
    /// <summary>Test-only accessor for the bearer token.</summary>
    internal string AuthToken => _authToken;
    internal DevtoolsLogger? Logger => _logger;
    /// <summary>Hard request body cap. TASK-006.</summary>
    internal const int MaxRequestBodyBytes = 1 * 1024 * 1024; // 1 MiB

    /// <summary>
    /// Routes banner/announcement lines. When stdio is the active MCP
    /// transport, stdout is reserved for JSON-RPC framing so everything else
    /// has to go to stderr — otherwise we'd corrupt the agent's message stream.
    /// </summary>
    private TextWriter BannerWriter =>
        _transport == McpTransport.Stdio ? Console.Error : Console.Out;

    public DevtoolsMcpServer(
        DispatcherQueue dispatcherQueue,
        Window window,
        int? preferredPort = null,
        DevtoolsLogger? logger = null,
        McpTransport transport = McpTransport.Http,
        string? projectIdentifier = null)
    {
        _dispatcherQueue = dispatcherQueue;
        _window = window;
        _tools = new McpToolRegistry();
        _buildTag = ResolveBuildTag();
        _logger = logger;
        _transport = transport;
        _projectIdentifier = projectIdentifier;
        _authToken = GenerateToken();

        Port = preferredPort ?? FindFreePort();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
    }

    /// <summary>
    /// Single-instance check per spec §6. Called before <see cref="Start"/> by
    /// the host bring-up. Returns true when a live lockfile names another
    /// session for the same project; emits the one stderr line the spec
    /// mandates. When the existing lockfile is stale it's silently deleted
    /// and the method returns false.
    /// </summary>
    public static bool IsAnotherSessionActive(string projectIdentifier, out LockfileEntry? existing)
    {
        existing = null;
        var path = LockfileRegistry.PathFor(projectIdentifier);
        if (!LockfileRegistry.TryRead(path, out var entry) || entry is null) return false;
        if (LockfileRegistry.IsLive(entry))
        {
            existing = entry;
            return true;
        }
        LockfileRegistry.TryDelete(path);
        return false;
    }

    public void Start()
    {
        if (_transport == McpTransport.Http)
        {
            // SECURITY (TASK-006): bound the IO timers so a slow-loris client
            // can't park a worker indefinitely. Defaults are minutes; cap at
            // 10s so any single request fits well inside the dispatch budget.
            try
            {
                var tm = _listener.TimeoutManager;
                tm.HeaderWait = TimeSpan.FromSeconds(10);
                tm.EntityBody = TimeSpan.FromSeconds(10);
                tm.IdleConnection = TimeSpan.FromSeconds(15);
                tm.RequestQueue = TimeSpan.FromSeconds(10);
            }
            catch
            {
                // TimeoutManager is unavailable on some hosts; the per-read
                // ContentLength64 cap below is the load-bearing protection.
            }
            _listener.Start();
            _ = ListenAsync().ContinueWith(
                t => Console.Error.WriteLine($"[devtools:mcp] Listener loop failed: {t.Exception!.GetBaseException()}"),
                TaskContinuationOptions.OnlyOnFaulted);

            BannerWriter.WriteLine($"[devtools] MCP serving on http://127.0.0.1:{Port}/mcp");
            BannerWriter.WriteLine($"MCP_TRANSPORT=http");
            BannerWriter.WriteLine($"MCP_ENDPOINT=http://127.0.0.1:{Port}/mcp");
            BannerWriter.WriteLine($"MCP_PORT={Port}");
        }
        else // Stdio
        {
            // Bypass Console.In/Out: LogCaptureInstall replaces Console.Out
            // with a TeeTextWriter that doesn't forward to the pipe in stdio
            // mode (so app writes don't corrupt the JSON-RPC frame). Use the
            // raw stdin/stdout streams directly so JSON-RPC always reaches
            // the parent process regardless of capture state.
            var stdinReader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            var stdoutWriter = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = false };
            _stdioLoop = new StdioMcpLoop(
                new McpDispatcher(_tools, _logger),
                stdinReader,
                stdoutWriter);
            _stdioLoop.Start();

            // Stdio banner goes to stderr so stdout stays clean for framing.
            BannerWriter.WriteLine($"[devtools] MCP serving over stdio");
            BannerWriter.WriteLine($"MCP_TRANSPORT=stdio");
        }
        BannerWriter.Flush();
    }

    /// <summary>
    /// Emits the one-time <c>[devtools] ready</c> line after the first render
    /// completes. Callers invoke this from the reconciler's first-commit hook.
    /// </summary>
    public void AnnounceReady()
    {
        BannerWriter.WriteLine($"[devtools] ready (build {_buildTag})");
        // Machine-readable sibling: one JSON line an agent harness can regex
        // or line-parse for without re-parsing the human banners. Fields are
        // stable by contract; add new ones, never rename.
        var transportStr = _transport == McpTransport.Http ? "http" : "stdio";
        var endpoint = _transport == McpTransport.Http
            ? $"http://127.0.0.1:{Port}/mcp"
            : "stdio";
        var pid = global::System.Diagnostics.Process.GetCurrentProcess().Id;
        var readyNode = new JsonObject
        {
            ["event"] = "devtools-ready",
            ["endpoint"] = endpoint,
            ["transport"] = transportStr,
            ["port"] = Port,
            ["pid"] = pid,
            ["buildTag"] = _buildTag,
        };
        BannerWriter.WriteLine(readyNode.ToJsonString());
        BannerWriter.Flush();

        WriteLockfile();
    }

    private void WriteLockfile()
    {
        if (string.IsNullOrEmpty(_projectIdentifier)) return;
        try
        {
            var path = LockfileRegistry.PathFor(_projectIdentifier);
            var transportStr = _transport == McpTransport.Http ? "http" : "stdio";
            var endpoint = _transport == McpTransport.Http
                ? $"http://127.0.0.1:{Port}/mcp"
                : "stdio";
            var entry = new LockfileEntry
            {
                Schema = LockfileRegistry.SchemaTag,
                Endpoint = endpoint,
                Transport = transportStr,
                Port = Port,
                Pid = global::System.Diagnostics.Process.GetCurrentProcess().Id,
                BuildTag = _buildTag,
                Project = _projectIdentifier,
                StartedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Token = _authToken,
            };
            LockfileRegistry.Write(path, entry);
            _lockfilePath = path;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[devtools] Failed to write lockfile: {ex.Message}");
        }
    }

    /// <summary>
    /// 256-bit random token, base64url-encoded. Constant-time-comparable.
    /// </summary>
    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private bool BearerMatches(string? authHeader)
    {
        if (string.IsNullOrEmpty(authHeader)) return false;
        const string prefix = "Bearer ";
        if (!authHeader.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var presented = authHeader.AsSpan(prefix.Length).Trim();
        var expected = _authToken.AsSpan();
        if (presented.Length != expected.Length) return false;
        // Constant-time compare to avoid timing side-channels on token length.
        int diff = 0;
        for (int i = 0; i < expected.Length; i++) diff |= presented[i] ^ expected[i];
        return diff == 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdownCts.Cancel();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        try { _stdioLoop?.Dispose(); } catch { }
        try { _logger?.Dispose(); } catch { }
        if (!string.IsNullOrEmpty(_lockfilePath))
            LockfileRegistry.TryDelete(_lockfilePath);
    }

    // -- HTTP Loop ---------------------------------------------------------------

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

            // SECURITY (TASK-007): bound concurrent dispatch. WaitOne with zero
            // timeout — if all 16 slots are in use, fail fast with 503. Doing
            // it here, before Task.Run, prevents queue blow-up.
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

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "JSON serialization for MCP HTTP request/response handling.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "JSON serialization for MCP HTTP request/response handling.")]
    private void HandleRequest(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "/";
        var response = ctx.Response;

        // SECURITY (TASK-003): block DNS rebinding by validating the Host
        // header before doing any work. Anything that isn't loopback gets
        // 421 Misdirected Request — a rebound attacker page that resolves
        // back to localhost still needs to send Host: <attacker>:<port>.
        if (!IsAllowedHost(ctx.Request.Headers["Host"]))
        {
            response.StatusCode = 421;
            response.Close();
            return;
        }

        if (ctx.Request.HttpMethod == "OPTIONS")
        {
            response.Headers.Add("Access-Control-Allow-Origin", "http://127.0.0.1");
            response.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
            response.StatusCode = 204;
            response.Close();
            return;
        }

        if (!string.Equals(path, "/mcp", StringComparison.Ordinal))
        {
            response.StatusCode = 404;
            response.Close();
            return;
        }

        // SECURITY (TASK-001): every method on /mcp requires a valid bearer.
        // The token lives in the lockfile; only same-user processes can read
        // it, so this is what gates same-machine-different-process access.
        if (!BearerMatches(ctx.Request.Headers["Authorization"]))
        {
            response.StatusCode = 401;
            response.Headers.Add("WWW-Authenticate", "Bearer realm=\"reactor-devtools\"");
            response.Close();
            return;
        }

        // GET /mcp — self-describing schema endpoint. Returns the tool inventory,
        // selector grammar, schema version, and protocol version in one payload so
        // an agent can orient itself with a single curl / browser visit without
        // crafting a JSON-RPC initialize + tools/list dance first.
        if (ctx.Request.HttpMethod == "GET")
        {
            var schemaDoc = BuildSchemaDocument();
            var schemaJson = JsonSerializer.Serialize(schemaDoc, JsonOpts);
            var schemaBytes = Encoding.UTF8.GetBytes(schemaJson);
            response.ContentType = "application/json";
            response.ContentLength64 = schemaBytes.Length;
            response.StatusCode = 200;
            try { response.OutputStream.Write(schemaBytes, 0, schemaBytes.Length); } catch { }
            finally { try { response.Close(); } catch { } }
            return;
        }

        if (ctx.Request.HttpMethod != "POST")
        {
            response.StatusCode = 405;
            response.Close();
            return;
        }

        // SECURITY (TASK-002): require a non-simple Content-Type. A browser
        // form/<img>/text-plain POST cannot satisfy this, blocking CSRF without
        // requiring a preflight.
        var contentType = ctx.Request.ContentType ?? "";
        var ctMain = contentType.Split(';', 2)[0].Trim();
        if (!string.Equals(ctMain, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 415;
            response.Close();
            return;
        }
        // Origin allowlist: vscode-webview, loopback, or absent (CLI/curl with
        // valid bearer). Reject explicit cross-origin senders.
        var origin = ctx.Request.Headers["Origin"];
        if (!string.IsNullOrEmpty(origin) && !IsAllowedOrigin(origin))
        {
            response.StatusCode = 403;
            response.Close();
            return;
        }

        // SECURITY (TASK-006): cap body size before we read. ContentLength64
        // is the advertised length; reject anything over the cap. We also
        // bound the actual read so chunked/lying clients can't slip past.
        var advertised = ctx.Request.ContentLength64;
        if (advertised > MaxRequestBodyBytes)
        {
            response.StatusCode = 413;
            response.Close();
            return;
        }

        string body;
        try
        {
            body = ReadCappedBody(ctx.Request.InputStream, ctx.Request.ContentEncoding, MaxRequestBodyBytes);
        }
        catch (InvalidDataException)
        {
            response.StatusCode = 413;
            try { response.Close(); } catch { }
            return;
        }
        catch
        {
            response.StatusCode = 400;
            try { response.Close(); } catch { }
            return;
        }

        var responsePayload = DispatchRpc(body);
        var json = JsonSerializer.Serialize(responsePayload, JsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);

        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        response.StatusCode = 200;
        try
        {
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch { }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    /// <summary>
    /// Reads <paramref name="stream"/> into a string while strictly capping the
    /// total byte count. Throws <see cref="InvalidDataException"/> if the
    /// stream produces more than <paramref name="cap"/> bytes.
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

    private bool IsAllowedHost(string? hostHeader)
    {
        if (string.IsNullOrEmpty(hostHeader)) return false;
        // The HttpListener prefix already binds 127.0.0.1, so any TCP packet
        // reaching us came in via loopback. Still validate the Host header
        // string defensively against rebinding-style attacks.
        var portStr = Port.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
        return string.Equals(hostHeader, $"127.0.0.1:{portStr}", StringComparison.Ordinal)
            || string.Equals(hostHeader, $"localhost:{portStr}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowedOrigin(string origin)
    {
        // Parse via System.Uri rather than string-prefix matching. A
        // StartsWith allow-list would let `http://localhost.evil.com` through
        // because the malicious host genuinely starts with "localhost";
        // Uri.TryCreate decomposes scheme/host correctly per RFC 3986.
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;

        if (string.Equals(uri.Scheme, "vscode-webview", StringComparison.OrdinalIgnoreCase))
            return true;

        // MCP traffic is HTTP only — no https:// arm (HttpListener binds to
        // http://127.0.0.1:<port>, so an https origin would never originate
        // from this server's own callers).
        if (uri.Scheme != Uri.UriSchemeHttp) return false;

        return uri.Host == "localhost" || uri.Host == "127.0.0.1";
    }

    // -- Dispatch ----------------------------------------------------------------

    internal JsonRpcResponse DispatchRpc(string body) => new McpDispatcher(_tools, _logger).Dispatch(body);

    // -- Dispatcher marshalling --------------------------------------------------

    /// <summary>
    /// Runs <paramref name="action"/> on the UI dispatcher and blocks the caller
    /// until it completes. Tool handlers use this to touch WinUI state safely.
    /// Timeout defaults to 5s so a stuck UI thread doesn't hang the HTTP worker.
    /// Exceptions raised on the dispatcher surface with their original type
    /// (in particular <see cref="McpToolException"/>) — not wrapped in
    /// <see cref="AggregateException"/> — so structured errors round-trip.
    /// </summary>
    public T OnDispatcher<T>(Func<T> action, int timeoutMs = 5000)
    {
        if (_dispatcherQueue.HasThreadAccess)
            return action();

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcherQueue.TryEnqueue(() =>
        {
            try { tcs.TrySetResult(action()); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }))
        {
            throw new McpToolException("Could not enqueue work onto the UI dispatcher.");
        }

        // Avoid Task.Wait — it re-wraps faults in AggregateException, hiding the
        // structured McpToolException payload. Poll IsCompleted with a timeout,
        // then unwrap manually so the original exception type propagates.
        using var completed = new ManualResetEventSlim(false);
        tcs.Task.ContinueWith(_ => completed.Set(), TaskContinuationOptions.ExecuteSynchronously);
        if (!completed.Wait(timeoutMs))
            throw new McpToolException("Dispatcher call timed out.");

        if (tcs.Task.IsFaulted)
        {
            var inner = tcs.Task.Exception!.InnerException ?? tcs.Task.Exception;
            global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(inner).Throw();
        }
        return tcs.Task.Result;
    }

    public void OnDispatcher(Action action, int timeoutMs = 5000) =>
        OnDispatcher<object?>(() => { action(); return null; }, timeoutMs);

    // -- Helpers -----------------------------------------------------------------

    internal static JsonSerializerOptions JsonOpts { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
#pragma warning disable IL2026, IL3050 // DefaultJsonTypeInfoResolver is intentional fallback for non-AOT builds
        TypeInfoResolverChain = { DevtoolsJsonContext.Default, new global::System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() },
#pragma warning restore IL2026, IL3050
    };

    /// <summary>
    /// Canonical selector grammar the resolver accepts. Emitted by GET /mcp so an
    /// agent can read the five forms without reading our source, and quoted in
    /// every tool's <c>selector</c> schema description for inline discoverability.
    /// </summary>
    internal static readonly string SelectorGrammarDoc =
        "Selectors accept any of these forms:\n" +
        "  1. Node id — `r:<window>/<local>` (e.g. r:main/DemoApp.SubmitButton). Stable handle from `tree`.\n" +
        "  2. AutomationId — `#btn-inc`. Matches AutomationProperties.AutomationId exactly.\n" +
        "  3. AutomationName — `[name='Increment']`. Matches AutomationProperties.Name OR the visible caption of Buttons / TextBlocks / TextBoxes / ContentControls (case-sensitive).\n" +
        "  4. TypePath — `Button`, `Button[2]`, `StackPanel > Button`. Type names match on `element.GetType().Name`. Index disambiguates when multiple match.\n" +
        "  5. Reactor source — `{component:'CounterDemo',line:42}`. Requires the Phase 3 source map; returns a structured `not-implemented` today.\n" +
        "Windows: when multiple windows are active, pass `window: \"<id>\"` to scope resolution; a node-id from a different window is a hard error.";

    /// <summary>
    /// Builds the self-describing document emitted by GET /mcp — the tool
    /// inventory, selector grammar, and protocol / schema versions in one
    /// payload so an agent can orient itself without crafting a JSON-RPC
    /// initialize + tools/list dance first.
    /// </summary>
    private object BuildSchemaDocument()
    {
        return new
        {
            schema = "reactor-devtools-mcp/1",
            protocolVersion = "2024-11-05",
            build = BuildTag,
            transport = _transport == McpTransport.Http ? "http" : "stdio",
            endpoint = _transport == McpTransport.Http
                ? $"http://127.0.0.1:{Port}/mcp"
                : "stdio",
            selectorGrammar = SelectorGrammarDoc,
            treeSchemaVersion = TreeWalker.SchemaVersion,
            tools = _tools.List().Select(t => new
            {
                name = t.Name,
                description = t.Description,
                inputSchema = t.InputSchema,
            }).ToArray(),
            events = new[]
            {
                new { name = "devtools-ready", description = "One-line JSON sentinel emitted on stdout after first render. Fields: endpoint, transport, port, pid, buildTag." },
            },
        };
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Derives a stable build tag from the entry assembly's compile timestamp (or
    /// informational version, if richer). Agents use this to confirm a reload took.
    /// </summary>
    [UnconditionalSuppressMessage("AOT", "IL3000", Justification = "Assembly.Location used for diagnostic build tag.")]
    private static string ResolveBuildTag()
    {
        var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info)) return info!;

        try
        {
            var path = asm.Location;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
                return File.GetLastWriteTimeUtc(path).ToString("yyyy-MM-ddTHH:mm:ssZ");
        }
        catch { }

        return "unknown";
    }
}
