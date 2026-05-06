using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Windows;
using OpenQA.Selenium.Support.UI;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Base class for all Appium-based UI test classes.
/// Provides helpers for navigation, element lookup, waiting, and DPI-aware assertions.
/// </summary>
public class AppTestBase
{
    /// <summary>
    /// The active WindowsDriver session.
    /// </summary>
    protected static WindowsDriver<WindowsElement> Session => TestSession.Session;

    // Per-test interactivity preflight — bails out as Inconclusive (not Failed)
    // when the workstation is locked or the session is disconnected, so flake
    // reports don't drown in environmental noise.
    [TestInitialize]
    public void GuardSessionInteractive()
    {
        SessionInteractivityGuard.EnsureInteractive("TestInitialize");
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

        var expected = $"Loaded: {name}";

        // Click + wait. If the click is silently absorbed (observed when the
        // previous test left a flyout open, or when a Reset re-render races the
        // navigator's hit-test rebuild), the wait times out — retry the click
        // once before giving up. This keeps fast paths fast (no extra waits in
        // the common case) but absorbs the occasional missed click.
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Session.FindElement(MobileBy.AccessibilityId($"Nav_{name}")).Click();
                try
                {
                    WaitForText("FixtureStatus", expected, timeoutMs: 5000);
                    _currentFixture = name;
                    return;
                }
                catch (WebDriverTimeoutException) when (attempt == 0)
                {
                    // Brief pause before the retry so the next click doesn't land in
                    // the same window that swallowed the first one.
                    Thread.Sleep(250);
                }
            }
        }
        catch (WebDriverException)
        {
            // The screen may have locked between the preflight check and the click.
            // Recheck — if locked, surface as Inconclusive; otherwise rethrow as a
            // real test failure.
            SessionInteractivityGuard.RecheckAfterWebDriverFailure($"NavigateToFixture({name})");
            throw;
        }
    }

    /// <summary>
    /// Forces re-navigation to the fixture even if it's the current one.
    /// Use when the test modifies fixture state and needs a fresh start.
    /// </summary>
    /// <remarks>
    /// Resets the host to "Ready" first (which un-mounts the fixture's component
    /// tree, discarding any useState) and then clicks the Nav_ button to remount.
    /// Without the reset step, clicking the same nav button a second time is a
    /// no-op in TestHost (setFixture is called with the same value), and state
    /// from the previous run leaks into the next test.
    /// </remarks>
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
            var reset = Session.FindElement(MobileBy.AccessibilityId("ResetFixture"));
            reset.Click();
            WaitForText("FixtureStatus", "Ready", timeoutMs: 3000);
        }
        catch (WebDriverException)
        {
            // Reset button may not be present yet (e.g., before first navigation).
        }
    }

    /// <summary>
    /// Finds an element by its AutomationId (UIA accessibility identifier).
    /// </summary>
    protected WindowsElement FindById(string automationId)
    {
        return Session.FindElement(MobileBy.AccessibilityId(automationId));
    }

    /// <summary>
    /// Finds an element by its Name property.
    /// </summary>
    protected WindowsElement FindByName(string name)
    {
        return Session.FindElement(MobileBy.Name(name));
    }

    /// <summary>
    /// Waits for an element with the given AutomationId to appear.
    /// </summary>
    protected WindowsElement WaitForElement(string automationId, int timeoutMs = 5000)
    {
        var wait = new DefaultWait<WindowsDriver<WindowsElement>>(Session)
        {
            Timeout = TimeSpan.FromMilliseconds(timeoutMs),
            PollingInterval = TimeSpan.FromMilliseconds(100),
        };
        wait.IgnoreExceptionTypes(typeof(WebDriverException));

        return wait.Until(driver => driver.FindElement(MobileBy.AccessibilityId(automationId)));
    }

    /// <summary>
    /// Waits until the element with the given AutomationId displays the expected text.
    /// </summary>
    protected void WaitForText(string automationId, string expectedText, int timeoutMs = 5000)
    {
        var wait = new DefaultWait<WindowsDriver<WindowsElement>>(Session)
        {
            Timeout = TimeSpan.FromMilliseconds(timeoutMs),
            PollingInterval = TimeSpan.FromMilliseconds(100),
        };
        wait.IgnoreExceptionTypes(typeof(WebDriverException));

        string lastSeen = "<not found>";
        try
        {
            wait.Until(driver =>
            {
                var element = driver.FindElement(MobileBy.AccessibilityId(automationId));
                lastSeen = element.Text ?? "<null>";
                return lastSeen == expectedText ? element : null;
            });
        }
        catch (WebDriverTimeoutException)
        {
            throw new WebDriverTimeoutException(
                $"Timed out after {timeoutMs}ms waiting for AutomationId='{automationId}' " +
                $"to have text '{expectedText}'. Last-seen text: '{lastSeen}'.");
        }
    }

    /// <summary>
    /// Waits until the element's text contains the expected substring.
    /// Returns the element text for use in assertion messages.
    /// </summary>
    protected string WaitForTextContaining(string automationId, string substring, int timeoutMs = 5000)
    {
        var wait = new DefaultWait<WindowsDriver<WindowsElement>>(Session)
        {
            Timeout = TimeSpan.FromMilliseconds(timeoutMs),
            PollingInterval = TimeSpan.FromMilliseconds(100),
        };
        wait.IgnoreExceptionTypes(typeof(WebDriverException));

        string lastText = "";
        wait.Until(driver =>
        {
            var element = driver.FindElement(MobileBy.AccessibilityId(automationId));
            lastText = element.Text ?? "";
            return lastText.Contains(substring) ? element : null;
        });
        return lastText;
    }

    /// <summary>
    /// Reads the DPI scale factor from the TestHostRoot element.
    /// The Host app sets its Name property to "DpiScale:X.XXXX".
    /// </summary>
    protected double GetDpiScale()
    {
        var root = Session.FindElement(MobileBy.AccessibilityId("TestHostRoot"));
        var name = root.GetAttribute("Name");

        // Expected format: "DpiScale:1.5000"
        if (name != null && name.StartsWith("DpiScale:") &&
            double.TryParse(name["DpiScale:".Length..],
                global::System.Globalization.NumberStyles.Float,
                global::System.Globalization.CultureInfo.InvariantCulture,
                out var scale))
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
        var element = FindById(automationId);
        return element.Rect;
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
        try
        {
            var element = Session.FindElement(MobileBy.AccessibilityId(nameOrId));
            element.Click();
        }
        catch (WebDriverException)
        {
            var element = Session.FindElement(MobileBy.Name(nameOrId));
            element.Click();
        }
    }
}
