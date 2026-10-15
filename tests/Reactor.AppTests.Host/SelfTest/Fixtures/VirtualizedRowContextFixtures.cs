using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Navigation;
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

    private sealed record NavRowProps(int Index);

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

            var rows = RowTexts(H);
            H.Check("VirtualizedCtx_ListView_Rows", AllEndWith(rows, ":provided"), Sample(rows));
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

            var rows = RowTexts(H);
            H.Check("VirtualizedCtx_TreeView_Nodes", AllEndWith(rows, ":provided"), Sample(rows));
        }
    }
}
