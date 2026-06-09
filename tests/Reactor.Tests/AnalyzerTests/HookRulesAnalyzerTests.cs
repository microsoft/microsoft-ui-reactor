using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Unit tests for the REACTOR_HOOKS_001/004/005 analyzer. Tests embed minimal stub
/// types in the <c>Microsoft.UI.Reactor.Core</c> namespace so the analyzer's
/// fully-qualified-name receiver check fires without pulling in the real framework.
/// </summary>
public class HookRulesAnalyzerTests
{
    private const string Stubs = @"
namespace Microsoft.UI.Reactor.Core
{
    public class RenderContext { }

    public abstract class Component
    {
        protected internal RenderContext Context { get; } = new RenderContext();
        public abstract string Render();
        protected (int, System.Action<int>) UseState(int initial) => (0, _ => { });
        protected void UseEffect(System.Action effect, params object[] deps) { }
        protected T UseMemo<T>(System.Func<T> factory, params object[] deps) => factory();
    }
}

namespace Microsoft.UI.Reactor.Hooks
{
    using Microsoft.UI.Reactor.Core;
    public static class Extensions
    {
        public static int UseCustom(this RenderContext ctx, object[] deps) => 0;
    }
}
";

    // Stubs for REACTOR_HOOKS_006. Separate constant so adding signatures here doesn't
    // shift line numbers baked into the other rules' WithSpan assertions.
    private const string ResourceStubs = @"
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.UI.Reactor.Core
{
    public class RenderContext { }

    public abstract class Component
    {
        protected internal RenderContext Context { get; } = new RenderContext();
        public abstract string Render();
        protected T UseResource<T>(System.Func<CancellationToken, Task<T>> fetcher, object[] deps) => default(T);
    }

    public static class Fakes
    {
        public static Task<int> GetUserAsync(CancellationToken ct) => Task.FromResult(0);
        public static Task<int> PostMessageAsync(CancellationToken ct) => Task.FromResult(0);
        public static Task<int> CreateOrderAsync(CancellationToken ct) => Task.FromResult(0);
        public static Task<int> DeleteItemAsync(int id, CancellationToken ct) => Task.FromResult(0);
        public static Task<int> GeneratePostalCodeAsync(CancellationToken ct) => Task.FromResult(0);
        public static Task<int> PostalLookupAsync(CancellationToken ct) => Task.FromResult(0);
    }
}
";

    private static DiagnosticResult Diagnostic(string id) =>
        CSharpAnalyzerVerifier<HookRulesAnalyzer, DefaultVerifier>.Diagnostic(id);

    // ── REACTOR_HOOKS_001 — conditional hook ──────────────────────────

