using System.Runtime.CompilerServices;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression for the 0xC000027B (STATUS_STOWED_EXCEPTION) host crash that CI's Coverage
/// lanes hit in OptionalEchoStrandRegression, right after ControlledOptionalTextInputFamily.
///
/// <para><b>Mechanism.</b> <c>AutoSuggestBox</c> raises <c>TextChanged</c> from an internal
/// 150 ms timer, restarted whenever the text in its template TextBox changes. The timer keeps
/// running after the box leaves the tree, and it does not keep the box reachable. Once nothing
/// holds the box, full GCs can reclaim its managed side (the TextChanged delegate and the
/// <c>ReactorState</c>, which native code reaches only through reference-tracked CCWs) while
/// the native box and its timer live on. The tick then calls into collected managed state;
/// CsWinRT throws <see cref="NullReferenceException"/> into WinUI, which fail-fasts the
/// process. Reactor now holds the box from <c>Unloaded</c>, and from installing its handler,
/// until well after any tick it can still receive.</para>
///
/// <para><b>Forcing the order.</b> Each attempt arms the timer, drops every reference to the
/// box, and runs full GCs around UI ticks: the first GC lets XAML's reference tracker find the
/// box unreachable, the tick lets XAML run its cleanup, and the next GC reclaims what the box's
/// native side still points at. Without a hold, no attempt's tick reaches managed code alive:
/// each one either lands on collected state (the crash) or is lost with it. Of the orphaned
/// arms, the first three are the ways a tick gets armed in practice, and
/// <see cref="Arm.HeldOnlyByUnloaded"/> leaves only the hold taken on <c>Unloaded</c>. Two final
/// checks pin down the WinUI behaviour the design relies on (the timer is not armed while the
/// box is out of the tree) and that every hold ends.</para>
/// </summary>
internal static class AutoSuggestBoxCollectedStateTickFixture
{
    private enum Arm
    {
        /// <summary>Mounted empty with a callback; typing arms the timer.</summary>
        Typed,
        /// <summary>Mounted with text and a callback; the first layout copies Text into the
        /// fresh template TextBox, which arms the timer without changing Text.</summary>
        MountedWithText,
        /// <summary>Mounted with text but no callback; an update adds one after the copy.</summary>
        CallbackAddedLate,
        /// <summary>As <see cref="MountedWithText"/>, with the hold taken at mount dropped, so
        /// only the hold taken on Unloaded is left.</summary>
        HeldOnlyByUnloaded,
        /// <summary>Mounted empty, then Text set while in the tree. Not orphaned: the control
        /// for <see cref="TextSetAfterLeaving"/>.</summary>
        TextSetInTree,
        /// <summary>Taken out of the tree while still referenced, then Text set. Not orphaned:
        /// it checks that this arms no timer at all.</summary>
        TextSetAfterLeaving,
    }

    private static readonly Arm[] OrphanedArms =
        [Arm.Typed, Arm.MountedWithText, Arm.CallbackAddedLate, Arm.HeldOnlyByUnloaded];

    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        private const string Name = "AutoSuggestBoxCollectedStateTick";
        private const int Attempts = 2;

        // TextChanged ticks of any reason that reached managed code. The counting handler only
        // references this fixture, so subscribing it keeps no box alive.
        private int _ticks;

