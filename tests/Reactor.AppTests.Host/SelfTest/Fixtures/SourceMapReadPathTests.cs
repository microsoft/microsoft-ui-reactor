using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Diagnostics;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 010 — proves the source-map chain end to end against real WinUI controls:
/// interceptor stamp → <c>Element.CallSite</c> → <c>ReactorAttached.StateProperty</c>
/// → <c>ReactorState.Element</c> → <see cref="ReactorSourceMap.GetSource"/>.
///
/// <para>This host builds with <c>ReactorSourceMap=true</c> (the Debug default
/// from <c>build/Reactor.targets</c>), so the DSL calls below are really
/// intercepted — these fixtures exercise the shipping path rather than a
/// hand-written stand-in. The headless generator-side assertions live in
/// <c>tests/Reactor.SourceMap.Tests</c>; what needs a live control, and so lives
/// here, is the reconciler half: whether a stamped element actually gets a
/// <c>ReactorState</c> back-pointer and survives to be read back.</para>
/// </summary>
internal static class SourceMapReadPathTests
{
    private static int Line([CallerLineNumber] int line = 0) => line;

    /// <summary>
    /// Why the interceptor-dependent checks below skip rather than fail when
    /// REACTOR_SOURCEMAP is not defined.
    ///
    /// <para>The gate is a BUILD fact, not a runtime probe. Skipping on "GetSource
    /// returned null" would be the classic comforting negative: a generator that
    /// silently stopped emitting would skip green forever. Keying on the compile
    /// constant means a Debug build (which is what PR CI runs) always asserts
    /// hard, and only a build that genuinely has no interceptors compiled in can
    /// skip — e.g. the AOT selftests job, which publishes Release.</para>
    ///
    /// <para>Each fixture still asserts an observable precondition (the control
    /// mounted) before skipping, so a broken harness stays red rather than
    /// disappearing into a skip.</para>
    /// </summary>
    private const string SkipReason =
        "assembly built without REACTOR_SOURCEMAP (Release) - no interceptors compiled in, so there is no call site to read";

    private static readonly SourceLocation HandStamp = new(@"C:\fixture\HandStamped.cs", 4242);

    /// <summary>
    /// Flag ON: a bare TextBlock — no callbacks, no key, no reference modifiers,
    /// i.e. exactly the display leaf PR #468 stopped tagging — becomes readable
    /// again purely because the interceptor stamped it, and reports the real
    /// file and line of its own call site.
    /// </summary>
    internal class LeafIsReadableWhenEnabled(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = true;
            try
            {
                // Factory call and line probe on ONE physical line, so the expected
                // line is an independent oracle rather than a magic offset.
                var leaf = TextBlock("mapped"); var expectedLine = Line();

                var host = H.CreateHost();
                host.Mount(ctx => VStack(leaf));
                await Harness.Render();

                var target = H.FindControl<TextBlock>(t => t.Text == "mapped");
                H.Check("SourceMapReadPath_LeafMounted", target is not null);
                if (target is null) return;

#if REACTOR_SOURCEMAP
                var resolved = ReactorSourceMap.GetSource(target);

                // Non-null alone would be satisfied by any stamp reaching any
                // element, so assert the exact call site.
                H.Check("SourceMapReadPath_Resolved", resolved is not null);
                H.Check("SourceMapReadPath_LineIsRealCallSite",
                    resolved?.LineNumber == expectedLine);
                H.Check("SourceMapReadPath_FileIsThisFixture",
                    resolved?.FilePath.EndsWith("SourceMapReadPathTests.cs", StringComparison.Ordinal) == true);
#else
                _ = expectedLine;
                H.Skip("SourceMapReadPath_Resolved", SkipReason);
                H.Skip("SourceMapReadPath_LineIsRealCallSite", SkipReason);
                H.Skip("SourceMapReadPath_FileIsThisFixture", SkipReason);
#endif
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }
    }

