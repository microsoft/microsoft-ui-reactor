using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Text;
using Windows.UI.Text;
using MenuFlyoutItemBase = Microsoft.UI.Reactor.Core.MenuFlyoutItemBase;
// Spec 048 §7 — aliases for the per-factory `Reg<>` registration touch.
using V1 = Microsoft.UI.Reactor.Core.V1Protocol;
using Desc = Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;

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
    // Shared single-Star track array for the cross-axis of UniformGrid /
    // InterspersedGrid. Safe to share: GridDefinition immediately converts
    // GridSize[] tracks to string[] (read-only consumption), so the array is
    // never retained or mutated by callers, and GridSize is a value struct.
    private static readonly GridSize[] s_oneStar = { GridSize.Star() };

    private static Optional<int> ToOptionalSelectedIndex(int? selectedIndex) =>
        selectedIndex.HasValue ? Optional<int>.Of(selectedIndex.Value) : Optional<int>.Unset;

    // ── Localization ──────────────────────────────────────────────────

    public static ComponentElement<Localization.LocaleProviderElement> LocaleProvider(
        string locale, Element child,
        Localization.IStringResourceProvider? resourceProvider = null,
        string defaultLocale = "en-US",
        bool pseudoLocalize = false) =>
        Component<Localization.LocaleProviderComponent, Localization.LocaleProviderElement>(
            new Localization.LocaleProviderElement(locale, child, resourceProvider, defaultLocale, pseudoLocalize));

    // ── Text ────────────────────────────────────────────────────────

    public static TextBlockElement TextBlock(string content)
    {
        // Spec 048 §7 — register TextBlockElement's handler into the global
        // ControlRegistry the first time any TextBlock-producing factory runs.
        // Post-§3.4 this is the live dispatch path (the eager registrar is gone).
        return new(content);
    }

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
    public static TextBlockElement Heading(string content)
    {
        return new(content) { FontSize = 28, Weight = new global::Windows.UI.Text.FontWeight(700),
            Modifiers = new Core.ElementModifiers
            {
                HeadingLevel = Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1
            } };
    }

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
    public static TextBlockElement SubHeading(string content)
    {
        return new(content) { FontSize = 20, Weight = new global::Windows.UI.Text.FontWeight(600),
            Modifiers = new Core.ElementModifiers
            {
                HeadingLevel = Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2
            } };
    }

    /// <summary>
    /// Creates a caption-styled <see cref="TextBlockElement"/> (12px).
    /// </summary>
    /// <remarks>
    /// Reactor-original convenience wrapper — there is no WinUI control named
    /// <c>Caption</c>. Sized for the WinUI Caption type-ramp slot; use for
    /// secondary metadata (timestamps, helper text, hints) below primary copy.
    /// (spec 039 §0.3)
    /// </remarks>
    public static TextBlockElement Caption(string content)
    {
        return new(content) { FontSize = 12 };
    }

    /// <summary>
    /// Creates a <see cref="RichTextBlockElement"/> wrapping a single string of
    /// plain text. Use the <see cref="RichTextBlock(RichTextParagraph[])"/>
    /// overload to compose runs, hyperlinks, and inline formatting.
    /// </summary>
    /// <remarks>
    /// Named for parity with WinUI's <c>Microsoft.UI.Xaml.Controls.RichTextBlock</c>.
    /// (spec 039 §1.3 / §14 #8)
    /// </remarks>
    public static RichTextBlockElement RichTextBlock(string text)
    {
        return new(text);
    }

    /// <summary>
    /// Creates a <see cref="RichEditBoxElement"/>. <paramref name="text"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the WinUI control own its text
    /// (uncontrolled). Pass any string (implicit <c>T → Optional&lt;T&gt;</c>) to control
    /// the text from a parent state.
    /// </summary>
    public static RichEditBoxElement RichEditBox(Optional<string> text = default, Action<string>? onTextChanged = null)
    {
        return new(text) { OnTextChanged = onTextChanged };
    }

    // ── Buttons ─────────────────────────────────────────────────────

    public static ButtonElement Button(string label, Action? onClick = null)
    {
        // Spec 048 §3.4 — per-factory `Reg<>` registration touch.
        // Live dispatch path post-§3.4 (the eager registrar is gone).
        return new(label, onClick);
    }

    public static ButtonElement Button(Element content, Action? onClick = null)
    {
        return new("", onClick) { ContentElement = content };
    }

    /// <summary>
    /// Creates a Button driven by a Command. Maps Label → Content; the command's
    /// Execute/ExecuteAsync is invoked on click and its IsEnabled / Description / Accelerator /
    /// AccessKey are applied by the reconciler from the typed <see cref="ButtonElement.Command"/>
    /// property (issues #153, #637) — no per-render Setters array or lambda is allocated, and a
    /// bare <c>new ButtonElement(cmd.Label) { Command = cmd }</c> record-init behaves identically.
    /// </summary>
    public static ButtonElement Button(Core.Command command)
    {
        return new ButtonElement(command.Label) { Command = command };
    }

    public static HyperlinkButtonElement HyperlinkButton(string content, Uri? navigateUri = null, Action? onClick = null)
    {
        // Spec 048 §3.3 — register HyperlinkButtonElement's handler into the
        // global ControlRegistry on first HyperlinkButton-producing factory use.
        // Live dispatch path post-§3.4 (the eager registrar is gone).
        return new(content, navigateUri, onClick);
    }

    /// <summary>
    /// Creates a HyperlinkButton driven by a Command. Maps Label → Content, Execute →
    /// Click. For external navigation, chain <see cref="ElementExtensions.NavigateUri(HyperlinkButtonElement, Uri)"/>:
    /// <c>HyperlinkButton(cmd).NavigateUri(new Uri("https://..."))</c>.
    /// </summary>
    public static HyperlinkButtonElement HyperlinkButton(Core.Command command)
    {
        return new HyperlinkButtonElement(command.Label) { Command = command };
    }

    public static RepeatButtonElement RepeatButton(string label, Action? onClick = null)
    {
        return new(label, onClick);
    }

    /// <summary>Creates a RepeatButton driven by a Command. Click auto-repeats while held.</summary>
    public static RepeatButtonElement RepeatButton(Core.Command command)
    {
        return new RepeatButtonElement(command.Label) { Command = command };
    }

    public static ToggleButtonElement ToggleButton(string label, bool isChecked = false, Action<bool>? onIsCheckedChanged = null)
    {
        return new(label, isChecked, onIsCheckedChanged);
    }

    /// <summary>
    /// Creates a ToggleButton driven by a Command. The command fires on each toggle
    /// (both check and uncheck) — per the spec's "Option A" semantics. Use the
    /// <c>isChecked</c> parameter to seed the initial state.
    /// </summary>
    public static ToggleButtonElement ToggleButton(Core.Command command, bool isChecked = false)
    {
        return new ToggleButtonElement(command.Label, isChecked) { Command = command };
    }

    /// <summary>
    /// Three-state toggle button (true → false → null → ...). Matches the
    /// established <c>ThreeStateCheckBox</c> factory pattern from spec 039 §2.4.
    /// </summary>
    public static ToggleButtonElement ThreeStateToggleButton(string label, bool? checkedState = null, Action<bool?>? onCheckedStateChanged = null)
    {
        return new(label, checkedState == true) { IsThreeState = true, CheckedState = checkedState, OnCheckedStateChanged = onCheckedStateChanged };
    }

    public static DropDownButtonElement DropDownButton(string label, Element? flyout = null)
    {
        return new(label, flyout);
    }

    public static SplitButtonElement SplitButton(string label, Action? onClick = null, Element? flyout = null)
    {
        return new(label, onClick, flyout);
    }

    /// <summary>
    /// Creates a SplitButton driven by a Command for the primary action. The flyout
    /// (dropdown portion) is independent and supplied separately.
    /// </summary>
    public static SplitButtonElement SplitButton(Core.Command command, Element? flyout = null)
    {
        return new SplitButtonElement(command.Label, null, flyout) { Command = command };
    }

    /// <summary>
    /// Creates a <see cref="ToggleSplitButtonElement"/>. <paramref name="isChecked"/> defaults
    /// to <see cref="Optional{T}.Unset"/> — omit it to let the WinUI control own its checked
    /// state (uncontrolled). Pass <c>true</c>/<c>false</c> (implicit <c>T → Optional&lt;T&gt;</c>)
    /// to control it from a parent state.
    /// </summary>
    public static ToggleSplitButtonElement ToggleSplitButton(string label, Optional<bool> isChecked = default, Action<bool>? onIsCheckedChanged = null, Element? flyout = null)
    {
        return new(label, isChecked, onIsCheckedChanged, flyout);
    }

    /// <summary>Creates a ToggleSplitButton driven by a Command (fires on each toggle).</summary>
    public static ToggleSplitButtonElement ToggleSplitButton(Core.Command command, Optional<bool> isChecked = default, Element? flyout = null)
    {
        return new ToggleSplitButtonElement(command.Label, isChecked, null, flyout) { Command = command };
    }

    // ── Input controls ──────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="TextBoxElement"/> wrapping WinUI's
    /// <c>Microsoft.UI.Xaml.Controls.TextBox</c>.
    /// </summary>
    /// <summary>
    /// Creates a <see cref="TextBoxElement"/>. <paramref name="value"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the WinUI control own its text
    /// (uncontrolled). Pass any string (implicit <c>T → Optional&lt;T&gt;</c>) to control it.
    /// </summary>
    public static TextBoxElement TextBox(Optional<string> value = default, Action<string>? onChanged = null, string? placeholderText = null, string? header = null)
    {
        return new(value, onChanged, placeholderText) { Header = header };
    }

    /// <summary>
    /// Creates a <see cref="PasswordBoxElement"/>. <paramref name="password"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the WinUI control own its password.
    /// </summary>
    public static PasswordBoxElement PasswordBox(Optional<string> password = default, Action<string>? onPasswordChanged = null, string? placeholderText = null)
    {
        return new(password, onPasswordChanged, placeholderText);
    }

    /// <summary>
    /// Creates a <see cref="NumberBoxElement"/>. <paramref name="value"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the WinUI control own its value.
    /// </summary>
    public static NumberBoxElement NumberBox(Optional<double> value = default, Action<double>? onValueChanged = null, string? header = null)
    {
        return new(value, onValueChanged, header);
    }

    /// <summary>
    /// Creates an <see cref="AutoSuggestBoxElement"/>. <paramref name="text"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the WinUI control own its text.
    /// </summary>
    public static AutoSuggestBoxElement AutoSuggestBox(Optional<string> text = default, Action<string>? onTextChanged = null, Action<string>? onQuerySubmitted = null)
    {
        return new(text, onTextChanged, onQuerySubmitted);
    }

    /// <summary>
    /// Creates a <see cref="CheckBoxElement"/>. <paramref name="isChecked"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the WinUI control own its checked state.
    /// </summary>
    public static CheckBoxElement CheckBox(Optional<bool?> isChecked = default, Action<bool>? onIsCheckedChanged = null, string? label = null)
    {
        return new(isChecked, onIsCheckedChanged, label);
    }

    /// <summary>
    /// Creates a three-state <see cref="CheckBoxElement"/>. <paramref name="checkedState"/>
    /// defaults to <see cref="Optional{T}.Unset"/> — omit it to let the control own its state.
    /// </summary>
    public static CheckBoxElement ThreeStateCheckBox(Optional<bool?> checkedState = default, Action<bool?>? onCheckedStateChanged = null, string? label = null)
    {
        return new(checkedState, Label: label) { IsThreeState = true, CheckedState = checkedState.HasValue ? checkedState.Value : null, OnCheckedStateChanged = onCheckedStateChanged };
    }

    /// <summary>
    /// Creates a <see cref="RadioButtonElement"/>. <paramref name="isChecked"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the control own its checked state.
    /// </summary>
    public static RadioButtonElement RadioButton(string label, Optional<bool> isChecked = default, Action<bool>? onIsCheckedChanged = null, string? groupName = null)
    {
        return new(label, isChecked, onIsCheckedChanged, groupName);
    }

    /// <summary>
    /// Creates a <see cref="RadioButtonsElement"/>. <paramref name="selectedIndex"/> defaults
    /// to <see cref="Optional{T}.Unset"/> — omit it to let the control own its selection.
    /// </summary>
    public static RadioButtonsElement RadioButtons(string[] items, Optional<int> selectedIndex = default, Action<int>? onSelectedIndexChanged = null)
    {
        return new(items, selectedIndex, onSelectedIndexChanged);
    }

    /// <summary>
    /// Creates a <see cref="ComboBoxElement"/>. <paramref name="selectedIndex"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the control own its selection.
    /// </summary>
    public static ComboBoxElement ComboBox(string[] items, Optional<int> selectedIndex = default, Action<int>? onSelectedIndexChanged = null)
    {
        return new(items, selectedIndex, onSelectedIndexChanged);
    }

    /// <summary>
    /// Creates a <see cref="ComboBoxElement"/> from element items. <paramref name="selectedIndex"/>
    /// is taken as-is — pass <see cref="Optional{T}.Unset"/> for uncontrolled, or any int
    /// (implicit <c>int → Optional&lt;int&gt;</c>) for controlled. Both trailing args are
    /// required (rather than defaulted) to disambiguate from the <c>string[]</c> overload —
    /// <c>string</c> has an implicit conversion to <c>Element</c>, so a collection-expression
    /// call like <c>ComboBox(["A","B"], selectedIndex: 0)</c> would otherwise resolve to either.
    /// </summary>
    public static ComboBoxElement ComboBox(Element[] itemElements, Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged)
    {
        return new([], selectedIndex, onSelectedIndexChanged) { ItemElements = itemElements };
    }

    /// <summary>
    /// Creates a <see cref="SliderElement"/>. <paramref name="value"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the control own its value.
    /// </summary>
    public static SliderElement Slider(Optional<double> value = default, double min = 0, double max = 100, Action<double>? onValueChanged = null)
    {
        return new(value, min, max, onValueChanged);
    }

    /// <summary>
    /// Creates a <see cref="ToggleSwitchElement"/>. <paramref name="isOn"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the control own its on/off state.
    /// </summary>
    public static ToggleSwitchElement ToggleSwitch(Optional<bool> isOn = default, Action<bool>? onIsOnChanged = null, string? onContent = null, string? offContent = null, string? header = null)
    {
        return new(isOn, onIsOnChanged, onContent, offContent) { Header = header };
    }

    /// <summary>
    /// Creates a <see cref="RatingControlElement"/>. <paramref name="value"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the control own its value.
    /// </summary>
    public static RatingControlElement RatingControl(Optional<double> value = default, Action<double>? onValueChanged = null)
    {
        return new(value, onValueChanged);
    }

    /// <summary>
    /// Creates a <see cref="ColorPickerElement"/>. <paramref name="color"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the control own its color.
    /// </summary>
    public static ColorPickerElement ColorPicker(Optional<global::Windows.UI.Color> color = default, Action<global::Windows.UI.Color>? onColorChanged = null)
    {
        return new(color, onColorChanged);
    }

    // ── Date / Time ─────────────────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="CalendarDatePickerElement"/>. <paramref name="date"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the control own its date.
    /// Pass <c>null</c> to explicitly control the date to "no selection".
    /// </summary>
    public static CalendarDatePickerElement CalendarDatePicker(Optional<DateTimeOffset?> date = default, Action<DateTimeOffset?>? onDateChanged = null)
    {
        return new(date, onDateChanged);
    }

    /// <summary>
    /// Creates a <see cref="DatePickerElement"/>. <paramref name="date"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the control own its date.
    /// </summary>
    public static DatePickerElement DatePicker(Optional<DateTimeOffset> date = default, Action<DateTimeOffset>? onDateChanged = null)
    {
        return new(date, onDateChanged);
    }

    /// <summary>
    /// Creates a <see cref="TimePickerElement"/>. <paramref name="time"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the control own its time.
    /// </summary>
    public static TimePickerElement TimePicker(Optional<TimeSpan> time = default, Action<TimeSpan>? onTimeChanged = null)
    {
        return new(time, onTimeChanged);
    }

    // ── Progress ────────────────────────────────────────────────────

    /// <summary>
    /// Creates a determinate <see cref="ProgressElement"/> at the given value.
    /// </summary>
    /// <remarks>
    /// The element reconciles to a WinUI <c>ProgressBar</c>. The Reactor name
    /// <c>Progress</c> is the short, intent-naming spelling — the WinUI name
    /// includes the visual shape (<c>Bar</c>) the way agents reach for a
    /// rendering primitive; Reactor calls it by what it does.
    /// <see cref="ProgressRing(double)"/> is the circular variant.
    /// (spec 039 §5 / §16)
    /// </remarks>
    public static ProgressElement Progress(double value)
    {
        return new(value);
    }

    /// <summary>
    /// Creates an indeterminate <see cref="ProgressElement"/> (animated bar with
    /// no value). The reconciler maps this to <c>ProgressBar.IsIndeterminate</c>.
    /// </summary>
    /// <remarks>
    /// Reactor-original convenience for the indeterminate-bar case; see
    /// <see cref="Progress(double)"/> for the naming rationale. (spec 039 §5 / §16)
    /// </remarks>
    public static ProgressElement ProgressIndeterminate()
    {
        return new(null);
    }

    public static ProgressRingElement ProgressRing()
    {
        return new(null);
    }
    public static ProgressRingElement ProgressRing(double value)
    {
        return new(value);
    }

    // ── Status / Info ───────────────────────────────────────────────

    public static InfoBarElement InfoBar(string? title = null, string? message = null)
    {
        return new(title, message);
    }

    public static InfoBadgeElement InfoBadge()
    {
        return new();
    }
    public static InfoBadgeElement InfoBadge(int value)
    {
        return new() { Value = value };
    }

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
    public static StackElement VStack(params Element?[] children)
    {
        // Spec 048 §3.4 — per-factory `Reg<>` registration touch (Containers group).
        return new(Orientation.Vertical, FilterChildren(children));
    }

    /// <summary>
    /// Creates a vertical <see cref="StackElement"/> with an explicit
    /// <c>Spacing</c> override (the first positional argument).
    /// </summary>
    /// <remarks>
    /// Reactor-original convenience overload — see <see cref="VStack(Element?[])"/>
    /// for the naming rationale.
    /// </remarks>
    public static StackElement VStack(double spacing, params Element?[] children)
    {
        return new(Orientation.Vertical, FilterChildren(children)) { Spacing = spacing };
    }

    /// <summary>
    /// Creates a horizontal <see cref="StackElement"/> (WinUI <c>StackPanel</c>
    /// with <see cref="Orientation.Horizontal"/>). Default <c>Spacing</c> is 8 —
    /// see <see cref="StackElement.Spacing"/>.
    /// </summary>
    /// <remarks>
    /// Reactor-original name — see <see cref="VStack(Element?[])"/> for the
    /// naming rationale. (spec 039 §0.3)
    /// </remarks>
    public static StackElement HStack(params Element?[] children)
    {
        return new(Orientation.Horizontal, FilterChildren(children));
    }

    /// <summary>
    /// Creates a horizontal <see cref="StackElement"/> with an explicit
    /// <c>Spacing</c> override (the first positional argument).
    /// </summary>
    /// <remarks>
    /// Reactor-original convenience overload — see <see cref="VStack(Element?[])"/>
    /// for the naming rationale.
    /// </remarks>
    public static StackElement HStack(double spacing, params Element?[] children)
    {
        return new(Orientation.Horizontal, FilterChildren(children)) { Spacing = spacing };
    }

    public static WrapGridElement WrapGrid(params Element?[] children)
    {
        return new(FilterChildren(children));
    }

    public static WrapGridElement WrapGrid(int maxRowsOrColumns, params Element?[] children)
    {
        return new(FilterChildren(children)) { MaximumRowsOrColumns = maxRowsOrColumns };
    }

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
    public static ScrollViewerElement ScrollViewer(Element child)
    {
        return new(child);
    }

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
    public static ScrollViewElement ScrollView(Element child)
    {
        return new(child);
    }

    public static BorderElement Border(Element? child)
    {
        // Hand-coded handler (not descriptor-backed) — touch its Reg<> directly.
        return new(child!);
    }

    /// <summary>
    /// Creates an <see cref="ExpanderElement"/>. <paramref name="isExpanded"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it and the WinUI control owns its expansion
    /// state (uncontrolled — user clicks persist across rerenders). Pass <c>true</c>/<c>false</c>
    /// (implicit <c>T → Optional&lt;T&gt;</c>) to control it from a parent state.
    /// </summary>
    public static ExpanderElement Expander(string header, Element content, Optional<bool> isExpanded = default, Action<bool>? onIsExpandedChanged = null)
    {
        _ = V1.RegDecorator<ExpanderElement, V1.Handlers.ExpanderHandler>.Done;
        return new(header, content, isExpanded, onIsExpandedChanged);
    }

    public static SplitViewElement SplitView(Element? pane = null, Element? content = null)
    {
        return new(pane, content);
    }

    public static ViewboxElement Viewbox(Element child)
    {
        // Descriptor-only migration (spec 058 §15 / P5.3): the generated
        // descriptor self-registers via ViewboxElement's Pattern-A static cctor,
        // which fires on this `new`. No explicit Reg<> touch needed.
        return new(child);
    }

    public static CanvasElement Canvas(params Element?[] children)
    {
        return new(FilterChildren(children));
    }

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
    public static FlexElement Flex(params Element?[] children)
    {
        return new(FilterChildren(children));
    }

    /// <summary>
    /// Creates a Yoga flexbox container with an explicit direction.
    /// </summary>
    /// <remarks>
    /// Reactor-original — see <see cref="Flex(Element?[])"/> for the rationale.
    /// </remarks>
    public static FlexElement Flex(Microsoft.UI.Reactor.Layout.FlexDirection direction, params Element?[] children)
    {
        return new(FilterChildren(children)) { Direction = direction };
    }

    /// <summary>
    /// Creates a Yoga flexbox container with
    /// <see cref="Microsoft.UI.Reactor.Layout.FlexDirection.Row"/>.
    /// </summary>
    /// <remarks>
    /// Reactor-original convenience for the row-direction flex case — see
    /// <see cref="Flex(Element?[])"/> for the rationale. (spec 039 §0.3)
    /// </remarks>
    public static FlexElement FlexRow(params Element?[] children)
    {
        return new(FilterChildren(children)) { Direction = Microsoft.UI.Reactor.Layout.FlexDirection.Row };
    }

    /// <summary>
    /// Creates a Yoga flexbox container with
    /// <see cref="Microsoft.UI.Reactor.Layout.FlexDirection.Column"/>.
    /// </summary>
    /// <remarks>
    /// Reactor-original convenience for the column-direction flex case — see
    /// <see cref="Flex(Element?[])"/> for the rationale. (spec 039 §0.3)
    /// </remarks>
    public static FlexElement FlexColumn(params Element?[] children)
    {
        return new(FilterChildren(children)) { Direction = Microsoft.UI.Reactor.Layout.FlexDirection.Column };
    }

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

        var oneStar = s_oneStar;
        bool isHorizontal = orientation == Orientation.Horizontal;

        // #172 — exact track/child count is known up front (one track per item
        // plus a separator between each pair), so fill pre-sized arrays directly
        // instead of growing two Lists and copying them with ToArray().
        int trackCount = items.Length * 2 - 1;
        var sizes = new GridSize[trackCount];
        var children = new Element[trackCount];

        for (int i = 0; i < items.Length; i++)
        {
            var starValue = proportions[i];
            sizes[i * 2] = GridSize.Star(starValue);

            children[i * 2] = isHorizontal
                ? items[i].Grid(row: 0, column: i * 2)
                : items[i].Grid(row: i * 2, column: 0);

            if (i < items.Length - 1)
            {
                sizes[i * 2 + 1] = GridSize.Px(separatorSize);
                var sep = separatorFactory(i);
                children[i * 2 + 1] = isHorizontal
                    ? sep.Grid(row: 0, column: i * 2 + 1)
                    : sep.Grid(row: i * 2 + 1, column: 0);
            }
        }

        return isHorizontal
            ? Grid(sizes, oneStar, children)
            : Grid(oneStar, sizes, children);
    }

    /// <summary>
    /// Creates a uniform grid with equal-sized cells along one axis.
    /// Shorthand for a grid where all items share equal proportions with no separators.
    /// </summary>
    public static GridElement UniformGrid(Orientation orientation, params Element?[] items)
    {
        var filtered = FilterChildren(items);
        if (filtered.Length == 0) return Grid(global::System.Array.Empty<GridSize>(), global::System.Array.Empty<GridSize>());

        // #171 — fill the equal-Star track array with a pre-sized loop instead
        // of Enumerable.Repeat(...).ToArray() (which allocates a LINQ iterator on
        // top of the array). GridSize is a value struct, so every slot is the
        // same Star value.
        var sizes = new GridSize[filtered.Length];
        var star = GridSize.Star();
        for (int i = 0; i < sizes.Length; i++)
            sizes[i] = star;
        var oneStar = s_oneStar;
        bool isHorizontal = orientation == Orientation.Horizontal;

        // Position each cell into its OWN array — never write back into
        // `filtered`. FilterChildren's fast path returns the caller's array
        // aliased (no copy), so mutating it in place would corrupt a
        // caller-supplied `items` array with .Grid(...) wrappers. This matches
        // the historical behavior, where FilterChildren always returned a fresh
        // owned array that was safe to fill.
        var positioned = new Element[filtered.Length];
        for (int i = 0; i < filtered.Length; i++)
        {
            positioned[i] = isHorizontal
                ? filtered[i].Grid(row: 0, column: i)
                : filtered[i].Grid(row: i, column: 0);
        }

        return isHorizontal
            ? Grid(sizes, oneStar, positioned)
            : Grid(oneStar, sizes, positioned);
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
        _ = V1.Reg<NavigationHostElement, WinUI.Grid, V1.Handlers.NavigationHostHandler>.Done;
        return new NavigationHostElement(nav, route => routeMap((TRoute)route))
            .Provide(Navigation.NavigationContext<TRoute>.Instance, nav);
    }

    public static NavigationViewElement NavigationView(NavigationViewItemData[] menuItems, Element? content = null)
    {
        return new(menuItems, content);
    }

    public static NavigationViewItemData NavItem(string content, string? icon = null, string? tag = null) =>
        new(content, icon, tag);

    public static NavigationViewItemData NavItemHeader(string content) =>
        new(content) { IsHeader = true };

    public static TitleBarElement TitleBar(string title)
    {
        return new(title);
    }

    public static TabViewElement TabView(params TabViewItemData[] tabs)
    {
        return new(tabs);
    }

    /// <summary>
    /// Creates a tab view whose selected index is unset when <paramref name="selectedIndex"/> is <c>null</c>.
    /// </summary>
    public static TabViewElement TabView(int? selectedIndex, Action<int>? onSelectedIndexChanged = null, params TabViewItemData[] tabs)
    {
        return new(tabs) { SelectedIndex = ToOptionalSelectedIndex(selectedIndex), OnSelectedIndexChanged = onSelectedIndexChanged };
    }

    /// <summary>
    /// Creates a tab view whose selected index is controlled only when <paramref name="selectedIndex"/> has a value.
    /// </summary>
    public static TabViewElement TabView(Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged = null, params TabViewItemData[] tabs)
    {
        return new(tabs) { SelectedIndex = selectedIndex, OnSelectedIndexChanged = onSelectedIndexChanged };
    }

    public static TabViewItemData Tab(string header, Element content) => new(header, content);

    public static BreadcrumbBarElement BreadcrumbBar(BreadcrumbBarItemData[] items, Action<BreadcrumbBarItemData>? onItemClicked = null)
    {
        return new(items, onItemClicked);
    }

    public static BreadcrumbBarItemData Breadcrumb(string label, object? tag = null) => new(label, tag);

    public static PivotElement Pivot(params PivotItemData[] items)
    {
        return new(items);
    }

    /// <summary>
    /// Creates a pivot whose selected index is unset when <paramref name="selectedIndex"/> is <c>null</c>.
    /// </summary>
    public static PivotElement Pivot(int? selectedIndex, Action<int>? onSelectedIndexChanged = null, params PivotItemData[] items)
    {
        return new(items) { SelectedIndex = ToOptionalSelectedIndex(selectedIndex), OnSelectedIndexChanged = onSelectedIndexChanged };
    }

    /// <summary>
    /// Creates a pivot whose selected index is controlled only when <paramref name="selectedIndex"/> has a value.
    /// </summary>
    public static PivotElement Pivot(Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged = null, params PivotItemData[] items)
    {
        return new(items) { SelectedIndex = selectedIndex, OnSelectedIndexChanged = onSelectedIndexChanged };
    }

    public static PivotItemData PivotItem(string header, Element content) => new(header, content);

    // ── Collections ─────────────────────────────────────────────────

    public static ListViewElement ListView(params Element[] items)
    {
        _ = V1.Reg<ListViewElement, WinUI.ListView, V1.Handlers.ListViewHandler>.Done;
        return new(items);
    }

    /// <summary>
    /// Creates a list view whose selected index is unset when <paramref name="selectedIndex"/> is <c>null</c>.
    /// </summary>
    public static ListViewElement ListView(int? selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element[] items)
    {
        _ = V1.Reg<ListViewElement, WinUI.ListView, V1.Handlers.ListViewHandler>.Done;
        return new(items) { SelectedIndex = ToOptionalSelectedIndex(selectedIndex), OnSelectedIndexChanged = onSelectedIndexChanged };
    }

    /// <summary>
    /// Creates a list view whose selected index is controlled only when <paramref name="selectedIndex"/> has a value.
    /// </summary>
    public static ListViewElement ListView(Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element[] items)
    {
        _ = V1.Reg<ListViewElement, WinUI.ListView, V1.Handlers.ListViewHandler>.Done;
        return new(items) { SelectedIndex = selectedIndex, OnSelectedIndexChanged = onSelectedIndexChanged };
    }

    public static GridViewElement GridView(params Element[] items)
    {
        _ = V1.Reg<GridViewElement, WinUI.GridView, V1.Handlers.GridViewHandler>.Done;
        return new(items);
    }

    /// <summary>
    /// Creates a grid view whose selected index is unset when <paramref name="selectedIndex"/> is <c>null</c>.
    /// </summary>
    public static GridViewElement GridView(int? selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element[] items)
    {
        _ = V1.Reg<GridViewElement, WinUI.GridView, V1.Handlers.GridViewHandler>.Done;
        return new(items) { SelectedIndex = ToOptionalSelectedIndex(selectedIndex), OnSelectedIndexChanged = onSelectedIndexChanged };
    }

    /// <summary>
    /// Creates a grid view whose selected index is controlled only when <paramref name="selectedIndex"/> has a value.
    /// </summary>
    public static GridViewElement GridView(Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element[] items)
    {
        _ = V1.Reg<GridViewElement, WinUI.GridView, V1.Handlers.GridViewHandler>.Done;
        return new(items) { SelectedIndex = selectedIndex, OnSelectedIndexChanged = onSelectedIndexChanged };
    }

    public static TreeViewElement TreeView(params TreeViewNodeData[] nodes)
    {
        return new(nodes);
    }

    public static TreeViewNodeData TreeNode(string content, params TreeViewNodeData[] children) =>
        new(content, children.Length > 0 ? children : null);

    /// <summary>
    /// Creates a typed, data-driven <see cref="TemplatedTreeViewElement{T}"/> —
    /// the hierarchical peer of <see cref="ListView{T}(IReadOnlyList{T}, Func{T, string}, Func{T, int, Element})"/>.
    /// The reconciler builds a WinUI <c>TreeView</c> from <paramref name="items"/>,
    /// walks the hierarchy via <paramref name="childrenSelector"/>, and renders
    /// each node via <paramref name="viewBuilder"/> (the <c>ItemTemplate</c>
    /// equivalent — a <c>data → Element</c> function).
    /// </summary>
    /// <remarks>
    /// Heterogeneous nodes with per-shape visuals are expressed as a
    /// <c>switch</c> inside <paramref name="viewBuilder"/> (the C# equivalent of
    /// WinUI's <c>ItemTemplateSelector</c>). This supersedes the deprecated
    /// <see cref="TreeViewNodeData.ContentElement"/> (issue #447). (spec 039 §0.3)
    /// </remarks>
    public static TemplatedTreeViewElement<T> TreeView<T>(
        IReadOnlyList<T> items,
        Func<T, string> keySelector,
        Func<T, IReadOnlyList<T>?> childrenSelector,
        Func<T, Element> viewBuilder)
    {
        // Spec 048 §3.4 — per-factory registration touch. All closed TreeView<T>
        // factories share the TemplatedTreeViewElementBase registry entry.
        _ = V1.RegBaseDecorator<TemplatedTreeViewElementBase, V1.Handlers.TemplatedTreeViewHandler>.Done;
        return new(items, keySelector, childrenSelector, viewBuilder);
    }

    /// <summary>
    /// <see cref="IReactorKeyed"/>-typed overload of
    /// <see cref="TreeView{T}(IReadOnlyList{T}, Func{T, string}, Func{T, IReadOnlyList{T}}, Func{T, Element})"/>;
    /// <c>KeySelector</c> defaults to <c>t =&gt; t.Key</c> so call sites can omit
    /// it. (spec 042 §5)
    /// </summary>
    public static TemplatedTreeViewElement<T> TreeView<T>(
        IReadOnlyList<T> items,
        Func<T, IReadOnlyList<T>?> childrenSelector,
        Func<T, Element> viewBuilder) where T : IReactorKeyed
    {
        _ = V1.RegBaseDecorator<TemplatedTreeViewElementBase, V1.Handlers.TemplatedTreeViewHandler>.Done;
        return new(items, static t => t.Key, childrenSelector, viewBuilder);
    }

    public static FlipViewElement FlipView(params Element[] items)
    {
        return new(items);
    }

    /// <summary>
    /// Creates a flip view whose selected index is unset when <paramref name="selectedIndex"/> is <c>null</c>.
    /// </summary>
    public static FlipViewElement FlipView(int? selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element[] items)
    {
        return new(items) { SelectedIndex = ToOptionalSelectedIndex(selectedIndex), OnSelectedIndexChanged = onSelectedIndexChanged };
    }

    /// <summary>
    /// Creates a flip view whose selected index is controlled only when <paramref name="selectedIndex"/> has a value.
    /// </summary>
    public static FlipViewElement FlipView(Optional<int> selectedIndex, Action<int>? onSelectedIndexChanged = null, params Element[] items)
    {
        return new(items) { SelectedIndex = selectedIndex, OnSelectedIndexChanged = onSelectedIndexChanged };
    }

    // ── Dialogs / Overlays ──────────────────────────────────────────

    public static ContentDialogElement ContentDialog(string title, Element content, string primaryButtonText = "OK")
    {
        // Spec 048 §3.4 — decorator-global-path fan-out (Overlays group).
        _ = V1.RegDecorator<ContentDialogElement, V1.Handlers.ContentDialogHandler>.Done;
        return new(title, content, primaryButtonText);
    }

    public static FlyoutElement Flyout(Element target, Element flyoutContent)
    {
        _ = V1.RegDecorator<FlyoutElement, V1.Handlers.FlyoutHandler>.Done;
        return new(target, flyoutContent);
    }

    public static TeachingTipElement TeachingTip(
        string title,
        string? subtitle = null,
        Microsoft.UI.Reactor.Input.ElementRef? target = null)
    {
        return new(title, subtitle) { Target = target };
    }

    public static ContentFlyoutElement ContentFlyout(Element content, FlyoutPlacementMode placement = FlyoutPlacementMode.Auto) =>
        new(content) { Placement = placement };

    public static MenuFlyoutContentElement MenuItems(params MenuFlyoutItemBase[] items) =>
        new(items);

    public static MenuFlyoutContentElement MenuItems(FlyoutPlacementMode placement, params MenuFlyoutItemBase[] items) =>
        new(items) { Placement = placement };

    // ── Menus ───────────────────────────────────────────────────────

    public static MenuBarElement MenuBar(params MenuBarItemData[] items)
    {
        _ = V1.RegDecorator<MenuBarElement, V1.Handlers.MenuBarHandler>.Done;
        return new(items);
    }

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

    public static MenuFlyoutElement MenuFlyout(Element target, params MenuFlyoutItemBase[] items)
    {
        _ = V1.RegDecorator<MenuFlyoutElement, V1.Handlers.MenuFlyoutHandler>.Done;
        return new(target, items);
    }

    public static CommandBarElement CommandBar(AppBarItemBase[]? primaryCommands = null, AppBarItemBase[]? secondaryCommands = null)
    {
        _ = V1.RegDecorator<CommandBarElement, V1.Handlers.CommandBarHandler>.Done;
        return new(primaryCommands, secondaryCommands);
    }

    public static AppBarButtonData AppBarButton(string label, Action? onClick = null, string? icon = null) => new(label, onClick, icon);

    /// <summary>
    /// Creates an AppBarButton driven by a Command. Maps Label, Icon, Execute,
    /// Accelerator, IsEnabled, AccessKey, and Description.
    /// </summary>
    public static AppBarButtonData AppBarButton(Core.Command command) =>
        new(command.Label, () => Core.CommandBindings.Invoke(command))
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

    public static ImageElement Image(string source)
    {
        return new(source);
    }

    public static PersonPictureElement PersonPicture()
    {
        return new();
    }

    public static WebView2Element WebView2(Uri? source = null)
    {
        return new(source);
    }

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
    /// Define a memoized inline function component. Skips re-render when dependencies haven't changed.
    /// Empty deps array = render once + own state changes only. Non-empty = re-render when any dep changes.
    /// Usage: Memo(ctx => TextBlock("stable"), someProp, otherProp)
    /// </summary>
    public static MemoElement Memo(Func<RenderContext, Element> render, params object?[] dependencies)
        => new(render, dependencies.Length == 0 ? null : dependencies);

    /// <summary>
    /// Opt-in typed keyed memo for virtualized rows (issue #327). Wrap a row body in
    /// <c>Memo(key, () =&gt; …)</c> to assert the body is a <b>pure function of <paramref name="key"/></b>.
    /// Inside a virtualized list (<c>VirtualList</c>, <c>LazyVStack&lt;T&gt;</c>, …) the owning
    /// <see cref="Core.ElementFactory{T}"/> caches the built element per key in a bounded LRU, so a
    /// container recycle that re-asks for the same key returns the <em>same</em> element instance —
    /// the reconciler then skips the row's rebuild + per-row diff entirely (sub-µs). This targets the
    /// fast-scroll cost of variable-height rows, where every recycle otherwise rebuilds and diffs the
    /// row from scratch.
    /// <para>Usage: <c>renderItem: i =&gt; Memo(items[i].Id, () =&gt; Border(BigVariableHeightRow(items[i])))</c></para>
    /// <para><b>Purity is your responsibility.</b> The key must capture every input the factory reads.
    /// If the body also depends on, say, a selection flag, fold it into the key:
    /// <c>Memo((items[i].Id, isSelected), () =&gt; …)</c>. Closing over unkeyed mutable state will serve
    /// stale content. The cache is cleared automatically when the list's items/renderItem change.</para>
    /// <para><b>Apply modifiers inside the factory, not on the wrapper.</b> The cross-recycle cache
    /// only unwraps a <em>bare</em> <c>Memo(key, …)</c> — one with no fluent modifiers, no
    /// <c>.WithKey(…)</c>, and no attached state (Grid/Canvas attached properties, <c>.Provide(…)</c>
    /// context, or theme bindings) on the wrapper itself. Decorating the wrapper
    /// (<c>Memo(id, () =&gt; …).Padding(8)</c>) opts the row out of caching and silently loses the perf
    /// benefit; put modifiers on the element the factory returns instead:
    /// <c>Memo(id, () =&gt; Border(…).Padding(8))</c>.</para>
    /// <para>Outside a virtualized factory the wrapper is transparent but <em>keyed</em>: a re-render
    /// with the same key is a no-op (the factory is not re-invoked and the inner subtree is not
    /// diffed), while a changed key replaces the inner (unmount + fresh mount of the new factory
    /// output). The cross-recycle cache only applies on the virtualized-list path. It is always safe
    /// to use anywhere an element is expected.</para>
    /// </summary>
    /// <typeparam name="TKey">
    /// Key type. Boxed to <see cref="object"/> and compared with <see cref="object.Equals(object)"/> /
    /// <see cref="object.GetHashCode"/>, so value keys (ints, strings, records, value tuples) dedupe by
    /// value. This is what lets the int-index <c>VirtualList</c> path hit the cache.
    /// </typeparam>
    /// <param name="key">Stable identity that fully determines <paramref name="factory"/>'s output.</param>
    /// <param name="factory">Builds the row element. Invoked once per key on a cache miss.</param>
    public static Core.KeyedMemoElement Memo<TKey>(TKey key, Func<Element> factory)
    {
        // Validate up front so a null reference key fails HERE (with the argument name) instead
        // of later as an opaque throw from the KeyedMemoCache dictionary lookup at realize time.
        // Boxing a value-type key yields a non-null object, so int / value-tuple / record keys
        // always pass; the boxed instance is reused for the element, so there is no double-box on
        // the per-row virtualized path.
        object? boxedKey = key;
        global::System.ArgumentNullException.ThrowIfNull(boxedKey, nameof(key));
        global::System.ArgumentNullException.ThrowIfNull(factory);
        return new(boxedKey, factory);
    }

    /// <summary>
    /// Define an inline function component that re-renders on every parent render
    /// (no memoization), keeping its own hook scope. Made explicit so the reader
    /// can tell the always-re-render case apart from a missing deps array.
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
    public static Core.CommandHostElement CommandHost(Core.Command[] commands, Element child)
    {
        // Spec 048 §3.4 — per-factory registration touch.
        _ = V1.RegDecorator<Core.CommandHostElement, V1.Handlers.CommandHostHandler>.Done;
        return new(commands, child);
    }

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
    /// <para>When <typeparamref name="T"/> implements
    /// <see cref="IReactorKeyed"/>, each projected element is keyed from
    /// <c>item.Key</c> unless the projection already set one, so the
    /// reconciler matches rows by identity instead of by position.</para>
    /// </summary>
    public static Element ForEach<T>(IEnumerable<T> items, Func<T, Element> render)
    {
        // #170 — IReadOnlyList fast-path: pre-size the Element[] and index
        // directly, skipping the Select iterator + closure allocations that
        // dominate when re-rendering large data-bound collections every frame.
        if (items is IReadOnlyList<T> list)
        {
            var arr = new Element[list.Count];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = AutoKey(render(list[i]), list[i]);
            return new GroupElement(arr);
        }
        // Build directly rather than `Select(item => AutoKey(render(item), item))`:
        // that lambda captures `render`, so it allocates a display class per call
        // on top of the Select iterator. The pre-#1156 code passed the delegate
        // straight through as `Select(render)` and captured nothing; a manual walk
        // keeps that, and pre-sizes when the source can report a count.
        return new GroupElement(BuildKeyed(items, render));
    }

    static Element[] BuildKeyed<T>(IEnumerable<T> items, Func<T, Element> render)
    {
        // Pre-size from a reported count, then grow or trim if the enumeration
        // disagrees. A count is only a snapshot — a concurrent source can yield
        // a different number of items than it just claimed — and getting that
        // wrong would leave trailing nulls or throw. Same contract as
        // Enumerable.ToArray, without its closure.
        var buffer = items.TryGetNonEnumeratedCount(out var count) && count > 0
            ? new Element[count]
            : new Element[4];
        var at = 0;
        foreach (var item in items)
        {
            if (at == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
            buffer[at++] = AutoKey(render(item), item);
        }
        if (at != buffer.Length) Array.Resize(ref buffer, at);
        return buffer;
    }

    /// <summary>
    /// Map with index:
    ///   ForEach(items, (item, i) =&gt; TextBlock($"{i}: {item}"))
    /// <para>Keys from <see cref="IReactorKeyed"/> items exactly as the
    /// single-parameter overload does.</para>
    /// </summary>
    public static Element ForEach<T>(IEnumerable<T> items, Func<T, int, Element> render)
    {
        if (items is IReadOnlyList<T> list)
        {
            var arr = new Element[list.Count];
            for (int i = 0; i < arr.Length; i++)
                arr[i] = AutoKey(render(list[i], i), list[i]);
            return new GroupElement(arr);
        }
        // Same reasoning as the single-parameter overload: no captured lambda.
        return new GroupElement(BuildKeyedIndexed(items, render));
    }

    static Element[] BuildKeyedIndexed<T>(IEnumerable<T> items, Func<T, int, Element> render)
    {
        // Same pre-size / grow / trim contract as BuildKeyed.
        var buffer = items.TryGetNonEnumeratedCount(out var count) && count > 0
            ? new Element[count]
            : new Element[4];
        var at = 0;
        foreach (var item in items)
        {
            if (at == buffer.Length) Array.Resize(ref buffer, buffer.Length * 2);
            buffer[at] = AutoKey(render(item, at), item);
            at++;
        }
        if (at != buffer.Length) Array.Resize(ref buffer, at);
        return buffer;
    }

    /// <summary>
    /// Spec 042 §5 — identity-on-data. When the item carries its own identity,
    /// key the projected element from it so the reconciler takes the keyed path
    /// without the author repeating <c>.WithKey(item.Key)</c> on every row.
    /// </summary>
    /// <remarks>
    /// Brings <c>ForEach</c> in line with the templated factories, which have
    /// defaulted their key selector to <c>t =&gt; t.Key</c> since Phase 2 (the
    /// 2-arg <c>ListView&lt;T&gt;</c> / <c>GridView&lt;T&gt;</c> /
    /// <c>TreeView&lt;T&gt;</c> overloads constrained
    /// <c>where T : IReactorKeyed</c>). <c>ForEach</c> was simply omitted from
    /// that list, so the same T auto-keyed through <c>ListView</c> but not here.
    /// <para>An explicit key always wins: this only fills a null, so
    /// <c>.WithKey(item.Id)</c> or any deliberate override is untouched.</para>
    /// <para>Deliberately NOT a positional fallback for non-keyed items. An
    /// index key is the position, so it would reproduce positional matching
    /// while forcing the LIS path (<c>ChildReconciler.Reconcile</c> switches on
    /// <c>HasAnyKeys</c>), and sibling <c>ForEach</c> groups flatten into one
    /// parent — so <c>"0"</c>, <c>"1"</c>, … would collide across them and trip
    /// the duplicate-key bailout. <c>REACTOR_DSL_002</c> flags exactly that
    /// shape when an author writes it by hand.</para>
    /// </remarks>
    static Element AutoKey<T>(Element element, T item)
    {
        // SelfKeyingItem<T> hoists the interface test, so the common case — T
        // does not implement IReactorKeyed — costs one branch on a cached bool
        // per item instead of a type test.
        //
        // It does NOT make the keyed path allocation-free: `item is
        // IReactorKeyed` below boxes once per row when T is a struct that
        // implements the interface. Calling an interface member on an
        // unconstrained T cannot avoid that, and a constrained overload is not
        // possible because constraints are not part of the signature. Records
        // are the documented shape for keyed items (spec 042 §5), so that path
        // is rare; ForEach_Keys_From_A_Struct_IReactorKeyed_Item pins that it
        // still produces the right keys.
        if (!SelfKeyingItem<T>.Supported) return element;
        // `Func<T, Element>` is non-nullable, so a null here is already a
        // contract violation — but ChildReconciler.Filter drops null children
        // rather than throwing, and before #1156 the null simply flowed through
        // to that filter. The `!` is that tolerance made explicit: the array
        // element type is non-nullable, exactly like the delegate's return.
        if (element is null) return element!;
        if (element.Key is not null) return element;
        return item is IReactorKeyed keyed ? element with { Key = keyed.Key } : element;
    }

    static class SelfKeyingItem<T>
    {
        internal static readonly bool Supported = typeof(IReactorKeyed).IsAssignableFrom(typeof(T));
    }

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
        Func<T, int, Element> viewBuilder)
    {
        _ = V1.RegBaseDecorator<TemplatedListElementBase, V1.Handlers.TemplatedListHandler>.Done;
        return new(items, keySelector, viewBuilder);
    }

    /// <summary>
    /// Creates a typed, data-driven <see cref="TemplatedListViewElement{T}"/>
    /// for items that implement <see cref="IReactorKeyed"/>. The
    /// <c>KeySelector</c> defaults to <c>t =&gt; t.Key</c> so call sites can
    /// omit it. (spec 042 §5)
    /// </summary>
    public static TemplatedListViewElement<T> ListView<T>(
        IReadOnlyList<T> items,
        Func<T, int, Element> viewBuilder) where T : IReactorKeyed
    {
        _ = V1.RegBaseDecorator<TemplatedListElementBase, V1.Handlers.TemplatedListHandler>.Done;
        return new(items, static t => t.Key, viewBuilder);
    }

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
        Func<T, int, Element> viewBuilder)
    {
        _ = V1.RegBaseDecorator<TemplatedListElementBase, V1.Handlers.TemplatedListHandler>.Done;
        return new(items, keySelector, viewBuilder);
    }

    /// <summary>
    /// <see cref="IReactorKeyed"/>-typed overload of
    /// <see cref="GridView{T}(IReadOnlyList{T}, Func{T, string}, Func{T, int, Element})"/>;
    /// <c>KeySelector</c> defaults to <c>t =&gt; t.Key</c>. (spec 042 §5)
    /// </summary>
    public static TemplatedGridViewElement<T> GridView<T>(
        IReadOnlyList<T> items,
        Func<T, int, Element> viewBuilder) where T : IReactorKeyed
    {
        _ = V1.RegBaseDecorator<TemplatedListElementBase, V1.Handlers.TemplatedListHandler>.Done;
        return new(items, static t => t.Key, viewBuilder);
    }

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
        Func<T, int, Element> viewBuilder)
    {
        _ = V1.RegBaseDecorator<TemplatedListElementBase, V1.Handlers.TemplatedListHandler>.Done;
        return new(items, keySelector, viewBuilder);
    }

    /// <summary>
    /// <see cref="IReactorKeyed"/>-typed overload of
    /// <see cref="FlipView{T}(IReadOnlyList{T}, Func{T, string}, Func{T, int, Element})"/>;
    /// <c>KeySelector</c> defaults to <c>t =&gt; t.Key</c>. (spec 042 §5)
    /// </summary>
    public static TemplatedFlipViewElement<T> FlipView<T>(
        IReadOnlyList<T> items,
        Func<T, int, Element> viewBuilder) where T : IReactorKeyed
    {
        _ = V1.RegBaseDecorator<TemplatedListElementBase, V1.Handlers.TemplatedListHandler>.Done;
        return new(items, static t => t.Key, viewBuilder);
    }

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
        Func<T, int, Element> viewBuilder)
    {
        _ = V1.RegBaseDecorator<LazyStackElementBase, V1.Handlers.LazyStackHandler>.Done;
        return new(items, keySelector, viewBuilder);
    }

    /// <summary>
    /// <see cref="IReactorKeyed"/>-typed overload of
    /// <see cref="LazyVStack{T}(IReadOnlyList{T}, Func{T, string}, Func{T, int, Element})"/>;
    /// <c>KeySelector</c> defaults to <c>t =&gt; t.Key</c>. (spec 042 §5)
    /// </summary>
    public static LazyVStackElement<T> LazyVStack<T>(
        IReadOnlyList<T> items,
        Func<T, int, Element> viewBuilder) where T : IReactorKeyed
    {
        _ = V1.RegBaseDecorator<LazyStackElementBase, V1.Handlers.LazyStackHandler>.Done;
        return new(items, static t => t.Key, viewBuilder);
    }

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
        Func<T, int, Element> viewBuilder)
    {
        _ = V1.RegBaseDecorator<LazyStackElementBase, V1.Handlers.LazyStackHandler>.Done;
        return new(items, keySelector, viewBuilder);
    }

    /// <summary>
    /// <see cref="IReactorKeyed"/>-typed overload of
    /// <see cref="LazyHStack{T}(IReadOnlyList{T}, Func{T, string}, Func{T, int, Element})"/>;
    /// <c>KeySelector</c> defaults to <c>t =&gt; t.Key</c>. (spec 042 §5)
    /// </summary>
    public static LazyHStackElement<T> LazyHStack<T>(
        IReadOnlyList<T> items,
        Func<T, int, Element> viewBuilder) where T : IReactorKeyed
    {
        _ = V1.RegBaseDecorator<LazyStackElementBase, V1.Handlers.LazyStackHandler>.Done;
        return new(items, static t => t.Key, viewBuilder);
    }

    /// <summary>
    /// Creates a virtualized <see cref="ItemsRepeaterElement{T}"/> — a bare
    /// <c>WinUI.ItemsRepeater</c> driven through the spec-042 keyed
    /// realization pipeline (same machinery LazyVStack/LazyHStack use, but
    /// without the implicit <c>ScrollViewer</c> wrap and with no hard-coded
    /// <c>StackLayout</c>). Author supplies a <see cref="Microsoft.UI.Xaml.Controls.Layout"/>
    /// instance via the <c>Layout</c> init-property (typically
    /// <c>UniformGridLayout</c> or <c>LinedFlowLayout</c>); host the result
    /// in a <c>ScrollViewer</c> / <c>ScrollView</c> / <c>RefreshContainer</c>
    /// for scrolling. Spec 047 §14 Phase 3 finish — Port (7).
    /// </summary>
    public static ItemsRepeaterElement<T> ItemsRepeater<T>(
        IReadOnlyList<T> items,
        Func<T, string> keySelector,
        Func<T, int, Element> viewBuilder)
    {
        _ = Desc.ItemsRepeaterDescriptor.Registration.Done;
        return new(items, keySelector, viewBuilder);
    }

    /// <summary>
    /// <see cref="IReactorKeyed"/>-typed overload of
    /// <see cref="ItemsRepeater{T}(IReadOnlyList{T}, Func{T, string}, Func{T, int, Element})"/>;
    /// <c>KeySelector</c> defaults to <c>t =&gt; t.Key</c>.
    /// </summary>
    public static ItemsRepeaterElement<T> ItemsRepeater<T>(
        IReadOnlyList<T> items,
        Func<T, int, Element> viewBuilder) where T : IReactorKeyed
    {
        _ = Desc.ItemsRepeaterDescriptor.Registration.Done;
        return new(items, static t => t.Key, viewBuilder);
    }

    // ── Shapes ───────────────────────────────────────────────────────

    public static RectangleElement Rectangle()
    {
        return new();
    }

    public static EllipseElement Ellipse()
    {
        return new();
    }

    public static LineElement Line(double x1, double y1, double x2, double y2)
    {
        return new() { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
    }

    // Named `Path2D` (not `Path`) to avoid colliding with `System.IO.Path`.
    // Models reach for both in the same file and the bare name causes
    // CS0119 cascades. Borrows the Web Canvas API's `Path2D` spelling for the
    // vector-geometry primitive — collision-free and familiar from JS/SVG.
    public static PathElement Path2D()
    {
        return new();
    }

    // ── Additional layout ───────────────────────────────────────────

    public static RelativePanelElement RelativePanel(params Element?[] children)
    {
        return new(FilterChildren(children));
    }

    // ── Additional media ────────────────────────────────────────────

    public static MediaPlayerElementElement MediaPlayerElement(string? source = null)
    {
        return new(source);
    }

    public static AnimatedVisualPlayerElement AnimatedVisualPlayer()
    {
        return new();
    }

    // ── Additional collections ──────────────────────────────────────

    public static SemanticZoomElement SemanticZoom(Element zoomedInView, Element zoomedOutView)
    {
        return new(zoomedInView, zoomedOutView);
    }

    /// <summary>
    /// Creates a <see cref="ListBoxElement"/>. <paramref name="selectedIndex"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the control own its selection.
    /// </summary>
    public static ListBoxElement ListBox(string[] items, Optional<int> selectedIndex = default, Action<int>? onSelectedIndexChanged = null)
    {
        return new(items) { SelectedIndex = selectedIndex, OnSelectedIndexChanged = onSelectedIndexChanged };
    }

    // ── Additional navigation ───────────────────────────────────────

    /// <summary>
    /// Creates a <see cref="SelectorBarElement"/>. <paramref name="selectedIndex"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the control own its selection.
    /// </summary>
    public static SelectorBarElement SelectorBar(SelectorBarItemData[] items, Optional<int> selectedIndex = default, Action<int>? onSelectedIndexChanged = null)
    {
        return new(items) { SelectedIndex = selectedIndex, OnSelectedIndexChanged = onSelectedIndexChanged };
    }

    public static SelectorBarItemData SelectorBarItem(string text, string? icon = null) => new(text, icon);

    /// <summary>
    /// Creates a <see cref="PipsPagerElement"/>. <paramref name="selectedPageIndex"/> defaults to
    /// <see cref="Optional{T}.Unset"/> — omit it to let the control own its current page.
    /// </summary>
    public static PipsPagerElement PipsPager(int numberOfPages, Optional<int> selectedPageIndex = default, Action<int>? onSelectedPageIndexChanged = null)
    {
        return new(numberOfPages) { SelectedPageIndex = selectedPageIndex, OnSelectedPageIndexChanged = onSelectedPageIndexChanged };
    }

    public static AnnotatedScrollBarElement AnnotatedScrollBar()
    {
        return new();
    }

    // ── Additional overlays / containers ────────────────────────────

    public static PopupElement Popup(Element child, bool isOpen = false, Action? onClosed = null)
    {
        _ = V1.RegDecorator<PopupElement, V1.Handlers.PopupHandler>.Done;
        return new(child) { IsOpen = isOpen, OnClosed = onClosed };
    }

    public static RefreshContainerElement RefreshContainer(Element content, Action? onRefreshRequested = null)
    {
        return new(content) { OnRefreshRequested = onRefreshRequested };
    }

    public static CommandBarFlyoutElement CommandBarFlyout(Element target, AppBarItemBase[]? primaryCommands = null, AppBarItemBase[]? secondaryCommands = null)
    {
        _ = V1.RegDecorator<CommandBarFlyoutElement, V1.Handlers.CommandBarFlyoutHandler>.Done;
        return new(target, primaryCommands, secondaryCommands);
    }

    // ── Additional date / time ──────────────────────────────────────

    public static CalendarViewElement CalendarView()
    {
        return new();
    }

    // ── SwipeControl ────────────────────────────────────────────────

    public static SwipeControlElement SwipeControl(Element content,
        SwipeItemData[]? leftItems = null, SwipeItemData[]? rightItems = null)
    {
        return new(content) { LeftItems = leftItems, RightItems = rightItems };
    }

    // ── AnimatedIcon ────────────────────────────────────────────────

    public static AnimatedIconElement AnimatedIcon(object? source = null, IconSource? fallbackIconSource = null)
    {
        return new() { Source = source, FallbackIconSource = fallbackIconSource };
    }

    // ── ParallaxView ────────────────────────────────────────────────

    public static ParallaxViewElement ParallaxView(Element child, double verticalShift = 0, double horizontalShift = 0)
    {
        return new(child) { VerticalShift = verticalShift, HorizontalShift = horizontalShift };
    }

    // ── MapControl ──────────────────────────────────────────────────

    public static MapControlElement MapControl(string? mapServiceToken = null, double zoomLevel = 1)
    {
        return new() { MapServiceToken = mapServiceToken, ZoomLevel = zoomLevel };
    }

    // ── Frame ───────────────────────────────────────────────────────

    /// <summary>
    /// A WinUI <c>Frame</c> that navigates to <paramref name="sourcePageType"/> once on mount.
    ///
    /// <para><c>Frame</c> is for interop with pages that already have a <c>.xaml</c> file. A
    /// <c>Page</c> declared only in C# is absent from the XAML metadata the compiler generates,
    /// so WinUI cannot resolve it; Reactor refuses such a navigation and reports it through
    /// <c>.NavigationFailed(...)</c> rather than letting it fault. For navigation inside a
    /// Reactor app use <c>UseNavigation&lt;TRoute&gt;</c> with <c>NavigationHost</c>.</para>
    /// </summary>
    public static FrameElement Frame(
        Type? sourcePageType = null,
        object? navigationParameter = null)
    {
        return new() { SourcePageType = sourcePageType, NavigationParameter = navigationParameter };
    }

    // ── ItemContainer ───────────────────────────────────────────────

    /// <summary>
    /// Wraps a child element in a WinUI <c>ItemContainer</c>. Required as
    /// the root element returned from an <see cref="ItemsViewElement{T}"/>
    /// view builder — ItemsView's selection / focus / animation
    /// infrastructure depends on it.
    /// </summary>
    public static ItemContainerElement ItemContainer(Element? child)
    {
        return new(child);
    }

    // ── ItemsView ───────────────────────────────────────────────────

    public static ItemsViewElement<T> ItemsView<T>(
        IReadOnlyList<T> items,
        Func<T, string> keySelector,
        Func<T, int, Element> viewBuilder)
    {
        _ = Desc.ItemsViewDescriptor.Registration.Done;
        return new(items, keySelector, viewBuilder);
    }

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
    public static RichTextBlockElement RichTextBlock(RichTextParagraph[] paragraphs)
    {
        return new("") { Paragraphs = paragraphs };
    }

    public static RichTextParagraph Paragraph(params RichTextInline[] inlines) => new(inlines);

    public static RichTextRun Run(string text) => new(text);

    public static RichTextHyperlink Hyperlink(string text, Uri navigateUri) => new(text, navigateUri);

    /// <summary>
    /// Issue #479 — clickable inline hyperlink: fires <paramref name="onClick"/>
    /// on click and suppresses platform navigation. Use for in-flow interactive
    /// fragments (open a flyout, enter edit mode) inside a virtualized list of
    /// <see cref="RichTextBlock(RichTextParagraph[])"/> rows where giving each
    /// fragment its own <c>UIElement</c> would be too heavy.
    /// </summary>
    public static RichTextHyperlink Hyperlink(string text, Action onClick)
    {
        global::System.ArgumentNullException.ThrowIfNull(onClick);
        // Sentinel URI: ignored at mount time when OnClick is non-null
        // (Reconciler skips NavigateUri assignment in click mode).
        return new(text, new Uri("about:blank")) { OnClick = onClick };
    }

    /// <summary>
    /// Issue #480 — embeds a Reactor <paramref name="child"/> element inline
    /// inside a <see cref="RichTextBlock(RichTextParagraph[])"/>. Mirrors
    /// WinUI's <c>InlineUIContainer</c>. The child is reconciled as any
    /// other Reactor element — descendant hooks, event wiring, and
    /// pooling all work as expected. Re-renders use the incremental update
    /// path: structurally compatible inlines are mutated in place and
    /// embedded children retain their WinUI control identity (Slider drag,
    /// focus, and animation state survive). Only structural changes
    /// (paragraph count change, inline type change, Route A↔B swap) fall
    /// back to a full block rebuild.
    /// </summary>
    public static RichTextInlineUIContainer InlineUI(Element child) =>
        new() { Child = child };

    /// <summary>
    /// Issue #480 — embeds a native WinUI control (produced by
    /// <paramref name="factory"/>) inline inside a
    /// <see cref="RichTextBlock(RichTextParagraph[])"/>. Escape hatch for
    /// controls without a Reactor element counterpart. The factory is
    /// re-invoked only when its delegate identity changes; passing the
    /// same factory across renders preserves the control instance.
    /// </summary>
    public static RichTextInlineUIContainer InlineUI(Func<FrameworkElement> factory) =>
        new() { Factory = factory };

    // ── Icons ────────────────────────────────────────────────────────

    public static SymbolIconData SymbolIcon(string symbol) => new(symbol);

    public static FontIconData FontIcon(string glyph, string? fontFamily = null, double? fontSize = null) =>
        new(glyph, fontFamily, fontSize);

    public static BitmapIconData BitmapIcon(global::System.Uri source, bool showAsMonochrome = true) =>
        new(source, showAsMonochrome);

    public static PathIconData PathIcon(string data) => new(data);

    public static ImageIconData ImageIcon(global::System.Uri source) => new(source);

    /// <summary>Creates a standalone icon element from an <see cref="IconData"/> instance.</summary>
    // IconElement migrated to a generated polymorphic descriptor (spec 058 §15 / P5.27):
    // its Pattern-A static cctor (emitted by [WrapPolymorphic]) registers the decorator
    // on first instantiation, so `new(...)` self-registers — no IconRegistration shim.
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
    /// <code>var brush = ctx.UseMemo(() => AcrylicBrush(color, 0.8), color);</code>
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

    /// <summary>
    /// Flattens one level of <see cref="GroupElement"/> and drops
    /// <see langword="null"/> / <see cref="EmptyElement"/> entries, returning the
    /// children array stored on a container element.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Array-ownership contract.</b> The no-expansion fast path below returns
    /// the caller's <see cref="Element"/>[] instance <i>directly</i> (no copy) for
    /// zero per-render allocation. The returned array becomes the container
    /// element's <c>Children</c>, and container <c>ShallowEquals</c> gates the
    /// reconciler skip-path on <c>ReferenceEquals(Children)</c>. Therefore a
    /// caller <b>must not retain and then mutate</b> an <see cref="Element"/>[]
    /// it passes to a container factory (<c>VStack</c>,
    /// <c>HStack</c>, <c>Grid</c>, <c>Group</c>,
    /// <c>FlexRow</c>, <c>FlexColumn</c>, …). Mutating that buffer
    /// after the call would rewrite the <i>previous</i> tree's children in place
    /// and corrupt the <c>ReferenceEquals</c>-based skip decision. The idiomatic
    /// pattern — passing a fresh <c>params</c> array per render — is always safe.
    /// </para>
    /// <para>
    /// In-framework container factories honor this by never writing back into a
    /// filtered/caller array: cell-positioning factories
    /// (<c>UniformGrid</c>, <c>InterspersedGrid</c>) build their
    /// <c>.Grid(...)</c> wrappers into a separate owned array. A Release-path
    /// defensive clone is intentionally <b>not</b> taken — it would reintroduce
    /// the per-render allocation #173 removed for the common case.
    /// </para>
    /// </remarks>
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
        // Fast path: no nulls / GroupElement / EmptyElement → return the caller's
        // array instance directly (zero-copy). See the array-ownership contract
        // in this method's <remarks>: callers must not retain and then mutate an
        // array passed to a container factory.
        if (!needsExpansion) return (Element[])(object)children;

        // #173 — slow path: two passes (count, then fill an exactly-sized array)
        // instead of growing a List and copying it with ToArray(). Flattens one
        // level of GroupElement and drops null / EmptyElement, matching the prior
        // behavior exactly.
        int count = 0;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is GroupElement group)
            {
                foreach (var gc in group.Children)
                {
                    if (gc is not null and not EmptyElement)
                        count++;
                }
            }
            else if (children[i] is not null and not EmptyElement)
            {
                count++;
            }
        }

        var result = new Element[count];
        int idx = 0;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] is GroupElement group)
            {
                foreach (var gc in group.Children)
                {
                    if (gc is not null and not EmptyElement)
                        result[idx++] = gc;
                }
            }
            else if (children[i] is not null and not EmptyElement)
            {
                result[idx++] = children[i]!;
            }
        }
        return result;
    }
}
