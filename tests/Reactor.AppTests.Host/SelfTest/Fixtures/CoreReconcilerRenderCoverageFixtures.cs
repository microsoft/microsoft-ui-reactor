using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor.Descriptors;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static Microsoft.UI.Reactor.Controls.Validation.ValidationVisualizerDsl;
using static Microsoft.UI.Reactor.Controls.Validation.ValidationRuleDsl;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selftest fixtures that raise coverage of the CORE reconciler + RenderContext +
/// V1Protocol lifecycle/children-strategy arms that the merged (unit + selftest)
/// report still misses. Each fixture mounts real WinUI controls and drives the
/// Update/Mount dispatch + RenderContext hooks through real re-renders / unmounts,
/// asserting real invariants (in-place reuse, correct patched value, correct child
/// order) — not just non-null.
///
/// Targets (uncovered arms as of baseline):
///   • OverlayLifecycle.cs   — Flyout / MenuFlyout / Popup / CommandBar /
///                             CommandBarFlyout / MenuBar UPDATE reconcile arms.
///   • CompositeLifecycle.cs — FormField validation update, ValidationVisualizer
///                             Warning/Inline styles, ValidationRule evaluate.
///   • ChildrenStrategy.cs   — PreMountedItems (templated FlipView) grow / shrink /
///                             reconcile (registered via the per-host descriptor
///                             path — "descriptors retained for isolated selftests").
///   • NavigationHostLifecycle.cs — cache-mode mount + LRU eviction callback.
///   • RenderContext.cs      — UseReducer (functional + dispatch, threadSafe on/off),
///                             no-op sets, and window/env hooks (UseWindowState,
///                             UseIsActive, UseDpi, UseBreakpoint, UseReducedMotion,
///                             UseHighContrastScheme, UseColorScheme, UseClosingGuard).
///   • ChildrenStrategy.cs   — also TreeChildren reconcile via the untyped TreeView
///                             (node reorder / child add-remove / new node).
/// </summary>
internal static class CoreReconcilerRenderCoverageFixtures
{
    // Reference-identity check for the in-place-reuse assertions. The controls
    // compared here are always reference types; the object-typed parameters make
    // that explicit (and keep the reference-equality analyzer from misreading the
    // call site as a value-type comparison).
    private static bool SameInstance(object? a, object? b) => ReferenceEquals(a, b);

