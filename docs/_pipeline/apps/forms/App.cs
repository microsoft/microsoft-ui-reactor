using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Reactor.Controls.Validation;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<FormsApp>("Forms", width: 600, height: 900
#if DEBUG
    , preview: true
#endif
);

// <snippet:controlled-input>
class ControlledInputDemo : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");

        return VStack(12,
            SubHeading("Controlled Input"),
            TextField(name, setName, placeholder: "Type your name"),
            TextBlock($"You typed: {name}").Opacity(0.6)
        ).Padding(24);
    }
}
// </snippet:controlled-input>

// <snippet:input-types>
class InputTypesDemo : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("");
        var (password, setPassword) = UseState("");
        var (volume, setVolume) = UseState(50.0);
        var (count, setCount) = UseState(1.0);
        var (agree, setAgree) = UseState(false);
        var (notify, setNotify) = UseState(true);
        var (role, setRole) = UseState(0);
        var (priority, setPriority) = UseState(0);

        return VStack(12,
            TextField(text, setText, placeholder: "Email",
                header: "Email"),
            PasswordBox(password, setPassword,
                placeholderText: "Enter password"),
            Slider(volume, 0, 100, setVolume),
            NumberBox(count, setCount, header: "Quantity"),
            CheckBox(agree, setAgree, label: "I agree to the terms"),
            ToggleSwitch(notify, setNotify,
                header: "Notifications"),
            ComboBox(["Admin", "Editor", "Viewer"],
                role, setRole),
            RadioButtons(["Low", "Medium", "High"],
                priority, setPriority)
        ).Padding(24);
    }
}
// </snippet:input-types>

// <snippet:validation>
class ValidationDemo : Component
{
    public override Element Render()
    {
        var (email, setEmail) = UseState("");
        var (age, setAge) = UseState(0.0);
        var (showErrors, setShowErrors) = UseState(false);

        var emailValid = email.Contains('@') && email.Contains('.');
        var ageValid = age >= 18 && age <= 120;
        var formValid = emailValid && ageValid
            && !string.IsNullOrWhiteSpace(email);

        return VStack(12,
            SubHeading("Simple Validation"),
            TextField(email, setEmail, placeholder: "user@example.com",
                header: "Email"),
            When(!string.IsNullOrEmpty(email) && !emailValid, () =>
                TextBlock("Enter a valid email address")
                    .Foreground(Theme.SystemCritical).FontSize(12)),
            NumberBox(age, setAge, header: "Age"),
            When((showErrors || age > 0) && !ageValid, () =>
                TextBlock("Age must be between 18 and 120")
                    .Foreground(Theme.SystemCritical).FontSize(12)),
            Button("Submit", () =>
            {
                setShowErrors(true);
                if (!formValid) return;
                // submit...
            }).Margin(0, 8, 0, 0)
        ).Padding(24);
    }
}
// </snippet:validation>

// <snippet:keep-submit-reachable>
class KeepSubmitReachableDemo : Component
{
    public override Element Render()
    {
        var (email, setEmail) = UseState("");
        var (age, setAge) = UseState(0.0);

        var emailValid = email.Contains('@') && email.Contains('.');
        var ageValid = age >= 18 && age <= 120;
        var formValid = emailValid && ageValid;

        return VStack(12,
            SubHeading("Keeping Submit Reachable"),
            TextField(email, setEmail, header: "Email",
                placeholder: "user@example.com"),

            // .Immediate() switches NumberBox from commit-on-blur to
            // commit-on-keystroke, so validation reacts as the user types.
            NumberBox(age, setAge, header: "Age").Immediate(),

            // .DisabledFocusable() keeps the button tab-reachable and
            // visually dimmed while preventing invocation. Pattern mirrors
            // Fluent UI's `disabledFocusable` and ARIA `aria-disabled`.
            Button("Submit", () => { /* submit */ })
                .DisabledFocusable(!formValid)
                .Margin(0, 8, 0, 0)
        ).Padding(24);
    }
}
// </snippet:keep-submit-reachable>

