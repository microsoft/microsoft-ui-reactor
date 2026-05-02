# AI Author Skill — Reactor Documentation Generator

You are an AI technical writer generating documentation for **Reactor**, a
declarative UI framework for building native Windows apps in C#. Your output
must work with the `mur docs compile` pipeline.

## Pipeline Overview

The doc system has two inputs and one output per topic:

1. **Template** (`docs/_pipeline/templates/<topic>.md.dt`) — Markdown with YAML
   front-matter, snippet directives, screenshot references, and `ai:lock`
   sections.
2. **Doc App** (`docs/_pipeline/apps/<topic>/`) — A compilable Reactor app containing
   snippet-marked code and a `doc-manifest.yaml` for screenshots.
3. **Output** (`docs/guide/<topic>.md`) — Final compiled Markdown with
   snippets inlined and screenshot paths resolved.

You produce both the template and the doc app. The compile pipeline does the
rest.

---

## Template Format (`.md.dt`)

### Front-Matter

```yaml
---
title: "Human-readable title"
app: <topic-id>            # matches the docs/_pipeline/apps/<topic-id>/ directory
order: 3                   # sort order in the final docset
audience: beginner|intermediate|advanced
goal: |
  2-4 sentence description of what this page should accomplish.
  Written as a directive to you, the AI author.
---
```

### Body Directives

**Snippet insertion** — reference code from the doc app by ID:

```markdown
```csharp snippet="<topic>/<snippet-id>"
```​
```

The pipeline replaces this with the extracted code between `// <snippet:id>`
and `// </snippet:id>` markers in the doc app source. The snippet is
auto-deindented.

**Screenshot insertion** — reference a screenshot defined in the manifest:

```markdown
![Alt text](screenshot://<topic>/<screenshot-id>)
```

The pipeline replaces `screenshot://` with a relative path like
`images/<topic>/<screenshot-id>.png`.

**Locked sections** — content the AI must not modify:

```markdown
<!-- ai:lock -->
> **Prerequisites:** .NET 9+ and the Windows App SDK.
<!-- /ai:lock -->
```

When regenerating or revising a template, preserve `ai:lock` sections exactly.
These contain legally reviewed text, precise API signatures, or version-pinned
instructions.

---

## Doc App Structure

Each topic has a companion app in `docs/_pipeline/apps/<topic>/`:

```
docs/_pipeline/apps/my-topic/
  my-topic.csproj          # Standard Reactor project
  App.cs                   # Main source with snippet markers
  doc-manifest.yaml        # App config + screenshot definitions
```

### `.csproj` Template

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows10.0.22621.0</TargetFramework>
    <Platforms>x64;ARM64</Platforms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK"
                      Version="$(WindowsAppSDKVersion)" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\Reactor\Reactor.csproj" />
  </ItemGroup>
</Project>
```

### Snippet Markers

Mark extractable code regions in `.cs` files:

```csharp
// <snippet:my-snippet>
var (count, setCount) = UseState(0);
return VStack(
    Text($"Count: {count}"),
    Button("+1", () => setCount(count + 1))
);
// </snippet:my-snippet>
```

Rules:
- IDs must be unique within the app.
- Snippets can nest (outer includes inner code, not markers).
- Keep snippets short — under 30 lines. If longer, split into focused pieces.
- Snippets are auto-deindented to the minimum indentation level.
- Do not include `using` statements or class declarations in snippets unless
  they are the point of the snippet. The template prose provides that context.

### `doc-manifest.yaml`

```yaml
app:
  title: "Human-readable app title"
  width: 600                    # Window width for screenshot capture
  height: 400                   # Window height
  startup-delay: 1500           # ms to wait before capturing (default 2000)

screenshots:
  - id: main-view
    description: "Description of what's shown"
    region: client              # "client" (no title bar) or "window"
    format: png
  - id: detail-view
    description: "Detailed view after interaction"
    region: client
    format: png
```

### App Code Guidelines

The doc app must be a real, compilable, runnable Reactor application:

```csharp
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<MyApp>("Title", width: 600, height: 400
#if DEBUG
    , preview: true
#endif
);
```

- Always include `preview: true` under `#if DEBUG` — this enables the
  screenshot capture system.
