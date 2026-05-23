using DemoScriptTool.App.Models;
using static Microsoft.UI.Reactor.Factories;

namespace DemoScriptTool.App.Components;

public sealed record DemoPromptPanelProps(
    DemoScriptModel Model,
    System.Action<string> OnPromptChanged,
    System.Action<string> OnTitleChanged)
{
    // Manual Equals: callback delegates are excluded from memo equality. They
    // get a fresh delegate identity each parent render. SAFETY CONTRACT: when
    // memo decides "skip render", Reactor does NOT refresh Props on the child,
    // so the child continues to dispatch through the *prior* delegates. That's
    // only safe when the callbacks' captured state doesn't change between
    // renders, OR any state they capture is reflected in one of the data
    // fields below. Both hold today: callbacks close over `model` (UseRef-
    // stable identity); Model changes flow via reference identity below.
    // Framework-level fix tracked at #151.
    public bool Equals(DemoPromptPanelProps? other) =>
        other is not null && ReferenceEquals(Model, other.Model);
    public override int GetHashCode() => Model.GetHashCode();
}

/// <summary>
/// Top-of-body region binding to <c>## Demo Prompt</c>. The text-area writes
/// into the model on every keystroke; the shell debounces persistent saves to
/// disk (spec §Demo Prompt Panel).
/// </summary>
public sealed class DemoPromptPanel : Component<DemoPromptPanelProps>
{
    public override Element Render()
    {
        // Local typing buffer — the source of truth WHILE the user is editing.
        // Keystroke flow: user types → local set → push to model (debounced save).
        // We deliberately do NOT subscribe to model.Changed in this panel: that
        // would round-trip our own keystrokes back through setState in the same
        // frame, which makes WinUI's TextBox clear its selection / reset the
        // caret to 0. The shell mints a NEW DemoScriptModel instance on Open
        // Folder / file-watcher reload, which lands here as a Props.Model swap
        // and is picked up by the dep-keyed effect below.
        var (title, setTitle) = UseState(Props.Model.Title);
        var (prompt, setPrompt) = UseState(Props.Model.DemoPrompt);

        UseEffect(() =>
        {
            setTitle(Props.Model.Title);
            setPrompt(Props.Model.DemoPrompt);
        }, Props.Model);

        var titleField = (TextBox(title, v =>
        {
            setTitle(v);
            Props.OnTitleChanged(v);
        }, placeholder: "Demo title (rendered as # heading in demo-script.md)")
            with { AcceptsReturn = false })
            .FontSize(18)
            .FontWeight(Microsoft.UI.Text.FontWeights.SemiBold)
            .AutomationName("Demo title");

        var promptField = (TextBox(prompt, v =>
        {
            setPrompt(v);
            Props.OnPromptChanged(v);
        }, placeholder: "Describe the demo: tech stack, single-file vs multi-file, audience level, constraints…")
            with { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap })
            .MinHeight(96)
            .MaxHeight(220)
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
