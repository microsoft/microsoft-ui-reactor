using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #681 (correctness) — cross-axis measure-cache staleness regression guard.
///
/// A flex child whose CONTENT changes its cross-axis size (wrapping text whose
/// line count grows while the longest word — i.e. the main-axis min-content —
/// is unchanged) must re-flow the panel. Yoga's per-leaf measurement cache is
/// keyed by the constraint (available size + sizing mode), NOT by content, so
/// an identical constraint with changed content can serve a STALE cached size
/// unless the leaf is marked dirty. FlexPanel.SyncYogaTree re-dirties every
/// child node every MeasureOverride (via the unconditional Display / MinWidth /
/// Margin setters), which clears that cache — so the new cross-axis size flows
/// through.
///
/// This bug was latent under the former #138 setter equality-guards (which made
/// those per-pass writes no-ops on unchanged values, leaving the leaf clean →
/// stale). PR #740 reverted the guards to unconditional dirty, fixing it. This
/// fixture locks the fixed behavior: if anyone re-introduces a "skip re-dirty on
/// unchanged value" scheme (the #742 danger zone), the wrapping-text height
/// below would freeze and this fixture fails.
/// </summary>
internal sealed class CrossAxisMeasureStalenessFixture(Harness h) : SelfTestFixtureBase(h)
{
    // Same longest word ("ww", length 2) in both strings, so the main-axis
    // min-content of the child is identical across the two renders; only the
    // line count — and therefore the cross-axis (height) — changes.
    private const string ShortText = "ww ww";
    private const string TallText = "ww ww ww ww ww ww ww ww ww ww ww ww ww ww";

    public override async Task RunAsync()
    {
        var host = H.CreateHost();

        // FlexRow → main axis is horizontal, cross axis is vertical (height).
        // The single child is a wrapping TextBlock with an explicit narrow
        // width, so its CONSTRAINT is identical across both renders while its
        // wrapped height grows with content. The row width is fixed; its height
        // is auto, so it tracks the child's (cross-axis) height.
        host.Mount(_ => BuildRow(ShortText));
        await Harness.Render();

        var row = H.FindControl<FlexPanel>(p => p.Direction == FlexDirection.Row);
        var text = H.FindText(ShortText);
        H.Check("CrossAxisStaleness_RowMounted", row is not null && text is not null);

        double shortRowHeight = row?.DesiredSize.Height ?? 0;
        double shortTextHeight = text?.ActualHeight ?? 0;

        // Re-render in place with the taller content. The reconciler reuses the
        // same TextBlock + FlexPanel controls (and thus the same Yoga node and
        // its measurement cache), so this exercises the cache-invalidation path
        // rather than building a fresh tree.
        host.Mount(_ => BuildRow(TallText));
        await Harness.Render();

        var rowAfter = H.FindControl<FlexPanel>(p => p.Direction == FlexDirection.Row);
        var textAfter = H.FindText(TallText);
        H.Check("CrossAxisStaleness_TallTextMounted", rowAfter is not null && textAfter is not null);

        double tallRowHeight = rowAfter?.DesiredSize.Height ?? 0;
        double tallTextHeight = textAfter?.ActualHeight ?? 0;

        Console.WriteLine($"# CrossAxisStaleness shortRow={shortRowHeight:F1} tallRow={tallRowHeight:F1} shortText={shortTextHeight:F1} tallText={tallTextHeight:F1}");

        // Sanity: the wrapping TextBlock itself actually grew taller (WinUI
        // re-measured the child correctly).
        H.Check(
            "CrossAxisStaleness_ChildTextGrewTaller",
            tallTextHeight > shortTextHeight + 1.0);

        // Core assertion: the panel's cross-axis (height) tracked the child's
        // new content height — no stale cached measurement was served.
        H.Check(
            "CrossAxisStaleness_PanelHeightTrackedContent",
            tallRowHeight > shortRowHeight + 1.0);

        // And the panel is at least as tall as its child (it did not clamp to a
        // stale, shorter cached height).
        H.Check(
            "CrossAxisStaleness_PanelNotStaleVsChild",
            tallRowHeight >= tallTextHeight - 2.0);

        H.SetContent(null);
        await Harness.Render();
    }

    private static Element BuildRow(string text) =>
        VStack(
            FlexRow(
                TextBlock(text)
                    .Width(60)
                    .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
            ).Width(200)
        );
}
