using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Reactor.Markdown;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Text;
using Windows.UI.Text;
using MenuFlyoutItemBase = Microsoft.UI.Reactor.Core.MenuFlyoutItemBase;

namespace Microsoft.UI.Reactor;

// AI-HINT: This is the main DSL entry point. All Reactor UI is built via:
//   using static Microsoft.UI.Reactor.Factories;
// Factory methods return Element records (virtual DOM), never real WinUI controls.
// Organization: Text → Buttons → Input → Layout → Navigation → Dialogs → Data → Media → Markdown.
// Layout helpers: VStack/HStack/Grid/Canvas/RelativePanel produce container elements.
// FlexRow/FlexColumn are Yoga-based flexbox containers (see FlexPanel.cs).

/// <summary>
/// Static factory methods that form the Reactor DSL.
/// Import with: using static Microsoft.UI.Reactor.Factories;
///
/// This gives you a clean, declarative syntax:
///   VStack(
///       TextBlock("Hello").Bold(),
///       Button("Click me", () => setCount(count + 1)),
///       count > 5 ? TextBlock("Wow!") : null
///   )
/// </summary>
public static partial class Factories
{
    // ── Localization ──────────────────────────────────────────────────

    public static ComponentElement<Localization.LocaleProviderElement> LocaleProvider(
        string locale, Element child,
        Localization.IStringResourceProvider? resourceProvider = null,
        string defaultLocale = "en-US",
        bool pseudoLocalize = false) =>
        Component<Localization.LocaleProviderComponent, Localization.LocaleProviderElement>(
            new Localization.LocaleProviderElement(locale, child, resourceProvider, defaultLocale, pseudoLocalize));

    // ── Text ────────────────────────────────────────────────────────

    public static TextBlockElement TextBlock(string content) => new(content);

    /// <summary>
    /// Creates a heading-styled <see cref="TextBlockElement"/> (28px, bold,
    /// automation heading level 1).
    /// </summary>
    /// <remarks>
    /// Reactor-original convenience wrapper — there is no WinUI control named
    /// <c>Heading</c>. Sized for the WinUI Title type-ramp slot, with the
    /// accessibility heading level set so screen readers announce it as a
    /// landmark. Prefer this over hand-styled <see cref="TextBlock(string)"/>
    /// for page / section titles. (spec 039 §0.3)
    /// </remarks>
    public static TextBlockElement Heading(string content) =>
        new(content) { FontSize = 28, Weight = new global::Windows.UI.Text.FontWeight(700),
            Modifiers = new Core.ElementModifiers
            {
                HeadingLevel = Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1
            } };

    /// <summary>
    /// Creates a sub-heading styled <see cref="TextBlockElement"/> (20px,
    /// semi-bold, automation heading level 2).
    /// </summary>
    /// <remarks>
    /// Reactor-original convenience wrapper — there is no WinUI control named
    /// <c>SubHeading</c>. Pairs with <see cref="Heading(string)"/> for the
    /// secondary section level; sized for the WinUI Subtitle type-ramp slot.
    /// (spec 039 §0.3)
    /// </remarks>
    public static TextBlockElement SubHeading(string content) =>
        new(content) { FontSize = 20, Weight = new global::Windows.UI.Text.FontWeight(600),
            Modifiers = new Core.ElementModifiers
            {
                HeadingLevel = Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2
            } };

    /// <summary>
    /// Creates a caption-styled <see cref="TextBlockElement"/> (12px).
    /// </summary>
    /// <remarks>
    /// Reactor-original convenience wrapper — there is no WinUI control named
    /// <c>Caption</c>. Sized for the WinUI Caption type-ramp slot; use for
    /// secondary metadata (timestamps, helper text, hints) below primary copy.
    /// (spec 039 §0.3)
    /// </remarks>
    public static TextBlockElement Caption(string content) =>
        new(content) { FontSize = 12 };

    /// <summary>
    /// Creates a <see cref="RichTextBlockElement"/> wrapping a single string of
    /// plain text. Use the <see cref="RichTextBlock(RichTextParagraph[])"/>
    /// overload to compose runs, hyperlinks, and inline formatting.
    /// </summary>
    /// <remarks>
    /// Named for parity with WinUI's <c>Microsoft.UI.Xaml.Controls.RichTextBlock</c>.
    /// (spec 039 §1.3 / §14 #8)
    /// </remarks>
    public static RichTextBlockElement RichTextBlock(string text) => new(text);

    /// <summary>
    /// Deprecated forwarding alias for <see cref="RichTextBlock(string)"/>.
    /// </summary>
    [global::System.Obsolete(
        "Renamed to RichTextBlock for parity with WinUI's Microsoft.UI.Xaml.Controls.RichTextBlock. " +
        "RichText will be removed in the next minor release. (spec 039 §1.3 / §14 #8)",
        error: false)]
    public static RichTextBlockElement RichText(string text) => RichTextBlock(text);

    public static RichEditBoxElement RichEditBox(string text = "", Action<string>? onTextChanged = null) =>
        new(text) { OnTextChanged = onTextChanged };

    // ── Buttons ─────────────────────────────────────────────────────

    public static ButtonElement Button(string label, Action? onClick = null) =>
        new(label, onClick);

    public static ButtonElement Button(Element content, Action? onClick = null) =>
        new("", onClick) { ContentElement = content };

    /// <summary>
    /// Creates a Button driven by a Command. Maps Label → Content, Execute → Click,
    /// IsEnabled → IsEnabled. Description / Accelerator / AccessKey are wired via
    /// a Setter so per-site overrides win via the normal modifier ordering — e.g.
    /// <c>Button(saveCommand).IsEnabled(canSave)</c> or
    /// <c>.Set(b =&gt; b.FlowDirection = FlowDirection.RightToLeft)</c>.
    /// </summary>
    public static ButtonElement Button(Core.Command command) =>
        new ButtonElement(command.Label, () => Core.CommandBindings.Invoke(command))
        {
            IsEnabled = command.IsEnabled,
            Setters = [b => Core.CommandBindings.ApplyButtonBaseCommon(b, command)],
        };

    public static HyperlinkButtonElement HyperlinkButton(string content, Uri? navigateUri = null, Action? onClick = null) =>
        new(content, navigateUri, onClick);

    /// <summary>
    /// Creates a HyperlinkButton driven by a Command. Maps Label → Content, Execute →
    /// Click. For external navigation, chain <see cref="ElementExtensions.NavigateUri(HyperlinkButtonElement, Uri)"/>:
    /// <c>HyperlinkButton(cmd).NavigateUri(new Uri("https://..."))</c>.
    /// </summary>
    public static HyperlinkButtonElement HyperlinkButton(Core.Command command) =>
        new HyperlinkButtonElement(command.Label, null, () => Core.CommandBindings.Invoke(command))
        {
            Setters = [b => Core.CommandBindings.ApplyButtonBaseCommon(b, command)],
        };

    public static RepeatButtonElement RepeatButton(string label, Action? onClick = null) =>
        new(label, onClick);

    /// <summary>Creates a RepeatButton driven by a Command. Click auto-repeats while held.</summary>
    public static RepeatButtonElement RepeatButton(Core.Command command) =>
        new RepeatButtonElement(command.Label, () => Core.CommandBindings.Invoke(command))
        {
            Setters = [b => Core.CommandBindings.ApplyButtonBaseCommon(b, command)],
        };

    public static ToggleButtonElement ToggleButton(string label, bool isChecked = false, Action<bool>? onIsCheckedChanged = null) =>
        new(label, isChecked, onIsCheckedChanged);

