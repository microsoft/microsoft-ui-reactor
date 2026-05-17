using System.Collections.Specialized;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 042 Phase 1 — end-to-end verification that the
/// <see cref="KeyedListDiff"/> pipeline produces the expected
/// <c>INotifyCollectionChanged</c> stream when a Reactor component re-renders
/// with a new immutable items list. Each fixture pins one shape (insert,
/// remove, move, bulk-replace bailout, ItemsRepeater parity, hand-built
/// <c>FlexColumn</c> regression) so a future refactor can't silently break
/// container-level animation.
///
/// These fixtures sit between the unit-tested algorithm
/// (<c>tests/Reactor.Tests/Internal/KeyedListDiffTests.cs</c>) and the
/// gallery-visible animation (Phase 4 samples) — they exercise the real
/// reconciler, mount real WinUI ListView/GridView/ItemsRepeater controls,
/// and read their attached <see cref="ReactorListState"/> + the resulting
/// CollectionChanged events.
/// </summary>
internal static class KeyedListReconciliationFixtures
{
    private record Item(string Id, string Label);

    // ────────────────────────────────────────────────────────────────────
    //  Helpers
    // ────────────────────────────────────────────────────────────────────

    private sealed class CollectionChangedRecorder
    {
        public List<NotifyCollectionChangedEventArgs> Events { get; } = new();
        public void Subscribe(WinUI.ListViewBase lvb)
        {
            if (lvb.ItemsSource is INotifyCollectionChanged ncc)
                ncc.CollectionChanged += (_, e) => Events.Add(e);
        }
        public void Subscribe(WinUI.ItemsRepeater repeater)
        {
            if (repeater.ItemsSource is INotifyCollectionChanged ncc)
                ncc.CollectionChanged += (_, e) => Events.Add(e);
        }
        public int Count(NotifyCollectionChangedAction action) =>
            Events.Count(e => e.Action == action);
        public int Total => Events.Count;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Mount path — ListView gets an OC<ReactorRow>, not int range
    // ────────────────────────────────────────────────────────────────────

    internal class ListView_MountsOcSource(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var items = new[]
            {
                new Item("a", "Alpha"),
                new Item("b", "Beta"),
                new Item("c", "Gamma"),
            };

            var host = H.CreateHost();
            host.Mount(_ =>
                ListView<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(200));

            await Harness.Render();

            var lv = H.FindControl<WinUI.ListView>(_ => true);
            H.Check("KLR_ListView_Mounted", lv is not null);

            // ItemsSource is the internally-owned OC<ReactorRow>.
            H.Check("KLR_ListView_BoundToReactorRowOc",
                lv?.ItemsSource is global::System.Collections.ObjectModel.ObservableCollection<ReactorRow>);

            // Attached ReactorListState round-trip.
            var state = lv is not null ? Reconciler.GetListState(lv) : null;
            H.Check("KLR_ListView_StateAttached", state is not null);
            H.Check("KLR_ListView_StateKeysMatchInput",
                state is not null
                && state.LastKeys.Count == 3
                && state.LastKeys[0] == "a"
                && state.LastKeys[1] == "b"
                && state.LastKeys[2] == "c");

            // Item rendering: the labels should appear in the visual tree.
            H.Check("KLR_ListView_LabelsRendered",
                H.FindTextContaining("Alpha") is not null
                && H.FindTextContaining("Beta") is not null
                && H.FindTextContaining("Gamma") is not null);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Update path — single insert at 0 produces a single Add event
    // ────────────────────────────────────────────────────────────────────

    internal class ListView_InsertAtZero_EmitsSingleAdd(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { new Item("a", "Alpha"), new Item("b", "Beta"), new Item("c", "Gamma") }
                    : new[] { new Item("z", "Zero"), new Item("a", "Alpha"), new Item("b", "Beta"), new Item("c", "Gamma") };

                return VStack(
                    Button("Trigger", () => setPhase(1)),
                    ListView<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(200)
                );
            });

            await Harness.Render();

            var lv = H.FindControl<WinUI.ListView>(_ => true);
            H.Check("KLR_InsertAt0_ListViewExists", lv is not null);
            var rec = new CollectionChangedRecorder();
            if (lv is not null) rec.Subscribe(lv);

            H.ClickButton("Trigger");
            await Harness.Render();

            // Single Add event, no Reset, no Remove.
            H.Check("KLR_InsertAt0_OneAdd", rec.Count(NotifyCollectionChangedAction.Add) == 1);
            H.Check("KLR_InsertAt0_NoReset", rec.Count(NotifyCollectionChangedAction.Reset) == 0);
            H.Check("KLR_InsertAt0_NoRemove", rec.Count(NotifyCollectionChangedAction.Remove) == 0);

