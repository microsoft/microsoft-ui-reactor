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
    public static string? UnloadBlockedReason(string selectedPath, bool elevated, IEnumerable<string> loadedHiveRoots)
    {
        if (RegPath.IsRemote(selectedPath)) return "not available on a remote registry";
        var parent = RegPath.Parent(selectedPath);
        if (!IsHiveParent(parent) || RegPath.IsHive(selectedPath))
            return "select a loaded hive";
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
    public static IReadOnlyList<string> HiveRootsFromHivelist(IEnumerable<string> names)
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
            if (hive == "HKEY_LOCAL_MACHINE" && s_systemHives.Contains(parts[2])) continue;
            result.Add(hive + "\\" + parts[2]);
        }
        return result;
    }

    private static readonly HashSet<string> s_systemHives = new(StringComparer.OrdinalIgnoreCase)
    {
        "SYSTEM", "SOFTWARE", "SAM", "SECURITY", "HARDWARE", "BCD00000000", "ELAM", "DRIVERS",
    };

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
