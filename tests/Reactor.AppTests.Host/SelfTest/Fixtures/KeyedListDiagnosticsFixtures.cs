using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Keyed-list diagnostics an inspector relies on, found missing while testing winapp
/// DevTools: templated <c>ListView&lt;T&gt;</c> / <c>GridView&lt;T&gt;</c> /
/// <c>FlipView&lt;T&gt;</c> rows carry their <c>KeySelector</c> key (as <c>LazyVStack</c>
/// rows do); a <c>LazyVStack</c> null key is reported; duplicate <c>.WithKey</c> siblings in a
/// <c>VStack</c> are reported; and the warnings name the component and call site.
/// </summary>
internal class KeyedListDiagnostics_RowsKeysAndWarnings(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        bool publish = ReactorSourcePublisher.IsSupported;
        var previous = ReactorSourcePublisher.IsEnabled;
        ReactorSourcePublisher.IsEnabled = publish;
        try
        {
            // ── Rows carry their KeySelector key ─────────────────────────
            H.CreateHost().Mount(new KeyedRowsProbe());
            await Harness.Render();
            await Harness.Render(100);

            string? KeyOf(string text)
            {
                var control = H.FindControl<WinUI.TextBlock>(t => t.Text == text);
                return control is null ? null : Reconciler.GetElementTag(control)?.Key;
            }

            Console.WriteLine($"# row keys: lv={KeyOf("lv-b")} gv={KeyOf("gv-b")} fv={KeyOf("fv-a")} lazy={KeyOf("lazy-b")}");
            H.Check("KeyedListDiagnostics_ListViewRowCarriesKey", KeyOf("lv-b") == "key-b");
            H.Check("KeyedListDiagnostics_GridViewRowCarriesKey", KeyOf("gv-b") == "key-b");
            H.Check("KeyedListDiagnostics_FlipViewRowCarriesKey", KeyOf("fv-a") == "key-a");
            // Positive control: LazyVStack rows already did.
            H.Check("KeyedListDiagnostics_LazyVStackRowCarriesKey", KeyOf("lazy-b") == "key-b");
            // An author's own row key still wins.
            H.Check("KeyedListDiagnostics_AuthorRowKeyWins", KeyOf("lv-own") == "own");
            H.SetContent(null);

            // ── LazyVStack null key ──────────────────────────────────────
            ReactorDiagnostics.ClearRecentKeyedListWarnings();
            H.CreateHost().Mount(new NullKeyLazyProbe());
            await Harness.Render();
            await Harness.Render(100);
            var nullKey = ReactorDiagnostics.RecentKeyedListWarnings
                .Where(w => w.Kind == KeyedListDiagnosticKind.NullKey).ToList();
            Console.WriteLine("# null key: " + string.Join("; ", nullKey.Select(Describe)));
            H.Check("KeyedListDiagnostics_LazyVStackNullKeyReported",
                nullKey.Count(w => w.ControlContext == "ItemsRepeater") == 1);
            H.Check("KeyedListDiagnostics_ListViewNullKeyReported",
                nullKey.Count(w => w.ControlContext == "ListView") == 1);
            CheckAttribution("KeyedListDiagnostics_LazyVStackNullKey",
                nullKey.FirstOrDefault(w => w.ControlContext == "ItemsRepeater"), nameof(NullKeyLazyProbe), publish);

            // Re-rendering a list that keeps its null key reports it again (deduplicated) but
            // still diffs incrementally: no Reset of the bound collection, so rows are not torn
            // down and re-realized every render (a null reaching the diff would bail out).
            var lazyRowBefore = H.FindControl<WinUI.TextBlock>(t => t.Text == "null-key row one");
            var listRowBefore = H.FindControl<WinUI.TextBlock>(t => t.Text == "null-key lv one");
            var repeaterSource = H.FindControl<WinUI.ItemsRepeater>(_ => true)?.ItemsSource
                as global::System.Collections.Specialized.INotifyCollectionChanged;
            var listSource = H.FindControl<WinUI.ListView>(_ => true)?.ItemsSource
                as global::System.Collections.Specialized.INotifyCollectionChanged;
            int repeaterResets = 0, listResets = 0;
            global::System.Collections.Specialized.NotifyCollectionChangedEventHandler onRepeater = (_, e) =>
            {
                if (e.Action == global::System.Collections.Specialized.NotifyCollectionChangedAction.Reset) repeaterResets++;
            };
            global::System.Collections.Specialized.NotifyCollectionChangedEventHandler onList = (_, e) =>
            {
                if (e.Action == global::System.Collections.Specialized.NotifyCollectionChangedAction.Reset) listResets++;
            };
            H.Check("KeyedListDiagnostics_NullKeyUpdate_SourcesFound", repeaterSource is not null && listSource is not null);
            if (repeaterSource is not null) repeaterSource.CollectionChanged += onRepeater;
            if (listSource is not null) listSource.CollectionChanged += onList;
            H.ClickButton("null-key-bump");
            await Harness.Render();
            await Harness.Render(100);
            if (repeaterSource is not null) repeaterSource.CollectionChanged -= onRepeater;
            if (listSource is not null) listSource.CollectionChanged -= onList;
            Console.WriteLine($"# null-key update resets: repeater={repeaterResets} list={listResets}");
            var lazyRowAfter = H.FindControl<WinUI.TextBlock>(t => t.Text == "null-key row one");
            var listRowAfter = H.FindControl<WinUI.TextBlock>(t => t.Text == "null-key lv one");
            H.Check("KeyedListDiagnostics_NullKeyUpdate_RowsRendered", H.FindText("null-key tick 1") is not null);
            H.Check("KeyedListDiagnostics_NullKeyUpdate_LazyVStackNotReset",
                repeaterResets == 0 && lazyRowBefore is not null && ReferenceEquals(lazyRowBefore, lazyRowAfter));
            H.Check("KeyedListDiagnostics_NullKeyUpdate_ListViewNotReset",
                listResets == 0 && listRowBefore is not null && ReferenceEquals(listRowBefore, listRowAfter));
            H.Check("KeyedListDiagnostics_NullKeyUpdate_StillReported",
                ReactorDiagnostics.RecentKeyedListWarnings.Any(w => w.Kind == KeyedListDiagnosticKind.NullKey && w.Count > 1));
            H.SetContent(null);

            // ── Duplicate sibling keys in a VStack ───────────────────────
            ReactorDiagnostics.ClearRecentKeyedListWarnings();
            var dupHost = H.CreateHost();
            dupHost.Mount(new DuplicateSiblingsProbe());
            await Harness.Render();
            var mountDup = ReactorDiagnostics.RecentKeyedListWarnings
                .Where(w => w.Kind == KeyedListDiagnosticKind.DuplicateKey && w.SampleKeys.Contains("same")).ToList();
            Console.WriteLine("# duplicate siblings (mount): " + string.Join("; ", mountDup.Select(Describe)));
            H.Check("KeyedListDiagnostics_DuplicateSiblingKeysReportedOnMount", mountDup.Count == 1);
            CheckAttribution("KeyedListDiagnostics_DuplicateSiblingKeys", mountDup.FirstOrDefault(), nameof(DuplicateSiblingsProbe), publish);

            // An update that changes the sibling keys is checked again.
            ReactorDiagnostics.ClearRecentKeyedListWarnings();
            H.ClickButton("dup-bump");
            await Harness.Render();
            var updateDup = ReactorDiagnostics.RecentKeyedListWarnings
                .Where(w => w.Kind == KeyedListDiagnosticKind.DuplicateKey && w.SampleKeys.Contains("again")).ToList();
            Console.WriteLine("# duplicate siblings (update): " + string.Join("; ", updateDup.Select(Describe)));
            H.Check("KeyedListDiagnostics_DuplicateSiblingKeysReportedOnUpdate", updateDup.Count == 1);
            // Negative control: unique sibling keys are not reported.
            H.Check("KeyedListDiagnostics_UniqueSiblingKeysNotReported",
                !ReactorDiagnostics.RecentKeyedListWarnings.Any(w => w.SampleKeys.Contains("unique-a")));
            H.SetContent(null);

            // ── ListView duplicate keys name the component ───────────────
            ReactorDiagnostics.ClearRecentKeyedListWarnings();
            H.CreateHost().Mount(new DuplicateListProbe());
            await Harness.Render();
            await Harness.Render(100);
            var listDup = ReactorDiagnostics.RecentKeyedListWarnings
                .Where(w => w.Kind == KeyedListDiagnosticKind.DuplicateKey && w.SampleKeys.Contains("a")).ToList();
            Console.WriteLine("# duplicate list keys: " + string.Join("; ", listDup.Select(Describe)));
            H.Check("KeyedListDiagnostics_ListViewDuplicateKeysReported", listDup.Count == 1);
            CheckAttribution("KeyedListDiagnostics_ListViewDuplicateKeys", listDup.FirstOrDefault(), nameof(DuplicateListProbe), publish);
            H.SetContent(null);

            // ── ItemsView: mount and update report one entry ─────────────
            // Its seed (at mount) and its diff (on update) must name the same control, or one bad
            // list shows up as two diagnostic entries. A null key is reported too.
            ReactorDiagnostics.ClearRecentKeyedListWarnings();
            H.CreateHost().Mount(new ItemsViewKeysProbe());
            await Harness.Render();
            await Harness.Render(100);
            H.ClickButton("iv-bump");
            await Harness.Render();
            await Harness.Render(100);
            var ivWarnings = ReactorDiagnostics.RecentKeyedListWarnings.Where(w => w.ControlContext == "ItemsView").ToList();
            Console.WriteLine("# items view: " + string.Join("; ", ivWarnings.Select(w => Describe(w) + $" x{w.Count}")));
            var ivDup = ivWarnings.Where(w => w.Kind == KeyedListDiagnosticKind.DuplicateKey).ToList();
            H.Check("KeyedListDiagnostics_ItemsViewDuplicateKeys_OneEntryAcrossMountAndUpdate",
                ivDup.Count == 1 && ivDup[0].Count == 2 && ivDup[0].SampleKeys.SequenceEqual(["dup"]));
            H.Check("KeyedListDiagnostics_ItemsViewNullKeyReported",
                ivWarnings.Count(w => w.Kind == KeyedListDiagnosticKind.NullKey) == 1
                && ivWarnings.First(w => w.Kind == KeyedListDiagnosticKind.NullKey).Count == 2);
            H.SetContent(null);

            // ── FlipView pages stay positional ───────────────────────────
            // The stamped key is for diagnostics only: prepending an item updates the existing
            // pages in place (as before) instead of remounting them, so a controlled FlipView
            // does not report transient selection changes.
            FlipPrependProbe.SelectionChanges = 0;
            H.CreateHost().Mount(new FlipPrependProbe());
            await Harness.Render();
            await Harness.Render(100);
            var firstPage = H.FindControl<WinUI.TextBlock>(t => t.Text == "flip page a");
            FlipPrependProbe.SelectionChanges = 0;
            H.ClickButton("flip-prepend");
            await Harness.Render();
            await Harness.Render(100);
            var samePage = H.FindControl<WinUI.TextBlock>(t => t.Text == "flip page z");
            Console.WriteLine($"# flip prepend: selection changes={FlipPrependProbe.SelectionChanges}");
            H.Check("KeyedListDiagnostics_FlipViewPrependUpdatesInPlace",
                firstPage is not null && ReferenceEquals(firstPage, samePage));
            H.Check("KeyedListDiagnostics_FlipViewPrependNoSelectionCallback", FlipPrependProbe.SelectionChanges == 0);
            H.Check("KeyedListDiagnostics_FlipViewPrependRowKeyFollowsItem",
                samePage is not null && Reconciler.GetElementTag(samePage)?.Key == "z");
            H.SetContent(null);

            // A different item at the same position that renders identical content: the update
            // skips, and the page must still take the new item's key (tag and published value).
            H.CreateHost().Mount(new FlipSameContentProbe());
            await Harness.Render();
            await Harness.Render(100);
            H.ClickButton("flip-replace");
            await Harness.Render();
            await Harness.Render(100);
            var sameContent = H.FindControl<WinUI.TextBlock>(t => t.Text == "same page");
            var sameValue = sameContent is null ? null : ReactorDiagnostics.GetSource(sameContent);
            Console.WriteLine($"# flip same content: key={(sameContent is null ? null : Reconciler.GetElementTag(sameContent)?.Key)} value={sameValue}");
            H.Check("KeyedListDiagnostics_FlipViewSameContentKeyRefreshed",
                sameContent is not null && Reconciler.GetElementTag(sameContent)?.Key == "next");
            if (publish)
                H.Check("KeyedListDiagnostics_FlipViewSameContentSourceRefreshed",
                    sameValue?.Contains("|key=next", StringComparison.Ordinal) == true);
            H.SetContent(null);
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
            ReactorDiagnostics.ClearRecentKeyedListWarnings();
        }
    }

    private static string Describe(KeyedListDiagnostic w)
        => $"{w.Kind} [{string.Join(",", w.SampleKeys)}] ctx={w.ControlContext} component={w.ComponentName} at={w.CallSite?.LineNumber}";

    private void CheckAttribution(string name, KeyedListDiagnostic? warning, string component, bool publish)
    {
        if (warning is null)
        {
            H.Check(name + "_Attributed", false, "no warning");
            return;
        }
        if (publish)
            H.Check(name + "_NamesComponent", warning.ComponentName == component, warning.ComponentName ?? "<null>");
        else
            H.Skip(name + "_NamesComponent", "Reactor.DevtoolsSupport is off in this host");
        if (TextBlock("call-site probe").CallSite is null)
            H.Skip(name + "_NamesCallSite", "call sites are not stamped in this host");
        else
            H.Check(name + "_NamesCallSite",
                warning.CallSite is { } site && site.FilePath.EndsWith("KeyedListDiagnosticsFixtures.cs", StringComparison.OrdinalIgnoreCase),
                warning.CallSite?.FilePath ?? "<null>");
    }
}

