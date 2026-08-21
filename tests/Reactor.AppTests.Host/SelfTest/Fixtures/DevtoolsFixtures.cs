using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Self-host MCP fixtures: mount a small Reactor tree inside the harness window,
/// spin up a <see cref="DevtoolsMcpServer"/> against it on a free loopback port,
/// and exercise the tool surface via in-test JSON-RPC calls.
/// Covers the self-host rows of §2.17 and §3.11 that need a real WinUI window
/// in-process but don't need Appium's second-process rigging.
/// </summary>
internal static class DevtoolsFixtures
{
    // -- Shared MCP harness ------------------------------------------------------

    /// <summary>
    /// Wires a <see cref="DevtoolsMcpServer"/> + registries + tool surface around
    /// the selftest harness window so fixtures can make real JSON-RPC calls over
    /// HTTP. Disposed per-fixture so ports don't leak and event handlers on the
    /// shared window don't accumulate past the fixture's run.
    /// </summary>
    internal sealed class McpHarness : IDisposable
    {
        public DevtoolsMcpServer Server { get; }
        public WindowRegistry Windows { get; }
        public NodeRegistry Nodes { get; }
        private readonly HttpClient _client;
        private readonly string _currentComponent;

        public McpHarness(
            Window window,
            Func<Component?> rootComponent,
            string currentComponent,
            IReadOnlyList<string>? components = null,
            DevtoolsLogger? logger = null,
            Func<string, bool>? switchComponent = null)
        {
            Server = new DevtoolsMcpServer(window.DispatcherQueue, window, logger: logger);
            Windows = new WindowRegistry(Server.BuildTag);
            Nodes = new NodeRegistry();
            Windows.Attach(window, isMain: true);
            _currentComponent = currentComponent;

            var available = components ?? new[] { currentComponent };
            DevtoolsTools.RegisterCore(Server, new DevtoolsTools.ToolHostContext
            {
                GetComponents = () => available,
                GetCurrentComponent = () => _currentComponent,
                SwitchComponent = switchComponent ?? (_ => false),
                RequestReload = () => { /* reload is Appium-only; no-op here */ },
                RequestShutdown = () => { /* shutdown is Appium-only; no-op here */ },
                Windows = Windows,
                Nodes = Nodes,
            });
            DevtoolsUiaTools.RegisterUiaTools(Server, Nodes, Windows);
            DevtoolsStateTool.Register(Server, rootComponent);
            DevtoolsFireTool.Register(Server, rootComponent);

            Server.Start();
            _client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{Server.Port}/") };
        }

        public async Task<JsonElement> CallAsync(string method, CallArgs? args = null)
        {
            var envelope = new McpCallEnvelope("2.0", 1, "tools/call", new McpCallParams(method, args));
            var body = JsonSerializer.Serialize(envelope, DevtoolsFixtureJsonContext.Default.McpCallEnvelope);
            var req = new HttpRequestMessage(HttpMethod.Post, "mcp")
            {
                Content = new StringContent(body, Encoding.UTF8),
            };
            req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Server.AuthToken);
            using var resp = await _client.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            // Clone so the element survives disposing the document.
            return doc.RootElement.Clone();
        }

