using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;

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

            // This grid renders through DataGridComponent, so its commits take HandleAsyncCommit's
            // dispatcher path: UseMutation invokes onRowChanged synchronously on the committing
            // thread, which is this UI thread. Capture the dispatcher anyway — see the callback
            // below for why the marshal stays.
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
                        // Keep this marshal. It is not a required thread hop on this path — the
                        // callback is invoked on the committing (UI) thread — but the DataGrid's
                        // contract makes the callback's thread depend on which commit path runs
                        // (HandleAsyncCommit's no-dispatcher fallback offloads to the thread pool),
                        // and enqueueing also keeps the state write out of the commit call stack.
                        // The functional update composes append-only, so concurrent commits
                        // accumulate instead of clobbering each other.
                        dq?.TryEnqueue(() =>
                        {
                            appendEdit(prev => prev + $"[{key.Value}:{item.FirstName},{item.LastName}]");
                        });
                        return Task.CompletedTask;
                    },
                    rowHeight: 36
                ).AutomationId("EditableGrid"),
                // Focusable target OUTSIDE the grid, so an E2E can move focus off the grid and
                // exercise the "focus left the grid" LostFocus commit path.
                Button("blur anchor", () => { }).AutomationId("BlurAnchor")
            );
        }
    }

    internal static Element EditableGrid(RenderContext ctx) =>
        Component<EditableGridComponent>();

    // ── Row-mode editable DataGrid (issue #976) ──────────────────
    //
    // The cell-mode grid above cannot exercise row-mode Tab: in cell mode Tab commits the cell and
    // reopens the next one, whereas in row mode Tab has to cycle real keyboard focus among the
    // row's editors WITHOUT committing.

    internal class RowEditGridComponent : Component
    {
        public override Element Render()
        {
            var (editLog, appendEdit) = UseReducer("");
            var dq = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            var source = UseMemo(() => new ListDataSource<Employee>(
                new[]
                {
                    new Employee { Id = 1, FirstName = "Alice", LastName = "Smith", Salary = 75000 },
                    new Employee { Id = 2, FirstName = "Bob", LastName = "Jones", Salary = 82000 },
                    new Employee { Id = 3, FirstName = "Carol", LastName = "Lee", Salary = 91000 },
                },
                e => (RowKey)e.Id));

            // Id is deliberately read-only so row-mode Tab has a column it must SKIP, and the two
            // editable columns are adjacent so a wrap is distinguishable from "stayed put".
            var columns = new FieldDescriptor[]
            {
                Column<Employee>("Id", e => e.Id, width: 60),
                Column<Employee>("FirstName", e => e.FirstName, editable: true, displayName: "First Name", width: 140),
                Column<Employee>("LastName", e => e.LastName, editable: true, displayName: "Last Name", width: 140),
                Column<Employee>("Salary", e => e.Salary, format: "C0", width: 100),
            };

            return VStack(8,
                TextBlock($"Edits:{editLog}").AutomationId("RowEditLog"),
                DataGrid(
                    source: source,
                    columns: columns,
                    editable: true,
                    editMode: EditMode.Row,
                    onRowChanged: (key, item) =>
                    {
                        dq?.TryEnqueue(() =>
                        {
                            appendEdit(prev => prev + $"[{key.Value}:{item.FirstName},{item.LastName}]");
                        });
                        return Task.CompletedTask;
                    },
                    rowHeight: 36
                ).AutomationId("RowEditGrid"),
                Button("blur anchor", () => { }).AutomationId("RowEditBlurAnchor")
            );
        }
    }

    internal static Element RowEditGrid(RenderContext ctx) =>
        Component<RowEditGridComponent>();
}
