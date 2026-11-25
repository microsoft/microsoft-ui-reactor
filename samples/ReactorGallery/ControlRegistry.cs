using System;
using System.Collections.Generic;
using System.Linq;

namespace WinUIGalleryReactor;

public record ControlInfo(string Title, string Description, string Category, string Tag, string ImageFile = "Placeholder.png")
{
    /// <summary>Path for use with Reactor Image() element.</summary>
    public string ImagePath => $"ms-appx:///Assets/ControlImages/{ImageFile}";
}

public static class ControlRegistry
{
    public static ControlInfo[] All { get; } = new ControlInfo[]
    {
        // Basic Input
        new("Button", "A button that responds to user clicks.", "Basic Input", "button", "Button.png"),
        new("CheckBox", "A control that a user can select or clear.", "Basic Input", "check-box", "Checkbox.png"),
        new("ColorPicker", "A control that lets a user pick a color.", "Basic Input", "color-picker", "ColorPicker.png"),
        new("ComboBox", "A drop-down list of items a user can select from.", "Basic Input", "combo-box", "ComboBox.png"),
        new("DropDownButton", "A button that displays a flyout of choices when clicked.", "Basic Input", "drop-down-button", "DropDownButton.png"),
        new("HyperlinkButton", "A button that appears as a hyperlink and navigates to a URI.", "Basic Input", "hyperlink-button", "HyperlinkButton.png"),
        new("NumberBox", "A text control for entering numeric values with validation.", "Basic Input", "number-box", "NumberBox.png"),
        new("PasswordBox", "A text input that conceals typed characters for secure entry.", "Basic Input", "password-box", "PasswordBox.png"),
        new("RadioButton", "A button that allows a user to select one option from a group.", "Basic Input", "radio-button", "RadioButton.png"),
        new("RatingControl", "A control that lets users provide a star rating.", "Basic Input", "rating-control", "RatingControl.png"),
        new("RepeatButton", "A button that raises click events repeatedly while pressed.", "Basic Input", "repeat-button", "RepeatButton.png"),
        new("Slider", "A control that lets the user select from a range of values by moving a thumb.", "Basic Input", "slider", "Slider.png"),
        new("SplitButton", "A button with two parts: a primary action and a flyout menu.", "Basic Input", "split-button", "SplitButton.png"),
        new("TextBox", "A single-line or multi-line plain text input field.", "Basic Input", "text-box", "TextBox.png"),
        new("ToggleButton", "A button that can be toggled between two states.", "Basic Input", "toggle-button", "ToggleButton.png"),
        new("ToggleSwitch", "A switch that toggles between two mutually exclusive states.", "Basic Input", "toggle-switch", "ToggleSwitch.png"),

        // Collections
        new("FlipView", "A control that presents one item at a time with flipping navigation.", "Collections", "flip-view", "FlipView.png"),
        new("GridView", "A control that displays items in a horizontally wrapping grid.", "Collections", "grid-view", "GridView.png"),
        new("ItemsView", "A flexible control for displaying collections of data items.", "Collections", "items-view", "ItemsView.png"),
        new("ListBox", "A list of selectable items presented inline.", "Collections", "list-box", "ListBox.png"),
        new("ListView", "A control that displays items in a vertical scrolling list.", "Collections", "list-view", "ListView.png"),
        new("TreeView", "A hierarchical list with expanding and collapsing nodes.", "Collections", "tree-view", "TreeView.png"),

        // Date and Time
        new("CalendarDatePicker", "A drop-down control that lets a user pick a date from a calendar.", "Date and Time", "calendar-date-picker", "CalendarDatePicker.png"),
        new("CalendarView", "A calendar display that lets a user select a date.", "Date and Time", "calendar-view", "CalendarView.png"),
        new("DatePicker", "A control that lets a user pick a date using spinners.", "Date and Time", "date-picker", "DatePicker.png"),
        new("TimePicker", "A control that lets a user pick a time using spinners.", "Date and Time", "time-picker", "TimePicker.png"),

        // Dialogs and Flyouts
        new("CommandBarFlyout", "A flyout that provides quick access to common commands.", "Dialogs and Flyouts", "command-bar-flyout", "CommandBarFlyout.png"),
        new("ContentDialog", "A modal dialog box that displays content and action buttons.", "Dialogs and Flyouts", "content-dialog", "ContentDialog.png"),
        new("Flyout", "A lightweight popup that shows contextual information.", "Dialogs and Flyouts", "flyout", "Flyout.png"),
        new("MenuFlyout", "A flyout that displays a list of menu commands.", "Dialogs and Flyouts", "menu-flyout", "MenuFlyout.png"),
        new("TeachingTip", "A notification flyout for guiding users through features.", "Dialogs and Flyouts", "teaching-tip", "TeachingTip.png"),

        // Layout
        new("Border", "A container that draws a border around its child element.", "Layout", "border", "Border.png"),
        new("Card", "A theme-aware preset Border with the WinUI card brushes, corner radius, and padding.", "Layout", "card", "Border.png"),
        new("Canvas", "A layout panel that supports absolute positioning of child elements.", "Layout", "canvas", "Canvas.png"),
        new("Expander", "A container that expands and collapses to show or hide content.", "Layout", "expander", "Expander.png"),
        new("Flex", "A flexible layout container inspired by CSS flexbox.", "Layout", "flex", "Grid.png"),
        new("Grid", "A layout panel that arranges children in rows and columns.", "Layout", "grid", "Grid.png"),
        new("RelativePanel", "A panel that positions children relative to each other.", "Layout", "relative-panel", "RelativePanel.png"),
        new("ScrollView", "A scrollable container for content that exceeds available space.", "Layout", "scroll-view", "ScrollView.png"),
        new("SplitView", "A container with a collapsible pane and a content area.", "Layout", "split-view", "SplitView.png"),
        new("StackPanel", "A panel that arranges children in a single horizontal or vertical line.", "Layout", "stack-panel", "StackPanel.png"),
        new("Viewbox", "A container that scales its child to fill available space.", "Layout", "viewbox", "Viewbox.png"),
        new("WrapGrid", "A grid that wraps items to the next row or column automatically.", "Layout", "wrap-grid", "VariableSizedWrapGrid.png"),

        // Media
        new("Image", "A control that displays an image from a file or URI.", "Media", "image", "Image.png"),
        new("PersonPicture", "A control that displays a circular avatar for a person.", "Media", "person-picture", "PersonPicture.png"),
        new("WebView2", "A control that hosts web content using the Edge rendering engine.", "Media", "web-view-2", "WebView.png"),

        // Menus and Toolbars
        new("CommandBar", "A toolbar for exposing app commands and actions.", "Menus and Toolbars", "command-bar", "CommandBar.png"),
        new("MenuBar", "A horizontal bar that hosts a set of drop-down menus.", "Menus and Toolbars", "menu-bar", "MenuBar.png"),
        new("SelectorBar", "A bar that lets users switch between different views or modes.", "Menus and Toolbars", "selector-bar", "Pivot.png"),

        // Navigation
        new("BreadcrumbBar", "A trail of links showing the user's navigation path.", "Navigation", "breadcrumb-bar", "BreadcrumbBar.png"),
        new("Frame", "A container that hosts Page navigation, raising .Navigated / .Navigating / .NavigationFailed.", "Navigation", "frame", "PageTransition.png"),
        new("NavigationView", "A side or top navigation pane for app-level navigation.", "Navigation", "navigation-view", "NavigationView.png"),
        new("Pivot", "A tabbed interface for switching between content sections.", "Navigation", "pivot", "Pivot.png"),
        new("TabView", "A control that displays a set of closable, rearrangeable tabs.", "Navigation", "tab-view", "TabView.png"),
        new("TitleBar", "A customizable title bar for the application window.", "Navigation", "title-bar", "TitleBar.png"),

        // Status and Info
        new("InfoBadge", "A small indicator that conveys status on another element.", "Status and Info", "info-badge", "InfoBadge.png"),
        new("InfoBar", "A dismissible bar for displaying essential app-level messages.", "Status and Info", "info-bar", "InfoBar.png"),
        new("ProgressBar", "A horizontal bar that shows progress of an operation.", "Status and Info", "progress-bar", "ProgressBar.png"),
        new("ProgressRing", "A circular indicator that shows ongoing progress.", "Status and Info", "progress-ring", "ProgressRing.png"),
        new("ToolTip", "A popup that displays helpful text when hovering over an element.", "Status and Info", "tool-tip", "ToolTip.png"),

        // Text
        new("AutoSuggestBox", "A text input that shows suggestions as the user types.", "Text", "auto-suggest-box", "AutoSuggestBox.png"),
        new("RichEditBox", "A rich text editing control with formatting support.", "Text", "rich-edit-box", "RichEditBox.png"),
        new("RichTextBlock", "A control that displays formatted read-only rich text.", "Text", "rich-text-block", "RichTextBlock.png"),
        new("Type ramp", "WinUI 3 type ramp factories \u2014 Title, Subtitle, Body, BodyStrong, BodyLarge, TitleLarge, Display.", "Text", "type-ramp", "TextBlock.png"),

        // Styles
        new("Acrylic", "A translucent material brush that creates a frosted glass effect.", "Styles", "acrylic", "Acrylic.png"),

        // Design
        new("Color", "Guidance on using the WinUI color system and theme resources.", "Design", "color", "ColorPaletteResources.png"),
        new("Geometry", "Corner radius resources for consistent rounding on controls and overlays.", "Design", "geometry", "Shape.png"),
        new("Spacing", "Margin, padding, and stack spacing for consistent layout rhythm.", "Design", "spacing", "CompactSizing.png"),
        new("Theming", "Guidance on applying light, dark, and high-contrast themes.", "Design", "theming", "ThemeTransition.png"),
        new("Typography", "Guidance on typographic styles, font ramps, and text hierarchy.", "Design", "typography", "TextBlock.png"),

        // ── Added: controls surfaced from recent Reactor additions ──
        new("RadioButtons", "A group of radio buttons where a user selects a single option.", "Basic Input", "radio-buttons", "RadioButton.png"),
        new("ToggleSplitButton", "A split button whose primary part toggles on and off.", "Basic Input", "toggle-split-button", "ToggleSplitButton.png"),

        new("ItemsRepeater", "A lightweight, data-driven panel for laying out custom collections.", "Collections", "items-repeater", "ListView.png"),
        new("SemanticZoom", "A control that switches between a zoomed-in and zoomed-out view of data.", "Collections", "semantic-zoom", "SemanticZoom.png"),
        new("SwipeControl", "A container that reveals swipe commands on its content.", "Collections", "swipe-control", "SwipeControl.png"),
        new("RefreshContainer", "A pull-to-refresh container for scrollable content.", "Collections", "refresh-container", "PullToRefresh.png"),

        new("UniformGrid", "A panel that arranges children in evenly sized cells.", "Layout", "uniform-grid", "Grid.png"),
        new("InterspersedGrid", "A grid that places proportionally sized panels with separators between them.", "Layout", "interspersed-grid", "Grid.png"),
        new("AnnotatedScrollBar", "A scrollbar that shows labelled annotations along its track.", "Layout", "annotated-scroll-bar", "AnnotatedScrollBar.png"),
        new("Docking", "Dockable, tearable, and floatable panes with drag-to-rearrange layout.", "Layout", "docking", "LayoutPanel.png"),

        new("MediaPlayerElement", "Embeds audio and video playback with built-in transport controls.", "Media", "media-player-element", "MediaPlayerElement.png"),
        new("AnimatedVisualPlayer", "Plays vector (Lottie/codegen) animations.", "Media", "animated-visual-player", "AnimatedVisualPlayer.png"),
        new("AnimatedIcon", "An icon that transitions between states with a vector animation.", "Media", "animated-icon", "AnimatedIcon.png"),
        new("MapControl", "Displays an interactive, pannable, zoomable map.", "Media", "map-control", "MapControl.png"),
        new("ParallaxView", "Shifts a background layer as content scrolls for a depth effect.", "Media", "parallax-view", "ParallaxView.png"),
        new("Shapes", "Rectangle, Ellipse, Line, and Path vector primitives.", "Media", "shapes", "Shape.png"),
        new("Icons", "Standalone icon elements from Symbol, Font glyph, or Path sources.", "Media", "icons", "IconElement.png"),

        new("PipsPager", "A pager that represents pages as a row of selectable dots.", "Navigation", "pips-pager", "PipsPager.png"),

        new("Popup", "A lightweight overlay that displays arbitrary content over the UI.", "Dialogs and Flyouts", "popup", "Popup.png"),

        new("Markdown", "Renders a Markdown string as a Reactor element tree.", "Text", "markdown", "RichTextBlock.png"),

        new("DataGrid", "A virtualized data grid with sortable columns, selection, and inline editing.", "Data", "data-grid", "DataGrid.png"),
        new("PropertyGrid", "A reflection-driven editor for an object's properties.", "Data", "property-grid", "DataGrid.png"),

        new("Commands", "Declarative Command binding with async IsExecuting tracking and debounce.", "Patterns", "commands", "AppBarButton.png"),
        new("ErrorBoundary", "Catch an exception thrown while a subtree renders and show a fallback in its place.", "Patterns", "error-boundary", "CodeTagIcon.png"),

        // Motion
        new("Connected Animation", "Carries an element's visual from one view into another across a state change.", "Motion", "connected-animation", "ConnectedAnimation.png"),
        new("Transitions", "Implicit opacity, scale, and translation transitions that run on value change, plus layout animation for layout-driven position changes.", "Motion", "transitions", "ImplicitTransition.png"),

        // ── Fundamentals — framework mechanics, not controls (issue #1275) ──
        new("UseState", "Component state \u2014 a value plus a setter that schedules a re-render.", "Fundamentals", "use-state", "CodeTagIcon.png"),
        new("UseEffect", "Side effects and lifecycle \u2014 run after render, clean up on unmount.", "Fundamentals", "use-effect", "CodeTagIcon.png"),
        new("UseReducer", "State derived from the previous value \u2014 the hook collections need.", "Fundamentals", "use-reducer", "CodeTagIcon.png"),
        new("UseMemo", "Memoized computations and stable callbacks, via UseMemo and UseCallback.", "Fundamentals", "use-memo", "CodeTagIcon.png"),
        new("UseRef", "A mutable box that survives re-renders without triggering one.", "Fundamentals", "use-ref", "CodeTagIcon.png"),
        new("Context", "Tree-scoped ambient state, read without threading it through every component.", "Fundamentals", "context", "CodeTagIcon.png"),
        new("Element refs", "Refs to realized WinUI elements \u2014 imperative focus and reference props.", "Fundamentals", "element-refs", "CodeTagIcon.png"),
        new("Keyboard input", "Key events and VirtualKey, and where a Command accelerator belongs instead.", "Fundamentals", "keyboard-input", "CodeTagIcon.png"),
        new("Pointer and gestures", "Pointer and tap events plus continuous pan, pinch, and rotate gestures.", "Fundamentals", "pointer-input", "CodeTagIcon.png"),

        // System
        new("File pickers", "Native open, multi-select, folder, and save dialogs owned by the component's window.", "System", "file-pickers", "StoragePickers.png"),
    }
    .OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
    .ToArray();

