using System;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

// V1-owned factories and patchers for menu flyout and command bar item models.
internal static class MenuCommandFactory
{
    internal static WinUI.ICommandBarElement CreateAppBarItem(AppBarItemBase item, Reconciler? reconciler = null)
    {
        var created = CreateAppBarItemCore(item);
        Publish(created as Microsoft.UI.Xaml.UIElement, item, reconciler);
        return created;
    }

    private static WinUI.ICommandBarElement CreateAppBarItemCore(AppBarItemBase item)
    {
        switch (item)
        {
            case AppBarButtonData cmd:
            {
                var abb = new WinUI.AppBarButton { Label = cmd.Label };
                abb.IsEnabled = cmd.IsEnabled;
                abb.Icon = IconResolver.ResolveIcon(cmd.IconElement, cmd.Icon);
                if (cmd.KeyboardAccelerators is not null)
                    foreach (var ka in cmd.KeyboardAccelerators)
                        abb.KeyboardAccelerators.Add(new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = ka.Key, Modifiers = ka.Modifiers });
                if (cmd.AccessKey is not null) abb.AccessKey = cmd.AccessKey;
                if (cmd.Description is not null)
                {
                    WinUI.ToolTipService.SetToolTip(abb, cmd.Description);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(abb, cmd.Description);
                }
                abb.Tag = cmd;
                abb.Click += (s, _) => ((AppBarButtonData)((WinUI.AppBarButton)s!).Tag!).OnClick?.Invoke();
                return abb;
            }
            case AppBarToggleButtonData toggle:
            {
                var atb = new WinUI.AppBarToggleButton { Label = toggle.Label, IsChecked = toggle.IsChecked };
                atb.Icon = IconResolver.ResolveIcon(toggle.IconElement, toggle.Icon);
                atb.Tag = toggle;
                atb.Checked += (s, _) => ((AppBarToggleButtonData)((WinUI.AppBarToggleButton)s!).Tag!).OnIsCheckedChanged?.Invoke(true);
                atb.Unchecked += (s, _) => ((AppBarToggleButtonData)((WinUI.AppBarToggleButton)s!).Tag!).OnIsCheckedChanged?.Invoke(false);
                return atb;
            }
            case AppBarSeparatorData:
                return new WinUI.AppBarSeparator();
            default:
                return new WinUI.AppBarSeparator();
        }
    }

    internal static WinUI.MenuFlyoutItemBase CreateMenuFlyoutItem(MenuFlyoutItemBase item, Reconciler? reconciler = null)
    {
        var created = CreateMenuFlyoutItemCore(item, reconciler);
        Publish(created, item, reconciler);
        return created;
    }

    private static WinUI.MenuFlyoutItemBase CreateMenuFlyoutItemCore(MenuFlyoutItemBase item, Reconciler? reconciler)
    {
        switch (item)
        {
            case MenuFlyoutItemData mfi:
            {
                var flyoutItem = new WinUI.MenuFlyoutItem { Text = mfi.Text };
                flyoutItem.IsEnabled = mfi.IsEnabled;
                flyoutItem.Icon = IconResolver.ResolveIcon(mfi.IconElement, mfi.Icon);
                if (mfi.KeyboardAccelerators is not null)
                    foreach (var ka in mfi.KeyboardAccelerators)
                        flyoutItem.KeyboardAccelerators.Add(new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = ka.Key, Modifiers = ka.Modifiers });
                if (mfi.AccessKey is not null) flyoutItem.AccessKey = mfi.AccessKey;
                if (mfi.Description is not null)
                {
                    WinUI.ToolTipService.SetToolTip(flyoutItem, mfi.Description);
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(flyoutItem, mfi.Description);
                }
                flyoutItem.Tag = mfi;
                flyoutItem.Click += (s, _) => ((MenuFlyoutItemData)((WinUI.MenuFlyoutItem)s!).Tag!).OnClick?.Invoke();
                return flyoutItem;
            }
            case ToggleMenuFlyoutItemData toggle:
            {
                var toggleItem = new WinUI.ToggleMenuFlyoutItem { Text = toggle.Text, IsChecked = toggle.IsChecked };
                toggleItem.Icon = IconResolver.ResolveIcon(toggle.IconElement, toggle.Icon);
                toggleItem.Tag = toggle;
                toggleItem.Click += (s, _) =>
                {
                    var ti = (WinUI.ToggleMenuFlyoutItem)s!;
                    ((ToggleMenuFlyoutItemData)ti.Tag!).OnIsCheckedChanged?.Invoke(ti.IsChecked);
                };
                return toggleItem;
            }
            case RadioMenuFlyoutItemData radio:
            {
                var radioItem = new WinUI.RadioMenuFlyoutItem { Text = radio.Text, GroupName = radio.GroupName, IsChecked = radio.IsChecked };
                radioItem.Tag = radio;
                radioItem.Click += (s, _) => ((RadioMenuFlyoutItemData)((WinUI.RadioMenuFlyoutItem)s!).Tag!).OnClick?.Invoke();
                return radioItem;
            }
            case MenuFlyoutSeparatorData:
                return new WinUI.MenuFlyoutSeparator();
            case MenuFlyoutSubItemData sub:
            {
                var subItem = new WinUI.MenuFlyoutSubItem { Text = sub.Text };
                subItem.Icon = IconResolver.ResolveIcon(sub.IconElement, sub.Icon);
                foreach (var child in sub.Items) subItem.Items.Add(CreateMenuFlyoutItem(child, reconciler));
                return subItem;
            }
            default:
                return new WinUI.MenuFlyoutSeparator();
        }
    }

    internal static void UpdateMenuFlyoutItems(
        global::System.Collections.Generic.IList<WinUI.MenuFlyoutItemBase> target,
        MenuFlyoutItemBase[] oldSource,
        MenuFlyoutItemBase[] newSource,
        Reconciler? reconciler = null)
    {
        int oldCount = oldSource.Length;
        int newCount = newSource.Length;
        int shared = Math.Min(oldCount, newCount);

        for (int i = 0; i < shared; i++)
        {
            switch (newSource[i])
            {
                case MenuFlyoutItemData mfi when target[i] is WinUI.MenuFlyoutItem existing:
                    existing.Text = mfi.Text;
                    existing.IsEnabled = mfi.IsEnabled;
                    existing.Icon = IconResolver.ResolveIcon(mfi.IconElement, mfi.Icon);
                    if (mfi.AccessKey is not null) existing.AccessKey = mfi.AccessKey;
                    if (mfi.Description is not null)
                    {
                        WinUI.ToolTipService.SetToolTip(existing, mfi.Description);
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(existing, mfi.Description);
                    }
                    existing.Tag = mfi;
                    break;

                case ToggleMenuFlyoutItemData toggle when target[i] is WinUI.ToggleMenuFlyoutItem toggleItem:
                    toggleItem.Text = toggle.Text;
                    toggleItem.IsChecked = toggle.IsChecked;
                    toggleItem.Icon = IconResolver.ResolveIcon(toggle.IconElement, toggle.Icon);
                    toggleItem.Tag = toggle;
                    break;

                case RadioMenuFlyoutItemData radio when target[i] is WinUI.RadioMenuFlyoutItem radioItem:
                    radioItem.Text = radio.Text;
                    radioItem.IsChecked = radio.IsChecked;
                    radioItem.Tag = radio;
                    break;

                case MenuFlyoutSeparatorData when target[i] is WinUI.MenuFlyoutSeparator:
                    break; // nothing to update

                case MenuFlyoutSubItemData sub when target[i] is WinUI.MenuFlyoutSubItem subItem:
                    subItem.Text = sub.Text;
                    subItem.Icon = IconResolver.ResolveIcon(sub.IconElement, sub.Icon);
                    // Recursively patch sub-items
                    var oldSub = oldSource[i] is MenuFlyoutSubItemData oldSubData ? oldSubData.Items : [];
                    UpdateMenuFlyoutItems(subItem.Items, oldSub, sub.Items, reconciler);
                    break;

                default:
                    // Type mismatch — replace the item (published as it is created)
                    target[i] = CreateMenuFlyoutItem(newSource[i], reconciler);
                    continue;
            }
            // Patched in place: the item at this slot may come from a different call site now.
            Publish(target[i], newSource[i], reconciler);
        }

        // Remove excess
        for (int i = oldCount - 1; i >= shared; i--)
            target.RemoveAt(i);

        // Add new
        for (int i = shared; i < newCount; i++)
            target.Add(CreateMenuFlyoutItem(newSource[i], reconciler));
    }

    internal static void UpdateAppBarItems(
        global::System.Collections.Generic.IList<WinUI.ICommandBarElement> target,
        AppBarItemBase[]? source,
        Reconciler? reconciler = null)
    {
        int newCount = source?.Length ?? 0;
        int oldCount = target.Count;

        // Update shared range (only update if types match, otherwise replace)
        int shared = Math.Min(oldCount, newCount);
        for (int i = 0; i < shared; i++)
        {
            if (source is null) continue;
            switch (source[i])
            {
                case AppBarButtonData cmd when target[i] is WinUI.AppBarButton abb:
                    abb.Label = cmd.Label;
                    abb.IsEnabled = cmd.IsEnabled;
                    abb.Icon = IconResolver.ResolveIcon(cmd.IconElement, cmd.Icon);
                    if (cmd.AccessKey is not null) abb.AccessKey = cmd.AccessKey;
                    if (cmd.Description is not null)
                    {
                        WinUI.ToolTipService.SetToolTip(abb, cmd.Description);
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(abb, cmd.Description);
                    }
                    abb.Tag = cmd;
                    break;
                case AppBarToggleButtonData toggle when target[i] is WinUI.AppBarToggleButton atb:
                    atb.Label = toggle.Label;
                    atb.IsChecked = toggle.IsChecked;
                    atb.Icon = IconResolver.ResolveIcon(toggle.IconElement, toggle.Icon);
                    atb.Tag = toggle;
                    break;
                case AppBarSeparatorData when target[i] is WinUI.AppBarSeparator:
                    break; // nothing to update
                default:
                    // Type mismatch — replace (published as it is created)
                    target[i] = CreateAppBarItem(source[i], reconciler);
                    continue;
            }
            Publish(target[i] as Microsoft.UI.Xaml.UIElement, source[i], reconciler);
        }

        // Remove excess
        for (int i = oldCount - 1; i >= shared; i--)
            target.RemoveAt(i);

        // Add new
        if (source is not null)
            for (int i = shared; i < newCount; i++)
                target.Add(CreateAppBarItem(source[i], reconciler));
    }

    /// <summary>
    /// Gives a menu or command-bar item control a Reactor identity for inspectors: the
    /// <c>ReactorSource</c> value (call site of the <c>MenuItem(…)</c> / <c>AppBarButton(…)</c>
    /// factory, owning component, item kind), so it maps back to C# and to the component
    /// whose handlers it calls. Diagnostics mode only, like every other published value.
    /// </summary>
    private static void Publish(Microsoft.UI.Xaml.UIElement? control, object item, Reconciler? reconciler)
    {
        if (control is null
            || !Hosting.ReactorFeatures.DevtoolsSupported
            || !Diagnostics.ReactorSourcePublisher.IsEnabled)
            return;
        var site = item switch
        {
            MenuFlyoutItemBase menuItem => menuItem.CallSite,
            AppBarItemBase command => command.CallSite,
            _ => null,
        };
        Diagnostics.ReactorSourcePublisher.PublishItem(control, site, reconciler?.DiagnosticComponent, ItemKind(item.GetType()));
        // The exact call site, for GetSource: the published at= text is shortened and can be
        // shared by two call sites, and these controls have no element tag to tell them apart.
        Core.Diagnostics.ReactorDiagnostics.SetItemSite(control, site);
    }

    /// <summary><c>MenuFlyoutItemData</c> → <c>MenuFlyoutItem</c>, <c>AppBarButtonData</c> → <c>AppBarButton</c>.</summary>
    internal static string ItemKind(Type itemType)
    {
        var name = itemType.Name;
        return name.Length > 4 && name.EndsWith("Data", StringComparison.Ordinal) ? name.Substring(0, name.Length - 4) : name;
    }
}
