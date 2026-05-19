using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Tests targeting uncovered modifier and event handler paths in ApplyModifiers
/// (Reconciler.cs lines 462-870+):
///   - Event handler attachment (OnSizeChanged, OnTapped, OnPointerPressed, OnKeyDown)
///   - Background/Foreground brush modifiers
///   - Tooltip modifier
///   - Attached flyout / context flyout
///   - FontFamily/FontSize/FontWeight modifiers
///   - AutomationName/AutomationId
///   - Implicit transitions
/// </summary>
internal static class ModifierEventFixtures
{
    // ════════════════════════════════════════════════════════════════════
    //  Event handler modifiers (OnSizeChanged, OnTapped, OnPointerPressed, OnKeyDown)
    //  Exercises Reconciler.cs lines 653-750
    // ════════════════════════════════════════════════════════════════════

    internal class EventHandlerModifiers(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            int sizeChangedCount = 0;
            int tappedCount = 0;

            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);

                var mods = new ElementModifiers
                {
                    Width = phase == 0 ? 100 : 200,
                    Height = 50,
                    OnSizeChanged = (w, h) => sizeChangedCount++,
                    OnTapped = (sender, args) => tappedCount++,
                    OnPointerPressed = (sender, args) => { },
                    OnPointerReleased = (sender, args) => { },
                    OnPointerMoved = (sender, args) => { },
                    OnKeyDown = (sender, args) => { },
                };

