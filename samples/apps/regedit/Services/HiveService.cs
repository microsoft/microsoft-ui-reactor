using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using ReactorRegedit.Core;

namespace ReactorRegedit.Services;

/// <summary>File > Load Hive / Unload Hive (RegLoadKey / RegUnLoadKey), elevated only.</summary>
internal static partial class HiveService
{
    public static bool IsElevated => Environment.IsPrivilegedProcess;

    /// <summary>Keys under HKLM/HKU that are the roots of loaded (unloadable) hives.</summary>
    public static IReadOnlyList<string> LoadedHiveRoots()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\hivelist");
            return key is null ? [] : HiveRules.HiveRootsFromHivelist(key.GetValueNames());
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    /// <summary>Loads <paramref name="file"/> as <paramref name="parent"/>\<paramref name="name"/>.</summary>
    public static string Load(string parent, string name, string file)
    {
        if (HiveRules.LoadBlockedReason(parent, IsElevated) is { } reason)
            throw new RegistryOperationException($"Cannot load the hive: {reason}.");
        if (HiveRules.ValidateHiveName(name) is { } invalid) throw new RegistryOperationException(invalid);
        EnablePrivileges();
        var root = parent == "HKEY_USERS" ? HKEY_USERS : HKEY_LOCAL_MACHINE;
        var rc = RegLoadKey(root, name, file);
        if (rc != 0)
            throw new RegistryOperationException($"Cannot load {file}: {new Win32Exception(rc).Message}", rc);
        return parent + "\\" + name;
    }

    /// <summary>Unloads the hive rooted at <paramref name="path"/>.</summary>
    public static void Unload(string path)
    {
        if (HiveRules.UnloadBlockedReason(path, IsElevated, LoadedHiveRoots()) is { } reason)
            throw new RegistryOperationException($"Cannot unload {RegPath.Leaf(path)}: {reason}.");
        EnablePrivileges();
        var root = RegPath.Parent(path) == "HKEY_USERS" ? HKEY_USERS : HKEY_LOCAL_MACHINE;
        var rc = RegUnLoadKey(root, RegPath.Leaf(path));
        if (rc != 0)
            throw new RegistryOperationException($"Cannot unload {RegPath.Leaf(path)}: {new Win32Exception(rc).Message}", rc);
    }

    // RegLoadKey/RegUnLoadKey need SeRestorePrivilege and SeBackupPrivilege enabled.
    private static void EnablePrivileges()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
            throw new RegistryOperationException("Cannot adjust process privileges.", Marshal.GetLastPInvokeError());
        try
        {
            foreach (var name in new[] { "SeRestorePrivilege", "SeBackupPrivilege" })
            {
                if (!LookupPrivilegeValue(null, name, out var luid)) continue;
                var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                AdjustTokenPrivileges(token, false, ref tp, 0, 0, 0);
            }
        }
        finally { CloseHandle(token); }
    }

    private static readonly nint HKEY_LOCAL_MACHINE = unchecked((nint)(int)0x80000002);
    private static readonly nint HKEY_USERS = unchecked((nint)(int)0x80000003);
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
    private const uint TOKEN_QUERY = 0x8;
    private const uint SE_PRIVILEGE_ENABLED = 0x2;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public long Luid;
        public uint Attributes;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "RegLoadKeyW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegLoadKey(nint hKey, string subKey, string file);

    [LibraryImport("advapi32.dll", EntryPoint = "RegUnLoadKeyW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegUnLoadKey(nint hKey, string subKey);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", EntryPoint = "LookupPrivilegeValueW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? system, string name, out long luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(nint token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        ref TOKEN_PRIVILEGES newState, uint bufferLength, nint previousState, nint returnLength);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
