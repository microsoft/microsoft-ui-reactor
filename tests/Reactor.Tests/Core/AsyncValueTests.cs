using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Core;

public class AsyncValueTests
{
    // ════════════════════════════════════════════════════════════════
    //  Construction + pattern discrimination
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Every_State_Is_Constructable_And_Discriminable()
    {
        AsyncValue<int> loading = AsyncValue<int>.Loading.Instance;
        AsyncValue<int> data = new AsyncValue<int>.Data(42);
        AsyncValue<int> error = new AsyncValue<int>.Error(new InvalidOperationException("x"));
        AsyncValue<int> reloading = new AsyncValue<int>.Reloading(41);

        Assert.IsType<AsyncValue<int>.Loading>(loading);
        Assert.IsType<AsyncValue<int>.Data>(data);
        Assert.IsType<AsyncValue<int>.Error>(error);
        Assert.IsType<AsyncValue<int>.Reloading>(reloading);
    }

    [Fact]
    public void Pattern_Match_Destructures_Payload()
    {
        AsyncValue<string> data = new AsyncValue<string>.Data("hello");
        string result = data switch
        {
            AsyncValue<string>.Data(var v) => v,
            _ => "other",
        };
        Assert.Equal("hello", result);

        AsyncValue<string> reloading = new AsyncValue<string>.Reloading("prev");
        string result2 = reloading switch
        {
            AsyncValue<string>.Reloading(var v) => v,
            _ => "other",
        };
        Assert.Equal("prev", result2);
    }

    // ════════════════════════════════════════════════════════════════
    //  Match convenience
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Match_Dispatches_Loading()
    {
        AsyncValue<int> v = AsyncValue<int>.Loading.Instance;
        var result = v.Match(
            loading: () => "L",
            loaded: d => $"D{d}",
            error: e => $"E{e.Message}");
        Assert.Equal("L", result);
    }

    [Fact]
    public void Match_Dispatches_Data()
    {
        AsyncValue<int> v = new AsyncValue<int>.Data(7);
        var result = v.Match(
            loading: () => "L",
            loaded: d => $"D{d}",
            error: e => $"E{e.Message}");
        Assert.Equal("D7", result);
    }

    [Fact]
    public void Match_Dispatches_Error_With_Exception()
    {
        AsyncValue<int> v = new AsyncValue<int>.Error(new InvalidOperationException("boom"));
        var result = v.Match(
            loading: () => "L",
            loaded: d => $"D{d}",
            error: e => $"E{e.Message}");
        Assert.Equal("Eboom", result);
    }

    [Fact]
    public void Match_Reloading_Falls_Through_To_Loading_When_Null()
    {
        AsyncValue<int> v = new AsyncValue<int>.Reloading(9);
        var result = v.Match(
            loading: () => "L",
            loaded: d => $"D{d}",
            error: e => $"E{e.Message}");
        Assert.Equal("L", result);
    }

    [Fact]
    public void Match_Reloading_Uses_Override_When_Provided()
    {
        AsyncValue<int> v = new AsyncValue<int>.Reloading(9);
        var result = v.Match(
            loading: () => "L",
            loaded: d => $"D{d}",
            error: e => $"E{e.Message}",
            reloading: r => $"R{r}");
        Assert.Equal("R9", result);
    }

    // ════════════════════════════════════════════════════════════════
    //  Record equality
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Same_State_Same_Value_Are_Equal()
    {
        Assert.Equal(new AsyncValue<int>.Data(5), new AsyncValue<int>.Data(5));
        Assert.Equal(new AsyncValue<int>.Reloading(5), new AsyncValue<int>.Reloading(5));
        Assert.Equal(AsyncValue<int>.Loading.Instance, AsyncValue<int>.Loading.Instance);
    }

    [Fact]
    public void Same_State_Different_Value_Are_Unequal()
    {
        Assert.NotEqual(new AsyncValue<int>.Data(5), new AsyncValue<int>.Data(6));
        Assert.NotEqual(new AsyncValue<int>.Reloading(5), new AsyncValue<int>.Reloading(6));
    }

    [Fact]
    public void Different_States_Same_Payload_Are_Unequal()
    {
        AsyncValue<int> data = new AsyncValue<int>.Data(5);
        AsyncValue<int> reloading = new AsyncValue<int>.Reloading(5);
        Assert.NotEqual(data, reloading);
    }

    [Fact]
    public void Error_Equality_Respects_Exception_Identity()
    {
        var e1 = new InvalidOperationException("x");
        var e2 = new InvalidOperationException("x");
        Assert.Equal(new AsyncValue<int>.Error(e1), new AsyncValue<int>.Error(e1));
        Assert.NotEqual(new AsyncValue<int>.Error(e1), new AsyncValue<int>.Error(e2));
    }

    [Fact]
    public void Nested_AsyncValue_Equality()
    {
        var inner1 = new AsyncValue<int>.Data(1);
        var inner2 = new AsyncValue<int>.Data(1);
        var outer1 = new AsyncValue<AsyncValue<int>>.Data(inner1);
        var outer2 = new AsyncValue<AsyncValue<int>>.Data(inner2);
        Assert.Equal(outer1, outer2);
    }