internal sealed record KeyedRow(string Key, string Label);

internal sealed class KeyedRowsProbe : Component
{
    private static readonly KeyedRow[] Rows = [new("key-a", "a"), new("key-b", "b")];

    public override Element Render() => VStack(4,
        ListView(Rows, static r => r.Key, static (r, _) => TextBlock("lv-" + r.Label)).Height(120),
        ListView(new[] { new KeyedRow("key-own", "own") }, static r => r.Key,
            static (r, _) => TextBlock("lv-own").WithKey("own")).Height(60),
        GridView(Rows, static r => r.Key, static (r, _) => TextBlock("gv-" + r.Label)).Height(120),
        FlipView(Rows, static r => r.Key, static (r, _) => TextBlock("fv-" + r.Label)).Height(60),
        LazyVStack(Rows, static r => r.Key, static (r, _) => TextBlock("lazy-" + r.Label)).Height(120));
}

internal sealed record MaybeKeyed(string? Key, string Text);

internal sealed class NullKeyLazyProbe : Component
{
    private static readonly MaybeKeyed[] Items = [new("k1", "one"), new(null, "nokey"), new("k3", "three")];

    public override Element Render()
    {
        var (tick, setTick) = UseState(0);
        // A fresh list each render, so the update runs the keyed diff.
        var items = Items.ToArray();
        return VStack(4,
            TextBlock($"null-key tick {tick}"),
            Button("null-key-bump", () => setTick(tick + 1)),
            LazyVStack(items, static m => m.Key!, static (m, _) => TextBlock("null-key row " + m.Text)).Height(120),
            ListView(items, static m => m.Key!, static (m, _) => TextBlock("null-key lv " + m.Text)).Height(120));
    }
}

