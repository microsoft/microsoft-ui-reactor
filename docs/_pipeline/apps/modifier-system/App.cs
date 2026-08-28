using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<ModifierSystemApp>("Modifier System", width: 650, height: 550);

class ModifierSystemApp : Component
{
    public override Element Render() =>
        ScrollView(
            VStack(16,
                TextBlock("Modifier System").FontSize(24).Bold(),
                Border(TextBlock("Card chrome").Padding(12)).Card()
            ).Padding(24)
        );
}

static class ModifierSystemExamples
{
    // <snippet:card-modifier>
    public static T Card<T>(this T el) where T : Element =>
        el.Padding(8).WithBorder(Theme.CardStroke).Background(Theme.CardBackground);
    // </snippet:card-modifier>
}

// <snippet:badge-modifier>
public record BadgeAttached(string Label);

public static class BadgeExtensions
{
    public static T Badge<T>(this T el, string label) where T : Element
    {
        // Copy any existing entries first — replacing the dictionary wholesale
        // would drop another provider's attached record on the same element.
        var attached = new Dictionary<Type, object>();
        if (el.Attached is { } existing)
        {
            foreach (var kv in existing) attached[kv.Key] = kv.Value;
        }
        attached[typeof(BadgeAttached)] = new BadgeAttached(label);
        return el with { Attached = attached };
    }
}
// </snippet:badge-modifier>

static class BadgeReaderExample
{
    public static void Read(Element child, FrameworkElement realizedChild)
    {
        // <snippet:badge-reader>
        if (child.Attached?.TryGetValue(typeof(BadgeAttached), out var data) == true
            && data is BadgeAttached badge)
        {
            ApplyBadge(realizedChild, badge);
        }
        // </snippet:badge-reader>
    }

    private static void ApplyBadge(FrameworkElement realizedChild, BadgeAttached badge)
    {
    }
}
