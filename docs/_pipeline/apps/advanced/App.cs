using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Input;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Component = Microsoft.UI.Reactor.Core.Component;

ReactorApp.Run<AdvancedApp>("Advanced Patterns", width: 650, height: 700
);

// <snippet:error-boundary>
class ErrorBoundaryDemo : Component
{
    public override Element Render()
    {
        return VStack(12,
            SubHeading("Error Boundary"),
            ErrorBoundary(
                Component<BuggyComponent>(),
                (Exception ex) => VStack(8,
                    TextBlock("Something went wrong").Bold()
                        .Foreground(Theme.SystemCritical),
                    TextBlock(ex.Message).FontSize(12).Opacity(0.7)
                ).Padding(12)
                 .Background(Theme.SystemCriticalBackground)
                 .CornerRadius(8)
            )
        ).Padding(24);
    }
}

class BuggyComponent : Component
{
    public override Element Render()
    {
        var (crash, setCrash) = UseState(false);
        if (crash) throw new InvalidOperationException("Oops!");
        return Button("Click to crash", () => setCrash(true));
    }
}
// </snippet:error-boundary>

// <snippet:memo-subtree>
class MemoSubtreeDemo : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var (label, setLabel) = UseState("Expensive");

        return VStack(12,
            SubHeading("Memo"),
            TextBlock($"Parent renders: click count = {count}"),
            Button("Increment", () => setCount(count + 1)),
            Memo(ctx =>
            {
                // This subtree only re-renders when label changes
                return Border(
                    VStack(4,
                        TextBlock($"Memoized: {label}").Bold(),
                        TextBlock("Skips re-render when deps unchanged")
                            .FontSize(12).Opacity(0.6)
                    ).Padding(12)
                ).Background(Theme.CardBackground).CornerRadius(8);
            }, label)
        ).Padding(24);
    }
}
// </snippet:memo-subtree>

// <snippet:set-escape-hatch>
class SetEscapeHatchDemo : Component
{
    public override Element Render()
    {
        return VStack(12,
            SubHeading(".Set() Escape Hatch"),
            // Tooltip and padding are first-class modifiers — reach for those
            // first. ClickMode has no modifier, so it needs the escape hatch.
            Button("Custom Tooltip", () => { })
                .ToolTip("This is a native tooltip")
                .Padding(20, 10, 20, 10)
                .Set(btn => btn.ClickMode = ClickMode.Press),
            TextBlock("Styled via .Set()")
                .TextWrapping(TextWrapping.WrapWholeWords)
                .CharacterSpacing(80)
                .IsTextSelectionEnabled()
                .Set(tb =>
                {
                    // No TextBlockElement modifiers for these two.
                    tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                    tb.IsTextScaleFactorEnabled = false;
                })
        ).Padding(24);
    }
}
// </snippet:set-escape-hatch>

// <snippet:observable-viewmodel-class>
class SettingsViewModel : INotifyPropertyChanged
{
    private string _userName = "Alice";
    private bool _darkMode;
    private int _fontSize = 14;

