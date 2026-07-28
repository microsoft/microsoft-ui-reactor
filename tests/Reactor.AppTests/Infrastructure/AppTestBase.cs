using System.Drawing;
using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Base class for all UI test classes. Provides helpers for navigation, element lookup,
/// waiting, and DPI-aware assertions.
///
/// Drives the running Host app through <see cref="WinAppUi"/> (the <c>winapp ui</c> CLI,
/// UIA-based) instead of an Appium <c>WindowsDriver</c> session. Method signatures are kept
/// identical to the former Appium harness so existing test bodies keep their shape — element
/// handles are now <see cref="UiElement"/> rather than <c>WindowsElement</c>.
/// </summary>
public class AppTestBase
{
    /// <summary>The winapp-backed UI automation driver bound to the Host window.</summary>
    protected static WinAppUi App => TestSession.App;

    /// <summary>In-process UIA property reader (fallback for properties winapp can't surface).</summary>
    protected static IUiaPropertyReader Uia => TestSession.Uia;

    /// <summary>HWND of the primary Host window.</summary>
    protected static long HostHwnd => TestSession.HostHwnd;

    /// <summary>Build a <see cref="UiElement"/> handle for a selector against the host window.</summary>
    protected static UiElement Element(string selector, string? automationId = null, long hwnd = 0,
        UiRect? cachedBounds = null) =>
        new(App, Uia, selector, automationId, hwnd == 0 ? TestSession.HostHwnd : hwnd, cachedBounds);

    // Per-test interactivity preflight — bails out as Inconclusive (not Failed)
    // when the workstation is locked or the session is disconnected, so flake
    // reports don't drown in environmental noise.
    [TestInitialize]
    public void GuardSessionInteractive()
    {
        _winappCountAtStart = WinAppUi.InvocationCount;
        _testStopwatch = System.Diagnostics.Stopwatch.StartNew();
        SessionInteractivityGuard.EnsureInteractive("TestInitialize");
    }

    /// <summary>Injected by MSTest; used to attribute winapp invocation counts to each test.</summary>
    public TestContext? TestContext { get; set; }

    private long _winappCountAtStart;
    private System.Diagnostics.Stopwatch? _testStopwatch;

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

    private static string? _currentFixture;

    /// <summary>
    /// Navigates to a named test fixture by clicking its nav element and waiting
    /// for the fixture status to indicate it has loaded. Skips if already on
    /// the requested fixture (safe for read-only tests like accessibility checks).
    /// </summary>
    protected void NavigateToFixture(string name)
    {
        if (_currentFixture == name)
            return;

        // Navigating to a different fixture breaks any consecutive-send chain.
        UiElement.ResetTypingContext();

        var expected = $"Loaded: {name}";

        // Click + wait. If the click is silently absorbed (observed when the
        // previous test left a flyout open, or when a Reset re-render races the
        // navigator's hit-test rebuild), the wait times out — retry the click
        // once before giving up.
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                App.Invoke($"Nav_{name}");
                if (App.WaitForValue("FixtureStatus", expected, timeoutMs: 5000))
                {
                    _currentFixture = name;
                    return;
                }
                if (attempt == 0)
                    Thread.Sleep(250);
            }

