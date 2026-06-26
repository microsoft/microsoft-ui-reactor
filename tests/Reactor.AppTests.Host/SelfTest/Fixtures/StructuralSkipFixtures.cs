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
/// PR-C (Spec 034 §C) end-to-end proofs through the REAL reconciler for the
/// positional structural-skip fast path in <c>ChildReconciler.ReconcilePositional</c>.
///
/// A memoizing producer (<c>UseMemoCellsByIndex</c>) publishes a
/// <c>ChildDiffHint</c> keyed on the fresh-per-render <c>Element[]</c>; the
/// consumer then updates ONLY the changed indices and skips the rest (provably
/// reference-equal). These fixtures mount a live WinUI tree so the actual hint
/// production + fast-path consumption are exercised, not just the headless
/// primitives in <c>ChildReconcilerStructuralSkipTests</c> /
/// <c>ChildDiffHintsTests</c>.
///
/// Two complementary live observables, each matched to what the path actually does:
///   • <see cref="LifecycleParity"/> uses an <c>OnUpdateAdd</c> tally. A CHANGED
///     cell (ShallowEquals false) takes the full <c>UpdateChild</c> → <c>ApplyModifiers</c>
///     path, so <c>OnUpdateAction</c> fires; an untouched reference-equal cell takes
///     the skip arm and never fires it — identical under the fast path and the full
///     walk. This pins the changed-index ⇒ fires / untouched ⇒ skipped semantics.
///   • <see cref="ThemeRangeParity"/> is a live PARITY / SMOKE check over a
///     theme-sensitive reference-equal range under a parent <c>RequestedTheme</c>
///     toggle: every themed cell must render and its <c>{ThemeResource}</c> Foreground
///     must re-resolve Light↔Dark, with no cell dropped by the fast-path-eligible
///     range. It is deliberately NOT the gate teeth — see its remarks.
///
/// <para>WHY THE GATE TEETH IS HEADLESS, NOT HERE: the load-bearing teeth for the
/// <c>!AnyThemeSensitive</c> gate is the headless
/// <c>ChildReconcilerStructuralSkipTests.ThemeSensitive_Hint_Forces_Full_Walk</c>
/// (revert the gate → it FAILS). A live color delta CANNOT witness the gate because
/// WinUI auto-re-resolves a <c>{ThemeResource}</c> Style setter on any effective-theme
/// change — empirically, with the gate reverted the themed cells were structurally
/// skipped (ApplyThemeBindings never re-ran) yet their Foreground brushes STILL went
/// Light→Dark. The one snapshot a skip truly leaves stale —
/// <c>ApplyResourceOverrides</c>' concrete <c>ThemeRef.Resolve</c> into
/// <c>fe.Resources[key]</c> — does not reliably re-resolve in the reconcile harness
/// (its effective-theme view lags a parent RequestedTheme propagation), so it makes
/// an unreliable live observable. The headless visited-index assertion is therefore
/// the authoritative gate teeth; this fixture is the end-to-end parity companion.</para>
/// </summary>
internal static class StructuralSkipFixtures
{
    /// <summary>
    /// Lifecycle PARITY: an <c>OnUpdateAction</c>-bearing cell sits at BOTH a
    /// changed index AND untouched reference-equal indices under the hinted
    /// (fast-path-eligible) range. The engaged fast path must fire the update
    /// callback for the changed cell and leave the untouched cells untouched —
    /// identical to the full O(count) walk (whose ref-equal cells also hit the
    /// CanSkipUpdate skip arm and never fire OnUpdateAction).
    /// </summary>
    internal class LifecycleParity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Reference-stable tally the cells write to (no static mutable state):
            // index i counts how many times cell i actually went through Update.
            var counts = new int[5];

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (vals, setVals) = ctx.UseState(new[] { 0, 0, 0, 0, 0 });
                var (changed, setChanged) = ctx.UseState(new int[0]);

                // Plain (non-theme) cells. For i not in changedIndices the hook
                // reuses the previous element reference verbatim, so the consumer's
                // fast path can skip it; only changed indices are rebuilt.
                var cells = ctx.UseMemoCellsByIndex(
                    vals,
                    changed,
                    (item, i) => TextBlock($"cellL-{i}:{item}").OnUpdateAdd(_ => counts[i]++));