    /// <summary>
    /// Creates a ToggleButton driven by a Command. The command fires on each toggle
    /// (both check and uncheck) — per the spec's "Option A" semantics. Use the
    /// <c>isChecked</c> parameter to seed the initial state.
    /// </summary>
    public static ToggleButtonElement ToggleButton(Core.Command command, bool isChecked = false) =>
        new ToggleButtonElement(command.Label, isChecked, _ => Core.CommandBindings.Invoke(command))
        {
            Setters = [b => Core.CommandBindings.ApplyButtonBaseCommon(b, command)],
        };

    /// <summary>
    /// Three-state toggle button (true → false → null → ...). Matches the
    /// established <c>ThreeStateCheckBox</c> factory pattern from spec 039 §2.4.
    /// </summary>
    public static ToggleButtonElement ThreeStateToggleButton(string label, bool? checkedState = null, Action<bool?>? onCheckedStateChanged = null) =>
        new(label, checkedState == true) { IsThreeState = true, CheckedState = checkedState, OnCheckedStateChanged = onCheckedStateChanged };

    public static DropDownButtonElement DropDownButton(string label, Element? flyout = null) =>
        new(label, flyout);

    public static SplitButtonElement SplitButton(string label, Action? onClick = null, Element? flyout = null) =>
        new(label, onClick, flyout);

    /// <summary>
    /// Creates a SplitButton driven by a Command for the primary action. The flyout
    /// (dropdown portion) is independent and supplied separately.
    /// </summary>
    public static SplitButtonElement SplitButton(Core.Command command, Element? flyout = null) =>
        new SplitButtonElement(command.Label, () => Core.CommandBindings.Invoke(command), flyout)
        {
            Setters = [b => Core.CommandBindings.ApplyButtonBaseCommon(b, command)],
        };

    public static ToggleSplitButtonElement ToggleSplitButton(string label, bool isChecked = false, Action<bool>? onIsCheckedChanged = null, Element? flyout = null) =>
        new(label, isChecked, onIsCheckedChanged, flyout);

    /// <summary>Creates a ToggleSplitButton driven by a Command (fires on each toggle).</summary>
    public static ToggleSplitButtonElement ToggleSplitButton(Core.Command command, bool isChecked = false, Element? flyout = null) =>
        new ToggleSplitButtonElement(command.Label, isChecked, _ => Core.CommandBindings.Invoke(command), flyout)
        {
            Setters = [b => Core.CommandBindings.ApplyButtonBaseCommon(b, command)],
        };

    // ── Input controls ──────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="TextFieldElement"/> — the Reactor name for WinUI's
    /// <c>Microsoft.UI.Xaml.Controls.TextBox</c>.
    /// </summary>
    /// <remarks>
    /// Reactor uses <c>TextField</c> (HTML / iOS / Android / SwiftUI vocabulary)
    /// rather than WinUI's legacy WinForms-era <c>TextBox</c>. The deviation
    /// is deliberate — see spec 039 §3.1 / §16 for the impact analysis and
    /// §16.5 for the decision (keep as-is). The reconciler still maps this to
    /// a real WinUI <c>TextBox</c> internally. (spec 039 §3.1 / §16)
    /// </remarks>
    public static TextFieldElement TextField(string value, Action<string>? onChanged = null, string? placeholder = null, string? header = null) =>
        new(value, onChanged, placeholder) { Header = header };

    public static PasswordBoxElement PasswordBox(string password, Action<string>? onPasswordChanged = null, string? placeholderText = null) =>
        new(password, onPasswordChanged, placeholderText);

    public static NumberBoxElement NumberBox(double value, Action<double>? onValueChanged = null, string? header = null) =>
        new(value, onValueChanged, header);

    public static AutoSuggestBoxElement AutoSuggestBox(string text, Action<string>? onTextChanged = null, Action<string>? onQuerySubmitted = null) =>
        new(text, onTextChanged, onQuerySubmitted);

    public static CheckBoxElement CheckBox(bool isChecked, Action<bool>? onIsCheckedChanged = null, string? label = null) =>
        new(isChecked, onIsCheckedChanged, label);

    public static CheckBoxElement ThreeStateCheckBox(bool? checkedState, Action<bool?>? onCheckedStateChanged = null, string? label = null) =>
        new(checkedState == true, Label: label) { IsThreeState = true, CheckedState = checkedState, OnCheckedStateChanged = onCheckedStateChanged };

    public static RadioButtonElement RadioButton(string label, bool isChecked = false, Action<bool>? onIsCheckedChanged = null, string? groupName = null) =>
        new(label, isChecked, onIsCheckedChanged, groupName);

    public static RadioButtonsElement RadioButtons(string[] items, int selectedIndex = -1, Action<int>? onSelectedIndexChanged = null) =>
        new(items, selectedIndex, onSelectedIndexChanged);

    public static ComboBoxElement ComboBox(string[] items, int selectedIndex = -1, Action<int>? onSelectedIndexChanged = null) =>
        new(items, selectedIndex, onSelectedIndexChanged);

    public static ComboBoxElement ComboBox(Element[] itemElements, int selectedIndex, Action<int>? onSelectedIndexChanged) =>
        new([], selectedIndex, onSelectedIndexChanged) { ItemElements = itemElements };

    public static SliderElement Slider(double value, double min = 0, double max = 100, Action<double>? onValueChanged = null) =>
        new(value, min, max, onValueChanged);

    public static ToggleSwitchElement ToggleSwitch(bool isOn, Action<bool>? onIsOnChanged = null, string? onContent = null, string? offContent = null, string? header = null) =>
        new(isOn, onIsOnChanged, onContent, offContent) { Header = header };

    public static RatingControlElement RatingControl(double value = 0, Action<double>? onValueChanged = null) =>
        new(value, onValueChanged);

    public static ColorPickerElement ColorPicker(global::Windows.UI.Color color, Action<global::Windows.UI.Color>? onColorChanged = null) =>
        new(color, onColorChanged);

    // ── Date / Time ─────────────────────────────────────────────────

    public static CalendarDatePickerElement CalendarDatePicker(DateTimeOffset? date = null, Action<DateTimeOffset?>? onDateChanged = null) =>
        new(date, onDateChanged);

    public static DatePickerElement DatePicker(DateTimeOffset date, Action<DateTimeOffset>? onDateChanged = null) =>
        new(date, onDateChanged);

    public static TimePickerElement TimePicker(TimeSpan time, Action<TimeSpan>? onTimeChanged = null) =>
        new(time, onTimeChanged);

    // ── Progress ────────────────────────────────────────────────────

    /// <summary>
    /// Creates a determinate <see cref="ProgressElement"/> at the given value.
    /// </summary>
    /// <remarks>
    /// The element reconciles to a WinUI <c>ProgressBar</c>. The Reactor name
    /// <c>Progress</c> is the short, intent-naming spelling — the WinUI name
    /// includes the visual shape (<c>Bar</c>) the way agents reach for a
    /// rendering primitive; Reactor calls it by what it does. The
    /// <c>ProgressBar</c> alias is preserved for callers reaching for the
    /// WinUI name. <see cref="ProgressRing(double)"/> is the circular variant.
    /// (spec 039 §5 / §16)
    /// </remarks>
    public static ProgressElement Progress(double value) => new(value);

    /// <summary>
    /// Creates an indeterminate <see cref="ProgressElement"/> (animated bar with
    /// no value). The reconciler maps this to <c>ProgressBar.IsIndeterminate</c>.
    /// </summary>
    /// <remarks>
    /// Reactor-original convenience for the indeterminate-bar case; see
    /// <see cref="Progress(double)"/> for the naming rationale. (spec 039 §5 / §16)
    /// </remarks>
    public static ProgressElement ProgressIndeterminate() => new(null);