            var lastSeen = App.GetValue("FixtureStatus") ?? "<not found>";
            throw new WinAppTimeoutException(
                $"Timed out waiting for fixture '{name}' to load (FixtureStatus expected " +
                $"'{expected}', last-seen '{lastSeen}').");
        }
        catch (WinAppException)
        {
            // The screen may have locked between the preflight check and the click.
            // Recheck — if locked, surface as Inconclusive; otherwise rethrow as a
            // real test failure.
            SessionInteractivityGuard.RecheckAfterFailure($"NavigateToFixture({name})");
            throw;
        }
    }

    /// <summary>
    /// Forces re-navigation to the fixture even if it's the current one.
    /// Use when the test modifies fixture state and needs a fresh start.
    /// </summary>
    protected void NavigateToFixtureFresh(string name)
    {
        ResetFixture();
        _currentFixture = null;
        NavigateToFixture(name);
    }

    /// <summary>
    /// Resets the current fixture to its default state.
    /// </summary>
    protected void ResetFixture()
    {
        try
        {
            if (!App.Exists("ResetFixture"))
                return; // not present yet (e.g., before first navigation)
            App.Invoke("ResetFixture");
        }
        catch (WinAppException)
        {
            // Reset button may not be present yet (e.g., before first navigation).
            return;
        }

        // The reset was invoked, so the fixture must report Ready. The reset click can be absorbed
        // when a re-render races the navigator's hit-test rebuild, so retry the invoke once and
        // allow a longer settle before giving up, rather than failing an otherwise-healthy test.
        // (A same-name re-nav no-ops in the host because UseState suppresses a rerender when the
        // value is unchanged, so a stale fixture would silently corrupt the next navigation.)
        // Thrown outside the catch above so it isn't swallowed as a "button not present" case.
        if (App.WaitForValue("FixtureStatus", "Ready", timeoutMs: 5000))
            return;

        try { App.Invoke("ResetFixture"); }
        catch (WinAppException) { /* button may be mid-rerender; the wait below is the real gate */ }

        if (!App.WaitForValue("FixtureStatus", "Ready", timeoutMs: 5000))
            throw new WinAppException(
                "ResetFixture was invoked but FixtureStatus never reached 'Ready' within 10000ms " +
                "across two attempts; the next navigation could run against stale fixture state.");
    }

    /// <summary>
    /// Selects (activates) a tab in a WinUI <c>TabView</c> by its visible header title.
    /// WinUI surfaces only the *selected* tab's content to UI Automation — an inactive tab's
    /// content subtree is not in the tree (verified: only the active tab's content elements
    /// resolve via <c>winapp ui search</c>). Tab *headers*, however, are always present (verified
    /// on a 3-tab group: inactive tabs still expose their TabItem + caption), so a test that
    /// needs to read an inactive tab's content activates that tab first.
    ///
    /// Resolution is deliberately indirect. The title is an ambiguous text selector — it
    /// substring-matches the caption TextBlock, the pane Group, and (for pinnable docking tabs)
    /// the pin button, whose AutomationId embeds the pane key (e.g. <c>pin:dock-input:right</c>).
    /// Worse, a pinnable tab renders a composite (StackPanel) header, so the <c>TabViewItem</c>
    /// itself has no Name and is NOT returned by a text search for the title — a direct "find the
    /// TabItem named X" lookup finds nothing, and a plain Invoke(title) toggles the pin button.
    /// So resolve the tab from its caption's owning <c>TabItem</c>.
    ///
    /// Prefer the <c>invokableAncestor</c> that <c>search</c> already computes in the SAME call:
    /// resolving via a second <c>inspect --ancestors</c> opens a re-render race (selecting the
    /// previous tab re-renders the strip and stales the caption's hash slug) — that race is why
    /// SelectTab worked for the first tab but threw "No tab header found" for the second. The
    /// inspect walk remains a fallback for older winapp builds that don't emit invokableAncestor.
    /// </summary>
    protected void SelectTab(string title)
    {
        var matches = App.Search(title);

        // A directly-named TabItem (string-header / non-pinnable tabs) can be invoked as-is.
        var namedTab = matches.FirstOrDefault(m =>
            string.Equals(m.Type, "TabItem", StringComparison.OrdinalIgnoreCase) && m.Name == title);
        if (namedTab is not null)
        {
            App.Invoke(namedTab.Selector);
            return;
        }

        // Composite/pinnable header: the caption TextBlock (exact Name==title) carries its owning
        // TabItem as invokableAncestor — race-free, from this one search call.
        var captionWithTab = matches.FirstOrDefault(m =>
            m.Name == title &&
            string.Equals(m.InvokableAncestorType, "TabItem", StringComparison.OrdinalIgnoreCase) &&
            m.InvokableAncestorSelector is not null);
        if (captionWithTab is not null)
        {
            App.Invoke(captionWithTab.InvokableAncestorSelector!);
            return;
        }

        // Fallback (older winapp without invokableAncestor): caption Text → inspect-ancestors walk.
        var caption = matches.FirstOrDefault(m =>
                          string.Equals(m.Type, "Text", StringComparison.OrdinalIgnoreCase) && m.Name == title)
                      ?? matches.FirstOrDefault(m => m.Name == title && !m.IsInvokable);
        if (caption is not null && App.ResolveAncestorTab(caption.Selector) is { } tabSelector)
        {
            App.Invoke(tabSelector);
            return;
        }

        throw new WinAppException($"No tab header found for title '{title}'.");
    }

    /// <summary>
    /// Finds an element by its AutomationId (UIA accessibility identifier).
    /// Throws when no element matches, mirroring the former FindElement contract.
    /// </summary>
    protected UiElement FindById(string automationId)
        => UiElementResolver.FindByAutomationId(App, Uia, HostHwnd, automationId);

    /// <summary>
    /// Finds an element by its Name property.
    /// </summary>
    protected UiElement FindByName(string name)
        => UiElementResolver.FindByName(App, Uia, HostHwnd, name);

    /// <summary>
    /// Waits for an element with the given AutomationId to appear.
    /// </summary>
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

    /// <summary>
    /// Waits until the element with the given AutomationId displays the expected text.
    /// </summary>
    protected void WaitForText(string automationId, string expectedText, int timeoutMs = 5000)
    {
        if (App.WaitForValue(automationId, expectedText, contains: false, timeoutMs: timeoutMs))
            return;

        var lastSeen = App.GetValue(automationId) ?? "<not found>";
        throw new WinAppTimeoutException(
            $"Timed out after {timeoutMs}ms waiting for AutomationId='{automationId}' " +
            $"to have text '{expectedText}'. Last-seen text: '{lastSeen}'.");
    }

    /// <summary>
    /// Waits until the element's text contains the expected substring.
    /// Returns the element text for use in assertion messages.
    /// </summary>
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
    /// Reads the DPI scale factor from the TestHostRoot element.
    /// The Host app sets its Name property to "DpiScale:X.XXXX".
    /// </summary>
    protected double GetDpiScale()
    {
        var name = App.GetProperty("TestHostRoot", "Name");

        // Expected format: "DpiScale:1.5000"
        if (name != null && name.StartsWith("DpiScale:") &&
            double.TryParse(name["DpiScale:".Length..],
                NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
        {
            return scale;
        }

        // Default to 1.0 if not available.
        return 1.0;
    }

    /// <summary>
    /// Asserts that <paramref name="actual"/> is within <paramref name="tolerance"/>
    /// of <paramref name="expected"/>.
    /// </summary>
    protected static void AssertNear(double actual, double expected, double tolerance)
    {
        var diff = Math.Abs(actual - expected);
        Assert.IsTrue(
            diff <= tolerance,
            $"Expected {expected} ± {tolerance}, but got {actual} (off by {diff}).");
    }

    /// <summary>
    /// Returns the UIA BoundingRectangle of the element as a <see cref="Rectangle"/>.
    /// </summary>
    protected Rectangle GetElementRect(string automationId)
    {
        return FindById(automationId).Rect;
    }

    /// <summary>
    /// Returns the logical (DPI-independent) size of an element as (width, height).
    /// </summary>
    protected (double Width, double Height) GetLogicalSize(string automationId)
    {
        var rect = GetElementRect(automationId);
        var dpi = GetDpiScale();
        return (rect.Width / dpi, rect.Height / dpi);
    }

    /// <summary>
    /// Clicks a button by AccessibilityId first, falling back to Name.
    /// </summary>
    protected void ClickButton(string nameOrId)
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
}
