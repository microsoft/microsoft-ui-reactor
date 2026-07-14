using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <c>REACTOR_CTX_001</c> (context value re-allocated each render) and
/// <see cref="ContextProvideCodeFix"/>. Stubs <c>ContextExtensions.Provide</c>, a plain
/// (reference-equality) config, a record config, and a class overriding <c>Equals</c> so the
/// mandatory value-equality gate is exercised in both directions.
/// </summary>
public class ContextProvideAnalyzerTests
{
    private const string Stubs = @"
namespace Microsoft.UI.Reactor.Core
{
    public class RenderContext { }
    public abstract record Element { }
    public sealed record TextElement(string Text) : Element;

    public abstract class ContextBase { }
    public sealed class Context<T> : ContextBase { }

    public static class ContextExtensions
    {
        public static T Provide<T, TValue>(this T element, Context<TValue> context, TValue value)
            where T : Element => element;
    }

    // Reference-equality (plain class) — fires.
    public class ThemeConfig { public bool IsDark; }
    // Value-equality via record — does not fire.
    public record ThemeRecord(bool IsDark);
    // Value-equality via Equals override — does not fire.
    public class ThemeWithEquals
    {
        public override bool Equals(object o) => true;
        public override int GetHashCode() => 0;
    }
    // IEquatable<T> WITHOUT an Equals(object) override — context diff uses object.Equals, which
    // falls back to reference equality here, so this DOES fire.
    public class ThemeEquatable : System.IEquatable<ThemeEquatable>
    {
        public bool Equals(ThemeEquatable other) => true;
    }

    public abstract class Component
    {
        protected internal RenderContext Context { get; } = new RenderContext();
        public abstract Element Render();
        protected T UseMemo<T>(System.Func<T> factory, params object[] dependencies) => factory();
    }
}
";

