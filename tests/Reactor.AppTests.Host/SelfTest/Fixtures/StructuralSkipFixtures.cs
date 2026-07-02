using System.Threading;
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
///     reference-equal range of <c>{ThemeResource}</c>-themed cells under a parent
///     <c>RequestedTheme</c> toggle: every themed cell must render and its Foreground
///     must re-resolve Light↔Dark, with no cell dropped by the fast-path-eligible range.
///
/// <para>NOTE (updated for #758): since ThemeBindings were dropped from the
/// theme-sensitivity gate, these <c>{ThemeResource}</c> cells are NO LONGER
/// theme-sensitive, so the structural fast path now ENGAGES over them (rather than
/// being gated off) — and they still re-theme, because WinUI auto-re-resolves a
/// <c>{ThemeResource}</c> Style setter on any effective-theme change. That native
/// self-heal is exactly why the gate could be narrowed; the deterministic
/// skip-plus-self-heal proof (with a <c>DebugElementsDiffed</c> discriminator) lives in
/// <c>ThemeBindingsSkipSelfHealFixtures</c>. The one snapshot a skip truly leaves
/// stale — <c>ApplyResourceOverrides</c>' concrete <c>ThemeRef.Resolve</c> into
/// <c>fe.Resources[key]</c> — is the arm the gate KEEPS (see <c>Issue675…</c> +
/// <c>ChildReconcilerStructuralSkipTests</c>).</para>
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
    /// THEME RANGE PARITY (live smoke): a reference-equal range of cells (each carries a
    /// <c>{ThemeResource}</c> Foreground via <c>.Foreground(Theme.PrimaryText)</c>) sits
    /// under a parent whose <c>RequestedTheme</c> is toggled Light↔Dark. Since #758 these
    /// ThemeBindings cells are NOT theme-sensitive, so the producer flags
    /// <c>AnyThemeSensitive=false</c> and the structural fast path ENGAGES (skips the
    /// untouched range); this fixture proves the end-to-end scenario is healthy — every
    /// themed cell renders, none is dropped across the toggle, and each cell's Foreground
    /// brush re-resolves to the new effective theme even though it was structurally skipped.
    ///
    /// <para>NOT THE GATE TEETH (by construction it cannot be): WinUI auto-re-resolves a
    /// <c>{ThemeResource}</c> Style setter on any effective-theme change, so the Foreground
    /// brush flips Light↔Dark whether the cell is re-applied OR structurally skipped. Its
    /// value is catching a fast path that DROPS or CORRUPTS themed cells. The authoritative
    /// deterministic teeth are headless
    /// (<c>ElementTests.CanSkipUpdate_ThemeBindingsOnly_NowSkips_SelfHealing</c>,
    /// <c>ChildDiffHintsTests.IsThemeSensitive_False_For_ThemeBindings_SelfHealing</c>,
    /// <c>UseMemoCellsTests.ByIndex_Reuse_ThemeBindings_Cells_Are_Not_ThemeSensitive</c> —
    /// each reverts red if the arm is restored); the deterministic live skip-plus-self-heal
    /// proof is <c>ThemeBindingsSkipSelfHealFixtures</c>.</para>
    /// </summary>
    internal class ThemeRangeParity(Harness h) : SelfTestFixtureBase(h)
    {
        // Reference-stable items: changedIndices is ALWAYS empty, so every cell is reused
        // reference-equal across the theme toggle (the untouched-range case the fast path
        // targets — since #758 the ThemeBindings cells are non-sensitive, so the fast path
        // engages and structurally skips them; they self-heal via {ThemeResource}).
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

    /// <summary>
    /// HOT-RELOAD GATE TEETH (C1, Spec 034 §C): a memoized range built by
    /// <c>UseMemoCellsByIndex</c> contains a WRAPPER cell (a <c>Component</c>) whose
    /// rendered body is changed by a simulated hot-reload "edit" (a flipped static
    /// shape flag, exactly as <see cref="HotReloadRecoveryFixtures"/> does). Across a
    /// normal render the wrapper is reused reference-equal and correctly skipped; but
    /// during a hot-reload FORCE pass the structural fast path MUST defer to the full
    /// walk so the wrapper re-renders its edited body (the full walk honours
    /// <c>ForceRenderThroughWrapper</c> per cell, which a wholesale structural skip
    /// would bypass).
    ///
    /// <para>This is the teeth for the fast path's <c>!reconciler.ForceFullRenderActive</c>
    /// gate (<c>ChildReconciler.ReconcilePositional</c>): the memoized cells are reused
    /// reference-equal with EMPTY <c>changedIndices</c>, so every other gate is satisfied
    /// during the force pass and only this gate keeps the fast path from engaging. Revert
    /// the gate → the force pass structurally skips the untouched wrapper, the edited body
    /// is swallowed, and <c>HotReloadStructuralSkip_WrapperReRenders</c> FAILS. A pure
    /// hot-reload force does not mark any node self-triggered, so the dirty-ancestor-path
    /// gate alone does NOT cover this case — hence the dedicated gate + this teeth.</para>
    /// </summary>
    internal sealed class HotReloadWrapperReRender(Harness h) : SelfTestFixtureBase(h)
    {
        // Drives the memoized wrapper cell's body. 0 = pre-edit, 1 = post-edit.
        private static int _cellShape;

        // A WRAPPER cell (Component) whose body a hot-reload edit changes. Only a
        // wrapper re-render is at risk from a structural skip during a force pass;
        // plain elements are safe to skip because their fields are unchanged.
        private sealed class MemoizedCellComponent : Component
        {
            public override Element Render() =>
                TextBlock(Volatile.Read(ref _cellShape) == 0 ? "wrapCell: v1" : "wrapCell: v2");
        }

        public override async Task RunAsync()
        {
            Volatile.Write(ref _cellShape, 0);

            var items = new[] { 0, 1, 2 };
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                // changedIndices is ALWAYS empty: every cell is reused reference-equal
                // across renders, so the producer publishes a hint with no changed
                // indices and the structural fast path is otherwise eligible — it must
                // be gated off during the hot-reload force pass so the wrapper at index
                // 1 re-renders its edited body.
                var cells = ctx.UseMemoCellsByIndex(
                    items,
                    new int[0],
                    (item, i) => i == 1
                        ? Component<MemoizedCellComponent>()   // wrapper cell
                        : TextBlock($"plainCell-{i}"));        // plain cells around it
                return VStack(cells);
            });

            await Harness.Render();
            H.Check("HotReloadStructuralSkip_MountV1", H.FindText("wrapCell: v1") is not null);

            // Simulate the developer's edit to the wrapper cell's body.
            Volatile.Write(ref _cellShape, 1);

            // Drive a real hot-reload force pass. The memoized cells are still reused
            // reference-equal (changedIndices empty), so WITHOUT the gate the fast path
            // would skip the untouched wrapper and swallow the edit.
            HotReloadService.UpdateApplication(null);
            host.RequestRender(force: true);
            await Harness.Render();

            // The wrapper re-rendered its edited body. Revert the
            // !ForceFullRenderActive gate and this stays "wrapCell: v1" (the structural
            // skip swallowed the edit) → the teeth fails.
            H.Check("HotReloadStructuralSkip_WrapperReRenders", H.FindText("wrapCell: v2") is not null);
            H.Check("HotReloadStructuralSkip_OldBodyGone", H.FindText("wrapCell: v1") is null);

            Volatile.Write(ref _cellShape, 0);
        }
    }
}
