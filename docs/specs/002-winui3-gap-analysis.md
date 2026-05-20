# Reactor Framework vs WinUI 3 — Comprehensive Gap Analysis

How every WinUI 3 application-programming feature is exposed, replaced, augmented,
hidden, or blocked by the Reactor framework design.

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
| **CheckBox** | Exposed | `CheckBox(label, isChecked, onChanged)` | Tri-state via `.Set()` |
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
| **TextBox** | Augmented | `TextField(text, onChanged)` | Renamed for clarity; TextFieldElement |
| **RichEditBox** | Exposed | `RichEditBox(...)` | RichEditBoxElement |
| **PasswordBox** | Exposed | `PasswordBox(password, onChanged)` | PasswordBoxElement |
| **AutoSuggestBox** | Exposed | `AutoSuggestBox(text, items, onChanged, onQuery)` | AutoSuggestBoxElement |

**Verdict: 6/6 exposed (2 augmented).** Text system is one of Reactor's strengths — convenience
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
| **ItemsView** | Exposed | `ItemsView<T>(items, template)` | ItemsViewElement\<T\> |
| **ItemsRepeater** | Passthrough | Used internally by LazyVStack/LazyHStack | Not exposed as standalone element; used as implementation detail |
| **FlipView** | Exposed | `FlipView(items, template)` | TemplatedFlipViewElement\<T\> available |
| **TreeView** | Exposed | `TreeView(items)` | TreeViewElement with drag support |

**Verdict: 5/6 exposed, 1 passthrough.** ItemsRepeater is used internally but not exposed as
a standalone element. LazyVStack/LazyHStack provide the typical use case (virtualizing list
with custom layout). SemanticZoom is also exposed (SemanticZoomElement).

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
| **ToolTip** | Missing | `.ToolTip("text")` / `.WithToolTip(element)` modifier | Basic text tooltips work, but the full WinUI ToolTip feature set (placement, timing, rich content styling, ToolTipService properties) is not exposed; needs first-class element or richer modifier |
| **Popup** | Exposed | `Popup(content)` | PopupElement |

**Verdict: 6/7 exposed, 1 missing.** ToolTip has a basic modifier but lacks the full
WinUI ToolTip feature set (placement, timing, rich content control). Needs a richer API
to expose the full platform capability.

### 1.7 Menus, Toolbars, and Commands

| WinUI Control | Status | Reactor Surface | Notes |
|---|---|---|---|
| **MenuBar** | Exposed | `MenuBar(items)` | MenuBarElement |
| **MenuBarItem** | Exposed | Via MenuBar items | Part of MenuBar data model |
| **CommandBar** | Exposed | `CommandBar(primaryCommands, ...)` | CommandBarElement |
| **AppBarButton** | Exposed | Via CommandBar items | Part of CommandBar data model |
| **AppBarToggleButton** | Passthrough | Via `.Set()` on CommandBar items | Not separately wrapped |
| **AppBarSeparator** | Passthrough | Via `.Set()` on CommandBar items | Not separately wrapped |

**Verdict: 4/6 exposed, 2 passthrough.** AppBarToggleButton and AppBarSeparator are usable
via the CommandBar element's item model but don't have dedicated factories.

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

**Verdict: 7/10 exposed (1 augmented), 3 missing.** InkCanvas, InkToolbar, and CaptureElement
are not wrapped. These are specialized controls that can still be used via `.Set()` on a host
container, or via `Reconciler.RegisterType<>()` to create custom element types.

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
| 1.2 Text | 6 | 4 | 2 | — | — | — |
| 1.3 Icons | 6 | 6 | — | — | — | — |
| 1.4 Collections | 6 | 4 | 1 | — | 1 | — |
| 1.5 Date/Time | 4 | 4 | — | — | — | — |
| 1.6 Dialogs | 7 | 6 | — | — | — | 1 |
| 1.7 Menus/Toolbars | 6 | 4 | — | — | 2 | — |
| 1.8 Navigation | 8 | 7 | — | 1 | — | — |
| 1.9 Media | 10 | 6 | 1 | — | — | 3 |
| 1.10 Status | 5 | 5 | — | — | — | — |
| 1.11 Scrolling | 4 | 3 | — | — | 1 | — |
| 1.12 Containers | 6 | 4 | 1 | — | — | 1 |
| 1.13 Title Bar | 1 | 1 | — | — | — | — |
| **Totals** | **86** | **71** | **6** | **1** | **4** | **4** |

