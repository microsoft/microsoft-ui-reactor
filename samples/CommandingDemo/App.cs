// CommandingDemo — Showcases Reactor's commanding system.
// Shows: StandardCommand, Command, UseCommand, CommandHost, parameterized
// commands via MenuItem<T>, per-site overrides with `with`, context-based
// command sharing.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;
using Windows.System;

ReactorApp.Run<CommandingDemoApp>("Commanding Demo", width: 960, height: 720
#if DEBUG
    , devtools: true
#endif
);

// ─── Root component ───────────────────────────────────────────────────────────

class CommandingDemoApp : Component
{
    public override Element Render() =>
        Grid(
            columns: [GridSize.Star()], rows: [GridSize.Auto, GridSize.Star()],
            (TitleBar("Commanding Demo") with
            {
                Subtitle = "StandardCommand · UseCommand · CommandHost",
            }).Grid(row: 0),
            TabView(
                Tab("Standard Commands",  Component<StandardCommandsDemo>())  with { IsClosable = false },
                Tab("Async / UseCommand", Component<AsyncCommandDemo>())      with { IsClosable = false },
                Tab("Parameterized",      Component<ParameterizedCommandDemo>()) with { IsClosable = false },
                Tab("CommandHost",        Component<CommandHostDemo>())       with { IsClosable = false },
                Tab("Per-Site Override",  Component<PerSiteOverrideDemo>())   with { IsClosable = false },
                Tab("Context Sharing",    Component<ContextSharingDemo>())    with { IsClosable = false }
            ).Grid(row: 1))
        // Spec 033 §6 — Mica window backdrop.
        .Backdrop(BackdropKind.Mica);
}

// ─── 1. Standard Commands Demo ────────────────────────────────────────────────

class StandardCommandsDemo : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("Select some text and use Cut/Copy/Paste from the toolbar or menu.");
        var (clipboard, setClipboard) = UseState("");
        var (selectedText, setSelectedText) = UseState("");

        // Keep caret/length in refs — OnSelectionChanged fires on every click
        // inside the TextBox, and if these drove state the re-render would
        // re-push SelectionStart/Length back through the reconciler, which in
        // turn refires SelectionChanged. Refs break that feedback loop while
        // still giving Cut/Paste access to the live caret position.
        var selStart = UseRef(0);
        var selLength = UseRef(0);

        // Tracks a one-shot selection we want to apply after a programmatic
        // text mutation (Cut/Paste/SelectAll). Cleared back to null as soon
        // as the TextBox consumes it, so normal user clicks don't trigger
        // a programmatic selection write.
        var (pendingSelection, setPendingSelection) = UseState<(int Start, int Length)?>(null);

        var hasSelection = selectedText.Length > 0;

        // Define commands once — use in toolbar, menu, and context menu.
        var cut = StandardCommand.Cut(() =>
        {
            var start = selStart.Current;
            var len = selectedText.Length;
            if (len > 0)
            {
                setClipboard(selectedText);
                setText(text.Remove(start, len));
                setSelectedText("");
                setPendingSelection((start, 0));
            }
        }, canExecute: hasSelection);

        var copy = StandardCommand.Copy(() => setClipboard(selectedText), canExecute: hasSelection);

        var paste = StandardCommand.Paste(() =>
        {
            var start = selStart.Current;
            var len = selLength.Current;
            setText(text.Remove(start, len).Insert(start, clipboard));
            setSelectedText("");
            setPendingSelection((start + clipboard.Length, 0));
        }, canExecute: clipboard.Length > 0);

        var selectAll = StandardCommand.SelectAll(() =>
        {
            setSelectedText(text);
            setPendingSelection((0, text.Length));
        });

        return VStack(8,
            SubHeading("Standard Commands — define once, use everywhere").Margin(horizontal: 16, vertical: 8),

            CommandBar(primaryCommands: [
                AppBarButton(cut),
                AppBarButton(copy),
                AppBarButton(paste),
                AppBarSeparator(),
                AppBarButton(selectAll),
            ]),

            MenuBar(
                Menu("Edit",
                    MenuItem(cut),
                    MenuItem(copy),
                    MenuItem(paste),
                    MenuSeparator(),
                    MenuItem(selectAll))),

            (TextBox(text, setText, placeholderText: "Type here...") with
            {
                OnSelectionChanged = (sel, start, len) =>
                {
                    selStart.Current = start;
                    selLength.Current = len;
                    // Only update state when the presence-or-absence of a
                    // selection changes — caret moves without a selection
                    // shouldn't trigger a re-render (avoids the flash on
                    // simple clicks inside the text box).
                    var nowHas = sel.Length > 0;
                    if (nowHas || selectedText.Length > 0) setSelectedText(sel);
                },
                SelectionStart = pendingSelection?.Start,
                SelectionLength = pendingSelection?.Length,
            }).Margin(horizontal: 16, vertical: 8),

            // Clear the pending selection once the TextBox has had a chance
            // to apply it. Runs after render; no-op when nothing pending.
            PendingSelectionConsumer(pendingSelection, setPendingSelection),

            Caption($"Clipboard: \"{clipboard}\"").Foreground(SecondaryText).Margin(horizontal: 16, vertical: 4),
            Caption($"Selected: \"{selectedText}\"").Foreground(SecondaryText).Margin(horizontal: 16, vertical: 4),
            Caption($"Has selection: {hasSelection}").Foreground(SecondaryText).Margin(horizontal: 16, vertical: 4));
    }

    static Element PendingSelectionConsumer(
        (int Start, int Length)? pending,
        Action<(int Start, int Length)?> setPending) =>
        // Spec 033 §4 — migrated from `Func(...)` to `RenderEachTime(...)` to
        // make the always-re-render intent explicit. The body has hooks and
        // depends on the parent's `pending` argument, so we want to re-evaluate
        // on every parent render.
        RenderEachTime(ctx =>
        {
            ctx.UseEffect(() =>
            {
                if (pending.HasValue) setPending(null);
            }, (object?)pending ?? (object)"null");
            return Empty();
        });
}

