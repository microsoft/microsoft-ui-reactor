using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Media;

class PersonPicturePage : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("Jane Doe");

        return ScrollView(
            VStack(16,
                PageHeader("PersonPicture",
                    "A control that displays a circular avatar for a person."),

                SampleCard("Display Name",
                    HStack(16,
                        PersonPicture().DisplayName("Jane Doe").Width(64).Height(64),
                        PersonPicture().DisplayName("John Smith").Width(64).Height(64),
                        PersonPicture().DisplayName("Alice Bob").Width(64).Height(64)
                    ),
                    @"PersonPicture().DisplayName(""Jane Doe"").Width(64).Height(64)"),

                SampleCard("Initials",
                    HStack(16,
                        PersonPicture().Initials("JD").Width(48).Height(48),
                        PersonPicture().Initials("AB").Width(48).Height(48),
                        PersonPicture().Initials("XY").Width(48).Height(48)
                    ),
                    @"PersonPicture().Initials(""JD"").Width(48).Height(48)"),

                SampleCard("Interactive Display Name",
                    VStack(8,
                        PersonPicture().DisplayName(name).Width(72).Height(72),
                        TextBox(name, s => setName(s), placeholderText: "Enter name").Width(250)
                    ),
                    @"var (name, setName) = UseState(""Jane Doe"");
PersonPicture().DisplayName(name).Width(72).Height(72)
TextBox(name, s => setName(s))")
            ).Margin(36, 24, 36, 36)
        );
    }
}
