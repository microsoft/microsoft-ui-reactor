using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #1291 — app-replaceable render-error fallback. Each fixture pairs a positive check
/// (the app's fallback is shown / the handler saw the right <see cref="RenderErrorSource"/>)
/// with a negative one (the built-in "Render error" text is absent), so deleting the dispatch
/// at any site reddens it: the built-in fallback would reappear and the handler would not run.
/// </summary>
internal static class RenderErrorHandlerFixtures
{
    // Both built-in surfaces contain this: the in-tree placeholder ("⚠ Render error: …") and the
    // host panel header ("Render error: …").
    private const string BuiltInMarker = "Render error";

    private sealed class ThrowingComponent : Component
    {
        public override Element Render() => throw new InvalidOperationException("child render boom");
    }

    private sealed class ThrowingEffectComponent : Component
    {
        public override Element Render()
        {
            UseEffect(() => throw new InvalidOperationException("child effect boom"));
            return TextBlock("EffectChildRendered");
        }
    }

    private sealed class ThrowingCleanupComponent : Component
    {
        public override Element Render()
        {
            UseEffect(() => () => throw new InvalidOperationException("child cleanup boom"));
            return TextBlock("CleanupChild");
        }
    }

    private static RenderErrorHandler Recording(List<RenderError> log, Func<RenderError, Element?> reply) =>
        e => { log.Add(e); return reply(e); };

    // Every fixture that touches the process-wide default resets it, so a failure cannot leak
    // a handler into unrelated fixtures (which would hide the built-in fallback they assert on).
    private static async Task WithDefault(RenderErrorHandler? handler, Func<Task> body)
    {
        var previous = ReactorApp.DefaultRenderErrorHandler;
        ReactorApp.DefaultRenderErrorHandler = handler;
        try { await body(); }
        finally { ReactorApp.DefaultRenderErrorHandler = previous; }
    }

    private static async Task WithUnhandledCallback(Func<Exception, bool>? callback, Func<Task> body)
    {
        var previous = ReactorApplication.OnUnhandledException;
        ReactorApplication.OnUnhandledException = callback;
        try { await body(); }
        finally { ReactorApplication.OnUnhandledException = previous; }
    }

