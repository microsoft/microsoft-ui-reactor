---
title: "Modifiers: Native properties"
description: "The Set modifier, which changes the real WinUI control directly."
sidebar:
  label: "Native properties"
---

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Modifier System](/microsoft-ui-reactor/main/how-it-works/modifier-system), [Coming from XAML](/microsoft-ui-reactor/main/get-started/xaml-developers)

## `Set` [#set]

```csharp
Win2DAnimatedCanvasElement Set(this Win2DAnimatedCanvasElement el, Action<CanvasAnimatedControl> setter)
Win2DCanvasElement Set(this Win2DCanvasElement el, Action<CanvasControl> setter)
Win2DVirtualCanvasElement Set(this Win2DVirtualCanvasElement el, Action<CanvasVirtualControl> setter)
AnimatedIconElement Set(this AnimatedIconElement el, Action<AnimatedIcon> configure)
AnimatedVisualPlayerElement Set(this AnimatedVisualPlayerElement el, Action<AnimatedVisualPlayer> configure)
AnnotatedScrollBarElement Set(this AnnotatedScrollBarElement el, Action<AnnotatedScrollBar> configure)
AutoSuggestBoxElement Set(this AutoSuggestBoxElement el, Action<AutoSuggestBox> configure)
BorderElement Set(this BorderElement el, Action<Border> configure)
BreadcrumbBarElement Set(this BreadcrumbBarElement el, Action<BreadcrumbBar> configure)
ButtonElement Set(this ButtonElement el, Action<Button> configure)
CalendarDatePickerElement Set(this CalendarDatePickerElement el, Action<CalendarDatePicker> configure)
CalendarViewElement Set(this CalendarViewElement el, Action<CalendarView> configure)
CanvasElement Set(this CanvasElement el, Action<Canvas> configure)
CheckBoxElement Set(this CheckBoxElement el, Action<CheckBox> configure)
ColorPickerElement Set(this ColorPickerElement el, Action<ColorPicker> configure)
ComboBoxElement Set(this ComboBoxElement el, Action<ComboBox> configure)
CommandBarElement Set(this CommandBarElement el, Action<CommandBar> configure)
CommandBarFlyoutElement Set(this CommandBarFlyoutElement el, Action<CommandBarFlyout> configure)
ContentDialogElement Set(this ContentDialogElement el, Action<ContentDialog> configure)
DatePickerElement Set(this DatePickerElement el, Action<DatePicker> configure)
DropDownButtonElement Set(this DropDownButtonElement el, Action<DropDownButton> configure)
EllipseElement Set(this EllipseElement el, Action<Ellipse> configure)
ExpanderElement Set(this ExpanderElement el, Action<Expander> configure)
FlexElement Set(this FlexElement el, Action<FlexPanel> configure)
FlipViewElement Set(this FlipViewElement el, Action<FlipView> configure)
FlyoutElement Set(this FlyoutElement el, Action<Flyout> configure)
FrameElement Set(this FrameElement el, Action<Frame> configure)
GridElement Set(this GridElement el, Action<Grid> configure)
GridViewElement Set(this GridViewElement el, Action<GridView> configure)
HyperlinkButtonElement Set(this HyperlinkButtonElement el, Action<HyperlinkButton> configure)
IconElement Set(this IconElement el, Action<IconElement> configure)
ImageElement Set(this ImageElement el, Action<Image> configure)
InfoBadgeElement Set(this InfoBadgeElement el, Action<InfoBadge> configure)
InfoBarElement Set(this InfoBarElement el, Action<InfoBar> configure)
ItemContainerElement Set(this ItemContainerElement el, Action<ItemContainer> configure)
LineElement Set(this LineElement el, Action<Line> configure)
ListBoxElement Set(this ListBoxElement el, Action<ListBox> configure)
ListViewElement Set(this ListViewElement el, Action<ListView> configure)
MapControlElement Set(this MapControlElement el, Action<MapControl> configure)
MediaPlayerElementElement Set(this MediaPlayerElementElement el, Action<MediaPlayerElement> configure)
MenuBarElement Set(this MenuBarElement el, Action<MenuBar> configure)
MenuFlyoutElement Set(this MenuFlyoutElement el, Action<MenuFlyout> configure)
NavigationViewElement Set(this NavigationViewElement el, Action<NavigationView> configure)
NumberBoxElement Set(this NumberBoxElement el, Action<NumberBox> configure)
ParallaxViewElement Set(this ParallaxViewElement el, Action<ParallaxView> configure)
PasswordBoxElement Set(this PasswordBoxElement el, Action<PasswordBox> configure)
PathElement Set(this PathElement el, Action<Path> configure)
PersonPictureElement Set(this PersonPictureElement el, Action<PersonPicture> configure)
PipsPagerElement Set(this PipsPagerElement el, Action<PipsPager> configure)
PivotElement Set(this PivotElement el, Action<Pivot> configure)
PopupElement Set(this PopupElement el, Action<Popup> configure)
ProgressElement Set(this ProgressElement el, Action<ProgressBar> configure)
ProgressRingElement Set(this ProgressRingElement el, Action<ProgressRing> configure)
RadioButtonElement Set(this RadioButtonElement el, Action<RadioButton> configure)
RadioButtonsElement Set(this RadioButtonsElement el, Action<RadioButtons> configure)
RatingControlElement Set(this RatingControlElement el, Action<RatingControl> configure)
RectangleElement Set(this RectangleElement el, Action<Rectangle> configure)
RefreshContainerElement Set(this RefreshContainerElement el, Action<RefreshContainer> configure)
RelativePanelElement Set(this RelativePanelElement el, Action<RelativePanel> configure)
RepeatButtonElement Set(this RepeatButtonElement el, Action<RepeatButton> configure)
RichEditBoxElement Set(this RichEditBoxElement el, Action<RichEditBox> configure)
RichTextBlockElement Set(this RichTextBlockElement el, Action<RichTextBlock> configure)
ScrollViewElement Set(this ScrollViewElement el, Action<ScrollView> configure)
ScrollViewerElement Set(this ScrollViewerElement el, Action<ScrollViewer> configure)
SelectorBarElement Set(this SelectorBarElement el, Action<SelectorBar> configure)
SemanticZoomElement Set(this SemanticZoomElement el, Action<SemanticZoom> configure)
SliderElement Set(this SliderElement el, Action<Slider> configure)
SplitButtonElement Set(this SplitButtonElement el, Action<SplitButton> configure)
SplitViewElement Set(this SplitViewElement el, Action<SplitView> configure)
StackElement Set(this StackElement el, Action<StackPanel> configure)
SwipeControlElement Set(this SwipeControlElement el, Action<SwipeControl> configure)
TabViewElement Set(this TabViewElement el, Action<TabView> configure)
TeachingTipElement Set(this TeachingTipElement el, Action<TeachingTip> configure)
TextBlockElement Set(this TextBlockElement el, Action<TextBlock> configure)
TextBoxElement Set(this TextBoxElement el, Action<TextBox> configure)
TimePickerElement Set(this TimePickerElement el, Action<TimePicker> configure)
TitleBarElement Set(this TitleBarElement el, Action<TitleBar> configure)
ToggleButtonElement Set(this ToggleButtonElement el, Action<ToggleButton> configure)
ToggleSplitButtonElement Set(this ToggleSplitButtonElement el, Action<ToggleSplitButton> configure)
ToggleSwitchElement Set(this ToggleSwitchElement el, Action<ToggleSwitch> configure)
TreeViewElement Set(this TreeViewElement el, Action<TreeView> configure)
ViewboxElement Set(this ViewboxElement el, Action<Viewbox> configure)
WebView2Element Set(this WebView2Element el, Action<WebView2> configure)
WrapGridElement Set(this WrapGridElement el, Action<VariableSizedWrapGrid> configure)
ItemsViewElement<T> Set<T>(this ItemsViewElement<T> el, Action<ItemsView> configure)
LazyHStackElement<T> Set<T>(this LazyHStackElement<T> el, Action<ScrollViewer> configure)
LazyVStackElement<T> Set<T>(this LazyVStackElement<T> el, Action<ScrollViewer> configure)
TemplatedFlipViewElement<T> Set<T>(this TemplatedFlipViewElement<T> el, Action<FlipView> configure)
TemplatedGridViewElement<T> Set<T>(this TemplatedGridViewElement<T> el, Action<GridView> configure)
TemplatedListViewElement<T> Set<T>(this TemplatedListViewElement<T> el, Action<ListView> configure)
```

