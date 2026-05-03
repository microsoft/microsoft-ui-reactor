using DemoScriptTool.App.Models;
using static Microsoft.UI.Reactor.Factories;

namespace DemoScriptTool.App.Components;

public sealed record DemoPromptPanelProps(
    DemoScriptModel Model,
    System.Action<string> OnPromptChanged,
    System.Action<string> OnTitleChanged);

/// <summary>
/// Top-of-body region binding to <c>## Demo Prompt</c>. The text-area writes
/// into the model on every keystroke; the shell debounces persistent saves to
/// disk (spec §Demo Prompt Panel).
/// </summary>
public sealed class DemoPromptPanel : Component<DemoPromptPanelProps>
{
    public override Element Render()
    {
        var (title, setTitle) = UseState(Props.Model.Title);
        var (prompt, setPrompt) = UseState(Props.Model.DemoPrompt);

        // Sync local state to the model on (a) initial mount with this model, (b) every
        // mutation the model raises through Changed, and (c) when Props.Model is REPLACED
        // (Open Folder swaps the whole instance — Changed does not fire in that case).
        UseEffect(() =>
        {
            setTitle(Props.Model.Title);
            setPrompt(Props.Model.DemoPrompt);

            void Handler()
            {
                setTitle(Props.Model.Title);
                setPrompt(Props.Model.DemoPrompt);
            }
            Props.Model.Changed += Handler;
            return () => Props.Model.Changed -= Handler;
        }, Props.Model);

        var titleField = (TextField(title, v =>
        {
            setTitle(v);
            Props.OnTitleChanged(v);
        }, placeholder: "Demo title (rendered as # heading in demo-script.md)"))
            .Set(tb =>
            {
                tb.AcceptsReturn = false;
                tb.FontSize = 18;
                tb.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            })
            .AutomationName("Demo title");

        var promptField = (TextField(prompt, v =>
        {
            setPrompt(v);
            Props.OnPromptChanged(v);
        }, placeholder: "Describe the demo: tech stack, single-file vs multi-file, audience level, constraints…"))
            .Set(tb =>
            {
                tb.AcceptsReturn = true;
                tb.TextWrapping = TextWrapping.Wrap;
                tb.MinHeight = 96;
                tb.MaxHeight = 220;
            })
            .AutomationName("Demo prompt — persistent context for AI generation");

        return Border(
            VStack(8,
                Caption("DEMO TITLE").Foreground(Theme.SecondaryText),
                titleField,
                TextBlock("").Height(4),
                Caption("DEMO PROMPT").Foreground(Theme.SecondaryText),
                promptField))
            .Background(Theme.LayerFill)
            .CornerRadius(8)
            .Padding(16)
            .Margin(0, 0, 0, 12)
            .Landmark(Microsoft.UI.Xaml.Automation.Peers.AutomationLandmarkType.Form);
    }
}
