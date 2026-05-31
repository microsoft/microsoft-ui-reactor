// Spec-048 §3.4 test bootstrap.
//
// `Reconciler.RegisterV1BuiltInHandlers()` was removed so the trimmer can drop
// unreferenced WinUI controls in shipping apps. Production code is expected to
// either (a) call a factory (e.g. `TextBlock("hi")`) which auto-registers via
// its closed-generic `Reg<>` cctor latch, or (b) call
// `ControlRegistry.Register<,>` explicitly.
//
// Test assemblies, however, exercise direct-record-ctor patterns extensively
// (`new TextBlockElement("hi")` — see issue #486). Forcing every test to call
// a factory first would be invasive and would mask genuine "missing handler"
// regressions. Instead, this file registers every built-in handler globally
// via a `[ModuleInitializer]` — equivalent to the legacy
// `RegisterV1BuiltInHandlers` body, but rooted in the test assembly so the
// shipping Reactor.dll trimmer story is preserved (the spec forbids
// `[ModuleInitializer]` in `Reactor.dll` itself precisely because it would
// unconditionally root every handler).
//
// Mirrors `Reconciler.RegisterV1BuiltInHandlers` 1:1 (order, contents) as of
// the §3.4 removal commit. Keep in sync with `Dsl.cs` whenever a new built-in
// handler/descriptor is added.

using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using V1 = Microsoft.UI.Reactor.Core.V1Protocol;
using Desc = Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using WinUI = Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinShapes = Microsoft.UI.Xaml.Shapes;
using SemanticPanel = Microsoft.UI.Reactor.Accessibility.SemanticPanel;

namespace Reactor.Tests.Bootstrap;

