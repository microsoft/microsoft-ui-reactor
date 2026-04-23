using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Data;
using Microsoft.UI.Reactor.Layout;
using HeadTrax.Components;
using HeadTrax.DataSources;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace HeadTrax;

/// <summary>
/// HeadTrax root application component.
/// Demonstrates DataGrid with both SQLite direct and GraphQL data sources.
/// </summary>
internal class App : Component
{
    public override Element Render()
    {
        // Data source mode: "sqlite" or "graphql"
        var (mode, setMode) = UseState("sqlite");

        // Hold a ref to the current source so we can dispose on swap
        var sourceRef = UseRef<IDisposable?>(null);

        // Rows loaded counter (proves virtualization — not all 100K loaded).
        // threadSafe: the OnRowsFetchedChanged callback fires from GetPageAsync
        // which may run on a deferred continuation after Task.Yield().
        var (rowsLoaded, setRowsLoaded) = UseState(0, threadSafe: true);

        // Create the data source (pure — no side effects)
        var dataSource = UseMemo(() =>
            CreateDataSource(mode, setRowsLoaded), [mode]);

        // Dispose old source and reset counter when mode changes
        UseEffect(() =>
        {
            setRowsLoaded(0);
            sourceRef.Current = dataSource as IDisposable;
            return () =>
            {
                if (sourceRef.Current is IDisposable d)
                    d.Dispose();
            };
        }, [mode]);

        var titleBar = (TitleBar("HeadTrax") with
        {
            Content = HStack(6,
                ToggleButton("SQLite Direct", isChecked: mode == "sqlite",
                    onToggled: on => { if (on) setMode("sqlite"); })
                    .AutomationName("SQLite Direct data source"),
                ToggleButton("GraphQL API", isChecked: mode == "graphql",
                    onToggled: on => { if (on) setMode("graphql"); })
                    .AutomationName("GraphQL API data source")
            ),

            RightHeader = HStack(12,
                Caption(ReactorFeatureFlags.UseHookBasedPaging ? "hook paging" : "legacy paging")
                    .Foreground(Theme.TertiaryText)
                    .Set(t => t.IsHitTestVisible = false),
                Caption($"{rowsLoaded:N0} rows fetched").Foreground(Theme.SecondaryText)
                    .LiveRegion(Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite)
                    .Set(t => t.IsHitTestVisible = false)
            ),
        }).Flex(shrink: 0);

        return FlexColumn(
            titleBar,

            // ── Main content: Employee DataGrid ─────────────────────
            Component<EmployeeGrid, EmployeeGridProps>(new EmployeeGridProps
            {
                DataSource = dataSource,
            }).Flex(grow: 1)
              .Landmark(Microsoft.UI.Xaml.Automation.Peers.AutomationLandmarkType.Main)
              .AutomationName("Employee data")
        );
    }

    private static IDataSource<Dictionary<string, object?>> CreateDataSource(
        string mode, Action<int> onRowsFetched)
    {
        switch (mode)
        {
            case "graphql":
                var gql = new GraphQLDataSource(AppConfig.GraphQLUrl);
                gql.OnRowsFetchedChanged = onRowsFetched;
                return gql;
            default:
                var sqlite = new SqliteDataSource(AppConfig.SqliteDbPath);
                sqlite.OnRowsFetchedChanged = onRowsFetched;
                return sqlite;
        }
    }
}
