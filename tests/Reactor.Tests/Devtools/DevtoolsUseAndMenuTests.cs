using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Spec 028 — <c>UseDevtools()</c> reflects the session-scoped
/// <see cref="ReactorApp.DevtoolsEnabled"/> flag; the <c>DevtoolsMenu</c>
/// factory renders to <c>Empty</c> (and skips the items lambda) when the
/// flag is off so retail builds pay only the bool check.
/// </summary>
public class DevtoolsUseAndMenuTests : IDisposable
{
    public DevtoolsUseAndMenuTests() => ReactorApp.ResetDevtoolsEnabledForTests();
    public void Dispose() => ReactorApp.ResetDevtoolsEnabledForTests();

    [Fact]
    public void UseDevtools_ReturnsFalse_WhenFlagOff()
    {
        ReactorApp.DevtoolsEnabled = false;
        var ctx = new RenderContext();
        Assert.False(ctx.UseDevtools());
    }

    [Fact]
    public void UseDevtools_ReturnsTrue_WhenFlagOn()
    {
        ReactorApp.DevtoolsEnabled = true;
        var ctx = new RenderContext();
        Assert.True(ctx.UseDevtools());
    }

    [Fact]
    public void DevtoolsMenu_RendersEmpty_WhenDisabled()
    {
        ReactorApp.DevtoolsEnabled = false;

        var el = DevtoolsMenu(() => new MenuFlyoutItemBase[] { MenuItem("x") });

        // Empty() returns EmptyElement.Instance; compare to verify the early-out.
        Assert.Same(Empty(), el);
    }

    [Fact]
    public void DevtoolsMenu_DoesNotInvokeItemsLambda_WhenDisabled()
    {
        ReactorApp.DevtoolsEnabled = false;
        var invoked = 0;

        _ = DevtoolsMenu(() =>
        {
            invoked++;
            return new MenuFlyoutItemBase[] { MenuItem("x") };
        });

        Assert.Equal(0, invoked);
    }

    // The enabled-path (materialize items + build the Button+MenuFlyout) uses
    // fluent modifiers like .Foreground(string) that eagerly construct
    // WinUI brushes — valid during Render() in a running app, but not reachable
    // from a headless xUnit context without spinning up XAML. That path is
    // covered by Reactor.AppTests (real WinUI window) and by manual runs of
    // Reactor.TestApp with `--devtools app`. Don't reintroduce an enabled-path
    // unit test here without a WinUI harness — it will flake on COMException.
}
