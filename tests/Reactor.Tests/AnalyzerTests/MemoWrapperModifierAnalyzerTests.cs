using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="MemoWrapperModifierAnalyzer"/> (<c>REACTOR_MEMO_001</c>) and its
/// <see cref="MemoWrapperModifierCodeFix"/>. Stubs both <c>Memo</c> overloads — the keyed
/// <c>Memo&lt;TKey&gt;(TKey, Func&lt;Element&gt;)</c> → <c>KeyedMemoElement</c> and the non-keyed
/// <c>Memo(Func&lt;RenderContext, Element&gt;, params object[])</c> → <c>MemoElement</c> — so the
/// analyzer's semantic keyed-overload discrimination is exercised for real.
/// </summary>
public class MemoWrapperModifierAnalyzerTests
{
    // `IsExternalInit` lets the stub sources use `record`. The two Memo overloads plus a couple of
    // fluent modifiers reproduce the real DSL shape well enough for overload resolution to pick the
    // keyed vs non-keyed method exactly as the framework would.
    private const string Stubs = @"
namespace System.Runtime.CompilerServices
{
    public static class IsExternalInit { }
}

namespace Microsoft.UI.Reactor.Core
{
    public sealed class RenderContext { }

    public abstract record Element { }
    public sealed record MemoElement(System.Func<RenderContext, Element> Render) : Element { }
    public sealed record KeyedMemoElement(object MemoKey, System.Func<Element> Factory) : Element { }

    public static class Factories
    {
        public static MemoElement Memo(System.Func<RenderContext, Element> render, params object[] dependencies) => null!;
        public static KeyedMemoElement Memo<TKey>(TKey key, System.Func<Element> factory) => null!;
        // Synthetic non-keyed look-alike: same (value, () => Element) call shape as the keyed
        // overload but returns MemoElement, so only the analyzer's return-type check distinguishes
        // it. A `bool` key binds this non-generic overload ahead of the generic keyed one.
        public static MemoElement Memo(bool flag, System.Func<Element> factory) => null!;
        public static Element Text(string s) => null!;
    }

    public static class ElementExtensions
    {
        public static T Padding<T>(this T el, int value) where T : Element => el;
        public static T Margin<T>(this T el, int value) where T : Element => el;
    }
}
";

    private static string Program(string body) => Stubs + @"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public record Item(string Id);

