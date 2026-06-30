using System;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #717 — <b>root-cause</b> fix for the RichTextBlock inline-UI scroll drift that
/// #487's reactive anchor only mitigates after the fact.
///
/// <para><b>Root cause.</b> Mutating a <c>Run.Text</c> in a paragraph that hosts an
/// <c>InlineUIContainer</c> makes WinUI's text engine re-measure the paragraph from
/// scratch (<c>ParagraphNode::Measure</c> → <c>RemoveEmbeddedElements</c> →
/// <c>desiredSize = 0</c>) for one layout pass, so the block transiently collapses. In
/// pure WinUI the detach + re-attach coalesce into one synchronous pass, but in the
/// live Reactor app the reconcile mutates the document and returns to the dispatcher;
/// the compositor commits the collapsed frame <i>before</i> the inline UI re-attaches,
/// the ancestor scroll host clamps <c>VerticalOffset</c> down to the smaller
/// <c>ScrollableHeight</c>, and re-growing the extent never restores the lost offset.</para>
///
/// <para><b>The fix (prevention at source).</b> Before the reconcile yields,
/// <c>PinExtentAcrossInlineUiMutation</c> (in <c>Reconciler.RichTextScrollAnchor.cs</c>)
/// raises the block's <c>MinHeight</c> floor to its full pre-collapse <c>ActualHeight</c>,
/// then releases it a few rendered frames later once the inline UI has re-attached. With
/// the floor pinned the transient <c>desiredSize = 0</c> can no longer shrink the block's
/// measured height, so <c>ScrollableHeight</c> never drops and there is nothing to clamp.
/// The #487 anchor is retained as a belt-and-suspenders backstop.</para>
///
/// <para><b>Why these fixtures are structured this way.</b> The live collapse is a
/// multi-frame, compositor-scheduled phenomenon that an in-process selftest's atomic
/// <see cref="Harness.Render"/> (<c>UpdateLayout</c>) cannot reproduce headlessly (the
/// inline UI re-adds within the same pass). So the fixtures decompose the contract into
/// three deterministic pieces: (1) the production reconcile <i>engages</i> the pin on an
/// inline-UI mutation, observed via the <c>Reconciler.InlineUiPinEngagementCount</c> test
/// seam — race-free w.r.t. the compositor-timed release; (2) an <i>engaged</i> pin
/// prevents the exact <c>ScrollableHeight</c> shrink + offset clamp that an un-pinned
/// block suffers under a simulated collapse; (3) the pin is <i>released</i> afterwards so
/// no permanent <c>MinHeight</c> inflation / blank space is left behind.</para>
/// </summary>
internal static class Issue717ExtentPinFixtures
{
    private const double ViewportHeight = 240;
    private const double InlineHeight = 90;

    private static Element BuildContent(int n, Action bump)
    {
        // Several inline-UI-bearing paragraphs (fixed-height borders) interleaved with
        // narration, so there is real scrollable height and a large combined inline-UI
        // extent for a collapse to remove. A Run text in every paragraph carries the
        // counter, so bumping `n` drives a genuine UpdateRichTextBlocks mutation.
        var paras = new RichTextParagraph[8];
        for (int i = 0; i < paras.Length; i++)
        {
            paras[i] = i % 2 == 0
                ? Paragraph(
                    Run($"row {i} counter {n} "),
                    InlineUI(Border(null).Width(220).Height(InlineHeight).Background("#4285F4")),
                    Run(" trailing narration that wraps to keep each paragraph tall."))
                : Paragraph(
                    Run($"row {i} counter {n} narration that wraps to keep the block " +
                        "tall enough to scroll well past the viewport."));
        }

        return VStack(8.0,
            Button("MutatePin717", bump),
            ScrollViewer(
                    (RichTextBlock(paras) with { IsTextSelectionEnabled = true }))
                .Height(ViewportHeight)
                .Set(sv =>
                {
                    sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                    sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                }));
    }

    // The live FrameworkElements embedded via InlineUIContainer — what WinUI's
    // ParagraphNode::Measure transiently detaches during an inline-UI re-measure.
    private static global::System.Collections.Generic.List<FrameworkElement> CollectInlineUiChildren(RichTextBlock? rtb)
    {
        var result = new global::System.Collections.Generic.List<FrameworkElement>();
        if (rtb is null) return result;
        foreach (var p in rtb.Blocks.OfType<Microsoft.UI.Xaml.Documents.Paragraph>())
            foreach (var fe in p.Inlines.OfType<Microsoft.UI.Xaml.Documents.InlineUIContainer>()
                         .Select(c => c.Child)
                         .OfType<FrameworkElement>())
                result.Add(fe);
        return result;
    }

