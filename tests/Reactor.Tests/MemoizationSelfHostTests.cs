using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Phase 3 self-host tests: Memoization logic exercised by manually driving
/// the component lifecycle (BeginRender → Render → FlushEffects) through
/// a ContextScope, simulating the reconciler's component flow.
/// Same pattern as ContextSystemSelfHostTests.
/// </summary>
public class MemoizationSelfHostTests
{
    // ── Test contexts ─────────────────────────────────────────────

    private static readonly Context<string> ThemeCtx = new("light");
    private static readonly Context<int> CounterCtx = new(0);

    // ── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Simulates the reconciler's memo check for class components.
    /// Returns true if the component should be re-rendered.
    /// </summary>
    private static bool ShouldRender(
        Component component,
        object? oldProps,
        object? newProps,
        ContextScope scope,
        bool selfTriggered)
    {
        if (selfTriggered) return true;

        // Check props via ShouldUpdate
        bool propsChanged;
        if (component is IPropsReceiver)
        {
            propsChanged = ShouldUpdateWithProps(component, oldProps, newProps);
        }
        else
        {
            propsChanged = component.ShouldUpdate();
        }

        // Check context changes
        bool contextChanged = false;
        foreach (var ctxHook in component.Context.ContextHooks)
        {
            var currentValue = scope.Read(ctxHook.Context);
            if (!Equals(currentValue, ctxHook.LastValue))
            {
                contextChanged = true;
                break;
            }
        }

        return propsChanged || contextChanged;
    }

    /// <summary>
    /// Calls ShouldUpdate(oldProps, newProps) on a Component&lt;TProps&gt; via reflection.
    /// </summary>
    /// <remarks>
    /// NOTE: Product code does NOT use reflection here. Reconciler.ShouldUpdateWithProps
    /// dispatches via the IPropsComparable interface (see Component.cs and Reconciler.cs).
    /// This test-only mirror keeps the harness independent of the production interface so
    /// it can validate ShouldUpdate(TProps?, TProps?) against arbitrary Component&lt;TProps&gt;
    /// subclasses without re-implementing the dispatch path. Do not infer from this code
    /// that the product reconciler reflects on every reconcile — it does not.
    /// </remarks>
    private static bool ShouldUpdateWithProps(Component component, object? oldProps, object? newProps)
    {
        var compType = component.GetType();
        var baseType = compType.BaseType;
        while (baseType is not null && !baseType.IsGenericType)
            baseType = baseType.BaseType;

        if (baseType is not null && baseType.GetGenericTypeDefinition() == typeof(Component<>))
        {
            var method = compType.GetMethod("ShouldUpdate",
                global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Public,
                null, new[] { baseType.GetGenericArguments()[0], baseType.GetGenericArguments()[0] }, null);
            if (method is not null)
                return (bool)method.Invoke(component, new[] { oldProps, newProps })!;
        }
        return true;
    }

    /// <summary>
    /// Mount a component: set props, BeginRender, Render, FlushEffects.
    /// </summary>
    private static Element MountComponent(
        Component component, ContextScope scope, Action? requestRerender = null,
        Dictionary<ContextBase, object?>? contextValues = null)
    {
        if (contextValues is { Count: > 0 })
            scope.Push(contextValues);

        try
        {
            component.Context.BeginRender(requestRerender ?? (() => { }), scope);
            var element = component.Render();
            component.Context.FlushEffects();
            return element;
        }
        finally
        {
            if (contextValues is { Count: > 0 })
                scope.Pop(contextValues.Count);
        }
    }

    /// <summary>
    /// Simulates a MemoElement memo check.
    /// Returns true if deps changed or context changed.
    /// </summary>
    private static bool MemoShouldRender(
        object?[]? oldDeps, object?[]? newDeps,
        RenderContext renderCtx, ContextScope scope,
        bool selfTriggered)
    {
        if (selfTriggered) return true;

        bool depsChanged;
        if (oldDeps is null && newDeps is null)
            depsChanged = false; // both null = render once
        else if (oldDeps is null || newDeps is null)
            depsChanged = true;
        else
            depsChanged = !DepsEqual(oldDeps, newDeps);

        bool contextChanged = false;
        foreach (var ctxHook in renderCtx.ContextHooks)
        {
            var currentValue = scope.Read(ctxHook.Context);
            if (!Equals(currentValue, ctxHook.LastValue))
            {
                contextChanged = true;
                break;
            }
        }

        return depsChanged || contextChanged;
    }

