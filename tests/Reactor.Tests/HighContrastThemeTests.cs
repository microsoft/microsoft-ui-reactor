using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #1506: theme references follow the contrast theme that WinUI has applied, as the
/// hosts' theme probes report it, not the setting Windows announces before WinUI applies it;
/// and a switch refreshes the hosts once. The probes themselves, and resolution against live
/// WinUI resources, are covered by the <c>HighContrastThemeFixtures</c> selftests.
/// </summary>
[Collection("ThemeResolutionCache")]
public sealed class HighContrastThemeTests
{
    private sealed class FakeListener : IThemeResourceListener
    {
        public int Notified { get; private set; }
        public void OnThemeResourcesChanged() => Notified++;
    }

    private static void Restore()
    {
        HighContrastState.ReaderForTest = null;
        HighContrastState.SystemReaderForTest = null;
        HighContrastState.ResetForTest();
        ThemeRef.InvalidateResolutionCache();
    }

    [Fact]
    public void SystemSetting_StandsInUntilAProbeReports_AndIsReReadOnInvalidation()
    {
        var contrastOn = false;
        HighContrastState.SystemReaderForTest = () => contrastOn;
        HighContrastState.ResetForTest();
        try
        {
            ThemeRef.InvalidateResolutionCache();
            Assert.False(HighContrastState.IsActive);

            contrastOn = true;
            Assert.False(HighContrastState.IsActive);   // cached: resolves stay cheap

            ThemeRef.InvalidateResolutionCache();       // what the hosts call on a theme or palette change
            Assert.True(HighContrastState.IsActive);
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public void AnnouncedSwitch_IsNotUsedUntilWinUIHasAppliedIt()
    {
        var systemOn = false;
        HighContrastState.SystemReaderForTest = () => systemOn;
        HighContrastState.ResetForTest();
        HighContrastState.ProbeAttached();
        try
        {
            HighContrastState.OnApplied(false);         // WinUI renders the light theme
            Assert.False(HighContrastState.IsActive);

            systemOn = true;                            // Windows announces a contrast theme,
            ThemeRef.InvalidateResolutionCache();       // and a palette change re-renders
            Assert.False(HighContrastState.IsActive);   // before WinUI has applied it

            HighContrastState.OnApplied(true);          // the probe: now WinUI has
            Assert.True(HighContrastState.IsActive);

            systemOn = false;                           // the same on the way back
            ThemeRef.InvalidateResolutionCache();
            Assert.True(HighContrastState.IsActive);
            HighContrastState.OnApplied(false);
            Assert.False(HighContrastState.IsActive);
        }
        finally
        {
            HighContrastState.ProbeDetached();
            Restore();
        }
    }

    [Fact]
    public void ProbeReports_RefreshTheHostsOncePerSwitch()
    {
        var listener = new FakeListener();
        HighContrastState.ResetForTest();
        HighContrastState.ProbeAttached();
        using var isolation = ThemeResourceListeners.IsolateForTest(listener);
        try
        {
            HighContrastState.OnApplied(false);
            _ = HighContrastState.IsActive;             // a brush was resolved with contrast off
            HighContrastState.OnApplied(false);         // another host's probe: nothing moved
            Assert.Equal(0, listener.Notified);

            HighContrastState.OnApplied(true);          // WinUI applied a contrast theme
            Assert.Equal(1, listener.Notified);

            HighContrastState.OnApplied(true);          // the same switch, from a second host
            Assert.Equal(1, listener.Notified);

            HighContrastState.OnApplied(false);         // turned off again
            Assert.Equal(2, listener.Notified);
        }
        finally
        {
            HighContrastState.ProbeDetached();
            Restore();
        }
    }

    [Fact]
    public void FirstProbeReport_RefreshesNothingBeforeABrushWasResolved()
    {
        var listener = new FakeListener();
        HighContrastState.ResetForTest();
        HighContrastState.ProbeAttached();
        using var isolation = ThemeResourceListeners.IsolateForTest(listener);
        try
        {
            HighContrastState.OnApplied(true);          // a host's first report, at startup
            Assert.Equal(0, listener.Notified);
            Assert.True(HighContrastState.IsActive);    // and the first resolve uses it

            HighContrastState.OnApplied(false);
            Assert.Equal(1, listener.Notified);
        }
        finally
        {
            HighContrastState.ProbeDetached();
            Restore();
        }
    }

    [Fact]
    public void LastProbeDetached_TheSystemSettingStandsInAgain()
    {
        HighContrastState.SystemReaderForTest = static () => false;
        HighContrastState.ResetForTest();
        HighContrastState.ProbeAttached();
        HighContrastState.ProbeAttached();
        try
        {
            HighContrastState.OnApplied(true);
            HighContrastState.ProbeDetached();
            Assert.True(HighContrastState.IsActive);    // one host still reports what WinUI applies

            HighContrastState.ProbeDetached();
            Assert.False(HighContrastState.IsActive);   // none does: back to the system setting
        }
        finally
        {
            Restore();
        }
    }

    [Fact]
    public void SystemRead_SucceedsAndAgreesWithAccessibilitySettings()
    {
        Assert.True(HighContrastState.TryReadSystem(out var on));
        Assert.Equal(new global::Windows.UI.ViewManagement.AccessibilitySettings().HighContrast, on);
    }
}
