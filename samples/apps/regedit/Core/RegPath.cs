namespace ReactorRegedit.Core;

/// <summary>
/// Registry path helpers. Canonical paths look like <c>HKEY_CURRENT_USER\Software\X</c>;
/// the empty string is the "Computer" root. Remote paths (F19) are prefixed with
/// <c>\\MACHINE\</c>.
/// </summary>
public static class RegPath
{
    public const string ComputerLabel = "Computer";

    public static readonly string[] RootNames =
    [
        "HKEY_CLASSES_ROOT",
        "HKEY_CURRENT_USER",
        "HKEY_LOCAL_MACHINE",
        "HKEY_USERS",
        "HKEY_CURRENT_CONFIG",
    ];

    /// <summary>Maps a hive name or abbreviation to its canonical name, else null.</summary>
    public static string? CanonicalRoot(string name) => name.ToUpperInvariant() switch
    {
        "HKEY_CLASSES_ROOT" or "HKCR" => "HKEY_CLASSES_ROOT",
        "HKEY_CURRENT_USER" or "HKCU" => "HKEY_CURRENT_USER",
        "HKEY_LOCAL_MACHINE" or "HKLM" => "HKEY_LOCAL_MACHINE",
        "HKEY_USERS" or "HKU" => "HKEY_USERS",
        "HKEY_CURRENT_CONFIG" or "HKCC" => "HKEY_CURRENT_CONFIG",
        _ => null,
    };

    /// <summary>
    /// Normalizes address-bar input to a canonical path: accepts full hive names or
    /// HKLM/HKCU/HKCR/HKU/HKCC, an optional <c>Computer\</c> prefix, any casing,
    /// surrounding quotes/whitespace and trailing (or doubled) backslashes.
    /// Returns "" for the Computer root and null when the hive is unknown.
    /// Subkey casing is preserved as typed; callers resolve the real casing.
    /// </summary>
    public static string? Normalize(string? input)
    {
        if (input is null) return null;
        var s = input.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s[1..^1].Trim();

        string? machine = null;
        if (s.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var rest = s[2..];
            var sep = rest.IndexOf('\\');
            machine = sep < 0 ? rest : rest[..sep];
            if (machine.Length == 0) return null;
            s = sep < 0 ? "" : rest[(sep + 1)..];
        }

        var segments = s.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        int start = 0;
        if (machine is null && segments.Length > 0 &&
            segments[0].Equals(ComputerLabel, StringComparison.OrdinalIgnoreCase))
        {
            start = 1;
        }

        if (segments.Length == start) return machine is null ? "" : @"\\" + machine;

        var root = CanonicalRoot(segments[start]);
        if (root is null) return null;

        var parts = new List<string>(segments.Length - start) { root };
        for (int i = start + 1; i < segments.Length; i++) parts.Add(segments[i]);
        var path = string.Join('\\', parts);
        return machine is null ? path : $@"\\{machine}\{path}";
    }

    /// <summary>The address-bar form: <c>Computer\HKEY_…\…</c> (remote paths are shown as-is).</summary>
    public static string ToDisplay(string path)
    {
        if (path.Length == 0) return ComputerLabel;
        if (IsRemote(path)) return path;
        return ComputerLabel + "\\" + path;
    }

    public static bool IsRemote(string path) => path.StartsWith(@"\\", StringComparison.Ordinal);

    /// <summary>True for a hive or key path; false for the Computer root ("") and a bare <c>\\MACHINE</c> node.</summary>
    public static bool IsKeyPath(string path) => path.Length > 0 && Hive(path).Length > 0;

    /// <summary>
    /// Normalizes a computer name typed in Connect Network Registry ("\\host", " host ")
    /// to "HOST", or null when it isn't a plausible name.
    /// </summary>
    public static string? NormalizeMachine(string? input)
    {
        var s = (input ?? "").Trim();
        if (s.StartsWith(@"\\", StringComparison.Ordinal)) s = s[2..];
        s = s.TrimEnd('\\');
        if (s.Length == 0 || s.Length > 255 || s.IndexOfAny(['\\', '/', ' ', '\t', '"']) >= 0) return null;
        return s.ToUpperInvariant();
    }

    /// <summary>Splits off a <c>\\MACHINE</c> prefix.</summary>
    public static (string? Machine, string LocalPath) SplitMachine(string path)
    {
        if (!IsRemote(path)) return (null, path);
        var rest = path[2..];
        var sep = rest.IndexOf('\\');
        return sep < 0 ? (rest, "") : (rest[..sep], rest[(sep + 1)..]);
    }

    public static string Parent(string path)
    {
        var idx = path.LastIndexOf('\\');
        if (idx < 0) return "";
        if (IsRemote(path) && idx <= 1) return "";
        return path[..idx];
    }

    public static string Leaf(string path)
    {
        var idx = path.LastIndexOf('\\');
        return idx < 0 ? path : path[(idx + 1)..];
    }

    public static string Combine(string parent, string child) =>
        parent.Length == 0 ? child : parent + "\\" + child;

    /// <summary>A hive root (e.g. HKEY_USERS or \\M\HKEY_USERS) — not deletable/renamable.</summary>
    public static bool IsHive(string path)
    {
        var (_, local) = SplitMachine(path);
        return local.Length > 0 && !local.Contains('\\');
    }

    /// <summary>The canonical hive name of a path, or "" for the Computer/machine root.</summary>
    public static string Hive(string path)
    {
        var (_, local) = SplitMachine(path);
        var idx = local.IndexOf('\\');
        return idx < 0 ? local : local[..idx];
    }

    /// <summary>Subkey path below the hive ("" for the hive itself).</summary>
    public static string SubPath(string path)
    {
        var (_, local) = SplitMachine(path);
        var idx = local.IndexOf('\\');
        return idx < 0 ? "" : local[(idx + 1)..];
    }

    /// <summary>True when <paramref name="path"/> equals or is below <paramref name="ancestor"/>.</summary>
    public static bool IsSameOrDescendant(string path, string ancestor)
    {
        if (ancestor.Length == 0) return true;
        if (path.Equals(ancestor, StringComparison.OrdinalIgnoreCase)) return true;
        return path.Length > ancestor.Length &&
               path[ancestor.Length] == '\\' &&
               path.StartsWith(ancestor, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ancestors from the hive down to (and including) <paramref name="path"/>.</summary>
    public static List<string> AncestorsAndSelf(string path)
    {
        var result = new List<string>();
        var (machine, local) = SplitMachine(path);
        var prefix = machine is null ? "" : $@"\\{machine}";
        if (machine is not null) result.Add(prefix);
        if (local.Length == 0) return result;
        var segments = local.Split('\\');
        var current = prefix;
        foreach (var seg in segments)
        {
            current = current.Length == 0 ? seg : current + "\\" + seg;
            result.Add(current);
        }
        return result;
    }
}