    /// <summary>
    /// Deprecated forwarding alias for <see cref="Progress(double)"/>.
    /// </summary>
    [global::System.Obsolete(
        "Use Progress(double) for parity with Reactor's intent-naming convention. " +
        "ProgressBar(double) will be removed in the next minor release. (spec 039 §5 / §16)",
        error: false)]
    public static ProgressElement ProgressBar(double value) => Progress(value);

    /// <summary>
    /// Deprecated forwarding alias for <see cref="ProgressIndeterminate"/>.
    /// </summary>
    [global::System.Obsolete(
        "Use ProgressIndeterminate() for parity with Reactor's intent-naming convention. " +
        "ProgressBar() will be removed in the next minor release. (spec 039 §5 / §16)",
        error: false)]
    public static ProgressElement ProgressBar() => ProgressIndeterminate();

    public static ProgressRingElement ProgressRing() => new(null);
    public static ProgressRingElement ProgressRing(double value) => new(value);

    // ── Status / Info ───────────────────────────────────────────────

    public static InfoBarElement InfoBar(string? title = null, string? message = null) => new(title, message);

    public static InfoBadgeElement InfoBadge() => new();
    public static InfoBadgeElement InfoBadge(int value) => new() { Value = value };

    // ── Layout ──────────────────────────────────────────────────────

    /// <summary>
    /// Creates a vertical <see cref="StackElement"/> (WinUI <c>StackPanel</c>
    /// with <see cref="Orientation.Vertical"/>). Default <c>Spacing</c> is 8 —
    /// see <see cref="StackElement.Spacing"/>.
    /// </summary>
    /// <remarks>
    /// Reactor-original name — WinUI exposes one <c>StackPanel</c> control
    /// keyed by <c>Orientation</c>; Reactor splits it into two factories
    /// (<see cref="VStack(Element?[])"/> / <see cref="HStack(Element?[])"/>)
    /// because the orientation is almost always known at the call site and the
    /// shorter names reduce DSL noise. The SwiftUI / React Native names are
    /// load-bearing for cross-platform agent familiarity. (spec 039 §0.3)
    /// </remarks>
    public static StackElement VStack(params Element?[] children) =>
        new(Orientation.Vertical, FilterChildren(children));

    /// <summary>
    /// Creates a vertical <see cref="StackElement"/> with an explicit
    /// <c>Spacing</c> override (the first positional argument).
    /// </summary>
    /// <remarks>
    /// Reactor-original convenience overload — see <see cref="VStack(Element?[])"/>
    /// for the naming rationale.
    /// </remarks>
    public static StackElement VStack(double spacing, params Element?[] children) =>
        new(Orientation.Vertical, FilterChildren(children)) { Spacing = spacing };

    /// <summary>
    /// Creates a horizontal <see cref="StackElement"/> (WinUI <c>StackPanel</c>
    /// with <see cref="Orientation.Horizontal"/>). Default <c>Spacing</c> is 8 —
    /// see <see cref="StackElement.Spacing"/>.
    /// </summary>
    /// <remarks>
    /// Reactor-original name — see <see cref="VStack(Element?[])"/> for the
    /// naming rationale. (spec 039 §0.3)
    /// </remarks>
    public static StackElement HStack(params Element?[] children) =>
        new(Orientation.Horizontal, FilterChildren(children));

    /// <summary>
    /// Creates a horizontal <see cref="StackElement"/> with an explicit
    /// <c>Spacing</c> override (the first positional argument).
    /// </summary>
    /// <remarks>
    /// Reactor-original convenience overload — see <see cref="VStack(Element?[])"/>
    /// for the naming rationale.
    /// </remarks>
    public static StackElement HStack(double spacing, params Element?[] children) =>
        new(Orientation.Horizontal, FilterChildren(children)) { Spacing = spacing };

    public static WrapGridElement WrapGrid(params Element?[] children) =>
        new(FilterChildren(children));

    public static WrapGridElement WrapGrid(int maxRowsOrColumns, params Element?[] children) =>
        new(FilterChildren(children)) { MaximumRowsOrColumns = maxRowsOrColumns };

    /// <summary>
    /// Creates a <see cref="ScrollViewerElement"/> wrapping <paramref name="child"/>
    /// in the classic <see cref="Microsoft.UI.Xaml.Controls.ScrollViewer"/>
    /// container (derives from <c>Control</c>; pan + zoom).
    /// </summary>
    /// <remarks>
    /// For the newer InteractionTracker-backed
    /// <see cref="Microsoft.UI.Xaml.Controls.ScrollView"/> (different enum
    /// surface, different events, additive features like
    /// <c>ContentOrientation</c> and anchor ratios), use
    /// <see cref="ScrollView(Element)"/>. Issue #348.
    /// </remarks>
    /// <remarks>
    /// <b>Naming collision with the WinUI attached-property host.</b> When
    /// a caller imports both <c>using static Microsoft.UI.Reactor.Factories;</c>
    /// and <c>using Microsoft.UI.Xaml.Controls;</c>, the simple name
    /// <c>ScrollViewer</c> resolves to this factory method, and a
    /// member-access expression like
    /// <c>ScrollViewer.SetVerticalScrollMode(child, ScrollMode.Disabled)</c>
    /// fails with <c>CS0119</c>. Fully-qualify the attached-property call as
    /// <c>global::Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollMode(...)</c>
    /// (or introduce a type alias) to disambiguate.
    /// </remarks>
    public static ScrollViewerElement ScrollViewer(Element child) => new(child);

    /// <summary>
    /// Creates a <see cref="ScrollViewElement"/> wrapping <paramref name="child"/>
    /// in the modern <see cref="Microsoft.UI.Xaml.Controls.ScrollView"/>
    /// (InteractionTracker-backed, derives from <c>FrameworkElement</c>).
    /// </summary>
    /// <remarks>
    /// Exposes capabilities the legacy <c>ScrollViewer</c> lacks —
    /// <c>ContentOrientation</c>, <c>HorizontalAnchorRatio</c> /
    /// <c>VerticalAnchorRatio</c>, and the <c>Scrolling*</c> enum surface.
    /// For the classic control, use <see cref="ScrollViewer(Element)"/>.
    /// Issue #348.
    /// </remarks>
    public static ScrollViewElement ScrollView(Element child) => new(child);

    public static BorderElement Border(Element? child) => new(child!);

    public static ExpanderElement Expander(string header, Element content, bool isExpanded = false, Action<bool>? onIsExpandedChanged = null) =>
        new(header, content, isExpanded, onIsExpandedChanged);

    public static SplitViewElement SplitView(Element? pane = null, Element? content = null) =>
        new(pane, content);

    public static ViewboxElement Viewbox(Element child) => new(child);

    public static CanvasElement Canvas(params Element?[] children) => new(FilterChildren(children));

    // ── Flex ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a Yoga-based flexbox container (<see cref="FlexElement"/>).
    /// Default direction is <see cref="Microsoft.UI.Reactor.Layout.FlexDirection.Row"/>.
    /// </summary>
    /// <remarks>
    /// Reactor-original — there is no WinUI <c>Flex</c> control. This is a
    /// custom panel backed by Yoga (Facebook's flexbox engine, see
    /// <see cref="Microsoft.UI.Reactor.Layout.FlexPanel"/>) for full CSS-flexbox
    /// semantics inside a WinUI tree. Prefer <see cref="VStack(Element?[])"/> /
    /// <see cref="HStack(Element?[])"/> for simple stacks; reach for Flex when
    /// you need wrap, justify-content / align-items, or per-child grow/shrink.
    /// (spec 039 §0.3)
    /// </remarks>
    public static FlexElement Flex(params Element?[] children) =>
        new(FilterChildren(children));

