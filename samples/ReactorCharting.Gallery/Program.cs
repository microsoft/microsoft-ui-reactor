using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Reactor.Navigation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using ReactorCharting.Gallery;

if (args.Contains("--self-test"))
{
    GallerySelfTestRunner.RunAll();
}
else
{
    ReactorApp.Run<GalleryApp>("Reactor Charting Gallery", width: 1400, height: 900,
#if DEBUG
        devtools: true,
#endif
        configure: host => XamlInterop.Register(host.Reconciler));
}

// ═══════════════════════════════════════════════════════════════════════
//  Routes
// ═══════════════════════════════════════════════════════════════════════

abstract record GalleryRoute;
sealed record Landing : GalleryRoute;
sealed record SampleDetail(string SampleTitle) : GalleryRoute;

// ═══════════════════════════════════════════════════════════════════════
//  Gallery shell — native TitleBar + stack navigation
// ═══════════════════════════════════════════════════════════════════════

class GalleryApp : Component
{
    // Exposed so the self-test harness can drive navigation without
    // having to find the TitleBar back button in the visual tree.
    internal static NavigationHandle<GalleryRoute>? CurrentNav;

    public override Element Render()
    {
        var (isDark, setIsDark) = UseState(false);
        var nav = UseNavigation<GalleryRoute>(new Landing());
        CurrentNav = nav;

        // Charts read this flag to pick axis/label/grid brushes that contrast
        // with the current theme. Set here so it's current by the time any
        // descendant sample.Render() runs.
        Microsoft.UI.Reactor.Charting.D3Charts.IsDarkTheme = isDark;

        // The Windows caption buttons (min/max/close) are system chrome —
        // they don't adapt to RequestedTheme on the Reactor tree. Push the
        // colors directly onto AppWindow.TitleBar so they track the toggle.
        UseEffect(() =>
        {
            if (Microsoft.UI.Reactor.ReactorApp.PrimaryWindow?.Host.Window?.AppWindow is { } aw)
            {
                var tb = aw.TitleBar;
                var fg       = isDark ? global::Windows.UI.Color.FromArgb(255, 240, 240, 240)
                                      : global::Windows.UI.Color.FromArgb(255,  30,  30,  30);
                var inactive = isDark ? global::Windows.UI.Color.FromArgb(255, 140, 140, 140)
                                      : global::Windows.UI.Color.FromArgb(255, 140, 140, 140);
                var transparent = global::Windows.UI.Color.FromArgb(0, 0, 0, 0);
                var hoverBg    = isDark ? global::Windows.UI.Color.FromArgb( 40, 255, 255, 255)
                                        : global::Windows.UI.Color.FromArgb( 20,   0,   0,   0);
                var pressedBg  = isDark ? global::Windows.UI.Color.FromArgb( 70, 255, 255, 255)
                                        : global::Windows.UI.Color.FromArgb( 40,   0,   0,   0);

                tb.ButtonForegroundColor          = fg;
                tb.ButtonHoverForegroundColor     = fg;
                tb.ButtonPressedForegroundColor   = fg;
                tb.ButtonInactiveForegroundColor  = inactive;
                tb.ButtonBackgroundColor          = transparent;
                tb.ButtonInactiveBackgroundColor  = transparent;
                tb.ButtonHoverBackgroundColor     = hoverBg;
                tb.ButtonPressedBackgroundColor   = pressedBg;
            }
        }, isDark);

        // Spec 033 §6 — Mica window backdrop. Drop the opaque
        // Theme.SolidBackground at the root so the material shows through;
        // sample cards keep their own backgrounds inside.
        return Border(
            FlexColumn(
                TitleBar("Reactor Charting Gallery")
                    .WithNavigation(nav)
                    .Subtitle(SubtitleFor(nav.CurrentRoute)) with
                {
                    RightHeader = ThemeToggle(isDark, setIsDark),
                },

                NavigationHost(nav, route => route switch
                {
                    Landing             => Component<LandingPage>(),
                    SampleDetail d      => LookupSample(d.SampleTitle) is { } s
                                             ? Component<SampleDetailPage, GallerySample>(s)
                                             : TextBlock($"Sample '{d.SampleTitle}' not found").Padding(24),
                    _                   => Empty(),
                }) with
                {
                    Transition = NavigationTransition.DrillIn(),
                }
            )
        )
        .RequestedTheme(isDark ? ElementTheme.Dark : ElementTheme.Light)
        .Backdrop(BackdropKind.Mica);
    }

    static string SubtitleFor(GalleryRoute route) => route switch
    {
        Landing          => $"{SampleRegistry.All.Length} samples — powered by D3.js ported to C#",
        SampleDetail d   => LookupSample(d.SampleTitle)?.Category ?? "",
        _                => "",
    };

