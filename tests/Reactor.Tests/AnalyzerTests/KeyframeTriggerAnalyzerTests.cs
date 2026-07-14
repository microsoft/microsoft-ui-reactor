using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="KeyframeTriggerAnalyzer"/> (<c>REACTOR_ANIM_002</c>).
/// Stubs the minimum <c>.Keyframes(name, trigger, configure)</c> surface so the
/// analyzer's syntactic gate and its semantic "is this Reactor's Keyframes?"
/// confirmation both fire without pulling the framework in.
/// </summary>
public class KeyframeTriggerAnalyzerTests
{
    // `IsExternalInit` is required for `record` types under older runtime
    // metadata — supply a stub so test sources can use records freely. The
    // Reactor stub lives in `Microsoft.UI.Reactor.Core` (the analyzer accepts
    // any `Microsoft.UI.Reactor*` namespace for `ElementExtensions`).
    private const string Stubs = @"
namespace System.Runtime.CompilerServices
{
    public static class IsExternalInit { }
}

namespace Microsoft.UI.Reactor.Core
{
    public abstract record Element { }
    public sealed record BorderElement : Element { }

    public sealed class KeyframeBuilder
    {
        public KeyframeBuilder Opacity(double from, double to) => this;
    }

    public static class Factories
    {
        public static BorderElement Border() => new();
    }

    public static class ElementExtensions
    {
        // The real 3-arg modifier: name, trigger, configure.
        public static T Keyframes<T>(this T el, string name, object? trigger,
            System.Func<KeyframeBuilder, KeyframeBuilder> configure) where T : Element => el;

        // A 2-arg near-miss overload used to prove the arity gate.
        public static T Keyframes<T>(this T el, string name, object? trigger) where T : Element => el;
    }
}
";

    // Injects `body` into a fixed builder method that already has a stable local
    // (`stableKey`) and stable parameters in scope.
    private static Task Verify(string body) =>
        new CSharpAnalyzerTest<KeyframeTriggerAnalyzer, DefaultVerifier>
        {
            TestCode = Stubs + @"
namespace TestApp
{
    using System;
    using System.Collections.Generic;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Core.Factories;

    public static class C
    {
        public static Element Build(int stableCounter, string name)
        {
            var stableKey = stableCounter;
" + body + @"
        }
    }
}",
        }.RunAsync(TestContext.Current.CancellationToken);

    // ── Positive: unstable time / identity sources fire ─────────────────

    [Fact]
    public Task Fires_On_DateTime_Now() =>
        Verify(@"            return Border().Keyframes(""pulse"", {|REACTOR_ANIM_002:DateTime.Now|}, kf => kf.Opacity(0, 1));");

    [Fact]
    public Task Fires_On_DateTime_UtcNow() =>
        Verify(@"            return Border().Keyframes(""pulse"", {|REACTOR_ANIM_002:DateTime.UtcNow|}, kf => kf.Opacity(0, 1));");

    [Fact]
    public Task Fires_On_DateTimeOffset_Now() =>
        Verify(@"            return Border().Keyframes(""pulse"", {|REACTOR_ANIM_002:DateTimeOffset.Now|}, kf => kf.Opacity(0, 1));");

    [Fact]
    public Task Fires_On_DateTimeOffset_UtcNow() =>
        Verify(@"            return Border().Keyframes(""pulse"", {|REACTOR_ANIM_002:DateTimeOffset.UtcNow|}, kf => kf.Opacity(0, 1));");

    [Fact]
    public Task Fires_On_Guid_NewGuid() =>
        Verify(@"            return Border().Keyframes(""pulse"", {|REACTOR_ANIM_002:Guid.NewGuid()|}, kf => kf.Opacity(0, 1));");

    [Fact]
    public Task Fires_On_Fully_Qualified_Guid_NewGuid() =>
        Verify(@"            return Border().Keyframes(""pulse"", {|REACTOR_ANIM_002:System.Guid.NewGuid()|}, kf => kf.Opacity(0, 1));");

    [Fact]
    public Task Fires_On_Environment_TickCount() =>
        Verify(@"            return Border().Keyframes(""pulse"", {|REACTOR_ANIM_002:Environment.TickCount|}, kf => kf.Opacity(0, 1));");

    [Fact]
    public Task Fires_On_Environment_TickCount64() =>
        Verify(@"            return Border().Keyframes(""pulse"", {|REACTOR_ANIM_002:Environment.TickCount64|}, kf => kf.Opacity(0, 1));");

    // ── Positive: per-render allocations fire ───────────────────────────

    [Fact]
    public Task Fires_On_Fresh_Object_Allocation() =>
        Verify(@"            return Border().Keyframes(""pulse"", {|REACTOR_ANIM_002:new List<int>()|}, kf => kf.Opacity(0, 1));");