                return VStack(
                    Button("UpdEvents", () => set(1)),
                    TextBlock("EventTarget") with { Modifiers = mods }
                );
            });

            await Harness.Render();
            H.Check("Events_Mounted", H.FindText("EventTarget") is not null);

            // Trigger size change by updating width
            H.ClickButton("UpdEvents");
            await Harness.Render();

            // SizeChanged should fire when width changes from 100 → 200
            H.Check("Events_SizeChangedFired", sizeChangedCount > 0);

            // Re-render with same handlers to test the update path (detach old, attach new)
            H.Check("Events_TargetPresent", H.FindText("EventTarget") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Background/Foreground brush modifiers
    //  Exercises ApplyModifiers lines for Background, Foreground
    // ════════════════════════════════════════════════════════════════════

    internal class BrushModifiers(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var bg = phase == 0
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red)
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Blue);
                var fg = phase == 0
                    ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White)
                    : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Yellow);

                return VStack(
                    Button("UpdBrush", () => set(1)),
                    TextBlock("BrushTarget") with
                    {
                        Modifiers = new ElementModifiers
                        {
                            Background = bg,
                            Foreground = fg,
                            FontSize = phase == 0 ? 14.0 : 20.0,
                            FontWeight = phase == 0
                                ? new global::Windows.UI.Text.FontWeight(400)
                                : new global::Windows.UI.Text.FontWeight(700),
                        }
                    }
                );
            });

            await Harness.Render();
            var target = H.FindText("BrushTarget");
            H.Check("Brush_Initial", target is not null);

            H.ClickButton("UpdBrush");
            await Harness.Render();

            target = H.FindText("BrushTarget");
            H.Check("Brush_Updated", target is not null);
            H.Check("Brush_FontSizeChanged", target!.FontSize == 20);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Tooltip modifier
    //  Exercises ApplyModifiers lines 524-525 (simple tooltip)
    // ════════════════════════════════════════════════════════════════════

    internal class TooltipModifier(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdTip", () => set(1)),
                    TextBlock("TipTarget") with
                    {
                        Modifiers = new ElementModifiers
                        {
                            ToolTip = phase == 0 ? "Tip1" : "Tip2",
                        }
                    }
                );
            });

            await Harness.Render();
            var target = H.FindText("TipTarget");
            H.Check("Tooltip_Initial", target is not null);

            var tip = ToolTipService.GetToolTip(target!);
            H.Check("Tooltip_Set", tip is not null && tip.ToString() == "Tip1");

            H.ClickButton("UpdTip");
            await Harness.Render();

            target = H.FindText("TipTarget");
            tip = ToolTipService.GetToolTip(target!);
            H.Check("Tooltip_Updated", tip is not null && tip.ToString() == "Tip2");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  AutomationName / AutomationId modifiers
    //  Exercises ApplyModifiers automation properties lines
    // ════════════════════════════════════════════════════════════════════

    internal class AutomationModifiers(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdAuto", () => set(1)),
                    TextBlock("AutoTarget") with
                    {
                        Modifiers = new ElementModifiers
                        {
                            AutomationName = phase == 0 ? "AutoName1" : "AutoName2",
                            AutomationId = "auto-test-id",
                        }
                    }
                );
            });

            await Harness.Render();
            var target = H.FindText("AutoTarget");
            H.Check("Automation_Initial", target is not null);
            H.Check("Automation_NameSet",
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(target!) == "AutoName1");

            H.ClickButton("UpdAuto");
            await Harness.Render();

            target = H.FindText("AutoTarget");
            H.Check("Automation_NameUpdated",
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(target!) == "AutoName2");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Implicit transitions modifier
    //  Exercises ApplyTransitions (Reconciler.cs lines 416-460)
    // ════════════════════════════════════════════════════════════════════

    internal class ImplicitTransitionModifier(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdTrans", () => set(1)),
                    TextBlock("TransTarget") with
                    {
                        ImplicitTransitions = new ImplicitTransitions
                        {
                            Opacity = new Microsoft.UI.Xaml.ScalarTransition { Duration = TimeSpan.FromMilliseconds(200) },
                            Translation = new Microsoft.UI.Xaml.Vector3Transition { Duration = TimeSpan.FromMilliseconds(200) },
                        },
                        Modifiers = new ElementModifiers
                        {
                            Opacity = phase == 0 ? 1.0 : 0.5,
                        }
                    }
                );
            });

            await Harness.Render();
            var target = H.FindText("TransTarget");
            H.Check("Transition_Initial", target is not null);
            H.Check("Transition_OpacityTransSet", target!.OpacityTransition is not null);
            H.Check("Transition_TranslationSet", target!.TranslationTransition is not null);

            H.ClickButton("UpdTrans");
            await Harness.Render();

            target = H.FindText("TransTarget");
            H.Check("Transition_StillPresent", target is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  BorderBrush / BorderThickness modifiers on Control and Border
    //  Exercises ApplyModifiers lines 551-572
    // ════════════════════════════════════════════════════════════════════

    internal class BorderModifiers(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var borderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    phase == 0 ? Microsoft.UI.Colors.Red : Microsoft.UI.Colors.Green);
                var borderThickness = new Thickness(phase == 0 ? 1 : 3);

                return VStack(
                    Button("UpdBorderMod", () => set(1)),
                    Border(TextBlock("BdrModTarget")) with
                    {
                        Modifiers = new ElementModifiers
                        {
                            BorderBrush = borderBrush,
                            BorderThickness = borderThickness,
                            CornerRadius = new CornerRadius(phase == 0 ? 0 : 8),
                        }
                    },
                    // Also test on a Control (Button)
                    Button("StyledBtn") with
                    {
                        Modifiers = new ElementModifiers
                        {
                            BorderBrush = borderBrush,
                            BorderThickness = borderThickness,
                        }
                    }
                );
            });

            await Harness.Render();
            H.Check("BorderMod_Initial", H.FindText("BdrModTarget") is not null);

            H.ClickButton("UpdBorderMod");
            await Harness.Render();

            H.Check("BorderMod_Updated", H.FindText("BdrModTarget") is not null);
            H.Check("BorderMod_BtnPresent", H.FindButton("StyledBtn") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  OnMountAction modifier
    //  Exercises ApplyModifiers OnMountAction path
    // ════════════════════════════════════════════════════════════════════

    internal class OnMountActionModifier(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int mountActionCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                return TextBlock("MountActionTarget") with
                {
                    Modifiers = new ElementModifiers
                    {
                        OnMountAction = fe => { mountActionCount++; },
                    }
                };
            });

            await Harness.Render();
            H.Check("MountAction_Fired", mountActionCount >= 1);
        }
    }

    internal class ModifierClearResets(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var brush = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);

                var controlMods = phase == 0
                    ? new ElementModifiers
                    {
                        RequestedTheme = ElementTheme.Dark,
                        Margin = new Thickness(3, 4, 5, 6),
                        Padding = new Thickness(7, 8, 9, 10),
                        Width = 120,
                        Height = 44,
                        MinWidth = 80,
                        MinHeight = 30,
                        MaxWidth = 240,
                        MaxHeight = 90,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Opacity = 0.5,
                        IsVisible = false,
                        ToolTip = "clear-me",
                        IsEnabled = false,
                        CornerRadius = new CornerRadius(6),
                        BorderBrush = brush,
                        BorderThickness = new Thickness(2),
                        Background = brush,
                        Foreground = brush,
                        AutomationName = "clear-name",
                        AutomationId = "clear-id",
                        IsTabStop = false,
                        TabIndex = 7,
                        AccessKey = "C",
                        XYFocusKeyboardNavigation = Microsoft.UI.Xaml.Input.XYFocusKeyboardNavigationMode.Enabled,
                        ElementSoundMode = ElementSoundMode.Off,
                        HeadingLevel = Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 22,
                        FontWeight = new global::Windows.UI.Text.FontWeight(700),
                    }
                    : new ElementModifiers();

                var borderMods = phase == 0
                    ? new ElementModifiers
                    {
                        Margin = new Thickness(4),
                        Padding = new Thickness(5),
                        CornerRadius = new CornerRadius(8),
                        BorderBrush = brush,
                        BorderThickness = new Thickness(3),
                        Background = brush,
                        AutomationName = "border-clear-name",
                    }
                    : new ElementModifiers();

                return VStack(
                    Button("ClearModifierPhase", () => setPhase(1)),
                    Button("ClearTarget") with { Modifiers = controlMods },
                    Border(TextBlock("ClearBorderChild")) with { Modifiers = borderMods });
            });

            await Harness.Render();
            var initial = H.FindButton("ClearTarget");
            H.Check("ModifierClear_InitialCollapsed",
                initial is not null && initial.Visibility == Visibility.Collapsed);

            H.ClickButton("ClearModifierPhase");
            await Harness.Render();

            var button = H.FindButton("ClearTarget");
            H.Check("ModifierClear_ButtonPresent", button is not null);
            if (button is not null)
            {
                H.Check("ModifierClear_ThemeCleared", button.RequestedTheme == ElementTheme.Default);
                H.Check("ModifierClear_SizeCleared",
                    double.IsNaN(button.Width) && double.IsNaN(button.Height)
                    && button.MinWidth == 0 && button.MinHeight == 0
                    && double.IsPositiveInfinity(button.MaxWidth)
                    && double.IsPositiveInfinity(button.MaxHeight));
                H.Check("ModifierClear_AlignmentCleared",
                    button.HorizontalAlignment == HorizontalAlignment.Stretch
                    && button.VerticalAlignment == VerticalAlignment.Stretch);
                H.Check("ModifierClear_VisibleEnabled",
                    button.Visibility == Visibility.Visible && button.IsEnabled);
                H.Check("ModifierClear_TooltipCleared",
                    ToolTipService.GetToolTip(button) is null);
                H.Check("ModifierClear_AutomationCleared",
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) != "clear-name"
                    && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) != "clear-id");
                H.Check("ModifierClear_AccessKeyCleared", button.AccessKey == "");
                H.Check("ModifierClear_BorderCleared", button.BorderThickness == new Thickness(0));
            }

            var border = H.FindControl<Border>(b => b.Child is TextBlock tb && tb.Text == "ClearBorderChild");
            H.Check("ModifierClear_BorderPresent", border is not null);
            if (border is not null)
            {
                H.Check("ModifierClear_BorderThicknessCleared", border.BorderThickness == new Thickness(0));
                H.Check("ModifierClear_BorderCornerCleared", border.CornerRadius == new CornerRadius(0));
                H.Check("ModifierClear_BorderAutomationCleared",
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(border) != "border-clear-name");
            }
        }
    }
}
