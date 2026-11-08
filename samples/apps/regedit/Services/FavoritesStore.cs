using System.Runtime.InteropServices;
using Microsoft.Win32;
using ReactorRegedit.Core;

namespace ReactorRegedit.Services;

/// <summary>A registry favorite: menu name and normalized key path (no "Computer\" prefix).</summary>
internal sealed record Favorite(string Name, string Path);

/// <summary>
/// Favorites live where classic regedit keeps them
/// (HKCU\Software\Microsoft\Windows\CurrentVersion\Applets\Regedit\Favorites, one REG_SZ per
/// favorite whose data is "Computer\HKEY_…\…"), so both editors share the same list.
/// </summary>
internal static partial class FavoritesStore
{
    public const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit\Favorites";

    /// <summary>Reads favorites in registry enumeration order; unreadable entries are skipped.</summary>
    public static Favorite[] Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (key is null) return [];
            var result = new List<Favorite>();
            foreach (var name in key.GetValueNames())
            {
                if (name.Length == 0) continue;
                if (key.GetValueKind(name) is not (RegistryValueKind.String or RegistryValueKind.ExpandString)) continue;
                if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string data) continue;
                var path = RegPath.Normalize(data);
                if (path is null) continue;
                result.Add(new Favorite(name, path));
            }
            return [.. result];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    /// <summary>Adds (or replaces) a favorite.</summary>
    public static void Add(string name, string path)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
            key.SetValue(name, RegPath.ToDisplay(path), RegistryValueKind.String);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            throw new RegistryOperationException($"Cannot save the favorite {name}: {ex.Message}");
        }
    }

    /// <summary>Removes a favorite; a missing one is not an error.</summary>
    public static void Remove(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            key?.DeleteValue(name, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            throw new RegistryOperationException($"Cannot remove the favorite {name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Calls <paramref name="changed"/> (on a background thread) whenever the Favorites key's
    /// values change, e.g. when classic regedit adds one. Dispose to stop watching.
    /// </summary>
    public static IDisposable Watch(Action changed)
    {
        var stop = new ManualResetEvent(false);
        var thread = new Thread(() => WatchLoop(changed, stop)) { IsBackground = true, Name = "Favorites watcher" };
        thread.Start();
        return new Stopper(stop);
    }

    private static void WatchLoop(Action changed, ManualResetEvent stop)
    {
        using var notify = new AutoResetEvent(false);
        while (!stop.WaitOne(0))
        {
            RegistryKey? key = null;
            try
            {
                key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: false);
                var rc = RegNotifyChangeKeyValue(key.Handle, false, RegNotifyLastSet | RegNotifyName,
                    notify.SafeWaitHandle, true);
                if (rc != 0)
                {
                    // Can't watch (e.g. access denied): poll slowly instead.
                    if (WaitHandle.WaitAny([stop], TimeSpan.FromSeconds(5)) == 0) return;
                    changed();
                    continue;
                }
                if (WaitHandle.WaitAny([stop, notify]) == 0) return;
                changed();
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                if (WaitHandle.WaitAny([stop], TimeSpan.FromSeconds(5)) == 0) return;
            }
            finally { key?.Dispose(); }
        }
    }

    private const int RegNotifyName = 0x1;
    private const int RegNotifyLastSet = 0x4;

    [LibraryImport("advapi32.dll")]
    private static partial int RegNotifyChangeKeyValue(Microsoft.Win32.SafeHandles.SafeRegistryHandle hKey,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree, int notifyFilter,
        Microsoft.Win32.SafeHandles.SafeWaitHandle hEvent, [MarshalAs(UnmanagedType.Bool)] bool asynchronous);

    private sealed class Stopper(ManualResetEvent stop) : IDisposable
    {
        public void Dispose() => stop.Set();
    }
}
