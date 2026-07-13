using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Reactor.Data;

namespace HeadTrax.Schema;

/// <summary>
/// Defines the column schema for the employee data grid.
/// Since we use Dictionary&lt;string, object?&gt; as the row type (no codegen),
/// FieldDescriptors are built manually with dictionary accessors.
/// </summary>
internal static class EmployeeSchema
{
    private static readonly HashSet<string> CompactFields =
    [
        "id", "employee_number", "first_name", "last_name",
        "title", "department", "location", "salary", "status",
    ];

    /// <summary>All columns for the employee grid.</summary>
    public static IReadOnlyList<FieldDescriptor> AllColumns { get; } = BuildColumns();

    /// <summary>A compact subset of columns for narrower views.</summary>
    public static IReadOnlyList<FieldDescriptor> CompactColumns { get; } =
        AllColumns.Where(c => CompactFields.Contains(c.Name)).ToList();

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Func<object, object?> DictGet(string key) =>
        row => ((Dictionary<string, object?>)row).GetValueOrDefault(key);

    private static Func<object, object?, object> DictSet(string key) =>
        (row, value) =>
        {
            var dict = (Dictionary<string, object?>)row;
            dict[key] = value;
            return dict;
        };

    private static FieldDescriptor Col(
        string name,
        string displayName,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
        Type type,
        double? width = null,
        bool sortable = true,
        bool filterable = true,
        PinPosition pin = PinPosition.None,
        bool isReadOnly = false,
        string? category = null,
        Func<object?, string>? format = null)
    {
        return new FieldDescriptor
        {
            Name = name,
            DisplayName = displayName,
            FieldType = type,
            GetValue = DictGet(name),
            SetValue = isReadOnly ? null : DictSet(name),
            IsReadOnly = isReadOnly,
            Width = width,
            Sortable = sortable,
            Filterable = filterable,
            Pin = pin,
            Category = category,
            FormatValue = format,
            Order = 0, // set below
        };
    }

    private static IReadOnlyList<FieldDescriptor> BuildColumns()
    {
        var columns = new List<FieldDescriptor>
        {
            // ── Identity ────────────────────────────────────────────────
            Col("id", "ID", typeof(long),
                width: 70, pin: PinPosition.Left, isReadOnly: true, category: "Identity"),

            Col("employee_number", "Employee #", typeof(string),
                width: 120, isReadOnly: true, category: "Identity"),

            // ── Person ──────────────────────────────────────────────────
            Col("first_name", "First Name", typeof(string),
                width: 130, category: "Person"),

            Col("last_name", "Last Name", typeof(string),
                width: 130, category: "Person"),

            Col("email", "Email", typeof(string),
                width: 240, category: "Person"),

            Col("phone", "Phone", typeof(string),
                width: 140, sortable: false, category: "Person"),

            Col("gender", "Gender", typeof(string),
                width: 100, category: "Person"),

            Col("birth_date", "Birth Date", typeof(string),
                width: 110, category: "Person"),

            // ── Role ────────────────────────────────────────────────────
            Col("title", "Title", typeof(string),
                width: 200, category: "Role"),

            Col("department", "Department", typeof(string),
                width: 150, category: "Role"),

            Col("level", "Level", typeof(long),
                width: 70, category: "Role",
                format: v => v switch
                {
                    0L => "L0 – CEO",
                    1L => "L1 – SVP",
                    2L => "L2 – VP",
                    3L => "L3 – Dir",
                    4L => "L4 – Sr Mgr",
                    5L => "L5 – Mgr",
                    6L => "L6 – Lead",
                    7L => "L7 – IC",
                    _ => v?.ToString() ?? "",
                }),

            Col("manager_id", "Manager ID", typeof(long?),
                width: 100, category: "Role"),

            // ── Location ────────────────────────────────────────────────
            Col("location", "Location", typeof(string),
                width: 150, category: "Location"),

            Col("is_remote", "Remote", typeof(long),
                width: 80, category: "Location",
                format: v => v is 1L or true ? "Yes" : "No"),

            // ── Compensation ────────────────────────────────────────────
            Col("salary", "Salary", typeof(double),
                width: 110, category: "Compensation",
                format: v => v is double d ? d.ToString("C0") : v?.ToString() ?? ""),

            Col("stock_options", "Stock Options", typeof(long),
                width: 120, category: "Compensation",
                format: v => v is long n ? n.ToString("N0") : v?.ToString() ?? ""),

            Col("cost_center", "Cost Center", typeof(string),
                width: 110, category: "Compensation"),

            // ── Status ──────────────────────────────────────────────────
            Col("status", "Status", typeof(string),
                width: 100, category: "Status"),

            Col("hire_date", "Hire Date", typeof(string),
                width: 110, category: "Status"),

            Col("performance_rating", "Perf Rating", typeof(double?),
                width: 110, category: "Status",
                format: v => v is double d ? d.ToString("0.0") : ""),

            // ── Audit ───────────────────────────────────────────────────
            Col("created_at", "Created", typeof(string),
                width: 160, isReadOnly: true, category: "Audit"),

            Col("updated_at", "Updated", typeof(string),
                width: 160, isReadOnly: true, category: "Audit"),
        };

        // Assign stable order
        for (var i = 0; i < columns.Count; i++)
            columns[i] = columns[i] with { Order = i };

        return columns;
    }
}
