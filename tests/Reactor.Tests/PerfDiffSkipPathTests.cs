using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
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

    [Fact]
    public void ShallowEquals_False_When_OnUnmount_Changes_With_Stable_Handler()
    {
        // FLAGSHIP-1 lets a stable .OnTapped element take the skip path. The
        // imperative .OnUnmount teardown is re-registered ONLY on the non-skip Update
        // path (the reconciler's _onUnmountActions), and the latest registered
        // delegate is what fires at unmount — so a *changed* teardown closure must
        // decline the skip, or the stale (first-render) action is stranded and runs
        // in place of the current one. Pre-FLAGSHIP-1 this was masked because any
        // handler present forced Update every render; the per-slot ref-equality skip
        // re-exposed it, so OnUnmountAction is compared by reference in ModifiersEqual.
        Action<FrameworkElement> teardown1 = _ => { };
        Action<FrameworkElement> teardown2 = _ => { };

        var changedA = TextBlock("AAPL").OnTapped(SharedTap).OnUnmount(teardown1);
        var changedB = TextBlock("AAPL").OnTapped(SharedTap).OnUnmount(teardown2);
        Assert.False(Element.ShallowEquals(changedA, changedB));

        // A reference-stable teardown (same delegate instance) stays skip-eligible.
        var stableA = TextBlock("AAPL").OnTapped(SharedTap).OnUnmount(teardown1);
        var stableB = TextBlock("AAPL").OnTapped(SharedTap).OnUnmount(teardown1);
        Assert.True(Element.ShallowEquals(stableA, stableB));

        // Presence change (add / remove the teardown) must also decline the skip.
        var withTeardown = TextBlock("AAPL").OnTapped(SharedTap).OnUnmount(teardown1);
        var withoutTeardown = TextBlock("AAPL").OnTapped(SharedTap);
        Assert.False(Element.ShallowEquals(withTeardown, withoutTeardown));
        Assert.False(Element.ShallowEquals(withoutTeardown, withTeardown));
    }

    [Fact]
    public void ModifierCallbacksEqual_Every_Slot_Participates_By_Reference()
    {
        // FLAGSHIP-1 compares 27 event/gesture/drag-drop handler slots by per-slot
        // ReferenceEquals. This sweeps EACH slot to prove it participates: setting a
        // slot to the same instance on both sides stays skip-eligible, while changing
        // that one slot's instance (or adding/removing it) declines the skip. Guards
        // against a forgotten slot or a mis-paired comparison (e.g. a.X vs a.X). One
        // distinct instance pair per delegate/config type; only one slot is set per
        // case, so a sibling mis-pair (a.OnKeyDown vs b.OnKeyUp) is caught too.
        Action<object, SizeChangedEventArgs> sc1 = (s, e) => { }, sc2 = (s, e) => { };
        Action<object, PointerRoutedEventArgs> p1 = (s, e) => { }, p2 = (s, e) => { };
        Action<object, TappedRoutedEventArgs> tap1 = (s, e) => { }, tap2 = (s, e) => { };
        Action<object, DoubleTappedRoutedEventArgs> dt1 = (s, e) => { }, dt2 = (s, e) => { };
        Action<object, RightTappedRoutedEventArgs> rt1 = (s, e) => { }, rt2 = (s, e) => { };
        Action<object, HoldingRoutedEventArgs> hd1 = (s, e) => { }, hd2 = (s, e) => { };
        Action<object, KeyRoutedEventArgs> k1 = (s, e) => { }, k2 = (s, e) => { };
        Action<UIElement, CharacterReceivedRoutedEventArgs> cr1 = (s, e) => { }, cr2 = (s, e) => { };
        Action<object, RoutedEventArgs> f1 = (s, e) => { }, f2 = (s, e) => { };
        Action<UIElement, AccessKeyDisplayRequestedEventArgs> ak1 = (s, e) => { }, ak2 = (s, e) => { };
        PanGestureConfig pan1 = new(_ => { }), pan2 = new(_ => { });
        PinchGestureConfig pin1 = new(_ => { }), pin2 = new(_ => { });
        RotateGestureConfig rot1 = new(_ => { }), rot2 = new(_ => { });
        LongPressGestureConfig lp1 = new(_ => { }), lp2 = new(_ => { });
        DragSourceConfig ds1 = new(() => default!), ds2 = new(() => default!);
        DropTargetConfig dp1 = new(), dp2 = new();

        // (name, equalA, equalB [same instance as A], diff [different instance])
        var cases = new (string Name, ElementModifiers A, ElementModifiers B, ElementModifiers Diff)[]
        {
            ("OnSizeChanged",        new() { OnSizeChanged = sc1 },        new() { OnSizeChanged = sc1 },        new() { OnSizeChanged = sc2 }),
            ("OnPointerPressed",     new() { OnPointerPressed = p1 },      new() { OnPointerPressed = p1 },      new() { OnPointerPressed = p2 }),
            ("OnPointerMoved",       new() { OnPointerMoved = p1 },        new() { OnPointerMoved = p1 },        new() { OnPointerMoved = p2 }),
            ("OnPointerReleased",    new() { OnPointerReleased = p1 },     new() { OnPointerReleased = p1 },     new() { OnPointerReleased = p2 }),
            ("OnPointerEntered",     new() { OnPointerEntered = p1 },      new() { OnPointerEntered = p1 },      new() { OnPointerEntered = p2 }),
            ("OnPointerExited",      new() { OnPointerExited = p1 },       new() { OnPointerExited = p1 },       new() { OnPointerExited = p2 }),
            ("OnPointerCanceled",    new() { OnPointerCanceled = p1 },     new() { OnPointerCanceled = p1 },     new() { OnPointerCanceled = p2 }),
            ("OnPointerCaptureLost", new() { OnPointerCaptureLost = p1 },  new() { OnPointerCaptureLost = p1 },  new() { OnPointerCaptureLost = p2 }),
            ("OnPointerWheelChanged",new() { OnPointerWheelChanged = p1 }, new() { OnPointerWheelChanged = p1 }, new() { OnPointerWheelChanged = p2 }),
            ("OnTapped",             new() { OnTapped = tap1 },            new() { OnTapped = tap1 },            new() { OnTapped = tap2 }),
            ("OnDoubleTapped",       new() { OnDoubleTapped = dt1 },       new() { OnDoubleTapped = dt1 },       new() { OnDoubleTapped = dt2 }),
            ("OnRightTapped",        new() { OnRightTapped = rt1 },        new() { OnRightTapped = rt1 },        new() { OnRightTapped = rt2 }),
            ("OnHolding",            new() { OnHolding = hd1 },            new() { OnHolding = hd1 },            new() { OnHolding = hd2 }),
            ("OnKeyDown",            new() { OnKeyDown = k1 },             new() { OnKeyDown = k1 },             new() { OnKeyDown = k2 }),
            ("OnKeyUp",              new() { OnKeyUp = k1 },               new() { OnKeyUp = k1 },               new() { OnKeyUp = k2 }),
            ("OnPreviewKeyDown",     new() { OnPreviewKeyDown = k1 },      new() { OnPreviewKeyDown = k1 },      new() { OnPreviewKeyDown = k2 }),
            ("OnPreviewKeyUp",       new() { OnPreviewKeyUp = k1 },        new() { OnPreviewKeyUp = k1 },        new() { OnPreviewKeyUp = k2 }),
            ("OnCharacterReceived",  new() { OnCharacterReceived = cr1 },  new() { OnCharacterReceived = cr1 },  new() { OnCharacterReceived = cr2 }),
            ("OnGotFocus",           new() { OnGotFocus = f1 },            new() { OnGotFocus = f1 },            new() { OnGotFocus = f2 }),
            ("OnLostFocus",          new() { OnLostFocus = f1 },           new() { OnLostFocus = f1 },           new() { OnLostFocus = f2 }),
            ("OnAccessKeyDisplayRequested", new() { OnAccessKeyDisplayRequested = ak1 }, new() { OnAccessKeyDisplayRequested = ak1 }, new() { OnAccessKeyDisplayRequested = ak2 }),
            ("Pan",                  new() { Pan = pan1 },                 new() { Pan = pan1 },                 new() { Pan = pan2 }),
            ("Pinch",                new() { Pinch = pin1 },               new() { Pinch = pin1 },               new() { Pinch = pin2 }),
            ("Rotate",               new() { Rotate = rot1 },              new() { Rotate = rot1 },              new() { Rotate = rot2 }),
            ("LongPress",            new() { LongPress = lp1 },            new() { LongPress = lp1 },            new() { LongPress = lp2 }),
            ("DragSource",           new() { DragSource = ds1 },           new() { DragSource = ds1 },           new() { DragSource = ds2 }),
            ("DropTarget",           new() { DropTarget = dp1 },           new() { DropTarget = dp1 },           new() { DropTarget = dp2 }),
        };

        var empty = new ElementModifiers();
        foreach (var (name, a, b, diff) in cases)
        {
            Assert.True(Element.ModifiersEqual(a, b), $"{name}: same handler instance ⇒ skip-eligible");
            Assert.False(Element.ModifiersEqual(a, diff), $"{name}: changed handler instance ⇒ must Update");
            Assert.False(Element.ModifiersEqual(a, empty), $"{name}: adding handler ⇒ must Update");
            Assert.False(Element.ModifiersEqual(empty, a), $"{name}: removing handler ⇒ must Update");
        }

        // Tracks the slot count in Element.ModifierCallbacksEqual; bump both together.
        Assert.Equal(27, cases.Length);
    }

    // ════════════════════════════════════════════════════════════════
    //  Setters (.Set) — the imperative escape hatch always re-applies on
    //  Update; only the no-setter case is skip-eligible. A reference-stable
    //  setter is NOT skippable: it is an apply-time imperative write that can
    //  read mutable external state (statics/singletons), and non-capturing
    //  lambdas are compiler-cached to a stable identity — so identity-equality
    //  must not be mistaken for "the write is unnecessary". (Safety counterpart
    //  to FLAGSHIP-1, where handlers dispatch later via Current* and are safe.)
    // ════════════════════════════════════════════════════════════════

    // Same source location, non-capturing lambda → the compiler caches a single
    // static delegate, so both renders' Setters arrays hold the identical instance.
    private static Element BuildWithStaticSetter() =>
        TextBlock("Hdr").Set(tb => tb.FontSize = 12);

    // Captures `size` → a fresh display-class + delegate per call.
    private static Element BuildWithCapturingSetter(double size) =>
        TextBlock("Hdr").Set(tb => tb.FontSize = size);

    [Fact]
    public void ShallowEquals_True_When_Neither_Element_Has_Setters()
    {
        // Both Setters arrays are the empty singleton → no imperative writes to
        // re-apply → the cell stays skip-eligible.
        Assert.True(Element.ShallowEquals(TextBlock("Hdr"), TextBlock("Hdr")));
    }

    [Fact]
    public void ShallowEquals_False_For_Stable_Set_Chain()
    {
        // Even a cached, reference-stable (non-capturing) setter must decline the
        // skip: `.Set` is an apply-time imperative write whose effect can depend on
        // mutable external state, so the reconciler has to re-apply it every update.
        Assert.False(Element.ShallowEquals(BuildWithStaticSetter(), BuildWithStaticSetter()));
    }

    [Fact]
    public void ShallowEquals_False_For_Capturing_Set_Chain()
    {
        // A capturing setter allocates a new delegate per render and likewise
        // declines the fast-path.
        Assert.False(Element.ShallowEquals(BuildWithCapturingSetter(12), BuildWithCapturingSetter(12)));
    }

    [Fact]
    public void SettersEqual_True_For_Same_Instance_Or_Both_Empty()
    {
        Action a = () => { };
        var arr = new[] { a };
        Assert.True(Element.SettersEqual(arr, arr));                              // same instance
        Assert.True(Element.SettersEqual(Array.Empty<Action>(), new Action[0])); // both empty
        Assert.True(Element.SettersEqual<Action>(null, null));                    // both null
        Assert.True(Element.SettersEqual(Array.Empty<Action>(), null));          // empty + null
    }

    [Fact]
    public void SettersEqual_False_When_Setters_Present_In_Distinct_Arrays()
    {
        Action shared = () => { };
        // Distinct arrays holding the IDENTICAL (cached/stable) delegate must still
        // decline — the imperative write has to re-run every update.
        Assert.False(Element.SettersEqual(new[] { shared }, new[] { shared }));
        Assert.False(Element.SettersEqual(new[] { shared }, Array.Empty<Action>()));
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
    public void ForEach_IReadOnlyList_FastPath_Preserves_Order_And_Content()
    {
        var items = new List<string> { "a", "b", "c" };
        var group = Assert.IsType<GroupElement>(ForEach(items, s => TextBlock(s)));
        Assert.Equal(
            new[] { "a", "b", "c" },
            group.Children.Select(c => Assert.IsType<TextBlockElement>(c).Content));
    }

    [Fact]
    public void ForEach_NonList_Enumerable_Matches_FastPath()
    {
        // A Where-iterator is IEnumerable but not IReadOnlyList → exercises the
        // fallback; result must match the fast-path exactly (same order/content).
        IEnumerable<string> seq = new List<string> { "a", "b", "c" }.Where(_ => true);
        var group = Assert.IsType<GroupElement>(ForEach(seq, s => TextBlock(s)));
        Assert.Equal(
            new[] { "a", "b", "c" },
            group.Children.Select(c => Assert.IsType<TextBlockElement>(c).Content));
    }

    [Fact]
    public void ForEach_Indexed_Overload_Preserves_Index_And_Content()
    {
        var items = new List<string> { "x", "y" };
        var group = Assert.IsType<GroupElement>(ForEach(items, (s, i) => TextBlock($"{i}:{s}")));
        Assert.Equal(
            new[] { "0:x", "1:y" },
            group.Children.Select(c => Assert.IsType<TextBlockElement>(c).Content));
    }

    [Fact]
    public void ForEach_Empty_Input_Produces_Empty_Group()
    {
        // Both the IReadOnlyList fast-path and the indexed overload must yield an
        // empty child set (no off-by-one, no phantom element) for empty input.
        var list = Assert.IsType<GroupElement>(ForEach(new List<string>(), s => TextBlock(s)));
        Assert.Empty(list.Children);

        var indexed = Assert.IsType<GroupElement>(ForEach(new List<string>(), (s, i) => TextBlock(s)));
        Assert.Empty(indexed.Children);

        IEnumerable<string> emptySeq = new List<string>().Where(_ => true);
        var seq = Assert.IsType<GroupElement>(ForEach(emptySeq, s => TextBlock(s)));
        Assert.Empty(seq.Children);
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
    public void UniformGrid_Does_Not_Mutate_Caller_Items_Array()
    {
        // FilterChildren's #173 fast path returns a null-free input array aliased
        // (no copy). UniformGrid must position cells into its OWN array — writing
        // .Grid(...) wrappers back into `items` would corrupt a caller-supplied,
        // reusable array. Lock that in: the source array's elements must be
        // unchanged (and un-positioned) after the call.
        var cellA = TextBlock("a");
        var cellB = TextBlock("b");
        var items = new Element?[] { cellA, cellB };

        var grid = UniformGrid(Orientation.Vertical, items);

        // Source array untouched — same instances, no Grid attached-data added.
        Assert.Same(cellA, items[0]);
        Assert.Same(cellB, items[1]);
        Assert.Null(((Element)items[0]!).GetAttached<GridAttached>());
        Assert.Null(((Element)items[1]!).GetAttached<GridAttached>());

        // The grid's own children ARE positioned.
        Assert.Equal(2, grid.Children.Length);
        Assert.NotNull(grid.Children[0].GetAttached<GridAttached>());
        Assert.Equal(1, grid.Children[1].GetAttached<GridAttached>()!.Row);
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

    // ════════════════════════════════════════════════════════════════
    //  #168 — BrushHelper.ParseColor cache. The string→Color parse is
    //  cached (an immutable value, safe to share); the public Parse still
    //  returns a FRESH SolidColorBrush per call (a brush is a mutable,
    //  thread-affine DependencyObject and must not be shared across
    //  controls). These headless tests lock down the cached color parse.
    // ════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("red", 255, 255, 0, 0)]
    [InlineData("transparent", 0, 0, 0, 0)]
    [InlineData("#FF8800", 255, 255, 0x88, 0)]
    [InlineData("#8000FF00", 0x80, 0, 255, 0)]
    public void ParseColor_Resolves_Named_And_Hex(string input, int a, int r, int g, int b)
    {
        var color = BrushHelper.ParseColor(input);
        Assert.Equal((byte)a, color.A);
        Assert.Equal((byte)r, color.R);
        Assert.Equal((byte)g, color.G);
        Assert.Equal((byte)b, color.B);
    }

    [Fact]
    public void ParseColor_Is_Deterministic_Across_Calls()
    {
        // Repeated parses (served from the string cache on the second call) must
        // yield the same Color value.
        Assert.Equal(BrushHelper.ParseColor("#123456"), BrushHelper.ParseColor("#123456"));
        Assert.Equal(BrushHelper.ParseColor("blue"), BrushHelper.ParseColor("blue"));
    }

    // ════════════════════════════════════════════════════════════════
    //  #165 / #157 — ModifyLayout / ModifyVisual fluent helpers merge the
    //  delta straight into the Layout / Visual bucket (skipping a throwaway
    //  parent ElementModifiers + shim-built bucket per chained call). The
    //  resulting ElementModifiers must be value-identical to the historical
    //  `Modify(el, new ElementModifiers { <field> })` path so the diff and
    //  reconciler see no behavioural change — only fewer allocations.
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Fluent_Layout_Chain_Equals_HandBuilt_Modifiers()
    {
        var fluent = TextBlock("x").Margin(8).Width(120).Height(40).MinWidth(10);
        var handBuilt = new ElementModifiers
        {
            Margin = new Thickness(8),
            Width = 120,
            Height = 40,
            MinWidth = 10,
        };
        Assert.Equal(handBuilt, fluent.Modifiers);
        // Every layout field landed in a single Layout bucket; no Visual allocated.
        Assert.NotNull(fluent.Modifiers!.Layout);
        Assert.Null(fluent.Modifiers.Visual);
    }

    [Fact]
    public void Fluent_Visual_Chain_Equals_HandBuilt_Modifiers()
    {
        var fluent = TextBlock("x").Opacity(0.5).Scale(2f).Rotation(45f)
            .CornerRadius(4);
        var handBuilt = new ElementModifiers
        {
            Opacity = 0.5,
            Scale = new global::System.Numerics.Vector3(2f, 2f, 1f),
            Rotation = 45f,
            CornerRadius = new Microsoft.UI.Xaml.CornerRadius(4),
        };
        Assert.Equal(handBuilt, fluent.Modifiers);
        Assert.NotNull(fluent.Modifiers!.Visual);
        Assert.Null(fluent.Modifiers.Layout);
    }

    [Fact]
    public void Fluent_Mixed_Chain_Populates_Both_Buckets_Like_HandBuilt()
    {
        var fluent = TextBlock("x").Margin(8).Opacity(0.5).Width(120).Rotation(90f);
        var handBuilt = new ElementModifiers
        {
            Margin = new Thickness(8),
            Width = 120,
            Opacity = 0.5,
            Rotation = 90f,
        };
        Assert.Equal(handBuilt, fluent.Modifiers);
    }

    [Fact]
    public void ModifyLayout_Preserves_Existing_NonBucket_Field()
    {
        // IsEnabled is a top-level (non-bucketed) ElementModifiers field. Routing the
        // following .Margin() through ModifyLayout must not drop it — the bucket merge
        // only touches the Layout slot and copies every other field unchanged.
        var fluent = TextBlock("x").IsEnabled(false).Margin(8);
        var handBuilt = new ElementModifiers { IsEnabled = false, Margin = new Thickness(8) };
        Assert.Equal(handBuilt, fluent.Modifiers);
        Assert.False(fluent.Modifiers!.IsEnabled);
        Assert.Equal(new Thickness(8), fluent.Modifiers.Margin);
    }

    [Fact]
    public void ModifyVisual_Preserves_Existing_Layout_Bucket()
    {
        // A pure-visual modifier applied after a pure-layout one must leave the
        // existing Layout bucket intact (regression guard for the `mods with { Visual }`
        // merge not clobbering Layout).
        var fluent = TextBlock("x").Width(120).Opacity(0.5);
        Assert.Equal(120, fluent.Modifiers!.Width);
        Assert.Equal(0.5, fluent.Modifiers.Opacity);
        Assert.Equal(new ElementModifiers { Width = 120, Opacity = 0.5 }, fluent.Modifiers);
    }

    [Fact]
    public void Fluent_Layout_Chain_Is_ShallowEquals_Across_Identical_Builds()
    {
        // The end-to-end skip-path payoff: an unchanged layout/visual chain compares
        // equal so the cell takes the fast path.
        var a = TextBlock("x").Margin(8).Width(120).Opacity(0.5);
        var b = TextBlock("x").Margin(8).Width(120).Opacity(0.5);
        Assert.True(Element.ShallowEquals(a, b));
    }

    // ════════════════════════════════════════════════════════════════
    //  #174 — ApplyStyle caches the OnMount delegate per style name so an
    //  unchanged `.ApplyStyle("X")` chain reuses one delegate instance
    //  instead of allocating a fresh capturing closure + display class every
    //  render. (OnMountAction is intentionally excluded from the diff
    //  equality — it runs at mount only — so this is a pure allocation cut,
    //  not a skip-path change.)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ApplyStyle_Same_Name_Produces_ReferenceStable_OnMountAction()
    {
        var a = TextBlock("x").ApplyStyle("BodyTextBlockStyle");
        var b = TextBlock("x").ApplyStyle("BodyTextBlockStyle");
        // Same cached delegate instance for the same style name — no fresh closure
        // allocated on the second (and every subsequent) render.
        Assert.Same(a.Modifiers!.OnMountAction, b.Modifiers!.OnMountAction);
    }

    [Fact]
    public void ApplyStyle_Different_Name_Produces_Distinct_OnMountAction()
    {
        var a = TextBlock("x").ApplyStyle("BodyTextBlockStyle");
        var b = TextBlock("x").ApplyStyle("TitleTextBlockStyle");
        // Distinct style names map to distinct cached delegates (each applies its
        // own resource key).
        Assert.NotSame(a.Modifiers!.OnMountAction, b.Modifiers!.OnMountAction);
    }
}
