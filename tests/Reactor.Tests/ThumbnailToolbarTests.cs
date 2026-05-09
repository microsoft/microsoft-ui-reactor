using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Hosting.Shell;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Pure-CLR coverage for thumbnail-toolbar validation. The shell COM dispatch
/// (ThumbBarAddButtons / UpdateButtons) is exercised by the Phase-9 selftest
/// matrix when a real HWND is available; here we cover the public-API guards.
/// </summary>
public class ThumbnailToolbarTests
{
    [Fact]
    public void Replace_TooManyButtons_Throws()
    {
        var state = new ThumbnailToolbarState(hwnd: 0);
        var buttons = Enumerable.Range(0, 8)
            .Select(i => new ThumbnailToolbarButton(
                Id: $"b{i}",
                Icon: WindowIcon.FromPath("a.ico"),
                Tooltip: "t",
                OnClick: () => { }))
            .ToArray();
        var ex = Assert.Throws<ArgumentException>(() => state.Replace(buttons));
        Assert.Contains("at most 7", ex.Message);
    }

    [Fact]
    public void Replace_DuplicateId_Throws()
    {
        var state = new ThumbnailToolbarState(hwnd: 0);
        var buttons = new[]
        {
            new ThumbnailToolbarButton("dup", WindowIcon.FromPath("a.ico"), "t", () => { }),
            new ThumbnailToolbarButton("dup", WindowIcon.FromPath("a.ico"), "t", () => { }),
        };
        var ex = Assert.Throws<ArgumentException>(() => state.Replace(buttons));
        Assert.Contains("duplicated", ex.Message);
    }

    [Fact]
    public void Replace_EmptyId_Throws()
    {
        var state = new ThumbnailToolbarState(hwnd: 0);
        var buttons = new[]
        {
            new ThumbnailToolbarButton("", WindowIcon.FromPath("a.ico"), "t", () => { }),
        };
        Assert.Throws<ArgumentException>(() => state.Replace(buttons));
    }

    [Fact]
    public void Replace_NullOnClick_Throws()
    {
        var state = new ThumbnailToolbarState(hwnd: 0);
        var buttons = new[]
        {
            new ThumbnailToolbarButton("a", WindowIcon.FromPath("a.ico"), "t", null!),
        };
        Assert.Throws<ArgumentException>(() => state.Replace(buttons));
    }

    [Fact]
    public void Replace_NullList_Throws()
    {
        var state = new ThumbnailToolbarState(hwnd: 0);
        Assert.Throws<ArgumentNullException>(() => state.Replace(null!));
    }

    [Fact]
    public void TryDispatchClick_OutOfRangeSlot_ReturnsFalse()
    {
        // No Replace called; slots empty.
        var state = new ThumbnailToolbarState(hwnd: 0);
        Assert.False(state.TryDispatchClick(0));
        Assert.False(state.TryDispatchClick(99));
    }

    [Fact]
    public void ThumbnailToolbarButton_RecordEquality_HoldsForSameValues()
    {
        var icon = WindowIcon.FromPath("a.ico");
        Action onClick = () => { };
        var a = new ThumbnailToolbarButton("id", icon, "tip", onClick);
        var b = new ThumbnailToolbarButton("id", icon, "tip", onClick);
        Assert.Equal(a, b);
    }
}
