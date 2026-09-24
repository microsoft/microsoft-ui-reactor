using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;
using System.Threading.Tasks;
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
    public void A_Shrinking_Rule_Set_Withdraws_The_Rules_That_Disappeared()
    {
        var ctx = new ValidationContext();

        ValidationReconciler.EvaluateRules(ctx,
            ValidationRule(() => false, "First rule failed", "form"),
            ValidationRule(() => false, "Second rule failed", "form"));
        Assert.Equal(2, ctx.GetMessages("form").Count);

        // The second rule is gone this time. Its message would otherwise keep the form
        // invalid forever, because nothing re-evaluates a rule that no longer exists.
        ValidationReconciler.EvaluateRules(ctx,
            ValidationRule(() => false, "First rule failed", "form"));

        var texts = ctx.GetMessages("form").Select(m => m.Text).ToList();
        Assert.Single(texts);
        Assert.Contains("First rule failed", texts);
    }

    [Fact]
    public void An_Emptied_Rule_Set_Leaves_The_Context_Valid()
    {
        var ctx = new ValidationContext();

        ValidationReconciler.EvaluateRules(ctx,
            ValidationRule(() => false, "Rule failed", "form"));
        Assert.False(ctx.IsValid());

        ValidationReconciler.EvaluateRules(ctx);

        Assert.True(ctx.IsValid());
        Assert.Empty(ctx.GetMessages("form"));
    }

    [Fact]
    public void A_Rule_That_Moves_Field_Withdraws_From_The_Old_One()
    {
        var ctx = new ValidationContext();

        ValidationReconciler.EvaluateRules(ctx,
            ValidationRule(() => false, "Rule failed", "start"));
        Assert.Single(ctx.GetMessages("start"));

        ValidationReconciler.EvaluateRules(ctx,
            ValidationRule(() => false, "Rule failed", "end"));

        Assert.Empty(ctx.GetMessages("start"));
        Assert.Single(ctx.GetMessages("end"));
    }

    [Fact]
    public void Rule_Sets_Do_Not_Disturb_Field_Level_Errors()
    {
        var ctx = new ValidationContext();
        ValidationReconciler.ValidateField(ctx, "form", "", Validate.Required("Field is required"));

        ValidationReconciler.EvaluateRules(ctx,
            ValidationRule(() => false, "Rule failed", "form"));
        Assert.Equal(2, ctx.GetMessages("form").Count);

        // Withdrawing the whole rule set must not take the sync verdict with it.
        ValidationReconciler.EvaluateRules(ctx);

        var texts = ctx.GetMessages("form").Select(m => m.Text).ToList();
        Assert.Single(texts);
        Assert.Contains("Field is required", texts);
    }

    [Fact]
    public async Task A_Sync_Value_Change_Retires_An_In_Flight_Async_Pass()
    {
        var ctx = new ValidationContext();
        var gate = new global::System.Threading.Tasks.TaskCompletionSource<bool>();
        var validators = new[]
        {
            Validate.MustAsync<string>(async _ => await gate.Task, "stale async verdict"),
        };

        // An async check opens for the old value...
        var pending = ValidationReconciler.ValidateFieldAsync(
            ctx, "username", "old", validators, TestContext.Current.CancellationToken);

        // ...then the user types, and the synchronous pass records the new value.
        ValidationReconciler.ValidateField(ctx, "username", "new", Validate.Required());

        gate.SetResult(false);
        await pending;

        // The verdict belongs to a value that is no longer on screen.
        Assert.True(ctx.IsValid());
        Assert.Empty(ctx.GetMessages("username"));
    }

    [Fact]
    public async Task A_Sync_Value_Change_Withdraws_An_Installed_Async_Verdict()
    {
        var ctx = new ValidationContext();
        var validators = new[]
        {
            Validate.MustAsync<string>(_ => global::System.Threading.Tasks.Task.FromResult(false), "Reserved"),
        };

        ValidationReconciler.ValidateField(ctx, "username", "admin", Validate.Required());
        await ValidationReconciler.ValidateFieldAsync(
            ctx, "username", "admin", validators, TestContext.Current.CancellationToken);
        Assert.Single(ctx.GetMessages("username"));

        // Typing a new value must drop the async error computed for the old one.
        ValidationReconciler.ValidateField(ctx, "username", "someone-else", Validate.Required());

        Assert.True(ctx.IsValid());
        Assert.Empty(ctx.GetMessages("username"));
    }

    [Fact]
    public void Re_Validating_The_Same_Value_Keeps_The_Async_Verdict()
    {
        var ctx = new ValidationContext();
        ValidationReconciler.ValidateField(ctx, "username", "admin", Validate.Required());
        ctx.ApplyOwned("username", ValidationContext.AsyncProducer,
            [new ValidationMessage("username", "Reserved")]);
        Assert.Single(ctx.GetMessages("username"));

        // A re-render that revalidates the *same* value is not a value change, so the
        // async verdict still applies and must survive.
        ValidationReconciler.ValidateField(ctx, "username", "admin", Validate.Required());

        Assert.Single(ctx.GetMessages("username"));
        Assert.Equal("Reserved", ctx.GetMessages("username")[0].Text);
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

        // One rule, re-evaluated from one call site — a rule's identity is its code
        // location, not its message text.
        var matches = false;
        void RunRule() => ValidationRule(() => matches, "Passwords must match", "confirm").Evaluate(ctx);

        RunRule();
        Assert.Equal(2, ctx.GetMessages("confirm").Count);

        // Whole-field replacement made a passing rule wipe the required error and report
        // the form valid.
        matches = true;
        RunRule();

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

    [Fact]
    public void A_Value_Change_Retracts_The_Async_Verdict_For_The_Old_Value()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");

        var generation = ctx.BeginAsyncValidation("email");
        ctx.ApplyAsyncValidation("email", generation, [new ValidationMessage("email", "Already registered", Severity.Error, "TAKEN")]);
        Assert.Single(ctx.GetMessages("email"));

        // The verdict was about the old value; it says nothing about the new one.
        ctx.NotifyValueChanged("email", "someone-else@example.com");

        Assert.Empty(ctx.GetMessages("email"));
    }

    [Fact]
    public void A_Value_Change_Retires_An_In_Flight_Async_Pass()
    {
        var ctx = new ValidationContext();
        ctx.RegisterField("email");

        // Pass opened against the old value, still running.
        var stale = ctx.BeginAsyncValidation("email");

        ctx.NotifyValueChanged("email", "someone-else@example.com");

        // It resolves afterwards and must not install a verdict about a value that
        // is no longer on screen.
        ctx.ApplyAsyncValidation("email", stale, [new ValidationMessage("email", "Already registered", Severity.Error, "TAKEN")]);

        Assert.Empty(ctx.GetMessages("email"));
    }

    [Fact]
    public async Task A_Value_Change_Retracts_An_Async_Rule_Verdict_Too()
    {
        var ctx = new ValidationContext();

        var rule = ValidationRuleAsync(() => Task.FromResult(false), "End must follow start", "dates");
        await rule.EvaluateAsync(ctx, "rule#1", TestContext.Current.CancellationToken);
        Assert.Single(ctx.GetMessages("dates"));

        // The rule owns its own producer key, not the field's plain async slot.
        ctx.ApplyValidation("dates", "2026-01-02", []);

        Assert.Empty(ctx.GetMessages("dates"));
        Assert.True(ctx.IsValid());
    }

    [Fact]
    public async Task NotifyValueChanged_Retracts_An_Async_Rule_Verdict_Too()
    {
        var ctx = new ValidationContext();

        var rule = ValidationRuleAsync(() => Task.FromResult(false), "End must follow start", "dates");
        await rule.EvaluateAsync(ctx, "rule#1", TestContext.Current.CancellationToken);
        Assert.Single(ctx.GetMessages("dates"));

        ctx.NotifyValueChanged("dates", "2026-01-02");

        Assert.Empty(ctx.GetMessages("dates"));
        Assert.True(ctx.IsValid());
    }

    [Fact]
    public async Task A_Value_Change_Retires_An_In_Flight_Async_Rule_Pass()
    {
        var ctx = new ValidationContext();
        var pending = new TaskCompletionSource<bool>();

        var rule = ValidationRuleAsync(() => pending.Task, "End must follow start", "dates");
        var running = rule.EvaluateAsync(ctx, "rule#1", TestContext.Current.CancellationToken);

        ctx.NotifyValueChanged("dates", "2026-01-02");

        pending.SetResult(false);
        await running;

        Assert.Empty(ctx.GetMessages("dates"));
    }

    [Fact]
    public void A_Directly_Evaluated_Rule_Replaces_Its_Own_Message_When_The_Text_Changes()
    {
        var ctx = new ValidationContext();

        // The message is interpolated, so it moves on every evaluation — the case that
        // used to orphan the previous verdict under a message-derived producer key.
        for (var start = 1; start <= 4; start++)
        {
            var rule = ValidationRule(() => false, $"Must be after day {start}", "end");
            rule.Evaluate(ctx);
        }

        Assert.Single(ctx.GetMessages("end"));
        Assert.Equal("Must be after day 4", ctx.GetMessages("end")[0].Text);
    }

    [Fact]
    public void A_Directly_Evaluated_Rule_Retracts_Once_It_Passes()
    {
        var ctx = new ValidationContext();

        var ordered = false;
        void RunRule() => ValidationRule(() => ordered, "Must be after start", "end").Evaluate(ctx);

        RunRule();
        Assert.Single(ctx.GetMessages("end"));

        // Same call site, now passing — it has to withdraw what it installed.
        ordered = true;
        RunRule();

        Assert.Empty(ctx.GetMessages("end"));
    }

    [Fact]
    public void Two_Distinct_Rules_On_One_Field_Keep_Separate_Slots()
    {
        var ctx = new ValidationContext();

        ValidationRule(() => false, "Range is closed", "dates").Evaluate(ctx);
        ValidationRule(() => false, "Range is too long", "dates").Evaluate(ctx);

        Assert.Equal(2, ctx.GetMessages("dates").Count);
    }

    [Fact]
    public void A_Directly_Evaluated_Rule_Leaves_Sync_Field_Messages_Alone()
    {
        var ctx = new ValidationContext();

        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("").Validate("end", "", Validate.Required());
        Assert.Single(ctx.GetMessages("end"));

        ValidationRule(() => false, "Must be after start", "end").Evaluate(ctx);

        // The old whole-field clear took the sync verdict with it.
        Assert.Equal(2, ctx.GetMessages("end").Count);
    }

    [Fact]
    public void Evaluating_An_Async_Rule_Synchronously_Throws_Instead_Of_Passing_It()
    {
        var ctx = new ValidationContext();
        var rule = ValidationRuleAsync(() => Task.FromResult(false), "Name is taken", "name");

        var ex = Assert.Throws<global::System.InvalidOperationException>(() => rule.Evaluate(ctx));

        Assert.Contains("name", ex.Message, StringComparison.Ordinal);
        // The silent failure mode was recording a passing verdict for a field that
        // was never checked.
        Assert.Empty(ctx.GetMessages("name"));
    }

    [Fact]
    public void The_Batch_Rule_Path_Rejects_An_Async_Rule_Too()
    {
        var ctx = new ValidationContext();

        Assert.Throws<global::System.InvalidOperationException>(() =>
            ValidationReconciler.EvaluateRules(
                ctx,
                ValidationRule(() => false, "Sync rule", "a"),
                ValidationRuleAsync(() => Task.FromResult(false), "Async rule", "b")));
    }

    [Fact]
    public async Task The_Async_Batch_Path_Runs_Both_Kinds_Of_Rule()
    {
        var ctx = new ValidationContext();

        await ValidationReconciler.EvaluateRulesAsync(
            ctx,
            ValidationRule(() => false, "Sync rule", "a"),
            ValidationRuleAsync(() => Task.FromResult(false), "Async rule", "b"));

        Assert.Single(ctx.GetMessages("a"));
        Assert.Single(ctx.GetMessages("b"));
        Assert.Equal("Async rule", ctx.GetMessages("b")[0].Text);
    }

    [Fact]
    public async Task Retiring_A_Producer_Stops_Its_In_Flight_Pass_From_Installing()
    {
        var ctx = new ValidationContext();
        var pending = new TaskCompletionSource<bool>();

        var rule = ValidationRuleAsync(() => pending.Task, "Name is taken", "name");
        var running = rule.EvaluateAsync(ctx, "rule#7", TestContext.Current.CancellationToken);

        // The rule leaves the tree while its check is still out.
        ctx.RetireProducer("name", "rule#7");

        pending.SetResult(false);
        await running;

        Assert.Empty(ctx.GetMessages("name"));
    }

    [Fact]
    public async Task Retiring_A_Producer_Withdraws_What_It_Already_Installed()
    {
        var ctx = new ValidationContext();

        var rule = ValidationRuleAsync(() => Task.FromResult(false), "Name is taken", "name");
        await rule.EvaluateAsync(ctx, "rule#7", TestContext.Current.CancellationToken);
        Assert.Single(ctx.GetMessages("name"));

        ctx.RetireProducer("name", "rule#7");

        Assert.Empty(ctx.GetMessages("name"));
        Assert.True(ctx.IsValid());
    }

    [Fact]
    public void A_Message_Whose_Text_Contains_The_Snapshot_Separators_Is_Still_Distinguished()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("").Validate("email", "",
                Validate.Must<string>(_ => false, "a"),
                Validate.Must<string>(_ => false, "b"));

        var two = ctx.GetMessages("email");
        Assert.Equal(2, two.Count);
        Assert.Equal(1, notifications);

        // One message crafted to serialize exactly like those two under a
        // separator-only encoding: it embeds the record separator and a second
        // message header. Derived from the real metadata so it cannot drift.
        var collider = $"a\u0003i\u0001{two[0].Severity}\u0001{two[0].Code}\u0001b";

        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("").Validate("email", "", Validate.Must<string>(_ => false, collider));

        Assert.Single(ctx.GetMessages("email"));
        Assert.Equal(2, notifications);
    }

    [Fact]
    public async Task The_Async_Field_Path_Registers_Its_Field()
    {
        var ctx = new ValidationContext();

        await ValidationReconciler.ValidateFieldAsync(
            ctx, "email", "",
            [Validate.MustAsync<string>(_ => Task.FromResult(false), "Already registered")],
            TestContext.Current.CancellationToken);

        Assert.Contains("email", ctx.RegisteredFields);

        // The point of registering: MarkAllTouched() has to cover it.
        ctx.MarkAllTouched();
        Assert.True(ctx.IsTouched("email"));
    }

    [Fact]
    public void The_Batch_Rule_Path_Registers_Every_Rule_Field()
    {
        var ctx = new ValidationContext();

        ValidationReconciler.EvaluateRules(
            ctx,
            ValidationRule(() => false, "Range is closed", "start"),
            ValidationRule(() => true, "Range is too long", "end"));

        Assert.Contains("start", ctx.RegisteredFields);
        Assert.Contains("end", ctx.RegisteredFields);
    }

    [Fact]
    public async Task An_Overtaken_Async_Batch_Stands_Down_Instead_Of_Overwriting()
    {
        var ctx = new ValidationContext();
        var slow = new TaskCompletionSource<bool>();

        // Older batch: blocks on field "a".
        var older = ValidationReconciler.EvaluateRulesAsync(
            ctx, ValidationRuleAsync(() => slow.Task, "Stale verdict", "a"));

        // Newer batch, requested while the older one is still out.
        var newer = ValidationReconciler.EvaluateRulesAsync(
            ctx, ValidationRuleAsync(() => Task.FromResult(false), "Current verdict", "b"));

        slow.SetResult(false);
        await older;
        await newer;

        // The older batch stood down: it neither installed its own verdict nor retired
        // the newer batch's producer.
        Assert.Empty(ctx.GetMessages("a"));
        Assert.Single(ctx.GetMessages("b"));
        Assert.Equal("Current verdict", ctx.GetMessages("b")[0].Text);
    }

    [Fact]
    public void A_Value_Change_Leaves_Sync_Messages_For_The_New_Value_Intact()
    {
        var ctx = new ValidationContext();

        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("").Validate("email", "", Validate.Required());

        Assert.Single(ctx.GetMessages("email"));

        // Retiring the async producer must not take the sync verdict with it.
        ctx.NotifyValueChanged("email", "");
        ctx.NotifyValueChanged("email", "  ");

        Assert.Single(ctx.GetMessages("email"));
        Assert.Equal("REQUIRED", ctx.GetMessages("email")[0].Code);
    }

    [Fact]
    public void A_Reconcile_Frame_Defers_Notifications_Raised_Between_Renders()
    {
        var ctx = new ValidationContext();
        var notified = 0;
        ctx.Changed += () => notified++;

        using (ValidationRenderScope.BeginReconcile())
        {
            // No component is rendering, so .Validate() must still be attach-only.
            Assert.Null(ValidationRenderScope.Current);

            // This is what a rule unmounting mid-reconcile does.
            ctx.Add("dates", "End must follow start");
            Assert.Equal(0, notified);
        }

        Assert.Equal(1, notified);
    }

    [Fact]
    public void A_Deferred_Notification_Reaches_A_Subscriber_That_Arrives_After_The_Flush()
    {
        var ctx = new ValidationContext();
        var notified = 0;

        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("").Validate("email", "", Validate.Required());

        // The frame has closed and the deferral already flushed — a host flushes root
        // effects only after reconciliation, so the parent subscribes at about here.
        ctx.Changed += () => notified++;

        Assert.Equal(1, notified);
    }

    [Fact]
    public void A_Held_Notification_Is_Delivered_Once_Not_Per_Subscriber()
    {
        var ctx = new ValidationContext();
        int first = 0, second = 0;

        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("").Validate("email", "", Validate.Required());

        ctx.Changed += () => first++;
        ctx.Changed += () => second++;

        Assert.Equal(1, first);
        Assert.Equal(0, second);
    }

    [Fact]
    public async Task Overlapping_Async_Rule_Evaluations_Discard_The_Older_Result()
    {
        var ctx = new ValidationContext();
        var slow = new TaskCompletionSource<bool>();
        var quick = new TaskCompletionSource<bool>();

        var failing = ValidationRuleAsync(() => slow.Task, "End must follow start", "dates");
        var passing = failing with { AsyncPredicate = () => quick.Task };

        // Same producer: two evaluations of one mounted rule, overlapping.
        var older = failing.EvaluateAsync(ctx, "rule#1", TestContext.Current.CancellationToken);
        var newer = passing.EvaluateAsync(ctx, "rule#1", TestContext.Current.CancellationToken);

        quick.SetResult(true);
        await newer;
        Assert.Empty(ctx.GetMessages("dates"));

        // The older run resolves last and must not reinstate its verdict.
        slow.SetResult(false);
        await older;

        Assert.Empty(ctx.GetMessages("dates"));
    }

    [Fact]
    public async Task An_Async_Rule_Still_Applies_Its_Own_Newest_Result()
    {
        var ctx = new ValidationContext();

        var rule = ValidationRuleAsync(() => Task.FromResult(false), "End must follow start", "dates");
        await rule.EvaluateAsync(ctx, "rule#1", TestContext.Current.CancellationToken);

        Assert.Single(ctx.GetMessages("dates"));
        Assert.Equal("End must follow start", ctx.GetMessages("dates")[0].Text);
    }

    [Fact]
    public void Chained_Value_Overloads_Settle_Instead_Of_Repainting_Forever()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        // Each call in the chain eagerly applies its own intermediate validator set
        // under the same producer, so every pass removes the later message and puts it
        // straight back. The net state never moves, and announcing that churn would
        // schedule another render that churns identically — forever.
        for (var pass = 0; pass < 5; pass++)
        {
            using (ValidationRenderScope.Begin(ctx))
            {
                _ = TextBox("abc")
                    .Validate("email", "abc", Validate.Email())
                    .Validate("email", "abc", Validate.MinLength(10));
            }
        }

        Assert.Equal(2, ctx.GetMessages("email").Count);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void A_Net_Zero_Pass_Leaves_Version_Alone()
    {
        var ctx = new ValidationContext();

        using (ValidationRenderScope.Begin(ctx))
        {
            _ = TextBox("abc")
                .Validate("email", "abc", Validate.Email())
                .Validate("email", "abc", Validate.MinLength(10));
        }

        var settled = ctx.Version;

        // Version is documented for change detection in hooks and memos, so a pass that
        // churns and lands where it started must not read as a change.
        for (var pass = 0; pass < 4; pass++)
        {
            using (ValidationRenderScope.Begin(ctx))
            {
                _ = TextBox("abc")
                    .Validate("email", "abc", Validate.Email())
                    .Validate("email", "abc", Validate.MinLength(10));
            }
        }

        Assert.Equal(settled, ctx.Version);
    }

    [Fact]
    public void A_Real_Change_During_A_Render_Still_Moves_Version()
    {
        var ctx = new ValidationContext();

        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("").Validate("email", "", Validate.Required());
        var settled = ctx.Version;

        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("a@b.co").Validate("email", "a@b.co", Validate.Required());

        Assert.Empty(ctx.GetMessages("email"));
        Assert.True(ctx.Version > settled, $"settled={settled} now={ctx.Version}");
    }

    [Fact]
    public void Reordering_A_Field_Messages_Is_Announced()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("abc")
                .Validate("email", "abc", Validate.Email("A"), Validate.MinLength(10, "B"));
        Assert.Equal(1, notifications);
        Assert.Equal("A", ctx.GetMessages("email")[0].Text);

        // Same set, different order. GetMessages exposes order and callers read the
        // first message, so this is a real change.
        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("abc")
                .Validate("email", "abc", Validate.MinLength(10, "B"), Validate.Email("A"));

        Assert.Equal("B", ctx.GetMessages("email")[0].Text);
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void A_Chained_Chain_Still_Announces_A_Real_Change()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        using (ValidationRenderScope.Begin(ctx))
        {
            _ = TextBox("abc")
                .Validate("email", "abc", Validate.Email())
                .Validate("email", "abc", Validate.MinLength(10));
        }
        Assert.Equal(1, notifications);

        // The user fixes the value: the suppression must not swallow this.
        using (ValidationRenderScope.Begin(ctx))
        {
            _ = TextBox("someone@example.com")
                .Validate("email", "someone@example.com", Validate.Email())
                .Validate("email", "someone@example.com", Validate.MinLength(10));
        }

        Assert.Empty(ctx.GetMessages("email"));
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void Suppression_Does_Not_Swallow_A_Touch_Made_During_A_Net_Zero_Pass()
    {
        var ctx = new ValidationContext();
        var notifications = 0;
        ctx.Changed += () => notifications++;

        using (ValidationRenderScope.Begin(ctx))
            _ = TextBox("").Validate("email", "", Validate.Required());
        Assert.Equal(1, notifications);

        // Same messages as last time, but a field also became touched — non-message
        // state, so the pass is not net-zero.
        using (ValidationRenderScope.Begin(ctx))
        {
            _ = TextBox("").Validate("email", "", Validate.Required());
            ctx.MarkTouched("email");
        }

        Assert.True(ctx.IsTouched("email"));
        Assert.Equal(2, notifications);
    }

    [Fact]
    public async Task Async_Producers_On_One_Field_Do_Not_Cancel_Each_Other()
    {
        var ctx = new ValidationContext();
        var closed = new TaskCompletionSource<bool>();
        var tooLong = new TaskCompletionSource<bool>();

        var ruleA = ValidationRuleAsync(() => closed.Task, "Range is closed", "dates");
        var ruleB = ValidationRuleAsync(() => tooLong.Task, "Range is too long", "dates");

        // Both passes are open before either applies — a token shared across the field
        // would let B's Begin retire A's still-pending pass.
        var a = ruleA.EvaluateAsync(ctx, "rule#1", TestContext.Current.CancellationToken);
        var b = ruleB.EvaluateAsync(ctx, "rule#2", TestContext.Current.CancellationToken);

        closed.SetResult(false);
        await a;
        tooLong.SetResult(false);
        await b;

        Assert.Equal(2, ctx.GetMessages("dates").Count);
    }
}