    /// <summary>
    /// The production reconcile engages the extent pin on an inline-UI-bearing
    /// mutation. Observed via the engagement-count seam so the assertion does not race
    /// the compositor-frame-timed release. Red without the fix (pin never engaged).
    /// </summary>
    internal class Issue717_InlineUiMutationEngagesExtentPin(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (n, setN) = ctx.UseState(0);
                return BuildContent(n, () => setN(n + 1));
            });

            await Harness.Render();

            var rtb = H.FindControl<RichTextBlock>(_ => true);
            H.Check("Issue717_Pin_RtbMounted", rtb is not null);
            H.Check("Issue717_Pin_RtbMeasured", rtb is not null && rtb.ActualHeight > 0);

            int baseline = Reconciler.InlineUiPinEngagementCount;

            // A real inline-UI mutation: the Run text changes → UpdateRichTextBlocks
            // runs → PinExtentAcrossInlineUiMutation engages the floor. Only drain the
            // render queue (no test-driven layout) to exercise the production path.
            H.ClickButton("MutatePin717");
            await host.WaitForIdleAsync();

            // Core assertion: the reconcile engaged the pin for the inline-UI block.
            // Without the fix the method does not exist / is never called → no bump.
            H.Check("Issue717_PinEngagedOnInlineUiMutation",
                Reconciler.InlineUiPinEngagementCount > baseline);
        }
    }

    /// <summary>
    /// An engaged pin (MinHeight raised to the full extent) keeps the scroll host's
    /// <c>ScrollableHeight</c> and the user's <c>VerticalOffset</c> stable through the
    /// exact inline-UI collapse that clamps an un-pinned block. The fixture first proves
    /// the simulated collapse really does clamp without the pin (so it is not vacuous),
    /// then proves the pin prevents it.
    /// </summary>
    internal class Issue717_PinnedExtentSurvivesInlineUiCollapse(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (n, setN) = ctx.UseState(0);
                return BuildContent(n, () => setN(n + 1));
            });

            await Harness.Render();

            var sv = H.FindControl<ScrollViewer>(_ => true);
            var rtb = H.FindControl<RichTextBlock>(_ => true);
            H.Check("Issue717_Collapse_Mounted", sv is not null && rtb is not null);
            if (sv is null || rtb is null) return;

            // Park at the very bottom.
            sv.ChangeView(null, sv.ScrollableHeight, null, disableAnimation: true);
            await Harness.Render();
            await Harness.Render();

            double fullHeight = rtb.ActualHeight;
            double preScrollable = sv.ScrollableHeight;
            double preOffset = sv.VerticalOffset;

            H.Check("Issue717_Collapse_HasScrollableHeight", preScrollable > InlineHeight);
            H.Check("Issue717_Collapse_ParkedAtBottom",
                Math.Abs(preOffset - preScrollable) <= 2.0 && preOffset > InlineHeight);

            var inlineChildren = CollectInlineUiChildren(rtb);
            H.Check("Issue717_Collapse_HasInlineUi", inlineChildren.Count > 0);
            var savedHeights = inlineChildren.Select(c => c.Height).ToArray();

            // ── Control: WITHOUT the pin the collapse shrinks the extent and the host
            // clamps the offset (proves the simulation reproduces the #717 bug). A
            // synchronous UpdateLayout is used so no compositor frame is needed and the
            // classic ScrollViewer clamps within the pass.
            for (int i = 0; i < inlineChildren.Count; i++) inlineChildren[i].Height = 0;
            rtb.MinHeight = 0; // ensure no residual pin from the reconcile
            rtb.InvalidateMeasure();
            sv.UpdateLayout();

            bool clampWithoutPin =
                sv.ScrollableHeight < preScrollable - 0.5 && sv.VerticalOffset < preOffset - 0.5;
            H.Check("Issue717_Collapse_UnpinnedClamps", clampWithoutPin);

            // Restore the content and re-park, then repeat the collapse WITH the pin.
            for (int i = 0; i < inlineChildren.Count; i++) inlineChildren[i].Height = savedHeights[i];
            rtb.InvalidateMeasure();
            sv.UpdateLayout();
            sv.ChangeView(null, sv.ScrollableHeight, null, disableAnimation: true);
            await Harness.Render();
            await Harness.Render();

            double prePinScrollable = sv.ScrollableHeight;
            double prePinOffset = sv.VerticalOffset;

            // ── Engaged pin: raise the floor to the full extent (exactly what the
            // production reconcile does), then drive the same collapse.
            rtb.MinHeight = fullHeight;
            for (int i = 0; i < inlineChildren.Count; i++) inlineChildren[i].Height = 0;
            rtb.InvalidateMeasure();
            sv.UpdateLayout();

            // Core assertions: the pinned floor holds the extent, so ScrollableHeight
            // does not drop and the offset is never clamped.
            H.Check("Issue717_Collapse_PinnedExtentHeld",
                sv.ScrollableHeight >= prePinScrollable - 1.0);
            H.Check("Issue717_Collapse_PinnedOffsetPreserved",
                Math.Abs(sv.VerticalOffset - prePinOffset) <= 2.0);

            // Cleanup: drop the floor and restore the content.
            rtb.MinHeight = 0;
            for (int i = 0; i < inlineChildren.Count; i++) inlineChildren[i].Height = savedHeights[i];
            rtb.InvalidateMeasure();
            sv.UpdateLayout();
        }
    }

    /// <summary>
    /// The pin is released after the content recovers, so it leaves no permanent
    /// <c>MinHeight</c> inflation (which would show as stale blank space below a block
    /// whose content legitimately shrank). After a real mutation the floor returns to
    /// the author's original MinHeight within a bounded number of rendered frames.
    /// </summary>
    internal class Issue717_ExtentPinReleasesAfterContentRecovers(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (n, setN) = ctx.UseState(0);
                return BuildContent(n, () => setN(n + 1));
            });

            await Harness.Render();

            var rtb = H.FindControl<RichTextBlock>(_ => true);
            H.Check("Issue717_Release_RtbMounted", rtb is not null);
            if (rtb is null) return;

            double originalMinHeight = rtb.MinHeight; // author default (0)

            H.ClickButton("MutatePin717");
            await host.WaitForIdleAsync();

            // Let the compositor frames the pin schedules its release on actually run.
            await Harness.WaitFor(
                () => Math.Abs(rtb.MinHeight - originalMinHeight) <= 0.5,
                maxPasses: 90, perPassMs: 12);

            H.Check("Issue717_PinReleasedAfterRecovery",
                Math.Abs(rtb.MinHeight - originalMinHeight) <= 0.5);
        }
    }

    /// <summary>
    /// Regression for the release path: if the author changes the block's
    /// <c>MinHeight</c> while a pin is still pending (their <c>.Set(...)</c> setter runs
    /// after <c>UpdateRichTextBlocks</c> in the same reconcile, or on a later render), the
    /// deferred release must NOT overwrite that value with the pre-pin snapshot. The
    /// release restores only while the floor is still the exact value the pin raised.
    /// </summary>
    internal class Issue717_PinReleaseDoesNotClobberAuthorMinHeight(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (n, setN) = ctx.UseState(0);
                return BuildContent(n, () => setN(n + 1));
            });

            await Harness.Render();

            var rtb = H.FindControl<RichTextBlock>(_ => true);
            H.Check("Issue717_NoClobber_RtbMounted", rtb is not null);
            if (rtb is null) return;

            double fullHeight = rtb.ActualHeight;
            H.Check("Issue717_NoClobber_RtbMeasured", fullHeight > 0);

            // Engage the pin via a real inline-UI mutation (raises MinHeight to the extent).
            int baseline = Reconciler.InlineUiPinEngagementCount;
            H.ClickButton("MutatePin717");
            await host.WaitForIdleAsync();

            // Prove the pin actually engaged and raised the floor, so the no-clobber
            // assertion below cannot pass vacuously (e.g. if the pin never ran).
            H.Check("Issue717_NoClobber_PinEngaged",
                Reconciler.InlineUiPinEngagementCount > baseline);
            H.Check("Issue717_NoClobber_FloorRaised", rtb.MinHeight >= fullHeight - 1.0);

            // Simulate the author setting a fresh MinHeight while the pin is still pending,
            // e.g. a `.Set(rtb => rtb.MinHeight = …)` modifier or a subsequent render.
            const double authorMinHeight = 1234.0;
            rtb.MinHeight = authorMinHeight;

            // Pump a bounded number of render/compositor passes so the pin's scheduled
            // release frames elapse (there is no condition to wait on — we are asserting a
            // non-event, that the release does NOT fire a restore).
            for (int i = 0; i < 30; i++)
                await Harness.Render(12);

            // The release must leave the author's value intact, not restore the pre-pin floor.
            H.Check("Issue717_AuthorMinHeightNotClobbered",
                Math.Abs(rtb.MinHeight - authorMinHeight) <= 0.5);
        }
    }
}
