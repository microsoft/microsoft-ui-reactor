---
name: cs-ui-framework-review
description: >-
  Review C# WPF, WinUI 3, and .NET MAUI code for UI framework correctness
  defects: DependencyProperty registration errors (wrong owner type, name
  mismatch, CLR wrapper side effects, mutable default values), data binding
  failures (missing INotifyPropertyChanged, wrong property name strings,
  non-public properties, cross-thread ObservableCollection modification),
  control lifecycle violations (constructor logic depending on visual tree,
  missing Loaded/Unloaded event pairs), threading errors (DependencyObject
  access from background thread, missing DispatcherQueue marshaling), and
  resource/style misuse (StaticResource forward references, implicit style
  scope errors, missing ThemeResource for theme changes).
  38 patterns across 6 sub-domains covering dependency-property-correctness,
  data-binding-MVVM, control-lifecycle-visual-tree, threading-dispatcher,
  and resource-style patterns. Sources include WPF/WinUI team guidance,
  .NET MAUI team, and XAML specification.
  Use this skill when reviewing C# code using WPF, WinUI 3, .NET MAUI, or
  any XAML-based UI framework programming model.
---

# UI Framework (WPF / WinUI 3 / MAUI) Code Review

## Quick Detection

**Primary Symptoms (in code under review)**:
- `DependencyProperty.Register` calls where the owner type or property name string doesn't match
- CLR property wrappers that do more than `GetValue`/`SetValue` (bypassed by binding engine)
- ViewModel classes missing `INotifyPropertyChanged` implementation
- `PropertyChanged` raised with hard-coded string instead of `nameof()`
- `ObservableCollection` modified from a background thread
- UI element access from non-UI thread without `DispatcherQueue.TryEnqueue`
- `StaticResource` referencing a key defined later in the XAML file (forward reference)
- Control constructor logic that depends on visual tree being connected

**Key Code Patterns to Search For**:
```csharp
// DependencyProperty name mismatch
public static readonly DependencyProperty WidthProperty =
    DependencyProperty.Register("Widht", ...);  // Typo — binding to "Width" silently fails

// CLR wrapper with side effects (bypassed by binding engine)
public double Width
{
    get { return (double)GetValue(WidthProperty); }
    set { SetValue(WidthProperty, value); OnWidthChanged(); }  // OnWidthChanged never called via binding
}

// ObservableCollection modified on background thread
Task.Run(() => Items.Add(newItem));  // Cross-thread exception

// Missing INotifyPropertyChanged
public class MyViewModel  // No INPC — UI never updates
{
    public string Name { get; set; }
}
```

## Analysis Workflow

### Step 1: Map UI Components

Identify the UI framework in use and the component architecture.

1. Check for framework indicators:
   - **WPF**: `xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"`, `System.Windows`
   - **WinUI 3**: `Microsoft.UI.Xaml`, `DispatcherQueue`, `XamlRoot`
   - **MAUI**: `Microsoft.Maui`, `ContentPage`, `BindableProperty`

2. Identify all custom controls, user controls, and view models in the PR.

3. Build a **Component Map**:
   | Component | Type | DependencyProperties | Bindings | Thread Concerns |
   |-----------|------|---------------------|----------|-----------------|
   | `MyControl` | UserControl | 3 DPs registered | 5 bindings | Background data load |

### Step 2: Scan for Pattern Matches

Apply the 38 UI framework patterns across 6 sub-domains.

**Priority order** (by severity and frequency):
1. **Threading & Dispatcher** (8 patterns) — cross-thread access crashes immediately in production
2. **Dependency Property Correctness** (10 patterns) — silent binding failures, shared state corruption
3. **Data Binding & MVVM** (10 patterns) — UI never updates, silent failures, memory leaks
4. **Control Lifecycle & Visual Tree** (8 patterns) — null references, resource leaks
5. **Resource & Style Patterns** (6 patterns) — runtime exceptions, theme issues

**Key detection queries per category**:

| Sub-domain | What to Look For | Risk |
|-----------|-----------------|------|
| Threading | `Task.Run` + UI access, missing `DispatcherQueue`, `ObservableCollection` on background thread | Critical |
| DependencyProperty | Name string mismatch, CLR wrapper side effects, mutable default value | Critical/High |
| Data Binding | Missing INPC, `PropertyChanged` with string literal, binding to non-public property | High |
| Lifecycle | Constructor visual tree access, missing Loaded/Unloaded pair, template part access before `OnApplyTemplate` | High |
| Resources | `StaticResource` forward reference, missing `ThemeResource`, duplicate `MergedDictionaries` | Medium/High |

### Step 3: Classify Findings

For each potential match:

1. **Confirm the defect**: Is the pattern actually triggered?
   - Does the binding engine actually bind to this property? (If only set in code-behind, CLR wrapper side effects are acceptable.)
   - Is the background thread actually modifying the collection while it's bound?
   - Is the `StaticResource` actually a forward reference, or is it in a merged dictionary?
2. **Severity**:
   - **Critical**: Cross-thread UI access, mutable reference-type DP default, `ObservableCollection` on background thread
   - **High**: DP name mismatch, CLR wrapper side effects, missing INPC, memory leaks from binding
   - **Medium**: Wrong metadata flags, missing `ValidateValueCallback`, suboptimal `DynamicResource`
   - **Low**: Style scope issues in app-level resources, unnecessary `DynamicResource`

### Step 4: Generate Fix

**Fix Strategy Decision Tree**:

```
What kind of UI framework defect?
├── DependencyProperty
│   ├── Name mismatch → Fix string to match CLR property name, use nameof() where supported
│   ├── CLR wrapper side effects → Move logic to PropertyChangedCallback
│   ├── Mutable default → Use PropertyMetadata with new instance per-owner in CreateDefaultValueCallback
│   └── Wrong owner type → Fix typeof() to match declaring class
├── Data Binding
│   ├── Missing INPC → Implement interface, raise PropertyChanged in setters
│   ├── Wrong property name → Use nameof() instead of string literal
│   ├── Non-public property → Make public, or use x:FieldModifier in XAML
│   └── Memory leak → Use WeakReference or ensure proper cleanup
├── Control Lifecycle
│   ├── Constructor visual tree access → Move to Loaded event or OnApplyTemplate
│   ├── Missing Unloaded cleanup → Add Unloaded handler that mirrors Loaded registration
│   └── Template parts null → Access template parts in OnApplyTemplate override
├── Threading
│   ├── Background thread UI access → Wrap in DispatcherQueue.TryEnqueue
│   ├── ObservableCollection on background thread → Marshal to UI thread, or use lock + CollectionChanged on UI thread
│   └── Synchronous Dispatcher.Invoke → Switch to async TryEnqueue/BeginInvoke
└── Resources
    ├── StaticResource forward reference → Move resource definition before usage, or switch to DynamicResource
    ├── Missing ThemeResource → Replace StaticResource with ThemeResource for theme-responsive values
    └── Duplicate MergedDictionaries → Use SharedResourceDictionary or ensure single loading
```

**Fix template**:
```markdown
#### Finding: [Pattern-ID] — [Brief description]
**File**: `path/to/file.cs` lines N-M
**Severity**: [Critical|High|Medium|Low]
**Pattern**: [Pattern ID and name]

**Before** (defective):
```csharp
// Problematic UI code
```

**After** (correct):
```csharp
// Fixed UI code
```

**Verification**:
- [ ] Binding engine exercises the corrected code path
- [ ] No cross-thread access under async load scenarios
- [ ] Visual tree lifecycle events paired (Loaded/Unloaded)
- [ ] Build succeeds; XAML compilation passes
```

### Step 5: Verify Fix

