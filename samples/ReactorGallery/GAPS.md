# WinUI Gallery → Reactor: Feature Gaps

This document lists WinUI Gallery controls, features, and XAML concepts that **cannot be directly ported** to the Reactor declarative framework, along with the recommended Reactor alternative where one exists.

---

## Controls with No Reactor Equivalent

### InkCanvas / InkToolbar
**WinUI:** Rich inking surface with pen, pencil, and eraser tools.
**Reactor Alternative:** None. Inking requires platform ink infrastructure. Use `.Set()` to embed a native `InkCanvas` inside a Reactor tree if needed, but there is no declarative wrapper.

### AppNotification (Toast Notifications)
**WinUI:** System-level toast notifications via `AppNotificationManager`.
**Reactor Alternative:** None. Toast notifications are OS-level APIs outside the UI tree. Call the `AppNotificationManager` API directly from hook callbacks or event handlers.

### XamlDirect
**WinUI:** Low-level API for high-performance XAML object creation bypassing the type system.
**Reactor Alternative:** None. Reactor's reconciler already optimizes element creation; XamlDirect's perf benefits are subsumed by the virtual element tree.

---

## Windowing & System APIs

### AppWindow (Multi-Window)
**WinUI:** Create and manage multiple application windows.
**Reactor Alternative:** None. Reactor renders a single component tree per window. Multi-window apps must create separate `ReactorHost` instances per window using the hosting API directly.

### Clipboard APIs
**WinUI:** `DataPackage`, `Clipboard.SetContent()`, `Clipboard.GetContent()`.
**Reactor Alternative:** None. Clipboard is a platform API, not a UI element. Call `Windows.ApplicationModel.DataTransfer.Clipboard` directly from event handlers.

### FilePicker / FolderPicker
**WinUI:** System file/folder picker dialogs.
**Reactor Alternative:** None. Pickers are OS dialogs. Call `FileOpenPicker` / `FolderPicker` directly from button click handlers.

---

## XAML Concepts Replaced by C# Patterns

### DataTemplate / ControlTemplate
**WinUI:** Declarative XAML templates for data presentation and control styling.
**Reactor Alternative:** **C# functions.** Write a method like `Element RenderItem(MyModel item) => HStack(...)` and call it inline. Reactor's composable element model replaces templates entirely.

### x:Bind / {Binding}
**WinUI:** Declarative data binding expressions in XAML markup.
**Reactor Alternative:** **Hooks (`UseState`, `UseReducer`).** State is managed via hooks and flows through `Render()` naturally. No binding expressions needed — values are captured in closures.

### VisualStateManager / AdaptiveTriggers
**WinUI:** State-based visual changes and responsive layout triggers.
**Reactor Alternative:** **C# conditionals and hooks.** Use `if`/`switch` in `Render()` based on state or window size. Example: `var layout = width > 800 ? WideLayout() : NarrowLayout();`

### Custom Styles / ResourceDictionaries
**WinUI:** XAML resource dictionaries for sharing styles and theme resources.
**Reactor Alternative:** **`Theme` API + `.Set()` modifier.** Use `Theme.Accent`, `Theme.CardBackground`, etc. for theme-aware colors. For custom styling, use `.Set(el => { ... })` to configure native properties. Share styles as C# helper methods.

---

## Visual Effects (Partial Support)

### ConnectedAnimation
**WinUI:** Fluid page-to-page element transitions using `ConnectedAnimationService`.
**Reactor Alternative:** **Supported via `.ConnectedAnimation(key)`.** Put the same key on the element leaving the tree and the one entering it, in the same render; the reconciler snapshots the outgoing element and plays it into the incoming one. See the **Motion → Connected Animation** gallery page.

One caveat: the reconciler cannot know which of several keyed siblings you activated, so it snapshots them all. The unpicked ones expire on WinUI's own timeout (about a second), during which they briefly overlap the new view. Keep the keyed set to the rows you actually navigate between if that overlap is distracting.

### ThemeAnimation / Storyboard
**WinUI:** Declarative animations via `Storyboard`, `DoubleAnimation`, and theme transitions.
**Reactor Alternative:** **Limited.** Reactor supports implicit theme transitions on supported properties. For custom animations, use `.Set()` to access the composition layer directly. No declarative animation DSL exists.

### Reveal / Composition Effects
**WinUI:** Reveal highlight, composition visual effects, blur, shadows.
**Reactor Alternative:** **Partial via `.Set()`.** Use `.Set(el => { ... })` to access the element's visual and apply composition effects. `AcrylicBrush()` provides built-in acrylic support. Other effects require manual composition API usage.

---

## Summary Table

| Feature | Reactor Status | Alternative |
|---|---|---|
| InkCanvas / InkToolbar | ❌ Not available | Embed native via `.Set()` |
| AppNotification (Toast) | ❌ Not available | Call OS API directly |
| AppWindow (Multi-Window) | ❌ Not available | Multiple `ReactorHost` instances |
| Clipboard APIs | ❌ Not available | Call `Clipboard` API directly |
| FilePicker / FolderPicker | ❌ Not available | Call picker API directly |
| XamlDirect | ❌ Not needed | Reconciler handles perf |
| DataTemplate | ✅ Replaced | C# functions |
| x:Bind / {Binding} | ✅ Replaced | Hooks (`UseState`) |
| VisualStateManager | ✅ Replaced | C# conditionals |
| Styles / ResourceDictionary | ✅ Replaced | `Theme` API + `.Set()` |
| ThemeAnimation / Storyboard | ⚠️ Limited | Implicit transitions, `.Set()` |
| ConnectedAnimation | ✅ Supported | `.ConnectedAnimation(key)` |
| Reveal / Composition | ⚠️ Partial | `AcrylicBrush()`, `.Set()` |
