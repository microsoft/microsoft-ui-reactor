using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using WinXC = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class CollectionFixtures
{
    private record Animal(string Name, string Species);

    private static readonly Animal[] Animals =
    [
        new("Rex", "Dog"),
        new("Whiskers", "Cat"),
        new("Polly", "Parrot"),
        new("Nemo", "Fish"),
        new("Buddy", "Dog"),
    ];

    internal class ListViewTyped(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
                VStack(
                    TextBlock("Animals List"),
                    ListView(Animals,
                        keySelector: a => a.Name,
                        viewBuilder: (animal, idx) =>
                            HStack(
                                TextBlock($"{idx + 1}."),
                                TextBlock(animal.Name),
                                TextBlock($"({animal.Species})")
                            )
                    ).Height(300)
                )
            );

            await Harness.Render();

            H.Check("ListView_TypedRendering_TitleVisible",
                H.FindText("Animals List") is not null);

            var listView = H.FindControl<WinXC.ListView>(_ => true);
            H.Check("ListView_TypedRendering_ListViewCreated",
                listView is not null);

            H.Check("ListView_TypedRendering_ItemsRendered",
                H.FindText("Rex") is not null || H.FindTextContaining("Rex") is not null);

            H.Check("ListView_TypedRendering_SpeciesShown",
                H.FindTextContaining("Dog") is not null);
        }
    }
}