                return VStack(
                    Button("BumpL2", () =>
                    {
                        var next = (int[])vals.Clone();
                        next[2]++;
                        setChanged(new[] { 2 });
                        setVals(next);
                    }),
                    VStack(cells));
            });

            await Harness.Render();
            // OnUpdateAction is update-only — it must NEVER fire at mount.
            H.Check("StructuralSkip_NoUpdateAtMount",
                counts[0] == 0 && counts[2] == 0 && counts[4] == 0);
            H.Check("StructuralSkip_MountTextPresent", H.FindText("cellL-2:0") is not null);

            // Bump exactly one cell (index 2): changedIndices=[2] → cell 2 is rebuilt
            // (fresh instance, new text) while cells 0/1/3/4 are reused reference-equal,
            // so the fast path engages (no theme cells, container not on the dirty path).
            H.ClickButton("BumpL2");
            await Harness.Render();

            // The changed index goes through the real Update path → its tally fires.
            H.Check("StructuralSkip_ChangedCellUpdated", counts[2] >= 1);
            H.Check("StructuralSkip_ChangedTextUpdated", H.FindText("cellL-2:1") is not null);
            // Untouched reference-equal indices are skipped wholesale → no tally fires,
            // exactly as the full walk would (ref-equal cells hit CanSkipUpdate).
            H.Check("StructuralSkip_UntouchedHeadNotUpdated", counts[0] == 0);
            H.Check("StructuralSkip_UntouchedTailNotUpdated", counts[4] == 0);
            H.Check("StructuralSkip_UntouchedTextUnchanged", H.FindText("cellL-0:0") is not null);
        }
    }

    /// <summary>
    /// THEME RANGE PARITY (live smoke): a reference-equal range of theme-sensitive cells
    /// (each carries a <c>{ThemeResource}</c> Foreground via <c>.Foreground(Theme.PrimaryText)</c>)
    /// sits under a parent whose <c>RequestedTheme</c> is toggled Light↔Dark. Because the
    /// producer flags <c>AnyThemeSensitive=true</c>, the structural fast path is gated off
    /// (gate 5) and the full walk runs; this fixture proves the end-to-end scenario is
    /// healthy — every themed cell renders, none is dropped across the toggle, and each
    /// cell's Foreground brush re-resolves to the new effective theme.
    ///
    /// <para>NOT THE GATE TEETH (by construction it cannot be): WinUI auto-re-resolves a
    /// <c>{ThemeResource}</c> Style setter on any effective-theme change, so the Foreground
    /// brush flips Light↔Dark whether the cell is re-applied OR structurally skipped — this
    /// check passes under both the correct gate and a reverted one. Its value is catching a
    /// fast path that DROPS or CORRUPTS themed cells, not detecting an over-eager skip. The
    /// authoritative teeth for the <c>!AnyThemeSensitive</c> gate is the deterministic
    /// headless visited-index assertion
    /// <c>ChildReconcilerStructuralSkipTests.ThemeSensitive_Hint_Forces_Full_Walk</c>
    /// (revert the gate → that test FAILS). See the class remarks for the empirical basis.</para>
    /// </summary>
    internal class ThemeRangeParity(Harness h) : SelfTestFixtureBase(h)
    {
        // Reference-stable items: changedIndices is ALWAYS empty, so every cell is reused
        // reference-equal across the theme toggle (the untouched-range case the fast path
        // targets — here defeated by AnyThemeSensitive, exercising the gated full walk).
        private static readonly int[] Items = { 0, 1, 2, 3, 4 };

        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (theme, setTheme) = ctx.UseState(ElementTheme.Light);

                // Each cell carries a {ThemeResource} Foreground → the range is
                // theme-sensitive (hint.AnyThemeSensitive = true). The cells take no theme
                // dependency, so changedIndices stays empty and every cell is reused
                // reference-equal across the toggle.
                var cells = ctx.UseMemoCellsByIndex(
                    Items,
                    new int[0],
                    (item, i) => TextBlock($"cellT-{i}").Foreground(Theme.PrimaryText));

                return VStack(
                    Button("ToggleThemeT", () =>
                        setTheme(theme == ElementTheme.Light ? ElementTheme.Dark : ElementTheme.Light)),
                    // The parent RequestedTheme toggle changes the effective theme for the
                    // subtree WITHOUT touching the (reference-equal) cell elements. The inner
                    // VStack still updates (fresh children array each render), so child
                    // reconciliation runs over the theme-sensitive range.
                    VStack(cells).RequestedTheme(theme));
            });

            await Harness.Render();

            // All themed cells render and their Foreground resolves at mount (Light theme).
            H.Check("ThemeRange_AllCellsPresentAtMount", AllCellsPresent());
            var lightColor = CellForeground("cellT-2")?.Color;
            H.Check("ThemeRange_MountForegroundThemed", lightColor is not null);

            // Toggle the parent RequestedTheme Light→Dark. WinUI re-resolves the live
            // {ThemeResource} Foreground for the whole subtree; the cells must survive the
            // fast-path-eligible range and re-theme.
            H.ClickButton("ToggleThemeT");
            var reresolved = await Harness.WaitFor(() =>
            {
                var b = CellForeground("cellT-2");
                return b is not null && b.Color != lightColor;
            });

            H.Check("ThemeRange_AllCellsSurviveToggle", AllCellsPresent());
            H.Check("ThemeRange_ForegroundReresolvedOnToggle", reresolved);
        }

        private bool AllCellsPresent()
        {
            for (int i = 0; i < Items.Length; i++)
                if (H.FindText($"cellT-{i}") is null)
                    return false;
            return true;
        }

        private SolidColorBrush? CellForeground(string text)
        {
            var tb = H.FindControl<TextBlock>(t => t.Text == text);
            return tb?.Foreground as SolidColorBrush;
        }
    }
}
