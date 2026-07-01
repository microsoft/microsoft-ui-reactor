using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// #758 CRUX — the make-or-break safety proof for narrowing the theme-sensitivity gate
/// (dropping the <c>ThemeBindings</c> arm from <c>Element.CanSkipUpdate</c> and
/// <c>ChildDiffHints.IsThemeSensitive</c> while KEEPING the
/// <c>ResourceOverrides.ThemeRefs</c> arm).
///
/// <para>THE CLAIM UNDER TEST: a <c>ThemeBindings</c> child
/// (<c>.Foreground(Theme.X)</c> → a <c>{ThemeResource}</c> Style setter) that is
/// STRUCTURALLY SKIPPED by the reconciler (its <c>Update</c> is never called, so
/// <c>ApplyThemeBindings</c> is NOT re-applied) STILL re-themes when its effective
/// theme changes — because WinUI re-resolves the live <c>{ThemeResource}</c> NATIVELY
/// on the control's <c>ActualTheme</c> change. If that self-healing did NOT hold, the
/// gate arm would be load-bearing and MUST be kept — so this fixture is the STOP gate.</para>
///
/// <para>AIRTIGHT DESIGN:
/// <list type="bullet">
/// <item>The themed child is <c>UseMemo</c>-stabilized to a single reference-equal
/// instance across renders, so the reconciler's positional child arm takes the
/// <c>CanSkipUpdate</c> child-level skip (<c>Update</c> is NEVER entered for it —
/// distinct from the element-level shallow-skip inside <c>Update</c>, which WOULD
/// re-apply ThemeBindings). The companion headless
/// <c>ElementCanSkipUpdateThemeTests</c> proves <c>CanSkipUpdate</c> returns true for
/// exactly this shape.</item>
/// <item>Skip is VERIFIED live, not assumed: the tree's only reference-equal leaf is
/// the themed child, so <c>host.Reconciler.DebugElementsSkipped &gt;= 1</c> after the
/// toggle render attributes the skip to it (the ancestor container whose
/// <c>RequestedTheme</c> flips is NOT shallow-equal, so it updates rather than skips).</item>
/// <item>The effective-theme change is driven by an ANCESTOR <c>RequestedTheme</c>
/// toggle — the RISKY per-element case (an app-level system-theme toggle is not
/// reproducible in a live selftest; an ancestor <c>RequestedTheme</c> flips the child's
/// <c>ActualTheme</c> identically and is the established proxy used across the theme
/// fixtures). Covered at TWO depths: the immediate parent, and a higher grandparent
/// through a themeless intermediate (inheritance).</item>
/// <item>Concrete <c>{ThemeResource}</c> brushes (NOT self-healing ThemeRef resource
/// overrides), asserted by color delta — the brush only flips if WinUI re-resolves it.</item>
/// </list></para>
/// </summary>
internal static class ThemeBindingsSkipSelfHealFixtures
{
    private static global::Windows.UI.Color? ForegroundColor(Harness h, string text) =>
        (h.FindControl<TextBlock>(t => t.Text == text)?.Foreground as SolidColorBrush)?.Color;

    private static string Fmt(global::Windows.UI.Color? c) =>
        c is null ? "null" : $"{c.Value.R:X2}{c.Value.G:X2}{c.Value.B:X2}";

    // ════════════════════════════════════════════════════════════════════
    //  Immediate-parent RequestedTheme toggle over a child-skipped ThemeBindings leaf
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The themed child sits directly under a container whose <c>RequestedTheme</c> is
    /// toggled. The child is memoized (reference-equal) so it is child-level skipped on
    /// the toggle render; its <c>{ThemeResource}</c> Foreground must still re-resolve.
    /// </summary>
    internal sealed class ImmediateAncestorToggleSelfHeals(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ReactorHost? host = null;
            Action? toggle = null;
            host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (theme, setTheme) = ctx.UseState(ElementTheme.Dark);
                toggle = () => setTheme(theme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark);
                // Reference-stable themed leaf → child-level CanSkipUpdate skip on re-render.
                var child = ctx.UseMemo(
                    () => TextBlock("skipImmediate").Foreground(Theme.PrimaryText),
                    "static");
                // Only the container's RequestedTheme changes on toggle; the container is
                // therefore NOT shallow-equal (it updates), leaving the memoized child as
                // the sole reference-equal, skip-eligible element.
                return VStack(child).RequestedTheme(theme);
            });

            await Harness.Render();

            H.Check("SkipImmediate_ChildPresent", H.FindText("skipImmediate") is not null);
            var darkColor = ForegroundColor(H, "skipImmediate");
            H.Check($"SkipImmediate_MountThemed[{Fmt(darkColor)}]", darkColor is not null);

            // Toggle the ancestor theme Dark→Light. The memoized child is child-level
            // skipped (Update never called → ApplyThemeBindings NOT re-run); WinUI must
            // re-resolve the {ThemeResource} Foreground natively.
            toggle!();
            await Harness.Render();

            var skipped = host.Reconciler.DebugElementsSkipped;
            H.Check($"SkipImmediate_ChildWasSkipped[{skipped}]", skipped >= 1);

            var reresolved = await Harness.WaitFor(() =>
            {
                var c = ForegroundColor(H, "skipImmediate");
                return c is not null && c != darkColor;
            });
            var lightColor = ForegroundColor(H, "skipImmediate");
            // THE CRUX: brush re-resolved despite the child being skipped (self-healing).
            H.Check($"SkipImmediate_SelfHealedOnSkip[{Fmt(darkColor)}->{Fmt(lightColor)}]", reresolved);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Inherited (grandparent) RequestedTheme toggle through a themeless intermediate
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The <c>RequestedTheme</c> toggle lives on a grandparent; an intermediate
    /// container declares no theme of its own. The memoized themed leaf (child-level
    /// skipped) must still re-resolve as the effective theme inherits down — proving
    /// self-healing tracks the inherited <c>ActualTheme</c>, not just a directly-set one.
    /// </summary>
    internal sealed class InheritedAncestorToggleSelfHeals(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ReactorHost? host = null;
            Action? toggle = null;
            host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (theme, setTheme) = ctx.UseState(ElementTheme.Light);
                toggle = () => setTheme(theme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark);
                var child = ctx.UseMemo(
                    () => TextBlock("skipInherited").Foreground(Theme.PrimaryText),
                    "static");
                // grandparent(RequestedTheme) → intermediate(no theme) → memoized child.
                return VStack(VStack(child)).RequestedTheme(theme);
            });

            await Harness.Render();

            H.Check("SkipInherited_ChildPresent", H.FindText("skipInherited") is not null);
            var lightColor = ForegroundColor(H, "skipInherited");
            H.Check($"SkipInherited_MountThemed[{Fmt(lightColor)}]", lightColor is not null);

            toggle!();
            await Harness.Render();

            var skipped = host.Reconciler.DebugElementsSkipped;
            H.Check($"SkipInherited_ChildWasSkipped[{skipped}]", skipped >= 1);

            var reresolved = await Harness.WaitFor(() =>
            {
                var c = ForegroundColor(H, "skipInherited");
                return c is not null && c != lightColor;
            });
            var darkColor = ForegroundColor(H, "skipInherited");
            H.Check($"SkipInherited_SelfHealedOnSkip[{Fmt(lightColor)}->{Fmt(darkColor)}]", reresolved);
        }
    }
}
