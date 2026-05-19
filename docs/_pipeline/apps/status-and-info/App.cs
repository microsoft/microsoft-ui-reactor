using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;

ReactorApp.Run<StatusAndInfoApp>("Status and Info", width: 640, height: 1000
#if DEBUG
    , preview: true
#endif
);

// <snippet:infobar-severities>
class InfoBarSeveritiesDemo : Component
{
    public override Element Render() => VStack(8,
        SubHeading("InfoBar — severity fluents"),
        InfoBar("Saving…", "Your changes are being written.")
            .Informational().Closable(false),
        InfoBar("Saved", "Your changes were saved.")
            .Success().Closable(false),
        InfoBar("Slow connection",
            "Some assets may not load until the network recovers.")
            .Warning().Closable(false),
        InfoBar("Save failed",
            "The destination drive is read-only.")
            .Error().Closable(false)
    ).Padding(24);
}
// </snippet:infobar-severities>

// <snippet:infobar-dismiss>
class InfoBarDismissDemo : Component
{
    public override Element Render()
    {
        // InfoBar is a controlled component — you own the IsOpen flag,
        // and you reset it via the OnClosed callback the user dismiss
        // raises.
        var (open, setOpen) = UseState(true);

        return VStack(8,
            SubHeading("Dismiss and re-open"),
            InfoBar("Tip", "InfoBar uses controlled visibility.") with
            {
                IsOpen = open,
                IsClosable = true,
                OnClosed = () => setOpen(false),
                Severity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational,
            },
            Button("Show again", () => setOpen(true)).Disabled(open)
        ).Padding(24);
    }
}
// </snippet:infobar-dismiss>

// <snippet:infobadge>
class InfoBadgeDemo : Component
{
    public override Element Render() => VStack(8,
        SubHeading("InfoBadge"),
        HStack(16,
            // Dot variant — no value, just a presence indicator.
            VStack(4,
                InfoBadge(),
                TextBlock("dot").FontSize(11).Opacity(0.6)),
            // Numeric — common for unread counts.
            VStack(4,
                InfoBadge(3),
                TextBlock("count").FontSize(11).Opacity(0.6)),
            VStack(4,
                InfoBadge(127),
                TextBlock("large").FontSize(11).Opacity(0.6))
        )
    ).Padding(24);
}
// </snippet:infobadge>

// <snippet:progress-bar>
class ProgressBarDemo : Component
{
    public override Element Render()
    {
        var (value, setValue) = UseState(35.0);

        return VStack(12,
            SubHeading("ProgressBar"),
            Progress(value).Width(320),
            TextBlock($"{value:0}%").Opacity(0.6),
            HStack(8,
                Button("−10", () => setValue(Math.Max(0, value - 10))),
                Button("+10", () => setValue(Math.Min(100, value + 10)))
            ),
            // Indeterminate — no value argument.
            SubHeading("Indeterminate"),
            ProgressIndeterminate().Width(320)
        ).Padding(24);
    }
}
// </snippet:progress-bar>

// <snippet:progress-ring>
class ProgressRingDemo : Component
{
    public override Element Render() => VStack(12,
        SubHeading("ProgressRing"),
        // Determinate ring at 60%.
        ProgressRing(0.6).Width(48).Height(48),
        // Indeterminate spinner.
        ProgressRing().Active().Width(48).Height(48)
    ).Padding(24);
}
// </snippet:progress-ring>

// <snippet:teaching-tip>
class TeachingTipDemo : Component
{
    public override Element Render()
    {
        var (show, setShow) = UseState(false);

        return VStack(12,
            SubHeading("TeachingTip"),
            Button("Show tip", () => setShow(true)),
            TeachingTip("Try the new sort menu",
                "Sort across multiple columns by holding Shift.") with
            {
                IsOpen = show,
                OnClosed = () => setShow(false),
            }
        ).Padding(24);
    }
}
// </snippet:teaching-tip>

// <snippet:pips-pager>
class PipsPagerDemo : Component
{
    public override Element Render()
    {
        var (page, setPage) = UseState(2);
        var pageCount = 5;

        return VStack(8,
            SubHeading("PipsPager"),
            TextBlock($"Page {page + 1} of {pageCount}").Opacity(0.6),
            PipsPager(pageCount, page, setPage)
        ).Padding(24);
    }
}
// </snippet:pips-pager>

// <snippet:person-picture>
class PersonPictureDemo : Component
{
    public override Element Render() => VStack(8,
        SubHeading("PersonPicture"),
        HStack(12,
            PersonPicture()
                .DisplayName("Ada Lovelace")
                .Width(48).Height(48),
            PersonPicture()
                .Initials("CB")
                .Width(48).Height(48),
            // No name or initials — falls back to the generic person glyph.
            PersonPicture().Width(48).Height(48)
        )
    ).Padding(24);
}
// </snippet:person-picture>

// <snippet:rating>
class RatingDemo : Component
{
    public override Element Render()
    {
        var (rating, setRating) = UseState(3.0);

        return VStack(8,
            SubHeading("RatingControl"),
            RatingControl(rating, setRating)
                .Caption("Tap a star or use ←/→ to rate"),
            TextBlock($"Selected: {rating} stars").Opacity(0.6)
        ).Padding(24);
    }
}
// </snippet:rating>

class StatusAndInfoApp : Component
{
    public override Element Render() => ScrollView(
        VStack(24,
            Heading("Status and Info"),
            Component<InfoBarSeveritiesDemo>(),
            Component<InfoBarDismissDemo>(),
            Component<InfoBadgeDemo>(),
            Component<ProgressBarDemo>(),
            Component<ProgressRingDemo>(),
            Component<TeachingTipDemo>(),
            Component<PipsPagerDemo>(),
            Component<PersonPictureDemo>(),
            Component<RatingDemo>()
        ).Padding(24)
    );
}
