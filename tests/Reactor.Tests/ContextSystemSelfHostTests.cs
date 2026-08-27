using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Phase 2 self-host tests: Exercise full component lifecycle with context push/pop.
/// Simulates the reconciler's component rendering flow by manually driving
/// BeginRender → Render → FlushEffects with ContextScope, avoiding WinUI control creation.
/// </summary>
public class ContextSystemSelfHostTests
{
    // ── Test contexts ─────────────────────────────────────────────

    private static readonly Context<string> ThemeCtx = new("light");
    private static readonly Context<int> CounterCtx = new(0);

    // ════════════════════════════════════════════════════════════════
    //  Helper: simulate reconciler component rendering
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Simulates the reconciler mounting a component in a context scope.
    /// Pushes context values, calls BeginRender, Render, FlushEffects, then pops.
    /// </summary>
    // <snippet:component-mount-helper>
    private static Element MountComponent(
        Component component, ContextScope scope,
        Dictionary<ContextBase, object?>? contextValues = null)
    {
        if (contextValues is { Count: > 0 })
            scope.Push(contextValues);

        try
        {
            component.Context.BeginRender(() => { }, scope);
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
    // </snippet:component-mount-helper>

    /// <summary>
    /// Simulates re-rendering a component (update path) with optional new context.
    /// </summary>
    private static Element RerenderComponent(
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
    /// Simulates a function component render with context.
    /// </summary>
    private static Element RenderFunc(
        RenderContext ctx, Func<RenderContext, Element> renderFunc, ContextScope scope,
        Dictionary<ContextBase, object?>? contextValues = null)
    {
        if (contextValues is { Count: > 0 })
            scope.Push(contextValues);

        try
        {
            ctx.BeginRender(() => { }, scope);
            var element = renderFunc(ctx);
            ctx.FlushEffects();
            return element;
        }
        finally
        {
            if (contextValues is { Count: > 0 })
                scope.Pop(contextValues.Count);
        }
    }

    // ── Test components ───────────────────────────────────────────

    private class ThemeReaderComponent : Component
    {
        public string? LastValue;
        public int RenderCount;

        public override Element Render()
        {
            RenderCount++;
            var theme = UseContext(ThemeCtx);
            LastValue = theme;
            return new TextBlockElement(theme);
        }
    }

    private class CounterReaderComponent : Component
    {
        public int LastValue;

        public override Element Render()
        {
            var count = UseContext(CounterCtx);
            LastValue = count;
            return new TextBlockElement($"Count: {count}");
        }
    }

    private class DualReaderComponent : Component
    {
        public string? LastTheme;
        public int LastCounter;

        public override Element Render()
        {
            LastTheme = UseContext(ThemeCtx);
            LastCounter = UseContext(CounterCtx);
            return new TextBlockElement($"{LastTheme}:{LastCounter}");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Mount: .Provide() → child UseContext returns provided value
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Mount_With_Provide_Child_Component_Reads_Provided_Value()
    {
        var scope = new ContextScope();
        var reader = new ThemeReaderComponent();

        MountComponent(reader, scope, new() { [ThemeCtx] = "dark" });

        Assert.Equal("dark", reader.LastValue);
        Assert.Equal(1, reader.RenderCount);
    }

    // ════════════════════════════════════════════════════════════════
    //  No provider → UseContext returns default
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void No_Provider_UseContext_Returns_Default()
    {
        var scope = new ContextScope();
        var reader = new ThemeReaderComponent();

        MountComponent(reader, scope);

        Assert.Equal("light", reader.LastValue); // Context default
    }

    // ════════════════════════════════════════════════════════════════
    //  Nested providers: inner shadows outer
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Nested_Providers_Inner_Shadows_Outer()
    {
        var scope = new ContextScope();
        var reader = new ThemeReaderComponent();

        // Simulate outer provider
        scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "dark" });
        // Simulate inner provider (shadows outer)
        MountComponent(reader, scope, new() { [ThemeCtx] = "high-contrast" });

        Assert.Equal("high-contrast", reader.LastValue);

        scope.Pop(1); // cleanup outer
    }

    [Fact]
    public void Nested_Providers_Outer_Component_Sees_Outer_Value()
    {
        var scope = new ContextScope();
        var outerReader = new ThemeReaderComponent();
        var innerReader = new ThemeReaderComponent();

        // Simulate: outer provides "dark", inner provides "high-contrast"
        scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "dark" });

        // Outer component reads in the "dark" scope
        outerReader.Context.BeginRender(() => { }, scope);
        outerReader.Render();
        outerReader.Context.FlushEffects();

        // Inner component reads in "high-contrast" scope
        MountComponent(innerReader, scope, new() { [ThemeCtx] = "high-contrast" });

        Assert.Equal("dark", outerReader.LastValue);
        Assert.Equal("high-contrast", innerReader.LastValue);

        scope.Pop(1);
    }

    // ════════════════════════════════════════════════════════════════
    //  Different contexts both readable
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Multiple_Contexts_Both_Readable()
    {
        var scope = new ContextScope();
        var reader = new DualReaderComponent();

        var ctxValues = new Dictionary<ContextBase, object?>
        {
            [ThemeCtx] = "dark",
            [CounterCtx] = 42,
        };
        MountComponent(reader, scope, ctxValues);

        Assert.Equal("dark", reader.LastTheme);
        Assert.Equal(42, reader.LastCounter);
    }

    // ════════════════════════════════════════════════════════════════
    //  Context scope cleanup: sibling subtrees independent
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Context_Scope_Cleanup_Sibling_Subtree_Does_Not_See_Adjacent_Context()
    {
        var scope = new ContextScope();
        var reader1 = new ThemeReaderComponent();
        var reader2 = new ThemeReaderComponent();

        // First sibling subtree: provides "dark"
        MountComponent(reader1, scope, new() { [ThemeCtx] = "dark" });

        // Second sibling subtree: no provider — context was popped
        MountComponent(reader2, scope);

        Assert.Equal("dark", reader1.LastValue);
        Assert.Equal("light", reader2.LastValue); // default — "dark" was popped
    }

    // ════════════════════════════════════════════════════════════════
    //  Context value change triggers consumer re-render
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Context_Value_Change_Component_Sees_New_Value()
    {
        var scope = new ContextScope();
        var reader = new ThemeReaderComponent();

        // Initial mount with "dark"
        MountComponent(reader, scope, new() { [ThemeCtx] = "dark" });
        Assert.Equal("dark", reader.LastValue);
        Assert.Equal(1, reader.RenderCount);

        // Re-render with "blue"
        RerenderComponent(reader, scope, contextValues: new() { [ThemeCtx] = "blue" });
        Assert.Equal("blue", reader.LastValue);
        Assert.Equal(2, reader.RenderCount);
    }

    // ════════════════════════════════════════════════════════════════
    //  Deep nesting: context passes through intermediates
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Deep_Nesting_Context_Passes_Through_Intermediate_Components()
    {
        var scope = new ContextScope();
        var reader = new ThemeReaderComponent();

        // Simulate 5 levels of nesting — all intermediate levels just push/pop
        // The context should still be visible at the deepest level
        scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "deep-dark" });

