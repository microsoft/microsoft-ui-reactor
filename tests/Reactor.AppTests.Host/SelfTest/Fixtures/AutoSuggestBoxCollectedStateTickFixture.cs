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
/// process. Reactor now holds the box until well after any tick it can still receive.</para>
///
/// <para><b>Forcing the order.</b> Each attempt arms the timer, drops every reference to the
/// box, and runs full GCs around UI ticks: the first GC lets XAML's reference tracker find the
/// box unreachable, the tick lets XAML run its cleanup, and the next GC reclaims what the box's
/// native side still points at. Without a hold, no attempt's tick reaches managed code alive:
/// each one either lands on collected state (the crash) or is lost with it. Each way a tick can
/// be pending while Reactor listens has its own arm, and three of them isolate one hold each.</para>
/// </summary>
internal static class AutoSuggestBoxCollectedStateTickFixture
{
    private enum Arm
    {
        /// <summary>Mounted empty, with a callback; typing changes Text (the Text hold).</summary>
        Typed,
        /// <summary>Mounted with text and a callback; the first layout copies Text into the TextBox.</summary>
        MountedWithText,
        /// <summary>Mounted with text but no callback; an update adds one after the copy (the
        /// hold taken when the handler is installed).</summary>
        CallbackAddedLate,
        /// <summary>Mounted collapsed with text and a callback, the mount's hold dropped, then shown;
        /// the copy happens in that first layout (the SizeChanged hold).</summary>
        ShownLate,
    }

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
            // TextChanged that reaches Reactor while the box is mounted.
            var mountedCallbacks = 0;
            var mountedHost = await ArmAsync(H, Arm.Typed, () => mountedCallbacks++, countTick, orphan: false);
            H.Check(
                $"{Name}_TickReachesMountedBox",
                await Harness.WaitFor(() => mountedCallbacks > 0, maxPasses: 40, perPassMs: 20));
            mountedHost?.Dispose();
            H.SetContent(null);
            await Harness.Render();

            var unhandled = new List<string>();
            Microsoft.UI.Xaml.UnhandledExceptionEventHandler handler = (_, e) =>
            {
                unhandled.Add($"{e.Exception?.GetType().Name} 0x{e.Exception?.HResult:X8}: {e.Message}");
                // Report through the checks below instead of letting WinUI fail-fast the run.
                e.Handled = true;
            };

            var typedCallbacks = 0;
            int typedTicks, mountedTicks, lateTicks, shownTicks;
            Application.Current.UnhandledException += handler;
            try
            {
                typedTicks = await OrphanAndCollectAsync(Arm.Typed, () => typedCallbacks++, countTick);
                mountedTicks = await OrphanAndCollectAsync(Arm.MountedWithText, static () => { }, countTick);
                lateTicks = await OrphanAndCollectAsync(Arm.CallbackAddedLate, static () => { }, countTick);
                shownTicks = await OrphanAndCollectAsync(Arm.ShownLate, static () => { }, countTick);
            }
            finally
            {
                Application.Current.UnhandledException -= handler;
            }

            H.Check(
                $"{Name}_NoUnhandledException",
                unhandled.Count == 0,
                $"{unhandled.Count} of {4 * Attempts} attempt(s) threw into WinUI, first: {(unhandled.Count > 0 ? unhandled[0] : "")}");
            H.Check(
                $"{Name}_TypedTickReachesCallback",
                typedCallbacks == Attempts && typedTicks == Attempts,
                $"typed: callback reached {typedCallbacks}, tick delivered {typedTicks}, of {Attempts} attempt(s)");
            H.Check(
                $"{Name}_MountedWithTextTickDelivered",
                mountedTicks == Attempts,
                $"mounted with text: tick delivered {mountedTicks} of {Attempts} attempt(s)");
            H.Check(
                $"{Name}_LateSubscriptionTickDelivered",
                lateTicks == Attempts,
                $"callback added after the copy: tick delivered {lateTicks} of {Attempts} attempt(s)");
            H.Check(
                $"{Name}_ShownLateTickDelivered",
                shownTicks == Attempts,
                $"shown after the mount's hold ended: tick delivered {shownTicks} of {Attempts} attempt(s)");
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
        // only when the box stays mounted.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<ReactorHost?> ArmAsync(
            Harness h,
            Arm arm,
            Action onTextChanged,
            TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxTextChangedEventArgs> countTick,
            bool orphan)
        {
            var text = arm == Arm.Typed ? Optional<string>.Unset : Optional<string>.Of("initial");
            Action? addCallback = null;
            Action? show = null;
            var host = h.CreateHost();
            host.Mount(ctx =>
            {
                var (hasCallback, setHasCallback) = ctx.UseState(arm != Arm.CallbackAddedLate);
                var (visible, setVisible) = ctx.UseState(arm != Arm.ShownLate);
                addCallback = () => setHasCallback(true);
                show = () => setVisible(true);
                return VStack(AutoSuggestBox(text, hasCallback ? _ => onTextChanged() : null).IsVisible(visible));
            });
            // A visible box gets its template in this first layout, and with text it copies the
            // text into the TextBox, which starts the timer.
            await Harness.Render();

            var box = h.FindControl<WinUI.AutoSuggestBox>(_ => true)
                ?? throw new InvalidOperationException("AutoSuggestBox was not mounted");

            switch (arm)
            {
                case Arm.CallbackAddedLate:
                    // The update installs Reactor's TextChanged handler while that tick is pending.
                    addCallback!();
                    await Harness.Render();
                    break;

                case Arm.ShownLate:
                    // Drop the hold taken when the handler was installed at mount, so only the
                    // first layout can hold the box. Showing it applies the template and copies.
                    AutoSuggestBoxElement.ReleaseTextChangedTickHoldForTests(box);
                    show!();
                    await Harness.Render();
                    break;
            }

            box.TextChanged += countTick;

            if (arm == Arm.Typed)
            {
                var inner = FindDescendant<WinUI.TextBox>(box)
                    ?? throw new InvalidOperationException("AutoSuggestBox's inner TextBox was not realized");
                // A text change the box did not make itself is reported as UserInput. The box
                // restarts its timer when it sees the TextBox's own, queued, TextChanged, which
                // the Render below delivers.
                inner.Text = "typed-" + Environment.TickCount64;
                await Harness.Render();
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
