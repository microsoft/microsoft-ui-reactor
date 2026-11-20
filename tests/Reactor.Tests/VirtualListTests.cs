using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for VirtualListElement, VirtualListRef, and Factories.
/// </summary>
public class VirtualListTests
{
    private sealed record ProbeElement(string Label) : Element;

    private static LazyVStackElement<int> Render(VirtualListComponent component)
    {
        component.Context.BeginRender(static () => { });
        return Assert.IsType<LazyVStackElement<int>>(component.Render());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(100_000)]
    public void Indices_AreCheckedAndEnumerateTheCompleteRange(int count)
    {
        IReadOnlyList<int> indices = new VirtualListIndices(count);
        Assert.Equal(Enumerable.Range(0, count), indices);
        Assert.Equal(Enumerable.Range(0, count),
            ((global::System.Collections.IEnumerable)indices).Cast<int>());
        Assert.Throws<ArgumentOutOfRangeException>(() => indices[-1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => indices[count]);
        if (count > 0)
        {
            Assert.Equal(0, indices[0]);
            Assert.Equal(count - 1, indices[count - 1]);
        }
    }

    [Fact]
    public void Indices_RejectNegativeCount_AndSupportMaximumCountWithoutMaterializing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VirtualListIndices(-1));
        var indices = new VirtualListIndices(int.MaxValue);
        Assert.Equal(int.MaxValue, indices.Count);
        Assert.Equal(int.MaxValue - 1, indices[int.MaxValue - 1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => indices[int.MaxValue]);
    }

    [Fact]
    public void Render_MemoizesOnlyTheIndices_AndRefreshesTheViewBuilder()
    {
        var component = new VirtualListComponent
        {
            Props = new() { ItemCount = 4, RenderItem = i => new ProbeElement($"old-{i}") },
        };
        var before = Render(component);
        component.Props = component.Props with
        {
            RenderItem = i => new ProbeElement($"new-{i}"),
            ItemHeight = 37,
            Spacing = 3,
        };
        var after = Render(component);
        Assert.IsType<VirtualListIndices>(after.Items);
        Assert.Same(before.Items, after.Items);
        Assert.Equal("old-2", Assert.IsType<ProbeElement>(before.BuildItemView(2)).Label);
        var row = Assert.IsType<ProbeElement>(after.BuildItemView(2));
        Assert.Equal("new-2", row.Label);
        Assert.Equal(37, row.Modifiers?.Layout?.Height);
        Assert.Equal(3, after.Spacing);
        Assert.True(VirtualListIndices.IsDefaultKeySource(after));

        component.Props = component.Props with { ItemCount = 5 };
        var grown = Render(component);
        Assert.Equal(5, grown.Items.Count);
        Assert.NotSame(after.Items, grown.Items);
        Assert.Equal(4, after.Items.Count);
    }

    [Fact]
    public void DefaultKeyInvariant_RejectsReplacedItemsOrSelector()
    {
        var component = new VirtualListComponent
        {
            Props = new() { ItemCount = 3, RenderItem = i => new ProbeElement(i.ToString()) },
        };
        var original = Render(component);
        Assert.True(VirtualListIndices.IsDefaultKeySource(original));
        Assert.False(VirtualListIndices.IsDefaultKeySource(original with { Items = new[] { 2, 1, 0 } }));
        Assert.False(VirtualListIndices.IsDefaultKeySource(original with { KeySelector = i => i.ToString() }));

        var keys = new[] { "placeholder", "a", "b" };
        Func<int, string> selector = i => keys[i];
        component.Props = component.Props with { GetItemKey = selector };
        var custom = Render(component);
        Assert.Same(original.Items, custom.Items);
        Assert.Same(selector, custom.KeySelector);
        Assert.False(VirtualListIndices.IsDefaultKeySource(custom));
        keys[0] = "loaded";
        Assert.Equal("loaded", custom.GetKeyAt(0));
        component.Props = component.Props with { GetItemKey = null };
        Assert.True(VirtualListIndices.IsDefaultKeySource(Render(component)));
    }

    // ── Element construction ────────────────────────────────────────

    [Fact]
    public void Element_Construction_With_Required_Properties()
    {
        var el = new VirtualListElement
        {
            ItemCount = 100,
            RenderItem = i => TextBlock($"Item {i}"),
        };

        Assert.Equal(100, el.ItemCount);
        Assert.NotNull(el.RenderItem);
        Assert.Null(el.GetItemKey);
        Assert.Null(el.ItemHeight);
        Assert.Equal(40, el.EstimatedItemHeight); // default
        Assert.Equal(0, el.Spacing); // default
        Assert.Null(el.Ref);
        Assert.Null(el.OnVisibleRangeChanged);
    }

    [Fact]
    public void Element_With_All_Properties()
    {
        Func<int, Element> renderItem = i => TextBlock($"Row {i}");
        Func<int, string> getKey = i => $"key-{i}";
        Action<VirtualListRef> onRef = _ => { };
        Action<int, int> onRange = (_, _) => { };

        var el = new VirtualListElement
        {
            ItemCount = 500,
            RenderItem = renderItem,
            GetItemKey = getKey,
            ItemHeight = 48,
            EstimatedItemHeight = 48,
            Spacing = 2,
            Ref = onRef,
            OnVisibleRangeChanged = onRange,
        };

        Assert.Equal(500, el.ItemCount);
        Assert.Same(renderItem, el.RenderItem);
        Assert.Same(getKey, el.GetItemKey);
        Assert.Equal(48.0, el.ItemHeight);
        Assert.Equal(48.0, el.EstimatedItemHeight);
        Assert.Equal(2.0, el.Spacing);
        Assert.Same(onRef, el.Ref);
        Assert.Same(onRange, el.OnVisibleRangeChanged);
    }

