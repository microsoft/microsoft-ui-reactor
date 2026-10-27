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
            H.Check("KeyedListDiagnostics_LazyVStackNullKeyReported", nullKey.Count == 1);
            CheckAttribution("KeyedListDiagnostics_LazyVStackNullKey", nullKey.FirstOrDefault(), nameof(NullKeyLazyProbe), publish);
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

            ReactorDiagnostics.ClearRecentKeyedListWarnings();
            H.ClickButton("dup-bump");
            await Harness.Render();
            var updateDup = ReactorDiagnostics.RecentKeyedListWarnings
                .Where(w => w.Kind == KeyedListDiagnosticKind.DuplicateKey && w.SampleKeys.Contains("same")).ToList();
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
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
            ReactorDiagnostics.ClearRecentKeyedListWarnings();
        }
    }

    private static string Describe(KeyedListDiagnostic w)
        => $"{w.Kind} [{string.Join(",", w.SampleKeys)}] ctx={w.ControlContext} component={w.Component} at={w.CallSite?.LineNumber}";

    private void CheckAttribution(string name, KeyedListDiagnostic? warning, string component, bool publish)
    {
        if (warning is null)
        {
            H.Check(name + "_Attributed", false, "no warning");
            return;
        }
        if (publish)
            H.Check(name + "_NamesComponent", warning.Component == component, warning.Component ?? "<null>");
        else
            H.Skip(name + "_NamesComponent", "Reactor.DevtoolsSupport is off in this host");
        if (warning.CallSite is null && !Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled)
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
        => LazyVStack(Items, static m => m.Key!, static (m, _) => TextBlock("null-key row " + m.Text)).Height(120);
}

internal sealed class DuplicateSiblingsProbe : Component
{
    public override Element Render()
    {
        var (tick, setTick) = UseState(0);
        return VStack(2,
            TextBlock("dupA").WithKey("same"),
            TextBlock($"dupB {tick}").WithKey("same"),
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
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
            H.SetContent(null);
        }
    }
}

internal sealed class FlyoutItemsProbe : Component
{
    internal static int AlphaLine;
    internal static int CutLine;

    private static int Line([global::System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;

    public override Element Render()
    {
        var (last, setLast) = UseState("(none)");
        var alpha = MenuItem("Alpha", () => setLast("Alpha")); AlphaLine = Line();
        var cut = AppBarButton("Cut", () => setLast("Cut"), icon: "Cut"); CutLine = Line();
        return VStack(4,
            TextBlock("last: " + last),
            MenuFlyout(Button("Menu target"), alpha, MenuItem("Beta", () => setLast("Beta"))),
            CommandBarFlyout(Button("Commands"), primaryCommands: [cut, AppBarButton("Copy", () => setLast("Copy"))]));
    }
}
