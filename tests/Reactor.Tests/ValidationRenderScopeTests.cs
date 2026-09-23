using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Controls.Validation.ValidationRuleDsl;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Covers the render-scoped validation path added for issue #1262: <c>.Validate()</c>
/// running eagerly during a render pass, the context being published to the subtree
/// automatically, and the change notification that repaints a form when the context is
/// mutated from an event handler.
/// </summary>
public class ValidationRenderScopeTests
{
    // ════════════════════════════════════════════════════════════════
    //  .Validate() eager execution
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Validate_Outside_A_Render_Pass_Only_Attaches()
    {
        var ctx = new ValidationContext();

        // No scope open — this is an element assembled in an event handler, a cached
        // element, or a headless test. Nothing should reach the context.
        var el = TextBox("").Validate("email", "", Validate.Required());

        Assert.NotNull(el.GetValidation());
        Assert.Empty(ctx.GetAllMessages());
        Assert.Empty(ctx.RegisteredFields);
        Assert.True(ctx.IsValid());
    }

    [Fact]
    public void Validate_Inside_A_Render_Pass_Runs_Validators()
    {
        var ctx = new ValidationContext();

        using (ValidationRenderScope.Begin(ctx))
        {
            _ = TextBox("").Validate("email", "", Validate.Required(), Validate.Email());
        }

        // This is the whole bug: before the fix a bare .Validate() produced nothing, so
        // IsValid() was trivially true and the form submitted.
        Assert.False(ctx.IsValid());
        Assert.Contains("email", ctx.RegisteredFields);
        Assert.Single(ctx.GetMessages("email"));
        Assert.Equal("REQUIRED", ctx.GetMessages("email")[0].Code);
    }

    [Fact]
    public void Validate_Inside_A_Render_Pass_Registers_The_Field_For_MarkAllTouched()
    {
        var ctx = new ValidationContext();

        using (ValidationRenderScope.Begin(ctx))
        {
            _ = TextBox("").Validate("email", "", Validate.Required());
        }

        Assert.False(ctx.IsTouched("email"));
        ctx.MarkAllTouched();
        Assert.True(ctx.IsTouched("email"));
    }

    [Fact]
    public void Validate_Inside_A_Render_Pass_Clears_Messages_When_The_Value_Becomes_Valid()
    {
        var ctx = new ValidationContext();

        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("").Validate("email", "", Validate.Required(), Validate.Email());
        Assert.False(ctx.IsValid());

        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("").Validate("email", "user@example.com", Validate.Required(), Validate.Email());

        Assert.True(ctx.IsValid());
        Assert.Empty(ctx.GetMessages("email"));
    }

    [Fact]
    public void Validate_Without_A_Value_Stays_Attach_Only_Even_Inside_A_Render_Pass()
    {
        var ctx = new ValidationContext();

        using (ValidationRenderScope.Begin(ctx))
        {
            // No value overload — there is nothing to validate against.
            _ = TextBox("").Validate("email", Validate.Required());
        }

        Assert.Empty(ctx.GetAllMessages());
    }

    [Fact]
    public void Scope_Does_Not_Leak_Past_Its_Frame()
    {
        var ctx = new ValidationContext();

        using (ValidationRenderScope.Begin(ctx))
            Assert.Same(ctx, ValidationRenderScope.Current);

        Assert.Null(ValidationRenderScope.Current);
        Assert.False(ValidationRenderScope.InRender);

        // And a .Validate() after the frame closed reaches nothing.
        _ = TextBox("").Validate("late", "", Validate.Required());
        Assert.Empty(ctx.GetMessages("late"));
    }

    [Fact]
    public void Nested_Frames_Restore_The_Enclosing_Context()
    {
        var outer = new ValidationContext();
        var inner = new ValidationContext();

        using (ValidationRenderScope.Begin(outer))
        {
            using (ValidationRenderScope.Begin(inner))
                Assert.Same(inner, ValidationRenderScope.Current);

            Assert.Same(outer, ValidationRenderScope.Current);
        }

        Assert.Null(ValidationRenderScope.Current);
    }

    // ════════════════════════════════════════════════════════════════
    //  Idempotence — the guard against a render loop
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Revalidating_An_Unchanged_Value_Neither_Bumps_Version_Nor_Notifies()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("").Validate("email", "", Validate.Required());

        var versionAfterFirstPass = ctx.Version;

        // Five more render passes over the same value. Because .Validate() now runs on
        // every pass, a version bump or a notification here would feed the re-render
        // subscription and loop forever.
        for (var i = 0; i < 5; i++)
        {
            using (ValidationRenderScope.Begin(ctx))
                _ = TextBox("").Validate("email", "", Validate.Required());
        }

