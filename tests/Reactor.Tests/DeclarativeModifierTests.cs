using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the new declarative modifier APIs:
/// - FontFamily/FontSize/FontWeight as general ElementModifiers (Fix 1)
/// - Declarative event handlers on ElementModifiers (Fix 2)
/// - Action-based UseReducer hook (Fix 3)
/// - Grid layout builder helpers (Fix 4)
/// </summary>
public class DeclarativeModifierTests
{
    // ════════════════════════════════════════════════════════════════
    //  Fix 1: FontFamily/FontSize/FontWeight as general modifiers
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void FontSize_On_Button_Stores_In_Modifiers()
    {
        var el = Button("Go").FontSize(14);
        Assert.IsType<ButtonElement>(el); // concrete type preserved
        Assert.NotNull(el.Modifiers);
        Assert.Equal(14.0, el.Modifiers!.FontSize);
    }

    // FontFamily_String_On_Button and FontFamily_Instance_On_Border moved to
    // selfhost fixtures (WinUIActivationFixtures) — they require WinUI activation.

    [Fact]
    public void FontWeight_On_Button_Stores_In_Modifiers()
    {
        var weight = new global::Windows.UI.Text.FontWeight(700);
        var el = Button("Bold").FontWeight(weight);
        Assert.NotNull(el.Modifiers);
        Assert.Equal(700, el.Modifiers!.FontWeight!.Value.Weight);
    }

    [Fact]
    public void FontSize_Chains_With_Other_Modifiers()
    {
        var el = Button("Go")
            .FontSize(10)
            .Margin(4)
            .MinWidth(0);
        Assert.Equal(10.0, el.Modifiers!.FontSize);
        Assert.Equal(new Thickness(4), el.Modifiers.Margin);
        Assert.Equal(0.0, el.Modifiers.MinWidth);
    }

    [Fact]
    public void FontSize_On_TextBlockElement_Uses_General_Modifier()
    {
        // The general FontSize modifier stores on ElementModifiers
        // (different from TextBlockElement's typed FontSize property)
        var el = (Element)TextBlock("Hi").FontSize(20);
        // TextBlockElement.FontSize() returns TextBlockElement with the TextBlockElement.FontSize property set
        var textEl = (TextBlockElement)el;
        Assert.Equal(20, textEl.FontSize); // typed property
    }

    [Fact]
    public void General_FontSize_On_Non_Text_Element()
    {
        // Using the generic extension on a non-TextBlockElement
        Element el = CheckBox(true).FontSize(16);
        Assert.Equal(16.0, el.Modifiers!.FontSize);
    }

    [Fact]
    public void ContentAlignment_Modifiers_Store_In_Layout_Bucket()
    {
        var el = Button("Go")
            .HorizontalContentAlignment(HorizontalAlignment.Right)
            .VerticalContentAlignment(VerticalAlignment.Bottom);

        Assert.NotNull(el.Modifiers);
        Assert.NotNull(el.Modifiers!.Layout);
        Assert.Equal(HorizontalAlignment.Right, el.Modifiers.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Bottom, el.Modifiers.VerticalContentAlignment);
    }

    [Fact]
    public void BorderBrush_And_BorderThickness_Modifiers_Store_Independent_Surface()
    {
        var el = Button("Go")
            .BorderBrush(Theme.CardStroke)
            .BorderThickness(1, 2, 3, 4);

        Assert.NotNull(el.Modifiers);
        Assert.NotNull(el.Modifiers!.Visual);
        Assert.True(el.ThemeBindings!.ContainsKey("BorderBrush"));
        Assert.Equal(new Thickness(1, 2, 3, 4), el.Modifiers.BorderThickness);
    }

    [Fact]
    public void ContentAlignment_ModifiersEqual_Tracks_Field_Changes()
    {
        var a = new ElementModifiers
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var b = new ElementModifiers
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var c = new ElementModifiers
        {
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var d = new ElementModifiers
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Bottom,
        };

        Assert.True(Element.ModifiersEqual(a, b));
        Assert.False(Element.ModifiersEqual(a, c));
        Assert.False(Element.ModifiersEqual(a, d));
    }

    // FontFamily_Merge_Overwrites moved to selfhost fixtures (WinUIActivationFixtures).

    // ════════════════════════════════════════════════════════════════
    //  Fix 2: Declarative event handlers
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void OnSizeChanged_Stores_Handler_In_Modifiers()
    {
        Action<object, SizeChangedEventArgs> handler = (s, e) => { };
        var el = Border(TextBlock("x")).OnSizeChanged(handler);
        Assert.NotNull(el.Modifiers);
        Assert.Same(handler, el.Modifiers!.OnSizeChanged);
    }

