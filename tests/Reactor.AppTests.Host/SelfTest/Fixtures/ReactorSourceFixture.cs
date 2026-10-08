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
            // A root render function's hooks resolve through the recorded root mount site
            // (the source map keys them by the call the function was passed to).
            if (namedValue?.Contains("|at=", StringComparison.Ordinal) != true)
            {
                H.Skip("ReactorSource_RootRenderFunctionHooks", "call sites are not stamped in this host");
            }
            else
            {
                H.Check("ReactorSource_RootRenderFunctionHooks", rootValue?.Contains("|hooks=0:n@", StringComparison.Ordinal) == true);
            }

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

internal static class RemountView
{
    // Both roots render this, so swapping the root updates the kept content in place and
    // every call site, key and kind below is unchanged.
    public static Element Body() => VStack(
        TextBlock("remount-leaf"),
        Flyout(Button("remount-flyout-target"), TextBlock("remount-flyout-body")),
        // The target is a component (its control publishes mounts=), the body is not its.
        Flyout(Component<RemountFlyoutTarget>(), TextBlock("remount-cflyout-body")),
        // Never opened: the body Button's template is not applied, so its content TextBlock
        // is a logical child only.
        Flyout(Button("remount-nflyout-target"), Button(TextBlock("remount-nflyout-inner"))),
        // A context flyout hangs off the control itself, outside every child walk until opened.
        Button("remount-ctx-target").WithContextFlyout(TextBlock("remount-ctx-body")),
        // Inline UI in a never-opened flyout's RichTextBlock lives in its document blocks only.
        Flyout(Button("remount-rflyout-target"), RichTextBlock([Paragraph(InlineUI(TextBlock("remount-inline-ui")))])),
#pragma warning disable CS0618 // the legacy node-mode ContentElement path still mounts controls
        // Node content under a collapsed TreeView node is mounted but in no child walk. Kept in a
        // keyed Memo so the root swap retains it (an updated TreeView rebuilds its nodes anyway).
        Memo("remount-tree", static () => TreeView(new TreeViewNodeData("remount-tree-root", [
            new TreeViewNodeData("remount-tree-child") { ContentElement = TextBlock("remount-tree-content") },
        ]) { IsExpanded = false })),
#pragma warning restore CS0618
        Component<RemountNested, int>(0));
}

internal sealed class RemountFlyoutTarget : Component
{
    public override Element Render() => TextBlock("remount-cflyout-target");
}

// Keyed-memo roots: the realized output is kept across the swap (same key), and the host adds
// the root's hooks to it, so a swap must replace or drop hooks the previous root added.
internal sealed class MemoRootA : Component
{
    public override Element Render()
    {
        var (a, _) = UseState(0);
        return Memo(1, () => TextBlock("memo-remount"));
    }
}

internal sealed class MemoRootB : Component
{
    public override Element Render() => Memo(1, () => TextBlock("memo-remount"));
}

// Roots whose output is a ContentDialog: the visible dialog must follow the root swap too.
internal static class RemountDialog
{
    public static Element Open() => ContentDialog("RemountDialog", TextBlock("remount-dialog-body"), "OK") with { IsOpen = true };
}

internal sealed class DialogRootA : Component
{
    public override Element Render() => RemountDialog.Open();
}

internal sealed class DialogRootB : Component
{
    public override Element Render() => RemountDialog.Open();
}

internal sealed class MemoDialogRootA : Component
{
    public override Element Render() => Memo(1, RemountDialog.Open);
}

internal sealed class MemoDialogRootB : Component
{
    public override Element Render() => Memo(1, RemountDialog.Open);
}

internal sealed class MemoRootC : Component
{
    public override Element Render()
    {
        var (c, _) = UseState(0);
        var (d, _) = UseState(1);
        return Memo(1, () => TextBlock("memo-remount"));
    }
}

internal sealed class RemountRootA : Component
{
    public override Element Render() => RemountView.Body();
}

internal sealed class RemountRootB : Component
{
    public override Element Render() => RemountView.Body();
}

internal sealed class RemountNested : Component<int>
{
    public override Element Render() => TextBlock("remount-nested");
}

/// <summary>
/// Mounting a different root over kept content (both hosts allow it) must re-attribute the
/// root-owned controls the in-place update did not re-publish, while a nested component's
/// own subtree keeps naming that component.
/// </summary>
internal class ReactorSource_RootRemountRenamesOwner(Harness h) : SelfTestFixtureBase(h)
{
    private string? Source(string text)
        => H.FindControl<WinUI.TextBlock>(t => t.Text == text) is { } tb ? ReactorDiagnostics.GetSource(tb) : null;

    private string? FlyoutBodySource()
        => FlyoutOf(H.FindControl<WinUI.Button>(b => b.Content as string == "remount-flyout-target"))?.Content is WinUI.TextBlock body
            ? ReactorDiagnostics.GetSource(body)
            : null;

    private string? ComponentFlyoutBodySource()
        => FlyoutOf(H.FindControl<WinUI.TextBlock>(t => t.Text == "remount-cflyout-target"))?.Content is WinUI.TextBlock body
            ? ReactorDiagnostics.GetSource(body)
            : null;

    private string? NestedFlyoutInnerSource()
        => FlyoutOf(H.FindControl<WinUI.Button>(b => b.Content as string == "remount-nflyout-target"))?.Content
            is WinUI.Button { Content: WinUI.TextBlock inner }
            ? ReactorDiagnostics.GetSource(inner)
            : null;