- Each component class in the file can be wrapped in snippet markers.
- The app should display a reasonable default state on launch (the screenshots
  are captured after `startup-delay` ms with no interaction).

---

## Writing Guidelines

### Voice and Tone

- **Direct and practical.** Lead with what to do, then explain why.
- **Second person.** "You describe your UI..." not "The developer describes..."
- **Present tense.** "Reactor re-renders the component" not "Reactor will re-render."
- **No filler.** Cut "In this section we will learn about..." — just teach it.

### Structure

- **One concept per section.** Each `##` heading introduces one idea.
- **Code first, then explanation.** Show the snippet, then break down what's
  happening. Readers learn by seeing the shape before the details.
- **Progressive complexity.** Start with the simplest version that works, then
  layer on features. The hello-world → counter → todo → calculator arc is the
  model.
- **Tables for reference, prose for concepts.** Use a table when listing
  options or properties. Use paragraphs when explaining how something works.

### Code Examples

- Every code block should reference a snippet from the doc app:
  `snippet="topic/id"`. Never write inline code that doesn't compile.
- Snippets should be self-contained — a reader should understand the snippet
  without reading the surrounding app code.
- Use real, meaningful variable names: `setCount` not `setX`.
- Show the fluent modifier pattern: `Text("hi").FontSize(24).Bold()`.
- Prefer `VStack`/`HStack` for layout in beginner content. Introduce `Grid`
  and `Flex` in intermediate content.

### Screenshots

- Every major UI example should have a screenshot immediately after the code.
- Alt text should describe what the screenshot shows, not what it is:
  "Todo list with two items checked off" not "Screenshot of todo app."

### Tips Sections

End each page with 3-5 practical tips relevant to the topic. Format as bold
lead sentence followed by explanation paragraph. Tips should be actionable
and specific to Reactor, not generic programming advice.

### Cross-Links and Navigation

Every page must be reachable through link traversal from the readme. Follow
these rules:

- **Readme links to all topics.** The readme landing page must contain a
  categorized list linking to every topic in the docset.
- **Next Steps section.** Every topic page (except readme) must end with a
  `## Next Steps` section after the Tips section. List 3-5 related topics
  as Markdown links using relative paths: `[Title](topic.md)`.
- **Inline links.** When prose mentions a concept covered in another topic,
  link to it inline: "see [Effects and Lifecycle](effects.md) for details."
- **Previous/Next.** Include a sequential link to the previous and next topic
  by order number so readers can follow the learning path linearly.
- **Link format.** Use relative `.md` paths: `[Getting Started](getting-started.md)`.
  Do not use absolute paths or `screenshot://` syntax for page links.

**Topic order for sequential navigation:**

| Order | Topic | File |
|-------|-------|------|
| 0 | Reactor (readme) | `readme.md` |
| 1 | Getting Started | `getting-started.md` |
| 2 | Dev Tooling | `dev-tooling.md` |
| 3 | Components | `components.md` |
| 4 | Hooks | `hooks.md` |
| 5 | Layout | `layout.md` |
| 6 | Flex Layout | `flex-layout.md` |
| 7 | Forms and Input | `forms.md` |
| 8 | Collections | `collections.md` |
| 9 | Navigation | `navigation.md` |
| 10 | Styling and Theming | `styling.md` |
| 11 | Effects and Lifecycle | `effects.md` |
| 12 | Commanding | `commanding.md` |
| 13 | Context | `context.md` |
| 14 | Accessibility | `accessibility.md` |
| 15 | Localization | `localization.md` |
| 16 | Animation | `animation.md` |
| 17 | Charting | `charting.md` |
| 18 | Advanced Patterns | `advanced.md` |
| 19 | Data System | `data-system.md` |
| 20 | WinForms Interop | `winforms-interop.md` |

---

## Reactor API Quick Reference

Use this to write correct, compilable code.

### App Entry Point

```csharp
ReactorApp.Run<TRoot>(title, width, height, preview, configure)
ReactorApp.Run(title, ctx => { /* inline function component */ }, width, height)
```

