using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class VirtualListRangeFixtures
{
    internal sealed class RefreshesCallbackAndGeometry(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => RunScenario(H, initiallyNull: false, initialRef: true);
    }

    internal sealed class AddsInitiallyMissingCallback(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await RunScenario(H, initiallyNull: true, initialRef: false);
            await RunScenario(H, initiallyNull: true, initialRef: true);
        }
    }

    private static async Task RunScenario(Harness h, bool initiallyNull, bool initialRef)
    {
        string prefix = $"VirtualListRange_Null{initiallyNull}_Ref{initialRef}";
        var callbacks = new List<(int Version, int First, int Last)>();
        var nativeEvents = new List<(int First, int Last)>();
        var refs = new List<(int Version, VirtualListRef Value)>();
        Action<int, int> Callback(int version) =>
            (first, last) => callbacks.Add((version, first, last));
        Action<VirtualListRef> CaptureRef(int version) => value => refs.Add((version, value));
        var tree = VirtualList(100, i => TextBlock($"range row {i}").Height(40),
            itemHeight: 32, estimatedItemHeight: 48, spacing: 2,
            @ref: initialRef ? CaptureRef(0) : null,
            onVisibleRangeChanged: initiallyNull ? null : Callback(0))
            .WithKey("range-list").Height(220);
        var original = tree;
        using var host = h.CreateHost();
        host.Mount(_ => tree);
        await Harness.Render();
        var viewer = h.FindControl<WinUI.ScrollViewer>(sv => sv.Content is WinUI.ItemsRepeater)
            ?? throw new InvalidOperationException("VirtualList viewer not mounted.");
        var repeater = (WinUI.ItemsRepeater)viewer.Content;
        var state = Reconciler.GetListState(repeater)!;
        var source = repeater.ItemsSource;
        var factory = repeater.ItemTemplate;
        var row = state.Source[0];
        bool intermediate = true;
        bool unmounted = false;
        int structuralChanges = 0;
        state.Source.CollectionChanged += (_, _) => structuralChanges++;

        void Observe(object? sender, WinUI.ScrollViewerViewChangedEventArgs args)
        {
            double size = (tree.Props.ItemHeight ?? tree.Props.EstimatedItemHeight) + tree.Props.Spacing;
            nativeEvents.Add(((int)(viewer.VerticalOffset / size),
                (int)((viewer.VerticalOffset + viewer.ViewportHeight) / size)));
            intermediate = args.IsIntermediate;
        }

        void CheckNotifications(string phase, int? version)
        {
            var expected = version is { } v
                ? nativeEvents.Select(range => (v, range.First, range.Last)).ToArray()
                : [];
            h.Check($"{prefix}_{phase}_CurrentCallbackAndGeometry",
                callbacks.SequenceEqual(expected),
                $"native={nativeEvents.Count}, expected=[{string.Join(",", expected)}], actual=[{string.Join(",", callbacks)}]");
        }

        void CheckIdentity(string phase)
        {
            h.Check($"{prefix}_{phase}_NativeAndBindingIdentity",
                ReferenceEquals(viewer, h.FindControl<WinUI.ScrollViewer>(sv => sv.Content is WinUI.ItemsRepeater))
                && ReferenceEquals(repeater, viewer.Content)
                && ReferenceEquals(state, Reconciler.GetListState(repeater))
                && ReferenceEquals(source, repeater.ItemsSource)
                && ReferenceEquals(factory, repeater.ItemTemplate)
                && ReferenceEquals(row, state.Source[0])
                && structuralChanges == 0);
        }

        async Task Scroll(string phase, double offset, int? version)
        {
            callbacks.Clear();
            nativeEvents.Clear();
            intermediate = true;
            double previousOffset = viewer.VerticalOffset;
            bool accepted = viewer.ChangeView(null, offset, null, disableAnimation: true);
            // ItemsRepeater can correct the requested offset as rows are measured.
            // Require a completed native event and real movement, not an exact target.
            bool settled = await Harness.WaitForWithin(
                () => nativeEvents.Count > 0 && !intermediate && Math.Abs(viewer.VerticalOffset - previousOffset) > 100,
                timeoutMs: 5_000);
            h.Check($"{prefix}_{phase}_NativeScroll", accepted && settled && viewer.ViewportHeight > 0,
                $"accepted={accepted}, events={nativeEvents.Count}, before={previousOffset}, offset={viewer.VerticalOffset}, target={offset}, viewport={viewer.ViewportHeight}");
            Console.WriteLine($"# {prefix} {phase}: events={nativeEvents.Count}, offset={viewer.VerticalOffset}, viewport={viewer.ViewportHeight}, height={tree.Props.ItemHeight}, estimate={tree.Props.EstimatedItemHeight}, spacing={tree.Props.Spacing}");
            CheckNotifications(phase, version);
            if (!unmounted) CheckIdentity(phase);
        }

        async Task Refresh(string phase, VirtualListElement props, int? version, int? refVersion)
        {
            callbacks.Clear();
            nativeEvents.Clear();
            int previousRefs = refs.Count;
            tree = tree with { Props = props };
            host.RequestRender(force: true);
            await host.WaitForIdleAsync();
            await Harness.Render();
            CheckNotifications($"{phase}_Render", version);
            h.Check($"{prefix}_{phase}_CurrentRef",
                refVersion is { } r
                    ? refs.Count > previousRefs && refs.Skip(previousRefs).All(value =>
                        value.Version == r && ReferenceEquals(repeater, value.Value.Repeater))
                    : refs.Count == previousRefs);
        }

        viewer.ViewChanged += Observe;
        try
        {
            await Scroll("Initial", 320, initiallyNull ? null : 0);
            h.Check($"{prefix}_InitialRef",
                initialRef ? refs.Count > 0 && refs.All(value => value.Version == 0) : refs.Count == 0);

            var currentCallback = Callback(1);
            await Refresh("ReplaceCallback", tree.Props with
            {
                OnVisibleRangeChanged = currentCallback,
                Ref = CaptureRef(1),
            }, 1, 1);
            await Scroll("ReplaceCallback", 960, 1);

            await Refresh("FixedHeight", tree.Props with { ItemHeight = 64 }, 1, 1);
            await Scroll("FixedHeight", 320, 1);
            await Refresh("Spacing", tree.Props with { Spacing = 18 }, 1, 1);
            await Scroll("Spacing", 960, 1);
            await Refresh("EstimatedHeight", tree.Props with { ItemHeight = null }, 1, 1);
            await Scroll("EstimatedHeight", 320, 1);
            await Refresh("ChangedEstimate", tree.Props with { EstimatedItemHeight = 24 }, 1, 1);
            await Scroll("ChangedEstimate", 960, 1);

            // Both callbacks absent means the rendered LazyVStack has no viewer setters.
            await Refresh("RemoveCallbacks", tree.Props with
            {
                OnVisibleRangeChanged = null,
                Ref = null,
                EstimatedItemHeight = 56,
                Spacing = 4,
            }, null, null);
            await Scroll("RemoveCallbacks", 320, null);
            await Refresh("ReAddCallback", tree.Props with
            {
                OnVisibleRangeChanged = Callback(2),
                ItemHeight = 40,
                Ref = CaptureRef(2),
            }, 2, 2);
            await Scroll("ReAddCallback", 960, 2);
            await Refresh("RepeatedRender", tree.Props with { Ref = CaptureRef(3) }, 2, 3);
            await Scroll("RepeatedRender", 320, 2);

            h.Check($"{prefix}_OriginalSnapshot",
                original.Props.ItemHeight == 32 && original.Props.EstimatedItemHeight == 48
                && original.Props.Spacing == 2
                && (original.Props.Ref is null) == !initialRef
                && (original.Props.OnVisibleRangeChanged is null) == initiallyNull);

            host.Dispose();
            unmounted = true;
            // Keep the retired native viewer alive and scrollable to prove cleanup,
            // rather than treating an unloaded viewer's lack of events as success.
            viewer.Content = new WinUI.Border { Height = 10_000 };
            await Harness.Render();
            await Scroll("AfterUnmount", 960, null);
        }
        finally
        {
            viewer.ViewChanged -= Observe;
            h.SetContent(null);
        }
    }
}