    // The flyout is attached to the target's control: the target itself, or (for a component
    // target) the wrapper above the control we can find by text.
    private static WinUI.Flyout? FlyoutOf(DependencyObject? start)
    {
        for (var d = start; d is not null; d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement fe && Reconciler.GetFlyoutOnControl(fe) is WinUI.Flyout flyout)
                return flyout;
        }
        return null;
    }

    private async Task<bool> Owned(string text, string owner)
        => await Harness.WaitFor(
            () => (text switch
            {
                "remount-flyout-body" => FlyoutBodySource(),
                "remount-cflyout-body" => ComponentFlyoutBodySource(),
                "remount-nflyout-inner" => NestedFlyoutInnerSource(),
                "remount-ctx-body" => H.FindControl<WinUI.Button>(b => b.Content as string == "remount-ctx-target")?.ContextFlyout
                    is WinUI.Flyout { Content: WinUI.TextBlock ctxBody } ? ReactorDiagnostics.GetSource(ctxBody) : null,
                "remount-tree-content" => H.FindControl<WinUI.TreeView>(_ => true) is { RootNodes.Count: > 0 } tv
                    && tv.RootNodes[0].Children.FirstOrDefault()?.Content is WinUI.TextBlock treeContent
                    ? ReactorDiagnostics.GetSource(treeContent) : null,
                "remount-inline-ui" => FlyoutOf(H.FindControl<WinUI.Button>(b => b.Content as string == "remount-rflyout-target"))?.Content
                    is WinUI.RichTextBlock rtb
                    && rtb.Blocks.FirstOrDefault() is Microsoft.UI.Xaml.Documents.Paragraph para
                    && para.Inlines.FirstOrDefault() is Microsoft.UI.Xaml.Documents.InlineUIContainer { Child: WinUI.TextBlock inlineUi }
                    ? ReactorDiagnostics.GetSource(inlineUi) : null,
                _ => Source(text),
            })?.Contains($"|owner={owner}|", StringComparison.Ordinal) == true,
            maxPasses: 32, perPassMs: 10);

    private async Task CheckDialogFollowsRootSwap(Component first, Component second, string shape)
    {
        var host = H.CreateHost();
        host.Mount(first);
        var dialog = await ContentDialogProbe.WaitForOpen(H, "RemountDialog", 2_000);
        var before = dialog is null ? null : ReactorDiagnostics.GetSource(dialog);
        host.Mount(second);
        await Harness.Render();
        await Harness.Render();
        var after = dialog is null ? null : ReactorDiagnostics.GetSource(dialog);
        Console.WriteLine($"# dialog root swap ({shape}): {before} -> {after}");
        H.Check($"ReactorSource_RootRemount_Dialog{shape}_FollowsTheRoot",
            before?.Contains($"|root={first.GetType().Name}", StringComparison.Ordinal) == true
            && after?.Contains($"|root={second.GetType().Name}", StringComparison.Ordinal) == true
            && after.Contains("|element=ContentDialog", StringComparison.Ordinal));
        dialog?.Hide();
        await Harness.Render(50);
        host.Dispose();
        H.SetContent(null);
    }

    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_RootRemount", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var previous = ReactorSourcePublisher.IsEnabled;
        try
        {
            ReactorSourcePublisher.IsEnabled = true;

            var host = H.CreateHost();
            host.Mount(new RemountRootA());
            await Harness.Render();
            H.Check("ReactorSource_RootRemount_Host_FirstRoot", await Owned("remount-leaf", nameof(RemountRootA)));
            host.Mount(new RemountRootB());
            await Harness.Render();
            Console.WriteLine($"# remount (ReactorHost): {Source("remount-leaf")} / {Source("remount-nested")}");
            H.Check("ReactorSource_RootRemount_Host_LeafRenamed", await Owned("remount-leaf", nameof(RemountRootB)));
            H.Check("ReactorSource_RootRemount_Host_FlyoutContentRenamed", await Owned("remount-flyout-body", nameof(RemountRootB)));
            H.Check("ReactorSource_RootRemount_Host_ComponentTargetFlyoutRenamed", await Owned("remount-cflyout-body", nameof(RemountRootB)));
            H.Check("ReactorSource_RootRemount_Host_UnopenedNestedContentRenamed", await Owned("remount-nflyout-inner", nameof(RemountRootB)));
            H.Check("ReactorSource_RootRemount_Host_ContextFlyoutRenamed", await Owned("remount-ctx-body", nameof(RemountRootB)));
            H.Check("ReactorSource_RootRemount_Host_UnopenedInlineUIRenamed", await Owned("remount-inline-ui", nameof(RemountRootB)));
            H.Check("ReactorSource_RootRemount_Host_CollapsedTreeNodeContentRenamed", await Owned("remount-tree-content", nameof(RemountRootB)));
            H.Check("ReactorSource_RootRemount_Host_NestedKeepsItsOwner", await Owned("remount-nested", nameof(RemountNested)));
            host.Dispose();
            H.SetContent(null);

            var memoHost = H.CreateHost();
            if (Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetComponentHooks(typeof(MemoRootA)) is null)
            {
                // Hook names come from the source-map generator's static table; a host built
                // without it has no root hooks to add, replace or drop.
                H.Skip("ReactorSource_RootRemount_MemoHooks", "this host has no source-map hook table");
            }
            else
            {
                memoHost.Mount(new MemoRootA());
                await Harness.Render();
                var withA = Source("memo-remount");
                memoHost.Mount(new MemoRootB());
                await Harness.Render();
                var withB = Source("memo-remount");
                memoHost.Mount(new MemoRootC());
                await Harness.Render();
                var withC = Source("memo-remount");
                Console.WriteLine($"# keyed-memo remount: A {withA} / B {withB} / C {withC}");
                H.Check("ReactorSource_RootRemount_MemoHooks_FirstRoot",
                    withA?.Contains("|root=MemoRootA", StringComparison.Ordinal) == true && withA.Contains("|hooks=0:a@", StringComparison.Ordinal));
                H.Check("ReactorSource_RootRemount_MemoHooks_DroppedForAHookFreeRoot",
                    withB?.Contains("|root=MemoRootB", StringComparison.Ordinal) == true && !withB.Contains("|hooks=", StringComparison.Ordinal));
                H.Check("ReactorSource_RootRemount_MemoHooks_ReplacedByTheNewRoots",
                    withC?.Contains("|hooks=0:c@", StringComparison.Ordinal) == true && !withC.Contains("0:a@", StringComparison.Ordinal));
            }
            memoHost.Dispose();
            H.SetContent(null);

            // A keyed-memo root wrapped in modifiers is still a keyed-memo root: its realized
            // output keeps describing itself (element=TextBlock), with the host's root fields.
            var wrappedHost = H.CreateHost();
            wrappedHost.Mount(_ => new ModifiedElement(
                Memo(1, () => TextBlock("wrapped-memo-root")),
                new ElementModifiers { Margin = new Thickness(2) }));
            await Harness.Render();
            var wrapped = Source("wrapped-memo-root");
            Console.WriteLine($"# wrapped keyed-memo root: {wrapped}");
            H.Check("ReactorSource_RootRemount_WrappedKeyedMemoRootKeepsItsOutput",
                wrapped?.Contains("|element=TextBlock", StringComparison.Ordinal) == true
                && wrapped.Contains("|root=FuncElement", StringComparison.Ordinal));
            wrappedHost.Dispose();
            H.SetContent(null);

            await CheckDialogFollowsRootSwap(new DialogRootA(), new DialogRootB(), "Plain");
            await CheckDialogFollowsRootSwap(new MemoDialogRootA(), new MemoDialogRootB(), "KeyedMemo");

            var hostControl = new Microsoft.UI.Reactor.Hosting.ReactorHostControl();
            hostControl.Mount(new RemountRootA());
            H.SetContent(new WinUI.Border { Child = hostControl });
            // A standalone ReactorHostControl is not ReactorApp.ActiveHost: poll its loop.
            H.Check("ReactorSource_RootRemount_HostControl_FirstRoot", await Owned("remount-leaf", nameof(RemountRootA)));
            hostControl.Mount(new RemountRootB());
            H.Check("ReactorSource_RootRemount_HostControl_LeafRenamed", await Owned("remount-leaf", nameof(RemountRootB)));
            H.Check("ReactorSource_RootRemount_HostControl_FlyoutContentRenamed", await Owned("remount-flyout-body", nameof(RemountRootB)));
            H.Check("ReactorSource_RootRemount_HostControl_ComponentTargetFlyoutRenamed", await Owned("remount-cflyout-body", nameof(RemountRootB)));
            H.Check("ReactorSource_RootRemount_HostControl_UnopenedNestedContentRenamed", await Owned("remount-nflyout-inner", nameof(RemountRootB)));
            H.Check("ReactorSource_RootRemount_HostControl_ContextFlyoutRenamed", await Owned("remount-ctx-body", nameof(RemountRootB)));
            H.Check("ReactorSource_RootRemount_HostControl_UnopenedInlineUIRenamed", await Owned("remount-inline-ui", nameof(RemountRootB)));
            H.Check("ReactorSource_RootRemount_HostControl_CollapsedTreeNodeContentRenamed", await Owned("remount-tree-content", nameof(RemountRootB)));
            Console.WriteLine($"# remount (ReactorHostControl): {Source("remount-leaf")} / {Source("remount-nested")}");
            H.Check("ReactorSource_RootRemount_HostControl_NestedKeepsItsOwner", await Owned("remount-nested", nameof(RemountNested)));
            hostControl.Dispose();
            H.SetContent(null);

            // An embedded ReactorHostControl is another host: the walk renames what this root owns
            // around it and leaves the island alone, even when the island's root shares the name.
            const string OldOwnerValue = "v=1|at=Probe.cs:1|owner=RemountRootA|element=TextBlock";
            var outerLeaf = new WinUI.TextBlock();
            outerLeaf.SetValue(ReactorDiagnostics.SourceProperty, OldOwnerValue);
            var islandLeaf = new WinUI.TextBlock();
            islandLeaf.SetValue(ReactorDiagnostics.SourceProperty, OldOwnerValue);
            using var island = new Microsoft.UI.Reactor.Hosting.ReactorHostControl { Content = islandLeaf };
            var outer = new WinUI.StackPanel();
            outer.Children.Add(outerLeaf);
            outer.Children.Add(island);
            var probeHost = H.CreateHost();
            probeHost.Reconciler.RenameRootOwner(outer, nameof(RemountRootA), nameof(RemountRootB));
            H.Check("ReactorSource_RootRemount_EmbeddedIslandUntouched",
                (ReactorDiagnostics.GetSource(outerLeaf) as string)?.Contains("|owner=RemountRootB|", StringComparison.Ordinal) == true
                && ReactorDiagnostics.GetSource(islandLeaf) == OldOwnerValue);

            // The island can be the root's content itself (a root returning XamlHost(() => island)):
            // the walk renames the host control the root owns and a flyout the root hung on it,
            // but still does not enter the island.
            var directLeaf = new WinUI.TextBlock();
            directLeaf.SetValue(ReactorDiagnostics.SourceProperty, OldOwnerValue);
            using var directIsland = new Microsoft.UI.Reactor.Hosting.ReactorHostControl { Content = directLeaf };
            directIsland.SetValue(ReactorDiagnostics.SourceProperty, OldOwnerValue);
            var islandFlyoutBody = new WinUI.TextBlock();
            islandFlyoutBody.SetValue(ReactorDiagnostics.SourceProperty, OldOwnerValue);
            Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase.SetAttachedFlyout(directIsland, new WinUI.Flyout { Content = islandFlyoutBody });
            probeHost.Reconciler.RenameRootOwner(directIsland, nameof(RemountRootA), nameof(RemountRootB));
            H.Check("ReactorSource_RootRemount_DirectIslandRootUntouched",
                ReactorDiagnostics.GetSource(directLeaf) == OldOwnerValue
                && ReactorDiagnostics.GetSource(directIsland)?.Contains("|owner=RemountRootB|", StringComparison.Ordinal) == true
                && ReactorDiagnostics.GetSource(islandFlyoutBody)?.Contains("|owner=RemountRootB|", StringComparison.Ordinal) == true);
            probeHost.Dispose();
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
        }
    }
}

