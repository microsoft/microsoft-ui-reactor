using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Reactor.Wrappers;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.Core;

/// <summary>An annotation in scroll-offset coordinates. Detail defaults to Label.</summary>
public sealed record ScrollAnnotation(double Offset, string Label, string? Detail = null);

/// <summary>Bounds and keyboard interaction for an optional SplitView resize handle, in DIPs.</summary>
public sealed record PaneResizeOptions(
    double Minimum = 160,
    double Maximum = 640,
    double KeyboardStep = 16,
    string AutomationName = "Resize pane");

[WrapManual(nameof(OnSubmitted))]
public partial record TextBoxElement
{
    /// <summary>
    /// Submit the current native text on unmodified Enter. Shift+Enter remains a newline
    /// when AcceptsReturn is true. IME composition and key repeats do not submit.
    /// This does not clear the text or move focus.
    /// </summary>
    public Action<string>? OnSubmitted { get; init; }
}

[WrapManual(nameof(PaneResize))]
[WrapManual(nameof(OnPaneLengthChanged))]
public partial record SplitViewElement
{
    /// <summary>Null keeps the native, non-resizable pane. Otherwise adds a pointer and keyboard resize handle.</summary>
    public PaneResizeOptions? PaneResize { get; init; }
    /// <summary>Reports user resizing; feed it back into OpenPaneLength for controlled sizing.</summary>
    public Action<double>? OnPaneLengthChanged { get; init; }
    /// <summary>Side on which the pane appears.</summary>
    public WinUI.SplitViewPanePlacement PanePlacement { get; init; } = WinUI.SplitViewPanePlacement.Left;
}

[WrapManual(nameof(Annotations))]
[WrapManual(nameof(ScrollMaximum))]
[WrapManual(nameof(ScrollValue))]
[WrapManual(nameof(ScrollViewport))]
[WrapManual(nameof(OnScrollRequested))]
public partial record AnnotatedScrollBarElement
{
    /// <summary>Labels and hover details at scroll offsets. Use a short glyph as Label for history ticks.</summary>
    public IReadOnlyList<ScrollAnnotation> Annotations { get; init; } = [];
    /// <summary>
    /// An optional standalone range, starting at zero, for ordinal navigation.
    /// Leave unset when a ScrollView or ItemsView supplies the native scroll controller's range.
    /// </summary>
    public double? ScrollMaximum { get; init; }
    public double ScrollValue { get; init; }
    public double ScrollViewport { get; init; }
    /// <summary>Handles scrolling as application navigation instead of moving a connected native viewport.</summary>
    public Action<double>? OnScrollRequested { get; init; }
    internal override bool HasCallbacks => OnScrollRequested is not null;

    private static partial ControlDescriptor<AnnotatedScrollBarElement, WinUI.AnnotatedScrollBar> Customize(
        ControlDescriptor<AnnotatedScrollBarElement, WinUI.AnnotatedScrollBar> d)
        => d.ImperativeBridged(
            static (_, c, e) => AnnotatedScrollBehavior.Update(c, e),
            static (_, c, _, e) => AnnotatedScrollBehavior.Update(c, e),
            static (_, c) => AnnotatedScrollBehavior.Detach(c));
}

[WrapManual(nameof(VerticalScrollBar))]
public partial record ScrollViewElement
{
    /// <summary>A sibling AnnotatedScrollBar used as this viewport's vertical scroll controller.</summary>
    public ElementRef<WinUI.AnnotatedScrollBar>? VerticalScrollBar { get; init; }

    private static partial ControlDescriptor<ScrollViewElement, WinUI.ScrollView> Customize(
        ControlDescriptor<ScrollViewElement, WinUI.ScrollView> d)
        => d.Reference(static e => e.VerticalScrollBar, ScrollViewScrollBarBinding.Set)
            .WithUnmount(static (in V1Protocol.UnmountContext _, WinUI.ScrollView c) => ScrollViewScrollBarBinding.Set(c, null));
}

public abstract partial record ItemsViewElementBase
{
    /// <summary>Opt in to initial positioning and following appended or growing content while near the tail.</summary>
    public bool IsTailFollowingEnabled { get; init; }
    /// <summary>Distance from the bottom, in DIPs, within which user scrolling resumes tail following.</summary>
    public double TailFollowThreshold { get; init; } = 60;
    /// <summary>Reports changes between following and reading history.</summary>
    public Action<bool>? OnTailFollowingChanged { get; init; }
    /// <summary>Increment to explicitly return to the tail and resume following.</summary>
    public int TailScrollRequest { get; init; }
    /// <summary>Stable item key to bring into view at the top, without stealing keyboard focus.</summary>
    public string? ScrollIntoViewKey { get; init; }
    /// <summary>Increment to repeat navigation to the same ScrollIntoViewKey.</summary>
    public int ScrollIntoViewRequest { get; init; }
    /// <summary>A sibling AnnotatedScrollBar used as the native vertical scroll controller.</summary>
    public ElementRef<WinUI.AnnotatedScrollBar>? VerticalScrollBar { get; init; }
}
