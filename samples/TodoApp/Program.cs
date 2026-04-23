// TodoApp — A starter template that showcases Reactor's component model.
// Highlights: UseReducer, Command, command-aware Button, FlexColumn/FlexRow
// layout with grow/shrink, theme tokens, TitleBar integration, and
// a11y-friendly controls.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

ReactorApp.Run<TodoApp>("Todos", width: 900, height: 720
#if DEBUG
    , devtools: true
#endif
);

// ─── State ───────────────────────────────────────────────────────────────────

record TodoItem(string Id, string Text, bool IsCompleted);

enum TodoFilter { All, Active, Completed }

record TodoState(IReadOnlyList<TodoItem> Items, string NewItemText, TodoFilter Filter)
{
    public static readonly TodoState Initial = new([], "", TodoFilter.All);
}

abstract record TodoAction;
record AddItem : TodoAction;
record ToggleItem(string Id) : TodoAction;
record DeleteItem(string Id) : TodoAction;
record SetNewItemText(string Text) : TodoAction;
record SetFilter(TodoFilter Filter) : TodoAction;
record ClearCompleted : TodoAction;

// ─── Reducer ─────────────────────────────────────────────────────────────────

static class TodoReducer
{
    public static TodoState Reduce(TodoState state, TodoAction action) => action switch
    {
        AddItem when !string.IsNullOrWhiteSpace(state.NewItemText) =>
            state with
            {
                Items = [.. state.Items, new(Guid.NewGuid().ToString(), state.NewItemText.Trim(), false)],
                NewItemText = ""
            },
        ToggleItem t => state with
        {
            Items = [.. state.Items.Select(i =>
                i.Id == t.Id ? i with { IsCompleted = !i.IsCompleted } : i)]
        },
        DeleteItem d => state with
        {
            Items = [.. state.Items.Where(i => i.Id != d.Id)]
        },
        SetNewItemText s => state with { NewItemText = s.Text },
        SetFilter f => state with { Filter = f.Filter },
        ClearCompleted => state with { Items = [.. state.Items.Where(i => !i.IsCompleted)] },
        _ => state
    };
}

// ─── Root component ──────────────────────────────────────────────────────────

class TodoApp : Component
{
    public override Element Render()
    {
        var (state, dispatch) = UseReducer<TodoState, TodoAction>(TodoReducer.Reduce, TodoState.Initial);

        var filtered = UseMemo(() => state.Filter switch
        {
            TodoFilter.Active    => state.Items.Where(i => !i.IsCompleted).ToArray(),
            TodoFilter.Completed => state.Items.Where(i => i.IsCompleted).ToArray(),
            _                    => state.Items.ToArray()
        }, state.Items, state.Filter);

        var remaining = state.Items.Count(i => !i.IsCompleted);
        var completed = state.Items.Count - remaining;

        var addCmd = new Command
        {
            Label = "Add",
            Execute = () => dispatch(new AddItem()),
            CanExecute = !string.IsNullOrWhiteSpace(state.NewItemText),
            Accelerator = Accelerator(Windows.System.VirtualKey.Enter),
        };

        var subtitle = state.Items.Count switch
        {
            0 => "No tasks",
            _ => $"{remaining} remaining of {state.Items.Count}",
        };

        return CommandHost([addCmd],
            FlexColumn(
                // Real Windows title bar — integrates with caption buttons and
                // the system drag region instead of painting a custom header.
                (TitleBar("Todos") with { Subtitle = subtitle }).Flex(shrink: 0),

                // Scrollable body — padded off the window edges so the card
                // has breathing room on every side.
                // ScrollView fills remaining space. Inside it, a 3-column Grid
                // centers the card in the available width — the star/auto/star
                // columns give equal gutters without fighting MaxWidth in the way
                // HAlign(Center) does on narrow windows.
                ScrollView(
                    Grid(
                        columns: ["*", "Auto", "*"],
                        rows: ["Auto"],
                        Card(state, filtered, addCmd, remaining, completed, dispatch)
                            .MinWidth(320)
                            .MaxWidth(560)
                            .Margin(16, 16, 16, 24)
                            .Grid(row: 0, column: 1)
                    )
                ).Flex(grow: 1)
            )
            .Background(SolidBackground));
    }

    // ── Card: input row, list, footer — all inside a rounded surface ──

    static Element Card(
        TodoState state,
        TodoItem[] filtered,
        Command addCmd,
        int remaining,
        int completed,
        Action<TodoAction> dispatch) =>
        Border(
            FlexColumn(
                InputRow(state.NewItemText, addCmd, dispatch),
                Divider(),
                filtered.Length == 0
                    ? EmptyState(state.Filter)
                    : FlexColumn(filtered.Select(item => TodoRow(item, dispatch)).ToArray<Element?>()),
                Divider(),
                FooterRow(remaining, completed, state.Filter, dispatch)
            )
        )
        .Background(CardBackground)
        .WithBorder(CardStroke, 1)
        .CornerRadius(8);

    // ── Input row ────────────────────────────────────────────────────