    [Fact]
    public void OnPointerPressed_Stores_Handler()
    {
        Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> handler = (s, e) => { };
        var el = Border(TextBlock("x")).OnPointerPressed(handler);
        Assert.Same(handler, el.Modifiers!.OnPointerPressed);
    }

    [Fact]
    public void OnPointerMoved_Stores_Handler()
    {
        Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> handler = (s, e) => { };
        var el = Border(TextBlock("x")).OnPointerMoved(handler);
        Assert.Same(handler, el.Modifiers!.OnPointerMoved);
    }

    [Fact]
    public void OnPointerReleased_Stores_Handler()
    {
        Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> handler = (s, e) => { };
        var el = Border(TextBlock("x")).OnPointerReleased(handler);
        Assert.Same(handler, el.Modifiers!.OnPointerReleased);
    }

    [Fact]
    public void OnTapped_Stores_Handler()
    {
        Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> handler = (s, e) => { };
        var el = Button("Go").OnTapped(handler);
        Assert.Same(handler, el.Modifiers!.OnTapped);
    }

    [Fact]
    public void OnKeyDown_Stores_Handler()
    {
        Action<object, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs> handler = (s, e) => { };
        var el = Border(TextBlock("x")).OnKeyDown(handler);
        Assert.Same(handler, el.Modifiers!.OnKeyDown);
    }

    [Fact]
    public void Event_Handlers_Chain_With_Modifiers()
    {
        Action<object, SizeChangedEventArgs> sizeHandler = (s, e) => { };
        Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> tapHandler = (s, e) => { };

        var el = Border(TextBlock("x"))
            .Padding(8)
            .OnSizeChanged(sizeHandler)
            .OnTapped(tapHandler)
            .Margin(4);

        Assert.Same(sizeHandler, el.Modifiers!.OnSizeChanged);
        Assert.Same(tapHandler, el.Modifiers!.OnTapped);
        Assert.Equal(new Thickness(8), el.Modifiers.Padding);
        Assert.Equal(new Thickness(4), el.Modifiers.Margin);
    }

    [Fact]
    public void Event_Handlers_Merge_Overwrites()
    {
        Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> handler1 = (s, e) => { };
        Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> handler2 = (s, e) => { };

        var el = Button("Go").OnTapped(handler1).OnTapped(handler2);
        Assert.Same(handler2, el.Modifiers!.OnTapped); // second handler wins
    }

    [Fact]
    public void ModifiersEqual_True_When_Same_Handler_Reference()
    {
        // FLAGSHIP-1 — a reference-stable handler (memoized / non-capturing /
        // method group) now takes the skip fast-path: the reconciler's Current*
        // dispatch field already holds this exact delegate, so skipping Update is
        // safe. Previously ANY handler present forced a full Update every render.
        Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> handler = (s, e) => { };
        var a = new ElementModifiers { OnTapped = handler };
        var b = new ElementModifiers { OnTapped = handler };
        Assert.True(Element.ModifiersEqual(a, b));
    }

    [Fact]
    public void ModifiersEqual_False_When_Different_Handler_Reference()
    {
        // A freshly-captured closure each render is a different delegate instance,
        // so the skip path is correctly declined and Update re-wires Current*.
        Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> handler1 = (s, e) => { };
        Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> handler2 = (s, e) => { };
        var a = new ElementModifiers { OnTapped = handler1 };
        var b = new ElementModifiers { OnTapped = handler2 };
        Assert.False(Element.ModifiersEqual(a, b));
    }

    [Fact]
    public void ModifiersEqual_False_When_Handler_Presence_Differs()
    {
        // null → non-null transition must force Update so the trampoline subscribes.
        Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> handler = (s, e) => { };
        var a = new ElementModifiers { OnTapped = handler };
        var b = new ElementModifiers { };
        Assert.False(Element.ModifiersEqual(a, b));
        Assert.False(Element.ModifiersEqual(b, a));
    }

    [Fact]
    public void ModifiersEqual_False_When_Unlisted_Handler_Identity_Differs()
    {
        // Regression guard: handlers outside the historical 6-handler null-check
        // (e.g. PointerEntered) are now compared too, closing the latent hole
        // where a stale closure could be stranded on the skip path.
        Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> h1 = (s, e) => { };
        Action<object, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs> h2 = (s, e) => { };
        var a = new ElementModifiers { OnPointerEntered = h1 };
        var b = new ElementModifiers { OnPointerEntered = h2 };
        Assert.False(Element.ModifiersEqual(a, b));
    }

    [Fact]
    public void ModifiersEqual_Returns_True_When_No_Event_Handlers()
    {
        var a = new ElementModifiers { Width = 100, FontSize = 14 };
        var b = new ElementModifiers { Width = 100, FontSize = 14 };
        Assert.True(Element.ModifiersEqual(a, b));
    }

