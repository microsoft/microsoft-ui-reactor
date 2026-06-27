using System;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Charting.Charts;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #487 — a <c>RichTextBlock</c> hosting inline UI (charts/sliders/buttons via
/// <c>InlineUI(...)</c>) inside a <c>ScrollViewer</c> silently scrolls up on any
/// <c>Run</c> mutation. WinUI's text engine re-measures every inline-UI-bearing
/// paragraph from scratch (<c>ParagraphNode::Measure</c> → <c>RemoveEmbeddedElements</c>
/// + <c>desiredSize = 0</c>) for one layout pass, so the block transiently shrinks and
/// the ScrollViewer silently clamps <c>VerticalOffset</c> down to the smaller
/// <c>ScrollableHeight</c> — then never restores it once the inline UI re-attaches.
/// These fixtures prove the descriptor-level anchor
/// (<c>Reconciler.RichTextScrollAnchor.cs</c>) restores the user's real offset.
///
/// <para><b>Why the clamp is driven explicitly here.</b> WinUI's transient
/// <c>RemoveEmbeddedElements</c> pass is a multi-frame, compositor-scheduled
/// phenomenon. An in-process selftest window only runs layout via a synchronous,
/// atomic <see cref="Harness.Render"/> (<c>UpdateLayout</c>), which re-adds the inline
/// elements within the same pass so the shrink never commits a frame — the bug does
/// not surface headlessly. So instead of relying on the text engine, these fixtures
/// reproduce the exact ScrollViewer-observable contract the anchor guards: the
/// embedded inline UI transiently contributes zero height (<c>ScrollableHeight</c>
/// shrinks → the SV silently clamps <c>VerticalOffset</c>), then recovers. The anchor
/// is armed by the <b>real</b> descriptor code path (the inline button click drives a
/// genuine <c>UpdateRichTextBlocks</c>), so red-then-green is real: with the descriptor
/// hook disabled the offset stays clamped; with it enabled the offset is restored.</para>
/// </summary>
internal static class Issue487ScrollAnchorFixtures
{
    // Constrained viewport with tall content so there is real scrollable height,
    // and a large combined inline-UI height for the clamp to remove.
    private const double ViewportHeight = 240;
    private const double InlineHeight = 90;

    private sealed record Pt(double X, double Y);

    private static Pt[] BuildData(int n)
    {
        var d = new Pt[24];
        for (int i = 0; i < d.Length; i++)
            d[i] = new Pt(i, Math.Sin((i + n) * 0.5) * 5.0);
        return d;
    }

    private static Element BuildContent(int n, Action bump)
    {
        // Mirror the demo: embedded charts whose data depends on `n`, plus an
        // inline button that bumps `n`. Per issue #487 the mutation must originate
        // from inline UI inside a chart-bearing paragraph — an external button
        // driving the same state change does not reproduce the bug.
        var data = BuildData(n);
        var paras = new RichTextParagraph[6];
        for (int i = 0; i < paras.Length; i++)
        {
            Element inline = i == paras.Length - 2
                ? Button("MutateRun", bump).Width(220).Height(InlineHeight)
                : LineChart(data, d => d.X, d => d.Y)
                    .Width(300).Height(InlineHeight)
                    .Stroke("#4285F4").StrokeWidth(2);

            paras[i] = Paragraph(
                Run($"row {i} counter {n} "),
                InlineUI(inline),
                Run(" trailing narration that wraps to keep each paragraph tall enough."));
        }

        return ScrollViewer(
                (RichTextBlock(paras) with { IsTextSelectionEnabled = true }))
            .Height(ViewportHeight)
            .Set(sv =>
            {
                sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            });
    }

    // The live FrameworkElements embedded via InlineUIContainer — the elements
    // WinUI's ParagraphNode::Measure transiently detaches (RemoveEmbeddedElements)
    // during an inline-UI-bearing paragraph re-measure.
    private static global::System.Collections.Generic.List<FrameworkElement> CollectInlineUiChildren(RichTextBlock? rtb)
    {
        var result = new global::System.Collections.Generic.List<FrameworkElement>();
        if (rtb is null) return result;
        foreach (var p in rtb.Blocks.OfType<Microsoft.UI.Xaml.Documents.Paragraph>())
        {
            foreach (var fe in p.Inlines.OfType<Microsoft.UI.Xaml.Documents.InlineUIContainer>()
                         .Select(c => c.Child)
                         .OfType<FrameworkElement>())
                result.Add(fe);
        }
        return result;
    }

