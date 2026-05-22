# Commanding System Implementation Tasks

Derived from: `docs/specs/012-commanding-design.md`

---

## Phase 1: Core Types — Command Records

### 1.1 Command Record
- [x] Create `Reactor/Core/Command.cs`
- [x] Define `Command` sealed record with all properties:
  - [x] `required string Label`
  - [x] `Action? Execute`
  - [x] `Func<Task>? ExecuteAsync`
  - [x] `bool CanExecute` (default `true`)
  - [x] `bool IsExecuting`
  - [x] `IconData? Icon`
  - [x] `string? Description`
  - [x] `KeyboardAcceleratorData? Accelerator`
  - [x] `string? AccessKey`
  - [x] `bool IsEnabled => CanExecute && !IsExecuting` (computed)

### 1.2 Command\<T\> Record
- [x] Define `Command<T>` sealed record in same file
  - [x] `required string Label`
  - [x] `Action<T>? Execute`
  - [x] `Func<T, Task>? ExecuteAsync`
  - [x] `bool CanExecute` (default `true`)
  - [x] `bool IsExecuting`
  - [x] `IconData? Icon`
  - [x] `string? Description`
  - [x] `KeyboardAcceleratorData? Accelerator`
  - [x] `string? AccessKey`
  - [x] `bool IsEnabled => CanExecute && !IsExecuting` (computed)

### 1.3 Unit Tests — Core Records
- [x] Test: `Command` structural equality (two commands with same properties are equal)
- [x] Test: `Command` `with` expression creates modified copy
- [x] Test: `IsEnabled` returns `false` when `CanExecute = false`
- [x] Test: `IsEnabled` returns `false` when `IsExecuting = true`
- [x] Test: `IsEnabled` returns `false` when both `CanExecute = false` and `IsExecuting = true`
- [x] Test: `IsEnabled` returns `true` when `CanExecute = true` and `IsExecuting = false`
- [x] Test: `Command<T>` record equality and `IsEnabled` logic matches `Command`

---

## Phase 2: StandardCommand Factory

### 2.1 StandardCommand Static Class
- [x] Create `StandardCommand` static class in `Reactor/Core/StandardCommand.cs`
- [x] Implement sync + async overloads for all 16 standard commands:
  - [x] `Cut` — SymbolIcon("Cut"), Ctrl+X
  - [x] `Copy` — SymbolIcon("Copy"), Ctrl+C
  - [x] `Paste` — SymbolIcon("Paste"), Ctrl+V
  - [x] `Undo` — SymbolIcon("Undo"), Ctrl+Z
  - [x] `Redo` — SymbolIcon("Redo"), Ctrl+Y
  - [x] `Delete` — SymbolIcon("Delete"), Delete key
  - [x] `SelectAll` — Ctrl+A (no standard icon)
  - [x] `Save` — SymbolIcon("Save"), Ctrl+S
  - [x] `Open` — SymbolIcon("OpenFile"), Ctrl+O
  - [x] `Close` — Ctrl+W
  - [x] `Share` — SymbolIcon("Share")
  - [x] `Play` — SymbolIcon("Play")
  - [x] `Pause` — SymbolIcon("Pause")
  - [x] `Stop` — SymbolIcon("Stop")
  - [x] `Forward` — SymbolIcon("Forward")
  - [x] `Backward` — SymbolIcon("Back")

### 2.2 Localization Integration
- [x] Wire `StandardCommand` labels through Microsoft.UI.Reactor's existing localization system (`UseIntl()`)
- [x] Add localization resource strings for all 16 standard command labels and descriptions
- [x] Verify plain English fallback when no `LocaleProvider` is present

### 2.3 Unit Tests — StandardCommand
- [x] Test: each `StandardCommand` factory returns correct `Label`, `Icon`, `Accelerator`
- [x] Test: sync overload sets `Execute`, not `ExecuteAsync`
- [x] Test: async overload sets `ExecuteAsync`, not `Execute`
- [x] Test: `canExecute` parameter flows through to `CanExecute` property
- [x] Test: default `canExecute` is `true`

