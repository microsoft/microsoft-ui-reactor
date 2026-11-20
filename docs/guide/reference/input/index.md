# Input and commands

Commands, element refs, focus, drag and drop, and gestures.

**Learn more:** [Input and gestures](../../input-and-gestures.md), [Commanding](../../commanding.md)

## Types

| Type | Description |
| --- | --- |
| [Drag and drop](drag-and-drop.md) | Types for dragging data out of and dropping it onto elements. |
| [Element refs](element-refs.md) | Refs to mounted controls. |
| [Gestures](gestures.md) | Gesture events and their options. |
| [`Command`](Command.md) | Immutable (init-only) command descriptor that bundles an action with its metadata (label, icon, keyboard accelerator, enabled state). |
| [`Command<T>`](Command-1.md) | Immutable (init-only) parameterized command descriptor. |
| [`CommandInterop`](CommandInterop.md) | Bridges ICommand (MVVM/CommunityToolkit) to Command, enabling migration from ViewModel-based patterns to Reactor's declarative commanding. var reactorCmd = CommandInterop.FromCommand(viewModel.SaveCommand, "Save", icon: new SymbolIconData("Save"), accelerator: Accelerator(VirtualKey.S, Control)); |
| [`FocusManager`](FocusManager.md) | Imperative focus helpers (spec 027 Tier 5). |
| [`StandardCommand`](StandardCommand.md) | Factory methods for the 16 standard application commands with pre-filled metadata (label, icon, keyboard accelerator). |
