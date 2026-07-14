using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="UIThreadAffinityAnalyzer"/> (<c>REACTOR_THREAD_001</c>) and
/// its <see cref="UIThreadAffinityCodeFix"/>. Stubs a minimal Reactor shape — a
/// <c>[UIThreadOnly]</c>-marked mutator, the <c>ReactorApp.UIDispatcher</c> /
/// <c>DispatcherQueue.TryEnqueue</c> marshal path — so the analyzer's
/// background-lambda gate and attribute check fire without pulling the framework in.
/// The stub attribute reuses the real namespace/name (<c>Microsoft.UI.Reactor.Hosting.UIThreadOnlyAttribute</c>)
/// that the analyzer keys off in metadata.
/// </summary>
public class UIThreadAffinityAnalyzerTests
{
    private const string Stubs = @"
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Hosting;

namespace Microsoft.UI.Dispatching
{
    public sealed class DispatcherQueue
    {
        public bool TryEnqueue(Action callback) { callback(); return true; }
    }
}

namespace Microsoft.UI.Reactor.Hosting
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property, Inherited = false)]
    public sealed class UIThreadOnlyAttribute : Attribute { }
}

namespace Microsoft.UI.Reactor
{
    public static class ReactorApp
    {
        public static DispatcherQueue UIDispatcher = new DispatcherQueue();
    }
}

public sealed class FakeWindow
{
    [UIThreadOnly] public void Close() { }
    [UIThreadOnly] public void Activate() { }

    // UI-thread-only property: the setter would call ThrowIfNotOnUIThread.
    [UIThreadOnly] public string Title { get; set; } = string.Empty;
    [UIThreadOnly] public int Ticks { get; set; }

    // Attribute on the set accessor only (not the property symbol).
    public int Guarded { get; [UIThreadOnly] set; }

    // Not UI-thread-only — background use is legitimate.
    public void SafeMethod() { }
}

// A non-dispatcher type that happens to expose a TryEnqueue method.
public sealed class NotADispatcher
{
    public bool TryEnqueue(Action callback) { callback(); return true; }
}

