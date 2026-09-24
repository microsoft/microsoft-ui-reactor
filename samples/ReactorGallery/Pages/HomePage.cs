using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace WinUIGalleryReactor;

class HomePage : Component<Action<string>>
{
    public override Element Render()
    {
        var navigate = Props;

        var categoryControls = ControlRegistry.All
            .GroupBy(c => c.Category)
            .OrderBy(g => g.Key)
            .Select(g => new ControlInfo(
                g.Key,
                // "9 controls" is wrong for Fundamentals and Design, which hold framework
                // topics and guidance. One predicate, shared with the shell and the header.
                $"{g.Count()} {(ControlRegistry.IsControlCategory(g.Key) ? "controls" : "topics")}",
                g.Key,
                g.First().IconGlyph,
                g.Key.ToLowerInvariant().Replace(" ", "-"),
                g.First().ImageFile))
            .ToArray();

        var recentControls = ControlRegistry.All
            .Where(c => ControlRegistry.IsControlCategory(c.Category))
            .Take(8)
            .ToArray();

        return (ScrollViewer(
            VStack(0,
                // ── Hero section ────────────────────────────────────────
                Border(
                    VStack(12,
                        TextBlock("Reactor WinUI Gallery")
                            .ApplyStyle("TitleTextBlockStyle")
                            .Bold(),
                        TextBlock("A showcase of WinUI controls built entirely with Reactor — a declarative,\ncomponent-based UI framework for WinUI 3.")
                            .Foreground(Theme.SecondaryText)
                            .TextWrapping(TextWrapping.Wrap)
                            .MaxWidth(600)
                    )
                    .Margin(0, 0, 0, 36)
                    .HAlign(HorizontalAlignment.Left)
                ),

                // ── Category cards section ──────────────────────────────
                VStack(16,
                    TextBlock("Browse by Category")
                        .ApplyStyle("BodyStrongTextBlockStyle"),

                    GalleryControls.ControlCardGrid(categoryControls, navigate),

                    // Recently added section
                    TextBlock("Recently Added")
                        .ApplyStyle("BodyStrongTextBlockStyle"),

                    GalleryControls.ControlCardGrid(recentControls, navigate)
                )
            ).Margin(36,40,36,36)
        ) with
        {
            HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled,
            HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Disabled,
        });
    }
}
