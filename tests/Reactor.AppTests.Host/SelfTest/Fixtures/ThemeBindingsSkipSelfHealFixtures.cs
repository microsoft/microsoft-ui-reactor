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
/// (<c>.Foreground(Theme.X)</c> → a <c>{ThemeResource}</c> Style setter) whose
/// <c>Update</c> is NEVER entered (child-level structural skip → <c>ApplyThemeBindings</c>
/// is NOT re-applied) STILL re-themes when its effective theme changes — because WinUI
/// re-resolves the live <c>{ThemeResource}</c> NATIVELY on the control's
/// <c>ActualTheme</c> change. If that self-healing did NOT hold, the gate arm would be
/// load-bearing and MUST be kept — so this fixture is the STOP gate.</para>
///
/// <para>THE DISCRIMINATOR IS <c>DebugElementsDiffed</c>, NOT <c>DebugElementsSkipped</c>.
/// A colour delta alone is NOT teeth: even with the arm RESTORED the memoized child is
/// routed through <c>Update</c> and takes the <b>element-level shallow-skip</b> arm, which
/// re-applies <c>ApplyThemeBindings</c> — so the brush flips either way.
/// <c>DebugElementsSkipped</c> is ALSO ambiguous: it increments on BOTH the child-level
/// skip (this fix — <c>Update</c> never entered) AND the element-level shallow-skip
/// (arm restored — <c>Update</c> entered, then skips), so it is >= 1 in both cases.
/// Only <c>DebugElementsDiffed</c> (incremented once per <c>Update</c> ENTRY) distinguishes
/// them: with the arm dropped the child is child-skipped and NOT diffed; with the arm
/// restored the child is routed through <c>Update</c> and IS diffed (+1). So the exact
/// <c>DebugElementsDiffed</c> assertion below goes RED if the arm is restored — proving the
/// child's <c>Update</c> genuinely never ran and the re-theme is pure WinUI self-heal.
/// (Empirically verified both directions while authoring: immediate diffed 1→2, inherited
/// diffed 2→3 when the arm is restored.) <c>ReferenceEquals</c> on the control pins that
/// the same WinUI instance survived (not a remount masquerading as a skip).</para>
///
/// <para>The effective-theme change is driven by an ANCESTOR <c>RequestedTheme</c> toggle —
/// the RISKY per-element case (an app-level system-theme toggle is not reproducible in a
/// live selftest; an ancestor <c>RequestedTheme</c> flips the child's <c>ActualTheme</c>
/// identically and is the established proxy across the theme fixtures). Covered at TWO
/// depths: the immediate parent, and a higher grandparent through a themeless intermediate.</para>
/// </summary>
internal static class ThemeBindingsSkipSelfHealFixtures
{
    private static TextBlock? FindChild(Harness h, string text) =>
        h.FindControl<TextBlock>(t => t.Text == text);

    private static global::Windows.UI.Color? ForegroundColor(TextBlock? tb) =>
        (tb?.Foreground as SolidColorBrush)?.Color;

    private static string Fmt(global::Windows.UI.Color? c) =>
        c is null ? "null" : $"{c.Value.R:X2}{c.Value.G:X2}{c.Value.B:X2}";