    private static bool DepsEqual(object?[] prev, object?[] next)
    {
        if (prev.Length != next.Length) return false;
        for (int i = 0; i < prev.Length; i++)
        {
            if (!Equals(prev[i], next[i])) return false;
        }
        return true;
    }

    // ── Test components ──────────────────────────────────────────

    private class ProplessComponent : Component
    {
        public int RenderCount;
        public override Element Render()
        {
            RenderCount++;
            return new TextBlockElement($"rendered {RenderCount}");
        }
    }

    private record GreetingProps(string Name, int Age);

    private class GreetingComponent : Component<GreetingProps>
    {
        public int RenderCount;
        public override Element Render()
        {
            RenderCount++;
            return new TextBlockElement($"Hello, {Props.Name} ({Props.Age})");
        }
    }

    private class ClassPropsData
    {
        public string Name { get; set; } = "";
    }

    private class ClassPropsComponent : Component<ClassPropsData>
    {
        public int RenderCount;
        public override Element Render()
        {
            RenderCount++;
            return new TextBlockElement($"Hello, {Props.Name}");
        }
    }

    private class AlwaysUpdateComponent : Component<GreetingProps>
    {
        public int RenderCount;
        protected internal override bool ShouldUpdate(GreetingProps? oldProps, GreetingProps? newProps) => true;
        public override Element Render()
        {
            RenderCount++;
            return new TextBlockElement($"Always: {Props.Name}");
        }
    }

    private class NameOnlyUpdateComponent : Component<GreetingProps>
    {
        public int RenderCount;
        protected internal override bool ShouldUpdate(GreetingProps? oldProps, GreetingProps? newProps)
            => oldProps?.Name != newProps?.Name;
        public override Element Render()
        {
            RenderCount++;
            return new TextBlockElement($"Name: {Props.Name}");
        }
    }

    private class ThemeConsumerComponent : Component
    {
        public int RenderCount;
        public string? LastTheme;
        public override Element Render()
        {
            RenderCount++;
            LastTheme = UseContext(ThemeCtx);
            return new TextBlockElement($"Theme: {LastTheme}");
        }
    }

