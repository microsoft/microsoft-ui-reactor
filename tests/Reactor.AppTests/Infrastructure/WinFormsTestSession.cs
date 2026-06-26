using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Manages the WinForms interop test host process + winapp UI-automation context.
/// Same pattern as <see cref="TestSession"/> but launches the WinForms test host
/// (Reactor.WinFormsTests.Host) instead of the WinUI host. Drives it through
/// <see cref="WinAppUi"/> (winapp ui) — no Appium/WinAppDriver.
/// </summary>
public class WinFormsTestSession
{
    private const string WindowTitle = "WinForms Interop Test Host";
    private const string ProcessName = "Reactor.WinFormsTests.Host";

    private static Process? _appProcess;
    private static WinAppUi? _app;
    private static UiaPropertyReader? _uia;
    private static int _refCount;

    public static WinAppUi App =>
        _app ?? throw new InvalidOperationException(
            "WinForms test session has not been initialized. Ensure [ClassInitialize] has run.");

    public static IUiaPropertyReader Uia =>
        _uia ?? throw new InvalidOperationException(
            "WinForms test session has not been initialized. Ensure [ClassInitialize] has run.");

    public static long HostHwnd => App.HostHwnd;

    public static int HostPid => _appProcess?.Id ?? 0;

    public static void Init(object? context = null)
    {
        _refCount++;

        if (_app != null)
        {
            Console.WriteLine($"WinForms session already active (ref {_refCount}), reusing.");
            return;
        }

        SessionInteractivityGuard.EnsureInteractive("WinFormsTestSession.Init");

        KillOrphanedProcesses();

        var exePath = FindHostExe();
        Console.WriteLine($"WinForms host: {exePath}");

        _appProcess = Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = false });
        Console.WriteLine($"WinForms host launched (PID {_appProcess?.Id}).");

        try
        {
            var pid = _appProcess!.Id;
            var hwnd = WinAppUi.FindWindowHwnd(pid, WindowTitle, timeoutMs: 15000);
            _app = new WinAppUi(pid, hwnd);
            _uia = new UiaPropertyReader(hwnd);
            Console.WriteLine($"winapp UI automation bound to WinForms host (HWND 0x{hwnd:X}).");
        }
        catch (Exception ex) when (ex is WinAppException or TimeoutException)
        {
            SessionInteractivityGuard.RecheckAfterFailure("WinFormsTestSession bootstrap");
            throw;
        }
    }

    public static void Cleanup()
    {
        _refCount--;

        if (_refCount > 0)
        {
            Console.WriteLine($"WinForms session still in use (ref {_refCount}), skipping cleanup.");
            return;
        }

        ForceCleanup();
    }

    public static void ForceCleanup()
    {
        _refCount = 0;
        _app = null;
        _uia = null;

        if (_appProcess != null)
        {
            try
            {
                if (!_appProcess.HasExited)
                {
                    _appProcess.Kill();
                    _appProcess.WaitForExit(5000);
                }
            }
            catch { }
            finally
            {
                _appProcess.Dispose();
                _appProcess = null;
            }
        }
    }

    private static void KillOrphanedProcesses()
    {
        foreach (var proc in Process.GetProcessesByName(ProcessName))
        {
            try
            {
                Console.WriteLine($"Killing orphaned WinForms host (PID {proc.Id}).");
                proc.Kill();
                proc.WaitForExit(3000);
            }
            catch { }
            finally { proc.Dispose(); }
        }
    }

    private static string FindHostExe()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Reactor.slnx")))
            dir = Path.GetDirectoryName(dir);

        if (dir == null)
            throw new DirectoryNotFoundException("Could not find repo root (Reactor.slnx)");

        var platform = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "ARM64",
            _ => "x64"
        };

        var exe = Path.Combine(dir, "tests", "Reactor.WinFormsTests.Host", "bin", platform,
            "Debug", "net10.0-windows10.0.22621.0", ProcessName + ".exe");

        if (!File.Exists(exe))
            throw new FileNotFoundException($"Build the WinForms host first. Expected: {exe}");

        return exe;
    }
}
