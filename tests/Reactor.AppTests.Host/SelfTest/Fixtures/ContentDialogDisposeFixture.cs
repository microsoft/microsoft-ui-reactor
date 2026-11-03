using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal sealed class ContentDialogDisposeFixture(Harness h) : SelfTestFixtureBase(h)
{
    private sealed class Probe
    {
        internal int RootEffects, ChildEffects, RootCleanups, ChildCleanups;
        internal int Opened, Closed, FrameworkClosed;
    }

    private sealed class Child : Component<Probe>
    {
        public override Element Render()
        {
            var probe = Props;
            UseEffect(() =>
            {
                probe.ChildEffects++;
                return () => probe.ChildCleanups++;
            }, []);
            return TextBlock("Dispose dialog content");
        }
    }

    public override async Task RunAsync()
    {
        const string title = "Host disposal dialog";
        var probe = new Probe();
        using var host = new ReactorHostControl();
        var container = new WinUI.Grid();
        container.Children.Add(host);
        H.SetContent(container);
        WinUI.ContentDialog? dialog = null;
        try
        {
            host.Mount(ctx =>
            {
                ctx.UseEffect(() =>
                {
                    probe.RootEffects++;
                    return () => probe.RootCleanups++;
                }, []);
                return (ContentDialog(title, Component<Child, Probe>(probe), "OK") with
                {
                    IsOpen = true,
                    OnClosed = _ => probe.FrameworkClosed++,
                }).Set(native =>
                {
                    if (ReferenceEquals(dialog, native)) return;
                    dialog = native;
                    native.Opened += (_, _) => probe.Opened++;
                    native.Closed += (_, _) => probe.Closed++;
                });
            });
            var ready = await Harness.WaitFor(() =>
                probe.Opened == 1 && probe.RootEffects == 1 && probe.ChildEffects == 1
                && dialog is { IsLoaded: true } && ContentDialogProbe.FindOpen(H, title) is not null);
            H.Check("DialogDispose_OpenedAndEffectsCommitted", ready);
            if (!ready) throw new InvalidOperationException("Native dialog/effect readiness failed.");
            var native = dialog!;
            var root = host.XamlRoot;
            H.Check("DialogDispose_NoPrematureCleanup",
                probe.RootCleanups == 0 && probe.ChildCleanups == 0);

            host.Dispose();
            await Harness.WaitFor(() => probe.Closed == 1 && !native.IsLoaded);
            var popups = VisualTreeHelper.GetOpenPopupsForXamlRoot(root).Count;
            Console.WriteLine($"# DialogDispose before rescue: opened={probe.Opened}; closed={probe.Closed}; " +
                $"loaded={native.IsLoaded}; contentAttached={native.Content is not null}; popups={popups}; " +
                $"rootCleanups={probe.RootCleanups}; childCleanups={probe.ChildCleanups}; frameworkClosed={probe.FrameworkClosed}");
            H.Check("DialogDispose_NativeClosedOnce", probe.Closed == 1);
            H.Check("DialogDispose_NativeUnloaded", !native.IsLoaded);
            H.Check("DialogDispose_ContentDetached", native.Content is null);
            H.Check("DialogDispose_NoOpenPopup", popups == 0);
            H.Check("DialogDispose_CleanupsOnce", probe.RootCleanups == 1 && probe.ChildCleanups == 1);
            H.Check("DialogDispose_NoFrameworkClose", probe.FrameworkClosed == 0);
            H.Check("DialogDispose_WindowStillAlive", ReferenceEquals(container.XamlRoot, root) && root.Content is not null);
            host.Dispose();
            await Harness.Render();
            H.Check("DialogDispose_Idempotent", probe.RootCleanups == 1 && probe.ChildCleanups == 1);
        }
        finally
        {
            // Rescue only after recording the natural disposal result, so the baseline
            // failure cannot strand a modal dialog over subsequent fixtures.
            if (dialog is not null)
            {
                dialog.Hide();
                H.Check("DialogDispose_RescueClosed", await ContentDialogProbe.WaitForClosed(H, title));
            }
            container.Children.Remove(host);
            H.SetContent(null);
        }
    }
}
