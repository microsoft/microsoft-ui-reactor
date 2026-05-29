// id: async-fetch-list
// intent: fetch async data with UseResource and render loading, error, and reloading states
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Async Fetch List", width: 560, height: 520);

record Repo(int Id, string Name, string Description);

static class Api
{
    public static async Task<IReadOnlyList<Repo>> ListReposAsync(string owner, CancellationToken cancellationToken)
    {
        await Task.Delay(800, cancellationToken);
        if (owner == "fail")
        {
            throw new InvalidOperationException("Owner not found");
        }

        return
        [
            new(1, $"{owner}/alpha", "first repo"),
            new(2, $"{owner}/beta", "second repo"),
            new(3, $"{owner}/gamma", "third repo")
        ];
    }
}

class App : Component
{
    public override Element Render()
    {
        var (owner, setOwner) = UseState("microsoft");
        var repos = UseResource(cancellationToken => Api.ListReposAsync(owner, cancellationToken), deps: [owner]);

        return VStack(12,
            TextField(owner, setOwner, placeholder: "GitHub owner"),
            Caption("Try \"fail\" to see the error state."),
            repos.Match<Element>(
                loading: () => HStack(8,
                    ProgressRing().IsActive(true).Width(20).Height(20),
                    TextBlock("Loading…")),
                data: list => VStack(8,
                    list.Select(repo =>
                        Border(
                            VStack(2,
                                TextBlock(repo.Name).Bold(),
                                Caption(repo.Description)))
                            .Padding(12)
                            .CornerRadius(6)
                            .WithKey(repo.Id.ToString())
                    ).ToArray<Element?>()),
                error: ex => InfoBar("Error", ex.Message).Severity(InfoBarSeverity.Error),
                reloading: previous => VStack(8,
                    HStack(8,
                        ProgressRing().IsActive(true).Width(20).Height(20),
                        TextBlock("Refreshing…")),
                    VStack(8,
                        previous.Select(repo =>
                            TextBlock(repo.Name)
                                .Opacity(0.5)
                                .WithKey(repo.Id.ToString()))
                        .ToArray<Element?>()))))
        .Padding(24);
    }
}