internal sealed class TeardownProbe : Component
{
    internal static int Cleanups;

    public override Element Render()
    {
        UseEffect(() => () => Cleanups++);
        return TextBlock("teardown-probe");
    }
}

/// <summary>
/// The Native AOT tag skip must not skip teardown. A callback-free, keyless NavigationView
/// tears down its named slots (PaneHeader) in its handler's unmount, which is dispatched
/// through the tag; and a control returned through <c>ReturnControl</c> with pooling disabled
/// must stop answering <c>GetSource</c> for the element it hosted. Both, in both tag paths.
/// </summary>
internal class ReactorSource_AotTagSkipKeepsTeardown(Harness h) : SelfTestFixtureBase(h)
{
    private static Element Tree(RenderContext ctx)
    {
        var (shown, setShown) = ctx.UseState(true);
        var nav = ctx.UseNavigation("a");
        return VStack(
            Button("teardown-toggle", () => setShown(false)),
            shown
                ? VStack(
                    NavigationView([]).PaneHeader(Component<TeardownProbe>()),
                    // An item host (ItemsHost strategy, hidden from Children): its items are
                    // torn down by the handler too.
                    ComboBox([Component<TeardownProbe>()], default, null),
                    // A hand-written handler with no children strategy whose Unmount detaches
                    // the route subscription and clears the page cache.
                    NavigationHost(nav, route => TextBlock($"teardown-page {route}")))
                : TextBlock("teardown-gone"));
    }

