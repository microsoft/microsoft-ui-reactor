using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

ReactorApp.Run<CommandingApp>("Commanding", width: 650, height: 550
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
            TextBox(text, v => { setText(v); setSaved(false); }, header: "Document")
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
            TextBox(text, setText, header: "Document").Margin(16)
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
            UseMemo(() => new[] { new TodoItem(1, "Buy milk"), new TodoItem(2, "Walk dog"), new TodoItem(3, "Ship doc") }, []));

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
                        .AutomationName($"Delete {item.Title}")
                        .IsEnabled(delete.IsEnabled)))
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
// Don't: re-create the Command on every render — each render allocates a
// fresh command record (and its captured closures). Memoizing keeps a
// stable instance across renders. Lift to a memo or hoist out of Render().
class DontCreateInRender : Component
{
    public override Element Render()
    {
        // BAD — a fresh Command (and closure) is allocated every render:
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

// <snippet:command-modifier>
class CommandModifierExample : Component
{
    public override Element Render()
    {
        var saveCmd = new Command { Label = "Save", Execute = () => { } };

        return VStack(12,
            // Plain label — the factory is enough:
            Button(saveCmd),

            // Custom content — compose the layout, then bind the command:
            Button(HStack(8, Icon(SymbolIcon("Save")), TextBlock("Save")))
                .Command(saveCmd)
        ).Padding(24);
    }
}
// </snippet:command-modifier>

// <snippet:binding-paths>
class BindingPathsExample : Component
{
    public override Element Render()
    {
        var saveCmd = new Command { Label = "Save", Execute = () => { } };

        return VStack(12,
            // Factory — plain label:
            Button(saveCmd),

            // Modifier — custom content:
            Button(HStack(8, Icon(SymbolIcon("Save")), TextBlock("Save"))).Command(saveCmd),

            // Record-init — the typed property is public (the Label ctor arg is required):
            new ButtonElement(saveCmd.Label) { Command = saveCmd },

            // `with` on an existing element hits the same property:
            Button("Save") with { Command = saveCmd },

            // The typed property also covers the split buttons:
            new SplitButtonElement(saveCmd.Label) { Command = saveCmd }
        ).Padding(24);
    }
}
// </snippet:binding-paths>

// <snippet:debounce-ms>
class DebounceExample : Component
{
    public override Element Render()
    {
        var (runs, setRuns) = UseState(0);

        // Sync action + framework-managed debounce — no fake async, no Task.Delay.
        var runCmd = UseCommand(new Command
        {
            Label = "Run",
            Execute = () => setRuns(runs + 1),
            DebounceMs = 1500,
        });

        // Async action — IsExecuting still tracks the lambda; DebounceMs keeps the
        // button disabled past the lambda's return (the disabled window is the
        // longer of the two).
        var regenCmd = UseCommand(new Command
        {
            Label = "Re-gen",
            ExecuteAsync = () => { setRuns(runs + 1); return Task.CompletedTask; },
            DebounceMs = 250,
        });

        return VStack(12,
            HStack(8, Button(runCmd), Button(regenCmd)),
            TextBlock($"Fired {runs} time(s)").Foreground(Theme.SecondaryText)
        ).Padding(24);
    }
}
// </snippet:debounce-ms>

// <snippet:menuitem-parameterized>
class MenuItemParameterizedExample : Component
{
    public override Element Render()
    {
        var (log, setLog) = UseState("");
        var item = new TodoItem(1, "Buy milk");

        var deleteCommand = new Command<TodoItem>
        {
            Label = "Delete",
            Execute = i => setLog($"Deleted {i.Title}"),
        };
        var renameCommand = new Command<TodoItem>
        {
            Label = "Rename",
            Execute = i => setLog($"Renamed {i.Title}"),
        };

        // The row content is the flyout target; each MenuItem carries the row's data.
        return VStack(8,
            MenuFlyout(TextBlock(item.Title).Padding(8),
                MenuItem(deleteCommand, item),
                MenuItem(renameCommand, item)),
            TextBlock(log).Foreground(Theme.SecondaryText)
        ).Padding(24);
    }
}
// </snippet:menuitem-parameterized>

// <snippet:accelerator-scope>
class AcceleratorScopeExample : Component
{
    public override Element Render()
    {
        var (saves, setSaves) = UseState(0);

        var save = new Command
        {
            Label = "Save",
            Accelerator = Accelerator(VirtualKey.S, VirtualKeyModifiers.Control),
            Execute = () => setSaves(saves + 1),
        };

        // Window-scoped via MenuBar at the root.
        return VStack(0,
            MenuBar(Menu("File", MenuItem(save))),
            TextBlock($"Saved {saves} time(s)").Padding(16));
    }
}
// </snippet:accelerator-scope>

// <snippet:three-surfaces>
class ThreeSurfacesExample : Component
{
    public override Element Render()
    {
        var (saves, setSaves) = UseState(0);

        var save = new Command
        {
            Label = "Save",
            Icon = SymbolIcon("Save"),
            Accelerator = Accelerator(VirtualKey.S, VirtualKeyModifiers.Control),
            ExecuteAsync = () => { setSaves(saves + 1); return Task.CompletedTask; },
        };
        var saveWrapped = UseCommand(save);

        return VStack(0,
            MenuBar(Menu("File", MenuItem(saveWrapped))),
            CommandBar(primaryCommands: new[] { AppBarButton(saveWrapped) }),
            TextBlock($"Saved {saves} time(s)").Padding(16));
    }
}
// </snippet:three-surfaces>

// <snippet:canexecute-dont>
class CanExecuteDontExample : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("");

        // Don't — the guard hides inside Execute, so every surface still
        // looks enabled and the user clicks expecting an action.
        var save = new Command
        {
            Label = "Save",
            Execute = () => { if (text.Length > 0) Save(); },
        };

        return VStack(12, TextBox(text, setText, header: "Document").Width(300), Button(save))
            .Padding(24);

        void Save() { }
    }
}
// </snippet:canexecute-dont>

