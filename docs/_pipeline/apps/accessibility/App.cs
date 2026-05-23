using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;

ReactorApp.Run<AccessibilityApp>("Accessibility", width: 650, height: 700
#if DEBUG
    , preview: true
#endif
);

// <snippet:tier1-modifiers>
class Tier1Demo : Component
{
    public override Element Render()
    {
        return VStack(12,
            TextBlock("Account Settings")
                .FontSize(24).Bold()
                .HeadingLevel(AutomationHeadingLevel.Level1),
            TextBlock("Profile")
                .FontSize(18).SemiBold()
                .HeadingLevel(AutomationHeadingLevel.Level2),
            TextBox("", _ => { }, placeholder: "Display name")
                .AutomationName("Display name")
                .TabIndex(1)
                .AccessKey("N"),
            Button("Save", () => { })
                .AutomationName("Save profile changes")
                .TabIndex(2)
                .AccessKey("S")
        ).Padding(24);
    }
}
// </snippet:tier1-modifiers>

// <snippet:tier2-modifiers>
class Tier2Demo : Component
{
    public override Element Render()
    {
        return VStack(12,
            TextBox("", _ => { }, placeholder: "Search...")
                .AutomationName("Search products")
                .HelpText("Type a product name or SKU to filter results")
                .Width(300),
            VStack(8,
                TextBlock("Revenue by Region").Bold(),
                TextBlock("Bar chart placeholder").Opacity(0.5)
            ).FullDescription(
                "Bar chart showing Q1 revenue: East $4.2M, " +
                "West $3.8M, Central $2.1M")
             .Padding(16).Background("#f5f5f5").CornerRadius(8),
            TextBlock("Decorative divider")
                .Opacity(0.2)
                .AccessibilityHidden()
        ).Padding(24);
    }
}
// </snippet:tier2-modifiers>

// <snippet:accessible-form>
class AccessibleFormDemo : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");
        var (email, setEmail) = UseState("");
        var (agree, setAgree) = UseState(false);
        var valid = !string.IsNullOrWhiteSpace(name)
            && email.Contains('@') && agree;

        return VStack(12,
            TextBlock("Create Account").FontSize(24).Bold()
                .HeadingLevel(AutomationHeadingLevel.Level1),
            TextBox(name, setName, header: "Full Name")
                .AutomationName("Full name").Required().TabIndex(1),
            TextBox(email, setEmail, header: "Email")
                .AutomationName("Email address").Required().TabIndex(2)
                .HelpText("We'll send a verification link"),
            CheckBox(agree, setAgree, label: "I accept the terms")
                .TabIndex(3),
            Button("Register", () => { })
                .IsEnabled(valid).TabIndex(4).AccessKey("R")
        ).Landmark(AutomationLandmarkType.Form).Padding(24);
    }
}
// </snippet:accessible-form>

// <snippet:landmarks>
class LandmarksDemo : Component
{
    public override Element Render()
    {
        return VStack(16,
            HStack(8,
                Button("Home", () => { }),
                Button("Products", () => { }),
                Button("About", () => { })
            ).Landmark(AutomationLandmarkType.Navigation)
             .AutomationName("Main navigation"),

            VStack(12,
                TextBlock("Dashboard")
                    .FontSize(20).Bold()
                    .HeadingLevel(AutomationHeadingLevel.Level1),
                TextBlock("Welcome back. Here is your overview.")
            ).Landmark(AutomationLandmarkType.Main)
             .AutomationName("Main content"),

            TextBox("", _ => { }, placeholder: "Search...")
                .AutomationName("Site search")
                .Landmark(AutomationLandmarkType.Search)
        ).Padding(24);
    }
}
// </snippet:landmarks>

