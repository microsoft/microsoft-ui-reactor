using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Behavior tests for the <see cref="UseMemoCellsExtensions"/> hook trio
/// (spec 034 §C). Operates on <see cref="RenderContext"/> directly — no
/// reconciler, no WinUI controls. Element constructors that require a
/// XAML application context are avoided; cells are stand-in
/// <see cref="DivElement"/> instances.
/// </summary>
public class UseMemoCellsTests
{
    private static RenderContext NewCtx()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });
        return ctx;
    }

    private static Element MakeCell(int v)
        => new DivElement(Children: Array.Empty<Element>(), Content: $"v={v}");

    private record DivElement(Element[] Children, string Content) : Element;

    // ════════════════════════════════════════════════════════════════
    //  UseMemoCells (base)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void First_Render_Builds_All_Cells()
    {
        var ctx = NewCtx();
        int builds = 0;
        var children = ctx.UseMemoCells<int>(
            new[] { 1, 2, 3 },
            (item, i) => { builds++; return MakeCell(item); });

        Assert.Equal(3, children.Length);
        Assert.Equal(3, builds);
    }

    [Fact]
    public void Same_Items_Same_Deps_Reuses_All_Cells()
    {
        var ctx = NewCtx();
        var items = new[] { 1, 2, 3 };
        int builds = 0;
        var first = ctx.UseMemoCells<int>(items, (item, i) => { builds++; return MakeCell(item); }, "deps");
        ctx.BeginRender(() => { });
        var second = ctx.UseMemoCells<int>(items, (item, i) => { builds++; return MakeCell(item); }, "deps");

        Assert.Equal(3, builds); // only first render built
        for (int i = 0; i < 3; i++)
            Assert.Same(first[i], second[i]);
    }

    [Fact]
    public void Partial_Item_Change_Rebuilds_Only_Changed_Indices()
    {
        var ctx = NewCtx();
        int builds = 0;
        var first = ctx.UseMemoCells<int>(new[] { 1, 2, 3 }, (item, i) => { builds++; return MakeCell(item); }, "deps");
        ctx.BeginRender(() => { });
        var second = ctx.UseMemoCells<int>(new[] { 1, 99, 3 }, (item, i) => { builds++; return MakeCell(item); }, "deps");

        Assert.Equal(3 + 1, builds);          // 3 first render + 1 second
        Assert.Same(first[0], second[0]);
        Assert.NotSame(first[1], second[1]);
        Assert.Same(first[2], second[2]);
    }

    [Fact]
    public void Deps_Change_Invalidates_Entire_Memo()
    {
        var ctx = NewCtx();
        int builds = 0;
        var first = ctx.UseMemoCells<int>(new[] { 1, 2, 3 }, (item, i) => { builds++; return MakeCell(item); }, "themeA");
        ctx.BeginRender(() => { });
        var second = ctx.UseMemoCells<int>(new[] { 1, 2, 3 }, (item, i) => { builds++; return MakeCell(item); }, "themeB");

        Assert.Equal(6, builds);
        for (int i = 0; i < 3; i++)
            Assert.NotSame(first[i], second[i]);
    }

    [Fact]
    public void Zero_Deps_Allowed_At_Runtime()
    {
        var ctx = NewCtx();
        var children = ctx.UseMemoCells<int>(new[] { 1 }, (item, i) => MakeCell(item));
        Assert.Single(children);
    }

    [Fact]
    public void Null_Items_Throws_ArgumentNullException()
    {
        var ctx = NewCtx();
        Assert.Throws<ArgumentNullException>(() =>
            ctx.UseMemoCells<int>(null!, (item, i) => MakeCell(item)));
    }

    [Fact]
    public void Null_Builder_Throws_ArgumentNullException()
    {
        var ctx = NewCtx();
        Assert.Throws<ArgumentNullException>(() =>
            ctx.UseMemoCells<int>(new[] { 1 }, null!));
    }

    [Fact]
    public void Explicit_Null_Deps_Array_Throws()
    {
        var ctx = NewCtx();
        Assert.Throws<ArgumentNullException>(() =>
            ctx.UseMemoCells<int>(new[] { 1 }, (item, i) => MakeCell(item), (object[])null!));
    }

    [Fact]
    public void Item_Count_Grows_New_Tail_Builds()
    {
        var ctx = NewCtx();
        int builds = 0;
        var first = ctx.UseMemoCells<int>(new[] { 1, 2 }, (item, i) => { builds++; return MakeCell(item); });
        ctx.BeginRender(() => { });
        var second = ctx.UseMemoCells<int>(new[] { 1, 2, 3 }, (item, i) => { builds++; return MakeCell(item); });

        Assert.Equal(2 + 1, builds);
        Assert.Same(first[0], second[0]);
        Assert.Same(first[1], second[1]);
        Assert.Equal(3, second.Length);
    }

    [Fact]
    public void Item_Count_Shrinks_Trailing_Children_Released()
    {
        var ctx = NewCtx();
        var first = ctx.UseMemoCells<int>(new[] { 1, 2, 3 }, (item, i) => MakeCell(item));
        ctx.BeginRender(() => { });
        var second = ctx.UseMemoCells<int>(new[] { 1, 2 }, (item, i) => MakeCell(item));

        Assert.Equal(2, second.Length);
        Assert.Same(first[0], second[0]);
        Assert.Same(first[1], second[1]);
    }

    [Fact]
    public void Hook_Order_Stable_Between_UseState_Calls()
    {
        var ctx = NewCtx();
        var (a, _) = ctx.UseState(0);
        var first = ctx.UseMemoCells<int>(new[] { 1 }, (item, i) => MakeCell(item));
        var (b, _) = ctx.UseState(0);

        ctx.BeginRender(() => { });
        var (a2, _) = ctx.UseState(0);
        var second = ctx.UseMemoCells<int>(new[] { 1 }, (item, i) => MakeCell(item));
        var (b2, _) = ctx.UseState(0);

        Assert.Same(first[0], second[0]);
        Assert.Equal(0, a2);
        Assert.Equal(0, b2);
    }

    // ════════════════════════════════════════════════════════════════
    //  UseMemoCellsByKey
    // ════════════════════════════════════════════════════════════════

    private record KeyedItem(int Id, string Body);

    [Fact]
    public void ByKey_Same_Key_And_Value_Reuses_Cell()
    {
        var ctx = NewCtx();
        var first = ctx.UseMemoCellsByKey<KeyedItem, int>(
            new[] { new KeyedItem(1, "a"), new KeyedItem(2, "b") },
            x => x.Id,
            (item, i) => MakeCell(item.Id));
        ctx.BeginRender(() => { });
        var second = ctx.UseMemoCellsByKey<KeyedItem, int>(
            new[] { new KeyedItem(1, "a"), new KeyedItem(2, "b") },
            x => x.Id,
            (item, i) => MakeCell(item.Id));

        Assert.Same(first[0], second[0]);
        Assert.Same(first[1], second[1]);
    }

    [Fact]
    public void ByKey_Same_Key_Different_Value_Rebuilds()
    {
        var ctx = NewCtx();
        var first = ctx.UseMemoCellsByKey<KeyedItem, int>(
            new[] { new KeyedItem(1, "a") },
            x => x.Id,
            (item, i) => MakeCell(item.Id));
        ctx.BeginRender(() => { });
        var second = ctx.UseMemoCellsByKey<KeyedItem, int>(
            new[] { new KeyedItem(1, "DIFFERENT") },
            x => x.Id,
            (item, i) => MakeCell(item.Id));

        Assert.NotSame(first[0], second[0]);
    }

    [Fact]
    public void ByKey_Reorder_Reuses_Cells_In_New_Positions()
    {
        var ctx = NewCtx();
        var a = new KeyedItem(1, "a");
        var b = new KeyedItem(2, "b");
        var first = ctx.UseMemoCellsByKey<KeyedItem, int>(
            new[] { a, b },
            x => x.Id,
            (item, i) => MakeCell(item.Id));
        ctx.BeginRender(() => { });
        var second = ctx.UseMemoCellsByKey<KeyedItem, int>(
            new[] { b, a },
            x => x.Id,
            (item, i) => MakeCell(item.Id));

        Assert.Same(first[1], second[0]);   // b moved from idx 1 → 0
        Assert.Same(first[0], second[1]);   // a moved from idx 0 → 1
    }

    [Fact]
    public void ByKey_Null_KeySelector_Throws()
    {
        var ctx = NewCtx();
        Assert.Throws<ArgumentNullException>(() =>
            ctx.UseMemoCellsByKey<KeyedItem, int>(
                new[] { new KeyedItem(1, "a") },
                null!,
                (item, i) => MakeCell(item.Id)));
    }

    [Fact]
    public void ByKey_Duplicate_Keys_Last_Write_Wins()
    {
        var ctx = NewCtx();
        // Two items with the same key (Id=1). Lookup table should map
        // Id=1 → index 1 (last write).
        var first = ctx.UseMemoCellsByKey<KeyedItem, int>(
            new[] { new KeyedItem(1, "a"), new KeyedItem(1, "b") },
            x => x.Id,
            (item, i) => MakeCell(item.Id));

        ctx.BeginRender(() => { });
        // Re-render with a single item matching the second one's value:
        // it should reuse the cell at index 1 (last-write-wins).
        int builds = 0;
        var second = ctx.UseMemoCellsByKey<KeyedItem, int>(
            new[] { new KeyedItem(1, "b") },
            x => x.Id,
            (item, i) => { builds++; return MakeCell(item.Id); });

        Assert.Same(first[1], second[0]);
        Assert.Equal(0, builds); // reused
    }

    // ════════════════════════════════════════════════════════════════
    //  UseMemoCellsByIndex
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ByIndex_Empty_ChangedIndices_Same_Items_Full_Reuse()
    {
        var ctx = NewCtx();
        var items = new[] { 10, 20, 30 };
        var first = ctx.UseMemoCellsByIndex<int>(items, Array.Empty<int>(), (item, i) => MakeCell(item));
        ctx.BeginRender(() => { });
        int builds = 0;
        var second = ctx.UseMemoCellsByIndex<int>(items, Array.Empty<int>(), (item, i) => { builds++; return MakeCell(item); });

        Assert.Equal(0, builds);
        for (int i = 0; i < 3; i++) Assert.Same(first[i], second[i]);
    }

    [Fact]
    public void ByIndex_Single_Index_Change_Only_That_Cell_Rebuilds()
    {
        var ctx = NewCtx();
        var first = ctx.UseMemoCellsByIndex<int>(new[] { 10, 20, 30 }, Array.Empty<int>(), (item, i) => MakeCell(item));
        ctx.BeginRender(() => { });
        int builds = 0;
        var second = ctx.UseMemoCellsByIndex<int>(new[] { 10, 99, 30 }, new[] { 1 }, (item, i) => { builds++; return MakeCell(item); });

        Assert.Equal(1, builds);
        Assert.Same(first[0], second[0]);
        Assert.NotSame(first[1], second[1]);
        Assert.Same(first[2], second[2]);
    }

    [Fact]
    public void ByIndex_Out_Of_Range_Throws()
    {
        var ctx = NewCtx();
        ctx.UseMemoCellsByIndex<int>(new[] { 1, 2, 3 }, Array.Empty<int>(), (item, i) => MakeCell(item));
        ctx.BeginRender(() => { });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ctx.UseMemoCellsByIndex<int>(new[] { 1, 2, 3 }, new[] { 5 }, (item, i) => MakeCell(item)));
    }

    [Fact]
    public void ByIndex_Negative_Index_Throws()
    {
        var ctx = NewCtx();
        ctx.UseMemoCellsByIndex<int>(new[] { 1, 2, 3 }, Array.Empty<int>(), (item, i) => MakeCell(item));
        ctx.BeginRender(() => { });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ctx.UseMemoCellsByIndex<int>(new[] { 1, 2, 3 }, new[] { -1 }, (item, i) => MakeCell(item)));
    }

    [Fact]
    public void ByIndex_Item_Count_Change_Falls_Back_To_Full_Rebuild()
    {
        var ctx = NewCtx();
        var first = ctx.UseMemoCellsByIndex<int>(new[] { 1, 2 }, Array.Empty<int>(), (item, i) => MakeCell(item));
        ctx.BeginRender(() => { });
        int builds = 0;
        var second = ctx.UseMemoCellsByIndex<int>(new[] { 1, 2, 3 }, Array.Empty<int>(), (item, i) => { builds++; return MakeCell(item); });

        Assert.Equal(3, builds);
        Assert.Equal(3, second.Length);
    }

    [Fact]
    public void ByIndex_Null_ChangedIndices_Throws()
    {
        var ctx = NewCtx();
        Assert.Throws<ArgumentNullException>(() =>
            ctx.UseMemoCellsByIndex<int>(new[] { 1 }, null!, (item, i) => MakeCell(item)));
    }
}