    private static WinUI.TextBlock? FindTextIn(DependencyObject? root, string substring)
    {
        if (root is null) return null;
        if (root is WinUI.TextBlock tb && tb.Text?.Contains(substring) == true) return tb;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var hit = FindTextIn(VisualTreeHelper.GetChild(root, i), substring);
            if (hit is not null) return hit;
        }
        return null;
    }

    private static string Sources(List<RenderError> log) =>
        log.Count == 0 ? "(none)" : string.Join(",", log.Select(e => $"{e.Source}/{(e.IsHostLevel ? "host" : "tree")}"));

    // ── In-tree: child Render() ─────────────────────────────────────────────

    internal class ChildRender_CustomFallback(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var log = new List<RenderError>();
            var host = H.CreateHost();
            host.RenderErrorHandler = Recording(log, e => TextBlock($"Custom:{e.Source}"));
            host.Mount(_ => VStack(Component<ThrowingComponent>(), TextBlock("Sibling")));
            await Harness.Render();

            H.Check("RenderErrorHandler_ChildRender_CustomShown",
                H.FindText("Custom:ComponentRender") is not null, Sources(log));
            H.Check("RenderErrorHandler_ChildRender_NoBuiltIn", H.FindTextContaining(BuiltInMarker) is null);
            H.Check("RenderErrorHandler_ChildRender_SiblingIntact", H.FindText("Sibling") is not null);
            H.Check("RenderErrorHandler_ChildRender_Context",
                log.Count >= 1 && !log[0].IsHostLevel && log[0].ComponentName == nameof(ThrowingComponent)
                && log[0].Exception.Message == "child render boom", Sources(log));
        }
    }

    // ── Default fallback unchanged ──────────────────────────────────────────

    internal class NullReturn_KeepsBuiltIn(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var log = new List<RenderError>();
            var host = H.CreateHost();
            host.RenderErrorHandler = Recording(log, _ => null);
            host.Mount(_ => VStack(Component<ThrowingComponent>()));
            await Harness.Render();

            H.Check("RenderErrorHandler_NullReturn_HandlerRan", log.Count >= 1, Sources(log));
            H.Check("RenderErrorHandler_NullReturn_BuiltInShown", H.FindTextContaining("\u26A0 Render error") is not null);
        }
    }

    internal class ThrowingHandler_KeepsBuiltIn(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.RenderErrorHandler = _ => throw new InvalidOperationException("handler bug");
            host.Mount(_ => VStack(Component<ThrowingComponent>()));
            await Harness.Render();

            H.Check("RenderErrorHandler_ThrowingHandler_BuiltInShown",
                H.FindTextContaining("\u26A0 Render error") is not null);
        }
    }

    internal class ThrowingFallback_DegradesToBuiltIn(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var log = new List<RenderError>();
            var host = H.CreateHost();
            // The app's fallback itself throws: it must degrade to the built-in placeholder
            // instead of re-entering the handler (which would loop).
            host.RenderErrorHandler = Recording(log, _ => Component<ThrowingComponent>());
            host.Mount(_ => VStack(Component<ThrowingComponent>()));
            await Harness.Render();

            H.Check("RenderErrorHandler_ThrowingFallback_BuiltInShown",
                H.FindTextContaining("\u26A0 Render error") is not null);
            H.Check("RenderErrorHandler_ThrowingFallback_HandlerCalledOnce", log.Count == 1, Sources(log));
        }
    }

    // ── In-tree: child update ───────────────────────────────────────────────

    private sealed class ToggleThrowComponent : Component<bool>
    {
        public override Element Render() =>
            Props ? throw new InvalidOperationException("child update boom") : TextBlock("ChildHealthy");
    }

    // Exercises the update-path catch (Reconciler.ReconcileComponent), not the mount one:
    // the child mounts healthy and only throws when re-rendered with new props.
    internal class ChildUpdate_CustomFallback(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var log = new List<RenderError>();
            bool shouldThrow = false;
            var host = H.CreateHost();
            host.RenderErrorHandler = Recording(log, e => TextBlock($"Custom:{e.Source}"));
            host.Mount(_ => VStack(
                Component<ToggleThrowComponent, bool>(shouldThrow),
                Button("Break", () => { shouldThrow = true; host.RequestRender(); })));
            await Harness.Render();
            H.Check("RenderErrorHandler_ChildUpdate_InitiallyHealthy",
                H.FindText("ChildHealthy") is not null && log.Count == 0, Sources(log));

            H.ClickButton("Break");
            await Harness.Render();

            H.Check("RenderErrorHandler_ChildUpdate_CustomShown",
                H.FindText("Custom:ComponentRender") is not null, Sources(log));
            H.Check("RenderErrorHandler_ChildUpdate_NoBuiltIn", H.FindTextContaining(BuiltInMarker) is null);
            H.Check("RenderErrorHandler_ChildUpdate_Context",
                log.Count == 1 && log[0].ComponentName == nameof(ToggleThrowComponent)
                && log[0].Exception.Message == "child update boom", Sources(log));
        }
    }

    // ── In-tree: child effect ───────────────────────────────────────────────

    internal class ChildEffect_SourceEffects(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var log = new List<RenderError>();
            var host = H.CreateHost();
            host.RenderErrorHandler = Recording(log, e => TextBlock($"Custom:{e.Source}"));
            host.Mount(_ => VStack(Component<ThrowingEffectComponent>()));
            await Harness.Render();

            H.Check("RenderErrorHandler_ChildEffect_CustomShown",
                H.FindText("Custom:Effects") is not null, Sources(log));
            H.Check("RenderErrorHandler_ChildEffect_NoBuiltIn", H.FindTextContaining(BuiltInMarker) is null);
        }
    }

    // ── Host level: root render, root effect, recovery ──────────────────────

    internal class RootRender_CustomFallbackAndRecovery(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var log = new List<RenderError>();
            bool shouldThrow = true;
            var host = H.CreateHost();
            host.RenderErrorHandler = Recording(log, e => VStack(
                TextBlock($"Custom:{e.Source}"),
                Button("Retry", () => { shouldThrow = false; host.RequestRender(); })));
            host.Mount(_ => shouldThrow
                ? throw new InvalidOperationException("root boom")
                : TextBlock("Healthy"));
            await Harness.Render();

            H.Check("RenderErrorHandler_RootRender_CustomShown",
                H.FindText("Custom:RootRender") is not null, Sources(log));
            H.Check("RenderErrorHandler_RootRender_HostLevel", log.Count >= 1 && log[0].IsHostLevel, Sources(log));
            H.Check("RenderErrorHandler_RootRender_NoBuiltIn", H.FindTextContaining(BuiltInMarker) is null);

            // The app fallback is a live Reactor tree: its Button works, and the next good
            // render reconciles away from it.
            H.ClickButton("Retry");
            await Harness.Render();

            H.Check("RenderErrorHandler_RootRender_Recovered", H.FindText("Healthy") is not null);
            H.Check("RenderErrorHandler_RootRender_FallbackGone", H.FindText("Custom:RootRender") is null);
        }
    }

    internal class RootEffect_SourceEffects(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var log = new List<RenderError>();
            var host = H.CreateHost();
            host.RenderErrorHandler = Recording(log, e => TextBlock($"Custom:{e.Source}"));
            host.Mount(ctx =>
            {
                ctx.UseEffect(() => throw new InvalidOperationException("root effect boom"));
                return TextBlock("RootRendered");
            });
            await Harness.Render();

            H.Check("RenderErrorHandler_RootEffect_CustomShown",
                H.FindText("Custom:Effects") is not null, Sources(log));
            H.Check("RenderErrorHandler_RootEffect_HostLevel",
                log.Count >= 1 && log[0].IsHostLevel && log[0].Source == RenderErrorSource.Effects, Sources(log));
            H.Check("RenderErrorHandler_RootEffect_NoBuiltIn", H.FindTextContaining(BuiltInMarker) is null);
        }
    }

    // ── Resolution: app default vs host override ────────────────────────────

    internal class AppDefault_And_HostOverride(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithDefault(_ => TextBlock("FromDefault"), async () =>
        {
            var host = H.CreateHost();
            H.Check("RenderErrorHandler_Default_Effective", host.EffectiveRenderErrorHandler is not null);
            host.Mount(_ => VStack(Component<ThrowingComponent>()));
            await Harness.Render();
            H.Check("RenderErrorHandler_Default_Used", H.FindText("FromDefault") is not null);
            H.Check("RenderErrorHandler_Default_NoBuiltIn", H.FindTextContaining(BuiltInMarker) is null);

            var overridden = H.CreateHost();
            overridden.RenderErrorHandler = _ => TextBlock("FromHost");
            overridden.Mount(_ => VStack(Component<ThrowingComponent>()));
            await Harness.Render();
            H.Check("RenderErrorHandler_Override_Wins", H.FindText("FromHost") is not null);
            H.Check("RenderErrorHandler_Override_DefaultNotUsed", H.FindText("FromDefault") is null);
        });
    }

    internal class HostControl_Override(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync() => WithDefault(_ => TextBlock("FromDefault"), async () =>
        {
            var control = new ReactorHostControl();
            control.RenderErrorHandler = e => TextBlock($"FromControl:{e.Source}");
            H.SetContent(control);
            control.Mount(_ => VStack(Component<ThrowingComponent>()));
            await Harness.Render(50);

            H.Check("RenderErrorHandler_HostControl_OverrideShown",
                H.FindText("FromControl:ComponentRender") is not null);
            H.Check("RenderErrorHandler_HostControl_NoBuiltIn", H.FindTextContaining(BuiltInMarker) is null);

            control.RenderErrorHandler = null;
            control.Mount(_ => throw new InvalidOperationException("control root boom"));
            await Harness.Render(50);
            H.Check("RenderErrorHandler_HostControl_FallsBackToDefault", H.FindText("FromDefault") is not null);
            H.SetContent(null);
            control.Dispose();
        });
    }

    // ── Propagate ───────────────────────────────────────────────────────────

    internal class Propagate_HandledByApp(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            var unhandled = new List<Exception>();
            return WithUnhandledCallback(ex => { unhandled.Add(ex); return true; }, async () =>
            {
                var host = H.CreateHost();
                host.RenderErrorHandler = e => { e.Propagate(); return TextBlock("IgnoredBecausePropagated"); };
                host.Mount(_ => VStack(Component<ThrowingComponent>(), TextBlock("PropagateSibling")));
                await Harness.Render();

                H.Check("RenderErrorHandler_Propagate_InTree_Reported",
                    unhandled.Count == 1 && unhandled[0].Message == "child render boom", $"count={unhandled.Count}");
                H.Check("RenderErrorHandler_Propagate_InTree_NoFallback",
                    H.FindTextContaining(BuiltInMarker) is null && H.FindText("IgnoredBecausePropagated") is null);
                H.Check("RenderErrorHandler_Propagate_InTree_SiblingIntact", H.FindText("PropagateSibling") is not null);

                unhandled.Clear();
                var root = H.CreateHost();
                root.RenderErrorHandler = e => { e.Propagate(); return null; };
                root.Mount(_ => throw new InvalidOperationException("root propagate boom"));
                await Harness.Render();

                H.Check("RenderErrorHandler_Propagate_Root_Reported",
                    unhandled.Count == 1 && unhandled[0].Message == "root propagate boom", $"count={unhandled.Count}");
                H.Check("RenderErrorHandler_Propagate_Root_NoFallback", H.FindTextContaining(BuiltInMarker) is null);
            });
        }
    }

    // ── Dispose cleanups ────────────────────────────────────────────────────

    internal class DisposeCleanup_Reported(Harness h) : SelfTestFixtureBase(h)
    {
        private static async Task<(Window Window, ReactorHost Host)> MountOnOwnWindow(RenderErrorHandler? handler)
        {
            // Own window so disposing the host leaves the harness host alone (see HostDispose).
            var window = new Window { Title = "RenderErrorHandler Dispose" };
            window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(300, 200));
            window.Activate();
            var host = new ReactorHost(window) { RenderErrorHandler = handler };
            host.Mount(ctx =>
            {
                ctx.UseEffect(() => () => throw new InvalidOperationException("root cleanup boom"));
                return VStack(Component<ThrowingCleanupComponent>());
            });
            await Task.Delay(150);
            await Harness.Render();
            return (window, host);
        }

        public override async Task RunAsync()
        {
            var log = new List<RenderError>();
            var (window, host) = await MountOnOwnWindow(Recording(log, _ => TextBlock("ignored")));
            Exception? escaped = null;
            try { host.Dispose(); } catch (Exception ex) { escaped = ex; }
            window.Close();

            H.Check("RenderErrorHandler_Dispose_NoThrow", escaped is null, escaped?.Message ?? "");
            H.Check("RenderErrorHandler_Dispose_RootCleanupReported",
                log.Any(e => e.Source == RenderErrorSource.Cleanup && e.IsHostLevel
                    && e.Exception.Message == "root cleanup boom"), Sources(log));
            H.Check("RenderErrorHandler_Dispose_ChildCleanupReported",
                log.Any(e => e.Source == RenderErrorSource.Cleanup && !e.IsHostLevel
                    && e.Exception.Message == "child cleanup boom"), Sources(log));

            // Differential: with no handler anywhere the pre-#1291 behavior (escape) is kept.
            await WithDefault(null, async () =>
            {
                var (window2, host2) = await MountOnOwnWindow(null);
                Exception? escaped2 = null;
                try { host2.Dispose(); } catch (Exception ex) { escaped2 = ex; }
                window2.Close();
                H.Check("RenderErrorHandler_Dispose_NoHandler_Escapes", escaped2?.Message == "root cleanup boom",
                    escaped2?.Message ?? "(nothing escaped)");
            });

            var next = H.CreateHost();
            next.Mount(_ => TextBlock("AfterRenderErrorDispose"));
            await Harness.Render();
            H.Check("RenderErrorHandler_Dispose_NewHostWorks", H.FindText("AfterRenderErrorDispose") is not null);
        }
    }

    // ── WindowSpec seeding ──────────────────────────────────────────────────

    internal class WindowSpec_SeedsHost(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            if (ReactorApp.UIDispatcher is null)
                ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;

            var log = new List<RenderError>();
            RenderErrorHandler handler = Recording(log, e => TextBlock($"FromSpec:{e.Source}"));
            var win = ReactorApp.OpenWindow(
                new WindowSpec { Title = "RenderErrorHandler Spec", Width = 320, Height = 200, RenderErrorHandler = handler },
                (Func<RenderContext, Element>)(_ => throw new InvalidOperationException("spec root boom")));
            try
            {
                await win.Host.WaitForIdleAsync();
                await Harness.Render(50);

                H.Check("RenderErrorHandler_WindowSpec_AppliedToHost", ReferenceEquals(win.Host.RenderErrorHandler, handler));
                H.Check("RenderErrorHandler_WindowSpec_FirstRenderHandled",
                    log.Count >= 1 && log[0].Source == RenderErrorSource.RootRender, Sources(log));
                H.Check("RenderErrorHandler_WindowSpec_CustomShown",
                    FindTextIn(win.NativeWindow.Content, "FromSpec:RootRender") is not null);
                H.Check("RenderErrorHandler_WindowSpec_NoBuiltIn",
                    FindTextIn(win.NativeWindow.Content, BuiltInMarker) is null);

                // Update() re-seeds the handler on the host (and clearing it restores fall-through).
                win.Update(win.Spec with { RenderErrorHandler = null });
                H.Check("RenderErrorHandler_WindowSpec_UpdateClears", win.Host.RenderErrorHandler is null);
            }
            finally
            {
                try { win.Close(); } catch { }
                await Task.Delay(80);
            }
        }
    }
}