Declared in `ElementExtensions`, `Win2DCanvasModifiers` (package `Microsoft.UI.Reactor.Advanced`, `using Microsoft.UI.Reactor.Advanced.Win2D;`).

| Overload | Description |
| --- | --- |
| `Set(Win2DAnimatedCanvasElement, Action<CanvasAnimatedControl>)` | Adds a raw `CanvasAnimatedControl` setter to an animated Win2D canvas. |
| `Set(Win2DCanvasElement, Action<CanvasControl>)` | Adds a raw `CanvasControl` setter to a manual Win2D canvas. |
| `Set(Win2DVirtualCanvasElement, Action<CanvasVirtualControl>)` | Adds a raw `CanvasVirtualControl` setter to a virtual Win2D canvas. |
| `Set(AnimatedIconElement, Action<AnimatedIcon>)` |  |
| `Set(AnimatedVisualPlayerElement, Action<AnimatedVisualPlayer>)` |  |
| `Set(AnnotatedScrollBarElement, Action<AnnotatedScrollBar>)` |  |
| `Set(AutoSuggestBoxElement, Action<AutoSuggestBox>)` |  |
| `Set(BorderElement, Action<Border>)` |  |
| `Set(BreadcrumbBarElement, Action<BreadcrumbBar>)` |  |
| `Set(ButtonElement, Action<Button>)` |  |
| `Set(CalendarDatePickerElement, Action<CalendarDatePicker>)` |  |
| `Set(CalendarViewElement, Action<CalendarView>)` |  |
| `Set(CanvasElement, Action<Canvas>)` |  |
| `Set(CheckBoxElement, Action<CheckBox>)` |  |
| `Set(ColorPickerElement, Action<ColorPicker>)` |  |
| `Set(ComboBoxElement, Action<ComboBox>)` |  |
| `Set(CommandBarElement, Action<CommandBar>)` |  |
| `Set(CommandBarFlyoutElement, Action<CommandBarFlyout>)` |  |
| `Set(ContentDialogElement, Action<ContentDialog>)` |  |
| `Set(DatePickerElement, Action<DatePicker>)` |  |
| `Set(DropDownButtonElement, Action<DropDownButton>)` |  |
| `Set(EllipseElement, Action<Ellipse>)` |  |
| `Set(ExpanderElement, Action<Expander>)` |  |
| `Set(FlexElement, Action<FlexPanel>)` |  |
| `Set(FlipViewElement, Action<FlipView>)` |  |
| `Set(FlyoutElement, Action<Flyout>)` |  |
| `Set(FrameElement, Action<Frame>)` |  |
| `Set(GridElement, Action<Grid>)` |  |
| `Set(GridViewElement, Action<GridView>)` |  |
| `Set(HyperlinkButtonElement, Action<HyperlinkButton>)` |  |
| `Set(IconElement, Action<IconElement>)` |  |
| `Set(ImageElement, Action<Image>)` |  |
| `Set(InfoBadgeElement, Action<InfoBadge>)` |  |
| `Set(InfoBarElement, Action<InfoBar>)` |  |
| `Set(ItemContainerElement, Action<ItemContainer>)` |  |
| `Set(LineElement, Action<Line>)` |  |
| `Set(ListBoxElement, Action<ListBox>)` |  |
| `Set(ListViewElement, Action<ListView>)` |  |
| `Set(MapControlElement, Action<MapControl>)` |  |
| `Set(MediaPlayerElementElement, Action<MediaPlayerElement>)` |  |
| `Set(MenuBarElement, Action<MenuBar>)` |  |
| `Set(MenuFlyoutElement, Action<MenuFlyout>)` |  |
| `Set(NavigationViewElement, Action<NavigationView>)` |  |
| `Set(NumberBoxElement, Action<NumberBox>)` |  |
| `Set(ParallaxViewElement, Action<ParallaxView>)` |  |
| `Set(PasswordBoxElement, Action<PasswordBox>)` |  |
| `Set(PathElement, Action<Path>)` |  |
| `Set(PersonPictureElement, Action<PersonPicture>)` |  |
| `Set(PipsPagerElement, Action<PipsPager>)` |  |
| `Set(PivotElement, Action<Pivot>)` |  |
| `Set(PopupElement, Action<Popup>)` |  |
| `Set(ProgressElement, Action<ProgressBar>)` |  |
| `Set(ProgressRingElement, Action<ProgressRing>)` |  |
| `Set(RadioButtonElement, Action<RadioButton>)` |  |
| `Set(RadioButtonsElement, Action<RadioButtons>)` |  |
| `Set(RatingControlElement, Action<RatingControl>)` |  |
| `Set(RectangleElement, Action<Rectangle>)` |  |
| `Set(RefreshContainerElement, Action<RefreshContainer>)` |  |
| `Set(RelativePanelElement, Action<RelativePanel>)` |  |
| `Set(RepeatButtonElement, Action<RepeatButton>)` |  |
| `Set(RichEditBoxElement, Action<RichEditBox>)` |  |
| `Set(RichTextBlockElement, Action<RichTextBlock>)` |  |
| `Set(ScrollViewElement, Action<ScrollView>)` |  |
| `Set(ScrollViewerElement, Action<ScrollViewer>)` |  |
| `Set(SelectorBarElement, Action<SelectorBar>)` |  |
| `Set(SemanticZoomElement, Action<SemanticZoom>)` |  |
| `Set(SliderElement, Action<Slider>)` |  |
| `Set(SplitButtonElement, Action<SplitButton>)` |  |
| `Set(SplitViewElement, Action<SplitView>)` |  |
| `Set(StackElement, Action<StackPanel>)` |  |
| `Set(SwipeControlElement, Action<SwipeControl>)` |  |
| `Set(TabViewElement, Action<TabView>)` |  |
| `Set(TeachingTipElement, Action<TeachingTip>)` |  |
| `Set(TextBlockElement, Action<TextBlock>)` |  |
| `Set(TextBoxElement, Action<TextBox>)` |  |
| `Set(TimePickerElement, Action<TimePicker>)` |  |
| `Set(TitleBarElement, Action<TitleBar>)` |  |
| `Set(ToggleButtonElement, Action<ToggleButton>)` |  |
| `Set(ToggleSplitButtonElement, Action<ToggleSplitButton>)` |  |
| `Set(ToggleSwitchElement, Action<ToggleSwitch>)` |  |
| `Set(TreeViewElement, Action<TreeView>)` |  |
| `Set(ViewboxElement, Action<Viewbox>)` |  |
| `Set(WebView2Element, Action<WebView2>)` |  |
| `Set(WrapGridElement, Action<VariableSizedWrapGrid>)` |  |
| `Set<T>(ItemsViewElement<T>, Action<ItemsView>)` |  |
| `Set<T>(LazyHStackElement<T>, Action<ScrollViewer>)` |  |
| `Set<T>(LazyVStackElement<T>, Action<ScrollViewer>)` |  |
| `Set<T>(TemplatedFlipViewElement<T>, Action<FlipView>)` |  |
| `Set<T>(TemplatedGridViewElement<T>, Action<GridView>)` |  |
| `Set<T>(TemplatedListViewElement<T>, Action<ListView>)` |  |

