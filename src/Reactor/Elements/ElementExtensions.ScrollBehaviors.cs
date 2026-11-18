using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor;

public static partial class ElementExtensions
{
    /// <summary>Submit the native text on Enter; Shift+Enter, composition, and modified Enter retain native behavior.</summary>
    public static TextBoxElement SubmitOnEnter(this TextBoxElement element, Action<string>? onSubmitted)
        => element with { OnSubmitted = onSubmitted };

    /// <summary>Add a resize handle to the pane. Arrow keys resize; Home/End select the bounds.</summary>
    public static SplitViewElement ResizablePane(this SplitViewElement element,
        double minimum = 160, double maximum = 640, Action<double>? onLengthChanged = null,
        bool enabled = true, string automationName = "Resize pane", double keyboardStep = 16)
        => element with
        {
            PaneResize = enabled ? new(minimum, maximum, keyboardStep, automationName) : null,
            OnPaneLengthChanged = onLengthChanged,
        };

    /// <summary>Follow appended and growing rows until the reader scrolls away; scrolling back resumes following.</summary>
    public static T FollowTail<T>(this T element, bool enabled = true,
        Action<bool>? onFollowingChanged = null, double threshold = 60) where T : ItemsViewElementBase
        => element with
        {
            IsTailFollowingEnabled = enabled,
            TailFollowThreshold = threshold,
            OnTailFollowingChanged = onFollowingChanged,
        };

    /// <summary>Increment requestId to return to the last row, including when the item collection has not changed.</summary>
    public static T ScrollToTail<T>(this T element, int requestId) where T : ItemsViewElementBase
        => element with { TailScrollRequest = requestId };

    /// <summary>Navigate to a stable key. Increment requestId to revisit the same key. Null removes the request.</summary>
    public static T ScrollIntoView<T>(this T element, string? key, int requestId = 0) where T : ItemsViewElementBase
        => element with { ScrollIntoViewKey = key, ScrollIntoViewRequest = requestId };

    /// <summary>Connect an annotated scrollbar through a lifecycle-aware sibling reference.</summary>
    public static T VerticalScrollBar<T>(this T element, ElementRef<WinUI.AnnotatedScrollBar>? scrollBar)
        where T : ItemsViewElementBase
        => element with { VerticalScrollBar = scrollBar };

    /// <summary>Connect an annotated scrollbar to the modern ScrollView (not the classic ScrollViewer).</summary>
    public static ScrollViewElement VerticalScrollBar(this ScrollViewElement element,
        ElementRef<WinUI.AnnotatedScrollBar>? scrollBar)
        => element with { VerticalScrollBar = scrollBar };

    public static AnnotatedScrollBarElement Annotations(this AnnotatedScrollBarElement element,
        IReadOnlyList<ScrollAnnotation> annotations)
        => element with { Annotations = annotations };

    /// <summary>Use the bar as a standalone ordinal navigator instead of a connected pixel scrollbar.</summary>
    public static AnnotatedScrollBarElement ScrollRange(this AnnotatedScrollBarElement element,
        double maximum, double value = 0, double viewport = 0)
        => element with { ScrollMaximum = maximum, ScrollValue = value, ScrollViewport = viewport };

    public static AnnotatedScrollBarElement ScrollRequested(this AnnotatedScrollBarElement element,
        Action<double>? onRequested)
        => element with { OnScrollRequested = onRequested };
}
