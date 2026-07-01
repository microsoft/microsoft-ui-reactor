using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #659 / #758 — end-to-end guard for the base <c>UseMemoCells</c>
/// full-cache-hit early-out vs theme propagation. A base <c>UseMemoCells</c> list of
/// <c>ThemeBindings</c> cells sits in a container under an ancestor
/// <c>RequestedTheme</c> toggle, driven through a full value-equal cache hit
/// (items + deps unchanged), then the theme is toggled and the cell's themed
/// Foreground must re-resolve to the new theme's brush.
///
/// EMPIRICAL FINDING (verified WITH and WITHOUT the <c>AnyThemeSensitive</c> gate,
/// and the basis for narrowing it in #758): the themed Foreground re-resolves
/// identically in both cases (000000 Light -> FFFFFF Dark). A <c>.Foreground(Theme.X)</c>
/// binding compiles to a <c>{ThemeResource}</c> Style setter which WinUI re-resolves
/// NATIVELY on the cell's effective-theme change — independent of whether Reactor
/// recurses into the cell or the container <c>ShallowEquals</c>-skips it. Since #758,
/// ThemeBindings are NO LONGER theme-sensitive, so this list now takes the array-reuse
/// early-out and the container ACTUALLY structural-skips the cells on the toggle — this
/// fixture therefore now exercises (not just anticipates) the skipped-child self-heal
/// path, and the Foreground still re-resolves. (The deterministic skip-plus-self-heal
/// proof with a DebugElementsSkipped assertion is in <c>ThemeBindingsSkipSelfHealFixtures</c>.)
///
/// Note: a <c>ResourceOverride</c> ThemeRef resolved to a CONCRETE brush does NOT
/// self-heal on this toggle — it is the arm KEPT by #758 (and, resolved correctly at
/// mount against the ancestor theme since #771).
/// </summary>
internal static class UseMemoCellsThemeReResolveFixtures
{
    private static readonly int[] Items = { 0, 1, 2 };

    internal class BaseMemoCellsReThemeOnAncestorToggle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (theme, setTheme) = ctx.UseState(ElementTheme.Light);

                // Theme-sensitive cells, no theme dep + constant items => a full
                // value-equal cache hit on the toggle (the early-out is eligible;
                // the AnyThemeSensitive gate makes it fall through to a fresh array).
                var cells = ctx.UseMemoCells<int>(
                    Items,
                    (item, i) => TextBlock($"memoCell-{i}").Foreground(Theme.PrimaryText),
                    "static-deps");

                return VStack(
                    Button("ToggleMemoTheme", () =>
                        setTheme(theme == ElementTheme.Light ? ElementTheme.Dark : ElementTheme.Light)),
                    VStack(cells)
                ).RequestedTheme(theme);
            });

            await Harness.Render();

            H.Check("MemoReTheme_AllCellsPresent", AllCellsPresent());
            var lightColor = ForegroundColor("memoCell-1");
            H.Check($"MemoReTheme_MountThemed[{Fmt(lightColor)}]", lightColor is not null);

            H.ClickButton("ToggleMemoTheme");
            var reresolved = await Harness.WaitFor(() =>
            {
                var c = ForegroundColor("memoCell-1");
                return c is not null && c != lightColor;
            });

            H.Check("MemoReTheme_AllCellsSurviveToggle", AllCellsPresent());
            var darkColor = ForegroundColor("memoCell-1");
            H.Check($"MemoReTheme_ForegroundReResolvedOnToggle[{Fmt(lightColor)}->{Fmt(darkColor)}]", reresolved);
        }

        private static string Fmt(global::Windows.UI.Color? c) =>
            c is null ? "null" : $"{c.Value.R:X2}{c.Value.G:X2}{c.Value.B:X2}";

        private bool AllCellsPresent()
        {
            for (int i = 0; i < Items.Length; i++)
                if (FindCell($"memoCell-{i}") is null)
                    return false;
            return true;
        }

        private TextBlock? FindCell(string content) =>
            H.FindControl<TextBlock>(t => t.Text == content);

        private global::Windows.UI.Color? ForegroundColor(string content) =>
            (FindCell(content)?.Foreground as SolidColorBrush)?.Color;
    }
}
