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

    // Only called in a host that stamps call sites (see RunAsync), so a missing at= or hooks=
    // is a publication failure.
    private async Task RunPath(string name, string text, Func<Action<bool>?> setShown, Func<Task> mount, Func<Task> render, Action dispose)
    {
        await mount();
        var root = Find(H, text);
        var before = root is null ? null : ReactorDiagnostics.GetSource(root);
        Console.WriteLine($"# {name}, before: {before}");
        bool published = before?.Contains("|at=", StringComparison.Ordinal) == true
            && before.Contains("|root=FuncElement|hooks=0:shown@", StringComparison.Ordinal);
        H.Check($"{name}_RootHooksPublished", published);
        if (!published)
        {
            dispose();
            return;
        }
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
    }

    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_NullRenderLateConflict", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var (previous, mapped) = (ReactorSourcePublisher.IsEnabled, Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled);
        try
        {
            ReactorSourcePublisher.IsEnabled = true;
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = true;
            // Decided independently of the published values under test.
            if (TextBlock("late-null-stamp-probe").CallSite is null)
            {
                H.Skip("ReactorSource_NullRenderLateConflict", "call sites are not stamped in this host");
                return;
            }

            ReactorHost? host = null;
            await RunPath("ReactorSource_NullRenderLateConflict_Host", "late-null-root-host",
                () => LateNullRootHostMount.SetShown,
                mount: async () =>
                {
                    host = H.CreateHost();
                    LateNullRootHostMount.Mount(host);
                    await Harness.Render();
                },
                render: async () => { await Harness.Render(); await Harness.Render(); },
                dispose: () => { host?.Dispose(); H.SetContent(null); });

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
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = mapped;
        }
    }
}