// ─── 2. Async Command Demo (UseCommand) ──────────────────────────────────────

class AsyncCommandDemo : Component
{
    public override Element Render()
    {
        var (saveCount, setSaveCount) = UseState(0);
        var (lastStatus, setLastStatus) = UseState("Ready");

        // UseCommand wraps the async command so IsExecuting flips automatically
        // and click re-entrance is guarded.
        var saveCmd = UseCommand(StandardCommand.Save(async () =>
        {
            setLastStatus("Saving...");
            await Task.Delay(2000);
            setSaveCount(saveCount + 1);
            setLastStatus($"Saved! (total: {saveCount + 1})");
        }));

        return VStack(8,
            SubHeading("Async Commands — UseCommand auto-tracks IsExecuting").Margin(horizontal: 16, vertical: 8),

            HStack(8,
                Button(saveCmd).Margin(horizontal: 16, vertical: 0),
                saveCmd.IsExecuting ? ProgressRing().Size(20, 20) : Empty()),

            TextBlock($"Status: {lastStatus}").Margin(horizontal: 16, vertical: 4),
            Caption($"IsExecuting: {saveCmd.IsExecuting}").Foreground(SecondaryText).Margin(horizontal: 16, vertical: 4),
            Caption($"IsEnabled: {saveCmd.IsEnabled}").Foreground(SecondaryText).Margin(horizontal: 16, vertical: 4),
            Caption("Try clicking Save — the button auto-disables during the 2-second operation.")
                .Foreground(SecondaryText).Margin(horizontal: 16, vertical: 8));
    }
}

// ─── 3. Parameterized Command Demo ───────────────────────────────────────────

class ParameterizedCommandDemo : Component
{
    record Item(string Name, string Id);

    public override Element Render()
    {
        var (items, updateItems) = UseReducer(new[]
        {
            new Item("Apple", "1"),
            new Item("Banana", "2"),
            new Item("Cherry", "3"),
            new Item("Date", "4"),
            new Item("Elderberry", "5"),
        });

        // Single command definition — parameter bound per-item via MenuItem<T>.
        var deleteCmd = new Command<Item>
        {
            Label = "Delete",
            Icon = new SymbolIconData("Delete"),
            Execute = item => updateItems(list => list.Where(i => i.Id != item.Id).ToArray()),
        };

        return VStack(8,
            SubHeading("Parameterized Commands — Command<T> with per-item binding").Margin(horizontal: 16, vertical: 8),
            Caption("Right-click each row to open the context menu — MenuItem<T> binds the item as the command parameter.")
                .Foreground(SecondaryText).Margin(16, 0, 16, 8),

            ForEach(items, item =>
                HStack(8,
                    TextBlock(item.Name).Width(120),
                    Button("Delete", () => deleteCmd.Execute?.Invoke(item))
                        .AutomationName($"Delete {item.Name}")
                ).Margin(horizontal: 16, vertical: 2)
                 .WithContextFlyout(MenuItems(MenuItem(deleteCmd, item)))
                 .WithKey(item.Id)),

            Caption($"Items remaining: {items.Length}").Foreground(SecondaryText).Margin(horizontal: 16, vertical: 8));
    }
}

// ─── 4. CommandHost Demo ─────────────────────────────────────────────────────

