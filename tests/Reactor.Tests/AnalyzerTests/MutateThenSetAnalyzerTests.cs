using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <c>REACTOR_HOOKS_010</c> (mutate-then-set reference state) and
/// <see cref="MutateThenSetCodeFix"/>. The state is seeded from a field so the initial value is
/// not itself an allocation (which would also trip <c>REACTOR_HOOKS_013</c>).
/// </summary>
public class MutateThenSetAnalyzerTests
{
    private const string Stubs = @"
namespace Microsoft.UI.Reactor.Core
{
    public class RenderContext { }

    // A value type is COPIED when passed to the setter, so a mutated copy differs from the stored
    // original and re-renders correctly — mutate-then-set is not the silent-miss bug here.
    public struct ValueList
    {
        public void Add(string s) { }
    }

    // A reference collection with .Add but NO parameterless constructor — cannot be built from a
    // collection expression, so the fix must be withheld (the diagnostic still fires).
    public class NoCtorList : System.Collections.IEnumerable
    {
        public NoCtorList(int capacity) { }
        public void Add(string s) { }
        public System.Collections.IEnumerator GetEnumerator() => null;
    }

    public abstract class Component
    {
        protected internal RenderContext Context { get; } = new RenderContext();
        public abstract string Render();
        protected (T Value, System.Action<T> Set) UseState<T>(T initialValue, bool threadSafe = false) => (initialValue, _ => { });
        protected (T Value, System.Action<T> Set) UsePersisted<T>(string key, T initialValue) => (initialValue, _ => { });

        protected System.Collections.Generic.List<string> Seed = new System.Collections.Generic.List<string>();
        protected ValueList ValueSeed = new ValueList();
        protected NoCtorList NoCtorSeed = new NoCtorList(1);
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
    public async Task Add_Then_Set_Same_Reference_Flags()
    {
        await VerifyAnalyzer(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(Seed);
        items.Add(""x"");
        {|REACTOR_HOOKS_010:setItems(items)|};
        return """";
    }
}");
    }

    [Fact]
    public async Task Clear_Then_Set_Same_Reference_Flags()
    {
        await VerifyAnalyzer(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(Seed);
        items.Clear();
        {|REACTOR_HOOKS_010:setItems(items)|};
        return """";
    }
}");
    }

    // UsePersisted also yields a (value, setter) pair whose setter compares by reference.
    [Fact]
    public async Task UsePersisted_Add_Then_Set_Same_Reference_Flags()
    {
        await VerifyAnalyzer(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UsePersisted(""k"", Seed);
        items.Add(""x"");
        {|REACTOR_HOOKS_010:setItems(items)|};
        return """";
    }
}");
    }

    [Fact]
    public async Task Indexer_Set_Then_Set_Same_Reference_Flags()
    {
        await VerifyAnalyzer(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(Seed);
        items[0] = ""x"";
        {|REACTOR_HOOKS_010:setItems(items)|};
        return """";
    }
}");
    }

    // Negative: a value type is copied when passed to the setter, so the mutated copy differs from
    // the stored original and re-renders correctly.
    [Fact]
    public async Task Mutate_Then_Set_ValueType_DoesNotFlag()
    {
        await VerifyAnalyzer(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(ValueSeed);
        items.Add(""x"");
        setItems(items);
        return """";
    }
}");
    }

    // Negative: a defensive copy before the mutation means the setter receives a NEW reference.
    [Fact]
    public async Task Defensive_Copy_Before_Mutation_DoesNotFlag()
    {
        await VerifyAnalyzer(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(Seed);
        items = new System.Collections.Generic.List<string>(items);
        items.Add(""x"");
        setItems(items);
        return """";
    }
}");
    }

    // Near-miss: setX(x) where x is NOT a state local (setX and x are ordinary locals).
    [Fact]
    public async Task Setter_Not_From_UseState_DoesNotFlag()
    {
        await VerifyAnalyzer(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var items = new System.Collections.Generic.List<string>();
        System.Action<System.Collections.Generic.List<string>> setItems = _ => { };
        items.Add(""x"");
        setItems(items);
        return """";
    }
}");
    }

    [Fact]
    public async Task CodeFix_Rewrites_Add_To_New_Collection_Value()
    {
        var before = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(Seed);
        items.Add(""x"");
        {|REACTOR_HOOKS_010:setItems(items)|};
        return """";
    }
}";

        var after = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(Seed);
        setItems([.. items, ""x""]);
        return """";
    }
}";

        await new CSharpCodeFixTest<HookRulesAnalyzer, MutateThenSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CodeActionEquivalenceKey = HookRulesAnalyzer.MutateThenSetId,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // A non-.Add mutator (here .Clear) still fires the warning but offers no auto-fix — there is no
    // single value to fold into a collection expression.
    [Fact]
    public async Task CodeFix_Not_Offered_For_NonAdd_Mutator()
    {
        var source = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(Seed);
        items.Clear();
        {|REACTOR_HOOKS_010:setItems(items)|};
        return """";
    }
}";

        await new CSharpCodeFixTest<HookRulesAnalyzer, MutateThenSetCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // A comment on the mutated line is preserved rather than silently dropped by the removal.
    [Fact]
    public async Task CodeFix_Preserves_Comment_On_Mutator_Line()
    {
        var before = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(Seed);
        // keep me
        items.Add(""x"");
        {|REACTOR_HOOKS_010:setItems(items)|};
        return """";
    }
}";

        var after = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(Seed);
        // keep me
        
        setItems([.. items, ""x""]);
        return """";
    }
}";

        await new CSharpCodeFixTest<HookRulesAnalyzer, MutateThenSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CodeActionEquivalenceKey = HookRulesAnalyzer.MutateThenSetId,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // An inline trailing comment on the mutated line is preserved (Roslyn keeps it as its own line).
    [Fact]
    public async Task CodeFix_Preserves_Trailing_Comment_On_Mutator_Line()
    {
        var before = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(Seed);
        items.Add(""x""); // keep me
        {|REACTOR_HOOKS_010:setItems(items)|};
        return """";
    }
}";

        var after = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(Seed);
         // keep me
        setItems([.. items, ""x""]);
        return """";
    }
}";

        await new CSharpCodeFixTest<HookRulesAnalyzer, MutateThenSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CodeActionEquivalenceKey = HookRulesAnalyzer.MutateThenSetId,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // The fix is withheld when the state type cannot be built from a collection expression (here a
    // collection with no parameterless constructor); the warning still fires.
    [Fact]
    public async Task CodeFix_Not_Offered_When_Type_Has_No_Collection_Expression()
    {
        var source = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(NoCtorSeed);
        items.Add(""x"");
        {|REACTOR_HOOKS_010:setItems(items)|};
        return """";
    }
}";

        await new CSharpCodeFixTest<HookRulesAnalyzer, MutateThenSetCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // A #directive around the mutated line is preserved (not dropped) — Roslyn's directive-aware
    // removal keeps the region balanced.
    [Fact]
    public async Task CodeFix_Preserves_Directive_On_Mutator_Line()
    {
        var before = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(Seed);
#region seed
        items.Add(""x"");
#endregion
        {|REACTOR_HOOKS_010:setItems(items)|};
        return """";
    }
}";

        var after = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (items, setItems) = UseState(Seed);
#region seed
        
#endregion
        setItems([.. items, ""x""]);
        return """";
    }
}";

        await new CSharpCodeFixTest<HookRulesAnalyzer, MutateThenSetCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CodeActionEquivalenceKey = HookRulesAnalyzer.MutateThenSetId,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
