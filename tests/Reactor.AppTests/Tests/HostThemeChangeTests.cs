using System;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.UI.Reactor.AppTests.Infrastructure;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// E2E coverage for issue #679 (b) — the host theme-change → ThemeRef re-resolution wiring
/// (#86 / #751). The <c>ThemeChange_ResourceOverrideReResolve</c> host fixture binds a CONCRETE
/// <c>ResourceOverride</c> ThemeRef brush (which, unlike a <c>{ThemeResource}</c>
/// <c>.Foreground(Theme.X)</c> setter, does NOT self-heal natively) and toggles the window-root
/// <c>RequestedTheme</c> without any fixture <c>setState</c>. So the surfaced brush color can
/// only change if the host's <c>ActualThemeChanged → RequestRender</c> wiring re-renders Reactor
/// and re-runs <c>ApplyResourceOverrides</c> — no manual <c>InvalidateCache()</c> anywhere.
///
/// Non-vacuous: break the host <c>RequestRender</c> and the color stays stale → this fails.
///
/// Residual gap (named): the strict same-theme-<i>name</i> invalidation path (system
/// <c>ColorValuesChanged</c> / accent / high-contrast, where <c>themeName</c> is unchanged) is not
/// deterministically reproducible in the winapp ui tier; it remains covered in-process by the
/// selftest <c>ThemeBrushCacheFixtures.InvalidationReResolves</c>.
/// </summary>
[TestClass]
public class HostThemeChangeTests : AppTestBase
{
    private const string Fixture = "ThemeChange_ResourceOverrideReResolve";

    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    /// <summary>
    /// Toggling the real host theme must re-resolve the concrete ThemeRef override brush
    /// (color flips), and toggling back must return it to the original theme's brush.
    /// </summary>
    // [Retry] mops up rare unattended-desktop UIA read flakes; a real regression fails every attempt.
    [E2eRetry(3)]
    [TestMethod]
    public void HostThemeChange_ReResolvesConcreteResourceOverride()
    {
        NavigateToFixtureFresh(Fixture);

        var initial = WaitForConcreteProbeColor();

        // Real host theme change (window-root RequestedTheme); the fixture forces no re-render,
        // so only the host ActualThemeChanged → RequestRender wiring can update the brush.
        ClickButton("ThemeToggleBtn");
        var toggled = WaitUntilProbeColorChanges(initial);
        Assert.AreNotEqual(initial, toggled,
            "Concrete ResourceOverride ThemeRef brush should re-resolve after a real host theme change " +
            "driven solely by the host RequestRender wiring.");

        // Toggle back — re-resolves to the original theme's brush and restores the session theme.
        ClickButton("ThemeToggleBtn");
        var restored = WaitUntilProbeColorChanges(toggled);
        Assert.AreEqual(initial, restored,
            "Toggling the host theme back should re-resolve the override to the original theme's brush.");
    }

    private string ReadProbeColor() => App.GetValue("ThemeProbeColor") ?? "";

    private string WaitForConcreteProbeColor(int timeoutMs = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var cur = ReadProbeColor();
            if (cur.StartsWith("Probe: #", StringComparison.Ordinal)) return cur;
            Thread.Sleep(150);
        }
        Assert.Fail($"ThemeProbeColor never resolved to a concrete '#' value within {timeoutMs}ms " +
                    $"(last: '{ReadProbeColor()}').");
        return ""; // unreachable
    }

    private string WaitUntilProbeColorChanges(string from, int timeoutMs = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            var cur = ReadProbeColor();
            if (cur.StartsWith("Probe: #", StringComparison.Ordinal) && cur != from) return cur;
            Thread.Sleep(150);
        }
        Assert.Fail($"ThemeProbeColor did not change from '{from}' within {timeoutMs}ms " +
                    $"(last: '{ReadProbeColor()}').");
        return ""; // unreachable
    }
}
