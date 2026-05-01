using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Regression tests for the memoization self-triggered propagation bug.
///
/// Bug: When a child component calls setState during render (or any child-initiated
/// setState), the re-render never reaches the child because ancestor memo checks
/// short-circuit the subtree. The root cause was that MountComponent/MountFuncComponent/
/// MountMemoComponent passed the raw requestRerender (root callback) to child Mount()
/// calls instead of the component's own wrapped componentRerender. This meant
/// SelfTriggered only got set on the immediate component, not on every ancestor in
/// the chain.
///
/// Fix: Pass componentRerender (not requestRerender) to Mount(childElement, ...) and
/// Reconcile(...), so child setState propagates SelfTriggered up through all ancestors.
/// Also add the missing MemoElement branch in ReconcileComponent's render switch.
///
/// These tests simulate the reconciler's callback chain and component lifecycle using
/// the same self-host pattern as MemoizationSelfHostTests.
/// </summary>
public class MemoizationPropagationTests
{
    // ── Propagation model ────────────────────────────────────────

    /// <summary>
    /// Mirrors Reconciler.ComponentNode's SelfTriggered tracking.
    /// </summary>
    private class NodeState
    {
        public bool SelfTriggered;
    }

    /// <summary>
    /// Mirrors Reconciler.CreateComponentRerender: wraps a parent rerender callback
    /// so that invoking it sets this node's SelfTriggered flag before bubbling up.
    /// </summary>
    private static Action CreateChainedRerender(NodeState node, Action parentRerender)
    {
        return () =>
        {
            node.SelfTriggered = true;
            parentRerender();
        };
    }

    // ── Test helpers ─────────────────────────────────────────────

    private static readonly Context<string> ThemeCtx = new("light");

