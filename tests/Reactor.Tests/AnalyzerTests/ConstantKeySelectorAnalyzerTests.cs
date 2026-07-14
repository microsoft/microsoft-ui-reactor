using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="ConstantKeySelectorAnalyzer"/> (<c>REACTOR_DSL_003</c>). Stubs
/// a minimal <c>Microsoft.UI.Reactor.Factories</c> surface — the typed keySelector
/// overload the rule targets, plus the <c>IReactorKeyed</c> viewBuilder overload and
/// the untyped selection overload it must <em>not</em> confuse with it — so the
/// analyzer's syntactic pre-gate and semantic confirmation both fire without pulling
/// the framework in.
/// </summary>
public class ConstantKeySelectorAnalyzerTests
{
    private const string Stubs = @"
using System;
using System.Collections.Generic;

namespace Microsoft.UI.Reactor
{
    public abstract class Element { }
    public sealed class TemplatedListViewElement<T> : Element { }
    public sealed class TemplatedGridViewElement<T> : Element { }
    public sealed class TemplatedTreeViewElement<T> : Element { }
    public interface IReactorKeyed { string Key { get; } }

    public static class Factories
    {
        // The typed keySelector overload REACTOR_DSL_003 targets.
        public static TemplatedListViewElement<T> ListView<T>(
            IReadOnlyList<T> items, Func<T, string> keySelector, Func<T, int, Element> viewBuilder)
            => new TemplatedListViewElement<T>();

        // IReactorKeyed overload — the 2nd arg is a two-parameter viewBuilder, not a keySelector.
        public static TemplatedListViewElement<T> ListView<T>(
            IReadOnlyList<T> items, Func<T, int, Element> viewBuilder) where T : IReactorKeyed
            => new TemplatedListViewElement<T>();

        // Untyped selection overload — the 2nd arg is Action<int> onSelectedIndexChanged.
        public static Element ListView(int? selectedIndex, Action<int> onSelectedIndexChanged, params Element[] items)
            => null;

        public static TemplatedGridViewElement<T> GridView<T>(
            IReadOnlyList<T> items, Func<T, string> keySelector, Func<T, int, Element> viewBuilder)
            => new TemplatedGridViewElement<T>();

        // Different shape: keySelector is still positional index 1, but there is a
        // childrenSelector at index 2 and a single-parameter viewBuilder at index 3.
        public static TemplatedTreeViewElement<T> TreeView<T>(
            IReadOnlyList<T> items, Func<T, string> keySelector,
            Func<T, IReadOnlyList<T>> childrenSelector, Func<T, Element> viewBuilder)
            => new TemplatedTreeViewElement<T>();

        public static Element Text(string s) => null;
    }
}

namespace Other
{
    using Microsoft.UI.Reactor;
    // A same-named method on an unrelated type — must never trip the rule.
    public static class NotReactor
    {
        public static Element ListView<T>(
            System.Collections.Generic.IReadOnlyList<T> items,
            System.Func<T, string> keySelector,
            System.Func<T, int, Element> viewBuilder) => null;
    }
}

namespace TestApp
{
    using Microsoft.UI.Reactor;
    public sealed class Item : IReactorKeyed { public string Id { get; set; } public string Key => Id; }
    public static class Keys { public const string Row = ""row""; public static string KeyOf(Item i) => i.Id; }
    public static class Config { public const string x = ""row""; }
}
";

    private static Task Verify(string body) =>
        new CSharpAnalyzerTest<ConstantKeySelectorAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + @"
namespace TestApp
{
    using System.Collections.Generic;
    using Microsoft.UI.Reactor;
    using static Microsoft.UI.Reactor.Factories;

