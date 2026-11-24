using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using WctControls;
using static Microsoft.UI.Reactor.Factories;
using WinXC = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal sealed class ToolkitHeaderedItemsControlFixture(Harness h) : SelfTestFixtureBase(h)
{
    private const string Name = "Toolkit_HeaderedItemsControl_KeyedChildren";

    private sealed class RowProbe(string id)
    {
        internal string Id { get; } = id;
        internal int Mounts;
        internal int Cleanups;
        internal Action<int> SetCount = null!;
    }

    private sealed record RowProps(RowProbe Probe, int Revision);

    private sealed class Row : Component<RowProps>
    {
        public override Element Render()
        {
            var probe = Props.Probe;
            var (count, setCount) = UseState(0);
            probe.SetCount = setCount;
            UseEffect(() =>
            {
                probe.Mounts++;
                return () => probe.Cleanups++;
            }, Array.Empty<object>());
            return TextBlock($"{probe.Id}@{Props.Revision}:{count}");
        }
    }

    public override async Task RunAsync()
    {
        var a = new RowProbe("a");
        var b = new RowProbe("b");
        var c = new RowProbe("c");
        var d = new RowProbe("d");
        RowProbe[] original = [a, b, c];
        RowProbe[] changed = [c, a, d];
        int rootMounts = 0, rootCleanups = 0;
        Action<int> setStep = null!;
        using var host = H.CreateHost();
        host.Mount(ctx =>
        {
            var (step, set) = ctx.UseState(0);
            setStep = set;
            if (step is 2 or 4)
                return TextBlock("items host removed");
            var rows = step == 1 ? changed : original;
            return HeaderedItemsControlElement.HeaderedItemsControl(
                header: $"revision {step}",
                items: rows.Select(row => (object)Component<Row, RowProps>(new(row, step))
                    .WithKey(row.Id)).ToArray())
                .OnMount(_ => rootMounts++)
                .OnUnmount(_ => rootCleanups++);
        });

        await Harness.Render();
        var root = FindRoot();
        var mounted = Snapshot(root, "Mount", ["a@0:0", "b@0:0", "c@0:0"], ["a", "b", "c"]);
        CheckLifetimes("Mount", [a, b, c, d], [1, 1, 1, 0], [0, 0, 0, 0]);

        // Non-default state distinguishes a surviving component from a remount
        // that happens to rent the very same native control from the pool.
        a.SetCount(7);
        b.SetCount(8);
        c.SetCount(9);
        await Harness.Render();
        Snapshot(root, "State", ["a@0:7", "b@0:8", "c@0:9"], ["a", "b", "c"]);

        setStep(1);
        await Harness.Render();
        var updatedRoot = FindRoot();
        var updated = Snapshot(updatedRoot, "Update", ["c@1:9", "a@1:7", "d@1:0"], ["c", "a", "d"]);
        H.Check($"{Name}_RetainedRoot",
            ReferenceEquals(root, updatedRoot) && rootMounts == 1 && rootCleanups == 0,
            $"root mounts={rootMounts}, cleanups={rootCleanups}");
        H.Check($"{Name}_SurvivingNativeItems",
            ReferenceEquals(mounted[2].Root, updated[0].Root) &&
            ReferenceEquals(mounted[0].Root, updated[1].Root) &&
            ReferenceEquals(mounted[2].Text, updated[0].Text) &&
            ReferenceEquals(mounted[0].Text, updated[1].Text));
        CheckLifetimes("Update", [a, b, c, d], [1, 1, 1, 1], [0, 1, 0, 0]);

        setStep(2);
        await Harness.Render();
        CheckRemoved("Teardown", root, rootMounts, rootCleanups, expected: 1);
        CheckLifetimes("Teardown", [a, b, c, d], [1, 1, 1, 1], [1, 1, 1, 1]);

        setStep(3);
        await Harness.Render();
        var remountedRoot = FindRoot();
        // A true remount may reuse pooled physical objects; fresh hook state and
        // balanced logical lifetimes, not reference inequality, are the oracle.
        Snapshot(remountedRoot, "Remount", ["a@3:0", "b@3:0", "c@3:0"], ["a", "b", "c"]);
        H.Check($"{Name}_RootRemounted", rootMounts == 2 && rootCleanups == 1,
            $"root mounts={rootMounts}, cleanups={rootCleanups}");
        CheckLifetimes("Remount", [a, b, c, d], [2, 2, 2, 1], [1, 1, 1, 1]);

        setStep(4);
        await Harness.Render();
        CheckRemoved("FinalTeardown", remountedRoot, rootMounts, rootCleanups, expected: 2);
        CheckLifetimes("FinalTeardown", [a, b, c, d], [2, 2, 2, 1], [2, 2, 2, 1]);
    }

    private HeaderedItemsControl FindRoot() =>
        H.FindControl<HeaderedItemsControl>(_ => true)
        ?? throw new InvalidOperationException("Generated HeaderedItemsControl was not mounted.");

    private (UIElement Root, WinXC.TextBlock Text)[] Snapshot(
        HeaderedItemsControl root, string phase, string[] expectedText, string[] expectedKeys)
    {
        var items = root.Items.Cast<object>().Select(item =>
            item is WinXC.Border { Child: WinXC.TextBlock leaf } border
                ? (Root: (UIElement)border, Text: leaf)
                : throw new InvalidOperationException($"Unexpected native item {item?.GetType().Name}.")).ToArray();
        var text = items.Select(item => item.Text.Text).ToArray();
        var keys = items.Select(item => Reconciler.GetElementTag(item.Root)?.Key).ToArray();
        H.Check($"{Name}_{phase}_NativeContent", text.SequenceEqual(expectedText),
            $"expected [{string.Join(", ", expectedText)}], actual [{string.Join(", ", text)}]");
        H.Check($"{Name}_{phase}_NativeKeys", keys.SequenceEqual(expectedKeys),
            $"expected [{string.Join(", ", expectedKeys)}], actual [{string.Join(", ", keys)}]");
        return items;
    }

    private void CheckLifetimes(string phase, RowProbe[] rows, int[] mounts, int[] cleanups)
    {
        for (int i = 0; i < rows.Length; i++)
            H.Check($"{Name}_{phase}_{rows[i].Id}_Lifetime",
                rows[i].Mounts == mounts[i] && rows[i].Cleanups == cleanups[i],
                $"mounts={rows[i].Mounts} (expected {mounts[i]}), cleanups={rows[i].Cleanups} (expected {cleanups[i]})");
    }

    private void CheckRemoved(string phase, HeaderedItemsControl root, int mounts, int cleanups, int expected)
    {
        H.Check($"{Name}_{phase}_NativeItemsCleared", root.Items.Count == 0,
            $"remaining native items={root.Items.Count}");
        H.Check($"{Name}_{phase}_RootRemoved",
            H.FindControl<HeaderedItemsControl>(_ => true) is null &&
            H.FindText("items host removed") is not null && mounts == expected && cleanups == expected,
            $"root mounts={mounts}, cleanups={cleanups}");
    }
}
