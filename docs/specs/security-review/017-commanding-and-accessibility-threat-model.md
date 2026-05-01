# Chunk 17 — Commanding & Accessibility — Threat Model

**Status:** Phase 2, deep review
**Reviewer:** security review
**Commit reviewed:** `4623474cac6e5f2b64df2501636fd5f8491a1bc3` (HEAD of `main`)
**Companion docs:** `000-chunking-and-threat-model.md`

---

## 1. Scope

The chunk-as-defined in `000-chunking-and-threat-model.md §7 Chunk 17` covers the
runtime commanding bundle (label / icon / shortcut / action) and the
accessibility-scanner / `SemanticPanel` surface that crosses into UI Automation
(UIA), which is reachable by other automation processes on the same desktop.

| File | Lines | Role |
|---|---|---|
| `src/Reactor/Core/Command.cs` | 84 | Immutable `Command` / `Command<T>` record bundling label, icon, shortcut, action, description, access key. |
| `src/Reactor/Core/CommandBindings.cs` | 69 | Internal helper that wires a `Command` onto any `Control` — sets `IsEnabled`, `ToolTipService.ToolTip`, **`AutomationProperties.HelpText`**, `AccessKey`, `KeyboardAccelerators`. Owns `_commandAccelerators` `ConditionalWeakTable`. |
| `src/Reactor/Core/CommandInterop.cs` | 60 | `CommandInterop.FromCommand(ICommand,…)` — one-way bridge from MVVM/CommunityToolkit `ICommand` to Reactor `Command`. |
| `src/Reactor/Core/StandardCommand.cs` | 122 | 16 standard commands (Cut/Copy/Paste/Undo/Redo/Delete/SelectAll/Save/Open/Close/Share/Play/Pause/Stop/Forward/Backward) with pre-baked accelerators. Pure factory. |
| `src/Reactor/Core/AccessibilityScanner.cs` | 1102 | DEBUG-only post-reconciliation scanner: walks the virtual element tree, emits `A11yDiagnostic` records, can `ExportJson(...)` to disk. |
| `src/Reactor/Accessibility/SemanticPanel.cs` | 191 | WinUI `Panel` + `FrameworkElementAutomationPeer` that exposes `IRangeValueProvider` and `IValueProvider` for composite Reactor components (e.g. `StarRating`). |

**Total:** 1 628 lines.

The scanner is the largest file (1 102 lines) and has by far the largest review surface.

The chart-accessibility namespace `src/Reactor/Charting/Accessibility/**` (10 files,
~2 105 lines including `ChartAutomationPeer.cs`, `ChartLiveAnnouncer.cs`,
`ChartKeyboardNavigator.cs`, `ChartPalette.cs`, etc.) was **not** part of the
chunk-17 scope as written. The scanner references it (palette/colour-blind
contrast checks) but the implementation belongs to Chunk 21 (Charting/D3 port).
See §8 Out-of-scope referrals.

