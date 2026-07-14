using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace WidgetCreator.Services;

/// <summary>Outcome of an artifact-integrity check for a stored widget.</summary>
/// <param name="Ok">False only when a record exists and a covered artifact was
/// tampered with — callers must refuse to run.</param>
/// <param name="Present">Whether an integrity record existed at all.</param>
/// <param name="Reason">Human-readable detail (the tamper, or why it was skipped).</param>
public sealed record IntegrityResult(bool Ok, bool Present, string Reason);

/// <summary>
/// Tamper-evidence for a stored widget's on-disk artifacts (threat-model H-3).
/// A widget lives under <c>%LOCALAPPDATA%\WidgetCreator\apps\&lt;id&gt;</c> as
/// <c>widget.cs</c>, an optional <c>policy.json</c>, and the built
/// <c>widget.exe</c> — none of which were integrity-protected, so any process
/// running as the user could widen <c>policy.json</c> (e.g. add
/// <c>readwritePaths</c> over the profile) or swap <c>widget.exe</c>, which MXC
/// then grants read+execute. This records an HMAC-SHA256 over those three
/// artifacts (an <c>integrity.json</c> sidecar) at every legitimate save, and
/// re-verifies it before each relaunch, refusing to run a widget whose source,
/// policy, or executable changed underneath us.
///
/// <para>The HMAC key is 32 random bytes generated once and stored
/// DPAPI-protected (per-user) at <c>%LOCALAPPDATA%\WidgetCreator\integrity.key</c>,
/// so a naive file edit cannot re-sign the record. This is <b>tamper-evidence</b>,
/// not prevention: a determined same-user attacker can still call DPAPI to read
/// the key and re-stamp, and can delete a sidecar (treated as "unprotected",
/// re-stamped on next launch). It raises the bar and catches accidental or
/// unsophisticated tampering and corruption; strong prevention needs a
/// higher-integrity signer outside this sample's scope.</para>
/// </summary>
public sealed partial class ArtifactIntegrity
{
    const string SidecarName = "integrity.json";
    const int CurrentVersion = 1;

    readonly string _keyPath;
    readonly Lazy<byte[]> _key;

    public ArtifactIntegrity(string rootDir)
    {
        // rootDir is the ...\WidgetCreator\apps folder; keep the key one level up
        // alongside it, not inside a per-widget dir the widget could reach.
        var parent = Directory.GetParent(rootDir)?.FullName ?? rootDir;
        _keyPath = Path.Combine(parent, "integrity.key");
        _key = new Lazy<byte[]>(GetOrCreateKey);
    }

    static string SidecarPath(WidgetApp app) => Path.Combine(app.Dir, SidecarName);

    /// <summary>
    /// Record the current integrity of a widget's artifacts. Call after every
    /// legitimate mutation (save/update/repair, policy change/reset). Best-effort:
    /// logs and returns on failure rather than blocking a save.
    /// </summary>
    public async Task StampAsync(WidgetApp app)
    {
        try
        {
            var record = Compute(app);
            var json = JsonSerializer.Serialize(record, IntegrityJsonContext.Default.Record);
            var path = SidecarPath(app);
            var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
            await File.WriteAllTextAsync(tmp, json).ConfigureAwait(false);
            if (File.Exists(path)) File.Replace(tmp, path, null, ignoreMetadataErrors: true);
            else File.Move(tmp, path);
            SessionLog.Write($"[Integrity] stamped {app.Id} (exe={Short(record.ExeSha256)}, policy={(record.PolicySha256 is null ? "none" : Short(record.PolicySha256))})");
        }
        catch (Exception ex)
        {
            SessionLog.Write($"[Integrity] stamp failed for {app.Id}: {ex.Message}");
        }
    }

