using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class ErrorStylingTests
{
    // ════════════════════════════════════════════════════════════════
    //  GetBrushKey
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetBrushKey_Error_Returns_CriticalBrush()
    {
        Assert.Equal("SystemFillColorCriticalBrush", ErrorStyling.GetBrushKey(Severity.Error));
    }

    [Fact]
    public void GetBrushKey_Warning_Returns_CautionBrush()
    {
        Assert.Equal("SystemFillColorCautionBrush", ErrorStyling.GetBrushKey(Severity.Warning));
    }

    [Fact]
    public void GetBrushKey_Info_Returns_NeutralBrush()
    {
        Assert.Equal("SystemFillColorSolidNeutralBrush", ErrorStyling.GetBrushKey(Severity.Info));
    }

    // ════════════════════════════════════════════════════════════════
    //  ShouldShowErrors
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ShouldShowErrors_Always_Returns_True_When_Messages()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "error");
        Assert.True(ErrorStyling.ShouldShowErrors(ctx, "f", ShowWhen.Always));
    }

    [Fact]
    public void ShouldShowErrors_Always_Returns_False_When_No_Messages()
    {
        var ctx = new ValidationContext();
        Assert.False(ErrorStyling.ShouldShowErrors(ctx, "f", ShowWhen.Always));
    }

    [Fact]
    public void ShouldShowErrors_WhenTouched_False_If_Not_Touched()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "error");
        Assert.False(ErrorStyling.ShouldShowErrors(ctx, "f", ShowWhen.WhenTouched));
    }

    [Fact]
    public void ShouldShowErrors_WhenTouched_True_If_Touched()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "error");
        ctx.MarkTouched("f");
        Assert.True(ErrorStyling.ShouldShowErrors(ctx, "f", ShowWhen.WhenTouched));
    }

    [Fact]
    public void ShouldShowErrors_WhenDirty_False_If_Not_Dirty()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("f");
        ctx.SetInitialValue("f", "initial");
        ctx.Add("f", "error");
        Assert.False(ErrorStyling.ShouldShowErrors(ctx, "f", ShowWhen.WhenDirty));
    }

    [Fact]
    public void ShouldShowErrors_WhenDirty_True_If_Dirty()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("f");
        ctx.SetInitialValue("f", "initial");
        ctx.NotifyValueChanged("f", "changed");
        ctx.Add("f", "error");
        Assert.True(ErrorStyling.ShouldShowErrors(ctx, "f", ShowWhen.WhenDirty));
    }

    [Fact]
    public void ShouldShowErrors_AfterFirstSubmit_False_Before_Submit()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "error");
        Assert.False(ErrorStyling.ShouldShowErrors(ctx, "f", ShowWhen.AfterFirstSubmit, submitAttempted: false));
    }

    [Fact]
    public void ShouldShowErrors_AfterFirstSubmit_True_After_Submit()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "error");
        Assert.True(ErrorStyling.ShouldShowErrors(ctx, "f", ShowWhen.AfterFirstSubmit, submitAttempted: true));
    }

    [Fact]
    public void ShouldShowErrors_Never_Always_False()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "error");
        ctx.MarkTouched("f");
        Assert.False(ErrorStyling.ShouldShowErrors(ctx, "f", ShowWhen.Never));
    }

    // ════════════════════════════════════════════════════════════════
    //  WithErrorStyling - border changes
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void WithErrorStyling_Applies_Border_When_Error_And_Always()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required", Severity.Error);

        var el = TextBox("").WithErrorStyling(ctx, "email", ShowWhen.Always);

        Assert.NotNull(el.Modifiers);
        Assert.Equal(ErrorStyling.ErrorBorderThickness, el.Modifiers!.BorderThickness);
        Assert.NotNull(el.ThemeBindings);
        Assert.True(el.ThemeBindings!.ContainsKey("BorderBrush"));
        Assert.Equal("SystemFillColorCriticalBrush", el.ThemeBindings["BorderBrush"].ResourceKey);
    }

    [Fact]
    public void WithErrorStyling_No_Change_When_Valid()
    {
        var ctx = new ValidationContext();

        var el = TextBox("").WithErrorStyling(ctx, "email", ShowWhen.Always);

        Assert.Null(el.ThemeBindings);
    }

    [Fact]
    public void WithErrorStyling_Warning_Uses_Caution_Brush()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "warning", Severity.Warning);

        var el = TextBox("").WithErrorStyling(ctx, "f", ShowWhen.Always);

        Assert.NotNull(el.ThemeBindings);
        Assert.Equal("SystemFillColorCautionBrush", el.ThemeBindings!["BorderBrush"].ResourceKey);
    }

    [Fact]
    public void WithErrorStyling_Reverts_When_No_Errors()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required");

        // With error
        var el1 = TextBox("").WithErrorStyling(ctx, "email", ShowWhen.Always);
        Assert.NotNull(el1.ThemeBindings);

        // Clear error
        ctx.Clear("email");
        var el2 = TextBox("").WithErrorStyling(ctx, "email", ShowWhen.Always);
        Assert.Null(el2.ThemeBindings);
    }

    // ════════════════════════════════════════════════════════════════
    //  Touched-state gating (default behavior)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void WithErrorStyling_Default_WhenTouched_Untouched_No_Styling()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required");

        // Default is WhenTouched, field not touched
        var el = TextBox("").WithErrorStyling(ctx, "email");
        Assert.Null(el.ThemeBindings);
    }

    [Fact]
    public void WithErrorStyling_Default_WhenTouched_Touched_Shows_Styling()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required");
        ctx.MarkTouched("email");

        var el = TextBox("").WithErrorStyling(ctx, "email");
        Assert.NotNull(el.ThemeBindings);
    }

    [Fact]
    public void Submit_Marks_All_Touched_Then_All_Errors_Visible()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");
        ctx.RegisterField("name");
        ctx.Add("email", "Required");
        ctx.Add("name", "Required");

        // Before submit: untouched, no styling
        Assert.False(ErrorStyling.ShouldShowErrors(ctx, "email", ShowWhen.WhenTouched));
        Assert.False(ErrorStyling.ShouldShowErrors(ctx, "name", ShowWhen.WhenTouched));

        // Submit marks all touched
        ctx.MarkAllTouched();

        Assert.True(ErrorStyling.ShouldShowErrors(ctx, "email", ShowWhen.WhenTouched));
        Assert.True(ErrorStyling.ShouldShowErrors(ctx, "name", ShowWhen.WhenTouched));
    }

    // ════════════════════════════════════════════════════════════════
    //  Custom error styling overrides
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void OnError_Custom_Styling_Applied()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required");

        var el = TextBox("")
            .OnError(e => e.Margin(99))
            .WithErrorStyling(ctx, "email", ShowWhen.Always);

        Assert.NotNull(el.Modifiers);
        Assert.Equal(new Microsoft.UI.Xaml.Thickness(99), el.Modifiers!.Margin);
    }

    [Fact]
    public void OnWarning_Custom_Styling_Applied()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "hint", Severity.Warning);

        var el = TextBox("")
            .OnWarning(e => e.Opacity(0.5))
            .WithErrorStyling(ctx, "f", ShowWhen.Always);

        Assert.NotNull(el.Modifiers);
        Assert.Equal(0.5, el.Modifiers!.Opacity);
    }

    [Fact]
    public void Custom_Styling_Reverted_When_Error_Cleared()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "error");

        // With error: custom styling applied
        var el1 = TextBox("")
            .OnError(e => e.Margin(99))
            .WithErrorStyling(ctx, "f", ShowWhen.Always);
        Assert.Equal(new Microsoft.UI.Xaml.Thickness(99), el1.Modifiers!.Margin);

        // Clear error
        ctx.Clear("f");
        var el2 = TextBox("")
            .OnError(e => e.Margin(99))
            .WithErrorStyling(ctx, "f", ShowWhen.Always);
        // No custom styling applied since no error
        Assert.Null(el2.Modifiers?.Margin);
    }

    // ════════════════════════════════════════════════════════════════
    //  Severity maps to correct brush
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Mixed_Severities_Uses_Highest()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "info", Severity.Info);
        ctx.Add("f", "warning", Severity.Warning);

        var el = TextBox("").WithErrorStyling(ctx, "f", ShowWhen.Always);
        Assert.Equal("SystemFillColorCautionBrush", el.ThemeBindings!["BorderBrush"].ResourceKey);
    }

    [Fact]
    public void Error_Severity_Shows_Critical_Brush()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "info", Severity.Info);
        ctx.Add("f", "error", Severity.Error);

        var el = TextBox("").WithErrorStyling(ctx, "f", ShowWhen.Always);
        Assert.Equal("SystemFillColorCriticalBrush", el.ThemeBindings!["BorderBrush"].ResourceKey);
    }
}
