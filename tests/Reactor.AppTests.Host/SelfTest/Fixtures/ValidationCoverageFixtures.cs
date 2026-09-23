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

            // Async rule — passes
            var asyncRule2 = ValidationRuleAsync(
                async () => { await Task.Delay(1); return true; },
                "Async pass", "field2");
            await asyncRule2.EvaluateAsync(ctx);
            H.Check("ValRule_AsyncPass", !ctx.HasError("field2"));

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
            H.Check("Issue1262_FieldsRegistered",
                captured!.RegisteredFields.Contains("email") && captured.RegisteredFields.Contains("password"));

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
            H.Check("Issue1262_Blur_NotTouchedInitially", !captured!.IsTouched("name"));
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
        private static ValidationContext? s_captured;
        private static int s_renders;

        // Must be a child Component, not the host's root render func: a child's
        // re-render callback runs INLINE (CreateComponentRerender), which is what turns
        // a notification raised during reconcile into unbounded re-entrancy. The root's
        // callback merely schedules, so mounting this at the root would hide the bug.
        private sealed class RuleOwner : Component
        {
            public override Element Render()
            {
                var valCtx = this.UseValidationContext();
                s_captured = valCtx;
                s_renders++;

                return VStack(12,
                    ValidationRule(() => false, "Passwords must match", "confirm"),
                    TextBlock($"valid:{valCtx.IsValid()}"));
            }
        }

        public override async Task RunAsync()
        {
            s_captured = null;
            s_renders = 0;

            var host = H.CreateHost();
            host.Mount(_ => Component<RuleOwner>());

            // If the loop were still present this throws "Render loop detected".
            await Harness.Render();
            await Harness.Render();

            H.Check("Issue1262_Rule_NoLoopThrown", s_captured is not null);
            H.Check("Issue1262_Rule_MessageRecorded",
                s_captured!.GetMessages("confirm").Count == 1);
            H.Check("Issue1262_Rule_NoAccumulation",
                s_captured.GetAllMessages().Count == 1);
            H.Check("Issue1262_Rule_RenderCountBounded", s_renders < 10, $"renders={s_renders}");

            var settledVersion = s_captured.Version;
            var settledRenders = s_renders;
            await Harness.Render();

            H.Check("Issue1262_Rule_VersionStableOnReRender", s_captured.Version == settledVersion,
                $"before={settledVersion} after={s_captured.Version}");
            H.Check("Issue1262_Rule_RendersSettle", s_renders - settledRenders <= 2,
                $"delta={s_renders - settledRenders}");

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

            // While the context is reachable, blur marks the field.
            box!.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();
            elsewhere!.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            await Harness.Render();
            H.Check("Issue1262_Binding_MarksWhileBound", ctx.IsTouched("name"));

            // Drop the provider. The same control is patched in place, so the binding
            // has to be cleared rather than left pointing at the old context.
            var stillSameControl = ReferenceEquals(box, H.FindControl<TextBox>(_ => true));
            setProvide!(false);
            await Harness.Render();
            H.Check("Issue1262_Binding_ControlPreserved",
                stillSameControl && ReferenceEquals(box, H.FindControl<TextBox>(_ => true)));

            var freshCtx = new ValidationContext();
            freshCtx.RegisterField("name");
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
}
