using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Styles;

class SpacingPage : Component
{
    public override Element Render()
    {
        return PageContent("Spacing",
            "WinUI uses a 4px base grid and consistent spacing values to create hierarchy, group related content, and maintain visual rhythm. Reactor exposes Margin and Padding modifiers that map directly to WinUI layout properties.",

            SpacingScaleSection(),
            MarginVsPaddingSection(),
            MarginSection(),
            PaddingSection(),
            StackSpacingSection(),
            PageLayoutSection()
        );
    }

    // ── Section 1: Spacing scale ─────────────────────────────────────

    static Element SpacingScaleSection()
    {
        var steps = new (int Value, string Usage)[]
        {
            (0,  "Flush — no gap between elements."),
            (2,  "Hairline — tight spacing between related inline items."),
            (4,  "Compact — icon-to-label gaps and small control internals."),
            (8,  "Standard — default gap between sibling controls."),
            (12, "Relaxed — padding inside cards and grouped sections."),
            (16, "Spacious — between distinct content sections."),
            (24, "Section — major section boundaries and card padding."),
            (36, "Page — page-level margins around content."),
            (48, "Hero — large spacing for hero areas and visual breaks."),
        };

        return SampleCard("Spacing Scale",
            VStack(8,
                TextBlock("WinUI's spacing scale is built on a 4px base unit. Consistent use of these values creates a clear visual hierarchy.")
                    .Foreground(Theme.SecondaryText)
                    .FontSize(13)
                    .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                    .Margin(0, 0, 0, 4),

                VStack(4, steps.Select(s =>
                    HStack(12,
                        Border(VStack())
                            .Background(Theme.Accent)
                            .Width(s.Value == 0 ? 2 : s.Value)
                            .Height(20)
                            .CornerRadius(2)
                            .VAlign(VerticalAlignment.Center),
                        TextBlock($"{s.Value}px")
                            .FontSize(13).SemiBold()
                            .Foreground(Theme.PrimaryText)
                            .Width(40)
                            .VAlign(VerticalAlignment.Center),
                        TextBlock(s.Usage)
                            .FontSize(12)
                            .Foreground(Theme.SecondaryText)
                            .VAlign(VerticalAlignment.Center)
                    )
                ).ToArray())
            ),
            "// 4px base unit: 0, 2, 4, 8, 12, 16, 24, 36, 48");
    }

    // ── Section 2: Margin vs Padding ─────────────────────────────────

    static Element MarginVsPaddingSection() =>
        SampleCard("Margin vs Padding",
            VStack(16,
                TextBlock("Margin adds space outside an element; Padding adds space inside. In Reactor, .Padding() only works on Border and Control elements (Button, TextField, etc.). Layout panels like VStack and HStack only support .Margin() — wrap content in a Border if you need inner padding on a stack.")
                    .Foreground(Theme.SecondaryText)
                    .FontSize(13)
                    .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                    .Margin(0, 0, 0, 4),

                Grid(
                    columns: [GridSize.Star(), GridSize.Star()], rows: [GridSize.Auto],

                    // Margin demo
                    VStack(4,
                        TextBlock("Margin").SemiBold().Foreground(Theme.PrimaryText),
                        TextBlock("Space outside the border").FontSize(12).Foreground(Theme.SecondaryText),
                        Border(
                            Border(
                                TextBlock("Content").Foreground(Theme.PrimaryText)
                                    .HAlign(HorizontalAlignment.Center)
                                    .VAlign(VerticalAlignment.Center)
                            )
                            .Background(Theme.Accent)
                            .CornerRadius(4)
                            .Height(40)
                            .HAlign(HorizontalAlignment.Stretch)
                            .Margin(16) // ← margin shown
                        )
                        .Background(Theme.SubtleFill)
                        .WithBorder(Theme.SurfaceStroke, 2)
                        .Set(b =>
                        {
                            var border = (Microsoft.UI.Xaml.Controls.Border)b;
                            border.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                                global::Windows.UI.Color.FromArgb(128, 255, 150, 50));
                        })
                        .CornerRadius(6)
                        .Height(80)
                    ).Grid(column: 0).Margin(0, 0, 8, 0),

                    // Padding demo
                    VStack(4,
                        TextBlock("Padding").SemiBold().Foreground(Theme.PrimaryText),
                        TextBlock("Space inside the border").FontSize(12).Foreground(Theme.SecondaryText),
                        Border(
                            TextBlock("Content").Foreground(Theme.PrimaryText)
                                .HAlign(HorizontalAlignment.Center)
                                .VAlign(VerticalAlignment.Center)
                        )
                        .Background(Theme.Accent)
                        .WithBorder(Theme.SurfaceStroke, 2)
                        .Set(b =>
                        {
                            var border = (Microsoft.UI.Xaml.Controls.Border)b;
                            border.BorderBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                                global::Windows.UI.Color.FromArgb(128, 100, 180, 255));
                        })
                        .Padding(16) // ← padding shown
                        .CornerRadius(6)
                        .Height(80)
                    ).Grid(column: 1)
                ),