// <snippet:canexecute-do>
class CanExecuteDoExample : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("");

        // Do — promote the predicate to CanExecute so every surface
        // disables together.
        var save = new Command
        {
            Label = "Save",
            Execute = Save,
            CanExecute = text.Length > 0,
        };

        return VStack(12, TextBox(text, setText, header: "Document").Width(300), Button(save))
            .Padding(24);

        void Save() { }
    }
}
// </snippet:canexecute-do>

// <snippet:async-confirm-dialog>
class AsyncConfirmDialogExample : Component
{
    public override Element Render() => Memo(ctx =>
    {
        var (open, setOpen) = ctx.UseState(false);
        var (status, setStatus) = ctx.UseState("Ready");

        var delete = ctx.UseCommand(new Command
        {
            Label = "Delete",
            ExecuteAsync = async () =>
            {
                await Task.Delay(500);   // stands in for api.DeleteAsync(id)
                setStatus("Deleted");
                setOpen(false);
            },
        });

        return VStack(12,
            Button("Delete…", () => setOpen(true)),
            TextBlock(status).Foreground(Theme.SecondaryText),
            ContentDialog("Delete?", TextBlock("This cannot be undone."),
                    primaryButtonText: "Delete") with
            {
                IsOpen = open,
                IsPrimaryButtonEnabled = delete.IsEnabled,
                OnClosed = r =>
                {
                    if (r == ContentDialogResult.Primary) delete.Execute?.Invoke();
                    else setOpen(false);
                },
            }
        ).Padding(24);
    });
}
// </snippet:async-confirm-dialog>

// <snippet:localized-command>
class LocalizedCommandExample : Component
{
    public override Element Render()
    {
        var intl = UseIntl();

        var save = StandardCommand.Save(() => { }) with
        {
            Label = intl.Message(new MessageKey("Commands", "save.button")),
            Description = intl.Message(new MessageKey("Commands", "save.tooltip")),
        };

        return VStack(12, Button(save)).Padding(24);
    }
}
// </snippet:localized-command>

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