    private class StatefulComponent : Component
    {
        public int RenderCount;
        public Action<int>? SetCounter;
        public override Element Render()
        {
            RenderCount++;
            var (count, set) = UseState(0);
            SetCounter = set;
            return new TextBlockElement($"Count: {count}");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Props-based memo (self-host)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ComponentElement_Null_Props_Memo_Skips_Rerender_On_Parent_Change()
    {
        var scope = new ContextScope();
        var comp = new ProplessComponent();

        // Mount
        MountComponent(comp, scope);
        Assert.Equal(1, comp.RenderCount);

        // Simulate parent re-render. Propless component → ShouldUpdate() returns false
        bool shouldRender = ShouldRender(comp, null, null, scope, selfTriggered: false);
        Assert.False(shouldRender);
        // If not shouldRender, we don't call Render — render count stays at 1
        Assert.Equal(1, comp.RenderCount);
    }

    [Fact]
    public void ComponentElement_Record_Props_Memo_Skips_When_Structurally_Equal()
    {
        var scope = new ContextScope();
        var comp = new GreetingComponent();
        var props1 = new GreetingProps("Alice", 30);
        ((IPropsReceiver)comp).SetProps(props1);

        MountComponent(comp, scope);
        Assert.Equal(1, comp.RenderCount);

        // Same props (structurally equal) → should skip
        var props2 = new GreetingProps("Alice", 30);
        bool shouldRender = ShouldRender(comp, props1, props2, scope, selfTriggered: false);
        Assert.False(shouldRender);
    }

    [Fact]
    public void ComponentElement_Changed_Props_Memo_Allows_Rerender()
    {
        var scope = new ContextScope();
        var comp = new GreetingComponent();
        var props1 = new GreetingProps("Alice", 30);
        ((IPropsReceiver)comp).SetProps(props1);

        MountComponent(comp, scope);
        Assert.Equal(1, comp.RenderCount);

        // Different props → should re-render
        var props2 = new GreetingProps("Bob", 25);
        bool shouldRender = ShouldRender(comp, props1, props2, scope, selfTriggered: false);
        Assert.True(shouldRender);

        // Actually re-render
        ((IPropsReceiver)comp).SetProps(props2);
        MountComponent(comp, scope);
        Assert.Equal(2, comp.RenderCount);
    }

    [Fact]
    public void ComponentElement_Class_Props_No_Equals_Rerenders_Every_Time()
    {
        var scope = new ContextScope();
        var comp = new ClassPropsComponent();
        var props1 = new ClassPropsData { Name = "Alice" };
        ((IPropsReceiver)comp).SetProps(props1);

        MountComponent(comp, scope);
        Assert.Equal(1, comp.RenderCount);

        // New instance with same data → reference inequality → re-render
        var props2 = new ClassPropsData { Name = "Alice" };
        bool shouldRender = ShouldRender(comp, props1, props2, scope, selfTriggered: false);
        Assert.True(shouldRender);
    }

    // ════════════════════════════════════════════════════════════════
    //  ShouldUpdate override (self-host)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ShouldUpdate_Override_Returning_True_Always_Rerenders()
    {
        var scope = new ContextScope();
        var comp = new AlwaysUpdateComponent();
        var props = new GreetingProps("Alice", 30);
        ((IPropsReceiver)comp).SetProps(props);

        MountComponent(comp, scope);
        Assert.Equal(1, comp.RenderCount);

        // Same props but ShouldUpdate always returns true
        bool shouldRender = ShouldRender(comp, props, new GreetingProps("Alice", 30), scope, selfTriggered: false);
        Assert.True(shouldRender);

        MountComponent(comp, scope);
        Assert.Equal(2, comp.RenderCount);
    }

    [Fact]
    public void ShouldUpdate_Custom_Comparison_Only_Rerenders_When_Check_Says_So()
    {
        var scope = new ContextScope();
        var comp = new NameOnlyUpdateComponent();
        var props1 = new GreetingProps("Alice", 30);
        ((IPropsReceiver)comp).SetProps(props1);

        MountComponent(comp, scope);
        Assert.Equal(1, comp.RenderCount);

        // Same name, different age → skip (custom comparison only checks name)
        var props2 = new GreetingProps("Alice", 99);
        Assert.False(ShouldRender(comp, props1, props2, scope, selfTriggered: false));

        // Different name → re-render
        var props3 = new GreetingProps("Bob", 99);
        Assert.True(ShouldRender(comp, props1, props3, scope, selfTriggered: false));
    }

    // ════════════════════════════════════════════════════════════════
    //  Self-triggered bypass (self-host)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Self_Triggered_Rerender_Bypasses_Memo()
    {
        var scope = new ContextScope();
        var comp = new StatefulComponent();

        MountComponent(comp, scope);
        Assert.Equal(1, comp.RenderCount);
        Assert.NotNull(comp.SetCounter);

        // Self-triggered → always re-render regardless of props
        comp.SetCounter!(42);
        bool shouldRender = ShouldRender(comp, null, null, scope, selfTriggered: true);
        Assert.True(shouldRender);

        // Re-render picks up state change
        var el = MountComponent(comp, scope) as TextBlockElement;
        Assert.Equal(2, comp.RenderCount);
        Assert.Equal("Count: 42", el?.Content);
    }

    // ════════════════════════════════════════════════════════════════
    //  Context + memo interaction (self-host)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Context_Change_Bypasses_Memo_When_Consumed()
    {
        var scope = new ContextScope();
        var comp = new ThemeConsumerComponent();

        // Mount with "dark" context
        MountComponent(comp, scope, contextValues: new() { [ThemeCtx] = "dark" });
        Assert.Equal(1, comp.RenderCount);
        Assert.Equal("dark", comp.LastTheme);

        // Now scope has "blue" — context changed → must re-render
        scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "blue" });
        bool shouldRender = ShouldRender(comp, null, null, scope, selfTriggered: false);
        Assert.True(shouldRender);

        // Actually re-render and verify
        comp.Context.BeginRender(() => { }, scope);
        comp.Render();
        comp.Context.FlushEffects();
        Assert.Equal(2, comp.RenderCount);
        Assert.Equal("blue", comp.LastTheme);

        scope.Pop(1);
    }