            // Final state matches expected.
            var state = lv is not null ? Reconciler.GetListState(lv) : null;
            H.Check("KLR_InsertAt0_FinalKeys",
                state is not null
                && state.LastKeys.Count == 4
                && state.LastKeys[0] == "z"
                && state.LastKeys[3] == "c");

            H.Check("KLR_InsertAt0_NewLabelRendered",
                H.FindTextContaining("Zero") is not null);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Update path — single remove from end produces a single Remove event
    // ────────────────────────────────────────────────────────────────────

    internal class ListView_RemoveFromEnd_EmitsSingleRemove(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { new Item("a", "Alpha"), new Item("b", "Beta"), new Item("c", "Gamma") }
                    : new[] { new Item("a", "Alpha"), new Item("b", "Beta") };

                return VStack(
                    Button("Trigger", () => setPhase(1)),
                    ListView<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(200)
                );
            });

            await Harness.Render();

            var lv = H.FindControl<WinUI.ListView>(_ => true);
            var rec = new CollectionChangedRecorder();
            if (lv is not null) rec.Subscribe(lv);

            H.ClickButton("Trigger");
            await Harness.Render();

            H.Check("KLR_RemoveFromEnd_OneRemove", rec.Count(NotifyCollectionChangedAction.Remove) == 1);
            H.Check("KLR_RemoveFromEnd_NoAdd", rec.Count(NotifyCollectionChangedAction.Add) == 0);
            H.Check("KLR_RemoveFromEnd_NoReset", rec.Count(NotifyCollectionChangedAction.Reset) == 0);

            var state = lv is not null ? Reconciler.GetListState(lv) : null;
            H.Check("KLR_RemoveFromEnd_FinalCount",
                state is not null && state.LastKeys.Count == 2);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Update path — single move emits one Move event (not insert+remove)
    // ────────────────────────────────────────────────────────────────────

