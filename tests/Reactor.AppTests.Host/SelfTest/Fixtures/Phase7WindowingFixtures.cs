using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
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

    private sealed class Spec054HooksComponent : Component
    {
        public Button? FileButton { get; private set; }
        public Button? FolderButton { get; private set; }
        public (double X, double Y) Position { get; private set; }
        public int DisplaysCount { get; private set; }
        public bool Covered { get; private set; }
        public Action? Drag { get; private set; }
        public Action? TriggerRender { get; private set; }
        public Action? UnmountAspect { get; private set; }
        public int RenderCount { get; private set; }

        public override Element Render()
        {
            var position = Context.UseWindowPosition();
            var displays = Context.UseDisplays();
            var covered = Context.UseIsCovered();
            var drag = Context.UseWindowDragMove();
            var (count, setCount) = UseState(0);
            var (aspectMounted, setAspectMounted) = UseState(true);

            Position = position;
            DisplaysCount = displays.Count;
            Covered = covered;
            Drag = drag;
            TriggerRender = () => setCount(count + 1);
            UnmountAspect = () => setAspectMounted(false);
            RenderCount++;

            var fileOptions = new FilePickerOptions([".txt"], PickerLocationId.PicturesLibrary, "Open Test");
            var folderOptions = new FolderPickerOptions(PickerLocationId.Desktop, "Select Test");

            return VStack(
                aspectMounted ? Component<WindowAspectRatioHookChild>() : TextBlock("aspect unmounted"),
                TextBlock($"hooks {count}"),
                Button("File", () => _ = UseFilePickerAsync(fileOptions)).OnMount(fe => FileButton = (Button)fe),
                Button("Folder", () => _ = UseFolderPickerAsync(folderOptions)).OnMount(fe => FolderButton = (Button)fe));
        }
    }

    private sealed class WindowAspectRatioHookChild : Component
    {
        public override Element Render()
        {
            Context.UseWindowAspectRatio(2.0);
            return TextBlock("aspect");
        }
    }
    private sealed class StubPickerService : IPickerService
    {
        public nint LastHwnd { get; private set; }
        public int FileCalls { get; private set; }
        public int FolderCalls { get; private set; }
        public FilePickerOptions? LastFileOptions { get; private set; }
        public FolderPickerOptions? LastFolderOptions { get; private set; }

        public Task<StorageFile?> PickFileAsync(nint hwnd, FilePickerOptions options)
        {
            LastHwnd = hwnd;
            LastFileOptions = options;
            FileCalls++;
            return Task.FromResult<StorageFile?>(null);
        }

        public Task<StorageFolder?> PickFolderAsync(nint hwnd, FolderPickerOptions options)
        {
            LastHwnd = hwnd;
            LastFolderOptions = options;
            FolderCalls++;
            return Task.FromResult<StorageFolder?>(null);
        }
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
        await CollectWindowResources();
    }

    // Forced GC + finalizer drain is intentional. See the comment on
    // CollectWindowResources in Phase2WindowingFixtures.cs for rationale.
    // Uses a longer 100ms drain because Phase 7 fixtures hold more native
    // resources (custom title bars, picker stubs, multi-hook components).
    private static async Task CollectWindowResources()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(100);
    }

    private static void Invoke(Button button)
        => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessage(nint hWnd, uint msg, nuint wParam, nint lParam);

    private const uint WM_DISPLAYCHANGE = 0x007E;

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
            finally
            {
                win.Hide();
                ReactorDisplay.UnregisterWindowMonitor(win);
                await Harness.Render(50);
            }
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

    internal class UseSpec054HooksSuite(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var previous = RenderContext.PickerService;
            var service = new StubPickerService();
            RenderContext.PickerService = service;
            var component = new Spec054HooksComponent();
            var win = await OpenAndSettle(new WindowSpec { Title = "UseSpec054Hooks", Width = 320, Height = 220 }, () => component);
            try
            {
                bool registered = await Harness.WaitFor(() => win.EffectiveAspectRatioForTests == 2.0, maxPasses: 10, perPassMs: 20);
                H.Check("UseWindowAspectRatio_Registers_SpecUnchanged", win.Spec.AspectRatio is null);
                H.Check("UseWindowAspectRatio_Registers_Effective", registered);

                win.SetPosition(180, 140);
                bool positionUpdated = await Harness.WaitFor(
                    () => Math.Abs(component.Position.X - 180) <= 4 && Math.Abs(component.Position.Y - 140) <= 4 && component.RenderCount > 1,
                    maxPasses: 10,
                    perPassMs: 20);
                H.Check("UseWindowPosition_RerendersOnMove", positionUpdated);

                var firstDrag = component.Drag;
                int renders = component.RenderCount;
                component.TriggerRender!();
                bool rerendered = await Harness.WaitFor(() => component.RenderCount > renders, maxPasses: 10, perPassMs: 20);
                H.Check("UseWindowDragMove_StableActionAcrossRenders_Rerendered", rerendered);
                H.Check("UseWindowDragMove_StableActionAcrossRenders", ReferenceEquals(firstDrag, component.Drag));

                component.UnmountAspect!();
                bool cleaned = await Harness.WaitFor(() => win.EffectiveAspectRatioForTests is null, maxPasses: 10, perPassMs: 20);
                H.Check("UseWindowAspectRatio_CleansUp", cleaned);

                bool initialDisplays = component.DisplaysCount == ReactorDisplay.Displays.Count;
                renders = component.RenderCount;
                _ = SendMessage(WinRT.Interop.WindowNative.GetWindowHandle(win.NativeWindow), WM_DISPLAYCHANGE, 0, 0);
                bool displaysRerendered = await Harness.WaitFor(() => component.RenderCount > renders, maxPasses: 10, perPassMs: 20);
                H.Check("UseDisplays_RerendersOnLayoutChange_Initial", initialDisplays);
                H.Check("UseDisplays_RerendersOnLayoutChange", displaysRerendered && component.DisplaysCount == ReactorDisplay.Displays.Count);

                renders = component.RenderCount;
                win.RaiseZOrderChangedForTests(movedToTop: false, isCovered: true);
                bool coveredUpdated = await Harness.WaitFor(() => component.Covered && component.RenderCount > renders, maxPasses: 10, perPassMs: 20);
                H.Check("UseIsCovered_RerendersOnZOrderChange", coveredUpdated);

                Invoke(component.FileButton!);
                var expected = WinRT.Interop.WindowNative.GetWindowHandle(win.NativeWindow);
                H.Check("UseFilePickerAsync_RoutesThroughPickerService", service.FileCalls == 1 && service.LastHwnd == expected);
                var fileOpts = service.LastFileOptions;
                H.Check("UseFilePickerAsync_RoutesThroughPickerService_Options",
                    fileOpts is not null
                    && fileOpts.FileTypeFilter?.FirstOrDefault() == ".txt"
                    && fileOpts.SuggestedStartLocation == PickerLocationId.PicturesLibrary
                    && fileOpts.CommitButtonText == "Open Test");

                Invoke(component.FolderButton!);
                H.Check("UseFolderPickerAsync_RoutesThroughPickerService", service.FolderCalls == 1 && service.LastHwnd == expected);
                var folderOpts = service.LastFolderOptions;
                H.Check("UseFolderPickerAsync_RoutesThroughPickerService_Options",
                    folderOpts is not null
                    && folderOpts.SuggestedStartLocation == PickerLocationId.Desktop
                    && folderOpts.CommitButtonText == "Select Test");
            }
            finally
            {
                RenderContext.PickerService = previous;
                await CloseAndSettle(win);
                await CollectWindowResources();
            }
        }
    }
}