    private async Task<(int Cleanups, bool NavTagged, bool ComboTagged, int HostNodesBefore, int HostNodesAfter, bool ReturnedBefore, bool ReturnedAfter)> RunPath(bool noManagedAgent)
    {
        ReactorSourcePublisher.NoManagedAgent = noManagedAgent;
        TeardownProbe.Cleanups = 0;
        var host = H.CreateHost();
        host.Mount(Tree);
        await Harness.Render();
        await Harness.Render();
        var nav = H.FindControl<WinUI.NavigationView>(_ => true);
        bool navTagged = nav is not null && Reconciler.GetElementTag(nav) is not null;
        var combo = H.FindControl<WinUI.ComboBox>(_ => true);
        bool comboTagged = combo is not null && Reconciler.GetElementTag(combo) is not null;
        int hostNodesBefore = host.Reconciler._navigationHostNodes.Count;
        H.ClickButton("teardown-toggle");
        await Harness.Render();
        await Harness.Render();
        int cleanups = TeardownProbe.Cleanups;
        int hostNodesAfter = host.Reconciler._navigationHostNodes.Count;

        host.Reconciler.Pool.Enabled = false;
        var element = TextBlock("returned");
        var returned = new WinUI.TextBlock();
        Reconciler.SetElementTagIfNeeded(returned, element);
        ReactorSourcePublisher.Publish(returned, element, "Owner");
        bool before = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(returned) is not null;
        host.Reconciler.ReturnControl(returned);
        bool after = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(returned) is null;
        host.Reconciler.Pool.Enabled = true;

        host.Dispose();
        H.SetContent(null);
        return (cleanups, navTagged, comboTagged, hostNodesBefore, hostNodesAfter, before, after);
    }

    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_AotSkipTeardown", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var (enabled, noAgent, mapped) = (ReactorSourcePublisher.IsEnabled, ReactorSourcePublisher.NoManagedAgent,
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);
        try
        {
            ReactorSourcePublisher.IsEnabled = true;
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = true;
            // The scenario is a STAMPED element losing its tag; a host without source mapping
            // stamps nothing, so neither path tags these controls and there is nothing to test.
            if (TextBlock("teardown-stamp-probe").CallSite is null)
            {
                H.Skip("ReactorSource_AotSkipTeardown", "call sites are not stamped in this host");
                return;
            }

            var tagged = await RunPath(noManagedAgent: false);
            var skipped = await RunPath(noManagedAgent: true);
            Console.WriteLine($"# teardown: tagged {tagged}; skipped {skipped}");

            H.Check("ReactorSource_AotSkipTeardown_PaneHeaderAndItemsCleanedUp", tagged.Cleanups == 2 && skipped.Cleanups == 2);
            H.Check("ReactorSource_AotSkipTeardown_NavigationViewKeepsItsTag", tagged.NavTagged && skipped.NavTagged);
            H.Check("ReactorSource_AotSkipTeardown_ItemHostKeepsItsTag", tagged.ComboTagged && skipped.ComboTagged);
            // NavigationHost's handler-owned teardown ran: its tracked node is gone.
            H.Check("ReactorSource_AotSkipTeardown_NavigationHostCleanedUp",
                tagged.HostNodesBefore == 1 && skipped.HostNodesBefore == 1 && tagged.HostNodesAfter == 0 && skipped.HostNodesAfter == 0);
            H.Check("ReactorSource_AotSkipTeardown_ReturnedControlForgotten",
                tagged.ReturnedBefore && skipped.ReturnedBefore && tagged.ReturnedAfter && skipped.ReturnedAfter);
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
/// A descriptor-backed <c>ContentControl</c> whose single-content strategy keeps its child in a
/// slot the generic unmount walk does not visit (here <c>Tag</c>; <c>Content</c> stays null).
/// The walk would visit the empty <c>Content</c>, so in the Native AOT skip mode the element
/// must still keep the tag that routes unmount to the strategy, or the child's effect cleanup
/// never runs. Both tag modes.
/// </summary>
internal class ReactorSource_AotSkipCustomSlotKeepsTeardown(Harness h) : SelfTestFixtureBase(h)
{
    internal sealed record AuxSlotElement(Element Child) : Element;

    private sealed class AuxSlotHandler()
        : Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.DescriptorHandler<AuxSlotElement, WinUI.ContentControl>(Slot)
    {
        private static readonly Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.ControlDescriptor<AuxSlotElement, WinUI.ContentControl> Slot =
            new() { Children = new Microsoft.UI.Reactor.Core.V1Protocol.SingleContent<AuxSlotElement, WinUI.ContentControl>(
                GetChild: static e => e.Child,
                SetChild: static (c, ui) => c.Tag = ui)
            {
                GetCurrentChild = static c => c.Tag as UIElement,
            } };
    }

    private async Task<(int Cleanups, bool Tagged)> RunPath(bool noManagedAgent)
    {
        ReactorSourcePublisher.NoManagedAgent = noManagedAgent;
        TeardownProbe.Cleanups = 0;
        Action<bool>? setShown = null;
        var host = H.CreateHost();
        host.Mount(ctx =>
        {
            var (shown, set) = ctx.UseState(true);
            setShown = set;
            return shown
                ? VStack(new AuxSlotElement(Component<TeardownProbe>()) { CallSite = new SourceLocation("AuxSlot.cs", 1, 1) })
                : VStack(TextBlock("aux-slot-gone"));
        });
        await Harness.Render();
        await Harness.Render();
        var slot = H.FindControl<WinUI.ContentControl>(c => c.Tag is UIElement);
        bool tagged = slot is not null && Reconciler.GetElementTag(slot) is not null;
        setShown!(false);
        await Harness.Render();
        await Harness.Render();
        int cleanups = TeardownProbe.Cleanups;
        host.Dispose();
        H.SetContent(null);
        return (cleanups, tagged);
    }

    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_AotSkipCustomSlot", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        Microsoft.UI.Reactor.Core.V1Protocol.ControlRegistry.Register<AuxSlotElement, WinUI.ContentControl>(static () => new AuxSlotHandler());
        var (enabled, noAgent) = (ReactorSourcePublisher.IsEnabled, ReactorSourcePublisher.NoManagedAgent);
        try
        {
            ReactorSourcePublisher.IsEnabled = true;
            var tagged = await RunPath(noManagedAgent: false);
            var skipped = await RunPath(noManagedAgent: true);
            Console.WriteLine($"# custom slot: tagged {tagged}; skipped {skipped}");
            H.Check("ReactorSource_AotSkipCustomSlot_ChildCleanedUp", tagged.Cleanups == 1 && skipped.Cleanups == 1);
            H.Check("ReactorSource_AotSkipCustomSlot_KeepsItsTag", tagged.Tagged && skipped.Tagged);
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = enabled;
            ReactorSourcePublisher.NoManagedAgent = noAgent;
        }
    }
}

