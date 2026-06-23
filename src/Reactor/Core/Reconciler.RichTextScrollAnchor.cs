using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

// AI-HINT: Reconciler.RichTextScrollAnchor.cs — issue #487 scroll-offset
// preservation for RichTextBlock + inline UI inside a ScrollViewer/ScrollView.
//
// Mutating any Run.Text inside a paragraph that hosts an InlineUIContainer makes
// WinUI's text engine re-measure the paragraph from scratch. ParagraphNode::Measure
// calls RemoveEmbeddedElements() and reports desiredSize=0 for one layout pass, so
// the RichTextBlock transiently shrinks, the enclosing ScrollViewer/ScrollView
// silently clamps VerticalOffset down to the (smaller) ScrollableHeight, and then
// never restores it once the embedded elements re-attach. The user perceives this
// as "the scroll position jumps to the top whenever I touch an inline control".
//
// Reactor's incremental UpdateRichTextBlocks is correct (no remount). The fix here
// is invisible to authors: ScrollViewer(RichTextBlock(...)) "Just Works". Before a
// RichTextBlock that hosts inline UI is mutated we arm an anchor on the nearest
// ancestor scroll host; after layout settles we restore the user's real offset if
// the content recovered. Five mechanisms — learned from the hand-rolled prototype —
// are ALL load-bearing (see the numbered notes inline below).
public sealed partial class Reconciler
{
    // Mechanism #1 — per-scroll-host state keyed off the host instance via an
    // attached DP (matching ReactorAttached.StateProperty), NOT a captured
    // closure local (which would be reset on every render) and NOT a
    // ConditionalWeakTable (whose GC timing is unreliable for layout state).
    private sealed class RichTextScrollAnchorState
    {
        public bool Wired;            // Mechanism #2 — one-time wiring guard.
        public bool Armed;            // Set just before a mutation; gates restore.
        public bool RestorePending;   // Single in-flight deferred restore.
        public bool SawClamp;         // True once we've observed the clamp (drift) this arm.
        public int RestoreAttempts;   // Bounded self-retry counter (mechanism #5).
        public int PreClampWaits;     // Bounded passes spent armed-but-not-yet-clamped.
        public double Intended = double.NaN;            // User's last committed offset.
        public double LastScrollableHeight = double.NaN; // Full (pre-clamp) content height.
    }

    private static readonly DependencyProperty s_richTextScrollAnchorProperty =
        DependencyProperty.RegisterAttached(
            "ReactorRichTextScrollAnchor",
            typeof(RichTextScrollAnchorState),
            typeof(Reconciler),
            new PropertyMetadata(null));

    // Sub-pixel slack so layout rounding doesn't masquerade as a real offset
    // change or a content-height shrink/recovery.
    private const double ScrollAnchorEpsilon = 0.5;

    // Upper bound on dispatcher-deferred restore attempts. A single ChangeView is
    // occasionally dropped while the host drains its in-flight layout pass, so the
    // restore re-defers itself; the (generous) cap only exists to stop a
    // pathological spin if the intent can never be reached — the normal exit is the
    // no-drift branch once the offset lands (or a genuine user scroll updates the
    // intent so it is no longer "drifted").
    private const int MaxRestoreAttempts = 64;

    // Upper bound on how many armed-but-not-yet-clamped layout passes we tolerate
    // before disarming. In production the clamp materializes in the very first
    // post-arm layout pass, so this is effectively never reached; it only exists so
    // an arm that never sees a clamp (e.g. the mutation didn't actually shrink the
    // block) eventually releases instead of staying armed forever.
    private const int MaxPreClampWaits = 64;

    private static RichTextScrollAnchorState GetAnchorState(FrameworkElement host)
    {
        if (host.GetValue(s_richTextScrollAnchorProperty) is RichTextScrollAnchorState s)
            return s;
        s = new RichTextScrollAnchorState();
        host.SetValue(s_richTextScrollAnchorProperty, s);
        return s;
    }

    /// <summary>
    /// Issue #487 — invoked from <see cref="UpdateRichTextBlocks"/> immediately
    /// before the document is mutated. If the block hosts any inline UI and lives
    /// inside a <see cref="WinUI.ScrollViewer"/>/<see cref="WinUI.ScrollView"/>,
    /// arm a scroll anchor on that host so the silent offset clamp WinUI performs
    /// during the inline-UI re-measure is restored once layout settles.
    /// </summary>
    internal void PreserveScrollAroundInlineUiMutation(WinUI.RichTextBlock rtb, RichTextBlockElement next)
    {
        // The clamp only happens for blocks that actually host embedded
        // UIElements — pure text/hyperlink/linebreak mutations don't re-measure
        // an InlineUIContainer, so skip the ancestor walk entirely for them.
        if (!HasInlineUi(next)) return;

        switch (FindAncestorScrollHost(rtb))
        {
            case WinUI.ScrollViewer sv: ArmScrollViewer(sv); break;
            case WinUI.ScrollView sv: ArmScrollView(sv); break;
        }
    }

