// Recipe: editable list with add / toggle / delete.
//
// Pattern: UseReducer for the whole list (UseState + List<T>.Add won't trigger
// a re-render because the reference is unchanged). One reducer, immutable
// updates with `with` and collection expressions, .WithKey on every row so the
// reconciler keeps focus and animation state across reorders.

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
using Microsoft.UI.Reactor.Layout;  // FlexAlign
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("List demo", width: 480, height: 600);

record Item(string Id, string Text, bool Done);

abstract record Action;
record Add(string Text) : Action;
record Toggle(string Id) : Action;
record Delete(string Id) : Action;

static class Reducer
{
    public static IReadOnlyList<Item> Apply(IReadOnlyList<Item> s, Action a) => a switch
    {
        Add x when !string.IsNullOrWhiteSpace(x.Text)
            => [.. s, new(Guid.NewGuid().ToString(), x.Text.Trim(), false)],
        Toggle t => [.. s.Select(i => i.Id == t.Id ? i with { Done = !i.Done } : i)],
        Delete d => [.. s.Where(i => i.Id != d.Id)],
        _ => s,
    };
}

class App : Component
{
    public override Element Render()
    {
        var (items, dispatch) = UseReducer<IReadOnlyList<Item>, Action>(
            Reducer.Apply, []);
        var (draft, setDraft) = UseState("");

        var add = new Command
        {
            Label = "Add",
            Execute = () => { dispatch(new Add(draft)); setDraft(""); },
            CanExecute = !string.IsNullOrWhiteSpace(draft),
        };

        return CommandHost([add],
            VStack(12,
                HStack(8,
                    TextField(draft, setDraft, placeholder: "What needs doing?")
                        .Flex(grow: 1),
                    Button(add).Flex(shrink: 0)),

                VStack(4, items.Select(item =>
                    HStack(8,
                        CheckBox(item.Done, _ => dispatch(new Toggle(item.Id))),
                        TextBlock(item.Text).Flex(grow: 1, alignSelf: FlexAlign.Center),
                        Button("✕", () => dispatch(new Delete(item.Id))))
                    .WithKey(item.Id)
                ).ToArray<Element?>()))
            .Padding(16));
    }
}