---

## Phase 3: IsEnabled on Existing Element Types

### 3.1 Add IsEnabled to Element Records
- [x] Add `bool IsEnabled { get; init; } = true` to `AppBarButtonData` in `Element.cs`
- [x] Add `bool IsEnabled { get; init; } = true` to `MenuFlyoutItemData` in `Element.cs`
- [x] Verify `ButtonElement` already has or add `IsEnabled` support

### 3.2 Reconciler — IsEnabled Handling
- [x] Update `MountAppBarButton` in `Reconciler.Mount.cs` to apply `IsEnabled`
- [x] Update `UpdateAppBarButton` in `Reconciler.Update.cs` to diff and apply `IsEnabled`
- [x] Update `MountMenuFlyoutItem` in `Reconciler.Mount.cs` to apply `IsEnabled`
- [x] Update `UpdateMenuFlyoutItem` in `Reconciler.Update.cs` to diff and apply `IsEnabled`
- [x] Update `MountButton` / `UpdateButton` if needed for `IsEnabled` (already had it)

### 3.3 Unit Tests — IsEnabled Reconciliation
- [x] Test: `AppBarButton` with `IsEnabled = false` renders disabled WinUI control
- [x] Test: `AppBarButton` toggling `IsEnabled` updates WinUI control's `IsEnabled`
- [x] Test: `MenuFlyoutItem` with `IsEnabled = false` renders disabled
- [x] Test: `MenuFlyoutItem` toggling `IsEnabled` updates correctly
- [x] Test: default `IsEnabled = true` does not unnecessarily set the property

---

## Phase 4: Command-Aware DSL Overloads

### 4.1 Button Overloads
- [x] Add `Button(Command command)` to `Dsl.cs`
  - Maps: Label → Content, Execute → Click, IsEnabled → IsEnabled, Description → Tooltip

### 4.2 AppBarButton Overloads
- [x] Add `AppBarButton(Command command)` to `Dsl.cs`
  - Maps: Label → Content, Icon → IconElement, Execute → OnClick, Accelerator → KeyboardAccelerators, IsEnabled → IsEnabled, AccessKey → AccessKey, Description → Tooltip

### 4.3 MenuItem Overloads
- [x] Add `MenuItem(Command command)` to `Dsl.cs`
  - Maps: Label → Text, Icon → IconElement, Execute → OnClick, Accelerator → KeyboardAccelerators, IsEnabled → IsEnabled, AccessKey → AccessKey
- [x] Add `MenuItem<T>(Command<T> command, T parameter)` to `Dsl.cs`
  - Wraps `Execute` to invoke with bound parameter

### 4.4 Unit Tests — Command-Aware Overloads
- [x] Test: `Button(command)` maps all metadata correctly
- [x] Test: `AppBarButton(command)` maps Label, Icon, Accelerator, IsEnabled, AccessKey
- [x] Test: `MenuItem(command)` maps Label, Icon, Accelerator, IsEnabled
- [x] Test: `MenuItem(command, parameter)` invokes `Execute` with correct argument
- [x] Test: disabled command (`CanExecute = false`) results in disabled controls
- [x] Test: command with no icon results in no icon on control
- [x] Test: command with no accelerator results in no accelerator on control

---

## Phase 5: UseCommand Hook (Async Lifecycle)

### 5.1 UseCommand in RenderContext
- [x] Add `UseCommand(Command command)` method to `RenderContext.cs`
  - [x] Early return for sync-only commands (no hook slots consumed)
  - [x] `UseState(false)` for `isExecuting` tracking
  - [x] `UseMemo` for wrapped execute action with re-entrance guard
  - [x] Return command `with { Execute = wrapped, ExecuteAsync = null, IsExecuting = isExecuting }`