1. **Binding verification**: Confirm bindings resolve at runtime (check Output window for binding errors)
2. **Thread safety**: Verify all UI access paths marshal to the UI thread
3. **Lifecycle correctness**: Confirm Loaded/Unloaded handlers are paired and resource cleanup occurs
4. **Theme testing**: Switch between Light/Dark/HighContrast themes to verify ThemeResource usage
5. **Memory profiling**: Check for binding-related memory leaks using diagnostic tools

---

## Pattern Catalog

### Dependency Property Correctness

#### UI-DP-01: DependencyProperty.Register with wrong owner type
**Severity**: High — property won't work correctly on subclasses

The first `typeof()` parameter in `Register` must be the class that declares the property. Using a base class or wrong class causes the property to be registered against the wrong type, breaking inheritance and property lookup.

```csharp
// BAD: Wrong owner type — property registered against base class
public class MyButton : Button
{
    public static readonly DependencyProperty CornerStyleProperty =
        DependencyProperty.Register(
            nameof(CornerStyle),
            typeof(CornerStyle),
            typeof(Button),  // BUG: Should be typeof(MyButton)
            new PropertyMetadata(CornerStyle.Square));

    public CornerStyle CornerStyle
    {
        get => (CornerStyle)GetValue(CornerStyleProperty);
        set => SetValue(CornerStyleProperty, value);
    }
}
```

```csharp
// GOOD: Correct owner type matches declaring class
public class MyButton : Button
{
    public static readonly DependencyProperty CornerStyleProperty =
        DependencyProperty.Register(
            nameof(CornerStyle),
            typeof(CornerStyle),
            typeof(MyButton),  // Correct: matches the declaring class
            new PropertyMetadata(CornerStyle.Square));

    public CornerStyle CornerStyle
    {
        get => (CornerStyle)GetValue(CornerStyleProperty);
        set => SetValue(CornerStyleProperty, value);
    }
}
```

---

#### UI-DP-02: DependencyProperty name string doesn't match CLR wrapper property name
**Severity**: High — XAML binding breaks silently

The name string in `Register` must exactly match the CLR property name. A typo means `{Binding PropertyName}` resolves to the CLR property, but the DP has a different internal name, causing silent binding failures.

```csharp
// BAD: Name string has typo — binding to "IsExpanded" silently fails
public static readonly DependencyProperty IsExpandedProperty =
    DependencyProperty.Register(
        "IsExpaned",  // BUG: Typo — missing 'd'
        typeof(bool),
        typeof(ExpanderControl),
        new PropertyMetadata(false));

public bool IsExpanded
{
    get => (bool)GetValue(IsExpandedProperty);
    set => SetValue(IsExpandedProperty, value);
}
```

```csharp
// GOOD: Use nameof() to guarantee name matches CLR property
public static readonly DependencyProperty IsExpandedProperty =
    DependencyProperty.Register(
        nameof(IsExpanded),  // Compiler-verified match
        typeof(bool),
        typeof(ExpanderControl),
        new PropertyMetadata(false));

public bool IsExpanded
{
    get => (bool)GetValue(IsExpandedProperty);
    set => SetValue(IsExpandedProperty, value);
}
```

---

#### UI-DP-03: CLR property wrapper does more than GetValue/SetValue
**Severity**: Critical — Framework Design Rule

The XAML binding engine calls `GetValue`/`SetValue` directly, bypassing the CLR property wrapper entirely. Any logic in the wrapper (validation, events, side effects) will not execute when the property is set via binding, style, animation, or template.

```csharp
// BAD: Side effect in CLR wrapper — bypassed by binding engine
public double Opacity
{
    get => (double)GetValue(OpacityProperty);
    set
    {
        if (value < 0 || value > 1)
            throw new ArgumentOutOfRangeException();  // Never called via binding
        SetValue(OpacityProperty, value);
        OnOpacityChanged();  // Never called via binding
        _logger.Log($"Opacity set to {value}");  // Never called via binding
    }
}
```

```csharp
// GOOD: CLR wrapper is pure pass-through; logic in callbacks
public static readonly DependencyProperty OpacityProperty =
    DependencyProperty.Register(
        nameof(Opacity),
        typeof(double),
        typeof(MyControl),
        new PropertyMetadata(1.0, OnOpacityChanged),
        new ValidateValueCallback(IsValidOpacity));  // WPF validation

public double Opacity
{
    get => (double)GetValue(OpacityProperty);
    set => SetValue(OpacityProperty, value);  // Pure pass-through only
}

private static bool IsValidOpacity(object value)
    => value is double d && d >= 0.0 && d <= 1.0;

private static void OnOpacityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
{
    var control = (MyControl)d;
    control._logger.Log($"Opacity changed to {e.NewValue}");
}
```

---

#### UI-DP-04: Missing PropertyChangedCallback when side effects needed
**Severity**: Medium

When a DependencyProperty change should trigger visual updates, state changes, or event raises, but no `PropertyChangedCallback` is registered, the side effects never occur.

```csharp
// BAD: No callback — control never redraws when Color changes
public static readonly DependencyProperty ColorProperty =
    DependencyProperty.Register(
        nameof(Color),
        typeof(Color),
        typeof(ColorPicker),
        new PropertyMetadata(Colors.Black));  // No callback registered
```

```csharp
// GOOD: PropertyChangedCallback triggers visual update
public static readonly DependencyProperty ColorProperty =
    DependencyProperty.Register(
        nameof(Color),
        typeof(Color),
        typeof(ColorPicker),
        new PropertyMetadata(Colors.Black, OnColorChanged));

private static void OnColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
{
    var picker = (ColorPicker)d;
    picker.UpdateColorPreview((Color)e.NewValue);
}
```

---

#### UI-DP-05: CoerceValueCallback that can throw
**Severity**: High — crash in binding pipeline

A `CoerceValueCallback` that throws an exception will crash the property system during binding evaluation. Coerce callbacks must always return a valid value; they should clamp or fall back, never throw.

```csharp
// BAD: CoerceValueCallback throws — crashes binding pipeline
private static object CoerceMinWidth(DependencyObject d, object baseValue)
{
    double value = (double)baseValue;
    if (value < 0)
        throw new ArgumentException("MinWidth cannot be negative");  // CRASH in binding
    return value;
}
```

```csharp
// GOOD: CoerceValueCallback clamps to valid range
private static object CoerceMinWidth(DependencyObject d, object baseValue)
{
    double value = (double)baseValue;
    if (value < 0)
        return 0.0;  // Clamp to valid minimum — no exception
    if (double.IsNaN(value) || double.IsInfinity(value))
        return 0.0;
    return value;
}
```

---

#### UI-DP-06: Default value is mutable reference type
**Severity**: Critical — shared across all instances

When a DependencyProperty's default value is a mutable reference type (e.g., a `List<T>`, `ObservableCollection<T>`, or custom object), that single instance is shared across all instances of the control. Modifying it on one instance affects all others.

```csharp
// BAD: Mutable reference type as default — shared across ALL instances
public static readonly DependencyProperty ItemsProperty =
    DependencyProperty.Register(
        nameof(Items),
        typeof(ObservableCollection<string>),
        typeof(MyListControl),
        new PropertyMetadata(new ObservableCollection<string>()));  // ONE instance shared by ALL controls
```

```csharp
// GOOD (WPF): Use CreateDefaultValueCallback for per-instance defaults
public static readonly DependencyProperty ItemsProperty =
    DependencyProperty.Register(
        nameof(Items),
        typeof(ObservableCollection<string>),
        typeof(MyListControl),
        new PropertyMetadata(null));  // null default

// In constructor, set per-instance value:
public MyListControl()
{
    InitializeComponent();
    Items = new ObservableCollection<string>();  // Each instance gets its own collection
}

public ObservableCollection<string> Items
{
    get => (ObservableCollection<string>)GetValue(ItemsProperty);
    set => SetValue(ItemsProperty, value);
}
```

