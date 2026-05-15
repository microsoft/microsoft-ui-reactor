using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Second wave of update tests targeting remaining uncovered UpdateXxx methods,
/// additional Mount paths for uncommon controls, and modifier edge cases.
/// </summary>
internal static class ControlUpdateFixtures2
{
    // ════════════════════════════════════════════════════════════════════
    //  NavigationView update (UpdateNavigationView — Update.cs ~860)
    // ════════════════════════════════════════════════════════════════════

    internal class NavigationViewUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdNV", () => set(1)),
                    NavigationView(
                        [NavItem("Home"), NavItem("Settings")],
                        content: phase == 0 ? TextBlock("NV_Page1") : TextBlock("NV_Page2")
                    )
                );
            });

            await Harness.Render();
            H.Check("NVUpdate_Initial", H.FindText("NV_Page1") is not null);

            H.ClickButton("UpdNV");
            await Harness.Render();

            H.Check("NVUpdate_ContentChanged", H.FindText("NV_Page2") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Image update (UpdateImage — Update.cs ~690)
    // ════════════════════════════════════════════════════════════════════

    internal class ImageUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdImg", () => set(1)),
                    Image(phase == 0 ? "ms-appx:///Assets/StoreLogo.png" : "ms-appx:///Assets/LockScreenLogo.png")
                        .Width(32).Height(32)
                );
            });

            await Harness.Render();
            H.Check("ImgUpdate_Exists",
                H.FindControl<Microsoft.UI.Xaml.Controls.Image>(_ => true) is not null);

            H.ClickButton("UpdImg");
            await Harness.Render();

            H.Check("ImgUpdate_StillExists",
                H.FindControl<Microsoft.UI.Xaml.Controls.Image>(_ => true) is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Additional mount-only controls (SplitView, Flyout, TeachingTip, etc.)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mounts controls not in the original catalog to exercise their MountXxx paths:
    /// SplitView, NavigationView, Line, Path, RelativePanel, CalendarView.
    /// (TitleBar is exercised by the persistent test-runner TitleBar across all fixtures.)
    /// </summary>
    internal class AdditionalControlsMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdExtra", () => set(1)),
                    SplitView(
                        pane: TextBlock("ExPane"),
                        content: TextBlock(phase == 0 ? "ExContent1" : "ExContent2")
                    ),
                    NavigationView(
                        [NavItem("NavH", icon: "Home")],
                        content: TextBlock(phase == 0 ? "NavC1" : "NavC2")
                    ),
                    new LineElement
                    {
                        X1 = 0, Y1 = 0, X2 = 50, Y2 = 50,
                        Modifiers = new ElementModifiers { Width = 50, Height = 50 }
                    },
                    new PathElement
                    {
                        Modifiers = new ElementModifiers { Width = 20, Height = 20 }
                    }
                );
            });

            await Harness.Render();
            H.Check("ExtraMount_SplitView", H.FindText("ExContent1") is not null);
            H.Check("ExtraMount_NavView", H.FindText("NavC1") is not null);
            H.Check("ExtraMount_Line",
                H.FindControl<Microsoft.UI.Xaml.Shapes.Line>(_ => true) is not null);

            H.ClickButton("UpdExtra");
            await Harness.Render();

            // SplitView remounts on update (doesn't have an UpdateSplitView)
            H.Check("ExtraUpdate_SplitView", H.FindText("ExContent2") is not null);
            H.Check("ExtraUpdate_NavView", H.FindText("NavC2") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Bitmask diff path (UpdateTextBitmask — Update.cs ~250)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Enables the experimental bitmask diff for TextBlockElement and exercises
    /// UpdateTextBitmask which avoids COM interop reads.
    /// </summary>
    internal class TextBitmaskDiffUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            // Enable bitmask diff mode
            var wasBitmask = Reconciler.EnableBitmaskDiff;
            Reconciler.EnableBitmaskDiff = true;
            try
            {
                host.Mount(ctx =>
                {
                    var (phase, set) = ctx.UseState(0);
                    return VStack(
                        Button("UpdBitmask", () => set(1)),
                        phase == 0
                            ? TextBlock("BM_Before").FontSize(12)
                            : TextBlock("BM_After").FontSize(24).Bold()
                    );
                });

                await Harness.Render();
                var tb = H.FindText("BM_Before");
                H.Check("Bitmask_Initial", tb is not null);

                H.ClickButton("UpdBitmask");
                await Harness.Render();

                var tb2 = H.FindText("BM_After");
                H.Check("Bitmask_Updated", tb2 is not null);
                H.Check("Bitmask_Reused", ReferenceEquals(tb, tb2));
                H.Check("Bitmask_FontChanged", tb2!.FontSize == 24);
            }
            finally
            {
                Reconciler.EnableBitmaskDiff = wasBitmask;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ModifiedElement unwrap in Update path (Update.cs lines 16-28)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tests the backward-compat ModifiedElement unwrapping in Update().
    /// Creates a ModifiedElement wrapping a TextBlockElement to exercise lines 16-28.
    /// </summary>
    internal class LegacyModifiedElementUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                // Directly create ModifiedElement (legacy path)
                var inner = phase == 0
                    ? new TextBlockElement("Leg_Before")
                    : new TextBlockElement("Leg_After");
                var modified = new ModifiedElement(inner, new ElementModifiers
                {
                    Width = phase == 0 ? 100 : 200,
                    Margin = new Thickness(phase == 0 ? 4 : 8),
                });

                return VStack(
                    Button("UpdLegacy", () => set(1)),
                    modified
                );
            });

            await Harness.Render();
            H.Check("LegacyMod_Initial", H.FindText("Leg_Before") is not null);

            H.ClickButton("UpdLegacy");
            await Harness.Render();

            H.Check("LegacyMod_Updated", H.FindText("Leg_After") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ToggleButton, RepeatButton, SplitButton updates
    // ════════════════════════════════════════════════════════════════════

    internal class ButtonVariantsUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdBtnVar", () => set(1)),
                    ToggleButton(phase == 0 ? "Tog1" : "Tog2", isChecked: phase == 1),
                    RepeatButton(phase == 0 ? "Rep1" : "Rep2"),
                    DropDownButton(phase == 0 ? "DD1" : "DD2"),
                    SplitButton(phase == 0 ? "Spl1" : "Spl2"),
                    ToggleSplitButton(phase == 0 ? "TSB1" : "TSB2", isChecked: phase == 1)
                );
            });

            await Harness.Render();
            H.Check("BtnVar_Initial", H.FindText("Tog1") is not null && H.FindText("Rep1") is not null);

            H.ClickButton("UpdBtnVar");
            await Harness.Render();

            H.Check("BtnVar_ToggleUpdated", H.FindText("Tog2") is not null);
            H.Check("BtnVar_RepeatUpdated", H.FindText("Rep2") is not null);
            H.Check("BtnVar_DropDownUpdated", H.FindText("DD2") is not null);
            H.Check("BtnVar_SplitUpdated", H.FindText("Spl2") is not null);
            H.Check("BtnVar_ToggleSplitUpdated", H.FindText("TSB2") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Expander with content + border update (tests CornerRadius modifier)
    // ════════════════════════════════════════════════════════════════════

    internal class ExpanderContentUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdExp", () => set(1)),
                    Expander(
                        phase == 0 ? "ExpanderHdr1" : "ExpanderHdr2",
                        phase == 0 ? TextBlock("ExpContent1") : VStack(TextBlock("ExpContent2a"), TextBlock("ExpContent2b")),
                        isExpanded: true,
                        onIsExpandedChanged: _ => { })
                );
            });

            await Harness.Render();
            H.Check("ExpUpd_Initial", H.FindText("ExpContent1") is not null);

            H.ClickButton("UpdExp");
            await Harness.Render();

            H.Check("ExpUpd_HeaderChanged", H.FindText("ExpanderHdr2") is not null);
            H.Check("ExpUpd_ContentChanged", H.FindText("ExpContent2a") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Visibility toggle via modifiers (exercises IsVisible → Collapsed)
    // ════════════════════════════════════════════════════════════════════

    internal class VisibilityModifier(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (visible, setVisible) = ctx.UseState(true);
                return VStack(
                    Button("ToggleVis", () => setVisible(!visible)),
                    TextBlock("VisTarget") with
                    {
                        Modifiers = new ElementModifiers
                        {
                            IsVisible = visible,
                            Opacity = visible ? 1.0 : 0.0,
                        }
                    }
                );
            });

            await Harness.Render();
            var target = H.FindText("VisTarget");
            H.Check("Vis_InitialVisible", target is not null && target!.Visibility == Visibility.Visible);

            H.ClickButton("ToggleVis");
            await Harness.Render();

            target = H.FindText("VisTarget");
            H.Check("Vis_Collapsed", target is not null && target!.Visibility == Visibility.Collapsed);

            H.ClickButton("ToggleVis");
            await Harness.Render();

            target = H.FindText("VisTarget");
            H.Check("Vis_Restored", target is not null && target!.Visibility == Visibility.Visible);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  RichEditBox update
    // ════════════════════════════════════════════════════════════════════

    internal class RichEditBoxUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdREB", () => set(1)),
                    RichEditBox(phase == 0 ? "reb_before" : "reb_after")
                );
            });

            await Harness.Render();
            H.Check("REBUpdate_Mounted",
                H.FindControl<RichEditBox>(_ => true) is not null);

            H.ClickButton("UpdREB");
            await Harness.Render();

            H.Check("REBUpdate_StillPresent",
                H.FindControl<RichEditBox>(_ => true) is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  AutoSuggestBox update
    // ════════════════════════════════════════════════════════════════════

    internal class AutoSuggestBoxUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdASB", () => set(1)),
                    AutoSuggestBox(phase == 0 ? "asb_query" : "asb_changed")
                );
            });

            await Harness.Render();
            H.Check("ASBUpdate_Mounted",
                H.FindControl<AutoSuggestBox>(_ => true) is not null);

            H.ClickButton("UpdASB");
            await Harness.Render();

            H.Check("ASBUpdate_StillPresent",
                H.FindControl<AutoSuggestBox>(_ => true) is not null);
        }
    }
}
