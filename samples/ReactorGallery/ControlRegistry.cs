using System;
using System.Linq;

namespace WinUIGalleryReactor;

public record ControlInfo(string Title, string Description, string Category, string IconGlyph, string Tag, string ImageFile = "Placeholder.png")
{
    /// <summary>Path for use with Reactor Image() element.</summary>
    public string ImagePath => $"ms-appx:///Assets/ControlImages/{ImageFile}";
}

public static class ControlRegistry
{
    public static ControlInfo[] All { get; } = new ControlInfo[]
    {
        // Basic Input
        new("Button", "A button that responds to user clicks.", "Basic Input", "\uE73A", "button", "Button.png"),
        new("CheckBox", "A control that a user can select or clear.", "Basic Input", "\uE73A", "check-box", "Checkbox.png"),
        new("ColorPicker", "A control that lets a user pick a color.", "Basic Input", "\uE73A", "color-picker", "ColorPicker.png"),
        new("ComboBox", "A drop-down list of items a user can select from.", "Basic Input", "\uE73A", "combo-box", "ComboBox.png"),
        new("DropDownButton", "A button that displays a flyout of choices when clicked.", "Basic Input", "\uE73A", "drop-down-button", "DropDownButton.png"),
        new("HyperlinkButton", "A button that appears as a hyperlink and navigates to a URI.", "Basic Input", "\uE73A", "hyperlink-button", "HyperlinkButton.png"),
        new("NumberBox", "A text control for entering numeric values with validation.", "Basic Input", "\uE73A", "number-box", "NumberBox.png"),
        new("PasswordBox", "A text input that conceals typed characters for secure entry.", "Basic Input", "\uE73A", "password-box", "PasswordBox.png"),
        new("RadioButton", "A button that allows a user to select one option from a group.", "Basic Input", "\uE73A", "radio-button", "RadioButton.png"),
        new("RatingControl", "A control that lets users provide a star rating.", "Basic Input", "\uE73A", "rating-control", "RatingControl.png"),
        new("RepeatButton", "A button that raises click events repeatedly while pressed.", "Basic Input", "\uE73A", "repeat-button", "RepeatButton.png"),
        new("Slider", "A control that lets the user select from a range of values by moving a thumb.", "Basic Input", "\uE73A", "slider", "Slider.png"),
        new("SplitButton", "A button with two parts: a primary action and a flyout menu.", "Basic Input", "\uE73A", "split-button", "SplitButton.png"),
        new("TextBox", "A single-line or multi-line plain text input field.", "Basic Input", "\uE73A", "text-box", "TextBox.png"),
        new("ToggleButton", "A button that can be toggled between two states.", "Basic Input", "\uE73A", "toggle-button", "ToggleButton.png"),
        new("ToggleSwitch", "A switch that toggles between two mutually exclusive states.", "Basic Input", "\uE73A", "toggle-switch", "ToggleSwitch.png"),

        // Collections
        new("FlipView", "A control that presents one item at a time with flipping navigation.", "Collections", "\uE80A", "flip-view", "FlipView.png"),
        new("GridView", "A control that displays items in a horizontally wrapping grid.", "Collections", "\uE80A", "grid-view", "GridView.png"),
        new("ItemsView", "A flexible control for displaying collections of data items.", "Collections", "\uE80A", "items-view", "ItemsView.png"),
        new("ListBox", "A list of selectable items presented inline.", "Collections", "\uE80A", "list-box", "ListBox.png"),
        new("ListView", "A control that displays items in a vertical scrolling list.", "Collections", "\uE80A", "list-view", "ListView.png"),
        new("TreeView", "A hierarchical list with expanding and collapsing nodes.", "Collections", "\uE80A", "tree-view", "TreeView.png"),

        // Date and Time
        new("CalendarDatePicker", "A drop-down control that lets a user pick a date from a calendar.", "Date and Time", "\uEC92", "calendar-date-picker", "CalendarDatePicker.png"),
        new("CalendarView", "A calendar display that lets a user select a date.", "Date and Time", "\uEC92", "calendar-view", "CalendarView.png"),
        new("DatePicker", "A control that lets a user pick a date using spinners.", "Date and Time", "\uEC92", "date-picker", "DatePicker.png"),
        new("TimePicker", "A control that lets a user pick a time using spinners.", "Date and Time", "\uEC92", "time-picker", "TimePicker.png"),

        // Dialogs and Flyouts
        new("CommandBarFlyout", "A flyout that provides quick access to common commands.", "Dialogs and Flyouts", "\uE15F", "command-bar-flyout", "CommandBarFlyout.png"),
        new("ContentDialog", "A modal dialog box that displays content and action buttons.", "Dialogs and Flyouts", "\uE15F", "content-dialog", "ContentDialog.png"),
        new("Flyout", "A lightweight popup that shows contextual information.", "Dialogs and Flyouts", "\uE15F", "flyout", "Flyout.png"),
        new("MenuFlyout", "A flyout that displays a list of menu commands.", "Dialogs and Flyouts", "\uE15F", "menu-flyout", "MenuFlyout.png"),
        new("TeachingTip", "A notification flyout for guiding users through features.", "Dialogs and Flyouts", "\uE15F", "teaching-tip", "TeachingTip.png"),

        // Layout
        new("Border", "A container that draws a border around its child element.", "Layout", "\uE8A1", "border", "Border.png"),
        new("Card", "A theme-aware preset Border with the WinUI card brushes, corner radius, and padding.", "Layout", "\uE8A1", "card", "Border.png"),
        new("Canvas", "A layout panel that supports absolute positioning of child elements.", "Layout", "\uE8A1", "canvas", "Canvas.png"),
        new("Expander", "A container that expands and collapses to show or hide content.", "Layout", "\uE8A1", "expander", "Expander.png"),
        new("Flex", "A flexible layout container inspired by CSS flexbox.", "Layout", "\uE8A1", "flex", "Grid.png"),
        new("Grid", "A layout panel that arranges children in rows and columns.", "Layout", "\uE8A1", "grid", "Grid.png"),
        new("RelativePanel", "A panel that positions children relative to each other.", "Layout", "\uE8A1", "relative-panel", "RelativePanel.png"),
        new("ScrollView", "A scrollable container for content that exceeds available space.", "Layout", "\uE8A1", "scroll-view", "ScrollView.png"),
        new("SplitView", "A container with a collapsible pane and a content area.", "Layout", "\uE8A1", "split-view", "SplitView.png"),
        new("StackPanel", "A panel that arranges children in a single horizontal or vertical line.", "Layout", "\uE8A1", "stack-panel", "StackPanel.png"),
        new("Viewbox", "A container that scales its child to fill available space.", "Layout", "\uE8A1", "viewbox", "Viewbox.png"),
        new("WrapGrid", "A grid that wraps items to the next row or column automatically.", "Layout", "\uE8A1", "wrap-grid", "VariableSizedWrapGrid.png"),

        // Media
        new("Image", "A control that displays an image from a file or URI.", "Media", "\uE173", "image", "Image.png"),
        new("PersonPicture", "A control that displays a circular avatar for a person.", "Media", "\uE173", "person-picture", "PersonPicture.png"),
        new("WebView2", "A control that hosts web content using the Edge rendering engine.", "Media", "\uE173", "web-view-2", "WebView.png"),

        // Menus and Toolbars
        new("CommandBar", "A toolbar for exposing app commands and actions.", "Menus and Toolbars", "\uE74E", "command-bar", "CommandBar.png"),
        new("MenuBar", "A horizontal bar that hosts a set of drop-down menus.", "Menus and Toolbars", "\uE74E", "menu-bar", "MenuBar.png"),
        new("SelectorBar", "A bar that lets users switch between different views or modes.", "Menus and Toolbars", "\uE74E", "selector-bar", "Pivot.png"),

        // Navigation
        new("BreadcrumbBar", "A trail of links showing the user's navigation path.", "Navigation", "\uE700", "breadcrumb-bar", "BreadcrumbBar.png"),
        new("Frame", "A container that hosts Page navigation, raising .Navigated / .Navigating / .NavigationFailed.", "Navigation", "\uE700", "frame", "PageTransition.png"),
        new("NavigationView", "A side or top navigation pane for app-level navigation.", "Navigation", "\uE700", "navigation-view", "NavigationView.png"),
        new("Pivot", "A tabbed interface for switching between content sections.", "Navigation", "\uE700", "pivot", "Pivot.png"),
        new("TabView", "A control that displays a set of closable, rearrangeable tabs.", "Navigation", "\uE700", "tab-view", "TabView.png"),
        new("TitleBar", "A customizable title bar for the application window.", "Navigation", "\uE700", "title-bar", "TitleBar.png"),

        // Status and Info
        new("InfoBadge", "A small indicator that conveys status on another element.", "Status and Info", "\uE8F2", "info-badge", "InfoBadge.png"),
        new("InfoBar", "A dismissible bar for displaying essential app-level messages.", "Status and Info", "\uE8F2", "info-bar", "InfoBar.png"),
        new("ProgressBar", "A horizontal bar that shows progress of an operation.", "Status and Info", "\uE8F2", "progress-bar", "ProgressBar.png"),
        new("ProgressRing", "A circular indicator that shows ongoing progress.", "Status and Info", "\uE8F2", "progress-ring", "ProgressRing.png"),
        new("ToolTip", "A popup that displays helpful text when hovering over an element.", "Status and Info", "\uE8F2", "tool-tip", "ToolTip.png"),

        // Text
        new("AutoSuggestBox", "A text input that shows suggestions as the user types.", "Text", "\uE8D2", "auto-suggest-box", "AutoSuggestBox.png"),
        new("RichEditBox", "A rich text editing control with formatting support.", "Text", "\uE8D2", "rich-edit-box", "RichEditBox.png"),
        new("RichTextBlock", "A control that displays formatted read-only rich text.", "Text", "\uE8D2", "rich-text-block", "RichTextBlock.png"),
        new("Type ramp", "WinUI 3 type ramp factories \u2014 Title, Subtitle, Body, BodyStrong, BodyLarge, TitleLarge, Display.", "Text", "\uE8D2", "type-ramp", "TextBlock.png"),

        // Styles
        new("Acrylic", "A translucent material brush that creates a frosted glass effect.", "Styles", "\uE2B1", "acrylic", "Acrylic.png"),

        // Design
        new("Color", "Guidance on using the WinUI color system and theme resources.", "Design", "\uEB3C", "color", "ColorPaletteResources.png"),
        new("Geometry", "Corner radius resources for consistent rounding on controls and overlays.", "Design", "\uEB3C", "geometry", "Shape.png"),
        new("Spacing", "Margin, padding, and stack spacing for consistent layout rhythm.", "Design", "\uEB3C", "spacing", "CompactSizing.png"),
        new("Theming", "Guidance on applying light, dark, and high-contrast themes.", "Design", "\uEB3C", "theming", "ThemeTransition.png"),
        new("Typography", "Guidance on typographic styles, font ramps, and text hierarchy.", "Design", "\uEB3C", "typography", "TextBlock.png"),

        // ── Added: controls surfaced from recent Reactor additions ──
        new("RadioButtons", "A group of radio buttons where a user selects a single option.", "Basic Input", "\uE73A", "radio-buttons", "RadioButton.png"),
        new("ToggleSplitButton", "A split button whose primary part toggles on and off.", "Basic Input", "\uE73A", "toggle-split-button", "ToggleSplitButton.png"),

        new("ItemsRepeater", "A lightweight, data-driven panel for laying out custom collections.", "Collections", "\uE80A", "items-repeater", "ListView.png"),
        new("SemanticZoom", "A control that switches between a zoomed-in and zoomed-out view of data.", "Collections", "\uE80A", "semantic-zoom", "SemanticZoom.png"),
        new("SwipeControl", "A container that reveals swipe commands on its content.", "Collections", "\uE80A", "swipe-control", "SwipeControl.png"),
        new("RefreshContainer", "A pull-to-refresh container for scrollable content.", "Collections", "\uE80A", "refresh-container", "PullToRefresh.png"),

        new("UniformGrid", "A panel that arranges children in evenly sized cells.", "Layout", "\uE8A1", "uniform-grid", "Grid.png"),
        new("InterspersedGrid", "A grid that places proportionally sized panels with separators between them.", "Layout", "\uE8A1", "interspersed-grid", "Grid.png"),
        new("AnnotatedScrollBar", "A scrollbar that shows labelled annotations along its track.", "Layout", "\uE8A1", "annotated-scroll-bar", "AnnotatedScrollBar.png"),
        new("Docking", "Dockable, tearable, and floatable panes with drag-to-rearrange layout.", "Layout", "\uE8A1", "docking", "LayoutPanel.png"),

        new("MediaPlayerElement", "Embeds audio and video playback with built-in transport controls.", "Media", "\uE173", "media-player-element", "MediaPlayerElement.png"),
        new("AnimatedVisualPlayer", "Plays vector (Lottie/codegen) animations.", "Media", "\uE173", "animated-visual-player", "AnimatedVisualPlayer.png"),
        new("AnimatedIcon", "An icon that transitions between states with a vector animation.", "Media", "\uE173", "animated-icon", "AnimatedIcon.png"),
        new("MapControl", "Displays an interactive, pannable, zoomable map.", "Media", "\uE173", "map-control", "MapControl.png"),
        new("ParallaxView", "Shifts a background layer as content scrolls for a depth effect.", "Media", "\uE173", "parallax-view", "ParallaxView.png"),
        new("Shapes", "Rectangle, Ellipse, Line, and Path vector primitives.", "Media", "\uE173", "shapes", "Shape.png"),
        new("Icons", "Standalone icon elements from Symbol, Font glyph, or Path sources.", "Media", "\uE173", "icons", "IconElement.png"),

        new("PipsPager", "A pager that represents pages as a row of selectable dots.", "Navigation", "\uE700", "pips-pager", "PipsPager.png"),

        new("Popup", "A lightweight overlay that displays arbitrary content over the UI.", "Dialogs and Flyouts", "\uE15F", "popup", "Popup.png"),

        new("Markdown", "Renders a Markdown string as a Reactor element tree.", "Text", "\uE8D2", "markdown", "RichTextBlock.png"),

        new("DataGrid", "A virtualized data grid with sortable columns, selection, and inline editing.", "Data", "\uE7C3", "data-grid", "DataGrid.png"),
        new("PropertyGrid", "A reflection-driven editor for an object's properties.", "Data", "\uE7C3", "property-grid", "DataGrid.png"),

        new("Commands", "Declarative Command binding with async IsExecuting tracking and debounce.", "Patterns", "\uE943", "commands", "AppBarButton.png"),
        new("ErrorBoundary", "Catch an exception thrown while a subtree renders and show a fallback in its place.", "Patterns", "\uE943", "error-boundary", "CodeTagIcon.png"),

        // Motion
        new("Connected Animation", "Carries an element's visual from one view into another across a state change.", "Motion", "\uE945", "connected-animation", "ConnectedAnimation.png"),
        new("Transitions", "Implicit opacity, scale, and translation transitions that run on value change, plus layout animation for layout-driven position changes.", "Motion", "\uE945", "transitions", "ImplicitTransition.png"),

        // ── Fundamentals — framework mechanics, not controls (issue #1275) ──
        new("UseState", "Component state \u2014 a value plus a setter that schedules a re-render.", "Fundamentals", "\uE8F1", "use-state", "CodeTagIcon.png"),
        new("UseEffect", "Side effects and lifecycle \u2014 run after render, clean up on unmount.", "Fundamentals", "\uE8F1", "use-effect", "CodeTagIcon.png"),
        new("UseReducer", "State derived from the previous value \u2014 the hook collections need.", "Fundamentals", "\uE8F1", "use-reducer", "CodeTagIcon.png"),
        new("UseMemo", "Memoized computations and stable callbacks, via UseMemo and UseCallback.", "Fundamentals", "\uE8F1", "use-memo", "CodeTagIcon.png"),
        new("UseRef", "A mutable box that survives re-renders without triggering one.", "Fundamentals", "\uE8F1", "use-ref", "CodeTagIcon.png"),
        new("Context", "Tree-scoped ambient state, read without threading it through every component.", "Fundamentals", "\uE8F1", "context", "CodeTagIcon.png"),
        new("Element refs", "Refs to realized WinUI elements \u2014 imperative focus and reference props.", "Fundamentals", "\uE8F1", "element-refs", "CodeTagIcon.png"),
        new("Keyboard input", "Key events and VirtualKey, and where a Command accelerator belongs instead.", "Fundamentals", "\uE8F1", "keyboard-input", "CodeTagIcon.png"),
        new("Pointer and gestures", "Pointer and tap events plus continuous pan, pinch, and rotate gestures.", "Fundamentals", "\uE8F1", "pointer-input", "CodeTagIcon.png"),

        // System
        new("File pickers", "Native open, multi-select, folder, and save dialogs owned by the component's window.", "System", "\uE770", "file-pickers", "StoragePickers.png"),
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