```csharp
// GOOD (WPF advanced): CreateDefaultValueCallback
public static readonly DependencyProperty ItemsProperty =
    DependencyProperty.Register(
        nameof(Items),
        typeof(FreezableCollection<MyItem>),
        typeof(MyListControl),
        new FrameworkPropertyMetadata(
            new CreateDefaultValueCallback(() => new FreezableCollection<MyItem>())));
```

---

#### UI-DP-07: Attached property registered as regular DependencyProperty
**Severity**: High

Attached properties must be registered with `DependencyProperty.RegisterAttached`, not `DependencyProperty.Register`. Using `Register` for an attached property means it cannot be set on arbitrary elements via XAML.

```csharp
// BAD: Attached property using Register instead of RegisterAttached
public static readonly DependencyProperty DockProperty =
    DependencyProperty.Register(  // WRONG: should be RegisterAttached
        "Dock",
        typeof(Dock),
        typeof(DockPanel),
        new PropertyMetadata(Dock.Left));

// These static Get/Set methods won't work correctly:
public static Dock GetDock(DependencyObject obj) => (Dock)obj.GetValue(DockProperty);
public static void SetDock(DependencyObject obj, Dock value) => obj.SetValue(DockProperty, value);
```

```xml
<!-- This XAML will fail or behave incorrectly -->
<Button local:DockPanel.Dock="Right" Content="Save" />
```

```csharp
// GOOD: Attached property using RegisterAttached
public static readonly DependencyProperty DockProperty =
    DependencyProperty.RegisterAttached(
        "Dock",
        typeof(Dock),
        typeof(DockPanel),
        new PropertyMetadata(Dock.Left, OnDockChanged));

public static Dock GetDock(DependencyObject obj) => (Dock)obj.GetValue(DockProperty);
public static void SetDock(DependencyObject obj, Dock value) => obj.SetValue(DockProperty, value);
```

---

#### UI-DP-08: Missing ValidateValueCallback for range-constrained properties
**Severity**: Medium

Properties with value constraints (e.g., 0-100 for a percentage, non-negative dimensions) should use `ValidateValueCallback` (WPF) or validation in the `PropertyChangedCallback` to reject invalid values early rather than allowing invalid state to propagate.

```csharp
// BAD: No validation — invalid values silently accepted
public static readonly DependencyProperty ProgressProperty =
    DependencyProperty.Register(
        nameof(Progress),
        typeof(double),
        typeof(ProgressRing),
        new PropertyMetadata(0.0));
// Nothing prevents: myRing.Progress = -50; or myRing.Progress = 999;
```

```csharp
// GOOD: ValidateValueCallback rejects invalid values immediately (WPF)
public static readonly DependencyProperty ProgressProperty =
    DependencyProperty.Register(
        nameof(Progress),
        typeof(double),
        typeof(ProgressRing),
        new PropertyMetadata(0.0, OnProgressChanged),
        new ValidateValueCallback(IsValidProgress));

private static bool IsValidProgress(object value)
    => value is double d && d >= 0.0 && d <= 100.0 && !double.IsNaN(d);

// GOOD (WinUI 3 — no ValidateValueCallback): Validate in PropertyChangedCallback
private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
{
    var ring = (ProgressRing)d;
    double newValue = (double)e.NewValue;
    if (newValue < 0.0 || newValue > 100.0)
    {
        ring.Progress = Math.Clamp(newValue, 0.0, 100.0);  // Coerce to valid range
        return;
    }
    ring.UpdateProgressVisual(newValue);
}
```

---

#### UI-DP-09: DependencyProperty registered with wrong metadata flags
**Severity**: Medium

Using `FrameworkPropertyMetadataOptions.AffectsMeasure` when only a render update is needed forces unnecessary and expensive layout passes. Conversely, using `AffectsRender` when measure is actually needed causes stale layout.

```csharp
// BAD: AffectsMeasure for a color property — forces expensive layout pass on every color change
public static readonly DependencyProperty ForegroundColorProperty =
    DependencyProperty.Register(
        nameof(ForegroundColor),
        typeof(Color),
        typeof(CustomLabel),
        new FrameworkPropertyMetadata(
            Colors.Black,
            FrameworkPropertyMetadataOptions.AffectsMeasure));  // Color doesn't change size!
```

```csharp
// GOOD: AffectsRender — color change only needs repaint, not relayout
public static readonly DependencyProperty ForegroundColorProperty =
    DependencyProperty.Register(
        nameof(ForegroundColor),
        typeof(Color),
        typeof(CustomLabel),
        new FrameworkPropertyMetadata(
            Colors.Black,
            FrameworkPropertyMetadataOptions.AffectsRender));  // Only repaint needed
```

---

#### UI-DP-10: Read-only DependencyProperty without DependencyPropertyKey encapsulation
**Severity**: High

Read-only DependencyProperties must use `DependencyPropertyKey` to prevent external code from setting the value. Exposing the `DependencyProperty` without the key allows anyone to call `SetValue`.

```csharp
// BAD: "Read-only" property is actually settable by anyone
public static readonly DependencyProperty IsMouseOverProperty =
    DependencyProperty.Register(
        nameof(IsMouseOver),
        typeof(bool),
        typeof(HoverControl),
        new PropertyMetadata(false));

// Anyone can do: myControl.SetValue(HoverControl.IsMouseOverProperty, true);
```

```csharp
// GOOD: DependencyPropertyKey makes it truly read-only externally
private static readonly DependencyPropertyKey IsMouseOverPropertyKey =
    DependencyProperty.RegisterReadOnly(
        nameof(IsMouseOver),
        typeof(bool),
        typeof(HoverControl),
        new PropertyMetadata(false));

public static readonly DependencyProperty IsMouseOverProperty =
    IsMouseOverPropertyKey.DependencyProperty;  // Read-only public access

public bool IsMouseOver => (bool)GetValue(IsMouseOverProperty);

// Only the declaring class can set it:
private void OnMouseEnter() => SetValue(IsMouseOverPropertyKey, true);
private void OnMouseLeave() => SetValue(IsMouseOverPropertyKey, false);
```

---

### Data Binding & MVVM

#### UI-BIND-01: INotifyPropertyChanged not implemented on bound ViewModel
**Severity**: High — UI never updates

If a ViewModel is used as `DataContext` but doesn't implement `INotifyPropertyChanged`, the UI will display initial values but never update when properties change.

```csharp
// BAD: No INPC — UI shows initial values forever
public class SettingsViewModel
{
    public string UserName { get; set; }  // UI bound to this will never update
    public bool IsDarkMode { get; set; }
}
```

```csharp
// GOOD: INPC implemented — UI updates on property changes
public class SettingsViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _userName = string.Empty;
    public string UserName
    {
        get => _userName;
        set
        {
            if (_userName != value)
            {
                _userName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UserName)));
            }
        }
    }

    private bool _isDarkMode;
    public bool IsDarkMode
    {
        get => _isDarkMode;
        set
        {
            if (_isDarkMode != value)
            {
                _isDarkMode = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDarkMode)));
            }
        }
    }
}
```

---

#### UI-BIND-02: PropertyChanged raised with wrong property name string (typo)
**Severity**: High — use `nameof()`

A typo in the property name string means the binding engine doesn't recognize the change notification, so the UI doesn't update for the intended property.

```csharp
// BAD: Typo in property name string — UI never updates for "Title"
private string _title = "";
public string Title
{
    get => _title;
    set
    {
        _title = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Titel"));  // BUG: Typo
    }
}
```

