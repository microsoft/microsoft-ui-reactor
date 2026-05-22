# Reactor Devtools UX — In-App Dev Menu & App State Guidance

## Status

**Draft** — 2026-04-22.

---

## Problem Statement

Microsoft.UI.Reactor (Reactor) today has a strong external devtools story — the MCP server, VS Code
preview panel, component switching, property inspection (#63) — all driven by
`--devtools run` launching the process in a preview harness. What it does not
have is a story for **devtools UX that lives inside a running app**:

- A launcher a developer can drop into their titlebar (or anywhere) to expose
  dev-only commands to the user running the app.
- A way to gate pieces of UX so they appear only in developer sessions and
  cost nothing at runtime in retail sessions.
- A recommended pattern for "global-ish" app state (dark mode, locale, dev
  flags) that's simple and doesn't invent a new primitive.

Today a developer who wants any of these has to hand-roll all three and
repeat the "is devtools on?" check at every call site. This spec adds:

1. `UseDevtools()` — a one-bit ambient signal that's the AND of a build-time
   opt-in (`Run(devtools: true)`) and a session opt-in (`--devtools app` on
   the command line).
2. `DevtoolsMenu(...)` — a factory that returns `Empty()` when devtools is
   off, so its subtree is never constructed in retail sessions.
3. `Observable<T>` — a tiny INPC-backed cell so the state for dev flags (and
   any other app-global toggles) can be declared in one line without
   hand-writing `INotifyPropertyChanged`.

No new state primitive (no `Ambient<T>`, no `AppState<T>`). No attributes.
No reflection. Subtree state stays on the existing `Context<T>`; global
state is just a plain object + `ctx.UseObservable(obj)`, which Reactor
already has.

---

## Non-Goals

- **Not** a new global-state primitive. The canonical pattern remains
  `Context<T>` for subtree scope and a plain INPC object (often a
  `static readonly` singleton) + `ctx.UseObservable` for process-global
  scope. `Observable<T>` is a one-line INPC wrapper, nothing more.
- **Not** a per-window state system. Covered by a separate future spec.
- **Not** reflection-driven menu generation. Developers write their own
  menu items — Reactor provides no `[DevFlag]` attribute, no registry,
  no auto-discovery.
- **Not** a replacement for the external devtools flow. The MCP server and
  VS Code panel continue to work as they do today; `UseDevtools()` is
  orthogonal and can be used alongside preview mode.

---

## Design

### The AND gate

`UseDevtools()` returns a constant `bool` for the session. It is `true`
iff **both**:

1. `ReactorApp.Run(devtools: true, ...)` was passed by the developer — a
   build-time capability gate. Typical pattern is `devtools: true` only in
   DEBUG builds, or always-on in internal tooling.
2. The process was launched with `--devtools app` (new subverb) or
   `--devtools run` (preview mode) on the command line — a session-scoped
   opt-in by the person running the app.

The AND ensures that shipping `devtools: true` in a retail binary does **not**
leak the dev UI unless the end user explicitly asks for it.

### CLI: new `app` subverb

A new subverb is added to `DevtoolsCliParser`:

```
myapp.exe --devtools app
```

Runs the app normally (same mount path as `myapp.exe`) with
`ReactorApp.DevtoolsEnabled = true` for the session. Distinct from
`--devtools run`, which mounts a single component in preview mode with
MCP + VS Code integration.

Why a new subverb and not a bool flag or overloading bare `--devtools`:

- Keeps the `--devtools <verb>` taxonomy consistent with `list`, `run`,
  `screenshot`, `tree`.
- Bare `--devtools` continues to default to `run` (preview mode) — zero
  breaking change to existing workflow.
- Explicit verb is self-documenting in launch scripts and docs.

### `UseDevtools()`

```csharp
// Shipped in namespace Microsoft.UI.Reactor.Hooks —
// consumers need `using Microsoft.UI.Reactor.Hooks;` to call it.
namespace Microsoft.UI.Reactor.Hooks;

public static class UseDevtoolsExtensions
{
    public static bool UseDevtools(this RenderContext ctx) =>
        ReactorApp.DevtoolsEnabled;
}
```

No subscription, no hook slot consumed. The value is frozen at startup; a
component that reads it does not re-render on any state change (because
the state never changes). `ReactorApp.DevtoolsEnabled` is also exposed as
a public static for non-component code paths (CLI integration, ad-hoc
diagnostics).

### `DevtoolsMenu` factory

```csharp
// Lives on the Microsoft.UI.Reactor.Factories partial class so it's
// available via `using static Microsoft.UI.Reactor.Factories;`.
public static partial class Factories
{
    public static Element DevtoolsMenu(
        Func<IEnumerable<MenuFlyoutItemBase>> items,
        string glyph = "⚡",
        string toolTip = "Devtools",
        string? automationId = null)
    {
        if (!ReactorApp.DevtoolsEnabled) return Empty();

        var materialized = items?.Invoke()?.ToArray()
            ?? Array.Empty<MenuFlyoutItemBase>();

        var trigger = Button(glyph)
            .Foreground("#F59E0B")
            .Background("#00000000")
            .WithBorder("#00000000", 0)
            .Padding(8, 4)
            .FontSize(16)
            .ToolTip(toolTip)
            .AutomationName(toolTip);   // glyph-only content → explicit a11y name

        if (automationId is not null)
            trigger = trigger.AutomationId(automationId);

        return MenuFlyout(trigger, materialized);
    }
}
```

Key properties: when `ReactorApp.DevtoolsEnabled` is `false`, the factory
returns `Empty()` and the `items` lambda is **not invoked**. Any
`Observable<T>` reads or element constructions inside the lambda are not
executed. The reconciler skips the subtree entirely. Retail cost per
render ≈ one bool check + one `Empty` return. The on-state uses a
`MenuFlyout`-wrapped `Button` (not `DropDownButton`) so there is no
chevron — a bare ⚡ glyph, intentionally distinct from normal app chrome.

### `Observable<T>`

```csharp
public sealed class Observable<T> : INotifyPropertyChanged
{
    T _value;
    public Observable() : this(default!) { }
    public Observable(T initial) => _value = initial;

    public T Value
    {
        get => _value;
        set
        {
            if (EqualityComparer<T>.Default.Equals(_value, value)) return;
            _value = value;
            PropertyChanged?.Invoke(this, new(nameof(Value)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public override string ToString() => _value?.ToString() ?? "";
    public static implicit operator T(Observable<T> o) => o._value;
}
```

Purpose: let developers write

```csharp
public static readonly Observable<bool> DebugUI = new(false);
```

instead of a 10-line INPC class, while still working with Reactor's
existing `ctx.UseObservable(source)` hook. No framework-side registration;
a developer who prefers hand-rolled INPC or a `record class`-based store
is free to use that instead.

---

## End-to-end example

```csharp
// 1. Flags — one line per flag, static, reachable from anywhere.
public static class AppFlags
{
    public static readonly Observable<bool> DebugUI   = new(false);
    public static readonly Observable<bool> SlowMode  = new(false);
    public static readonly Observable<bool> ForceDark = new(false);
}

// 2. Menu placement — typically in a titlebar component.
class TitleBar : Component
{
    public override Element Render()
    {
        // Subscribe so toggle state renders correctly inside the menu.
        ctx.UseObservable(AppFlags.DebugUI);
        ctx.UseObservable(AppFlags.SlowMode);
        ctx.UseObservable(AppFlags.ForceDark);

        return HStack(
            Text("My App"), Spacer(),
            DevtoolsMenu(() => new MenuFlyoutItemBase[]
            {
                ToggleMenuItem("Debug UI",
                    AppFlags.DebugUI.Value,
                    v => AppFlags.DebugUI.Value = v),
                ToggleMenuItem("Slow mode",
                    AppFlags.SlowMode.Value,
                    v => AppFlags.SlowMode.Value = v),
                ToggleMenuItem("Force dark",
                    AppFlags.ForceDark.Value,
                    v => AppFlags.ForceDark.Value = v),
                MenuSeparator(),
                MenuItem("Clear cache", () => CacheService.Clear()),
            })
        );
    }
}

// 3. Consume anywhere — one line per flag read.
class SomeScreen : Component
{
    public override Element Render()
    {
        var debugUI = ctx.UseObservable(AppFlags.DebugUI).Value;
        return VStack(
            MainContent(),
            debugUI ? DebugOverlay() : null   // DebugOverlay() not called when false
        );
    }
}
```

---

## Cost model

| Scenario | Per-render cost |
|---|---|
| Retail (devtools off): `DevtoolsMenu` present | 1 static bool read + 1 `Empty` return |
| Retail: `debugUI ? X : null` at call site | 1 instance field read + 1 branch |
| Dev session (devtools on): menu rendered | Same as any other small flyout |
| Flag toggle (dev session) | PropertyChanged → subscribing components rerender |

The "compile away at runtime" guarantee is standard C# ternary semantics:
`false ? a() : null` does not evaluate `a()`. `DebugOverlay()` is never
constructed when the flag is off. The only overhead in retail is the bool
check itself.

---

## Why no new state primitive

Earlier iterations of this design proposed an `Ambient<T>` / `AppState<T>`
primitive with Global/Window/Subtree scope factories. It was withdrawn
after comparing against what developers can already express with existing
Reactor hooks:

| Need | Existing mechanism |
|---|---|
| Subtree / positional state | `Context<T>` + `.Provide()` + `UseContext()` |
| Process-global mutable state | `static readonly` INPC object + `ctx.UseObservable(obj)` |
| Change notification | `INotifyPropertyChanged` + existing `UseObservable` / `UseObservableTree` |
| Discoverable without prop-drilling | `static` field is the discovery mechanism |

The only thing plain INPC + a static field was missing was an ergonomic
one-line declaration for single-value cells. That is what `Observable<T>`
provides — nothing more, nothing less. Adding a scoped primitive on top
of this would be duplicating infrastructure developers already have.

---

## Rollout

1. Add `DevtoolsSubverb.App` + parser support. Existing behaviors unchanged.
2. Add `ReactorApp.DevtoolsEnabled` static. Set to `true` from
   `TryRunDevtools` when `Subverb` is `App` or `Run` and the Run bool is set.
   When `Subverb == App`, the method sets the flag and returns `false` so
   the app falls through to the normal render loop.
3. Add `UseDevtools()` extension, `Observable<T>` helper, and
   `Factories.DevtoolsMenu(...)` factory.
4. Unit tests for the AND gate, parser, Observable semantics, menu
   disabled-path early-out.
5. Guide page template.

No deprecations. No migration. Features are additive.
