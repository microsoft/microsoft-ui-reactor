// Spec-048 §3.4 close-out (option A) — public, opt-in bulk registration of the
// built-in control catalog.
//
// Background (issue #486): spec-048 §3.4 deleted the eager
// `Reconciler.RegisterV1BuiltInHandlers()` bootstrap so the trimmer can drop
// every built-in handler/control an app never reaches. Registration now happens
// lazily, the first time a factory (e.g. `Factories.TextBlock(...)`) is called —
// the factory body carries the `Reg<…>.Done` touch that registers the handler.
//
// That breaks the deliberately-documented **direct-record-initializer idiom**
// (`new TextBlockElement(text) { … }`, spec 034 §B / docs/guide/advanced.md
// "Hot loops"): it bypasses the factory, so without a prior factory touch the
// handler is never registered and the reconciler throws on first mount.
//
// `RegisterAllBuiltIns()` is the opt-in escape hatch. An app that wants the full
// catalog available (so any element record — factory-built or direct-built —
// mounts) calls this once at startup. An app that wants a small trimmed binary
// simply does NOT call it and lets each factory register only what it uses.
//
// **Why this is trim-safe (and `[ModuleInitializer]` is not).** A
// `[ModuleInitializer]` is an unconditional trimmer/AOT root: it runs on first
// type-load of the assembly, so the trimmer can never prove it dead, and its
// body names every handler+control → the whole catalog is kept, defeating the
// trim story (spec §4, §132-141). `RegisterAllBuiltIns()` is an ordinary public
// method: NativeAOT's whole-program reachability analysis removes it (and every
// handler/control it names) when no reachable code calls it. The Hello-World
// trim-proof app (`tests/aot_trim_proof/Reactor.AotHelloWorld`) never calls it,
// so the forbidden-symbol assertion stays green.
//
// **Keep in sync with `Dsl.cs`.** When a new built-in handler/descriptor is
// added, wire it both in its factory (production fan-out) and here. The test
// bootstrap (`tests/_shared/BuiltInHandlerBootstrap.cs`) delegates to this
// method, so this is the single catalog list.

using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using V1 = Microsoft.UI.Reactor.Core.V1Protocol;
using Desc = Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