/// <summary>
/// A root whose child mounts the root's replacement during the first pass: that pass still
/// renders (and publishes) the outgoing root, so the root value and the owner it records name it.
/// The replacement's first pass then renames the retained, skipped descendants it shares
/// with the outgoing tree. Both hosts.
/// </summary>
internal class ReactorSource_ReentrantRootMount(Harness h) : SelfTestFixtureBase(h)
{
    // One call site for both roots, so the leaf reconciles by a shallow skip.
    internal static Element SharedLeaf() => TextBlock("reentrant-shared");

    internal sealed class OutgoingRoot : Component
    {
        internal static Action? MountReplacement;

        // The replacement is mounted by a child, during reconciliation: after the pass has
        // already published the shared leaf as the outgoing root's.
        public override Element Render() => VStack(SharedLeaf(), Component<MountingChild>());
    }

    internal sealed class MountingChild : Component
    {
        public override Element Render()
        {
            var mount = OutgoingRoot.MountReplacement;
            OutgoingRoot.MountReplacement = null;
            mount?.Invoke();
            return TextBlock("reentrant-outgoing");
        }
    }

    internal sealed class ReplacementRoot : Component
    {
        public override Element Render() => VStack(SharedLeaf(), TextBlock("reentrant-replacement"));
    }

    private void Check(string name)
    {
        var shared = H.FindControl<WinUI.TextBlock>(t => t.Text == "reentrant-shared");
        var sharedValue = shared is null ? null : ReactorDiagnostics.GetSource(shared);
        var rootValue = shared is null || VisualTreeHelper.GetParent(shared) is not { } parent ? null : ReactorDiagnostics.GetSource(parent);
        Console.WriteLine($"# {name}: shared {sharedValue}; root {rootValue}");
        H.Check(name,
            sharedValue?.Contains("|owner=ReplacementRoot|", StringComparison.Ordinal) == true
            && rootValue?.Contains("|root=ReplacementRoot", StringComparison.Ordinal) == true);
    }

    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_ReentrantRootMount", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var previous = ReactorSourcePublisher.IsEnabled;
        try
        {
            ReactorSourcePublisher.IsEnabled = true;

            var host = H.CreateHost();
            OutgoingRoot.MountReplacement = () => host.Mount(new ReplacementRoot());
            host.Mount(new OutgoingRoot());
            await Harness.WaitFor(() => H.FindControl<WinUI.TextBlock>(t => t.Text == "reentrant-replacement") is not null,
                maxPasses: 32, perPassMs: 10);
            await Harness.Render();
            Check("ReactorSource_ReentrantRootMount_Host_RenamesRetainedDescendants");
            host.Dispose();
            H.SetContent(null);

            var control = new Microsoft.UI.Reactor.Hosting.ReactorHostControl();
            OutgoingRoot.MountReplacement = () => control.Mount(new ReplacementRoot());
            control.Mount(new OutgoingRoot());
            H.SetContent(new WinUI.Border { Child = control });
            // A standalone ReactorHostControl is not ReactorApp.ActiveHost: poll its loop.
            await Harness.WaitFor(() => H.FindControl<WinUI.TextBlock>(t => t.Text == "reentrant-replacement") is not null,
                maxPasses: 32, perPassMs: 10);
            await Harness.Render();
            Check("ReactorSource_ReentrantRootMount_HostControl_RenamesRetainedDescendants");
            control.Dispose();
            H.SetContent(null);
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
        }
    }
}

