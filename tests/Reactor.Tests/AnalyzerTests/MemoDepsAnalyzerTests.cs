using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <c>REACTOR_HOOKS_012</c> (Memo dependency lacks value equality). Stubs the two
/// <c>Factories.Memo</c> overloads so the analyzer disambiguates the <c>params object?[]
/// dependencies</c> target from the keyed <c>Memo&lt;TKey&gt;</c> (which is excluded).
/// </summary>
public class MemoDepsAnalyzerTests
{
    private const string Stubs = @"
namespace Microsoft.UI.Reactor.Core
{
    public class RenderContext { }
    public abstract record Element { }
    public sealed record TextElement(string Text) : Element;
    public record Data(int X);
    // IEquatable<T> WITHOUT an Equals(object) override — memo deps use object.Equals, which falls
    // back to reference equality here, so a fresh one DOES defeat the memo.
    public class EquatableDep : System.IEquatable<EquatableDep>
    {
        public bool Equals(EquatableDep other) => true;
    }
    // Implements a look-alike IEquatable<T> from a NON-System namespace — must not be treated as
    // value equality, so a fresh one still fires.
    public class FakeEquatableDep : FakeEq.IEquatable<FakeEquatableDep>
    {
        public bool Equals(FakeEquatableDep other) => true;
    }
}

namespace FakeEq
{
    public interface IEquatable<T> { bool Equals(T other); }
}

namespace Microsoft.UI.Reactor
{
    using Microsoft.UI.Reactor.Core;
    public static class Factories
    {
        public static Element Memo(System.Func<RenderContext, Element> render, params object?[] dependencies) => null!;
        public static Element Memo<TKey>(TKey key, System.Func<Element> factory) => null!;
    }
}

namespace Other
{
    using Microsoft.UI.Reactor.Core;
    public static class NotReactor
    {
        public static Element Memo(System.Func<RenderContext, Element> render, params object?[] dependencies) => null!;
    }
}
";

    private static Task Verify(string body) =>
        new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + body,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        }.RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task Memo_With_Fresh_List_Dep_Flags()
    {
        await Verify(@"
namespace TestApp
{
    using static Microsoft.UI.Reactor.Factories;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        Element M() => Memo(ctx => new TextElement(""x""), {|REACTOR_HOOKS_012:new System.Collections.Generic.List<int>()|});
    }
}");
    }

    [Fact]
    public async Task Memo_With_Fresh_Array_Dep_Flags()
    {
        await Verify(@"
namespace TestApp
{
    using static Microsoft.UI.Reactor.Factories;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        Element M() => Memo(ctx => new TextElement(""x""), {|REACTOR_HOOKS_012:new[] { 1, 2 }|});
    }
}");
    }

    // Positive: IEquatable<T> without an Equals(object) override compares by reference under
    // object.Equals (the deps diff), so a fresh instance defeats the memo and fires.
    [Fact]
    public async Task Memo_With_Fresh_IEquatableOnly_Dep_Flags()
    {
        await Verify(@"
namespace TestApp
{
    using static Microsoft.UI.Reactor.Factories;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        Element M() => Memo(ctx => new TextElement(""x""), {|REACTOR_HOOKS_012:new EquatableDep()|});
    }
}");
    }

    // Positive: a look-alike IEquatable<T> from a non-System namespace is not value equality.
    [Fact]
    public async Task Memo_With_NonSystem_IEquatable_Dep_Flags()
    {
        await Verify(@"
namespace TestApp
{
    using static Microsoft.UI.Reactor.Factories;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        Element M() => Memo(ctx => new TextElement(""x""), {|REACTOR_HOOKS_012:new FakeEquatableDep()|});
    }
}");
    }

    // Named `dependencies:` argument passes the whole params array by name — still analyzed.
    [Fact]
    public async Task Memo_With_Named_Dependencies_Arg_Flags()
    {
        await Verify(@"
namespace TestApp
{
    using static Microsoft.UI.Reactor.Factories;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        Element M() => Memo(ctx => new TextElement(""x""), dependencies: new object[] { {|REACTOR_HOOKS_012:new System.Collections.Generic.List<int>()|} });
    }
}");
    }

    // Negative: a freshly-allocated record compares by value, so the memo still hits its stable path.
    [Fact]
    public async Task Memo_With_Fresh_Record_Dep_DoesNotFlag()
    {
        await Verify(@"
namespace TestApp
{
    using static Microsoft.UI.Reactor.Factories;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        Element M() => Memo(ctx => new TextElement(""x""), new Data(1));
    }
}");
    }

    // Negative: a stable reference (not an allocation) is fine even though List lacks value equality.
    [Fact]
    public async Task Memo_With_Stable_Reference_Dep_DoesNotFlag()
    {
        await Verify(@"
namespace TestApp
{
    using static Microsoft.UI.Reactor.Factories;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        System.Collections.Generic.List<int> _stable = new System.Collections.Generic.List<int>();
        Element M() => Memo(ctx => new TextElement(""x""), _stable);
    }
}");
    }

    // Exclusion: the keyed Memo<TKey>(TKey, Func<Element>) is DESIGNED to take fresh keys — never flag it.
    [Fact]
    public async Task Keyed_Memo_Is_Excluded()
    {
        await Verify(@"
namespace TestApp
{
    using static Microsoft.UI.Reactor.Factories;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        Element M() => Memo(new System.Collections.Generic.List<int>(), () => new TextElement(""x""));
    }
}");
    }

    // Near-miss: an unrelated Memo with the same params shape in a non-Reactor namespace.
    [Fact]
    public async Task NonReactor_Memo_DoesNotFlag()
    {
        await Verify(@"
namespace TestApp
{
    using static Other.NotReactor;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        Element M() => Memo(ctx => new TextElement(""x""), new System.Collections.Generic.List<int>());
    }
}");
    }

    // Negative: an explicit empty deps container `[]` is the idiomatic "render once" form.
    [Fact]
    public async Task Memo_With_Empty_Deps_DoesNotFlag()
    {
        await Verify(@"
namespace TestApp
{
    using static Microsoft.UI.Reactor.Factories;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        Element M() => Memo(ctx => new TextElement(""x""), []);
    }
}");
    }

    // Negative: a constant-folded zero-length array is still an empty deps container.
    [Fact]
    public async Task Memo_With_Const_Zero_Length_Array_DoesNotFlag()
    {
        await Verify(@"
namespace TestApp
{
    using static Microsoft.UI.Reactor.Factories;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        const int Zero = 0;
        Element M() => Memo(ctx => new TextElement(""x""), new object[Zero]);
    }
}");
    }

    // An explicit object[] deps CONTAINER is compared element-wise: only the element lacking value
    // equality is flagged, not the container itself.
    [Fact]
    public async Task Memo_With_Object_Array_Container_Flags_Only_Bad_Element()
    {
        await Verify(@"
namespace TestApp
{
    using static Microsoft.UI.Reactor.Factories;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        Element M() => Memo(ctx => new TextElement(""x""), new object[] { {|REACTOR_HOOKS_012:new System.Collections.Generic.List<int>()|}, 5 });
    }
}");
    }

    // Negative: an object[] container whose elements all have value equality does not flag.
    [Fact]
    public async Task Memo_With_Object_Array_Container_ValueEquality_Elements_DoesNotFlag()
    {
        await Verify(@"
namespace TestApp
{
    using static Microsoft.UI.Reactor.Factories;
    using Microsoft.UI.Reactor.Core;

    class C
    {
        Element M() => Memo(ctx => new TextElement(""x""), new object[] { new Data(1), 5, ""s"" });
    }
}");
    }
}
