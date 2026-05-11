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

class FormDemo : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");
        var (email, setEmail) = UseState("");
        var (agreeToTerms, setAgree) = UseState(false);
        var (darkMode, setDarkMode) = UseState(false);
        var (fontSize, setFontSize) = UseState(14.0);
        var (submitted, setSubmitted) = UseState(false);

        if (submitted)
        {
            return VStack(12,
                Heading("Form Submitted!"),
                TextBlock($"Name: {name}"),
                TextBlock($"Email: {email}"),
                TextBlock($"Dark mode: {(darkMode ? "Yes" : "No")}"),
                TextBlock($"Font size: {fontSize:F0}px"),
                Button("Back", () => setSubmitted(false))
            );
        }

        var isValid = !string.IsNullOrWhiteSpace(name)
            && !string.IsNullOrWhiteSpace(email)
            && agreeToTerms;

        return VStack(16,
            Heading("Registration Form"),

            VStack(8,
                TextBlock("Name"),
                TextField(name, setName, placeholder: "Enter your name").Width(300)
            ),

            VStack(8,
                TextBlock("Email"),
                TextField(email, setEmail, placeholder: "you@example.com").Width(300)
            ),

            ToggleSwitch(darkMode, setDarkMode, onContent: "Dark", offContent: "Light"),

            HStack(8,
                TextBlock("Font size:"),
                Slider(fontSize, 10, 30, setFontSize).Width(200),
                TextBlock($"{fontSize:F0}px")
            ),

            CheckBox(agreeToTerms, setAgree, label: "I agree to the terms"),

            When(!isValid, () =>
                TextBlock("Please fill all fields and agree to terms").Foreground(TertiaryText)),

            Button("Submit", () => setSubmitted(true)).DisabledFocusable(!isValid)
        );
    }
}
