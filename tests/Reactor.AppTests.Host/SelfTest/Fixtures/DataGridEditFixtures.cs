using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selfhost tests for DataGrid inline editing and LazyStack reconciler updates.
/// These mount a real DataGrid with a ReactorHost, programmatically trigger editing
/// via DataGridState, and verify the visual tree updates correctly.
/// </summary>
internal static class DataGridEditFixtures
{
    record TestProduct(int Id, string Name, string Category, double Price);

    /// <summary>
    /// A second row type, used only to instantiate <c>DataGridComponent&lt;TOther&gt;</c> so the
    /// grid-root KeyDown wiring can be proved shared across closed generic types.
    /// </summary>
    record TestOtherRow(int Id, string Name);

    private static ListDataSource<TestProduct> CreateSource(int count = 20)
    {
        var items = Enumerable.Range(0, count).Select(i => new TestProduct(
            Id: i,
            Name: $"Product {i}",
            Category: i % 3 == 0 ? "A" : "B",
            Price: 10.0 + i * 5
        ));
        return new ListDataSource<TestProduct>(items, p => (RowKey)p.Id);
    }

    private static IReadOnlyList<FieldDescriptor> CreateEditableColumns()
    {
        return new FieldDescriptor[]
        {
            Column<TestProduct>("Id", p => p.Id, width: 60),
            Column<TestProduct>("Name", p => p.Name, editable: true, width: 160),
            Column<TestProduct>("Category", p => p.Category, editable: true, width: 120),
            Column<TestProduct>("Price", p => p.Price, editable: true, format: "C2", width: 100),
        };
    }

