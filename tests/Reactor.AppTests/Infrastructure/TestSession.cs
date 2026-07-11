using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Manages the Host app process + winapp UI-automation context for the test assembly.
/// Launches <c>Reactor.AppTests.Host.exe</c> as a regular process, captures its PID and
/// primary window HWND, and exposes a <see cref="WinAppUi"/> driver bound to that window.
///
/// Replaces the former Appium/WinAppDriver two-step bootstrap — there is no persistent
/// automation session; <see cref="WinAppUi"/> drives the app via per-call <c>winapp ui</c>
/// invocations.
///
/// The context is shared across all test classes — the first ClassInitialize starts it,
/// the last ClassCleanup tears it down.
/// </summary>
public class TestSession
{
    private const string WindowTitle = "Reactor Test Host";

    private static Process? _appProcess;
    private static WinAppUi? _app;
    private static UiaPropertyReader? _uia;
    private static int _refCount;

    /// <summary>The winapp-backed UI automation driver bound to the Host window.</summary>
    public static WinAppUi App =>
        _app ?? throw new InvalidOperationException(
            "Test session has not been initialized. Ensure [ClassInitialize] has run.");

    /// <summary>In-process UIA property reader (fallback for properties winapp can't surface).</summary>
    public static IUiaPropertyReader Uia =>
        _uia ?? throw new InvalidOperationException(
            "Test session has not been initialized. Ensure [ClassInitialize] has run.");

    /// <summary>HWND of the primary Host window.</summary>
    public static long HostHwnd => App.HostHwnd;

    /// <summary>PID of the Host process.</summary>
    public static int HostPid => _appProcess?.Id ?? 0;

    /// <summary>
    /// Called by each test class's ClassInitialize. Only the first call actually starts the
    /// session; subsequent calls increment the ref count.
    /// </summary>
    public static void AssemblyInit(object? context = null)
    {
        _refCount++;

        if (_app != null)
        {
            Console.WriteLine($"Session already active (ref {_refCount}), reusing.");
            return;
        }

        // Bail out cleanly if the desktop is locked / disconnected, so flake reports don't
        // drown in environmental noise.
        SessionInteractivityGuard.EnsureInteractive("TestSession.AssemblyInit");

        KillOrphanedProcesses();

        var exePath = FindHostExe();
        Console.WriteLine($"Host app: {exePath}");

        try
        {
            var (proc, hwnd) = HostLaunch.LaunchAndBind(exePath, WindowTitle);
            _appProcess = proc;
            _app = new WinAppUi(proc.Id, hwnd);
            _uia = new UiaPropertyReader(hwnd);
            Console.WriteLine($"winapp UI automation bound to Host window (HWND 0x{hwnd:X}).");
        }
        catch (Exception ex) when (ex is WinAppException or TimeoutException)
        {
            // A mid-init screen lock surfaces here. Reclassify as Inconclusive when locked.
            SessionInteractivityGuard.RecheckAfterFailure("TestSession bootstrap");
            throw;
        }
    }

    /// <summary>
    /// Called by each test class's ClassCleanup. Only the last call (ref count drops to zero)
    /// actually tears down the session and kills the process.
    /// </summary>
    public static void AssemblyCleanup()
    {
        _refCount--;

        if (_refCount > 0)
        {
            Console.WriteLine($"Session still in use (ref {_refCount}), skipping cleanup.");
            return;
        }

        ForceCleanup();
    }

    /// <summary>Unconditionally tears down the session and kills the Host process.</summary>
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
        foreach (var proc in Process.GetProcessesByName("Reactor.AppTests.Host"))
        {
            try
            {
                Console.WriteLine($"Killing orphaned Host app (PID {proc.Id}).");
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

        var exe = Path.Combine(dir, "tests", "Reactor.AppTests.Host", "bin", platform,
            "Debug", "net10.0-windows10.0.22621.0", "Reactor.AppTests.Host.exe");

        if (!File.Exists(exe))
            throw new FileNotFoundException($"Build the Host app first. Expected: {exe}");

        return exe;
    }
}
