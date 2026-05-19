using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<LoginRecipeApp>("Login Recipe", width: 360, height: 380
#if DEBUG
    , preview: true
#endif
);

class LoginRecipeApp : Component
{
    public override Element Render() => Component<LoginForm>();
}

class LoginForm : Component
{
    public override Element Render()
    {
        // <snippet:state>
        var (email, setEmail) = UseState("");
        var (pwd, setPwd) = UseState("");
        var (submitting, setSubmitting) = UseState(false);
        var (error, setError) = UseState<string?>(null);
        // </snippet:state>

        // <snippet:validation>
        // Local validation runs on every keystroke. The submit button is
        // disabled until the form is valid; in-flight submits are gated
        // by the same predicate.
        var emailValid = email.Contains('@') && email.Contains('.');
        var pwdValid = pwd.Length >= 8;
        var canSubmit = emailValid && pwdValid && !submitting;
        // </snippet:validation>

        // <snippet:submit>
        async Task Submit()
        {
            setSubmitting(true);
            setError(null);
            try
            {
                await Task.Delay(800);                       // pretend API call
                if (pwd == "wrong") setError("Invalid credentials.");
            }
            finally { setSubmitting(false); }
        }
        // </snippet:submit>

        // <snippet:render>
        return VStack(12,
            Heading("Sign in"),
            TextField(email, setEmail, placeholder: "you@example.com",
                header: "Email").Width(280),
            PasswordBox(pwd, setPwd, placeholderText: "8+ characters"),
            error is null
                ? Empty()
                : TextBlock(error).Foreground("#C42B1C"),
            Button(submitting ? "Signing in…" : "Sign in", () => _ = Submit())
                .IsEnabled(canSubmit)
        ).Padding(20).Width(320);
        // </snippet:render>
    }
}
