using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Controls.Validation.ValidationVisualizerDsl;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class ValidationVisualizerTests
{
    // ════════════════════════════════════════════════════════════════
    //  Error bubbling / filtering
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void FilterMessages_No_Filter_Catches_All()
    {
        var messages = new List<ValidationMessage>
        {
            new("f1", "error", Severity.Error),
            new("f2", "warning", Severity.Warning),
        };

        var (caught, uncaught) = ErrorBubbling.FilterMessages(messages, null);
        Assert.Equal(2, caught.Count);
        Assert.Empty(uncaught);
    }

    [Fact]
    public void FilterMessages_Error_Filter_Only_Catches_Errors()
    {
        var messages = new List<ValidationMessage>
        {
            new("f1", "error", Severity.Error),
            new("f2", "warning", Severity.Warning),
            new("f3", "info", Severity.Info),
        };

        var (caught, uncaught) = ErrorBubbling.FilterMessages(messages, Severity.Error);
        Assert.Single(caught);
        Assert.Equal("f1", caught[0].Field);
        Assert.Equal(2, uncaught.Count);
    }

    [Fact]
    public void FilterMessages_Warning_Filter()
    {
        var messages = new List<ValidationMessage>
        {
            new("f1", "error", Severity.Error),
            new("f2", "warning", Severity.Warning),
        };

        var (caught, uncaught) = ErrorBubbling.FilterMessages(messages, Severity.Warning);
        Assert.Single(caught);
        Assert.Equal(Severity.Warning, caught[0].Severity);
        Assert.Single(uncaught);
        Assert.Equal(Severity.Error, uncaught[0].Severity);
    }

    // ════════════════════════════════════════════════════════════════
    //  Error caught by nearest visualizer / uncaught bubbles
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Caught_Errors_Removed_From_Further_Bubbling()
    {
        var messages = new List<ValidationMessage>
        {
            new("email", "Required", Severity.Error),
            new("name", "Too short", Severity.Error),
        };

        // Section-level visualizer catches errors (no severity filter = catch all)
        var (sectionCaught, sectionUncaught) = ErrorBubbling.FilterMessages(messages, null);

        // All caught at section level
        Assert.Equal(2, sectionCaught.Count);
        Assert.Empty(sectionUncaught);

        // Page-level would see nothing
    }

    [Fact]
    public void Uncaught_Errors_Bubble_To_Parent()
    {
        var messages = new List<ValidationMessage>
        {
            new("email", "Required", Severity.Error),
            new("email", "Hint", Severity.Warning),
        };

        // Section-level catches only errors
        var (sectionCaught, sectionUncaught) = ErrorBubbling.FilterMessages(messages, Severity.Error);
        Assert.Single(sectionCaught);

        // Page-level gets the warnings
        Assert.Single(sectionUncaught);
        Assert.Equal(Severity.Warning, sectionUncaught[0].Severity);
    }

    [Fact]
    public void No_Error_Is_Silently_Lost()
    {
        var messages = new List<ValidationMessage>
        {
            new("f1", "e1", Severity.Error),
            new("f2", "w1", Severity.Warning),
            new("f3", "i1", Severity.Info),
        };

        // Level 1: catches only errors
        var (level1Caught, level1Uncaught) = ErrorBubbling.FilterMessages(messages, Severity.Error);

        // Level 2: catches remaining (no filter)
        var (level2Caught, level2Uncaught) = ErrorBubbling.FilterMessages(level1Uncaught, null);

        // All messages accounted for
        Assert.Single(level1Caught);
        Assert.Equal(2, level2Caught.Count);
        Assert.Empty(level2Uncaught);

        // Total = original count
        Assert.Equal(messages.Count, level1Caught.Count + level2Caught.Count);
    }

    // ════════════════════════════════════════════════════════════════
    //  ShouldDisplay
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ShouldDisplay_Empty_Messages_Returns_False()
    {
        Assert.False(ErrorBubbling.ShouldDisplay([], ShowWhen.Always));
    }

    [Fact]
    public void ShouldDisplay_Always_With_Messages_Returns_True()
    {
        var messages = new List<ValidationMessage> { new("f", "error") };
        Assert.True(ErrorBubbling.ShouldDisplay(messages, ShowWhen.Always));
    }

    [Fact]
    public void ShouldDisplay_WhenTouched_Respects_Context()
    {
        var ctx = new ValidationContext();
        var messages = new List<ValidationMessage> { new("f", "error") };

        // Not touched
        Assert.False(ErrorBubbling.ShouldDisplay(messages, ShowWhen.WhenTouched, ctx));

        // Touch the field
        ctx.MarkTouched("f");
        Assert.True(ErrorBubbling.ShouldDisplay(messages, ShowWhen.WhenTouched, ctx));
    }

    [Fact]
    public void ShouldDisplay_Never_Always_False()
    {
        var messages = new List<ValidationMessage> { new("f", "error") };
        Assert.False(ErrorBubbling.ShouldDisplay(messages, ShowWhen.Never));
    }

    // ════════════════════════════════════════════════════════════════
    //  HighestSeverity
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void HighestSeverity_Returns_Error_When_Mixed()
    {
        var messages = new List<ValidationMessage>
        {
            new("f1", "info", Severity.Info),
            new("f2", "error", Severity.Error),
            new("f3", "warning", Severity.Warning),
        };
        Assert.Equal(Severity.Error, ErrorBubbling.HighestSeverity(messages));
    }

    [Fact]
    public void HighestSeverity_Returns_Null_When_Empty()
    {
        Assert.Null(ErrorBubbling.HighestSeverity([]));
    }

    // ════════════════════════════════════════════════════════════════
    //  Inline visualizer (.ShowErrors())
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ShowErrors_Creates_Inline_Visualizer()
    {
        var el = TextBox("test")
            .Validate("email", Validate.Required())
            .ShowErrors();

        Assert.IsType<ValidationVisualizerElement>(el);
        Assert.Equal(VisualizerStyle.Inline, el.Style);
        Assert.IsType<TextBoxElement>(el.Content);
    }

    [Fact]
    public void ShowErrors_Respects_ShowWhen()
    {
        var el = TextBox("test").ShowErrors(ShowWhen.WhenTouched);
        Assert.Equal(ShowWhen.WhenTouched, el.ShowWhen);
    }

    // ════════════════════════════════════════════════════════════════
    //  Summary visualizer
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Summary_Visualizer_Creates_Correctly()
    {
        var el = ValidationVisualizer(
            VisualizerStyle.Summary,
            VStack(TextBox("a"), TextBox("b")),
            title: "Please fix:");

        Assert.Equal(VisualizerStyle.Summary, el.Style);
        Assert.Equal("Please fix:", el.Title);
    }

    [Fact]
    public void Summary_Visualizer_Collects_All_Errors()
    {
        // Verify the summary receives all messages from the subtree
        var ctx = new ValidationContext();
        ctx.Add("email", "Required");
        ctx.Add("name", "Too short");

        var allMessages = ctx.GetAllMessages();
        Assert.Equal(2, allMessages.Count);
    }

    // ════════════════════════════════════════════════════════════════
    //  InfoBar visualizer
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void InfoBar_Visualizer_Creates_Correctly()
    {
        var el = ValidationVisualizer(
            VisualizerStyle.InfoBar,
            VStack(TextBox("a")));

        Assert.Equal(VisualizerStyle.InfoBar, el.Style);
    }

    [Fact]
    public void InfoBar_Severity_Matches_Worst_Error()
    {
        var messages = new List<ValidationMessage>
        {
            new("f1", "info", Severity.Info),
            new("f2", "error", Severity.Error),
        };
        Assert.Equal(Severity.Error, ErrorBubbling.HighestSeverity(messages));
    }

    // ════════════════════════════════════════════════════════════════
    //  Custom visualizer
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Custom_Visualizer_Receives_Messages()
    {
        IReadOnlyList<ValidationMessage>? receivedMessages = null;

        var el = ValidationVisualizer(
            render: msgs => { receivedMessages = msgs; return TextBlock("errors"); },
            content: TextBox("a"));

        Assert.Equal(VisualizerStyle.Custom, el.Style);
        Assert.NotNull(el.CustomRender);

        // Simulate rendering with messages
        var testMessages = new List<ValidationMessage> { new("f", "test error") };
        var rendered = el.CustomRender!(testMessages);
        Assert.NotNull(rendered);
        Assert.Same(testMessages, receivedMessages);
    }

    // ════════════════════════════════════════════════════════════════
    //  Hierarchical composition
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Nested_Visualizers_Section_Catches_Page_Gets_Remainder()
    {
        var allMessages = new List<ValidationMessage>
        {
            new("email", "Required", Severity.Error),
            new("email", "Format hint", Severity.Info),
            new("name", "Too short", Severity.Error),
        };

        // Section-level: catches only errors
        var (sectionCaught, sectionUncaught) = ErrorBubbling.FilterMessages(allMessages, Severity.Error);
        Assert.Equal(2, sectionCaught.Count); // Both Error messages
        Assert.Single(sectionUncaught); // Info message bubbles up

        // Page-level: catches everything remaining
        var (pageCaught, pageUncaught) = ErrorBubbling.FilterMessages(sectionUncaught, null);
        Assert.Single(pageCaught); // The Info message
        Assert.Empty(pageUncaught);
    }

    [Fact]
    public void Errors_Caught_At_Section_Dont_Appear_At_Page()
    {
        var messages = new List<ValidationMessage>
        {
            new("email", "Required", Severity.Error),
        };

        // Section catches all
        var (sectionCaught, sectionUncaught) = ErrorBubbling.FilterMessages(messages, null);
        Assert.Single(sectionCaught);
        Assert.Empty(sectionUncaught);

        // Page sees nothing
        var (pageCaught, _) = ErrorBubbling.FilterMessages(sectionUncaught, null);
        Assert.Empty(pageCaught);
    }

    [Fact]
    public void Errors_Not_Caught_At_Section_Appear_At_Page()
    {
        var messages = new List<ValidationMessage>
        {
            new("email", "Required", Severity.Error),
            new("global", "Server error", Severity.Warning),
        };

        // Section only catches Warnings
        var (sectionCaught, sectionUncaught) = ErrorBubbling.FilterMessages(messages, Severity.Warning);
        Assert.Single(sectionCaught);
        Assert.Equal("global", sectionCaught[0].Field);

        // Page catches the Error that section didn't catch
        var (pageCaught, _) = ErrorBubbling.FilterMessages(sectionUncaught, null);
        Assert.Single(pageCaught);
        Assert.Equal("email", pageCaught[0].Field);
    }

    // ════════════════════════════════════════════════════════════════
    //  Visualizer options
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Severity_Filter_Lets_Lower_Severity_Bubble()
    {
        var messages = new List<ValidationMessage>
        {
            new("f", "error", Severity.Error),
            new("f", "info", Severity.Info),
        };

        // Filter for Error only
        var (caught, uncaught) = ErrorBubbling.FilterMessages(messages, Severity.Error);
        Assert.Single(caught);
        Assert.Equal(Severity.Error, caught[0].Severity);
        Assert.Single(uncaught);
        Assert.Equal(Severity.Info, uncaught[0].Severity);
    }

    [Fact]
    public void ShowWhen_Gating_Works()
    {
        var ctx = new ValidationContext();
        var messages = new List<ValidationMessage> { new("f", "error") };

        // WhenTouched + not touched = don't display
        Assert.False(ErrorBubbling.ShouldDisplay(messages, ShowWhen.WhenTouched, ctx));

        // Touch it
        ctx.MarkTouched("f");
        Assert.True(ErrorBubbling.ShouldDisplay(messages, ShowWhen.WhenTouched, ctx));
    }
}