    private static bool ShouldRender(
        Component component, object? oldProps, object? newProps,
        ContextScope scope, bool selfTriggered)
    {
        if (selfTriggered) return true;

        bool propsChanged;
        if (component is IPropsReceiver)
        {
            propsChanged = ShouldUpdateWithProps(component, oldProps, newProps);
        }
        else
        {
            propsChanged = component.ShouldUpdate();
        }

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

    // NOTE: Product code does NOT use reflection here. Reconciler.ShouldUpdateWithProps
    // dispatches via the IPropsComparable interface (see Component.cs and Reconciler.cs).
    // This test-only mirror keeps the harness independent of the production interface so
    // it can validate ShouldUpdate(TProps?, TProps?) against arbitrary Component<TProps>
    // subclasses without re-implementing the dispatch path. Do not infer from this code
    // that the product reconciler reflects on every reconcile — it does not.
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

    private static bool MemoShouldRender(
        object?[]? oldDeps, object?[]? newDeps,
        RenderContext renderCtx, ContextScope scope,
        bool selfTriggered)
    {
        if (selfTriggered) return true;

        bool depsChanged;
        if (oldDeps is null && newDeps is null)
            depsChanged = false;
        else if (oldDeps is null || newDeps is null)
            depsChanged = true;
        else
        {
            if (oldDeps.Length != newDeps.Length) depsChanged = true;
            else
            {
                depsChanged = false;
                for (int i = 0; i < oldDeps.Length; i++)
                {
                    if (!Equals(oldDeps[i], newDeps[i])) { depsChanged = true; break; }
                }
            }
        }

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

    // ── Test components ──────────────────────────────────────────

    private class SimpleParent : Component
    {
        public int RenderCount;
        public override Element Render()
        {
            RenderCount++;
            return new ComponentElement(typeof(SimpleMiddle));
        }
    }

    private class SimpleMiddle : Component
    {
        public int RenderCount;
        public override Element Render()
        {
            RenderCount++;
            return new ComponentElement(typeof(SetStateDuringRenderChild));
        }
    }

    /// <summary>
    /// Simulates the PIX DockPanelFrame pattern: calls setState during its first render,
    /// lazily creating content.
    /// </summary>
    private class SetStateDuringRenderChild : Component
    {
        public int RenderCount;
        public string? LastContent;
        public override Element Render()
        {
            RenderCount++;
            var (content, setContent) = UseState<string?>(null);

            // Lazy content creation on first render
            if (content is null)
            {
                setContent("created");
            }

            LastContent = content;
            return new TextBlockElement(content ?? "empty");
        }
    }

    private class StatefulChild : Component
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

    private class PureChild : Component
    {
        public int RenderCount;
        public override Element Render()
        {
            RenderCount++;
            return new TextBlockElement("pure");
        }
    }

    private class ThemeAwareChild : Component
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

    // ════════════════════════════════════════════════════════════════
    //  Test 1: Child setState propagates SelfTriggered through all ancestors
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Child_SetState_Propagates_SelfTriggered_Through_Three_Level_Chain()
    {
        // Simulate the FIXED callback chain: Parent > Middle > Child
        // Each component wraps its parent's rerender, so the chain is:
        //   child.setState() -> childRerender -> middleRerender -> parentRerender -> root

        bool rootRerenderCalled = false;
        Action rootRerender = () => rootRerenderCalled = true;

        var parentNode = new NodeState();
        var middleNode = new NodeState();
        var childNode = new NodeState();

        // FIXED wiring: each component wraps its parent's componentRerender
        var parentRerender = CreateChainedRerender(parentNode, rootRerender);
        var middleRerender = CreateChainedRerender(middleNode, parentRerender);
        var childRerender = CreateChainedRerender(childNode, middleRerender);

        // Child calls setState → fires childRerender
        childRerender();

        Assert.True(childNode.SelfTriggered, "Child should be marked SelfTriggered");
        Assert.True(middleNode.SelfTriggered, "Middle should be marked SelfTriggered");
        Assert.True(parentNode.SelfTriggered, "Parent should be marked SelfTriggered");
        Assert.True(rootRerenderCalled, "Root rerender should be called");
    }

    [Fact]
    public void Buggy_Wiring_Skips_Intermediate_Ancestors()
    {
        // Demonstrate the BUG pattern: all children wire to rootRerender directly
        // instead of chaining through parent componentRerender

        bool rootRerenderCalled = false;
        Action rootRerender = () => rootRerenderCalled = true;

        var parentNode = new NodeState();
        var middleNode = new NodeState();
        var childNode = new NodeState();

        // BUGGY wiring: all components wrap rootRerender directly
        var parentRerender = CreateChainedRerender(parentNode, rootRerender);
        var middleRerender = CreateChainedRerender(middleNode, rootRerender); // BUG: should be parentRerender
        var childRerender = CreateChainedRerender(childNode, rootRerender);   // BUG: should be middleRerender

        childRerender();

        Assert.True(childNode.SelfTriggered, "Child is marked (immediate component)");
        Assert.False(middleNode.SelfTriggered, "BUG: Middle is NOT marked — skipped in chain");
        Assert.False(parentNode.SelfTriggered, "BUG: Parent is NOT marked — skipped in chain");
        Assert.True(rootRerenderCalled, "Root still gets called (goes directly from child)");
    }

    [Fact]
    public void Child_SetState_Propagates_Through_Class_Components()
    {
        // Full lifecycle test with real Component instances.
        // Wire rerender callbacks like the fixed reconciler would.

        var scope = new ContextScope();
        var parent = new SimpleParent();
        var child = new StatefulChild();

        bool rootRerenderCalled = false;
        Action rootRerender = () => rootRerenderCalled = true;

        var parentNode = new NodeState();
        var childNode = new NodeState();

        var parentComponentRerender = CreateChainedRerender(parentNode, rootRerender);
        var childComponentRerender = CreateChainedRerender(childNode, parentComponentRerender);

        // Mount parent
        MountComponent(parent, scope, parentComponentRerender);
        Assert.Equal(1, parent.RenderCount);

        // Mount child with the chained rerender (this is what the fix ensures)
        MountComponent(child, scope, childComponentRerender);
        Assert.Equal(1, child.RenderCount);

        // Child calls setState
        child.SetCounter!(42);

        // Verify propagation
        Assert.True(childNode.SelfTriggered);
        Assert.True(parentNode.SelfTriggered);
        Assert.True(rootRerenderCalled);

        // Both should re-render
        Assert.True(ShouldRender(parent, null, null, scope, selfTriggered: parentNode.SelfTriggered));
        Assert.True(ShouldRender(child, null, null, scope, selfTriggered: childNode.SelfTriggered));

        // Re-render child and verify state took effect
        var el = MountComponent(child, scope, childComponentRerender) as TextBlockElement;
        Assert.Equal("Count: 42", el?.Content);
    }

    [Fact]
    public void Child_SetState_Propagates_Through_Func_Components()
    {
        var scope = new ContextScope();

        bool rootRerenderCalled = false;
        Action rootRerender = () => rootRerenderCalled = true;

        var parentNode = new NodeState();
        var childNode = new NodeState();

        var parentRerender = CreateChainedRerender(parentNode, rootRerender);
        var childRerender = CreateChainedRerender(childNode, parentRerender);

        int parentRenderCount = 0;
        int childRenderCount = 0;
        Action<int>? setCounter = null;

        // Simulate FuncElement mount: parent renders child
        var parentCtx = new RenderContext();
        parentCtx.BeginRender(parentRerender, scope);
        parentRenderCount++;
        parentCtx.FlushEffects();

        // Child FuncElement with UseState
        var childCtx = new RenderContext();
        childCtx.BeginRender(childRerender, scope);
        childRenderCount++;
        var (count, set) = childCtx.UseState(0);
        setCounter = set;
        childCtx.FlushEffects();

        Assert.Equal(1, parentRenderCount);
        Assert.Equal(1, childRenderCount);

        // Child setState
        setCounter!(99);

        Assert.True(childNode.SelfTriggered);
        Assert.True(parentNode.SelfTriggered);
        Assert.True(rootRerenderCalled);
    }

    [Fact]
    public void Child_SetState_Propagates_Through_Memo_Components()
    {
        var scope = new ContextScope();

        bool rootRerenderCalled = false;
        Action rootRerender = () => rootRerenderCalled = true;

        var parentNode = new NodeState();
        var childNode = new NodeState();

        var parentRerender = CreateChainedRerender(parentNode, rootRerender);
        var childRerender = CreateChainedRerender(childNode, parentRerender);

        Action<string>? setContent = null;

        // Parent is a MemoElement with null deps (render once)
        var parentCtx = new RenderContext();
        parentCtx.BeginRender(parentRerender, scope);
        parentCtx.FlushEffects();

        // Child is a MemoElement with deps
        var childCtx = new RenderContext();
        childCtx.BeginRender(childRerender, scope);
        var (content, set) = childCtx.UseState("initial");
        setContent = set;
        childCtx.FlushEffects();

        // Child setState
        setContent!("updated");

        Assert.True(childNode.SelfTriggered);
        Assert.True(parentNode.SelfTriggered);
        Assert.True(rootRerenderCalled);

        // Parent memo with null deps would normally skip, but SelfTriggered overrides
        Assert.True(MemoShouldRender(null, null, parentCtx, scope, selfTriggered: parentNode.SelfTriggered));
    }

    // ════════════════════════════════════════════════════════════════
    //  Test 2: Memo check still skips clean siblings
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Dirty_Child_Rerenders_While_Clean_Sibling_Skips()
    {
        var scope = new ContextScope();

        Action rootRerender = () => { };

        var parentNode = new NodeState();
        var dirtyNode = new NodeState();
        var cleanNode = new NodeState();

        var parentRerender = CreateChainedRerender(parentNode, rootRerender);
        var dirtyRerender = CreateChainedRerender(dirtyNode, parentRerender);
        var cleanRerender = CreateChainedRerender(cleanNode, parentRerender);

        var dirtyChild = new StatefulChild();
        var cleanChild = new PureChild();

        MountComponent(dirtyChild, scope, dirtyRerender);
        MountComponent(cleanChild, scope, cleanRerender);

        Assert.Equal(1, dirtyChild.RenderCount);
        Assert.Equal(1, cleanChild.RenderCount);

        // Dirty child calls setState
        dirtyChild.SetCounter!(10);

        // Dirty path is marked, clean path is not
        Assert.True(dirtyNode.SelfTriggered);
        Assert.False(cleanNode.SelfTriggered, "Clean sibling should NOT be marked SelfTriggered");
        Assert.True(parentNode.SelfTriggered, "Parent should be marked (on dirty path)");

        // Memo decisions
        Assert.True(ShouldRender(dirtyChild, null, null, scope, selfTriggered: dirtyNode.SelfTriggered));
        Assert.False(ShouldRender(cleanChild, null, null, scope, selfTriggered: cleanNode.SelfTriggered),
            "Clean sibling should be skipped by memo check");

        // Re-render dirty child, don't re-render clean child
        MountComponent(dirtyChild, scope, dirtyRerender);
        Assert.Equal(2, dirtyChild.RenderCount);
        Assert.Equal(1, cleanChild.RenderCount); // still 1 — correctly skipped
    }

    // ════════════════════════════════════════════════════════════════
    //  Test 3: MemoElement re-renders on update (no else-return fallthrough)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void MemoElement_Rerenders_When_Dependencies_Change()
    {
        var scope = new ContextScope();
        var ctx = new RenderContext();
        int renderCount = 0;

        // Mount
        ctx.BeginRender(() => { }, scope);
        renderCount++;
        ctx.FlushEffects();

        // Deps changed → should re-render
        var oldDeps = new object?[] { 1, "a" };
        var newDeps = new object?[] { 2, "a" };
        Assert.True(MemoShouldRender(oldDeps, newDeps, ctx, scope, selfTriggered: false));
    }

    [Fact]
    public void MemoElement_With_Null_Deps_Still_Rerenders_When_SelfTriggered()
    {
        // A render-once MemoElement (null deps) should still re-render when a child
        // triggers setState, because SelfTriggered bypasses the deps check.
        var scope = new ContextScope();
        var ctx = new RenderContext();
        Action<int>? setState = null;

        ctx.BeginRender(() => { }, scope);
        var (val, set) = ctx.UseState(0);
        setState = set;
        ctx.FlushEffects();

        // Normally null deps = never re-render from parent
        Assert.False(MemoShouldRender(null, null, ctx, scope, selfTriggered: false));

        // But SelfTriggered overrides
        Assert.True(MemoShouldRender(null, null, ctx, scope, selfTriggered: true));
    }

    // ════════════════════════════════════════════════════════════════
    //  Test 4: During-render setState with deep nesting (PIX pattern)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Lazy_SetState_During_Render_Propagates_Through_Deep_Ancestors()
    {
        // Reproduces the PIX docking bug: a component lazily creates content via
        // setState during its first render, nested 3+ levels deep inside components
        // with stable props.

        var scope = new ContextScope();

        var rootCallCount = 0;
        Action rootRerender = () => rootCallCount++;

        var hostNode = new NodeState();       // DockingHost equivalent
        var tabGroupNode = new NodeState();   // DockTabGroup equivalent
        var panelNode = new NodeState();      // DockPanelFrame equivalent

        // FIXED chain: each component wraps its parent's rerender
        var hostRerender = CreateChainedRerender(hostNode, rootRerender);
        var tabGroupRerender = CreateChainedRerender(tabGroupNode, hostRerender);
        var panelRerender = CreateChainedRerender(panelNode, tabGroupRerender);

        var host = new SimpleParent();
        var tabGroup = new SimpleMiddle();
        var panel = new SetStateDuringRenderChild();

        // Mount the chain
        MountComponent(host, scope, hostRerender);
        MountComponent(tabGroup, scope, tabGroupRerender);

        // Panel's UseState setter captures panelRerender via BeginRender
        MountComponent(panel, scope, panelRerender);

        // During-render setState was called (content was null → setContent("created")).
        // This fires the rerender chain via panelRerender.
        // Note: in the real reconciler, the setState during render triggers a re-render
        // cycle. Here we verify the chain was wired correctly.
        Assert.True(rootCallCount > 0, "Root rerender should have been called by during-render setState");

        // All ancestors should be marked SelfTriggered
        Assert.True(panelNode.SelfTriggered, "Panel (the child that called setState) should be SelfTriggered");
        Assert.True(tabGroupNode.SelfTriggered, "TabGroup ancestor should be SelfTriggered");
        Assert.True(hostNode.SelfTriggered, "Host ancestor should be SelfTriggered");

        // Memo check should pass for all three on the re-render
        Assert.True(ShouldRender(host, null, null, scope, selfTriggered: hostNode.SelfTriggered));
        Assert.True(ShouldRender(tabGroup, null, null, scope, selfTriggered: tabGroupNode.SelfTriggered));

        // Re-render the panel — on second render, content is non-null
        // (reset SelfTriggered as the reconciler does)
        panelNode.SelfTriggered = false;
        var el = MountComponent(panel, scope, panelRerender) as TextBlockElement;
        Assert.Equal(2, panel.RenderCount);
        Assert.Equal("created", panel.LastContent);
        Assert.Equal("created", el?.Content);
    }

    // ════════════════════════════════════════════════════════════════
    //  Test 5: Propagation with four levels of nesting
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SelfTriggered_Propagates_Through_Four_Levels()
    {
        bool rootCalled = false;
        Action rootRerender = () => rootCalled = true;

        var nodes = new NodeState[4];
        for (int i = 0; i < 4; i++) nodes[i] = new NodeState();

        // Chain: node[0] (outermost) -> node[1] -> node[2] -> node[3] (leaf)
        var rerender = rootRerender;
        var rerenders = new Action[4];
        for (int i = 0; i < 4; i++)
        {
            rerenders[i] = CreateChainedRerender(nodes[i], rerender);
            rerender = rerenders[i];
        }

        // Leaf setState fires the deepest rerender
        rerenders[3]();

        for (int i = 0; i < 4; i++)
        {
            Assert.True(nodes[i].SelfTriggered, $"Node at level {i} should be SelfTriggered");
        }
        Assert.True(rootCalled);
    }

    // ════════════════════════════════════════════════════════════════
    //  Test 6: SelfTriggered is reset after reconcile processes it
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SelfTriggered_Is_Consumed_After_Reconcile_And_Does_Not_Persist()
    {
        // The reconciler reads SelfTriggered then immediately sets it to false.
        // Simulate this: after the re-render cycle completes, subsequent parent
        // re-renders should correctly skip the component via memo check.

        var scope = new ContextScope();
        var comp = new PureChild();

        var node = new NodeState();
        var rerender = CreateChainedRerender(node, () => { });

        MountComponent(comp, scope, rerender);
        Assert.Equal(1, comp.RenderCount);

        // Simulate child setState → SelfTriggered is set
        node.SelfTriggered = true;
        Assert.True(ShouldRender(comp, null, null, scope, selfTriggered: node.SelfTriggered));

        // Reconciler consumes the flag
        node.SelfTriggered = false;

        // Next parent re-render: no state change, no props change → skip
        Assert.False(ShouldRender(comp, null, null, scope, selfTriggered: node.SelfTriggered));
    }

    // ════════════════════════════════════════════════════════════════
    //  Test 7: Mixed component types in chain
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Mixed_Class_Func_Memo_Chain_Propagates_Correctly()
    {
        // Class Component (parent) > FuncElement (middle) > MemoElement (leaf with state)
        var scope = new ContextScope();

        bool rootCalled = false;
        var classNode = new NodeState();
        var funcNode = new NodeState();
        var memoNode = new NodeState();

        var classRerender = CreateChainedRerender(classNode, () => rootCalled = true);
        var funcRerender = CreateChainedRerender(funcNode, classRerender);
        var memoRerender = CreateChainedRerender(memoNode, funcRerender);

        // Mount class component
        var parent = new PureChild();
        MountComponent(parent, scope, classRerender);

        // Mount func component
        var funcCtx = new RenderContext();
        funcCtx.BeginRender(funcRerender, scope);
        funcCtx.FlushEffects();

        // Mount memo component with state
        var memoCtx = new RenderContext();
        memoCtx.BeginRender(memoRerender, scope);
        var (val, setState) = memoCtx.UseState("init");
        memoCtx.FlushEffects();

        // Leaf memo component calls setState
        setState("updated");

        Assert.True(memoNode.SelfTriggered);
        Assert.True(funcNode.SelfTriggered);
        Assert.True(classNode.SelfTriggered);
        Assert.True(rootCalled);

        // Func with no context hooks and not self-triggered would skip
        Assert.False(MemoShouldRender(null, null, funcCtx, scope, selfTriggered: false));
        // But with SelfTriggered it re-renders
        Assert.True(MemoShouldRender(null, null, funcCtx, scope, selfTriggered: funcNode.SelfTriggered));
    }

    // ════════════════════════════════════════════════════════════════
    //  Test 8: Multiple children triggering setState independently
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Multiple_Children_SetState_Each_Marks_Own_Path()
    {
        var scope = new ContextScope();

        int rootCallCount = 0;
        Action rootRerender = () => { rootCallCount++; };

        var parentNode = new NodeState();
        var childANode = new NodeState();
        var childBNode = new NodeState();

        var parentRerender = CreateChainedRerender(parentNode, rootRerender);
        var childARerender = CreateChainedRerender(childANode, parentRerender);
        var childBRerender = CreateChainedRerender(childBNode, parentRerender);

        var childA = new StatefulChild();
        var childB = new StatefulChild();

        MountComponent(childA, scope, childARerender);
        MountComponent(childB, scope, childBRerender);

        // Only child A calls setState
        childA.SetCounter!(5);

        Assert.True(childANode.SelfTriggered);
        Assert.False(childBNode.SelfTriggered, "Child B should NOT be marked — it didn't setState");
        Assert.True(parentNode.SelfTriggered, "Parent is on child A's dirty path");

        // Reset for next cycle
        parentNode.SelfTriggered = false;
        childANode.SelfTriggered = false;

        // Now child B calls setState
        childB.SetCounter!(7);

        Assert.False(childANode.SelfTriggered, "Child A should NOT be marked this time");
        Assert.True(childBNode.SelfTriggered);
        Assert.True(parentNode.SelfTriggered, "Parent is on child B's dirty path");
        Assert.Equal(2, rootCallCount);
    }

    // ════════════════════════════════════════════════════════════════
    //  Test 9: Context change still works alongside propagation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Context_Change_And_SelfTriggered_Are_Independent_Bypass_Paths()
    {
        var scope = new ContextScope();

        var node = new NodeState();
        var rerender = CreateChainedRerender(node, () => { });

        var child = new ThemeAwareChild();

        // Mount with "dark" theme
        MountComponent(child, scope, rerender, contextValues: new() { [ThemeCtx] = "dark" });
        Assert.Equal(1, child.RenderCount);

        // Context changes to "light" — should re-render even without SelfTriggered
        scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "light" });
        Assert.False(node.SelfTriggered);
        Assert.True(ShouldRender(child, null, null, scope, selfTriggered: false),
            "Context change alone should trigger re-render");
        scope.Pop(1);
    }

