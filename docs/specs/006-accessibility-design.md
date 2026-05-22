# Reactor Accessibility System — Detailed Design

## Status

**Proposal** — pending review.

---

## Problem Statement

The [gap analysis](002-winui3-gap-analysis.md) §16 identifies accessibility as **"the largest
gap for production apps."** Today, only `AutomationProperties.Name` and
`AutomationProperties.AutomationId` have first-class Microsoft.UI.Reactor (Reactor) modifiers. Nine WinUI accessibility
features are **Missing**, one is **Blocked** (custom `AutomationPeer`), and everything else
requires the `.Set()` escape hatch. This blocks WCAG AA compliance for any production Reactor
application.

### Current Surface (before this spec)

| WinUI Feature | Reactor Status | Reactor API |
|---|---|---|
| `AutomationProperties.Name` | **Exposed** | `.AutomationName("label")` |
| `AutomationProperties.AutomationId` | **Exposed** | `.AutomationId("id")` |
| `AutomationProperties.HelpText` | Missing | `.Set()` only |
| `AutomationProperties.HeadingLevel` | Missing | `.Set()` only |
| `AutomationProperties.LandmarkType` | Missing | `.Set()` only |
| `AutomationProperties.LiveSetting` | Missing | `.Set()` only |
| `AutomationProperties.AccessibilityView` | Missing | `.Set()` only |
| `AutomationProperties.IsRequiredForForm` | Missing | `.Set()` only |
| `AutomationProperties.LabeledBy` | Missing | `.Set()` only |
| `AutomationProperties.FullDescription` | Missing | `.Set()` only |
| `AutomationProperties.PositionInSet/SizeOfSet` | Missing | `.Set()` only |
| Custom `AutomationPeer` | **Blocked** | Components don't subclass `Control` |
| UIA Tree | **Passthrough** | Works automatically on rendered controls |
| Focus management (`IsTabStop`, `TabIndex`) | Missing | `.Set()` only |
| Access keys | Missing | `.Set()` only |

---

## Goals

1. **Make Reactor apps accessible by default** with world-class ergonomics — matching or exceeding
   the developer experience of SwiftUI's `.accessibilityLabel()`, React Aria's hooks, and
   Compose's `Modifier.semantics {}`.
2. **Leverage WinUI 3's built-in accessibility** — every standard control already has an
   `AutomationPeer`; Reactor just needs to expose the knobs.
3. **WCAG 2.1 AA compliance** as the target for any Reactor app that uses the accessibility API
   surface.
4. **Breaking changes are acceptable** where they improve accessibility correctness.

---

## Design Philosophy

**Explicit is better than implicit.** Reactor will NOT auto-infer accessible names at runtime
(unlike SwiftUI which derives labels from `Text` content). Instead:

1. Make the right thing trivially easy — fluent modifiers, sensible defaults on `Heading()`, etc.
2. Make the wrong thing visibly wrong — compile-time warnings, runtime diagnostics.
3. Make diagnostics **machine-readable** so that a developer's AI agent (Copilot, etc.) can
   trivially consume the warnings and auto-generate all missing `.AutomationName()`,
   `.HelpText()`, `.HeadingLevel()` annotations in a single pass.
4. Keep escape hatches always available — `.Set()` remains for anything not wrapped.

**AI-agent-friendly by design:** Rather than baking inference heuristics into the framework,
Reactor's accessibility diagnostics emit structured data. Each warning includes the element type,
component location, surrounding context (sibling text, parent labels, child content), and the
specific modifier needed. This gives an AI agent everything it needs to generate intelligent,
context-aware defaults — without Reactor itself doing any guessing.

**Priority areas:**
- Screen readers (Narrator / NVDA)
- Keyboard navigation
- High contrast / contrast themes
- UI Automation testing (FlaUI / WinAppDriver)
- Touch accessibility
- Reduced motion / animations

---

## Architecture Overview

The accessibility system has seven layers, each building on the previous:

```
┌──────────────────────────────────────────────────────────────────┐
│  Layer 7: Convenience DSL (FormField, Heading defaults)          │
├──────────────────────────────────────────────────────────────────┤
│  Layer 6: Diagnostics & Linting                                  │
│  → Runtime warnings, machine-readable JSON export, Roslyn        │
├──────────────────────────────────────────────────────────────────┤
│  Layer 5: Hooks — UseAccessibility, UseReducedMotion,            │
│           UseHighContrast, UseScreenReaderActive                 │
├──────────────────────────────────────────────────────────────────┤
│  Layer 4: High Contrast & Theme Integration                      │
│  → Reconciler respects contrast themes, ThemeRef interop         │
├──────────────────────────────────────────────────────────────────┤
│  Layer 3: Focus & Keyboard Navigation                            │
│  → Tab stops, tab index, access keys, focus trapping             │
├──────────────────────────────────────────────────────────────────┤
│  Layer 2: Live Regions & Dynamic Announcements                   │
│  → LiveSetting modifier, UseAnnounce() imperative hook           │
├──────────────────────────────────────────────────────────────────┤
│  Layer 1: Core Modifiers (AutomationProperties surface)          │
│  → HelpText, HeadingLevel, LandmarkType, AccessibilityView,     │
│    LabeledBy, IsRequiredForForm, FullDescription, etc.           │
├──────────────────────────────────────────────────────────────────┤
│  Layer 0: WinUI 3 Built-in (already works, no Reactor changes)     │
│  → AutomationPeers on every standard control, UIA tree,         │
│    Narrator support, default keyboard handling                   │
└──────────────────────────────────────────────────────────────────┘
```