    // Drive one full inline-UI mutation cycle exactly as the WinUI bug presents it
    // to the ScrollViewer:
    //   1. Click the inline button → a real UpdateRichTextBlocks runs and arms the
    //      anchor on the ancestor ScrollViewer (the production code path).
    //   2. Detach ~half the embedded inline UI (Height = 0) and re-measure →
    //      ScrollableHeight shrinks below the parked offset → the SV silently
    //      clamps VerticalOffset (the documented silent coercion).
    //   3. Re-attach the inline UI and re-measure → ScrollableHeight recovers; the
    //      offset stays stuck at the clamped value unless the anchor restores it.
    //   4. Pump the dispatcher so the anchor's deferred ChangeView (mechanism #5)
    //      can run.
    private static async Task<bool> DriveInlineUiClampCycleAsync(
        ReactorHost host,
        Func<double> getOffset, Func<double> getScrollable,
        Action clickMutate, Func<RichTextBlock?> findRtb)
    {
        clickMutate();
        await host.WaitForIdleAsync();

        var rtb = findRtb();
        var inlineChildren = CollectInlineUiChildren(rtb);

        double beforeHeight = getScrollable();
        double beforeOffset = getOffset();

        int detachCount = Math.Max(1, inlineChildren.Count / 2);
        var savedHeights = new double[detachCount];
        for (int i = 0; i < detachCount; i++)
        {
            savedHeights[i] = inlineChildren[i].Height;
            inlineChildren[i].Height = 0;
        }
        // Neutralize any residual extent pin (issue #717) the production reconcile left
        // on the block from clickMutate(). The pinned MinHeight floor legitimately holds
        // the extent up for a couple of frames to PREVENT the real collapse — but this
        // fixture *simulates* the collapse (zeroing inline heights), a scenario the pin
        // is not meant to cover, so the lingering floor would race the simulated clamp.
        // The ScrollViewer variant clamps synchronously and usually wins that race; the
        // InteractionTracker-backed ScrollView clamps a pass or two later, landing inside
        // the pin's release window — hence the flake this clear removes.
        if (rtb is not null) rtb.MinHeight = 0;
        rtb?.InvalidateMeasure();
        await Harness.Render();

        // test-cov-b: confirm the simulated clamp actually fired — ScrollableHeight
        // shrank below the parked content AND the host clamped the offset down. If
        // this is ever false the red-then-green fixtures would be silently vacuous
        // (no clamp ⇒ nothing for the anchor to restore), so the callers assert it.
        //
        // Wait for the clamp to manifest rather than sampling after a single Render:
        // the classic ScrollViewer clamps synchronously inside the first layout pass
        // (so this returns on pass 1), but the modern ScrollView is
        // InteractionTracker-backed and re-clamps its position on a compositor pass
        // that can land a pass or two after the measure invalidation — headless CI
        // runners surface that lag. Polling until the clamp is observed makes the
        // detection deterministic across both scroll hosts and environments.
        Func<bool> clampSignature =
            () => getScrollable() < beforeHeight - 0.5 && getOffset() < beforeOffset - 0.5;
        await Harness.WaitFor(clampSignature, maxPasses: 30, perPassMs: 12);
        bool clampObserved = clampSignature();

        for (int i = 0; i < detachCount; i++)
            inlineChildren[i].Height = savedHeights[i];
        rtb?.InvalidateMeasure();
        await Harness.Render();

        // Let the anchor's dispatcher-deferred restore (mechanism #5) run to
        // completion. The anchor re-defers the ChangeView/ScrollTo on the dispatcher
        // until the offset lands at the intent, and each one only commits during a
        // host layout pass with a little compositor breathing room. Use the
        // harness's contention-proof convergence primitive — Harness.WaitFor runs a
        // full Render() (WaitForIdle → UpdateLayout → Low-yield → 16ms settle) per
        // pass and re-queries the live tree — so the restore reliably lands across
        // platforms/load instead of racing a fixed-cadence pump. When the fix is
        // absent the offset stays clamped, WaitFor exhausts its passes and returns
        // false, and the caller's offset assertion still fails (red preserved).
        await Harness.WaitFor(
            () => getOffset() + 0.5 >= getScrollable() && getScrollable() > InlineHeight,
            maxPasses: 60, perPassMs: 12);

        return clampObserved;
    }

    /// <summary>
    /// Scroll to the bottom, mutate a Run inside the inline-UI-bearing paragraphs,
    /// reproduce the WinUI inline-UI clamp, and assert the ScrollViewer's
    /// VerticalOffset is restored to the pre-mutation value rather than left clamped.
    /// </summary>
    internal class Issue487_ScrollOffsetRestoredAfterRunMutation(Harness h) : SelfTestFixtureBase(h)
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
            H.Check("Issue487_SVMounted", sv is not null);
            if (sv is null) return;


