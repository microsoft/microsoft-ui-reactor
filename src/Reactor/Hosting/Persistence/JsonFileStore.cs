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
/// without throwing.</para>
/// <para><b>Concurrency.</b> Read-merge-write is serialized two ways: an in-process
/// lock orders threads sharing an instance, and a named
/// <see cref="CrossProcessWriteGuard"/> extends that ordering across separate
/// instances and separate processes. Without the latter, two writers could each read
/// the pre-merge document and the second rename would silently discard the first's
/// entry — even when the two wrote <i>different</i> persistence ids. Each write stages
/// through a temp file named for the writing process and thread, so a crash mid-write
/// cannot leave a shared temp that another process renames over the real file.</para>
/// <para>Readers take no guard, but they do open with
/// <see cref="FileShare.Delete"/> in addition to <see cref="FileShare.Read"/>, and the
/// commit uses <c>File.Replace</c> rather than <c>File.Move(overwrite: true)</c>.
/// Measured on Windows: <c>Move</c> cannot replace a destination any process still
/// holds open — it fails even when that reader granted delete sharing — whereas
/// <c>Replace</c> succeeds precisely when the reader does grant it. Both halves are
/// required; with only one, a concurrent reader in a second app instance silently
/// kills the write, because write failures are swallowed by contract. The reader then
/// continues against the file it opened, seeing either the previous document or the
/// next one, never a torn one.</para>
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
    // events. NEVER a file path — paths are PII per §6.2.1. Instance rather
    // than const because UnpackagedAppDataStore composes this type over a
    // different root and must stay distinguishable on the trace (spec 063 §4).
    private const string DefaultStoreKind = "json-file";

    private readonly string _storeKind;

    private static int ClampSize(long bytes) => bytes > int.MaxValue ? int.MaxValue : (int)bytes;

    /// <summary>
    /// The one place this store opens the document for reading. Both read paths route
    /// through it so the share mode cannot drift apart, and so a test can hold a stream
    /// opened by the <i>production</i> code across a write — a test that opened its own
    /// <see cref="FileStream"/> would keep passing if these flags regressed.
    /// </summary>
    /// <remarks>
    /// <see cref="FileShare.Delete"/> is load-bearing, not defensive: without it the
    /// <c>File.Replace</c> that commits a write fails while any reader is open, and
    /// because writes are best-effort that failure is silent.
    /// </remarks>
    internal static FileStream OpenForRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

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
    public JsonFileStore(string path) : this(path, DefaultStoreKind) { }

    /// <summary>
    /// Construct with an explicit file path and trace label. Internal because the
    /// label is a stable diagnostic contract, not something callers should invent:
    /// <see cref="UnpackagedAppDataStore"/> composes this type over the Windows App
    /// SDK app-data root and needs to stay distinguishable on the spec 044
    /// Persistence trace. (spec 063 §4)
    /// </summary>
    internal JsonFileStore(string path, string storeKind)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path must be non-empty.", nameof(path));
        _path = path;
        _storeKind = storeKind;
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
                        ReactorEventSource.Log.PersistenceRejected(_storeKind, "oversize-read");
                    return false;
                }

                using var stream = OpenForRead(_path);
                var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
                if (!doc.RootElement.TryGetProperty(id, out var entry)) return false;
                if (entry.ValueKind != JsonValueKind.String) return false;
                var b64 = entry.GetString();
                if (string.IsNullOrEmpty(b64)) return false;
                data = Convert.FromBase64String(b64);
                if (data is not null
                    && ReactorEventSource.Log.IsEnabled(EventLevel.Informational, ReactorEventSource.Keywords.Persistence))
                    ReactorEventSource.Log.PersistenceRead(_storeKind, ClampSize(info.Length));
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
            // Two locks, two scopes. The in-process lock keeps threads sharing this
            // instance ordered; the named mutex extends that ordering across
            // instances AND processes, which is what the IWindowPersistenceStore
            // contract promises and what read-merge-write actually requires — two
            // writers can otherwise both read the old document and the second rename
            // silently discards the first's id.
            lock (_ioLock)
            using (var guard = CrossProcessWriteGuard.Acquire(_path))
            {
                // Not held means a peer held the lock for the whole timeout. Writing
                // anyway would merge a stale document and drop that peer's entry —
                // the exact lost update the guard exists to prevent — so this write is
                // abandoned instead. Best-effort by contract; the diagnostic is emitted
                // by the guard.
                if (!guard.IsHeld)
                {
                    if (ReactorEventSource.Log.IsEnabled(EventLevel.Warning, ReactorEventSource.Keywords.Persistence))
                        ReactorEventSource.Log.PersistenceRejected(_storeKind, "write-lock-timeout");
                    return;
                }

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
                        ReactorEventSource.Log.PersistenceRejected(_storeKind, "oversize-write");
                    return;
                }

                // Commit. File.Move(overwrite:true) cannot replace a destination that
                // any process still has open — measured: it fails with
                // UnauthorizedAccessException even when the reader granted
                // FileShare.Delete. File.Replace (ReplaceFileW) can, provided readers
                // share delete, which TryRead/ReadDocumentOrEmpty do. Without this a
                // concurrent reader in another app instance would silently kill the
                // write, since write failures are swallowed by contract.
                //
                // Replace requires an existing destination, so the first write of a
                // fresh store still goes through Move.
                var tmp = $"{_path}.{Environment.ProcessId:x}.{Environment.CurrentManagedThreadId:x}.tmp";
                try
                {
                    File.WriteAllBytes(tmp, bytes);
                    if (File.Exists(_path))
                        File.Replace(tmp, _path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    else
                        File.Move(tmp, _path, overwrite: true);
                }
                finally
                {
                    // Consumed on success; this only fires if the write or the commit
                    // threw, and must not mask that exception.
                    try { if (File.Exists(tmp)) File.Delete(tmp); }
                    catch (IOException) { /* best effort */ }
                    catch (UnauthorizedAccessException) { /* best effort */ }
                }

                if (ReactorEventSource.Log.IsEnabled(EventLevel.Informational, ReactorEventSource.Keywords.Persistence))
                    ReactorEventSource.Log.PersistenceWrite(_storeKind, ClampSize(bytes.Length));
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
            using var stream = OpenForRead(_path);
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