    /// <summary>
    /// Creates a Yoga flexbox container with an explicit direction.
    /// </summary>
    /// <remarks>
    /// Reactor-original — see <see cref="Flex(Element?[])"/> for the rationale.
    /// </remarks>
    public static FlexElement Flex(Microsoft.UI.Reactor.Layout.FlexDirection direction, params Element?[] children) =>
        new(FilterChildren(children)) { Direction = direction };

    /// <summary>
    /// Creates a Yoga flexbox container with
    /// <see cref="Microsoft.UI.Reactor.Layout.FlexDirection.Row"/>.
    /// </summary>
    /// <remarks>
    /// Reactor-original convenience for the row-direction flex case — see
    /// <see cref="Flex(Element?[])"/> for the rationale. (spec 039 §0.3)
    /// </remarks>
    public static FlexElement FlexRow(params Element?[] children) =>
        new(FilterChildren(children)) { Direction = Microsoft.UI.Reactor.Layout.FlexDirection.Row };

    /// <summary>
    /// Creates a Yoga flexbox container with
    /// <see cref="Microsoft.UI.Reactor.Layout.FlexDirection.Column"/>.
    /// </summary>
    /// <remarks>
    /// Reactor-original convenience for the column-direction flex case — see
    /// <see cref="Flex(Element?[])"/> for the rationale. (spec 039 §0.3)
    /// </remarks>
    public static FlexElement FlexColumn(params Element?[] children) =>
        new(FilterChildren(children)) { Direction = Microsoft.UI.Reactor.Layout.FlexDirection.Column };

    // ── Grid ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="GridElement"/> with strongly-typed track sizes.
    /// </summary>
    /// <remarks>
    /// Spec 033 §1. Use <see cref="GridSize.Auto"/> / <see cref="GridSize.Star(double)"/> /
    /// <see cref="GridSize.Px(double)"/> instead of the legacy string-form
    /// (<c>"Auto"</c>/<c>"*"</c>/<c>"200"</c>) for compile-time validation and
    /// IntelliSense over the legal track shapes.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="columns"/> or <paramref name="rows"/> is null.</exception>
    public static GridElement Grid(
        GridSize[] columns, GridSize[] rows,
        params Element?[] children)
    {
        if (columns is null) throw new ArgumentNullException(nameof(columns));
        if (rows is null) throw new ArgumentNullException(nameof(rows));
        return new(new GridDefinition(columns, rows), FilterChildren(children));
    }

    /// <summary>
    /// Creates a <see cref="GridElement"/> with string-form track sizes. Deprecated
    /// in favor of the typed overload; will be removed in the next minor release.
    /// </summary>
    [global::System.Obsolete(
        "Use Grid(GridSize[], GridSize[], ...) — GridSize.Star/.Auto/.Px helpers. " +
        "String-track overload will be removed in the next minor release. (spec 033 §1)",
        error: false)]
    public static GridElement Grid(
        string[] columns, string[] rows,
        params Element?[] children) =>
        new(new GridDefinition(columns, rows), FilterChildren(children));

    // ── Grid layout builders ────────────────────────────────────────

    /// <summary>
    /// Creates a grid with items interspersed with separator elements along one axis.
    /// Commonly used for split panels where children are separated by splitters.
    ///
    /// Each item gets a proportional (*) size from <paramref name="proportions"/>,
    /// and separators get a fixed pixel size of <paramref name="separatorSize"/>.
    ///
    /// Example: InterspersedGrid(Orientation.Horizontal, children, proportions, 6,
    ///              i => MySplitter(i))
    /// produces columns: "0.33*", "6", "0.33*", "6", "0.34*" with children and splitters placed.
    /// </summary>
    public static GridElement InterspersedGrid(
        Orientation orientation,
        Element[] items,
        double[] proportions,
        double separatorSize,
        Func<int, Element> separatorFactory)
    {
        if (items.Length == 0) return Grid(global::System.Array.Empty<GridSize>(), global::System.Array.Empty<GridSize>());
        if (items.Length != proportions.Length)
            throw new ArgumentException("items and proportions must have the same length");
        for (int i = 0; i < proportions.Length; i++)
        {
            if (proportions[i] < 0 || double.IsNaN(proportions[i]))
                throw new ArgumentOutOfRangeException(nameof(proportions), $"proportions[{i}] must be a non-negative number, got {proportions[i]}");
        }

        var sizes = new List<GridSize>();
        var children = new List<Element>();
        bool isHorizontal = orientation == Orientation.Horizontal;

        for (int i = 0; i < items.Length; i++)
        {
            var starValue = proportions[i];
            sizes.Add(GridSize.Star(starValue));

            children.Add(isHorizontal
                ? items[i].Grid(row: 0, column: i * 2)
                : items[i].Grid(row: i * 2, column: 0));

            if (i < items.Length - 1)
            {
                sizes.Add(GridSize.Px(separatorSize));
                var sep = separatorFactory(i);
                children.Add(isHorizontal
                    ? sep.Grid(row: 0, column: i * 2 + 1)
                    : sep.Grid(row: i * 2 + 1, column: 0));
            }
        }

        var oneStar = new[] { GridSize.Star() };
        return isHorizontal
            ? Grid(sizes.ToArray(), oneStar, children.ToArray())
            : Grid(oneStar, sizes.ToArray(), children.ToArray());
    }

    /// <summary>
    /// Creates a uniform grid with equal-sized cells along one axis.
    /// Shorthand for a grid where all items share equal proportions with no separators.
    /// </summary>
    public static GridElement UniformGrid(Orientation orientation, params Element?[] items)
    {
        var filtered = FilterChildren(items);
        if (filtered.Length == 0) return Grid(global::System.Array.Empty<GridSize>(), global::System.Array.Empty<GridSize>());

        var sizes = Enumerable.Repeat(GridSize.Star(), filtered.Length).ToArray();
        var oneStar = new[] { GridSize.Star() };
        bool isHorizontal = orientation == Orientation.Horizontal;

        for (int i = 0; i < filtered.Length; i++)
        {
            filtered[i] = isHorizontal
                ? filtered[i].Grid(row: 0, column: i)
                : filtered[i].Grid(row: i, column: 0);
        }

        return isHorizontal
            ? Grid(sizes, oneStar, filtered)
            : Grid(oneStar, sizes, filtered);
    }

    // ── Navigation ──────────────────────────────────────────────────

    /// <summary>
    /// Creates a navigation host that renders the current route's content.
    /// Automatically provides the navigation handle via context so child components
    /// can retrieve it with <c>UseNavigation&lt;TRoute&gt;()</c>.
    /// Use <c>with { }</c> to set Transition, CacheMode, and CacheSize.
    /// </summary>
    public static NavigationHostElement NavigationHost<TRoute>(
        Navigation.NavigationHandle<TRoute> nav,
        Func<TRoute, Element> routeMap) where TRoute : notnull
    {
        return new NavigationHostElement(nav, route => routeMap((TRoute)route))
            .Provide(Navigation.NavigationContext<TRoute>.Instance, nav);
    }

    public static NavigationViewElement NavigationView(NavigationViewItemData[] menuItems, Element? content = null) =>
        new(menuItems, content);

    public static NavigationViewItemData NavItem(string content, string? icon = null, string? tag = null) =>
        new(content, icon, tag);

    public static NavigationViewItemData NavItemHeader(string content) =>
        new(content) { IsHeader = true };