    /// <summary>
    /// Two leaves on different source lines must resolve to different locations.
    /// Guards against a stamp that is present but constant — which the single
    /// fixture above could not distinguish from a correct one if every element
    /// happened to share one location.
    /// </summary>
    internal class DistinctLeavesReportDistinctLines(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = true;
            try
            {
                var first = TextBlock("first"); var firstLine = Line();
                var second = TextBlock("second"); var secondLine = Line();

                var host = H.CreateHost();
                host.Mount(ctx => VStack(first, second));
                await Harness.Render();

                var a = H.FindControl<TextBlock>(t => t.Text == "first");
                var b = H.FindControl<TextBlock>(t => t.Text == "second");
                H.Check("SourceMapReadPath_BothLeavesMounted", a is not null && b is not null);
                if (a is null || b is null) return;

#if REACTOR_SOURCEMAP
                var ra = ReactorSourceMap.GetSource(a);
                var rb = ReactorSourceMap.GetSource(b);

                H.Check("SourceMapReadPath_FirstLine", ra?.LineNumber == firstLine);
                H.Check("SourceMapReadPath_SecondLine", rb?.LineNumber == secondLine);
                H.Check("SourceMapReadPath_LinesDiffer",
                    ra is not null && rb is not null && ra.Value.LineNumber != rb.Value.LineNumber);
#else
                _ = firstLine; _ = secondLine;
                H.Skip("SourceMapReadPath_FirstLine", SkipReason);
                H.Skip("SourceMapReadPath_SecondLine", SkipReason);
                H.Skip("SourceMapReadPath_LinesDiffer", SkipReason);
#endif
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }
    }

    /// <summary>
    /// Flag OFF: the interceptor is still compiled in, but skips the stamp, so
    /// the leaf carries no <c>Extensions</c> and must NOT be tagged. This is the
    /// negative control for the fixtures above AND the guard on PR #468's
    /// leaf-tagging allocation win — if it ever passes with a tag, the retail
    /// allocation profile has regressed.
    /// </summary>
    internal class LeafIsNotTaggedWhenDisabled(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = false;
            try
            {
                var leaf = TextBlock("unmapped");

                var host = H.CreateHost();
                host.Mount(ctx => VStack(leaf));
                await Harness.Render();

                var target = H.FindControl<TextBlock>(t => t.Text == "unmapped");
                H.Check("SourceMapReadPath_DisabledLeafMounted", target is not null);
                if (target is null) return;

                H.Check("SourceMapReadPath_DisabledLeafHasNoStamp", leaf.CallSite is null);
                H.Check("SourceMapReadPath_DisabledLeafNotTagged",
                    Reconciler.GetElementTag(target) is null);
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }
    }

    /// <summary>
    /// A stamped element is tagged even with the runtime flag off, because
    /// <c>CallSite</c> lives in the <c>ElementExtras</c> bucket and
    /// <c>NeedsTag</c> already accepts a non-null bucket. This is what lets spec
    /// 010 add no arm to <c>NeedsTag</c> at all — asserted rather than assumed,
    /// since deleting that arm was a deliberate design decision.
    /// </summary>
    internal class HandStampedLeafIsTaggedWithFlagOff(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = false;
            try
            {
                var host = H.CreateHost();
                host.Mount(ctx => VStack(
                    TextBlock("hand-stamped") with { CallSite = HandStamp }));
                await Harness.Render();

                var target = H.FindControl<TextBlock>(t => t.Text == "hand-stamped");
                H.Check("SourceMapReadPath_HandStampedMounted", target is not null);
                if (target is null) return;

                H.Check("SourceMapReadPath_HandStampedReadableWithoutFlag",
                    ReactorSourceMap.GetSource(target)?.LineNumber == 4242);
                H.Check("SourceMapReadPath_HandStampedShortForm",
                    ReactorSourceMap.GetSource(target)?.ToShortString() == "HandStamped.cs:4242");
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }
    }

    /// <summary>
    /// An element that was ALREADY tagged for its own reasons (a callback-bearing
    /// Button) still carries its source location — confirming the stamp composes
    /// with the pre-existing tagging categories rather than competing with them.
    /// </summary>
    internal class CallbackControlAlsoCarriesSource(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = true;
            try
            {
                var btn = Button("tagged", () => { }); var expectedLine = Line();

                var host = H.CreateHost();
                host.Mount(ctx => VStack(btn));
                await Harness.Render();

                var target = H.FindControl<Button>(b => b.Content as string == "tagged");
                H.Check("SourceMapReadPath_ButtonMounted", target is not null);
                if (target is null) return;

#if REACTOR_SOURCEMAP
                H.Check("SourceMapReadPath_ButtonCarriesRealCallSite",
                    ReactorSourceMap.GetSource(target)?.LineNumber == expectedLine);
#else
                _ = expectedLine;
                H.Skip("SourceMapReadPath_ButtonCarriesRealCallSite", SkipReason);
#endif
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }
    }