### 5.2 UseCommand\<T\> in RenderContext
- [x] Add `UseCommand<T>(Command<T> command)` method to `RenderContext.cs`
  - [x] Same pattern: early return for sync, UseState + UseMemo for async
  - [x] Wrapped execute invokes `ExecuteAsync(arg)` with state management

### 5.3 Component Convenience Methods
- [x] Add `UseCommand(Command)` convenience method to `Component.cs`
- [x] Add `UseCommand<T>(Command<T>)` convenience method to `Component.cs`

### 5.4 Unit Tests — UseCommand
- [x] Test: sync command passes through unchanged (no hook state consumed)
- [x] Test: async command returns with `Execute` set and `ExecuteAsync = null`
- [x] Test: `IsExecuting` is `false` initially
- [x] Test: `IsExecuting` becomes `true` while async execute is in-flight
- [x] Test: `IsExecuting` returns to `false` after async execute completes
- [x] Test: re-entrance guard prevents double-invocation during execution
- [x] Test: `IsEnabled` is `false` while `IsExecuting` is `true`
- [x] Test: error in `ExecuteAsync` still sets `IsExecuting = false` (finally block)
- [x] Test: parameterized `UseCommand<T>` tracks `IsExecuting` correctly
- [x] Test: parameterized `UseCommand<T>` passes argument through to `ExecuteAsync`

---

## Phase 6: CommandHost Element (Keyboard Accelerator Scope)

### 6.1 CommandHostElement Record
- [x] Add `CommandHostElement` record to `Element.cs`
  - [x] `Command[] Commands` property
  - [x] `Element Child` property

### 6.2 CommandHost DSL Factory
- [x] Add `CommandHost(Command[] commands, Element child)` to `Dsl.cs`

### 6.3 Reconciler — CommandHost Mount
- [x] Add `MountCommandHost` to `Reconciler.Mount.cs`
  - [x] Create host UIElement (Grid panel)
  - [x] Iterate commands with accelerators
  - [x] Create WinUI `KeyboardAccelerator` for each
  - [x] Wire `Invoked` event to call `Execute` (guarded by `IsEnabled`)
  - [x] Store command references in Tag for later access

### 6.4 Reconciler — CommandHost Update
- [x] Add `UpdateCommandHost` to `Reconciler.Update.cs`
  - [x] Update child element in-place
  - [x] Clear and rebuild accelerators on update (handlers reference command closures)

### 6.5 Reconciler — CommandHost Unmount
- [x] Handle unmount: Grid.KeyboardAccelerators cleared automatically on GC

### 6.6 Unit Tests — CommandHost
- [x] Test: commands with accelerators register `KeyboardAccelerator` on host element
- [x] Test: commands without accelerators don't create accelerators
- [x] Test: accelerator invocation calls `Execute` when `IsEnabled = true`
- [x] Test: accelerator invocation does NOT call `Execute` when `IsEnabled = false`
- [x] Test: updating commands adds/removes accelerators correctly
- [x] Test: unmount clears all accelerators
- [x] Test: child element renders correctly within CommandHost

### 6.7 Integration Tests — CommandHost
- [x] Test: accelerator scope is limited to CommandHost subtree
- [x] Test: nested CommandHosts — inner shadows outer for same key combo
- [x] Test: commands with `UseCommand` async debounce work with CommandHost accelerators

---

## Phase 7: Context-Based Command Sharing

### 7.1 Documentation & Patterns
- [x] Document the `Context<TCommandSet>` pattern for command sharing
- [x] Document the editor-provides / toolbar-consumes pattern
- [x] Add code examples showing `.Provide()` and `UseContext()` with command records

### 7.2 ICommand Interop Adapter
- [x] Create `Reactor/Core/CommandInterop.cs`
- [x] Implement `CommandInterop.FromCommand(ICommand, label, icon?, description?, accelerator?, parameter?)`
  - [x] Maps `ICommand.Execute` → `Command.Execute`
  - [x] Maps `ICommand.CanExecute` → `Command.CanExecute` (evaluated at render time)

