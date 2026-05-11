using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Mount and update tests for less common controls whose Mount/Update handlers
/// are not exercised by the main control catalog or update fixtures.
/// Each test mounts the control, verifies it exists, updates props, verifies again.
/// </summary>
internal static class RareControlFixtures
{
    // ════════════════════════════════════════════════════════════════════
    //  ColorPicker mount + update (~12 lines Mount, ~10 lines Update)
    // ════════════════════════════════════════════════════════════════════

    internal class ColorPickerMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var color = phase == 0
                    ? global::Windows.UI.Color.FromArgb(255, 255, 0, 0)
                    : global::Windows.UI.Color.FromArgb(255, 0, 0, 255);
                return VStack(
                    Button("UpdCP", () => set(1)),
                    ColorPicker(color)
                );
            });

            await Harness.Render();
            H.Check("ColorPicker_Mounted",
                H.FindControl<Microsoft.UI.Xaml.Controls.ColorPicker>(_ => true) is not null);

            H.ClickButton("UpdCP");
            await Harness.Render();

            H.Check("ColorPicker_Updated",
                H.FindControl<Microsoft.UI.Xaml.Controls.ColorPicker>(_ => true) is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  TeachingTip mount (~14 lines Mount)
    // ════════════════════════════════════════════════════════════════════

    internal class TeachingTipMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdTT", () => set(1)),
                    TeachingTip(phase == 0 ? "TipTitle1" : "TipTitle2", "SubTitle")
                );
            });

            await Harness.Render();
            H.Check("TeachingTip_Mounted",
                H.FindControl<Microsoft.UI.Xaml.Controls.TeachingTip>(_ => true) is not null);

            H.ClickButton("UpdTT");
            await Harness.Render();

            H.Check("TeachingTip_Updated",
                H.FindControl<Microsoft.UI.Xaml.Controls.TeachingTip>(_ => true) is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ContentDialog mount + update (~15 lines each)
    // ════════════════════════════════════════════════════════════════════

    // ContentDialog mount + open coverage lives in CoreCoverageFixtures
    // (ContentDialogMount + ContentDialogOpensAtMount + ContentDialogOpensOnStateFlip).
    // The dialog appears as a popup; ShowAsync() is fire-and-forget here so the
    // fixtures probe VisualTreeHelper.GetOpenPopupsForXamlRoot() after a render.

    // ════════════════════════════════════════════════════════════════════
    //  GridView mount + update
    // ════════════════════════════════════════════════════════════════════

    internal class GridViewMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdGV", () => set(1)),
                    GridView(phase == 0
                        ? [TextBlock("GV_A"), TextBlock("GV_B")]
                        : [TextBlock("GV_A"), TextBlock("GV_B"), TextBlock("GV_C")])
                );
            });

            await Harness.Render();
            H.Check("GridView_Mounted", H.FindText("GV_A") is not null);

            H.ClickButton("UpdGV");
            await Harness.Render();

            H.Check("GridView_Updated", H.FindText("GV_C") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  PersonPicture update
    // ════════════════════════════════════════════════════════════════════

    internal class PersonPictureUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdPP", () => set(1)),
                    PersonPicture() with
                    {
                        Modifiers = new ElementModifiers { Width = phase == 0 ? 32 : 64 }
                    }
                );
            });

            await Harness.Render();
            var pp = H.FindControl<Microsoft.UI.Xaml.Controls.PersonPicture>(_ => true);
            H.Check("PersonPic_Mounted", pp is not null);

            H.ClickButton("UpdPP");
            await Harness.Render();

            pp = H.FindControl<Microsoft.UI.Xaml.Controls.PersonPicture>(_ => true);
            H.Check("PersonPic_Updated", pp is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  RelativePanel mount (~20 lines)
    // ════════════════════════════════════════════════════════════════════

    internal class RelativePanelMount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return new RelativePanelElement([
                    TextBlock("RP_A") with
                    {
                        Attached = new Dictionary<Type, object>
                        {
                            [typeof(RelativePanelAttached)] = new RelativePanelAttached("ItemA")
                            {
                                AlignLeftWithPanel = true,
                                AlignTopWithPanel = true,
                            }
                        }
                    },
                    TextBlock("RP_B") with
                    {
                        Attached = new Dictionary<Type, object>
                        {
                            [typeof(RelativePanelAttached)] = new RelativePanelAttached("ItemB")
                            {
                                Below = "ItemA",
                                AlignLeftWithPanel = true,
                            }
                        }
                    }
                ]);
            });

            await Harness.Render();
            H.Check("RelativePanel_A", H.FindText("RP_A") is not null);
            H.Check("RelativePanel_B", H.FindText("RP_B") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  CalendarView mount + update
    // ════════════════════════════════════════════════════════════════════

    internal class CalendarViewMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdCalV", () => set(1)),
                    new CalendarViewElement() with
                    {
                        Modifiers = new ElementModifiers { Height = 300, Width = 300 }
                    }
                );
            });

            await Harness.Render();
            H.Check("CalendarView_Mounted",
                H.FindControl<Microsoft.UI.Xaml.Controls.CalendarView>(_ => true) is not null);

            H.ClickButton("UpdCalV");
            await Harness.Render();

            H.Check("CalendarView_Updated",
                H.FindControl<Microsoft.UI.Xaml.Controls.CalendarView>(_ => true) is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  RichTextBlock update with paragraph changes
    // ════════════════════════════════════════════════════════════════════

    internal class RichTextBlockUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdRTB", () => set(1)),
                    RichText(phase == 0 ? "Rich text before" : "Rich text after updated")
                );
            });

            await Harness.Render();
            // RichTextBlock renders text inside Run elements, not directly as TextBlock.Text
            var rtb = H.FindControl<Microsoft.UI.Xaml.Controls.RichTextBlock>(_ => true);
            H.Check("RTBUpdate_Initial", rtb is not null);

            H.ClickButton("UpdRTB");
            await Harness.Render();

            rtb = H.FindControl<Microsoft.UI.Xaml.Controls.RichTextBlock>(_ => true);
            H.Check("RTBUpdate_Changed", rtb is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  PasswordBox update
    // ════════════════════════════════════════════════════════════════════

    internal class PasswordBoxUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdPB", () => set(1)),
                    PasswordBox(phase == 0 ? "pass1" : "pass2",
                        placeholderText: phase == 0 ? "Enter password" : "New placeholder")
                );
            });

            await Harness.Render();
            H.Check("PBUpdate_Mounted",
                H.FindControl<Microsoft.UI.Xaml.Controls.PasswordBox>(_ => true) is not null);

            H.ClickButton("UpdPB");
            await Harness.Render();

            var pb = H.FindControl<Microsoft.UI.Xaml.Controls.PasswordBox>(_ => true);
            H.Check("PBUpdate_Updated", pb is not null);
            H.Check("PBUpdate_Placeholder", pb!.PlaceholderText == "New placeholder");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  AutoSuggestBox update
    // ════════════════════════════════════════════════════════════════════

    internal class ComboBoxRadioButtonsUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdCRB", () => set(1)),
                    // ComboBox and RadioButtons remount on update (no UpdateXxx, dispatches to Mount)
                    ComboBox(phase == 0 ? ["A", "B"] : ["X", "Y", "Z"], selectedIndex: 0),
                    RadioButtons(phase == 0 ? ["R1", "R2"] : ["R3", "R4", "R5"], selectedIndex: 0)
                );
            });

            await Harness.Render();
            H.Check("CRB_ComboMounted",
                H.FindControl<Microsoft.UI.Xaml.Controls.ComboBox>(_ => true) is not null);
            H.Check("CRB_RadiosMounted",
                H.FindControl<Microsoft.UI.Xaml.Controls.RadioButtons>(_ => true) is not null);

            H.ClickButton("UpdCRB");
            await Harness.Render();

            H.Check("CRB_ComboUpdated",
                H.FindControl<Microsoft.UI.Xaml.Controls.ComboBox>(_ => true) is not null);
            H.Check("CRB_RadiosUpdated",
                H.FindControl<Microsoft.UI.Xaml.Controls.RadioButtons>(_ => true) is not null);
        }
    }

    // MapControl is intentionally not exercised here — mounting it into a real
    // WinUI window blocks on networking/graphics initialization in the self-test
    // harness, which causes the whole run to hang. MapControl requires a
    // MapServiceToken and a realistic host environment (packaged identity,
    // network access) to initialize cleanly; it is better exercised in a
    // dedicated interactive sample, not a unit-style selftest.
}
