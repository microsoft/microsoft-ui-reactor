using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// <see cref="ReactorDiagnostics.SourceProperty"/> on live controls: every realized control
/// carries it in diagnostics mode (not only the ones Reactor tags), component wrappers say
/// which component they mount, children name their owner, keys and the root are reported,
/// and nothing is written when publishing is off.
/// </summary>
internal class ReactorSource_PublishedOnEveryControl(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_Plain", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var previous = ReactorSourcePublisher.IsEnabled;
        try
        {
            // ── Off: nothing published ───────────────────────────────────
            ReactorSourcePublisher.IsEnabled = false;
            var offHost = H.CreateHost();
            offHost.Mount(_ => TextBlock("source-off"));
            await Harness.Render();
            var off = H.FindControl<WinUI.TextBlock>(t => t.Text == "source-off");
            H.Check("ReactorSource_OffWritesNothing", off is not null && ReactorDiagnostics.GetSource(off) is null);

            // ── On ───────────────────────────────────────────────────────
            ReactorSourcePublisher.IsEnabled = true;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (n, setN) = ctx.UseState(0);
                var namedLabel = TextBlock("source-named");
                return VStack(4,
                    TextBlock("source-plain"),
                    TextBlock("source-keyed").WithKey("k|1"),
                    namedLabel,
                    Component<FlipProbe, int>(n),
                    Component<SourceProbe, int>(n),
                    Component<HookProbe>(),
                    Button("source-bump", () => setN(n + 1)));
            });
            await Harness.Render();

            string? Of(DependencyObject? d) => d is null ? null : ReactorDiagnostics.GetSource(d);

            // Source-map static info reaches the live value: the declared name, and the
            // component wrapper's hook names (slot:variable@line).
            var namedValue = Of(H.FindControl<WinUI.TextBlock>(t => t.Text == "source-named"));
            var hookText = H.FindControl<WinUI.TextBlock>(t => t.Text.StartsWith("hooks ", StringComparison.Ordinal));
            var hookWrapperValue = Of(hookText is null ? null : VisualTreeHelper.GetParent(hookText));
            Console.WriteLine($"# named: {namedValue}");
            Console.WriteLine($"# hook wrapper: {hookWrapperValue}");
            if (namedValue?.Contains("|at=", StringComparison.Ordinal) != true)
            {
                H.Skip("ReactorSource_DeclaredNameAndHooks", "call sites are not stamped in this host");
            }
            else
            {
                H.Check("ReactorSource_DeclaredNamePublished",
                    namedValue.Contains("|name=namedLabel", StringComparison.Ordinal));
                H.Check("ReactorSource_HookNamesPublished",
                    hookWrapperValue?.Contains("|mounts=HookProbe", StringComparison.Ordinal) == true
                    && hookWrapperValue.Contains($"|hooks=0:clicks@{HookProbe.ClicksLine};1:label@{HookProbe.LabelLine}", StringComparison.Ordinal));
            }

            var plain = H.FindControl<WinUI.TextBlock>(t => t.Text == "source-plain");
            var plainValue = Of(plain);
            Console.WriteLine($"# plain: {plainValue}");
            // An untagged display leaf (no callbacks, key or extras) is published too.
            H.Check("ReactorSource_Plain",
                plainValue is not null && plainValue.StartsWith("v=1|", StringComparison.Ordinal)
                && plainValue.Contains("|element=TextBlock", StringComparison.Ordinal)
                && plainValue.Contains("|owner=FuncElement", StringComparison.Ordinal));

            var keyedValue = Of(H.FindControl<WinUI.TextBlock>(t => t.Text == "source-keyed"));
            Console.WriteLine($"# keyed: {keyedValue}");
            H.Check("ReactorSource_KeyEscaped", keyedValue?.Contains("|key=k%7C1", StringComparison.Ordinal) == true);

            var probeText = H.FindControl<WinUI.TextBlock>(t => t.Text.StartsWith("probe ", StringComparison.Ordinal));
            var probeValue = Of(probeText);
            var wrapperValue = Of(probeText is null ? null : VisualTreeHelper.GetParent(probeText));
            Console.WriteLine($"# probe child: {probeValue}");
            Console.WriteLine($"# probe wrapper: {wrapperValue}");
            H.Check("ReactorSource_ChildOwnedByComponent", probeValue?.Contains("|owner=SourceProbe", StringComparison.Ordinal) == true);
            H.Check("ReactorSource_WrapperMountsComponent",
                wrapperValue?.Contains("|element=Component", StringComparison.Ordinal) == true
                && wrapperValue.Contains("|mounts=SourceProbe", StringComparison.Ordinal)
                && wrapperValue.Contains("|owner=FuncElement", StringComparison.Ordinal));

            var rootValue = Of(plain is null ? null : VisualTreeHelper.GetParent(plain));
            Console.WriteLine($"# root: {rootValue}");
            H.Check("ReactorSource_RootNamed", rootValue?.Contains("|root=FuncElement", StringComparison.Ordinal) == true);

            // ── Re-render keeps every control described ─────────────────
            H.ClickButton("source-bump");
            await Harness.Render();
            var afterText = H.FindControl<WinUI.TextBlock>(t => t.Text == "probe 1");
            H.Check("ReactorSource_UpdatedChildStillDescribed",
                Of(afterText)?.Contains("|owner=SourceProbe", StringComparison.Ordinal) == true);

            // ── A skipped element whose call site moved ─────────────────
            // "source-flip" is equal on every render except for its call site, so the
            // reconciler skips it and only re-publishes, with the owner of the render it is in.
            // (The publisher used to recover that owner from a side table keyed by the managed
            // wrapper, which a GC can collect and re-create; this host happens to keep the wrapper
            // alive, so the GC below is belt and braces, not a reproduction.)
            var flip = H.FindControl<WinUI.TextBlock>(t => t.Text == "source-flip");
            var flipBefore = Of(flip);
            Console.WriteLine($"# flip before: {flipBefore}");
            if (flipBefore?.Contains("|at=", StringComparison.Ordinal) != true)
            {
                H.Skip("ReactorSource_SkipRepublishKeepsOwner", "call sites are not stamped in this host");
            }
            else
            {
                flip = null;
                for (int i = 0; i < 2; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                H.ClickButton("source-bump");
                await Harness.Render();
                var flipAfter = Of(H.FindControl<WinUI.TextBlock>(t => t.Text == "source-flip"));
                Console.WriteLine($"# flip after: {flipAfter}");
                H.Check("ReactorSource_SkipRepublishKeepsOwner",
                    flipAfter is not null && flipAfter != flipBefore
                    && flipAfter.Contains("|owner=FlipProbe", StringComparison.Ordinal));
            }
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
        }
    }
}