### 7.3 Unit Tests — CommandInterop
- [x] Test: `FromCommand` maps `Execute` correctly
- [x] Test: `FromCommand` evaluates `CanExecute` at creation time
- [x] Test: `FromCommand` passes `parameter` to both `Execute` and `CanExecute`
- [x] Test: metadata (label, icon, description, accelerator) flows through

---

## Phase 8: Description → Tooltip + Accessibility

### 8.1 Tooltip Propagation
- [x] Update `MountAppBarButton` to set `ToolTipService.ToolTip` from command's `Description`
- [x] Update `MountMenuFlyoutItem` to set `ToolTipService.ToolTip` from command's `Description`
- [x] Update `MountButton` to set `ToolTipService.ToolTip` from command's `Description` (via DSL mapping)
- [x] Update corresponding `Update*` methods to diff and apply tooltip changes

### 8.2 Accessibility Propagation
- [x] Set `AutomationProperties.HelpText` from command's `Description` on mount
- [x] Update `AutomationProperties.HelpText` on command update

### 8.3 Unit Tests — Tooltip & Accessibility
- [x] Test: command with `Description` sets tooltip on AppBarButton
- [x] Test: command with `Description` sets tooltip on MenuFlyoutItem
- [x] Test: command with `Description` sets tooltip on Button
- [x] Test: command with `Description` sets `AutomationProperties.HelpText`
- [x] Test: command with `null` Description does not set tooltip or HelpText
- [x] Test: updating Description changes tooltip and HelpText

---

## Phase 9: Samples

### 9.1 Commanding Demo Sample
- [x] Create `samples/CommandingDemo/` project with standard `.csproj`
- [x] Implement basic commanding demo:
  - [x] Text editor area with selection state
  - [x] Cut/Copy/Paste using `StandardCommand` factories
  - [x] CommandBar with `AppBarButton(command)` overloads
  - [x] MenuBar with `MenuItem(command)` overloads
  - [x] Context menu with same commands (demonstrating "define once, use N places")
  - [x] Commands auto-disable when no selection (CanExecute demo)
- [x] Implement async command demo:
  - [x] Save button using `UseCommand` with `ExecuteAsync`
  - [x] ProgressRing shown while `IsExecuting` is true
  - [x] Button auto-disables during save (debounce demo)
- [x] Implement parameterized command demo:
  - [x] List of items with `Command<T>` delete command
  - [x] Each item passes itself as parameter
- [x] Implement CommandHost demo:
  - [x] Keyboard accelerators scoped to a region
  - [x] Ctrl+S, Ctrl+Z, Ctrl+Y active within CommandHost scope
- [x] Implement per-site override demo:
  - [x] Same delete command with different labels in toolbar vs context menu (`with` expression)
- [x] Implement context-based command sharing demo:
  - [x] Editor component providing commands via `Context`
  - [x] Separate toolbar component consuming commands via `UseContext`
- [ ] Implement ICommand interop demo:
  - [ ] ViewModel with `[RelayCommand]` from CommunityToolkit.Mvvm
  - [ ] Bridge to `Command` via `CommandInterop.FromCommand`

### 9.2 Update Existing Samples
- [ ] Review `samples/Reactor.TestApp/` — add commanding examples to relevant pages
- [ ] Review `samples/apps/reactorfiles/` — replace bare `Action` callbacks with commands for toolbar actions (Cut/Copy/Paste/Delete)
- [ ] Review `samples/apps/outlook/` — replace toolbar actions with commands
- [ ] Review `samples/apps/regedit/` — replace toolbar/menu actions with commands where appropriate
- [ ] Review `samples/apps/monaco-editor/` — add Cut/Copy/Paste/Undo/Redo commands

---

## Phase 10: Selfhost / Integration Tests

