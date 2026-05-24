using System.Diagnostics;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Reactor.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

class PersistedDemo : Component
{
    public override Element Render()
    {
        // Persisted state — survives tab switches. Window scope: this state is
        // bound to the current host's lifetime, not process-wide.
        var (pName, setPName) = UsePersisted("demo.p.name", "", PersistedScope.Window);
        var (pEmail, setPEmail) = UsePersisted("demo.p.email", "", PersistedScope.Window);
        var (pColor, setPColor) = UsePersisted("demo.p.color", "Blue", PersistedScope.Window);

        // Regular state — lost on tab switch
        var (rName, setRName) = UseState("");
        var (rEmail, setREmail) = UseState("");
        var (rColor, setRColor) = UseState("Blue");

        string[] colors = ["Blue", "Red", "Green", "Purple"];

        Element FormColumn(string heading,
            string name, Action<string> setName,
            string email, Action<string> setEmail,
            string color, Action<string> setColor)
        {
            return VStack(12,
                SubHeading(heading),
                VStack(4, TextBlock("Name"), TextBox(name, setName, placeholderText: "Enter name").Width(220)),
                VStack(4, TextBlock("Email"), TextBox(email, setEmail, placeholderText: "you@example.com").Width(220)),
                VStack(4,
                    TextBlock("Color"),
                    HStack(4, colors.Select(c =>
                        Button(c, () => setColor(c)).IsEnabled(!(color == c))
                    ).ToArray())
                ),
                Border(VStack(4,
                    TextBlock("Current values:").SemiBold(),
                    TextBlock($"Name: {(string.IsNullOrEmpty(name) ? "(empty)" : name)}"),
                    TextBlock($"Email: {(string.IsNullOrEmpty(email) ? "(empty)" : email)}"),
                    TextBlock($"Color: {color}")
                )).Padding(12).CornerRadius(8).Background(SubtleFill)
            );
        }

        return ScrollView(VStack(16,
            Heading("Persisted State"),
            TextBlock("UsePersisted keeps values across unmount/remount (tab switches)."),

            HStack(32,
                FormColumn("UsePersisted (survives)",
                    pName, setPName, pEmail, setPEmail, pColor, setPColor),
                FormColumn("UseState (lost on switch)",
                    rName, setRName, rEmail, setREmail, rColor, setRColor)
            ),

            Border(
                TextBlock("Fill in both sides, switch to another tab, then come back. Left persists, right resets.")
            ).Padding(12).CornerRadius(8).Background(SubtleFill)
        ));
    }
}