        public void Dispose()
        {
            try { _client.Dispose(); } catch { }
            try { Server.Dispose(); } catch { }
        }
    }

    // Small helper — returns the "result" element if present, or null when the
    // response is an error envelope. Fixtures that expect errors read "error".
    private static JsonElement? Result(JsonElement response) =>
        response.TryGetProperty("result", out var r) ? r : null;

    private static JsonElement? Error(JsonElement response) =>
        response.TryGetProperty("error", out var e) ? e : null;

    // -- Test component ----------------------------------------------------------

    /// <summary>
    /// A component that exposes the surfaces the tool tests poke at: a button
    /// with a hooked counter, a textbox, a checkbox, and a handler that mutates
    /// state on a timer (for waitFor). AutomationIds let selector tests pin
    /// specific elements without tree-walking.
    /// </summary>
    private sealed class DevtoolsFixtureRoot : Component
    {
        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            var (text, setText) = UseState(string.Empty);
            var (toggled, setToggled) = UseState(false);

            return VStack(
                TextBlock($"count:{count}").AutomationId("count-label"),
                Button("Increment", () => setCount(count + 1)).AutomationId("btn-increment"),
                TextBox(text, setText).AutomationId("txt-input"),
                CheckBox(toggled, setToggled, label: "Accept").AutomationId("chk-accept"),
                // Delayed-update button: used by waitFor.
                Button("DelayedBump", async () =>
                {
                    await Task.Delay(120);
                    setCount(count + 10);
                }).AutomationId("btn-delayed")
            );
        }
    }

    private static DevtoolsFixtureRoot MountRoot(Harness h)
    {
        var host = h.CreateHost();
        var root = new DevtoolsFixtureRoot();
        host.Mount(root);
        return root;
    }

    // -- Fixtures ----------------------------------------------------------------

    internal sealed class VersionTool(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));
            var resp = await mcp.CallAsync("version");
            var result = Result(resp) ?? throw new Exception("missing result");

            H.Check("Devtools_Version_HasBuild",
                result.TryGetProperty("build", out var b) && b.ValueKind == JsonValueKind.String && b.GetString()!.Length > 0);
            H.Check("Devtools_Version_HasPid",
                result.TryGetProperty("pid", out var pid) && pid.GetInt32() > 0);
            H.Check("Devtools_Version_HasMcpPort",
                result.TryGetProperty("mcpPort", out var port) && port.GetInt32() > 0);
        }
    }

    internal sealed class ComponentsTool(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot),
                components: new[] { "Alpha", "Beta", nameof(DevtoolsFixtureRoot) });
            var resp = await mcp.CallAsync("components");
            var result = Result(resp) ?? throw new Exception("missing result");

            var names = result.GetProperty("components").EnumerateArray().Select(e => e.GetString()).ToArray();
            H.Check("Devtools_Components_ListsAllNames",
                names.Contains("Alpha") && names.Contains("Beta") && names.Contains(nameof(DevtoolsFixtureRoot)));
            H.Check("Devtools_Components_CurrentMatches",
                result.GetProperty("current").GetString() == nameof(DevtoolsFixtureRoot));
        }
    }

    internal sealed class WindowsTool(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));
            var resp = await mcp.CallAsync("windows");
            var result = Result(resp) ?? throw new Exception("missing result");

            var entries = result.GetProperty("windows").EnumerateArray().ToArray();
            H.Check("Devtools_Windows_HasEntry", entries.Length >= 1);
            var first = entries[0];
            H.Check("Devtools_Windows_EntryShape",
                first.TryGetProperty("id", out _) &&
                first.TryGetProperty("title", out _) &&
                first.TryGetProperty("bounds", out _) &&
                first.TryGetProperty("isMain", out var ismain) && ismain.GetBoolean());
        }
    }

    internal sealed class TreeSummary(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));
            var resp = await mcp.CallAsync("tree", new CallArgs());
            var result = Result(resp) ?? throw new Exception("missing result");

            H.Check("Devtools_Tree_SchemaPinned",
                result.GetProperty("$schema").GetString() == "reactor-tree/1");

            var nodes = result.GetProperty("nodes").EnumerateArray().ToArray();
            H.Check("Devtools_Tree_HasNodes", nodes.Length > 0);

            bool sawButton = nodes.Any(n =>
                n.TryGetProperty("automationId", out var aid) &&
                aid.ValueKind == JsonValueKind.String &&
                aid.GetString() == "btn-increment");
            H.Check("Devtools_Tree_FindsAutomationId", sawButton);

            bool allIdsScoped = nodes.All(n =>
                n.GetProperty("id").GetString()!.StartsWith("r:", StringComparison.Ordinal));
            H.Check("Devtools_Tree_IdsPrefixed", allIdsScoped);
        }
    }

    internal sealed class TreeFullView(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));
            var resp = await mcp.CallAsync("tree", new CallArgs { View = "full" });
            var result = Result(resp) ?? throw new Exception("missing result");

            var nodes = result.GetProperty("nodes").EnumerateArray().ToArray();
            // At least one node should carry full-view fields (layout info or desiredSize).
            bool anyFullField = nodes.Any(n =>
                n.TryGetProperty("layout", out var l) && l.ValueKind == JsonValueKind.Object);
            H.Check("Devtools_TreeFull_HasLayoutBlock", anyFullField);

            bool anyDesiredSize = nodes.Any(n =>
                n.TryGetProperty("desiredSize", out var d) && d.ValueKind == JsonValueKind.Object);
            H.Check("Devtools_TreeFull_HasDesiredSize", anyDesiredSize);
        }
    }

    internal sealed class TreeSelectorScope(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));
            var resp = await mcp.CallAsync("tree", new CallArgs { Selector = "#btn-increment" });
            var result = Result(resp) ?? throw new Exception("missing result");

            var nodes = result.GetProperty("nodes").EnumerateArray().ToArray();
            // Rooting at the button should produce just the button (+ optional visual children).
            H.Check("Devtools_TreeScope_NodeCountBounded", nodes.Length >= 1 && nodes.Length < 15);
            H.Check("Devtools_TreeScope_RootIsButton",
                nodes[0].GetProperty("type").GetString() == "Button");
        }
    }

    internal sealed class ClickInvokesButton(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            H.Check("Devtools_Click_InitialCount", H.FindText("count:0") is not null);

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));
            var resp = await mcp.CallAsync("click", new CallArgs { Selector = "#btn-increment" });
            var result = Result(resp) ?? throw new Exception("missing result");

            H.Check("Devtools_Click_ViaInvoke",
                result.GetProperty("via").GetString() == "invoke");

            await Harness.Render();
            H.Check("Devtools_Click_CountIncremented", H.FindText("count:1") is not null);
        }
    }

    internal sealed class TypeSetsTextBox(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));
            var resp = await mcp.CallAsync("type", new CallArgs { Selector = "#txt-input", Text = "hello", Clear = true });
            var result = Result(resp) ?? throw new Exception("missing result");

            H.Check("Devtools_Type_Ok", result.GetProperty("ok").GetBoolean());

            await Harness.Render();
            var tb = H.FindControl<TextBox>(x => AutomationProperties.GetAutomationId(x) == "txt-input");
            H.Check("Devtools_Type_TextApplied", tb is not null && tb.Text == "hello");

            // Append (clear false) should concatenate.
            await mcp.CallAsync("type", new CallArgs { Selector = "#txt-input", Text = "-world" });
            await Harness.Render();
            tb = H.FindControl<TextBox>(x => AutomationProperties.GetAutomationId(x) == "txt-input");
            H.Check("Devtools_Type_Appends", tb is not null && tb.Text == "hello-world");
        }
    }

    internal sealed class FocusElement(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));
            var resp = await mcp.CallAsync("focus", new CallArgs { Selector = "#btn-increment" });
            var result = Result(resp) ?? throw new Exception("missing result");

            // WinUI may decline focus if the control isn't yet visible to the
            // compositor — assert either an "ok: true" or a structured false.
            // The tool returns `{ ok: bool }`, never an error.
            H.Check("Devtools_Focus_ResponseShape",
                result.TryGetProperty("ok", out var ok) && ok.ValueKind is JsonValueKind.True or JsonValueKind.False);
        }
    }

    internal sealed class WaitForTextChange(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));

            // Click the delayed button; it bumps count by 10 after ~120ms.
            _ = await mcp.CallAsync("click", new CallArgs { Selector = "#btn-delayed" });

            var resp = await mcp.CallAsync("waitFor", new CallArgs
            {
                Predicate = new WaitPredicate("#count-label", "count:10"),
                TimeoutMs = 2000,
            });
            var result = Result(resp) ?? throw new Exception("missing result");

            H.Check("Devtools_WaitFor_Succeeded",
                result.TryGetProperty("ok", out var ok) && ok.GetBoolean());
            H.Check("Devtools_WaitFor_ReportedElapsed",
                result.TryGetProperty("elapsedMs", out var e) && e.ValueKind == JsonValueKind.Number);
        }
    }

    internal sealed class WaitForTimeout(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));
            // Predicate can never become true (count starts at 0, no mutation scheduled).
            var resp = await mcp.CallAsync("waitFor", new CallArgs
            {
                Predicate = new WaitPredicate("#count-label", "count:999"),
                TimeoutMs = 150,
            });
            var result = Result(resp) ?? throw new Exception("missing result");

            H.Check("Devtools_WaitFor_Timeout_NotOk",
                result.TryGetProperty("ok", out var ok) && !ok.GetBoolean());
            H.Check("Devtools_WaitFor_Timeout_Reason",
                result.TryGetProperty("reason", out var r) && r.GetString() == "timeout");
        }
    }

    internal sealed class ToggleFlipsCheckBox(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));
            var resp = await mcp.CallAsync("toggle", new CallArgs { Selector = "#chk-accept" });
            var result = Result(resp) ?? throw new Exception("missing result");

            H.Check("Devtools_Toggle_Ok", result.GetProperty("ok").GetBoolean());
            H.Check("Devtools_Toggle_StateOn",
                result.GetProperty("state").GetString() == "on");

            // Second toggle flips back off.
            resp = await mcp.CallAsync("toggle", new CallArgs { Selector = "#chk-accept" });
            result = Result(resp) ?? throw new Exception("missing result");
            H.Check("Devtools_Toggle_StateOff",
                result.GetProperty("state").GetString() == "off");
        }
    }

    internal sealed class InvokeDirectPattern(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));
            var resp = await mcp.CallAsync("invoke", new CallArgs { Selector = "#btn-increment" });
            var result = Result(resp) ?? throw new Exception("missing result");

            H.Check("Devtools_Invoke_Ok", result.GetProperty("ok").GetBoolean());
            await Harness.Render();
            H.Check("Devtools_Invoke_HandlerFired", H.FindText("count:1") is not null);

            // Calling invoke on a non-invokable element (the textbox) returns a structured error.
            resp = await mcp.CallAsync("invoke", new CallArgs { Selector = "#txt-input" });
            var err = Error(resp) ?? throw new Exception("expected error envelope");
            H.Check("Devtools_Invoke_NoPatternError",
                err.TryGetProperty("data", out var data) &&
                data.TryGetProperty("code", out var code) &&
                code.GetString() == "no-pattern");
        }
    }

    internal sealed class StateReadsHooks(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));

            // Initial read — count is 0.
            var resp = await mcp.CallAsync("state");
            var result = Result(resp) ?? throw new Exception("missing result");
            var hooks = result.GetProperty("hooks").EnumerateArray().ToArray();
            H.Check("Devtools_State_HasHooks", hooks.Length >= 3);

            // First useState is the count. Value is the raw primitive per §12.
            var firstHook = hooks[0];
            H.Check("Devtools_State_ComponentName",
                firstHook.GetProperty("component").GetString() == nameof(DevtoolsFixtureRoot));
            H.Check("Devtools_State_InitialCountZero",
                firstHook.GetProperty("value").GetInt32() == 0);

            // Mutate via click, re-read, observe new value.
            _ = await mcp.CallAsync("click", new CallArgs { Selector = "#btn-increment" });
            await Harness.Render();
            resp = await mcp.CallAsync("state");
            result = Result(resp) ?? throw new Exception("missing result");
            hooks = result.GetProperty("hooks").EnumerateArray().ToArray();
            H.Check("Devtools_State_CountReflectsClick",
                hooks[0].GetProperty("value").GetInt32() == 1);
        }
    }

    internal sealed class ScreenshotReturnsPng(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));
            var resp = await mcp.CallAsync("screenshot", new CallArgs());
            var result = Result(resp);
            if (result is null)
            {
                // Some CI hosts (headless or off-screen) may refuse PrintWindow —
                // record the condition instead of flapping the test. The response
                // shape is still the expected JSON-RPC error envelope.
                var err = Error(resp) ?? throw new Exception("no result and no error");
                H.Check("Devtools_Screenshot_ErrorHasCode",
                    err.TryGetProperty("code", out _));
                return;
            }

            H.Check("Devtools_Screenshot_BoundsReported",
                result.Value.TryGetProperty("bounds", out var b) && b.ValueKind == JsonValueKind.Object);

            var png = result.Value.GetProperty("png").GetString()!;
            H.Check("Devtools_Screenshot_PngNonEmpty", png.Length > 0);

            // Validate it parses as base64 and decodes to something PNG-ish (starts with 0x89 'PNG').
            var bytes = Convert.FromBase64String(png);
            H.Check("Devtools_Screenshot_PngMagic",
                bytes.Length > 8 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47);
        }
    }

    /// <summary>
    /// A component that mounts a ListView and a ScrollView of many items —
    /// targets for the <c>select</c> and <c>scroll</c> MCP tools. Kept separate
    /// from <see cref="DevtoolsFixtureRoot"/> so other fixtures stay lean.
    /// </summary>
    private sealed class ScrollAndSelectRoot : Component
    {
        public override Element Render()
        {
            var items = Enumerable.Range(0, 50)
                .Select(i => TextBlock($"row-{i}").AutomationId($"row-{i}") as Element)
                .ToArray();

            return VStack(
                ListView(
                    TextBlock("Alpha").AutomationId("item-alpha"),
                    TextBlock("Beta").AutomationId("item-beta"),
                    TextBlock("Gamma").AutomationId("item-gamma")
                ).AutomationId("lv-items"),
                ScrollView(VStack(items)).AutomationId("sv-items")
            );
        }
    }

    internal sealed class SelectListItem(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var root = new ScrollAndSelectRoot();
            host.Mount(root);
            await Harness.Render(50);

            using var mcp = new McpHarness(H.Window, () => root, nameof(ScrollAndSelectRoot));
            var resp = await mcp.CallAsync("select", new CallArgs
            {
                Selector = "#lv-items",
                ItemSelector = "#item-beta",
            });
            var result = Result(resp) ?? throw new Exception("missing result");

            H.Check("Devtools_Select_Ok", result.GetProperty("ok").GetBoolean());
            H.Check("Devtools_Select_Selected", result.GetProperty("selected").GetBoolean());

            // Restore default root so later fixtures using H.CreateHost()/MountRoot
            // start from a clean tree.
            H.SetContent(null);
        }
    }

    internal sealed class ScrollByAndInto(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var root = new ScrollAndSelectRoot();
            host.Mount(root);
            await Harness.Render(50);

            using var mcp = new McpHarness(H.Window, () => root, nameof(ScrollAndSelectRoot));

            // "to" path — should scroll a far row into view. The selftest window
            // may be sized such that all rows already fit; in that case the
            // ScrollItem call is a no-op but still returns ok.
            var resp = await mcp.CallAsync("scroll", new CallArgs
            {
                Selector = "#sv-items",
                To = "#row-40",
            });
            var result = Result(resp);
            // Some hosts expose ScrollItem only when the container actually
            // scrolls; accept either ok=true OR a structured no-pattern error
            // (the `to` codepath is still wired).
            if (result is not null)
            {
                H.Check("Devtools_ScrollTo_Ok", result.Value.GetProperty("ok").GetBoolean());
            }
            else
            {
                var err = Error(resp) ?? throw new Exception("expected result or error");
                H.Check("Devtools_ScrollTo_AcceptNoPattern",
                    err.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("code", out var code) &&
                    code.GetString() == "no-pattern");
            }

            // "by" path — shift vertical by some percent. Again the container
            // may not be scrollable in this layout; accept no-pattern.
            resp = await mcp.CallAsync("scroll", new CallArgs
            {
                Selector = "#sv-items",
                By = new ScrollByArg(0.0, 10.0),
            });
            result = Result(resp);
            if (result is not null)
            {
                H.Check("Devtools_ScrollBy_HasPosition",
                    result.Value.TryGetProperty("scrollPosition", out var pos) &&
                    pos.ValueKind == JsonValueKind.Object);
            }
            else
            {
                var err = Error(resp) ?? throw new Exception("expected result or error");
                // Accept either "no-pattern" (container doesn't implement
                // IScrollProvider at all) or "not-scrollable" (implements
                // the pattern but the requested axis isn't scrollable in the
                // current layout — e.g. content fits in the viewport). Both
                // are structured, actionable responses the agent can reason
                // about without catching raw COM exceptions.
                H.Check("Devtools_ScrollBy_AcceptNoPatternOrNotScrollable",
                    err.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("code", out var code) &&
                    (code.GetString() == "no-pattern" || code.GetString() == "not-scrollable"));
            }

            H.SetContent(null);
        }
    }

    internal sealed class LoggerWritesOneLinePerCall(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            var tempDir = global::System.IO.Path.Combine(
                global::System.IO.Path.GetTempPath(),
                "reactor-devtools-selftest",
                Guid.NewGuid().ToString("N"));
            using var logger = new DevtoolsLogger(tempDir, pid: Environment.ProcessId, DevtoolsLogLevel.Call);
            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot), logger: logger);

            // Spec §4.7: 100 tool calls produce 100 log lines with monotonic
            // timestamps and non-negative latencies. `version` is the cheapest
            // tool (no dispatcher hop) so the fixture finishes quickly even at
            // this count on slow CI hosts.
            const int Calls = 100;
            for (int i = 0; i < Calls; i++)
                _ = await mcp.CallAsync("version");

            // Force a flush via dispose before reading.
            logger.Dispose();

            var logFile = global::System.IO.Path.Combine(tempDir, $"{Environment.ProcessId}.log");
            H.Check("Devtools_Logging_FileExists", File.Exists(logFile));

            var lines = File.ReadAllLines(logFile);
            H.Check("Devtools_Logging_OneLinePerCall", lines.Length == Calls);

            // Every line is tab-separated with >=6 columns: ts, tool, selector, latency, status, code.
            bool shapeOk = lines.All(l =>
            {
                var parts = l.Split('\t');
                return parts.Length >= 6 &&
                       parts[1] == "version" &&
                       parts[3].EndsWith("ms", StringComparison.Ordinal) &&
                       parts[4] == "ok";
            });
            H.Check("Devtools_Logging_LineShape", shapeOk);

            // Timestamps parse and are monotonic (non-decreasing).
            bool monotonic = true;
            DateTime prev = DateTime.MinValue;
            foreach (var line in lines)
            {
                var ts = line.Split('\t')[0];
                if (!DateTime.TryParse(ts, null, global::System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                { monotonic = false; break; }
                if (parsed < prev) { monotonic = false; break; }
                prev = parsed;
            }
            H.Check("Devtools_Logging_MonotonicTimestamps", monotonic);

            // Latencies are non-negative integers.
            bool latencyOk = lines.All(l =>
            {
                var parts = l.Split('\t');
                if (parts.Length < 4) return false;
                var latencyStr = parts[3].TrimEnd('m', 's');
                return long.TryParse(latencyStr, out var ms) && ms >= 0;
            });
            H.Check("Devtools_Logging_NonNegativeLatency", latencyOk);

            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    internal sealed class UnknownSelectorStructuredError(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));
            var resp = await mcp.CallAsync("click", new CallArgs { Selector = "#does-not-exist" });
            var err = Error(resp) ?? throw new Exception("expected error envelope");

            H.Check("Devtools_Error_HasCode",
                err.TryGetProperty("code", out _));
            H.Check("Devtools_Error_StructuredData",
                err.TryGetProperty("data", out var data) &&
                data.TryGetProperty("code", out var ec) &&
                ec.GetString() == "unknown-selector");
        }
    }

    /// <summary>
    /// B5: a Reactor <c>Button("Increment", …)</c> is selectable via
    /// <c>[name='Increment']</c>, matching what <c>tree</c> reports as the
    /// button's text. Previously failed because WinUI doesn't auto-populate
    /// <see cref="AutomationProperties.Name"/> from string content.
    /// </summary>
    internal sealed class NameSelectorMatchesButtonContent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));
            var resp = await mcp.CallAsync("click", new CallArgs { Selector = "[name='Increment']" });
            var result = Result(resp) ?? throw new Exception("expected result; got " + resp);

            H.Check("Devtools_NameSelector_ClickOk", result.GetProperty("ok").GetBoolean());
            H.Check("Devtools_NameSelector_ViaInvoke",
                result.GetProperty("via").GetString() == "invoke");

            await Harness.Render();
            H.Check("Devtools_NameSelector_Incremented", H.FindText("count:1") is not null);
        }
    }

    /// <summary>
    /// B1 regression: two same-typed siblings under different parents must get
    /// distinct node ids. The previous bug collapsed all non-root ids to a
    /// single segment, so the two TextBoxes in this fixture collided and
    /// the tree had duplicate ids.
    /// </summary>
    private sealed class TwoTextBoxesRoot : Component
    {
        public override Element Render() => VStack(
            VStack(
                TextBlock("Name").AutomationId("lbl-name"),
                TextBox("a", _ => { }).AutomationId("tb-name")
            ),
            VStack(
                TextBlock("Email").AutomationId("lbl-email"),
                TextBox("b", _ => { }).AutomationId("tb-email")
            )
        );
    }

    internal sealed class TreeIdsUniqueAcrossSiblingsWithDifferentParents(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var root = new TwoTextBoxesRoot();
            host.Mount(root);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(TwoTextBoxesRoot));
            var resp = await mcp.CallAsync("tree", new CallArgs());
            var result = Result(resp) ?? throw new Exception("missing result");

            var ids = result.GetProperty("nodes").EnumerateArray()
                .Select(n => n.GetProperty("id").GetString()!)
                .ToArray();

            H.Check("Devtools_NodeIds_AllUnique",
                ids.Length == ids.Distinct(StringComparer.Ordinal).Count());

            H.SetContent(null);
        }
    }

    /// <summary>
    /// U6: <c>fire</c> must refuse lifecycle / hook-owned methods like
    /// <c>Render</c> to keep the reconciler's invariants. A raw invocation
    /// could corrupt hook state.
    /// </summary>
    internal sealed class FireRejectsLifecycleMethods(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));
            var resp = await mcp.CallAsync("fire", new CallArgs
            {
                Component = nameof(DevtoolsFixtureRoot),
                Event = "Render",
            });
            var err = Error(resp) ?? throw new Exception("expected error envelope");
            H.Check("Devtools_Fire_BlocksRender",
                err.TryGetProperty("data", out var data) &&
                data.TryGetProperty("code", out var code) &&
                code.GetString() == "forbidden-method");
        }
    }

    /// <summary>
    /// B6: <c>waitFor</c> returning <c>{ok:false, reason:"timeout"}</c> is a
    /// soft failure, but the rolling log was writing <c>ok</c> because the
    /// handler didn't throw. Inspect the log line for the call and assert the
    /// status column is <c>err</c>.
    /// </summary>
    internal sealed class WaitForTimeoutLoggedAsErr(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            var tempDir = global::System.IO.Path.Combine(
                global::System.IO.Path.GetTempPath(),
                "reactor-devtools-selftest",
                Guid.NewGuid().ToString("N"));
            using var logger = new DevtoolsLogger(tempDir, pid: Environment.ProcessId, DevtoolsLogLevel.Call);
            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot), logger: logger);

            var resp = await mcp.CallAsync("waitFor", new CallArgs
            {
                Predicate = new WaitPredicate("#count-label", "count:999"),
                TimeoutMs = 120,
            });
            var result = Result(resp) ?? throw new Exception("missing result");
            H.Check("Devtools_WaitForLog_ReturnsSoftFail", !result.GetProperty("ok").GetBoolean());

            logger.Dispose();

            var logFile = global::System.IO.Path.Combine(tempDir, $"{Environment.ProcessId}.log");
            var lines = File.ReadAllLines(logFile);
            var waitForLine = lines.FirstOrDefault(l => l.Split('\t') is { Length: >= 6 } c && c[1] == "waitFor");
            H.Check("Devtools_WaitForLog_HasEntry", waitForLine is not null);

            var parts = waitForLine!.Split('\t');
            H.Check("Devtools_WaitForLog_StatusIsErr", parts[4] == "err");

            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// §2.7 + §2.2 wiring: <c>switchComponent</c> invalidates every tree id
    /// for the window so an agent holding an id from before the swap sees
    /// <c>gone</c>, not a stale element.
    /// </summary>
    private sealed class AltRoot : Component
    {
        public override Element Render() => VStack(
            TextBlock("alt-root").AutomationId("lbl-alt")
        );
    }

    internal sealed class SwitchComponentInvalidatesIds(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var root = new DevtoolsFixtureRoot();
            host.Mount(root);
            await Harness.Render();

            bool DoSwitch(string name)
            {
                if (name == nameof(AltRoot))
                {
                    host.Mount(new AltRoot());
                    return true;
                }
                if (name == nameof(DevtoolsFixtureRoot))
                {
                    host.Mount(new DevtoolsFixtureRoot());
                    return true;
                }
                return false;
            }

            using var mcp = new McpHarness(
                H.Window,
                () => root,
                nameof(DevtoolsFixtureRoot),
                components: new[] { nameof(DevtoolsFixtureRoot), nameof(AltRoot) },
                switchComponent: DoSwitch);

            // First walk: populate the registry with ids for the initial tree.
            var firstTree = await mcp.CallAsync("tree", new CallArgs());
            var firstNodes = Result(firstTree)!.Value.GetProperty("nodes").EnumerateArray().ToArray();
            H.Check("Devtools_SwitchIds_FirstTreeNonEmpty", firstNodes.Length > 0);
            var firstId = firstNodes[0].GetProperty("id").GetString()!;

            // Swap component.
            var switchResp = await mcp.CallAsync("switchComponent", new CallArgs { Name = nameof(AltRoot) });
            H.Check("Devtools_SwitchIds_SwitchOk", Result(switchResp)!.Value.GetProperty("ok").GetBoolean());
            await Harness.Render();

            // Old id should now resolve as "gone", not silently reach a live element.
            var staleResp = await mcp.CallAsync("click", new CallArgs { Selector = firstId });
            var err = Error(staleResp) ?? throw new Exception("expected error envelope after invalidation");
            H.Check("Devtools_SwitchIds_OldIdGone",
                err.TryGetProperty("data", out var data) &&
                data.TryGetProperty("code", out var c) &&
                c.GetString() == "gone");

            H.SetContent(null);
        }
    }

    /// <summary>
    /// §3.11 open item: the <c>fire</c> tool's happy path on the root component.
    /// Unit tests cover error shapes and lifecycle rejection; this fixture
    /// confirms a real handler runs on the dispatcher and the response carries
    /// the <c>via: "reactor-event-injection"</c> tag. Kept separate from the
    /// <see cref="DevtoolsFixtureRoot"/> since that component's handlers are
    /// all lambdas (no named method surface for <c>fire</c> to bind to).
    /// </summary>
    private sealed class FireFixtureRoot : Component
    {
        private int _count;
        private Action<int>? _setCount;

        public override Element Render()
        {
            var (count, setCount) = UseState(0);
            _count = count;
            _setCount = setCount;
            return VStack(
                TextBlock($"count:{count}").AutomationId("count-label")
            );
        }

        // Named internal handler — the kind of method `fire` is meant to reach
        // when no UIA pattern exposes the behavior (custom gesture, awaited
        // test helper, etc.).
        internal void BumpCount() => _setCount?.Invoke(_count + 1);
    }

    internal sealed class FireInvokesNamedHandler(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var root = new FireFixtureRoot();
            host.Mount(root);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(FireFixtureRoot));
            var resp = await mcp.CallAsync("fire", new CallArgs
            {
                Component = nameof(FireFixtureRoot),
                Event = "BumpCount",
            });
            var result = Result(resp) ?? throw new Exception("missing result; got " + resp);

            H.Check("Devtools_Fire_Ok", result.GetProperty("ok").GetBoolean());
            H.Check("Devtools_Fire_ViaTag",
                result.GetProperty("via").GetString() == "reactor-event-injection");

            // Handler ran on the dispatcher — state bumped by 1 and the live
            // tree reflects it after the next render tick.
            await Harness.Render();
            H.Check("Devtools_Fire_HandlerFired", H.FindText("count:1") is not null);

            // Unknown event name on the root component returns a structured
            // error (code: unknown-event). Covered in unit tests too but worth
            // pinning in the self-host path so serialization round-trips.
            var errResp = await mcp.CallAsync("fire", new CallArgs
            {
                Component = nameof(FireFixtureRoot),
                Event = "NoSuchHandler",
            });
            var err = Error(errResp) ?? throw new Exception("expected error envelope");
            H.Check("Devtools_Fire_UnknownEvent",
                err.TryGetProperty("data", out var data) &&
                data.TryGetProperty("code", out var code) &&
                code.GetString() == "unknown-event");

            H.SetContent(null);
        }
    }

    private sealed class PropertyToolsRoot : Component
    {
        public override Element Render() => VStack(
            Border(
                Button("Property Target").AutomationId("prop-button") with
                {
                    Modifiers = new ElementModifiers
                    {
                        OnMountAction = fe =>
                        {
                            if (fe is not Button button) return;

                            button.Resources["DevtoolsElementBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);

                            var merged = new ResourceDictionary
                            {
                                ["DevtoolsMergedThickness"] = new Thickness(1, 2, 3, 4),
                            };
                            button.Resources.MergedDictionaries.Add(merged);

                            var theme = new ResourceDictionary
                            {
                                ["DevtoolsThemeCorner"] = new CornerRadius(3),
                            };
                            button.Resources.ThemeDictionaries.Add("Default", theme);

                            var basedOn = new Style(typeof(Button));
                            basedOn.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2)));

                            var style = new Style(typeof(Button))
                            {
                                BasedOn = basedOn,
                            };
                            style.Setters.Add(new Setter(Control.FontSizeProperty, 23.0));
                            style.Setters.Add(new Setter(Control.ForegroundProperty, new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Blue)));
                            button.Style = style;
                        },
                    },
                }
            ).AutomationId("prop-border")
        );
    }

    internal sealed class PropertyToolsExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var button = new Button
            {
                Content = "Property Target",
                Style = CreatePropertyButtonStyle(),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "prop-button");
            button.Resources["DevtoolsElementBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            button.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                ["DevtoolsMergedThickness"] = new Thickness(1, 2, 3, 4),
            });
            button.Resources.ThemeDictionaries.Add("Default", new ResourceDictionary
            {
                ["DevtoolsThemeCorner"] = new CornerRadius(3),
            });

            var border = new Border { Child = button };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(border, "prop-border");

            H.Check("Devtools_PropertyTools_Start", true);
            H.SetContent(border);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => null, nameof(PropertyToolsRoot));

            H.Check("Devtools_PropButton_Found", button is not null);

            var resources = Result(await mcp.CallAsync("resources", new CallArgs { Selector = "#prop-button", Scope = "element", Filter = "Devtools" }))
                ?? throw new Exception("missing resources result");
            var resourceKeys = resources.GetProperty("resources").EnumerateArray()
                .Select(r => r.GetProperty("key").GetString())
                .ToArray();
            H.Check("Devtools_Resources_ElementMergedTheme",
                resourceKeys.Contains("DevtoolsElementBrush")
                && resourceKeys.Contains("DevtoolsMergedThickness")
                && resourceKeys.Contains("DevtoolsThemeCorner"));

            var setElementResource = Result(await mcp.CallAsync("setResource", new CallArgs
            {
                Selector = "#prop-button",
                Scope = "element",
                Key = "DevtoolsSetElementThickness",
                Value = "6,7",
            })) ?? throw new Exception("missing setResource element result");
            H.Check("Devtools_SetResource_Element", setElementResource.GetProperty("ok").GetBoolean());

            var setWindowResource = Result(await mcp.CallAsync("setResource", new CallArgs
            {
                Selector = "#prop-button",
                Scope = "window",
                Key = "DevtoolsSetWindowBrush",
                Value = "#11223344",
            })) ?? throw new Exception("missing setResource window result");
            H.Check("Devtools_SetResource_Window", setWindowResource.GetProperty("ok").GetBoolean());

            var appKey = "DevtoolsSetAppResource_" + Guid.NewGuid().ToString("N");
            var setAppResourceResp = await mcp.CallAsync("setResource", new CallArgs
            {
                Scope = "application",
                Key = appKey,
                Value = "app-value",
                ConfirmAppWide = true,
            });
            H.Check("Devtools_SetResource_App",
                Result(setAppResourceResp) is { } setAppResource
                    ? setAppResource.GetProperty("ok").GetBoolean()
                    : Error(setAppResourceResp) is not null);
            Application.Current.Resources.Remove(appKey);

            var styles = Result(await mcp.CallAsync("styles", new CallArgs { Selector = "#prop-button" }))
                ?? throw new Exception("missing styles result");
            H.Check("Devtools_Styles_DescribesSetters",
                styles.GetProperty("hasStyle").GetBoolean()
                && styles.GetProperty("style").GetProperty("setterCount").GetInt32() >= 2
                && styles.GetProperty("style").TryGetProperty("basedOn", out var basedOn)
                && basedOn.ValueKind == JsonValueKind.Object);

            var ancestors = Result(await mcp.CallAsync("ancestors", new CallArgs { Selector = "#prop-button" }))
                ?? throw new Exception("missing ancestors result");
            H.Check("Devtools_Ancestors_WalksTree",
                ancestors.GetProperty("count").GetInt32() > 0
                && ancestors.GetProperty("ancestors").EnumerateArray().Any(a => a.GetProperty("type").GetString() == "Border"));

            H.SetContent(null);
        }

        private static Style CreatePropertyButtonStyle()
        {
            var basedOn = new Style(typeof(Button));
            basedOn.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2)));

            var style = new Style(typeof(Button))
            {
                BasedOn = basedOn,
            };
            style.Setters.Add(new Setter(Control.FontSizeProperty, 23.0));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Blue)));
            return style;
        }
    }

    /// <summary>
    /// Issue #1109: DependencyProperty *discovery* for the <c>properties</c> /
    /// <c>setProperty</c> MCP tools, asserted both end-to-end over JSON-RPC and
    /// directly against the two reflection helpers.
    /// <para>
    /// This lives apart from <see cref="PropertyToolsExercise"/> and
    /// <see cref="PropertyToolsReflectionExercise"/> for one reason: it is the only
    /// part of the property-tool surface that does not survive trimming, so it is the
    /// only part that has to be muted under NativeAOT
    /// (<c>SelfTestRunner.DefaultAotSkipPatterns</c>). Keeping it separate means the
    /// AOT-safe majority of those two fixtures — resources, styles, ancestors, value
    /// formatting and parsing — keeps running as live AOT coverage instead of being
    /// switched off as collateral.
    /// </para>
    /// </summary>
    internal sealed class PropertyToolsDpDiscovery(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var button = new Button
            {
                Content = "DP Target",
                // A distinctive local value for the by-name read below, so the assertion
                // doesn't depend on whatever the default/theme Padding happens to be.
                Padding = new Thickness(7, 8, 9, 10),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "dp-button");

            H.Check("Devtools_Dp_Start", true);
            H.SetContent(new Border { Child = button });
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => null, nameof(PropertyToolsRoot));

            // Each check reads its response through `is { }` rather than `?? throw`, so a
            // tool that regresses to an error envelope fails that one assertion instead
            // of aborting the fixture and hiding the rest.
            var allPropsResp = await mcp.CallAsync("properties", new CallArgs { Selector = "#dp-button" });
            var enumeratedNames = Result(allPropsResp) is { } allProps
                ? allProps.GetProperty("properties").EnumerateArray()
                    .Select(p => p.GetProperty("name").GetString())
                    .ToArray()
                : [];
            // Before the fix, `properties` returned {"count":0,"properties":[]} for every
            // element because DP discovery only looked at Type.GetField(s), while CsWinRT
            // projects WinUI DependencyProperty statics as static *properties*. These
            // names are the oracle: `Content` is declared on ContentControl and `Padding`
            // on Control, so finding both also proves the walk climbs the base chain
            // rather than stopping at Button.
            H.Check("Devtools_Dp_Enumerates",
                Result(allPropsResp) is { } props
                && props.GetProperty("count").GetInt32() == enumeratedNames.Length
                && enumeratedNames.Length > 0
                && enumeratedNames.Contains("Content")
                && enumeratedNames.Contains("Padding"));

            // The single-property path uses a different lookup (FindDependencyProperty by
            // name) than the enumeration, so it needs its own oracle.
            var singleReadResp = await mcp.CallAsync("properties", new CallArgs { Selector = "#dp-button", Name = "Padding" });
            H.Check("Devtools_Dp_ReadsSingleByName",
                Result(singleReadResp) is { } singleRead
                && singleRead.GetProperty("name").GetString() == "Padding"
                && singleRead.GetProperty("declaringType").GetString() == "Control"
                && singleRead.GetProperty("value").GetString() == "7,8,9,10"
                && singleRead.GetProperty("isLocal").GetBoolean());

            // Attached DP read: Grid.RowProperty exists only as a CsWinRT-projected static
            // property, never as a field, so this is the exact shape #1109 broke. Grid.Row
            // was never set, so it must read back as the DP's default of 0.
            var attachedPropResp = await mcp.CallAsync("properties", new CallArgs { Selector = "#dp-button", Name = "Grid.Row" });
            H.Check("Devtools_Dp_ReadAttached",
                Result(attachedPropResp) is { } attachedProp
                && attachedProp.GetProperty("name").GetString() == "Grid.Row"
                && attachedProp.GetProperty("value").GetString() == "0"
                && !attachedProp.GetProperty("isLocal").GetBoolean());

            // Assert off the live control, not the tool's own echo: a setProperty that
            // silently wrote nothing would still echo back ok:true for whatever it read.
            var setAttachedResp = await mcp.CallAsync("setProperty", new CallArgs { Selector = "#dp-button", Name = "Grid.Row", Value = "2" });
            H.Check("Devtools_Dp_SetAttached",
                Result(setAttachedResp) is { } setAttached
                && setAttached.GetProperty("ok").GetBoolean()
                && Microsoft.UI.Xaml.Controls.Grid.GetRow(button) == 2);

            var setDirectResp = await mcp.CallAsync("setProperty", new CallArgs { Selector = "#dp-button", Name = "Width", Value = "321" });
            H.Check("Devtools_Dp_SetDirect",
                Result(setDirectResp) is { } setDirect
                && setDirect.GetProperty("ok").GetBoolean()
                && button.Width == 321);

            // The by-name lookup must still reject genuinely absent DPs rather than
            // matching some unrelated static now that properties are in scope.
            var missingProp = await mcp.CallAsync("properties", new CallArgs { Selector = "#dp-button", Name = "NoSuchDevtoolsProperty" });
            H.Check("Devtools_Dp_UnknownNameErrors",
                Result(missingProp) is null
                && Error(missingProp) is { } missingErr
                && missingErr.GetProperty("message").GetString()!.Contains("NoSuchDevtoolsProperty", StringComparison.Ordinal));

            H.SetContent(null);

            // -- and the same two helpers, called directly ---------------------------

            var toolsType = typeof(DevtoolsPropertyTools);
            object? Invoke(string name, params object?[] args) =>
                toolsType.GetMethod(name, global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static)!
                    .Invoke(null, args);

            var bare = new Button { Width = 123 };

            // FindDependencyProperty must return the *real* Grid.RowProperty, not just
            // "something non-null": prove it by writing through the returned DP and
            // reading the value back with Grid.GetRow. The lookup throws McpToolException
            // when it finds nothing, so catch that and fail this one check rather than
            // aborting the fixture.
            (DependencyProperty Dp, global::System.Reflection.MemberInfo Member)? found = null;
            try
            {
                found = ((DependencyProperty, global::System.Reflection.MemberInfo))
                    Invoke("FindDependencyProperty", bare, "Grid.Row")!;
            }
            catch (global::System.Reflection.TargetInvocationException ex) when (ex.InnerException is McpToolException)
            {
                // Leave the tuple null; the H.Check below turns that into a red check.
            }
            if (found is { } attachedDp) bare.SetValue(attachedDp.Dp, 5);
            H.Check("Devtools_Dp_ReflectFindsAttached",
                found is { } resolved
                && Microsoft.UI.Xaml.Controls.Grid.GetRow(bare) == 5
                && resolved.Member.Name == "RowProperty"
                && resolved.Member.DeclaringType == typeof(Microsoft.UI.Xaml.Controls.Grid));

            bool missingDpThrows = false;
            try { Invoke("FindDependencyProperty", bare, "NoSuchDevtoolsProperty"); }
            catch (global::System.Reflection.TargetInvocationException ex) when (ex.InnerException is McpToolException)
            {
                missingDpThrows = true;
            }
            H.Check("Devtools_Dp_ReflectMissingThrows", missingDpThrows);

            // Enumeration must reach DP statics declared across the whole base chain
            // (Width on FrameworkElement, Content on ContentControl) and report each name
            // exactly once even though a DP can surface as both a field and a
            // CsWinRT-projected property.
            var enumerated = (List<PropertyResult>)Invoke("EnumerateDependencyProperties", bare)!;
            var reflectNames = enumerated.Select(p => p.Name).ToArray();
            H.Check("Devtools_Dp_ReflectEnumerates",
                reflectNames.Contains("Width")
                && reflectNames.Contains("Content")
                && reflectNames.Distinct().Count() == reflectNames.Length);

            // …and it must report live values, not just names: Width was set to 123 above.
            H.Check("Devtools_Dp_ReflectEnumeratesLiveValues",
                enumerated.SingleOrDefault(p => p.Name == "Width")
                    is { Value: "123", IsLocal: true, DeclaringType: "FrameworkElement" });

            // -- the C#-authored shape: a DP that really is a static *field* -----------
            //
            // Everything above runs against CsWinRT-projected static properties. The
            // fields arm is the pre-existing behaviour this change must not regress, and
            // no WinUI type can exercise it (typeof(Button) has zero DP-typed static
            // fields), so it needs a control that declares one.
            var fieldControl = new DevtoolsFieldDpControl { FieldOnly = "field-dp-live" };

            (DependencyProperty Dp, global::System.Reflection.MemberInfo Member)? fieldFound = null;
            try
            {
                fieldFound = ((DependencyProperty, global::System.Reflection.MemberInfo))
                    Invoke("FindDependencyProperty", fieldControl, "FieldOnly")!;
            }
            catch (global::System.Reflection.TargetInvocationException ex) when (ex.InnerException is McpToolException)
            {
                // Leave the tuple null; the H.Check below turns that into a red check.
            }
            H.Check("Devtools_Dp_FindsFieldDeclaredDp",
                fieldFound is { } fieldResolved
                && ReferenceEquals(fieldResolved.Dp, DevtoolsFieldDpControl.FieldOnlyProperty)
                && fieldResolved.Member is global::System.Reflection.FieldInfo
                && fieldResolved.Member.DeclaringType == typeof(DevtoolsFieldDpControl));

            var fieldEnumerated = (List<PropertyResult>)Invoke("EnumerateDependencyProperties", fieldControl)!;
            H.Check("Devtools_Dp_EnumeratesFieldDeclaredDp",
                fieldEnumerated.SingleOrDefault(p => p.Name == "FieldOnly")
                    is { Value: "field-dp-live", IsLocal: true, DeclaringType: nameof(DevtoolsFieldDpControl) });

            // -- reflection shapes the helper has to survive ---------------------------
            //
            // TryReadDependencyPropertyStatic takes a bare Type, so these can be plain
            // classes; they don't have to be UIElements.
            (DependencyProperty, global::System.Reflection.MemberInfo)? TryRead(Type t, string member) =>
                (global::System.ValueTuple<DependencyProperty, global::System.Reflection.MemberInfo>?)
                    Invoke("TryReadDependencyPropertyStatic", t, member);

            // A getter that throws must read back as "not found", not escape as a raw
            // TargetInvocationException through the MCP transport.
            H.Check("Devtools_Dp_ThrowingGetterIsNotFound",
                TryRead(typeof(AwkwardDpStatics), "ThrowingProperty") is null);

            // A write-only DP-typed static must be skipped before GetValue(null) is
            // reached — that call throws ArgumentException, which is deliberately NOT in
            // ReadStatic's catch list, so this reddens if the CanRead guard is dropped.
            H.Check("Devtools_Dp_WriteOnlyPropertyIsNotFound",
                TryRead(typeof(AwkwardDpStatics), "WriteOnlyProperty") is null);

            // `new`-hiding a base static DP member does not make reflection ambiguous —
            // the binder resolves to the most-derived declaration. Asserted for both
            // member kinds so the lookups aren't carrying a speculative catch: if a
            // future runtime ever does throw AmbiguousMatchException here, these redden.
            H.Check("Devtools_Dp_ShadowedPropertyResolvesToDerived",
                TryRead(typeof(ShadowedDpStatics), "ShadowedProperty") is { } shadowedProp
                && shadowedProp.Item2.DeclaringType == typeof(ShadowedDpStatics));

            H.Check("Devtools_Dp_ShadowedFieldResolvesToDerived",
                TryRead(typeof(ShadowedDpFields), "ShadowedProperty") is { } shadowedField
                && shadowedField.Item2.DeclaringType == typeof(ShadowedDpFields));

            // A member that exists but cannot be read must not claim the name: the
            // derived type here hides a readable base DP *field* with a static property
            // whose getter throws, and enumeration walks the derived type first. If the
            // failed read consumed "ShadowedDp", the base's readable field would be
            // skipped and the DP would disappear from the listing.
            var shadowEnumerated = (List<PropertyResult>)Invoke(
                "EnumerateDependencyProperties", new DevtoolsUnreadableShadowControl())!;
            H.Check("Devtools_Dp_UnreadableMemberDoesNotHideReadableOne",
                shadowEnumerated.SingleOrDefault(p => p.Name == "ShadowedDp")
                    is { Value: "readable-base-dp", DeclaringType: nameof(DevtoolsReadableBaseControl) });
        }

        /// <summary>Static DP members that misbehave when read reflectively.</summary>
        private class AwkwardDpStatics
        {
            public static DependencyProperty ThrowingProperty =>
                throw new InvalidOperationException("static DP getter blew up");

            public static DependencyProperty WriteOnlyProperty
            {
                set { _ = value; }
            }
        }

        /// <summary>Re-declares an inherited static property; the binder must pick the derived one.</summary>
        private sealed class ShadowedDpStatics : ShadowedDpStaticsBase
        {
            public static new DependencyProperty ShadowedProperty => DevtoolsFieldDpControl.FieldOnlyProperty;
        }

        private class ShadowedDpStaticsBase
        {
            public static DependencyProperty ShadowedProperty => DevtoolsFieldDpControl.FieldOnlyProperty;
        }

        /// <summary>Same, as fields — the arm that resolves C#-authored DPs.</summary>
        private sealed class ShadowedDpFields : ShadowedDpFieldsBase
        {
            public static new readonly DependencyProperty ShadowedProperty = DevtoolsFieldDpControl.FieldOnlyProperty;
        }

        private class ShadowedDpFieldsBase
        {
            public static readonly DependencyProperty ShadowedProperty = DevtoolsFieldDpControl.FieldOnlyProperty;
        }
    }

    internal sealed class PropertyToolsReflectionExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var toolsType = typeof(DevtoolsPropertyTools);
            H.Check("Devtools_PropReflect_Start", true);
            object? Invoke(string name, params object?[] args) =>
                toolsType.GetMethod(name, global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static)!
                    .Invoke(null, args);

            var button = new Button
            {
                Width = 123,
                Margin = new Thickness(1, 2, 3, 4),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red),
            };

            // NOTE: DP *discovery* (FindDependencyProperty / EnumerateDependencyProperties)
            // is asserted by Devtools_PropertyToolsDpDiscovery, not here — it is the one
            // part of this surface that does not survive trimming, so it lives in a
            // fixture that is skipped under NativeAOT. What remains below is the
            // value-formatting / parsing / resource / style logic, which is AOT-safe.

            H.Check("Devtools_PropReflect_FormatValues",
                (string?)Invoke("FormatValue", button.Background) == "#FFFF0000"
                && (string?)Invoke("FormatValue", new Thickness(1, 2, 3, 4)) == "1,2,3,4"
                && (string?)Invoke("FormatValue", new CornerRadius(1, 2, 3, 4)) == "1,2,3,4"
                && (string?)Invoke("FormatValue", Microsoft.UI.Colors.Blue) == "#FF0000FF"
                && (string?)Invoke("FormatValue", 12.5) == "12.5");

            H.Check("Devtools_PropReflect_ParseValues",
                Invoke("ParseValue", "Collapsed", typeof(Visibility)) is Visibility.Collapsed
                && Invoke("ParseValue", "Right", typeof(HorizontalAlignment)) is HorizontalAlignment.Right
                && Invoke("ParseValue", "Bottom", typeof(VerticalAlignment)) is VerticalAlignment.Bottom
                && Invoke("ParseValue", "true", null) is true
                && Invoke("ParseValue", "1,2", typeof(Thickness)) is Thickness
                && Invoke("ParseValue", "3", typeof(CornerRadius)) is CornerRadius
                && Invoke("ParseValue", "#0f0", typeof(Microsoft.UI.Xaml.Media.Brush)) is Microsoft.UI.Xaml.Media.SolidColorBrush
                && Invoke("ParseValue", "42", typeof(int)) is 42
                && Invoke("ParseValue", "42.5", typeof(double)) is 42.5);

            var thicknessArgs = new object?[] { "5,6,7,8", null };
            var thicknessOk = (bool)Invoke("TryParseThickness", thicknessArgs)!;
            var cornerArgs = new object?[] { "1,2,3,4", null };
            var cornerOk = (bool)Invoke("TryParseCornerRadius", cornerArgs)!;
            var colorArgs = new object?[] { "#11223344", null };
            var colorOk = (bool)Invoke("TryParseColor", colorArgs)!;
            H.Check("Devtools_PropReflect_TryParse", thicknessOk && cornerOk && colorOk);

            var dict = new ResourceDictionary
            {
                ["ReflectBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green),
            };
            dict.MergedDictionaries.Add(new ResourceDictionary
            {
                ["ReflectMerged"] = new Thickness(2),
            });
            dict.ThemeDictionaries.Add("Default", new ResourceDictionary
            {
                ["ReflectTheme"] = new CornerRadius(4),
            });
            var resources = new List<ResourceEntry>();
            Invoke(
                "CollectResources",
                dict,
                "element",
                new global::System.Text.RegularExpressions.Regex("Reflect", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                resources);
            H.Check("Devtools_PropReflect_CollectResources", resources.Count == 3);

            var baseStyle = new Style(typeof(Button));
            baseStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2)));
            var style = new Style(typeof(Button)) { BasedOn = baseStyle };
            style.Setters.Add(new Setter(Control.FontSizeProperty, 21.0));
            var description = Invoke("DescribeStyle", style);
            H.Check("Devtools_PropReflect_DescribeStyle", description is not null);

            bool invalidParseThrows = false;
            try { Invoke("ParseValue", "not-a-number", typeof(double)); }
            catch (global::System.Reflection.TargetInvocationException ex) when (ex.InnerException is McpToolException)
            {
                invalidParseThrows = true;
            }
            H.Check("Devtools_PropReflect_InvalidParseThrows", invalidParseThrows);

            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// U7: standard MCP clients hit <c>initialize</c> first. The server must
    /// respond with a well-formed handshake (protocol version + capabilities
    /// + server info) so the client doesn't bail.
    /// </summary>
    internal sealed class InitializeHandshake(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var root = MountRoot(H);
            await Harness.Render();

            using var mcp = new McpHarness(H.Window, () => root, nameof(DevtoolsFixtureRoot));

            var envelope = new McpInitializeEnvelope("2.0", 1, "initialize",
                new McpInitializeParams(
                    "2024-11-05",
                    new McpEmptyObject(),
                    new McpClientInfo("reactor-selftest", "1.0")));
            var body = JsonSerializer.Serialize(envelope, DevtoolsFixtureJsonContext.Default.McpInitializeEnvelope);
            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{mcp.Server.Port}/") };
            using var req = new HttpRequestMessage(HttpMethod.Post, "mcp")
            { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcp.Server.AuthToken);
            using var resp = await client.SendAsync(req);
            var text = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(text);
            var root2 = doc.RootElement;

            H.Check("Devtools_Initialize_HasResult",
                root2.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object);

            var r = root2.GetProperty("result");
            H.Check("Devtools_Initialize_ProtocolVersion",
                r.TryGetProperty("protocolVersion", out var pv) && pv.ValueKind == JsonValueKind.String);
            H.Check("Devtools_Initialize_Capabilities",
                r.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Object);
            H.Check("Devtools_Initialize_ServerInfo",
                r.TryGetProperty("serverInfo", out var info) && info.ValueKind == JsonValueKind.Object);
        }
    }

    internal sealed class McpServerProtocolEdges(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var projectId = "reactor-selftest-" + Guid.NewGuid().ToString("N");
            var lockfilePath = LockfileRegistry.PathFor(projectId);
            using var server = new DevtoolsMcpServer(
                H.Window.DispatcherQueue,
                H.Window,
                projectIdentifier: projectId);
            server.Tools.Register(
                new McpToolDescriptor("selftest.echo", "Echoes a value", new SchemaNode("object")),
                args => new global::System.Text.Json.Nodes.JsonObject
                {
                    ["ok"] = true,
                    ["value"] = args is { } a && a.TryGetProperty("value", out var value) ? value.GetString() : null,
                });
            server.Start();
            server.AnnounceReady();

            H.Check("Devtools_McpLockfileActive",
                LockfileRegistry.TryRead(lockfilePath, out var active) &&
                active is not null &&
                active.Token == server.AuthToken &&
                active.Port == server.Port);

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}/") };

            using var optionsReq = new HttpRequestMessage(HttpMethod.Options, "mcp");
            using var options = await client.SendAsync(optionsReq);
            H.Check("Devtools_McpOptions204", options.StatusCode == global::System.Net.HttpStatusCode.NoContent);

            using var missingPath = await client.GetAsync("missing");
            H.Check("Devtools_McpMissingPath404", missingPath.StatusCode == global::System.Net.HttpStatusCode.NotFound);

            using var unauthorized = await client.PostAsync("mcp", new StringContent("{}", Encoding.UTF8, "application/json"));
            H.Check("Devtools_McpUnauthorized401", unauthorized.StatusCode == global::System.Net.HttpStatusCode.Unauthorized);

            using var schemaReq = new HttpRequestMessage(HttpMethod.Get, "mcp");
            schemaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.AuthToken);
            using var schema = await client.SendAsync(schemaReq);
            var schemaText = await schema.Content.ReadAsStringAsync();
            H.Check("Devtools_McpSchemaGet200",
                schema.StatusCode == global::System.Net.HttpStatusCode.OK &&
                schemaText.Contains("reactor-devtools-mcp/1") &&
                schemaText.Contains("selftest.echo"));

            using var methodReq = new HttpRequestMessage(HttpMethod.Put, "mcp");
            methodReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.AuthToken);
            using var method = await client.SendAsync(methodReq);
            H.Check("Devtools_McpMethod405", method.StatusCode == global::System.Net.HttpStatusCode.MethodNotAllowed);

            using var typeReq = new HttpRequestMessage(HttpMethod.Post, "mcp")
            {
                Content = new StringContent("{}", Encoding.UTF8, "text/plain"),
            };
            typeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.AuthToken);
            using var type = await client.SendAsync(typeReq);
            H.Check("Devtools_McpContentType415", type.StatusCode == global::System.Net.HttpStatusCode.UnsupportedMediaType);

            using var originReq = new HttpRequestMessage(HttpMethod.Post, "mcp")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
            originReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.AuthToken);
            originReq.Headers.TryAddWithoutValidation("Origin", "http://localhost.evil.com");
            using var origin = await client.SendAsync(originReq);
            H.Check("Devtools_McpBadOrigin403", origin.StatusCode == global::System.Net.HttpStatusCode.Forbidden);

            using var largeReq = new HttpRequestMessage(HttpMethod.Post, "mcp")
            {
                Content = new ByteArrayContent(new byte[DevtoolsMcpServer.MaxRequestBodyBytes + 1]),
            };
            largeReq.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            largeReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.AuthToken);
            using var large = await client.SendAsync(largeReq);
            H.Check("Devtools_McpLarge413", large.StatusCode == global::System.Net.HttpStatusCode.RequestEntityTooLarge);

            var envelope = new McpCallEnvelope("2.0", 1, "tools/call",
                new McpCallParams("selftest.echo", new CallArgs { Value = "pong" }));
            using var validReq = new HttpRequestMessage(HttpMethod.Post, "mcp")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(envelope, DevtoolsFixtureJsonContext.Default.McpCallEnvelope),
                    Encoding.UTF8, "application/json"),
            };
            validReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.AuthToken);
            using var valid = await client.SendAsync(validReq);
            var validText = await valid.Content.ReadAsStringAsync();
            H.Check("Devtools_McpPostDispatch200",
                valid.StatusCode == global::System.Net.HttpStatusCode.OK && validText.Contains("pong"));

            using var badHostReq = new HttpRequestMessage(HttpMethod.Get, "mcp");
            badHostReq.Headers.Host = $"example.com:{server.Port}";
            badHostReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.AuthToken);
            using var badHost = await client.SendAsync(badHostReq);
            H.Check("Devtools_McpBadHost421", (int)badHost.StatusCode == 421);

            var capped = DevtoolsMcpServer.ReadCappedBody(new MemoryStream(Encoding.UTF8.GetBytes("ok")), Encoding.UTF8, cap: 2);
            H.Check("Devtools_McpReadCappedSmall", capped == "ok");

            bool cappedThrows = false;
            try
            {
                _ = DevtoolsMcpServer.ReadCappedBody(new MemoryStream(Encoding.UTF8.GetBytes("toolarge")), Encoding.UTF8, cap: 3);
            }
            catch (InvalidDataException)
            {
                cappedThrows = true;
            }
            H.Check("Devtools_McpReadCappedThrows", cappedThrows);

            server.Dispose();
            H.Check("Devtools_McpLockfileRemoved",
                !LockfileRegistry.TryRead(lockfilePath, out _));
        }
    }
}

