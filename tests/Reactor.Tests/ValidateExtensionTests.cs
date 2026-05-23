using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class ValidateExtensionTests
{
    // ════════════════════════════════════════════════════════════════
    //  .Validate() on TextBox
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_On_TextBox_Attaches_Validators()
    {
        var el = TextBox("test")
            .Validate("email", Validate.Required(), Validate.Email());

        var attached = el.GetValidation();
        Assert.NotNull(attached);
        Assert.Equal("email", attached!.FieldName);
        Assert.Equal(2, attached.Validators.Length);
    }

    [Fact]
    public void Validate_On_TextBox_Preserves_Element_Type()
    {
        var el = TextBox("test")
            .Validate("email", Validate.Required());

        Assert.IsType<TextBoxElement>(el);
        Assert.Equal("test", el.Value);
    }

    // ════════════════════════════════════════════════════════════════
    //  .Validate() on NumberBox
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_On_NumberBox()
    {
        var el = NumberBox(42.0)
            .Validate("age", Validate.Range(0, 120));

        var attached = el.GetValidation();
        Assert.NotNull(attached);
        Assert.Equal("age", attached!.FieldName);
        Assert.Single(attached.Validators);
    }

    // ════════════════════════════════════════════════════════════════
    //  .Validate() on CheckBox
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_On_CheckBox()
    {
        var el = CheckBox(false, label: "Accept Terms")
            .Validate("terms", Validate.MustBeTrue("You must accept the terms"));

        var attached = el.GetValidation();
        Assert.NotNull(attached);
        Assert.Equal("terms", attached!.FieldName);
    }

    // ════════════════════════════════════════════════════════════════
    //  Validator chaining
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_Can_Chain_With_Other_Modifiers()
    {
        var el = TextBox("test")
            .Validate("email", Validate.Required())
            .Margin(10)
            .Width(200);

        Assert.IsType<TextBoxElement>(el);
        Assert.NotNull(el.GetValidation());
        Assert.NotNull(el.Modifiers);
        Assert.Equal(200, el.Modifiers!.Width);
    }

    [Fact]
    public void Validate_Merges_When_Called_Twice()
    {
        var el = TextBox("test")
            .Validate("email", Validate.Required())
            .Validate("email", Validate.Email());

        var attached = el.GetValidation();
        Assert.Equal(2, attached!.Validators.Length);
    }

    // ════════════════════════════════════════════════════════════════
    //  .ValidateAsync()
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidateAsync_Attaches_Async_Validators()
    {
        var asyncValidator = Validate.MustAsync<string>(async s =>
        {
            await Task.Yield();
            return s.Length > 0;
        }, "Required");

        var el = TextBox("test")
            .ValidateAsync("email", asyncValidator);

        var attached = el.GetValidation();
        Assert.NotNull(attached);
        Assert.Single(attached!.AsyncValidators);
        Assert.Empty(attached.Validators);
    }

    [Fact]
    public void Validate_And_ValidateAsync_Can_Combine()
    {
        var asyncValidator = Validate.MustAsync<string>(async s =>
        {
            await Task.Yield();
            return true;
        }, "Async check");

        var el = TextBox("test")
            .Validate("email", Validate.Required())
            .ValidateAsync("email", asyncValidator);

        var attached = el.GetValidation();
        Assert.Single(attached!.Validators);
        Assert.Single(attached.AsyncValidators);
    }

    // ════════════════════════════════════════════════════════════════
    //  RunValidators
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void RunValidators_Returns_Messages_On_Failure()
    {
        var attached = new ValidationAttached("email",
            [Validate.Required(), Validate.Email()], []);

        var messages = attached.RunValidators("");
        Assert.Single(messages); // Required fails, Email skips empty
        Assert.Equal("REQUIRED", messages[0].Code);
    }

    [Fact]
    public void RunValidators_Returns_Multiple_Messages()
    {
        var attached = new ValidationAttached("password",
            [Validate.Required(), Validate.MinLength(8)], []);

        var messages = attached.RunValidators("ab");
        // Required passes ("ab" is not empty), MinLength fails
        Assert.Single(messages);
        Assert.Equal("MIN_LENGTH", messages[0].Code);
    }

    [Fact]
    public void RunValidators_Returns_Empty_When_Valid()
    {
        var attached = new ValidationAttached("email",
            [Validate.Required(), Validate.Email()], []);

        var messages = attached.RunValidators("user@example.com");
        Assert.Empty(messages);
    }

    [Fact]
    public void RunValidators_Sets_Correct_Field_Name()
    {
        var attached = new ValidationAttached("myField",
            [Validate.Required()], []);

        var messages = attached.RunValidators(null);
        Assert.Equal("myField", messages[0].Field);
    }

    // ════════════════════════════════════════════════════════════════
    //  RunAsyncValidators
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RunAsyncValidators_Returns_Messages_On_Failure()
    {
        var asyncValidator = Validate.MustAsync<string>(async s =>
        {
            await Task.Yield();
            return s == "taken@test.com" ? false : true;
        }, "Email already taken");

        var attached = new ValidationAttached("email", [],
            [asyncValidator]);

        var messages = await attached.RunAsyncValidators("taken@test.com");
        Assert.Single(messages);
        Assert.Equal("Email already taken", messages[0].Text);
    }

    [Fact]
    public async Task RunAsyncValidators_Returns_Empty_When_Valid()
    {
        var asyncValidator = Validate.MustAsync<string>(async s =>
        {
            await Task.Yield();
            return true;
        }, "msg");

        var attached = new ValidationAttached("email", [],
            [asyncValidator]);

        var messages = await attached.RunAsyncValidators("good@test.com");
        Assert.Empty(messages);
    }

    [Fact]
    public async Task RunAsyncValidators_Respects_Cancellation()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var asyncValidator = Validate.MustAsync<string>(async s =>
        {
            await Task.Delay(5000);
            return true;
        }, "msg");

        var attached = new ValidationAttached("email", [],
            [asyncValidator]);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            attached.RunAsyncValidators("test", cts.Token));
    }

    // ════════════════════════════════════════════════════════════════
    //  GetValidation returns null when not set
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetValidation_Returns_Null_When_No_Validators()
    {
        var el = TextBox("test");
        Assert.Null(el.GetValidation());
    }
}