---

## Layer 1: Core Accessibility Modifiers

### 1.1 New `ElementModifiers` Properties

Add to the `ElementModifiers` record in `Reactor/Core/Element.cs`, alongside the existing
`AutomationName` and `AutomationId`:

```csharp
// ── Screen reader / semantic properties ──────────────────────────

/// <summary>AutomationProperties.HelpText — supplemental description.</summary>
public string? HelpText { get; init; }

/// <summary>AutomationProperties.FullDescription — extended description (WinUI 1.4+).</summary>
public string? FullDescription { get; init; }

/// <summary>AutomationProperties.HeadingLevel — heading rank (Level1–Level9).</summary>
public AutomationHeadingLevel? HeadingLevel { get; init; }

/// <summary>AutomationProperties.LandmarkType — landmark region kind.</summary>
public AutomationLandmarkType? LandmarkType { get; init; }

/// <summary>AutomationProperties.AccessibilityView — UIA tree visibility.</summary>
public AccessibilityView? AccessibilityView { get; init; }

/// <summary>AutomationProperties.IsRequiredForForm — required-field marker.</summary>
public bool? IsRequiredForForm { get; init; }

/// <summary>AutomationProperties.LiveSetting — live region announcement mode.</summary>
public AutomationLiveSetting? LiveSetting { get; init; }

/// <summary>AutomationProperties.PositionInSet — ordinal position (1-based).</summary>
public int? PositionInSet { get; init; }

/// <summary>AutomationProperties.SizeOfSet — total count in set.</summary>
public int? SizeOfSet { get; init; }

/// <summary>AutomationProperties.Level — hierarchical depth (e.g., tree level).</summary>
public int? Level { get; init; }

/// <summary>AutomationProperties.ItemStatus — status string (e.g., "3 unread").</summary>
public string? ItemStatus { get; init; }

/// <summary>AutomationProperties.LabeledBy — AutomationId of the labelling element.</summary>
public string? LabeledBy { get; init; }

// ── Keyboard / focus management ──────────────────────────────────

/// <summary>Control.IsTabStop — whether element participates in Tab navigation.</summary>
public bool? IsTabStop { get; init; }

/// <summary>Control.TabIndex — Tab order index.</summary>
public int? TabIndex { get; init; }

/// <summary>UIElement.AccessKey — Alt+Key shortcut (shown on Alt press).</summary>
public string? AccessKey { get; init; }

/// <summary>UIElement.TabFocusNavigation — Tab behavior within a container.</summary>
public KeyboardNavigationMode? TabFocusNavigation { get; init; }
```

### 1.2 Fluent Modifier Extension Methods

Add to `Reactor/Elements/ElementExtensions.cs`, in a new `Accessibility Modifiers` section after
the existing `AutomationName` / `AutomationId` section:

