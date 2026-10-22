using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Virtualized hosts realize rows from WinUI layout callbacks
/// (<c>IElementFactory.GetElement</c>, <c>ContainerContentChanging</c>) that run
/// after the reconciler traversal has unwound, so a row that consumes context used
/// to read the provider's default (and <c>UseNavigation&lt;T&gt;()</c> threw) until
/// the parent happened to re-render (issue #1386). Each host now snapshots the provider stack when
/// reconciled and replays it around the row mount.
/// </summary>
internal static class VirtualizedRowContextFixtures
{
    private static readonly Context<string> RowCtx = new("default");

    private sealed record RowProps(int Index);

    private sealed class RowConsumer : Component<RowProps>
    {
        public override Element Render()
            => TextBlock($"row{Props.Index}:{UseContext(RowCtx)}").Height(30);
    }

    private sealed class EagerConsumer : Component
    {
        public override Element Render() => TextBlock($"eager:{UseContext(RowCtx)}");
    }

    private static Element Row(int i) => Component<RowConsumer, RowProps>(new RowProps(i));

    private static List<string> RowTexts(Harness h, int minIndex = 0)
        => h.FindAllControls<TextBlock>(tb => tb.Text?.StartsWith("row", StringComparison.Ordinal) == true)
            .Select(tb => tb.Text)
            .Where(t => int.TryParse(t.AsSpan(3, t.IndexOf(':') - 3), out var i) && i >= minIndex)
            .ToList();

    private static bool AllEndWith(List<string> texts, string suffix)
        => texts.Count > 0 && texts.All(t => t.EndsWith(suffix, StringComparison.Ordinal));

    private static string Sample(List<string> texts)
        => texts.Count == 0 ? "<no rows realized>" : string.Join(",", texts.Take(5));

    private static async Task ScrollListTo(Harness h, double offset)
    {
        var sv = h.FindControl<ScrollViewer>(s => s.Content is ItemsRepeater)
            ?? throw new InvalidOperationException("LazyVStack ScrollViewer not found");
        sv.ChangeView(null, offset, null, disableAnimation: true);
        await Harness.Render();
        await Harness.Render();
    }

