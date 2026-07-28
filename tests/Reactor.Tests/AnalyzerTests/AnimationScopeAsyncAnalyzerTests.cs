using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="AnimationScopeAsyncAnalyzer"/> (<c>REACTOR_ANIM_003</c>). Stubs a minimal
/// <c>Microsoft.UI.Reactor.Animation.AnimationScope</c> with the two real scope entry points —
/// <c>WithAnimation(Curve, Action)</c> and <c>WithAnimationAsync(Curve, Action)</c>, both taking an
/// <c>Action</c> — so the analyzer's async-void detection fires without pulling the framework in.
/// The rule ships no code fix (the async variant also takes an <c>Action</c>), so these are
/// analyzer-only tests.
/// </summary>
public class AnimationScopeAsyncAnalyzerTests
{
    // The real AnimationScope shape: [ThreadStatic]-scoped, and BOTH entry points take an Action —
    // there is no Func<Task> overload, which is exactly why an async lambda becomes async void.
    private const string ScopeTypes = @"
namespace Microsoft.UI.Reactor.Animation
{
    public sealed class Curve
    {
        public static Curve Ease(int ms) => new Curve();
        public static Curve Spring() => new Curve();
    }

    public static class AnimationScope
    {
        public static void WithAnimation(Curve curve, System.Action action) { action(); }
        public static System.Threading.Tasks.Task WithAnimationAsync(Curve curve, System.Action action)
        {
            action();
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}
";

    private const string Usings =
        "using System;\nusing System.Threading.Tasks;\nusing Microsoft.UI.Reactor.Animation;\n";

    private static Task VerifyAsync(string source) =>
        new CSharpAnalyzerTest<AnimationScopeAsyncAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);

    // ── Positive ────────────────────────────────────────────────────────

