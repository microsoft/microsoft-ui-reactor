// id: use-memo
// intent: memoize derived filtered data so it recomputes only when inputs change
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// UseMemo is a good fit for derived values that are expensive or noisy to rebuild.
ReactorApp.Run<App>("UseMemo", width: 400, height: 200);

class App : Component
{
    static readonly string[] Items = ["Apple", "Apricot", "Banana", "Blueberry", "Cherry", "Date"];

    public override Element Render()
    {
        var (filter, setFilter) = UseState("");
        var filtered = UseMemo(() => Items
            .Where(item => item.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToArray(), filter);

        return VStack(12,
            TextBox(filter, setFilter, "Filter fruit", header: "Filter"),
            Caption($"Visible items: {filtered.Length}"),
            ForEach(filtered, item => TextBlock(item)));
    }
}

