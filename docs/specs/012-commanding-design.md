# Reactor Commanding System — Detailed Design

## Status

**Draft** — 2026-04-08.

---

## Problem Statement

The [critical review](../critical-review.md) §11 and scorecard grades Commands at **F** —
the joint-lowest score in the entire framework alongside Navigation:

> **No command abstraction.** WinUI's `ICommand` bundles execute + canExecute +
> change notification. Microsoft.UI.Reactor (Reactor) has bare `Action` callbacks. This means:
> - No automatic button disabling when a command can't execute
> - No reusable command objects (label + icon + accelerator + action)
> - No `StandardUICommand` equivalents (Cut/Copy/Paste)
> - Every button's enabled state must be separately managed

Reactor currently has clean DSL surface for CommandBar, AppBarButton, MenuBar,
MenuFlyoutItem, and CommandBarFlyout — but every action is a bare `Action?`
callback with no CanExecute, no metadata bundling, no keyboard shortcut
integration, and no way to define a command once and surface it in N places.

---

## Research: Why Modern Frameworks Don't Have ICommand

### The mechanism is dead, but the problem is alive

React, SwiftUI, and Jetpack Compose all lack an ICommand equivalent. The standard
explanation is that declarative UI makes `CanExecuteChanged` unnecessary — you
just write `disabled={!canCut}` and the framework handles re-render. This is
correct: the **event-based enablement mechanism** of ICommand is genuinely
obsolete in a reactive world.

