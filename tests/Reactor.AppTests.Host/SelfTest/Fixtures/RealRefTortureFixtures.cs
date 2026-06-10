using System.Threading;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using WinPrim = Microsoft.UI.Xaml.Controls.Primitives;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 057 Phase 2 capstone: heterogeneous, shipping WinUI reference DPs under
/// topology and churn stress. Assertions read live DP/UIA values off realized controls.
/// </summary>
internal static class RealRefTortureFixtures
{
    private static WinUI.Button? FindButton(Harness h, string content) =>
        h.FindControl<WinUI.Button>(b => b.Content is string s && s == content);

    private static WinUI.TextBlock? FindTextBlock(Harness h, string text) =>
        h.FindControl<WinUI.TextBlock>(tb => tb.Text == text);

    private static WinUI.TeachingTip? FindTip(Harness h, string name) =>
        h.FindControl<WinUI.TeachingTip>(t => t.Name == name);

    private static WinPrim.Popup? FindPopup(Harness h, string name) =>
        h.FindControl<WinPrim.Popup>(p => p.Name == name);

    private static bool MissingButton(Harness h, string content) => FindButton(h, content) is null;

    private static bool TipTargets(Harness h, string tipName, string targetContent)
    {
        var tip = FindTip(h, tipName);
        var target = FindButton(h, targetContent);
        return tip is not null && target is not null && ReferenceEquals(tip.Target, target);
    }

    private static bool TipTargetNull(Harness h, string tipName) =>
        FindTip(h, tipName) is { Target: null };

    private static bool PopupTargets(Harness h, string popupName, string targetContent)
    {
        var popup = FindPopup(h, popupName);
        var target = FindButton(h, targetContent);
        return popup is not null && target is not null && ReferenceEquals(popup.PlacementTarget, target);
    }

    private static bool PopupTargetNull(Harness h, string popupName) =>
        FindPopup(h, popupName) is { PlacementTarget: null };

    private static bool TextBlockTargets(Harness h, ElementRef<FrameworkElement>? reference, string text)
    {
        var textBlock = FindTextBlock(h, text);
        return textBlock is not null && ReferenceEquals(reference?.Current, textBlock);
    }

    private static bool LabeledBy(Harness h, string inputContent, string labelContent)
    {
        var input = FindButton(h, inputContent);
        var label = FindButton(h, labelContent);
        return input is not null && label is not null &&
            ReferenceEquals(AutomationProperties.GetLabeledBy(input), label);
    }

    private static bool LabeledByNull(Harness h, string inputContent) =>
        FindButton(h, inputContent) is { } input &&
        AutomationProperties.GetLabeledBy(input) is null;