internal sealed class ItemsViewKeysProbe : Component
{
    public override Element Render()
    {
        var (tick, setTick) = UseState(0);
        // A duplicated key and a null key; the update changes the list so its diff runs.
        MaybeKeyed[] items = tick == 0
            ? [new("dup", "one"), new("dup", "two"), new(null, "three")]
            : [new("dup", "one"), new("dup", "two"), new(null, "three"), new("four", "four")];
        return VStack(4,
            Button("iv-bump", () => setTick(tick + 1)),
            ItemsView(items, keySelector: static m => m.Key!, viewBuilder: static (m, _) => ItemContainer(TextBlock("iv " + m.Text)))
                .Height(160));
    }
}

internal sealed class FlipSameContentProbe : Component
{
    public override Element Render()
    {
        var (items, setItems) = UseState<string[]>(["first"]);
        return VStack(4,
            Button("flip-replace", () => setItems(["next"])),
            FlipView(items, static s => s, static (_, _) => TextBlock("same page")).Height(60));
    }
}

internal sealed class FlipPrependProbe : Component
{
    internal static int SelectionChanges;

    public override Element Render()
    {
        var (items, setItems) = UseState<string[]>(["a", "b"]);
        return VStack(4,
            Button("flip-prepend", () => setItems(["z", .. items])),
            FlipView(items, static s => s, static (s, i) => TextBlock(i == 0 ? "flip page " + s : "flip other " + s)) with
            {
                SelectedIndex = 0,
                OnSelectedIndexChanged = _ => SelectionChanges++,
            });
    }
}

