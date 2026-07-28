using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="EffectCleanupAnalyzer"/> (<c>REACTOR_LIFECYCLE_002</c>). Stubs a minimal
/// Reactor surface — a <c>RenderContext</c>/<c>Component</c> exposing both the <c>Action</c> and
/// <c>Func&lt;Action&gt;</c> <c>UseEffect</c> overloads — plus lightweight producer types
/// (<c>PeriodicTimer</c>/<c>Timer</c>, an observable-shaped <c>Subscribe</c>, and an event source)
/// so the analyzer's overload selection and lifetime-allocation detection resolve without pulling
/// in the framework.
/// </summary>
public class EffectCleanupAnalyzerTests
{
    private const string Stubs = @"
using System;

namespace System.Runtime.CompilerServices { public static class IsExternalInit { } }

namespace Fakes
{
    // Simple-named producer types the analyzer recognizes.
    public sealed class PeriodicTimer : IDisposable
    {
        public PeriodicTimer(TimeSpan period) { }
        public void Dispose() { }
        public System.Threading.Tasks.ValueTask DisposeAsync() => default;
        public System.Threading.Tasks.Task<bool> WaitForNextTickAsync() =>
            System.Threading.Tasks.Task.FromResult(true);
    }

    // Real System.Threading.Timer / System.Timers.Timer are IDisposable.
    public sealed class Timer : IDisposable
    {
        public Timer(Action callback) { }
        public void Dispose() { }
    }

    // Distinctive dispatcher-timer name that exposes Start/Stop instead of IDisposable (the real
    // WinUI DispatcherTimer has a public constructor — see the samples).
    public sealed class DispatcherTimer
    {
        public void Start() { }
        public void Stop() { }
    }

    public sealed class Subscription : IDisposable { public void Dispose() { } }

    public sealed class Ticker
    {
        // Rx-shaped: Subscribe returns System.IDisposable directly.
        public IDisposable Subscribe(Action onNext) => new Subscription();
    }

    public sealed class ConcreteTicker
    {
        // Subscribe returns a concrete type that implements IDisposable (interface-set path).
        public Subscription Subscribe(Action onNext) => new Subscription();
    }

    public sealed class VoidTicker
    {
        public void Subscribe(Action onNext) { }
    }

    public sealed class PlainTicker
    {
        // Subscribe returns a non-disposable value.
        public int Subscribe(Action onNext) => 0;
    }

    public sealed class Producer
    {
        public event Action Ping;
        public void Raise() => Ping?.Invoke();
    }
}

namespace UserCode
{
    // A user type that merely shares the `Timer` simple name but is not disposable — must NOT fire.
    public sealed class Timer
    {
        public Timer(System.Action callback) { }
    }
}

namespace Microsoft.UI.Reactor.Core
{
    using System;

    public class RenderContext
    {
        public void UseEffect(Action effect, params object[] dependencies) { }
        public void UseEffect(Func<Action> effectWithCleanup, params object[] dependencies) { }
        public void UseEffect<T1>(Action effect, T1 d1) { }
        public void UseEffect<T1>(Func<Action> effectWithCleanup, T1 d1) { }
    }

    public abstract class Component
    {
        protected internal RenderContext Context { get; } = new RenderContext();
        public abstract string Render();
        protected void UseEffect(Action effect, params object[] dependencies)
            => Context.UseEffect(effect, dependencies);
        protected void UseEffect(Func<Action> effectWithCleanup, params object[] dependencies)
            => Context.UseEffect(effectWithCleanup, dependencies);
        protected void UseEffect<T1>(Action effect, T1 d1) => Context.UseEffect(effect, d1);
        protected void UseEffect<T1>(Func<Action> effectWithCleanup, T1 d1)
            => Context.UseEffect(effectWithCleanup, d1);
        protected (int, Action<Func<int, int>>) UseReducer(int initial) => (0, _ => { });
    }
}
";

