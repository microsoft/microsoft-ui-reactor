using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace WinUIGalleryReactor;

/// <summary>
/// Maps control tags to their page components.
/// </summary>
static class PageRouter
{
    public static Element Route(string tag) => tag switch
    {
        // Basic Input
        "button" => Component<ControlPages.BasicInput.ButtonPage>(),
        "check-box" => Component<ControlPages.BasicInput.CheckBoxPage>(),
        "combo-box" => Component<ControlPages.BasicInput.ComboBoxPage>(),
        "color-picker" => Component<ControlPages.BasicInput.ColorPickerPage>(),
        "drop-down-button" => Component<ControlPages.BasicInput.DropDownButtonPage>(),
        "hyperlink-button" => Component<ControlPages.BasicInput.HyperlinkButtonPage>(),
        "number-box" => Component<ControlPages.BasicInput.NumberBoxPage>(),
        "password-box" => Component<ControlPages.BasicInput.PasswordBoxPage>(),
        "radio-button" => Component<ControlPages.BasicInput.RadioButtonPage>(),
        "rating-control" => Component<ControlPages.BasicInput.RatingControlPage>(),
        "repeat-button" => Component<ControlPages.BasicInput.RepeatButtonPage>(),
        "slider" => Component<ControlPages.BasicInput.SliderPage>(),
        "split-button" => Component<ControlPages.BasicInput.SplitButtonPage>(),
        "text-box" => Component<ControlPages.BasicInput.TextBoxPage>(),
        "toggle-button" => Component<ControlPages.BasicInput.ToggleButtonPage>(),
        "toggle-switch" => Component<ControlPages.BasicInput.ToggleSwitchPage>(),

        // Collections
        "flip-view" => Component<FlipViewPage>(),
        "grid-view" => Component<GridViewPage>(),
        "items-view" => Component<ItemsViewPage>(),
        "list-box" => Component<ListBoxPage>(),
        "list-view" => Component<ListViewPage>(),
        "tree-view" => Component<TreeViewPage>(),

        // Date and Time
        "calendar-date-picker" => Component<ControlPages.DateAndTime.CalendarDatePickerPage>(),
        "calendar-view" => Component<ControlPages.DateAndTime.CalendarViewPage>(),
        "date-picker" => Component<ControlPages.DateAndTime.DatePickerPage>(),
        "time-picker" => Component<ControlPages.DateAndTime.TimePickerPage>(),

        // Dialogs and Flyouts
        "command-bar-flyout" => Component<ControlPages.DialogsAndFlyouts.CommandBarFlyoutPage>(),
        "content-dialog" => Component<ControlPages.DialogsAndFlyouts.ContentDialogPage>(),
        "flyout" => Component<ControlPages.DialogsAndFlyouts.FlyoutPage>(),
        "menu-flyout" => Component<ControlPages.DialogsAndFlyouts.MenuFlyoutPage>(),
        "teaching-tip" => Component<ControlPages.DialogsAndFlyouts.TeachingTipPage>(),

        // Layout
        "border" => Component<BorderPage>(),
        "card" => Component<ControlPages.Layout.CardPage>(),
        "canvas" => Component<CanvasPage>(),
        "expander" => Component<ExpanderPage>(),
        "flex" => Component<FlexPage>(),
        "grid" => Component<GridPage>(),
        "relative-panel" => Component<RelativePanelPage>(),
        "scroll-view" => Component<ScrollViewPage>(),
        "split-view" => Component<SplitViewPage>(),
        "stack-panel" => Component<StackPanelPage>(),
        "viewbox" => Component<ViewboxPage>(),
        "wrap-grid" => Component<WrapGridPage>(),

        // Media
        "image" => Component<ControlPages.Media.ImagePage>(),
        "person-picture" => Component<ControlPages.Media.PersonPicturePage>(),
        "web-view-2" => Component<ControlPages.Media.WebView2Page>(),

        // Menus and Toolbars
        "command-bar" => Component<ControlPages.MenusAndToolbars.CommandBarPage>(),
        "menu-bar" => Component<ControlPages.MenusAndToolbars.MenuBarPage>(),
        "selector-bar" => Component<ControlPages.MenusAndToolbars.SelectorBarPage>(),

        // Navigation
        "breadcrumb-bar" => Component<ControlPages.Navigation.BreadcrumbBarPage>(),
        "frame" => Component<ControlPages.Navigation.FrameNavigationPage>(),
        "navigation-view" => Component<ControlPages.Navigation.NavigationViewPage>(),
        "pivot" => Component<ControlPages.Navigation.PivotPage>(),
        "tab-view" => Component<ControlPages.Navigation.TabViewPage>(),
        "title-bar" => Component<ControlPages.Navigation.TitleBarPage>(),

        // Status and Info
        "info-bar" => Component<ControlPages.StatusAndInfo.InfoBarPage>(),
        "info-badge" => Component<ControlPages.StatusAndInfo.InfoBadgePage>(),
        "progress-bar" => Component<ControlPages.StatusAndInfo.ProgressBarPage>(),
        "progress-ring" => Component<ControlPages.StatusAndInfo.ProgressRingPage>(),
        "tool-tip" => Component<ControlPages.StatusAndInfo.ToolTipPage>(),

        // Text
        "auto-suggest-box" => Component<AutoSuggestBoxPage>(),
        "rich-edit-box" => Component<RichEditBoxPage>(),
        "rich-text-block" => Component<RichTextBlockPage>(),
        "type-ramp" => Component<ControlPages.Text.TypeRampPage>(),

        // Styles
        "acrylic" => Component<ControlPages.Styles.AcrylicPage>(),

        // Design
        "color" => Component<ControlPages.Styles.ColorPage>(),
        "geometry" => Component<ControlPages.Styles.GeometryPage>(),
        "spacing" => Component<ControlPages.Styles.SpacingPage>(),
        "theming" => Component<ControlPages.DesignGuidance.ThemePage>(),
        "typography" => Component<ControlPages.Styles.TypographyPage>(),

        _ => TextBlock($"Page not found: {tag}").Foreground(Theme.SecondaryText)
    };
}
