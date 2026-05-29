// id: form-submit-gating
// intent: disable submit button until form is valid
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Controls.Validation;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Form submit gating", width: 500, height: 400);

class App : Component
{
    public override Element Render()
    {
        var ctx = this.UseValidationContext();
        var (email, setEmail) = UseState("");
        var (accessCode, setAccessCode) = UseState("");
        var (status, setStatus) = UseState("Waiting for valid input.");

        return VStack(12,
            Heading("Submit gating"),
            FormField(TextField(email, v => { setEmail(v); ctx.MarkTouched("email"); }, placeholder: "you@example.com")
                    .Validate("email", email, Validate.Required("Email is required"), Validate.Email("Enter a valid email")),
                label: "Email", required: true),
            FormField(TextField(accessCode, v => { setAccessCode(v); ctx.MarkTouched("accessCode"); }, placeholder: "ACCESS-123")
                    .Validate("accessCode", accessCode, Validate.Required("Code is required"), Validate.MinLength(8, "Use at least 8 characters")),
                label: "Access code", required: true),
            Button("Submit", () => setStatus($"Submitted for {email}."))
                .AccentButton()
                .Set(b => b.IsEnabled = ctx.IsValid()),
            TextBlock(status))
            .Padding(24);
    }
}