    [Fact]
    public async Task Fires_For_Async_Lambda_With_PostAwait_Mutation()
    {
        var source = Usings + ScopeTypes + @"
class C
{
    void SetStage(string s) {}
    Task Save() => Task.CompletedTask;

    void M()
    {
        AnimationScope.WithAnimation(Curve.Ease(300), {|REACTOR_ANIM_003:async|} () =>
        {
            SetStage(""loading"");
            await Save();
            SetStage(""done"");
        });
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task Fires_For_WithAnimationAsync()
    {
        // WithAnimationAsync has the identical (Curve, Action) signature, so an async lambda passed
        // to it has the identical async-void footgun. Naively "fixing" WithAnimation -> Async must
        // NOT silence the rule.
        var source = Usings + ScopeTypes + @"
class C
{
    void SetStage(string s) {}
    Task Save() => Task.CompletedTask;

    void M()
    {
        AnimationScope.WithAnimationAsync(Curve.Ease(300), {|REACTOR_ANIM_003:async|} () =>
        {
            SetStage(""loading"");
            await Save();
            SetStage(""done"");
        });
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task Fires_For_Async_Delegate()
    {
        var source = Usings + ScopeTypes + @"
class C
{
    void SetStage(string s) {}
    Task Save() => Task.CompletedTask;

    void M()
    {
        AnimationScope.WithAnimation(Curve.Spring(), {|REACTOR_ANIM_003:async|} delegate
        {
            SetStage(""loading"");
            await Save();
            SetStage(""done"");
        });
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task Fires_For_Unqualified_Call_Via_Using_Static()
    {
        var source =
            "using System;\nusing System.Threading.Tasks;\n" +
            "using static Microsoft.UI.Reactor.Animation.AnimationScope;\n" +
            "using Microsoft.UI.Reactor.Animation;\n" + ScopeTypes + @"
class C
{
    void SetStage(string s) {}
    Task Save() => Task.CompletedTask;

    void M()
    {
        WithAnimation(Curve.Ease(300), {|REACTOR_ANIM_003:async|} () =>
        {
            SetStage(""loading"");
            await Save();
            SetStage(""done"");
        });
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task Fires_When_Await_Nested_In_Control_Flow()
    {
        // The await sits inside an `if` (control flow, not a closure), so it is still the lambda's
        // own await and the trailing SetStage runs post-await. Confirms we descend into control flow.
        var source = Usings + ScopeTypes + @"
class C
{
    void SetStage(string s) {}
    Task Save() => Task.CompletedTask;

    void M(bool cond)
    {
        AnimationScope.WithAnimation(Curve.Ease(300), {|REACTOR_ANIM_003:async|} () =>
        {
            SetStage(""loading"");
            if (cond)
            {
                await Save();
            }
            SetStage(""done"");
        });
    }
}";
        await VerifyAsync(source);
    }

    // ── Negative ────────────────────────────────────────────────────────

    [Fact]
    public async Task No_Diagnostic_For_Sync_Lambda()
    {
        var source = Usings + ScopeTypes + @"
class C
{
    void SetStage(string s) {}

    void M()
    {
        AnimationScope.WithAnimation(Curve.Ease(300), () =>
        {
            SetStage(""loading"");
            SetStage(""done"");
        });
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task No_Diagnostic_For_Async_Lambda_Without_Await()
    {
        // async but no await (CS1998) — the scope is never lost, so this is not the footgun.
        var source = Usings + ScopeTypes + @"
class C
{
    void SetStage(string s) {}

    void M()
    {
#pragma warning disable CS1998
        AnimationScope.WithAnimation(Curve.Ease(300), async () =>
        {
            SetStage(""loading"");
            SetStage(""done"");
        });
#pragma warning restore CS1998
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task No_Diagnostic_When_Await_Is_Terminal()
    {
        // The mutation runs BEFORE the await (with the scope live); nothing runs after the await,
        // so no animated mutation is lost.
        var source = Usings + ScopeTypes + @"
class C
{
    void SetStage(string s) {}
    Task Save() => Task.CompletedTask;

    void M()
    {
        AnimationScope.WithAnimation(Curve.Ease(300), async () =>
        {
            SetStage(""loading"");
            await Save();
        });
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task No_Diagnostic_When_Mutations_Only_Before_Await()
    {
        var source = Usings + ScopeTypes + @"
class C
{
    void SetStage(string s) {}
    Task Save() => Task.CompletedTask;

    void M()
    {
        AnimationScope.WithAnimation(Curve.Ease(300), async () =>
        {
            SetStage(""a"");
            SetStage(""b"");
            await Save();
        });
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task No_Diagnostic_For_Await_In_Nested_Closure()
    {
        // The only await belongs to a nested closure (`inner`), not the outer WithAnimation lambda,
        // so the outer lambda has no own-level await and the trailing SetStage runs with the scope
        // still live. Confirms we bail on nested closures.
        var source = Usings + ScopeTypes + @"
class C
{
    void SetStage(string s) {}
    Task Save() => Task.CompletedTask;

    void M()
    {
#pragma warning disable CS1998
        AnimationScope.WithAnimation(Curve.Ease(300), async () =>
        {
            Func<Task> inner = async () => { await Save(); };
            SetStage(""done"");
        });
#pragma warning restore CS1998
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task No_Diagnostic_For_FuncTask_Overload()
    {
        // Future-proofing: if a real Func<Task> overload is ever added, the async lambda binds to it
        // (awaits correctly, keeps its scope) and must NOT warn. A dedicated stub adds that overload;
        // C# overload resolution prefers the Task-returning delegate for an async lambda.
        var source =
            "using System;\nusing System.Threading.Tasks;\nusing Microsoft.UI.Reactor.Animation;\n" + @"
namespace Microsoft.UI.Reactor.Animation
{
    public sealed class Curve { public static Curve Ease(int ms) => new Curve(); }
    public static class AnimationScope
    {
        public static void WithAnimation(Curve curve, Action action) { action(); }
        public static void WithAnimation(Curve curve, Func<Task> action) { action(); }
    }
}

class C
{
    void SetStage(string s) {}
    Task Save() => Task.CompletedTask;

    void M()
    {
        AnimationScope.WithAnimation(Curve.Ease(300), async () =>
        {
            SetStage(""loading"");
            await Save();
            SetStage(""done"");
        });
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task No_Diagnostic_For_NonReactor_AnimationScope()
    {
        // A same-named AnimationScope.WithAnimation in a different namespace is an unrelated API
        // (not [ThreadStatic]-scoped). The namespace anchor keeps the rule from firing on it.
        var source = @"
using System;
using System.Threading.Tasks;
using Other;

namespace Other
{
    public sealed class Curve { public static Curve Ease(int ms) => new Curve(); }
    public static class AnimationScope
    {
        public static void WithAnimation(Curve curve, Action action) { action(); }
    }
}

class C
{
    void SetStage(string s) {}
    Task Save() => Task.CompletedTask;

    void M()
    {
        AnimationScope.WithAnimation(Curve.Ease(300), async () =>
        {
            SetStage(""loading"");
            await Save();
            SetStage(""done"");
        });
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task Fires_For_Mutation_Between_Two_Awaits()
    {
        // The mutation runs after the FIRST await (scope already restored), even though a second
        // await follows it. Confirms detection keys off "an await has happened", not the last await.
        var source = Usings + ScopeTypes + @"
class C
{
    void SetStage(string s) {}
    Task Save() => Task.CompletedTask;

    void M()
    {
        AnimationScope.WithAnimation(Curve.Ease(300), {|REACTOR_ANIM_003:async|} () =>
        {
            await Save();
            SetStage(""mid"");
            await Save();
        });
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task No_Diagnostic_When_Await_And_Mutation_Are_In_Exclusive_Branches()
    {
        // The await and the mutation are in mutually-exclusive `if`/`else` arms, so the mutation
        // never runs after the await on any path — must NOT fire (the key false-positive guard).
        var source = Usings + ScopeTypes + @"
class C
{
    void SetStage(string s) {}
    Task Save() => Task.CompletedTask;

    void M(bool cond)
    {
        AnimationScope.WithAnimation(Curve.Ease(300), async () =>
        {
            if (cond)
            {
                await Save();
            }
            else
            {
                SetStage(""x"");
            }
        });
    }
}";
        await VerifyAsync(source);
    }

    [Fact]
    public async Task No_Diagnostic_For_PostAwait_Assignment()
    {
        // A bare assignment (e.g. a local counter) after the await is not an animated mutation —
        // only state-setter calls animate, so an assignment must not trip the rule.
        var source = Usings + ScopeTypes + @"
class C
{
    Task Save() => Task.CompletedTask;

    void M()
    {
        int total = 0;
        AnimationScope.WithAnimation(Curve.Ease(300), async () =>
        {
            await Save();
            total += 1;
        });
    }
}";
        await VerifyAsync(source);
    }
}
