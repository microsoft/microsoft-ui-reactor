using System;
using DemoScriptTool.App.Models;
using Microsoft.UI.Reactor.Animation;
using static Microsoft.UI.Reactor.Factories;

namespace DemoScriptTool.App.Components;

public sealed record StepCardProps(
    StepModel Step,
    int TotalSteps,
    Action<int, string> OnPromptChanged,
    Action<int, string> OnTitleChanged,
    Action<StepModel> OnRun,
    Action<StepModel> OnCopyDelta,
    Action<StepModel> OnDelete);

/// <summary>
/// One step rendered as a three-column card: prompt | code | actions
/// (spec §Steps Panel / §Card Surface). The card subscribes directly to the
/// step model so streaming token updates re-render only this card, not the
/// parent panel.
/// </summary>
public sealed class StepCard : Component<StepCardProps>
{
    public override Element Render()
    {
        var step = Props.Step;
        var (_, setRevision) = UseState(0, threadSafe: true);
        var counterRef = UseRef(0);

        UseEffect(() =>
        {
            void Handler() { counterRef.Current++; setRevision(counterRef.Current); }
            step.Changed += Handler;
            return () => step.Changed -= Handler;
        }, step);

        var hasCode = !string.IsNullOrEmpty(step.Code);
        var hasDelta = !string.IsNullOrWhiteSpace(step.Delta);
        var canRun = step.OutputPath is not null;

        var promptField = (TextField(step.Prompt,
                v => Props.OnPromptChanged(step.Number, v),
                placeholder: "What should this step do?"))
            .Set(tb =>
            {
                tb.AcceptsReturn = true;
                tb.TextWrapping = TextWrapping.Wrap;
                tb.MinHeight = 140;
            })
            .AutomationName($"Prompt for step {step.Number}");

        var codeBody = hasCode
            ? (Element)Border(
                ScrollView(TextBlock(step.Code)
                    .Set(tb =>
                    {
                        tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas, Courier New");
                        tb.IsTextSelectionEnabled = true;
                        tb.TextWrapping = TextWrapping.NoWrap;
                    })
                    .Foreground(Theme.PrimaryText)
                    .Padding(12))
                .Set(sv => sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto))
                .Background(Theme.ControlFill)
                .CornerRadius(4)
                .WithBorder(Theme.ControlStrokeSecondary, 1)
            : Border(TextBlock("No code generated yet.")
                    .Foreground(Theme.SecondaryText)
                    .Padding(12))
                .Background(Theme.SubtleFill)
                .CornerRadius(4);

        var stateBadge = BuildStateBadge(step);

        var runCmd = new Command
        {
            Label = "Run",
            Execute = () => Props.OnRun(step),
            CanExecute = canRun,
            Icon = SymbolIcon("Play"),
            Description = $"Run step {step.Number} via dotnet run",
        };

        var copyCmd = new Command
        {
            Label = "Copy Delta",
            Execute = () => Props.OnCopyDelta(step),
            CanExecute = hasDelta,
            Icon = SymbolIcon("Copy"),
            Description = $"Copy speaker notes for step {step.Number} to the clipboard",
        };

        var deleteCmd = new Command
        {
            Label = "Delete",
            Execute = () => Props.OnDelete(step),
            Icon = SymbolIcon("Delete"),
            Description = $"Remove step {step.Number} from the script",
        };

        var actions = VStack(8,
            Button(runCmd).AutomationName($"Run step {step.Number}"),
            Button(copyCmd).AutomationName($"Copy speaker notes for step {step.Number}"),
            Button(deleteCmd).AutomationName($"Delete step {step.Number}"));

        var failureOutput = (step.BuildState == BuildState.Failed && !string.IsNullOrEmpty(step.BuildOutput))
            ? Border(
                ScrollView(TextBlock(step.BuildOutput!)
                    .Set(tb => tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas, Courier New"))
                    .Foreground(Theme.PrimaryText)
                    .Padding(8))
                .Set(sv => { sv.MaxHeight = 140; sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto; }))
                .Background(Theme.SystemCriticalBackground)
                .WithBorder(Theme.SystemCritical, 1)
                .CornerRadius(4)
                .Margin(0, 8, 0, 0)
                .AutomationName("Compiler output")
            : Empty();

        var titleField = (TextField(step.Title,
                v => Props.OnTitleChanged(step.Number, v),
                placeholder: "Step title"))
            .Set(tb =>
            {
                tb.AcceptsReturn = false;
                tb.FontSize = 18;
                tb.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            })
            .AutomationName($"Title for step {step.Number}")
            .HeadingLevel(Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2)
            .Flex(grow: 1);

        var headerRow = (FlexRow(
                Caption($"STEP {step.Number}")
                    .Foreground(Theme.SecondaryText)
                    .VAlign(VerticalAlignment.Center)
                    .Width(64),
                titleField,
                stateBadge.VAlign(VerticalAlignment.Center))
            with { ColumnGap = 12, AlignItems = FlexAlign.Center });

        var grid = Grid(
            columns: [GridSize.Px(280), GridSize.Star(), GridSize.Px(140)],
            rows: [GridSize.Auto, GridSize.Auto, GridSize.Auto],
            headerRow.Grid(row: 0, columnSpan: 3),
            VStack(6,
                Caption("PROMPT").Foreground(Theme.SecondaryText),
                promptField).Grid(row: 1, column: 0).Margin(0, 12, 16, 0),
            VStack(6,
                Caption("GENERATED CODE").Foreground(Theme.SecondaryText),
                codeBody).Grid(row: 1, column: 1).Margin(0, 12, 16, 0),
            VStack(6,
                Caption("ACTIONS").Foreground(Theme.SecondaryText),
                actions).Grid(row: 1, column: 2).Margin(0, 12, 0, 0),
            failureOutput.Grid(row: 2, columnSpan: 3));

        return Border(grid)
            .Background(Theme.CardBackground)
            .WithBorder(Theme.CardStroke, 1)
            .CornerRadius(8)
            .Padding(16)
            .Set(b => b.BackgroundSizing = Microsoft.UI.Xaml.Controls.BackgroundSizing.InnerBorderEdge)
            .Margin(0, 0, 0, 12)
            .AutomationName($"Step {step.Number} — {step.Title}")
            .PositionInSet(step.Number, Props.TotalSteps)
            .Transition(Transition.Fade + Transition.Slide(Edge.Bottom))
            .WithKey($"step-{step.Number}");
    }

    static Element BuildStateBadge(StepModel step) => step.BuildState switch
    {
        BuildState.NotBuilt => Empty(),
        BuildState.Building => HStack(6,
                ProgressRing().Width(14).Height(14),
                Caption("Building…").Foreground(Theme.SecondaryText)),
        BuildState.Succeeded => HStack(6,
                TextBlock("✓").Foreground(Theme.SystemSuccess).FontSize(14).VAlign(VerticalAlignment.Center),
                Caption("Build succeeded").Foreground(Theme.SystemSuccess)),
        BuildState.Fixing => HStack(6,
                ProgressRing().Width(14).Height(14),
                Caption($"Fixing… (attempt {step.FixAttempts + 1})").Foreground(Theme.SystemCaution)),
        BuildState.Failed => HStack(6,
                TextBlock("✕").Foreground(Theme.SystemCritical).FontSize(14).VAlign(VerticalAlignment.Center),
                Caption("Build failed").Foreground(Theme.SystemCritical)),
        _ => Empty(),
    };
}