                // Compatibility table
                TextBlock("Compatibility").SemiBold().Foreground(Theme.PrimaryText),
                VStack(2,
                    CompatRow("Border",    true,  true),
                    CompatRow("Button",    true,  true),
                    CompatRow("TextField", true,  true),
                    CompatRow("Text",      true,  false),
                    CompatRow("VStack",    true,  false),
                    CompatRow("HStack",    true,  false),
                    CompatRow("Grid",      true,  false),
                    CompatRow("Image",     true,  false)
                )
            ),
            @"// Margin — works on ALL elements
TextBlock(""Hello"").Margin(8)
VStack(children).Margin(16)
Border(child).Margin(12)

// Padding — only on Border and Control (Button, TextField, etc.)
Border(child).Padding(16)    // ✓ works
Button(""Go"").Padding(12)    // ✓ works

// VStack/HStack don't support Padding — wrap in Border instead:
Border(
    VStack(8, items)
).Padding(16)  // ✓ padding applied to the Border");

    static Element CompatRow(string element, bool margin, bool padding) =>
        Grid(
            columns: [GridSize.Px(120), GridSize.Px(80), GridSize.Px(80)], rows: [GridSize.Auto],
            TextBlock(element).FontSize(13).Foreground(Theme.PrimaryText)
                .Set(tb => tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas, monospace"))
                .VAlign(VerticalAlignment.Center)
                .Grid(column: 0),
            TextBlock(margin ? "✓ Margin" : "✗")
                .FontSize(12)
                .Foreground(margin ? Theme.SystemSuccess : Theme.DisabledText)
                .VAlign(VerticalAlignment.Center)
                .Grid(column: 1),
            TextBlock(padding ? "✓ Padding" : "—")
                .FontSize(12)
                .Foreground(padding ? Theme.SystemSuccess : Theme.TertiaryText)
                .VAlign(VerticalAlignment.Center)
                .Grid(column: 2)
        ).Margin(0, 2, 0, 2);

    // ── Section 3: Margin overloads ──────────────────────────────────

    static Element MarginSection() =>
        SampleCard("Margin",
            VStack(12,
                TextBlock("Margin pushes an element away from its neighbors. It works on every Reactor element.")
                    .Foreground(Theme.SecondaryText)
                    .FontSize(13)
                    .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                    .Margin(0, 0, 0, 4),

                MarginDemo("Uniform (16)", 16, 16, 16, 16),
                MarginDemo("Horizontal / Vertical (24, 8)", 24, 8, 24, 8),
                MarginDemo("Per-side (4, 8, 16, 24)", 4, 8, 16, 24)
            ),
            @"// Uniform — same on all sides
element.Margin(16)

// Horizontal, Vertical
element.Margin(horizontal: 24, vertical: 8)

// Per-side: left, top, right, bottom
element.Margin(4, 8, 16, 24)");

    static Element MarginDemo(string label, double l, double t, double r, double b) =>
        VStack(2,
            TextBlock(label).FontSize(12).Foreground(Theme.SecondaryText),
            Border(
                Border(
                    TextBlock($"{l}, {t}, {r}, {b}")
                        .FontSize(11)
                        .Foreground(Theme.PrimaryText)
                        .HAlign(HorizontalAlignment.Center)
                        .VAlign(VerticalAlignment.Center)
                )
                .Background(Theme.Accent)
                .CornerRadius(4)
                .HAlign(HorizontalAlignment.Stretch)
                .VAlign(VerticalAlignment.Stretch)
                .Margin(l, t, r, b)
            )
            .Background(Theme.SubtleFill)
            .WithBorder(Theme.DividerStroke)
            .CornerRadius(6)
            .Height(64)
            .Width(240)
        );

    // ── Section 4: Padding overloads ─────────────────────────────────

    static Element PaddingSection() =>
        SampleCard("Padding",
            VStack(12,
                TextBlock("Padding adds inner space between a container's edge and its content. In Reactor it applies to Border and Control-based elements only.")
                    .Foreground(Theme.SecondaryText)
                    .FontSize(13)
                    .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                    .Margin(0, 0, 0, 4),

                PaddingDemo("Uniform (16)", 16, 16, 16, 16),
                PaddingDemo("Horizontal / Vertical (24, 8)", 24, 8, 24, 8),
                PaddingDemo("Per-side (4, 8, 16, 24)", 4, 8, 16, 24)
            ),
            @"// Uniform — same on all sides
Border(child).Padding(16)

// Horizontal, Vertical
Border(child).Padding(horizontal: 24, vertical: 8)

// Per-side: left, top, right, bottom
Border(child).Padding(4, 8, 16, 24)");

    static Element PaddingDemo(string label, double l, double t, double r, double b) =>
        VStack(2,
            TextBlock(label).FontSize(12).Foreground(Theme.SecondaryText),
            Border(
                TextBlock($"{l}, {t}, {r}, {b}")
                    .FontSize(11)
                    .Foreground(Theme.PrimaryText)
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Center)
                    .Background(Theme.Accent)
            )
            .Background(Theme.SubtleFill)
            .WithBorder(Theme.DividerStroke)
            .CornerRadius(6)
            .Padding(l, t, r, b)
            .Width(240)
        );

    // ── Section 5: Stack spacing ─────────────────────────────────────

    static Element StackSpacingSection() =>
        SampleCard("Stack Spacing",
            VStack(16,
                TextBlock("VStack and HStack accept a spacing parameter that inserts uniform gaps between children. This is the recommended way to space siblings — use it instead of adding margins to individual items.")
                    .Foreground(Theme.SecondaryText)
                    .FontSize(13)
                    .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                    .Margin(0, 0, 0, 4),

                TextBlock("VStack spacing").SemiBold().FontSize(13).Foreground(Theme.PrimaryText),
                HStack(24,
                    StackDemo("0", 0, true),
                    StackDemo("4", 4, true),
                    StackDemo("8", 8, true),
                    StackDemo("16", 16, true)
                ),

                TextBlock("HStack spacing").SemiBold().FontSize(13).Foreground(Theme.PrimaryText),
                VStack(8,
                    StackDemo("0", 0, false),
                    StackDemo("4", 4, false),
                    StackDemo("8", 8, false),
                    StackDemo("16", 16, false)
                )
            ),
            @"// VStack with spacing between children
VStack(8,
    TextBlock(""Item 1""),
    TextBlock(""Item 2""),
    TextBlock(""Item 3"")
)

// HStack with spacing between children
HStack(12,
    Button(""A""),
    Button(""B""),
    Button(""C"")
)");

    static Element StackDemo(string label, int spacing, bool vertical)
    {
        Element[] items =
        [
            Border(VStack()).Width(vertical ? 80 : 28).Height(vertical ? 18 : 28)
                .Background(Theme.Accent).CornerRadius(3),
            Border(VStack()).Width(vertical ? 80 : 28).Height(vertical ? 18 : 28)
                .Background(Theme.AccentSecondary).CornerRadius(3),
            Border(VStack()).Width(vertical ? 80 : 28).Height(vertical ? 18 : 28)
                .Background(Theme.AccentTertiary).CornerRadius(3),
        ];

        var stack = vertical
            ? (Element)VStack(spacing, items)
            : HStack(spacing, items);

        return VStack(2,
            TextBlock($"{label}px").FontSize(11).Foreground(Theme.SecondaryText),
            Border(stack)
                .Background(Theme.SubtleFill)
                .WithBorder(Theme.DividerStroke)
                .CornerRadius(6)
                .Padding(8)
        );
    }

    // ── Section 6: Practical page layout ─────────────────────────────

    static Element PageLayoutSection() =>
        SampleCard("Page Layout Spacing",
            VStack(12,
                TextBlock("A typical page combines all spacing techniques: page-level margins, section spacing in stacks, card padding via Border, and control gaps. Here is a common pattern:")
                    .Foreground(Theme.SecondaryText)
                    .FontSize(13)
                    .Set(tb => tb.TextWrapping = TextWrapping.Wrap)
                    .Margin(0, 0, 0, 4),

                // Mock page layout
                Border(
                    VStack(16,
                        // Header
                        VStack(4,
                            TextBlock("Page Title").FontSize(18).SemiBold().Foreground(Theme.PrimaryText),
                            TextBlock("A brief description of the page content.")
                                .FontSize(13).Foreground(Theme.SecondaryText)
                        ),

                        // Card
                        Border(
                            VStack(8,
                                TextBlock("Section Card").SemiBold().Foreground(Theme.PrimaryText),
                                TextBlock("Content inside a card uses 16px padding via the Border wrapper.")
                                    .FontSize(13).Foreground(Theme.SecondaryText)
                                    .Set(tb => tb.TextWrapping = TextWrapping.Wrap),
                                HStack(8,
                                    Button("Primary", () => { }).Background(Theme.Accent),
                                    Button("Secondary", () => { })
                                )
                            )
                        )
                        .Background(Theme.CardBackground)
                        .WithBorder(Theme.CardStroke)
                        .CornerRadius(8)
                        .Padding(16)
                    )
                )
                .Background(Theme.SolidBackground)
                .WithBorder(Theme.SurfaceStroke)
                .CornerRadius(8)
                .Padding(36, 24, 36, 24) // page margins
                .Width(420)
            ),
            @"// Typical page structure
VStack(16,                          // 16px between sections
    VStack(4,                       // 4px title-to-subtitle gap
        TextBlock(""Page Title""),
        TextBlock(""Description"")
    ),

    Border(                         // Card with inner padding
        VStack(8,                   // 8px between card children
            TextBlock(""Section""),
            TextBlock(""Content""),
            HStack(8,               // 8px between buttons
                Button(""Primary""),
                Button(""Secondary"")
            )
        )
    )
    .Background(Theme.CardBackground)
    .WithBorder(Theme.CardStroke)
    .Padding(16)                    // 16px card padding
)
.Margin(36, 24, 36, 36)            // page-level margins");
}
