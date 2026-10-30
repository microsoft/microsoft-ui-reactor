using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ContentDialogDisposeEdgeFixtures
{
    private sealed class EffectProbe
    {
        internal int Effects;
        internal int Cleanups;
    }

    private sealed class EffectfulChild : Component<EffectProbe>
    {
        public override Element Render()
        {
            var probe = Props;
            UseEffect(() =>
            {
                probe.Effects++;
                return () => probe.Cleanups++;
            }, []);
            return TextBlock("effectful child");
        }
    }

    internal sealed class PendingShow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var reconciler = new Reconciler();
            var opened = 0;
            var closed = 0;
            WinUI.ContentDialog? dialog = null;
            var element = (ContentDialog("Disposed pending show", TextBlock("content"), "OK") with
            {
                OnOpened = () => opened++,
                OnClosed = _ => closed++,
            }).Set(native => dialog = native);
            var anchor = (FrameworkElement)reconciler.Mount(element, () => { })!;
            H.SetContent(anchor);
            try
            {
                H.Check("DialogDispose_PendingShowAnchorLoaded", await Harness.WaitFor(() => anchor.IsLoaded));
                reconciler.Reconcile(element, element with { IsOpen = true }, anchor, () => { });
                H.Check("DialogDispose_PendingShowCreated", dialog is not null && opened == 0);
                reconciler.Dispose();
                await Harness.Render(100);
                H.Check("DialogDispose_PendingShowCloses", await ContentDialogProbe.WaitForNoneOpen(H));
                H.Check("DialogDispose_PendingShowDetached", dialog is { Content: null, IsLoaded: false });
                H.Check("DialogDispose_PendingShowNoCallbacks", opened == 0 && closed == 0);
            }
            finally
            {
                dialog?.Hide();
                await ContentDialogProbe.WaitForNoneOpen(H);
                H.SetContent(null);
            }
        }
    }

    internal sealed class Closing(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = new ReactorHostControl();
            Action<bool>? setOpen = null;
            var opened = false;
            var closed = 0;
            WinUI.ContentDialogClosingDeferral? deferral = null;
            var completed = false;
            H.SetContent(host);
            host.Mount(ctx =>
            {
                var (open, set) = ctx.UseState(true);
                setOpen = set;
                return ContentDialog("Disposed while closing", TextBlock("content"), "OK") with
                {
                    IsOpen = open,
                    OnOpened = () => opened = true,
                    OnClosed = _ => closed++,
                };
            });
            var dialog = await ContentDialogProbe.WaitForOpen(H, "Disposed while closing")
                ?? throw new InvalidOperationException("Closing dialog did not open.");
            dialog.Closing += (_, args) => deferral ??= args.GetDeferral();
            try
            {
                if (!await Harness.WaitFor(() => opened))
                    throw new InvalidOperationException("Dialog did not finish opening before the closing probe.");
                setOpen!(false);
                var closing = await Harness.WaitFor(() => deferral is not null);
                H.Check("DialogDispose_ClosingDeferralReached", closing);
                if (!closing) throw new InvalidOperationException("Native closing deferral was not reached.");
                host.Dispose();
                H.Check("DialogDispose_ClosingDetachesImmediately", dialog.Content is null);
                deferral!.Complete();
                completed = true;
                H.Check("DialogDispose_ClosingCompletes", await ContentDialogProbe.WaitForClosed(H, "Disposed while closing"));
                H.Check("DialogDispose_ClosingNoCallback", closed == 0);
            }
            finally
            {
                if (!completed) deferral?.Complete();
                dialog.Hide();
                await ContentDialogProbe.WaitForNoneOpen(H);
                H.SetContent(null);
            }
        }
    }

    internal sealed class Deferred(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var reconciler = new Reconciler();
            var shows = 0;
            var anchor = (FrameworkElement)reconciler.Mount(
                (ContentDialog("Disposed deferred", TextBlock("content"), "OK") with { IsOpen = true })
                    .Set(_ => shows++), () => { })!;
            H.Check("DialogDispose_DeferredWasArmed", reconciler.ContentDialogLifetimes?.Count == 1 && anchor.XamlRoot is null);
            reconciler.Dispose();
            H.Check("DialogDispose_DeferredRetired", reconciler.ContentDialogLifetimes is null);
            H.SetContent(anchor);
            try
            {
                H.Check("DialogDispose_DeferredAnchorLoaded", await Harness.WaitFor(() => anchor.IsLoaded));
                H.Check("DialogDispose_DeferredNeverShows", shows == 0 && ContentDialogProbe.FindAnyOpen(H) is null);
            }
            finally { H.SetContent(null); }
        }
    }

    internal sealed class DeferredUnmount(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var reconciler = new Reconciler();
            var shows = 0;
            var anchor = (FrameworkElement)reconciler.Mount(
                (ContentDialog("Unmounted deferred", TextBlock("content"), "OK") with { IsOpen = true })
                    .Set(_ => shows++), () => { })!;
            H.Check("DialogDispose_UnmountPendingArmed", reconciler.ContentDialogLifetimes?.Count == 1);
            reconciler.UnmountChild(anchor);
            H.Check("DialogDispose_UnmountReleasesPending", reconciler.ContentDialogLifetimes?.Count == 0);
            H.SetContent(anchor);
            try
            {
                H.Check("DialogDispose_UnmountedAnchorLoaded", await Harness.WaitFor(() => anchor.IsLoaded));
                H.Check("DialogDispose_UnmountedNeverShows", shows == 0 && ContentDialogProbe.FindAnyOpen(H) is null);
            }
            finally { H.SetContent(null); }
        }
    }

    internal sealed class WindowHost(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            var closed = 0;
            host.Mount(_ => ContentDialog("Disposed window host", TextBlock("content"), "OK") with
            {
                IsOpen = true,
                OnClosed = _ => closed++,
            });
            var dialog = await ContentDialogProbe.WaitForOpen(H, "Disposed window host")
                ?? throw new InvalidOperationException("Window host dialog did not open.");
            try
            {
                host.Dispose();
                H.Check("DialogDispose_WindowHostCloses", await ContentDialogProbe.WaitForClosed(H, "Disposed window host"));
                H.Check("DialogDispose_WindowHostDetaches", dialog.Content is null && closed == 0);
                H.Check("DialogDispose_WindowHostKeepsWindow", H.Window.Content is not null);
            }
            finally
            {
                dialog.Hide();
                await ContentDialogProbe.WaitForNoneOpen(H);
            }
        }
    }

    internal sealed class ThrowingRoot(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = new ReactorHostControl();
            var expected = new InvalidOperationException("root cleanup failure");
            var committed = false;
            var closed = 0;
            H.SetContent(host);
            host.Mount(ctx =>
            {
                ctx.UseEffect(() =>
                {
                    committed = true;
                    return () => throw expected;
                }, []);
                return ContentDialog("Throwing disposal", TextBlock("content"), "OK") with
                {
                    IsOpen = true,
                    OnClosed = _ => closed++,
                };
            });
            var dialog = await ContentDialogProbe.WaitForOpen(H, "Throwing disposal")
                ?? throw new InvalidOperationException("Throwing-root dialog did not open.");
            try
            {
                H.Check("DialogDispose_ThrowingRootCommitted", await Harness.WaitFor(() => committed));
                Exception? caught = null;
                try { host.Dispose(); }
                catch (InvalidOperationException ex) { caught = ex; }
                H.Check("DialogDispose_RootExceptionPreserved", ReferenceEquals(caught, expected));
                H.Check("DialogDispose_ThrowingRootCloses", await ContentDialogProbe.WaitForClosed(H, "Throwing disposal"));
                H.Check("DialogDispose_ThrowingRootDetaches", dialog.Content is null && closed == 0);
            }
            finally
            {
                dialog.Hide();
                await ContentDialogProbe.WaitForNoneOpen(H);
                H.SetContent(null);
            }
        }
    }

    internal sealed class BeforeShow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = new ReactorHostControl();
            var setters = 0;
            var opened = 0;
            WinUI.ContentDialog? dialog = null;
            H.SetContent(host);
            host.Mount(_ => (ContentDialog("Disposed before show", TextBlock("content"), "OK") with
            {
                IsOpen = true,
                OnOpened = () => opened++,
            }).Set(native =>
            {
                dialog = native;
                setters++;
                host.Dispose();
            }));
            try
            {
                H.Check("DialogDispose_BeforeShowSetterRan", await Harness.WaitFor(() => setters == 1));
                await Harness.Render();
                H.Check("DialogDispose_BeforeShowNeverOpened", opened == 0 && ContentDialogProbe.FindAnyOpen(H) is null);
                H.Check("DialogDispose_BeforeShowDetached", dialog is { Content: null });
            }
            finally
            {
                dialog?.Hide();
                await ContentDialogProbe.WaitForNoneOpen(H);
                H.SetContent(null);
            }
        }
    }

    internal sealed class MountSiblingAfterDisposal(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = new ReactorHostControl();
            var probe = new EffectProbe();
            WinUI.ContentDialog? dialog = null;
            H.SetContent(host);
            host.Mount(_ =>
                (ContentDialog(
                    "Dispose during content mount",
                    VStack(
                        TextBlock("dispose first").Set(_ => host.Dispose()),
                        Component<EffectfulChild, EffectProbe>(probe)),
                    "OK") with
                { IsOpen = true })
                .Set(native => dialog = native));

            try
            {
                await Harness.Render();
                H.Check("DialogDispose_MountSiblingMountedAfterDisposal", probe.Effects == 1);
                H.Check("DialogDispose_MountSiblingCleanupAfterDisposal", probe.Cleanups == 1);
                H.Check("DialogDispose_MountSiblingNotReattached", dialog is { Content: null });
                H.Check("DialogDispose_MountSiblingNeverOpened", ContentDialogProbe.FindAnyOpen(H) is null);
            }
            finally
            {
                dialog?.Hide();
                await ContentDialogProbe.WaitForNoneOpen(H);
                H.SetContent(null);
            }
        }
    }

    internal sealed class UpdateReplacementAfterDisposal(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const string title = "Dispose during content update";
            using var host = new ReactorHostControl();
            var probe = new EffectProbe();
            Action<bool>? setReplacement = null;
            WinUI.ContentDialog? dialog = null;
            H.SetContent(host);
            host.Mount(ctx =>
            {
                var (replace, set) = ctx.UseState(false);
                setReplacement = set;
                var content = replace
                    ? (Element)Border(Component<EffectfulChild, EffectProbe>(probe))
                        .Set(_ => host.Dispose())
                    : TextBlock("original content");
                return (ContentDialog(title, content, "OK") with { IsOpen = true })
                    .Set(native => dialog = native);
            });

            try
            {
                var opened = await ContentDialogProbe.WaitForOpen(H, title) is not null;
                H.Check("DialogDispose_UpdateReplacementOpened", opened);
                if (!opened) return;

                setReplacement!(true);
                await Harness.Render();
                var closed = await ContentDialogProbe.WaitForClosed(H, title);
                H.Check("DialogDispose_UpdateReplacementClosed", closed);
                H.Check("DialogDispose_UpdateReplacementNotReattached", dialog is { Content: null });
                H.Check("DialogDispose_UpdateReplacementEffectMounted", probe.Effects == 1);
                H.Check("DialogDispose_UpdateReplacementCleanupOnce", probe.Cleanups == 1);
            }
            finally
            {
                dialog?.Hide();
                await ContentDialogProbe.WaitForNoneOpen(H);
                H.SetContent(null);
            }
        }
    }

}
