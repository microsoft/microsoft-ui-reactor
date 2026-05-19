using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;
using Windows.System;

ReactorApp.Run<CommandingApp>("Commanding", width: 650, height: 550
#if DEBUG
    , preview: true
#endif
);

// <snippet:basic-command>
class BasicCommandExample : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("Hello, World!");
        var (saved, setSaved) = UseState(false);

        var saveCmd = new Command
        {
            Label = "Save",
            Execute = () => setSaved(true),
            CanExecute = !saved,
            Icon = SymbolIcon("Save"),
            Accelerator = Accelerator(VirtualKey.S, VirtualKeyModifiers.Control)
        };

        return VStack(12,
            TextField(text, v => { setText(v); setSaved(false); })
                .Width(400),
            HStack(8,
                Button(saveCmd),
                When(saved, () => TextBlock("Saved!").Foreground(Theme.SystemSuccess))
            )
        ).Padding(24);
    }
}
// </snippet:basic-command>

// <snippet:standard-commands>
class StandardCommandsExample : Component
{
    public override Element Render()
    {
        var (log, updateLog) = UseReducer(new List<string>());

        var cut = StandardCommand.Cut(() => updateLog(l => [.. l, "Cut"]));
        var copy = StandardCommand.Copy(() => updateLog(l => [.. l, "Copy"]));
        var paste = StandardCommand.Paste(() => updateLog(l => [.. l, "Paste"]));
        var undo = StandardCommand.Undo(
            () => updateLog(l => [.. l, "Undo"]),
            canExecute: log.Count > 0);

        return VStack(12,
            CommandBar(
                primaryCommands: new[] { AppBarButton(cut), AppBarButton(copy),
                    AppBarButton(paste), AppBarButton(undo) }
            ),
            TextBlock($"Actions: {string.Join(", ", log)}").Padding(12)
        ).Padding(24);
    }
}
// </snippet:standard-commands>

// <snippet:async-command>
class AsyncCommandExample : Component
{
    public override Element Render()
    {
        var (status, setStatus) = UseState("Ready");

        var saveCmd = UseCommand(new Command
        {
            Label = "Save to Cloud",
            ExecuteAsync = async () =>
            {
                setStatus("Saving...");
                await Task.Delay(2000);
                setStatus("Saved at " + DateTime.Now.ToString("HH:mm:ss"));
            },
            Icon = SymbolIcon("Save")
        });

        return VStack(12,
            HStack(8,
                Button(saveCmd),
                TextBlock(status).Foreground(Theme.SecondaryText)
            ),
            When(saveCmd.IsExecuting, () =>
                ProgressRing().Width(20).Height(20))
        ).Padding(24);
    }
}
// </snippet:async-command>

// <snippet:command-bar>
class CommandBarExample : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("Edit me");

        var save = StandardCommand.Save(() => { });
        var copy = StandardCommand.Copy(() => { });
        var delete = StandardCommand.Delete(
            () => setText(""), canExecute: text.Length > 0);

        return VStack(0,
            CommandBar(
                primaryCommands: new[] {
                    AppBarButton(save), AppBarButton(copy) },
                secondaryCommands: new[] {
                    AppBarButton(delete) }
            ),
            TextField(text, setText).Margin(16)
        );
    }
}
// </snippet:command-bar>

// <snippet:menu-bar>
class MenuBarExample : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("Document text");

        var save = StandardCommand.Save(() => { });
        var close = StandardCommand.Close(() => setText(""));
        var undo = StandardCommand.Undo(() => { });
        var redo = StandardCommand.Redo(() => { });

        return VStack(0,
            MenuBar(
                Menu("File", MenuItem(save), MenuItem(close)),
                Menu("Edit", MenuItem(undo), MenuItem(redo))
            ),
            TextBlock(text).Padding(16)
        );
    }
}
// </snippet:menu-bar>

