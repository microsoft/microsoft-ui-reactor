using System.Diagnostics.Tracing;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Reactor.Core.Diagnostics;

namespace Microsoft.UI.Reactor.Hosting.Persistence;

/// <summary>
/// Default unpackaged-app persistence store. Writes a JSON document
/// (one base64 string per persistence id) to
/// <c>%LOCALAPPDATA%/&lt;ProcessName&gt;/reactor-windows.json</c>. Atomic via
/// write-then-rename; bounded at <see cref="MaxFileSizeBytes"/> (1 MB) on read
/// to refuse pathological payloads. (spec 036 §8 / §0.5)
/// </summary>
/// <remarks>
/// <para>Read failures (missing file, malformed JSON, oversize) return
/// <c>false</c> from <see cref="TryRead"/> with no exception bubbling out —
/// the spec calls for "warn-and-default" semantics on corruption.</para>
/// <para>Writes are best-effort and survive disk-full / permission denied
/// without throwing. The file is opened with <see cref="FileShare.None"/> to
/// prevent concurrent same-process writers from clobbering each other; cross-
/// process writers are out of scope (the store is keyed off the entry
/// process's name).</para>
/// </remarks>
public sealed class JsonFileStore : IWindowPersistenceStore
{
    /// <summary>
    /// Hard ceiling on the persistence file size. A 1 MB cap is generous —
    /// real-world payloads are ~80 bytes per window — and rejects pathological
    /// payloads from a tampered file. (spec 036 §0.5)
    /// </summary>
    public const long MaxFileSizeBytes = 1L * 1024 * 1024;

    // Stable, developer-authored label for the spec 044 Phase B Persistence
    // events. NEVER a file path — paths are PII per §6.2.1.
    private const string StoreKind = "json-file";

    private static int ClampSize(long bytes) => bytes > int.MaxValue ? int.MaxValue : (int)bytes;

    private readonly string _path;
    private readonly object _ioLock = new();

    /// <summary>The on-disk file path.</summary>
    public string Path => _path;

    /// <summary>
    /// Construct the default unpackaged store rooted at
    /// <c>%LOCALAPPDATA%/&lt;ProcessName&gt;/reactor-windows.json</c>.
    /// </summary>
    public JsonFileStore() : this(DefaultPath()) { }

    /// <summary>Construct with an explicit file path. Used by unit tests.</summary>
    public JsonFileStore(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path must be non-empty.", nameof(path));
        _path = path;
    }

