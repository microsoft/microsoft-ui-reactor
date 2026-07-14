using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="BlockingTaskAnalyzer"/> (<c>REACTOR_THREAD_002</c>). Stubs a
/// minimal Reactor-shaped <c>Component</c> / <c>RenderContext</c> (with a real
/// <c>UseEffect</c> overload set) so the analyzer's Render/effect context walk and its
/// semantic <c>Task</c>-receiver confirmation both fire without pulling the framework in.
/// </summary>
public class BlockingTaskAnalyzerTests
{
    // Shapes the two anchoring types the analyzer keys off — Component (with a Render()
    // override target + protected UseEffect wrappers) and RenderContext (public UseEffect)
    // — under the real Microsoft.UI.Reactor.Core namespace, plus a couple of async helpers.
    private const string Stubs = @"
using System;
using System.Threading.Tasks;

namespace Microsoft.UI.Reactor.Core
{
    public abstract class Element { }

    public sealed class RenderContext
    {
        public void UseEffect(Action effect, params object[] dependencies) { }
        public void UseEffect(Func<Action> effectWithCleanup, params object[] dependencies) { }
    }

    public abstract class Component
    {
        protected RenderContext Context = new RenderContext();
        public abstract Element Render();
        protected void UseEffect(Action effect, params object[] dependencies) { }
        protected void UseEffect(Func<Action> effectWithCleanup, params object[] dependencies) { }
    }
}

namespace App
{
    using Microsoft.UI.Reactor.Core;

    public sealed class TextElement : Element
    {
        public TextElement(string s) { }
    }

    public static class Data
    {
        public static Task<int> FetchAsync() => Task.FromResult(1);
        public static ValueTask<int> FetchValueAsync() => new ValueTask<int>(1);
        public static Task RunAsync() => Task.CompletedTask;
    }

    // A non-Task type that also exposes a .Result member — must never trip the rule.
    public sealed class Poll
    {
        public int Result => 42;
    }

    // A non-Task type exposing Wait()/Result()/GetAwaiter().GetResult() members whose
    // names match the syntactic fast path but whose receiver is not Task-like — the
    // semantic receiver-type check must reject all of these.
    public sealed class NotATask
    {
        public void Wait() { }
        public int Result() => 0;              // a method named Result, not the Task property
        public CustomAwaiter GetAwaiter() => new CustomAwaiter();
    }

    public sealed class CustomAwaiter
    {
        public int GetResult() => 0;
    }
}
";

