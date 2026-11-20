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
        private ContentSizerTrace? _contentSizerTrace;
        private GridSplitterTrace? _gridSplitterTrace;
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
            _contentSizerTrace?.Dispose();
            _contentSizerTrace = null;
            _gridSplitterTrace?.Dispose();
            _gridSplitterTrace = null;
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
            if (Props == "csz" && _contentSizerTrace is null)
                _contentSizerTrace = new ContentSizerTrace(
                    elements.OfType<Wct.ContentSizer>().Single(),
                    elements.Single(e => AutomationProperties.GetAutomationId(e) == "ContentSizer.Target"));
            if (Props == "splitter" && _gridSplitterTrace is null)
            {
                var (grid, left, right) = FindAdvertisedGrid(elements);
                _gridSplitterTrace = new GridSplitterTrace(elements.OfType<Wct.GridSplitter>().Single(), grid, left, right);
            }
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

    private sealed class ContentSizerTrace : IDisposable
    {
        private readonly Wct.ContentSizer _sizer;
        private readonly FrameworkElement _advertisedTarget;
        private readonly List<string> _events = [];
        private int _gesture;

        public ContentSizerTrace(Wct.ContentSizer sizer, FrameworkElement advertisedTarget)
        {
            _sizer = sizer;
            _advertisedTarget = advertisedTarget;
            sizer.ManipulationStarting += Starting;
            sizer.ManipulationStarted += Started;
            sizer.ManipulationDelta += Delta;
            sizer.ManipulationCompleted += Completed;
            AutomationProperties.SetHelpText(sizer, "No completed native manipulation.");
        }

        public void Dispose()
        {
            _sizer.ManipulationStarting -= Starting;
            _sizer.ManipulationStarted -= Started;
            _sizer.ManipulationDelta -= Delta;
            _sizer.ManipulationCompleted -= Completed;
        }

        private void Starting(object sender, ManipulationStartingRoutedEventArgs args)
        {
            _events.Clear();
            var target = _sizer.TargetControl as FrameworkElement;
            _events.Add(FormattableString.Invariant($"""
                gesture={++_gesture}; orientation={_sizer.Orientation}; drag-increment={_sizer.DragIncrement:R}
                inverted={_sizer.IsDragInverted}; flow={_sizer.FlowDirection}; target-is-advertised={ReferenceEquals(target, _advertisedTarget)}
                initial-target-height={target?.Height:R}; initial-target-actual-height={target?.ActualHeight:R}
                initial-advertised-height={_advertisedTarget.Height:R}; initial-advertised-actual-height={_advertisedTarget.ActualHeight:R}; mode={args.Mode}
                minimum-height={target?.MinHeight:R}; maximum-height={target?.MaxHeight:R}; sibling-height={_sizer.ActualHeight:R}; sibling-width={_sizer.ActualWidth:R}
                """));
            Record("starting");
        }

        private void Started(object sender, ManipulationStartedRoutedEventArgs args) =>
            Record("started", cumulative: args.Cumulative.Translation);

        private void Delta(object sender, ManipulationDeltaRoutedEventArgs args) =>
            Record("delta", args.Delta.Translation, args.Cumulative.Translation);

        private void Completed(object sender, ManipulationCompletedRoutedEventArgs args)
        {
            Record("completed", cumulative: args.Cumulative.Translation);
            // Buffer only during input; publish non-layout metadata after completion, with
            // no UseState, RequestRender, control-value write or timer-cadence change.
            AutomationProperties.SetHelpText(_sizer, string.Join("\n", _events));
        }

        private void Record(string phase, global::Windows.Foundation.Point? delta = null,
            global::Windows.Foundation.Point? cumulative = null)
        {
            var target = _sizer.TargetControl as FrameworkElement;
            _events.Add(FormattableString.Invariant($"""
                phase={phase}; delta={FormatPoint(delta)}; cumulative={FormatPoint(cumulative)}
                target-height={target?.Height:R}; target-actual-height={target?.ActualHeight:R}; advertised-height={_advertisedTarget.Height:R}; advertised-actual-height={_advertisedTarget.ActualHeight:R}
                sibling-height={_sizer.ActualHeight:R}; sibling-width={_sizer.ActualWidth:R}
                """));
        }

    }

    private sealed class GridSplitterTrace : IDisposable
    {
        private readonly Wct.GridSplitter _splitter;
        private readonly Grid _grid;
        private readonly Border _left;
        private readonly Border _right;
        private readonly ColumnDefinition[] _columns;
        private readonly RowDefinition _row;
        private readonly List<string> _events = [];
        private int _gesture;

        public GridSplitterTrace(Wct.GridSplitter splitter, Grid grid, Border left, Border right)
        {
            if (grid.ColumnDefinitions.Count != 3 || grid.RowDefinitions.Count != 1)
                throw new InvalidOperationException("The advertised splitter grid must have three columns and one row.");
            _splitter = splitter;
            _grid = grid;
            _left = left;
            _right = right;
            _columns = grid.ColumnDefinitions.ToArray();
            _row = grid.RowDefinitions[0];
            splitter.ManipulationStarting += Starting;
            splitter.ManipulationStarted += Started;
            splitter.ManipulationDelta += Delta;
            splitter.ManipulationCompleted += Completed;
            AutomationProperties.SetHelpText(splitter, "No completed native manipulation.");
        }

        public void Dispose()
        {
            _splitter.ManipulationStarting -= Starting;
            _splitter.ManipulationStarted -= Started;
            _splitter.ManipulationDelta -= Delta;
            _splitter.ManipulationCompleted -= Completed;
        }

        private void Starting(object sender, ManipulationStartingRoutedEventArgs args)
        {
            _events.Clear();
            _events.Add(FormattableString.Invariant($"""
                gesture={++_gesture}; resize-direction={_splitter.ResizeDirection}; resize-behavior={_splitter.ResizeBehavior}; parent-level={_splitter.ParentLevel}
                orientation={_splitter.Orientation}; horizontal-alignment={_splitter.HorizontalAlignment}; vertical-alignment={_splitter.VerticalAlignment}
                flow={_splitter.FlowDirection}; drag-increment={_splitter.DragIncrement:R}; splitter-width={_splitter.ActualWidth:R}; splitter-height={_splitter.ActualHeight:R}
                """) + "\n" + Snapshot());
            Record("starting");
        }

        private void Started(object sender, ManipulationStartedRoutedEventArgs args) =>
            Record("started", cumulative: args.Cumulative.Translation);

        private void Delta(object sender, ManipulationDeltaRoutedEventArgs args) =>
            Record("delta", args.Delta.Translation, args.Cumulative.Translation);

        private void Completed(object sender, ManipulationCompletedRoutedEventArgs args)
        {
            Record("completed", cumulative: args.Cumulative.Translation);
            AutomationProperties.SetHelpText(_splitter, string.Join("\n", _events));
        }

        private void Record(string phase, global::Windows.Foundation.Point? delta = null,
            global::Windows.Foundation.Point? cumulative = null) =>
            _events.Add($"phase={phase}; delta={FormatPoint(delta)}; cumulative={FormatPoint(cumulative)}\n{Snapshot()}");

        private string Snapshot()
        {
            bool definitionsUnchanged = _grid.ColumnDefinitions.Count == 3 && _grid.RowDefinitions.Count == 1 &&
                ReferenceEquals(_grid.RowDefinitions[0], _row) &&
                Enumerable.Range(0, 3).All(i => ReferenceEquals(_grid.ColumnDefinitions[i], _columns[i]));
            var lines = new List<string>
            {
                FormattableString.Invariant($"""
                    parent-is-advertised={ReferenceEquals(_splitter.Parent, _grid)}; panes-associated={ReferenceEquals(_left.Parent, _grid) && ReferenceEquals(_right.Parent, _grid)}; definitions-unchanged={definitionsUnchanged}
                    splitter-column={Grid.GetColumn(_splitter)}; splitter-row={Grid.GetRow(_splitter)}; left-column={Grid.GetColumn(_left)}; left-row={Grid.GetRow(_left)}; right-column={Grid.GetColumn(_right)}; right-row={Grid.GetRow(_right)}
                    grid-width={_grid.ActualWidth:R}; grid-height={_grid.ActualHeight:R}; left-pane-width={_left.ActualWidth:R}; right-pane-width={_right.ActualWidth:R}
                    row-unit={_row.Height.GridUnitType}; row-value={_row.Height.Value:R}; row-actual={_row.ActualHeight:R}
                    """)
            };
            for (int i = 0; i < _columns.Length; i++)
            {
                var column = _columns[i];
                lines.Add(FormattableString.Invariant(
                    $"column-{i}-unit={column.Width.GridUnitType}; column-{i}-value={column.Width.Value:R}; column-{i}-actual={column.ActualWidth:R}; column-{i}-min={column.MinWidth:R}; column-{i}-max={column.MaxWidth:R}"));
            }
            return string.Join("\n", lines);
        }
    }

    private static string FormatPoint(global::Windows.Foundation.Point? point) =>
        point is { } value ? FormattableString.Invariant($"{value.X:R},{value.Y:R}") : "<not supplied>";

    private static (Grid Grid, Border Left, Border Right) FindAdvertisedGrid(FrameworkElement[] elements)
    {
        static bool IsPane(Border pane, string label) => pane.Child is TextBlock text && text.Text == label;
        var grid = elements.OfType<Grid>().Single(g =>
            g.Children.OfType<Border>().Any(b => IsPane(b, "Left")) &&
            g.Children.OfType<Border>().Any(b => IsPane(b, "Right")));
        return (grid, grid.Children.OfType<Border>().Single(b => IsPane(b, "Left")),
            grid.Children.OfType<Border>().Single(b => IsPane(b, "Right")));
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
                var target = elements.Single(e => AutomationProperties.GetAutomationId(e) == "ContentSizer.Target");
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
                var (grid, left, right) = FindAdvertisedGrid(elements);
                var columns = grid.ColumnDefinitions;
                return FormattableString.Invariant(
                    $"{columns[0].ActualWidth:R}|{columns[1].ActualWidth:R}|{columns[2].ActualWidth:R}|{left.ActualWidth:R}|{right.ActualWidth:R}|{grid.ActualWidth:R}|{grid.ActualHeight:R}");
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