/// <summary>Two named hooks, for the published <c>hooks=</c> field.</summary>
internal sealed class HookProbe : Component
{
    internal static int ClicksLine;
    internal static int LabelLine;

    public override Element Render()
    {
        var (clicks, _) = UseState(0); ClicksLine = Line();
        var label = UseRef("hooked"); LabelLine = Line();
        return TextBlock($"hooks {clicks} {label.Current}");
    }

    private static int Line([global::System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;
}
/// <summary>Renders an element that differs between renders only in its call site.</summary>
internal sealed class FlipProbe : Component<int>
{
    public override Element Render() => Props % 2 == 0
        ? TextBlock("source-flip")
        : TextBlock("source-flip");
}

internal sealed class SourceProbe : Component<int>
{
    public override Element Render() => TextBlock($"probe {Props}");
}

/// <summary>
/// Native AOT in diagnostics mode skips the element tag whose only purpose is the call site
/// (no managed agent can load to read it) and resolves <c>ReactorSourceMap.GetSource</c> from
/// the published value instead. Runs the same tree through BOTH paths in this JIT host (the
/// AOT decision is forced with <c>ReactorSourcePublisher.NoManagedAgent</c>) and requires
/// <c>GetSource</c> to agree on every control, after mount and after a re-render that moves
/// call sites; and requires the skip to have really happened (fewer tags, and controls that
/// resolve their location without one), so the comparison is not vacuous.
/// </summary>
internal class ReactorSource_AotTagSkipKeepsGetSource(Harness h) : SelfTestFixtureBase(h)
{
    private static readonly int[] Rows = [0, 1, 2, 3, 4];

    private static Element Tree(RenderContext ctx)
    {
        var (n, setN) = ctx.UseState(0);
        bool odd = n % 2 == 1;
        return VStack(4,
            TextBlock("aot-plain"),
            odd ? null : ProgressRing(),
            odd ? TextBlock("aot-flip") : TextBlock("aot-flip"),
            Border(TextBlock("aot-in-border")).Margin(4),
            TextBlock("aot-keyed").WithKey("k|1"),
            TextBlock("aot-attached").Grid(row: 0, column: 0),
            Component<SourceProbe, int>(n),
            Component<FlipProbe, int>(n),
            Flyout(Button("aot-flyout-target"), TextBlock("aot-flyout-body")),
            Button("aot-bump", () => setN(n + 1)),
            LazyVStack(Rows, static i => i.ToString(global::System.Globalization.CultureInfo.InvariantCulture),
                (i, _) => odd
                    ? HStack(TextBlock($"aot-row {i}"), TextBlock("aot-cell"))
                    : HStack(TextBlock($"aot-row {i}"), TextBlock("aot-cell"))).Height(200));
    }

    private sealed record Snapshot(List<(string Type, SourceLocation? Source)> Controls, int Tagged, int ResolvedUntagged);

    private Snapshot Take()
    {
        var all = H.FindAllControls<DependencyObject>(d => d is UIElement);
        var list = new List<(string, SourceLocation?)>(all.Count);
        int tagged = 0, resolvedUntagged = 0;
        foreach (var d in all)
        {
            var ui = (UIElement)d;
            var source = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(ui);
            list.Add((d.GetType().Name, source));
            if (Reconciler.GetElementTag(ui) is not null) tagged++;
            else if (source is not null) resolvedUntagged++;
        }
        return new Snapshot(list, tagged, resolvedUntagged);
    }

    private async Task<(Snapshot Mounted, Snapshot Rerendered, SourceLocation? Removed, SourceLocation? RemovedBefore)> RunPath(bool noManagedAgent)
    {
        ReactorSourcePublisher.NoManagedAgent = noManagedAgent;
        var host = H.CreateHost();
        host.Mount(Tree);
        await Harness.Render();
        await Harness.Render();
        var mounted = Take();
        // The re-render removes the only ProgressRing, which goes back to the pool: a pooled
        // control must stop answering for the element it hosted, in both paths.
        var gone = H.FindControl<WinUI.ProgressRing>(_ => true);
        var goneBefore = gone is null ? null : Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(gone);
        H.ClickButton("aot-bump");
        await Harness.Render();
        await Harness.Render();
        var rerendered = Take();
        var removed = gone is null ? null : Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(gone);
        host.Dispose();
        H.SetContent(null);
        return (mounted, rerendered, removed, goneBefore);
    }

    private static int Mismatches(Snapshot a, Snapshot b, string label)
    {
        if (a.Controls.Count != b.Controls.Count)
        {
            Console.WriteLine($"# {label}: control count {a.Controls.Count} vs {b.Controls.Count}");
            return int.MaxValue;
        }
        int bad = 0;
        for (int i = 0; i < a.Controls.Count; i++)
        {
            if (a.Controls[i] == b.Controls[i]) continue;
            if (bad++ < 5)
                Console.WriteLine($"# {label} #{i}: tagged {a.Controls[i].Type} {a.Controls[i].Source} vs skipped {b.Controls[i].Type} {b.Controls[i].Source}");
        }
        return bad;
    }

    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_AotSkip_GetSourceUnchanged", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var (enabled, noAgent, mapped) = (ReactorSourcePublisher.IsEnabled, ReactorSourcePublisher.NoManagedAgent,
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);
        try
        {
            ReactorSourcePublisher.IsEnabled = true;
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = true;

            var tagged = await RunPath(noManagedAgent: false);
            var skipped = await RunPath(noManagedAgent: true);

            int located = tagged.Mounted.Controls.Count(c => c.Source is not null);
            Console.WriteLine($"# controls={tagged.Mounted.Controls.Count} located={located} " +
                $"tagged: {tagged.Mounted.Tagged}/{tagged.Rerendered.Tagged}, skipped-path tagged: {skipped.Mounted.Tagged}/{skipped.Rerendered.Tagged}, " +
                $"resolved without a tag: {skipped.Mounted.ResolvedUntagged}/{skipped.Rerendered.ResolvedUntagged}");

            if (located == 0)
            {
                H.Skip("ReactorSource_AotSkip_GetSourceUnchanged", "call sites are not stamped in this host");
                return;
            }

            H.Check("ReactorSource_AotSkip_GetSourceUnchanged_Mounted", Mismatches(tagged.Mounted, skipped.Mounted, "mounted") == 0);
            H.Check("ReactorSource_AotSkip_GetSourceUnchanged_Rerendered", Mismatches(tagged.Rerendered, skipped.Rerendered, "rerendered") == 0);
            Console.WriteLine($"# removed control: tagged {tagged.RemovedBefore} -> {tagged.Removed}; skipped {skipped.RemovedBefore} -> {skipped.Removed}");
            H.Check("ReactorSource_AotSkip_RemovedControlAgrees",
                tagged.RemovedBefore is not null && skipped.RemovedBefore is not null && tagged.Removed is null && skipped.Removed is null);
            // The re-render moved call sites, so a stale location would differ from the mount's.
            H.Check("ReactorSource_AotSkip_RerenderMovedCallSites",
                !tagged.Mounted.Controls.SequenceEqual(tagged.Rerendered.Controls));
            // Non-vacuous: the skip happened, and the fallback carried real locations.
            H.Check("ReactorSource_AotSkip_FewerTags",
                skipped.Mounted.Tagged < tagged.Mounted.Tagged && skipped.Rerendered.Tagged < tagged.Rerendered.Tagged);
            H.Check("ReactorSource_AotSkip_FallbackResolves",
                skipped.Mounted.ResolvedUntagged > 0 && skipped.Rerendered.ResolvedUntagged > 0
                && tagged.Mounted.ResolvedUntagged == 0);
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = enabled;
            ReactorSourcePublisher.NoManagedAgent = noAgent;
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = mapped;
        }
    }
}