// JSON-RPC request + tool-argument types for McpHarness.CallAsync. Named (not
// anonymous) so serialization goes through the System.Text.Json source generator
// and stays NativeAOT/trim-safe. CamelCase + WhenWritingNull match the previous
// DevtoolsMcpServer.JsonOpts serialization byte-for-byte.
internal sealed record McpCallEnvelope(string Jsonrpc, int Id, string Method, McpCallParams Params);

internal sealed record McpCallParams(string Name, CallArgs? Arguments);

/// <summary>
/// The union of every argument any devtools tool call in these fixtures sends.
/// All members are nullable and omitted when null, so each call site sets only
/// the fields the tool it targets needs — the source-generated equivalent of the
/// previous per-call anonymous <c>arguments</c> objects.
/// </summary>
internal sealed record CallArgs
{
    public string? Selector { get; init; }
    public string? ItemSelector { get; init; }
    public string? Text { get; init; }
    public bool? Clear { get; init; }
    public string? View { get; init; }
    public string? Name { get; init; }
    public string? Value { get; init; }
    public string? Scope { get; init; }
    public string? Filter { get; init; }
    public string? Key { get; init; }
    public bool? ConfirmAppWide { get; init; }
    public string? To { get; init; }
    public ScrollByArg? By { get; init; }
    public string? Component { get; init; }
    public string? Event { get; init; }
    public WaitPredicate? Predicate { get; init; }
    public int? TimeoutMs { get; init; }
}