internal sealed class DuplicateSiblingsProbe : Component
{
    public override Element Render()
    {
        var (tick, setTick) = UseState(0);
        var dupKey = tick == 0 ? "same" : "again";
        return VStack(2,
            TextBlock("dupA").WithKey(dupKey),
            TextBlock($"dupB {tick}").WithKey(dupKey),
            TextBlock("unique sibling a").WithKey("unique-a"),
            TextBlock("unkeyed sibling"),
            Button("dup-bump", () => setTick(tick + 1)));
    }
}

internal sealed class DuplicateListProbe : Component
{
    private static readonly string[] Items = ["a", "b", "a"];

    public override Element Render()
        => ListView(Items, static s => s, static (s, i) => TextBlock($"dup row {s} #{i}")).Height(120);
}

/// <summary>
/// Items of a <c>MenuFlyout</c> and a <c>CommandBarFlyout</c> are created by the flyout, not
/// mounted from elements, so they had no Reactor identity. In diagnostics mode each now
/// publishes its <c>ReactorSource</c> value: the <c>MenuItem(…)</c> / <c>AppBarButton(…)</c>
/// call site, the owning component and the item kind.
/// </summary>
internal class FlyoutItemIdentity_Published(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("FlyoutItemIdentity", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var previous = ReactorSourcePublisher.IsEnabled;
        ReactorSourcePublisher.IsEnabled = true;
        try
        {
            H.CreateHost().Mount(new FlyoutItemsProbe());
            await Harness.Render();

            var menuTarget = H.FindControl<WinUI.Button>(b => b.Content as string == "Menu target");
            var commandTarget = H.FindControl<WinUI.Button>(b => b.Content as string == "Commands");
            var menu = menuTarget?.Flyout as WinUI.MenuFlyout;
            var commands = commandTarget?.Flyout as WinUI.CommandBarFlyout;
            H.Check("FlyoutItemIdentity_FlyoutsFound", menu is not null && commands is not null);
            if (menu is null || commands is null) return;

            var alpha = menu.Items.OfType<WinUI.MenuFlyoutItem>().FirstOrDefault(i => i.Text == "Alpha");
            var cut = commands.PrimaryCommands.OfType<WinUI.AppBarButton>().FirstOrDefault(b => b.Label == "Cut");
            var alphaValue = alpha is null ? null : ReactorDiagnostics.GetSource(alpha);
            var cutValue = cut is null ? null : ReactorDiagnostics.GetSource(cut);
            Console.WriteLine($"# Alpha: {alphaValue}");
            Console.WriteLine($"# Cut: {cutValue}");

            H.Check("FlyoutItemIdentity_MenuItemPublished",
                alphaValue?.Contains("|element=MenuFlyoutItem", StringComparison.Ordinal) == true
                && alphaValue.Contains("|owner=" + nameof(FlyoutItemsProbe), StringComparison.Ordinal));
            H.Check("FlyoutItemIdentity_AppBarButtonPublished",
                cutValue?.Contains("|element=AppBarButton", StringComparison.Ordinal) == true
                && cutValue.Contains("|owner=" + nameof(FlyoutItemsProbe), StringComparison.Ordinal));

            if (alphaValue?.Contains("|at=", StringComparison.Ordinal) != true)
            {
                H.Skip("FlyoutItemIdentity_CallSite", "call sites are not stamped in this host");
            }
            else
            {
                var alphaSite = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(alpha!);
                var cutSite = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(cut!);
                H.Check("FlyoutItemIdentity_MenuItemCallSite", alphaSite?.LineNumber == FlyoutItemsProbe.AlphaLine,
                    $"{alphaSite?.LineNumber} vs {FlyoutItemsProbe.AlphaLine}");
                H.Check("FlyoutItemIdentity_AppBarButtonCallSite", cutSite?.LineNumber == FlyoutItemsProbe.CutLine,
                    $"{cutSite?.LineNumber} vs {FlyoutItemsProbe.CutLine}");
            }

            // MenuBar and CommandBar items (and a submenu's children) are built the same way.
            var menuBar = H.FindControl<WinUI.MenuBar>(_ => true);
            var fileMenu = menuBar?.Items.FirstOrDefault();
            var open = fileMenu?.Items.OfType<WinUI.MenuFlyoutItem>().FirstOrDefault(i => i.Text == "Open");
            var nested = fileMenu?.Items.OfType<WinUI.MenuFlyoutSubItem>().FirstOrDefault()?.Items
                .OfType<WinUI.MenuFlyoutItem>().FirstOrDefault(i => i.Text == "Nested");
            var commandBar = H.FindControl<WinUI.CommandBar>(_ => true);
            var save = commandBar?.PrimaryCommands.OfType<WinUI.AppBarButton>().FirstOrDefault(b => b.Label == "Save");
            var help = commandBar?.SecondaryCommands.OfType<WinUI.AppBarButton>().FirstOrDefault(b => b.Label == "Help");
            bool Owned(UIElement? item, string kind)
                => item is not null && ReactorDiagnostics.GetSource(item) is { } v
                    && v.Contains("|element=" + kind, StringComparison.Ordinal)
                    && v.Contains("|owner=" + nameof(FlyoutItemsProbe), StringComparison.Ordinal);
            H.Check("FlyoutItemIdentity_MenuBarItemPublished", Owned(open, "MenuFlyoutItem"));
            H.Check("FlyoutItemIdentity_SubMenuChildPublished", Owned(nested, "MenuFlyoutItem"));
            H.Check("FlyoutItemIdentity_CommandBarPrimaryPublished", Owned(save, "AppBarButton"));
            H.Check("FlyoutItemIdentity_CommandBarSecondaryPublished", Owned(help, "AppBarButton"));

            // An item patched in place by an update republishes: after the swap, the first
            // menu-bar slot holds the "Close" item from another call site (same type, so the
            // control is reused), and the second the toggle item (a different type, replaced).
            var firstSlot = fileMenu?.Items.FirstOrDefault();
            var beforeSwap = firstSlot is null ? null : ReactorDiagnostics.GetSource(firstSlot);
            H.ClickButton("menu-swap");
            await Harness.Render();
            var swapped = fileMenu?.Items.FirstOrDefault();
            var afterSwap = swapped is null ? null : ReactorDiagnostics.GetSource(swapped);
            var replaced = fileMenu?.Items.Skip(1).FirstOrDefault();
            Console.WriteLine($"# swap: {beforeSwap} -> {afterSwap}; replaced: {(replaced is null ? null : ReactorDiagnostics.GetSource(replaced))}");
            H.Check("FlyoutItemIdentity_InPlacePatch_SameControl",
                swapped is WinUI.MenuFlyoutItem { Text: "Close" } && ReferenceEquals(firstSlot, swapped));
            if (beforeSwap?.Contains("|at=", StringComparison.Ordinal) == true)
            {
                H.Check("FlyoutItemIdentity_InPlacePatch_Republished",
                    afterSwap is not null && afterSwap != beforeSwap
                    && Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(swapped!)?.LineNumber == FlyoutItemsProbe.CloseLine);
            }
            H.Check("FlyoutItemIdentity_ReplacedItemPublished", Owned(replaced, "ToggleMenuFlyoutItem"));

            // Two item call sites whose published at= text is identical (the same file name, line
            // and column outside the project, so both publish just the file name): GetSource must
            // still name each item's own file, not the first one published with that text.
            H.SetContent(null);
            H.CreateHost().Mount(new AmbiguousItemSitesProbe());
            await Harness.Render();
            var ambiguousTarget = H.FindControl<WinUI.Button>(b => b.Content as string == "Ambiguous items");
            var ambiguousItems = (ambiguousTarget?.Flyout as WinUI.MenuFlyout)?.Items.OfType<WinUI.MenuFlyoutItem>().ToList();
            var firstSite = ambiguousItems is { Count: 2 } ? Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(ambiguousItems[0]) : null;
            var secondSite = ambiguousItems is { Count: 2 } ? Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(ambiguousItems[1]) : null;
            Console.WriteLine($"# ambiguous item sites: {firstSite?.FilePath} | {secondSite?.FilePath}");
            H.Check("FlyoutItemIdentity_AmbiguousPublishedText_ExactSites",
                firstSite?.FilePath == AmbiguousItemSitesProbe.First.FilePath
                && secondSite?.FilePath == AmbiguousItemSitesProbe.Second.FilePath);
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
            H.SetContent(null);
        }
    }
}

