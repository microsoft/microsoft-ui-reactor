using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ModifierReferenceFixtures
{
    private static WinUI.Button? FindButton(Harness h, string content) =>
        h.FindControl<WinUI.Button>(b => b.Content is string s && s == content);

    internal sealed class XYFocusBidirectionalRing(Harness h) : SelfTestFixtureBase(h)
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
                var (showB, setShowB) = ctx.UseState(true);
                var (tick, setTick) = ctx.UseState(0);

                var a = Button("XY_A", () => { }).Ref(aRef) with { Key = "XY_A" };
                var b = Button("XY_B", () => { }).Ref(bRef) with { Key = "XY_B" };
                var c = Button("XY_C", () => { }).Ref(cRef) with { Key = "XY_C" };
                var d = Button("XY_D", () => { }).Ref(dRef) with { Key = "XY_D" };

                if (links)
                {
                    a = a.XYFocusRight(bRef).XYFocusLeft(dRef);
                    b = b.XYFocusRight(cRef).XYFocusLeft(aRef);
                    c = c.XYFocusRight(dRef).XYFocusLeft(bRef);
                    d = d.XYFocusRight(aRef).XYFocusLeft(cRef);
                }

                return VStack(
                    TextBlock($"xy {tick}"),
                    a,
                    showB ? b : Empty(),
                    c,
                    d,
                    Button("XY_Rerender", () => setTick(tick + 1)),
                    Button("XY_ToggleLinks", () => setLinks(!links)),
                    Button("XY_ToggleB", () => setShowB(!showB)));
            });

            await Harness.Render();
            H.Check("XYFocus_BidirectionalRing_Commit", await Harness.WaitFor(() =>
            {
                var a = FindButton(H, "XY_A");
                var b = FindButton(H, "XY_B");
                var c = FindButton(H, "XY_C");
                var d = FindButton(H, "XY_D");
                return a is not null && b is not null && c is not null && d is not null &&
                    ReferenceEquals(a.XYFocusRight, b) &&
                    ReferenceEquals(a.XYFocusLeft, d) &&
                    ReferenceEquals(b.XYFocusRight, c) &&
                    ReferenceEquals(b.XYFocusLeft, a) &&
                    ReferenceEquals(c.XYFocusRight, d) &&
                    ReferenceEquals(c.XYFocusLeft, b) &&
                    ReferenceEquals(d.XYFocusRight, a) &&
                    ReferenceEquals(d.XYFocusLeft, c);
            }));

            H.ClickButton("XY_Rerender");
            await Harness.Render();
            H.Check("XYFocus_BidirectionalRing_StableRerender", await Harness.WaitFor(() =>
            {
                var a = FindButton(H, "XY_A");
                var b = FindButton(H, "XY_B");
                var c = FindButton(H, "XY_C");
                var d = FindButton(H, "XY_D");
                return a is not null && b is not null && c is not null && d is not null &&
                    ReferenceEquals(a.XYFocusRight, b) &&
                    ReferenceEquals(c.XYFocusLeft, b);
            }));

            H.ClickButton("XY_ToggleB");
            await Harness.Render();
            H.Check("XYFocus_BidirectionalRing_TargetUnmountClears", await Harness.WaitFor(() =>
            {
                var a = FindButton(H, "XY_A");
                var b = FindButton(H, "XY_B");
                var c = FindButton(H, "XY_C");
                return a is not null && b is null && c is not null &&
                    a.XYFocusRight is null &&
                    c.XYFocusLeft is null &&
                    bRef?.Inner.CurrentChangedSubscriberCount == 2;
            }));

            H.ClickButton("XY_ToggleLinks");
            await Harness.Render();
            H.Check("XYFocus_BidirectionalRing_ModifierRemovalClearsAndUnsubscribes", await Harness.WaitFor(() =>
            {
                var a = FindButton(H, "XY_A");
                var c = FindButton(H, "XY_C");
                var d = FindButton(H, "XY_D");
                return a is not null && c is not null && d is not null &&
                    a.XYFocusLeft is null &&
                    c.XYFocusRight is null &&
                    d.XYFocusLeft is null &&
                    aRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                    bRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                    cRef?.Inner.CurrentChangedSubscriberCount == 0 &&
                    dRef?.Inner.CurrentChangedSubscriberCount == 0;
            }));
        }
    }

    internal sealed class AccessibilityLabeledByScalar(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? labelRef = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                labelRef = ctx.UseElementRef<FrameworkElement>();
                var (showInput, setShowInput) = ctx.UseState(true);
                var (showLabel, setShowLabel) = ctx.UseState(true);
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    TextBlock($"label {tick}"),
                    showLabel ? Button("A11y_Label_Target", () => { }).Ref(labelRef) with { Key = "A11y_Label_Target" } : Empty(),
                    showInput ? Button("A11y_Label_Input", () => { }).LabeledBy(labelRef) with { Key = "A11y_Label_Input" } : Empty(),
                    Button("A11y_Label_Rerender", () => setTick(tick + 1)),
                    Button("A11y_Label_ToggleInput", () => setShowInput(!showInput)),
                    Button("A11y_Label_ToggleLabel", () => setShowLabel(!showLabel)));
            });

            await Harness.Render();
            H.Check("Accessibility_LabeledBy_Scalar_Commit", await Harness.WaitFor(() =>
            {
                var input = FindButton(H, "A11y_Label_Input");
                var label = FindButton(H, "A11y_Label_Target");
                return input is not null && label is not null &&
                    ReferenceEquals(AutomationProperties.GetLabeledBy(input), label) &&
                    labelRef?.Inner.CurrentChangedSubscriberCount == 1;
            }));

            H.ClickButton("A11y_Label_Rerender");
            await Harness.Render();
            H.Check("Accessibility_LabeledBy_Scalar_StableRerender", await Harness.WaitFor(() =>
            {
                var input = FindButton(H, "A11y_Label_Input");
                var label = FindButton(H, "A11y_Label_Target");
                return input is not null && label is not null &&
                    ReferenceEquals(AutomationProperties.GetLabeledBy(input), label);
            }));

            H.ClickButton("A11y_Label_ToggleInput");
            await Harness.Render();
            H.Check("Accessibility_LabeledBy_Scalar_ReferrerUnmountDropsSubscription", await Harness.WaitFor(() =>
                FindButton(H, "A11y_Label_Input") is null &&
                FindButton(H, "A11y_Label_Target") is not null &&
                labelRef?.Inner.CurrentChangedSubscriberCount == 0));

            H.ClickButton("A11y_Label_ToggleInput");
            await Harness.Render();
            H.Check("Accessibility_LabeledBy_Scalar_SurvivesReferrerRecreation", await Harness.WaitFor(() =>
            {
                var input = FindButton(H, "A11y_Label_Input");
                var label = FindButton(H, "A11y_Label_Target");
                return input is not null && label is not null &&
                    ReferenceEquals(AutomationProperties.GetLabeledBy(input), label) &&
                    labelRef?.Inner.CurrentChangedSubscriberCount == 1;
            }));

            H.ClickButton("A11y_Label_ToggleLabel");
            await Harness.Render();
            H.Check("Accessibility_LabeledBy_Scalar_TargetUnmountClears", await Harness.WaitFor(() =>
            {
                var input = FindButton(H, "A11y_Label_Input");
                return input is not null &&
                    FindButton(H, "A11y_Label_Target") is null &&
                    AutomationProperties.GetLabeledBy(input) is null &&
                    labelRef?.Inner.CurrentChangedSubscriberCount == 1;
            }));
        }
    }

    internal sealed class AccessibilityDescribedByList(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ElementRef<FrameworkElement>? d1Ref = null, d2Ref = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                d1Ref = ctx.UseElementRef<FrameworkElement>();
                d2Ref = ctx.UseElementRef<FrameworkElement>();
                var (showInput, setShowInput) = ctx.UseState(true);
                var (showD2, setShowD2) = ctx.UseState(true);
                var (tick, setTick) = ctx.UseState(0);
                return VStack(
                    TextBlock($"desc {tick}"),
                    Button("A11y_Desc_One", () => { }).Ref(d1Ref) with { Key = "A11y_Desc_One" },
                    showD2 ? Button("A11y_Desc_Two", () => { }).Ref(d2Ref) with { Key = "A11y_Desc_Two" } : Empty(),
                    showInput ? Button("A11y_Desc_Input", () => { }).DescribedBy(d1Ref, d2Ref) with { Key = "A11y_Desc_Input" } : Empty(),
                    Button("A11y_Desc_Rerender", () => setTick(tick + 1)),
                    Button("A11y_Desc_ToggleInput", () => setShowInput(!showInput)),
                    Button("A11y_Desc_ToggleD2", () => setShowD2(!showD2)));
            });

            await Harness.Render();
            H.Check("Accessibility_DescribedBy_List_CommitOrder", await Harness.WaitFor(() =>
            {
                var input = FindButton(H, "A11y_Desc_Input");
                var d1 = FindButton(H, "A11y_Desc_One");
                var d2 = FindButton(H, "A11y_Desc_Two");
                var list = input is not null ? AutomationProperties.GetDescribedBy(input) : null;
                return list is not null && d1 is not null && d2 is not null &&
                    list.Count == 2 &&
                    ReferenceEquals(list[0], d1) &&
                    ReferenceEquals(list[1], d2) &&
                    d1Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                    d2Ref?.Inner.CurrentChangedSubscriberCount == 1;
            }));

            H.ClickButton("A11y_Desc_Rerender");
            await Harness.Render();
            H.Check("Accessibility_DescribedBy_List_StableRerender", await Harness.WaitFor(() =>
            {
                var input = FindButton(H, "A11y_Desc_Input");
                var d1 = FindButton(H, "A11y_Desc_One");
                var d2 = FindButton(H, "A11y_Desc_Two");
                var list = input is not null ? AutomationProperties.GetDescribedBy(input) : null;
                return list is not null && d1 is not null && d2 is not null &&
                    list.Count == 2 &&
                    ReferenceEquals(list[0], d1) &&
                    ReferenceEquals(list[1], d2);
            }));

            H.ClickButton("A11y_Desc_ToggleD2");
            await Harness.Render();
            H.Check("Accessibility_DescribedBy_List_TargetUnmountDropsOne", await Harness.WaitFor(() =>
            {
                var input = FindButton(H, "A11y_Desc_Input");
                var d1 = FindButton(H, "A11y_Desc_One");
                var list = input is not null ? AutomationProperties.GetDescribedBy(input) : null;
                return list is not null && d1 is not null &&
                    FindButton(H, "A11y_Desc_Two") is null &&
                    list.Count == 1 &&
                    ReferenceEquals(list[0], d1) &&
                    d1Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                    d2Ref?.Inner.CurrentChangedSubscriberCount == 1;
            }));

            H.ClickButton("A11y_Desc_ToggleInput");
            await Harness.Render();
            H.Check("Accessibility_DescribedBy_List_ReferrerUnmountDropsSubscriptions", await Harness.WaitFor(() =>
                FindButton(H, "A11y_Desc_Input") is null &&
                FindButton(H, "A11y_Desc_One") is not null &&
                d1Ref?.Inner.CurrentChangedSubscriberCount == 0 &&
                d2Ref?.Inner.CurrentChangedSubscriberCount == 0));

            H.ClickButton("A11y_Desc_ToggleInput");
            await Harness.Render();
            H.Check("Accessibility_DescribedBy_List_SurvivesReferrerRecreation", await Harness.WaitFor(() =>
            {
                var input = FindButton(H, "A11y_Desc_Input");
                var d1 = FindButton(H, "A11y_Desc_One");
                var list = input is not null ? AutomationProperties.GetDescribedBy(input) : null;
                return list is not null && d1 is not null &&
                    list.Count == 1 &&
                    ReferenceEquals(list[0], d1) &&
                    d1Ref?.Inner.CurrentChangedSubscriberCount == 1 &&
                    d2Ref?.Inner.CurrentChangedSubscriberCount == 1;
            }));
        }
    }
}