**Applies to:** [`Win2DAnimatedCanvasElement`](/microsoft-ui-reactor/main/reference/elements/win2d#win2danimatedcanvaselement), [`Win2DCanvasElement`](/microsoft-ui-reactor/main/reference/elements/win2d#win2dcanvaselement), [`Win2DVirtualCanvasElement`](/microsoft-ui-reactor/main/reference/elements/win2d#win2dvirtualcanvaselement), [`AnimatedIconElement`](/microsoft-ui-reactor/main/reference/elements/media#animatediconelement), [`AnimatedVisualPlayerElement`](/microsoft-ui-reactor/main/reference/elements/media#animatedvisualplayerelement), [`AnnotatedScrollBarElement`](/microsoft-ui-reactor/main/reference/elements/layout#annotatedscrollbarelement), [`AutoSuggestBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#autosuggestboxelement), [`BorderElement`](/microsoft-ui-reactor/main/reference/elements/layout#borderelement), [`BreadcrumbBarElement`](/microsoft-ui-reactor/main/reference/elements/navigation#breadcrumbbarelement), [`ButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#buttonelement), [`CalendarDatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#calendardatepickerelement), [`CalendarViewElement`](/microsoft-ui-reactor/main/reference/elements/input#calendarviewelement), [`CanvasElement`](/microsoft-ui-reactor/main/reference/elements/layout#canvaselement), [`CheckBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#checkboxelement), [`ColorPickerElement`](/microsoft-ui-reactor/main/reference/elements/input#colorpickerelement), [`ComboBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#comboboxelement), [`CommandBarElement`](/microsoft-ui-reactor/main/reference/elements/menus-and-commands#commandbarelement), [`CommandBarFlyoutElement`](/microsoft-ui-reactor/main/reference/elements/menus-and-commands#commandbarflyoutelement), [`ContentDialogElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#contentdialogelement), [`DatePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#datepickerelement), [`DropDownButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#dropdownbuttonelement), [`EllipseElement`](/microsoft-ui-reactor/main/reference/elements/shapes#ellipseelement), [`ExpanderElement`](/microsoft-ui-reactor/main/reference/elements/layout#expanderelement), [`FlexElement`](/microsoft-ui-reactor/main/reference/elements/layout#flexelement), [`FlipViewElement`](/microsoft-ui-reactor/main/reference/elements/collections#flipviewelement), [`FlyoutElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#flyoutelement), [`FrameElement`](/microsoft-ui-reactor/main/reference/elements/navigation#frameelement), [`GridElement`](/microsoft-ui-reactor/main/reference/elements/layout#gridelement), [`GridViewElement`](/microsoft-ui-reactor/main/reference/elements/collections#gridviewelement), [`HyperlinkButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#hyperlinkbuttonelement), [`IconElement`](/microsoft-ui-reactor/main/reference/elements/media#iconelement), [`ImageElement`](/microsoft-ui-reactor/main/reference/elements/media#imageelement), [`InfoBadgeElement`](/microsoft-ui-reactor/main/reference/elements/status-and-info#infobadgeelement), [`InfoBarElement`](/microsoft-ui-reactor/main/reference/elements/status-and-info#infobarelement), [`ItemContainerElement`](/microsoft-ui-reactor/main/reference/elements/collections#itemcontainerelement), [`LineElement`](/microsoft-ui-reactor/main/reference/elements/shapes#lineelement), [`ListBoxElement`](/microsoft-ui-reactor/main/reference/elements/collections#listboxelement), [`ListViewElement`](/microsoft-ui-reactor/main/reference/elements/collections#listviewelement), [`MapControlElement`](/microsoft-ui-reactor/main/reference/elements/interop#mapcontrolelement), [`MediaPlayerElementElement`](/microsoft-ui-reactor/main/reference/elements/media#mediaplayerelementelement), [`MenuBarElement`](/microsoft-ui-reactor/main/reference/elements/menus-and-commands#menubarelement), [`MenuFlyoutElement`](/microsoft-ui-reactor/main/reference/elements/menus-and-commands#menuflyoutelement), [`NavigationViewElement`](/microsoft-ui-reactor/main/reference/elements/navigation#navigationviewelement), [`NumberBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#numberboxelement), [`ParallaxViewElement`](/microsoft-ui-reactor/main/reference/elements/layout#parallaxviewelement), [`PasswordBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#passwordboxelement), [`PathElement`](/microsoft-ui-reactor/main/reference/elements/shapes#pathelement), [`PersonPictureElement`](/microsoft-ui-reactor/main/reference/elements/media#personpictureelement), [`PipsPagerElement`](/microsoft-ui-reactor/main/reference/elements/navigation#pipspagerelement), [`PivotElement`](/microsoft-ui-reactor/main/reference/elements/navigation#pivotelement), [`PopupElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#popupelement), [`ProgressElement`](/microsoft-ui-reactor/main/reference/elements/status-and-info#progresselement), [`ProgressRingElement`](/microsoft-ui-reactor/main/reference/elements/status-and-info#progressringelement), [`RadioButtonElement`](/microsoft-ui-reactor/main/reference/elements/input#radiobuttonelement), [`RadioButtonsElement`](/microsoft-ui-reactor/main/reference/elements/input#radiobuttonselement), [`RatingControlElement`](/microsoft-ui-reactor/main/reference/elements/input#ratingcontrolelement), [`RectangleElement`](/microsoft-ui-reactor/main/reference/elements/shapes#rectangleelement), [`RefreshContainerElement`](/microsoft-ui-reactor/main/reference/elements/collections#refreshcontainerelement), [`RelativePanelElement`](/microsoft-ui-reactor/main/reference/elements/layout#relativepanelelement), [`RepeatButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#repeatbuttonelement), [`RichEditBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#richeditboxelement), [`RichTextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#richtextblockelement), [`ScrollViewElement`](/microsoft-ui-reactor/main/reference/elements/layout#scrollviewelement), [`ScrollViewerElement`](/microsoft-ui-reactor/main/reference/elements/layout#scrollviewerelement), [`SelectorBarElement`](/microsoft-ui-reactor/main/reference/elements/navigation#selectorbarelement), [`SemanticZoomElement`](/microsoft-ui-reactor/main/reference/elements/collections#semanticzoomelement), [`SliderElement`](/microsoft-ui-reactor/main/reference/elements/input#sliderelement), [`SplitButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#splitbuttonelement), [`SplitViewElement`](/microsoft-ui-reactor/main/reference/elements/layout#splitviewelement), [`StackElement`](/microsoft-ui-reactor/main/reference/elements/layout#stackelement), [`SwipeControlElement`](/microsoft-ui-reactor/main/reference/elements/collections#swipecontrolelement), [`TabViewElement`](/microsoft-ui-reactor/main/reference/elements/navigation#tabviewelement), [`TeachingTipElement`](/microsoft-ui-reactor/main/reference/elements/dialogs-and-flyouts#teachingtipelement), [`TextBlockElement`](/microsoft-ui-reactor/main/reference/elements/text#textblockelement), [`TextBoxElement`](/microsoft-ui-reactor/main/reference/elements/input#textboxelement), [`TimePickerElement`](/microsoft-ui-reactor/main/reference/elements/input#timepickerelement), [`TitleBarElement`](/microsoft-ui-reactor/main/reference/elements/navigation#titlebarelement), [`ToggleButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#togglebuttonelement), [`ToggleSplitButtonElement`](/microsoft-ui-reactor/main/reference/elements/buttons#togglesplitbuttonelement), [`ToggleSwitchElement`](/microsoft-ui-reactor/main/reference/elements/input#toggleswitchelement), [`TreeViewElement`](/microsoft-ui-reactor/main/reference/elements/collections#treeviewelement), [`ViewboxElement`](/microsoft-ui-reactor/main/reference/elements/layout#viewboxelement), [`WebView2Element`](/microsoft-ui-reactor/main/reference/elements/interop#webview2element), [`WrapGridElement`](/microsoft-ui-reactor/main/reference/elements/layout#wrapgridelement), [`ItemsViewElement<T>`](/microsoft-ui-reactor/main/reference/elements/collections#itemsviewelementt), [`LazyHStackElement<T>`](/microsoft-ui-reactor/main/reference/elements/collections#lazyhstackelementt), [`LazyVStackElement<T>`](/microsoft-ui-reactor/main/reference/elements/collections#lazyvstackelementt), [`TemplatedFlipViewElement<T>`](/microsoft-ui-reactor/main/reference/elements/collections#templatedflipviewelementt), [`TemplatedGridViewElement<T>`](/microsoft-ui-reactor/main/reference/elements/collections#templatedgridviewelementt), [`TemplatedListViewElement<T>`](/microsoft-ui-reactor/main/reference/elements/collections#templatedlistviewelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `setter` | `Action<CanvasAnimatedControl>` or `Action<CanvasControl>` or `Action<CanvasVirtualControl>` |  |
| `configure` | `Action<AnimatedIcon>` or `Action<AnimatedVisualPlayer>` or `Action<AnnotatedScrollBar>` or `Action<AutoSuggestBox>` or `Action<Border>` or `Action<BreadcrumbBar>` or `Action<Button>` or `Action<CalendarDatePicker>` or `Action<CalendarView>` or `Action<Canvas>` or `Action<CheckBox>` or `Action<ColorPicker>` or `Action<ComboBox>` or `Action<CommandBar>` or `Action<CommandBarFlyout>` or `Action<ContentDialog>` or `Action<DatePicker>` or `Action<DropDownButton>` or `Action<Ellipse>` or `Action<Expander>` or `Action<FlexPanel>` or `Action<FlipView>` or `Action<Flyout>` or `Action<Frame>` or `Action<Grid>` or `Action<GridView>` or `Action<HyperlinkButton>` or `Action<IconElement>` or `Action<Image>` or `Action<InfoBadge>` or `Action<InfoBar>` or `Action<ItemContainer>` or `Action<Line>` or `Action<ListBox>` or `Action<ListView>` or `Action<MapControl>` or `Action<MediaPlayerElement>` or `Action<MenuBar>` or `Action<MenuFlyout>` or `Action<NavigationView>` or `Action<NumberBox>` or `Action<ParallaxView>` or `Action<PasswordBox>` or `Action<Path>` or `Action<PersonPicture>` or `Action<PipsPager>` or `Action<Pivot>` or `Action<Popup>` or `Action<ProgressBar>` or `Action<ProgressRing>` or `Action<RadioButton>` or `Action<RadioButtons>` or `Action<RatingControl>` or `Action<Rectangle>` or `Action<RefreshContainer>` or `Action<RelativePanel>` or `Action<RepeatButton>` or `Action<RichEditBox>` or `Action<RichTextBlock>` or `Action<ScrollView>` or `Action<ScrollViewer>` or `Action<SelectorBar>` or `Action<SemanticZoom>` or `Action<Slider>` or `Action<SplitButton>` or `Action<SplitView>` or `Action<StackPanel>` or `Action<SwipeControl>` or `Action<TabView>` or `Action<TeachingTip>` or `Action<TextBlock>` or `Action<TextBox>` or `Action<TimePicker>` or `Action<TitleBar>` or `Action<ToggleButton>` or `Action<ToggleSplitButton>` or `Action<ToggleSwitch>` or `Action<TreeView>` or `Action<Viewbox>` or `Action<WebView2>` or `Action<VariableSizedWrapGrid>` or `Action<ItemsView>` |  |
