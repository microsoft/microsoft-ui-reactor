using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Microsoft.UI.Reactor.Core;

internal sealed class ItemsViewScrollBarBinding(IScrollController? original)
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "ItemsScrollBarBinding", typeof(object), typeof(ItemsViewScrollBarBinding), new PropertyMetadata(null));

    internal static void Set(ItemsView view, AnnotatedScrollBar? bar)
    {
        var state = view.GetValue(StateProperty) as ItemsViewScrollBarBinding;
        if (bar is null)
        {
            if (state is null) return;
            view.VerticalScrollController = state.Original;
            view.ClearValue(StateProperty);
            return;
        }
        if (state is null)
            view.SetValue(StateProperty, new ItemsViewScrollBarBinding(view.VerticalScrollController));
        view.VerticalScrollController = bar.ScrollController;
    }

    private IScrollController? Original { get; } = original;
}

internal sealed class ScrollViewScrollBarBinding
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "ScrollBarBinding", typeof(object), typeof(ScrollViewScrollBarBinding), new PropertyMetadata(null));
    private readonly ScrollView _view;
    private AnnotatedScrollBar _bar;
    private ScrollPresenter? _presenter;
    private IScrollController? _original;

    private ScrollViewScrollBarBinding(ScrollView view, AnnotatedScrollBar bar)
    {
        _view = view;
        _bar = bar;
        view.Loaded += OnLoaded;
        view.Unloaded += OnUnloaded;
    }

    internal static void Set(ScrollView view, AnnotatedScrollBar? bar)
    {
        var state = view.GetValue(StateProperty) as ScrollViewScrollBarBinding;
        if (bar is null)
        {
            if (state is null) return;
            state.Restore();
            view.Loaded -= state.OnLoaded;
            view.Unloaded -= state.OnUnloaded;
            view.ClearValue(StateProperty);
            return;
        }
        if (state is null)
        {
            state = new(view, bar);
            view.SetValue(StateProperty, state);
        }
        state._bar = bar;
        state.Attach();
    }

    private void OnLoaded(object sender, RoutedEventArgs args) => Attach();
    private void OnUnloaded(object sender, RoutedEventArgs args) => Restore();

    private void Attach()
    {
        if (!_view.IsLoaded || _view.ScrollPresenter is not { } presenter) return;
        if (!ReferenceEquals(presenter, _presenter))
        {
            Restore();
            _presenter = presenter;
            _original = presenter.VerticalScrollController;
        }
        presenter.VerticalScrollController = _bar.ScrollController;
    }

    private void Restore()
    {
        if (_presenter is not null) _presenter.VerticalScrollController = _original;
        _presenter = null;
        _original = null;
    }
}