        public override async Task RunAsync()
        {
            TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxTextChangedEventArgs> countTick =
                (_, _) => Interlocked.Increment(ref _ticks);

            // Positive control: typing into the inner TextBox queues a UserInput-reason
            // TextChanged that reaches Reactor, and the tick counter, while the box is mounted.
            var mountedCallbacks = 0;
            var ticksBefore = Volatile.Read(ref _ticks);
            var mountedHost = await ArmAsync(H, Arm.Typed, () => mountedCallbacks++, countTick, orphan: false);
            H.Check(
                $"{Name}_TickReachesMountedBox",
                await Harness.WaitFor(() => mountedCallbacks > 0 && Volatile.Read(ref _ticks) > ticksBefore, maxPasses: 40, perPassMs: 20));
            await DisposeHostAsync(mountedHost);

            var unhandled = new List<string>();
            Microsoft.UI.Xaml.UnhandledExceptionEventHandler handler = (_, e) =>
            {
                unhandled.Add($"{e.Exception?.GetType().Name} 0x{e.Exception?.HResult:X8}: {e.Message}");
                // Report through the checks below instead of letting WinUI fail-fast the run.
                e.Handled = true;
            };

            var delivered = new Dictionary<Arm, int>();
            var typedCallbacks = 0;
            Application.Current.UnhandledException += handler;
            try
            {
                foreach (var arm in OrphanedArms)
                {
                    Action onTextChanged = arm == Arm.Typed ? () => typedCallbacks++ : static () => { };
                    delivered[arm] = await OrphanAndCollectAsync(arm, onTextChanged, countTick);
                }
            }
            finally
            {
                Application.Current.UnhandledException -= handler;
            }

            H.Check(
                $"{Name}_NoUnhandledException",
                unhandled.Count == 0,
                $"{unhandled.Count} of {OrphanedArms.Length * Attempts} attempt(s) threw into WinUI, first: {(unhandled.Count > 0 ? unhandled[0] : "")}");
            H.Check(
                $"{Name}_TypedTickReachesCallback",
                typedCallbacks == Attempts,
                $"typed: callback reached in {typedCallbacks} of {Attempts} attempt(s)");
            foreach (var arm in OrphanedArms)
            {
                H.Check(
                    $"{Name}_{arm}_TickDelivered",
                    delivered[arm] == Attempts,
                    $"{arm}: tick delivered in {delivered[arm]} of {Attempts} attempt(s)");
            }

            // The design relies on this: a Text change on a box that has left the tree arms no
            // timer, so no tick can be armed after Unloaded. The box stays referenced (by its
            // host) so that a tick, were one armed, would be counted, and the same probe on a
            // box in the tree shows that the window is long enough to count one.
            var inTreeTicks = await CountTicksAfterTextSetAsync(Arm.TextSetInTree, countTick);
            var afterLeavingTicks = await CountTicksAfterTextSetAsync(Arm.TextSetAfterLeaving, countTick);
            H.Check(
                $"{Name}_TextSetAfterLeavingArmsNoTick",
                inTreeTicks > 0 && afterLeavingTicks == 0,
                $"ticks after setting Text: {inTreeTicks} in the tree (control, expected > 0), {afterLeavingTicks} out of it (expected 0)");

            // Every hold ends. A release that never ran would root every AutoSuggestBox with a
            // callback for good while every check above still passed. Mounting a box takes a
            // hold, which shows that the state can be seen; well after the last hold expires,
            // the table must be empty and the release timer stopped.
            var whileMounted = await MountAndReadHoldStateAsync();
            var drained = await Harness.WaitFor(
                () => AutoSuggestBoxElement.TextChangedTickHoldStateForTests() == (0, false),
                maxPasses: 60, perPassMs: 100);
            var afterWait = AutoSuggestBoxElement.TextChangedTickHoldStateForTests();
            H.Check(
                $"{Name}_HoldsEnd",
                whileMounted.Held > 0 && whileMounted.ReleaseTimerRunning && drained,
                $"while mounted: {whileMounted.Held} held, release timer running: {whileMounted.ReleaseTimerRunning} (control); " +
                $"after waiting: {afterWait.Held} held, release timer running: {afterWait.ReleaseTimerRunning}");
        }

