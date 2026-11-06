using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Mxc.Sdk.V1;

namespace WidgetCreator.Services;

/// <summary>Friendly network choices surfaced in the permissions dialog.</summary>
public enum NetworkAccess
{
    /// <summary>No outbound network (<c>network.egress.default = "deny"</c>).</summary>
    None,
    /// <summary>Outbound internet allowed (<c>network.egress.default = "allow"</c>).</summary>
    Internet,
    /// <summary>Egress defaults to deny, but advanced <c>network.egress.allow</c>
    /// rules permit specific destinations (edit them in Advanced).</summary>
    Custom,
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
/// A widget's MXC permission policy. The policy is a small JSON document that
/// mirrors the field names of the stable MXC 1.0 containment contract — the
/// cross-backend <c>filesystem</c>, <c>network</c>, and <c>ui</c> sections plus
/// <c>processContainer.capabilities</c>:
/// <code>
/// {
///   "filesystem": { "readonlyPaths": [], "readwritePaths": [], "deniedPaths": [] },
///   "network": {
///     "egress": { "default": "deny", "allow": [], "deny": [] },
///     "ingress": { "default": "deny", "hostLoopback": "deny" }
///   },
///   "ui": { "disable": false, "clipboard": "none", "injection": false },
///   "processContainer": { "capabilities": [] }
/// }
/// </code>
/// <see cref="ToContainerRequest"/> maps it onto the typed
/// <c>Microsoft.Mxc.Sdk.V1</c> request at launch (the SDK owns the wire schema
/// version; we never hand MXC raw JSON). A widget with no stored policy uses
/// <see cref="DefaultJson"/> — a least-privilege baseline: a visible window with
/// <b>no</b> network, no clipboard access, and no input injection. Network (and
/// any other capability) is opt-in per widget via the Permissions dialog.
///
/// <para>The JSON string is the single source of truth. The dialog's friendly
/// controls are typed accessors over it, and advanced mode edits it directly.
/// Like the MXC 1.0 contract, the document is closed: unknown fields are
/// rejected rather than silently ignored. Policies saved by older builds (the
/// pre-1.0 wxc-exec <c>ContainerConfig</c> shape) are migrated on read.</para>
/// </summary>
public static class MxcPolicy
{
    const string ReadWriteKind = "readwritePaths";
    const string ReadOnlyKind = "readonlyPaths";
    const string DeniedKind = "deniedPaths";
    static readonly string[] PathKinds = [ReadWriteKind, ReadOnlyKind, DeniedKind];

    /// <summary>Clipboard levels accepted by MXC 1.0 (<c>ui.clipboard</c>).</summary>
    public static readonly string[] ClipboardLevels = ["none", "read", "write", "all"];

    static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>The default policy (least privilege) as a mutable JSON object.</summary>
    public static JsonObject DefaultTemplate() => new()
    {
        ["network"] = new JsonObject
        {
            // H-2: outbound network is opt-in, not on by default. MXC derives the
            // AppContainer network capabilities (internetClient /
            // privateNetworkClientServer) from these directional actions.
            ["egress"] = new JsonObject { ["default"] = "deny" },
            ["ingress"] = new JsonObject { ["default"] = "deny", ["hostLoopback"] = "deny" },
        },
        ["ui"] = new JsonObject
        {
            // MXC disables UI by default; widgets are windowed apps.
            ["disable"] = false,
            ["clipboard"] = "none",
            ["injection"] = false,
        },
    };

    /// <summary>Pretty-printed default policy JSON.</summary>
    public static string DefaultJson => DefaultTemplate().ToJsonString(Pretty);

    /// <summary>
    /// Parse, migrate (legacy pre-1.0 documents), and validate a policy JSON
    /// document. Blank input yields the default policy.
    /// </summary>
    public static bool TryParse(string? json, out JsonObject obj, out string? error)
    {
        obj = DefaultTemplate();
        error = null;
        if (string.IsNullOrWhiteSpace(json))
            return true;

        JsonObject parsed;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o)
            {
                error = "Policy must be a JSON object.";
                return false;
            }
            parsed = o;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }

