// id: master-detail
// intent: master-detail layout with list selection
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property UseWinUI=true
#:property WindowsPackageType=None

using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Master Detail", width: 500, height: 400);

record Topic(string Id, string Title, string Detail);

class App : Component
{
    public override Element Render()
    {
        var topics = new[]
        {
            new Topic("layout", "Layout", "Use stacks to compose responsive UI."),
            new Topic("state", "State", "Hooks keep selection and local state in sync."),
            new Topic("lists", "Lists", "ForEach renders rows while state tracks selection."),
        };
        var (selectedId, setSelectedId) = UseState(topics[0].Id);
        var selected = topics.First(topic => topic.Id == selectedId);

        return HStack(16,
            VStack(8,
                Heading("Topics"),
                ForEach(topics, topic =>
                    Button(topic.Id == selectedId ? $"> {topic.Title}" : topic.Title, () => setSelectedId(topic.Id))
                        .Width(180)
                        .WithKey(topic.Id))),
            Border(
                VStack(8,
                    Heading(selected.Title),
                    TextBlock(selected.Detail),
                    TextBlock($"Selected: {selected.Id}").Opacity(0.7))
            ).Padding(12))
            .Padding(16);
    }
}
