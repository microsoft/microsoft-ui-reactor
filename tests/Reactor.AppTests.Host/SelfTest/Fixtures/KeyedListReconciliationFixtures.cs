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

    // ────────────────────────────────────────────────────────────────────
    //  LazyVStack (ItemsRepeater) — full op-shape coverage. The Phase 1
    //  `ElementFactory<T>._mountedElements` rekey only paid for itself
    //  if remove / move / reverse also stayed incremental. Pin all three.
    // ────────────────────────────────────────────────────────────────────

    internal class LazyVStack_RemoveFromMiddle_EmitsSingleRemove(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { new Item("a", "A"), new Item("b", "B"), new Item("c", "C"), new Item("d", "D") }
                    : new[] { new Item("a", "A"), new Item("c", "C"), new Item("d", "D") };
                return VStack(
                    Button("RemoveB", () => setPhase(1)),
                    LazyVStack<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(300)
                );
            });

            await Harness.Render();

            var rep = H.FindControl<WinUI.ItemsRepeater>(_ => true);
            var rec = new CollectionChangedRecorder();
            if (rep is not null) rec.Subscribe(rep);

            H.ClickButton("RemoveB");
            await Harness.Render();

            H.Check("KLR_LazyVStack_RemoveMiddle_OneRemove",
                rec.Count(NotifyCollectionChangedAction.Remove) == 1);
            H.Check("KLR_LazyVStack_RemoveMiddle_NoOtherOps",
                rec.Count(NotifyCollectionChangedAction.Add) == 0
                && rec.Count(NotifyCollectionChangedAction.Reset) == 0);

            var state = rep is not null ? Reconciler.GetListState(rep) : null;
            H.Check("KLR_LazyVStack_RemoveMiddle_FinalOrder",
                state is not null
                && state.LastKeys.Count == 3
                && state.LastKeys[0] == "a"
                && state.LastKeys[1] == "c"
                && state.LastKeys[2] == "d");
        }
    }

    internal class LazyVStack_MoveOne_EmitsSingleMove(Harness h) : SelfTestFixtureBase(h)
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
                    Button("Swap", () => setPhase(1)),
                    LazyVStack<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(300)
                );
            });

            await Harness.Render();

            var rep = H.FindControl<WinUI.ItemsRepeater>(_ => true);
            var rec = new CollectionChangedRecorder();
            if (rep is not null) rec.Subscribe(rep);

            H.ClickButton("Swap");
            await Harness.Render();

            H.Check("KLR_LazyVStack_Move_EmitsAtLeastOneMove",
                rec.Count(NotifyCollectionChangedAction.Move) >= 1);
            H.Check("KLR_LazyVStack_Move_NoAddRemove",
                rec.Count(NotifyCollectionChangedAction.Add) == 0
                && rec.Count(NotifyCollectionChangedAction.Remove) == 0);

            var state = rep is not null ? Reconciler.GetListState(rep) : null;
            H.Check("KLR_LazyVStack_Move_FinalOrder",
                state is not null
                && state.LastKeys.Count == 4
                && state.LastKeys[0] == "a"
                && state.LastKeys[1] == "c"
                && state.LastKeys[2] == "b"
                && state.LastKeys[3] == "d");
        }
    }

    // Survivors keep their realized element across an insert. The
    // ElementFactory<T>._mountedElements dictionary is keyed by
    // ReactorRow.Key after Phase 1, so a prepend at index 0 must NOT
    // invalidate the entry for "a"/"b"/"c". A reorder-stable factory
    // is the whole point of the rekey — pin it.
    internal class LazyVStack_InsertAtZero_RealizedElementsKeepIdentity(Harness h) : SelfTestFixtureBase(h)
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
                    LazyVStack<Item>(items, i => i.Id, (i, _) =>
                        TextBlock(i.Label).AutomationId($"lv_{i.Id}")).Height(300)
                );
            });

            await Harness.Render();

            // Capture each TextBlock's hash before the prepend. After the
            // prepend, the survivors should still hand back the exact
            // same TextBlock instances.
            int? Hash(string key) =>
                H.FindControl<TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == $"lv_{key}") is { } tb
                    ? global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(tb)
                    : null;

            var beforeA = Hash("a");
            var beforeB = Hash("b");
            var beforeC = Hash("c");
            H.Check("KLR_LazyVStack_BeforePrepend_AllRealized",
                beforeA is not null && beforeB is not null && beforeC is not null);

            H.ClickButton("Prepend");
            await Harness.Render();

            var afterA = Hash("a");
            var afterB = Hash("b");
            var afterC = Hash("c");

            H.Check("KLR_LazyVStack_SurvivorIdentity_a", beforeA == afterA);
            H.Check("KLR_LazyVStack_SurvivorIdentity_b", beforeB == afterB);
            H.Check("KLR_LazyVStack_SurvivorIdentity_c", beforeC == afterC);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  GridView — match LazyVStack/ListView coverage on move + remove.
    // ────────────────────────────────────────────────────────────────────

    internal class GridView_MoveOne_EmitsSingleMove(Harness h) : SelfTestFixtureBase(h)
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
                    Button("Swap", () => setPhase(1)),
                    GridView<Item>(items, i => i.Id, (i, _) => TextBlock(i.Label)).Height(300)
                );
            });

            await Harness.Render();

            var gv = H.FindControl<WinUI.GridView>(_ => true);
            var rec = new CollectionChangedRecorder();
            if (gv is not null) rec.Subscribe(gv);

            H.ClickButton("Swap");
            await Harness.Render();

            H.Check("KLR_GridView_Move_AtLeastOne",
                rec.Count(NotifyCollectionChangedAction.Move) >= 1);
            H.Check("KLR_GridView_Move_NoAddRemove",
                rec.Count(NotifyCollectionChangedAction.Add) == 0
                && rec.Count(NotifyCollectionChangedAction.Remove) == 0);

            var state = gv is not null ? Reconciler.GetListState(gv) : null;
            H.Check("KLR_GridView_Move_FinalOrder",
                state is not null
                && state.LastKeys.Count == 4
                && state.LastKeys[1] == "c"
                && state.LastKeys[2] == "b");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Hand-built FlexColumn(.WithKey(...)) — extend the existing
    //  prepend-survivor pin to cover every op shape (remove / move /
    //  reverse). This is the ChildReconciler keyed-LIS regression gate
    //  that spec 042 success criterion #3 calls out.
    // ────────────────────────────────────────────────────────────────────

    internal class FlexColumn_KeyedChildren_RemoveMiddle_SurvivorsKeepIdentity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { "a", "b", "c", "d" }
                    : new[] { "a", "c", "d" }; // drop "b"
                return VStack(
                    Button("Remove", () => setPhase(1)),
                    FlexColumn(items.Select(item =>
                        Border(TextBlock(item).AutomationId($"fc_{item}"))
                            .WithKey(item)).Cast<Element>().ToArray())
                );
            });

            await Harness.Render();

            int? Hash(string key) =>
                H.FindControl<TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == $"fc_{key}") is { Parent: Border br }
                    ? global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(br)
                    : null;

            var beforeA = Hash("a");
            var beforeC = Hash("c");
            var beforeD = Hash("d");
            H.Check("KLR_FlexColumn_RemoveMiddle_Captured", beforeA is not null && beforeC is not null && beforeD is not null);

            H.ClickButton("Remove");
            await Harness.Render();

            H.Check("KLR_FlexColumn_RemoveMiddle_SurvivorA", beforeA == Hash("a"));
            H.Check("KLR_FlexColumn_RemoveMiddle_SurvivorC", beforeC == Hash("c"));
            H.Check("KLR_FlexColumn_RemoveMiddle_SurvivorD", beforeD == Hash("d"));
            H.Check("KLR_FlexColumn_RemoveMiddle_BGone",
                H.FindControl<TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "fc_b") is null);
        }
    }

    internal class FlexColumn_KeyedChildren_Swap_SurvivorsKeepIdentity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { "a", "b", "c", "d" }
                    : new[] { "a", "c", "b", "d" }; // swap b and c
                return VStack(
                    Button("Swap", () => setPhase(1)),
                    FlexColumn(items.Select(item =>
                        Border(TextBlock(item).AutomationId($"fcs_{item}"))
                            .WithKey(item)).Cast<Element>().ToArray())
                );
            });

            await Harness.Render();

            int? Hash(string key) =>
                H.FindControl<TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == $"fcs_{key}") is { Parent: Border br }
                    ? global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(br)
                    : null;

            var before = new Dictionary<string, int?>
            {
                ["a"] = Hash("a"),
                ["b"] = Hash("b"),
                ["c"] = Hash("c"),
                ["d"] = Hash("d"),
            };
            H.Check("KLR_FlexColumn_Swap_AllInitial", before.Values.All(v => v is not null));

            H.ClickButton("Swap");
            await Harness.Render();

            // After swap, every key still resolves to its original Border.
            H.Check("KLR_FlexColumn_Swap_SurvivorA", before["a"] == Hash("a"));
            H.Check("KLR_FlexColumn_Swap_SurvivorB", before["b"] == Hash("b"));
            H.Check("KLR_FlexColumn_Swap_SurvivorC", before["c"] == Hash("c"));
            H.Check("KLR_FlexColumn_Swap_SurvivorD", before["d"] == Hash("d"));
        }
    }

    internal class FlexColumn_KeyedChildren_Reverse_SurvivorsKeepIdentity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Reverse-N is the worst case for any LIS-based reconciler
            // because the longest increasing subsequence collapses to a
            // single survivor pin and every other child becomes a move.
            // Identity must still be preserved across the lot — no remount.
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { "a", "b", "c", "d", "e" }
                    : new[] { "e", "d", "c", "b", "a" };
                return VStack(
                    Button("Reverse", () => setPhase(1)),
                    FlexColumn(items.Select(item =>
                        Border(TextBlock(item).AutomationId($"fcr_{item}"))
                            .WithKey(item)).Cast<Element>().ToArray())
                );
            });

            await Harness.Render();

            int? Hash(string key) =>
                H.FindControl<TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == $"fcr_{key}") is { Parent: Border br }
                    ? global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(br)
                    : null;

            var keys = new[] { "a", "b", "c", "d", "e" };
            var before = keys.ToDictionary(k => k, Hash);
            H.Check("KLR_FlexColumn_Reverse_AllInitial", before.Values.All(v => v is not null));

            H.ClickButton("Reverse");
            await Harness.Render();

            int preserved = keys.Count(k => before[k] == Hash(k));
            H.Check("KLR_FlexColumn_Reverse_AllSurvivorsKeepIdentity", preserved == keys.Length);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  IReactorKeyed identity-on-data convention via WithKey<T>(item) —
    //  proves the new Phase 2 overload threads identity into the same
    //  ChildReconciler keyed path as the explicit string form.
    // ────────────────────────────────────────────────────────────────────

    private sealed record KeyedItem(string Id, string Label) : IReactorKeyed
    {
        string IReactorKeyed.Key => Id;
    }

    internal class FlexColumn_WithKeyItem_PreservesIdentityAcrossInsert(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var items = phase == 0
                    ? new[] { new KeyedItem("a", "A"), new KeyedItem("b", "B"), new KeyedItem("c", "C") }
                    : new[] { new KeyedItem("z", "Z"), new KeyedItem("a", "A"), new KeyedItem("b", "B"), new KeyedItem("c", "C") };
                return VStack(
                    Button("Prepend", () => setPhase(1)),
                    // .WithKey(item) — the Phase 2 IReactorKeyed overload.
                    // No explicit string key passed; identity comes from
                    // KeyedItem.IReactorKeyed.Key.
                    FlexColumn(items.Select(item =>
                        Border(TextBlock(item.Label).AutomationId($"kw_{item.Id}"))
                            .WithKey(item)).Cast<Element>().ToArray())
                );
            });

            await Harness.Render();

            int? Hash(string key) =>
                H.FindControl<TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == $"kw_{key}") is { Parent: Border br }
                    ? global::System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(br)
                    : null;

            var beforeA = Hash("a");
            var beforeB = Hash("b");
            var beforeC = Hash("c");
            H.Check("KLR_FlexColumn_WithKeyItem_Captured",
                beforeA is not null && beforeB is not null && beforeC is not null);

            H.ClickButton("Prepend");
            await Harness.Render();

            H.Check("KLR_FlexColumn_WithKeyItem_SurvivorA", beforeA == Hash("a"));
            H.Check("KLR_FlexColumn_WithKeyItem_SurvivorB", beforeB == Hash("b"));
            H.Check("KLR_FlexColumn_WithKeyItem_SurvivorC", beforeC == Hash("c"));
            H.Check("KLR_FlexColumn_WithKeyItem_NewMounted",
                H.FindControl<TextBlock>(t =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(t) == "kw_z") is not null);
        }
    }
}