    public static TitleBarElement TitleBar(string title) => new(title);

    public static TabViewElement TabView(params TabViewItemData[] tabs) => new(tabs);

    public static TabViewItemData Tab(string header, Element content) => new(header, content);

    public static BreadcrumbBarElement BreadcrumbBar(BreadcrumbBarItemData[] items, Action<BreadcrumbBarItemData>? onItemClicked = null) =>
        new(items, onItemClicked);

    public static BreadcrumbBarItemData Breadcrumb(string label, object? tag = null) => new(label, tag);

    public static PivotElement Pivot(params PivotItemData[] items) => new(items);

    public static PivotItemData PivotItem(string header, Element content) => new(header, content);

    // ── Collections ─────────────────────────────────────────────────

    public static ListViewElement ListView(params Element[] items) => new(items);

    public static GridViewElement GridView(params Element[] items) => new(items);

    public static TreeViewElement TreeView(params TreeViewNodeData[] nodes) => new(nodes);

    public static TreeViewNodeData TreeNode(string content, params TreeViewNodeData[] children) =>
        new(content, children.Length > 0 ? children : null);

    public static FlipViewElement FlipView(params Element[] items) => new(items);

    // ── Dialogs / Overlays ──────────────────────────────────────────

    public static ContentDialogElement ContentDialog(string title, Element content, string primaryButtonText = "OK") =>
        new(title, content, primaryButtonText);

    public static FlyoutElement Flyout(Element target, Element flyoutContent) =>
        new(target, flyoutContent);

    public static TeachingTipElement TeachingTip(string title, string? subtitle = null) =>
        new(title, subtitle);

    public static ContentFlyoutElement ContentFlyout(Element content, FlyoutPlacementMode placement = FlyoutPlacementMode.Auto) =>
        new(content) { Placement = placement };

    public static MenuFlyoutContentElement MenuItems(params MenuFlyoutItemBase[] items) =>
        new(items);

    public static MenuFlyoutContentElement MenuItems(FlyoutPlacementMode placement, params MenuFlyoutItemBase[] items) =>
        new(items) { Placement = placement };

    // ── Menus ───────────────────────────────────────────────────────

    public static MenuBarElement MenuBar(params MenuBarItemData[] items) => new(items);

    public static MenuBarItemData Menu(string title, params MenuFlyoutItemBase[] items) => new(title, items);

    public static MenuFlyoutItemData MenuItem(string text, Action? onClick = null, string? icon = null) => new(text, onClick, icon);

    /// <summary>
    /// Creates a MenuFlyoutItem driven by a Command. Maps Label → Text, Icon,
    /// Execute → OnClick, Accelerator, IsEnabled, AccessKey.
    /// </summary>
    public static MenuFlyoutItemData MenuItem(Core.Command command) =>
        new(command.Label, command.Execute)
        {
            IsEnabled = command.IsEnabled,
            IconElement = command.Icon,
            KeyboardAccelerators = command.Accelerator is not null ? [command.Accelerator] : null,
            AccessKey = command.AccessKey,
            Description = command.Description,
        };

    /// <summary>
    /// Creates a MenuFlyoutItem driven by a parameterized Command. Wraps the action
    /// to invoke with the bound parameter.
    /// </summary>
    public static MenuFlyoutItemData MenuItem<T>(Core.Command<T> command, T parameter) =>
        new(command.Label, command.Execute is not null ? () => command.Execute(parameter) : null)
        {
            IsEnabled = command.IsEnabled,
            IconElement = command.Icon,
            KeyboardAccelerators = command.Accelerator is not null ? [command.Accelerator] : null,
            AccessKey = command.AccessKey,
            Description = command.Description,
        };

    public static ToggleMenuFlyoutItemData ToggleMenuItem(string text, bool isChecked = false, Action<bool>? onIsCheckedChanged = null, string? icon = null) => new(text, isChecked, onIsCheckedChanged, icon);

    public static RadioMenuFlyoutItemData RadioMenuItem(string text, string groupName, bool isChecked = false, Action? onClick = null, string? icon = null) => new(text, groupName, isChecked, onClick, icon);

    public static MenuFlyoutSeparatorData MenuSeparator() => new();

    public static MenuFlyoutSubItemData MenuSubItem(string text, params MenuFlyoutItemBase[] items) => new(text, items);

    public static MenuFlyoutElement MenuFlyout(Element target, params MenuFlyoutItemBase[] items) => new(target, items);

    public static CommandBarElement CommandBar(AppBarItemBase[]? primaryCommands = null, AppBarItemBase[]? secondaryCommands = null) =>
        new(primaryCommands, secondaryCommands);

    public static AppBarButtonData AppBarButton(string label, Action? onClick = null, string? icon = null) => new(label, onClick, icon);

    /// <summary>
    /// Creates an AppBarButton driven by a Command. Maps Label, Icon, Execute,
    /// Accelerator, IsEnabled, AccessKey, and Description.
    /// </summary>
    public static AppBarButtonData AppBarButton(Core.Command command) =>
        new(command.Label, command.Execute)
        {
            IsEnabled = command.IsEnabled,
            IconElement = command.Icon,
            KeyboardAccelerators = command.Accelerator is not null ? [command.Accelerator] : null,
            AccessKey = command.AccessKey,
            Description = command.Description,
        };

    public static AppBarToggleButtonData AppBarToggleButton(string label, bool isChecked = false, Action<bool>? onIsCheckedChanged = null, string? icon = null) =>
        new(label, isChecked, onIsCheckedChanged, icon);

    public static AppBarSeparatorData AppBarSeparator() => new();

    // ── Media ───────────────────────────────────────────────────────

    public static ImageElement Image(string source) => new(source);

    public static PersonPictureElement PersonPicture() => new();

    public static WebView2Element WebView2(Uri? source = null) => new(source);

    // ── Components ──────────────────────────────────────────────────

    /// <summary>
    /// Embed a Component class as a child element.
    /// Usage: Component&lt;MyWidget&gt;()
    /// </summary>
    public static ComponentElement Component<T>() where T : Component, new() =>
        new(typeof(T)) { _factory = () => new T() };

    /// <summary>
    /// Embed a Component class with typed props as a child element.
    /// Returns <see cref="ComponentElement{TProps}"/> so callers can use a
    /// record <c>with</c>-expression to produce a modified copy with updated
    /// typed props (records are immutable — <c>with</c> clones, it does not mutate).
    /// Usage: Component&lt;MyWidget, string&gt;("param")
    /// </summary>
    public static ComponentElement<TProps> Component<T, TProps>(TProps props)
        where T : Component<TProps>, new() =>
        new(typeof(T), props) { _factory = () => new T() };

    /// <summary>
    /// Define an inline function component (like a React function component).
    /// Usage: Func(ctx => { var (n,setN) = ctx.UseState(0); return TextBlock($"{n}"); })
    /// </summary>
    /// <remarks>
    /// Spec 033 §4 — soft-deprecated. <c>Func(...)</c> is "inline + own hooks +
    /// no memoization", a niche rarely used intentionally. Replace with:
    /// <list type="bullet">
    ///   <item><description><see cref="Memo(Func{RenderContext, Element}, object?[])"/> with no deps for the common case (render once + state-driven re-renders).</description></item>
    ///   <item><description><see cref="RenderEachTime"/> when you specifically want the old "re-render every parent render" behavior.</description></item>
    /// </list>
    /// </remarks>
    [global::System.Obsolete(
        "Use Memo(ctx => …) for render-once-plus-state semantics, or " +
        "RenderEachTime(ctx => …) for the explicit always-re-render case. " +
        "Func will be removed in the next minor release. (spec 033 §4)",
        error: false)]
    public static FuncElement Func(Func<RenderContext, Element> render) => new(render);

