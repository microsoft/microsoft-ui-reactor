using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #1090 — <c>ListView</c>/<c>GridView</c> swallow the user's first
/// genuine <c>SelectionChanged</c> after mounting with an <b>empty</b> items
/// list.
///
/// <para><b>Mechanism (confirmed by instrumented ledger against a live
/// framework-dependent app).</b> Echo suppression arms one token per
/// <em>expected</em> change event. A controlled <c>SelectedIndex</c> write is
/// gated on drift (<c>control != requested</c>), which is not sufficient: WinUI
/// silently <b>clamps</b> a selection past the end of its <c>ItemsSource</c>.
/// The property stays put, no <c>SelectionChanged</c> is raised, and the token
/// armed for that write strands.</para>
///
/// <para>The common trigger is an items array that is still empty on mount while
/// its data loads — idiomatic for <c>UseState&lt;T[]&gt;([])</c> plus a fetch.
/// Mount writes <c>SelectedIndex = 0</c> against an empty source (clamped,
/// token #1), an intermediate render writes it again while still empty (clamped,
/// token #2), then the items arrive and WinUI materializes the selection with a
/// single coalesced event that consumes only <em>one</em> token. The leftover
/// swallows the user's first real click. Observed ledger on the unfixed code:</para>
/// <code>
/// MOUNT  write: itemsCount=0 current=-1 -> 0
/// BEGIN  count=1
/// MOUNT  write done: readback=-1        // clamped — no event
/// UPDATE write: itemsCount=0 current=-1 -> 0
/// BEGIN  count=2
/// UPDATE write done: readback=-1        // clamped — no event
/// SHOULD consumed -> count=1            // items arrive: ONE coalesced event
/// ...                                   // counter never returns to 0
/// SHOULD consumed -> count=0            // the user's real click, swallowed
/// </code>
///
/// <para><b>The fix</b> is a reachability guard: only write (and therefore only
/// arm) when the requested index actually exists in the current source. A write
/// that can only clamp is skipped; the later update that brings the items in
/// performs it instead, where it lands and echoes normally.</para>
///
/// <para><b>The two failure modes any fix here trades off.</b>
/// <list type="bullet">
/// <item><description><b>Strand</b> — a token outlives the write it was armed
/// for and swallows a genuine selection. That is #1090.</description></item>
/// <item><description><b>Leak</b> — an engine-synthesized event reaches the user
/// callback, which calls <c>setIndex</c>, which re-renders and rebuilds
/// <c>ItemsSource</c> again: the #495 render storm. Guarded by
/// <c>SelectionDroppedByRebuild_NoEchoLeak</c> here and by the six existing
/// <c>Issue495_*</c> fixtures, which must stay green.</description></item>
/// </list>
/// Fixing one by reintroducing the other is not a fix.</para>
///
/// <para><b>Positive controls.</b> Every fixture that asserts "the callback did
/// NOT fire" first drives an interaction proving the callback path is live on
/// that control instance. A zero from a correctly-suppressed echo and a zero
/// from a callback that was never wired look identical in the log.</para>
/// </summary>
internal static class Issue1090SelectionStrandFixtures
{
    private static readonly Action _noOp = static () => { };

    private static Element[] MakeItems(int count, string tag)
    {
        var items = new Element[count];
        for (int i = 0; i < count; i++)
            items[i] = new TextBlockElement($"{tag}-item{i}");
        return items;
    }

    // ─────────────────────────────────────────────────────────────────────
    //  1. The bug: selection survives the rebuild → next real selection is
    //     swallowed by the stranded token.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Issue #1090 — grow the item array while the selected index stays
    /// valid, then make a genuine selection. The rebuild must not leave a
    /// suppression token behind that eats it.</summary>
    internal class ListView_SelectionSurvivesRebuild_NextSelectionFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            int lastIdx = -99;
            var el1 = new ListViewElement(MakeItems(2, "lv1090a"))
            {
                SelectedIndex = 0,
                OnSelectedIndexChanged = i => { fireCount++; lastIdx = i; },
            };

            if (rec.Mount(el1, _noOp) is not WinUI.ListView lv)
            {
                H.Check("Issue1090_LVSurvive_Mounted", false);
                return;
            }

