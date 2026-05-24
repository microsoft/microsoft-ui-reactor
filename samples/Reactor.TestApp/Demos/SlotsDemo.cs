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

class SlotsDemo : Component
{
    record CardProps(
        Element? Header = null,
        Element? Body = null,
        Element? Footer = null
    );

    class Card : Component<CardProps>
    {
        public override Element Render()
        {
            return Border(VStack(0,
                Props.Header is not null
                    ? Border(Props.Header)
                        .Padding(horizontal: 12, vertical: 8).Background(SubtleFill)
                        .WithBorder(ControlStroke)
                    : Empty(),
                Props.Body is not null
                    ? Border(Props.Body).Padding(16)
                    : Empty(),
                Props.Footer is not null
                    ? Border(Props.Footer)
                        .Padding(horizontal: 8, vertical: 12).Background(LayerFill)
                        .WithBorder(ControlStroke)
                    : Empty()
            )).CornerRadius(8).WithBorder(ControlStroke);
        }
    }

    record InfoRowProps(Element? Leading = null, Element? Title = null, Element? Trailing = null);

    class InfoRow : Component<InfoRowProps>
    {
        public override Element Render()
        {
            return HStack(12,
                Props.Leading ?? Empty(),
                Props.Title is not null
                    ? Border(Props.Title).Flex(grow: 1)
                    : Empty(),
                Props.Trailing ?? Empty()
            ).Padding(8).VAlign(VerticalAlignment.Center);
        }
    }

    public override Element Render()
    {
        var (name, setName) = UseState("World");
        var (expanded, setExpanded) = UseState(false);

        return ScrollView(VStack(16,
            Heading("Slots Pattern"),
            TextBlock("Components accept Element? props as named content areas — like React children/render props."),

            // 1. Card with all three slots
            SubHeading("1. Card — Header / Body / Footer"),
            Component<Card, CardProps>(new(
                Header: TextBlock("User Profile").SemiBold(),
                Body: VStack(8,
                    TextBlock("Name: Alice Johnson"),
                    TextBlock("Email: alice@example.com"),
                    TextBlock("Role: Software Engineer")
                ),
                Footer: HStack(8,
                    Button("Edit", () => { }),
                    Button("Delete", () => { })
                )
            )),

            // 2. Partial slots
            SubHeading("2. Partial slots — omit Footer"),
            Component<Card, CardProps>(new(
                Header: TextBlock("Notification").SemiBold(),
                Body: TextBlock("Build completed successfully. No footer needed.")
            )),

            // 3. Dynamic slot content
            SubHeading("3. Dynamic slot content"),
            TextBox(name, setName, placeholderText: "Type a name...").Width(220),
            Component<Card, CardProps>(new(
                Header: HStack(8,
                    Border(Empty()).Background(Accent).CornerRadius(12).Size(24, 24),
                    TextBlock($"Hello, {name}!").SemiBold()
                ),
                Body: TextBlock($"The slot content updates when you type. Name length: {name.Length} characters."),
                Footer: Button("Reset", () => setName("World"))
            )),

            // 4. InfoRow — Leading / Title / Trailing
            SubHeading("4. InfoRow — Leading / Title / Trailing"),
            TextBlock("Another slot pattern: a row with optional leading icon, title area, and trailing action."),
            VStack(4,
                Component<InfoRow, InfoRowProps>(new(
                    Leading: TextBlock("\uE77B").Set(t => t.FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"]),
                    Title: TextBlock("Inbox").SemiBold(),
                    Trailing: TextBlock("12").Foreground(TertiaryText)
                )),
                Component<InfoRow, InfoRowProps>(new(
                    Leading: TextBlock("\uE724").Set(t => t.FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"]),
                    Title: TextBlock("Sent"),
                    Trailing: TextBlock("3").Foreground(TertiaryText)
                )),
                Component<InfoRow, InfoRowProps>(new(
                    Leading: TextBlock("\uE74D").Set(t => t.FontFamily = (FontFamily)Application.Current.Resources["SymbolThemeFontFamily"]),
                    Title: TextBlock("Deleted"),
                    Trailing: TextBlock("0").Foreground(TertiaryText)
                ))
            ),

            // 5. Naming conventions
            SubHeading("5. Slot naming conventions"),
            VStack(4,
                Caption("Single default slot: params Element?[] children"),
                Caption("Named slots: Element?-typed props on a record"),
                Caption("Common names: Header, Body/Content, Footer/Actions, Leading, Trailing, Icon, Label, Title"),
                Caption("Optional slots: Element? with = null default, skip rendering when null"),
                Caption("Memo tip: static slot content (Text) → memo works. Slots with handlers → use UseCallback.").SemiBold()
            )
        ));
    }
}