**Overall control coverage: 81/86 (94%) accessible, 5 missing (InkCanvas, InkToolbar,
CaptureElement, TwoPaneView, full ToolTip).**

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
| Custom panels via MeasureOverride/ArrangeOverride | Passthrough | FlexPanel implements this internally; users can create custom panels via `Reconciler.RegisterType<>()` |
| InvalidateMeasure/InvalidateArrange | Passthrough | Available via `.Set()` |

**Verdict: Passthrough.** Reactor's virtual element tree doesn't directly expose the
measure/arrange cycle. The reconciler handles layout property changes, which trigger
WinUI's built-in invalidation. Custom panel creation requires `Reconciler.RegisterType<>()`
with manual measure/arrange overrides.

### 2.3 Attached Layouts (ItemsRepeater)

| Feature | Status | Notes |
|---|---|---|
| StackLayout | Passthrough | Used internally by LazyVStack |
| UniformGridLayout | Passthrough | Configurable via `.Set()` |
| FlowLayout | Passthrough | Configurable via `.Set()` |
| LinedFlowLayout | Passthrough | Configurable via `.Set()` |
| Custom VirtualizingLayout | Passthrough | Possible via `.Set()` on ItemsRepeater host |

**Verdict: Passthrough.** ItemsRepeater is used internally by LazyVStack/LazyHStack but not
directly exposed. Attached layouts are configurable via `.Set()` on the underlying control.

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
| **Frame + Page navigation** | Blocked | — | Page subclasses require XAML code-behind files; cannot define Pages in pure Reactor. Component state switching is a workaround but lacks transitions, parameter passing, and lifecycle hooks |
| **Back stack management** | Missing | — | No Reactor equivalent; no automatic back stack, no history, no forward/back. Apps must build their own navigation state management |
| **TabView** | Exposed | TabViewElement | Full tab management with selection |
| **BreadcrumbBar** | Exposed | BreadcrumbBarElement | Click handler per item |
| **SelectorBar** | Exposed | SelectorBarElement | View switching |
| **Frame.Navigate(typeof(Page))** | Blocked | FrameElement exists but requires XAML Pages | Frame element can be mounted but is unusable without XAML-defined Page types |

**Verdict: Navigation is generally broken.** While the navigation *controls* (NavigationView,
TabView, BreadcrumbBar) work as UI elements, the core navigation *scenario* — Frame/Page
with back stack, transitions, and parameter passing — is blocked because Page subclasses
require XAML files. The component-state-switching workaround (`currentPage == "home" ? ...`)
is functional but loses automatic back-stack management, navigation transitions, and the
standard WinUI navigation lifecycle. A Reactor-native navigation system (router, history stack,
transition coordination) is needed to close this gap.

---

## 4. Data Binding

| WinUI Feature | Status | Reactor Replacement | Notes |
|---|---|---|---|
| **{x:Bind}** | Replaced | Direct property access in render | No markup; values flow from component state to element properties in render() |
| **{Binding}** | Replaced | UseObservable hook | Bridges INotifyPropertyChanged to re-renders |
| **OneTime mode** | Replaced | Constant values in render | A non-state value is effectively one-time |
| **OneWay mode** | Replaced | UseState + render | State changes trigger re-render, updating all outputs |
| **TwoWay mode** | Replaced | UseState + OnChanged callback | `TextField(text, t => setText(t))` is two-way |
| **INotifyPropertyChanged** | Replaced | UseObservable hook | `UseObservable(viewModel)` subscribes to changes |
| **ObservableCollection** | Replaced | UseCollection hook | `UseCollection(items)` re-renders on add/remove |
| **IValueConverter** | Replaced | Inline C# expressions | `Text($"${price:F2}")` — no converter classes needed |
| **Function bindings** | Replaced | Inline C# functions | `Text(FormatDate(date))` — just call the function |
| **FallbackValue/TargetNullValue** | Replaced | Null-coalescing / conditional | `Text(value ?? "N/A")` |
| **DataTemplate {x:DataType}** | Replaced | Lambda templates | `ListView(items, item => Text(item.Name))` |

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
| **Value precedence** | Blocked | Reactor's modifiers always do local property sets (highest precedence), which overrides styles, theme resources, and template values. Until ThemeRef (theming design spec) is implemented, there is no way to set a value at the style/theme tier of WinUI's precedence chain |
| **Attached properties** | Augmented | Type-safe `.Grid(row:, col:)` / `.Canvas(left:, top:)` / `.Flex(grow:)` extensions stored in Element.Attached dictionary |
| **RegisterPropertyChangedCallback** | Passthrough | Available via `.Set()` for instance-level observation |
| **ClearValue** | Passthrough | Available via `.Set()` |