    private static bool HasInlineUi(RichTextBlockElement el)
    {
        if (el.Paragraphs is null) return false;
        foreach (var para in el.Paragraphs)
        {
            foreach (var inline in para.Inlines)
                if (inline is RichTextInlineUIContainer) return true;
        }
        return false;
    }

    private static FrameworkElement? FindAncestorScrollHost(DependencyObject start)
    {
        var cur = VisualTreeHelper.GetParent(start);
        while (cur is not null)
        {
            if (cur is WinUI.ScrollViewer or WinUI.ScrollView)
                return (FrameworkElement)cur;
            cur = VisualTreeHelper.GetParent(cur);
        }
        return null;
    }

    // ── Shared anchor algorithm ──────────────────────────────────────────────

    // Mechanism #4 — clamp-signature filter. The silent clamp DOES raise a
    // committed ViewChanged carrying the clamped offset. Recording it would
    // overwrite the user's real intent with the clamped value and the restore
    // would never fire. Detect the clamp (ScrollableHeight decreased AND
    // VerticalOffset pinned to the new max AND previous intent exceeded that new
    // max) and refuse to record it.
    private static void RecordCommittedOffset(RichTextScrollAnchorState st, double verticalOffset, double scrollableHeight)
    {
        bool isClamp =
            !double.IsNaN(st.LastScrollableHeight)
            && scrollableHeight < st.LastScrollableHeight - ScrollAnchorEpsilon
            && Math.Abs(verticalOffset - scrollableHeight) <= ScrollAnchorEpsilon
            && !double.IsNaN(st.Intended)
            && st.Intended > scrollableHeight + ScrollAnchorEpsilon;

        if (isClamp) return;

        st.Intended = verticalOffset;
        st.LastScrollableHeight = scrollableHeight;
    }

    // Mechanism #5 — restore evaluation, run from LayoutUpdated. While the inline
    // UI is detached the content is still short (scrollableHeight < intended), so
    // we wait. Once it recovers enough to hold the intended offset but the live
    // offset is still clamped below it, defer a ChangeView/ScrollTo back to the
    // intended offset (a synchronous call here is silently dropped while the host
    // drains its in-flight layout pass). The restore stays armed and re-defers on
    // each subsequent LayoutUpdated until the offset actually lands at the intended
    // value — a single deferred ChangeView is itself occasionally dropped while the
    // host is mid-pass, so one-shot disarming would leave the offset stuck at the
    // clamped value. We only disarm once the offset has recovered with no drift, so
    // we never fight a later genuine user scroll (a real user scroll updates the
    // intent through the committed-ViewChanged path and lands here as "no drift").
    //
    // Clamp-observed gate: we arm BEFORE the document mutation, but the silent clamp
    // only materializes once WinUI re-measures the inline UI in a later layout pass.
    // A no-drift LayoutUpdated can fire in that window (offset still == intent,
    // content not yet shrunk). Disarming on it would release the anchor a beat before
    // the clamp lands, and the clamp would then go uncorrected. So we refuse to
    // disarm-on-no-drift until we've actually observed the clamp (a drift) at least
    // once this arm; until then a no-drift pass just waits (bounded by
    // MaxPreClampWaits so an arm whose mutation never shrank anything still releases).
    private static void EvaluateRestore(
        RichTextScrollAnchorState st,
        Func<(double offset, double scrollableHeight)> read,
        DispatcherQueue dispatcher,
        Action<double> applyOffset)
    {
        if (!st.Armed || double.IsNaN(st.Intended)) return;
        if (st.RestorePending) return;

        var (verticalOffset, scrollableHeight) = read();

        bool recovered = scrollableHeight + ScrollAnchorEpsilon >= st.Intended;
        if (!recovered) return; // still mid re-measure — keep waiting.

        bool drifted = verticalOffset + ScrollAnchorEpsilon < st.Intended;
        if (!drifted)
        {
            if (!st.SawClamp)
            {
                // Armed but the clamp hasn't materialized yet — don't disarm into the
                // pre-clamp window. Wait a bounded number of passes for it to appear.
                if (++st.PreClampWaits >= MaxPreClampWaits)
                {
                    st.Armed = false;
                    st.PreClampWaits = 0;
                }
                return;
            }

            // Recovered with the offset already correct after the clamp was handled —
            // disarm so we never fight a later genuine user scroll, and reset state.
            st.Armed = false;
            st.RestoreAttempts = 0;
            st.PreClampWaits = 0;
            st.SawClamp = false;
            return;
        }

        // Observed the clamp (or a still-clamped offset) — from here disarm-on-no-drift
        // is allowed because we know the correction is genuine, not the pre-clamp window.
        st.SawClamp = true;

        if (st.RestoreAttempts >= MaxRestoreAttempts)
        {
            // Intent unreachable after the retry budget (e.g. the content genuinely
            // shrank below the old intent and will never hold it again) — give up
            // rather than fight the user indefinitely. This budget is the intentional
            // backstop for the permanent-shrink case (corr-b): a clamp we can never
            // satisfy still self-disarms here instead of staying armed forever.
            st.Armed = false;
            st.RestoreAttempts = 0;
            st.PreClampWaits = 0;
            st.SawClamp = false;
            return;
        }

        st.RestorePending = true;
        st.RestoreAttempts++;
        if (!dispatcher.TryEnqueue(() =>
            {
                st.RestorePending = false;
                // corr-a: re-read the intent at apply time rather than capturing it
                // at enqueue time. A genuine user scroll that commits between arming
                // and this deferred callback updates st.Intended through the
                // committed-ViewChanged path; applying a stale captured offset would
                // yank the user back and violate the "never fight a real scroll"
                // contract. If the anchor was disarmed in the interim, skip the write.
                if (st.Armed && !double.IsNaN(st.Intended))
                    applyOffset(st.Intended);
                // Self-retry independent of LayoutUpdated: a live window raises
                // LayoutUpdated every frame, but under load (or once layout goes
                // quiescent) it may not, and a single ChangeView can be dropped
                // mid-pass. Re-evaluate on a later tick until the offset lands at
                // the intent or the retry budget is exhausted; the no-drift branch
                // above disarms on success.
                dispatcher.TryEnqueue(
                    DispatcherQueuePriority.Low,
                    () => EvaluateRestore(st, read, dispatcher, applyOffset));
            }))
        {
            // Dispatcher rejected the enqueue (host tearing down) — don't strand
            // the guard flags.
            st.RestorePending = false;
            st.Armed = false;
            st.RestoreAttempts = 0;
            st.PreClampWaits = 0;
            st.SawClamp = false;
        }
    }

