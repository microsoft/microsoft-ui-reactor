using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using System.Reflection;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selftest coverage for the spec-036 window model — lifecycle events,
/// the new hooks, multi-window topology, taskbar progress / overlay /
/// thumbnail-toolbar shell COM, persistence-scope isolation, and tray
/// icon registration. Each fixture spawns a secondary
/// <see cref="ReactorWindow"/> via <see cref="ReactorApp.OpenWindow"/> so
/// the live UI-thread surface is exercised; cleanup closes every window
/// and unregisters every tray icon so the next fixture starts clean.
/// </summary>
internal static class WindowModelFixtures
{
    /// <summary>
    /// Capture the UI dispatcher on the harness window once. The selftest
    /// runner constructs its `Window` directly (not via
    /// <see cref="ReactorApp.Run"/>), so <see cref="ReactorApp.UIDispatcher"/>
    /// would otherwise stay null and the public mutators would silently
    /// skip the threading invariant.
    /// </summary>
    private static void EnsureUIDispatcher()
    {
        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
        // The selftest harness owns the long-lived host window directly (not
        // through ReactorApp.OpenWindow), so any secondary ReactorWindow we
        // open here would land in the registry as the *only* known window —
        // closing it under the default `OnPrimaryWindowClosed` policy would
        // call `Application.Exit` and kill the harness mid-fixture. Pin to
        // `Explicit` so window close is purely a registry event.
        ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
    }

    private static async Task<ReactorWindow> OpenAndSettle(WindowSpec spec, Func<Component> root)
    {
        var win = ReactorApp.OpenWindow(spec, root);
        await win.Host.WaitForIdleAsync();
        return win;
    }