/// <summary>
/// A ContentDialog element realizes a collapsed placeholder; the dialog the user sees is a
/// separate WinUI object. An out-of-process inspector sees the dialog, so it carries the
/// element's published value too: when opened at mount (the reconciler mirrors the value it
/// publishes) and when opened later by a state change (copied at creation).
/// </summary>
internal class ReactorSource_LiveDialogPublished(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_LiveDialog", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var previous = ReactorSourcePublisher.IsEnabled;
        try
        {
            ReactorSourcePublisher.IsEnabled = true;

            var host = H.CreateHost();
            host.Mount(_ => VStack(
                TextBlock("live-dialog-anchor"),
                ContentDialog("SourceAtMount", TextBlock("live-dialog-body"), "OK") with { IsOpen = true }));
            var atMount = await ContentDialogProbe.WaitForOpen(H, "SourceAtMount", 2_000);
            var atMountValue = atMount is null ? null : ReactorDiagnostics.GetSource(atMount);
            Console.WriteLine($"# live dialog (mount): {atMountValue}");
            H.Check("ReactorSource_LiveDialog_PublishedWhenOpenedAtMount",
                atMountValue?.Contains("|element=ContentDialog", StringComparison.Ordinal) == true
                && atMountValue.Contains("|owner=FuncElement", StringComparison.Ordinal));
            // The open was deferred to the placeholder's Loaded, outside the render: the body was
            // still mounted under the owner it was declared under.
            var bodyValue = atMount?.Content is WinUI.TextBlock body ? ReactorDiagnostics.GetSource(body) : null;
            Console.WriteLine($"# live dialog body (mount): {bodyValue}");
            H.Check("ReactorSource_LiveDialog_DeferredBodyKeepsItsOwner",
                bodyValue?.Contains("|owner=FuncElement|", StringComparison.Ordinal) == true
                && bodyValue.Contains("|element=TextBlock", StringComparison.Ordinal));
            atMount?.Hide();
            await Harness.Render(50);
            host.Dispose();
            H.SetContent(null);

            var flipHost = H.CreateHost();
            flipHost.Mount(ctx =>
            {
                var (open, setOpen) = ctx.UseState(false);
                return VStack(
                    Button("live-dialog-open", () => setOpen(true)),
                    ContentDialog("SourceOnFlip", TextBlock("live-dialog-body"), "OK") with { IsOpen = open });
            });
            await Harness.Render();
            H.ClickButton("live-dialog-open");
            var onFlip = await ContentDialogProbe.WaitForOpen(H, "SourceOnFlip", 2_000);
            var onFlipValue = onFlip is null ? null : ReactorDiagnostics.GetSource(onFlip);
            Console.WriteLine($"# live dialog (flip): {onFlipValue}");
            H.Check("ReactorSource_LiveDialog_PublishedWhenOpenedLater",
                onFlipValue?.Contains("|element=ContentDialog", StringComparison.Ordinal) == true);
            onFlip?.Hide();
            await Harness.Render(50);
            flipHost.Dispose();
            H.SetContent(null);
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
        }
    }
}

internal sealed record ForwardingElement(string Label) : Element;

/// <summary>
/// A per-host <c>RegisterType</c> callback that forwards to a child returns the control the
/// reconciler mounted for that child: the control describes the child (JIT keeps the child's
/// tag). Its published value must stay the child's too, so that in the Native AOT tag-skip
/// mode, where neither element is tagged, <c>GetSource</c> still names the child's call site,
/// exactly as JIT does. Both tag paths, on mount and after an update.
/// </summary>
internal class ReactorSource_ForwardingRegistrationKeepsChildSource(Harness h) : SelfTestFixtureBase(h)
{
    private async Task<(SourceLocation? Mounted, string? MountedValue, SourceLocation? Updated, string? UpdatedValue)> RunPath(bool noManagedAgent, bool unstampedChild = false, bool keyed = false, bool withUnmount = false)
    {
        ReactorSourcePublisher.NoManagedAgent = noManagedAgent;
        var host = H.CreateHost();
        // An unstamped child (built with a constructor, not a factory) has no call site: JIT then
        // tags the control with the registration, so GetSource names the registration's location.
        Element Child(string label) => unstampedChild ? new TextBlockElement(label) : TextBlock(label);
        host.Reconciler.RegisterType<ForwardingElement, UIElement>(
            mount: (r, el, rerender) => r.Mount(Child(el.Label), rerender)!,
            update: (r, oldEl, newEl, control, rerender) => r.UpdateChild(Child(oldEl.Label), Child(newEl.Label), control, rerender),
            // An unmount callback makes the registration always tag the control (when it has no tag).
            unmount: withUnmount ? static (_, _) => { } : (Action<Reconciler, UIElement>?)null);
        host.Mount(ctx =>
        {
            var (label, setLabel) = ctx.UseState("fwd-a");
            return VStack(
                Button("fwd-rename", () => setLabel("fwd-b")),
                // Hand-stamped, with a call site that moves on the update: the update path's
                // identity check then re-publishes, so the forwarding guard there is exercised.
                new ForwardingElement(label) { CallSite = new SourceLocation("Forwarding.cs", label == "fwd-a" ? 1 : 2, 1), Key = keyed ? "fwd-key" : null });
        });
        await Harness.Render();
        var a = H.FindControl<WinUI.TextBlock>(t => t.Text == "fwd-a");
        var mounted = a is null ? null : Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(a);
        var mountedValue = a is null ? null : ReactorDiagnostics.GetSource(a);
        H.ClickButton("fwd-rename");
        await Harness.Render();
        await Harness.Render();
        var b = H.FindControl<WinUI.TextBlock>(t => t.Text == "fwd-b");
        var updated = b is null ? null : Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(b);
        var updatedValue = b is null ? null : ReactorDiagnostics.GetSource(b);
        host.Dispose();
        H.SetContent(null);
        return (mounted, mountedValue, updated, updatedValue);
    }

