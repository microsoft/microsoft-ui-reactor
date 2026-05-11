using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Layout;
using HeadTrax.Schema;
using static Microsoft.UI.Reactor.Factories;

namespace HeadTrax.Components;

internal record EmployeeGridProps
{
    public required IDataSource<Dictionary<string, object?>> DataSource { get; init; }
}

/// <summary>
/// The main employee DataGrid component.
/// Demonstrates data virtualization with server-side sorting and filtering.
/// </summary>
internal class EmployeeGrid : Component<EmployeeGridProps>
{
    public override Element Render()
    {
        var source = Props.DataSource;
        var visibleColumns = EmployeeSchema.CompactColumns;

        return FlexColumn(
            // ── DataGrid ────────────────────────────────────────────
            DataGrid(
                source: source,
                columns: visibleColumns,
                selectionMode: SelectionMode.Multiple,
                rowHeight: 36,
                editable: false,
                showSearch: true,
                loadingTemplate: LoadingIndicator(),
                emptyTemplate: EmptyState(),
                rowDetailTemplate: EmployeeDetail
            ).Flex(grow: 1)
        );
    }

    private static Element EmployeeDetail(Dictionary<string, object?> employee, RowKey key)
    {
        string Get(string field) =>
            employee.TryGetValue(field, out var v) && v is not null ? v.ToString()! : "—";

        string FormatSalary() =>
            employee.TryGetValue("salary", out var v) && v is double d ? d.ToString("C0") : "—";

        string FormatLevel() => Get("level") switch
        {
            "0" => "L0 – CEO",
            "1" => "L1 – SVP",
            "2" => "L2 – VP",
            "3" => "L3 – Dir",
            "4" => "L4 – Sr Mgr",
            "5" => "L5 – Mgr",
            "6" => "L6 – Lead",
            "7" => "L7 – IC",
            _ => Get("level"),
        };

        string FormatRemote() =>
            employee.TryGetValue("is_remote", out var v) && v is 1L or true ? "Yes" : "No";

        return (FlexRow(
            // Left column: person info
            (FlexColumn(
                TextBlock($"{Get("first_name")} {Get("last_name")}").FontSize(16).Bold(),
                TextBlock(Get("title")).Opacity(0.7),
                TextBlock(Get("email")).FontSize(12).Opacity(0.5),
                TextBlock(Get("phone")).FontSize(12).Opacity(0.5)
            ) with { RowGap = 2 }).Flex(grow: 1),

            // Middle column: org info
            (FlexColumn(
                DetailField("Department", Get("department")),
                DetailField("Location", Get("location")),
                DetailField("Level", FormatLevel()),
                DetailField("Remote", FormatRemote()),
                DetailField("Manager ID", Get("manager_id"))
            ) with { RowGap = 2 }).Flex(grow: 1),

            // Right column: compensation & status
            (FlexColumn(
                DetailField("Status", Get("status")),
                DetailField("Salary", FormatSalary()),
                DetailField("Stock Options", Get("stock_options")),
                DetailField("Hire Date", Get("hire_date")),
                DetailField("Perf Rating", Get("performance_rating")),
                DetailField("Cost Center", Get("cost_center"))
            ) with { RowGap = 2 }).Flex(grow: 1)

        ) with { ColumnGap = 24 }).Padding(horizontal: 8, vertical: 4);
    }

    private static Element DetailField(string label, string value)
    {
        return (FlexRow(
            TextBlock($"{label}: ").FontSize(12).Opacity(0.5),
            TextBlock(value).FontSize(12)
        ) with { ColumnGap = 4 });
    }

    private static Element CapabilitiesBadge(DataSourceCapabilities caps)
    {
        var parts = new List<string>();
        if (caps.HasFlag(DataSourceCapabilities.ServerSort)) parts.Add("Sort");
        if (caps.HasFlag(DataSourceCapabilities.ServerFilter)) parts.Add("Filter");
        if (caps.HasFlag(DataSourceCapabilities.ServerSearch)) parts.Add("Search");
        if (caps.HasFlag(DataSourceCapabilities.ServerCount)) parts.Add("Count");

        var label = parts.Count > 0
            ? $"Server: {string.Join(", ", parts)}"
            : "Client-side only";

        return TextBlock(label).FontSize(11).Opacity(0.4);
    }

    private static Element LoadingIndicator()
    {
        return (FlexColumn(
            ProgressRing().Width(32).Height(32),
            TextBlock("Loading employee data...").Opacity(0.6).Margin(8, 0, 0, 0)
        ) with { AlignItems = FlexAlign.Center, JustifyContent = FlexJustify.Center })
            .Flex(grow: 1);
    }

    private static Element EmptyState()
    {
        return (FlexColumn(
            TextBlock("No employees found").FontSize(16).Bold(),
            TextBlock("Try adjusting your filters or generate sample data.").Opacity(0.6)
        ) with { AlignItems = FlexAlign.Center, JustifyContent = FlexJustify.Center })
            .Flex(grow: 1);
    }
}