    private static Task VerifyAnalyzer(string body) =>
        new CSharpAnalyzerTest<ContextProvideAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + body,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        }.RunAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task Provide_Fresh_ReferenceEquality_Value_Flags()
    {
        await VerifyAnalyzer(@"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    class C : Component
    {
        private Context<ThemeConfig> Ctx = new();
        public override Element Render()
            => new TextElement(""x"").Provide(Ctx, {|REACTOR_CTX_001:new ThemeConfig()|});
    }
}");
    }

    // Negative: a record compares by value (context diffs use Equals), so it does not thrash.
    [Fact]
    public async Task Provide_Fresh_Record_Value_DoesNotFlag()
    {
        await VerifyAnalyzer(@"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    class C : Component
    {
        private Context<ThemeRecord> Ctx = new();
        public override Element Render()
            => new TextElement(""x"").Provide(Ctx, new ThemeRecord(true));
    }
}");
    }

    // Negative: a class overriding Equals(object) has value semantics.
    [Fact]
    public async Task Provide_Fresh_EqualsOverride_Value_DoesNotFlag()
    {
        await VerifyAnalyzer(@"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    class C : Component
    {
        private Context<ThemeWithEquals> Ctx = new();
        public override Element Render()
            => new TextElement(""x"").Provide(Ctx, new ThemeWithEquals());
    }
}");
    }

    // Negative: a stable reference (not a fresh allocation) is fine.
    [Fact]
    public async Task Provide_Stable_Reference_DoesNotFlag()
    {
        await VerifyAnalyzer(@"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    class C : Component
    {
        private Context<ThemeConfig> Ctx = new();
        private ThemeConfig _config = new();
        public override Element Render()
            => new TextElement(""x"").Provide(Ctx, _config);
    }
}");
    }

    // Positive: a class implementing IEquatable<T> but NOT overriding Equals(object) still uses
    // reference equality under object.Equals (the context diff), so it fires.
    [Fact]
    public async Task Provide_Fresh_IEquatableOnly_Value_Flags()
    {
        await VerifyAnalyzer(@"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    class C : Component
    {
        private Context<ThemeEquatable> Ctx = new();
        public override Element Render()
            => new TextElement(""x"").Provide(Ctx, {|REACTOR_CTX_001:new ThemeEquatable()|});
    }
}");
    }

    // Near-miss: an unrelated Provide(...) that is not the Reactor ContextExtensions method.
    [Fact]
    public async Task Unrelated_Provide_DoesNotFlag()
    {
        await VerifyAnalyzer(@"
namespace TestApp
{
    public class Widget
    {
        public Widget Provide(object context, object value) => this;
    }

    class C
    {
        void M()
        {
            new Widget().Provide(new object(), new System.Collections.Generic.List<int>());
        }
    }
}");
    }

    [Fact]
    public async Task CodeFix_Memoizes_The_Context_Value()
    {
        var before = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    class C : Component
    {
        private Context<ThemeConfig> Ctx = new();
        public override Element Render()
            => new TextElement(""x"").Provide(Ctx, {|REACTOR_CTX_001:new ThemeConfig()|});
    }
}";

        var after = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    class C : Component
    {
        private Context<ThemeConfig> Ctx = new();
        public override Element Render()
            => new TextElement(""x"").Provide(Ctx, UseMemo(() => new ThemeConfig(), []));
    }
}";

        await new CSharpCodeFixTest<ContextProvideAnalyzer, ContextProvideCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CodeActionEquivalenceKey = ContextProvideAnalyzer.Id,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // The fix is withheld when the value captures render-varying state (memoizing with `[]` would
    // freeze it); the Info diagnostic still fires but no code action is offered.
    [Fact]
    public async Task CodeFix_Withheld_For_Captured_Value()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    class C : Component
    {
        private bool _isDark;
        private Context<ThemeConfig> Ctx = new();
        public override Element Render()
            => new TextElement(""x"").Provide(Ctx, {|REACTOR_CTX_001:new ThemeConfig { IsDark = _isDark }|});
    }
}";

        await new CSharpCodeFixTest<ContextProvideAnalyzer, ContextProvideCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // The fix is also withheld when the value reads a render-varying member (here DateTime.Now)
    // that data-flow analysis does not surface — memoizing with `[]` would freeze it.
    [Fact]
    public async Task CodeFix_Withheld_For_Property_Read()
    {
        var source = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    class C : Component
    {
        private Context<ThemeConfig> Ctx = new();
        public override Element Render()
            => new TextElement(""x"").Provide(Ctx, {|REACTOR_CTX_001:new ThemeConfig { IsDark = System.DateTime.Now.Hour > 0 }|});
    }
}";

        await new CSharpCodeFixTest<ContextProvideAnalyzer, ContextProvideCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // A capture-free object initializer with only literals IS still offered the fix (the initializer
    // member target `IsDark` is a write, not a render-varying read).
    [Fact]
    public async Task CodeFix_Offered_For_Literal_Object_Initializer()
    {
        var before = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    class C : Component
    {
        private Context<ThemeConfig> Ctx = new();
        public override Element Render()
            => new TextElement(""x"").Provide(Ctx, {|REACTOR_CTX_001:new ThemeConfig { IsDark = true }|});
    }
}";

        var after = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    class C : Component
    {
        private Context<ThemeConfig> Ctx = new();
        public override Element Render()
            => new TextElement(""x"").Provide(Ctx, UseMemo(() => new ThemeConfig { IsDark = true }, []));
    }
}";

        await new CSharpCodeFixTest<ContextProvideAnalyzer, ContextProvideCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CodeActionEquivalenceKey = ContextProvideAnalyzer.Id,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
    [Fact]
    public async Task CodeFix_TargetTyped_New_Emits_Explicit_Type_Argument()
    {
        var before = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    class C : Component
    {
        private Context<ThemeConfig> Ctx = new();
        public override Element Render()
            => new TextElement(""x"").Provide(Ctx, {|REACTOR_CTX_001:new()|});
    }
}";

        var after = Stubs + @"
namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    class C : Component
    {
        private Context<ThemeConfig> Ctx = new();
        public override Element Render()
            => new TextElement(""x"").Provide(Ctx, UseMemo<global::Microsoft.UI.Reactor.Core.ThemeConfig>(() => new(), []));
    }
}";

        await new CSharpCodeFixTest<ContextProvideAnalyzer, ContextProvideCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CodeActionEquivalenceKey = ContextProvideAnalyzer.Id,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