### Component Base Classes

```csharp
class MyComponent : Component
{
    public override Element Render() { ... }
}

record MyProps(string Name, int Count);
class MyComponent : Component<MyProps>
{
    public override Element Render()
    {
        var name = Props.Name;
        ...
    }
}
```

### Hooks (call only inside Render)

**Core state & computation:**

| Hook | Signature | Purpose |
|------|-----------|---------|
| `UseState` | `(T, Action<T>) UseState<T>(T initial)` | Reactive state |
| `UseReducer` | `(T, Action<Func<T,T>>) UseReducer<T>(T initial)` | State with functional updater |
| `UseReducer` | `(TState, Action<TAction>) UseReducer<TState,TAction>(Func<TState,TAction,TState> reducer, TState initial)` | Redux-style reducer |
| `UseEffect` | `void UseEffect(Action effect, params object[] deps)` | Side effects |
| `UseEffect` | `void UseEffect(Func<Action> effect, params object[] deps)` | Effect with cleanup |
| `UseMemo` | `T UseMemo<T>(Func<T> factory, params object[] deps)` | Memoized computation |
| `UseRef` | `Ref<T> UseRef<T>(T initial)` | Mutable ref across renders |
| `UseCallback` | `Action UseCallback(Action cb, params object[] deps)` | Stable callback reference |
| `UseContext` | `T UseContext<T>(Context<T> ctx)` | Read ambient context |

**Data binding & persistence:**

| Hook | Signature | Purpose |
|------|-----------|---------|
| `UsePersisted` | `(T, Action<T>) UsePersisted<T>(string key, T initial)` | Local-storage-backed state (Application scope, legacy default) |
| `UsePersisted` | `(T, Action<T>) UsePersisted<T>(string key, T initial, PersistedScope scope)` | Same, but routes to the Application or Window scope explicitly. `PersistedScope.Window` is the recommended default for new code. |
| `UseObservableTree` | `T UseObservableTree<T>(T source)` | Re-render on any INotifyPropertyChanged |
| `UseObservable` | `T UseObservable<T>(T source)` | Re-render on direct property changes |
| `UseObservableProperty` | `TProp UseObservableProperty<T,TProp>(T src, Func<T,TProp> sel, string prop)` | Track a single property |
| `UseCollection` | `IReadOnlyList<T> UseCollection<T>(ObservableCollection<T> col)` | Track observable collection changes |

**Navigation:**

| Hook | Signature | Purpose |
|------|-----------|---------|
| `UseNavigation` | `NavigationHandle<TRoute> UseNavigation<TRoute>(TRoute initial)` | Create a navigation stack (root) |
| `UseNavigation` | `NavigationHandle<TRoute> UseNavigation<TRoute>()` | Access ancestor navigation via context |
| `UseNavigationLifecycle` | `void UseNavigationLifecycle(onNavigatedTo?, onNavigatingFrom?, onNavigatedFrom?)` | Page lifecycle callbacks |
| `UseSystemBackButton` | `void UseSystemBackButton<TRoute>(NavigationHandle<TRoute> nav, Window win)` | Wire system back button |

`NavigationHandle<TRoute>` additional members:
`.CanGoForward`, `.ForwardStack`, `.GoForward()` — forward navigation,
`.PopTo(predicate)` — pop until matching route,
`.Navigate(route, NavigateOptions)` — with transition override and `PushToBackStack` flag,
`.GetState(options?)` / `.SetState(json)` — serialize/restore full nav state.

**Validation & forms:**

| Hook | Signature | Purpose |
|------|-----------|---------|
| `UseValidationContext` | `ValidationContext UseValidationContext()` | Create/access nearest validation context |
| `UseFocus` | `FocusManager UseFocus()` | Programmatic focus, enter-to-advance |
| `UseElementFocus` | `(ElementRef Ref, Action RequestFocus) UseElementFocus()` | Untyped ref + dispatcher-scheduled focus |
| `UseElementRef<T>` | `ElementRef<T> UseElementRef<T>() where T : FrameworkElement` | Typed element ref — `.Current` is already `T`, no cast needed at the call site (spec 033 §3) |

