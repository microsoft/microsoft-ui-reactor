using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ConnectedNavigationReviewFixtures
{
    internal sealed class InterruptedSlide(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            var departed = new List<NavigatedFromContext>();
            var arrived = new List<NavigatedToContext>();
            var nav = new NavigationHandle<string>(new NavigationStack<string>("a"));
            var host = H.CreateHost();
            host.Mount(_ => (NavigationHost(nav, route =>
                Component<Page, PageProps>(new(route, departed, arrived))) with
            {
                Transition = NavigationTransition.None,
            }).AutomationId("review-host"));
            await Harness.Render();
            var pages = H.FindControl<Grid>(g => Id(g) == "review-host")!;

            nav.Navigate("b", new() { Transition = NavigationTransition.Slide(duration: TimeSpan.FromMilliseconds(600)) });
            H.Check("ConnectedNav_SlideActuallyRunning", await Harness.WaitFor(() =>
                pages.Children.Count == 2 && !pages.Children[1].IsHitTestVisible
                && ElementCompositionPreview.GetElementVisual(pages.Children[1]).TryGetAnimationController("Offset") is not null));
            var leaving = pages.Children[^1];
            var baseline = host.Reconciler.ConnectedAnimationStartCount;
            nav.Navigate("c", new()
            {
                ConnectedAnimationKey = "hero",
                Transition = NavigationTransition.Fade(TimeSpan.FromMilliseconds(1600)),
            });
            H.Check("ConnectedNav_SlideReplacementActuallyStarts", await Harness.WaitFor(() =>
                host.Reconciler.ConnectedAnimationStartCount == baseline + 1));
            var operation = host.Reconciler._navigationHostNodes[pages].ConnectedOperation;
            H.Check("ConnectedNav_SlideReplacementPending", operation is { IsCompleted: false } && !leaving.IsHitTestVisible);
            H.Check("ConnectedNav_OldSlideReallyCompletes", await Harness.WaitFor(() =>
                departed.Any(context => Equals(context.Route, "a")), 80, 10));
            H.Check("ConnectedNav_OldSlideDoesNotReleaseNewSuppression", operation is { IsCompleted: false }
                && !leaving.IsHitTestVisible
                && ElementCompositionPreview.GetElementVisual(leaving).TryGetAnimationController("Opacity") is not null);
            H.Check("ConnectedNav_ReplacementEventuallyRestoresInput", await Harness.WaitFor(() =>
                operation is { IsCompleted: true } && leaving.IsHitTestVisible && pages.Children.Count == 1, 120, 10));
        }
    }

    internal sealed class DepartureRedirect(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            var departed = new List<NavigatedFromContext>();
            var arrived = new List<NavigatedToContext>();
            var nav = new NavigationHandle<string>(new NavigationStack<string>("a"));
            var redirected = false;
            var host = H.CreateHost();
            host.Mount(_ => (NavigationHost(nav, route =>
                Component<Page, PageProps>(new(route, departed, arrived, context =>
                {
                    if (!Equals(context.Route, "a") || nav.CurrentRoute != "c") return;
                    redirected = nav.Navigate("d", new()
                    {
                        PushToBackStack = false,
                        ConnectedAnimationKey = "d",
                        Transition = NavigationTransition.Fade(TimeSpan.FromMilliseconds(1200)),
                    });
                }))) with
            {
                Transition = NavigationTransition.None,
            }).AutomationId("review-host"));
            await Harness.Render();
            var baseline = host.Reconciler.ConnectedAnimationStartCount;
            nav.Navigate("b", new()
            {
                ConnectedAnimationKey = "hero",
                Transition = NavigationTransition.Fade(TimeSpan.FromSeconds(3)),
            });
            H.Check("ConnectedNav_RedirectInterruptsRealMotion", await Harness.WaitFor(() =>
                host.Reconciler.ConnectedAnimationStartCount == baseline + 1));
            var pages = H.FindControl<Grid>(g => Id(g) == "review-host")!;
            H.Check("ConnectedNav_RedirectInitialOperationPending",
                host.Reconciler._navigationHostNodes[pages].ConnectedOperation is { IsCompleted: false });
            nav.Navigate("c", new()
            {
                ConnectedAnimationKey = "c",
                Transition = NavigationTransition.Slide(duration: TimeSpan.FromSeconds(2)),
            });
            H.Check("ConnectedNav_DepartureCallbackRedirects", await Harness.WaitFor(() => redirected && nav.CurrentRoute == "d"));
            H.Check("ConnectedNav_RedirectKeepsItsConnectedKey", await Harness.WaitFor(() =>
                Equals(host.Reconciler._navigationHostNodes[pages].LastRenderedRoute, "d")
                && host.Reconciler._navigationHostNodes[pages].ConnectedOperation is { IsCompleted: false, PageAnimationBatch: not null }
                && host.Reconciler.ConnectedAnimationStartCount >= baseline + 2));
            var destination = pages.Children[^1];
            H.Check("ConnectedNav_RedirectKeepsItsFade", H.FindControl<Border>(b => Id(b) == "review-page-d") is not null
                && ElementCompositionPreview.GetElementVisual(destination).TryGetAnimationController("Opacity") is not null
                && ElementCompositionPreview.GetElementVisual(destination).TryGetAnimationController("Offset") is null
                && arrived.All(context => !Equals(context.Route, "d")));
            H.Check("ConnectedNav_RedirectArrivalWaitsForMotion", await Harness.WaitFor(() =>
                arrived.Any(context => Equals(context.Route, "d")), 150, 10));
            var arrival = arrived.SingleOrDefault(context => Equals(context.Route, "d"));
            H.Check("ConnectedNav_RedirectKeepsPreviousRouteAndMode", arrival is
                { PreviousRoute: "c", Mode: NavigationMode.Replace });
            H.Check("ConnectedNav_RedirectDeliversIntermediateDeparture", departed.Count(context =>
                context is { Route: "c", TargetRoute: "d", Mode: NavigationMode.Replace }) == 1);
            H.Check("ConnectedNav_RedirectDeparturesExactlyOnce", new[] { "a", "b", "c" }.All(route =>
                departed.Count(context => Equals(context.Route, route)) == 1) && pages.Children.Count == 1);
        }
    }

    internal sealed class MissingTargetPageMotion(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            var departed = new List<NavigatedFromContext>();
            var arrived = new List<NavigatedToContext>();
            var nav = new NavigationHandle<string>(new NavigationStack<string>("a"));
            var host = H.CreateHost();
            host.Mount(_ => (NavigationHost(nav, route =>
                Component<Page, PageProps>(new(route, departed, arrived, MissingTarget: route == "b"))) with
            {
                Transition = NavigationTransition.None,
            }).AutomationId("review-host"));
            await Harness.Render();
            var pages = H.FindControl<Grid>(g => Id(g) == "review-host")!;
            var source = (FrameworkElement)pages.Children[0];
            H.Check("ConnectedNav_FallbackSourceMeasured", source is { ActualWidth: > 0, ActualHeight: > 0 });
            var baseline = host.Reconciler.ConnectedAnimationStartCount;
            nav.Navigate("b", new()
            {
                ConnectedAnimationKey = "hero",
                Transition = NavigationTransition.Fade(TimeSpan.FromMilliseconds(1200)),
            });
            H.Check("ConnectedNav_MissingTargetRunsSelectedFade", await Harness.WaitFor(() =>
                pages.Children.Count == 2
                && ElementCompositionPreview.GetElementVisual(pages.Children[1]).TryGetAnimationController("Opacity") is not null, 90));
            H.Check("ConnectedNav_MissingTargetWaitsForFade", pages.Children.Count == 2 && arrived.Count == 0
                && ElementCompositionPreview.GetElementVisual(source).TryGetAnimationController("Opacity") is not null);
            H.Check("ConnectedNav_FallbackCompletesAfterSelectedFade", await Harness.WaitFor(() =>
                pages.Children.Count == 1 && arrived.Any(context => Equals(context.Route, "b")), 150, 10));
            H.Check("ConnectedNav_MissingTargetNeverStartsNative", host.Reconciler.ConnectedAnimationStartCount == baseline);
            H.Check("ConnectedNav_FallbackReleasesSelectedFade", ElementCompositionPreview.GetElementVisual(pages.Children[0])
                .TryGetAnimationController("Opacity") is null && pages.Children[0].IsHitTestVisible);
        }
    }

    private sealed record PageProps(string Route, List<NavigatedFromContext> Departed,
        List<NavigatedToContext> Arrived, Action<NavigatedFromContext>? OnDeparted = null, bool MissingTarget = false);

    private sealed class Page : Component<PageProps>
    {
        public override Element Render()
        {
            UseNavigationLifecycle(onNavigatedTo: context => Props.Arrived.Add(context), onNavigatedFrom: context =>
            {
                Props.Departed.Add(context);
                Props.OnDeparted?.Invoke(context);
            });
            return Border(VStack(
                Border(TextBlock(Props.Route)).Size(120, 40).ConnectedAnimation(Props.MissingTarget ? "missing" : "hero"),
                Border(TextBlock("C card")).Size(120, 30).ConnectedAnimation("c"),
                Border(TextBlock("D card")).Size(120, 30).ConnectedAnimation("d")))
                .AutomationId("review-page-" + Props.Route);
        }
    }

    private static string Id(DependencyObject element) => Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(element);
}