        private async Task<(int Held, bool ReleaseTimerRunning)> MountAndReadHoldStateAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ => VStack(AutoSuggestBox(Optional<string>.Unset, static _ => { })));
            await Harness.Render();
            var state = AutoSuggestBoxElement.TextChangedTickHoldStateForTests();
            await DisposeHostAsync(host);
            return state;
        }

        private async Task<int> CountTicksAfterTextSetAsync(
            Arm arm,
            TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxTextChangedEventArgs> countTick)
        {
            var ticksBefore = Volatile.Read(ref _ticks);
            var host = await ArmAsync(H, arm, static () => { }, countTick, orphan: false);
            // A fixed window rather than a wait, because one of the two claims is that nothing
            // happens.
            await Harness.Render(400);
            var ticks = Volatile.Read(ref _ticks) - ticksBefore;
            await DisposeHostAsync(host);
            return ticks;
        }

        private async Task DisposeHostAsync(ReactorHost? host)
        {
            host?.Dispose();
            H.SetContent(null);
            await Harness.Render();
        }

        private async Task<int> OrphanAndCollectAsync(
            Arm arm,
            Action onTextChanged,
            TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxTextChangedEventArgs> countTick)
        {
            var ticksBefore = Volatile.Read(ref _ticks);
            for (var attempt = 0; attempt < Attempts; attempt++)
            {
                await ArmAsync(H, arm, onTextChanged, countTick, orphan: true);

                GC.Collect();
                await Harness.Render();
                GC.Collect();
                await Harness.Render();
                GC.Collect();

                // Outlast the 150 ms timer.
                await Harness.Render(250);
            }
            return Volatile.Read(ref _ticks) - ticksBefore;
        }

        // Kept out of RunAsync's state machine so nothing in the fixture still references
        // the box, its host, or its inner TextBox once the box is orphaned. Returns the host
        // only when the box stays referenced.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<ReactorHost?> ArmAsync(
            Harness h,
            Arm arm,
            Action onTextChanged,
            TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxTextChangedEventArgs> countTick,
            bool orphan)
        {
            var text = arm is Arm.Typed or Arm.TextSetInTree or Arm.TextSetAfterLeaving
                ? Optional<string>.Unset
                : Optional<string>.Of("initial");
            Action? addCallback = null;
            var host = h.CreateHost();
            host.Mount(ctx =>
            {
                var (hasCallback, setHasCallback) = ctx.UseState(arm != Arm.CallbackAddedLate);
                addCallback = () => setHasCallback(true);
                return VStack(AutoSuggestBox(text, hasCallback ? _ => onTextChanged() : null));
            });
            // The first layout applies the template; with text, the box copies it into the
            // TextBox, which arms the timer.
            await Harness.Render();

            var box = h.FindControl<WinUI.AutoSuggestBox>(_ => true)
                ?? throw new InvalidOperationException("AutoSuggestBox was not mounted");
            box.TextChanged += countTick;

            switch (arm)
            {
                case Arm.Typed:
                    var inner = FindDescendant<WinUI.TextBox>(box)
                        ?? throw new InvalidOperationException("AutoSuggestBox's inner TextBox was not realized");
                    // A text change the box did not make itself is reported as UserInput. The
                    // box arms its timer when it sees the TextBox's own, queued, TextChanged,
                    // which the Render below delivers.
                    inner.Text = "typed-" + Environment.TickCount64;
                    await Harness.Render();
                    break;

                case Arm.CallbackAddedLate:
                    // The update installs Reactor's TextChanged handler while that tick is pending.
                    addCallback!();
                    await Harness.Render();
                    break;

                case Arm.HeldOnlyByUnloaded:
                    AutoSuggestBoxElement.ReleaseTextChangedTickHoldForTests(box);
                    break;

                case Arm.TextSetInTree:
                    box.Text = "set-in-tree";
                    break;

                case Arm.TextSetAfterLeaving:
                    // Out of the tree, but still referenced by the live host.
                    h.SetContent(null);
                    await Harness.Render();
                    box.Text = "set-after-leaving";
                    break;
            }

            if (!orphan) return host;
            host.Dispose();
            h.SetContent(null);
            return null;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T match) return match;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var found = FindDescendant<T>(VisualTreeHelper.GetChild(root, i));
                if (found is not null) return found;
            }
            return null;
        }
    }
}
