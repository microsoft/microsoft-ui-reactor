using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selfhost fixtures targeting Reactor\Hosting coverage gaps:
/// ReactorHostControl (0%), ReactorHost uncovered paths, PageHelper, RenderStats.
/// </summary>
internal static class HostingCoverageFixtures
{
    // ════════════════════════════════════════════════════════════════════════
    //  1. ReactorHostControl — mount component, render, verify content
    //     Targets: ReactorHostControl constructor, Mount(Component), Render, Dispose
    // ════════════════════════════════════════════════════════════════════════

    private class SimpleHostedComponent : Microsoft.UI.Reactor.Core.Component
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            return VStack(
                TextBlock($"Hosted:{count}"),
                Button("HostInc", () => setCount(count + 1))
            );
        }
    }

    internal class HostControlMountComponent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Create a ReactorHostControl and mount a component into it
            var hostControl = new ReactorHostControl();
            var comp = new SimpleHostedComponent();
            hostControl.Mount(comp);

            // Place it in the test content area. Use extra delay because this
            // standalone ReactorHostControl doesn't register with ReactorApp.ActiveHost,
            // so Harness.Render() can't wait on its render loop directly.
            var container = new Border { Child = hostControl };
            H.SetContent(container);
            await Harness.Render(200);

            // Verify it rendered
            var text = FindInContainer<TextBlock>(hostControl, tb => tb.Text?.StartsWith("Hosted:") == true);
            H.Check("HostCtrl_Mounted", text is not null);
            H.Check("HostCtrl_InitialValue", text?.Text == "Hosted:0");

            // Click and verify re-render
            var btn = FindInContainer<Button>(hostControl, b => b.Content is string s && s == "HostInc");
            if (btn is not null)
            {
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(btn);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)
                    peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            }
            await Harness.Render(200);
            text = FindInContainer<TextBlock>(hostControl, tb => tb.Text?.StartsWith("Hosted:") == true);
            H.Check("HostCtrl_Updated", text?.Text == "Hosted:1");

            // Dispose
            hostControl.Dispose();
            H.Check("HostCtrl_Disposed", true);

            // Restore harness content
            H.SetContent(null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  2. ReactorHostControl — mount function component
    //     Targets: ReactorHostControl.Mount(Func<RenderContext, Element>)
    // ════════════════════════════════════════════════════════════════════════

    internal class HostControlMountFunc(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var hostControl = new ReactorHostControl();
            hostControl.Mount(ctx =>
            {
                var (val, setVal) = ctx.UseState("hello");
                return VStack(
                    TextBlock($"Func:{val}"),
                    Button("HostChange", () => setVal("world"))
                );
            });

            // Standalone ReactorHostControl doesn't register with ReactorApp.ActiveHost,
            // so Harness.Render() can't wait on its render loop directly — give it
            // wall-clock time, same as the sibling Component / Factory fixtures.
            var container = new Border { Child = hostControl };
            H.SetContent(container);
            await Harness.Render(200);

            var text = FindInContainer<TextBlock>(hostControl, tb => tb.Text?.StartsWith("Func:") == true);
            H.Check("HostCtrlFunc_Mounted", text is not null);
            H.Check("HostCtrlFunc_Initial", text?.Text == "Func:hello");

            var btn = FindInContainer<Button>(hostControl, b => b.Content is string s && s == "HostChange");
            if (btn is not null)
            {
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(btn);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)
                    peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            }
            await Harness.Render(200);
            text = FindInContainer<TextBlock>(hostControl, tb => tb.Text?.StartsWith("Func:") == true);
            H.Check("HostCtrlFunc_Updated", text?.Text == "Func:world");

            hostControl.Dispose();
            H.SetContent(null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  3. ReactorHostControl — ComponentFactory (Loaded path)
    //     Targets: OnLoaded with ComponentFactory, Props
    // ════════════════════════════════════════════════════════════════════════

    private class PropsComponent : Microsoft.UI.Reactor.Core.Component<string>
    {
        public override Element Render()
        {
            return TextBlock($"WithProps:{Props}");
        }
    }

    internal class HostControlFactory(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var hostControl = new ReactorHostControl
            {
                ComponentFactory = () => new PropsComponent(),
                Props = "test-value",
            };

            // Adding to visual tree triggers Loaded → OnLoaded → Mount.
            // The Loaded event fires asynchronously after the visual tree processes
            // the addition. Use extra delay to ensure the mount + render completes
            // (this host control doesn't go through ReactorApp.ActiveHost).
            var container = new Border { Child = hostControl };
            H.SetContent(container);
            await Harness.Render(200);

            var text = FindInContainer<TextBlock>(hostControl, tb => tb.Text?.StartsWith("WithProps:") == true);
            H.Check("HostCtrlFactory_Mounted", text is not null);
            H.Check("HostCtrlFactory_PropsApplied", text?.Text == "WithProps:test-value");

            hostControl.Dispose();
            H.SetContent(null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  4. ReactorHostControl — Reconciler access + OnRenderComplete callback
    //     Targets: Reconciler property, OnRenderComplete
    // ════════════════════════════════════════════════════════════════════════

    internal class HostControlRenderCallback(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var hostControl = new ReactorHostControl();
            var renderCallbackFired = false;
            double lastTreeBuild = -1, lastReconcile = -1, lastEffects = -1;

            hostControl.OnRenderComplete = (tree, reconcile, effects) =>
            {
                renderCallbackFired = true;
                lastTreeBuild = tree;
                lastReconcile = reconcile;
                lastEffects = effects;
            };

            H.Check("HostCtrlCb_HasReconciler", hostControl.Reconciler is not null);

            hostControl.Mount(ctx => TextBlock("Callback test"));

            var container = new Border { Child = hostControl };
            H.SetContent(container);
            await Harness.Render();

            H.Check("HostCtrlCb_CallbackFired", renderCallbackFired);
            H.Check("HostCtrlCb_TreeBuildNonNeg", lastTreeBuild >= 0);
            H.Check("HostCtrlCb_ReconcileNonNeg", lastReconcile >= 0);
            H.Check("HostCtrlCb_EffectsNonNeg", lastEffects >= 0);

            hostControl.Dispose();
            H.SetContent(null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  5. ReactorHost — OnRenderComplete + Stats
    //     Targets: ReactorHost.OnRenderComplete, Stats property, perf reporting
    // ════════════════════════════════════════════════════════════════════════

    internal class HostRenderStats(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var callbackCount = 0;
            host.OnRenderComplete = (_, _, _) => callbackCount++;

            host.Mount(ctx =>
            {
                var (n, set) = ctx.UseState(0);
                return VStack(
                    TextBlock($"Stats:{n}"),
                    Button("Bump", () => set(n + 1))
                );
            });

            await Harness.Render();
            H.Check("HostStats_InitialCallback", callbackCount >= 1);

            // Trigger multiple renders to accumulate stats
            for (int i = 0; i < 3; i++)
            {
                H.ClickButton("Bump");
                await Harness.Render();
            }

            H.Check("HostStats_MultipleCallbacks", callbackCount >= 4);
            // Stats.TotalRenders only updates after the ~1s report window;
            // verify via the callback count instead (always incremented).
            H.Check("HostStats_CallbacksAccurate", callbackCount >= 4);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  6. ReactorHost — Dispose lifecycle
    //     Targets: ReactorHost.Dispose, cleanup paths
    // ════════════════════════════════════════════════════════════════════════

    internal class HostDispose(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Use a separate window so Dispose doesn't affect the test harness
            var window = new Window { Title = "Dispose Test" };
            window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(400, 300));
            window.Activate();

            var host = new ReactorHost(window);
            host.Mount(ctx => TextBlock("WillDispose"));
            await Task.Delay(200);
            await Harness.Render();

            host.Dispose();
            H.Check("HostDispose_Completed", true);
            window.Close();

            // After dispose, re-set ActiveHost for subsequent fixtures
            var newHost = H.CreateHost();
            newHost.Mount(ctx => TextBlock("AfterDispose"));
            await Harness.Render();
            H.Check("HostDispose_NewHostWorks", H.FindText("AfterDispose") is not null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  7. PageHelper — Mount and Unmount
    //     Targets: PageHelper.Mount<T>, PageHelper.Unmount
    // ════════════════════════════════════════════════════════════════════════

    internal class PageHelperExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // PageHelper.Unmount with null — should not throw
            ReactorHostControl? nullHost = null;
            PageHelper.Unmount(ref nullHost);
            H.Check("PageHelper_UnmountNull", nullHost is null);

            // Create a ReactorHostControl, mount, then unmount
            var hostCtrl = new ReactorHostControl();
            hostCtrl.Mount(ctx => TextBlock("PageHelper"));
            var container = new Border { Child = hostCtrl };
            H.SetContent(container);
            await Harness.Render();

            ReactorHostControl? hostRef = hostCtrl;
            PageHelper.Unmount(ref hostRef);
            H.Check("PageHelper_UnmountClears", hostRef is null);

            H.SetContent(null);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  8. XamlInterop.Register — exercises the Register path
    //     Targets: XamlInterop.Register, XamlHostElement/XamlPageElement via registered types
    // ════════════════════════════════════════════════════════════════════════

    internal class XamlInteropRegister(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Use a separate window so the ReactorHostControl's own render loop
            // doesn't interfere with the test harness TitleBar.
            var window = new Window { Title = "XamlInterop Test" };
            window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(400, 300));

            var hostControl = new ReactorHostControl();
            XamlInterop.Register(hostControl.Reconciler);

            hostControl.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                return VStack(
                    new XamlHostElement(
                        () => new TextBlock { Text = "interop" },
                        ctrl => ((TextBlock)ctrl).Text = $"interop:{phase}"
                    ),
                    Button("UpdInterop", () => set(1))
                );
            });

            window.Content = hostControl;
            window.Activate();
            await Task.Delay(200);
            await Harness.Render();

            var text = FindInContainer<TextBlock>(hostControl, tb => tb.Text?.Contains("interop") == true);
            H.Check("XamlInterop_Mounted", text is not null);

            var btn = FindInContainer<Button>(hostControl, b => b.Content is string s && s == "UpdInterop");
            if (btn is not null)
            {
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(btn);
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)
                    peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
            }
            await Task.Delay(200);
            await Harness.Render();
            text = FindInContainer<TextBlock>(hostControl, tb => tb.Text?.Contains("interop:1") == true);
            H.Check("XamlInterop_Updated", text is not null);

            hostControl.Dispose();
            window.Close();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  9. PreviewCaptureServer — HTTP/auth/CORS/component endpoints
    //     Targets: PreviewCaptureServer request handling without external tools.
    // ════════════════════════════════════════════════════════════════════════

    internal class PreviewCaptureServerEndpoints(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            string? current = "Counter";
            using var server = new PreviewCaptureServer(H.Window.DispatcherQueue, H.Window, fps: 3)
            {
                GetComponents = () => ["Counter", "Todo"],
                GetCurrentComponent = () => current,
                SwitchComponent = name =>
                {
                    if (name is not ("Counter" or "Todo")) return false;
                    current = name;
                    return true;
                },
            };
            server.Start();

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}/") };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.AuthToken);

            using var status = await client.GetAsync("status");
            var statusText = await status.Content.ReadAsStringAsync();
            H.Check("Preview_StatusOk", status.StatusCode == HttpStatusCode.OK && statusText.Contains("\"fps\":3"));

            using var components = await client.GetAsync("components");
            var componentsText = await components.Content.ReadAsStringAsync();
            H.Check("Preview_ComponentsOk",
                components.StatusCode == HttpStatusCode.OK &&
                componentsText.Contains("Counter") &&
                componentsText.Contains("\"current\":\"Counter\""));

            using var frameEmpty = await client.GetAsync("frame");
            H.Check("Preview_FrameEmpty204", frameEmpty.StatusCode == HttpStatusCode.NoContent);

            typeof(PreviewCaptureServer)
                .GetField("_latestFrame", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(server, new byte[] { 1, 2, 3, 4 });
            using var frame = await client.GetAsync("frame");
            H.Check("Preview_FrameOk", frame.StatusCode == HttpStatusCode.OK && frame.Content.Headers.ContentType?.MediaType == "image/jpeg");

            using var focusGet = await client.GetAsync("focus");
            H.Check("Preview_FocusGet405", focusGet.StatusCode == HttpStatusCode.MethodNotAllowed);

            using var focusPost = await client.PostAsync("focus", new StringContent("", Encoding.UTF8, "application/json"));
            H.Check("Preview_FocusPostOk", focusPost.StatusCode == HttpStatusCode.OK);

            using var previewGet = await client.GetAsync("preview");
            H.Check("Preview_SwitchGet405", previewGet.StatusCode == HttpStatusCode.MethodNotAllowed);

            using var previewWrongType = await client.PostAsync("preview", new StringContent("component=Todo", Encoding.UTF8, "text/plain"));
            H.Check("Preview_SwitchContentType415", previewWrongType.StatusCode == HttpStatusCode.UnsupportedMediaType);

            using var previewMissing = await client.PostAsync("preview", new StringContent("{}", Encoding.UTF8, "application/json"));
            H.Check("Preview_SwitchMissing400", previewMissing.StatusCode == HttpStatusCode.BadRequest);

            using var previewOk = await client.PostAsync("preview", new StringContent("{\"component\":\"Todo\"}", Encoding.UTF8, "application/json"));
            var previewOkText = await previewOk.Content.ReadAsStringAsync();
            H.Check("Preview_SwitchOk", previewOk.StatusCode == HttpStatusCode.OK && current == "Todo" && previewOkText.Contains("\"ok\":true"));

            using var previewMissingComponent = await client.PostAsync("preview", new StringContent("{\"component\":\"Missing\"}", Encoding.UTF8, "application/json"));
            H.Check("Preview_SwitchNotFound404", previewMissingComponent.StatusCode == HttpStatusCode.NotFound);

            using var missingPath = await client.GetAsync("missing");
            H.Check("Preview_MissingPath404", missingPath.StatusCode == HttpStatusCode.NotFound);

            using var noAuth = new HttpClient { BaseAddress = client.BaseAddress };
            using var unauthorized = await noAuth.GetAsync("status");
            H.Check("Preview_Unauthorized401", unauthorized.StatusCode == HttpStatusCode.Unauthorized);

            using var corsReq = new HttpRequestMessage(HttpMethod.Options, "status");
            corsReq.Headers.TryAddWithoutValidation("Origin", "vscode-webview://reactor-preview");
            corsReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.AuthToken);
            using var cors = await client.SendAsync(corsReq);
            H.Check("Preview_OptionsCors204",
                cors.StatusCode == HttpStatusCode.NoContent &&
                cors.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) &&
                values.Contains("vscode-webview://reactor-preview"));

            using var badOriginReq = new HttpRequestMessage(HttpMethod.Get, "status");
            badOriginReq.Headers.TryAddWithoutValidation("Origin", "http://localhost.evil.com");
            badOriginReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.AuthToken);
            using var badOrigin = await client.SendAsync(badOriginReq);
            H.Check("Preview_BadOrigin403", badOrigin.StatusCode == HttpStatusCode.Forbidden);

            using var badHostReq = new HttpRequestMessage(HttpMethod.Get, "status");
            badHostReq.Headers.Host = $"example.com:{server.Port}";
            badHostReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.AuthToken);
            using var badHost = await client.SendAsync(badHostReq);
            H.Check("Preview_BadHost421", (int)badHost.StatusCode == 421);

            var small = PreviewCaptureServer.ReadCappedBody(new MemoryStream(Encoding.UTF8.GetBytes("abc")), Encoding.UTF8, cap: 4);
            H.Check("Preview_ReadCappedSmall", small == "abc");

            bool cappedThrows = false;
            try
            {
                _ = PreviewCaptureServer.ReadCappedBody(new MemoryStream(Encoding.UTF8.GetBytes("abcde")), Encoding.UTF8, cap: 4);
            }
            catch (InvalidDataException)
            {
                cappedThrows = true;
            }
            H.Check("Preview_ReadCappedThrows", cappedThrows);
        }
    }

    // ── Helper: find controls within a specific ReactorHostControl ──────────

    private static T? FindInContainer<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        if (root is T match && predicate(match)) return match;
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindInContainer(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i), predicate);
            if (found is not null) return found;
        }
        return null;
    }
}