    [Fact]
    public void Context_Change_On_Non_Consumed_Context_Does_Not_Trigger_Rerender()
    {
        var scope = new ContextScope();
        var comp = new ProplessComponent();

        // Mount — this component doesn't consume any context
        MountComponent(comp, scope);
        Assert.Equal(1, comp.RenderCount);

        // Push a context that this component doesn't use
        scope.Push(new Dictionary<ContextBase, object?> { [CounterCtx] = 99 });
        bool shouldRender = ShouldRender(comp, null, null, scope, selfTriggered: false);
        Assert.False(shouldRender); // no context hooks → no context change detected
        scope.Pop(1);
    }

    // ════════════════════════════════════════════════════════════════
    //  MemoElement for function components (self-host)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void MemoElement_With_Deps_Skips_Render_When_Deps_Unchanged()
    {
        var scope = new ContextScope();
        var ctx = new RenderContext();
        int renderCount = 0;

        // Mount
        ctx.BeginRender(() => { }, scope);
        renderCount++;
        var _el = new TextBlockElement("memo");
        ctx.FlushEffects();

        // Same deps → skip
        var oldDeps = new object?[] { "dep1" };
        var newDeps = new object?[] { "dep1" };
        bool shouldRender = MemoShouldRender(oldDeps, newDeps, ctx, scope, selfTriggered: false);
        Assert.False(shouldRender);
    }

    [Fact]
    public void MemoElement_With_Changed_Deps_Rerenders()
    {
        var scope = new ContextScope();
        var ctx = new RenderContext();

        ctx.BeginRender(() => { }, scope);
        ctx.FlushEffects();

        var oldDeps = new object?[] { "dep1" };
        var newDeps = new object?[] { "dep2" };
        bool shouldRender = MemoShouldRender(oldDeps, newDeps, ctx, scope, selfTriggered: false);
        Assert.True(shouldRender);
    }

    [Fact]
    public void MemoElement_With_No_Deps_Never_Rerenders_From_Parent()
    {
        var scope = new ContextScope();
        var ctx = new RenderContext();

        ctx.BeginRender(() => { }, scope);
        ctx.FlushEffects();

        // null deps = render once, never re-render from parent
        bool shouldRender = MemoShouldRender(null, null, ctx, scope, selfTriggered: false);
        Assert.False(shouldRender);
    }

    [Fact]
    public void MemoElement_Self_Triggered_UseState_Triggers_Rerender()
    {
        var scope = new ContextScope();
        var ctx = new RenderContext();
        int renderCount = 0;
        Action<int>? setState = null;

        // Mount
        ctx.BeginRender(() => { }, scope);
        renderCount++;
        var (count, set) = ctx.UseState(0);
        setState = set;
        ctx.FlushEffects();

        Assert.Equal(1, renderCount);
        Assert.NotNull(setState);

        // Self-triggered → always re-render
        setState!(42);
        bool shouldRender = MemoShouldRender(null, null, ctx, scope, selfTriggered: true);
        Assert.True(shouldRender);

        // Actually re-render and verify state was updated
        ctx.BeginRender(() => { }, scope);
        renderCount++;
        var (count2, _) = ctx.UseState(0);
        ctx.FlushEffects();

        Assert.Equal(2, renderCount);
        Assert.Equal(42, count2);
    }

    // ════════════════════════════════════════════════════════════════
    //  Slots + memo interaction (self-host)
    // ════════════════════════════════════════════════════════════════

    private record SlotProps(Element Content);

    private class SlotComponent : Component<SlotProps>
    {
        public int RenderCount;
        public override Element Render()
        {
            RenderCount++;
            return new BorderElement(Props.Content);
        }
    }

    [Fact]
    public void Memo_And_Slots_Static_Slot_Content_Allows_Memo_Skip()
    {
        var scope = new ContextScope();
        var comp = new SlotComponent();

        var props1 = new SlotProps(new TextBlockElement("static"));
        ((IPropsReceiver)comp).SetProps(props1);
        MountComponent(comp, scope);
        Assert.Equal(1, comp.RenderCount);

        // Same static content → structurally equal (records) → skip
        var props2 = new SlotProps(new TextBlockElement("static"));
        Assert.False(ShouldRender(comp, props1, props2, scope, selfTriggered: false));
    }

