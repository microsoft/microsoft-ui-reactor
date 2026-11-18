using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Layout;

class AnnotatedScrollBarPage : Component
{
    public override Element Render()
    {
        var bar = this.UseElementRef<Microsoft.UI.Xaml.Controls.AnnotatedScrollBar>();
        var annotations = Enumerable.Range(0, 8)
            .Select(index => new ScrollAnnotation(index * 180, $"Section {index + 1}", $"Jump to section {index + 1}")).ToArray();
        var content = VStack(0, Enumerable.Range(1, 8).Select(index =>
            Border(VStack(8, TextBlock($"Section {index}").Bold(),
                TextBlock("Scroll with the wheel, drag the thumb, or click a label.").TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap))
                .Padding(16)).Height(180)).ToArray());
        return ScrollView(VStack(16,
            PageHeader("AnnotatedScrollBar", "Shows a scrollbar surface that can display app-defined annotations."),
            SampleCard("Connected scrolling with annotations",
                Grid(columns: [GridSize.Star(), GridSize.Px(100)], rows: [GridSize.Star()],
                    children:
                    [
                        (ScrollView(content)
                            with { VerticalScrollBarVisibility = ScrollingScrollBarVisibility.Hidden })
                            .VerticalScrollBar(bar).Grid(column: 0),
                        AnnotatedScrollBar().Ref(bar).Annotations(annotations)
                            .AutomationName("Sections").Grid(column: 1),
                    ]).Height(340),
                sourceCode: """
                var bar = this.UseElementRef<Microsoft.UI.Xaml.Controls.AnnotatedScrollBar>();
                Grid(columns: [GridSize.Star(), GridSize.Px(100)], rows: [GridSize.Star()],
                    children:
                    [
                        (ScrollView(content) with
                            { VerticalScrollBarVisibility = ScrollingScrollBarVisibility.Hidden })
                            .VerticalScrollBar(bar),
                        AnnotatedScrollBar().Ref(bar).Annotations(
                            [new ScrollAnnotation(0, "Start"), new ScrollAnnotation(540, "Chapter 2")])
                            .AutomationName("Sections").Grid(column: 1),
                    ]).Height(340)
                """),
            Caption("Offsets use content DIPs for connected scrolling. For a stable ordinal history rail over variable-height ItemsView rows, see Collections > Chat feed.")
        ).Margin(36, 24, 36, 36));
    }
}
