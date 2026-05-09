using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Spec 036 §3.4 / §4.4 — two windows of the same component class get
/// distinct keyed values when storing through <see cref="PersistedScope.Window"/>.
/// We can exercise the scope directly without spinning windows: the scope
/// type is the same instance <see cref="ReactorWindow.PersistedScope"/>
/// surfaces, so isolation is a property of the scope object itself.
/// </summary>
public class WindowPersistedScopeIsolationTests
{
    [Fact]
    public void Two_Window_Scopes_Hold_Independent_Values_For_Same_Key()
    {
        var a = new WindowPersistedScope();
        var b = new WindowPersistedScope();

        a.Set("counter", 1);
        b.Set("counter", 99);

        Assert.True(a.TryGet<int>("counter", out var av));
        Assert.True(b.TryGet<int>("counter", out var bv));
        Assert.Equal(1, av);
        Assert.Equal(99, bv);
    }

    [Fact]
    public void Disposing_One_Scope_Does_Not_Affect_The_Other()
    {
        var a = new WindowPersistedScope();
        var b = new WindowPersistedScope();
        a.Set("k", "alpha");
        b.Set("k", "beta");

        a.Dispose();

        Assert.False(a.TryGet<string>("k", out _));
        Assert.True(b.TryGet<string>("k", out var bv));
        Assert.Equal("beta", bv);
    }

    [Fact]
    public void Application_Scope_Is_Process_Wide_And_Distinct_From_Window_Scope()
    {
        // Storing through ApplicationPersistedScope.Default must not appear
        // in a fresh WindowPersistedScope, and vice versa. Spec 033 §2.
        var win = new WindowPersistedScope();
        var key = $"isolation-test-{Guid.NewGuid():N}";
        try
        {
            ApplicationPersistedScope.Default.Set(key, "app-value");
            win.Set(key, "win-value");

            Assert.True(ApplicationPersistedScope.Default.TryGet<string>(key, out var av));
            Assert.True(win.TryGet<string>(key, out var wv));
            Assert.Equal("app-value", av);
            Assert.Equal("win-value", wv);
        }
        finally
        {
            ApplicationPersistedScope.Default.Remove(key);
            win.Dispose();
        }
    }
}
