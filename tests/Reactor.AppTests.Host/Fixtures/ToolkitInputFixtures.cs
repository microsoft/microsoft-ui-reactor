using System.Collections;
using System.Globalization;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Wct = CommunityToolkit.WinUI.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

internal static class ToolkitInputFixtures
{
    public static Element Build(string route) => Component<InputPage, string>(route).WithKey(route);

    private sealed class InputPage : Component<string>
    {
        private FrameworkElement? _root;
        private DispatcherTimer? _timer;
        private long _sequence;

        public override Element Render() =>
            VStack(4,
                WctControls.SamplePages.CreatePage(Props),
                TextBlock("Waiting").AutomationId("ToolkitInput.Observation"),
                TextBlock("Waiting").AutomationId("ToolkitInput.Focus"),
                TextBlock("Waiting").AutomationId("ToolkitInput.Dpi"))
            .Width(740)
            .OnMount(Attach)
            .OnUnmount(Detach);

        private void Attach(FrameworkElement root)
        {
            _root = root;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += Observe;
            root.Loaded += Start;
            root.Unloaded += Stop;
            if (root.IsLoaded) _timer.Start();
        }

        private void Detach(FrameworkElement root)
        {
            root.Loaded -= Start;
            root.Unloaded -= Stop;
            if (_timer is { } timer)
            {
                timer.Stop();
                timer.Tick -= Observe;
            }
            _timer = null;
            _root = null;
        }

        private void Start(object sender, RoutedEventArgs args) => _timer?.Start();
        private void Stop(object sender, RoutedEventArgs args) => _timer?.Stop();

        private void Observe(object? sender, object args)
        {
            if (_root is not { IsLoaded: true } root) return;
            var elements = Descendants(root).OfType<FrameworkElement>().ToArray();
            var observation = Read(Props, elements);
            // A fresh sequence lets tests reject a pre-input snapshot, especially when
            // the controlled parent rejects an edit and the expected value is unchanged.
            Write(elements, "ToolkitInput.Observation", $"{++_sequence}\n{observation}");
            Write(elements, "ToolkitInput.Focus",
                FocusManager.GetFocusedElement(root.XamlRoot) is FrameworkElement focused
                    ? AutomationProperties.GetAutomationId(focused) : "");
            Write(elements, "ToolkitInput.Dpi",
                root.XamlRoot.RasterizationScale.ToString("R", CultureInfo.InvariantCulture));
        }
    }

    // Only instrumentation is written here: IDs on native template parts and observation
    // labels. Values, tokens, selection, focus and callbacks remain owned by the real sample.
    private static string Read(string route, FrameworkElement[] elements)
    {
        switch (route)
        {
            case "range":
            {
                var range = elements.OfType<Wct.RangeSelector>().Single();
                foreach (var thumb in Descendants(range).OfType<Thumb>())
                    Identify(thumb, $"ToolkitInput.{thumb.Name}");
                return FormattableString.Invariant($"{range.RangeStart:R}|{range.RangeEnd:R}");
            }
            case "rsb":
            {
                var box = elements.OfType<Wct.RichSuggestBox>().Single();
                var editor = Descendants(box).OfType<RichEditBox>().SingleOrDefault();
                if (editor is null || box.TextDocument is null) return "Waiting";
                Identify(editor, "ToolkitInput.RichEditor");
                box.TextDocument.GetText(TextGetOptions.NoHidden, out var text);
                return $"{string.Join('|', box.Tokens.Select(t => $"{t.DisplayText}={t.Item}"))}\n" +
                    $"{text.TrimEnd('\r', '\n', ' ')}\n{Strings(box.ItemsSource)}";
            }
            case "tokens":
            {
                var box = elements.OfType<Wct.TokenizingTextBox>().Single();
                var editor = Descendants(box).OfType<TextBox>().SingleOrDefault();
                if (editor is null) return "Waiting";
                Identify(editor, "ToolkitInput.TokenEditor");
                foreach (var token in box.Items.OfType<string>())
                {
                    if (box.ContainerFromItem(token) is not DependencyObject container) continue;
                    foreach (var button in Descendants(container).OfType<Button>()
                        .Where(b => b.Name == "PART_RemoveButton"))
                        Identify(button, $"ToolkitInput.Remove.{token}");
                }
                return $"{string.Join('|', box.Items.OfType<string>())}\n{box.Text}\n{Strings(box.SuggestedItemsSource)}";
            }
            case "csz":
            {
                var sizer = elements.OfType<Wct.ContentSizer>().Single();
                if (sizer.TargetControl is not FrameworkElement target) return "Waiting";
                return FormattableString.Invariant($"{target.Height:R}|{target.ActualHeight:R}|{target.ActualWidth:R}");
            }
            case "psz":
            {
                var sizer = elements.OfType<Wct.PropertySizer>().Single();
                Identify(sizer, "ToolkitInput.PropertySizer");
                var target = elements.OfType<Border>().Single(b =>
                    b.Child is TextBlock text && text.Text.StartsWith("Pane", StringComparison.Ordinal));
                Identify(target, "ToolkitInput.PropertyTarget");
                return FormattableString.Invariant($"{sizer.Binding:R}|{target.Width:R}|{target.ActualWidth:R}");
            }
            case "splitter":
            {
                var splitter = elements.OfType<Wct.GridSplitter>().Single();
                Identify(splitter, "ToolkitInput.GridSplitter");
                var grid = (Grid)splitter.Parent;
                var columns = grid.ColumnDefinitions;
                return FormattableString.Invariant(
                    $"{columns[0].ActualWidth:R}|{columns[1].ActualWidth:R}|{columns[2].ActualWidth:R}");
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(route), route, "Not a Toolkit input fixture.");
        }
    }

    private static string Strings(object source) => string.Join('|', ((IEnumerable)source).Cast<string>());

    private static void Identify(FrameworkElement element, string id)
    {
        if (AutomationProperties.GetAutomationId(element) != id)
            AutomationProperties.SetAutomationId(element, id);
    }

    private static void Write(FrameworkElement[] elements, string id, string value)
    {
        var text = elements.OfType<TextBlock>().Single(t => AutomationProperties.GetAutomationId(t) == id);
        if (text.Text == value) return;
        Reconciler.UpdateDefaultAutomationName(text, text.Text, value);
        text.Text = value;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var descendant in Descendants(VisualTreeHelper.GetChild(root, i)))
                yield return descendant;
    }
}