public static partial class ReactorApp
{
    /// <summary>
    /// Registers every built-in Reactor control handler with the global
    /// <see cref="Core.V1Protocol.ControlRegistry"/> in one call.
    /// </summary>
    /// <remarks>
    /// <para><b>When to call.</b> Call this once at process startup if your app
    /// constructs element records directly (the <c>new TextBlockElement(…) { … }</c>
    /// hot-loop idiom documented in <c>docs/guide/advanced.md</c> "Hot loops")
    /// rather than exclusively through factory methods. Factory methods already
    /// self-register on first call, so an app that only ever builds UI through
    /// the fluent/factory DSL does not need this.</para>
    ///
    /// <para><b>Trimming.</b> This method is the deliberate opt-out of spec-048's
    /// lazy-registration trim story: calling it roots the entire built-in
    /// catalog, so a NativeAOT/trimmed build that calls it keeps every built-in
    /// handler and WinUI control. Apps that want a minimal trimmed binary should
    /// <i>not</i> call this and instead let each factory register only the
    /// controls it uses — or register specific controls explicitly:
    /// <see cref="Core.V1Protocol.ControlRegistry.Register{TElement,TControl}"/>
    /// for control-backed handlers, or
    /// <see cref="Core.V1Protocol.ControlRegistry.RegisterDecorator{TElement}"/>
    /// for decorator-backed elements (overlays, validation, composite wrappers).
    /// Because this is an ordinary method (not a <c>[ModuleInitializer]</c>),
    /// the trimmer removes it — and everything it names — when it is unreachable.</para>
    ///
    /// <para>Registration is idempotent and process-wide; calling it more than
    /// once is a cheap no-op after the first call.</para>
    /// </remarks>
    public static void RegisterAllBuiltIns()
    {
        // ── Descriptor-backed value controls ──
        // Generated/descriptor controls self-register via a Pattern-A static
        // cctor; fire it explicitly so direct-record callers don't depend on a
        // prior factory touch.
        RuntimeHelpers.RunClassConstructor(typeof(ToggleSwitchElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(SliderElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(TextBoxElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(BorderElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ViewboxElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ProgressRingElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ProgressElement).TypeHandle);
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
        RuntimeHelpers.RunClassConstructor(typeof(ButtonElement).TypeHandle);

        // ── Composite / validation decorators ──
        _ = V1.RegDecorator<Core.CommandHostElement, V1.Handlers.CommandHostHandler>.Done;
        _ = V1.RegDecorator<Controls.Validation.FormFieldElement, V1.Handlers.FormFieldHandler>.Done;
        _ = V1.RegDecorator<Controls.Validation.ValidationVisualizerElement, V1.Handlers.ValidationVisualizerHandler>.Done;
        _ = V1.RegDecorator<Controls.Validation.ValidationRuleElement, V1.Handlers.ValidationRuleHandler>.Done;

        // ── Base-derived (typed templated lists / lazy stacks / typed templated tree views / items hosts) ──
        _ = V1.RegBaseDecorator<TemplatedListElementBase, V1.Handlers.TemplatedListHandler>.Done;
        _ = V1.RegBaseDecorator<LazyStackElementBase, V1.Handlers.LazyStackHandler>.Done;
        _ = V1.RegBaseDecorator<TemplatedTreeViewElementBase, V1.Handlers.TemplatedTreeViewHandler>.Done;
        _ = Desc.ItemsRepeaterDescriptor.Registration.Done;
        _ = Desc.ItemsViewDescriptor.Registration.Done;

        // ── Standard concrete descriptors (alphabetical) ──
        RuntimeHelpers.RunClassConstructor(typeof(AnimatedIconElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(AnimatedVisualPlayerElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(AnnotatedScrollBarElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(AnnounceRegionElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(AutoSuggestBoxElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(CalendarDatePickerElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(CalendarViewElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(CanvasElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(CheckBoxElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ColorPickerElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ComboBoxElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(BreadcrumbBarElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(SelectorBarElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(SwipeControlElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(SemanticZoomElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(DatePickerElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(DropDownButtonElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(EllipseElement).TypeHandle);
        _ = V1.RegDecorator<ExpanderElement, V1.Handlers.ExpanderHandler>.Done;
        RuntimeHelpers.RunClassConstructor(typeof(FlexElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(FlipViewElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(FrameElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(GridElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(HyperlinkButtonElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ImageElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(InfoBadgeElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(InfoBarElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ItemContainerElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(LineElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ListBoxElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(MapControlElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(MediaPlayerElementElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(NavigationViewElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(NumberBoxElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ParallaxViewElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(PasswordBoxElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(PathElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(PersonPictureElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(PipsPagerElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(PivotElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(RadioButtonElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(RadioButtonsElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(RatingControlElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(RectangleElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(RefreshContainerElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(RelativePanelElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(RepeatButtonElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(RichEditBoxElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(RichTextBlockElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ScrollViewElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ScrollViewerElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(SemanticElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(SplitButtonElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(SplitViewElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(StackElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(TabViewElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(TeachingTipElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(TextBlockElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(TimePickerElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(TitleBarElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ToggleButtonElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(ToggleSplitButtonElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(TreeViewElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(WebView2Element).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(WrapGridElement).TypeHandle);

        // IconElement — generated polymorphic descriptor self-registers its
        // decorator via a Pattern-A static cctor.
        RuntimeHelpers.RunClassConstructor(typeof(Core.IconElement).TypeHandle);

        // XamlPageElement / XamlHostElement — generated monomorphic decorators
        // that self-register on first type load.
        RuntimeHelpers.RunClassConstructor(typeof(Hosting.XamlPageElement).TypeHandle);
        RuntimeHelpers.RunClassConstructor(typeof(Hosting.XamlHostElement).TypeHandle);
    }
}
