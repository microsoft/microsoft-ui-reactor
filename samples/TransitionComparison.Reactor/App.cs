// Transition Comparison — Reactor side.
//
// The XAML twin lives at samples/TransitionComparison.Xaml and is laid out the same way, so
// the two can sit side by side and be compared motion-for-motion. Each button here maps to
// exactly one WinUI NavigationTransitionInfo over there:
//
//   Reactor                                  WinUI XAML
//   ───────────────────────────────────────  ─────────────────────────────────────────────
//   NavigationTransition.Default             (Frame's own default)
//   NavigationTransition.Entrance()          EntranceNavigationTransitionInfo
//   NavigationTransition.Slide(FromRight)    SlideNavigationTransitionInfo { FromRight }
//   NavigationTransition.Slide(FromLeft)     SlideNavigationTransitionInfo { FromLeft }
//   NavigationTransition.Slide()             SlideNavigationTransitionInfo { FromBottom }
//   NavigationTransition.DrillIn()           DrillInNavigationTransitionInfo
//   NavigationTransition.None                SuppressNavigationTransitionInfo
//   NavigationTransition.Fade()              — Reactor extension, no counterpart
//   NavigationTransition.Spring()            — Reactor extension, no counterpart
//
// No XAML. Single-file WinUI 3 app using Reactor's functional projection.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<TransitionGallery>("Transitions — Reactor", width: 1040, height: 800);

// ─── Routes ───────────────────────────────────────────────────────────────────
// Two visually distinct pages. Navigating between them is what the transition animates.

record Stage(int Index);

sealed record TransitionChoice(string Label, string Detail, NavigationTransition Transition);

static class Choices
{
    public static readonly TransitionChoice[] All =
    [
        new("Default", "= Entrance", NavigationTransition.Default),
        new("Entrance", "slide up + fade", NavigationTransition.Entrance()),
        new("Slide — FromRight", "horizontal", NavigationTransition.Slide(SlideDirection.FromRight)),
        new("Slide — FromLeft", "horizontal", NavigationTransition.Slide(SlideDirection.FromLeft)),
        new("Slide — FromBottom", "the Slide() default", NavigationTransition.Slide()),
        new("DrillIn", "scale + fade", NavigationTransition.DrillIn()),
        new("None", "instant swap", NavigationTransition.None),
        new("Fade", "Reactor only", NavigationTransition.Fade()),
        new("Spring", "Reactor only", NavigationTransition.Spring()),
    ];
}

// ─── Shell ────────────────────────────────────────────────────────────────────

class TransitionGallery : Component
{
    public override Element Render()
    {
        var nav = UseNavigation(new Stage(0));
        var (selected, setSelected) = UseState(0);

        void Go(int choiceIndex)
        {
            setSelected(choiceIndex);
            var next = new Stage(nav.CurrentRoute.Index == 0 ? 1 : 0);
            nav.Navigate(next, new NavigateOptions { Transition = Choices.All[choiceIndex].Transition });
        }

        // A two-column Grid, not an HStack: a horizontal StackPanel sizes children to their
        // content, so the stage would shrink-wrap the label instead of filling the pane and the
        // transition distances would read against the wrong width. This matches the XAML twin's
        // Grid.ColumnDefinitions of 268 / *.
        return Grid(
            [GridSize.Px(268), GridSize.Star()],
            [GridSize.Star()],
            SidePanel(selected, Go, nav.CanGoBack, () => nav.GoBack()).Grid(row: 0, column: 0),
            NavigationHost(nav, route => StagePage(route.Index)).Grid(row: 0, column: 1)
        );
    }

    static Element SidePanel(int selected, Action<int> go, bool canGoBack, Action goBack) =>
        VStack(4,
            [
                TextBlock("Reactor").FontSize(26).Bold(),
                TextBlock("NavigationTransition").FontSize(12).Opacity(0.65).Margin(0, 0, 0, 14),

                .. Choices.All.Select((c, i) => (Element)Button(
                        VStack(0,
                            TextBlock(c.Label).FontSize(14).SemiBold(),
                            TextBlock(c.Detail).FontSize(11).Opacity(0.6)),
                        () => go(i))
                    .Width(232)
                    .Set(b => b.HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left)),

                TextBlock($"Playing: {Choices.All[selected].Label}")
                    .FontSize(12).Opacity(0.75).Margin(0, 16, 0, 6),

                Button("Go Back  (reverse motion)", goBack)
                    .Width(232)
                    .IsEnabled(canGoBack),
            ]
        ).Padding(18);

    // Each stage is a full-bleed slab so the motion is unmistakable — a small centred label
    // would make a 140px translate hard to read.
    //
    // The stretch is explicit: the first page inherits the host's alignment, but a page mounted
    // later by a navigation sizes to its content unless it asks for the space, which would make
    // the two apps' stages different sizes and the comparison worthless.
    static Element StagePage(int index) =>
        Border(
            VStack(6,
                TextBlock(index == 0 ? "PAGE  A" : "PAGE  B")
                    .FontSize(64).Bold().Foreground("#FFFFFF"),
                TextBlock(index == 0 ? "teal" : "violet")
                    .FontSize(18).Foreground("#FFFFFF").Opacity(0.85)
            ).Center()
        )
        .Background(index == 0 ? "#0F766E" : "#6D28D9")
        .HAlign(Microsoft.UI.Xaml.HorizontalAlignment.Stretch)
        .VAlign(Microsoft.UI.Xaml.VerticalAlignment.Stretch);
}
