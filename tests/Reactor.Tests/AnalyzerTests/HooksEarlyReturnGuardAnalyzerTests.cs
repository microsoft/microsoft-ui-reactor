using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <c>REACTOR_HOOKS_002</c> (hook after a single-guard early return). Stubs a minimal
/// Reactor <c>Component</c> in <c>Microsoft.UI.Reactor.Core</c> so <c>IsLikelyReactorHook</c>
/// anchors without the framework.
/// </summary>
public class HooksEarlyReturnGuardAnalyzerTests
{
    private const string Stubs = @"
namespace Microsoft.UI.Reactor.Core
{
    public class RenderContext { }

    public abstract class Component
    {
        protected internal RenderContext Context { get; } = new RenderContext();
        public abstract string Render();
        protected (T Value, System.Action<T> Set) UseState<T>(T initialValue, bool threadSafe = false) => (initialValue, _ => { });
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
    public async Task Hook_After_SingleGuard_EarlyReturn_Flags()
    {
        await Verify(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (a, _) = UseState(0);
        if (a < 0) return ""invalid"";
        var (b, setB) = {|REACTOR_HOOKS_002:UseState("""")|};
        return b;
    }
}");
    }

    [Fact]
    public async Task Hook_After_BracedSingleGuard_EarlyReturn_Flags()
    {
        await Verify(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (a, _) = UseState(0);
        if (a < 0)
        {
            return ""invalid"";
        }
        var (b, setB) = {|REACTOR_HOOKS_002:UseState("""")|};
        return b;
    }
}");
    }

    // Multiple stacked single-guard returns still shift later hooks — the hook after them fires.
    [Fact]
    public async Task Hook_After_Stacked_Guards_Flags()
    {
        await Verify(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (a, _) = UseState(0);
        if (a < 0) return ""neg"";
        if (a > 10) return ""big"";
        var (b, setB) = {|REACTOR_HOOKS_002:UseState("""")|};
        return b;
    }
}");
    }

    [Fact]
    public async Task No_Guard_Before_Hook_DoesNotFlag()
    {
        await Verify(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (a, _) = UseState(0);
        var (b, setB) = UseState("""");
        return b;
    }
}");
    }

    // Near-miss: the guard has an else, so it is not an unconditional early return.
    [Fact]
    public async Task Guard_With_Else_DoesNotFlag()
    {
        await Verify(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (a, _) = UseState(0);
        if (a < 0) return ""invalid""; else a = 0;
        var (b, setB) = UseState("""");
        return b;
    }
}");
    }

    // Near-miss: the guard body does more than return (two statements), so it is not the
    // single-guard shape the rule is scoped to.
    [Fact]
    public async Task Guard_With_NonReturnOnlyBody_DoesNotFlag()
    {
        await Verify(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (a, _) = UseState(0);
        if (a < 0)
        {
            a = 1;
            return ""invalid"";
        }
        var (b, setB) = UseState("""");
        return b;
    }
}");
    }

    // Near-miss: `throw` is explicitly out of scope (only `return` guards shift the slot table here).
    [Fact]
    public async Task Guard_With_Throw_DoesNotFlag()
    {
        await Verify(@"
class C : Microsoft.UI.Reactor.Core.Component
{
    public override string Render()
    {
        var (a, _) = UseState(0);
        if (a < 0) throw new System.InvalidOperationException();
        var (b, setB) = UseState("""");
        return b;
    }
}");
    }
}
