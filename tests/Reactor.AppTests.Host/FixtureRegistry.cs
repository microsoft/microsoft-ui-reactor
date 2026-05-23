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
    public static readonly string[] AllFixtures =
    [
        // Layout
        "FlexLayout_RowDistribution",
        "FlexLayout_ColumnWrap",
        "Grid_RowColumnLayout",
        "GridVsFlex_StarSizing",

        // Flex Layout (20 fixtures)
        "Flex_NestedRowInColumn",
        "Flex_NestedColumnInRow",
        "Flex_NestedDeep",
        "Flex_InsideGrid",
        "Flex_InsideBorder",
        "Flex_InsideVStack",
        "Flex_InsideScrollView",
        "Flex_ScrollViewInsideFlex",
        "Flex_ColumnGrow",
        "Flex_MixedGrowFixed",
        "Flex_WithGaps",
        "Flex_WithPadding",
        "Flex_CrossAxisTextHeight",
        "Flex_GridInsideFlex",
        "Flex_WithChildMargins",
        "Flex_JustifySpaceBetween",
        "Flex_LayoutCycleInGridStar",
        "Flex_LayoutCycleNestedDeep",
        "Flex_LayoutCycleAutoText",
        "Flex_LayoutCycleSizeMismatch",

        // Reconciler
        "Reconciler_MountText",
        "Reconciler_UpdateText",
        "Reconciler_AddRemoveChildren",
        "Reconciler_ComponentRerender",
        "Reconciler_KeyedList",
        "Reconciler_GridDynamicChildCount",
        "Reconciler_AllLayoutsDynamicChildCount",

        // Error Boundary
        "ErrorBoundary_CatchesRenderError",
        "ErrorBoundary_Recovery",

        // Dynamic
        "DynamicList_GrowShrink",
        "ConditionalRendering_Toggle",

        // Collections
        "ListView_TypedRendering",

        // Navigation
        "Navigation_TabSwitching",

        // Observable
        "Observable_UseObservable_Rerender",
        "Observable_UseObservable_ExternalMutation",
        "Observable_UseObservableProperty_FineGrained",
        "Observable_UseCollection_ListUpdates",
        "Observable_UseObservable_SourceSwap",

        // PropertyGrid
        "PropertyGrid_Reflection_MutableObject",
        "PropertyGrid_Reflection_Categorized",
        "PropertyGrid_Reflection_EnumEditor",
        "PropertyGrid_Nested_ImmutableRecord",
        "PropertyGrid_Immutable_Root",
        "PropertyGrid_Custom_Editor",
        "PropertyGrid_Target_Switching",
        "PropertyGrid_Category_ExpandCollapse",
        "PropertyGrid_DeepNesting_RecordInRecord",
        "PropertyGrid_INPC_ExternalMutation",

        // Markdown
        "Markdown_HeadingsAndFormatting",
        "Markdown_CodeBlockAndLinks",

        // D3 Charts
        "D3_LineChart",
        "D3_BarChart",
        "D3_PieChart",

        // Localization
        "Localization_LocaleSwitching",

        // Demo
        "Demo_Counter",
        "Demo_Conditional",
        "Demo_TabNavigation",

        // Navigation (UseNavigation + NavigationHost)
        "Navigation_MultiLevel",
        "Navigation_Guard",
        "Navigation_ViewIntegration",

        // Event Handlers (declarative modifiers)
        "EventHandler_Tapped",
        "EventHandler_SizeChanged",
        "EventHandler_PointerPressed",
        "EventHandler_KeyDown",
        "EventHandler_Typography",
        "EventHandler_UseReducer",

        // Validation pit-of-success (NumberBox.Immediate + Button.DisabledFocusable)
        "Validation_ImmediateAndDisabledFocusable",

        // Accessibility (validated via out-of-process UIA tests)
        "Accessibility_Showcase",
        "Accessibility_KeyboardNav",
        "Accessibility_LiveRegion",
        "Accessibility_UseAnnounce",
        "Accessibility_HeadingHierarchy",
        "Accessibility_AccessKey",
        "Accessibility_SemanticPanel",
        "Accessibility_LabeledBy",
        "Accessibility_TabNavigation",

        // Chart Accessibility (E2E UIA validation)
        "ChartAccessibility_Showcase",

        // DataGrid
        "DataGrid_EditableGrid",

        // Input & Gestures (spec 027 — E2E validation via WinAppDriver)
        "Gesture_Pan",
        "Gesture_DoubleTap",
        "Gesture_RightTap",
        "Gesture_LongPress",
        "DragDrop_TypedReorder",
        "DragDrop_TextFormat",

        // Devtools UX (spec 028 — E2E validation)
        "DevtoolsUx_MenuAndToggle",

        // Docking input (spec 045 — E2E validation of keyboard focus
        // across docking layout mutations)
        "DockingInput_TwoPaneTextBoxes",
        "DockingInput_TwoPaneTextBoxesNoPin",
    ];

    public static Element? Build(string name, RenderContext ctx) => name switch
    {
        // Layout
        "FlexLayout_RowDistribution" => LayoutFixtures.FlexRowDistribution(ctx),
        "FlexLayout_ColumnWrap" => LayoutFixtures.FlexColumnWrap(ctx),
        "Grid_RowColumnLayout" => LayoutFixtures.GridRowColumn(ctx),
        "GridVsFlex_StarSizing" => LayoutFixtures.GridVsFlexStarSizing(ctx),

        // Flex Layout
        "Flex_NestedRowInColumn" => FlexLayoutFixtures.FlexNestedRowInColumn(ctx),
        "Flex_NestedColumnInRow" => FlexLayoutFixtures.FlexNestedColumnInRow(ctx),
        "Flex_NestedDeep" => FlexLayoutFixtures.FlexNestedDeep(ctx),
        "Flex_InsideGrid" => FlexLayoutFixtures.FlexInsideGrid(ctx),
        "Flex_InsideBorder" => FlexLayoutFixtures.FlexInsideBorder(ctx),
        "Flex_InsideVStack" => FlexLayoutFixtures.FlexInsideVStack(ctx),
        "Flex_InsideScrollView" => FlexLayoutFixtures.FlexInsideScrollView(ctx),
        "Flex_ScrollViewInsideFlex" => FlexLayoutFixtures.ScrollViewInsideFlex(ctx),
        "Flex_ColumnGrow" => FlexLayoutFixtures.FlexColumnGrow(ctx),
        "Flex_MixedGrowFixed" => FlexLayoutFixtures.FlexMixedGrowFixed(ctx),
        "Flex_WithGaps" => FlexLayoutFixtures.FlexWithGaps(ctx),
        "Flex_WithPadding" => FlexLayoutFixtures.FlexWithPadding(ctx),
        "Flex_CrossAxisTextHeight" => FlexLayoutFixtures.FlexCrossAxisTextHeight(ctx),
        "Flex_GridInsideFlex" => FlexLayoutFixtures.GridInsideFlex(ctx),
        "Flex_WithChildMargins" => FlexLayoutFixtures.FlexWithChildMargins(ctx),
        "Flex_JustifySpaceBetween" => FlexLayoutFixtures.FlexJustifySpaceBetween(ctx),
        "Flex_LayoutCycleInGridStar" => FlexLayoutFixtures.FlexLayoutCycleInGridStar(ctx),
        "Flex_LayoutCycleNestedDeep" => FlexLayoutFixtures.FlexLayoutCycleNestedDeep(ctx),
        "Flex_LayoutCycleAutoText" => FlexLayoutFixtures.FlexLayoutCycleAutoText(ctx),
        "Flex_LayoutCycleSizeMismatch" => FlexLayoutFixtures.FlexLayoutCycleSizeMismatch(ctx),

        // Reconciler
        "Reconciler_MountText" => ReconcilerFixtures.MountText(ctx),
        "Reconciler_UpdateText" => ReconcilerFixtures.UpdateText(ctx),
        "Reconciler_AddRemoveChildren" => ReconcilerFixtures.AddRemoveChildren(ctx),
        "Reconciler_ComponentRerender" => ReconcilerFixtures.ComponentRerender(ctx),
        "Reconciler_KeyedList" => ReconcilerFixtures.KeyedList(ctx),
        "Reconciler_GridDynamicChildCount" => ReconcilerFixtures.GridDynamicChildCount(ctx),
        "Reconciler_AllLayoutsDynamicChildCount" => ReconcilerFixtures.AllLayoutsDynamicChildCount(ctx),

        // Error Boundary
        "ErrorBoundary_CatchesRenderError" => ErrorBoundaryFixtures.CatchesRenderError(ctx),
        "ErrorBoundary_Recovery" => ErrorBoundaryFixtures.Recovery(ctx),

        // Dynamic
        "DynamicList_GrowShrink" => DynamicFixtures.ListGrowShrink(ctx),
        "ConditionalRendering_Toggle" => DynamicFixtures.ConditionalToggle(ctx),

        // Collections
        "ListView_TypedRendering" => CollectionFixtures.ListViewTyped(ctx),

        // Navigation
        "Navigation_TabSwitching" => NavigationFixtures.TabSwitching(ctx),

        // Observable
        "Observable_UseObservable_Rerender" => ObservableFixtures.UseObservable_Rerender(ctx),
        "Observable_UseObservable_ExternalMutation" => ObservableFixtures.UseObservable_ExternalMutation(ctx),
        "Observable_UseObservableProperty_FineGrained" => ObservableFixtures.UseObservableProperty_FineGrained(ctx),
        "Observable_UseCollection_ListUpdates" => ObservableFixtures.UseCollection_ListUpdates(ctx),
        "Observable_UseObservable_SourceSwap" => ObservableFixtures.UseObservable_SourceSwap(ctx),

        // PropertyGrid
        "PropertyGrid_Reflection_MutableObject" => PropertyGridFixtures.Reflection_MutableObject(ctx),
        "PropertyGrid_Reflection_Categorized" => PropertyGridFixtures.Reflection_Categorized(ctx),
        "PropertyGrid_Reflection_EnumEditor" => PropertyGridFixtures.Reflection_EnumEditor(ctx),
        "PropertyGrid_Nested_ImmutableRecord" => PropertyGridFixtures.Nested_ImmutableRecord(ctx),
        "PropertyGrid_Immutable_Root" => PropertyGridFixtures.Immutable_Root(ctx),
        "PropertyGrid_Custom_Editor" => PropertyGridFixtures.Custom_Editor(ctx),
        "PropertyGrid_Target_Switching" => PropertyGridFixtures.Target_Switching(ctx),
        "PropertyGrid_Category_ExpandCollapse" => PropertyGridFixtures.Category_ExpandCollapse(ctx),
        "PropertyGrid_DeepNesting_RecordInRecord" => PropertyGridFixtures.DeepNesting_RecordInRecord(ctx),
        "PropertyGrid_INPC_ExternalMutation" => PropertyGridFixtures.INPC_ExternalMutation(ctx),

        // Markdown
        "Markdown_HeadingsAndFormatting" => MarkdownFixtures.HeadingsAndFormatting(ctx),
        "Markdown_CodeBlockAndLinks" => MarkdownFixtures.CodeBlockAndLinks(ctx),

        // D3 Charts
        "D3_LineChart" => D3Fixtures.LineChart(ctx),
        "D3_BarChart" => D3Fixtures.BarChart(ctx),
        "D3_PieChart" => D3Fixtures.PieChart(ctx),

        // Localization
        "Localization_LocaleSwitching" => LocalizationFixtures.LocaleSwitching(ctx),

        // Demo
        "Demo_Counter" => DemoFixtures.CounterDemo(ctx),
        "Demo_Conditional" => DemoFixtures.ConditionalDemo(ctx),
        "Demo_TabNavigation" => DemoFixtures.TabNavigation(ctx),

        // Navigation (UseNavigation + NavigationHost)
        "Navigation_MultiLevel" => NavigationFixtures.MultiLevelNav(ctx),
        "Navigation_Guard" => NavigationFixtures.NavGuard(ctx),
        "Navigation_ViewIntegration" => NavigationFixtures.NavViewIntegration(ctx),

        // Event Handlers (declarative modifiers)
        "EventHandler_Tapped" => EventHandlerFixtures.TappedTest(ctx),
        "EventHandler_SizeChanged" => EventHandlerFixtures.SizeChangedTest(ctx),
        "EventHandler_PointerPressed" => EventHandlerFixtures.PointerPressedTest(ctx),
        "EventHandler_KeyDown" => EventHandlerFixtures.KeyDownTest(ctx),
        "EventHandler_Typography" => EventHandlerFixtures.TypographyTest(ctx),
        "EventHandler_UseReducer" => EventHandlerFixtures.ReducerTest(ctx),

        // Validation pit-of-success
        "Validation_ImmediateAndDisabledFocusable" => ImmediateAndDisabledFocusableFixtures.Demo(ctx),

        // Accessibility
        "Accessibility_Showcase" => AccessibilityFixtures.AccessibilityShowcase(ctx),
        "Accessibility_KeyboardNav" => AccessibilityInteractionFixtures.KeyboardNavTest(ctx),
        "Accessibility_LiveRegion" => AccessibilityInteractionFixtures.LiveRegionStaticTest(ctx),
        "Accessibility_UseAnnounce" => AccessibilityInteractionFixtures.UseAnnounceTest(ctx),
        "Accessibility_HeadingHierarchy" => AccessibilityInteractionFixtures.HeadingHierarchyTest(ctx),
        "Accessibility_AccessKey" => AccessibilityInteractionFixtures.AccessKeyTest(ctx),
        "Accessibility_SemanticPanel" => AccessibilityInteractionFixtures.SemanticPanelTest(ctx),
        "Accessibility_LabeledBy" => AccessibilityInteractionFixtures.LabeledByTest(ctx),
        "Accessibility_TabNavigation" => AccessibilityInteractionFixtures.TabNavigationTest(ctx),

        // Chart Accessibility (E2E)
        "ChartAccessibility_Showcase" => AccessibilityFixtures.ChartAccessibilityShowcase(ctx),

        // DataGrid
        "DataGrid_EditableGrid" => DataGridFixtures.EditableGrid(ctx),

        // Devtools UX (spec 028 — E2E validation)
        "DevtoolsUx_MenuAndToggle" => DevtoolsUxFixtures.DevtoolsUxTest(ctx),

        // Input & Gestures (spec 027 — E2E validation)
        "Gesture_Pan" => GestureE2EFixtures.PanTest(ctx),
        "Gesture_DoubleTap" => GestureE2EFixtures.DoubleTapTest(ctx),
        "Gesture_RightTap" => GestureE2EFixtures.RightTapTest(ctx),
        "Gesture_LongPress" => GestureE2EFixtures.LongPressTest(ctx),
        "DragDrop_TypedReorder" => DragDropE2EFixtures.TypedReorderTest(ctx),
        "DragDrop_TextFormat" => DragDropE2EFixtures.TextFormatTest(ctx),

        // Docking input (spec 045 — E2E validation)
        "DockingInput_TwoPaneTextBoxes" => DockingInputE2EFixtures.TwoPaneTextBoxTest(ctx),
        "DockingInput_TwoPaneTextBoxesNoPin" => DockingInputE2EFixtures.TwoPaneTextBoxNoPinTest(ctx),

        _ => null,
    };
}
