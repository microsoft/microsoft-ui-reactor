using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Comprehensive integration tests for the full component model:
/// hooks, state, context, memoization, and persistence working together.
/// Exercises realistic component patterns by manually driving the reconciler's
/// component lifecycle (BeginRender → Render → FlushEffects → RunCleanups)
/// through a shared ContextScope.
/// </summary>
// Shares the process-wide ApplicationPersistedScope.Default with other
// persistence tests; setup/teardown Clear() must not race another class's
// Set/TryGet against the singleton.
[Collection("PersistedStateCache")]
public class ComponentModelIntegrationTests : IDisposable
{
    private readonly ContextScope _scope = new();

    public ComponentModelIntegrationTests()
    {
        PersistedStateCache.Clear();
    }

    public void Dispose()
    {
        PersistedStateCache.Clear();
    }

    // ── Helpers ─────────────────────────────────────────────────

    private Element Mount(Component component,
        Dictionary<ContextBase, object?>? ctx = null,
        Action? onRerender = null)
    {
        if (ctx is { Count: > 0 }) _scope.Push(ctx);
        try
        {
            component.Context.BeginRender(onRerender ?? (() => { }), _scope);
            var el = component.Render();
            component.Context.FlushEffects();
            return el;
        }
        finally
        {
            if (ctx is { Count: > 0 }) _scope.Pop(ctx.Count);
        }
    }

    private Element Rerender(Component component,
        Dictionary<ContextBase, object?>? ctx = null,
        Action? onRerender = null)
        => Mount(component, ctx, onRerender);

    private void Unmount(Component component)
    {
        component.Context.RunCleanups();
    }

    // ── Contexts ────────────────────────────────────────────────

    private static readonly Context<string> ThemeCtx = new("light");
    private static readonly Context<int> CountCtx = new(0);
    private static readonly Context<string> LocaleCtx = new("en-US");

    // ═══════════════════════════════════════════════════════════
    //  Test: Component with state + context + effects
    // ═══════════════════════════════════════════════════════════

    private class DashboardComponent : Component
    {
        public int RenderCount;
        public string? Theme;
        public int Counter;
        public Action<int>? SetCounter;
        public List<string> EffectLog = new();

        public override Element Render()
        {
            RenderCount++;
            Theme = UseContext(ThemeCtx);
            var (count, set) = UseState(0);
            Counter = count;
            SetCounter = set;

            UseEffect(() =>
            {
                EffectLog.Add($"effect:{count}");
                return () => EffectLog.Add($"cleanup:{count}");
            }, count);

            return new TextBlockElement($"Dashboard: {Theme} count={count}");
        }
    }

    [Fact]
    public void Full_Lifecycle_State_Context_Effects()
    {
        var comp = new DashboardComponent();

        // Mount with theme context
        Mount(comp, new() { [ThemeCtx] = "dark" });
        Assert.Equal(1, comp.RenderCount);
        Assert.Equal("dark", comp.Theme);
        Assert.Equal(0, comp.Counter);
        Assert.Equal(new[] { "effect:0" }, comp.EffectLog);

        // State change → re-render
        comp.SetCounter!(5);
        Rerender(comp, new() { [ThemeCtx] = "dark" });
        Assert.Equal(2, comp.RenderCount);
        Assert.Equal(5, comp.Counter);
        // Cleanup from old effect runs before new effect
        Assert.Equal(new[] { "effect:0", "cleanup:0", "effect:5" }, comp.EffectLog);

        // Context change to "blue"
        Rerender(comp, new() { [ThemeCtx] = "blue" });
        Assert.Equal(3, comp.RenderCount);
        Assert.Equal("blue", comp.Theme);
        Assert.Equal(5, comp.Counter); // state preserved

        // Unmount
        Unmount(comp);
        Assert.Contains("cleanup:5", comp.EffectLog);
    }

    // ═══════════════════════════════════════════════════════════
    //  Test: Persisted state survives unmount/remount cycle
    // ═══════════════════════════════════════════════════════════

    private class TabContentComponent : Component
    {
        public int RenderCount;
        public int ScrollPos;
        public Action<int>? SetScroll;
        public int TransientCount;
        public Action<int>? SetTransient;

