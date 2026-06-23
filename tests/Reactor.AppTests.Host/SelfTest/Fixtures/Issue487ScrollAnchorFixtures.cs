using System;
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
        foreach (var block in rtb.Blocks)
        {
            if (block is not Microsoft.UI.Xaml.Documents.Paragraph p) continue;
            foreach (var inline in p.Inlines)
                if (inline is Microsoft.UI.Xaml.Documents.InlineUIContainer c && c.Child is FrameworkElement fe)
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
    private static async Task DriveInlineUiClampCycleAsync(
        ReactorHost host, ScrollViewer sv, Action clickMutate, Func<RichTextBlock?> findRtb)
    {
        clickMutate();
        await host.WaitForIdleAsync();

        var rtb = findRtb();
        var inlineChildren = CollectInlineUiChildren(rtb);

        int detachCount = Math.Max(1, inlineChildren.Count / 2);
        var savedHeights = new double[detachCount];
        for (int i = 0; i < detachCount; i++)
        {
            savedHeights[i] = inlineChildren[i].Height;
            inlineChildren[i].Height = 0;
        }
        rtb?.InvalidateMeasure();
        await Harness.Render();

        for (int i = 0; i < detachCount; i++)
            inlineChildren[i].Height = savedHeights[i];
        rtb?.InvalidateMeasure();
        await Harness.Render();

        // Let the anchor's dispatcher-deferred restore (mechanism #5) run to
        // completion. The anchor re-defers the ChangeView on the dispatcher until
        // the offset lands at the intent, and each ChangeView only commits during a
        // ScrollViewer layout pass with a little compositor breathing room. Use the
        // harness's contention-proof convergence primitive — Harness.WaitFor runs a
        // full Render() (WaitForIdle → UpdateLayout → Low-yield → 16ms settle) per
        // pass and re-queries the live tree — so the restore reliably lands across
        // platforms/load instead of racing a fixed-cadence pump. When the fix is
        // absent the offset stays clamped, WaitFor exhausts its passes and returns
        // false, and the caller's offset assertion still fails (red preserved).
        await Harness.WaitFor(
            () => sv.VerticalOffset + 0.5 >= sv.ScrollableHeight && sv.ScrollableHeight > InlineHeight,
            maxPasses: 60, perPassMs: 12);
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

            await DriveInlineUiClampCycleAsync(
                host, sv,
                () => H.ClickButton("MutateRun"),
                () => H.FindControl<RichTextBlock>(_ => true));

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
            for (int i = 0; i < 6; i++)
            {
                await DriveInlineUiClampCycleAsync(
                    host, sv,
                    () => H.ClickButton("MutateRun"),
                    () => H.FindControl<RichTextBlock>(_ => true));
            }

            // No net drift: the final committed offset is the parked bottom, not a
            // clamped value accumulated across the burst. With the descriptor hook
            // disabled this stays clamped well below bottom — the red signal.
            H.Check("Issue487_Drift_FinalOffsetAtBottom",
                Math.Abs(sv.VerticalOffset - bottom) <= 4.0);
        }
    }
}
