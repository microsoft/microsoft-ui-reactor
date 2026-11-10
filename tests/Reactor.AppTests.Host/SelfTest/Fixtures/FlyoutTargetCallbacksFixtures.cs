using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System.Numerics;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>Issue #942: decorators must not replace their target's event identity.</summary>
internal static class FlyoutTargetCallbacksFixtures
{
    internal sealed class AnimateCurveAcrossNestedFlyouts(Harness h, bool vector) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cases = new (string Name, Curve Target, Curve? Decorator, Curve? Scope)[]
            {
                ("Target", Curve.Linear(600), null, null),
                ("Decorator", Curve.Spring(), Curve.Linear(600), null),
                ("Scope", Curve.Spring(), Curve.Spring(), Curve.Linear(600)),
            };
            foreach (var scenario in cases)
            {
                var name = $"FlyoutAnimate_{(vector ? "Vector3" : "Scalar")}_{scenario.Name}";
                var property = vector ? "Scale" : "Opacity";
                using var reconciler = new Reconciler();
                var decorated = Flyout(Flyout(Flyout(
                    Button("curve target").Size(120, 60).Animate(scenario.Target),
                    TextBlock("inner")), TextBlock("middle")), TextBlock("outer"));
                if (scenario.Decorator is { } decoratorCurve)
                    decorated = decorated.Animate(decoratorCurve);
                var button = (Button)reconciler.Mount(decorated, static () => { })!;
                H.SetContent(button);
                await Harness.Render();
                var visual = ElementCompositionPreview.GetElementVisual(button);
                try
                {
                    // Isolate the helper: implicit animations could otherwise hide a missing curve fallback.
                    bool hadImplicitAnimations = visual.ImplicitAnimations is not null;
                    visual.ImplicitAnimations = null;
                    H.Check(name + "_Isolated", hadImplicitAnimations
                        && visual.TryGetAnimationController(property) is null);
                    Action change = () =>
                    {
                        if (vector) AnimationHelper.SetOrAnimateVector3(button, property, new Vector3(1.2f));
                        else AnimationHelper.SetOrAnimate(button, property, 0.4f);
                    };
                    // The selected Linear curve has a keyframe controller; lower-priority Springs do not.
                    H.Check(name + "_SelectedCurve", () =>
                    {
                        if (scenario.Scope is { } scopeCurve)
                            AnimationScope.WithAnimation(scopeCurve, change);
                        else
                            change();
                        return visual.TryGetAnimationController(property) is not null;
                    });
                }
                finally
                {
                    visual.StopAnimation(property);
                    reconciler.UnmountChild(button);
                    H.SetContent(null);
                }
            }
        }
    }

    internal sealed class ConnectedNavigation(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var motion = TransitionEngine.OverrideAnimationsEnabled(true);
            using var host = H.CreateHost();
            var nav = new NavigationHandle<string>(new NavigationStack<string>("list"));
            const string key = "flyout-connected-navigation";
            host.Mount(_ => (NavigationHost(nav, route => Border(VStack(8,
                TextBlock(route),
                ConnectedFlyoutTarget(route == "list" ? "connected source" : "connected destination",
                    key, route == "list" ? 100 : 180)))) with
            {
                Transition = NavigationTransition.None,
            }).AutomationId("flyout-connected-host"));
            await Harness.Render();
            var source = H.FindButton("connected source");
            var pages = H.FindControl<Grid>(grid =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(grid) == "flyout-connected-host")!;
            H.Check("FlyoutConnectedNav_SourceMeasured", source is { ActualWidth: > 0, ActualHeight: > 0 });
            H.Check("FlyoutConnectedNav_SourceCollected", source is not null
                && ConnectedNavigationOperation.Collect(host.Reconciler, pages).Contains(source));
            int baseline = host.Reconciler.ConnectedAnimationStartCount;

            nav.Navigate("detail", new() { ConnectedAnimationKey = key, Transition = NavigationTransition.Fade() });
            H.Check("FlyoutConnectedNav_AnimationStarted", await Harness.WaitFor(() =>
                host.Reconciler.ConnectedAnimationStartCount == baseline + 1));
            H.Check("FlyoutConnectedNav_DestinationMeasured",
                H.FindButton("connected destination") is { ActualWidth: > 0, ActualHeight: > 0 });
            H.Check("FlyoutConnectedNav_Settled", await Harness.WaitFor(() => pages.Children.Count == 1, 100));
            H.Check("FlyoutConnectedNav_NoDuplicateStarts", host.Reconciler.ConnectedAnimationStartCount == baseline + 1);
        }
    }

    internal sealed class ConnectedSnapshotAcrossUnmount(Harness h, bool pooled) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var name = pooled ? "FlyoutConnectedPool" : "FlyoutConnectedUnmount";
            var key = pooled ? "flyout-connected-pool" : "flyout-connected-unmount";
            using var reconciler = new Reconciler();
            var panel = (StackPanel)reconciler.Mount(VStack(ConnectedFlyoutTarget("snapshot source", key, 100)), static () => { })!;
            H.SetContent(panel);
            await Harness.Render();
            var source = (Button)panel.Children[0];
            try
            {
                reconciler.FlushConnectedAnimations();
                H.Check(name + "_NoSourceNoStart", reconciler.ConnectedAnimationStartCount == 0);
                // Mount queues the destination. Lay it out before preparing the source snapshot.
                var destination = (Button)reconciler.Mount(ConnectedFlyoutTarget("snapshot destination", key, 180), static () => { })!;
                panel.Children.Add(destination);
                await Harness.Render();
                H.Check(name + "_BothMeasured", source.ActualWidth > 0 && source.ActualHeight > 0
                    && destination.ActualWidth > 0 && destination.ActualHeight > 0);

                if (pooled) reconciler.UnmountAndPool(source);
                else reconciler.UnmountChild(source);
                panel.Children.Remove(source);

                var animation = Microsoft.UI.Xaml.Media.Animation.ConnectedAnimationService.GetForCurrentView().GetAnimation(key);
                H.Check(name + "_SnapshotPrepared", animation is not null);
                bool completed = false;
                if (animation is not null) animation.Completed += (_, _) => completed = true;
                reconciler.FlushConnectedAnimations();
                bool started = reconciler.ConnectedAnimationStartCount == 1;
                H.Check(name + "_AnimationStarted", started);
                if (started)
                    H.Check(name + "_AnimationCompleted", await Harness.WaitFor(() => completed, 100));
            }
            finally
            {
                await Harness.Render();
                // Dispose the remaining destination without preparing an unused source snapshot.
                ConnectedNavigationOperation.UnmountPage(reconciler, panel);
                H.SetContent(null);
            }
        }
    }

    private static FlyoutElement ConnectedFlyoutTarget(string label, string key, double width) =>
        Flyout(Flyout(Flyout(Button(label).Size(width, 60).ConnectedAnimation(key),
            TextBlock("inner")), TextBlock("middle")), TextBlock("outer"));

    internal sealed class TargetExitTransition(Harness h, bool replace, int depth = 1) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var name = replace ? "FlyoutExit_Replacement" : "FlyoutExit_Removal";
            int contentCleanups = 0;
            var targetRef = new ElementRef();
            using var reconciler = new Reconciler();
            Element decorated = Button("exit target").Ref(targetRef)
                .Transition(Animation.Transition.Exit(Animation.Transition.Fade), Animation.Curve.Ease(600));
            for (var layer = 1; layer < depth; layer++)
                decorated = Flyout(decorated, TextBlock($"inner {layer}"));
            var initial = VStack(Flyout(decorated,
                Component<CleanupProbe, Action>(() => contentCleanups++)));
            var panel = (StackPanel)reconciler.Mount(initial, static () => { })!;
            H.SetContent(panel);
            await Harness.Render();
            var target = (Button)panel.Children[0];
            try
            {
                H.Check(name + "_Mounted", ReferenceEquals(targetRef.Current, target) && contentCleanups == 0);
                var next = replace ? VStack(TextBlock("replacement")) : VStack();
                reconciler.Update(initial, next, panel, static () => { });

                H.Check(name + "_ExitInFlight", reconciler.IsExiting(target) && panel.Children.Contains(target));
                H.Check(name + "_CleanupDeferred", ReferenceEquals(targetRef.Current, target) && contentCleanups == 0);
                H.Check(name + "_FadeStarted", reconciler.IsExiting(target)
                    && ElementCompositionPreview.GetElementVisual(target).TryGetAnimationController("Opacity") is not null);
                if (replace)
                    H.Check(name + "_ReplacementVisible", panel.Children.Count == 2
                        && panel.Children[0] is TextBlock { Text: "replacement" }
                        && ReferenceEquals(panel.Children[1], target));

                var completed = await Harness.WaitFor(() => !reconciler.HasExitingControls, maxPasses: 40, perPassMs: 50);
                H.Check(name + "_ExitCompleted", completed && !panel.Children.Contains(target)
                    && panel.Children.Count == (replace ? 1 : 0));
                H.Check(name + "_CleanupOnce", targetRef.Current is null && contentCleanups == 1);
            }
            finally
            {
                H.SetContent(null);
                reconciler.UnmountChild(panel);
            }
        }
    }

    internal sealed class DeepButtonCallbacksAcrossUpdates(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            var calls = new List<int>();
            Action? advance = null;
            ButtonElement? currentTarget = null;
            FlyoutElement? currentFlyout = null;
            host.Mount(ctx =>
            {
                var (version, setVersion) = ctx.UseState(0);
                advance = () => setVersion(version + 1);
                // The label stays the same, so each new callback takes the Button's shallow-skip path.
                currentTarget = Button("deep target", () => calls.Add(version));
                currentFlyout = Flyout(
                    Flyout(Flyout(currentTarget, TextBlock("inner")), TextBlock("middle")),
                    TextBlock("outer"));
                return currentFlyout;
            });
            await Harness.Render();
            var first = H.FindButton("deep target");
            H.Check("FlyoutDeep_Mounted", first is not null);
            if (first is null) return;

            for (var version = 0; version < 3; version++)
            {
                if (version > 0)
                {
                    advance!();
                    await Harness.Render();
                }
                var button = H.FindButton("deep target");
                var name = $"FlyoutDeep_Phase{version}";
                H.Check(name + "_SameControl", ReferenceEquals(first, button));
                H.Check(name + "_CurrentTarget", ReferenceEquals(Reconciler.GetElementTag(first), currentTarget));
                H.Check(name + "_CurrentDecorator", ReferenceEquals(Reconciler.GetMountedElement(first), currentFlyout));
                H.ClickButton("deep target");
                H.Check(name + "_CurrentClosure", calls.SequenceEqual(Enumerable.Range(0, version + 1)),
                    $"calls: {string.Join(", ", calls)}");
                first.Flyout?.Hide();
                await Harness.Render();
            }
        }
    }

    internal sealed class RegisterTypeCallbacksAcrossUpdates(Harness h, int depth = 1) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            var calls = new List<int>();
            Action? advance = null;
            RegisteredButtonElement? currentTarget = null;
            FlyoutElement? currentFlyout = null;
            host.Reconciler.RegisterType<RegisteredButtonElement, Button>(
                mount: static (_, element, _) =>
                {
                    var button = new Button { Content = $"registered target {element.Version}" };
                    button.Click += static (sender, _) =>
                        (Reconciler.GetElementTag((Button)sender!) as RegisteredButtonElement)?.OnClick();
                    return button;
                },
                update: static (_, _, element, button, _) =>
                {
                    button.Content = $"registered target {element.Version}";
                    return null;
                });
            host.Mount(ctx =>
            {
                var (version, setVersion) = ctx.UseState(0);
                advance = () => setVersion(1);
                currentTarget = new RegisteredButtonElement(version, () => calls.Add(version))
                    .WithKey("registered-target");
                Element decorated = currentTarget;
                for (var layer = 1; layer < depth; layer++)
                    decorated = Flyout(decorated, TextBlock($"registered inner {layer}"));
                currentFlyout = Flyout(decorated, TextBlock("registered target body"));
                return currentFlyout;
            });
            await Harness.Render();

            var first = H.FindButton("registered target 0");
            H.Check("FlyoutRegisterType_Mounted", first is not null);
            if (first is null) return;
            H.Check("FlyoutRegisterType_InitialTarget", ReferenceEquals(Reconciler.GetElementTag(first), currentTarget));
            H.Check("FlyoutRegisterType_InitialDecorator", ReferenceEquals(Reconciler.GetMountedElement(first), currentFlyout));
            H.ClickButton("registered target 0");
            H.Check("FlyoutRegisterType_InitialClosure", calls.SequenceEqual(new[] { 0 }), $"calls: {string.Join(", ", calls)}");
            first.Flyout?.Hide();

            advance!();
            await Harness.Render();
            var updated = H.FindButton("registered target 1");
            H.Check("FlyoutRegisterType_Updated", updated is not null);
            if (updated is null) return;
            H.Check("FlyoutRegisterType_SameControl", ReferenceEquals(first, updated));
            H.Check("FlyoutRegisterType_CurrentTarget", ReferenceEquals(Reconciler.GetElementTag(updated), currentTarget));
            H.Check("FlyoutRegisterType_CurrentDecorator", ReferenceEquals(Reconciler.GetMountedElement(updated), currentFlyout));
            H.ClickButton("registered target 1");
            H.Check("FlyoutRegisterType_CurrentClosure", calls.SequenceEqual(new[] { 0, 1 }), $"calls: {string.Join(", ", calls)}");
            updated.Flyout?.Hide();
        }
    }

    private sealed record RegisteredButtonElement(int Version, Action OnClick) : Element;

    internal sealed class TargetTeardownAcrossPaths(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            foreach (var (registered, pooled) in new[] { (false, false), (false, true), (true, false), (true, true) })
            {
                string name = $"FlyoutTeardown_{(registered ? "RegisterType" : "V1")}_{(pooled ? "Pool" : "Ordinary")}";
                int targetUnmounts = 0, contentCleanups = 0;
                var unmountOrder = new List<string>();
                Action onTargetUnmount = () => { targetUnmounts++; unmountOrder.Add("target"); };
                int trackersBefore = Reconciler.InteractionTrackerCountForTests;
                var targetRef = new ElementRef();
                var decoratorRef = new ElementRef();
                Action? remove = null;
                using var host = H.CreateHost();
                if (registered)
                {
                    host.Reconciler.RegisterType<TeardownButtonElement, Button>(
                        mount: static (_, element, _) => new Button { Content = element.Label },
                        update: static (_, _, element, button, _) => { button.Content = element.Label; return null; },
                        unmount: (_, _) => onTargetUnmount());
                }
                else
                    host.Reconciler.RegisterHandler<TeardownButtonElement, Button>(new TeardownButtonHandler(onTargetUnmount));
                host.Mount(ctx =>
                {
                    var (show, setShow) = ctx.UseState(true);
                    remove = () => setShow(false);
                    if (!show) return VStack();
                    Element target = new TeardownButtonElement(name).Ref(targetRef);
                    // Interaction states and keyframes taint the compositor; keep pool-reuse cases untainted.
                    if (!pooled)
                        target = target.InteractionStates(states => states.PointerOver(opacity: 0.8f))
                            .Keyframes("teardown", 1, keyframes => keyframes.Duration(1000).Loop()
                                .At(0f, translation: Vector3.Zero)
                                .At(1f, translation: new Vector3(8, 0, 0)));
                    var decorated = Flyout(target, Component<CleanupProbe, Action>(() =>
                    {
                        contentCleanups++;
                        unmountOrder.Add("flyout-content");
                    })).Ref(decoratorRef);
                    return pooled ? VStack(decorated) : decorated;
                });
                await Harness.Render();

                var first = H.FindButton(name);
                H.Check(name + "_RefsSetOnMount", first is not null
                    && ReferenceEquals(targetRef.Current, first) && ReferenceEquals(decoratorRef.Current, first));
                if (first is null) continue;
                H.Check(name + "_NoEarlyCleanup", targetUnmounts == 0 && contentCleanups == 0);
                var visual = !pooled ? ElementCompositionPreview.GetElementVisual(first) : null;
                if (!pooled)
                {
                    H.Check(name + "_InteractionRegistered", Reconciler.InteractionTrackerCountForTests == trackersBefore + 1);
                    H.Check(name + "_KeyframesStarted", visual!.TryGetAnimationController("Translation") is not null);
                }
                if (pooled) remove!();
                else host.Mount(_ => TextBlock("removed"));
                await Harness.Render();

                H.Check(name + "_TargetRefCleared", targetRef.Current is null);
                H.Check(name + "_DecoratorRefCleared", decoratorRef.Current is null);
                H.Check(name + "_TargetUnmountOnce", targetUnmounts == 1, $"unmounts: {targetUnmounts}");
                H.Check(name + "_ContentCleanupOnce", contentCleanups == 1, $"cleanups: {contentCleanups}");
                H.Check(name + "_UnmountOrder", unmountOrder.SequenceEqual(new[] { "flyout-content", "target" }),
                    $"order: {string.Join(", ", unmountOrder)}");
                if (!pooled)
                {
                    H.Check(name + "_InteractionCleared", Reconciler.InteractionTrackerCountForTests == trackersBefore);
                    H.Check(name + "_KeyframesStopped", visual!.TryGetAnimationController("Translation") is null);
                    // Stop the loop even if the cleanup assertion failed.
                    visual.StopAnimation("Translation");
                }
                if (pooled)
                {
                    host.Mount(_ => Button(name + " reused"));
                    await Harness.Render();
                    H.Check(name + "_SameControlReused", ReferenceEquals(first, H.FindButton(name + " reused")));
                    H.Check(name + "_OldOwnerStaysCleared", targetRef.Current is null
                        && decoratorRef.Current is null && targetUnmounts == 1 && contentCleanups == 1);
                }
            }
        }
    }

    private sealed record TeardownButtonElement(string Label) : Element;

    private sealed class TeardownButtonHandler(Action onUnmount) : IElementHandler<TeardownButtonElement, Button>
    {
        public Button Mount(MountContext ctx, TeardownButtonElement element) => new() { Content = element.Label };
        public void Update(UpdateContext ctx, TeardownButtonElement oldElement, TeardownButtonElement newElement, Button control)
            => control.Content = newElement.Label;
        public void Unmount(UnmountContext ctx, Button control) => onUnmount();
    }

    internal sealed class MenuFlyoutTargetCallbacks(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int decoratedClicks = 0, plainClicks = 0;
            using var host = H.CreateHost();
            host.Mount(_ => VStack(
                MenuFlyout(Button("menu target", () => decoratedClicks++), MenuItem("item")),
                Button("plain target", () => plainClicks++)));
            await Harness.Render();

            H.ClickButton("menu target");
            await Harness.Render();
            H.Check("Diag_MenuFlyoutTargetOnClick", decoratedClicks == 1);
            H.FindButton("menu target")?.Flyout?.Hide();
            H.ClickButton("plain target");
            H.Check("Diag_PlainButtonOnClick", plainClicks == 1);
        }
    }

    internal sealed class TeardownAndPlainPoolReuse(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int contentCleanups = 0, passThroughCleanups = 0, plainClicks = 0;
            using var host = H.CreateHost();
            host.Mount(_ => Flyout(Button("cleanup target", () => { }),
                Component<CleanupProbe, Action>(() => contentCleanups++)) with
            {
                OverlayInputPassThroughElement = Component<CleanupProbe, Action>(() => passThroughCleanups++),
            });
            await Harness.Render();
            var target = H.FindButton("cleanup target");
            H.Check("FlyoutTarget_CleanupTargetMounted", target is not null);
            if (target is null) return;
            H.Check("FlyoutTarget_CleanupsNotRunBeforeUnmount", contentCleanups == 0 && passThroughCleanups == 0);

            host.Mount(_ => TextBlock("removed"));
            await Harness.Render();
            H.Check("FlyoutTarget_ContentCleanupRuns", contentCleanups == 1);
            H.Check("FlyoutTarget_PassThroughCleanupRuns", passThroughCleanups == 1);

            // Root replacement unmounts without pooling; return this target explicitly.
            host.Reconciler.Pool.Return(target);
            H.Check("FlyoutTarget_PoolClearsBothIdentities", Reconciler.GetMountedElement(target) is null);
            host.Mount(_ => Button("reused plain target", () => plainClicks++));
            await Harness.Render();
            var plain = H.FindButton("reused plain target");
            H.Check("FlyoutTarget_PlainButtonReusesTarget", ReferenceEquals(target, plain));
            H.Check("FlyoutTarget_PlainButtonHasNoDecorator",
                plain is not null && Reconciler.GetMountedElement(plain) is ButtonElement);
            H.Check("FlyoutTarget_PlainButtonHasNoFlyout", plain?.Flyout is null);
            H.ClickButton("reused plain target");
            H.Check("FlyoutTarget_PlainButtonCallbackWorks", plainClicks == 1);

            host.Mount(_ => TextBlock("finished"));
            await Harness.Render();
            H.Check("FlyoutTarget_PlainUnmountDoesNotRepeatCleanup", contentCleanups == 1 && passThroughCleanups == 1);
        }
    }

    private sealed class CleanupProbe : Component<Action>
    {
        public override Element Render()
        {
            UseEffect(() => Props, []);
            return TextBlock("cleanup probe");
        }
    }

    internal sealed class CallbacksAcrossUpdates(Harness h, int kind) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var probe = new Probe(kind);
            var host = H.CreateHost();
            host.Mount(probe);
            await Harness.Render();
            var first = H.FindButton("target");
            H.Check("FlyoutTarget_InitialButtonMounted", first is not null);
            if (first is null) return;

            for (var phase = 0; phase < 4; phase++)
            {
                if (phase > 0)
                {
                    H.ClickButton("advance");
                    await Harness.Render();
                }
                H.Check($"FlyoutTarget_Phase{phase}", probe.Phase == phase);
                if (phase == 2)
                {
                    var replacement = H.FindControl<CheckBox>(c => c.Content as string == "replacement");
                    H.Check("FlyoutTarget_ReplacedWithCheckBox", replacement is not null);
                    H.ToggleCheckBox("replacement");
                    // Target replacement unmounts without pooling. Explicitly return the
                    // removed button to exercise both identity cleanup and its next rent.
                    host.Reconciler.ReturnControl(first);
                    H.Check("FlyoutTarget_OldIdentityCleared", Reconciler.GetMountedElement(first) is null);
                }
                else
                {
                    var button = H.FindButton("target");
                    H.Check($"FlyoutTarget_Phase{phase}_ButtonIdentity",
                        button is not null && Reconciler.GetElementTag(button) is ButtonElement);
                    H.Check($"FlyoutTarget_Phase{phase}_SameButton", ReferenceEquals(first, button));
                    H.ClickButton("target");
                    H.Check($"FlyoutTarget_Phase{phase}_Opens",
                        await Harness.WaitFor(() => button?.Flyout?.IsOpen == true));
                    // IsOpen becomes true before the opening animation completes.
                    // Closing immediately can cancel Opened even on a native Flyout.
                    if (kind == 0)
                        await Harness.WaitFor(() => probe.Opened[phase] == 1);
                    button?.Flyout?.Hide();
                    await Harness.WaitFor(() => button?.Flyout?.IsOpen == false);
                    await Harness.Render(50);
                }
                H.ToggleCheckBox("check");
                await Harness.Render();
                H.Check($"FlyoutTarget_Phase{phase}_TargetCallback", probe.Clicks[phase] == 1);
                H.Check($"FlyoutTarget_Phase{phase}_CheckCallback", probe.Checks[phase] == 1);
                for (var previous = 0; previous < phase; previous++)
                    H.Check($"FlyoutTarget_Phase{phase}_Previous{previous}Unchanged",
                        probe.Clicks[previous] == 1 && probe.Checks[previous] == 1);
                if (kind == 0 && phase != 2)
                    H.Check($"FlyoutTarget_Phase{phase}_CurrentDecoratorCallbacks",
                        await Harness.WaitFor(() => probe.Opened[phase] == 1 && probe.Closed[phase] == 1),
                        $"opened={probe.Opened[phase]}, closed={probe.Closed[phase]}");
            }
        }
    }

    internal sealed class NestedCallbacksAcrossUpdates(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            for (var kind = 0; kind < 3; kind++)
            {
                using var host = H.CreateHost();
                var probe = new NestedProbe(kind);
                host.Mount(probe);
                await Harness.Render();
                var first = H.FindButton("nested target");
                for (var phase = 0; phase < 2; phase++)
                {
                    if (phase > 0)
                    {
                        H.ClickButton("nested advance");
                        await Harness.Render();
                    }
                    var button = H.FindButton("nested target");
                    var name = $"FlyoutTarget_Nested{kind}_Phase{phase}";
                    H.Check(name + "_SameTarget", button is not null && ReferenceEquals(first, button));
                    if (button is null) continue;
                    H.Check(name + "_TargetIdentity", Reconciler.GetElementTag(button) is ButtonElement);
                    H.Check(name + "_DecoratorIdentity", ReferenceEquals(Reconciler.GetMountedElement(button), probe.Outer));
                    H.ClickButton("nested target");
                    H.Check(name + "_Opens", await Harness.WaitFor(() => button.Flyout?.IsOpen == true));
                    if (kind == 0)
                        H.Check(name + "_CurrentOpened", await Harness.WaitFor(() => probe.Opened[phase] == 1));
                    button.Flyout?.Hide();
                    await Harness.WaitFor(() => button.Flyout?.IsOpen == false);
                    await Harness.Render(50);
                    H.Check(name + "_TargetCallback", probe.Clicks == phase + 1);
                }
            }
        }
    }

    private sealed class NestedProbe : Component
    {
        private readonly int _kind;
        private readonly Element _inner;
        internal int Clicks;
        internal readonly int[] Opened = new int[2];
        internal Element? Outer;

        internal NestedProbe(int kind)
        {
            _kind = kind;
            // Reusing this exact element takes the inner decorator's shallow-skip path.
            // Its callback must refresh the decorator identity, not the button's tag.
            _inner = Flyout(Button("nested target", () => Clicks++), TextBlock("inner")) with
            {
                OnOpened = static () => { },
            };
        }

        public override Element Render()
        {
            var (phase, setPhase) = UseState(0);
            Outer = _kind switch
            {
                0 => Flyout(_inner, TextBlock($"outer-{phase}")) with
                {
                    OnOpened = () => Opened[phase]++,
                    AreOpenCloseAnimationsEnabled = false,
                },
                1 => MenuFlyout(_inner, MenuItem($"outer-{phase}")),
                _ => CommandBarFlyout(_inner, primaryCommands: [AppBarButton($"outer-{phase}")]),
            };
            return VStack(Button("nested advance", () => setPhase(phase + 1)), Outer);
        }
    }

    private sealed class Probe(int kind) : Component
    {
        internal int Phase;
        internal readonly int[] Clicks = new int[4];
        internal readonly int[] Checks = new int[4];
        internal readonly int[] Opened = new int[4];
        internal readonly int[] Closed = new int[4];

        public override Element Render()
        {
            var (phase, setPhase) = UseState(0);
            Phase = phase;
            Element Wrap(Element target) => kind switch
            {
                0 => Flyout(target, TextBlock("body")) with
                {
                    Placement = WinPrim.FlyoutPlacementMode.Top,
                    AreOpenCloseAnimationsEnabled = false,
                    OnOpened = () => Opened[phase]++,
                    OnClosed = () => Closed[phase]++,
                },
                1 => MenuFlyout(target, MenuItem("item")),
                _ => CommandBarFlyout(target, primaryCommands: [AppBarButton("command")]) with
                {
                    Placement = WinPrim.FlyoutPlacementMode.Top,
                },
            };
            Element target = phase == 2
                ? CheckBox(false, _ => Clicks[phase]++, label: "replacement")
                : Button("target", () => Clicks[phase]++);
            return VStack(
                Button("advance", () => setPhase(phase + 1)),
                Wrap(target),
                Wrap(CheckBox(false, _ => Checks[phase]++, label: "check")));
        }
    }
}
