using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Locks in Reactor's contract for a declared <c>bool</c> that the NATIVE
/// control can also mutate (issue R7 — <c>InfoBar.IsOpen</c> after the user
/// dismisses the bar with its built-in ✕).
///
/// <para><b>The contract.</b> Such a value is <b>edge-triggered</b>: the element
/// declares a <i>transition</i>, not a mirror. Reactor writes the control only
/// when the declared value <i>changes</i>. App state is kept in sync with a
/// native dismissal by wiring the control's change callback
/// (<c>OnClosed</c>).</para>
///
/// <para><b>Why both halves are asserted.</b> The two failure modes point in
/// opposite directions, so no single assertion can pin the contract:</para>
/// <list type="bullet">
///   <item><c>RisingEdgeReopens</c> fails if the rising edge ever stops
///   writing — that is the recovery path a caller needs after a dismissal.</item>
///   <item><c>SameDeclaredValueDoesNotReopen</c> fails if the engine is ever
///   changed to re-assert the declared value against the live control. That
///   "fix" looks attractive until you notice <c>InfoBarElement.IsOpen</c>
///   defaults to <c>true</c>: it would make every InfoBar written without an
///   <c>OnClosed</c> handler undismissable, because the next unrelated
///   re-render would bring the bar back.</item>
/// </list>
///
/// <para><b>Element identity matters here.</b> Every re-render below passes a
/// <i>freshly constructed</i> element, because that is what a real render loop
/// produces — <c>InfoBar(...)</c> allocates a new record each pass. Reusing one
/// instance as both old and new would trip <c>Element.ShallowEquals</c>'s
/// <c>ReferenceEquals</c> fast path and skip the descriptor entirely, so these
/// checks would pass without ever exercising the prop entry.</para>
/// </summary>
internal static class IsOpenEdgeTriggeredFixtures
{
    private static readonly Action _noOp = static () => { };

    /// <summary>
    /// Teardown guard shared by the TeachingTip fixtures. Since issue #949 these tips
    /// genuinely <i>present</i>, and a presented light-dismiss overlay left behind would sit
    /// over whatever fixture runs next in this shared host process.
    ///
    /// <para>Note the explicit render passes: <c>IsOpen</c> flips synchronously, so a
    /// <c>WaitFor(() =&gt; !tip.IsOpen)</c> would satisfy its predicate on the first probe and
    /// pump the dispatcher zero times — the exit transition and popup teardown would still be
    /// in flight at unmount. Pump unconditionally instead.</para>
    /// </summary>
    private static async Task CloseAndSettle(Reconciler rec, WinUI.TeachingTip tip)
    {
        tip.IsOpen = false;
        for (int i = 0; i < 2; i++)
            await Harness.Render(25);
        rec.UnmountChild(tip);
    }