    [Fact]
    public void Element_With_Expression_Preserves_Unchanged()
    {
        var original = new VirtualListElement
        {
            ItemCount = 100,
            RenderItem = i => TextBlock($"Item {i}"),
            Spacing = 4,
        };

        var modified = original with { ItemCount = 200 };

        Assert.Equal(200, modified.ItemCount);
        Assert.Equal(4.0, modified.Spacing); // unchanged
        Assert.Same(original.RenderItem, modified.RenderItem); // unchanged
    }

    // ── Fixed-height mode ──────────────────────────────────────────

    [Fact]
    public void Fixed_Height_Mode_Sets_ItemHeight()
    {
        var el = new VirtualListElement
        {
            ItemCount = 1000,
            RenderItem = i => TextBlock($"Item {i}"),
            ItemHeight = 32,
        };

        Assert.Equal(32.0, el.ItemHeight);
        Assert.True(el.ItemHeight.HasValue);
    }

    [Fact]
    public void Variable_Height_Mode_Uses_EstimatedHeight()
    {
        var el = new VirtualListElement
        {
            ItemCount = 1000,
            RenderItem = i => TextBlock($"Item {i}"),
            EstimatedItemHeight = 60,
        };

        Assert.Null(el.ItemHeight);
        Assert.Equal(60.0, el.EstimatedItemHeight);
    }

    // ── RenderItem callback ────────────────────────────────────────

    [Fact]
    public void RenderItem_Called_With_Correct_Index()
    {
        var calledIndices = new List<int>();
        var el = new VirtualListElement
        {
            ItemCount = 5,
            RenderItem = i =>
            {
                calledIndices.Add(i);
                return TextBlock($"Item {i}");
            },
        };

        // Simulate calls for visible items
        for (int i = 0; i < 5; i++)
        {
            var result = el.RenderItem(i);
            Assert.NotNull(result);
            Assert.IsType<TextBlockElement>(result);
        }

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, calledIndices);
    }

    [Fact]
    public void GetItemKey_Returns_Stable_Keys()
    {
        var el = new VirtualListElement
        {
            ItemCount = 3,
            RenderItem = i => TextBlock($"Item {i}"),
            GetItemKey = i => $"item-{i * 10}",
        };

        Assert.Equal("item-0", el.GetItemKey!(0));
        Assert.Equal("item-10", el.GetItemKey!(1));
        Assert.Equal("item-20", el.GetItemKey!(2));
    }

    // ── DSL factory ────────────────────────────────────────────────

    [Fact]
    public void DSL_VirtualList_Creates_ComponentElement()
    {
        var element = VirtualList(
            itemCount: 50,
            renderItem: i => TextBlock($"Item {i}"));

        // Returns ComponentElement<VirtualListElement> (issue #308 — typed
        // wrapper that preserves the props type for with-expression mutation).
        Assert.IsType<ComponentElement<VirtualListElement>>(element);
    }

    [Fact]
    public void DSL_VirtualList_With_FixedHeight_Creates_Element()
    {
        var element = VirtualList(
            itemCount: 1000,
            renderItem: i => TextBlock($"Row {i}"),
            itemHeight: 32,
            spacing: 1);

        Assert.IsType<ComponentElement<VirtualListElement>>(element);
    }

    [Fact]
    public void DSL_VirtualList_With_Key_Selector_Creates_Element()
    {
        var element = VirtualList(
            itemCount: 100,
            renderItem: i => TextBlock($"Item {i}"),
            getItemKey: i => $"key-{i}");

        Assert.IsType<ComponentElement<VirtualListElement>>(element);
    }

    // ── VirtualListRef ─────────────────────────────────────────────

    [Fact]
    public void VirtualListRef_With_Null_ScrollViewer_Is_Safe()
    {
        // VirtualListRef should handle null gracefully (no-op)
        var @ref = new VirtualListRef(null, null, 40);

        @ref.ScrollToIndex(10); // Should not throw
        Assert.Equal(0, @ref.ScrollOffset);

        @ref.RestoreScrollOffset(100); // Should not throw
    }

    [Fact]
    public void Ref_Callback_Receives_VirtualListRef()
    {
        VirtualListRef? captured = null;
        var el = new VirtualListElement
        {
            ItemCount = 100,
            RenderItem = i => TextBlock($"Item {i}"),
            Ref = r => captured = r,
        };

        Assert.NotNull(el.Ref);
    }

    // ── Visible range callback ─────────────────────────────────────

    [Fact]
    public void OnVisibleRangeChanged_Can_Be_Set()
    {
        var ranges = new List<(int First, int Last)>();
        var el = new VirtualListElement
        {
            ItemCount = 1000,
            RenderItem = i => TextBlock($"Item {i}"),
            OnVisibleRangeChanged = (first, last) => ranges.Add((first, last)),
        };

        Assert.NotNull(el.OnVisibleRangeChanged);
    }
}