```csharp
// GOOD: nameof() ensures compile-time correctness
private string _title = "";
public string Title
{
    get => _title;
    set
    {
        if (_title != value)
        {
            _title = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
        }
    }
}
```

---

#### UI-BIND-03: Binding to non-public property
**Severity**: High — binding silently fails

The XAML binding engine can only bind to `public` properties. Binding to `internal`, `protected`, or `private` properties silently fails with no runtime error; the UI simply shows nothing or the default value.

```csharp
// BAD: Internal property — binding silently fails
public class UserViewModel : INotifyPropertyChanged
{
    internal string DisplayName { get; set; }  // Not accessible to binding engine
}
```

```xml
<!-- This binding silently fails — no error, no data -->
<TextBlock Text="{Binding DisplayName}" />
```

```csharp
// GOOD: Public property — binding works
public class UserViewModel : INotifyPropertyChanged
{
    public string DisplayName { get; set; }
}
```

---

#### UI-BIND-04: Two-way binding on read-only property
**Severity**: Medium — silent failure

A `{Binding Mode=TwoWay}` on a property without a public setter silently fails to write back. The UI may appear to accept input, but the ViewModel value never changes.

```csharp
// BAD: Read-only property with TwoWay binding — setter never called
public class SearchViewModel : INotifyPropertyChanged
{
    public string Query { get; }  // No setter — TwoWay binding write-back silently fails
}
```

```xml
<!-- User types in TextBox, but ViewModel.Query never updates -->
<TextBox Text="{Binding Query, Mode=TwoWay}" />
```

```csharp
// GOOD: Property has public setter for TwoWay binding
public class SearchViewModel : INotifyPropertyChanged
{
    private string _query = "";
    public string Query
    {
        get => _query;
        set { _query = value; OnPropertyChanged(nameof(Query)); }
    }
}
```

---

#### UI-BIND-05: Memory leak from strong-reference binding to long-lived source
**Severity**: High

When a short-lived UI element binds to a long-lived source (e.g., a singleton service or static ViewModel), the binding holds a strong reference that prevents the UI element from being garbage collected, causing a memory leak.

```csharp
// BAD: Short-lived UserControl binds to singleton — UserControl is never collected
public partial class StatusPanel : UserControl
{
    public StatusPanel()
    {
        InitializeComponent();
        DataContext = AppState.Instance;  // Singleton holds reference back via PropertyChanged
    }
}
```

```csharp
// GOOD: Use WeakEventManager or unsubscribe on Unloaded
public partial class StatusPanel : UserControl
{
    public StatusPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DataContext = AppState.Instance;
        // WPF bindings use weak references by default, but explicit PropertyChanged
        // subscriptions need WeakEventManager:
        PropertyChangedEventManager.AddHandler(
            AppState.Instance, OnAppStateChanged, string.Empty);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        PropertyChangedEventManager.RemoveHandler(
            AppState.Instance, OnAppStateChanged, string.Empty);
        DataContext = null;
    }
}
```

---

#### UI-BIND-06: Binding in code-behind without specifying UpdateSourceTrigger
**Severity**: Medium

When creating bindings in code-behind (rather than XAML), the `UpdateSourceTrigger` defaults to `Default`, which for `TextBox.Text` in WPF is `LostFocus`. This means changes aren't pushed to the source until the TextBox loses focus, which can surprise developers.

```csharp
// BAD: UpdateSourceTrigger not specified — defaults to LostFocus for TextBox.Text
var binding = new Binding("SearchQuery")
{
    Source = _viewModel,
    Mode = BindingMode.TwoWay
};
searchBox.SetBinding(TextBox.TextProperty, binding);
// User types but ViewModel doesn't update until focus leaves TextBox
```

```csharp
// GOOD: Explicitly specify UpdateSourceTrigger
var binding = new Binding("SearchQuery")
{
    Source = _viewModel,
    Mode = BindingMode.TwoWay,
    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged  // Updates as user types
};
searchBox.SetBinding(TextBox.TextProperty, binding);
```

---

#### UI-BIND-07: DataContext inheritance broken by setting DataContext on intermediate element
**Severity**: Medium

XAML DataContext flows down the element tree via inheritance. Setting `DataContext` on an intermediate element breaks this flow for all descendants, causing bindings below it to resolve against the new DataContext instead of the expected parent.

```xml
<!-- BAD: DataContext on Grid breaks inheritance for children -->
<Window DataContext="{Binding MainViewModel}">
    <Grid DataContext="{Binding SettingsSection}">
        <!-- These bindings resolve against SettingsSection, not MainViewModel -->
        <TextBlock Text="{Binding UserName}" />  <!-- Looks for SettingsSection.UserName -->
        <Button Command="{Binding SaveCommand}" />  <!-- Looks for SettingsSection.SaveCommand -->
    </Grid>
</Window>
```

```xml
<!-- GOOD: Use explicit source for nested context without breaking inheritance -->
<Window DataContext="{Binding MainViewModel}">
    <Grid>
        <TextBlock Text="{Binding UserName}" />  <!-- Resolves to MainViewModel.UserName -->
        <ContentControl Content="{Binding SettingsSection}">
            <ContentControl.ContentTemplate>
                <DataTemplate>
                    <!-- Bindings here correctly resolve against SettingsSection -->
                    <TextBox Text="{Binding Theme}" />
                </DataTemplate>
            </ContentControl.ContentTemplate>
        </ContentControl>
    </Grid>
</Window>
```

---

#### UI-BIND-08: ICommand.CanExecuteChanged not raised on state change
**Severity**: Medium — stale button enabled state

When a condition that affects `CanExecute` changes, the command must raise `CanExecuteChanged` to notify the UI to re-evaluate button enabled state. Without this, buttons remain enabled or disabled incorrectly.

```csharp
// BAD: CanExecute depends on IsLoggedIn, but CanExecuteChanged never raised
public class LoginCommand : ICommand
{
    private readonly AuthService _auth;

    public bool CanExecute(object? parameter) => !_auth.IsLoggedIn;

    public void Execute(object? parameter) { _auth.Login(); }

    public event EventHandler? CanExecuteChanged;
    // BUG: Nothing ever raises CanExecuteChanged when IsLoggedIn changes
}
```

```csharp
// GOOD: Raise CanExecuteChanged when dependent state changes
public class LoginCommand : ICommand
{
    private readonly AuthService _auth;

    public LoginCommand(AuthService auth)
    {
        _auth = auth;
        _auth.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AuthService.IsLoggedIn))
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public bool CanExecute(object? parameter) => !_auth.IsLoggedIn;

    public void Execute(object? parameter) { _auth.Login(); }

    public event EventHandler? CanExecuteChanged;
}
```

---

#### UI-BIND-09: ObservableCollection modified from background thread
**Severity**: Critical — cross-thread exception

`ObservableCollection<T>` raises `CollectionChanged` events that the UI subscribes to. Modifying the collection on a background thread raises the event on that thread, causing a cross-thread access violation in the UI framework.

```csharp
// BAD: Background thread modifies bound ObservableCollection — throws InvalidOperationException
public class DataViewModel
{
    public ObservableCollection<LogEntry> Logs { get; } = new();

    public async Task LoadLogsAsync()
    {
        var entries = await _logService.GetEntriesAsync();
        foreach (var entry in entries)
        {
            Logs.Add(entry);  // CRASH: Called on background thread, bound to UI
        }
    }
}
```

```csharp
// GOOD: Marshal collection changes to UI thread
public class DataViewModel
{
    private readonly DispatcherQueue _dispatcherQueue;
    public ObservableCollection<LogEntry> Logs { get; } = new();

    public DataViewModel()
    {
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task LoadLogsAsync()
    {
        var entries = await _logService.GetEntriesAsync();
        _dispatcherQueue.TryEnqueue(() =>
        {
            foreach (var entry in entries)
            {
                Logs.Add(entry);  // Safe: runs on UI thread
            }
        });
    }
}
```