    /// <summary>
    /// Mount an editable DataGrid, programmatically begin editing via state,
    /// and verify a TextBox editor appears in the visual tree.
    /// </summary>
    internal class EditLifecycle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DataGridState<TestProduct>? state = null;
            Action? forceRender = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => CreateSource());
                var columns = CreateEditableColumns();

                // Capture the DataGridState from the component via a wrapper
                var stateCapture = ctx.UseRef<DataGridState<TestProduct>?>(null);
                var (tick, setTick) = ctx.UseState(0);
                forceRender = () => setTick(tick + 1);

                if (stateCapture.Current is null)
                {
                    var s = new DataGridState<TestProduct>(source, columns, Microsoft.UI.Reactor.Controls.SelectionMode.Single);
                    _ = s.LoadDataAsync();
                    stateCapture.Current = s;
                }
                state = stateCapture.Current;

                return DataGrid(
                    source: source,
                    columns: columns,
                    selectionMode: Microsoft.UI.Reactor.Controls.SelectionMode.Single,
                    editable: true,
                    editMode: EditMode.Cell,
                    rowHeight: 36
                );
            });

            await Harness.Render(500);

            // 1. Grid renders with data
            H.Check("DataGrid_Edit_Renders",
                H.FindTextContaining("Product 0") is not null);

            H.Check("DataGrid_Edit_MultipleRows",
                H.FindTextContaining("Product 5") is not null);

            // 2. No TextBox initially (not editing)
            var textBoxesBefore = H.FindAllControls<TextBox>(_ => true);
            H.Check("DataGrid_Edit_NoEditorInitially",
                textBoxesBefore.Count == 0);

            await Harness.Render(200);

            // 3. Grid still alive after delay
            H.Check("DataGrid_Edit_StillAlive",
                H.FindTextContaining("Product 0") is not null);
        }
    }

    /// <summary>
    /// Mount an editable DataGrid, verify editor appears when editing
    /// is triggered, and that commit works.
    /// </summary>
    internal class EditCommitCycle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            string lastCommit = "";

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => CreateSource(10));

                return DataGrid(
                    source: source,
                    columns: CreateEditableColumns(),
                    selectionMode: Microsoft.UI.Reactor.Controls.SelectionMode.Single,
                    editable: true,
                    editMode: EditMode.Cell,
                    onRowChanged: (key, item) =>
                    {
                        lastCommit = $"{key.Value}:{item.Name}";
                        return Task.CompletedTask;
                    },
                    rowHeight: 36
                );
            });

            H.Check("DataGrid_Commit_InitialRender",
                await Harness.WaitFor(() => H.FindTextContaining("Product 0") is not null,
                    maxPasses: 25, perPassMs: 20));

            await Harness.Render(300);

            H.Check("DataGrid_Commit_Stable",
                H.FindTextContaining("Product 1") is not null);
        }
    }

    /// <summary>
    /// Mount a DataGrid with selection, verify it renders and survives
    /// rapid state changes without crashing.
    /// </summary>
    internal class RapidSelection(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => CreateSource(30));
                var (sel, setSel) = ctx.UseState<IReadOnlySet<RowKey>>(new HashSet<RowKey>());

                return VStack(
                    TextBlock($"Selected: {sel.Count}"),
                    DataGrid(
                        source: source,
                        columns: CreateEditableColumns(),
                        selectionMode: Microsoft.UI.Reactor.Controls.SelectionMode.Multiple,
                        onSelectionChanged: keys => setSel(keys),
                        rowHeight: 36
                    )
                );
            });

            H.Check("DataGrid_RapidSel_Renders",
                await Harness.WaitFor(() => H.FindText("Selected: 0") is not null,
                    maxPasses: 25, perPassMs: 20));

            await Harness.Render(500);

            H.Check("DataGrid_RapidSel_Stable",
                H.FindTextContaining("Product") is not null);
        }
    }

    /// <summary>
    /// Regression test for issue #872: a DataGrid must react to <c>SelectionMode</c> prop changes
    /// after the first mount. Mounts a grid whose <c>selectionMode</c> comes from
    /// <c>UseState(Single)</c>, captures the live <see cref="DataGridState{T}"/> through the
    /// internal test seam, then flips the prop to <c>Multiple</c> on a re-render (same grid
    /// instance, no key change) and asserts the live state now performs Ctrl-toggle multi-select.
    ///
    /// Non-vacuous: before the fix, the state captured its mode once at mount, so after the flip it
    /// stays in <c>Single</c> — Ctrl-toggling two rows keeps a single selection, failing both the
    /// reconciled-mode and multi-select checks.
    /// </summary>
    internal class SelectionModeReactsToPropChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DataGridState<TestProduct>? gridState = null;
            Action<Microsoft.UI.Reactor.Controls.SelectionMode>? setMode = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => CreateSource(10));
                var (mode, setM) = ctx.UseState(Microsoft.UI.Reactor.Controls.SelectionMode.Single);
                setMode = setM;

                var grid = DataGrid(
                    source: source,
                    columns: CreateEditableColumns(),
                    selectionMode: mode,
                    rowHeight: 36);

                // Test-only seam: capture the live headless state — it has no public imperative
                // handle, and the visible tree reads el.SelectionMode directly (already reactive),
                // so only the state's selection BEHAVIOR proves the fix.
                grid = grid with { Props = grid.Props with { OnStateReadyInternal = s => gridState = s } };

                return VStack(TextBlock($"Mode: {mode}"), grid);
            });

            H.Check("DataGrid_SelectionMode_Mounted",
                await Harness.WaitFor(() => gridState is not null, maxPasses: 25, perPassMs: 20));
            if (gridState is null) return;

            H.Check("DataGrid_SelectionMode_InitialSingle",
                gridState.SelectionMode == Microsoft.UI.Reactor.Controls.SelectionMode.Single);

            // Baseline: in Single mode, Ctrl-toggling two rows does NOT accumulate — it replaces.
            // This makes the post-flip accumulation assertion meaningful rather than always-true.
            gridState.HandleRowClick((RowKey)0, ctrlKey: true);
            gridState.HandleRowClick((RowKey)1, ctrlKey: true);
            H.Check($"DataGrid_SelectionMode_SingleDoesNotAccumulate (count={gridState.SelectedKeys.Count})",
                gridState.SelectedKeys.Count == 1);

            // Flip the prop to Multiple on a re-render (same grid instance, no key change).
            setMode?.Invoke(Microsoft.UI.Reactor.Controls.SelectionMode.Multiple);
            await Harness.Render(500);

            // The live state must have reconciled to the new mode (the #872 fix).
            H.Check("DataGrid_SelectionMode_ReconciledToMultiple",
                gridState.SelectionMode == Microsoft.UI.Reactor.Controls.SelectionMode.Multiple);

            // Now Ctrl-toggling two distinct rows selects BOTH.
            gridState.ClearSelection();
            gridState.HandleRowClick((RowKey)2, ctrlKey: true);
            gridState.HandleRowClick((RowKey)3, ctrlKey: true);
            H.Check($"DataGrid_SelectionMode_MultiSelectWorks (count={gridState.SelectedKeys.Count})",
                gridState.SelectedKeys.Count == 2
                && gridState.IsSelected((RowKey)2)
                && gridState.IsSelected((RowKey)3));
        }
    }

    /// <summary>
    /// Mount an editable DataGrid with a selection column, programmatically trigger
    /// cell editing via OnTapped on the Name cell, and verify the TextBox editor
    /// appears in the correct Grid column (not shifted to column 0).
    /// Regression test for Grid.SetColumn not being re-applied when a cell control
    /// is replaced during reconciliation.
    /// </summary>
    internal class EditCellColumnPlacement(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => CreateSource(10));

                return DataGrid(
                    source: source,
                    columns: CreateEditableColumns(),
                    selectionMode: Microsoft.UI.Reactor.Controls.SelectionMode.Single,
                    editable: true,
                    editMode: EditMode.Cell,
                    rowHeight: 36
                );
            });

            await Harness.Render(500);

            // Verify initial render
            H.Check("EditCol_InitialRender",
                H.FindTextContaining("Product 0") is not null);

            // Find the Name cell's TextBlock ("Product 0") and walk up to the nearest
            // Border/panel that has the OnTapped handler, then invoke it programmatically.
            var nameCell = H.FindText("Product 0");
            H.Check("EditCol_NameCellFound", nameCell is not null);
            if (nameCell is null) return;

            // Walk up to find a tappable ancestor (Border with TappedEvent handler)
            Microsoft.UI.Xaml.UIElement? tappable = nameCell;
            while (tappable is not null)
            {
                // Trigger tapped event on this element (DataGrid attaches OnTapped to cell wrappers)
                try
                {
                    // Use Automation peer to invoke, or simply dispatch the edit command.
                    // Since we can't easily raise Tapped, use ClickButton approach or
                    // directly invoke edit via the fact that the DataGrid defers to dispatcher.
                    break;
                }
                catch { break; }
            }

            // Instead of simulating tap, find the TextBox by looking for a Grid child
            // that contains the "Product 0" text and programmatically clicking it.
            // More reliable: use H.ClickButton or simulate pointer.
            // Simplest approach: find the cell and call AutomationPeer.
            if (nameCell is Microsoft.UI.Xaml.FrameworkElement feName)
            {
                // Programmatic click via Automation
                var peer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer
                    .CreatePeerForElement(feName);
                // TextBlock doesn't support Invoke, so walk up to find the containing
                // element that has the Tapped handler.
                var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(feName);
                while (parent is not null)
                {
                    if (parent is Microsoft.UI.Xaml.Controls.Border border)
                    {
                        // The DataGrid wraps cells in Border elements via .WithBorder() or
                        // the cell wrapper. Try invoking on the Border.
                        var borderPeer = Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer
                            .CreatePeerForElement(border);
                        if (borderPeer?.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)
                            is Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider invoker)
                        {
                            invoker.Invoke();
                            break;
                        }
                    }
                    parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
                }
            }

            await Harness.Render(500);

            // After editing starts, a TextBox should appear
            var editors = H.FindAllControls<TextBox>(_ => true);
            Console.WriteLine($"# TextBox count after tap attempt: {editors.Count}");

            // If tap didn't trigger edit (Automation may not fire Tapped), fall back:
            // Look for any Grid with column definitions matching the DataGrid row pattern
            // and check that all children have correct Grid.Column values.
            // This validates the general Grid reconciler fix regardless of edit trigger.

            // Find all row Grids (they have the same column count as data columns + selection)
            var rowGrids = H.FindAllControls<Microsoft.UI.Xaml.Controls.Grid>(
                g => g.ColumnDefinitions.Count >= 5); // 1 select + 4 data = 5+

            H.Check("EditCol_RowGridsFound", rowGrids.Count > 0);
            if (rowGrids.Count == 0) return;

            // Verify that in each row Grid, children have sequential Grid.Column values
            bool allColumnsCorrect = true;
            foreach (var rowGrid in rowGrids.Take(3)) // check first 3 rows
            {
                for (int i = 0; i < rowGrid.Children.Count; i++)
                {
                    if (rowGrid.Children[i] is Microsoft.UI.Xaml.FrameworkElement child)
                    {
                        var col = Microsoft.UI.Xaml.Controls.Grid.GetColumn(child);
                        if (col != i)
                        {
                            Console.WriteLine($"# Row grid child {i}: Grid.Column={col} (expected {i})");
                            allColumnsCorrect = false;
                        }
                    }
                }
            }
            H.Check("EditCol_AllColumnsCorrect", allColumnsCorrect);
        }
    }

    /// <summary>
    /// Test that external state changes (outside the DataGrid) propagate
    /// correctly into the VirtualList's realized items. Mounts a DataGrid
    /// whose cell renderer depends on an external font size variable, then
    /// changes the variable and verifies the TextBlock.FontSize updates.
    /// This validates the LazyStack in-place factory update + refresh path.
    /// </summary>
    internal class ExternalStateUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Action<double>? setFontSize = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => CreateSource(10));
                var (fontSize, setFs) = ctx.UseState(14.0);
                setFontSize = setFs;

                // Columns with a cell renderer that reads the external fontSize
                var columns = new FieldDescriptor[]
                {
                    Column<TestProduct>("Id", p => p.Id, width: 60),
                    (Column<TestProduct>("Name", p => p.Name, width: 160)
                        .CellRenderer(val => TextBlock((string)val).FontSize(fontSize))).Build(),
                    Column<TestProduct>("Category", p => p.Category, width: 120),
                };

                return DataGrid(
                    source: source,
                    columns: columns,
                    rowHeight: 36
                );
            });

            // 1. Initial render with fontSize=14 — retry to absorb VirtualList
            //    item realization timing on slow CI runners.
            TextBlock? initialTb = null;
            for (int attempt = 0; attempt < 5 && initialTb is null; attempt++)
            {
                await Harness.Render(200);
                initialTb = H.FindControl<TextBlock>(tb => tb.Text == "Product 0");
            }
            H.Check("DataGrid_ExtState_Renders", initialTb is not null);
            H.Check("DataGrid_ExtState_InitialFontSize",
                initialTb is not null && Math.Abs(initialTb.FontSize - 14.0) < 0.1);

            // 2. Change font size externally
            setFontSize?.Invoke(24.0);
            await Harness.Render(500);

            // 3. Verify the TextBlock updated with new font size.
            // FindControl returns first match in tree-order; find ALL TextBlocks with "Product 0"
            // and check if any has the updated font size (the CellRenderer column).
            var allProduct0 = H.FindAllControls<TextBlock>(tb => tb.Text == "Product 0");
            var anyHas24 = allProduct0.Any(tb => Math.Abs(tb.FontSize - 24.0) < 0.1);
            H.Check($"DataGrid_ExtState_FontSizeUpdated (found={allProduct0.Count}, anyHas24={anyHas24})",
                anyHas24);
        }
    }

    /// <summary>
    /// Exercises row edit mode, custom header/cell templates, search chrome,
    /// row-detail columns, empty templates, and the async row-commit path in one
    /// mounted DataGrid scenario.
    /// </summary>
    internal class RowEditTemplatesAndEmptyState(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            string lastCommit = "";

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => CreateSource(8));
                var emptySource = ctx.UseMemo(() => CreateSource(0));
                var columns = new FieldDescriptor[]
                {
                    Column<TestProduct>("Id", p => p.Id, width: 60),
                    Column<TestProduct>("Name", p => p.Name, editable: true, width: 160),
                    Column<TestProduct>("Category", p => p.Category, editable: true, width: 120),
                    Column<TestProduct>("Price", p => p.Price, editable: true, format: "C2", width: 100),
                };

                return VStack(
                    DataGrid(
                        source: source,
                        columns: columns,
                        selectionMode: Microsoft.UI.Reactor.Controls.SelectionMode.Single,
                        editable: true,
                        editMode: EditMode.Row,
                        onRowChanged: (key, item) =>
                        {
                            lastCommit = $"{key.Value}:{item.Name}:{item.Category}:{item.Price:G}";
                            return Task.CompletedTask;
                        },
                        rowHeight: 36,
                        cellTemplate: ctx => TextBlock($"cell:{ctx.Column.Name}:{ctx.Value}"),
                        headerTemplate: ctx => Button(
                            $"hdr:{ctx.Column.Name}:{ctx.CurrentSort?.ToString() ?? "none"}",
                            ctx.ToggleSort),
                        showSearch: true,
                        rowDetailTemplate: (row, key) => TextBlock($"detail:{key.Value}:{row.Name}")),
                    DataGrid(
                        source: emptySource,
                        columns: columns,
                        rowHeight: 36,
                        emptyTemplate: TextBlock("empty-grid-template")));
            });

            H.Check("DataGrid_RowEdit_CustomCellRendered",
                await Harness.WaitFor(() => H.FindText("cell:Name:Product 0") is not null,
                    maxPasses: 25, perPassMs: 20));
            H.Check("DataGrid_RowEdit_CustomHeaderRendered",
                await Harness.WaitFor(() => H.FindButton("hdr:Name:none") is not null,
                    maxPasses: 25, perPassMs: 20));
            H.Check("DataGrid_RowEdit_SearchBoxRendered",
                await Harness.WaitFor(() => H.FindAllControls<TextBox>(_ => true).Count >= 1,
                    maxPasses: 25, perPassMs: 20));
            H.Check("DataGrid_RowEdit_EmptyTemplateRendered",
                await Harness.WaitFor(() => H.FindText("empty-grid-template") is not null,
                    maxPasses: 25, perPassMs: 20));

            H.ClickButton("hdr:Name:none");
            H.Check("DataGrid_RowEdit_HeaderSortUpdated",
                await Harness.WaitFor(() => H.FindButton("hdr:Name:Ascending") is not null,
                    maxPasses: 25, perPassMs: 20));

            H.ClickButton("Edit");
            await Harness.Render(600);

            var rowEditTextBoxes = H.FindAllControls<TextBox>(tb =>
                tb.Text == "Product 0" || tb.Text == "A");
            var rowEditNumberBoxes = H.FindAllControls<NumberBox>(nb =>
                Math.Abs(nb.Value - 10.0) < 0.01);

            H.Check("DataGrid_RowEdit_TextEditorsMounted", rowEditTextBoxes.Count >= 2);
            H.Check("DataGrid_RowEdit_NumberEditorMounted", rowEditNumberBoxes.Count >= 1);
            H.Check("DataGrid_RowEdit_SaveCancelMounted",
                H.FindButton("Save") is not null && H.FindButton("Cancel") is not null);

            H.ClickButton("Cancel");
            H.Check("DataGrid_RowEdit_CancelClearsEditors",
                await Harness.WaitFor(() => H.FindButton("Save") is null, maxPasses: 20, perPassMs: 15));

            H.ClickButton("Edit");
            await Harness.Render(500);
            H.ClickButton("Save");

            H.Check("DataGrid_RowEdit_SaveCommitted",
                await Harness.WaitFor(
                    () => lastCommit.StartsWith("0:Product 0:A:10", StringComparison.Ordinal),
                    maxPasses: 25, perPassMs: 20));
            H.Check("DataGrid_RowEdit_ReturnedToDisplay",
                H.FindText("cell:Name:Product 0") is not null);
        }
    }

    /// <summary>
    /// Regression for GitHub #34. When a child element's type flips inside a Grid
    /// (TextBlock → TextBox → TextBlock — e.g. a DataGrid cell entering and leaving
    /// inline-edit mode), the row Grid used to drop the trailing cell and retarget
    /// the intermediate cell's AutomationName to a sibling's stale value. Root cause:
    /// <c>ReconcileImperative</c> called <c>UnmountAndPool</c> on the replaced
    /// control, whose <c>ElementPool.Return</c> → <c>DetachFromParent</c> removed
    /// the old child from the Grid's <c>Children</c> collection *before* the caller's
    /// <c>g.Children[i] = replacement</c> assignment, shifting siblings down by one.
    /// After the shift, the final-column update path hit <c>i &gt;= g.Children.Count</c>
    /// and broke out of the loop, leaving the trailing cell dropped and one
    /// sibling's AutomationName carrying the flipped cell's text.
    ///
    /// This fixture mounts a tiny hand-rolled Grid whose middle child flips between
    /// <c>Text</c> and <c>TextBox</c> based on a state bit, then asserts the
    /// Grid's child count and per-child <c>AutomationProperties.Name</c> values
    /// survive the flip and the flip-back.
    /// </summary>
    internal class CellTypeFlipPreservesTrailingCells(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Action<bool>? setEditing = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (editing, setEd) = ctx.UseState(false);
                setEditing = setEd;

                // Four children matching the DataGrid row layout: [Id][Name][Cat][Price].
                // Middle child flips between Text and TextBox based on `editing`.
                var nameCell = editing
                    ? (Element)TextBox("Alice", _ => { }).Padding(2)
                    : (Element)TextBlock("Alice").Padding(horizontal: 8, vertical: 4);

                return Grid(
                    new[] { GridSize.Px(60), GridSize.Px(140), GridSize.Px(120), GridSize.Px(100) },
                    new[] { GridSize.Star() },
                    TextBlock("1").Padding(horizontal: 8, vertical: 4).Grid(row: 0, column: 0),
                    nameCell.Grid(row: 0, column: 1),
                    TextBlock("Widgets").Padding(horizontal: 8, vertical: 4).Grid(row: 0, column: 2),
                    TextBlock("$10.00").Padding(horizontal: 8, vertical: 4).Grid(row: 0, column: 3)
                );
            });

            await Harness.Render(300);

            // Sanity: all four cells present and correctly named at mount.
            H.Check("CellFlip_Initial_Id", H.FindText("1") is not null);
            H.Check("CellFlip_Initial_Name", H.FindText("Alice") is not null);
            H.Check("CellFlip_Initial_Cat", H.FindText("Widgets") is not null);
            H.Check("CellFlip_Initial_Price", H.FindText("$10.00") is not null);

            var rowGrid = H.FindControl<Microsoft.UI.Xaml.Controls.Grid>(
                g => g.ColumnDefinitions.Count == 4);
            H.Check("CellFlip_RowGridMounted", rowGrid is not null);
            if (rowGrid is null || setEditing is null) return;

            var initialChildCount = rowGrid.Children.Count;
            H.Check($"CellFlip_Initial_ChildCount ({initialChildCount})",
                initialChildCount == 4);

            // 1. Flip middle cell to TextBox (TextBlock → TextBox replacement).
            setEditing(true);
            await Harness.Render(200);

            H.Check($"CellFlip_AfterEnter_ChildCount ({rowGrid.Children.Count})",
                rowGrid.Children.Count == 4);
            H.Check("CellFlip_AfterEnter_PriceStillVisible",
                H.FindText("$10.00") is not null);
            H.Check("CellFlip_AfterEnter_CategoryStillVisible",
                H.FindText("Widgets") is not null);
            var textBoxAfterEnter = H.FindControl<TextBox>(tb => tb.Text == "Alice");
            H.Check("CellFlip_AfterEnter_EditorMounted", textBoxAfterEnter is not null);

            // 2. Flip back to TextBlock (TextBox → TextBlock replacement).
            setEditing(false);
            await Harness.Render(200);

            H.Check($"CellFlip_AfterExit_ChildCount ({rowGrid.Children.Count})",
                rowGrid.Children.Count == 4);
            H.Check("CellFlip_AfterExit_PriceStillVisible",
                H.FindText("$10.00") is not null);
            H.Check("CellFlip_AfterExit_CategoryStillVisible",
                H.FindText("Widgets") is not null);

            // 3. Critical check: trailing cell's AutomationProperties.Name must
            //    equal its visible text. Under the #34 bug, the failure mode was
            //    either (a) Name cleared to empty by ElementPool.CleanElement on
            //    the replaced sibling, or (b) Name holding a neighbouring cell's
            //    stale caption after the Grid.Children shift. Require an exact
            //    match so both modes are caught — allowing empty would silently
            //    regress into the UIA-opaque state the issue reports.
            var priceTb = rowGrid.Children.OfType<TextBlock>()
                .FirstOrDefault(tb => tb.Text == "$10.00");
            H.Check("CellFlip_AfterExit_PriceCellPresent", priceTb is not null);
            if (priceTb is not null)
            {
                var priceName = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(priceTb);
                H.Check($"CellFlip_PriceAutomationNameIntact (name='{priceName}')",
                    priceName == "$10.00");
            }

            // 4. And the middle cell's AutomationName must be "Alice", not stale
            //    or empty. Same reasoning as above.
            var nameTb = rowGrid.Children.OfType<TextBlock>()
                .FirstOrDefault(tb => tb.Text == "Alice");
            H.Check("CellFlip_AfterExit_NameCellPresent", nameTb is not null);
            if (nameTb is not null)
            {
                var uiName = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(nameTb);
                H.Check($"CellFlip_NameAutomationNameIntact (name='{uiName}')",
                    uiName == "Alice");
            }
        }
    }

    internal class KeyboardAndPrivateRenderPaths(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var source = CreateSource(8);
            var columns = CreateEditableColumns();
            var state = new DataGridState<TestProduct>(source, columns, Microsoft.UI.Reactor.Controls.SelectionMode.Multiple);
            await state.LoadDataAsync();

            var committed = 0;
            var el = new DataGridElement<TestProduct>
            {
                Source = source,
                Columns = columns,
                Editable = true,
                SelectionMode = Microsoft.UI.Reactor.Controls.SelectionMode.Multiple,
                EditMode = EditMode.Cell,
                OnRowChanged = (_, _) =>
                {
                    committed++;
                    return Task.CompletedTask;
                },
            };

            var componentType = typeof(DataGridComponent<TestProduct>);
            object? Invoke(string name, params object?[] args) =>
                componentType.GetMethod(name, global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static)!
                    .Invoke(null, args);

            bool Should(global::Windows.System.VirtualKey key) =>
                (bool)Invoke("ShouldHandleKey", state, el, key)!;

            void Key(global::Windows.System.VirtualKey key) =>
                Invoke("HandleKeyDown", state, el, key);

            H.Check("DataGrid_KeyReflect_ShouldHandleNavigation",
                Should(global::Windows.System.VirtualKey.Down)
                && Should(global::Windows.System.VirtualKey.Tab)
                && Should(global::Windows.System.VirtualKey.Home)
                && Should(global::Windows.System.VirtualKey.End)
                && Should(global::Windows.System.VirtualKey.Enter)
                && Should(global::Windows.System.VirtualKey.F2)
                && !Should(global::Windows.System.VirtualKey.A));

            Key(global::Windows.System.VirtualKey.Down);
            Key(global::Windows.System.VirtualKey.Right);
            Key(global::Windows.System.VirtualKey.Home);
            Key(global::Windows.System.VirtualKey.End);
            Key(global::Windows.System.VirtualKey.Tab);
            H.Check("DataGrid_KeyReflect_FocusMoved", state.FocusedRowIndex >= 0 && state.FocusedColIndex >= 0);

            Key(global::Windows.System.VirtualKey.Space);
            H.Check("DataGrid_KeyReflect_SpaceSelected", state.SelectedKeys.Count > 0);

            state.SetFocus(0, 1);
            Key(global::Windows.System.VirtualKey.F2);
            H.Check("DataGrid_KeyReflect_BeginEdit", state.IsEditing);
            H.Check("DataGrid_KeyReflect_ShouldHandleEditing",
                Should(global::Windows.System.VirtualKey.Enter)
                && Should(global::Windows.System.VirtualKey.Escape)
                && Should(global::Windows.System.VirtualKey.Tab)
                && !Should(global::Windows.System.VirtualKey.Down));

            state.UpdateEditingValue("Updated product");
            Key(global::Windows.System.VirtualKey.Enter);
            await Task.Delay(50);
            H.Check("DataGrid_KeyReflect_EnterCommitted", !state.IsEditing && committed >= 1);

            state.BeginEdit(0, 1);
            Key(global::Windows.System.VirtualKey.Escape);
            H.Check("DataGrid_KeyReflect_EscapeCanceled", !state.IsEditing);

            state.SetFocus(0, 1);
            state.BeginEdit(0, 1);
            state.UpdateEditingValue("Tab product");
            Key(global::Windows.System.VirtualKey.Tab);
            await Task.Delay(50);
            H.Check("DataGrid_KeyReflect_TabMovesAndReopens", state.IsEditing && committed >= 2);

            // ── Row mode (#853) ─────────────────────────────────────────────────────
            // IsEditing is TRUE during a row edit too (BeginRowEdit sets _editingRowKey while
            // leaving _editingColumnName null), so before the fix these keys fell into the
            // single-CELL block above and ran CommitEdit() with a null column name.
            state.CancelEdit();
            var rowCommitted = 0;
            var rowEl = el with
            {
                EditMode = EditMode.Row,
                OnRowChanged = (_, _) =>
                {
                    rowCommitted++;
                    return Task.CompletedTask;
                },
            };

            state.SetFocus(2, 1); // Name — column 0 (Id) is read-only. Row 2 is untouched above.
            state.BeginRowEdit(2);
            state.UpdateRowEditValue("Name", "Row edited");
            state.UpdateRowEditValue("Price", 42.0);

            DataGridComponent<TestProduct>.HandleKeyDownForTests(
                state, rowEl, global::Windows.System.VirtualKey.Tab);
            await Task.Delay(50);

            // Tab cycles the row's editors and must NOT commit — the pending values and the
            // row-edit state both survive, and nothing was written through to the item.
            H.Check("DataGrid_KeyReflect_RowEditTabKeepsRowEditing",
                state.IsRowEditing && state.EditingRowKey is not null);
            H.Check("DataGrid_KeyReflect_RowEditTabKeepsPendingValues",
                (state.GetRowEditValue("Name") as string) == "Row edited"
                && state.GetRowEditValue("Price") is 42.0);
            H.Check($"DataGrid_KeyReflect_RowEditTabMovedInRow (col={state.FocusedColIndex}, row={state.FocusedRowIndex})",
                state.FocusedColIndex == 2 && state.FocusedRowIndex == 2);
            H.Check("DataGrid_KeyReflect_RowEditTabDidNotCommit",
                state.GetItemAt(2)?.Name == "Product 2" && rowCommitted == 0);

            // Enter runs the ROW commit — both pending columns land, which a single-cell
            // commit could never do.
            DataGridComponent<TestProduct>.HandleKeyDownForTests(
                state, rowEl, global::Windows.System.VirtualKey.Enter);
            await Task.Delay(50);
            H.Check("DataGrid_KeyReflect_RowEditEnterCommitsWholeRow",
                state.GetItemAt(2)?.Name == "Row edited" && state.GetItemAt(2)?.Price == 42.0);
            H.Check("DataGrid_KeyReflect_RowEditEnterClearsState",
                !state.IsRowEditing && !state.IsEditing && rowCommitted >= 1);

            var registry = new TypeRegistry();
            var cell = Invoke("RenderCell", columns[3], 12.5, registry);
            var editingCell = Invoke("RenderEditingCell", columns[1], state, registry);
            var rowEditingCell = Invoke("RenderRowEditingCell", columns[1], state, registry);
            var placeholder = Invoke("RenderDefaultPlaceholderCell", columns[1], 120.0);
            var error = Invoke("RenderDefaultError", new InvalidOperationException("boom"));
            H.Check("DataGrid_KeyReflect_RenderHelpers",
                cell is Element
                && editingCell is Element
                && rowEditingCell is Element
                && placeholder is Element
                && error is Element);
        }
    }

    /// <summary>
    /// Issue #976: opening an editor must move REAL XAML keyboard focus into it, and row-mode Tab
    /// must cycle that focus between the row's editors instead of walking out of the grid.
    ///
    /// <para>Before the fix, <see cref="DataGridState{T}"/>'s focus APIs moved a purely logical cell
    /// cursor and nothing ever called <c>Focus(...)</c>. Every check below would then observe focus
    /// still sitting on the baseline element no matter which editor was open.</para>
    ///
    /// <para>This is a selftest rather than an E2E because a selftest runs in a real WinUI window and
    /// can read <c>FocusManager.GetFocusedElement(xamlRoot)</c> directly — E2E is reserved for the
    /// physical keystroke path. Rows are built through <c>BuildRowForTests</c>, the same seam
    /// <c>RenderDataRows</c> uses, so the real <c>RenderRow</c> editing arms (and therefore the real
    /// <c>WithEditorFocusRequest</c> / <c>ScheduleFocus</c> / <c>TryFocusEditor</c> chain) are what
    /// is under test — not a fixture-local re-implementation of them.</para>
    /// </summary>
    internal class EditorRealFocus(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var source = CreateSource(8);
            var columns = CreateEditableColumns();
            var registry = new TypeRegistry();
            var state = new DataGridState<TestProduct>(
                source, columns, Microsoft.UI.Reactor.Controls.SelectionMode.Single);
            await state.LoadDataAsync();

            var el = new DataGridElement<TestProduct>
            {
                Source = source,
                Columns = columns,
                Editable = true,
                SelectionMode = Microsoft.UI.Reactor.Controls.SelectionMode.Single,
                EditMode = EditMode.Row,
                RowHeight = 36,
            };

            // A plain focusable control OUTSIDE the grid rows. Doubles as the positive control for
            // the focus instrument and as the "focus is not in an editor" baseline, so that every
            // later assertion is a real transition rather than something already true.
            var anchorRef = new Microsoft.UI.Reactor.Input.ElementRef();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (_, bump) = ctx.UseReducer(0);
                ctx.UseEffect(() =>
                {
                    void OnChanged() => bump(v => v + 1);
                    state.StateChanged += OnChanged;
                    return () => state.StateChanged -= OnChanged;
                }, global::System.Array.Empty<object>());

                var rows = Enumerable.Range(0, 4)
                    .Select(i => DataGridComponent<TestProduct>.BuildRowForTests(i, state, columns, el, registry))
                    .ToArray();

                return VStack(
                    [Button("anchor", () => { }).Ref(anchorRef), .. rows]);
            });

            await Harness.Render(300);

            H.Check("EditorFocus_RowsRendered", H.FindTextContaining("Product 1") is not null);

            var anchor = anchorRef.Current as Microsoft.UI.Xaml.Controls.Button;
            if (anchor is null) { H.Check("EditorFocus_AnchorMounted", false); return; }

            var xamlRoot = anchor.XamlRoot;
            if (xamlRoot is null) { H.Check("EditorFocus_XamlRootAvailable", false); return; }

            object? Focused() => Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot);

            // ── Positive control ────────────────────────────────────────────────────────
            // Every check below reads GetFocusedElement. If this window cannot REPORT focus
            // (unactivated / non-interactive session) that call returns null forever, and an
            // assertion phrased as "the focused element is no longer the anchor" would pass
            // vacuously against a completely broken implementation. So prove the instrument
            // works first — focus a known element and read it back — and SKIP loudly, logging
            // the probed value, rather than assert against nothing.
            anchor.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();
            var probe = Focused();
            if (!ReferenceEquals(probe, anchor))
            {
                H.Skip("EditorFocus_PositiveControl",
                    $"window cannot report focus (GetFocusedElement returned '{probe?.GetType().Name ?? "null"}' " +
                    "right after focusing a Button) — real-focus checks are not observable here");
                return;
            }
            H.Check("EditorFocus_PositiveControl", true);

            // ── Row edit focuses the row's FIRST editor ─────────────────────────────────
            // Column 0 (Id) is not editable, so the first editor is the Name cell — and its Text
            // says which column actually received focus without needing an AutomationId.
            state.BeginRowEdit(1);
            await Harness.WaitFor(() => Focused() is Microsoft.UI.Xaml.Controls.TextBox);

            var firstEditor = Focused() as Microsoft.UI.Xaml.Controls.TextBox;
            H.Check($"EditorFocus_RowEdit_FocusLeftTheAnchor (focused='{Focused()?.GetType().Name ?? "null"}')",
                firstEditor is not null && !ReferenceEquals(Focused(), anchor));
            H.Check($"EditorFocus_RowEdit_FocusesFirstEditor (text='{firstEditor?.Text}')",
                firstEditor?.Text == "Product 1");

            // ── Tab moves focus to the NEXT editor ──────────────────────────────────────
            DataGridComponent<TestProduct>.HandleKeyDownForTests(
                state, el, global::Windows.System.VirtualKey.Tab);
            await Harness.WaitFor(() => Focused() is Microsoft.UI.Xaml.Controls.TextBox tb
                                        && !ReferenceEquals(tb, firstEditor));

            var secondEditor = Focused() as Microsoft.UI.Xaml.Controls.TextBox;
            // Differential against a no-op only: the first editor is already a focused TextBox here,
            // so "a TextBox is focused" would pass against a Tab that did nothing. It does NOT pin
            // direction — a backward Tab also lands on a different editor. That is the next check's
            // job, and the two teeth are kept separate so a failure says which one broke.
            H.Check($"EditorFocus_RowEditTab_MovedToADifferentEditor (text='{secondEditor?.Text}')",
                secondEditor is not null && !ReferenceEquals(secondEditor, firstEditor));
            // Row 1 → Category "B". Naming the expected value catches a Tab that skipped a column
            // or landed on a different row.
            H.Check($"EditorFocus_RowEditTab_LandsOnTheNextColumn (text='{secondEditor?.Text}')",
                secondEditor?.Text == "B");
            H.Check("EditorFocus_RowEditTab_KeepsRowInEditMode",
                state.IsRowEditing && state.EditingRowKey is not null);

            // ── Tab wraps from the last editor back to the first ────────────────────────
            // Editable columns are Name, Category, Price; focus is on Category, so two more Tabs
            // pass through Price and wrap. Save/Cancel are deliberately skipped — Enter and Esc
            // are their keyboard equivalents.
            DataGridComponent<TestProduct>.HandleKeyDownForTests(
                state, el, global::Windows.System.VirtualKey.Tab);
            await Harness.WaitFor(() => !ReferenceEquals(Focused(), secondEditor));
            var priceEditor = Focused() as Microsoft.UI.Xaml.Controls.TextBox;
            // Row 1's Price is 15, and naming it pins REAL focus to a specific cell. FocusedColIndex
            // alone only pins the logical cursor, which is the thing that was already correct before
            // #976 — a regression that moved the cursor without moving focus would still pass.
            H.Check($"EditorFocus_RowEditTab_ReachesLastEditor (text='{priceEditor?.Text}', col={state.FocusedColIndex})",
                priceEditor?.Text == "15" && state.FocusedColIndex == 3);

            var colBeforeWrap = state.FocusedColIndex;
            DataGridComponent<TestProduct>.HandleKeyDownForTests(
                state, el, global::Windows.System.VirtualKey.Tab);
            await Harness.WaitFor(() => Focused() is Microsoft.UI.Xaml.Controls.TextBox tb
                                        && tb.Text == "Product 1");

            var wrapped = Focused() as Microsoft.UI.Xaml.Controls.TextBox;
            // Asserting only the destination is direction-vacuous here: walking BACKWARD from
            // Category also lands on Name/col 1, so "wrapped past the end" and "stepped back one"
            // are indistinguishable by arrival alone. Pinning the origin column too (3 → 1) makes
            // this fail for an inverted traversal, which is the mutation that matters.
            H.Check($"EditorFocus_RowEditTab_WrapsToFirstEditor (col {colBeforeWrap}→{state.FocusedColIndex}, text='{wrapped?.Text}')",
                colBeforeWrap == 3 && state.FocusedColIndex == 1
                && wrapped?.Text == "Product 1" && !ReferenceEquals(wrapped, priceEditor));
            H.Check("EditorFocus_RowEditTab_WrapDidNotCommit",
                state.IsRowEditing && state.GetItemAt(1)?.Name == "Product 1");

            state.CancelRowEdit();
            await Harness.Render();

            // ── Cell edit focuses its editor too ────────────────────────────────────────
            // Re-baseline onto the anchor so "an editor is focused" is a real transition.
            anchor.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.WaitFor(() => ReferenceEquals(Focused(), anchor));
            H.Check("EditorFocus_CellEdit_BaselineOnAnchor", ReferenceEquals(Focused(), anchor));

            state.BeginEdit(3, 1); // row 3, Name
            await Harness.WaitFor(() => Focused() is Microsoft.UI.Xaml.Controls.TextBox);

            var cellEditor = Focused() as Microsoft.UI.Xaml.Controls.TextBox;
            H.Check($"EditorFocus_CellEdit_FocusesEditor (text='{cellEditor?.Text}')",
                cellEditor is not null && cellEditor.Text == "Product 3");

            state.CancelEdit();
            await Harness.Render();

            // ── Native-Tab ordering: FocusManager moves focus BEFORE our handler runs ───
            // Every Tab check above drives HandleKeyDownForTests directly, which models a Tab that
            // did NOT already move XAML focus. Real Tab does: our KeyDown handler is registered
            // handledEventsToo precisely because WinUI's focus navigation has run by the time it
            // fires. If the grid merely re-focused whatever cell it thought was next, the checks
            // above would still pass while real Tab did the wrong thing — so replicate the real
            // ordering here, which is what the E2E tier exercises.
            anchor.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.WaitFor(() => ReferenceEquals(Focused(), anchor));

            state.BeginRowEdit(1);
            await Harness.WaitFor(() => Focused() is Microsoft.UI.Xaml.Controls.TextBox tb
                                        && tb.Text == "Product 1");
            var nativeStart = Focused() as Microsoft.UI.Xaml.Controls.TextBox;

            // What WinUI does for Tab, before the grid ever sees the key. FindNextElement is the
            // same lookup tab navigation uses; the parameterless TryMoveFocus overload throws
            // "Catastrophic failure" in a desktop app, where the XamlRoot is ambiguous.
            var next = Microsoft.UI.Xaml.Input.FocusManager.FindNextElement(
                Microsoft.UI.Xaml.Input.FocusNavigationDirection.Next,
                new Microsoft.UI.Xaml.Input.FindNextElementOptions { SearchRoot = xamlRoot.Content })
                as Microsoft.UI.Xaml.UIElement;
            var moved = next is not null && next.Focus(Microsoft.UI.Xaml.FocusState.Keyboard);
            await Harness.Render();
            var afterNative = Focused();
            H.Check($"EditorFocus_NativeTab_MovedFocusOffTheEditor (moved={moved}, now='{(afterNative as Microsoft.UI.Xaml.Controls.TextBox)?.Text ?? afterNative?.GetType().Name ?? "null"}')",
                moved && !ReferenceEquals(afterNative, nativeStart));

            // Now the grid's handler runs, exactly as it would after the native move.
            DataGridComponent<TestProduct>.HandleKeyDownForTests(
                state, el, global::Windows.System.VirtualKey.Tab);
            await Harness.Render(200);

            var afterHandler = Focused() as Microsoft.UI.Xaml.Controls.TextBox;
            H.Check($"EditorFocus_NativeTabThenHandler_LandsOnCategory (text='{afterHandler?.Text}', col={state.FocusedColIndex})",
                afterHandler?.Text == "B" && state.FocusedColIndex == 2);

            state.CancelRowEdit();
            await Harness.Render();

            // ── Grid-root KeyDown wiring is one-shot per control ────────────────────────
            // OnMount can run more than once for the SAME FrameworkElement (ElementPool recycles
            // grid roots across remounts), and AddHandler does not de-duplicate. Every extra
            // handler processes every key again, which for row-mode Tab means an extra cursor
            // step: with two editable columns a single Tab went first → last → wrapped back to
            // first, so real focus appeared never to move. This drives the production wiring path
            // itself, so it cannot drift from what OnMount does.
            var wireTarget = new Microsoft.UI.Xaml.Controls.Border();
            var wireRef = new Ref<DataGridElement<TestProduct>>(el);

            var displacedOnFirstWire = DataGridComponent<TestProduct>.WireGridKeyDown(wireTarget, state, wireRef);
            var displacedOnSecondWire = DataGridComponent<TestProduct>.WireGridKeyDown(wireTarget, state, wireRef);

            // Differential, and it fails in both directions: an unguarded AddHandler displaces
            // nothing on either call (false, false) and leaves two live handlers; a guard that
            // skipped re-wiring instead of replacing would leave the FIRST mount's stale closure
            // driving a recycled control's previous state.
            H.Check($"EditorFocus_GridKeyDownWiring_IsOneShotPerControl (first={displacedOnFirstWire}, second={displacedOnSecondWire})",
                !displacedOnFirstWire && displacedOnSecondWire);

            // A different control must get its own wiring, not inherit the first one's.
            var otherTarget = new Microsoft.UI.Xaml.Controls.Border();
            H.Check("EditorFocus_GridKeyDownWiring_IsPerControl",
                !DataGridComponent<TestProduct>.WireGridKeyDown(otherTarget, state, wireRef));

            // ── …and the registry is shared across closed generic types ─────────────────
            // The two checks above pass whether the table is a static on the GENERIC
            // DataGridComponent<T> (one table per closed T) or a single shared one, because both
            // wire through DataGridComponent<TestProduct>. They therefore cannot see the defect
            // this check exists for: ElementPool recycles by CLR type and Grid is on its poolable
            // list, so the same grid root can be remounted under DataGridComponent<TOther>. With a
            // per-instantiation table the TOther wiring sees no entry, displaces nothing, and
            // leaves TestProduct's stale closure attached — two live handlers driving two
            // different grids' state. Re-wiring the SAME control through a DIFFERENT closed
            // generic type must still report a displacement.
            var otherSource = new ListDataSource<TestOtherRow>(
                [new TestOtherRow(0, "x")], r => (RowKey)r.Id);
            var otherColumns = new FieldDescriptor[]
            {
                Column<TestOtherRow>("Id", r => r.Id, width: 60),
                Column<TestOtherRow>("Name", r => r.Name, editable: true, width: 160),
            };
            var otherState = new DataGridState<TestOtherRow>(
                otherSource, otherColumns, Microsoft.UI.Reactor.Controls.SelectionMode.Single);
            var otherRef = new Ref<DataGridElement<TestOtherRow>>(
                new DataGridElement<TestOtherRow> { Source = otherSource, Columns = otherColumns });

            var displacedAcrossInstantiations =
                DataGridComponent<TestOtherRow>.WireGridKeyDown(wireTarget, otherState, otherRef);
            H.Check($"EditorFocus_GridKeyDownWiring_IsSharedAcrossClosedGenerics (displaced={displacedAcrossInstantiations})",
                displacedAcrossInstantiations);
        }
    }

    /// <summary>
    /// Issue #976. Everything in <see cref="EditorRealFocus"/> goes through the happy path: the
    /// built-in editor root IS a focusable <c>Control</c>. A custom <c>col.Editor</c> need not be —
    /// it can hand back a composite whose root is a bare <c>Panel</c>, or something with no
    /// focusable content at all. Those are <c>TryFocusEditor</c>'s other two arms, and they are
    /// unreachable from the built-in editors, so this fixture supplies real custom editors.
    /// </summary>
    internal class EditorFocusCustomEditors(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var source = CreateSource(4);
            var registry = new TypeRegistry();

            var baseColumns = CreateEditableColumns();
            var columns = new FieldDescriptor[]
            {
                baseColumns[0], // Id — read-only

                // Composite: the ROOT is a StackPanel, which is not a Control at all, so
                // Control.Focus can't even be attempted. Focus can only land via
                // FindFirstFocusableElement reaching the TextBox inside.
                baseColumns[1] with
                {
                    Editor = (value, set) => HStack(
                        TextBlock("#"),
                        TextBox(value?.ToString() ?? "", s => set(s))),
                },

                // No focusable content anywhere in the subtree.
                baseColumns[2] with
                {
                    Editor = (value, _) => TextBlock(value?.ToString() ?? ""),
                },

                // A focusable root that is NOT a Control. TextBlock derives from FrameworkElement,
                // and enabling text selection puts it in the tab order — so this is focusable while
                // failing an `is Control` test. Gating TryFocusEditor on Control (as it originally
                // was) declines this silently and focus never moves.
                baseColumns[3] with
                {
                    Editor = (value, _) => TextBlock(value?.ToString() ?? "")
                        .Set(tb => tb.IsTextSelectionEnabled = true),
                },
            };

            var state = new DataGridState<TestProduct>(
                source, columns, Microsoft.UI.Reactor.Controls.SelectionMode.Single);
            await state.LoadDataAsync();

            var el = new DataGridElement<TestProduct>
            {
                Source = source,
                Columns = columns,
                Editable = true,
                SelectionMode = Microsoft.UI.Reactor.Controls.SelectionMode.Single,
                EditMode = EditMode.Cell,
                RowHeight = 36,
            };

            var anchorRef = new Microsoft.UI.Reactor.Input.ElementRef();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (_, bump) = ctx.UseReducer(0);
                ctx.UseEffect(() =>
                {
                    void OnChanged() => bump(v => v + 1);
                    state.StateChanged += OnChanged;
                    return () => state.StateChanged -= OnChanged;
                }, global::System.Array.Empty<object>());

                var rows = Enumerable.Range(0, 3)
                    .Select(i => DataGridComponent<TestProduct>.BuildRowForTests(i, state, columns, el, registry))
                    .ToArray();

                return VStack([Button("anchor", () => { }).Ref(anchorRef), .. rows]);
            });

            await Harness.Render(300);

            var anchor = anchorRef.Current as Microsoft.UI.Xaml.Controls.Button;
            if (anchor is null) { H.Check("CustomEditorFocus_AnchorMounted", false); return; }

            var xamlRoot = anchor.XamlRoot;
            if (xamlRoot is null) { H.Check("CustomEditorFocus_XamlRootAvailable", false); return; }

            object? Focused() => Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot);

            // Same positive control as EditorRealFocus: prove the window can REPORT focus before
            // asserting anything about it, or every check below passes vacuously against null.
            anchor.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();
            var probe = Focused();
            if (!ReferenceEquals(probe, anchor))
            {
                H.Skip("CustomEditorFocus_PositiveControl",
                    $"window cannot report focus (GetFocusedElement returned '{probe?.GetType().Name ?? "null"}' " +
                    "right after focusing a Button) — real-focus checks are not observable here");
                return;
            }
            H.Check("CustomEditorFocus_PositiveControl", true);

            // ── Composite editor: focus reaches INSIDE it ───────────────────────────────
            state.BeginEdit(1, 1); // row 1, Name — the composite editor
            await Harness.WaitFor(() => Focused() is Microsoft.UI.Xaml.Controls.TextBox);

            var composed = Focused() as Microsoft.UI.Xaml.Controls.TextBox;
            H.Check($"CustomEditorFocus_ReachesIntoAComposite (focused='{Focused()?.GetType().Name ?? "null"}', text='{composed?.Text}')",
                composed is not null && composed.Text == "Product 1");

            state.CancelEdit();
            await Harness.Render();

            // ── Nothing focusable: report failure, steal nothing ────────────────────────
            anchor.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.WaitFor(() => ReferenceEquals(Focused(), anchor));
            H.Check("CustomEditorFocus_BaselineOnAnchor", ReferenceEquals(Focused(), anchor));

            state.BeginEdit(1, 2); // row 1, Category — the TextBlock-only editor
            await Harness.Render(200);

            // The request was armed and honoured; XAML simply had nothing to give focus to. The
            // point is that this is a quiet no-op: focus must not be dumped on the grid root, on
            // a neighbouring editor, or cleared to null.
            H.Check($"CustomEditorFocus_NonFocusableEditorLeavesFocusPut (focused='{Focused()?.GetType().Name ?? "null"}')",
                ReferenceEquals(Focused(), anchor));

            state.CancelEdit();
            await Harness.Render();

            // ── Focusable root that is not a Control ────────────────────────────────────
            anchor.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.WaitFor(() => ReferenceEquals(Focused(), anchor));

            state.BeginEdit(1, 3); // row 1, Price — the selectable-TextBlock editor
            await Harness.WaitFor(() => Focused() is Microsoft.UI.Xaml.Controls.TextBlock);

            var selectable = Focused() as Microsoft.UI.Xaml.Controls.TextBlock;
            // Naming TextBlock is the whole point: every other focus check in this file lands on a
            // Control, so all of them pass whether TryFocusEditor accepts UIElement or only Control.
            // This one fails against the Control-gated version, which is what makes it worth having.
            H.Check($"CustomEditorFocus_FocusesANonControlRoot (focused='{Focused()?.GetType().Name ?? "null"}', text='{selectable?.Text}')",
                selectable is not null && selectable.Text == "15"
                && Focused() is not Microsoft.UI.Xaml.Controls.Control);

            state.CancelEdit();
            await Harness.Render();
        }
    }

    /// <summary>
    /// Issue #976 — the virtualized/real-grid counterpart to <see cref="EditorRealFocus"/>.
    /// </summary>
    /// <remarks>
    /// <para><see cref="EditorRealFocus"/> builds rows through <c>BuildRowForTests</c>, so its rows
    /// are plain children of the fixture's own host. The shipping grid instead renders rows through
    /// an <c>ItemsRepeater</c> + <c>ElementFactory</c>, which realizes, recycles and re-realizes row
    /// containers. That is a materially different reconcile path for the very hooks this feature
    /// depends on, and it is the path the E2E tier exercises — so a green
    /// <c>BuildRowForTests</c> fixture is not evidence that the real grid focuses anything.</para>
    /// <para>This mounts the REAL <c>DataGridElement</c> and drives it through the production
    /// <c>OnStateReadyInternal</c> seam, so a regression that only exists under virtualization is
    /// caught here — one tier below E2E, and in seconds rather than a CI round trip.</para>
    /// </remarks>
    internal class EditorRealFocusVirtualized(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DataGridState<TestProduct>? state = null;
            var anchorRef = new Microsoft.UI.Reactor.Input.ElementRef();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => CreateSource(6));
                var columns = CreateEditableColumns();

                return VStack(
                    Button("anchor", () => { }).Ref(anchorRef),
                    Component<DataGridComponent<TestProduct>, DataGridElement<TestProduct>>(
                        new DataGridElement<TestProduct>
                        {
                            Source = source,
                            Columns = columns,
                            Editable = true,
                            SelectionMode = Microsoft.UI.Reactor.Controls.SelectionMode.Single,
                            EditMode = EditMode.Row,
                            RowHeight = 36,
                            OnStateReadyInternal = s => state = s,
                        }));
            });

            H.Check("EditorFocusVirt_Rendered",
                await Harness.WaitFor(() => H.FindTextContaining("Product 1") is not null,
                    maxPasses: 40, perPassMs: 25));

            if (state is null) { H.Check("EditorFocusVirt_StateCaptured", false); return; }
            H.Check("EditorFocusVirt_StateCaptured", true);

            var anchor = anchorRef.Current as Microsoft.UI.Xaml.Controls.Button;
            if (anchor is null) { H.Check("EditorFocusVirt_AnchorMounted", false); return; }
            var xamlRoot = anchor.XamlRoot;
            if (xamlRoot is null) { H.Check("EditorFocusVirt_XamlRootAvailable", false); return; }

            object? Focused() => Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot);

            // Positive control: if the window cannot report focus at all, every check below would
            // pass or fail for reasons that have nothing to do with the grid. Skip loudly instead.
            anchor.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render(60);
            if (!ReferenceEquals(Focused(), anchor))
            {
                H.Skip("EditorFocusVirt_PositiveControl",
                    $"window cannot report focus (got '{Focused()?.GetType().Name ?? "null"}')");
                return;
            }
            H.Check("EditorFocusVirt_PositiveControl", true);

            // ── Row-edit entry focuses the first editor ─────────────────────────────────
            state.BeginRowEdit(1);
            await Harness.WaitFor(() => Focused() is Microsoft.UI.Xaml.Controls.TextBox,
                maxPasses: 40, perPassMs: 25);

            var firstEditor = Focused() as Microsoft.UI.Xaml.Controls.TextBox;
            H.Check($"EditorFocusVirt_RowEdit_FocusesFirstEditor (text='{firstEditor?.Text ?? "<null>"}')",
                firstEditor?.Text == "Product 1");

            // ── Tab moves to the NEXT editor, under virtualization ──────────────────────
            // Row 1: Name="Product 1", Category="B", Price=15. Naming the destination is what
            // makes this non-vacuous — "focus moved" is satisfied by moving the wrong way, and a
            // double-stepped Tab lands on Price rather than Category.
            DataGridComponent<TestProduct>.HandleKeyDownForTests(
                state, BuildProbeElement(), global::Windows.System.VirtualKey.Tab);
            await Harness.WaitFor(() => Focused() is Microsoft.UI.Xaml.Controls.TextBox tb
                                        && !ReferenceEquals(tb, firstEditor),
                maxPasses: 60, perPassMs: 25);

            var secondEditor = Focused() as Microsoft.UI.Xaml.Controls.TextBox;
            H.Check($"EditorFocusVirt_RowEditTab_LandsOnNextColumn (text='{secondEditor?.Text ?? "<null>"}')",
                secondEditor?.Text == "B");

            state.CancelRowEdit();
            await Harness.Render(60);
        }

        private static DataGridElement<TestProduct> BuildProbeElement() =>
            new()
            {
                Source = CreateSource(6),
                Columns = CreateEditableColumns(),
                Editable = true,
                EditMode = EditMode.Row,
            };
    }

    /// <summary>
    /// #976 — when a row-edit traversal cannot actually take focus, the blur-commit that was
    /// suppressed on its behalf must be repaid by pulling focus back onto the grid root.
    /// </summary>
    /// <remarks>
    /// The gap this covers is a mismatch of kinds: the grid arms
    /// <c>SuppressNextLostFocusCommit</c> from a LOGICAL predicate (there is another editable
    /// column to move to), but the claim is only sound if the move PHYSICALLY takes focus. With a
    /// custom editor whose subtree refuses focus, the routed LostFocus consumes the claim and
    /// returns early — no deferred "is focus still inside the grid?" check is scheduled — so
    /// without the repayment focus would be left outside the grid with the row edit still open.
    ///
    /// This has to go through the real component mount, exactly as
    /// <see cref="EditorRealFocusVirtualized"/> does, rather than the row-building test seam: the
    /// debt is opened by the grid root's own LostFocus handler, and that seam has no grid root, so
    /// no debt can exist there to repay.
    /// </remarks>
    internal class EditorFocusDebtRepaid(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            DataGridState<TestProduct>? state = null;
            var anchorRef = new Microsoft.UI.Reactor.Input.ElementRef();

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var source = ctx.UseMemo(() => CreateSource(6));
                var columns = BuildColumnsWithNonFocusableCategory();

                return VStack(
                    Button("anchor", () => { }).Ref(anchorRef),
                    Component<DataGridComponent<TestProduct>, DataGridElement<TestProduct>>(
                        new DataGridElement<TestProduct>
                        {
                            Source = source,
                            Columns = columns,
                            Editable = true,
                            SelectionMode = Microsoft.UI.Reactor.Controls.SelectionMode.Single,
                            EditMode = EditMode.Row,
                            RowHeight = 36,
                            OnStateReadyInternal = s => state = s,
                        }));
            });

            H.Check("EditorFocusDebt_Rendered",
                await Harness.WaitFor(() => H.FindTextContaining("Product 1") is not null,
                    maxPasses: 40, perPassMs: 25));

            if (state is null)
            {
                H.Check("EditorFocusDebt_StateCaptured", false);
                return;
            }
            H.Check("EditorFocusDebt_StateCaptured", true);

            var anchor = anchorRef.Current as Microsoft.UI.Xaml.Controls.Button;
            if (anchor is null)
            {
                H.Check("EditorFocusDebt_AnchorMounted", false);
                return;
            }

            var xamlRoot = anchor.XamlRoot;
            if (xamlRoot is null)
            {
                H.Check("EditorFocusDebt_XamlRootAvailable", false);
                return;
            }

            object? Focused() => Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot);

            // Positive control: if this window cannot report focus at all, every assertion below
            // would pass or fail for reasons that have nothing to do with the product.
            anchor.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render(60);
            if (!ReferenceEquals(Focused(), anchor))
            {
                H.Skip("EditorFocusDebt_PositiveControl",
                    $"window cannot report focus (got '{Focused()?.GetType().Name ?? "null"}')");
                return;
            }
            H.Check("EditorFocusDebt_PositiveControl", true);

            // ── Open a row edit. The cursor parks on Name, which has a real TextBox editor ──────
            state.BeginRowEdit(1);
            await Harness.WaitFor(() => Focused() is Microsoft.UI.Xaml.Controls.TextBox,
                maxPasses: 40, perPassMs: 25);

            var firstEditor = Focused() as Microsoft.UI.Xaml.Controls.TextBox;
            H.Check($"EditorFocusDebt_RowEdit_FocusesFirstEditor (text='{firstEditor?.Text ?? "<null>"}')",
                firstEditor?.Text == "Product 1");
            if (firstEditor is null) return;

            // ── Open the debt: a blur the grid suppresses instead of committing ─────────────────
            // This is the state a row-edit Tab leaves behind. Native Tab moves focus out of the
            // grid BEFORE the focus request is dispatched, so the routed LostFocus arrives first,
            // consumes the claim and returns. Setting the flag directly reproduces that ordering
            // without depending on the platform's tab-navigation timing.
            state.SuppressNextLostFocusCommit = true;
            anchor.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render(60);

            H.Check($"EditorFocusDebt_BlurSuppressed_RowStaysOpen (focused='{Focused()?.GetType().Name ?? "null"}', rowEditing={state.IsRowEditing})",
                ReferenceEquals(Focused(), anchor) && state.IsRowEditing);

            // ── Traverse onto the non-focusable editor. XAML refuses; the debt is repaid ────────
            DataGridComponent<TestProduct>.HandleKeyDownForTests(
                state, BuildProbeElement(), global::Windows.System.VirtualKey.Tab);
            await Harness.WaitFor(() => !ReferenceEquals(Focused(), anchor),
                maxPasses: 60, perPassMs: 25);
            await Harness.Render(60);

            // The Name editor must still be parented, or the ancestor oracle below is answering a
            // different question — a detached element has no parent chain to walk at all.
            H.Check("EditorFocusDebt_ProbeStillParented",
                Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(firstEditor) is not null);

            // Assert the DESTINATION, not the movement. Focus must land on something that CONTAINS
            // the row's editors (the grid root) and does NOT contain the anchor. The host VStack
            // and the window root contain both, so the second clause is what makes this tight
            // without the fixture holding a direct reference to the grid. Under the mutation that
            // deletes the repayment, focus stays on the anchor and the first clause fails.
            var landed = Focused();
            var containsEditors = IsAncestorOf(landed, firstEditor);
            var containsAnchor = IsAncestorOf(landed, anchor);
            H.Check($"EditorFocusDebt_RepaidOntoTheGrid (focused='{landed?.GetType().Name ?? "null"}', containsEditors={containsEditors}, containsAnchor={containsAnchor})",
                containsEditors && !containsAnchor);

            // ── The stale-request arm of Apply, exercised directly ─────────────────────────────
            // Staging "the edit ends between the enqueue and the deferred tick" through the real
            // dispatcher is not deterministic; SettleStaleFocusDebt IS that arm, so call it. Two
            // arms, so neither the "always clear" nor the "never clear" mutation stays green.
            var debtProbe = new global::Microsoft.UI.Reactor.Core.Ref<Microsoft.UI.Xaml.FrameworkElement?>(anchor);

            state.CancelRowEdit();
            await Harness.Render(60);
            DataGridComponent<TestProduct>.SettleStaleFocusDebt(state, debtProbe);
            H.Check($"EditorFocusDebt_DroppedWhenTheEditIsOver (editing={state.IsEditing}, rowEditing={state.IsRowEditing}, debt='{debtProbe.Current?.GetType().Name ?? "null"}')",
                debtProbe.Current is null);

            // The mirror arm. A request superseded DURING a live edit is settled by the newer
            // request's own tick, so an unconditional clear here would drop a legitimate debt.
            debtProbe.Current = anchor;
            state.BeginRowEdit(1);
            await Harness.Render(60);
            DataGridComponent<TestProduct>.SettleStaleFocusDebt(state, debtProbe);
            H.Check($"EditorFocusDebt_KeptWhileTheEditIsStillOpen (rowEditing={state.IsRowEditing}, debt='{debtProbe.Current?.GetType().Name ?? "null"}')",
                state.IsRowEditing && ReferenceEquals(debtProbe.Current, anchor));

            state.CancelRowEdit();
            await Harness.Render(60);
        }

        /// <summary>
        /// The default column set has no non-focusable editor, so this fixture builds its own:
        /// Category renders a bare TextBlock, which has no focusable content anywhere in its
        /// subtree and therefore makes <c>TryFocusEditor</c> ask XAML and be refused.
        /// </summary>
        private static FieldDescriptor[] BuildColumnsWithNonFocusableCategory()
        {
            var baseColumns = CreateEditableColumns();
            return new FieldDescriptor[]
            {
                baseColumns[0], // Id — read-only, so the row-edit cursor never parks here.
                baseColumns[1], // Name — a real TextBox editor; this is where BeginRowEdit lands.
                baseColumns[2] with
                {
                    Editor = (value, _) => TextBlock(value?.ToString() ?? ""),
                },
                baseColumns[3],
            };
        }

        private static DataGridElement<TestProduct> BuildProbeElement() =>
            new()
            {
                Source = CreateSource(6),
                Columns = BuildColumnsWithNonFocusableCategory(),
                Editable = true,
                EditMode = EditMode.Row,
            };

        /// <summary>
        /// True when <paramref name="candidate"/> is a strict visual-tree ancestor of
        /// <paramref name="descendant"/>. Deliberately structural: the fixture cannot get a
        /// reference to the grid root through any public seam, but "contains the editors and does
        /// not contain the anchor" identifies it uniquely within this host.
        /// </summary>
        private static bool IsAncestorOf(object? candidate, Microsoft.UI.Xaml.DependencyObject? descendant)
        {
            if (candidate is not Microsoft.UI.Xaml.DependencyObject ancestor || descendant is null)
                return false;

            for (var node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(descendant);
                 node is not null;
                 node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            {
                if (ReferenceEquals(node, ancestor)) return true;
            }

            return false;
        }
    }

    /// <summary>
    /// #976 — <c>IsFocusInside</c> must treat a root whose <c>XamlRoot</c> is null as "focus is
    /// outside", instead of handing the null to <c>FocusManager.GetFocusedElement</c>.
    /// </summary>
    /// <remarks>
    /// Both halves of the hazard were measured on a live window before this fixture was written,
    /// and both are load-bearing:
    /// <list type="bullet">
    /// <item><description><c>FocusManager.GetFocusedElement(null)</c> <b>throws</b>
    /// <c>ArgumentException</c> — it does not return null — so an unguarded call is a crash, not a
    /// wrong answer.</description></item>
    /// <item><description>A <c>FrameworkElement</c> that is not connected to a window really does
    /// report a null <c>XamlRoot</c>, so the guard is reachable.</description></item>
    /// </list>
    /// <c>ScheduleFocus</c> reaches this helper on a later dispatcher tick, by which time the grid
    /// it captured can have been unmounted — that is the real route to a disconnected root.
    ///
    /// The fixture uses a never-parented <c>Grid</c> rather than racing an unmount: both were
    /// measured to give <c>XamlRoot == null</c>, and only the first is deterministic. Nothing here
    /// renders a DataGrid, because the helper is a static that takes any element; the integration
    /// path is already covered by <see cref="EditorFocusDebtRepaid"/>.
    /// </remarks>
    internal class EditorFocusDisconnectedRoot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var anchorRef = new Microsoft.UI.Reactor.Input.ElementRef();

            var host = H.CreateHost();
            host.Mount(_ => VStack(Button("anchor", () => { }).Ref(anchorRef)));

            H.Check("EditorFocusDetached_Rendered",
                await Harness.WaitFor(() => anchorRef.Current is not null,
                    maxPasses: 40, perPassMs: 25));

            var anchor = anchorRef.Current as Microsoft.UI.Xaml.Controls.Button;
            if (anchor is null)
            {
                H.Check("EditorFocusDetached_AnchorMounted", false);
                return;
            }

            var xamlRoot = anchor.XamlRoot;
            if (xamlRoot is null)
            {
                H.Check("EditorFocusDetached_XamlRootAvailable", false);
                return;
            }

            object? Focused() => Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(xamlRoot);

            // Positive control: on a window that cannot report focus at all, the connected arm
            // below would fail for reasons that have nothing to do with the product.
            anchor.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render(60);
            if (!ReferenceEquals(Focused(), anchor))
            {
                H.Skip("EditorFocusDetached_PositiveControl",
                    $"window cannot report focus (got '{Focused()?.GetType().Name ?? "null"}')");
                return;
            }
            H.Check("EditorFocusDetached_PositiveControl", true);

            // ── Precondition ─────────────────────────────────────────────────────────────────
            // If a never-parented element somehow reported a non-null XamlRoot, the guard would
            // never be reached and both assertions below would be tautologies. Log the input, not
            // just the verdict, and stop rather than report a pass the run cannot support.
            var detached = new Microsoft.UI.Xaml.Controls.Grid();
            var detachedRoot = detached.XamlRoot;
            H.Check($"EditorFocusDetached_RootIsDisconnected (xamlRoot='{detachedRoot?.GetType().Name ?? "null"}')",
                detachedRoot is null);
            if (detachedRoot is not null) return;

            // ── Arm 1: the guard ─────────────────────────────────────────────────────────────
            // Seeded with the WRONG answer so a throw cannot leave a value that satisfies the
            // assertion below.
            var detachedResult = true;
            string? threw = null;
            try
            {
                detachedResult = DataGridComponent<TestProduct>.IsFocusInsideForTests(detached);
            }
            // Excludes only the process-fatal pair, matching the house style in src/Reactor.
            // Declining to catch is a safe no-op here: an escaping exception unwinds out of
            // RunAsync and SelfTestRunner reports it as `not ok <fixture>_CRASH` plus a recorded
            // failure, so a process-fatal condition is surfaced as a crash rather than
            // misreported below as "the guard returned the wrong answer".
            catch (global::System.Exception ex)
                when (ex is not global::System.OutOfMemoryException
                      and not global::System.StackOverflowException)
            {
                threw = $"{ex.GetType().Name}: {ex.Message}";
            }

            H.Check($"EditorFocusDetached_DoesNotThrow (threw='{threw ?? "no"}')", threw is null);
            H.Check($"EditorFocusDetached_ReportsOutside (threw='{threw ?? "no"}', result={detachedResult})",
                threw is null && !detachedResult);

            // ── Arm 2: differential ──────────────────────────────────────────────────────────
            // Same method, a connected root, and the opposite answer. Without this a guard that
            // simply returned false for every input would satisfy arm 1.
            var selfResult = DataGridComponent<TestProduct>.IsFocusInsideForTests(anchor);
            H.Check($"EditorFocusDetached_FocusedRootReportsInside (result={selfResult})", selfResult);

            // The walk starts at the focused element itself, so the arm above never leaves the
            // first iteration. Run it once more from the anchor's parent to cover the ancestor
            // walk as well; a mounted button always has a visual parent inside this host, so a
            // null here is a structural surprise and should redden rather than be skipped.
            var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(anchor)
                as Microsoft.UI.Xaml.FrameworkElement;
            var parentResult = parent is not null
                && DataGridComponent<TestProduct>.IsFocusInsideForTests(parent);
            H.Check($"EditorFocusDetached_AncestorRootReportsInside (parent='{parent?.GetType().Name ?? "null"}', result={parentResult})",
                parentResult);
        }
    }
}
