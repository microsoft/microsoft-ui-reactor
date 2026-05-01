using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.UI.Reactor.Cli.Devtools;

/// <summary>
/// Reader-side of the spec 025 lockfile contract. Kept separate from the
/// server-side <c>LockfileRegistry</c> so the CLI doesn't pull WinUI in via a
/// project reference. The two sides agree on path + schema constants; changes
/// to the on-disk format must touch both files.
/// </summary>
internal sealed class LockfileEntry
{
    [JsonPropertyName("schema")] public string Schema { get; set; } = LockfileReader.SchemaTag;
    [JsonPropertyName("endpoint")] public string Endpoint { get; set; } = "";
    [JsonPropertyName("transport")] public string Transport { get; set; } = "http";
    [JsonPropertyName("port")] public int Port { get; set; }
    [JsonPropertyName("pid")] public int Pid { get; set; }
    [JsonPropertyName("buildTag")] public string BuildTag { get; set; } = "";
    [JsonPropertyName("project")] public string Project { get; set; } = "";
    [JsonPropertyName("startedAt")] public string StartedAt { get; set; } = "";
    [JsonPropertyName("token")] public string Token { get; set; } = "";
}

internal static class LockfileReader
{
    public const string SchemaTag = "reactor-devtools-lockfile/1";
    public const string McpSchemaTag = "reactor-devtools-mcp/1";

    public static string Directory => Path.Combine(Path.GetTempPath(), "reactor-devtools");

    /// <summary>
    /// Hard cap on lockfile size in bytes. Rejected before parsing. Mirrors
    /// <c>LockfileRegistry.MaxLockfileBytes</c>. TASK-005.
    /// </summary>
    public const int MaxLockfileBytes = 8 * 1024;

    /// <summary>
    /// Hard cap on the number of lockfiles processed during a single
    /// enumeration. A co-tenant that plants 10k files could otherwise pin the
    /// CLI; we sort by mtime newest-first and process at most this many.
    /// TASK-034.
    /// </summary>
    public const int MaxLockfilesPerEnumeration = 64;

    public static IEnumerable<(string Path, LockfileEntry? Entry)> EnumerateAll()
    {
        if (!System.IO.Directory.Exists(Directory)) yield break;
        string[] files;
        try { files = System.IO.Directory.GetFiles(Directory, "*.json", SearchOption.TopDirectoryOnly); }
        catch { yield break; }

        // SECURITY (TASK-034): bound enumeration. Order by mtime newest-first
        // so a freshly-started session is always considered.
        var ordered = files
            .Select(f => { try { return (Path: f, Mtime: File.GetLastWriteTimeUtc(f)); } catch { return (Path: f, Mtime: DateTime.MinValue); } })
            .OrderByDescending(p => p.Mtime)
            .Take(MaxLockfilesPerEnumeration);

        foreach (var (f, _) in ordered)
        {
            TryRead(f, out var entry);
            yield return (f, entry);
        }
    }

    public static bool TryRead(string path, out LockfileEntry? entry)
    {
        entry = null;
        if (!File.Exists(path)) return false;
        try
        {
            // SECURITY (TASK-005): cap size before reading.
            var info = new FileInfo(path);
            if (info.Length > MaxLockfileBytes) return false;
            var json = File.ReadAllText(path);
            entry = JsonSerializer.Deserialize<LockfileEntry>(json);
            if (entry is null) return false;
            // SECURITY (TASK-031): enforce schema tag and reject endpoints that
            // are not loopback. A lockfile pointing at off-machine endpoints is
            // dropped before any HTTP probe runs.
            if (!string.Equals(entry.Schema, SchemaTag, StringComparison.Ordinal))
            {
                entry = null;
                return false;
            }
            if (string.Equals(entry.Transport, "http", StringComparison.OrdinalIgnoreCase)
                && !IsLoopbackHttpEndpoint(entry.Endpoint))
            {
                entry = null;
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsLoopbackHttpEndpoint(string endpoint)
    {
        if (string.IsNullOrEmpty(endpoint)) return false;
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, "http", StringComparison.Ordinal)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        var host = uri.Host;
        return string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
            || string.Equals(host, "::1", StringComparison.Ordinal)
            || string.Equals(host, "[::1]", StringComparison.Ordinal)
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Strips ASCII control characters and bidi-override codepoints, then
    /// truncates to <paramref name="maxLength"/>. Apply to any field read from
    /// a same-user lockfile before printing to a terminal — a hostile
    /// co-tenant could otherwise embed ANSI escapes or RTL overrides to
    /// confuse a downstream LLM agent reading our output. TASK-032.
    /// </summary>
    public static string SafeForTerminal(string value, int maxLength = 256)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var sb = new global::System.Text.StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var c in value)
        {
            if (sb.Length >= maxLength) break;
            // Strip C0 (0..31) except printable space, DEL (127), C1 (128..159),
            // and bidi/format codepoints that move output around.
            if (c < 0x20 && c != 0x09) continue;
            if (c == 0x7F) continue;
            if (c >= 0x80 && c <= 0x9F) continue;
            // U+200E/200F LRM/RLM, U+202A-202E embedding/override,
            // U+2066-U+2069 isolate, U+FEFF BOM.
            if (c == 0x200E || c == 0x200F) continue;
            if (c >= 0x202A && c <= 0x202E) continue;
            if (c >= 0x2066 && c <= 0x2069) continue;
            if (c == 0xFEFF) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>Pid + HTTP probe. Stdio sessions are live on pid match alone.</summary>
    public static bool IsLive(LockfileEntry entry)
    {
        if (entry is null || entry.Pid <= 0) return false;
        if (!PidAlive(entry.Pid)) return false;
        if (string.Equals(entry.Transport, "stdio", StringComparison.OrdinalIgnoreCase))
            return true;
        // SECURITY (TASK-030 / TASK-004): if the platform supports it, verify
        // the lockfile pid actually owns the listening TCP port. Without this,
        // a hostile process can plant a fake server + matching lockfile and
        // route all CLI traffic through itself.
        if (!PortOwnedBy(entry.Port, entry.Pid)) return false;
        return HttpProbe(entry.Endpoint, entry.Token);
    }

    private static bool PidAlive(int pid)
    {
        try
        {
            var p = global::System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch { return false; }
    }

    /// <summary>
    /// Returns true iff the listening TCP socket on <paramref name="port"/> on
    /// 127.0.0.1 is owned by <paramref name="pid"/>. On non-Windows hosts the
    /// check is unimplemented and returns true (the HTTP probe is the next-best
    /// signal). On Windows, an exception during the lookup means we cannot
    /// confirm ownership — fail closed (return false) so an attacker can't
    /// bypass the PID→port check by triggering the exception path.
    /// </summary>
    internal static bool PortOwnedBy(int port, int pid)
    {
        if (!OperatingSystem.IsWindows()) return true; // best effort
        try { return PortOwnership.IsPortOwnedBy(port, pid); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[reactor.cli] PortOwnership check failed for pid={pid} port={port}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static bool HttpProbe(string endpoint, string token)
    {
        if (string.IsNullOrEmpty(endpoint)) return false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
            using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrEmpty(token))
            {
                req.Headers.Authorization = new global::System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }
            var resp = http.SendAsync(req).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return false;
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("schema", out var s)) return false;
            return string.Equals(s.GetString(), McpSchemaTag, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