    // The forwarding element as the host's root itself: root publication adds only the
    // host-root fields to the child's value.
    private async Task<(SourceLocation? Mounted, string? MountedValue, SourceLocation? Updated, string? UpdatedValue)> RunRootPath(bool noManagedAgent)
    {
        ReactorSourcePublisher.NoManagedAgent = noManagedAgent;
        var host = H.CreateHost();
        host.Reconciler.RegisterType<ForwardingElement, UIElement>(
            mount: (r, el, rerender) => r.Mount(TextBlock(el.Label), rerender)!,
            update: (r, oldEl, newEl, control, rerender) => r.UpdateChild(TextBlock(oldEl.Label), TextBlock(newEl.Label), control, rerender));
        Action? rename = null;
        host.Mount(ctx =>
        {
            var (label, setLabel) = ctx.UseState("fwd-root-a");
            rename = () => setLabel("fwd-root-b");
            return new ForwardingElement(label) { CallSite = new SourceLocation("ForwardingRoot.cs", label == "fwd-root-a" ? 1 : 2, 1) };
        });
        await Harness.Render();
        var a = H.FindControl<WinUI.TextBlock>(t => t.Text == "fwd-root-a");
        var mounted = a is null ? null : Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(a);
        var mountedValue = a is null ? null : ReactorDiagnostics.GetSource(a);
        rename?.Invoke();
        await Harness.Render();
        await Harness.Render();
        var b = H.FindControl<WinUI.TextBlock>(t => t.Text == "fwd-root-b");
        var updated = b is null ? null : Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(b);
        var updatedValue = b is null ? null : ReactorDiagnostics.GetSource(b);
        host.Dispose();
        H.SetContent(null);
        return (mounted, mountedValue, updated, updatedValue);
    }

    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_ForwardingRegistration", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var (enabled, noAgent, mapped) = (ReactorSourcePublisher.IsEnabled, ReactorSourcePublisher.NoManagedAgent,
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);
        try
        {
            ReactorSourcePublisher.IsEnabled = true;
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = true;
            if (TextBlock("forwarding-stamp-probe").CallSite is null)
            {
                H.Skip("ReactorSource_ForwardingRegistration", "call sites are not stamped in this host");
                return;
            }

            var tagged = await RunPath(noManagedAgent: false);
            var skipped = await RunPath(noManagedAgent: true);
            Console.WriteLine($"# forwarding: tagged {tagged}; skipped {skipped}");

            H.Check("ReactorSource_ForwardingRegistration_GetSourceAgreesOnMount",
                tagged.Mounted is not null && tagged.Mounted == skipped.Mounted);
            H.Check("ReactorSource_ForwardingRegistration_GetSourceAgreesAfterUpdate",
                tagged.Updated is not null && tagged.Updated == skipped.Updated);
            H.Check("ReactorSource_ForwardingRegistration_ValueDescribesTheChild",
                new[] { tagged.MountedValue, tagged.UpdatedValue, skipped.MountedValue, skipped.UpdatedValue }
                    .All(v => v?.Contains("|element=TextBlock", StringComparison.Ordinal) == true));

            var rootTagged = await RunRootPath(noManagedAgent: false);
            var rootSkipped = await RunRootPath(noManagedAgent: true);
            Console.WriteLine($"# forwarding root: tagged {rootTagged}; skipped {rootSkipped}");
            H.Check("ReactorSource_ForwardingRegistration_Root_GetSourceAgrees",
                rootTagged.Mounted is not null && rootTagged.Mounted == rootSkipped.Mounted
                && rootTagged.Updated is not null && rootTagged.Updated == rootSkipped.Updated);
            H.Check("ReactorSource_ForwardingRegistration_Root_ValueIsTheChildsWithRootFields",
                new[] { rootTagged.MountedValue, rootTagged.UpdatedValue, rootSkipped.MountedValue, rootSkipped.UpdatedValue }
                    .All(v => v?.Contains("|element=TextBlock", StringComparison.Ordinal) == true
                        && v.Contains("|root=FuncElement", StringComparison.Ordinal)
                        && !v.Contains("ForwardingRoot.cs", StringComparison.Ordinal)));

            // Unstamped child: JIT's GetSource names the registration (it tags the control with
            // itself, as the child has no tag), and the AOT path must too, instead of resolving
            // nothing from a child value without a call site. (After an update JIT's tag is the
            // child's: the child's own in-place update refreshes the existing tag to itself,
            // which its callbacks rely on, so JIT reports null there; the AOT path keeps
            // naming the registration's moved location.)
            var unstampedTagged = await RunPath(noManagedAgent: false, unstampedChild: true);
            var unstampedSkipped = await RunPath(noManagedAgent: true, unstampedChild: true);
            Console.WriteLine($"# forwarding to an unstamped child: tagged {unstampedTagged}; skipped {unstampedSkipped}");
            H.Check("ReactorSource_ForwardingRegistration_UnstampedChild_GetSourceAgrees",
                unstampedTagged.Mounted == new SourceLocation("Forwarding.cs", 1, 1)
                && unstampedSkipped.Mounted == unstampedTagged.Mounted
                && unstampedSkipped.Updated == new SourceLocation("Forwarding.cs", 2, 1));

            // Registrations that need a tag of their own (a key, an unmount callback): in the AOT
            // skip mode they tag the control with themselves, as the stamped child is untagged,
            // yet GetSource must still name the child, as JIT's kept child tag does.
            foreach (var (keyed, withUnmount, name) in new[] { (true, false, "Keyed"), (false, true, "UnmountCallback") })
            {
                var t = await RunPath(noManagedAgent: false, keyed: keyed, withUnmount: withUnmount);
                var s = await RunPath(noManagedAgent: true, keyed: keyed, withUnmount: withUnmount);
                Console.WriteLine($"# forwarding ({name}): tagged {t.Mounted} / {t.Updated}; skipped {s.Mounted} / {s.Updated}");
                H.Check($"ReactorSource_ForwardingRegistration_{name}_GetSourceNamesTheChild",
                    t.Mounted is { } tm && tm.FilePath != "Forwarding.cs" && s.Mounted == tm
                    && t.Updated is { } tu && tu.FilePath != "Forwarding.cs" && s.Updated == tu);
            }
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = enabled;
            ReactorSourcePublisher.NoManagedAgent = noAgent;
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = mapped;
        }
    }
}

