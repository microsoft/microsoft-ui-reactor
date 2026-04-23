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
        ["Date and Time"] = "\uE787",
        ["Dialogs and Flyouts"] = "\uE8BD",
        ["Layout"] = "\uE8A1",
        ["Media"] = "\uE8B9",
        ["Menus and Toolbars"] = "\uE700",
        ["Navigation"] = "\uE8B0",
        ["Status and Info"] = "\uE946",
        ["Text"] = "\uE8D2",
        ["Styles"] = "\uE790",
    };

    public override Element Render()
    {
        var (selectedTag, setSelectedTag) = UseState("home");
        var (searchQuery, setSearchQuery) = UseState("");
        var (isDark, setIsDark) = UseState(false);
        var (isPaneOpen, setIsPaneOpen) = UseState(true);
        var (prevTag, setPrevTag) = UseState<string?>(null);

        var categoryTags = ControlRegistry.Categories
            .Select(c => c.ToLowerInvariant().Replace(" ", "-"))
            .ToHashSet();

        var designCategories = new HashSet<string> { "Design" };

        var controlNavItems = ControlRegistry.Categories
            .Where(cat => !designCategories.Contains(cat))
            .Select(cat =>
                NavItem(cat,
                    tag: cat.ToLowerInvariant().Replace(" ", "-")) with
                {
                    IconElement = FontIcon(CategoryIcons.GetValueOrDefault(cat, "\uE71D")),
                    Children = ControlRegistry.All
                        .Where(c => c.Category == cat)
                        .Select(c => NavItem(c.Title, tag: c.Tag))
                        .ToArray()
                })
            .ToArray();

        var navItems = new[]
        {
            NavItem("Home", tag: "home") with { IconElement = FontIcon("\uE80F") },
            NavItem("Design", tag: "design") with
            {
                IconElement = FontIcon("\uE790"),
                Children = ControlRegistry.All
                    .Where(c => c.Category == "Design")
                    .Select(c => NavItem(c.Title, tag: c.Tag))
                    .ToArray()
            },
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
                    $"{searchResults.Length} controls matching \"{searchQuery}\"")
                    .Margin(36, 24, 36, 0),
                GalleryControls.ControlCardGrid(searchResults, setSelectedTag)
                    .Margin(36, 0, 0, 36)
            );
        }
        else if (selectedTag == "home")
        {
            content = Component<HomePage, Action<string>>(setSelectedTag);
        }
        else if (selectedTag == "settings")
        {
            content = Component<SettingsPage>();
        }
        else if (categoryTags.Contains(selectedTag))
        {
            var categoryName = ControlRegistry.Categories
                .First(c => c.ToLowerInvariant().Replace(" ", "-") == selectedTag);
            var controls = ControlRegistry.All
                .Where(c => c.Category == categoryName)
                .ToArray();

            content = VStack(16,
                GalleryControls.PageHeader(categoryName,
                    $"{controls.Length} controls in this category")
                    .Margin(36, 24, 36, 0),
                GalleryControls.ControlCardGrid(controls, setSelectedTag)
                    .Margin(36, 0, 0, 36)
            );
        }
        else
        {
            content = PageRouter.Route(selectedTag);
        }

        var shell = Grid(
            columns: ["*"], rows: ["Auto", "*"],

            (TitleBar("Reactor WinUI Gallery") with
            {
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
                RightHeader =
                    Button(isDark ? "\uE706" : "\uE708", () => setIsDark(!isDark))
                        .Set(b => b.FontFamily = new FontFamily("Segoe MDL2 Assets"))
                        .Width(40).Height(36)
                        .ToolTip(isDark ? "Switch to Light" : "Switch to Dark")
                        .AutomationName(isDark ? "Switch to Light theme" : "Switch to Dark theme"),
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
                SelectedTag = selectedTag,
                IsPaneOpen = isPaneOpen,
                OnSelectionChanged = tag =>
                {
                    setSearchQuery("");
                    if (tag != null)
                    {
                        setPrevTag(selectedTag);
                        setSelectedTag(tag);
                    }
                },
                IsBackEnabled = false,
                IsSettingsVisible = true,
            })
            .Set(nv =>
            {
                nv.IsPaneToggleButtonVisible = false;
                nv.IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed;
                nv.SelectionChanged += (s, args) =>
                {
                    if (args.IsSettingsSelected)
                    {
                        setSearchQuery("");
                        setPrevTag(selectedTag);
                        setSelectedTag("settings");
                    }
                };
            })
            .Grid(row: 1)
        );

        return Border(shell)
            .Background(Theme.SolidBackground)
            .RequestedTheme(isDark ? ElementTheme.Dark : ElementTheme.Light);
    }
}
