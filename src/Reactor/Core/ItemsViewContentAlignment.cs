using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

internal sealed class ItemsViewContentAlignment : IDisposable
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "ItemsViewContentAlignment", typeof(object), typeof(ItemsViewContentAlignment), new PropertyMetadata(null));
    private readonly ItemsView _control;
    private readonly long _scrollViewToken;
    private ScrollView? _scroll;
    private long _contentToken;
    private FrameworkElement? _content;
    private object? _originalAlignment;
    private VerticalAlignment _alignment;

    private ItemsViewContentAlignment(ItemsView control, VerticalAlignment alignment)
    {
        _control = control;
        _alignment = alignment;
        control.Loaded += OnLoaded;
        control.Unloaded += OnUnloaded;
        _scrollViewToken = control.RegisterPropertyChangedCallback(ItemsView.ScrollViewProperty, OnScrollViewChanged);
    }

    internal static void Set(ItemsView control, VerticalAlignment? alignment)
    {
        if (alignment is null)
        {
            Detach(control);
            return;
        }
        var state = control.GetValue(StateProperty) as ItemsViewContentAlignment;
        if (state is null)
        {
            state = new(control, alignment.Value);
            control.SetValue(StateProperty, state);
        }
        state._alignment = alignment.Value;
        state.AttachScroll();
    }

    private void OnLoaded(object sender, RoutedEventArgs args) => AttachScroll();
    private void OnUnloaded(object sender, RoutedEventArgs args) => ReleaseScroll();
    private void OnScrollViewChanged(DependencyObject sender, DependencyProperty property) => AttachScroll();
    private void OnContentChanged(DependencyObject sender, DependencyProperty property) => ApplyContent();

    private void AttachScroll()
    {
        var scroll = _control.ScrollView;
        if (!ReferenceEquals(_scroll, scroll))
        {
            ReleaseScroll();
            _scroll = scroll;
            if (scroll is not null)
                _contentToken = scroll.RegisterPropertyChangedCallback(ScrollView.ContentProperty, OnContentChanged);
        }
        ApplyContent();
    }

    private void ApplyContent()
    {
        var content = _scroll?.Content as FrameworkElement;
        if (!ReferenceEquals(_content, content))
        {
            ReleaseContent();
            _content = content;
            if (content is not null)
                _originalAlignment = content.ReadLocalValue(FrameworkElement.VerticalAlignmentProperty);
        }
        // ItemsView's stock template fixes its repeater to Top instead of binding the
        // inherited VerticalContentAlignment. Align the native content, not the viewport.
        if (content is not null)
            content.VerticalAlignment = _alignment;
    }

    private void ReleaseContent()
    {
        if (_content is null) return;
        if (_originalAlignment == DependencyProperty.UnsetValue)
            _content.ClearValue(FrameworkElement.VerticalAlignmentProperty);
        else
            _content.SetValue(FrameworkElement.VerticalAlignmentProperty, _originalAlignment);
        _content = null;
        _originalAlignment = null;
    }

    private void ReleaseScroll()
    {
        ReleaseContent();
        if (_scroll is not null)
            _scroll.UnregisterPropertyChangedCallback(ScrollView.ContentProperty, _contentToken);
        _scroll = null;
    }

    internal static void Detach(ItemsView control)
    {
        (control.GetValue(StateProperty) as ItemsViewContentAlignment)?.Dispose();
        control.ClearValue(StateProperty);
    }

    public void Dispose()
    {
        _control.Loaded -= OnLoaded;
        _control.Unloaded -= OnUnloaded;
        _control.UnregisterPropertyChangedCallback(ItemsView.ScrollViewProperty, _scrollViewToken);
        ReleaseScroll();
    }
}