    private static Task Verify(string body) =>
        new CSharpAnalyzerTest<EffectCleanupAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + body,
        }.RunAsync(TestContext.Current.CancellationToken);

    // ── Positive ────────────────────────────────────────────────────────

    // The canonical docs/guide/effects.md "Missing cleanup" example.
    [Fact]
    public Task Fires_On_PeriodicTimer_Without_Cleanup()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            var (tick, updateTick) = UseReducer(0);
            UseEffect(() =>
            {
                var timer = {|REACTOR_LIFECYCLE_002:new PeriodicTimer(TimeSpan.FromSeconds(1))|};
                System.Threading.Tasks.Task.Run(async () =>
                {
                    while (await timer.WaitForNextTickAsync())
                        updateTick(t => t + 1);
                });
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    [Fact]
    public Task Fires_On_Timer_Without_Cleanup()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            UseEffect(() =>
            {
                var t = {|REACTOR_LIFECYCLE_002:new Timer(() => { })|};
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    [Fact]
    public Task Fires_On_Subscription_Without_Cleanup()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            var ticker = new Ticker();
            UseEffect(() =>
            {
                {|REACTOR_LIFECYCLE_002:ticker.Subscribe(() => { })|};
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    [Fact]
    public Task Fires_On_Event_Subscription_Without_Unsubscribe()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        void OnPing() { }

        public override string Render()
        {
            var producer = new Producer();
            UseEffect(() =>
            {
                {|REACTOR_LIFECYCLE_002:producer.Ping += OnPing|};
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // Anchors on RenderContext directly (Context.UseEffect), not just the Component wrapper.
    [Fact]
    public Task Fires_Via_RenderContext_Receiver()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            Context.UseEffect(() =>
            {
                var t = {|REACTOR_LIFECYCLE_002:new PeriodicTimer(TimeSpan.FromSeconds(1))|};
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // Expression-bodied effect whose whole body IS the offending subscription.
    [Fact]
    public Task Fires_On_Expression_Bodied_Effect()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            var ticker = new Ticker();
            UseEffect(() => {|REACTOR_LIFECYCLE_002:ticker.Subscribe(() => { })|}, Array.Empty<object>());
            return """";
        }
    }
}");

    // ── Negative ────────────────────────────────────────────────────────

    // Returning a cleanup selects the Func<Action> overload — the correct pattern.
    [Fact]
    public Task NoFire_When_Cleanup_Returned()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            UseEffect(() =>
            {
                var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                return () => timer.Dispose();
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    [Fact]
    public Task NoFire_When_Using_Declaration()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            UseEffect(() =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    [Fact]
    public Task NoFire_When_Disposed_In_Body()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            UseEffect(() =>
            {
                var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                timer.Dispose();
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    [Fact]
    public Task NoFire_When_Event_Unsubscribed_In_Body()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        void OnPing() { }

        public override string Render()
        {
            var producer = new Producer();
            UseEffect(() =>
            {
                producer.Ping += OnPing;
                producer.Ping -= OnPing;
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // A numeric -= must NOT be mistaken for an event unsubscribe, so the timer still fires.
    [Fact]
    public Task Fires_Even_With_Numeric_CompoundAssign()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            var count = 5;
            UseEffect(() =>
            {
                var timer = {|REACTOR_LIFECYCLE_002:new PeriodicTimer(TimeSpan.FromSeconds(1))|};
                count -= 1;
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // No lifetime resource at all — pure side effect.
    [Fact]
    public Task NoFire_When_No_Lifetime_Resource()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            UseEffect(() =>
            {
                Console.WriteLine(""side effect"");
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // Producer created inside a nested continuation has its own lifetime — not effect setup, so the
    // top-level-only allocation scan skips it even though the effect returns no cleanup.
    [Fact]
    public Task NoFire_When_Resource_In_Nested_Lambda()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            UseEffect(() =>
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    var t = new PeriodicTimer(TimeSpan.FromSeconds(1));
                });
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // UseEffect on an unrelated (non-Reactor) type must not be flagged.
    [Fact]
    public Task NoFire_When_Not_Reactor_UseEffect()
        => Verify(@"
namespace TestApp
{
    using System;
    using Fakes;

    public sealed class NotReactor
    {
        public void UseEffect(Action effect, params object[] deps) { }

        public void Setup()
        {
            UseEffect(() =>
            {
                var t = new PeriodicTimer(TimeSpan.FromSeconds(1));
            }, Array.Empty<object>());
        }
    }
}");

    // Near-miss: a method group hides the body, so the rule can't prove a leak — bail.
    [Fact]
    public Task NoFire_On_Method_Group_Effect()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        void SetUp()
        {
            var t = new PeriodicTimer(TimeSpan.FromSeconds(1));
        }

        public override string Render()
        {
            UseEffect(SetUp, Array.Empty<object>());
            return """";
        }
    }
}");

    // Near-miss: a similarly-named hook that isn't UseEffect never trips the syntactic fast path.
    [Fact]
    public Task NoFire_On_Similarly_Named_Hook()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public static class Extra
    {
        public static void UseLayoutEffect(this RenderContext ctx, Action effect, params object[] deps) { }
    }

    public sealed class Comp : Component
    {
        public override string Render()
        {
            Context.UseLayoutEffect(() =>
            {
                var t = new PeriodicTimer(TimeSpan.FromSeconds(1));
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // ── Added coverage: producer branches, overloads, and bail paths ─────

    // Target-typed `new(...)` — the type comes from the declared variable, resolved semantically.
    [Fact]
    public Task Fires_On_TargetTyped_New_Timer()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            UseEffect(() =>
            {
                PeriodicTimer t = {|REACTOR_LIFECYCLE_002:new(TimeSpan.FromSeconds(1))|};
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // A distinctive dispatcher-timer name (not IDisposable) is matched by name.
    [Fact]
    public Task Fires_On_DispatcherTimer()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            UseEffect(() =>
            {
                var t = {|REACTOR_LIFECYCLE_002:new DispatcherTimer()|};
                t.Start();
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // Subscribe returning a concrete IDisposable implementer (interface-set path).
    [Fact]
    public Task Fires_On_Subscribe_Concrete_Disposable()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            var ticker = new ConcreteTicker();
            UseEffect(() =>
            {
                {|REACTOR_LIFECYCLE_002:ticker.Subscribe(() => { })|};
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // Typed arity-1 Action overload is still the no-cleanup overload → fires.
    [Fact]
    public Task Fires_On_Typed_Arity_Action_Overload()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            var dep = 5;
            UseEffect(() =>
            {
                var t = {|REACTOR_LIFECYCLE_002:new PeriodicTimer(TimeSpan.FromSeconds(1))|};
            }, dep);
            return """";
        }
    }
}");

    // An anonymous-method effect body is inspected like a lambda.
    [Fact]
    public Task Fires_On_AnonymousMethod_Effect()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            UseEffect(delegate
            {
                var t = {|REACTOR_LIFECYCLE_002:new PeriodicTimer(TimeSpan.FromSeconds(1))|};
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // Multiple producers in one body → exactly one diagnostic (on the first).
    [Fact]
    public Task Reports_Single_Diagnostic_For_Multiple_Producers()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        void OnPing() { }

        public override string Render()
        {
            var producer = new Producer();
            UseEffect(() =>
            {
                var t = {|REACTOR_LIFECYCLE_002:new PeriodicTimer(TimeSpan.FromSeconds(1))|};
                producer.Ping += OnPing;
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // A user type that merely shares the `Timer` name but is not disposable → must NOT fire.
    [Fact]
    public Task NoFire_On_UserDefined_NonDisposable_Timer()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            UseEffect(() =>
            {
                var t = new UserCode.Timer(() => { });
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // Typed arity-1 Func<Action> (cleanup) overload → not the Action overload → must NOT fire,
    // even with a no-op cleanup (we trust the cleanup contract; adequacy is unprovable).
    [Fact]
    public Task NoFire_On_Typed_Arity_Cleanup_Overload()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            var dep = 5;
            UseEffect(() =>
            {
                var t = new PeriodicTimer(TimeSpan.FromSeconds(1));
                return () => { };
            }, dep);
            return """";
        }
    }
}");

    // Subscribe returning void is not a subscription handle → must NOT fire.
    [Fact]
    public Task NoFire_On_Void_Subscribe()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            var ticker = new VoidTicker();
            UseEffect(() =>
            {
                ticker.Subscribe(() => { });
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // Subscribe returning a non-disposable value → must NOT fire.
    [Fact]
    public Task NoFire_On_NonDisposable_Subscribe()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            var ticker = new PlainTicker();
            UseEffect(() =>
            {
                ticker.Subscribe(() => { });
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // `using (...) { }` block statement form is a teardown signal → must NOT fire.
    [Fact]
    public Task NoFire_On_Using_Statement_Block()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            UseEffect(() =>
            {
                using (var t = new PeriodicTimer(TimeSpan.FromSeconds(1))) { }
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // DisposeAsync in the body is a teardown signal → must NOT fire.
    [Fact]
    public Task NoFire_On_DisposeAsync_In_Body()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            UseEffect(() =>
            {
                var t = new PeriodicTimer(TimeSpan.FromSeconds(1));
                _ = t.DisposeAsync();
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // Conditional-access UseEffect (`ctx?.UseEffect(...)`) still passes the syntactic fast path.
    [Fact]
    public Task Fires_Via_ConditionalAccess_UseEffect()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            Context?.UseEffect(() =>
            {
                var t = {|REACTOR_LIFECYCLE_002:new PeriodicTimer(TimeSpan.FromSeconds(1))|};
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // Conditional-access disposal (`timer?.Dispose()`) is a teardown signal → must NOT fire.
    [Fact]
    public Task NoFire_On_ConditionalAccess_Dispose()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            UseEffect(() =>
            {
                var t = new PeriodicTimer(TimeSpan.FromSeconds(1));
                t?.Dispose();
            }, Array.Empty<object>());
            return """";
        }
    }
}");

    // A user-declared UseEffect that shadows the framework hook (declared on the subclass, not on
    // Component/RenderContext) has unknown semantics → must NOT be treated as the Reactor hook.
    [Fact]
    public Task NoFire_On_User_Shadowing_UseEffect()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        // Distinct single-arg overload declared on the subclass; the call below binds to it.
        public void UseEffect(Action effect) { }

        public override string Render()
        {
            UseEffect(() =>
            {
                var t = new PeriodicTimer(TimeSpan.FromSeconds(1));
            });
            return """";
        }
    }
}");

    // Stopping a timer in the body (DispatcherTimer's teardown verb) is a cleanup signal → no fire.
    [Fact]
    public Task NoFire_When_Timer_Stopped_In_Body()
        => Verify(@"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;
    using Fakes;

    public sealed class Comp : Component
    {
        public override string Render()
        {
            UseEffect(() =>
            {
                var t = new DispatcherTimer();
                t.Start();
                t.Stop();
            }, Array.Empty<object>());
            return """";
        }
    }
}");
}
