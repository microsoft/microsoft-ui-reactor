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
///
/// <para>In the SourceMapGlobals collection (spec 010): the <c>DevtoolsEnabled</c>
/// setter and <c>ResetDevtoolsEnabledForTests()</c> both write
/// <c>ReactorSourceMap.Enabled</c>, so the ctor/Dispose here would otherwise clear
/// that flag out from under <c>SourceMapElementSlotTests</c> running in parallel.</para>
/// </summary>
[Collection("SourceMapGlobals")]
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

    // ── Spec 010: the devtools flag mirrors into source mapping ───────────
    //
    // This is the activation path real users take (`--devtools app` / `--devtools
    // run`); every other source-map test sets ReactorSourceMap.Enabled directly, so
    // without these three the mirror could be deleted and the whole source-map suite
    // would stay green while devtools produced no stamps at all.

    [Fact]
    public void EnablingDevtools_TurnsSourceMappingOn()
    {
        Assert.False(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);

        ReactorApp.DevtoolsEnabled = true;

        Assert.True(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);
    }

    [Fact]
    public void DisablingDevtools_TurnsSourceMappingOff()
    {
        ReactorApp.DevtoolsEnabled = true;
        Assert.True(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);

        ReactorApp.DevtoolsEnabled = false;

        Assert.False(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);
    }

    [Fact]
    public void ResettingDevtools_TurnsSourceMappingOff()
    {
        // The reset helper writes the backing field directly, bypassing the property
        // setter that normally mirrors — so it needs its own assertion. A reset that
        // left source mapping on would leak the flag into every later test.
        ReactorApp.DevtoolsEnabled = true;
        Assert.True(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);

        ReactorApp.ResetDevtoolsEnabledForTests();

        Assert.False(global::Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);
        Assert.False(ReactorApp.DevtoolsEnabled);
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

