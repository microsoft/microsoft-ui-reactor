using Microsoft.UI.Reactor.Markdown;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

internal sealed class MarkdownSelectionInputFixture : Component
{
    private readonly MarkdownTextSelectionOptions _outerOptions = new();
    private readonly MarkdownTextSelectionOptions _innerOptions = new();
    private readonly MarkdownTextSelectionOptions _longOptions = new();
    private int _clicks;

    public override Element Render()
    {
        var (streamed, setStreamed) = UseState(false);
        Element Md(string text, MarkdownTextSelectionOptions options) =>
            Microsoft.UI.Reactor.Advanced.Factories.Markdown(text, new MarkdownOptions { TextSelection = options });
        var link = RichTextBlock("").Set(control =>
        {
            control.Blocks.Clear();
            var hyperlink = new Hyperlink();
            hyperlink.Inlines.Add(new Run { Text = "native link" });
            hyperlink.Click += (_, _) => _clicks++;
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(hyperlink);
            control.Blocks.Add(paragraph);
        });
        return VStack(
            TextBlock("Clicks 0").AutomationId("SelectionClicks").Set(text =>
            {
                var timer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                timer.Tick += (_, _) =>
                {
                    string caption = $"Clicks {_clicks}";
                    Reconciler.UpdateDefaultAutomationName(text, text.Text, caption);
                    text.Text = caption;
                };
                text.Loaded += (_, _) => timer.Start();
                text.Unloaded += (_, _) => timer.Stop();
            }),
            Button("Stream", () => setStreamed(true)).AutomationId("SelectionStream"),
            MarkdownTextSelection.Scope(_outerOptions, VStack(
                Md("outer text", _outerOptions),
                MarkdownTextSelection.Scope(_innerOptions, Md(streamed ? "inner text streamed" : "inner text", _innerOptions))
                    .AutomationId("SelectionInner"),
                MarkdownTextSelection.Surface("native link", link)).Set(TracePointerRouting))
                .AutomationId("SelectionOuter"),
            ItemsView(new[] { "message" }, row => row, (_, _) =>
                ItemContainer(MarkdownTextSelection.Scope(_longOptions,
                    Md(string.Join("\n\n", Enumerable.Range(0, 70).Select(i => $"drag paragraph {i:D2}")), _longOptions))
                    .AutomationId("SelectionLong")))
                .Height(180).AutomationId("SelectionTimeline")).Width(450);
    }

    private static void TracePointerRouting(StackPanel panel)
    {
        MarkdownSelectionHost? scope = null;
        var history = new Queue<string>();
        void Record(string kind, PointerRoutedEventArgs args)
        {
            if (scope is null) return;
            var point = args.GetCurrentPoint(scope);
            history.Enqueue($"{kind} source={args.OriginalSource.GetType().Name} id={args.Pointer.PointerId} " +
                $"left={point.Properties.IsLeftButtonPressed} xy={point.Position.X:F1},{point.Position.Y:F1} " +
                $"handled={args.Handled} captures={scope.PointerCaptures?.Count ?? 0} " +
                $"selection={scope.Document.Start}..{scope.Document.End}");
            while (history.Count > 20) history.Dequeue();
            AutomationProperties.SetHelpText(scope, string.Join(" | ", history));
        }
        PointerEventHandler pressed = (_, args) => Record("press", args);
        PointerEventHandler moved = (_, args) => Record("move", args);
        PointerEventHandler released = (_, args) => Record("release", args);
        PointerEventHandler lost = (_, args) => Record("capture-lost", args);
        panel.Loaded += (_, _) =>
        {
            if (scope is not null) return;
            for (DependencyObject? parent = panel; parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is MarkdownSelectionHost owner) { scope = owner; break; }
            if (scope is null) throw new InvalidOperationException("The pointer proof needs its enclosing selection scope.");
            scope.AddHandler(UIElement.PointerPressedEvent, pressed, true);
            scope.AddHandler(UIElement.PointerMovedEvent, moved, true);
            scope.AddHandler(UIElement.PointerReleasedEvent, released, true);
            scope.AddHandler(UIElement.PointerCaptureLostEvent, lost, true);
        };
        panel.Unloaded += (_, _) =>
        {
            if (scope is null) return;
            scope.RemoveHandler(UIElement.PointerPressedEvent, pressed);
            scope.RemoveHandler(UIElement.PointerMovedEvent, moved);
            scope.RemoveHandler(UIElement.PointerReleasedEvent, released);
            scope.RemoveHandler(UIElement.PointerCaptureLostEvent, lost);
            scope = null;
        };
    }
}
