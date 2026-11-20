using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the bucketed <see cref="LayoutModifiers"/> sub-record introduced
/// by spec 034 §A. Mirrors <see cref="VisualModifiersTests"/>.
/// </summary>
public class LayoutModifiersTests
{
    [Fact]
    public void Merge_With_Empty_Other_Returns_This_Equivalent()
    {
        var a = new LayoutModifiers { Padding = new Thickness(2) };
        var merged = a.Merge(new LayoutModifiers());
        Assert.Equal(a, merged);
    }

    [Fact]
    public void Merge_Partial_Other_Takes_Other_Where_Set()
    {
        var a = new LayoutModifiers { Padding = new Thickness(2), Width = 10 };
        var b = new LayoutModifiers { Padding = new Thickness(8) };
        var merged = a.Merge(b);
        Assert.Equal(new Thickness(8), merged.Padding);
        Assert.Equal(10, merged.Width);
    }

    [Fact]
    public void Merge_Full_Other_Returns_Other_Equivalent()
    {
        var a = new LayoutModifiers { Padding = new Thickness(2) };
        var b = new LayoutModifiers
        {
            Margin = new Thickness(1),
            Padding = new Thickness(3),
            Width = 5,
            Height = 6,
            MinWidth = 1,
            MinHeight = 2,
            MaxWidth = 100,
            MaxHeight = 200,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsVisible = true,
            MarginInlineStart = 1,
            MarginInlineEnd = 2,
            PaddingInlineStart = 3,
            PaddingInlineEnd = 4,
            BorderInlineStart = new Thickness(0.5),
            RequestedTheme = ElementTheme.Dark,
        };
        var merged = a.Merge(b);
        Assert.Equal(b, merged);
    }

