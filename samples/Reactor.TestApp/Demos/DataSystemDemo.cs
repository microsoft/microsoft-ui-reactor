using System.ComponentModel;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Data.Providers;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;

// ═══════════════════════════════════════════════════════════════════════
//  Shared types
// ═══════════════════════════════════════════════════════════════════════

record RgbColor(byte R, byte G, byte B)
{
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

record Employee(int Id, string Name, string Department, int Age, double Salary)
{
    public override string ToString() => $"{Name} ({Department})";
}

// ═══════════════════════════════════════════════════════════════════════
//  Root demo — tab selector only, no hooks beyond UseState for tab
// ═══════════════════════════════════════════════════════════════════════

enum DataDemoSection { PropertyGrid, VirtualListFixed, VirtualListVariable, DataSource, FormFields }

class DataSystemDemo : Microsoft.UI.Reactor.Core.Component
{
    static readonly string[] SectionLabels =
    [
        "PropertyGrid + FullEditor",
        "VirtualList (Fixed Height)",
        "VirtualList (Variable Height)",
        "ListDataSource",
        "FormField + FieldDescriptor",
    ];

    public override Element Render()
    {
        var (section, setSection) = UseState(DataDemoSection.PropertyGrid);

        return FlexColumn(
            Heading("Data System Demo").Flex(shrink: 0),
            TextBlock("Phase 0 + Phase 1: FieldDescriptor, PropertyGrid FullEditor, VirtualList, ListDataSource, FormField").Foreground(SecondaryText).Flex(shrink: 0),

            HStack(8,
                TextBlock("Demo:"),
                ComboBox(SectionLabels, (int)section, i => setSection((DataDemoSection)i)).Width(280)
            ).Margin(0, 8, 0, 0).Flex(shrink: 0),

            // Each section is its own Component — isolated hook state
            Border(
                section switch
                {
                    DataDemoSection.PropertyGrid => Component<PropertyGridFullEditorDemo>(),
                    DataDemoSection.VirtualListFixed => Component<VirtualListFixedDemo>(),
                    DataDemoSection.VirtualListVariable => Component<VirtualListVariableDemo>(),
                    DataDemoSection.DataSource => Component<DataSourceDemo>(),
                    DataDemoSection.FormFields => Component<FormFieldDemo>(),
                    _ => Empty()
                }
            ).Flex(grow: 1).Margin(0, 8, 0, 0)
        );
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  1. PropertyGrid with FullEditor "..." expand
// ═══════════════════════════════════════════════════════════════════════

class SpriteConfig : INotifyPropertyChanged
{
    private string _name = "Hero";
    [PropertyCategory("Identity")]
    [PropertyDisplayName("Sprite Name")]
    [PropertyDescription("The display name of this sprite")]
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropChanged(nameof(Name)); } }
    }

    private int _health = 100;
    [PropertyCategory("Stats")]
    [PropertyDescription("Current health points")]
    public int Health
    {
        get => _health;
        set { if (_health != value) { _health = value; OnPropChanged(nameof(Health)); } }
    }

    private double _speed = 5.5;
    [PropertyCategory("Stats")]
    [PropertyDescription("Movement speed")]
    public double Speed
    {
        get => _speed;
        set { if (_speed != value) { _speed = value; OnPropChanged(nameof(Speed)); } }
    }

    private bool _visible = true;
    [PropertyCategory("Appearance")]
    public bool Visible
    {
        get => _visible;
        set { if (_visible != value) { _visible = value; OnPropChanged(nameof(Visible)); } }
    }

