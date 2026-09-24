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
        var notificationsAfterFirstPass = notifications;

        // Five more render passes over the same value. Because .Validate() now runs on
        // every pass, a version bump or a notification here would feed the re-render
        // subscription and loop forever.
        for (var i = 0; i < 5; i++)
        {
            using (ValidationRenderScope.Begin(ctx))
                _ = TextBox("").Validate("email", "", Validate.Required());
        }

        Assert.Equal(versionAfterFirstPass, ctx.Version);
        Assert.Equal(notificationsAfterFirstPass, notifications);
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
    public void The_Per_Render_Seed_Then_Notify_Pattern_Settles()
    {
        // DirtyResetDemo's shape: register, re-seed the baseline and re-notify the
        // current value on *every* render. SetInitialValue used to rewind the current
        // value, so once the user had typed, the rewind and the re-notify took turns
        // and the subscription repainted forever.
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        void RenderPass(string typed)
        {
            ctx.RegisterField("name");
            ctx.SetInitialValue("name", "John Doe");
            ctx.NotifyValueChanged("name", typed);
        }

        RenderPass("John Doe");
        Assert.Equal(0, notifications);         // nothing moved on first paint
        Assert.False(ctx.IsDirty("name"));

        RenderPass("John Doex");                // user typed
        var afterEdit = notifications;
        Assert.Equal(1, afterEdit);
        Assert.True(ctx.IsDirty("name"));

        // Every subsequent repaint over the same value must be silent.
        for (var i = 0; i < 5; i++) RenderPass("John Doex");

        Assert.Equal(afterEdit, notifications);
        Assert.True(ctx.IsDirty("name"));
    }

    [Fact]
    public void Re_Seeding_An_Initial_Value_Does_Not_Rewind_The_Current_One()
    {
        var ctx = new ValidationContext();
        ctx.SetInitialValue("name", "John Doe");
        ctx.NotifyValueChanged("name", "edited");

        ctx.SetInitialValue("name", "John Doe");

        Assert.True(ctx.IsDirty("name"));
    }

    [Fact]
    public void Re_Baselining_A_Dirty_Field_Notifies()
    {
        var ctx = new ValidationContext();
        ctx.SetInitialValue("name", "a");
        ctx.NotifyValueChanged("name", "b");
        Assert.True(ctx.IsDirty("name"));

        var notifications = 0;
        ctx.Changed += () => notifications++;

        // Adopting the edited value as the new baseline flips IsDirty with no message
        // or touched-state change, so subscribers would otherwise stay stale.
        ctx.SetInitialValue("name", "b");

        Assert.False(ctx.IsDirty("name"));
        Assert.Equal(1, notifications);

        // ...and a re-seed that changes nothing observable stays quiet.
        ctx.SetInitialValue("name", "b");
        Assert.Equal(1, notifications);
    }

    // ════════════════════════════════════════════════════════════════
    //  Async validators — one atomic install, no duplicates
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Async_Validators_Install_As_One_Notification()
    {
        var ctx = new ValidationContext();
        var seen = new List<int>();
        ctx.Changed += () => seen.Add(ctx.GetMessages("username").Count);

        var validators = new[]
        {
            Validate.MustAsync<string>(_ => global::System.Threading.Tasks.Task.FromResult(false), "Reserved"),
            Validate.MustAsync<string>(_ => global::System.Threading.Tasks.Task.FromResult(false), "Already taken"),
        };

        await ValidationReconciler.ValidateFieldAsync(
            ctx, "username", "admin", validators, TestContext.Current.CancellationToken);

        // Adding each result as it resolved exposed a partial verdict and repainted
        // between messages.
        Assert.Single(seen);
        Assert.Equal(2, seen[0]);
        Assert.Equal(2, ctx.GetMessages("username").Count);
    }

    [Fact]
    public async Task Repeating_Async_Validation_Replaces_Instead_Of_Appending()
    {
        var ctx = new ValidationContext();
        var validators = new[]
        {
            Validate.MustAsync<string>(_ => global::System.Threading.Tasks.Task.FromResult(false), "Reserved"),
        };

        await ValidationReconciler.ValidateFieldAsync(
            ctx, "username", "admin", validators, TestContext.Current.CancellationToken);
        Assert.Single(ctx.GetMessages("username"));

        var notifications = 0;
        ctx.Changed += () => notifications++;

        await ValidationReconciler.ValidateFieldAsync(
            ctx, "username", "admin", validators, TestContext.Current.CancellationToken);

        Assert.Single(ctx.GetMessages("username"));
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task Repeated_Async_Validation_Stays_Single_Across_Many_Passes()
    {
        var ctx = new ValidationContext();
        var validators = new[]
        {
            Validate.MustAsync<string>(_ => global::System.Threading.Tasks.Task.FromResult(false), "Reserved"),
        };

        // The duplicate only surfaced on the *third* pass: pass two produced an equal
        // result, so the diff said "unchanged" and left the original instance installed
        // while ownership had already been repointed at the discarded copy.
        for (var i = 0; i < 4; i++)
        {
            await ValidationReconciler.ValidateFieldAsync(
                ctx, "username", "admin", validators, TestContext.Current.CancellationToken);
        }

        Assert.Single(ctx.GetMessages("username"));
    }

    [Fact]
    public async Task A_Late_Async_Result_For_An_Older_Value_Is_Discarded()
    {
        var ctx = new ValidationContext();
        var older = new global::System.Threading.Tasks.TaskCompletionSource<bool>();
        var newer = new global::System.Threading.Tasks.TaskCompletionSource<bool>();

        var staleValidators = new[]
        {
            Validate.MustAsync<string>(async _ => await older.Task, "stale verdict"),
        };
        var freshValidators = new[]
        {
            Validate.MustAsync<string>(async _ => await newer.Task, "fresh verdict"),
        };

        var stale = ValidationReconciler.ValidateFieldAsync(
            ctx, "username", "old", staleValidators, TestContext.Current.CancellationToken);
        var fresh = ValidationReconciler.ValidateFieldAsync(
            ctx, "username", "new", freshValidators, TestContext.Current.CancellationToken);

        // The newer value's check resolves first and passes.
        newer.SetResult(true);
        await fresh;
        Assert.True(ctx.IsValid());

        // The older value's check then resolves and fails. Applying it would show an
        // error that belongs to a value the user has already replaced.
        older.SetResult(false);
        await stale;

        Assert.True(ctx.IsValid());
        Assert.Empty(ctx.GetMessages("username"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Several producers on one field
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task A_Pass_Cleared_Mid_Flight_Cannot_Be_Resurrected_By_A_Later_Pass()
    {
        var ctx = new ValidationContext();
        var older = new global::System.Threading.Tasks.TaskCompletionSource<bool>();

        var staleValidators = new[]
        {
            Validate.MustAsync<string>(async _ => await older.Task, "stale verdict"),
        };
        var freshValidators = new[]
        {
            Validate.MustAsync<string>(_ => global::System.Threading.Tasks.Task.FromResult(true), "fresh verdict"),
        };

        // Pass one is in flight and holds a token.
        var stale = ValidationReconciler.ValidateFieldAsync(
            ctx, "username", "old", staleValidators, TestContext.Current.CancellationToken);

        // The field is cleared, retiring that token...
        ctx.Clear("username");

        // ...and a brand new pass opens. With a per-field counter this would have been
        // handed the same number the in-flight pass is still holding.
        await ValidationReconciler.ValidateFieldAsync(
            ctx, "username", "new", freshValidators, TestContext.Current.CancellationToken);

        older.SetResult(false);
        await stale;

        Assert.True(ctx.IsValid());
        Assert.Empty(ctx.GetMessages("username"));
    }

    [Fact]
    public void A_Cross_Field_Rule_Does_Not_Erase_Field_Level_Errors()
    {
        var ctx = new ValidationContext();
        ValidationReconciler.ValidateField(ctx, "confirm", "", Validate.Required("Confirmation is required"));
        Assert.Single(ctx.GetMessages("confirm"));

        ValidationRule(() => false, "Passwords must match", "confirm").Evaluate(ctx);

        var texts = ctx.GetMessages("confirm").Select(m => m.Text).ToList();
        Assert.Equal(2, texts.Count);
        Assert.Contains("Confirmation is required", texts);
        Assert.Contains("Passwords must match", texts);
    }

    [Fact]
    public void A_Passing_Rule_Retracts_Only_Its_Own_Message()
    {
        var ctx = new ValidationContext();
        ValidationReconciler.ValidateField(ctx, "confirm", "", Validate.Required("Confirmation is required"));
        ValidationRule(() => false, "Passwords must match", "confirm").Evaluate(ctx);

        // Whole-field replacement made a passing rule wipe the required error and report
        // the form valid.
        ValidationRule(() => true, "Passwords must match", "confirm").Evaluate(ctx);

        var texts = ctx.GetMessages("confirm").Select(m => m.Text).ToList();
        Assert.Single(texts);
        Assert.Contains("Confirmation is required", texts);
        Assert.False(ctx.IsValid());
    }

    [Fact]
    public void Interleaved_Producers_On_One_Field_Settle()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        // Sync runs during render, the rule during reconcile — every pass, forever.
        // Retract-and-append would swap their order each time, and an order change is a
        // structural change, which would notify on every pass.
        for (var i = 0; i < 5; i++)
        {
            ValidationReconciler.ValidateField(ctx, "confirm", "", Validate.Required("Confirmation is required"));
            ValidationRule(() => false, "Passwords must match", "confirm").Evaluate(ctx);
        }

        Assert.Equal(2, ctx.GetMessages("confirm").Count);
        Assert.Equal(2, notifications); // one per producer's first real change, then silence
    }

    [Fact]
    public async Task Async_Validation_Leaves_Synchronous_Messages_Alone()
    {
        var ctx = new ValidationContext();
        ValidationReconciler.ValidateField(ctx, "username", "", Validate.Required("Username is required"));
        Assert.Single(ctx.GetMessages("username"));

        var validators = new[]
        {
            Validate.MustAsync<string>(_ => global::System.Threading.Tasks.Task.FromResult(false), "Reserved"),
        };

        await ValidationReconciler.ValidateFieldAsync(
            ctx, "username", "", validators, TestContext.Current.CancellationToken);

        // Retracting the previous async pass must not take the sync verdict with it.
        var texts = ctx.GetMessages("username").Select(m => m.Text).ToList();
        Assert.Equal(2, texts.Count);
        Assert.Contains("Username is required", texts);
        Assert.Contains("Reserved", texts);

        await ValidationReconciler.ValidateFieldAsync(
            ctx, "username", "", validators, TestContext.Current.CancellationToken);

        texts = [.. ctx.GetMessages("username").Select(m => m.Text)];
        Assert.Equal(2, texts.Count);
        Assert.Contains("Username is required", texts);
    }

    [Fact]
    public async Task An_Async_Rule_Re_Evaluated_To_The_Same_Verdict_Is_Silent()
    {
        var ctx = new ValidationContext();
        var rule = ValidationRuleAsync(
            () => global::System.Threading.Tasks.Task.FromResult(false),
            "Username is taken",
            "username");

        await rule.EvaluateAsync(ctx, TestContext.Current.CancellationToken);

        var notifications = 0;
        ctx.Changed += () => notifications++;
        var version = ctx.Version;

        // Clearing before the await would raise once for the clear and again for the
        // identical failure, and briefly report the field valid in between.
        await rule.EvaluateAsync(ctx, TestContext.Current.CancellationToken);

        Assert.Equal(0, notifications);
        Assert.Equal(version, ctx.Version);
        Assert.Single(ctx.GetMessages("username"));
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
    public void Changed_Is_Deferred_Until_The_Render_Pass_Ends()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        using (ValidationRenderScope.Begin(ctx))
        {
            ctx.Add("email", "boom");
            ctx.MarkTouched("email");

            // Nothing is announced mid-pass: the rendering component reads the new
            // state later in the same pass, and notifying here would re-enter
            // requestRerender from inside Render().
            Assert.Equal(0, notifications);
        }

        // ...but the change is not dropped — other subscribers still need it. Two
        // mutations, one delivery.
        Assert.Equal(1, notifications);

        ctx.MarkTouched("password");
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void A_Deferred_Notification_Reaches_A_Subscriber_That_Is_Not_The_Rendering_Component()
    {
        // The parent/child shape: a parent renders ctx.IsValid() and provides the
        // context; the child's eager .Validate() invalidates it during the child's own
        // render. Dropping that notification left the parent's summary stale forever.
        var shared = new ValidationContext();
        var parentRerenders = 0;

        var parent = new RenderContext();
        parent.BeginRender(() => parentRerenders++);
        var parentScope = new ContextScope();
        parentScope.Push(new Dictionary<ContextBase, object?> { [ValidationContexts.Current] = shared });
        parent.BeginRender(() => parentRerenders++, parentScope);
        Assert.Same(shared, parent.UseValidationContext());
        parent.FlushEffects();
        Assert.True(shared.IsValid());

        var before = parentRerenders;

        using (ValidationRenderScope.Begin(shared))
        {
            _ = TextBox("").Validate("email", "", Validate.Required());
            Assert.Equal(before, parentRerenders); // not mid-pass
        }

        Assert.False(shared.IsValid());
        Assert.True(parentRerenders > before);
    }

    [Fact]
    public void Nested_Frames_Defer_To_The_Outermost_Exit()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        using (ValidationRenderScope.Begin(ctx))
        {
            using (ValidationRenderScope.Begin(ctx))
            {
                ctx.Add("email", "boom");
            }
            Assert.Equal(0, notifications);
        }

        Assert.Equal(1, notifications);
    }

    // ════════════════════════════════════════════════════════════════
    //  Reset — must not notify when there is nothing to reset
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Resetting_An_Untouched_Unknown_Field_Is_Silent()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        ctx.Reset("never-seen");

        // An effect that resets on every render would otherwise repaint forever.
        Assert.Equal(0, notifications);
        Assert.Equal(0, ctx.Version);
    }

    [Fact]
    public void Resetting_Real_State_Notifies_Once_Then_Goes_Quiet()
    {
        var ctx = new ValidationContext();
        ctx.SetInitialValue("email", "start@example.com");
        ctx.Add("email", "boom");
        ctx.MarkTouched("email");
        ctx.NotifyValueChanged("email", "changed@example.com");

        var notifications = 0;
        ctx.Changed += () => notifications++;

        ctx.Reset("email");
        Assert.Equal(1, notifications);
        Assert.True(ctx.IsValid());
        Assert.False(ctx.IsTouched("email"));

        ctx.Reset("email");
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void ResetAll_With_Nothing_To_Reset_Is_Silent()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        ctx.ResetAll();

        Assert.Equal(0, notifications);
        Assert.Equal(0, ctx.Version);
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
