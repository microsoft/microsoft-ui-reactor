// id: form-text-fields
// intent: basic form with multiple text inputs and labels
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Form text fields", width: 500, height: 400);

class App : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");
        var (email, setEmail) = UseState("");
        var (message, setMessage) = UseState("");

        return VStack(12,
            Heading("Contact form"),
            TextField(name, setName, placeholder: "Full name", header: "Name"),
            TextField(email, setEmail, placeholder: "you@example.com", header: "Email"),
            TextField(message, setMessage, placeholder: "How can we help?", header: "Message"),
            TextBlock($"Draft: {name} / {email}"))
            .Padding(24);
    }
}
