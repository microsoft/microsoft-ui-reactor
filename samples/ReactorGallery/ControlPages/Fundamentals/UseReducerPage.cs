using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Fundamentals;

class UseReducerPage : Component
{
    enum CartAction { Add, Remove, Clear }

    static int Reduce(int state, CartAction action) => action switch
    {
        CartAction.Add => state + 1,
        CartAction.Remove => global::System.Math.Max(0, state - 1),
        _ => 0,
    };

    public override Element Render()
    {
        var (items, updateItems) = UseReducer(new List<string>());
        var (cart, dispatch) = UseReducer<int, CartAction>(Reduce, 0);
        var (score, updateScore) = UseReducer(0);

        return ScrollView(VStack(16,
            PageHeader("UseReducer",
                "State derived from the previous value. The updater receives the current state and returns the next one, so queued updates compose instead of overwriting each other."),

            SampleCard("The hook collections need",
                VStack(8,
                    HStack(8,
                        Button("Add item", () => updateItems(list => [.. list, $"Item {list.Count + 1}"])),
                        Button("Remove last", () => updateItems(list => list.Count == 0 ? list : list.Take(list.Count - 1).ToList())),
                        Button("Clear", () => updateItems(_ => new List<string>()))),
                    TextBlock(items.Count == 0 ? "The list is empty." : string.Join(", ", items))
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (items, updateItems) = UseReducer(new List<string>());

HStack(8,
    Button(""Add item"", () => updateItems(list => [.. list, $""Item {list.Count + 1}""])),
    Button(""Clear"", () => updateItems(_ => new List<string>())))
"),

            SampleCard("Action-style reducer",
                VStack(8,
                    HStack(8,
                        Button("Add", () => dispatch(CartAction.Add)),
                        Button("Remove", () => dispatch(CartAction.Remove)),
                        Button("Empty cart", () => dispatch(CartAction.Clear))),
                    TextBlock($"Cart holds {cart} item(s).").Foreground(Theme.SecondaryText)),
                sourceCode: @"
enum CartAction { Add, Remove, Clear }

static int Reduce(int state, CartAction action) => action switch
{
    CartAction.Add => state + 1,
    CartAction.Remove => Math.Max(0, state - 1),
    _ => 0,
};

var (cart, dispatch) = UseReducer<int, CartAction>(Reduce, 0);
Button(""Add"", () => dispatch(CartAction.Add))
"),

            SampleCard("Two updates in one tick compose",
                VStack(8,
                    Button("Add two points", () =>
                    {
                        updateScore(s => s + 1);
                        updateScore(s => s + 1);
                    }),
                    TextBlock($"Score: {score}").Foreground(Theme.SecondaryText),
                    Caption("Each updater reads the value the previous one produced, so the score moves by two. The same pair written with a UseState setter would both read the captured value and move it by one.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (score, updateScore) = UseReducer(0);

Button(""Add two points"", () =>
{
    updateScore(s => s + 1);
    updateScore(s => s + 1);   // sees the value the line above produced
})
")
        ).Margin(36, 24, 36, 36));
    }
}