    /// <summary>
    /// Define a memoized inline function component. Skips re-render when dependencies haven't changed.
    /// Empty deps array = render once + own state changes only. Non-empty = re-render when any dep changes.
    /// Usage: Memo(ctx => TextBlock("stable"), someProp, otherProp)
    /// </summary>
    public static MemoElement Memo(Func<RenderContext, Element> render, params object?[] dependencies)
        => new(render, dependencies.Length == 0 ? null : dependencies);

    /// <summary>
    /// Define an inline function component that re-renders on every parent render
    /// (no memoization), keeping its own hook scope. Equivalent to the legacy
    /// <see cref="Func"/> behavior, made explicit so the reader can tell the
    /// always-re-render case apart from a missing deps array.
    /// </summary>
    /// <remarks>
    /// Spec 033 §4. Use sparingly — components that re-render on every parent
    /// render defeat the memoization story and can amplify render storms.
    /// Prefer <see cref="Memo(Func{RenderContext, Element}, object?[])"/> with
    /// an explicit deps array whenever the re-render trigger can be enumerated.
    /// </remarks>
    public static FuncElement RenderEachTime(Func<RenderContext, Element> render) => new(render);

    // ── Command host ─────────────────────────────────────────────────

    /// <summary>
    /// Scopes keyboard accelerators from the given commands to the child subtree.
    /// Only commands with an Accelerator produce keyboard accelerators on the host element.
    /// </summary>
    public static Core.CommandHostElement CommandHost(Core.Command[] commands, Element child) =>
        new(commands, child);

    // ── Conditional helpers ─────────────────────────────────────────

    /// <summary>
    /// Renders element only when condition is true. Reads nicely:
    ///   When(items.Any(), () =&gt; TextBlock("Has items"))
    /// </summary>
    public static Element When(bool condition, Func<Element> then) =>
        condition ? then() : EmptyElement.Instance;

    /// <summary>
    /// If/else as an expression:
    ///   If(loggedIn, () =&gt; TextBlock("Welcome"), () =&gt; Button("Login", ...))
    /// </summary>
    public static Element If(bool condition, Func<Element> then, Func<Element>? otherwise = null) =>
        condition ? then() : (otherwise?.Invoke() ?? EmptyElement.Instance);

    /// <summary>
    /// Inline block-expression escape hatch: invokes <paramref name="render"/> and returns
    /// its element, or <c>EmptyElement.Instance</c> when the lambda returns <c>null</c>.
    /// Lets callers write multi-statement bodies inside a DSL tree without extracting a
    /// local function or relying on the <c>((Func&lt;Element?&gt;)(() =&gt; …))()</c> cast trick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spec 033 §5. <c>Expr</c> performs no memoization, owns no <c>RenderContext</c>, and
    /// is not a reconciler boundary — it is purely composition sugar. If the body needs
    /// hooks, use <c>Memo(...)</c> or a <c>Component&lt;TProps&gt;</c> instead.
    /// </para>
    /// <para>
    /// Exceptions thrown inside <paramref name="render"/> propagate unchanged so the
    /// surrounding error-boundary path applies.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// VStack(
    ///     Header(),
    ///     Expr(() =&gt; {
    ///         var summary = ComputeSummary(orders);
    ///         return summary.Total &gt; 0
    ///             ? TotalsBanner(summary)
    ///             : null;
    ///     }),
    ///     Footer())
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="render"/> is <c>null</c>.</exception>
    public static Element Expr(Func<Element?> render)
    {
        ArgumentNullException.ThrowIfNull(render);
        return render() ?? EmptyElement.Instance;
    }

    /// <summary>
    /// Map a list to elements (like .map() in React JSX):
    ///   ForEach(items, item =&gt; TextBlock(item.Name))
    /// </summary>
    public static Element ForEach<T>(IEnumerable<T> items, Func<T, Element> render) =>
        new GroupElement(items.Select(render).ToArray());

    /// <summary>
    /// Map with index:
    ///   ForEach(items, (item, i) =&gt; TextBlock($"{i}: {item}"))
    /// </summary>
    public static Element ForEach<T>(IEnumerable<T> items, Func<T, int, Element> render) =>
        new GroupElement(items.Select((item, i) => render(item, i)).ToArray());

    /// <summary>
    /// Groups elements without introducing a layout container (like React's Fragment).
    /// Children are flattened into the parent container.
    /// </summary>
    public static Element Group(params Element?[] children) =>
        new GroupElement(FilterChildren(children));

    /// <summary>
    /// Renders nothing. Useful as a default/fallback.
    /// </summary>
    public static Element Empty() => EmptyElement.Instance;

    /// <summary>
    /// Wraps a child subtree in an error boundary. If any component in the subtree
    /// throws during rendering, the fallback function is called with the exception.
    /// When the ErrorBoundary re-renders, it retries the child (error recovery).
    /// </summary>
    public static ErrorBoundaryElement ErrorBoundary(
        Element child, Func<Exception, Element> fallback) => new(child, fallback);

    /// <summary>
    /// Wraps a child subtree in an error boundary with a static fallback element.
    /// </summary>
    public static ErrorBoundaryElement ErrorBoundary(
        Element child, Element fallback) => new(child, _ => fallback);

    // ── Thickness helpers (WinUI lacks a (horizontal, vertical) constructor) ──

    /// <summary>
    /// Creates a Thickness with horizontal and vertical values.
    /// Usage: Thick(16, 8) → Thickness(16, 8, 16, 8)
    /// </summary>
    public static Thickness Thick(double horizontal, double vertical) =>
        new(horizontal, vertical, horizontal, vertical);

    /// <summary>
    /// Creates a uniform Thickness. Shorthand for new Thickness(uniform).
    /// </summary>
    public static Thickness Thick(double uniform) => new(uniform);

    /// <summary>
    /// Creates a Thickness with all four sides specified.
    /// </summary>
    public static Thickness Thick(double left, double top, double right, double bottom) =>
        new(left, top, right, bottom);

    // ── Typed (data-driven) collections ───────────────────────────

    /// <summary>
    /// Creates a typed, data-driven <see cref="TemplatedListViewElement{T}"/>.
    /// The reconciler builds a WinUI <c>ListView</c> bound to <paramref name="items"/>
    /// and instantiates one view per item via <paramref name="viewBuilder"/>.
    /// </summary>
    /// <remarks>
    /// Reactor-original generic peer of WinUI's untyped <c>ListView</c>. The
    /// element record name is <c>TemplatedListViewElement&lt;T&gt;</c> (templated +
    /// typed) but the factory is the short <c>ListView</c>; the type parameter
    /// disambiguates from the existing untyped factory at the call site.
    /// (spec 039 §0.3)
    /// </remarks>
    public static TemplatedListViewElement<T> ListView<T>(
        IReadOnlyList<T> items,
        Func<T, string> keySelector,
        Func<T, int, Element> viewBuilder) => new(items, keySelector, viewBuilder);

    /// <summary>
    /// Creates a typed, data-driven <see cref="TemplatedListViewElement{T}"/>
    /// for items that implement <see cref="IReactorKeyed"/>. The
    /// <c>KeySelector</c> defaults to <c>t =&gt; t.Key</c> so call sites can
    /// omit it. (spec 042 §5)
    /// </summary>
    public static TemplatedListViewElement<T> ListView<T>(
        IReadOnlyList<T> items,
        Func<T, int, Element> viewBuilder) where T : IReactorKeyed =>
        new(items, static t => t.Key, viewBuilder);

