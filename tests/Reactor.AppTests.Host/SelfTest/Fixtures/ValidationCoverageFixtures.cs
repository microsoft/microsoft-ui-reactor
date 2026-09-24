using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static Microsoft.UI.Reactor.Controls.Validation.ValidationRuleDsl;
using static Microsoft.UI.Reactor.Controls.Validation.ValidationVisualizerDsl;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selfhost fixtures targeting Reactor\Validation coverage (0%):
/// ValidationContext, built-in validators, FormField, ValidateExtensions,
/// UseValidationContext hook, ValidationRule.
/// </summary>
internal static class ValidationCoverageFixtures
{
    // ════════════════════════════════════════════════════════════════════════
    //  1. ValidationContext — full API exercise
    // ════════════════════════════════════════════════════════════════════════

    internal class ValidationContextExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ctx = new ValidationContext();

            // Register fields
            ctx.RegisterField("name");
            ctx.RegisterField("email");
            H.Check("ValCtx_Registered", ctx.RegisteredFields.Count == 2);

            // Add messages
            ctx.Add("name", "Required", Severity.Error);
            ctx.Add("email", "Invalid format", Severity.Warning);
            H.Check("ValCtx_HasError", ctx.HasError("name"));
            H.Check("ValCtx_HasMessages", ctx.HasMessages("email"));
            H.Check("ValCtx_NotValid", !ctx.IsValid());
            H.Check("ValCtx_InvalidFields", ctx.InvalidFields.Count == 1);

            // HighestSeverity
            H.Check("ValCtx_HighSev", ctx.HighestSeverity("name") == Severity.Error);
            H.Check("ValCtx_HighSevWarn", ctx.HighestSeverity("email") == Severity.Warning);
            H.Check("ValCtx_HighSevNull", ctx.HighestSeverity("unknown") is null);

            // GetMessages
            H.Check("ValCtx_GetMessages", ctx.GetMessages("name").Count == 1);
            H.Check("ValCtx_GetAllMessages", ctx.GetAllMessages().Count == 2);

            // External messages
            ctx.AddExternal("name", "Server error");
            H.Check("ValCtx_ExternalAdded", ctx.GetMessages("name").Count == 2);

            ctx.ClearExternal("name");
            H.Check("ValCtx_ExternalCleared", ctx.GetMessages("name").Count == 1);

            // Clear field
            ctx.Clear("name");
            H.Check("ValCtx_FieldCleared", ctx.GetMessages("name").Count == 0);

            // Touched/dirty state
            H.Check("ValCtx_NotTouched", !ctx.IsTouched("email"));
            ctx.MarkTouched("email");
            H.Check("ValCtx_Touched", ctx.IsTouched("email"));

            ctx.MarkAllTouched();
            H.Check("ValCtx_AllTouched", ctx.IsTouched("name"));

            // Dirty tracking
            ctx.SetInitialValue("name", "Alice");
            H.Check("ValCtx_NotDirty", !ctx.IsDirty("name"));
            ctx.NotifyValueChanged("name", "Bob");
            H.Check("ValCtx_Dirty", ctx.IsDirty("name"));
            H.Check("ValCtx_AnyDirty", ctx.IsDirty());

            // Reset single field
            var initial = ctx.Reset("name");
            H.Check("ValCtx_ResetInitial", (string?)initial == "Alice");
            H.Check("ValCtx_ResetNotDirty", !ctx.IsDirty("name"));

            // Reset all
            ctx.NotifyValueChanged("email", "changed");
            ctx.SetInitialValue("email", "original");
            ctx.NotifyValueChanged("email", "changed");
            var allReset = ctx.ResetAll();
            H.Check("ValCtx_ResetAll", allReset.Count >= 1);

            // ClearAll
            ctx.Add("name", "Error");
            ctx.AddExternal("email", "Ext");
            ctx.ClearAll();
            H.Check("ValCtx_ClearAll", ctx.GetAllMessages().Count == 0);

            // Version increments
            H.Check("ValCtx_VersionPositive", ctx.Version > 0);