    public static string[] Categories { get; } = new[]
    {
        "Fundamentals",
        "Basic Input",
        "Collections",
        "Data",
        "Date and Time",
        "Dialogs and Flyouts",
        "Layout",
        "Media",
        "Menus and Toolbars",
        "Motion",
        "Navigation",
        "Status and Info",
        "System",
        "Text",
        "Patterns",
        "Design",
        "Styles",
    };

    /// <summary>
    /// Categories that hold framework topics and design guidance rather than controls.
    /// Single source of truth so the shell nav, the category header, and the home page cannot
    /// disagree about whether "9 controls" is an honest label.
    /// </summary>
    public static string[] NonControlCategories { get; } = new[] { "Fundamentals", "Design", "System" };

    /// <summary>
    /// UI label for a category, in WinUI Gallery's sentence-case style ("Date &amp; time").
    /// The <see cref="Categories"/> keys stay unchanged because they also produce the
    /// deep-link slugs and the search-index category.
    /// </summary>
    public static string CategoryDisplayName(string category) => category switch
    {
        "Basic Input" => "Basic input",
        "Date and Time" => "Date & time",
        "Dialogs and Flyouts" => "Dialogs & flyouts",
        "Menus and Toolbars" => "Menus & toolbars",
        "Status and Info" => "Status & info",
        _ => category,
    };