    public static class C
    {
        static Element Row(Item item) => Text(item.Id);
        static Element BuildRow() => Text(""x"");

" + body + @"
    }
}";

    // ── Analyzer positive ──────────────────────────────────────────────

    [Fact]
    public async Task Fires_On_Modifier_Applied_To_Keyed_Memo_Wrapper()
    {
        var source = Program(@"
        public static Element Build(Item item)
            => Memo(item.Id, () => Row(item)).{|REACTOR_MEMO_001:Padding|}(8);");

        await new CSharpAnalyzerTest<MemoWrapperModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_Once_On_Innermost_Modifier_Of_A_Chain()
    {
        // Only the modifier whose receiver is the bare Memo(...) call fires — not each link.
        var source = Program(@"
        public static Element Build(Item item)
            => Memo(item.Id, () => Row(item)).{|REACTOR_MEMO_001:Padding|}(8).Margin(4);");

        await new CSharpAnalyzerTest<MemoWrapperModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Fully_Qualified_Factories_Memo_Call()
    {
        // Member-access callee form `Factories.Memo(...)` (not the `using static` identifier form).
        var source = Program(@"
        public static Element Build(Item item)
            => Factories.Memo(item.Id, () => Row(item)).{|REACTOR_MEMO_001:Padding|}(8);");

        await new CSharpAnalyzerTest<MemoWrapperModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_On_Keyed_Memo_With_OutOfOrder_Named_Arguments()
    {
        // The factory is located by shape, not position, so named/out-of-order args still fire.
        var source = Program(@"
        public static Element Build(Item item)
            => Memo(factory: () => Row(item), key: item.Id).{|REACTOR_MEMO_001:Padding|}(8);");

        await new CSharpAnalyzerTest<MemoWrapperModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Analyzer negatives ─────────────────────────────────────────────

    [Fact]
    public async Task No_Diagnostic_On_NonKeyed_Memo_Overload()
    {
        // The non-keyed Memo(ctx => ...) overload returns MemoElement and never participates in the
        // cross-recycle cache, so decorating it is fine. This is the critical semantic near-miss:
        // syntactically identical `Memo(...).Padding(8)`, but the wrong overload.
        var source = Program(@"
        public static Element Build(Item item)
            => Memo(ctx => Row(item)).Padding(8);");

        await new CSharpAnalyzerTest<MemoWrapperModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_On_Bare_Keyed_Memo_Without_Modifier()
    {
        var source = Program(@"
        public static Element Build(Item item)
            => Memo(item.Id, () => Row(item));");

        await new CSharpAnalyzerTest<MemoWrapperModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_TwoArg_Memo_Resolves_To_NonKeyed_Return()
    {
        // Exercises the SEMANTIC return-type guard specifically: this call passes every syntactic
        // gate (2 args, parameterless-lambda factory, trailing Element modifier) yet resolves to the
        // non-keyed look-alike returning MemoElement, so only the KeyedMemoElement check stops it.
        var source = Program(@"
        public static Element Build(Item item)
            => Memo(true, () => Row(item)).Padding(8);");

        await new CSharpAnalyzerTest<MemoWrapperModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Analyzer near-misses (almost trip the syntactic fast path) ─────

    [Fact]
    public async Task No_Diagnostic_When_Factory_Is_A_Method_Group()
    {
        // Opaque factory — there is no lambda body to move the modifier into, so the rule bails
        // rather than fire an unfixable Info.
        var source = Program(@"
        public static Element Build(Item item)
            => Memo(item.Id, BuildRow).Padding(8);");

        await new CSharpAnalyzerTest<MemoWrapperModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_When_Trailing_Call_Is_Not_An_Element_Modifier()
    {
        // `.ToString()` returns a string, not an Element — not a wrapper-decorating modifier.
        var source = Program(@"
        public static string Build(Item item)
            => Memo(item.Id, () => Row(item)).ToString();");

        await new CSharpAnalyzerTest<MemoWrapperModifierAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Code-fix round-trips ───────────────────────────────────────────

    [Fact]
    public async Task CodeFix_Moves_Single_Modifier_Into_Factory()
    {
        var before = Program(@"
        public static Element Build(Item item)
            => Memo(item.Id, () => Row(item)).{|REACTOR_MEMO_001:Padding|}(8);");

        var after = Program(@"
        public static Element Build(Item item)
            => Memo(item.Id, () => Row(item).Padding(8));");

        await new CSharpCodeFixTest<MemoWrapperModifierAnalyzer, MemoWrapperModifierCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Moves_Chained_Modifiers_Into_Factory()
    {
        var before = Program(@"
        public static Element Build(Item item)
            => Memo(item.Id, () => Row(item)).{|REACTOR_MEMO_001:Padding|}(8).Margin(4);");

        var after = Program(@"
        public static Element Build(Item item)
            => Memo(item.Id, () => Row(item).Padding(8).Margin(4));");

        await new CSharpCodeFixTest<MemoWrapperModifierAnalyzer, MemoWrapperModifierCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Moves_Modifier_Into_A_Block_Body_Factory()
    {
        var before = Program(@"
        public static Element Build(Item item)
            => Memo(item.Id, () => { return Row(item); }).{|REACTOR_MEMO_001:Padding|}(8);");

        var after = Program(@"
        public static Element Build(Item item)
            => Memo(item.Id, () => { return Row(item).Padding(8); });");

        await new CSharpCodeFixTest<MemoWrapperModifierAnalyzer, MemoWrapperModifierCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Parenthesizes_A_NonPrimary_Factory_Body()
    {
        // A conditional factory body must be wrapped so the moved modifier binds to the whole
        // expression, not just the else-branch (operator precedence).
        var before = Program(@"
        public static Element Build(Item item)
            => Memo(item.Id, () => item.Id.Length > 0 ? Row(item) : Text(""e"")).{|REACTOR_MEMO_001:Padding|}(8);");

        var after = Program(@"
        public static Element Build(Item item)
            => Memo(item.Id, () => (item.Id.Length > 0 ? Row(item) : Text(""e"")).Padding(8));");

        await new CSharpCodeFixTest<MemoWrapperModifierAnalyzer, MemoWrapperModifierCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Leaves_A_Trailing_NonElement_Call_Outside_The_Factory()
    {
        // The walk must stop at the last Element modifier: `.ToString()` returns a string, so pulling
        // it into the factory would break Func<Element>. It stays on the (now bare) wrapper.
        var before = Program(@"
        public static string Build(Item item)
            => Memo(item.Id, () => Row(item)).{|REACTOR_MEMO_001:Padding|}(8).ToString();");

        var after = Program(@"
        public static string Build(Item item)
            => Memo(item.Id, () => Row(item).Padding(8)).ToString();");

        await new CSharpCodeFixTest<MemoWrapperModifierAnalyzer, MemoWrapperModifierCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Targets_The_Factory_Argument_By_Identity_With_Named_Args()
    {
        // Out-of-order named args: the fix must rewrite the `factory:` argument (located by
        // identity), leaving the `key:` argument untouched.
        var before = Program(@"
        public static Element Build(Item item)
            => Memo(factory: () => Row(item), key: item.Id).{|REACTOR_MEMO_001:Padding|}(8);");

        var after = Program(@"
        public static Element Build(Item item)
            => Memo(factory: () => Row(item).Padding(8), key: item.Id);");

        await new CSharpCodeFixTest<MemoWrapperModifierAnalyzer, MemoWrapperModifierCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