        Assert.Equal(versionAfterFirstPass, ctx.Version);
        Assert.Equal(0, notifications); // suppressed during render anyway
        Assert.Single(ctx.GetMessages("email")); // and not accumulated
    }

    [Fact]
    public void Revalidating_Outside_A_Render_Pass_Notifies_Only_On_A_Real_Change()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        ValidationReconciler.ValidateField(ctx, "email", "", Validate.Required());
        var afterFirst = notifications;
        Assert.True(afterFirst > 0);

        ValidationReconciler.ValidateField(ctx, "email", "", Validate.Required());
        Assert.Equal(afterFirst, notifications);

        ValidationReconciler.ValidateField(ctx, "email", "user@example.com", Validate.Required());
        Assert.True(notifications > afterFirst);
    }

    [Fact]
    public void A_First_Validation_Notifies_Exactly_Once_With_Results_Already_Installed()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        var messagesWhenNotified = -1;

        // Registering the value and installing the verdict used to be three calls, so a
        // subscriber woke up mid-update and re-rendered against the previous pass's
        // messages. Observe what the context looks like at notification time.
        ctx.Changed += () =>
        {
            notifications++;
            messagesWhenNotified = ctx.GetMessages("email").Count;
        };

        ValidationReconciler.ValidateField(ctx, "email", "", Validate.Required(), Validate.MinLength(3));

        Assert.Equal(1, notifications);
        Assert.Equal(2, messagesWhenNotified);
    }

    // ════════════════════════════════════════════════════════════════
    //  Cross-field rules — the other reconcile-time writer
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void A_Failing_Rule_Re_Evaluated_Is_Silent()
    {
        var ctx = new ValidationContext();
        var rule = ValidationRule(() => false, "Passwords must match", "confirm");

        rule.Evaluate(ctx);
        var notifications = 0;
        ctx.Changed += () => notifications++;
        var versionAfterFirst = ctx.Version;

        // ValidationRule mounts/updates evaluate during reconcile, outside any render
        // scope. Clear-then-add raised Changed on every pass, and each notification
        // drove another render — the reconciler tripped its re-entrancy limit.
        for (var i = 0; i < 5; i++)
            rule.Evaluate(ctx);

        Assert.Equal(0, notifications);
        Assert.Equal(versionAfterFirst, ctx.Version);
        Assert.Single(ctx.GetMessages("confirm"));
    }

    [Fact]
    public void A_Rule_Flipping_Verdict_Notifies_Each_Way()
    {
        var ctx = new ValidationContext();
        var passing = true;
        var rule = ValidationRule(() => passing, "Passwords must match", "confirm");

        var notifications = 0;
        ctx.Changed += () => notifications++;

        rule.Evaluate(ctx);
        Assert.Equal(0, notifications);   // passing, nothing to record
        Assert.True(ctx.IsValid());

        passing = false;
        rule.Evaluate(ctx);
        Assert.Equal(1, notifications);
        Assert.False(ctx.IsValid());

        passing = true;
        rule.Evaluate(ctx);
        Assert.Equal(2, notifications);
        Assert.True(ctx.IsValid());
    }

    [Fact]
    public void Changed_Is_Suppressed_While_A_Render_Pass_Is_In_Flight()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        using (ValidationRenderScope.Begin(ctx))
        {
            ctx.Add("email", "boom");
            ctx.MarkTouched("email");
        }

        // The rendering component reads the new state later in the same pass, so
        // notifying would only re-enter requestRerender from inside Render().
        Assert.Equal(0, notifications);

        ctx.MarkTouched("password");
        Assert.Equal(1, notifications);
    }

    // ════════════════════════════════════════════════════════════════
    //  Change notification
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void MarkAllTouched_Notifies_Once_And_Stays_Quiet_When_Already_Touched()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");
        ctx.RegisterField("password");

        var notifications = 0;
        ctx.Changed += () => notifications++;

        ctx.MarkAllTouched();
        Assert.Equal(1, notifications);

        // This is the submit-twice case. Without the real-change gate it would bump the
        // version and repaint on every click forever.
        ctx.MarkAllTouched();
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void MarkAllTouched_With_No_Registered_Fields_Is_Silent()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        ctx.MarkAllTouched();

        Assert.Equal(0, notifications);
        Assert.Equal(0, ctx.Version);
    }

    [Fact]
    public void ClearAll_Notifies_Only_When_There_Was_Something_To_Clear()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        ctx.ClearAll();
        Assert.Equal(0, notifications);

        ctx.Add("email", "boom");
        var afterAdd = notifications;
        ctx.ClearAll();
        Assert.Equal(afterAdd + 1, notifications);
    }

    // ════════════════════════════════════════════════════════════════
    //  External messages vs. per-render validation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void External_Messages_Survive_Revalidation_Of_An_Unchanged_Value()
    {
        var ctx = new ValidationContext();

        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("").Validate("email", "taken@example.com", Validate.Required());

        ctx.AddExternal("email", "Email already registered");

        // A repaint for any unrelated reason re-runs .Validate(). Before the fix this
        // called NotifyValueChanged unconditionally and wiped the server's verdict
        // before the user could read it.
        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("").Validate("email", "taken@example.com", Validate.Required());

        Assert.Single(ctx.GetMessages("email"));
        Assert.Equal("Email already registered", ctx.GetMessages("email")[0].Text);
        Assert.False(ctx.IsValid());
    }

    [Fact]
    public void External_Messages_Clear_Once_The_Value_Actually_Changes()
    {
        var ctx = new ValidationContext();

        ctx.NotifyValueChanged("email", "taken@example.com");
        ctx.AddExternal("email", "Email already registered");
        Assert.Single(ctx.GetMessages("email"));

        ctx.NotifyValueChanged("email", "fresh@example.com");

        Assert.Empty(ctx.GetMessages("email"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Auto-provide
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void A_Local_Context_Is_Published_To_The_Rendered_Subtree()
    {
        var render = new RenderContext();
        Element rendered;
        ValidationContext resolved;

        using (ValidationRenderScope.Begin(null))
        {
            render.BeginRender(() => { });
            resolved = render.UseValidationContext();
            rendered = ValidationRenderScope.ApplyProvide(TextBlock("body"));
        }

        Assert.NotNull(rendered.ContextValues);
        Assert.Same(resolved, rendered.ContextValues![ValidationContexts.Current]);
    }

    [Fact]
    public void An_Explicit_Provide_Reaches_Descendants_Not_The_Providers_Own_Eager_Validation()
    {
        var render = new RenderContext();
        var mine = new ValidationContext();
        Element rendered;
        ValidationContext resolved;

        using (ValidationRenderScope.Begin(null))
        {
            render.BeginRender(() => { });
            resolved = render.UseValidationContext();

            // A bare control — nothing re-validates it later, so where this lands is
            // observable rather than masked by FormField's reconcile-time pass.
            var body = TextBox("").Validate("email", "", Validate.Required());
            rendered = ValidationRenderScope.ApplyProvide(
                body.Provide(ValidationContexts.Current, mine));
        }

        // `.Provide` publishes to the SUBTREE. The providing component's own eager
        // .Validate() already ran while the tree was being built, against the context
        // its own hook resolved — matching how UseContext cannot see a value the same
        // component provides.
        Assert.False(resolved.IsValid());
        Assert.Single(resolved.GetMessages("email"));
        Assert.True(mine.IsValid());
        Assert.Empty(mine.GetAllMessages());

        // ...and the explicit value is what descendants will read.
        Assert.Same(mine, rendered.ContextValues![ValidationContexts.Current]);
    }

    [Fact]
    public void An_Explicit_Provide_Is_What_A_Descendant_Resolves()
    {
        var mine = new ValidationContext();
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [ValidationContexts.Current] = mine });

        // The descendant renders inside the provided scope, so its hook resolves `mine`
        // and its eager .Validate() lands there.
        var child = new RenderContext();
        using (ValidationRenderScope.Begin(mine))
        {
            child.BeginRender(() => { }, scope);
            Assert.Same(mine, child.UseValidationContext());
            _ = TextBox("").Validate("email", "", Validate.Required());
        }

        Assert.False(mine.IsValid());
        Assert.Single(mine.GetMessages("email"));
    }

    [Fact]
    public void An_Inherited_Context_Is_Not_Re_Provided()
    {
        var parent = new ValidationContext();
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [ValidationContexts.Current] = parent });

        var render = new RenderContext();
        Element rendered;

        using (ValidationRenderScope.Begin(parent))
        {
            render.BeginRender(() => { }, scope);
            Assert.Same(parent, render.UseValidationContext());
            rendered = ValidationRenderScope.ApplyProvide(TextBlock("body"));
        }

        // The ancestor already provides it; re-providing would only add allocation.
        Assert.Null(rendered.ContextValues);
    }

    [Fact]
    public void UseValidationContext_Publishes_Its_Result_To_The_Active_Scope()
    {
        var render = new RenderContext();

        using (ValidationRenderScope.Begin(null))
        {
            render.BeginRender(() => { });
            var resolved = render.UseValidationContext();

            Assert.Same(resolved, ValidationRenderScope.Current);

            // And from here on .Validate() in the same pass reaches it.
            _ = TextBox("").Validate("email", "", Validate.Required());
            Assert.False(resolved.IsValid());
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Re-render subscription
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Mutating_The_Context_From_Outside_A_Render_Requests_A_Rerender()
    {
        var render = new RenderContext();
        var rerenders = 0;

        render.BeginRender(() => rerenders++);
        var ctx = render.UseValidationContext();
        render.FlushEffects();
        ctx.RegisterField("email");

        var before = rerenders;

        // The submit handler in the docs example does exactly this and nothing else —
        // no state setter runs, so without the subscription nothing repaints.
        ctx.MarkAllTouched();

        Assert.True(rerenders > before);
    }

    [Fact]
    public void The_Rerender_Subscription_Is_Released_When_The_Component_Unmounts()
    {
        var render = new RenderContext();
        var rerenders = 0;

        render.BeginRender(() => rerenders++);
        var ctx = render.UseValidationContext();
        render.FlushEffects();
        ctx.RegisterField("email");

        render.RunCleanups();
        var after = rerenders;

        ctx.MarkAllTouched();

        Assert.Equal(after, rerenders);
    }
}