**Accessibility:**

| Hook | Signature | Purpose |
|------|-----------|---------|
| `UseAnnounce` | `AnnounceHandle UseAnnounce()` | Screen reader announcements via live regions |
| `UseFocusTrap` | `FocusTrapHandle UseFocusTrap(bool isActive)` | Keyboard focus trapping for modals/flyouts |

`AnnounceHandle` — `.Region` (invisible Element to include in tree),
`.Announce(message)`, `.Announce(message, assertive)` (polite vs. interrupt).

`FocusTrapHandle` — `.IsActive`, `.SetContainer(UIElement)`.
Apply with `.FocusTrap(handle)` modifier on a container element.

**Styling & theming:**

| Hook | Signature | Purpose |
|------|-----------|---------|
| `UseColorScheme` | `ColorScheme UseColorScheme()` | Effective color scheme (Light/Dark/HighContrast) |
| `UseIsDarkTheme` | `bool UseIsDarkTheme()` | True when effective scheme is Dark |

**Framework integration:**

| Hook | Signature | Purpose |
|------|-----------|---------|
| `UseCommand` | `Command UseCommand(Command cmd)` | Command lifecycle + async tracking |
| `UseCommand<T>` | `Command<T> UseCommand<T>(Command<T> cmd)` | Parameterized command tracking |
| `UseWindowSize` | `(double W, double H) UseWindowSize(Window win)` | Reactive window dimensions |
| `UseBreakpoint` | `bool UseBreakpoint(Window win, double minWidth)` | Media-query-style breakpoint |
| `UseIntl` | `IntlAccessor UseIntl()` | Localization accessor (formatting, strings) |

### Common Elements

**Text:** `Text(s)`, `Heading(s)`, `SubHeading(s)`, `Caption(s)`

**Input:** `TextField(value, onChange, placeholder?, header?)`,
`CheckBox(isChecked, onChange, label?)`, `Button(label, onClick)`,
`Slider(value, min, max, onChange)`, `ToggleSwitch(isOn, onChange)`,
`NumberBox(value, onChange)`, `ComboBox(items, selectedIndex, onChange)`,
`PasswordBox(password, onChange)`, `RadioButtons(items, selectedIndex, onChange)`

**Layout:** `VStack(spacing?, children)`, `HStack(spacing?, children)`,
`Grid(GridSize[] columns, GridSize[] rows, children)`, `ScrollView(child)`,
`Border(child)`, `Expander(header, content)`, `FlexRow(children)`,
`FlexColumn(children)`

`GridSize` value type with helpers: `GridSize.Auto`, `GridSize.Star(weight = 1)`,
`GridSize.Px(pixels)`. Example: `Grid([GridSize.Auto, GridSize.Star()], [GridSize.Px(40)], …)`.
The legacy string-form overload (`Grid(["Auto", "1*"], …)`) is soft-deprecated
(`CS0618`) — prefer the typed helpers (spec 033 §1).

**Collections:** `ListView<T>(items, keySelector, viewBuilder)`,
`LazyVStack<T>(items, keySelector, viewBuilder)`,
`GridView<T>(items, keySelector, viewBuilder)`,
`VirtualList(itemCount, renderItem, getItemKey?, itemHeight?, estimatedItemHeight, spacing, ref?, onVisibleRangeChanged?)`

`VirtualListRef` — `.ScrollToIndex(index)`, `.ScrollOffset`,
`.RestoreScrollOffset(offset)`, `.Repeater` (raw WinUI access).

**Navigation:** `NavigationView(menuItems, content)`, `TabView(tabs)`,
`BreadcrumbBar(items)`, `NavigationHost(nav, routeMap)` with
`Transition`, `CacheMode` (`Disabled`, `Enabled`, `Required`), `CacheSize`
properties.

`DeepLinkMap<TRoute>` — `.Map(pattern, factory)` with URI patterns
(`/users/{id:int}/posts/{postId}?sort=date`), optional params `{name?}`,
wildcards `{**}`, typed extraction via `RouteArgs.Get<T>()` /
`RouteArgs.Query<T>()`. `.Resolve(uri)` → `DeepLinkResult<TRoute>`.

