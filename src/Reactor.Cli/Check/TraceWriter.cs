// `mur check --trace <path>` writer. Appends one JSON row per surfaced
// diagnostic to <path> alongside the normal stdout output. The agent never
// reads the trace; it is for offline mining (spec 037 / 038).
//
// Schema is the source of truth (mirrored in spec 038 §0.3):
//
//   { ts, code, severity, file, line, col, msg, receiver_type?, member?, mode }
//
// Constraints:
// - Source code text never appears in the trace. (`msg` is the diagnostic
//   message; bounded to 1024 chars to keep individual rows under the 2 KB cap
//   the unit test enforces.)
// - Absolute paths outside the project root are replaced with "<external>"
//   so traces never carry information about a user's machine layout.
// - Absolute paths inside the project root are normalized to project-relative
//   form (forward-slash separators) — same rationale: don't carry the
//   `C:\Users\<name>\...` prefix into a trace file that ships off-machine.
// - `mode` is always "iteration" until Phase 2 lands the ranker — present as
//   a stable schema field so traces written today join cleanly later.

using System.Text.Json;

namespace Microsoft.UI.Reactor.Cli.Check;

internal sealed class TraceWriter : IDisposable
{
    readonly StreamWriter writer;
    readonly string projectRoot;

    public const int MaxMessageChars = 1024;
    public const string Mode = "iteration";

    TraceWriter(StreamWriter writer, string projectRoot)
    {
        this.writer = writer;
        this.projectRoot = projectRoot;
    }

    public static TraceWriter Open(string path, string projectRoot)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        var sw = new StreamWriter(stream) { AutoFlush = true };
        return new TraceWriter(sw, NormalizeRoot(projectRoot));
    }

    public void Write(CheckCommand.Diag d)
    {
        var row = ToRow(d, projectRoot);
        var json = JsonSerializer.Serialize(row, JsonOpts);
        writer.WriteLine(json);
    }

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    internal static TraceRow ToRow(CheckCommand.Diag d, string projectRoot)
    {
        var msg = d.Message;
        if (msg.Length > MaxMessageChars) msg = msg[..MaxMessageChars];
        return new TraceRow(
            ts: DateTime.UtcNow.ToString("o"),
            code: d.Code,
            severity: SeverityShort(d.Severity),
            file: SanitizePath(d.File, projectRoot),
            line: d.Line,
            col: d.Col,
            msg: msg,
            receiver_type: null,
            member: null,
            mode: Mode);
    }

    static string SeverityShort(string sev) => sev switch
    {
        "error" => "E",
        "warning" => "W",
        "info" => "I",
        _ => sev,
    };

    static string NormalizeRoot(string root)
    {
        try { return Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return root; }
    }

    internal static string SanitizePath(string raw, string projectRoot)
    {
        if (string.IsNullOrEmpty(raw)) return raw;
        if (!Path.IsPathRooted(raw))
            return raw.Replace('\\', '/'); // already relative — keep as-is, normalize separators
        string full;
        try { full = Path.GetFullPath(raw); }
        catch { return "<external>"; }
        var rootWithSep = projectRoot + Path.DirectorySeparatorChar;
        if (full.Equals(projectRoot, StringComparison.OrdinalIgnoreCase))
            return ".";
        if (full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            return full[rootWithSep.Length..].Replace('\\', '/');
        return "<external>";
    }

    public void Dispose() => writer.Dispose();

    internal sealed record TraceRow(
        string ts,
        string code,
        string severity,
        string file,
        int line,
        int col,
        string msg,
        string? receiver_type,
        string? member,
        string mode);
}