/// <summary>
/// ItemsRepeater row adoption (a realized row's key changes, so a fresh component subtree is
/// moved into the still-parented wrapper) must leave the live wrapper describing the element
/// it now hosts: its published value carries the NEW key.
/// </summary>
internal class ReactorSource_AdoptedRowRepublished(Harness h) : SelfTestFixtureBase(h)
{
    private static readonly string[] Ids = ["a"];

    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_AdoptedRow", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var previous = ReactorSourcePublisher.IsEnabled;
        try
        {
            ReactorSourcePublisher.IsEnabled = true;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (rev, setRev) = ctx.UseState(0);
                return VStack(
                    Button("adopt-bump", () => setRev(rev + 1)),
                    LazyVStack(Ids, static id => id, (id, _) =>
                        Component<SourceProbe, int>(rev).WithKey($"{id}:{rev}")).Height(200));
            });
            await Harness.Render();
            await Harness.Render();

            var repeater = H.FindControl<Microsoft.UI.Xaml.Controls.ItemsRepeater>(_ => true);
            var before = repeater?.TryGetElement(0);
            var beforeValue = before is null ? null : ReactorDiagnostics.GetSource(before);
            Console.WriteLine($"# row before: {beforeValue}");
            H.Check("ReactorSource_AdoptedRow_InitialKey", beforeValue?.Contains("|key=a:0", StringComparison.Ordinal) == true);

            H.ClickButton("adopt-bump");
            await Harness.Render();
            await Harness.Render();

            var after = repeater?.TryGetElement(0);
            var afterValue = after is null ? null : ReactorDiagnostics.GetSource(after);
            Console.WriteLine($"# row after: {afterValue} (same wrapper: {ReferenceEquals(before, after)})");
            H.Check("ReactorSource_AdoptedRow_WrapperKept", before is not null && ReferenceEquals(before, after));
            H.Check("ReactorSource_AdoptedRow_NewKeyPublished",
                afterValue?.Contains("|key=a:1", StringComparison.Ordinal) == true
                && afterValue.Contains("|mounts=SourceProbe", StringComparison.Ordinal));
            host.Dispose();
            H.SetContent(null);
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
        }
    }
}

