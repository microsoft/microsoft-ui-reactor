using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression coverage for "CommandBarFlyout renders a button that does nothing".
///
/// <c>OverlayLifecycle.MountCommandBarFlyout</c> used to install the flyout as
/// <c>FlyoutBase.AttachedFlyout</c> metadata, which only ever opens via an explicit
/// <c>ShowAttachedFlyout</c> call that nothing in Reactor makes — so
/// <c>CommandBarFlyout(Button("Show Commands"), primaryCommands: ...)</c> produced a dead
/// button. Its two sibling overlays (Flyout, MenuFlyout) both go through
/// <c>Reconciler.SetFlyoutOnControl</c>, which puts the flyout in <c>Button.Flyout</c> /
/// <c>SplitButton.Flyout</c> so a click opens it natively. WinUI's own AttachedFlyout docs
/// say the same: "To attach a flyout to a Button, use Button.Flyout instead."
///
/// The update path had the matching half of the bug: it looked the existing flyout up with
/// <c>GetAttachedFlyout</c> only, so once mount stopped writing there every re-render would
/// build a *second* flyout and drop it in the slot nobody reads, leaving the live one stale.
///
/// Placement is pinned to <c>Top</c> throughout: the default <c>Auto</c> has its own
/// separate open-time issue and would confound these assertions.
/// </summary>
internal static class CommandBarFlyoutWiringFixtures
{
    private static bool SameInstance(object? a, object? b) => a is not null && ReferenceEquals(a, b);

    /// <summary>
    /// Waits (bounded) for a flyout to report open. Opening runs through WinUI's popup
    /// machinery — deferred to the target's Loaded at mount time, and serialized behind
    /// any still-closing popup — so a single render pass isn't a reliable barrier. A real
    /// regression still fails: the flyout never opens within the whole budget.
    /// </summary>
    private static async Task<bool> WaitOpen(WinPrim.FlyoutBase? flyout)
    {
        for (int i = 0; i < 40 && flyout?.IsOpen != true; i++)
            await Harness.Render(25);
        return flyout?.IsOpen == true;
    }

    /// <summary>Closes a flyout and waits for it to settle so it can't leak into the next fixture.</summary>
    private static async Task CloseAndSettle(WinPrim.FlyoutBase? flyout)
    {
        flyout?.Hide();
        for (int i = 0; i < 20 && flyout?.IsOpen == true; i++)
            await Harness.Render(25);
        await Harness.Render(50);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Mount installs into the target's own Flyout slot (the click-to-open
    //  one), and Update finds it back there instead of duplicating it.
    // ════════════════════════════════════════════════════════════════════
    internal class TargetWiring(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                AppBarItemBase[] primary = phase == 0
                    ? [AppBarButton("cbfw-cut"), AppBarButton("cbfw-copy")]
                    : [AppBarButton("cbfw-paste")];
                return VStack(
                    Button("CbfWireGo", () => set(phase + 1)),
                    CommandBarFlyout(
                        Button("cbfw-target", () => { }),
                        primaryCommands: primary,
                        secondaryCommands: [AppBarButton("cbfw-more")]) with
                    {
                        Placement = WinPrim.FlyoutPlacementMode.Top,
                    });
            });

            await Harness.Render();
            var target0 = H.FindButton("cbfw-target");
            H.Check("CbfWire_TargetMounted", target0 is not null);

            // THE FIX: the flyout lands in Button.Flyout — the slot WinUI opens on click.
            var flyout0 = target0?.Flyout as CommandBarFlyout;
            H.Check("CbfWire_FlyoutInButtonSlot", flyout0 is not null);
            H.Check("CbfWire_MountPrimary2", flyout0?.PrimaryCommands.Count == 2);
            H.Check("CbfWire_MountSecondary1", flyout0?.SecondaryCommands.Count == 1);
            H.Check("CbfWire_MountPrimaryLabels",
                flyout0?.PrimaryCommands.Count == 2
                && (flyout0?.PrimaryCommands[0] as AppBarButton)?.Label == "cbfw-cut"
                && (flyout0?.PrimaryCommands[1] as AppBarButton)?.Label == "cbfw-copy");
            // ...and NOT also in the attached-flyout slot (would be a second, invisible copy).
            H.Check("CbfWire_MountNotAttached",
                target0 is not null && WinPrim.FlyoutBase.GetAttachedFlyout(target0) is null);

