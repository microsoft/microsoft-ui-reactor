using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// Its own file: ReactorSource_StaleFactsDropped makes ITS file's facts unattributable for the
// rest of the process, which must not decide whether this fixture's name= is published.
internal static class NullRenderFactsView
{
    public static Element Body()
    {
        var nullTitle = TextBlock("null-render-title");
        return VStack(nullTitle);
    }
}

/// <summary>
/// A root that re-renders to null keeps its content, and nothing re-publishes it. Static facts
/// that went stale in the meantime (here: a hot-reload update) must still be dropped from that
/// retained content, in both hosts.
/// </summary>
internal class ReactorSource_NullRenderDropsStaleFacts(Harness h) : SelfTestFixtureBase(h)
{
    private static Action<bool>? s_setShown;

    private static Element? Root(RenderContext ctx)
    {
        var (shown, set) = ctx.UseState(true);
        s_setShown = set;
        return shown ? NullRenderFactsView.Body() : null;
    }

    private string? Title()
        => H.FindControl<WinUI.TextBlock>(t => t.Text == "null-render-title") is { } tb ? ReactorDiagnostics.GetSource(tb) : null;

    // Returns false when this host does not stamp call sites (nothing to test).
    private async Task<bool> RunPath(string name, Func<Task> mount, Func<Task> render, Action dispose)
    {
        await mount();
        var before = Title();
        Console.WriteLine($"# {name}, before: {before}");
        if (before?.Contains("|at=", StringComparison.Ordinal) != true)
        {
            dispose();
            return false;
        }
        H.Check($"{name}_NamePublished", before.Contains("|name=nullTitle", StringComparison.Ordinal));
        try
        {
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.InvalidateStaticFactsForHotReload();
            s_setShown!(false);
            await render();
            var after = Title();
            Console.WriteLine($"# {name}, after a null render: {after}");
            H.Check($"{name}_RetainedContentDropsStaleName",
                after is not null && !after.Contains("|name=", StringComparison.Ordinal));
        }
        finally
        {
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.ResetHotReloadInvalidationForTests();
            dispose();
        }
        return true;
    }

    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_NullRenderDropsStaleFacts", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var previous = ReactorSourcePublisher.IsEnabled;
        try
        {
            ReactorSourcePublisher.IsEnabled = true;

            Microsoft.UI.Reactor.Hosting.ReactorHost? host = null;
            bool stamped = await RunPath("ReactorSource_NullRender_Host",
                mount: async () =>
                {
                    host = H.CreateHost();
                    host.Mount(ctx => Root(ctx)!);
                    await Harness.Render();
                },
                render: async () => { await Harness.Render(); await Harness.Render(); },
                dispose: () => { host?.Dispose(); H.SetContent(null); });
            if (!stamped)
            {
                H.Skip("ReactorSource_NullRenderDropsStaleFacts", "call sites are not stamped in this host");
                return;
            }

            Microsoft.UI.Reactor.Hosting.ReactorHostControl? control = null;
            await RunPath("ReactorSource_NullRender_HostControl",
                mount: async () =>
                {
                    control = new Microsoft.UI.Reactor.Hosting.ReactorHostControl();
                    control.Mount(ctx => Root(ctx)!);
                    H.SetContent(new WinUI.Border { Child = control });
                    // A standalone ReactorHostControl is not ReactorApp.ActiveHost: poll its loop.
                    await Harness.WaitFor(() => Title() is not null, maxPasses: 32, perPassMs: 10);
                },
                render: async () =>
                {
                    await Harness.WaitFor(() => Title() is { } v && !v.Contains("|name=", StringComparison.Ordinal),
                        maxPasses: 32, perPassMs: 10);
                    await Harness.Render();
                },
                dispose: () => { control?.Dispose(); H.SetContent(null); });
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
        }
    }
}
