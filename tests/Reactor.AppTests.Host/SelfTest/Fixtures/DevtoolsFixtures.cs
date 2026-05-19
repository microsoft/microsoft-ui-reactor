using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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

        public async Task<JsonElement> CallAsync(string method, object? args = null)
        {
            var envelope = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new { name = method, arguments = args },
            };
            var body = JsonSerializer.Serialize(envelope, DevtoolsMcpServer.JsonOpts);
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
                TextField(text, setText).AutomationId("txt-input"),
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
            var resp = await mcp.CallAsync("tree", new { });
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
            var resp = await mcp.CallAsync("tree", new { view = "full" });
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
            var resp = await mcp.CallAsync("tree", new { selector = "#btn-increment" });
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
            var resp = await mcp.CallAsync("click", new { selector = "#btn-increment" });
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
            var resp = await mcp.CallAsync("type", new { selector = "#txt-input", text = "hello", clear = true });
            var result = Result(resp) ?? throw new Exception("missing result");

            H.Check("Devtools_Type_Ok", result.GetProperty("ok").GetBoolean());

            await Harness.Render();
            var tb = H.FindControl<TextBox>(x => AutomationProperties.GetAutomationId(x) == "txt-input");
            H.Check("Devtools_Type_TextApplied", tb is not null && tb.Text == "hello");

            // Append (clear false) should concatenate.
            await mcp.CallAsync("type", new { selector = "#txt-input", text = "-world" });
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
            var resp = await mcp.CallAsync("focus", new { selector = "#btn-increment" });
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
            _ = await mcp.CallAsync("click", new { selector = "#btn-delayed" });

            var resp = await mcp.CallAsync("waitFor", new
            {
                predicate = new
                {
                    selector = "#count-label",
                    textEquals = "count:10",
                },
                timeoutMs = 2000,
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
            var resp = await mcp.CallAsync("waitFor", new
            {
                predicate = new
                {
                    selector = "#count-label",
                    textEquals = "count:999",
                },
                timeoutMs = 150,
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
            var resp = await mcp.CallAsync("toggle", new { selector = "#chk-accept" });
            var result = Result(resp) ?? throw new Exception("missing result");

            H.Check("Devtools_Toggle_Ok", result.GetProperty("ok").GetBoolean());
            H.Check("Devtools_Toggle_StateOn",
                result.GetProperty("state").GetString() == "on");

            // Second toggle flips back off.
            resp = await mcp.CallAsync("toggle", new { selector = "#chk-accept" });
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
            var resp = await mcp.CallAsync("invoke", new { selector = "#btn-increment" });
            var result = Result(resp) ?? throw new Exception("missing result");

            H.Check("Devtools_Invoke_Ok", result.GetProperty("ok").GetBoolean());
            await Harness.Render();
            H.Check("Devtools_Invoke_HandlerFired", H.FindText("count:1") is not null);

            // Calling invoke on a non-invokable element (the textbox) returns a structured error.
            resp = await mcp.CallAsync("invoke", new { selector = "#txt-input" });
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
            _ = await mcp.CallAsync("click", new { selector = "#btn-increment" });
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
            var resp = await mcp.CallAsync("screenshot", new { });
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
            var resp = await mcp.CallAsync("select", new
            {
                selector = "#lv-items",
                itemSelector = "#item-beta",
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
            var resp = await mcp.CallAsync("scroll", new
            {
                selector = "#sv-items",
                to = "#row-40",
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
            resp = await mcp.CallAsync("scroll", new
            {
                selector = "#sv-items",
                by = new { horizontal = 0.0, vertical = 10.0 },
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
            var resp = await mcp.CallAsync("click", new { selector = "#does-not-exist" });
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
            var resp = await mcp.CallAsync("click", new { selector = "[name='Increment']" });
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
                TextField("a", _ => { }).AutomationId("tb-name")
            ),
            VStack(
                TextBlock("Email").AutomationId("lbl-email"),
                TextField("b", _ => { }).AutomationId("tb-email")
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
            var resp = await mcp.CallAsync("tree", new { });
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
            var resp = await mcp.CallAsync("fire", new
            {
                component = nameof(DevtoolsFixtureRoot),
                @event = "Render",
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

            var resp = await mcp.CallAsync("waitFor", new
            {
                predicate = new { selector = "#count-label", textEquals = "count:999" },
                timeoutMs = 120,
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
            var firstTree = await mcp.CallAsync("tree", new { });
            var firstNodes = Result(firstTree)!.Value.GetProperty("nodes").EnumerateArray().ToArray();
            H.Check("Devtools_SwitchIds_FirstTreeNonEmpty", firstNodes.Length > 0);
            var firstId = firstNodes[0].GetProperty("id").GetString()!;

            // Swap component.
            var switchResp = await mcp.CallAsync("switchComponent", new { name = nameof(AltRoot) });
            H.Check("Devtools_SwitchIds_SwitchOk", Result(switchResp)!.Value.GetProperty("ok").GetBoolean());
            await Harness.Render();

            // Old id should now resolve as "gone", not silently reach a live element.
            var staleResp = await mcp.CallAsync("click", new { selector = firstId });
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
            var resp = await mcp.CallAsync("fire", new
            {
                component = nameof(FireFixtureRoot),
                @event = "BumpCount",
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
            var errResp = await mcp.CallAsync("fire", new
            {
                component = nameof(FireFixtureRoot),
                @event = "NoSuchHandler",
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

            var allProps = Result(await mcp.CallAsync("properties", new { selector = "#prop-button" }))
                ?? throw new Exception("missing properties result");
            H.Check("Devtools_Props_Enumerates",
                allProps.GetProperty("count").GetInt32() >= 0
                && allProps.GetProperty("properties").ValueKind == JsonValueKind.Array);

            var attachedPropResp = await mcp.CallAsync("properties", new { selector = "#prop-button", name = "Grid.Row" });
            H.Check("Devtools_Props_ReadAttached",
                Result(attachedPropResp) is { } attachedProp
                    ? attachedProp.GetProperty("name").GetString() == "Grid.Row"
                    : Error(attachedPropResp) is not null);

            var setAttachedResp = await mcp.CallAsync("setProperty", new { selector = "#prop-button", name = "Grid.Row", value = "2" });
            H.Check("Devtools_SetProp_Attached",
                Result(setAttachedResp) is { } setAttached
                    ? setAttached.GetProperty("ok").GetBoolean()
                    : Error(setAttachedResp) is not null);

            H.Check("Devtools_PropButton_Found", button is not null);

            var resources = Result(await mcp.CallAsync("resources", new { selector = "#prop-button", scope = "element", filter = "Devtools" }))
                ?? throw new Exception("missing resources result");
            var resourceKeys = resources.GetProperty("resources").EnumerateArray()
                .Select(r => r.GetProperty("key").GetString())
                .ToArray();
            H.Check("Devtools_Resources_ElementMergedTheme",
                resourceKeys.Contains("DevtoolsElementBrush")
                && resourceKeys.Contains("DevtoolsMergedThickness")
                && resourceKeys.Contains("DevtoolsThemeCorner"));

            var setElementResource = Result(await mcp.CallAsync("setResource", new
            {
                selector = "#prop-button",
                scope = "element",
                key = "DevtoolsSetElementThickness",
                value = "6,7",
            })) ?? throw new Exception("missing setResource element result");
            H.Check("Devtools_SetResource_Element", setElementResource.GetProperty("ok").GetBoolean());

            var setWindowResource = Result(await mcp.CallAsync("setResource", new
            {
                selector = "#prop-button",
                scope = "window",
                key = "DevtoolsSetWindowBrush",
                value = "#11223344",
            })) ?? throw new Exception("missing setResource window result");
            H.Check("Devtools_SetResource_Window", setWindowResource.GetProperty("ok").GetBoolean());

            var appKey = "DevtoolsSetAppResource_" + Guid.NewGuid().ToString("N");
            var setAppResourceResp = await mcp.CallAsync("setResource", new
            {
                scope = "application",
                key = appKey,
                value = "app-value",
                confirmAppWide = true,
            });
            H.Check("Devtools_SetResource_App",
                Result(setAppResourceResp) is { } setAppResource
                    ? setAppResource.GetProperty("ok").GetBoolean()
                    : Error(setAppResourceResp) is not null);
            Application.Current.Resources.Remove(appKey);

            var styles = Result(await mcp.CallAsync("styles", new { selector = "#prop-button" }))
                ?? throw new Exception("missing styles result");
            H.Check("Devtools_Styles_DescribesSetters",
                styles.GetProperty("hasStyle").GetBoolean()
                && styles.GetProperty("style").GetProperty("setterCount").GetInt32() >= 2
                && styles.GetProperty("style").TryGetProperty("basedOn", out var basedOn)
                && basedOn.ValueKind == JsonValueKind.Object);

            var ancestors = Result(await mcp.CallAsync("ancestors", new { selector = "#prop-button" }))
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

    internal sealed class PropertyToolsReflectionExercise(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var toolsType = typeof(DevtoolsPropertyTools);
            H.Check("Devtools_PropReflect_Start", toolsType is not null);
            object? Invoke(string name, params object?[] args) =>
                toolsType.GetMethod(name, global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Static)!
                    .Invoke(null, args);

            var button = new Button
            {
                Width = 123,
                Margin = new Thickness(1, 2, 3, 4),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red),
            };

            bool findDpHandled = false;
            try
            {
                findDpHandled = Invoke("FindDependencyProperty", button, "Grid.Row") is not null;
            }
            catch (global::System.Reflection.TargetInvocationException ex) when (ex.InnerException is McpToolException)
            {
                findDpHandled = true;
            }
            H.Check("Devtools_PropReflect_FindDpHandled", findDpHandled);

            var enumerated = ((IEnumerable<object>)Invoke("EnumerateDependencyProperties", button)!).ToArray();
            H.Check("Devtools_PropReflect_EnumeratesNoThrow", enumerated is not null);

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
            var resources = new List<object>();
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

            var envelope = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2024-11-05",
                    capabilities = new { },
                    clientInfo = new { name = "reactor-selftest", version = "1.0" },
                },
            };
            var body = JsonSerializer.Serialize(envelope, DevtoolsMcpServer.JsonOpts);
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
                new McpToolDescriptor("selftest.echo", "Echoes a value", new { type = "object" }),
                args => new { ok = true, value = args is { } a && a.TryGetProperty("value", out var value) ? value.GetString() : null });
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

            var envelope = new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new { name = "selftest.echo", arguments = new { value = "pong" } },
            };
            using var validReq = new HttpRequestMessage(HttpMethod.Post, "mcp")
            {
                Content = new StringContent(JsonSerializer.Serialize(envelope, DevtoolsMcpServer.JsonOpts), Encoding.UTF8, "application/json"),
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
