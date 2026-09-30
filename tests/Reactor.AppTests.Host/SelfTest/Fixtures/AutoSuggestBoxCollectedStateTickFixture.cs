using System.Runtime.CompilerServices;
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
/// native side still points at. Without the hold, no attempt's tick reaches managed code alive:
/// each one either lands on collected state (the crash) or is lost with it. The timer is armed
/// two ways, because they restart it through different paths: typing (a <c>Text</c> change)
/// and mounting with text (the first layout copies <c>Text</c> into the fresh TextBox without
/// changing it).</para>
/// </summary>
internal static class AutoSuggestBoxCollectedStateTickFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        private const string Name = "AutoSuggestBoxCollectedStateTick";
        private const int Attempts = 4;

        // Counts TextChanged ticks of any reason. Static, so subscribing it roots nothing.
        private static int s_ticks;
        private static readonly TypedEventHandler<WinUI.AutoSuggestBox, WinUI.AutoSuggestBoxTextChangedEventArgs> CountTick =
            static (_, _) => Interlocked.Increment(ref s_ticks);

        public override async Task RunAsync()
        {
            // Positive control: typing into the inner TextBox queues a UserInput-reason
            // TextChanged that reaches Reactor while the box is mounted.
            var mountedCallbacks = 0;
            var mountedHost = await ArmAsync(H, Optional<string>.Unset, type: true, () => mountedCallbacks++, orphan: false);
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
            int typedTicks, copiedTicks;
            Application.Current.UnhandledException += handler;
            try
            {
                typedTicks = await OrphanAndCollectAsync(Optional<string>.Unset, type: true, () => typedCallbacks++);
                copiedTicks = await OrphanAndCollectAsync(Optional<string>.Of("initial"), type: false, static () => { });
            }
            finally
            {
                Application.Current.UnhandledException -= handler;
            }

            H.Check(
                $"{Name}_NoUnhandledException",
                unhandled.Count == 0,
                $"{unhandled.Count} of {2 * Attempts} attempt(s) threw into WinUI, first: {(unhandled.Count > 0 ? unhandled[0] : "")}");
            H.Check(
                $"{Name}_TypedTickReachesCallback",
                typedCallbacks == Attempts && typedTicks == Attempts,
                $"typed: callback reached {typedCallbacks}, tick delivered {typedTicks}, of {Attempts} attempt(s)");
            H.Check(
                $"{Name}_CopiedTextTickDelivered",
                copiedTicks == Attempts,
                $"mounted with text: tick delivered {copiedTicks} of {Attempts} attempt(s)");
        }

        private async Task<int> OrphanAndCollectAsync(Optional<string> text, bool type, Action onTextChanged)
        {
            var ticksBefore = Volatile.Read(ref s_ticks);
            for (var attempt = 0; attempt < Attempts; attempt++)
            {
                await ArmAsync(H, text, type, onTextChanged, orphan: true);

                GC.Collect();
                await Harness.Render();
                GC.Collect();
                await Harness.Render();
                GC.Collect();

                // Outlast the 150 ms timer.
                await Harness.Render(250);
            }
            return Volatile.Read(ref s_ticks) - ticksBefore;
        }

        // Kept out of RunAsync's state machine so nothing in the fixture still references
        // the box, its host, or its inner TextBox once the box is orphaned. Returns the host
        // only when the box stays mounted.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task<ReactorHost?> ArmAsync(Harness h, Optional<string> text, bool type, Action onTextChanged, bool orphan)
        {
            var host = h.CreateHost();
            host.Mount(_ => VStack(AutoSuggestBox(text, _ => onTextChanged())));
            // First layout applies the template; with text, the box copies it into the TextBox,
            // which restarts the timer.
            await Harness.Render();

            var box = h.FindControl<WinUI.AutoSuggestBox>(_ => true);
            var inner = box is null ? null : FindDescendant<WinUI.TextBox>(box);
            if (box is null || inner is null)
                throw new InvalidOperationException("AutoSuggestBox or its inner TextBox was not realized");
            box.TextChanged += CountTick;

            if (type)
            {
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