        // No additional push/pop at intermediate levels (they don't provide anything)
        MountComponent(reader, scope);

        Assert.Equal("deep-dark", reader.LastValue);
        scope.Pop(1);
    }

    // ════════════════════════════════════════════════════════════════
    //  Two components sharing a context
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Two_Components_Sharing_Context_Both_See_Same_Value()
    {
        var scope = new ContextScope();
        var reader1 = new ThemeReaderComponent();
        var reader2 = new ThemeReaderComponent();

        scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "shared" });

        MountComponent(reader1, scope);
        MountComponent(reader2, scope);

        Assert.Equal("shared", reader1.LastValue);
        Assert.Equal("shared", reader2.LastValue);

        scope.Pop(1);
    }

    // ════════════════════════════════════════════════════════════════
    //  FuncElement (function component) with UseContext
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void FuncElement_Reads_Context_Value()
    {
        var scope = new ContextScope();
        var ctx = new RenderContext();
        string? readValue = null;

        RenderFunc(ctx, c =>
        {
            readValue = c.UseContext(ThemeCtx);
            return new TextBlockElement(readValue);
        }, scope, new() { [ThemeCtx] = "func-dark" });

        Assert.Equal("func-dark", readValue);
    }

    // ════════════════════════════════════════════════════════════════
    //  UseContext with state interaction
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseContext_Coexists_With_UseState()
    {
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "dark" });

        var ctx = new RenderContext();
        string? theme = null;
        int stateValue = 0;
        Action<int>? setState = null;

        // First render
        ctx.BeginRender(() => { }, scope);
        (stateValue, setState) = ctx.UseState(0);
        theme = ctx.UseContext(ThemeCtx);
        ctx.FlushEffects();

        Assert.Equal("dark", theme);
        Assert.Equal(0, stateValue);

        // Mutate state
        setState!(42);

        // Re-render
        ctx.BeginRender(() => { }, scope);
        (stateValue, _) = ctx.UseState(0);
        theme = ctx.UseContext(ThemeCtx);
        ctx.FlushEffects();

        Assert.Equal("dark", theme);
        Assert.Equal(42, stateValue);

        scope.Pop(1);
    }

    // ════════════════════════════════════════════════════════════════
    //  Context hook stores LastValue for memo detection
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseContext_Stores_LastValue_For_Memo_Detection()
    {
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "dark" });

        var ctx = new RenderContext();
        ctx.BeginRender(() => { }, scope);
        ctx.UseContext(ThemeCtx);

        var hooks = ctx.ContextHooks.ToList();
        Assert.Single(hooks);
        Assert.Equal("dark", hooks[0].LastValue);

        scope.Pop(1);

        // Re-render with different value
        scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "blue" });
        ctx.BeginRender(() => { }, scope);
        ctx.UseContext(ThemeCtx);

        hooks = ctx.ContextHooks.ToList();
        Assert.Equal("blue", hooks[0].LastValue);

        scope.Pop(1);
    }
}