    /// <summary>
    /// The branch-switch case: two structurally identical, callback-free leaves
    /// rendered from DIFFERENT lines. <c>ShallowEquals</c> ignores
    /// <c>CallSite</c>, so these compare equal and take the reconciler's skip
    /// path — which left the control's back-pointer naming the branch that is no
    /// longer live, so <c>GetSource</c> reported a confidently wrong line.
    /// Guards the refresh added to the skip arms.
    /// </summary>
    internal class BranchSwitchRefreshesTheReportedLine(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = true;
            try
            {
                // Two calls, same text, different lines. Identical for rendering.
                var fromA = TextBlock("branch"); var lineA = Line();
                var fromB = TextBlock("branch"); var lineB = Line();

                var host = H.CreateHost();
                var useA = true;
                host.Mount(ctx => VStack(useA ? fromA : fromB));
                await Harness.Render();

                var target = H.FindControl<TextBlock>(t => t.Text == "branch");
                H.Check("SourceMapBranch_Mounted", target is not null);
                if (target is null) return;

#if REACTOR_SOURCEMAP
                H.Check("SourceMapBranch_InitialLineIsA",
                    ReactorSourceMap.GetSource(target)?.LineNumber == lineA);

                // Swap branches and re-render. Re-mounting the host is how the
                // other fixtures drive an update; the two elements are
                // shallow-equal, so this lands on the skip path rather than a
                // real update — which is precisely the path under test.
                useA = false;
                host.Mount(ctx => VStack(useA ? fromA : fromB));
                await Harness.Render();

                var after = H.FindControl<TextBlock>(t => t.Text == "branch");
                H.Check("SourceMapBranch_StillMounted", after is not null);
                if (after is null) return;

                var reported = ReactorSourceMap.GetSource(after)?.LineNumber;
                H.Check("SourceMapBranch_LineFollowsTheLiveBranch", reported == lineB);
                H.Check("SourceMapBranch_LineIsNotStale", reported != lineA);
#else
                _ = lineA; _ = lineB;
                H.Skip("SourceMapBranch_InitialLineIsA", SkipReason);
                H.Skip("SourceMapBranch_StillMounted", SkipReason);
                H.Skip("SourceMapBranch_LineFollowsTheLiveBranch", SkipReason);
                H.Skip("SourceMapBranch_LineIsNotStale", SkipReason);
#endif
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }
    }

    /// <summary>
    /// Spec 010 — flipping the runtime flag OFF between renders must clear the
    /// reported location, without remounting the subtree.
    ///
    /// <para>This is the mechanism the source-map-explorer sample's toggle relies on.
    /// With the flag off the interceptor returns the element unstamped, so the new
    /// element's <c>CallSite</c> is null while the mounted one's is not. The elements
    /// are still shallow-equal — <c>ShallowEquals</c> ignores <c>CallSite</c> — so the
    /// skip is TAKEN, and <c>Reconciler.CallSiteChangedOnSkip</c> is evaluated inside
    /// the skip arm to refresh the control's back-pointer without a full update. Without
    /// that refresh the control keeps reporting the location it was mounted with, which
    /// is what made the sample read "8 of 14 mapped" instead of "0 of 14" and forced a
    /// generation-key remount to paper over.</para>
    /// </summary>
    internal class FlagOffClearsTheReportedLocation(Harness h) : SelfTestFixtureBase(h)
    {
        private static int Line([CallerLineNumber] int line = 0) => line;

        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            try
            {
                ReactorSourceMap.Enabled = true;
                var stamped = TextBlock("toggle"); var stampedLine = Line();

                var host = H.CreateHost();
                var current = stamped;
                host.Mount(ctx => VStack(current));
                await Harness.Render();

                var target = H.FindControl<TextBlock>(t => t.Text == "toggle");
                H.Check("SourceMapToggle_Mounted", target is not null);
                if (target is null) return;

#if REACTOR_SOURCEMAP
                H.Check("SourceMapToggle_MappedWhileOn",
                    ReactorSourceMap.GetSource(target)?.LineNumber == stampedLine);

                // Flag off, then build the element AGAIN so it goes through the
                // interceptor in its disabled state. Re-using the already-stamped
                // instance would prove nothing: it would still carry its location.
                ReactorSourceMap.Enabled = false;
                current = TextBlock("toggle");
                host.Mount(ctx => VStack(current));
                await Harness.Render();

                var after = H.FindControl<TextBlock>(t => t.Text == "toggle");
                H.Check("SourceMapToggle_StillMounted", after is not null);
                if (after is null) return;

                H.Check("SourceMapToggle_ClearsWithoutRemount",
                    ReactorSourceMap.GetSource(after) is null);
#else
                _ = stampedLine;
                H.Skip("SourceMapToggle_MappedWhileOn", SkipReason);
                H.Skip("SourceMapToggle_StillMounted", SkipReason);
                H.Skip("SourceMapToggle_ClearsWithoutRemount", SkipReason);
#endif
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }
    }

