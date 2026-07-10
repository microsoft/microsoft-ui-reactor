using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WidgetCreator.Services;

/// <summary>Friendly network choices surfaced in the permissions dialog.</summary>
public enum NetworkAccess
{
    /// <summary>No outbound network (no <c>internetClient</c> capability, default-block).</summary>
    None,
    /// <summary>Outbound internet allowed (today's default).</summary>
    Internet,
}

/// <summary>
/// Per-path filesystem access tier. Maps 1:1 to MXC's three path lists. MXC
/// bundles execute with read (RO = read+execute, RW = read+write+execute+delete),
/// so there is no standalone execute tier.
/// </summary>
public enum PathAccess
{
    /// <summary>Read (and execute) only — <c>filesystem.readonlyPaths</c>.</summary>
    ReadOnly,
    /// <summary>Read and write — <c>filesystem.readwritePaths</c>.</summary>
    ReadWrite,
    /// <summary>Explicitly denied — <c>filesystem.deniedPaths</c>.</summary>
    Denied,
}

/// <summary>
/// Helpers for a widget's MXC permission policy. The policy is a JSON "template"
/// (a partial <c>ContainerConfig</c>) that <see cref="MxcSandbox"/> merges with
/// the computed run fields (process command line/cwd + the app-dir grant) at
/// launch. A widget with no stored policy uses <see cref="DefaultJson"/> — a
/// least-privilege baseline: a visible window with <b>no</b> outbound network, no
/// clipboard access, and no input injection. Network (and any other capability) is
/// opt-in per widget via the Permissions dialog.
///
/// <para>The JSON string is the single source of truth. The dialog's friendly
/// controls are typed accessors over it, and advanced mode edits it directly, so
/// arbitrary <c>ContainerConfig</c> fields (hosts, proxy, capabilities, env, …)
/// survive a round-trip even if the friendly form does not surface them.</para>
/// </summary>
public static class MxcPolicy
{
    public const string InternetCapability = "internetClient";

    const string ReadWriteKind = "readwritePaths";
    const string ReadOnlyKind = "readonlyPaths";
    const string DeniedKind = "deniedPaths";
    static readonly string[] PathKinds = [ReadWriteKind, ReadOnlyKind, DeniedKind];

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>The default policy (least privilege) as a mutable JSON object.</summary>
    public static JsonObject DefaultTemplate() => new()
    {
        ["version"] = MxcSandbox.SchemaVersion,
        ["containment"] = "processcontainer",
        ["appContainer"] = new JsonObject
        {
            // H-2: no capabilities by default (no outbound network) until the user
            // grants them per-widget in the Permissions dialog. leastPrivilege stays
            // FALSE on purpose — the stricter AppContainer breaks WinUI window
            // creation (managed exception at MountAndActivate); turning off network
            // already removes the main default exfiltration channel.
            ["leastPrivilege"] = false,
            ["capabilities"] = new JsonArray(),
        },
        ["network"] = new JsonObject
        {
            // H-2: outbound network is opt-in, not on by default.
            ["defaultPolicy"] = "block",
            ["enforcementMode"] = "capabilities",
        },
        ["ui"] = new JsonObject
        {
            ["disable"] = false,
            ["clipboard"] = "none",
            ["injection"] = false,
        },
    };

    /// <summary>Pretty-printed default policy JSON.</summary>
    public static string DefaultJson => DefaultTemplate().ToJsonString(Pretty);