```csharp
// ════════════════════════════════════════════════════════════════
//  Accessibility — Screen Reader Annotations
// ════════════════════════════════════════════════════════════════

/// <summary>
/// Sets AutomationProperties.HelpText — supplemental text read by screen readers
/// after the Name. Analogous to SwiftUI's .accessibilityHint() or Compose's stateDescription.
/// </summary>
/// <example>TextField(email, setEmail).HelpText("Enter your work email address")</example>
public static T HelpText<T>(this T el, string text) where T : Element =>
    Modify(el, new ElementModifiers { HelpText = text });

/// <summary>
/// Sets AutomationProperties.FullDescription — extended description for complex elements.
/// Requires Windows App SDK 1.4+.
/// </summary>
/// <example>Chart(...).FullDescription("Bar chart showing Q1 revenue by region")</example>
public static T FullDescription<T>(this T el, string desc) where T : Element =>
    Modify(el, new ElementModifiers { FullDescription = desc });

/// <summary>
/// Sets AutomationProperties.HeadingLevel (Level1–Level9).
/// Screen reader users navigate by headings, like HTML &lt;h1&gt;–&lt;h9&gt;.
/// </summary>
/// <example>Text("Settings").HeadingLevel(AutomationHeadingLevel.Level1)</example>
public static T HeadingLevel<T>(this T el, AutomationHeadingLevel level) where T : Element =>
    Modify(el, new ElementModifiers { HeadingLevel = level });

/// <summary>
/// Sets AutomationProperties.LandmarkType (Main, Navigation, Search, Form, Custom).
/// Screen readers announce landmarks and let users jump between them.
/// </summary>
/// <example>VStack(children).Landmark(AutomationLandmarkType.Main)</example>
public static T Landmark<T>(this T el, AutomationLandmarkType type) where T : Element =>
    Modify(el, new ElementModifiers { LandmarkType = type });

/// <summary>
/// Sets AutomationProperties.AccessibilityView (Content, Control, Raw).
/// Use Raw to hide decorative elements from screen readers.
/// </summary>
/// <example>Image(decorativeUri).AccessibilityView(AccessibilityView.Raw)</example>
public static T AccessibilityView<T>(this T el,
    Microsoft.UI.Xaml.Automation.Peers.AccessibilityView view) where T : Element =>
    Modify(el, new ElementModifiers { AccessibilityView = view });

/// <summary>
/// Hides element from screen readers entirely.
/// Shorthand for .AccessibilityView(AccessibilityView.Raw).
/// </summary>
/// <example>Icon(decorativeGlyph).AccessibilityHidden()</example>
public static T AccessibilityHidden<T>(this T el) where T : Element =>
    Modify(el, new ElementModifiers {
        AccessibilityView = Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw
    });

/// <summary>
/// Sets AutomationProperties.IsRequiredForForm. Screen readers announce "required".
/// </summary>
/// <example>TextField(name, setName).Required()</example>
public static T Required<T>(this T el) where T : Element =>
    Modify(el, new ElementModifiers { IsRequiredForForm = true });

/// <summary>
/// Sets AutomationProperties.PositionInSet and SizeOfSet (e.g., "item 3 of 10").
/// </summary>
/// <example>ListItem(text).PositionInSet(3, 10)</example>
public static T PositionInSet<T>(this T el, int position, int size) where T : Element =>
    Modify(el, new ElementModifiers { PositionInSet = position, SizeOfSet = size });

/// <summary>
/// Sets AutomationProperties.Level — hierarchical depth (e.g., tree node depth).
/// </summary>
/// <example>TreeItem(text).Level(2)</example>
public static T Level<T>(this T el, int level) where T : Element =>
    Modify(el, new ElementModifiers { Level = level });

/// <summary>
/// Sets AutomationProperties.ItemStatus — status string announced by screen readers.
/// </summary>
/// <example>MailFolder("Inbox").ItemStatus("3 unread")</example>
public static T ItemStatus<T>(this T el, string status) where T : Element =>
    Modify(el, new ElementModifiers { ItemStatus = status });

/// <summary>
/// Associates this element with a labelling element via its AutomationId.
/// The reconciler resolves the reference and calls AutomationProperties.SetLabeledBy().
/// </summary>
/// <example>
/// Text("Email address").AutomationId("EmailLabel")
/// TextField(email, setEmail).LabeledBy("EmailLabel")
/// </example>
public static T LabeledBy<T>(this T el, string labelAutomationId) where T : Element =>
    Modify(el, new ElementModifiers { LabeledBy = labelAutomationId });

// ════════════════════════════════════════════════════════════════
//  Accessibility — Live Regions
// ════════════════════════════════════════════════════════════════

/// <summary>
/// Sets AutomationProperties.LiveSetting on the element. Screen readers announce content
/// changes automatically. Polite = queued after current speech. Assertive = interrupts.
/// </summary>
/// <example>Text(statusMessage).LiveRegion(AutomationLiveSetting.Polite)</example>
public static T LiveRegion<T>(this T el,
    AutomationLiveSetting mode = AutomationLiveSetting.Polite) where T : Element =>
    Modify(el, new ElementModifiers { LiveSetting = mode });

// ════════════════════════════════════════════════════════════════
//  Accessibility — Keyboard / Focus Management
// ════════════════════════════════════════════════════════════════

/// <summary>
/// Sets Control.IsTabStop — whether the element participates in Tab navigation.
/// </summary>
/// <example>Panel(children).IsTabStop(false)  // skip decorative container</example>
public static T IsTabStop<T>(this T el, bool isTabStop) where T : Element =>
    Modify(el, new ElementModifiers { IsTabStop = isTabStop });

/// <summary>
/// Sets Control.TabIndex — Tab order position. Lower values receive focus first.
/// </summary>
/// <example>SubmitButton.TabIndex(1)</example>
public static T TabIndex<T>(this T el, int index) where T : Element =>
    Modify(el, new ElementModifiers { TabIndex = index });

/// <summary>
/// Sets UIElement.AccessKey — the Alt+Key shortcut (underlined hint shown on Alt press).
/// </summary>
/// <example>Button("File", onClick).AccessKey("F")</example>
public static T AccessKey<T>(this T el, string key) where T : Element =>
    Modify(el, new ElementModifiers { AccessKey = key });

/// <summary>
/// Sets UIElement.TabFocusNavigation — how Tab navigates within a container's children.
/// Local = cycle within container. Once = enter once then leave. Cycle = loop forever.
/// </summary>
/// <example>ToolBar(buttons).TabNavigation(KeyboardNavigationMode.Once)</example>
public static T TabNavigation<T>(this T el, KeyboardNavigationMode mode) where T : Element =>
    Modify(el, new ElementModifiers { TabFocusNavigation = mode });
```

### 1.3 Reconciler Changes

Extend the modifier application block in `Reactor/Core/Reconciler.cs` (after the existing
`AutomationProperties.SetName` / `SetAutomationId` calls, ~line 540):

