using Microsoft.UI.Reactor.Analyzers;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using System.Threading.Tasks;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.AnalyzerTests;

/// <summary>
/// Tests for <see cref="StaticNavigationHandleAnalyzer"/> (<c>REACTOR_NAV_001</c>).
/// Stubs a minimal Reactor-shaped <c>NavigationHandle&lt;TRoute&gt;</c> (in the real
/// <c>Microsoft.UI.Reactor.Navigation</c> namespace the analyzer keys on) plus a
/// <c>Component</c> base exposing <c>UseNavigation</c>, so the pure symbol gate fires
/// without pulling the framework in. The analyzer flags a <c>static</c> field or property
/// typed <c>NavigationHandle&lt;&gt;</c> regardless of how the value flows in — the handle's
/// constructor is <c>internal</c>, so consumer code can only get one from
/// <c>UseNavigation</c>.
/// </summary>
public class StaticNavigationHandleAnalyzerTests
{
    private const string Stubs = @"
#nullable enable
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;

namespace Microsoft.UI.Reactor.Navigation
{
    // Mirrors src/Reactor/Core/Navigation/NavigationHandle.cs — a sealed generic
    // handle in this exact namespace (name + namespace + arity are the gate). Its
    // constructor is internal (as in the framework), so consumer code can only obtain
    // one from UseNavigation — which is why static storage of this type is the leak.
    public sealed class NavigationHandle<TRoute> where TRoute : notnull
    {
        internal NavigationHandle() { }
    }
}

namespace Microsoft.UI.Reactor.Core
{
    using Microsoft.UI.Reactor.Navigation;

    // Mirrors the protected Component.UseNavigation wrappers (Component.cs:242/245).
    public abstract class Component
    {
        protected NavigationHandle<TRoute> UseNavigation<TRoute>(TRoute initial) where TRoute : notnull
            => new NavigationHandle<TRoute>();
        protected NavigationHandle<TRoute> UseNavigation<TRoute>() where TRoute : notnull
            => new NavigationHandle<TRoute>();
    }
}

public enum Route { Home, Settings }
";

    // ── Positive: static NavigationHandle<> field assigned from UseNavigation ──

    [Fact]
    public async Task Fires_For_Static_Field_Assigned_Via_Local()
    {
        // Canonical doc pitfall (navigation.md:745-759): the handle flows through an
        // intermediate local before landing in the static field.
        var source = Stubs + @"
class Shell : Component
{
    public static NavigationHandle<Route>? {|REACTOR_NAV_001:Nav|};

    public void Render()
    {
        var nav = UseNavigation(Route.Home);
        Nav = nav;
    }
}";

        await new CSharpAnalyzerTest<StaticNavigationHandleAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Static_Field_Assigned_Directly()
    {
        var source = Stubs + @"
class Shell : Component
{
    public static NavigationHandle<Route>? {|REACTOR_NAV_001:Nav|};

    public void Render()
    {
        Nav = UseNavigation(Route.Home);
    }
}";

        await new CSharpAnalyzerTest<StaticNavigationHandleAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Private_Static_Field()
    {
        // Accessibility is irrelevant — any static storage of the handle leaks.
        var source = Stubs + @"
class Shell : Component
{
    private static NavigationHandle<Route>? {|REACTOR_NAV_001:_nav|};

    public void Render() => _nav = UseNavigation(Route.Home);
}";

        await new CSharpAnalyzerTest<StaticNavigationHandleAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fires_For_Static_AutoProperty()
    {
        // A static auto-property holds the handle for the same static lifetime as a
        // field; its backing field is implicitly declared, so it's reported here once.
        var source = Stubs + @"
class Shell : Component
{
    public static NavigationHandle<Route>? {|REACTOR_NAV_001:Nav|} { get; set; }

    public void Render() => Nav = UseNavigation(Route.Home);
}";

        await new CSharpAnalyzerTest<StaticNavigationHandleAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Negative: instance field / property / local assigned from UseNavigation ──

    [Fact]
    public async Task No_Diagnostic_For_Instance_Field()
    {
        var source = Stubs + @"
class Shell : Component
{
    public NavigationHandle<Route>? Nav;

    public void Render()
    {
        var nav = UseNavigation(Route.Home);
        Nav = nav;
    }
}";

        await new CSharpAnalyzerTest<StaticNavigationHandleAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Instance_AutoProperty()
    {
        var source = Stubs + @"
class Shell : Component
{
    public NavigationHandle<Route>? Nav { get; set; }

    public void Render() => Nav = UseNavigation(Route.Home);
}";

        await new CSharpAnalyzerTest<StaticNavigationHandleAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Local()
    {
        var source = Stubs + @"
class Shell : Component
{
    public void Render()
    {
        var nav = UseNavigation(Route.Home);
        _ = nav;
    }
}";

        await new CSharpAnalyzerTest<StaticNavigationHandleAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    // ── Near-miss: a static field of a different type ──

    [Fact]
    public async Task No_Diagnostic_For_Static_Field_Of_Different_Type()
    {
        var source = Stubs + @"
class Shell : Component
{
    public static string? Nav;
}";

        await new CSharpAnalyzerTest<StaticNavigationHandleAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task No_Diagnostic_For_Same_Named_Type_In_Different_Namespace()
    {
        // A NavigationHandle<> from a foreign namespace is not the Reactor handle.
        var source = Stubs + @"
namespace Other
{
    public sealed class NavigationHandle<TRoute> where TRoute : notnull { }
}

class Shell
{
    public static Other.NavigationHandle<Route>? Nav;
}";

        await new CSharpAnalyzerTest<StaticNavigationHandleAnalyzer, DefaultVerifier>
        {
            TestCode = source,
        }.RunAsync(TestContext.Current.CancellationToken);
    }
}