    public static class C
    {
        public static Element Build(IReadOnlyList<Item> items)
        {
" + body + @"
            return null;
        }
    }
}",
        }.RunAsync(TestContext.Current.CancellationToken);

    // ── Positive: fires ────────────────────────────────────────────────

    [Fact]
    public Task Fires_For_Constant_String_Literal() =>
        Verify(@"            ListView(items, {|REACTOR_DSL_003:_ => ""row""|}, (i, idx) => Text(i.Id));");

    [Fact]
    public Task Fires_For_Null_Literal() =>
        Verify(@"            ListView(items, {|REACTOR_DSL_003:i => null|}, (i, idx) => Text(i.Id));");

    [Fact]
    public Task Fires_When_Ignoring_Param_And_Returning_A_Constant_Field() =>
        // Never reads its item, returns a compile-time constant → duplicate keys.
        Verify(@"            ListView(items, {|REACTOR_DSL_003:_ => Keys.Row|}, (i, idx) => Text(i.Id));");

    [Fact]
    public Task Fires_For_Parenthesized_Single_Param_Lambda() =>
        Verify(@"            ListView(items, {|REACTOR_DSL_003:(item) => ""row""|}, (i, idx) => Text(i.Id));");

    [Fact]
    public Task Fires_For_Named_KeySelector_Argument() =>
        Verify(@"            ListView(items, keySelector: {|REACTOR_DSL_003:_ => ""row""|}, viewBuilder: (i, idx) => Text(i.Id));");

    [Fact]
    public Task Fires_For_GridView_Too() =>
        Verify(@"            GridView(items, {|REACTOR_DSL_003:_ => ""g""|}, (i, idx) => Text(i.Id));");

    [Fact]
    public Task Fires_For_TreeView_With_Different_Signature() =>
        // TreeView<T> keeps keySelector at positional index 1 despite a
        // childrenSelector at index 2 and a single-parameter viewBuilder at index 3.
        Verify(@"            TreeView(items, {|REACTOR_DSL_003:_ => ""root""|}, i => items, i => Text(i.Id));");

    [Fact]
    public Task Fires_For_Block_Body_Returning_Constant() =>
        Verify(@"            ListView(items, {|REACTOR_DSL_003:_ => { return ""row""; }|}, (i, idx) => Text(i.Id));");

    [Fact]
    public Task Fires_When_Member_Name_Matches_Param_Name() =>
        // `x => Config.x` — the `x` after the dot is a member name, not a reference to
        // the lambda parameter, so the selector still ignores its item (IsMemberName arm).
        Verify(@"            ListView(items, {|REACTOR_DSL_003:x => Config.x|}, (i, idx) => Text(i.Id));");

    // ── Negative: does not fire ────────────────────────────────────────

    [Fact]
    public Task No_Diagnostic_When_Selector_Reads_The_Item() =>
        Verify(@"            ListView(items, i => i.Id, (i, idx) => Text(i.Id));");

    [Fact]
    public Task No_Diagnostic_When_Item_Used_In_Interpolation() =>
        Verify(@"            ListView(items, i => $""row-{i.Id}"", (i, idx) => Text(i.Id));");

    [Fact]
    public Task No_Diagnostic_When_Item_Passed_To_Helper() =>
        // Parameter is used indirectly via a helper — bail (also an invocation).
        Verify(@"            ListView(items, i => Keys.KeyOf(i), (i, idx) => Text(i.Id));");

    [Fact]
    public Task No_Diagnostic_When_Body_Has_Opaque_Helper_Call() =>
        // Ignores the item but calls a helper that could return unique values — bail.
        Verify(@"            ListView(items, _ => System.Guid.NewGuid().ToString(), (i, idx) => Text(i.Id));");

    [Fact]
    public Task No_Diagnostic_When_Body_Mutates_State_Per_Call() =>
        // Ignores the item but produces a unique key per call via increment — this is
        // NOT the duplicate-key bug the rule reports, so it must not fire.
        Verify(@"            var n = 0;
            ListView(items, _ => $""{n++}"", (i, idx) => Text(i.Id));");

    [Fact]
    public Task No_Diagnostic_For_IReactorKeyed_ViewBuilder_Overload() =>
        // 2nd arg is the two-parameter viewBuilder, not a keySelector.
        Verify(@"            ListView(items, (i, idx) => Text(i.Id));");

    [Fact]
    public Task No_Diagnostic_For_Untyped_Selection_OnSelectedIndexChanged_Discard() =>
        // Near-miss: a param-ignoring one-arg lambda in the 2nd slot, but it binds to
        // Action<int> onSelectedIndexChanged on the untyped overload — not keySelector.
        Verify(@"            ListView(0, _ => { }, Text(""a""), Text(""b""));");

    [Fact]
    public Task No_Diagnostic_For_Method_Group_Selector() =>
        // A method group (not a lambda) is not analyzed.
        Verify(@"            ListView(items, Keys.KeyOf, (i, idx) => Text(i.Id));");

    [Fact]
    public Task No_Diagnostic_For_Same_Named_Method_On_Unrelated_Type() =>
        Verify(@"            Other.NotReactor.ListView(items, _ => ""row"", (i, idx) => Text(i.Id));");

    [Fact]
    public Task No_Diagnostic_For_Non_Factory_Method_Name() =>
        // Cheap name gate: a constant single-param lambda passed to a method whose
        // name isn't a typed collection factory (here Enumerable.Select) is skipped
        // before any lambda/body scan or semantic lookup.
        Verify(@"            System.Linq.Enumerable.Select(items, _ => ""row"");");
}
