using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="UsePersistedScopeAnalyzer"/> (<c>REACTOR_PERSIST_001</c>) and its
/// <see cref="UsePersistedScopeCodeFix"/>. Stubs a minimal Reactor-shaped
/// <c>RenderContext</c> with both <c>UsePersisted</c> overloads plus a
/// <c>PersistedScope</c> enum, so the arity + symbol gate fires without pulling the
/// framework in.
/// </summary>
public class UsePersistedScopeAnalyzerTests
{
    // Mirrors the real shape (RenderContext.cs:824/842): a two-argument overload that
    // implicitly means Application scope, and a three-argument overload that takes the
    // scope explicitly. The extra (string, PersistedScope) overload exists ONLY so the
    // "scope named by hand" near-miss below is a legal call to compile against.
    // The `using` directives sit at the top of the compilation unit (before the
    // namespace) so the appended test bodies resolve RenderContext / PersistedScope.
    private const string Stubs = @"
using System;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Core
{
    public enum PersistedScope { Window, Application }

    public class RenderContext
    {
        // Target overload — silently defaults to Application scope.
        public void UsePersisted<T>(string key, T initialValue) { }

        // Explicit-scope overload.
        public void UsePersisted<T>(string key, T initialValue, PersistedScope scope) { }

        // Only here so `UsePersisted(""k"", scope: ...)` is a legal 2-arg near-miss.
        public void UsePersisted(string key, PersistedScope scope) { }
    }

    // Component re-exposes the hook as a protected wrapper (Component.cs:260) that
    // also defaults to Application scope; an unqualified `UsePersisted(...)` inside a
    // component's Render binds here, not to RenderContext.
    public class Component
    {
        protected void UsePersisted<T>(string key, T initialValue) { }
        protected void UsePersisted<T>(string key, T initialValue, PersistedScope scope) { }
    }

    // Same method name + 2-arg shape, but neither RenderContext nor Component — must never fire.
    public class NotRenderContext
    {
        public void UsePersisted<T>(string key, T initialValue) { }
    }
}
";

    // ── Positive ────────────────────────────────────────────────────────

    [Fact]
    public async Task Fires_For_TwoArg_Call()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var ctx = new RenderContext();
        {|REACTOR_PERSIST_001:ctx.UsePersisted(""filter"", """")|};
    }
}";

        await new CSharpAnalyzerTest<UsePersistedScopeAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_TwoArg_Call_With_Explicit_TypeArgument()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var ctx = new RenderContext();
        {|REACTOR_PERSIST_001:ctx.UsePersisted<string>(""filter"", """")|};
    }
}";