    // ════════════════════════════════════════════════════════════════════
    //  OverlayLifecycle — Flyout UPDATE reconcile arms
    //  Target: OverlayLifecycle.UpdateFlyoutElement (content type change,
    //  placement/show-mode change, lazy Opened/Closed wiring).
    // ════════════════════════════════════════════════════════════════════
    internal class OverlayFlyoutUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var opened = new int[1];
            var closed = new int[1];
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var target = Button("fly-target", () => { });
                // Phase 0: TextBlock flyout content, Auto placement, no handlers.
                // Phase 1: Button flyout content (type change), Bottom placement,
                //          Standard show-mode, animations on, Opened/Closed newly wired.
                Element content = phase == 0 ? TextBlock("fly-c0") : Button("fly-c1", () => { });
                var flyout = (Flyout(target, content) with
                {
                    Placement = phase == 0 ? WinPrim.FlyoutPlacementMode.Top : WinPrim.FlyoutPlacementMode.Bottom,
                    ShowMode = phase == 0 ? WinPrim.FlyoutShowMode.Auto : WinPrim.FlyoutShowMode.Standard,
                    AreOpenCloseAnimationsEnabled = phase != 0,
                    OnOpened = phase == 0 ? null : () => opened[0]++,
                    OnClosed = phase == 0 ? null : () => closed[0]++,
                });
                return VStack(Button("FlyGo", () => set(1)), flyout);
            });

            await Harness.Render();
            var target0 = H.FindButton("fly-target");
            H.Check("Flyout_TargetMounted", target0 is not null);
            var flyout0 = target0?.Flyout as Flyout;
            H.Check("Flyout_ContentIsTextBlock", flyout0?.Content is TextBlock);

            H.ClickButton("FlyGo");
            await Harness.Render();

            var target1 = H.FindButton("fly-target");
            // Target Button reconciled in-place (same element type across renders).
            H.Check("Flyout_TargetReused", SameInstance(target0, target1));
            var flyout1 = target1?.Flyout as Flyout;
            // The attached Flyout is patched in place (same instance), not replaced.
            H.Check("Flyout_SameFlyoutInstance", flyout0 is not null && SameInstance(flyout0, flyout1));
            H.Check("Flyout_ContentTypeSwapped", flyout1?.Content is Button);
            H.Check("Flyout_PlacementPatched", flyout1?.Placement == WinPrim.FlyoutPlacementMode.Bottom);
            H.Check("Flyout_ShowModePatched", flyout1?.ShowMode == WinPrim.FlyoutShowMode.Standard);
            H.Check("Flyout_AnimationsPatched", flyout1?.AreOpenCloseAnimationsEnabled == true);

            // Verify the newly-wired OnOpened/OnClosed handlers actually fire (the
            // lazy null->non-null wiring arm). Open, then close, the patched flyout.
            if (target1 is not null && flyout1 is not null)
            {
                flyout1.ShowAt(target1);
                H.Check("Flyout_OnOpenedFired", await Harness.WaitFor(() => opened[0] > 0));
                flyout1.Hide();
                H.Check("Flyout_OnClosedFired", await Harness.WaitFor(() => closed[0] > 0));
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  OverlayLifecycle — MenuFlyout UPDATE reconcile.
    //  Phase 0->1 keeps the SAME target and changes the item set (exercises the
    //  in-place UpdateMenuFlyoutItems arm — flyout reused). Phase 1->2 changes
    //  the target TYPE (exercises target remount + fresh MenuFlyout creation).
    // ════════════════════════════════════════════════════════════════════
    internal class OverlayMenuFlyoutUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                Element target = phase < 2
                    ? Button("mf-target", () => { })
                    : TextBlock("mf-target-tb");
                var items = phase == 0
                    ? new[] { MenuItem("mi-a", () => { }), MenuItem("mi-b", () => { }) }
                    : new[] { MenuItem("mi-a2", () => { }), MenuItem("mi-b2", () => { }), MenuItem("mi-c2", () => { }) };
                return VStack(Button("MfGo", () => set(phase + 1)), MenuFlyout(target, items));
            });

            await Harness.Render();
            var btnTarget0 = H.FindButton("mf-target");
            H.Check("MenuFlyout_ButtonTargetMounted", btnTarget0 is not null);
            var flyout0 = btnTarget0?.Flyout as MenuFlyout;
            H.Check("MenuFlyout_InitialItems", flyout0?.Items.Count == 2);

            // Phase 0 -> 1: same Button target, item set grows 2 -> 3. The target
            // and its MenuFlyout are reused; items are patched in place.
            H.ClickButton("MfGo");
            await Harness.Render();
            var btnTarget1 = H.FindButton("mf-target");
            H.Check("MenuFlyout_TargetReusedInPlace", SameInstance(btnTarget0, btnTarget1));
            var flyout1 = btnTarget1?.Flyout as MenuFlyout;
            H.Check("MenuFlyout_FlyoutReusedInPlace", SameInstance(flyout0, flyout1));
            H.Check("MenuFlyout_ItemsPatchedInPlace", flyout1?.Items.Count == 3);
            // Exact item texts/order after the in-place item update (a broken update
            // that appended but left old labels stale would fail this).
            H.Check("MenuFlyout_ItemTextsPatched",
                flyout1?.Items.Count == 3
                && (flyout1?.Items[0] as MenuFlyoutItem)?.Text == "mi-a2"
                && (flyout1?.Items[1] as MenuFlyoutItem)?.Text == "mi-b2"
                && (flyout1?.Items[2] as MenuFlyoutItem)?.Text == "mi-c2");

            // Phase 1 -> 2: target type changes Button -> TextBlock, forcing a
            // target remount + a freshly attached MenuFlyout on the new target.
            H.ClickButton("MfGo");
            await Harness.Render();
            H.Check("MenuFlyout_OldButtonGone", H.FindButton("mf-target") is null);
            var tbTarget = H.FindText("mf-target-tb");
            H.Check("MenuFlyout_NewTextTargetMounted", tbTarget is not null);
            var freshFlyout = tbTarget is null ? null : WinPrim.FlyoutBase.GetAttachedFlyout(tbTarget) as MenuFlyout;
            H.Check("MenuFlyout_FreshFlyoutHas3Items", freshFlyout?.Items.Count == 3);
            H.Check("MenuFlyout_FreshItemTexts",
                freshFlyout?.Items.Count == 3
                && (freshFlyout?.Items[0] as MenuFlyoutItem)?.Text == "mi-a2"
                && (freshFlyout?.Items[1] as MenuFlyoutItem)?.Text == "mi-b2"
                && (freshFlyout?.Items[2] as MenuFlyoutItem)?.Text == "mi-c2");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  OverlayLifecycle — Popup UPDATE (scalar props + child reconcile/remount)
    // ════════════════════════════════════════════════════════════════════
    internal class OverlayPopupUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                // Phase 0: TextBlock child, offsets 0, closed.
                // Phase 1: Button child (type change → remount), offsets 25, open.
                Element child = phase == 0 ? TextBlock("popup-c0") : Button("popup-c1", () => { });
                var popup = (Popup(child) with
                {
                    IsOpen = phase != 0,
                    HorizontalOffset = phase == 0 ? 0 : 25,
                    VerticalOffset = phase == 0 ? 0 : 25,
                    IsLightDismissEnabled = phase == 0,
                });
                return VStack(Button("PopGo", () => set(1)), popup);
            });

            await Harness.Render();
            var popup0 = H.FindControl<WinPrim.Popup>(_ => true);
            H.Check("Popup_Mounted", popup0 is not null);
            H.Check("Popup_InitialChildIsText", popup0?.Child is TextBlock);
            H.Check("Popup_InitiallyClosed", popup0?.IsOpen == false);
            H.Check("Popup_InitialLightDismissOn", popup0?.IsLightDismissEnabled == true);

            H.ClickButton("PopGo");
            await Harness.Render();

            var popup1 = H.FindControl<WinPrim.Popup>(_ => true);
            H.Check("Popup_Reused", SameInstance(popup0, popup1));
            H.Check("Popup_ChildTypeSwapped", popup1?.Child is Button);
            H.Check("Popup_HOffsetPatched", popup1?.HorizontalOffset == 25);
            H.Check("Popup_VOffsetPatched", popup1?.VerticalOffset == 25);
            H.Check("Popup_IsOpenPatched", popup1?.IsOpen == true);
            H.Check("Popup_LightDismissPatchedOff", popup1?.IsLightDismissEnabled == false);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  OverlayLifecycle — CommandBar UPDATE (content null-drop + command diff)
    // ════════════════════════════════════════════════════════════════════
    internal class OverlayCommandBarUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                AppBarItemBase[] primary = phase == 0
                    ? new AppBarItemBase[] { AppBarButton("cb-save"), AppBarButton("cb-open") }
                    : new AppBarItemBase[] { AppBarButton("cb-save") };
                // Phase 0 provides Content; phase 1 drops it (null) — exercises the
                // ReconcileChild remove arm (cb.Content = null).
                var bar = (CommandBar(primaryCommands: primary) with
                {
                    Content = phase == 0 ? TextBlock("cb-content") : null,
                    IsOpen = phase != 0,
                });
                return VStack(Button("CbGo", () => set(1)), bar);
            });

            await Harness.Render();
            var bar0 = H.FindControl<CommandBar>(_ => true);
            H.Check("CommandBar_Mounted", bar0 is not null);
            H.Check("CommandBar_InitialContent", bar0?.Content is TextBlock);
            H.Check("CommandBar_InitialPrimary2", bar0?.PrimaryCommands.Count == 2);

            H.ClickButton("CbGo");
            await Harness.Render();

            var bar1 = H.FindControl<CommandBar>(_ => true);
            H.Check("CommandBar_Reused", SameInstance(bar0, bar1));
            H.Check("CommandBar_ContentDropped", bar1?.Content is null);
            H.Check("CommandBar_PrimaryShrank", bar1?.PrimaryCommands.Count == 1);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  OverlayLifecycle — CommandBarFlyout UPDATE.
    //  Phase 0->1 keeps the SAME target and changes placement + command sets
    //  (exercises the existing-flyout reuse arm: patched placement + cleared/
    //  re-added primary & secondary commands). Phase 1->2 changes the target
    //  TYPE (exercises target remount + fresh flyout creation).
    // ════════════════════════════════════════════════════════════════════
    internal class OverlayCommandBarFlyoutUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                Element target = phase < 2 ? Button("cbf-target", () => { }) : TextBlock("cbf-target-tb");
                AppBarItemBase[] primary = phase == 0
                    ? new AppBarItemBase[] { AppBarButton("cbf-cut") }
                    : new AppBarItemBase[] { AppBarButton("cbf-copy"), AppBarButton("cbf-paste") };
                AppBarItemBase[] secondary = phase == 0
                    ? new AppBarItemBase[] { AppBarButton("cbf-more1") }
                    : new AppBarItemBase[] { AppBarButton("cbf-more2"), AppBarButton("cbf-more3") };
                return VStack(
                    Button("CbfGo", () => set(phase + 1)),
                    CommandBarFlyout(target, primaryCommands: primary, secondaryCommands: secondary) with
                    {
                        Placement = phase == 0 ? WinPrim.FlyoutPlacementMode.Top : WinPrim.FlyoutPlacementMode.Bottom,
                    });
            });

            await Harness.Render();
            var btnTarget0 = H.FindButton("cbf-target");
            H.Check("CmdBarFlyout_ButtonTargetMounted", btnTarget0 is not null);
            var flyout0 = btnTarget0 is null ? null : WinPrim.FlyoutBase.GetAttachedFlyout(btnTarget0) as CommandBarFlyout;
            H.Check("CmdBarFlyout_InitialPrimary1", flyout0?.PrimaryCommands.Count == 1);
            H.Check("CmdBarFlyout_InitialSecondary1", flyout0?.SecondaryCommands.Count == 1);
            H.Check("CmdBarFlyout_InitialPlacementTop", flyout0?.Placement == WinPrim.FlyoutPlacementMode.Top);

            // Phase 0 -> 1: same Button target. The attached CommandBarFlyout is
            // reused; placement is patched and both command collections are
            // cleared and re-populated in place.
            H.ClickButton("CbfGo");
            await Harness.Render();
            var btnTarget1 = H.FindButton("cbf-target");
            H.Check("CmdBarFlyout_TargetReusedInPlace", SameInstance(btnTarget0, btnTarget1));
            var flyout1 = btnTarget1 is null ? null : WinPrim.FlyoutBase.GetAttachedFlyout(btnTarget1) as CommandBarFlyout;
            H.Check("CmdBarFlyout_FlyoutReusedInPlace", SameInstance(flyout0, flyout1));
            H.Check("CmdBarFlyout_PlacementPatchedBottom", flyout1?.Placement == WinPrim.FlyoutPlacementMode.Bottom);
            H.Check("CmdBarFlyout_PrimaryReAdded2", flyout1?.PrimaryCommands.Count == 2);
            H.Check("CmdBarFlyout_SecondaryReAdded2", flyout1?.SecondaryCommands.Count == 2);
            // Exact labels/order after clear-and-repopulate (wrong labels/order would fail).
            H.Check("CmdBarFlyout_PrimaryLabelsPatched",
                flyout1?.PrimaryCommands.Count == 2
                && (flyout1?.PrimaryCommands[0] as AppBarButton)?.Label == "cbf-copy"
                && (flyout1?.PrimaryCommands[1] as AppBarButton)?.Label == "cbf-paste");
            H.Check("CmdBarFlyout_SecondaryLabelsPatched",
                flyout1?.SecondaryCommands.Count == 2
                && (flyout1?.SecondaryCommands[0] as AppBarButton)?.Label == "cbf-more2"
                && (flyout1?.SecondaryCommands[1] as AppBarButton)?.Label == "cbf-more3");

            // Phase 1 -> 2: target type changes Button -> TextBlock, forcing a fresh
            // CommandBarFlyout attached to the new target.
            H.ClickButton("CbfGo");
            await Harness.Render();
            H.Check("CmdBarFlyout_OldButtonGone", H.FindButton("cbf-target") is null);
            var tbTarget = H.FindText("cbf-target-tb");
            H.Check("CmdBarFlyout_NewTargetMounted", tbTarget is not null);
            var flyout2 = tbTarget is null ? null : WinPrim.FlyoutBase.GetAttachedFlyout(tbTarget) as CommandBarFlyout;
            H.Check("CmdBarFlyout_FreshPrimary2", flyout2?.PrimaryCommands.Count == 2);
            H.Check("CmdBarFlyout_FreshSecondary2", flyout2?.SecondaryCommands.Count == 2);
            H.Check("CmdBarFlyout_FreshPrimaryLabels",
                flyout2?.PrimaryCommands.Count == 2
                && (flyout2?.PrimaryCommands[0] as AppBarButton)?.Label == "cbf-copy"
                && (flyout2?.PrimaryCommands[1] as AppBarButton)?.Label == "cbf-paste");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  OverlayLifecycle — MenuBar UPDATE (title patch + add/remove menus)
    // ════════════════════════════════════════════════════════════════════
    internal class OverlayMenuBarUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                // Phase 0: two menus (File, Edit).
                // Phase 1: File renamed, Edit kept, View added (title patch + append).
                var menus = phase == 0
                    ? new[]
                    {
                        Menu("File", MenuItem("New", () => { }), MenuItem("Open", () => { })),
                        Menu("Edit", MenuItem("Cut", () => { })),
                    }
                    : new[]
                    {
                        Menu("File!", MenuItem("New", () => { }), MenuItem("Open", () => { }), MenuItem("Close", () => { })),
                        Menu("Edit", MenuItem("Cut", () => { })),
                        Menu("View", MenuItem("Zoom", () => { })),
                    };
                return VStack(Button("MbGo", () => set(1)), MenuBar(menus));
            });

            await Harness.Render();
            var mb0 = H.FindControl<MenuBar>(_ => true);
            H.Check("MenuBar_Mounted", mb0 is not null);
            H.Check("MenuBar_Initial2Menus", mb0?.Items.Count == 2);
            H.Check("MenuBar_InitialFileTitle",
                mb0?.Items.Count == 2 && (mb0?.Items[0] as MenuBarItem)?.Title == "File");

            H.ClickButton("MbGo");
            await Harness.Render();

            var mb1 = H.FindControl<MenuBar>(_ => true);
            H.Check("MenuBar_Reused", SameInstance(mb0, mb1));
            H.Check("MenuBar_Grew3Menus", mb1?.Items.Count == 3);
            H.Check("MenuBar_FileTitlePatched",
                mb1?.Items.Count == 3 && (mb1?.Items[0] as MenuBarItem)?.Title == "File!");
            H.Check("MenuBar_ViewAppended",
                mb1?.Items.Count == 3 && (mb1?.Items[2] as MenuBarItem)?.Title == "View");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  CompositeLifecycle — FormField with validation UPDATE.
    //  Provides a ValidationContext so the auto-validate + error-styling arms
    //  run on both mount and update.
    // ════════════════════════════════════════════════════════════════════
    internal class CompositeFormFieldValidationUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            ValidationContext? captured = null;
            host.Mount(ctx =>
            {
                var valCtx = ctx.UseValidationContext();
                captured = valCtx;
                var (phase, set) = ctx.UseState(0);
                // Phase 0: empty value -> Required fails (error). Phase 1: a valid
                // value -> Required passes. This lets the post-update assertion PROVE
                // UpdateFormField re-validated: the stale mount-time error can only
                // clear if update-time auto-validation actually ran.
                var value = phase == 0 ? "" : "Alice";
                var field = FormField(
                    TextBox(value).Validate("ffname", value, Validate.Required()),
                    label: phase == 0 ? "Name" : "Full name",
                    description: phase == 0 ? "d0" : "d1");
                return VStack(
                    Button("FfGo", () => set(1)),
                    field.Provide(ValidationContexts.Current, valCtx));
            });

            await Harness.Render();
            H.Check("FormField_LabelMounted", H.FindText("Name") is not null);
            // Mount ran auto-validation against the provided context: empty Required is invalid.
            H.Check("FormField_MountInvalid", captured?.HasError("ffname") == true);

            H.ClickButton("FfGo");
            await Harness.Render();

            H.Check("FormField_LabelUpdated", H.FindText("Full name") is not null);
            H.Check("FormField_OldLabelGone", H.FindText("Name") is null);
            // UpdateFormField re-validated with the now-valid value and cleared the
            // stale error. This assertion FAILS if update-time validation is skipped
            // (the mount-time error would persist).
            H.Check("FormField_UpdateRevalidatedAndCleared", captured?.HasError("ffname") == false);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  CompositeLifecycle — ValidationVisualizer Warning-InfoBar + Inline styles.
    //  A manually-populated context carries Warning + Error messages so the
    //  Warning→InfoBarSeverity.Warning arm and the Inline error-text arm run.
    // ════════════════════════════════════════════════════════════════════
    internal class CompositeValidationVisualizerStyles(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var warnCtx = ctx.UseMemo(() =>
                {
                    var c = new ValidationContext();
                    c.RegisterField("wf");
                    c.Add("wf", "warn-only-message", Severity.Warning);
                    return c;
                });
                var errCtx = ctx.UseMemo(() =>
                {
                    var c = new ValidationContext();
                    c.RegisterField("ef");
                    c.Add("ef", "inline-error-message", Severity.Error);
                    return c;
                });
                return VStack(
                    // InfoBar style with only a Warning message -> Warning severity arm.
                    ValidationVisualizer(VisualizerStyle.InfoBar, TextBlock("vv-warn-body"), title: "WarnBar")
                        .Provide(ValidationContexts.Current, warnCtx),
                    // Inline style with an Error message -> inline error-text arm.
                    ValidationVisualizer(VisualizerStyle.Inline, TextBlock("vv-inline-body"))
                        .Provide(ValidationContexts.Current, errCtx));
            });

            await Harness.Render();

            var infoBar = H.FindControl<InfoBar>(_ => true);
            H.Check("Visualizer_WarningInfoBarRendered", infoBar is not null);
            H.Check("Visualizer_InfoBarSeverityWarning",
                infoBar?.Severity == InfoBarSeverity.Warning);
            H.Check("Visualizer_InlineBodyRendered", H.FindText("vv-inline-body") is not null);
            H.Check("Visualizer_InlineErrorTextRendered",
                H.FindTextContaining("inline-error-message") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  CompositeLifecycle — ValidationRule mount + update (Evaluate against ctx).
    // ════════════════════════════════════════════════════════════════════
    internal class CompositeValidationRuleUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            ValidationContext? captured = null;
            host.Mount(ctx =>
            {
                // Own the context so the rule (which reads ValidationContexts.Current
                // via the .Provide below) and our assertion observe the same object.
                var valCtx = ctx.UseMemo(() =>
                {
                    var c = new ValidationContext();
                    c.RegisterField("rulefield");
                    return c;
                });
                captured = valCtx;
                var (n, set) = ctx.UseState(1);
                // Rule fails while n < 3 -> message added under "rulefield".
                var rule = ValidationRule(() => n >= 3, "n must be >= 3", "rulefield");
                return VStack(
                    Button("RuleBump", () => set(n + 1)),
                    rule.Provide(ValidationContexts.Current, valCtx));
            });

            await Harness.Render();
            // Mount evaluated the rule against the live context (predicate false).
            H.Check("ValidationRule_MountEvaluatedFail", captured?.HasError("rulefield") == true);

            // Bump twice: 1 -> 2 (still failing) -> 3 (passing). Each re-render runs
            // UpdateValidationRule -> rule.Evaluate against the live context.
            H.ClickButton("RuleBump");
            await Harness.Render();
            H.ClickButton("RuleBump");
            await Harness.Render();

            H.Check("ValidationRule_UpdateEvaluatedPass", captured?.HasError("rulefield") == false);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ChildrenStrategy — PreMountedItems (templated FlipView) grow / shrink /
    //  reconcile / unbind. The descriptor is not registered by default (the
    //  public FlipView<T> factory routes through TemplatedListHandler), so we
    //  wire the descriptor per-host — "descriptors retained for isolated
    //  selftests" (TemplatedListHandler.cs).
    // ════════════════════════════════════════════════════════════════════
    internal class PreMountedFlipViewReconcile(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            // Per-host descriptor registration routes every closed
            // TemplatedFlipViewElement<T> through PreMountedItems (arm 1 wins over
            // the global TemplatedListHandler); isolated to this host.
            host.Reconciler.RegisterHandlerForDerivedTypes<TemplatedFlipViewElementBase, FlipView>(
                new DescriptorHandler<TemplatedFlipViewElementBase, FlipView>(TemplatedFlipViewDescriptor.Descriptor));

            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                // phase 0: [10,20]         (mount, 2 items)
                // phase 1: [11,20,30,40]   (reconcile item 0 in place, append 2)
                // phase 2: [11]            (truncate to 1)
                IReadOnlyList<int> items = phase switch
                {
                    0 => new[] { 10, 20 },
                    1 => new[] { 11, 20, 30, 40 },
                    _ => new[] { 11 },
                };
                return VStack(
                    Button("FvNext", () => set(phase + 1)),
                    FlipView(items, i => "k" + i, (item, idx) => TextBlock("fv-" + item)));
            });

            await Harness.Render();
            var flip = H.FindControl<FlipView>(_ => true);
            H.Check("PreMountedFlip_Mounted", flip is not null);
            H.Check("PreMountedFlip_Initial2", flip?.Items.Count == 2);
            var item0Before = flip?.Items.Count > 0 ? flip?.Items[0] as TextBlock : null;
            H.Check("PreMountedFlip_Item0Text10", item0Before?.Text == "fv-10");

            // Grow: reconcile item 0 (10 -> 11, in place) + append 2 new tail items.
            H.ClickButton("FvNext");
            await Harness.Render();
            var flipGrow = H.FindControl<FlipView>(_ => true);
            H.Check("PreMountedFlip_ControlReused", SameInstance(flip, flipGrow));
            H.Check("PreMountedFlip_Grew4", flipGrow?.Items.Count == 4);
            var item0After = flipGrow?.Items.Count > 0 ? flipGrow?.Items[0] as TextBlock : null;
            H.Check("PreMountedFlip_Item0ReconciledInPlace", SameInstance(item0Before, item0After));
            H.Check("PreMountedFlip_Item0Patched", item0After?.Text == "fv-11");
            H.Check("PreMountedFlip_TailAppended",
                flipGrow?.Items.Count == 4 && (flipGrow?.Items[3] as TextBlock)?.Text == "fv-40");

            // Shrink: truncate down to a single item.
            H.ClickButton("FvNext");
            await Harness.Render();
            var flipShrink = H.FindControl<FlipView>(_ => true);
            H.Check("PreMountedFlip_Truncated1", flipShrink?.Items.Count == 1);
            H.Check("PreMountedFlip_SurvivorKept",
                flipShrink?.Items.Count == 1 && (flipShrink?.Items[0] as TextBlock)?.Text == "fv-11");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  NavigationHostLifecycle — cache-mode mount + LRU eviction.
    //  A small CacheSize plus navigation through more distinct routes than the
    //  cache holds forces the LRU eviction callback (unmount the evicted page).
    // ════════════════════════════════════════════════════════════════════
    internal class NavigationHostCacheEviction(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            // Per-route unmount counter: a cached page stays mounted, but an
            // LRU-evicted page is unmounted (its component cleanup runs). This lets
            // us prove eviction happened rather than merely that a page left the tree.
            var unmounts = new Dictionary<string, int>();
            host.Mount(ctx =>
            {
                var nav = ctx.UseNavigation<string>("r0");
                Element RoutePage(string r) => RenderEachTime(c =>
                {
                    c.UseEffect(() => () => unmounts[r] = unmounts.TryGetValue(r, out var v) ? v + 1 : 1);
                    return TextBlock("route-" + r);
                });
                return VStack(
                    Button("NavR1", () => nav.Navigate("r1")),
                    Button("NavR2", () => nav.Navigate("r2")),
                    Button("NavR3", () => nav.Navigate("r3")),
                    Button("NavR0", () => nav.Navigate("r0")),
                    NavigationHost(nav, RoutePage) with
                    {
                        Transition = NavigationTransition.None,
                        CacheMode = NavigationCacheMode.Enabled,
                        CacheSize = 2,
                    });
            });

            await Harness.Render();
            H.Check("NavCacheEvict_InitialRoute", H.FindText("route-r0") is not null);
            H.Check("NavCacheEvict_NoUnmountsYet", unmounts.Count == 0);

            // Navigate away caches the departing page. Cache holds 2: leaving r0
            // caches it, leaving r1 fills the cache ([r0,r1]), leaving r2 overflows
            // and evicts the LRU entry r0 -> r0's page is unmounted.
            H.ClickButton("NavR1");
            H.Check("NavCacheEvict_R1", await Harness.WaitFor(() => H.FindText("route-r1") is not null));
            H.ClickButton("NavR2");
            H.Check("NavCacheEvict_R2", await Harness.WaitFor(() => H.FindText("route-r2") is not null));
            H.ClickButton("NavR3");
            H.Check("NavCacheEvict_R3", await Harness.WaitFor(() => H.FindText("route-r3") is not null));

            // Eviction proof: the LRU page (r0) was unmounted, while a newer cached
            // page (r2) was retained (not unmounted).
            H.Check("NavCacheEvict_LruPageUnmounted", unmounts.GetValueOrDefault("r0") == 1);
            H.Check("NavCacheEvict_NewerCachedPageRetained", unmounts.GetValueOrDefault("r2") == 0);

            // Returning to the evicted route re-mounts it fresh (it was not in cache).
            H.ClickButton("NavR0");
            H.Check("NavCacheEvict_BackToEvictedRemounts", await Harness.WaitFor(() => H.FindText("route-r0") is not null));
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  RenderContext — UseReducer functional updater + dispatch, threadSafe
    //  on/off, plus no-op (unchanged) updates.
    // ════════════════════════════════════════════════════════════════════
    internal class RenderContextReducers(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var renders = new int[1];
            host.Mount(ctx =>
            {
                renders[0]++;   // count component renders to verify no-op setters don't re-render
                var (a, updateA) = ctx.UseReducer(0);                       // functional, non-threadSafe
                var (b, updateB) = ctx.UseReducer(0, threadSafe: true);      // functional, threadSafe
                var (c, dispatchC) = ctx.UseReducer<int, string>(
                    (s, act) => act == "inc" ? s + 1 : s, 0);                // reducer/dispatch, non-threadSafe
                var (d, dispatchD) = ctx.UseReducer<int, string>(
                    (s, act) => act == "inc" ? s + 1 : s, 0, threadSafe: true); // reducer/dispatch, threadSafe
                return VStack(
                    Button("A+", () => updateA(x => x + 1)),
                    Button("Anoop", () => updateA(x => x)),          // no-op: reducer returns same value
                    Button("B+", () => updateB(x => x + 2)),
                    Button("Bnoop", () => updateB(x => x)),
                    Button("Cinc", () => dispatchC("inc")),
                    Button("Cnoop", () => dispatchC("other")),       // no-op: reducer returns same state
                    Button("Dinc", () => dispatchD("inc")),
                    Button("Dnoop", () => dispatchD("other")),
                    TextBlock($"R:{a},{b},{c},{d}"));
            });

            await Harness.Render();
            H.Check("Reducers_Initial", H.FindText("R:0,0,0,0") is not null);
            var rendersAfterMount = renders[0];

            // Isolated no-ops first: none may re-render (the setter must skip
            // _requestRerender when the value is unchanged) and none may change state.
            H.ClickButton("Anoop");
            H.ClickButton("Bnoop");
            H.ClickButton("Cnoop");
            H.ClickButton("Dnoop");
            await Harness.Render();
            H.Check("Reducers_NoOpDidNotRerender", renders[0] == rendersAfterMount);
            H.Check("Reducers_NoOpValuesUnchanged", H.FindText("R:0,0,0,0") is not null);

            // Real updates: each must re-render and change its own value.
            H.ClickButton("A+");
            H.ClickButton("B+");
            H.ClickButton("Cinc");
            H.ClickButton("Dinc");
            await Harness.Render();
            H.Check("Reducers_RealUpdatesRerendered", renders[0] > rendersAfterMount);
            H.Check("Reducers_AfterOps", H.FindText("R:1,2,1,1") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  RenderContext — threadSafe UseState setter + no-op set.
    // ════════════════════════════════════════════════════════════════════
    internal class RenderContextThreadSafeState(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var renders = new int[1];
            host.Mount(ctx =>
            {
                renders[0]++;
                var (n, setN) = ctx.UseState(5, threadSafe: true);
                return VStack(
                    Button("TsSet7", () => setN(7)),
                    Button("TsSetSame", () => setN(n)),   // no-op: equal value, no re-render
                    TextBlock($"TS:{n}"));
            });

            await Harness.Render();
            H.Check("ThreadSafeState_Initial", H.FindText("TS:5") is not null);
            var rendersAfterMount = renders[0];

            H.ClickButton("TsSetSame");   // equal -> changed=false -> must NOT re-render
            await Harness.Render();
            H.Check("ThreadSafeState_NoOpDidNotRerender", renders[0] == rendersAfterMount);
            H.Check("ThreadSafeState_NoOpKept", H.FindText("TS:5") is not null);

            H.ClickButton("TsSet7");
            await Harness.Render();
            H.Check("ThreadSafeState_ChangedRerendered", renders[0] > rendersAfterMount);
            H.Check("ThreadSafeState_Changed", H.FindText("TS:7") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  RenderContext — window / environment hooks inside a real host window.
    //  Mounts a child component that reads the window/env hooks, then unmounts
    //  it so the effect cleanups (event unsubscribe / guard dispose) also run.
    // ════════════════════════════════════════════════════════════════════
    internal class RenderContextWindowEnvHooks(Harness h) : SelfTestFixtureBase(h)
    {
        private static Element EnvHooksChild(RenderContext c)
        {
            var ws = c.UseWindowState();
            var active = c.UseIsActive();
            var dpi = c.UseDpi();
            // Breakpoint boundary: width >= 0 is always true and width >= 1,000,000px
            // is always false, so these must be true/false respectively regardless of
            // the exact live width. A constant/always-true impl would fail one of them.
            var wide = c.UseBreakpoint(0.0);
            var narrow = c.UseBreakpoint(1_000_000.0);
            var reduced = c.UseReducedMotion();
            var hcScheme = c.UseHighContrastScheme();
            var scheme = c.UseColorScheme();
            c.UseClosingGuard(() => true);
            return VStack(
                TextBlock($"dpi={dpi}"),
                TextBlock($"bp={wide}:{narrow}"),
                TextBlock($"scheme={scheme}"),
                TextBlock($"active={active};ws={ws};reduced={reduced};hc={(hcScheme ?? "none")}"));
        }

        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (show, set) = ctx.UseState(true);
                return VStack(
                    Button("EnvHide", () => set(false)),
                    show ? RenderEachTime(EnvHooksChild) : TextBlock("env-unmounted"));
            });

            await Harness.Render();
            var dpiText = H.FindTextContaining("dpi=");
            H.Check("EnvHooks_DpiRead", dpiText is not null);
            // A real host window reports a positive DPI — parse the value and assert
            // it is greater than zero (rather than just "not the string dpi=0").
            uint parsedDpi = 0;
            if (dpiText is not null && dpiText.Text.StartsWith("dpi=", StringComparison.Ordinal))
                uint.TryParse(dpiText.Text.AsSpan(4), out parsedDpi);
            H.Check("EnvHooks_DpiPositive", parsedDpi > 0);
            // UseBreakpoint compares the real window width against the threshold:
            // >= 1px is true, >= 1,000,000px is false. A constant/always-true impl
            // would fail one of these.
            H.Check("EnvHooks_BreakpointBoundary", H.FindText("bp=True:False") is not null);
            // UseColorScheme resolves to a concrete scheme (Light, Dark, or
            // HighContrast) — parse the rendered value and assert it is a valid
            // ColorScheme, so this is robust under a High Contrast test theme.
            var schemeText = H.FindTextContaining("scheme=");
            H.Check("EnvHooks_ColorSchemeResolved",
                schemeText is not null
                && schemeText.Text.StartsWith("scheme=", StringComparison.Ordinal)
                && Enum.TryParse<ColorScheme>(schemeText.Text["scheme=".Length..], out _));
            H.Check("EnvHooks_EnvLineRendered", H.FindTextContaining("active=") is not null);

            // Unmount the hooks subtree -> effect cleanups run (event unsubscribe /
            // closing-guard token dispose) without throwing.
            H.ClickButton("EnvHide");
            await Harness.Render();
            H.Check("EnvHooks_UnmountedCleanly", H.FindText("env-unmounted") is not null);
            H.Check("EnvHooks_HooksGone", H.FindTextContaining("dpi=") is null);
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  ChildrenStrategy — TreeChildren reconcile for the untyped TreeView:
    //  reorder, child add/remove, and a brand-new node across a re-render.
    // ════════════════════════════════════════════════════════════════════
    internal class UntypedTreeViewReconcile(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var nodes = phase == 0
                    ? new[]
                    {
                        TreeNode("A", TreeNode("A1"), TreeNode("A2")),
                        TreeNode("B"),
                    }
                    : new[]
                    {
                        // Reorder (B before A), mutate A's children (drop A1, add A3,
                        // keep A2), and append a brand-new C node.
                        TreeNode("B"),
                        TreeNode("A", TreeNode("A2"), TreeNode("A3")),
                        TreeNode("C"),
                    };
                return VStack(Button("TreeGo", () => set(1)), TreeView(nodes));
            });

            await Harness.Render();
            var tv0 = H.FindControl<TreeView>(_ => true);
            H.Check("TreeView_Mounted", tv0 is not null);
            H.Check("TreeView_Initial2Roots", tv0?.RootNodes.Count == 2);

            H.ClickButton("TreeGo");
            await Harness.Render();

            var tv1 = H.FindControl<TreeView>(_ => true);
            H.Check("TreeView_Reused", SameInstance(tv0, tv1));
            H.Check("TreeView_Grew3Roots", tv1?.RootNodes.Count == 3);
            // After reorder, first root is now "B".
            H.Check("TreeView_ReorderedFirstIsB",
                tv1?.RootNodes.Count == 3 && NodeContentString(tv1?.RootNodes[0]) == "B");
            // "A" moved to index 1 and its children were diffed to exactly [A2, A3]
            // (A1 dropped, A3 added, A2 kept) — a bare count check would also pass
            // for a stale [A1, A2], so assert the actual contents/order.
            var aNode = tv1?.RootNodes.Count == 3 ? tv1?.RootNodes[1] : null;
            H.Check("TreeView_AChildrenDiffedExact",
                aNode is not null
                && aNode.Children.Count == 2
                && NodeContentString(aNode.Children[0]) == "A2"
                && NodeContentString(aNode.Children[1]) == "A3");
            // Brand-new "C" root appended.
            H.Check("TreeView_NewRootAppended",
                tv1?.RootNodes.Count == 3 && NodeContentString(tv1?.RootNodes[2]) == "C");
        }

        private static string? NodeContentString(TreeViewNode? node)
        {
            return node?.Content switch
            {
                string s => s,
                TreeViewNodeData d => d.Content,
                _ => node?.Content?.ToString(),
            };
        }
    }
}