```csharp
// ── Accessibility modifiers ──────────────────────────────────────

if (m.HelpText is not null && m.HelpText != oldM?.HelpText)
    AutomationProperties.SetHelpText(fe, m.HelpText);

if (m.FullDescription is not null && m.FullDescription != oldM?.FullDescription)
    AutomationProperties.SetFullDescription(fe, m.FullDescription);

if (m.HeadingLevel.HasValue && m.HeadingLevel != oldM?.HeadingLevel)
    AutomationProperties.SetHeadingLevel(fe, m.HeadingLevel.Value);

if (m.LandmarkType.HasValue && m.LandmarkType != oldM?.LandmarkType)
    AutomationProperties.SetLandmarkType(fe, m.LandmarkType.Value);

if (m.AccessibilityView.HasValue && m.AccessibilityView != oldM?.AccessibilityView)
    AutomationProperties.SetAccessibilityView(fe, m.AccessibilityView.Value);

if (m.IsRequiredForForm.HasValue && m.IsRequiredForForm != oldM?.IsRequiredForForm)
    AutomationProperties.SetIsRequiredForForm(fe, m.IsRequiredForForm.Value);

if (m.LiveSetting.HasValue && m.LiveSetting != oldM?.LiveSetting)
    AutomationProperties.SetLiveSetting(fe, m.LiveSetting.Value);

if (m.PositionInSet.HasValue && m.PositionInSet != oldM?.PositionInSet)
    AutomationProperties.SetPositionInSet(fe, m.PositionInSet.Value);

if (m.SizeOfSet.HasValue && m.SizeOfSet != oldM?.SizeOfSet)
    AutomationProperties.SetSizeOfSet(fe, m.SizeOfSet.Value);

if (m.Level.HasValue && m.Level != oldM?.Level)
    AutomationProperties.SetLevel(fe, m.Level.Value);

if (m.ItemStatus is not null && m.ItemStatus != oldM?.ItemStatus)
    AutomationProperties.SetItemStatus(fe, m.ItemStatus);

// ── Keyboard / focus (Control properties) ────────────────────────

if (fe is Control a11yCtrl)
{
    if (m.IsTabStop.HasValue && m.IsTabStop != oldM?.IsTabStop)
        a11yCtrl.IsTabStop = m.IsTabStop.Value;
    if (m.TabIndex.HasValue && m.TabIndex != oldM?.TabIndex)
        a11yCtrl.TabIndex = m.TabIndex.Value;
}

if (m.AccessKey is not null && m.AccessKey != oldM?.AccessKey)
    fe.AccessKey = m.AccessKey;

if (m.TabFocusNavigation.HasValue && m.TabFocusNavigation != oldM?.TabFocusNavigation)
    fe.TabFocusNavigation = m.TabFocusNavigation.Value;
```

### 1.4 `LabeledBy` Resolution

`AutomationProperties.LabeledBy` takes a `UIElement` reference, not a string. Reactor's virtual
tree cannot reference sibling elements directly. The resolution strategy:

1. `.LabeledBy("EmailLabel")` stores the target `AutomationId` string on `ElementModifiers`.
2. After mounting, the reconciler walks the visual tree to find the `FrameworkElement` whose
   `AutomationProperties.AutomationId` matches the stored string.
3. Calls `AutomationProperties.SetLabeledBy(target, labelElement)`.
4. This runs in the same dispatcher batch as mount, so both elements are already in the tree.

If no match is found, emit a diagnostic warning (see Layer 6).

### 1.5 `ModifiersEqual()` Update

Add all new properties to the equality check in `Element.ModifiersEqual()`:

```csharp
&& a.HelpText == b.HelpText
&& a.FullDescription == b.FullDescription
&& a.HeadingLevel == b.HeadingLevel
&& a.LandmarkType == b.LandmarkType
&& a.AccessibilityView == b.AccessibilityView
&& a.IsRequiredForForm == b.IsRequiredForForm
&& a.LiveSetting == b.LiveSetting
&& a.PositionInSet == b.PositionInSet
&& a.SizeOfSet == b.SizeOfSet
&& a.Level == b.Level
&& a.ItemStatus == b.ItemStatus
&& a.LabeledBy == b.LabeledBy
&& a.IsTabStop == b.IsTabStop
&& a.TabIndex == b.TabIndex
&& a.AccessKey == b.AccessKey
&& a.TabFocusNavigation == b.TabFocusNavigation
```

### 1.6 `Merge()` Update

Add all new properties to `ElementModifiers.Merge()` using the existing
`other.X ?? X` null-coalescing pattern.

---

## Layer 2: Live Regions & Dynamic Announcements

### 2.1 `UseAnnounce()` Hook

For scenarios where you need to announce something to screen readers without a visible UI
element (e.g., "File saved", "3 items deleted"):

```csharp
// In Component.Render():
var announce = UseAnnounce();

// Later, in a callback:
announce("File saved successfully", AnnouncePriority.Polite);
announce("Error: connection lost", AnnouncePriority.Assertive);
```

**New types:**

```csharp
public enum AnnouncePriority { Polite, Assertive }
```

**Implementation:**

1. The `ReactorHost` maintains a hidden, zero-size `TextBlock` in the visual tree with
   `AutomationProperties.LiveSetting` set.
2. `UseAnnounce()` returns an `Action<string, AnnouncePriority>` delegate bound to the host.
3. When called, the delegate:
   - Sets `LiveSetting` to `Polite` or `Assertive` as requested.
   - Sets the TextBlock's `Text` to the announcement string.
   - Calls `AutomationPeer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged)` via the
     TextBlock's peer.
   - Clears the text after a short delay to prevent stale re-announcements.

**Exposed on both `Component` and `RenderContext`:**

```csharp
// Component base class:
protected Action<string, AnnouncePriority> UseAnnounce()
    => Context.UseAnnounce();

// RenderContext:
public Action<string, AnnouncePriority> UseAnnounce()
{
    return (message, priority) => _host.Announce(message, priority);
}
```