    [Fact]
    public async Task Hook_Inside_If_Flags_Conditional()
    {
        var test = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        if (System.DateTime.Now.Ticks > 0)
        {
            var (count, setCount) = UseState(0);
        }
        return """";
    }
}";
        var expected = Diagnostic(HookRulesAnalyzer.ConditionalHookId)
            .WithSpan(31, 37, 31, 48)
            .WithArguments("UseState", "if");

        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Hook_Inside_ForEach_Flags_Conditional()
    {
        var test = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        foreach (var i in new[] { 1, 2, 3 })
        {
            var (count, setCount) = UseState(0);
        }
        return """";
    }
}";
        var expected = Diagnostic(HookRulesAnalyzer.ConditionalHookId)
            .WithSpan(31, 37, 31, 48)
            .WithArguments("UseState", "foreach loop");

        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Hook_Inside_Lambda_Flags_Conditional()
    {
        var test = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        System.Action a = () => UseEffect(() => { }, new object[0]);
        return """";
    }
}";
        // The lambda UseEffect call triggers both the conditional rule (it's inside a
        // nested lambda) and the unstable-deps rule (the `new object[0]` deps literal).
        var conditional = Diagnostic(HookRulesAnalyzer.ConditionalHookId)
            .WithSpan(29, 33, 29, 68)
            .WithArguments("UseEffect", "nested lambda/local function");
        var unstableDeps = Diagnostic(HookRulesAnalyzer.UnstableDepsId)
            .WithSpan(29, 54, 29, 67)
            .WithArguments("UseEffect", "array");

        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { conditional, unstableDeps },
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Hook_At_Top_Of_Render_No_Diagnostic()
    {
        var test = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    // ── REACTOR_HOOKS_004 — freshly-allocated deps ────────────────────

    [Fact]
    public async Task UseEffect_With_New_List_Dep_Flags()
    {
        var test = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        UseEffect(() => { }, new System.Collections.Generic.List<int>());
        return """";
    }
}";
        var expected = Diagnostic(HookRulesAnalyzer.UnstableDepsId)
            .WithSpan(29, 30, 29, 72)
            .WithArguments("UseEffect", "object");

        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task UseEffect_With_Lambda_Dep_Flags()
    {
        var test = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        UseEffect(() => { }, (System.Func<int>)(() => 1));
        return """";
    }
}";
        var expected = Diagnostic(HookRulesAnalyzer.UnstableDepsId)
            .WithSpan(29, 30, 29, 57)
            .WithArguments("UseEffect", "lambda");

        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task UseEffect_With_Scalar_Dep_No_Diagnostic()
    {
        var test = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        int userId = 42;
        UseEffect(() => { }, userId);
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    // ── REACTOR_HOOKS_005 — hook outside Render/custom hook ────────────

    [Fact]
    public async Task Hook_In_Event_Handler_Flags_Outside_Render()
    {
        var test = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    void HandleClick()
    {
        var (count, setCount) = UseState(0);
    }
    public override string Render() => """";
}";
        var expected = Diagnostic(HookRulesAnalyzer.HookOutsideRenderId)
            .WithSpan(29, 33, 29, 44)
            .WithArguments("UseState");

        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Hook_In_Custom_Use_Method_No_Diagnostic()
    {
        var test = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    int UseCustom()
    {
        var (count, setCount) = UseState(0);
        return count;
    }
    public override string Render()
    {
        var c = UseCustom();
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    // ── Non-hook lookalike ────────────────────────────────────────────

    // ── REACTOR_HOOKS_006 — non-idempotent fetcher ────────────────────

    [Fact]
    public async Task UseResource_With_Post_Method_Reference_Flags()
    {
        var test = ResourceStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var x = UseResource<int>(Microsoft.UI.Reactor.Core.Fakes.PostMessageAsync, new object[] { 1 });
        return """";
    }
}";
        // WithSpan points at the method-name identifier in the member-access expression.
        var expected = Diagnostic(HookRulesAnalyzer.NonIdempotentFetcherId)
            .WithSpan(31, 66, 31, 82)
            .WithArguments("UseResource", "PostMessage");

        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task UseResource_With_Lambda_Calling_DeleteAsync_Flags()
    {
        var test = ResourceStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var x = UseResource<int>(ct => Microsoft.UI.Reactor.Core.Fakes.DeleteItemAsync(1, ct), new object[] { 1 });
        return """";
    }
}";
        var expected = Diagnostic(HookRulesAnalyzer.NonIdempotentFetcherId)
            .WithSpan(31, 72, 31, 87)
            .WithArguments("UseResource", "DeleteItem");

        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task UseResource_With_Lambda_Calling_GenerateAsync_Flags()
    {
        var test = ResourceStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var x = UseResource<int>(ct => Microsoft.UI.Reactor.Core.Fakes.GeneratePostalCodeAsync(ct), new object[] { 1 });
        return """";
    }
}";
        var expected = Diagnostic(HookRulesAnalyzer.NonIdempotentFetcherId)
            .WithSpan(31, 72, 31, 95)
            .WithArguments("UseResource", "GeneratePostalCode");

        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ExpectedDiagnostics = { expected },
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task UseResource_With_Get_Method_No_Diagnostic()
    {
        var test = ResourceStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var x = UseResource<int>(ct => Microsoft.UI.Reactor.Core.Fakes.GetUserAsync(ct), new object[] { 1 });
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task UseResource_With_PostalLookup_Does_Not_Match_Post_Word_Boundary()
    {
        // Word-boundary guard: `PostalLookup` shares the `Post` prefix but the next
        // letter is lower-case, so it's not treated as a `Post<Word>` match.
        var test = ResourceStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var x = UseResource<int>(ct => Microsoft.UI.Reactor.Core.Fakes.PostalLookupAsync(ct), new object[] { 1 });
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    // ── Non-Reactor lookalike ────────────────────────────────────────

    [Fact]
    public async Task Non_Reactor_Use_Method_Not_Flagged()
    {
        // `UseAuthentication`-style helpers outside Reactor's type hierarchy should be
        // left alone.
        var test = @"
class Builder
{
    public Builder UseAuthentication() => this;
}
class Program
{
    static void Main()
    {
        // Called outside Render, inside an if — but receiver isn't a Reactor type.
        var b = new Builder();
        if (true) { b.UseAuthentication(); }
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    // ── REACTOR_HOOKS_008 — stale state read after setter ─────────────

    // Generic UseState/UsePersisted stubs so tests can exercise both the value form
    // (`UseState(0)`) and the delegate-value form used by the functional-arg exemption.
    // Kept separate from `Stubs` so its line count doesn't shift WithSpan assertions.
    private const string StaleStubs = @"
namespace Microsoft.UI.Reactor.Core
{
    public class RenderContext { }

    public abstract class Component
    {
        protected internal RenderContext Context { get; } = new RenderContext();
        public abstract string Render();
        protected (T Value, System.Action<T> Set) UseState<T>(T initial) => (initial, _ => { });
        protected (T Value, System.Action<T> Set) UsePersisted<T>(string key, T initial) => (initial, _ => { });
        protected (T Value, System.Action<System.Func<T, T>> Update) UseReducer<T>(T initial) => (initial, _ => { });
    }
}
";

    [Fact]
    public async Task Setter_Then_Read_Same_Block_Flags()
    {
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        setCount(5);
        System.Console.WriteLine({|REACTOR_HOOKS_008:count|});
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Read_In_If_Branch_After_Setter_Flags()
    {
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        setCount(5);
        if ({|REACTOR_HOOKS_008:count|} > 0) System.Console.WriteLine(""hi"");
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task UsePersisted_Setter_Then_Read_Flags()
    {
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (theme, setTheme) = UsePersisted(""theme"", 0);
        setTheme(1);
        System.Console.WriteLine({|REACTOR_HOOKS_008:theme|});
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Lambda_Value_Setter_Then_Read_Flags()
    {
        // UseState<T> returns Action<T>; when T is a delegate the lambda IS the new value,
        // so a later read of the state local is still stale. (Not a functional updater —
        // that is UseReducer.) The lambda argument must NOT suppress the diagnostic.
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (fn, setFn) = UseState<System.Func<int, int>>(x => x);
        setFn(p => p + 1);
        var stale = {|REACTOR_HOOKS_008:fn|};
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task UseReducer_Updater_Then_Read_Flags()
    {
        // UseReducer's updater also only queues a re-render, so reading the captured value
        // afterwards is stale.
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, updateCount) = UseReducer(0);
        updateCount(c => c + 1);
        System.Console.WriteLine({|REACTOR_HOOKS_008:count|});
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Immediately_Invoked_Lambda_Read_Flags()
    {
        // The lambda is assigned to a local and invoked synchronously before any rerender,
        // so the read inside its body runs now and is stale.
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        setCount(5);
        System.Action later = () => System.Console.WriteLine({|REACTOR_HOOKS_008:count|});
        later();
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Immediately_Invoked_Local_Function_Read_Flags()
    {
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        setCount(5);
        void Apply() => System.Console.WriteLine({|REACTOR_HOOKS_008:count|});
        Apply();
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Deferred_Callback_Not_Invoked_No_Diagnostic()
    {
        // The handler is handed off (not invoked) — it runs on a later render and sees the
        // fresh value, so the read inside it is not stale.
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    void Defer(System.Action a) { }
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        setCount(5);
        System.Action later = () => System.Console.WriteLine(count);
        Defer(later);
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Read_Before_Setter_No_Diagnostic()
    {
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        System.Console.WriteLine(count);
        setCount(5);
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Two_Setters_Then_Read_Flags_Once()
    {
        // Only the read after the LAST setter is reported; the first setter's scan stops
        // when it reaches the second setter call.
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        setCount(1);
        setCount(2);
        System.Console.WriteLine({|REACTOR_HOOKS_008:count|});
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Multiple_UseState_Pairs_Flags_Only_The_Set_One()
    {
        // setA is called; reading `a` afterwards is stale and flagged, but `b` (whose
        // setter was never called) is left alone.
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (a, setA) = UseState(0);
        var (b, setB) = UseState(0);
        setA(5);
        System.Console.WriteLine({|REACTOR_HOOKS_008:a|});
        System.Console.WriteLine(b);
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Setter_In_Event_Lambda_Then_Read_Flags()
    {
        // Mirrors the real-world ComboBox handler: setter and read share the lambda body.
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    void Apply(int v) { }
    public override string Render()
    {
        var (idx, setIdx) = UseState(0);
        System.Action<int> onChanged = i =>
        {
            setIdx(i);
            Apply({|REACTOR_HOOKS_008:idx|});
        };
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task ReSetter_With_Stale_Argument_Flags()
    {
        // `setCount(count + 1)` reads the stale `count` to compute the new value — the
        // increment anti-pattern. The read inside the second setter's argument is flagged.
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        setCount(1);
        setCount({|REACTOR_HOOKS_008:count|} + 1);
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Nested_ReSetter_In_If_Does_Not_Stop_Outer_Scan()
    {
        // A setter buried in a conditional branch may not run, so it must not stop the
        // outer scan from reaching the stale read after the if.
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        setCount(1);
        if (System.DateTime.Now.Ticks > 0) { setCount(2); }
        System.Console.WriteLine({|REACTOR_HOOKS_008:count|});
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Write_To_State_Local_No_Diagnostic()
    {
        // Assigning the local (`count = 5`) is a write, not a stale read.
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        setCount(1);
        count = 5;
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Setter_In_Expression_Lambda_Then_Outer_Read_No_Diagnostic()
    {
        // The setter runs later (inside the event handler). The read happens during the
        // current render, before the handler fires, so it is not stale.
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        System.Action handler = () => setCount(1);
        System.Console.WriteLine(count);
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Compound_Assignment_And_Increment_Read_Flag()
    {
        // `+=`, `++`, and `--` all read the current value, so they are stale reads.
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        setCount(5);
        {|REACTOR_HOOKS_008:count|} += 1;
        {|REACTOR_HOOKS_008:count|}++;
        --{|REACTOR_HOOKS_008:count|};
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Member_Access_Tuple_And_Switch_Reads_Flag()
    {
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    void Use(object o) { }
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        setCount(5);
        Use({|REACTOR_HOOKS_008:count|}.ToString());
        Use(({|REACTOR_HOOKS_008:count|}, 1));
        var label = {|REACTOR_HOOKS_008:count|} switch { 0 => ""zero"", _ => ""other"" };
        return label;
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Out_Argument_Is_A_Write_No_Diagnostic()
    {
        // `out count` assigns the local; it is not a stale read.
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        setCount(5);
        System.Int32.TryParse(""7"", out count);
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Expression_Bodied_Local_Function_Returning_State_Flags()
    {
        // The whole body is the state identifier; invoking it synchronously reads it stale.
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        setCount(5);
        int Get() => {|REACTOR_HOOKS_008:count|};
        System.Console.WriteLine(Get());
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Expression_Bodied_Lambda_Local_Returning_State_Flags()
    {
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        setCount(5);
        System.Func<int> get = () => {|REACTOR_HOOKS_008:count|};
        var x = get();
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }

    [Fact]
    public async Task Invoke_Form_Of_Lambda_Local_Read_Flags()
    {
        // `later.Invoke()` is the explicit delegate-call form of `later()`.
        var test = StaleStubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        setCount(5);
        System.Action later = () => System.Console.WriteLine({|REACTOR_HOOKS_008:count|});
        later.Invoke();
        return """";
    }
}";
        var analyzerTest = new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
        };
        await analyzerTest.RunAsync();
    }
}
