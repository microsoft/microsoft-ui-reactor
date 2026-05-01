using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace ReactorRegedit.Components;

// ─── Custom element + control: CursorBorder ─────────────────────────────────

internal record CursorBorderElement(Element Child, InputSystemCursorShape Cursor) : Element
{
    public Microsoft.UI.Xaml.Media.Brush? Background { get; init; }
}

internal sealed partial class CursorPanel : Grid
{
    public CursorPanel(InputSystemCursorShape shape)
    {
        ProtectedCursor = InputSystemCursor.Create(shape);
    }
}

internal static class CursorBorderRegistration
{
    public static void Register(Reconciler reconciler)
    {
        reconciler.RegisterType<CursorBorderElement, CursorPanel>(
            mount: (r, el, rerender) =>
            {
                var panel = new CursorPanel(el.Cursor);
                if (el.Background is not null) panel.Background = el.Background;
                var child = r.Mount(el.Child, rerender);
                if (child is not null) panel.Children.Add(child);
                panel.Tag = el;
                return panel;
            },
            update: (r, oldEl, newEl, panel, rerender) =>
            {
                if (newEl.Background is not null) panel.Background = newEl.Background;
                if (panel.Children.Count > 0 && panel.Children[0] is UIElement existingChild)
                {
                    var replacement = r.UpdateChild(oldEl.Child, newEl.Child, existingChild, rerender);
                    if (replacement is not null)
                        panel.Children[0] = replacement;
                }
                panel.Tag = newEl;
                return null;
            });
    }
}

// ─── SplitPanel component ───────────────────────────────────────────────────

internal sealed record SplitPanelProps(
    Element Left,
    Element Right,
    double InitialWidth = 280,
    double MinWidth = 120
);

internal sealed class SplitPanel : Component<SplitPanelProps>
{
    public override Element Render()
    {
        var widthRef = UseRef(Props.InitialWidth);
        var draggingRef = UseRef(false);
        var startXRef = UseRef(0.0);
        var startWidthRef = UseRef(0.0);

        // Pointer hover + imperative drag via the declarative pointer modifiers.
        // The drag still uses CapturePointer + direct grid-column mutation for
        // 60fps resize without re-renders; that's the same pattern as the
        // ReactorFiles SplitPanel.
        var splitter = new CursorBorderElement(Empty(), InputSystemCursorShape.SizeWestEast)
            { Background = DividerBrush() }
            .Width(4)
            .OnPointerEntered((s, _) =>
            {
                if (!draggingRef.Current)
                    ((CursorPanel)s).Background = HoverBrush();
            })
            .OnPointerExited((s, _) =>
            {
                if (!draggingRef.Current)
                    ((CursorPanel)s).Background = DividerBrush();
            })
            .OnPointerPressed((s, e) =>
            {
                var el = (UIElement)s;
                el.CapturePointer(e.Pointer);
                draggingRef.Current = true;
                startXRef.Current = e.GetCurrentPoint(null).Position.X;
                startWidthRef.Current = widthRef.Current;
                ((CursorPanel)s).Background = HoverBrush();
                e.Handled = true;
            })
            .OnPointerMoved((s, e) =>
            {
                if (!draggingRef.Current) return;
                var x = e.GetCurrentPoint(null).Position.X;
                var newWidth = Math.Max(Props.MinWidth, startWidthRef.Current + (x - startXRef.Current));
                widthRef.Current = newWidth;
                if (((FrameworkElement)s).Parent is Grid grid)
                    grid.ColumnDefinitions[0].Width = new GridLength(newWidth, GridUnitType.Pixel);
            })
            .OnPointerReleased((s, e) =>
            {
                var el = (UIElement)s;
                el.ReleasePointerCapture(e.Pointer);
                draggingRef.Current = false;
                e.Handled = true;
            });

        return Grid(
            [GridSize.Px(widthRef.Current), GridSize.Auto, GridSize.Star()],
            [GridSize.Star()],
            Props.Left.Grid(row: 0, column: 0),
            splitter.Grid(row: 0, column: 1),
            Props.Right.Grid(row: 0, column: 2)
        );
    }

    static Microsoft.UI.Xaml.Media.Brush DividerBrush() =>
        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];

    static Microsoft.UI.Xaml.Media.Brush HoverBrush() =>
        (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SubtleFillColorTertiaryBrush"];
}
