using System.Collections.Generic;
using System.Text.Json;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Wire-shape regression tests for the source-generated devtools result / error
/// records. These serialize through <see cref="DevtoolsMcpServer.JsonOpts"/> — the
/// exact options the MCP server uses — so a missing registration (which would throw
/// <see cref="System.NotSupportedException"/> now that the reflection fallback is
/// gone) or a camelCase/field-name drift surfaces here rather than on the wire.
/// </summary>
public class DevtoolsSerializationTests
{
    // -- components: Components is `object` (ComponentInfo[] | string[]) -----------

    [Fact]
    public void ComponentsResult_WithComponentInfoArray_Serializes()
    {
        var result = new ComponentsResult(
            new[] { new ComponentInfo("Demo", "Ns.Demo", false, true, "Ns") },
            Current: "Demo");

        var json = JsonSerializer.Serialize(result, DevtoolsMcpServer.JsonOpts);

        Assert.Contains("\"components\"", json);
        Assert.Contains("\"Demo\"", json);
        Assert.Contains("\"current\":\"Demo\"", json);
    }

    [Fact]
    public void ComponentsResult_WithStringArray_Serializes()
    {
        var result = new ComponentsResult(new[] { "Alpha", "Beta" }, Current: null);

        var json = JsonSerializer.Serialize(result, DevtoolsMcpServer.JsonOpts);

        Assert.Contains("\"components\":[\"Alpha\",\"Beta\"]", json);
        // Current is null -> omitted (WhenWritingNull).
        Assert.DoesNotContain("current", json);
    }

    // -- McpErrorData union: newly-added SelectorResolver payloads -----------------

    [Fact]
    public void McpErrorData_AmbiguousSelector_SerializesCandidates()
    {
        var data = new McpErrorData("ambiguous-selector", Candidates: new[] { "r:a", "r:b" });

        var json = JsonSerializer.Serialize(data, DevtoolsMcpServer.JsonOpts);

        Assert.Contains("\"code\":\"ambiguous-selector\"", json);
        Assert.Contains("\"candidates\":[\"r:a\",\"r:b\"]", json);
    }

    [Fact]
    public void McpErrorData_IndexOutOfRange_SerializesCount()
    {
        var data = new McpErrorData("index-out-of-range", Count: 3);

        var json = JsonSerializer.Serialize(data, DevtoolsMcpServer.JsonOpts);

        Assert.Contains("\"code\":\"index-out-of-range\"", json);
        Assert.Contains("\"count\":3", json);
    }

    [Fact]
    public void McpErrorData_OmitsUnsetFields()
    {
        var data = new McpErrorData("no-pattern", Pattern: "Invoke");

        var json = JsonSerializer.Serialize(data, DevtoolsMcpServer.JsonOpts);

        Assert.Contains("\"code\":\"no-pattern\"", json);
        Assert.Contains("\"pattern\":\"Invoke\"", json);
        // Every other union field is null and must be omitted.
        Assert.DoesNotContain("candidates", json);
        Assert.DoesNotContain("count", json);
        Assert.DoesNotContain("window", json);
    }

    // -- state ShapeValue: uncountable collection keeps explicit null count --------

    [Fact]
    public void ShapeValue_UncountableEnumerable_EmitsExplicitNullCount()
    {
        var shaped = DevtoolsStateTool.ShapeValue(Yielded());
        var json = shaped!.ToJsonString();

        Assert.Contains("\"kind\":\"collection\"", json);
        // A lazy sequence advertises no count; the member stays present as null
        // (matches the previous Dictionary<string,object?> wire).
        Assert.Contains("\"count\":null", json);
    }

    private static IEnumerable<int> Yielded()
    {
        yield return 1;
        yield return 2;
    }
}