**Verdict: Partially blocked.** The DP system is the backbone of XAML but is invisible in
Reactor's programming model. Element properties are plain C# record fields. The reconciler
translates these to real DP values on mount/update. Attached properties are reimplemented
as a type-safe dictionary system. However, value precedence is blocked: every Reactor modifier
does a local-value set, which sits at the top of WinUI's precedence chain and overrides
styles and theme resources. This is the root cause of theming breakage (see Section 9).

---

## 6. XAML Markup Features

| WinUI Feature | Status | Notes |
|---|---|---|
| **{x:Bind}** | Blocked | No XAML → no markup extensions |
| **{Binding}** | Blocked | No XAML |
| **{StaticResource}** | Blocked | No XAML; resource access via `.ApplyStyle()` or ThemeResource helpers |
| **{ThemeResource}** | Replaced | ThemeResource lookup helpers; planned `Theme.Accent` token system |
| **{TemplateBinding}** | Blocked | No control templates in Reactor |
| **x:Name** | Replaced | `.OnMount(control => ...)` captures control references |
| **x:Key** | Blocked | No resource dictionaries in DSL |
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
| **ResourceDictionary** | Passthrough | ReactorApp loads XamlControlsResources; no DSL for custom dictionaries |
| **Resource lookup chain** | Passthrough | WinUI's chain works on underlying controls; Reactor doesn't intercept |
| **Merged dictionaries** | Passthrough | Can merge via `.Set()` on Application.Resources |
| **Theme dictionaries** | Passthrough | WinUI's theme dictionaries work natively |
| **XamlControlsResources** | Exposed | Automatically loaded by ReactorApplication.OnLaunched |
| **Forward reference restriction** | N/A | No XAML → no forward reference issue |

**Verdict: Passthrough.** Reactor delegates resource management entirely to WinUI. The
framework loads XamlControlsResources automatically so Fluent design resources are
available. Custom resource dictionaries can be merged programmatically via `.Set()` or
Application.Resources but there's no DSL for it.

---

## 8. Styling

| WinUI Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| **Implicit styles** | Passthrough | Work on underlying controls | WinUI's implicit styles apply to Reactor-created controls normally |
| **Explicit styles (x:Key)** | Exposed | `.ApplyStyle("AccentButtonStyle")` | Apply named WinUI styles to any element |
| **BasedOn inheritance** | Passthrough | Works in WinUI layer | Style inheritance works; Reactor doesn't interfere |
| **Lightweight styling** | Missing | — | No per-control theme resource key overrides in DSL; planned in theming design |
| **ControlTemplate** | Blocked | — | Reactor renders content directly; no template authoring |
| **DataTemplate** | Replaced | Lambda template functions | `ListView(items, item => HStack(Image(item.Icon), Text(item.Name)))` |
| **Templated controls (Generic.xaml)** | Blocked | — | Custom controls are Reactor components, not templated controls |

**Verdict: Mixed.** Basic style application works. DataTemplate is elegantly replaced by
lambda functions. However, ControlTemplate authoring is blocked — you cannot re-template
WinUI controls from Reactor. Lightweight styling (overriding theme resource keys per-control)
is not yet available but is planned per the theming design spec.

---

## 9. Theming

Theming is evaluated with a "could a normal developer use this correctly" bar. While
WinUI's theme system technically operates on the underlying controls, Reactor's design
decisions make it easy to silently break theming without realizing it.

