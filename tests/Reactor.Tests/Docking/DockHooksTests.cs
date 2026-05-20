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