// <snippet:validation-context>
class ValidationContextDemo : Component
{
    public override Element Render()
    {
        var ctx = this.UseValidationContext();
        var (email, setEmail) = UseState("");
        var (password, setPassword) = UseState("");
        var (submitted, setSubmitted) = UseState(false);

        return VStack(12,
            SubHeading("Validation Context"),
            TextField(email, v => { setEmail(v); ctx.NotifyValueChanged("email", v); },
                placeholder: "user@example.com", header: "Email")
                .Validate("email", email,
                    Validate.Required(),
                    Validate.Email()),
            When(ctx.IsTouched("email") && ctx.HasError("email"), () =>
                TextBlock(ctx.GetMessages("email").First().Text)
                    .Foreground(Theme.SystemCritical).FontSize(12)),
            PasswordBox(password, v => { setPassword(v); ctx.NotifyValueChanged("password", v); },
                placeholderText: "Min 8 characters")
                .Validate("password", password,
                    Validate.Required(),
                    Validate.MinLength(8)),
            When(ctx.IsTouched("password") && ctx.HasError("password"), () =>
                TextBlock(ctx.GetMessages("password").First().Text)
                    .Foreground(Theme.SystemCritical).FontSize(12)),
            Button("Register", () =>
            {
                ctx.MarkAllTouched();
                if (ctx.IsValid()) setSubmitted(true);
            }).Disabled(submitted),
            When(submitted, () =>
                TextBlock("Registration successful!")
                    .Foreground(Theme.SystemSuccess).SemiBold())
        ).Padding(24);
    }
}
// </snippet:validation-context>

// <snippet:form-field>
class FormFieldDemo : Component
{
    public override Element Render()
    {
        var ctx = this.UseValidationContext();
        var (name, setName) = UseState("");
        var (email, setEmail) = UseState("");

        return VStack(12,
            SubHeading("FormField Helper"),
            FormField(
                TextField(name, v => { setName(v); ctx.NotifyValueChanged("name", v); })
                    .Validate("name", name, Validate.Required()),
                label: "Full Name",
                required: true,
                description: "As it appears on your ID"),
            FormField(
                TextField(email, v => { setEmail(v); ctx.NotifyValueChanged("email", v); })
                    .Validate("email", email,
                        Validate.Required(), Validate.Email()),
                label: "Email Address",
                required: true)
        ).Padding(24);
    }
}
// </snippet:form-field>

// <snippet:masked-input>
class MaskedInputDemo : Component
{
    public override Element Render()
    {
        var phoneMask = UseMemo(() => new MaskEngine(MaskPreset.PhoneUS));
        var dateMask = UseMemo(() => new MaskEngine(MaskPreset.Date));
        var (phone, setPhone) = UseState("");
        var (date, setDate) = UseState("");

        return VStack(12,
            SubHeading("Masked Input"),
            TextField(phoneMask.Apply(phone), v => setPhone(phoneMask.GetRawValue(v)),
                placeholder: "(___) ___-____", header: "Phone"),
            TextBlock($"Raw: {phone}").FontSize(12).Opacity(0.6),
            TextField(dateMask.Apply(date), v => setDate(dateMask.GetRawValue(v)),
                placeholder: "__/__/____", header: "Date"),
            TextBlock($"Raw: {date}").FontSize(12).Opacity(0.6)
        ).Padding(24);
    }
}
// </snippet:masked-input>

// <snippet:input-formatters>
class InputFormattersDemo : Component
{
    public override Element Render()
    {
        var (currency, setCurrency) = UseState("");
        var (upper, setUpper) = UseState("");

        var currencyFmt = UseMemo(() => InputFormatter.Currency());
        var upperFmt = UseMemo(() => InputFormatter.UpperCase);

        return VStack(12,
            SubHeading("Input Formatters"),
            TextField(currencyFmt.Format(currency, 0).Output,
                v => setCurrency(currencyFmt.Parse(v)),
                placeholder: "$0.00", header: "Amount"),
            TextField(upperFmt.Format(upper, 0).Output,
                v => setUpper(upperFmt.Parse(v)),
                placeholder: "UPPERCASE", header: "Code")
        ).Padding(24);
    }
}
// </snippet:input-formatters>

class FormsApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Forms and Input"),
                Component<ControlledInputDemo>(),
                Component<InputTypesDemo>(),
                Component<ValidationDemo>(),
                Component<KeepSubmitReachableDemo>(),
                Component<ValidationContextDemo>(),
                Component<FormFieldDemo>(),
                Component<MaskedInputDemo>(),
                Component<InputFormattersDemo>()
            ).Padding(24)
        );
    }
}