internal sealed class AmbiguousItemSitesProbe : Component
{
    internal static readonly SourceLocation First = new(@"Z:\reactor-selftest-a\Ambiguous.cs", 10, 5);
    internal static readonly SourceLocation Second = new(@"Z:\reactor-selftest-b\Ambiguous.cs", 10, 5);

    public override Element Render()
        => MenuFlyout(Button("Ambiguous items"),
            MenuItem("First") with { CallSite = First },
            MenuItem("Second") with { CallSite = Second });
}

internal sealed class FlyoutItemsProbe : Component
{
    internal static int AlphaLine;
    internal static int CutLine;
    internal static int CloseLine;

    private static int Line([global::System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;

    public override Element Render()
    {
        var (last, setLast) = UseState("(none)");
        var (swapped, setSwapped) = UseState(false);
        var alpha = MenuItem("Alpha", () => setLast("Alpha")); AlphaLine = Line();
        var cut = AppBarButton("Cut", () => setLast("Cut"), icon: "Cut"); CutLine = Line();
        var close = MenuItem("Close", () => setLast("Close")); CloseLine = Line();
        MenuFlyoutItemBase[] fileItems = swapped
            ? [close, ToggleMenuItem("Wrap"), MenuSubItem("More", MenuItem("Nested"))]
            : [MenuItem("Open", () => setLast("Open")), MenuItem("Recent"), MenuSubItem("More", MenuItem("Nested"))];
        return VStack(4,
            TextBlock("last: " + last),
            Button("menu-swap", () => setSwapped(true)),
            MenuBar(Menu("File", fileItems)),
            CommandBar([AppBarButton("Save", () => setLast("Save"))], [AppBarButton("Help", () => setLast("Help"))]),
            MenuFlyout(Button("Menu target"), alpha, MenuItem("Beta", () => setLast("Beta"))),
            CommandBarFlyout(Button("Commands"), primaryCommands: [cut, AppBarButton("Copy", () => setLast("Copy"))]));
    }
}
