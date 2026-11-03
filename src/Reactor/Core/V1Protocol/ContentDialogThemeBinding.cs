using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core.V1Protocol;

/// <summary>
/// Bridges the declaration's theme to the side-mounted dialog. XamlRoot chooses
/// the window, but does not parent the dialog under its declaration's subtree.
/// </summary>
internal sealed class ContentDialogThemeBinding : IDisposable
{
    private readonly FrameworkElement _anchor;
    private readonly WinUI.ContentDialog _dialog;
    private ElementTheme? _requestedTheme;
    private ElementTheme _appliedTheme;
    private bool _setterOverride;
    private bool _disposed;

    internal ContentDialogThemeBinding(FrameworkElement anchor, WinUI.ContentDialog dialog)
    {
        _anchor = anchor;
        _dialog = dialog;
        anchor.ActualThemeChanged += OnAnchorThemeChanged;
    }

    internal void Apply(ContentDialogElement element)
    {
        var requested = element.Modifiers?.RequestedTheme;
        _requestedTheme = requested is ElementTheme.Default ? null : requested;
        _setterOverride = false;
        ApplyTheme(_requestedTheme ?? _anchor.ActualTheme);
    }

    // .Set is the escape hatch and runs last. Do not overwrite a theme it chose
    // when an ancestor later changes; nor re-run arbitrary setters from an event.
    internal void ApplySetters(ContentDialogElement element)
    {
        if (element.Setters.Length == 0)
        {
            _setterOverride = false;
            ApplyTheme(_requestedTheme ?? _anchor.ActualTheme);
            return;
        }

        // A write of the same value is still an explicit override. Value-change
        // notifications cannot distinguish that from a setter that never wrote.
        _dialog.ClearValue(FrameworkElement.RequestedThemeProperty);
        try
        {
            Reconciler.ApplySetters(element.Setters, _dialog);
        }
        finally
        {
            _setterOverride = _dialog.ReadLocalValue(FrameworkElement.RequestedThemeProperty)
                != DependencyProperty.UnsetValue;
            if (!_setterOverride)
                ApplyTheme(_requestedTheme ?? _anchor.ActualTheme);
        }
    }

    private void OnAnchorThemeChanged(FrameworkElement sender, object args)
    {
        if (_disposed || _requestedTheme.HasValue || _setterOverride
            || _dialog.RequestedTheme != _appliedTheme)
            return;
        ApplyTheme(_anchor.ActualTheme);
    }

    private void ApplyTheme(ElementTheme theme)
    {
        _appliedTheme = theme;
        if (_dialog.RequestedTheme != theme)
            _dialog.RequestedTheme = theme;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _anchor.ActualThemeChanged -= OnAnchorThemeChanged;
    }
}