But all three frameworks also lack the **bundling** and **routing** capabilities
that ICommand (specifically WPF's `RoutedUICommand`) provided:

| Capability | WPF RoutedUICommand | React | SwiftUI | Compose | Reactor (today) |
|---|---|---|---|---|---|
| Execute callback | ✓ | ✓ | ✓ | ✓ | ✓ |
| Reactive enabled state | Clunky (events) | ✓ (state) | ✓ (@State) | ✓ (state) | ✗ |
| Bundle label+icon+shortcut+action | ✓ | ✗ | ✗ | ✗ | ✗ |
| Command routing to focused view | ✓ | ✗ | ✓ (FocusedValue) | ✗ | ✗ |
| Standard commands (Cut/Copy/Paste) | ✓ | ✗ | Partial | ✗ | ✗ |
| One definition → N surfaces | ✓ | ✗ | ✗ | ✗ | ✗ |

### Every serious app reinvents commanding

- **VS Code**: Built a full command registry (`registerCommand` with id, label,
  icon, keybinding, declarative `when`-clause). Commands surface automatically
  in command palette, context menus, menu bar, and keyboard shortcuts.
- **Files App** (most prominent WinUI3 app, ~40k stars): Built custom `IAction`
  interface with Label, Description, Glyph, HotKey, ExecuteAsync. Does NOT use
  XamlUICommand.
- **Windows Terminal** (Microsoft flagship): Built `ActionAndArgs` + `ActionMap`
  for keybinding resolution. Does NOT use XamlUICommand.
- **Figma, Linear, every IDE**: Custom command/action registries.

The pattern is clear: the framework doesn't provide it, so every team builds
their own. This is the gap Reactor can close.

### SwiftUI is the closest

SwiftUI has `CommandMenu`/`CommandGroup` for macOS menu bars and `FocusedValue`
for routing commands to the focused view. But it still requires repeating
label/icon/shortcut at each use site. There is no "define Cut once, use
everywhere" abstraction.

---

## Research: WinUI3's Commanding Model

### The four-layer model

WinUI3 has a surprisingly rich commanding model that is almost entirely unused
in practice:

**Layer 1 — ICommand.** Controls with `Command` properties: ButtonBase (and all
subclasses), AppBarButton, MenuFlyoutItem, SplitButton, SwipeItem, ContentDialog
(3 commands), TabView, InfoBar, TeachingTip, PagerControl. Setting `Command`
auto-wires Execute on click and disables when CanExecute returns false.

**Layer 2 — XamlUICommand.** Implements ICommand AND bundles Label, IconSource,
Description, KeyboardAccelerators, AccessKey. When set as a control's Command,
WinUI **automatically binds** the metadata to the control:
- AppBarButton gets Content from Label, Icon from IconSource, tooltip from
  Description, accelerators, and access key
- MenuFlyoutItem gets Text from Label, Icon, accelerators, access key, tooltip
- Bindings are "if unset" — per-site overrides work

**Layer 3 — StandardUICommand.** 16 pre-configured XamlUICommands (Cut, Copy,
Paste, SelectAll, Delete, Share, Save, Open, Close, Pause, Play, Stop, Forward,
Backward, Undo, Redo) with correct localized labels, icons, and keyboard
accelerators. You just handle `ExecuteRequested`.

**Layer 4 — CommandingContainer.** Behind `Feature_CommandingImprovements`.
Provides command target context so "Delete" knows which list/item to act on.

### The adoption problem: nobody uses it

Despite this rich model, real-world adoption is essentially zero:

- **Zero Stack Overflow questions** for XamlUICommand. A technology with no SO
  questions is a technology nobody uses.
- **Files App**: Built custom `IAction`, does NOT use XamlUICommand
- **Windows Terminal**: Built custom action system, does NOT use XamlUICommand
- **Dev Home**: Direct event wiring, no commanding
- **WinUI Gallery**: XamlUICommand/StandardUICommand pages exist only as API
  documentation samples, not used in the app's own navigation

**CommunityToolkit.Mvvm is the real ICommand story.** 19.8M NuGet downloads.
Its `[RelayCommand]` source generator is the de facto standard for WinUI3 MVVM
apps. But RelayCommand only implements Execute + CanExecute — it carries no
metadata (no icon, label, keyboard shortcut, description). It's a thin method
wrapper, not a commanding system.

### Why XamlUICommand failed

1. It's an imperative, mutable object in a world moving toward declarative UI
2. `ExecuteRequested`/`CanExecuteRequested` events fight the MVVM pattern
3. The `ICommand` namespace conflict (`Microsoft.UI.Xaml.Input.ICommand` vs
   `System.Windows.Input.ICommand`) caused real friction
4. CommunityToolkit.Mvvm solved the ICommand ergonomics problem without needing
   WinUI's commanding layer

---

## Design Decision: Reactor-Native Command System

### Why not wrap XamlUICommand (Option B)?

Building interop with XamlUICommand solves a problem nobody has. Zero production
apps use it. The two most sophisticated WinUI3 apps both rejected it and built
custom registries. Wrapping it would mean:
- Fighting its imperative event model (ExecuteRequested/CanExecuteRequested)
- Taking on its mutable-object semantics in a declarative framework
- Engineering effort for a dead API

### Why Reactor-native (Option C)?

1. **Proven pattern**: VS Code, Files, and Terminal all built custom registries.
   The design is well-understood.
2. **Declarative from the ground up**: CanExecute is a bool (reactive state),
   not an event. Commands are records (immutable descriptions), not mutable
   objects.
3. **Leverages Context**: Commands can be scoped to subtrees, shadowed, and
   consumed via hooks — using the infrastructure that already exists.
4. **No framework provides this**: A first-class command registry with
   declarative enablement would be a genuine differentiator — the one feature
   area where Reactor could score higher than the competition.
5. **ICommand interop is trivial**: A 10-line adapter class covers migration
   from CommunityToolkit.Mvvm. This doesn't need to be an architectural decision.

---

## Goals

1. **Define once, use everywhere** — a command bundles execute + canExecute +
   label + icon + description + keyboard accelerator into a single record
2. **Declarative enablement** — CanExecute is a bool, not an event. Re-render
   updates it automatically.
3. **Multiple surfaces from one definition** — the same command can drive
   AppBarButton, MenuFlyoutItem, ContextMenu item, and keyboard shortcut
   without repeating metadata
4. **Standard commands** — built-in Cut/Copy/Paste/Undo/Redo/etc. with correct
   icons, localized labels, and keyboard accelerators
5. **Keyboard accelerator integration** — commands with accelerators register
   with the accelerator system automatically
6. **UseCommand hook** — components can define, consume, and override commands
   declaratively
7. **ICommand interop** — simple adapter for existing ICommand implementations

### Non-goals

- Command palette UI (future work — the registry enables it, the UI is separate)
- Undo/redo framework (commands can integrate with one, but we don't build the
  undo stack)
- Menu bar / toolbar layout redesign (existing CommandBar/MenuBar elements are
  fine; they just gain command-aware overloads)
- Command routing to focused view (future work — needs focus management first)

---

## API Design

### 1. Command and Command\<T\> records

The core types: immutable descriptions of actions with all their metadata.

```csharp
/// <summary>
/// An immutable description of a command: what it does, whether it can execute,
/// and all its presentation metadata. Create in component Render() methods —
/// CanExecute and Execute naturally close over component state.
/// </summary>
public sealed record Command
{
    /// <summary>
    /// Human-readable label displayed in menus, toolbars, tooltips.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// The action to perform. For sync commands. Mutually exclusive with ExecuteAsync.
    /// Null means the command exists but has no handler.
    /// </summary>
    public Action? Execute { get; init; }

    /// <summary>
    /// Async action to perform. When running, the command auto-disables to
    /// prevent double-invocation (debounce). Mutually exclusive with Execute.
    /// </summary>
    public Func<Task>? ExecuteAsync { get; init; }

    /// <summary>
    /// Whether the command can currently execute. When false, controls bound
    /// to this command are automatically disabled. Default: true.
    /// Also false while ExecuteAsync is in-flight (auto-debounce).
    /// </summary>
    public bool CanExecute { get; init; } = true;

    /// <summary>
    /// True while an ExecuteAsync invocation is in-flight.
    /// Managed by the UseCommand hook, not set by the developer directly.
    /// </summary>
    public bool IsExecuting { get; init; }

    /// <summary>
    /// Icon for menus, toolbars, and buttons. Uses Reactor's existing IconData
    /// hierarchy: SymbolIcon("Cut"), FontIcon("\uE8C6"), BitmapIcon(uri),
    /// PathIcon(data), ImageIcon(uri).
    /// </summary>
    public IconData? Icon { get; init; }

    /// <summary>
    /// Extended description for tooltips and accessibility (maps to
    /// AutomationProperties.HelpText and ToolTipService.ToolTip).
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Keyboard accelerator. When a command is active (provided via context or
    /// registered in a CommandHost), the accelerator is live.
    /// </summary>
    public KeyboardAcceleratorData? Accelerator { get; init; }

    /// <summary>
    /// Access key (Alt+key) for menu/toolbar keyboard navigation.
    /// </summary>
    public string? AccessKey { get; init; }

    /// <summary>
    /// Effective CanExecute: false if CanExecute is false OR if IsExecuting is true.
    /// This is what controls bind to for IsEnabled.
    /// </summary>
    public bool IsEnabled => CanExecute && !IsExecuting;
}

/// <summary>
/// A parameterized command that takes a typed argument. Use for context-dependent
/// actions like "Delete item X" where X varies per invocation.
/// For untyped commands, use Command (or Command&lt;object&gt; for loose typing).
/// </summary>
public sealed record Command<T>
{
    public required string Label { get; init; }
    public Action<T>? Execute { get; init; }
    public Func<T, Task>? ExecuteAsync { get; init; }
    public bool CanExecute { get; init; } = true;
    public bool IsExecuting { get; init; }
    public IconData? Icon { get; init; }
    public string? Description { get; init; }
    public KeyboardAcceleratorData? Accelerator { get; init; }
    public string? AccessKey { get; init; }
    public bool IsEnabled => CanExecute && !IsExecuting;
}
```
```

**Why a record?** Records give us:
- Immutability (safe to capture in closures, pass through context)
- Value equality (memoization can diff commands structurally)
- `with` expressions (override individual properties per-site)

**Why CanExecute is a bool, not a Func\<bool\>?** Because commands are created
during Render(), which runs on every state change. The bool naturally reflects
current state:

```csharp
var (selection, _) = UseState<string?>(null);

// CanExecute is evaluated during render — always current
var cut = new Command {
    Label = "Cut",
    Icon = SymbolIcon("Cut"),
    Accelerator = Accelerator(VirtualKey.X, VirtualKeyModifiers.Control),
    Execute = () => clipboard.Cut(selection!),
    CanExecute = selection is not null,
};
```

**Why IconData? instead of string?** Reactor already has a full `IconData` type
hierarchy (`Element.cs:680-685`) with `SymbolIconData`, `FontIconData`,
`BitmapIconData`, `PathIconData`, `ImageIconData` — plus DSL factories
(`SymbolIcon("Cut")`, `FontIcon("\uE8C6")`, etc.) and a `ResolveIcon()` method
in the reconciler. Using `IconData?` gives commands full icon flexibility.
The existing element types have a dual `string? Icon` / `IconData? IconElement`
API that's tech debt — commands use only `IconData?` from the start.

**Async commands and auto-debounce.** `ExecuteAsync` supports the common case
of network/IO-bound commands. The `UseCommand` hook manages the async lifecycle:
when `ExecuteAsync` is in-flight, it sets `IsExecuting = true` on the returned
command, which makes `IsEnabled` return `false`, automatically disabling all
controls bound to the command. The component has full visibility into the
async state for loading UI:

```csharp
var save = UseCommand(new Command {
    Label = "Save",
    Icon = SymbolIcon("Save"),
    Accelerator = Accelerator(VirtualKey.S, VirtualKeyModifiers.Control),
    ExecuteAsync = async () => {
        await documentService.SaveAsync(doc);
        setSaved(true);
    },
    CanExecute = isDirty,
});

return VStack(
    Button(save),                                    // auto-disables during save
    save.IsExecuting ? ProgressRing() : null,        // component sees async state
    Text(save.IsExecuting ? "Saving..." : "Ready")   // loading text
);
```

Sync commands (`Execute` only, no `ExecuteAsync`) don't need `UseCommand` — they
can go directly into `AppBarButton(command)` with no wrapping. The hook is only
needed when you want async debounce or need to read `IsExecuting` in render logic.

**Parameterized commands.** `Command<T>` supports context-dependent actions:

```csharp
// Delete command that takes the item to delete
var deleteItem = new Command<FileItem> {
    Label = "Delete",
    Icon = SymbolIcon("Delete"),
    Accelerator = Accelerator(VirtualKey.Delete),
    Execute = item => fileService.Delete(item),
    CanExecute = hasSelection,
};

// In a list template — each item passes itself
MenuItem(deleteItem, item)  // overload: MenuItem(Command<T>, T)
```

### 2. StandardCommand factory

Pre-configured commands for the 16 standard operations. Labels and icons match
WinUI's StandardUICommand for platform consistency. Labels use Reactor's existing
localization system (`UseIntl()`) — localized when a `LocaleProvider` is present,
plain English otherwise (single-language apps shouldn't pay a complexity tax).

```csharp
public static class StandardCommand
{
    /// <summary>
    /// Creates a Cut command with standard icon (✂), Ctrl+X accelerator,
    /// and localized label. Caller provides execute and canExecute.
    /// Async overload auto-debounces during execution.
    /// </summary>
    public static Command Cut(Action execute, bool canExecute = true) => new()
    {
        Label = Strings.Cut,     // localized via resource lookup
        Icon = SymbolIcon("Cut"),
        Accelerator = Accelerator(VirtualKey.X, VirtualKeyModifiers.Control),
        Description = Strings.CutDescription,
        Execute = execute,
        CanExecute = canExecute,
    };

    public static Command Cut(Func<Task> executeAsync, bool canExecute = true) => new()
    {
        Label = Strings.Cut,
        Icon = SymbolIcon("Cut"),
        Accelerator = Accelerator(VirtualKey.X, VirtualKeyModifiers.Control),
        Description = Strings.CutDescription,
        ExecuteAsync = executeAsync,
        CanExecute = canExecute,
    };

    // Sync + async overloads for each:
    public static Command Copy(Action execute, bool canExecute = true) => ...;
    public static Command Copy(Func<Task> executeAsync, bool canExecute = true) => ...;
    public static Command Paste(Action execute, bool canExecute = true) => ...;
    public static Command Paste(Func<Task> executeAsync, bool canExecute = true) => ...;
    public static Command Undo(Action execute, bool canExecute = true) => ...;
    public static Command Redo(Action execute, bool canExecute = true) => ...;
    public static Command Delete(Action execute, bool canExecute = true) => ...;
    public static Command SelectAll(Action execute, bool canExecute = true) => ...;
    public static Command Save(Action execute, bool canExecute = true) => ...;
    public static Command Save(Func<Task> executeAsync, bool canExecute = true) => ...;
    public static Command Open(Action execute, bool canExecute = true) => ...;
    public static Command Open(Func<Task> executeAsync, bool canExecute = true) => ...;
    public static Command Close(Action execute, bool canExecute = true) => ...;
    public static Command Share(Action execute, bool canExecute = true) => ...;
    public static Command Share(Func<Task> executeAsync, bool canExecute = true) => ...;
    public static Command Play(Action execute, bool canExecute = true) => ...;
    public static Command Pause(Action execute, bool canExecute = true) => ...;
    public static Command Stop(Action execute, bool canExecute = true) => ...;
    public static Command Forward(Action execute, bool canExecute = true) => ...;
    public static Command Backward(Action execute, bool canExecute = true) => ...;
}
```

### 3. Command-aware control overloads

Existing DSL factories gain overloads that accept `Command`. The command's
metadata auto-populates the control's properties.

```csharp
// ── Buttons ──

/// Creates a Button driven by a command.
public static ButtonElement Button(Command command) => ...;

/// Creates an AppBarButton driven by a command. All metadata auto-populated.
public static AppBarButtonData AppBarButton(Command command) =>
    new(command.Label)
    {
        OnClick = command.Execute,
        IconElement = command.Icon,
        KeyboardAccelerators = command.Accelerator is { } acc ? [acc] : null,
        IsEnabled = command.IsEnabled,
    };

// ── Menu items ──

/// Creates a MenuFlyoutItem driven by a command.
public static MenuFlyoutItemData MenuItem(Command command) =>
    new(command.Label)
    {
        OnClick = command.Execute,
        IconElement = command.Icon,
        KeyboardAccelerators = command.Accelerator is { } acc ? [acc] : null,
        IsEnabled = command.IsEnabled,
    };

/// Creates a MenuFlyoutItem driven by a parameterized command with a bound argument.
public static MenuFlyoutItemData MenuItem<T>(Command<T> command, T parameter) =>
    new(command.Label)
    {
        OnClick = command.IsEnabled ? () => command.Execute?.Invoke(parameter) : null,
        IconElement = command.Icon,
        KeyboardAccelerators = command.Accelerator is { } acc ? [acc] : null,
        IsEnabled = command.IsEnabled,
    };

// Note: async commands should be wrapped via UseCommand() hook before passing
// to these factories. UseCommand converts ExecuteAsync → Execute with debounce.
// Sync commands can be passed directly — no wrapping needed.
```

**The key ergonomic win**: define a command once, use it in N places with zero
metadata duplication:

```csharp
var cut = StandardCommand.Cut(() => editor.Cut(selection), selection is not null);
var copy = StandardCommand.Copy(() => editor.Copy(selection), selection is not null);
var paste = StandardCommand.Paste(() => editor.Paste(), clipboard.HasContent);

// All three surfaces get label, icon, accelerator, enabled state from the command
CommandBar(
    primary: [AppBarButton(cut), AppBarButton(copy), AppBarButton(paste)]
)

MenuBar([
    Menu("Edit", [MenuItem(cut), MenuItem(copy), MenuItem(paste)])
])

// Context menu
ContextMenu([MenuItem(cut), MenuItem(copy), MenuItem(paste)])
```

### 4. Per-site overrides

Since `Command` is a record, `with` expressions allow per-site customization
while keeping the shared metadata:

```csharp
var delete = StandardCommand.Delete(() => DeleteSelected(), hasSelection);

// Toolbar: standard
AppBarButton(delete)

// Context menu: custom label for this context
MenuItem(delete with { Label = "Remove from playlist" })

// Danger zone: custom icon + description
Button(delete with {
    Icon = SymbolIcon("Important"),
    Description = "This action cannot be undone"
})
```

### 5. IsEnabled on existing element types

To support command-driven disabling, add `IsEnabled` to element types that don't
have it:

```csharp
// AppBarButtonData gains IsEnabled
public record AppBarButtonData(string Label, Action? OnClick = null, string? Icon = null)
    : AppBarItemBase
{
    public bool IsEnabled { get; init; } = true;
    public KeyboardAcceleratorData[]? KeyboardAccelerators { get; init; }
}

// MenuFlyoutItemData gains IsEnabled
public record MenuFlyoutItemData(string Text, Action? OnClick = null, string? Icon = null)
    : MenuFlyoutItemBase
{
    public bool IsEnabled { get; init; } = true;
    public KeyboardAcceleratorData[]? KeyboardAccelerators { get; init; }
}
```

The reconciler applies `IsEnabled` during mount/update:

```csharp
// In MountAppBarButton / UpdateAppBarButton:
if (!data.IsEnabled)
    button.IsEnabled = false;
```

### 6. CommandHost element (keyboard accelerator scope)

Commands with accelerators need a scope — a region of the UI where the
accelerators are active. `CommandHost` provides this:

```csharp
/// <summary>
/// Registers commands' keyboard accelerators on a scope element.
/// Accelerators are live while the CommandHost is in the visual tree.
/// </summary>
public static Element CommandHost(Command[] commands, Element child) =>
    new CommandHostElement(commands, child);
```

Usage:

```csharp
var cut = StandardCommand.Cut(...);
var copy = StandardCommand.Copy(...);
var paste = StandardCommand.Paste(...);
var save = StandardCommand.Save(...);

// All four commands' accelerators are active within this scope
return CommandHost([cut, copy, paste, save],
    VStack(
        CommandBar(primary: [AppBarButton(cut), AppBarButton(copy), AppBarButton(paste)]),
        editor
    )
);
```

The reconciler implementation:
- On mount: creates WinUI `KeyboardAccelerator` objects and attaches them to the
  host element's `KeyboardAccelerators` collection
- On update: diffs the command list, adds/removes accelerators as needed
- Each accelerator's `Invoked` event calls the command's `Execute` (only if
  `CanExecute` is true)
- The scope element's visual tree defines where accelerators are active

### 7. UseCommand hook (async lifecycle management)

The `UseCommand` hook wraps a `Command` with `ExecuteAsync` and manages the
async lifecycle — tracking `IsExecuting` state and triggering re-render on
completion. The component gets full visibility into the async state.

```csharp
/// <summary>
/// Wraps a command's ExecuteAsync with lifecycle management. Returns a new
/// Command with IsExecuting reflecting the current async state.
/// When ExecuteAsync completes (or fails), triggers a re-render so the
/// component sees the updated IsExecuting = false.
///
/// For sync-only commands (Execute, no ExecuteAsync), returns the command
/// unchanged — no hook state consumed, no overhead.
/// </summary>
public Command UseCommand(Command command)
{
    // No async — pass through unchanged
    if (command.ExecuteAsync is null)
        return command;

    var (isExecuting, setIsExecuting) = UseState(false);

    // Wrap the async execute with state management
    var wrappedExecute = UseMemo(() => new Action(() =>
    {
        if (isExecuting) return; // guard re-entrance
        setIsExecuting(true);
        _ = Task.Run(async () =>
        {
            try
            {
                await command.ExecuteAsync();
            }
            finally
            {
                setIsExecuting(false); // triggers re-render
            }
        });
    }), command.ExecuteAsync, isExecuting);

    return command with
    {
        Execute = wrappedExecute,
        ExecuteAsync = null,  // consumed — Execute is now the entry point
        IsExecuting = isExecuting,
    };
}
```

**Design notes:**

- `UseCommand` consumes 2 hook slots (UseState + UseMemo) per async command.
  Sync commands consume zero — the early return avoids hook calls, but this
  means UseCommand for async commands must always be called (can't be
  conditional). This follows standard hook rules.
- The returned command has `Execute` (sync, wraps the async call) instead of
  `ExecuteAsync` — the debounce is transparent to controls.
- `IsExecuting` is visible to the component for loading UI (spinners, text).
- Multiple controls bound to the same `UseCommand` result share one
  `IsExecuting` flag — click one, all disable. This is correct because
  they're the same command record.
- Errors in `ExecuteAsync` set `IsExecuting = false` in the `finally` block.
  Error handling (toast, error boundary) is the component's responsibility.

**Parameterized variant:**

```csharp
/// For Command<T>, UseCommand manages the same async lifecycle.
public Command<T> UseCommand<T>(Command<T> command)
{
    if (command.ExecuteAsync is null)
        return command;

    var (isExecuting, setIsExecuting) = UseState(false);

    var wrappedExecute = UseMemo(() => new Action<T>(arg =>
    {
        if (isExecuting) return;
        setIsExecuting(true);
        _ = Task.Run(async () =>
        {
            try { await command.ExecuteAsync(arg); }
            finally { setIsExecuting(false); }
        });
    }), command.ExecuteAsync, isExecuting);

    return command with
    {
        Execute = wrappedExecute,
        ExecuteAsync = null,
        IsExecuting = isExecuting,
    };
}
```

### 8. Context-based command sharing

For components that want to provide commands to their subtree (e.g., an editor
component providing Cut/Copy/Paste to a surrounding toolbar):

```csharp
// Define a command set context
public static readonly Context<EditorCommands?> EditorCommandsCtx = new(null);

public record EditorCommands(
    Command Cut,
    Command Copy,
    Command Paste,
    Command Undo,
    Command Redo
);

// Editor component provides commands to its subtree
class EditorComponent : Component
{
    public override Element Render()
    {
        var (selection, _) = UseState<string?>(null);
        var (history, _) = UseState(new UndoHistory());

        var commands = new EditorCommands(
            Cut: StandardCommand.Cut(() => Cut(selection!), selection is not null),
            Copy: StandardCommand.Copy(() => Copy(selection!), selection is not null),
            Paste: StandardCommand.Paste(() => Paste(), HasClipboard()),
            Undo: StandardCommand.Undo(() => history.Undo(), history.CanUndo),
            Redo: StandardCommand.Redo(() => history.Redo(), history.CanRedo)
        );

        return VStack(editor)
            .Provide(EditorCommandsCtx, commands);
    }
}

// Toolbar component consumes commands from any editor in its ancestor chain
class ToolbarComponent : Component
{
    public override Element Render()
    {
        var commands = UseContext(EditorCommandsCtx);
        if (commands is null) return Empty();

        return CommandBar(primary: [
            AppBarButton(commands.Cut),
            AppBarButton(commands.Copy),
            AppBarButton(commands.Paste),
            AppBarButton(commands.Undo),
            AppBarButton(commands.Redo),
        ]);
    }
}
```

This uses the existing `Context` system — no new infrastructure needed. The
toolbar automatically re-renders when the editor's commands change (e.g., when
selection state changes and CanExecute flips), because `Context` triggers
re-render on value change.

### 9. ICommand interop adapter

A simple adapter for migrating from CommunityToolkit.Mvvm `[RelayCommand]`-based
ViewModels:

```csharp
/// <summary>
/// Wraps an existing ICommand as a Command for migration scenarios.
/// CanExecute is re-evaluated during each render (no event subscription needed).
/// </summary>
public static class CommandInterop
{
    public static Command FromCommand(
        System.Windows.Input.ICommand command,
        string label,
        IconData? icon = null,
        string? description = null,
        KeyboardAcceleratorData? accelerator = null,
        object? parameter = null)
    {
        return new Command
        {
            Label = label,
            Icon = icon,
            Description = description,
            Accelerator = accelerator,
            Execute = () => command.Execute(parameter),
            CanExecute = command.CanExecute(parameter),
        };
    }
}
```

Usage during migration:

```csharp
// Existing ViewModel with CommunityToolkit.Mvvm
public partial class EditorViewModel : ObservableObject
{
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Cut() { ... }
}

// In Reactor component — bridge the existing ICommand
var vm = UseObservable(editorViewModel);
var cut = CommandInterop.FromCommand(vm.CutCommand, "Cut",
    icon: SymbolIcon("Cut"),
    accelerator: Accelerator(VirtualKey.X, VirtualKeyModifiers.Control));

AppBarButton(cut)
```

This is intentionally minimal — a migration aid, not a core feature. Developers
are expected to move to native `Command` over time.

---

## Reconciler Integration

### Command metadata propagation

When the reconciler encounters a command-driven element, it maps metadata:

| Command property | AppBarButton | MenuFlyoutItem | Button |
|---|---|---|---|
| `Label` | Content | Text | Content |
| `Icon` (IconData) | Icon (via ResolveIcon) | Icon (via ResolveIcon) | *(not standard — prepend to content)* |
| `IsEnabled` (CanExecute && !IsExecuting) | IsEnabled | IsEnabled | IsEnabled |
| `Execute` | Click handler | Click handler | Click handler |
| `Accelerator` | KeyboardAccelerators | KeyboardAccelerators | *(via CommandHost scope)* |
| `AccessKey` | AccessKey | AccessKey | AccessKey |
| `Description` | ToolTip + A11y HelpText | ToolTip + A11y HelpText | ToolTip + A11y HelpText |

### CommandHost accelerator management

The reconciler manages `CommandHostElement` by:

1. **Mount**: Iterates commands, creates `KeyboardAccelerator` for each with
   an accelerator, attaches to the host element's `KeyboardAccelerators`
   collection, wires `Invoked` to call `Execute` (guarded by `CanExecute`)
2. **Update**: Diffs old vs new command list by accelerator key+modifiers.
   Adds new accelerators, removes stale ones, updates handlers for changed
   commands
3. **Unmount**: Clears all accelerators from the host element

**CanExecute enforcement in accelerators**: The `Invoked` handler checks the
current element's Tag (which holds the current `CommandHostElement`) to get the
latest `IsEnabled` value (which reflects both `CanExecute` and `IsExecuting`
when the command was wrapped via `UseCommand`). This follows the existing
Tag-based event dispatch pattern.

### Memoization

`Command` is a record, so structural equality works automatically. The
reconciler can skip updates when the command hasn't changed (same label, same
icon, same CanExecute, same accelerator). The `Execute` delegate comparison will
typically fail (new closure each render), but the reconciler only needs to update
the Tag — the Click handler reads from Tag at invocation time.

---

## Implementation Plan

### Phase 1: Core types and command-aware overloads

**Scope**: `Command` record, `Command<T>` record, `StandardCommand`
factory (sync + async overloads), command-aware `AppBarButton(Command)`,
`MenuItem(Command)`, `MenuItem(Command<T>, T)`, `Button(Command)`
overloads, `IsEnabled` on `AppBarButtonData` and `MenuFlyoutItemData`.

**Deliverables**:
- `Reactor/Core/Command.cs` — Command, Command\<T\>, StandardCommand
- `Reactor/Core/RenderContext.cs` — UseCommand hook (async lifecycle management)
- `Reactor/Core/Component.cs` — UseCommand convenience method
- `Reactor/Elements/Dsl.cs` — command-aware overloads including parameterized
- `Reactor/Core/Element.cs` — IsEnabled on AppBarButtonData, MenuFlyoutItemData
- `Reconciler.Mount.cs` / `Reconciler.Update.cs` — IsEnabled handling
- Unit tests: command record equality, StandardCommand factory correctness,
  IsEnabled propagation, UseCommand async debounce (IsExecuting lifecycle,
  re-entrance guard, error handling), parameterized command binding

**Validates**: "define once, use in N places" ergonomic, auto-populated metadata,
hook-based async debounce with component-visible IsExecuting, parameterized
commands.

### Phase 2: CommandHost and keyboard accelerator scope

**Scope**: `CommandHostElement`, reconciler mounting/updating of scoped keyboard
accelerators from commands.

**Deliverables**:
- `Reactor/Core/Element.cs` — CommandHostElement record
- `Reactor/Elements/Dsl.cs` — `CommandHost()` factory
- `Reconciler.Mount.cs` / `Reconciler.Update.cs` — accelerator lifecycle
- Integration tests: accelerator registration, scope boundaries, CanExecute
  enforcement

**Validates**: Commands' keyboard shortcuts are live without manual accelerator
wiring.

### Phase 3: Context-based command sharing

**Scope**: Documentation and examples showing the Context pattern for
sharing commands between editor components and toolbars. `CommandInterop` adapter.

**Deliverables**:
- `Reactor/Core/CommandInterop.cs` — ICommand adapter
- Sample: editor component providing commands via Context, toolbar consuming
- Documentation: commanding patterns guide

**Validates**: End-to-end scenario of toolbar reflecting editor's command state.

### Phase 4: Description → Tooltip + Accessibility

**Scope**: When a command has `Description`, auto-set `ToolTipService.ToolTip`
and `AutomationProperties.HelpText` on the bound control.

**Deliverables**:
- Reconciler updates for tooltip and a11y propagation from command metadata
- Tests validating tooltip and UIA properties

---

## What This Achieves (Scorecard Impact)

### Before

| Feature Area | Grade | Notes |
|---|---|---|
| Commands | F | No ICommand equivalent |
| Input/Events | C+ | Semantic events good, rest is .Set() |

### After (projected)

| Feature Area | Grade | Notes |
|---|---|---|
| Commands | A- | Define-once commands, standard commands, accelerator scoping, context-based sharing; no command routing to focused view yet |
| Input/Events | B- | Commands close the toolbar/menu gap; gesture system and pointer events still missing |

### Competitive position

| | Reactor (after) | React | SwiftUI | Compose |
|---|---|---|---|---|
| Command bundling (metadata + action) | ✓ | ✗ | ✗ | ✗ |
| Standard commands with correct metadata | ✓ | ✗ | Partial | ✗ |
| Declarative CanExecute (bool, not events) | ✓ | ✓ | ✓ | ✓ |
| One definition → N surfaces | ✓ | ✗ | ✗ | ✗ |
| Keyboard accelerator from command | ✓ | ✗ | ✓ | Desktop only |
| Context-scoped command sharing | ✓ | ✗ | ✓ (FocusedValue) | ✗ |
| Command routing to focused view | ✗ (future) | ✗ | ✓ (FocusedValue) | ✗ |

This would be the **first reactive UI framework with first-class command
bundling** — a genuine differentiator. React, SwiftUI, and Compose all require
ad-hoc solutions for the "define once, use everywhere" pattern. Reactor solves it
at the framework level.

---

## Resolved Design Decisions

1. **Async commands**: Yes. `Command` has `Func<Task>? ExecuteAsync` alongside
   `Action? Execute`. The `UseCommand` hook manages the async lifecycle: when
   `ExecuteAsync` is in-flight, it returns a command with `IsExecuting = true`,
   which makes `IsEnabled` return `false`, auto-disabling all bound controls
   (debounce). The hook approach was chosen over reconciler-managed state because
   it keeps `IsExecuting` visible to the component for loading UI (spinners,
   status text). Sync commands don't need the hook — they pass through unchanged.

2. **Command groups**: No dedicated type. Use plain records with `Command`
   fields (as shown in the `EditorCommands` example). Keeps complexity low.

3. **Parameterized commands**: Yes. `Command<T>` with `Action<T>? Execute`
   and `Func<T, Task>? ExecuteAsync`. Strong typing by default; developers can
   use `Command<object>` for loose typing. DSL overloads like
   `MenuItem(Command<T> command, T parameter)` bind the argument at the
   call site.

4. **Localization**: StandardCommand labels use Reactor's existing localization
   system. When a `LocaleProvider` is present, labels resolve from resource
   files. When no provider is present, labels are plain English strings. Single-
   language apps pay no complexity tax.

5. **Icon system**: `Command.Icon` is `IconData?`, using Reactor's existing
   type hierarchy (`Element.cs:680-685`): `SymbolIconData`, `FontIconData`,
   `BitmapIconData`, `PathIconData`, `ImageIconData`. DSL factories
   (`SymbolIcon("Cut")`, `FontIcon("\uE8C6")`, etc.) and the reconciler's
   `ResolveIcon()` method already handle the full mapping to WinUI `IconElement`.
   This avoids the dual `string? Icon` / `IconData? IconElement` tech debt
   present in existing element types.
