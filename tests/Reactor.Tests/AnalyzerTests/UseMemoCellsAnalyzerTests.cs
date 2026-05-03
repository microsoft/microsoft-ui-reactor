using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;
using CodeFixVerifier = Microsoft.CodeAnalysis.CSharp.Testing.CSharpCodeFixVerifier<
    Microsoft.UI.Reactor.Analyzers.UseMemoCellsAnalyzer,
    Microsoft.UI.Reactor.Analyzers.UseMemoCellsCodeFix,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="UseMemoCellsAnalyzer"/> (REACTOR_HOOKS_007).
/// Stubs the relevant Reactor types so the analyzer's symbol-match check
/// (<c>Microsoft.UI.Reactor.Hooks.UseMemoCellsExtensions</c>) fires without
/// pulling in the real framework. Diagnostic locations are matched via
/// <c>{|REACTOR_HOOKS_007:…|}</c> markup so individual tests don't have to
/// compute line/column spans.
/// </summary>
public class UseMemoCellsAnalyzerTests
{
    private const string Stubs = @"
using System;
using System.Collections.Generic;

namespace Microsoft.UI.Reactor.Core
{
    public class RenderContext { }
    public abstract record Element { }
    public abstract class Component
    {
        protected internal RenderContext Context { get; } = new RenderContext();
        public abstract Element Render();
    }
}

namespace Microsoft.UI.Reactor.Hooks
{
    using Microsoft.UI.Reactor.Core;

    public static class UseMemoCellsExtensions
    {
        public static Element[] UseMemoCells<T>(
            this RenderContext ctx,
            IReadOnlyList<T> items,
            Func<T, int, Element> builder,
            params object[] dependencies)
            where T : notnull
            => Array.Empty<Element>();

        public static Element[] UseMemoCellsByKey<T, TKey>(
            this RenderContext ctx,
            IReadOnlyList<T> items,
            Func<T, TKey> keySelector,
            Func<T, int, Element> builder,
            params object[] dependencies)
            where T : notnull
            where TKey : notnull
            => Array.Empty<Element>();

        public static Element[] UseMemoCellsByIndex<T>(
            this RenderContext ctx,
            IReadOnlyList<T> items,
            IReadOnlyList<int> changedIndices,
            Func<T, int, Element> builder,
            params object[] dependencies)
            where T : notnull
            => Array.Empty<Element>();
    }
}

namespace TestApp
{
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Reactor.Hooks;

    public sealed record DivElement : Element { }

    public static class Theme
    {
        public static readonly object Brush = new();
    }
}
";

    private static async Task RunAsync(string code, params string[] expectedCaptureNames)
    {
        var t = new CSharpAnalyzerTest<UseMemoCellsAnalyzer, DefaultVerifier> { TestCode = Stubs + code };
        foreach (var name in expectedCaptureNames)
        {
            t.ExpectedDiagnostics.Add(
                CSharpAnalyzerVerifier<UseMemoCellsAnalyzer, DefaultVerifier>
                    .Diagnostic(UseMemoCellsAnalyzer.DiagnosticId)
                    .WithLocation(0)
                    .WithArguments(name));
        }
        await t.RunAsync();
    }

    // ── Happy path ────────────────────────────────────────────────────

