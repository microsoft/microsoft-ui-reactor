using System.Diagnostics;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Animation;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ConnectedNavigationCompletionFixtures
{
    internal sealed class CompletionOrder(Harness h, bool longPage, bool shortNative = false) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                Border(Border(TextBlock("Source")).Size(100, 50).ConnectedAnimation("hero")).AutomationId("completion-source"),
                Border(Border(TextBlock("Target")).Size(180, 90).ConnectedAnimation("hero")).AutomationId("completion-target")));
            await Harness.Render();
            var source = H.FindControl<Border>(b => Id(b) == "completion-source")!;
            var target = H.FindControl<Border>(b => Id(b) == "completion-target")!;
            var service = ConnectedAnimationService.GetForCurrentView();
            var savedDuration = service.DefaultDuration;
            service.DefaultDuration = TimeSpan.FromMilliseconds(shortNative ? 1 : 1600);
            try
            {
                var completions = 0;
                var clock = Stopwatch.StartNew();
                using var operation = new ConnectedNavigationOperation(host.Reconciler, source, target,
                    "hero", NavigationTransition.Fade(TimeSpan.FromMilliseconds(longPage ? 4500 : 250)),
                    NavigationMode.Pop, () => completions++);
                var native = service.GetAnimation(operation.NativeAnimationKey!);
                H.Check("ConnectedNav_CompletionSnapshotPositiveControl", native is not null);
                if (native is null) return;
                // Basic respects DefaultDuration, but WinUI reduces native motion
                // to 1ms when Windows animations are disabled (e.g. CI runners).
                // Reactor's override above only controls Reactor's own motion.
                native.Configuration = new BasicConnectedAnimationConfiguration();
                var nativeCompleted = false;
                native.Completed += OnNativeCompleted;
                void OnNativeCompleted(ConnectedAnimation _, object args) => nativeCompleted = true;
                try
                {
                    var baseline = host.Reconciler.ConnectedAnimationStartCount;
                    H.Check("ConnectedNav_CompletionActuallyStarts", await Harness.WaitFor(() =>
                        host.Reconciler.ConnectedAnimationStartCount == baseline + 1));
                    var visual = ElementCompositionPreview.GetElementVisual(target);
                    H.Check("ConnectedNav_CompletionPageMotionPositiveControl",
                        visual.TryGetAnimationController("Opacity") is not null);

                    if (longPage)
                    {
                        H.Check("ConnectedNav_NativeCompletesFirst", await Harness.WaitFor(() => nativeCompleted, 100, 10));
                        H.Check("ConnectedNav_WaitsForLongPageMotion", completions == 0 && !operation.IsCompleted
                            && visual.TryGetAnimationController("Opacity") is not null);
                        await Harness.Render(Math.Max(0, 3250 - (int)clock.ElapsedMilliseconds));
                        H.Check("ConnectedNav_PageMotionSurvivesReadinessDeadline", completions == 0
                            && !operation.IsCompleted && visual.TryGetAnimationController("Opacity") is not null);
                    }
                    else
                    {
                        H.Check("ConnectedNav_PageMotionCompletes", await Harness.WaitFor(() =>
                            operation.PageAnimationBatch is null, 70, 5));
                        // Observe the actual native order instead of imposing a
                        // duration assumption on the machine's accessibility settings.
                        // Both orders are also covered deterministically by the
                        // completion barrier's unit tests.
                        H.Check("ConnectedNav_PageCompletionRespectsNativeMotion", nativeCompleted
                            ? completions == 1 && operation.IsCompleted
                            : completions == 0 && !operation.IsCompleted);
                    }

                    H.Check("ConnectedNav_CompletesAfterBothMotions", await Harness.WaitFor(() =>
                        completions == 1 && nativeCompleted && operation.IsCompleted, 160, 20));
                    H.Check("ConnectedNav_CompletionReleasesMotionAndInput", source.IsHitTestVisible && target.IsHitTestVisible
                        && visual.TryGetAnimationController("Opacity") is null
                        && ElementCompositionPreview.GetElementVisual(source).TryGetAnimationController("Opacity") is null);
                }
                finally { native.Completed -= OnNativeCompleted; }
            }
            finally { service.DefaultDuration = savedDuration; }
        }
    }

    internal sealed class CancelPreparedSnapshot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            var host = H.CreateHost();
            host.Mount(_ => VStack(
                Border(Border(TextBlock("Source")).Size(100, 50).ConnectedAnimation("hero")).AutomationId("cancel-source"),
                Border(Border(TextBlock("Target")).Size(180, 90).ConnectedAnimation("hero")).AutomationId("cancel-target")));
            await Harness.Render();
            var source = H.FindControl<Border>(b => Id(b) == "cancel-source")!;
            var target = H.FindControl<Border>(b => Id(b) == "cancel-target")!;
            var completions = 0;
            var baseline = host.Reconciler.ConnectedAnimationStartCount;
            using var operation = new ConnectedNavigationOperation(host.Reconciler, source, target,
                "hero", NavigationTransition.None, NavigationMode.Push, () => completions++);
            var service = ConnectedAnimationService.GetForCurrentView();
            var key = operation.NativeAnimationKey;
            var native = key is null ? null : service.GetAnimation(key);
            H.Check("ConnectedNav_CancelActualSnapshotPositiveControl", native is not null);
            operation.Dispose();
            var destination = (Microsoft.UI.Xaml.UIElement)target.Child;
            H.Check("ConnectedNav_CancelDestinationPositiveControl", destination is Microsoft.UI.Xaml.FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 });
            var restarted = native?.TryStart(destination) == true;
            H.Check("ConnectedNav_CancelPreventsSnapshotRestart", native is not null && !restarted);
            if (restarted) native!.Cancel();
            await Harness.Render(150);
            H.Check("ConnectedNav_CancelCompletesOnce", completions == 1 && operation.IsCompleted
                && host.Reconciler.ConnectedAnimationStartCount == baseline);
        }
    }

    internal sealed class InterruptedLifecycle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            var events = new List<string>();
            var nav = new NavigationHandle<string>(new NavigationStack<string>("a"));
            var host = H.CreateHost();
            host.Mount(_ => (NavigationHost(nav, route =>
                Component<LifecyclePage, LifecycleProps>(new(route, events)).WithKey(route)) with
            {
                Transition = NavigationTransition.None,
                CacheMode = NavigationCacheMode.Enabled,
                CacheSize = 3,
            }).AutomationId("lifecycle-connected-host"));
            await Harness.Render();
            events.Clear();
            var baseline = host.Reconciler.ConnectedAnimationStartCount;
            nav.Navigate("b", new() { Transition = NavigationTransition.Fade(TimeSpan.FromSeconds(4)), ConnectedAnimationKey = "hero" });
            H.Check("ConnectedNav_LifecycleInterruptsRealMotion", await Harness.WaitFor(() =>
                host.Reconciler.ConnectedAnimationStartCount == baseline + 1));
            H.Check("ConnectedNav_LifecycleDestinationNotYetArrived", !events.Contains("to:b"));
            nav.Navigate("c", new() { Transition = NavigationTransition.Fade(), ConnectedAnimationKey = "hero" });
            H.Check("ConnectedNav_LifecycleLatestPageArrives", await Harness.WaitFor(() => events.Contains("to:c"), 100));
            H.Check("ConnectedNav_LifecycleEveryDepartureOnce", events.Count(e => e == "from:a") == 1
                && events.Count(e => e == "from:b") == 1);
            H.Check("ConnectedNav_LifecycleLatestArrivalOnly", events.Count(e => e == "to:c") == 1
                && !events.Contains("to:b"));
            await Harness.Render(200);
            H.Check("ConnectedNav_LifecycleNoLateDuplicates", events.Count(e => e.StartsWith("from:", StringComparison.Ordinal)) == 2);
            nav.GoBack();
            H.Check("ConnectedNav_LifecycleCachedPageCanArrive", await Harness.WaitFor(() => events.Contains("to:b"), 100));
            H.Check("ConnectedNav_LifecycleCachedReturnDepartsOnce", events.Count(e => e == "from:c") == 1);
        }
    }

    internal sealed record LifecycleProps(string Route, List<string> Events);

    internal sealed class NestedLifecycleIsolation(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            var events = new List<string>();
            var outer = new NavigationHandle<int>(new NavigationStack<int>(0));
            var inner = new NavigationHandle<string>(new NavigationStack<string>("send"));
            var host = H.CreateHost();
            host.Mount(_ => (NavigationHost(outer, scene => scene == 0
                ? NavigationHost(inner, route => Component<GuardedNestedPage, GuardedNestedProps>(new(route, inner, events)))
                : Border(Border(TextBlock("Transfer overlay")).Size(180, 90).ConnectedAnimation("hero"))) with
            {
                Transition = NavigationTransition.None,
                CacheMode = NavigationCacheMode.Enabled,
                CacheSize = 1,
            }).AutomationId("isolated-outer-host"));
            await Harness.Render();
            var pages = H.FindControl<Grid>(g => Id(g) == "isolated-outer-host")!;
            for (var visit = 1; visit <= 3; visit++)
            {
                H.Check("ConnectedNav_NestedOverlayNavigationAccepted_" + visit,
                    outer.Navigate(1, new() { Transition = NavigationTransition.Fade(), ConnectedAnimationKey = "hero" }));
                H.Check("ConnectedNav_NestedOverlayDisplayed_" + visit, await Harness.WaitFor(() =>
                    H.FindText("Transfer overlay") is not null && pages.Children.Count == 1, 100));
                H.Check("ConnectedNav_NestedOverlayReturnAccepted_" + visit, outer.GoBack());
                H.Check("ConnectedNav_NestedMainRestored_" + visit, await Harness.WaitFor(() =>
                    H.FindText("Nested send") is not null && pages.Children.Count == 1, 100));
                H.Check("ConnectedNav_OuterNavigationKeepsInnerLifecycleIdle_" + visit, events.Count == 0);
            }

            H.Check("ConnectedNav_InnerNavigationStillAccepted", inner.Navigate("settings"));
            H.Check("ConnectedNav_InnerNavigationStillCompletes", await Harness.WaitFor(() =>
                H.FindText("Nested settings") is not null && events.Contains("to:settings"), 100));
            H.Check("ConnectedNav_InnerLifecycleObservesOwnRoutes", events.SequenceEqual(new[]
                { "from:send", "enter:settings", "to:settings", "left:send" }));
        }
    }

    internal sealed record GuardedNestedProps(string Route, NavigationHandle<string> Navigation, List<string> Events);

    internal sealed class GuardedNestedPage : Component<GuardedNestedProps>
    {
        public override Element Render()
        {
            var transitioning = UseRef(false);
            var route = Props.Route;
            var events = Props.Events;
            UseNavigationLifecycle(onNavigatingTo: _ => events.Add("enter:" + route), onNavigatingFrom: ctx =>
            {
                if (transitioning.Current) { ctx.Cancel(); return; }
                transitioning.Current = true;
                events.Add("from:" + route);
            }, onNavigatedTo: ctx =>
            {
                if (!Equals(ctx.Route, Props.Navigation.CurrentRoute)) return;
                transitioning.Current = false;
                events.Add("to:" + route);
            }, onNavigatedFrom: _ => events.Add("left:" + route));
            return Border(Border(TextBlock("Nested " + route)).Size(100, 50).ConnectedAnimation("hero"));
        }
    }

    internal sealed class CachedLifecycleState(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            var events = new List<string>();
            var nav = new NavigationHandle<string>(new NavigationStack<string>("main"));
            var host = H.CreateHost();
            host.Mount(_ => (NavigationHost(nav, route => route == "main"
                ? Border(Border(TextBlock("Source")).Size(100, 50).ConnectedAnimation("hero"))
                : Component<StatefulOverlay, LifecycleProps>(new(route, events)).WithKey("same-device")) with
            {
                Transition = NavigationTransition.None,
                CacheMode = NavigationCacheMode.Enabled,
                CacheSize = 1,
            }).AutomationId("cached-state-host"));
            await Harness.Render();
            var pages = H.FindControl<Grid>(g => Id(g) == "cached-state-host")!;
            for (var visit = 1; visit <= 3; visit++)
            {
                nav.Navigate("overlay", new() { Transition = NavigationTransition.Fade(), ConnectedAnimationKey = "hero" });
                H.Check("ConnectedNav_CachedStateArrives_" + visit, await Harness.WaitFor(() =>
                    events.Count(e => e == "arrived") == visit && pages.Children.Count == 1, 100));
                H.Check("ConnectedNav_CachedStateVisible_" + visit, H.FindText("Overlay active") is not null);
                nav.GoBack();
                H.Check("ConnectedNav_CachedStateDeparts_" + visit, await Harness.WaitFor(() =>
                    events.Count(e => e == "departed") == visit && pages.Children.Count == 1, 100));
            }
        }
    }

    internal sealed class StatefulOverlay : Component<LifecycleProps>
    {
        public override Element Render()
        {
            var (active, setActive) = UseState(true);
            var events = Props.Events;
            UseNavigationLifecycle(onNavigatingTo: _ => setActive(true),
                onNavigatedTo: _ => events.Add("arrived"),
                onNavigatedFrom: _ => { setActive(false); events.Add("departed"); });
            return Border(VStack(TextBlock(active ? "Overlay active" : "Overlay inactive"),
                Border(TextBlock("Destination")).Size(180, 90).ConnectedAnimation("hero")));
        }
    }

    internal sealed class LifecyclePage : Component<LifecycleProps>
    {
        public override Element Render()
        {
            var route = Props.Route;
            var events = Props.Events;
            UseNavigationLifecycle(onNavigatedTo: _ => events.Add("to:" + route),
                onNavigatedFrom: _ => events.Add("from:" + route));
            return Border(Border(TextBlock(route)).Size(150, 60).ConnectedAnimation("hero"));
        }
    }

    private static string Id(Microsoft.UI.Xaml.DependencyObject element) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(element);
}
