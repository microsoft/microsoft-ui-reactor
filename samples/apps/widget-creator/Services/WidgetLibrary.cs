using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetCreator.Services;

/// <summary>
/// Persists generated widgets so the gallery survives restarts. Each app is a
/// folder under <c>%LOCALAPPDATA%\WidgetCreator\apps\&lt;id&gt;</c> with a
/// <c>meta.json</c> sidecar. This is just the metadata index — the source and
/// published binaries already live in the same folder (written by
/// <see cref="WidgetWorkspace"/>).
/// </summary>
public sealed partial class WidgetLibrary
{
    const string MetaFile = "meta.json";
    const int MaxIoAttempts = 10;

    static readonly SemaphoreSlim IoGate = new(1, 1);

    public string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WidgetCreator", "apps");

    readonly ArtifactIntegrity _integrity;

    public WidgetLibrary()
    {
        Directory.CreateDirectory(Root);
        _integrity = new ArtifactIntegrity(Root);
    }

    /// <summary>H-3: verify a stored widget's artifacts before relaunch.</summary>
    public IntegrityResult VerifyIntegrity(WidgetApp app) => _integrity.Verify(app);

    /// <summary>H-3: record the integrity of a widget's current artifacts (also
    /// used to migrate a legacy widget that predates integrity protection).</summary>
    public Task StampIntegrityAsync(WidgetApp app) => _integrity.StampAsync(app);

    public async Task SaveAsync(WidgetApp app)
    {
        await IoGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(app.Dir);
            var json = JsonSerializer.Serialize(new Meta(
                app.Id, app.Title, app.Icon, app.Prompt, app.Model, app.CreatedAt,
                app.ExePath, app.PublishDir, app.SessionId), LibraryJsonContext.Default.Meta);
            await WriteAtomicWithRetriesAsync(Path.Combine(app.Dir, MetaFile), json).ConfigureAwait(false);
            await _integrity.StampAsync(app).ConfigureAwait(false);
            SessionLog.Write($"[Library] saved {app.Id} '{app.Title}' session={app.SessionId}");
        }
        finally
        {
            IoGate.Release();
        }
    }

    public IReadOnlyList<WidgetApp> LoadAll()
    {
        IoGate.Wait();
        try
        {
            var apps = new List<WidgetApp>();
            if (!Directory.Exists(Root)) return apps;

            foreach (var dir in Directory.EnumerateDirectories(Root))
            {
                var metaPath = Path.Combine(dir, MetaFile);
                if (!File.Exists(metaPath)) continue;
                try
                {
                    var meta = JsonSerializer.Deserialize(ReadAllTextShared(metaPath), LibraryJsonContext.Default.Meta);
                    if (meta is null) continue;
                    apps.Add(new WidgetApp(
                        meta.Id, meta.Title, meta.Icon, meta.Prompt, meta.Model,
                        meta.CreatedAt, dir, meta.ExePath, meta.PublishDir, meta.SessionId ?? ""));
                }
                catch (Exception ex)
                {
                    SessionLog.Write($"[Library] skip {dir}: {ex.Message}");
                }
            }
            return apps.OrderByDescending(a => a.CreatedAt).ToList();
        }
        finally
        {
            IoGate.Release();
        }
    }

    public void Delete(string id)
    {
        IoGate.Wait();
        try
        {
            var dir = Path.Combine(Root, id);
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch (Exception ex) { SessionLog.Write($"[Library] delete {id} failed: {ex.Message}"); }
        }
        finally
        {
            IoGate.Release();
        }
    }

    /// <summary>Read a widget's saved permission policy, or null if it uses the default.</summary>
    public string? ReadPolicy(WidgetApp app)
    {
        try { return File.Exists(app.PolicyPath) ? ReadAllTextShared(app.PolicyPath) : null; }
        catch (Exception ex)
        {
            SessionLog.Write($"[Library] read policy for {app.Id} failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Persist a widget's permission policy JSON.</summary>
    public async Task SavePolicyAsync(WidgetApp app, string json)
    {
        await IoGate.WaitAsync().ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(app.Dir);
            await WriteAtomicWithRetriesAsync(app.PolicyPath, json).ConfigureAwait(false);
            await _integrity.StampAsync(app).ConfigureAwait(false);
            SessionLog.Write($"[Library] saved policy for {app.Id}");
        }
        finally
        {
            IoGate.Release();
        }
    }

    /// <summary>Remove a widget's permission policy so it reverts to the default.</summary>
    public void ResetPolicy(WidgetApp app)
    {
        IoGate.Wait();
        try
        {
            try { if (File.Exists(app.PolicyPath)) File.Delete(app.PolicyPath); }
            catch (Exception ex) { SessionLog.Write($"[Library] reset policy for {app.Id} failed: {ex.Message}"); }
            _integrity.StampAsync(app).GetAwaiter().GetResult();
        }
        finally
        {
            IoGate.Release();
        }
    }

    static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    static async Task WriteAtomicWithRetriesAsync(string path, string text)
    {
        var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                tmp,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.Read,
                    Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                }))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(text).ConfigureAwait(false);
            }

            await ReplaceWithRetriesAsync(tmp, path).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch (Exception ex) when (IsTransientFileAccess(ex))
            {
                SessionLog.Write($"[Library] temp cleanup deferred for {tmp}: {ex.Message}");
            }
        }
    }

    static async Task ReplaceWithRetriesAsync(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (File.Exists(destination))
                    File.Replace(source, destination, destinationBackupFileName: null, ignoreMetadataErrors: true);
                else
                    File.Move(source, destination);
                return;
            }
            catch (Exception ex) when (IsTransientFileAccess(ex) && attempt < MaxIoAttempts)
            {
                SessionLog.Write($"[Library] meta save retry {attempt}/{MaxIoAttempts - 1}: {ex.Message}");
                await Task.Delay(50 * attempt).ConfigureAwait(false);
            }
        }
    }

    static bool IsTransientFileAccess(Exception ex) =>
        ex is IOException or UnauthorizedAccessException;

    sealed record Meta(
        string Id, string Title, string Icon, string Prompt, string Model,
        DateTime CreatedAt, string ExePath, string PublishDir, string? SessionId);

    // AOT/trim-safe JSON: source-generated metadata for the meta.json sidecar
    // (System.Text.Json reflection serialization is IL2026/IL3050). Nested so it
    // can see the private Meta; options mirror the former JsonOpts exactly.
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(Meta))]
    sealed partial class LibraryJsonContext : JsonSerializerContext
    {
    }
}