/// <summary>Relative scroll delta for the <c>scroll</c> tool's <c>by</c> argument.</summary>
internal sealed record ScrollByArg(double Horizontal, double Vertical);

/// <summary>Predicate for the <c>waitFor</c> tool: wait until <c>Selector</c>'s text equals <c>TextEquals</c>.</summary>
internal sealed record WaitPredicate(string Selector, string TextEquals);

/// <summary>The <c>initialize</c> JSON-RPC request the standard-MCP-handshake fixture sends.</summary>
internal sealed record McpInitializeEnvelope(string Jsonrpc, int Id, string Method, McpInitializeParams Params);

internal sealed record McpInitializeParams(string ProtocolVersion, McpEmptyObject Capabilities, McpClientInfo ClientInfo);

internal sealed record McpClientInfo(string Name, string Version);

/// <summary>An empty JSON object (<c>{}</c>) — e.g. <c>capabilities</c>.</summary>
internal sealed record McpEmptyObject;

/// <summary>
/// A control whose DependencyProperty is declared the C# way — a
/// <c>public static readonly</c> <b>field</b> — rather than as the CsWinRT-projected
/// static property WinUI types expose. Used by
/// <see cref="DevtoolsFixtures.PropertyToolsDpDiscovery"/> to prove the fields arm of
/// DP discovery still works after issue #1109 taught it to read properties too; no
/// built-in WinUI type can cover that arm.
/// <para>
/// Declared at namespace scope, not nested in the fixture, because CsWinRT1028
/// requires every enclosing type of a WinRT-derived class to be <c>partial</c>.
/// </para>
/// </summary>
internal sealed partial class DevtoolsFieldDpControl : Control
{
    public static readonly DependencyProperty FieldOnlyProperty =
        DependencyProperty.Register(
            "FieldOnly",
            typeof(string),
            typeof(DevtoolsFieldDpControl),
            new PropertyMetadata("field-dp-default"));