### 2.2 Declarative Live Regions

Already covered by the `.LiveRegion()` modifier in Layer 1. When the text content of an element
with `AutomationProperties.LiveSetting` changes, WinUI's built-in automation peers handle the
screen reader announcement automatically — no additional Reactor logic needed.

---

## Layer 3: Focus & Keyboard Navigation

### 3.1 Programmatic Focus via `UseRef` + `.Ref()`

Reactor already has `UseRef<T>()`. Add a `.Ref()` convenience modifier that captures the mounted
`FrameworkElement`, enabling programmatic focus:

```csharp
// In Component.Render():
var submitRef = UseRef<FrameworkElement>();

return VStack(
    TextField(email, setEmail),
    Button("Submit", () =>
    {
        if (!Validate(email))
            submitRef.Current?.Focus(FocusState.Programmatic);
        else
            Submit(email);
    }).Ref(submitRef)
);
```

**`.Ref()` implementation** — a thin wrapper over `.OnMount()`:

```csharp
public static T Ref<T>(this T el, Ref<FrameworkElement> refHolder) where T : Element =>
    el.OnMount(fe => refHolder.Current = fe);
```

### 3.2 `UseFocusTrap()` Hook

For modal dialogs and flyouts, traps focus within a container:

```csharp
var trapRef = UseFocusTrap(isActive: isDialogOpen);

return Border(
    VStack(
        Text("Confirm delete?"),
        HStack(
            Button("Cancel", () => setOpen(false)),
            Button("Delete", onDelete)
        )
    )
).Ref(trapRef);
```

**Implementation:** When `isActive` is true, the hook installs a `LosingFocus` handler on the
container that cancels focus changes targeting elements outside the container. Uses
`FocusManager.TryMoveFocus()` with `FindNextElementOptions` scoped to the container's subtree.
When `isActive` becomes false, the handler is removed.

### 3.3 Keyboard Accelerator Tooltip Auto-Generation

Reactor already has `KeyboardAcceleratorData`. Enhance the reconciler so that when a
`KeyboardAccelerator` is applied, it automatically sets `KeyboardAcceleratorTextOverride` to a
human-readable string (e.g., "Ctrl+S"). This ensures:

- Tooltips display the shortcut to sighted users.
- Screen readers announce the shortcut when the control receives focus.

---

## Layer 4: High Contrast & Theme Integration

### 4.1 `UseHighContrast()` Hook

```csharp
var isHighContrast = UseHighContrast();

return Canvas(draw: ctx =>
{
    var lineColor = isHighContrast ? Colors.White : accentColor;
    ctx.DrawLine(lineColor, startPoint, endPoint);
});
```

**Implementation:**

1. Uses `ThemeSettings.CreateForWindowId()` (Windows App SDK 1.4+) to read the `.HighContrast`
   property.
2. Subscribes to `ThemeSettings.Changed` event via `UseEffect`.
3. Returns a `bool` that triggers a re-render on change.

**Exposed on `Component` and `RenderContext`:**

```csharp
protected bool UseHighContrast() => Context.UseHighContrast();
```

### 4.2 `UseReducedMotion()` Hook

```csharp
var prefersReducedMotion = UseReducedMotion();

return Image(uri)
    .If(!prefersReducedMotion, el => el.WithOpacityTransition());
```

**Implementation:**

1. Reads `UISettings.AnimationsEnabled` (a Windows system setting).
2. Returns a `bool` — components can conditionally skip animations.

### 4.3 Reconciler: Auto-Respect Reduced Motion

When `UISettings.AnimationsEnabled` is false, the reconciler skips applying
`ImplicitTransitions` and `ThemeTransitions`. This is a **breaking change** — animations are
silently disabled — but is the correct accessibility behavior. Apps that rely on transitions for
layout correctness (bad practice) may break.

### 4.4 High Contrast & Concrete Brushes

Reactor controls styled with concrete brush values (`.Background("#FF5733")`) will lose their
custom color in high contrast mode — Windows overrides brushes in HC. This is documented
behavior, but developers may not expect it.

**Guidance:** Use `ThemeRef` values from the [theming system](001-theming-design.md) instead
of concrete brushes on interactive controls. `Theme.Accent`, `Theme.PrimaryText`, etc.
automatically re-resolve in HC mode.

**Enforcement:** A runtime diagnostic (Layer 6) warns when concrete brushes are applied to
interactive controls (`Button`, `TextField`, etc.), suggesting `ThemeRef` alternatives.

---

## Layer 5: Accessibility Hooks

### 5.1 `UseAccessibility()` — Composite Hook

A convenience hook that bundles the most common accessibility state into a single call:

```csharp
var a11y = UseAccessibility();

// a11y.IsHighContrast       — bool
// a11y.ReducedMotion        — bool
// a11y.IsScreenReaderActive — bool
// a11y.TextScaleFactor      — double
// a11y.Announce             — Action<string, AnnouncePriority>
```

**Implementation:** Composes `UseHighContrast()`, `UseReducedMotion()`, `UseAnnounce()`, plus
additional system accessibility state, into a single record returned to the component.

**Return type:**

```csharp
public record AccessibilityState(
    bool IsHighContrast,
    bool ReducedMotion,
    bool IsScreenReaderActive,
    double TextScaleFactor,
    Action<string, AnnouncePriority> Announce);
```