            // Scroll to the very bottom and let it commit.
            sv.ChangeView(null, sv.ScrollableHeight, null, disableAnimation: true);
            await Harness.Render();
            await Harness.Render();

            double scrollable = sv.ScrollableHeight;
            double preOffset = sv.VerticalOffset;

            H.Check("Issue487_HasScrollableHeight", scrollable > InlineHeight);
            H.Check("Issue487_ParkedAtBottom",
                Math.Abs(preOffset - scrollable) <= 2.0 && preOffset > InlineHeight);

            bool clamp = await DriveInlineUiClampCycleAsync(
                host,
                () => sv.VerticalOffset, () => sv.ScrollableHeight,
                () => H.ClickButton("MutateRun"),
                () => H.FindControl<RichTextBlock>(_ => true));

            H.Check("Issue487_ClampObserved", clamp);

            double postOffset = sv.VerticalOffset;

            // Core assertion: the offset is restored to the pre-mutation value.
            // With the descriptor hook disabled this lands far below preOffset
            // (clamped) — the red signal this fixture is designed to catch.
            H.Check("Issue487_OffsetRestoredAfterMutation",
                Math.Abs(postOffset - preOffset) <= 3.0);
        }
    }

    /// <summary>
    /// Slider-drag style: repeated rapid mutations must not accumulate drift — each
    /// mutation clamps and the anchor restores, so after many mutations the offset
    /// is still parked at the bottom.
    /// </summary>
    internal class Issue487_RepeatedMutationDoesNotDrift(Harness h) : SelfTestFixtureBase(h)
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
            H.Check("Issue487_Drift_SVMounted", sv is not null);
            if (sv is null) return;


            sv.ChangeView(null, sv.ScrollableHeight, null, disableAnimation: true);
            await Harness.Render();
            await Harness.Render();

            double bottom = sv.VerticalOffset;
            H.Check("Issue487_Drift_ParkedAtBottom",
                bottom > InlineHeight && Math.Abs(bottom - sv.ScrollableHeight) <= 2.0);

            // Fire a burst of mutation+clamp cycles, mimicking a slider drag that
            // re-rolls the inline charts on every tick. The guarantee is that the
            // offset does not accumulate drift: after the burst settles it is back
            // at the user's parked position, never walked far below it.
            bool anyClamp = false;
            for (int i = 0; i < 6; i++)
            {
                anyClamp |= await DriveInlineUiClampCycleAsync(
                    host,
                    () => sv.VerticalOffset, () => sv.ScrollableHeight,
                    () => H.ClickButton("MutateRun"),
                    () => H.FindControl<RichTextBlock>(_ => true));
            }

            H.Check("Issue487_Drift_ClampObserved", anyClamp);

            // No net drift: the final committed offset is the parked bottom, not a
            // clamped value accumulated across the burst. With the descriptor hook
            // disabled this stays clamped well below bottom — the red signal.
            H.Check("Issue487_Drift_FinalOffsetAtBottom",
                Math.Abs(sv.VerticalOffset - bottom) <= 4.0);
        }
    }

    // ── Modern ScrollView (InteractionTracker-backed) path ───────────────────

    private static Element BuildContentScrollView(int n, Action bump)
    {
        var data = BuildData(n);
        var paras = new RichTextParagraph[6];
        for (int i = 0; i < paras.Length; i++)
        {
            Element inline = i == paras.Length - 2
                ? Button("MutateRunSV", bump).Width(220).Height(InlineHeight)
                : LineChart(data, d => d.X, d => d.Y)
                    .Width(300).Height(InlineHeight)
                    .Stroke("#4285F4").StrokeWidth(2);

            paras[i] = Paragraph(
                Run($"row {i} counter {n} "),
                InlineUI(inline),
                Run(" trailing narration that wraps to keep each paragraph tall enough."));
        }

        return ScrollView(
                (RichTextBlock(paras) with { IsTextSelectionEnabled = true }))
            .Height(ViewportHeight);
    }

    /// <summary>
    /// test-cov-a — the same #487 contract on the modern
    /// <see cref="Microsoft.UI.Xaml.Controls.ScrollView"/> (the common forward-looking
    /// scroll host, which the classic-ScrollViewer fixtures above do not exercise):
    /// the anchor restores the offset after an inline-UI mutation clamps it.
    /// </summary>
    internal class Issue487_ScrollViewOffsetRestoredAfterRunMutation(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (n, setN) = ctx.UseState(0);
                return BuildContentScrollView(n, () => setN(n + 1));
            });

            await Harness.Render();

            var sv = H.FindControl<ScrollView>(_ => true);
            H.Check("Issue487_SV2Mounted", sv is not null);
            if (sv is null) return;

            var noAnim = new ScrollingScrollOptions(ScrollingAnimationMode.Disabled);
            await Harness.WaitFor(() => sv.ScrollableHeight > InlineHeight, maxPasses: 30, perPassMs: 12);
            sv.ScrollTo(0, sv.ScrollableHeight, noAnim);
            await Harness.WaitFor(
                () => sv.ScrollableHeight > InlineHeight
                      && sv.VerticalOffset + 0.5 >= sv.ScrollableHeight,
                maxPasses: 60, perPassMs: 12);

            double scrollable = sv.ScrollableHeight;
            double preOffset = sv.VerticalOffset;

            H.Check("Issue487_SV2HasScrollableHeight", scrollable > InlineHeight);
            H.Check("Issue487_SV2ParkedAtBottom",
                Math.Abs(preOffset - scrollable) <= 2.0 && preOffset > InlineHeight);

            bool clamp = await DriveInlineUiClampCycleAsync(
                host,
                () => sv.VerticalOffset, () => sv.ScrollableHeight,
                () => H.ClickButton("MutateRunSV"),
                () => H.FindControl<RichTextBlock>(_ => true));

            H.Check("Issue487_SV2ClampObserved", clamp);

            H.Check("Issue487_SV2OffsetRestoredAfterMutation",
                Math.Abs(sv.VerticalOffset - preOffset) <= 3.0);
        }
    }

    /// <summary>
    /// test-cov-c — the anchor must never fight a <b>genuine</b> user scroll. After a
    /// mutation arms the anchor and the inline-UI clamp drives a restore, the user
    /// scrolls to a new position; that committed scroll updates the intent through the
    /// ViewChanged path, so the anchor must honor it rather than yank the user back to
    /// the pre-mutation offset. This locks the contract that corr-a (re-reading the
    /// intent at restore-apply time instead of a stale captured target) protects.
    /// </summary>
    internal class Issue487_GenuineUserScrollAfterArmingNotFought(Harness h) : SelfTestFixtureBase(h)
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
            H.Check("Issue487_UserScroll_SVMounted", sv is not null);
            if (sv is null) return;

            sv.ChangeView(null, sv.ScrollableHeight, null, disableAnimation: true);
            await Harness.Render();
            await Harness.Render();

            double bottom = sv.VerticalOffset;
            H.Check("Issue487_UserScroll_ParkedAtBottom",
                bottom > InlineHeight && Math.Abs(bottom - sv.ScrollableHeight) <= 2.0);

            // Arm the anchor via a real mutation, then reproduce the clamp so a
            // restore (targeting the bottom intent) is genuinely in flight.
            H.ClickButton("MutateRun");
            await host.WaitForIdleAsync();

            var rtb = H.FindControl<RichTextBlock>(_ => true);
            var inlineChildren = CollectInlineUiChildren(rtb);
            int detachCount = Math.Max(1, inlineChildren.Count / 2);
            var savedHeights = new double[detachCount];
            for (int i = 0; i < detachCount; i++)
            {
                savedHeights[i] = inlineChildren[i].Height;
                inlineChildren[i].Height = 0;
            }
            // Drop any residual #717 extent pin so the simulated collapse isn't held up
            // by the pinned MinHeight floor (see DriveInlineUiClampCycleAsync).
            if (rtb is not null) rtb.MinHeight = 0;
            rtb?.InvalidateMeasure();
            await Harness.Render();
            for (int i = 0; i < detachCount; i++)
                inlineChildren[i].Height = savedHeights[i];
            rtb?.InvalidateMeasure();
            await Harness.Render();

            // The user now deliberately scrolls to a mid position and commits it.
            double mid = Math.Round(bottom * 0.35);
            sv.ChangeView(null, mid, null, disableAnimation: true);
            await Harness.Render();
            await Harness.Render();

            // Settle: the offset must converge to the user's chosen mid position and
            // stay there — not get dragged back toward the pre-mutation bottom.
            await Harness.WaitFor(
                () => Math.Abs(sv.VerticalOffset - mid) <= 6.0,
                maxPasses: 60, perPassMs: 12);

            H.Check("Issue487_UserScroll_HonorsUserPosition",
                Math.Abs(sv.VerticalOffset - mid) <= 6.0);
            H.Check("Issue487_UserScroll_NotYankedToBottom",
                bottom - sv.VerticalOffset > InlineHeight);
        }
    }
}