    public string UserName
    {
        get => _userName;
        set { _userName = value; Notify(nameof(UserName)); }
    }
    public bool DarkMode
    {
        get => _darkMode;
        set { _darkMode = value; Notify(nameof(DarkMode)); }
    }
    public int FontSize
    {
        get => _fontSize;
        set { _fontSize = value; Notify(nameof(FontSize)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string n) =>
        PropertyChanged?.Invoke(this, new(n));
}
// </snippet:observable-viewmodel-class>

// <snippet:observable-viewmodel>
class ObservableTreeDemo : Component
{
    private static readonly SettingsViewModel _vm = new();

    public override Element Render()
    {
        var vm = UseObservableTree(_vm);

        return VStack(12,
            SubHeading("UseObservableTree"),
            TextBox(vm.UserName, v => vm.UserName = v,
                header: "User Name"),
            ToggleSwitch(vm.DarkMode, v => vm.DarkMode = v,
                header: "Dark Mode"),
            Slider(vm.FontSize, 10, 32, v => vm.FontSize = (int)v)
                .AutomationName("Font size"),
            TextBlock($"Preview: {vm.UserName}")
                .FontSize(vm.FontSize).Bold()
        ).Padding(24);
    }
}
// </snippet:observable-viewmodel>

// <snippet:observable-collection>
class ObservableCollectionDemo : Component
{
    private record TaskItem(int Id, string Title);

    private static int _nextId = 3;
    private static readonly ObservableCollection<TaskItem> _tasks = new()
        { new TaskItem(1, "Review pull request"), new TaskItem(2, "Update documentation") };

    public override Element Render()
    {
        var tasks = UseCollection(_tasks);
        var (input, setInput) = UseState("");

        return VStack(12,
            SubHeading("UseCollection"),
            HStack(8,
                TextBox(input, setInput, placeholderText: "New task")
                    .AutomationName("New task")
                    .Width(200),
                Button("Add", () => {
                    if (!string.IsNullOrWhiteSpace(input))
                    { _tasks.Add(new TaskItem(_nextId++, input.Trim())); setInput(""); }
                })
            ),
            TextBlock($"{tasks.Count} tasks:").SemiBold(),
            VStack(4, tasks.Select((task, i) =>
                HStack(8,
                    // The index is fine for display; it must not become the key.
                    TextBlock($"{i + 1}. {task.Title}"),
                    Button("Remove", () => _tasks.Remove(task))
                        .AutomationName($"Remove {task.Title}")
                ).WithKey(task.Id.ToString())   // stable identity survives removal
            ).ToArray())
        ).Padding(24);
    }
}
// </snippet:observable-collection>

// <snippet:element-ref-focus>
class ElementRefFocusDemo : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");
        var fieldRef = this.UseElementRef<TextBox>();

        return VStack(12,
            SubHeading("Imperative focus via ElementRef<T>"),
            TextBox(name, setName, placeholderText: "Name")
                .AutomationName("Name")
                .Ref(fieldRef),
            Button("Focus the field", () =>
                fieldRef.Current?.Focus(FocusState.Programmatic))
        ).Padding(24);
    }
}
// </snippet:element-ref-focus>

// <snippet:custom-hook>
// Custom hook — composes UseState + UseEffect on RenderContext. Treated like
// a built-in hook from any function-component Render. Same hook-rules apply:
// call unconditionally, at the top of render, in the same order every time.
static class TogglerHook
{
    public static (bool IsOn, Action Toggle) UseToggler(this RenderContext ctx, bool initial = false)
    {
        var (on, setOn) = ctx.UseState(initial);
        return (on, () => setOn(!on));
    }
}

class CustomHookDemo : Component
{
    public override Element Render() => Memo(ctx =>
    {
        var (isOn, toggle) = ctx.UseToggler();
        return VStack(8,
            SubHeading("Custom hook: UseToggler"),
            Button(isOn ? "On" : "Off", toggle)
                .AutomationName("Toggle state"),
            TextBlock(isOn ? "State is on." : "State is off.")
                .Foreground(isOn ? Theme.SystemSuccess : Theme.SecondaryText)
        ).Padding(24);
    });
}
// </snippet:custom-hook>

// <snippet:error-boundary-retry>
class ErrorBoundaryRetryDemo : Component
{
    public override Element Render()
    {
        var (resetKey, setResetKey) = UseState(0);

        return VStack(12,
            SubHeading("ErrorBoundary with retry"),
            ErrorBoundary(
                Component<FlakyComponent>().WithKey($"flaky-{resetKey}"),
                ex => VStack(8,
                    TextBlock("Couldn't load.").Bold().Foreground(Theme.SystemCritical),
                    TextBlock(ex.Message).FontSize(12).Opacity(0.7),
                    // Bumping resetKey reassigns identity to the child, so the
                    // ErrorBoundary mounts a fresh subtree on the next render.
                    Button("Retry", () => setResetKey(resetKey + 1))
                ).Padding(12).Background(Theme.SystemCriticalBackground).CornerRadius(8)
            )
        ).Padding(24);
    }
}

class FlakyComponent : Component
{
    public override Element Render()
    {
        var (attempt, _) = UseState(Random.Shared.Next(0, 3));
        if (attempt == 0) throw new InvalidOperationException("Service unavailable");
        return TextBlock("Loaded.").Foreground(Theme.SystemSuccess);
    }
}
// </snippet:error-boundary-retry>

