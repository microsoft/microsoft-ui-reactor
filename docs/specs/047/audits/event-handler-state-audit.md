# `EventHandlerState` Field Audit — Phase 0 §14 Deliverable 2

Drives the spec 047 §9.2 split between `ModifierEventHandlerState` (shared,
routed-input) and per-control payloads inside `ControlEventStateBox`. Raw data
in [`event-handler-state-audit.csv`](event-handler-state-audit.csv).

## Source

`src/Reactor/Core/Reconciler.cs:2787–2877` — class `EventHandlerState` —
plus the `Ensure*Subscribed` family at `Reconciler.cs:2963–3069+` (and the
`NumberBox`-flag site at `Reconciler.Mount.cs:629–635`). Spec §3 cited
`Reconciler.cs:2780+` and `2963-3069+`; current line numbers have drifted by
+7 / 0 (covered by the §3 update in 0.5).

## Tally

| Category | Count | Notes |
|---|---:|---|
| `routed-input-modifier` | 42 (21 `Current*` × 21 `*Trampoline` pairs) | All universally available on every `FrameworkElement` / `UIElement`. |
| `control-intrinsic` | 9 | One bool flag (`NumberBoxInnerTextChanged`) + 8 trampoline delegates across 7 control types. |
| `hybrid-or-ambiguous` | 0 | No fields needed the third bucket. |

Total: 51 fields on `EventHandlerState` today.

## Per-control payload sketches

The audit found **7 distinct control types** with at least one
`control-intrinsic` event. The §9.2 design splits each into its own struct,
allocated only when the control is mounted (not on every `FrameworkElement`).

```csharp
// New: discriminated payload inside ControlEventStateBox.
// One per built-in control with control-intrinsic events.

internal struct ToggleSwitchControlEventState
{
    public RoutedEventHandler? ToggledTrampoline;
}

internal struct ButtonControlEventState
{
    // Shared across Button, HyperlinkButton, RepeatButton, ToggleButton,
    // ToggleSplitButton — all ButtonBase subclasses use the same trampoline.
    public RoutedEventHandler? ClickTrampoline;
}

internal struct TextBoxControlEventState
{
    public WinUI.TextChangedEventHandler? TextChangedTrampoline;
    public RoutedEventHandler? SelectionChangedTrampoline;
}

internal struct ImageControlEventState
{
    public RoutedEventHandler? OpenedTrampoline;
    public Microsoft.UI.Xaml.ExceptionRoutedEventHandler? FailedTrampoline;
}

internal struct ScrollViewerControlEventState
{
    public global::System.EventHandler<WinUI.ScrollViewerViewChangedEventArgs>? ViewChangedTrampoline;
}

internal struct ScrollViewControlEventState
{
    // ScrollView (no -er): WinAppSDK control with TypedEventHandler signature.
    public global::Windows.Foundation.TypedEventHandler<WinUI.ScrollView, object>? ViewChangedTrampoline;
}

internal struct NumberBoxControlEventState
{
    public bool InnerTextChangedWired;
}
```

Notes:
- All seven structs above are *engine-owned* — they store the stable trampoline
  delegate(s) needed to dispatch into the user handler that lives on the
  current `Element` (read via `GetElementTag`). Unlike the universal
  `Current*` fields on `ModifierEventHandlerState`, these don't need their own
  mutable "current user delegate" slot because the user handler is always
  reached through the element, not stored on the box.
- `ButtonControlEventState` covers five element types via the `ButtonBase`
  base class. The struct keys on native DO identity (one trampoline per
  `Button`), but the *kind* of element the trampoline dispatches to is read
  via `GetElementTag` at fire time. This mirrors today's discipline at
  `Reconciler.cs:2839`.
- The Element wrappers themselves are unchanged — `ToggleSwitchElement` still
  carries `OnIsOnChanged`. Only the bookkeeping of "is the trampoline attached
  to this native control yet?" moves into the per-control struct.

## §9.4 hypothesis — testable from this audit

Spec §9.4 hypothesizes that most controls in a representative tree would
**never allocate `ModifierEventHandlerState`** after the split, because their
engine-side wiring only needs control-intrinsic state, and the user never
attaches any of the 21 universal routed-input modifiers (pointer, key, tap,
focus, character).

**Count of controls whose engine-owned events are exclusively
`control-intrinsic`: 7 / 7 distinct controls in the audit.** Every
`control-intrinsic` field today co-exists on the same `EventHandlerState` as
the routed-input fields *only because they share one box*. There is no
engine-owned routed-input handler on any built-in (e.g., the engine does not
wire `PointerPressed` for `Button` — the button uses its own `Click` event
internally; routed-input handlers attach only when the user adds an
`OnPointerXxx` modifier).

This means after the §9.2 split:
- A leaf control with no user-supplied modifiers (e.g., a `TextBlock`, an
  `Image` that loaded synchronously, a stable `Button`) allocates **only** its
  per-control payload — and only if it has any `control-intrinsic` events
  (TextBlock has none, so it allocates nothing at all).
- A tree of ~1000 controls where ~10% have user-modifiers allocates ~100
  `ModifierEventHandlerState` instances instead of 1000 — a 10× allocation
  reduction in the §11 byte tables (subject to the M11 measurement once the
  perf suite lands).

The audit cannot prove the *frequency* claim ("~90% of representative-tree
controls have no user modifiers") — that's what M11 in §15.3 measures. But it
confirms the *structural* claim that the split is well-defined and complete.

## Follow-ups carried to Phase 1

- The 7 per-control struct shapes above are inputs to §9.2's
  `ControlEventStateBox` design. Phase 1 work item: wire one of them (suggest
  `ButtonControlEventState` since it covers 5 element types) end-to-end as a
  walking-skeleton before generalizing.
- The `NumberBoxInnerTextChanged` flag is the smallest, ugliest case — it's
  a single bool that piggybacks on the universal `EventHandlerState` today.
  Once split, it should live on `NumberBoxControlEventState`.
- The `ScrollViewer` (legacy) vs `ScrollView` (WinAppSDK) split has parallel
  trampolines today. The §9.2 design should accept that the per-control
  struct identity is keyed by the native control type, not the protocol
  family — these stay distinct.

## Cross-reference to §3 / Appendix A line numbers

Phase 0 0.5 will update spec §3 and Appendix A to point at the current line
numbers (`Reconciler.cs:2787` for the class, `2963+` for the Ensure family).