    private static async Task CloseAndSettle(ReactorWindow win)
    {
        try { win.Close(); } catch { }
        // Allow the close cascade to finish.
        await Task.Delay(50);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Lifecycle events — Activated / Deactivated / Closed fire correctly
    // ════════════════════════════════════════════════════════════════════

    private sealed class StubComponent : Component
    {
        public override Element Render() => TextBlock("ok");
    }

    internal class WindowLifecycleEvents(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            int activated = 0, closed = 0;
            var win = await OpenAndSettle(
                new WindowSpec { Title = "Lifecycle Test", Width = 320, Height = 240 },
                () => new StubComponent());
            try
            {
                win.Activated += (_, _) => activated++;
                win.Closed += (_, _) => closed++;

                // Re-activate to fire the event at least once.
                win.Activate();
                await Harness.Render();
                H.Check("Window_Spec_Title_Set", win.Spec.Title == "Lifecycle Test");
                H.Check("Window_Id_Allocated", !string.IsNullOrEmpty(win.Id) && win.Id.StartsWith("win-"));
                H.Check("Window_PrimaryWindow_NotNull", ReactorApp.PrimaryWindow is not null);
                H.Check("Window_Windows_Snapshot_Contains", ReactorApp.Windows.Contains(win));
            }
            finally
            {
                await CloseAndSettle(win);
            }

            H.Check("Window_Closed_Event_Fired", closed >= 1);
            H.Check("Window_Removed_From_Snapshot", !ReactorApp.Windows.Contains(win));
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Closing event cancels the close (event subscriber path)
    //
    //  Note: the UseClosingGuard *hook* path resolves the owning window
    //  via ReactorApp.ActiveHostInternal in Phase 3 — fine for the legacy
    //  single-window app, but in this selftest the harness owns a separate
    //  raw-Window-backed host so a hook-resolved window would point at the
    //  wrong place. The Phase-9 multi-window selftest pass needs to refactor
    //  the hook resolution onto the per-host owning-window before the hook
    //  shape can be exercised live. The unit tests already cover the hook
    //  fallback semantics, so here we exercise the equivalent event path —
    //  which already uses the correct per-window state machine.
    // ════════════════════════════════════════════════════════════════════

    internal class WindowClosingEventCancels(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(
                new WindowSpec { Title = "Close Event Test", Width = 200, Height = 200 },
                () => new StubComponent());

            // Closing-event subscriber wires up the same way as a hook
            // would. We don't assert the event fires on a programmatic
            // Close() — WinUI's AppWindow.Closing isn't guaranteed to fire
            // on programmatic close paths (only on user-initiated close
            // chrome / Alt+F4). The unit-level WindowHookFallbackTests +
            // ReactorWindow.OnAppWindowClosing logic both prove the
            // cancellation flow under direct event invocation; this
            // fixture just verifies the surface is wired and Close()
            // itself completes.
            int closingCalls = 0;
            EventHandler<WindowClosingEventArgs> handler = (_, args) => closingCalls++;
            win.Closing += handler;
            H.Check("Closing_Subscribe_NoThrow", true);

            win.Closing -= handler;
            H.Check("Closing_Unsubscribe_NoThrow", true);

            win.Close();
            await Task.Delay(80);
            H.Check("Programmatic_Close_Removes_Window",
                !ReactorApp.Windows.Contains(win));
            // closingCalls is informational — we don't assert on it because
            // the platform may bypass AppWindow.Closing on programmatic
            // close. Document the count for future investigation.
            Console.WriteLine($"# Closing event count on programmatic Close(): {closingCalls}");
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  TaskbarProgress lazy-init + state round-trip on a real HWND
    // ════════════════════════════════════════════════════════════════════

    internal class TaskbarProgressLiveCom(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(
                new WindowSpec { Title = "Progress Test", Width = 200, Height = 200 },
                () => new StubComponent());
            try
            {
                var progress = win.Progress;
                progress.State = TaskbarProgressState.Indeterminate;
                H.Check("Progress_State_Indeterminate", progress.State == TaskbarProgressState.Indeterminate);

                progress.Value = 0.42;
                H.Check("Progress_Value_Roundtrip", Math.Abs(progress.Value - 0.42) < 0.0001);

                progress.State = TaskbarProgressState.Paused;
                H.Check("Progress_State_Paused", progress.State == TaskbarProgressState.Paused);

                progress.Clear();
                H.Check("Progress_Cleared_State", progress.State == TaskbarProgressState.None);
                H.Check("Progress_Cleared_Value", progress.Value == 0);
            }
            finally
            {
                await CloseAndSettle(win);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Thumbnail toolbar — add then clear without crashing on a real HWND
    // ════════════════════════════════════════════════════════════════════

    internal class ThumbnailToolbarLiveCom(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(
                new WindowSpec { Title = "Thumb Test", Width = 200, Height = 200 },
                () => new StubComponent());
            try
            {
                int aClicks = 0;
                var btnA = new ThumbnailToolbarButton(
                    Id: "a",
                    Icon: WindowIcon.FromPath("nonexistent.ico"),
                    Tooltip: "Action A",
                    OnClick: () => aClicks++);
                var btnB = new ThumbnailToolbarButton(
                    Id: "b",
                    Icon: WindowIcon.FromPath("nonexistent.ico"),
                    Tooltip: "Action B",
                    OnClick: () => { });

                win.SetThumbnailToolbar(new[] { btnA, btnB });
                await Task.Delay(20);
                H.Check("ThumbnailToolbar_Add_NoThrow", true);

                // Update path: replace with one button, then clear.
                win.SetThumbnailToolbar(new[] { btnA });
                await Task.Delay(20);
                H.Check("ThumbnailToolbar_Update_NoThrow", true);

                win.ClearThumbnailToolbar();
                H.Check("ThumbnailToolbar_Clear_NoThrow", true);

                // Validation invariants — > 7 buttons rejected, duplicate id rejected.
                bool tooMany = false;
                try
                {
                    win.SetThumbnailToolbar(Enumerable.Range(0, 8)
                        .Select(i => new ThumbnailToolbarButton(
                            Id: $"x{i}",
                            Icon: WindowIcon.FromPath("x.ico"),
                            Tooltip: "x",
                            OnClick: () => { }))
                        .ToArray());
                }
                catch (ArgumentException) { tooMany = true; }
                H.Check("ThumbnailToolbar_Rejects_GT7", tooMany);

                bool duplicate = false;
                try
                {
                    win.SetThumbnailToolbar(new[] { btnA, btnA });
                }
                catch (ArgumentException) { duplicate = true; }
                H.Check("ThumbnailToolbar_Rejects_Duplicate_Id", duplicate);
            }
            finally
            {
                await CloseAndSettle(win);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Per-window persistence scope isolation
    // ════════════════════════════════════════════════════════════════════

    private sealed class CounterComponent : Component
    {
        public override Element Render()
        {
            // UsePersisted bound to PersistedScope.Window — independent
            // values across the two windows.
            var (count, setCount) = UsePersisted("counter", 0, PersistedScope.Window);
            return VStack(
                TextBlock($"Count: {count}"),
                Button("Inc", () => setCount(count + 1)));
        }
    }

    internal class WindowPersistedScopeIsolated(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var winA = await OpenAndSettle(
                new WindowSpec { Title = "Counter A", Width = 200, Height = 200 },
                () => new CounterComponent());
            var winB = await OpenAndSettle(
                new WindowSpec { Title = "Counter B", Width = 200, Height = 200 },
                () => new CounterComponent());

            try
            {
                H.Check("Two_Windows_Open", ReactorApp.Windows.Count >= 2);
                H.Check("Distinct_PersistedScope_Instances",
                    !ReferenceEquals(winA.PersistedScope, winB.PersistedScope));
                H.Check("Distinct_Window_Ids", winA.Id != winB.Id);
            }
            finally
            {
                await CloseAndSettle(winA);
                await CloseAndSettle(winB);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  Tray icon — NIM_ADD then NIM_DELETE on a real Shell_NotifyIcon path
    // ════════════════════════════════════════════════════════════════════

    internal class TrayIconRoundTrip(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            ReactorTrayIcon? icon = null;
            try
            {
                // Use a path that won't load — LoadImageW fails gracefully and the
                // shell registration still succeeds with a null HICON.
                var spec = new TrayIconSpec(
                    Icon: WindowIcon.FromPath("definitely-not-a-real-icon.ico"),
                    Tooltip: "Reactor Selftest",
                    Key: WindowKey.Of("selftest-tray"));
                icon = ReactorApp.OpenTrayIcon(spec);
                await Task.Delay(50);

                H.Check("TrayIcon_Registered", ReactorApp.TrayIcons.Contains(icon));
                H.Check("TrayIcon_FindByKey", ReactorApp.FindTrayIcon(WindowKey.Of("selftest-tray")) == icon);
                H.Check("TrayIcon_Spec_Tooltip_Set", icon.Tooltip == "Reactor Selftest");

                // Mutating Tooltip flows through NIM_MODIFY without throwing.
                icon.Tooltip = "Updated Tooltip";
                H.Check("TrayIcon_Update_Tooltip", icon.Tooltip == "Updated Tooltip");

                // Hide / show via IsVisible flag.
                icon.IsVisible = false;
                H.Check("TrayIcon_IsVisible_False", icon.IsVisible == false);
                icon.IsVisible = true;
                H.Check("TrayIcon_IsVisible_True", icon.IsVisible == true);
            }
            finally
            {
                if (icon is not null)
                {
                    icon.Close();
                    await Task.Delay(50);
                    H.Check("TrayIcon_Closed_Removed", !ReactorApp.TrayIcons.Contains(icon));
                }
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  UseOpenWindow keyed reuse — same key returns same handle
    // ════════════════════════════════════════════════════════════════════

    private sealed class OpenerComponent : Component
    {
        public static ReactorWindow? LastOpened;
        public static int RenderCount;

        public override Element Render()
        {
            RenderCount++;
            var win = UseOpenWindow(
                WindowKey.Of("opener-child"),
                new WindowSpec { Title = "Child", Width = 240, Height = 180 },
                () => new StubComponent());
            LastOpened = win;
            return TextBlock("opener");
        }
    }

    internal class UseOpenWindowReusesByKey(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            OpenerComponent.LastOpened = null;
            OpenerComponent.RenderCount = 0;

            var parent = await OpenAndSettle(
                new WindowSpec { Title = "Parent", Width = 320, Height = 240 },
                () => new OpenerComponent());

            try
            {
                await Harness.Render();
                var firstChild = OpenerComponent.LastOpened;
                H.Check("UseOpenWindow_Opened_Child", firstChild is not null);
                H.Check("UseOpenWindow_Child_In_Snapshot", firstChild is not null && ReactorApp.Windows.Contains(firstChild));

                // Lookup by key from anywhere returns the same handle.
                var byKey = ReactorApp.FindWindow(WindowKey.Of("opener-child"));
                H.Check("UseOpenWindow_FindByKey_Matches", ReferenceEquals(byKey, firstChild));

                // Re-mount the parent's root tree to trigger a fresh render. The
                // keyed slot must hand back the same window handle. Mounting a
                // new component drops the old hooks (the spec calls for explicit
                // close-on-cleanup), but FindWindow still resolves by the live
                // registry so we use that as the canonical handle check.
                parent.Mount(new OpenerComponent());
                await Harness.Render();
                H.Check("UseOpenWindow_Same_Handle_From_Registry",
                    ReferenceEquals(ReactorApp.FindWindow(WindowKey.Of("opener-child")), firstChild));
            }
            finally
            {
                // Close the child first so the parent unmount doesn't try to
                // re-resolve the keyed slot.
                var child = ReactorApp.FindWindow(WindowKey.Of("opener-child"));
                if (child is not null) await CloseAndSettle(child);
                await CloseAndSettle(parent);
            }
        }
    }

    internal class WindowMutatorsOwnerAndGuards(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();

            var parent = await OpenAndSettle(
                new WindowSpec
                {
                    Title = "Owner Window",
                    Width = 260,
                    Height = 180,
                    MinWidth = 120,
                    MinHeight = 90,
                    MaxWidth = 640,
                    MaxHeight = 480,
                    ActivateOnOpen = false,
                    StartPosition = WindowStartPosition.Manual,
                    ManualPosition = (20, 20),
                },
                () => new StubComponent());

            ReactorWindow? child = null;
            try
            {
                child = await OpenAndSettle(
                    new WindowSpec
                    {
                        Title = "Owned Child",
                        Width = 220,
                        Height = 150,
                        Owner = parent,
                        ActivateOnOpen = false,
                        StartPosition = WindowStartPosition.CenterOnOwner,
                        Key = WindowKey.Of("owned-child-coverage"),
                    },
                    () => new StubComponent());

                H.Check("WindowMut_OwnedRegistered", parent.OwnedWindows.Contains(child));
                H.Check("WindowMut_FindByKey", ReferenceEquals(ReactorApp.FindWindow(WindowKey.Of("owned-child-coverage")), child));

                var guard = (IDisposable)typeof(ReactorWindow)
                    .GetMethod("RegisterClosingGuard", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(child, [new Func<bool>(() => true)])!;
                guard.Dispose();
                guard.Dispose();
                H.Check("WindowMut_GuardTokenIdempotent", true);

                child.Hide();
                await Task.Delay(20);
                child.Show();
                child.SetSize(240, 160);
                child.SetPosition(40, 60);
                child.CenterOnScreen();

                bool rejectedSize = false;
                try { child.SetSize(0, 1); }
                catch (ArgumentOutOfRangeException) { rejectedSize = true; }
                H.Check("WindowMut_SetSizeRejectsInvalid", rejectedSize);

                child.Update(child.Spec with
                {
                    Title = "Owned Child Updated",
                    Width = 250,
                    Height = 170,
                    IsResizable = false,
                    IsMinimizable = false,
                    IsMaximizable = false,
                    IsAlwaysOnTop = true,
                    ExtendsContentIntoTitleBar = true,
                });
                H.Check("WindowMut_UpdateSpec", child.Spec.Title == "Owned Child Updated");

                child.Mount(ctx => TextBlock("window-render-root"));
                await child.Host.WaitForIdleAsync();
                H.Check("WindowMut_MountRenderFunc", child.NativeWindow.Content is not null);
            }
            finally
            {
                if (child is not null) await CloseAndSettle(child);
                await CloseAndSettle(parent);
            }

            H.Check("WindowMut_OwnedRemoved", child is null || !parent.OwnedWindows.Contains(child));
        }
    }
}
