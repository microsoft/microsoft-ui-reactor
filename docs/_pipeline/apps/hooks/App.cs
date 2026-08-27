using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<HooksApp>("Hooks Demo", width: 650, height: 600
);

// <snippet:usestate>
class StateDemo : Component
{
    public override Element Render()
    {
        var (color, setColor) = UseState("#0078D4");
        var (size, setSize) = UseState(20.0);

        return VStack(12,
            SubHeading("UseState"),
            TextBlock("Sample text").FontSize(size).Foreground(color),
            TextBox(color, setColor, placeholderText: "#hex color")
                .AutomationName("Sample text color")
                .Width(150),
            HStack(8,
                TextBlock("Size:"),
                Slider(size, 10, 48, setSize)
                    .AutomationName("Sample text size")
                    .Width(200)
            )
        );
    }
}
// </snippet:usestate>

// <snippet:usereducer>
class ReducerDemo : Component
{
    public override Element Render()
    {
        var (items, updateItems) = UseReducer(new List<string>());
        var (input, setInput) = UseState("");

        return VStack(12,
            SubHeading("UseReducer"),
            HStack(8,
                TextBox(input, setInput, placeholderText: "Add item")
                    .AutomationName("Item text")
                    .Width(180),
                Button("Add", () =>
                {
                    if (string.IsNullOrWhiteSpace(input)) return;
                    updateItems(list =>
                        new List<string>(list) { input });
                    setInput("");
                }),
                Button("Clear", () =>
                    updateItems(_ => new List<string>()))
            ),
            ForEach(items, item => TextBlock($"  - {item}"))
        );
    }
}
// </snippet:usereducer>

// <snippet:usereducer-redux>
record CounterState(int Count, string LastAction);
abstract record CounterAction;
record Increment : CounterAction;  record Decrement : CounterAction;
record Reset : CounterAction;

class ReduxReducerDemo : Component
{
    public override Element Render()
    {
        var (state, dispatch) = UseReducer(
            (CounterState s, CounterAction a) => a switch {
                Increment => s with { Count = s.Count + 1, LastAction = "+" },
                Decrement => s with { Count = s.Count - 1, LastAction = "-" },
                Reset => new(0, "reset"), _ => s
            }, new CounterState(0, "none"));

        return VStack(8,
            SubHeading("UseReducer (Redux-style)"),
            TextBlock($"Count: {state.Count}  (last: {state.LastAction})")
                .FontSize(18).Bold(),
            HStack(8,
                Button("-", () => dispatch(new Decrement()))
                    .AutomationName("Decrement count"),
                Button("Reset", () => dispatch(new Reset())),
                Button("+", () => dispatch(new Increment()))
                    .AutomationName("Increment count")
            )
        );
    }
}
// </snippet:usereducer-redux>

// <snippet:useeffect>
class EffectDemo : Component
{
    public override Element Render()
    {
        var (seconds, updateSeconds) = UseReducer(0);
        var (running, setRunning) = UseState(false);

        UseEffect(() =>
        {
            if (!running) return () => { };
            var cts = new CancellationTokenSource();
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            var token = cts.Token;   // capture once — the loop must not re-read cts.Token
            _ = Task.Run(async () =>
            {
                try
                {
                    while (await timer.WaitForNextTickAsync(token))
                        updateSeconds(s => s + 1);
                }
                catch (OperationCanceledException) { /* expected on cleanup */ }
            });
            return () => { cts.Cancel(); timer.Dispose(); };
        }, running);

        return VStack(8,
            SubHeading("UseEffect"),
            TextBlock($"Elapsed: {seconds}s").FontSize(18),
            HStack(8,
                Button(running ? "Stop" : "Start", () => setRunning(!running))
                    .AutomationName(running ? "Stop timer" : "Start timer"),
                Button("Reset", () => updateSeconds(_ => 0))
            )
        );
    }
}
// </snippet:useeffect>

class BackgroundSetterDemo : Component
{
    // <snippet:background-setter>
    public override Element Render()
    {
        var (seconds, updateSeconds) = UseReducer(0);

        UseEffect(() =>
        {
            var cts = new CancellationTokenSource();
            var token = cts.Token;   // capture once — the loop must not re-read cts.Token
            _ = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                try
                {
                    while (await timer.WaitForNextTickAsync(token))
                        updateSeconds(s => s + 1);   // auto-marshals to the UI thread
                }
                catch (OperationCanceledException) { /* expected on cleanup */ }
            });
            return () => { cts.Cancel(); };
        });

        return TextBlock($"Elapsed: {seconds}s");
    }
    // </snippet:background-setter>
}