    private static bool ListMatches(IList<DependencyObject>? list, params FrameworkElement?[] targets)
    {
        if (list is null || list.Count != targets.Length) return false;
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] is null || !ReferenceEquals(list[i], targets[i]))
                return false;
        }
        return true;
    }

    private static bool DescribedBy(Harness h, string inputContent, params string[] targetContents)
    {
        var input = FindButton(h, inputContent);
        var targets = targetContents.Select(c => (FrameworkElement?)FindButton(h, c)).ToArray();
        return input is not null && ListMatches(AutomationProperties.GetDescribedBy(input), targets);
    }

    private static bool FlowsTo(Harness h, string inputContent, params string[] targetContents)
    {
        var input = FindButton(h, inputContent);
        var targets = targetContents.Select(c => (FrameworkElement?)FindButton(h, c)).ToArray();
        return input is not null && ListMatches(AutomationProperties.GetFlowsTo(input), targets);
    }

    private static bool FlowsFrom(Harness h, string inputContent, params string[] targetContents)
    {
        var input = FindButton(h, inputContent);
        var targets = targetContents.Select(c => (FrameworkElement?)FindButton(h, c)).ToArray();
        return input is not null && ListMatches(AutomationProperties.GetFlowsFrom(input), targets);
    }

    private static bool XYRight(Harness h, string fromContent, string toContent)
    {
        var from = FindButton(h, fromContent);
        var to = FindButton(h, toContent);
        return from is not null && to is not null && ReferenceEquals(from.XYFocusRight, to);
    }

    private static bool XYLeft(Harness h, string fromContent, string toContent)
    {
        var from = FindButton(h, fromContent);
        var to = FindButton(h, toContent);
        return from is not null && to is not null && ReferenceEquals(from.XYFocusLeft, to);
    }

    private static bool XYRightNull(Harness h, string fromContent) =>
        FindButton(h, fromContent) is { XYFocusRight: null };

    private static bool XYLeftNull(Harness h, string fromContent) =>
        FindButton(h, fromContent) is { XYFocusLeft: null };

    internal sealed class TeachingTipTargetCrossSubtree(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? targetRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                targetRef = ctx.UseElementRef<FrameworkElement>();
                var (showTarget, setShowTarget) = ctx.UseState(true);
                return VStack(
                    HStack(showTarget ? Button("RR_Tip_Target", () => { }).Ref(targetRef) with { Key = "RR_Tip_Target" } : Empty()),
                    HStack(TeachingTip("RR_Tip", "cross-subtree", target: targetRef)
                        .Set(t => t.Name = "RR_Tip")
                        with { IsOpen = true, Key = "RR_Tip" }),
                    Button("RR_Tip_ToggleTarget", () => setShowTarget(!showTarget)));
            });

            await Harness.Render();
            H.Check("RealRef_TeachingTipTarget_CrossSubtree_CommitGlitchFree", await Harness.WaitFor(() =>
                TipTargets(H, "RR_Tip", "RR_Tip_Target") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 1));

            H.ClickButton("RR_Tip_ToggleTarget");
            await Harness.Render();
            H.Check("RealRef_TeachingTipTarget_CrossSubtree_TargetUnmountClears", await Harness.WaitFor(() =>
                MissingButton(H, "RR_Tip_Target") &&
                TipTargetNull(H, "RR_Tip") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 1));
        }
    }

    internal sealed class XYFocusRing(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? aRef = null, bRef = null, cRef = null, dRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                aRef = ctx.UseElementRef<FrameworkElement>();
                bRef = ctx.UseElementRef<FrameworkElement>();
                cRef = ctx.UseElementRef<FrameworkElement>();
                dRef = ctx.UseElementRef<FrameworkElement>();
                var (links, setLinks) = ctx.UseState(true);
                var (showC, setShowC) = ctx.UseState(true);
                var a = Button("RR_XY_A", () => { }).Ref(aRef) with { Key = "RR_XY_A" };
                var b = Button("RR_XY_B", () => { }).Ref(bRef) with { Key = "RR_XY_B" };
                var c = Button("RR_XY_C", () => { }).Ref(cRef) with { Key = "RR_XY_C" };
                var d = Button("RR_XY_D", () => { }).Ref(dRef) with { Key = "RR_XY_D" };
                if (links)
                {
                    a = a.XYFocusRight(bRef).XYFocusLeft(dRef);
                    b = b.XYFocusRight(cRef).XYFocusLeft(aRef);
                    c = c.XYFocusRight(dRef).XYFocusLeft(bRef);
                    d = d.XYFocusRight(aRef).XYFocusLeft(cRef);
                }

                return VStack(
                    a,
                    b,
                    showC ? c : Empty(),
                    d,
                    Button("RR_XY_ToggleC", () => setShowC(!showC)),
                    Button("RR_XY_ToggleLinks", () => setLinks(!links)));
            });

            await Harness.Render();
            H.Check("RealRef_XYFocusRing_CommitCycleConverges", await Harness.WaitFor(() =>
                XYRight(H, "RR_XY_A", "RR_XY_B") &&
                XYLeft(H, "RR_XY_A", "RR_XY_D") &&
                XYRight(H, "RR_XY_B", "RR_XY_C") &&
                XYLeft(H, "RR_XY_B", "RR_XY_A") &&
                XYRight(H, "RR_XY_C", "RR_XY_D") &&
                XYLeft(H, "RR_XY_C", "RR_XY_B") &&
                XYRight(H, "RR_XY_D", "RR_XY_A") &&
                XYLeft(H, "RR_XY_D", "RR_XY_C") &&
                aRef?.Inner.CurrentChangedSubscriberCount == 2 &&
                bRef?.Inner.CurrentChangedSubscriberCount == 2 &&
                cRef?.Inner.CurrentChangedSubscriberCount == 2 &&
                dRef?.Inner.CurrentChangedSubscriberCount == 2));

            H.ClickButton("RR_XY_ToggleC");
            await Harness.Render();
            H.Check("RealRef_XYFocusRing_TargetUnmountClears", await Harness.WaitFor(() =>
                MissingButton(H, "RR_XY_C") &&
                XYRightNull(H, "RR_XY_B") &&
                XYLeftNull(H, "RR_XY_D") &&
                cRef?.Inner.CurrentChangedSubscriberCount == 2));

            H.ClickButton("RR_XY_ToggleLinks");
            await Harness.Render();
            H.Check("RealRef_XYFocusRing_ModifierRemovalDropsSubscribers", await Harness.WaitFor(() =>
                XYRightNull(H, "RR_XY_A") &&
                XYLeftNull(H, "RR_XY_A") &&
                XYRightNull(H, "RR_XY_B") &&
                XYLeftNull(H, "RR_XY_B") &&
                XYRightNull(H, "RR_XY_D") &&
                XYLeftNull(H, "RR_XY_D") &&
                bRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                cRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                dRef?.Inner.CurrentChangedSubscriberCount == 0));
        }
    }

    internal sealed class AutomationRelationships(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? labelRef = null, d1Ref = null, d2Ref = null;
            ElementRef<FrameworkElement>? to1Ref = null, to2Ref = null, from1Ref = null, from2Ref = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                labelRef = ctx.UseElementRef<FrameworkElement>();
                d1Ref = ctx.UseElementRef<FrameworkElement>();
                d2Ref = ctx.UseElementRef<FrameworkElement>();
                to1Ref = ctx.UseElementRef<FrameworkElement>();
                to2Ref = ctx.UseElementRef<FrameworkElement>();
                from1Ref = ctx.UseElementRef<FrameworkElement>();
                from2Ref = ctx.UseElementRef<FrameworkElement>();
                var (showLabel, setShowLabel) = ctx.UseState(true);
                var (showD2, setShowD2) = ctx.UseState(true);
                var (showInput, setShowInput) = ctx.UseState(true);
                var (links, setLinks) = ctx.UseState(true);
                Element input = Button("RR_A11y_Input", () => { });
                if (links)
                {
                    input = input
                        .LabeledBy(labelRef)
                        .DescribedBy(d1Ref, d2Ref)
                        .FlowsTo(to1Ref, to2Ref)
                        .FlowsFrom(from1Ref, from2Ref);
                }

                return VStack(
                    showLabel ? Button("RR_A11y_Label", () => { }).Ref(labelRef) with { Key = "RR_A11y_Label" } : Empty(),
                    Button("RR_A11y_D1", () => { }).Ref(d1Ref) with { Key = "RR_A11y_D1" },
                    showD2 ? Button("RR_A11y_D2", () => { }).Ref(d2Ref) with { Key = "RR_A11y_D2" } : Empty(),
                    Button("RR_A11y_To1", () => { }).Ref(to1Ref) with { Key = "RR_A11y_To1" },
                    Button("RR_A11y_To2", () => { }).Ref(to2Ref) with { Key = "RR_A11y_To2" },
                    Button("RR_A11y_From1", () => { }).Ref(from1Ref) with { Key = "RR_A11y_From1" },
                    Button("RR_A11y_From2", () => { }).Ref(from2Ref) with { Key = "RR_A11y_From2" },
                    showInput
                        ? input with { Key = "RR_A11y_Input" }
                        : Empty(),
                    Button("RR_A11y_ToggleLinks", () => setLinks(!links)),
                    Button("RR_A11y_ToggleLabel", () => setShowLabel(!showLabel)),
                    Button("RR_A11y_ToggleD2", () => setShowD2(!showD2)),
                    Button("RR_A11y_ToggleInput", () => setShowInput(!showInput)));
            });

            await Harness.Render();
            H.Check("RealRef_AutomationRelationships_CommitOrder", await Harness.WaitFor(() =>
                LabeledBy(H, "RR_A11y_Input", "RR_A11y_Label") &&
                DescribedBy(H, "RR_A11y_Input", "RR_A11y_D1", "RR_A11y_D2") &&
                FlowsTo(H, "RR_A11y_Input", "RR_A11y_To1", "RR_A11y_To2") &&
                FlowsFrom(H, "RR_A11y_Input", "RR_A11y_From1", "RR_A11y_From2") &&
                labelRef?.Inner.CurrentChangedSubscriberCount == 1 &&
                d1Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                d2Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                to1Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                to2Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                from1Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                from2Ref?.Inner.CurrentChangedSubscriberCount == 1));

            H.ClickButton("RR_A11y_ToggleLinks");
            await Harness.Render();
            H.Check("RealRef_AutomationRelationships_ModifierRemovalClearsPropertiesAndLists", await Harness.WaitFor(() =>
                LabeledByNull(H, "RR_A11y_Input") &&
                DescribedBy(H, "RR_A11y_Input") &&
                FlowsTo(H, "RR_A11y_Input") &&
                FlowsFrom(H, "RR_A11y_Input") &&
                labelRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                d1Ref?.Inner.CurrentChangedSubscriberCount == 0 &&
                d2Ref?.Inner.CurrentChangedSubscriberCount == 0 &&
                to1Ref?.Inner.CurrentChangedSubscriberCount == 0 &&
                to2Ref?.Inner.CurrentChangedSubscriberCount == 0 &&
                from1Ref?.Inner.CurrentChangedSubscriberCount == 0 &&
                from2Ref?.Inner.CurrentChangedSubscriberCount == 0));

            H.ClickButton("RR_A11y_ToggleLinks");
            await Harness.Render();
            H.Check("RealRef_AutomationRelationships_ModifierRestoreRebinds", await Harness.WaitFor(() =>
                LabeledBy(H, "RR_A11y_Input", "RR_A11y_Label") &&
                DescribedBy(H, "RR_A11y_Input", "RR_A11y_D1", "RR_A11y_D2") &&
                FlowsTo(H, "RR_A11y_Input", "RR_A11y_To1", "RR_A11y_To2") &&
                FlowsFrom(H, "RR_A11y_Input", "RR_A11y_From1", "RR_A11y_From2") &&
                labelRef?.Inner.CurrentChangedSubscriberCount == 1 &&
                d1Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                d2Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                to1Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                to2Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                from1Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                from2Ref?.Inner.CurrentChangedSubscriberCount == 1));

            H.ClickButton("RR_A11y_ToggleD2");
            await Harness.Render();
            H.Check("RealRef_AutomationRelationships_ListTargetUnmountDropsAndKeepsOrder", await Harness.WaitFor(() =>
                MissingButton(H, "RR_A11y_D2") &&
                DescribedBy(H, "RR_A11y_Input", "RR_A11y_D1") &&
                d2Ref?.Inner.CurrentChangedSubscriberCount == 1));

            H.ClickButton("RR_A11y_ToggleLabel");
            await Harness.Render();
            H.Check("RealRef_AutomationRelationships_ScalarTargetUnmountClears", await Harness.WaitFor(() =>
                MissingButton(H, "RR_A11y_Label") &&
                LabeledByNull(H, "RR_A11y_Input") &&
                labelRef?.Inner.CurrentChangedSubscriberCount == 1));

            H.ClickButton("RR_A11y_ToggleInput");
            await Harness.Render();
            H.Check("RealRef_AutomationRelationships_ReferrerUnmountDropsAllSubscribers", await Harness.WaitFor(() =>
                MissingButton(H, "RR_A11y_Input") &&
                labelRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                d1Ref?.Inner.CurrentChangedSubscriberCount == 0 &&
                d2Ref?.Inner.CurrentChangedSubscriberCount == 0 &&
                to1Ref?.Inner.CurrentChangedSubscriberCount == 0 &&
                to2Ref?.Inner.CurrentChangedSubscriberCount == 0 &&
                from1Ref?.Inner.CurrentChangedSubscriberCount == 0 &&
                from2Ref?.Inner.CurrentChangedSubscriberCount == 0));
        }
    }

    internal sealed class PlacementTarget(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? targetRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                targetRef = ctx.UseElementRef<FrameworkElement>();
                var (showTarget, setShowTarget) = ctx.UseState(true);
                return VStack(
                    showTarget ? Button("RR_Popup_Target", () => { }).Ref(targetRef) with { Key = "RR_Popup_Target" } : Empty(),
                    PopupPlacementProbeFactory.Of("RR_Popup", targetRef) with { Key = "RR_Popup" },
                    Button("RR_Popup_ToggleTarget", () => setShowTarget(!showTarget)));
            });

            await Harness.Render();
            H.Check("RealRef_PlacementTarget_PopupPlacementTargetCommit", await Harness.WaitFor(() =>
                PopupTargets(H, "RR_Popup", "RR_Popup_Target") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 1));

            H.ClickButton("RR_Popup_ToggleTarget");
            await Harness.Render();
            H.Check("RealRef_PlacementTarget_TargetUnmountClears", await Harness.WaitFor(() =>
                MissingButton(H, "RR_Popup_Target") &&
                PopupTargetNull(H, "RR_Popup") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 1));
        }
    }

    internal sealed class ImperativeRefLifecycle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? firstRef = null, secondRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                firstRef = ctx.UseElementRef<FrameworkElement>();
                secondRef = ctx.UseElementRef<FrameworkElement>();
                var (phase, setPhase) = ctx.UseState(0);
                Element target = phase switch
                {
                    0 => TextBlock("RR_Ref_Target").Ref(firstRef),
                    1 => TextBlock("RR_Ref_Target").Ref(secondRef),
                    2 => TextBlock("RR_Ref_Target"),
                    3 => TextBlock("RR_Ref_Target").Ref(firstRef),
                    4 => Empty(),
                    _ => TextBlock("RR_Ref_Reused"),
                };

                return VStack(
                    target,
                    Button("RR_Ref_Advance", () => setPhase(phase + 1)));
            });

            await Harness.Render();
            H.Check("RealRef_ImperativeRefLifecycle_InitialRefOnlyMount", await Harness.WaitFor(() =>
                TextBlockTargets(H, firstRef, "RR_Ref_Target") &&
                secondRef?.Current is null));

            H.ClickButton("RR_Ref_Advance");
            await Harness.Render();
            H.Check("RealRef_ImperativeRefLifecycle_SwapClearsOldRef", await Harness.WaitFor(() =>
                firstRef?.Current is null &&
                TextBlockTargets(H, secondRef, "RR_Ref_Target")));

            H.ClickButton("RR_Ref_Advance");
            await Harness.Render();
            H.Check("RealRef_ImperativeRefLifecycle_RemovalClearsRef", await Harness.WaitFor(() =>
                FindTextBlock(H, "RR_Ref_Target") is not null &&
                firstRef?.Current is null &&
                secondRef?.Current is null));

            H.ClickButton("RR_Ref_Advance");
            await Harness.Render();
            H.Check("RealRef_ImperativeRefLifecycle_ReAddWorksAfterRemoval", await Harness.WaitFor(() =>
                TextBlockTargets(H, firstRef, "RR_Ref_Target") &&
                secondRef?.Current is null));

            H.ClickButton("RR_Ref_Advance");
            await Harness.Render();
            H.Check("RealRef_ImperativeRefLifecycle_RefOnlyUnmountClears", await Harness.WaitFor(() =>
                FindTextBlock(H, "RR_Ref_Target") is null &&
                firstRef?.Current is null &&
                secondRef?.Current is null));

            H.ClickButton("RR_Ref_Advance");
            await Harness.Render();
            H.Check("RealRef_ImperativeRefLifecycle_PoolReuseDoesNotRestoreOldRef", await Harness.WaitFor(() =>
                FindTextBlock(H, "RR_Ref_Reused") is not null &&
                firstRef?.Current is null &&
                secondRef?.Current is null));
        }
    }

    internal sealed class KeyedReorder(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? targetRef = null, labelRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                targetRef = ctx.UseElementRef<FrameworkElement>();
                labelRef = ctx.UseElementRef<FrameworkElement>();
                var (reversed, setReversed) = ctx.UseState(false);
                Element target = Button("RR_Reorder_Target", () => { }).Ref(targetRef) with { Key = "RR_Reorder_Target" };
                Element label = Button("RR_Reorder_Label", () => { }).Ref(labelRef) with { Key = "RR_Reorder_Label" };
                Element focus = Button("RR_Reorder_Focus", () => { }).XYFocusRight(targetRef) with { Key = "RR_Reorder_Focus" };
                Element input = Button("RR_Reorder_Input", () => { }).LabeledBy(labelRef) with { Key = "RR_Reorder_Input" };
                Element desc = Button("RR_Reorder_Desc", () => { }).DescribedBy(labelRef, targetRef) with { Key = "RR_Reorder_Desc" };
                Element tip = TeachingTip("RR_Reorder_Tip", "keyed", target: targetRef)
                    .Set(t => t.Name = "RR_Reorder_Tip")
                    with { IsOpen = true, Key = "RR_Reorder_Tip" };
                Element popup = PopupPlacementProbeFactory.Of("RR_Reorder_Popup", targetRef) with { Key = "RR_Reorder_Popup" };
                return reversed
                    ? VStack(target, label, desc, input, focus, tip, popup, Button("RR_Reorder_Shuffle", () => setReversed(!reversed)))
                    : VStack(target, label, focus, input, desc, tip, popup, Button("RR_Reorder_Shuffle", () => setReversed(!reversed)));
            });

            await Harness.Render();
            H.Check("RealRef_KeyedReorder_Commit", await Harness.WaitFor(() =>
                XYRight(H, "RR_Reorder_Focus", "RR_Reorder_Target") &&
                LabeledBy(H, "RR_Reorder_Input", "RR_Reorder_Label") &&
                DescribedBy(H, "RR_Reorder_Desc", "RR_Reorder_Label", "RR_Reorder_Target") &&
                TipTargets(H, "RR_Reorder_Tip", "RR_Reorder_Target") &&
                PopupTargets(H, "RR_Reorder_Popup", "RR_Reorder_Target") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 4 &&
                labelRef?.Inner.CurrentChangedSubscriberCount == 2));

            H.ClickButton("RR_Reorder_Shuffle");
            await Harness.Render();
            H.Check("RealRef_KeyedReorder_AfterShuffle_Focus", await Harness.WaitFor(() =>
                XYRight(H, "RR_Reorder_Focus", "RR_Reorder_Target")));
            H.Check("RealRef_KeyedReorder_AfterShuffle_A11y", await Harness.WaitFor(() =>
                LabeledBy(H, "RR_Reorder_Input", "RR_Reorder_Label") &&
                DescribedBy(H, "RR_Reorder_Desc", "RR_Reorder_Label", "RR_Reorder_Target")));
            H.Check("RealRef_KeyedReorder_AfterShuffle_Tip", await Harness.WaitFor(() =>
                TipTargets(H, "RR_Reorder_Tip", "RR_Reorder_Target")));
            H.Check("RealRef_KeyedReorder_AfterShuffle_Popup", await Harness.WaitFor(() =>
                PopupTargets(H, "RR_Reorder_Popup", "RR_Reorder_Target")));
            H.Check("RealRef_KeyedReorder_AfterShuffle_Counts", await Harness.WaitFor(() =>
                targetRef?.Inner.CurrentChangedSubscriberCount == 4 &&
                labelRef?.Inner.CurrentChangedSubscriberCount == 2));
            H.Check("RealRef_KeyedReorder_AfterShuffleRepoints", await Harness.WaitFor(() =>
                XYRight(H, "RR_Reorder_Focus", "RR_Reorder_Target") &&
                LabeledBy(H, "RR_Reorder_Input", "RR_Reorder_Label") &&
                DescribedBy(H, "RR_Reorder_Desc", "RR_Reorder_Label", "RR_Reorder_Target") &&
                TipTargets(H, "RR_Reorder_Tip", "RR_Reorder_Target") &&
                PopupTargets(H, "RR_Reorder_Popup", "RR_Reorder_Target") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 4 &&
                labelRef?.Inner.CurrentChangedSubscriberCount == 2));
        }
    }

    internal sealed class PoolRecycle(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? targetRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                targetRef = ctx.UseElementRef<FrameworkElement>();
                var (cycle, setCycle) = ctx.UseState(0);
                var showExtra = cycle % 2 == 0;
                // True virtualizing scroll recycle is low-signal in the headless host;
                // mirror RefNode row 12 with keyed add/remove to force rent/return.
                return VStack(
                    Button($"RR_Pool_Cycle_{cycle}", () => { }),
                    Button("RR_Pool_Target", () => { }).Ref(targetRef) with { Key = "RR_Pool_Target" },
                    Button("RR_Pool_Root", () => { }).LabeledBy(targetRef) with { Key = "RR_Pool_Root" },
                    showExtra ? Button("RR_Pool_XY_1", () => { }).XYFocusRight(targetRef) with { Key = "RR_Pool_XY_1" } : Empty(),
                    showExtra ? Button("RR_Pool_XY_2", () => { }).XYFocusRight(targetRef) with { Key = "RR_Pool_XY_2" } : Empty(),
                    showExtra ? Button("RR_Pool_Label_1", () => { }).LabeledBy(targetRef) with { Key = "RR_Pool_Label_1" } : Empty(),
                    showExtra ? Button("RR_Pool_Label_2", () => { }).LabeledBy(targetRef) with { Key = "RR_Pool_Label_2" } : Empty(),
                    showExtra ? TeachingTip("RR_Pool_Tip_1", "pool", target: targetRef).Set(t => t.Name = "RR_Pool_Tip_1") with { IsOpen = true, Key = "RR_Pool_Tip_1" } : Empty(),
                    showExtra ? TeachingTip("RR_Pool_Tip_2", "pool", target: targetRef).Set(t => t.Name = "RR_Pool_Tip_2") with { IsOpen = true, Key = "RR_Pool_Tip_2" } : Empty(),
                    showExtra ? PopupPlacementProbeFactory.Of("RR_Pool_Popup_1", targetRef) with { Key = "RR_Pool_Popup_1" } : Empty(),
                    showExtra ? PopupPlacementProbeFactory.Of("RR_Pool_Popup_2", targetRef) with { Key = "RR_Pool_Popup_2" } : Empty(),
                    Button("RR_Pool_Cycle", () => setCycle(cycle + 1)));
            });

            await Harness.Render();
            H.Check("RealRef_PoolRecycle_CommitBoundedSubscribers", await Harness.WaitFor(() =>
                LabeledBy(H, "RR_Pool_Root", "RR_Pool_Target") &&
                XYRight(H, "RR_Pool_XY_1", "RR_Pool_Target") &&
                TipTargets(H, "RR_Pool_Tip_1", "RR_Pool_Target") &&
                PopupTargets(H, "RR_Pool_Popup_1", "RR_Pool_Target") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 9));

            H.ClickButton("RR_Pool_Cycle");
            await Harness.Render();
            H.Check("RealRef_PoolRecycle_CycleRemovesReferrers", await Harness.WaitFor(() =>
                MissingButton(H, "RR_Pool_XY_1") &&
                LabeledBy(H, "RR_Pool_Root", "RR_Pool_Target") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 1));

            H.ClickButton("RR_Pool_Cycle");
            await Harness.Render();
            H.Check("RealRef_PoolRecycle_CycleRestoresWithoutDoubleSubscribe", await Harness.WaitFor(() =>
                XYRight(H, "RR_Pool_XY_2", "RR_Pool_Target") &&
                TipTargets(H, "RR_Pool_Tip_2", "RR_Pool_Target") &&
                PopupTargets(H, "RR_Pool_Popup_2", "RR_Pool_Target") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 9));
        }
    }

    internal sealed class ConditionalRemount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? targetRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                targetRef = ctx.UseElementRef<FrameworkElement>();
                var (showTarget, setShowTarget) = ctx.UseState(true);
                return VStack(
                    showTarget ? Button("RR_Conditional_Target", () => { }).Ref(targetRef) with { Key = "RR_Conditional_Target" } : Empty(),
                    Button("RR_Conditional_Focus", () => { }).XYFocusRight(targetRef) with { Key = "RR_Conditional_Focus" },
                    Button("RR_Conditional_Input", () => { }).LabeledBy(targetRef) with { Key = "RR_Conditional_Input" },
                    TeachingTip("RR_Conditional_Tip", "conditional", target: targetRef).Set(t => t.Name = "RR_Conditional_Tip") with { IsOpen = true, Key = "RR_Conditional_Tip" },
                    PopupPlacementProbeFactory.Of("RR_Conditional_Popup", targetRef) with { Key = "RR_Conditional_Popup" },
                    Button("RR_Conditional_ToggleTarget", () => setShowTarget(!showTarget)));
            });

            await Harness.Render();
            H.Check("RealRef_ConditionalRemount_Commit", await Harness.WaitFor(() =>
                XYRight(H, "RR_Conditional_Focus", "RR_Conditional_Target") &&
                LabeledBy(H, "RR_Conditional_Input", "RR_Conditional_Target") &&
                TipTargets(H, "RR_Conditional_Tip", "RR_Conditional_Target") &&
                PopupTargets(H, "RR_Conditional_Popup", "RR_Conditional_Target") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 4));

            H.ClickButton("RR_Conditional_ToggleTarget");
            await Harness.Render();
            H.Check("RealRef_ConditionalRemount_ToggleOutClears", await Harness.WaitFor(() =>
                MissingButton(H, "RR_Conditional_Target") &&
                XYRightNull(H, "RR_Conditional_Focus") &&
                LabeledByNull(H, "RR_Conditional_Input") &&
                TipTargetNull(H, "RR_Conditional_Tip") &&
                PopupTargetNull(H, "RR_Conditional_Popup") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 4));

            H.ClickButton("RR_Conditional_ToggleTarget");
            await Harness.Render();
            H.Check("RealRef_ConditionalRemount_ToggleInRelinks", await Harness.WaitFor(() =>
                XYRight(H, "RR_Conditional_Focus", "RR_Conditional_Target") &&
                LabeledBy(H, "RR_Conditional_Input", "RR_Conditional_Target") &&
                TipTargets(H, "RR_Conditional_Tip", "RR_Conditional_Target") &&
                PopupTargets(H, "RR_Conditional_Popup", "RR_Conditional_Target") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 4));
        }
    }

    internal sealed class LateAsyncTarget(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? targetRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                targetRef = ctx.UseElementRef<FrameworkElement>();
                var (showTarget, setShowTarget) = ctx.UseState(false);
                return VStack(
                    Button("RR_Late_Focus", () => { }).XYFocusRight(targetRef) with { Key = "RR_Late_Focus" },
                    Button("RR_Late_Input", () => { }).LabeledBy(targetRef) with { Key = "RR_Late_Input" },
                    TeachingTip("RR_Late_Tip", "late", target: targetRef).Set(t => t.Name = "RR_Late_Tip") with { IsOpen = true, Key = "RR_Late_Tip" },
                    PopupPlacementProbeFactory.Of("RR_Late_Popup", targetRef) with { Key = "RR_Late_Popup" },
                    showTarget ? Button("RR_Late_Target", () => { }).Ref(targetRef) with { Key = "RR_Late_Target" } : Empty(),
                    Button("RR_Late_ShowTarget", () => setShowTarget(true)));
            });

            await Harness.Render();
            H.Check("RealRef_LateAsyncTarget_InitiallyUnresolved", await Harness.WaitFor(() =>
                XYRightNull(H, "RR_Late_Focus") &&
                LabeledByNull(H, "RR_Late_Input") &&
                TipTargetNull(H, "RR_Late_Tip") &&
                PopupTargetNull(H, "RR_Late_Popup") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 4));

            H.ClickButton("RR_Late_ShowTarget");
            await Harness.Render();
            H.Check("RealRef_LateAsyncTarget_TargetMountPushFills", await Harness.WaitFor(() =>
                XYRight(H, "RR_Late_Focus", "RR_Late_Target") &&
                LabeledBy(H, "RR_Late_Input", "RR_Late_Target") &&
                TipTargets(H, "RR_Late_Tip", "RR_Late_Target") &&
                PopupTargets(H, "RR_Late_Popup", "RR_Late_Target") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 4));
        }
    }

    internal sealed class SourceSwap(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? bRef = null, cRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                bRef = ctx.UseElementRef<FrameworkElement>();
                cRef = ctx.UseElementRef<FrameworkElement>();
                var (useC, setUseC) = ctx.UseState(false);
                var (showC, setShowC) = ctx.UseState(true);
                return VStack(
                    Button("RR_Swap_A", () => { }).XYFocusRight(useC ? cRef : bRef) with { Key = "RR_Swap_A" },
                    Button("RR_Swap_B", () => { }).Ref(bRef) with { Key = "RR_Swap_B" },
                    showC ? Button("RR_Swap_C", () => { }).Ref(cRef) with { Key = "RR_Swap_C" } : Empty(),
                    Button("RR_Swap_Source", () => setUseC(!useC)),
                    Button("RR_Swap_ToggleC", () => setShowC(!showC)));
            });

            await Harness.Render();
            H.Check("RealRef_SourceSwap_InitialB", await Harness.WaitFor(() =>
                XYRight(H, "RR_Swap_A", "RR_Swap_B") &&
                bRef?.Inner.CurrentChangedSubscriberCount == 1 &&
                cRef?.Inner.CurrentChangedSubscriberCount == 0));

            H.ClickButton("RR_Swap_Source");
            await Harness.Render();
            H.Check("RealRef_SourceSwap_MovesSubscriptionToC", await Harness.WaitFor(() =>
                XYRight(H, "RR_Swap_A", "RR_Swap_C") &&
                bRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                cRef?.Inner.CurrentChangedSubscriberCount == 1));

            H.ClickButton("RR_Swap_ToggleC");
            await Harness.Render();
            H.Check("RealRef_SourceSwap_NewTargetUnmountClears", await Harness.WaitFor(() =>
                MissingButton(H, "RR_Swap_C") &&
                XYRightNull(H, "RR_Swap_A") &&
                bRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                cRef?.Inner.CurrentChangedSubscriberCount == 1));
        }
    }

    internal sealed class LeakBaseline(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? targetRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                targetRef = ctx.UseElementRef<FrameworkElement>();
                var (showTarget, setShowTarget) = ctx.UseState(true);
                var (showReferrers, setShowReferrers) = ctx.UseState(true);
                return VStack(
                    showTarget ? Button("RR_Leak_Target", () => { }).Ref(targetRef) with { Key = "RR_Leak_Target" } : Empty(),
                    showReferrers ? Button("RR_Leak_Focus", () => { }).XYFocusRight(targetRef) with { Key = "RR_Leak_Focus" } : Empty(),
                    showReferrers ? Button("RR_Leak_Input", () => { }).LabeledBy(targetRef) with { Key = "RR_Leak_Input" } : Empty(),
                    showReferrers ? TeachingTip("RR_Leak_Tip", "leak", target: targetRef).Set(t => t.Name = "RR_Leak_Tip") with { IsOpen = true, Key = "RR_Leak_Tip" } : Empty(),
                    showReferrers ? PopupPlacementProbeFactory.Of("RR_Leak_Popup", targetRef) with { Key = "RR_Leak_Popup" } : Empty(),
                    Button("RR_Leak_ToggleReferrers", () => setShowReferrers(!showReferrers)),
                    Button("RR_Leak_ToggleTarget", () => setShowTarget(!showTarget)));
            });

            await Harness.Render();
            H.Check("RealRef_LeakBaseline_Commit", await Harness.WaitFor(() =>
                targetRef?.Inner.CurrentChangedSubscriberCount == 4 &&
                XYRight(H, "RR_Leak_Focus", "RR_Leak_Target") &&
                LabeledBy(H, "RR_Leak_Input", "RR_Leak_Target") &&
                TipTargets(H, "RR_Leak_Tip", "RR_Leak_Target") &&
                PopupTargets(H, "RR_Leak_Popup", "RR_Leak_Target")));

            H.ClickButton("RR_Leak_ToggleReferrers");
            await Harness.Render();
            H.Check("RealRef_LeakBaseline_ReferrerTeardownReturnsToZero", await Harness.WaitFor(() =>
                targetRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                MissingButton(H, "RR_Leak_Focus") &&
                MissingButton(H, "RR_Leak_Input")));

            H.ClickButton("RR_Leak_ToggleReferrers");
            await Harness.Render();
            H.Check("RealRef_LeakBaseline_RestoreBaseline", await Harness.WaitFor(() =>
                targetRef?.Inner.CurrentChangedSubscriberCount == 4));

            H.ClickButton("RR_Leak_ToggleTarget");
            await Harness.Render();
            H.Check("RealRef_LeakBaseline_TargetTeardownKeepsLiveReferrerBaseline", await Harness.WaitFor(() =>
                MissingButton(H, "RR_Leak_Target") &&
                XYRightNull(H, "RR_Leak_Focus") &&
                LabeledByNull(H, "RR_Leak_Input") &&
                TipTargetNull(H, "RR_Leak_Tip") &&
                PopupTargetNull(H, "RR_Leak_Popup") &&
                targetRef?.Inner.CurrentChangedSubscriberCount == 4));

            H.ClickButton("RR_Leak_ToggleReferrers");
            await Harness.Render();
            H.Check("RealRef_LeakBaseline_AllTeardownReturnsToZero", await Harness.WaitFor(() =>
                targetRef?.Inner.CurrentChangedSubscriberCount == 0));
        }
    }

    internal sealed class EverythingAtOnce(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? targetRef = null, altRef = null, labelRef = null, d1Ref = null, d2Ref = null;
            ElementRef<FrameworkElement>? aRef = null, bRef = null, cRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                targetRef = ctx.UseElementRef<FrameworkElement>();
                altRef = ctx.UseElementRef<FrameworkElement>();
                labelRef = ctx.UseElementRef<FrameworkElement>();
                d1Ref = ctx.UseElementRef<FrameworkElement>();
                d2Ref = ctx.UseElementRef<FrameworkElement>();
                aRef = ctx.UseElementRef<FrameworkElement>();
                bRef = ctx.UseElementRef<FrameworkElement>();
                cRef = ctx.UseElementRef<FrameworkElement>();
                var (reversed, setReversed) = ctx.UseState(false);
                var (showExtras, setShowExtras) = ctx.UseState(true);
                var (showTarget, setShowTarget) = ctx.UseState(true);
                var (useAlt, setUseAlt) = ctx.UseState(false);
                var (showReferrers, setShowReferrers) = ctx.UseState(true);
                var anchor = useAlt ? altRef : targetRef;

                Element target = showTarget
                    ? Button("RR_All_Target", () => { }).Ref(targetRef) with { Key = "RR_All_Target" }
                    : Empty();
                Element alt = Button("RR_All_AltTarget", () => { }).Ref(altRef) with { Key = "RR_All_AltTarget" };
                Element label = Button("RR_All_Label", () => { }).Ref(labelRef) with { Key = "RR_All_Label" };
                Element d1 = Button("RR_All_D1", () => { }).Ref(d1Ref) with { Key = "RR_All_D1" };
                Element d2 = Button("RR_All_D2", () => { }).Ref(d2Ref) with { Key = "RR_All_D2" };
                Element xyA = Button("RR_All_XY_A", () => { }).Ref(aRef).XYFocusRight(bRef).XYFocusLeft(cRef) with { Key = "RR_All_XY_A" };
                Element xyB = Button("RR_All_XY_B", () => { }).Ref(bRef).XYFocusRight(cRef).XYFocusLeft(aRef) with { Key = "RR_All_XY_B" };
                Element xyC = Button("RR_All_XY_C", () => { }).Ref(cRef).XYFocusRight(aRef).XYFocusLeft(bRef) with { Key = "RR_All_XY_C" };
                Element input = showReferrers
                    ? Button("RR_All_Input", () => { }).LabeledBy(labelRef).DescribedBy(d1Ref, d2Ref) with { Key = "RR_All_Input" }
                    : Empty();
                Element tip = showReferrers
                    ? TeachingTip("RR_All_Tip", "everything", target: anchor).Set(t => t.Name = "RR_All_Tip") with { IsOpen = true, Key = "RR_All_Tip" }
                    : Empty();
                Element popup = showReferrers
                    ? PopupPlacementProbeFactory.Of("RR_All_Popup", anchor) with { Key = "RR_All_Popup" }
                    : Empty();
                Element extra1 = Button("RR_All_ExtraFocus", () => { }).XYFocusRight(anchor) with { Key = "RR_All_ExtraFocus" };
                Element extra2 = Button("RR_All_ExtraLabel", () => { }).LabeledBy(anchor) with { Key = "RR_All_ExtraLabel" };

                Element[] orderedReferrers = reversed
                    ? new[] { xyC, xyB, xyA }
                    : new[] { xyA, xyB, xyC };
                Element[] extras = showExtras && showReferrers
                    ? new[] { extra1, extra2 }
                    : [];
                return VStack(new Element[] { target, alt, label, d1, d2, input }
                    .Concat(orderedReferrers)
                    .Concat(extras)
                    .Concat(new Element[] { tip, popup })
                    .Concat(new Element[]
                {
                    Button("RR_All_Reorder", () => setReversed(!reversed)),
                    Button("RR_All_Recycle", () => setShowExtras(!showExtras)),
                    Button("RR_All_ToggleTarget", () => setShowTarget(!showTarget)),
                    Button("RR_All_SourceSwap", () => setUseAlt(!useAlt)),
                    Button("RR_All_UnmountReferrers", () => setShowReferrers(!showReferrers)),
                }).ToArray());
            });

            async Task CheckAll(string name, string? anchorContent, int targetSubscribers, int altSubscribers, bool extras, bool referrers)
            {
                H.Check(name, await Harness.WaitFor(() =>
                {
                    var anchorOk = !referrers ||
                        (anchorContent is null
                            ? TipTargetNull(H, "RR_All_Tip") && PopupTargetNull(H, "RR_All_Popup") && XYRightNull(H, "RR_All_ExtraFocus") && LabeledByNull(H, "RR_All_ExtraLabel")
                            : TipTargets(H, "RR_All_Tip", anchorContent) && PopupTargets(H, "RR_All_Popup", anchorContent) &&
                              (!extras || (XYRight(H, "RR_All_ExtraFocus", anchorContent) && LabeledBy(H, "RR_All_ExtraLabel", anchorContent))));
                    return (!referrers || (
                            LabeledBy(H, "RR_All_Input", "RR_All_Label") &&
                            DescribedBy(H, "RR_All_Input", "RR_All_D1", "RR_All_D2"))) &&
                        XYRight(H, "RR_All_XY_A", "RR_All_XY_B") &&
                        XYRight(H, "RR_All_XY_B", "RR_All_XY_C") &&
                        XYRight(H, "RR_All_XY_C", "RR_All_XY_A") &&
                        XYLeft(H, "RR_All_XY_A", "RR_All_XY_C") &&
                        XYLeft(H, "RR_All_XY_B", "RR_All_XY_A") &&
                        XYLeft(H, "RR_All_XY_C", "RR_All_XY_B") &&
                        anchorOk &&
                        targetRef?.Inner.CurrentChangedSubscriberCount == targetSubscribers &&
                        altRef?.Inner.CurrentChangedSubscriberCount == altSubscribers &&
                        labelRef?.Inner.CurrentChangedSubscriberCount == (referrers ? 1 : 0) &&
                        d1Ref?.Inner.CurrentChangedSubscriberCount == (referrers ? 1 : 0) &&
                        d2Ref?.Inner.CurrentChangedSubscriberCount == (referrers ? 1 : 0) &&
                        aRef?.Inner.CurrentChangedSubscriberCount == 2 &&
                        bRef?.Inner.CurrentChangedSubscriberCount == 2 &&
                        cRef?.Inner.CurrentChangedSubscriberCount == 2;
                }));
            }

            await Harness.Render();
            await CheckAll("RealRef_EverythingAtOnce_Commit", "RR_All_Target", 4, 0, extras: true, referrers: true);

            H.ClickButton("RR_All_Reorder");
            await Harness.Render();
            await CheckAll("RealRef_EverythingAtOnce_AfterReorder", "RR_All_Target", 4, 0, extras: true, referrers: true);

            H.ClickButton("RR_All_Recycle");
            await Harness.Render();
            await CheckAll("RealRef_EverythingAtOnce_AfterRecycleRemove", "RR_All_Target", 2, 0, extras: false, referrers: true);

            H.ClickButton("RR_All_Recycle");
            await Harness.Render();
            await CheckAll("RealRef_EverythingAtOnce_AfterRecycleRestore", "RR_All_Target", 4, 0, extras: true, referrers: true);

            H.ClickButton("RR_All_ToggleTarget");
            await Harness.Render();
            await CheckAll("RealRef_EverythingAtOnce_AfterConditionalTargetOut", null, 4, 0, extras: true, referrers: true);

            H.ClickButton("RR_All_SourceSwap");
            await Harness.Render();
            await CheckAll("RealRef_EverythingAtOnce_AfterSourceSwapToAlt", "RR_All_AltTarget", 0, 4, extras: true, referrers: true);

            H.ClickButton("RR_All_UnmountReferrers");
            await Harness.Render();
            await CheckAll("RealRef_EverythingAtOnce_AfterUnmountNoLeaks", "RR_All_AltTarget", 0, 0, extras: false, referrers: false);
        }
    }

    private sealed record PopupPlacementProbeElement(
        string ProbeName,
        ElementRef<FrameworkElement>? PlacementTarget) : Element
    {
        internal Action<WinPrim.Popup>[] Setters { get; init; } = [];
    }

    private static class PopupPlacementProbeFactory
    {
        private static int s_registered;

        private static void EnsureRegistered()
        {
            if (Interlocked.Exchange(ref s_registered, 1) == 1) return;
            ControlRegistry.Register<PopupPlacementProbeElement, WinPrim.Popup>(static () =>
                new DescriptorHandler<PopupPlacementProbeElement, WinPrim.Popup>(Descriptor));
        }

        public static PopupPlacementProbeElement Of(
            string name,
            ElementRef<FrameworkElement>? placementTarget)
        {
            EnsureRegistered();
            return new(name, placementTarget);
        }

        private static readonly ControlDescriptor<PopupPlacementProbeElement, WinPrim.Popup> Descriptor =
            new ControlDescriptor<PopupPlacementProbeElement, WinPrim.Popup>
            {
                GetSetters = static e => e.Setters,
            }
            .OneWay(
                get: static e => e.ProbeName,
                set: static (p, v) => p.Name = v)
            .OneWay(
                get: static _ => false,
                set: static (p, v) => p.IsOpen = v)
            .Reference<FrameworkElement>(
                get: static e => e.PlacementTarget,
                set: static (p, target) => p.PlacementTarget = target);
    }
}
