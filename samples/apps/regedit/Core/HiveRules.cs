namespace ReactorRegedit.Core;

/// <summary>
/// When File > Load Hive / Unload Hive apply, mirroring classic regedit: hives load only
/// directly under HKEY_LOCAL_MACHINE or HKEY_USERS (and only elevated), and only a key that
/// is itself the root of a loaded hive can be unloaded.
/// </summary>
public static class HiveRules
{
    /// <summary>Null when Load Hive is allowed for the selected key, else why it isn't.</summary>
    public static string? LoadBlockedReason(string selectedPath, bool elevated)
    {
        if (RegPath.IsRemote(selectedPath)) return "not available on a remote registry";
        if (!IsHiveParent(selectedPath)) return "select HKEY_LOCAL_MACHINE or HKEY_USERS";
        if (!elevated) return "requires administrator";
        return null;
    }

    /// <summary>Null when Unload Hive is allowed for the selected key, else why it isn't.</summary>
    public static string? UnloadBlockedReason(string selectedPath, bool elevated, IEnumerable<string> loadedHiveRoots, string? currentUserSid = null)
    {
        if (RegPath.IsRemote(selectedPath)) return "not available on a remote registry";
        var parent = RegPath.Parent(selectedPath);
        if (!IsHiveParent(parent) || RegPath.IsHive(selectedPath))
            return "select a loaded hive";
        if (IsSystemHive(selectedPath, currentUserSid)) return "system hives cannot be unloaded";
        if (!loadedHiveRoots.Contains(selectedPath, StringComparer.OrdinalIgnoreCase))
            return "the selected key is not a loaded hive";
        if (!elevated) return "requires administrator";
        return null;
    }

    /// <summary>
    /// Converts the value names of HKLM\SYSTEM\CurrentControlSet\Control\hivelist
    /// ("\REGISTRY\MACHINE\SOFTWARE", "\REGISTRY\USER\S-1-5-…") to key paths.
    /// System hives that can never be unloaded are left out.
    /// </summary>
    public static IReadOnlyList<string> HiveRootsFromHivelist(IEnumerable<string> names, string? currentUserSid = null)
    {
        var result = new List<string>();
        foreach (var raw in names)
        {
            var parts = raw.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || !parts[0].Equals("REGISTRY", StringComparison.OrdinalIgnoreCase)) continue;
            var hive = parts[1].ToUpperInvariant() switch
            {
                "MACHINE" => "HKEY_LOCAL_MACHINE",
                "USER" => "HKEY_USERS",
                _ => null,
            };
            if (hive is null) continue;
            var root = hive + "\\" + parts[2];
            if (IsSystemHive(root, currentUserSid)) continue;
            result.Add(root);
        }
        return result;
    }

    private static readonly HashSet<string> s_systemHives = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM", "SOFTWARE", "SAM", "SECURITY", "HARDWARE", "BCD00000000", "ELAM", "DRIVERS", "Schema",
    };

    private static readonly HashSet<string> s_systemUserHives = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DEFAULT", "S-1-5-18", "S-1-5-19", "S-1-5-20",
    };

    /// <summary>
    /// Hives the OS depends on and that must never be unloaded: the HKLM system hives
    /// (including Schema, which can sit idle yet still be needed by servicing), the default
    /// and service-account profiles, and the current user's own profile and classes hives.
    /// </summary>
    public static bool IsSystemHive(string path, string? currentUserSid)
    {
        var parent = RegPath.Parent(path);
        var leaf = RegPath.Leaf(path);
        if (parent.Equals("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)) return s_systemHives.Contains(leaf);
        if (!parent.Equals("HKEY_USERS", StringComparison.OrdinalIgnoreCase)) return false;
        if (s_systemUserHives.Contains(leaf)) return true;
        return currentUserSid is { Length: > 0 } sid &&
               (leaf.Equals(sid, StringComparison.OrdinalIgnoreCase) || leaf.Equals(sid + "_Classes", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHiveParent(string path) =>
        path.Equals("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("HKEY_USERS", StringComparison.OrdinalIgnoreCase);

    /// <summary>Validates the key name typed in the Load Hive prompt.</summary>
    public static string? ValidateHiveName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Type a key name for the hive.";
        if (name.Contains('\\')) return "The key name cannot contain a backslash.";
        if (name.Length > 255) return "The key name is too long.";
        return null;
    }
}