            // Update must read the flyout back from the slot mount wrote to. With an
            // attached-only lookup this branch creates a brand-new flyout, so the
            // instance changes AND the live Button.Flyout keeps the stale commands.
            H.ClickButton("CbfWireGo");
            await Harness.Render();
            var target1 = H.FindButton("cbfw-target");
            H.Check("CbfWire_TargetReusedInPlace", SameInstance(target0, target1));
            var flyout1 = target1?.Flyout as CommandBarFlyout;
            H.Check("CbfWire_FlyoutReusedInPlace", SameInstance(flyout0, flyout1));
            H.Check("CbfWire_UpdatePrimaryPatched",
                flyout1?.PrimaryCommands.Count == 1
                && (flyout1?.PrimaryCommands[0] as AppBarButton)?.Label == "cbfw-paste");
            H.Check("CbfWire_UpdateNoAttachedDuplicate",
                target1 is not null && WinPrim.FlyoutBase.GetAttachedFlyout(target1) is null);

            // The whole point of the Button.Flyout slot: a plain click on the target
            // opens the flyout, with no ShowAttachedFlyout call anywhere.
            H.ClickButton("cbfw-target");
            await Harness.Render();
            H.Check("CbfWire_TargetClickOpensFlyout", await WaitOpen(flyout1));
            await CloseAndSettle(flyout1);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  A non-button target still uses the attached-flyout fallback — the
    //  slot rule is per-target-type, not "always Button.Flyout".
    // ════════════════════════════════════════════════════════════════════
    internal class NonButtonTargetUsesAttachedSlot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                CommandBarFlyout(
                    TextBlock("cbfw-tb-target"),
                    primaryCommands: [AppBarButton("cbfw-tb-cut")]) with
                {
                    Placement = WinPrim.FlyoutPlacementMode.Top,
                }));

            await Harness.Render();
            var target = H.FindText("cbfw-tb-target");
            H.Check("CbfAttached_TargetMounted", target is not null);
            var flyout = target is null ? null : WinPrim.FlyoutBase.GetAttachedFlyout(target) as CommandBarFlyout;
            H.Check("CbfAttached_FlyoutInAttachedSlot", flyout is not null);
            H.Check("CbfAttached_PrimaryCommands1", flyout?.PrimaryCommands.Count == 1);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  CommandBarFlyoutElement.IsOpen — declarative open on the false→true
    //  edge of an update (the target is live, so ShowAt runs immediately).
    // ════════════════════════════════════════════════════════════════════
    internal class IsOpenOnUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (open, setOpen) = ctx.UseState(false);
                return VStack(
                    Button("CbfOpenGo", () => setOpen(true)),
                    CommandBarFlyout(
                        Button("cbfo-target", () => { }),
                        primaryCommands: [AppBarButton("cbfo-cut")]) with
                    {
                        Placement = WinPrim.FlyoutPlacementMode.Top,
                        IsOpen = open,
                    });
            });

            await Harness.Render();
            var flyout = H.FindButton("cbfo-target")?.Flyout as CommandBarFlyout;
            H.Check("CbfIsOpen_FlyoutInstalled", flyout is not null);
            H.Check("CbfIsOpen_ClosedWhenFalse", flyout?.IsOpen == false);

            H.ClickButton("CbfOpenGo");
            await Harness.Render();
            H.Check("CbfIsOpen_OpenedOnRisingEdge", await WaitOpen(flyout));

            // Don't leak an open popup into the next fixture.
            await CloseAndSettle(flyout);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  IsOpen already true at mount. The target has no XamlRoot while the
    //  tree is being built, so the show has to be deferred to its Loaded.
    // ════════════════════════════════════════════════════════════════════
    internal class IsOpenOnMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                CommandBarFlyout(
                    Button("cbfm-target", () => { }),
                    primaryCommands: [AppBarButton("cbfm-cut")]) with
                {
                    Placement = WinPrim.FlyoutPlacementMode.Top,
                    IsOpen = true,
                }));

            await Harness.Render();
            var target = H.FindButton("cbfm-target");
            var flyout = target?.Flyout as CommandBarFlyout;
            H.Check("CbfIsOpenMount_FlyoutInstalled", flyout is not null);

            // The show is deferred to the target's Loaded, which lands on the dispatcher
            // after the mount render — poll (bounded) instead of assuming one pass is enough.
            H.Check("CbfIsOpenMount_OpenedAfterLoaded", await WaitOpen(flyout));

            await CloseAndSettle(flyout);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Default (Auto) placement must survive being opened.
    //
    //  FlyoutPlacementMode.Auto (13) is outside the range FlyoutBase::ShowAtCore
    //  accepts, so writing it through fail-fasts the process with E_INVALIDARG
    //  the moment the flyout opens — which nothing noticed while CommandBarFlyout
    //  could never open at all. If this regresses the whole selftest host dies,
    //  which is exactly the signal we want.
    // ════════════════════════════════════════════════════════════════════
    internal class DefaultPlacementOpens(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (open, setOpen) = ctx.UseState(false);
                // No `with { Placement = ... }` — CommandBarFlyoutElement defaults to Auto.
                return VStack(
                    Button("CbfAutoGo", () => setOpen(true)),
                    CommandBarFlyout(
                        Button("cbfa-target", () => { }),
                        primaryCommands: [AppBarButton("cbfa-cut")]) with
                    {
                        IsOpen = open,
                    });
            });

            await Harness.Render();
            var flyout = H.FindButton("cbfa-target")?.Flyout as CommandBarFlyout;
            H.Check("CbfAuto_FlyoutInstalled", flyout is not null);
            // Auto is never written through; WinUI's own Placement default stands.
            // (`is CommandBarFlyout` so a null flyout can't pass this by accident.)
            H.Check($"CbfAuto_PlacementNotAuto[{flyout?.Placement}]",
                flyout is CommandBarFlyout { Placement: not WinPrim.FlyoutPlacementMode.Auto });

            H.ClickButton("CbfAutoGo");
            await Harness.Render();
            H.Check("CbfAuto_OpenedWithoutFailFast", await WaitOpen(flyout));

            await CloseAndSettle(flyout);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  A SplitButton target uses SplitButton.Flyout (its own slot — SplitButton
    //  does not derive from Button), not the attached-flyout metadata.
    // ════════════════════════════════════════════════════════════════════
    internal class SplitButtonTargetWiring(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                AppBarItemBase[] primary = phase == 0
                    ? [AppBarButton("cbfs-cut")]
                    : [AppBarButton("cbfs-copy"), AppBarButton("cbfs-paste")];
                return VStack(
                    Button("CbfSplitGo", () => set(phase + 1)),
                    CommandBarFlyout(
                        SplitButton("cbfs-target", () => { }),
                        primaryCommands: primary) with
                    {
                        Placement = WinPrim.FlyoutPlacementMode.Top,
                    });
            });

            await Harness.Render();
            var target0 = H.FindControl<SplitButton>(sb => sb.Content is string s && s == "cbfs-target");
            H.Check("CbfSplit_TargetMounted", target0 is not null);
            var flyout0 = target0?.Flyout as CommandBarFlyout;
            H.Check("CbfSplit_FlyoutInSplitButtonSlot", flyout0 is not null);
            H.Check("CbfSplit_MountPrimary1", flyout0?.PrimaryCommands.Count == 1);
            H.Check("CbfSplit_MountNotAttached",
                target0 is not null && WinPrim.FlyoutBase.GetAttachedFlyout(target0) is null);

            H.ClickButton("CbfSplitGo");
            await Harness.Render();
            var target1 = H.FindControl<SplitButton>(sb => sb.Content is string s && s == "cbfs-target");
            var flyout1 = target1?.Flyout as CommandBarFlyout;
            H.Check("CbfSplit_FlyoutReusedInPlace", SameInstance(flyout0, flyout1));
            H.Check("CbfSplit_UpdatePrimaryPatched",
                flyout1?.PrimaryCommands.Count == 2
                && (flyout1?.PrimaryCommands[0] as AppBarButton)?.Label == "cbfs-copy");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Placement explicit → Auto must land back on WinUI's default, matching
    //  what a fresh mount of the same element would produce. Merely skipping
    //  the Auto write would strand the previous explicit value.
    // ════════════════════════════════════════════════════════════════════
    internal class PlacementExplicitToAutoResets(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("CbfResetGo", () => set(1)),
                    CommandBarFlyout(
                        Button("cbfr-target", () => { }),
                        primaryCommands: [AppBarButton("cbfr-cut")]) with
                    {
                        Placement = phase == 0
                            ? WinPrim.FlyoutPlacementMode.Bottom
                            : WinPrim.FlyoutPlacementMode.Auto,
                    });
            });

            await Harness.Render();
            var flyout = H.FindButton("cbfr-target")?.Flyout as CommandBarFlyout;
            H.Check("CbfReset_ExplicitPlacementApplied",
                flyout?.Placement == WinPrim.FlyoutPlacementMode.Bottom);

            // The default a fresh Auto mount produces — captured from a real fresh flyout so
            // the assertion doesn't hard-code WinUI's default and silently rot.
            var freshDefault = new CommandBarFlyout().Placement;
            H.ClickButton("CbfResetGo");
            await Harness.Render();
            H.Check("CbfReset_AutoRestoresDefaultPlacement",
                flyout is CommandBarFlyout && flyout.Placement == freshDefault);
            // Value embedded in the TAP name (house style) so the resolved default is a
            // recorded measurement, not an assumption — this is the exact value an unset
            // CommandBarFlyout placement lands on.
            H.Check($"CbfReset_DefaultIsNotAuto[{freshDefault}]",
                freshDefault != WinPrim.FlyoutPlacementMode.Auto);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Unmount detaches the flyout so a recycled target can't keep showing
    //  the previous component's command bar.
    // ════════════════════════════════════════════════════════════════════
    internal class UnmountDetachesFlyout(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (shown, set) = ctx.UseState(true);
                return VStack(
                    Button("CbfUnmountGo", () => set(false)),
                    shown
                        ? CommandBarFlyout(
                            Button("cbfu-target", () => { }),
                            primaryCommands: [AppBarButton("cbfu-cut")]) with
                          {
                              Placement = WinPrim.FlyoutPlacementMode.Top,
                          }
                        : TextBlock("cbfu-gone"));
            });

            await Harness.Render();
            // Hold the control across the unmount so the detach is observable.
            var target = H.FindButton("cbfu-target");
            H.Check("CbfUnmount_FlyoutInstalled", target?.Flyout is CommandBarFlyout);

            H.ClickButton("CbfUnmountGo");
            await Harness.Render();
            H.Check("CbfUnmount_TargetRemoved", H.FindButton("cbfu-target") is null);
            H.Check("CbfUnmount_FlyoutDetached", target is not null && target.Flyout is null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Unmount detach must only reach the decorator's OWN target. Keyed
    //  reorder + removal is the case where an unmounting decorator could
    //  plausibly strip a sibling's (or a reused control's) live flyout.
    // ════════════════════════════════════════════════════════════════════
    internal class KeyedReorderKeepsSiblingFlyouts(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                Element A = CommandBarFlyout(
                    Button("cbfk-a", () => { }),
                    primaryCommands: [AppBarButton("cbfk-a-cut")]) with
                {
                    Key = "a",
                    Placement = WinPrim.FlyoutPlacementMode.Top,
                };
                Element B = CommandBarFlyout(
                    Button("cbfk-b", () => { }),
                    primaryCommands: [AppBarButton("cbfk-b-cut")]) with
                {
                    Key = "b",
                    Placement = WinPrim.FlyoutPlacementMode.Top,
                };
                Element[] children = phase switch
                {
                    0 => [A, B],
                    1 => [B, A],   // reorder
                    _ => [B],      // drop A entirely
                };
                return VStack([Button("CbfKeyGo", () => set(phase + 1)), .. children]);
            });

            static string? PrimaryLabel(Button? b)
            {
                if (b?.Flyout is not CommandBarFlyout f || f.PrimaryCommands.Count != 1) return null;
                return (f.PrimaryCommands[0] as AppBarButton)?.Label;
            }

            await Harness.Render();
            H.Check("CbfKeyed_MountA", PrimaryLabel(H.FindButton("cbfk-a")) == "cbfk-a-cut");
            H.Check("CbfKeyed_MountB", PrimaryLabel(H.FindButton("cbfk-b")) == "cbfk-b-cut");

            H.ClickButton("CbfKeyGo");
            await Harness.Render();
            H.Check("CbfKeyed_ReorderKeepsA", PrimaryLabel(H.FindButton("cbfk-a")) == "cbfk-a-cut");
            H.Check("CbfKeyed_ReorderKeepsB", PrimaryLabel(H.FindButton("cbfk-b")) == "cbfk-b-cut");

            H.ClickButton("CbfKeyGo");
            await Harness.Render();
            H.Check("CbfKeyed_RemovedAGone", H.FindButton("cbfk-a") is null);
            // The survivor must keep its own flyout — the removed sibling's unmount
            // must not detach it.
            H.Check("CbfKeyed_SurvivorKeepsFlyout", PrimaryLabel(H.FindButton("cbfk-b")) == "cbfk-b-cut");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Wrapping a target in CommandBarFlyout must not cost the target its
    //  own callbacks.
    //
    //  Issue #942: the decorator and target share a native control, but
    //  must keep separate element identities for their own callbacks.
    // ════════════════════════════════════════════════════════════════════
    internal class TargetKeepsItsOwnCallbacks(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var clicks = 0;
            var checks = 0;
            host.Mount(ctx => VStack(
                CommandBarFlyout(
                    Button("cbfcb-btn", () => clicks++),
                    primaryCommands: [AppBarButton("cbfcb-cut")]) with
                {
                    Placement = WinPrim.FlyoutPlacementMode.Top,
                },
                CommandBarFlyout(
                    CheckBox(false, _ => checks++, label: "cbfcb-chk"),
                    primaryCommands: [AppBarButton("cbfcb-copy")]) with
                {
                    Placement = WinPrim.FlyoutPlacementMode.Top,
                }));

            await Harness.Render();
            H.Check("CbfCallbacks_ButtonMounted", H.FindButton("cbfcb-btn") is not null);
            H.Check("CbfCallbacks_CheckBoxMounted",
                H.FindControl<CheckBox>(c => c.Content is string s && s == "cbfcb-chk") is not null);

            // The same click opens the flyout and invokes the target's own callback.
            H.ClickButton("cbfcb-btn");
            await Harness.Render();
            var button = H.FindButton("cbfcb-btn");
            H.Check("CbfCallbacks_ButtonClickOpensFlyout", await WaitOpen(button?.Flyout));
            H.Check("CbfCallbacks_ButtonOnClickFired", clicks == 1);
            await CloseAndSettle(button?.Flyout);

            // Non-button targets use the attached slot and still dispatch their own events.
            H.ToggleCheckBox("cbfcb-chk");
            await Harness.Render();
            H.Check("CbfCallbacks_CheckBoxOnChangedFired", checks == 1);
        }
    }
}
