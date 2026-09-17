using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ItemsViewParkingFixtures
{
    private static UIElement? ParkingContent(WinUI.ItemContainer row) =>
        Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(row) > 0
            ? Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(row, 0) as UIElement
            : row.Child;

    private static ElementFactory<int> CreateFactory(Func<int, Element> row) =>
        new(Enumerable.Range(0, 80).ToArray(), (i, _) => row(i),
            new Reconciler(), requestRerender: static () => { }, pool: null);

    internal class PoolRestoresValueSources(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            foreach (var kind in new[] { "Default", "Local", "Style" })
            {
                var factory = CreateFactory(i => ItemContainer(TextBlock($"Row {i}")));
                var bridge = (IElementFactory)factory;
                var row = (WinUI.ItemContainer)bridge.GetElement(new ElementFactoryGetArgs { Data = 0 });
                H.SetContent(row);
                await Harness.Render();
                var content = (FrameworkElement)ParkingContent(row)!;
                if (kind == "Local")
                    content.Visibility = Visibility.Collapsed;
                else if (kind == "Style")
                {
                    var style = new Style(typeof(FrameworkElement));
                    style.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
                    content.Style = style;
                }
                await Harness.Render();
                var properties = new[]
                {
                    UIElement.VisibilityProperty, UIElement.OpacityProperty,
                    Control.IsEnabledProperty, UIElement.IsHitTestVisibleProperty, Control.IsTabStopProperty,
                };
                var outerValues = properties.Select(row.ReadLocalValue).ToArray();
                var transition = new ScalarTransition { Duration = TimeSpan.FromSeconds(1) };
                row.OpacityTransition = transition;
                var contentVisibility = content.ReadLocalValue(UIElement.VisibilityProperty);
                var effectiveVisibility = content.Visibility;

                bridge.RecycleElement(new ElementFactoryRecycleArgs { Element = row });
                H.Check($"Parking_{kind}_ContentCollapsed", content.Visibility == Visibility.Collapsed);
                H.Check($"Parking_{kind}_OuterPropertiesUntouched",
                    properties.Select(row.ReadLocalValue).SequenceEqual(outerValues)
                    && Equals(transition, row.OpacityTransition));
                H.Check($"Parking_{kind}_Pooled", factory.DebugRecyclePoolCount == 1);

                var reused = bridge.GetElement(new ElementFactoryGetArgs { Data = 0 });
                H.Check($"Parking_{kind}_ReusesContainer", ReferenceEquals(row, reused));
                H.Check($"Parking_{kind}_RestoresVisibilitySource",
                    Equals(contentVisibility, content.ReadLocalValue(UIElement.VisibilityProperty))
                    && content.Visibility == effectiveVisibility);
                H.Check($"Parking_{kind}_ReuseLeavesOuterPropertiesUntouched",
                    properties.Select(row.ReadLocalValue).SequenceEqual(outerValues)
                    && Equals(transition, row.OpacityTransition));
                H.SetContent(null);
            }
        }
    }

    internal class BoundRowsAndEviction(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            foreach (var (property, path) in new[]
            {
                (UIElement.VisibilityProperty, nameof(UIElement.Visibility)),
                (UIElement.OpacityProperty, nameof(UIElement.Opacity)),
                (Control.IsEnabledProperty, nameof(Control.IsEnabled)),
                (UIElement.IsHitTestVisibleProperty, nameof(UIElement.IsHitTestVisible)),
                (Control.IsTabStopProperty, nameof(Control.IsTabStop)),
            })
            {
                var source = new WinUI.Button { Opacity = 0.7, IsEnabled = true };
                var factory = CreateFactory(i => ItemContainer(TextBlock($"Row {i}"))
                    .Set(row => row.SetBinding(property, new Binding
                    {
                        Source = source,
                        Path = new PropertyPath(path),
                    })));
                var bridge = (IElementFactory)factory;
                var row = (WinUI.ItemContainer)bridge.GetElement(new ElementFactoryGetArgs { Data = 0 });
                H.Check("AnchorRetire_HasBinding",
                    !ReferenceEquals(row.ReadLocalValue(property), DependencyProperty.UnsetValue)
                    && row.ReadLocalValue(property) is not double and not bool and not Visibility);
                var binding = row.ReadLocalValue(property);
                bridge.RecycleElement(new ElementFactoryRecycleArgs { Element = row });
                H.Check("AnchorBound_UnchangedBindingAllowsPooling", factory.DebugRecyclePoolCount == 1);
                H.Check("AnchorBound_PooledRowStaysTracked", factory.DebugTryGetLastElementByControl(row, out _));
                H.Check("AnchorBound_OuterVisibleAndContentCollapsed",
                    row.Visibility == Visibility.Visible
                    && ParkingContent(row)?.Visibility == Visibility.Collapsed);
                H.Check("AnchorBound_UnchangedBindingPreserved", Equals(binding, row.ReadLocalValue(property)));
                H.Check("AnchorBound_PooledRowReused",
                    ReferenceEquals(row, bridge.GetElement(new ElementFactoryGetArgs { Data = 0 })));
            }

            foreach (var templated in new[] { false, true })
            {
                var source = new WinUI.Border { Visibility = Visibility.Visible };
                var factory = CreateFactory(i => ItemContainer(TextBlock($"Row {i}")));
                var bridge = (IElementFactory)factory;
                var row = (WinUI.ItemContainer)bridge.GetElement(new ElementFactoryGetArgs { Data = 0 });
                if (templated)
                {
                    H.SetContent(row);
                    await Harness.Render();
                }
                var target = (FrameworkElement)ParkingContent(row)!;
                target.SetBinding(UIElement.VisibilityProperty, new Binding
                {
                    Source = source,
                    Path = new PropertyPath(nameof(UIElement.Visibility)),
                });
                bridge.RecycleElement(new ElementFactoryRecycleArgs { Element = row });
                H.Check($"ParkingBound_{templated}_Retired", factory.DebugRecyclePoolCount == 0
                    && !factory.DebugTryGetLastElementByControl(row, out _));
                H.Check($"ParkingBound_{templated}_Collapsed", target.Visibility == Visibility.Collapsed);
                H.Check($"ParkingBound_{templated}_NextRealizationFresh",
                    !ReferenceEquals(row, bridge.GetElement(new ElementFactoryGetArgs { Data = 0 })));
                H.SetContent(null);
            }

            var evictionFactory = CreateFactory(i => ItemContainer(TextBlock($"Row {i}")).WithKey($"row:{i}"));
            var evictionBridge = (IElementFactory)evictionFactory;
            WinUI.ItemContainer? first = null;
            for (var i = 0; i < 80; i++)
            {
                var row = (WinUI.ItemContainer)evictionBridge.GetElement(new ElementFactoryGetArgs { Data = i });
                first ??= row;
                evictionBridge.RecycleElement(new ElementFactoryRecycleArgs { Element = row });
            }
            H.Check("AnchorEviction_PoolBounded", evictionFactory.DebugRecyclePoolCount <= 32);
            H.Check("AnchorEviction_FirstRetired",
                first is not null && !evictionFactory.DebugTryGetLastElementByControl(first, out _));
            H.Check("AnchorEviction_StillVisibilityValidAndHidden",
                first is { Visibility: Visibility.Visible }
                && ParkingContent(first)?.Visibility == Visibility.Collapsed);
        }
    }

    internal class PendingBringThenKeyedReset(Harness h, bool variableHeight = false) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var histories = Enumerable.Range(0, 2).Select(session =>
                Enumerable.Range(0, 77).Select(index => (
                    Key: $"session:{session}|message:{index}",
                    Text: $"Session {session}, message {index}\n" + (variableHeight ? string.Concat(
                        Enumerable.Repeat("Variable height synthetic chat row.\n", index % 7 + 1)) : "")
                )).ToArray()).ToArray();
            Action<int>? switchSession = null;
            var renderedSession = -1;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (session, setSession) = ctx.UseState(0);
                switchSession = next => setSession(next);
                renderedSession = session;
                return ItemsView(histories[session], row => row.Key, (row, _) =>
                {
                    var element = ItemContainer(TextBlock(row.Text).TextWrapping(TextWrapping.Wrap)).WithKey(row.Key);
                    return variableHeight ? element : element.Height(60);
                }) with
                {
                    SelectionMode = ItemsViewSelectionMode.None,
                    IsItemInvokedEnabled = false,
                };
            });
            await Harness.Render();
            var view = H.FindControl<WinUI.ItemsView>(_ => true)
                ?? throw new InvalidOperationException("ItemsView was not mounted.");
            var repeater = H.FindControl<WinUI.ItemsRepeater>(_ => true)
                ?? throw new InvalidOperationException("ItemsRepeater was not mounted.");
            var scroll = H.FindControl<WinUI.ScrollView>(_ => true)
                ?? throw new InvalidOperationException("ScrollView was not mounted.");
            var observedAnchors = 0;
            var recycledAnchors = 0;
            var recycledTargets = 0;
            void ObserveAnchor(WinUI.ScrollView sender, ScrollingAnchorRequestedEventArgs args)
            {
                observedAnchors++;
                if (args.AnchorElement is WinUI.ItemContainer anchor
                    && ReferenceEquals(anchor.Parent, repeater) && repeater.GetElementIndex(anchor) < 0)
                    recycledAnchors++;
            }
            var options = new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 1 };
            var scrollOptions = new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore);

            try
            {
                for (var cycle = 0; cycle < (variableHeight ? 24 : 12); cycle++)
                {
                    var nextSession = 1 - renderedSession;
                    view.StartBringItemIntoView(76, options);
                    var oldTail = repeater.TryGetElement(76) as WinUI.ItemContainer
                        ?? throw new InvalidOperationException("Bring did not realize the current tail.");
                    switchSession!(nextSession);
                    H.Check($"AnchorReset_{cycle}_NewHistoryRendered",
                        await Harness.WaitFor(() => renderedSession == nextSession
                            && H.FindTextContaining($"Session {nextSession}, message ") is not null));
                    await Harness.Render(100);
                    if (cycle == 0)
                        scroll.AnchorRequested += ObserveAnchor;
                    H.Check($"AnchorReset_{cycle}_OldTargetNotCollapsed",
                        oldTail.Visibility == Visibility.Visible);
                    if (repeater.GetElementIndex(oldTail) < 0)
                        recycledTargets++;
                    H.Check($"AnchorReset_{cycle}_ClearedContentCollapsed",
                        H.FindAllControls<WinUI.ItemContainer>(_ => true)
                            .Where(row => ReferenceEquals(row.Parent, repeater) && repeater.GetElementIndex(row) < 0)
                            .All(row => ParkingContent(row)?.Visibility == Visibility.Collapsed));

                    scroll.ScrollTo(0, scroll.ScrollableHeight, scrollOptions);
                    // Fixed heights isolate exact tail positioning from WinUI's
                    // separate estimated-extent behavior on variable-height resets.
                    var reachedTail = variableHeight
                        ? await Harness.WaitFor(() => scroll.VerticalOffset > 0)
                        : await Harness.WaitFor(() => scroll.ScrollableHeight > 0
                            && Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1
                            && repeater.TryGetElement(76) is WinUI.ItemContainer
                                { Opacity: 1, IsEnabled: true, Child: WinUI.TextBlock text }
                            && text.Text.StartsWith($"Session {nextSession}, message 76\n", StringComparison.Ordinal));
                    H.Check($"AnchorReset_{cycle}_{(variableHeight ? "ScrolledForward" : "NewTailReached")}", reachedTail);
                    scroll.ScrollTo(0, 0, scrollOptions);
                    H.Check($"AnchorReset_{cycle}_CanScrollBackToFirstRow",
                        await Harness.WaitFor(() => scroll.VerticalOffset < 1
                            && repeater.TryGetElement(0) is WinUI.ItemContainer
                                { Opacity: 1, IsEnabled: true, Child: WinUI.TextBlock text }
                            && text.Text.StartsWith($"Session {nextSession}, message 0\n", StringComparison.Ordinal)));
                    await Harness.Render(150);
                }
            }
            finally
            {
                scroll.AnchorRequested -= ObserveAnchor;
            }
            H.Check("AnchorReset_ObservedNativeAnchorRequests", observedAnchors > 0);
            H.Check("AnchorReset_ExercisedRecycledBringTargets", recycledTargets > 0);
            H.Check("AnchorReset_OnlyRealizedRowsSelectedAsAnchors", recycledAnchors == 0);
        }
    }
}