### 5.2 `UseScreenReaderActive()` Hook

```csharp
var isScreenReaderActive = UseScreenReaderActive();

return VStack(
    Button(SymbolIcon(Symbol.Delete), onDelete)
        .AutomationName("Delete selected items"),
    // Show visible label only when a screen reader is running
    isScreenReaderActive
        ? Text("Delete selected items").Caption()
        : null
);
```

**Implementation:** Uses `AutomationPeer.ListenerExists(AutomationEvents.AutomationFocusChanged)`
as a heuristic for screen reader presence. Polled on mount and cached for the component lifetime.

---

## Layer 6: Diagnostics & Linting

### 6.1 Runtime Accessibility Diagnostics

Opt-in diagnostic mode that validates the accessibility tree after each reconciliation pass:

```csharp
ReactorApp.Run<MyApp>("App", configure: host =>
{
    host.EnableAccessibilityDiagnostics(); // opt-in, recommended for DEBUG builds
});
```

**Diagnostic checks:**

| ID | Check | Severity | Description |
|----|-------|----------|-------------|
| `A11Y_001` | Missing name on icon-only Button | Warning | `ButtonElement` with no text content and no `.AutomationName()` |
| `A11Y_002` | Missing name on Image | Warning | `ImageElement` without `.AutomationName()` or `.AccessibilityHidden()` |
| `A11Y_003` | Form field without label | Warning | `TextField` / `NumberBox` etc. without `header:`, `.AutomationName()`, or `.LabeledBy()` |
| `A11Y_004` | Heading without HeadingLevel | Info | `Text` styled as heading but no `.HeadingLevel()` set |
| `A11Y_005` | Concrete brush on interactive control | Warning | `.Background("#color")` on Button/TextField (breaks high contrast) |
| `A11Y_006` | Missing landmark on root layout | Info | Top-level layout container without `.Landmark(Main)` |
| `A11Y_007` | TabIndex gaps | Info | Non-sequential `TabIndex` values that may confuse keyboard navigation |
| `A11Y_008` | Unresolved LabeledBy | Warning | `.LabeledBy("id")` references an `AutomationId` not found in the tree |

Output via `ILogger` (existing logging interface), tagged `[A11Y]` for filtering:

```
[A11Y] WARNING A11Y_001: Icon-only Button has no accessible name
       Component: MyApp.Components.Toolbar
       Element: Button(SymbolIcon(Symbol.Delete), onDelete)
       Fix: Add .AutomationName("description") to provide a screen reader label
```

### 6.2 Machine-Readable Diagnostic Export (Agent-Friendly)

In addition to human-readable log output, diagnostics can produce structured JSON that an AI
agent can consume and auto-fix:

```csharp
host.EnableAccessibilityDiagnostics(output: AccessibilityDiagnosticOutput.Json);
// Writes to: obj/Debug/a11y-diagnostics.json after each reconciliation
```

**Schema for each diagnostic entry:**

```json
{
  "diagnostics": [
    {
      "id": "A11Y_001",
      "severity": "warning",
      "elementType": "ButtonElement",
      "automationId": "DeleteBtn",
      "message": "Icon-only Button has no accessible name",
      "fix": {
        "modifier": "AutomationName",
        "suggestedValue": null
      },
      "context": {
        "parentAutomationName": "Toolbar",
        "siblingTexts": ["Edit", "Copy", "Paste"],
        "childContent": "SymbolIcon(Symbol.Delete)",
        "nearestHeading": "Document Actions"
      },
      "sourceLocation": {
        "component": "MyApp.Components.Toolbar",
        "approximateLine": "Button(SymbolIcon(Symbol.Delete), onDelete)"
      }
    }
  ]
}
```

**Design decisions for agent-friendliness:**

- **`context`** gives the agent semantic clues (sibling labels, parent names, child content) so
  it can generate *intelligent* names — not placeholder text like `"TODO: add name"`.
- **`fix.modifier`** tells the agent exactly which Reactor modifier to add.
- **`sourceLocation`** helps the agent locate the code to patch.
- **Agent workflow:** run app → read `a11y-diagnostics.json` → generate `.AutomationName()`,
  `.HelpText()`, etc. for every warning → done in one pass.

### 6.3 Roslyn Analyzer (Stretch Goal)

A source generator / Roslyn analyzer that catches issues at compile time:

| Diagnostic ID | Pattern | Message |
|---|---|---|
| `REACTOR_A11Y_001` | `Button(iconElement, onClick)` without `.AutomationName()` | Icon-only buttons need an accessible name |
| `REACTOR_A11Y_002` | `Image(uri)` without `.AutomationName()` or `.AccessibilityHidden()` | Images need alt text or explicit decoration marking |
| `REACTOR_A11Y_003` | `TextField(...)` without `header:` parameter or `.AutomationName()` | Form fields need labels |

These are **warnings**, not errors — allowing gradual adoption.

### 6.4 Accessibility Test Helper

A test utility for UI test projects using FlaUI or WinAppDriver:

```csharp
// In test code:
var tree = ReactorAccessibilityTree.Capture(host);

// Assert every interactive element has a name
tree.AssertAllInteractiveElementsHaveNames();

// Assert heading hierarchy is valid (no skipped levels)
tree.AssertValidHeadingHierarchy();

// Assert all form fields have labels
tree.AssertFormFieldsLabeled();

// Assert specific elements
var button = tree.FindById("SubmitButton");
Assert.Equal("Submit order", button.Name);
```