    internal class ListView_MoveOne_EmitsSingleMove(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { new Item("a", "A"), new Item("b", "B"), new Item("c", "C"), new Item("d", "D") }
                    : new[] { new Item("a", "A"), new Item("c", "C"), new Item("b", "B"), new Item("d", "D") };

                return VStack(
                    Button("Trigger", () => setPhase(1)),
                    ListView<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(200)
                );
            });

            await Harness.Render();

            var lv = H.FindControl<WinUI.ListView>(_ => true);
            var rec = new CollectionChangedRecorder();
            if (lv is not null) rec.Subscribe(lv);

            H.ClickButton("Trigger");
            await Harness.Render();

            H.Check("KLR_Move_EmitsAtLeastOneMove",
                rec.Count(NotifyCollectionChangedAction.Move) >= 1);
            H.Check("KLR_Move_NoAddRemove",
                rec.Count(NotifyCollectionChangedAction.Add) == 0
                && rec.Count(NotifyCollectionChangedAction.Remove) == 0);

            var state = lv is not null ? Reconciler.GetListState(lv) : null;
            H.Check("KLR_Move_FinalOrder",
                state is not null
                && state.LastKeys.Count == 4
                && state.LastKeys[0] == "a"
                && state.LastKeys[1] == "c"
                && state.LastKeys[2] == "b"
                && state.LastKeys[3] == "d");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Update path — bulk replace bailout. >25% churn over the floor.
    // ────────────────────────────────────────────────────────────────────

    internal class ListView_BulkReplace_TriggersBailout(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Build two 20-item lists with 100% churn — completely fresh keys.
            var initial = new Item[20];
            var replaced = new Item[20];
            for (int i = 0; i < 20; i++)
            {
                initial[i] = new Item($"old{i}", $"L{i}");
                replaced[i] = new Item($"new{i}", $"R{i}");
            }

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0 ? initial : replaced;
                return VStack(
                    Button("Replace", () => setPhase(1)),
                    ListView<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(300)
                );
            });

            await Harness.Render();
            var lv = H.FindControl<WinUI.ListView>(_ => true);
            var initialState = lv is not null ? Reconciler.GetListState(lv) : null;
            H.Check("KLR_BulkReplace_InitialState", initialState is not null && initialState.LastKeys.Count == 20);

            H.ClickButton("Replace");
            await Harness.Render();

            // After bailout, Reset replaces Source contents — the state
            // object is reused, but its LastKeys reflect the new items.
            var afterState = lv is not null ? Reconciler.GetListState(lv) : null;
            H.Check("KLR_BulkReplace_AfterStateExists", afterState is not null);
            H.Check("KLR_BulkReplace_AfterCountMatches",
                afterState is not null && afterState.LastKeys.Count == 20);
            H.Check("KLR_BulkReplace_AfterKeysSwapped",
                afterState is not null
                && afterState.LastKeys[0] == "new0"
                && afterState.LastKeys[19] == "new19");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  No-op render — identical items list does NOT emit any
    //  CollectionChanged events. This is the steady-state cost path.
    // ────────────────────────────────────────────────────────────────────

    internal class ListView_IdenticalRender_NoCollectionChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var items = new[]
            {
                new Item("a", "A"),
                new Item("b", "B"),
                new Item("c", "C"),
            };

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (count, setCount) = ctx.UseState(0);
                return VStack(
                    TextBlock($"Count:{count}"),
                    Button("Inc", () => setCount(count + 1)),
                    ListView<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(200)
                );
            });

            await Harness.Render();

            var lv = H.FindControl<WinUI.ListView>(_ => true);
            var rec = new CollectionChangedRecorder();
            if (lv is not null) rec.Subscribe(lv);

            H.ClickButton("Inc");
            await Harness.Render();
            H.ClickButton("Inc");
            await Harness.Render();

            H.Check("KLR_Identical_NoCollectionChange", rec.Total == 0);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  GridView parity — same diff shape on insert.
    // ────────────────────────────────────────────────────────────────────

    internal class GridView_InsertAtEnd_EmitsSingleAdd(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { new Item("a", "A"), new Item("b", "B") }
                    : new[] { new Item("a", "A"), new Item("b", "B"), new Item("c", "C") };
                return VStack(
                    Button("Add", () => setPhase(1)),
                    GridView<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(200)
                );
            });

            await Harness.Render();

            var gv = H.FindControl<WinUI.GridView>(_ => true);
            H.Check("KLR_GridView_Mounted", gv is not null);
            H.Check("KLR_GridView_BoundToReactorRowOc",
                gv?.ItemsSource is global::System.Collections.ObjectModel.ObservableCollection<ReactorRow>);

            var rec = new CollectionChangedRecorder();
            if (gv is not null) rec.Subscribe(gv);

            H.ClickButton("Add");
            await Harness.Render();

            H.Check("KLR_GridView_OneAdd", rec.Count(NotifyCollectionChangedAction.Add) == 1);
            H.Check("KLR_GridView_NoOtherOps",
                rec.Count(NotifyCollectionChangedAction.Remove) == 0
                && rec.Count(NotifyCollectionChangedAction.Reset) == 0);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  LazyVStack — same diff shape on insert at 0, plus the
    //  ElementFactory._mountedElements stays reorder-stable. The previous
    //  int-keyed dictionary lost every realized item's tracking entry
    //  when the index shifted by one.
    // ────────────────────────────────────────────────────────────────────

    internal class LazyVStack_InsertAtZero_EmitsSingleAdd(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { new Item("a", "A"), new Item("b", "B"), new Item("c", "C") }
                    : new[] { new Item("z", "Z"), new Item("a", "A"), new Item("b", "B"), new Item("c", "C") };
                return VStack(
                    Button("Prepend", () => setPhase(1)),
                    LazyVStack<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(300)
                );
            });

            await Harness.Render();

            var rep = H.FindControl<WinUI.ItemsRepeater>(_ => true);
            H.Check("KLR_LazyVStack_RepeaterMounted", rep is not null);
            H.Check("KLR_LazyVStack_BoundToReactorRowOc",
                rep?.ItemsSource is global::System.Collections.ObjectModel.ObservableCollection<ReactorRow>);

            var rec = new CollectionChangedRecorder();
            if (rep is not null) rec.Subscribe(rep);

            H.ClickButton("Prepend");
            await Harness.Render();

            H.Check("KLR_LazyVStack_OneAdd", rec.Count(NotifyCollectionChangedAction.Add) == 1);
            H.Check("KLR_LazyVStack_NoOtherOps",
                rec.Count(NotifyCollectionChangedAction.Remove) == 0
                && rec.Count(NotifyCollectionChangedAction.Reset) == 0);

            // Final state is reorder-stable: key "z" → index 0, "c" → 3.
            var state = rep is not null ? Reconciler.GetListState(rep) : null;
            H.Check("KLR_LazyVStack_FinalOrder",
                state is not null
                && state.LastKeys.Count == 4
                && state.LastKeys[0] == "z"
                && state.LastKeys[1] == "a"
                && state.LastKeys[2] == "b"
                && state.LastKeys[3] == "c");

            // The new "Z" label should be in the visual tree.
            H.Check("KLR_LazyVStack_NewLabelRendered",
                H.FindTextContaining("Z") is not null);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Hand-built FlexColumn(items.Select(... .WithKey(item.Id))) — the
    //  spec criterion #3 regression gate. Phase 1 must not touch this path.
    // ────────────────────────────────────────────────────────────────────

    internal class FlexColumn_KeyedChildren_SurvivorIdentityPreserved(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            // Track WinUI Border instances by their RuntimeHelpers hash code
            // before/after re-render. Survivors must keep the same hash.
            var beforeHashes = new Dictionary<string, int>();
            var afterHashes = new Dictionary<string, int>();

            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { "a", "b", "c" }
                    : new[] { "z", "a", "b", "c" }; // prepend "z"
                return VStack(
                    Button("Prepend", () => setPhase(1)),
                    FlexColumn(items.Select(item =>
                        Border(TextBlock(item).AutomationId($"row_{item}"))
                            .WithKey(item)).Cast<Element>().ToArray())
                );
            });

            await Harness.Render();
            // Capture each border by its child TextBlock's automation id.
            foreach (var key in new[] { "a", "b", "c" })
            {
                var tb = H.FindControl<TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == $"row_{key}");
                if (tb?.Parent is Border b)
                    beforeHashes[key] = global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(b);
            }
            H.Check("KLR_FlexColumn_InitialCaptures3", beforeHashes.Count == 3);

            H.ClickButton("Prepend");
            await Harness.Render();

            foreach (var key in new[] { "a", "b", "c" })
            {
                var tb = H.FindControl<TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == $"row_{key}");
                if (tb?.Parent is Border b)
                    afterHashes[key] = global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(b);
            }

            H.Check("KLR_FlexColumn_SurvivorsKeepIdentity_a",
                beforeHashes.TryGetValue("a", out var a1) && afterHashes.TryGetValue("a", out var a2) && a1 == a2);
            H.Check("KLR_FlexColumn_SurvivorsKeepIdentity_b",
                beforeHashes.TryGetValue("b", out var b1) && afterHashes.TryGetValue("b", out var b2) && b1 == b2);
            H.Check("KLR_FlexColumn_SurvivorsKeepIdentity_c",
                beforeHashes.TryGetValue("c", out var c1) && afterHashes.TryGetValue("c", out var c2) && c1 == c2);

            // The new "z" row exists.
            H.Check("KLR_FlexColumn_NewKeyMounted",
                H.FindControl<TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "row_z") is not null);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Empty → non-empty and non-empty → empty
    // ────────────────────────────────────────────────────────────────────

    internal class ListView_EmptyToNonEmpty_OnlyAdds(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? global::System.Array.Empty<Item>()
                    : new[] { new Item("a", "A"), new Item("b", "B") };
                return VStack(
                    Button("Fill", () => setPhase(1)),
                    ListView<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(200)
                );
            });

            await Harness.Render();
            var lv = H.FindControl<WinUI.ListView>(_ => true);
            var rec = new CollectionChangedRecorder();
            if (lv is not null) rec.Subscribe(lv);

            H.ClickButton("Fill");
            await Harness.Render();

            H.Check("KLR_EmptyToNonEmpty_TwoAdds", rec.Count(NotifyCollectionChangedAction.Add) == 2);
            H.Check("KLR_EmptyToNonEmpty_NoReset", rec.Count(NotifyCollectionChangedAction.Reset) == 0);
        }
    }

    internal class ListView_NonEmptyToEmpty_OnlyRemoves(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { new Item("a", "A"), new Item("b", "B"), new Item("c", "C") }
                    : global::System.Array.Empty<Item>();
                return VStack(
                    Button("Clear", () => setPhase(1)),
                    ListView<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(200)
                );
            });

            await Harness.Render();
            var lv = H.FindControl<WinUI.ListView>(_ => true);
            var rec = new CollectionChangedRecorder();
            if (lv is not null) rec.Subscribe(lv);

            H.ClickButton("Clear");
            await Harness.Render();

            H.Check("KLR_NonEmptyToEmpty_ThreeRemoves", rec.Count(NotifyCollectionChangedAction.Remove) == 3);
            H.Check("KLR_NonEmptyToEmpty_NoReset", rec.Count(NotifyCollectionChangedAction.Reset) == 0);

            var state = lv is not null ? Reconciler.GetListState(lv) : null;
            H.Check("KLR_NonEmptyToEmpty_StateCleared",
                state is not null && state.LastKeys.Count == 0);
        }
    }
}
