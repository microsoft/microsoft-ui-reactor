using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="StaticRegisterLambdaAnalyzer"/> (<c>REACTOR_DESC_001</c>) and its
/// <see cref="StaticRegisterLambdaCodeFix"/>. Stubs a minimal <c>ControlRegistry</c> in the
/// real namespace so the analyzer's semantic confirmation fires without pulling the framework
/// in; a same-named <c>Register</c> on an unrelated type proves the near-miss guard.
/// </summary>
public class StaticRegisterLambdaAnalyzerTests
{
    // Minimal shape: ControlRegistry lives in Microsoft.UI.Reactor.Core.V1Protocol (the
    // semantic gate keys off type name + namespace), each entry point takes a single
    // Func<object> factory. NotTheRegistry mirrors the 'Register' name on a different type so
    // the near-miss (name matches, symbol does not) can be exercised. The `using` for the
    // registry namespace sits at the top so appended user code can name ControlRegistry
    // unqualified (a using must precede all type declarations).
    private const string Stubs = @"
using System;
using Microsoft.UI.Reactor.Core.V1Protocol;

namespace Microsoft.UI.Reactor.Core.V1Protocol
{
    public static class ControlRegistry
    {
        public static void Register<TElement, TControl>(Func<object> handlerFactory) {}
        public static void RegisterForDerivedTypes<TBase, TControl>(Func<object> handlerFactory) {}
        public static void RegisterDecorator<TElement>(Func<object> handlerFactory) {}
        public static void RegisterDecoratorForDerivedTypes<TBase>(Func<object> handlerFactory) {}
    }
}

public class MyElement {}
public class MyControl {}
public class MyHandler
{
    public MyHandler() {}
    public MyHandler(int captured) {}
    public Action? OnReady;
    public void Ping() {}
}

public static class NotTheRegistry
{
    public static void Register<TElement, TControl>(Func<object> handlerFactory) {}
}
";

    // ── Positive ────────────────────────────────────────────────────────