---

## Layer 7: Convenience DSL Enhancements

### 7.1 `Heading()` and `SubHeading()` with Default HeadingLevel

Update the existing `Heading()` and `SubHeading()` factories in `Dsl.cs` to set
`AutomationProperties.HeadingLevel` by default:

```csharp
// Current:
public static TextElement Heading(string content) =>
    new(content) { FontSize = 28, Weight = new FontWeight(700) };

// Proposed — adds HeadingLevel automatically:
public static TextElement Heading(string content,
    AutomationHeadingLevel level = AutomationHeadingLevel.Level1) =>
    new(content)
    {
        FontSize = 28,
        Weight = new FontWeight(700),
        Modifiers = new ElementModifiers { HeadingLevel = level }
    };

public static TextElement SubHeading(string content,
    AutomationHeadingLevel level = AutomationHeadingLevel.Level2) =>
    new(content)
    {
        FontSize = 20,
        Weight = new FontWeight(600),
        Modifiers = new ElementModifiers { HeadingLevel = level }
    };
```

This means `Heading("Settings")` automatically announces as a Level 1 heading to screen
readers. The level can be overridden: `Heading("Subsection", AutomationHeadingLevel.Level3)`.

**Breaking change:** Existing `Heading()` calls will now have `HeadingLevel.Level1` set. This
is correct behavior but changes the UIA tree structure for existing apps.

### 7.2 `FormField()` Convenience Component

A higher-level factory that wires up label + input + error + required + help text automatically:

```csharp
FormField(
    label: "Email address",
    input: TextField(email, setEmail),
    error: emailError,          // null when valid, string when invalid
    required: true,
    helpText: "We'll never share your email"
)
```

**Internally produces:**

```csharp
VStack(
    Text("Email address").AutomationId($"lbl_{uniqueId}"),
    TextField(email, setEmail)
        .LabeledBy($"lbl_{uniqueId}")
        .Required()
        .HelpText("We'll never share your email"),
    emailError != null
        ? Text(emailError).Foreground(Theme.Error).LiveRegion(AutomationLiveSetting.Assertive)
        : null
)
```

Key behaviors:
- Generates a unique `AutomationId` for the label and wires `LabeledBy` automatically.
- `required: true` sets `IsRequiredForForm` on the input.
- `error` text, when present, is wrapped in a live region so screen readers announce it
  immediately.
- `helpText` is set as `AutomationProperties.HelpText` on the input.

---

## Breaking Changes

| Change | Impact | Justification |
|--------|--------|---------------|
| `Heading()` / `SubHeading()` now set `HeadingLevel` | UIA tree gains heading annotations where none existed | Screen reader users can now navigate by heading — correct behavior |
| Reduced motion auto-disables transitions | Animations silently disabled when `UISettings.AnimationsEnabled = false` | Required for users who enabled "Turn off all unnecessary animations" in Windows settings |

Both changes improve accessibility correctness and are intentional. Apps that relied on
transitions for layout correctness (bad practice) may need adjustment.

---

## Cross-Framework Comparison

| Concept | Reactor (proposed) | SwiftUI | React / React Aria | Compose |
|---------|----------------|---------|-------------------|---------|
| Accessible name | `.AutomationName()` | `.accessibilityLabel()` | `aria-label` | `contentDescription` |
| Help text | `.HelpText()` | `.accessibilityHint()` | `aria-describedby` | `stateDescription` |
| Heading level | `.HeadingLevel()` | `.accessibilityHeading()` | `role="heading" aria-level` | `heading()` |
| Landmark | `.Landmark()` | N/A (NavigationView) | `role="main/nav/search"` | N/A |
| Live region | `.LiveRegion()` | `.accessibilityLiveRegion()` | `aria-live` | `liveRegion` |
| Hide from AT | `.AccessibilityHidden()` | `.accessibilityHidden()` | `aria-hidden` | `clearAndSetSemantics{}` |
| Required field | `.Required()` | N/A | `aria-required` | N/A |
| Focus trap | `UseFocusTrap()` | `@FocusState` | `FocusTrap` (React Aria) | `FocusRequester` |
| Announce | `UseAnnounce()` | `AccessibilityNotification` | `aria-live` polyfill | `announceForAccessibility` |
| Reduced motion | `UseReducedMotion()` | `@Environment(\.accessibilityReduceMotion)` | `prefers-reduced-motion` | `isReduceMotionEnabled` |
| High contrast | `UseHighContrast()` | `@Environment(\.colorSchemeContrast)` | `prefers-contrast` | N/A (system) |
| Screen reader detect | `UseScreenReaderActive()` | N/A | N/A | N/A |
| Composite hook | `UseAccessibility()` | N/A | N/A | N/A |
| Agent-friendly lint | JSON diagnostic export | N/A | eslint-plugin-jsx-a11y | N/A |

---

## Implementation Phases

### Phase 1 — Core Modifiers (P0)

**Effort:** Low — follows the established modifier pattern exactly.

- Add all new `ElementModifiers` properties (§1.1)
- Add all fluent extension methods (§1.2)
- Update `Reconciler.cs` modifier application (§1.3)
- Update `ModifiersEqual()` (§1.5) and `Merge()` (§1.6)
- Update `Heading()` / `SubHeading()` to set `HeadingLevel` by default (§7.1)