    /// <summary>
    /// Full mount → native dismissal → re-render matrix against a real
    /// <see cref="WinUI.InfoBar"/>.
    /// </summary>
    internal sealed class InfoBarEdgeTriggered(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int closed = 0;

            // A fresh instance per render, exactly like a real render pass.
            InfoBarElement Declared(bool isOpen) => new("Edge", "message")
            {
                IsOpen = isOpen,
                IsClosable = true,
                OnClosed = () => closed++,
            };

            var mounted = Declared(isOpen: true);
            if (rec.Mount(mounted, _noOp) is not WinUI.InfoBar bar)
            {
                H.Check("IsOpenEdge_InfoBar_Mounted", false);
                return;
            }

            parent.Children.Add(bar);
            await Harness.Render();
            H.Check("IsOpenEdge_InfoBar_MountAppliesDeclaredOpen", bar.IsOpen);
            H.Check("IsOpenEdge_InfoBar_MountDoesNotFireOnClosed", closed == 0);

            // The native ✕ — WinUI sets IsOpen = false on the live control and
            // raises Closed. Reactor's declared value is untouched, still true.
            bar.IsOpen = false;
            await Harness.Render();
            H.Check("IsOpenEdge_InfoBar_NativeDismissFiresOnClosedOnce", closed == 1);

            // CONTRACT HALF 1 — re-rendering the same declared value is not an
            // edge, so the dismissal stands. Fails if the engine is changed to
            // re-assert the declared value against the live control (which would
            // make every default-`true` InfoBar undismissable).
            var previous = mounted;
            for (int i = 0; i < 3; i++)
            {
                var next = Declared(isOpen: true);
                rec.UpdateChild(previous, next, bar, _noOp);
                await Harness.Render();
                previous = next;
            }
            H.Check("IsOpenEdge_InfoBar_SameDeclaredValueDoesNotReopen", !bar.IsOpen);
            H.Check("IsOpenEdge_InfoBar_SameDeclaredValueRaisesNoCallback", closed == 1);

            // The documented sync step: OnClosed -> setState(false). The control
            // is already closed, so this must be a silent no-op rather than a
            // second Closed event.
            var syncedClosed = Declared(isOpen: false);
            rec.UpdateChild(previous, syncedClosed, bar, _noOp);
            await Harness.Render();
            H.Check("IsOpenEdge_InfoBar_FallingEdgeOnClosedControlRaisesNoCallback", !bar.IsOpen && closed == 1);

            // CONTRACT HALF 2 — the rising edge re-opens. This is the recovery
            // path R7 claimed was impossible; fails if the edge write is lost.
            var reopened = Declared(isOpen: true);
            rec.UpdateChild(syncedClosed, reopened, bar, _noOp);
            await Harness.Render();
            H.Check("IsOpenEdge_InfoBar_RisingEdgeReopens", bar.IsOpen);
            H.Check("IsOpenEdge_InfoBar_RisingEdgeRaisesNoCallback", closed == 1);

            // A programmatic close from the OPEN state must still close the
            // control. The precondition is folded into the same check so this
            // cannot pass vacuously on an already-closed bar.
            var wasOpenBeforeClose = bar.IsOpen;
            rec.UpdateChild(reopened, Declared(isOpen: false), bar, _noOp);
            await Harness.Render();
            H.Check("IsOpenEdge_InfoBar_ProgrammaticCloseClosesOpenControl", wasOpenBeforeClose && !bar.IsOpen);

            rec.UnmountChild(bar);
            parent.Children.Clear();
        }
    }

    /// <summary>
    /// <see cref="WinUI.TeachingTip"/> shares <c>InfoBar</c>'s authoring shape
    /// (a declared <c>IsOpen</c> + a hand-coded <c>Closed</c> event), so the same
    /// edge contract must hold for it. This fixture pins that contract at the
    /// <c>IsOpen</c> property level for post-mount edges;
    /// <see cref="TeachingTipMountOpen"/> covers the mount path (issue #949).
    ///
    /// <para><b>Why there is no <c>OnClosed</c> oracle here</b> (the callback half is
    /// covered by <see cref="InfoBarEdgeTriggered"/> and by
    /// <see cref="TeachingTipMountOpen"/> instead): the edges below are driven back
    /// to back with no dwell between them, so the tip is repeatedly re-closed
    /// before its entrance transition has played out. A <c>Closed</c> count under
    /// those conditions measures animation timing, not the edge contract.</para>
    /// </summary>
    internal sealed class TeachingTipEdgeTriggered(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            static TeachingTipElement Declared(bool isOpen) => new("Edge") { IsOpen = isOpen };

            var mounted = Declared(isOpen: false);
            if (rec.Mount(mounted, _noOp) is not WinUI.TeachingTip tip)
            {
                H.Check("IsOpenEdge_TeachingTip_Mounted", false);
                return;
            }

            parent.Children.Add(tip);
            await Harness.Render();
            H.Check("IsOpenEdge_TeachingTip_MountClosed", !tip.IsOpen);

            // Rising edge opens. TeachingTip's open/close are animated, so settle
            // on the observed state rather than a fixed delay.
            var opened = Declared(isOpen: true);
            rec.UpdateChild(mounted, opened, tip, _noOp);
            H.Check("IsOpenEdge_TeachingTip_RisingEdgeOpens",
                await Harness.WaitFor(() => tip.IsOpen, maxPasses: 40, perPassMs: 25));

            // Native light-dismiss.
            tip.IsOpen = false;
            H.Check("IsOpenEdge_TeachingTip_NativeDismissSettles",
                await Harness.WaitFor(() => !tip.IsOpen, maxPasses: 40, perPassMs: 25));

            // Same declared value is not an edge — the dismissal stands. Several
            // re-renders, so a delayed re-open would still be caught.
            var previous = opened;
            for (int i = 0; i < 3; i++)
            {
                var next = Declared(isOpen: true);
                rec.UpdateChild(previous, next, tip, _noOp);
                await Harness.Render(25);
                previous = next;
            }
            H.Check("IsOpenEdge_TeachingTip_SameDeclaredValueDoesNotReopen", !tip.IsOpen);

            // …and the rising edge after a sync-down still re-opens.
            var syncedClosed = Declared(isOpen: false);
            rec.UpdateChild(previous, syncedClosed, tip, _noOp);
            await Harness.Render();
            var reopened = Declared(isOpen: true);
            rec.UpdateChild(syncedClosed, reopened, tip, _noOp);
            H.Check("IsOpenEdge_TeachingTip_RisingEdgeReopensAfterDismiss",
                await Harness.WaitFor(() => tip.IsOpen, maxPasses: 40, perPassMs: 25));

            // Teardown: close first and let it settle, so unmounting never leaves a live
            // overlay behind for the next fixture in this shared host process.
            rec.UpdateChild(reopened, Declared(isOpen: false), tip, _noOp);
            await CloseAndSettle(rec, tip);
            parent.Children.Clear();
        }
    }

    /// <summary>
    /// Issue #949 — a <see cref="WinUI.TeachingTip"/> whose <b>first</b> render declares
    /// <c>IsOpen: true</c> must actually present.
    ///
    /// <para><b>What used to break.</b> WinUI only holds a pending open on an
    /// <i>unparented</i> tip while nothing else is written to it — the next property write
    /// silently discards it. Reactor configures a control fully before its parent takes it,
    /// so the auto-mapped one-way <c>IsOpen</c> write was always followed by something
    /// (<c>PreferredPlacement</c>, setters, the content slots, common modifiers) and the
    /// declared open was lost. The write is now deferred to <c>Loaded</c>.</para>
    ///
    /// <para><b>Why these specific checks.</b> Each one fails if the deferral is removed or
    /// mis-scoped, and none can pass vacuously:</para>
    /// <list type="bullet">
    ///   <item><c>DeclaredOpenOnFirstRenderOpens</c> is the direct regression oracle — it
    ///   reads <c>false</c> under the old one-way entry.</item>
    ///   <item><c>PresentedTipRaisesClosedOnTeardown</c> stops the property flip from being
    ///   the whole story: WinUI raises <c>Closed</c> only for a tip that really presented, so
    ///   a "set the DP and hope" fix would not satisfy it.</item>
    ///   <item><c>DeclaredClosedStaysClosed</c> is the differential control — it fails if the
    ///   deferral ever opens a tip that did not ask to be open.</item>
    ///   <item><c>FallingEdgeBeforeLoadCancelsPendingOpen</c> fails if a cancelled arm is left
    ///   subscribed, which would pop a tip the app has already closed.</item>
    ///   <item><c>UnmountBeforeLoadCancelsPendingOpen</c> fails if an arm outlives the mount
    ///   that created it — the descriptor's unmount hook has to drop it.</item>
    ///   <item><c>RisingEdgeBeforeLoadOpensOnLoad</c> fails if the deferral only covers mount
    ///   and drops an edge that lands while the tip is still unparented.</item>
    ///   <item><c>DeclaredOpenWinsOverSetter</c> pins the documented setter-precedence
    ///   consequence so the XML doc cannot drift from reality.</item>
    /// </list>
    ///
    /// <para>Every render below passes a freshly constructed element, because that is what a
    /// real render loop produces — reusing one instance would trip
    /// <c>Element.ShallowEquals</c>'s <c>ReferenceEquals</c> fast path and skip the
    /// descriptor entirely.</para>
    /// </summary>
    internal sealed class TeachingTipMountOpen(Harness h) : SelfTestFixtureBase(h)
    {
        /// <summary>TeachingTip's entrance transition is animated. The presentation oracles wait
        /// on a real signal (an open popup) rather than assuming the dwell was long enough; the
        /// dwell that remains covers only the entrance animation, which must finish before a
        /// close raises <c>Closed</c>.</summary>
        private const int PresentDwellMs = 300;

        /// <summary>Settle window for the checks that assert a tip stays CLOSED. The positive
        /// path opens within a pass or two of <c>Loaded</c>, so this only has to outlast that —
        /// the full entrance dwell would be pure wall-clock cost in a suite that runs under a
        /// process-wide time cap.</summary>
        private const int StayClosedWindowMs = 75;

        /// <summary>Number of popups open on the control's XamlRoot, or <c>null</c> when the
        /// count could not be taken. A presented TeachingTip lives in one, so a rise from a
        /// pre-open baseline is a genuine presentation signal — unlike <c>IsOpen</c>, which only
        /// says the DP write stuck.
        ///
        /// <para>Returns <c>int?</c> rather than <c>0</c> on an unavailable <c>XamlRoot</c> so
        /// "I could not measure" cannot be mistaken for "I measured and found none". A zero
        /// there would silently lower the baseline, making the comparison below <i>easier</i> to
        /// satisfy — a leftover popup from an earlier fixture would then satisfy it on its own,
        /// with no tip presented. Callers must treat <c>null</c> as a failed check, never as
        /// a value (issue #992's fail-open class).</para></summary>
        private static int? OpenPopupCount(Microsoft.UI.Xaml.FrameworkElement fe)
        {
            var root = fe.XamlRoot;
            if (root is null) return null;
            return Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count;
        }

        public override async Task RunAsync()
        {
            await MountOpenPresents();
            await MountClosedStaysClosed();
            await FallingEdgeBeforeLoadCancelsPendingOpen();
            await UnmountBeforeLoadCancelsPendingOpen();
            await RisingEdgeBeforeLoadOpensOnLoad();
            await SetterCannotOverrideDeclaredMountOpen();
        }

        private async Task MountOpenPresents()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int closed = 0;
            TeachingTipElement Declared(bool isOpen) =>
                new("Mount open", "Declared open on the very first render.")
                {
                    IsOpen = isOpen,
                    OnClosed = () => closed++,
                };

            var mounted = Declared(isOpen: true);
            if (rec.Mount(mounted, _noOp) is not WinUI.TeachingTip tip)
            {
                H.Check("MountOpen_TeachingTip_PresentsFixtureMounted", false);
                return;
            }

            // Parent AFTER mount — the ordering the reconciler itself uses, and the ordering
            // that used to lose the open. Pump one pass first so XamlRoot is populated before
            // the baseline is taken; an unmeasurable baseline is asserted below rather than
            // silently treated as zero.
            await Harness.Render();
            var popupsBeforeOpen = OpenPopupCount(parent);
            H.Check("MountOpen_TeachingTip_PopupBaselineIsMeasurable", popupsBeforeOpen.HasValue);
            parent.Children.Add(tip);

            H.Check("MountOpen_TeachingTip_DeclaredOpenOnFirstRenderOpens",
                await Harness.WaitFor(() => tip.IsOpen, maxPasses: 40, perPassMs: 25));
            H.Check("MountOpen_TeachingTip_MountDoesNotFireOnClosed", closed == 0);

            // Wait for a REAL presentation signal rather than a blind animation dwell: a
            // presented tip lives in a popup on the XamlRoot. Counting from a pre-open baseline
            // keeps this immune to a popup left over from an earlier fixture — but only because
            // an unmeasurable count is null rather than zero, so it can never lower the bar.
            var presented = await Harness.WaitFor(
                () =>
                {
                    var now = OpenPopupCount(tip);
                    return popupsBeforeOpen.HasValue && now.HasValue && now.Value > popupsBeforeOpen.Value;
                },
                maxPasses: 60, perPassMs: 25);
            H.Check("MountOpen_TeachingTip_DeclaredOpenOnFirstRenderPresentsAPopup", presented);

            // WinUI raises Closed on the way down only for a tip that genuinely presented AND
            // finished its entrance transition — closing mid-animation silently skips the event.
            // The dwell now covers only the animation, because presentation itself was waited
            // for above rather than assumed.
            await Harness.Render(PresentDwellMs);
            var wasOpenBeforeClose = tip.IsOpen;
            rec.UpdateChild(mounted, Declared(isOpen: false), tip, _noOp);
            var settled = await Harness.WaitFor(() => !tip.IsOpen && closed > 0, maxPasses: 60, perPassMs: 25);
            H.Check("MountOpen_TeachingTip_PresentedTipRaisesClosedOnTeardown",
                wasOpenBeforeClose && settled && closed == 1);

            await CloseAndSettle(rec, tip);
            parent.Children.Clear();
        }

        private async Task MountClosedStaysClosed()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            int closed = 0;
            var mounted = new TeachingTipElement("Mount closed", "Declared closed.")
            {
                IsOpen = false,
                OnClosed = () => closed++,
            };

            if (rec.Mount(mounted, _noOp) is not WinUI.TeachingTip tip)
            {
                H.Check("MountOpen_TeachingTip_StaysClosedFixtureMounted", false);
                return;
            }

            parent.Children.Add(tip);
            await Harness.Render(StayClosedWindowMs);

            H.Check("MountOpen_TeachingTip_DeclaredClosedStaysClosed", !tip.IsOpen && closed == 0);

            await CloseAndSettle(rec, tip);
            parent.Children.Clear();
        }

        private async Task FallingEdgeBeforeLoadCancelsPendingOpen()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            static TeachingTipElement Declared(bool isOpen) =>
                new("Cancelled before load") { IsOpen = isOpen };

            var mounted = Declared(isOpen: true);
            if (rec.Mount(mounted, _noOp) is not WinUI.TeachingTip tip)
            {
                H.Check("MountOpen_TeachingTip_CancelFixtureMounted", false);
                return;
            }

            // The falling edge lands while the tip is still unparented, i.e. before the
            // deferred open could run. The app's latest word must win.
            rec.UpdateChild(mounted, Declared(isOpen: false), tip, _noOp);

            parent.Children.Add(tip);
            await Harness.Render(StayClosedWindowMs);

            H.Check("MountOpen_TeachingTip_FallingEdgeBeforeLoadCancelsPendingOpen", !tip.IsOpen);

            await CloseAndSettle(rec, tip);
            parent.Children.Clear();
        }

        private async Task UnmountBeforeLoadCancelsPendingOpen()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            var mounted = new TeachingTipElement("Unmounted before load") { IsOpen = true };
            if (rec.Mount(mounted, _noOp) is not WinUI.TeachingTip tip)
            {
                H.Check("MountOpen_TeachingTip_UnmountFixtureMounted", false);
                return;
            }

            // Unmounted while still unparented, i.e. before the deferred open could run. The arm
            // must not outlive the mount that created it: if the control is later put back into a
            // live tree — an app holding it through .Set, a host re-attaching it — it must stay
            // closed rather than pop open on behalf of a mount that no longer exists.
            rec.UnmountChild(tip);

            parent.Children.Add(tip);
            await Harness.Render(StayClosedWindowMs);

            H.Check("MountOpen_TeachingTip_UnmountBeforeLoadCancelsPendingOpen", !tip.IsOpen);

            tip.IsOpen = false;
            parent.Children.Clear();
        }

        private async Task RisingEdgeBeforeLoadOpensOnLoad()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            // The PreferredPlacement change is load-bearing, not decoration. It is auto-mapped,
            // and the generator chains the auto-mapped entries AFTER the ones Customize adds,
            // so its write lands after IsOpen's in the same update — reproducing the exact
            // "a later write on an unparented tip discards the pending open" trap the issue's
            // descriptor bisect pinned. Without it the rising edge is effectively the last
            // write of the pass and would survive with no deferral, making this vacuous.
            //
            // OnClosed is load-bearing too, for a different reason. This path arms the deferral
            // via GetOrCreateControlEventPayload AFTER the .HandCodedEvent entries have already
            // wired ClosedTrampoline into the same payload box. That API replaces the box on a
            // HandlerType mismatch, so a regression there would silently drop the user's
            // callback. Counting Closed makes that failure visible instead of invisible.
            int closed = 0;
            TeachingTipElement Declared(bool isOpen, WinUI.TeachingTipPlacementMode placement) =>
                new("Opened before load")
                {
                    IsOpen = isOpen,
                    PreferredPlacement = placement,
                    OnClosed = () => closed++,
                };

            var mounted = Declared(isOpen: false, WinUI.TeachingTipPlacementMode.Auto);
            if (rec.Mount(mounted, _noOp) is not WinUI.TeachingTip tip)
            {
                H.Check("MountOpen_TeachingTip_RisingEdgeFixtureMounted", false);
                return;
            }

            // Rising edge before the tip is ever parented — the same "unparented write is
            // discarded" trap as mount, so it needs the same deferral.
            var opened = Declared(isOpen: true, WinUI.TeachingTipPlacementMode.Bottom);
            rec.UpdateChild(mounted, opened, tip, _noOp);

            parent.Children.Add(tip);
            H.Check("MountOpen_TeachingTip_RisingEdgeBeforeLoadOpensOnLoad",
                await Harness.WaitFor(() => tip.IsOpen, maxPasses: 40, perPassMs: 25));

            // Let the entrance transition finish, then close through the declared value: Closed
            // must still reach the callback that was wired before the arm touched the payload.
            await Harness.Render(PresentDwellMs);
            rec.UpdateChild(opened, Declared(isOpen: false, WinUI.TeachingTipPlacementMode.Bottom), tip, _noOp);
            var closedFired = await Harness.WaitFor(() => closed > 0, maxPasses: 60, perPassMs: 25);
            H.Check("MountOpen_TeachingTip_ArmingDoesNotClobberTheClosedTrampoline", closedFired);

            await CloseAndSettle(rec, tip);
            parent.Children.Clear();
        }

        /// <summary>
        /// Pins the documented consequence of deferring the mount-time open: the write lands
        /// after the descriptor's setter pass, so a <c>.Set(t =&gt; t.IsOpen = false)</c> cannot
        /// override a declared <c>true</c> the way setters normally win. Asserting it here means
        /// the XML doc on <c>TeachingTipElement.IsOpen</c> cannot silently drift from reality.
        /// </summary>
        private async Task SetterCannotOverrideDeclaredMountOpen()
        {
            var rec = new Reconciler();
            var parent = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
            H.SetContent(parent);

            var mounted = new TeachingTipElement("Setter loses to declared open")
            {
                IsOpen = true,
            }.Set(t => t.IsOpen = false);

            if (rec.Mount(mounted, _noOp) is not WinUI.TeachingTip tip)
            {
                H.Check("MountOpen_TeachingTip_SetterFixtureMounted", false);
                return;
            }

            parent.Children.Add(tip);

            H.Check("MountOpen_TeachingTip_DeclaredOpenWinsOverSetter",
                await Harness.WaitFor(() => tip.IsOpen, maxPasses: 40, perPassMs: 25));

            await CloseAndSettle(rec, tip);
            parent.Children.Clear();
        }
    }
}
