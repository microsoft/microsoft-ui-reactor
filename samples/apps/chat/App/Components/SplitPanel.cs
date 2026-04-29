using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

namespace ChatSample.App;

record SplitPanelProps(Element Left, Element Right, double InitialWidth = 280, double MinWidth = 200);

/// <summary>
/// Provides the resizable two-pane shell used by the sample sidebar and chat area.
/// </summary>
class SplitPanel : Component<SplitPanelProps>
{
    public override Element Render()
    {
        var widthRef = UseRef(Props.InitialWidth);
        var leftPaneRef = UseRef<FrameworkElement?>(null);
        var draggingRef = UseRef(false);
        var startXRef = UseRef(0.0);
        var startWidthRef = UseRef(0.0);

        var splitter = Border(Empty()).Width(1).Background(DividerStroke)
            .OnMount(fe =>
            {
                var grip = (Microsoft.UI.Xaml.Controls.Border)fe;
                grip.PointerPressed += (s, e) =>
                {
                    ((UIElement)s!).CapturePointer(e.Pointer);
                    draggingRef.Current = true;
                    startXRef.Current = e.GetCurrentPoint(null).Position.X;
                    startWidthRef.Current = widthRef.Current;
                    e.Handled = true;
                };
                grip.PointerMoved += (s, e) =>
                {
                    if (!draggingRef.Current) return;
                    var newWidth = Math.Max(Props.MinWidth, startWidthRef.Current + (e.GetCurrentPoint(null).Position.X - startXRef.Current));
                    widthRef.Current = newWidth;
                    if (leftPaneRef.Current is { } leftPane)
                        leftPane.Width = newWidth;
                };
                grip.PointerReleased += (s, e) =>
                {
                    ((UIElement)s!).ReleasePointerCapture(e.Pointer);
                    draggingRef.Current = false; e.Handled = true;
                };
            })
            .Flex(shrink: 0);

        var leftPane = Border(Props.Left)
            .Width(widthRef.Current)
            .MinWidth(Props.MinWidth)
            .OnMount(fe =>
            {
                var leftPane = (FrameworkElement)fe;
                leftPaneRef.Current = leftPane;
                leftPane.Width = widthRef.Current;
                leftPane.MinWidth = Props.MinWidth;
            })
            .Flex(shrink: 0);

        return FlexRow(
            leftPane,
            splitter,
            Props.Right.MinWidth(0).Flex(grow: 1, shrink: 1, basis: 0));
    }
}
