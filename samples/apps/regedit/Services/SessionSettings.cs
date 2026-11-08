using System.Globalization;
using Microsoft.Win32;
using ReactorRegedit.Core;

namespace ReactorRegedit.Services;

/// <summary>
/// Per-user layout that classic regedit keeps in its private "View" blob: tree/value
/// splitter and value-column widths. Stored as a small key=value file under
/// %LOCALAPPDATA%\ReactorRegedit so we never touch the binary classic format.
/// Window placement is persisted by Reactor (WindowSpec.WithPersistence).
/// </summary>
internal sealed class SessionSettings
{
    public double TreeWidth { get; set; } = 300;
    public double NameWidth { get; set; } = 220;
    public double TypeWidth { get; set; } = 190;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReactorRegedit", "layout.txt");

    public static SessionSettings Load()
    {
        var s = new SessionSettings();
        try
        {
            if (!File.Exists(FilePath)) return s;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var eq = line.IndexOf('=');
                if (eq <= 0 || !double.TryParse(line[(eq + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                if (double.IsNaN(v) || v < 40 || v > 4000) continue;
                switch (line[..eq])
                {
                    case nameof(TreeWidth): s.TreeWidth = v; break;
                    case nameof(NameWidth): s.NameWidth = v; break;
                    case nameof(TypeWidth): s.TypeWidth = v; break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllLines(FilePath,
            [
                string.Create(CultureInfo.InvariantCulture, $"{nameof(TreeWidth)}={TreeWidth:0.##}"),
                string.Create(CultureInfo.InvariantCulture, $"{nameof(NameWidth)}={NameWidth:0.##}"),
                string.Create(CultureInfo.InvariantCulture, $"{nameof(TypeWidth)}={TypeWidth:0.##}"),
            ]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // ── LastKey: shared with classic regedit ─────────────────────────

    private const string AppletKey = @"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit";

    /// <summary>The key classic regedit (or we) had selected on exit, normalized; null if none.</summary>
    public static string? ReadLastKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppletKey);
            return key?.GetValue("LastKey") is string s ? RegPath.Normalize(s) : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>Writes LastKey as "Computer\HKEY_…" like classic regedit does on exit.</summary>
    public static void WriteLastKey(string path)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AppletKey, writable: true);
            key.SetValue("LastKey", RegPath.ToDisplay(RegPath.IsRemote(path) ? "" : path), RegistryValueKind.String);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
    }
}
