# XML doc backlog — spec 041 (REACTOR_DOC_001)

Generated from `src/Reactor/bin/x64/Debug/net10.0-windows10.0.22621.0/Reactor.xml`
on 2026-05-16 — scan of every `<member>` element with no `<summary>` child.

Total public surface: 3,445 members. Of those, **35** lack `<summary>`
(≈1.0%). The framework is in excellent XML-doc shape; the REACTOR_DOC_001
analyzer (Phase 1.8) lights up the residual gap and Phase 4 elevates it
to Error.

## Phase 1 fixes (this commit) — public Hooks surface

These five members are inside `src/Reactor/Hooks/` and feed the
Phase 1.7 reference generator. Fixed in the same commit as the analyzer.

- `M:Microsoft.UI.Reactor.Hooks.UseElementFocusExtensions.UseElementFocus(Microsoft.UI.Reactor.Core.Component,Microsoft.UI.Xaml.FocusState)`
- `M:Microsoft.UI.Reactor.Hooks.UseElementRefExtensions.UseElementRef\`\`1(Microsoft.UI.Reactor.Core.Component)`
- `M:Microsoft.UI.Reactor.Hooks.ComponentUseMemoCellsExtensions.UseMemoCells\`\`1(...)`
- `M:Microsoft.UI.Reactor.Hooks.ComponentUseMemoCellsExtensions.UseMemoCellsByKey\`\`2(...)`
- `M:Microsoft.UI.Reactor.Hooks.ComponentUseMemoCellsExtensions.UseMemoCellsByIndex\`\`1(...)`

## Phase 1 deferred xmldoc backlog

The 30 remaining gaps fall into four buckets, all deferred to Phase 4
when the analyzer severity rises to Error.

### Bucket A — JsonContext partials (auto-generated; suppress in Phase 4)

These are produced by the System.Text.Json source generator and authors
don't write them directly. We'll filter `JsonContext` partial-class
members from the analyzer's reach in Phase 4 — the right fix is a
`[GeneratedCode]`-attributed source-generator emit upstream, but the
short-term path is the analyzer skip.

- `M:Microsoft.UI.Reactor.Core.AccessibilityScanner.A11yJsonContext.#ctor`
- `M:Microsoft.UI.Reactor.Core.AccessibilityScanner.A11yJsonContext.#ctor(System.Text.Json.JsonSerializerOptions)`
- `M:Microsoft.UI.Reactor.Core.AccessibilityScanner.A11yJsonContext.GetTypeInfo(System.Type)`
- `M:Microsoft.UI.Reactor.Hosting.Devtools.DevtoolsJsonContext.#ctor` *(× 3 — same pattern)*
- `M:Microsoft.UI.Reactor.Hosting.Devtools.LockfileJsonContext.#ctor` *(× 3)*
- `M:Microsoft.UI.Reactor.Hosting.PreviewJsonContext.#ctor` *(× 3)*

### Bucket B — Constructors

Constructor `<summary>` is often redundant with the type-level summary.
Phase 4 either documents each one or extends the analyzer to skip
non-static constructors when the containing type carries a summary.

- `M:Microsoft.UI.Reactor.Controls.DataGridState\`1.#ctor(...)`
- `M:Microsoft.UI.Reactor.Navigation.NavigationCache.#ctor(...)`

### Bucket C — Modifier extensions

`Microsoft.UI.Reactor.ElementExtensions.AccentButton/SubtleButton/TextLink/BackgroundTransition`
overloads — one-line modifiers whose parent (`AccentButton`) has a
documented overload. The author's intent was clearly "same as the
canonical overload"; Phase 4 fills these in (small change set).

- `M:Microsoft.UI.Reactor.ElementExtensions.BackgroundTransition(Microsoft.UI.Reactor.Core.StackElement,System.Nullable{System.TimeSpan})`
- `M:Microsoft.UI.Reactor.ElementExtensions.AccentButton(Microsoft.UI.Reactor.Core.DropDownButtonElement)`
- `M:Microsoft.UI.Reactor.ElementExtensions.AccentButton(Microsoft.UI.Reactor.Core.SplitButtonElement)`
- `M:Microsoft.UI.Reactor.ElementExtensions.AccentButton(Microsoft.UI.Reactor.Core.ToggleSplitButtonElement)`
- `M:Microsoft.UI.Reactor.ElementExtensions.SubtleButton(Microsoft.UI.Reactor.Core.DropDownButtonElement)`
- `M:Microsoft.UI.Reactor.ElementExtensions.SubtleButton(Microsoft.UI.Reactor.Core.SplitButtonElement)`
- `M:Microsoft.UI.Reactor.ElementExtensions.SubtleButton(Microsoft.UI.Reactor.Core.ToggleSplitButtonElement)`
- `M:Microsoft.UI.Reactor.ElementExtensions.TextLink(Microsoft.UI.Reactor.Core.ButtonElement)`

### Bucket D — Misc

- `M:Microsoft.UI.Reactor.Hosting.Persistence.JsonFileStore.TryRead(...)`
- `M:Microsoft.UI.Reactor.Hosting.Persistence.JsonFileStore.Write(...)`
- `M:Microsoft.UI.Reactor.Hosting.Persistence.PackagedSettingsStore.TryRead(...)`
- `M:Microsoft.UI.Reactor.Hosting.Persistence.PackagedSettingsStore.Write(...)`
- `M:Microsoft.UI.Reactor.Hosting.Shell.TrayFlyoutHostWindow.Dispose`
- `M:Microsoft.UI.Reactor.ReactorTrayIcon.Dispose`
- `M:Microsoft.UI.Reactor.WindowKey.ToString`
- `M:Microsoft.UI.Reactor.Input.ElementRef\`1.ToString`

`Dispose` / `ToString` overrides typically pick up base-class doc; the
analyzer's override-skip path already handles those without breaking
the build. Listed here so the Phase 4 audit doesn't miss them when the
severity rises.
