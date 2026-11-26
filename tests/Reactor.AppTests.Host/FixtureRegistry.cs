using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.Fixtures;

namespace Microsoft.UI.Reactor.AppTests.Host;

/// <summary>
/// Maps fixture names to their Element-building methods.
/// Each fixture is either a static method (RenderContext -> Element) or
/// returns a Component via Component&lt;T&gt;().
/// </summary>
internal static class FixtureRegistry
{
    // Keep registration order for the E2E fixture navigator.
    private static readonly (string Name, Func<RenderContext, Element?> Factory)[] Registrations =
    [
        // Layout
        ("FlexLayout_RowDistribution", static ctx => LayoutFixtures.FlexRowDistribution(ctx)),
        ("FlexLayout_ColumnWrap", static ctx => LayoutFixtures.FlexColumnWrap(ctx)),
        ("Grid_RowColumnLayout", static ctx => LayoutFixtures.GridRowColumn(ctx)),
        ("GridVsFlex_StarSizing", static ctx => LayoutFixtures.GridVsFlexStarSizing(ctx)),

        // Flex Layout (20 fixtures)
        ("Flex_NestedRowInColumn", static ctx => FlexLayoutFixtures.FlexNestedRowInColumn(ctx)),
        ("Flex_NestedColumnInRow", static ctx => FlexLayoutFixtures.FlexNestedColumnInRow(ctx)),
        ("Flex_NestedDeep", static ctx => FlexLayoutFixtures.FlexNestedDeep(ctx)),
        ("Flex_InsideGrid", static ctx => FlexLayoutFixtures.FlexInsideGrid(ctx)),
        ("Flex_InsideBorder", static ctx => FlexLayoutFixtures.FlexInsideBorder(ctx)),
        ("Flex_InsideVStack", static ctx => FlexLayoutFixtures.FlexInsideVStack(ctx)),
        ("Flex_InsideScrollView", static ctx => FlexLayoutFixtures.FlexInsideScrollView(ctx)),
        ("Flex_ScrollViewInsideFlex", static ctx => FlexLayoutFixtures.ScrollViewInsideFlex(ctx)),
        ("Flex_ColumnGrow", static ctx => FlexLayoutFixtures.FlexColumnGrow(ctx)),
        ("Flex_MixedGrowFixed", static ctx => FlexLayoutFixtures.FlexMixedGrowFixed(ctx)),
        ("Flex_WithGaps", static ctx => FlexLayoutFixtures.FlexWithGaps(ctx)),
        ("Flex_WithPadding", static ctx => FlexLayoutFixtures.FlexWithPadding(ctx)),
        ("Flex_CrossAxisTextHeight", static ctx => FlexLayoutFixtures.FlexCrossAxisTextHeight(ctx)),
        ("Flex_GridInsideFlex", static ctx => FlexLayoutFixtures.GridInsideFlex(ctx)),
        ("Flex_WithChildMargins", static ctx => FlexLayoutFixtures.FlexWithChildMargins(ctx)),
        ("Flex_JustifySpaceBetween", static ctx => FlexLayoutFixtures.FlexJustifySpaceBetween(ctx)),
        ("Flex_LayoutCycleInGridStar", static ctx => FlexLayoutFixtures.FlexLayoutCycleInGridStar(ctx)),
        ("Flex_LayoutCycleNestedDeep", static ctx => FlexLayoutFixtures.FlexLayoutCycleNestedDeep(ctx)),
        ("Flex_LayoutCycleAutoText", static ctx => FlexLayoutFixtures.FlexLayoutCycleAutoText(ctx)),
        ("Flex_LayoutCycleSizeMismatch", static ctx => FlexLayoutFixtures.FlexLayoutCycleSizeMismatch(ctx)),

        // Reconciler
        ("Reconciler_MountText", static ctx => ReconcilerFixtures.MountText(ctx)),
        ("Reconciler_UpdateText", static ctx => ReconcilerFixtures.UpdateText(ctx)),
        ("Reconciler_AddRemoveChildren", static ctx => ReconcilerFixtures.AddRemoveChildren(ctx)),
        ("Reconciler_ComponentRerender", static ctx => ReconcilerFixtures.ComponentRerender(ctx)),
        ("Reconciler_KeyedList", static ctx => ReconcilerFixtures.KeyedList(ctx)),
        ("Reconciler_GridDynamicChildCount", static ctx => ReconcilerFixtures.GridDynamicChildCount(ctx)),
        ("Reconciler_AllLayoutsDynamicChildCount", static ctx => ReconcilerFixtures.AllLayoutsDynamicChildCount(ctx)),

        // Error Boundary
        ("ErrorBoundary_CatchesRenderError", static ctx => ErrorBoundaryFixtures.CatchesRenderError(ctx)),
        ("ErrorBoundary_Recovery", static ctx => ErrorBoundaryFixtures.Recovery(ctx)),

        // Dynamic
        ("DynamicList_GrowShrink", static ctx => DynamicFixtures.ListGrowShrink(ctx)),
        ("ConditionalRendering_Toggle", static ctx => DynamicFixtures.ConditionalToggle(ctx)),

        // Collections
        ("ListView_TypedRendering", static ctx => CollectionFixtures.ListViewTyped(ctx)),
        // Issue #951 — keyed list rows must not announce Reactor's internal row
        // identity, and must honor an author-declared item name (UIA-verified).
        ("KeyedList_ItemNames", static ctx => CollectionFixtures.KeyedListItemNames(ctx)),

        // ItemClick once-fire guard (issue #679 (a) — E2E real-pointer-click coverage of
        // the ListViewHandler once-subscribe contract across re-renders + items change)
        ("ItemClick_OnceFire", static ctx => ItemClickE2EFixtures.OnceFire(ctx)),
        ("ItemsHost_SkippedGesture", static ctx => Factories.Component<ItemsHostGestureFixture>()),

        // ItemClick toggle-path double-subscribe guard (issue #779 — E2E proof that
        // toggling OnItemClick off then on again still fires exactly once, on both
        // ListView and GridView)
        ("ItemClick_ToggleListView", static ctx => ItemClickE2EFixtures.ToggleListView(ctx)),
        ("ItemClick_ToggleGridView", static ctx => ItemClickE2EFixtures.ToggleGridView(ctx)),

        // Host theme-change re-resolution (issue #679 (b) — E2E proof that the host
        // ActualThemeChanged → RequestRender wiring re-resolves a concrete ResourceOverride
        // ThemeRef brush on a real root-theme change)
        ("ThemeChange_ResourceOverrideReResolve", static ctx => ThemeChangeE2EFixtures.ThemeReResolve(ctx)),

        // TreeView expand/collapse (mirrors ReactorGallery Basic TreeView —
        // E2E repro/guard for the item-body-collapse bug)
        ("TreeView_BasicTextTree", static ctx => TreeViewE2EFixtures.BasicTextTree(ctx)),

        // Navigation
        ("Navigation_TabSwitching", static ctx => NavigationFixtures.TabSwitching(ctx)),

        // NavigationView hierarchical expand/collapse (E2E repro/guard for the
        // re-render rebuild-clobber that collapsed expanded categories)
        ("NavigationView_Hierarchical", static ctx => NavigationViewE2EFixtures.HierarchicalNav(ctx)),

        // Observable
        ("Observable_UseObservable_Rerender", static ctx => ObservableFixtures.UseObservable_Rerender(ctx)),
        ("Observable_UseObservable_ExternalMutation", static ctx => ObservableFixtures.UseObservable_ExternalMutation(ctx)),
        ("Observable_UseObservableProperty_FineGrained", static ctx => ObservableFixtures.UseObservableProperty_FineGrained(ctx)),
        ("Observable_UseCollection_ListUpdates", static ctx => ObservableFixtures.UseCollection_ListUpdates(ctx)),
        ("Observable_UseObservable_SourceSwap", static ctx => ObservableFixtures.UseObservable_SourceSwap(ctx)),

        // PropertyGrid
        ("PropertyGrid_Reflection_MutableObject", static ctx => PropertyGridFixtures.Reflection_MutableObject(ctx)),
        ("PropertyGrid_Reflection_Categorized", static ctx => PropertyGridFixtures.Reflection_Categorized(ctx)),
        ("PropertyGrid_Reflection_EnumEditor", static ctx => PropertyGridFixtures.Reflection_EnumEditor(ctx)),
        ("PropertyGrid_Nested_ImmutableRecord", static ctx => PropertyGridFixtures.Nested_ImmutableRecord(ctx)),
        ("PropertyGrid_Immutable_Root", static ctx => PropertyGridFixtures.Immutable_Root(ctx)),
        ("PropertyGrid_Custom_Editor", static ctx => PropertyGridFixtures.Custom_Editor(ctx)),
        ("PropertyGrid_Target_Switching", static ctx => PropertyGridFixtures.Target_Switching(ctx)),
        ("PropertyGrid_Category_ExpandCollapse", static ctx => PropertyGridFixtures.Category_ExpandCollapse(ctx)),
        ("PropertyGrid_DeepNesting_RecordInRecord", static ctx => PropertyGridFixtures.DeepNesting_RecordInRecord(ctx)),
        ("PropertyGrid_INPC_ExternalMutation", static ctx => PropertyGridFixtures.INPC_ExternalMutation(ctx)),

        // Markdown
        ("Markdown_HeadingsAndFormatting", static ctx => MarkdownFixtures.HeadingsAndFormatting(ctx)),
        ("Markdown_CodeBlockAndLinks", static ctx => MarkdownFixtures.CodeBlockAndLinks(ctx)),
        ("Markdown_Selection_InputProof", static ctx => Factories.Component<MarkdownSelectionInputFixture>()),
        ("Markdown_Selection_Edges_Modern", static ctx => Factories.Component<MarkdownSelectionEdgesFixture, bool>(true)),
        ("Markdown_Selection_Edges_Legacy", static ctx => Factories.Component<MarkdownSelectionEdgesFixture, bool>(false)),

        // D3 Charts
        ("D3_LineChart", static ctx => D3Fixtures.LineChart(ctx)),
        ("D3_BarChart", static ctx => D3Fixtures.BarChart(ctx)),
        ("D3_PieChart", static ctx => D3Fixtures.PieChart(ctx)),
        ("D3_ChartKeyboardNav", static ctx => D3Fixtures.ChartKeyboardNav(ctx)),

        // Localization
        ("Localization_LocaleSwitching", static ctx => LocalizationFixtures.LocaleSwitching(ctx)),

        // Demo
        ("Demo_Counter", static ctx => DemoFixtures.CounterDemo(ctx)),
        ("Demo_Conditional", static ctx => DemoFixtures.ConditionalDemo(ctx)),
        ("Demo_TabNavigation", static ctx => DemoFixtures.TabNavigation(ctx)),

        // Navigation (UseNavigation + NavigationHost)
        ("Navigation_MultiLevel", static ctx => NavigationFixtures.MultiLevelNav(ctx)),
        ("Navigation_Guard", static ctx => NavigationFixtures.NavGuard(ctx)),
        ("Navigation_ViewIntegration", static ctx => NavigationFixtures.NavViewIntegration(ctx)),

        // Event Handlers (declarative modifiers)
        ("EventHandler_Tapped", static ctx => EventHandlerFixtures.TappedTest(ctx)),
        ("EventHandler_SizeChanged", static ctx => EventHandlerFixtures.SizeChangedTest(ctx)),
        ("EventHandler_PointerPressed", static ctx => EventHandlerFixtures.PointerPressedTest(ctx)),
        ("EventHandler_KeyDown", static ctx => EventHandlerFixtures.KeyDownTest(ctx)),
        ("EventHandler_Typography", static ctx => EventHandlerFixtures.TypographyTest(ctx)),
        ("EventHandler_UseReducer", static ctx => EventHandlerFixtures.ReducerTest(ctx)),

        // [WrapElementSlot] secondary element slot (E2E validation of the generated
        // ImperativeBridged bridge writing TabView.TabStripHeader)
        ("WrapElementSlot_TabStripHeader", static ctx => WrapElementSlotE2EFixtures.TabStripHeaderSlot(ctx)),

        // Validation pit-of-success (NumberBox.Immediate + Button.DisabledFocusable)
        ("Validation_ImmediateAndDisabledFocusable", static ctx => ImmediateAndDisabledFocusableFixtures.Demo(ctx)),
        // Accessibility (validated via out-of-process UIA tests)
        ("Accessibility_Showcase", static ctx => AccessibilityFixtures.AccessibilityShowcase(ctx)),
        ("Accessibility_KeyboardNav", static ctx => AccessibilityInteractionFixtures.KeyboardNavTest(ctx)),
        ("Accessibility_LiveRegion", static ctx => AccessibilityInteractionFixtures.LiveRegionStaticTest(ctx)),
        ("Accessibility_UseAnnounce", static ctx => AccessibilityInteractionFixtures.UseAnnounceTest(ctx)),
        ("Accessibility_HeadingHierarchy", static ctx => AccessibilityInteractionFixtures.HeadingHierarchyTest(ctx)),
        ("Accessibility_AccessKey", static ctx => AccessibilityInteractionFixtures.AccessKeyTest(ctx)),
        ("Accessibility_SemanticPanel", static ctx => AccessibilityInteractionFixtures.SemanticPanelTest(ctx)),
        ("Accessibility_LabeledBy", static ctx => AccessibilityInteractionFixtures.LabeledByTest(ctx)),
        ("Accessibility_TabNavigation", static ctx => AccessibilityInteractionFixtures.TabNavigationTest(ctx)),

        // Chart Accessibility (E2E UIA validation)
        ("ChartAccessibility_Showcase", static ctx => AccessibilityFixtures.ChartAccessibilityShowcase(ctx)),

        // DataGrid
        ("DataGrid_EditableGrid", static ctx => DataGridFixtures.EditableGrid(ctx)),
        ("DataGrid_RowEditGrid", static ctx => DataGridFixtures.RowEditGrid(ctx)),

        // Input & Gestures (spec 027 — E2E validation via WinAppDriver)
        ("Gesture_Pan", static ctx => GestureE2EFixtures.PanTest(ctx)),
        ("Gesture_DoubleTap", static ctx => GestureE2EFixtures.DoubleTapTest(ctx)),
        ("Gesture_RightTap", static ctx => GestureE2EFixtures.RightTapTest(ctx)),
        ("Gesture_LongPress", static ctx => GestureE2EFixtures.LongPressTest(ctx)),
        ("DragDrop_TypedReorder", static ctx => DragDropE2EFixtures.TypedReorderTest(ctx)),
        ("DragDrop_TextFormat", static ctx => DragDropE2EFixtures.TextFormatTest(ctx)),

        // Toolkit gallery pages, with read-only native observations for real-input tests.
        ("ToolkitInput_RangeSelector", static _ => ToolkitInputFixtures.Build("range")),
        ("ToolkitInput_RichSuggestBox", static _ => ToolkitInputFixtures.Build("rsb")),
        ("ToolkitInput_TokenizingTextBox", static _ => ToolkitInputFixtures.Build("tokens")),
        ("ToolkitInput_ContentSizer", static _ => ToolkitInputFixtures.Build("csz")),
        ("ToolkitInput_PropertySizer", static _ => ToolkitInputFixtures.Build("psz")),
        ("ToolkitInput_GridSplitter", static _ => ToolkitInputFixtures.Build("splitter")),

        // Docking input (spec 045 — E2E validation of keyboard focus
        // across docking layout mutations)
        ("DockingInput_TwoPaneTextBoxes", static ctx => DockingInputE2EFixtures.TwoPaneTextBoxTest(ctx)),
        ("DockingInput_TwoPaneTextBoxesNoPin", static ctx => DockingInputE2EFixtures.TwoPaneTextBoxNoPinTest(ctx)),

        // Docking tear-off (spec 045 §2.6 — E2E validation of the
        // immediate-tear-off pipeline with real WinAppDriver pointer
        // input)
        ("DockingTearOff_Flow", static ctx => DockingTearOffE2EFixtures.TearOffFlowTest(ctx)),
    ];

    public static readonly string[] AllFixtures = Registrations.Select(static entry => entry.Name).ToArray();

    private static readonly Dictionary<string, Func<RenderContext, Element?>> FactoriesByName =
        Registrations.ToDictionary(static entry => entry.Name, static entry => entry.Factory, StringComparer.Ordinal);

    public static Element? Build(string name, RenderContext ctx) =>
        name is not null && FactoriesByName.TryGetValue(name, out var factory) ? factory(ctx) : null;
}