    [Fact]
    public void Equal_Records_Have_Equal_HashCode()
    {
        var a = new LayoutModifiers { Padding = new Thickness(2), Width = 10 };
        var b = new LayoutModifiers { Padding = new Thickness(2), Width = 10 };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Different_Records_Are_Not_Equal()
    {
        var a = new LayoutModifiers { Padding = new Thickness(2) };
        var b = new LayoutModifiers { Padding = new Thickness(3) };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Merge_With_Both_Empty_Is_Empty()
    {
        var merged = new LayoutModifiers().Merge(new LayoutModifiers());
        Assert.Equal(new LayoutModifiers(), merged);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Fluent_Updates_Match_Merge_Without_Changing_Previous_Snapshots(int seedKind)
    {
        var populated = new TaggedLayout("layout")
        {
            Margin = new Thickness(1), Padding = new Thickness(2),
            Width = 10, Height = 20, MinWidth = 3, MinHeight = 4,
            MaxWidth = 100, MaxHeight = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Right,
            VerticalContentAlignment = VerticalAlignment.Bottom,
            IsVisible = true, RequestedTheme = ElementTheme.Dark,
            MarginInlineStart = 5, MarginInlineEnd = 6,
            PaddingInlineStart = 7, PaddingInlineEnd = 8,
            BorderInlineStart = new Thickness(9),
        };
        ElementModifiers? mods = seedKind == 0 ? null : new TaggedModifiers("parent")
        {
            Layout = seedKind switch { 1 => null, 2 => new LayoutModifiers(), _ => populated },
            Visual = new VisualModifiers { Opacity = 0.7 },
            FontSize = 17,
            OnTapped = static (_, _) => { },
        };
        var source = new TestElement("leaf") { Key = "key", Modifiers = mods };
        var snapshot = source with
        {
            Modifiers = mods is null ? null : mods with
            {
                Layout = mods.Layout is { } layout ? layout with { } : null,
            },
        };

        var updates = new (Func<TestElement, TestElement> Apply, LayoutModifiers Delta)[]
        {
            (el => el.Margin(0), new() { Margin = new Thickness(0) }),
            (el => el.Margin(11, 12), new() { Margin = new Thickness(11, 12, 11, 12) }),
            (el => el.Margin(top: 13), new() { Margin = new Thickness(0, 13, 0, 0) }),
            (el => el.Margin(new Thickness(11, 12, 13, 14)), new() { Margin = new Thickness(11, 12, 13, 14) }),
            (el => el.Padding(0), new() { Padding = new Thickness(0) }),
            (el => el.Padding(11, 12), new() { Padding = new Thickness(11, 12, 11, 12) }),
            (el => el.Padding(bottom: 13), new() { Padding = new Thickness(0, 0, 0, 13) }),
            (el => el.Padding(new Thickness(11, 12, 13, 14)), new() { Padding = new Thickness(11, 12, 13, 14) }),
            (el => el.MarginInlineStart(0), new() { MarginInlineStart = 0 }),
            (el => el.MarginInlineEnd(0), new() { MarginInlineEnd = 0 }),
            (el => el.PaddingInlineStart(0), new() { PaddingInlineStart = 0 }),
            (el => el.PaddingInlineEnd(0), new() { PaddingInlineEnd = 0 }),
            (el => el.BorderInlineStart(new Thickness(0)), new() { BorderInlineStart = new Thickness(0) }),
            (el => el.Width(double.NaN), new() { Width = double.NaN }),
            (el => el.Height(0), new() { Height = 0 }),
            (el => el.Size(31, 32), new() { Width = 31, Height = 32 }),
            (el => el.MinWidth(0), new() { MinWidth = 0 }),
            (el => el.MinHeight(0), new() { MinHeight = 0 }),
            (el => el.MaxWidth(double.PositiveInfinity), new() { MaxWidth = double.PositiveInfinity }),
            (el => el.MaxHeight(double.PositiveInfinity), new() { MaxHeight = double.PositiveInfinity }),
            (el => el.HAlign(HorizontalAlignment.Stretch), new() { HorizontalAlignment = HorizontalAlignment.Stretch }),
            (el => el.VAlign(VerticalAlignment.Stretch), new() { VerticalAlignment = VerticalAlignment.Stretch }),
            (el => el.HorizontalAlignment(HorizontalAlignment.Center), new() { HorizontalAlignment = HorizontalAlignment.Center }),
            (el => el.VerticalAlignment(VerticalAlignment.Center), new() { VerticalAlignment = VerticalAlignment.Center }),
            (el => el.HorizontalContentAlignment(HorizontalAlignment.Stretch), new() { HorizontalContentAlignment = HorizontalAlignment.Stretch }),
            (el => el.VerticalContentAlignment(VerticalAlignment.Stretch), new() { VerticalContentAlignment = VerticalAlignment.Stretch }),
            (el => el.Center(), new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }),
            (el => el.RequestedTheme(ElementTheme.Default), new() { RequestedTheme = ElementTheme.Default }),
            (el => el.IsVisible(false), new() { IsVisible = false }),
        };

        foreach (var (apply, delta) in updates)
        {
            var expectedMods = mods?.Merge(new ElementModifiers { Layout = delta })
                ?? new ElementModifiers { Layout = delta };
            var result = apply(source);
            Assert.Equal(source with { Modifiers = expectedMods }, result);
            Assert.Equal(snapshot, source);
            Assert.NotSame(source, result);
            Assert.Same(mods?.Visual, result.Modifiers!.Visual);
            if (seedKind == 3)
            {
                Assert.IsType<TaggedModifiers>(result.Modifiers);
                Assert.IsType<TaggedLayout>(result.Modifiers.Layout);
            }
        }
    }

    [Fact]
    public void Fluent_Chain_Preserves_Factory_Type_And_Explicitly_Unset_Fields()
    {
        var original = TextBlock("cell").Padding(8).Size(120, 32).Center();
        var unset = original with
        {
            Modifiers = original.Modifiers! with { Width = null, HorizontalAlignment = null },
        };
        TextBlockElement result = unset.Height(0).Padding(0).VAlign(VerticalAlignment.Bottom);
        Assert.Null(result.Modifiers!.Width);
        Assert.Null(result.Modifiers.HorizontalAlignment);
        Assert.Equal(0, result.Modifiers.Height);
        Assert.Equal(new Thickness(0), result.Modifiers.Padding);
        Assert.Equal(VerticalAlignment.Bottom, result.Modifiers.VerticalAlignment);
        Assert.Equal(120, original.Modifiers!.Width);
        Assert.Equal(32, original.Modifiers.Height);
        Assert.Equal(new Thickness(8), original.Modifiers.Padding);
        Assert.Equal(HorizontalAlignment.Center, original.Modifiers.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, unset.Modifiers!.VerticalAlignment);
        Assert.Equal(41, result.Width(40).Width(41).Modifiers!.Width);
        Assert.Null(TextBlock("fresh").Height(32).Modifiers!.Width);
    }

    [Fact]
    public void Fluent_Layout_Updates_Allocate_Only_The_Returned_Records()
    {
        var source = new TestElement("cell")
        {
            Modifiers = new ElementModifiers { Layout = new LayoutModifiers { Padding = new Thickness(8) } },
        };
        var directBytes = Measure(() => source with
        {
            Modifiers = source.Modifiers with { Layout = source.Modifiers.Layout with { Width = 120 } },
        });
        Assert.True(directBytes > 0);
        Assert.Equal(directBytes, Measure(() => source.Width(120)));
        Assert.Equal(directBytes, Measure(() => source.Size(120, 32)));
        Assert.Equal(directBytes, Measure(() => source.Center()));

        var fresh = new TestElement("cell");
        var firstWriteBytes = Measure(() => fresh with
        {
            Modifiers = new ElementModifiers { Layout = new LayoutModifiers { Width = 120 } },
        });
        Assert.Equal(firstWriteBytes, Measure(() => fresh.Width(120)));
    }

    private static long Measure(Func<TestElement> build)
    {
        var sink = new TestElement[256];
        for (var i = 0; i < sink.Length; i++)
            sink[i] = build();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < sink.Length; i++)
            sink[i] = build();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(sink);
        return allocated;
    }

    private sealed record TestElement(string Label) : Element;
    private sealed record TaggedLayout(string Tag) : LayoutModifiers;
    private sealed record TaggedModifiers(string Tag) : ElementModifiers;
}