    /// <summary>
    /// Creates a typed, data-driven <see cref="TemplatedGridViewElement{T}"/>
    /// — the templated peer of WinUI's untyped <c>GridView</c>.
    /// </summary>
    /// <remarks>
    /// Reactor-original — see <see cref="ListView{T}(IReadOnlyList{T}, Func{T, string}, Func{T, int, Element})"/>
    /// for the templated-peer naming rationale. (spec 039 §0.3)
    /// </remarks>
    public static TemplatedGridViewElement<T> GridView<T>(
        IReadOnlyList<T> items,
        Func<T, string> keySelector,
        Func<T, int, Element> viewBuilder) => new(items, keySelector, viewBuilder);

    /// <summary>
    /// <see cref="IReactorKeyed"/>-typed overload of
    /// <see cref="GridView{T}(IReadOnlyList{T}, Func{T, string}, Func{T, int, Element})"/>;
    /// <c>KeySelector</c> defaults to <c>t =&gt; t.Key</c>. (spec 042 §5)
    /// </summary>
    public static TemplatedGridViewElement<T> GridView<T>(
        IReadOnlyList<T> items,
        Func<T, int, Element> viewBuilder) where T : IReactorKeyed =>
        new(items, static t => t.Key, viewBuilder);

    /// <summary>
    /// Creates a typed, data-driven <see cref="TemplatedFlipViewElement{T}"/>
    /// — the templated peer of WinUI's untyped <c>FlipView</c>.
    /// </summary>
    /// <remarks>
    /// Reactor-original — see <see cref="ListView{T}(IReadOnlyList{T}, Func{T, string}, Func{T, int, Element})"/>
    /// for the templated-peer naming rationale. (spec 039 §0.3)
    /// </remarks>
    public static TemplatedFlipViewElement<T> FlipView<T>(
        IReadOnlyList<T> items,
        Func<T, string> keySelector,
        Func<T, int, Element> viewBuilder) => new(items, keySelector, viewBuilder);

    /// <summary>
    /// <see cref="IReactorKeyed"/>-typed overload of
    /// <see cref="FlipView{T}(IReadOnlyList{T}, Func{T, string}, Func{T, int, Element})"/>;
    /// <c>KeySelector</c> defaults to <c>t =&gt; t.Key</c>. (spec 042 §5)
    /// </summary>
    public static TemplatedFlipViewElement<T> FlipView<T>(
        IReadOnlyList<T> items,
        Func<T, int, Element> viewBuilder) where T : IReactorKeyed =>
        new(items, static t => t.Key, viewBuilder);

    // ── Virtualized collections ───────────────────────────────────

    /// <summary>
    /// Creates a virtualized vertical stack of templated items. Backed by a
    /// WinUI <c>ItemsRepeater</c> inside a <c>ScrollViewer</c> — children are
    /// materialized on demand, so this scales to large item counts.
    /// </summary>
    /// <remarks>
    /// Reactor-original — there is no WinUI <c>LazyVStack</c>; the name is borrowed
    /// from SwiftUI for the "vertical stack, lazy materialization" semantics.
    /// Prefer this over <see cref="VStack(Element?[])"/> when the child count is
    /// large or the children are expensive to instantiate. (spec 039 §0.3)
    /// </remarks>
    public static LazyVStackElement<T> LazyVStack<T>(
        IReadOnlyList<T> items,
        Func<T, string> keySelector,
        Func<T, int, Element> viewBuilder) => new(items, keySelector, viewBuilder);

    /// <summary>
    /// <see cref="IReactorKeyed"/>-typed overload of
    /// <see cref="LazyVStack{T}(IReadOnlyList{T}, Func{T, string}, Func{T, int, Element})"/>;
    /// <c>KeySelector</c> defaults to <c>t =&gt; t.Key</c>. (spec 042 §5)
    /// </summary>
    public static LazyVStackElement<T> LazyVStack<T>(
        IReadOnlyList<T> items,
        Func<T, int, Element> viewBuilder) where T : IReactorKeyed =>
        new(items, static t => t.Key, viewBuilder);

    /// <summary>
    /// Creates a virtualized horizontal stack of templated items — the horizontal
    /// peer of <see cref="LazyVStack{T}(IReadOnlyList{T}, Func{T, string}, Func{T, int, Element})"/>.
    /// </summary>
    /// <remarks>
    /// Reactor-original — see <see cref="LazyVStack{T}(IReadOnlyList{T}, Func{T, string}, Func{T, int, Element})"/>
    /// for the naming rationale. (spec 039 §0.3)
    /// </remarks>
    public static LazyHStackElement<T> LazyHStack<T>(
        IReadOnlyList<T> items,
        Func<T, string> keySelector,
        Func<T, int, Element> viewBuilder) => new(items, keySelector, viewBuilder);

    /// <summary>
    /// <see cref="IReactorKeyed"/>-typed overload of
    /// <see cref="LazyHStack{T}(IReadOnlyList{T}, Func{T, string}, Func{T, int, Element})"/>;
    /// <c>KeySelector</c> defaults to <c>t =&gt; t.Key</c>. (spec 042 §5)
    /// </summary>
    public static LazyHStackElement<T> LazyHStack<T>(
        IReadOnlyList<T> items,
        Func<T, int, Element> viewBuilder) where T : IReactorKeyed =>
        new(items, static t => t.Key, viewBuilder);

    // ── Shapes ───────────────────────────────────────────────────────

    public static RectangleElement Rectangle() => new();

    public static EllipseElement Ellipse() => new();

