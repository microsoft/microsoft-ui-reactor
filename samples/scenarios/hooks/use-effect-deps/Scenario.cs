// id: use-effect-deps
// intent: re-run an effect whenever the search query dependency changes
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// The effect reruns for each query change and writes the latest derived results.
ReactorApp.Run<App>("UseEffectDeps", width: 400, height: 200);

class App : Component
{
    static readonly string[] Data = ["Hooks", "Reducer", "Memo", "Context", "Ref", "Effect"];

    public override Element Render()
    {
        var (query, setQuery) = UseState("");
        var (runs, bumpRuns) = UseReducer(0);
        var (results, setResults) = UseState(Array.Empty<string>());

        UseEffect(() =>
        {
            bumpRuns(value => value + 1);
            setResults(query.Length == 0
                ? Array.Empty<string>()
                : Data.Where(item => item.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray());
        }, query);

        return VStack(12,
            TextBox(query, setQuery, "Search hooks", header: "Search query"),
            Caption($"Effect runs: {runs}"),
            ForEach(results, item => TextBlock(item)));
    }
}

