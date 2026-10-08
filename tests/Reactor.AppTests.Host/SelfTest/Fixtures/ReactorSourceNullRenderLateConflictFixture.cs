using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Hosting;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// Kept in its own file (as is LateNullRootControlMount): the fixture below registers a
// conflicting fingerprint for the file holding each Mount call, which makes that file's facts
// unattributable for the rest of the process.
internal static class LateNullRootHostMount
{
    internal static Action<bool>? SetShown;

    public static void Mount(ReactorHost host) => host.Mount(ctx =>
    {
        var (shown, set) = ctx.UseState(true);
        SetShown = set;
        return shown ? TextBlock("late-null-root-host") : null!;
    });
}

/// <summary>
/// A function root's <c>hooks=</c> is added by the host from the root mount site. When a late
/// registration makes that file unattributable and the root then renders null, the content it
/// keeps must lose those hooks too, as it would on a publishing pass. Both hosts.
/// </summary>
internal class ReactorSource_NullRenderDropsLateConflictRootHooks(Harness h) : SelfTestFixtureBase(h)
{
    private static WinUI.TextBlock? Find(Harness h, string text) => h.FindControl<WinUI.TextBlock>(t => t.Text == text);

    // Returns false when this host does not stamp call sites (nothing to test).
    private async Task<bool> RunPath(string name, string text, Func<Action<bool>?> setShown, Func<Task> mount, Func<Task> render, Action dispose)
    {
        await mount();
        var root = Find(H, text);
        var before = root is null ? null : ReactorDiagnostics.GetSource(root);
        Console.WriteLine($"# {name}, before: {before}");
        if (before?.Contains("|at=", StringComparison.Ordinal) != true)
        {
            dispose();
            return false;
        }
        H.Check($"{name}_RootHooksPublished", before.Contains("|root=FuncElement|hooks=0:shown@", StringComparison.Ordinal));
        var siteFile = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(root!)?.FilePath;
        if (siteFile is not null)
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.RegisterStaticInfo(
                typeof(object).Assembly, b => b.Source(siteFile, "0000000000000000"));
        setShown()!(false);
        await render();
        var after = Find(H, text) is { } kept ? ReactorDiagnostics.GetSource(kept) : null;
        Console.WriteLine($"# {name}, after a late conflict and a null render: {after}");
        H.Check($"{name}_RetainedRootDropsHostHooks",
            siteFile is not null && after is not null
            && after.Contains("|root=FuncElement", StringComparison.Ordinal)
            && !after.Contains("|hooks=", StringComparison.Ordinal));
        dispose();
        return true;
    }

    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_NullRenderLateConflict", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var previous = ReactorSourcePublisher.IsEnabled;
        try
        {
            ReactorSourcePublisher.IsEnabled = true;

            ReactorHost? host = null;
            bool stamped = await RunPath("ReactorSource_NullRenderLateConflict_Host", "late-null-root-host",
                () => LateNullRootHostMount.SetShown,
                mount: async () =>
                {
                    host = H.CreateHost();
                    LateNullRootHostMount.Mount(host);
                    await Harness.Render();
                },
                render: async () => { await Harness.Render(); await Harness.Render(); },
                dispose: () => { host?.Dispose(); H.SetContent(null); });
            if (!stamped)
            {
                H.Skip("ReactorSource_NullRenderLateConflict", "call sites are not stamped in this host");
                return;
            }

            ReactorHostControl? control = null;
            await RunPath("ReactorSource_NullRenderLateConflict_HostControl", "late-null-root-control",
                () => LateNullRootControlMount.SetShown,
                mount: async () =>
                {
                    control = new ReactorHostControl();
                    LateNullRootControlMount.Mount(control);
                    H.SetContent(new WinUI.Border { Child = control });
                    // A standalone ReactorHostControl is not ReactorApp.ActiveHost: poll its loop.
                    await Harness.WaitFor(() => Find(H, "late-null-root-control") is not null, maxPasses: 32, perPassMs: 10);
                },
                render: async () =>
                {
                    await Harness.WaitFor(
                        () => Find(H, "late-null-root-control") is { } tb
                            && ReactorDiagnostics.GetSource(tb) is { } v && !v.Contains("|hooks=", StringComparison.Ordinal),
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
