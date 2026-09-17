# Reactor Framework vs WinUI 3 — Comprehensive Gap Analysis

**Status:** Living document — re-verified 2026-07-30 against `skills/reactor.api.txt` and `src/Reactor`.

How every WinUI 3 application-programming feature is exposed, replaced, augmented,
hidden, or blocked by the Microsoft.UI.Reactor (Reactor) framework design.

> **Read this before trusting a row.** This document ages badly: it is a snapshot of coverage,
> and coverage moves every week. The 2026-07-30 pass corrected a large number of rows that had
> gone stale — **all four of the original P0 gaps have since shipped** (theming tokens,
> accessibility modifiers, a navigation router, and a command model). Anything not re-verified
> on that date should be treated as a hypothesis, not a fact. §24 says how to re-verify.

## Legend

| Symbol | Meaning |
|--------|---------|
| **Exposed** | WinUI feature is wrapped with a first-class Reactor DSL element, modifier, or hook |
| **Replaced** | WinUI feature is intentionally superseded by a different Reactor mechanism |
| **Augmented** | Feature is exposed AND Reactor adds value on top (simpler API, extras) |
| **Passthrough** | Not wrapped, but accessible via `.Set()` escape hatch on a parent element |
| **Blocked** | Cannot be used at all due to Reactor's architecture (no XAML, no templates, etc.) |
| **Missing** | Could be wrapped but isn't yet; no architectural blocker |

---

## Table of Contents

