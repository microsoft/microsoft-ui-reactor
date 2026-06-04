using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 050 R5 regression surface: a sibling re-render that omits SelectedIndex
/// must keep the prop Unset so the ListView/GridView handlers leave user
/// selection authority with WinUI.
/// </summary>
public class ListGridSelectedIndexOptionalRegressionTests
{
    [Fact]
    public void ListView_SiblingRerender_OmittedSelectedIndex_RemainsUnset()
    {
        var first = new ListViewElement([new EmptyElement()]);
        var rerender = first with { Header = "sibling changed" };

        Assert.False(first.SelectedIndex.HasValue);
        Assert.False(rerender.SelectedIndex.HasValue);
    }

    [Fact]
    public void GridView_SiblingRerender_OmittedSelectedIndex_RemainsUnset()
    {
        var first = new GridViewElement([new EmptyElement()]);
        var rerender = first with { Header = "sibling changed" };

        Assert.False(first.SelectedIndex.HasValue);
        Assert.False(rerender.SelectedIndex.HasValue);
    }
}
