using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Structured-error contract tests for the <c>reactor.fire</c> resolver
/// (spec §3.10). The pure helpers (FindComponent, FindHandler, ExtractArgs)
/// are covered in <see cref="DevtoolsFireToolTests"/>; here we exercise the
/// <see cref="DevtoolsFireTool.ResolveTarget"/> wrapper that mirrors the
/// live tool path's error shapes.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: reflection-based System.Text.Json serialization of devtools/MCP payload objects (no source-gen context; DevtoolsMcpServer.JsonOpts). Issue #70 documents this devtools JSON surface as RUC/RDC-by-design and not-yet-AOT-clean; the standard `dotnet test` path is JIT (never trimmed). Behaviour-neutral.")]
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test-only: reflection-based System.Text.Json serialization of devtools/MCP payloads (see the IL2026 note). Standard test run is JIT, not AOT-compiled. Behaviour-neutral.")]
public class FireResolutionTests
{
    private sealed class CounterDemo : Component
    {
        public int Clicks;
        public void OnIncrement() => Clicks++;
        public override Element Render() => null!;
    }

    [Fact]
    public void ResolveTarget_RootAndHandlerMatch_ReturnsPair()
    {
        var root = new CounterDemo();
        var (instance, handler) = DevtoolsFireTool.ResolveTarget(root, "CounterDemo", "OnIncrement");
        Assert.Same(root, instance);
        Assert.Equal("OnIncrement", handler.Name);
    }

    [Fact]
    public void ResolveTarget_NullRoot_EmitsNotReady()
    {
        var ex = Assert.Throws<McpToolException>(() =>
            DevtoolsFireTool.ResolveTarget(null, "Any", "Handler"));
        Assert.Equal(JsonRpcErrorCodes.ToolExecution, ex.Code);
        Assert.Contains("\"code\":\"not-ready\"",
            JsonSerializer.Serialize(ex.Payload, DevtoolsMcpServer.JsonOpts));
    }

    [Fact]
    public void ResolveTarget_WrongComponent_EmitsUnknownComponent_WithAvailableList()
    {
        var root = new CounterDemo();
        var ex = Assert.Throws<McpToolException>(() =>
            DevtoolsFireTool.ResolveTarget(root, "SomeOther", "OnIncrement"));
        Assert.Equal(JsonRpcErrorCodes.ToolExecution, ex.Code);

        var payload = JsonSerializer.Serialize(ex.Payload, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"code\":\"unknown-component\"", payload);
        Assert.Contains("CounterDemo", payload); // root name surfaces in the available list
    }

    [Fact]
    public void ResolveTarget_UnknownEvent_EmitsUnknownEvent_WithContext()
    {
        var root = new CounterDemo();
        var ex = Assert.Throws<McpToolException>(() =>
            DevtoolsFireTool.ResolveTarget(root, "CounterDemo", "DoesNotExist"));
        Assert.Equal(JsonRpcErrorCodes.ToolExecution, ex.Code);

        var payload = JsonSerializer.Serialize(ex.Payload, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"code\":\"unknown-event\"", payload);
        Assert.Contains("\"component\":\"CounterDemo\"", payload);
        Assert.Contains("\"event\":\"DoesNotExist\"", payload);
    }

    [Fact]
    public void ResolveTarget_UnknownEvent_ListsReachableMethodsAndLambdaHint()
    {
        // feedback #9: agents using `fire` to call the handler wired to an
        // inline lambda hit "unknown-event" with no idea why. The error now
        // lists the declared methods on the component and explains inline
        // lambdas aren't reachable so the agent can correct course.
        var root = new CounterDemo();
        var ex = Assert.Throws<McpToolException>(() =>
            DevtoolsFireTool.ResolveTarget(root, "CounterDemo", "DoesNotExist"));
        var payload = JsonSerializer.Serialize(ex.Payload, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"reachableMethods\"", payload);
        Assert.Contains("OnIncrement", payload);
        Assert.Contains("inline-lambda", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveTarget_IsCaseInsensitiveOnComponentAndEvent()
    {
        var root = new CounterDemo();
        var (instance, handler) = DevtoolsFireTool.ResolveTarget(root, "counterdemo", "onincrement");
        Assert.Same(root, instance);
        Assert.Equal("OnIncrement", handler.Name);
    }

    [Theory]
    [InlineData("Render")]
    [InlineData("render")]
    [InlineData("OnInitialized")]
    [InlineData("UseState")]
    [InlineData("Dispose")]
    public void ResolveTarget_ForbiddenLifecycleMethod_Rejected(string forbidden)
    {
        var root = new CounterDemo();
        var ex = Assert.Throws<McpToolException>(() =>
            DevtoolsFireTool.ResolveTarget(root, "CounterDemo", forbidden));
        Assert.Equal(JsonRpcErrorCodes.ToolExecution, ex.Code);

        var payload = JsonSerializer.Serialize(ex.Payload, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"code\":\"forbidden-method\"", payload);
    }
}
