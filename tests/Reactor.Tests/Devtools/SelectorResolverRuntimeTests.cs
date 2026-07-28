using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Pure-path tests for <see cref="SelectorResolver"/>: the NodeId resolution
/// branch routes through <see cref="NodeRegistry"/> and doesn't touch a live
/// visual tree, so we can assert the structured-error contracts without a
/// self-host fixture. Tree-walk paths (AutomationId, TypePath) live in §2.17.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: reflection-based System.Text.Json serialization of devtools/MCP payload objects (no source-gen context; DevtoolsMcpServer.JsonOpts). Issue #70 documents this devtools JSON surface as RUC/RDC-by-design and not-yet-AOT-clean; the standard `dotnet test` path is JIT (never trimmed). Behaviour-neutral.")]
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test-only: reflection-based System.Text.Json serialization of devtools/MCP payloads (see the IL2026 note). Standard test run is JIT, not AOT-compiled. Behaviour-neutral.")]
public class SelectorResolverRuntimeTests
{
    [Fact]
    public void UnknownNodeId_ThrowsUnknownSelector()
    {
        var resolver = new SelectorResolver(new NodeRegistry(), new WindowRegistry("build-tag"));

        var ex = Assert.Throws<McpToolException>(() => resolver.Resolve("r:main/does-not-exist"));
        Assert.Equal(JsonRpcErrorCodes.ToolExecution, ex.Code);

        var payload = JsonSerializer.Serialize(ex.Payload, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"code\":\"unknown-selector\"", payload);
        Assert.Contains("\"id\":\"r:main/does-not-exist\"", payload);
    }

    [Fact]
    public void GoneNodeId_ReturnsStructuredGone()
    {
        var nodes = new NodeRegistry();
        // Inject and immediately invalidate — the lookup should now return Gone
        // via the tombstone path.
        var id = nodes.InjectForTests(NodeDescriptorForIdOnly("main", "Counter", "btn-inc"));
        nodes.InvalidateWindow("main");

        var resolver = new SelectorResolver(nodes, new WindowRegistry("build-tag"));
        var ex = Assert.Throws<McpToolException>(() => resolver.Resolve(id));

        var payload = JsonSerializer.Serialize(ex.Payload, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"code\":\"gone\"", payload);
    }

    [Fact]
    public void CrossWindowMismatch_ThrowsWindowMismatch()
    {
        var resolver = new SelectorResolver(new NodeRegistry(), new WindowRegistry("build-tag"));

        // Node id encodes window=win2, caller pins window=main — must fail
        // before any registry lookup since the mismatch is a caller bug.
        var ex = Assert.Throws<McpToolException>(() =>
            resolver.Resolve("r:win2/Counter.btn-inc", explicitWindowId: "main"));

        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.Code);
        var payload = JsonSerializer.Serialize(ex.Payload, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"code\":\"window-mismatch\"", payload);
        Assert.Contains("\"idWindow\":\"win2\"", payload);
        Assert.Contains("\"requested\":\"main\"", payload);
    }

    [Fact]
    public void NodeIdWithMatchingWindow_PassesThroughToRegistry()
    {
        var nodes = new NodeRegistry();
        var id = nodes.InjectForTests(NodeDescriptorForIdOnly("main", "Counter", "btn-inc"));
        var resolver = new SelectorResolver(nodes, new WindowRegistry("build-tag"));

        // Matching window doesn't short-circuit; it still hits the registry.
        // A live element would return here; our sentinel-backed entry resolves
        // to Found but exposes a non-UIElement target, so the resolver's cast
        // yields null — the error we surface in that path is Gone when the
        // target vanishes, otherwise unknown-selector for missing. Either
        // way, we should not get a window-mismatch.
        var ex = Record.Exception(() => resolver.Resolve(id, explicitWindowId: "main"));
        if (ex is McpToolException mte)
        {
            var payload = JsonSerializer.Serialize(mte.Payload, DevtoolsMcpServer.JsonOpts);
            Assert.DoesNotContain("window-mismatch", payload);
        }
    }

    [Theory]
    [InlineData("r:main/Counter.btn", "main")]
    [InlineData("r:win2/Counter.btn", "win2")]
    [InlineData("r:Window_3/foo", "Window_3")]
    [InlineData("r:/nothing", null)]        // empty window segment
    [InlineData("r:no-slash-here", null)]  // malformed
    [InlineData("#automation-id", null)]   // not a node id at all
    public void ExtractWindowFromNodeId_ParsesWindowSegment(string id, string? expected)
    {
        Assert.Equal(expected, SelectorResolver.ExtractWindowFromNodeId(id));
    }

    // -- helpers ----------------------------------------------------------------

    private static NodeDescriptor NodeDescriptorForIdOnly(string window, string component, string automationId) =>
        new(
            WindowId: window,
            ComponentName: component,
            AutomationId: automationId,
            ReactorSource: null,
            TypeName: "Button",
            SiblingIndex: 0,
            StableAncestor: null);
}
