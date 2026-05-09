using System.Net.Http;
using System.Text.Json;

namespace Microsoft.UI.Reactor.Cli.Devtools;

/// <summary>
/// One method per <c>mur devtools &lt;verb&gt;</c>. Each verb parses its argv,
/// builds a JSON <c>arguments</c> object for the target MCP tool, and delegates
/// to <see cref="McpCliClient"/>. Output / error mapping per spec 025 §9.
/// </summary>
internal static class DevtoolsVerbs
{
    /// <summary>Verb names that dispatch to a named CLI verb. Also serves as the
    /// guard the top-level <c>mur devtools</c> router uses to decide
    /// "named verb vs. launcher" — unknown names fall back to the supervisor
    /// so <c>mur devtools MyProj.csproj</c> keeps working.</summary>
    public static readonly HashSet<string> KnownVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "version", "windows", "components", "switch", "tree", "screenshot",
        "state", "click", "type", "focus", "invoke", "toggle", "select",
        "scroll", "expand", "collapse", "wait", "fire", "reload", "shutdown",
        "call", "logs", "properties", "set-property", "resources",
        "set-resource", "styles", "ancestors",
        // Spec 036 §10 windows.* tools — dotted names match the MCP tool names.
        "windows.list", "windows.activate", "windows.close", "windows.open",
    };

    public static int Run(string verb, string[] args)
    {
        // Separate universal flags (--endpoint, --auto, --pretty) from verb argv.
        var (shared, verbArgs) = ExtractShared(args);

        var resolution = EndpointDiscovery.Resolve(shared.Endpoint, shared.AutoScan);
        if (resolution.Exit != DevtoolsCliExit.Success)
        {
            if (!string.IsNullOrEmpty(resolution.ErrorMessage))
                Console.Error.WriteLine($"[mur devtools] {resolution.ErrorMessage}");
            return (int)resolution.Exit;
        }

        using var client = new McpCliClient(resolution.Endpoint!, token: resolution.Token);
        try
        {
            return verb.ToLowerInvariant() switch
            {
                "call" => Call(client, verbArgs, shared),
                "version" => Simple(client, "version", verbArgs, shared),
                "windows" => Simple(client, "windows", verbArgs, shared),
                "components" => Simple(client, "components", verbArgs, shared),
                "switch" => Switch(client, verbArgs, shared),
                "tree" => Tree(client, verbArgs, shared),
                "screenshot" => Screenshot(client, verbArgs, shared),
                "state" => State(client, verbArgs, shared),
                "click" => UnarySelector(client, "click", verbArgs, shared),
                "focus" => UnarySelector(client, "focus", verbArgs, shared),
                "invoke" => UnarySelector(client, "invoke", verbArgs, shared),
                "toggle" => UnarySelector(client, "toggle", verbArgs, shared),
                "expand" => UnarySelector(client, "expand", verbArgs, shared),
                "collapse" => UnarySelector(client, "collapse", verbArgs, shared),
                "type" => Type(client, verbArgs, shared),
                "select" => SelectVerb(client, verbArgs, shared),
                "scroll" => Scroll(client, verbArgs, shared),
                "wait" => Wait(client, verbArgs, shared),
                "fire" => Fire(client, verbArgs, shared),
                "reload" => Reload(client, verbArgs, shared),
                "shutdown" => Simple(client, "shutdown", verbArgs, shared),
                "logs" => Logs(client, verbArgs, shared),
                "properties" => Properties(client, verbArgs, shared),
                "set-property" => SetProperty(client, verbArgs, shared),
                "resources" => Resources(client, verbArgs, shared),
                "set-resource" => SetResource(client, verbArgs, shared),
                "styles" => UnarySelector(client, "styles", verbArgs, shared),
                "ancestors" => UnarySelector(client, "ancestors", verbArgs, shared),
                "windows.list" => Simple(client, "windows.list", verbArgs, shared),
                "windows.activate" => WindowsActivate(client, verbArgs, shared),
                "windows.close" => WindowsClose(client, verbArgs, shared),
                "windows.open" => WindowsOpen(client, verbArgs, shared),
                _ => UsageError($"Unknown devtools verb: {verb}"),
            };
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"[mur devtools] transport error: {ex.Message}");
            return (int)DevtoolsCliExit.Transport;
        }
        catch (TaskCanceledException)
        {
            Console.Error.WriteLine("[mur devtools] transport timeout");
            return (int)DevtoolsCliExit.Transport;
        }
    }

    // -- Shared flag parsing -------------------------------------------------

    internal sealed record SharedFlags(string? Endpoint, bool AutoScan, bool Pretty);

    internal static (SharedFlags Shared, string[] VerbArgs) ExtractShared(string[] args)
    {
        string? endpoint = null;
        bool auto = false;
        bool pretty = false;
        var rest = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--endpoint" && i + 1 < args.Length) { endpoint = args[++i]; continue; }
            if (a == "--auto") { auto = true; continue; }
            if (a == "--pretty") { pretty = true; continue; }
            rest.Add(a);
        }
        return (new SharedFlags(endpoint, auto, pretty), rest.ToArray());
    }

    // -- Argument-object builders --------------------------------------------

    // Clone before returning so the JsonElement's lifetime doesn't depend on
    // a JsonDocument that goes out of scope — RootElement without Clone is
    // invalidated once the document is finalized.
    private static JsonElement EmptyArgs()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static JsonElement ArgsFromDict(Dictionary<string, object?> fields)
    {
        var filtered = fields.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);
        var json = JsonSerializer.Serialize(filtered);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    // -- Verb implementations ------------------------------------------------

    private static int Simple(McpCliClient client, string tool, string[] args, SharedFlags shared)
    {
        if (args.Length != 0) return UsageError($"{tool} takes no arguments");
        return EmitResult(client.InvokeTool(tool, EmptyArgs()), shared);
    }

    private static int Switch(McpCliClient client, string[] args, SharedFlags shared)
    {
        if (args.Length != 1) return UsageError("switch <component>");
        var argObj = ArgsFromDict(new() { ["name"] = args[0] });
        return EmitResult(client.InvokeTool("switchComponent", argObj), shared);
    }

    private static int Tree(McpCliClient client, string[] args, SharedFlags shared)
    {
        string? selector = null, window = null, view = null;
        bool includeSource = false;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--selector" && i + 1 < args.Length) selector = args[++i];
            else if (a == "--window" && i + 1 < args.Length) window = args[++i];
            else if (a == "--view" && i + 1 < args.Length) view = args[++i];
            else if (a == "--include-reactor-source") includeSource = true;
            else return UsageError($"tree: unexpected argument '{a}'");
        }
        var fields = new Dictionary<string, object?>
        {
            ["selector"] = selector,
            ["window"] = window,
            ["view"] = view,
            ["includeReactorSource"] = includeSource ? (object?)true : null,
        };
        return EmitResult(client.InvokeTool("tree", ArgsFromDict(fields)), shared);
    }

    private static int Screenshot(McpCliClient client, string[] args, SharedFlags shared)
    {
        string? selector = null, window = null, outPath = null;
        bool waitIdle = false, includeChrome = false;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--selector" && i + 1 < args.Length) selector = args[++i];
            else if (a == "--window" && i + 1 < args.Length) window = args[++i];
            else if (a == "--out" && i + 1 < args.Length) outPath = args[++i];
            else if (a == "--wait-idle") waitIdle = true;
            else if (a == "--include-chrome") includeChrome = true;
            else return UsageError($"screenshot: unexpected argument '{a}'");
        }
        var fields = new Dictionary<string, object?>
        {
            ["selector"] = selector,
            ["window"] = window,
            ["waitIdle"] = waitIdle ? (object?)true : null,
            ["includeChrome"] = includeChrome ? (object?)true : null,
        };
        var doc = client.InvokeTool("screenshot", ArgsFromDict(fields));
        if (HasError(doc)) return EmitResult(doc, shared);

        // Spec §9: stdout is the PNG bytes when --out - is passed; otherwise
        // --out <path> writes the file and stdout gets the result metadata.
        // Without --out at all, the result envelope (which contains the base64
        // PNG) is the stdout payload.
        if (!string.IsNullOrEmpty(outPath) && doc.RootElement.TryGetProperty("result", out var result))
        {
            try
            {
                if (result.TryGetProperty("png", out var pngEl) && pngEl.ValueKind == JsonValueKind.String)
                {
                    var bytes = Convert.FromBase64String(pngEl.GetString()!);
                    // SECURITY (TASK-033): cap size, validate PNG magic, and
                    // refuse paths with NUL or NTFS alternate-stream `:`.
                    if (bytes.Length > 64 * 1024 * 1024)
                    {
                        Console.Error.WriteLine("[mur devtools] screenshot exceeds 64 MiB cap; refusing to write.");
                        return (int)DevtoolsCliExit.ToolError;
                    }
                    if (bytes.Length < 8
                        || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47
                        || bytes[4] != 0x0D || bytes[5] != 0x0A || bytes[6] != 0x1A || bytes[7] != 0x0A)
                    {
                        Console.Error.WriteLine("[mur devtools] screenshot bytes are not a PNG; refusing to write.");
                        return (int)DevtoolsCliExit.ToolError;
                    }
                    if (outPath == "-")
                    {
                        using var stdout = Console.OpenStandardOutput();
                        stdout.Write(bytes, 0, bytes.Length);
                        // Binary stream on stdout — must not also print the
                        // JSON envelope, or it'd corrupt the PNG. Metadata
                        // still goes to stderr for humans.
                        var meta = new { width = result.TryGetProperty("width", out var w) ? (object?)w : null,
                                         height = result.TryGetProperty("height", out var h) ? (object?)h : null,
                                         bounds = result.TryGetProperty("bounds", out var b) ? (object?)b : null };
                        Console.Error.WriteLine(JsonSerializer.Serialize(meta));
                        return (int)DevtoolsCliExit.Success;
                    }
                    if (!IsSafeOutPath(outPath))
                    {
                        Console.Error.WriteLine($"[mur devtools] screenshot --out path refused (NUL or alternate-stream): '{outPath}'");
                        return (int)DevtoolsCliExit.Usage;
                    }
                    File.WriteAllBytes(outPath, bytes);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[mur devtools] screenshot: failed to write PNG: {ex.Message}");
                return (int)DevtoolsCliExit.Usage;
            }
        }
        return EmitResult(doc, shared);
    }

    private static int State(McpCliClient client, string[] args, SharedFlags shared)
    {
        string? selector = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--selector" && i + 1 < args.Length) selector = args[++i];
            else if (selector is null && !a.StartsWith("-")) selector = a;
            else return UsageError($"state: unexpected argument '{a}'");
        }
        var fields = new Dictionary<string, object?> { ["selector"] = selector };
        return EmitResult(client.InvokeTool("state", ArgsFromDict(fields)), shared);
    }

    private static int UnarySelector(McpCliClient client, string tool, string[] args, SharedFlags shared)
    {
        if (args.Length != 1) return UsageError($"{tool} <selector>");
        var fields = new Dictionary<string, object?> { ["selector"] = args[0] };
        return EmitResult(client.InvokeTool(tool, ArgsFromDict(fields)), shared);
    }

    private static int Type(McpCliClient client, string[] args, SharedFlags shared)
    {
        string? selector = null, text = null;
        bool clear = false;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--clear") clear = true;
            else if (selector is null) selector = a;
            else if (text is null) text = a;
            else return UsageError("type <selector> <text> [--clear]");
        }
        if (selector is null || text is null) return UsageError("type <selector> <text> [--clear]");
        var fields = new Dictionary<string, object?>
        {
            ["selector"] = selector,
            ["text"] = text,
            ["clear"] = clear ? (object?)true : null,
        };
        return EmitResult(client.InvokeTool("type", ArgsFromDict(fields)), shared);
    }

    private static int SelectVerb(McpCliClient client, string[] args, SharedFlags shared)
    {
        if (args.Length != 2) return UsageError("select <selector> <item-selector>");
        var fields = new Dictionary<string, object?>
        {
            ["selector"] = args[0],
            ["itemSelector"] = args[1],
        };
        return EmitResult(client.InvokeTool("select", ArgsFromDict(fields)), shared);
    }

    private static int Scroll(McpCliClient client, string[] args, SharedFlags shared)
    {
        string? selector = null, by = null, to = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--by" && i + 1 < args.Length) by = args[++i];
            else if (a == "--to" && i + 1 < args.Length) to = args[++i];
            else if (selector is null && !a.StartsWith("-")) selector = a;
            else return UsageError($"scroll: unexpected argument '{a}'");
        }
        if (selector is null) return UsageError("scroll <selector> [--by H%,V% | --to <selector>]");
        var fields = new Dictionary<string, object?> { ["selector"] = selector };
        if (by is not null)
        {
            // Tool's `by` is {horizontal, vertical} percent deltas (0–100).
            // Parse invariant so `,` as a decimal separator (de-DE, fr-FR)
            // doesn't collide with the `H,V` pair separator.
            var parts = by.Split(',');
            if (parts.Length != 2
                || !double.TryParse(parts[0].Trim(), global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var h)
                || !double.TryParse(parts[1].Trim(), global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var v))
                return UsageError("--by must be H%,V% (percent deltas 0-100)");
            fields["by"] = new { horizontal = h, vertical = v };
        }
        if (to is not null) fields["to"] = to;
        return EmitResult(client.InvokeTool("scroll", ArgsFromDict(fields)), shared);
    }

    private static int Wait(McpCliClient client, string[] args, SharedFlags shared)
    {
        string? selector = null, textEquals = null, textMatches = null;
        bool visible = false;
        int? count = null, timeout = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--text" && i + 1 < args.Length) textEquals = args[++i];
            else if (a == "--text-matches" && i + 1 < args.Length) textMatches = args[++i];
            else if (a == "--visible") visible = true;
            else if (a == "--count" && i + 1 < args.Length && int.TryParse(args[++i], out var c)) count = c;
            else if (a == "--timeout" && i + 1 < args.Length && int.TryParse(args[++i], out var t)) timeout = t;
            else if (selector is null && !a.StartsWith("-")) selector = a;
            else return UsageError($"wait: unexpected argument '{a}'");
        }
        if (selector is null) return UsageError("wait <selector> [--text X | --text-matches RE | --visible | --count N] [--timeout MS]");

        // Tool's input schema wraps match fields in a `predicate` object.
        var predicate = new Dictionary<string, object?>
        {
            ["selector"] = selector,
            ["textEquals"] = textEquals,
            ["textMatches"] = textMatches,
            ["visible"] = visible ? (object?)true : null,
            ["count"] = count,
        };
        var predicateFiltered = predicate.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);
        var fields = new Dictionary<string, object?>
        {
            ["predicate"] = predicateFiltered,
            ["timeoutMs"] = timeout,
        };
        return EmitResult(client.InvokeTool("waitFor", ArgsFromDict(fields)), shared);
    }

    private static int Fire(McpCliClient client, string[] args, SharedFlags shared)
    {
        string? target = null;
        string? argsJson = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--args" && i + 1 < args.Length) argsJson = args[++i];
            else if (target is null) target = a;
            else return UsageError($"fire: unexpected argument '{a}'");
        }
        if (target is null) return UsageError("fire <Component>.<event> [--args JSON]");
        var dotIdx = target.LastIndexOf('.');
        if (dotIdx <= 0) return UsageError("fire expects <Component>.<event>");
        var component = target[..dotIdx];
        var @event = target[(dotIdx + 1)..];
        var fields = new Dictionary<string, object?>
        {
            ["component"] = component,
            ["event"] = @event,
        };
        if (argsJson is not null)
        {
            try { fields["args"] = JsonDocument.Parse(argsJson).RootElement.Clone(); }
            catch (JsonException ex) { return UsageError($"--args must be valid JSON: {ex.Message}"); }
        }
        return EmitResult(client.InvokeTool("fire", ArgsFromDict(fields)), shared);
    }

    private static int Reload(McpCliClient client, string[] args, SharedFlags shared)
    {
        string? component = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--component" && i + 1 < args.Length) component = args[++i];
            else return UsageError($"reload: unexpected argument '{a}'");
        }
        var fields = new Dictionary<string, object?> { ["component"] = component };
        return EmitResult(client.InvokeTool("reload", ArgsFromDict(fields)), shared);
    }

    // -- logs -----------------------------------------------------------------

    private static int Logs(McpCliClient client, string[] args, SharedFlags shared)
    {
        long since = 0;
        int? tail = null;
        string? filter = null;
        string? source = null;
        string? level = null;
        bool follow = false;
        int pollMs = 1000;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--since" && i + 1 < args.Length && long.TryParse(args[++i], out var s)) since = s;
            else if (a == "--tail" && i + 1 < args.Length && int.TryParse(args[++i], out var t)) tail = t;
            else if (a == "--filter" && i + 1 < args.Length) filter = args[++i];
            else if (a == "--source" && i + 1 < args.Length) source = args[++i];
            else if (a == "--level" && i + 1 < args.Length) level = args[++i];
            else if (a == "--follow" || a == "-f") follow = true;
            else if (a == "--poll-ms" && i + 1 < args.Length && int.TryParse(args[++i], out var p)) pollMs = Math.Clamp(p, 100, 30_000);
            else return UsageError($"logs: unexpected argument '{a}'");
        }

        if (!follow)
        {
            var fields = new Dictionary<string, object?>
            {
                ["since"] = since > 0 ? (object?)since : null,
                ["tail"] = tail,
                ["filter"] = filter,
                ["source"] = source,
                ["level"] = level,
            };
            return EmitResult(client.InvokeTool("logs", ArgsFromDict(fields)), shared);
        }

        // Follow mode: stream one line per entry to stdout until Ctrl+C.
        // Uses server-side long-poll (`waitMs`) so we aren't hot-spinning.
        // On error (tool returns an error envelope), exit with ToolError.
        using var cancelSource = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancelSource.Cancel(); };
        long cursor = since;
        while (!cancelSource.IsCancellationRequested)
        {
            var fields = new Dictionary<string, object?>
            {
                ["since"] = cursor > 0 ? (object?)cursor : null,
                ["tail"] = tail,
                ["filter"] = filter,
                ["source"] = source,
                ["level"] = level,
                ["waitMs"] = pollMs,
            };
            try
            {
                using var doc = client.InvokeTool("logs", ArgsFromDict(fields));
                if (HasError(doc)) return EmitResult(doc, shared);

                if (doc.RootElement.TryGetProperty("result", out var result))
                {
                    if (result.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in entries.EnumerateArray())
                            PrintFollowLine(e);
                    }
                    // Server semantics are inclusive: pass nextSeq directly as
                    // the next `since`. (Was `- 1` when the server used >.)
                    if (result.TryGetProperty("nextSeq", out var ns) && ns.TryGetInt64(out var nsv))
                        cursor = nsv;
                }
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"[mur devtools] logs: transport closed: {ex.Message}");
                return (int)DevtoolsCliExit.Transport;
            }
            // Tight loop is safe because InvokeTool blocks server-side up to
            // waitMs; only empty-on-timeout returns fast, and that's the rate
            // we actually want.
            tail = null; // --tail only applies to the first page, not follow deltas.
        }
        return (int)DevtoolsCliExit.Success;
    }

    private static void PrintFollowLine(JsonElement e)
    {
        string ts = e.TryGetProperty("ts", out var t) ? (t.GetString() ?? "") : "";
        string src = e.TryGetProperty("source", out var s) ? (s.GetString() ?? "?") : "?";
        string text = e.TryGetProperty("text", out var tx) ? (tx.GetString() ?? "") : "";
        long seq = e.TryGetProperty("seq", out var sq) && sq.TryGetInt64(out var sv) ? sv : 0;
        Console.WriteLine($"[{ts}] {src,-6} #{seq}  {text}");
    }

    // -- Generic passthrough (§10) -------------------------------------------

    private static int Call(McpCliClient client, string[] args, SharedFlags shared)
    {
        if (args.Length == 0) return UsageError("call <tool|method> [--args JSON]");
        var target = args[0];
        string? argsJson = null;
        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--args" && i + 1 < args.Length) argsJson = args[++i];
            else return UsageError($"call: unexpected argument '{a}'");
        }
        JsonElement? @params = null;
        if (argsJson is not null)
        {
            try { @params = JsonDocument.Parse(argsJson).RootElement.Clone(); }
            catch (JsonException ex) { return UsageError($"--args must be valid JSON: {ex.Message}"); }
        }

        // Heuristic: names with a slash ("tools/list", "notifications/...") or
        // the well-known bare methods ("initialize", "ping") go through
        // InvokeMethod; everything else is assumed to be a tool and wrapped in
        // tools/call. Spec §10 lets either work, but we need to pick one per
        // invocation since we don't see the server's method list at the CLI.
        bool isMethod = target.Contains('/')
            || target == "initialize"
            || target == "ping";
        JsonDocument doc = isMethod
            ? client.InvokeMethod(target, @params)
            : client.InvokeTool(target, @params ?? EmptyArgs());
        return EmitResult(doc, shared);
    }

    // -- windows.* (spec 036 §10) -------------------------------------------

    private static int WindowsActivate(McpCliClient client, string[] args, SharedFlags shared)
    {
        if (args.Length != 1) return UsageError("windows.activate <id>");
        var fields = new Dictionary<string, object?> { ["id"] = args[0] };
        return EmitResult(client.InvokeTool("windows.activate", ArgsFromDict(fields)), shared);
    }

    private static int WindowsClose(McpCliClient client, string[] args, SharedFlags shared)
    {
        if (args.Length != 1) return UsageError("windows.close <id>");
        var fields = new Dictionary<string, object?> { ["id"] = args[0] };
        return EmitResult(client.InvokeTool("windows.close", ArgsFromDict(fields)), shared);
    }

    private static int WindowsOpen(McpCliClient client, string[] args, SharedFlags shared)
    {
        string? component = null, title = null, key = null;
        double? width = null, height = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--title" && i + 1 < args.Length) title = args[++i];
            else if (a == "--key" && i + 1 < args.Length) key = args[++i];
            else if (a == "--width" && i + 1 < args.Length)
            {
                if (!double.TryParse(args[++i], global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var w))
                    return UsageError("windows.open: --width must be a number");
                width = w;
            }
            else if (a == "--height" && i + 1 < args.Length)
            {
                if (!double.TryParse(args[++i], global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var h))
                    return UsageError("windows.open: --height must be a number");
                height = h;
            }
            else if (component is null && !a.StartsWith("-")) component = a;
            else return UsageError($"windows.open: unexpected argument '{a}'");
        }
        if (component is null)
            return UsageError("windows.open <Component> [--title T] [--width W] [--height H] [--key K]");
        var fields = new Dictionary<string, object?>
        {
            ["component"] = component,
            ["title"] = title,
            ["width"] = width,
            ["height"] = height,
            ["key"] = key,
        };
        return EmitResult(client.InvokeTool("windows.open", ArgsFromDict(fields)), shared);
    }

    // -- Output / error mapping ----------------------------------------------

    // -- property / resource / style verbs -----------------------------------

    private static int Properties(McpCliClient client, string[] args, SharedFlags shared)
    {
        string? selector = null, name = null, window = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--name" && i + 1 < args.Length) name = args[++i];
            else if (a == "--window" && i + 1 < args.Length) window = args[++i];
            else if (selector is null && !a.StartsWith("-")) selector = a;
            else return UsageError("properties <selector> [--name PropName] [--window W]");
        }
        if (selector is null) return UsageError("properties <selector> [--name PropName] [--window W]");
        var fields = new Dictionary<string, object?>
        {
            ["selector"] = selector,
            ["name"] = name,
            ["window"] = window,
        };
        return EmitResult(client.InvokeTool("properties", ArgsFromDict(fields)), shared);
    }

    private static int SetProperty(McpCliClient client, string[] args, SharedFlags shared)
    {
        string? selector = null, name = null, value = null, window = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--window" && i + 1 < args.Length) window = args[++i];
            else if (selector is null) selector = a;
            else if (name is null) name = a;
            else if (value is null) value = a;
            else return UsageError("set-property <selector> <name> <value> [--window W]");
        }
        if (selector is null || name is null || value is null)
            return UsageError("set-property <selector> <name> <value> [--window W]");
        var fields = new Dictionary<string, object?>
        {
            ["selector"] = selector,
            ["name"] = name,
            ["value"] = value,
            ["window"] = window,
        };
        return EmitResult(client.InvokeTool("setProperty", ArgsFromDict(fields)), shared);
    }

    private static int Resources(McpCliClient client, string[] args, SharedFlags shared)
    {
        string? selector = null, scope = null, filter = null, window = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--selector" && i + 1 < args.Length) selector = args[++i];
            else if (a == "--scope" && i + 1 < args.Length) scope = args[++i];
            else if (a == "--filter" && i + 1 < args.Length) filter = args[++i];
            else if (a == "--window" && i + 1 < args.Length) window = args[++i];
            else return UsageError("resources [--selector S] [--scope element|window|app] [--filter RE] [--window W]");
        }
        var fields = new Dictionary<string, object?>
        {
            ["selector"] = selector,
            ["scope"] = scope,
            ["filter"] = filter,
            ["window"] = window,
        };
        return EmitResult(client.InvokeTool("resources", ArgsFromDict(fields)), shared);
    }

    private static int SetResource(McpCliClient client, string[] args, SharedFlags shared)
    {
        string? key = null, value = null, scope = null, selector = null, window = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--scope" && i + 1 < args.Length) scope = args[++i];
            else if (a == "--selector" && i + 1 < args.Length) selector = args[++i];
            else if (a == "--window" && i + 1 < args.Length) window = args[++i];
            else if (key is null) key = a;
            else if (value is null) value = a;
            else return UsageError("set-resource <key> <value> [--scope app|window|element] [--selector S] [--window W]");
        }
        if (key is null || value is null)
            return UsageError("set-resource <key> <value> [--scope app|window|element] [--selector S] [--window W]");
        var fields = new Dictionary<string, object?>
        {
            ["key"] = key,
            ["value"] = value,
            ["scope"] = scope,
            ["selector"] = selector,
            ["window"] = window,
        };
        return EmitResult(client.InvokeTool("setResource", ArgsFromDict(fields)), shared);
    }

    // -- Output / error mapping ----------------------------------------------

    /// <summary>
    /// Returns false for paths containing NUL or NTFS alternate-stream `:`
    /// past the optional drive letter. TASK-033.
    /// </summary>
    internal static bool IsSafeOutPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (path.IndexOf('\0') >= 0) return false;
        // Allow `C:` or `c:` only at index 1.
        for (int i = 0; i < path.Length; i++)
        {
            if (path[i] == ':')
            {
                if (i == 1 && char.IsLetter(path[0])) continue;
                return false;
            }
        }
        return true;
    }

    private static bool HasError(JsonDocument doc) =>
        doc.RootElement.TryGetProperty("error", out var err) && err.ValueKind != JsonValueKind.Null;

    private static int EmitResult(JsonDocument doc, SharedFlags shared)
    {
        var opts = new JsonSerializerOptions { WriteIndented = shared.Pretty };
        if (HasError(doc))
        {
            var err = doc.RootElement.GetProperty("error");
            var msg = err.TryGetProperty("message", out var m) ? (m.GetString() ?? "tool error") : "tool error";
            Console.Error.WriteLine($"[mur devtools] {msg}");
            Console.WriteLine(JsonSerializer.Serialize(doc.RootElement, opts));
            return (int)DevtoolsCliExit.ToolError;
        }
        if (doc.RootElement.TryGetProperty("result", out var result))
            Console.WriteLine(JsonSerializer.Serialize(result, opts));
        else
            Console.WriteLine(JsonSerializer.Serialize(doc.RootElement, opts));
        return (int)DevtoolsCliExit.Success;
    }

    private static int UsageError(string msg)
    {
        Console.Error.WriteLine($"[mur devtools] {msg}");
        return (int)DevtoolsCliExit.Usage;
    }
}
