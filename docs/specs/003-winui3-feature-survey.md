# WinUI 3 Application Programming Feature Survey

A comprehensive inventory of WinUI 3 / Windows App SDK features relevant to application
development, organized for gap analysis against the Microsoft.UI.Reactor (Reactor) framework.

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

---

## 1. Built-in Controls

### 1.1 Basic Input

| Control | Purpose |
|---|---|
| **Button** | Standard push button, raises Click |
| **DropDownButton** | Button with chevron opening a flyout |
| **SplitButton** | Two-part: immediate action + flyout |
| **ToggleSplitButton** | Two-part with toggle side |
| **HyperlinkButton** | Link-styled navigation button |
| **RepeatButton** | Raises Click repeatedly while held |
| **ToggleButton** | Two-state toggle base class |
| **CheckBox** | Tri-state selection (checked/unchecked/indeterminate) |
| **RadioButton** | Mutually exclusive selection |
| **RadioButtons** | Grouped radio button container with keyboard nav |
| **ToggleSwitch** | On/off binary switch |
| **Slider** | Value selection from continuous range |
| **ComboBox** | Drop-down single selection |
| **ListBox** | Inline single/multi selection |
| **ColorPicker** | Color selection with spectrum, sliders, text input |
| **RatingControl** | Star-based rating input |
| **NumberBox** | Numeric input with validation, stepping, inline math |

