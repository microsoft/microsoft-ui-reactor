using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<LoginRecipeApp>("Login Recipe", width: 360, height: 380
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
        // Only the two input fields are hand-held state. The in-flight flag
        // and the last error belong to the mutation below, not to UseState.
        var (email, setEmail) = UseState("");
        var (pwd, setPwd) = UseState("");
        // </snippet:state>

        // <snippet:submit>
        // UseMutation owns IsPending / Error / LastResult and cancels the
        // in-flight call on unmount. The mutator throws on failure; the hook
        // captures the exception into signIn.Error and re-renders.
        var signIn = UseMutation<(string Email, string Password), bool>(
            mutator: async (input, ct) =>
            {
                await Task.Delay(800, ct);                   // pretend API call
                // Sentinel long enough to pass local validation, so the
                // failure branch is actually reachable from the form.
                if (input.Password == "wrongpassword")
                    throw new InvalidOperationException("Invalid credentials.");
                return true;
            });
        // </snippet:submit>

        // <snippet:validation>
        // Local validation runs on every keystroke. The submit button is
        // disabled until the form is valid; in-flight submits are gated by
        // the same predicate, reading the mutation's own pending flag.
        var emailValid = email.Contains('@') && email.Contains('.');
        var pwdValid = pwd.Length >= 8;
        var canSubmit = emailValid && pwdValid && !signIn.IsPending;
        // </snippet:validation>

        // <snippet:render>
        // RunAsync returns a task that faults when the mutator throws, so
        // discarding it directly would leave that fault unobserved. The hook
        // has already captured the exception into signIn.Error — rendered
        // above — so awaiting here is purely about observing the task.
        async Task SubmitAsync()
        {
            try { await signIn.RunAsync((email, pwd)); }
            catch (Exception) { /* displayed via signIn.Error */ }
        }

        return VStack(12,
            Heading("Sign in"),
            TextBox(email, setEmail, placeholderText: "you@example.com",
                header: "Email").Width(280),
            PasswordBox(pwd, setPwd, placeholderText: "8+ characters"),
            signIn.Error is null
                ? Empty()
                : TextBlock(signIn.Error.Message).Foreground("#C42B1C"),
            // SubmitAsync swallows the fault above, so this discard cannot
            // leave anything unobserved.
            Button(signIn.IsPending ? "Signing in…" : "Sign in",
                    () => _ = SubmitAsync())
                .IsEnabled(canSubmit)
        ).Padding(20).Width(320);
        // </snippet:render>
    }
}