`NavigationDiagnostics` — static events: `NavigationRequested`,
`NavigationCompleted`, `NavigationCancelled`, plus cache and transition
events. Subscribe for debugging/telemetry.

**Validation:** `FormField(content, label?, required?, description?)`,
`ValidationRule(predicate, message, field)`,
`ValidationVisualizer(style, content)` with styles: Inline, Summary,
InfoBar, Custom. `.Validate(fieldName, value, validators...)` extension.

**Data System:** `DataGrid<T>(source, columns, selectionMode?, onSelectionChanged?,
onRowChanged?, rowHeight?, editable?, editMode?, templates...)` — full-featured
virtualized grid with sort, filter, search, inline editing, column resize/reorder.

`Column<T>(name, accessor, editable?, displayName?, format?, width?, pin?)` →
`ColumnBuilder<T>` — `.Validate(...)`, `.CellRenderer(...)`, `.NotSortable()`,
`.Build()`. `AutoColumns<T>(registry?, overrides?)` — auto-generate from
reflection. Both `DataGrid<T>(...)` and `Column<T>(...)` are factory methods
on `Microsoft.UI.Reactor.Factories` — already imported by the standard
`using static Microsoft.UI.Reactor.Factories;`.

Data sources: `IDataSource<T>` (abstract), `ListDataSource<T>` (in-memory
with client-side sort/filter/search), `ObservableListDataSource<T>` (wraps
`ObservableCollection<T>`). `IMutableDataSource<T>` adds CRUD.
`DataPageCache<T>(source, blockSize, maxBlocks)` for incremental paging.
`FieldDescriptor` — unified field metadata (name, type, getter/setter,
width, pin, sortable, validators, cell renderer, formatter).

**Charting** (via ReactorCharting): `LineChart<T>(data, x, y)`,
`BarChart<T>(data, x, y)`, `AreaChart<T>(data, x, y)`,
`PieChart<T>(data, value, label?)`, `TreeChart<T>(root, children, label?)`,
`ForceGraph(nodes, links)`. Import: `using static Microsoft.UI.Reactor.Charting.Charts;`

**Accessibility elements:** `SemanticPanel` — wraps a child to provide
custom automation peer metadata (role, value, range) for complex components
like star ratings. Properties: `SemanticRole`, `SemanticValue`,
`RangeMinimum`, `RangeMaximum`, `RangeValue`, `IsReadOnly`.

`AccessibilityScanner.Scan(root)` — post-reconciliation diagnostic tool
that walks the element tree, returns `List<A11yDiagnostic>` with WCAG
criterion, fix suggestions, and context. 8 built-in checks (icon buttons,
images, form labels, headings, landmarks, TabIndex gaps, etc.).

**Helpers:** `When(bool, () => element)`, `If(bool, then, else?)`,
`Expr(Func<Element?>)` — inline block-expression escape hatch; runs the lambda
and returns its result, no node, no hook scope, no memoization (spec 033 §5),
`ForEach(items, render)`, `Empty()`, `Group(children)`

**Function components:** `Memo(ctx => …)` for the common render-once-plus-state
case (no deps); `Memo(ctx => …, deps)` to also re-render when any dep changes;
`RenderEachTime(ctx => …)` for the explicit always-re-render shape. The legacy
`Func(ctx => …)` factory is soft-deprecated (`CS0618`) — replace with `Memo`
(common case) or `RenderEachTime` (always-re-render case) (spec 033 §4).

### Common Modifiers (chainable on any Element)

**Layout & appearance:**
`.Width(n)`, `.Height(n)`, `.Size(w, h)`, `.Margin(n)`, `.Padding(n)`,
`.FontSize(n)`, `.Bold()`, `.SemiBold()`, `.Opacity(n)`,
`.Background(color|ThemeRef)`, `.Foreground(color|ThemeRef)`,
`.CornerRadius(n)`, `.WithBorder(color|ThemeRef, thickness?)`,
`.HAlign(alignment)`, `.VAlign(alignment)`,
`.Disabled(bool)`, `.Visible(bool)`, `.WithKey(string)`,
`.Flex(grow?, shrink?, basis?)`, `.ToolTip(string)`,
`.FocusTrap(FocusTrapHandle)`,
`.Set(control => { /* raw WinUI access */ })`

