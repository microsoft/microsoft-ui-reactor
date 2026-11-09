using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ButtonContentFixture
{
    internal sealed record Scenario(string Name, Func<Element?, Action, Element> Create, bool Invokable = true);
    internal sealed class Counters
    {
        public int Mounts;
        public int Cleanups;
        public int LastClickStage = -1;
    }

    internal sealed record MatrixProps(Scenario Scenario, Counters Counters);
    internal sealed class Matrix : Component<MatrixProps>
    {
        public override Element Render()
        {
            var (stage, setStage) = UseState(-2);
            Element? content = stage is -2 or 0 or 3 ? null
                : stage is -1 or 5 ? Empty()
                : Component<ContentProbe, ProbeProps>(new(stage == 2 ? "updated" : "initial", Props.Counters));
            return VStack(
                Button("BC_Next", () => setStage(stage + 1)),
                stage >= 7 ? null : Props.Scenario.Create(content, () => Props.Counters.LastClickStage = stage)
                    .AutomationId("BC_Target"));
        }
    }

    internal sealed record ProbeProps(string Label, Counters Counters);
    internal sealed class ContentProbe : Component<ProbeProps>
    {
        public override Element Render()
        {
            var (draft, setDraft) = UseState("initial draft");
            var (count, setCount) = UseState(0);
            UseEffect(() =>
            {
                Props.Counters.Mounts++;
                return () => Props.Counters.Cleanups++;
            });
            return HStack(
                Icon(FontIcon("\uE8A5")).AccessibilityHidden(),
                TextBlock(Props.Label).AutomationId("BC_Label"),
                TextBox(draft, setDraft).Width(120).AutomationName("draft").AutomationId("BC_Draft"),
                Button($"BC_Child:{count}", () => setCount(count + 1)));
        }
    }

    // Keep the fallback label unchanged in both forms: switching back must not
    // depend on a coincidental change to the string property.
    private static readonly Scenario[] Scenarios =
    [
        new("Button", (c, click) => Button("fallback", click) with { ContentElement = c }),
        new("HyperlinkButton", (c, click) => HyperlinkButton("fallback", onClick: click) with { ContentElement = c }),
        new("RepeatButton", (c, click) => RepeatButton("fallback", click) with { ContentElement = c }),
        new("ToggleButton", (c, click) => ToggleButton("fallback", false, _ => click()) with { ContentElement = c }),
        new("ThreeStateToggleButton", (c, click) => ThreeStateToggleButton("fallback", false, _ => click()) with { ContentElement = c }),
        new("DropDownButton", (c, _) => DropDownButton("fallback") with { ContentElement = c }, false),
        new("SplitButton", (c, click) => SplitButton("fallback", click) with { ContentElement = c }),
        new("ToggleSplitButton", (c, click) => ToggleSplitButton("fallback", false, _ => click()) with { ContentElement = c }),
        new("CheckBox", (c, click) => CheckBox(false, _ => click(), "fallback") with { ContentElement = c }),
        new("ThreeStateCheckBox", (c, click) => ThreeStateCheckBox(false, _ => click(), "fallback") with { ContentElement = c }),
        new("RadioButton", (c, click) => RadioButton("fallback", false, _ => click()) with { ContentElement = c }),
    ];

    internal sealed class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            foreach (var scenario in Scenarios)
            {
                var counters = new Counters();
                var host = H.CreateHost();
                host.Mount(_ => Component<Matrix, MatrixProps>(new(scenario, counters)));
                await Harness.Render();
                var control = FindTarget();
                H.Check($"{scenario.Name}_InitialText", control?.Content as string == "fallback");
                if (control is null) continue;

                await Next();
                H.Check($"{scenario.Name}_EmptyReplacesText", control.Content is null);
                await Next();
                H.Check($"{scenario.Name}_TextAfterEmpty", control.Content as string == "fallback");
                await Next();
                var draft = FindDraft();
                H.Check($"{scenario.Name}_ElementMounted", draft is not null && FindLabel()?.Text == "initial"
                    && counters.Mounts == 1 && counters.Cleanups == 0);
                if (draft is null) continue;

                draft.Text = "user draft";
                await Harness.Render();
                H.ClickButton("BC_Child:0");
                await Harness.Render();
                await Next();
                H.Check($"{scenario.Name}_UpdatedInPlace", ReferenceEquals(control, FindTarget())
                    && ReferenceEquals(draft, FindDraft()) && FindDraft()?.Text == "user draft"
                    && FindLabel()?.Text == "updated" && H.FindButton("BC_Child:1") is not null
                    && counters.Mounts == 1 && counters.Cleanups == 0);

                if (scenario.Invokable)
                {
                    H.Check($"{scenario.Name}_Activated", Activate(control));
                    await Harness.Render();
                    H.Check($"{scenario.Name}_LatestCallback", counters.LastClickStage == 2);
                }

                await Next();
                H.Check($"{scenario.Name}_ReturnsToText", ReferenceEquals(control, FindTarget())
                    && control.Content as string == "fallback" && counters.Cleanups == 1
                    && FindDraft() is null);
                await Next();
                H.Check($"{scenario.Name}_RemountAfterText", FindDraft()?.Text == "initial draft"
                    && counters.Mounts == 2 && counters.Cleanups == 1);
                await Next();
                H.Check($"{scenario.Name}_EmptyClearsContent", control.Content is null
                    && counters.Cleanups == 2 && FindDraft() is null);
                await Next();
                H.Check($"{scenario.Name}_RemountBeforeRemoval", FindDraft()?.Text == "initial draft"
                    && counters.Mounts == 3 && counters.Cleanups == 2);
                await Next();
                H.Check($"{scenario.Name}_Removed", FindTarget() is null && FindDraft() is null
                    && counters.Cleanups == 3);
            }
        }

        private WinUI.ContentControl? FindTarget() => H.FindControl<WinUI.ContentControl>(
            c => AutomationProperties.GetAutomationId(c) == "BC_Target");
        private WinUI.TextBox? FindDraft() => H.FindControl<WinUI.TextBox>(
            c => AutomationProperties.GetAutomationId(c) == "BC_Draft");
        private WinUI.TextBlock? FindLabel() => H.FindControl<WinUI.TextBlock>(
            c => AutomationProperties.GetAutomationId(c) == "BC_Label");
        private async Task Next()
        {
            H.ClickButton("BC_Next");
            await Harness.Render();
        }

        private static bool Activate(FrameworkElement control)
        {
            var peer = FrameworkElementAutomationPeer.CreatePeerForElement(control);
            if (peer?.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
                invoke.Invoke();
            else if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle)
                toggle.Toggle();
            else if (peer?.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider select)
                select.Select();
            else
                return false;
            return true;
        }
    }
}