    internal sealed class LazyVStack_RowsSeeProvider(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var items = Enumerable.Range(0, 300).ToList();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                        Button("VCtxBump", () => setTick(tick + 1)),
                        TextBlock($"vctx-tick:{tick}"),
                        Component<EagerConsumer>(),
                        LazyVStack<int>(items, i => i.ToString(), (i, _) => Row(i)).Height(300))
                    .Provide(RowCtx, "provided");
            });
            await Harness.Render();

            // Positive control: the same consumer outside the virtualized host.
            H.Check("VirtualizedCtx_Lazy_EagerControl", H.FindText("eager:provided") is not null);

            var initial = RowTexts(H);
            H.Check("VirtualizedCtx_Lazy_InitialRows", AllEndWith(initial, ":provided"), Sample(initial));

            await ScrollListTo(H, 150 * 30);
            var scrolled = RowTexts(H, minIndex: 100);
            H.Check("VirtualizedCtx_Lazy_ScrolledRows", AllEndWith(scrolled, ":provided"), Sample(scrolled));

            H.ClickButton("VCtxBump");
            await Harness.Render();
            H.Check("VirtualizedCtx_Lazy_Bumped", H.FindText("vctx-tick:1") is not null);
            var afterBump = RowTexts(H);
            H.Check("VirtualizedCtx_Lazy_RowsAfterRerender", AllEndWith(afterBump, ":provided"), Sample(afterBump));
        }
    }

    // The list element is reference-stable and the realized rows do not consume the
    // context, so nothing realized tells the reconciler the provider changed. Rows
    // realized afterwards must still see the new value, not the mount-time snapshot.
    internal sealed class LazyVStack_ProviderChangeWhileSkipped(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var items = Enumerable.Range(0, 300).ToList();
            var stableList = LazyVStack<int>(items, i => i.ToString(), (i, _) =>
                    i < 100 ? TextBlock($"plain{i}").Height(30) : Row(i))
                .Height(300);

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (value, setValue) = ctx.UseState("first");
                return VStack(
                        Button("VCtxChange", () => setValue("second")),
                        TextBlock($"vctx-value:{value}"),
                        stableList)
                    .Provide(RowCtx, value);
            });
            await Harness.Render();
            H.Check("VirtualizedCtx_Skip_NoConsumerRealized", RowTexts(H).Count == 0, Sample(RowTexts(H)));

            H.ClickButton("VCtxChange");
            await Harness.Render();
            H.Check("VirtualizedCtx_Skip_ProviderChanged", H.FindText("vctx-value:second") is not null);

            await ScrollListTo(H, 150 * 30);
            var scrolled = RowTexts(H, minIndex: 100);
            H.Check("VirtualizedCtx_Skip_RowsSeeNewValue", AllEndWith(scrolled, ":second"), Sample(scrolled));
        }
    }

    private static readonly Context<string> OuterCtx = new("outer-default");

    private static LazyVStackElement<int> HalfConsumerList()
        => LazyVStack<int>(Enumerable.Range(0, 300).ToList(), i => i.ToString(), (i, _) =>
                i < 100 ? TextBlock($"plain{i}").Height(30) : Row(i))
            .Height(300);

    // A removed provider leaves a shorter live stack that is still a prefix of the
    // skipped host's snapshot, so removal needs its own signal.
    internal sealed class LazyVStack_OnlyProviderRemovedWhileSkipped(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var stableList = HalfConsumerList();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (on, setOn) = ctx.UseState(true);
                var body = VStack(
                    Button("VCtxDropOnly", () => setOn(false)),
                    TextBlock($"vctx-on:{on}"),
                    stableList);
                return on ? body.Provide(RowCtx, "removed") : body;
            });
            await Harness.Render();

            H.ClickButton("VCtxDropOnly");
            await Harness.Render();
            H.Check("VirtualizedCtx_DropOnly_Toggled", H.FindText("vctx-on:False") is not null);

            await ScrollListTo(H, 150 * 30);
            var scrolled = RowTexts(H, minIndex: 100);
            H.Check("VirtualizedCtx_DropOnly_RowsSeeDefault", AllEndWith(scrolled, ":default"), Sample(scrolled));
        }
    }

    internal sealed class LazyVStack_InnerProviderRemovedWhileSkipped(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var stableList = HalfConsumerList();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (on, setOn) = ctx.UseState(true);
                var inner = Border(stableList);
                return VStack(
                        Button("VCtxDropInner", () => setOn(false)),
                        TextBlock($"vctx-inner:{on}"),
                        on ? inner.Provide(RowCtx, "removed") : inner)
                    .Provide(OuterCtx, "stable");
            });
            await Harness.Render();

            H.ClickButton("VCtxDropInner");
            await Harness.Render();
            H.Check("VirtualizedCtx_DropInner_Toggled", H.FindText("vctx-inner:False") is not null);

            await ScrollListTo(H, 150 * 30);
            var scrolled = RowTexts(H, minIndex: 100);
            H.Check("VirtualizedCtx_DropInner_RowsSeeDefault", AllEndWith(scrolled, ":default"), Sample(scrolled));
        }
    }

    // Same signal, non-virtualized: removing the LAST provider used to empty the stack,
    // which short-circuited the reference-stable consumer check entirely.
    internal sealed class StableConsumer_LastProviderRemoved(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var stableConsumer = Component<EagerConsumer>();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (on, setOn) = ctx.UseState(true);
                var body = VStack(Button("VCtxDropEager", () => setOn(false)), stableConsumer);
                return on ? body.Provide(RowCtx, "removed") : body;
            });
            await Harness.Render();
            H.Check("VirtualizedCtx_DropEager_Before", H.FindText("eager:removed") is not null);

            H.ClickButton("VCtxDropEager");
            await Harness.Render();
            H.Check("VirtualizedCtx_DropEager_SeesDefault", H.FindText("eager:default") is not null,
                H.FindTextContaining("eager:")?.Text ?? "<missing>");
        }
    }

    // A decorator (Flyout) shares the host's control and refreshes its snapshot before
    // the list element's own .Provide() is on the stack; the list then skips.
    internal sealed class LazyVStack_OwnProviderUnderDecorator(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var stableList = HalfConsumerList().Provide(RowCtx, "own");
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                        Button("VCtxDecoBump", () => setTick(tick + 1)),
                        TextBlock($"vctx-deco:{tick}"),
                        Flyout(stableList, TextBlock($"fly{tick}")))
                    .Provide(RowCtx, "outer");
            });
            await Harness.Render();

            H.ClickButton("VCtxDecoBump");
            await Harness.Render();
            H.Check("VirtualizedCtx_Deco_Bumped", H.FindText("vctx-deco:1") is not null);

            await ScrollListTo(H, 150 * 30);
            var scrolled = RowTexts(H, minIndex: 100);
            H.Check("VirtualizedCtx_Deco_RowsSeeOwnProvider", AllEndWith(scrolled, ":own"), Sample(scrolled));
        }
    }

    // The whole decorator is reference-stable and skips at the element level (Border's
    // single child goes through Update's skip arm). Its skip must not re-snapshot the
    // shared control at the decorator's depth, which would drop the list's own provider.
    internal sealed class LazyVStack_OwnProviderUnderSkippedDecorator(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var stableFlyout = Flyout(HalfConsumerList().Provide(RowCtx, "own"), TextBlock("fly"));
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    Button("VCtxDecoSkipBump", () => setTick(tick + 1)),
                    TextBlock($"vctx-decoskip:{tick}"),
                    // The Border changes each tick so it is updated, and hands the
                    // reference-stable Flyout to Update, whose element-level skip arm runs.
                    Border(stableFlyout).Margin(tick));
            });
            await Harness.Render();

            H.ClickButton("VCtxDecoSkipBump");
            await Harness.Render();
            H.Check("VirtualizedCtx_DecoSkip_Bumped", H.FindText("vctx-decoskip:1") is not null);

            await ScrollListTo(H, 150 * 30);
            var scrolled = RowTexts(H, minIndex: 100);
            H.Check("VirtualizedCtx_DecoSkip_RowsSeeOwnProvider", AllEndWith(scrolled, ":own"), Sample(scrolled));
        }
    }

    private sealed record NavRowProps(int Index);

    // A row parked by RecycleElement is not refreshed when the provider is removed;
    // reusing it for the same row under the now-EMPTY snapshot must still re-check the
    // context its consumer read, rather than shallow-skip and keep the removed value.
    // Drives a standalone factory so every Get/Recycle is one the test asked for.
    internal sealed class Factory_RecycledRowAfterLastProviderRemoved(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var items = Enumerable.Range(0, 5).ToList();
            // A reference-stable consumer under a fresh Border: the Border updates, and its
            // child takes the element-level skip unless a context check declines it.
            var consumers = items.Select(Row).ToArray();
            Func<int, int, Element> builder = (i, _) => Border(consumers[i]);
            var reconciler = new Reconciler();

            ElementFactory<int> factory;
            using (reconciler.PushContextDisposable(RowCtx, "removed"))
                factory = new ElementFactory<int>(items, builder, reconciler, static () => { }, pool: null);
            IElementFactory f = factory;

            static string Text(UIElement c)
                => (c as Border)?.Child is Border wrapper && wrapper.Child is TextBlock tb ? tb.Text : $"<{c.GetType().Name}>";

            var first = f.GetElement(new ElementFactoryGetArgs { Data = 0 });
            H.Check("VirtualizedCtx_Recycle_FirstSeesProvider", Text(first) == "row0:removed", Text(first));
            f.RecycleElement(new ElementFactoryRecycleArgs { Element = first });

            // The host re-renders without the provider: its snapshot is now empty.
            factory.UpdateInPlace(items, builder);

            var again = f.GetElement(new ElementFactoryGetArgs { Data = 0 });
            H.Check("VirtualizedCtx_Recycle_ContainerReused", ReferenceEquals(first, again));
            H.Check("VirtualizedCtx_Recycle_ReusedRowSeesDefault", Text(again) == "row0:default", Text(again));
            return Task.CompletedTask;
        }
    }

    private sealed class NavRow : Component<NavRowProps>
    {
        public override Element Render()
        {
            var nav = UseNavigation<string>();
            return Button($"open{Props.Index}", () => nav.Navigate($"detail{Props.Index}")).Height(30);
        }
    }

    // The common list → detail shape: child-mode UseNavigation in a virtualized row
    // used to throw "found no ancestor NavigationHost" on every realization.
    internal sealed class LazyVStack_UseNavigationChildMode(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var items = Enumerable.Range(0, 100).ToList();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var nav = ctx.UseNavigation<string>("list");
                return NavigationHost(nav, route => route == "list"
                    ? LazyVStack<int>(items, i => i.ToString(), (i, _) =>
                        Component<NavRow, NavRowProps>(new NavRowProps(i))).Height(300)
                    : TextBlock($"page:{route}")) with { Transition = NavigationTransition.None };
            });
            await Harness.Render();

            var open0 = H.FindButton("open0");
            H.Check("VirtualizedCtx_Nav_RowRendered", open0 is not null,
                H.FindTextContaining("UseNavigation")?.Text ?? "<row missing, no error text>");
            if (open0 is null) return;

            H.ClickButton("open0");
            await Harness.Render();
            H.Check("VirtualizedCtx_Nav_RowNavigates", H.FindText("page:detail0") is not null);
        }
    }

    internal sealed class ListView_RowsSeeProvider(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var items = Enumerable.Range(0, 50).ToList();
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                    ListView<int>(items, i => i.ToString(), (i, _) => Row(i)).Height(300))
                .Provide(RowCtx, "provided"));
            await Harness.Render();
            // Realization can take more than one layout wave (TreeView hosts on Loaded/LayoutUpdated).
            await Harness.WaitFor(() => RowTexts(H).Count > 0);

            var rows = RowTexts(H);
            H.Check("VirtualizedCtx_ListView_Rows", AllEndWith(rows, ":provided"), Sample(rows));
        }
    }

    internal sealed class GridView_RowsSeeProvider(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var items = Enumerable.Range(0, 50).ToList();
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                    GridView<int>(items, i => i.ToString(), (i, _) => Row(i)).Height(300))
                .Provide(RowCtx, "provided"));
            await Harness.Render();
            // Realization can take more than one layout wave (TreeView hosts on Loaded/LayoutUpdated).
            await Harness.WaitFor(() => RowTexts(H).Count > 0);

            var rows = RowTexts(H);
            H.Check("VirtualizedCtx_GridView_Rows", AllEndWith(rows, ":provided"), Sample(rows));
        }
    }

    // The untyped ListView/GridView handlers have their own ContainerContentChanging bodies.
    internal sealed class UntypedListView_RowsSeeProvider(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                    ListView(Enumerable.Range(0, 20).Select(Row).ToArray()).Height(300))
                .Provide(RowCtx, "provided"));
            await Harness.Render();
            // Realization can take more than one layout wave (TreeView hosts on Loaded/LayoutUpdated).
            await Harness.WaitFor(() => RowTexts(H).Count > 0);

            var rows = RowTexts(H);
            H.Check("VirtualizedCtx_UntypedListView_Rows", AllEndWith(rows, ":provided"), Sample(rows));
        }
    }

    internal sealed class UntypedGridView_RowsSeeProvider(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                    GridView(Enumerable.Range(0, 20).Select(Row).ToArray()).Height(300))
                .Provide(RowCtx, "provided"));
            await Harness.Render();
            // Realization can take more than one layout wave (TreeView hosts on Loaded/LayoutUpdated).
            await Harness.WaitFor(() => RowTexts(H).Count > 0);

            var rows = RowTexts(H);
            H.Check("VirtualizedCtx_UntypedGridView_Rows", AllEndWith(rows, ":provided"), Sample(rows));
        }
    }

    internal sealed class ItemsView_RowsSeeProvider(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var items = Enumerable.Range(0, 50).ToList();
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                    ItemsView<int>(items, i => i.ToString(), (i, _) => ItemContainer(Row(i))).Height(300))
                .Provide(RowCtx, "provided"));
            await Harness.Render();
            // Realization can take more than one layout wave (TreeView hosts on Loaded/LayoutUpdated).
            await Harness.WaitFor(() => RowTexts(H).Count > 0);

            var rows = RowTexts(H);
            H.Check("VirtualizedCtx_ItemsView_Rows", AllEndWith(rows, ":provided"), Sample(rows));
        }
    }

    internal sealed class TreeView_NodesSeeProvider(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var roots = Enumerable.Range(0, 5).ToList();
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                    TreeView<int>(roots, i => i.ToString(), _ => null, i => Row(i)).Height(300))
                .Provide(RowCtx, "provided"));
            await Harness.Render();
            // Realization can take more than one layout wave (TreeView hosts on Loaded/LayoutUpdated).
            await Harness.WaitFor(() => RowTexts(H).Count > 0);

            var rows = RowTexts(H);
            H.Check("VirtualizedCtx_TreeView_Nodes", AllEndWith(rows, ":provided"), Sample(rows));
        }
    }
}