```csharp
// GOOD (alternative with ConfigureAwait in WPF):
public async Task LoadLogsAsync()
{
    var entries = await _logService.GetEntriesAsync().ConfigureAwait(false);
    // Return to UI thread via the captured SynchronizationContext
    await App.Current.Dispatcher.InvokeAsync(() =>
    {
        foreach (var entry in entries)
            Logs.Add(entry);
    });
}
```

---

#### UI-BIND-10: Binding path with typo fails silently in XAML
**Severity**: High

A typo in a XAML binding path (e.g., `{Binding Naem}` instead of `{Binding Name}`) produces no compile-time error and no runtime exception. The binding silently fails and the control shows nothing or a default value.

```xml
<!-- BAD: Typo in binding path — silently shows nothing -->
<TextBlock Text="{Binding Naem}" />       <!-- Should be "Name" -->
<TextBlock Text="{Binding UserAdress}" /> <!-- Should be "UserAddress" -->
```

```xml
<!-- GOOD: Use x:Bind (WinUI 3/UWP) for compile-time checked bindings -->
<TextBlock Text="{x:Bind ViewModel.Name}" />         <!-- Compile error if "Name" doesn't exist -->
<TextBlock Text="{x:Bind ViewModel.UserAddress}" />   <!-- Compile error on typo -->

<!-- GOOD (WPF): Enable binding failure diagnostics in debug -->
<!-- In App.xaml.cs or diagnostic config: -->
<!-- PresentationTraceSources.SetTraceLevel(element, PresentationTraceLevel.High) -->

<!-- At minimum, use consistent naming and review binding paths in code review -->
<TextBlock Text="{Binding Name}" />
<TextBlock Text="{Binding UserAddress}" />
```

---

### Control Lifecycle & Visual Tree

#### UI-LIFE-01: Logic in constructor that depends on visual tree
**Severity**: High

During construction, the control is not yet part of the visual tree. Accessing `Parent`, `ActualWidth`, `ActualHeight`, `FindName`, or traversing the visual tree in the constructor will return null or zero.

```csharp
// BAD: Constructor accesses visual tree — always null/zero
public class CustomPanel : Panel
{
    public CustomPanel()
    {
        InitializeComponent();
        var parent = this.Parent;  // Always null — not yet in visual tree
        double width = this.ActualWidth;  // Always 0 — not yet measured
        var sibling = FindName("OtherElement") as UIElement;  // Always null
        UpdateLayout(parent, width);  // Operates on invalid data
    }
}
```

```csharp
// GOOD: Defer visual-tree-dependent logic to Loaded event
public class CustomPanel : Panel
{
    public CustomPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var parent = this.Parent;  // Valid — now in visual tree
        double width = this.ActualWidth;  // Valid — measured
        var sibling = FindName("OtherElement") as UIElement;  // Valid — name scope available
        UpdateLayout(parent, width);
    }
}
```

---

#### UI-LIFE-02: Missing Loaded/Unloaded event pair (resource leak)
**Severity**: High

When event handlers or subscriptions are registered in the `Loaded` event but not unregistered in `Unloaded`, the control leaks references. This is especially problematic for controls in `DataTemplate` that are created and destroyed as items scroll.

```csharp
// BAD: Subscribes in Loaded but never unsubscribes — memory leak
public partial class NotificationBadge : UserControl
{
    public NotificationBadge()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NotificationService.Instance.NotificationReceived += OnNotification;
        // BUG: No Unloaded handler to unsubscribe — badge never collected
    }

    private void OnNotification(object? sender, NotificationEventArgs e)
    {
        BadgeCount.Text = e.Count.ToString();
    }
}
```

```csharp
// GOOD: Paired Loaded/Unloaded handlers
public partial class NotificationBadge : UserControl
{
    public NotificationBadge()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NotificationService.Instance.NotificationReceived += OnNotification;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        NotificationService.Instance.NotificationReceived -= OnNotification;
    }

    private void OnNotification(object? sender, NotificationEventArgs e)
    {
        BadgeCount.Text = e.Count.ToString();
    }
}
```

---

#### UI-LIFE-03: Accessing Parent or FindName before element is in visual tree
**Severity**: Medium

Code that accesses `Parent`, `FindName`, or performs visual tree traversal outside of lifecycle events (e.g., in a property setter or method called during construction) will get null results if the element hasn't been added to the tree yet.

```csharp
// BAD: Property setter traverses visual tree — null if called before Loaded
public string HeaderText
{
    get => _headerText;
    set
    {
        _headerText = value;
        // This fails if set before the control is in the visual tree
        var header = this.FindName("HeaderLabel") as TextBlock;
        header!.Text = value;  // NullReferenceException if called during construction/XAML parsing
    }
}
```

```csharp
// GOOD: Store value and apply when visual tree is ready
private string _pendingHeaderText = "";

public string HeaderText
{
    get => _headerText;
    set
    {
        _headerText = value;
        if (_isLoaded)
            ApplyHeaderText();
        else
            _pendingHeaderText = value;
    }
}

private void OnLoaded(object sender, RoutedEventArgs e)
{
    _isLoaded = true;
    if (!string.IsNullOrEmpty(_pendingHeaderText))
        ApplyHeaderText();
}

private void ApplyHeaderText()
{
    if (FindName("HeaderLabel") is TextBlock header)
        header.Text = _headerText;
}
```

---

#### UI-LIFE-04: Event handler registered in Loaded but not unregistered in Unloaded
**Severity**: High — memory leak

Same principle as UI-LIFE-02, but specifically for event handler registrations on external objects (services, timers, system events). The external object's event delegate holds a strong reference to the control, preventing garbage collection.

```csharp
// BAD: Timer event handler registered but never unregistered
private DispatcherTimer _timer;

private void OnLoaded(object sender, RoutedEventArgs e)
{
    _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    _timer.Tick += OnTimerTick;
    _timer.Start();
    // BUG: No Unloaded handler — timer continues running after control is removed
}
```

```csharp
// GOOD: Paired registration and cleanup
private DispatcherTimer? _timer;

private void OnLoaded(object sender, RoutedEventArgs e)
{
    _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    _timer.Tick += OnTimerTick;
    _timer.Start();
}

private void OnUnloaded(object sender, RoutedEventArgs e)
{
    if (_timer != null)
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _timer = null;
    }
}
```

---

#### UI-LIFE-05: FindAncestor/FindDescendant assuming specific visual tree structure
**Severity**: Medium — fragile

Code that uses `VisualTreeHelper` to walk a specific number of levels up or down the visual tree is fragile. Adding a decorator, panel, or template element breaks the assumed structure.

```csharp
// BAD: Assumes specific visual tree depth — breaks if template changes
private void OnButtonClick(object sender, RoutedEventArgs e)
{
    var button = (Button)sender;
    var parent = VisualTreeHelper.GetParent(button);         // Assumes Grid
    var grandparent = VisualTreeHelper.GetParent(parent);    // Assumes StackPanel
    var listItem = grandparent as ListViewItem;              // Fragile assumption
}
```

```csharp
// GOOD: Walk the tree with type-based search
private void OnButtonClick(object sender, RoutedEventArgs e)
{
    var button = (Button)sender;
    var listItem = FindAncestor<ListViewItem>(button);
    if (listItem != null)
    {
        // Work with listItem
    }
}

private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
{
    var parent = VisualTreeHelper.GetParent(child);
    while (parent != null)
    {
        if (parent is T target)
            return target;
        parent = VisualTreeHelper.GetParent(parent);
    }
    return null;
}
```

