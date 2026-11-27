using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #1753: <c>.WithFlyout(...)</c> on a non-Button target writes
/// <c>FlyoutBase.AttachedFlyout</c>. A pooled control used to keep the previous renter's
/// flyout, and dropping the modifier on an update left it attached. Each case also checks
/// that the flyout's content component was unmounted, not just detached.
/// </summary>
public static class FlyoutAttachmentPoolFixtures
{
    private static int s_cleanupCount;

    private sealed class CleanupChild : Component
    {
        public override Element Render()
        {
            UseEffect(() => () => global::System.Threading.Interlocked.Increment(ref s_cleanupCount));
            return TextBlock("flyout-content");
        }
    }

    // The carrier sits at the VStack tail, so it is the one control returned to the pool and
    // rented back. The instance check keeps the null checks non-vacuous: a fresh control has no
    // flyout either. The renter declares no flyout, so nothing but the pool return can clear it.
    internal class WithFlyoutPoolReturn(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_cleanupCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("DropFlyoutCarrier", () => set(1)),
                    Button("RemountFlyoutCarrier", () => set(2)),
                    phase switch
                    {
                        0 => TextBlock("flyout-pool-carrier").WithFlyout(Component<CleanupChild>()),
                        1 => Empty(),
                        _ => TextBlock("flyout-pool-carrier-2"),
                    });
            });

            await Harness.Render();
            var first = H.FindText("flyout-pool-carrier");
            H.Check("WithFlyoutPool_Phase0_Attached",
                first is not null && WinPrim.FlyoutBase.GetAttachedFlyout(first) is WinUI.Flyout);
            H.Check("WithFlyoutPool_Phase0_NoCleanupYet", s_cleanupCount == 0);

            H.ClickButton("DropFlyoutCarrier");
            await Harness.Render();
            H.Check("WithFlyoutPool_Phase1_ContentUnmounted", s_cleanupCount == 1);

            H.ClickButton("RemountFlyoutCarrier");
            await Harness.Render();
            var second = H.FindText("flyout-pool-carrier-2");
            H.Check("WithFlyoutPool_Phase2_ReusedInstance", first is not null && ReferenceEquals(first, second));
            H.Check("WithFlyoutPool_Phase2_FlyoutCleared",
                second is not null && WinPrim.FlyoutBase.GetAttachedFlyout(second) is null);
        }
    }

    // An attached flyout Reactor did not install (here an .OnMount write) is not the
    // reconciler's to unmount, so only the pool's own reset can drop it.
    internal class ForeignAttachedFlyoutPoolReturn(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("DropForeignFlyout", () => set(1)),
                    Button("RemountForeignFlyout", () => set(2)),
                    phase switch
                    {
                        0 => TextBlock("foreign-flyout-carrier").OnMount(fe =>
                            WinPrim.FlyoutBase.SetAttachedFlyout(fe, new WinUI.Flyout { Content = new WinUI.TextBlock() })),
                        1 => Empty(),
                        _ => TextBlock("foreign-flyout-carrier-2"),
                    });
            });

            await Harness.Render();
            var first = H.FindText("foreign-flyout-carrier");
            H.Check("ForeignFlyoutPool_Phase0_Attached",
                first is not null && WinPrim.FlyoutBase.GetAttachedFlyout(first) is not null);

            H.ClickButton("DropForeignFlyout");
            await Harness.Render();
            H.ClickButton("RemountForeignFlyout");
            await Harness.Render();
            var second = H.FindText("foreign-flyout-carrier-2");
            H.Check("ForeignFlyoutPool_Phase2_ReusedInstance", first is not null && ReferenceEquals(first, second));
            H.Check("ForeignFlyoutPool_Phase2_FlyoutCleared",
                second is not null && second.ReadLocalValue(WinPrim.FlyoutBase.AttachedFlyoutProperty) == DependencyProperty.UnsetValue);
        }
    }

    // Set → unset on a control that stays mounted, for both flyout slots (the attached slot on a
    // TextBlock, Button.Flyout on a Button) and for a MenuFlyout. The TextBlock's flyout content
    // carries its own .WithFlyout, which is released by the unmount walk over that content.
    internal class WithFlyoutDroppedOnUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_cleanupCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (dropped, set) = ctx.UseState(false);
                Element text = TextBlock("flyout-unset-text");
                Element button = Button("flyout-unset-button", () => { });
                Element menu = TextBlock("flyout-unset-menu");
                return VStack(
                    Button("DropFlyoutModifier", () => set(true)),
                    dropped ? text : text.WithFlyout(TextBlock("flyout-nested").WithFlyout(Component<CleanupChild>())),
                    dropped ? button : button.WithFlyout(Component<CleanupChild>()),
                    dropped ? menu : menu.WithFlyout(MenuItems(MenuItem("Copy"))));
            });

            await Harness.Render();
            var text = H.FindText("flyout-unset-text");
            var button = H.FindButton("flyout-unset-button");
            var menu = H.FindText("flyout-unset-menu");
            H.Check("WithFlyoutUnset_Phase0_Attached",
                text is not null && WinPrim.FlyoutBase.GetAttachedFlyout(text) is WinUI.Flyout
                && button?.Flyout is WinUI.Flyout
                && menu is not null && WinPrim.FlyoutBase.GetAttachedFlyout(menu) is WinUI.MenuFlyout);
            H.Check("WithFlyoutUnset_Phase0_NoCleanupYet", s_cleanupCount == 0);

            H.ClickButton("DropFlyoutModifier");
            await Harness.Render();
            H.Check("WithFlyoutUnset_Phase1_SameControls",
                ReferenceEquals(text, H.FindText("flyout-unset-text"))
                && button == H.FindButton("flyout-unset-button")
                && ReferenceEquals(menu, H.FindText("flyout-unset-menu")));
            H.Check("WithFlyoutUnset_Phase1_TextFlyoutCleared",
                text is not null && text.ReadLocalValue(WinPrim.FlyoutBase.AttachedFlyoutProperty) == DependencyProperty.UnsetValue);
            H.Check("WithFlyoutUnset_Phase1_ButtonFlyoutCleared",
                button is not null && button.ReadLocalValue(WinUI.Button.FlyoutProperty) == DependencyProperty.UnsetValue);
            H.Check("WithFlyoutUnset_Phase1_MenuFlyoutCleared",
                menu is not null && menu.ReadLocalValue(WinPrim.FlyoutBase.AttachedFlyoutProperty) == DependencyProperty.UnsetValue);
            // One from the Button's content, one from the nested flyout inside the TextBlock's.
            H.Check("WithFlyoutUnset_Phase1_ContentUnmounted", s_cleanupCount == 2);
        }
    }

    // A keyed Memo's modifiers land on its factory output's control, which carries the inner
    // element's tag, so the release can't rely on finding .WithFlyout in that tag.
    internal class KeyedMemoWithFlyoutUnmount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_cleanupCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (shown, set) = ctx.UseState(true);
                return VStack(
                    Button("DropFlyoutMemo", () => set(false)),
                    shown
                        ? Memo("flyout-memo", static () => TextBlock("flyout-memo-carrier")).WithFlyout(Component<CleanupChild>())
                        : Empty());
            });

            await Harness.Render();
            var carrier = H.FindText("flyout-memo-carrier");
            H.Check("WithFlyoutMemo_Phase0_Attached",
                carrier is not null && WinPrim.FlyoutBase.GetAttachedFlyout(carrier) is WinUI.Flyout);

            H.ClickButton("DropFlyoutMemo");
            await Harness.Render();
            H.Check("WithFlyoutMemo_Phase1_Removed", H.FindText("flyout-memo-carrier") is null);
            H.Check("WithFlyoutMemo_Phase1_ContentUnmounted", s_cleanupCount == 1);
        }
    }

    // Dropping .WithFlyout in the same update that sets the button's own flyout: argument. The
    // descriptor writes the new flyout first, so the release must take only the flyout the
    // modifier installed and leave the new one in the slot.
    internal class WithFlyoutHandsOffToButtonFlyout(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_cleanupCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (swapped, set) = ctx.UseState(false);
                return VStack(
                    Button("SwapFlyoutSource", () => set(true)),
                    swapped
                        ? DropDownButton("flyout-handoff", TextBlock("flyout-handoff-own"))
                        : DropDownButton("flyout-handoff").WithFlyout(Component<CleanupChild>()));
            });

            await Harness.Render();
            var dropDown = H.FindButton("flyout-handoff");
            H.Check("WithFlyoutHandoff_Phase0_ModifierFlyout",
                dropDown?.Flyout is WinUI.Flyout);

            H.ClickButton("SwapFlyoutSource");
            await Harness.Render();
            H.Check("WithFlyoutHandoff_Phase1_SameControl", dropDown == H.FindButton("flyout-handoff"));
            H.Check("WithFlyoutHandoff_Phase1_OwnFlyoutKept",
                dropDown?.Flyout is WinUI.Flyout { Content: WinUI.TextBlock { Text: "flyout-handoff-own" } });
            H.Check("WithFlyoutHandoff_Phase1_ModifierContentUnmounted", s_cleanupCount == 1);
        }
    }

    // The button's own flyout: argument arrives while .WithFlyout stays declared, then the modifier
    // is dropped. The argument's flyout keeps its content throughout, and the modifier's content
    // is released only when the modifier goes.
    internal class WithFlyoutKeptBesideButtonFlyout(Harness h) : SelfTestFixtureBase(h)
    {
        private static Element ModifierContent(string text) =>
            TextBlock(text).OnUnmount(static _ => global::System.Threading.Interlocked.Increment(ref s_cleanupCount));

        public override async Task RunAsync()
        {
            s_cleanupCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("AddOwnFlyout", () => set(1)),
                    Button("DropModifierFlyout", () => set(2)),
                    phase switch
                    {
                        // The modifier's content changes as the argument arrives, so patching the
                        // slot's flyout instead of the modifier's own would overwrite the argument's.
                        0 => DropDownButton("flyout-beside").WithFlyout(ModifierContent("flyout-beside-mod-0")),
                        1 => DropDownButton("flyout-beside", TextBlock("flyout-beside-own")).WithFlyout(ModifierContent("flyout-beside-mod-1")),
                        _ => DropDownButton("flyout-beside", TextBlock("flyout-beside-own")),
                    });
            });

            await Harness.Render();
            var dropDown = H.FindButton("flyout-beside");
            H.Check("WithFlyoutBeside_Phase0_ModifierFlyout", dropDown?.Flyout is WinUI.Flyout);

            H.ClickButton("AddOwnFlyout");
            await Harness.Render();
            H.Check("WithFlyoutBeside_Phase1_OwnFlyoutInstalled",
                dropDown?.Flyout is WinUI.Flyout { Content: WinUI.TextBlock { Text: "flyout-beside-own" } });
            H.Check("WithFlyoutBeside_Phase1_ModifierContentStillMounted", s_cleanupCount == 0);

            H.ClickButton("DropModifierFlyout");
            await Harness.Render();
            H.Check("WithFlyoutBeside_Phase2_SameControl", dropDown == H.FindButton("flyout-beside"));
            H.Check("WithFlyoutBeside_Phase2_OwnFlyoutKept",
                dropDown?.Flyout is WinUI.Flyout { Content: WinUI.TextBlock { Text: "flyout-beside-own" } });
            H.Check("WithFlyoutBeside_Phase2_ModifierContentUnmounted", s_cleanupCount == 1);
        }
    }

    // The modifier is dropped while the button's own flyout: argument stays the same instance, so
    // the descriptor doesn't write it again. The flyout the modifier displaced takes the slot back.
    internal class WithFlyoutDroppedBesideUnchangedButtonFlyout(Harness h) : SelfTestFixtureBase(h)
    {
        private static readonly Element s_own = TextBlock("flyout-unchanged-own");

        public override async Task RunAsync()
        {
            s_cleanupCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (dropped, set) = ctx.UseState(false);
                Element dropDown = DropDownButton("flyout-unchanged", s_own);
                return VStack(
                    Button("DropModifierBesideOwn", () => set(true)),
                    dropped ? dropDown : dropDown.WithFlyout(Component<CleanupChild>()));
            });

            await Harness.Render();
            var dropDown = H.FindButton("flyout-unchanged");
            // The modifier is applied after the descriptor's argument, so it holds the slot.
            H.Check("WithFlyoutUnchangedOwn_Phase0_ModifierFlyout",
                dropDown?.Flyout is WinUI.Flyout { Content: not WinUI.TextBlock { Text: "flyout-unchanged-own" } });

            H.ClickButton("DropModifierBesideOwn");
            await Harness.Render();
            H.Check("WithFlyoutUnchangedOwn_Phase1_SameControl", dropDown == H.FindButton("flyout-unchanged"));
            H.Check("WithFlyoutUnchangedOwn_Phase1_OwnFlyoutRestored",
                dropDown?.Flyout is WinUI.Flyout { Content: WinUI.TextBlock { Text: "flyout-unchanged-own" } });
            H.Check("WithFlyoutUnchangedOwn_Phase1_ModifierContentUnmounted", s_cleanupCount == 1);
        }
    }

    // A flyout a Style supplies is not a local value, so dropping .WithFlyout clears the slot and
    // the Style's flyout shows through again. Restoring it as a local value would pin it.
    internal class WithFlyoutDroppedOverStyledFlyout(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            s_cleanupCount = 0;
            var styledFlyout = new WinUI.Flyout { Content = new WinUI.TextBlock { Text = "flyout-styled" } };
            var style = new Style(typeof(WinUI.Button));
            style.Setters.Add(new Setter(WinUI.Button.FlyoutProperty, styledFlyout));
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (dropped, set) = ctx.UseState(false);
                Element button = Button("flyout-styled-button", () => { }).Set(b => b.Style = style);
                return VStack(
                    Button("DropModifierOverStyle", () => set(true)),
                    dropped ? button : button.WithFlyout(Component<CleanupChild>()));
            });

            await Harness.Render();
            var button = H.FindButton("flyout-styled-button");
            H.Check("WithFlyoutStyled_Phase0_ModifierFlyout",
                button is not null && button.Flyout is WinUI.Flyout && !ReferenceEquals(button.Flyout, styledFlyout));

            H.ClickButton("DropModifierOverStyle");
            await Harness.Render();
            H.Check("WithFlyoutStyled_Phase1_SameControl", button == H.FindButton("flyout-styled-button"));
            H.Check("WithFlyoutStyled_Phase1_NoLocalFlyout",
                button is not null && button.ReadLocalValue(WinUI.Button.FlyoutProperty) == DependencyProperty.UnsetValue);
            H.Check("WithFlyoutStyled_Phase1_StyledFlyoutShows",
                button is not null && ReferenceEquals(button.Flyout, styledFlyout));
            H.Check("WithFlyoutStyled_Phase1_ModifierContentUnmounted", s_cleanupCount == 1);
        }
    }
}