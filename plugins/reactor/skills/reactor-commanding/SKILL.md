---
name: reactor-commanding
description: "Reactor commanding — actions that appear in multiple surfaces (menu + toolbar), need keyboard shortcuts, or need `CanExecute`. `Command`, `StandardCommand`, `UseCommand`, `CommandHost`. Use when wiring shared actions or shortcuts."
---

# Commanding in Reactor

Use `Command` when an action shows up in multiple surfaces (toolbar + menu +
context menu), needs a keyboard shortcut, or needs `CanExecute` disabling.
Use a bare `Action` for one-off button clicks with no reuse.

## Command record

```csharp
var save = new Command
{
    Label = "Save",                                  // required
    Execute = () => Save(),                          // sync
    // OR:
    ExecuteAsync = async () => await SaveAsync(),    // async (wrap with UseCommand)
    CanExecute = hasChanges,                         // default true
    Icon = SymbolIcon("Save"),
    Description = "Save the document",               // tooltip + a11y
    Accelerator = Accelerator(VirtualKey.S, VirtualKeyModifiers.Control),
    AccessKey = "S",                                 // Alt+key
};
// Computed: IsEnabled = CanExecute && !IsExecuting
```

`Command<T>` is identical but `Execute`/`ExecuteAsync` receive a typed
parameter — bind the parameter at the call site with `MenuItem(cmd, item)`.

## StandardCommand factory

Pre-built commands with correct labels, icons, and accelerators:

```csharp
var cut    = StandardCommand.Cut(() => CutSelection());
var copy   = StandardCommand.Copy(() => CopySelection());
var paste  = StandardCommand.Paste(() => PasteFromClipboard());
var undo   = StandardCommand.Undo(() => Undo());
var redo   = StandardCommand.Redo(() => Redo());
var delete = StandardCommand.Delete(() => DeleteSelected());
var save   = StandardCommand.Save(async () => await SaveAsync()); // async overload
var open   = StandardCommand.Open(() => OpenFile());

// CanExecute parameter:
var cut2 = StandardCommand.Cut(() => CutSelection(), canExecute: hasSelection);
```

Also available: `SelectAll`, `Close`, `Share`, `Play`, `Pause`, `Stop`,
`Forward`, `Backward`.

## Command-aware DSL overloads

Define once, bind anywhere:

```csharp
var save = StandardCommand.Save(() => SaveFile());

Button(save)              // label → content, execute → click, isEnabled → isEnabled
AppBarButton(save)        // + icon, accelerator, accessKey, description
MenuItem(save)            // + icon, accelerator, accessKey, description
MenuItem(deleteCmd, item) // parameterized: binds item as argument
```

Per-site overrides with `with`:

```csharp
var delete = StandardCommand.Delete(() => DeleteSelected());
MenuItem(delete)                                          // "Delete"
MenuItem(delete with { Label = "Remove permanently" })
AppBarButton(delete with { Icon = SymbolIcon("Clear") })
```

## UseCommand — async lifecycle

**Only needed for commands with `ExecuteAsync`.** Sync commands pass through
unchanged.

```csharp
class Editor : Component
{
    public override Element Render()
    {
        var saveCmd = UseCommand(StandardCommand.Save(async () =>
        {
            await SaveAsync();
        }));

        // saveCmd.Execute is now a sync wrapper around the async
        // saveCmd.IsExecuting is true while the async is in-flight
        // saveCmd.IsEnabled auto-flips to false while executing
        return HStack(
            Button(saveCmd),
            saveCmd.IsExecuting ? ProgressRing() : Empty());
    }
}
```

- Consumes 2 hook slots; re-entrance guard ignores clicks while executing.
- `IsExecuting` resets to false even if `ExecuteAsync` throws.
- Call unconditionally — don't wrap in `if`.

## CommandHost — keyboard-scoped accelerators

Limits `Accelerator` registration to a subtree:

```csharp
var save = StandardCommand.Save(() => SaveFile());
var undo = StandardCommand.Undo(() => UndoAction());

CommandHost([save, undo],
    VStack(
        TextBlock("Ctrl+S / Ctrl+Z only fire inside this region"),
        TextBox(value, onChange)))
```

Commands without an `Accelerator` are ignored by `CommandHost`.

## Sharing commands via Context

Editor-provides / toolbar-consumes:

```csharp
record EditorCommands(Command Save, Command Undo, Command Redo);
static readonly Context<EditorCommands?> EditorCtx = new(null);

class Editor : Component
{
    public override Element Render()
    {
        var save = UseCommand(StandardCommand.Save(async () => await SaveAsync()));
        var undo = StandardCommand.Undo(() => Undo());
        return TextBox(text, onChange)
            .Provide(EditorCtx, new EditorCommands(save, undo, redo));
    }
}

class Toolbar : Component
{
    public override Element Render()
    {
        var cmds = UseContext(EditorCtx);
        if (cmds is null) return Empty();
        return CommandBar(primaryCommands: [
            AppBarButton(cmds.Save),
            AppBarButton(cmds.Undo),
        ]);
    }
}
```

## ICommand interop

Bridge existing MVVM/CommunityToolkit `ICommand`:

```csharp
var cmd = CommandInterop.FromCommand(
    viewModel.SaveCommand,
    "Save",
    icon: SymbolIcon("Save"),
    accelerator: Accelerator(VirtualKey.S, VirtualKeyModifiers.Control));
```

## Anti-patterns

- Don't create commands inside loops. Define once, bind per item with
  `MenuItem(cmd, item)`.
- Don't `UseCommand` for sync-only commands — it wastes hook slots.
- Don't call `UseCommand` conditionally — hooks must run in the same order
  every render.
- Don't mix `Execute` and `ExecuteAsync` on the same command — pick one.
