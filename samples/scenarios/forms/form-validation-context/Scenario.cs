// id: form-validation-context
// intent: form validation with UseValidationContext
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Controls.Validation;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Form validation context", width: 500, height: 400);

class App : Component
{
    public override Element Render()
    {
        var ctx = this.UseValidationContext();
        var (name, setName) = UseState("");
        var (email, setEmail) = UseState("");
        var (password, setPassword) = UseState("");
        var (submitted, setSubmitted) = UseState(false);
        var showWhen = submitted ? ShowWhen.Always : ShowWhen.WhenTouched;

        return VStack(12,
            Heading("Create account"),
            FormField(TextField(name, v => { setName(v); ctx.MarkTouched("name"); }, placeholder: "Full name")
                    .Validate("name", name, Validate.Required("Name is required"), Validate.MinLength(2, "Use at least 2 characters")),
                label: "Name", required: true, showWhen: showWhen),
            FormField(TextField(email, v => { setEmail(v); ctx.MarkTouched("email"); }, placeholder: "you@example.com")
                    .Validate("email", email, Validate.Required("Email is required"), Validate.Email("Enter a valid email")),
                label: "Email", required: true, showWhen: showWhen),
            FormField(TextField(password, v => { setPassword(v); ctx.MarkTouched("password"); }, placeholder: "Minimum 8 characters")
                    .Validate("password", password, Validate.Required("Password is required"), Validate.MinLength(8, "Use at least 8 characters")),
                label: "Password", required: true, showWhen: showWhen),
            Button("Validate", () => { setSubmitted(true); ctx.MarkAllTouched(); }).AccentButton(),
            TextBlock(ctx.IsValid() ? "All fields pass validation." : "Fix the errors above."))
            .Padding(24);
    }
}
