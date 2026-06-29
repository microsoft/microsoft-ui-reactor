using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #660 (#86) review regression coverage for the (key,theme)→Brush
/// resolution cache in <see cref="ThemeRef"/>. Real WinUI app context so
/// <c>Application.Current.Resources</c> exists.
///
/// H2 — a null miss must NOT be cached: a key unknown at first resolve (e.g. its
/// ResourceDictionary is merged in later — XamlIslandBootstrap / docking embed
/// windows do this) must resolve once it becomes available, not stay stuck on a
/// poisoned cached null.
///
/// H1 (mechanism) — InvalidateResolutionCache (called by the hosts on
/// ActualThemeChanged AND UISettings.ColorValuesChanged) must drop cached entries
/// so a brush that changed for the same (key,theme) — e.g. an accent/high-contrast
/// change that doesn't flip Light/Dark — re-resolves.
/// </summary>
internal static class ThemeBrushCacheFixtures
{
    private const string MissingKey = "ReactorTest_NotYetMergedBrush";
    private const string SwapKey = "ReactorTest_InvalidatableBrush";

    internal class NullMissNotCached(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.Yield();
            var resources = Application.Current.Resources;
            ThemeRef.InvalidateResolutionCache();

            // 1. Resolve before the key exists → null. Must NOT be cached.
            var before = ThemeRef.Resolve(MissingKey, isDark: false);
            H.Check("BrushCache_MissingKey_IsNull", before is null);

            var dict = new ResourceDictionary();
            var brush = new SolidColorBrush(Microsoft.UI.Colors.Red);
            dict[MissingKey] = brush;
            resources.MergedDictionaries.Add(dict);
            try
            {
                // 2. Now that the dictionary is merged it must resolve — a cached
                //    null would poison this permanently (the H2 regression).
                var after = ThemeRef.Resolve(MissingKey, isDark: false);
                H.Check("BrushCache_NullNotCached_ResolvesAfterMerge", ReferenceEquals(after, brush));
            }
            finally
            {
                resources.MergedDictionaries.Remove(dict);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }

    internal class InvalidationReResolves(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Task.Yield();
            var resources = Application.Current.Resources;
            ThemeRef.InvalidateResolutionCache();

            var dictA = new ResourceDictionary();
            var brushA = new SolidColorBrush(Microsoft.UI.Colors.Red);
            dictA[SwapKey] = brushA;
            resources.MergedDictionaries.Add(dictA);
            ResourceDictionary? dictB = null;
            try
            {
                var first = ThemeRef.Resolve(SwapKey, isDark: false);
                H.Check("BrushCache_FirstResolve_BrushA", ReferenceEquals(first, brushA));

                // Swap the brush for the same key+theme (mimics an accent/palette
                // change that keeps the effective theme name).
                resources.MergedDictionaries.Remove(dictA);
                dictB = new ResourceDictionary();
                var brushB = new SolidColorBrush(Microsoft.UI.Colors.Blue);
                dictB[SwapKey] = brushB;
                resources.MergedDictionaries.Add(dictB);

                // Without invalidation the cache still returns brushA.
                var stale = ThemeRef.Resolve(SwapKey, isDark: false);
                H.Check("BrushCache_StaleBeforeInvalidate", ReferenceEquals(stale, brushA));

                // InvalidateResolutionCache (what the hosts call on
                // ColorValuesChanged) re-resolves to brushB.
                ThemeRef.InvalidateResolutionCache();
                var fresh = ThemeRef.Resolve(SwapKey, isDark: false);
                H.Check("BrushCache_FreshAfterInvalidate_BrushB", ReferenceEquals(fresh, brushB));
            }
            finally
            {
                if (dictB is not null) resources.MergedDictionaries.Remove(dictB);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }
}