    // ════════════════════════════════════════════════════════════════════
    //  Immediate-parent RequestedTheme toggle over a child-skipped ThemeBindings leaf
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The themed child sits directly under a container whose <c>RequestedTheme</c> is
    /// toggled. The child is memoized (reference-equal) so on the toggle render it is
    /// child-level skipped (<c>Update</c> never entered → only the container is diffed,
    /// <c>DebugElementsDiffed == 1</c>); its <c>{ThemeResource}</c> Foreground must still
    /// re-resolve, and the control instance must be preserved.
    /// </summary>
    internal sealed class ImmediateAncestorToggleSelfHeals(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            Action? toggle = null;
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

            var ctrlBefore = FindChild(H, "skipImmediate");
            H.Check("SkipImmediate_ChildPresent", ctrlBefore is not null);
            var darkColor = ForegroundColor(ctrlBefore);
            H.Check($"SkipImmediate_MountThemed[{Fmt(darkColor)}]", darkColor is not null);

            // Toggle the ancestor theme Dark→Light.
            toggle!();
            await Harness.Render();

            var diffed = host.Reconciler.DebugElementsDiffed;
            var ctrlAfter = FindChild(H, "skipImmediate");

            // TEETH: exactly the container was diffed; the themed child's Update was NEVER
            // entered (child-level skip → ApplyThemeBindings NOT re-run). RED if the arm is
            // restored (child routed through Update → diffed == 2).
            H.Check($"SkipImmediate_ChildNotDiffed[diffed={diffed}]", diffed == 1);
            // The same WinUI control instance survived the skip (not a remount).
            H.Check("SkipImmediate_ControlPreserved",
                ctrlAfter is not null && ReferenceEquals(ctrlBefore, ctrlAfter));

            // THE CRUX: the {ThemeResource} Foreground re-resolved despite the child being
            // skipped and ApplyThemeBindings never re-running — pure native self-heal.
            var reresolved = await Harness.WaitFor(() =>
            {
                var c = ForegroundColor(FindChild(H, "skipImmediate"));
                return c is not null && c != darkColor;
            });
            var lightColor = ForegroundColor(FindChild(H, "skipImmediate"));
            H.Check($"SkipImmediate_SelfHealedOnSkip[{Fmt(darkColor)}->{Fmt(lightColor)}]", reresolved);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Inherited (grandparent) RequestedTheme toggle through a themeless intermediate
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The <c>RequestedTheme</c> toggle lives on a grandparent; an intermediate container
    /// declares no theme of its own. The memoized themed leaf must still be child-level
    /// skipped (<c>Update</c> never entered — only the two containers are diffed,
    /// <c>DebugElementsDiffed == 2</c>) and re-resolve as the effective theme inherits down,
    /// proving self-healing tracks the inherited <c>ActualTheme</c>, not just a directly-set one.
    /// </summary>
    internal sealed class InheritedAncestorToggleSelfHeals(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            Action? toggle = null;
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

            var ctrlBefore = FindChild(H, "skipInherited");
            H.Check("SkipInherited_ChildPresent", ctrlBefore is not null);
            var lightColor = ForegroundColor(ctrlBefore);
            H.Check($"SkipInherited_MountThemed[{Fmt(lightColor)}]", lightColor is not null);

            toggle!();
            await Harness.Render();

            var diffed = host.Reconciler.DebugElementsDiffed;
            var ctrlAfter = FindChild(H, "skipInherited");

            // TEETH: the two containers (grandparent + intermediate) were diffed; the themed
            // child's Update was NEVER entered. RED if the arm is restored (diffed == 3).
            H.Check($"SkipInherited_ChildNotDiffed[diffed={diffed}]", diffed == 2);
            H.Check("SkipInherited_ControlPreserved",
                ctrlAfter is not null && ReferenceEquals(ctrlBefore, ctrlAfter));

            var reresolved = await Harness.WaitFor(() =>
            {
                var c = ForegroundColor(FindChild(H, "skipInherited"));
                return c is not null && c != lightColor;
            });
            var darkColor = ForegroundColor(FindChild(H, "skipInherited"));
            H.Check($"SkipInherited_SelfHealedOnSkip[{Fmt(lightColor)}->{Fmt(darkColor)}]", reresolved);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Whole-children-list skip via the UseMemoCells array-reuse early-out
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// #758 H1 — proves the OTHER narrowed arm: <c>ChildDiffHints.IsThemeSensitive</c> →
    /// <c>AnyThemeSensitive</c> → the <c>UseMemoCells</c> full-cache-hit ARRAY-REUSE
    /// early-out → the container's <c>ShallowEquals(Children)</c> whole-subtree skip. A
    /// <c>UseMemoCells</c> list of <c>ThemeBindings</c> cells, unchanged across a render,
    /// now returns the SAME array reference (ThemeBindings are no longer theme-sensitive),
    /// so the inner container holding it is <c>ShallowEquals</c> and is child-level skipped
    /// WHOLE — <c>Update</c> is entered for NEITHER the inner container NOR any cell.
    /// Under an ancestor <c>RequestedTheme</c> toggle every cell's <c>{ThemeResource}</c>
    /// Foreground must still re-resolve.
    ///
    /// <para>DISCRIMINATOR: with the arm dropped, only the outer (theme-changed) container
    /// is diffed (<c>DebugElementsDiffed == 1</c>). With the arm RESTORED the cells become
    /// theme-sensitive → <c>UseMemoCells</c> returns a FRESH array → the inner container is
    /// no longer <c>ShallowEquals</c> → it AND all N cells route through <c>Update</c>
    /// (<c>diffed</c> jumps to 1 + 1 + N). So the exact <c>diffed == 1</c> assertion is RED
    /// with the arm restored — proving the whole list was genuinely skipped.</para>
    /// </summary>
    internal sealed class WholeListEarlyOutToggleSelfHeals(Harness h) : SelfTestFixtureBase(h)
    {
        private static readonly int[] Items = { 0, 1, 2 };

        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            Action? toggle = null;
            host.Mount(ctx =>
            {
                var (theme, setTheme) = ctx.UseState(ElementTheme.Dark);
                toggle = () => setTheme(theme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark);
                // ThemeBindings cells + constant items/deps → full value-equal cache hit.
                // Since #758 they are NOT theme-sensitive, so the early-out reuses the SAME
                // array reference → the inner VStack(cells) is ShallowEquals-skippable.
                var cells = ctx.UseMemoCells<int>(
                    Items,
                    (item, i) => TextBlock($"wl-{i}").Foreground(Theme.PrimaryText),
                    "static-deps");
                // ancestor(RequestedTheme) → inner VStack(cells): the toggle changes the
                // ancestor only, so the inner container's skip is decided purely by whether
                // the cells array reused (the arm under test).
                return VStack(VStack(cells)).RequestedTheme(theme);
            });

            await Harness.Render();

            var ctrlBefore = FindChild(H, "wl-1");
            H.Check("WholeList_CellsPresent",
                FindChild(H, "wl-0") is not null && ctrlBefore is not null && FindChild(H, "wl-2") is not null);
            var darkColor = ForegroundColor(ctrlBefore);
            H.Check($"WholeList_MountThemed[{Fmt(darkColor)}]", darkColor is not null);

            toggle!();
            await Harness.Render();

            var diffed = host.Reconciler.DebugElementsDiffed;
            var ctrlAfter = FindChild(H, "wl-1");

            // TEETH: only the outer theme-changed container was diffed; the inner container
            // and all cells were skipped WHOLE (array-reuse early-out). RED if the arm is
            // restored (fresh array → inner container + N cells diffed).
            H.Check($"WholeList_SubtreeSkippedWhole[diffed={diffed}]", diffed == 1);
            H.Check("WholeList_CellControlPreserved",
                ctrlAfter is not null && ReferenceEquals(ctrlBefore, ctrlAfter));

            // THE CRUX: each skipped cell's {ThemeResource} Foreground re-resolves natively.
            var reresolved = await Harness.WaitFor(() =>
            {
                var c = ForegroundColor(FindChild(H, "wl-1"));
                return c is not null && c != darkColor;
            });
            var lightColor = ForegroundColor(FindChild(H, "wl-1"));
            var allPresent = FindChild(H, "wl-0") is not null && FindChild(H, "wl-1") is not null && FindChild(H, "wl-2") is not null;
            H.Check("WholeList_AllCellsSurviveToggle", allPresent);
            H.Check($"WholeList_SelfHealedOnSkip[{Fmt(darkColor)}->{Fmt(lightColor)}]", reresolved);
        }
    }
}
