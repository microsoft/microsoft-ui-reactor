# Modifiers: Native properties

The Set modifier, which changes the real WinUI control directly.

Modifiers are extension methods that return a changed copy of the element, so you can chain them.

**Learn more:** [Modifier System](../../modifier-system.md), [Coming from XAML](../../xaml-developers.md)

## `Set`

```csharp
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

Declared in `ElementExtensions`.

**Applies to:** [`AnimatedIconElement`](../elements/media.md#animatediconelement), [`AnimatedVisualPlayerElement`](../elements/media.md#animatedvisualplayerelement), [`AnnotatedScrollBarElement`](../elements/layout.md#annotatedscrollbarelement), [`AutoSuggestBoxElement`](../elements/input.md#autosuggestboxelement), [`BorderElement`](../elements/layout.md#borderelement), [`BreadcrumbBarElement`](../elements/navigation.md#breadcrumbbarelement), [`ButtonElement`](../elements/buttons.md#buttonelement), [`CalendarDatePickerElement`](../elements/input.md#calendardatepickerelement), [`CalendarViewElement`](../elements/input.md#calendarviewelement), [`CanvasElement`](../elements/layout.md#canvaselement), [`CheckBoxElement`](../elements/input.md#checkboxelement), [`ColorPickerElement`](../elements/input.md#colorpickerelement), [`ComboBoxElement`](../elements/input.md#comboboxelement), [`CommandBarElement`](../elements/menus-and-commands.md#commandbarelement), [`CommandBarFlyoutElement`](../elements/menus-and-commands.md#commandbarflyoutelement), [`ContentDialogElement`](../elements/dialogs-and-flyouts.md#contentdialogelement), [`DatePickerElement`](../elements/input.md#datepickerelement), [`DropDownButtonElement`](../elements/buttons.md#dropdownbuttonelement), [`EllipseElement`](../elements/shapes.md#ellipseelement), [`ExpanderElement`](../elements/layout.md#expanderelement), [`FlexElement`](../elements/layout.md#flexelement), [`FlipViewElement`](../elements/collections.md#flipviewelement), [`FlyoutElement`](../elements/dialogs-and-flyouts.md#flyoutelement), [`FrameElement`](../elements/navigation.md#frameelement), [`GridElement`](../elements/layout.md#gridelement), [`GridViewElement`](../elements/collections.md#gridviewelement), [`HyperlinkButtonElement`](../elements/buttons.md#hyperlinkbuttonelement), [`IconElement`](../elements/media.md#iconelement), [`ImageElement`](../elements/media.md#imageelement), [`InfoBadgeElement`](../elements/status-and-info.md#infobadgeelement), [`InfoBarElement`](../elements/status-and-info.md#infobarelement), [`ItemContainerElement`](../elements/collections.md#itemcontainerelement), [`LineElement`](../elements/shapes.md#lineelement), [`ListBoxElement`](../elements/collections.md#listboxelement), [`ListViewElement`](../elements/collections.md#listviewelement), [`MapControlElement`](../elements/interop.md#mapcontrolelement), [`MediaPlayerElementElement`](../elements/media.md#mediaplayerelementelement), [`MenuBarElement`](../elements/menus-and-commands.md#menubarelement), [`MenuFlyoutElement`](../elements/menus-and-commands.md#menuflyoutelement), [`NavigationViewElement`](../elements/navigation.md#navigationviewelement), [`NumberBoxElement`](../elements/input.md#numberboxelement), [`ParallaxViewElement`](../elements/layout.md#parallaxviewelement), [`PasswordBoxElement`](../elements/input.md#passwordboxelement), [`PathElement`](../elements/shapes.md#pathelement), [`PersonPictureElement`](../elements/media.md#personpictureelement), [`PipsPagerElement`](../elements/navigation.md#pipspagerelement), [`PivotElement`](../elements/navigation.md#pivotelement), [`PopupElement`](../elements/dialogs-and-flyouts.md#popupelement), [`ProgressElement`](../elements/status-and-info.md#progresselement), [`ProgressRingElement`](../elements/status-and-info.md#progressringelement), [`RadioButtonElement`](../elements/input.md#radiobuttonelement), [`RadioButtonsElement`](../elements/input.md#radiobuttonselement), [`RatingControlElement`](../elements/input.md#ratingcontrolelement), [`RectangleElement`](../elements/shapes.md#rectangleelement), [`RefreshContainerElement`](../elements/collections.md#refreshcontainerelement), [`RelativePanelElement`](../elements/layout.md#relativepanelelement), [`RepeatButtonElement`](../elements/buttons.md#repeatbuttonelement), [`RichEditBoxElement`](../elements/input.md#richeditboxelement), [`RichTextBlockElement`](../elements/text.md#richtextblockelement), [`ScrollViewElement`](../elements/layout.md#scrollviewelement), [`ScrollViewerElement`](../elements/layout.md#scrollviewerelement), [`SelectorBarElement`](../elements/navigation.md#selectorbarelement), [`SemanticZoomElement`](../elements/collections.md#semanticzoomelement), [`SliderElement`](../elements/input.md#sliderelement), [`SplitButtonElement`](../elements/buttons.md#splitbuttonelement), [`SplitViewElement`](../elements/layout.md#splitviewelement), [`StackElement`](../elements/layout.md#stackelement), [`SwipeControlElement`](../elements/collections.md#swipecontrolelement), [`TabViewElement`](../elements/navigation.md#tabviewelement), [`TeachingTipElement`](../elements/dialogs-and-flyouts.md#teachingtipelement), [`TextBlockElement`](../elements/text.md#textblockelement), [`TextBoxElement`](../elements/input.md#textboxelement), [`TimePickerElement`](../elements/input.md#timepickerelement), [`TitleBarElement`](../elements/navigation.md#titlebarelement), [`ToggleButtonElement`](../elements/buttons.md#togglebuttonelement), [`ToggleSplitButtonElement`](../elements/buttons.md#togglesplitbuttonelement), [`ToggleSwitchElement`](../elements/input.md#toggleswitchelement), [`TreeViewElement`](../elements/collections.md#treeviewelement), [`ViewboxElement`](../elements/layout.md#viewboxelement), [`WebView2Element`](../elements/interop.md#webview2element), [`WrapGridElement`](../elements/layout.md#wrapgridelement), [`ItemsViewElement<T>`](../elements/collections.md#itemsviewelementt), [`LazyHStackElement<T>`](../elements/collections.md#lazyhstackelementt), [`LazyVStackElement<T>`](../elements/collections.md#lazyvstackelementt), [`TemplatedFlipViewElement<T>`](../elements/collections.md#templatedflipviewelementt), [`TemplatedGridViewElement<T>`](../elements/collections.md#templatedgridviewelementt), [`TemplatedListViewElement<T>`](../elements/collections.md#templatedlistviewelementt).

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `configure` | `Action<AnimatedIcon>` or `Action<AnimatedVisualPlayer>` or `Action<AnnotatedScrollBar>` or `Action<AutoSuggestBox>` or `Action<Border>` or `Action<BreadcrumbBar>` or `Action<Button>` or `Action<CalendarDatePicker>` or `Action<CalendarView>` or `Action<Canvas>` or `Action<CheckBox>` or `Action<ColorPicker>` or `Action<ComboBox>` or `Action<CommandBar>` or `Action<CommandBarFlyout>` or `Action<ContentDialog>` or `Action<DatePicker>` or `Action<DropDownButton>` or `Action<Ellipse>` or `Action<Expander>` or `Action<FlexPanel>` or `Action<FlipView>` or `Action<Flyout>` or `Action<Frame>` or `Action<Grid>` or `Action<GridView>` or `Action<HyperlinkButton>` or `Action<IconElement>` or `Action<Image>` or `Action<InfoBadge>` or `Action<InfoBar>` or `Action<ItemContainer>` or `Action<Line>` or `Action<ListBox>` or `Action<ListView>` or `Action<MapControl>` or `Action<MediaPlayerElement>` or `Action<MenuBar>` or `Action<MenuFlyout>` or `Action<NavigationView>` or `Action<NumberBox>` or `Action<ParallaxView>` or `Action<PasswordBox>` or `Action<Path>` or `Action<PersonPicture>` or `Action<PipsPager>` or `Action<Pivot>` or `Action<Popup>` or `Action<ProgressBar>` or `Action<ProgressRing>` or `Action<RadioButton>` or `Action<RadioButtons>` or `Action<RatingControl>` or `Action<Rectangle>` or `Action<RefreshContainer>` or `Action<RelativePanel>` or `Action<RepeatButton>` or `Action<RichEditBox>` or `Action<RichTextBlock>` or `Action<ScrollView>` or `Action<ScrollViewer>` or `Action<SelectorBar>` or `Action<SemanticZoom>` or `Action<Slider>` or `Action<SplitButton>` or `Action<SplitView>` or `Action<StackPanel>` or `Action<SwipeControl>` or `Action<TabView>` or `Action<TeachingTip>` or `Action<TextBlock>` or `Action<TextBox>` or `Action<TimePicker>` or `Action<TitleBar>` or `Action<ToggleButton>` or `Action<ToggleSplitButton>` or `Action<ToggleSwitch>` or `Action<TreeView>` or `Action<Viewbox>` or `Action<WebView2>` or `Action<VariableSizedWrapGrid>` or `Action<ItemsView>` |  |
