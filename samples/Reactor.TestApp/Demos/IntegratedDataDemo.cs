using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;

// ═══════════════════════════════════════════════════════════════════════
//  Model — mutable INPC class with validation defined once
// ═══════════════════════════════════════════════════════════════════════

class ProjectTask : INotifyPropertyChanged
{
    // Shared validators — defined once, used by DataGrid, PropertyGrid, and FormField
    public static readonly IValidator[] NameValidators =
        [Validate.Required(), Validate.MinLength(2, "Name must be at least 2 characters")];
    public static readonly IValidator[] PriorityValidators =
        [Validate.Range(1, 5, "Priority must be between 1 and 5")];
    public static readonly IValidator[] BudgetValidators =
        [Validate.Range(0, 100000, "Budget must be between 0 and 100,000")];

    public int Id { get; init; }

    private string _name = "";
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropChanged(); } }
    }

    private string _category = "";
    public string Category
    {
        get => _category;
        set { if (_category != value) { _category = value; OnPropChanged(); } }
    }

    private int _priority = 3;
    public int Priority
    {
        get => _priority;
        set { if (_priority != value) { _priority = value; OnPropChanged(); } }
    }

    private double _budget;
    public double Budget
    {
        get => _budget;
        set { if (_budget != value) { _budget = value; OnPropChanged(); } }
    }

    private bool _complete;
    public bool Complete
    {
        get => _complete;
        set { if (_complete != value) { _complete = value; OnPropChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public override string ToString() => $"{Name} ({Category})";
}

// ═══════════════════════════════════════════════════════════════════════
//  Integrated Data Demo — DataGrid + PropertyGrid + FormField
// ═══════════════════════════════════════════════════════════════════════

class IntegratedDataDemo : Microsoft.UI.Reactor.Core.Component
{
    static readonly string[] Categories = ["Engineering", "Marketing", "Sales", "HR", "Finance"];

    public override Element Render()
    {
        // ── Data — ObservableListDataSource so DataGrid refreshes on INPC ──
        var collection = UseMemo(() => new ObservableCollection<ProjectTask>(
            Enumerable.Range(0, 30).Select(i => new ProjectTask
            {
                Id = i,
                Name = $"Task {i}",
                Category = Categories[i % Categories.Length],
                Priority = (i % 5) + 1,
                Budget = 5000 + (i * 1234 % 95000),
                Complete = i % 4 == 0,
            })));

        var source = UseMemo(() =>
            new ObservableListDataSource<ProjectTask>(collection, t => (RowKey)t.Id));

        // ── Selection ─────────────────────────────────────────────
        var (selectedKeys, setSelectedKeys) = UseState<IReadOnlySet<RowKey>>(new HashSet<RowKey>());
        var selectedItem = selectedKeys.Count > 0
            ? collection.FirstOrDefault(t => selectedKeys.Contains((RowKey)t.Id))
            : null;

        // ── INPC observation — force re-render when selected item changes ──
        var (_, forceRender) = UseReducer(0);
        UseEffect(() =>
        {
            if (selectedItem is null) return () => { };
            void Handler(object? s, PropertyChangedEventArgs e) => forceRender(v => v + 1);
            selectedItem.PropertyChanged += Handler;
            return () => selectedItem.PropertyChanged -= Handler;
        }, selectedItem!);

        // ── TypeRegistry for PropertyGrid ─────────────────────────
        var registry = UseMemo(() =>
        {
            var reg = new TypeRegistry();
            reg.Register<ProjectTask>(new TypeMetadata
            {
                Decompose = target =>
                {
                    var t = (ProjectTask)target;
                    return new List<FieldDescriptor>
                    {
                        new()
                        {
                            Name = "Name", DisplayName = "Task Name",
                            FieldType = typeof(string),
                            GetValue = _ => t.Name,
                            SetValue = (_, val) => { t.Name = (string)(val ?? ""); return t; },
                            Validators = ProjectTask.NameValidators,
                            Description = "The name of this task",
                            Order = 0,
                        },
                        new()
                        {
                            Name = "Category", DisplayName = "Category",
                            FieldType = typeof(string),
                            GetValue = _ => t.Category,
                            SetValue = (_, val) => { t.Category = (string)(val ?? ""); return t; },
                            Order = 1,
                        },
                        new()
                        {
                            Name = "Priority", DisplayName = "Priority",
                            FieldType = typeof(int),
                            GetValue = _ => t.Priority,
                            SetValue = (_, val) => { t.Priority = (int)(val ?? 3); return t; },
                            Validators = ProjectTask.PriorityValidators,
                            Description = "Priority 1 (lowest) to 5 (highest)",
                            Order = 2,
                        },
                        new()
                        {
                            Name = "Budget", DisplayName = "Budget",
                            FieldType = typeof(double),
                            GetValue = _ => t.Budget,
                            SetValue = (_, val) => { t.Budget = Convert.ToDouble(val ?? 0.0); return t; },
                            Validators = ProjectTask.BudgetValidators,
                            Description = "Project budget (0 - 100,000)",
                            Order = 3,
                        },
                        new()
                        {
                            Name = "Complete", DisplayName = "Complete",
                            FieldType = typeof(bool),
                            GetValue = _ => t.Complete,
                            SetValue = (_, val) => { t.Complete = (bool)(val ?? false); return t; },
                            Order = 4,
                        },
                    };
                },
            });
            return reg;
        });

        // ── DataGrid columns — same validators as PropertyGrid ───
        var columns = UseMemo(() => new FieldDescriptor[]
        {
            Column<ProjectTask>("Id", t => t.Id, width: 50),
            (Column<ProjectTask>("Name", t => t.Name, editable: true,
                    displayName: "Task Name", width: 180)
                .Validate(ProjectTask.NameValidators)).Build(),
            Column<ProjectTask>("Category", t => t.Category,
                editable: true, width: 120),
            (Column<ProjectTask>("Priority", t => t.Priority,
                    editable: true, width: 70)
                .Validate(ProjectTask.PriorityValidators)).Build(),
            (Column<ProjectTask>("Budget", t => t.Budget,
                    editable: true, displayName: "Budget", format: "C0", width: 100)
                .Validate(ProjectTask.BudgetValidators)).Build(),
            Column<ProjectTask>("Complete", t => t.Complete,
                editable: true, width: 80),
        });

        // ── Layout ────────────────────────────────────────────────
        return FlexColumn(
            Heading("Integrated Data Demo").Flex(shrink: 0),
            TextBlock("All 4 data system pieces: FieldDescriptor defines fields + validation once. " +
                 "DataGrid, PropertyGrid, and FormField all share the same definitions. " +
                 "Edit in any view — changes sync to the other two.")
                .Foreground(SecondaryText).Flex(shrink: 0),

            (FlexRow(
                // Left: DataGrid (60%)
                (FlexColumn(
                    TextBlock("DataGrid").SemiBold().Flex(shrink: 0),
                    Caption("Click a row to select. Double-click or press F2 to edit a cell.").Foreground(TertiaryText).Flex(shrink: 0),
                    DataGrid(
                        source: source,
                        columns: columns,
                        selectionMode: SelectionMode.Single,
                        onSelectionChanged: keys => setSelectedKeys(keys),
                        editable: true,
                        editMode: EditMode.Cell,
                        onRowChanged: (key, item) => Task.CompletedTask,
                        rowHeight: 32
                    ).Flex(grow: 1)
                ) with { RowGap = 4 }).Flex(grow: 3, basis: 0),

                // Right: FormField + PropertyGrid (40%) — Expr() (spec 033 §5)
                // keeps the validator locals scoped to the branch that uses them.
                Expr(() =>
                {
                    if (selectedItem is null)
                        return Border(
                            TextBlock("Select a row in the DataGrid to see its details here.")
                                .Foreground(TertiaryText).Padding(20)
                        ).Background(SubtleFill).CornerRadius(4).VAlign(VerticalAlignment.Center);

                    var nameField = new FieldDescriptor
                    {
                        Name = "Name",
                        DisplayName = "Task Name",
                        FieldType = typeof(string),
                        GetValue = _ => selectedItem.Name,
                        Validators = ProjectTask.NameValidators,
                        Description = "Edit the task name here — changes sync to DataGrid and PropertyGrid",
                    };

                    // Run validators once, share results across views.
                    var nameErrors = ProjectTask.NameValidators
                        .Select(v => v.Validate(selectedItem.Name, "Name"))
                        .Where(m => m is not null).ToList();
                    var priorityErrors = ProjectTask.PriorityValidators
                        .Select(v => v.Validate(selectedItem.Priority, "Priority"))
                        .Where(m => m is not null).ToList();
                    var budgetErrors = ProjectTask.BudgetValidators
                        .Select(v => v.Validate(selectedItem.Budget, "Budget"))
                        .Where(m => m is not null).ToList();
                    var allErrors = nameErrors.Concat(priorityErrors).Concat(budgetErrors).ToList();

                    var nameEditor = TextBox(selectedItem.Name, v =>
                    {
                        selectedItem.Name = (string)v;
                    }, placeholder: "Task name...");
                    if (nameErrors.Count > 0)
                        nameEditor = nameEditor.WithBorder(Theme.Ref("SystemFillColorCriticalBrush"), 1);

                    return VStack(8,
                        // FormField section — red border on invalid
                        Border(
                            VStack(4,
                                TextBlock("FormField (first property)").SemiBold(),
                                Caption("Task Name *"),
                                nameEditor,
                                nameErrors.Count > 0
                                    ? Caption(nameErrors[0]!.Text).Foreground(Theme.Ref("SystemFillColorCriticalBrush"))
                                    : Caption(nameField.Description ?? "").Foreground(TertiaryText)
                            )
                        ).Padding(12).Background(SubtleFill).CornerRadius(4),

                        // PropertyGrid section — plain, no inline validation
                        TextBlock("PropertyGrid (selected item)").SemiBold(),
                        PropertyGrid(selectedItem, registry),

                        // Form-level validation summary
                        allErrors.Count > 0
                            ? Border(
                                VStack(4, new Element?[] {
                                    TextBlock($"Validation ({allErrors.Count} error{(allErrors.Count != 1 ? "s" : "")})")
                                        .SemiBold().Foreground(Theme.Ref("SystemFillColorCriticalBrush"))
                                }.Concat(allErrors.Select(e => (Element?)
                                    Caption($"• {e!.Field}: {e.Text}").Foreground(Theme.Ref("SystemFillColorCriticalBrush"))
                                )).ToArray())
                            ).Padding(12).WithBorder(Theme.Ref("SystemFillColorCriticalBrush"), 1).CornerRadius(4)
                            : (Element)Border(
                                TextBlock("✓ All fields valid").Foreground(Theme.Ref("SystemFillColorSuccessBrush")).SemiBold()
                            ).Padding(12).WithBorder(Theme.Ref("SystemFillColorSuccessBrush"), 1).CornerRadius(4)
                    );
                }).Flex(grow: 2, basis: 0)

            ) with { ColumnGap = 16, AlignItems = FlexAlign.Stretch }).Flex(grow: 1)

        ) with { RowGap = 8 };
    }
}
