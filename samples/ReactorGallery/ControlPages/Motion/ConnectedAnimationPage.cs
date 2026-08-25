using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Motion;

class ConnectedAnimationPage : Component
{
    public override Element Render() =>
        PageContent("Connected Animation",
            "ConnectedAnimation carries a visual across a view change. The element leaving the tree "
            + "publishes a snapshot under its key, the element entering the tree under the same key plays "
            + "that snapshot into its own position, and the result reads as one object moving rather than "
            + "two views swapping. Source and destination must appear in the same render.",

            Component<ListToDetailSample>(),
            Component<KeyMatchSample>());

    // ── List to detail ───────────────────────────────────────────────

    sealed class ListToDetailSample : Component
    {
        static readonly (string Title, string Blurb)[] Items =
        [
            ("Aurora", "Ribbons of light over the northern ice."),
            ("Basalt", "Hexagonal columns from a slow-cooled flow."),
            ("Cirrus", "Ice crystals drawn out by high wind."),
            ("Delta", "Where a river gives its sediment back."),
        ];

        public override Element Render()
        {
            var (selected, setSelected) = UseState<string?>(null);

            return SampleCard("List to detail",
                VStack(12,
                    Stage(selected is null ? ListView(setSelected) : DetailView(selected, setSelected)),
                    Caption(selected is null
                            ? "Every row carries a key, but only the row you pick has a destination — the "
                            + "other three are released at the end of the render instead of hovering over "
                            + "the detail view."
                            : "The row's snapshot travelled here and grew into the headline. Going back "
                            + "reverses it: the headline is now the source.")
                        .Foreground(Theme.SecondaryText)
                        .TextWrapping(TextWrapping.Wrap)),
                sourceCode: @"
string[] items = [""Aurora"", ""Basalt"", ""Cirrus"", ""Delta""];
var (selected, setSelected) = UseState<string?>(null);

if (selected is not null)
    return VStack(12,
        Button(""Back to list"", () => setSelected(null)),
        TextBlock(selected).FontSize(34).Bold()
            .ConnectedAnimation($""hero-{selected}""));

return VStack(6, items.Select(i =>
    Button(i, () => setSelected(i))
        .ConnectedAnimation($""hero-{i}"")).ToArray());

// Source and destination must appear in the SAME render. The reconciler publishes the
// outgoing element's snapshot during the reconcile pass and plays it into the incoming
// element at the end of that same pass. A key that unmounts without a matching mount in
// that render is released, which is what keeps the rows you didn't pick from ghosting.
");
        }

        static Element ListView(Action<string?> setSelected) =>
            VStack(8,
                Caption("Pick a row — its title flies into the detail headline.")
                    .Foreground(Theme.SecondaryText),
                VStack(6, Items.Select(i =>
                    (Element)Button(i.Title, () => setSelected(i.Title))
                        .HAlign(HorizontalAlignment.Stretch)
                        .ConnectedAnimation($"gallery-hero-{i.Title}")).ToArray()));

        static Element DetailView(string selected, Action<string?> setSelected)
        {
            var item = Items.First(i => i.Title == selected);
            return VStack(16,
                Button("← Back to list", () => setSelected(null)),
                // Same key as the row that was clicked, so the row's snapshot travels here.
                TextBlock(item.Title).FontSize(34).Bold()
                    .Foreground(Theme.PrimaryText)
                    .ConnectedAnimation($"gallery-hero-{item.Title}"),
                TextBlock(item.Blurb)
                    .Foreground(Theme.SecondaryText)
                    .TextWrapping(TextWrapping.Wrap));
        }

        // Fixed-size stage so the two views occupy the same box and the travel distance
        // is the thing that changes, not the card's height.
        static Element Stage(Element content) =>
            Border(content)
                .Background(Theme.SubtleFill)
                .CornerRadius(8)
                .Padding(16)
                .Width(340)
                .Height(250)
                .HAlign(HorizontalAlignment.Left);
    }

    // ── Key matching ─────────────────────────────────────────────────

    sealed class KeyMatchSample : Component
    {
        public override Element Render()
        {
            var (open, setOpen) = UseState(false);
            var (connected, setConnected) = UseState(true);

            // Turning the destination key off is the point of this sample: the headline
            // still mounts, it just has nothing to travel from, so it cuts straight in.
            var headline = TextBlock("Fluent").FontSize(34).Bold()
                .Foreground(Theme.PrimaryText);

            return SampleCard("Both ends need the same key",
                VStack(12,
                    Border(open
                            ? VStack(16,
                                Button("← Back", () => setOpen(false)),
                                connected
                                    ? headline.ConnectedAnimation("gallery-key-demo")
                                    : headline)
                            : VStack(12,
                                Caption("Open the detail view to compare.")
                                    .Foreground(Theme.SecondaryText),
                                Button("Fluent", () => setOpen(true))
                                    .ConnectedAnimation("gallery-key-demo")
                                    .HAlign(HorizontalAlignment.Left)))
                        .Background(Theme.SubtleFill)
                        .CornerRadius(8)
                        .Padding(16)
                        .Width(340)
                        .Height(170)
                        .HAlign(HorizontalAlignment.Left),
                    Caption(connected
                            ? "Keys match — the button's snapshot travels into the headline."
                            : "The destination has no key, so nothing travels and the headline appears "
                            + "at its final position. That is also what a typo in either key looks like.")
                        .Foreground(Theme.SecondaryText)
                        .TextWrapping(TextWrapping.Wrap)),
                sourceCode: @"
// A key with no partner is inert -- it never throws and never warns, so a mismatch reads
// exactly like ""the animation is broken"". Both ends must spell the key the same way.
Button(""Fluent"", () => setOpen(true))
    .ConnectedAnimation(""hero"")

TextBlock(""Fluent"").FontSize(34).Bold()
    .ConnectedAnimation(""hero"")   // drop this and the headline just appears
",
                OptionPanel(
                    TextBlock("Destination key"),
                    ToggleSwitch(connected, setConnected,
                        onContent: "Matches", offContent: "Missing")));
        }
    }
}
