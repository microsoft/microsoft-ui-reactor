using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Regression coverage for the perf "restore diff skip-path + cut DSL/element
/// per-render allocations" work. These are pure C# record/equality tests — no
/// WinUI thread, and intentionally brush-free (constructing a SolidColorBrush
/// requires a UI thread, so the #168 brush-cache is validated by the selftest /
/// AppTests tiers that render on a real thread).
///
/// The headline behaviour being locked down: an interactive grid cell whose only
/// per-render churn is handler identity / appended setters / re-applied attached
/// data must once again hit the <see cref="Element.ShallowEquals"/> skip path,
/// while any change that actually affects rendering still declines it.
/// </summary>
public class PerfDiffSkipPathTests
{
    // ════════════════════════════════════════════════════════════════
    //  FLAGSHIP-1 — interactive elements regain the skip fast-path
    // ════════════════════════════════════════════════════════════════

    // A reference-stable handler (hoisted/memoized/method-group). The reconciler's
    // ModifierEventHandlerState.Current* already holds this exact delegate, so the
    // skip path dispatches identically — skipping is safe.
    private static readonly Action<object, TappedRoutedEventArgs> SharedTap = (s, e) => { };

    [Fact]
    public void ShallowEquals_True_For_Interactive_Element_With_Stable_Handler()
    {
        var a = TextBlock("AAPL").OnTapped(SharedTap);
        var b = TextBlock("AAPL").OnTapped(SharedTap);
        // Pre-fix this returned false (any handler present ⇒ never skip), forcing a
        // full Update on every selectable/interactive cell every render.
        Assert.True(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_False_When_Handler_Identity_Changes()
    {
        Action<object, TappedRoutedEventArgs> h1 = (s, e) => { };
        Action<object, TappedRoutedEventArgs> h2 = (s, e) => { };
        var a = TextBlock("AAPL").OnTapped(h1);
        var b = TextBlock("AAPL").OnTapped(h2);
        // Distinct delegate instances (e.g. a freshly captured closure each render)
        // must decline the skip so Update re-wires Current* to the new capture.
        Assert.False(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ShallowEquals_False_When_Handler_Added_Or_Removed()
    {
        var plain = TextBlock("AAPL");
        var interactive = TextBlock("AAPL").OnTapped(SharedTap);
        // null → non-null presence change must run Update so the trampoline subscribes.
        Assert.False(Element.ShallowEquals(plain, interactive));
        Assert.False(Element.ShallowEquals(interactive, plain));
    }

    // ════════════════════════════════════════════════════════════════
    //  FLAGSHIP-2 — an unchanged .Set(...) chain preserves skip-equality
    // ════════════════════════════════════════════════════════════════

    // Same source location, non-capturing lambda → the compiler caches a single
    // static delegate, so both renders' Setters arrays hold the identical instance.
    private static Element BuildWithStaticSetter() =>
        TextBlock("Hdr").Set(tb => tb.FontSize = 12);

    // Captures `size` → a fresh display-class + delegate per call.
    private static Element BuildWithCapturingSetter(double size) =>
        TextBlock("Hdr").Set(tb => tb.FontSize = size);

    [Fact]
    public void ShallowEquals_True_For_Unchanged_Set_Chain()
    {
        // The fluent `.Set` helper appends to a fresh array each render, so the old
        // ReferenceEquals(Setters) always failed. Element-wise reference compare now
        // restores the skip when the chain (and its cached static lambda) is unchanged.
        Assert.True(Element.ShallowEquals(BuildWithStaticSetter(), BuildWithStaticSetter()));
    }

    [Fact]
    public void ShallowEquals_False_For_Capturing_Set_Chain()
    {
        // A capturing setter allocates a new delegate per render, so it correctly
        // declines the fast-path even when the captured value is equal.
        Assert.False(Element.ShallowEquals(BuildWithCapturingSetter(12), BuildWithCapturingSetter(12)));
    }

    // ════════════════════════════════════════════════════════════════
    //  #159 — ModifiersEqual hoists Layout/Visual sub-records (value compare)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ModifiersEqual_True_For_Identical_Layout_And_Visual_Chain()
    {
        var a = TextBlock("x").Width(120).Margin(8).Opacity(0.5).Scale(1.5f);
        var b = TextBlock("x").Width(120).Margin(8).Opacity(0.5).Scale(1.5f);
        Assert.True(Element.ModifiersEqual(a.Modifiers, b.Modifiers));
        Assert.True(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void ModifiersEqual_False_When_Layout_Field_Differs()
    {
        var a = TextBlock("x").Width(120);
        var b = TextBlock("x").Width(121);
        Assert.False(Element.ModifiersEqual(a.Modifiers, b.Modifiers));
    }

    [Fact]
    public void ModifiersEqual_False_When_RequestedTheme_Differs()
    {
        // RequestedTheme lives in LayoutModifiers; the hoisted record value-equality
        // now covers it (the historical explicit field list omitted it).
        var a = TextBlock("x").RequestedTheme(ElementTheme.Dark);
        var b = TextBlock("x").RequestedTheme(ElementTheme.Light);
        Assert.False(Element.ModifiersEqual(a.Modifiers, b.Modifiers));
    }

    [Fact]
    public void ModifiersEqual_False_When_Visual_Opacity_Differs()
    {
        var a = TextBlock("x").Opacity(1.0);
        var b = TextBlock("x").Opacity(0.25);
        Assert.False(Element.ModifiersEqual(a.Modifiers, b.Modifiers));
    }

    [Fact]
    public void ModifiersEqual_False_When_Visual_Scale_Differs()
    {
        // Scale lives in VisualModifiers and was historically omitted from the
        // per-field compare (a latent skip bug); it must now be honored.
        var a = TextBlock("x").Scale(1.0f);
        var b = TextBlock("x").Scale(2.0f);
        Assert.False(Element.ModifiersEqual(a.Modifiers, b.Modifiers));
    }

    // ════════════════════════════════════════════════════════════════
    //  #155 — Attached single-slot dictionary (per-cell .Grid alloc cut)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Grid_Attached_RoundTrips_Through_SingleAttachedDictionary()
    {
        var el = TextBlock("cell").Grid(2, 3);
        Assert.NotNull(el.Attached);
        Assert.Single(el.Attached!);
        var ga = el.GetAttached<GridAttached>();
        Assert.NotNull(ga);
        Assert.Equal(2, ga!.Row);
        Assert.Equal(3, ga.Column);
        Assert.True(el.Attached.ContainsKey(typeof(GridAttached)));
    }

    [Fact]
    public void Grid_Reapplied_Same_Position_Is_AttachedEqual_And_Skips()
    {
        var a = TextBlock("cell").Grid(2, 3);
        var b = TextBlock("cell").Grid(2, 3);
        Assert.True(Element.AttachedEqual(a.Attached, b.Attached));
        Assert.True(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void Grid_Different_Position_Is_Not_AttachedEqual()
    {
        var a = TextBlock("cell").Grid(2, 3);
        var b = TextBlock("cell").Grid(4, 5);
        Assert.False(Element.AttachedEqual(a.Attached, b.Attached));
        Assert.False(Element.ShallowEquals(a, b));
    }

    [Fact]
    public void Second_Distinct_Attached_Type_Promotes_To_Full_Dictionary()
    {
        // The single-slot dictionary is an optimization for the common one-value
        // case; a second distinct attached type must still materialize correctly.
        var el = TextBlock("cell").Grid(1, 1).WrapGridRowSpan(2);
        Assert.Equal(2, el.Attached!.Count);
        Assert.NotNull(el.GetAttached<GridAttached>());
        Assert.NotNull(el.GetAttached<WrapGridAttached>());
        Assert.Equal(2, el.GetAttached<WrapGridAttached>()!.RowSpan);
    }

    // ════════════════════════════════════════════════════════════════
    //  Dsl micro-opts — behaviour preserved (#170 / #171 / #172 / #173)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ForEach_IReadOnlyList_FastPath_Preserves_Order_And_Count()
    {
        var items = new List<string> { "a", "b", "c" };
        var group = Assert.IsType<GroupElement>(ForEach(items, s => TextBlock(s)));
        Assert.Equal(3, group.Children.Length);
        Assert.All(group.Children, c => Assert.IsType<TextBlockElement>(c));
    }

    [Fact]
    public void ForEach_NonList_Enumerable_Matches_FastPath()
    {
        // A Where-iterator is IEnumerable but not IReadOnlyList → exercises the
        // fallback; result must match the fast-path exactly.
        IEnumerable<string> seq = new List<string> { "a", "b", "c" }.Where(_ => true);
        var group = Assert.IsType<GroupElement>(ForEach(seq, s => TextBlock(s)));
        Assert.Equal(3, group.Children.Length);
    }

    [Fact]
    public void ForEach_Indexed_Overload_Preserves_Count()
    {
        var items = new List<string> { "x", "y" };
        var group = Assert.IsType<GroupElement>(ForEach(items, (s, i) => TextBlock($"{i}:{s}")));
        Assert.Equal(2, group.Children.Length);
    }

    [Fact]
    public void FilterChildren_Drops_Nulls_And_Flattens_One_Group_Level()
    {
        // #173 two-pass exact-array path (triggered by the null) drops nulls.
        var filtered = Assert.IsType<GroupElement>(Group(TextBlock("a"), null, TextBlock("b")));
        Assert.Equal(2, filtered.Children.Length);

        // Nested GroupElement is flattened one level.
        var nested = Assert.IsType<GroupElement>(
            Group(Group(TextBlock("a"), TextBlock("b")), TextBlock("c")));
        Assert.Equal(3, nested.Children.Length);
    }

    [Fact]
    public void UniformGrid_Builds_One_Star_Track_Per_Item()
    {
        var grid = UniformGrid(Orientation.Horizontal, TextBlock("a"), TextBlock("b"), TextBlock("c"));
        Assert.Equal(3, grid.Children.Length);
        Assert.Equal(3, grid.Definition.Columns.Length);
        Assert.Single(grid.Definition.Rows);
    }

    [Fact]
    public void InterspersedGrid_Builds_Item_And_Separator_Tracks()
    {
        var grid = InterspersedGrid(
            Orientation.Horizontal,
            new Element[] { TextBlock("a"), TextBlock("b"), TextBlock("c") },
            new double[] { 1, 1, 1 },
            6,
            _ => TextBlock("|"));
        // 3 items + 2 separators = 5 tracks/children.
        Assert.Equal(5, grid.Children.Length);
        Assert.Equal(5, grid.Definition.Columns.Length);
        Assert.Single(grid.Definition.Rows);
    }
}