            parent.Children.Add(lv);
            await Harness.Render();
            H.Check("Issue1090_LVSurvive_MountValue", lv.SelectedIndex == 0);

            // ListView's SelectionChanged is deferred to container realization, so
            // the mount-time SelectedIndex=0 write echoes once after the trampoline
            // subscribes. Pre-existing behavior; baseline-reset so the assertions
            // below measure only what the rebuild did.
            fireCount = 0;
            lastIdx = -99;

            // Positive control: the callback path is live on THIS control before
            // we assert anything about it not firing.
            lv.SelectedIndex = 1;
            await Harness.Render();
            H.Check("Issue1090_LVSurvive_CallbackWiredControl", fireCount == 1 && lastIdx == 1);
            fireCount = 0;
            lastIdx = -99;

            // The customer's render: a fresh Element[] (so ItemsSource rebuilds)
            // that GROWS, with the selected index still valid in the new source.
            // This is the exact shape reported in #1090 — the reporter's ComboBox
            // went from ["a","b"] to ["a","b","c"] with the selection intact.
            var el2 = el1 with { Items = MakeItems(3, "lv1090a"), SelectedIndex = 1 };
            rec.UpdateChild(el1, el2, lv, _noOp);
            await Harness.Render();

            int firesFromRebuild = fireCount;
            int indexAfterRebuild = lv.SelectedIndex;
            Console.WriteLine(
                $"# Issue1090 LVSurvive diag: indexAfterRebuild={indexAfterRebuild} " +
                $"firesFromRebuild={firesFromRebuild}");

            // The rebuild is engine work, not user intent: it must not reach the
            // callback whether WinUI kept the selection (no event at all) or
            // dropped it (event consumed by the suppressor).
            H.Check("Issue1090_LVSurvive_RebuildNoLeak", firesFromRebuild == 0);
            fireCount = 0;
            lastIdx = -99;

            // THE BUG. A genuine user selection of the newly-added item. On the
            // unfixed handler the speculative token armed before the swap is
            // still outstanding and ShouldSuppress eats this event.
            lv.SelectedIndex = 2;
            await Harness.Render();
            H.Check("Issue1090_LVSurvive_RealSelectFires", fireCount == 1 && lastIdx == 2);

            // The selection must still land on the control even when the callback
            // is swallowed — this separates "event suppressed" from "write lost".
            H.Check("Issue1090_LVSurvive_ControlAtUserSelection", lv.SelectedIndex == 2);