**Files:** `Element.cs`, `ElementExtensions.cs`, `Reconciler.cs`, `Dsl.cs`

### Phase 2 — Focus & Keyboard (P0)

**Effort:** Low.

- `.IsTabStop()`, `.TabIndex()`, `.AccessKey()`, `.TabNavigation()` modifiers (included in §1.1–1.3)
- `.Ref()` modifier for programmatic focus (§3.1)
- Keyboard accelerator tooltip auto-generation (§3.3)

**Files:** `ElementExtensions.cs`, `Reconciler.cs`

### Phase 3 — Live Regions & Announce (P0)

**Effort:** Medium — needs hidden TextBlock management in the host.

- `UseAnnounce()` hook (§2.1)
- Hidden announcement TextBlock in `ReactorHost` (§2.1 implementation)

**Files:** `RenderContext.cs`, `Component.cs`, `ReactorHost.cs` / `ReactorHostControl.cs`

### Phase 4 — Accessibility Hooks (P1)

**Effort:** Medium — needs Windows API integration.

- `UseHighContrast()` hook (§4.1)
- `UseReducedMotion()` hook (§4.2)
- `UseScreenReaderActive()` hook (§5.2)
- `UseAccessibility()` composite hook (§5.1)
- Reconciler: auto-skip transitions when reduced motion is on (§4.3)

**Files:** `RenderContext.cs`, `Component.cs`, `Reconciler.cs`

### Phase 5 — LabeledBy & FormField (P1)

**Effort:** Medium — `LabeledBy` needs post-mount tree walk in the reconciler.

- `LabeledBy(string)` reconciler resolution (§1.4)
- `FormField()` DSL factory (§7.2)

**Files:** `Element.cs`, `ElementExtensions.cs`, `Reconciler.cs`, `Dsl.cs`

### Phase 6 — Runtime Diagnostics (P1)

**Effort:** Medium.

- `EnableAccessibilityDiagnostics()` on host (§6.1)
- Post-reconciliation tree walker with checks A11Y_001–A11Y_008 (§6.1)
- `[A11Y]` tagged log output (§6.1)
- JSON diagnostic export for AI agents (§6.2)

**Files:** new `Reactor/Core/AccessibilityDiagnostics.cs`, `ReactorHost.cs`

### Phase 7 — Test Helpers (P2)

**Effort:** Low.

- `ReactorAccessibilityTree` capture and assertion API (§6.4)

**Files:** new test utility class (possibly in a `Reactor.Testing` namespace)

### Phase 8 — Roslyn Analyzers (P2, Stretch)

**Effort:** High.

- Source analyzers for REACTOR_A11Y_001–003 (§6.3)

**Files:** new `Reactor.Analyzers/` project

### Phase 9 — Documentation & Samples (P0, runs parallel)

- Accessibility guide in `Reactor/Docs/`
- Sample app demonstrating all patterns
- Update `SKILL.md` with new accessibility modifiers
- Update `002-winui3-gap-analysis.md` §16 to reflect changes

---

## Files Changed (Summary)

| File | Changes |
|------|---------|
| `Reactor/Core/Element.cs` | ~15 new properties on `ElementModifiers`; update `Merge()`, `ModifiersEqual()` |
| `Reactor/Elements/ElementExtensions.cs` | ~20 new fluent modifier extension methods |
| `Reactor/Core/Reconciler.cs` | Apply new `AutomationProperties` + keyboard modifiers; `LabeledBy` resolution; skip transitions when reduced motion is on |
| `Reactor/Core/RenderContext.cs` | `UseAnnounce()`, `UseHighContrast()`, `UseReducedMotion()`, `UseScreenReaderActive()`, `UseAccessibility()` hooks |
| `Reactor/Core/Component.cs` | Expose new hooks as `protected` convenience methods |
| `Reactor/Elements/Dsl.cs` | Update `Heading()` / `SubHeading()` defaults; add `FormField()` |
| `Reactor/Hosting/ReactorHost.cs` | `Announce()` method, hidden announcement TextBlock, `EnableAccessibilityDiagnostics()` |
| `Reactor/Core/AccessibilityDiagnostics.cs` | **New** — runtime a11y tree validation + JSON export |
| `SKILL.md` | Document all new accessibility modifiers and hooks |
| `docs/spec/002-winui3-gap-analysis.md` | Update §16 status from Missing → Exposed |

---

## Open Questions

1. **`UseFocusTrap` scope:** Should focus trapping use `LosingFocus` event cancellation (cleanest
   but requires WinUI 1.3+) or a `GotFocus` handler on the parent that redirects back (works
   on all versions but may flash focus)?

2. **`UseScreenReaderActive` reliability:** `AutomationPeer.ListenerExists()` is a heuristic —
   it can return false positives if other UIA clients (test tools, etc.) are running. Is this
   acceptable, or should we also expose the raw heuristic and let devs decide?

3. **JSON diagnostic output path:** `obj/Debug/a11y-diagnostics.json` is convenient for local
   dev but may not work in all build configurations. Should the path be configurable?

4. **`FullDescription` SDK version:** `AutomationProperties.FullDescription` requires Windows
   App SDK 1.4+. Should we guard this with a runtime version check, or just document the
   minimum version requirement?