/// <summary>
/// Rows an ItemsRepeater realizes or reuses during layout (scrolling) are reconciled outside
/// any render, so their owner is unknown and <c>owner=</c> is omitted, rather than reported
/// as the host's root (the reuse path runs <c>Reconcile</c>, which used to pick up a stale
/// root owner).
/// </summary>
internal class ReactorSource_LayoutRealizedRowsHaveNoOwner(Harness h) : SelfTestFixtureBase(h)
{
    private static readonly int[] Rows = Enumerable.Range(0, 300).ToArray();

    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_LayoutRows", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var previous = ReactorSourcePublisher.IsEnabled;
        try
        {
            ReactorSourcePublisher.IsEnabled = true;
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                TextBlock("layout-rows-header"),
                LazyVStack(Rows, static i => i.ToString(global::System.Globalization.CultureInfo.InvariantCulture),
                    (i, _) => Component<SourceProbe, int>(i).WithKey($"row-{i}")).Height(150)));
            await Harness.Render();
            await Harness.Render();

            var header = ReactorDiagnostics.GetSource(H.FindControl<WinUI.TextBlock>(t => t.Text == "layout-rows-header")!);
            H.Check("ReactorSource_LayoutRows_RootOwnerKnownInRender",
                header?.Contains("|owner=FuncElement", StringComparison.Ordinal) == true);

            var viewer = H.FindControl<WinUI.ScrollViewer>(_ => true);
            for (int step = 1; step <= 6 && viewer is not null; step++)
            {
                viewer.ChangeView(null, step * 1200, null, disableAnimation: true);
                await Harness.Render();
                await Harness.Render();
            }

            // Row wrappers (the component Borders) realized or reused while scrolling.
            var rows = H.FindAllControls<WinUI.TextBlock>(t => t.Text.StartsWith("probe ", StringComparison.Ordinal)
                    && int.Parse(t.Text.AsSpan("probe ".Length), global::System.Globalization.CultureInfo.InvariantCulture) >= 40)
                .Select(t => VisualTreeHelper.GetParent(t)).OfType<UIElement>().ToList();
            var withOwner = rows.Select(r => ReactorDiagnostics.GetSource(r)).Where(v => v?.Contains("|owner=", StringComparison.Ordinal) == true).ToList();
            Console.WriteLine($"# scrolled rows: {rows.Count}, with owner: {withOwner.Count}{(withOwner.Count > 0 ? " e.g. " + withOwner[0] : "")}; sample {(rows.Count > 0 ? ReactorDiagnostics.GetSource(rows[^1]) : null)}");
            H.Check("ReactorSource_LayoutRows_Realized", rows.Count > 0);
            H.Check("ReactorSource_LayoutRows_NoStaleRootOwner", rows.Count > 0 && withOwner.Count == 0);
            host.Dispose();
            H.SetContent(null);
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
        }
    }
}