// A DispatcherQueue that is not ReactorApp.UIDispatcher.
public static class OtherHost
{
    public static DispatcherQueue OtherDispatcher = new DispatcherQueue();
}
";

    // ── Positive: fires inside each background launcher ──────────────────

    [Fact]
    public async Task Fires_For_Marked_Method_In_TaskRun_ExpressionLambda()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() => {|REACTOR_THREAD_001:window.Close()|});
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Marked_Method_In_TaskRun_Block()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() =>
        {
            {|REACTOR_THREAD_001:window.Close()|};
        });
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Marked_Method_In_TaskFactoryStartNew()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Factory.StartNew(() => {|REACTOR_THREAD_001:window.Close()|});
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Marked_Method_In_ThreadPool_QueueUserWorkItem()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        ThreadPool.QueueUserWorkItem(_ => {|REACTOR_THREAD_001:window.Close()|});
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Marked_Method_Nested_In_Inner_Lambda()
    {
        // The call is two lambdas deep inside Task.Run — the inner LINQ-style
        // lambda is transparent, the Task.Run boundary still governs the thread.
        var source = Stubs + @"
class C
{
    void M(List<int> items)
    {
        var window = new FakeWindow();
        Task.Run(() => items.ForEach(x => {|REACTOR_THREAD_001:window.Close()|}));
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negative: marshaled or unmarked ─────────────────────────────────

    [Fact]
    public async Task No_Diagnostic_When_Already_Marshaled_Through_TryEnqueue()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() => ReactorApp.UIDispatcher.TryEnqueue(() => window.Close()));
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Unmarked_Method_In_TaskRun()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() => window.SafeMethod());
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Near-miss: almost trips the syntactic fast path ─────────────────

    [Fact]
    public async Task No_Diagnostic_For_Marked_Method_On_UI_Thread()
    {
        // Called directly — not inside any background lambda. This is the correct
        // UI-thread call site and must not fire.
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        window.Close();
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Marked_Method_In_Plain_Lambda()
    {
        // A lambda that is not passed to a background launcher — e.g. assigned to
        // an Action — runs on whatever thread invokes it; the gate must not fire.
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Action a = () => window.Close();
        a();
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Code fix: null-safe dispatcher marshal ──────────────────────────

    [Fact]
    public async Task CodeFix_Marshals_ExpressionLambda_Call()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() => {|REACTOR_THREAD_001:window.Close()|});
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() =>
        {
            var d = ReactorApp.UIDispatcher;
            if (d is null)
                window.Close();
            else
                d.TryEnqueue(() => window.Close());
        });
    }
}";

        await new CSharpCodeFixTest<UIThreadAffinityAnalyzer, UIThreadAffinityCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Marshals_Statement_In_Block()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() =>
        {
            {|REACTOR_THREAD_001:window.Close()|};
        });
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() =>
        {
            var d = ReactorApp.UIDispatcher;
            if (d is null)
                window.Close();
            else
                d.TryEnqueue(() => window.Close());
        });
    }
}";

        await new CSharpCodeFixTest<UIThreadAffinityAnalyzer, UIThreadAffinityCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Property setters (the [UIThreadOnly] attribute also targets properties) ──

    [Fact]
    public async Task Fires_For_Marked_Property_Set_In_TaskRun()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() => {|REACTOR_THREAD_001:window.Title = ""hi""|});
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Marked_Property_Read_In_TaskRun()
    {
        // Only writes hit the UI-thread-guarded setter; a read must not fire.
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() =>
        {
            var t = window.Title;
        });
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Marshals_Property_Set()
    {
        // Property-set fix uses the block form: `Task.Run(() => window.Title = ...)`
        // as an expression body binds to Func<string>, so the fix is (correctly) not
        // offered there — see No_Fix_For_Value_Producing_Expression_Lambda.
        var before = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() =>
        {
            {|REACTOR_THREAD_001:window.Title = ""hi""|};
        });
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() =>
        {
            var d = ReactorApp.UIDispatcher;
            if (d is null)
                window.Title = ""hi"";
            else
                d.TryEnqueue(() => window.Title = ""hi"");
        });
    }
}";

        await new CSharpCodeFixTest<UIThreadAffinityAnalyzer, UIThreadAffinityCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Fix_For_Value_Producing_Expression_Lambda()
    {
        // `() => window.Title = "hi"` binds to Func<string> (Task.Run(Func<T>)), so
        // rewriting the expression body into a statement block would change overload
        // resolution. The analyzer still fires; no fix is offered (TestCode == FixedCode).
        var code = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() => {|REACTOR_THREAD_001:window.Title = ""hi""|});
    }
}";

        await new CSharpCodeFixTest<UIThreadAffinityAnalyzer, UIThreadAffinityCodeFix, DefaultVerifier>
        {
            TestCode = code,
            FixedCode = code,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Marked_Property_Increment()
    {
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() => {|REACTOR_THREAD_001:window.Ticks++|});
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Marshals_Property_Increment_In_Block()
    {
        var before = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() =>
        {
            {|REACTOR_THREAD_001:window.Ticks++|};
        });
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() =>
        {
            var d = ReactorApp.UIDispatcher;
            if (d is null)
                window.Ticks++;
            else
                d.TryEnqueue(() => window.Ticks++);
        });
    }
}";

        await new CSharpCodeFixTest<UIThreadAffinityAnalyzer, UIThreadAffinityCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Null-fallback suppression precision (must be tied to the dispatcher) ──

    [Fact]
    public async Task No_Diagnostic_For_Matching_Dispatcher_Null_Fallback()
    {
        // The exact idiom the code fix emits: same local checked for null and used
        // as the TryEnqueue receiver. The direct call is the safe pre-bootstrap path.
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() =>
        {
            var d = ReactorApp.UIDispatcher;
            if (d is null)
                window.Close();
            else
                d.TryEnqueue(() => window.Close());
        });
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_When_Null_Check_Marshals_Through_Unrelated_Receiver()
    {
        // An unrelated null check whose else marshals through a DIFFERENT receiver
        // must NOT suppress the direct call — otherwise a real off-thread call hides.
        var source = Stubs + @"
class C
{
    void M(object gate)
    {
        var window = new FakeWindow();
        Task.Run(() =>
        {
            if (gate is null)
                {|REACTOR_THREAD_001:window.Close()|};
            else
                ReactorApp.UIDispatcher.TryEnqueue(() => window.Close());
        });
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Collision-free dispatcher local name ──

    [Fact]
    public async Task CodeFix_Uses_CollisionFree_Name_When_d_In_Scope()
    {
        var before = Stubs + @"
class C
{
    void M(int d)
    {
        var window = new FakeWindow();
        Task.Run(() => {|REACTOR_THREAD_001:window.Close()|});
    }
}";

        var after = Stubs + @"
class C
{
    void M(int d)
    {
        var window = new FakeWindow();
        Task.Run(() =>
        {
            var dispatcher = ReactorApp.UIDispatcher;
            if (dispatcher is null)
                window.Close();
            else
                dispatcher.TryEnqueue(() => window.Close());
        });
    }
}";

        await new CSharpCodeFixTest<UIThreadAffinityAnalyzer, UIThreadAffinityCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Marked_Property_Compound_Assignment()
    {
        // A compound assignment still invokes the guarded setter.
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() => {|REACTOR_THREAD_001:window.Title += ""!""|});
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CodeFix_Preserves_Sibling_Statements_And_Comment()
    {
        // Only the flagged statement is wrapped; the sibling call and the comment
        // above the flagged statement are preserved (no whole-block churn).
        var before = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() =>
        {
            window.SafeMethod();
            // marshal me
            {|REACTOR_THREAD_001:window.Close()|};
        });
    }
}";

        var after = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() =>
        {
            window.SafeMethod();
            // marshal me
            var d = ReactorApp.UIDispatcher;
            if (d is null)
                window.Close();
            else
                d.TryEnqueue(() => window.Close());
        });
    }
}";

        await new CSharpCodeFixTest<UIThreadAffinityAnalyzer, UIThreadAffinityCodeFix, DefaultVerifier>
        {
            TestCode = before,
            FixedCode = after,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Semantic precision: attribute on the set accessor; real vs. fake dispatcher ──

    [Fact]
    public async Task Fires_For_Attribute_On_Set_Accessor()
    {
        // [UIThreadOnly] on the setter (SetMethod), not on the property symbol.
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() => {|REACTOR_THREAD_001:window.Guarded = 5|});
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_When_TryEnqueue_Receiver_Is_Not_A_DispatcherQueue()
    {
        // An unrelated TryEnqueue (not Microsoft.UI.Dispatching.DispatcherQueue) must
        // not be treated as marshaling — the call still runs on the background thread.
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        var other = new NotADispatcher();
        Task.Run(() => other.TryEnqueue(() => {|REACTOR_THREAD_001:window.Close()|}));
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_StartNew_On_A_Local_TaskFactory()
    {
        // Receiver is a local (`factory`), not the literal `Task.Factory`. The type
        // confirmation (TaskFactory), not the receiver name, drives the gate.
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        var factory = Task.Factory;
        factory.StartNew(() => {|REACTOR_THREAD_001:window.Close()|});
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Null_Fallback_On_A_Non_Reactor_Dispatcher()
    {
        // `d` is a DispatcherQueue but NOT ReactorApp.UIDispatcher. If it is null
        // while the framework dispatcher is already captured, the direct call still
        // throws — so the null-fallback suppression must not apply here.
        var source = Stubs + @"
class C
{
    void M()
    {
        var window = new FakeWindow();
        Task.Run(() =>
        {
            var d = OtherHost.OtherDispatcher;
            if (d is null)
                {|REACTOR_THREAD_001:window.Close()|};
            else
                d.TryEnqueue(() => window.Close());
        });
    }
}";

        await new CSharpAnalyzerTest<UIThreadAffinityAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