    /// <summary>Parse a policy JSON document into a mutable object.</summary>
    public static bool TryParse(string? json, out JsonObject obj, out string? error)
    {
        obj = new JsonObject();
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            obj = DefaultTemplate();
            return true;
        }
        try
        {
            if (JsonNode.Parse(json) is JsonObject parsed)
            {
                obj = parsed;
                return true;
            }
            error = "Policy must be a JSON object.";
            return false;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string Prettify(JsonObject obj) => obj.ToJsonString(Pretty);

    // ── friendly getters (read from a parsed, valid policy object) ──────────

    public static bool GetShowWindow(JsonObject o) => !GetBool(o, "ui", "disable", false);
    public static bool GetInjection(JsonObject o) => GetBool(o, "ui", "injection", false);
    public static string GetClipboard(JsonObject o) => GetString(o, "ui", "clipboard") ?? "none";
    public static bool GetLeastPrivilege(JsonObject o) => GetBool(o, "appContainer", "leastPrivilege", false);
    public static bool GetAllowLocalNetwork(JsonObject o) => GetBool(o, "network", "allowLocalNetwork", false);

    public static NetworkAccess GetNetwork(JsonObject o)
    {
        var hasInternet = (o["appContainer"] as JsonObject)?["capabilities"] is JsonArray caps &&
            caps.Any(n => string.Equals((string?)n, InternetCapability, StringComparison.OrdinalIgnoreCase));
        var defaultPolicy = GetString(o, "network", "defaultPolicy") ?? "block";
        return hasInternet && string.Equals(defaultPolicy, "allow", StringComparison.OrdinalIgnoreCase)
            ? NetworkAccess.Internet
            : NetworkAccess.None;
    }

    public static string[] GetPaths(JsonObject o, string kind) =>
        (o["filesystem"] as JsonObject)?[kind] is JsonArray arr
            ? arr.Select(n => (string?)n ?? "").Where(s => s.Length > 0).ToArray()
            : Array.Empty<string>();

    /// <summary>
    /// All explicitly-listed filesystem entries (read-write, read-only, denied),
    /// de-duplicated (RW &gt; RO &gt; Denied wins if a path somehow appears twice)
    /// and ordered by path for a stable UI.
    /// </summary>
    public static (string Path, PathAccess Access)[] GetFileEntries(JsonObject o)
    {
        var pairs = new (string Kind, PathAccess Access)[]
        {
            (ReadWriteKind, PathAccess.ReadWrite),
            (ReadOnlyKind, PathAccess.ReadOnly),
            (DeniedKind, PathAccess.Denied),
        };
        return pairs
            .SelectMany(p => GetPaths(o, p.Kind).Select(path => (Path: path, p.Access)))
            .GroupBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // ── friendly setters (take the JSON string, return a new JSON string) ───

    public static string WithShowWindow(string json, bool show) =>
        Mutate(json, o => Obj(o, "ui")["disable"] = !show);

    public static string WithInjection(string json, bool on) =>
        Mutate(json, o => Obj(o, "ui")["injection"] = on);

    public static string WithClipboard(string json, string level) =>
        Mutate(json, o => Obj(o, "ui")["clipboard"] = level);

    public static string WithLeastPrivilege(string json, bool on) =>
        Mutate(json, o => Obj(o, "appContainer")["leastPrivilege"] = on);

    public static string WithAllowLocalNetwork(string json, bool on) =>
        Mutate(json, o => Obj(o, "network")["allowLocalNetwork"] = on);

    public static string WithNetwork(string json, NetworkAccess access) => Mutate(json, o =>
    {
        var caps = Arr(Obj(o, "appContainer"), "capabilities");
        var net = Obj(o, "network");
        var existing = caps.Any(n => string.Equals((string?)n, InternetCapability, StringComparison.OrdinalIgnoreCase));
        if (access == NetworkAccess.Internet)
        {
            if (!existing) caps.Add(JsonValue.Create(InternetCapability));
            net["defaultPolicy"] = "allow";
            net["enforcementMode"] ??= "capabilities";
        }
        else
        {
            for (var i = caps.Count - 1; i >= 0; i--)
                if (string.Equals((string?)caps[i], InternetCapability, StringComparison.OrdinalIgnoreCase))
                    caps.RemoveAt(i);
            net["defaultPolicy"] = "block";
        }
    });

    public static string WithPaths(string json, string kind, string[] paths) => Mutate(json, o =>
    {
        var fs = Obj(o, "filesystem");
        if (paths.Length == 0)
        {
            fs.Remove(kind);
            return;
        }
        var arr = new JsonArray();
        foreach (var p in paths) arr.Add(JsonValue.Create(p));
        fs[kind] = arr;
    });

    /// <summary>
    /// Add a path (or move an existing one) to the given access tier. The path is
    /// first removed from every tier so it lives in exactly one list.
    /// </summary>
    public static string WithFileAccess(string json, string path, PathAccess access) => Mutate(json, o =>
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        RemovePathFromAll(o, path);
        var kind = access switch
        {
            PathAccess.ReadWrite => ReadWriteKind,
            PathAccess.Denied => DeniedKind,
            _ => ReadOnlyKind,
        };
        Arr(Obj(o, "filesystem"), kind).Add(JsonValue.Create(path.Trim()));
        PruneFilesystem(o);
    });

    /// <summary>Remove a path from every filesystem tier.</summary>
    public static string WithoutPath(string json, string path) => Mutate(json, o =>
    {
        RemovePathFromAll(o, path);
        PruneFilesystem(o);
    });

    static void RemovePathFromAll(JsonObject o, string path)
    {
        if (o["filesystem"] is not JsonObject fs) return;
        foreach (var arr in PathKinds.Select(k => fs[k]).OfType<JsonArray>())
            for (var i = arr.Count - 1; i >= 0; i--)
                if (string.Equals((string?)arr[i], path, StringComparison.OrdinalIgnoreCase))
                    arr.RemoveAt(i);
    }

    /// <summary>Drop empty path arrays, and the <c>filesystem</c> object if it has nothing left.</summary>
    static void PruneFilesystem(JsonObject o)
    {
        if (o["filesystem"] is not JsonObject fs) return;
        foreach (var kind in PathKinds.Where(k => fs[k] is JsonArray { Count: 0 }))
            fs.Remove(kind);
        if (fs.Count == 0)
            o.Remove("filesystem");
    }

    // ── internals ───────────────────────────────────────────────────────────

    static string Mutate(string json, Action<JsonObject> change)
    {
        if (!TryParse(json, out var o, out _))
            return json; // friendly edits only happen on valid JSON
        change(o);
        return o.ToJsonString(Pretty);
    }

    /// <summary>Get-or-create a child object under <paramref name="key"/>.</summary>
    static JsonObject Obj(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject existing) return existing;
        var created = new JsonObject();
        parent[key] = created;
        return created;
    }

    /// <summary>Get-or-create a child array under <paramref name="key"/>.</summary>
    static JsonArray Arr(JsonObject parent, string key)
    {
        if (parent[key] is JsonArray existing) return existing;
        var created = new JsonArray();
        parent[key] = created;
        return created;
    }

    static bool GetBool(JsonObject o, string section, string key, bool fallback)
    {
        try { return (o[section] as JsonObject)?[key]?.GetValue<bool>() ?? fallback; }
        catch { return fallback; }
    }

    static string? GetString(JsonObject o, string section, string key)
    {
        try { return (o[section] as JsonObject)?[key]?.GetValue<string>(); }
        catch { return null; }
    }
}
