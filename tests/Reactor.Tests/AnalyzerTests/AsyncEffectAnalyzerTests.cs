using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <c>REACTOR_HOOKS_003</c> (async-void <c>UseEffect</c> body) and
/// <see cref="AsyncEffectCodeFix"/>. The stub exposes both real <c>UseEffect</c> overloads
/// (<c>Action</c> / <c>Func&lt;Action&gt;</c>) so the analyzer confirms there is no
/// <c>Func&lt;Task&gt;</c> overload for an async lambda to bind to.
/// </summary>
public class AsyncEffectAnalyzerTests
{
    private const string Stubs = @"
namespace Microsoft.UI.Reactor.Core
{
    public class RenderContext { }

    public abstract class Component
    {
        protected internal RenderContext Context { get; } = new RenderContext();
        public abstract string Render();
        protected void UseEffect(System.Action effect, params object[] dependencies) { }
        protected void UseEffect(System.Func<System.Action> effect, params object[] dependencies) { }
        protected System.Threading.Tasks.Task FetchAsync() => System.Threading.Tasks.Task.CompletedTask;
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
    public async Task Async_Block_UseEffect_Flags()
    {
        await Verify(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        UseEffect({|REACTOR_HOOKS_003:async () => { await FetchAsync(); }|});
        return """";
    }
}");
    }

    [Fact]
    public async Task Async_Expression_UseEffect_Flags()
    {
        await Verify(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        UseEffect({|REACTOR_HOOKS_003:async () => await FetchAsync()|});
        return """";
    }
}");
    }

    // Negative: a synchronous effect is the intended shape.
    [Fact]
    public async Task Sync_UseEffect_DoesNotFlag()
    {
        await Verify(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        UseEffect(() => { var x = 1; });
        return """";
    }
}");
    }

    // Near-miss: a synchronous effect that returns a cleanup (Func<Action>) — not async.
    [Fact]
    public async Task Sync_Effect_With_Cleanup_DoesNotFlag()
    {
        await Verify(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        UseEffect(() => { return () => { }; });
        return """";
    }
}");
    }

    // Near-miss: an async lambda passed to an unrelated (non-Reactor) API must not flag.
    [Fact]
    public async Task Async_Lambda_On_NonReactor_Api_DoesNotFlag()
    {
        await Verify(@"
class Other
{
    public void UseEffect(System.Func<System.Threading.Tasks.Task> effect) { }
    public void M()
    {
        UseEffect(async () => await System.Threading.Tasks.Task.Delay(1));
    }
}");
    }

    // Negative: if the call binds to a Task-returning effect overload (a consumer-added
    // UseEffect(Func<Task>)), the async lambda is awaited safely — no async void — so it must not fire.
    [Fact]
    public async Task Async_UseEffect_Bound_To_FuncTask_Overload_DoesNotFlag()
    {
        var test = @"
using System;
using System.Threading.Tasks;

namespace Microsoft.UI.Reactor.Core
{
    public class RenderContext { }

    public abstract class Component
    {
        protected internal RenderContext Context { get; } = new RenderContext();
        public abstract string Render();
        protected void UseEffect(Action effect, params object[] deps) { }
        protected void UseEffect(Func<Task> effect, params object[] deps) { }
        protected Task FetchAsync() => Task.CompletedTask;
    }
}

class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        UseEffect(async () => await FetchAsync());
        return """";
    }
}";

        await new CSharpAnalyzerTest<HookRulesAnalyzer, DefaultVerifier>
        {
            TestCode = test,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Extracts_Async_Body_Into_Cancelable_Task()
    {
        var before = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        UseEffect({|REACTOR_HOOKS_003:async () => { await FetchAsync(); }|});
        return """";
    }
}";

        var after = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        UseEffect(() =>
        {
            var cts = new global::System.Threading.CancellationTokenSource();
            _ = RunAsync(cts.Token);
            return () => { cts.Cancel(); cts.Dispose(); };

            async global::System.Threading.Tasks.Task RunAsync(global::System.Threading.CancellationToken ct)
            {
                await FetchAsync();
            }
        });
        return """";
    }
}";

        await new CSharpCodeFixTest<HookRulesAnalyzer, AsyncEffectCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CodeActionEquivalenceKey = HookRulesAnalyzer.AsyncEffectId,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Handles_Expression_Bodied_Async_Lambda()
    {
        var before = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        UseEffect({|REACTOR_HOOKS_003:async () => await FetchAsync()|});
        return """";
    }
}";

        var after = Stubs + @"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        UseEffect(() =>
        {
            var cts = new global::System.Threading.CancellationTokenSource();
            _ = RunAsync(cts.Token);
            return () => { cts.Cancel(); cts.Dispose(); };

            async global::System.Threading.Tasks.Task RunAsync(global::System.Threading.CancellationToken ct)
            {
                await FetchAsync();
            }
        });
        return """";
    }
}";

        await new CSharpCodeFixTest<HookRulesAnalyzer, AsyncEffectCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            CodeActionEquivalenceKey = HookRulesAnalyzer.AsyncEffectId,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