// <snippet:button-and-menu>
class ButtonAndMenuExample : Component
{
    public override Element Render()
    {
        var (saves, setSaves) = UseState(0);

        // One Command. Two surfaces. Identical enabled-state, label, icon, accelerator.
        var save = new Command
        {
            Label = "Save",
            Icon = SymbolIcon("Save"),
            Accelerator = Accelerator(VirtualKey.S, VirtualKeyModifiers.Control),
            Execute = () => setSaves(saves + 1),
            CanExecute = saves < 3,
        };

        return VStack(12,
            // Button surface.
            Button(save),
            // MenuFlyout surface — same Command record.
            MenuFlyout(
                Button("File…"),
                MenuItem(save)),
            TextBlock($"Saved {saves} time(s); CanExecute={save.CanExecute}")
                .Foreground(Theme.SecondaryText)
        ).Padding(24);
    }
}
// </snippet:button-and-menu>

// <snippet:parameterized-command>
record TodoItem(int Id, string Title);

class ParameterizedCommandExample : Component
{
    public override Element Render()
    {
        var (items, setItems) = UseState<IReadOnlyList<TodoItem>>(
            new[] { new TodoItem(1, "Buy milk"), new TodoItem(2, "Walk dog"), new TodoItem(3, "Ship doc") });

        // One Command<TodoItem> drives every row.
        var delete = new Command<TodoItem>
        {
            Label = "Delete",
            Icon = SymbolIcon("Delete"),
            Execute = item => setItems(items.Where(i => i.Id != item.Id).ToList()),
        };

        return VStack(8,
            ForEach(items, item =>
                HStack(8,
                    TextBlock(item.Title).Width(180),
                    // Inline button — Command<T> doesn't have a Button(cmd, arg) overload
                    // by design, so call .Execute(arg) directly from the click handler.
                    Button(delete.Label, () => delete.Execute?.Invoke(item))
                        .Disabled(!delete.IsEnabled)))
        ).Padding(24);
    }
}
// </snippet:parameterized-command>

// <snippet:async-with-progress>
class AsyncWithProgressExample : Component
{
    public override Element Render()
    {
        var (progress, setProgress) = UseState(0.0);

        var upload = UseCommand(new Command
        {
            Label = "Upload",
            Icon = SymbolIcon("Upload"),
            ExecuteAsync = async () =>
            {
                for (var i = 0; i <= 100; i += 10)
                {
                    setProgress(i / 100.0);
                    await Task.Delay(120);
                }
            },
        });

        return VStack(12,
            HStack(8,
                Button(upload),
                When(upload.IsExecuting, () =>
                    TextBlock($"{(int)(progress * 100)}%")
                        .Foreground(Theme.SecondaryText))
            ),
            When(upload.IsExecuting, () =>
                Progress(progress * 100).Width(300))
        ).Padding(24);
    }
}
// </snippet:async-with-progress>

// <snippet:dont-create-in-render>
// Don't: re-create the Command on every render — every surface that
// holds the previous reference sees a fresh identity each frame, which
// thrashes the WinUI keyboard-accelerator wiring and re-renders every
// consumer. Lift to a memo or hoist out of Render().
class DontCreateInRender : Component
{
    public override Element Render()
    {
        // BAD — Command identity churns every render:
        // var save = new Command { Label = "Save", Execute = () => { } };

        // GOOD — UseMemo pins identity until deps change:
        var (count, setCount) = UseState(0);
        var save = UseMemo(() => new Command
        {
            Label = "Save",
            Execute = () => setCount(count + 1),
        }, count);

        return VStack(8, Button(save), TextBlock($"Saved {count}")).Padding(24);
    }
}
// </snippet:dont-create-in-render>

// Main app
class CommandingApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Commanding"),
                Component<BasicCommandExample>(),
                Component<StandardCommandsExample>(),
                Component<AsyncCommandExample>(),
                Component<CommandBarExample>(),
                Component<MenuBarExample>(),
                Component<ButtonAndMenuExample>(),
                Component<ParameterizedCommandExample>(),
                Component<AsyncWithProgressExample>(),
                Component<DontCreateInRender>()
            ).Padding(24)
        );
    }
}
