using System.Linq;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 010 — direct headless coverage for <c>Reconciler.CallSiteChangedOnSkip</c>, the
/// predicate that decides whether a shallow-skipped element still needs its source tag
/// refreshed.
///
/// <para>This exists because the selftest that nominally covered the decorator arm,
/// <c>SourceMapReadPathTests.DecoratedTargetBranchSwitchRefreshes</c>, is a
/// characterization test rather than a regression guard: its scenario takes a full
/// update, so it stays green with the decorator unwrap removed. Calling the predicate
/// directly is what actually pins the branch — every case below fails if the
/// corresponding behaviour is removed.</para>
/// </summary>
public sealed class CallSiteChangedOnSkipTests
{
    private static readonly SourceLocation A = new("A.cs", 1);
    private static readonly SourceLocation B = new("B.cs", 2);

    [Fact]
    public void EqualCallSites_DoNotNeedARefresh()
    {
        var oldEl = new TextBlockElement("same") with { CallSite = A };
        var newEl = new TextBlockElement("same") with { CallSite = A };

        Assert.False(Reconciler.CallSiteChangedOnSkip(oldEl, newEl));
    }

    [Fact]
    public void DifferentCallSites_NeedARefresh()
    {
        var oldEl = new TextBlockElement("same") with { CallSite = A };
        var newEl = new TextBlockElement("same") with { CallSite = B };

        Assert.True(Reconciler.CallSiteChangedOnSkip(oldEl, newEl));
    }

    [Fact]
    public void BothUnstamped_NeedNoRefresh()
    {
        // The flag-off shape: nothing is stamped, so there is nothing to refresh. Also
        // the case that must stay cheap, since it is every ordinary shallow skip.
        Assert.False(Reconciler.CallSiteChangedOnSkip(
            new TextBlockElement("same"),
            new TextBlockElement("same")));
    }

    [Fact]
    public void DecoratorWithSameOwnStampButDifferentTarget_NeedsARefresh()
    {
        // THE decorator arm. Both Flyouts carry the same CallSite, so comparing only the
        // outer elements says "no change" and the control keeps reporting a target line
        // that is no longer live. Only unwrapping to the target reveals the difference.
        var oldEl = new FlyoutElement(new TextBlockElement("t") with { CallSite = A }, new TextBlockElement("c")) with { CallSite = A };
        var newEl = new FlyoutElement(new TextBlockElement("t") with { CallSite = B }, new TextBlockElement("c")) with { CallSite = A };

        Assert.True(Reconciler.CallSiteChangedOnSkip(oldEl, newEl));
    }

    [Fact]
    public void DecoratorWithSameOwnStampAndSameTarget_NeedsNoRefresh()
    {
        // Negative control for the case above: if the unwrap had degenerated into
        // "always true", the test above would pass while proving nothing.
        var oldEl = new FlyoutElement(new TextBlockElement("t") with { CallSite = A }, new TextBlockElement("c")) with { CallSite = A };
        var newEl = new FlyoutElement(new TextBlockElement("t") with { CallSite = A }, new TextBlockElement("c")) with { CallSite = A };

        Assert.False(Reconciler.CallSiteChangedOnSkip(oldEl, newEl));
    }

    [Fact]
    public void NestedDecorators_ResolveToTheInnermostTarget()
    {
        var oldEl = new FlyoutElement(
            new MenuFlyoutElement(new TextBlockElement("t") with { CallSite = A }, []) with { CallSite = A },
            new TextBlockElement("c")) with { CallSite = A };
        var newEl = new FlyoutElement(
            new MenuFlyoutElement(new TextBlockElement("t") with { CallSite = B }, []) with { CallSite = A },
            new TextBlockElement("c")) with { CallSite = A };

        Assert.True(Reconciler.CallSiteChangedOnSkip(oldEl, newEl));
    }

    [Fact]
    public void SelfReferentialResolver_TerminatesInsteadOfHanging()
    {
        // GetSourceTarget is a third-party extension point, so a handler can return its
        // own element. A naive unwrap loop spins forever — hanging RECONCILIATION, not
        // just the inspector. If this regresses the test does not fail, it hangs, which
        // is why CI runs these suites under --hangdump.
        ControlRegistry.RegisterDecorator<SelfTargetingElement>(
            static () => new SelfTargetingHandler());

        var element = new SelfTargetingElement() with { CallSite = A };

        Assert.Null(ReactorSourceMap.UnwrapDecorators(element));
        Assert.False(Reconciler.CallSiteChangedOnSkip(element, element));
    }

