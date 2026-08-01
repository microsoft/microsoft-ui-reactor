using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

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
    //  ToolTipService.Placement / PlacementTarget modifiers
    //  Exercises ApplyModifiers' ToolTipPlacement arm + the
    //  ModifierRef_ToolTipPlacementTarget reference edge.
    // ════════════════════════════════════════════════════════════════════

    internal class TooltipPlacementModifier(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var anchor = new Microsoft.UI.Reactor.Input.ElementRef();

            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("UpdPlacement", () => set(phase + 1)),
                    // At phase 3 the ref MOVES from one live anchor to another. Both
                    // stay mounted, so this isolates the ref-cell change without
                    // depending on unmount/remount ordering.
                    phase >= 3
                        ? TextBlock("PlacementAnchor")
                        : TextBlock("PlacementAnchor").Ref(anchor),
                    phase >= 3
                        ? TextBlock("PlacementAnchorB").Ref(anchor)
                        : TextBlock("PlacementAnchorB"),
                    phase switch
                    {
                        0 => TextBlock("PlacementTarget")
                                .ToolTip("Tip", PlacementMode.Right)
                                .ToolTipPlacementTarget(anchor),
                        1 => TextBlock("PlacementTarget")
                                .ToolTip("Tip", PlacementMode.Left),
                        _ => TextBlock("PlacementTarget")
                                .ToolTip("Tip"),
                    }
                );
            });

            await Harness.Render();
            var target = H.FindText("PlacementTarget");
            H.Check("TipPlacement_Initial",
                target is not null && ToolTipService.GetPlacement(target) == PlacementMode.Right);
            H.Check("TipPlacementTarget_Wired",
                target is not null
                && ReferenceEquals(ToolTipService.GetPlacementTarget(target), H.FindText("PlacementAnchor")));

            // Phase 1 — placement changes, placement target goes away.
            H.ClickButton("UpdPlacement");
            await Harness.Render();
            target = H.FindText("PlacementTarget");
            H.Check("TipPlacement_Updated",
                ToolTipService.GetPlacement(target!) == PlacementMode.Left);
            H.Check("TipPlacementTarget_Cleared",
                target!.ReadLocalValue(ToolTipService.PlacementTargetProperty) == DependencyProperty.UnsetValue);

            // Phase 2 — placement itself goes unset: the local value must be
            // cleared so WinUI's own default takes over again, not left pinned
            // at the last explicit placement.
            H.ClickButton("UpdPlacement");
            await Harness.Render();
            target = H.FindText("PlacementTarget");
            H.Check("TipPlacement_Cleared",
                target!.ReadLocalValue(ToolTipService.PlacementProperty) == DependencyProperty.UnsetValue);
            H.Check("TipPlacement_TooltipSurvivesPlacementClear",
                ToolTipService.GetToolTip(target!)?.ToString() == "Tip");

            // Phase 3 — the reference edge must have been UNWIRED when the target
            // was dropped at phase 1, not merely cleared once. Re-key the anchor so
            // it remounts as a different control and the ref cell raises
            // CurrentChanged. A stale subscription would re-apply the new anchor as
            // a placement target on an element that no longer asks for one.
            var anchorBefore = anchor.Current;
            H.ClickButton("UpdPlacement");
            await Harness.Render();
            target = H.FindText("PlacementTarget");
            var anchorAfter = anchor.Current;
            // Load-bearing precondition: if the re-key did not actually swap the
            // control, CurrentChanged never fired and the assertion below is vacuous.
            H.Check("TipPlacementTarget_AnchorActuallyChanged",
                anchorBefore is not null && anchorAfter is not null
                && !ReferenceEquals(anchorBefore, anchorAfter));
            H.Check("TipPlacementTarget_StaysClearedAfterAnchorChange",
                target!.ReadLocalValue(ToolTipService.PlacementTargetProperty) == DependencyProperty.UnsetValue);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ToolTipService attached props must not survive a pool round-trip.
    //  ApplyModifiers only clears them on a set → unset *update*; a full
    //  unmount returns the control to ElementPool with the attached props
    //  still on it, so ElementPool.CleanElement has to clear all three or
    //  the next renter inherits a phantom tooltip.
    //
    //  The carrier is the LAST child of the root VStack, so dropping and
    //  re-adding it is a pure tail add/remove: exactly one TextBlock is
    //  returned to the pool and exactly one is rented back, which makes the
    //  instance-identity check deterministic — and that check is what keeps
    //  the "cleared" assertions non-vacuous (a freshly-constructed control
    //  would trivially have no tooltip).
    // ════════════════════════════════════════════════════════════════════

    internal class TooltipPoolCleanOnRent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var anchor = new Microsoft.UI.Reactor.Input.ElementRef();

            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    // At phase 3 the ref MOVES from one live anchor to another; both
                    // stay mounted so the ref-cell change is isolated from
                    // unmount/remount ordering.
                    phase >= 3
                        ? TextBlock("TipPoolAnchor")
                        : TextBlock("TipPoolAnchor").Ref(anchor),
                    phase >= 3
                        ? TextBlock("TipPoolAnchorB").Ref(anchor)
                        : TextBlock("TipPoolAnchorB"),
                    Button("DropTipPool", () => set(1)),
                    Button("RemountTipPool", () => set(2)),
                    Button("SwapTipPoolAnchor", () => set(3)),
                    phase switch
                    {
                        0 => TextBlock("tip-pool-carrier")
                                .ToolTip("leaky tip", PlacementMode.Right)
                                .ToolTipPlacementTarget(anchor),
                        1 => Empty(),
                        // Remounted with no tooltip modifiers at all.
                        _ => TextBlock("tip-pool-carrier-2"),
                    }
                );
            });

            await Harness.Render();
            var first = H.FindText("tip-pool-carrier");
            H.Check("TipPool_Phase0_ToolTipSet",
                first is not null && ToolTipService.GetToolTip(first)?.ToString() == "leaky tip");
            H.Check("TipPool_Phase0_PlacementSet",
                first is not null && ToolTipService.GetPlacement(first) == PlacementMode.Right);
            H.Check("TipPool_Phase0_PlacementTargetSet",
                first is not null && ToolTipService.GetPlacementTarget(first) is not null);

            H.ClickButton("DropTipPool");
            await Harness.Render();
            H.Check("TipPool_Phase1_Returned", H.FindText("tip-pool-carrier") is null);

            H.ClickButton("RemountTipPool");
            await Harness.Render();
            var second = H.FindText("tip-pool-carrier-2");
            // Load-bearing: without instance reuse the "cleared" checks below would
            // pass trivially on a freshly-constructed control.
            H.Check("TipPool_Phase2_ReusedInstance",
                first is not null && ReferenceEquals(first, second));
            H.Check("TipPool_Phase2_ToolTipCleared",
                second is not null
                && second.ReadLocalValue(ToolTipService.ToolTipProperty) == DependencyProperty.UnsetValue);
            H.Check("TipPool_Phase2_PlacementCleared",
                second is not null
                && second.ReadLocalValue(ToolTipService.PlacementProperty) == DependencyProperty.UnsetValue);
            H.Check("TipPool_Phase2_PlacementTargetCleared",
                second is not null
                && second.ReadLocalValue(ToolTipService.PlacementTargetProperty) == DependencyProperty.UnsetValue);

            // Phase 3 — the reference edge must have been torn down when the carrier
            // UNMOUNTED (a different path from the in-place unwire covered by
            // TooltipPlacementModifier). Swap the anchor for a new control: a stale
            // CurrentChanged subscription surviving the unmount would re-apply a
            // placement target onto the recycled carrier.
            var anchorBefore = anchor.Current;
            H.ClickButton("SwapTipPoolAnchor");
            await Harness.Render();
            var carrier = H.FindText("tip-pool-carrier-2");
            var anchorAfter = anchor.Current;
            H.Check("TipPool_Phase3_AnchorActuallyChanged",
                anchorBefore is not null && anchorAfter is not null
                && !ReferenceEquals(anchorBefore, anchorAfter));
            H.Check("TipPool_Phase3_PlacementTargetStaysCleared",
                carrier is not null
                && carrier.ReadLocalValue(ToolTipService.PlacementTargetProperty) == DependencyProperty.UnsetValue);
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

                var controlMods = phase switch
                {
                    0 => new ElementModifiers
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
                        HorizontalContentAlignment = HorizontalAlignment.Right,
                        VerticalContentAlignment = VerticalAlignment.Bottom,
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
                        IsHitTestVisible = false,
                        TabIndex = 7,
                        AccessKey = "C",
                        XYFocusKeyboardNavigation = Microsoft.UI.Xaml.Input.XYFocusKeyboardNavigationMode.Enabled,
                        ElementSoundMode = ElementSoundMode.Off,
                        HeadingLevel = Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 22,
                        FontWeight = new global::Windows.UI.Text.FontWeight(700),
                    },
                    1 => new ElementModifiers
                    {
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        VerticalContentAlignment = VerticalAlignment.Top,
                    },
                    _ => new ElementModifiers(),
                };

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
                    Button("ClearModifierPhase", () => setPhase(phase + 1)),
                    Button("ClearTarget") with { Modifiers = controlMods },
                    Border(TextBlock("ClearBorderChild")) with { Modifiers = borderMods });
            });

            await Harness.Render();
            var initial = H.FindButton("ClearTarget");
            H.Check("ModifierClear_InitialCollapsed",
                initial is not null && initial.Visibility == Visibility.Collapsed);
            if (initial is not null)
            {
                H.Check("ModifierClear_ContentAlignmentInitial",
                    initial.HorizontalContentAlignment == HorizontalAlignment.Right
                    && initial.VerticalContentAlignment == VerticalAlignment.Bottom);
            }

            H.ClickButton("ClearModifierPhase");
            await Harness.Render();

            var updated = H.FindButton("ClearTarget");
            H.Check("ModifierClear_ContentAlignmentUpdated", updated is not null);
            if (updated is not null)
            {
                H.Check("ModifierClear_ContentAlignmentUpdatedValues",
                    updated.HorizontalContentAlignment == HorizontalAlignment.Left
                    && updated.VerticalContentAlignment == VerticalAlignment.Top);
            }

            H.ClickButton("ClearModifierPhase");
            await Harness.Render();

            var button = H.FindButton("ClearTarget");
            H.Check("ModifierClear_ButtonPresent", button is not null);
            if (button is not null)
            {
                H.Check("ModifierClear_ThemeCleared",
                    button.ReadLocalValue(FrameworkElement.RequestedThemeProperty) == DependencyProperty.UnsetValue
                    && button.RequestedTheme == ElementTheme.Default);
                // Asserted as "the local value was released" rather than "the property
                // reads its DP default": the two are only the same on a control with no
                // style, and Button's default style supplies alignment, border thickness,
                // corner radius and padding (issue #952).
                H.Check("ModifierClear_SizeCleared",
                    button.ReadLocalValue(FrameworkElement.WidthProperty) == DependencyProperty.UnsetValue
                    && button.ReadLocalValue(FrameworkElement.HeightProperty) == DependencyProperty.UnsetValue
                    && button.ReadLocalValue(FrameworkElement.MinWidthProperty) == DependencyProperty.UnsetValue
                    && button.ReadLocalValue(FrameworkElement.MinHeightProperty) == DependencyProperty.UnsetValue
                    && button.ReadLocalValue(FrameworkElement.MaxWidthProperty) == DependencyProperty.UnsetValue
                    && button.ReadLocalValue(FrameworkElement.MaxHeightProperty) == DependencyProperty.UnsetValue);
                H.Check("ModifierClear_AlignmentCleared",
                    button.ReadLocalValue(FrameworkElement.HorizontalAlignmentProperty) == DependencyProperty.UnsetValue
                    && button.ReadLocalValue(FrameworkElement.VerticalAlignmentProperty) == DependencyProperty.UnsetValue);
                H.Check("ModifierClear_MarginCleared",
                    button.ReadLocalValue(FrameworkElement.MarginProperty) == DependencyProperty.UnsetValue);
                H.Check("ModifierClear_PaddingCleared",
                    button.ReadLocalValue(Control.PaddingProperty) == DependencyProperty.UnsetValue);
                H.Check("ModifierClear_OpacityCleared",
                    button.ReadLocalValue(UIElement.OpacityProperty) == DependencyProperty.UnsetValue);
                H.Check("ModifierClear_ContentAlignmentCleared",
                    button.ReadLocalValue(Control.HorizontalContentAlignmentProperty) == DependencyProperty.UnsetValue
                    && button.ReadLocalValue(Control.VerticalContentAlignmentProperty) == DependencyProperty.UnsetValue
                    && button.HorizontalContentAlignment == HorizontalAlignment.Center
                    && button.VerticalContentAlignment == VerticalAlignment.Center);
                H.Check("ModifierClear_VisibleEnabled",
                    button.ReadLocalValue(UIElement.VisibilityProperty) == DependencyProperty.UnsetValue
                    && button.ReadLocalValue(Control.IsEnabledProperty) == DependencyProperty.UnsetValue
                    && button.Visibility == Visibility.Visible && button.IsEnabled);
                H.Check("ModifierClear_TooltipCleared",
                    ToolTipService.GetToolTip(button) is null);
                H.Check("ModifierClear_AutomationCleared",
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) != "clear-name"
                    && Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(button) != "clear-id");
                H.Check("ModifierClear_AccessKeyCleared",
                    button.ReadLocalValue(UIElement.AccessKeyProperty) == DependencyProperty.UnsetValue
                    && button.AccessKey == "");
                H.Check("ModifierClear_HitTestVisibleRestored", button.IsHitTestVisible);
                H.Check("ModifierClear_BorderCleared",
                    button.ReadLocalValue(Control.BorderThicknessProperty) == DependencyProperty.UnsetValue
                    && button.ReadLocalValue(Control.CornerRadiusProperty) == DependencyProperty.UnsetValue);
            }

            var border = H.FindControl<Border>(b => b.Child is TextBlock tb && tb.Text == "ClearBorderChild");
            H.Check("ModifierClear_BorderPresent", border is not null);
            if (border is not null)
            {
                // Border is not a Control and has no default style, so the effective value
                // would be 0 either way — the local-value read is what discriminates.
                H.Check("ModifierClear_BorderThicknessCleared",
                    border.ReadLocalValue(Microsoft.UI.Xaml.Controls.Border.BorderThicknessProperty) == DependencyProperty.UnsetValue
                    && border.BorderThickness == new Thickness(0));
                H.Check("ModifierClear_BorderCornerCleared",
                    border.ReadLocalValue(Microsoft.UI.Xaml.Controls.Border.CornerRadiusProperty) == DependencyProperty.UnsetValue
                    && border.CornerRadius == new CornerRadius(0));
                H.Check("ModifierClear_BorderPaddingCleared",
                    border.ReadLocalValue(Microsoft.UI.Xaml.Controls.Border.PaddingProperty) == DependencyProperty.UnsetValue);
                H.Check("ModifierClear_BorderAutomationCleared",
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(border) != "border-clear-name");
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Issue #952 — unsetting a common modifier must RELEASE the local
    //  value (ClearValue) so the control falls back to its Style, not pin
    //  the DP's default as a local value.
    //
    //  The explicit Style is the load-bearing part of this fixture. With no
    //  Style attached, "wrote the DP default" and "released the local
    //  value" produce identical effective values on every property here —
    //  which is exactly why the bug survived. Each setter below therefore
    //  uses a value distinct from BOTH the DP default and the modifier
    //  value, so one H.Check can tell all three states apart.
    // ════════════════════════════════════════════════════════════════════

    internal class ModifierStyleUnsetRestore(Harness h) : SelfTestFixtureBase(h)
    {
        private static readonly Thickness StyleMargin = new(11);
        private static readonly Thickness StylePadding = new(13);
        private static readonly CornerRadius StyleCornerRadius = new(9);
        private static readonly Thickness StyleBorderThickness = new(4);
        private const double StyleWidth = 150;
        private const double StyleHeight = 55;
        private const double StyleMinWidth = 90;
        private const double StyleMinHeight = 35;
        private const double StyleMaxWidth = 260;
        private const double StyleMaxHeight = 95;
        private const double StyleOpacity = 0.75;
        private const string StyleAccessKey = "S";

        /// <summary>
        /// An explicit Style whose every setter differs from the DP default AND from the
        /// modifier value the fixture applies, so "restored the style" is distinguishable
        /// from "wrote the default". Based on the shipped Button style when it can be
        /// resolved, so the control still templates normally; the assertions do not
        /// depend on that lookup succeeding.
        /// </summary>
        private static Microsoft.UI.Xaml.Style BuildStyle()
        {
            Microsoft.UI.Xaml.Style? baseStyle = null;
            if (Application.Current?.Resources is { } resources
                && resources.TryGetValue("DefaultButtonStyle", out var resource))
            {
                baseStyle = resource as Microsoft.UI.Xaml.Style;
            }

            var style = new Microsoft.UI.Xaml.Style(typeof(Button)) { BasedOn = baseStyle };
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, StyleMargin));
            style.Setters.Add(new Setter(Control.PaddingProperty, StylePadding));
            style.Setters.Add(new Setter(FrameworkElement.WidthProperty, StyleWidth));
            style.Setters.Add(new Setter(FrameworkElement.HeightProperty, StyleHeight));
            style.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, StyleMinWidth));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, StyleMinHeight));
            style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, StyleMaxWidth));
            style.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, StyleMaxHeight));
            // Center/Top rather than the shipped Button style's Left/Center, so the
            // assertion still discriminates if BasedOn resolved.
            style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top));
            style.Setters.Add(new Setter(UIElement.OpacityProperty, StyleOpacity));
            style.Setters.Add(new Setter(Control.IsEnabledProperty, false));
            style.Setters.Add(new Setter(Control.CornerRadiusProperty, StyleCornerRadius));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, StyleBorderThickness));
            style.Setters.Add(new Setter(UIElement.AccessKeyProperty, StyleAccessKey));
            return style;
        }

        /// <summary>
        /// Tolerance comparison for the <c>double</c>-valued dependency properties under
        /// test. Every value here is a literal round-tripped through a DP with no
        /// arithmetic, so <c>==</c> would be exact — but the buggy code produces
        /// <c>NaN</c> (Width/Height), <c>0</c> (Opacity) or <c>PositiveInfinity</c>
        /// (MaxWidth/MaxHeight), none of which land inside any small epsilon. The
        /// assertions are exactly as discriminating either way.
        /// </summary>
        private static bool NearlyEqual(double actual, double expected) =>
            global::System.Math.Abs(actual - expected) < 0.0001;

        public override async Task RunAsync()
        {
            var style = BuildStyle();
            var host = H.CreateHost();

            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);

                // OnMountAction is in both bags on purpose — it only ever runs at mount
                // (gated on oldM is null), and keeping it identical means the two bags
                // differ solely by the properties under test.
                var mods = phase == 0
                    ? new ElementModifiers
                    {
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
                        IsEnabled = true,
                        CornerRadius = new CornerRadius(6),
                        BorderThickness = new Thickness(2),
                        AccessKey = "C",
                        OnMountAction = fe => fe.Style = style,
                    }
                    : new ElementModifiers { OnMountAction = fe => fe.Style = style };

                return VStack(
                    Button("StyleUnsetPhase", () => setPhase(1)),
                    Button("StyleUnsetTarget") with { Modifiers = mods });
            });

            await Harness.Render();
            var button = H.FindButton("StyleUnsetTarget");
            H.Check("StyleUnset_Phase0_Present", button is not null);
            if (button is null) return;

            // Without the Style the phase-1 checks below degenerate into "the DP has its
            // default value", which the buggy code also satisfies.
            H.Check("StyleUnset_Phase0_StyleAttached", button.Style is not null);

            // The modifier beats the Style setter, and it beats it *because* it is a
            // local value. Asserting the local value exists here is what makes its
            // absence in phase 1 evidence of ClearValue rather than of a no-op.
            H.Check("StyleUnset_Phase0_Margin",
                button.Margin == new Thickness(3, 4, 5, 6)
                && button.ReadLocalValue(FrameworkElement.MarginProperty) != DependencyProperty.UnsetValue);
            H.Check("StyleUnset_Phase0_Padding",
                button.Padding == new Thickness(7, 8, 9, 10)
                && button.ReadLocalValue(Control.PaddingProperty) != DependencyProperty.UnsetValue);
            H.Check("StyleUnset_Phase0_Size",
                NearlyEqual(button.Width, 120) && NearlyEqual(button.Height, 44)
                && NearlyEqual(button.MinWidth, 80) && NearlyEqual(button.MinHeight, 30)
                && NearlyEqual(button.MaxWidth, 240) && NearlyEqual(button.MaxHeight, 90));
            H.Check("StyleUnset_Phase0_Alignment",
                button.HorizontalAlignment == HorizontalAlignment.Right
                && button.VerticalAlignment == VerticalAlignment.Bottom);
            H.Check("StyleUnset_Phase0_Opacity", NearlyEqual(button.Opacity, 0.5));
            H.Check("StyleUnset_Phase0_Collapsed", button.Visibility == Visibility.Collapsed);
            H.Check("StyleUnset_Phase0_Enabled", button.IsEnabled);
            H.Check("StyleUnset_Phase0_Border",
                button.CornerRadius == new CornerRadius(6)
                && button.BorderThickness == new Thickness(2));
            H.Check("StyleUnset_Phase0_AccessKey", button.AccessKey == "C");

            H.ClickButton("StyleUnsetPhase");
            await Harness.Render();

            button = H.FindButton("StyleUnsetTarget");
            H.Check("StyleUnset_Phase1_Present", button is not null);
            if (button is null) return;
            H.Check("StyleUnset_Phase1_StyleStillAttached", button.Style is not null);

            // Each check is two claims: the local value is gone (ClearValue ran), and the
            // effective value came back from the Style (the reset did not shadow it).
            H.Check("StyleUnset_Phase1_MarginRestored",
                button.ReadLocalValue(FrameworkElement.MarginProperty) == DependencyProperty.UnsetValue
                && button.Margin == StyleMargin);
            H.Check("StyleUnset_Phase1_PaddingRestored",
                button.ReadLocalValue(Control.PaddingProperty) == DependencyProperty.UnsetValue
                && button.Padding == StylePadding);
            H.Check("StyleUnset_Phase1_WidthRestored",
                button.ReadLocalValue(FrameworkElement.WidthProperty) == DependencyProperty.UnsetValue
                && NearlyEqual(button.Width, StyleWidth));
            H.Check("StyleUnset_Phase1_HeightRestored",
                button.ReadLocalValue(FrameworkElement.HeightProperty) == DependencyProperty.UnsetValue
                && NearlyEqual(button.Height, StyleHeight));
            H.Check("StyleUnset_Phase1_MinWidthRestored",
                button.ReadLocalValue(FrameworkElement.MinWidthProperty) == DependencyProperty.UnsetValue
                && NearlyEqual(button.MinWidth, StyleMinWidth));
            H.Check("StyleUnset_Phase1_MinHeightRestored",
                button.ReadLocalValue(FrameworkElement.MinHeightProperty) == DependencyProperty.UnsetValue
                && NearlyEqual(button.MinHeight, StyleMinHeight));
            H.Check("StyleUnset_Phase1_MaxWidthRestored",
                button.ReadLocalValue(FrameworkElement.MaxWidthProperty) == DependencyProperty.UnsetValue
                && NearlyEqual(button.MaxWidth, StyleMaxWidth));
            H.Check("StyleUnset_Phase1_MaxHeightRestored",
                button.ReadLocalValue(FrameworkElement.MaxHeightProperty) == DependencyProperty.UnsetValue
                && NearlyEqual(button.MaxHeight, StyleMaxHeight));
            H.Check("StyleUnset_Phase1_HorizontalAlignmentRestored",
                button.ReadLocalValue(FrameworkElement.HorizontalAlignmentProperty) == DependencyProperty.UnsetValue
                && button.HorizontalAlignment == HorizontalAlignment.Center);
            H.Check("StyleUnset_Phase1_VerticalAlignmentRestored",
                button.ReadLocalValue(FrameworkElement.VerticalAlignmentProperty) == DependencyProperty.UnsetValue
                && button.VerticalAlignment == VerticalAlignment.Top);
            H.Check("StyleUnset_Phase1_OpacityRestored",
                button.ReadLocalValue(UIElement.OpacityProperty) == DependencyProperty.UnsetValue
                && NearlyEqual(button.Opacity, StyleOpacity));
            H.Check("StyleUnset_Phase1_IsEnabledRestored",
                button.ReadLocalValue(Control.IsEnabledProperty) == DependencyProperty.UnsetValue
                && !button.IsEnabled);
            H.Check("StyleUnset_Phase1_CornerRadiusRestored",
                button.ReadLocalValue(Control.CornerRadiusProperty) == DependencyProperty.UnsetValue
                && button.CornerRadius == StyleCornerRadius);
            H.Check("StyleUnset_Phase1_BorderThicknessRestored",
                button.ReadLocalValue(Control.BorderThicknessProperty) == DependencyProperty.UnsetValue
                && button.BorderThickness == StyleBorderThickness);
            H.Check("StyleUnset_Phase1_AccessKeyRestored",
                button.ReadLocalValue(UIElement.AccessKeyProperty) == DependencyProperty.UnsetValue
                && button.AccessKey == StyleAccessKey);
            // Visibility's only sane Style value IS the DP default, so the effective
            // value can't discriminate here — the local-value read is the whole check.
            H.Check("StyleUnset_Phase1_VisibilityLocalValueReleased",
                button.ReadLocalValue(UIElement.VisibilityProperty) == DependencyProperty.UnsetValue
                && button.Visibility == Visibility.Visible);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Issue #952, pool half — a control returned to the pool must go back
    //  with its common-modifier DPs CLEARED, not overwritten with defaults.
    //  Overwritten defaults are local values that outrank the next renter's
    //  default style, so a recycled Button could never show its styled
    //  alignment/padding again.
    //
    //  Mount at the VStack tail so exactly one control is returned and
    //  rented back, which makes the instance-identity check deterministic —
    //  and that check is what keeps the "cleared" assertions non-vacuous
    //  (a freshly-constructed control trivially has no local values).
    //  Note the re-rented element carries NO modifiers, so ApplyModifiers'
    //  unset arms cannot run on it: CleanElement is the only thing that
    //  could have released these values.
    // ════════════════════════════════════════════════════════════════════

    internal class ModifierPoolClearValueOnRent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("DropPoolClear", () => set(1)),
                    Button("RemountPoolClear", () => set(2)),
                    phase switch
                    {
                        0 => TextBlock("pool-clear-carrier") with
                        {
                            Modifiers = new ElementModifiers
                            {
                                Margin = new Thickness(3, 4, 5, 6),
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
                                AccessKey = "P",
                            }
                        },
                        1 => Empty(),
                        // Remounted with no modifiers at all.
                        _ => TextBlock("pool-clear-carrier-2"),
                    });
            });

            await Harness.Render();
            var first = H.FindText("pool-clear-carrier");
            H.Check("PoolClear_Phase0_Present", first is not null);
            H.Check("PoolClear_Phase0_LocalValuesWritten",
                first is not null
                && first.ReadLocalValue(FrameworkElement.MarginProperty) != DependencyProperty.UnsetValue
                && first.ReadLocalValue(FrameworkElement.WidthProperty) != DependencyProperty.UnsetValue
                && first.ReadLocalValue(FrameworkElement.HorizontalAlignmentProperty) != DependencyProperty.UnsetValue
                && first.ReadLocalValue(UIElement.OpacityProperty) != DependencyProperty.UnsetValue
                && first.ReadLocalValue(UIElement.VisibilityProperty) != DependencyProperty.UnsetValue
                && first.ReadLocalValue(UIElement.AccessKeyProperty) != DependencyProperty.UnsetValue);

            H.ClickButton("DropPoolClear");
            await Harness.Render();
            H.Check("PoolClear_Phase1_Returned", H.FindText("pool-clear-carrier") is null);

            H.ClickButton("RemountPoolClear");
            await Harness.Render();
            var second = H.FindText("pool-clear-carrier-2");
            // Load-bearing: without instance reuse every check below passes trivially.
            H.Check("PoolClear_Phase2_ReusedInstance",
                first is not null && ReferenceEquals(first, second));
            if (second is null) return;

            H.Check("PoolClear_Phase2_MarginCleared",
                second.ReadLocalValue(FrameworkElement.MarginProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolClear_Phase2_WidthCleared",
                second.ReadLocalValue(FrameworkElement.WidthProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolClear_Phase2_HeightCleared",
                second.ReadLocalValue(FrameworkElement.HeightProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolClear_Phase2_MinSizeCleared",
                second.ReadLocalValue(FrameworkElement.MinWidthProperty) == DependencyProperty.UnsetValue
                && second.ReadLocalValue(FrameworkElement.MinHeightProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolClear_Phase2_MaxSizeCleared",
                second.ReadLocalValue(FrameworkElement.MaxWidthProperty) == DependencyProperty.UnsetValue
                && second.ReadLocalValue(FrameworkElement.MaxHeightProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolClear_Phase2_AlignmentCleared",
                second.ReadLocalValue(FrameworkElement.HorizontalAlignmentProperty) == DependencyProperty.UnsetValue
                && second.ReadLocalValue(FrameworkElement.VerticalAlignmentProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolClear_Phase2_OpacityCleared",
                second.ReadLocalValue(UIElement.OpacityProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolClear_Phase2_VisibilityCleared",
                second.ReadLocalValue(UIElement.VisibilityProperty) == DependencyProperty.UnsetValue
                && second.Visibility == Visibility.Visible);
            H.Check("PoolClear_Phase2_AccessKeyCleared",
                second.ReadLocalValue(UIElement.AccessKeyProperty) == DependencyProperty.UnsetValue);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  The Border/Panel half of the same contract. Padding / CornerRadius /
    //  BorderThickness / Background are declared on Border (and Background on
    //  Panel) rather than FrameworkElement, and CleanElement used to reset
    //  them by ASSIGNING the default — so a recycled Border was handed to its
    //  next renter carrying Thickness(0) as a *local* value that outranks any
    //  Style the renter attaches. A ContextFlyout is a live object graph owned
    //  by the previous renter's component and was never released at all.
    // ════════════════════════════════════════════════════════════════════

    internal class ModifierPoolClearValueControl(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("DropPoolCtrl", () => set(1)),
                    Button("RemountPoolCtrl", () => set(2)),
                    phase switch
                    {
                        0 => Factories.Border(Factories.TextBlock("pool-ctrl-carrier"))
                            .Padding(21)
                            .CornerRadius(13)
                            .BorderThickness(7)
                            .BorderBrush("#FF66AA33")
                            .Background("#FF113355")
                            .WithContextFlyout(MenuItems(MenuItem("PoolCtrlMenu"))),
                        1 => Empty(),
                        // Remounted with no modifiers at all.
                        _ => Factories.Border(Factories.TextBlock("pool-ctrl-carrier-2")),
                    });
            });

            await Harness.Render();
            var first = H.FindControl<Microsoft.UI.Xaml.Controls.Border>(b =>
                b.Child is TextBlock tb && tb.Text == "pool-ctrl-carrier");
            H.Check("PoolCtrl_Phase0_Present", first is not null);
            H.Check("PoolCtrl_Phase0_LocalValuesWritten",
                first is not null
                && first.ReadLocalValue(Microsoft.UI.Xaml.Controls.Border.PaddingProperty) != DependencyProperty.UnsetValue
                && first.ReadLocalValue(Microsoft.UI.Xaml.Controls.Border.BorderBrushProperty) != DependencyProperty.UnsetValue
                && first.ReadLocalValue(Microsoft.UI.Xaml.Controls.Border.BackgroundProperty) != DependencyProperty.UnsetValue
                && first.ReadLocalValue(UIElement.ContextFlyoutProperty) != DependencyProperty.UnsetValue);

            H.ClickButton("DropPoolCtrl");
            await Harness.Render();
            // Load-bearing premise, not a status report: it is the *drop* that forces the
            // pool round-trip. Without it the reconciler could update the Border in place,
            // and every "cleared" check below would be testing ApplyModifiers' unset arm
            // instead of CleanElement — passing for entirely the wrong reason.
            H.Check("PoolCtrl_Phase1_Returned",
                H.FindControl<Microsoft.UI.Xaml.Controls.Border>(b =>
                    b.Child is TextBlock tb && tb.Text == "pool-ctrl-carrier") is null);
            H.ClickButton("RemountPoolCtrl");
            await Harness.Render();
            var second = H.FindControl<Microsoft.UI.Xaml.Controls.Border>(b =>
                b.Child is TextBlock tb && tb.Text == "pool-ctrl-carrier-2");
            // Load-bearing: without instance reuse every check below passes trivially.
            H.Check("PoolCtrl_Phase2_ReusedInstance",
                first is not null && ReferenceEquals(first, second));
            if (second is null) return;

            // Cleared, not "reset to Thickness(0)" — ReadLocalValue is the only oracle that
            // tells those two apart, and the difference is the whole of issue #952.
            H.Check("PoolCtrl_Phase2_PaddingCleared",
                second.ReadLocalValue(Microsoft.UI.Xaml.Controls.Border.PaddingProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolCtrl_Phase2_ContextFlyoutCleared",
                second.ReadLocalValue(UIElement.ContextFlyoutProperty) == DependencyProperty.UnsetValue
                && second.ContextFlyout is null);
            H.Check("PoolCtrl_Phase2_CornerRadiusCleared",
                second.ReadLocalValue(Microsoft.UI.Xaml.Controls.Border.CornerRadiusProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolCtrl_Phase2_BorderThicknessCleared",
                second.ReadLocalValue(Microsoft.UI.Xaml.Controls.Border.BorderThicknessProperty) == DependencyProperty.UnsetValue);
            // Issue #985 moved Border's five clears into CleanElement's FE-common chain.
            // BorderBrush and Background were the two that no live fixture exercised —
            // only the source-scanning invariants pinned them, and a scanner cannot tell
            // whether the line it found actually runs for a Border receiver.
            H.Check("PoolCtrl_Phase2_BorderBrushCleared",
                second.ReadLocalValue(Microsoft.UI.Xaml.Controls.Border.BorderBrushProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolCtrl_Phase2_BackgroundCleared",
                second.ReadLocalValue(Microsoft.UI.Xaml.Controls.Border.BackgroundProperty) == DependencyProperty.UnsetValue);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Issue #985 — the Control and Panel halves of the same contract.
    //  ApplyModifiers writes Padding / CornerRadius / BorderThickness /
    //  BorderBrush / Background / IsEnabled onto a *Control* receiver and
    //  Background onto a *Panel*, but CleanElement only ever reset the Border
    //  arm. A recycled ScrollViewer or Grid was therefore handed to its next
    //  renter still carrying the previous renter's LOCAL values — and a local
    //  value outranks every Style setter in WinUI's precedence order, so the
    //  new renter could never show its styled padding/background again.
    //
    //  ScrollViewer is the Control carrier precisely because ScrollViewerElement
    //  declares none of the six properties: any local value that survives the
    //  recycle came from ApplyModifiers, not from the descriptor re-writing it
    //  on the second mount. A Button would be vacuous for IsEnabled —
    //  ButtonElement writes EffectiveIsEnabled unconditionally on every mount,
    //  so `IsEnabled` would read as a local value whether or not the pool
    //  cleared it. Grid is the Panel carrier for the same reason.
    //
    //  Both carriers are remounted with NO modifiers at all, so ApplyModifiers'
    //  unset arms cannot run on them: CleanElement is the only thing that could
    //  have released these values. One check per property, so deleting a single
    //  ClearValue turns exactly one check red.
    // ════════════════════════════════════════════════════════════════════

    internal class ModifierPoolClearValueControlPanel(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("DropPoolCp", () => set(1)),
                    Button("RemountPoolCp", () => set(2)),
                    phase switch
                    {
                        0 => Factories.ScrollViewer(Factories.TextBlock("pool-cp-control"))
                            .Padding(23)
                            .CornerRadius(11)
                            .BorderThickness(6)
                            .BorderBrush("#FF3366CC")
                            .Background("#FF224466")
                            .IsEnabled(false),
                        1 => Empty(),
                        // Remounted with no modifiers at all.
                        _ => Factories.ScrollViewer(Factories.TextBlock("pool-cp-control-2")),
                    },
                    phase switch
                    {
                        // Padding and CornerRadius are declared by Grid, not by Panel, so they
                        // exercise the nested arm inside CleanElement's Panel branch. Without
                        // them the Grid carrier reaches only Panel.Background and the two
                        // Grid-specific clears are covered by the raw source scan alone —
                        // which cannot tell a live clear from a commented-out one.
                        0 => Factories.Grid([GridSize.Star()], [GridSize.Star()],
                                Factories.TextBlock("pool-cp-panel"))
                            .Background("#FF884422")
                            .Padding(19)
                            .CornerRadius(7),
                        1 => Empty(),
                        _ => Factories.Grid([GridSize.Star()], [GridSize.Star()],
                                Factories.TextBlock("pool-cp-panel-2")),
                    });
            });

            await Harness.Render();
            var firstControl = H.FindControl<ScrollViewer>(sv =>
                sv.Content is TextBlock tb && tb.Text == "pool-cp-control");
            var firstPanel = H.FindControl<Grid>(g =>
                g.Children.Count > 0 && g.Children[0] is TextBlock tb && tb.Text == "pool-cp-panel");
            H.Check("PoolCp_Phase0_Present", firstControl is not null && firstPanel is not null);

            // Establishes that the modifiers really wrote LOCAL values, which is what makes
            // every "cleared" assertion below able to come out the other way.
            H.Check("PoolCp_Phase0_ControlLocalValuesWritten",
                firstControl is not null
                && firstControl.ReadLocalValue(Control.PaddingProperty) != DependencyProperty.UnsetValue
                && firstControl.ReadLocalValue(Control.CornerRadiusProperty) != DependencyProperty.UnsetValue
                && firstControl.ReadLocalValue(Control.BorderThicknessProperty) != DependencyProperty.UnsetValue
                && firstControl.ReadLocalValue(Control.BorderBrushProperty) != DependencyProperty.UnsetValue
                && firstControl.ReadLocalValue(Control.BackgroundProperty) != DependencyProperty.UnsetValue
                && firstControl.ReadLocalValue(Control.IsEnabledProperty) != DependencyProperty.UnsetValue);
            H.Check("PoolCp_Phase0_PanelLocalValuesWritten",
                firstPanel is not null
                && firstPanel.ReadLocalValue(Panel.BackgroundProperty) != DependencyProperty.UnsetValue
                && firstPanel.ReadLocalValue(WinUI.Grid.PaddingProperty) != DependencyProperty.UnsetValue
                && firstPanel.ReadLocalValue(WinUI.Grid.CornerRadiusProperty) != DependencyProperty.UnsetValue);

            H.ClickButton("DropPoolCp");
            await Harness.Render();
            // Load-bearing premise, one per carrier. Without the drop the reconciler could
            // update in place, and the cleared checks below would be exercising
            // ApplyModifiers' unset arm rather than CleanElement — passing for the wrong
            // reason. The ReferenceEquals guards further down do not cover this: an in-place
            // update reuses the instance too, so instance identity cannot tell a pool
            // round-trip from an update. Both carriers need their own check, or the half
            // that lacks one can silently go vacuous.
            H.Check("PoolCp_Phase1_Returned",
                H.FindControl<ScrollViewer>(sv =>
                    sv.Content is TextBlock tb && tb.Text == "pool-cp-control") is null);
            H.Check("PoolCp_Phase1_PanelReturned",
                H.FindControl<Grid>(g =>
                    g.Children.Count > 0 && g.Children[0] is TextBlock tb
                    && tb.Text == "pool-cp-panel") is null);

            H.ClickButton("RemountPoolCp");
            await Harness.Render();
            var secondControl = H.FindControl<ScrollViewer>(sv =>
                sv.Content is TextBlock tb && tb.Text == "pool-cp-control-2");
            var secondPanel = H.FindControl<Grid>(g =>
                g.Children.Count > 0 && g.Children[0] is TextBlock tb && tb.Text == "pool-cp-panel-2");

            // Load-bearing: without instance reuse every check below passes trivially,
            // because a freshly constructed control has no local values to begin with.
            H.Check("PoolCp_Phase2_ReusedControlInstance",
                firstControl is not null && ReferenceEquals(firstControl, secondControl));
            H.Check("PoolCp_Phase2_ReusedPanelInstance",
                firstPanel is not null && ReferenceEquals(firstPanel, secondPanel));
            if (secondControl is null || secondPanel is null) return;

            // Cleared, not "reset to the default value" — ReadLocalValue is the only oracle
            // that tells those two apart, and that difference is the whole of the bug.
            H.Check("PoolCp_Phase2_ControlPaddingCleared",
                secondControl.ReadLocalValue(Control.PaddingProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolCp_Phase2_ControlCornerRadiusCleared",
                secondControl.ReadLocalValue(Control.CornerRadiusProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolCp_Phase2_ControlBorderThicknessCleared",
                secondControl.ReadLocalValue(Control.BorderThicknessProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolCp_Phase2_ControlBorderBrushCleared",
                secondControl.ReadLocalValue(Control.BorderBrushProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolCp_Phase2_ControlBackgroundCleared",
                secondControl.ReadLocalValue(Control.BackgroundProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolCp_Phase2_ControlIsEnabledCleared",
                secondControl.ReadLocalValue(Control.IsEnabledProperty) == DependencyProperty.UnsetValue
                && secondControl.IsEnabled);
            H.Check("PoolCp_Phase2_PanelBackgroundCleared",
                secondPanel.ReadLocalValue(Panel.BackgroundProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolCp_Phase2_PanelPaddingCleared",
                secondPanel.ReadLocalValue(WinUI.Grid.PaddingProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolCp_Phase2_PanelCornerRadiusCleared",
                secondPanel.ReadLocalValue(WinUI.Grid.CornerRadiusProperty) == DependencyProperty.UnsetValue);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Issue #985, StackPanel half — StackPanel is a Panel, not a Control, so
    //  its Padding and CornerRadius need their own clears inside the Panel
    //  arm. It gets its own fixture because the Grid above cannot reach those
    //  lines: CleanElement's Grid and StackPanel arms are mutually exclusive
    //  `else if` branches under the same Panel receiver, so a Grid takes the
    //  first arm and never executes the StackPanel one. (Both types do carry
    //  the properties — #1003 widened the gates to Grid — which is exactly why
    //  the exclusivity, not the property set, is what splits the fixtures.)
    // ════════════════════════════════════════════════════════════════════

    internal class ModifierPoolClearValueStackPadding(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("DropPoolStack", () => set(1)),
                    Button("RemountPoolStack", () => set(2)),
                    phase switch
                    {
                        0 => VStack(Factories.TextBlock("pool-stack-carrier"))
                            .Padding(17)
                            .CornerRadius(9)
                            .Background("#FF335577"),
                        1 => Empty(),
                        // Remounted with no modifiers at all.
                        _ => VStack(Factories.TextBlock("pool-stack-carrier-2")),
                    });
            });

            await Harness.Render();
            var first = H.FindControl<StackPanel>(sp =>
                sp.Children.Count > 0 && sp.Children[0] is TextBlock tb && tb.Text == "pool-stack-carrier");
            H.Check("PoolStack_Phase0_Present", first is not null);
            H.Check("PoolStack_Phase0_LocalValuesWritten",
                first is not null
                && first.ReadLocalValue(StackPanel.PaddingProperty) != DependencyProperty.UnsetValue
                && first.ReadLocalValue(StackPanel.CornerRadiusProperty) != DependencyProperty.UnsetValue
                && first.ReadLocalValue(Panel.BackgroundProperty) != DependencyProperty.UnsetValue);

            H.ClickButton("DropPoolStack");
            await Harness.Render();
            // Load-bearing premise, not a status report — see PoolCtrl_Phase1_Returned.
            // Without the drop the reconciler could update the StackPanel in place, and the
            // cleared checks below would be exercising ApplyModifiers' unset arm rather
            // than CleanElement.
            H.Check("PoolStack_Phase1_Returned",
                H.FindControl<StackPanel>(sp =>
                    sp.Children.Count > 0 && sp.Children[0] is TextBlock tb
                    && tb.Text == "pool-stack-carrier") is null);
            H.ClickButton("RemountPoolStack");
            await Harness.Render();
            var second = H.FindControl<StackPanel>(sp =>
                sp.Children.Count > 0 && sp.Children[0] is TextBlock tb && tb.Text == "pool-stack-carrier-2");
            // Load-bearing: without instance reuse every check below passes trivially.
            H.Check("PoolStack_Phase2_ReusedInstance",
                first is not null && ReferenceEquals(first, second));
            if (second is null) return;

            H.Check("PoolStack_Phase2_PaddingCleared",
                second.ReadLocalValue(StackPanel.PaddingProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolStack_Phase2_CornerRadiusCleared",
                second.ReadLocalValue(StackPanel.CornerRadiusProperty) == DependencyProperty.UnsetValue);
            H.Check("PoolStack_Phase2_BackgroundCleared",
                second.ReadLocalValue(Panel.BackgroundProperty) == DependencyProperty.UnsetValue);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Issue #985, TextBlock half — TextBlock is the fourth Padding receiver
    //  ApplyModifiers writes to, and it is neither a Control, a Border nor a
    //  Panel, so it needs its own arm. The clear itself predates #985 (it
    //  arrived with #950) but lived in CleanElement's TextBlock case arm,
    //  past the point every source-scanning invariant stops reading — so the
    //  one receiver in Padding's control gate that no scanner and no fixture
    //  covered was the one that had shipped longest. #985 moved the line into
    //  the FE-common chain; this fixture is the live half of that.
    // ════════════════════════════════════════════════════════════════════

    internal class ModifierPoolClearValueTextPadding(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    Button("DropPoolText", () => set(1)),
                    Button("RemountPoolText", () => set(2)),
                    phase switch
                    {
                        0 => Factories.TextBlock("pool-text-carrier").Padding(23),
                        1 => Empty(),
                        // Remounted with no modifiers at all.
                        _ => Factories.TextBlock("pool-text-carrier-2"),
                    });
            });

            await Harness.Render();
            var first = H.FindControl<TextBlock>(tb => tb.Text == "pool-text-carrier");
            H.Check("PoolText_Phase0_Present", first is not null);
            H.Check("PoolText_Phase0_LocalValueWritten",
                first is not null
                && first.ReadLocalValue(Microsoft.UI.Xaml.Controls.TextBlock.PaddingProperty) != DependencyProperty.UnsetValue);

            H.ClickButton("DropPoolText");
            await Harness.Render();
            // Load-bearing premise, not a status report — see PoolCtrl_Phase1_Returned.
            // Without the drop the reconciler could update the TextBlock in place, and the
            // cleared check below would be exercising ApplyModifiers' unset arm rather
            // than CleanElement.
            H.Check("PoolText_Phase1_Returned",
                H.FindControl<TextBlock>(tb => tb.Text == "pool-text-carrier") is null);

            H.ClickButton("RemountPoolText");
            await Harness.Render();
            var second = H.FindControl<TextBlock>(tb => tb.Text == "pool-text-carrier-2");
            // Load-bearing: without instance reuse the check below passes trivially.
            H.Check("PoolText_Phase2_ReusedInstance",
                first is not null && ReferenceEquals(first, second));
            if (second is null) return;

            H.Check("PoolText_Phase2_PaddingCleared",
                second.ReadLocalValue(Microsoft.UI.Xaml.Controls.TextBlock.PaddingProperty) == DependencyProperty.UnsetValue);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Issue #952, BiDi half — Margin/Padding are computed into a local
    //  that overlays the logical (inline-start/end) variants on top of the
    //  physical value. That local used to be seeded with
    //  `m.Margin ?? oldM?.Margin`, which made it non-null whenever the
    //  PREVIOUS render had a margin and so left the unset arm unreachable:
    //  `.Margin(x)` → no margin reset nothing at all.
    //
    //  The previous physical value is still the base the overlay computes
    //  from — this fixture pins that, and pins that dropping every margin
    //  modifier now releases the local value.
    // ════════════════════════════════════════════════════════════════════

    internal class ModifierInlineMarginCarryForward(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);

                var textMods = phase switch
                {
                    0 => new ElementModifiers { Margin = new Thickness(2, 3, 4, 5), MarginInlineStart = 20 },
                    // Physical margin dropped, a DIFFERENT inline edge set: the overlay
                    // must still compute from the previous *physical* margin (2,3,4,5),
                    // not from the live overlaid value (20,3,4,5) the control carries.
                    1 => new ElementModifiers { MarginInlineEnd = 30 },
                    _ => new ElementModifiers(),
                };

                var borderMods = phase switch
                {
                    0 => new ElementModifiers { Padding = new Thickness(2, 3, 4, 5), PaddingInlineStart = 20 },
                    1 => new ElementModifiers { PaddingInlineEnd = 30 },
                    _ => new ElementModifiers(),
                };

                return VStack(
                    Button("InlineCarryPhase", () => set(phase + 1)),
                    TextBlock("inline-carry-target") with { Modifiers = textMods },
                    Border(TextBlock("inline-carry-border-child")) with { Modifiers = borderMods });
            });

            Border? FindCarryBorder() => H.FindControl<Border>(
                b => b.Child is TextBlock tb && tb.Text == "inline-carry-border-child");

            await Harness.Render();
            var text = H.FindText("inline-carry-target");
            var border = FindCarryBorder();
            H.Check("InlineCarry_Phase0_Present", text is not null && border is not null);
            // LTR: inline-start overlays Left, the other three edges come from the physical margin.
            H.Check("InlineCarry_Phase0_MarginOverlaid",
                text is not null && text.Margin == new Thickness(20, 3, 4, 5));
            H.Check("InlineCarry_Phase0_PaddingOverlaid",
                border is not null && border.Padding == new Thickness(20, 3, 4, 5));

            H.ClickButton("InlineCarryPhase");
            await Harness.Render();
            text = H.FindText("inline-carry-target");
            border = FindCarryBorder();
            // Left comes from the previous physical margin (2), NOT from the live 20 the
            // phase-0 overlay wrote — that difference is the whole point of this check.
            H.Check("InlineCarry_Phase1_MarginBaseIsPreviousPhysical",
                text is not null && text.Margin == new Thickness(2, 3, 30, 5));
            H.Check("InlineCarry_Phase1_PaddingBaseIsPreviousPhysical",
                border is not null && border.Padding == new Thickness(2, 3, 30, 5));

            H.ClickButton("InlineCarryPhase");
            await Harness.Render();
            text = H.FindText("inline-carry-target");
            border = FindCarryBorder();
            // Only an inline modifier was set last render, never a physical one, so the
            // reset has to key off the inline modifiers to fire at all.
            H.Check("InlineCarry_Phase2_MarginLocalValueReleased",
                text is not null
                && text.ReadLocalValue(FrameworkElement.MarginProperty) == DependencyProperty.UnsetValue);
            H.Check("InlineCarry_Phase2_PaddingLocalValueReleased",
                border is not null
                && border.ReadLocalValue(Microsoft.UI.Xaml.Controls.Border.PaddingProperty) == DependencyProperty.UnsetValue);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Issue #952, inline-drop half — the write guard compares the resolved
    //  value against the previous *physical* value, so dropping an inline
    //  edge while the physical value stays put resolves straight back to the
    //  old value and used to skip the write entirely, stranding the previous
    //  render's inline edge on the control. And BorderThickness's reset arm
    //  keyed only off oldM.BorderThickness, so an inline-only border was
    //  never released at all.
    // ════════════════════════════════════════════════════════════════════

    internal class ModifierInlineDropRestoresPhysical(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var physical = new Thickness(2, 3, 4, 5);

            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);

                // Physical value identical in both phases; only the inline edge goes away.
                var textMods = phase == 0
                    ? new ElementModifiers { Margin = physical, MarginInlineStart = 20 }
                    : new ElementModifiers { Margin = physical };

                var padMods = phase == 0
                    ? new ElementModifiers { Padding = physical, PaddingInlineStart = 20 }
                    : new ElementModifiers { Padding = physical };

                var thicknessMods = phase == 0
                    ? new ElementModifiers { BorderThickness = new Thickness(2), BorderInlineStart = new Thickness(9) }
                    : new ElementModifiers { BorderThickness = new Thickness(2) };

                // No physical border at all — only the reset guard keying off
                // BorderInlineStart can release this one.
                var inlineOnlyMods = phase == 0
                    ? new ElementModifiers { BorderInlineStart = new Thickness(9) }
                    : new ElementModifiers();

                return VStack(
                    Button("InlineDropPhase", () => set(phase + 1)),
                    TextBlock("inline-drop-target") with { Modifiers = textMods },
                    Border(TextBlock("inline-drop-pad-child")) with { Modifiers = padMods },
                    Border(TextBlock("inline-drop-thickness-child")) with { Modifiers = thicknessMods },
                    Border(TextBlock("inline-drop-only-child")) with { Modifiers = inlineOnlyMods });
            });

            Border? FindBorder(string childText) => H.FindControl<Border>(
                b => b.Child is TextBlock tb && tb.Text == childText);

            await Harness.Render();
            var text = H.FindText("inline-drop-target");
            var pad = FindBorder("inline-drop-pad-child");
            var thickness = FindBorder("inline-drop-thickness-child");
            var inlineOnly = FindBorder("inline-drop-only-child");

            H.Check("InlineDrop_Phase0_Present",
                text is not null && pad is not null && thickness is not null && inlineOnly is not null);
            // Phase 0 has to actually differ from the physical value, or phase 1 proves nothing.
            H.Check("InlineDrop_Phase0_MarginOverlaid",
                text is not null && text.Margin == new Thickness(20, 3, 4, 5));
            H.Check("InlineDrop_Phase0_PaddingOverlaid",
                pad is not null && pad.Padding == new Thickness(20, 3, 4, 5));
            H.Check("InlineDrop_Phase0_BorderOverlaid",
                thickness is not null && thickness.BorderThickness == new Thickness(9, 2, 2, 2));
            H.Check("InlineDrop_Phase0_InlineOnlyBorderApplied",
                inlineOnly is not null && inlineOnly.BorderThickness == new Thickness(9, 0, 0, 0));

            H.ClickButton("InlineDropPhase");
            await Harness.Render();
            text = H.FindText("inline-drop-target");
            pad = FindBorder("inline-drop-pad-child");
            thickness = FindBorder("inline-drop-thickness-child");
            inlineOnly = FindBorder("inline-drop-only-child");

            H.Check("InlineDrop_Phase1_MarginBackToPhysical",
                text is not null && text.Margin == physical);
            H.Check("InlineDrop_Phase1_PaddingBackToPhysical",
                pad is not null && pad.Padding == physical);
            H.Check("InlineDrop_Phase1_BorderBackToPhysical",
                thickness is not null && thickness.BorderThickness == new Thickness(2));
            H.Check("InlineDrop_Phase1_InlineOnlyBorderReleased",
                inlineOnly is not null
                && inlineOnly.ReadLocalValue(Microsoft.UI.Xaml.Controls.Border.BorderThicknessProperty)
                    == DependencyProperty.UnsetValue);
        }
    }
}