---

#### UI-LIFE-06: Style/Template applied before control fully initialized
**Severity**: High

Accessing named template parts (via `GetTemplateChild`) outside of `OnApplyTemplate` can return null because the template hasn't been applied yet. This commonly occurs when property changed callbacks try to update template parts during initialization.

```csharp
// BAD: Accessing template parts in PropertyChangedCallback before template applied
private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
{
    var slider = (CustomSlider)d;
    // Template may not be applied yet — GetTemplateChild returns null
    var thumb = slider.GetTemplateChild("PART_Thumb") as Thumb;
    thumb!.Width = (double)e.NewValue;  // NullReferenceException during initialization
}
```

```csharp
// GOOD: Cache template parts in OnApplyTemplate, null-check in callbacks
private Thumb? _thumb;

public override void OnApplyTemplate()
{
    base.OnApplyTemplate();
    _thumb = GetTemplateChild("PART_Thumb") as Thumb;
    UpdateThumbWidth();  // Apply current value to template part
}

private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
{
    var slider = (CustomSlider)d;
    slider.UpdateThumbWidth();
}

private void UpdateThumbWidth()
{
    if (_thumb != null)  // Safe: null before OnApplyTemplate
        _thumb.Width = Value;
}
```

---

#### UI-LIFE-07: Disposing managed resources in wrong lifecycle event
**Severity**: Medium

Disposing resources in the destructor/finalizer instead of in `Unloaded` or `IDisposable.Dispose` leads to unpredictable cleanup timing. UI controls should clean up in `Unloaded` for visual-tree-related resources and in `Dispose` for non-visual resources.

```csharp
// BAD: Resource cleanup in finalizer — unpredictable timing, may access disposed UI objects
public class CameraPreview : UserControl
{
    private MediaCapture? _capture;

    ~CameraPreview()
    {
        _capture?.Dispose();  // Finalizer runs on GC thread — wrong thread for UI resources
    }
}
```

```csharp
// GOOD: Cleanup in Unloaded for visual-tree-tied resources
public class CameraPreview : UserControl
{
    private MediaCapture? _capture;

    public CameraPreview()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _capture = new MediaCapture();
        await _capture.InitializeAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _capture?.Dispose();
        _capture = null;
    }
}
```

---

#### UI-LIFE-08: Control assumes single DataContext assignment
**Severity**: High — breaks in reuse/recycling scenarios

Controls used in `ListView`, `GridView`, or other virtualizing containers are recycled. The `DataContext` changes as items scroll, so any one-time initialization based on `DataContext` in the constructor or `Loaded` event will become stale.

```csharp
// BAD: One-time setup in Loaded — stale when DataContext changes on recycling
public partial class ProductCard : UserControl
{
    public ProductCard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var product = DataContext as Product;
        // This only runs once — when the card is recycled with a new Product,
        // the image is never updated
        LoadProductImage(product?.ImageUrl);
    }
}
```

```csharp
// GOOD: React to DataContext changes via DataContextChanged event
public partial class ProductCard : UserControl
{
    public ProductCard()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (args.NewValue is Product product)
        {
            LoadProductImage(product.ImageUrl);
        }
    }
}
```

---

### Threading & Dispatcher

#### UI-THREAD-01: Property setter called from background thread on DependencyObject
**Severity**: Critical

DependencyObjects have thread affinity and can only be accessed from the thread that created them (the UI thread). Setting a DependencyProperty value from a background thread throws an `InvalidOperationException`.

```csharp
// BAD: Background thread sets DependencyProperty — throws InvalidOperationException
public partial class DownloadControl : UserControl
{
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(DownloadControl));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public void StartDownload()
    {
        Task.Run(async () =>
        {
            for (int i = 0; i <= 100; i++)
            {
                await Task.Delay(100);
                Progress = i;  // CRASH: Cross-thread access to DependencyObject
            }
        });
    }
}
```

```csharp
// GOOD: Marshal back to UI thread for DependencyProperty access
public void StartDownload()
{
    var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

    Task.Run(async () =>
    {
        for (int i = 0; i <= 100; i++)
        {
            await Task.Delay(100);
            dispatcherQueue.TryEnqueue(() =>
            {
                Progress = i;  // Safe: runs on UI thread
            });
        }
    });
}
```

---

#### UI-THREAD-02: Collection modified on background thread bound to UI
**Severity**: Critical

Modifying an `ObservableCollection` that is bound to a UI control from a background thread crashes with a cross-thread violation. This is the collection-level variant of UI-THREAD-01.

```csharp
// BAD: Background thread modifies UI-bound collection
public ObservableCollection<FileInfo> Files { get; } = new();

public void ScanDirectory(string path)
{
    Task.Run(() =>
    {
        foreach (var file in Directory.EnumerateFiles(path))
        {
            Files.Add(new FileInfo(file));  // CRASH: cross-thread collection modification
        }
    });
}
```

```csharp
// GOOD: Batch results and add on UI thread
public ObservableCollection<FileInfo> Files { get; } = new();

public async Task ScanDirectoryAsync(string path)
{
    var files = await Task.Run(() =>
        Directory.EnumerateFiles(path).Select(f => new FileInfo(f)).ToList());

    // Back on UI thread (if called from UI context with no ConfigureAwait(false))
    foreach (var file in files)
        Files.Add(file);
}
```

---

#### UI-THREAD-03: Missing DispatcherQueue.TryEnqueue for cross-thread UI update
**Severity**: Critical

In WinUI 3, `DispatcherQueue.TryEnqueue` is the correct way to marshal work to the UI thread. Using `Dispatcher.RunAsync` (UWP) or `Dispatcher.Invoke` (WPF) in WinUI 3 code compiles but may not work correctly.

```csharp
// BAD (WinUI 3): Using deprecated or wrong dispatcher pattern
private async void OnDataReceived(object? sender, DataEventArgs e)
{
    // This runs on a background thread (event from network layer)
    StatusText.Text = e.Message;  // CRASH: cross-thread UI access
}
```

```csharp
// GOOD (WinUI 3): Use DispatcherQueue.TryEnqueue
private readonly DispatcherQueue _dispatcherQueue;

public MyPage()
{
    InitializeComponent();
    _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
}

private void OnDataReceived(object? sender, DataEventArgs e)
{
    _dispatcherQueue.TryEnqueue(() =>
    {
        StatusText.Text = e.Message;  // Safe: runs on UI thread
    });
}
```

---

#### UI-THREAD-04: Dispatcher.Invoke (synchronous) instead of BeginInvoke/TryEnqueue — blocks calling thread
**Severity**: Medium

Synchronous `Dispatcher.Invoke` blocks the calling thread until the UI thread processes the work item. If the calling thread holds a lock, and the UI thread is waiting on that lock, this causes a deadlock.

```csharp
// BAD: Synchronous Invoke — blocks background thread, risk of deadlock
private void ProcessData()
{
    lock (_dataLock)
    {
        var result = ComputeExpensiveResult();
        // Blocks here until UI thread processes — but UI thread might be waiting on _dataLock
        Application.Current.Dispatcher.Invoke(() =>
        {
            ResultLabel.Text = result.ToString();
        });
    }
}
```

```csharp
// GOOD: Asynchronous dispatch — no blocking, no deadlock risk
private void ProcessData()
{
    string result;
    lock (_dataLock)
    {
        result = ComputeExpensiveResult().ToString();
    }
    // Non-blocking: queues work and returns immediately
    Application.Current.Dispatcher.BeginInvoke(() =>
    {
        ResultLabel.Text = result;
    });
}
```

---

#### UI-THREAD-05: DispatcherPriority too high — starves input processing
**Severity**: Medium