**Styling:**
`.RequestedTheme(ElementTheme)`,
`.Resources(r => r.Set(key, value))` — lightweight styling overrides,
`.Backdrop(BackdropKind)` — apply a system backdrop (Mica / MicaAlt /
DesktopAcrylic / AcrylicThin / None) to the host window. Modifier is
host-side: it walks up to the `ReactorHost`'s `Window` and assigns the
materialized `SystemBackdrop`. On `ReactorHostControl` (windowless) it
no-ops (spec 033 §6).

**Animation — implicit transitions:**
`.OpacityTransition(duration?)`, `.ScaleTransition(transition?)`,
`.TranslationTransition(transition?)`, `.RotationTransition(duration?)`,
`.BackgroundTransition(duration?)` (Grid/Stack only)

**Animation — compositor:**
`.Animate(Curve, AnimateProperty?)` — persistent implicit animation,
`.Transition(Transition, Curve?)` — enter/exit (Fade, Slide, Scale; `+` parallel, `|` asymmetric),
`.InteractionStates(builder, curve?)` — zero-reconcile hover/press/focus,
`.Stagger(delay, curve?)` — cascade child animations,
`.Keyframes(name, trigger, builder)` — trigger-based multi-property keyframes,
`.ScrollLinked(scrollViewer, builder)` — scroll-driven expression animations

**Animation — layout:**
`.LayoutAnimation()`, `.LayoutAnimation(duration)`,
`.SpringLayoutAnimation(damping?, period?)`,
`.ConnectedAnimation(key)`

**Animation — scopes (call in event handlers):**
`AnimationScope.WithAnimation(Curve, Action)` — ambient animation scope,
`AnimationScope.WithAnimationAsync(Curve, Action)` — async choreography

**Compositor properties (animated via .Animate() or WithAnimation):**
`.Scale(Vector3)`, `.Rotation(float)`, `.Translation(x, y, z)`,
`.CenterPoint(Vector3)`

---

## Topic Ideas for the Full Docset

Generate these as `<topic>.md.dt` + `docs/_pipeline/apps/<topic>/` pairs:

### Beginner

0. **readme** — Landing page: what Reactor is, why no XAML, links to all topics
1. **getting-started** *(done)* — Project setup, hello world, state, layout, todo + calculator mini-apps
2. **dev-tooling** — `dotnet watch` + preview mode, VS Code extension, `mur` CLI, hot reload workflow
3. **components** — `Component`, `Component<TProps>`, record props, composition, `ShouldUpdate`, function components via `ReactorApp.Run(ctx => ...)`
4. **hooks** — Deep dive: UseState, UseReducer (both overloads), UseEffect (with cleanup), UseMemo, UseRef, UseCallback; hook rules and ordering
5. **layout** — VStack, HStack, Grid, ScrollView, Border, Expander, Canvas; spacing, alignment, responsive patterns with UseWindowSize/UseBreakpoint

### Intermediate

