using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

/// <summary>
/// Test fixtures for accessibility interaction patterns:
/// keyboard navigation, focus trapping, live region announcements,
/// and heading hierarchy validation.
/// </summary>
internal static class AccessibilityInteractionFixtures
{
    // ════════════════════════════════════════════════════════════════
    //  Keyboard Navigation — Tab order follows TabIndex
    // ════════════════════════════════════════════════════════════════

    internal static Element KeyboardNavTest(RenderContext ctx)
    {
        return VStack(12,
            TextBlock("Keyboard Navigation Test")
                .HeadingLevel(AutomationHeadingLevel.Level1)
                .AutomationId("A11yNav_Title"),

            TextBox("", _ => { }, placeholder: "First")
                .AutomationName("First field")
                .TabIndex(1)
                .AutomationId("A11yNav_Field1"),

            TextBox("", _ => { }, placeholder: "Second")
                .AutomationName("Second field")
                .TabIndex(2)
                .AutomationId("A11yNav_Field2"),

            TextBox("", _ => { }, placeholder: "Third")
                .AutomationName("Third field")
                .TabIndex(3)
                .AutomationId("A11yNav_Field3"),

            TextBox("", _ => { }, placeholder: "Fourth")
                .AutomationName("Fourth field")
                .TabIndex(4)
                .AutomationId("A11yNav_Field4"),

            Button("Submit", () => { })
                .TabIndex(5)
                .AutomationId("A11yNav_Submit")
        ).Landmark(AutomationLandmarkType.Main)
         .Padding(24);
    }

    // ════════════════════════════════════════════════════════════════
    //  Live Region — static property validation
    // ════════════════════════════════════════════════════════════════

    internal static Element LiveRegionStaticTest(RenderContext ctx)
    {
        // Stateless fixture — validates that LiveRegion UIA properties are set.
        return VStack(12,
            TextBlock("Live Region Test")
                .HeadingLevel(AutomationHeadingLevel.Level1)
                .AutomationId("A11yLive_Title"),

            TextBlock("Status: 3 items deleted")
                .LiveRegion(AutomationLiveSetting.Polite)
                .AutomationId("A11yLive_Status"),

            TextBlock("Alert: Connection lost!")
                .LiveRegion(AutomationLiveSetting.Assertive)
                .AutomationId("A11yLive_Alert")
        ).Landmark(AutomationLandmarkType.Main)
         .Padding(24);
    }

    // ════════════════════════════════════════════════════════════════
    //  UseAnnounce — stateful live region with changing text
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Component class for UseAnnounce E2E testing. Uses Component base class
    /// (not function component) because the test host fixture system requires
    /// Component classes for stateful rendering.
    /// </summary>
    internal class UseAnnounceTestComponent : Component
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            var announce = UseAnnounce();

