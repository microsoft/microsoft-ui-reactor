using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Microsoft.UI.Reactor.Core;

internal sealed class AnnotatedScrollBehavior : IDisposable
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "AnnotatedScrollBehavior", typeof(object), typeof(AnnotatedScrollBehavior), new PropertyMetadata(null));
    private readonly AnnotatedScrollBar _control;
    private ScrollAnnotation[] _annotations = [];
    private AnnotatedScrollBarElement _element;
    private double _selectedOffset;
    private readonly bool _originalTabStop;

    private AnnotatedScrollBehavior(AnnotatedScrollBar control, AnnotatedScrollBarElement element)
    {
        _control = control;
        _element = element;
        _originalTabStop = control.IsTabStop;
        control.DetailLabelRequested += OnDetailLabelRequested;
        control.Scrolling += OnScrolling;
        control.KeyDown += OnKeyDown;
    }

    internal static void Update(AnnotatedScrollBar control, AnnotatedScrollBarElement element)
    {
        ArgumentNullException.ThrowIfNull(element.Annotations);
        foreach (var annotation in element.Annotations)
        {
            ArgumentNullException.ThrowIfNull(annotation);
            if (!double.IsFinite(annotation.Offset) || annotation.Offset < 0)
                throw new ArgumentOutOfRangeException(nameof(element.Annotations), "Annotation offsets must be finite and nonnegative.");
        }
        if (element.ScrollMaximum is { } maximum
            && (!double.IsFinite(maximum) || maximum < 0 || !double.IsFinite(element.ScrollValue)
                || !double.IsFinite(element.ScrollViewport) || element.ScrollViewport < 0))
            throw new ArgumentOutOfRangeException(nameof(element.ScrollMaximum), "Scroll range values must be finite and nonnegative.");

        var state = control.GetValue(StateProperty) as AnnotatedScrollBehavior;
        if (state is null)
        {
            state = new(control, element);
            control.SetValue(StateProperty, state);
        }
        state._element = element;
        control.IsTabStop = element.OnScrollRequested is not null || state._originalTabStop;
        var annotations = element.Annotations.OrderBy(a => a.Offset).ToArray();
        var labelsChanged = state._annotations.Length != annotations.Length
            || state._annotations.Where((a, i) => a.Offset != annotations[i].Offset || a.Label != annotations[i].Label).Any();
        state._annotations = annotations;
        if (labelsChanged)
        {
            control.Labels.Clear();
            foreach (var annotation in state._annotations)
                control.Labels.Add(new AnnotatedScrollBarLabel(annotation.Label, annotation.Offset));
        }
        if (element.ScrollMaximum is { } max)
        {
            state._selectedOffset = Math.Clamp(element.ScrollValue, 0, max);
            control.ScrollController.SetValues(0, max, state._selectedOffset, element.ScrollViewport);
            control.ScrollController.SetIsScrollable(max > 0);
        }
    }

    internal static ScrollAnnotation? Nearest(IReadOnlyList<ScrollAnnotation> annotations, double offset)
    {
        if (!double.IsFinite(offset)) return null;
        ScrollAnnotation? nearest = null;
        var distance = double.PositiveInfinity;
        foreach (var annotation in annotations)
        {
            var candidate = Math.Abs(annotation.Offset - offset);
            if (candidate < distance)
            {
                nearest = annotation;
                distance = candidate;
            }
        }
        return nearest;
    }

    private void OnDetailLabelRequested(AnnotatedScrollBar sender, AnnotatedScrollBarDetailLabelRequestedEventArgs args)
    {
        var annotation = Nearest(_annotations, args.ScrollOffset);
        args.Content = annotation?.Detail ?? annotation?.Label;
    }

    private void OnScrolling(AnnotatedScrollBar sender, AnnotatedScrollBarScrollingEventArgs args)
    {
        if (_element.OnScrollRequested is null) return;
        args.Cancel = true;
        Request(args.ScrollOffset);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled || _element.OnScrollRequested is null || _annotations.Length == 0) return;
        var current = Array.IndexOf(_annotations, Nearest(_annotations, _selectedOffset));
        var next = args.Key switch
        {
            VirtualKey.Home => 0,
            VirtualKey.End => _annotations.Length - 1,
            VirtualKey.Up => Math.Max(0, current - 1),
            VirtualKey.Down => Math.Min(_annotations.Length - 1, current + 1),
            VirtualKey.Enter or VirtualKey.Space => current,
            _ => -1,
        };
        if (next < 0) return;
        args.Handled = true;
        Request(_annotations[next].Offset);
    }

    private void Request(double offset)
    {
        if (!double.IsFinite(offset)) return;
        _selectedOffset = offset;
        var annotation = Nearest(_annotations, offset);
        AutomationProperties.SetItemStatus(_control, annotation?.Detail ?? annotation?.Label ?? string.Empty);
        _element.OnScrollRequested?.Invoke(offset);
    }

    internal static void Detach(AnnotatedScrollBar control)
    {
        (control.GetValue(StateProperty) as AnnotatedScrollBehavior)?.Dispose();
        control.ClearValue(StateProperty);
    }

    public void Dispose()
    {
        _control.DetailLabelRequested -= OnDetailLabelRequested;
        _control.Scrolling -= OnScrolling;
        _control.KeyDown -= OnKeyDown;
        _control.Labels.Clear();
        _control.IsTabStop = _originalTabStop;
        AutomationProperties.SetItemStatus(_control, string.Empty);
    }
}