Dispatching work at `DispatcherPriority.Send` or `DispatcherPriority.Normal` in a tight loop starves input processing, making the UI unresponsive even though updates are happening on the UI thread.

```csharp
// BAD: High-priority dispatch in loop — UI appears frozen (input events starved)
foreach (var item in largeDataSet)
{
    Application.Current.Dispatcher.Invoke(
        () => { Items.Add(item); },
        DispatcherPriority.Normal);  // Each dispatch preempts pending input events
}
```

```csharp
// GOOD: Use Background priority or batch updates
// Option 1: Lower priority allows input processing between items
foreach (var item in largeDataSet)
{
    await Application.Current.Dispatcher.InvokeAsync(
        () => { Items.Add(item); },
        DispatcherPriority.Background);
}

// Option 2: Batch updates (preferred for large datasets)
var batch = largeDataSet.ToList();
Application.Current.Dispatcher.BeginInvoke(() =>
{
    foreach (var item in batch)
        Items.Add(item);
}, DispatcherPriority.Background);
```

---

#### UI-THREAD-06: Async command handler not marshaling back to UI thread for result
**Severity**: High

When an `ICommand.Execute` calls an async method that uses `ConfigureAwait(false)`, the continuation runs on a thread pool thread. Any UI updates in the continuation crash.

```csharp
// BAD: ConfigureAwait(false) loses UI context — continuation crashes on UI access
public async void Execute(object? parameter)
{
    IsLoading = true;  // OK: on UI thread
    var data = await _dataService.LoadAsync().ConfigureAwait(false);
    // Now on thread pool thread!
    IsLoading = false;  // May crash: PropertyChanged raised on non-UI thread
    Items.Clear();      // CRASH: ObservableCollection on non-UI thread
    foreach (var item in data)
        Items.Add(item);
}
```

```csharp
// GOOD: Don't use ConfigureAwait(false) in UI command handlers
public async void Execute(object? parameter)
{
    IsLoading = true;
    var data = await _dataService.LoadAsync();  // No ConfigureAwait(false) — returns to UI thread
    IsLoading = false;  // Safe: on UI thread
    Items.Clear();
    foreach (var item in data)
        Items.Add(item);
}
```

---

#### UI-THREAD-07: Timer callback updating UI without dispatcher check
**Severity**: High

`System.Threading.Timer` and `System.Timers.Timer` fire callbacks on thread pool threads, not the UI thread. Updating UI elements or bound properties in these callbacks causes cross-thread exceptions.

```csharp
// BAD: System.Threading.Timer fires on thread pool — UI access crashes
private Timer _refreshTimer;

public void StartAutoRefresh()
{
    _refreshTimer = new Timer(_ =>
    {
        StatusText.Text = DateTime.Now.ToString();  // CRASH: thread pool, not UI thread
        RefreshData();  // If this modifies ObservableCollection — also crashes
    }, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
}
```

```csharp
// GOOD (WinUI 3): Use DispatcherTimer or marshal via DispatcherQueue
// Option 1: DispatcherTimer fires on UI thread automatically
private DispatcherTimer _refreshTimer;

public void StartAutoRefresh()
{
    _refreshTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromSeconds(5)
    };
    _refreshTimer.Tick += (s, e) =>
    {
        StatusText.Text = DateTime.Now.ToString();  // Safe: DispatcherTimer fires on UI thread
        RefreshData();
    };
    _refreshTimer.Start();
}

// Option 2: If you need System.Threading.Timer precision, marshal UI updates
private Timer _refreshTimer;
private DispatcherQueue _dispatcherQueue;

public void StartAutoRefresh()
{
    _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    _refreshTimer = new Timer(_ =>
    {
        var data = FetchLatestData();  // OK on thread pool
        _dispatcherQueue.TryEnqueue(() =>
        {
            StatusText.Text = data.Timestamp.ToString();  // Marshaled to UI thread
        });
    }, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
}
```

---

#### UI-THREAD-08: Background task accessing UI-bound ObservableCollection
**Severity**: Critical

This is a consolidation of UI-THREAD-02 and UI-BIND-09. Any `Task.Run`, `Task.Factory.StartNew`, thread pool work item, or event handler from a non-UI source that modifies a UI-bound `ObservableCollection` will crash.

```csharp
// BAD: Parallel processing adds to UI-bound collection
public ObservableCollection<AnalysisResult> Results { get; } = new();

public async Task RunAnalysisAsync(IEnumerable<DataPoint> data)
{
    await Parallel.ForEachAsync(data, async (point, ct) =>
    {
        var result = await AnalyzeAsync(point, ct);
        Results.Add(result);  // CRASH: Parallel tasks are on thread pool threads
    });
}
```

```csharp
// GOOD: Collect results, then batch-update on UI thread
public ObservableCollection<AnalysisResult> Results { get; } = new();

public async Task RunAnalysisAsync(IEnumerable<DataPoint> data)
{
    var results = new ConcurrentBag<AnalysisResult>();

    await Parallel.ForEachAsync(data, async (point, ct) =>
    {
        var result = await AnalyzeAsync(point, ct);
        results.Add(result);  // ConcurrentBag is thread-safe, not UI-bound
    });

    // Back on UI thread (no ConfigureAwait(false) above)
    foreach (var result in results)
        Results.Add(result);  // Safe: sequential, on UI thread
}
```

---

### Resource & Style Patterns

#### UI-RES-01: StaticResource referencing key defined later in XAML
**Severity**: High — runtime exception

`StaticResource` is resolved at XAML parse time in a single forward pass. If the resource key is defined later in the same file or dictionary, the lookup fails with a `StaticResourceExtension` exception at runtime.

```xml
<!-- BAD: Forward reference — StaticResource "AccentBrush" not yet defined at parse time -->
<Page>
    <Page.Resources>
        <Style x:Key="HighlightStyle" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{StaticResource AccentBrush}" />
            <!-- CRASH: AccentBrush defined below, not yet parsed -->
        </Style>

        <SolidColorBrush x:Key="AccentBrush" Color="#0078D4" />
    </Page.Resources>
</Page>
```

```xml
<!-- GOOD: Define resource before it's referenced -->
<Page>
    <Page.Resources>
        <SolidColorBrush x:Key="AccentBrush" Color="#0078D4" />

        <Style x:Key="HighlightStyle" TargetType="TextBlock">
            <Setter Property="Foreground" Value="{StaticResource AccentBrush}" />
            <!-- OK: AccentBrush defined above -->
        </Style>
    </Page.Resources>
</Page>

<!-- GOOD (alternative): Use DynamicResource if order can't be controlled (WPF) -->
<Setter Property="Foreground" Value="{DynamicResource AccentBrush}" />
```

---

#### UI-RES-02: DynamicResource where StaticResource appropriate
**Severity**: Low — unnecessary overhead

`DynamicResource` sets up a runtime listener for resource changes, adding overhead. If the resource never changes at runtime (e.g., fixed brushes, converters, data templates), `StaticResource` is more efficient.

```xml
<!-- BAD: DynamicResource for a fixed converter — unnecessary overhead -->
<TextBlock Text="{Binding Date, Converter={DynamicResource DateConverter}}" />
<!-- DateConverter never changes at runtime -->
```

```xml
<!-- GOOD: StaticResource for fixed resources — resolved once at parse time -->
<TextBlock Text="{Binding Date, Converter={StaticResource DateConverter}}" />
```

---

#### UI-RES-03: ResourceDictionary.MergedDictionaries causing duplicate resource loading
**Severity**: Medium

When multiple pages or controls each merge the same `ResourceDictionary` file, each merge creates a separate copy of all resources in memory. For large resource dictionaries, this wastes significant memory.

