// id: form-with-server-errors
// intent: display server-side validation errors on form fields
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Controls.Validation;
using static Microsoft.UI.Reactor.Controls.Validation.FormFieldDsl;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Form with server errors", width: 500, height: 400);

class App : Component
{
    public override Element Render()
    {
        var ctx = this.UseValidationContext();
        var (email, setEmail) = UseState("");
        var (inviteCode, setInviteCode) = UseState("");
        var (status, setStatus) = UseState("Submit to simulate API validation.");

        return VStack(12,
            Heading("Invite signup"),
            FormField(TextField(email, v => { setEmail(v); ctx.MarkTouched("email"); }, placeholder: "user@contoso.com")
                    .Validate("email", email, Validate.Required("Email is required"), Validate.Email("Enter a valid email")),
                label: "Email", required: true, showWhen: ShowWhen.Always),
            FormField(TextField(inviteCode, v => { setInviteCode(v); ctx.MarkTouched("inviteCode"); }, placeholder: "INVITE-2025")
                    .Validate("inviteCode", inviteCode, Validate.Required("Invite code is required"), Validate.MinLength(6, "Use at least 6 characters")),
                label: "Invite code", required: true, showWhen: ShowWhen.Always),
            Button("Submit", () =>
            {
                ctx.ClearExternal("email");
                ctx.ClearExternal("inviteCode");
                ctx.MarkAllTouched();
                if (!ctx.IsValid())
                {
                    setStatus("Fix the client-side errors first.");
                    return;
                }
                if (!email.EndsWith("@contoso.com", System.StringComparison.OrdinalIgnoreCase))
                    ctx.AddExternal("email", "This email is not allowed by the server.");
                if (inviteCode != "INVITE-2025")
                    ctx.AddExternal("inviteCode", "That invite code has expired.");
                setStatus(ctx.IsValid() ? "Server accepted the form." : "Server returned field errors.");
            }).AccentButton(),
            TextBlock(status))
            .Padding(24);
    }
}
