namespace Microsoft.UI.Reactor.Core;

/// <summary>
/// Where a <see cref="SettingsCardElement"/> places its content (spec 065). Mirrors the
/// Windows Community Toolkit's <c>SettingsCard.ContentAlignment</c>.
/// </summary>
public enum SettingsCardContentAlignment
{
    /// <summary>The content sits to the right of the header. This is the default. When the
    /// card is narrower than 476 pixels the content moves below the header, and below 286
    /// pixels the icon is hidden as well.</summary>
    Right,

    /// <summary>The content is left-aligned, and the icon, header and description are hidden.
    /// Use it for content that carries its own label, such as a check box.</summary>
    Left,

    /// <summary>The content sits below the header and the description.</summary>
    Vertical,
}

/// <summary>
/// A Windows 11 settings row (spec 065): an optional icon, a header, an optional description
/// and a control. A native port of the Windows Community Toolkit's <c>SettingsCard</c>, with the
/// same look, states, narrow layout, keyboard and UI Automation behavior. Create it with
/// <see cref="Factories.SettingsCard(string, string?, IconData?, Element?, Action?)"/>.
/// </summary>
/// <param name="Header">The header text. <see cref="HeaderContent"/>, when set, is shown instead.</param>
/// <param name="Description">The description below the header. <see cref="DescriptionContent"/>,
/// when set, is shown instead.</param>
/// <param name="Icon">The icon on the left of the header.</param>
/// <param name="Content">The control on the right of the card, or below the header in a narrow
/// card and with <see cref="SettingsCardContentAlignment.Vertical"/>. Content without its own
/// UI Automation name gets the header text as its name, as in the Toolkit: that includes a check
/// box, radio button or hyperlink button that shows its own text, so give those
/// <c>.AutomationName(...)</c> when the header is not the right name.</param>
/// <param name="OnClick">Makes the card clickable. A clickable card is a button: it shows a
/// chevron and hover and pressed states, takes keyboard focus, and runs this callback on a
/// click, on Enter or Space, and on a UI Automation Invoke.</param>
public sealed record SettingsCardElement(
    string? Header,
    string? Description = null,
    IconData? Icon = null,
    Element? Content = null,
    Action? OnClick = null) : Element
{
    /// <summary>Rich header content, such as text with a link. Shown instead of <see cref="Header"/>.</summary>
    public Element? HeaderContent { get; init; }

    /// <summary>Rich description content, such as text with a link. Shown instead of <see cref="Description"/>.</summary>
    public Element? DescriptionContent { get; init; }

    /// <summary>
    /// The command that drives a card made with <see cref="Factories.SettingsCard(Command)"/>.
    /// Its label, description and icon fill the header, description and icon; a click runs its
    /// action (an <see cref="OnClick"/> callback takes precedence); and its
    /// <see cref="Command.IsEnabled"/> sets the card's enabled state. A command without an
    /// action does not make the card clickable.
    /// </summary>
    public Command? Command { get; init; }

    /// <summary>Where the content sits. See <see cref="SettingsCardContentAlignment"/>.</summary>
    public SettingsCardContentAlignment ContentAlignment { get; init; }

    /// <summary>The icon a clickable card shows on its right. <c>null</c> keeps the default chevron.</summary>
    public IconData? ActionIcon { get; init; }

    /// <summary>The tooltip of the action icon.</summary>
    public string? ActionIconToolTip { get; init; }

    /// <summary>Whether a clickable card shows its action icon. Defaults to <c>true</c>.</summary>
    public bool IsActionIconVisible { get; init; } = true;

    /// <summary>Set by <see cref="SettingsExpanderElement"/> on its direct card items, which take
    /// the expander-item look (a top divider instead of a full border, indented padding).</summary>
    internal bool IsExpanderItem { get; init; }

    /// <summary>Whether the card is a button: it has an <see cref="OnClick"/> callback or a
    /// command with an action.</summary>
    internal bool IsClickable => OnClick is not null || CommandBindings.Invokable(Command) is not null;

    internal override bool HasCallbacks => OnClick is not null || Command is not null;
}

/// <summary>
/// A settings card that expands to show more settings (spec 065). A native port of the
/// Windows Community Toolkit's <c>SettingsExpander</c>. Create it with
/// <see cref="Factories.SettingsExpander(string, string, IconData, Element, Optional{bool}, Action{bool}, Element[])"/>.
/// </summary>
/// <param name="Header">The header text. <see cref="HeaderContent"/>, when set, is shown instead.</param>
/// <param name="Description">The description below the header. <see cref="DescriptionContent"/>,
/// when set, is shown instead.</param>
/// <param name="Icon">The icon on the left of the header.</param>
/// <param name="Content">The control in the header row, next to the expand button, for example a
/// combo box. It is always visible; the expanding body is <paramref name="Items"/>.</param>
/// <param name="IsExpanded">The expanded state. Leave it unset to let the expander keep its own
/// state; set it (with <paramref name="OnIsExpandedChanged"/>) to control it from your state.</param>
/// <param name="OnIsExpandedChanged">Called when the user expands (<c>true</c>) or collapses
/// (<c>false</c>) the expander.</param>
/// <param name="Items">The body that shows when the expander is expanded, one row per item. Items
/// that are settings cards take the expander-item look, as in the Toolkit. Use
/// <c>.WithKey(...)</c> for items that move.</param>
public sealed record SettingsExpanderElement(
    string? Header,
    string? Description = null,
    IconData? Icon = null,
    Element? Content = null,
    Optional<bool> IsExpanded = default,
    Action<bool>? OnIsExpandedChanged = null,
    Element[]? Items = null) : Element
{
    /// <summary>Rich header content, such as text with a link. Shown instead of <see cref="Header"/>.</summary>
    public Element? HeaderContent { get; init; }

    /// <summary>Rich description content, such as text with a link. Shown instead of <see cref="Description"/>.</summary>
    public Element? DescriptionContent { get; init; }

    /// <summary>Content above the items, inside the expanding body.</summary>
    public Element? ItemsHeader { get; init; }

    /// <summary>Content below the items, inside the expanding body.</summary>
    public Element? ItemsFooter { get; init; }

    internal override bool HasCallbacks => OnIsExpandedChanged is not null;
}
