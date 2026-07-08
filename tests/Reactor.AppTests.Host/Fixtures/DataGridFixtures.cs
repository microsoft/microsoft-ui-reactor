using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

internal static class DataGridFixtures
{
    // ── Editable DataGrid ────────────────────────────────────────

    class Employee
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public double Salary { get; set; }
    }

    internal class EditableGridComponent : Component
    {
        public override Element Render()
        {
            // Accumulate every onRowChanged into an append-only log instead of overwriting a
            // single "last edit". A cross-row commit-tap can fire a spurious unchanged commit whose
            // async status write races the real one; an append-only log (functional UseReducer
            // update applied in enqueue order on the UI thread) lets a test deterministically assert
            // that a specific edit callback fired, regardless of which async write lands last.
            var (editLog, appendEdit) = UseReducer("");

            // Capture UI-thread dispatcher so onRowChanged (which runs on a threadpool
            // thread via HandleAsyncCommit's Task.Run) can safely update component state.
            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            var source = UseMemo(() => new ListDataSource<Employee>(
                new[]
                {
                    new Employee { Id = 1, FirstName = "Alice", LastName = "Smith", Salary = 75000 },
                    new Employee { Id = 2, FirstName = "Bob", LastName = "Jones", Salary = 82000 },
                    new Employee { Id = 3, FirstName = "Carol", LastName = "Lee", Salary = 91000 },
                },
                e => (RowKey)e.Id));

            var columns = new FieldDescriptor[]
            {
                Column<Employee>("Id", e => e.Id, width: 60),
                Column<Employee>("FirstName", e => e.FirstName, editable: true, displayName: "First Name", width: 140),
                Column<Employee>("LastName", e => e.LastName, editable: true, displayName: "Last Name", width: 140),
                Column<Employee>("Salary", e => e.Salary, format: "C0", width: 100),
            };

            return VStack(8,
                TextBlock($"Edits:{editLog}").AutomationId("EditLog"),
                DataGrid(
                    source: source,
                    columns: columns,
                    editable: true,
                    editMode: EditMode.Cell,
                    onRowChanged: (key, item) =>
                    {
                        // Dispatch to UI thread — this callback runs on a threadpool thread from
                        // HandleAsyncCommit's Task.Run. The functional update composes append-only,
                        // so concurrent commits accumulate instead of clobbering each other.
                        dq?.TryEnqueue(() =>
                        {
                            appendEdit(prev => prev + $"[{key.Value}:{item.FirstName},{item.LastName}]");
                        });
                        return Task.CompletedTask;
                    },
                    rowHeight: 36
                ).AutomationId("EditableGrid")
            );
        }
    }

    internal static Element EditableGrid(RenderContext ctx) =>
        Component<EditableGridComponent>();

    // ── Keyboard-navigation DataGrid (E2E) ───────────────────────────
    // Drives the REAL WinUI KeyDown pipeline (DataGridComponent.OnMount AddHandler ->
    // ShouldHandleKey -> HandleKeyDown -> DataGridState focus/edit methods) cross-process.
    // The in-process selftest (DataGrid_KeyReflect_*) reflectively invokes HandleKeyDown
    // directly, so it never exercises the real AddHandler lambda, and never presses Up/Left
    // or ShouldHandleKey(Space) — those arms are only reachable through injected keys.
    //
    // Design so a green E2E deterministically proves which arm ran:
    //   * All three columns are editable string columns with globally-unique cell values, so
    //     BeginEdit's inline editor value uniquely identifies the (row, col) the focus landed
    //     on — that is how the test asserts arrow / Home / End / Tab navigation actually moved
    //     cell focus through the handler (there is no OnFocusChanged callback to probe).
    //   * SelectionMode.Single + onSelectionChanged updates a status TextBlock, so a Space
    //     press (row select) is observable cross-process (mirrors the chart's OnPointInvoke probe).
    //   * onRowChanged appends to an append-only edit log, so an editing Enter/Tab commit is
    //     observable too.
    // Uses a Component so the setState/reducer updates persist + re-render (the TestHost rebuilds
    // each fixture with a fresh RenderContext per render, so a raw ctx.UseState would not re-render).

    class NavItem
    {
        public int Id { get; set; }
        public string First { get; set; } = "";
        public string Last { get; set; } = "";
        public string City { get; set; } = "";
    }

    internal class KeyboardNavGridComponent : Component
    {
        public override Element Render()
        {
            var (editLog, appendEdit) = UseReducer("");
            var (selStatus, setSelStatus) = UseState("none");

            // Capture the UI-thread dispatcher up front. The DataGrid commits edits through its
            // UseMutation pipeline (see DataGridComponent), whose async mutator awaits onRowChanged
            // with ConfigureAwait(false), so neither the callback nor its continuation is guaranteed
            // to run on the UI thread. Marshal the state updates (appendEdit/setSelStatus) back
            // through this dispatcher so they are always applied on the UI thread.
            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            var source = UseMemo(() => new ListDataSource<NavItem>(
                new[]
                {
                    new NavItem { Id = 1, First = "Alice", Last = "Smith", City = "Reno" },
                    new NavItem { Id = 2, First = "Bob", Last = "Jones", City = "Miami" },
                    new NavItem { Id = 3, First = "Carol", Last = "Lee", City = "Tulsa" },
                },
                e => (RowKey)e.Id));

            // All editable so Home (col 0) and End (last col) both land on editable cells and a
            // BeginEdit editor appears there — the focus probe. Values are unique across the whole
            // grid so an editor's value pins down exactly which cell has focus.
            var columns = new FieldDescriptor[]
            {
                Column<NavItem>("First", e => e.First, editable: true, displayName: "First", width: 120),
                Column<NavItem>("Last", e => e.Last, editable: true, displayName: "Last", width: 120),
                Column<NavItem>("City", e => e.City, editable: true, displayName: "City", width: 120),
            };

            return VStack(8,
                TextBlock($"Sel:{selStatus}").AutomationId("KbdNav_Status"),
                TextBlock($"Edits:{editLog}").AutomationId("KbdNav_EditLog"),
                // Focusable anchor immediately before the grid. The DataGrid's own Grid container is
                // a tab stop but does NOT surface its AutomationId in UIA (so it can't be reached by
                // winapp SetFocus), whereas a Button does. The E2E focuses this anchor via UIA, then
                // presses Tab once to land keyboard focus on the grid (the next tab stop) so injected
                // arrow keys route to its KeyDown handler.
                Button("Focus grid").AutomationId("KbdNav_FocusAnchor"),
                DataGrid(
                    source: source,
                    columns: columns,
                    selectionMode: SelectionMode.Single,
                    editable: true,
                    editMode: EditMode.Cell,
                    onSelectionChanged: keys =>
                    {
                        // HandleRowClick (Space) fires this on the UI thread; a threadpool hop is
                        // harmless from the UI thread and keeps this consistent with onRowChanged.
                        var selected = "none";
                        foreach (var k in keys)
                            selected = k.Value; // SelectionMode.Single -> at most one key
                        dq?.TryEnqueue(() => setSelStatus(selected));
                    },
                    onRowChanged: (key, item) =>
                    {
                        dq?.TryEnqueue(() =>
                        {
                            appendEdit(prev => prev + $"[{key.Value}:{item.First},{item.Last},{item.City}]");
                        });
                        return Task.CompletedTask;
                    },
                    rowHeight: 36
                ).AutomationId("KbdNavGrid")
            );
        }
    }

    internal static Element KeyboardNavGrid(RenderContext ctx) =>
        Component<KeyboardNavGridComponent>();
}