        public override Element Render()
        {
            RenderCount++;
            var (scroll, setScroll) = UsePersisted("tab-scroll", 0);
            var (transient, setTransient) = UseState(0);
            ScrollPos = scroll;
            SetScroll = setScroll;
            TransientCount = transient;
            SetTransient = setTransient;
            return new TextBlockElement($"Scroll: {scroll}, Transient: {transient}");
        }
    }

    [Fact]
    public void Persisted_State_Survives_Unmount_Remount()
    {
        var comp1 = new TabContentComponent();

        // Mount and update both states
        Mount(comp1);
        comp1.SetScroll!(100);
        comp1.SetTransient!(42);

        Rerender(comp1);
        Assert.Equal(100, comp1.ScrollPos);
        Assert.Equal(42, comp1.TransientCount);

        // Unmount → persisted saved, transient lost
        Unmount(comp1);

        // Remount new instance
        var comp2 = new TabContentComponent();
        Mount(comp2);
        Assert.Equal(100, comp2.ScrollPos); // persisted — restored
        Assert.Equal(0, comp2.TransientCount); // transient — fresh
    }

    // ═══════════════════════════════════════════════════════════
    //  Test: Memo + context interaction in nested components
    // ═══════════════════════════════════════════════════════════

    private class HeaderComponent : Component
    {
        public int RenderCount;
        public string? Theme;
        public override Element Render()
        {
            RenderCount++;
            Theme = UseContext(ThemeCtx);
            return new TextBlockElement($"Header: {Theme}");
        }
    }

    private class FooterComponent : Component
    {
        public int RenderCount;
        public override Element Render()
        {
            RenderCount++;
            // Doesn't consume any context
            return new TextBlockElement("Footer");
        }
    }

