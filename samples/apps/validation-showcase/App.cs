// Validation Showcase — Demonstrates the Reactor forms & validation system.
// Shows: ValidationContext, built-in validators, error styling,
// MaskedTextBox, InputFormatters, FocusManager, cross-field rules,
// window-level InfoBar validation summary.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static Microsoft.UI.Reactor.Controls.Validation.ValidationRuleDsl;
using static Microsoft.UI.Reactor.Controls.Validation.ValidationVisualizerDsl;
using Microsoft.UI.Reactor.Animation;

ReactorApp.Run<ShowcaseApp>("Validation Showcase", width: 720, height: 900
#if DEBUG
    , devtools: true
#endif
);

// ═══════════════════════════════════════════════════════════════════════
//  1. Basic inline validation with window-level InfoBar
// ═══════════════════════════════════════════════════════════════════════

class BasicValidationDemo : Component
{
    public override Element Render()
    {
        var ctx = this.UseValidationContext();
        var fm = this.UseFocus();
        var (submitted, setSubmitted) = UseState(false);
        var (infoDismissed, setInfoDismissed) = UseState(false);

        var (email, setEmail) = UseState("");
        var (age, setAge) = UseState(0.0);
        var (website, setWebsite) = UseState("");

        // No manual ValidateField calls — FormField auto-validates via .Validate() with value

        var sw = submitted ? ShowWhen.Always : ShowWhen.WhenTouched;
        var errorCount = ctx.InvalidFields.Count;
        var showInfoBar = submitted && !ctx.IsValid() && !infoDismissed;

        // Reset dismissed state when errors change
        if (submitted && ctx.IsValid())
            infoDismissed = false;

        return VStack(0,
            // Window-level InfoBars — always in tree, controlled via IsOpen.
            // Using IsOpen instead of When() keeps child positions stable so
            // the form VStack below is updated in-place (preserving caret/focus).
            (InfoBar($"Please fix {errorCount} error{(errorCount == 1 ? "" : "s")} before submitting",
                     string.Join(", ", ctx.InvalidFields.Select(f => f)))
                with
                {
                    IsOpen = showInfoBar,
                    IsClosable = true,
                    OnClosed = () => setInfoDismissed(true),
                })
                .Severity(InfoBarSeverity.Error)
                .Margin(0, 0, 0, showInfoBar ? 8 : 0),

            (InfoBar("Success", "Form submitted successfully!") with
                {
                    IsOpen = submitted && ctx.IsValid(),
                    IsClosable = true,
                })
                .Severity(InfoBarSeverity.Success)
                .Margin(0, 0, 0, submitted && ctx.IsValid() ? 8 : 0),

            VStack(12,
                // FormField auto-renders label, content, and description/error area.
                // .Validate() with value triggers automatic validation — no manual calls.
                FormField(
                    TextBox(email, v => { setEmail(v); ctx.MarkTouched("email"); setInfoDismissed(false); },
                            placeholderText: "user@example.com")
                        .Validate("email", email,
                            Validate.Required("Email is required"),
                            Validate.Email("Enter a valid email"))
                        .Focus(fm, "email", autoFocus: true),
                    label: "Email", required: true,
                    description: "We'll never share your email",
                    showWhen: sw),

                FormField(
                    NumberBox(age, v => { setAge(v); ctx.MarkTouched("age"); setInfoDismissed(false); })
                        .Validate("age", age,
                            Validate.Range(18, 120, "Age must be between 18 and 120"))
                        .Immediate()  // validate per keystroke, not on blur
                        .Focus(fm, "age"),
                    label: "Age", required: true,
                    description: "Must be 18 or older",
                    showWhen: sw),

                FormField(
                    TextBox(website, v => { setWebsite(v); ctx.MarkTouched("website"); setInfoDismissed(false); },
                            placeholderText: "https://example.com")
                        .Validate("website", website,
                            Validate.Url("Enter a valid URL (https://...)"))
                        .Focus(fm, "website"),
                    label: "Website",
                    description: "Optional — your personal site",
                    showWhen: sw),

                HStack(12,
                    Button("Submit", () =>
                    {
                        setSubmitted(true);
                        setInfoDismissed(false);
                        ctx.MarkAllTouched();
                        if (!ctx.IsValid())
                            fm.FocusFirst(ctx.InvalidFields);
                    }),
                    Button("Reset", () =>
                    {
                        setSubmitted(false);
                        setInfoDismissed(false);
                        setEmail("");
                        setAge(0.0);
                        setWebsite("");
                        ctx.ClearAll();
                    })
                ).Margin(0, 8, 0, 0)
            )
        ).Padding(24)
         .Provide(ValidationContexts.Current, ctx);
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  2. Password form with cross-field validation
// ═══════════════════════════════════════════════════════════════════════

class PasswordFormDemo : Component
{
    public override Element Render()
    {
        var ctx = this.UseValidationContext();
        var (submitted, setSubmitted) = UseState(false);

        var (password, setPassword) = UseState("");
        var (confirm, setConfirm) = UseState("");
        var (agree, setAgree) = UseState(false);

        // No manual ValidateField or .Evaluate() calls needed

        var sw = submitted ? ShowWhen.Always : ShowWhen.WhenTouched;

        return VStack(12,
            SubHeading("Cross-Field Validation"),
            Caption("Password confirmation uses a cross-field ValidationRule."),

            FormField(
                PasswordBox(password, v => { setPassword(v); ctx.MarkTouched("password"); })
                    .Validate("password", password,
                        Validate.Required("Password is required"),
                        Validate.MinLength(8, "At least 8 characters"),
                        Validate.Must<string>(
                            s => s.Any(char.IsUpper) && s.Any(char.IsDigit),
                            "Must contain uppercase letter and digit")),
                label: "Password", required: true,
                description: "8+ chars, uppercase + digit",
                showWhen: sw),

            FormField(
                PasswordBox(confirm, v => { setConfirm(v); ctx.MarkTouched("confirm"); })
                    .Validate("confirm", confirm,
                        Validate.Required("Please confirm your password")),
                label: "Confirm Password", required: true,
                showWhen: sw),

            // Cross-field rule: placed in tree, evaluated automatically by reconciler
            ValidationRule(
                () => string.IsNullOrEmpty(confirm) || password == confirm,
                "Passwords do not match",
                "confirm"),

            FormField(
                CheckBox(agree, v => { setAgree(v); ctx.MarkTouched("agree"); },
                    label: "I accept the terms of service")
                    .Validate("agree", agree,
                        Validate.MustBeTrue("You must accept the terms")),
                fieldName: "agree",
                showWhen: sw),

            HStack(12,
                Button("Set Password", () =>
                {
                    setSubmitted(true);
                    ctx.MarkAllTouched();
                }),
                When(submitted && ctx.IsValid(), () =>
                    TextBlock("Password set!").Foreground(Theme.SystemSuccess)
                        .VAlign(VerticalAlignment.Center))
            ).Margin(0, 8, 0, 0)
        ).Padding(24)
         .Provide(ValidationContexts.Current, ctx);
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  3. Masked input & formatters
// ═══════════════════════════════════════════════════════════════════════

class MaskedInputDemo : Component
{
    public override Element Render()
    {
        var (phone, setPhone) = UseState("");
        var (ssn, setSsn) = UseState("");
        var (zip, setZip) = UseState("");
        var (amount, setAmount) = UseState("");
        var (shout, setShout) = UseState("");

        var phoneMask = new MaskEngine(MaskPreset.PhoneUS);
        var ssnMask = new MaskEngine(MaskPreset.SSN);
        var zipMask = new MaskEngine(MaskPreset.ZipCode);

        var phoneFormatted = phoneMask.Apply(phone);
        var ssnFormatted = ssnMask.Apply(ssn);
        var zipFormatted = zipMask.Apply(zip);

        var currencyFmt = InputFormatter.Currency("$");
        var currencyResult = amount.Length > 0
            ? currencyFmt.Format(amount, amount.Length) : new FormatResult("", 0);

        return VStack(12,
            SubHeading("Masked Input & Formatters"),
            Caption("Input masks enforce structured formats. Formatters transform text live."),

            VStack(8,
                TextBlock("Phone (US) — digits only").Foreground(Theme.SecondaryText),
                TextBox(phone, v => setPhone(new string(v.Where(char.IsDigit).ToArray())),
                    placeholderText: "5551234567"),
                When(phone.Length > 0, () =>
                    HStack(8,
                        TextBlock($"Formatted: {phoneFormatted}").Foreground(Theme.SecondaryText),
                        TextBlock($"Raw: {phoneMask.GetRawValue(phoneFormatted)}").Foreground(Theme.SecondaryText),
                        TextBlock(phoneMask.IsComplete(phoneFormatted) ? "Complete" : "Incomplete")
                            .Foreground(phoneMask.IsComplete(phoneFormatted) ? Theme.SystemSuccess : Theme.SystemCritical)
                    ))
            ),

            VStack(8,
                TextBlock("SSN — digits only").Foreground(Theme.SecondaryText),
                TextBox(ssn, v => setSsn(new string(v.Where(char.IsDigit).ToArray())),
                    placeholderText: "123456789"),
                When(ssn.Length > 0, () =>
                    TextBlock($"Formatted: {ssnFormatted}").Foreground(Theme.SecondaryText))
            ),

            VStack(8,
                TextBlock("ZIP Code — digits only").Foreground(Theme.SecondaryText),
                TextBox(zip, v => setZip(new string(v.Where(char.IsDigit).ToArray())),
                    placeholderText: "90210"),
                When(zip.Length > 0, () =>
                    TextBlock($"Formatted: {zipFormatted}").Foreground(Theme.SecondaryText))
            ),

            VStack(8,
                TextBlock("Currency (InputFormatter)").Foreground(Theme.SecondaryText),
                TextBox(amount,
                    v => setAmount(new string(v.Where(c => char.IsDigit(c) || c == '.').ToArray())),
                    placeholderText: "1234.56"),
                When(amount.Length > 0, () =>
                    TextBlock($"Display: {currencyResult.Output}  |  Raw: {currencyFmt.Parse(currencyResult.Output)}")
                        .Foreground(Theme.SecondaryText))
            ),

            VStack(8,
                TextBlock("Uppercase Formatter").Foreground(Theme.SecondaryText),
                TextBox(shout, v => setShout(v.ToUpperInvariant()),
                    placeholderText: "auto uppercased")
                    .Set(tb => tb.CharacterCasing = CharacterCasing.Upper)
            )
        ).Padding(24);
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  4. Dirty / Touched / Reset demo
// ═══════════════════════════════════════════════════════════════════════

class DirtyResetDemo : Component
{
    public override Element Render()
    {
        var ctx = this.UseValidationContext();
        // tick forces re-render when we mutate ctx outside of state setters
        var (tick, setTick) = UseState(0);

        var (name, setName) = UseState("John Doe");
        var (color, setColor) = UseState(0);
        var colors = new[] { "Red", "Green", "Blue" };

        ctx.RegisterField("name");
        ctx.SetInitialValue("name", "John Doe");
        ctx.NotifyValueChanged("name", name);

        ctx.RegisterField("color");
        ctx.SetInitialValue("color", 0);
        ctx.NotifyValueChanged("color", color);

        return VStack(12,
            SubHeading("Dirty & Touched Tracking"),
            Caption("Tracks whether fields changed from initial values or were interacted with."),

            VStack(4,
                TextBlock("Name")
                    .Set(t => t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                TextBox(name, v => { setName(v); ctx.MarkTouched("name"); }),
                TextBlock("Initial: \"John Doe\"").Foreground(Theme.SecondaryText)
            ),

            VStack(4,
                TextBlock("Favorite Color")
                    .Set(t => t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                ComboBox(colors, color, v => { setColor(v); ctx.MarkTouched("color"); }),
                TextBlock("Initial: Red").Foreground(Theme.SecondaryText)
            ),

            VStack(4,
                TextBlock($"name — touched: {ctx.IsTouched("name")}, dirty: {ctx.IsDirty("name")}")
                    .Foreground(Theme.SecondaryText),
                TextBlock($"color — touched: {ctx.IsTouched("color")}, dirty: {ctx.IsDirty("color")}")
                    .Foreground(Theme.SecondaryText),
                TextBlock($"Form dirty: {ctx.IsDirty()}").Foreground(Theme.SecondaryText)
            ),

            HStack(12,
                Button("Reset All", () =>
                {
                    var restored = ctx.ResetAll();
                    if (restored.TryGetValue("name", out var n)) setName((string)(n ?? ""));
                    if (restored.TryGetValue("color", out var c)) setColor((int)(c ?? 0));
                    setTick(tick + 1);
                }).IsEnabled(ctx.IsDirty()),
                Button("Mark All Touched", () =>
                {
                    ctx.MarkAllTouched();
                    setTick(tick + 1);
                })
            ).Margin(0, 8, 0, 0)
        ).Padding(24);
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  5. Focus management demo
// ═══════════════════════════════════════════════════════════════════════

class FocusDemo : Component
{
    public override Element Render()
    {
        var fm = this.UseFocus();
        var (log, setLog) = UseState("");

        fm.SetSubmitHandler(() => setLog("Form submitted via Enter on last field!"));

        var (f1, setF1) = UseState("");
        var (f2, setF2) = UseState("");
        var (f3, setF3) = UseState("");

        return VStack(12,
            SubHeading("Focus Management"),
            Caption("UseFocus provides programmatic focus control and enter-to-advance."),

            TextBox(f1, setF1, placeholderText: "First field", header: "First Name")
                .Focus(fm, "first"),
            TextBox(f2, setF2, placeholderText: "Second field", header: "Last Name")
                .Focus(fm, "last"),
            TextBox(f3, setF3, placeholderText: "Third (last) field", header: "Nickname")
                .Focus(fm, "nick"),

            HStack(8,
                Button("Focus First", () => fm.FocusField("first")),
                Button("Focus Next", () => fm.FocusNext("first")),
                Button("Focus Last", () => fm.FocusField("nick"))
            ),

            When(log.Length > 0, () =>
                TextBlock(log).Foreground(Theme.SecondaryText).Margin(0, 4, 0, 0))
        ).Padding(24);
    }
}

// ═══════════════════════════════════════════════════════════════════════
//  Root app
// ═══════════════════════════════════════════════════════════════════════

class ShowcaseApp : Component
{
    public override Element Render()
    {
        return Grid(
            [GridSize.Star()], [GridSize.Auto, GridSize.Star()],

            TitleBar("Validation Showcase")
                .Subtitle("Reactor Forms & Data Entry")
                .Grid(row: 0),

            ScrollView(
                VStack(0,
                    Component<BasicValidationDemo>(),

                    Separator(),

                    Component<PasswordFormDemo>(),

                    Separator(),

                    Component<MaskedInputDemo>(),

                    Separator(),

                    Component<DirtyResetDemo>(),

                    Separator(),

                    Component<FocusDemo>(),

                    Empty().Height(24)
                )
            ).Grid(row: 1)
        )
        // Spec 033 §6 — Mica window backdrop.
        .Backdrop(BackdropKind.Mica);
    }

    static Element Separator() =>
        Border(Empty().Height(1))
            .Set(b => b.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.Gray))
            .Opacity(0.15)
            .Margin(24, 8, 24, 8);
}