6. **flex-layout** — FlexPanel powered by Yoga (CSS Flexbox): direction, justify, align, wrap, grow/shrink/basis, gap, absolute positioning; when to use Flex vs. VStack/Grid
7. **forms** — TextField, CheckBox, ComboBox, Slider, NumberBox, PasswordBox, RadioButtons, ToggleSwitch; controlled-input idioms, ValidationContext + .Validate() with 11 built-in validators, FormField helper, MaskEngine (8 presets), InputFormatter (11 built-ins), AutoSuggest\<T\>, UseFocus for focus management
8. **collections** — ListView\<T\>, LazyVStack\<T\>, GridView\<T\>, VirtualList (count-based); virtualization, key selection, ForEach, stable identity with WithKey, VirtualListRef for imperative scrolling
9. **navigation** — UseNavigation\<TRoute\> hook, NavigationHandle (forward nav, state serialization, PopTo), type-safe stack-based routing, NavigationView/TabView/BreadcrumbBar elements, DeepLinkMap for URL routing (query params, wildcards, typed extraction), NavigationCache with Required mode, page lifecycle (UseNavigationLifecycle with cancel guards), UseSystemBackButton, animated transitions via TransitionEngine, NavigationDiagnostics events
10. **styling** — Theme tokens (37+ semantic ThemeRef values), UseColorScheme/UseIsDarkTheme hooks, .RequestedTheme() modifier, lightweight styling via .Resources() + ResourceBuilder, Style caching, Roslyn analyzers (REACTOR_THEME_001-003), fonts, CornerRadius
11. **effects** — UseEffect lifecycle: mount, update, cleanup; timers, async data loading, file I/O, dependency arrays, infinite-loop pitfalls
12. **commanding** — Command, Command\<T\>, StandardCommand, UseCommand hook, keyboard accelerators, async commands with IsExecuting tracking
13. **context** — Context\<T\>, ContextProvider, UseContext, nested/overridden contexts; theme context as a worked example

### Advanced

14. **accessibility** — AutomationName, HeadingLevel, landmarks, live regions, IsTabStop/TabIndex, AccessKey, FullDescription; tiered modifier pattern (common vs. advanced); WCAG compliance patterns; UseFocusTrap for modals, UseAnnounce for screen reader notifications, SemanticPanel for custom automation peers, AccessibilityScanner runtime diagnostics, Roslyn analyzers (REACTOR\_A11Y\_001-003)
15. **localization** — LocaleProvider, UseIntl, RtlHelper, logical layout properties (MarginInlineStart/End), string resource providers, date/number formatting, pseudo-localization for testing
16. **animation** — Implicit transitions (opacity, scale, translate, rotation, background), .Animate() compositor modifier, enter/exit .Transition() with combinators (+, |), .InteractionStates() zero-reconcile hover/press/focus, .Stagger() cascade, .Keyframes() trigger animations, .ScrollLinked() expression animations, WithAnimation/WithAnimationAsync ambient scopes, spring/ease curves, LayoutAnimation, ConnectedAnimation
17. **charting** — ReactorCharting chart DSL: LineChart, BarChart, AreaChart, PieChart, TreeChart, ForceGraph; scales (Linear, Band, Log, Pow, Ordinal); shape generators; D3 Canvas drawing primitives; ChartHandle for live updates
18. **advanced** — ErrorBoundary + fallback UI, Memo for subtree skip, performance tuning (ElementPool, batched renders, bitmask diffs), WinUI escape hatch (`.Set()`), observable data binding (UseObservableTree, UseCollection)
19. **data-system** — DataGrid\<T\> with sort, filter, search, inline editing (cell/row modes), column resize/reorder, selection; IDataSource\<T\> abstraction, ListDataSource, ObservableListDataSource; FieldDescriptor and `Column<T>` / `AutoColumns<T>` factories for column definition; DataPageCache for incremental paging; DataGridState headless state machine
20. **winforms-interop** — Hosting Reactor components in WinForms via XAML Islands; XamlIslandBootstrap.Run() initialization flow, XamlIslandControl (code and designer), ComponentType property with Properties-grid dropdown, Tab/keyboard bridging, accessibility hooks, background/stretch considerations

---

## Checklist Before Submitting

- [ ] Template has valid YAML front-matter with all required fields
- [ ] All `snippet=` references match `// <snippet:id>` markers in the app
- [ ] All `screenshot://` references match entries in `doc-manifest.yaml`
- [ ] `ai:lock` sections are preserved exactly from any prior version
- [ ] Doc app compiles with `dotnet build`
- [ ] Doc app shows a useful default state when launched (no interaction needed)
- [ ] Snippets are under 30 lines each
- [ ] Prose explains code *after* showing it, not before
- [ ] Tips are specific to Reactor, not generic programming advice
- [ ] Page has a `## Next Steps` section with links to related and sequential topics
- [ ] Readme links to all topic pages; all pages are reachable via link traversal
- [ ] Run `mur docs compile --validate-only` to check all references resolve
