using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Base class for WinForms interop E2E tests. Provides helpers for element lookup, waiting,
/// focus verification, and Tab testing. Drives the WinForms host through
/// <see cref="WinAppUi"/> (winapp ui), with Win32 <see cref="InputInjector"/> for the
/// real keystrokes (Tab/Shift+Tab/typing) winapp can't synthesize.
/// </summary>
public class WinFormsTestBase
{
    protected static WinAppUi App => WinFormsTestSession.App;

    protected static IUiaPropertyReader Uia => WinFormsTestSession.Uia;

    protected static long HostHwnd => WinFormsTestSession.HostHwnd;

    /// <summary>Injected by MSTest; used to attribute winapp invocation counts to each test.</summary>
    public TestContext? TestContext { get; set; }

    private long _winappCountAtStart;
    private System.Diagnostics.Stopwatch? _testStopwatch;

    [TestInitialize]
    public void SnapshotWinAppCount()
    {
        _winappCountAtStart = WinAppUi.InvocationCount;
        _testStopwatch = System.Diagnostics.Stopwatch.StartNew();
    }

    // Record how many winapp.exe processes this test spawned (process-per-call overhead).
    [TestCleanup]
    public void RecordWinAppInvocations()
    {
        var spawned = WinAppUi.InvocationCount - _winappCountAtStart;
        var seconds = (_testStopwatch?.Elapsed.TotalSeconds) ?? 0;
        var name = TestContext?.TestName ?? GetType().Name;
        TestContext?.WriteLine($"winapp-invocations={spawned}");
        WinAppMetrics.Record(name, spawned, seconds);
    }

    protected static UiElement Element(string selector, string? automationId = null, UiRect? cachedBounds = null) =>
        new(App, Uia, selector, automationId, WinFormsTestSession.HostHwnd, cachedBounds);

    protected UiElement FindById(string automationId)
        => UiElementResolver.FindByAutomationId(App, Uia, HostHwnd, automationId);

    protected UiElement FindByName(string name)
        => UiElementResolver.FindByName(App, Uia, HostHwnd, name);

    protected UiElement WaitForElement(string automationId, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try { return FindById(automationId); }
            catch (WinAppException ex) { last = ex; }
            Thread.Sleep(100);
        }

        throw new WinAppTimeoutException(
            $"Timed out after {timeoutMs}ms waiting for AutomationId='{automationId}' to appear." +
            (last is null ? "" : $" Last error: {last.Message}"));
    }

    protected void WaitForText(string automationId, string expectedText, int timeoutMs = 5000)
    {
        if (!App.WaitForValue(automationId, expectedText, contains: false, timeoutMs: timeoutMs))
        {
            var seen = App.GetValue(automationId) ?? "<not found>";
            throw new WinAppTimeoutException(
                $"Timed out after {timeoutMs}ms waiting for AutomationId='{automationId}' " +
                $"to have text '{expectedText}'. Last-seen text: '{seen}'.");
        }
    }

    protected string WaitForTextContaining(string automationId, string substring, int timeoutMs = 5000)
    {
        if (!App.WaitForValue(automationId, substring, contains: true, timeoutMs: timeoutMs))
        {
            var seen = App.GetValue(automationId) ?? "<not found>";
            throw new WinAppTimeoutException(
                $"Timed out after {timeoutMs}ms waiting for AutomationId='{automationId}' " +
                $"text to contain '{substring}'. Last-seen text: '{seen}'.");
        }
        return App.GetValue(automationId) ?? "";
    }

    /// <summary>
    /// Sends a Tab key press to the currently focused element. Foregrounds the host first so
    /// the injected keystroke routes to it.
    /// </summary>
    protected void SendTab()
    {
        InputInjector.Foreground(HostHwnd);
        InputInjector.Tab();
    }

    /// <summary>
    /// Sends a Shift+Tab key press to the currently focused element.
    /// </summary>
    protected void SendShiftTab()
    {
        InputInjector.Foreground(HostHwnd);
        InputInjector.ShiftTab();
    }

    /// <summary>
    /// Clicks an element by AccessibilityId first, falling back to Name.
    /// </summary>
    protected void ClickElement(string nameOrId)
    {
        if (App.Search(nameOrId).Any(m =>
                string.Equals(m.AutomationId, nameOrId, StringComparison.Ordinal) ||
                string.Equals(m.Selector, nameOrId, StringComparison.Ordinal)))
        {
            App.Invoke(nameOrId);
            return;
        }
        FindByName(nameOrId).Invoke();
    }

    /// <summary>
    /// Returns the AutomationId of the currently focused element (live UIA focus).
    /// Returns empty string if the focused element has no AutomationId or on error.
    /// </summary>
    protected string GetFocusedAutomationId() => Uia.GetFocusedAutomationId();

    /// <summary>
    /// Polls until the focused element's AutomationId matches the expected value.
    /// Focus transitions (especially across WinForms ↔ XAML Island boundaries)
    /// are asynchronous — this avoids flaky assertions from checking too early.
    /// </summary>
    protected void AssertFocused(string expectedAutomationId, string step, int timeoutMs = 2000)
    {
        string actual = "";
        int elapsed = 0;
        const int pollMs = 50;

        while (elapsed < timeoutMs)
        {
            actual = GetFocusedAutomationId();
            if (actual == expectedAutomationId)
                return;
            Thread.Sleep(pollMs);
            elapsed += pollMs;
        }

        actual = GetFocusedAutomationId();
        Assert.AreEqual(expectedAutomationId, actual,
            $"[{step}] Expected focus on '{expectedAutomationId}' but found '{actual}'");
    }
}