    /// <summary>
    /// Verify a widget's stored artifacts against its integrity record. Returns
    /// <c>Ok=false</c> only on positive tampering (bad MAC, or a source/policy/exe
    /// hash that no longer matches) — callers must then refuse to relaunch. A
    /// missing record is <c>Ok=true, Present=false</c> (unprotected legacy widget).
    /// </summary>
    public IntegrityResult Verify(WidgetApp app)
    {
        var path = SidecarPath(app);
        if (!File.Exists(path))
            return new IntegrityResult(Ok: true, Present: false, "no integrity record (unprotected)");
        try
        {
            var stored = JsonSerializer.Deserialize(File.ReadAllText(path), IntegrityJsonContext.Default.Record);
            if (stored is null)
                return new IntegrityResult(false, true, "integrity record is empty or invalid");

            // 1. The record must not itself have been edited (verify its own MAC).
            var expectedMac = ComputeMac(app.Id, stored.SourceSha256, stored.PolicySha256, stored.ExeSha256, stored.ExePath);
            if (!FixedTimeEquals(expectedMac, stored.Mac))
                return new IntegrityResult(false, true, "integrity record MAC mismatch (record tampered)");

            // 2. Each covered artifact must still hash to the recorded value.
            var live = Compute(app);
            if (!string.Equals(live.SourceSha256, stored.SourceSha256, StringComparison.OrdinalIgnoreCase))
                return new IntegrityResult(false, true, "widget.cs changed since last save");
            if (!string.Equals(live.PolicySha256 ?? "", stored.PolicySha256 ?? "", StringComparison.OrdinalIgnoreCase))
                return new IntegrityResult(false, true, "policy.json changed since last save");
            if (!string.Equals(live.ExeSha256, stored.ExeSha256, StringComparison.OrdinalIgnoreCase))
                return new IntegrityResult(false, true, "widget.exe changed since it was built");

            return new IntegrityResult(true, true, "artifacts verified");
        }
        catch (Exception ex)
        {
            return new IntegrityResult(false, true, $"integrity record unreadable: {ex.Message}");
        }
    }

    Record Compute(WidgetApp app)
    {
        var sourceSha = HashFileOrEmpty(app.SourcePath);
        var policySha = File.Exists(app.PolicyPath) ? HashFile(app.PolicyPath) : null;
        var exeSha = HashFileOrEmpty(app.ExePath);
        var exePath = NormalizePath(app.ExePath);
        var mac = ComputeMac(app.Id, sourceSha, policySha, exeSha, exePath);
        return new Record(CurrentVersion, sourceSha, policySha, exeSha, exePath, mac);
    }

    string ComputeMac(string id, string sourceSha, string? policySha, string exeSha, string exePath)
    {
        var canonical = $"v{CurrentVersion}|{id}|{sourceSha}|{policySha ?? "-"}|{exeSha}|{exePath}";
        using var hmac = new HMACSHA256(_key.Value);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    static string NormalizePath(string? p)
    {
        if (string.IsNullOrEmpty(p)) return "";
        try { return Path.GetFullPath(p).ToLowerInvariant(); }
        catch { return p.ToLowerInvariant(); }
    }

    static string HashFileOrEmpty(string? path) =>
        !string.IsNullOrEmpty(path) && File.Exists(path) ? HashFile(path) : "";

    static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    static bool FixedTimeEquals(string a, string b)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(a), Convert.FromHexString(b));
        }
        catch { return false; }
    }

    static string Short(string h) => h.Length <= 12 ? h : h[..12];

    // ── key management (DPAPI-protected, per-user) ───────────────────────────

    byte[] GetOrCreateKey()
    {
        try
        {
            if (File.Exists(_keyPath))
            {
                var protectedBytes = File.ReadAllBytes(_keyPath);
                var key = Dpapi.Unprotect(protectedBytes);
                if (key is { Length: 32 }) return key;
                SessionLog.Write("[Integrity] existing key unusable — regenerating.");
            }
        }
        catch (Exception ex)
        {
            SessionLog.Write($"[Integrity] key read failed ({ex.Message}) — regenerating.");
        }

        var fresh = RandomNumberGenerator.GetBytes(32);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_keyPath)!);
            File.WriteAllBytes(_keyPath, Dpapi.Protect(fresh));
            SessionLog.Write("[Integrity] generated a new DPAPI-protected integrity key.");
        }
        catch (Exception ex)
        {
            // Without a persisted key the MAC won't survive restarts, but the
            // per-session hash comparison still catches an exe/policy swap while
            // the app is running. Log and continue.
            SessionLog.Write($"[Integrity] could not persist key ({ex.Message}) — using session-only key.");
        }
        return fresh;
    }

    sealed record Record(
        int Version,
        string SourceSha256,
        string? PolicySha256,
        string ExeSha256,
        string ExePath,
        string Mac);

    // AOT/trim-safe JSON: source-generated metadata for the integrity sidecar
    // (System.Text.Json reflection serialization is IL2026/IL3050). Nested so it
    // can see the private Record; options mirror the former JsonOpts exactly.
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(Record))]
    sealed partial class IntegrityJsonContext : JsonSerializerContext
    {
    }

    /// <summary>Managed DPAPI (per-user) wrapper via <see cref="ProtectedData"/>.</summary>
    static class Dpapi
    {
        public static byte[] Protect(byte[] data) =>
            ProtectedData.Protect(data, optionalEntropy: null, DataProtectionScope.CurrentUser);

        public static byte[]? Unprotect(byte[] data)
        {
            try { return ProtectedData.Unprotect(data, optionalEntropy: null, DataProtectionScope.CurrentUser); }
            catch { return null; }
        }
    }
}