Docs: [Buttons](https://learn.microsoft.com/en-us/windows/apps/design/controls/buttons) |
[CheckBox](https://learn.microsoft.com/en-us/windows/apps/design/controls/checkbox) |
[ComboBox](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/combo-box) |
[ColorPicker](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.colorpicker) |
[NumberBox](https://learn.microsoft.com/en-us/windows/winui/api/microsoft.ui.xaml.controls.numberbox)

### 1.2 Text Controls

| Control | Purpose |
|---|---|
| **TextBlock** | Read-only text with inline formatting (bold, italic, underline, hyperlinks) |
| **RichTextBlock** | Advanced read-only text with inline UI elements, multi-column overflow |
| **TextBox** | Single/multi-line plain text input |
| **RichEditBox** | Rich text editing with formatting (RTF) |
| **PasswordBox** | Masked secret input |
| **AutoSuggestBox** | Text input with suggestion dropdown |

Docs: [Text controls overview](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/text-controls) |
[TextBox](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/text-box) |
[RichEditBox](https://learn.microsoft.com/en-us/windows/apps/design/controls/rich-edit-box)

### 1.3 Icons

| Control | Purpose |
|---|---|
| **FontIcon** | Glyph from a font (default: Segoe Fluent Icons) |
| **SymbolIcon** | Predefined icon from Symbol enum |
| **ImageIcon** | Image (PNG, SVG) as icon |
| **AnimatedIcon** | Lottie-animated icon responding to state changes |
| **BitmapIcon** | Bitmap as monochrome icon |
| **PathIcon** | Vector path as icon |

Docs: [Icons](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/icons) |
[AnimatedIcon](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/animated-icon)

### 1.4 Collections and Lists

| Control | Purpose |
|---|---|
| **ListView** | Vertical list with selection, grouping, virtualization |
| **GridView** | Wrapping grid with virtualization |
| **ItemsView** | Modern collection control (replaces ListView/GridView for new code) |
| **ItemsRepeater** | Low-level data-driven repeater with pluggable layout and virtualization |
| **FlipView** | One-at-a-time with flip/swipe navigation |
| **TreeView** | Hierarchical expandable/collapsible nodes |

Docs: [Collections and lists](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/lists) |
[ListView & GridView](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/listview-and-gridview) |
[ItemsView](https://learn.microsoft.com/en-us/windows/apps/design/controls/itemsview) |
[ItemsRepeater](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/items-repeater)

### 1.5 Date and Time

| Control | Purpose |
|---|---|
| **CalendarView** | Always-visible calendar for date/range selection |
| **CalendarDatePicker** | Drop-down opening a CalendarView |
| **DatePicker** | Spinner-style date selector (month/day/year) |
| **TimePicker** | Spinner-style time selector (hour/minute/AM-PM) |

Docs: [Date and time](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/date-and-time)

### 1.6 Dialogs, Flyouts, and Popups

| Control | Purpose |
|---|---|
| **ContentDialog** | Modal dialog with up to 3 buttons |
| **Flyout** | Light-dismiss popup attached to an element |
| **MenuFlyout** | Flyout with menu items, sub-items, toggles, radio items |
| **CommandBarFlyout** | Rich flyout with primary/secondary commands |
| **TeachingTip** | Onboarding/feature-discovery notification |
| **ToolTip** | Supplementary info on hover/focus |
| **Popup** | General-purpose overlay |

Docs: [Dialogs and flyouts](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/dialogs-and-flyouts/) |
[TeachingTip](https://learn.microsoft.com/en-us/windows/apps/design/controls/dialogs-and-flyouts/teaching-tip)

### 1.7 Menus, Toolbars, and Commands

| Control | Purpose |
|---|---|
| **MenuBar** | Horizontal row of top-level menus (File, Edit, View...) |
| **MenuBarItem** | Single top-level menu in a MenuBar |
| **CommandBar** | Toolbar with primary and overflow commands |
| **AppBarButton** | Icon+label button for CommandBar |
| **AppBarToggleButton** | Toggle button for CommandBar |
| **AppBarSeparator** | Visual separator in CommandBar |

Docs: [MenuBar](https://learn.microsoft.com/en-us/windows/apps/design/controls/menus) |
[CommandBar](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/command-bar)

### 1.8 Navigation Controls

| Control | Purpose |
|---|---|
| **NavigationView** | App-level nav with hamburger menu; left-nav, top-nav, compact modes |
| **TabView** | Browser-style tabs with reorder, add, close |
| **BreadcrumbBar** | Path-based navigation with overflow ellipsis |
| **SelectorBar** | Tab-like view switcher within a page |
| **Pivot** | Deprecated tabbed navigation |
| **Frame** | Hosts Pages, manages back-stack |
| **Page** | Content container for Frame navigation |
| **PipsPager** | Dot-based page indicator |

Docs: [NavigationView](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/navigationview) |
[TabView](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.tabview) |
[BreadcrumbBar](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/breadcrumbbar) |
[Page navigation](https://learn.microsoft.com/en-us/windows/apps/design/basics/navigate-between-two-pages)

### 1.9 Media and Graphics

| Control | Purpose |
|---|---|
| **Image** | Displays raster/vector images (PNG, JPG, SVG, GIF) |
| **MediaPlayerElement** | Audio/video playback with transport controls |
| **InkCanvas** | Freehand pen/touch inking surface |
| **InkToolbar** | Toolbar for InkCanvas (pen, pencil, highlighter, eraser, ruler) |
| **WebView2** | Embedded Chromium browser (Edge) |
| **PersonPicture** | Avatar image or initials |
| **ParallaxView** | Parallax scrolling tied to ScrollViewer offset |
| **CaptureElement** | Camera preview stream |
| **MapControl** | Map visualization (with MapElement, MapIcon, MapLayer) |
| **AnimatedVisualPlayer** | Lottie animation playback |

Docs: [MediaPlayerElement](https://learn.microsoft.com/en-us/windows/apps/design/controls/media-playback) |
[WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/get-started/winui)

### 1.10 Status and Information

| Control | Purpose |
|---|---|
| **ProgressBar** | Determinate/indeterminate horizontal progress |
| **ProgressRing** | Circular progress indicator |
| **InfoBar** | Inline non-blocking notification (success/warning/error/info) |
| **InfoBadge** | Small badge overlay (dot, number, or icon) |
| **Expander** | Collapsible header/content |

Docs: [Progress controls](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/progress-controls) |
[InfoBar](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/infobar) |
[InfoBadge](https://learn.microsoft.com/en-us/windows/apps/design/controls/info-badge)

### 1.11 Scrolling

| Control | Purpose |
|---|---|
| **ScrollViewer** | Classic scroll container with pan and zoom |
| **ScrollView** | Modern scroll control based on InteractionTracker |
| **AnnotatedScrollBar** | Scrollbar with labeled section annotations |
| **ScrollBar** | Standalone scrollbar primitive |

Docs: [Scroll controls](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/scroll-controls)

### 1.12 Layout and Container Controls

| Control | Purpose |
|---|---|
| **Border** | Border/background around a single child |
| **Viewbox** | Scales single child to fill available space |
| **SplitView** | Collapsible pane + content region |
| **TwoPaneView** | Adaptive dual-pane (side-by-side or top-bottom) |
| **SwipeControl** | Swipe gestures revealing contextual commands |
| **RefreshContainer** | Pull-to-refresh interaction wrapper |

Docs: [SplitView](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/split-view) |
[TwoPaneView](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/two-pane-view)

### 1.13 Title Bar

| Control | Purpose |
|---|---|
| **TitleBar** | Custom window title bar control |

Source: `microsoft-ui-xaml-lift/controls/dev/TitleBar/`

---

## 2. Layout System

### 2.1 Panel Types

| Panel | Behavior |
|---|---|
| **Grid** | Row/column grid with star, auto, and pixel sizing |
| **StackPanel** | Stacks children vertically (default) or horizontally; supports Spacing |
| **Canvas** | Absolute positioning via Canvas.Left/Top attached properties |
| **RelativePanel** | Children positioned relative to each other or panel edges |
| **VariableSizedWrapGrid** | Wrapping grid where items can span multiple rows/columns |

Docs: [Layout panels](https://learn.microsoft.com/en-us/windows/apps/design/layout/layout-panels)

### 2.2 Measure/Arrange Two-Pass System

The XAML layout engine uses a recursive two-pass algorithm:

1. **Measure pass** -- `UIElement.Measure(availableSize)` called on each element. Each element
   computes how much space it needs and sets its `DesiredSize`. Panels call Measure on each child
   from their `MeasureOverride`.

2. **Arrange pass** -- `UIElement.Arrange(finalRect)` assigns each element its actual position and
   size. Panels call Arrange on each child from their `ArrangeOverride`.

Key rules:
- Property changes affecting size trigger `InvalidateMeasure` (new Measure+Arrange cycle)
- Property changes affecting only position trigger `InvalidateArrange`
- Custom panels subclass `Panel` and override `MeasureOverride`/`ArrangeOverride`

Docs: [Custom panels](https://learn.microsoft.com/en-us/windows/apps/design/layout/custom-panels-overview)

### 2.3 Attached Layouts (for ItemsRepeater)

The `Layout` abstract class provides "attached layouts" used by ItemsRepeater with
`NonVirtualizingLayout` and `VirtualizingLayout` subclasses.

Built-in attached layouts:
- **StackLayout** -- linear stack
- **UniformGridLayout** -- uniform grid with item justification
- **FlowLayout** -- flowing wrapping layout
- **LinedFlowLayout** -- advanced flowing layout with line management and aspect-ratio sizing

Custom virtualizing layouts subclass `VirtualizingLayout` and implement `MeasureOverride`/
`ArrangeOverride` with a `VirtualizingLayoutContext`.

Docs: [Attached layouts](https://learn.microsoft.com/en-us/windows/apps/design/layout/attached-layouts)

### 2.4 Adaptive/Responsive Layout

| Feature | Description |
|---|---|
| **AdaptiveTrigger** | Fires on MinWindowWidth/MinWindowHeight thresholds in VisualStateManager |
| **Custom StateTriggers** | Extend `StateTriggerBase` for any condition (device, orientation, etc.) |
| **NavigationView auto-mode** | CompactModeThresholdWidth/ExpandedModeThresholdWidth auto-switch |
| **RelativePanel** | Rearrange children via visual states without changing panel |
| **TwoPaneView** | Automatic side-by-side / top-bottom based on space and dual-screen hinge |

Docs: [Responsive layouts](https://learn.microsoft.com/en-us/windows/apps/design/layout/layouts-with-xaml)

---

## 3. Navigation Patterns

Primary pattern: `NavigationView` as root with a `Frame` as Content. Each destination is a `Page`
loaded into the Frame.

| Component | Role |
|---|---|
| **NavigationView** | Nav menu (left/top/compact), header, back button, settings, content region |
| **Frame** | Hosts Pages, maintains back-stack. `Navigate(typeof(Page))`, `GoBack()`, `GoForward()` |
| **Page** | ContentControl subclass for navigation content |
| **TabView** | Document/multi-tab scenarios with drag-reorder, add, close |
| **BreadcrumbBar** | Hierarchical path with overflow ellipsis |
| **SelectorBar** | Horizontal labeled items for in-page view switching |

NavigationView features (from source):
- **NavigationViewDisplayMode**: Minimal, Compact, Expanded
- **NavigationViewPaneDisplayMode**: Auto, Left, Top, LeftCompact, LeftMinimal
- **NavigationViewBackButtonVisible**: back button visibility
- **NavigationViewSelectionFollowsFocus**: keyboard/focus navigation modes
- **NavigationViewShoulderNavigationEnabled**: gamepad shoulder button support

Docs: [NavigationView](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/navigationview) |
[Navigate between pages](https://learn.microsoft.com/en-us/windows/apps/design/basics/navigate-between-two-pages)

---

## 4. Data Binding

### 4.1 {x:Bind} vs {Binding}

| Aspect | {x:Bind} (compiled) | {Binding} (runtime) |
|---|---|---|
| Default mode | **OneTime** | **OneWay** |
| Resolution | Compile-time against code-behind | Runtime via DataContext |
| Source | Page/UserControl itself | DataContext (inherited) |
| Type safety | Compile-time validated | Runtime; can bind to Object |
| DataTemplate | Requires `x:DataType` | Uses inherited DataContext |
| Event binding | Supported | Not supported |
| Function binding | Supported (leaf step can be a function) | Not supported |
| Programmatic creation | Not possible | Supported via `Binding` class |
| Target requirement | Any property | Must be a DependencyProperty |

### 4.2 Binding Modes

- **OneTime** -- reads source once at initialization
- **OneWay** -- source-to-target, updates on change notification
- **TwoWay** -- bidirectional; `UpdateSourceTrigger` controls when (default `PropertyChanged`,
  `LostFocus` for TextBox.Text)

`x:DefaultBindMode` attribute changes the default for `{x:Bind}` on an element and descendants.

### 4.3 Change Notification Interfaces

- **INotifyPropertyChanged** -- fires `PropertyChanged` event for single-property updates
- **INotifyCollectionChanged** -- fires `CollectionChanged` for Add/Remove/Replace/Move/Reset
- **ObservableCollection\<T\>** -- implements both; does NOT monitor property changes on items

### 4.4 IValueConverter

`Convert` (source-to-target) and `ConvertBack` (target-to-source). Declared as XAML resource.
Built-in: `bool` to `Visibility` is automatic (SDK 14393+).

Additional properties: `FallbackValue`, `TargetNullValue`, `ConverterParameter`, `ConverterLanguage`.

### 4.5 Function Bindings ({x:Bind} only)

```xaml
<TextBlock Text="{x:Bind local:MyHelpers.Half(BigTextBlock.FontSize)}" />
```

- Functions as leaf step of binding path
- Two-way via `BindBack` property
- Change detection: re-evaluates when any parameter source fires PropertyChanged
- Arguments: binding paths, quoted strings, numbers, `x:True`/`x:False`
- System functions: `sys:String.Format(...)`, `sys:DateTime.Parse(...)`

### 4.6 Generated Bindings Infrastructure

Pages/UserControls with `{x:Bind}` get a `Bindings` property:
- `Update()` -- re-evaluates all compiled bindings
- `Initialize()` -- calls Update if not initialized
- `StopTracking()` -- unhooks all OneWay/TwoWay listeners

Docs: [Data binding in depth](https://learn.microsoft.com/en-us/windows/apps/develop/data-binding/data-binding-in-depth) |
[{x:Bind}](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/x-bind-markup-extension) |
[Function bindings](https://learn.microsoft.com/en-us/windows/apps/develop/data-binding/function-bindings) |
[Data binding and MVVM](https://learn.microsoft.com/en-us/windows/apps/develop/data-binding/data-binding-and-mvvm)

---

## 5. Dependency Property System

### 5.1 Core Concepts

`DependencyObject` provides the global internal property store. `DependencyProperty` is the
identifier (static token). Properties stored in centralized store, not as class fields.

Features enabled: data binding, styles, storyboarded animations, property-changed callbacks,
metadata-based defaults, `ClearValue`.

### 5.2 Registration

```csharp
public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
    nameof(Label), typeof(string), typeof(MyControl),
    new PropertyMetadata(null, new PropertyChangedCallback(OnLabelChanged)));
```

**Critical**: Property wrappers must only call `GetValue`/`SetValue`. XAML parser bypasses wrappers.

### 5.3 PropertyMetadata

- Default value (object) and/or `PropertyChangedCallback`
- `PropertyMetadata.Create` with `CreateDefaultValueCallback` for thread-safe reference-type defaults
- Never pass instantiated reference-type as default (shared across instances)

### 5.4 PropertyChangedCallback

- Must be **static**
- Receives `DependencyObject d` and `DependencyPropertyChangedEventArgs e`
- Can reject values by setting back to OldValue

### 5.5 Instance-level Change Notifications

`RegisterPropertyChangedCallback` registers for changes on any DP of a specific instance (even
built-in properties).

### 5.6 Value Precedence (highest to lowest)

1. Active animations (including HoldEnd)
2. Local value (SetValue, XAML, bindings, resource references)
3. Templated properties (from ControlTemplate/DataTemplate)
4. Style setters
5. Default value (from PropertyMetadata)

### 5.7 Attached Properties

`DependencyProperty.RegisterAttached` with static `Get`/`Set` accessors. Owning class does NOT
need to derive from `DependencyObject`.

### 5.8 Differences from WPF

- No built-in `CoerceValueCallback` (implement manually in PropertyChangedCallback)
- No read-only dependency properties (`DependencyPropertyKey`)
- All DependencyObject access requires UI thread

Docs: [Dependency properties](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/dependency-properties-overview) |
[Custom dependency properties](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/custom-dependency-properties) |
[Attached properties](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/custom-attached-properties)

---

## 6. XAML Markup Features

### 6.1 Markup Extensions

| Extension | Purpose |
|---|---|
| `{x:Bind}` | Compiled data binding |
| `{Binding}` | Runtime data binding |
| `{StaticResource}` | One-time keyed resource lookup |
| `{ThemeResource}` | Theme-aware resource lookup (re-evaluates on theme change) |
| `{TemplateBinding}` | Bind within ControlTemplate to templated parent property |
| `{x:Null}` | Null value |
| `{CustomResource}` | For CustomXamlResourceLoader |

Custom markup extensions: derive from `MarkupExtension` and override `ProvideValue`.

### 6.2 XAML Attributes

| Attribute | Purpose |
|---|---|
| `x:Name` | Generates code-behind field; used in {x:Bind} paths |
| `x:Key` | Resource dictionary key |
| `x:Class` | Code-behind partial class for page/control |
| `x:DataType` | Required on DataTemplate for {x:Bind} |
| `x:DefaultBindMode` | Default binding mode (OneTime/OneWay/TwoWay) for subtree |

### 6.3 Deferred Loading

- **x:DeferLoadStrategy="Lazy"** -- element not created until realized
- **x:Load** (supersedes above) -- supports unloading too (`x:Load="False"`)
- Realize via: `FindName()`, `GetTemplateChild()`, VisualState targeting, binding targeting
- ~600 bytes overhead per deferred element

### 6.4 Conditional XAML

API contract checks in namespace declarations for conditional element inclusion:

```xaml
xmlns:contract7="...?IsApiContractPresent(Windows.Foundation.UniversalApiContract,7)"
```

### 6.5 Casting in {x:Bind}

```xaml
{x:Bind ((TextBox)obj).Text}
```

Docs: [x:DeferLoadStrategy](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/x-deferloadstrategy-attribute) |
[Conditional XAML](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/conditional-xaml)

---

## 7. Resources and Resource Management

### 7.1 ResourceDictionary

Keyed collection of shareable objects. Every `FrameworkElement` has a `Resources` property. Common
types: Style, ControlTemplate, DataTemplate, Brush, Color, Thickness, CornerRadius, converters.

**Not shareable**: UIElement subclasses (can only exist once in visual tree).

### 7.2 Resource Lookup Chain

1. `FrameworkElement.Resources` of the element itself
2. Walk up visual tree checking each parent's Resources
3. `Application.Resources` (app-level)
4. Theme dictionaries (from control templates)
5. Platform/system resources (generic.xaml, themeresources.xaml)

Code-based `Resources["key"]` does NOT walk up -- only searches that specific dictionary.

### 7.3 Merged Dictionaries

```xaml
<ResourceDictionary.MergedDictionaries>
    <ResourceDictionary Source="Dictionary1.xaml"/>
    <ResourceDictionary Source="Dictionary2.xaml"/>
</ResourceDictionary.MergedDictionaries>
```

Lookup: primary first, then merged in **reverse** order (last-added wins).

### 7.4 Theme Dictionaries

Keys: `"Default"`, `"Dark"`, `"Light"`, `"HighContrast"`. Active dictionary switches on theme
change. Only effective via `{ThemeResource}`.

### 7.5 XamlControlsResources

WinUI controls library dictionary. Add to `Application.Resources` **first** so custom styles
override it.

### 7.6 Forward References

Resources must be defined **lexically before** references. No forward references supported.

Docs: [ResourceDictionary and XAML resources](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-resource-dictionary)

---

## 8. Styling

### 8.1 Style Basics

A `Style` has a `TargetType` and `Setter` elements. Two kinds:
- **Implicit** (no x:Key) -- auto-applied to all matching controls in scope
- **Explicit** (has x:Key) -- must be explicitly referenced

### 8.2 BasedOn Inheritance

```xaml
<Style x:Key="DerivedStyle" TargetType="Button" BasedOn="{StaticResource BasicStyle}">
```

Derived TargetType must be same type or subclass.

### 8.3 Lightweight Styling

Override specific theme resource keys per-control without re-templating. Each control exposes named
brush resources with state suffixes:

```xaml
<CheckBox.Resources>
    <SolidColorBrush x:Key="CheckBoxForegroundUnchecked" Color="Purple"/>
</CheckBox.Resources>
```

State suffix pattern: `{ControlName}{Property}{State}` (e.g., `ButtonBackgroundPointerOver`).

### 8.4 ControlTemplate

Defines the complete visual tree for a control. Includes VisualStateManager groups, template parts
(named elements via x:Name), and ContentPresenter.

### 8.5 DataTemplate

Visual structure for data items in ListView, GridView, ContentPresenter. With `x:DataType` for
compiled bindings.

### 8.6 Templated Controls

Custom templated controls require a `Generic.xaml` file in a `Themes` folder (required naming
convention).

Docs: [XAML styles](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-styles) |
[Templated controls](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/xaml-templated-controls-csharp-winui-3)

---

## 9. Theming

### 9.1 Built-in Themes

Three themes: **Light**, **Dark**, **HighContrast**.

- Set `Application.RequestedTheme` in App.xaml (Light/Dark/omit for system)
- Per-element override: `FrameworkElement.RequestedTheme = ElementTheme.Light`
- High contrast overrides `RequestedTheme` when active

### 9.2 ThemeResource vs StaticResource

- `{ThemeResource}` -- re-evaluated on theme change
- `{StaticResource}` -- evaluated once at load time

Inside ThemeDictionaries, use `{StaticResource}` except for `SystemColor*` in HighContrast
(those need `{ThemeResource}`).

### 9.3 Accent Colors

System accent: `SystemAccentColor` with light/dark shades (`SystemAccentColorLight1` through
`Light3`, `Dark1` through `Dark3`).

Override app-wide:
```xaml
<Color x:Key="SystemAccentColor">#107C10</Color>
```

### 9.4 High Contrast

Uses `SystemColor*` prefixed resources (e.g., `SystemColorWindowTextColor`). Custom controls MUST
provide a HighContrast theme dictionary and use these system resources.

Detection (SDK 1.6+):
```csharp
var settings = new Microsoft.UI.System.ThemeSettings();
bool isHighContrast = settings.HighContrast;
```

Docs: [Theming](https://learn.microsoft.com/en-us/windows/apps/develop/ui/theming) |
[XAML theme resources](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-theme-resources) |
[Contrast themes](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/high-contrast-themes) |
[Color](https://learn.microsoft.com/en-gb/windows/apps/design/signature-experiences/color)

---

## 10. Visual State Manager

### 10.1 Architecture

- **VisualStateManager** -- manages transitions between visual states
- **VisualStateGroup** -- contains mutually exclusive VisualState objects
- **VisualState** -- one discrete visual configuration (Setters and/or Storyboard)

A control can have multiple groups (e.g., "CommonStates", "FocusStates", "CheckStates").

### 10.2 Setters vs Storyboard

- **Setters** -- discrete instant property changes
- **Storyboard** -- animated property changes over time
- Both can coexist in the same VisualState

### 10.3 Programmatic State Changes

```csharp
VisualStateManager.GoToState(myControl, "PointerOver", useTransitions: true);
```

### 10.4 StateTriggers (Declarative)

| Trigger | Purpose |
|---|---|
| **AdaptiveTrigger** | Fires on MinWindowWidth/MinWindowHeight |
| **StateTrigger** | Simple boolean `IsActive` binding |
| **Custom triggers** | Extend `StateTriggerBase`, call `SetActive(bool)` |

### 10.5 VisualTransition

Animated transitions between states within a group:
```xaml
<VisualTransition From="Normal" To="PointerOver" GeneratedDuration="0:0:0.25"/>
```

Docs: [VisualState](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.visualstate) |
[AdaptiveTrigger](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.adaptivetrigger)

---

## 11. Animations and Transitions

### 11.1 Animation Layers (High to Low Level)

1. **Theme Transitions** -- automatic, declarative via `Transitions` property
2. **Theme Animations** -- pre-configured, inside Storyboards
3. **Storyboarded Animations** -- custom From/To/By or keyframe
4. **Connected Animations** -- cross-view continuity
5. **Composition Animations** -- visual-layer (KeyFrame, Expression, Spring, Implicit)

### 11.2 Theme Transitions

Applied via `UIElement.Transitions` or `ItemsControl.ItemContainerTransitions`:

| Transition | Purpose |
|---|---|
| `EntranceThemeTransition` | Element first appearing |
| `ContentThemeTransition` | Content changing in container |
| `RepositionThemeTransition` | Element changing position |
| `AddDeleteThemeTransition` | Items added/removed from list |
| `ReorderThemeTransition` | Items reordered |
| `PopupThemeTransition` | Popup appearing |
| `EdgeUIThemeTransition` | Small edge UI sliding in |
| `PaneThemeTransition` | Large pane sliding in |
| `NavigationThemeTransition` | Page navigation in Frame |

### 11.3 Theme Animations (Storyboard-based)

| Animation | Purpose |
|---|---|
| `FadeInThemeAnimation` / `FadeOutThemeAnimation` | Opacity |
| `PopInThemeAnimation` / `PopOutThemeAnimation` | Popup scale + opacity |
| `PointerDownThemeAnimation` / `PointerUpThemeAnimation` | Press feedback |
| `RepositionThemeAnimation` | Reposition |
| `DragItemThemeAnimation` / `DragOverThemeAnimation` | Drag and drop |
| `SplitOpenThemeAnimation` / `SplitCloseThemeAnimation` | ComboBox open/close |
| `DrillInThemeAnimation` / `DrillOutThemeAnimation` | Navigation drill |

### 11.4 Storyboarded Animations (Custom)

Animate dependency properties: `Double`, `Point`, `Color`, `Object` (discrete).

```xaml
<Storyboard>
    <DoubleAnimation Storyboard.TargetName="MyRect" Storyboard.TargetProperty="Opacity"
                     From="1.0" To="0.0" Duration="0:0:1"/>
</Storyboard>
```

Key properties: `From`, `To`, `By`, `Duration`, `AutoReverse`, `RepeatBehavior`, `BeginTime`,
`FillBehavior`, `SpeedRatio`.

**Independent vs Dependent**: Animations on `Opacity`, `RenderTransform`, `Projection`, `Clip`,
`Canvas.Left/Top`, `SolidColorBrush.Color` run on composition thread (independent). All others are
dependent, require `EnableDependentAnimation = true`, and block the UI thread.

### 11.5 KeyFrame Animations

Easing functions: `EasingDoubleKeyFrame`, `SplineDoubleKeyFrame`, built-in easing types
(CubicEase, QuadraticEase, BounceEase, etc.).

`ObjectAnimationUsingKeyFrames` for discrete (non-interpolatable) values like Visibility.

### 11.6 Connected Animations

Animate an element "continuing" between views during navigation.

- **Prepare** on source: `ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("key", element)`
- **Start** on destination: `animation.TryStart(destElement)`
- **Coordinated**: `animation.TryStart(mainElement, new[] { otherElements })`
- **Configurations**: GravityConnectedAnimationConfiguration (forward), DirectConnected (back), BasicConnected

ListView/GridView helpers: `PrepareConnectedAnimation()`, `TryStartConnectedAnimationAsync()`.

### 11.7 Collection Transitions (from source)

- **ItemCollectionTransition** -- base collection transition
- **ItemCollectionTransitionProvider** -- provides transitions for collection changes
- **LinedFlowLayoutItemCollectionTransitionProvider** -- specialized for LinedFlowLayout
- Trigger types: Add, Remove, Reset, LayoutTransition

Docs: [Animations overview](https://learn.microsoft.com/en-us/windows/apps/design/motion/xaml-animation) |
[Storyboarded animations](https://learn.microsoft.com/en-us/windows/apps/design/motion/storyboarded-animations) |
[Connected animation](https://learn.microsoft.com/en-us/windows/apps/develop/motion/connected-animation)

---

## 12. Composition Visual Layer

### 12.1 Visual Hierarchy

`Microsoft.UI.Composition` provides a retained-mode graphics API running at 60 FPS on an
independent thread.

- **Visual** -- lightweight, thread-agile base
- **ContainerVisual** -- can have children
- **SpriteVisual** -- painted with brushes, colors, or effects

### 12.2 Getting a Compositor

```csharp
var visual = ElementCompositionPreview.GetElementVisual(myElement);
var compositor = visual.Compositor;
// or without element:
var compositor = CompositionTarget.GetCompositorForCurrentThread();
```

### 12.3 Composition Animations

**KeyFrameAnimation**: ScalarKeyFrame, Vector2/3/4, Color, Quaternion.

**ExpressionAnimation**: mathematical relationships between properties.
```csharp
var expr = compositor.CreateExpressionAnimation("target.Offset.X + 100");
```

**ImplicitAnimations**: fire automatically when a property changes.
```csharp
implicitAnimations["Offset"] = offsetAnimation;
visual.ImplicitAnimations = implicitAnimations;
```

**NaturalMotionAnimations (Springs)**:
```csharp
var spring = compositor.CreateSpringVector3Animation();
spring.DampingRatio = 0.6f;
spring.Period = TimeSpan.FromMilliseconds(50);
```

**InteractionTracker**: input-driven animations for smooth scrolling, pull-to-refresh, etc.

### 12.4 SwapChainPanel

Hosts DirectX swap chains within XAML. Limitations:
- External content (below compositor surface)
- No transparency, no AcrylicBrush/CompositionBackdropBrush over it
- Same limitations for MediaPlayerElement, WebView2

Docs: [Visual layer](https://learn.microsoft.com/en-us/windows/apps/develop/composition/visual-layer) |
[Composition animations](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-animation) |
[Composition effects](https://learn.microsoft.com/en-us/windows/apps/develop/composition/composition-effects)

---

## 13. Materials and Effects

### 13.1 System Backdrops

```xaml
<Window.SystemBackdrop>
    <MicaBackdrop/>            <!-- or MicaBackdrop Kind="BaseAlt" -->
    <DesktopAcrylicBackdrop/>
</Window.SystemBackdrop>
```

Also on FlyoutBase.SystemBackdrop, Popup.SystemBackdrop.

- **Mica** -- opaque, captures desktop wallpaper once, for app base layer
- **Mica Alt** -- `MicaKind.BaseAlt` variant
- **Acrylic** -- semi-transparent frosted glass, for transient surfaces (Base/Thin)

Advanced: `MicaController`/`DesktopAcrylicController` with customizable FallbackColor, TintColor,
TintOpacity, LuminosityOpacity. Requires `MicaController.IsSupported()` check.

### 13.2 Composition Effects

Use Win2D `IGraphicsEffect` to define effects compiled into `CompositionEffectBrush`:

Supported: 2D Affine Transform, Arithmetic Composite, Blend (21 modes), Color Source, Composite
(13 modes), Contrast, Exposure, Grayscale, Gamma Transfer, Hue Rotate, Invert, Saturate, Sepia,
Temperature/Tint.

Effects can be chained, animated, and applied to XAML via `XamlCompositionBrushBase`.

### 13.3 Brushes (from source)

- **AcrylicBrush** -- frosted glass effect
- **RevealBrush** -- reveal highlight on hover
- **RadialGradientBrush** -- radial gradient fill

### 13.4 Lighting (from source)

- **XamlAmbientLight** -- ambient lighting
- **RevealBorderLight** -- border highlight
- **RevealHoverLight** -- hover reveal lighting

Docs: [System backdrops](https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops) |
[Mica](https://learn.microsoft.com/en-us/windows/apps/design/style/mica) |
[Acrylic](https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic) |
[Materials](https://learn.microsoft.com/en-us/windows/apps/develop/ui/materials)

---

## 14. Input Handling

### 14.1 Pointer Events

All on `UIElement`, routed-event bubbling:

| Event | When |
|---|---|
| `PointerPressed` | Pointer contacts element |
| `PointerMoved` | Pointer moves while over element |
| `PointerReleased` | Pointer lifts |
| `PointerEntered` | Pointer enters hit-test area |
| `PointerExited` | Pointer leaves |
| `PointerCanceled` | Contact abnormally lost |
| `PointerCaptureLost` | Capture released |

`PointerRoutedEventArgs` provides: device type, id, position, pressure, tilt, contact rect,
IsInContact, barrel button, eraser, KeyModifiers.

### 14.2 Gesture Events

| Event | Description |
|---|---|
| `Tapped` | Quick press-release |
| `DoubleTapped` | Two taps in succession |
| `RightTapped` | Right-click or touch-hold-release |
| `Holding` | Long press (touch only): Started/Completed/Canceled |

### 14.3 Manipulation Events

Require `ManipulationMode` flags: `TranslateX/Y`, `TranslateRailsX/Y`, `Rotate`, `Scale`,
`TranslateInertia`, `RotateInertia`, `ScaleInertia`, `All`, `System`, `None`.

| Event | Description |
|---|---|
| `ManipulationStarting` | About to start |
| `ManipulationStarted` | Movement detected |
| `ManipulationDelta` | Continuous Translation, Scale, Rotation, Expansion deltas |
| `ManipulationInertiaStarting` | Finger lifted, inertia begins |
| `ManipulationCompleted` | Finished |

### 14.4 Keyboard Input

| Event | Routing | Description |
|---|---|---|
| `PreviewKeyDown` | Tunneling | Fires before all other key handling |
| `KeyDown` | Bubbling | Standard key-press |
| `CharacterReceived` | After KeyDown | Character-level input |
| `PreviewKeyUp` | Tunneling | Before key-release |
| `KeyUp` | Bubbling | Standard key-release |

Modifier detection (no CoreWindow in WinUI 3):
```csharp
var state = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
```

Intercept handled events: `AddHandler(UIElement.KeyDownEvent, handler, handledEventsToo: true)`

### 14.5 Keyboard Accelerators

```xaml
<Button.KeyboardAccelerators>
    <KeyboardAccelerator Key="S" Modifiers="Control"/>
</Button.KeyboardAccelerators>
```

Scoped via `ScopeOwner`. Auto-appended to tooltips. UIA pattern invocation priority:
Invoke > Toggle > Selection > Expand/Collapse.

### 14.6 Access Keys

Alt-key mnemonics. `AccessKey` on UIElement, `IsAccessKeyScope` for nested scopes.

### 14.7 Focus Management

`FocusManager` static methods: `TryMoveFocus(direction)`, `FindNextFocusableElement`,
`GetFocusedElement`, `TryFocusAsync`.

Focus events (in order): `LosingFocus` -> `GettingFocus` -> `LostFocus` -> `GotFocus`.
`LosingFocus`/`GettingFocus` are cancelable.

Tab: `IsTabStop`, `TabIndex`, `TabFocusNavigation` (Local/Cycle/Once).
XY: `XYFocusUp/Down/Left/Right`, `XYFocusNavigationStrategy`.

### 14.8 Drag and Drop

Source: `CanDrag="True"`, handle `DragStarting` (populate DataPackage), `DropCompleted`.
Target: `AllowDrop="True"`, handle `DragOver` (set AcceptedOperation), `Drop` (read DataView).
DragUI: customize via `e.DragUIOverride` (Caption, bitmap).
ListView reorder: `AllowDrop="True"` + `CanReorderItems="True"`.

Docs: [Keyboard events](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-events) |
[Keyboard accelerators](https://learn.microsoft.com/en-us/windows/apps/develop/input/keyboard-accelerators) |
[Focus navigation](https://learn.microsoft.com/en-us/windows/apps/design/input/focus-navigation-programmatic) |
[Drag and drop](https://learn.microsoft.com/en-us/windows/apps/develop/data/drag-and-drop) |
[Touch guide](https://learn.microsoft.com/en-us/windows/apps/develop/input/touch-developer-guide)

---

## 15. Commands

### 15.1 ICommand

Foundation: `Execute(parameter)`, `CanExecute(parameter)`, `CanExecuteChanged` event.

Controls with `Command` property (Button, AppBarButton, MenuFlyoutItem) auto-call Execute on
click and disable when CanExecute returns false.

### 15.2 Command Hierarchy

```
ICommand
  -> XamlUICommand (adds Label, IconSource, Description, KeyboardAccelerators, AccessKey)
       -> StandardUICommand (predefined kinds: Cut, Copy, Paste, Delete, Save, Open, Undo, Redo...)
```

XamlUICommand fires `ExecuteRequested`/`CanExecuteRequested` events.

StandardUICommand provides predefined Label, Icon, Accelerators, Description per kind.

### 15.3 MVVM Pattern

WinUI has no built-in RelayCommand. Use CommunityToolkit.Mvvm's `RelayCommand`/`AsyncRelayCommand`
or custom implementation.

Docs: [Commanding](https://learn.microsoft.com/en-us/windows/apps/design/controls/commanding) |
[XamlUICommand](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.input.xamluicommand) |
[StandardUICommand](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.input.standarduicommand)

---

## 16. Accessibility

### 16.1 AutomationPeer

Every standard control has an `AutomationPeer` exposing it to UI Automation. Custom controls
override `OnCreateAutomationPeer()`.

Core overrides: `GetPatternCore`, `GetClassNameCore`, `GetAutomationControlTypeCore`, `GetNameCore`,
`IsContentElementCore`, `IsControlElementCore`.

Common patterns: `IInvokeProvider`, `IToggleProvider`, `ISelectionProvider`,
`IExpandCollapseProvider`, `IValueProvider`, `IRangeValueProvider`, `IScrollProvider`,
`IGridProvider`, `IDragProvider`, `IDropTargetProvider`.

The source repo contains 35+ automation peer classes.

### 16.2 AutomationProperties (Attached)

| Property | Purpose |
|---|---|
| `Name` | Accessible name (what screen reader announces) |
| `LabeledBy` | Points to TextBlock for accessible name |
| `HelpText` | Supplemental description |
| `LiveSetting` | Off/Polite/Assertive for live regions |
| `AutomationId` | Stable test automation identifier |
| `AccessibilityView` | Content/Control/Raw tree membership |
| `HeadingLevel` | Level1-9 for navigation landmarks |
| `LandmarkType` | Custom/Form/Main/Navigation/Search |
| `IsRequiredForForm` | Required field marker |

### 16.3 Live Regions

```xaml
<TextBlock AutomationProperties.LiveSetting="Assertive"/>
```
Raise `AutomationEvents.LiveRegionChanged` when content changes. Assertive = interrupts,
Polite = queued.

### 16.4 UIA Tree Views

| View | Purpose |
|---|---|
| Raw | All automation elements |
| Control | Interactive controls + structural points |
| Content | User-facing content only |

Docs: [Accessibility overview](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-overview) |
[Custom automation peers](https://learn.microsoft.com/en-us/windows/apps/design/accessibility/custom-automation-peers)

---

## 17. Threading Model

### 17.1 DispatcherQueue

`Microsoft.UI.Dispatching.DispatcherQueue` replaces UWP's CoreDispatcher. Thread singleton.

```csharp
dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () => { /* UI work */ });
```

Priorities: High, Normal, Low. Check `HasThreadAccess` before dispatching.

### 17.2 DispatcherQueueController

| Method | Use |
|---|---|
| `CreateOnCurrentThread()` | Initialize for calling thread |
| `CreateOnDedicatedThread()` | New thread with own queue |
| `ShutdownQueue()` | Sync shutdown (XAML Islands) |
| `ShutdownQueueAsync()` | Async shutdown (dedicated threads) |

Shutdown order: `ShutdownStarting` -> drain -> `FrameworkShutdownStarting` -> drain ->
`FrameworkShutdownCompleted` -> `ShutdownCompleted`.

### 17.3 System DispatcherQueue

Some components (MicaController) need `Windows.System.DispatcherQueue`. Call
`DispatcherQueue.EnsureSystemDispatcherQueue()`.

### 17.4 CoreDispatcher Migration

`CoreWindow`/`CoreDispatcher` are NOT available in WinUI 3 desktop apps.

Docs: [DispatcherQueue](https://learn.microsoft.com/en-us/windows/apps/develop/dispatcherqueue)

---

## 18. Windowing

### 18.1 AppWindow

`Microsoft.UI.Windowing.AppWindow` -- high-level HWND abstraction. 1:1 with top-level windows.

```csharp
AppWindow appWindow = this.AppWindow;  // from Window (SDK 1.3+)
```

### 18.2 Size and Position

`Resize(SizeInt32)`, `ResizeClient(SizeInt32)`, `Move(PointInt32)`, `MoveAndResize(RectInt32)`.
Z-order: `MoveInZOrderAtTop()`, `MoveInZOrderAtBottom()`, `MoveInZOrderBelow(WindowId)`.

### 18.3 Presenters

| Presenter | Description |
|---|---|
| **OverlappedPresenter** | Standard window with border/title bar/min/max/close (default) |
| **FullScreenPresenter** | Full-screen, no border, hides taskbar |
| **CompactOverlayPresenter** | Always-on-top, 16:9 (picture-in-picture) |

OverlappedPresenter config: `IsResizable`, `IsMaximizable`, `IsMinimizable`, `IsAlwaysOnTop`,
`IsModal`, `HasBorder`/`HasTitleBar`, `PreferredMinimumWidth/Height`.

Factory methods: `Create()`, `CreateForDialog()`, `CreateForToolWindow()`, `CreateForContextMenu()`.

### 18.4 Title Bar Customization

`AppWindow.TitleBar`: `ExtendsContentIntoTitleBar`, foreground/background colors, button colors,
`SetDragRectangles(RectInt32[])`, `IconShowOptions`.

### 18.5 Multi-Window

Each window is a new `Window()` instance with its own XAML tree and DispatcherQueue.

### 18.6 Events

`AppWindow.Changed` (position, size, presenter, visibility, z-order), `Closing` (cancelable),
`Destroying`.

Docs: [Manage app windows](https://learn.microsoft.com/en-us/windows/apps/develop/ui/manage-app-windows) |
[Title bar](https://learn.microsoft.com/en-us/windows/apps/develop/title-bar) |
[Multiple windows](https://learn.microsoft.com/en-us/windows/apps/develop/ui/multiple-windows)

---

## 19. Application Lifecycle

### 19.1 Application Class

Override `OnLaunched(LaunchActivatedEventArgs)` to create main window.
`Application.Resources` for app-wide XAML resources. `Application.RequestedTheme` for theme.

### 19.2 Activation

All activation goes through `OnLaunched`. Check kind via:
```csharp
var args = AppInstance.GetCurrent().GetActivatedEventArgs();
// args.Kind: Launch, File, Protocol, ShareTarget, Search, AppNotification, StartupTask, etc.
```

Do NOT use the `LaunchActivatedEventArgs` parameter -- it always reports Launch.

### 19.3 Single-Instancing

WinUI 3 is multi-instance by default. Single-instance via `AppInstance.FindOrRegisterForKey` +
`RedirectActivationToAsync` in `Main()`.

### 19.4 Suspension

Desktop WinUI 3 apps do NOT receive UWP-style Suspending/Resuming events. Save state on window
close or app-specific triggers.

### 19.5 Unhandled Exceptions

`Application.UnhandledException` -- set `e.Handled = true` to prevent termination. Fires for XAML
framework and WinRT exceptions.

Docs: [App lifecycle migration](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/migrate-to-windows-app-sdk/guides/applifecycle) |
[App instancing](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-instancing)

---

## 20. App Services

### 20.1 Printing

Use `PrintManagerInterop.GetForWindow(hWnd)` (required in WinUI 3). `PrintDocument` events:
Paginate, GetPreviewPage, AddPages. Show UI via `PrintManagerInterop.ShowPrintUIForWindowAsync`.

### 20.2 Clipboard

`Windows.ApplicationModel.DataTransfer.Clipboard`. `SetContent(DataPackage)`,
`GetContent().GetTextAsync()`. Formats: Text, HTML, RTF, Bitmap, StorageItems, Uri.
`ContentChanged` event. Only accessible when app is in focus.

### 20.3 File Pickers

**SDK 1.8+** (recommended): `Microsoft.Windows.Storage.Pickers.FileOpenPicker(appWindow.Id)`.
**Older**: `Windows.Storage.Pickers` with `InitializeWithWindow.Initialize(picker, hWnd)`.

Types: `FileOpenPicker`, `FileSavePicker`, `FolderPicker`.

Docs: [Printing](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/print-from-your-app) |
[File pickers](https://learn.microsoft.com/en-us/windows/apps/develop/files/using-file-folder-pickers) |
[Clipboard](https://learn.microsoft.com/en-us/windows/uwp/app-to-app/copy-and-paste)

---

## 21. Interop

### 21.1 HWND Interop

```csharp
var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(myWindow);
WindowId id = Win32Interop.GetWindowIdFromWindow(hWnd);
AppWindow appWindow = AppWindow.GetFromWindowId(id);
```

### 21.2 XAML Islands

Host WinUI 3 content in non-XAML apps via `DesktopWindowXamlSource`. Requires
DispatcherQueue initialization and synchronous shutdown.

### 21.3 C++/WinRT Projections

Standard C++ projection for WinRT APIs. Namespace: `Microsoft::UI::Xaml` (WinUI 3).
Patterns: `auto button = Button();`, `button.Click({this, &Page::OnClick});`, `co_await`.

### 21.4 C#/WinRT Projections

`Microsoft.Windows.CsWinRT` NuGet. TFM: `net8.0-windows10.0.19041.0`.
`.As<T>()` for COM casting.

### 21.5 WinRT Component Authoring

**C#**: `<CsWinRTComponent>true</CsWinRTComponent>`, WinRT-compatible public surface only.
**C++/WinRT**: MIDL 3.0 IDL files, generates headers and implementations.

### 21.6 COM Interop Interfaces

| Interface | Purpose |
|---|---|
| `IWindowNative` | Get HWND from Window |
| `IInitializeWithWindow` | Initialize pickers/dialogs with owner HWND |
| `IDesktopWindowXamlSourceNative` | XAML Islands HWND management |

Docs: [C#/WinRT](https://learn.microsoft.com/en-us/windows/apps/develop/platform/csharp-winrt/) |
[MIDL 3.0](https://learn.microsoft.com/en-us/uwp/midl-3/intro) |
[XAML Islands](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/xaml-islands/host-custom-control-with-xaml-islands-cpp)

---

## 22. Content and Items Infrastructure

### 22.1 Control Hierarchy

```
DependencyObject
  UIElement
    FrameworkElement
      Control
        ContentControl          -- single Content object
          Frame, Page, AppBarButton, NavigationViewItem,
          TabViewItem, Expander, ScrollViewer, ContentDialog
        ItemsControl              -- collection via Items/ItemsSource
          Selector                -- adds SelectedItem/SelectedIndex
            ListViewBase (ListView, GridView)
            ComboBox, ListBox, FlipView, TabView
          MenuFlyoutPresenter, TreeView
```

### 22.2 Virtualization

| Control | Virtualization |
|---|---|
| ListView/GridView | Built-in via ItemsStackPanel / ItemsWrapGrid |
| ItemsView | Built-in (uses ItemsRepeater internally) |
| ItemsRepeater | Built-in with VirtualizingLayout in scroll host |
| ItemsControl (base) | None -- avoid for large collections |
| TreeView | Built-in, expands on demand |

Performance: `x:Phase` for incremental loading, `ContainerContentChanging` for deferred binding.

### 22.3 Data Source Infrastructure (from source)

- **ItemsSourceView** -- data source abstraction
- **SelectionModel** -- selection state management with hierarchical support
- **ElementFactory** / **RecyclingElementFactory** -- element creation and recycling
- **ElementManager** -- manages element lifecycle

### 22.4 Collection Types (from source)

- **Vector\<T\>** -- generic vector
- **HashMap\<K,V\>** -- generic hash map
- **BindableVector** -- bindable collection base
- **VectorChangedEventArgs** -- collection change notifications

Docs: [Optimize ListView/GridView](https://learn.microsoft.com/en-us/windows/apps/develop/performance/optimize-gridview-and-listview)

---

## Source Repository Statistics

From `microsoft-ui-xaml-lift`:

| Category | Count |
|---|---|
| Public controls/components | 48+ |
| IDL definition files | 121 |
| C++ header files | 565 |
| C++ implementation files | 600 |
| XAML theme resource files | 384 |
| C# managed files | 479 |
| Automation peer classes | 35+ |
| Control theme resource files | 40+ |
| Generated property files | 200+ |

---

## Key Documentation Entry Points

| Topic | URL |
|---|---|
| All controls index | https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/ |
| WinUI 3 overview | https://learn.microsoft.com/en-us/windows/apps/winui/winui3/ |
| Layout panels | https://learn.microsoft.com/en-us/windows/apps/design/layout/layout-panels |
| Data binding in depth | https://learn.microsoft.com/en-us/windows/apps/develop/data-binding/data-binding-in-depth |
| XAML styles | https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-styles |
| Theming | https://learn.microsoft.com/en-us/windows/apps/develop/ui/theming |
| Animations overview | https://learn.microsoft.com/en-us/windows/apps/design/motion/xaml-animation |
| Visual layer | https://learn.microsoft.com/en-us/windows/apps/develop/composition/visual-layer |
| System backdrops | https://learn.microsoft.com/en-us/windows/apps/develop/ui/system-backdrops |
| Accessibility | https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessibility-overview |
| Windowing | https://learn.microsoft.com/en-us/windows/apps/develop/ui-input/windowing-overview |
| DispatcherQueue | https://learn.microsoft.com/en-us/windows/apps/develop/dispatcherqueue |
| ResourceDictionary | https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/xaml-resource-dictionary |
| Dependency properties | https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/dependency-properties-overview |
| Commanding | https://learn.microsoft.com/en-us/windows/apps/design/controls/commanding |
| WinUI API namespace | https://learn.microsoft.com/en-us/windows/winui/api/microsoft.ui.xaml.controls |
| Community Toolkit | https://learn.microsoft.com/en-us/dotnet/communitytoolkit/windows/ |
