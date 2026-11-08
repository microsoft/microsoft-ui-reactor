using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace ReactorRegedit.Native;

/// <summary>
/// ObservableCollection that can swap its whole content with a single Reset
/// notification (WinUI list controls only understand single-item change events, and
/// thousands of them are slow).
/// </summary>
internal sealed class RowCollection<T> : ObservableCollection<T>
{
    public void ResetTo(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Makes this collection equal to <paramref name="target"/> (compared by reference)
    /// by replacing only the differing middle span; falls back to a Reset when the span
    /// is large.
    /// </summary>
    public bool SyncTo(IReadOnlyList<T> target, int resetThreshold = 300)
    {
        int prefix = 0;
        int max = Math.Min(Count, target.Count);
        while (prefix < max && ReferenceEquals(this[prefix], target[prefix])) prefix++;
        int suffix = 0;
        while (suffix < max - prefix &&
               ReferenceEquals(this[Count - 1 - suffix], target[target.Count - 1 - suffix])) suffix++;

        int removeCount = Count - prefix - suffix;
        int insertCount = target.Count - prefix - suffix;
        if (removeCount == 0 && insertCount == 0) return false;
        if (removeCount + insertCount > resetThreshold)
        {
            ResetTo(target);
            return true;
        }
        for (int i = 0; i < removeCount; i++) RemoveAt(prefix + removeCount - 1 - i);
        for (int i = 0; i < insertCount; i++) Insert(prefix + i, target[prefix + i]);
        return false;
    }
}

/// <summary>Shared helpers for the two list islands.</summary>
internal static class ListHelpers
{
    /// <summary>Compact item container: ≤ 22 epx rows, no extra padding.</summary>
    public static Style CompactItemStyle(double height)
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0.0));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, height));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 0, 2, 0)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        return style;
    }

    /// <summary>An empty Grid template; rows are populated in ContainerContentChanging.</summary>
    public static DataTemplate EmptyGridTemplate() =>
        (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Grid Background=\"Transparent\"/></DataTemplate>");

    /// <summary>
    /// Accent-tinted selection (lightweight styling) so the selected row reads clearly,
    /// like classic regedit, instead of the faint default gray.
    /// </summary>
    public static void ApplySelectionBrushes(FrameworkElement list)
    {
        var accent = Application.Current.Resources.TryGetValue("SystemAccentColor", out var a) && a is Windows.UI.Color c
            ? c : Windows.UI.Color.FromArgb(0xFF, 0x00, 0x78, 0xD4);
        ResourceDictionary Make(byte selected, byte hover) => new()
        {
            ["ListViewItemBackgroundSelected"] = new SolidColorBrush(Windows.UI.Color.FromArgb(selected, accent.R, accent.G, accent.B)),
            ["ListViewItemBackgroundSelectedPointerOver"] = new SolidColorBrush(Windows.UI.Color.FromArgb(hover, accent.R, accent.G, accent.B)),
            ["ListViewItemBackgroundSelectedPressed"] = new SolidColorBrush(Windows.UI.Color.FromArgb(hover, accent.R, accent.G, accent.B)),
        };
        list.Resources.ThemeDictionaries["Light"] = Make(0x40, 0x55);
        list.Resources.ThemeDictionaries["Default"] = Make(0x66, 0x80);
    }

    public static T? FindAncestor<T>(DependencyObject? d) where T : class
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    public static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null) return found;
        }
        return null;
    }

    public static MenuFlyout BuildMenu(IEnumerable<MenuSpec> items)
    {
        var flyout = new MenuFlyout();
        foreach (var item in items) flyout.Items.Add(BuildItem(item));
        return flyout;
    }

    private static MenuFlyoutItemBase BuildItem(MenuSpec spec)
    {
        if (spec.IsSeparator) return new MenuFlyoutSeparator();
        if (spec.Children is { } children)
        {
            var sub = new MenuFlyoutSubItem { Text = spec.Text, IsEnabled = spec.Enabled };
            foreach (var c in children) sub.Items.Add(BuildItem(c));
            return sub;
        }
        var mi = new MenuFlyoutItem { Text = spec.Text, IsEnabled = spec.Enabled };
        if (spec.KeyText is not null) mi.KeyboardAcceleratorTextOverride = spec.KeyText;
        if (spec.Invoke is { } invoke) mi.Click += (_, _) => invoke();
        return mi;
    }

    /// <summary>A plain TextBox used for inline (in-row) rename.</summary>
    public static TextBox InlineEditor(string text)
    {
        var tb = new TextBox
        {
            Text = text,
            MinHeight = 0,
            Height = 22,
            Padding = new Thickness(4, 1, 4, 1),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tb, "Rename");
        return tb;
    }
}

/// <summary>Declarative description of a native context-menu entry.</summary>
internal sealed record MenuSpec(string Text, Action? Invoke = null, bool Enabled = true, MenuSpec[]? Children = null, string? KeyText = null)
{
    public bool IsSeparator { get; init; }
    public static MenuSpec Separator { get; } = new("") { IsSeparator = true };
}

/// <summary>Grid that shows a resize cursor (ProtectedCursor needs a subclass).</summary>
internal sealed partial class ResizeGrip : Grid
{
    public ResizeGrip()
    {
        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
}
