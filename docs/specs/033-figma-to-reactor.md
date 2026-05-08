# 033 — Figma-to-Reactor Design Translation

**Status:** Draft
**Date:** 2026-05-04
**Depends on:** [024 — AI Agent Devtools](024-ai-agent-devtools.md), [015 — Styling Design](015-styling-design.md)

---

## Table of Contents

1. [Problem Statement](#1-problem-statement)
2. [Goals and Non-Goals](#2-goals-and-non-goals)
3. [Architecture](#3-architecture)
4. [Figma Context Acquisition](#4-figma-context-acquisition)
5. [Layout Intermediate Representation](#5-layout-intermediate-representation)
6. [Control Mapping](#6-control-mapping)
7. [Token Resolution](#7-token-resolution)
8. [Code Generation](#8-code-generation)
9. [Preview and Iteration Loop](#9-preview-and-iteration-loop)
10. [Validation Model](#10-validation-model)
11. [Fidelity Levels](#11-fidelity-levels)
12. [Scope: Tier 1 Support Matrix](#12-scope-tier-1-support-matrix)
13. [Unsupported / Fallback Behavior](#13-unsupported--fallback-behavior)
14. [Open Questions](#14-open-questions)

---

## 1. Problem Statement

A designer hands a developer a Figma link to a screen built with the
[Windows UI Kit (Community)](https://www.figma.com/design/t7yLwpMUOWJSYt5ahz3ROC/Windows-UI-kit--Community-).
Today the developer manually reads the Figma inspect panel, translates
each element to Reactor code by hand, picks the right `Theme.*` token
for each color, and iterates until the running app visually matches. This
process is slow, error-prone, and wastes the developer's time on
mechanical translation rather than interaction design.

The goal is an AI-agent-driven workflow where the agent reads the Figma
file via MCP, maps elements to Reactor controls using a documented skill,
generates C# code, and launches a live preview — letting the developer
inspect and iterate on a working app within minutes of receiving a design.

## 2. Goals and Non-Goals

### Goals

- **Figma → Reactor in one agent session.** Paste a Figma frame URL, get
  a running Reactor app with correct layout, theming, and controls.
- **Scoped extraction.** The agent reads a specific frame or component
  from Figma, not the entire file. Token limits and reliability demand it.
- **Correct theming from day one.** Generated code uses `Theme.*` tokens
  and `Theme.Ref()` — never hardcoded hex for themed surfaces.
- **Live preview loop.** The agent runs the generated app and can inspect
  it via `mur devtools` (spec 024), iterate, and re-verify.
- **Documented mapping tables.** All Figma → Reactor translations live in
  a skill file the agent loads, not baked into a binary.
- **Graceful degradation.** Unmapped elements produce TODO markers, not
  silent failures or incorrect output.

### Non-Goals

- **Pixel-perfect output on first pass.** The goal is structurally and
  thematically correct code, not visual perfection. The developer refines.
- **Full WinUI kit coverage in v1.** A Tier 1 control set is defined
  (§12). Everything else is explicitly unsupported with fallback markers.
- **Bidirectional sync.** Code → Figma is not in scope.
- **Runtime code generation.** The agent generates static `.cs` files.
  There is no runtime Figma-to-element pipeline.
- **Replacing the designer.** Interaction logic, navigation wiring, and
  data binding are the developer's job. The agent handles visual
  structure.

## 3. Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  Developer pastes Figma URL into agent chat                      │
└─────────────────────────────────────────────────────────────────┘
                     │
                     ▼  (1) Figma MCP: get_file / node extraction
┌─────────────────────────────────────────────────────────────────┐
│  Figma Context Layer                                             │
│  - Parse file key + node IDs from URL                           │
│  - Call figma-developer-mcp to get scoped node tree              │
│  - Extract: layout, fills, strokes, text, instances, variables   │
└─────────────────────────────────────────────────────────────────┘
                     │
                     ▼  (2) Build Layout IR
┌─────────────────────────────────────────────────────────────────┐
│  Layout IR (Intermediate Representation)                         │
│  - Axis, gap, padding, alignment, grow/shrink                   │
│  - Absolute children, z-order, clipping, scroll intent          │
│  - Fills → token candidates, text → typography intent            │
└─────────────────────────────────────────────────────────────────┘
                     │
                     ▼  (3) Map to Reactor primitives
┌─────────────────────────────────────────────────────────────────┐
│  WinUI Control Mapper + Token Resolver                           │
│  - IR containers → VStack / HStack / Grid / Border              │
│  - Figma instances → WinUI controls (Button, TextBox, etc.)    │
│  - Color tokens → Theme.* / Theme.Ref() / AppTheme.Register()  │
│  - Typography → Heading() / Caption() / font helpers            │
│  - Unknown elements → TODO markers                              │
└─────────────────────────────────────────────────────────────────┘
                     │
                     ▼  (4) Emit Reactor C# code
┌─────────────────────────────────────────────────────────────────┐
│  Code Emitter                                                    │
│  - One Component class per top-level frame                      │
│  - AppTheme.Register() for promoted custom tokens               │
│  - Project scaffold via patch.exe --create if needed            │
└─────────────────────────────────────────────────────────────────┘
                     │
                     ▼  (5) Preview + Iterate
┌─────────────────────────────────────────────────────────────────┐
│  mur devtools (spec 024)                                         │
│  - Launch app with devtools enabled                             │
│  - Inspect visual tree structure                                │
│  - Capture screenshots for human review                         │
│  - Agent iterates: edit → reactor.reload → verify               │
└─────────────────────────────────────────────────────────────────┘
```

**Key architectural choice:** The mapping logic is NOT a dedicated binary
or code generator. It is a **skill file** (`skills/figma.md`) that the AI
agent reads and applies. The agent is the code generator. The skill file
provides the translation rules, token tables, and patterns.

## 4. Figma Context Acquisition

### URL parsing

A Figma design URL has this structure:

```
https://www.figma.com/design/<file_key>/<file_name>?node-id=<node_id>&...
```

The agent extracts `file_key` and `node_id` from the URL. The `node_id`
uses Figma's `parent-child` encoding (e.g., `29792-125378` in the URL
maps to node `29792:125378` in the API).

### Figma MCP server

The workflow assumes a Figma MCP server is available in the agent's MCP
configuration. The recommended server is
[`figma-developer-mcp`](https://github.com/GLips/Figma-Context-MCP)
(Framelink), configured with a Figma personal access token:

```json
{
  "mcpServers": {
    "figma": {
      "command": "npx",
      "args": ["-y", "figma-developer-mcp", "--figma-api-key=<TOKEN>", "--stdio"]
    }
  }
}
```

### Scoped extraction (mandatory)

The agent MUST extract a specific frame or component, never the full file.
The Windows UI Kit file is enormous; full-file retrieval exceeds token
limits and produces unreliable results.

**Extraction sequence:**

1. Parse `file_key` and `node_id` from the pasted URL
2. Call the Figma MCP's `get_file` tool with the file URL — the MCP
   server handles node scoping automatically when a URL with node-id is
   provided
3. Receive a simplified layout tree for the selected frame and its
   descendants
4. If the response is still too large, narrow to specific child frames

### What the Figma MCP returns

The `figma-developer-mcp` server simplifies the raw Figma API response,
returning only layout-relevant data: node types, auto-layout properties,
fills, strokes, text content, component references, and Figma variable
bindings. This is intentional — reducing context makes the agent more
accurate.

## 5. Layout Intermediate Representation

Raw Figma nodes map to Reactor primitives via an intermediate layout
model. This IR captures the *intent* of the Figma layout before
committing to specific Reactor elements — handling edge cases that a
direct `FRAME → VStack` mapping would miss.

### IR Node Schema (conceptual)

```
LayoutNode:
  type: container | text | control | image | placeholder
  axis: vertical | horizontal | none       # auto-layout direction
  gap: number                              # spacing between children
  padding: [top, right, bottom, left]
  alignment: [main-axis, cross-axis]       # e.g., center/start/end/stretch
  sizing: [width-mode, height-mode]        # fixed | hug | fill
  fixedSize: [width?, height?]
  minSize: [minWidth?, minHeight?]
  maxSize: [maxWidth?, maxHeight?]
  fills: TokenCandidate[]                  # resolved or unresolved colors
  strokes: TokenCandidate[]
  cornerRadius: [tl, tr, br, bl]
  clipping: bool
  scrollIntent: none | vertical | horizontal | both
  absoluteChildren: LayoutNode[]           # children positioned absolutely
  children: LayoutNode[]                   # auto-layout children
  zOrder: number
  opacity: number
  text: TextInfo?                          # for text nodes
  controlRef: ControlRef?                  # for WinUI control instances
  figmaNodeId: string                      # trace back to source
```

### IR → Reactor mapping rules

| IR Pattern | Reactor Output |
|---|---|
| `container, axis: vertical` | `VStack(gap, children)` |
| `container, axis: horizontal` | `HStack(gap, children)` |
| `container, axis: none, has children` | `Grid(children)` or `Border(child)` depending on child count |
| `container, scrollIntent: vertical` | `ScrollView(VStack(children))` |
| `container + fills/strokes + single child` | `Border(child).Background(fill).WithBorder(stroke)` |
| `container + fills + no children` | `Border().Background(fill).Size(w, h)` (spacer/decorative) |
| `text` | `TextBlock(text).typography(...)` |
| `control (Button)` | `Button(text, onClick)` |
| `image` | `Image(source)` |
| `absoluteChildren present` | Emit as `Canvas(children)` with explicit positioning, flagged for review |

### When the IR cannot map cleanly

- **Mixed absolute + auto-layout children:** Emit the auto-layout
  children in a VStack/HStack; emit absolute children as a TODO marker
  with coordinates noted.
- **Overlapping children (z-order):** Wrap in a `Grid` with overlapping
  cells. Flag for review.
- **Complex constraints (min/max + fill):** Emit `.MinWidth()` /
  `.MaxWidth()` where Reactor supports it; flag unsupported constraints.
- **Groups with transforms:** Preserve as a container (do not flatten)
  and flag for review.

## 6. Control Mapping

### Component identification

Figma instances from the Windows UI Kit carry component metadata (set
name + variant properties). The agent maps by **component set name and
variant properties**, not display name alone — display names vary across
kit versions and locales.

### Tier 1 Control Map (v1)

| Figma Component (WinUI Kit) | Variant Hint | Reactor Element |
|---|---|---|
| Button | Style=Standard | `Button(text, onClick)` |
| Button | Style=Accent | `Button(text, onClick).Resources(accentButtonResources)` |
| Button | Style=Subtle | `Button(text, onClick).Resources(subtleButtonResources)` |
| TextBox | — | `TextField(value, placeholder, onChanged)` |
| CheckBox | — | `CheckBox(text, isChecked, onToggle)` |
| ToggleSwitch | — | `ToggleSwitch(isOn, header, onToggle)` |
| RadioButton | — | `RadioButton(text, isChecked, onChecked)` |
| ComboBox | — | `ComboBox(items, selectedIndex, onSelectionChanged)` |
| Slider | — | `Slider(value, min, max, onValueChanged)` |
| ProgressBar | — | `ProgressBar(value)` |
| ProgressRing | — | `ProgressRing()` |
| InfoBar | Severity=* | `InfoBar(title, message, severity)` |
| NavigationViewItem | — | Navigation item in `NavigationView` shell |
| ListViewItem / card pattern | — | List item in `ListView` |
| Expander | — | `Expander(header, content)` |
| Tooltip | — | `.Tooltip(text)` modifier |

### Non-control elements

| Figma Pattern | Reactor Output |
|---|---|
| Frame with background fill, rounded corners, border | `Border(child).Background(token).WithBorder(stroke).CornerRadius(cr)` |
| Frame with "Card" component style | `Border(child).Background(Theme.CardBackground).WithBorder(Theme.CardStroke).CornerRadius(controlCR)` |
| Divider line | `Border().Background(Theme.DividerStroke).Height(1)` |
| Icon (from Segoe Fluent Icons) | `FontIcon(glyph)` or `SymbolIcon(symbol)` |
| Image / photo placeholder | `Image(source).Stretch(Stretch.UniformToFill)` |

### Unsupported instances (v1)

Any Figma instance not in the Tier 1 map is emitted as:

```csharp
// TODO: Unsupported Figma component "TreeView" (node: 29792:125400)
// Placeholder — replace with actual implementation
Border(
    TextBlock("TreeView placeholder").Foreground(Theme.SecondaryText)
).Background(Theme.ControlFill).Padding(16).CornerRadius(4)
```

## 7. Token Resolution

### Fallback ladder (strict order)

When the agent encounters a color, brush, or style value from Figma, it
resolves using this ladder — top to bottom, first match wins:

| Priority | Condition | Output |
|---|---|---|
| 1 | Value matches a known `Theme.*` semantic token | `Theme.PrimaryText`, `Theme.Accent`, etc. |
| 2 | Value matches a known WinUI resource key | `Theme.Ref("ResourceKeyBrush")` |
| 3 | Value is a Figma variable bound to a WinUI design token | Map variable name → `Theme.*` or `Theme.Ref()` |
| 4 | Value is a repeated semantic color (appears 3+ times, distinct role) | Promote to `AppTheme.Register()` custom brush with Light/Dark/HC variants |
| 5 | Value is a one-off literal color | Emit as hex string, flagged with comment: `// TODO: non-semantic color — consider Theme token` |

### Known token mapping (WinUI Kit Figma variables → Reactor)

| Figma Variable Path | Reactor Token |
|---|---|
| `Fill Color/Text/Primary` | `Theme.PrimaryText` |
| `Fill Color/Text/Secondary` | `Theme.SecondaryText` |
| `Fill Color/Text/Tertiary` | `Theme.TertiaryText` |
| `Fill Color/Text/Disabled` | `Theme.DisabledText` |
| `Fill Color/Accent/Default` | `Theme.Accent` |
| `Fill Color/Accent/Secondary` | `Theme.AccentSecondary` |
| `Fill Color/Accent/Tertiary` | `Theme.AccentTertiary` |
| `Fill Color/Accent/Disabled` | `Theme.AccentDisabled` |
| `Fill Color/Control/Default` | `Theme.ControlFill` |
| `Fill Color/Control/Secondary` | `Theme.ControlFillSecondary` |
| `Fill Color/Subtle/Secondary` | `Theme.SubtleFill` |
| `Fill Color/Card Background/Default` | `Theme.CardBackground` |
| `Fill Color/Layer/Default` | `Theme.LayerFill` |
| `Fill Color/Solid Background/Base` | `Theme.SolidBackground` |
| `Fill Color/Smoke/Default` | `Theme.SmokeFill` |
| `Stroke Color/Card/Default` | `Theme.CardStroke` |
| `Stroke Color/Surface/Default` | `Theme.SurfaceStroke` |
| `Stroke Color/Divider/Default` | `Theme.DividerStroke` |
| `Stroke Color/Control/Default` | `Theme.ControlStroke` |
| `Fill Color/System/Attention` | `Theme.SystemAttention` |
| `Fill Color/System/Success` | `Theme.SystemSuccess` |
| `Fill Color/System/Caution` | `Theme.SystemCaution` |
| `Fill Color/System/Critical` | `Theme.SystemCritical` |

### Typography mapping

| Figma Text Style | Reactor Output |
|---|---|
| Caption (12px, Regular, 16px line) | `.Caption()` |
| Body (14px, Regular, 20px line) | Default TextBlock — no modifier needed |
| Body Strong (14px, SemiBold, 20px line) | `.SemiBold()` |
| Subtitle (20px, SemiBold, 28px line) | `.Heading()` |
| Title (28px, SemiBold, 36px line) | `.FontSize(28).SemiBold()` |
| Title Large (40px, SemiBold, 52px line) | `.FontSize(40).SemiBold()` |
| Display (68px, SemiBold, 92px line) | `.FontSize(68).SemiBold()` |

### Corner radius mapping

| Figma Value | Reactor Output |
|---|---|
| 4px | `ThemeResource.CornerRadius("ControlCornerRadius")` |
| 8px | `ThemeResource.CornerRadius("OverlayCornerRadius")` |
| Other values | Flag as non-standard: `// TODO: non-standard corner radius` |

## 8. Code Generation

### Project structure

When generating from scratch (no existing project):

```
MyApp/
├── MyApp.sln
├── MyApp.csproj
├── Program.cs              # ReactorApp.Run<MainComponent>(...)
├── Components/
│   ├── MainComponent.cs    # Top-level component from Figma frame
│   └── CardComponent.cs    # Extracted sub-components
├── Theme/
│   └── AppBrushes.cs       # AppTheme.Register() for custom brand colors
└── Assets/
    └── (exported images)
```

Scaffold with `patch.exe --create MyApp`, then generate component files.

### Code style rules

- Use `VStack` / `HStack` for auto-layout containers, not raw `StackPanel`
- Use `Theme.*` tokens, never hardcoded hex for themed surfaces
- Use `.Resources()` for button variants (accent, subtle), not direct `.Background()`
- Use `ControlCornerRadius` / `OverlayCornerRadius` theme resources, not number values
- Use semantic typography helpers (`.Caption()`, `.Heading()`) where they map
- Round spacing values to the 4px grid
- Emit TODO comments for anything the agent cannot confidently map
- Include the source Figma node ID in a comment for traceability

### Example: generated component

```csharp
// Generated from Figma frame "Settings Page" (node: 29792:125378)
// Fidelity: Level 2 (structure + theming + controls)

public class SettingsPage : Component
{
    public override Element Render()
    {
        var controlCR = ThemeResource.CornerRadius("ControlCornerRadius");

        return ScrollView(
            VStack(16,
                // Header
                TextBlock("Settings").Heading(),

                // Account card
                Border(
                    HStack(12,
                        // TODO: Avatar image — replace with actual source
                        Border()
                            .Background(Theme.Accent)
                            .Size(48, 48)
                            .CornerRadius(24),
                        VStack(2,
                            TextBlock("User Name").SemiBold(),
                            TextBlock("user@example.com")
                                .Foreground(Theme.SecondaryText)
                                .Caption()
                        ).CenterVertical()
                    ).Padding(16)
                )
                .Background(Theme.CardBackground)
                .WithBorder(Theme.CardStroke, 1)
                .CornerRadius(controlCR.TopLeft),

                // Preferences section
                TextBlock("Preferences")
                    .Foreground(Theme.SecondaryText)
                    .Caption()
                    .Margin(0, 16, 0, 4),

                ToggleSwitch(false, header: "Dark mode"),
                ToggleSwitch(true, header: "Notifications"),

                // Actions
                Button("Sign out", () => { })
            ).Padding(24).MaxWidth(600)
        );
    }
}
```

## 9. Preview and Iteration Loop

### Agent workflow (primary — MCP-driven)

The agent uses `mur devtools` (spec 024) for the preview and iteration
loop. This is the recommended path for AI agents because it provides
stable MCP sessions, structured tree inspection, and coordinated
reload.

```
1. Agent generates code → writes .cs files
2. dotnet run -- --devtools run
3. mur devtools tree              # verify structure
4. mur devtools screenshot        # capture for comparison
5. Agent identifies discrepancies
6. Agent edits code
7. mur devtools reload            # rebuild + relaunch, same MCP port
8. Repeat from step 3
```

### Developer workflow (secondary — watch mode)

For a human developer iterating without MCP automation, `dotnet watch`
provides a familiar hot-reload experience:

```bash
dotnet watch run --project MyApp/MyApp.csproj
```

**Important:** `dotnet watch` kills and restarts the process on rude
edits, which tears down any active MCP/devtools session (spec 024 §6
discusses this). Use `dotnet watch` only when you don't need MCP
continuity.

### Comparison strategy

Validation is **structural first, visual second:**

1. **Primary:** `mur devtools tree` — verify that the generated control
   hierarchy matches the Figma frame structure. Check element types,
   nesting depth, text content.
2. **Secondary:** `mur devtools screenshot` — capture the rendered app
   and present to the developer for visual review.
3. **Optional:** If the agent has vision capabilities, it may compare
   the screenshot to the Figma frame image. This is explicitly optional
   and not required for the workflow.

## 10. Validation Model

### Structural validation (automated)

The agent can verify its own output by inspecting the running app:

| Check | Tool | Pass Condition |
|---|---|---|
| App launches without error | `mur devtools version` | Returns build tag |
| Correct number of top-level children | `mur devtools tree` | Child count matches Figma frame |
| Text content matches | `mur devtools tree --view full` | TextBlock values match Figma text |
| Theme tokens used (no hex on themed surfaces) | Code review | No hardcoded hex in `.Background()` / `.Foreground()` |
| Controls are correct type | `mur devtools tree` | Button/TextBox/ToggleSwitch types present |

### Visual validation (human-assisted)

| Check | Method | Who |
|---|---|---|
| Layout spacing matches design | Screenshot + developer eyes | Developer |
| Colors match design intent | Screenshot in Light + Dark mode | Developer |
| High Contrast works | Switch to HC, screenshot | Developer |
| Typography looks correct | Screenshot at 100% + 200% scale | Developer |

## 11. Fidelity Levels

Generated code targets a declared fidelity level, set by the agent based
on the complexity of the Figma frame and the agent's confidence:

| Level | Name | Includes | Excludes |
|---|---|---|---|
| 1 | Structure | Layout hierarchy, text, spacing, sizing | Theming, controls, assets |
| 2 | Themed | Level 1 + Theme tokens, control mapping, corner radii, typography | Interaction states, variant logic, assets |
| 3 | Complete | Level 2 + assets, interaction stubs, all mapped controls | Custom animations, data binding, navigation wiring |

The agent declares the fidelity level in a comment at the top of each
generated file. TODO markers indicate what's needed to reach the next
level.

## 12. Scope: Tier 1 Support Matrix

### Supported in v1

**Layout:** VStack, HStack, Border, Grid (simple), ScrollView, Canvas (flagged)

**Controls:** Button (3 variants), TextBox, CheckBox, ToggleSwitch, RadioButton, ComboBox, Slider, ProgressBar, ProgressRing, InfoBar, Expander, Tooltip

**Surfaces:** Card (Border + CardBackground), Dialog shell, Flyout shell

**Text:** All WinUI type ramp styles (Caption through Display)

**Theming:** Full token ladder (§7), AppTheme.Register() for custom brushes, ControlCornerRadius / OverlayCornerRadius

### NOT supported in v1

TreeView, TabView, MenuBar, CommandBar, CalendarView, DatePicker, TimePicker, MediaPlayerElement, MapControl, WebView2, RichEditBox, custom drawn elements, Segoe Fluent Icons (mapped by glyph), complex data templates.

These emit TODO placeholders with the Figma node ID.

## 13. Unsupported / Fallback Behavior

Every unmapped element gets a deterministic fallback:

```csharp
// TODO [Figma node 29792:125400]: Unsupported component "TreeView"
// Manual implementation required — see WinUI docs for TreeView API
Border(
    TextBlock("[TreeView]").Foreground(Theme.SecondaryText).Caption()
).Background(Theme.ControlFill).Padding(12).CornerRadius(4)
```

### Asset handling

| Figma Asset Type | Fallback |
|---|---|
| Icon (from Segoe Fluent Icons set) | `FontIcon(glyph)` if glyph is identifiable; else `SymbolIcon(Symbol.Placeholder)` |
| Raster image | Export as PNG to `Assets/` folder, reference via `Image("ms-appx:///Assets/name.png")` |
| SVG vector | Export as PNG (WinUI SVG support is limited); flag for manual SVG handling |
| Avatar / photo placeholder | Colored `Border` with size, flagged as placeholder |

### Manual review markers

Generated code includes a summary of unresolved items at the top of the
file:

```csharp
// ═══════════════════════════════════════════════════════════
// FIGMA TRANSLATION SUMMARY
// Source: https://www.figma.com/design/t7yLwpMUOWJSYt5ahz3ROC/...
// Fidelity: Level 2
// Resolved: 24/28 elements
// TODOs: 4 items requiring manual review
//   - Line 45: Unsupported component "TreeView"
//   - Line 72: Non-semantic color #E8D5F5 — consider Theme token
//   - Line 89: Avatar image placeholder
//   - Line 103: Absolute-positioned element (Canvas fallback)
// ═══════════════════════════════════════════════════════════
```

## 14. Open Questions

1. **Figma variable access:** The Figma REST API exposes variables only
   to Enterprise plans. If the agent cannot read variables, it falls back
   to matching hex values against known WinUI token colors. How reliable
   is this fallback in practice?

2. **Icon glyph mapping:** The WinUI Figma kit uses named icons that
   correspond to Segoe Fluent Icons glyphs. A glyph lookup table would
   improve accuracy. Should this be part of the skill file or a separate
   asset?

3. **Multi-page designs:** When a Figma file contains multiple pages
   (e.g., different screens of an app), should the agent generate one
   Component per page, or prompt the developer to select which pages to
   generate?

4. **Dark mode preview:** The WinUI Figma kit has Light and Dark
   variants. Should the agent generate a single component and verify it
   renders correctly in both themes, or use the Dark variant only as a
   visual reference?

5. **Design handoff format:** If the Figma MCP is not available, should
   there be a fallback path where the developer exports a Figma JSON
   file and the agent reads it locally?

---

## Appendix A: Live Sync Architecture

### Background — FigmaBridge removal

An earlier iteration included a **FigmaBridge** relay server
(`tools/FigmaBridge`) that used a Figma plugin + WebSocket + localhost
HTTP MCP to push design changes to agents in real-time. It was removed
due to security concerns: `Access-Control-Allow-Origin: *`, arbitrary
filesystem writes, unauthenticated endpoints, and shell command
injection surfaces. See commit `e91c167` for details.

### Figma Webhooks V2 — not viable for live sync

The Figma Webhooks V2 API was evaluated as a replacement. Key findings:

| Event | Latency | Suitability |
|---|---|---|
| `FILE_UPDATE` | ~30 min after inactivity | ❌ Too slow for live sync |
| `FILE_VERSION_UPDATE` | Immediate (designer clicks "Save version") | ✅ Good for explicit gates |
| `FILE_DELETE` | Immediate | N/A |
| `LIBRARY_PUBLISH` | Immediate | ✅ For design system workflows |

`FILE_UPDATE` is heavily debounced (30-minute inactivity window) and its
payload is minimal (no diff, no changed nodes). It is designed for "a
work session ended" notifications, not real-time change detection.

Webhooks also cannot scope below file level — no node/frame targeting.

### Adopted approach: polling `lastModified`

The `mur figma watch` CLI command replaces the bridge by polling the
Figma REST API's `lastModified` timestamp at a configurable interval:

```
mur figma watch <figma-url> [--interval 10]
```

**How it works:**

1. Parses `file_key` and `node_id` from the Figma URL
2. Calls `GET /v1/files/:key?depth=1` to check `lastModified`
   (lightweight — no document tree traversal)
3. When the timestamp advances, emits a JSON event to stdout
4. The agent reads the event and calls `figma-get_figma_data` via MCP
   to fetch the updated design tree
5. The agent diffs and regenerates code as needed

**Security properties:**

- No open ports — runs as a local CLI process
- No CORS surface — no HTTP server
- Auth via `FIGMA_API_KEY` env var — same scoped PAT the Figma MCP uses
- No filesystem writes — only emits to stdout
- No shell execution — agent handles code generation natively

**Stdout event format:**

```json
{"event":"changed","fileKey":"abc123","nodeId":"29792:125378",
 "fileName":"My Design","lastModified":"2026-05-08T10:30:00Z",
 "version":"123456","figmaUrl":"https://www.figma.com/design/abc123?node-id=29792-125378"}
```

Status messages go to stderr, keeping stdout clean for machine parsing.

### Future: webhook-gated sync (Tier 2)

For team workflows where a designer explicitly signals "design is ready":

1. Register a `FILE_VERSION_UPDATE` webhook via the Figma API
2. Designer saves a named version in Figma → webhook fires immediately
3. CI/agent picks up the event and triggers a one-shot translation

This is documented but not yet implemented. `FILE_VERSION_UPDATE` has no
debounce and fires immediately, making it suitable for explicit handoff
gates. It requires a publicly reachable endpoint (ngrok, CI webhook
receiver, etc.) and `webhooks:write` token scope.
