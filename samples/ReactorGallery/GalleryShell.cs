using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace WinUIGalleryReactor;

class GalleryShell : Component
{
    static readonly Dictionary<string, string> CategoryIcons = new()
    {
        ["Basic Input"] = "\uE73A",
        ["Collections"] = "\uE8A9",
        ["Data"] = "\uE7C3",
        ["Date and Time"] = "\uE787",
        ["Dialogs and Flyouts"] = "\uE8BD",
        ["Fundamentals"] = "\uE8F1",
        ["Layout"] = "\uE8A1",
        ["Media"] = "\uE8B9",
        ["Menus and Toolbars"] = "\uE700",
        ["Navigation"] = "\uE8B0",
        ["Status and Info"] = "\uE946",
        ["Text"] = "\uE8D2",
        ["Patterns"] = "\uE943",
        ["Styles"] = "\uE790",
    };

    public override Element Render()
    {
        // A cold-start deep link (`reactor-gallery:///item/button`) seeds the initial
        // view. UseState only reads its initial value on the first render, so links
        // that arrive later go through the RouteActivated subscription below.
        var initialRoute = GalleryActivation.InitialRoute;
        var (selectedTag, setSelectedTag) = UseState(initialRoute?.Tag ?? GalleryRoutes.HomeTag);
        var (searchQuery, setSearchQuery) = UseState(SearchTextFor(initialRoute));
        var (isDark, setIsDark) = UseState(false);
        var (isPaneOpen, setIsPaneOpen) = UseState(true);
        var (prevTag, setPrevTag) = UseState<string?>(null);

        // Warm-start deep links: GalleryActivation marshals them onto the UI thread and
        // raises RouteActivated. The subscription mounts once, so it reads the live tag
        // through a ref — capturing `selectedTag` directly would pin the back target to
        // whatever was selected on the very first render.
        var currentTag = UseRef(selectedTag);
        currentTag.Current = selectedTag;

        // The single navigation entry point for every source: nav items, control cards,
        // the Home page, and deep links.
        //
        // Nothing derives navigation from NavigationView.SelectionChanged, and that is the
        // whole point. WinUI raises SelectionChanged for our own programmatic SelectedTag
        // writes as well as for user clicks, and Reactor forwards both, so a handler there
        // cannot tell a deep link's own echo from a real click — it would clear the query a
        // `/search?q=` link just set, and overwrite the back target with the destination.
        // `OnItemInvoked` is user-only, so SelectedTag stays pure controlled output.
        //
        // Created once and held in a ref so the callback handed to HomePage /
        // ControlCardGrid is reference-stable and doesn't force them to re-render on every
        // shell pass. Safe to capture from the first render: it touches only refs and
        // UseState setters, both of which are stable for the component's lifetime.
        void NavigateTo(string tag, string search)
        {
            if (tag != currentTag.Current) setPrevTag(currentTag.Current);
            setSearchQuery(search);
            setSelectedTag(tag);
        }

        var navigate = UseRef<Action<string>>(null!);
        navigate.Current ??= tag => NavigateTo(tag, string.Empty);

        UseEffect(() =>
        {
            void OnRouteActivated(GalleryRoute route) => NavigateTo(route.Tag, SearchTextFor(route));

            GalleryActivation.RouteActivated += OnRouteActivated;

            // A link can land between process start and this subscription — the shell
            // reads InitialRoute only once, on its first render, so anything arriving
            // afterwards is parked instead. Drain it now that there is a listener.
            if (GalleryActivation.TryTakePendingRoute(out var pending))
                OnRouteActivated(pending);

            return () => GalleryActivation.RouteActivated -= OnRouteActivated;
        });

        // Category slugs double as NavigationView tags AND as the `/category/{name}`
        // segment of a deep link, so both sides go through GalleryRoutes.CategorySlug —
        // if these two ever computed the slug differently, category links would break.
        var categoryTags = ControlRegistry.Categories
            .Select(GalleryRoutes.CategorySlug)
            .ToHashSet();

        var nonControlCategories = new HashSet<string>(ControlRegistry.NonControlCategories, StringComparer.Ordinal);

        // Design and Fundamentals are not control categories — they sit above the "Controls"
        // header as their own top-level entries rather than inside it.
        NavigationViewItemData CategoryNavItem(string category, string glyph) =>
            NavItem(category, tag: GalleryRoutes.CategorySlug(category)) with
            {
                IconElement = FontIcon(glyph),
                Children = ControlRegistry.All
                    .Where(c => c.Category == category)
                    .Select(c => NavItem(c.Title, tag: c.Tag))
                    .ToArray()
            };

        var controlNavItems = ControlRegistry.Categories
            .Where(cat => !nonControlCategories.Contains(cat))
            .Select(cat => CategoryNavItem(cat, CategoryIcons.GetValueOrDefault(cat, "\uE71D")))
            .ToArray();

        var navItems = new[]
        {
            NavItem("Home", tag: GalleryRoutes.HomeTag) with { IconElement = FontIcon("\uE80F") },
            CategoryNavItem("Fundamentals", "\uE8F1"),
            CategoryNavItem("Design", "\uE790"),
            NavItemHeader("Controls"),
        }
        .Concat(controlNavItems)
        .ToArray();

        // Search filtering
        var searchResults = !string.IsNullOrWhiteSpace(searchQuery)
            ? ControlRegistry.Search(searchQuery) : null;

        Element content;
        if (searchResults != null)
        {
            content = VStack(16,
                GalleryControls.PageHeader("Search Results",
                    $"{searchResults.Length} matching \"{searchQuery}\"")
                    .Margin(36, 24, 36, 0),
                GalleryControls.ControlCardGrid(searchResults, navigate.Current)
                    .Margin(36, 0, 0, 36)
            );
        }
        else if (selectedTag == GalleryRoutes.HomeTag)
        {
            content = Component<HomePage, Action<string>>(navigate.Current);
        }
        else if (selectedTag == GalleryRoutes.SettingsTag)
        {
            content = Component<SettingsPage>();
        }
        else if (categoryTags.Contains(selectedTag))
        {
            var categoryName = ControlRegistry.Categories
                .First(c => GalleryRoutes.CategorySlug(c) == selectedTag);
            var controls = ControlRegistry.All
                .Where(c => c.Category == categoryName)
                .ToArray();

            content = VStack(16,
                GalleryControls.PageHeader(categoryName,
                    ControlRegistry.IsControlCategory(categoryName)
                        ? $"{controls.Length} controls in this category"
                        : $"{controls.Length} topics in this category")
                    .Margin(36, 24, 36, 0),
                GalleryControls.ControlCardGrid(controls, navigate.Current)
                    .Margin(36, 0, 0, 36)
            );
        }
        else
        {
            content = PageRouter.Route(selectedTag);
        }

        var shell = Grid(
            columns: [GridSize.Star()], rows: [GridSize.Auto, GridSize.Star()],

            (TitleBar("Reactor WinUI Gallery") with
            {
                Icon = new ImageIconData(new Uri(global::System.IO.Path.Combine(
                    global::System.AppContext.BaseDirectory, "Assets", "GalleryIcon.ico"))),
                Content = HStack(8,
                    AutoSuggestBox(searchQuery, setSearchQuery)
                        .Width(320)
                        .OnMount(el =>
                        {
                            var box = (Microsoft.UI.Xaml.Controls.AutoSuggestBox)el;
                            box.PlaceholderText = "Search controls and Samples...";
                            box.QueryIcon = new SymbolIcon(Symbol.Find);
                        })
                ),
                RightHeader = HStack(4,
                    Component<CopyDeepLinkButton, string>(
                        GalleryRoutes.UriForCurrentView(selectedTag, searchQuery)),
                    Button(Icon(FontIcon(isDark ? "\uE706" : "\uE708",
                        fontSize: GalleryControls.TitleBarGlyphSize)), () => setIsDark(!isDark))
                        .Width(40).Height(36).Padding(0)
                        .ToolTip(isDark ? "Switch to Light" : "Switch to Dark")
                        .AutomationName(isDark ? "Switch to Light theme" : "Switch to Dark theme")
                ),
                IsPaneToggleButtonVisible = true,
                OnPaneToggleRequested = () => setIsPaneOpen(!isPaneOpen),
                IsBackButtonVisible = true,
                IsBackButtonEnabled = prevTag != null,
                OnBackRequested = prevTag != null ? () =>
                {
                    var back = prevTag;
                    setPrevTag(null);
                    setSearchQuery("");
                    if (back != null) setSelectedTag(back);
                } : null,
            }).Grid(row: 0),

            (NavigationView(
                navItems,
                content: content
            ) with
            {
                // NavigationView's built-in settings entry is the one item the control
                // creates itself, so it can only be selected through the reserved
                // sentinel — the gallery's own "settings" tag would select nothing and
                // leave the pane with no highlight after a `/settings` deep link or a
                // Back into settings. OnItemInvoked below translates the same pair in
                // the opposite direction.
                SelectedTag = selectedTag == GalleryRoutes.SettingsTag
                    ? NavigationViewElement.SettingsTag
                    : selectedTag,
                IsPaneOpen = isPaneOpen,
                OnItemInvoked = tag =>
                {
                    // User-only: unlike OnSelectedTagChanged this never fires for our own
                    // programmatic SelectedTag writes, so deep links and Back can't be
                    // undone by their own echo. It does fire for an already-selected item,
                    // which NavigateTo handles by leaving the back target alone.
                    if (tag is null) return;
                    NavigateTo(
                        tag == NavigationViewElement.SettingsTag ? GalleryRoutes.SettingsTag : tag,
                        string.Empty);
                },
                IsBackEnabled = false,
                IsSettingsVisible = true,
                IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
                IsPaneToggleButtonVisible = false,
                // No OnSettingsSelected: it is raised from the same SelectionChanged
                // trampoline as OnSelectedTagChanged, so it echoes programmatic writes too.
                // OnItemInvoked already reports the settings item (as SettingsTag).
                OnPaneOpenChanged = setIsPaneOpen,
            })
            .Grid(row: 1)
        );

        // Spec 033 §6 — Mica window backdrop. The shell intentionally drops the
        // opaque Theme.SolidBackground at the root so Mica is visible through
        // the layout chrome; cards and surfaces inside still set their own
        // backgrounds to float above the material.
        return Border(shell)
            .RequestedTheme(isDark ? ElementTheme.Dark : ElementTheme.Light)
            .Backdrop(BackdropKind.Mica);
    }

    /// <summary>
    /// Search text a route implies. Only a <c>/search?q=</c> link carries one; every
    /// other route clears the box so the deep-linked page is actually visible instead
    /// of hidden behind stale search results.
    /// </summary>
    static string SearchTextFor(GalleryRoute? route) =>
        route is { Kind: GalleryRouteKind.Search } ? route.Query ?? "" : "";
}