| WinUI Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| **Light/Dark/HighContrast (unstyled controls)** | Passthrough | Works if dev uses zero Reactor style modifiers | A Button with no `.Background()` / `.Foreground()` responds to theme changes correctly. The moment a dev sets any color via Reactor modifiers, they silently opt out of theme reactivity with no warning |
| **Light/Dark/HighContrast (styled controls)** | Blocked | — | Any use of `.Background()`, `.Foreground()`, or other color modifiers does a local-value set (highest DP precedence), permanently overriding the theme resource. A normal developer would not discover this until testing dark mode |
| **Application.RequestedTheme** | Passthrough | Set via ReactorApp or `.Set()` | App-level theme selection works for unstyled controls |
| **Per-element RequestedTheme** | Missing | Via `.Set(fe => fe.RequestedTheme = Dark)` | No first-class modifier; requires knowing the WinUI escape hatch |
| **ThemeResource lookup** | Exposed | ThemeResource helper class | Read-only, one-shot resolve; does NOT re-resolve on theme change — a trap for developers who assume it's reactive |
| **Theme-reactive values** | Blocked | Not implemented | The `Theme.Accent` token system from the theming design spec is not yet built. There is currently no way to set a color in Reactor that reacts to theme changes. Developers must either leave properties unset (Tier 3) or accept broken theming |
| **Accent colors** | Blocked | — | No way to reference `SystemAccentColor` or its shades in a theme-reactive manner from Reactor DSL |
| **High contrast** | Blocked | — | Unstyled controls work, but any Reactor-styled control will keep its hard-coded colors in high-contrast mode, violating accessibility requirements. A normal developer has no path to high-contrast correctness for custom-styled elements |

**Verdict: Blocked for any real-world app that uses Reactor's styling API.** The core problem:
Reactor's styling modifiers (`.Background()`, `.Foreground()`, etc.) do local-value property
sets, which sit at the top of WinUI's dependency property precedence chain. This permanently
overrides theme resources, styles, and high-contrast dictionaries on that control.

A developer building a styled app today faces an impossible choice:
- **Don't use Reactor modifiers** → theming works but the app can't have custom styling
- **Use Reactor modifiers** → custom styling works but dark mode and high contrast break silently

The theming design spec (see `001-theming-design.md`) outlines the solution: a three-tier
value model with `ThemeRef` tokens that resolve from WinUI resources and re-resolve on theme
change. Until this is implemented, theming is effectively broken for any control that uses
Reactor's color/brush modifiers.

**This is a P0 architectural gap.**

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
    .Background(isHovered ? "#0078D4" : "#CCCCCC")
    .OpacityTransition()
    .Set(r => {
        r.PointerEntered += (_, _) => setHovered(true);
        r.PointerExited += (_, _) => setHovered(false);
    });
