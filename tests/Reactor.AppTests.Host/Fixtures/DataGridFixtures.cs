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
            var (lastEdit, setLastEdit) = UseState("none");

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
                TextBlock($"Last edit: {lastEdit}").AutomationId("EditStatus"),
                DataGrid(
                    source: source,
                    columns: columns,
                    editable: true,
                    editMode: EditMode.Cell,
                    onRowChanged: (key, item) =>
                    {
                        // Dispatch to UI thread — this callback runs on a threadpool
                        // thread from HandleAsyncCommit's Task.Run.
                        dq?.TryEnqueue(() =>
                        {
                            setLastEdit($"{key.Value}:{item.FirstName},{item.LastName}");
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
}
