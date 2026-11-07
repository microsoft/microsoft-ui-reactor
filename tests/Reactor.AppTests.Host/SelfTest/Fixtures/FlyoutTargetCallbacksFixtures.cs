using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>Issue #942: decorators must not replace their target's event identity.</summary>
internal static class FlyoutTargetCallbacksFixtures
{
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