    /// <summary>
    /// Spec 010 — a <c>Component&lt;T&gt;()</c> call must be resolvable from its
    /// realized control.
    ///
    /// <para>The composition primitives mount a <c>Border</c> wrapper, and that wrapper
    /// is the control an inspector actually hits. Until the mount paths tagged it, an
    /// intercepted <c>Component&lt;T&gt;()</c> had a perfectly good
    /// <c>Element.CallSite</c> that <c>GetSource</c> could never reach — the read path
    /// silently excluded the DSL call a devtools user most wants to resolve.</para>
    /// </summary>
    internal class ComponentWrapperIsResolvable(Harness h) : SelfTestFixtureBase(h)
    {
        private sealed class Leaf : Microsoft.UI.Reactor.Core.Component
        {
            public override Element Render() => TextBlock("component-leaf");
        }

        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = true;
            try
            {
                var host = H.CreateHost();
                var compLine = 0;
                host.Mount(ctx => VStack(At(out compLine, Component<Leaf>())));
                await Harness.Render();

                var leaf = H.FindControl<TextBlock>(t => t.Text == "component-leaf");
                H.Check("SourceMapComp_Mounted", leaf is not null);
                if (leaf is null) return;

                // Walk up to the component's Border wrapper — the control an inspector
                // hits when it picks the component rather than its rendered leaf.
                var wrapper = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(leaf) as Microsoft.UI.Xaml.FrameworkElement;
                H.Check("SourceMapComp_WrapperFound", wrapper is not null);
                if (wrapper is null) return;

#if REACTOR_SOURCEMAP
                H.Check("SourceMapComp_WrapperReportsTheComponentCallSite",
                    ReactorSourceMap.GetSource(wrapper)?.LineNumber == compLine);
#else
                _ = compLine;
                H.Skip("SourceMapComp_WrapperReportsTheComponentCallSite", SkipReason);
#endif
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }

        /// <summary>
        /// Captures the physical line of the <c>Component&lt;&gt;</c> call, which sits on
        /// the same line as this call — an independent oracle rather than a constant.
        /// </summary>
        private static Element At(out int line, Element element, [global::System.Runtime.CompilerServices.CallerLineNumber] int caller = 0)
        {
            line = caller;
            return element;
        }
    }

    /// <summary>
    /// Spec 010 — a control wrapped by a target-wrapping decorator must report the
    /// TARGET's call site, not the decorator's.
    ///
    /// <para><c>Flyout</c> mounts its <c>Target</c>'s control and then replaces that
    /// control's tag with the <c>FlyoutElement</c>, because its Opened/Closed handlers
    /// read it back. The realized control is still the Button, created by the
    /// <c>Button(</c> line — so without the decorator walk in <c>GetSource</c> an
    /// inspector would name the <c>Flyout(</c> line as the Button's creator. Same
    /// misattribution the generator avoids for pass-through factories.</para>
    /// </summary>
    internal class DecoratedControlReportsItsTargetsCallSite(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = true;
            try
            {
                var host = H.CreateHost();
                int buttonLine = 0, flyoutLine = 0;
                host.Mount(ctx => VStack(
                    At(out flyoutLine, Flyout(
                        At(out buttonLine, Button("decorated", () => { })),
                        TextBlock("menu")))
                ));
                await Harness.Render();

                var btn = H.FindControl<Microsoft.UI.Xaml.Controls.Button>(b => (b.Content as string) == "decorated");
                H.Check("SourceMapDecorator_Mounted", btn is not null);
                if (btn is null) return;

#if REACTOR_SOURCEMAP
                // Guard the premise: the two calls really are on different lines, so the
                // assertion below cannot pass by coincidence.
                H.Check("SourceMapDecorator_LinesDiffer", buttonLine != 0 && buttonLine != flyoutLine);

                var reported = ReactorSourceMap.GetSource(btn)?.LineNumber;
                H.Check("SourceMapDecorator_ReportsTheButtonLine", reported == buttonLine);
                H.Check("SourceMapDecorator_NotTheFlyoutLine", reported != flyoutLine);
#else
                _ = buttonLine; _ = flyoutLine;
                H.Skip("SourceMapDecorator_LinesDiffer", SkipReason);
                H.Skip("SourceMapDecorator_ReportsTheButtonLine", SkipReason);
                H.Skip("SourceMapDecorator_NotTheFlyoutLine", SkipReason);
#endif
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }

        private static Element At(out int line, Element element, [global::System.Runtime.CompilerServices.CallerLineNumber] int caller = 0)
        {
            line = caller;
            return element;
        }
    }