    [Fact]
    public async Task Fires_For_NonStatic_Register_Lambda()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        ControlRegistry.Register<MyElement, MyControl>({|REACTOR_DESC_001:() => new MyHandler()|});
    }
}";

        await new CSharpAnalyzerTest<StaticRegisterLambdaAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_RegisterForDerivedTypes()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        ControlRegistry.RegisterForDerivedTypes<MyElement, MyControl>({|REACTOR_DESC_001:() => new MyHandler()|});
    }
}";

        await new CSharpAnalyzerTest<StaticRegisterLambdaAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_RegisterDecorator()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        ControlRegistry.RegisterDecorator<MyElement>({|REACTOR_DESC_001:() => new MyHandler()|});
    }
}";

        await new CSharpAnalyzerTest<StaticRegisterLambdaAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_RegisterDecoratorForDerivedTypes()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        ControlRegistry.RegisterDecoratorForDerivedTypes<MyElement>({|REACTOR_DESC_001:() => new MyHandler()|});
    }
}";

        await new CSharpAnalyzerTest<StaticRegisterLambdaAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negative ────────────────────────────────────────────────────────

    [Fact]
    public async Task No_Diagnostic_When_Already_Static()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        ControlRegistry.Register<MyElement, MyControl>(static () => new MyHandler());
    }
}";

        await new CSharpAnalyzerTest<StaticRegisterLambdaAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Method_Group_Argument()
    {
        // A method group has no lambda modifiers to make static — nothing to flag.
        var source = Stubs + @"
class C
{
    static object CreateHandler() => new MyHandler();
    void M()
    {
        ControlRegistry.Register<MyElement, MyControl>(CreateHandler);
    }
}";

        await new CSharpAnalyzerTest<StaticRegisterLambdaAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Near-miss (syntactic name match, different symbol) ───────────────

    [Fact]
    public async Task No_Diagnostic_For_Unrelated_Register_Method()
    {
        // Same member name ('Register') and shape, but the symbol is NOT ControlRegistry —
        // the semantic gate must keep this quiet.
        var source = Stubs + @"
class C
{
    void M()
    {
        NotTheRegistry.Register<MyElement, MyControl>(() => new MyHandler());
    }
}";

        await new CSharpAnalyzerTest<StaticRegisterLambdaAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Code fix ────────────────────────────────────────────────────────

    [Fact]
    public async Task CodeFix_Inserts_Static()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        ControlRegistry.Register<MyElement, MyControl>({|REACTOR_DESC_001:() => new MyHandler()|});
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        ControlRegistry.Register<MyElement, MyControl>(static () => new MyHandler());
    }
}";

        await new CSharpCodeFixTest<StaticRegisterLambdaAnalyzer, StaticRegisterLambdaCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_For_Capturing_Lambda()
    {
        // A capturing lambda cannot compile with 'static', so the analyzer still reports the
        // nudge but no code fix is offered: TestCode == FixedCode (diagnostic persists, no
        // rewrite). This is the "emit the diagnostic but NO auto-fix" contract.
        var code = Stubs + @"
class C
{
    void M()
    {
        int captured = 5;
        ControlRegistry.Register<MyElement, MyControl>({|REACTOR_DESC_001:() => new MyHandler(captured)|});
    }
}";

        await new CSharpCodeFixTest<StaticRegisterLambdaAnalyzer, StaticRegisterLambdaCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Not_Offered_For_This_Capturing_Lambda()
    {
        // Capturing the implicit `this` (via an instance field) also blocks `static`. The
        // analyzer nudges but the fix is withheld — this exercises the `this`-capture arm of
        // the code fix's data-flow gate, distinct from the enclosing-local case above.
        var code = Stubs + @"
class C
{
    int _seed = 7;
    void M()
    {
        ControlRegistry.Register<MyElement, MyControl>({|REACTOR_DESC_001:() => new MyHandler(_seed)|});
    }
}";

        await new CSharpCodeFixTest<StaticRegisterLambdaAnalyzer, StaticRegisterLambdaCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Offered_When_Only_A_Nested_Lambda_Captures_An_Own_Local()
    {
        // The outer factory declares its own local `h` and a nested lambda closes over it. The
        // OUTER lambda still captures nothing from an enclosing scope, so it can be `static` —
        // the fix must be offered even though data-flow's CapturedInside lists `h`.
        var before = Stubs + @"
class C
{
    void M()
    {
        ControlRegistry.Register<MyElement, MyControl>({|REACTOR_DESC_001:() => { var h = new MyHandler(); h.OnReady = () => h.Ping(); return h; }|});
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        ControlRegistry.Register<MyElement, MyControl>(static () => { var h = new MyHandler(); h.OnReady = () => h.Ping(); return h; });
    }
}";

        await new CSharpCodeFixTest<StaticRegisterLambdaAnalyzer, StaticRegisterLambdaCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_Block_Bodied_Lambda()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        ControlRegistry.Register<MyElement, MyControl>({|REACTOR_DESC_001:() => { return new MyHandler(); }|});
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        ControlRegistry.Register<MyElement, MyControl>(static () => { return new MyHandler(); });
    }
}";

        await new CSharpCodeFixTest<StaticRegisterLambdaAnalyzer, StaticRegisterLambdaCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Rewrites_Anonymous_Method()
    {
        // `delegate { ... }` is also an AnonymousFunctionExpressionSyntax and can be `static`.
        var before = Stubs + @"
class C
{
    void M()
    {
        ControlRegistry.Register<MyElement, MyControl>({|REACTOR_DESC_001:delegate { return new MyHandler(); }|});
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        ControlRegistry.Register<MyElement, MyControl>(static delegate { return new MyHandler(); });
    }
}";

        await new CSharpCodeFixTest<StaticRegisterLambdaAnalyzer, StaticRegisterLambdaCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Unqualified_Static_Imported_Call()
    {
        // `using static ...ControlRegistry;` makes the call an unqualified GenericNameSyntax;
        // GetInvokedName's SimpleNameSyntax arm must still resolve it.
        var source = @"
using System;
using static Microsoft.UI.Reactor.Core.V1Protocol.ControlRegistry;

namespace Microsoft.UI.Reactor.Core.V1Protocol
{
    public static class ControlRegistry
    {
        public static void Register<TElement, TControl>(Func<object> handlerFactory) {}
    }
}

public class MyElement {}
public class MyControl {}
public class MyHandler { public MyHandler() {} }

class C
{
    void M()
    {
        Register<MyElement, MyControl>({|REACTOR_DESC_001:() => new MyHandler()|});
    }
}";

        await new CSharpAnalyzerTest<StaticRegisterLambdaAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