class CommandHostDemo : Component
{
    public override Element Render()
    {
        var (log, setLog) = UseState("Press Ctrl+S, Ctrl+Z, or Ctrl+Y within the highlighted region.");

        var save = StandardCommand.Save(() => setLog($"[{DateTime.Now:HH:mm:ss}] Save triggered via Ctrl+S"));
        var undo = StandardCommand.Undo(() => setLog($"[{DateTime.Now:HH:mm:ss}] Undo triggered via Ctrl+Z"));
        var redo = StandardCommand.Redo(() => setLog($"[{DateTime.Now:HH:mm:ss}] Redo triggered via Ctrl+Y"));

        return VStack(8,
            SubHeading("CommandHost — keyboard accelerators scoped to a subtree").Margin(horizontal: 16, vertical: 8),

            CommandHost([save, undo, redo],
                VStack(8,
                    TextBlock("INSIDE CommandHost scope — Ctrl+S / Ctrl+Z / Ctrl+Y fire here:")
                        .Set(tb => tb.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                    TextBox("", _ => { }, placeholderText: "Click here and press Ctrl+S..."),
                    Caption(log).Foreground(SecondaryText))
                .Padding(16)
                .Background(SystemAttentionBackground)
                .CornerRadius(8)
            ).Margin(horizontal: 16, vertical: 0),

            VStack(8,
                TextBlock("OUTSIDE CommandHost scope — accelerators do NOT fire here:")
                    .Set(tb => tb.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                TextBox("", _ => { }, placeholderText: "Click here and press Ctrl+S — nothing should happen...")
            ).Padding(16).Margin(horizontal: 16, vertical: 0).Background(SystemCriticalBackground).CornerRadius(8));
    }
}

// ─── 5. Per-Site Override Demo ───────────────────────────────────────────────

class PerSiteOverrideDemo : Component
{
    public override Element Render()
    {
        var (lastAction, setLastAction) = UseState("(none)");

        var deleteCmd = StandardCommand.Delete(() => setLastAction("Deleted!"));

        return VStack(8,
            SubHeading("Per-Site Overrides — same command, different presentation with `with`").Margin(horizontal: 16, vertical: 8),

            HStack(16,
                VStack(4,
                    Caption("Toolbar:").Foreground(SecondaryText),
                    CommandBar(primaryCommands: [AppBarButton(deleteCmd)])),

                VStack(4,
                    Caption("Context menu (overridden labels):").Foreground(SecondaryText),
                    MenuBar(
                        Menu("Actions",
                            MenuItem(deleteCmd with { Label = "Remove selected item" }),
                            MenuItem(deleteCmd with { Label = "Erase permanently", Icon = new SymbolIconData("Clear") }))))
            ).Margin(horizontal: 16, vertical: 0),

            TextBlock($"Last action: {lastAction}").Margin(horizontal: 16, vertical: 8));
    }
}

// ─── 6. Context-Based Command Sharing Demo ──────────────────────────────────

record EditorCommands(Command Save, Command Undo, Command Redo);

static class EditorCommandsContext
{
    public static readonly Context<EditorCommands?> Instance = new(null);
}

class ToolbarComponent : Component
{
    public override Element Render()
    {
        var commands = UseContext(EditorCommandsContext.Instance);
        if (commands is null)
            return Caption("No editor commands available").Foreground(SecondaryText);

        return VStack(4,
            Caption("Toolbar (consumes commands via context):").Foreground(SecondaryText),
            CommandBar(primaryCommands: [
                AppBarButton(commands.Save),
                AppBarButton(commands.Undo),
                AppBarButton(commands.Redo),
            ]));
    }
}

class ContextSharingDemo : Component
{
    public override Element Render()
    {
        // Command state lives here so context wraps both toolbar and editor.
        var (content, setContent) = UseState("Edit this text...");
        var (history, updateHistory) = UseReducer(new[] { "Edit this text..." });
        var (saveStatus, setSaveStatus) = UseState("");

        var save = UseCommand(StandardCommand.Save(async () =>
        {
            setSaveStatus("Saving...");
            await Task.Delay(500);
            setSaveStatus($"Saved at {DateTime.Now:HH:mm:ss}");
        }));
        var undo = StandardCommand.Undo(() =>
        {
            if (history.Length > 1)
            {
                var prev = history[^2];
                setContent(prev);
                updateHistory(h => h[..^1]);
            }
        }, canExecute: history.Length > 1);
        var redo = StandardCommand.Redo(() => { }, canExecute: false);

        var commands = new EditorCommands(save, undo, redo);

        return VStack(8,
            SubHeading("Context Sharing — parent provides, children consume").Margin(horizontal: 16, vertical: 8),

            VStack(8,
                Component<ToolbarComponent>(),
                VStack(4,
                    Caption("Editor:").Foreground(SecondaryText),
                    TextBox(content, newText =>
                    {
                        setContent(newText);
                        updateHistory(h => [.. h, newText]);
                    })),
                saveStatus.Length > 0
                    ? Caption(saveStatus).Foreground(SecondaryText).Margin(horizontal: 0, vertical: 4)
                    : Empty()
            ).Margin(horizontal: 16, vertical: 0).Provide(EditorCommandsContext.Instance, commands),

            Caption("The parent component owns the commands and provides them via Context.")
                .Foreground(SecondaryText).Margin(horizontal: 16, vertical: 8),
            Caption("The toolbar consumes them via UseContext — no prop drilling.")
                .Foreground(SecondaryText).Margin(horizontal: 16, vertical: 4));
    }
}