1. [Built-in Controls](#1-built-in-controls)
2. [Layout System](#2-layout-system)
3. [Navigation Patterns](#3-navigation-patterns)
4. [Data Binding](#4-data-binding)
5. [Dependency Property System](#5-dependency-property-system)
6. [XAML Markup Features](#6-xaml-markup-features)
7. [Resources and Resource Management](#7-resources-and-resource-management)
8. [Styling](#8-styling)
9. [Theming](#9-theming)
10. [Visual State Manager](#10-visual-state-manager)
11. [Animations and Transitions](#11-animations-and-transitions)
12. [Composition Visual Layer](#12-composition-visual-layer)
13. [Materials and Effects](#13-materials-and-effects)
14. [Input Handling](#14-input-handling)
15. [Commands](#15-commands)
16. [Accessibility](#16-accessibility)
17. [Threading Model](#17-threading-model)
18. [Windowing](#18-windowing)
19. [Application Lifecycle](#19-application-lifecycle)
20. [App Services](#20-app-services)
21. [Interop](#21-interop)
22. [Content and Items Infrastructure](#22-content-and-items-infrastructure)
23. [Summary Scorecard](#23-summary-scorecard)
24. [Keeping This Document Honest](#24-keeping-this-document-honest)

---

## 1. Built-in Controls

### 1.1 Basic Input

| WinUI Control | Status | Reactor Surface | Notes |
|---|---|---|---|
| **Button** | Exposed | `Button("label", onClick)` | First-class; `OnClick` callback |
| **DropDownButton** | Exposed | `DropDownButton(content, flyout)` | DropDownButtonElement |
| **SplitButton** | Exposed | `SplitButton(content, flyout)` | SplitButtonElement |
| **ToggleSplitButton** | Exposed | `ToggleSplitButton(content, flyout)` | ToggleSplitButtonElement |
| **HyperlinkButton** | Exposed | `HyperlinkButton(label, uri)` | HyperlinkButtonElement |
| **RepeatButton** | Exposed | `RepeatButton(label, onClick)` | RepeatButtonElement |
| **ToggleButton** | Exposed | `ToggleButton(content, isChecked, onToggled)` | ToggleButtonElement |
| **CheckBox** | Exposed | `CheckBox(isChecked, onChanged, label)` | `Optional<bool?>` value; tri-state via `ThreeStateCheckBox(checkedState, onCheckedStateChanged, label)` |
| **RadioButton** | Exposed | `RadioButton(label, isChecked, onChecked)` | RadioButtonElement |
| **RadioButtons** | Exposed | `RadioButtons(items, selectedIndex)` | RadioButtonsElement |
| **ToggleSwitch** | Exposed | `ToggleSwitch(isOn, onChanged)` | ToggleSwitchElement |
| **Slider** | Exposed | `Slider(value, min, max, onChanged)` | SliderElement |
| **ComboBox** | Exposed | `ComboBox(items, selectedIndex)` | ComboBoxElement |
| **ListBox** | Exposed | `ListBox(...)` | ListBoxElement |
| **ColorPicker** | Exposed | `ColorPicker(color, onChanged)` | ColorPickerElement |
| **RatingControl** | Exposed | `RatingControl(value, onChanged)` | RatingControlElement |
| **NumberBox** | Exposed | `NumberBox(value, onChanged)` | NumberBoxElement |

**Verdict: 17/17 exposed.** All basic input controls have first-class Reactor elements with
callback-based event handling replacing XAML event bindings.

### 1.2 Text Controls

| WinUI Control | Status | Reactor Surface | Notes |
|---|---|---|---|
| **TextBlock** | Augmented | `Text("content")` | Also: `Heading()`, `SubHeading()`, `Caption()` convenience factories; implicit string-to-TextElement conversion |
| **RichTextBlock** | Exposed | `RichTextBlock(...)` | RichTextBlockElement |
| **TextBox** | Exposed | `TextBox(value, onChanged, placeholderText, header)` | TextBoxElement; `Optional<string>` value for controlled-input authority (spec 050) |
| **RichEditBox** | Exposed | `RichEditBox(...)` | RichEditBoxElement |
| **PasswordBox** | Exposed | `PasswordBox(password, onChanged)` | PasswordBoxElement |
| **AutoSuggestBox** | Exposed | `AutoSuggestBox(text, items, onChanged, onQuery)` | AutoSuggestBoxElement |

**Verdict: 6/6 exposed (1 augmented).** Text is one of Reactor's strengths — convenience
factories (`Heading`, `Caption`) and implicit string conversion add ergonomics on top of full
WinUI text control coverage.

### 1.3 Icons

| WinUI Control | Status | Reactor Surface | Notes |
|---|---|---|---|
| **FontIcon** | Exposed | `FontIcon(glyph)` | FontIconData record |
| **SymbolIcon** | Exposed | `SymbolIcon(symbol)` | SymbolIconData record |
| **ImageIcon** | Exposed | `ImageIcon(uri)` | ImageIconData record |
| **AnimatedIcon** | Exposed | `AnimatedIcon(...)` | AnimatedIconElement |
| **BitmapIcon** | Exposed | `BitmapIcon(uri)` | BitmapIconData record |
| **PathIcon** | Exposed | `PathIcon(data)` | PathIconData record |

**Verdict: 6/6 exposed.** Icons are modeled as data records (not elements) for use in
NavigationViewItem, CommandBar, etc.

### 1.4 Collections and Lists

| WinUI Control | Status | Reactor Surface | Notes |
|---|---|---|---|
| **ListView** | Augmented | `ListView(items, template)` + `LazyVStack<T>(items, template)` | Virtualized; also TemplatedListViewElement\<T\> for typed templates |
| **GridView** | Augmented | `GridView(items, template)` + `LazyHStack<T>` | TemplatedGridViewElement\<T\> available |
| **ItemsView** | Exposed | `ItemsView<T>(items, keySelector, viewBuilder)` | ItemsViewElement\<T\>; `keySelector` is **required** here (unlike ListView/GridView/ItemsRepeater, which also have a keyless overload). `viewBuilder` must return an `ItemContainer(...)` root — enforced at build time by `REACTOR_ITEMS_002` and again at mount by `GuardedViewBuilder` |
| **ItemsRepeater** | Exposed | `ItemsRepeater<T>(items, viewBuilder)` | ItemsRepeaterElement\<T\>, with a keyed overload; also drives LazyVStack/LazyHStack internally |
| **FlipView** | Exposed | `FlipView(items, template)` | TemplatedFlipViewElement\<T\> available |
| **TreeView** | Exposed | `TreeView(items)` | TreeViewElement with drag support |

**Verdict: 6/6 exposed (2 augmented).** ItemsRepeater is now a first-class factory rather than
an implementation detail, so custom virtualizing layouts no longer require reaching through
LazyVStack. `SemanticZoom(zoomedIn, zoomedOut)` and `ItemContainer(...)` are exposed alongside
these.

### 1.5 Date and Time

| WinUI Control | Status | Reactor Surface | Notes |
|---|---|---|---|
| **CalendarView** | Exposed | `CalendarView(...)` | CalendarViewElement |
| **CalendarDatePicker** | Exposed | `CalendarDatePicker(date, onChanged)` | CalendarDatePickerElement |
| **DatePicker** | Exposed | `DatePicker(date, onChanged)` | DatePickerElement |
| **TimePicker** | Exposed | `TimePicker(time, onChanged)` | TimePickerElement |

**Verdict: 4/4 exposed.**

### 1.6 Dialogs, Flyouts, and Popups

| WinUI Control | Status | Reactor Surface | Notes |
|---|---|---|---|
| **ContentDialog** | Exposed | `ContentDialog(title, content, ...)` | ContentDialogElement; modal with button callbacks |
| **Flyout** | Exposed | `Flyout(target, content)` | ContentFlyoutElement |
| **MenuFlyout** | Exposed | `MenuFlyout(target, items)` | MenuFlyoutElement + MenuFlyoutContentElement |
| **CommandBarFlyout** | Exposed | `CommandBarFlyout(target, ...)` | CommandBarFlyoutElement |
| **TeachingTip** | Exposed | `TeachingTip(title, content)` | TeachingTipElement |
| **ToolTip** | Exposed | `.ToolTip("text")` / `.WithToolTip(element)` / `.ToolTipPlacement(mode)` / `.ToolTipPlacementTarget(ref)` | Text and rich (Element) content, plus both `ToolTipService` attached properties — `Placement` and `PlacementTarget`. WinUI has no tooltip show/hide delay knobs (`InitialShowDelay`/`BetweenShowDelay` are WPF-only), so nothing is missing there |
| **Popup** | Exposed | `Popup(content)` | PopupElement |

**Verdict: 7/7 exposed.** ToolTip covers text content, rich Element content, and the
full `ToolTipService` attached-property surface (`ToolTip`, `Placement`,
`PlacementTarget`).

### 1.7 Menus, Toolbars, and Commands

| WinUI Control | Status | Reactor Surface | Notes |
|---|---|---|---|
| **MenuBar** | Exposed | `MenuBar(items)` | MenuBarElement |
| **MenuBarItem** | Exposed | Via MenuBar items | Part of MenuBar data model |
| **CommandBar** | Exposed | `CommandBar(primaryCommands, ...)` | CommandBarElement |
| **AppBarButton** | Exposed | `AppBarButton(label, onClick, icon)` | Also accepts a `Command` |
| **AppBarToggleButton** | Exposed | `AppBarToggleButton(label, isChecked, onIsCheckedChanged, icon)` | Dedicated factory |
| **AppBarSeparator** | Exposed | `AppBarSeparator()` | Dedicated factory |

**Verdict: 6/6 exposed.** AppBarToggleButton and AppBarSeparator have dedicated factories;
they are no longer `.Set()`-only holes in the CommandBar item model.

### 1.8 Navigation Controls

| WinUI Control | Status | Reactor Surface | Notes |
|---|---|---|---|
| **NavigationView** | Exposed | `NavigationView(menuItems, content)` | Full support: pane, back button, settings, selection |
| **TabView** | Exposed | `TabView(tabs, selectedIndex)` | TabViewElement with tab data model |
| **BreadcrumbBar** | Exposed | `BreadcrumbBar(items, onClick)` | BreadcrumbBarElement |
| **SelectorBar** | Exposed | `SelectorBar(...)` | SelectorBarElement |
| **Pivot** | Exposed | `Pivot(items)` | PivotElement |
| **Frame** | Exposed | `Frame(sourcePageType)` | FrameElement with navigation parameter |
| **Page** | Replaced | Components | Reactor components replace Page; no Page subclassing needed |
| **PipsPager** | Exposed | `PipsPager(...)` | PipsPagerElement |

**Verdict: 7/8 exposed, 1 replaced.** Page is replaced by the component model — each "page"
is a component that renders its content directly.

### 1.9 Media and Graphics

| WinUI Control | Status | Reactor Surface | Notes |
|---|---|---|---|
| **Image** | Exposed | `Image(source)` | ImageElement |
| **MediaPlayerElement** | Exposed | `MediaPlayerElement(...)` | MediaPlayerElementElement |
| **InkCanvas** | Missing | — | No element; use `.Set()` on a host |
| **InkToolbar** | Missing | — | No element |
| **WebView2** | Exposed | `WebView2(uri)` | WebView2Element |
| **PersonPicture** | Exposed | `PersonPicture(...)` | PersonPictureElement |
| **ParallaxView** | Exposed | `ParallaxView(...)` | ParallaxViewElement |
| **CaptureElement** | Missing | — | No element |
| **MapControl** | Exposed | `MapControl(...)` | MapControlElement |
| **AnimatedVisualPlayer** | Exposed | `AnimatedVisualPlayer(...)` | AnimatedVisualPlayerElement |

**Verdict: 7/10 exposed, 3 missing.** InkCanvas, InkToolbar, and CaptureElement
are not wrapped. These are specialized controls that can still be used via `.Set()` on a host
container, or by registering a custom control type (see §2.2).

**Not counted above:** XAML shapes have first-class factories — `Rectangle()`, `Ellipse()`,
`Line()`, `Path2D()`. Note that shapes are painted with `.Fill()` / `.Stroke()`, **not**
`.Background()` — `ApplyModifiers` only routes `Background` to `Panel`, `Control`, and `Border`,
so `.Background()` on a shape is silently dropped (flagged at build time by `REACTOR_MOD_003`).

### 1.10 Status and Information

| WinUI Control | Status | Reactor Surface | Notes |
|---|---|---|---|
| **ProgressBar** | Exposed | `Progress(value, isIndeterminate)` | ProgressElement |
| **ProgressRing** | Exposed | `ProgressRing(isActive)` | ProgressRingElement |
| **InfoBar** | Exposed | `InfoBar(...)` | InfoBarElement |
| **InfoBadge** | Exposed | `InfoBadge(...)` | InfoBadgeElement |
| **Expander** | Exposed | `Expander(header, content, isExpanded)` | ExpanderElement |

**Verdict: 5/5 exposed.**

### 1.11 Scrolling

| WinUI Control | Status | Reactor Surface | Notes |
|---|---|---|---|
| **ScrollViewer** | Exposed | `ScrollViewer(child)` | `ScrollViewerElement` — classic `Control`-shaped one. Use for parallax animations, `ScrollViewer.SetXxx` attached properties on templated parents, or the `IsIntermediate` view-changed flag. |
| **ScrollView** | Exposed | `ScrollView(child)` | `ScrollViewElement` — modern `InteractionTracker`-backed control. Default choice for new code. (Issue #348) |
| **AnnotatedScrollBar** | Exposed | `AnnotatedScrollBar(...)` | AnnotatedScrollBarElement |
| **ScrollBar** | Passthrough | Via `.Set()` | Primitive; rarely needed directly |

**Verdict: 3/4 exposed, 1 passthrough.**

### 1.12 Layout and Container Controls

| WinUI Control | Status | Reactor Surface | Notes |
|---|---|---|---|
| **Border** | Augmented | `Border(child)` + `.WithBorder()` modifier | BorderElement; also usable as card/container |
| **Viewbox** | Exposed | `Viewbox(child)` | ViewboxElement |
| **SplitView** | Exposed | `SplitView(pane, content)` | SplitViewElement |
| **TwoPaneView** | Missing | — | No element; use `.Set()` |
| **SwipeControl** | Exposed | `SwipeControl(...)` | SwipeControlElement |
| **RefreshContainer** | Exposed | `RefreshContainer(child)` | RefreshContainerElement |

**Verdict: 5/6 exposed (1 augmented), 1 missing.** TwoPaneView is not wrapped.

### 1.13 Title Bar

| WinUI Control | Status | Reactor Surface | Notes |
|---|---|---|---|
| **TitleBar** | Exposed | `TitleBar(title)` | TitleBarElement with LeftHeader, RightHeader, Content |

**Verdict: 1/1 exposed.**

### Controls Summary

| Category | Total | Exposed | Augmented | Replaced | Passthrough | Missing |
|---|---|---|---|---|---|---|
| 1.1 Basic Input | 17 | 17 | — | — | — | — |
| 1.2 Text | 6 | 5 | 1 | — | — | — |
| 1.3 Icons | 6 | 6 | — | — | — | — |
| 1.4 Collections | 6 | 4 | 2 | — | — | — |
| 1.5 Date/Time | 4 | 4 | — | — | — | — |
| 1.6 Dialogs | 7 | 7 | — | — | — | — |
| 1.7 Menus/Toolbars | 6 | 6 | — | — | — | — |
| 1.8 Navigation | 8 | 7 | — | 1 | — | — |
| 1.9 Media | 10 | 7 | — | — | — | 3 |
| 1.10 Status | 5 | 5 | — | — | — | — |
| 1.11 Scrolling | 4 | 3 | — | — | 1 | — |
| 1.12 Containers | 6 | 4 | 1 | — | — | 1 |
| 1.13 Title Bar | 1 | 1 | — | — | — | — |
| **Totals** | **86** | **76** | **4** | **1** | **1** | **4** |

**Overall control coverage: 82/86 (95%) accessible, 4 missing (InkCanvas, InkToolbar,
CaptureElement, TwoPaneView).**

Beyond the WinUI catalogue, Reactor ships first-class controls with no WinUI counterpart:
`Flex` (Yoga flexbox), `DataGrid`, `PropertyGrid`, `VirtualList`, `UniformGrid`,
`InterspersedGrid`, `Card`, `MaskedTextBox`, and the docking window family (spec 045/046).

---

## 2. Layout System

### 2.1 Panel Types

| WinUI Panel | Status | Reactor Surface | Notes |
|---|---|---|---|
| **Grid** | Augmented | `Grid(columns, rows, children)` | String-based column/row definitions like CSS: `["*", "Auto"]` |
| **StackPanel** | Augmented | `VStack(children)` / `HStack(children)` | Renamed for clarity; orientation via factory choice |
| **Canvas** | Exposed | `Canvas(children)` | CanvasElement with `.Canvas(left:, top:)` attached props |
| **RelativePanel** | Exposed | `RelativePanel(children)` | `.RelativePanel(name:, below:, ...)` attached props |
| **VariableSizedWrapGrid** | Exposed | `WrapGrid(children)` | WrapGridElement |

**Additional Reactor-only panel:**
- **FlexPanel** — CSS Flexbox layout via Yoga engine port. `Flex(children)` with `.Flex(grow:,
  shrink:, basis:, alignSelf:, ...)` attached properties. Supports FlexDirection, JustifyContent,
  AlignItems, AlignContent, Wrap, Gap. This has no WinUI equivalent.

**Verdict: 5/5 exposed (2 augmented), plus 1 Reactor-exclusive (FlexPanel).**

### 2.2 Measure/Arrange Two-Pass System

| Feature | Status | Notes |
|---|---|---|
| Custom panels via MeasureOverride/ArrangeOverride | Passthrough | FlexPanel implements this internally; author a custom panel as a WinUI type and register it with `ControlRegistry.Register<TElement, TControl>(...)` |
| InvalidateMeasure/InvalidateArrange | Passthrough | Available via `.Set()` |

**Verdict: Passthrough.** Reactor's virtual element tree doesn't directly expose the
measure/arrange cycle. The reconciler handles layout property changes, which trigger
WinUI's built-in invalidation. Custom panels are registered through the extensible control
model (spec 047): a `ControlDescriptor<TElement, TControl>` for regular shapes, or a
hand-coded `IElementHandler<TElement, TControl>` for irregular ones, both wired via
`ControlRegistry.Register*`. (`Reconciler.RegisterType<>()` still exists but is the older,
lower-level seam; spec 062 documents `ControlRegistry` as the supported escape hatch.)

### 2.3 Attached Layouts (ItemsRepeater)

| Feature | Status | Notes |
|---|---|---|
| StackLayout | Passthrough | Default for `ItemsRepeater<T>` / LazyVStack; configure via `.SetRepeater()` |
| UniformGridLayout | Passthrough | Selectable on ItemsView via `ItemsViewLayoutKind`; otherwise `.SetRepeater()` |
| FlowLayout | Passthrough | Configurable via `.SetRepeater()` / `.Set()` |
| LinedFlowLayout | Passthrough | Selectable on ItemsView via `ItemsViewLayoutKind`; otherwise `.SetRepeater()` |
| Custom VirtualizingLayout | Passthrough | Assign via `.SetRepeater(r => r.Layout = ...)` |

**Verdict: Passthrough.** `ItemsRepeater<T>` is now a first-class element (§1.4), and
`LazyVStackElement<T>.SetRepeater(Action<ItemsRepeater>)` / `LazyHStackElement<T>.SetRepeater(...)`
give typed access to the underlying repeater, so attached layouts are reachable without a raw
`.Set()` cast. `ItemsView` additionally exposes a `ItemsViewLayoutKind` enum for the common
layouts. There is still no declarative DSL for layout objects themselves.

### 2.4 Adaptive/Responsive Layout

| Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| **AdaptiveTrigger** | Replaced | `UseWindowSize()` / `UseBreakpoint()` hooks | React-style responsive: re-render with different elements based on window size |
| **Custom StateTriggers** | Replaced | Component state + conditional rendering | Any condition → state → different render output |
| **NavigationView auto-mode** | Exposed | Via NavigationView element properties | Compact/expanded thresholds work natively |
| **RelativePanel** | Exposed | RelativePanelElement | Layout changes via different attached props per breakpoint |
| **TwoPaneView** | Missing | — | Not wrapped |

**Verdict: 3/5 exposed, 1 replaced, 1 missing.** Reactor's hook-based responsive design
(`UseBreakpoint`) is arguably more powerful than AdaptiveTrigger because it can change the
entire element tree, not just visual state properties. However, it triggers full re-renders
rather than lightweight property changes.

---

## 3. Navigation Patterns

| WinUI Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| **NavigationView** | Exposed | NavigationViewElement | Control works; pane, back button, settings, selection, display modes |
| **Frame + Page navigation** | Replaced | `NavigationHandle<TRoute>` + `NavigationHost<TRoute>` | A route is any value you choose (usually an enum or record); `routeMap` turns it into an Element. No Page subclass, no XAML, no code-behind |
| **Back stack management** | Replaced | `nav.BackStack` / `nav.ForwardStack`, `GoBack()`, `GoForward()`, `CanGoBack`, `CanGoForward` | Real history with `NavigationMode` (Push, Pop, Replace, Reset, Forward) and `NavigateOptions.PushToBackStack` to opt a route out |
| **Navigation parameter passing** | Replaced | The route value *is* the parameter | Strongly typed; no `object`-typed parameter bag, no cast at the destination |
| **Navigation transitions** | Exposed | `NavigationTransition.Entrance/Fade/DrillIn/Slide/Spring/Connected`, plus `.Default` and `.None` | Chosen per navigation rather than baked into the target. `.Default` is the entrance motion, matching a WinUI `Frame` navigated with no transition info |
| **Deep links / protocol activation** | Augmented | `DeepLinkMap<TRoute>.Map(pattern, factory)` + `LaunchActivation.TryResolve` | Pattern-routed URIs resolve to a route *and* can seed a synthetic back stack (`backStackFactory`). No WinUI equivalent |
| **TabView** | Exposed | TabViewElement | Full tab management with selection |
| **BreadcrumbBar** | Exposed | BreadcrumbBarElement | Click handler per item |
| **SelectorBar** | Exposed | SelectorBarElement | View switching |
| **Frame.Navigate(typeof(Page))** | Blocked | — | `Frame(sourcePageType, navigationParameter)` mounts, but a **code-only** `Page` — the only kind a no-XAML Reactor app has — cannot be navigated to. `FrameNavigation.TryNavigate` now verifies the target is resolvable in the XAML metadata chain and **refuses** rather than handing WinUI an unresolvable type, so this fails safely instead of killing the process (#945). Usable only for XAML-interop Pages. See the caveat below |

**Verdict: the navigation *scenario* is solved by replacement; the WinUI `Frame` *element* is
still blocked.** The original verdict here ("navigation is generally broken") is obsolete.
Reactor now ships a native router:
`UseNavigation<TRoute>()` gives a component a `NavigationHandle<TRoute>` with genuine back and
forward stacks, `NavigationHost<TRoute>(nav, routeMap)` renders the current route, and
`NavigationTransition` supplies the animation that WinUI would have attached to the Page. The
route being an ordinary C# value means parameter passing is type-checked at compile time —
strictly better than `Frame.Navigate(typeof(T), object)`. Use `NavigationHost`, not `Frame`.

**Caveat — the WinUI `Frame` element itself.** `Frame(sourcePageType)` mounts, but it cannot
navigate to a **code-only** `Page` subclass. WinUI resolves navigation targets through
`IXamlMetadataProvider`; a Reactor app ships no XAML, so no metadata provider is emitted and
`GetXamlTypeNoRef()` returns null. This used to kill the process with an access violation;
#945 added `FrameNavigation.TryNavigate`, which confirms the target is resolvable and refuses
the navigation when it is not, so the failure is now safe and reportable rather than fatal.

It is **not** unblocked, and deliberately so. #945 was cut back from an earlier version that
published app-defined `Page` types into the metadata chain: spec 011's goal 3 is *"zero XAML
dependency — no `.xaml` files, no `IXamlMetadataProvider`"*, and its §"Why WinUI Frame is not
the answer" documents four hard C++ constraints of which metadata resolution is only the first.
Publishing types would have satisfied one constraint and left three, making a discouraged path
*partially* work — worse than not supporting it. `Frame` is an interop escape hatch for apps
that already have XAML pages; use `NavigationHost` for Reactor-native navigation.

---

## 4. Data Binding

| WinUI Feature | Status | Reactor Replacement | Notes |
|---|---|---|---|
| **{x:Bind}** | Replaced | Direct property access in render | No markup; values flow from component state to element properties in render() |
| **{Binding}** | Replaced | UseObservable hook | Bridges INotifyPropertyChanged to re-renders |
| **OneTime mode** | Replaced | Constant values in render | A non-state value is effectively one-time |
| **OneWay mode** | Replaced | UseState + render | State changes trigger re-render, updating all outputs |
| **TwoWay mode** | Replaced | UseState + OnChanged callback | `TextBox(text, t => setText(t))` is two-way |
| **INotifyPropertyChanged** | Replaced | UseObservable hook | `UseObservable(viewModel)` subscribes to changes |
| **ObservableCollection** | Replaced | UseCollection hook | `UseCollection(items)` re-renders on add/remove |
| **IValueConverter** | Replaced | Inline C# expressions | `Text($"${price:F2}")` — no converter classes needed |
| **Function bindings** | Replaced | Inline C# functions | `Text(FormatDate(date))` — just call the function |
| **FallbackValue/TargetNullValue** | Replaced | Null-coalescing / conditional | `Text(value ?? "N/A")` |
| **DataTemplate {x:DataType}** | Replaced | Lambda templates | `ListView(items, item => Text(item.Name))` |
| **DataContext inheritance** | Replaced | `Context<T>` + `.Provide(context, value)` + `UseContext(context)` | Ambient values flow down the element tree explicitly and type-safely, instead of an untyped `object DataContext` that any descendant may silently re-bind |

**Verdict: Entirely replaced.** Reactor eliminates the entire binding subsystem in favor of
React-style unidirectional data flow. This removes an entire category of runtime errors
(binding failures, wrong DataContext, type mismatches) at the cost of requiring explicit
state management. MVVM interop is available via `UseObservable` for gradual migration.

---

## 5. Dependency Property System

| WinUI Feature | Status | Notes |
|---|---|---|
| **DependencyProperty registration** | Blocked | Reactor elements are C# records, not DependencyObjects; no DP registration needed or possible |
| **PropertyChangedCallback** | Replaced | Reconciler diffing detects property changes and applies them to real WinUI controls |
| **Value precedence** | Replaced | Reactor substitutes its own three-tier model (§9) for WinUI's precedence chain. Concrete modifier values (`.Background("#FF0000")`) do a local-value set, which sits at the top of the chain. `ThemeRef` values (`.Background(Theme.Accent)`) are stored in `Element.ThemeBindings` and applied by `Reconciler.ApplyThemeBindings`, so they re-resolve on theme change instead of pinning a brush. There is still no way to write at the *style* tier |
| **Attached properties** | Augmented | Type-safe `.Grid(row:, col:)` / `.Canvas(left:, top:)` / `.Flex(grow:)` extensions stored in Element.Attached dictionary |
| **RegisterPropertyChangedCallback** | Passthrough | Available via `.Set()` for instance-level observation |
| **ClearValue** | Passthrough | Available via `.Set()`; the reconciler calls it internally when a modifier is unset |

**Verdict: Partially blocked, but no longer the root cause of broken theming.** The DP system
is the backbone of XAML and stays invisible in Reactor's programming model — element properties
are plain C# record fields, and the reconciler translates them to real DP values on mount and
update. Attached properties are reimplemented as a type-safe dictionary system.

The original verdict blamed value precedence for breaking theming. That is now only half true:
the three-tier value model from `001-theming-design.md` shipped, and Tier 2 (`ThemeRef`) writes
theme-reactive values that survive a theme change. Tier 1 (concrete brushes and colors) still
performs a local-value set and still overrides styles and theme resources — but that is now a
*documented, opt-in* trade-off with a first-class alternative, not the only available behaviour.
See §9.

A related sharp edge remains: `Reconciler.ApplyModifiers` only routes some modifiers to some
control shapes. `.Background()` is applied to `Panel`, `Control`, and `Border` only, so it is
silently dropped on a `Shape`. `REACTOR_MOD_003` now flags this class of silent no-op at build
time.

---

## 6. XAML Markup Features

| WinUI Feature | Status | Notes |
|---|---|---|
| **{x:Bind}** | Blocked | No XAML → no markup extensions |
| **{Binding}** | Blocked | No XAML |
| **{StaticResource}** | Replaced | No XAML markup extension, but resource *values* are reachable: `.ApplyStyle("AccentButtonStyle")` for named styles, `Theme.Ref("SomeResourceKey")` for a theme-reactive lookup, and `.Resources(r => r.Set(key, value))` for per-control overrides |
| **{ThemeResource}** | Replaced | `ThemeRef` / `Theme.*` tokens — resolved from WinUI resources and re-resolved on theme change |
| **{TemplateBinding}** | Blocked | No control templates in Reactor |
| **x:Name** | Replaced | `.OnMount(control => ...)` and `ElementRef` capture control references |
| **x:Key** | Replaced | Resource keys are ordinary strings passed to `.Resources(r => r.Set("ButtonBackground", ...))` or `Theme.Ref("...")`. There is still no app-level resource-dictionary DSL (see §7) |
| **x:Class** | Replaced | C# class declaration IS the component |
| **x:DataType** | Replaced | C# generics on template lambdas |
| **x:DeferLoadStrategy / x:Load** | Replaced | C# conditional rendering (`if`/ternary in render) inherently defers creation — elements not in the current render output are never mounted |
| **Conditional XAML** | Replaced | C# `if`/`switch` in render method |
| **Custom MarkupExtension** | Replaced | The user scenario (inject custom resolution logic into property values) is handled by C# methods, extension methods, and helper functions called inline during render |
| **Casting in {x:Bind}** | Replaced | Standard C# casting |

**Verdict: Mostly blocked/replaced.** XAML markup features are inherently tied to the XAML
parser and are not applicable in Reactor's pure-C# model. Every feature that XAML markup
extensions provide is handled by standard C# language features (conditionals, generics,
casting, string interpolation, helper methods). Deferred loading is inherently replaced
by conditional rendering — elements not returned from render are never created.

---

## 7. Resources and Resource Management

| WinUI Feature | Status | Notes |
|---|---|---|
| **ResourceDictionary (app-level)** | Passthrough | ReactorApp loads XamlControlsResources; no DSL for authoring app-level dictionaries |
| **ResourceDictionary (per-element)** | Exposed | `.Resources(r => r.Set(key, value))` writes into that element's own `FrameworkElement.Resources`; `ResourceBuilder.Set` overloads accept a hex/named color string, a `Brush`, a `ThemeRef`, a `double`, or a `CornerRadius` |
| **Resource lookup chain** | Passthrough | WinUI's chain works on underlying controls; a per-element override participates in it normally |
| **Merged dictionaries** | Passthrough | Can merge via `.Set()` on Application.Resources |
| **Theme dictionaries** | Passthrough | WinUI's theme dictionaries work natively; `ThemeRef` resolution reads through them |
| **XamlControlsResources** | Exposed | Automatically loaded by ReactorApplication.OnLaunched |
| **Forward reference restriction** | N/A | No XAML → no forward reference issue |

**Verdict: Split.** App-level resource management is still delegated entirely to WinUI, with no
DSL — that part of the original verdict stands. What changed is the per-element tier: the
`ResourceBuilder` DSL (`.Resources(...)`) makes scoped resource overrides a first-class,
type-checked operation, which is what "lightweight styling" needs (see §8).

---

## 8. Styling

| WinUI Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| **Implicit styles** | Passthrough | Work on underlying controls | WinUI's implicit styles apply to Reactor-created controls normally |
| **Explicit styles (x:Key)** | Augmented | `.ApplyStyle("AccentButtonStyle")` plus named-style fluents | `.AccentButton()`, `.SubtleButton()`, `.TextLink()`, `Title()`, `Subtitle()`, `Body()`, `BodyStrong()`, `BodyLarge()`, `TitleLarge()`, `Display()` wrap the common keys so the string literal is optional |
| **BasedOn inheritance** | Passthrough | Works in WinUI layer | Style inheritance works; Reactor doesn't interfere |
| **Lightweight styling** | Exposed | `.Resources(r => r.Set("ButtonBackground", Theme.Accent))` | Per-control theme-resource key overrides; the `ThemeRef` overload keeps the override theme-reactive |
| **ControlTemplate** | Blocked | — | Reactor renders content directly; no template authoring |
| **DataTemplate** | Replaced | Lambda template functions | `ListView(items, item => HStack(Image(item.Icon), Text(item.Name)))` |
| **Templated controls (Generic.xaml)** | Blocked | — | Custom controls are Reactor components, not templated controls |

**Verdict: Mixed, and better than it was.** Basic style application works, DataTemplate is
elegantly replaced by lambda functions, and lightweight styling — called out as Missing in the
original pass — now ships as `.Resources(...)`. That closes the "custom-branded controls"
scenario without giving up theme reactivity, because `ResourceBuilder.Set` takes a `ThemeRef`.

Still blocked: ControlTemplate authoring. You cannot re-template a WinUI control from Reactor,
and a "custom control" in Reactor means a component (composition) or a registered control type
(§2.2), never a templated `Control` subclass with template parts.

---

## 9. Theming

Theming is evaluated with a "could a normal developer use this correctly" bar. The original
pass rated this section **Blocked / P0** because every Reactor color modifier did a local-value
set and there was no theme-reactive alternative. The three-tier value model from
`001-theming-design.md` has since shipped, so most of that verdict no longer holds.

**The three tiers, as implemented:**

| Tier | How you write it | Behaviour |
|---|---|---|
| **Tier 3 — unset** | `Button("Save")` | Property is never written; WinUI's own theme resources drive it. Perfect theming, no customization |
| **Tier 2 — `ThemeRef`** | `.Background(Theme.Accent)` | Stored in `Element.ThemeBindings`, resolved through the WinUI resource chain by `Reconciler.ApplyThemeBindings`, and **re-resolved on theme change** (the hosts' theme-change handlers clear `Theme`'s resolution cache). Custom styling *and* working dark mode / high contrast |
| **Tier 1 — concrete** | `.Background("#FF6B6B")` | Local-value set, highest DP precedence. Pins the value across theme changes — sometimes exactly what you want (a brand color, a data-driven chart series), sometimes a bug |

| WinUI Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| **Light/Dark/HighContrast (unstyled controls)** | Passthrough | Tier 3 — set no color modifiers | Responds to theme changes correctly |
| **Light/Dark/HighContrast (styled controls)** | Exposed | Tier 2 — `.Background(Theme.CardBackground)`, `.Foreground(Theme.PrimaryText)`, `.BorderBrush(...)`, `.WithBorder(ThemeRef, thickness)` | Theme-reactive styling. Tier 1 concrete values still opt out, by design |
| **Application.RequestedTheme** | Passthrough | Set via ReactorApp or `.Set()` | App-level theme selection |
| **Per-element RequestedTheme** | Exposed | `.RequestedTheme(ElementTheme.Dark)` | First-class modifier; applied before theme bindings are resolved so a subtree resolves against its own theme |
| **ThemeResource lookup** | Exposed | `Theme.Ref("SomeResourceKey")`, and 36 semantic tokens on `Theme` | Reactive, not a one-shot resolve. `ThemeResource` (the older read-only helper) still exists and is still one-shot — prefer `ThemeRef` |
| **Theme-reactive values** | Exposed | `Theme.Accent`, `Theme.PrimaryText`, `Theme.CardBackground`, `Theme.CardStroke`, … | The token system the original pass listed as "not yet built" |
| **Accent colors** | Exposed | `Theme.Accent`, `Theme.AccentSecondary`, `Theme.AccentTertiary`, `Theme.AccentDisabled` | Resolve from `SystemAccentColor` shades through the resource chain |
| **High contrast** | Exposed | Tier 2 tokens resolve through high-contrast dictionaries; `UseHighContrast()` and `UseHighContrastScheme()` hooks for layout/asset decisions | Tier 1 concrete colors still survive into high contrast — the remaining accessibility footgun |
| **Reacting to theme in component logic** | Exposed | `UseColorScheme()`, `UseIsDarkTheme()`, `UseHighContrast()`, `UseHighContrastScheme()` | For choosing assets or layout, not just brushes |
| **Guardrails** | Exposed | `REACTOR_THEME_001/002/003` analyzers | Flag hard-coded colors and suggest the matching token at build time |

**Verdict: the P0 is closed; a P2-sized footgun remains.** A developer no longer faces the
impossible choice the original document described. The path is: leave it unset (Tier 3), or
use a token (Tier 2). Both theme correctly. Reaching for Tier 1 is now a deliberate act that
an analyzer will question.

What is genuinely still open:

- **Tier 1 still silently wins.** `.Background("#FF0000")` overrides the theme with no runtime
  warning. The analyzers catch the common literal-color shapes at build time, but a color that
  arrives through a variable or a computed string is invisible to them.
- **`.Background()` is dropped entirely on shapes.** Not a theming bug as such, but it lands in
  the same "silently wrong color" bucket; see §5. `REACTOR_MOD_003` catches it at build time.
- **`{ThemeResource}` inside a dynamically-applied `Style` does not respect a per-element
  `RequestedTheme`.** This is a WinUI platform constraint, documented at the call site in
  `Reconciler.cs`, not something Reactor can fix.
- **`UseColorScheme()` reads `Application.Current.RequestedTheme`**, not the element-effective
  theme, so it does not compose with `.RequestedTheme()` on an ancestor. This is a real bug and
  is not yet fixed.

---

## 10. Visual State Manager

| WinUI Feature | Status | Notes |
|---|---|---|
| **VisualStateManager** | Replaced | Component state + conditional rendering |
| **VisualStateGroup** | Replaced | Multiple state variables in a component |
| **VisualState (Setters)** | Replaced | Different element properties per state |
| **VisualState (Storyboard)** | Partially replaced | Implicit transitions cover some cases; complex storyboard sequences need `.Set()` |
| **GoToState()** | Replaced | `setState(newState)` triggers re-render |
| **AdaptiveTrigger** | Replaced | `UseBreakpoint()` / `UseWindowSize()` hooks |
| **StateTrigger** | Replaced | Any boolean state variable |
| **Custom StateTriggerBase** | Replaced | UseEffect with custom condition |
| **VisualTransition** | Partially replaced | Implicit transitions (OpacityTransition, etc.) cover smooth property changes; no cross-state transition choreography |

**Verdict: Replaced.** Reactor's component state model replaces VSM entirely. The pattern is:

```csharp
// WinUI XAML: VisualStateManager.GoToState(this, "PointerOver", true);
// Reactor equivalent:
var (isHovered, setHovered) = UseState(false);
return Rectangle()
    .Fill(BrushHelper.Parse(isHovered ? "#0078D4" : "#CCCCCC"))
    .OpacityTransition()
    .OnPointerEntered((_, _) => setHovered(true))
    .OnPointerExited((_, _) => setHovered(false));
```

(Shapes take `.Fill()` / `.Stroke()`, not `.Background()` — see §1.9. Pointer events have
first-class modifiers, so the `.Set()` escape hatch this example used to need is gone.)

The trade-off: VSM transitions are declarative and run on the composition thread; Reactor's
approach requires a full re-render cycle for state changes. Implicit transitions mitigate
this for simple property animations.

---

## 11. Animations and Transitions

### 11.1 Animation Layers

| WinUI Layer | Status | Reactor Surface | Notes |
|---|---|---|---|
| **Theme Transitions** | Exposed | `.WithTransitions(new EntranceThemeTransition())` | Applied via ThemeTransitions property on elements |
| **Theme Animations** | Passthrough | Via `.Set()` inside Storyboard | Not wrapped; use WinUI Storyboard API directly |
| **Storyboarded Animations** | Passthrough | Via `.Set()` | No declarative storyboard DSL |
| **Connected Animations** | Exposed | `.ConnectedAnimation("key")` + `FlushConnectedAnimations()` | Source and destination elements share a key; also drivable from navigation via `NavigationTransition.Connected(key)` |
| **Composition Animations** | Augmented | `.Animate(Curve, AnimateProperty)` / `.SpringLayoutAnimation(dampingRatio, period)`, or `.Set()` + ElementCompositionPreview | A curve-based DSL covers the common cases; the raw compositor is still reachable |

### 11.2 Theme Transitions

| Transition | Status | Notes |
|---|---|---|
| EntranceThemeTransition | Exposed | `.WithTransitions(...)` |
| ContentThemeTransition | Exposed | `.WithTransitions(...)` |
| RepositionThemeTransition | Exposed | `.WithTransitions(...)` |
| AddDeleteThemeTransition | Exposed | `.ItemContainerTransitions(...)` |
| ReorderThemeTransition | Exposed | `.ItemContainerTransitions(...)` |
| PopupThemeTransition | Exposed | `.WithTransitions(...)` |
| EdgeUIThemeTransition | Exposed | `.WithTransitions(...)` |
| PaneThemeTransition | Exposed | `.WithTransitions(...)` |
| NavigationThemeTransition | Exposed | `.WithTransitions(...)` |

### 11.3 Implicit Transitions (Composition-backed)

| Transition | Status | Reactor Surface | Notes |
|---|---|---|---|
| Opacity | Exposed | `.OpacityTransition(duration?)` | ScalarTransition |
| Rotation | Exposed | `.RotationTransition(duration?)` | ScalarTransition |
| Scale | Exposed | `.ScaleTransition(components?)` | Vector3Transition |
| Translation | Exposed | `.TranslationTransition(duration?)` | Vector3Transition |
| Background | Exposed | `.BackgroundTransition(duration?)` | BrushTransition |

### 11.4 Not Wrapped

| Feature | Status | Notes |
|---|---|---|
| Custom keyframe animations | Passthrough | Use WinUI's DoubleAnimation/ColorAnimation via `.Set()` |
| Spring animations | Exposed | `Curve.Spring(dampingRatio, period)` fed to `.Animate(...)`, plus `.SpringLayoutAnimation(...)` |
| Expression animations | Passthrough | Use compositor.CreateExpressionAnimation via `.Set()` |
| Collection transitions | Passthrough | ItemCollectionTransitionProvider via `.Set()` |

### 11.5 Navigation Transitions

| Transition | Status | Reactor Surface |
|---|---|---|
| Entrance (page refresh) | Exposed | `NavigationTransition.Entrance()` — mirrors WinUI's `EntranceNavigationTransitionInfo`, the animation a `Frame` plays with no transition info |
| Fade / DrillIn / Slide / Spring | Exposed | `NavigationTransition.Fade(duration?)`, `.DrillIn(duration?)`, `.Slide(direction, duration?, easing?, distance?)`, `.Spring(dampingRatio, period, direction)`. DrillIn and parameterless Slide use WinUI's native timing, easing, scale, distance, and opacity staging; explicit duration/distance/easing values opt into Reactor's customizable variants. |
| Connected | Exposed | `NavigationTransition.Connected("key")` — pairs with `.ConnectedAnimation("key")` |
| Default / suppressed | Exposed | `NavigationTransition.Default` (alias for `Entrance()`), `NavigationTransition.None` (suppress) |

**Verdict: well-covered.** Theme transitions, implicit transitions, connected animations, and a
curve-based `.Animate()` DSL are all first-class; the router picks the page transition per
navigation rather than baking it into the target. Storyboards, expression animations, and
collection transition providers remain passthrough — deliberate, since they are authoring
surfaces for the composition layer rather than app-level concepts (§12).

---

## 12. Composition Visual Layer

| WinUI Feature | Status | Notes |
|---|---|---|
| **Visual / ContainerVisual / SpriteVisual** | Passthrough | Access via `ElementCompositionPreview.GetElementVisual()` in `.Set()` |
| **Compositor** | Passthrough | `CompositionTarget.GetCompositorForCurrentThread()` or from visual |
| **Composition Animations** | Passthrough | KeyFrame, Expression, Spring — all via compositor APIs |
| **ImplicitAnimations** | Exposed | Reactor's implicit transitions use this internally |
| **InteractionTracker** | Passthrough | Advanced input-driven animations via `.Set()` |
| **SwapChainPanel** | Passthrough | DirectX interop available but not wrapped |

**Verdict: Passthrough.** The composition layer is a low-level API that Reactor intentionally
doesn't wrap. Implicit transitions use it under the hood. For advanced composition work,
`.Set()` provides full access to the Visual and Compositor.

---

## 13. Materials and Effects

| WinUI Feature | Status | Notes |
|---|---|---|
| **MicaBackdrop** | Exposed | `.Backdrop(BackdropKind.Mica)` / `BackdropKind.MicaAlt`, or `WindowSpec.Backdrop` |
| **DesktopAcrylicBackdrop** | Exposed | `.Backdrop(BackdropKind.DesktopAcrylic)` / `BackdropKind.AcrylicThin` |
| **MicaController / AcrylicController** | Passthrough | `.Backdrop(Func<SystemBackdrop> factory)` takes a fully configured controller |
| **Composition Effects** | Passthrough | Win2D effects via compositor |
| **AcrylicBrush** | Exposed | `AcrylicBrush(tintColor, tintOpacity, fallbackColor, tintLuminosityOpacity)` factory |
| **RadialGradientBrush** | Passthrough | Usable as brush value |
| **XamlCompositionBrushBase** | Passthrough | Custom brush creation via raw API |
| **Lighting** | Passthrough | XamlLight subclasses via raw API |

**Verdict: system backdrops are wrapped; the rest is passthrough.** The convenience modifier the
original verdict asked for exists: `BackdropKind { None, Mica, MicaAlt, DesktopAcrylic,
AcrylicThin, Transparent }` covers the named materials, and the `Func<SystemBackdrop>` overload
is the escape hatch for a hand-configured controller. Effects, custom brushes, and lighting
remain visual-layer concerns applied via `.Set()`.

---

## 14. Input Handling

### 14.1 Control-Level Input (Semantic Events)

| Event Type | Status | Reactor Surface | Notes |
|---|---|---|---|
| Button.Click | Exposed | `OnClick` callback | All 7 button types |
| TextBox.TextChanged | Exposed | `OnTextChanged` callback | TextBox, AutoSuggestBox |
| CheckBox.Checked/Unchecked | Exposed | `OnChanged(bool)` callback | Simplified to single callback |
| Slider.ValueChanged | Exposed | `OnChanged(double)` callback | |
| ToggleSwitch.Toggled | Exposed | `OnChanged(bool)` callback | |
| ComboBox.SelectionChanged | Exposed | `OnSelectionChanged` callback | |
| ListView.SelectionChanged | Exposed | `OnSelectionChanged` callback | |
| RatingControl.ValueChanged | Exposed | `OnValueChanged` callback | |
| ColorPicker.ColorChanged | Exposed | `OnColorChanged` callback | |

### 14.2 Pointer Events

| Event | Status | Notes |
|---|---|---|
| PointerPressed/Released/Moved | Exposed | `.OnPointerPressed()`, `.OnPointerReleased()`, `.OnPointerMoved()` |
| PointerEntered/Exited | Exposed | `.OnPointerEntered()`, `.OnPointerExited()` |
| PointerCanceled/CaptureLost | Exposed | `.OnPointerCanceled()`, `.OnPointerCaptureLost()` |
| PointerWheelChanged | Exposed | `.OnPointerWheelChanged()` |

### 14.3 Gesture Events

| Event | Status | Notes |
|---|---|---|
| Tapped/DoubleTapped | Exposed | `.OnTapped()`, `.OnDoubleTapped()`, plus a synthetic `.OnDoubleTap(Action)` / `.OnDoubleTap(Action<Point>)` |
| RightTapped | Exposed | `.OnRightTapped()` |
| Holding | Exposed | `.OnHolding()`, plus `.OnLongPress(action, threshold?, slop?, ...)` |
| Pan / Pinch / Rotate | Augmented | `.OnPan(Action<PanGesture>)`, `.OnPinch(Action<PinchGesture>)`, `.OnRotate(Action<RotateGesture>)` — composed recognizers with no direct WinUI equivalent |

### 14.4 Manipulation Events

| Feature | Status | Notes |
|---|---|---|
| ManipulationMode | Missing | No modifier; set via `.Set()` |
| ManipulationStarted/Delta/Completed | Passthrough | Via `.Set()`. The `.OnPan()` / `.OnPinch()` / `.OnRotate()` recognizers cover most reasons you would reach for these |
| ManipulationInertiaStarting | Passthrough | Via `.Set()` |

### 14.5 Keyboard Input

| Feature | Status | Notes |
|---|---|---|
| KeyDown/KeyUp | Exposed | `.OnKeyDown()`, `.OnKeyUp()` |
| PreviewKeyDown/PreviewKeyUp | Exposed | `.OnPreviewKeyDown()`, `.OnPreviewKeyUp()` |
| CharacterReceived | Exposed | `.OnCharacterReceived()` |
| InputKeyboardSource.GetKeyStateForCurrentThread | Passthrough | Direct API call |
| AddHandler (handledEventsToo) | Passthrough | Via `.Set()` |

### 14.6 Keyboard Accelerators

| Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| KeyboardAccelerator | Exposed | `Accelerator(key, modifiers)` data + element property | KeyboardAcceleratorData record; applied by reconciler; also carried on a `Command` |
| ScopeOwner | Passthrough | Via `.Set()` | |
| Auto-tooltip | Passthrough | WinUI handles natively | |

### 14.7 Access Keys / Focus / Drag-Drop

| Feature | Status | Notes |
|---|---|---|
| AccessKey | Exposed | `.AccessKey("A")`; also a `Command.AccessKey` field |
| AccessKeyDisplayRequested | Exposed | `.OnAccessKeyDisplayRequested()` |
| IsAccessKeyScope | Missing | Via `.Set()` |
| FocusManager methods | Augmented | `UseElementFocus(FocusState)` hook + `ElementRef`; raw FocusManager still callable |
| Focus events | Exposed | `.OnGotFocus()`, `.OnLostFocus()` |
| IsTabStop / TabIndex | Exposed | `.IsTabStop(bool)`, `.TabIndex(int)` |
| TabFocusNavigation | Exposed | `.TabNavigation(KeyboardNavigationMode)` |
| XYFocus properties | Exposed | `.XYFocusUp/Down/Left/Right(ElementRef)`, `.XYFocusKeyboardNavigation(mode)` |
| CanDrag / AllowDrop | Replaced | You never set these flags: `.OnDragStart(...)` sets `UIElement.CanDrag` and `.OnDrop(...)` sets `AllowDrop` for you. `.DraggableWhen(Func<bool>)` gates an attached drag source. `TabView` additionally has `.CanDragTabs()` / `.AllowDropTabs()` |
| DragStarting / DragOver / Drop | Augmented | `.OnDragStart<T>(payloadFactory, operations?, onEnd?)`, `.OnDragEnter/DragLeave/DragOver(Action<DragTargetArgs>)`, `.OnDrop<T>(Action<T>)` — typed payloads instead of `DataPackage` string keys |

**Verdict: input is now first-class, not an escape hatch.** The original verdict — "low-level
pointer, gesture, keyboard, and manipulation events are all passthrough via `.Set()`; access
keys, focus management, tab navigation, XY focus, and drag-drop are all missing" — is obsolete.
Pointer, gesture, keyboard, focus, access-key, and drag-drop surfaces all have typed modifiers,
and drag-drop and the composed gestures are *better* than the WinUI originals because the
payload is generic and the recognizers are prebuilt.

The one genuine remaining hole is the **manipulation** family (`ManipulationMode` and the
`Manipulation*` events), which is still `.Set()`-only.

---

## 15. Commands

| WinUI Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| **ICommand** | Replaced | `Command` record | Bundles `Label`, `Execute` / `ExecuteAsync`, `CanExecute`, `Icon`, `Description`, `Accelerator`, `AccessKey`, `DebounceMs`. Immutable, named, reusable — the thing a callback isn't |
| **ICommand (interop)** | Augmented | `CommandInterop.FromCommand(ICommand, label, …)` → `Command` (and a `Command<T>` overload) | **One direction only:** adopts an existing MVVM / CommunityToolkit `ICommand` as a Reactor `Command`, for gradual migration. There is no adapter the other way |
| **Command with parameter** | Replaced | `Command<T>` | Typed parameter instead of `object` |
| **XamlUICommand** | Replaced | `Command` | Label + icon + accelerator + description are fields on the same record |
| **StandardUICommand** | Replaced | `StandardCommand.Cut/Copy/Paste/Undo/Redo/Delete/SelectAll/Save/Open/Close/Share/Play/Pause/Stop/Forward/Backward(execute)` | 16 pre-labelled commands, each with sync and `Func<Task>` overloads plus a `canExecute` flag. Most carry a conventional `Icon` and `Accelerator`, but not uniformly — `SelectAll` and `Close` have no icon, and the media commands (`Share`, `Play`, `Pause`, `Stop`, `Forward`, `Backward`) have no accelerator |
| **Command property on controls** | Exposed | `Button(command)`, `AppBarButton(command)`, `MenuItem(command)`, `HyperlinkButton(command)`, `RepeatButton(command)`, `ToggleButton(command)`, `SplitButton(command)`, `ToggleSplitButton(command)` | A command binds to many controls; they all reflect its state |
| **CanExecute / auto-disable** | Exposed | `Command.IsEnabled` = `CanExecute && !IsExecuting && !IsDebouncing` | Bound controls set `IsEnabled` automatically — no per-control `.IsEnabled(!condition)` wiring |
| **CanExecuteChanged** | Replaced | Re-render | The command record is rebuilt with a new `CanExecute` during render; every bound control updates in the same pass. No event subscription, no leak |
| **Async / in-flight state** | Augmented | `ExecuteAsync` + `IsExecuting`, `UseCommand(command)` hook | Controls disable themselves for the duration of an async execute — no WinUI equivalent |
| **Debounce** | Augmented | `Command.DebounceMs` + `IsDebouncing` | No WinUI equivalent |
| **Command scope** | Augmented | `CommandHost(commands, child)` | Makes a command set available to a subtree |

**Verdict: Replaced, and the P0 is closed.** The original verdict — "Reactor has no command
abstraction" — is obsolete. `Command` is a named, queryable, reusable action whose
enable/disable state propagates automatically to every control bound to it, which is precisely
what the original text said had no equivalent. The async and debounce states are additions
over `ICommand`.

One thing is deliberately *not* replicated: Reactor's `Command` is **not** an `ICommand`.
Interop is one-way — `CommandInterop.FromCommand` adopts an existing `ICommand` into Reactor,
but there is no adapter for handing a Reactor `Command` to a surface that demands an `ICommand`.

---

## 16. Accessibility

| WinUI Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| **AutomationProperties.Name** | Exposed | `.AutomationName("label")` | Several controls also derive a sensible default from their caption |
| **AutomationProperties.LabeledBy** | Exposed | `.LabeledBy(ElementRef)` / `.LabeledBy("labelAutomationId")` | Two overloads: a captured ref, or a by-id lookup |
| **AutomationProperties.HelpText** | Exposed | `.HelpText("text")` | |
| **AutomationProperties.LiveSetting** | Exposed | `.LiveRegion(AutomationLiveSetting.Polite)` | Defaults to Polite |
| **AutomationProperties.AutomationId** | Exposed | `.AutomationId("id")` | Also the anchor for `.LabeledBy(string)` and for E2E tests |
| **AutomationProperties.AccessibilityView** | Exposed | `.AccessibilityView(AccessibilityView)` | |
| **AutomationProperties.HeadingLevel** | Exposed | `.HeadingLevel(AutomationHeadingLevel)` | |
| **AutomationProperties.LandmarkType** | Exposed | `.Landmark(AutomationLandmarkType)` | |
| **AutomationProperties.IsRequiredForForm** | Exposed | `.Required()` | Sets the `IsRequiredForForm` element property |
| **Live Regions** | Exposed | `.LiveRegion(...)` | Announcement on content change |
| **Keyboard accessibility** | Exposed | `.IsTabStop()`, `.TabIndex()`, `.XYFocus*()`, `.AccessKey()`, `UseElementFocus()` | See §14.7 |
| **Custom AutomationPeer** | Blocked | — | Reactor components are not `Control` subclasses, so `OnCreateAutomationPeer` cannot be overridden. Reactor supplies its own peers where it must (e.g. `SemanticPanelAutomationPeer`), but an app cannot author one for a component |
| **UIA Tree Views** | Passthrough | WinUI's automation tree works on rendered controls | |
| **Build-time guardrails** | Augmented | `REACTOR_A11Y_001`–`004` analyzers | 001 icon-only button needs a name; 002 image needs alt text or `.AccessibilityHidden()`; 003 form field needs a label; 004 clickable container must be keyboard-reachable. No WinUI equivalent |

**Verdict: Exposed, and the P0 is closed.** The original verdict — "only
`AutomationProperties.Name` has a first-class modifier … accessibility is the largest gap for
production apps" — is obsolete. Every automation property it named as missing now has a
modifier, and the analyzer suite goes further than WinUI by catching the four most common
accessibility mistakes at build time rather than in an audit.

The remaining hard limit is **custom automation peers**. Because a Reactor component is a
render function and not a `Control` subclass, there is no `OnCreateAutomationPeer` to override,
so an app cannot publish a bespoke control pattern (e.g. a custom `IValueProvider`) for a
composed component. Elements fall back to the automation peer of whatever WinUI control they
mount, which is correct for every wrapped control and merely coarse for novel compositions.

---

## 17. Threading Model

| WinUI Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| **DispatcherQueue** | Exposed | Render loop uses DispatcherQueue; state batching built-in | Multiple state changes in one sync block → single re-render |
| **DispatcherQueue priorities** | Passthrough | Direct API access | |
| **HasThreadAccess** | Passthrough | Direct API check | |
| **DispatcherQueueController** | Passthrough | ReactorApp sets up main thread queue | |
| **Dedicated thread queues** | Passthrough | Create via DispatcherQueueController API | |
| **System DispatcherQueue** | Passthrough | EnsureSystemDispatcherQueue available | |

**Verdict: Well-handled.** Reactor's render loop is built on DispatcherQueue with automatic
batching — this is a significant improvement over manual Dispatcher.Invoke patterns. Async
state updates from background threads must marshal via SynchronizationContext.Post, which
is standard .NET practice.

---

## 18. Windowing

| WinUI Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| **AppWindow** | Passthrough | Via `ReactorHost.Window.AppWindow` and `ReactorWindow` | |
| **Size and Position** | Exposed | `WindowSpec.StartPosition` / `ManualPosition` / `SizeToContent`; `UseWindowSize()`, `UseWindowPosition()` hooks | Declarative at open, observable at runtime |
| **OverlappedPresenter** | Exposed | `WindowSpec.Presenter = PresenterKind.Overlapped` | Plus `IsMaximizable`, `IsMinimizable`, `ResizeMode`, `Style` |
| **FullScreenPresenter** | Exposed | `PresenterKind.FullScreen`; `UseWindowState()` reports `WindowState.FullScreen` | |
| **CompactOverlayPresenter** | Exposed | `PresenterKind.CompactOverlay` | |
| **Title bar customization** | Exposed | TitleBarElement, `WindowSpec.TitleBarHeight`, `.IsDragRegion()` | Custom content, left/right headers, drag regions (spec 059) |
| **AppWindow color properties** | Passthrough | Via `.Set()` on TitleBar or AppWindow.TitleBar | |
| **SetDragRectangles** | Exposed | `.IsDragRegion()` modifier + `AutoRefreshDragRegions` | Regions are derived from the element tree instead of hand-computed rectangles |
| **Multi-window** | Exposed | `UseOpenWindow(WindowKey, WindowSpec, Func<Component>)` → `ReactorWindow` | Keyed, so re-opening the same key **reuses** the existing window rather than duplicating it — call `ReactorWindow.Activate()` on the returned handle to bring it forward. `WindowSpec.Owner` gives modal/owned relationships |
| **Window backdrop / corners / level** | Exposed | `WindowSpec.Backdrop`, `CornerStyle`, `Level`, `Icon`, `Opacity` | |
| **Window persistence** | Augmented | `WindowSpec.PersistenceFallback` + `WindowStartPosition` | Save/restore placement with a fallback policy; no WinUI equivalent |
| **AppWindow events** | Augmented | `UseWindowState()`, `UseWindowSize()`, `UseWindowPosition()`, `UseWindowDragMove()`, `UseWindowAspectRatio()` | Hooks instead of event subscriptions |

**Verdict: Windowing is a strength, not a gap.** The original verdict ("multi-window is
missing … not currently supported by the framework") is obsolete: `UseOpenWindow` opens and
manages additional windows, each with its own component tree, keyed so that a second open with
the same key reuses the existing window instead of creating a duplicate. See
`036-window-design.md` and `054-windowing-evolution.md`.

---

## 19. Application Lifecycle

| WinUI Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| **Application.OnLaunched** | Exposed | ReactorApplication.OnLaunched handles setup | Automatic; users don't override |
| **Activation kinds** | Exposed | `LaunchActivation` (`Kind`, `Arguments`, `Files`) with `LaunchKind { Normal, JumpList, Toast, Protocol, File }` | Available from the app context at startup |
| **Protocol / deep-link activation** | Augmented | `LaunchActivation.TryResolve(DeepLinkMap<TRoute>, out result)` | Resolves a launch URI straight to a route, optionally seeding a back stack |
| **Jump lists** | Exposed | `JumpListItem.ForCommandLine(...)` / `.ForUri(...)` | Round-trips back through `LaunchKind.JumpList` |
| **Single-instancing** | Missing | — | `WindowKey` is **process-scoped**, so it cannot dedupe across launches; a second launch starts a second process with its own windows. `AppInstance` redirection is explicitly deferred in `036-window-design.md` |
| **Suspension** | N/A | Desktop WinUI 3 doesn't have UWP suspension | |
| **UnhandledException** | Exposed | `ReactorApplication.OnUnhandledException` static callback | Set handler before Run(). Note: access violations from native code are *not* catchable here — see §24 |

**Verdict: activation is covered; instancing is not.** The original verdict said both "advanced
activation and instancing are missing"; only the activation half is now obsolete. File, protocol,
toast, and jump-list activation all resolve through `LaunchActivation` without manual
`Program.Main` plumbing. Single-instancing still requires the app to do its own `AppInstance`
redirection before calling `ReactorApp.Run`.

---

## 20. App Services

| WinUI Feature | Status | Notes |
|---|---|---|
| **Printing** | Passthrough | Use PrintManagerInterop directly with the HWND from `ReactorHost.Window` |
| **Clipboard** | Passthrough | Use Windows.ApplicationModel.DataTransfer.Clipboard |
| **File Pickers** | Exposed | `UseFilePickerAsync(FilePickerOptions)` → `Task<StorageFile?>` — owner-window initialization is handled for you |

**Verdict: mostly passthrough, with file pickers wrapped.** File picking was the one OS service
where the HWND-initialization dance was both mandatory and easy to get wrong, so it has a hook.
Clipboard and printing are still direct WinRT calls; a broader `Reactor.Services` surface
remains a natural future addition.

---

## 21. Interop

| WinUI Feature | Status | Notes |
|---|---|---|
| **HWND access** | Exposed | Via ReactorHost.Window → WindowNative.GetWindowHandle |
| **XAML Islands** | N/A | Reactor IS the host; embedding Reactor in non-XAML apps not supported |
| **C++/WinRT projections** | N/A | Reactor is C#-only |
| **C#/WinRT projections** | Passthrough | Standard CsWinRT; Reactor apps use .NET TFM with Windows target |
| **WinRT Component authoring** | Passthrough | Standard CsWinRT authoring; orthogonal to Reactor |
| **IInitializeWithWindow** | Passthrough | Use with HWND from ReactorHost.Window |

**Verdict: Passthrough.** Interop is orthogonal to Reactor's UI layer. HWND access is available
for APIs that need it.

---

## 22. Content and Items Infrastructure

| WinUI Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| **ContentControl pattern** | Replaced | Every element can contain children | Reactor's element tree replaces Content property |
| **ItemsControl / ItemsSource** | Replaced | Collection elements with template lambdas | `ListView(items, item => ...)` |
| **Selector (SelectedItem/Index)** | Exposed | SelectedIndex/SelectedItem on collection elements | |
| **Virtualization (ListView/GridView)** | Exposed | Built-in via underlying WinUI controls | |
| **Virtualization (ItemsRepeater)** | Exposed | `ItemsRepeater<T>(items, viewBuilder)`; also powers LazyVStack/LazyHStack | |
| **Incremental loading** | Exposed | `.IncrementalLoadingTrigger(...)` on ListView/GridView | |
| **x:Phase incremental loading** | Blocked | No XAML phase annotations | |
| **ContainerContentChanging** | Passthrough | Via `.Set()` | |
| **ItemsSourceView** | Passthrough | Used internally | |
| **SelectionModel** | Passthrough | Via `.Set()` for hierarchical selection | |
| **ElementFactory / RecyclingElementFactory** | Replaced | Reconciler handles element recycling internally | Element pooling for TextBlock, StackPanel, Grid, Border, etc. |

**Verdict: Core collection infrastructure is well-exposed.** Virtualization works through
WinUI's built-in mechanisms. Element recycling is handled by Reactor's reconciler. x:Phase
is blocked due to no XAML.

---

## 23. Summary Scorecard

### By Feature Area

Counts are of *table rows in the section above*, so they measure breadth of coverage, not
importance. `N/A` rows (features that cannot apply to a no-XAML framework) are excluded, as are
the two `Partially replaced` rows in §10.

| # | Feature Area | Exposed | Augmented | Replaced | Passthrough | Missing | Blocked |
|---|---|---|---|---|---|---|---|
| 1 | Built-in Controls | 76 | 4 | 1 | 1 | 4 | — |
| 2 | Layout System | 5 | 2 | 2 | 7 | 1 | — |
| 3 | Navigation | 5 | 1 | 3 | — | — | 1 |
| 4 | Data Binding | — | — | 12 | — | — | — |
| 5 | Dependency Properties | — | 1 | 2 | 2 | — | 1 |
| 6 | XAML Markup | — | — | 10 | — | — | 3 |
| 7 | Resources | 2 | — | — | 4 | — | — |
| 8 | Styling | 1 | 1 | 1 | 2 | — | 2 |
| 9 | Theming | 8 | — | — | 2 | — | — |
| 10 | Visual State Manager | — | — | 7 | — | — | — |
| 11 | Animations | 20 | 1 | — | 5 | — | — |
| 12 | Composition Layer | 1 | — | — | 5 | — | — |
| 13 | Materials/Effects | 3 | — | — | 5 | — | — |
| 14 | Input Handling | 26 | 3 | 1 | 6 | 2 | — |
| 15 | Commands | 2 | 4 | 5 | — | — | — |
| 16 | Accessibility | 11 | 1 | — | 1 | — | 1 |
| 17 | Threading | 1 | — | — | 5 | — | — |
| 18 | Windowing | 8 | 2 | — | 2 | — | — |
| 19 | App Lifecycle | 4 | 1 | — | — | 1 | — |
| 20 | App Services | 1 | — | — | 2 | — | — |
| 21 | Interop | 1 | — | — | 3 | — | — |
| 22 | Items Infrastructure | 4 | — | 3 | 3 | — | 1 |

### What changed since the first pass

**All four** of the original **P0** gaps have shipped, as have all six **P1**s. The list below
is kept as a record so the document's own history stays auditable.

| Original priority | Gap | Now |
|---|---|---|
| P0 | Theming: ThemeRef token system + theme-reactive modifiers | **Shipped** — `Theme.*` tokens, `.Background(ThemeRef)`, re-resolve on theme change, `REACTOR_THEME_*` analyzers (§9) |
| P0 | Accessibility modifiers | **Shipped** — 11 automation modifiers + `REACTOR_A11Y_001`–`004` (§16) |
| P0 | Navigation: native router with back stack, transitions, parameters | **Shipped** — `NavigationHandle<TRoute>`, `NavigationHost`, `NavigationTransition`, `DeepLinkMap` (§3) |
| P0 | Command model | **Shipped** — `Command` / `Command<T>`, CanExecute auto-disable, async + debounce (§15) |
| P1 | Lightweight styling | **Shipped** — `.Resources(r => r.Set(key, value))` (§8) |
| P1 | Focus management modifiers | **Shipped** — `.IsTabStop()`, `.TabIndex()`, `.TabNavigation()`, `.XYFocus*()` (§14.7) |
| P1 | Access key modifiers | **Shipped** — `.AccessKey()` (§14.7) |
| P1 | Drag-and-drop modifiers | **Shipped** — typed `.OnDragStart<T>()` / `.OnDrop<T>()` (§14.7) |
| P1 | Multi-window support | **Shipped** — `UseOpenWindow(WindowKey, WindowSpec, …)` (§18) |
| P1 | ToolTip full feature set | **Shipped** — `.ToolTipPlacement()`, `.ToolTipPlacementTarget()`, plus placement overloads of `.ToolTip()` / `.WithToolTip()` (§1.6) |
| P2 | Connected animations | **Shipped** — `.ConnectedAnimation(key)` + `NavigationTransition.Connected` (§11) |
| P2 | Per-element RequestedTheme | **Shipped** — `.RequestedTheme()` (§9) |
| P2 | ManipulationMode modifier | **Still open** |
| P3 | Activation kinds | **Shipped** — `LaunchActivation` / `LaunchKind` / `DeepLinkMap` (§19). **Single-instancing did not** — `AppInstance` redirection is still the app's job |
| P3 | InkCanvas / InkToolbar | **Still open** |
| P3 | App service wrappers | **Partly** — `UseFilePickerAsync` shipped; clipboard and printing still raw (§20) |

### Top Gaps to Address (Priority Order)

| Priority | Gap | Impact | Effort |
|---|---|---|---|
| **P1** | Tier-1 concrete colors silently override theming | The remaining "my color is wrong and nothing told me" class. `REACTOR_THEME_*` covers the literal cases; a color arriving via a variable or computed string is still invisible to it | Low–Medium — widen analyzer coverage |
| **P1** | `UseColorScheme()` reads the app-level theme, not the element-effective theme | Doesn't compose with `.RequestedTheme()` on an ancestor; a subtree renders theme-aware content for the wrong theme | Low — resolve through the element |
| **P1** | Custom AutomationPeer for components | A composed control cannot publish its own UIA control pattern; blocks bespoke accessible widgets | High — needs a peer-authoring seam that doesn't require subclassing `Control` |
| **P2** | WinUI `Frame` with code-only `Page` types | Blocked by design, not by omission — see §3. Listed so the decision stays visible, not as scheduled work | N/A — `NavigationHost` is the supported path |
| **P2** | ControlTemplate authoring / re-templating | Cannot restyle a WinUI control's internals from Reactor; must compose a replacement instead | High — arguably out of scope by design |
| **P2** | Manipulation family (`ManipulationMode`, `Manipulation*` events) | Custom direct-manipulation surfaces still need `.Set()`; the pan/pinch/rotate recognizers cover most cases | Low — add modifiers |
| **P2** | App-level resource dictionary DSL | Merged/theme dictionaries still need `.Set()` on `Application.Resources` | Medium |
| **P3** | TwoPaneView | Dual-screen / list-detail layout | Low — add element + descriptor |
| **P3** | InkCanvas / InkToolbar / CaptureElement | Blocks inking and capture apps | Medium — stateful controls, non-trivial diffing |
| **P3** | Clipboard and printing wrappers | Convenience; not blocking | Medium — new `Reactor.Services` surface |
| **P3** | `IsAccessKeyScope` | Scoped Alt-key mnemonics | Low — add modifier |

### Architectural Trade-offs

| WinUI Strength Lost | Reactor Strength Gained |
|---|---|
| Visual designer / XAML hot reload | Full IntelliSense, refactoring, type safety |
| ControlTemplate re-templating | Simpler composition model; no template part contracts |
| Declarative VisualStateManager | Any C# logic can drive visual state |
| Compiled {x:Bind} with zero-overhead | No binding errors, no DataContext confusion |
| XAML resource forward-reference chain | Standard C# scoping rules |
| DP value precedence system | Three explicit tiers (unset / `ThemeRef` / concrete) instead of an implicit precedence chain — at the cost of no style-tier write |
| `Frame.Navigate(typeof(Page), object)` | Routes are ordinary typed C# values, so navigation parameters are compile-time checked |
| ICommand's `CanExecuteChanged` event | `Command` is recomputed during render, so there is no subscription to leak — at the cost of a render pass per state change |
| Custom AutomationPeer per control | Analyzer-enforced accessibility defaults — at the cost of no bespoke UIA patterns |
| x:Phase incremental rendering | (no equivalent; reconciler batching partially compensates) |

---

## 24. Keeping This Document Honest

This document has gone stale once already: it kept four P0 gaps open in print long after
all four had shipped. It will do so again unless re-verified deliberately.

When a PR lands that changes coverage, or on a scheduled pass:

1. Regenerate the API index (`mur --regen-api`) and diff it — new `Exposed` rows usually appear
   there first.
2. Re-grep the specific symbols named in the rows you are changing; do not trust the prose.
   A row naming an API that no longer exists (or never did) is this document's most common
   failure — the 2026-07-29 pass found `TextField(...)`, which was renamed to `TextBox` in #387
   and deleted in #390, still cited in three places.
3. Recompute the §1 Controls Summary totals and the §23 scorecard. The pre-2026-07-29 version of
   this document had a totals row that contradicted its own prose (4 vs 5 missing) — the counts
   are easy to break. Derive them by counting status words in each section, not by hand.
4. Check that a row's Status word agrees with its own Notes. "Exposed" beside a note explaining
   why the feature doesn't work is the failure mode that shipped §3's `Frame` row.
5. Grep the sibling specs that cite this one (`006`, `011`, `018`, `053`, and
   `docs/research/compare/overview.md`). A verdict reversed here usually leaves a stale quotation
   there.
6. Update the **Status** line at the top with the new verification date.
