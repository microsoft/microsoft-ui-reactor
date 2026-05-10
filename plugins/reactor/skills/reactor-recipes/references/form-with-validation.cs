// Recipe: form with validation, error display, and submit gating.
//
// Pattern: UseValidationContext owns per-field state. Inputs attach validators
// with `.Validate(fieldName, currentValue, ...)` — the value is required for
// auto-validation. FormField wraps each input with label + required marker +
// error display. The validation context is resolved through React-style
// context — no need to thread it explicitly to .Validate().

// In this clone, run `mur pack-local` once. Bump the version below to match
// whatever `mur pack-local` printed (default: 0.0.0-local). For a real NuGet
// consumer, set Version to a published Microsoft.UI.Reactor release.
#:package Microsoft.UI.Reactor@0.0.0-local
#:package Microsoft.WindowsAppSDK@2.0.1
#:property OutputType=WinExe
#:property TargetFramework=net10.0-windows10.0.22621.0
#:property UseWinUI=true
#:property WindowsPackageType=None

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;  // InfoBarSeverity
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;

ReactorApp.Run<App>("Form demo", width: 520, height: 540);

class App : Component
{
    public override Element Render()
    {
        var ctx = this.UseValidationContext();
        var (name,    setName)    = UseState("");
        var (email,   setEmail)   = UseState("");
        var (password, setPwd)    = UseState("");
        var (confirm, setConfirm) = UseState("");
        var (submitted, setSubmitted) = UseState(false);

        var sw = submitted ? ShowWhen.Always : ShowWhen.WhenTouched;

        return VStack(12,
            Heading("Sign up"),

            FormField(
                TextField(name, v => { setName(v); ctx.MarkTouched("name"); }, placeholder: "Your name")
                    .Validate("name", name,
                        Validate.Required("Name is required"),
                        Validate.MinLength(2, "At least 2 characters")),
                label: "Name", required: true, showWhen: sw),

            FormField(
                TextField(email, v => { setEmail(v); ctx.MarkTouched("email"); }, placeholder: "you@example.com")
                    .Validate("email", email,
                        Validate.Required("Email is required"),
                        Validate.Email("Not a valid email")),
                label: "Email", required: true, showWhen: sw),

            FormField(
                PasswordBox(password, v => { setPwd(v); ctx.MarkTouched("password"); })
                    .Validate("password", password,
                        Validate.Required("Password required"),
                        Validate.MinLength(8, "At least 8 characters")),
                label: "Password", required: true, showWhen: sw),

            FormField(
                PasswordBox(confirm, v => { setConfirm(v); ctx.MarkTouched("confirm"); })
                    .Validate("confirm", confirm,
                        Validate.Must<string>(c => c == password, "Passwords don't match")),
                label: "Confirm password", required: true, showWhen: sw),

            Button("Create account", () =>
            {
                setSubmitted(true);
                ctx.MarkAllTouched();
            }),

            submitted && ctx.IsValid()
                ? InfoBar("Created!", $"Welcome, {name}.").Severity(InfoBarSeverity.Success)
                : null
        ).Padding(24);
    }
}
