using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;
using AP = Microsoft.UI.Xaml.Automation.AutomationProperties;
using APeers = Microsoft.UI.Xaml.Automation.Peers;

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
        /// <summary>
        /// The <c>IsTabStop</c> value the phase-0 bag asks for. Named rather than repeated as
        /// a literal so the "applied" and "released" checks below are visibly reading the same
        /// value the bag wrote — the release check asserts the effective value has moved
        /// <em>away from</em> it, deliberately not towards a guessed WinUI default.
        /// </summary>
        private const bool Phase0IsTabStop = false;

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
                        IsTabStop = Phase0IsTabStop,
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

                // Issue #986 — load-bearing for the five ModifierClear_*Cleared checks
                // below. Those read ReadLocalValue(dp) == UnsetValue, which a property
                // that was NEVER written also satisfies, so without this the release
                // checks would pass against a reconciler whose *set* arms were deleted.
                // Assert both that a local value exists and that the effective value is
                // the deliberately non-default one the phase-0 bag asked for.
                H.Check("ModifierClear_Tier1Applied_IsTabStop",
                    initial.ReadLocalValue(UIElement.IsTabStopProperty) != DependencyProperty.UnsetValue
                    && initial.IsTabStop == Phase0IsTabStop);
                H.Check("ModifierClear_Tier1Applied_TabIndex",
                    initial.ReadLocalValue(Control.TabIndexProperty) != DependencyProperty.UnsetValue
                    && initial.TabIndex == 7);
                H.Check("ModifierClear_Tier1Applied_XYFocusKeyboardNavigation",
                    initial.ReadLocalValue(UIElement.XYFocusKeyboardNavigationProperty) != DependencyProperty.UnsetValue
                    && initial.XYFocusKeyboardNavigation
                       == Microsoft.UI.Xaml.Input.XYFocusKeyboardNavigationMode.Enabled);
                H.Check("ModifierClear_Tier1Applied_ElementSoundMode",
                    initial.ReadLocalValue(Control.ElementSoundModeProperty) != DependencyProperty.UnsetValue
                    && initial.ElementSoundMode == ElementSoundMode.Off);
                H.Check("ModifierClear_Tier1Applied_HeadingLevel",
                    initial.ReadLocalValue(AP.HeadingLevelProperty) != DependencyProperty.UnsetValue
                    && AP.GetHeadingLevel(initial) == APeers.AutomationHeadingLevel.Level2);
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

                // Issue #986 — these five were written on phase 0 above and had no unset
                // arm at all, so before the fix they stayed pinned on the control through
                // every later render. They were already in the phase-0 bag before this
                // change and simply never asserted on, which is why the gap was invisible
                // here. Each check reads the same DP its set arm writes, so a reset that
                // targeted a *different* identifier (Control.IsTabStopProperty vs
                // UIElement.IsTabStopProperty, for instance) would still fail.
                //
                // The release oracle is ReadLocalValue == UnsetValue and nothing else. The
                // effective-value comparison used to be folded into the same check, but
                // that conflated two different failures: a reconciler that failed to
                // release, and a phase-0 bag value that happens to equal the control's
                // default. Only the first is a product bug; the second means this fixture
                // stopped proving anything and needs a different phase-0 value. Splitting
                // them keeps the non-vacuity guard — without it a property whose default
                // already equals the phase-0 value would satisfy the release oracle
                // trivially — while making a failure self-attributing.
                H.Check("ModifierClear_IsTabStopCleared",
                    button.ReadLocalValue(UIElement.IsTabStopProperty) == DependencyProperty.UnsetValue);
                H.Check("ModifierClear_TabIndexCleared",
                    button.ReadLocalValue(Control.TabIndexProperty) == DependencyProperty.UnsetValue);
                H.Check("ModifierClear_XYFocusKeyboardNavigationCleared",
                    button.ReadLocalValue(UIElement.XYFocusKeyboardNavigationProperty) == DependencyProperty.UnsetValue);
                H.Check("ModifierClear_ElementSoundModeCleared",
                    button.ReadLocalValue(Control.ElementSoundModeProperty) == DependencyProperty.UnsetValue);
                H.Check("ModifierClear_HeadingLevelCleared",
                    button.ReadLocalValue(AP.HeadingLevelProperty) == DependencyProperty.UnsetValue);

                // Non-vacuity. Read *after* the release, so each asserts "the value this
                // control falls back to differs from the one phase 0 forced" — i.e. the
                // phase-0 write was observable and the release above is a real transition
                // rather than a no-op. A failure here is a fixture problem, not a
                // regression: pick a phase-0 value that is not the control's default.
                H.Check("ModifierClear_IsTabStopPhase0WasDistinct",
                    button.IsTabStop != Phase0IsTabStop);
                H.Check("ModifierClear_TabIndexPhase0WasDistinct",
                    button.TabIndex != 7);
                H.Check("ModifierClear_XYFocusKeyboardNavigationPhase0WasDistinct",
                    button.XYFocusKeyboardNavigation
                    != Microsoft.UI.Xaml.Input.XYFocusKeyboardNavigationMode.Enabled);
                H.Check("ModifierClear_ElementSoundModePhase0WasDistinct",
                    button.ElementSoundMode != ElementSoundMode.Off);
                H.Check("ModifierClear_HeadingLevelPhase0WasDistinct",
                    AP.GetHeadingLevel(button) != APeers.AutomationHeadingLevel.Level2);
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
                && first.ReadLocalValue(UIElement.ContextFlyoutProperty) != DependencyProperty.UnsetValue);

            H.ClickButton("DropPoolCtrl");
            await Harness.Render();

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

    // ════════════════════════════════════════════════════════════════════
    //  Issue #986, accessibility half — ApplyAccessibilityModifiers opened
    //  with `if (a is null) return;` while its caller invokes it whenever
    //  EITHER bag is non-null. So the one transition it most needed to
    //  handle — the whole Accessibility sub-record dropped this render —
    //  returned immediately and released nothing. Ten of its eleven
    //  properties additionally had no per-property unset arm even when the
    //  bag survived.
    //
    //  Both mechanisms are covered here, in that order: phase 1 keeps the
    //  bag and drops every property but one, phase 2 drops the bag itself.
    //  Phase 0 asserts the values actually landed first — every release
    //  check below reads UnsetValue on a property that was never written,
    //  so without the phase-0 check the whole fixture would pass against a
    //  reconciler that ignored the bag entirely.
    // ════════════════════════════════════════════════════════════════════

    internal class AccessibilityModifierClearResets(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);

                var mods = phase switch
                {
                    0 => new ElementModifiers
                    {
                        Accessibility = new AccessibilityModifiers
                        {
                            HelpText = "a11y-help",
                            FullDescription = "a11y-full",
                            LandmarkType = APeers.AutomationLandmarkType.Navigation,
                            AccessibilityView = APeers.AccessibilityView.Raw,
                            IsRequiredForForm = true,
                            LiveSetting = APeers.AutomationLiveSetting.Assertive,
                            PositionInSet = 3,
                            SizeOfSet = 9,
                            Level = 4,
                            ItemStatus = "a11y-status",
                            TabFocusNavigation = Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Cycle,
                            LabeledBy = "a11y-label-target",
                        },
                    },
                    // Bag survives, one property kept: isolates the per-property arms from
                    // the whole-bag path below, so a fix that only handled `a is null`
                    // still fails here.
                    1 => new ElementModifiers
                    {
                        Accessibility = new AccessibilityModifiers { HelpText = "a11y-help" },
                    },
                    // Sub-record gone entirely — the transition the early return swallowed.
                    _ => new ElementModifiers(),
                };

                return VStack(
                    Button("A11yClearPhase", () => setPhase(phase + 1)),
                    TextBlock("A11yLabelSource") with
                    {
                        Modifiers = new ElementModifiers { AutomationId = "a11y-label-target" },
                    },
                    Button("A11yClearTarget") with { Modifiers = mods });
            });

            // "Released" and "never written" both read UnsetValue, so this discriminates
            // only in combination with the phase-0 applied check below.
            static bool Released(FrameworkElement fe, DependencyProperty dp) =>
                fe.ReadLocalValue(dp) == DependencyProperty.UnsetValue;

            // The element LabeledBy is expected to resolve to. Re-read each time rather
            // than captured once: a null here would make the ReferenceEquals below
            // trivially satisfiable against another null, so it is asserted non-null.
            TextBlock? labelSource() =>
                H.FindControl<TextBlock>(t => t.Text == "A11yLabelSource");

            static bool NonHelpTextPropertiesReleased(FrameworkElement fe) =>
                Released(fe, AP.FullDescriptionProperty)
                && Released(fe, AP.LandmarkTypeProperty)
                && Released(fe, AP.AccessibilityViewProperty)
                && Released(fe, AP.IsRequiredForFormProperty)
                && Released(fe, AP.LiveSettingProperty)
                && Released(fe, AP.PositionInSetProperty)
                && Released(fe, AP.SizeOfSetProperty)
                && Released(fe, AP.LevelProperty)
                && Released(fe, AP.ItemStatusProperty)
                && Released(fe, UIElement.TabFocusNavigationProperty);

            await Harness.Render();
            var applied = H.FindButton("A11yClearTarget");
            H.Check("A11yClear_Phase0_Present", applied is not null);
            if (applied is not null)
            {
                H.Check("A11yClear_Phase0_Applied",
                    AP.GetHelpText(applied) == "a11y-help"
                    && AP.GetFullDescription(applied) == "a11y-full"
                    && AP.GetLandmarkType(applied) == APeers.AutomationLandmarkType.Navigation
                    && AP.GetAccessibilityView(applied) == APeers.AccessibilityView.Raw
                    && AP.GetIsRequiredForForm(applied)
                    && AP.GetLiveSetting(applied) == APeers.AutomationLiveSetting.Assertive
                    && AP.GetPositionInSet(applied) == 3
                    && AP.GetSizeOfSet(applied) == 9
                    && AP.GetLevel(applied) == 4
                    && AP.GetItemStatus(applied) == "a11y-status"
                    && applied.TabFocusNavigation == Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Cycle);

                // LabeledBy resolves an AutomationId against the visual tree, which can fail
                // during mount and get deferred to Loaded — an eventual condition, which is
                // the one shape WaitFor is sound for. But "eventual" requires the predicate
                // to be false at t=0, and ReferenceEquals(null, null) is true: with the
                // label source not yet discoverable *and* LabeledBy not yet resolved, the
                // wait returns immediately at zero elapsed time and the check below then
                // fails spuriously on a still-null source. Requiring the source inside the
                // same lambda is what makes the wait actually wait.
                // Load-bearing for A11yClear_Phase1_LabeledByReleased below in the same way
                // Phase0_Applied is for the rest.
                var resolved = await Harness.WaitFor(() =>
                {
                    var source = labelSource();
                    return source is not null && ReferenceEquals(AP.GetLabeledBy(applied), source);
                });
                H.Check("A11yClear_Phase0_LabeledByResolved",
                    resolved && labelSource() is not null);
            }

            H.ClickButton("A11yClearPhase");
            await Harness.Render();

            var partial = H.FindButton("A11yClearTarget");
            H.Check("A11yClear_Phase1_Present", partial is not null);
            if (partial is not null)
            {
                H.Check("A11yClear_Phase1_KeptPropertySurvives",
                    AP.GetHelpText(partial) == "a11y-help");
                H.Check("A11yClear_Phase1_DroppedPropertiesReleased",
                    NonHelpTextPropertiesReleased(partial));
                // LabeledBy already had an unset arm before issue #986; what is new is that
                // the arm now also cancels a still-parked deferred resolution, so a dropped
                // label cannot be re-applied by a Loaded handler from an earlier render.
                // The race itself is not driven here — the harness cannot hold Loaded open
                // across a re-render — so the guard is pinned structurally instead, by
                // ModifierUnsetClearValueTests.Deferred_LabeledBy_Rechecks_Pending_Request.
                H.Check("A11yClear_Phase1_LabeledByReleased",
                    Released(partial, AP.LabeledByProperty));
                H.Check("A11yClear_Phase1_LabeledByEffectiveNull",
                    AP.GetLabeledBy(partial) is null);
            }

            H.ClickButton("A11yClearPhase");
            await Harness.Render();

            var dropped = H.FindButton("A11yClearTarget");
            H.Check("A11yClear_Phase2_Present", dropped is not null);
            if (dropped is not null)
            {
                H.Check("A11yClear_Phase2_WholeBagReleased",
                    Released(dropped, AP.HelpTextProperty)
                    && NonHelpTextPropertiesReleased(dropped));
                // Effective-value spot check: on a Button these DPs have no style setter, so
                // this cannot replace the local-value reads above — it only rules out a reset
                // that released the local value while leaving the property reading the
                // previous render's value some other way. Split per-property so a failure
                // names the property instead of just "one of four".
                H.Check("A11yClear_Phase2_HelpTextDefaulted",
                    AP.GetHelpText(dropped) != "a11y-help");
                H.Check("A11yClear_Phase2_ItemStatusDefaulted",
                    AP.GetItemStatus(dropped) != "a11y-status");
                H.Check("A11yClear_Phase2_PositionInSetDefaulted",
                    AP.GetPositionInSet(dropped) != 3);
                H.Check("A11yClear_Phase2_TabFocusNavigationDefaulted",
                    dropped.TabFocusNavigation
                    != Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Cycle);
            }
        }
    }
}
