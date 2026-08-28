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
/// <c>tests/Reactor.SourceMap.Spike</c>; what needs a live control, and so lives
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
}
