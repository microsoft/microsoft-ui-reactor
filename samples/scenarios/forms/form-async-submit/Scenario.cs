// id: form-async-submit
// intent: form submission with loading state
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Form async submit", width: 500, height: 400);

class App : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");
        var (email, setEmail) = UseState("");
        var (isSubmitting, setIsSubmitting) = UseState(false, threadSafe: true);
        var (status, setStatus) = UseState("Fill in the form.", threadSafe: true);

        return VStack(12,
            Heading("Newsletter signup"),
            TextField(name, setName, placeholder: "Full name", header: "Name"),
            TextField(email, setEmail, placeholder: "you@example.com", header: "Email"),
            HStack(8,
                Button(isSubmitting ? "Submitting..." : "Submit", () =>
                {
                    if (isSubmitting) return;
                    setIsSubmitting(true);
                    setStatus("Submitting...");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1200);
                        setStatus($"Saved {name} ({email}).");
                        setIsSubmitting(false);
                    });
                })
                    .AccentButton()
                    .Set(b => b.IsEnabled = !isSubmitting && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(email)),
                isSubmitting ? ProgressRing().IsActive(true).Width(20).Height(20) : Empty()),
            TextBlock(status))
            .Padding(24);
    }
}