    [Fact]
    public async Task Capture_Present_In_Deps_No_Diagnostic()
    {
        await RunAsync(@"
namespace TestApp {
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Reactor.Hooks;
    public class C {
        public Element[] Build(RenderContext ctx, IReadOnlyList<int> items, object theme)
            => ctx.UseMemoCells<int>(items, (item, i) => Helper(item, theme), theme);
        static Element Helper(int x, object t) => new DivElement();
    }
}");
    }

    [Fact]
    public async Task Pure_Builder_Zero_Deps_No_Diagnostic()
    {
        await RunAsync(@"
namespace TestApp {
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Reactor.Hooks;
    public class C {
        public Element[] Build(RenderContext ctx, IReadOnlyList<int> items)
            => ctx.UseMemoCells<int>(items, (item, i) => new DivElement());
    }
}");
    }

    [Fact]
    public async Task Static_Readonly_Capture_No_Diagnostic()
    {
        await RunAsync(@"
namespace TestApp {
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Reactor.Hooks;
    public class C {
        public Element[] Build(RenderContext ctx, IReadOnlyList<int> items)
            => ctx.UseMemoCells<int>(items, (item, i) => Use(item, Theme.Brush));
        static Element Use(int x, object t) => new DivElement();
    }
}");
    }

    [Fact]
    public async Task Const_Capture_No_Diagnostic()
    {
        await RunAsync(@"
namespace TestApp {
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Reactor.Hooks;
    public class C {
        const int Multiplier = 3;
        public Element[] Build(RenderContext ctx, IReadOnlyList<int> items)
            => ctx.UseMemoCells<int>(items, (item, i) => Use(item * Multiplier));
        static Element Use(int x) => new DivElement();
    }
}");
    }

    // ── Diagnostic cases ─────────────────────────────────────────────

    [Fact]
    public async Task Capture_Missing_From_Deps_Emits_Warning()
    {
        await RunAsync(@"
namespace TestApp {
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Reactor.Hooks;
    public class C {
        public Element[] Build(RenderContext ctx, IReadOnlyList<int> items, object theme)
            => ctx.UseMemoCells<int>(items, {|#0:(item, i) => Use(item, theme)|});
        static Element Use(int x, object t) => new DivElement();
    }
}", "theme");
    }

    [Fact]
    public async Task Zero_Deps_With_Capturing_Builder_Emits_Warning()
    {
        await RunAsync(@"
namespace TestApp {
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Reactor.Hooks;
    public class C {
        public Element[] Build(RenderContext ctx, IReadOnlyList<int> items, object theme)
            => ctx.UseMemoCells<int>(items, {|#0:(item, i) => Use(item, theme)|});
        static Element Use(int x, object t) => new DivElement();
    }
}", "theme");
    }

    [Fact]
    public async Task Capture_Through_This_Field_Emits_Warning_With_Field_Name()
    {
        await RunAsync(@"
namespace TestApp {
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Reactor.Hooks;
    public class C : Component {
        private object theme = new object();
        public override Element Render() => new DivElement();
        public Element[] Build(RenderContext ctx, IReadOnlyList<int> items)
            => ctx.UseMemoCells<int>(items, {|#0:(item, i) => Use(item, this.theme)|});
        static Element Use(int x, object t) => new DivElement();
    }
}", "theme");
    }

    [Fact]
    public async Task Indirect_Capture_Through_Helper_Method_No_Diagnostic_Documented_Blind_Spot()
    {
        // The helper method's own captures aren't visible from the lambda's
        // syntactic scope. This is the documented blind spot. RenderRow is
        // a method (not a field/property), so the analyzer doesn't flag it.
        await RunAsync(@"
namespace TestApp {
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Reactor.Hooks;
    public class C {
        public Element[] Build(RenderContext ctx, IReadOnlyList<int> items)
            => ctx.UseMemoCells<int>(items, (item, i) => RenderRow(item));
        Element RenderRow(int x) => new DivElement();
    }
}");
    }

    // ── Variant coverage ──────────────────────────────────────────────

    [Fact]
    public async Task ByKey_Capture_Missing_Emits_Warning()
    {
        await RunAsync(@"
namespace TestApp {
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Reactor.Hooks;
    public class C {
        public Element[] Build(RenderContext ctx, IReadOnlyList<int> items, object theme)
            => ctx.UseMemoCellsByKey<int, int>(items, x => x, {|#0:(item, i) => Use(item, theme)|});
        static Element Use(int x, object t) => new DivElement();
    }
}", "theme");
    }

    [Fact]
    public async Task ByIndex_Capture_Missing_Emits_Warning()
    {
        await RunAsync(@"
namespace TestApp {
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Reactor.Hooks;
    public class C {
        public Element[] Build(RenderContext ctx, IReadOnlyList<int> items, IReadOnlyList<int> changed, object theme)
            => ctx.UseMemoCellsByIndex<int>(items, changed, {|#0:(item, i) => Use(item, theme)|});
        static Element Use(int x, object t) => new DivElement();
    }
}", "theme");
    }

    // ── Code fix ──────────────────────────────────────────────────────

    [Fact]
    public async Task CodeFix_Adds_Missing_Capture_To_Trailing_Params_Slot()
    {
        var before = Stubs + @"
namespace TestApp {
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Reactor.Hooks;
    public class C {
        public Element[] Build(RenderContext ctx, IReadOnlyList<int> items, object theme)
            => ctx.UseMemoCells<int>(items, {|#0:(item, i) => Use(item, theme)|});
        static Element Use(int x, object t) => new DivElement();
    }
}";

        var after = Stubs + @"
namespace TestApp {
    using Microsoft.UI.Reactor.Core;
    using Microsoft.UI.Reactor.Hooks;
    public class C {
        public Element[] Build(RenderContext ctx, IReadOnlyList<int> items, object theme)
            => ctx.UseMemoCells<int>(items, (item, i) => Use(item, theme), theme);
        static Element Use(int x, object t) => new DivElement();
    }
}";

        var t = new CSharpCodeFixTest<UseMemoCellsAnalyzer, UseMemoCellsCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        };
        t.ExpectedDiagnostics.Add(
            CodeFixVerifier.Diagnostic(UseMemoCellsAnalyzer.DiagnosticId)
                .WithLocation(0)
                .WithArguments("theme"));
        await t.RunAsync();
    }
}
