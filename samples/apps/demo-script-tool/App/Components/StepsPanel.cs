using System;
using System.Collections.Generic;
using System.Linq;
using DemoScriptTool.App.Models;
using static Microsoft.UI.Reactor.Factories;

namespace DemoScriptTool.App.Components;

public sealed record StepsPanelProps(
    DemoScriptModel Model,
    bool IsGenerating,
    Action<int, string> OnPromptChanged,
    Action<int, string> OnTitleChanged,
    Action<StepModel> OnRun,
    Action<StepModel> OnCopyDelta,
    Action OnAddStep,
    Action<StepModel> OnDeleteStep,
    Action<StepModel> OnRegenFromStep)
{
    // Manual Equals: callback delegates are excluded from memo equality. They
    // get a fresh delegate identity each parent render (local functions in
    // DemoScriptShell.Render). SAFETY CONTRACT: when memo decides "skip render",
    // Reactor does NOT refresh Props on the child, so the child continues to
    // dispatch through the *prior* delegates. That's only safe when the
    // callbacks' captured state doesn't change between renders, OR any state
    // they capture is reflected in one of the data fields below. Both hold
    // today: callbacks close over `model` (UseRef-stable identity) and
    // observable Model changes show up via reference identity. Framework-level
    // fix tracked at #151.
    public bool Equals(StepsPanelProps? other) =>
        other is not null
        && ReferenceEquals(Model, other.Model)
        && IsGenerating == other.IsGenerating;
    public override int GetHashCode() => HashCode.Combine(Model, IsGenerating);
}

/// <summary>
/// Vertical scroller of <see cref="StepCard"/> instances keyed by step number.
/// Subscribes to the model so step add/remove from external file edits
/// rebuild the list automatically (spec §Reconciliation).
/// </summary>
public sealed class StepsPanel : Component<StepsPanelProps>
{
    public override Element Render()
    {
        var (_, setRevision) = UseState(0, threadSafe: true);
        var counterRef = UseRef(0);

        // StepsChanged only — the steps panel cares about Add/Remove, not about
        // typing in the demo title/prompt (which would otherwise re-render every
        // step card on every keystroke).
        UseEffect(() =>
        {
            void Handler() { counterRef.Current++; setRevision(counterRef.Current); }
            Props.Model.StepsChanged += Handler;
            return () => Props.Model.StepsChanged -= Handler;
        }, Props.Model);

        var steps = Props.Model.Steps;

        var addStepCmd = new Command
        {
            Label = "Add step",
            Execute = Props.OnAddStep,
            Icon = SymbolIcon("Add"),
            Description = "Append a new empty step at the end of the script",
            Accelerator = Accelerator(Windows.System.VirtualKey.Enter,
                Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift),
        };

        var addButton = Button(addStepCmd)
            .AutomationName("Add a new step")
            .HAlign(HorizontalAlignment.Stretch)
            .Margin(0, 4, 0, 0);

        if (steps.Count == 0)
        {
            return Border(
                VStack(12,
                    SubHeading("No steps yet").Foreground(Theme.SecondaryText),
                    TextBlock("Add steps below, or run Generate All once you have a few in mind.")
                        .Foreground(Theme.SecondaryText)
                        .TextWrapping(TextWrapping.Wrap),
                    addButton))
                .Padding(40)
                .HAlign(HorizontalAlignment.Center)
                .Landmark(Microsoft.UI.Xaml.Automation.Peers.AutomationLandmarkType.Main);
        }

        // Pass each card a reference to the prior step so its show-code mode can
        // bold lines that appeared since the previous step. Card 0 has no prior.
        var cards = steps
            .Select((s, idx) => (Element)Component<StepCard, StepCardProps>(new StepCardProps(
                s,
                idx > 0 ? steps[idx - 1] : null,
                steps.Count,
                Props.IsGenerating,
                Props.OnPromptChanged, Props.OnTitleChanged, Props.OnRun, Props.OnCopyDelta, Props.OnDeleteStep,
                Props.OnRegenFromStep)))
            .Append(addButton)
            .ToArray();

        return (ScrollViewer(VStack(cards))
            with
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            })
            .Padding(0, 0, 8, 0)
            .Landmark(Microsoft.UI.Xaml.Automation.Peers.AutomationLandmarkType.Main);
    }
}
