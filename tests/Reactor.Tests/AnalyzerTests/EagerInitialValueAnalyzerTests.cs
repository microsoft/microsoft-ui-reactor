using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <c>REACTOR_HOOKS_013</c> (eager-allocated <c>UseState</c>/<c>UsePersisted</c> initial
/// value) and <see cref="EagerInitialValueCodeFix"/>. The initial value is bound by PARAMETER name,
/// so the arg-0 (<c>UseState</c>) vs arg-1 (<c>UsePersisted</c>) difference is exercised.
/// </summary>
public class EagerInitialValueAnalyzerTests
{
    private const string Stubs = @"
namespace Microsoft.UI.Reactor.Core
{
    public class RenderContext { }

    public struct Box { public int V; public Box(int v) { V = v; } }

    public abstract class Component
    {
        protected internal RenderContext Context { get; } = new RenderContext();
        public abstract string Render();
        protected (T Value, System.Action<T> Set) UseState<T>(T initialValue, bool threadSafe = false) => (initialValue, _ => { });
        protected (T Value, System.Action<T> Set) UsePersisted<T>(string key, T initialValue) => (initialValue, _ => { });
        protected T UseMemo<T>(System.Func<T> factory, params object[] dependencies) => factory();
    }
}
";

    private static Task VerifyAnalyzer(string body) =>
        new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + body,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        }.RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task UseState_With_Fresh_List_Flags()
    {
        await VerifyAnalyzer(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState({|REACTOR_HOOKS_013:new System.Collections.Generic.List<string>()|});
        return """";
    }
}");
    }

    // UsePersisted's initial value is arg 1 (arg 0 is the key) — the key string is not flagged.
    [Fact]
    public async Task UsePersisted_With_Fresh_List_Flags_Arg1()
    {
        await VerifyAnalyzer(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UsePersisted(""k"", {|REACTOR_HOOKS_013:new System.Collections.Generic.List<string>()|});
        return """";
    }
}");
    }

    // Negative: a scalar initial value is not an allocation.
    [Fact]
    public async Task UseState_With_Scalar_DoesNotFlag()
    {
        await VerifyAnalyzer(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (count, setCount) = UseState(0);
        return """";
    }
}");
    }

    // Near-miss: a ValueTuple lives on the stack — the restricted classifier must NOT treat it as
    // a heap allocation (the whole point of the restricted variant).
    [Fact]
    public async Task UseState_With_Tuple_DoesNotFlag()
    {
        await VerifyAnalyzer(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (state, setState) = UseState((0, """"));
        return """";
    }
}");
    }

    // Negative: a value-type creation (new struct) does not heap-allocate, so it is not the footgun.
    [Fact]
    public async Task UseState_With_ValueType_Creation_DoesNotFlag()
    {
        await VerifyAnalyzer(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (box, setBox) = UseState(new Microsoft.UI.Reactor.Core.Box(1));
        return """";
    }
}");
    }

    // Negative: a stable reference (field / existing value) is not a fresh allocation.
    [Fact]
    public async Task UseState_With_Existing_Reference_DoesNotFlag()
    {
        await VerifyAnalyzer(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    private System.Collections.Generic.List<string> _seed = new System.Collections.Generic.List<string>();
    public override string Render()
    {
        var (items, setItems) = UseState(_seed);
        return """";
    }
}");
    }

    [Fact]
    public async Task CodeFix_Wraps_Initial_Value_In_UseMemo()
    {
        var before = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState({|REACTOR_HOOKS_013:new System.Collections.Generic.List<string>()|});
        return """";
    }
}";

        var after = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(UseMemo(() => new System.Collections.Generic.List<string>(), []));
        return """";
    }
}";

        await new CSharpCodeFixTest<HookRulesAnalyzer, EagerInitialValueCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CodeActionEquivalenceKey = HookRulesAnalyzer.EagerInitialValueId,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Wraps_UsePersisted_Initial_Value()
    {
        var before = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UsePersisted(""k"", {|REACTOR_HOOKS_013:new System.Collections.Generic.List<string>()|});
        return """";
    }
}";

        var after = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UsePersisted(""k"", UseMemo(() => new System.Collections.Generic.List<string>(), []));
        return """";
    }
}";

        await new CSharpCodeFixTest<HookRulesAnalyzer, EagerInitialValueCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CodeActionEquivalenceKey = HookRulesAnalyzer.EagerInitialValueId,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // A target-typed `new()` must be expanded to an explicit UseMemo<T> so the wrapped call compiles.
    [Fact]
    public async Task CodeFix_TargetTyped_New_Emits_Explicit_Type_Argument()
    {
        var before = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState<System.Collections.Generic.List<string>>({|REACTOR_HOOKS_013:new()|});
        return """";
    }
}";

        var after = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState<System.Collections.Generic.List<string>>(UseMemo<global::System.Collections.Generic.List<string>>(() => new(), []));
        return """";
    }
}";

        await new CSharpCodeFixTest<HookRulesAnalyzer, EagerInitialValueCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CodeActionEquivalenceKey = HookRulesAnalyzer.EagerInitialValueId,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // The fix is withheld when the initializer calls a method (potential side effect): wrapping in
    // UseMemo(..., []) would change how often that method runs. The diagnostic still fires.
    [Fact]
    public async Task CodeFix_Withheld_When_Initializer_Calls_A_Method()
    {
        var source = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    int GetSeed() => 4;
    public override string Render()
    {
        var (items, setItems) = UseState({|REACTOR_HOOKS_013:new System.Collections.Generic.List<int>(GetSeed())|});
        return """";
    }
}";

        await new CSharpCodeFixTest<HookRulesAnalyzer, EagerInitialValueCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // A comment inside the initializer is preserved by the fix (ToString keeps internal trivia).
    [Fact]
    public async Task CodeFix_Preserves_Comment_Inside_Initializer()
    {
        var before = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState({|REACTOR_HOOKS_013:new System.Collections.Generic.List<string>(/* keep */)|});
        return """";
    }
}";

        var after = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(UseMemo(() => new System.Collections.Generic.List<string>(/* keep */), []));
        return """";
    }
}";

        await new CSharpCodeFixTest<HookRulesAnalyzer, EagerInitialValueCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CodeActionEquivalenceKey = HookRulesAnalyzer.EagerInitialValueId,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
