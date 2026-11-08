using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ReactorRegedit.Services;

/// <summary>
/// advapi32 entry points .NET's RegistryKey doesn't expose: raw typed values,
/// key metadata, and atomic key rename.
/// </summary>
internal static partial class NativeMethods
{
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_FILE_NOT_FOUND = 2;
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_MORE_DATA = 234;
    public const int ERROR_NO_MORE_ITEMS = 259;

    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryValueExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegQueryValueEx(
        SafeRegistryHandle hKey, string? lpValueName, nint lpReserved,
        out uint lpType, byte[]? lpData, ref uint lpcbData);

    [LibraryImport("advapi32.dll", EntryPoint = "RegSetValueExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegSetValueEx(
        SafeRegistryHandle hKey, string? lpValueName, int reserved,
        uint dwType, byte[] lpData, uint cbData);

    [LibraryImport("advapi32.dll", EntryPoint = "RegDeleteValueW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegDeleteValue(SafeRegistryHandle hKey, string? lpValueName);

    [LibraryImport("advapi32.dll", EntryPoint = "RegEnumValueW")]
    public static unsafe partial int RegEnumValue(
        SafeRegistryHandle hKey, uint dwIndex, char* lpValueName, ref uint lpcchValueName,
        nint lpReserved, out uint lpType, byte[]? lpData, ref uint lpcbData);

    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryInfoKeyW")]
    public static partial int RegQueryInfoKey(
        SafeRegistryHandle hKey, nint lpClass, nint lpcchClass, nint lpReserved,
        out uint lpcSubKeys, out uint lpcbMaxSubKeyLen, nint lpcbMaxClassLen,
        out uint lpcValues, out uint lpcbMaxValueNameLen, out uint lpcbMaxValueLen,
        nint lpcbSecurityDescriptor, out long lpftLastWriteTime);

    [LibraryImport("advapi32.dll", EntryPoint = "RegRenameKey", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegRenameKey(SafeRegistryHandle hKey, string? lpSubKeyName, string lpNewKeyName);
}