    public static LineElement Line(double x1, double y1, double x2, double y2) =>
        new() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };

    // Named `Path2D` (not `Path`) to avoid colliding with `System.IO.Path`.
    // Models reach for both in the same file and the bare name causes
    // CS0119 cascades. Borrows the Web Canvas API's `Path2D` spelling for the
    // vector-geometry primitive — collision-free and familiar from JS/SVG.
    public static PathElement Path2D() => new();

    // ── Additional layout ───────────────────────────────────────────

    public static RelativePanelElement RelativePanel(params Element?[] children) => new(FilterChildren(children));

    // ── Additional media ────────────────────────────────────────────

    public static MediaPlayerElementElement MediaPlayerElement(string? source = null) => new(source);

    public static AnimatedVisualPlayerElement AnimatedVisualPlayer() => new();

    // ── Additional collections ──────────────────────────────────────

    public static SemanticZoomElement SemanticZoom(Element zoomedInView, Element zoomedOutView) =>
        new(zoomedInView, zoomedOutView);

    public static ListBoxElement ListBox(string[] items, int selectedIndex = -1, Action<int>? onSelectedIndexChanged = null) =>
        new(items) { SelectedIndex = selectedIndex, OnSelectedIndexChanged = onSelectedIndexChanged };

    // ── Additional navigation ───────────────────────────────────────

    public static SelectorBarElement SelectorBar(SelectorBarItemData[] items, int selectedIndex = 0, Action<int>? onSelectedIndexChanged = null) =>
        new(items) { SelectedIndex = selectedIndex, OnSelectedIndexChanged = onSelectedIndexChanged };

    public static SelectorBarItemData SelectorBarItem(string text, string? icon = null) => new(text, icon);

    public static PipsPagerElement PipsPager(int numberOfPages, int selectedPageIndex = 0, Action<int>? onSelectedPageIndexChanged = null) =>
        new(numberOfPages) { SelectedPageIndex = selectedPageIndex, OnSelectedPageIndexChanged = onSelectedPageIndexChanged };

    public static AnnotatedScrollBarElement AnnotatedScrollBar() => new();

    // ── Additional overlays / containers ────────────────────────────

    public static PopupElement Popup(Element child, bool isOpen = false, Action? onClosed = null) =>
        new(child) { IsOpen = isOpen, OnClosed = onClosed };

    public static RefreshContainerElement RefreshContainer(Element content, Action? onRefreshRequested = null) =>
        new(content) { OnRefreshRequested = onRefreshRequested };

    public static CommandBarFlyoutElement CommandBarFlyout(Element target, AppBarItemBase[]? primaryCommands = null, AppBarItemBase[]? secondaryCommands = null) =>
        new(target, primaryCommands, secondaryCommands);

    // ── Additional date / time ──────────────────────────────────────

    public static CalendarViewElement CalendarView() => new();

    // ── SwipeControl ────────────────────────────────────────────────

    public static SwipeControlElement SwipeControl(Element content,
        SwipeItemData[]? leftItems = null, SwipeItemData[]? rightItems = null) =>
        new(content) { LeftItems = leftItems, RightItems = rightItems };

    // ── AnimatedIcon ────────────────────────────────────────────────

    public static AnimatedIconElement AnimatedIcon(object? source = null, IconSource? fallbackIconSource = null) =>
        new() { Source = source, FallbackIconSource = fallbackIconSource };

    // ── ParallaxView ────────────────────────────────────────────────

    public static ParallaxViewElement ParallaxView(Element child, double verticalShift = 0, double horizontalShift = 0) =>
        new(child) { VerticalShift = verticalShift, HorizontalShift = horizontalShift };

    // ── MapControl ──────────────────────────────────────────────────

    public static MapControlElement MapControl(string? mapServiceToken = null, double zoomLevel = 1) =>
        new() { MapServiceToken = mapServiceToken, ZoomLevel = zoomLevel };

    // ── Frame ───────────────────────────────────────────────────────

    public static FrameElement Frame(Type? sourcePageType = null, object? navigationParameter = null) =>
        new() { SourcePageType = sourcePageType, NavigationParameter = navigationParameter };

    // ── ItemsView ───────────────────────────────────────────────────

    public static ItemsViewElement<T> ItemsView<T>(
        IReadOnlyList<T> items,
        Func<T, string> keySelector,
        Func<T, int, Element> viewBuilder) => new(items, keySelector, viewBuilder);

    // ── Rich text helpers ───────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="RichTextBlockElement"/> from an array of typed
    /// paragraphs. Each paragraph contains a sequence of inline runs / hyperlinks
    /// / line breaks built with <see cref="Paragraph(RichTextInline[])"/>,
    /// <see cref="Run(string)"/>, and <see cref="Hyperlink(string, Uri)"/>.
    /// </summary>
    /// <remarks>
    /// Named for parity with WinUI's <c>Microsoft.UI.Xaml.Controls.RichTextBlock</c>.
    /// (spec 039 §1.3 / §14 #8)
    /// </remarks>
    public static RichTextBlockElement RichTextBlock(RichTextParagraph[] paragraphs) =>
        new("") { Paragraphs = paragraphs };

    /// <summary>
    /// Deprecated forwarding alias for <see cref="RichTextBlock(RichTextParagraph[])"/>.
    /// </summary>
    [global::System.Obsolete(
        "Renamed to RichTextBlock for parity with WinUI's Microsoft.UI.Xaml.Controls.RichTextBlock. " +
        "RichText will be removed in the next minor release. (spec 039 §1.3 / §14 #8)",
        error: false)]
    public static RichTextBlockElement RichText(RichTextParagraph[] paragraphs) =>
        RichTextBlock(paragraphs);

    public static RichTextParagraph Paragraph(params RichTextInline[] inlines) => new(inlines);

    public static RichTextRun Run(string text) => new(text);

    public static RichTextHyperlink Hyperlink(string text, Uri navigateUri) => new(text, navigateUri);

    // ── Markdown ─────────────────────────────────────────────────────

    /// <summary>
    /// Render a markdown string as a Reactor element tree.
    /// </summary>
    public static Element Markdown(string markdown) =>
        MarkdownBuilder.Build(markdown, null);

    /// <summary>
    /// Render a markdown string as a Reactor element tree with custom rendering options.
    /// </summary>
    public static Element Markdown(string markdown, MarkdownOptions options) =>
        MarkdownBuilder.Build(markdown, options);

    // ── Icons ────────────────────────────────────────────────────────

    public static SymbolIconData SymbolIcon(string symbol) => new(symbol);

    public static FontIconData FontIcon(string glyph, string? fontFamily = null, double? fontSize = null) =>
        new(glyph, fontFamily, fontSize);

    public static BitmapIconData BitmapIcon(global::System.Uri source, bool showAsMonochrome = true) =>
        new(source, showAsMonochrome);

    public static PathIconData PathIcon(string data) => new(data);

    public static ImageIconData ImageIcon(global::System.Uri source) => new(source);

    /// <summary>Creates a standalone icon element from an <see cref="IconData"/> instance.</summary>
    public static Core.IconElement Icon(IconData data) => new(data);

    /// <summary>Creates a standalone symbol icon element from a <see cref="Symbol"/> enum value.</summary>
    public static Core.IconElement Icon(Symbol symbol) => new(new SymbolIconData(symbol.ToString()));

    /// <summary>Creates a standalone symbol icon element (e.g. <c>Icon("Home")</c>).</summary>
    public static Core.IconElement Icon(string symbol) => new(new SymbolIconData(symbol));

    // ── Keyboard Accelerators ───────────────────────────────────────

    public static KeyboardAcceleratorData Accelerator(global::Windows.System.VirtualKey key, global::Windows.System.VirtualKeyModifiers modifiers = global::Windows.System.VirtualKeyModifiers.None) =>
        new(key, modifiers);

    // ── Brushes ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new AcrylicBrush. This allocates a WinRT DependencyObject on every call.
    /// On hot paths (e.g., inside Render methods), cache the result with <c>UseMemo</c>:
    /// <code>var brush = ctx.UseMemo(() => UI.AcrylicBrush(color, 0.8), color);</code>
    /// </summary>
    public static Microsoft.UI.Xaml.Media.AcrylicBrush AcrylicBrush(
        global::Windows.UI.Color tintColor,
        double tintOpacity = 0.8,
        global::Windows.UI.Color? fallbackColor = null,
        double? tintLuminosityOpacity = null)
    {
        var brush = new Microsoft.UI.Xaml.Media.AcrylicBrush
        {
            TintColor = tintColor,
            TintOpacity = tintOpacity,
        };
        if (fallbackColor.HasValue) brush.FallbackColor = fallbackColor.Value;
        if (tintLuminosityOpacity.HasValue) brush.TintLuminosityOpacity = tintLuminosityOpacity.Value;
        return brush;
    }

    // ── Internals ───────────────────────────────────────────────────

    private static Element[] FilterChildren(Element?[] children)
    {
        // Fast path: check if any nulls or GroupElements need expansion
        bool needsExpansion = false;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is null or GroupElement or EmptyElement)
            {
                needsExpansion = true;
                break;
            }
        }
        if (!needsExpansion) return (Element[])(object)children;

        // Flatten GroupElements and remove nulls
        var result = new List<Element>();
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is GroupElement group)
            {
                foreach (var gc in group.Children)
                {
                    if (gc is not null and not EmptyElement)
                        result.Add(gc);
                }
            }
            else if (children[i] is not null and not EmptyElement)
            {
                result.Add(children[i]!);
            }
        }
        return result.ToArray();
    }
}
