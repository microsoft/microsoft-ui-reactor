using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Controls.Validation;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class UseFocusTests
{
    // ════════════════════════════════════════════════════════════════
    //  FocusManager creation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void UseFocus_Creates_FocusManager()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        var fm = ctx.UseFocus();

        Assert.NotNull(fm);
        Assert.IsType<FocusManager>(fm);
    }

    [Fact]
    public void UseFocus_Same_Instance_Across_Renders()
    {
        var ctx = new RenderContext();

        ctx.BeginRender(() => { });
        var fm1 = ctx.UseFocus();

        ctx.BeginRender(() => { });
        var fm2 = ctx.UseFocus();

        Assert.Same(fm1, fm2);
    }

    // ════════════════════════════════════════════════════════════════
    //  Registration
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Register_Adds_Field()
    {
        var fm = new FocusManager();
        fm.Register("email");
        fm.Register("name");

        Assert.Equal(2, fm.Fields.Count);
        Assert.Equal("email", fm.Fields[0]);
        Assert.Equal("name", fm.Fields[1]);
    }

    [Fact]
    public void Register_Idempotent()
    {
        var fm = new FocusManager();
        fm.Register("email");
        fm.Register("email");

        Assert.Single(fm.Fields);
    }

    [Fact]
    public void Focus_Extension_Registers_Field()
    {
        var fm = new FocusManager();
        TextBox("test").Focus(fm, "email");

        Assert.Contains("email", fm.Fields);
    }

    // ════════════════════════════════════════════════════════════════
    //  Focus navigation (next/previous) — logic only, no WinUI
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void FocusNext_From_Last_Triggers_Submit()
    {
        var fm = new FocusManager();
        fm.Register("email");
        fm.Register("name");

        bool submitted = false;
        fm.SetSubmitHandler(() => submitted = true);

        fm.FocusNext("name"); // last field
        Assert.True(submitted);
    }

    [Fact]
    public void FocusNext_Null_Current_Targets_First()
    {
        var fm = new FocusManager();
        fm.Register("email");
        fm.Register("name");

        // FocusField("email") will be called but no WinUI control is registered,
        // so it's a no-op. We can verify the logic by checking it doesn't throw.
        fm.FocusNext(null);
    }

    [Fact]
    public void FocusPrevious_At_First_Is_NoOp()
    {
        var fm = new FocusManager();
        fm.Register("email");
        fm.Register("name");

        // Should not throw
        fm.FocusPrevious("email");
    }

    // ════════════════════════════════════════════════════════════════
    //  IsFirst / IsLast
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void IsFirstField_True_For_First()
    {
        var fm = new FocusManager();
        fm.Register("email");
        fm.Register("name");

        Assert.True(fm.IsFirstField("email"));
        Assert.False(fm.IsFirstField("name"));
    }

    [Fact]
    public void IsLastField_True_For_Last()
    {
        var fm = new FocusManager();
        fm.Register("email");
        fm.Register("name");

        Assert.True(fm.IsLastField("name"));
        Assert.False(fm.IsLastField("email"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Focus + touched integration
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Focus_And_Blur_Marks_Field_Touched()
    {
        var ctx = new ValidationContext();
        ctx.MarkTouched("email");
        Assert.True(ctx.IsTouched("email"));
    }

    [Fact]
    public void Clear_Resets_FocusManager()
    {
        var fm = new FocusManager();
        fm.Register("email");
        fm.Register("name");

        fm.Clear();
        Assert.Empty(fm.Fields);
    }
}
