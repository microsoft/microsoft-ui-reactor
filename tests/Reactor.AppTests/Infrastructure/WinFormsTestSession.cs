using System.ComponentModel;
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
        if (_app != null)
        {
            _refCount++;
            Console.WriteLine($"WinForms session already active (ref {_refCount}), reusing.");
            return;
        }

        SessionInteractivityGuard.EnsureInteractive("WinFormsTestSession.Init");

        // Resolved before the sweep so it can distinguish this checkout's orphans from
        // another checkout's live host. See OrphanedHostSweep.
        var exePath = FindHostExe();
        Console.WriteLine($"WinForms host: {exePath}");

        OrphanedHostSweep.KillOrphansOf(ProcessName, exePath, "WinForms host");

        Process? launched = null;

        try
        {
            var (proc, hwnd) = HostLaunch.LaunchAndBind(exePath, WindowTitle);
            launched = proc;
            var app = new WinAppUi(proc.Id, hwnd);
            var uia = new UiaPropertyReader(hwnd);

            // Published only on full success, for the reason given in TestSession.AssemblyInit:
            // the reuse path above keys on _app alone, so a throw between _app and _uia would
            // hand the next class a session whose reader was never built.
            _appProcess = proc;
            _app = app;
            _uia = uia;
            Console.WriteLine($"winapp UI automation bound to WinForms host (HWND 0x{hwnd:X}).");
        }
        catch (Exception ex)
        {
            // Nothing was published, so ForceCleanup cannot see this process and nothing else
            // will reap it. See TestSession.AssemblyInit.
            KillAndDispose(ref launched);

            if (ex is WinAppException or TimeoutException)
                SessionInteractivityGuard.RecheckAfterFailure("WinFormsTestSession bootstrap");

            throw;
        }

        // Counted only on success. See TestSession.AssemblyInit for why an increment taken
        // before the work leaks: MSTest does not run a class's cleanup when its initialize
        // threw, so the count never returns to zero and the host and its lease outlive the run.
        _refCount++;
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
        KillAndDispose(ref _appProcess);
    }

    /// <summary>
    /// Kills <paramref name="proc"/> if it is still running, disposes it and clears the
    /// reference. See <see cref="TestSession"/> for why failures are swallowed.
    /// </summary>
    private static void KillAndDispose(ref Process? proc)
    {
        if (proc is null) return;

        using var doomed = proc;
        proc = null;

        try
        {
            if (!doomed.HasExited)
            {
                doomed.Kill();
                doomed.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            // See TestSession.KillAndDispose for why exactly these three are swallowed.
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