internal sealed class ThrowingSourceRoot : Component
{
    public override Element Render()
    {
        var (n, _) = UseState(0);
        throw new InvalidOperationException("source root boom " + n);
    }
}

/// <summary>
/// A root that throws, with an app <c>RenderErrorHandler</c> supplying the fallback: the
/// fallback stands in for the root's content, so its content root still names the root
/// (<c>root=</c>) while describing the fallback itself. Both host types.
/// </summary>
internal class ReactorSource_AppFallbackNamesTheRoot(Harness h) : SelfTestFixtureBase(h)
{
    private static string? Value(DependencyObject? d) => d is null ? null : ReactorDiagnostics.GetSource(d);

    // The fallback is installed inside an internal guard (an error boundary), whose wrapper is the
    // host's content root: that carries root=, and the app's own control keeps describing itself.
    private void Check(string name, WinUI.TextBlock? fallback)
    {
        var own = Value(fallback);
        var contentRoot = Value(fallback is null ? null : Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(fallback));
        Console.WriteLine($"# {name}: content root {contentRoot}; fallback {own}");
        H.Check(name,
            contentRoot?.Contains("|root=ThrowingSourceRoot", StringComparison.Ordinal) == true
            && own?.Contains("|element=TextBlock", StringComparison.Ordinal) == true
            && !own.Contains("|root=", StringComparison.Ordinal),
            contentRoot ?? "(no value)");
    }

    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_AppFallback", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var previous = ReactorSourcePublisher.IsEnabled;
        try
        {
            ReactorSourcePublisher.IsEnabled = true;

            var host = H.CreateHost();
            host.RenderErrorHandler = _ => TextBlock("source-fallback-host");
            host.Mount(new ThrowingSourceRoot());
            await Harness.Render();
            Check("ReactorSource_AppFallback_Host_NamesTheRoot", H.FindControl<WinUI.TextBlock>(t => t.Text == "source-fallback-host"));
            host.Dispose();
            H.SetContent(null);

            var control = new Microsoft.UI.Reactor.Hosting.ReactorHostControl
            {
                RenderErrorHandler = _ => TextBlock("source-fallback-control"),
            };
            H.SetContent(new WinUI.Border { Child = control });
            control.Mount(new ThrowingSourceRoot());
            // A standalone ReactorHostControl is not ReactorApp.ActiveHost: poll its loop.
            await Harness.WaitFor(() => H.FindControl<WinUI.TextBlock>(t => t.Text == "source-fallback-control") is not null,
                maxPasses: 32, perPassMs: 10);
            Check("ReactorSource_AppFallback_HostControl_NamesTheRoot", H.FindControl<WinUI.TextBlock>(t => t.Text == "source-fallback-control"));
            control.Dispose();
            H.SetContent(null);
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
        }
    }
}

/// <summary>
/// An unstamped, keyless, callback-free item host (a ComboBox with component items) is
/// untagged. When a later render stamps it with the same props it takes a shallow skip, whose
/// tag refresh must honour handler-owned teardown in the Native AOT skip mode as mount and
/// update do; otherwise removing it never reaches the handler and the items' effect cleanups
/// never run. Root, positional and mixed-key skip paths, both tag modes.
/// </summary>
internal class ReactorSource_SkipStampedItemHostKeepsTeardown(Harness h) : SelfTestFixtureBase(h)
{
    private static readonly Element[] Items = [Component<TeardownProbe>()];

    // Phase 0: unstamped. Phase 1: the same props, stamped (a shallow skip). Phase 2: removed.
    private static Element Combo(int phase)
    {
        var combo = ComboBox(Items, default, null) with { CallSite = new SourceLocation("SkipStamp.cs", 1, 1) };
        return phase == 0 ? combo with { CallSite = null } : combo;
    }

    private static Element Layout(string layout, int phase) => layout switch
    {
        "Root" => phase == 2 ? TextBlock("skip-stamp-gone") : Combo(phase),
        "Positional" => phase == 2 ? VStack(TextBlock("skip-stamp-head")) : VStack(TextBlock("skip-stamp-head"), Combo(phase)),
        _ => phase == 2
            ? VStack(TextBlock("k1").WithKey("k1"), TextBlock("k2").WithKey("k2"))
            : VStack(TextBlock("k1").WithKey("k1"), Combo(phase), TextBlock("k2").WithKey("k2")),
    };

    private async Task<int> Run(string layout, bool noManagedAgent)
    {
        ReactorSourcePublisher.NoManagedAgent = noManagedAgent;
        TeardownProbe.Cleanups = 0;
        Action<int>? setPhase = null;
        var host = H.CreateHost();
        host.Mount(ctx =>
        {
            var (phase, set) = ctx.UseState(0);
            setPhase = set;
            return Layout(layout, phase);
        });
        await Harness.Render();
        setPhase!(1);
        await Harness.Render();
        await Harness.Render();
        setPhase!(2);
        await Harness.Render();
        await Harness.Render();
        int cleanups = TeardownProbe.Cleanups;
        host.Dispose();
        H.SetContent(null);
        return cleanups;
    }

    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_SkipStampedItemHost", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var (enabled, noAgent) = (ReactorSourcePublisher.IsEnabled, ReactorSourcePublisher.NoManagedAgent);
        try
        {
            ReactorSourcePublisher.IsEnabled = true;
            foreach (var layout in new[] { "Root", "Positional", "MixedKeys" })
            {
                int tagged = await Run(layout, noManagedAgent: false);
                int skipped = await Run(layout, noManagedAgent: true);
                Console.WriteLine($"# skip-stamped item host ({layout}): cleanups tagged {tagged}, skipped {skipped}");
                H.Check($"ReactorSource_SkipStampedItemHost_{layout}_ItemsCleanedUp", tagged == 1 && skipped == 1);
            }
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = enabled;
            ReactorSourcePublisher.NoManagedAgent = noAgent;
        }
    }
}