class ThreadSafeHooksDemo : Component
{
    public override Element Render()
    {
        // <snippet:thread-safe-hooks>
        var (count, setCount) = UseState(0, threadSafe: true);
        var (sum, addToSum) = UseReducer(0, threadSafe: true);
        // </snippet:thread-safe-hooks>

        return TextBlock($"Count: {count}, sum: {sum}");
    }
}

class HookOrderDoDemo : Component
{
    // <snippet:hook-order-do>
    public override Element Render()
    {
        var (a, setA) = UseState(0);     // always first
        var (b, setB) = UseState("");    // always second
        UseEffect(() => { /* ... */ }, a);     // always third
        return TextBlock($"{a} {b}");
    }
    // </snippet:hook-order-do>
}

class ConditionalEffectBodyDemo : Component
{
    public override Element Render()
    {
        var (a, setA) = UseState(0);

        // <snippet:conditional-effect-body>
        UseEffect(() => { if (a > 0) { /* ... */ } }, a);
        // </snippet:conditional-effect-body>

        return TextBlock($"{a}");
    }
}

// <snippet:usememo>
class MemoDemo : Component
{
    public override Element Render()
    {
        var (input, setInput) = UseState("Hello, Reactor!");

        var stats = UseMemo(() => new
        {
            Chars = input.Length,
            Words = input.Split(' ',
                StringSplitOptions.RemoveEmptyEntries).Length,
            Upper = input.ToUpperInvariant()
        }, input);

        return VStack(8,
            SubHeading("UseMemo"),
            TextBox(input, setInput)
                .AutomationName("Text to analyze")
                .Width(250),
            TextBlock($"Characters: {stats.Chars}, Words: {stats.Words}"),
            Caption($"Uppercased: {stats.Upper}")
        );
    }
}
// </snippet:usememo>

// <snippet:useref>
class RefDemo : Component
{
    public override Element Render()
    {
        var (value, setValue) = UseState("");
        var renderCount = UseRef(0);
        renderCount.Current++;

        return VStack(8,
            SubHeading("UseRef"),
            TextBlock($"Render count: {renderCount.Current}").SemiBold(),
            TextBox(value, setValue, placeholderText: "Type to trigger renders")
                .AutomationName("Render trigger text")
                .Width(250),
            Caption("UseRef persists across renders without causing them")
        );
    }
}
// </snippet:useref>

class DeferredRefDemo : Component
{
    public override Element Render()
    {
        var (current, setCurrent) = UseState(0);

        // <snippet:deferred-ref>
        var prev = UseRef<int?>(null);
        UseEffect(() => { /* compare prev.Current to current */ prev.Current = current; }, current);
        // </snippet:deferred-ref>

        return Button($"Current: {current}", () => setCurrent(current + 1))
            .AutomationName("Increment current value");
    }
}

// <snippet:usecallback>
class CallbackDemo : Component
{
    public override Element Render()
    {
        var (count, updateCount) = UseReducer(0);
        var (label, setLabel) = UseState("Click me");

        var stableIncrement = UseCallback(
            () => updateCount(c => c + 1), Array.Empty<object>());

        return VStack(8,
            SubHeading("UseCallback"),
            TextBlock($"Count: {count}").FontSize(18),
            TextBox(label, setLabel, placeholderText: "Button label")
                .AutomationName("Button label")
                .Width(200),
            Button(label, stableIncrement)
                .AutomationName("Increment count"),
            Caption("The callback identity stays stable across renders")
        );
    }
}
// </snippet:usecallback>

// <snippet:external-store>
record SessionSnapshot(string Title);

sealed class SessionStore
{
    private SessionSnapshot _snapshot = new("Untitled");

    public event Action? Changed;
    public SessionSnapshot Snapshot => _snapshot;

    public Action Subscribe(Action onChanged)
    {
        Changed += onChanged;
        return () => Changed -= onChanged;
    }

    public void Rename(string title)
    {
        _snapshot = new SessionSnapshot(title);
        Changed?.Invoke();
    }
}

class ExternalStoreDemo : Component
{
    private static readonly SessionStore _store = new();

