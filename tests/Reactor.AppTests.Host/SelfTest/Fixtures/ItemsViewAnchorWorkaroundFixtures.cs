using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// Workaround regression for https://github.com/microsoft/microsoft-ui-xaml/issues/11865.
internal static class ItemsViewAnchorWorkaroundFixtures
{
    private static ElementFactory<int> CreateFactory(Func<int, Element> row) =>
        new(Enumerable.Range(0, 80).ToArray(), (i, _) => row(i),
            new Reconciler(), requestRerender: static () => { }, pool: null);

    internal class PoolRestoresValueSources(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            foreach (var kind in new[] { "Default", "Local", "Style", "BoundVisibility", "Transition" })
            {
                var source = new WinUI.Border { Visibility = Visibility.Visible };
                var opacityTransition = new ScalarTransition { Duration = TimeSpan.FromSeconds(1) };
                var style = new Style(typeof(WinUI.ItemContainer));
                style.Setters.Add(new Setter(UIElement.OpacityProperty, 0.6));
                style.Setters.Add(new Setter(Control.IsEnabledProperty, false));
                var factory = CreateFactory(i => ItemContainer(Button($"Row {i}", () => { }))
                    .Set(row =>
                    {
                        if (kind == "Local")
                        {
                            row.Opacity = 0.4;
                            row.IsEnabled = false;
                            row.Visibility = Visibility.Collapsed;
                        }
                        else if (kind == "Style")
                        {
                            row.Style = style;
                        }
                        else if (kind == "BoundVisibility")
                        {
                            row.SetBinding(UIElement.VisibilityProperty, new Binding
                            {
                                Source = source,
                                Path = new PropertyPath(nameof(UIElement.Visibility)),
                                Mode = BindingMode.OneWay,
                            });
                        }
                        else if (kind == "Transition")
                        {
                            row.OpacityTransition = opacityTransition;
                        }
                    }));
                var bridge = (IElementFactory)factory;
                var row = (WinUI.ItemContainer)bridge.GetElement(new ElementFactoryGetArgs { Data = 0 });
                H.SetContent(row);
                await Harness.Render();
                var opacity = row.ReadLocalValue(UIElement.OpacityProperty);
                var enabled = row.ReadLocalValue(Control.IsEnabledProperty);
                var visibility = row.ReadLocalValue(UIElement.VisibilityProperty);
                var transition = row.OpacityTransition;
                var effectiveOpacity = row.Opacity;
                var effectiveEnabled = row.IsEnabled;
                var child = (WinUI.Button)row.Child;

                bridge.RecycleElement(new ElementFactoryRecycleArgs { Element = row });
                H.Check($"AnchorPark_{kind}_KeepsVisibilitySource",
                    Equals(visibility, row.ReadLocalValue(UIElement.VisibilityProperty)));
                H.Check($"AnchorPark_{kind}_HiddenAndDisabled", row.Opacity == 0 && !row.IsEnabled);
                H.Check($"AnchorPark_{kind}_CannotFadeWhileParked", row.OpacityTransition is null);
                H.Check($"AnchorPark_{kind}_ChildCannotFocus", !child.Focus(FocusState.Programmatic));
                H.Check($"AnchorPark_{kind}_Pooled", factory.DebugRecyclePoolCount == 1);

                var reused = bridge.GetElement(new ElementFactoryGetArgs { Data = 0 });
                H.Check($"AnchorPark_{kind}_ReusesContainer", ReferenceEquals(row, reused));
                H.Check($"AnchorPark_{kind}_RestoresOpacitySource",
                    Equals(opacity, row.ReadLocalValue(UIElement.OpacityProperty)));
                H.Check($"AnchorPark_{kind}_RestoresEnabledSource",
                    Equals(enabled, row.ReadLocalValue(Control.IsEnabledProperty)));
                H.Check($"AnchorPark_{kind}_RestoresTransitionSource",
                    Equals(transition, row.OpacityTransition));
                H.Check($"AnchorPark_{kind}_RestoresEffectiveValues",
                    row.Opacity == effectiveOpacity && row.IsEnabled == effectiveEnabled);
                if (kind == "BoundVisibility")
                {
                    source.Visibility = Visibility.Collapsed;
                    await Harness.Render();
                    H.Check("AnchorPark_VisibilityBindingStillLive", row.Visibility == Visibility.Collapsed);
                }
                H.SetContent(null);
            }
        }
    }

    internal class BoundRowsAndEviction(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            foreach (var property in new[] { UIElement.OpacityProperty, Control.IsEnabledProperty })
            {
                var source = new WinUI.Button { Opacity = 0.7, IsEnabled = true };
                var factory = CreateFactory(i => ItemContainer(TextBlock($"Row {i}"))
                    .Set(row => row.SetBinding(property, new Binding
                    {
                        Source = source,
                        Path = new PropertyPath(property == UIElement.OpacityProperty
                            ? nameof(UIElement.Opacity) : nameof(Control.IsEnabled)),
                    })));
                var bridge = (IElementFactory)factory;
                var row = (WinUI.ItemContainer)bridge.GetElement(new ElementFactoryGetArgs { Data = 0 });
                H.Check("AnchorRetire_HasBinding",
                    !ReferenceEquals(row.ReadLocalValue(property), DependencyProperty.UnsetValue)
                    && row.ReadLocalValue(property) is not double and not bool);
                bridge.RecycleElement(new ElementFactoryRecycleArgs { Element = row });
                H.Check("AnchorRetire_NotPooled", factory.DebugRecyclePoolCount == 0);
                H.Check("AnchorRetire_Untracked", !factory.DebugTryGetLastElementByControl(row, out _));
                H.Check("AnchorRetire_VisibleButHiddenAndDisabled",
                    row.Visibility == Visibility.Visible && row.Opacity == 0 && !row.IsEnabled);
                H.Check("AnchorRetire_NextRealizationIsFresh",
                    !ReferenceEquals(row, bridge.GetElement(new ElementFactoryGetArgs { Data = 0 })));
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
                first is { Visibility: Visibility.Visible, Opacity: 0, IsEnabled: false });
            return Task.CompletedTask;
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
            var retiredAnchors = 0;
            void ObserveAnchor(WinUI.ScrollView sender, ScrollingAnchorRequestedEventArgs args)
            {
                observedAnchors++;
                if (args.AnchorElement is WinUI.ItemContainer anchor
                    && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(anchor) is WinUI.ItemsRepeater owner
                    && owner.GetElementIndex(anchor) < 0)
                    retiredAnchors++;
            }
            var options = new BringIntoViewOptions { AnimationDesired = false, VerticalAlignmentRatio = 1 };
            var scrollOptions = new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore);

            try
            {
                for (var cycle = 0; cycle < 12; cycle++)
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
                    H.Check($"AnchorReset_{cycle}_ClearedRowsCannotPaintOrReceiveInput",
                        H.FindAllControls<WinUI.ItemContainer>(_ => true)
                            .Where(row => ReferenceEquals(row.Parent, repeater) && repeater.GetElementIndex(row) < 0)
                            .All(row => row.Opacity == 0 && !row.IsEnabled));

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
            H.Check("AnchorReset_NoRecycledAnchorEscapesGuard", retiredAnchors == 0);
        }
    }
}
