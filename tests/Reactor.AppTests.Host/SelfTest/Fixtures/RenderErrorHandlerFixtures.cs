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

    internal class ThrowingHandler_FailsClosed(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.RenderErrorHandler = _ => throw new InvalidOperationException("handler bug");
            host.Mount(_ => VStack(Component<ThrowingComponent>()));
            await Harness.Render();

            // A configured handler means "keep exception text off screen": its failure must
            // not fall back to the detailed built-in placeholder.
            H.Check("RenderErrorHandler_ThrowingHandler_NeutralShown", H.FindText(ErrorFallback.SafeMessage) is not null);
            H.Check("RenderErrorHandler_ThrowingHandler_NoDetails",
                H.FindTextContaining(BuiltInMarker) is null && H.FindTextContaining("child render boom") is null);

            var root = H.CreateHost();
            root.RenderErrorHandler = e => { e.Propagate(); throw new InvalidOperationException("handler bug"); };
            root.Mount(_ => throw new InvalidOperationException("root boom"));
            await Harness.Render();

            // Propagate() followed by a throw is cancelled: neutral panel, no crash.
            H.Check("RenderErrorHandler_ThrowingHandler_Root_NeutralShown", H.FindText(ErrorFallback.SafeMessage) is not null);
            H.Check("RenderErrorHandler_ThrowingHandler_Root_NoDetails",
                H.FindTextContaining(BuiltInMarker) is null && H.FindTextContaining("root boom") is null);
        }
    }

    internal class ThrowingFallback_FailsClosed(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var log = new List<RenderError>();
            var host = H.CreateHost();
            // The app's fallback itself throws: it must degrade to the neutral placeholder
            // instead of re-entering the handler (which would loop) or showing details.
            host.RenderErrorHandler = Recording(log, _ => Component<ThrowingComponent>());
            host.Mount(_ => VStack(Component<ThrowingComponent>()));
            await Harness.Render();

            H.Check("RenderErrorHandler_ThrowingFallback_NeutralShown", H.FindText(ErrorFallback.SafeMessage) is not null);
            H.Check("RenderErrorHandler_ThrowingFallback_NoDetails", H.FindTextContaining(BuiltInMarker) is null);
            H.Check("RenderErrorHandler_ThrowingFallback_HandlerCalledOnce", log.Count == 1, Sources(log));

            log.Clear();
            var root = H.CreateHost();
            root.RenderErrorHandler = Recording(log, _ => Component<ThrowingComponent>());
            root.Mount(_ => throw new InvalidOperationException("root boom"));
            await Harness.Render();

            H.Check("RenderErrorHandler_ThrowingFallback_Root_NeutralShown", H.FindText(ErrorFallback.SafeMessage) is not null);
            H.Check("RenderErrorHandler_ThrowingFallback_Root_NoDetails", H.FindTextContaining(BuiltInMarker) is null);
            H.Check("RenderErrorHandler_ThrowingFallback_Root_HandlerCalledOnce", log.Count == 1, Sources(log));
        }
    }

    // ── In-tree: RenderEachTime / Memo children (their own mount catch sites) ──

    internal class FuncAndMemoChildren(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var log = new List<RenderError>();
            var host = H.CreateHost();
            host.RenderErrorHandler = Recording(log, e => TextBlock($"Custom:{e.Source}:{e.Exception.Message}"));
            host.Mount(_ => VStack(
                RenderEachTime(_ => throw new InvalidOperationException("func boom")),
                Memo(_ => throw new InvalidOperationException("memo boom"), "dep"),
                RenderEachTime(ctx =>
                {
                    ctx.UseEffect(() => throw new InvalidOperationException("func effect boom"));
                    return TextBlock("FuncEffectRendered");
                })));
            await Harness.Render();

            H.Check("RenderErrorHandler_FuncChild_CustomShown",
                H.FindText("Custom:ComponentRender:func boom") is not null, Sources(log));
            H.Check("RenderErrorHandler_MemoChild_CustomShown",
                H.FindText("Custom:ComponentRender:memo boom") is not null, Sources(log));
            H.Check("RenderErrorHandler_FuncChildEffect_CustomShown",
                H.FindText("Custom:Effects:func effect boom") is not null, Sources(log));
            H.Check("RenderErrorHandler_FuncMemo_NoBuiltIn", H.FindTextContaining(BuiltInMarker) is null);
        }
    }

    // ── Host level: reconcile phase ─────────────────────────────────────────

    internal class Reconcile_SourceReconcile(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var log = new List<RenderError>();
            var host = H.CreateHost();
            host.RenderErrorHandler = Recording(log, e => TextBlock($"Custom:{e.Source}"));
            // The root render succeeds; mounting its element throws inside Reconcile.
            host.Mount(_ => TextBlock("NeverShown").Set(_ => throw new InvalidOperationException("reconcile boom")));
            await Harness.Render();

            H.Check("RenderErrorHandler_Reconcile_CustomShown",
                H.FindText("Custom:Reconcile") is not null, Sources(log));
            H.Check("RenderErrorHandler_Reconcile_HostLevel",
                log.Count >= 1 && log[0].IsHostLevel && log[0].Exception.Message == "reconcile boom", Sources(log));
            H.Check("RenderErrorHandler_Reconcile_NoBuiltIn", H.FindTextContaining(BuiltInMarker) is null);
        }
    }

    // ── Host level: overlay wrapper path ────────────────────────────────────

    internal class OverlayWrapper_TakesFallback(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var prev = ReactorFeatureFlags.HighlightReconcileChanges;
            try
            {
                ReactorFeatureFlags.HighlightReconcileChanges = true;
                bool shouldThrow = false;
                var host = H.CreateHost();
                host.RenderErrorHandler = e => TextBlock($"Custom:{e.Source}");
                host.Mount(_ => shouldThrow ? throw new InvalidOperationException("overlay boom") : TextBlock("OverlayHealthy"));
                await Harness.Render();
                H.Check("RenderErrorHandler_Overlay_WrapperInstalled", host.ContentTarget?.Child is WinUI.Grid,
                    host.ContentTarget?.Child?.GetType().Name ?? "null");

                shouldThrow = true;
                host.RequestRender();
                await Harness.Render();

                H.Check("RenderErrorHandler_Overlay_CustomShown", H.FindText("Custom:RootRender") is not null);
                // The fallback went into the wrapper's content slot, not over the wrapper.
                H.Check("RenderErrorHandler_Overlay_WrapperKept", host.ContentTarget?.Child is WinUI.Grid,
                    host.ContentTarget?.Child?.GetType().Name ?? "null");
            }
            finally
            {
                ReactorFeatureFlags.HighlightReconcileChanges = prev;
            }
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

            // Root effect (host-level Effects source) and dispose cleanups on the control.
            var log = new List<RenderError>();
            var effectControl = new ReactorHostControl { RenderErrorHandler = Recording(log, e => TextBlock($"FromControl:{e.Source}")) };
            H.SetContent(effectControl);
            effectControl.Mount(ctx =>
            {
                ctx.UseEffect(() => throw new InvalidOperationException("control effect boom"));
                return TextBlock("ControlEffectRendered");
            });
            await Harness.Render(50);
            H.Check("RenderErrorHandler_HostControl_RootEffect",
                H.FindText("FromControl:Effects") is not null && log.Count >= 1 && log[0].IsHostLevel, Sources(log));
            H.SetContent(null);
            effectControl.Dispose();

            log.Clear();
            var cleanupControl = new ReactorHostControl { RenderErrorHandler = Recording(log, _ => null) };
            H.SetContent(cleanupControl);
            cleanupControl.Mount(ctx =>
            {
                ctx.UseEffect(() => () => throw new InvalidOperationException("control cleanup boom"));
                return VStack(Component<ThrowingCleanupComponent>());
            });
            await Harness.Render(50);
            H.SetContent(null);
            Exception? escaped = null;
            try { cleanupControl.Dispose(); } catch (InvalidOperationException ex) { escaped = ex; }
            H.Check("RenderErrorHandler_HostControl_Dispose_NoThrow", escaped is null, escaped?.Message ?? "");
            H.Check("RenderErrorHandler_HostControl_Dispose_BothReported",
                log.Any(e => e.Source == RenderErrorSource.Cleanup && e.IsHostLevel && e.Exception.Message == "control cleanup boom")
                && log.Any(e => e.Source == RenderErrorSource.Cleanup && !e.IsHostLevel && e.Exception.Message == "child cleanup boom"),
                Sources(log));
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

    // ── Theme listener follows the fallback ─────────────────────────────────

    // A ThemeRef-bound fallback must re-render on theme changes, so the fallback that replaces
    // a successfully rendered root has to take over the ActualThemeChanged subscription. Each
    // re-render re-runs the throwing root, so the handler's call count is the oracle: with the
    // listener left on the detached old root, flipping the theme re-renders nothing.
    internal class FallbackTakesThemeListener(Harness h) : SelfTestFixtureBase(h)
    {
        private static ElementTheme Opposite(FrameworkElement fe) =>
            fe.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;

        public override async Task RunAsync()
        {
            // ReactorHostControl
            int controlCalls = 0;
            var control = new ReactorHostControl { RenderErrorHandler = _ => { controlCalls++; return TextBlock("ControlThemedFallback"); } };
            H.SetContent(control);
            control.Mount(_ => TextBlock("ControlThemeHealthy"));
            await Harness.Render(50);
            control.Mount(_ => throw new InvalidOperationException("control theme boom"));
            await Harness.Render(50);
            H.Check("RenderErrorHandler_Theme_HostControl_FallbackShown",
                H.FindText("ControlThemedFallback") is not null && controlCalls == 1, $"calls={controlCalls}");

            control.RequestedTheme = Opposite(control);
            await Harness.Render(100);
            H.Check("RenderErrorHandler_Theme_HostControl_Rerendered", controlCalls >= 2, $"calls={controlCalls}");
            H.SetContent(null);
            control.Dispose();

            // ReactorHost (renders into the harness's shared ContentTarget; restore its theme)
            var target = H.CreateHost().ContentTarget;
            var previousTheme = target?.RequestedTheme ?? ElementTheme.Default;
            try
            {
                int hostCalls = 0;
                bool shouldThrow = false;
                var host = H.CreateHost();
                host.RenderErrorHandler = _ => { hostCalls++; return TextBlock("HostThemedFallback"); };
                host.Mount(_ => shouldThrow ? throw new InvalidOperationException("host theme boom") : TextBlock("HostThemeHealthy"));
                await Harness.Render();
                shouldThrow = true;
                host.RequestRender();
                await Harness.Render();
                H.Check("RenderErrorHandler_Theme_Host_FallbackShown",
                    H.FindText("HostThemedFallback") is not null && hostCalls == 1, $"calls={hostCalls}");

                if (target is not null)
                {
                    target.RequestedTheme = Opposite(target);
                    await Harness.Render(100);
                }
                H.Check("RenderErrorHandler_Theme_Host_Rerendered", hostCalls >= 2, $"calls={hostCalls}");
            }
            finally
            {
                if (target is not null) target.RequestedTheme = previousTheme;
            }
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
            try { host.Dispose(); } catch (InvalidOperationException ex) { escaped = ex; }
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
                try { host2.Dispose(); } catch (InvalidOperationException ex) { escaped2 = ex; }
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
                win.NativeWindow.Title = "Changed natively";
                win.Update(win.Spec with { RenderErrorHandler = null });
                H.Check("RenderErrorHandler_WindowSpec_UpdateClears", win.Host.RenderErrorHandler is null);
                // A handler-only update must not re-apply chrome (which rewrites the title) ...
                H.Check("RenderErrorHandler_WindowSpec_HandlerOnlyUpdate_NoChrome",
                    win.NativeWindow.Title == "Changed natively", win.NativeWindow.Title);
                // ... while a real chrome change still does (differential control).
                win.Update(win.Spec with { Title = "RenderErrorHandler Spec 2", RenderErrorHandler = handler });
                H.Check("RenderErrorHandler_WindowSpec_ChromeUpdate_Applies",
                    win.NativeWindow.Title == "RenderErrorHandler Spec 2" && ReferenceEquals(win.Host.RenderErrorHandler, handler),
                    win.NativeWindow.Title);
            }
            finally
            {
                // Best-effort teardown: a window the fixture already lost must not mask the
                // assertion that failed, but the reason is still logged.
                try { win.Close(); }
                catch (global::System.Runtime.InteropServices.COMException ex) { global::System.Diagnostics.Debug.WriteLine($"[RenderErrorHandler] window close failed: {ex.Message}"); }
                catch (ObjectDisposedException ex) { global::System.Diagnostics.Debug.WriteLine($"[RenderErrorHandler] window close failed: {ex.Message}"); }
                await Task.Delay(80);
            }
        }
    }
}