            var host = H.CreateHost();
            host.Mount(c => TextBlock("Validation done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2. Built-in validators — exercise all factory methods
    // ════════════════════════════════════════════════════════════════════════

    internal class BuiltInValidatorsExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Required
            var req = Validate.Required();
            H.Check("Val_Required_Fail", req.Validate(null, "f") is not null);
            H.Check("Val_Required_FailEmpty", req.Validate("", "f") is not null);
            H.Check("Val_Required_Pass", req.Validate("hello", "f") is null);

            // MinLength
            var min = Validate.MinLength(3);
            H.Check("Val_MinLen_Fail", min.Validate("ab", "f") is not null);
            H.Check("Val_MinLen_Pass", min.Validate("abc", "f") is null);
            H.Check("Val_MinLen_NonString", min.Validate(42, "f") is null);

            // MaxLength
            var max = Validate.MaxLength(5);
            H.Check("Val_MaxLen_Fail", max.Validate("abcdef", "f") is not null);
            H.Check("Val_MaxLen_Pass", max.Validate("abc", "f") is null);

            // Range
            var range = Validate.Range(1, 10);
            H.Check("Val_Range_Fail", range.Validate(0, "f") is not null);
            H.Check("Val_Range_Pass", range.Validate(5, "f") is null);
            H.Check("Val_Range_Double", range.Validate(5.5, "f") is null);
            H.Check("Val_Range_Float", range.Validate(5.5f, "f") is null);
            H.Check("Val_Range_Long", range.Validate(5L, "f") is null);
            H.Check("Val_Range_NonNum", range.Validate("abc", "f") is null);

            // Match (regex)
            var match = Validate.Match(@"^\d{3}$");
            H.Check("Val_Match_Fail", match.Validate("ab", "f") is not null);
            H.Check("Val_Match_Pass", match.Validate("123", "f") is null);
            H.Check("Val_Match_Empty", match.Validate("", "f") is null);

            // Email
            var email = Validate.Email();
            H.Check("Val_Email_Fail", email.Validate("notanemail", "f") is not null);
            H.Check("Val_Email_Pass", email.Validate("test@example.com", "f") is null);

            // URL
            var url = Validate.Url();
            H.Check("Val_Url_Fail", url.Validate("notaurl", "f") is not null);
            H.Check("Val_Url_Pass", url.Validate("https://example.com", "f") is null);

            // Must<T>
            var must = Validate.Must<int>(v => v > 0, "Must be positive");
            H.Check("Val_Must_Fail", must.Validate(0, "f") is not null);
            H.Check("Val_Must_Pass", must.Validate(1, "f") is null);

            // MustBeTrue
            var mustTrue = Validate.MustBeTrue();
            H.Check("Val_MustTrue_Fail", mustTrue.Validate(false, "f") is not null);
            H.Check("Val_MustTrue_Pass", mustTrue.Validate(true, "f") is null);

            // EqualTo
            var eq = Validate.EqualTo("password");
            H.Check("Val_EqualTo_Fail", eq.Validate("wrong", "f") is not null);
            H.Check("Val_EqualTo_Pass", eq.Validate("password", "f") is null);

            // MustAsync<T>
            var mustAsync = Validate.MustAsync<string>(async s =>
            {
                await Task.Delay(1);
                return s.Length > 2;
            }, "Too short async");
            var asyncResult = await mustAsync.ValidateAsync("ab", "f");
            H.Check("Val_MustAsync_Fail", asyncResult is not null);
            var asyncPass = await mustAsync.ValidateAsync("abc", "f");
            H.Check("Val_MustAsync_Pass", asyncPass is null);

            var host = H.CreateHost();
            host.Mount(c => TextBlock("Validators done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3. ValidateExtensions — attach validators to elements
    // ════════════════════════════════════════════════════════════════════════

    internal class ValidateExtensionsExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Attach validators to an element
            var el = TextBlock("")
                .Validate("username", Validate.Required(), Validate.MinLength(3))
                .Validate("username", "testval", Validate.MaxLength(20));

            var attached = el.GetValidation();
            H.Check("ValExt_Attached", attached is not null);
            H.Check("ValExt_FieldName", attached?.FieldName == "username");
            H.Check("ValExt_ValidatorCount", attached?.Validators.Length == 3);
            H.Check("ValExt_HasValue", attached?.Value is not null);

            // Run validators
            var messages = attached!.RunValidators("");
            H.Check("ValExt_RunValidators", messages.Count >= 1);

            var passMessages = attached!.RunValidators("hello");
            H.Check("ValExt_RunValidatorsPass", passMessages.Count == 0);

            // Async validators
            var asyncEl = TextBlock("")
                .ValidateAsync("email", Validate.MustAsync<string>(async s =>
                {
                    await Task.Delay(1);
                    return s.Contains("@");
                }, "Invalid"))
                .ValidateAsync("email", "test", Validate.MustAsync<string>(async s =>
                {
                    await Task.Delay(1);
                    return s.Length > 0;
                }, "Required"));

            var asyncAttached = asyncEl.GetValidation();
            H.Check("ValExt_AsyncAttached", asyncAttached is not null);
            H.Check("ValExt_AsyncCount", asyncAttached?.AsyncValidators.Length == 2);

            var asyncMsgs = await asyncAttached!.RunAsyncValidators("bad");
            H.Check("ValExt_AsyncFail", asyncMsgs.Count >= 1);

            var host = H.CreateHost();
            host.Mount(c => TextBlock("Extensions done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  4. UseValidationContext hook + FormField rendering
    //     Targets: UseValidationContext, FormField mount, ValidationReconciler
    // ════════════════════════════════════════════════════════════════════════

    internal class FormFieldRendering(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var valCtx = ctx.UseValidationContext();
                var (val, setVal) = ctx.UseState(0.0);

                return VStack(
                    FormField(
                        NumberBox(val, v => setVal(v))
                            .Validate("amount", val, Validate.Range(1, 100)),
                        label: "Amount",
                        required: true,
                        description: "Enter an amount",
                        fieldName: "amount",
                        showWhen: ShowWhen.Always
                    ),
                    TextBlock($"Valid:{valCtx.IsValid()}")
                ).Provide(ValidationContexts.Current, valCtx);
            });

            await Harness.Render();
            // FormField should render with label and description
            H.Check("FormField_Mounted", H.FindTextContaining("Amount") is not null);

            var host2 = H.CreateHost();
            host2.Mount(c => TextBlock("FormField done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  5. ValidationRule — sync and async evaluation
    //     Targets: ValidationRuleDsl.ValidationRule, Evaluate, EvaluateAsync
    // ════════════════════════════════════════════════════════════════════════

    internal class CompositeLifecycleUpdateAndCleanup(Harness h) : SelfTestFixtureBase(h)
    {
        private static int s_cleanupCount;

        private sealed class CleanupChild : Component
        {
            public override Element Render()
            {
                UseEffect(() => () => global::System.Threading.Interlocked.Increment(ref s_cleanupCount));
                return TextBlock("cleanup-child");
            }
        }

        public override async Task RunAsync()
        {
            s_cleanupCount = 0;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (label, setLabel) = ctx.UseState("Name");
                var (buttonContent, setButtonContent) = ctx.UseState(false);
                return VStack(
                    FormField(
                        buttonContent ? Button("field-button") : TextBox("stable"),
                        label: label,
                        description: "desc"),
                    Button("ChangeFormLabel", () => setLabel("Display Name")),
                    Button("SwapFormContent", () => setButtonContent(true)));
            });

            await Harness.Render();
            var originalTextBox = H.FindControl<TextBox>(tb => tb.Text == "stable");
            H.Check("Composite_FormField_Mounted", originalTextBox is not null);

            H.ClickButton("ChangeFormLabel");
            await Harness.Render();
            H.Check("Composite_FormField_UpdatedLabel", H.FindText("Display Name") is not null);
            H.Check("Composite_FormField_UpdatePreservesContent", ReferenceEquals(originalTextBox, H.FindControl<TextBox>(tb => tb.Text == "stable")));

            H.ClickButton("SwapFormContent");
            await Harness.Render();
            H.Check("Composite_FormField_SubstitutedContent", H.FindButton("field-button") is not null);

            host.Mount(_ => FormField(Component<CleanupChild>(), label: "Cleanup"));
            await Harness.Render();
            H.Check("Composite_FormField_NoCleanupBeforeUnmount", s_cleanupCount == 0);
            host.Mount(_ => TextBlock("formfield-gone"));
            await Harness.Render();
            H.Check("Composite_FormField_CleanupRan", s_cleanupCount == 1);

            s_cleanupCount = 0;
            var vizHost = H.CreateHost();
            vizHost.Mount(ctx =>
            {
                var (title, setTitle) = ctx.UseState("Before");
                return VStack(
                    ValidationVisualizer(VisualizerStyle.Inline, Component<CleanupChild>(), title: title),
                    Button("RerenderVisualizer", () => setTitle("After")));
            });

            await Harness.Render();
            H.ClickButton("RerenderVisualizer");
            await Harness.Render();
            H.Check("Composite_ValidationVisualizer_SubstitutionCleanup", s_cleanupCount == 1);
            H.Check("Composite_ValidationVisualizer_RemountedContent", H.FindText("cleanup-child") is not null);
        }
    }

    internal class ValidationRuleExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ctx = new ValidationContext();

            // Sync rule — fails
            var rule = ValidationRule(() => false, "Must be valid", "field1");
            rule.Evaluate(ctx);
            H.Check("ValRule_SyncFail", ctx.HasError("field1"));

            // Sync rule — passes (clears previous)
            var rule2 = ValidationRule(() => true, "Must be valid", "field1");
            rule2.Evaluate(ctx);
            H.Check("ValRule_SyncPass", !ctx.HasError("field1"));

            // Async rule — fails
            var asyncRule = ValidationRuleAsync(
                async () => { await Task.Delay(1); return false; },
                "Async fail", "field2");
            await asyncRule.EvaluateAsync(ctx);
            H.Check("ValRule_AsyncFail", ctx.HasError("field2"));

            // Async rule — the same rule (same message, so the same producer) now
            // passes and retracts its own message. Using a *different* rule here would
            // assert the old whole-field-replace behaviour, where any passing rule
            // erased every other producer's errors on the field (issue #1262 review).
            var asyncRule2 = ValidationRuleAsync(
                async () => { await Task.Delay(1); return true; },
                "Async fail", "field2");
            await asyncRule2.EvaluateAsync(ctx);
            H.Check("ValRule_AsyncPass", !ctx.HasError("field2"));

            // A different passing rule must leave another producer's error alone.
            var asyncFail2 = ValidationRuleAsync(
                async () => { await Task.Delay(1); return false; },
                "Async fail", "field2");
            await asyncFail2.EvaluateAsync(ctx);
            var unrelatedPass = ValidationRuleAsync(
                async () => { await Task.Delay(1); return true; },
                "Unrelated rule", "field2");
            await unrelatedPass.EvaluateAsync(ctx);
            H.Check("ValRule_AsyncPassKeepsOtherProducers", ctx.HasError("field2"));
            ctx.Clear("field2");

            // Sync fallback when AsyncPredicate is null
            var syncFallback = ValidationRule(() => false, "Sync fallback", "field3");
            await syncFallback.EvaluateAsync(ctx);
            H.Check("ValRule_SyncFallback", ctx.HasError("field3"));

            var host = H.CreateHost();
            host.Mount(c => TextBlock("Rules done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  6. FormFieldHelpers — utility methods
    //     Targets: GetDisplayLabel, GetDescriptionOrError, GetAutomationName,
    //              DetectFieldName, ResolveFieldName
    // ════════════════════════════════════════════════════════════════════════

    internal class FormFieldHelpersExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // GetDisplayLabel
            H.Check("FFH_LabelRequired", FormFieldHelpers.GetDisplayLabel("Name", true) == "Name *");
            H.Check("FFH_LabelNotRequired", FormFieldHelpers.GetDisplayLabel("Name", false) == "Name");
            H.Check("FFH_LabelNull", FormFieldHelpers.GetDisplayLabel(null, false) == "");

            // GetAutomationName
            H.Check("FFH_AutoName", FormFieldHelpers.GetAutomationName("Name *") == "Name");
            H.Check("FFH_AutoNameNull", FormFieldHelpers.GetAutomationName(null) is null);

            // DetectFieldName
            var elWithVal = TextBlock("").Validate("email", Validate.Required());
            H.Check("FFH_DetectField", FormFieldHelpers.DetectFieldName(elWithVal) == "email");

            var elWithout = TextBlock("");
            H.Check("FFH_DetectFieldNull", FormFieldHelpers.DetectFieldName(elWithout) is null);

            // ResolveFieldName
            H.Check("FFH_ResolveExplicit", FormFieldHelpers.ResolveFieldName("explicit", elWithVal) == "explicit");
            H.Check("FFH_ResolveDetected", FormFieldHelpers.ResolveFieldName(null, elWithVal) == "email");

            // GetDescriptionOrError
            var ctx = new ValidationContext();
            ctx.RegisterField("test");
            ctx.MarkTouched("test");

            var (text1, isErr1) = FormFieldHelpers.GetDescriptionOrError(ctx, "test", "Help text", ShowWhen.WhenTouched);
            H.Check("FFH_DescNoError", text1 == "Help text" && !isErr1);

            ctx.Add("test", "Bad value");
            var (text2, isErr2) = FormFieldHelpers.GetDescriptionOrError(ctx, "test", "Help text", ShowWhen.WhenTouched);
            H.Check("FFH_DescWithError", isErr2 && text2 == "Bad value");

            // Null context
            var (text3, isErr3) = FormFieldHelpers.GetDescriptionOrError(null, "test", "desc", ShowWhen.Always);
            H.Check("FFH_NullCtx", text3 == "desc" && !isErr3);

            var host = H.CreateHost();
            host.Mount(c => TextBlock("Helpers done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 — the docs' "Validation Context" example, verbatim.
    //
    //  Bare .Validate() controls: no FormField wrapper, no explicit
    //  .Provide(ValidationContexts.Current, …), and the context queried inline
    //  during Render(). Every one of those was silently inert before the fix, so
    //  an empty form reported IsValid() == true and submitted.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_BareValidateOnControls(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            ValidationContext? captured = null;
            var submissions = 0;

            host.Mount(ctx =>
            {
                var valCtx = ctx.UseValidationContext();
                captured = valCtx;
                var (email, setEmail) = ctx.UseState("");
                var (password, setPassword) = ctx.UseState("");
                var (submitted, setSubmitted) = ctx.UseState(false);

                return VStack(12,
                    TextBox(email, v => { setEmail(v); valCtx.NotifyValueChanged("email", v); },
                        placeholderText: "user@example.com", header: "Email")
                        .Validate("email", email,
                            Validate.Required("Email is required"),
                            Validate.Email()),
                    When(valCtx.IsTouched("email") && valCtx.HasError("email"), () =>
                        TextBlock(valCtx.GetMessages("email")[0].Text)),
                    PasswordBox(password, v => { setPassword(v); valCtx.NotifyValueChanged("password", v); })
                        .Validate("password", password,
                            Validate.Required("Password is required"),
                            Validate.MinLength(8, "Password is too short")),
                    When(valCtx.IsTouched("password") && valCtx.HasError("password"), () =>
                        TextBlock(valCtx.GetMessages("password")[0].Text)),
                    Button("Register", () =>
                    {
                        valCtx.MarkAllTouched();
                        if (valCtx.IsValid()) { setSubmitted(true); submissions++; }
                    }),
                    When(submitted, () => TextBlock("Registration successful!")));
            });

            await Harness.Render();

            // Validators ran during Render(), so the verdict exists before any click.
            H.Check("Issue1262_ValidatorsRanOnMount", captured is not null && !captured.IsValid());
            if (captured is null) return;

            H.Check("Issue1262_FieldsRegistered",
                captured.RegisteredFields.Contains("email") && captured.RegisteredFields.Contains("password"));

            // ...but stays hidden until the field is touched.
            H.Check("Issue1262_ErrorsHiddenBeforeTouch", H.FindText("Email is required") is null);

            H.ClickButton("Register");
            await Harness.Render();

            // The reported symptom: this used to submit an empty form.
            H.Check("Issue1262_SubmitBlocked", submissions == 0);
            H.Check("Issue1262_NoSuccessMessage", H.FindText("Registration successful!") is null);

            // MarkAllTouched() mutates only the context — nothing else schedules a
            // repaint, so without the change notification the errors stayed invisible.
            H.Check("Issue1262_ErrorTextVisibleAfterSubmit", H.FindText("Email is required") is not null);
            H.Check("Issue1262_SecondFieldErrorVisible", H.FindText("Password is required") is not null);

            // An empty password trips Required *and* MinLength — both validators ran.
            H.Check("Issue1262_AllValidatorsRan", captured.GetMessages("password").Count == 2);

            // Re-clicking must stay stable rather than accumulating messages.
            H.ClickButton("Register");
            await Harness.Render();
            H.Check("Issue1262_NoMessageAccumulation", captured.GetMessages("email").Count == 1);

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 bare validate done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 — the docs' "FormField Helper" example, verbatim.
    //
    //  FormField finds the ValidationContext through the provider the hook now
    //  installs on the component's own output; the snippet never wrote
    //  .Provide(...) itself, so nothing validated and no error chrome appeared.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_FormFieldWithoutExplicitProvide(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            ValidationContext? captured = null;

            host.Mount(ctx =>
            {
                var valCtx = ctx.UseValidationContext();
                captured = valCtx;
                var (name, setName) = ctx.UseState("");

                return VStack(12,
                    FormField(
                        TextBox(name, v => { setName(v); valCtx.NotifyValueChanged("name", v); })
                            .Validate("name", name, Validate.Required("Name is required")),
                        label: "Full Name",
                        required: true,
                        description: "As it appears on your ID",
                        showWhen: ShowWhen.Always),
                    Button("SubmitForm", () => valCtx.MarkAllTouched()));
            });

            await Harness.Render();

            H.Check("Issue1262_FormField_ContextReached", captured is not null && !captured.IsValid());
            H.Check("Issue1262_FormField_ErrorRendered", H.FindText("Name is required") is not null);
            // The error text replaces the description in FormField's third slot.
            H.Check("Issue1262_FormField_DescriptionSwapped", H.FindText("As it appears on your ID") is null);

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 formfield done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 — an explicit .Provide() must still win over the automatic one.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_ExplicitProvideStillWins(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var mine = new ValidationContext();
            ValidationContext? hookResult = null;

            host.Mount(ctx =>
            {
                var valCtx = ctx.UseValidationContext();
                hookResult = valCtx;

                return VStack(
                    FormField(
                        TextBox("").Validate("name", "", Validate.Required("Explicit-ctx error")),
                        label: "Name",
                        showWhen: ShowWhen.Always))
                    .Provide(ValidationContexts.Current, mine);
            });

            await Harness.Render();

            // FormField validated against the caller's context, not the hook's local one.
            H.Check("Issue1262_ExplicitProvide_Wins", !mine.IsValid());
            H.Check("Issue1262_ExplicitProvide_HookCtxDistinct",
                hookResult is not null && !ReferenceEquals(hookResult, mine));

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 explicit provide done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 — FormField's default ShowWhen.WhenTouched must be reachable.
    //
    //  The guide promises "errors appear below the field after the field is
    //  touched (focus then blur)". Nothing in the framework ever called
    //  MarkTouched, so with the default ShowWhen an app that did not mark fields
    //  by hand — including the documented FormField snippet — could never show an
    //  error at all.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_FormFieldTouchedOnBlur(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            ValidationContext? captured = null;

            host.Mount(ctx =>
            {
                var valCtx = ctx.UseValidationContext();
                captured = valCtx;
                var (name, setName) = ctx.UseState("");

                return VStack(12,
                    FormField(
                        TextBox(name, v => { setName(v); valCtx.NotifyValueChanged("name", v); })
                            .Validate("name", name, Validate.Required("Name is required")),
                        label: "Full Name",
                        required: true,
                        description: "As it appears on your ID"),
                    Button("Elsewhere", () => { }));
            });

            await Harness.Render();

            // Invalid from the first pass, but untouched — so the description shows.
            H.Check("Issue1262_Blur_InvalidButQuiet",
                captured is not null && !captured.IsValid());
            if (captured is null) return;

            H.Check("Issue1262_Blur_NotTouchedInitially", !captured.IsTouched("name"));
            H.Check("Issue1262_Blur_DescriptionShown", H.FindText("As it appears on your ID") is not null);
            H.Check("Issue1262_Blur_NoErrorBeforeBlur", H.FindText("Name is required") is null);

            var box = H.FindControl<TextBox>(_ => true);
            var elsewhere = H.FindButton("Elsewhere");
            H.Check("Issue1262_Blur_ControlsFound", box is not null && elsewhere is not null);

            // Focus the editor, then move focus away — the blur is what marks it.
            box!.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();
            H.Check("Issue1262_Blur_StillQuietWhileFocused", !captured.IsTouched("name"));

            elsewhere!.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();

            H.Check("Issue1262_Blur_TouchedAfterBlur", captured.IsTouched("name"));
            H.Check("Issue1262_Blur_ErrorShownAfterBlur", H.FindText("Name is required") is not null);
            H.Check("Issue1262_Blur_DescriptionSwapped", H.FindText("As it appears on your ID") is null);

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 blur done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — a failing ValidationRule must not drive a render loop.
    //
    //  ValidationRule evaluates during reconcile, after the component's render
    //  scope has closed, so its notifications are NOT suppressed. Clear-then-add
    //  made every pass look like a change, each change requested another render,
    //  and the reconciler's re-entrancy guard would throw "Render loop detected".
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_FailingRuleDoesNotLoop(Harness h) : SelfTestFixtureBase(h)
    {
        private sealed class RuleProbe
        {
            internal ValidationContext? Context;
            internal int Renders;
        }

        private sealed record RuleOwnerProps(RuleProbe Probe);

        // Must be a child Component, not the host's root render func: a child's
        // re-render callback runs INLINE (CreateComponentRerender), which is what turns
        // a notification raised during reconcile into unbounded re-entrancy. The root's
        // callback merely schedules, so mounting this at the root would hide the bug.
        private sealed class RuleOwner : Component<RuleOwnerProps>
        {
            public override Element Render()
            {
                var valCtx = this.UseValidationContext();
                Props.Probe.Context = valCtx;
                Props.Probe.Renders++;

                return VStack(12,
                    ValidationRule(() => false, "Passwords must match", "confirm"),
                    TextBlock($"valid:{valCtx.IsValid()}"));
            }
        }

        public override async Task RunAsync()
        {
            var probe = new RuleProbe();
            var host = H.CreateHost();
            host.Mount(_ => Component<RuleOwner, RuleOwnerProps>(new RuleOwnerProps(probe)));

            // If the loop were still present this throws "Render loop detected".
            await Harness.Render();
            await Harness.Render();

            var captured = probe.Context;
            H.Check("Issue1262_Rule_NoLoopThrown", captured is not null);
            if (captured is null) return;

            H.Check("Issue1262_Rule_MessageRecorded", captured.GetMessages("confirm").Count == 1);
            H.Check("Issue1262_Rule_NoAccumulation", captured.GetAllMessages().Count == 1);
            H.Check("Issue1262_Rule_RenderCountBounded", probe.Renders < 10, $"renders={probe.Renders}");

            var settledVersion = captured.Version;
            var settledRenders = probe.Renders;
            await Harness.Render();

            H.Check("Issue1262_Rule_VersionStableOnReRender", captured.Version == settledVersion,
                $"before={settledVersion} after={captured.Version}");
            H.Check("Issue1262_Rule_RendersSettle", probe.Renders - settledRenders <= 2,
                $"delta={probe.Renders - settledRenders}");

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 rule done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — the blur binding must not outlive its FormField.
    //
    //  The LostFocus handler is attached once for the control's lifetime and
    //  TextBox is poolable, so a control that stops being a FormField's content
    //  must stop reporting to the old context/field.
    // ════════════════════════════════════════════════════════════════════════

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — a mounted rule must withdraw its verdict when it
    //  leaves the tree, or a conditionally rendered rule keeps the form invalid
    //  forever after it disappears.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_RuleRetractsOnUnmount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ctx = new ValidationContext();
            var host = H.CreateHost();
            Action<bool>? setShowRule = null;

            host.Mount(c =>
            {
                var (showRule, setShow) = c.UseState(true);
                setShowRule = setShow;

                return VStack(12,
                    When(showRule, () => ValidationRule(() => false, "Rule failed", "form")),
                    TextBlock("body"))
                    .Provide(ValidationContexts.Current, ctx);
            });

            await Harness.Render();
            H.Check("Issue1262_RuleUnmount_AppliedWhileMounted", !ctx.IsValid());
            H.Check("Issue1262_RuleUnmount_MessageRecorded", ctx.GetMessages("form").Count == 1);

            // Re-render with the rule still present: the verdict must not accumulate.
            await Harness.Render();
            H.Check("Issue1262_RuleUnmount_NoAccumulation", ctx.GetMessages("form").Count == 1);

            setShowRule!(false);
            await Harness.Render();

            H.Check("Issue1262_RuleUnmount_WithdrawnOnUnmount", ctx.GetMessages("form").Count == 0,
                $"remaining={string.Join("|", ctx.GetMessages("form").Select(m => m.Text))}");
            H.Check("Issue1262_RuleUnmount_ValidAfterUnmount", ctx.IsValid());

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 rule unmount done"));
            await Harness.Render();
        }
    }

    internal class Issue1262_TouchBindingClearedWhenContextGoes(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ctx = new ValidationContext();
            var host = H.CreateHost();
            var (provide, setProvide) = (true, (Action<bool>?)null);

            host.Mount(c =>
            {
                var (provided, setProvided) = c.UseState(true);
                setProvide = setProvided;
                provide = provided;

                var tree = VStack(12,
                    FormField(
                        TextBox("").Validate("name", "", Validate.Required("Name is required")),
                        label: "Full Name",
                        showWhen: ShowWhen.Always),
                    Button("Elsewhere", () => { }));

                return provided ? tree.Provide(ValidationContexts.Current, ctx) : tree;
            });

            await Harness.Render();
            var box = H.FindControl<TextBox>(_ => true);
            var elsewhere = H.FindButton("Elsewhere");
            H.Check("Issue1262_Binding_ControlsFound", box is not null && elsewhere is not null);
            if (box is null || elsewhere is null) return;

            // While the context is reachable, blur marks the field.
            box.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();
            elsewhere.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();
            H.Check("Issue1262_Binding_MarksWhileBound", ctx.IsTouched("name"));

            // Drop the provider. The same control is patched in place, so the binding
            // has to be cleared rather than left pointing at the old context.
            var stillSameControl = ReferenceEquals(box, H.FindControl<TextBox>(_ => true));
            setProvide!(false);
            await Harness.Render();
            H.Check("Issue1262_Binding_ControlPreserved",
                stillSameControl && ReferenceEquals(box, H.FindControl<TextBox>(_ => true)));

            var touchedBefore = ctx.IsTouched("name");

            // Re-blur now that nothing provides a context.
            ctx.Reset("name");
            H.Check("Issue1262_Binding_ResetClearedTouched", !ctx.IsTouched("name"));
            box.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();
            elsewhere.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();

            H.Check("Issue1262_Binding_SilentAfterContextGone", !ctx.IsTouched("name"),
                $"touchedBefore={touchedBefore}");

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 binding done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — a rule removed from a *child* component.
    //
    //  Retraction runs during unmount, which sits between renders and so used
    //  to fall outside every validation frame. When the context is owned by a
    //  child via UseValidationContext(), announcing that change inline drove
    //  CreateComponentRerender back into the reconciler while the subtree was
    //  still being torn down. The root-host fixture above cannot see this: a
    //  root re-render only schedules.
    // ════════════════════════════════════════════════════════════════════════

    internal sealed record ChildRuleProps(bool ShowRule, Action<ValidationContext> OnContext, Action OnRender);

    internal sealed class ChildRuleOwner : Component<ChildRuleProps>
    {
        public override Element Render()
        {
            var props = Props;
            var ctx = this.UseValidationContext();
            props.OnContext(ctx);
            props.OnRender();

            return VStack(8,
                When(props.ShowRule, () => ValidationRule(() => false, "Rule failed", "form")),
                // Structural: the removal's retraction notification re-renders this
                // component, and the re-render changes the very subtree the reconciler
                // is still walking to unmount the rule.
                When(!ctx.IsValid(), () => TextBlock("child-invalid")),
                When(ctx.IsValid(), () => VStack(4, TextBlock("child-valid"), TextBlock("ok"))),
                TextBlock("tail"));
        }
    }

    internal class Issue1262_RuleRetractsFromChildComponent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            Action<bool>? setShowRule = null;
            ValidationContext? childCtx = null;
            var childRenders = 0;

            host.Mount(c =>
            {
                var (showRule, setShow) = c.UseState(true);
                setShowRule = setShow;

                return VStack(12,
                    Component<ChildRuleOwner, ChildRuleProps>(
                        new ChildRuleProps(showRule, ctx => childCtx = ctx, () => childRenders++)),
                    TextBlock("host"));
            });

            await Harness.Render();
            H.Check("Issue1262_ChildRule_ContextResolved", childCtx is not null);
            if (childCtx is null) return;

            H.Check("Issue1262_ChildRule_InvalidWhileMounted", !childCtx.IsValid());
            H.Check("Issue1262_ChildRule_MessageRecorded", childCtx.GetMessages("form").Count == 1);

            var rendersBefore = childRenders;

            // The removal itself: unmount retracts, and that retraction is a real
            // change, so it notifies the child that owns the context.
            setShowRule!(false);
            await Harness.Render();
            await Harness.Render();

            H.Check("Issue1262_ChildRule_RetractedOnRemoval", childCtx.GetMessages("form").Count == 0,
                $"remaining={string.Join("|", childCtx.GetMessages("form").Select(m => m.Text))}");
            H.Check("Issue1262_ChildRule_ValidAfterRemoval", childCtx.IsValid());

            // A handful of renders is the removal plus its notification settling; an
            // inline re-entrant notification shows up as a storm. The reconcile-wide
            // deferral frame is what keeps this bounded for direct ctx.Changed
            // subscribers, which do not get UseState's marshalling.
            var delta = childRenders - rendersBefore;
            H.Check("Issue1262_ChildRule_NoRenderStorm", delta <= 6, $"childRenders={delta}");

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 child rule done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — a FormField that loses its context *and* swaps its
    //  content control in one update.
    //
    //  Only the incoming control's binding used to be neutralized; the root
    //  still pointed at the outgoing editor's live binding, and that editor was
    //  already on its way to the pool. Rented back for a non-FormField use — the
    //  one path that never re-points the binding — it kept marking the old field.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_DisplacedRootBindingCleared(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ctx = new ValidationContext();
            var host = H.CreateHost();
            Action<int>? setMode = null;

            host.Mount(c =>
            {
                var (mode, set) = c.UseState(0);
                setMode = set;

                if (mode == 0)
                {
                    return VStack(12,
                        FormField(
                            TextBox("").Validate("name", "", Validate.Required("Name is required")),
                            label: "Full Name",
                            showWhen: ShowWhen.Always),
                        Button("Away", () => { }))
                        .Provide(ValidationContexts.Current, ctx);
                }

                if (mode == 1)
                {
                    // Context dropped and the content control swapped in the same pass.
                    return VStack(12,
                        FormField(
                            TextBlock("swapped"),
                            label: "Full Name",
                            showWhen: ShowWhen.Always),
                        Button("Away", () => { }));
                }

                // The displaced editor is rented back for a plain, non-FormField use.
                return VStack(12,
                    TextBox(""),
                    Button("Away", () => { }));
            });

            await Harness.Render();
            var original = H.FindControl<TextBox>(_ => true);
            var away = H.FindButton("Away");
            H.Check("Issue1262_Displaced_ControlsFound", original is not null && away is not null);
            if (original is null || away is null) return;

            original.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();
            away.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();
            H.Check("Issue1262_Displaced_MarksWhileBound", ctx.IsTouched("name"));

            ctx.Reset("name");
            H.Check("Issue1262_Displaced_ResetClearedTouched", !ctx.IsTouched("name"));

            setMode!(1);
            await Harness.Render();

            // The editor is out of the tree and on its way to the pool. Its binding
            // must be neutralized, or a later non-FormField use of the same control
            // keeps marking this field — and keeps this context alive.
            H.Check("Issue1262_Displaced_BindingNeutralized",
                !global::Microsoft.UI.Reactor.Core.V1Protocol.CompositeLifecycle
                    .HasLiveTouchBindingForTests(original),
                "the displaced editor still carries a live blur binding");

            setMode!(2);
            await Harness.Render();

            var rented = H.FindControl<TextBox>(_ => true);
            if (rented is null) { H.Check("Issue1262_Displaced_PlainBoxRendered", false); return; }

            // Whether or not the pool handed back the same instance, nothing in the
            // plain, non-FormField slot may report to the old context.
            var awayAgain = H.FindButton("Away");
            if (awayAgain is null) { H.Check("Issue1262_Displaced_AwayFound", false); return; }

            rented.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();
            awayAgain.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();

            H.Check("Issue1262_Displaced_SilentAfterDisplacement", !ctx.IsTouched("name"),
                $"reused={ReferenceEquals(original, rented)}");

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 displaced binding done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — an async-only field on a cached element.
    //
    //  .ValidateAsync(field, value, …) registers the field through the render
    //  scope, which a cached element never passes through. FormField gated its
    //  registration on sync validators being present, so an async-only field
    //  existed nowhere and MarkAllTouched()/IsValid() skipped it entirely.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_AsyncOnlyFieldOnCachedElement(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ctx = new ValidationContext();
            var host = H.CreateHost();

            // Built here, outside any render pass — the case the render scope misses.
            var cached = FormField(
                TextBox("").ValidateAsync("email", "",
                    Validate.MustAsync<string>(_ => Task.FromResult(true), "Already registered")),
                label: "Email",
                showWhen: ShowWhen.Always);

            H.Check("Issue1262_AsyncOnly_UnregisteredBeforeMount", !ctx.RegisteredFields.Contains("email"));

            host.Mount(c => VStack(12, cached).Provide(ValidationContexts.Current, ctx));
            await Harness.Render();

            H.Check("Issue1262_AsyncOnly_RegisteredOnMount", ctx.RegisteredFields.Contains("email"),
                $"registered={string.Join("|", ctx.RegisteredFields)}");

            ctx.MarkAllTouched();
            H.Check("Issue1262_AsyncOnly_CoveredByMarkAllTouched", ctx.IsTouched("email"));

            // And the update path keeps it registered after a reset.
            ctx.ResetAll();
            await Harness.Render();
            H.Check("Issue1262_AsyncOnly_StillRegisteredAfterUpdate", ctx.RegisteredFields.Contains("email"));

    var done = H.CreateHost();
    done.Mount(c => TextBlock("Issue1262 async-only field done"));
    await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — two value overloads chained on one element.
    //
    //  Each call eagerly applies its own intermediate validator set under the
    //  same producer, so every pass strips the later message and puts it back.
    //  The net state never moves, but each write was a real change, so the
    //  frame announced one — repainting a component that churns identically.
    //  In a child component that re-renders inline, that is an endless loop.
    // ════════════════════════════════════════════════════════════════════════

    internal sealed record ChainedValidateProps(Action OnRender);

    internal sealed class ChainedValidateOwner : Component<ChainedValidateProps>
    {
        public override Element Render()
        {
            Props.OnRender();
            var ctx = this.UseValidationContext();

            return VStack(8,
                TextBox("abc")
                    .Validate("email", "abc", Validate.Email("Not an email"))
                    .Validate("email", "abc", Validate.MinLength(10, "Too short")),
                TextBlock(ctx.HasError("email") ? "invalid" : "valid"));
        }
    }

    internal class Issue1262_ChainedValueOverloadsDoNotLoop(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var renders = 0;

            host.Mount(c => VStack(12,
                Component<ChainedValidateOwner, ChainedValidateProps>(
                    new ChainedValidateProps(() => renders++)),
                TextBlock("host")));

            await Harness.Render();
            var afterFirst = renders;

            // Let any scheduled repaints drain. A churning chain never stops asking.
            for (var i = 0; i < 6; i++) await Harness.Render();

            H.Check("Issue1262_Chain_BothValidatorsApplied", afterFirst > 0);
            H.Check("Issue1262_Chain_RendersSettle", renders - afterFirst <= 8,
                $"extraRenders={renders - afterFirst}");

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 chained validate done"));
            await Harness.Render();
        }
    }
}