// <snippet:heading-hierarchy>
class HeadingHierarchyDemo : Component
{
    public override Element Render()
    {
        return VStack(12,
            TextBlock("Application Settings")
                .FontSize(24).Bold()
                .HeadingLevel(AutomationHeadingLevel.Level1),
            TextBlock("Appearance")
                .FontSize(18).SemiBold()
                .HeadingLevel(AutomationHeadingLevel.Level2),
            TextBlock("Choose your preferred theme and font size."),
            TextBlock("Notifications")
                .FontSize(18).SemiBold()
                .HeadingLevel(AutomationHeadingLevel.Level2),
            TextBlock("Email Alerts")
                .FontSize(15).SemiBold()
                .HeadingLevel(AutomationHeadingLevel.Level3),
            TextBlock("Configure which emails you receive.")
        ).Padding(24);
    }
}
// </snippet:heading-hierarchy>

// <snippet:focus-trap>
class FocusTrapDemo : Component
{
    public override Element Render()
    {
        var (showModal, setShowModal) = UseState(false);
        var trap = this.UseFocusTrap(showModal);

        return VStack(12,
            SubHeading("Focus Trapping"),
            Button("Open Modal", () => setShowModal(true)),
            When(showModal, () =>
                Border(
                    VStack(12,
                        TextBlock("Modal Dialog").FontSize(18).Bold(),
                        TextBlock("Tab/Shift+Tab stays inside this panel."),
                        TextBox("", _ => { }, placeholder: "Name")
                            .TabIndex(0),
                        Button("Close", () => setShowModal(false))
                            .TabIndex(1)
                    ).Padding(24)
                ).WithBorder("#888", 1)
                 .CornerRadius(8)
                 .Background("#ffffff")
                 .FocusTrap(trap)
            )
        ).Padding(24);
    }
}
// </snippet:focus-trap>

// <snippet:announcements>
class AnnouncementsDemo : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var announce = this.UseAnnounce();

        return VStack(12,
            SubHeading("Screen Reader Announcements"),
            Button("Save", () =>
            {
                setCount(count + 1);
                announce.Announce($"Document saved ({count + 1} times)");
            }),
            Button("Error (Assertive)", () =>
                announce.Announce("Connection lost!", assertive: true)),
            TextBlock($"Saves: {count}").Opacity(0.6),
            announce.Region  // invisible live region — must be in tree
        ).Padding(24);
    }
}
// </snippet:announcements>

// <snippet:semantic-panel>
class SemanticPanelDemo : Component
{
    public override Element Render()
    {
        var (rating, setRating) = UseState(3);

        // .Semantics() wraps the element in a SemanticPanel so
        // screen readers announce it as a slider, not raw buttons
        return VStack(12,
            SubHeading("Star Rating (Semantic Panel)"),
            HStack(4, Enumerable.Range(1, 5).Select(i =>
                Button(i <= rating ? "\u2605" : "\u2606",
                    () => setRating(i))
                    .AutomationName($"{i} star{(i == 1 ? "" : "s")}")
            ).ToArray())
            .Semantics(
                role: "slider",
                value: $"{rating} of 5 stars",
                rangeMin: 1, rangeMax: 5, rangeValue: rating),
            TextBlock($"Current: {rating}/5").Opacity(0.6)
        ).Padding(24);
    }
}
// </snippet:semantic-panel>

// <snippet:scanner>
// Run AccessibilityScanner during development or CI:
//
// var diagnostics = AccessibilityScanner.Scan(rootElement);
// foreach (var d in diagnostics)
// {
//     Console.WriteLine($"[{d.Severity}] {d.Message}");
//     Console.WriteLine($"  WCAG: {d.WcagCriterion}");
//     Console.WriteLine($"  Fix: {d.Fix?.Modifier}({d.Fix?.SuggestedValue})");
// }
//
// Export structured JSON for CI integration:
// AccessibilityScanner.ExportJson(diagnostics, "a11y-report.json");
// </snippet:scanner>

// Main app
class AccessibilityApp : Component
{
    public override Element Render()
    {
        return ScrollView(
            VStack(24,
                Heading("Accessibility"),
                Component<Tier1Demo>(),
                Component<Tier2Demo>(),
                Component<AccessibleFormDemo>(),
                Component<LandmarksDemo>(),
                Component<HeadingHierarchyDemo>()
            ).Padding(24)
        );
    }
}