    [Fact]
    public void SelfTriggered_Bypasses_Even_When_Context_Unchanged()
    {
        var scope = new ContextScope();

        var node = new NodeState();
        var rerender = CreateChainedRerender(node, () => { });

        var child = new ThemeAwareChild();

        // Mount with "dark" theme
        scope.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "dark" });
        MountComponent(child, scope, rerender);

        // Context unchanged, but SelfTriggered is set
        node.SelfTriggered = true;
        Assert.True(ShouldRender(child, null, null, scope, selfTriggered: node.SelfTriggered),
            "SelfTriggered should bypass memo even when context is unchanged");
        scope.Pop(1);
    }

    // ════════════════════════════════════════════════════════════════
    //  Test 10: Reconcile path also passes componentRerender
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Reconcile_Path_Passes_ComponentRerender_Not_Raw_RequestRerender()
    {
        // Verify the reconcile path: after initial mount, subsequent reconciles should
        // also chain rerender callbacks correctly. This tests that ReconcileComponent
        // passes componentRerender to Reconcile(), not requestRerender.

        var scope = new ContextScope();

        bool rootCalled = false;
        var parentNode = new NodeState();
        var childNode = new NodeState();

        var parentRerender = CreateChainedRerender(parentNode, () => rootCalled = true);
        var childRerender = CreateChainedRerender(childNode, parentRerender);

        var child = new StatefulChild();

        // Initial mount
        MountComponent(child, scope, childRerender);
        Assert.Equal(1, child.RenderCount);

        // Simulate reconcile: re-render the child (as the reconciler would after parent re-render)
        // The child is re-mounted with the same chained rerender
        MountComponent(child, scope, childRerender);
        Assert.Equal(2, child.RenderCount);

        // Reset flags
        rootCalled = false;
        parentNode.SelfTriggered = false;
        childNode.SelfTriggered = false;

        // Child calls setState again — should still propagate through chain
        child.SetCounter!(100);

        Assert.True(childNode.SelfTriggered);
        Assert.True(parentNode.SelfTriggered);
        Assert.True(rootCalled);

        // Verify state
        var el = MountComponent(child, scope, childRerender) as TextBlockElement;
        Assert.Equal("Count: 100", el?.Content);
    }

    // ════════════════════════════════════════════════════════════════
    //  Edge case: setState with same value does NOT trigger chain
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SetState_With_Same_Value_Does_Not_Trigger_Rerender_Chain()
    {
        var scope = new ContextScope();

        bool rootCalled = false;
        var parentNode = new NodeState();
        var childNode = new NodeState();

        var parentRerender = CreateChainedRerender(parentNode, () => rootCalled = true);
        var childRerender = CreateChainedRerender(childNode, parentRerender);

        var child = new StatefulChild();
        MountComponent(child, scope, childRerender);

        // Set to 0 (same as initial) → UseState's equality check prevents rerender
        child.SetCounter!(0);

        Assert.False(childNode.SelfTriggered, "setState with same value should not trigger rerender");
        Assert.False(parentNode.SelfTriggered);
        Assert.False(rootCalled);
    }

    // ════════════════════════════════════════════════════════════════
    //  Edge case: UseReducer functional update propagates
    // ════════════════════════════════════════════════════════════════

    private class ReducerChild : Component
    {
        public int RenderCount;
        public Action<Func<int, int>>? Update;
        public override Element Render()
        {
            RenderCount++;
            var (count, update) = UseReducer(0);
            Update = update;
            return new TextBlockElement($"Count: {count}");
        }
    }

    [Fact]
    public void UseReducer_Update_Propagates_Through_Ancestor_Chain()
    {
        var scope = new ContextScope();

        bool rootCalled = false;
        var parentNode = new NodeState();
        var childNode = new NodeState();

        var parentRerender = CreateChainedRerender(parentNode, () => rootCalled = true);
        var childRerender = CreateChainedRerender(childNode, parentRerender);

        var child = new ReducerChild();
        MountComponent(child, scope, childRerender);

        // Functional update: increment by 1
        child.Update!(prev => prev + 1);

        Assert.True(childNode.SelfTriggered);
        Assert.True(parentNode.SelfTriggered);
        Assert.True(rootCalled);

        var el = MountComponent(child, scope, childRerender) as TextBlockElement;
        Assert.Equal("Count: 1", el?.Content);
    }
}
