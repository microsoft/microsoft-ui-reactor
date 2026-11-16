using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using V1 = Microsoft.UI.Reactor.Core.V1Protocol;

namespace Microsoft.UI.Reactor;

public static partial class Factories
{
    /// <summary>
    /// Creates a settings card (spec 065): a Windows 11 settings row with a header, an optional
    /// description and icon, and a control on the right. A native port of the Windows Community
    /// Toolkit's <c>SettingsCard</c>, with the same look in light, dark and high contrast, the
    /// same narrow layout, and the same keyboard and UI Automation behavior.
    /// </summary>
    /// <param name="header">The header text.</param>
    /// <param name="description">The description below the header.</param>
    /// <param name="icon">The icon on the left, for example <c>FontIcon("\uE7E7")</c>.</param>
    /// <param name="content">The control on the right, for example a <c>ToggleSwitch</c>. When the
    /// card is narrower than 476 pixels it moves below the header. Content without its own UI
    /// Automation name gets the header as its name, as in the Toolkit: that includes a check box,
    /// radio button or hyperlink button that shows its own text, so give those
    /// <c>.AutomationName(...)</c> when the header is not the right name.</param>
    /// <param name="onClick">Makes the card clickable. A clickable card is a button: it shows a
    /// chevron and hover and pressed states, can be focused with Tab, and runs
    /// <paramref name="onClick"/> on a click, on Enter or Space, and on a UI Automation Invoke.
    /// Narrator reads it as a button named after the header.</param>
    /// <example>
    /// <code>
    /// SettingsCard("Notifications", description: "Alerts from apps and the system",
    ///     icon: FontIcon("\uE7E7"), content: ToggleSwitch(notify, setNotify));
    /// </code>
    /// </example>
    public static SettingsCardElement SettingsCard(
        string header,
        string? description = null,
        IconData? icon = null,
        Element? content = null,
        Action? onClick = null)
    {
        _ = V1.Reg<SettingsCardElement, SettingsCardControl, V1.Handlers.SettingsCardHandler>.Done;
        return new(header, description, icon, content, onClick);
    }

    /// <summary>
    /// Creates a settings card whose header and description are elements, for rich content
    /// such as text with a link. A plain text element renders exactly like the string overload.
    /// An element header does not name the card or its content for UI Automation: give a
    /// clickable card, and the content, <c>.AutomationName(...)</c>.
    /// See <see cref="SettingsCard(string, string?, IconData?, Element?, Action?)"/>.
    /// </summary>
    public static SettingsCardElement SettingsCard(
        Element header,
        Element? description = null,
        IconData? icon = null,
        Element? content = null,
        Action? onClick = null)
    {
        _ = V1.Reg<SettingsCardElement, SettingsCardControl, V1.Handlers.SettingsCardHandler>.Done;
        var (headerText, headerContent) = SplitSettingsText(header);
        var (descriptionText, descriptionContent) = SplitSettingsText(description);
        return new(headerText, descriptionText, icon, content, onClick)
        {
            HeaderContent = headerContent,
            DescriptionContent = descriptionContent,
        };
    }

    /// <summary>
    /// Creates a clickable settings card driven by a <see cref="Command"/>: the command's
    /// label, description and icon fill the header, description and icon, a click runs the
    /// command, and the card is disabled while the command cannot run. The command's
    /// accelerator and access key are not applied to the card; bind the command to a button,
    /// a menu item or the window for its shortcut.
    /// </summary>
    public static SettingsCardElement SettingsCard(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _ = V1.Reg<SettingsCardElement, SettingsCardControl, V1.Handlers.SettingsCardHandler>.Done;
        return new(command.Label, command.Description, command.Icon) { Command = command };
    }