### 10.1 Selfhost Tests (Reactor.AppTests)
- [ ] Add commanding selfhost test page to `Reactor.AppTests`
- [ ] Test: AppBarButton driven by Command renders with correct label and icon
- [ ] Test: AppBarButton driven by disabled command renders as disabled
- [ ] Test: MenuFlyoutItem driven by Command renders with correct text and icon
- [ ] Test: clicking command-driven button invokes the command's Execute
- [ ] Test: async command auto-disables button during execution and re-enables after
- [ ] Test: CommandHost registers keyboard accelerators on the host element
- [ ] Test: keyboard accelerator triggers command execution
- [ ] Test: per-site override (`with { Label = "..." }`) shows overridden label

### 10.2 Appium / E2E Tests
- [ ] Add E2E test: command-driven toolbar renders all buttons with correct labels
- [ ] Add E2E test: clicking a command button performs the action
- [ ] Add E2E test: disabled command results in non-interactive button (verify via UIA `IsEnabled`)
- [ ] Add E2E test: keyboard accelerator (e.g., Ctrl+S) triggers the command
- [ ] Add E2E test: async command shows loading state and re-enables after completion
- [ ] Add E2E test: context menu with command-driven items shows correct labels and icons
- [ ] Add E2E test: parameterized command in a list context-menu passes correct item

---

## Phase 11: SKILL.md Update

### 11.1 Add Commanding Section to SKILL.md
- [x] Add "Commands" section to SKILL.md covering:
  - [x] When to use commands vs bare `Action` callbacks
  - [x] `Command` record — all properties and their purpose
  - [x] `StandardCommand` factory — all 16 commands with usage examples
  - [x] Command-aware DSL overloads (`Button(cmd)`, `AppBarButton(cmd)`, `MenuItem(cmd)`)
  - [x] Per-site overrides with `with` expressions
  - [x] `UseCommand` hook — when needed (async only) and how it works
  - [x] `CommandHost` — keyboard accelerator scoping
  - [x] Context-based command sharing via `Context`
  - [x] `CommandInterop.FromCommand` for ICommand migration
  - [x] `Command<T>` parameterized commands
  - [x] Common patterns: define-once-use-everywhere, editor+toolbar, async save with loading UI

### 11.2 AI Guidance — When to Use Commands
- [x] Add guidance on when AI should suggest commands vs bare actions:
  - [x] Use commands when: action appears in multiple surfaces, needs keyboard shortcut, needs CanExecute disabling, is a standard operation (Cut/Copy/Paste/etc.)
  - [x] Use bare `Action` when: simple one-off button click, no need for metadata or reuse
- [x] Add guidance on async vs sync command choice
- [x] Add guidance on when `UseCommand` hook is needed vs not
- [x] Add common mistakes / anti-patterns to avoid

---

## Summary

| Phase | Description | Files Changed |
|-------|-------------|---------------|
| 1 | Core Command records | `Command.cs` (new) |
| 2 | StandardCommand factory | `Command.cs`, localization resources |
| 3 | IsEnabled on element types | `Element.cs`, `Reconciler.Mount.cs`, `Reconciler.Update.cs` |
| 4 | Command-aware DSL overloads | `Dsl.cs` |
| 5 | UseCommand hook | `RenderContext.cs`, `Component.cs` |
| 6 | CommandHost element | `Element.cs`, `Dsl.cs`, `Reconciler.Mount.cs`, `Reconciler.Update.cs` |
| 7 | Context sharing + ICommand interop | `CommandInterop.cs` (new), docs |
| 8 | Description → Tooltip + A11y | `Reconciler.Mount.cs`, `Reconciler.Update.cs` |
| 9 | Samples | `CommandingDemo/` (new), updates to existing samples |
| 10 | Selfhost + E2E tests | `Reactor.AppTests`, Appium tests |
| 11 | SKILL.md | `SKILL.md` |