// <snippet:snap-back>
class SnapBackDemo : Component
{
    public override Element Render()
    {
        // RenderContext.UseReducer<T>(T initialValue) returns
        // (T Value, Action<Func<T, T>> Update). Toggling the bool guarantees
        // a changed reducer result and therefore a re-render.
        var (_, bump) = UseReducer(false);

        return Slider(
            value: Optional<double>.Of(5.0),
            onValueChanged: _ => bump(b => !b));
    }
}
// </snippet:snap-back>

// <snippet:clear-value-channel>
public sealed record CardElement : Element
{
    public Optional<Brush?> Background { get; init; } = Optional<Brush?>.Unset;
}

static class CardDescriptorHost
{
    public static readonly ControlDescriptor<CardElement, Microsoft.UI.Xaml.Controls.Border> Descriptor =
        new ControlDescriptor<CardElement, Microsoft.UI.Xaml.Controls.Border>()
            .OneWay(
                get: static e => e.Background,
                set: static (c, v) => c.Background = v,
                dp: Microsoft.UI.Xaml.Controls.Border.BackgroundProperty);
}
// </snippet:clear-value-channel>

// <snippet:hot-loop-cells>
static class HotLoopCells
{
    record Quote(bool IsUp);

    static readonly Brush GreenBrush = new SolidColorBrush(Colors.Green);
    static readonly Brush RedBrush = new SolidColorBrush(Colors.Red);

    public static void Build(string label, int r, int c)
    {
        var item = new Quote(IsUp: true);

        // Fluent — five clones per cell. Right tool for ordinary UI.
        var fluentCell = TextBlock(label)
            .FontSize(8)
            .Foreground(item.IsUp ? GreenBrush : RedBrush)
            .Padding(2, 1, 2, 1)
            .Grid(row: r, column: c);

        // Direct record initializer — one TextBlockElement, one ElementModifiers,
        // two bucket sub-records, one Attached dictionary. Use only when the
        // allocation cost shows up in profiles.
        var directCell = new TextBlockElement(label)
        {
            FontSize = 8,
            Modifiers = new ElementModifiers
            {
                Layout = new LayoutModifiers { Padding = new Thickness(2, 1, 2, 1) },
                Visual = new VisualModifiers { Foreground = item.IsUp ? GreenBrush : RedBrush },
            },
            Attached = new Dictionary<Type, object>(1)
            {
                [typeof(GridAttached)] = new GridAttached(r, c, 1, 1),
            },
        };

        _ = (fluentCell, directCell);
    }
}
// </snippet:hot-loop-cells>

// <snippet:memo-cells>
class MemoCellsDemo : Component
{
    record Stock(string Symbol, double Price);

    static Element Cell(Stock item, ColorScheme scheme) =>
        TextBlock($"{item.Symbol} {item.Price:F2}")
            .Foreground(scheme == ColorScheme.Dark ? Theme.PrimaryText : Theme.SecondaryText);

    public override Element Render() => Memo(ctx =>
    {
        var stocks = new[] { new Stock("MSFT", 431.2), new Stock("GOOG", 176.5) };

        var scheme = ctx.UseColorScheme();
        var children = ctx.UseMemoCells(
            stocks,
            (item, i) => Cell(item, scheme),
            scheme);   // ← deps; framework invalidates on change

        return VStack(4, children);
    });
}
// </snippet:memo-cells>

// <snippet:wrong-this-capture>
// Don't — `.Set` runs on every mount AND update, so each render adds another
// subscription, and the lambda captures the parent component instance that was
// current when the closure was created.
class WrongThisCaptureDemo : Component
{
    public override Element Render()
    {
        // Don't do this:
        // return Button("Load").Set(b => b.Loaded += (s, e) => this.OnChildLoaded());
        return Button("Load");
    }

    void OnChildLoaded() { }
}
// </snippet:wrong-this-capture>

// Main app
class AdvancedApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Advanced Patterns"),
                Component<ErrorBoundaryDemo>(),
                Component<ErrorBoundaryRetryDemo>(),
                Component<MemoSubtreeDemo>(),
                Component<SetEscapeHatchDemo>(),
                Component<ElementRefFocusDemo>(),
                Component<CustomHookDemo>(),
                Component<ObservableTreeDemo>(),
                Component<ObservableCollectionDemo>()
            ).Padding(24)
        );
    }
}