    // ── ScrollViewer (classic) ───────────────────────────────────────────────

    private void ArmScrollViewer(WinUI.ScrollViewer sv)
    {
        var st = GetAnchorState(sv);
        if (!st.Wired)
        {
            // Mechanism #3 — capture the user's last *committed* offset
            // (IsIntermediate == false) so mid-drag intermediate frames never
            // pollute the intent we restore to.
            sv.ViewChanged += static (s, e) =>
            {
                if (s is not WinUI.ScrollViewer host) return;
                if (e.IsIntermediate) return;
                RecordCommittedOffset(GetAnchorState(host), host.VerticalOffset, host.ScrollableHeight);
            };
            sv.LayoutUpdated += (_, _) =>
            {
                var s2 = GetAnchorState(sv);
                EvaluateRestore(
                    s2, () => (sv.VerticalOffset, sv.ScrollableHeight), sv.DispatcherQueue,
                    target => sv.ChangeView(null, target, null, disableAnimation: true));
            };
            st.Wired = true;
        }

        // Seed the intent from the current (pre-mutation) committed offset if the
        // user hasn't scrolled since wiring — the descriptor runs synchronously
        // before WinUI re-measures, so VerticalOffset is still the real offset.
        if (double.IsNaN(st.Intended))
        {
            st.Intended = sv.VerticalOffset;
            st.LastScrollableHeight = sv.ScrollableHeight;
        }
        st.RestoreAttempts = 0;
        st.PreClampWaits = 0;
        st.SawClamp = false;
        st.Armed = true;
    }

    // ── ScrollView (modern, InteractionTracker-backed) ───────────────────────

    private void ArmScrollView(WinUI.ScrollView sv)
    {
        var st = GetAnchorState(sv);
        if (!st.Wired)
        {
            // ScrollView's ViewChanged carries no intermediate flag; the
            // clamp-signature filter still protects the recorded intent.
            sv.ViewChanged += (s, _) =>
            {
                if (s is not WinUI.ScrollView host) return;
                RecordCommittedOffset(GetAnchorState(host), host.VerticalOffset, host.ScrollableHeight);
            };
            sv.LayoutUpdated += (_, _) =>
            {
                var s2 = GetAnchorState(sv);
                EvaluateRestore(
                    s2, () => (sv.VerticalOffset, sv.ScrollableHeight), sv.DispatcherQueue,
                    target => sv.ScrollTo(
                        sv.HorizontalOffset, target,
                        new WinUI.ScrollingScrollOptions(WinUI.ScrollingAnimationMode.Disabled)));
            };
            st.Wired = true;
        }

        if (double.IsNaN(st.Intended))
        {
            st.Intended = sv.VerticalOffset;
            st.LastScrollableHeight = sv.ScrollableHeight;
        }
        st.RestoreAttempts = 0;
        st.PreClampWaits = 0;
        st.SawClamp = false;
        st.Armed = true;
    }

    // corr-c: invoked from ElementPool.CleanElement when a pooled ScrollViewer/
    // ScrollView is returned, so a recycled host can't inherit a prior renter's
    // anchor intent (the attached-DP state survives Content = null otherwise).
    // Same pool-contamination class as issue #162 — clear the attached state on
    // return alongside the other ClearValue resets.
    internal static void ClearRichTextScrollAnchor(FrameworkElement host)
        => host.ClearValue(s_richTextScrollAnchorProperty);
}
