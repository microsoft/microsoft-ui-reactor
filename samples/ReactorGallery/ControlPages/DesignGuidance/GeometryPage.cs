using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Styles;

class GeometryPage : Component
{
    record RadiusEntry(string Name, string ResourceKey, double Value, string Recommendation);

    public override Element Render()
    {
        // Resolve the WinUI corner radius resources at render time.
        var controlRadius = ThemeResource.CornerRadius("ControlCornerRadius");
        var overlayRadius = ThemeResource.CornerRadius("OverlayCornerRadius");

        var entries = new RadiusEntry[]
        {
            new("Control", "ControlCornerRadius", controlRadius.TopLeft,
                "Standard interactive controls — buttons, text fields, combo boxes, sliders, and toggles."),
            new("Overlay", "OverlayCornerRadius", overlayRadius.TopLeft,
                "Overlay surfaces that sit above the app — dialogs, flyouts, teaching tips, and menus."),
        };

        return PageContent("Geometry",
            "WinUI defines shared corner radius resources so controls and surfaces have a consistent shape language. Use ControlCornerRadius for interactive controls and OverlayCornerRadius for overlay surfaces like dialogs and flyouts.",

            CornerRadiusResourcesSection(entries),
            ControlCornerRadiusSection(controlRadius),
            OverlayCornerRadiusSection(overlayRadius),
            MixedRadiiSection(controlRadius, overlayRadius)
        );
    }

    // ── Section 1: Resource reference table ──────────────────────────