    public string FieldOnly
    {
        get => (string)GetValue(FieldOnlyProperty);
        set => SetValue(FieldOnlyProperty, value);
    }
}

/// <summary>
/// Declares a readable DP field that <see cref="DevtoolsUnreadableShadowControl"/>
/// hides with an unreadable static property of the same name.
/// </summary>
internal partial class DevtoolsReadableBaseControl : Control
{
    public static readonly DependencyProperty ShadowedDpProperty =
        DependencyProperty.Register(
            "ShadowedDp",
            typeof(string),
            typeof(DevtoolsReadableBaseControl),
            new PropertyMetadata("readable-base-dp"));
}

/// <summary>
/// Hides the base's readable DP field with a static property whose getter throws.
/// Enumeration walks the derived type first, so if a failed read were allowed to
/// claim the name, the base's perfectly readable field would be skipped and the DP
/// would vanish from the listing — the ordering bug this shape pins.
/// </summary>
internal sealed partial class DevtoolsUnreadableShadowControl : DevtoolsReadableBaseControl
{
    public static new DependencyProperty ShadowedDpProperty =>
        throw new InvalidOperationException("shadowing DP getter blew up");
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(McpCallEnvelope))]
[JsonSerializable(typeof(McpInitializeEnvelope))]
internal partial class DevtoolsFixtureJsonContext : JsonSerializerContext;
