using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #1506: the contrast setting that <see cref="ThemeRef"/> resolution uses is re-read on
/// every cache invalidation, and a system change refreshes the hosts once, only when the
/// setting really moved. Resolution against the <c>HighContrast</c> dictionary needs live
/// WinUI resources and is covered by the <c>HighContrastThemeFixtures</c> selftests.
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
        HighContrastState.ResetForTest();
        ThemeRef.InvalidateResolutionCache();
    }

    [Fact]
    public void ContrastSetting_IsReReadWhenTheResolutionCacheIsInvalidated()
    {
        var contrastOn = false;
        HighContrastState.ReaderForTest = () => contrastOn;
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
    public void SettingChange_RefreshesHostsOnlyWhenContrastMoved()
    {
        var listener = new FakeListener();
        var contrastOn = false;
        HighContrastState.ReaderForTest = () => contrastOn;
        HighContrastState.ResetForTest();
        using var isolation = ThemeResourceListeners.IsolateForTest(listener);
        try
        {
            _ = HighContrastState.IsActive;               // a brush was resolved with contrast off

            HighContrastState.OnSystemSettingChanged();   // some other setting changed
            Assert.Equal(0, listener.Notified);

            contrastOn = true;
            HighContrastState.OnSystemSettingChanged();   // a contrast theme was turned on
            Assert.Equal(1, listener.Notified);

            HighContrastState.OnSystemSettingChanged();   // the same broadcast, seen by a second window
            Assert.Equal(1, listener.Notified);

            contrastOn = false;
            HighContrastState.OnSystemSettingChanged();   // turned off again
            Assert.Equal(2, listener.Notified);
        }
        finally
        {
            Restore();
        }
    }
}
