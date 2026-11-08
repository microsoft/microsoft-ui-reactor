using Microsoft.Win32;
using ReactorRegedit.Core;

namespace ReactorRegedit.Services;

/// <summary>Summary of an import: how many operations applied and which failed.</summary>
internal sealed record ImportResult(int Applied, IReadOnlyList<string> Failures);

/// <summary>Registry ⇄ .reg text.</summary>
internal static class RegFileService
{
    /// <summary>
    /// Exports <paramref name="roots"/> (each with its whole subtree) as .reg text.
    /// Unreadable keys are skipped and reported through <paramref name="skipped"/>.
    /// </summary>
    public static string Export(IEnumerable<string> roots, CancellationToken ct, Action<string>? progress = null, List<string>? skipped = null)
    {
        var writer = new RegFileWriter();
        Walk(roots, (path, _, values) => writer.WriteKey(path, values), ct, progress, skipped);
        return writer.Text;
    }

    /// <summary>Receives each key of a walk (pre-order), with its last-write time and raw values.</summary>
    public delegate void KeySink(string path, DateTime? lastWriteLocal, List<RegValue> values);

    /// <summary>
    /// Visits <paramref name="roots"/> and their whole subtrees in pre-order. Unreadable keys
    /// are skipped and reported through <paramref name="skipped"/>.
    /// </summary>
    public static void Walk(IEnumerable<string> roots, KeySink sink, CancellationToken ct, Action<string>? progress = null, List<string>? skipped = null)
    {
        foreach (var root in roots) WalkKey(sink, root, ct, progress, skipped);
    }

    /// <summary>
    /// Streams the export to <paramref name="filePath"/> as UTF-16 LE with BOM (regedit's
    /// format). Writes to a temporary file first so a cancelled or failed export never
    /// leaves a truncated file behind. Returns the number of keys written.
    /// </summary>
    public static int ExportToFile(string filePath, IEnumerable<string> roots, CancellationToken ct, Action<int, string>? progress = null, List<string>? skipped = null, bool asText = false)
    {
        var temp = filePath + ".tmp-" + Environment.ProcessId;
        int keys = 0;
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            using (var text = new StreamWriter(stream, new System.Text.UnicodeEncoding(bigEndian: false, byteOrderMark: true), 1 << 16))
            {
                KeySink sink;
                if (asText)
                {
                    var writer = new RegTextWriter(text);
                    sink = writer.WriteKey;
                }
                else
                {
                    var writer = new RegFileWriter(text);
                    sink = (path, _, values) => writer.WriteKey(path, values);
                }
                Walk(roots, sink, ct, path => progress?.Invoke(++keys, path), skipped);
            }
            File.Move(temp, filePath, overwrite: true);
            return keys;
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static void WalkKey(KeySink sink, string path, CancellationToken ct, Action<string>? progress, List<string>? skipped)
    {
        ct.ThrowIfCancellationRequested();
        RegistryKey key;
        try { key = RegistryService.OpenKey(path); }
        catch (RegistryOperationException)
        {
            skipped?.Add(path);
            return;
        }
        string[] subKeys;
        using (key)
        {
            progress?.Invoke(path);
            List<RegValue> values;
            try { values = RegistryService.ReadAllValues(key, path); }
            catch (RegistryOperationException)
            {
                skipped?.Add(path);
                values = [];
            }
            sink(path, RegistryService.LastWriteTime(key), values);
            try { subKeys = key.GetSubKeyNames(); }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                skipped?.Add(path);
                return;
            }
        }
        foreach (var sub in subKeys) WalkKey(sink, path + "\\" + sub, ct, progress, skipped);
    }

    /// <summary>Applies parsed operations; continues past failures and reports them.</summary>
    public static ImportResult Apply(IReadOnlyList<RegFileOp> ops)
    {
        int applied = 0;
        var failures = new List<string>();
        foreach (var op in ops)
        {
            try
            {
                switch (op)
                {
                    case CreateKeyOp c:
                        RegistryService.EnsureKey(c.KeyPath);
                        break;
                    case DeleteKeyOp d:
                        if (!RegPath.IsHive(d.KeyPath)) RegistryService.DeleteKey(d.KeyPath, throwIfMissing: false);
                        break;
                    case SetValueOp s:
                        RegistryService.SetValue(s.KeyPath, s.Name, s.Type, s.Data);
                        break;
                    case DeleteValueOp dv:
                        try { RegistryService.DeleteValue(dv.KeyPath, dv.Name); }
                        catch (RegistryOperationException ex) when (ex.IsNotFound) { }
                        break;
                }
                applied++;
            }
            catch (RegistryOperationException ex)
            {
                failures.Add($"Line {op.Line}: {ex.Message}");
            }
        }
        return new ImportResult(applied, failures);
    }
}
