using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ScrollBehaviorFixtures
{
    internal sealed class TextSubmit(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            var submissions = new List<string>();
            host.Mount(_ => TextBox("declared").AcceptsReturn().SubmitOnEnter(submissions.Add).AutomationName("Draft"));
            await Harness.Render();
            var text = H.FindControl<TextBox>(_ => true)!;
            text.Text = "native edit";
            H.Check("Submit_ShiftEnterRemainsNative", !TextBoxSubmitBehavior.TrySubmit(text, true, false, false, false));
            H.Check("Submit_ControlEnterRemainsNative", !TextBoxSubmitBehavior.TrySubmit(text, false, true, false, false));
            var payload = Reconciler.GetOrCreateControlEventPayload<TextBoxEventPayload>(text);
            payload.IsComposing = true;
            H.Check("Submit_CompositionNotSent", !TextBoxSubmitBehavior.TrySubmit(text, false, false, false, false) && submissions.Count == 0);
            payload.IsComposing = false;
            H.Check("Submit_RepeatConsumedWithoutSending", TextBoxSubmitBehavior.TrySubmit(text, false, false, false, true) && submissions.Count == 0);
            H.Check("Submit_ReadsNativeText", TextBoxSubmitBehavior.TrySubmit(text, false, false, false, false)
                && submissions.SequenceEqual(["native edit"]));
            var updated = "";
            host.Mount(_ => TextBox("next").AcceptsReturn().SubmitOnEnter(value => updated = value).AutomationName("Draft"));
            await Harness.Render();
            TextBoxSubmitBehavior.TrySubmit(text, false, false, false, false);
            H.Check("Submit_CallbackReplaced", updated == "next" && submissions.Count == 1);
            host.Mount(_ => TextBox("plain").AcceptsReturn().AutomationName("Draft"));
            await Harness.Render();
            H.Check("Submit_RemovalRestoresEnter", !TextBoxSubmitBehavior.TrySubmit(text, false, false, false, false));
            host.Mount(_ => Empty());
            host.Mount(_ => TextBox("reused").SubmitOnEnter(submissions.Add).AutomationName("Draft"));
            await Harness.Render();
            var reused = H.FindControl<TextBox>(_ => true)!;
            TextBoxSubmitBehavior.TrySubmit(reused, false, false, false, false);
            H.Check("Submit_NewOwnerOnly", submissions.SequenceEqual(["native edit", "reused"]));
        }
    }

    internal sealed class TailFollowing(Harness h) : SelfTestFixtureBase(h)
    {
        private sealed record Row(string Key, double Height);

        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            var rows = Enumerable.Range(0, 60).Select(i => new Row($"row-{i}", 45 + i % 3 * 20)).ToArray();
            var tailRequest = 0;
            string? target = null;
            var navigation = 0;
            var following = true;
            Element Render() => ItemsView(rows, r => r.Key,
                    (r, _) => ItemContainer(TextBlock(r.Key)).Height(r.Height))
                .Height(260).Width(500)
                .FollowTail(onFollowingChanged: value => following = value)
                .ScrollToTail(tailRequest).ScrollIntoView(target, navigation);
            host.Mount(_ => Render());
            await Harness.Render();
            var items = H.FindControl<ItemsView>(_ => true)!;
            bool AtTail() => items.ScrollView is { ScrollableHeight: > 1000 } scroll
                && Math.Abs(scroll.ScrollableHeight - scroll.VerticalOffset) < 2
                && !ItemsViewScrollBehavior.IsNavigationPending(items);
            TextBlock? TailText() => (items.ScrollView?.Content as ItemsRepeater)?
                .TryGetElement(rows.Length - 1) is ItemContainer { Child: TextBlock text } ? text : null;
            FrameworkElement? TailRow() => (items.ScrollView?.Content as ItemsRepeater)?
                .TryGetElement(rows.Length - 1) as FrameworkElement;
            H.Check("Tail_InitialVirtualizedLanding", await Harness.WaitForWithin(AtTail, 5000));
            rows = [.. rows, new("appended", 240)];
            host.Mount(_ => Render());
            H.Check("Tail_AppendedRowFollowed", await Harness.WaitForWithin(() => AtTail()
                && TailText()?.Text == "appended", 5000),
                $"offset={items.ScrollView?.VerticalOffset} extent={items.ScrollView?.ScrollableHeight} pending={ItemsViewScrollBehavior.IsNavigationPending(items)}");
            rows = [.. rows[..^1], rows[^1] with { Height = 520 }];
            host.Mount(_ => Render());
            H.Check("Tail_StreamingRowGrowth", await Harness.WaitForWithin(() => AtTail()
                && TailRow()?.ActualHeight == 520, 5000),
                $"offset={items.ScrollView?.VerticalOffset} extent={items.ScrollView?.ScrollableHeight} rowHeight={TailRow()?.ActualHeight} following={following} pending={ItemsViewScrollBehavior.IsNavigationPending(items)}");
            items.ScrollView!.ScrollTo(0, 0,
                new ScrollingScrollOptions(ScrollingAnimationMode.Disabled, ScrollingSnapPointsMode.Ignore));
            H.Check("Tail_ReadingHistoryPauses", await Harness.WaitForWithin(() => !following && items.ScrollView.VerticalOffset < 2, 3000));
            rows = [.. rows, new("while-reading", 180)];
            host.Mount(_ => Render());
            await Harness.Render();
            await Harness.Render();
            H.Check("Tail_AppendDoesNotStealPosition", items.ScrollView.VerticalOffset < 2 && !following);
            target = "row-30";
            navigation++;
            rows = [new("prepended", 90), .. rows];
            host.Mount(_ => Render());
            H.Check("Tail_KeyNavigationSurvivesPrepend", await Harness.WaitForWithin(() =>
            {
                var row = H.FindText("row-30");
                return row is not null && Math.Abs(row.TransformToVisual(items.ScrollView).TransformPoint(default).Y) < 3;
            }, 5000));
            tailRequest++;
            host.Mount(_ => Render());
            H.Check("Tail_ExplicitJumpResumes", await Harness.WaitForWithin(() => AtTail() && following, 5000));
            rows = [.. rows, new("pending-at-unmount", 80)];
            host.Mount(_ => Render());
            host.Mount(_ => TextBlock("Navigation disposed"));
            await Harness.Render();
            H.Check("Tail_UnmountedNavigationCannotReplaceContent", H.FindText("Navigation disposed") is not null
                && H.FindControl<ItemsView>(_ => true) is null);
        }
    }

    internal sealed class Annotations(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            var barRef = new ElementRef<AnnotatedScrollBar>(new ElementRef());
            var connected = true;
            ScrollAnnotation[] annotations = [new(0, "Start", "First section"), new(600, "Middle", "Second section")];
            Element Render() => Grid(columns: [GridSize.Star(), GridSize.Px(100)], rows: [GridSize.Star()],
                children:
                [
                    ScrollView(TextBlock("Tall content").Height(1400))
                        .VerticalScrollBar(connected ? barRef : null).Grid(column: 0),
                    AnnotatedScrollBar().Ref(barRef).Annotations(annotations).AutomationName("Sections").Grid(column: 1),
                ]).Height(300);
            host.Mount(_ => Render());
            await Harness.Render();
            var view = H.FindControl<ScrollView>(_ => true)!;
            var bar = barRef.Current!;
            H.Check("Annotations_NativeControllerConnected", await Harness.WaitForWithin(() =>
                ReferenceEquals(view.ScrollPresenter?.VerticalScrollController, bar.ScrollController), 2000));
            H.Check("Annotations_LabelsHaveOffsets", bar.Labels.Count == 2 && bar.Labels[1].ScrollOffset == 600);
            var firstLabel = bar.Labels[0];
            annotations = [new(0, "Start", "Streaming preview updated"), annotations[1]];
            host.Mount(_ => Render());
            await Harness.Render();
            H.Check("Annotations_PreviewUpdateKeepsNativeLabels", ReferenceEquals(firstLabel, bar.Labels[0]));
            annotations = [new(300, "Replacement", "Updated detail")];
            host.Mount(_ => Render());
            await Harness.Render();
            H.Check("Annotations_LabelsReconciled", bar.Labels.Count == 1 && bar.Labels[0].ScrollOffset == 300
                && Equals(bar.Labels[0].Content, "Replacement"));
            connected = false;
            host.Mount(_ => Render());
            await Harness.Render();
            H.Check("Annotations_ControllerRestoredOnRemoval", !ReferenceEquals(view.ScrollPresenter.VerticalScrollController, bar.ScrollController));
            annotations = [];
            host.Mount(_ => Render());
            await Harness.Render();
            H.Check("Annotations_Cleared", bar.Labels.Count == 0);
        }
    }

    internal sealed class PaneResizing(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            var enabled = true;
            var callbackCount = 0;
            var length = 240.0;
            var title = "Original pane";
            Element Render() => (SplitView(TextBox(title).AutomationName("Pane input"), TextBlock("Main content")) with
                { DisplayMode = SplitViewDisplayMode.Inline, OpenPaneLength = length })
                .ResizablePane(160, 360, value => { length = value; callbackCount++; }, enabled: enabled)
                .Width(800).Height(240);
            host.Mount(_ => Render());
            await Harness.Render();
            var split = H.FindControl<SplitView>(_ => true)!;
            var child = H.FindControl<TextBox>(_ => true)!;
            SplitViewResizeBehavior.ResizePane(split, 900);
            H.Check("Resize_ClampedAndReported", split.OpenPaneLength == 360 && length == 360 && callbackCount == 1);
            SplitViewResizeBehavior.ResizePane(split, 50);
            H.Check("Resize_Minimum", split.OpenPaneLength == 160 && length == 160 && callbackCount == 2);
            title = "Updated pane";
            host.Mount(_ => Render());
            await Harness.Render();
            H.Check("Resize_ChildReconcilesThroughWrapper", ReferenceEquals(child, H.FindControl<TextBox>(_ => true)) && child.Text == title);
            H.Check("Resize_HandleHasName", H.FindControl<Button>(b => AutomationProperties.GetName(b) == "Resize pane") is not null);
            enabled = false;
            host.Mount(_ => Render());
            await Harness.Render();
            H.Check("Resize_DisablingPreservesPane", ReferenceEquals(split.Pane, child)
                && H.FindControl<Button>(b => AutomationProperties.GetName(b) == "Resize pane") is null);
            SplitViewResizeBehavior.ResizePane(split, 320);
            H.Check("Resize_RemovedBehaviorIsInert", split.OpenPaneLength == 160 && callbackCount == 2);
        }
    }
}