internal static class BuiltInHandlerBootstrap
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // ── Phase 1 hand-coded handlers ──
        _ = V1.Reg<ToggleSwitchElement, WinUI.ToggleSwitch, V1.Handlers.ToggleSwitchHandler>.Done;
        _ = V1.Reg<SliderElement, WinUI.Slider, V1.Handlers.SliderHandler>.Done;
        _ = V1.Reg<TextBoxElement, WinUI.TextBox, V1.Handlers.TextBoxHandler>.Done;
        _ = V1.Reg<BorderElement, WinUI.Border, V1.Handlers.BorderHandler>.Done;
        _ = V1.Reg<ListViewElement, WinUI.ListView, V1.Handlers.ListViewHandler>.Done;

        _ = V1.Reg<NavigationHostElement, WinUI.Grid, V1.Handlers.NavigationHostHandler>.Done;
        _ = V1.Reg<GridViewElement, WinUI.GridView, V1.Handlers.GridViewHandler>.Done;

        // ── Overlay / modal decorator handlers ──
        _ = V1.RegDecorator<ContentDialogElement, V1.Handlers.ContentDialogHandler>.Done;
        _ = V1.RegDecorator<FlyoutElement, V1.Handlers.FlyoutHandler>.Done;
        _ = V1.RegDecorator<MenuBarElement, V1.Handlers.MenuBarHandler>.Done;
        _ = V1.RegDecorator<CommandBarElement, V1.Handlers.CommandBarHandler>.Done;
        _ = V1.RegDecorator<MenuFlyoutElement, V1.Handlers.MenuFlyoutHandler>.Done;
        _ = V1.RegDecorator<PopupElement, V1.Handlers.PopupHandler>.Done;
        _ = V1.RegDecorator<CommandBarFlyoutElement, V1.Handlers.CommandBarFlyoutHandler>.Done;
        _ = V1.Reg<ButtonElement, WinUI.Button, Desc.ButtonDescriptorHandler>.Done;

        // ── Composite / validation decorators ──
        _ = V1.RegDecorator<Microsoft.UI.Reactor.Core.CommandHostElement, V1.Handlers.CommandHostHandler>.Done;
        _ = V1.RegDecorator<Microsoft.UI.Reactor.Controls.Validation.FormFieldElement, V1.Handlers.FormFieldHandler>.Done;
        _ = V1.RegDecorator<Microsoft.UI.Reactor.Controls.Validation.ValidationVisualizerElement, V1.Handlers.ValidationVisualizerHandler>.Done;
        _ = V1.RegDecorator<Microsoft.UI.Reactor.Controls.Validation.ValidationRuleElement, V1.Handlers.ValidationRuleHandler>.Done;

        // ── Base-derived (typed templated lists / lazy stacks / typed templated tree views / items hosts) ──
        _ = V1.RegBaseDecorator<TemplatedListElementBase, V1.Handlers.TemplatedListHandler>.Done;
        _ = V1.RegBaseDecorator<LazyStackElementBase, V1.Handlers.LazyStackHandler>.Done;
        _ = V1.RegBaseDecorator<TemplatedTreeViewElementBase, V1.Handlers.TemplatedTreeViewHandler>.Done;
        _ = Desc.ItemsRepeaterDescriptor.Registration.Done;
        _ = Desc.ItemsViewDescriptor.Registration.Done;

        // ── Standard concrete descriptors (alphabetical, mirrors RegisterV1BuiltInHandlers) ──
        _ = V1.Reg<AnimatedIconElement, WinUI.AnimatedIcon, Desc.AnimatedIconDescriptorHandler>.Done;
        _ = V1.Reg<AnimatedVisualPlayerElement, WinUI.AnimatedVisualPlayer, Desc.AnimatedVisualPlayerDescriptorHandler>.Done;
        _ = V1.Reg<AnnotatedScrollBarElement, WinUI.AnnotatedScrollBar, Desc.AnnotatedScrollBarDescriptorHandler>.Done;
        _ = V1.Reg<AnnounceRegionElement, WinUI.TextBlock, Desc.AnnounceRegionDescriptorHandler>.Done;
        _ = V1.Reg<AutoSuggestBoxElement, WinUI.AutoSuggestBox, Desc.AutoSuggestBoxDescriptorHandler>.Done;
        _ = V1.Reg<BreadcrumbBarElement, WinUI.BreadcrumbBar, Desc.BreadcrumbBarDescriptorHandler>.Done;
        _ = V1.Reg<CalendarDatePickerElement, WinUI.CalendarDatePicker, Desc.CalendarDatePickerDescriptorHandler>.Done;
        _ = V1.Reg<CalendarViewElement, WinUI.CalendarView, Desc.CalendarViewDescriptorHandler>.Done;
        _ = V1.Reg<CanvasElement, WinUI.Canvas, Desc.CanvasDescriptorHandler>.Done;
        _ = V1.RegDecorator<CheckBoxElement, V1.Handlers.CheckBoxHandler>.Done;
        _ = V1.Reg<ColorPickerElement, WinUI.ColorPicker, Desc.ColorPickerDescriptorHandler>.Done;
        _ = V1.Reg<ComboBoxElement, WinUI.ComboBox, Desc.ComboBoxDescriptorHandler>.Done;
        _ = V1.Reg<DatePickerElement, WinUI.DatePicker, Desc.DatePickerDescriptorHandler>.Done;
        _ = V1.Reg<DropDownButtonElement, WinUI.DropDownButton, Desc.DropDownButtonDescriptorHandler>.Done;
        _ = V1.Reg<EllipseElement, WinShapes.Ellipse, Desc.EllipseDescriptorHandler>.Done;
        _ = V1.RegDecorator<ExpanderElement, V1.Handlers.ExpanderHandler>.Done;
        _ = V1.Reg<FlexElement, Microsoft.UI.Reactor.Layout.FlexPanel, Desc.FlexPanelDescriptorHandler>.Done;
        _ = V1.Reg<FlipViewElement, WinUI.FlipView, Desc.FlipViewDescriptorHandler>.Done;
        _ = V1.Reg<FrameElement, WinUI.Frame, Desc.FrameDescriptorHandler>.Done;
        _ = V1.Reg<GridElement, WinUI.Grid, Desc.GridDescriptorHandler>.Done;
        _ = V1.Reg<HyperlinkButtonElement, WinUI.HyperlinkButton, Desc.HyperlinkButtonDescriptorHandler>.Done;
        _ = V1.Reg<ImageElement, WinUI.Image, Desc.ImageDescriptorHandler>.Done;
        _ = V1.Reg<InfoBadgeElement, WinUI.InfoBadge, Desc.InfoBadgeDescriptorHandler>.Done;
        _ = V1.Reg<InfoBarElement, WinUI.InfoBar, Desc.InfoBarDescriptorHandler>.Done;
        _ = V1.Reg<ItemContainerElement, WinUI.ItemContainer, Desc.ItemContainerDescriptorHandler>.Done;
        _ = V1.Reg<LineElement, WinShapes.Line, Desc.LineDescriptorHandler>.Done;
        _ = V1.Reg<ListBoxElement, WinUI.ListBox, Desc.ListBoxDescriptorHandler>.Done;
        _ = V1.Reg<MapControlElement, WinUI.MapControl, Desc.MapControlDescriptorHandler>.Done;
        _ = V1.Reg<MediaPlayerElementElement, WinUI.MediaPlayerElement, Desc.MediaPlayerElementDescriptorHandler>.Done;
        _ = V1.Reg<NavigationViewElement, WinUI.NavigationView, Desc.NavigationViewDescriptorHandler>.Done;
        _ = V1.Reg<NumberBoxElement, WinUI.NumberBox, Desc.NumberBoxDescriptorHandler>.Done;
        _ = V1.Reg<ParallaxViewElement, WinUI.ParallaxView, Desc.ParallaxViewDescriptorHandler>.Done;
        _ = V1.Reg<PasswordBoxElement, WinUI.PasswordBox, Desc.PasswordBoxDescriptorHandler>.Done;
        _ = V1.Reg<PathElement, WinShapes.Path, Desc.PathDescriptorHandler>.Done;
        _ = V1.Reg<PersonPictureElement, WinUI.PersonPicture, Desc.PersonPictureDescriptorHandler>.Done;
        _ = V1.Reg<PipsPagerElement, WinUI.PipsPager, Desc.PipsPagerDescriptorHandler>.Done;
        _ = V1.Reg<PivotElement, WinUI.Pivot, Desc.PivotDescriptorHandler>.Done;
        _ = V1.Reg<ProgressElement, WinUI.ProgressBar, Desc.ProgressBarDescriptorHandler>.Done;
        _ = V1.Reg<ProgressRingElement, WinUI.ProgressRing, Desc.ProgressRingDescriptorHandler>.Done;
        _ = V1.Reg<RadioButtonElement, WinUI.RadioButton, Desc.RadioButtonDescriptorHandler>.Done;
        _ = V1.Reg<RadioButtonsElement, WinUI.RadioButtons, Desc.RadioButtonsDescriptorHandler>.Done;
        _ = V1.Reg<RatingControlElement, WinUI.RatingControl, Desc.RatingControlDescriptorHandler>.Done;
        _ = V1.Reg<RectangleElement, WinShapes.Rectangle, Desc.RectangleDescriptorHandler>.Done;
        _ = V1.Reg<RefreshContainerElement, WinUI.RefreshContainer, Desc.RefreshContainerDescriptorHandler>.Done;
        _ = V1.Reg<RelativePanelElement, WinUI.RelativePanel, Desc.RelativePanelDescriptorHandler>.Done;
        _ = V1.Reg<RepeatButtonElement, WinPrim.RepeatButton, Desc.RepeatButtonDescriptorHandler>.Done;
        _ = V1.Reg<RichEditBoxElement, WinUI.RichEditBox, Desc.RichEditBoxDescriptorHandler>.Done;
        _ = V1.Reg<RichTextBlockElement, WinUI.RichTextBlock, Desc.RichTextBlockDescriptorHandler>.Done;
        _ = V1.Reg<ScrollViewElement, WinUI.ScrollView, Desc.ScrollViewDescriptorHandler>.Done;
        _ = V1.Reg<ScrollViewerElement, WinUI.ScrollViewer, Desc.ScrollViewerDescriptorHandler>.Done;
        _ = V1.Reg<SelectorBarElement, WinUI.SelectorBar, Desc.SelectorBarDescriptorHandler>.Done;
        _ = V1.Reg<SemanticElement, SemanticPanel, Desc.SemanticDescriptorHandler>.Done;
        _ = V1.Reg<SemanticZoomElement, WinUI.SemanticZoom, Desc.SemanticZoomDescriptorHandler>.Done;
        _ = V1.Reg<SplitButtonElement, WinUI.SplitButton, Desc.SplitButtonDescriptorHandler>.Done;
        _ = V1.Reg<SplitViewElement, WinUI.SplitView, Desc.SplitViewDescriptorHandler>.Done;
        _ = V1.Reg<StackElement, WinUI.StackPanel, Desc.StackPanelDescriptorHandler>.Done;
        _ = V1.Reg<SwipeControlElement, WinUI.SwipeControl, Desc.SwipeControlDescriptorHandler>.Done;
        _ = V1.Reg<TabViewElement, WinUI.TabView, Desc.TabViewDescriptorHandler>.Done;
        _ = V1.Reg<TeachingTipElement, WinUI.TeachingTip, Desc.TeachingTipDescriptorHandler>.Done;
        _ = V1.Reg<TextBlockElement, WinUI.TextBlock, Desc.TextBlockDescriptorHandler>.Done;
        _ = V1.Reg<TimePickerElement, WinUI.TimePicker, Desc.TimePickerDescriptorHandler>.Done;
        _ = V1.Reg<TitleBarElement, WinUI.TitleBar, Desc.TitleBarDescriptorHandler>.Done;
        _ = V1.Reg<ToggleButtonElement, WinPrim.ToggleButton, Desc.ToggleButtonDescriptorHandler>.Done;
        _ = V1.Reg<ToggleSplitButtonElement, WinUI.ToggleSplitButton, Desc.ToggleSplitButtonDescriptorHandler>.Done;
        _ = V1.Reg<TreeViewElement, WinUI.TreeView, Desc.TreeViewDescriptorHandler>.Done;
        _ = V1.Reg<ViewboxElement, WinUI.Viewbox, Desc.ViewboxDescriptorHandler>.Done;
        _ = V1.Reg<WebView2Element, WinUI.WebView2, Desc.WebView2DescriptorHandler>.Done;
        _ = V1.Reg<WrapGridElement, WinUI.VariableSizedWrapGrid, Desc.WrapGridDescriptorHandler>.Done;

        // IconElement / XamlPageElement / XamlHostElement: fully-qualified
        // because the namespace contains other types of the same short name
        // (Microsoft.UI.Xaml.Controls.IconElement). The handler classes for
        // these three are private nested types, so RegDecorator<>'s new()
        // constraint can't bind to them — register via a static lambda that
        // returns the public Handler singleton instead.
        V1.ControlRegistry.RegisterDecorator<Microsoft.UI.Reactor.Core.IconElement>(
            static () => Desc.IconDescriptor.Handler);
        V1.ControlRegistry.RegisterDecorator<Microsoft.UI.Reactor.Hosting.XamlPageElement>(
            static () => Desc.XamlPageDescriptor.Handler);
        V1.ControlRegistry.RegisterDecorator<Microsoft.UI.Reactor.Hosting.XamlHostElement>(
            static () => Desc.XamlHostDescriptor.Handler);
    }
}
