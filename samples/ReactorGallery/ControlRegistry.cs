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
        new("FlipView", "A control that presents one item at a time with flipping navigation.", "Collections", "\uE8A9", "flip-view", "FlipView.png"),
        new("GridView", "A control that displays items in a horizontally wrapping grid.", "Collections", "\uE8A9", "grid-view", "GridView.png"),
        new("ItemsView", "A flexible control for displaying collections of data items.", "Collections", "\uE8A9", "items-view", "ItemsView.png"),
        new("ListBox", "A list of selectable items presented inline.", "Collections", "\uE8A9", "list-box", "ListBox.png"),
        new("ListView", "A control that displays items in a vertical scrolling list.", "Collections", "\uE8A9", "list-view", "ListView.png"),
        new("TreeView", "A hierarchical list with expanding and collapsing nodes.", "Collections", "\uE8A9", "tree-view", "TreeView.png"),

        // Date and Time
        new("CalendarDatePicker", "A drop-down control that lets a user pick a date from a calendar.", "Date and Time", "\uE787", "calendar-date-picker", "CalendarDatePicker.png"),
        new("CalendarView", "A calendar display that lets a user select a date.", "Date and Time", "\uE787", "calendar-view", "CalendarView.png"),
        new("DatePicker", "A control that lets a user pick a date using spinners.", "Date and Time", "\uE787", "date-picker", "DatePicker.png"),
        new("TimePicker", "A control that lets a user pick a time using spinners.", "Date and Time", "\uE787", "time-picker", "TimePicker.png"),

        // Dialogs and Flyouts
        new("CommandBarFlyout", "A flyout that provides quick access to common commands.", "Dialogs and Flyouts", "\uE8BD", "command-bar-flyout", "CommandBarFlyout.png"),
        new("ContentDialog", "A modal dialog box that displays content and action buttons.", "Dialogs and Flyouts", "\uE8BD", "content-dialog", "ContentDialog.png"),
        new("Flyout", "A lightweight popup that shows contextual information.", "Dialogs and Flyouts", "\uE8BD", "flyout", "Flyout.png"),
        new("MenuFlyout", "A flyout that displays a list of menu commands.", "Dialogs and Flyouts", "\uE8BD", "menu-flyout", "MenuFlyout.png"),
        new("TeachingTip", "A notification flyout for guiding users through features.", "Dialogs and Flyouts", "\uE8BD", "teaching-tip", "TeachingTip.png"),

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
        new("Image", "A control that displays an image from a file or URI.", "Media", "\uE8B9", "image", "Image.png"),
        new("PersonPicture", "A control that displays a circular avatar for a person.", "Media", "\uE8B9", "person-picture", "PersonPicture.png"),
        new("WebView2", "A control that hosts web content using the Edge rendering engine.", "Media", "\uE8B9", "web-view-2", "WebView.png"),

        // Menus and Toolbars
        new("CommandBar", "A toolbar for exposing app commands and actions.", "Menus and Toolbars", "\uE700", "command-bar", "CommandBar.png"),
        new("MenuBar", "A horizontal bar that hosts a set of drop-down menus.", "Menus and Toolbars", "\uE700", "menu-bar", "MenuBar.png"),
        new("SelectorBar", "A bar that lets users switch between different views or modes.", "Menus and Toolbars", "\uE700", "selector-bar", "Placeholder.png"),

        // Navigation
        new("BreadcrumbBar", "A trail of links showing the user's navigation path.", "Navigation", "\uE8B0", "breadcrumb-bar", "BreadcrumbBar.png"),
        new("Frame", "A container that hosts Page navigation, raising .Navigated / .Navigating / .NavigationFailed.", "Navigation", "\uE8B0", "frame", "Placeholder.png"),
        new("NavigationView", "A side or top navigation pane for app-level navigation.", "Navigation", "\uE8B0", "navigation-view", "NavigationView.png"),
        new("Pivot", "A tabbed interface for switching between content sections.", "Navigation", "\uE8B0", "pivot", "Pivot.png"),
        new("TabView", "A control that displays a set of closable, rearrangeable tabs.", "Navigation", "\uE8B0", "tab-view", "TabView.png"),
        new("TitleBar", "A customizable title bar for the application window.", "Navigation", "\uE8B0", "title-bar", "TitleBar.png"),

        // Status and Info
        new("InfoBadge", "A small indicator that conveys status on another element.", "Status and Info", "\uE946", "info-badge", "InfoBadge.png"),
        new("InfoBar", "A dismissible bar for displaying essential app-level messages.", "Status and Info", "\uE946", "info-bar", "InfoBar.png"),
        new("ProgressBar", "A horizontal bar that shows progress of an operation.", "Status and Info", "\uE946", "progress-bar", "ProgressBar.png"),
        new("ProgressRing", "A circular indicator that shows ongoing progress.", "Status and Info", "\uE946", "progress-ring", "ProgressRing.png"),
        new("ToolTip", "A popup that displays helpful text when hovering over an element.", "Status and Info", "\uE946", "tool-tip", "ToolTip.png"),

        // Text
        new("AutoSuggestBox", "A text input that shows suggestions as the user types.", "Text", "\uE8D2", "auto-suggest-box", "AutoSuggestBox.png"),
        new("RichEditBox", "A rich text editing control with formatting support.", "Text", "\uE8D2", "rich-edit-box", "RichEditBox.png"),
        new("RichTextBlock", "A control that displays formatted read-only rich text.", "Text", "\uE8D2", "rich-text-block", "RichTextBlock.png"),
        new("Type ramp", "WinUI 3 type ramp factories \u2014 Title, Subtitle, Body, BodyStrong, BodyLarge.", "Text", "\uE8D2", "type-ramp", "TextBlock.png"),

        // Styles
        new("Acrylic", "A translucent material brush that creates a frosted glass effect.", "Styles", "\uE790", "acrylic", "Acrylic.png"),

        // Design
        new("Color", "Guidance on using the WinUI color system and theme resources.", "Design", "\uE790", "color", "ColorPaletteResources.png"),
        new("Geometry", "Corner radius resources for consistent rounding on controls and overlays.", "Design", "\uE790", "geometry"),
        new("Spacing", "Margin, padding, and stack spacing for consistent layout rhythm.", "Design", "\uE790", "spacing"),
        new("Theming", "Guidance on applying light, dark, and high-contrast themes.", "Design", "\uE771", "theming", "ThemeTransition.png"),
        new("Typography", "Guidance on typographic styles, font ramps, and text hierarchy.", "Design", "\uE790", "typography", "TextBlock.png"),
    }
    .OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
    .ToArray();

    public static string[] Categories { get; } = new[]
    {
        "Basic Input",
        "Collections",
        "Date and Time",
        "Dialogs and Flyouts",
        "Layout",
        "Media",
        "Menus and Toolbars",
        "Navigation",
        "Status and Info",
        "Text",
        "Design",
        "Styles",
    };

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
