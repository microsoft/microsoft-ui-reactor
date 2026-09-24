using Microsoft.UI.Reactor.Controls.Validation;
using static Microsoft.UI.Reactor.Controls.Validation.ValidationRuleDsl;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class ValidationRuleTests
{
    // ════════════════════════════════════════════════════════════════
    //  Sync validation rule
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ValidationRule_Creates_Element()
    {
        var rule = ValidationRule(() => true, "Passwords must match", "confirmPassword");
        Assert.IsType<ValidationRuleElement>(rule);
        Assert.Equal("confirmPassword", rule.Field);
        Assert.Equal("Passwords must match", rule.Message);
    }

    [Fact]
    public void ValidationRule_Fires_When_Predicate_Fails()
    {
        var password = "abc";
        var confirm = "xyz";

        var ctx = new ValidationContext();
        var rule = ValidationRule(
            () => password == confirm,
            "Passwords must match",
            "confirmPassword");

        rule.Evaluate(ctx);

        Assert.False(ctx.IsValid());
        var msgs = ctx.GetMessages("confirmPassword");
        Assert.Single(msgs);
        Assert.Equal("Passwords must match", msgs[0].Text);
    }

    [Fact]
    public void ValidationRule_Clears_When_Predicate_Passes()
    {
        var ctx = new ValidationContext();
        var password = "abc";
        var confirm = "xyz";

        var rule = ValidationRule(
            () => password == confirm,
            "Passwords must match",
            "confirmPassword");

        // First evaluation: fails
        rule.Evaluate(ctx);
        Assert.Single(ctx.GetMessages("confirmPassword"));

        // Fix the values
        confirm = "abc";
        rule.Evaluate(ctx);

        // Should be cleared now
        Assert.Empty(ctx.GetMessages("confirmPassword"));
        Assert.True(ctx.IsValid());
    }

    [Fact]
    public void ValidationRule_Associated_With_Correct_Field()
    {
        var ctx = new ValidationContext();
        var rule = ValidationRule(() => false, "End must be after start", "endDate");

        rule.Evaluate(ctx);

        var msgs = ctx.GetMessages("endDate");
        Assert.Single(msgs);
        Assert.Equal("endDate", msgs[0].Field);

        // Other fields unaffected
        Assert.Empty(ctx.GetMessages("startDate"));
    }

    [Fact]
    public void ValidationRule_Default_Severity_Is_Error()
    {
        var rule = ValidationRule(() => false, "msg", "f");
        Assert.Equal(Severity.Error, rule.Severity);
    }

    [Fact]
    public void ValidationRule_Custom_Severity()
    {
        var ctx = new ValidationContext();
        var rule = ValidationRule(() => false, "Consider matching", "f", Severity.Warning);

        rule.Evaluate(ctx);

        var msgs = ctx.GetMessages("f");
        Assert.Equal(Severity.Warning, msgs[0].Severity);
    }

    [Fact]
    public void ValidationRule_Does_Not_Duplicate_On_Multiple_Evaluations()
    {
        var ctx = new ValidationContext();
        var rule = ValidationRule(() => false, "Error", "f");

        rule.Evaluate(ctx);
        rule.Evaluate(ctx);
        rule.Evaluate(ctx);

        // ClearInternal is called before each Add, so only 1 message
        Assert.Single(ctx.GetMessages("f"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Async validation rule
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AsyncValidationRule_Fires_When_Predicate_Fails()
    {
        var ctx = new ValidationContext();
        var rule = ValidationRuleAsync(
            async () => { await Task.Yield(); return false; },
            "Username taken",
            "username");

        await rule.EvaluateAsync(ctx, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(ctx.GetMessages("username"));
    }

    [Fact]
    public async Task AsyncValidationRule_Clears_When_Passes()
    {
        var ctx = new ValidationContext();
        var shouldFail = true;

        var rule = ValidationRuleAsync(
            async () => { await Task.Yield(); return !shouldFail; },
            "Username taken",
            "username");

        await rule.EvaluateAsync(ctx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Single(ctx.GetMessages("username"));

        shouldFail = false;
        await rule.EvaluateAsync(ctx, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(ctx.GetMessages("username"));
    }

    [Fact]
    public async Task AsyncValidationRule_Respects_Cancellation()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var ctx = new ValidationContext();
        var rule = ValidationRuleAsync(
            async () => { await Task.Delay(5000); return false; },
            "msg",
            "f");

        // Cancellation surfaces as an OperationCanceledException; the exact subclass
        // depends on how the wait was cancelled, so don't pin it.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            rule.EvaluateAsync(ctx, cts.Token));
    }

    // ════════════════════════════════════════════════════════════════
    //  Multiple rules
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Multiple_Rules_Different_Fields()
    {
        var ctx = new ValidationContext();

        var rule1 = ValidationRule(() => false, "Dates must be valid", "startDate");
        var rule2 = ValidationRule(() => false, "Budget exceeded", "total");

        rule1.Evaluate(ctx);
        rule2.Evaluate(ctx);

        Assert.Equal(2, ctx.GetAllMessages().Count);
        Assert.Single(ctx.GetMessages("startDate"));
        Assert.Single(ctx.GetMessages("total"));
    }
}