    /// <summary>
    /// Creates a settings expander (spec 065): a settings card that expands to show more
    /// settings. A native port of the Windows Community Toolkit's <c>SettingsExpander</c>.
    /// </summary>
    /// <param name="header">The header text.</param>
    /// <param name="description">The description below the header.</param>
    /// <param name="icon">The icon on the left.</param>
    /// <param name="content">The control in the header row, next to the expand button. It is
    /// always visible; it is <b>not</b> the expanding body, which is <paramref name="items"/>.</param>
    /// <param name="isExpanded">The expanded state. Omit it to let the expander keep its own state;
    /// pass a value (with <paramref name="onIsExpandedChanged"/>) to control it from your state.</param>
    /// <param name="onIsExpandedChanged">Called when the user expands or collapses the expander.</param>
    /// <param name="items">The body shown when expanded, one row per item. Settings cards take the
    /// expander-item look (a divider line and indented padding). Use <c>.WithKey(...)</c> on items
    /// that move. Add content above or below them with <c>.ItemsHeader(...)</c> and
    /// <c>.ItemsFooter(...)</c>.</param>
    /// <example>
    /// <code>
    /// SettingsExpander("Display", description: "Brightness, night light and scale",
    ///     icon: FontIcon("\uE7F4"),
    ///     content: ComboBox(["100%", "125%", "150%"], scale, setScale),
    ///     isExpanded: open, onIsExpandedChanged: setOpen,
    ///     items: [
    ///         SettingsCard("Brightness", content: Slider(brightness, 0, 100, setBrightness)),
    ///         SettingsCard("Night light", content: ToggleSwitch(nightLight, setNightLight)),
    ///     ]);
    /// </code>
    /// </example>
    public static SettingsExpanderElement SettingsExpander(
        string header,
        string? description = null,
        IconData? icon = null,
        Element? content = null,
        Optional<bool> isExpanded = default,
        Action<bool>? onIsExpandedChanged = null,
        Element[]? items = null)
    {
        _ = V1.Reg<SettingsExpanderElement, SettingsExpanderControl, V1.Handlers.SettingsExpanderHandler>.Done;
        return new(header, description, icon, content, isExpanded, onIsExpandedChanged, items);
    }

    /// <summary>
    /// Creates a settings expander whose header and description are elements, for rich content
    /// such as text with a link. A plain text element renders exactly like the string overload.
    /// An element header does not name the expander or its header-row content for UI
    /// Automation: give them <c>.AutomationName(...)</c>.
    /// See <see cref="SettingsExpander(string, string, IconData, Element, Optional{bool}, Action{bool}, Element[])"/>.
    /// </summary>
    public static SettingsExpanderElement SettingsExpander(
        Element header,
        Element? description = null,
        IconData? icon = null,
        Element? content = null,
        Optional<bool> isExpanded = default,
        Action<bool>? onIsExpandedChanged = null,
        Element[]? items = null)
    {
        _ = V1.Reg<SettingsExpanderElement, SettingsExpanderControl, V1.Handlers.SettingsExpanderHandler>.Done;
        var (headerText, headerContent) = SplitSettingsText(header);
        var (descriptionText, descriptionContent) = SplitSettingsText(description);
        return new(headerText, descriptionText, icon, content, isExpanded, onIsExpandedChanged, items)
        {
            HeaderContent = headerContent,
            DescriptionContent = descriptionContent,
        };
    }

    // A plain text element (what a string converts to) is rendered as text, exactly like the
    // string overload, so SettingsCard("Header", description: HyperlinkButton(...)) keeps a
    // wrapping header that names the content and the clickable card. Any styling, setter or
    // modifier makes it rich content, rendered as given.
    internal static (string? Text, Element? Content) SplitSettingsText(Element? element)
        => element is TextBlockElement { Modifiers: null } text
            && !Element.HasBehavioralExtras(text)
            && TextBlockElement.DiffProps(new TextBlockElement(text.Content), text) == TextPropChanged.None
            ? (text.Content, null)
            : (null, element);
}

public static partial class ElementExtensions
{
    /// <summary>Sets where the card's content sits (spec 065): on the right (the default),
    /// on the left with the header hidden, or below the header.</summary>
    public static SettingsCardElement ContentAlignment(this SettingsCardElement el, SettingsCardContentAlignment alignment)
        => el with { ContentAlignment = alignment };

    /// <summary>Sets the icon a clickable card shows on its right. <c>null</c> restores the default chevron.</summary>
    public static SettingsCardElement ActionIcon(this SettingsCardElement el, IconData? icon)
        => el with { ActionIcon = icon };

    /// <summary>Sets the tooltip of a clickable card's action icon.</summary>
    public static SettingsCardElement ActionIconToolTip(this SettingsCardElement el, string? toolTip)
        => el with { ActionIconToolTip = toolTip };

    /// <summary>Shows or hides a clickable card's action icon. It is visible by default.</summary>
    public static SettingsCardElement ActionIconVisible(this SettingsCardElement el, bool visible)
        => el with { IsActionIconVisible = visible };

    /// <summary>Sets content shown above the items, inside the expanding body.</summary>
    public static SettingsExpanderElement ItemsHeader(this SettingsExpanderElement el, Element? header)
        => el with { ItemsHeader = header };

    /// <summary>Sets content shown below the items, inside the expanding body.</summary>
    public static SettingsExpanderElement ItemsFooter(this SettingsExpanderElement el, Element? footer)
        => el with { ItemsFooter = footer };
}
