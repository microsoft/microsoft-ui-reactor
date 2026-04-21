using Microsoft.UI.Reactor.Controls.Validation;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class ValidationContextTests
{
    // ════════════════════════════════════════════════════════════════
    //  Add / Query messages
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Add_And_GetMessages_Returns_Message()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required");

        var msgs = ctx.GetMessages("email");
        Assert.Single(msgs);
        Assert.Equal("Required", msgs[0].Text);
        Assert.Equal(Severity.Error, msgs[0].Severity);
    }

    [Fact]
    public void Add_Multiple_Messages_Per_Field()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required");
        ctx.Add("email", "Invalid format", Severity.Warning);

        var msgs = ctx.GetMessages("email");
        Assert.Equal(2, msgs.Count);
        Assert.Equal("Required", msgs[0].Text);
        Assert.Equal("Invalid format", msgs[1].Text);
    }

    [Fact]
    public void GetMessages_Returns_Empty_For_Unknown_Field()
    {
        var ctx = new ValidationContext();
        var msgs = ctx.GetMessages("nonexistent");
        Assert.Empty(msgs);
    }

    [Fact]
    public void GetAllMessages_Returns_All_Fields()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required");
        ctx.Add("name", "Too short");
        ctx.Add("email", "Invalid");

        var all = ctx.GetAllMessages();
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void GetAllMessages_Empty_Context()
    {
        var ctx = new ValidationContext();
        Assert.Empty(ctx.GetAllMessages());
    }

    // ════════════════════════════════════════════════════════════════
    //  Clear
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Clear_Removes_Messages_For_Field()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required");
        ctx.Add("name", "Required");

        ctx.Clear("email");

        Assert.Empty(ctx.GetMessages("email"));
        Assert.Single(ctx.GetMessages("name"));
    }

    [Fact]
    public void Clear_Nonexistent_Field_Is_Noop()
    {
        var ctx = new ValidationContext();
        ctx.Clear("ghost"); // should not throw
        Assert.Empty(ctx.GetAllMessages());
    }

    [Fact]
    public void ClearAll_Removes_Everything()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required");
        ctx.Add("name", "Required");
        ctx.AddExternal("email", "Already taken");

        ctx.ClearAll();

        Assert.Empty(ctx.GetAllMessages());
        Assert.True(ctx.IsValid());
    }

    // ════════════════════════════════════════════════════════════════
    //  HasError / HasMessages
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void HasError_True_When_Error_Present()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required", Severity.Error);
        Assert.True(ctx.HasError("email"));
    }

    [Fact]
    public void HasError_False_When_Only_Warning()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Looks suspicious", Severity.Warning);
        Assert.False(ctx.HasError("email"));
    }

    [Fact]
    public void HasError_False_When_No_Messages()
    {
        var ctx = new ValidationContext();
        Assert.False(ctx.HasError("email"));
    }

    [Fact]
    public void HasMessages_True_For_Any_Severity()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "hint", Severity.Info);
        Assert.True(ctx.HasMessages("email"));
    }

    [Fact]
    public void HasMessages_False_When_Empty()
    {
        var ctx = new ValidationContext();
        Assert.False(ctx.HasMessages("email"));
    }

    // ════════════════════════════════════════════════════════════════
    //  HighestSeverity
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void HighestSeverity_Returns_Error_When_Mixed()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "info", Severity.Info);
        ctx.Add("f", "warning", Severity.Warning);
        ctx.Add("f", "error", Severity.Error);
        Assert.Equal(Severity.Error, ctx.HighestSeverity("f"));
    }

    [Fact]
    public void HighestSeverity_Returns_Warning_When_No_Error()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "info", Severity.Info);
        ctx.Add("f", "warning", Severity.Warning);
        Assert.Equal(Severity.Warning, ctx.HighestSeverity("f"));
    }

    [Fact]
    public void HighestSeverity_Returns_Null_When_No_Messages()
    {
        var ctx = new ValidationContext();
        Assert.Null(ctx.HighestSeverity("f"));
    }

    // ════════════════════════════════════════════════════════════════
    //  IsValid / InvalidFields
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void IsValid_True_When_No_Messages()
    {
        var ctx = new ValidationContext();
        Assert.True(ctx.IsValid());
    }

    [Fact]
    public void IsValid_True_When_Only_Warnings()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "warning", Severity.Warning);
        ctx.Add("f2", "info", Severity.Info);
        Assert.True(ctx.IsValid());
    }

    [Fact]
    public void IsValid_False_When_Any_Error()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "ok", Severity.Warning);
        ctx.Add("f2", "bad", Severity.Error);
        Assert.False(ctx.IsValid());
    }

    [Fact]
    public void InvalidFields_Returns_Fields_With_Errors()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required", Severity.Error);
        ctx.Add("name", "Suggestion", Severity.Warning);
        ctx.Add("age", "Out of range", Severity.Error);

        var invalid = ctx.InvalidFields;
        Assert.Equal(2, invalid.Count);
        Assert.Contains("email", invalid);
        Assert.Contains("age", invalid);
        Assert.DoesNotContain("name", invalid);
    }

    [Fact]
    public void InvalidFields_Empty_When_Valid()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "warning", Severity.Warning);
        Assert.Empty(ctx.InvalidFields);
    }

    // ════════════════════════════════════════════════════════════════
    //  Version tracking
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Version_Increments_On_Add()
    {
        var ctx = new ValidationContext();
        var v0 = ctx.Version;
        ctx.Add("f", "msg");
        Assert.True(ctx.Version > v0);
    }

    [Fact]
    public void Version_Increments_On_Clear()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "msg");
        var v1 = ctx.Version;
        ctx.Clear("f");
        Assert.True(ctx.Version > v1);
    }

    [Fact]
    public void Version_Increments_On_ClearAll()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "msg");
        var v1 = ctx.Version;
        ctx.ClearAll();
        Assert.True(ctx.Version > v1);
    }

    // ════════════════════════════════════════════════════════════════
    //  External messages
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void AddExternal_Messages_Appear_In_GetMessages()
    {
        var ctx = new ValidationContext();
        ctx.AddExternal("email", "Already taken");

        var msgs = ctx.GetMessages("email");
        Assert.Single(msgs);
        Assert.Equal("Already taken", msgs[0].Text);
    }

    [Fact]
    public void External_And_Internal_Messages_Combined()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required");
        ctx.AddExternal("email", "Already taken");

        var msgs = ctx.GetMessages("email");
        Assert.Equal(2, msgs.Count);
    }

    [Fact]
    public void External_Messages_Cleared_On_Value_Change()
    {
        var ctx = new ValidationContext();
        ctx.AddExternal("email", "Already taken");
        ctx.NotifyValueChanged("email", "new@example.com");

        Assert.Empty(ctx.GetMessages("email"));
    }

    [Fact]
    public void Internal_Messages_Preserved_On_Value_Change()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required");
        ctx.NotifyValueChanged("email", "new@example.com");

        // Internal messages are managed by validators, not cleared by value change
        Assert.Single(ctx.GetMessages("email"));
    }

    [Fact]
    public void ClearExternal_Only_Removes_External()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required");
        ctx.AddExternal("email", "Already taken");

        ctx.ClearExternal("email");

        var msgs = ctx.GetMessages("email");
        Assert.Single(msgs);
        Assert.Equal("Required", msgs[0].Text);
    }

    [Fact]
    public void Clear_Removes_Both_Internal_And_External()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required");
        ctx.AddExternal("email", "Already taken");

        ctx.Clear("email");
        Assert.Empty(ctx.GetMessages("email"));
    }

    // ════════════════════════════════════════════════════════════════
    //  HasError includes external messages
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void HasError_Detects_External_Errors()
    {
        var ctx = new ValidationContext();
        ctx.AddExternal("email", "Server says no", Severity.Error);
        Assert.True(ctx.HasError("email"));
    }

    [Fact]
    public void IsValid_False_With_External_Errors()
    {
        var ctx = new ValidationContext();
        ctx.AddExternal("email", "Taken", Severity.Error);
        Assert.False(ctx.IsValid());
    }

    [Fact]
    public void InvalidFields_Includes_External_Error_Fields()
    {
        var ctx = new ValidationContext();
        ctx.AddExternal("email", "Taken", Severity.Error);
        Assert.Contains("email", ctx.InvalidFields);
    }

    // ════════════════════════════════════════════════════════════════
    //  Mixed severities per field
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Multiple_Messages_Mixed_Severities()
    {
        var ctx = new ValidationContext();
        ctx.Add("password", "Too short", Severity.Error);
        ctx.Add("password", "Consider adding numbers", Severity.Info);
        ctx.Add("password", "Weak", Severity.Warning);

        var msgs = ctx.GetMessages("password");
        Assert.Equal(3, msgs.Count);
        Assert.True(ctx.HasError("password"));
        Assert.Equal(Severity.Error, ctx.HighestSeverity("password"));
        Assert.False(ctx.IsValid());
    }

    // ════════════════════════════════════════════════════════════════
    //  Field registration
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void RegisterField_And_Query()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");
        ctx.RegisterField("name");
        Assert.Contains("email", ctx.RegisteredFields);
        Assert.Contains("name", ctx.RegisteredFields);
        Assert.Equal(2, ctx.RegisteredFields.Count);
    }

    [Fact]
    public void RegisterField_Idempotent()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");
        ctx.RegisterField("email");
        Assert.Single(ctx.RegisteredFields);
    }

    // ════════════════════════════════════════════════════════════════
    //  ClearInternal
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ClearInternal_Preserves_External()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Required");
        ctx.AddExternal("email", "Taken");
        ctx.ClearInternal("email");
        var msgs = ctx.GetMessages("email");
        Assert.Single(msgs);
        Assert.Equal("Taken", msgs[0].Text);
    }

    [Fact]
    public void ClearInternal_NonExistent_No_Version_Bump()
    {
        var ctx = new ValidationContext();
        var v0 = ctx.Version;
        ctx.ClearInternal("nonexistent");
        Assert.Equal(v0, ctx.Version);
    }

    [Fact]
    public void ClearExternal_NonExistent_No_Version_Bump()
    {
        var ctx = new ValidationContext();
        var v0 = ctx.Version;
        ctx.ClearExternal("nonexistent");
        Assert.Equal(v0, ctx.Version);
    }

    // ════════════════════════════════════════════════════════════════
    //  HighestSeverity with external messages
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void HighestSeverity_Considers_External()
    {
        var ctx = new ValidationContext();
        ctx.Add("email", "Hmm", Severity.Info);
        ctx.AddExternal("email", "Nope", Severity.Error);
        Assert.Equal(Severity.Error, ctx.HighestSeverity("email"));
    }

    [Fact]
    public void HighestSeverity_Info_Only()
    {
        var ctx = new ValidationContext();
        ctx.Add("f", "msg", Severity.Info);
        Assert.Equal(Severity.Info, ctx.HighestSeverity("f"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Touched state
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void IsTouched_False_Initially()
    {
        var ctx = new ValidationContext();
        Assert.False(ctx.IsTouched("email"));
    }

    [Fact]
    public void MarkTouched_Sets_Touched()
    {
        var ctx = new ValidationContext();
        ctx.MarkTouched("email");
        Assert.True(ctx.IsTouched("email"));
    }

    [Fact]
    public void MarkTouched_Idempotent_No_Double_Version_Bump()
    {
        var ctx = new ValidationContext();
        ctx.MarkTouched("email");
        var v1 = ctx.Version;
        ctx.MarkTouched("email");
        Assert.Equal(v1, ctx.Version);
    }

    [Fact]
    public void MarkAllTouched_Touches_All_Registered()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");
        ctx.RegisterField("name");
        ctx.MarkAllTouched();
        Assert.True(ctx.IsTouched("email"));
        Assert.True(ctx.IsTouched("name"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Dirty state
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void IsDirty_False_After_SetInitialValue()
    {
        var ctx = new ValidationContext();
        ctx.SetInitialValue("email", "test@example.com");
        Assert.False(ctx.IsDirty("email"));
    }

    [Fact]
    public void IsDirty_True_After_ValueChange()
    {
        var ctx = new ValidationContext();
        ctx.SetInitialValue("email", "old@test.com");
        ctx.NotifyValueChanged("email", "new@test.com");
        Assert.True(ctx.IsDirty("email"));
    }

    [Fact]
    public void IsDirty_False_After_Revert_To_Initial()
    {
        var ctx = new ValidationContext();
        ctx.SetInitialValue("email", "same@test.com");
        ctx.NotifyValueChanged("email", "different");
        ctx.NotifyValueChanged("email", "same@test.com");
        Assert.False(ctx.IsDirty("email"));
    }

    [Fact]
    public void IsDirty_Field_Without_InitialValue_Returns_False()
    {
        var ctx = new ValidationContext();
        Assert.False(ctx.IsDirty("unknown"));
    }

    [Fact]
    public void IsDirty_Global_False_When_All_Clean()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");
        ctx.SetInitialValue("email", "test");
        Assert.False(ctx.IsDirty());
    }

    [Fact]
    public void IsDirty_Global_True_When_Any_Dirty()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");
        ctx.RegisterField("name");
        ctx.SetInitialValue("email", "old");
        ctx.SetInitialValue("name", "Bob");
        ctx.NotifyValueChanged("email", "new");
        Assert.True(ctx.IsDirty());
    }

    [Fact]
    public void NotifyValueChanged_Clears_External_Messages()
    {
        var ctx = new ValidationContext();
        ctx.AddExternal("email", "Already taken");
        ctx.NotifyValueChanged("email", "different@email.com");
        Assert.Empty(ctx.GetMessages("email"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Reset
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Reset_Single_Field_Restores_Initial_Value()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");
        ctx.SetInitialValue("email", "initial@test.com");
        ctx.NotifyValueChanged("email", "changed");
        ctx.MarkTouched("email");
        ctx.Add("email", "Invalid");

        var result = ctx.Reset("email");
        Assert.Equal("initial@test.com", result);
        Assert.False(ctx.IsTouched("email"));
        Assert.Empty(ctx.GetMessages("email"));
        Assert.False(ctx.IsDirty("email"));
    }

    [Fact]
    public void Reset_Field_Without_InitialValue()
    {
        var ctx = new ValidationContext();
        var result = ctx.Reset("unknown");
        Assert.Null(result);
    }

    [Fact]
    public void ResetAll_Restores_Everything()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");
        ctx.RegisterField("name");
        ctx.SetInitialValue("email", "a@b.com");
        ctx.SetInitialValue("name", "Alice");
        ctx.NotifyValueChanged("email", "changed");
        ctx.MarkTouched("email");
        ctx.Add("email", "Bad");
        ctx.AddExternal("name", "Taken");

        var result = ctx.ResetAll();
        Assert.Equal("a@b.com", result["email"]);
        Assert.Equal("Alice", result["name"]);
        Assert.False(ctx.IsTouched("email"));
        Assert.Empty(ctx.GetAllMessages());
        Assert.False(ctx.IsDirty());
    }

    // ════════════════════════════════════════════════════════════════
    //  Version tracking for non-message mutations
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Version_Increments_On_Touch_And_Reset()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("f");
        ctx.SetInitialValue("f", "v");

        var v0 = ctx.Version;
        ctx.MarkTouched("f");
        Assert.True(ctx.Version > v0);

        var v1 = ctx.Version;
        ctx.Reset("f");
        Assert.True(ctx.Version > v1);

        ctx.SetInitialValue("f", "v2");
        var v2 = ctx.Version;
        ctx.ResetAll();
        Assert.True(ctx.Version > v2);
    }

    [Fact]
    public void Version_Increments_On_MarkAllTouched()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");
        var v0 = ctx.Version;
        ctx.MarkAllTouched();
        Assert.True(ctx.Version > v0);
    }
}