            rec.UnmountChild(lv);
            parent.Children.Clear();
        }
    }

    /// <summary>Issue #1090 — the GridView handler carries a byte-identical
    /// speculative arm, so it carries the identical defect.</summary>
    internal class GridView_SelectionSurvivesRebuild_NextSelectionFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            int lastIdx = -99;
            var el1 = new GridViewElement(MakeItems(2, "gv1090a"))
            {
                SelectedIndex = 0,
                OnSelectedIndexChanged = i => { fireCount++; lastIdx = i; },
            };

            if (rec.Mount(el1, _noOp) is not WinUI.GridView gv)
            {
                H.Check("Issue1090_GVSurvive_Mounted", false);
                return;
            }

            parent.Children.Add(gv);
            await Harness.Render();
            H.Check("Issue1090_GVSurvive_MountValue", gv.SelectedIndex == 0);

            fireCount = 0;
            lastIdx = -99;

            gv.SelectedIndex = 1;
            await Harness.Render();
            H.Check("Issue1090_GVSurvive_CallbackWiredControl", fireCount == 1 && lastIdx == 1);
            fireCount = 0;
            lastIdx = -99;

            var el2 = el1 with { Items = MakeItems(3, "gv1090a"), SelectedIndex = 1 };
            rec.UpdateChild(el1, el2, gv, _noOp);
            await Harness.Render();

            Console.WriteLine(
                $"# Issue1090 GVSurvive diag: indexAfterRebuild={gv.SelectedIndex} " +
                $"firesFromRebuild={fireCount}");

            H.Check("Issue1090_GVSurvive_RebuildNoLeak", fireCount == 0);
            fireCount = 0;
            lastIdx = -99;

            gv.SelectedIndex = 2;
            await Harness.Render();
            H.Check("Issue1090_GVSurvive_RealSelectFires", fireCount == 1 && lastIdx == 2);
            H.Check("Issue1090_GVSurvive_ControlAtUserSelection", gv.SelectedIndex == 2);

            rec.UnmountChild(gv);
            parent.Children.Clear();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  2. The discriminator: a genuine DESELECT after a surviving rebuild.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Issue #1090 — selection AND deselection must both survive a
    /// rebuild round-trip.
    ///
    /// <para>Both interactions are driven <em>after</em> the rebuild, so each
    /// assertion is a real state transition rather than a no-op write WinUI
    /// ignores. The deselect leg matters because its readback is -1 — the same
    /// value a rebuild's drop echo carries — so a suppression scheme keyed on
    /// "the echo will read back negative" cannot tell the two apart and eats
    /// this event.</para></summary>
    internal class ListView_SelectAndDeselectAcrossRebuild_BothFire(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            int lastIdx = -99;
            // Multiple mode is where a user can genuinely deselect down to an
            // empty selection by clicking the selected item again.
            var el1 = new ListViewElement(MakeItems(3, "lv1090b"))
            {
                SelectionMode = ListViewSelectionMode.Multiple,
                OnSelectedIndexChanged = i => { fireCount++; lastIdx = i; },
            };

            if (rec.Mount(el1, _noOp) is not WinUI.ListView lv)
            {
                H.Check("Issue1090_LVDeselect_Mounted", false);
                return;
            }

            parent.Children.Add(lv);
            await Harness.Render();

            fireCount = 0;
            lastIdx = -99;

            // Positive control: a real selection fires. If this check fails the
            // rest of the fixture is measuring a dead callback, not the product.
            lv.SelectedIndex = 1;
            await Harness.Render();
            H.Check("Issue1090_LVDeselect_CallbackWiredControl", fireCount == 1 && lastIdx == 1);
            fireCount = 0;
            lastIdx = -99;

            // Rebuild with a fresh Element[] — the ItemsSource swap.
            var el2 = el1 with { Items = MakeItems(3, "lv1090b-gen2") };
            rec.UpdateChild(el1, el2, lv, _noOp);
            await Harness.Render();

            int indexAfterRebuild = lv.SelectedIndex;
            Console.WriteLine(
                $"# Issue1090 LVDeselect diag: indexAfterRebuild={indexAfterRebuild} " +
                $"firesFromRebuild={fireCount}");
            H.Check("Issue1090_LVDeselect_RebuildNoLeak", fireCount == 0);
            fireCount = 0;
            lastIdx = -99;

            // A genuine selection AFTER the rebuild. Guarded so the write is a
            // real transition on either platform branch (whether the rebuild
            // preserved index 1 or reset it to -1).
            int target = indexAfterRebuild == 2 ? 1 : 2;
            lv.SelectedIndex = target;
            await Harness.Render();
            H.Check("Issue1090_LVDeselect_SelectAfterRebuildFires", fireCount == 1 && lastIdx == target);
            fireCount = 0;
            lastIdx = -99;

            // Genuine user deselect. The control is definitely selected here, so
            // this is a real transition and the readback is -1.
            lv.SelectedIndex = -1;
            await Harness.Render();
            H.Check("Issue1090_LVDeselect_RealDeselectFires", fireCount == 1 && lastIdx == -1);

            rec.UnmountChild(lv);
            parent.Children.Clear();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  3. Negative control: the #495 leak must not reopen.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Issue #1090 / #495 guard — when the rebuild genuinely DOES drop
    /// the selection (the new source is too short to hold it), the resulting
    /// engine-synthesized <c>SelectionChanged</c> must still be suppressed.
    ///
    /// <para>This is the fixture that fails if a candidate "fixes" #1090 by
    /// simply not arming at all. Bound to <c>UseState</c> that leaked echo is
    /// the #495 render storm.</para></summary>
    internal class ListView_SelectionDroppedByRebuild_NoEchoLeak(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            int lastIdx = -99;
            // SelectedIndex deliberately Unset: the control owns the selection, so
            // the handler performs no drift write and the ONLY event in play is
            // the one the ItemsSource swap itself produces.
            var el1 = new ListViewElement(MakeItems(3, "lv1090c"))
            {
                OnSelectedIndexChanged = i => { fireCount++; lastIdx = i; },
            };

            if (rec.Mount(el1, _noOp) is not WinUI.ListView lv)
            {
                H.Check("Issue1090_LVDrop_Mounted", false);
                return;
            }

            parent.Children.Add(lv);
            await Harness.Render();

            fireCount = 0;
            lastIdx = -99;

            // Positive control + setup: user selects the last item.
            lv.SelectedIndex = 2;
            await Harness.Render();
            H.Check("Issue1090_LVDrop_CallbackWiredControl", fireCount == 1 && lastIdx == 2);
            fireCount = 0;
            lastIdx = -99;

            // Shrink the source so index 2 cannot survive. WinUI drops the
            // selection and raises SelectionChanged(-1) — engine work, not user
            // intent, so it must not reach the callback.
            var el2 = el1 with { Items = MakeItems(1, "lv1090c") };
            rec.UpdateChild(el1, el2, lv, _noOp);
            await Harness.Render();

            Console.WriteLine(
                $"# Issue1090 LVDrop diag: indexAfterRebuild={lv.SelectedIndex} " +
                $"firesFromRebuild={fireCount} lastIdx={lastIdx}");

            H.Check("Issue1090_LVDrop_DropEchoSuppressed", fireCount == 0);

            // And the control must be left genuinely deselected, not merely quiet.
            H.Check("Issue1090_LVDrop_SelectionActuallyDropped", lv.SelectedIndex == -1);

            // A real selection in the shrunken source must still work afterwards —
            // suppressing the drop must not strand a token either.
            lv.SelectedIndex = 0;
            await Harness.Render();
            H.Check("Issue1090_LVDrop_RealSelectAfterDropFires", fireCount == 1 && lastIdx == 0);

            rec.UnmountChild(lv);
            parent.Children.Clear();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  4. Platform probe.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Issue #1090 — measures the raw WinUI <c>ItemsSource</c>-swap
    /// behavior the handler's suppression strategy is built on. No Reactor code
    /// is involved: this is the platform premise, isolated.
    ///
    /// <para><b>What it must NOT assert.</b> Whether a swap that keeps the index
    /// valid preserves the selection is <em>version-dependent</em> — that is the
    /// whole of issue #1090. The runtime bundled with WASDK 2.1.x (which this
    /// self-contained host binds) resets the selection and fires; the reporter's
    /// newer runtime preserves it and raises nothing. Asserting either one pins
    /// this suite to one WinUI build.</para>
    ///
    /// <para><b>What it does assert</b> — the two properties the arm-then-observe
    /// fix actually relies on, both of which hold on either behavior:</para>
    /// <list type="number">
    /// <item><description><c>SelectedIndex</c> is stable across the dispatcher
    /// drain: whatever it reads immediately after the assignment is what it still
    /// reads once everything settles. If WinUI ever updated the index
    /// asynchronously, observing the control right after the swap would be
    /// meaningless and the fix would silently mis-decide.</description></item>
    /// <item><description>The index and the event agree: the selection changing
    /// implies at least one <c>SelectionChanged</c>, and the selection not
    /// changing implies none. That equivalence is exactly what lets an unchanged
    /// index stand in for "no echo is coming."</description></item>
    /// </list>
    /// <para>The branch this machine actually takes, and whether the drop echo is
    /// synchronous or deferred, are logged rather than asserted.</para></summary>
    internal class Probe_ItemsSourceSwapBehavior(Harness h) : SelfTestFixtureBase(h)
    {
        /// <summary>Swaps ItemsSource under an active selection and checks the two
        /// version-independent invariants.</summary>
        private async Task MeasureAsync(
            Grid parent, string label, List<int> initial, int selectIndex, List<int> replacement)
        {
            var lv = new WinUI.ListView { Width = 240, Height = 160 };
            parent.Children.Add(lv);
            lv.ItemsSource = initial;
            await Harness.Render();
            lv.SelectedIndex = selectIndex;
            await Harness.Render();
            H.Check($"Issue1090_Probe_{label}_Setup", lv.SelectedIndex == selectIndex);

            int fires = 0, firesInside = 0;
            bool inside = false;
            lv.SelectionChanged += (_, _) => { fires++; if (inside) firesInside++; };

            inside = true;
            lv.ItemsSource = replacement;
            int idxImmediate = lv.SelectedIndex;
            inside = false;

            await Harness.Render();
            int idxSettled = lv.SelectedIndex;

            bool selectionMoved = idxSettled != selectIndex;
            Console.WriteLine(
                $"# Issue1090 probe {label}: setup={selectIndex} idxImmediate={idxImmediate} " +
                $"idxSettled={idxSettled} firesInsideAssignment={firesInside} firesSettled={fires} " +
                $"=> selection={(selectionMoved ? "RESET" : "PRESERVED")}, " +
                $"echo={(fires == 0 ? "NONE" : firesInside > 0 ? "SYNCHRONOUS" : "DEFERRED")}");

            // Invariant 1 — the post-swap index is readable synchronously and does
            // not drift afterwards, so observing it right after the assignment is
            // a sound basis for deciding whether an echo is coming.
            H.Check($"Issue1090_Probe_{label}_IndexStableAcrossDrain", idxImmediate == idxSettled);

            // Invariant 2 — index movement and event delivery agree in both
            // directions. This is what makes "index unchanged" a valid proxy for
            // "no echo will arrive".
            H.Check($"Issue1090_Probe_{label}_MovedImpliesEvent", !selectionMoved || fires >= 1);
            H.Check($"Issue1090_Probe_{label}_UnmovedImpliesNoEvent", selectionMoved || fires == 0);

            parent.Children.Remove(lv);
        }

        public override async Task RunAsync()
        {
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            // Grow — the selected index stays valid in the new source. This is the
            // case whose outcome differs between WinUI versions.
            await MeasureAsync(parent, "Grow", new List<int> { 0, 1 }, 0, new List<int> { 0, 1, 2 });

            // Same length, new list instance — the shape the handler always
            // produces (Enumerable.Range(...).ToList() on every rebuild).
            await MeasureAsync(parent, "SameLen", new List<int> { 0, 1, 2 }, 1, new List<int> { 0, 1, 2 });

            // Shrink — the selected index cannot survive, so every version must
            // drop the selection and raise.
            await MeasureAsync(parent, "Shrink", new List<int> { 0, 1, 2 }, 2, new List<int> { 0 });

            parent.Children.Clear();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  5. The reporter's end-to-end repro, verbatim.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Issue #1090 — the reporter's scenario, driven end to end through
    /// <c>Component</c> + <c>UseState</c>: grow the bound item array while the
    /// selected index stays valid, then make a genuine selection of the new item.
    /// The state must follow.</summary>
    private class ReproComponent : Component
    {
        public static int CallbackCount;
        public static int RenderCount;
        public static int StateIndex = -99;
        public static Action<int>? SwitchSource;

        public static void Reset()
        {
            CallbackCount = 0;
            RenderCount = 0;
            StateIndex = -99;
            SwitchSource = null;
        }

        public override Element Render()
        {
            RenderCount++;
            var (which, setWhich) = UseState(0);
            var (index, setIndex) = UseState(0);
            StateIndex = index;

            // Mirrors the reporter's ComboBox callback: switch source AND reset
            // the index in one batch.
            SwitchSource = w => { setWhich(w); setIndex(0); };

            var labels = which == 0 ? new[] { "a", "b" } : new[] { "a", "b", "c" };
            // Fresh Element[] every render — idiomatic Reactor, and the trigger
            // for the ItemsSource rebuild.
            return new ListViewElement(labels.Select(s => TextBlock(s)).ToArray())
            {
                SelectedIndex = index,
                OnSelectedIndexChanged = i => { CallbackCount++; setIndex(i); },
            }.Set(l => l.Name = "lv1090repro");
        }
    }

    internal class Repro_GrowSourceThenSelectNewItem(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ReproComponent.Reset();

            var host = H.CreateHost();
            host.Mount(new ReproComponent());
            await Harness.Render();

            var lv = H.FindControl<ListView>(l => l.Name == "lv1090repro");
            H.Check("Issue1090_Repro_Mounted", lv is not null);
            if (lv is null) return;

            H.Check("Issue1090_Repro_InitialSelection", lv.SelectedIndex == 0 && ReproComponent.StateIndex == 0);

            // Step 2 of the repro: switch the combo to L2 — items become a,b,c
            // and the selection (0) is still valid in the new source.
            ReproComponent.SwitchSource!(1);
            await Harness.Render();

            Console.WriteLine(
                $"# Issue1090 Repro after-grow: controlIndex={lv.SelectedIndex} stateIndex={ReproComponent.StateIndex} " +
                $"itemCount={(lv.ItemsSource as global::System.Collections.ICollection)?.Count} " +
                $"callbacks={ReproComponent.CallbackCount} renders={ReproComponent.RenderCount}");

            H.Check("Issue1090_Repro_GrewToThreeItems",
                (lv.ItemsSource as global::System.Collections.ICollection)?.Count == 3);
            H.Check("Issue1090_Repro_SelectionStillZeroAfterGrow",
                lv.SelectedIndex == 0 && ReproComponent.StateIndex == 0);

            int callbacksBeforeClick = ReproComponent.CallbackCount;

            // Step 3: click `c`. THE BUG — the control highlights it but the
            // callback never runs, so the state stays stale at 0.
            lv.SelectedIndex = 2;
            await Harness.Render();

            Console.WriteLine(
                $"# Issue1090 Repro after-click: controlIndex={lv.SelectedIndex} stateIndex={ReproComponent.StateIndex} " +
                $"callbacks+={ReproComponent.CallbackCount - callbacksBeforeClick}");

            H.Check("Issue1090_Repro_ClickFiredCallback",
                ReproComponent.CallbackCount - callbacksBeforeClick >= 1);
            H.Check("Issue1090_Repro_StateFollowedSelection", ReproComponent.StateIndex == 2);
            H.Check("Issue1090_Repro_ControlAtSelection", lv.SelectedIndex == 2);
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    //  6. The confirmed #1090 mechanism: clamped writes against an empty
    //     (or too-short) source strand tokens.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>Issue #1090 — mount with an EMPTY items array while
    /// <c>SelectedIndex</c> is controlled at 0, then let the items arrive. The
    /// mount write is clamped by WinUI, so it must not arm a suppression token;
    /// otherwise that token swallows the user's first real selection.
    ///
    /// <para>This is the reporter's confirmed scenario, reduced to the reconciler
    /// level. Two writes land against the empty source (mount + an intermediate
    /// update, mirroring a component that re-renders before its data loads);
    /// unfixed, both arm, and only one is ever consumed.</para></summary>
    internal class ListView_EmptyMountThenItemsArrive_FirstRealSelectionFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            int lastIdx = -99;

            // Mount EMPTY with a controlled selection — the write can only clamp.
            var el0 = new ListViewElement([])
            {
                SelectedIndex = 0,
                OnSelectedIndexChanged = i => { fireCount++; lastIdx = i; },
            };

            if (rec.Mount(el0, _noOp) is not WinUI.ListView lv)
            {
                H.Check("Issue1090_EmptyMount_Mounted", false);
                return;
            }

            parent.Children.Add(lv);
            await Harness.Render();
            H.Check("Issue1090_EmptyMount_ClampedToMinusOne", lv.SelectedIndex == -1);

            // Intermediate render, still empty (fresh array => a real update).
            var el1 = el0 with { Items = [] };
            rec.UpdateChild(el0, el1, lv, _noOp);
            await Harness.Render();

            // Items arrive.
            var el2 = el1 with { Items = MakeItems(3, "lv1090e") };
            rec.UpdateChild(el1, el2, lv, _noOp);
            await Harness.Render();

            Console.WriteLine(
                $"# Issue1090 EmptyMount diag: afterItemsArrive index={lv.SelectedIndex} fires={fireCount}");

            H.Check("Issue1090_EmptyMount_SelectionMaterialized", lv.SelectedIndex == 0);
            // Materializing the controlled selection is engine work, not user
            // intent — it must not reach the callback.
            H.Check("Issue1090_EmptyMount_NoEchoLeak", fireCount == 0);

            fireCount = 0;
            lastIdx = -99;

            // THE BUG: the user's first genuine selection. Unfixed, a token left
            // over from the clamped write(s) eats this.
            lv.SelectedIndex = 2;
            await Harness.Render();
            H.Check("Issue1090_EmptyMount_FirstRealSelectionFires", fireCount == 1 && lastIdx == 2);

            // And the one after it, proving no second token is lurking.
            fireCount = 0;
            lv.SelectedIndex = 1;
            await Harness.Render();
            H.Check("Issue1090_EmptyMount_SecondRealSelectionFires", fireCount == 1 && lastIdx == 1);

            rec.UnmountChild(lv);
            parent.Children.Clear();
        }
    }

    /// <summary>Issue #1090 — the GridView sibling carries the same clamped-write
    /// shape and therefore the same defect.</summary>
    internal class GridView_EmptyMountThenItemsArrive_FirstRealSelectionFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            int lastIdx = -99;
            var el0 = new GridViewElement([])
            {
                SelectedIndex = 0,
                OnSelectedIndexChanged = i => { fireCount++; lastIdx = i; },
            };

            if (rec.Mount(el0, _noOp) is not WinUI.GridView gv)
            {
                H.Check("Issue1090_GVEmptyMount_Mounted", false);
                return;
            }

            parent.Children.Add(gv);
            await Harness.Render();
            H.Check("Issue1090_GVEmptyMount_ClampedToMinusOne", gv.SelectedIndex == -1);

            var el1 = el0 with { Items = [] };
            rec.UpdateChild(el0, el1, gv, _noOp);
            await Harness.Render();

            var el2 = el1 with { Items = MakeItems(3, "gv1090e") };
            rec.UpdateChild(el1, el2, gv, _noOp);
            await Harness.Render();

            Console.WriteLine(
                $"# Issue1090 GVEmptyMount diag: afterItemsArrive index={gv.SelectedIndex} fires={fireCount}");

            H.Check("Issue1090_GVEmptyMount_SelectionMaterialized", gv.SelectedIndex == 0);
            H.Check("Issue1090_GVEmptyMount_NoEchoLeak", fireCount == 0);

            fireCount = 0;
            lastIdx = -99;

            gv.SelectedIndex = 2;
            await Harness.Render();
            H.Check("Issue1090_GVEmptyMount_FirstRealSelectionFires", fireCount == 1 && lastIdx == 2);

            rec.UnmountChild(gv);
            parent.Children.Clear();
        }
    }

    /// <summary>Issue #1090 — the general form: a controlled index past the end
    /// of a NON-empty source is clamped too, so it must not arm either.
    ///
    /// <para>Empty is just the common case. This pins the actual invariant —
    /// "only arm for a write that can land" — rather than a special-case
    /// <c>Items.Length == 0</c> check, which would leave this variant broken.</para></summary>
    internal class ListView_IndexPastEndOfShortSource_FirstRealSelectionFires(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int fireCount = 0;
            int lastIdx = -99;

            // Two items, but the controlled selection asks for index 5.
            var el0 = new ListViewElement(MakeItems(2, "lv1090f"))
            {
                SelectedIndex = 5,
                OnSelectedIndexChanged = i => { fireCount++; lastIdx = i; },
            };

            if (rec.Mount(el0, _noOp) is not WinUI.ListView lv)
            {
                H.Check("Issue1090_PastEnd_Mounted", false);
                return;
            }

            parent.Children.Add(lv);
            await Harness.Render();

            Console.WriteLine(
                $"# Issue1090 PastEnd diag: afterMount index={lv.SelectedIndex} fires={fireCount}");

            // WinUI cannot honor index 5 against 2 items.
            H.Check("Issue1090_PastEnd_Clamped", lv.SelectedIndex == -1);
            H.Check("Issue1090_PastEnd_NoEchoLeak", fireCount == 0);

            fireCount = 0;
            lastIdx = -99;

            // A genuine selection must not be swallowed by a token armed for the
            // write that could never land.
            lv.SelectedIndex = 1;
            await Harness.Render();
            H.Check("Issue1090_PastEnd_FirstRealSelectionFires", fireCount == 1 && lastIdx == 1);

            rec.UnmountChild(lv);
            parent.Children.Clear();
        }
    }
}
