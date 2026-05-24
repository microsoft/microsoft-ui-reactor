// Recipe: fetch a remote list, render loading / data / error / reloading.
//
// Pattern: UseResource owns the cancellation token and dedups across siblings.
// AsyncValue<T> is a sealed 4-state record — pattern-match it (or call .Match).
// `deps` controls cache key — pass scalar values or memoized arrays, never
// freshly allocated lambdas / collections.

// In this clone, run `mur pack-local` once. Bump the version below to match
// whatever `mur pack-local` printed (default: 0.0.0-local). For a real NuGet
// consumer, set Version to a published Microsoft.UI.Reactor release.
#:package Microsoft.UI.Reactor@0.0.0-local
#:package Microsoft.WindowsAppSDK@2.0.1
#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property UseWinUI=true
#:property WindowsPackageType=None

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;  // InfoBarSeverity, VerticalAlignment
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Async demo", width: 560, height: 600);

record Repo(int Id, string Name, string Description);

static class Api
{
    public static async Task<IReadOnlyList<Repo>> ListReposAsync(string owner, CancellationToken ct)
    {
        // Replace with a real HttpClient call — this stub keeps the recipe self-contained.
        await Task.Delay(800, ct);
        if (owner == "fail") throw new InvalidOperationException("Owner not found");
        return [
            new(1, $"{owner}/alpha", "first repo"),
            new(2, $"{owner}/beta",  "second repo"),
            new(3, $"{owner}/gamma", "third repo"),
        ];
    }
}

class App : Component
{
    public override Element Render()
    {
        var (owner, setOwner) = UseState("microsoft");

        var repos = UseResource(
            ct => Api.ListReposAsync(owner, ct),
            deps: [owner]);

        return VStack(12,
            HStack(8,
                TextBox(owner, setOwner, placeholderText: "GitHub owner").Flex(grow: 1),
                Caption("(try \"fail\" to see error state)").VAlign(VerticalAlignment.Center)),

            repos.Match<Element>(
                loading: () => HStack(8, ProgressRing(), TextBlock("Loading…")),
                data: list => VStack(8, list.Select(r =>
                    Border(VStack(2,
                            TextBlock(r.Name).Bold(),
                            Caption(r.Description)))
                        .Padding(12)
                        .CornerRadius(6)
                        .WithKey(r.Id.ToString())).ToArray<Element?>()),
                error: ex => InfoBar("Error", ex.Message).Severity(InfoBarSeverity.Error),
                reloading: prev => VStack(8,
                    ProgressIndeterminate(),
                    VStack(8, prev.Select(r => TextBlock(r.Name).Opacity(0.5)).ToArray<Element?>())))
        ).Padding(24);
    }
}
