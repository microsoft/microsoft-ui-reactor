using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Navigation;

class PipsPagerPage : Component
{
    public override Element Render()
    {
        var (pageIndex, setPageIndex) = UseState(0);
        var (verticalIndex, setVerticalIndex) = UseState(0);
        var pageMessages = new[]
        {
            "Welcome to the first page.",
            "Review the second page.",
            "Explore details on page three.",
            "Compare options on page four.",
            "Finish on the last page."
        };

        return ScrollView(VStack(16,
            PageHeader("PipsPager", "PipsPager helps people navigate through a small set of pages."),

            SampleCard("Five-page pager",
                VStack(12,
                    PipsPager(5, pageIndex, index => setPageIndex(index)),
                    TextBlock($"Page {pageIndex + 1} of 5").SemiBold(),
                    Border(TextBlock(pageMessages.ElementAt(pageIndex)).Center())
                        .Height(96)
                        .Background(Theme.SubtleFill)
                        .CornerRadius(8)),
                sourceCode: @"PipsPager(5, pageIndex, index => setPageIndex(index))
TextBlock($""Page {pageIndex + 1} of 5"")
Border(TextBlock(pageMessages.ElementAt(pageIndex)).Center())"),

            SampleCard("Vertical pager",
                HStack(16,
                    PipsPager(4, verticalIndex, index => setVerticalIndex(index))
                        .Set(p => p.Orientation = Orientation.Vertical)
                        .Height(160),
                    TextBlock($"Vertical page {verticalIndex + 1}").VAlign(VerticalAlignment.Center)),
                sourceCode: @"PipsPager(4, verticalIndex, index => setVerticalIndex(index))
    .Set(p => p.Orientation = Orientation.Vertical)
    .Height(160)")
        ).Margin(36, 24, 36, 36));
    }
}