    // ════════════════════════════════════════════════════════════════
    //  Fix 3: Action-based UseReducer
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void ActionReducer_Returns_Initial_State()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        static int reducer(int state, string action) => action switch
        {
            "increment" => state + 1,
            "decrement" => state - 1,
            "reset" => 0,
            _ => state,
        };

        var (value, dispatch) = ctx.UseReducer<int, string>(reducer, 42);
        Assert.Equal(42, value);
    }

    [Fact]
    public void ActionReducer_Dispatch_Updates_State()
    {
        var ctx = new RenderContext();
        bool rerenderCalled = false;
        ctx.BeginRender(() => rerenderCalled = true);

        static int reducer(int state, string action) => action switch
        {
            "increment" => state + 1,
            _ => state,
        };

        var (_, dispatch) = ctx.UseReducer<int, string>(reducer, 0);
        dispatch("increment");
        Assert.True(rerenderCalled);

        // Re-render to get new value
        ctx.BeginRender(() => { });
        var (value2, _) = ctx.UseReducer<int, string>(reducer, 0);
        Assert.Equal(1, value2);
    }

    [Fact]
    public void ActionReducer_Same_State_Does_Not_Rerender()
    {
        var ctx = new RenderContext();
        bool rerenderCalled = false;
        ctx.BeginRender(() => rerenderCalled = true);

        static int reducer(int state, string action) => state; // always returns same

        var (_, dispatch) = ctx.UseReducer<int, string>(reducer, 5);
        dispatch("anything");
        Assert.False(rerenderCalled);
    }

    private record AppState(int Count, string LastAction);

    [Fact]
    public void ActionReducer_Complex_State()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        static AppState reducer(AppState state, string action) => action switch
        {
            "increment" => state with { Count = state.Count + 1, LastAction = "increment" },
            "reset" => new AppState(0, "reset"),
            _ => state,
        };

        var initial = new AppState(0, "none");
        var (state, dispatch) = ctx.UseReducer<AppState, string>(reducer, initial);
        Assert.Equal(0, state.Count);

        dispatch("increment");
        dispatch("increment");

        ctx.BeginRender(() => { });
        var (state2, _) = ctx.UseReducer<AppState, string>(reducer, initial);
        // Second dispatch ran on state=1, so result is 2
        Assert.Equal(2, state2.Count);
        Assert.Equal("increment", state2.LastAction);
    }

    // Typed action hierarchy for the typed-actions test
    private abstract record CounterAction;
    private record Increment : CounterAction;
    private record Decrement : CounterAction;
    private record ResetTo(int Value) : CounterAction;

    [Fact]
    public void ActionReducer_Typed_Actions()
    {
        var ctx = new RenderContext();
        ctx.BeginRender(() => { });

        static int reducer(int state, CounterAction action) => action switch
        {
            Increment => state + 1,
            Decrement => state - 1,
            ResetTo r => r.Value,
            _ => state,
        };

        var (_, dispatch) = ctx.UseReducer<int, CounterAction>(reducer, 10);
        dispatch(new Increment());
        dispatch(new Increment());
        dispatch(new Decrement());
        dispatch(new ResetTo(100));

        ctx.BeginRender(() => { });
        var (val, _) = ctx.UseReducer<int, CounterAction>(reducer, 10);
        Assert.Equal(100, val); // reset to 100
    }

    // ════════════════════════════════════════════════════════════════
    //  Fix 4: Grid layout builder helpers
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void InterspersedGrid_Horizontal_Creates_Correct_Columns()
    {
        var items = new[] { TextBlock("A"), TextBlock("B"), TextBlock("C") };
        var proportions = new[] { 1.0, 1.0, 1.0 };

        var grid = InterspersedGrid(
            Orientation.Horizontal,
            items, proportions, 6.0,
            i => TextBlock($"sep-{i}"));

        // 3 items + 2 separators = 5 columns
        Assert.Equal(5, grid.Definition.Columns.Length);
        Assert.Single(grid.Definition.Rows); // ["*"]
        Assert.Equal(5, grid.Children.Length); // 3 items + 2 separators

        // Check column definitions: "1.000000*", "6", "1.000000*", "6", "1.000000*"
        Assert.Contains("*", grid.Definition.Columns[0]);
        Assert.Equal("6", grid.Definition.Columns[1]);
        Assert.Contains("*", grid.Definition.Columns[2]);
        Assert.Equal("6", grid.Definition.Columns[3]);
        Assert.Contains("*", grid.Definition.Columns[4]);
    }

    [Fact]
    public void InterspersedGrid_Vertical_Creates_Correct_Rows()
    {
        var items = new[] { TextBlock("A"), TextBlock("B") };
        var proportions = new[] { 0.7, 0.3 };

        var grid = InterspersedGrid(
            Orientation.Vertical,
            items, proportions, 4.0,
            i => TextBlock("---"));

        // 2 items + 1 separator = 3 rows
        Assert.Equal(3, grid.Definition.Rows.Length);
        Assert.Single(grid.Definition.Columns); // ["*"]
        Assert.Equal(3, grid.Children.Length);

        Assert.Contains("0.7", grid.Definition.Rows[0]);
        Assert.Equal("4", grid.Definition.Rows[1]);
        Assert.Contains("0.3", grid.Definition.Rows[2]);
    }

    [Fact]
    public void InterspersedGrid_Single_Item_No_Separators()
    {
        var items = new[] { TextBlock("Only") };
        var proportions = new[] { 1.0 };

        var grid = InterspersedGrid(
            Orientation.Horizontal,
            items, proportions, 6.0,
            i => TextBlock("sep"));

        Assert.Single(grid.Definition.Columns);
        Assert.Single(grid.Children);
    }

    [Fact]
    public void InterspersedGrid_Empty_Returns_Empty_Grid()
    {
        var grid = InterspersedGrid(
            Orientation.Horizontal,
            [], [], 6.0,
            i => TextBlock("sep"));

        Assert.Empty(grid.Definition.Columns);
        Assert.Empty(grid.Definition.Rows);
        Assert.Empty(grid.Children);
    }

    [Fact]
    public void InterspersedGrid_Mismatched_Lengths_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            InterspersedGrid(
                Orientation.Horizontal,
                [TextBlock("A"), TextBlock("B")],
                [1.0],  // wrong length
                6.0,
                i => TextBlock("sep")));
    }

    [Fact]
    public void InterspersedGrid_Children_Have_Correct_Grid_Attached()
    {
        var items = new[] { TextBlock("A"), TextBlock("B") };
        var proportions = new[] { 0.5, 0.5 };

        var grid = InterspersedGrid(
            Orientation.Horizontal,
            items, proportions, 6.0,
            i => TextBlock("sep"));

        // Item A at column 0, separator at column 1, Item B at column 2
        var itemA = grid.Children[0];
        var sep = grid.Children[1];
        var itemB = grid.Children[2];

        Assert.Equal(0, itemA.GetAttached<GridAttached>()!.Column);
        Assert.Equal(1, sep.GetAttached<GridAttached>()!.Column);
        Assert.Equal(2, itemB.GetAttached<GridAttached>()!.Column);
    }

    [Fact]
    public void UniformGrid_Horizontal_Creates_Equal_Columns()
    {
        var grid = UniformGrid(Orientation.Horizontal, TextBlock("A"), TextBlock("B"), TextBlock("C"));

        Assert.Equal(3, grid.Definition.Columns.Length);
        Assert.All(grid.Definition.Columns, col => Assert.Equal("*", col));
        Assert.Single(grid.Definition.Rows);
        Assert.Equal(3, grid.Children.Length);
    }

    [Fact]
    public void UniformGrid_Vertical_Creates_Equal_Rows()
    {
        var grid = UniformGrid(Orientation.Vertical, TextBlock("A"), TextBlock("B"));

        Assert.Equal(2, grid.Definition.Rows.Length);
        Assert.All(grid.Definition.Rows, row => Assert.Equal("*", row));
        Assert.Single(grid.Definition.Columns);
    }

    [Fact]
    public void UniformGrid_Filters_Nulls()
    {
        var grid = UniformGrid(Orientation.Horizontal, TextBlock("A"), null, TextBlock("B"));
        Assert.Equal(2, grid.Children.Length);
        Assert.Equal(2, grid.Definition.Columns.Length);
    }

    [Fact]
    public void UniformGrid_Empty_Returns_Empty_Grid()
    {
        var grid = UniformGrid(Orientation.Horizontal);
        Assert.Empty(grid.Children);
    }

    // ════════════════════════════════════════════════════════════════
    //  Modifier merge regression
    // ════════════════════════════════════════════════════════════════

    // Typography_Modifiers_Merge_Correctly moved to selfhost fixtures (WinUIActivationFixtures).

    [Fact]
    public void Event_Handler_Modifiers_Merge_Correctly()
    {
        Action<object, SizeChangedEventArgs> h1 = (s, e) => { };
        Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> h2 = (s, e) => { };
        Action<object, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs> h3 = (s, e) => { };

        var a = new ElementModifiers { OnSizeChanged = h1, OnTapped = h2 };
        var b = new ElementModifiers { OnTapped = h3 }; // override tap, keep size
        var merged = a.Merge(b);

        Assert.Same(h1, merged.OnSizeChanged);
        Assert.Same(h3, merged.OnTapped); // overridden
    }
}
