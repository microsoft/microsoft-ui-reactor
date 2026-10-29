using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ConnectedNavigationFixtures
{
    internal sealed class NestedSource(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            var navigation = new NavigationHandle<string>(new NavigationStack<string>("main"));
            var innerNavigation = new NavigationHandle<int>(new NavigationStack<int>(0));
            var host = H.CreateHost();
            host.Mount(_ => (NavigationHost(navigation, route => route == "main"
                ? Border(NavigationHost(innerNavigation, _ => ScrollView(VStack(
                    Border(TextBlock("Device card")).Size(100, 60).ConnectedAnimation("device")
                        .AutomationId("nested-source-card")))))
                : Border(VStack(TextBlock("Overlay"),
                    Border(TextBlock("Receiver")).Size(180, 80).ConnectedAnimation("device")))) with
            {
                Transition = NavigationTransition.None,
                CacheMode = NavigationCacheMode.Enabled,
                CacheSize = 1,
            }).AutomationId("nested-overlay-host"));
            await Harness.Render();
            var source = H.FindControl<Border>(b => Id(b) == "nested-source-card");
            H.Check("ConnectedNav_NestedSourceMeasured", source is { ActualWidth: > 0, ActualHeight: > 0 });
            var baseline = host.Reconciler.ConnectedAnimationStartCount;
            navigation.Navigate("overlay", new() { Transition = NavigationTransition.Fade(), ConnectedAnimationKey = "device" });
            H.Check("ConnectedNav_NestedSourceActuallyAnimates", await Harness.WaitFor(() =>
                host.Reconciler.ConnectedAnimationStartCount == baseline + 1));
            H.Check("ConnectedNav_NestedOverlaySettles", await Harness.WaitFor(() =>
                H.FindControl<Grid>(g => Id(g) == "nested-overlay-host")?.Children.Count == 1, 100));
            navigation.GoBack();
            H.Check("ConnectedNav_NestedReturnActuallyAnimates", await Harness.WaitFor(() =>
                host.Reconciler.ConnectedAnimationStartCount == baseline + 2));
            H.Check("ConnectedNav_NestedReturnSettles", await Harness.WaitFor(() =>
                H.FindControl<Grid>(g => Id(g) == "nested-overlay-host")?.Children.Count == 1, 100));
            H.Check("ConnectedNav_NestedSourceRetainsIdentity",
                ReferenceEquals(source, H.FindControl<Border>(b => Id(b) == "nested-source-card")));
        }
    }

    internal sealed class RoundTrip(Harness h, bool cached) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            var nav = new NavigationHandle<string>(new NavigationStack<string>("list"));
            var host = H.CreateHost();
            host.Mount(_ => (NavigationHost(nav, route =>
                Border(VStack(8,
                    TextBlock(route),
                    Border(TextBlock("Shared hero"))
                        .Size(route == "list" ? 100 : 180, route == "list" ? 50 : 90)
                        .ConnectedAnimation("hero")
                        .AutomationId("connected-" + route)))
                    .Padding(20)) with
            {
                Transition = NavigationTransition.None,
                CacheMode = cached ? NavigationCacheMode.Enabled : NavigationCacheMode.Disabled,
            }).AutomationId("connected-host"));
            await Harness.Render();
            var source = H.FindControl<Border>(b => Id(b) == "connected-list");
            H.Check("ConnectedNav_SourceHasBounds", source is { ActualWidth: > 0, ActualHeight: > 0 });
            var sourcePage = H.FindControl<Grid>(g => Id(g) == "connected-host")!.Children[0];
            if (cached)
            {
                TransitionEngine.SuppressHitTesting(sourcePage);
                H.Check("ConnectedNav_OutgoingSuppressionPositiveControl", !sourcePage.IsHitTestVisible);
            }
            var baseline = host.Reconciler.ConnectedAnimationStartCount;
            H.Check("ConnectedNav_NoPageFadeBeforeNavigation",
                ElementCompositionPreview.GetElementVisual(sourcePage).TryGetAnimationController("Opacity") is null);
            nav.Navigate("detail", new() { Transition = NavigationTransition.DrillIn(), ConnectedAnimationKey = "hero" });
            H.Check("ConnectedNav_ForwardNativeAnimationStarted",
                await Harness.WaitFor(() => host.Reconciler.ConnectedAnimationStartCount == baseline + 1));
            var forwardPages = H.FindControl<Grid>(g => Id(g) == "connected-host")!;
            H.Check("ConnectedNav_ForwardPagesCrossfade", forwardPages.Children.Count == 2
                && ElementCompositionPreview.GetElementVisual(forwardPages.Children[0]).TryGetAnimationController("Opacity") is not null
                && ElementCompositionPreview.GetElementVisual(forwardPages.Children[1]).TryGetAnimationController("Opacity") is not null);
            H.Check("ConnectedNav_FadingOutgoingPageNotInteractive", !sourcePage.IsHitTestVisible);
            H.Check("ConnectedNav_PageDrillInDuringHeroMotion", forwardPages.Children.Count == 2 && forwardPages.Children.All(page =>
                ElementCompositionPreview.GetElementVisual(page).TryGetAnimationController("Scale") is not null));
            H.Check("ConnectedNav_ForwardOldPageRemoved", await Harness.WaitFor(() =>
                H.FindControl<Grid>(g => Id(g) == "connected-host")?.Children.Count == 1, 100));
            H.Check("ConnectedNav_ForwardFadeReleased", forwardPages.Children.Count == 1
                && ElementCompositionPreview.GetElementVisual(forwardPages.Children[0]).TryGetAnimationController("Opacity") is null
                && ElementCompositionPreview.GetElementVisual(sourcePage).TryGetAnimationController("Opacity") is null
                && ElementCompositionPreview.GetElementVisual(sourcePage).TryGetAnimationController("Scale") is null
                && ElementCompositionPreview.GetElementVisual(forwardPages.Children[0]).TryGetAnimationController("Scale") is null
                && sourcePage.IsHitTestVisible);
            if (cached)
            {
                H.Check("ConnectedNav_OutgoingSuppressionReleased", sourcePage.IsHitTestVisible);
                TransitionEngine.SuppressHitTesting(sourcePage);
                H.Check("ConnectedNav_CachedSuppressionPositiveControl", !sourcePage.IsHitTestVisible);
            }
            nav.GoBack();
            H.Check("ConnectedNav_BackNativeAnimationStarted",
                await Harness.WaitFor(() => host.Reconciler.ConnectedAnimationStartCount == baseline + 2));
            H.Check("ConnectedNav_BackPagesCrossfade", forwardPages.Children.Count == 2
                && forwardPages.Children.All(page =>
                    ElementCompositionPreview.GetElementVisual(page).TryGetAnimationController("Opacity") is not null));
            H.Check("ConnectedNav_BackPageDrillIn", forwardPages.Children.Count == 2 && forwardPages.Children.All(page =>
                ElementCompositionPreview.GetElementVisual(page).TryGetAnimationController("Scale") is not null));
            H.Check("ConnectedNav_BackOldPageRemoved", await Harness.WaitFor(() =>
                H.FindControl<Grid>(g => Id(g) == "connected-host")?.Children.Count == 1, 100));
            var returned = H.FindControl<Border>(b => Id(b) == "connected-list");
            H.Check("ConnectedNav_ReturnedCardUsable", returned is { IsHitTestVisible: true, ActualWidth: > 0 });
            H.Check("ConnectedNav_BackFadeReleased", forwardPages.Children.Count == 1
                && ElementCompositionPreview.GetElementVisual(forwardPages.Children[0]).TryGetAnimationController("Opacity") is null
                && ElementCompositionPreview.GetElementVisual(forwardPages.Children[0]).TryGetAnimationController("Scale") is null);
            if (cached) H.Check("ConnectedNav_CacheRestoresSameCard", ReferenceEquals(source, returned));
            if (cached) H.Check("ConnectedNav_CachedPageInteractive", sourcePage.IsHitTestVisible);
            H.Check("ConnectedNav_NoDuplicateAutomaticStarts", host.Reconciler.ConnectedAnimationStartCount == baseline + 2);
        }
    }

    internal sealed class Fallback(Harness h, bool reducedMotion, bool duplicate = false, bool zeroSized = false, bool regular = false) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(!reducedMotion);
            var nav = new NavigationHandle<string>(new NavigationStack<string>("a"));
            var host = H.CreateHost();
            host.Mount(_ => (NavigationHost(nav, route => Border(VStack(4,
                Border(TextBlock(route)).ConnectedAnimation(route == "a" || reducedMotion || duplicate || zeroSized || regular ? "hero" : "missing")
                    .Size(100, route == "b" && zeroSized ? 0 : 40)
                    .MaxHeight(route == "b" && zeroSized ? 0 : 100)
                    .AutomationId("fallback-hero"),
                route == "b" && duplicate ? TextBlock("Duplicate").ConnectedAnimation("hero") : null))) with
            {
                Transition = NavigationTransition.None,
            }).AutomationId("connected-host"));
            await Harness.Render();
            var baseline = host.Reconciler.ConnectedAnimationStartCount;
            nav.Navigate("b", new() { Transition = regular ? NavigationTransition.None : NavigationTransition.Connected("hero") });
            H.Check("ConnectedNav_FallbackNavigates", await Harness.WaitFor(() => H.FindText("b") is not null));
            H.Check("ConnectedNav_FallbackReleasesOldPage", await Harness.WaitFor(() =>
                H.FindControl<Grid>(g => Id(g) == "connected-host")?.Children.Count == 1, 100));
            H.Check("ConnectedNav_FallbackDoesNotStartNativeAnimation", host.Reconciler.ConnectedAnimationStartCount == baseline);
            if (zeroSized) H.Check("ConnectedNav_ZeroTargetHasZeroBounds",
                H.FindControl<Border>(b => Id(b) == "fallback-hero") is { ActualHeight: 0 });
        }
    }

    internal sealed class CompositionOptions(Harness h, NavigationTransition pageTransition, bool hostDefault = false) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            var nav = new NavigationHandle<string>(new NavigationStack<string>("list"));
            var host = H.CreateHost();
            host.Mount(_ => (NavigationHost(nav, route => Border(VStack(8,
                TextBlock(route),
                Border(TextBlock("Shared card")).Size(route == "list" ? 100 : 180, 60).ConnectedAnimation("hero")))) with
            {
                Transition = hostDefault ? pageTransition : NavigationTransition.None,
            }).AutomationId("connected-options-host"));
            await Harness.Render();
            var pages = H.FindControl<Grid>(g => Id(g) == "connected-options-host")!;
            H.Check("ConnectedNav_OptionsNoInitialScale",
                ElementCompositionPreview.GetElementVisual(pages.Children[0]).TryGetAnimationController("Scale") is null);
            var baseline = host.Reconciler.ConnectedAnimationStartCount;
            nav.Navigate("detail", new()
            {
                Transition = hostDefault ? null : pageTransition,
                ConnectedAnimationKey = "hero",
            });
            H.Check("ConnectedNav_OptionsNativeCardStarts", await Harness.WaitFor(() =>
                host.Reconciler.ConnectedAnimationStartCount == baseline + 1));
            H.Check("ConnectedNav_OptionsBothPagesPresent", pages.Children.Count == 2);
            H.Check("ConnectedNav_OptionsPageOpacityMatchesSelection", pages.Children.All(page =>
                (ElementCompositionPreview.GetElementVisual(page).TryGetAnimationController("Opacity") is not null)
                == (pageTransition is not SuppressTransition)));
            H.Check("ConnectedNav_OptionsPageScaleMatchesSelection", pages.Children.All(page =>
                (ElementCompositionPreview.GetElementVisual(page).TryGetAnimationController("Scale") is not null)
                == (pageTransition is DrillInTransition)));
            H.Check("ConnectedNav_OptionsPageOffsetMatchesSelection", pages.Children.All(page =>
                (ElementCompositionPreview.GetElementVisual(page).TryGetAnimationController("Offset") is not null)
                == (pageTransition is SlideTransition)));
            var oldPage = pages.Children[0];
            H.Check("ConnectedNav_OptionsSettles", await Harness.WaitFor(() => pages.Children.Count == 1, 100));
            H.Check("ConnectedNav_OptionsRestoresInput", oldPage.IsHitTestVisible && pages.Children[0].IsHitTestVisible);
            H.Check("ConnectedNav_OptionsReleasesProperties", new[] { oldPage, pages.Children[0] }.All(page =>
            {
                var visual = ElementCompositionPreview.GetElementVisual(page);
                return visual.TryGetAnimationController("Opacity") is null
                    && visual.TryGetAnimationController("Scale") is null
                    && visual.TryGetAnimationController("Offset") is null;
            }));
        }
    }

    internal sealed class Teardown(Harness h, bool prepared = false) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            var nav = new NavigationHandle<string>(new NavigationStack<string>("a"));
            var host = H.CreateHost();
            var root = (NavigationHost(nav, route => Border(Border(TextBlock(route))
                .Size(100, 60).ConnectedAnimation("hero"))) with { Transition = NavigationTransition.None })
                .AutomationId("teardown-host");
            host.Mount(_ => root);
            await Harness.Render();
            var pages = H.FindControl<Grid>(g => Id(g) == "teardown-host")!;
            var source = pages.Children[0];
            var baseline = host.Reconciler.ConnectedAnimationStartCount;
            nav.Navigate("b", new()
            {
                ConnectedAnimationKey = "hero",
                Transition = NavigationTransition.Fade(TimeSpan.FromSeconds(2)),
            });
            // Execute the normal reconciler update synchronously so we can retain
            // the prepared snapshot before the compositor gets its first frame.
            host.Reconciler.UpdateChild(root, root with { CacheSize = 2 }, pages, () => { });
            var operation = host.Reconciler._navigationHostNodes[pages].ConnectedOperation;
            var key = operation?.NativeAnimationKey;
            var snapshot = key is null ? null : Microsoft.UI.Xaml.Media.Animation.ConnectedAnimationService
                .GetForCurrentView().GetAnimation(key);
            H.Check("ConnectedNav_TeardownOwnsPendingSnapshot", operation is { IsCompleted: false } && snapshot is not null);
            if (operation is null || snapshot is null) return;
            var destination = pages.Children[1];
            if (!prepared)
            {
                H.Check("ConnectedNav_TeardownInterruptsNativeMotion", await Harness.WaitFor(() =>
                    host.Reconciler.ConnectedAnimationStartCount == baseline + 1));
                H.Check("ConnectedNav_TeardownMotionAndInputPositiveControl", !operation.IsCompleted && !source.IsHitTestVisible
                    && ElementCompositionPreview.GetElementVisual(destination).TryGetAnimationController("Opacity") is not null);
            }
            else
                H.Check("ConnectedNav_TeardownSnapshotNotYetStarted", host.Reconciler.ConnectedAnimationStartCount == baseline);
            host.Mount(_ => VStack(TextBlock("Host replaced"), Border(TextBlock("Probe target"))
                .Size(150, 70).AutomationId("teardown-probe")));
            await Harness.Render();
            H.Check("ConnectedNav_TeardownReplacementRemainsVisible", H.FindText("Host replaced") is not null);
            H.Check("ConnectedNav_TeardownCompletesOperation", operation.IsCompleted);
            H.Check("ConnectedNav_TeardownRestoresInputAndVisuals", source.IsHitTestVisible && destination.IsHitTestVisible
                && new[] { source, destination }.All(page =>
                    ElementCompositionPreview.GetElementVisual(page).TryGetAnimationController("Opacity") is null));
            if (prepared)
            {
                var target = H.FindControl<Border>(b => Id(b) == "teardown-probe")!;
                H.Check("ConnectedNav_TeardownCancelProbeIsMeasured", target is { ActualWidth: > 0, ActualHeight: > 0 });
                var restarted = snapshot.TryStart(target);
                H.Check("ConnectedNav_TeardownCancelsSnapshot", !restarted);
                if (restarted) snapshot.Cancel();
            }
            await Harness.Render(250);
            H.Check("ConnectedNav_TeardownNoLatePageRestore", H.FindText("Host replaced") is not null && H.FindText("b") is null);
        }
    }

    internal sealed class ChangedCachedDetail(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            var selected = "one";
            var nav = new NavigationHandle<string>(new NavigationStack<string>("list"));
            var host = H.CreateHost();
            host.Mount(_ => (NavigationHost(nav, route => route == "list"
                ? VStack(8,
                    Border(TextBlock("one")).Size(100, 50).ConnectedAnimation("one"),
                    Border(TextBlock("two")).Size(100, 50).ConnectedAnimation("two"))
                : Border(TextBlock("Detail " + selected)).Size(160, 80).ConnectedAnimation(selected)) with
            {
                Transition = NavigationTransition.None,
                CacheMode = NavigationCacheMode.Enabled,
            }).AutomationId("connected-host"));
            await Harness.Render();
            var baseline = host.Reconciler.ConnectedAnimationStartCount;
            nav.Navigate("detail", new() { Transition = NavigationTransition.Connected("one") });
            H.Check("ConnectedNav_CachedDetailFirstStarts", await Harness.WaitFor(() =>
                host.Reconciler.ConnectedAnimationStartCount == baseline + 1
                && H.FindControl<Grid>(g => Id(g) == "connected-host")?.Children.Count == 1, 100));
            nav.GoBack();
            H.Check("ConnectedNav_CachedDetailReturns", await Harness.WaitFor(() =>
                host.Reconciler.ConnectedAnimationStartCount == baseline + 2
                && H.FindControl<Grid>(g => Id(g) == "connected-host")?.Children.Count == 1, 100));
            selected = "two";
            nav.Navigate("detail", new() { Transition = NavigationTransition.Connected("two") });
            H.Check("ConnectedNav_CachedDetailNewKeyStarts", await Harness.WaitFor(() =>
                host.Reconciler.ConnectedAnimationStartCount == baseline + 3
                && H.FindControl<Grid>(g => Id(g) == "connected-host")?.Children.Count == 1, 100));
            H.Check("ConnectedNav_CachedDetailShowsCurrentPayload", H.FindText("Detail two") is not null && H.FindText("Detail one") is null);
        }
    }

    internal sealed class Interrupted(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            var nav = new NavigationHandle<string>(new NavigationStack<string>("a"));
            var host = H.CreateHost();
            host.Mount(_ => (NavigationHost(nav, route => Border(VStack(8,
                TextBlock(route),
                Border(TextBlock("Hero")).Size(100, 60).ConnectedAnimation("hero"),
                Border(TextBlock("Unpicked")).Size(60, 30).ConnectedAnimation("unpicked")))) with
            {
                Transition = NavigationTransition.None,
                CacheMode = NavigationCacheMode.Enabled,
            }).AutomationId("connected-host"));
            await Harness.Render();
            var baseline = host.Reconciler.ConnectedAnimationStartCount;
            for (var i = 0; i < 5; i++)
            {
                // Capture before the last navigation, not after WaitForIdleAsync:
                // a native frame may already start it during that await.
                if (i == 4) baseline = host.Reconciler.ConnectedAnimationStartCount;
                nav.Navigate("page" + i, new() { Transition = NavigationTransition.DrillIn(), ConnectedAnimationKey = "hero" });
                // Commit another navigation before a rendered-frame wait. This
                // exercises cancellation of a prepared, not-yet-started snapshot.
                await host.WaitForIdleAsync();
            }
            H.Check("ConnectedNav_InterruptedFinalAnimationStarts",
                await Harness.WaitFor(() => host.Reconciler.ConnectedAnimationStartCount > baseline));
            var pages = H.FindControl<Grid>(g => Id(g) == "connected-host")!;
            var interruptedPage = pages.Children[pages.Children.Count - 1];
            H.Check("ConnectedNav_InterruptingActiveFadePositiveControl",
                ElementCompositionPreview.GetElementVisual(interruptedPage).TryGetAnimationController("Opacity") is not null);
            baseline = host.Reconciler.ConnectedAnimationStartCount;
            nav.Navigate("page5", new() { Transition = NavigationTransition.DrillIn(), ConnectedAnimationKey = "hero" });
            H.Check("ConnectedNav_ReplacementAnimationStarts",
                await Harness.WaitFor(() => host.Reconciler.ConnectedAnimationStartCount > baseline));
            H.Check("ConnectedNav_InterruptedLeavesOnePage", await Harness.WaitFor(() =>
                H.FindControl<Grid>(g => Id(g) == "connected-host")?.Children.Count == 1, 100));
            H.Check("ConnectedNav_InterruptedLatestRouteVisible", H.FindText("page5") is not null);
            H.Check("ConnectedNav_InterruptedFadeReleased",
                ElementCompositionPreview.GetElementVisual(interruptedPage).TryGetAnimationController("Opacity") is null
                && ElementCompositionPreview.GetElementVisual(interruptedPage).TryGetAnimationController("Scale") is null
                && interruptedPage.IsHitTestVisible);
            H.Check("ConnectedNav_InterruptedUnpickedAnimationNotPrepared",
                Microsoft.UI.Xaml.Media.Animation.ConnectedAnimationService.GetForCurrentView().GetAnimation("unpicked") is null);
            nav.GoBack();
            H.Check("ConnectedNav_InterruptedBackNavigates", await Harness.WaitFor(() =>
                H.FindText("page4") is not null
                && H.FindControl<Grid>(g => Id(g) == "connected-host")?.Children.Count == 1, 100));
        }
    }

    internal sealed class ColdScrollView(Harness h, bool deferTarget) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            var selected = "a";
            var mounts = new Dictionary<string, int>();
            var nav = new NavigationHandle<string>(new NavigationStack<string>("list"));
            var host = H.CreateHost();
            host.Mount(_ => (NavigationHost(nav, route => route == "list"
                ? ScrollView(VStack(8,
                    Border(TextBlock("Device a")).Size(100, 50).ConnectedAnimation("a"),
                    Border(TextBlock("Device b")).Size(100, 50).ConnectedAnimation("b")))
                : Component<ColdDetailPage, ColdDetailPageProps>(new(selected, deferTarget,
                    () => mounts[selected] = mounts.GetValueOrDefault(selected) + 1)).WithKey(selected)) with
            {
                Transition = NavigationTransition.None,
                CacheMode = NavigationCacheMode.Enabled,
            }).Size(320, 260).AutomationId("connected-cold-host"));
            await Harness.Render();
            H.Check("ConnectedNav_ColdDetailNotPrewarmed", mounts.Count == 0);

            // Both devices are cold on their first visit. Reusing the detail route
            // with a device key also recreates its ScrollView on a device change,
            // exactly like Tonarink; merely warming a single shared page is not enough.
            foreach (var device in new[] { "a", "a", "b", "b", "a" })
            {
                selected = device;
                var before = host.Reconciler.ConnectedAnimationStartCount;
                nav.Navigate("detail", new() { Transition = NavigationTransition.Connected(device) });
                H.Check("ConnectedNav_ColdForwardStarts_" + device, await Harness.WaitFor(() =>
                    host.Reconciler.ConnectedAnimationStartCount == before + 1));
                H.Check("ConnectedNav_ColdDetailSettles_" + device, await Harness.WaitFor(() =>
                    H.FindControl<Grid>(g => Id(g) == "connected-cold-host")?.Children.Count == 1, 100));
                H.Check("ConnectedNav_ColdCorrectDevice_" + device, H.FindText("Detail " + device) is not null);
                before = host.Reconciler.ConnectedAnimationStartCount;
                nav.GoBack();
                H.Check("ConnectedNav_ColdBackStarts_" + device, await Harness.WaitFor(() =>
                    host.Reconciler.ConnectedAnimationStartCount == before + 1));
                H.Check("ConnectedNav_ColdBackSettles_" + device, await Harness.WaitFor(() =>
                    H.FindControl<Grid>(g => Id(g) == "connected-cold-host")?.Children.Count == 1, 100));
            }
            H.Check("ConnectedNav_ColdBothDeviceCardsCreated", mounts.ContainsKey("a") && mounts.ContainsKey("b"));
        }
    }

    internal sealed record ColdDetailPageProps(string Device, bool Deferred, Action Mounted);

    internal sealed class ColdDetailPage : Component<ColdDetailPageProps>
    {
        public override Element Render()
        {
            var (ready, setReady) = UseState(!Props.Deferred);
            UseEffect(() => setReady(true));
            return ScrollView(Border(VStack(8,
                TextBlock("Device details"),
                ready ? Component<ColdDetailCard, ColdDetailPageProps>(Props) : TextBlock("Preparing"))));
        }
    }

    internal sealed class ColdDetailCard : Component<ColdDetailPageProps>
    {
        public override Element Render() => Border(TextBlock("Detail " + Props.Device))
            .Size(180, 90)
            .ConnectedAnimation(Props.Device)
            .OnMountAdd(_ => Props.Mounted());
    }

    private static string Id(Microsoft.UI.Xaml.DependencyObject element) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(element);
}
