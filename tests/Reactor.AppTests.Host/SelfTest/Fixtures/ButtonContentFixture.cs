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

    // Callback-free, unkeyed, modifier-free button-family controls: nothing asks
    // for an element tag, so their content must be torn down without one.
    internal sealed class UntaggedHost : Component<Counters>
    {
        public override Element Render()
        {
            var (shown, setShown) = UseState(true);
            return VStack(
                Button("BC_UntaggedToggle", () => setShown(!shown)),
                DropDownButton("BC_UntaggedText"),
                shown ? Button(Component<ContentProbe, ProbeProps>(new("untagged", Props))) : null);
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
        private static void Noop() { }

        public override async Task RunAsync()
        {
            await CheckUntaggedControls();
            CheckUntaggedPooledTeardown();
            await CheckUncaptionedControls();
            foreach (var scenario in Scenarios)
            {
                await CheckSetterContent(scenario);
                await CheckElementToSetterContent(scenario);
                await CheckDeclaredContentWithSetter(scenario);
                await CheckAccessibleCaption(scenario);
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

        private async Task CheckUntaggedControls()
        {
            var counters = new Counters();
            var host = H.CreateHost();
            host.Mount(_ => Component<UntaggedHost, Counters>(counters));
            await Harness.Render();
            var text = H.FindControl<WinUI.DropDownButton>(c => c.Content as string == "BC_UntaggedText");
            H.Check("Untagged_TextControlHasNoState", text is not null && !Reconciler.TryGetReactorState(text, out _));
            var target = FindUntaggedTarget();
            H.Check("Untagged_ContentControlHasNoState", target is not null
                && !Reconciler.TryGetReactorState(target, out _) && counters.Mounts == 1);

            H.ClickButton("BC_UntaggedToggle");
            await Harness.Render();
            H.Check("Untagged_RemovalRunsCleanup", FindUntaggedTarget() is null && counters.Cleanups == 1);

            // The removed Button may come back from the pool: it must mount a fresh
            // child rather than reconcile against the one already torn down.
            H.ClickButton("BC_UntaggedToggle");
            await Harness.Render();
            H.Check("Untagged_RemountMountsFreshChild", FindUntaggedTarget() is not null
                && FindDraft()?.Text == "initial draft" && counters.Mounts == 2 && counters.Cleanups == 1);

            H.ClickButton("BC_UntaggedToggle");
            await Harness.Render();
        }

        private WinUI.Button? FindUntaggedTarget() => H.FindControl<WinUI.Button>(b => b.Content is UIElement);

        // The pooling teardown is the path a removed untagged control takes.
        private void CheckUntaggedPooledTeardown()
        {
            var reconciler = new Reconciler();
            var native = new WinUI.StackPanel();
            native.Children.Add(new WinUI.TextBlock { Text = "native" });
            var setterControl = (FrameworkElement)reconciler.Mount(
                DropDownButton("BC_SetterNative").Set(c => c.Content = native), Noop)!;
            var boundaryControl = (FrameworkElement)reconciler.Mount(
                Button(ErrorBoundary(TextBlock("guarded"), TextBlock("fallback"))), Noop)!;
            var boundaries = reconciler.ErrorBoundaryNodeCount;

            reconciler.UnmountAndPool(setterControl);
            reconciler.UnmountAndPool(boundaryControl);
            H.Check("Untagged_SetterContentSurvivesPooledRemoval", native.Children.Count == 1
                && (native.Children[0] as WinUI.TextBlock)?.Text == "native");
            H.Check("Untagged_PooledRemovalDropsErrorBoundary", boundaries == 1
                && reconciler.ErrorBoundaryNodeCount == 0);
        }

        private async Task CheckUncaptionedControls()
        {
            Element[] elements = [VStack(TextBlock("child")), Grid([], [], TextBlock("child")), Border(TextBlock("child")), TextBox("draft", _ => { })];
            foreach (var element in elements)
            {
                var reconciler = new Reconciler();
                var control = (FrameworkElement)reconciler.Mount(element, Noop)!;
                H.SetContent(control);
                try
                {
                    await Harness.Render();
                    var writes = 0;
                    var token = control.RegisterPropertyChangedCallback(AutomationProperties.NameProperty, (_, _) => writes++);
                    try
                    {
                        reconciler.Reconcile(element, element.IsEnabled(false), control, Noop);
                        await Harness.Render();
                        H.Check($"{element.GetType().Name}_NoDefaultNameWrites", writes == 0
                            && control.ReadLocalValue(AutomationProperties.NameProperty) == DependencyProperty.UnsetValue);
                    }
                    finally
                    {
                        control.UnregisterPropertyChangedCallback(AutomationProperties.NameProperty, token);
                    }
                }
                finally
                {
                    reconciler.UnmountChild(control);
                    H.SetContent(null);
                }
            }
        }

        private async Task CheckSetterContent(Scenario scenario)
        {
            var reconciler = new Reconciler();
            var icon = new WinUI.SymbolIcon(WinUI.Symbol.Save);
            var current = WithNativeContent(scenario.Create(null, Noop), icon);
            var control = (WinUI.ContentControl)reconciler.Mount(current, Noop)!;
            H.SetContent(control);
            try
            {
                await Harness.Render();
                H.Check($"{scenario.Name}_SetterContentMounted", ReferenceEquals(control.Content, icon)
                    && icon.ActualWidth > 0 && icon.ActualHeight > 0);

                var next = WithNativeContent(scenario.Create(null, Noop), icon).IsEnabled(false);
                var updated = reconciler.Reconcile(current, next, control, Noop);
                current = next;
                await Harness.Render();
                H.Check($"{scenario.Name}_SetterContentSurvivesUpdate", ReferenceEquals(updated, control)
                    && ReferenceEquals(control.Content, icon) && !control.IsEnabled
                    && icon.ActualWidth > 0 && icon.ActualHeight > 0);

                var replacement = new WinUI.SymbolIcon(WinUI.Symbol.Help);
                next = WithNativeContent(scenario.Create(null, Noop), replacement);
                reconciler.Reconcile(current, next, control, Noop);
                current = next;
                await Harness.Render();
                H.Check($"{scenario.Name}_SetterContentCanBeReplaced", ReferenceEquals(control.Content, replacement)
                    && replacement.ActualWidth > 0 && replacement.Symbol == WinUI.Symbol.Help);

                next = scenario.Create(null, Noop).IsEnabled(false);
                reconciler.Reconcile(current, next, control, Noop);
                await Harness.Render();
                H.Check($"{scenario.Name}_UndeclaredContentIsNotReconciled", ReferenceEquals(control.Content, replacement)
                    && !control.IsEnabled && replacement.ActualWidth > 0);
            }
            finally
            {
                reconciler.UnmountChild(control);
                H.SetContent(null);
            }
        }

        private async Task CheckAccessibleCaption(Scenario scenario)
        {
            var reconciler = new Reconciler();
            var current = scenario.Create(TextBlock("visible caption"), Noop);
            var control = (WinUI.ContentControl)reconciler.Mount(current, Noop)!;
            H.SetContent(control);
            try
            {
                await Harness.Render();
                H.Check($"{scenario.Name}_NameUsesVisibleContent", AutomationProperties.GetName(control) == "visible caption");
                var next = scenario.Create(Icon(FontIcon("\uE8A5")), Noop);
                reconciler.Reconcile(current, next, control, Noop);
                current = next;
                await Harness.Render();
                H.Check($"{scenario.Name}_HiddenFallbackDoesNotNameIcon", string.IsNullOrEmpty(AutomationProperties.GetName(control)));
                H.Check($"{scenario.Name}_DefaultNameIsCleared", control.ReadLocalValue(AutomationProperties.NameProperty) == DependencyProperty.UnsetValue);
                next = scenario.Create(TextBlock("new caption"), Noop);
                reconciler.Reconcile(current, next, control, Noop);
                current = next;
                H.Check($"{scenario.Name}_CaptionAfterIconGetsDefaultName", AutomationProperties.GetName(control) == "new caption");
                next = scenario.Create(TextBlock("Save"), Noop).AutomationName("Save");
                reconciler.Reconcile(current, next, control, Noop);
                current = next;
                next = scenario.Create(Icon(FontIcon("\uE8A5")), Noop).AutomationName("Save");
                reconciler.Reconcile(current, next, control, Noop);
                current = next;
                await Harness.Render();
                H.Check($"{scenario.Name}_ExplicitNameMatchingOldCaptionSurvives", AutomationProperties.GetName(control) == "Save");
                next = scenario.Create(Icon(FontIcon("\uE8A5")), Noop).AutomationName("explicit action");
                reconciler.Reconcile(current, next, control, Noop);
                await Harness.Render();
                H.Check($"{scenario.Name}_ExplicitNameWins", AutomationProperties.GetName(control) == "explicit action");
            }
            finally
            {
                reconciler.UnmountChild(control);
                H.SetContent(null);
            }
        }

        private async Task CheckElementToSetterContent(Scenario scenario)
        {
            var reconciler = new Reconciler();
            var counters = new Counters();
            var current = scenario.Create(Component<ContentProbe, ProbeProps>(new("old", counters)), Noop);
            var control = (WinUI.ContentControl)reconciler.Mount(current, Noop)!;
            H.SetContent(control);
            try
            {
                await Harness.Render();
                H.Check($"{scenario.Name}_BeforeSetterTransition", counters.Mounts == 1 && counters.Cleanups == 0);
                var icon = new WinUI.SymbolIcon(WinUI.Symbol.Save);
                var next = WithNativeContent(scenario.Create(null, Noop), icon);
                reconciler.Reconcile(current, next, control, Noop);
                current = next;
                await Harness.Render();
                H.Check($"{scenario.Name}_ElementToSetterContent", ReferenceEquals(control.Content, icon)
                    && icon.ActualWidth > 0 && icon.ActualHeight > 0 && counters.Cleanups == 1);
                next = WithNativeContent(scenario.Create(null, Noop), icon).IsEnabled(false);
                reconciler.Reconcile(current, next, control, Noop);
                await Harness.Render();
                H.Check($"{scenario.Name}_SetterTransitionSurvivesRerender", ReferenceEquals(control.Content, icon)
                    && !control.IsEnabled && counters.Cleanups == 1);
            }
            finally
            {
                reconciler.UnmountChild(control);
                H.SetContent(null);
            }
            H.Check($"{scenario.Name}_SetterTransitionCleanupOnce", counters.Cleanups == 1);
        }

        private async Task CheckDeclaredContentWithSetter(Scenario scenario)
        {
            var reconciler = new Reconciler();
            var counters = new Counters();
            var icon = new WinUI.SymbolIcon(WinUI.Symbol.Save);
            var current = WithNativeContent(
                scenario.Create(Component<ContentProbe, ProbeProps>(new("declared", counters)), Noop), icon);
            var control = (WinUI.ContentControl)reconciler.Mount(current, Noop)!;
            H.SetContent(control);
            try
            {
                await Harness.Render();
                var original = control.Content;
                var draft = FindDraft();
                H.Check($"{scenario.Name}_DeclaredContentWinsSetterOnMount", !ReferenceEquals(original, icon)
                    && FindLabel()?.Text == "declared" && draft is not null && counters.Mounts == 1);
                if (draft is null) return;
                draft.Text = "kept draft";
                await Harness.Render();
                var next = WithNativeContent(
                    scenario.Create(Component<ContentProbe, ProbeProps>(new("updated", counters)), Noop), icon);
                reconciler.Reconcile(current, next, control, Noop);
                current = next;
                await Harness.Render();
                H.Check($"{scenario.Name}_DeclaredContentRetainedDespiteSetter", ReferenceEquals(control.Content, original)
                    && ReferenceEquals(FindDraft(), draft) && draft.Text == "kept draft"
                    && FindLabel()?.Text == "updated" && counters.Mounts == 1 && counters.Cleanups == 0);
            }
            finally
            {
                reconciler.UnmountChild(control);
                H.SetContent(null);
            }
            H.Check($"{scenario.Name}_DeclaredContentWithSetterCleanedOnce", counters.Cleanups == 1);
        }

        private static Element WithNativeContent(Element element, UIElement content) => element switch
        {
            ButtonElement e => e.Set(c => c.Content = content),
            HyperlinkButtonElement e => e.Set(c => c.Content = content),
            RepeatButtonElement e => e.Set(c => c.Content = content),
            ToggleButtonElement e => e.Set(c => c.Content = content),
            DropDownButtonElement e => e.Set(c => c.Content = content),
            SplitButtonElement e => e.Set(c => c.Content = content),
            ToggleSplitButtonElement e => e.Set(c => c.Content = content),
            CheckBoxElement e => e.Set(c => c.Content = content),
            RadioButtonElement e => e.Set(c => c.Content = content),
            _ => throw new InvalidOperationException($"Unexpected button scenario: {element.GetType().Name}"),
        };

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
