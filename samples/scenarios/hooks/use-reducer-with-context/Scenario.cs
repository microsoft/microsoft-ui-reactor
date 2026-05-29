// id: use-reducer-with-context
// intent: combine reducer state and context to model global app state
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

// Provide both state and dispatch so nested components can read and update shared state.
ReactorApp.Run<App>("UseReducerWithContext", width: 400, height: 200);

class App : Component
{
    record CounterState(int Count);
    abstract record CounterAction;
    record Increment() : CounterAction;
    record Decrement() : CounterAction;

    static readonly Context<CounterState> StateContext = new(new CounterState(0));
    static readonly Context<Action<CounterAction>> DispatchContext = new(_ => { });

    static CounterState Reduce(CounterState state, CounterAction action) => action switch
    {
        Increment => state with { Count = state.Count + 1 },
        Decrement => state with { Count = state.Count - 1 },
        _ => state
    };

    public override Element Render()
    {
        var (state, dispatch) = UseReducer<CounterState, CounterAction>(Reduce, new CounterState(0));

        return VStack(12,
            Component<CounterValue>(),
            Component<CounterButtons>())
            .Provide(StateContext, state)
            .Provide(DispatchContext, dispatch);
    }

    class CounterValue : Component
    {
        public override Element Render()
        {
            var state = UseContext(StateContext);
            return Heading($"Global count: {state.Count}");
        }
    }

    class CounterButtons : Component
    {
        public override Element Render()
        {
            var dispatch = UseContext(DispatchContext);
            return HStack(8,
                Button("-1", () => dispatch(new Decrement())),
                Button("+1", () => dispatch(new Increment())));
        }
    }
}