    static readonly Dictionary<string, string> CategoryGlyphs = new()
    {
        ["Basic Input"] = "\uE73A",
        ["Collections"] = "\uE80A",
        ["Data"] = "\uE7C3",
        ["Date and Time"] = "\uEC92",
        ["Dialogs and Flyouts"] = "\uE15F",
        ["Fundamentals"] = "\uE8F1",
        ["Layout"] = "\uE8A1",
        ["Media"] = "\uE173",
        ["Menus and Toolbars"] = "\uE74E",
        ["Navigation"] = "\uE700",
        ["Status and Info"] = "\uE8F2",
        ["System"] = "\uE770",
        ["Text"] = "\uE8D2",
        ["Patterns"] = "\uE943",
        ["Styles"] = "\uE2B1",
        ["Design"] = "\uEB3C",
        ["Motion"] = "\uE945",
    };

    /// <summary>Segoe Fluent Icons glyph for a category, shared by the nav and the home cards.</summary>
    public static string CategoryGlyph(string category) =>
        CategoryGlyphs.GetValueOrDefault(category, "\uE71D");

    public static bool IsControlCategory(string category) =>
        !NonControlCategories.Contains(category, StringComparer.Ordinal);

    public static ControlInfo[] Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return All;

        return All
            .Where(c =>
                c.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