    [Fact]
    public void Reference_Type_Value_Mutation_Does_Not_Break_Record_Equality_Reference_Check()
    {
        var list = new List<int> { 1, 2, 3 };
        var a = new AsyncValue<List<int>>.Data(list);
        var b = new AsyncValue<List<int>>.Data(list);
        // Same list reference — records are equal by virtue of same reference value.
        Assert.Equal(a, b);

        list.Add(4); // mutate underlying
        // Record still equal because both hold same reference (record equality is value-equality on fields,
        // and for reference types the field equality is Equals → reference equality for List<int>).
        Assert.Equal(a, b);
    }

    // ════════════════════════════════════════════════════════════════
    //  Exhaustive switch compiles without warnings
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Exhaustive_Switch_Covers_All_Arms()
    {
        // AsyncValue<T> is a C# 15 `closed` hierarchy, so the four arms are
        // exhaustive with no discard. Dropping an arm is CS8509 — a build error
        // under Release's TreatWarningsAsErrors — so this method is a
        // compile-time gate on the closed-ness, not just a runtime check.
        static string Render(AsyncValue<int> v) => v switch
        {
            AsyncValue<int>.Loading => "L",
            AsyncValue<int>.Data d => $"D{d.Value}",
            AsyncValue<int>.Error e => $"E{e.Exception.Message}",
            AsyncValue<int>.Reloading r => $"R{r.Previous}",
        };

        Assert.Equal("L", Render(AsyncValue<int>.Loading.Instance));
        Assert.Equal("D1", Render(new AsyncValue<int>.Data(1)));
        Assert.Equal("Eboom", Render(new AsyncValue<int>.Error(new InvalidOperationException("boom"))));
        Assert.Equal("R9", Render(new AsyncValue<int>.Reloading(9)));
    }

    // ════════════════════════════════════════════════════════════════
    //  LoadState exhaustive pattern match (Phase 2.1)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void LoadState_Every_State_Is_Constructable_And_Discriminable()
    {
        LoadState loading = LoadState.Loading.Instance;
        LoadState idle = LoadState.Idle.Instance;
        LoadState end = LoadState.EndOfList.Instance;
        LoadState error = new LoadState.Error(new InvalidOperationException("x"));

        Assert.IsType<LoadState.Loading>(loading);
        Assert.IsType<LoadState.Idle>(idle);
        Assert.IsType<LoadState.EndOfList>(end);
        Assert.IsType<LoadState.Error>(error);
    }

    [Fact]
    public void LoadState_Exhaustive_Switch_Covers_All_Arms()
    {
        static string Label(LoadState s) => s switch
        {
            LoadState.Loading => "L",
            LoadState.Idle => "I",
            LoadState.EndOfList => "E",
            LoadState.Error e => $"X:{e.Exception.Message}",
        };

        Assert.Equal("L", Label(LoadState.Loading.Instance));
        Assert.Equal("I", Label(LoadState.Idle.Instance));
        Assert.Equal("E", Label(LoadState.EndOfList.Instance));
        Assert.Equal("X:boom", Label(new LoadState.Error(new InvalidOperationException("boom"))));
    }

    [Fact]
    public void LoadState_Singletons_Are_Identity_Equal()
    {
        // Idle / Loading / EndOfList have no payload; `.Instance` is the canonical value.
        // Record-equality also holds (empty records compare equal), but identity is the
        // usage contract — hook implementations compare via ReferenceEquals for the fast path.
        Assert.Same(LoadState.Loading.Instance, LoadState.Loading.Instance);
        Assert.Same(LoadState.Idle.Instance, LoadState.Idle.Instance);
        Assert.Same(LoadState.EndOfList.Instance, LoadState.EndOfList.Instance);

        Assert.Equal(LoadState.Loading.Instance, LoadState.Loading.Instance);
        Assert.Equal(LoadState.Idle.Instance, LoadState.Idle.Instance);
        Assert.Equal(LoadState.EndOfList.Instance, LoadState.EndOfList.Instance);
    }

    [Fact]
    public void LoadState_Error_Equality_Follows_Record_Rules()
    {
        var ex = new InvalidOperationException("same");
        var a = new LoadState.Error(ex);
        var b = new LoadState.Error(ex);
        Assert.Equal(a, b);

        // Different exception instances compare by reference on the Exception field.
        var c = new LoadState.Error(new InvalidOperationException("same"));
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void LoadState_Distinct_States_Are_Not_Equal()
    {
        LoadState idle = LoadState.Idle.Instance;
        LoadState loading = LoadState.Loading.Instance;
        LoadState end = LoadState.EndOfList.Instance;
        LoadState error = new LoadState.Error(new InvalidOperationException());

        Assert.NotEqual(idle, loading);
        Assert.NotEqual(idle, end);
        Assert.NotEqual(loading, end);
        Assert.NotEqual(error, idle);
        Assert.NotEqual(error, loading);
        Assert.NotEqual(error, end);
    }
}