```

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
| **Connected Animations** | Missing | — | No wrapper; would need cross-component coordination |
| **Composition Animations** | Passthrough | Via `.Set()` and ElementCompositionPreview | Access compositor directly |

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
| Connected animations | Missing | ConnectedAnimationService not exposed; needs cross-element coordination |
| Spring animations | Passthrough | Use composition spring APIs via `.Set()` |
| Expression animations | Passthrough | Use compositor.CreateExpressionAnimation via `.Set()` |
| Collection transitions | Passthrough | ItemCollectionTransitionProvider via `.Set()` |

**Verdict: Theme transitions and implicit transitions are well-exposed. Storyboarded and
composition animations are passthrough. Connected animations are the main gap.**

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
| **MicaBackdrop** | Passthrough | Set via `Window.SystemBackdrop` in `.Set()` or ReactorApp setup |
| **DesktopAcrylicBackdrop** | Passthrough | Same as above |
| **MicaController / AcrylicController** | Passthrough | Advanced configuration via raw API |
| **Composition Effects** | Passthrough | Win2D effects via compositor |
| **AcrylicBrush** | Passthrough | Usable as brush value |
| **RadialGradientBrush** | Passthrough | Usable as brush value |
| **XamlCompositionBrushBase** | Passthrough | Custom brush creation via raw API |
| **Lighting** | Passthrough | XamlLight subclasses via raw API |

**Verdict: Passthrough.** Materials and effects are visual-layer features that work on any
UIElement. Reactor doesn't need to wrap them — they can be applied to any Reactor-rendered control
via `.Set()`. A convenience modifier for system backdrops could be added.

---

## 14. Input Handling

### 14.1 Control-Level Input (Semantic Events)

| Event Type | Status | Reactor Surface | Notes |
|---|---|---|---|
| Button.Click | Exposed | `OnClick` callback | All 7 button types |
| TextBox.TextChanged | Exposed | `OnTextChanged` callback | TextField, AutoSuggestBox |
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
| PointerPressed/Released/Moved | Passthrough | Wire via `.Set(el => el.PointerPressed += ...)` |
| PointerEntered/Exited | Passthrough | Same pattern |
| PointerCanceled/CaptureLost | Passthrough | Same pattern |

### 14.3 Gesture Events

| Event | Status | Notes |
|---|---|---|
| Tapped/DoubleTapped | Passthrough | Via `.Set()` |
| RightTapped | Passthrough | Via `.Set()` |
| Holding | Passthrough | Via `.Set()` |

### 14.4 Manipulation Events

| Feature | Status | Notes |
|---|---|---|
| ManipulationMode | Missing | No modifier; set via `.Set()` |
| ManipulationStarted/Delta/Completed | Passthrough | Via `.Set()` |
| ManipulationInertiaStarting | Passthrough | Via `.Set()` |

### 14.5 Keyboard Input

| Feature | Status | Notes |
|---|---|---|
| KeyDown/KeyUp | Passthrough | Via `.Set()` |
| PreviewKeyDown/PreviewKeyUp | Passthrough | Via `.Set()` |
| CharacterReceived | Passthrough | Via `.Set()` |
| InputKeyboardSource.GetKeyStateForCurrentThread | Passthrough | Direct API call |
| AddHandler (handledEventsToo) | Passthrough | Via `.Set()` |

### 14.6 Keyboard Accelerators

| Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| KeyboardAccelerator | Exposed | `Accelerator(key, modifiers)` data + element property | KeyboardAcceleratorData record; applied by reconciler |
| ScopeOwner | Passthrough | Via `.Set()` | |
| Auto-tooltip | Passthrough | WinUI handles natively | |

### 14.7 Access Keys / Focus / Drag-Drop

| Feature | Status | Notes |
|---|---|---|
| AccessKey | Missing | No modifier; use `.Set(el => el.AccessKey = "A")` |
| IsAccessKeyScope | Missing | Via `.Set()` |
| FocusManager methods | Passthrough | Direct API calls |
| IsTabStop / TabIndex | Missing | No modifier; via `.Set()` |
| TabFocusNavigation | Missing | Via `.Set()` |
| XYFocus properties | Missing | Via `.Set()` |
| CanDrag / AllowDrop | Missing | No general modifier; TreeView has `CanDragItems` |
| DragStarting / DragOver / Drop | Passthrough | Via `.Set()` |

**Verdict: Control-level semantic events are well-exposed with clean callback APIs.
Low-level pointer, gesture, keyboard, and manipulation events are all passthrough via
`.Set()`. Keyboard accelerators are exposed. Access keys, focus management, tab navigation,
XY focus, and drag-drop are all missing as first-class modifiers.**

---

## 15. Commands

| WinUI Feature | Status | Notes |
|---|---|---|
| **ICommand** | Missing | Action callbacks only cover the "execute" part of a command. ICommand bundles execute + can-execute query + change notification — none of which Reactor replicates. A command is a named, stateful, reusable action; a callback is just a closure |
| **XamlUICommand** | Missing | Label+Icon+Accelerator+Description bundling not replicated |
| **StandardUICommand** | Missing | No pre-built Cut/Copy/Paste/Undo command objects |
| **Command property on controls** | Missing | Controls only have OnClick/OnChanged callbacks, not a Command binding point |
| **CanExecute / auto-disable** | Missing | No automatic disable-when-unavailable; `.IsEnabled(!condition)` is manual and must be wired separately per control |

**Verdict: Missing.** Reactor has no command abstraction. Action callbacks cover the simplest
case (click → do thing) but do not replicate what ICommand provides: a named, queryable,
reusable action with enable/disable state that automatically propagates to all bound
controls. The XamlUICommand/StandardUICommand pattern that bundles label, icon, accelerator,
description, and CanExecute into a single reusable object has no Reactor equivalent. A
Reactor-native command model is needed.

---

## 16. Accessibility

| WinUI Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| **AutomationProperties.Name** | Exposed | `.AutomationName("label")` | First-class modifier |
| **AutomationProperties.LabeledBy** | Missing | Via `.Set()` | |
| **AutomationProperties.HelpText** | Missing | Via `.Set()` | |
| **AutomationProperties.LiveSetting** | Missing | Via `.Set()` | |
| **AutomationProperties.AutomationId** | Missing | Via `.Set()` | |
| **AutomationProperties.AccessibilityView** | Missing | Via `.Set()` | |
| **AutomationProperties.HeadingLevel** | Missing | Via `.Set()` | |
| **AutomationProperties.LandmarkType** | Missing | Via `.Set()` | |
| **AutomationProperties.IsRequiredForForm** | Missing | Via `.Set()` | |
| **Custom AutomationPeer** | Blocked | Cannot subclass OnCreateAutomationPeer for Reactor components |
| **Live Regions** | Missing | Via `.Set()` on underlying TextBlock |
| **UIA Tree Views** | Passthrough | WinUI's automation tree works on rendered controls |

**Verdict: Minimal.** Only `AutomationProperties.Name` has a first-class modifier. All other
accessibility properties require `.Set()`. Custom automation peers cannot be created for Reactor
components (they don't subclass Control). The underlying WinUI controls provide their built-in
accessibility, so basic screen reader support works, but fine-tuning is limited.

**Risk: Accessibility is the largest gap for production apps.** Additional modifiers for
HelpText, LiveSetting, HeadingLevel, LandmarkType, and AutomationId should be prioritized.

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
| **AppWindow** | Passthrough | Via `ReactorHost.Window.AppWindow` | |
| **Size and Position** | Exposed | `ReactorApp.Run(title, width:, height:)` | Initial size; runtime resize via AppWindow |
| **OverlappedPresenter** | Passthrough | Via AppWindow.SetPresenter | |
| **FullScreenPresenter** | Exposed | `ReactorApp.Run(..., fullScreen: true)` | |
| **CompactOverlayPresenter** | Passthrough | Via AppWindow.SetPresenter | |
| **Title bar customization** | Exposed | TitleBarElement | Custom content, left/right headers |
| **AppWindow color properties** | Passthrough | Via `.Set()` on TitleBar or AppWindow.TitleBar | |
| **SetDragRectangles** | Passthrough | Via AppWindow.TitleBar API | |
| **Multi-window** | Missing | Single window only | ReactorApp.Run creates one window; no multi-window API |
| **AppWindow events** | Passthrough | Via AppWindow.Changed, Closing, etc. | |

**Verdict: Basic windowing is covered. Multi-window is missing.** Single-window apps work
well. Multi-window would require creating additional Window instances and mounting separate
ReactorHost instances, which is not currently supported by the framework.

---

## 19. Application Lifecycle

| WinUI Feature | Status | Reactor Surface | Notes |
|---|---|---|---|
| **Application.OnLaunched** | Exposed | ReactorApplication.OnLaunched handles setup | Automatic; users don't override |
| **Activation kinds** | Missing | No activation dispatch | All activation goes through default launch path |
| **Single-instancing** | Missing | — | No AppInstance.FindOrRegisterForKey integration |
| **Suspension** | N/A | Desktop WinUI 3 doesn't have UWP suspension | |
| **UnhandledException** | Exposed | `ReactorApplication.OnUnhandledException` static callback | Set handler before Run() |

**Verdict: Basic lifecycle works. Advanced activation and instancing are missing.** For apps
that need file/protocol activation or single-instancing, manual setup in Program.Main is
required before calling ReactorApp.Run.

---

## 20. App Services

| WinUI Feature | Status | Notes |
|---|---|---|
| **Printing** | Passthrough | Use PrintManagerInterop directly |
| **Clipboard** | Passthrough | Use Windows.ApplicationModel.DataTransfer.Clipboard |
| **File Pickers** | Passthrough | Use FileOpenPicker/FileSavePicker with HWND from ReactorHost.Window |

**Verdict: All passthrough.** No Reactor wrappers for OS services. Apps use WinRT APIs directly,
getting the HWND from `ReactorHost.Window` for owner-window initialization. A Reactor.Services
namespace with convenience wrappers is a natural future addition.

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
| **Virtualization (ItemsRepeater)** | Passthrough | Used internally by LazyVStack/LazyHStack | |
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

| # | Feature Area | Exposed | Augmented | Replaced | Passthrough | Missing | Blocked |
|---|---|---|---|---|---|---|---|
| 1 | Built-in Controls | 71 | 5 | 1 | 4 | 5 | — |
| 2 | Layout System | 6 | 2 | 1 | 5 | — | — |
| 3 | Navigation | 4 | — | — | — | 1 | 3 |
| 4 | Data Binding | — | — | 11 | — | — | — |
| 5 | Dependency Properties | — | 1 | 2 | 2 | — | 1 |
| 6 | XAML Markup | — | — | 8 | — | — | 5 |
| 7 | Resources | 1 | — | — | 4 | — | — |
| 8 | Styling | 1 | — | 1 | 3 | 1 | 2 |
| 9 | Theming | — | — | — | 2 | 1 | 5 |
| 10 | Visual State Manager | — | — | 7 | — | — | — |
| 11 | Animations | 14 | — | — | 6 | 1 | — |
| 12 | Composition Layer | — | — | — | 6 | — | — |
| 13 | Materials/Effects | — | — | — | 8 | — | — |
| 14 | Input Handling | 10 | — | — | 12 | 8 | — |
| 15 | Commands | — | — | — | — | 5 | — |
| 16 | Accessibility | 1 | — | — | 1 | 9 | 1 |
| 17 | Threading | — | — | — | 5 | — | — |
| 18 | Windowing | 2 | — | — | 6 | 1 | — |
| 19 | App Lifecycle | 2 | — | — | — | 2 | — |
| 20 | App Services | — | — | — | 3 | — | — |
| 21 | Interop | 1 | — | — | 3 | — | — |
| 22 | Items Infrastructure | 2 | — | 3 | 3 | — | 1 |

### Top Gaps to Address (Priority Order)

| Priority | Gap | Impact | Effort |
|---|---|---|---|
| **P0** | Theming: ThemeRef token system + theme-reactive modifiers | Any Reactor-styled control breaks dark mode and high contrast silently; blocks all real-world apps | High — requires ThemeRef type, modifier overloads, reconciler changes, theme change detection (see `001-theming-design.md`) |
| **P0** | Accessibility modifiers (HelpText, LiveSetting, HeadingLevel, AutomationId, LandmarkType) | Blocks production apps; compliance requirement | Low — add modifiers to ElementExtensions |
| **P0** | Navigation: Reactor-native router with back stack, transitions, parameter passing | Core navigation scenario is broken; Frame/Page requires XAML | High — design and implement navigation system |
| **P0** | Command model (ICommand equivalent with name, query-status, action) | No reusable command abstraction; blocks toolbar/menu-driven apps | Medium — design command data model + control integration |
| **P1** | Lightweight styling (per-control theme resource key overrides) | Blocks custom-branded controls | Medium — per theming design spec |
| **P1** | Focus management modifiers (IsTabStop, TabIndex, TabFocusNavigation) | Poor keyboard navigation | Low — add modifiers |
| **P1** | Access key modifiers | Missing Alt-key mnemonics | Low — add modifier |
| **P1** | Drag-and-drop modifiers (CanDrag, AllowDrop) | Common interaction pattern | Low — add modifiers |
| **P1** | Multi-window support | Blocks MDI/tool-window apps | Medium — ReactorHost per window |
| **P1** | ToolTip: expose full feature set (placement, timing, rich content) | Platform capability hidden behind minimal modifier | Low — richer modifier or element |
| **P2** | Connected animations | Navigation feels static | Medium — cross-component coordination needed |
| **P2** | ManipulationMode modifier | Custom gesture handling awkward | Low — add modifier |
| **P2** | Per-element RequestedTheme modifier | Subtree theme override is verbose | Low — add modifier |
| **P3** | InkCanvas / InkToolbar elements | Blocks inking apps | Low — add element + mount handler |
| **P3** | Activation kinds (file, protocol, etc.) | Blocks registered-handler apps | Medium — ReactorApp.Run overload |
| **P3** | App service wrappers (clipboard, file pickers, printing) | Convenience; not blocking | Medium — new Reactor.Services namespace |

### Architectural Trade-offs

| WinUI Strength Lost | Reactor Strength Gained |
|---|---|
| Visual designer / XAML hot reload | Full IntelliSense, refactoring, type safety |
| ControlTemplate re-templating | Simpler composition model; no template part contracts |
| Declarative VisualStateManager | Any C# logic can drive visual state |
| Compiled {x:Bind} with zero-overhead | No binding errors, no DataContext confusion |
| XAML resource forward-reference chain | Standard C# scoping rules |
| Automatic back-stack navigation | (missing — no equivalent yet) |
| DP value precedence system | (blocked — local-value-only sets break theme/style integration) |
| Theme-reactive styled controls | (blocked — any color modifier silently opts out of theming) |
| ICommand with query-status | (missing — callbacks don't replicate command model) |
| x:Phase incremental rendering | (no equivalent; reconciler batching partially compensates) |
