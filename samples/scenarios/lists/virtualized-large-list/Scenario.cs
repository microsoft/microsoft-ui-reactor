// id: virtualized-large-list
// intent: virtualized list for large datasets using LazyVStack
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property UseWinUI=true
#:property WindowsPackageType=None

using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Virtualized List", width: 500, height: 400);

record LogRow(string Id, string Message);

class App : Component
{
    private static readonly IReadOnlyList<LogRow> Rows = Enumerable.Range(1, 1500)
        .Select(i => new LogRow($"row-{i}", $"Log entry {i}: visible rows are realized on demand."))
        .ToArray();

    public override Element Render()
    {
        return VStack(12,
            Heading($"LazyVStack ({Rows.Count} items)"),
            TextBlock("Use virtualization for long lists."),
            LazyVStack<LogRow>(
                Rows,
                row => row.Id,
                (row, index) => HStack(12,
                    TextBlock($"{index + 1}").Width(50),
                    TextBlock(row.Message))
                .Padding(8))
            .Height(300))
            .Padding(16);
    }
}