    /// <summary>
    /// Spec 010 — a decorated target that switches branches reports the live line.
    ///
    /// <para>CHARACTERIZATION, not a regression guard for the skip predicate. It passes
    /// with and without the decorator unwrap in
    /// <c>Reconciler.CallSiteChangedOnSkip</c> — verified by mutation — because this
    /// shape does not reach the skip arm: something upstream routes the decorator
    /// through a full update, which re-tags the control via the decorator adapter. The
    /// unwrap in the predicate is therefore defensive, kept because
    /// <c>ShallowEquals</c> DOES return true for <c>(FlyoutElement, FlyoutElement)</c>
    /// regardless of Target, so the skip is reachable in principle; I could not
    /// construct a shape that reaches it.</para>
    ///
    /// <para>What this fixture does prove is worth having on its own: a decorated
    /// target's reported location follows the live branch rather than the retired one.</para>
    /// </summary>
    internal class DecoratedTargetBranchSwitchRefreshes(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var previous = ReactorSourceMap.Enabled;
            ReactorSourceMap.Enabled = true;
            try
            {
                // Two identical buttons on different lines: identical for rendering, so
                // the decorator compares shallow-equal and the skip path is reached.
                var fromA = Button("decorated-branch", () => { }); var lineA = Line();
                var fromB = Button("decorated-branch", () => { }); var lineB = Line();

                var host = H.CreateHost();
                var useA = true;
                host.Mount(ctx => VStack(Flyout(useA ? fromA : fromB, TextBlock("menu"))));
                await Harness.Render();

                var btn = H.FindControl<Microsoft.UI.Xaml.Controls.Button>(b => (b.Content as string) == "decorated-branch");
                H.Check("SourceMapDecoBranch_Mounted", btn is not null);
                if (btn is null) return;

#if REACTOR_SOURCEMAP
                H.Check("SourceMapDecoBranch_LinesDiffer", lineA != 0 && lineA != lineB);
                H.Check("SourceMapDecoBranch_InitialIsA",
                    ReactorSourceMap.GetSource(btn)?.LineNumber == lineA);

                useA = false;
                host.Mount(ctx => VStack(Flyout(useA ? fromA : fromB, TextBlock("menu"))));
                await Harness.Render();

                var after = H.FindControl<Microsoft.UI.Xaml.Controls.Button>(b => (b.Content as string) == "decorated-branch");
                H.Check("SourceMapDecoBranch_StillMounted", after is not null);
                if (after is null) return;

                var reported = ReactorSourceMap.GetSource(after)?.LineNumber;
                H.Check("SourceMapDecoBranch_FollowsTheLiveBranch", reported == lineB);
                H.Check("SourceMapDecoBranch_NotStale", reported != lineA);
#else
                _ = lineA; _ = lineB;
                H.Skip("SourceMapDecoBranch_LinesDiffer", SkipReason);
                H.Skip("SourceMapDecoBranch_InitialIsA", SkipReason);
                H.Skip("SourceMapDecoBranch_StillMounted", SkipReason);
                H.Skip("SourceMapDecoBranch_FollowsTheLiveBranch", SkipReason);
                H.Skip("SourceMapDecoBranch_NotStale", SkipReason);
#endif
            }
            finally
            {
                ReactorSourceMap.Enabled = previous;
            }
        }

        private static int Line([global::System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;
    }
}