    private static Task VerifyAsync(string body) =>
        new CSharpAnalyzerTest<BlockingTaskAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + body,
        }.RunAsync(TestContext.Current.CancellationToken);

    // ── Positive: blocking inside Render() ──────────────────────────────

    [Fact]
    public async Task Fires_For_Result_In_Render()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            var data = {|REACTOR_THREAD_002:Data.FetchAsync().Result|};
            return new TextElement(data.ToString());
        }
    }
}");
    }

    [Fact]
    public async Task Fires_For_Wait_In_Render()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            {|REACTOR_THREAD_002:Data.RunAsync().Wait()|};
            return new TextElement(""hi"");
        }
    }
}");
    }

    [Fact]
    public async Task Fires_For_GetAwaiter_GetResult_In_Render()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            var data = {|REACTOR_THREAD_002:Data.FetchAsync().GetAwaiter().GetResult()|};
            return new TextElement(data.ToString());
        }
    }
}");
    }

    [Fact]
    public async Task Fires_For_ValueTask_Result_In_Render()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            var data = {|REACTOR_THREAD_002:Data.FetchValueAsync().Result|};
            return new TextElement(data.ToString());
        }
    }
}");
    }

    // ── Positive: blocking inside a UseEffect lambda ────────────────────

    [Fact]
    public async Task Fires_For_Result_In_UseEffect_Lambda()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            UseEffect(() =>
            {
                var data = {|REACTOR_THREAD_002:Data.FetchAsync().Result|};
            }, System.Array.Empty<object>());
            return new TextElement(""hi"");
        }
    }
}");
    }

    [Fact]
    public async Task Fires_For_Result_In_RenderContext_UseEffect_Lambda()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            Context.UseEffect(() =>
            {
                {|REACTOR_THREAD_002:Data.RunAsync().Wait()|};
            }, System.Array.Empty<object>());
            return new TextElement(""hi"");
        }
    }
}");
    }

    // ── Negative: nested Task.Run inside Render (background thread) ──────

    [Fact]
    public async Task No_Diagnostic_For_Result_Inside_Nested_TaskRun()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    using System.Threading.Tasks;
    public sealed class C : Component
    {
        public override Element Render()
        {
            _ = Task.Run(() =>
            {
                var data = Data.FetchAsync().Result;
                return data;
            });
            return new TextElement(""hi"");
        }
    }
}");
    }

    [Fact]
    public async Task No_Diagnostic_For_GetResult_Inside_Nested_TaskRun_In_Effect()
    {
        // Task.Run inside a UseEffect body still moves the block off the UI thread.
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    using System.Threading.Tasks;
    public sealed class C : Component
    {
        public override Element Render()
        {
            UseEffect(() =>
            {
                _ = Task.Run(() => Data.FetchAsync().GetAwaiter().GetResult());
            }, System.Array.Empty<object>());
            return new TextElement(""hi"");
        }
    }
}");
    }

    // ── Negative: .Result on a non-Task property ───────────────────────

    [Fact]
    public async Task No_Diagnostic_For_Result_On_Non_Task()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            var poll = new Poll();
            var value = poll.Result;
            return new TextElement(value.ToString());
        }
    }
}");
    }

    // ── Near-miss: blocking OUTSIDE any render/effect context ──────────

    [Fact]
    public async Task No_Diagnostic_For_Result_Outside_Render_Or_Effect()
    {
        // Same Data.FetchAsync().Result shape, but in a plain method on a Component —
        // not Render(), not a UseEffect lambda. This is the syntactic near-miss that the
        // context walk must reject.
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render() => new TextElement(""hi"");

        public int LoadSync()
        {
            return Data.FetchAsync().Result;
        }
    }
}");
    }

    [Fact]
    public async Task No_Diagnostic_For_Result_In_NonComponent_Render()
    {
        // A Render() override that is NOT on a Reactor Component must not fire.
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public abstract class Drawable
    {
        public abstract void Render();
    }
    public sealed class C : Drawable
    {
        public override void Render()
        {
            var data = Data.FetchAsync().Result;
        }
    }
}");
    }

    [Fact]
    public async Task No_Diagnostic_For_Awaited_Task_In_Render()
    {
        // The correct async form: an async effect that awaits. No blocking member.
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            UseEffect(() => { _ = Load(); }, System.Array.Empty<object>());
            return new TextElement(""hi"");
        }

        private static async System.Threading.Tasks.Task Load()
        {
            var data = await Data.FetchAsync();
        }
    }
}");
    }

    // ── Positive: null-conditional blocking forms ──────────────────────

    [Fact]
    public async Task Fires_For_Conditional_Result_In_Render()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            var data = {|REACTOR_THREAD_002:Data.FetchAsync()?.Result|};
            return new TextElement(data.ToString());
        }
    }
}");
    }

    [Fact]
    public async Task Fires_For_Conditional_Wait_In_Render()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            {|REACTOR_THREAD_002:Data.RunAsync()?.Wait()|};
            return new TextElement(""hi"");
        }
    }
}");
    }

    [Fact]
    public async Task Fires_For_Conditional_GetAwaiter_GetResult_In_Render()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            var data = {|REACTOR_THREAD_002:Data.FetchAsync()?.GetAwaiter().GetResult()|};
            return new TextElement(data.ToString());
        }
    }
}");
    }

    [Fact]
    public async Task Fires_For_Conditional_Result_In_UseEffect_Lambda()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            UseEffect(() =>
            {
                var data = {|REACTOR_THREAD_002:Data.FetchAsync()?.Result|};
            }, System.Array.Empty<object>());
            return new TextElement(""hi"");
        }
    }
}");
    }

    // ── Positive: named-argument UseEffect ─────────────────────────────

    [Fact]
    public async Task Fires_For_Result_In_UseEffect_With_Named_Arguments()
    {
        // Named-argument reordering must still be recognized as the effect lambda.
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            UseEffect(dependencies: System.Array.Empty<object>(), effect: () =>
            {
                var data = {|REACTOR_THREAD_002:Data.FetchAsync().Result|};
            });
            return new TextElement(""hi"");
        }
    }
}");
    }

    // ── Positive: ConfigureAwait(false).GetAwaiter().GetResult() ────────

    [Fact]
    public async Task Fires_For_ConfigureAwait_GetResult_In_Render()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            var data = {|REACTOR_THREAD_002:Data.FetchAsync().ConfigureAwait(false).GetAwaiter().GetResult()|};
            return new TextElement(data.ToString());
        }
    }
}");
    }

    [Fact]
    public async Task Fires_For_ValueTask_ConfigureAwait_GetResult_In_Render()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            var data = {|REACTOR_THREAD_002:Data.FetchValueAsync().ConfigureAwait(false).GetAwaiter().GetResult()|};
            return new TextElement(data.ToString());
        }
    }
}");
    }

    // ── Negative: .Result inside nested Task.Run within an effect ───────

    [Fact]
    public async Task No_Diagnostic_For_Result_Inside_Nested_TaskRun_In_Effect()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    using System.Threading.Tasks;
    public sealed class C : Component
    {
        public override Element Render()
        {
            UseEffect(() =>
            {
                _ = Task.Run(() =>
                {
                    var data = Data.FetchAsync().Result;
                    return data;
                });
            }, System.Array.Empty<object>());
            return new TextElement(""hi"");
        }
    }
}");
    }

    // ── Near-miss: invocation fast-path shapes on non-Task receivers ────

    [Fact]
    public async Task No_Diagnostic_For_Wait_On_Non_Task()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            new NotATask().Wait();
            return new TextElement(""hi"");
        }
    }
}");
    }

    [Fact]
    public async Task No_Diagnostic_For_Result_Method_On_Non_Task()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            var value = new NotATask().Result();
            return new TextElement(value.ToString());
        }
    }
}");
    }

    [Fact]
    public async Task No_Diagnostic_For_GetAwaiter_GetResult_On_Non_Task()
    {
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            var value = new NotATask().GetAwaiter().GetResult();
            return new TextElement(value.ToString());
        }
    }
}");
    }

    [Fact]
    public async Task No_Diagnostic_For_Wait_With_Timeout_Argument()
    {
        // Only zero-arg .Wait() is in scope; the timeout overload (returns bool and
        // includes the non-blocking Wait(0) poll) is intentionally excluded.
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            _ = Data.RunAsync().Wait(0);
            return new TextElement(""hi"");
        }
    }
}");
    }

    // ── Accepted false negative: blocking inside a non-effect nested function ──

    [Fact]
    public async Task No_Diagnostic_For_Result_In_NonEffect_Nested_Lambda_In_Render()
    {
        // Blocking inside a nested lambda that is not the UseEffect effect (here a LINQ
        // projection) is intentionally NOT flagged: a syntactic analyzer cannot prove the
        // lambda runs synchronously on the render thread, so it is treated as a deferred
        // boundary to keep false positives near zero (see ClassifyContext).
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    using System.Linq;
    public sealed class C : Component
    {
        public override Element Render()
        {
            var ids = new[] { 1, 2 };
            var first = ids.Select(i => Data.FetchAsync().Result).First();
            return new TextElement(first.ToString());
        }
    }
}");
    }

    [Fact]
    public async Task No_Diagnostic_For_Result_In_Local_Function_In_Render()
    {
        // A local function is a deferred boundary — blocking inside one is not flagged
        // even though it is declared in Render.
        await VerifyAsync(@"
namespace App
{
    using Microsoft.UI.Reactor.Core;
    public sealed class C : Component
    {
        public override Element Render()
        {
            int Load() => Data.FetchAsync().Result;
            return new TextElement(Load().ToString());
        }
    }
}");
    }
}
