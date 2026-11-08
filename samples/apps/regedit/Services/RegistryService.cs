using System.ComponentModel;
using System.Security;
using Microsoft.Win32;
using ReactorRegedit.Core;
using static ReactorRegedit.Services.NativeMethods;

namespace ReactorRegedit.Services;

/// <summary>A subkey as shown in the tree.</summary>
internal sealed record KeyEntry(string Name, string Path, bool HasChildren);

/// <summary>Key metadata for the status bar (F17).</summary>
internal sealed record KeyInfo(int SubKeyCount, int ValueCount, DateTime LastWriteTime);

/// <summary>A registry operation failed; <see cref="Exception.Message"/> is user-facing.</summary>
internal sealed class RegistryOperationException(string message, int win32Error = 0) : Exception(message)
{
    public int Win32Error { get; } = win32Error;
    public bool IsAccessDenied => Win32Error == ERROR_ACCESS_DENIED;
    public bool IsNotFound => Win32Error == ERROR_FILE_NOT_FOUND;
}

/// <summary>
/// All registry access. Methods are synchronous and may block — call them off the UI
/// thread. Failures throw <see cref="RegistryOperationException"/> with a message fit
/// for an InfoBar.
/// </summary>
internal static class RegistryService
{
    private static readonly Dictionary<string, RegistryKey> s_remoteRoots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock s_remoteGate = new();

    // ── Opening ──────────────────────────────────────────────────────

    private static RegistryKey LocalRoot(string hive) => hive switch
    {
        "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
        "HKEY_CURRENT_USER" => Registry.CurrentUser,
        "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
        "HKEY_USERS" => Registry.Users,
        "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
        _ => throw new RegistryOperationException($"Unknown registry hive \"{hive}\".", ERROR_FILE_NOT_FOUND),
    };

    private static RegistryKey RootFor(string path)
    {
        var (machine, _) = RegPath.SplitMachine(path);
        var hive = RegPath.Hive(path);
        if (machine is null) return LocalRoot(hive);
        lock (s_remoteGate)
        {
            if (s_remoteRoots.TryGetValue($"{machine}\\{hive}", out var key)) return key;
        }
        throw new RegistryOperationException($"Not connected to the registry on \\\\{machine} ({hive}).", ERROR_FILE_NOT_FOUND);
    }

    /// <summary>Registers a remote hive root opened with RegConnectRegistry (F19).</summary>
    internal static void AddRemoteRoot(string machine, string hive, RegistryKey key)
    {
        lock (s_remoteGate) s_remoteRoots[$"{machine}\\{hive}"] = key;
    }

