using System.ComponentModel;
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
        if (_app != null)
        {
            _refCount++;
            Console.WriteLine($"Session already active (ref {_refCount}), reusing.");
            return;
        }

        // Bail out cleanly if the desktop is locked / disconnected, so flake reports don't
        // drown in environmental noise.
        SessionInteractivityGuard.EnsureInteractive("TestSession.AssemblyInit");

        // Resolve the host we are about to launch *before* sweeping, so the sweep can tell
        // this checkout's orphans apart from another checkout's live host.
        var exePath = FindHostExe();
        Console.WriteLine($"Host app: {exePath}");

        OrphanedHostSweep.KillOrphansOf("Reactor.AppTests.Host", exePath, "Host app");

        Process? launched = null;

        try
        {
            // Outside winapp's turn arbitration on purpose, and the one gap in this tier's
            // concurrency story: the Host is launched and foregrounded directly, before any
            // `winapp ui` call has been made and so before this suite holds a turn to be
            // arbitrated against. Two suites starting at once can therefore each raise a Host
            // over the other's input. Closing it would need winapp to hold a turn on behalf of
            // a process it did not spawn, which the CLI does not expose; TESTING.md documents
            // the narrowed guarantee rather than leaving it implied.
            var (proc, hwnd) = HostLaunch.LaunchAndBind(exePath, WindowTitle);
            launched = proc;
            var app = new WinAppUi(proc.Id, hwnd);
            var uia = new UiaPropertyReader(hwnd);
            Console.WriteLine($"winapp UI automation bound to Host window (HWND 0x{hwnd:X}).");

            // Published only once every step above has succeeded, and deliberately the last
            // thing in the block - including after the log line, since a redirected stdout can
            // throw and that would leave a dead session looking healthy to the next class.
            // Assigning as we went would make a later failure indistinguishable from a healthy
            // session: AssemblyInit's reuse path keys on _app alone, so a throw between _app and
            // _uia left the next class incrementing the ref count on a session whose reader was
            // never built, and failing later on a null field far from the actual cause.
            _appProcess = proc;
            _app = app;
            _uia = uia;
        }
        catch (Exception ex)
        {
            // The statics were never assigned, so ForceCleanup cannot see this process and
            // nothing else will ever reap it. Left alive it holds the liveness lease, which
            // defers the orphan sweep of every concurrent run of this checkout for as long as
            // it lives — the same leak the ref-count ordering below exists to avoid.
            KillAndDispose(ref launched);

            // Unchanged for the two types that were previously filtered: a mid-init screen lock
            // surfaces as one of these and is reclassified as Inconclusive. Everything else —
            // a COMException out of the UIA reader, say — now gets the cleanup without being
            // reclassified, rather than escaping before either could happen.
            if (ex is WinAppException or TimeoutException)
                SessionInteractivityGuard.RecheckAfterFailure("TestSession bootstrap");

            throw;
        }

        // Counted only here, because every statement above can throw and MSTest does not run a
        // class's cleanup when its initialize failed. An increment taken before the work is
        // therefore never balanced: the next class that initializes successfully sees a count
        // of two, its own cleanup takes it to one rather than zero, and the host process and
        // its liveness lease survive the whole assembly. Leaking the lease is the worse half —
        // it keeps every concurrent run of this checkout deferring its sweep indefinitely.
        _refCount++;
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
        KillAndDispose(ref _appProcess);
    }

    /// <summary>
    /// Kills <paramref name="proc"/> if it is still running, disposes it, and clears the
    /// reference, swallowing anything that goes wrong.
    /// </summary>
    /// <remarks>
    /// Shared by normal teardown and by the bootstrap's failure path so a host launched by a
    /// failed initialization is reaped exactly the way a successful one is. Failures are
    /// swallowed because both callers are already cleaning up: the process may have exited on
    /// its own between the check and the kill, and in the bootstrap case an exception here
    /// would replace the one that actually explains the failure.
    /// </remarks>
    internal static void KillAndDispose(ref Process? proc)
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
            // The three ways a teardown kill legitimately fails: the process exited between
            // the check and the kill, it is not one this API can terminate, or Windows refused
            // the operation. In every case it is either already gone or beyond our reach.
            // Anything else is a defect in this file and is deliberately left to propagate.
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