            return VStack(12,
                TextBlock("UseAnnounce Test")
                    .HeadingLevel(AutomationHeadingLevel.Level1)
                    .AutomationId("A11yAnnounce_Title"),

                // The hidden live-region anchor required by UseAnnounce
                announce.Region,

                Button("Delete 3 items", () =>
                {
                    var next = count + 3;
                    setCount(next);
                    announce.Announce($"{next} items deleted");
                }).AutomationId("A11yAnnounce_DeleteBtn"),

                Button("Reset", () =>
                {
                    setCount(0);
                    announce.Announce("Counter reset");
                }).AutomationId("A11yAnnounce_ResetBtn"),

                // Visible live region — text changes trigger screen reader announcements
                TextBlock($"Deleted: {count} items")
                    .LiveRegion(AutomationLiveSetting.Polite)
                    .AutomationId("A11yAnnounce_StatusText"),

                TextBlock(count > 0 ? $"Last announcement: {count} items deleted" : "No announcements yet")
                    .AutomationId("A11yAnnounce_LastAction")
            ).Landmark(AutomationLandmarkType.Main)
             .Padding(24);
        }
    }

    internal static Element UseAnnounceTest(RenderContext ctx) =>
        Component<UseAnnounceTestComponent>();

    // ════════════════════════════════════════════════════════════════
    //  Heading Hierarchy Validation
    // ════════════════════════════════════════════════════════════════

    internal static Element HeadingHierarchyTest(RenderContext ctx)
    {
        return VStack(12,
            TextBlock("Application Settings")
                .FontSize(24).Bold()
                .HeadingLevel(AutomationHeadingLevel.Level1)
                .AutomationId("A11yH_H1"),

            TextBlock("Appearance")
                .FontSize(18).SemiBold()
                .HeadingLevel(AutomationHeadingLevel.Level2)
                .AutomationId("A11yH_H2a"),

            TextBlock("Choose your preferred theme."),

            TextBlock("Notifications")
                .FontSize(18).SemiBold()
                .HeadingLevel(AutomationHeadingLevel.Level2)
                .AutomationId("A11yH_H2b"),

            TextBlock("Email Alerts")
                .FontSize(15).SemiBold()
                .HeadingLevel(AutomationHeadingLevel.Level3)
                .AutomationId("A11yH_H3"),

            TextBlock("Configure which emails you receive.")
        ).Landmark(AutomationLandmarkType.Main)
         .Padding(24);
    }

    // ════════════════════════════════════════════════════════════════
    //  Access Key Activation
    // ════════════════════════════════════════════════════════════════

    internal static Element AccessKeyTest(RenderContext ctx)
    {
        return VStack(12,
            TextBlock("Access Key Test")
                .HeadingLevel(AutomationHeadingLevel.Level1)
                .AutomationId("A11yAK_Title"),

            Button("Save", () => { })
                .AccessKey("S")
                .AutomationId("A11yAK_SaveBtn"),

            Button("Cancel", () => { })
                .AccessKey("C")
                .AutomationId("A11yAK_CancelBtn"),

            TextBlock("Ready")
                .AutomationId("A11yAK_Status")
        ).Landmark(AutomationLandmarkType.Main)
         .Padding(24);
    }

    // ════════════════════════════════════════════════════════════════
    //  SemanticPanel — composite component semantics via UIA
    // ════════════════════════════════════════════════════════════════

    internal static Element SemanticPanelTest(RenderContext ctx)
    {
        return VStack(12,
            TextBlock("Semantic Panel Test")
                .HeadingLevel(AutomationHeadingLevel.Level1)
                .AutomationId("A11ySem_Title"),

            // Star rating: 3 out of 5 — should expose as a slider with range value
            HStack(4,
                TextBlock("★").FontSize(20),
                TextBlock("★").FontSize(20),
                TextBlock("★").FontSize(20),
                TextBlock("☆").FontSize(20),
                TextBlock("☆").FontSize(20)
            ).Semantics(
                role: "slider",
                value: "3 of 5 stars",
                rangeMin: 0,
                rangeMax: 5,
                rangeValue: 3
            ).AutomationName("Star rating")
             .AutomationId("A11ySem_StarRating"),

            // Status badge: should expose as a custom group with a value
            HStack(4,
                TextBlock("●").FontSize(10).Foreground("#00CC00"),
                TextBlock("Online")
            ).Semantics(
                role: "statusbar",
                value: "Online"
            ).AutomationName("Connection status")
             .AutomationId("A11ySem_StatusBadge")

        ).Landmark(AutomationLandmarkType.Main)
         .Padding(24);
    }

    // ════════════════════════════════════════════════════════════════
    //  LabeledBy — label → field association via UIA
    // ════════════════════════════════════════════════════════════════

    internal static Element LabeledByTest(RenderContext ctx)
    {
        return VStack(12,
            TextBlock("LabeledBy Test")
                .HeadingLevel(AutomationHeadingLevel.Level1)
                .AutomationId("A11yLbl_Title"),

            // Label element
            TextBlock("Email address")
                .AutomationId("A11yLbl_EmailLabel"),

            // Field referencing the label
            TextBox("user@example.com")
                .LabeledBy("A11yLbl_EmailLabel")
                .AutomationId("A11yLbl_EmailField"),

            // Self-labeled field (uses AutomationName instead)
            TextBox("")
                .AutomationName("Phone number")
                .AutomationId("A11yLbl_PhoneField")

        ).Landmark(AutomationLandmarkType.Main)
         .Padding(24);
    }

    // ════════════════════════════════════════════════════════════════
    //  TabNavigation mode — toolbar with contained navigation
    // ════════════════════════════════════════════════════════════════

    internal static Element TabNavigationTest(RenderContext ctx)
    {
        return VStack(12,
            TextBlock("Tab Navigation Test")
                .HeadingLevel(AutomationHeadingLevel.Level1)
                .AutomationId("A11yTabNav_Title"),

            // Regular field before toolbar
            TextBox("", _ => { })
                .AutomationName("Before toolbar")
                .AutomationId("A11yTabNav_Before"),

            // Toolbar with Once mode — Tab enters, then exits on next Tab
            HStack(4,
                Button("Bold", () => { })
                    .AutomationId("A11yTabNav_Bold"),
                Button("Italic", () => { })
                    .AutomationId("A11yTabNav_Italic"),
                Button("Underline", () => { })
                    .AutomationId("A11yTabNav_Underline")
            ).TabNavigation(Microsoft.UI.Xaml.Input.KeyboardNavigationMode.Once)
             .AutomationName("Formatting toolbar")
             .AutomationId("A11yTabNav_Toolbar"),

            // Regular field after toolbar
            TextBox("", _ => { })
                .AutomationName("After toolbar")
                .AutomationId("A11yTabNav_After")
        ).Landmark(AutomationLandmarkType.Main)
         .Padding(24);
    }
}