    static GallerySample? LookupSample(string title) =>
        SampleRegistry.All.FirstOrDefault(s => s.Title == title);

    static Element ThemeToggle(bool isDark, Action<bool> setIsDark) =>
        Button(isDark ? "\uE793" : "\uE708", () => setIsDark(!isDark))
            .Foreground(Theme.AccentText)
            .AutomationName(isDark ? "Switch to Light theme" : "Switch to Dark theme")
            .ToolTip(isDark ? "Switch to Light" : "Switch to Dark")
            .Size(36, 36)
            .Resources(r => r
                .Set("ButtonBackground", Theme.Ref("SubtleFillColorTransparentBrush"))
                .Set("ButtonBorderBrush", Theme.Ref("SubtleFillColorTransparentBrush")))
            .Set(b =>
            {
                b.FontFamily = new FontFamily("Segoe MDL2 Assets");
                b.Padding = new Thickness(0);
                b.MinWidth = 0;
                b.MinHeight = 0;
            });
}

// ─── Landing page — category grid of samples ─────────────────────────

class LandingPage : Component
{
    public override Element Render()
    {
        var nav = UseNavigation<GalleryRoute>();

        var categories = SampleRegistry.All
            .GroupBy(s => s.Category)
            .OrderBy(g => CategoryOrder(g.Key));

        var sections = categories.Select(group =>
            VStack(8,
                SubHeading(group.Key).Foreground(Theme.PrimaryText),
                new FlexElement(
                    group.Select(sample =>
                        Button(
                            VStack(6,
                                SampleIcon(sample, 36),
                                Caption(sample.Title)
                            ).MaxWidth(100).HAlign(HorizontalAlignment.Center),
                            () => nav.Navigate(new SampleDetail(sample.Title))
                        ).Width(130).Height(90)
                         .WithKey(sample.Title)
                         .AutomationName(sample.Title)
                    ).ToArray()
                )
                {
                    Direction = FlexDirection.Row,
                    Wrap = FlexWrap.Wrap,
                    ColumnGap = 8,
                    RowGap = 8,
                }
            )
        ).ToArray();

        return ScrollView(
            VStack(24, sections).Padding(24, 12, 24, 24).Margin(4)
        );
    }

    static string IconPath(GallerySample sample) =>
        global::System.IO.Path.Combine(AppContext.BaseDirectory, "Icons", $"{sample.IconName}.svg");

    static Element SampleIcon(GallerySample sample, double size) =>
        Image(IconPath(sample)).Size(size, size).AccessibilityHidden();

    static int CategoryOrder(string category) => category switch
    {
        "Bars" => 0,
        "Lines" => 1,
        "Areas" => 2,
        "Dots" => 3,
        "Radial" => 4,
        "Hierarchies" => 5,
        "Networks" => 6,
        "Analysis" => 7,
        "Controls" => 8,
        "Interactive" => 9,
        "Animation" => 10,
        "Design" => 11,
        _ => 99,
    };
}

// ─── Sample detail page — chart + description + source ───────────────

class SampleDetailPage : Component<GallerySample>
{
    public override Element Render()
    {
        var sample = Props;

        return ScrollView(
            VStack(16,
                Heading(sample.Title).Foreground(Theme.PrimaryText),
                TextBlock(sample.Description)
                    .Foreground(Theme.SecondaryText)
                    .Set(tb => tb.TextWrapping = TextWrapping.Wrap),

                // Chart card. The sample's rendered Element is centered horizontally
                // so charts with explicit Width sit in the middle of the card rather
                // than slamming against the left edge or drifting right.
                Border(sample.Render().HAlign(HorizontalAlignment.Center))
                    .Background(Theme.CardBackground)
                    .WithBorder(Theme.CardStroke)
                    .CornerRadius(8)
                    .Padding(16),

                SubHeading("Source Code").Foreground(Theme.PrimaryText),
                // Source-code card: long lines scroll horizontally inside this box
                // so the outer page doesn't grow wider than the window.
                Border(
                    ScrollView(
                        (TextBlock(sample.SourceCode) with
                        {
                            IsTextSelectionEnabled = true,
                            TextWrapping = TextWrapping.NoWrap,
                        })
                        .Set(tb =>
                        {
                            tb.FontFamily = new FontFamily("Cascadia Code, Consolas, monospace");
                            tb.FontSize = 12;
                        })
                        .Foreground(Theme.PrimaryText)
                    )
                    .HorizontalScrollMode(ScrollMode.Auto)
                    .Set(sv =>
                    {
                        sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                        sv.MaxHeight = 400;
                    })
                )
                .Background(Theme.LayerFill)
                .WithBorder(Theme.SurfaceStroke)
                .CornerRadius(6)
                .Padding(16)
            ).Padding(24, 16, 24, 24)
        ).Set(sv => sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled);
    }
}