/// <summary>
/// A host root that is a <c>Memo(key, …)</c>: its factory output publishes itself when it
/// mounts, and the host adds <c>root=</c> to that value without re-running the factory.
/// </summary>
internal class ReactorSource_KeyedMemoRootNamed(Harness h) : SelfTestFixtureBase(h)
{
    private static int s_factoryRuns;

    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_KeyedMemoRoot", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var previous = ReactorSourcePublisher.IsEnabled;
        try
        {
            ReactorSourcePublisher.IsEnabled = true;
            s_factoryRuns = 0;
            var host = H.CreateHost();
            host.Mount(_ => Memo(1, () =>
            {
                s_factoryRuns++;
                return TextBlock("memo-root");
            }));
            await Harness.Render();

            var value = ReactorDiagnostics.GetSource(H.FindControl<WinUI.TextBlock>(t => t.Text == "memo-root")!);
            Console.WriteLine($"# memo root: {value} (factory runs: {s_factoryRuns})");
            H.Check("ReactorSource_KeyedMemoRoot_Named",
                value?.Contains("|root=FuncElement", StringComparison.Ordinal) == true
                && value.Contains("|element=TextBlock", StringComparison.Ordinal));
            H.Check("ReactorSource_KeyedMemoRoot_FactoryRanOnce", s_factoryRuns == 1);
            host.Dispose();
            H.SetContent(null);
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
        }
    }
}