    /// <summary>
    /// Compute the default unpackaged-store path. Uses a sanitized process
    /// name segment so a process named <c>foo/bar</c> can't traverse out of
    /// LocalAppData. (spec 036 §0.5 / §9.5)
    /// </summary>
    public static string DefaultPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var processName = SanitizeProcessName(global::System.Diagnostics.Process.GetCurrentProcess().ProcessName);
        return global::System.IO.Path.Combine(localAppData, processName, "reactor-windows.json");
    }

    private static string SanitizeProcessName(string name)
    {
        // Strip path separators and any character that wouldn't survive Path.Combine.
        var sb = new global::System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')
                sb.Append(c);
            else
                sb.Append('_');
        }
        var result = sb.ToString();
        return result.Length == 0 ? "ReactorApp" : result;
    }

    /// <inheritdoc />
    public bool TryRead(string id, out byte[]? data)
    {
        data = null;
        if (string.IsNullOrEmpty(id)) return false;

        try
        {
            lock (_ioLock)
            {
                if (!File.Exists(_path)) return false;
                var info = new FileInfo(_path);
                if (info.Length > MaxFileSizeBytes)
                {
                    if (ReactorEventSource.Log.IsEnabled(EventLevel.Warning, ReactorEventSource.Keywords.Persistence))
                        ReactorEventSource.Log.PersistenceRejected(StoreKind, "oversize-read");
                    return false;
                }

                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
                if (!doc.RootElement.TryGetProperty(id, out var entry)) return false;
                if (entry.ValueKind != JsonValueKind.String) return false;
                var b64 = entry.GetString();
                if (string.IsNullOrEmpty(b64)) return false;
                data = Convert.FromBase64String(b64);
                if (data is not null
                    && ReactorEventSource.Log.IsEnabled(EventLevel.Informational, ReactorEventSource.Keywords.Persistence))
                    ReactorEventSource.Log.PersistenceRead(StoreKind, ClampSize(info.Length));
                return data is not null;
            }
        }
        catch (JsonException ex)
        {
            DiagnosticLog.SwallowedError(LogCategory.Persistence, "JsonFileStore.TryRead.parse", ex);
            data = null;
            return false;
        }
        catch (FormatException ex)
        {
            DiagnosticLog.SwallowedError(LogCategory.Persistence, "JsonFileStore.TryRead.base64", ex);
            data = null;
            return false;
        }
        catch (IOException ex)
        {
            DiagnosticLog.SwallowedError(LogCategory.Persistence, "JsonFileStore.TryRead", ex);
            data = null;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            DiagnosticLog.SwallowedError(LogCategory.Persistence, "JsonFileStore.TryRead", ex);
            data = null;
            return false;
        }
    }

    /// <inheritdoc />
    public void Write(string id, byte[] data)
    {
        if (string.IsNullOrEmpty(id) || data is null) return;

        try
        {
            lock (_ioLock)
            {
                var dir = global::System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Read-merge-write so multiple PersistenceIds coexist in the
                // same file. Rejects oversize files so a tampered file
                // doesn't cause the next write to OOM. Encoded by hand so
                // the AOT analyzer doesn't flag JsonSerializer.
                var doc = ReadDocumentOrEmpty();
                doc[id] = Convert.ToBase64String(data);

                var bytes = SerializeStringMap(doc);
                if (bytes.Length > MaxFileSizeBytes)
                {
                    if (ReactorEventSource.Log.IsEnabled(EventLevel.Warning, ReactorEventSource.Keywords.Persistence))
                        ReactorEventSource.Log.PersistenceRejected(StoreKind, "oversize-write");
                    return;
                }

                // Atomic-ish: write to temp then rename. WinAPI MoveFileEx with
                // MOVEFILE_REPLACE_EXISTING is what File.Move(_, _, true) maps
                // to on Windows.
                var tmp = _path + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                File.Move(tmp, _path, overwrite: true);

                if (ReactorEventSource.Log.IsEnabled(EventLevel.Informational, ReactorEventSource.Keywords.Persistence))
                    ReactorEventSource.Log.PersistenceWrite(StoreKind, ClampSize(bytes.Length));
            }
        }
        catch (IOException ex)
        {
            DiagnosticLog.SwallowedError(LogCategory.Persistence, "JsonFileStore.Write", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            DiagnosticLog.SwallowedError(LogCategory.Persistence, "JsonFileStore.Write", ex);
        }
    }

    private Dictionary<string, string> ReadDocumentOrEmpty()
    {
        try
        {
            if (!File.Exists(_path)) return new(StringComparer.Ordinal);
            var info = new FileInfo(_path);
            if (info.Length > MaxFileSizeBytes) return new(StringComparer.Ordinal);
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ParseStringMap(stream);
        }
        catch (IOException ex)
        {
            DiagnosticLog.SwallowedError(LogCategory.Persistence, "JsonFileStore.ReadDocumentOrEmpty", ex);
            return new(StringComparer.Ordinal);
        }
        catch (UnauthorizedAccessException ex)
        {
            DiagnosticLog.SwallowedError(LogCategory.Persistence, "JsonFileStore.ReadDocumentOrEmpty", ex);
            return new(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Parse a flat <c>{ "id": "base64", ... }</c> document into a
    /// <c>Dictionary&lt;string, string&gt;</c>. Hand-rolled via
    /// <see cref="JsonDocument"/> so we avoid the AOT-unfriendly
    /// <c>JsonSerializer.Deserialize&lt;Dictionary&gt;</c> path.
    /// </summary>
    private static Dictionary<string, string> ParseStringMap(Stream stream)
    {
        try
        {
            using var doc = JsonDocument.Parse(stream);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var entry in doc.RootElement.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String) continue;
                var s = entry.Value.GetString();
                if (s is not null) result[entry.Name] = s;
            }
            return result;
        }
        catch (JsonException)
        {
            return new(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Serialize a flat <c>Dictionary&lt;string, string&gt;</c> to UTF-8 JSON
    /// (no whitespace). Hand-rolled to stay AOT-safe; values are pre-known to
    /// be base64 (caller controls), so escaping is limited to backslash and
    /// double-quote in keys.
    /// </summary>
    private static byte[] SerializeStringMap(Dictionary<string, string> map)
    {
        var sb = new StringBuilder(64 + map.Count * 96);
        sb.Append('{');
        bool first = true;
        foreach (var kv in map)
        {
            if (!first) sb.Append(',');
            first = false;
            AppendQuotedString(sb, kv.Key);
            sb.Append(':');
            AppendQuotedString(sb, kv.Value);
        }
        sb.Append('}');
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void AppendQuotedString(StringBuilder sb, string value)
    {
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
