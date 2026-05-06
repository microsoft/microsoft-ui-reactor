using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Manages the Appium WindowsDriver session for the test assembly.
/// Uses a two-step approach: launches the Host app as a process, then creates
/// a WinAppDriver Desktop session and attaches to the app window. This avoids
/// WinAppDriver's app-launch session which can fail with WinUI3 apps.
///
/// The session is shared across all test classes — the first ClassInitialize
/// call starts it, and the last ClassCleanup call tears it down.
/// </summary>
public class TestSession
{
    private const string WinAppDriverUrl = "http://127.0.0.1:4723";

    private static WindowsDriver<WindowsElement>? _session;
    private static Process? _appProcess;
    private static int _refCount;

    public static WindowsDriver<WindowsElement> Session =>
        _session ?? throw new InvalidOperationException(
            "Test session has not been initialized. Ensure [ClassInitialize] has run.");

    /// <summary>
    /// Called by each test class's ClassInitialize. Only the first call actually
    /// starts the session; subsequent calls increment the ref count.
    /// </summary>
    public static void AssemblyInit(TestContext context)
    {
        _refCount++;

        if (_session != null)
        {
            Console.WriteLine($"Session already active (ref {_refCount}), reusing.");
            return;
        }

        // Bail out cleanly if the desktop is already locked / disconnected.
        // Without this, we'd spend minutes booting WinAppDriver + the host app,
        // only to fail every test on the first Click() with a generic error.
        SessionInteractivityGuard.EnsureInteractive("TestSession.AssemblyInit");

        // Kill any orphaned processes from a previous failed run
        KillOrphanedProcesses();

        WinAppDriverHelper.Start();

        // Step 1: Launch the Host app as a regular process
        var exePath = FindHostExe();
        Console.WriteLine($"Host app: {exePath}");

        _appProcess = Process.Start(new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
        });
        Console.WriteLine($"Host app launched (PID {_appProcess?.Id}).");

        try
        {
            // Poll for the app window instead of a fixed sleep. WaitForHostWindow
            // throws TimeoutException after swallowing per-poll WebDriverExceptions —
            // a mid-init screen lock surfaces here, not as a WebDriverException.
            WaitForHostWindow();

            // Step 2: Create a Desktop session and find the app window
            var desktopOptions = new AppiumOptions();
            desktopOptions.AddAdditionalCapability("app", "Root");
            desktopOptions.AddAdditionalCapability("deviceName", "WindowsPC");

            using var desktopSession = new WindowsDriver<WindowsElement>(
                new Uri(WinAppDriverUrl), desktopOptions);

            // Find the Host app window by title
            var appWindow = desktopSession.FindElementByName("Reactor Test Host");
            var appWindowHandle = appWindow.GetAttribute("NativeWindowHandle");
            var hwnd = int.Parse(appWindowHandle).ToString("x"); // hex for WinAppDriver

            // Step 3: Create a session attached to the app window
            var appOptions = new AppiumOptions();
            appOptions.AddAdditionalCapability("appTopLevelWindow", $"0x{hwnd}");
            appOptions.AddAdditionalCapability("deviceName", "WindowsPC");

            _session = new WindowsDriver<WindowsElement>(new Uri(WinAppDriverUrl), appOptions);
            _session.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(2);

            Console.WriteLine("WindowsDriver session attached to Host app.");
        }
        catch (Exception ex) when (ex is OpenQA.Selenium.WebDriverException || ex is TimeoutException)
        {
            // Catches both: WebDriverException from the session steps, and
            // TimeoutException from WaitForHostWindow. Either could mask a
            // workstation lock that happened after the AssemblyInit preflight.
            SessionInteractivityGuard.RecheckAfterWebDriverFailure("TestSession session bootstrap");
            throw;
        }
    }

    /// <summary>
    /// Called by each test class's ClassCleanup. Only the last call (ref count
    /// drops to zero) actually tears down the session and kills processes.
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

    /// <summary>
    /// Unconditionally tears down the session and kills all processes.
    /// </summary>
    public static void ForceCleanup()
    {
        _refCount = 0;

        if (_session != null)
        {
            try { _session.Quit(); }
            catch (Exception ex) { Console.WriteLine($"Warning: session close failed: {ex.Message}"); }
            finally { _session = null; }
        }

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

        WinAppDriverHelper.Stop();
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

    private static void WaitForHostWindow()
    {
        // Poll for the window to appear rather than a fixed 3s sleep.
        // The app typically renders in ~1s; we poll up to 5s as a safety net.
        for (int i = 0; i < 25; i++)
        {
            Thread.Sleep(200);
            try
            {
                var opts = new AppiumOptions();
                opts.AddAdditionalCapability("app", "Root");
                opts.AddAdditionalCapability("deviceName", "WindowsPC");
                using var desktop = new WindowsDriver<WindowsElement>(
                    new Uri(WinAppDriverUrl), opts);
                desktop.FindElementByName("Reactor Test Host");
                Console.WriteLine($"Host window found after {(i + 1) * 200}ms.");
                return;
            }
            catch { }
        }

        throw new TimeoutException("Host app window did not appear within 5 seconds.");
    }

    private static string FindHostExe()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Reactor.sln")))
            dir = Path.GetDirectoryName(dir);

        if (dir == null)
            throw new DirectoryNotFoundException("Could not find repo root (Reactor.sln)");

        var platform = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "ARM64",
            _ => "x64"
        };

        var exe = Path.Combine(dir, "tests", "Reactor.AppTests.Host", "bin", platform,
            "Debug", "net9.0-windows10.0.22621.0", "Reactor.AppTests.Host.exe");

        if (!File.Exists(exe))
            throw new FileNotFoundException($"Build the Host app first. Expected: {exe}");

        return exe;
    }
}