```xml
<!-- BAD: Every page merges the same dictionary — duplicated per page instance -->
<!-- PageA.xaml -->
<Page.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Styles/CommonStyles.xaml" />  <!-- Copy 1 -->
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Page.Resources>

<!-- PageB.xaml -->
<Page.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Styles/CommonStyles.xaml" />  <!-- Copy 2 -->
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Page.Resources>
```

```xml
<!-- GOOD: Merge shared dictionaries once at the App level -->
<!-- App.xaml -->
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Styles/CommonStyles.xaml" />  <!-- Single copy -->
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>

<!-- Pages automatically inherit App-level resources — no per-page merge needed -->
```

---

#### UI-RES-04: Implicit Style in wrong scope
**Severity**: Medium — applies to unintended elements

An implicit style (no `x:Key`) applies to all elements of the target type within its scope. Placing it at the wrong level (e.g., App-level when only one page needs it) affects all instances of that type throughout the app.

```xml
<!-- BAD: App-level implicit style affects ALL TextBlocks in the entire app -->
<!-- App.xaml -->
<Application.Resources>
    <Style TargetType="TextBlock">
        <Setter Property="FontSize" Value="24" />
        <Setter Property="Foreground" Value="Red" />
    </Style>
</Application.Resources>
<!-- Every TextBlock in every page is now 24pt Red — including system UI elements in controls -->
```

```xml
<!-- GOOD: Scope implicit style to the page/control that needs it -->
<!-- SpecialPage.xaml -->
<Page.Resources>
    <Style TargetType="TextBlock">
        <Setter Property="FontSize" Value="24" />
        <Setter Property="Foreground" Value="Red" />
    </Style>
</Page.Resources>
<!-- Only TextBlocks on this page are affected -->

<!-- GOOD (alternative): Use explicit keyed style and apply deliberately -->
<Application.Resources>
    <Style x:Key="LargeRedText" TargetType="TextBlock">
        <Setter Property="FontSize" Value="24" />
        <Setter Property="Foreground" Value="Red" />
    </Style>
</Application.Resources>

<TextBlock Style="{StaticResource LargeRedText}" Text="Important!" />
```

---

#### UI-RES-05: Missing x:Key on non-Style resource
**Severity**: Medium

Resources in a `ResourceDictionary` other than `Style` with `TargetType` require an `x:Key`. Omitting it causes a XAML compilation error (for non-Style types) or implicit behavior that may not be intended.

```xml
<!-- BAD: Missing x:Key on converter — XAML compilation error -->
<Page.Resources>
    <local:BoolToVisibilityConverter />  <!-- Error: x:Key required for non-Style -->
</Page.Resources>
```

```xml
<!-- GOOD: Explicit x:Key for all non-implicit-Style resources -->
<Page.Resources>
    <local:BoolToVisibilityConverter x:Key="BoolToVisibility" />
    <SolidColorBrush x:Key="PrimaryBrush" Color="#0078D4" />
    <sys:Double x:Key="DefaultMargin">8</sys:Double>
</Page.Resources>

<TextBlock Visibility="{Binding IsVisible, Converter={StaticResource BoolToVisibility}}" />
```

---

#### UI-RES-06: Theme resource not responding to theme changes
**Severity**: Medium — missing ThemeResource markup

In WinUI 3 and UWP, `StaticResource` for theme-dependent values (colors, brushes) does not update when the user switches between Light, Dark, and HighContrast themes. `ThemeResource` must be used instead.

```xml
<!-- BAD (WinUI 3): StaticResource for theme color — doesn't update on theme change -->
<TextBlock Foreground="{StaticResource SystemControlForegroundBaseHighBrush}" />
<!-- If user switches from Light to Dark theme, this TextBlock keeps the Light theme color -->
```

```xml
<!-- GOOD (WinUI 3): ThemeResource updates automatically on theme change -->
<TextBlock Foreground="{ThemeResource SystemControlForegroundBaseHighBrush}" />
<!-- Automatically picks up the correct brush for the current theme -->

<!-- For custom theme-aware resources, define per-theme values: -->
<Page.Resources>
    <ResourceDictionary>
        <ResourceDictionary.ThemeDictionaries>
            <ResourceDictionary x:Key="Light">
                <SolidColorBrush x:Key="CardBackground" Color="#FFFFFF" />
            </ResourceDictionary>
            <ResourceDictionary x:Key="Dark">
                <SolidColorBrush x:Key="CardBackground" Color="#1E1E1E" />
            </ResourceDictionary>
        </ResourceDictionary.ThemeDictionaries>
    </ResourceDictionary>
</Page.Resources>

<Grid Background="{ThemeResource CardBackground}" />
```

---

## UI Framework Correctness Checklist

Use this checklist when reviewing any C# XAML-based UI code (WPF, WinUI 3, MAUI).

### DependencyProperty Registration
- [ ] Owner type in `Register`/`RegisterAttached` matches the declaring class
- [ ] Name string matches CLR property name (prefer `nameof()`)
- [ ] CLR wrapper does **only** `GetValue`/`SetValue` (no side effects)
- [ ] Default value is **not** a mutable reference type (use per-instance creation)
- [ ] Attached properties use `RegisterAttached`, not `Register`
- [ ] Read-only properties use `DependencyPropertyKey`
- [ ] `CoerceValueCallback` never throws (clamps instead)
- [ ] Metadata flags match property behavior (`AffectsRender` vs `AffectsMeasure`)
- [ ] `ValidateValueCallback` present for range-constrained properties

### Data Binding & MVVM
- [ ] All bound ViewModel classes implement `INotifyPropertyChanged`
- [ ] `PropertyChanged` uses `nameof()` — no string literals
- [ ] Bound properties are `public`
- [ ] `TwoWay` bindings have writable setters
- [ ] `ICommand.CanExecuteChanged` raised when dependent state changes
- [ ] No strong-reference leaks from long-lived source bindings
- [ ] XAML binding paths spell-checked against ViewModel properties

### Control Lifecycle
- [ ] No visual-tree access in constructors (defer to `Loaded`)
- [ ] Every `Loaded` registration has a matching `Unloaded` unregistration
- [ ] Template parts accessed only in/after `OnApplyTemplate`
- [ ] Controls used in virtualizing containers handle `DataContext` changes
- [ ] Resources disposed in appropriate lifecycle event (not finalizer)

### Threading
- [ ] All UI element access is on the UI thread
- [ ] `ObservableCollection` modifications are on the UI thread
- [ ] `DispatcherQueue.TryEnqueue` used for cross-thread UI updates (WinUI 3)
- [ ] No `Dispatcher.Invoke` (synchronous) where `BeginInvoke`/`TryEnqueue` (async) works
- [ ] Timer callbacks marshal to UI thread before accessing UI
- [ ] Async command handlers don't use `ConfigureAwait(false)` before UI updates

### Resources & Styles
- [ ] `StaticResource` references point to previously-defined keys (no forward references)
- [ ] `DynamicResource` used only when runtime resource changes are needed
- [ ] Shared `ResourceDictionary` files merged at appropriate scope (App vs Page)
- [ ] Implicit styles scoped to intended elements (not accidentally global)
- [ ] Theme-dependent resources use `ThemeResource` (WinUI 3/UWP), not `StaticResource`

## References

1. WPF DependencyProperty documentation — Microsoft Learn: "Custom dependency properties"
2. WinUI 3 DependencyProperty documentation — Microsoft Learn: "Dependency properties overview"
3. .NET MAUI BindableProperty documentation — Microsoft Learn: "Bindable properties"
4. XAML specification — resource resolution order and markup extension behavior
5. Framework Design Guidelines (3rd ed.) — DependencyProperty wrapper rules (Section 9.4)