    [Fact]
    public Task Fires_On_Implicit_Object_Allocation() =>
        Verify(@"            return Border().Keyframes(""pulse"", {|REACTOR_ANIM_002:new()|}, kf => kf.Opacity(0, 1));");

    [Fact]
    public Task Fires_On_Fresh_Array_Allocation() =>
        Verify(@"            return Border().Keyframes(""pulse"", {|REACTOR_ANIM_002:new int[] { 1, 2, 3 }|}, kf => kf.Opacity(0, 1));");

    [Fact]
    public Task Fires_On_Implicit_Array_Allocation() =>
        Verify(@"            return Border().Keyframes(""pulse"", {|REACTOR_ANIM_002:new[] { 1, 2, 3 }|}, kf => kf.Opacity(0, 1));");

    [Fact]
    public Task Fires_On_Casted_Collection_Expression() =>
        // A bare `[...]` can't bind to `object?`, but a casted collection
        // expression can; UnwrapCasts exposes the inner collection expression.
        Verify(@"            return Border().Keyframes(""pulse"", {|REACTOR_ANIM_002:(int[])[1, 2, 3]|}, kf => kf.Opacity(0, 1));");

    // ── Positive: argument-resolution variants ──────────────────────────

    [Fact]
    public Task Fires_On_Named_Trigger_Argument_Reordered() =>
        // Named args in a different order — the analyzer must still find `trigger`.
        Verify(@"            return Border().Keyframes(configure: kf => kf.Opacity(0, 1), name: ""pulse"", trigger: {|REACTOR_ANIM_002:DateTime.Now|});");

    [Fact]
    public Task Fires_On_Positional_Trigger_With_Trailing_Named_Configure() =>
        // `configure:` is named/trailing; the trigger stays positional at index 1.
        Verify(@"            return Border().Keyframes(""pulse"", {|REACTOR_ANIM_002:DateTime.Now|}, configure: kf => kf.Opacity(0, 1));");

    // ── Negative: stable / value-equal triggers do not fire ─────────────

    [Fact]
    public Task No_Diagnostic_On_Stable_Local() =>
        Verify(@"            return Border().Keyframes(""pulse"", stableKey, kf => kf.Opacity(0, 1));");

    [Fact]
    public Task No_Diagnostic_On_Stable_Parameter() =>
        Verify(@"            return Border().Keyframes(""pulse"", stableCounter, kf => kf.Opacity(0, 1));");

    [Fact]
    public Task No_Diagnostic_On_Anonymous_Object() =>
        // Anonymous objects have value equality, so a stable-valued one does NOT
        // re-fire under !Equals — flagging it would be a false positive.
        Verify(@"            return Border().Keyframes(""pulse"", new { frame = stableCounter }, kf => kf.Opacity(0, 1));");

    // ── Near-miss: almost trips the syntactic fast path, but must not ───

    [Fact]
    public Task No_Diagnostic_When_Unstable_Value_Is_In_Name_Arg_Not_Trigger() =>
        // Allocation/unstable in the NAME slot; the trigger itself is stable.
        // Proves the analyzer inspects only the trigger argument (index 1).
        Verify(@"            return Border().Keyframes(Guid.NewGuid().ToString(), stableKey, kf => kf.Opacity(0, 1));");

    [Fact]
    public Task No_Diagnostic_On_Two_Arg_Overload() =>
        // Wrong arity — the 2-arg overload isn't the trigger-based modifier.
        Verify(@"            return Border().Keyframes(""pulse"", DateTime.Now);");

    // ── Semantic guard: binds to Reactor's Keyframes, not just any ──────

    [Fact]
    public async Task Fires_On_Generic_Element_Helper()
    {
        // Receiver is a type parameter `T : Element`; the invocation still binds
        // to Reactor's Keyframes, so a symbol-based guard must not false-negate.
        var source = Stubs + @"
namespace TestApp
{
    using System;
    using Microsoft.UI.Reactor.Core;

    public static class C
    {
        public static T Pulse<T>(T el) where T : Element
            => el.Keyframes(""x"", {|REACTOR_ANIM_002:DateTime.Now|}, kf => kf.Opacity(0, 1));
    }
}";

        await new CSharpAnalyzerTest<KeyframeTriggerAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_On_ThirdParty_Keyframes_Method()
    {
        // A same-named 3-arg `Keyframes` extension from another library, with an
        // unstable middle argument, must NOT produce a Reactor diagnostic.
        var source = Stubs + @"
namespace Other
{
    public static class OtherAnimations
    {
        public static T Keyframes<T>(this T self, string name, object? trigger, System.Func<int, int> configure) => self;
    }
}

namespace TestApp
{
    using System;
    using Other;

    public static class C
    {
        public static int Build()
            => 42.Keyframes(""x"", DateTime.Now, i => i);
    }
}";

        await new CSharpAnalyzerTest<KeyframeTriggerAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
