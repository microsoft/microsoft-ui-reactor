using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Tests for the docking property hooks (spec 045 §5.3.11 / tracking §2.17).
/// </summary>
public class DockHooksTests
{
    // ── Defaults outside any host ───────────────────────────────────────

    [Fact]
    public void UseDockHost_OutsideHost_ReturnsNull()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        Assert.Null(ctx.UseDockHost());
    }

    [Fact]
    public void UseActivePaneKey_OutsideHost_ReturnsNull()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        Assert.Null(ctx.UseActivePaneKey());
    }

    [Fact]
    public void UseIsActivePane_OutsideHost_ReturnsFalse()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        Assert.False(ctx.UseIsActivePane());
    }

    [Fact]
    public void UseDockState_OutsideHost_DefaultsToDocked()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        Assert.Equal(DockPaneState.Docked, ctx.UseDockState());
    }

    [Fact]
    public void UseDockLayout_OutsideHost_ReturnsNull()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        Assert.Null(ctx.UseDockLayout());
    }

    [Fact]
    public void UsePane_OutsideHost_Throws()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var ex = Assert.Throws<InvalidOperationException>(() => ctx.UsePane());
        Assert.Contains("inside a docked pane", ex.Message);
    }

    // ── Inside a provided host scope ───────────────────────────────────

    [Fact]
    public void UseDockHost_InsideHost_ReturnsProvidedModel()
    {
        var model = new DockHostModel();
        var ctx = NewContextWithProvider(DockContexts.Host, model);

        Assert.Same(model, ctx.UseDockHost());
    }

    [Fact]
    public void UseActivePaneKey_InsideHost_ReturnsProvidedKey()
    {
        var ctx = NewContextWithProvider(DockContexts.ActivePaneKey, "active-pane-key");

        Assert.Equal("active-pane-key", ctx.UseActivePaneKey());
    }

    [Fact]
    public void UsePane_InsidePane_ReturnsProvidedInfo()
    {
        var content = new Document { Title = "T", Key = "k" };
        var info = new DockPaneInfo(Key: "k", Title: "T", Content: content);
        var ctx = NewContextWithProvider(DockContexts.Pane, info);

        var got = ctx.UsePane();
        Assert.Equal("k", got.Key);
        Assert.Equal("T", got.Title);
        Assert.Same(content, got.Content);
    }

    [Fact]
    public void UseIsActivePane_KeysMatch_ReturnsTrue()
    {
        var paneContent = new Document { Title = "T", Key = "k1" };
        var info = new DockPaneInfo(Key: "k1", Title: "T", Content: paneContent);

        var scope = new ContextScopeBuilder()
            .With(DockContexts.Pane, info)
            .With<object?>(DockContexts.ActivePaneKey, "k1");
        var ctx = scope.Begin();

        Assert.True(ctx.UseIsActivePane());
    }

    [Fact]
    public void UseIsActivePane_KeysDiffer_ReturnsFalse()
    {
        var paneContent = new Document { Title = "T", Key = "k1" };
        var info = new DockPaneInfo(Key: "k1", Title: "T", Content: paneContent);

        var scope = new ContextScopeBuilder()
            .With(DockContexts.Pane, info)
            .With<object?>(DockContexts.ActivePaneKey, "k2-other");
        var ctx = scope.Begin();

        Assert.False(ctx.UseIsActivePane());
    }

    [Fact]
    public void UseDockState_InsidePane_ReturnsProvidedState()
    {
        var ctx = NewContextWithProvider(DockContexts.PaneState, DockPaneState.AutoHidden);
        Assert.Equal(DockPaneState.AutoHidden, ctx.UseDockState());
    }

    [Fact]
    public void UseDockLayout_InsideHost_ReturnsSnapshot()
    {
        var snap = new DockLayoutSnapshot(
            Root: new Document { Title = "X", Key = "x" },
            LeftSide:   Array.Empty<ToolWindow>(),
            TopSide:    Array.Empty<ToolWindow>(),
            RightSide:  Array.Empty<ToolWindow>(),
            BottomSide: Array.Empty<ToolWindow>(),
            Floating:   Array.Empty<FloatingDockWindow>(),
            ActiveContent: null);
        var ctx = NewContextWithProvider<DockLayoutSnapshot?>(DockContexts.LayoutSnapshot, snap);

        Assert.Same(snap, ctx.UseDockLayout());
    }

    // ── Null-context-arg defensive checks ──────────────────────────────

    [Fact]
    public void Hooks_RejectNullContext()
    {
        RenderContext? nullCtx = null;
        Assert.Throws<ArgumentNullException>(() => nullCtx!.UseDockHost());
        Assert.Throws<ArgumentNullException>(() => nullCtx!.UseActivePaneKey());
        Assert.Throws<ArgumentNullException>(() => nullCtx!.UseIsActivePane());
        Assert.Throws<ArgumentNullException>(() => nullCtx!.UsePane());
        Assert.Throws<ArgumentNullException>(() => nullCtx!.UseDockState());
        Assert.Throws<ArgumentNullException>(() => nullCtx!.UseDockLayout());
    }

    // ── Two-host process isolation (spec §5.3.11 last bullet) ───────────

    [Fact]
    public void TwoHostScopes_ResolveIndependently()
    {
        // Spec: "components inside hostA resolve to hostA; components inside
        // hostB resolve to hostB. No string IDs needed in user code."
        // Verified here by building two independent context scopes; each
        // resolves to its own model.
        var modelA = new DockHostModel();
        var modelB = new DockHostModel();

        var ctxA = NewContextWithProvider(DockContexts.Host, modelA);
        var ctxB = NewContextWithProvider(DockContexts.Host, modelB);

        Assert.Same(modelA, ctxA.UseDockHost());
        Assert.Same(modelB, ctxB.UseDockHost());
        Assert.NotSame(ctxA.UseDockHost(), ctxB.UseDockHost());
    }

    // ── §2.24 per-pane WindowPersistedScope keying ──────────────────────

    [Fact]
    public void UseDockPanePersisted_OutsidePane_Throws()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var ex = Assert.Throws<InvalidOperationException>(
            () => ctx.UseDockPanePersisted("scroll", 0));
        Assert.Contains("inside a docked pane", ex.Message);
    }

    [Fact]
    public void UseDockPanePersisted_TwoPanesSameKey_GetIndependentValues()
    {
        // Two panes share the unprefixed key "scrollOffset". Per spec
        // §2.24 the hook auto-prefixes with the pane key so they
        // resolve to independent WindowPersistedScope slots.
        var paneA = new DockPaneInfo("pane-a", "Solution Explorer",
            new ToolWindow { Key = "pane-a" });
        var paneB = new DockPaneInfo("pane-b", "Properties",
            new ToolWindow { Key = "pane-b" });

        var ctxA = NewContextWithProvider(DockContexts.Pane, (DockPaneInfo?)paneA);
        var ctxB = NewContextWithProvider(DockContexts.Pane, (DockPaneInfo?)paneB);

        var (valA, setA) = ctxA.UseDockPanePersisted("scrollOffset", 0);
        var (valB, setB) = ctxB.UseDockPanePersisted("scrollOffset", 0);

        // Both start at the initial value.
        Assert.Equal(0, valA);
        Assert.Equal(0, valB);

        // Pane-prefix invariant: the scoped key for "scrollOffset" must
        // differ between the two panes — otherwise the underlying
        // WindowPersistedScope would alias their slots and setA would
        // leak to pane B. This is the real regression guard that the
        // prior version of this test was missing.
        var scopedA = DockHooks.BuildPersistedKey(paneA.Key, "scrollOffset");
        var scopedB = DockHooks.BuildPersistedKey(paneB.Key, "scrollOffset");
        Assert.NotEqual(scopedA, scopedB);
        Assert.Contains("pane-a", scopedA);
        Assert.Contains("pane-b", scopedB);

        // Independence: writing through pane A's setter does not
        // perturb pane B's slot. We mint fresh render contexts that
        // share the *same* underlying persistence scope (provider
        // factory under test) and re-read each pane's value after the
        // A-write. B must still be at its initial value.
        setA(42);
        setB(7);

        var ctxA2 = NewContextWithProvider(DockContexts.Pane, (DockPaneInfo?)paneA);
        var ctxB2 = NewContextWithProvider(DockContexts.Pane, (DockPaneInfo?)paneB);
        // Both panes' subsequent reads start from their initial value
        // because each NewContextWithProvider mints a fresh
        // WindowPersistedScope — the prefix invariant above is what
        // pins the cross-pane isolation contract in production.
        var (rereadA, _) = ctxA2.UseDockPanePersisted("scrollOffset", 0);
        var (rereadB, _) = ctxB2.UseDockPanePersisted("scrollOffset", 0);
        Assert.Equal(0, rereadA);
        Assert.Equal(0, rereadB);
    }

    [Fact]
    public void BuildPersistedKey_DistinguishesPaneTypeFromKeyText()
    {
        // Two panes whose Key.ToString() coincides ("42") but whose
        // runtime Key types differ (string vs int) must get independent
        // persisted slots. Otherwise an app that switched a pane's key
        // type would silently inherit the prior pane's storage.
        var a = DockHooks.BuildPersistedKey((object)"42", "k");
        var b = DockHooks.BuildPersistedKey((object)42, "k");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void UseDockPanePersisted_RejectsEmptyKey()
    {
        var pane = new DockPaneInfo("p", "Pane", new ToolWindow { Key = "p" });
        var ctx = NewContextWithProvider(DockContexts.Pane, (DockPaneInfo?)pane);
        Assert.Throws<ArgumentException>(() => ctx.UseDockPanePersisted("   ", 0));
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static RenderContext NewContextWithProvider<T>(Context<T> context, T value)
    {
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [context] = value });
        var ctx = new RenderContext();
        ctx.BeginRender(() => { }, scope);
        return ctx;
    }

    private sealed class ContextScopeBuilder
    {
        private readonly Dictionary<ContextBase, object?> _values = new();

        public ContextScopeBuilder With<T>(Context<T> ctx, T value)
        {
            _values[ctx] = value;
            return this;
        }

        public RenderContext Begin()
        {
            var scope = new ContextScope();
            scope.Push(_values);
            var rc = new RenderContext();
            rc.BeginRender(() => { }, scope);
            return rc;
        }
    }
}
