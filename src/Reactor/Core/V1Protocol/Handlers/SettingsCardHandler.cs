using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol.Handlers;

// Spec 065 — SettingsCard and SettingsExpander. Hand-coded handlers: the native controls are
// Reactor's own (SettingsCardControl, SettingsExpanderControl), the element slots (rich
// header and description, content, the expander's items and items header/footer) reconcile
// through NamedSlots, and both controls are always tagged with their element, because unmount
// reaches the slot teardown only through the tag.

/// <summary>Spec 065 — <see cref="SettingsCardElement"/> → <see cref="SettingsCardControl"/>.</summary>
internal sealed class SettingsCardHandler : IElementHandler<SettingsCardElement, SettingsCardControl>
{
    private static readonly NamedSlots<SettingsCardElement, SettingsCardControl> s_slots = new(
    [
        new("Header", static el => el.HeaderContent, static (card, child) => card.HeaderChild = child)
        {
            GetCurrentChild = static card => card.HeaderChild,
        },
        new("Description", static el => el.DescriptionContent, static (card, child) => card.DescriptionChild = child)
        {
            GetCurrentChild = static card => card.DescriptionChild,
        },
        new("Content", static el => el.Content, static (card, child) => card.Content = child)
        {
            GetCurrentChild = static card => card.Content as UIElement,
        },
    ]);

    public ChildrenStrategy<SettingsCardElement, SettingsCardControl>? Children => s_slots;

    public SettingsCardControl Mount(MountContext ctx, SettingsCardElement el)
    {
        var card = new SettingsCardControl();
        Apply(card, null, el);
        card.Invoked += static card => Dispatch(card);
        Reconciler.SetElementTag(card, el);
        return card;
    }

    public void Update(UpdateContext ctx, SettingsCardElement oldEl, SettingsCardElement newEl, SettingsCardControl card)
        => Apply(card, oldEl, newEl);

    internal static void Apply(SettingsCardControl card, SettingsCardElement? oldEl, SettingsCardElement el)
    {
        card.SyncHighContrast();
        card.IsClickEnabled = el.IsClickable;
        card.IsExpanderItem = el.IsExpanderItem;
        card.HeaderText = el.Header;
        card.DescriptionText = el.Description;
        if (oldEl is null || !Equals(oldEl.Icon, el.Icon))
            card.HeaderIcon = IconResolver.ResolveIcon(el.Icon, null);
        if (oldEl is null || !Equals(oldEl.ActionIcon, el.ActionIcon))
            card.ActionIcon = IconResolver.ResolveIcon(el.ActionIcon, null);
        card.ActionIconToolTip = el.ActionIconToolTip;
        card.IsActionIconVisible = el.IsActionIconVisible;
        card.ContentAlignment = el.ContentAlignment;

        // A command's enabled state drives the card; modifiers (applied after) still win.
        if (el.Command is { } command)
        {
            if (card.IsEnabled != command.IsEnabled) card.IsEnabled = command.IsEnabled;
        }
        else if (oldEl?.Command is not null)
        {
            card.ClearValue(Control.IsEnabledProperty);
        }
    }

    private static void Dispatch(SettingsCardControl card)
    {
        if (Reconciler.GetElementTag(card) is not SettingsCardElement el) return;
        if (el.OnClick is { } onClick) onClick();
        else if (el.Command is { IsEnabled: true } command) CommandBindings.Invoke(command);
    }
}

/// <summary>Spec 065 — <see cref="SettingsExpanderElement"/> → <see cref="SettingsExpanderControl"/>.</summary>
internal sealed class SettingsExpanderHandler : IElementHandler<SettingsExpanderElement, SettingsExpanderControl>
{
    private static readonly NamedSlots<SettingsExpanderElement, SettingsExpanderControl> s_slots = new(
    [
        new("Header", static el => el.HeaderContent, static (expander, child) =>
        {
            expander.HeaderCard.HeaderChild = child;
            expander.OnHeaderChanged();
        })
        {
            GetCurrentChild = static expander => expander.HeaderCard.HeaderChild,
        },
        new("Description", static el => el.DescriptionContent, static (expander, child) => expander.HeaderCard.DescriptionChild = child)
        {
            GetCurrentChild = static expander => expander.HeaderCard.DescriptionChild,
        },
        new("Content", static el => el.Content, static (expander, child) => expander.HeaderCard.Content = child)
        {
            GetCurrentChild = static expander => expander.HeaderCard.Content as UIElement,
        },
        new("ItemsHeader", static el => el.ItemsHeader, static (expander, child) => expander.ItemsHeaderChild = child)
        {
            GetCurrentChild = static expander => expander.ItemsHeaderChild,
        },
        new("Items", static el => BuildItems(el.Items), static (expander, child) => expander.ItemsChild = child)
        {
            GetCurrentChild = static expander => expander.ItemsChild,
        },
        new("ItemsFooter", static el => el.ItemsFooter, static (expander, child) => expander.ItemsFooterChild = child)
        {
            GetCurrentChild = static expander => expander.ItemsFooterChild,
        },
    ]);

    public ChildrenStrategy<SettingsExpanderElement, SettingsExpanderControl>? Children => s_slots;

    public SettingsExpanderControl Mount(MountContext ctx, SettingsExpanderElement el)
    {
        var expander = new SettingsExpanderControl();
        Apply(expander, null, el);
        if (el.IsExpanded.HasValue) expander.IsExpanded = el.IsExpanded.Value;
        expander.IsExpandedChanged += static (expander, isExpanded) =>
            (Reconciler.GetElementTag(expander) as SettingsExpanderElement)?.OnIsExpandedChanged?.Invoke(isExpanded);
        Reconciler.SetElementTag(expander, el);
        return expander;
    }

    public void Update(UpdateContext ctx, SettingsExpanderElement oldEl, SettingsExpanderElement newEl, SettingsExpanderControl expander)
    {
        Apply(expander, oldEl, newEl);
        if (newEl.IsExpanded.HasValue) expander.IsExpanded = newEl.IsExpanded.Value;
    }

    private static void Apply(SettingsExpanderControl expander, SettingsExpanderElement? oldEl, SettingsExpanderElement el)
    {
        expander.SyncHighContrast();
        expander.HeaderCard.HeaderText = el.Header;
        expander.HeaderCard.DescriptionText = el.Description;
        if (oldEl is null || !Equals(oldEl.Icon, el.Icon))
            expander.HeaderCard.HeaderIcon = IconResolver.ResolveIcon(el.Icon, null);
        expander.OnHeaderChanged();
    }

    /// <summary>
    /// The expanding body: the items stacked without spacing. Items that are settings cards
    /// take the expander-item look, as the Toolkit's item style selector gives them.
    /// </summary>
    internal static Element? BuildItems(Element[]? items)
    {
        if (items is null || items.Length == 0) return null;
        var rows = new Element?[items.Length];
        for (var i = 0; i < items.Length; i++)
        {
            rows[i] = items[i] is SettingsCardElement { IsExpanderItem: false } card
                ? card with { IsExpanderItem = true }
                : items[i];
        }
        return Factories.VStack(0, rows);
    }
}
