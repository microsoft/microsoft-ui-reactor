using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Manages the Appium WindowsDriver session for WinForms interop tests.
/// Same two-step pattern as <see cref="TestSession"/> but launches the
/// WinForms test host (Reactor.WinFormsTests.Host) instead of the WinUI host.
/// </summary>
public class WinFormsTestSession
{
    private const string WinAppDriverUrl = "http://127.0.0.1:4723";
    private const string WindowTitle = "WinForms Interop Test Host";
    private const string ProcessName = "Reactor.WinFormsTests.Host";

    private static WindowsDriver<WindowsElement>? _session;
    private static Process? _appProcess;
    private static int _refCount;

    public static WindowsDriver<WindowsElement> Session =>
        _session ?? throw new InvalidOperationException(
            "WinForms test session has not been initialized. Ensure [ClassInitialize] has run.");

    public static void Init(TestContext context)
    {
        _refCount++;

        if (_session != null)
        {
            Console.WriteLine($"WinForms session already active (ref {_refCount}), reusing.");
            return;
        }

        KillOrphanedProcesses();
        WinAppDriverHelper.Start();

        var exePath = FindHostExe();
        Console.WriteLine($"WinForms host: {exePath}");

        _appProcess = Process.Start(new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
        });
        Console.WriteLine($"WinForms host launched (PID {_appProcess?.Id}).");

        WaitForWindow();

        // Create a Desktop session and find the app window
        var desktopOptions = new AppiumOptions();
        desktopOptions.AddAdditionalCapability("app", "Root");
        desktopOptions.AddAdditionalCapability("deviceName", "WindowsPC");

        using var desktopSession = new WindowsDriver<WindowsElement>(
            new Uri(WinAppDriverUrl), desktopOptions);

        var appWindow = desktopSession.FindElementByName(WindowTitle);
        var appWindowHandle = appWindow.GetAttribute("NativeWindowHandle");
        var hwnd = int.Parse(appWindowHandle).ToString("x");

        var appOptions = new AppiumOptions();
        appOptions.AddAdditionalCapability("appTopLevelWindow", $"0x{hwnd}");
        appOptions.AddAdditionalCapability("deviceName", "WindowsPC");

        _session = new WindowsDriver<WindowsElement>(new Uri(WinAppDriverUrl), appOptions);
        _session.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(2);

        Console.WriteLine("WindowsDriver session attached to WinForms host.");
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

    private static void WaitForWindow()
    {
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
                desktop.FindElementByName(WindowTitle);
                Console.WriteLine($"WinForms host window found after {(i + 1) * 200}ms.");
                return;
            }
            catch { }
        }

        throw new TimeoutException("WinForms host window did not appear within 5 seconds.");
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
