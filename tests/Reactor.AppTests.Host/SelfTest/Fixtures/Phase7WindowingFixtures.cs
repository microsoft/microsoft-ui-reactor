using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Windows.Storage;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>Spec 054 Phase 7 fixtures for title-bar inference, transparent backdrop, and picker HWND initialization.</summary>
internal static class Phase7WindowingFixtures
{
    private static void EnsureUIDispatcher()
    {
        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
        ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
    }

    private sealed class TitleBarComponent : Component
    {
        public override Element Render() => VStack(TitleBar("Phase 7"), TextBlock("body"));
    }

    private sealed class PlainComponent : Component
    {
        public override Element Render() => TextBlock("plain");
    }

    private sealed class TransparentBackdropComponent : Component
    {
        public override Element Render() => VStack(TextBlock("transparent backdrop")).Backdrop(BackdropKind.Transparent);
    }

    private sealed class PickerComponent : Component
    {
        public Func<Task<StorageFile?>>? Pick { get; private set; }

        public override Element Render()
        {
            Pick = () => UseFilePickerAsync(new FilePickerOptions());
            return TextBlock("picker");
        }
    }

    private sealed class StubPickerService : IPickerService
    {
        public nint LastHwnd { get; private set; }
        public int FileCalls { get; private set; }

        public Task<StorageFile?> PickFileAsync(nint hwnd, FilePickerOptions options)
        {
            LastHwnd = hwnd;
            FileCalls++;
            return Task.FromResult<StorageFile?>(null);
        }

        public Task<StorageFolder?> PickFolderAsync(nint hwnd, FolderPickerOptions options)
            => Task.FromResult<StorageFolder?>(null);
    }

    private static async Task<ReactorWindow> OpenAndSettle(WindowSpec spec, Func<Component> root)
    {
        var win = ReactorApp.OpenWindow(spec, root);
        await win.Host.WaitForIdleAsync();
        await Harness.Render(100);
        return win;
    }

    private static async Task CloseAndSettle(params ReactorWindow?[] windows)
    {
        foreach (var win in windows)
        {
            if (win is null) continue;
            try { win.Close(); } catch { }
        }
        await Task.Delay(100);
    }

    internal class TitleBarImplicitExtends(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "Implicit TitleBar", Width = 320, Height = 220 }, () => new TitleBarComponent());
            try { H.Check("TitleBar_ImplicitExtends", win.NativeWindow.ExtendsContentIntoTitleBar); }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class TitleBarExplicitFalseOverrides(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "Explicit False", Width = 320, Height = 220, ExtendsContentIntoTitleBar = false }, () => new TitleBarComponent());
            try { H.Check("TitleBar_ExplicitFalseOverrides", !win.NativeWindow.ExtendsContentIntoTitleBar); }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class TitleBarNoElementNullStaysFalse(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "No TitleBar", Width = 320, Height = 220 }, () => new PlainComponent());
            try { H.Check("TitleBar_NoElement_NullStaysFalse", !win.NativeWindow.ExtendsContentIntoTitleBar); }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class BackdropTransparentApply(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "Transparent Backdrop", Width = 320, Height = 220 }, () => new TransparentBackdropComponent());
            try
            {
                var backdrop = win.NativeWindow.SystemBackdrop;
                H.Check("BackdropTransparent_Apply", backdrop is null || backdrop.GetType().Name.Contains("Transparent", StringComparison.Ordinal));
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class FilePickerInitializesWithWindow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var previous = RenderContext.PickerService;
            var service = new StubPickerService();
            RenderContext.PickerService = service;
            var component = new PickerComponent();
            var win = await OpenAndSettle(new WindowSpec { Title = "Picker", Width = 320, Height = 220 }, () => component);
            try
            {
                await component.Pick!();
                var expected = WinRT.Interop.WindowNative.GetWindowHandle(win.NativeWindow);
                H.Check("FilePicker_InitializesWithWindow", service.FileCalls == 1 && service.LastHwnd == expected);
            }
            finally
            {
                RenderContext.PickerService = previous;
                await CloseAndSettle(win);
            }
        }
    }

    internal class FilePickerThrowsOffUiThread(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var previous = RenderContext.PickerService;
            RenderContext.PickerService = new StubPickerService();
            var component = new PickerComponent();
            var win = await OpenAndSettle(new WindowSpec { Title = "Picker Off Thread", Width = 320, Height = 220 }, () => component);
            try
            {
                bool threw = false;
                try { await Task.Run(() => component.Pick!()); }
                catch (InvalidOperationException) { threw = true; }
                H.Check("FilePicker_ThrowsOffUiThread", threw);
            }
            finally
            {
                RenderContext.PickerService = previous;
                await CloseAndSettle(win);
            }
        }
    }
}