    static Element CornerRadiusResourcesSection(RadiusEntry[] entries) =>
        SampleCard("Corner Radius Resources",
            VStack(12,
                TextBlock("WinUI provides two corner radius theme resources. Using these instead of hard-coded values ensures your UI stays consistent with the system design language and adapts if the values are customized.")
                    .Foreground(Theme.SecondaryText)
                    .FontSize(13)
                    .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                    .Margin(0, 0, 0, 4),

                VStack(8, entries.Select(RadiusRow).ToArray())
            ),
            @"// Resolve at render time
var controlRadius = ThemeResource.CornerRadius(""ControlCornerRadius"");
var overlayRadius = ThemeResource.CornerRadius(""OverlayCornerRadius"");

// Apply to elements
Border(content).CornerRadius(controlRadius.TopLeft)
Border(dialog).CornerRadius(overlayRadius.TopLeft)");

    static Element RadiusRow(RadiusEntry entry) =>
        Border(
            HStack(16,
                // Visual swatch showing the radius
                Border(VStack())
                    .Width(48).Height(48)
                    .Background(Theme.Accent)
                    .CornerRadius(entry.Value)
                    .VAlign(VerticalAlignment.Center),

                VStack(2,
                    HStack(8,
                        TextBlock(entry.Name)
                            .SemiBold()
                            .FontSize(14)
                            .Foreground(Theme.PrimaryText),
                        TextBlock($"{entry.Value}px")
                            .FontSize(13)
                            .Foreground(Theme.AccentText)
                    ),
                    TextBlock(entry.ResourceKey)
                        .FontSize(11)
                        .Foreground(Theme.TertiaryText)
                        .Set(tb => tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas, monospace")),
                    TextBlock(entry.Recommendation)
                        .FontSize(12)
                        .Foreground(Theme.SecondaryText)
                        .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                ).VAlign(VerticalAlignment.Center)
            ).Margin(12)
        )
        .Background(Theme.CardBackground)
        .WithBorder(Theme.CardStroke)
        .CornerRadius(entry.Value);

    // ── Section 2: ControlCornerRadius applied samples ───────────────

    static Element ControlCornerRadiusSection(CornerRadius cr) =>
        SampleCard("ControlCornerRadius",
            VStack(16,
                TextBlock("ControlCornerRadius is used by interactive controls to maintain a consistent, compact rounding. Apply it to buttons, inputs, cards, and other standard controls.")
                    .Foreground(Theme.SecondaryText)
                    .FontSize(13)
                    .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                    .Margin(0, 0, 0, 4),

                // Buttons
                TextBlock("Buttons").SemiBold().Foreground(Theme.PrimaryText),
                HStack(12,
                    Button("Standard", () => { })
                        .CornerRadius(cr.TopLeft),
                    Button("Accent", () => { })
                        .Background(Theme.Accent)
                        .CornerRadius(cr.TopLeft),
                    Button("Outline", () => { })
                        .WithBorder(Theme.ControlStroke)
                        .CornerRadius(cr.TopLeft)
                ),

                // Text inputs
                TextBlock("Text Input").SemiBold().Foreground(Theme.PrimaryText),
                TextBox("", placeholder: "Type here...")
                    .Width(280)
                    .CornerRadius(cr.TopLeft),

                // Card
                TextBlock("Card Surface").SemiBold().Foreground(Theme.PrimaryText),
                Border(
                    VStack(4,
                        TextBlock("Card Title").SemiBold().Foreground(Theme.PrimaryText),
                        TextBlock("Card content using ControlCornerRadius for its border rounding.")
                            .FontSize(13)
                            .Foreground(Theme.SecondaryText)
                            .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                    ).Margin(16)
                )
                .Background(Theme.CardBackground)
                .WithBorder(Theme.CardStroke)
                .CornerRadius(cr.TopLeft)
                .Width(320),

                // Chip / tag
                TextBlock("Tags & Badges").SemiBold().Foreground(Theme.PrimaryText),
                HStack(8,
                    Border(TextBlock("Info").FontSize(12).Foreground(Theme.PrimaryText).Margin(6, 2, 6, 2))
                        .Background(Theme.SubtleFill)
                        .WithBorder(Theme.ControlStroke)
                        .CornerRadius(cr.TopLeft),
                    Border(TextBlock("Success").FontSize(12).Foreground(Theme.PrimaryText).Margin(6, 2, 6, 2))
                        .Background(Theme.SystemSuccessBackground)
                        .WithBorder(Theme.ControlStroke)
                        .CornerRadius(cr.TopLeft),
                    Border(TextBlock("Warning").FontSize(12).Foreground(Theme.PrimaryText).Margin(6, 2, 6, 2))
                        .Background(Theme.SystemCautionBackground)
                        .WithBorder(Theme.ControlStroke)
                        .CornerRadius(cr.TopLeft)
                )
            ),
            @"var cr = ThemeResource.CornerRadius(""ControlCornerRadius"");

// Buttons
Button(""Standard"", () => { }).CornerRadius(cr.TopLeft)

// Text input
TextBox(""Placeholder"", value, onChange).CornerRadius(cr.TopLeft)

// Card
Border(content)
    .Background(Theme.CardBackground)
    .WithBorder(Theme.CardStroke)
    .CornerRadius(cr.TopLeft)

// Tags
Border(TextBlock(""Tag"").Padding(6, 2, 6, 2))
    .Background(Theme.SubtleFill)
    .WithBorder(Theme.ControlStroke)
    .CornerRadius(cr.TopLeft)");

    // ── Section 3: OverlayCornerRadius applied samples ───────────────

    static Element OverlayCornerRadiusSection(CornerRadius or) =>
        SampleCard("OverlayCornerRadius",
            VStack(16,
                TextBlock("OverlayCornerRadius is larger than ControlCornerRadius and is used for surfaces that float above the app layer — dialogs, flyouts, menus, teaching tips, and modal overlays.")
                    .Foreground(Theme.SecondaryText)
                    .FontSize(13)
                    .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                    .Margin(0, 0, 0, 4),

                // Dialog mock
                TextBlock("Dialog").SemiBold().Foreground(Theme.PrimaryText),
                Border(
                    VStack(12,
                        TextBlock("Delete this item?")
                            .FontSize(16).SemiBold().Foreground(Theme.PrimaryText),
                        TextBlock("This action cannot be undone. The item will be permanently removed.")
                            .FontSize(13)
                            .Foreground(Theme.SecondaryText)
                            .Set(tb => tb.TextWrapping = TextWrapping.Wrap),
                        HStack(8,
                            Button("Delete", () => { })
                                .Background(Theme.SystemCritical),
                            Button("Cancel", () => { })
                        ).HAlign(HorizontalAlignment.Right)
                    ).Margin(24)
                )
                .Background(Theme.LayerFill)
                .WithBorder(Theme.SurfaceStroke)
                .CornerRadius(or.TopLeft)
                .Width(400),

                // Flyout mock
                TextBlock("Flyout / Menu").SemiBold().Foreground(Theme.PrimaryText),
                Border(
                    VStack(2,
                        FlyoutMenuItem("Cut", "\uE8C6"),
                        FlyoutMenuItem("Copy", "\uE8C8"),
                        FlyoutMenuItem("Paste", "\uE77F"),
                        Border(VStack()).Height(1)
                            .Background(Theme.DividerStroke)
                            .HAlign(HorizontalAlignment.Stretch)
                            .Margin(0, 4, 0, 4),
                        FlyoutMenuItem("Select All", "\uE8B3")
                    ).Margin(4)
                )
                .Background(Theme.LayerFill)
                .WithBorder(Theme.SurfaceStroke)
                .CornerRadius(or.TopLeft)
                .Width(200),

                // Teaching tip mock
                TextBlock("Teaching Tip").SemiBold().Foreground(Theme.PrimaryText),
                Border(
                    VStack(8,
                        TextBlock("Did you know?")
                            .FontSize(14).SemiBold().Foreground(Theme.PrimaryText),
                        TextBlock("You can use OverlayCornerRadius to round any floating surface so it matches the system style for popups and overlays.")
                            .FontSize(13)
                            .Foreground(Theme.SecondaryText)
                            .Set(tb => tb.TextWrapping = TextWrapping.Wrap),
                        TextBlock("Got it").Foreground(Theme.AccentText).FontSize(13)
                    ).Margin(20)
                )
                .Background(Theme.LayerFill)
                .WithBorder(Theme.SurfaceStroke)
                .CornerRadius(or.TopLeft)
                .Width(320)
            ),
            @"var or = ThemeResource.CornerRadius(""OverlayCornerRadius"");

// Dialog
Border(dialogContent)
    .Background(Theme.LayerFill)
    .WithBorder(Theme.SurfaceStroke)
    .CornerRadius(or.TopLeft)

// Flyout / Menu
Border(menuItems)
    .Background(Theme.LayerFill)
    .WithBorder(Theme.SurfaceStroke)
    .CornerRadius(or.TopLeft)

// Teaching Tip
Border(tipContent)
    .Background(Theme.LayerFill)
    .WithBorder(Theme.SurfaceStroke)
    .CornerRadius(or.TopLeft)");

    // ── Section 4: Side-by-side comparison ───────────────────────────

    static Element MixedRadiiSection(CornerRadius cr, CornerRadius or) =>
        SampleCard("Comparing Radii",
            VStack(12,
                TextBlock("Nesting controls inside overlay surfaces is a common pattern. The outer container uses OverlayCornerRadius, while the inner controls use ControlCornerRadius.")
                    .Foreground(Theme.SecondaryText)
                    .FontSize(13)
                    .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                    .Margin(0, 0, 0, 4),

                // A dialog-like container with inner controls
                Border(
                    VStack(12,
                        TextBlock("Sign In")
                            .FontSize(16).SemiBold().Foreground(Theme.PrimaryText),
                        TextBox("", placeholder: "Username")
                            .CornerRadius(cr.TopLeft),
                        TextBox("", placeholder: "Password")
                            .CornerRadius(cr.TopLeft),
                        Button("Sign In", () => { })
                            .Background(Theme.Accent)
                            .CornerRadius(cr.TopLeft)
                            .HAlign(HorizontalAlignment.Stretch)
                    ).Margin(24)
                )
                .Background(Theme.LayerFill)
                .WithBorder(Theme.SurfaceStroke)
                .CornerRadius(or.TopLeft)
                .Width(320),

                HStack(24,
                    VStack(4,
                        Border(VStack()).Width(40).Height(40)
                            .Background(Theme.Accent).CornerRadius(cr.TopLeft),
                        TextBlock($"Control\n{cr.TopLeft}px")
                            .FontSize(11).Foreground(Theme.SecondaryText)
                            .HAlign(HorizontalAlignment.Center)
                    ),
                    VStack(4,
                        Border(VStack()).Width(40).Height(40)
                            .Background(Theme.Accent).CornerRadius(or.TopLeft),
                        TextBlock($"Overlay\n{or.TopLeft}px")
                            .FontSize(11).Foreground(Theme.SecondaryText)
                            .HAlign(HorizontalAlignment.Center)
                    )
                )
            ),
            @"var cr = ThemeResource.CornerRadius(""ControlCornerRadius"");
var or = ThemeResource.CornerRadius(""OverlayCornerRadius"");

// Overlay container with control-radius inner elements
Border(
    VStack(12,
        TextBox(""Username"", value, onChange).CornerRadius(cr.TopLeft),
        Button(""Sign In"", onClick)
            .Background(Theme.Accent)
            .CornerRadius(cr.TopLeft)
    ).Margin(24)
)
.Background(Theme.LayerFill)
.WithBorder(Theme.SurfaceStroke)
.CornerRadius(or.TopLeft)  // outer overlay radius");

    // ── Helper ───────────────────────────────────────────────────────

    static Element FlyoutMenuItem(string label, string glyph) =>
        Border(
            HStack(10,
                TextBlock(glyph)
                    .FontSize(14)
                    .Set(t => t.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe MDL2 Assets"))
                    .Foreground(Theme.PrimaryText)
                    .VAlign(VerticalAlignment.Center),
                TextBlock(label)
                    .FontSize(13)
                    .Foreground(Theme.PrimaryText)
                    .VAlign(VerticalAlignment.Center)
            ).Margin(8, 6, 8, 6)
        )
        .CornerRadius(4)
        .HAlign(HorizontalAlignment.Stretch);
}
