using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

// Kept in its own file on purpose: ReactorSource_StaleFactsDropped makes THIS file's facts
// unattributable for the rest of the process (a late conflicting registration cannot be
// undone), which must not affect the other ReactorSource fixtures.
internal static class LateFactsView
{
    public static Element Body()
    {
        var lateTitle = TextBlock("late-title");
        return Memo(ctx =>
        {
            var (count, _) = ctx.UseState(0);
            return VStack(lateTitle, TextBlock($"late-count {count}"));
        });
    }
}

/// <summary>
/// Static facts already published (<c>name=</c>, <c>hooks=</c>) must not outlive the table
/// they came from. Controls that are not re-published (unchanged call site, key and kind)
/// still lose stale facts on the host's next pass: after a source-mapped assembly registers a
/// conflicting fingerprint for their file later, and after a hot-reload update.
/// </summary>
internal class ReactorSource_StaleFactsDropped(Harness h) : SelfTestFixtureBase(h)
{
    private string? Source(string text)
        => H.FindControl<WinUI.TextBlock>(t => t.Text == text) is { } tb ? ReactorDiagnostics.GetSource(tb) : null;

    private string? MemoWrapperSource()
    {
        var tb = H.FindControl<WinUI.TextBlock>(t => t.Text == "late-title");
        for (DependencyObject? d = tb; d is not null; d = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(d))
        {
            if (ReactorDiagnostics.GetSource(d) is { } v && v.Contains("|mounts=MemoElement", StringComparison.Ordinal))
                return v;
        }
        return null;
    }

    public override async Task RunAsync()
    {
        if (!ReactorSourcePublisher.IsSupported)
        {
            H.Skip("ReactorSource_StaleFacts", "Reactor.DevtoolsSupport is off in this host");
            return;
        }

        var previous = ReactorSourcePublisher.IsEnabled;
        try
        {
            ReactorSourcePublisher.IsEnabled = true;

            // ── Hot reload: every name and hook list becomes unknown. ──
            var hrHost = H.CreateHost();
            hrHost.Mount(_ => LateFactsView.Body());
            await Harness.Render();
            var nameBefore = Source("late-title");
            var hooksBefore = MemoWrapperSource();
            Console.WriteLine($"# stale facts, before: {nameBefore} / {hooksBefore}");
            if (nameBefore?.Contains("|name=lateTitle", StringComparison.Ordinal) != true
                || hooksBefore?.Contains("|hooks=0:count@", StringComparison.Ordinal) != true)
            {
                H.Skip("ReactorSource_StaleFacts", "this host has no source-map static facts");
                hrHost.Dispose();
                H.SetContent(null);
                return;
            }

            try
            {
                Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.InvalidateStaticFactsForHotReload();
                hrHost.RequestRender(force: true);
                await Harness.Render();
                var nameAfter = Source("late-title");
                var hooksAfter = MemoWrapperSource();
                Console.WriteLine($"# stale facts, after hot reload: {nameAfter} / {hooksAfter}");
                H.Check("ReactorSource_StaleFacts_HotReloadDropsNamesAndHooks",
                    nameAfter is not null && !nameAfter.Contains("|name=", StringComparison.Ordinal)
                    && hooksAfter is not null && !hooksAfter.Contains("|hooks=", StringComparison.Ordinal));
            }
            finally
            {
                Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.ResetHotReloadInvalidationForTests();
            }
            hrHost.Dispose();
            H.SetContent(null);

            // ── A later registration makes this file unattributable. ──
            var host = H.CreateHost();
            host.Mount(_ => LateFactsView.Body());
            await Harness.Render();
            var fresh = Source("late-title");
            var siteFile = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(
                H.FindControl<WinUI.TextBlock>(t => t.Text == "late-title")!)?.FilePath;
            H.Check("ReactorSource_StaleFacts_RepublishedAfterReset",
                fresh?.Contains("|name=lateTitle", StringComparison.Ordinal) == true && siteFile is not null, fresh);
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.RegisterStaticInfo(
                typeof(object).Assembly, b => b.Source(siteFile!, "0000000000000000"));
            host.RequestRender(force: true);
            await Harness.Render();
            var nameLate = Source("late-title");
            var hooksLate = MemoWrapperSource();
            Console.WriteLine($"# stale facts, after a late conflict: {nameLate} / {hooksLate}");
            H.Check("ReactorSource_StaleFacts_LateConflictDropsLocationFacts",
                nameLate is not null && !nameLate.Contains("|name=", StringComparison.Ordinal)
                && hooksLate is not null && !hooksLate.Contains("|hooks=", StringComparison.Ordinal));
            host.Dispose();
            H.SetContent(null);
        }
        finally
        {
            ReactorSourcePublisher.IsEnabled = previous;
        }
    }
}