    private RgbColor _tint = new(255, 200, 100);
    [PropertyCategory("Appearance")]
    [PropertyDescription("Sprite tint color — click '...' for detailed RGB editor")]
    public RgbColor Tint
    {
        get => _tint;
        set { if (_tint != value) { _tint = value; OnPropChanged(nameof(Tint)); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

class PropertyGridFullEditorDemo : Microsoft.UI.Reactor.Core.Component
{
    public override Element Render()
    {
        var registry = UseMemo(() =>
        {
            var reg = new TypeRegistry();
            reg.Register<RgbColor>(new TypeMetadata
            {
                Editor = (val, onChange) =>
                {
                    var c = (RgbColor)val;
                    return TextBlock(c.ToString()).SemiBold();
                },
                FullEditor = (val, onChange) =>
                {
                    var c = (RgbColor)val;
                    return FlexColumn(
                        TextBlock("RGB Color Editor").SemiBold(),
                        FlexRow(
                            TextBlock("R:").Width(20),
                            NumberBox(c.R, v => onChange(new RgbColor((byte)v, c.G, c.B))).Width(100)
                        ) with { AlignItems = FlexAlign.Center, ColumnGap = 8 },
                        FlexRow(
                            TextBlock("G:").Width(20),
                            NumberBox(c.G, v => onChange(new RgbColor(c.R, (byte)v, c.B))).Width(100)
                        ) with { AlignItems = FlexAlign.Center, ColumnGap = 8 },
                        FlexRow(
                            TextBlock("B:").Width(20),
                            NumberBox(c.B, v => onChange(new RgbColor(c.R, c.G, (byte)v))).Width(100)
                        ) with { AlignItems = FlexAlign.Center, ColumnGap = 8 },
                        Border(Empty())
                            .Background($"#{c.R:X2}{c.G:X2}{c.B:X2}")
                            .CornerRadius(4).Size(120, 32).Margin(0, 4, 0, 0)
                    ) with { RowGap = 8 };
                },
                Decompose = val =>
                {
                    var c = (RgbColor)val;
                    return new List<FieldDescriptor>
                    {
                        new() { Name = "R", FieldType = typeof(byte), GetValue = _ => c.R, Order = 0 },
                        new() { Name = "G", FieldType = typeof(byte), GetValue = _ => c.G, Order = 1 },
                        new() { Name = "B", FieldType = typeof(byte), GetValue = _ => c.B, Order = 2 },
                    };
                },
                Compose = (val, updates) =>
                {
                    var c = (RgbColor)val;
                    return new RgbColor(
                        updates.TryGetValue("R", out var r) ? (byte)r : c.R,
                        updates.TryGetValue("G", out var g) ? (byte)g : c.G,
                        updates.TryGetValue("B", out var b) ? (byte)b : c.B);
                },
                DisplayName = "RGB Color"
            });
            return reg;
        });

        var spriteRef = UseRef(new SpriteConfig());
        UseObservable(spriteRef.Current);

        return FlexColumn(
            SubHeading("PropertyGrid with FullEditor Expand").Flex(shrink: 0),
            TextBlock("The Tint property has a FullEditor registered. Look for the \"\u2026\" button next to the color value.").Foreground(SecondaryText).Flex(shrink: 0),
            TextBlock("Clicking \"\u2026\" opens a flyout with individual R/G/B NumberBox editors and a live preview.").Foreground(SecondaryText).Flex(shrink: 0),

            ScrollView(
                PropertyGrid(spriteRef.Current, registry)
            ).Flex(grow: 1).Margin(0, 8, 0, 0),

            Border(
                VStack(4,
                    TextBlock("Live values:").SemiBold(),
                    TextBlock($"Name={spriteRef.Current.Name}, Health={spriteRef.Current.Health}, " +
                         $"Speed={spriteRef.Current.Speed:F1}, Visible={spriteRef.Current.Visible}, " +
                         $"Tint={spriteRef.Current.Tint}")
                )
            ).Padding(12).Background(SubtleFill)
        ) with { RowGap = 4 };
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  2. VirtualList — Fixed Height (100k items)
// ═══════════════════════════════════════════════════════════════════════

class VirtualListFixedDemo : Microsoft.UI.Reactor.Core.Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(100000);
        var listRef = UseRef<VirtualListRef?>(null);

        return FlexColumn(
            SubHeading($"VirtualList — Fixed Height ({count:N0} items)").Flex(shrink: 0),
            TextBlock("Fixed-height mode: ItemHeight=32px, O(1) offset calculation, smooth scrolling.").Foreground(SecondaryText).Flex(shrink: 0),

            HStack(8,
                TextBlock("Items:"),
                Button("100", () => setCount(100)),
                Button("1K", () => setCount(1000)),
                Button("10K", () => setCount(10000)),
                Button("100K", () => setCount(100000)),
                Button("1M", () => setCount(1000000))
            ).Flex(shrink: 0),

            HStack(8,
                Button("Scroll to 0", () => { listRef.Current?.ScrollToIndex(0); }),
                Button("Scroll to middle", () => { listRef.Current?.ScrollToIndex(count / 2); }),
                Button("Scroll to end", () => { listRef.Current?.ScrollToIndex(count - 1); })
            ).Flex(shrink: 0),

            VirtualList(
                itemCount: count,
                renderItem: i => FlexRow(
                    TextBlock($"{i:N0}").Width(80).Foreground(TertiaryText),
                    TextBlock($"Item {i:N0}").Flex(grow: 1),
                    TextBlock(i % 2 == 0 ? "Even" : "Odd").Width(60).Foreground(SecondaryText)
                ) with { AlignItems = FlexAlign.Center, ColumnGap = 8 },
                itemHeight: 32,
                spacing: 1,
                getItemKey: i => i.ToString(),
                @ref: r => listRef.Current = r
            ).Flex(grow: 1)
        ) with { RowGap = 8 };
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  3. VirtualList — Variable Height
// ═══════════════════════════════════════════════════════════════════════

class VirtualListVariableDemo : Microsoft.UI.Reactor.Core.Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(10000);

        return FlexColumn(
            SubHeading($"VirtualList — Variable Height ({count:N0} items)").Flex(shrink: 0),
            TextBlock("Variable-height mode: each item renders at its natural size. EstimatedItemHeight=50px.").Foreground(SecondaryText).Flex(shrink: 0),

            HStack(8,
                TextBlock("Items:"),
                Button("100", () => setCount(100)),
                Button("1K", () => setCount(1000)),
                Button("10K", () => setCount(10000))
            ).Flex(shrink: 0),

            VirtualList(
                itemCount: count,
                renderItem: i =>
                {
                    var isExpanded = i % 7 == 0;
                    var padding = isExpanded ? 16.0 : 8.0;

                    return Border(
                        FlexColumn(
                            FlexRow(
                                TextBlock($"#{i:N0}").SemiBold(),
                                TextBlock(DataDemoHelpers.GetDepartment(i)).Foreground(SecondaryText)
                            ) with { ColumnGap = 12, AlignItems = FlexAlign.Center },
                            isExpanded
                                ? TextBlock($"This is an expanded item with more detail. " +
                                       $"Employee #{i} works in {DataDemoHelpers.GetDepartment(i)} and has been " +
                                       $"with the company for {(i % 20) + 1} years.").Foreground(SecondaryText)
                                : null
                        ) with { RowGap = 4 }
                    ).Padding(padding, 8).Background(i % 2 == 0 ? SubtleFill : SolidBackground);
                },
                estimatedItemHeight: 50,
                spacing: 2,
                getItemKey: i => $"var-{i}"
            ).Flex(grow: 1)
        ) with { RowGap = 8 };
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  4. ListDataSource with paging, sort, filter
// ═══════════════════════════════════════════════════════════════════════

class DataSourceDemo : Microsoft.UI.Reactor.Core.Component
{
    public override Element Render()
    {
        var source = UseMemo(() =>
        {
            var employees = Enumerable.Range(0, 500).Select(i => new Employee(
                Id: i,
                Name: $"{DataDemoHelpers.GetFirstName(i)} {DataDemoHelpers.GetLastName(i)}",
                Department: DataDemoHelpers.GetDepartment(i),
                Age: 22 + (i % 40),
                Salary: 40000 + (i * 137 % 80000)
            )).ToArray();
            return new ListDataSource<Employee>(employees, e => (RowKey)e.Id);
        });

        var (pageSize, setPageSize) = UseState(20);
        var (token, setToken) = UseState<string?>(null);
        var (sortField, setSortField) = UseState("Name");
        var (sortDir, setSortDir) = UseState(SortDirection.Ascending);
        var (filterDept, setFilterDept) = UseState("");
        var (searchQuery, setSearchQuery) = UseState("");
        var (page, setPage) = UseState<DataPage<Employee>?>(null);
        var (loading, setLoading) = UseState(false);

        UseEffect(() =>
        {
            _ = FetchAsync();
            async Task FetchAsync()
            {
                setLoading(true);
                var sort = new SortDescriptor[] { new(sortField, sortDir) };
                var filters = string.IsNullOrEmpty(filterDept)
                    ? null
                    : new FilterDescriptor[] { new(nameof(Employee.Department), FilterOperator.Equals, filterDept) };
                var request = new DataRequest
                {
                    PageSize = pageSize,
                    ContinuationToken = token,
                    Sort = sort,
                    Filters = filters,
                    SearchQuery = string.IsNullOrEmpty(searchQuery) ? null : searchQuery,
                };
                var result = await source.GetPageAsync(request);
                setPage(result);
                setLoading(false);
            }
        }, token!, sortField, sortDir, filterDept, searchQuery, pageSize);

        var departments = new[] { "", "Engineering", "Marketing", "Sales", "HR", "Finance" };
        var sortFields = new[] { "Name", "Department", "Age", "Salary" };

        return FlexColumn(
            SubHeading("ListDataSource — Paging, Sorting, Filtering").Flex(shrink: 0),
            TextBlock($"500 employees. Capabilities: {source.Capabilities}").Foreground(SecondaryText).Flex(shrink: 0),

            HStack(8,
                TextBlock("Sort:"),
                ComboBox(sortFields, Array.IndexOf(sortFields, sortField),
                    i => { setToken(null); setSortField(sortFields[i]); }).Width(120),
                Button(sortDir == SortDirection.Ascending ? "\u25B2 Asc" : "\u25BC Desc",
                    () => { setToken(null); setSortDir(sortDir == SortDirection.Ascending ? SortDirection.Descending : SortDirection.Ascending); })
            ).Flex(shrink: 0),

            HStack(8,
                TextBlock("Dept:"),
                ComboBox(departments, Math.Max(0, Array.IndexOf(departments, filterDept)),
                    i => { setToken(null); setFilterDept(departments[i]); }).Width(140),
                TextBlock("Search:"),
                TextBox(searchQuery, s => { setToken(null); setSearchQuery(s); }, placeholderText: "Search...").Width(180)
            ).Flex(shrink: 0),

            loading
                ? TextBlock("Loading...").Foreground(SecondaryText)
                : page is not null
                    ? FlexColumn(
                        Caption($"Showing {page.Items.Count} of {page.TotalCount} results").Foreground(SecondaryText).Flex(shrink: 0),

                        FlexRow(
                            TextBlock("ID").Width(50).SemiBold(),
                            TextBlock("Name").Width(180).SemiBold(),
                            TextBlock("Department").Width(120).SemiBold(),
                            TextBlock("Age").Width(50).SemiBold(),
                            TextBlock("Salary").Width(100).SemiBold()
                        ) with { ColumnGap = 8 },

                        VirtualList(
                            itemCount: page.Items.Count,
                            renderItem: i =>
                            {
                                var emp = page.Items[i];
                                return FlexRow(
                                    TextBlock($"{emp.Id}").Width(50).Foreground(TertiaryText),
                                    TextBlock(emp.Name).Width(180),
                                    TextBlock(emp.Department).Width(120).Foreground(SecondaryText),
                                    TextBlock($"{emp.Age}").Width(50),
                                    TextBlock($"${emp.Salary:N0}").Width(100)
                                ) with { ColumnGap = 8, AlignItems = FlexAlign.Center };
                            },
                            itemHeight: 28,
                            spacing: 1,
                            getItemKey: i => page.Items[i].Id.ToString()
                        ).Flex(grow: 1),

                        FlexRow(
                            Button("\u25C0 Previous", () => setToken(null))
                                .IsEnabled(!(token is null)),
                            TextBlock($"Page size: {pageSize}"),
                            Button("Next \u25B6", () => setToken(page.ContinuationToken))
                                .IsEnabled(!(page.ContinuationToken is null))
                        ) with { ColumnGap = 8, AlignItems = FlexAlign.Center }
                      ) with { RowGap = 4 }
                    : TextBlock("No data")
        ) with { RowGap = 8 };
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  5. FormField auto-wired from FieldDescriptor
// ═══════════════════════════════════════════════════════════════════════

class FormFieldDemo : Microsoft.UI.Reactor.Core.Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("Alice");
        var (age, setAge) = UseState((object)25);
        var (dept, setDept) = UseState("Engineering");

        var registry = UseMemo(() => new TypeRegistry());

        var nameField = UseMemo(() => new FieldDescriptor
        {
            Name = "Name",
            DisplayName = "Employee Name",
            FieldType = typeof(string),
            GetValue = _ => name,
            Description = "Full name of the employee",
            Validators = new IValidator[] { Validate.Required(), Validate.MinLength(2, "Name must be at least 2 characters") },
        }, name);

        var ageField = UseMemo(() => new FieldDescriptor
        {
            Name = "Age",
            DisplayName = "Age",
            FieldType = typeof(int),
            GetValue = _ => age,
            Description = "Employee age (18-100)",
            Validators = new IValidator[] { Validate.Required(), Validate.Range(18, 100, "Age must be between 18 and 100") },
        }, age);

        var deptField = UseMemo(() => new FieldDescriptor
        {
            Name = "Department",
            DisplayName = "Department",
            FieldType = typeof(string),
            GetValue = _ => dept,
            Description = "Organizational department",
        }, dept);

        return FlexColumn(
            SubHeading("FormField Auto-Wired from FieldDescriptor"),
            TextBlock("FormField overload resolves editors from TypeRegistry, sets label/description from FieldDescriptor, detects Required from validators.").Foreground(SecondaryText),

            FormField(nameField, name, v => setName((string)v), registry).Margin(horizontal: 0, vertical: 8),
            FormField(ageField, age, v => setAge(v), registry).Margin(horizontal: 0, vertical: 8),
            FormField(deptField, dept, v => setDept((string)v), registry).Margin(horizontal: 0, vertical: 8),

            Border(
                VStack(4,
                    TextBlock("Current values:").SemiBold(),
                    TextBlock($"Name=\"{name}\", Age={age}, Department=\"{dept}\"")
                )
            ).Padding(12).Background(SubtleFill).Margin(0, 8, 0, 0)
        ) with { RowGap = 4 };
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  Shared helpers
// ═══════════════════════════════════════════════════════════════════════

static class DataDemoHelpers
{
    public static string GetDepartment(int i) => (i % 5) switch
    {
        0 => "Engineering",
        1 => "Marketing",
        2 => "Sales",
        3 => "HR",
        _ => "Finance",
    };

    public static string GetFirstName(int i) => (i % 10) switch
    {
        0 => "Alice", 1 => "Bob", 2 => "Carol", 3 => "Dave", 4 => "Eve",
        5 => "Frank", 6 => "Grace", 7 => "Heidi", 8 => "Ivan", _ => "Judy",
    };

    public static string GetLastName(int i) => (i % 8) switch
    {
        0 => "Smith", 1 => "Johnson", 2 => "Williams", 3 => "Brown",
        4 => "Jones", 5 => "Garcia", 6 => "Miller", _ => "Davis",
    };
}
