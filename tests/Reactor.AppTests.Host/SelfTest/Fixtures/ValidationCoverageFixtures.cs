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

            // Sync rule — fails, then the *same rule* (same call site) passes and
            // retracts its own message. A rule's identity is its code location, not its
            // message text, so re-running one call site is what models "this rule again"
            // (issue #1262 review).
            var field1Ok = false;
            void RunSyncRule() => ValidationRule(() => field1Ok, "Must be valid", "field1").Evaluate(ctx);

            RunSyncRule();
            H.Check("ValRule_SyncFail", ctx.HasError("field1"));

            field1Ok = true;
            RunSyncRule();
            H.Check("ValRule_SyncPass", !ctx.HasError("field1"));

            // Async rule — fails, then the same call site passes and retracts. Using a
            // *different* call site here would assert the old whole-field-replace
            // behaviour, where any passing rule erased every other producer's errors.
            var field2Ok = false;
            async Task RunAsyncRule() => await ValidationRuleAsync(
                async () => { await Task.Delay(1); return field2Ok; },
                "Async fail", "field2").EvaluateAsync(ctx);

            await RunAsyncRule();
            H.Check("ValRule_AsyncFail", ctx.HasError("field2"));

            field2Ok = true;
            await RunAsyncRule();
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
                        TextBox("binding-probe").Validate("name", "binding-probe", Validate.MinLength(50)),
                        label: "Full Name",
                        showWhen: ShowWhen.Always),
                    Button("Elsewhere", () => { }));

                return provided ? tree.Provide(ValidationContexts.Current, ctx) : tree;
            });

            await Harness.Render();
            // Matched by text: other fixtures leave TextBoxes in the search root, so
            // "the first TextBox" is not reliably this one.
            var box = H.FindControl<TextBox>(tb => tb.Text == "binding-probe");
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
            var stillSameControl = ReferenceEquals(box, H.FindControl<TextBox>(tb => tb.Text == "binding-probe"));
            setProvide!(false);
            await Harness.Render();
            H.Check("Issue1262_Binding_ControlPreserved",
                stillSameControl && ReferenceEquals(box, H.FindControl<TextBox>(tb => tb.Text == "binding-probe")));

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
                            TextBox("displaced-probe").Validate("name", "displaced-probe", Validate.MinLength(50)),
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
                    TextBox("plain-probe"),
                    Button("Away", () => { }));
            });

            await Harness.Render();
            // Matched by text, not by type: other fixtures in the same run leave
            // TextBoxes in the search root, so "the first TextBox" is not reliably this
            // one — which made the fixture order-dependent.
            var original = H.FindControl<TextBox>(tb => tb.Text == "displaced-probe");
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
            // Wait for the swap to be observable rather than assuming one render is
            // enough — under load a single pass may not have applied the state change,
            // and asserting then reads the *old* tree.
            for (var i = 0; i < 8 && H.FindControl<TextBox>(tb => tb.Text == "displaced-probe") is not null; i++)
                await Harness.Render();

            H.Check("Issue1262_Displaced_EditorLeftTree",
                H.FindControl<TextBox>(tb => tb.Text == "displaced-probe") is null,
                "the content swap never took effect, so the check below would be vacuous");

            // The editor is out of the tree and on its way to the pool. Its binding
            // must be neutralized, or a later non-FormField use of the same control
            // keeps marking this field — and keeps this context alive.
            H.Check("Issue1262_Displaced_BindingNeutralized",
                !global::Microsoft.UI.Reactor.Core.V1Protocol.CompositeLifecycle
                    .HasLiveTouchBindingForTests(original),
                "the displaced editor still carries a live blur binding");

            setMode!(2);
            for (var i = 0; i < 8 && H.FindControl<TextBox>(tb => tb.Text == "plain-probe") is null; i++)
                await Harness.Render();

            var rented = H.FindControl<TextBox>(tb => tb.Text == "plain-probe");
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

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — a mounted async rule must actually run.
    //
    //  ValidationRuleAsync builds an element whose synchronous predicate is a
    //  constant true, and both lifecycle paths called Evaluate — so placing an
    //  async rule in the tree recorded a passing verdict and never invoked the
    //  predicate at all.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_MountedAsyncRuleRuns(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ctx = new ValidationContext();
            var host = H.CreateHost();
            var invocations = 0;
            Action<bool>? setShowRule = null;

            host.Mount(c =>
            {
                var (showRule, setShow) = c.UseState(true);
                setShowRule = setShow;

                return VStack(12,
                    When(showRule, () => ValidationRuleAsync(
                        () =>
                        {
                            invocations++;
                            return Task.FromResult(false);
                        },
                        "Name is already taken",
                        "name")),
                    TextBlock("body"))
                    .Provide(ValidationContexts.Current, ctx);
            });

            await Harness.Render();
            for (var i = 0; i < 4 && ctx.GetMessages("name").Count == 0; i++)
                await Harness.Render();

            H.Check("Issue1262_AsyncRule_PredicateInvoked", invocations > 0, $"invocations={invocations}");
            H.Check("Issue1262_AsyncRule_VerdictInstalled", ctx.GetMessages("name").Count == 1,
                $"messages={ctx.GetMessages("name").Count}");
            H.Check("Issue1262_AsyncRule_Invalid", !ctx.IsValid());

            // Re-rendering must not accumulate: the producer slot is replaced, not appended.
            await Harness.Render();
            await Harness.Render();
            H.Check("Issue1262_AsyncRule_NoAccumulation", ctx.GetMessages("name").Count == 1,
                $"messages={ctx.GetMessages("name").Count}");

            // And removal retracts it, cancelling any pass still in flight.
            setShowRule!(false);
            await Harness.Render();
            await Harness.Render();
            H.Check("Issue1262_AsyncRule_RetractedOnRemoval", ctx.GetMessages("name").Count == 0,
                $"remaining={string.Join("|", ctx.GetMessages("name").Select(m => m.Text))}");

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 async rule done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — an async rule whose provider disappears mid-flight.
    //
    //  Update withdraws the rule's contribution from the old context, but a
    //  pass already running would resolve afterwards and reinstall the error
    //  into a context the rule no longer belongs to.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_AsyncRuleProviderRemovedMidFlight(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ctx = new ValidationContext();
            var gate = new TaskCompletionSource<bool>();
            var host = H.CreateHost();
            Action<bool>? setProvide = null;

            host.Mount(c =>
            {
                var (provided, set) = c.UseState(true);
                setProvide = set;

                var tree = VStack(12,
                    ValidationRuleAsync(() => gate.Task, "Name is already taken", "name"),
                    TextBlock("body"));

                return provided ? tree.Provide(ValidationContexts.Current, ctx) : tree;
            });

            await Harness.Render();
            H.Check("Issue1262_AsyncProvider_QuietWhilePending", ctx.GetMessages("name").Count == 0);

            // The provider goes away while the check is still out.
            setProvide!(false);
            await Harness.Render();

            gate.SetResult(false);
            await Harness.Render();
            await Harness.Render();

            H.Check("Issue1262_AsyncProvider_NoVerdictAfterRemoval", ctx.GetMessages("name").Count == 0,
                $"remaining={string.Join("|", ctx.GetMessages("name").Select(m => m.Text))}");
            H.Check("Issue1262_AsyncProvider_ContextStillValid", ctx.IsValid());

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 async provider done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — a mounted rule that stops being async.
    //
    //  The placeholder, and therefore the producer identity, survives the swap.
    //  Leaving the old async generation entry behind made the next value change
    //  treat that producer as async and retract a synchronous verdict that was
    //  still current.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_AsyncRuleBecomesSync(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ctx = new ValidationContext();
            var host = H.CreateHost();
            Action<bool>? setUseAsync = null;

            host.Mount(c =>
            {
                var (useAsync, set) = c.UseState(true);
                setUseAsync = set;

                return VStack(12,
                    useAsync
                        ? ValidationRuleAsync(() => Task.FromResult(false), "Rule failed", "name")
                        : ValidationRule(() => false, "Rule failed", "name"),
                    TextBlock("body"))
                    .Provide(ValidationContexts.Current, ctx);
            });

            await Harness.Render();
            for (var i = 0; i < 4 && ctx.GetMessages("name").Count == 0; i++)
                await Harness.Render();
            H.Check("Issue1262_RuleSwap_AsyncVerdictInstalled", ctx.GetMessages("name").Count == 1,
                $"messages={ctx.GetMessages("name").Count}");

            // Same placeholder, now a synchronous rule.
            setUseAsync!(false);
            await Harness.Render();
            await Harness.Render();
            H.Check("Issue1262_RuleSwap_SyncVerdictInstalled", ctx.GetMessages("name").Count == 1,
                $"messages={ctx.GetMessages("name").Count}");

            // A value change retires async producers. The swapped rule is no longer one,
            // so its verdict has to survive — checked before any re-render could
            // reinstall it.
            ctx.NotifyValueChanged("name", "anything");
            H.Check("Issue1262_RuleSwap_SyncVerdictSurvivesValueChange",
                ctx.GetMessages("name").Count == 1,
                $"messages={ctx.GetMessages("name").Count}");

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 rule swap done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — a validator-only attachment inside a FormField.
    //
    //  .Validate(field, validators…) supplies no value, and null is a legitimate
    //  value, so FormField validated null and reported "required" for a control
    //  that plainly had text in it.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_ValidatorOnlyAttachmentInFormField(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ctx = new ValidationContext();
            var host = H.CreateHost();

            host.Mount(c => VStack(12,
                FormField(
                    TextBox("Alice").Validate("name", Validate.Required("Name is required")),
                    label: "Full Name",
                    showWhen: ShowWhen.Always))
                .Provide(ValidationContexts.Current, ctx));

            await Harness.Render();

            H.Check("Issue1262_ValidatorOnly_NoSpuriousError", ctx.GetMessages("name").Count == 0,
                $"messages={string.Join("|", ctx.GetMessages("name").Select(m => m.Text))}");
            H.Check("Issue1262_ValidatorOnly_NoErrorRendered", H.FindText("Name is required") is null);

            // Still registered, so a submit-time MarkAllTouched() covers the field.
            H.Check("Issue1262_ValidatorOnly_FieldRegistered", ctx.RegisteredFields.Contains("name"),
                $"registered={string.Join("|", ctx.RegisteredFields)}");

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 validator-only done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — a control retired outside the normal unmount path.
    //
    //  DetachReactorState is reached when a control is discarded without the
    //  FormField/ValidationRule unmount callback running. The blur binding is
    //  captured by a once-per-lifetime LostFocus handler that survives detach, so
    //  a binding left live would keep marking the old field — and keep its
    //  ValidationContext alive.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_DetachClearsValidationBindings(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ctx = new ValidationContext();
            var host = H.CreateHost();

            host.Mount(c => VStack(12,
                FormField(
                    TextBox("detach-probe").Validate("name", "detach-probe", Validate.MinLength(50)),
                    label: "Full Name",
                    showWhen: ShowWhen.Always),
                Button("Away", () => { }))
                .Provide(ValidationContexts.Current, ctx));

            await Harness.Render();
            var box = H.FindControl<TextBox>(tb => tb.Text == "detach-probe");
            var away = H.FindButton("Away");
            H.Check("Issue1262_Detach_ControlsFound", box is not null && away is not null);
            if (box is null || away is null) return;

            box.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();
            away.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();
            H.Check("Issue1262_Detach_MarksWhileBound", ctx.IsTouched("name"));

            ctx.Reset("name");
            H.Check("Issue1262_Detach_ResetClearedTouched", !ctx.IsTouched("name"));

            // Retire the control directly, bypassing the FormField unmount path.
            global::Microsoft.UI.Reactor.Core.Reconciler.DetachReactorState(box);

            H.Check("Issue1262_Detach_BindingNeutralized",
                !global::Microsoft.UI.Reactor.Core.V1Protocol.CompositeLifecycle
                    .HasLiveTouchBindingForTests(box),
                "the retired editor still carries a live blur binding");

            // And the once-per-lifetime handler is now inert.
            box.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();
            away.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();
            H.Check("Issue1262_Detach_SilentAfterDetach", !ctx.IsTouched("name"));

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 detach done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — a mounted rule's placeholder retired outside unmount.
    //
    //  Detach has to withdraw the rule's verdict as well as cancel its pass, or
    //  the error outlives the control that produced it and nothing will ever
    //  re-evaluate it away.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_DetachRetiresRuleVerdict(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ctx = new ValidationContext();
            var host = H.CreateHost();

            host.Mount(c => VStack(12,
                ValidationRule(() => false, "Rule failed", "form"),
                TextBlock("body"))
                .Provide(ValidationContexts.Current, ctx));

            await Harness.Render();
            H.Check("Issue1262_DetachRule_VerdictInstalled", ctx.GetMessages("form").Count == 1,
                $"messages={ctx.GetMessages("form").Count}");

            // The rule's collapsed placeholder is the first child of the panel.
            var placeholder = H.FindControl<Microsoft.UI.Xaml.Controls.StackPanel>(
                p => p.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed);
            H.Check("Issue1262_DetachRule_PlaceholderFound", placeholder is not null);
            if (placeholder is null) return;

            global::Microsoft.UI.Reactor.Core.Reconciler.DetachReactorState(placeholder);

            H.Check("Issue1262_DetachRule_VerdictWithdrawn", ctx.GetMessages("form").Count == 0,
                $"remaining={string.Join("|", ctx.GetMessages("form").Select(m => m.Text))}");
            H.Check("Issue1262_DetachRule_ValidAfterDetach", ctx.IsValid());

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 detach rule done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — a context mutated from a worker thread.
    //
    //  UseValidationContext re-renders through the *default* (marshalling)
    //  UseState setter rather than threadSafe: threadSafe would invoke the
    //  re-render callback on whatever thread raised Changed, and an async
    //  validator raises it from a worker — entering the reconciler off the UI
    //  thread.
    //
    //  What this fixture does and does not establish. It does establish that a
    //  worker-thread mutation neither throws nor renders on the worker, and
    //  that the repaint it causes arrives on the UI thread. It does NOT make
    //  the threadSafe choice falsifiable: flipping that setter leaves every
    //  check here green, so the marshalling that saves us lives somewhere
    //  below the setter and this fixture cannot attribute it. An earlier
    //  version of this comment claimed otherwise.
    // ════════════════════════════════════════════════════════════════════════

    internal sealed record OffThreadProps(Action<ValidationContext> OnContext, Action<int> OnRender);

    internal sealed class OffThreadValidationOwner : Component<OffThreadProps>
    {
        public override Element Render()
        {
            var props = Props;
            var ctx = this.UseValidationContext();
            props.OnContext(ctx);
            props.OnRender(global::System.Environment.CurrentManagedThreadId);

            return VStack(8,
                When(ctx.HasError("email"), () => TextBlock("Email is taken")),
                TextBlock("body"));
        }
    }

    internal class Issue1262_OffThreadContextMutationMarshals(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var uiThreadId = global::System.Environment.CurrentManagedThreadId;
            // Appended from the render (UI thread) and read from the worker, so a plain
            // List races even when nothing renders off-thread.
            var renderThreads = new global::System.Collections.Concurrent.ConcurrentQueue<int>();
            ValidationContext? captured = null;

            host.Mount(c => VStack(12,
                Component<OffThreadValidationOwner, OffThreadProps>(
                    new OffThreadProps(ctx => captured = ctx, renderThreads.Enqueue)),
                TextBlock("host")));

            await Harness.Render();
            H.Check("Issue1262_OffThread_ContextResolved", captured is not null);
            if (captured is null) return;

            H.Check("Issue1262_OffThread_NoErrorInitially", H.FindText("Email is taken") is null);
            var rendersBefore = renderThreads.Count;

            // Exactly what a background async validator does when it resolves.
            global::System.Exception? thrown = null;
            var workerThreadId = -1;
            await Task.Run(() =>
            {
                workerThreadId = global::System.Environment.CurrentManagedThreadId;
                try { captured.Add("email", "Email is taken"); }
                catch (global::System.Exception ex)
                    when (ex is not global::System.OutOfMemoryException
                          and not global::System.StackOverflowException)
                {
                    // Deliberately broad: the assertion below is "any failure at all",
                    // since the defect this guards against is an off-thread reconcile
                    // surfacing as a COM or invalid-operation exception.
                    thrown = ex;
                }
            });

            H.Check("Issue1262_OffThread_MutationDidNotThrow", thrown is null,
                thrown is null ? "" : $"{thrown.GetType().Name}: {thrown.Message}");

            // The load-bearing assertion: the mutation must not have driven a render on
            // the worker. A threadSafe state setter invokes the re-render callback on
            // whatever thread raised Changed, which for a child component renders inline.
            //
            // Attributed by thread id, not by counting renders. Comparing a count taken
            // on the worker against one taken before it asks "did the counter move",
            // which a *marshalled* render landing on the UI thread in that same window
            // also satisfies — so the check reddened under AOT while every render was in
            // fact on the UI thread, as its sibling below confirmed. The healthy and
            // broken branches were separated only by timing, which is no separation.
            var workerRenders = renderThreads.Where(id => id == workerThreadId).ToList();
            H.Check("Issue1262_OffThread_NoSynchronousRenderFromWorker",
                workerThreadId != -1 && workerRenders.Count == 0,
                $"worker={workerThreadId} rendersOnWorker={workerRenders.Count}");

            // Positive control for the detector above. A zero from a working filter and
            // a zero from one that can never match read identically, and this one cannot
            // be falsified by mutating the product: flipping UseValidationContext's
            // setter to threadSafe: true — the very thing this fixture exists to justify
            // — leaves both checks green, so the re-render must be marshalled somewhere
            // further down than the setter. Rather than claim coverage the mutation does
            // not support, prove the instrument instead: the same filter, over a queue
            // that deliberately holds a worker-thread entry, has to find it.
            var detectorProbe = new global::System.Collections.Concurrent.ConcurrentQueue<int>();
            detectorProbe.Enqueue(workerThreadId);
            H.Check("Issue1262_OffThread_DetectorCanMatchAWorkerRender",
                detectorProbe.Any(id => id == workerThreadId),
                $"worker={workerThreadId} probe={string.Join("|", detectorProbe)}");

            for (var i = 0; i < 6 && H.FindText("Email is taken") is null; i++)
                await Harness.Render();

            H.Check("Issue1262_OffThread_Repainted", H.FindText("Email is taken") is not null);
            H.Check("Issue1262_OffThread_RenderedAgain", renderThreads.Count > rendersBefore,
                $"renders={renderThreads.Count - rendersBefore}");

            // The point: every render ran on the UI thread, including the one the
            // worker-thread mutation caused.
            var offThread = renderThreads.Where(id => id != uiThreadId).ToList();
            H.Check("Issue1262_OffThread_AllRendersOnUiThread", offThread.Count == 0,
                $"ui={uiThreadId} offThread={string.Join("|", offThread)}");

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 off-thread done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — a FormField whose field name moves between passes.
    //
    //  The verdict is installed under the field's own sync producer, so without
    //  withdrawing the old one its messages stay owned by a field nothing
    //  validates any more — keeping the form invalid forever.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_FormFieldNameMigration(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ctx = new ValidationContext();
            var host = H.CreateHost();
            Action<bool>? setUsePhone = null;

            host.Mount(c =>
            {
                var (usePhone, set) = c.UseState(false);
                setUsePhone = set;
                var field = usePhone ? "phone" : "email";

                return VStack(12,
                    FormField(
                        TextBox("").Validate(field, "", Validate.Required($"{field} is required")),
                        label: "Contact",
                        showWhen: ShowWhen.Always))
                    .Provide(ValidationContexts.Current, ctx);
            });

            await Harness.Render();
            H.Check("Issue1262_FieldMove_InitialError", ctx.GetMessages("email").Count == 1,
                $"email={ctx.GetMessages("email").Count}");

            setUsePhone!(true);
            await Harness.Render();
            await Harness.Render();

            H.Check("Issue1262_FieldMove_NewFieldValidated", ctx.GetMessages("phone").Count == 1,
                $"phone={ctx.GetMessages("phone").Count}");
            H.Check("Issue1262_FieldMove_OldFieldWithdrawn", ctx.GetMessages("email").Count == 0,
                $"remaining={string.Join("|", ctx.GetMessages("email").Select(m => m.Text))}");

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 field move done"));
            await Harness.Render();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — validators or provider disappearing with the field
    //  name unchanged.
    //
    //  A moving field name is only one of the ways the sync contribution can
    //  stop applying: the validators can go away, or the provider can change,
    //  and either leaves the old verdict owned by nothing.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_AttachedValidationWithdrawnWhenItStops(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var ctx = new ValidationContext();
            var other = new ValidationContext();
            var host = H.CreateHost();
            Action<int>? setMode = null;

            host.Mount(c =>
            {
                var (mode, set) = c.UseState(0);
                setMode = set;

                // mode 0: validated. mode 1: same field, validators gone.
                // mode 2: validated again, but under a different provider.
                var content = mode == 1
                    ? TextBox("")
                    : TextBox("").Validate("email", "", Validate.Required("Email is required"));

                var tree = VStack(12,
                    FormField(content, label: "Email", showWhen: ShowWhen.Always));

                return tree.Provide(ValidationContexts.Current, mode == 2 ? other : ctx);
            });

            await Harness.Render();
            H.Check("Issue1262_Stops_InitialError", ctx.GetMessages("email").Count == 1,
                $"email={ctx.GetMessages("email").Count}");

            // The validators disappear while the field name stays the same.
            setMode!(1);
            await Harness.Render();
            await Harness.Render();
            H.Check("Issue1262_Stops_WithdrawnWhenValidatorsGo", ctx.GetMessages("email").Count == 0,
                $"remaining={string.Join("|", ctx.GetMessages("email").Select(m => m.Text))}");

            // Validated again, but against a different context.
            setMode!(2);
            await Harness.Render();
            await Harness.Render();
            H.Check("Issue1262_Stops_NewContextValidated", other.GetMessages("email").Count == 1,
                $"other={other.GetMessages("email").Count}");
            H.Check("Issue1262_Stops_OldContextStillEmpty", ctx.GetMessages("email").Count == 0,
                $"remaining={string.Join("|", ctx.GetMessages("email").Select(m => m.Text))}");

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 attached stop done"));
            await Harness.Render();
        }
    }


    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — a validated control leaving the tree, and chained
    //  links that move.
    //
    //  `.Validate(field, value, …)` installs its verdict while the owning
    //  component renders, and nothing watched what became of it: a control
    //  behind a condition installed an error on the pass that showed it and
    //  then simply stopped being rendered, leaving the context invalid over a
    //  field with no control. The same held for a whole FormField, and for a
    //  removed child, which leaves through the pooling traversal rather than
    //  the ordinary unmount.
    //
    //  Two guards ride along. Replacing a control installs the incoming
    //  verdict *before* the outgoing control is unmounted, so an unconditional
    //  retraction on the way out would erase the verdict that replaced it. And
    //  every link of a chain evaluates eagerly, so a link that moves the
    //  attachment to another field has to take the earlier link's verdict with
    //  it — while a link that only changes the value must not turn a net-zero
    //  pass into a repaint.
    // ════════════════════════════════════════════════════════════════════════

    internal enum BareValidateShape
    {
        /// <summary>Validated control present.</summary>
        Present,
        /// <summary>Replaced by an unvalidated element of another type.</summary>
        Hidden,
        /// <summary>Removed from the children entirely — the pooling teardown path.</summary>
        Removed,
        /// <summary>Same field, different control type: the replacement guard.</summary>
        Replaced,
        /// <summary>Validated element built and then dropped without being rendered.</summary>
        Discarded,
        /// <summary>
        /// Validated control rendered, plus a second validated element naming the *same*
        /// field built and dropped afterwards — the shared-slot collision.
        /// </summary>
        DiscardedSameField,
    }

    internal sealed record BareValidateProps(
        BareValidateShape Shape, int Nudge, Action<ValidationContext> OnContext);

    internal sealed class BareValidateOwner : Component<BareValidateProps>
    {
        public override Element Render()
        {
            var props = Props;
            var ctx = this.UseValidationContext();
            props.OnContext(ctx);

            // No FormField anywhere: the verdict exists only because `.Validate()`
            // ran inside this component's render scope. Built inside the branch that
            // uses it — hoisting it would re-publish the verdict on the very passes
            // that are supposed to have stopped producing one.
            //
            // The validated control is the LAST child so that dropping it shortens the
            // collection: the child reconciler then *removes* it, which tears down
            // through the pooling traversal rather than the ordinary unmount.
            return props.Shape switch
            {
                BareValidateShape.Present => VStack(8,
                    TextBlock("bare-head"),
                    TextBox("").Validate("email", "", Validate.Required("Email is required"))),
                BareValidateShape.Hidden => VStack(8, TextBlock("bare-head"), TextBlock("hidden")),
                BareValidateShape.Removed => VStack(8, TextBlock("bare-head")),
                BareValidateShape.Discarded => DiscardedTree(),
                BareValidateShape.DiscardedSameField => DiscardedSameFieldTree(),
                _ => VStack(8,
                    TextBlock("bare-head"),
                    PasswordBox("").Validate("email", "", Validate.Required("Email is required"))),
            };
        }

        /// <summary>
        /// Builds a validated element and then drops it. `.Validate()` has already
        /// written its verdict by the time the element is discarded, so nothing will
        /// ever be mounted to own it.
        /// </summary>
        private static Element DiscardedTree()
        {
            _ = TextBox("").Validate("ghost", "", Validate.Required("ghost is required"));
            return VStack(8, TextBlock("bare-head"));
        }

        /// <summary>
        /// Renders a validated control and then builds and drops a second validated
        /// element naming the same field. Both write the one shared sync slot, and the
        /// dropped element holds the newer stamp — so retiring its unconsumed claim
        /// would clear the slot the mounted control depends on.
        /// </summary>
        private static Element DiscardedSameFieldTree()
        {
            var mounted = TextBox("").Validate("email", "", Validate.Required("Email is required"));
            _ = TextBox("").Validate("email", "", Validate.Required("Email is required"));
            return VStack(8, TextBlock("bare-head"), mounted);
        }
    }

    internal sealed record ChainProps(bool Moved, Action<ValidationContext> OnContext, Action OnRender);

    internal sealed class ChainOwner : Component<ChainProps>
    {
        public override Element Render()
        {
            var props = Props;
            var ctx = this.UseValidationContext();
            props.OnContext(ctx);
            props.OnRender();

            // Both links evaluate eagerly. The second decides which field the
            // attachment ends up naming, so the first link's write has to follow it.
            var el = props.Moved
                ? TextBox("")
                    .Validate("first", "", Validate.Required("first is required"))
                    .Validate("second", "", Validate.Required("second is required"))
                // Same field, different values on each link: a net-zero churn that
                // must not announce a change, or the repaint never settles. The two
                // values disagree on the verdict, so the final messages say which
                // link's value the field settled on.
                : TextBox("")
                    .Validate("first", "", Validate.Required("first is required"))
                    .Validate("first", "bb", Validate.MinLength(3, "first is too short"));

            return VStack(8, el, TextBlock("chain-tail"));
        }
    }

    internal class Issue1262_ValidatedControlUnmountWithdraws(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await BareAsync(BareValidateShape.Hidden, "Hidden", nudge: false);
            await BareAsync(BareValidateShape.Removed, "Removed", nudge: false);
            await BareAsync(BareValidateShape.Hidden, "Skipped", nudge: true);
            await RootHostAsync();
            await DiscardedElementAsync();
            await DiscardedSameFieldAsync();
            await SyncThenAsyncChainAsync();
            await AbortedRootRenderAsync();
            await BareReplacedAsync();
            await WholeFormFieldAsync();
            await ChainMovesFieldAsync();
            await ChainValueChurnSettlesAsync();

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 unmount withdraw done"));
            await Harness.Render();
        }

        // The host's own root render, with no child component in between. Its render
        // frame closes before the reconcile frame opens, so the claim has to survive
        // the gap between them to reach the control that inherits it.
        private async Task RootHostAsync()
        {
            var host = H.CreateHost();
            Action<bool>? setShow = null;
            ValidationContext? ctx = null;

            host.Mount(c =>
            {
                var (show, set) = c.UseState(true);
                setShow = set;
                ctx = c.UseValidationContext();

                return VStack(12,
                    TextBlock("root-head"),
                    show
                        ? TextBox("").Validate("email", "", Validate.Required("Email is required"))
                        : TextBlock("hidden"));
            });

            await Harness.Render();
            H.Check("Issue1262_Unmount_RootResolved", ctx is not null);
            if (ctx is null) return;

            H.Check("Issue1262_Unmount_RootInitialError", ctx.GetMessages("email").Count == 1,
                $"email={ctx.GetMessages("email").Count}");

            setShow!(false);
            await Harness.Render();
            await Harness.Render();
            for (var i = 0; i < 20 && ctx.GetMessages("email").Count > 0; i++)
            {
                await global::System.Threading.Tasks.Task.Delay(25);
                await Harness.Render();
            }

            H.Check("Issue1262_Unmount_RootWithdrawn", ctx.GetMessages("email").Count == 0,
                $"remaining={string.Join("|", ctx.GetMessages("email").Select(m => m.Text))}");
            H.Check("Issue1262_Unmount_RootValid", ctx.IsValid(), $"valid={ctx.IsValid()}");
        }

        // A bare `.Validate()` — no FormField anywhere — that stops being rendered,
        // either replaced by another element or removed from the children outright.
        // The two leave through different teardown paths.
        //
        // `nudge` adds a re-render that changes nothing about the validated element.
        // Its verdict is republished under a fresh claim, but the element is
        // structurally identical, so the update is shallow-skipped — and a control
        // whose binding still names the previous pass's write can no longer withdraw.
        private async Task BareAsync(BareValidateShape gone, string label, bool nudge)
        {
            var host = H.CreateHost();
            Action<BareValidateShape>? setShape = null;
            Action<int>? setNudge = null;
            ValidationContext? ctx = null;

            host.Mount(c =>
            {
                var (shape, set) = c.UseState(BareValidateShape.Present);
                var (nudgeCount, setN) = c.UseState(0);
                setShape = set;
                setNudge = setN;

                return VStack(12,
                    Component<BareValidateOwner, BareValidateProps>(
                        new BareValidateProps(shape, nudgeCount, found => ctx = found)),
                    TextBlock("host"));
            });

            await Harness.Render();
            H.Check($"Issue1262_Unmount_Bare{label}Resolved", ctx is not null);
            if (ctx is null) return;

            H.Check($"Issue1262_Unmount_Bare{label}InitialError", ctx.GetMessages("email").Count == 1,
                $"email={ctx.GetMessages("email").Count}");

            if (nudge)
            {
                setNudge!(1);
                await Harness.Render();
                await Harness.Render();
                H.Check($"Issue1262_Unmount_Bare{label}StillInvalid", ctx.GetMessages("email").Count == 1,
                    $"email={ctx.GetMessages("email").Count}");
            }

            setShape!(gone);
            await Harness.Render();
            await Harness.Render();

            // A removal can be deferred behind an exit transition, so the teardown that
            // withdraws runs on a later turn — and on a timer, not on a render — than
            // the pass that requested it.
            for (var i = 0; i < 20 && ctx.GetMessages("email").Count > 0; i++)
            {
                await global::System.Threading.Tasks.Task.Delay(25);
                await Harness.Render();
            }

            H.Check($"Issue1262_Unmount_Bare{label}Withdrawn", ctx.GetMessages("email").Count == 0,
                $"remaining={string.Join("|", ctx.GetMessages("email").Select(m => m.Text))}");
            H.Check($"Issue1262_Unmount_Bare{label}Valid", ctx.IsValid(), $"valid={ctx.IsValid()}");
        }

        // A validated element that is built and then dropped writes its verdict and
        // leaves nothing behind to own it. The claim it made is the only record that
        // the write happened, so the pass retires whatever it did not hand to a
        // control.
        private async Task DiscardedElementAsync()
        {
            var host = H.CreateHost();
            ValidationContext? ctx = null;

            host.Mount(c => VStack(12,
                Component<BareValidateOwner, BareValidateProps>(
                    new BareValidateProps(BareValidateShape.Discarded, 0, found => ctx = found)),
                TextBlock("host")));

            await Harness.Render();
            await Harness.Render();
            H.Check("Issue1262_Unmount_DiscardedResolved", ctx is not null);
            if (ctx is null) return;

            H.Check("Issue1262_Unmount_DiscardedWithdrawn", ctx.GetMessages("ghost").Count == 0,
                $"remaining={string.Join("|", ctx.GetMessages("ghost").Select(m => m.Text))}");
            H.Check("Issue1262_Unmount_DiscardedValid", ctx.IsValid(), $"valid={ctx.IsValid()}");
        }

        // A chain that ends on an async link. The async validators are attach-only,
        // but the sync verdict the earlier link installed is live — and the surviving
        // attachment is the only thing a mounted control can claim ownership through.
        private async Task SyncThenAsyncChainAsync()
        {
            var host = H.CreateHost();
            Action<bool>? setShow = null;
            ValidationContext? ctx = null;

            host.Mount(c =>
            {
                var (show, set) = c.UseState(true);
                setShow = set;
                ctx = c.UseValidationContext();

                return VStack(12,
                    TextBlock("chain-head"),
                    show
                        ? TextBox("")
                            .Validate("mixed", "", Validate.Required("mixed is required"))
                            .ValidateAsync("mixed", Validate.MustAsync<string>(
                                async s => { await Task.Yield(); return true; }, "taken"))
                        : TextBlock("hidden"));
            });

            await Harness.Render();
            H.Check("Issue1262_SyncAsync_Resolved", ctx is not null);
            if (ctx is null) return;

            H.Check("Issue1262_SyncAsync_InitialError", ctx.GetMessages("mixed").Count == 1,
                $"mixed={string.Join("|", ctx.GetMessages("mixed").Select(m => m.Text))}");

            setShow!(false);
            await Harness.Render();
            await Harness.Render();
            for (var i = 0; i < 20 && ctx.GetMessages("mixed").Count > 0; i++)
            {
                await Task.Delay(25);
                await Harness.Render();
            }

            H.Check("Issue1262_SyncAsync_Withdrawn", ctx.GetMessages("mixed").Count == 0,
                $"remaining={string.Join("|", ctx.GetMessages("mixed").Select(m => m.Text))}");
        }

        // A root render that writes a verdict and then throws never reaches
        // reconciliation, so nothing consumes or retires the claim it made. The next
        // pass has to settle it rather than discard it, or the field stays in error
        // for the lifetime of the context.
        private async Task AbortedRootRenderAsync()
        {
            var host = H.CreateHost();
            ValidationContext? ctx = null;
            var abort = true;

            host.Mount(c =>
            {
                ctx = c.UseValidationContext();
                if (abort)
                {
                    _ = TextBox("").Validate("aborted", "", Validate.Required("aborted is required"));
                    throw new global::System.InvalidOperationException("render aborted on purpose");
                }
                return VStack(8, TextBlock("recovered"));
            });

            var threw = false;
            try { await Harness.Render(); }
            catch (global::System.InvalidOperationException) { threw = true; }

            H.Check("Issue1262_Aborted_Resolved", ctx is not null, $"threw={threw}");
            if (ctx is null) return;

            H.Check("Issue1262_Aborted_VerdictWritten", ctx.GetMessages("aborted").Count == 1,
                $"aborted={ctx.GetMessages("aborted").Count}");

            // The recovered tree no longer renders the field at all.
            abort = false;
            var recovered = H.CreateHost();
            recovered.Mount(c => TextBlock("after abort"));
            await Harness.Render();
            await Harness.Render();

            H.Check("Issue1262_Aborted_ClaimSettled", ctx.GetMessages("aborted").Count == 0,
                $"remaining={string.Join("|", ctx.GetMessages("aborted").Select(m => m.Text))}");
        }

        // Two elements naming one field, one mounted and one dropped. The dropped one
        // wrote last, so its claim holds the current stamp — retiring it unconditionally
        // clears the verdict the mounted control is relying on and reports an invalid
        // field as valid.
        private async Task DiscardedSameFieldAsync()
        {
            var host = H.CreateHost();
            ValidationContext? ctx = null;

            host.Mount(c => VStack(12,
                Component<BareValidateOwner, BareValidateProps>(
                    new BareValidateProps(BareValidateShape.DiscardedSameField, 0, found => ctx = found)),
                TextBlock("host")));

            await Harness.Render();
            await Harness.Render();
            H.Check("Issue1262_SameField_Resolved", ctx is not null);
            if (ctx is null) return;

            H.Check("Issue1262_SameField_VerdictSurvives", ctx.GetMessages("email").Count == 1,
                $"email={ctx.GetMessages("email").Count}");
            H.Check("Issue1262_SameField_ReportsInvalid", !ctx.IsValid(),
                $"valid={ctx.IsValid()}");
        }

        // Guard: the outgoing control must not take the incoming one's verdict with
        // it. Both write the same field under the same producer, and the incoming
        // verdict is installed first.
        private async Task BareReplacedAsync()
        {
            var host = H.CreateHost();
            Action<BareValidateShape>? setShape = null;
            ValidationContext? ctx = null;

            host.Mount(c =>
            {
                var (shape, set) = c.UseState(BareValidateShape.Present);
                setShape = set;

                return VStack(12,
                    Component<BareValidateOwner, BareValidateProps>(
                        new BareValidateProps(shape, 0, found => ctx = found)),
                    TextBlock("host"));
            });

            await Harness.Render();
            if (ctx is null) { H.Check("Issue1262_Unmount_ReplaceContextResolved", false); return; }

            H.Check("Issue1262_Unmount_ReplaceInitialError", ctx.GetMessages("email").Count == 1,
                $"email={ctx.GetMessages("email").Count}");

            setShape!(BareValidateShape.Replaced);
            await Harness.Render();
            await Harness.Render();

            H.Check("Issue1262_Unmount_ReplaceKeepsVerdict", ctx.GetMessages("email").Count == 1,
                $"email={ctx.GetMessages("email").Count}");
        }

        // The whole FormField behind a condition: its own unmount has to withdraw,
        // not just its content's.
        private async Task WholeFormFieldAsync()
        {
            var ctx = new ValidationContext();
            var host = H.CreateHost();
            Action<bool>? setShow = null;

            host.Mount(c =>
            {
                var (show, set) = c.UseState(true);
                setShow = set;

                return VStack(12,
                    show
                        ? FormField(
                            TextBox("").Validate("email", "", Validate.Required("Email is required")),
                            label: "Email",
                            showWhen: ShowWhen.Always)
                        : TextBlock("hidden"))
                    .Provide(ValidationContexts.Current, ctx);
            });

            await Harness.Render();
            H.Check("Issue1262_Unmount_FieldInitialError", ctx.GetMessages("email").Count == 1,
                $"email={ctx.GetMessages("email").Count}");

            setShow!(false);
            await Harness.Render();
            await Harness.Render();

            H.Check("Issue1262_Unmount_FieldWithdrawn", ctx.GetMessages("email").Count == 0,
                $"remaining={string.Join("|", ctx.GetMessages("email").Select(m => m.Text))}");
        }

        // A later link moves the attachment to another field. The attachment keeps
        // only the final name, so the earlier link's verdict would otherwise be owned
        // by a field nothing revisits.
        private async Task ChainMovesFieldAsync()
        {
            var host = H.CreateHost();
            ValidationContext? ctx = null;
            var renders = 0;

            host.Mount(c => VStack(12,
                Component<ChainOwner, ChainProps>(
                    new ChainProps(true, found => ctx = found, () => renders++)),
                TextBlock("host")));

            await Harness.Render();
            if (ctx is null) { H.Check("Issue1262_Chain_ContextResolved", false); return; }

            // Chaining merges validators, so the final link runs both against its own
            // field — the point is that "first" keeps nothing, not that "second" has
            // exactly one message.
            H.Check("Issue1262_Chain_FinalFieldValidated", ctx.GetMessages("second").Count == 2,
                $"second={ctx.GetMessages("second").Count}");
            H.Check("Issue1262_Chain_EarlierLinkWithdrawn", ctx.GetMessages("first").Count == 0,
                $"remaining={string.Join("|", ctx.GetMessages("first").Select(m => m.Text))}");
        }

        // Two links on one field carrying different values churn `_currentValues`
        // every pass and land exactly where they started. The pass is net-zero, so it
        // must not announce a change — announcing one repaints, which churns again.
        private async Task ChainValueChurnSettlesAsync()
        {
            var host = H.CreateHost();
            ValidationContext? ctx = null;
            var renders = 0;

            host.Mount(c => VStack(12,
                Component<ChainOwner, ChainProps>(
                    new ChainProps(false, found => ctx = found, () => renders++)),
                TextBlock("host")));

            await Harness.Render();
            if (ctx is null) { H.Check("Issue1262_Churn_ContextResolved", false); return; }

            var settled = renders;
            for (var i = 0; i < 6; i++) await Harness.Render();

            // A self-sustaining notification loop shows up as renders that keep
            // arriving with no input; a settled pass adds none of its own.
            H.Check("Issue1262_Churn_Settles", renders - settled <= 2,
                $"settled={settled} now={renders}");
            // "bb" is the final link's value: it passes Required and fails MinLength,
            // so exactly one message survives. Had the first link's "" won, Required
            // would have failed too and there would be two.
            var texts = ctx.GetMessages("first").Select(m => m.Text).ToList();
            H.Check("Issue1262_Churn_FinalValueWins",
                texts.Count == 1 && texts[0] == "first is too short",
                $"messages={string.Join("|", texts)}");
        }
    }
    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — validators must not run twice per render.
    //
    //  `.Validate()` evaluates eagerly while the tree is built, and FormField's
    //  reconcile-time pass used to evaluate the same attachment again. The
    //  structural diff hid the duplicate notification but not the work, so a
    //  custom or expensive validator paid twice on every render.
    // ════════════════════════════════════════════════════════════════════════

    internal sealed record CountingValidatorProps(
        Action<ValidationContext> OnContext, Action Bump, Action OnRender);

    internal sealed class CountingValidatorOwner : Component<CountingValidatorProps>
    {
        public override Element Render()
        {
            var props = Props;
            var ctx = this.UseValidationContext();
            props.OnContext(ctx);
            props.OnRender();

            return VStack(8,
                FormField(
                    TextBox("").Validate("email", "",
                        Validate.Must<string>(_ => { props.Bump(); return false; }, "Email is required")),
                    label: "Email",
                    showWhen: ShowWhen.Always));
        }
    }

    internal class Issue1262_ValidatorsRunOncePerRender(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            ValidationContext? ctx = null;
            var runs = 0;
            var ownerRenders = 0;

            host.Mount(c => VStack(12,
                Component<CountingValidatorOwner, CountingValidatorProps>(
                    new CountingValidatorProps(found => ctx = found, () => runs++, () => ownerRenders++)),
                TextBlock("host")));

            await Harness.Render();
            H.Check("Issue1262_RunOnce_ContextResolved", ctx is not null);
            if (ctx is null) return;

            // Positive control: the validator must actually be reached, or "ran once"
            // would be satisfied by never running at all.
            H.Check("Issue1262_RunOnce_ValidatorReached", runs > 0, $"runs={runs}");
            H.Check("Issue1262_RunOnce_VerdictInstalled", ctx.GetMessages("email").Count == 1,
                $"email={ctx.GetMessages("email").Count}");

            // One evaluation per render of the owning component, whatever that count
            // happens to be. Asserting a bare `runs == 1` would fail for a second render
            // that legitimately re-validates, and would pass for a double evaluation
            // inside a single render if only one render occurred — neither is the
            // property under test.
            H.Check("Issue1262_RunOnce_NotDoubled", runs == ownerRenders,
                $"runs={runs} ownerRenders={ownerRenders}");

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 run-once done"));
            await Harness.Render();
        }
    }
    // ════════════════════════════════════════════════════════════════════════
    //  Issue #1262 review — whose cancellation was it?
    //
    //  A mounted async rule is cancelled on update and unmount, and that is
    //  routine. But the predicate takes no token of its own, so anything IT
    //  cancels is the app's business — treating that as lifecycle churn hides a
    //  real fault and silently leaves the stale verdict in place. The two are
    //  told apart by the token, not by the exception type.
    // ════════════════════════════════════════════════════════════════════════

    internal class Issue1262_PredicateCancellationIsReported(Harness h) : SelfTestFixtureBase(h)
    {
        private static IDisposable SubscribeToRuleErrors(List<string> sink)
            => Microsoft.UI.Reactor.Diagnostics.ReactorTrace.Subscribe(
                e =>
                {
                    if (e.EventName != nameof(Core.Diagnostics.ReactorEventSource.SwallowedError)) return;
                    if (e.Payload.Count < 3) return;
                    if (e.Payload[1] as string != "ValidationRuleAsync.Evaluate") return;
                    lock (sink) sink.Add(e.Payload[2] as string ?? "<unknown>");
                },
                global::System.Diagnostics.Tracing.EventLevel.Warning,
                Core.Diagnostics.ReactorEventSource.Keywords.Errors);

        public override async Task RunAsync()
        {
            var reported = new List<string>();
            using var sub = SubscribeToRuleErrors(reported);

            // The predicate cancels itself, with a token that is not the rule's.
            var ctx = new ValidationContext();
            var host = H.CreateHost();
            host.Mount(c => VStack(12,
                ValidationRuleAsync(
                    async () =>
                    {
                        await Task.Yield();
                        using var foreign = new global::System.Threading.CancellationTokenSource();
                        foreign.Cancel();
                        foreign.Token.ThrowIfCancellationRequested();
                        return true;
                    },
                    "never reached", "form"),
                TextBlock("rule host"))
                .Provide(ValidationContexts.Current, ctx));

            for (var i = 0; i < 10; i++)
            {
                await Task.Delay(25);
                await Harness.Render();
                lock (reported) { if (reported.Count > 0) break; }
            }

            int seen; string names;
            lock (reported) { seen = reported.Count; names = string.Join("|", reported); }

            // EventListener callbacks for managed EventSource events do not flow under
            // NativeAOT publish — IsEnabled() returns false on the emit side, so the
            // listener observes nothing regardless of what the classification did. The
            // same guard NativeDockingReliabilityFixture uses, and for the same reason:
            // asserting here would fail the AOT run for a runtime limitation rather than
            // a defect. The JIT selftest run covers it.
            if (global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            {
                H.Check("Issue1262_Cancel_ForeignCancellationReported", seen > 0,
                    $"reported={seen} names={names}");
            }

            // Positive control: the same subscription, same operation name, must stay
            // silent for the lifecycle cancellation it is supposed to ignore. A sink
            // that reports everything would satisfy the check above for the wrong
            // reason.
            var quiet = new List<string>();
            using var sub2 = SubscribeToRuleErrors(quiet);

            var ctx2 = new ValidationContext();
            var host2 = H.CreateHost();
            Action<bool>? setShow = null;
            host2.Mount(c =>
            {
                var (show, set) = c.UseState(true);
                setShow = set;
                return VStack(12,
                    When(show, () => ValidationRuleAsync(
                        async () => { await Task.Delay(5000); return true; },
                        "slow", "form")),
                    TextBlock("cancel host"))
                    .Provide(ValidationContexts.Current, ctx2);
            });

            await Harness.Render();
            setShow!(false);          // unmount cancels the in-flight pass
            await Harness.Render();
            for (var i = 0; i < 6; i++) { await Task.Delay(25); await Harness.Render(); }

            int quietCount; string quietNames;
            lock (quiet) { quietCount = quiet.Count; quietNames = string.Join("|", quiet); }
            // Guarded for the same reason as the check above, and additionally because a
            // listener that can never observe anything satisfies "stayed silent"
            // vacuously — the control would stop controlling for anything.
            if (global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
            {
                H.Check("Issue1262_Cancel_LifecycleCancellationSilent", quietCount == 0,
                    $"reported={quietCount} names={quietNames}");
            }

            var done = H.CreateHost();
            done.Mount(c => TextBlock("Issue1262 cancellation done"));
            await Harness.Render();
        }
    }
}