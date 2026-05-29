// id: use-reducer-typed
// intent: strongly-typed reducer actions with pattern matching
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Typed action records make reducer flows explicit and easy to extend.
ReactorApp.Run<App>("UseReducerTyped", width: 400, height: 200);

class App : Component
{
    abstract record CounterAction;
    record Increment(int Amount) : CounterAction;
    record Decrement(int Amount) : CounterAction;
    record Reset() : CounterAction;
    record SetValue(int Value) : CounterAction;

    static int Reduce(int state, CounterAction action) => action switch
    {
        Increment(var amount) => state + amount,
        Decrement(var amount) => state - amount,
        Reset => 0,
        SetValue(var value) => value,
        _ => state
    };

    public override Element Render()
    {
        var (count, dispatch) = UseReducer<int, CounterAction>(Reduce, 0);

        return VStack(12,
            Heading($"Count: {count}"),
            HStack(8,
                Button("+1", () => dispatch(new Increment(1))),
                Button("-1", () => dispatch(new Decrement(1))),
                Button("Set 10", () => dispatch(new SetValue(10))),
                Button("Reset", () => dispatch(new Reset()))));
    }
}