    [Fact]
    public void Memo_And_Slots_Slot_With_Event_Handler_Defeats_Memo()
    {
        var scope = new ContextScope();
        var comp = new SlotComponent();

        var props1 = new SlotProps(new ButtonElement("Click", () => { }));
        ((IPropsReceiver)comp).SetProps(props1);
        MountComponent(comp, scope);
        Assert.Equal(1, comp.RenderCount);

        // New lambda → different delegate reference → not equal → re-render
        var props2 = new SlotProps(new ButtonElement("Click", () => { }));
        Assert.True(ShouldRender(comp, props1, props2, scope, selfTriggered: false));
    }

    [Fact]
    public void UseCallback_Stabilizes_Delegate_Allows_Memo_Skip()
    {
        var scope = new ContextScope();
        var comp = new SlotComponent();

        Action stableCallback = () => { };
        var props1 = new SlotProps(new ButtonElement("Click", stableCallback));
        ((IPropsReceiver)comp).SetProps(props1);
        MountComponent(comp, scope);
        Assert.Equal(1, comp.RenderCount);

        // Same delegate reference → equal → skip
        var props2 = new SlotProps(new ButtonElement("Click", stableCallback));
        Assert.False(ShouldRender(comp, props1, props2, scope, selfTriggered: false));
    }

    // ════════════════════════════════════════════════════════════════
    //  Tree-level behavior (self-host)
    // ════════════════════════════════════════════════════════════════

    private class MiddleComponent : Component
    {
        public int RenderCount;
        public override Element Render()
        {
            RenderCount++;
            return new ComponentElement(typeof(GrandchildComponent));
        }
    }

    private class GrandchildComponent : Component
    {
        public int RenderCount;
        public override Element Render()
        {
            RenderCount++;
            return new TextBlockElement("grandchild");
        }
    }

    [Fact]
    public void Deeply_Nested_Memo_Parent_Rerenders_Memoized_Child_And_Grandchild_Skip()
    {
        var scope = new ContextScope();
        var middle = new MiddleComponent();
        var grandchild = new GrandchildComponent();

        // Mount both
        MountComponent(middle, scope);
        MountComponent(grandchild, scope);
        Assert.Equal(1, middle.RenderCount);
        Assert.Equal(1, grandchild.RenderCount);

        // Simulate parent re-render: middle has no props → ShouldUpdate returns false
        bool middleShouldRender = ShouldRender(middle, null, null, scope, selfTriggered: false);
        Assert.False(middleShouldRender);
        // If middle skips, grandchild is never even evaluated → both stay at 1
        Assert.Equal(1, middle.RenderCount);
        Assert.Equal(1, grandchild.RenderCount);
    }

    [Fact]
    public void Component_Unmount_Remount_After_Memo_State_Is_Fresh()
    {
        var scope = new ContextScope();
        var comp = new StatefulComponent();

        MountComponent(comp, scope);
        Assert.Equal(1, comp.RenderCount);
        comp.SetCounter!(42);

        // Re-render to pick up the state change
        MountComponent(comp, scope);
        Assert.Equal(2, comp.RenderCount);

        // Simulate unmount
        comp.Context.RunCleanups();

        // Remount with a fresh component instance — state should be fresh
        var comp2 = new StatefulComponent();
        MountComponent(comp2, scope);
        Assert.Equal(1, comp2.RenderCount);

        // The new component's state starts at 0, not 42
        comp2.Context.BeginRender(() => { }, scope);
        var el = comp2.Render() as TextBlockElement;
        Assert.Equal("Count: 0", el?.Content);
    }

    // ════════════════════════════════════════════════════════════════
    //  Context + MemoElement interaction
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void MemoElement_Context_Change_Bypasses_Deps_Check()
    {
        var scope = new ContextScope();
        var ctx = new RenderContext();
        string? lastTheme = null;

        // Mount with context
        scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "dark" });
        ctx.BeginRender(() => { }, scope);
        lastTheme = ctx.UseContext(ThemeCtx);
        ctx.FlushEffects();
        Assert.Equal("dark", lastTheme);
        scope.Pop(1);

        // Now context changes to "blue"
        scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "blue" });
        // Even with same deps, context change → should re-render
        bool shouldRender = MemoShouldRender(
            new object?[] { "dep1" }, new object?[] { "dep1" },
            ctx, scope, selfTriggered: false);
        Assert.True(shouldRender);
        scope.Pop(1);
    }
}
