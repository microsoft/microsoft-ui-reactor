using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Native = Microsoft.UI.Xaml.Controls;
using Wct = CommunityToolkit.WinUI.Controls;
using WctControls;
using static Microsoft.UI.Reactor.Factories;

namespace ToolkitCompatibility;

internal static class SettingsSampleChecks
{
    internal static async Task RunAsync(CompatibilityRoot root)
    {
        int cardHeaderCleanups = 0, expanderHeaderCleanups = 0, removedItemCleanups = 0;
        Element Build(int phase)
        {
            if (phase == 8) return TextBlock("All settings removed");
            string title = phase >= 4 ? "Beta" : "Alpha";
            bool rich = phase is 1 or 2 or 7;
            var card = SettingsCardElement.SettingsCard(
                header: title, description: $"Description {phase}", content: ToggleSwitch(isOn: true),
                isClickEnabled: phase >= 5) with
            {
                Header = rich ? TextBlock(phase == 2 ? "Updated rich" : "Rich")
                    .OnUnmount(_ => cardHeaderCleanups++) : null
            };
            card = card.AutomationId("settings-card");
            if (phase == 6) card = card.AutomationName("Caller name");
            var a = SettingsCardElement.SettingsCard(header: $"A{phase}").WithKey("a");
            var b = SettingsCardElement.SettingsCard(header: "B").WithKey("b")
                .OnUnmount(_ => removedItemCleanups++);
            object[] items = phase switch
            {
                2 => [b, a, SettingsCardElement.SettingsCard(header: "C").WithKey("c")],
                >= 3 => [a],
                _ => [a, b]
            };
            var expander = SettingsExpanderElement.SettingsExpander(
                header: title, description: $"Expander {phase}", content: TextBlock($"Summary {phase}"),
                isExpanded: true, items: items) with
            {
                Header = rich ? TextBlock(phase == 2 ? "Updated rich" : "Rich")
                    .OnUnmount(_ => expanderHeaderCleanups++) : null
            };
            return VStack(12, card, expander.AutomationId("settings-expander"));
        }

        await root.ShowAsync(Build(0));
        var card = Find<Wct.SettingsCard>("settings-card");
        var expander = Find<Wct.SettingsExpander>("settings-expander");
        Program.Check(card.Header is string and "Alpha" && expander.Header is string and "Alpha",
            "String helpers must retain native string Header values.");
        CheckPeer(card, AutomationControlType.Group, "");
        var description = (Native.TextBlock)card.Description;
        var summary = (Native.TextBlock)expander.Content;
        var firstItem = (Wct.SettingsCard)expander.Items[0];
        var secondItem = (Wct.SettingsCard)expander.Items[1];

        await root.ShowAsync(Build(1));
        var cardHeader = card.Header;
        var expanderHeader = expander.Header;
        Program.Check(cardHeader is Native.TextBlock { Text: "Rich" } &&
            expanderHeader is Native.TextBlock { Text: "Rich" }, "Text-to-rich header mapping failed.");
        await root.ShowAsync(Build(2));
        Program.Check(ReferenceEquals(cardHeader, card.Header) &&
            card.Header is Native.TextBlock { Text: "Updated rich" }, "Card header lost identity on rich update.");
        Program.Check(ReferenceEquals(expanderHeader, expander.Header) &&
            expander.Header is Native.TextBlock { Text: "Updated rich" }, "Expander header lost identity on rich update.");
        Program.Check(ReferenceEquals(description, card.Description) && description.Text == "Description 2",
            "Description did not reconcile in place.");
        Program.Check(ReferenceEquals(summary, expander.Content) && summary.Text == "Summary 2",
            "Expander content did not reconcile alongside items.");
        Program.Check(expander.Items.Count == 3 && ReferenceEquals(expander.Items[0], secondItem) &&
            ReferenceEquals(expander.Items[1], firstItem), "Keyed expander insertion/reorder lost identity or order.");

        await root.ShowAsync(Build(3));
        Program.Check(card.Header is string and "Alpha" && expander.Header is string and "Alpha",
            "Rich-to-unchanged-string transition did not restore native string Header.");
        Program.Check(cardHeaderCleanups == 1 && expanderHeaderCleanups == 1,
            $"Rich-header cleanup counts were {cardHeaderCleanups}/{expanderHeaderCleanups}, expected 1/1.");
        Program.Check(expander.Items.Count == 1 && ReferenceEquals(expander.Items[0], firstItem) &&
            removedItemCleanups == 1 && !secondItem.IsLoaded, "Removed keyed item was not detached/cleaned once.");
        await root.ShowAsync(Build(4));
        Program.Check(card.Header is string and "Beta", "Dynamic string header did not update.");
        await root.ShowAsync(Build(5));
        CheckPeer(card, AutomationControlType.Button, "Beta");
        await root.ShowAsync(Build(6));
        CheckPeer(card, AutomationControlType.Button, "Caller name");
        await root.ShowAsync(Build(7));
        await root.ShowAsync(Build(8));
        Program.Check(cardHeaderCleanups == 2 && expanderHeaderCleanups == 2 &&
            !card.IsLoaded && !expander.IsLoaded, "Final rich-header unmount did not clean up exactly once.");
        Program.Results.Note("Settings string/rich transitions, keyed items, naming and cleanup assertions satisfied.");
    }

    private static T Find<T>(string id) where T : FrameworkElement =>
        CompatibilityRoot.Descendants(ReactorApp.PrimaryWindow!.NativeWindow.Content)
            .OfType<T>().Single(c => AutomationProperties.GetAutomationId(c) == id);

    private static void CheckPeer(FrameworkElement element, AutomationControlType expectedRole, string expectedName)
    {
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(element)
            ?? throw new InvalidOperationException("Settings card has no automation peer.");
        var role = peer.GetAutomationControlType();
        var name = peer.GetName();
        Program.Results.Note($"Settings peer role={role}; name='{name}'");
        Program.Check(role == expectedRole && name == expectedName,
            $"Expected Settings peer {expectedRole}/'{expectedName}', got {role}/'{name}'.");
    }
}