        await new CSharpAnalyzerTest<UsePersistedScopeAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_When_Key_And_InitialValue_Are_Named()
    {
        // Named arguments are fine — only an explicit `scope:` suppresses the rule.
        var source = Stubs + @"
class C
{
    void M()
    {
        var ctx = new RenderContext();
        {|REACTOR_PERSIST_001:ctx.UsePersisted(key: ""filter"", initialValue: """")|};
    }
}";

        await new CSharpAnalyzerTest<UsePersistedScopeAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_When_Named_Arguments_Are_Reordered()
    {
        // Reordered named args are legal; the rule must still fire (and the message
        // resolves the key: argument rather than blindly using the first argument).
        var source = Stubs + @"
class C
{
    void M()
    {
        var ctx = new RenderContext();
        {|REACTOR_PERSIST_001:ctx.UsePersisted(initialValue: ""v"", key: ""filter"")|};
    }
}";

        await new CSharpAnalyzerTest<UsePersistedScopeAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negative ────────────────────────────────────────────────────────

    [Fact]
    public async Task No_Diagnostic_For_ThreeArg_Explicit_Scope()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var ctx = new RenderContext();
        ctx.UsePersisted(""filter"", """", PersistedScope.Window);
    }
}";

        await new CSharpAnalyzerTest<UsePersistedScopeAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Near-miss: syntactic fast path almost trips ─────────────────────

    [Fact]
    public async Task No_Diagnostic_For_TwoArg_On_NonRenderContext()
    {
        // A same-named 2-arg method on a different type — the semantic check rejects it.
        var source = Stubs + @"
class C
{
    void M()
    {
        var other = new NotRenderContext();
        other.UsePersisted(""filter"", """");
    }
}";

        await new CSharpAnalyzerTest<UsePersistedScopeAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_TwoArg_With_Named_Scope()
    {
        // Two arguments, but the author already stated the scope by name — leave it alone.
        var source = Stubs + @"
class C
{
    void M()
    {
        var ctx = new RenderContext();
        ctx.UsePersisted(""filter"", scope: PersistedScope.Window);
    }
}";

        await new CSharpAnalyzerTest<UsePersistedScopeAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Code-fix round trips ────────────────────────────────────────────

    [Fact]
    public async Task CodeFix_Adds_Window_Scope()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        var ctx = new RenderContext();
        {|REACTOR_PERSIST_001:ctx.UsePersisted(""filter"", """")|};
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var ctx = new RenderContext();
        ctx.UsePersisted(""filter"", """", PersistedScope.Window);
    }
}";

        await new CSharpCodeFixTest<UsePersistedScopeAnalyzer, UsePersistedScopeCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = UsePersistedScopeAnalyzer.DiagnosticId + ":Window",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Adds_Application_Scope()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        var ctx = new RenderContext();
        {|REACTOR_PERSIST_001:ctx.UsePersisted(""filter"", """")|};
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var ctx = new RenderContext();
        ctx.UsePersisted(""filter"", """", PersistedScope.Application);
    }
}";

        await new CSharpCodeFixTest<UsePersistedScopeAnalyzer, UsePersistedScopeCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = UsePersistedScopeAnalyzer.DiagnosticId + ":Application",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Component wrapper (the idiomatic unqualified call shape) ─────────

    [Fact]
    public async Task Fires_For_Unqualified_Component_Call()
    {
        // Real components call the protected Component.UsePersisted wrapper
        // unqualified inside Render (e.g. samples/apps/regedit/App.cs).
        var source = Stubs + @"
class MyComponent : Component
{
    void Build()
    {
        {|REACTOR_PERSIST_001:UsePersisted(""filter"", """")|};
    }
}";

        await new CSharpAnalyzerTest<UsePersistedScopeAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_When_Value_Type_Is_PersistedScope()
    {
        // T inferred as PersistedScope is still the 2-arg default-scope overload —
        // it must fire (guards the OriginalDefinition type-parameter check).
        var source = Stubs + @"
class MyComponent : Component
{
    void Build()
    {
        {|REACTOR_PERSIST_001:UsePersisted(""preferred-scope"", PersistedScope.Window)|};
    }
}";

        await new CSharpAnalyzerTest<UsePersistedScopeAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Adds_Scope_For_Unqualified_Component_Call()
    {
        var before = Stubs + @"
class MyComponent : Component
{
    void Build()
    {
        {|REACTOR_PERSIST_001:UsePersisted(""filter"", """")|};
    }
}";

        var after = Stubs + @"
class MyComponent : Component
{
    void Build()
    {
        UsePersisted(""filter"", """", PersistedScope.Window);
    }
}";

        await new CSharpCodeFixTest<UsePersistedScopeAnalyzer, UsePersistedScopeCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = UsePersistedScopeAnalyzer.DiagnosticId + ":Window",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Passes_Scope_By_Name_When_Arguments_Are_Named()
    {
        // Appending a positional argument after named arguments could be illegal
        // C# (positional-after-named), so the fix names the scope argument when the
        // call already uses named arguments.
        var before = Stubs + @"
class C
{
    void M()
    {
        var ctx = new RenderContext();
        {|REACTOR_PERSIST_001:ctx.UsePersisted(key: ""filter"", initialValue: """")|};
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var ctx = new RenderContext();
        ctx.UsePersisted(key: ""filter"", initialValue: """", scope: PersistedScope.Window);
    }
}";

        await new CSharpCodeFixTest<UsePersistedScopeAnalyzer, UsePersistedScopeCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = UsePersistedScopeAnalyzer.DiagnosticId + ":Window",
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_ThreeArg_Component_Call()
    {
        // The 3-arg Component wrapper already states the scope — leave it alone.
        var source = Stubs + @"
class MyComponent : Component
{
    void Build()
    {
        UsePersisted(""filter"", """", PersistedScope.Window);
    }
}";

        await new CSharpAnalyzerTest<UsePersistedScopeAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Qualifies_Scope_When_Namespace_Not_Imported()
    {
        // No `using Microsoft.UI.Reactor.Core;` and a fully-qualified receiver: the fix
        // must still emit a COMPILING PersistedScope reference. ToMinimalDisplayString
        // qualifies it here (a bare `PersistedScope` would not resolve). The harness fails
        // the test if the fixed code has any compiler error, so this proves robustness.
        const string before = @"
using System;

namespace Microsoft.UI.Reactor.Core
{
    public enum PersistedScope { Window, Application }
    public class RenderContext
    {
        public void UsePersisted<T>(string key, T initialValue) { }
        public void UsePersisted<T>(string key, T initialValue, PersistedScope scope) { }
    }
}

class C
{
    void M()
    {
        var ctx = new Microsoft.UI.Reactor.Core.RenderContext();
        {|REACTOR_PERSIST_001:ctx.UsePersisted(""filter"", """")|};
    }
}";

        const string after = @"
using System;

namespace Microsoft.UI.Reactor.Core
{
    public enum PersistedScope { Window, Application }
    public class RenderContext
    {
        public void UsePersisted<T>(string key, T initialValue) { }
        public void UsePersisted<T>(string key, T initialValue, PersistedScope scope) { }
    }
}

class C
{
    void M()
    {
        var ctx = new Microsoft.UI.Reactor.Core.RenderContext();
        ctx.UsePersisted(""filter"", """", Microsoft.UI.Reactor.Core.PersistedScope.Window);
    }
}";

        await new CSharpCodeFixTest<UsePersistedScopeAnalyzer, UsePersistedScopeCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            CodeActionEquivalenceKey = UsePersistedScopeAnalyzer.DiagnosticId + ":Window",
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
