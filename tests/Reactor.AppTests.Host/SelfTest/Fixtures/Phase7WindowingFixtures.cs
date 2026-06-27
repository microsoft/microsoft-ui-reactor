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

    // Spec 059 — captures the realized TitleBar control + a child marked
    // .IsDragRegion(false) so the fixture can read back the attached prop.
    private sealed class TitleBarDragRegionComponent : Component
    {
        public Microsoft.UI.Xaml.Controls.TitleBar? Bar;
        public Microsoft.UI.Xaml.FrameworkElement? Clickable;
        public override Element Render() =>
            VStack(
                (TitleBar("Drag") with
                {
                    Content = Button("X", () => { })
                        .IsDragRegion(false)
                        .OnMount(fe => Clickable = fe),
                })
                .AutoRefreshDragRegions()
                .Set(b => Bar = b),
                TextBlock("body"));
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

    // Spec 059 — TitleBar drag-region APIs (WinApp SDK ≥ 2.1.3): AutoRefreshDragRegions
    // auto-maps onto the control; .IsDragRegion(false) writes TitleBar.IsDragRegion on a child.
    internal class TitleBarDragRegions(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var comp = new TitleBarDragRegionComponent();
            var win = await OpenAndSettle(
                new WindowSpec { Title = "Drag Regions", Width = 360, Height = 220 },
                () => comp);
            try
            {
                H.Check("TitleBar_AutoRefreshDragRegions_RoundTrip", comp.Bar?.AutoRefreshDragRegions == true);
                bool? flag = comp.Clickable is null
                    ? null
                    : Microsoft.UI.Xaml.Controls.TitleBar.GetIsDragRegion(comp.Clickable);
                H.Check("TitleBar_IsDragRegion_ChildClickable", flag == false);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class TitleBarExplicitFalseOverrides(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "Explicit False", Width = 320, Height = 220, ExtendsContentIntoTitleBar = false }, () => new TitleBarComponent());
            // Issue #537 regression: a window with ExtendsContentIntoTitleBar=false
            // that still renders a TitleBar element must close cleanly. The WinUI
            // TitleBar control corrupts the heap (STATUS_HEAP_CORRUPTION) on
            // teardown unless the window is in content-extended mode, so Reactor
            // flips ExtendsContentIntoTitleBar=true just before the native close
            // (ReactorWindow.PrepareTitleBarForClose). Closing through the normal
            // Close() path — no Hide/UnregisterWindowMonitor mitigation — is itself
            // the assertion: without the fix this process terminates with
            // STATUS_HEAP_CORRUPTION instead of reaching the check. The assertion
            // still observes false because the flip happens only at close.
            try { H.Check("TitleBar_ExplicitFalseOverrides", !win.NativeWindow.ExtendsContentIntoTitleBar); }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class TitleBarOwnedChildClosesClean(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            // Issue #537 (multi-window coverage): an OWNED child window with
            // ExtendsContentIntoTitleBar=false that renders a TitleBar element
            // must close cleanly — the same heap-corruption hazard as the
            // top-level case (#537), verified here for an owned window in a
            // parent/child relationship. The child closes through the normal
            // Close() path, which walks the owned tree
            // (PrepareTitleBarTreeForClose) and flips it back into
            // content-extended mode first; reaching the final check without a
            // STATUS_HEAP_CORRUPTION is the regression proof.
            //
            // Note: the parent-driven owner-close *cascade* — a still-open owned
            // TitleBar child torn down by closing its owner — is exercised by the
            // chrome/Alt+F4 path (E2E tier). It is intentionally NOT reproduced
            // here: closing an owner while it still owns a live child trips a
            // separate, pre-existing multi-window teardown access violation in
            // BackdropApplier.Reset (0xC0000005) that is unrelated to #537 and
            // confirmed independent of this fix (it reproduces with a
            // non-TitleBar child). The existing owner/child fixtures likewise
            // close the child first for this reason.
            var parent = await OpenAndSettle(
                new WindowSpec { Title = "Owner", Width = 320, Height = 220 },
                () => new PlainComponent());
            var child = await OpenAndSettle(
                new WindowSpec { Title = "Owned TitleBar Child", Width = 280, Height = 180, Owner = parent, ExtendsContentIntoTitleBar = false },
                () => new TitleBarComponent());

            H.Check("TitleBar_OwnedChild_Owned",
                parent.OwnedWindows.Contains(child) && !child.NativeWindow.ExtendsContentIntoTitleBar);

            // Child-first is the harness-stable close order for owned windows.
            await CloseAndSettle(child);
            await CloseAndSettle(parent);
            H.Check("TitleBar_OwnedChild_ClosesClean", true);
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

    internal class TitleBarDisposeWithoutClose(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            // Issue #537 (Dispose-path coverage): a direct Dispose() that is NOT
            // preceded by Close() still tears down the mounted WinUI TitleBar
            // control — Dispose() disposes the host, which unmounts the content
            // and the TitleBar with it. ReactorWindow.Dispose() runs
            // PrepareTitleBarForClose() first so an ExtendsContentIntoTitleBar=false
            // window flips back into content-extended mode before that teardown.
            // Disposing without a prior Close() is the assertion: without the prep
            // this process terminates with STATUS_HEAP_CORRUPTION instead of
            // reaching the checks. A single top-level window (no owner) avoids the
            // unrelated multi-window BackdropApplier teardown AV (#647).
            var win = await OpenAndSettle(
                new WindowSpec { Title = "Dispose No Close", Width = 320, Height = 220, ExtendsContentIntoTitleBar = false },
                () => new TitleBarComponent());
            H.Check("TitleBar_DisposeNoClose_BeforeFalse", !win.NativeWindow.ExtendsContentIntoTitleBar);

            // Direct dispose — bypasses Close()/the Window.Closed -> Unregister ->
            // Dispose flow. The prep runs first, so the flip is observable after.
            win.Dispose();
            await Harness.Render(50);

            H.Check("TitleBar_DisposeNoClose_Flipped", win.NativeWindow.ExtendsContentIntoTitleBar);

            // Dispose() alone does not unregister (the normal flow is
            // Window.Closed -> UnregisterWindow -> Dispose). Clean up the
            // registration and the still-live native window for harness hygiene
            // since we bypassed Close(). Best-effort: closing a native window
            // whose content was already disposed can race its teardown, so catch
            // only the exceptions that path can realistically surface and record
            // them (mirrors ReactorApp.PrepareOpenWindowsForExit) rather than
            // rethrowing — this is post-test cleanup after a deliberate
            // Close()-bypass.
            ReactorApp.UnregisterWindow(win);
            try { win.NativeWindow.Close(); }
            catch (ObjectDisposedException ex) { global::System.Diagnostics.Debug.WriteLine($"[Reactor] TitleBar_DisposeNoClose cleanup close threw: {ex}"); }
            catch (InvalidOperationException ex) { global::System.Diagnostics.Debug.WriteLine($"[Reactor] TitleBar_DisposeNoClose cleanup close threw: {ex}"); }
            catch (COMException ex) { global::System.Diagnostics.Debug.WriteLine($"[Reactor] TitleBar_DisposeNoClose cleanup close threw: {ex}"); }
            await CollectWindowResources();
            H.Check("TitleBar_DisposeNoClose_Clean", true);
        }
    }

    internal class TitleBarOwnedTreeFlipsRecursively(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            // Issue #537 (recursive owned-tree coverage): PrepareTitleBarTreeForClose
            // must flip every owned DESCENDANT — not just direct children — back
            // into content-extended mode before a native close, because owned
            // windows close via the raw Window.Close() path and never raise their
            // own AppWindow.Closing. Build a parent -> child -> grandchild tree
            // where the child and grandchild are ExtendsContentIntoTitleBar=false
            // TitleBar windows, drive the recursive prep the owner-close cascade
            // and Close() use, and assert the flip reached BOTH levels.
            //
            // This exercises the prep STEP of the owner-close cascade directly. The
            // full parent-driven native cascade (closing the owner while it still
            // owns live children) stays at the E2E tier: it trips the separate,
            // pre-existing multi-window teardown access violation in
            // BackdropApplier.Reset (0xC0000005, #647), unrelated to #537 and
            // independent of this fix. So we drive the prep here and close
            // leaf-first.
            var parent = await OpenAndSettle(
                new WindowSpec { Title = "Tree Owner", Width = 320, Height = 220 },
                () => new PlainComponent());
            var child = await OpenAndSettle(
                new WindowSpec { Title = "Tree Child", Width = 300, Height = 200, Owner = parent, ExtendsContentIntoTitleBar = false },
                () => new TitleBarComponent());
            var grandchild = await OpenAndSettle(
                new WindowSpec { Title = "Tree Grandchild", Width = 260, Height = 160, Owner = child, ExtendsContentIntoTitleBar = false },
                () => new TitleBarComponent());

            H.Check("TitleBar_OwnedTree_Shape",
                parent.OwnedWindows.Contains(child)
                && child.OwnedWindows.Contains(grandchild)
                && !child.NativeWindow.ExtendsContentIntoTitleBar
                && !grandchild.NativeWindow.ExtendsContentIntoTitleBar);

            // Drive the recursive prep the cascade/Close paths use.
            parent.PrepareTitleBarTreeForClose();

            H.Check("TitleBar_OwnedTree_ChildFlipped", child.NativeWindow.ExtendsContentIntoTitleBar);
            H.Check("TitleBar_OwnedTree_GrandchildFlipped", grandchild.NativeWindow.ExtendsContentIntoTitleBar);

            // Close leaf-first — the harness-stable order for owned windows.
            await CloseAndSettle(grandchild);
            await CloseAndSettle(child);
            await CloseAndSettle(parent);
            H.Check("TitleBar_OwnedTree_ClosesClean", true);
        }
    }

    internal class TitleBarExitPrepFlipsOpenWindows(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            // Issue #537 (app-exit prep coverage): ReactorApp.Exit()/SafeExit()
            // prepares every still-open window's TitleBar before
            // Application.Current.Exit() tears them down natively. This is
            // reachable under the default OnPrimaryWindowClosed policy — closing
            // the primary fires SafeExit() while a secondary
            // ExtendsContentIntoTitleBar=false TitleBar window is still open. We
            // can't call Application.Exit() in-process (it would end the selftest
            // run), so drive the same per-window prep loop the exit path uses
            // (ReactorApp.PrepareOpenWindowsForExit) and assert the open
            // ECITB=false TitleBar window was flipped into content-extended mode.
            var win = await OpenAndSettle(
                new WindowSpec { Title = "Exit Prep Secondary", Width = 320, Height = 220, ExtendsContentIntoTitleBar = false },
                () => new TitleBarComponent());
            try
            {
                H.Check("TitleBar_ExitPrep_BeforeFalse", !win.NativeWindow.ExtendsContentIntoTitleBar);

                ReactorApp.PrepareOpenWindowsForExit();

                H.Check("TitleBar_ExitPrep_Flipped", win.NativeWindow.ExtendsContentIntoTitleBar);
            }
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
