using System.Collections.Specialized;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.Internal;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class VirtualListIdentityFixtures
{
    internal sealed class ContentAndCallbacks(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int version = 0, clicked = -1, refs = 0;
            var host = H.CreateHost();
            host.Mount(_ =>
            {
                int captured = version;
                return VirtualList(8, i => Button($"row {i} v{captured}", () => clicked = captured),
                    itemHeight: captured == 0 ? 32 : 46, spacing: captured,
                    @ref: _ => refs++).Height(200);
            });
            await Harness.Render();
            var repeater = H.FindControl<WinUI.ItemsRepeater>(_ => true)
                ?? throw new InvalidOperationException("VirtualList repeater not mounted.");
            var state = Reconciler.GetListState(repeater)!;
            var firstRow = state.Source[0];
            var firstControl = repeater.TryGetElement(0);
            var factory = repeater.ItemTemplate;
            int changes = 0, oldRefs = refs;
            state.Source.CollectionChanged += (_, _) => changes++;
            H.Check("VirtualListIdentity_InitialContent", H.FindButton("row 0 v0") is not null);
            H.ClickButton("row 0 v0");
            H.Check("VirtualListIdentity_InitialCallback", clicked == 0);

            version = 1;
            host.RequestRender(force: true);
            await Harness.Render();
            H.Check("VirtualListIdentity_UpdatedContent", H.FindButton("row 0 v1") is not null);
            H.ClickButton("row 0 v1");
            H.Check("VirtualListIdentity_UpdatedCallback", clicked == 1);
            H.Check("VirtualListIdentity_UpdatedRef", refs > oldRefs);
            H.Check("VirtualListIdentity_UpdatedHeight", H.FindButton("row 0 v1")?.Height == 46);
            H.Check("VirtualListIdentity_UpdatedSpacing", repeater.Layout is WinUI.StackLayout { Spacing: 1 });
            H.Check("VirtualListIdentity_RowIdentity", ReferenceEquals(firstRow, state.Source[0]));
            H.Check("VirtualListIdentity_ControlIdentity", firstControl is not null && ReferenceEquals(firstControl, repeater.TryGetElement(0)));
            H.Check("VirtualListIdentity_FactoryIdentity", ReferenceEquals(factory, repeater.ItemTemplate));
            H.Check("VirtualListIdentity_NoStructuralChanges", changes == 0);
        }
    }

    internal sealed class CountChanges(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int count = 4;
            var host = H.CreateHost();
            host.Mount(_ => VirtualList(count, i => TextBlock($"index {i}"), itemHeight: 32).WithKey("list").Height(240));
            await Harness.Render();
            var repeater = H.FindControl<WinUI.ItemsRepeater>(_ => true)
                ?? throw new InvalidOperationException("VirtualList repeater not mounted.");
            var state = Reconciler.GetListState(repeater)!;
            var source = state.Source;
            var events = new List<NotifyCollectionChangedAction>();
            source.CollectionChanged += (_, e) => events.Add(e.Action);
            foreach (int next in new[] { 5, 3, 0, 2 })
            {
                int previous = count;
                var survivor = count > 0 ? source[0] : null;
                events.Clear();
                count = next;
                host.RequestRender(force: true);
                await Harness.Render();
                H.Check($"VirtualListIdentity_Count_{previous}_{next}",
                    source.Count == next && state.LastKeys.SequenceEqual(Enumerable.Range(0, next).Select(i => i.ToString())));
                H.Check($"VirtualListIdentity_CountOps_{previous}_{next}",
                    events.Count == Math.Abs(next - previous) &&
                    events.All(e => e == (next > previous ? NotifyCollectionChangedAction.Add : NotifyCollectionChangedAction.Remove)));
                H.Check($"VirtualListIdentity_SourceRetained_{previous}_{next}", ReferenceEquals(source, repeater.ItemsSource));
                if (previous > 0 && next > 0)
                    H.Check($"VirtualListIdentity_Survivor_{previous}_{next}", ReferenceEquals(survivor, source[0]));
                H.Check($"VirtualListIdentity_RealizedCount_{next}",
                    next == 0 ? repeater.TryGetElement(0) is null : H.FindText($"index {next - 1}") is not null);
            }

            // A replaced ItemsSource invalidates the shortcut even at the same count.
            repeater.ItemsSource = new[] { 7, 8 };
            host.RequestRender(force: true);
            await Harness.Render();
            var rebuilt = Reconciler.GetListState(repeater);
            H.Check("VirtualListIdentity_ReplacedSourceRebuilt",
                rebuilt is not null && !ReferenceEquals(state, rebuilt) &&
                ReferenceEquals(rebuilt.Source, repeater.ItemsSource) && rebuilt.LastKeys.SequenceEqual(new[] { "0", "1" }));
            H.Check("VirtualListIdentity_RebuiltContent", H.FindText("index 1") is not null);
        }
    }

    internal sealed class CustomKeys(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            string[] keys = ["placeholder", "b", "c", "d", "e", "f", "g", "h"];
            int calls = 0, version = 0;
            Func<int, string> selector = i => { calls++; return keys[i]; };
            bool custom = true;
            var host = H.CreateHost();
            host.Mount(_ =>
            {
                int captured = version;
                return VirtualList(keys.Length, i => TextBlock($"item {i} v{captured}"),
                    getItemKey: custom ? selector : null, itemHeight: 32).Height(320);
            });
            await Harness.Render();
            var repeater = H.FindControl<WinUI.ItemsRepeater>(_ => true)
                ?? throw new InvalidOperationException("VirtualList repeater not mounted.");
            var state = Reconciler.GetListState(repeater)!;
            async Task Refresh()
            {
                version++;
                calls = 0;
                host.RequestRender(force: true);
                await Harness.Render();
            }

            H.Check("VirtualListIdentity_CustomMount", state.LastKeys.SequenceEqual(keys) && calls == keys.Length);
            var bRow = state.ByKey["b"];
            var stationaryControl = repeater.TryGetElement(0);
            (keys[1], keys[2]) = (keys[2], keys[1]);
            await Refresh();
            H.Check("VirtualListIdentity_CustomReordered", state.LastKeys.SequenceEqual(keys) && calls == keys.Length);
            H.Check("VirtualListIdentity_CustomRowSurvives", ReferenceEquals(bRow, state.Source[2]));
            // WinUI may de-realize a moved row. Exercise its factory channel rather
            // than requiring a native container to survive that framework decision.
            var movedControl = repeater.GetOrCreateElement(2);
            bool contentRefreshed = await Harness.WaitFor(() => H.FindText($"item 2 v{version}") is not null, perPassMs: 20);
            H.Check("VirtualListIdentity_CustomRealizedIdentity", Reconciler.GetElementTag(movedControl)?.Key == "b");
            H.Check("VirtualListIdentity_CustomStationaryControlSurvives", stationaryControl is not null && ReferenceEquals(stationaryControl, repeater.TryGetElement(0)));
            H.Check("VirtualListIdentity_CustomContentRefreshed", contentRefreshed);

            var placeholder = state.Source[0];
            keys[0] = "loaded";
            await Refresh();
            H.Check("VirtualListIdentity_PlaceholderBecomesLoaded",
                state.Source[0].Key == "loaded" && !ReferenceEquals(placeholder, state.Source[0]) && calls == keys.Length);
            await Refresh();
            H.Check("VirtualListIdentity_CustomUnchangedStillProjects", calls == keys.Length);

            ReactorDiagnostics.ClearRecentKeyedListWarnings();
            keys[0] = null!;
            await Refresh();
            H.Check("VirtualListIdentity_NullKeyReported", ReactorDiagnostics.RecentKeyedListWarnings.Any(w => w.Kind == KeyedListDiagnosticKind.NullKey));
            int nullReports = ReactorDiagnostics.RecentKeyedListWarnings.Where(w => w.Kind == KeyedListDiagnosticKind.NullKey).Sum(w => w.Count);
            await Refresh();
            H.Check("VirtualListIdentity_NullKeyReportedAgain",
                ReactorDiagnostics.RecentKeyedListWarnings.Where(w => w.Kind == KeyedListDiagnosticKind.NullKey).Sum(w => w.Count) > nullReports);
            keys[0] = keys[1];
            await Refresh();
            H.Check("VirtualListIdentity_DuplicateKeyReported", ReactorDiagnostics.RecentKeyedListWarnings.Any(w => w.Kind == KeyedListDiagnosticKind.DuplicateKey));
            int duplicates = ReactorDiagnostics.RecentKeyedListWarnings.Where(w => w.Kind == KeyedListDiagnosticKind.DuplicateKey).Sum(w => w.Count);
            await Refresh();
            H.Check("VirtualListIdentity_DuplicateKeyReportedAgain",
                ReactorDiagnostics.RecentKeyedListWarnings.Where(w => w.Kind == KeyedListDiagnosticKind.DuplicateKey).Sum(w => w.Count) > duplicates);

            custom = false;
            await Refresh();
            H.Check("VirtualListIdentity_CustomToDefault", calls == 0 &&
                state.LastKeys.SequenceEqual(Enumerable.Range(0, keys.Length).Select(i => i.ToString())));
            var defaultRow = state.Source[0];
            await Refresh();
            H.Check("VirtualListIdentity_DefaultAfterCustomRefreshes",
                ReferenceEquals(defaultRow, state.Source[0]) && H.FindText($"item 0 v{version}") is not null);
            keys[0] = "loaded";
            custom = true;
            await Refresh();
            H.Check("VirtualListIdentity_DefaultToCustom", state.LastKeys.SequenceEqual(keys) && calls >= keys.Length);
            ReactorDiagnostics.ClearRecentKeyedListWarnings();
        }
    }

    internal sealed class DuplicateKeysToDefaultThenGrow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            string[] keys = ["1", "1", "3", "3", "4", "5", "6", "7", "8", "9", "10", "11"];
            int count = keys.Length;
            bool custom = true;
            var host = H.CreateHost();
            ReactorDiagnostics.ClearRecentKeyedListWarnings();
            try
            {
                host.Mount(_ => VirtualList(count, i => TextBlock($"duplicate row {i}"),
                    getItemKey: custom ? i => keys[i] : null, itemHeight: 24)
                    .WithKey("duplicate-to-default").Height(160));
                await Harness.Render();
                var repeater = H.FindControl<WinUI.ItemsRepeater>(_ => true)
                    ?? throw new InvalidOperationException("VirtualList repeater not mounted.");
                var state = Reconciler.GetListState(repeater)!;
                var source = state.Source;
                H.Check("VirtualListIdentity_DuplicateOverlapMounted",
                    state.Source.Count == 12 && state.ByKey.Count == 10 && state.LastKeys.SequenceEqual(keys));

                custom = false;
                host.RequestRender(force: true);
                await Harness.Render();
                state = Reconciler.GetListState(repeater)!;
                var expected = Enumerable.Range(0, count).Select(i => i.ToString()).ToArray();
                Console.WriteLine($"# duplicate-to-default count={state.Source.Count} keys={string.Join(",", state.LastKeys)}");
                H.Check("VirtualListIdentity_DuplicateToDefaultKeepsBinding",
                    ReferenceEquals(source, state.Source) && ReferenceEquals(source, repeater.ItemsSource));
                H.Check("VirtualListIdentity_DuplicateToDefaultCount",
                    state.Source.Count == count && state.ByKey.Count == count);
                H.Check("VirtualListIdentity_DuplicateToDefaultKeys",
                    state.LastKeys.SequenceEqual(expected) && state.Source.Select(r => r.Key).SequenceEqual(expected));
                H.Check("VirtualListIdentity_DuplicateToDefaultIndices",
                    state.Source.Select((row, i) => row.Index == i
                        && state.ByKey.TryGetValue(row.Key, out var indexed) && ReferenceEquals(row, indexed)).All(match => match));
                var first = state.Source[0];
                var events = new List<NotifyCollectionChangedAction>();
                state.Source.CollectionChanged += (_, e) => events.Add(e.Action);

                count++;
                host.RequestRender(force: true);
                await Harness.Render();
                var grown = Reconciler.GetListState(repeater)!;
                expected = Enumerable.Range(0, count).Select(i => i.ToString()).ToArray();
                H.Check("VirtualListIdentity_DuplicateRecoveryGrowth",
                    grown.Source.Count == count && grown.ByKey.Count == count
                    && grown.LastKeys.SequenceEqual(expected) && grown.Source.Select(r => r.Key).SequenceEqual(expected));
                H.Check("VirtualListIdentity_DuplicateRecoveryAppendsOnce",
                    ReferenceEquals(state, grown) && ReferenceEquals(first, grown.Source[0])
                    && events.SequenceEqual(new[] { NotifyCollectionChangedAction.Add }));
                var tail = repeater.GetOrCreateElement(count - 1);
                H.Check("VirtualListIdentity_DuplicateRecoveryRendersTail",
                    tail is WinUI.TextBlock { Text: "duplicate row 12" } && Reconciler.GetElementTag(tail)?.Key == "12");
            }
            finally
            {
                ReactorDiagnostics.ClearRecentKeyedListWarnings();
            }
        }
    }

    internal sealed class UpdateAllocations(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const int count = 5_000, iterations = 8;
            foreach (bool custom in new[] { false, true })
            {
                var reconciler = new Reconciler();
                int selectorCalls = 0, renders = 0, changes = 0;
                Func<int, string>? selector = custom ? i => { selectorCalls++; return i.ToString(); } : null;
                Element Tree(int tick) => VirtualList(count, i =>
                {
                    renders++;
                    return TextBlock($"allocation row {i} tick {tick}");
                }, getItemKey: selector, itemHeight: 32).Height(160);
                Action rerender = static () => { };
                var tree = Tree(0);
                var control = reconciler.Reconcile(null, tree, null, rerender)!;
                H.SetContent(control);
                await Harness.Render();
                var repeater = H.FindControl<WinUI.ItemsRepeater>(_ => true)
                    ?? throw new InvalidOperationException("Allocation probe repeater not mounted.");
                var state = Reconciler.GetListState(repeater)!;
                var row = state.Source[0];
                var first = repeater.TryGetElement(0);
                state.Source.CollectionChanged += (_, _) => changes++;
                try
                {
                    void Update(int tick)
                    {
                        var next = Tree(tick);
                        control = reconciler.Reconcile(tree, next, control, rerender) ?? control;
                        tree = next;
                    }
                    for (int i = 0; i < 4; i++) Update(i + 1);
                    selectorCalls = renders = changes = 0;
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int i = 0; i < iterations; i++) Update(i + 10);
                    long bytes = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
                    Console.WriteLine($"# VirtualListAllocation custom={custom} count={count} bytes={bytes} selectorCalls={selectorCalls} rowBuilds={renders} structuralOps={changes} iterations={iterations}");
                    H.Check($"VirtualListIdentity_AllocationContent_{custom}",
                        H.FindText($"allocation row 0 tick {iterations + 9}") is not null && renders >= iterations);
                    H.Check($"VirtualListIdentity_AllocationIdentity_{custom}",
                        first is not null && ReferenceEquals(first, repeater.TryGetElement(0)) && ReferenceEquals(row, state.Source[0]) && changes == 0);
                    if (custom)
                        H.Check("VirtualListIdentity_AllocationCustomProjectsEveryKey", selectorCalls == count * iterations);
                    else
                        H.Check("VirtualListIdentity_DefaultAllocationBoundedByRealization", bytes < 100_000);
                }
                finally
                {
                    H.SetContent(null);
                    reconciler.Reconcile(tree, null, control, rerender);
                }
            }
        }
    }
}