        try
        {
            if (IsLegacy(parsed))
            {
                if (UnsupportedLegacyFields(parsed) is { Count: > 0 } unsupported)
                {
                    // Fail closed: dropping these would silently widen access.
                    error = "This policy was saved by an older Widget Creator and uses settings MXC 1.0 can't "
                        + $"express ({string.Join(", ", unsupported)}). Review and save it again (or reset it); "
                        + "until then the widget runs with the default policy instead (no network, no file "
                        + "access beyond its own folder) and these settings are NOT applied.";
                    return false;
                }
                parsed = MigrateLegacy(parsed);
            }

            error = Validate(parsed);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or JsonException)
        {
            error = $"Policy could not be read: {ex.Message}";
        }
        if (error is not null)
            return false;

        obj = parsed;
        return true;
    }

    public static string Prettify(JsonObject obj) => obj.ToJsonString(Pretty);

    // ── mapping to the MXC SDK ─────────────────────────────────────────────

    /// <summary>
    /// Build the typed MXC request for <paramref name="commandLine"/> from a
    /// validated policy, always adding a read grant on <paramref name="appDir"/>
    /// so the widget can launch regardless of the chosen policy.
    /// </summary>
    public static ContainerRequest ToContainerRequest(
        JsonObject policy, string commandLine, string appDir, uint timeoutMs)
    {
        var filesystem = new FilesystemPolicy();
        filesystem.ReadwritePaths.AddRange(GetPaths(policy, ReadWriteKind));
        filesystem.ReadonlyPaths.AddRange(GetPaths(policy, ReadOnlyKind));
        filesystem.DeniedPaths.AddRange(GetPaths(policy, DeniedKind));
        if (!filesystem.ReadonlyPaths.Concat(filesystem.ReadwritePaths)
                .Any(p => string.Equals(p, appDir, StringComparison.OrdinalIgnoreCase)))
            filesystem.ReadonlyPaths.Add(appDir);

        var egress = Section(policy, "network", "egress");
        var ingress = Section(policy, "network", "ingress");

        var capabilities = (Section(policy, "processContainer")?["capabilities"] as JsonArray)?
            .Select(n => (string?)n ?? "").Where(s => s.Length > 0).ToList() ?? new List<string>();

        return new ContainerRequest(commandLine)
        {
            WorkingDirectory = appDir,
            TimeoutMs = timeoutMs == 0 ? null : timeoutMs,
            Filesystem = filesystem,
            // Always send an explicit network section so the default-deny posture
            // is stated rather than inferred.
            Network = new NetworkPolicy
            {
                Egress = new NetworkEgressPolicy
                {
                    Default = ParseAction(GetString(egress, "default")),
                    Allow = ParseRules(egress?["allow"] as JsonArray),
                    Deny = ParseRules(egress?["deny"] as JsonArray),
                },
                Ingress = new NetworkIngressPolicy
                {
                    Default = ParseAction(GetString(ingress, "default")),
                    HostLoopback = ParseAction(GetString(ingress, "hostLoopback")),
                },
            },
            Ui = new UiPolicy
            {
                Disable = !GetShowWindow(policy),
                Clipboard = GetClipboard(policy) switch
                {
                    "read" => ClipboardPolicy.Read,
                    "write" => ClipboardPolicy.Write,
                    "all" => ClipboardPolicy.All,
                    _ => ClipboardPolicy.None,
                },
                AllowInputInjection = GetInjection(policy),
            },
            Containment = new Containment.ProcessContainer { Capabilities = capabilities },
        };
    }

    static NetworkAction ParseAction(string? value) =>
        string.Equals(value, "allow", StringComparison.OrdinalIgnoreCase) ? NetworkAction.Allow : NetworkAction.Deny;

    static List<NetworkRulePolicy>? ParseRules(JsonArray? rules)
    {
        if (rules is null || rules.Count == 0) return null;
        var result = new List<NetworkRulePolicy>();
        foreach (var rule in rules.OfType<JsonObject>())
        {
            var r = new NetworkRulePolicy();
            if (rule["to"] is JsonArray to)
            {
                r.To = to.OfType<JsonObject>().Select(peer => new NetworkPeerPolicy(GetString(peer, "cidr") ?? "")
                {
                    Except = (peer["except"] as JsonArray)?.Select(e => (string?)e ?? "").ToList(),
                }).ToList();
            }
            if (rule["ports"] is JsonArray ports)
            {
                r.Ports = ports.OfType<JsonObject>().Select(p => new NetworkPortPolicy
                {
                    Protocol = GetString(p, "protocol") switch
                    {
                        "tcp" => NetworkProtocol.Tcp,
                        "udp" => NetworkProtocol.Udp,
                        "icmp" => NetworkProtocol.Icmp,
                        "any" => NetworkProtocol.Any,
                        _ => null,
                    },
                    Port = GetUShort(p, "port"),
                    EndPort = GetUShort(p, "endPort"),
                }).ToList();
            }
            result.Add(r);
        }
        return result;
    }

    // ── friendly getters (read from a parsed, valid policy object) ──────────

    public static bool GetShowWindow(JsonObject o) => !GetBool(Section(o, "ui"), "disable", false);
    public static bool GetInjection(JsonObject o) => GetBool(Section(o, "ui"), "injection", false);
    public static string GetClipboard(JsonObject o) => GetString(Section(o, "ui"), "clipboard") ?? "none";

    /// <summary>
    /// Inbound/private-network access (MXC grants <c>privateNetworkClientServer</c>
    /// for <c>ingress.default: allow</c>); host-loopback access counts too.
    /// </summary>
    public static bool GetAllowLocalNetwork(JsonObject o)
    {
        var ingress = Section(o, "network", "ingress");
        return string.Equals(GetString(ingress, "default"), "allow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(GetString(ingress, "hostLoopback"), "allow", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Outbound access: <see cref="NetworkAccess.Internet"/> when egress defaults to
    /// allow, <see cref="NetworkAccess.Custom"/> when it defaults to deny but
    /// advanced <c>egress.allow</c> rules still permit some destinations.
    /// </summary>
    public static NetworkAccess GetNetwork(JsonObject o)
    {
        var egress = Section(o, "network", "egress");
        if (string.Equals(GetString(egress, "default"), "allow", StringComparison.OrdinalIgnoreCase))
            return NetworkAccess.Internet;
        return egress?["allow"] is JsonArray { Count: > 0 } ? NetworkAccess.Custom : NetworkAccess.None;
    }

    public static string[] GetPaths(JsonObject o, string kind) =>
        Section(o, "filesystem")?[kind] is JsonArray arr
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

    /// <summary>Turning local network off also closes host loopback.</summary>
    public static string WithAllowLocalNetwork(string json, bool on) => Mutate(json, o =>
    {
        var ingress = Obj(Obj(o, "network"), "ingress");
        ingress["default"] = on ? "allow" : "deny";
        if (!on)
            ingress["hostLoopback"] = "deny";
    });

    /// <summary>
    /// <see cref="NetworkAccess.None"/> also drops advanced <c>egress.allow</c>
    /// and <c>egress.deny</c> rules: "no network" then really means no outbound
    /// access, a deny list is redundant once the default is deny, and MXC's
    /// AppContainer fallback tier rejects any explicit egress rule.
    /// <see cref="NetworkAccess.Custom"/> is not a settable choice and is ignored.
    /// </summary>
    public static string WithNetwork(string json, NetworkAccess access) => access switch
    {
        NetworkAccess.Internet => Mutate(json, o => Obj(Obj(o, "network"), "egress")["default"] = "allow"),
        NetworkAccess.None => Mutate(json, o =>
        {
            var network = Obj(o, "network");
            var egress = Obj(network, "egress");
            egress["default"] = "deny";
            egress.Remove("allow");
            egress.Remove("deny");
            // Inbound/LAN access without outbound access is a combination MXC's
            // AppContainer fallback tier rejects, so "no network" closes it too.
            var ingress = Obj(network, "ingress");
            ingress["default"] = "deny";
            ingress["hostLoopback"] = "deny";
        }),
        _ => json,
    };

    public static string WithPaths(string json, string kind, string[] paths) => Mutate(json, o =>
    {
        var fs = Obj(o, "filesystem");
        if (paths.Length == 0)
        {
            fs.Remove(kind);
            PruneFilesystem(o);
            return;
        }
        var arr = new JsonArray();
        foreach (var p in paths) arr.Add((JsonNode?)JsonValue.Create(p));
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
        Arr(Obj(o, "filesystem"), kind).Add((JsonNode?)JsonValue.Create(path.Trim()));
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
        foreach (var kind in PathKinds.Where(k => fs[k] is JsonArray { Count: 0 }).ToArray())
            fs.Remove(kind);
        if (fs.Count == 0)
            o.Remove("filesystem");
    }

    // ── validation (closed schema, mirroring MXC 1.0) ───────────────────────

    static readonly Dictionary<string, string[]> AllowedKeys = new()
    {
        [""] = ["_comment", "filesystem", "network", "ui", "processContainer"],
        ["filesystem"] = [ReadWriteKind, ReadOnlyKind, DeniedKind],
        ["network"] = ["egress", "ingress"],
        ["network.egress"] = ["default", "allow", "deny"],
        ["network.ingress"] = ["default", "hostLoopback"],
        ["ui"] = ["disable", "clipboard", "injection"],
        ["processContainer"] = ["capabilities"],
    };

    static string? Validate(JsonObject o)
    {
        var errors = new List<string>();
        CheckKeys(o, "", errors);

        if (o["filesystem"] is { } fsNode)
        {
            if (fsNode is not JsonObject fs) errors.Add("'filesystem' must be an object.");
            else
            {
                CheckKeys(fs, "filesystem", errors);
                foreach (var kind in PathKinds)
                    CheckStringArray(fs[kind], $"filesystem.{kind}", errors);
            }
        }

        if (o["network"] is { } netNode)
        {
            if (netNode is not JsonObject net) errors.Add("'network' must be an object.");
            else
            {
                CheckKeys(net, "network", errors);
                if (net["egress"] is { } egNode)
                {
                    if (egNode is not JsonObject eg) errors.Add("'network.egress' must be an object.");
                    else
                    {
                        CheckKeys(eg, "network.egress", errors);
                        CheckAction(eg["default"], "network.egress.default", errors);
                        CheckRules(eg["allow"], "network.egress.allow", errors);
                        CheckRules(eg["deny"], "network.egress.deny", errors);
                    }
                }
                if (net["ingress"] is { } inNode)
                {
                    if (inNode is not JsonObject ing) errors.Add("'network.ingress' must be an object.");
                    else
                    {
                        CheckKeys(ing, "network.ingress", errors);
                        CheckAction(ing["default"], "network.ingress.default", errors);
                        CheckAction(ing["hostLoopback"], "network.ingress.hostLoopback", errors);
                    }
                }
            }
        }

        if (o["ui"] is { } uiNode)
        {
            if (uiNode is not JsonObject ui) errors.Add("'ui' must be an object.");
            else
            {
                CheckKeys(ui, "ui", errors);
                CheckBool(ui["disable"], "ui.disable", errors);
                CheckBool(ui["injection"], "ui.injection", errors);
                if (ui["clipboard"] is { } clip &&
                    (!TryGetString(clip, out var level) || !ClipboardLevels.Contains(level)))
                    errors.Add($"'ui.clipboard' must be one of: {string.Join(", ", ClipboardLevels)}.");
            }
        }

        if (o["processContainer"] is { } pcNode)
        {
            if (pcNode is not JsonObject pc) errors.Add("'processContainer' must be an object.");
            else
            {
                CheckKeys(pc, "processContainer", errors);
                CheckStringArray(pc["capabilities"], "processContainer.capabilities", errors);
                if (pc["capabilities"] is JsonArray caps &&
                    caps.Any(c => TryGetString(c, out var s) && s.Contains(',')))
                    errors.Add("'processContainer.capabilities' entries must be single names (no commas).");
            }
        }

        return errors.Count == 0 ? null : string.Join(" ", errors);
    }

    static void CheckKeys(JsonObject o, string path, List<string> errors)
    {
        var allowed = AllowedKeys[path];
        foreach (var key in o.Select(kv => kv.Key).Where(k => !allowed.Contains(k)))
            errors.Add($"Unknown field '{(path.Length == 0 ? key : path + "." + key)}'.");
    }

    static void CheckStringArray(JsonNode? node, string path, List<string> errors)
    {
        if (node is null) return;
        if (node is not JsonArray arr || arr.Any(n => !TryGetString(n, out _)))
            errors.Add($"'{path}' must be an array of strings.");
    }

    static void CheckAction(JsonNode? node, string path, List<string> errors)
    {
        if (node is null) return;
        if (!TryGetString(node, out var s) || (s != "allow" && s != "deny"))
            errors.Add($"'{path}' must be \"allow\" or \"deny\".");
    }

    static void CheckBool(JsonNode? node, string path, List<string> errors)
    {
        if (node is null) return;
        if (node is not JsonValue v || !v.TryGetValue<bool>(out _))
            errors.Add($"'{path}' must be true or false.");
    }

    static void CheckRules(JsonNode? node, string path, List<string> errors)
    {
        if (node is null) return;
        if (node is not JsonArray rules)
        {
            errors.Add($"'{path}' must be an array of rules.");
            return;
        }
        foreach (var rule in rules)
        {
            if (rule is not JsonObject r)
            {
                errors.Add($"'{path}' entries must be objects.");
                continue;
            }
            foreach (var key in r.Select(kv => kv.Key).Where(k => k is not ("to" or "ports")))
                errors.Add($"Unknown field '{path}[].{key}'.");
            if (r["to"] is { } to && (to is not JsonArray peers || peers.Any(p => p is not JsonObject po || !IsValidPeer(po))))
                errors.Add($"'{path}[].to' must be an array of {{ \"cidr\": \"...\", \"except\": [\"...\"] }} entries "
                    + "with valid CIDRs (e.g. \"203.0.113.0/24\"; host bits zero), each 'except' inside its 'cidr'.");
            if (r["ports"] is { } ports && (ports is not JsonArray portList ||
                portList.Any(p => p is not JsonObject po || !IsValidPort(po))))
                errors.Add($"'{path}[].ports' must be an array of {{ \"protocol\": tcp|udp|icmp|any, "
                    + "\"port\": 1-65535, \"endPort\": port-65535 } entries (endPort needs port; icmp takes no ports).");
        }
    }

    static bool IsValidPeer(JsonObject p)
    {
        if (!p.All(kv => kv.Key is "cidr" or "except")) return false;
        if (!TryGetString(p["cidr"], out var cidrText) || !TryParseCidr(cidrText, out var cidr)) return false;
        if (p["except"] is null) return true;
        return p["except"] is JsonArray ex
            && ex.All(e => TryGetString(e, out var s) && TryParseCidr(s, out var excluded) && Contains(cidr, excluded));
    }

    /// <summary>
    /// Parse a CIDR the way MXC 1.0 does (Rust <c>cidr::IpCidr</c>): a strict
    /// dotted-quad IPv4 or an IPv6 address, an optional <c>/prefix</c> within the
    /// family's width (a bare address is a single host), and no host bits set.
    /// </summary>
    internal static bool TryParseCidr(string text, out (byte[] Network, int Prefix) cidr)
    {
        cidr = default;
        var slash = text.IndexOf('/');
        if (!TryParseAddress(slash < 0 ? text : text[..slash], out var bytes)) return false;
        var width = bytes.Length * 8;
        var prefix = width;
        if (slash >= 0)
        {
            var prefixText = text.AsSpan(slash + 1);
            if (prefixText.Length is 0 or > 3 || !int.TryParse(prefixText, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out prefix) || prefix > width)
                return false;
        }
        for (var bit = prefix; bit < width; bit++)
            if ((bytes[bit / 8] & (0x80 >> (bit % 8))) != 0)
                return false;
        cidr = (bytes, prefix);
        return true;
    }

    static bool TryParseAddress(string text, out byte[] bytes)
    {
        bytes = [];
        if (text.Contains(':'))
        {
            if (text.Contains('%') || !System.Net.IPAddress.TryParse(text, out var v6)
                || v6.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
                return false;
            bytes = v6.GetAddressBytes();
            return true;
        }
        var octets = text.Split('.');
        if (octets.Length != 4) return false;
        var result = new byte[4];
        for (var i = 0; i < 4; i++)
        {
            var o = octets[i];
            if (o.Length is 0 or > 3 || !o.All(char.IsAsciiDigit) || (o.Length > 1 && o[0] == '0')) return false;
            var value = int.Parse(o, System.Globalization.CultureInfo.InvariantCulture);
            if (value > 255) return false;
            result[i] = (byte)value;
        }
        bytes = result;
        return true;
    }

    static bool Contains((byte[] Network, int Prefix) outer, (byte[] Network, int Prefix) inner)
    {
        if (outer.Network.Length != inner.Network.Length || inner.Prefix < outer.Prefix) return false;
        for (var bit = 0; bit < outer.Prefix; bit++)
        {
            var mask = 0x80 >> (bit % 8);
            if ((outer.Network[bit / 8] & mask) != (inner.Network[bit / 8] & mask)) return false;
        }
        return true;
    }

    /// <summary>MXC 1.0 port selector rules: ports 1-65535, <c>endPort</c> needs and
    /// is ≥ <c>port</c>, and ICMP selectors carry no ports.</summary>
    static bool IsValidPort(JsonObject p)
    {
        if (p.Any(kv => kv.Key is not ("protocol" or "port" or "endPort"))) return false;
        var protocol = "any";
        if (p["protocol"] is { } proto &&
            (!TryGetString(proto, out protocol) || protocol is not ("tcp" or "udp" or "icmp" or "any")))
            return false;
        int? port = null, endPort = null;
        foreach (var key in new[] { "port", "endPort" })
        {
            if (p[key] is not { } n) continue;
            if (n is not JsonValue v || !v.TryGetValue<int>(out var i) || i is < 1 or > 65535)
                return false;
            if (key == "port") port = i; else endPort = i;
        }
        if (protocol == "icmp" && (port is not null || endPort is not null)) return false;
        if (endPort is not null && (port is null || endPort < port)) return false;
        return true;
    }

    // ── legacy (pre-1.0 wxc-exec ContainerConfig) migration ─────────────────

    /// <summary>A pre-1.0 document carries schema/backends fields the 1.0 shape doesn't.</summary>
    static bool IsLegacy(JsonObject o) =>
        o.ContainsKey("version") || o.ContainsKey("containment") || o.ContainsKey("appContainer")
        || (o["network"] is JsonObject n && (n.ContainsKey("defaultPolicy") || n.ContainsKey("allowLocalNetwork")
            || n.ContainsKey("enforcementMode")));

    /// <summary>
    /// Legacy fields that MXC 1.0 (and this policy shape) cannot express. Several
    /// of them were restrictions (e.g. <c>network.blockedHosts</c>, a proxy,
    /// <c>leastPrivilege: true</c>), so migration refuses rather than drops them.
    /// </summary>
    static List<string> UnsupportedLegacyFields(JsonObject legacy)
    {
        var supported = new Dictionary<string, string[]>
        {
            [""] = ["version", "containment", "appContainer", "network", "ui", "filesystem", "process"],
            ["appContainer"] = ["capabilities", "leastPrivilege"],
            ["network"] = ["defaultPolicy", "enforcementMode", "allowLocalNetwork"],
            ["ui"] = ["disable", "clipboard", "injection"],
            ["filesystem"] = [ReadWriteKind, ReadOnlyKind, DeniedKind],
        };
        var result = legacy.Select(kv => kv.Key).Where(k => !supported[""].Contains(k)).ToList();
        foreach (var section in supported.Keys.Where(k => k.Length > 0 && legacy[k] is JsonObject))
            result.AddRange(legacy[section]!.AsObject().Select(kv => kv.Key)
                .Where(k => !supported[section].Contains(k)).Select(k => $"{section}.{k}"));
        if (GetBool(Section(legacy, "appContainer"), "leastPrivilege", false))
            result.Add("appContainer.leastPrivilege");
        return result;
    }

    /// <summary>
    /// Translate a legacy policy (schema 0.6.0-alpha <c>ContainerConfig</c>) into
    /// the MXC 1.0 shape. Filesystem and UI settings carry over; legacy internet
    /// access (<c>internetClient</c> + <c>defaultPolicy: allow</c>) becomes
    /// <c>egress.default: allow</c>, and <c>allowLocalNetwork</c> (only together
    /// with internet access) becomes <c>ingress.default: allow</c>. Non-network
    /// AppContainer capabilities move to <c>processContainer.capabilities</c>.
    /// Callers must first reject <see cref="UnsupportedLegacyFields"/>; the
    /// remaining schema-only fields (<c>version</c>, <c>containment</c>, …) are dropped.
    /// </summary>
    static JsonObject MigrateLegacy(JsonObject legacy)
    {
        var result = DefaultTemplate();

        if (legacy["filesystem"] is JsonObject fs)
        {
            var newFs = new JsonObject();
            foreach (var kind in PathKinds.Where(k => fs[k] is JsonArray { Count: > 0 }))
                newFs[kind] = fs[kind]!.DeepClone();
            if (newFs.Count > 0)
                result["filesystem"] = newFs;
        }

        if (legacy["ui"] is JsonObject ui)
        {
            var newUi = (JsonObject)result["ui"]!;
            if (ui["disable"] is JsonValue d && d.TryGetValue<bool>(out var disable)) newUi["disable"] = disable;
            if (ui["injection"] is JsonValue inj && inj.TryGetValue<bool>(out var injection)) newUi["injection"] = injection;
            if (TryGetString(ui["clipboard"], out var clip))
                newUi["clipboard"] = clip == "readwrite" ? "all" : ClipboardLevels.Contains(clip) ? clip : "none";
        }

        var legacyCaps = (Section(legacy, "appContainer")?["capabilities"] as JsonArray)?
            .Select(n => TryGetString(n, out var s) ? s : "").Where(s => s.Length > 0).ToList() ?? new List<string>();
        var legacyNet = Section(legacy, "network");
        var internet = legacyCaps.Any(c => c.Equals("internetClient", StringComparison.OrdinalIgnoreCase))
            && string.Equals(GetString(legacyNet, "defaultPolicy"), "allow", StringComparison.OrdinalIgnoreCase);
        var local = internet && GetBool(legacyNet, "allowLocalNetwork", false);
        var network = (JsonObject)result["network"]!;
        ((JsonObject)network["egress"]!)["default"] = internet ? "allow" : "deny";
        ((JsonObject)network["ingress"]!)["default"] = local ? "allow" : "deny";

        string[] networkCaps = ["internetClient", "internetClientServer", "privateNetworkClientServer", "networkLoopback"];
        var otherCaps = legacyCaps.Where(c => !networkCaps.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        if (otherCaps.Count > 0)
        {
            var caps = new JsonArray();
            foreach (var c in otherCaps) caps.Add((JsonNode?)JsonValue.Create(c));
            result["processContainer"] = new JsonObject { ["capabilities"] = caps };
        }

        return result;
    }

    // ── internals ───────────────────────────────────────────────────────────

    static string Mutate(string json, Action<JsonObject> change)
    {
        if (!TryParse(json, out var o, out _))
            return json; // friendly edits only happen on valid JSON
        change(o);
        return o.ToJsonString(Pretty);
    }

    static JsonObject? Section(JsonObject o, string key) => o[key] as JsonObject;

    static JsonObject? Section(JsonObject o, string key, string child) => Section(o, key)?[child] as JsonObject;

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

    static bool TryGetString(JsonNode? node, out string value)
    {
        value = "";
        if (node is JsonValue v && v.TryGetValue<string>(out var s))
        {
            value = s;
            return true;
        }
        return false;
    }

    static bool GetBool(JsonObject? o, string key, bool fallback) =>
        o?[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    static string? GetString(JsonObject? o, string key) =>
        TryGetString(o?[key], out var s) ? s : null;

    static ushort? GetUShort(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<int>(out var i) && i is >= 0 and <= ushort.MaxValue ? (ushort)i : null;
}