    public override Element Render()
    {
        // `subscribe` is a method group — a stable delegate, so the effect
        // doesn't tear down and re-establish the subscription every render.
        var snapshot = UseExternalStore(
            _store.Subscribe,
            () => _store.Snapshot);

        return VStack(8,
            SubHeading("UseExternalStore"),
            TextBlock(snapshot.Title),
            Button("Rename", () => _store.Rename($"Doc {Random.Shared.Next(100)}"))
        );
    }
}
// </snippet:external-store>

// <snippet:custom-hook-debounce>
// A custom hook is a RenderContext extension method whose name starts with
// `Use`. It owns three slots — two UseState and one UseEffect — and the caller
// still gets the simple (value, setter) shape they'd get from UseState.
static class DebouncedTextHook
{
    public static (string Value, Action<string> Set) UseDebouncedText(
        this RenderContext ctx, string initial, int ms)
    {
        var (value, setValue) = ctx.UseState(initial);
        var (debounced, setDebounced) = ctx.UseState(initial);

        ctx.UseEffect(() =>
        {
            var cts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(ms, cts.Token); setDebounced(value); }
                // Expected: the cleanup below cancels this delay whenever `value`
                // changes again inside the debounce window. Cancelling is how the
                // stale result is discarded, so there is nothing to report.
                catch (OperationCanceledException) { return; }
            });
            return () => { cts.Cancel(); };
        }, value);

        return (debounced, setValue);
    }
}

class CustomHookDemo : Component
{
    public override Element Render() => Memo(ctx =>
    {
        var (debounced, setText) = ctx.UseDebouncedText("", 300);
        return VStack(8,
            SubHeading("Custom hook: UseDebouncedText"),
            TextBox(debounced, setText, placeholderText: "Type…")
                .AutomationName("Text to debounce")
                .Width(250),
            Caption($"Debounced: {debounced}")
        );
    });
}
// </snippet:custom-hook-debounce>

// <snippet:setter-chain-dont>
class SetterChainDontDemo : Component
{
    public override Element Render()
    {
        // Don't — all three calls read the same captured `count`.
        var (count, setCount) = UseState(0);
        return Button("+3", () =>
            { setCount(count + 1); setCount(count + 1); setCount(count + 1); });
    }
}
// </snippet:setter-chain-dont>

class ConditionalSubscribeFixDemo : Component
{
    public override Element Render()
    {
        var (open, setOpen) = UseState(false);

        // <snippet:conditional-subscribe-fix>
        UseEffect(() => { if (!open) return () => { }; return Subscribe(); }, open);
        // </snippet:conditional-subscribe-fix>

        return Button(open ? "Close" : "Open", () => setOpen(!open))
            .AutomationName(open ? "Close subscription demo" : "Open subscription demo");
    }

    private static Action Subscribe() => () => { };
}

// <snippet:setter-chain-do>
class SetterChainDoDemo : Component
{
    public override Element Render()
    {
        // Do — each functional update sees the previous one's result.
        var (count, updateCount) = UseReducer(0);
        return Button("+3", () =>
            { updateCount(c => c + 1); updateCount(c => c + 1); updateCount(c => c + 1); });
    }
}
// </snippet:setter-chain-do>

// <snippet:stale-read-dont>
class StaleReadDontDemo : Component
{
    static readonly string[] SizeFitNames = ["Contain", "Cover", "Fill"];

    public override Element Render()
    {
        var (sizeFitIdx, setSizeFitIdx) = UseState(0);

        // Don't — the setter only queued a re-render.
        return ComboBox(SizeFitNames, sizeFitIdx, i =>
        {
            setSizeFitIdx(i);
            Apply(sizeFitIdx); // reads the PREVIOUS index
        });

        void Apply(int index) { }
    }
}
// </snippet:stale-read-dont>

// <snippet:stale-read-do>
class StaleReadDoDemo : Component
{
    static readonly string[] SizeFitNames = ["Contain", "Cover", "Fill"];

    public override Element Render()
    {
        var (sizeFitIdx, setSizeFitIdx) = UseState(0);

        return ComboBox(SizeFitNames, sizeFitIdx, i =>
        {
            setSizeFitIdx(i);
            Apply(i); // use the new value directly
        });

        void Apply(int index) { }
    }
}
// </snippet:stale-read-do>

class HooksApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Hooks Reference"),
                Component<StateDemo>(),
                Component<ReducerDemo>(),
                Component<ReduxReducerDemo>(),
                Component<EffectDemo>(),
                Component<MemoDemo>(),
                Component<RefDemo>(),
                Component<CallbackDemo>()
            ).Padding(24)
        );
    }
}