    [Fact]
    public void CyclicResolverChain_TerminatesInsteadOfHanging()
    {
        // Two decorators pointing at each other — the two-node cycle a single
        // "did it return itself" check would miss.
        ControlRegistry.RegisterDecorator<CycleAElement>(static () => new CycleAHandler());
        ControlRegistry.RegisterDecorator<CycleBElement>(static () => new CycleBHandler());

        var a = new CycleAElement();
        var b = new CycleBElement();
        CycleAHandler.Target = b;
        CycleBHandler.Target = a;

        try
        {
            Assert.Null(ReactorSourceMap.UnwrapDecorators(a));
        }
        finally
        {
            CycleAHandler.Target = null;
            CycleBHandler.Target = null;
        }
    }

    [Fact]
    public void NonCyclicChain_IsNotMistakenForACycle()
    {
        // Positive control for the two tests above: a legitimate chain must still
        // resolve, so "return null on cycle" cannot be passing by rejecting everything.
        var target = new TextBlockElement("t") with { CallSite = B };
        var element = new FlyoutElement(target, new TextBlockElement("c")) with { CallSite = A };

        Assert.Same(target, ReactorSourceMap.UnwrapDecorators(element));
    }

    [Fact]
    public void ResolverReturningAFreshElementEachCall_TerminatesInsteadOfHanging()
    {
        // The case tortoise-and-hare alone does NOT catch: every call returns a new
        // object, so no two nodes in the chain are ever reference-equal and the cycle
        // check never fires. `element with { }` is the easy way to write this by
        // accident. Only the depth cap bounds it.
        ControlRegistry.RegisterDecorator<FreshTargetElement>(
            static () => new FreshTargetHandler());

        var element = new FreshTargetElement() with { CallSite = A };

        Assert.Null(ReactorSourceMap.UnwrapDecorators(element));
        Assert.False(Reconciler.CallSiteChangedOnSkip(element, element));
    }

    private sealed record SelfTargetingElement : Element;

    private sealed class SelfTargetingHandler : IDecoratorElementHandler<SelfTargetingElement>
    {
        public UIElement Mount(MountContext ctx, SelfTargetingElement element) => throw new NotSupportedException();

        public UIElement Update(UpdateContext ctx, SelfTargetingElement oldEl, SelfTargetingElement newEl, UIElement control)
            => throw new NotSupportedException();

        public V1UnmountDisposition Unmount(UnmountContext ctx, SelfTargetingElement? element, UIElement control)
            => V1UnmountDisposition.ContinueDefaultTraversal;

        public Element? GetSourceTarget(SelfTargetingElement element) => element;
    }

    private sealed record FreshTargetElement : Element;

    private sealed class FreshTargetHandler : IDecoratorElementHandler<FreshTargetElement>
    {
        public UIElement Mount(MountContext ctx, FreshTargetElement element) => throw new NotSupportedException();

        public UIElement Update(UpdateContext ctx, FreshTargetElement oldEl, FreshTargetElement newEl, UIElement control)
            => throw new NotSupportedException();

        public V1UnmountDisposition Unmount(UnmountContext ctx, FreshTargetElement? element, UIElement control)
            => V1UnmountDisposition.ContinueDefaultTraversal;

        // A brand-new instance every call: an unbounded chain with no repeated identity.
        public Element? GetSourceTarget(FreshTargetElement element) => element with { };
    }

    private sealed record CycleAElement : Element;

    private sealed record CycleBElement : Element;

    private sealed class CycleAHandler : IDecoratorElementHandler<CycleAElement>
    {
        internal static Element? Target { get; set; }

        public UIElement Mount(MountContext ctx, CycleAElement element) => throw new NotSupportedException();

        public UIElement Update(UpdateContext ctx, CycleAElement oldEl, CycleAElement newEl, UIElement control)
            => throw new NotSupportedException();

        public V1UnmountDisposition Unmount(UnmountContext ctx, CycleAElement? element, UIElement control)
            => V1UnmountDisposition.ContinueDefaultTraversal;

        public Element? GetSourceTarget(CycleAElement element) => Target;
    }

    private sealed class CycleBHandler : IDecoratorElementHandler<CycleBElement>
    {
        internal static Element? Target { get; set; }

        public UIElement Mount(MountContext ctx, CycleBElement element) => throw new NotSupportedException();

        public UIElement Update(UpdateContext ctx, CycleBElement oldEl, CycleBElement newEl, UIElement control)
            => throw new NotSupportedException();

        public V1UnmountDisposition Unmount(UnmountContext ctx, CycleBElement? element, UIElement control)
            => V1UnmountDisposition.ContinueDefaultTraversal;

        public Element? GetSourceTarget(CycleBElement element) => Target;
    }
}