    internal static void RemoveRemoteMachine(string machine)
    {
        lock (s_remoteGate)
        {
            foreach (var k in s_remoteRoots.Keys.Where(k => k.StartsWith(machine + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                s_remoteRoots[k].Dispose();
                s_remoteRoots.Remove(k);
            }
        }
    }

    /// <summary>Opens a key (always a fresh handle the caller must dispose).</summary>
    public static RegistryKey OpenKey(string path, bool writable = false)
    {
        if (path.Length == 0 || (RegPath.IsRemote(path) && RegPath.Hive(path).Length == 0))
            throw new RegistryOperationException("Select a registry key first.");
        var root = RootFor(path);
        var sub = RegPath.SubPath(path);
        try
        {
            var key = root.OpenSubKey(sub, writable);
            return key ?? throw NotFound(path);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            throw new RegistryOperationException(
                $"Cannot open {RegPath.Leaf(path)}: Access is denied.", ERROR_ACCESS_DENIED);
        }
        catch (IOException ex)
        {
            throw new RegistryOperationException($"Cannot open {RegPath.Leaf(path)}: {ex.Message}", ex.HResult & 0xFFFF);
        }
    }

    public static bool KeyExists(string path)
    {
        try { using var _ = OpenKey(path); return true; }
        catch (RegistryOperationException ex) { return ex.IsAccessDenied; }
    }

    /// <summary>Which writes the current user may do on a key (for disabling commands up front).</summary>
    public static (bool SetValue, bool CreateSubKey) ProbeWriteAccess(string path)
    {
        static bool Can(string p, int access)
        {
            try { using var h = OpenKeyWithRights(p, access); return true; }
            catch (RegistryOperationException) { return false; }
        }
        if (path.Length == 0) return (false, false);
        return (Can(path, 0x0002 /* KEY_SET_VALUE */), Can(path, 0x0004 /* KEY_CREATE_SUB_KEY */));
    }

    /// <summary>Opens a key with an explicit access mask (e.g. READ_CONTROL / WRITE_DAC).</summary>
    public static Microsoft.Win32.SafeHandles.SafeRegistryHandle OpenKeyWithRights(string path, int access)
    {
        var root = RootFor(path);
        var sub = RegPath.SubPath(path);
        var rc = RegOpenKeyEx(root.Handle, sub.Length == 0 ? null : sub, 0, access, out var handle);
        if (rc == ERROR_SUCCESS) return handle;
        handle.Dispose();
        if (rc == ERROR_FILE_NOT_FOUND) throw NotFound(path);
        if (rc == ERROR_ACCESS_DENIED)
            throw new RegistryOperationException($"Cannot open {RegPath.Leaf(path)}: Access is denied.", ERROR_ACCESS_DENIED);
        throw Win32(rc, $"Cannot open {RegPath.Leaf(path)}");
    }

    /// <summary>
    /// Reads the key's self-relative security descriptor through GetNamedSecurityInfo, which
    /// (unlike RegGetKeySecurity) reports which ACEs are inherited and sets
    /// SE_DACL_AUTO_INHERITED, so the security editor greys inherited entries and hands back
    /// only the explicit ones.
    /// </summary>
    public static byte[] GetKeySecurity(string path, uint securityInformation)
    {
        var rc = GetNamedSecurityInfo(SecurityObjectName(path), 4 /* SE_REGISTRY_KEY */, securityInformation,
            out _, out _, out _, out _, out var sd);
        if (rc == ERROR_SUCCESS && sd != 0)
        {
            try
            {
                var bytes = new byte[GetSecurityDescriptorLength(sd)];
                System.Runtime.InteropServices.Marshal.Copy(sd, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { LocalFree(sd); }
        }
        throw rc == ERROR_ACCESS_DENIED
            ? new RegistryOperationException($"Cannot read the permissions of {RegPath.Leaf(path)}: Access is denied.", ERROR_ACCESS_DENIED)
            : rc == ERROR_FILE_NOT_FOUND ? NotFound(path)
            : Win32(rc, $"Cannot read the permissions of {RegPath.Leaf(path)}");
    }

    /// <summary>
    /// Writes the parts of <paramref name="descriptor"/> selected by <paramref name="securityInformation"/>.
    /// The security editor passes only the explicit ACEs plus PROTECTED/UNPROTECTED flags, so this
    /// goes through SetNamedSecurityInfo, which re-merges the ACEs inherited from the parent key
    /// (RegSetKeySecurity would write the descriptor verbatim and drop them).
    /// </summary>
    public static void SetKeySecurity(string path, uint securityInformation, nint descriptor)
    {
        nint owner = 0, group = 0, dacl = 0, sacl = 0;
        if ((securityInformation & 0x1) != 0) GetSecurityDescriptorOwner(descriptor, out owner, out _);
        if ((securityInformation & 0x2) != 0) GetSecurityDescriptorGroup(descriptor, out group, out _);
        if ((securityInformation & 0x4) != 0) GetSecurityDescriptorDacl(descriptor, out _, out dacl, out _);
        if ((securityInformation & 0x8) != 0) GetSecurityDescriptorSacl(descriptor, out _, out sacl, out _);
        var rc = SetNamedSecurityInfo(SecurityObjectName(path), 4 /* SE_REGISTRY_KEY */, securityInformation, owner, group, dacl, sacl);
        if (rc == ERROR_SUCCESS) return;
        throw rc == ERROR_ACCESS_DENIED
            ? new RegistryOperationException($"Cannot change the permissions of {RegPath.Leaf(path)}: Access is denied.", ERROR_ACCESS_DENIED)
            : Win32(rc, $"Cannot change the permissions of {RegPath.Leaf(path)}");
    }

    /// <summary>"CURRENT_USER\Software\…" — the registry object-name form SetNamedSecurityInfo expects.</summary>
    internal static string SecurityObjectName(string path)
    {
        var (machine, _) = RegPath.SplitMachine(path);
        var root = RegPath.Hive(path) switch
        {
            "HKEY_CLASSES_ROOT" => "CLASSES_ROOT",
            "HKEY_CURRENT_USER" => "CURRENT_USER",
            "HKEY_LOCAL_MACHINE" => "MACHINE",
            "HKEY_USERS" => "USERS",
            "HKEY_CURRENT_CONFIG" => "CURRENT_CONFIG",
            var other => other,
        };
        var sub = RegPath.SubPath(path);
        var local = sub.Length == 0 ? root : root + "\\" + sub;
        return machine is null ? local : $@"\\{machine}\{local}";
    }

    private static RegistryOperationException NotFound(string path) =>
        new($"Cannot find the key {RegPath.ToDisplay(path)}.", ERROR_FILE_NOT_FOUND);

    private static RegistryOperationException Win32(int code, string what) =>
        new($"{what}: {new Win32Exception(code).Message}", code);

    /// <summary>
    /// Resolves the real casing of every segment of <paramref name="path"/> (the address
    /// bar accepts any casing). Throws when a segment doesn't exist.
    /// </summary>
    public static string ResolveCase(string path)
    {
        var (machine, local) = RegPath.SplitMachine(path);
        if (local.Length == 0) return path;
        var segments = local.Split('\\');
        var current = machine is null ? segments[0] : $@"\\{machine}\{segments[0]}";
        RootFor(current);
        for (int i = 1; i < segments.Length; i++)
        {
            string[] names;
            try
            {
                using var key = OpenKey(current);
                names = key.GetSubKeyNames();
            }
            catch (RegistryOperationException ex) when (ex.IsAccessDenied)
            {
                // Can't enumerate: keep the typed casing for the rest and let the open decide.
                var rest = current + "\\" + string.Join('\\', segments[i..]);
                using var _ = OpenKey(rest);
                return rest;
            }
            var match = names.FirstOrDefault(n => n.Equals(segments[i], StringComparison.OrdinalIgnoreCase));
            if (match is null) throw NotFound(current + "\\" + segments[i]);
            current += "\\" + match;
        }
        return current;
    }

    // ── Reading ──────────────────────────────────────────────────────

    /// <summary>Subkeys sorted case-insensitively, each with a cheap "has children" probe.</summary>
    public static KeyEntry[] GetSubKeys(string path, CancellationToken ct = default)
    {
        using var key = OpenKey(path);
        string[] names;
        try { names = key.GetSubKeyNames(); }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            throw new RegistryOperationException($"Cannot enumerate {RegPath.Leaf(path)}: Access is denied.", ERROR_ACCESS_DENIED);
        }
        catch (IOException)
        {
            throw new RegistryOperationException($"The key {RegPath.Leaf(path)} no longer exists.", ERROR_FILE_NOT_FOUND);
        }
        Array.Sort(names, StringComparer.OrdinalIgnoreCase);
        var result = new KeyEntry[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            result[i] = new KeyEntry(names[i], path + "\\" + names[i], ProbeHasChildren(key, names[i]));
        }
        return result;
    }

    private static bool ProbeHasChildren(RegistryKey parent, string name)
    {
        try
        {
            using var child = parent.OpenSubKey(name, RegistryKeyPermissionCheck.ReadSubTree,
                System.Security.AccessControl.RegistryRights.QueryValues);
            if (child is null) return false;
            var rc = RegQueryInfoKey(child.Handle, 0, 0, 0, out var subKeys, out _, 0, out _, out _, out _, 0, out _);
            return rc != ERROR_SUCCESS || subKeys > 0;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            // Can't tell without access — show the chevron; expanding reports the error.
            return true;
        }
    }

    public static bool HasSubKeys(string path)
    {
        try
        {
            using var key = OpenKey(path);
            return key.SubKeyCount > 0;
        }
        catch (RegistryOperationException ex) when (ex.IsAccessDenied) { return true; }
        catch (RegistryOperationException) { return false; }
    }

    public static KeyInfo GetKeyInfo(string path)
    {
        using var key = OpenKey(path);
        var rc = RegQueryInfoKey(key.Handle, 0, 0, 0, out var subKeys, out _, 0, out var values, out _, out _, 0, out var ft);
        if (rc != ERROR_SUCCESS) throw Win32(rc, "Cannot query key information");
        return new KeyInfo((int)subKeys, (int)values, DateTime.FromFileTime(ft));
    }

    /// <summary>Local last-write time of an open key, or null if it can't be queried.</summary>
    internal static DateTime? LastWriteTime(RegistryKey key) =>
        RegQueryInfoKey(key.Handle, 0, 0, 0, out _, out _, 0, out _, out _, out _, 0, out var ft) == ERROR_SUCCESS
            ? DateTime.FromFileTime(ft)
            : null;

    /// <summary>
    /// All values of a key with raw types and bytes, in registry order. A synthetic
    /// "(value not set)" (Default) row is added when the key has no default value.
    /// </summary>
    public static RegValue[] GetValues(string path)
    {
        using var key = OpenKey(path);
        var result = ReadAllValues(key, path);
        if (!result.Any(v => v.IsDefault)) result.Insert(0, RegValue.UnsetDefault());
        return result.ToArray();
    }

    /// <summary>Raw values in registry order (no synthetic default row).</summary>
    internal static List<RegValue> ReadAllValues(RegistryKey key, string pathForErrors)
    {
        var handle = key.Handle;
        var rc = RegQueryInfoKey(handle, 0, 0, 0, out _, out _, 0, out var count, out var maxName, out var maxData, 0, out _);
        if (rc != ERROR_SUCCESS) throw Win32(rc, $"Cannot read values of {RegPath.Leaf(pathForErrors)}");

        var result = new List<RegValue>((int)count + 1);
        var nameBuf = new char[Math.Max(maxName, 256) + 1];
        var dataBuf = new byte[Math.Max(maxData, 256)];
        for (uint i = 0; ; i++)
        {
            uint cchName = (uint)nameBuf.Length;
            uint cbData = (uint)dataBuf.Length;
            uint type;
            unsafe
            {
                fixed (char* pName = nameBuf)
                    rc = RegEnumValue(handle, i, pName, ref cchName, 0, out type, dataBuf, ref cbData);
            }
            if (rc == ERROR_MORE_DATA)
            {
                // Grew since RegQueryInfoKey; retry this index with bigger buffers.
                nameBuf = new char[Math.Max(nameBuf.Length * 2, 16384)];
                dataBuf = new byte[Math.Max(dataBuf.Length * 2, (int)cbData)];
                i--;
                continue;
            }
            if (rc == ERROR_NO_MORE_ITEMS) break;
            if (rc != ERROR_SUCCESS) throw Win32(rc, $"Cannot read values of {RegPath.Leaf(pathForErrors)}");
            var name = new string(nameBuf, 0, (int)cchName);
            result.Add(new RegValue(name, type, dataBuf.AsSpan(0, (int)cbData).ToArray()));
        }
        return result;
    }

    /// <summary>Reads one value; null when it doesn't exist.</summary>
    public static RegValue? GetValue(string path, string name)
    {
        using var key = OpenKey(path);
        return ReadValue(key, name);
    }

    internal static RegValue? ReadValue(RegistryKey key, string name)
    {
        uint cb = 0;
        var rc = RegQueryValueEx(key.Handle, name, 0, out var type, null, ref cb);
        if (rc == ERROR_FILE_NOT_FOUND) return null;
        if (rc != ERROR_SUCCESS && rc != ERROR_MORE_DATA) throw Win32(rc, $"Cannot read value {DisplayName(name)}");
        while (true)
        {
            var data = new byte[cb];
            rc = RegQueryValueEx(key.Handle, name, 0, out type, data, ref cb);
            if (rc == ERROR_MORE_DATA) continue;
            if (rc == ERROR_FILE_NOT_FOUND) return null;
            if (rc != ERROR_SUCCESS) throw Win32(rc, $"Cannot read value {DisplayName(name)}");
            return new RegValue(name, type, cb == data.Length ? data : data[..(int)cb]);
        }
    }

    private static string DisplayName(string name) => name.Length == 0 ? "(Default)" : name;

    // ── Writing values ───────────────────────────────────────────────

    public static void SetValue(string path, string name, uint type, byte[] data)
    {
        using var key = OpenKey(path, writable: true);
        WriteValue(key, name, type, data);
    }

    internal static void WriteValue(RegistryKey key, string name, uint type, byte[] data)
    {
        var rc = RegSetValueEx(key.Handle, name, 0, type, data, (uint)data.Length);
        if (rc != ERROR_SUCCESS) throw Win32(rc, $"Cannot edit {DisplayName(name)}: Error writing the value's new contents");
    }

    /// <summary>Overwrites an existing value; fails if it was deleted meanwhile (F06).</summary>
    public static void ModifyValue(string path, string name, uint type, byte[] data)
    {
        using var key = OpenKey(path, writable: true);
        if (name.Length > 0 && ReadValue(key, name) is null)
            throw new RegistryOperationException($"Cannot edit {name}: The value no longer exists.", ERROR_FILE_NOT_FOUND);
        WriteValue(key, name, type, data);
    }

    public static void DeleteValue(string path, string name)
    {
        using var key = OpenKey(path, writable: true);
        var rc = RegDeleteValue(key.Handle, name);
        if (rc == ERROR_FILE_NOT_FOUND && name.Length > 0)
            throw new RegistryOperationException($"Unable to delete {name}: The value no longer exists.", rc);
        if (rc != ERROR_SUCCESS && rc != ERROR_FILE_NOT_FOUND)
            throw Win32(rc, $"Unable to delete {DisplayName(name)}");
    }

    public static void RenameValue(string path, string oldName, string newName)
    {
        if (newName.Length == 0)
            throw new RegistryOperationException($"Cannot rename {oldName}: Specify a value name.");
        if (string.Equals(oldName, newName, StringComparison.Ordinal)) return;
        using var key = OpenKey(path, writable: true);
        var caseOnly = oldName.Equals(newName, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && ReadValue(key, newName) is not null)
            throw new RegistryOperationException(
                $"Cannot rename {oldName}: The specified value name already exists. Type another name and try again.");
        var value = ReadValue(key, oldName)
            ?? throw new RegistryOperationException($"Cannot rename {oldName}: The value no longer exists.", ERROR_FILE_NOT_FOUND);
        if (caseOnly)
        {
            // Value names are case-insensitive: delete first, then write the new casing.
            var rcDel = RegDeleteValue(key.Handle, oldName);
            if (rcDel != ERROR_SUCCESS) throw Win32(rcDel, $"Cannot rename {oldName}");
            WriteValue(key, newName, value.Type, value.Data);
            return;
        }
        WriteValue(key, newName, value.Type, value.Data);
        var rc = RegDeleteValue(key.Handle, oldName);
        if (rc != ERROR_SUCCESS) throw Win32(rc, $"Cannot rename {oldName}");
    }

    public static string UniqueValueName(string path)
    {
        using var key = OpenKey(path);
        var names = new HashSet<string>(key.GetValueNames(), StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
        {
            var n = $"New Value #{i}";
            if (!names.Contains(n)) return n;
        }
    }

    /// <summary>Creates a value with regedit's defaults for a new value of <paramref name="type"/>.</summary>
    public static void CreateValue(string path, string name, uint type)
    {
        byte[] data = type switch
        {
            RegType.Sz or RegType.ExpandSz => ValueFormat.EncodeString(""),
            RegType.Dword => new byte[4],
            RegType.Qword => new byte[8],
            _ => [],
        };
        using var key = OpenKey(path, writable: true);
        if (ReadValue(key, name) is not null)
            throw new RegistryOperationException($"Cannot create {name}: The value already exists.");
        WriteValue(key, name, type, data);
    }

    // ── Writing keys ─────────────────────────────────────────────────

    public static string UniqueKeyName(string parentPath)
    {
        using var key = OpenKey(parentPath);
        var names = new HashSet<string>(key.GetSubKeyNames(), StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
        {
            var n = $"New Key #{i}";
            if (!names.Contains(n)) return n;
        }
    }

    /// <summary>Creates a new subkey; fails if it already exists.</summary>
    public static string CreateKey(string parentPath, string name)
    {
        if (name.Length == 0 || name.Contains('\\'))
            throw new RegistryOperationException("A key name cannot be empty or contain a backslash.");
        using var parent = OpenKey(parentPath, writable: true);
        if (SubKeyExists(parent, name))
            throw new RegistryOperationException($"Cannot create key {name}: The key already exists.");
        try
        {
            using var created = parent.CreateSubKey(name);
            return RegPath.Combine(parentPath, name);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            throw new RegistryOperationException($"Cannot create key: Error while creating key {name}. Access is denied.", ERROR_ACCESS_DENIED);
        }
        catch (IOException ex)
        {
            throw new RegistryOperationException($"Cannot create key {name}: {ex.Message}");
        }
    }

    /// <summary>Opens or creates a key path, creating every missing segment (used by import).</summary>
    public static void EnsureKey(string path)
    {
        var root = RootFor(path);
        var sub = RegPath.SubPath(path);
        if (sub.Length == 0) return;
        try
        {
            // An existing key only needs to be readable here; value writes check their own access.
            using (var existing = root.OpenSubKey(sub, writable: false)) { if (existing is not null) return; }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException) { }
        try
        {
            using var _ = root.CreateSubKey(sub, writable: false)
                ?? throw new RegistryOperationException($"Cannot create key {path}.");
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            throw new RegistryOperationException($"Cannot create key {path}: Access is denied.", ERROR_ACCESS_DENIED);
        }
        catch (IOException ex)
        {
            throw new RegistryOperationException($"Cannot create key {path}: {ex.Message}");
        }
    }

    /// <summary>True when the subkey exists (an access-denied subkey counts as existing).</summary>
    private static bool SubKeyExists(RegistryKey parent, string name)
    {
        try
        {
            using var k = parent.OpenSubKey(name);
            return k is not null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException) { return true; }
    }

    public static void DeleteKey(string path, bool throwIfMissing = true)
    {
        if (RegPath.IsHive(path) || path.Length == 0)
            throw new RegistryOperationException("Cannot delete a root key.");
        var parentPath = RegPath.Parent(path);
        var name = RegPath.Leaf(path);
        using var parent = OpenKey(parentPath, writable: true);
        try
        {
            if (!SubKeyExists(parent, name))
            {
                if (throwIfMissing) throw NotFound(path);
                return;
            }
            parent.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            throw new RegistryOperationException($"Cannot delete {name}: Error while deleting key. Access is denied.", ERROR_ACCESS_DENIED);
        }
        catch (IOException ex)
        {
            throw new RegistryOperationException($"Cannot delete {name}: {ex.Message}");
        }
    }

    public static string RenameKey(string path, string newName)
    {
        var oldName = RegPath.Leaf(path);
        if (RegPath.IsHive(path)) throw new RegistryOperationException("Cannot rename a root key.");
        if (newName.Length == 0 || newName.Contains('\\'))
            throw new RegistryOperationException($"Cannot rename {oldName}: A key name cannot be empty or contain a backslash.");
        if (newName == oldName) return path;
        var parentPath = RegPath.Parent(path);
        using var parent = OpenKey(parentPath, writable: true);
        if (!newName.Equals(oldName, StringComparison.OrdinalIgnoreCase) && SubKeyExists(parent, newName))
            throw new RegistryOperationException(
                $"Cannot rename {oldName}: The specified key name already exists. Type another name and try again.");
        var rc = RegRenameKey(parent.Handle, oldName, newName);
        if (rc == ERROR_FILE_NOT_FOUND) throw NotFound(path);
        if (rc != ERROR_SUCCESS) throw Win32(rc, $"Cannot rename {oldName}: Error while renaming key");
        return RegPath.Combine(parentPath, newName);
    }
}
