using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

/// <summary>
/// E2E fixtures for hierarchical <c>NavigationView</c> expand/collapse, mirroring
/// the ReactorGallery shell shape: a stateful component that holds the selected
/// tag, re-renders on every selection, and rebuilds its <c>MenuItems</c> array
/// fresh each render (the condition that made the old clear-and-rebuild update
/// path collapse expanded categories on every re-render).
///
/// PaneDisplayMode is pinned to <c>Left</c> so the pane stays open with text
/// labels (addressable by UIA Name) and children render inline when expanded,
/// regardless of the host window size.
/// </summary>
internal static class NavigationViewE2EFixtures
{
    internal class HierarchicalNavComponent : Component
    {
        public override Element Render()
        {
            var (selectedTag, setSelectedTag) = UseState("alpha-1");

            // Rebuilt fresh every render — exactly the pattern the Gallery uses
            // and the one that exposed the rebuild-clobber bug.
            var menuItems = new[]
            {
                NavItem("Alpha", tag: "alpha") with
                {
                    Children = new[]
                    {
                        NavItem("Alpha-1", tag: "alpha-1"),
                        NavItem("Alpha-2", tag: "alpha-2"),
                    }
                },
                NavItem("Bravo", tag: "bravo") with
                {
                    Children = new[]
                    {
                        NavItem("Bravo-1", tag: "bravo-1"),
                    }
                },
            };

            return NavigationView(
                menuItems,
                content: TextBlock($"Selected: {selectedTag}").AutomationId("NavSelectedTag")
            ) with
            {
                SelectedTag = selectedTag,
                PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
                IsSettingsVisible = false,
                OnSelectedTagChanged = tag =>
                {
                    if (tag != null) setSelectedTag(tag);
                },
            };
        }
    }

    internal static Element HierarchicalNav(RenderContext ctx) =>
        Component<HierarchicalNavComponent>();
}