The `cmd.Description → AppBarButton / MenuFlyoutItem` codepath at
`Reconciler.Mount.cs:1836-1840`, `1891-1895`, and `Reconciler.Update.cs:2826-2830`
mirrors the same pattern as `CommandBindings.ApplyButtonBaseCommon` and was
treated as in-scope for chunk 17 because the question ("does `Command.Description`
end up in UIA `HelpText`?") is identical.

---

## 2. Data-flow diagram

```
Author code
   │
   ├── new Command { Label, Description, Accelerator, AccessKey, Icon, Execute, ExecuteAsync, CanExecute }
   │       │
   │       └── Command.cs (immutable record, no validation)
   │
   ├── StandardCommand.Save(action) ────────► fixed Label/Icon/Accelerator
   │
   └── CommandInterop.FromCommand(ICommand) ─► wraps ICommand.Execute / CanExecute
                                                (CanExecute snapshot at call-time only)
                              │
                              ▼
            DSL factory (Dsl.cs:82-149): Button(cmd), HyperlinkButton(cmd), …
                              │
                              ▼
   ButtonElement / HyperlinkButtonElement / SplitButtonElement / ToggleButtonElement / …
   with Setters = [b => CommandBindings.ApplyButtonBaseCommon(b, command)]
                              │
                              ▼
   Reconciler.Mount → MountControl → ApplyModifiers → ApplySetters
                              │
                              ▼
            CommandBindings.ApplyButtonBaseCommon(Control btn, Command cmd):
              • btn.IsEnabled = cmd.IsEnabled                          (UIA: IsEnabled property)
              • ToolTipService.SetToolTip(btn, cmd.Description)        (visible tooltip)
              • AutomationProperties.SetHelpText(btn, cmd.Description) ◄── leaks to any UIA client
              • btn.AccessKey = cmd.AccessKey                          (UIA: AccessKey)
              • btn.KeyboardAccelerators.Add(KeyboardAccelerator{…})    (system accel registration)
                              │
                              ▼
   ApplyDefaultAutomationName(fe, ResolveCaptionForElement(el))         (Reconciler.cs:1126)
       └── If author did not set AutomationName, copies the visible caption
           (Button.Label, TextBlock.Content, … or for TextField, .Header ?? .Placeholder)
           into AutomationProperties.Name (truncated to 100 chars).
                              │
                              ▼
   WinUI Control with UIA peer ──────────► UI Automation pipeline (UIA host process)
                                              │
                              ┌───────────────┼────────────────────┐
                              ▼               ▼                    ▼
                    Narrator / 3rd-party    Hostile local         Reactor devtools
                    AT (e.g. JAWS, NVDA)    process w/ AT scope   (Chunk 02)


SemanticPanel (Reactor/Accessibility/SemanticPanel.cs):
   Reactor element tree ── SemanticElement ─► MountSemantic ─► SemanticPanel
                                                  │
                                                  ▼
              SemanticPanel (DependencyObject):
                  SemanticRole, SemanticValue, RangeMin/Max/Value, IsReadOnly
                                                  │
                  OnCreateAutomationPeer ─► SemanticPanelAutomationPeer
                       implements IRangeValueProvider, IValueProvider
                                                  │
                                                  ▼
              UIA: any client may call SetValue(double) / SetValue(string)
                  ── gated by IsReadOnly (defaults to true)


AccessibilityScanner (DEBUG-only, ReactorHostControl.EnableAccessibilityDiagnostics):
   Element tree root
        │
        ▼
   AccessibilityScanner.Scan(root)
        ├── recursive Walk(el, ctx, findings)
        │     ├── Per-element checks (CheckIconButton, CheckImage, CheckFormField,
        │     │     CheckHeadingStyle, CheckConcreteBrushOnInteractive, CheckChartRules)
        │     ├── Data collection (TabIndex, LabeledBy, AutomationId, Landmark)
        │     └── GetChildren(el) — pattern-matches ~25 container element types
        │
        └── post-walk: CheckNoMainLandmark, CheckTabIndexGaps, CheckUnresolvedLabeledBy
        │
        ▼
   List<A11yDiagnostic>
        │
        ▼
   ExportJson(diagnostics, filePath) ── writes JSON report to attacker-supplied path
        ├── Path.GetDirectoryName(filePath)
        ├── Directory.CreateDirectory(dir)        (no validation)
        └── File.WriteAllText(filePath, json)     (overwrites silently)
```

---

## 3. Trust boundaries crossed

| Boundary | Direction | Trust assumption | Notes |
|---|---|---|---|
| Reactor process ↔ UI Automation host (`uiautomationcore.dll`) | OUT | UIA peers are **broadcast**: any local process holding `UIAccess` or running on the user's interactive desktop can read every property via `IUIAutomation::GetRootElement` and walk down. | This is the chunk's headline boundary. Anything Reactor places in `Name`, `HelpText`, `LocalizedControlType`, or any `IValueProvider.Value` is visible to a hostile local AT-impersonating process. |
| Reactor process ↔ UIA invoker (any local automation client) | IN | `IInvokeProvider`, `IRangeValueProvider.SetValue`, `IValueProvider.SetValue`, `IToggleProvider.Toggle`, `ISelectionItemProvider.Select` can be called by any local UIA client. | `SemanticPanelAutomationPeer.SetValue(double/string)` is gated on `IsReadOnly` (default `true`). For ordinary `ButtonElement → WinUI.Button`, the invoke path executes `cmd.Execute` / `cmd.ExecuteAsync` with no consent prompt. |
| Reactor element tree ↔ AccessibilityScanner | IN | The scanner trusts the in-process `Element` tree as well-formed. | But: scanner is recursive with no depth cap and trusts that the tree has no cycles (see Findings §6). |
| AccessibilityScanner ↔ filesystem (`ExportJson`) | OUT | `filePath` is taken from the developer / debugging UI; treated as trusted in DEBUG. | No validation; `Directory.CreateDirectory` will silently create attacker-chosen paths if the developer pastes one in. Low priority — DEBUG-only. |
| Reactor Command ↔ `System.Windows.Input.ICommand` (CommunityToolkit / MVVM) | IN | `ICommand` instances are author-supplied; `command.CanExecute(parameter)` is invoked synchronously. | A misbehaving `ICommand.CanExecute` that throws would propagate out of `CommandInterop.FromCommand`. |

---

## 4. Asset inventory

**Confidentiality assets:**
- A1. Plain-text `Command.Description` strings — frequently localised tooltips, but authors sometimes embed account names, file paths, or licensing-related text. Every `cmd.Description` is mirrored into `AutomationProperties.HelpText` (`CommandBindings.cs:33`, `Reconciler.Mount.cs:1839`, `Reconciler.Mount.cs:1894`, `Reconciler.Update.cs:2829`).
- A2. Visible captions copied into `AutomationProperties.Name` by the default-name fallback (`Reconciler.cs:1133`, `Reconciler.cs:1152`). For `TextFieldElement`, this falls back to `.Placeholder` when there's no `Header` (`Reconciler.cs:1180`) — a placeholder may say "Enter API key" or similar.
- A3. `SemanticPanelAutomationPeer.Value` (`SemanticPanel.cs:183`) — exposed as plain-string UIA `Value` to any AT client.
- A4. The `A11yDiagnostic` JSON export — by design contains `ChildText`, `SiblingTexts`, `Header`, `PlaceholderText`, the first 40-character text excerpt of any heading-styled `TextBlock` (`AccessibilityScanner.cs:382`), and component-type names. Written with `File.WriteAllText` to a developer-supplied path.

**Integrity / capability assets:**
- A5. The execution capability of any `Command` whose Reactor surface is reachable via UIA `Invoke` — Save, Delete, Cut/Copy/Paste, Share, custom user actions. UIA `Invoke` requires no privilege beyond "running on the same desktop."
- A6. `SemanticPanelAutomationPeer.SetValue` (read-write when `IsReadOnly == false`) — direct attacker-controlled writes to a domain-meaningful range value.
- A7. Keyboard accelerators registered via `Control.KeyboardAccelerators` — affects global accelerator routing for the window.

**Availability assets:**
- A8. The accessibility scanner's recursion stack — affects developer experience but only in DEBUG.

---

## 5. STRIDE table

| # | Cat | Threat | Attacker model | Impact | Likelihood | Mitigation today | Recommendation |
|---|---|---|---|---|---|---|---|
| T1 | I (info disclosure) | `cmd.Description` for sensitive commands ("Connect to vault https://…", "Sign as alice@example.com") leaks via `AutomationProperties.HelpText` to any local UIA client. | Local malware running on the user's desktop, or an AT impersonator. | Medium — typical Reactor authors put short, non-secret strings here, but the surface is undocumented as "AT-readable." | Low–medium | None. There is no "private" / "internal" tooltip flag. Help text mirrors description verbatim. | Document that `Command.Description` is broadcast to UIA. Add a `cmd with { Description = ... }` semantic note. Optionally introduce `.HiddenFromAutomation()` / `AccessibilityHidden()` for tooltips that should not cross the UIA boundary. |
| T2 | I | `TextFieldElement.Placeholder` falls into `AutomationProperties.Name` via `ResolveCaptionForElement` (`Reconciler.cs:1180`). A placeholder like "Enter password" or "Paste recovery phrase" then becomes the UIA `Name`. | Same as T1. | Low — placeholders shouldn't carry secrets, but the flow is non-obvious. | Medium (defaults are silent) | The default-AutomationName fallback exists explicitly so screen-readers don't see an empty string. Truncates to 100 chars (`Reconciler.cs:1132`). | At minimum, document the precedence chain. Consider not deriving Name from Placeholder for `TextFieldElement` when `IsPasswordEnabled` analogues are expected. |
| T3 | I | `PasswordBoxElement` exposes its Password indirectly: the WinUI `PasswordBox` itself does not expose Password through its UIA peer (WinUI handles this), and `ResolveCaptionForElement` has **no** `PasswordBoxElement` case (`Reconciler.cs:1167-1182`), so no plaintext caption is mirrored. | n/a | n/a (currently safe) | n/a | Implicit safety: `PasswordBoxElement` is absent from `ResolveCaptionForElement`. | Add a defensive comment in `ResolveCaptionForElement` stating that `PasswordBoxElement` is intentionally omitted; otherwise a future contributor adding a "fall back to Password" line would silently leak passwords into UIA `Name`. |
| T4 | I | `AccessibilityScanner` JSON report (`A11yContext.ChildText`, `SiblingTexts`, `Header`, `PlaceholderText`) writes user-visible string content to a file at a developer-supplied path. | Local user with disk-read access to the export directory; CI logs that capture the artifact. | Low — DEBUG-only. | Low | None — `ExportJson` never sanitises content. | Document that `EnableAccessibilityDiagnostics` produces files containing every label/heading/placeholder in the tree. Consider redacting `PasswordBoxElement.PlaceholderText` and any element under an `Accessibility.AccessibilityHidden()` modifier. |
| T5 | T (tampering) / E (EoP) | Any local UIA client can `IInvokeProvider.Invoke` a `Button(cmd)` (or `HyperlinkButton`, `SplitButton`, `RepeatButton`) and fire `cmd.Execute` / `cmd.ExecuteAsync` without user consent. This includes destructive standard commands — `StandardCommand.Delete`, `Save`, `Cut`, `Paste`, `Share`. | Local malware, or a CI's UIA-driven test harness mishitting a real production binary. | High impact for destructive commands; likelihood depends on who is on the desktop. | Medium | `Command.IsEnabled` (= `CanExecute && !IsExecuting`) is honoured at peer level: `Control.IsEnabled = false` makes the peer not invokable. There is no per-command "requires user gesture" guard. | Recommend an opt-in `Command.RequiresUserGesture = true` flag that, when set, gates `Execute` on `WindowActivationState.PointerActivated` or similar evidence of a real input. At minimum, document the threat for destructive standard commands. |
| T6 | T / E | `SemanticPanelAutomationPeer.SetValue(double)` / `SetValue(string)` writes attacker-supplied values into a `SemanticPanel.RangeValue` / `SemanticValue` when `IsReadOnly` is `false`. (`SemanticPanel.cs:176-190`) | Same as T5. | Depends on what authors wire into the `SemanticElement` semantic value — could be a slider value, a rating count, etc. | Low — most uses are display-only. | Default `IsReadOnly = true` (`SemanticPanel.cs:50`); writes are gated. | Already mitigated by default. Document that `Reactor.Semantics(...)` becomes UIA-writable as soon as the author sets `IsReadOnly = false` and that the receiving code must validate the incoming value. |
| T7 | D (DoS) | `AccessibilityScanner.Walk` is recursive with no max-depth guard. A pathological tree (deep `StackElement` nesting from a runaway component) blows the stack. | A buggy app build, not a malicious actor — but still DEBUG-only DoS that crashes the host. | Low — debug-only. | Low | None. | Add a depth cap (e.g. 1024) and a finding `A11Y_DEPTH_EXCEEDED` rather than throwing `StackOverflowException`. |
| T8 | D | `AccessibilityScanner.GetChildren` returns `IEnumerable<Element?>` and re-iterates in `BuildContext` (`AccessibilityScanner.cs:1011, 1033`) — for each element, children are enumerated twice and `CheckChartPalette*` runs an O(n²) loop over palette colours. Very large palettes (custom) → quadratic. | n/a | Low — DEBUG only and capped by tree size in practice. | Low | None. | Cap palette size before O(n²); acceptable to leave today. |
| T9 | I | `ExportJson` will silently overwrite `filePath` and create any parent directory — `Directory.CreateDirectory(dir)` (`AccessibilityScanner.cs:150`). A typo'd path can clobber files. | Developer error, not an attacker. | Low | None. | DEBUG-only; document in API summary. |
| T10 | T | `CommandBindings.ApplyButtonBaseCommon` never *clears* `AutomationProperties.HelpText` when a re-rendered `Command` has `Description == null` after previously having a value. (`CommandBindings.cs:30`). The stale help text remains visible to AT. | n/a (correctness) | Low — confusing AT, possible info leak of *previous* command description. | Medium | The accelerator-add path explicitly removes the prior accel via `_commandAccelerators.TryGetValue(...)` (`CommandBindings.cs:40-44`); description has no parallel cleanup. | Add an `else AutomationProperties.SetHelpText(btn, null)` branch and similarly clear `ToolTipService.ToolTip` when `Description` transitions to null. Apply the same fix to `Reconciler.Update.cs:2826-2830` (AppBarButton update path). |
| T11 | T | `_commandAccelerators` is a `ConditionalWeakTable<Control, KeyboardAccelerator>` (`CommandBindings.cs:57`). When a `Command` is rebound to a *new* control instance (rebound via `with { }` after a remount), the old control's accelerator stays in `KeyboardAccelerators` until the control is collected — meaning an old gesture may briefly fire the new command on the old control. | Reconciler edge case, not adversarial. | Low | Works correctly under normal reconcile because the same control flows through. | Acceptable; document the assumption. |
| T12 | I | `CommandInterop.FromCommand` calls `command.CanExecute(parameter)` *once* at construction time (`CommandInterop.cs:29`). If `ICommand.CanExecute` queries account state, that state ends up frozen in the `Command.CanExecute` static bool. UIA clients reading the disabled state may see stale truth. | n/a (correctness) | Low — author confusion; no direct security impact. | n/a | Documented in the XML doc comment (`CommandInterop.cs:14-16`). | Acceptable. |
| T13 | E | `CommandBindings.Invoke` does `_ = cmd.ExecuteAsync()` (`CommandBindings.cs:67`), discarding the returned `Task`. Any exception thrown from `ExecuteAsync` becomes an unobserved task exception. If `ExecuteAsync` performs a privileged operation that fails, the failure is silently swallowed (UIA still observes "completed"). | Reliability, not security per se; but a hostile UIA invoker could repeatedly fire an `ExecuteAsync` that always faults to amplify side-channel timing. | Low | Low | None. | The `RenderContext.UseCommand` wrapper handles this correctly with re-entrance guards (`Command.cs:19`); the bare-Command path through `CommandBindings.Invoke` does not. Recommend at minimum a `TaskScheduler.UnobservedTaskException`-friendly `.ContinueWith` to surface the fault. |
| T14 | I | `StandardCommand.*` factories (`StandardCommand.cs:17-122`) always set hard-coded English `Label = "Cut"` etc. The XML doc says "Override with `with { Label = intl.Message(keys.Cut) }`" — but if the developer forgets, an English label leaks into UIA `Name` even on non-English systems. | n/a (correctness / localisation) | Low — visible to AT only. | n/a | Documented. | Acceptable. Could ship a localised variant or surface a one-line analyzer warning if `Label = "Cut"` reaches a Reactor element in a localised app. |
| T15 | T | `ApplyButtonBaseCommon` calls `btn.AccessKey = cmd.AccessKey` (`CommandBindings.cs:35`) but never clears the prior AccessKey on update. A `Command` change from `AccessKey = "X"` to `AccessKey = null` leaves the old `"X"` registered. | Author confusion; possible accelerator-fight with another control. | Low | Low | None. | Mirror the accelerator cleanup pattern: store prior AccessKey in a sibling `ConditionalWeakTable` and clear when the new command has no AccessKey. |
| T16 | E | The `ExtractElementCaption` / `ResolveCaptionForElement` chain (`Reconciler.cs:1167-1182`) does not validate caption content. A `Button.Label` can contain RTL override codepoints (U+202E etc.) and these will land in `AutomationProperties.Name`. AT clients that announce the name verbatim show the user a misleading or homograph-attacked label. | A hostile data source feeding into a `Button.Label` (e.g. a chat message that becomes a button) could mislead the user and AT both. | Medium for chat-style apps. | Low–medium | None. | Strip Unicode bidi-override codepoints (U+202A–U+202E, U+2066–U+2069) from captions copied into Name, or document the threat. Pairs with Chunk 11 (RTL override codepoint handling). |
| T17 | I | `A11yDiagnostic.ComponentType` is `ctx.CurrentComponent` which is set from `ComponentElement.ComponentType.Name` (`AccessibilityScanner.cs:1006-1007`) — this leaks internal class names into the JSON report. | Information disclosure if the JSON is shared (e.g. attached to a GitHub issue). | Low | Low | None — purely DEBUG output. | Acceptable; document. |

---

## 6. Findings

Ordered by severity. Severity rubric:
- **High** — direct security regression in a non-DEBUG path with realistic attacker.
- **Medium** — info disclosure or correctness bug in a default codepath.
- **Low** — DEBUG-only, future-hazard, or documentation gap.

---

### F1 — `Command.Description` mirrors verbatim into UIA `HelpText` with no opt-out (Medium)

**File:** `src/Reactor/Core/CommandBindings.cs:30-34`
**Also:** `src/Reactor/Core/Reconciler.Mount.cs:1836-1840`, `1891-1895`; `src/Reactor/Core/Reconciler.Update.cs:2826-2830`.

```csharp
if (cmd.Description is not null)
{
    ToolTipService.SetToolTip(btn, cmd.Description);
    Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(btn, cmd.Description);
}
```

The trust model in `000-chunking-and-threat-model.md §2` flags UIA as a multi-reader
boundary, but no chunk-17 file warns the author. There is no
`Command.HelpText` vs. `Command.Description` split, and no
`AccessibilityHidden` / `private:` flag for tooltips. Authors who put session
state in tooltips ("Connected as alice@example.com") leak it to any AT client.

**Recommendation:** document the broadcast property of `Description` in
`Command.cs`'s XML doc, and consider adding a `Command.PrivateTooltip` mode
(visible tooltip, no `HelpText`) — or vice versa. At minimum, add a comment in
`CommandBindings.cs` noting that this is a deliberate UIA-leakage point.

---

### F2 — `ApplyButtonBaseCommon` does not clear stale `HelpText` / `AccessKey` (Medium)

**File:** `src/Reactor/Core/CommandBindings.cs:30-44`

The accelerator-add path correctly removes the prior accelerator before adding the new one (`_commandAccelerators.TryGetValue(btn, out var prior)`, `KeyboardAccelerators.Remove(prior)` — lines 40-44). The `Description → HelpText` and `AccessKey` paths have no parallel cleanup:

```csharp
if (cmd.Description is not null)               // ← no else: stale HelpText survives
{
    ToolTipService.SetToolTip(btn, cmd.Description);
    AutomationProperties.SetHelpText(btn, cmd.Description);
}
if (cmd.AccessKey is not null) btn.AccessKey = cmd.AccessKey;   // ← no else
```

A `Command` that updates from `Description = "Sign as alice@example.com"` to
`Description = null` (e.g. on logout) leaves the previous description visible
in `AutomationProperties.HelpText` and as a tooltip. Same shape on the
AppBarButton update path (`Reconciler.Update.cs:2826-2830`).

**Recommendation:**

```csharp
if (cmd.Description is not null) {
    ToolTipService.SetToolTip(btn, cmd.Description);
    AutomationProperties.SetHelpText(btn, cmd.Description);
} else {
    ToolTipService.SetToolTip(btn, null);
    btn.ClearValue(AutomationProperties.HelpTextProperty);
}
btn.AccessKey = cmd.AccessKey ?? string.Empty;
```

(`AccessKey` defaults to `""` on `UIElement`, so assigning `""` correctly clears.)

---

### F3 — UIA `Invoke` fires `Command.Execute` for destructive standard commands without consent gate (Medium)

**File:** `src/Reactor/Core/StandardCommand.cs` (entire file) and the WinUI Button peer's default `IInvokeProvider`.

Once `Button(StandardCommand.Delete(() => DeleteCurrent()))` is on a window,
any local process able to walk UIA can locate the button by `Name="Delete"`
and call `IInvokeProvider.Invoke()` on it. WinUI's default
`ButtonAutomationPeer` invokes the click handler without any pointer-input
or user-presence check. Reactor offers no `RequiresUserGesture` flag.

This is partially a WinUI concern, but Reactor amplifies it:
- `StandardCommand.Save / Delete / Share` are pre-baked, well-known names that
  a UIA driver can target by string match.
- `cmd.IsEnabled` is the only gate; a Save command that is always-enabled is
  always-invokable.

**Recommendation:** add an opt-in `Command.RequiresUserGesture` (default
`false` for compatibility) that, when set, suppresses execution unless
`InputManager.LastInputDeviceType` indicates a real input within ~500 ms.
Apply by default to `StandardCommand.Delete` and any future financial /
destructive helpers. At minimum, document the threat in `Command.cs` so
authors know to add their own consent prompt.

---

### F4 — Default-AutomationName fallback derives `Name` from `TextFieldElement.Placeholder` (Medium)

**File:** `src/Reactor/Core/Reconciler.cs:1167-1182`

```csharp
TextFieldElement tfe => tfe.Header as string ?? tfe.Placeholder,
```

When a `TextField` has no `Header`, its placeholder becomes the UIA `Name`.
A placeholder string like "Enter API key" or "Paste recovery phrase" is
broadcast to AT, and to anything else with UIA read access.

This is an acceptable default for accessibility (better than no name), but
it surprises authors who wrote the placeholder as a *hint* rather than a
*label*. There is no explicit per-element opt-out beyond setting
`AutomationName` to `""` (which won't trip the `string.IsNullOrEmpty` check
at `Reconciler.cs:1131`).

**Recommendation:** document the precedence chain in `TextFieldElement`'s
XML doc, and consider stopping the placeholder fallback when
`PasswordRevealMode`-equivalent fields are detected. Today
`PasswordBoxElement` is **correctly absent** from `ResolveCaptionForElement`
(see F5), but a `TextFieldElement` masquerading as a password input would
still leak.

---

### F5 — `PasswordBoxElement` has no caption fallback — by accident, not by design (Low — defensive comment)

**File:** `src/Reactor/Core/Reconciler.cs:1167-1182`

`ResolveCaptionForElement` has no `PasswordBoxElement` arm, so `null` flows
through and `ApplyDefaultAutomationName` no-ops on the password box's
caption. **This is correct.** The risk is that a future contributor adds:

```csharp
PasswordBoxElement pbe => pbe.Password,    // <-- would leak the password to UIA
```

intending to "fix the empty Name" without realising what they have done.
There is no comment in the switch flagging this.

**Recommendation:** add an explicit `PasswordBoxElement => null` arm with a
comment:

```csharp
// PasswordBoxElement intentionally has no caption fallback — Password
// must NEVER be mirrored into AutomationProperties.Name. Authors are
// expected to set .AutomationName(...) explicitly. Do not "fix" this.
PasswordBoxElement => null,
```

This is the cheapest possible safety net against a regression that would be
catastrophic.

---

### F6 — `AccessibilityScanner.Walk` is unbounded recursion (Low)

**File:** `src/Reactor/Core/AccessibilityScanner.cs:160-187`

```csharp
private static void Walk(Element? el, ScanContext ctx, List<A11yDiagnostic> findings)
{
    if (el is null or EmptyElement) return;
    ctx.Push(el);
    …
    foreach (var child in GetChildren(el))
        Walk(child, ctx, findings);                    // ← no depth cap
    ctx.Pop();
}
```

A pathological component graph (regression in user code; a `Stack`-of-`Stack`
loop in a render method) crashes the host with `StackOverflowException` —
unrecoverable, no diagnostic. DEBUG-only, but the scanner is meant to *help*
catch issues, not amplify them.

**Recommendation:** add a depth cap (1 024 is generous) and emit
`A11Y_TREE_DEPTH_EXCEEDED` instead of recursing further. Same fix would be
prudent for `BuildContext`'s `GetChildren` re-walk (line 1033).

---

### F7 — Captions copied into `AutomationProperties.Name` are not stripped of bidi/control codepoints (Low)

**File:** `src/Reactor/Core/Reconciler.cs:1126-1134`

`ApplyDefaultAutomationName` truncates to 100 chars but performs no Unicode
sanitisation. A `Button.Label` containing U+202E (RIGHT-TO-LEFT OVERRIDE)
will land in `AutomationProperties.Name`; any AT client reading the name as
text and re-displaying it can be tricked into showing a homograph or
spoofed direction. This couples to chunk 11's RTL-override concern.

**Recommendation:** strip codepoints in `[U+202A..U+202E]` and
`[U+2066..U+2069]` from `caption` before calling `SetName`. Document
that authors should not pass attacker-controlled strings into `Label`
without sanitisation.

---

### F8 — `CommandBindings.Invoke` discards `ExecuteAsync` task; no faulted-task handling (Low)

**File:** `src/Reactor/Core/CommandBindings.cs:64-68`

```csharp
internal static void Invoke(Command cmd)
{
    if (cmd.Execute is not null) cmd.Execute();
    else if (cmd.ExecuteAsync is not null) _ = cmd.ExecuteAsync();
}
```

Discarding the `Task` means an exception thrown asynchronously surfaces as
an `UnobservedTaskException` (eventually) and never as a user-visible
diagnostic. The doc on `Command.ExecuteAsync` says "Use with
`RenderContext.UseCommand` to get … re-entrance guards" (`Command.cs:19-21`),
but the bare-`Command` path documented in `CommandBindings.Invoke`'s own
comment ("fires-and-forgets") *is* the path executed by every default
factory call (`Dsl.cs:82, 97, 107, 121, 137, 147`).

**Recommendation:** at minimum, attach a `.ContinueWith` that logs the
fault via `ReactorEventSource` (Chunk 15). Or surface it through the
`ErrorBoundary` machinery.

---

### F9 — `AccessibilityScanner.ExportJson` blindly creates directories at any caller-supplied path (Low — DEBUG-only)

**File:** `src/Reactor/Core/AccessibilityScanner.cs:135-154`

```csharp
var dir = global::System.IO.Path.GetDirectoryName(filePath);
if (!string.IsNullOrEmpty(dir) && !global::System.IO.Directory.Exists(dir))
    global::System.IO.Directory.CreateDirectory(dir);
var json = JsonSerializer.Serialize(report, A11yJsonContext.Default.A11yReport);
global::System.IO.File.WriteAllText(filePath, json);
```

No validation of `filePath`: no path-traversal check, no extension allow-list,
no overwrite confirmation. The exported JSON contains every label, heading,
placeholder, and component-type in the running app. DEBUG-only, but the
artefact is exactly the kind of thing developers attach to bug reports.

**Recommendation:** restrict default export location to a known scratch
directory (e.g. `Path.Combine(Path.GetTempPath(), "reactor-a11y")`),
sanitise the filename, and document that the export contains user-visible
text — discourage attaching it without review.

---

### F10 — `SemanticPanelAutomationPeer.SetValue` write-path correctly gates on `IsReadOnly`, but the gate is per-instance state mutable through the same UIA path (Low)

**File:** `src/Reactor/Accessibility/SemanticPanel.cs:48-50, 82-86, 176-190`

`IsReadOnly` is itself a dependency property on the `SemanticPanel`. UIA
clients cannot directly write `IsReadOnly` (it is not exposed via any
provider), so this is fine in practice. The risk is purely theoretical —
note that `Panel.IsReadOnly = …` on the WinUI side could be triggered by
author-side reconciliation while a UIA write is in flight, racing the
`if (!Panel.IsReadOnly)` check (`SemanticPanel.cs:178, 188`). UIA
calls dispatch on the UI thread, so concurrent races are not realistic
under WinUI's single-threaded UIA contract.

**Recommendation:** document the contract; no code change.

---

### F11 — `CheckChartDescription` has dead-code logic (Low — quality)

**File:** `src/Reactor/Core/AccessibilityScanner.cs:509-537`

```csharp
var summary = Charting.Accessibility.ChartSummarizer.Summarize(data);
if (data.Series.Count > 0)
    return;
```

`summary` is computed but never used; the early-return uses
`data.Series.Count > 0` regardless of whether the summary is empty. The
intent ("does the auto-summarizer produce a non-empty summary?" — line 515
comment) does not match the code. Authors of charts with non-empty series
will never get the warning, even when their chart has no description and
the auto-summary turns out empty for some other reason.

**Recommendation:** either use `string.IsNullOrWhiteSpace(summary)` as the
guard or delete the unused `summary` line.

---

## 7. Open questions

1. **Is the ICommand bridge (`CommandInterop.FromCommand`) intended to be one-way only?**
   `CanExecute` is captured at construction (`CommandInterop.cs:29`) with no
   `CanExecuteChanged` subscription. Authors who wire a CommunityToolkit
   `RelayCommand` and expect dynamic enablement will see a stale `IsEnabled`.
   Confirm this is intended and document.

2. **What is the policy on UIA-invokable commands?**
   Today every `Button(cmd)` is silently invokable by any local UIA client.
   Has the team taken a position that "UIA invocation = trusted, same as
   user click"? If so, document; if not, design F3's
   `RequiresUserGesture` flag.

3. **Should `Command.Description` be split into `Tooltip` (visible) and
   `HelpText` (UIA)?** The current 1:1 mirror is convenient for a11y but
   removes the author's ability to put visible-only diagnostic text in a
   tooltip.

4. **Should the default-AutomationName fallback (`Reconciler.cs:1167-1182`)
   include `PasswordBoxElement` as an explicit `=> null` case?** I argue
   yes (F5), but want explicit team agreement so the comment is durable.

5. **Is the chart accessibility namespace (`Charting/Accessibility/**`) in
   chunk 17 or chunk 21?** The chunk-17 scope as written
   (`src/Reactor/Accessibility/**`) only includes `SemanticPanel.cs`.
   `ChartAutomationPeer.cs`, `ChartLiveAnnouncer.cs`, etc. expose UIA
   patterns (`IGridProvider`, `ITableProvider`, `IScrollProvider`) and
   should be reviewed for the same UIA-broadcast and UIA-invoke threats.
   I have **not** reviewed them in this chunk; see §8.

6. **Does the team want a unit test that asserts every `Element` type that
   appears in `Reconciler.Mount`'s switch also has either a
   `ResolveCaptionForElement` arm or an explicit `=> null` arm?** Today,
   adding a new caption-bearing element type silently produces empty UIA
   names; adding a new password-like type silently could leak.

---

## 8. Out-of-scope referrals

- **Chart accessibility (`src/Reactor/Charting/Accessibility/**`, ~2 105 lines).**
  `ChartAutomationPeer` exposes `IGridProvider`/`ITableProvider`/`IScrollProvider`
  with full per-cell content. `ChartLiveAnnouncer` raises UIA notifications
  on every chart update. Any per-cell value is broadcast to UIA. **Refer
  to Chunk 21 (Charting / D3 port).** Specific concerns to forward: the
  scanner-hint pattern at `AccessibilityScanner.cs:449-454` reaches into
  charting via `Charting.Accessibility.ChartScannerHint.InnerCanvas`; what
  is the contract on attached-property mutation?

- **Reconciler attached-property cleanup on element pool reuse.** When the
  `ElementPool` reuses a `WinUI.Button` instance for a new
  `ButtonElement`, F2 (stale `HelpText`) is amplified because the prior
  `HelpText` survives the pool checkout. **Refer to Chunk 14
  (Reconciler).** Specifically, `ElementPool.Recycle` should call
  `ClearValue(AutomationProperties.HelpTextProperty)` and similar.

- **DevtoolsUiaTools `IInvokeProvider` driving (`src/Reactor/Hosting/Devtools/DevtoolsUiaTools.cs:47-59, 586-609`)** — the devtools
  surface explicitly automates `Invoke`-pattern firing. **Refer to Chunk 02
  (Devtools tools).** The risks identified in F3 (consentless invoke)
  apply doubly to that surface, where the caller is a remote MCP client.

- **RTL-override codepoint sanitisation (F7).** Same concern as Chunk 11
  (ICU + locale formatting); shared mitigation strategy makes sense.

- **`ReactorHostControl.EnableAccessibilityDiagnostics`** — the entry point
  is in `src/Reactor/Hosting/ReactorHostControl.cs`. The toggle is in
  Chunk 15 (Hosting); review there whether the toggle is gated to DEBUG
  builds or whether a release build with the symbol can also enable it.