    static Element InputRow(string text, Command addCmd, Action<TodoAction> dispatch) =>
        (FlexRow(
            TextField(text, v => dispatch(new SetNewItemText(v)),
                      placeholder: "What needs to be done?")
                .Flex(grow: 1),
            Button(addCmd).Flex(shrink: 0)
        ) with { AlignItems = FlexAlign.Center, ColumnGap = 12 })
            .Padding(16, 14);

    // ── Todo row ─────────────────────────────────────────────────────

    static Element TodoRow(TodoItem item, Action<TodoAction> dispatch) =>
        (FlexRow(
            // CheckBox's default style sets MinWidth=120 (CheckBoxMinWidth theme
            // resource) to reserve space for a Content label. Collapse it with
            // a local MinWidth(0) since we render the label separately so that
            // completed items can get strikethrough styling.
            CheckBox(item.IsCompleted, _ => dispatch(new ToggleItem(item.Id)))
                .AutomationName(item.IsCompleted ? "Mark as incomplete" : "Mark as complete")
                .MinWidth(0)
                .Flex(shrink: 0),

            TextBlock(item.Text)
                .Foreground(item.IsCompleted ? TertiaryText : PrimaryText)
                .Set(t =>
                {
                    t.TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis;
                    if (item.IsCompleted)
                        t.TextDecorations = global::Windows.UI.Text.TextDecorations.Strikethrough;
                })
                .ToolTip(item.Text)
                .Flex(grow: 1, alignSelf: FlexAlign.Center),

            // Icon-only delete button — trash-can glyph (E74D) from the
            // Segoe Fluent icon font. Styled as a subtle "ghost" button so it
            // recedes visually but still reveals hover + pressed states
            // through .Resources().
            Button("", () => dispatch(new DeleteItem(item.Id)))
                .AutomationName($"Delete '{item.Text}'")
                .ToolTip("Delete")
                .FontSize(14)
                .Size(36, 32)
                .Set(b => b.FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)
                    Application.Current.Resources["SymbolThemeFontFamily"])
                .Resources(r => r
                    .Set("ButtonBackground", Theme.Ref("SubtleFillColorTransparentBrush"))
                    .Set("ButtonBorderBrush", Theme.Ref("SubtleFillColorTransparentBrush"))
                    .Set("ButtonForeground", SecondaryText)
                    .Set("ButtonForegroundPointerOver", SystemCritical))
                .Flex(shrink: 0)
         ) with { AlignItems = FlexAlign.Center, ColumnGap = 12 })
            .Padding(16, 8)
            .WithKey(item.Id);

    // ── Empty state — shown when filter returns nothing ───────────────

    static Element EmptyState(TodoFilter filter) =>
        (FlexColumn(
                        // Decorative checkbox glyph (E73A) from the Segoe Fluent icon font.
            // Rendered as a TextBlock since FontIcon returns IconData (used on
            // commands / menu items), not a free-standing Element.
            TextBlock("")
                .FontSize(32)
                .Foreground(TertiaryText)
                .Set(tb => tb.FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)
                    Application.Current.Resources["SymbolThemeFontFamily"]),
            TextBlock(filter switch
            {
                TodoFilter.Active    => "Nothing active — you're all caught up!",
                TodoFilter.Completed => "Nothing completed yet.",
                _                    => "No todos yet. Add one above to get started.",
            })
            .Foreground(SecondaryText)
            .HAlign(HorizontalAlignment.Center)
        ) with { AlignItems = FlexAlign.Center, RowGap = 12 })
            .Padding(24, 40);

    // ── Divider ─────────────────────────────────────────────────────

    static Element Divider() =>
        Border(Empty()).Height(1).Background(DividerStroke);

    // ── Footer — counts, filter toggles, clear-completed ──────────────

    static Element FooterRow(int remaining, int completed, TodoFilter active, Action<TodoAction> dispatch) =>
        (FlexRow(
            Caption($"{remaining} item{(remaining == 1 ? "" : "s")} left")
                .Foreground(SecondaryText)
                .Flex(shrink: 0, alignSelf: FlexAlign.Center),

            // Spacer
            Flex().Flex(grow: 1),

            FilterToggle("All", TodoFilter.All, active, dispatch),
            FilterToggle("Active", TodoFilter.Active, active, dispatch),
            FilterToggle("Completed", TodoFilter.Completed, active, dispatch),

            When(completed > 0, () =>
                HyperlinkButton("Clear completed", onClick: () => dispatch(new ClearCompleted()))
                    .Flex(shrink: 0, alignSelf: FlexAlign.Center))
         ) with { AlignItems = FlexAlign.Center, ColumnGap = 6 })
            .Padding(16, 10);

    static Element FilterToggle(string label, TodoFilter filter, TodoFilter active, Action<TodoAction> dispatch) =>
        // shrink: 0 — the flex spacer would otherwise clip "Active" → "Ac" and
        // "Completed" → "Comp" in narrower windows.
        ToggleButton(label,
            isChecked: filter == active,
            onToggled: _ => dispatch(new SetFilter(filter)))
            .Flex(shrink: 0);
}
