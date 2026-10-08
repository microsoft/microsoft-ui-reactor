using System.Collections.ObjectModel;
using Microsoft.UI.Reactor.Advanced.Tabular;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Tabular;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Advanced.Factories;
using WinUITableView = Microsoft.UI.Xaml.Controls.Tabular.TableView;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Reactor.Advanced TableView (Microsoft.UI.Xaml.Controls.Tabular, Windows App SDK
/// experimental channel) against the live control: rows realize with both column kinds,
/// columns update in place, controlled selection does not echo, filter/group/empty
/// templates render Reactor content, and unmount tears down hosted cell subtrees.
/// </summary>
internal static partial class TableViewFixtures
{
    // A text column resolves its path through a classic {Binding}; the generated
    // ICustomPropertyProvider keeps that working under NativeAOT.
    [WinRT.GeneratedBindableCustomProperty]
    internal sealed partial class Row(int id, string name, string group, int score)
    {
        public int Id { get; } = id;
        public string Name { get; set; } = name;
        public string Group { get; } = group;
        public int Score { get; } = score;
    }

    private static Row[] Rows() =>
    [
        new(1, "Alice", "A", 42),
        new(2, "Bob", "B", 17),
        new(3, "Carol", "A", 99),
    ];

    private static WinUITableView? FindTable(Harness h) => h.FindControl<WinUITableView>(_ => true);

    internal class MountsRowsAndColumns(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rows = Rows();
            var host = H.CreateHost();
            host.Mount(_ => TableView(rows,
                    BoundColumn("Name", nameof(Row.Name), width: 140),
                    TemplateColumn<Row>("Score", r => TextBlock($"score:{r.Score}")))
                .Width(480).Height(240));

            H.Check("TableView_Mount_TemplateCellsRealized",
                await Harness.WaitFor(() => H.FindText("score:42") is not null && H.FindText("score:99") is not null,
                    maxPasses: 40, perPassMs: 25));
            H.Check("TableView_Mount_TextColumnBindingRealized",
                await Harness.WaitFor(() => H.FindText("Alice") is not null, maxPasses: 20, perPassMs: 25));

            var table = FindTable(H);
            H.Check("TableView_Mount_NativeColumns", table is not null
                && table.Columns.Count == 2
                && table.Columns[0] is TableViewTextColumn { Header: "Name" } text
                && text.Width.Value == 140
                && table.Columns[1] is TableViewTemplateColumn { Header: "Score" },
                $"columns={table?.Columns.Count}");
            H.Check("TableView_Mount_DefaultStyleApplied",
                table is not null && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(table) > 0);

            host.Mount(_ => TextBlock("TableView unmounted"));
            await Harness.Render();
        }
    }

    internal class UpdatesColumnsInPlace(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var first = Rows();
            var second = new Row[] { new(4, "Dave", "B", 7) };
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var columns = new List<TableColumn>
                {
                    BoundColumn(phase == 0 ? "Name" : "Full name", nameof(Row.Name), width: phase == 0 ? 120 : 200),
                    TemplateColumn<Row>("Score", r => TextBlock($"p{phase}:{r.Score}")),
                };
                if (phase == 2)
                    columns.Add(TemplateColumn<Row>("Group", r => TextBlock($"group:{r.Group}")));
                return VStack(8,
                    Button("Next phase", () => setPhase(phase + 1)),
                    TableView(phase == 0 ? first : second, [.. columns]).Width(480).Height(240));
            });

            await Harness.WaitFor(() => H.FindText("p0:42") is not null, maxPasses: 40, perPassMs: 25);
            var table = FindTable(H);
            var nameColumn = table?.Columns[0];
            var scoreColumn = table?.Columns[1];

            H.ClickButton("Next phase");
            H.Check("TableView_Update_TemplateCellsRerenderForNewItems",
                await Harness.WaitFor(() => H.FindText("p1:7") is not null && H.FindText("p0:42") is null,
                    maxPasses: 40, perPassMs: 25));
            H.Check("TableView_Update_ColumnInstancesPreserved",
                table is not null && ReferenceEquals(table.Columns[0], nameColumn) && ReferenceEquals(table.Columns[1], scoreColumn));
            H.Check("TableView_Update_ColumnPropsWritten",
                nameColumn is { Header: "Full name" } && nameColumn.Width.Value == 200,
                $"header={nameColumn?.Header} width={nameColumn?.Width.Value}");

            H.ClickButton("Next phase");
            H.Check("TableView_Update_ColumnAddedStructurally",
                await Harness.WaitFor(() => table!.Columns.Count == 3 && H.FindText("group:B") is not null,
                    maxPasses: 40, perPassMs: 25),
                $"columns={table?.Columns.Count}");

            host.Mount(_ => TextBlock("TableView unmounted"));
            await Harness.Render();
        }
    }

    internal class ControlledSelection(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rows = Rows();
            var callbacks = new List<int>();
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (selected, setSelected) = ctx.UseState(1);
                return VStack(8,
                    Button("Select first", () => setSelected(0)),
                    TableView(rows, TemplateColumn<Row>("Name", r => TextBlock($"sel:{r.Name}")))
                        .SelectionMode(TableViewSelectionMode.Single)
                        .SelectedIndex(selected)
                        .SelectionChanged((index, _) =>
                        {
                            callbacks.Add(index);
                            setSelected(index);
                        })
                        .Width(400).Height(200));
            });

            await Harness.WaitFor(() => H.FindText("sel:Alice") is not null, maxPasses: 40, perPassMs: 25);
            var table = FindTable(H);
            H.Check("TableView_Selection_ControlledMountApplied",
                await Harness.WaitFor(() => table?.SelectedIndex == 1, maxPasses: 20, perPassMs: 25),
                $"selected={table?.SelectedIndex}");

            H.ClickButton("Select first");
            H.Check("TableView_Selection_ControlledUpdateApplied",
                await Harness.WaitFor(() => table?.SelectedIndex == 0, maxPasses: 20, perPassMs: 25),
                $"selected={table?.SelectedIndex}");
            H.Check("TableView_Selection_ControlledWritesDoNotEcho", callbacks.Count == 0,
                $"callbacks=[{string.Join(",", callbacks)}]");

            // A selection the element did not ask for (user input) reaches the callback.
            table!.Select(2);
            H.Check("TableView_Selection_UserSelectionReported",
                await Harness.WaitFor(() => callbacks.Contains(2), maxPasses: 20, perPassMs: 25),
                $"callbacks=[{string.Join(",", callbacks)}]");

            host.Mount(_ => TextBlock("TableView unmounted"));
            await Harness.Render();
        }
    }

    internal class FilterGroupAndEmpty(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rows = new ObservableCollection<Row>(Rows());
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (mode, setMode) = ctx.UseState(0);
                var table = TableView(mode == 2 ? Array.Empty<Row>() : rows,
                        TemplateColumn<Row>("Name", r => TextBlock($"fg:{r.Name}")))
                    with
                {
                    GroupHeader = info => TextBlock($"group-header:{info.Key}"),
                    EmptyContent = TextBlock("nothing to show"),
                };
                if (mode == 1)
                    table = table.FilterRows<Row>(r => r.Group == "A").GroupRows<Row>(r => r.Group);
                return VStack(8,
                    Button("Next mode", () => setMode(mode + 1)),
                    table.Width(400).Height(240));
            });

            H.Check("TableView_Filter_UnfilteredRowsRealized",
                await Harness.WaitFor(() => H.FindText("fg:Bob") is not null, maxPasses: 40, perPassMs: 25));

            H.ClickButton("Next mode");
            H.Check("TableView_Filter_PredicateHidesRows",
                await Harness.WaitFor(() => H.FindText("fg:Bob") is null && H.FindText("fg:Alice") is not null,
                    maxPasses: 40, perPassMs: 25));
            H.Check("TableView_Group_HeaderRendersReactorContent",
                await Harness.WaitFor(() => H.FindText("group-header:A") is not null, maxPasses: 40, perPassMs: 25));

            H.ClickButton("Next mode");
            H.Check("TableView_Empty_ContentRendered",
                await Harness.WaitFor(() => H.FindText("nothing to show") is not null && H.FindText("fg:Alice") is null,
                    maxPasses: 40, perPassMs: 25));

            host.Mount(_ => TextBlock("TableView unmounted"));
            await Harness.Render();
        }
    }

    // An immutable row, as an MVU app holds it in state: no INPC, no bindable attribute.
    internal sealed record Contact(int Id, string Name, int Score);

    internal class ImmutableSnapshotsDiffInPlace(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Contact[] initial = [new(1, "Ada", 3), new(2, "Grace", 1), new(3, "Alan", 2)];
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (rows, setRows) = ctx.UseState(initial);
                return VStack(8,
                    Button("Rename and add", () => setRows(
                        [.. rows.Select(c => c.Id == 2 ? c with { Name = "Grace H." } : c), new(4, "Barbara", 4)])),
                    Button("Drop first", () => setRows(rows[1..])),
                    TableView(rows,
                            TextColumn<Contact>("Name", c => c.Name.StartsWith("mvu:") ? c.Name : $"mvu:{c.Name}",
                                (c, name) => setRows([.. rows.Select(r => r.Id == c.Id ? r with { Name = name } : r)])),
                            TextColumn<Contact>("Score", c => c.Score.ToString()).SortBy<Contact>((a, b) => a.Score.CompareTo(b.Score)))
                        .KeyRows<Contact>(c => c.Id)
                        .SelectionMode(TableViewSelectionMode.Single)
                        .Width(400).Height(260));
            });

            H.Check("TableView_Immutable_TextCellsRealized",
                await Harness.WaitFor(() => H.FindText("mvu:Grace") is not null, maxPasses: 40, perPassMs: 25));
            var table = FindTable(H)!;
            var boundSource = table.ItemsSource;
            H.Check("TableView_Immutable_ReadOnlyOnlyWithoutEditor",
                table.IsReadOnly == false
                && table.Columns[0] is TableViewTextColumn { IsReadOnly: false }
                && table.Columns[1] is TableViewTextColumn { IsReadOnly: true });

            // Drive a real edit through the native cell peer's UIA SetValue — BeginEdit, type into
            // the column's TextBox editor, CommitEdit — the path assistive technology uses. The
            // committed text must reach the onEdit callback, which produces the next snapshot.
            var alanCell = H.FindText("mvu:Alan")!;
            var cellHost = (FrameworkElement)Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(alanCell);
            DependencyObject? walk = cellHost;
            while (walk is not null and not TableViewRow) walk = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(walk);
            var peer = new TableViewCellAutomationPeer(cellHost, (TableViewRow)walk!, table.Columns[0], 0);
            H.Check("TableView_Immutable_CellPeerReadsText", peer.Value == "mvu:Alan" && !peer.IsReadOnly,
                $"value={peer.Value} readOnly={peer.IsReadOnly}");
            peer.SetValue("Alan T.");
            H.Check("TableView_Immutable_EditCommitsThroughCallback",
                await Harness.WaitFor(() => H.FindText("mvu:Alan T.") is not null && H.FindText("mvu:Alan") is null,
                    maxPasses: 40, perPassMs: 25));
            H.Check("TableView_Immutable_EditKeepsBoundCollection", ReferenceEquals(table.ItemsSource, boundSource));
            table.Select(0);
            await Harness.Render();

            H.ClickButton("Rename and add");
            H.Check("TableView_Immutable_ChangedRowReplaced",
                await Harness.WaitFor(() => H.FindText("mvu:Grace H.") is not null && H.FindText("mvu:Grace") is null
                    && H.FindText("mvu:Barbara") is not null, maxPasses: 40, perPassMs: 25));
            // The snapshot changed, the bound collection did not: the table kept its rows,
            // which is what preserves selection, scroll and an active edit.
            H.Check("TableView_Immutable_BoundCollectionStable", ReferenceEquals(table.ItemsSource, boundSource),
                $"before={boundSource?.GetType().Name} after={table.ItemsSource?.GetType().Name}");
            H.Check("TableView_Immutable_SelectionPreserved", table.SelectedIndex == 0 && table.SelectedItem is Contact { Id: 1 },
                $"selected={table.SelectedIndex}");

            H.ClickButton("Drop first");
            H.Check("TableView_Immutable_RemovedRowGone",
                await Harness.WaitFor(() => H.FindText("mvu:Ada") is null && H.FindText("mvu:Alan T.") is not null,
                    maxPasses: 40, perPassMs: 25));

            host.Mount(_ => TextBlock("TableView unmounted"));
            await Harness.Render();
        }
    }

    internal class UnmountTearsDownCells(Harness h) : SelfTestFixtureBase(h)
    {
        private static int s_mounted;
        private static int s_cleanedUp;

        private sealed class Cell : Component<int>
        {
            public override Element Render()
            {
                UseEffect(() =>
                {
                    Interlocked.Increment(ref s_mounted);
                    return () => Interlocked.Increment(ref s_cleanedUp);
                }, Array.Empty<object>());
                return TextBlock($"cell:{Props}");
            }
        }

        public override async Task RunAsync()
        {
            s_mounted = 0;
            s_cleanedUp = 0;
            var rows = Rows();
            var host = H.CreateHost();
            host.Mount(_ => TableView(rows, TemplateColumn<Row>("Id", r => Component<Cell, int>(r.Id)))
                .Width(400).Height(200));

            H.Check("TableView_Unmount_CellComponentsMounted",
                await Harness.WaitFor(() => Volatile.Read(ref s_mounted) >= rows.Length, maxPasses: 40, perPassMs: 25),
                $"mounted={s_mounted}");

            host.Mount(_ => TextBlock("TableView unmounted"));
            H.Check("TableView_Unmount_CellEffectsCleanedUp",
                await Harness.WaitFor(() => Volatile.Read(ref s_cleanedUp) == Volatile.Read(ref s_mounted),
                    maxPasses: 20, perPassMs: 25),
                $"mounted={s_mounted} cleanedUp={s_cleanedUp}");
        }
    }
}