    [Fact]
    public void Nested_Components_Memo_With_Context_Change()
    {
        var header = new HeaderComponent();
        var footer = new FooterComponent();

        // Mount both in "dark" theme
        Mount(header, new() { [ThemeCtx] = "dark" });
        Mount(footer, new() { [ThemeCtx] = "dark" });
        Assert.Equal(1, header.RenderCount);
        Assert.Equal(1, footer.RenderCount);

        // Simulate context change to "blue"
        // Header consumes ThemeCtx → context changed → should re-render
        _scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "blue" });
        bool headerShouldRender = false;
        foreach (var h in header.Context.ContextHooks)
        {
            if (!Equals(_scope.Read(h.Context), h.LastValue))
            { headerShouldRender = true; break; }
        }
        Assert.True(headerShouldRender);

        // Footer doesn't consume any context → no context hooks → should skip
        bool footerShouldRender = false;
        foreach (var h in footer.Context.ContextHooks)
        {
            if (!Equals(_scope.Read(h.Context), h.LastValue))
            { footerShouldRender = true; break; }
        }
        Assert.False(footerShouldRender);
        Assert.False(footer.ShouldUpdate()); // propless → default skip

        _scope.Pop(1);
    }

    // ═══════════════════════════════════════════════════════════
    //  Test: Multiple contexts + memo + state together
    // ═══════════════════════════════════════════════════════════

    private class MultiContextComponent : Component
    {
        public int RenderCount;
        public string? Theme;
        public int Count;
        public string? Locale;
        public int LocalState;
        public Action<int>? SetLocal;

        public override Element Render()
        {
            RenderCount++;
            Theme = UseContext(ThemeCtx);
            Count = UseContext(CountCtx);
            Locale = UseContext(LocaleCtx);
            var (local, set) = UseState(0);
            LocalState = local;
            SetLocal = set;
            return new TextBlockElement($"{Theme}/{Count}/{Locale} local={local}");
        }
    }

    [Fact]
    public void Multiple_Contexts_And_State_Coexist()
    {
        var comp = new MultiContextComponent();
        var ctxValues = new Dictionary<ContextBase, object?>
        {
            [ThemeCtx] = "dark",
            [CountCtx] = 5,
            [LocaleCtx] = "fr-FR",
        };

        Mount(comp, ctxValues);
        Assert.Equal(1, comp.RenderCount);
        Assert.Equal("dark", comp.Theme);
        Assert.Equal(5, comp.Count);
        Assert.Equal("fr-FR", comp.Locale);
        Assert.Equal(0, comp.LocalState);

        // Update local state
        comp.SetLocal!(10);
        Rerender(comp, ctxValues);
        Assert.Equal(2, comp.RenderCount);
        Assert.Equal(10, comp.LocalState);
        Assert.Equal("dark", comp.Theme); // context unchanged

        // Change one context
        var newCtxValues = new Dictionary<ContextBase, object?>
        {
            [ThemeCtx] = "dark",
            [CountCtx] = 99, // changed
            [LocaleCtx] = "fr-FR",
        };
        Rerender(comp, newCtxValues);
        Assert.Equal(3, comp.RenderCount);
        Assert.Equal(99, comp.Count);
        Assert.Equal(10, comp.LocalState); // state preserved
    }

    // ═══════════════════════════════════════════════════════════
    //  Test: Memo with record props using Component<TProps>
    // ═══════════════════════════════════════════════════════════

    private record UserCardProps(string Name, string Role);

    private class UserCardComponent : Component<UserCardProps>
    {
        public int RenderCount;
        public override Element Render()
        {
            RenderCount++;
            return new TextBlockElement($"{Props.Name} ({Props.Role})");
        }
    }

    [Fact]
    public void Record_Props_Memo_Full_Lifecycle()
    {
        var comp = new UserCardComponent();
        var props1 = new UserCardProps("Alice", "Admin");
        ((IPropsReceiver)comp).SetProps(props1);

        Mount(comp);
        Assert.Equal(1, comp.RenderCount);

        // Same props (structurally equal) → ShouldUpdate returns false
        var props2 = new UserCardProps("Alice", "Admin");
        Assert.False(comp.ShouldUpdate(props1, props2));

        // Changed props → ShouldUpdate returns true
        var props3 = new UserCardProps("Bob", "User");
        Assert.True(comp.ShouldUpdate(props1, props3));
        ((IPropsReceiver)comp).SetProps(props3);
        Rerender(comp);
        Assert.Equal(2, comp.RenderCount);
    }

    // ═══════════════════════════════════════════════════════════
    //  Test: Effect ordering with persisted state
    // ═══════════════════════════════════════════════════════════

    private class EffectOrderComponent : Component
    {
        public List<string> Log = new();
        public Action<int>? SetPersisted;

        public override Element Render()
        {
            var (val, set) = UsePersisted("effect-order-key", 0);
            SetPersisted = set;

            UseEffect(() =>
            {
                Log.Add($"mount-effect:{val}");
                return () => Log.Add($"cleanup-effect:{val}");
            }, val);

            return new TextBlockElement($"val={val}");
        }
    }

    [Fact]
    public void Effect_And_Persisted_State_Ordering()
    {
        var comp = new EffectOrderComponent();
        Mount(comp);
        Assert.Equal(new[] { "mount-effect:0" }, comp.Log);

        // Update persisted state
        comp.SetPersisted!(5);
        Rerender(comp);
        Assert.Equal(new[] { "mount-effect:0", "cleanup-effect:0", "mount-effect:5" }, comp.Log);

        // Unmount: cleanup runs, then persisted state saved
        Unmount(comp);
        Assert.Contains("cleanup-effect:5", comp.Log);

        // Verify persisted state was saved
        Assert.True(PersistedStateCache.TryGet<int>("effect-order-key", out var cached));
        Assert.Equal(5, cached);
    }

    // ═══════════════════════════════════════════════════════════
    //  Test: UseRef + UseCallback + UseMemo combo
    // ═══════════════════════════════════════════════════════════

    private class ComplexHooksComponent : Component
    {
        public int RenderCount;
        public int MemoValue;
        public Ref<int>? CountRef;
        public Action? StableCallback;

        public override Element Render()
        {
            RenderCount++;
            CountRef = UseRef(0);
            MemoValue = UseMemo(() => CountRef.Current * 2, CountRef.Current);
            StableCallback = UseCallback(() => CountRef.Current++, CountRef.Current);
            return new TextBlockElement($"memo={MemoValue} ref={CountRef.Current}");
        }
    }

    [Fact]
    public void UseRef_UseMemo_UseCallback_Combo()
    {
        var comp = new ComplexHooksComponent();
        Mount(comp);

        Assert.Equal(1, comp.RenderCount);
        Assert.Equal(0, comp.MemoValue);
        Assert.Equal(0, comp.CountRef!.Current);
        Assert.NotNull(comp.StableCallback);

        // Mutate ref (doesn't trigger re-render)
        comp.CountRef.Current = 5;

        // Re-render — UseMemo deps changed (countRef.Current changed)
        Rerender(comp);
        Assert.Equal(2, comp.RenderCount);
        Assert.Equal(10, comp.MemoValue); // 5 * 2

        // Re-render with same ref value — UseMemo cached
        Rerender(comp);
        Assert.Equal(3, comp.RenderCount);
        Assert.Equal(10, comp.MemoValue); // cached
    }

    // ═══════════════════════════════════════════════════════════
    //  Test: UseReducer with TState/TAction pattern
    // ═══════════════════════════════════════════════════════════

    private record TodoState(string[] Items, int NextId);
    private record TodoAction(string Type, string? Text = null);

    private class TodoComponent : Component
    {
        public TodoState? State;
        public Action<TodoAction>? Dispatch;
        public int RenderCount;

        public override Element Render()
        {
            RenderCount++;
            static TodoState reducer(TodoState state, TodoAction action) => action.Type switch
            {
                "add" => state with { Items = [.. state.Items, action.Text!], NextId = state.NextId + 1 },
                "clear" => state with { Items = [] },
                _ => state,
            };

            var (state, dispatch) = UseReducer<TodoState, TodoAction>(reducer, new TodoState([], 0));
            State = state;
            Dispatch = dispatch;
            return new TextBlockElement($"Items: {state.Items.Length}");
        }
    }

    [Fact]
    public void UseReducer_TState_TAction_Full_Flow()
    {
        var comp = new TodoComponent();
        Mount(comp);
        Assert.Equal(1, comp.RenderCount);
        Assert.Empty(comp.State!.Items);

        comp.Dispatch!(new TodoAction("add", "Buy milk"));
        Rerender(comp);
        Assert.Equal(2, comp.RenderCount);
        Assert.Single(comp.State!.Items);
        Assert.Equal("Buy milk", comp.State.Items[0]);

        comp.Dispatch!(new TodoAction("add", "Walk dog"));
        Rerender(comp);
        Assert.Equal(2, comp.State.Items.Length);

        comp.Dispatch!(new TodoAction("clear"));
        Rerender(comp);
        Assert.Empty(comp.State.Items);
    }

    // ═══════════════════════════════════════════════════════════
    //  Test: Context shadowing through nested component hierarchy
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Context_Shadowing_Through_Component_Hierarchy()
    {
        var outer = new HeaderComponent();
        var inner = new HeaderComponent();

        // Outer sees "dark"
        _scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "dark" });
        Mount(outer);
        Assert.Equal("dark", outer.Theme);

        // Inner sees "high-contrast" (shadows outer)
        _scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "high-contrast" });
        Mount(inner);
        Assert.Equal("high-contrast", inner.Theme);

        _scope.Pop(1);

        // After pop, outer still sees "dark"
        Rerender(outer);
        Assert.Equal("dark", outer.Theme);

        _scope.Pop(1);
    }

    // ═══════════════════════════════════════════════════════════
    //  Test: Function component (RenderContext) with all hooks
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void FuncComponent_Full_Hook_Suite()
    {
        var ctx = new RenderContext();
        int renderCount = 0;
        var events = new List<string>();

        void render()
        {
            renderCount++;
            var (count, set) = ctx.UseState(0);
            var doubled = ctx.UseMemo(() => count * 2, count);
            var countRef = ctx.UseRef(0);
            var cb = ctx.UseCallback(() => { }, count);
            var theme = ctx.UseContext(ThemeCtx);
            var (persisted, setPersisted) = ctx.UsePersisted("func-persist", "init");

            ctx.UseEffect(() =>
            {
                events.Add($"effect:{count}");
                return () => events.Add($"cleanup:{count}");
            }, count);
        }

        _scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "dark" });

        ctx.BeginRender(() => { }, _scope);
        render();
        ctx.FlushEffects();
        Assert.Equal(1, renderCount);
        Assert.Equal(new[] { "effect:0" }, events);

        // Second render
        ctx.BeginRender(() => { }, _scope);
        render();
        ctx.FlushEffects();
        Assert.Equal(2, renderCount);
        // Same deps → no cleanup/re-effect
        Assert.Equal(new[] { "effect:0" }, events);

        _scope.Pop(1);
    }

    // ═══════════════════════════════════════════════════════════
    //  Test: Multiple effects with independent deps
    // ═══════════════════════════════════════════════════════════

    private class MultiEffectComponent : Component
    {
        public int RenderCount;
        public List<string> Log = new();
        public Action<int>? SetA;
        public Action<string>? SetB;

        public override Element Render()
        {
            RenderCount++;
            var (a, setA) = UseState(0);
            var (b, setB) = UseState("x");
            SetA = setA;
            SetB = setB;

            UseEffect(() =>
            {
                Log.Add($"effectA:{a}");
                return () => Log.Add($"cleanupA:{a}");
            }, a);

            UseEffect(() =>
            {
                Log.Add($"effectB:{b}");
                return () => Log.Add($"cleanupB:{b}");
            }, b);

            return new TextBlockElement($"a={a} b={b}");
        }
    }

    [Fact]
    public void Multiple_Effects_Independent_Deps()
    {
        var comp = new MultiEffectComponent();
        Mount(comp);
        Assert.Equal(new[] { "effectA:0", "effectB:x" }, comp.Log);

        // Change only A
        comp.SetA!(1);
        comp.Log.Clear();
        Rerender(comp);
        // Only A's cleanup+effect should fire; B unchanged
        Assert.Equal(new[] { "cleanupA:0", "effectA:1" }, comp.Log);

        // Change only B
        comp.SetB!("y");
        comp.Log.Clear();
        Rerender(comp);
        Assert.Equal(new[] { "cleanupB:x", "effectB:y" }, comp.Log);

        // Change both
        comp.SetA!(2);
        comp.SetB!("z");
        comp.Log.Clear();
        Rerender(comp);
        Assert.Equal(new[] { "cleanupA:1", "cleanupB:y", "effectA:2", "effectB:z" }, comp.Log);
    }

    // ═══════════════════════════════════════════════════════════
    //  Test: Context default value when no provider
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Context_Default_Value_When_No_Provider()
    {
        var comp = new MultiContextComponent();
        Mount(comp); // no context pushed

        Assert.Equal("light", comp.Theme); // ThemeCtx default
        Assert.Equal(0, comp.Count); // CountCtx default
        Assert.Equal("en-US", comp.Locale); // LocaleCtx default
    }

    // ═══════════════════════════════════════════════════════════
    //  Test: Provide modifier preserves element type
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void Provide_Modifier_Preserves_Element_Type()
    {
        var text = new TextBlockElement("hello")
            .Provide(ThemeCtx, "dark")
            .Provide(CountCtx, 5);

        Assert.IsType<TextBlockElement>(text);
        Assert.Equal("hello", text.Content);
        Assert.NotNull(text.ContextValues);
        Assert.Equal(2, text.ContextValues.Count);
    }

    // ═══════════════════════════════════════════════════════════
    //  Test: ShouldUpdate with null props edge cases
    // ═══════════════════════════════════════════════════════════

    private record NullableProps(string? Name);

    private class NullablePropsComponent : Component<NullableProps>
    {
        public int RenderCount;
        public override Element Render()
        {
            RenderCount++;
            return new TextBlockElement(Props?.Name ?? "null");
        }
    }

    [Fact]
    public void ShouldUpdate_Null_Props_Edge_Cases()
    {
        var comp = new NullablePropsComponent();

        // null to null → no change
        Assert.False(comp.ShouldUpdate(null, null));

        // null to non-null → changed
        Assert.True(comp.ShouldUpdate(null, new NullableProps("Alice")));

        // non-null to null → changed
        Assert.True(comp.ShouldUpdate(new NullableProps("Alice"), null));

        // Same record → no change
        var p = new NullableProps("Bob");
        Assert.False(comp.ShouldUpdate(p, new NullableProps("Bob")));

        // Different → changed
        Assert.True(comp.ShouldUpdate(new NullableProps("A"), new NullableProps("B")));
    }

    // ═══════════════════════════════════════════════════════════
    //  Test: MemoElement DSL factory
    // ═══════════════════════════════════════════════════════════

    [Fact]
    public void MemoElement_Record_Fields()
    {
        // No deps → null
        var m1 = new MemoElement(ctx => new TextBlockElement("hi"));
        Assert.Null(m1.Dependencies);

        // With deps
        var m2 = new MemoElement(ctx => new TextBlockElement("hi"), new object?[] { "a", 1 });
        Assert.NotNull(m2.Dependencies);
        Assert.Equal(2, m2.Dependencies!.Length);
    }
}
