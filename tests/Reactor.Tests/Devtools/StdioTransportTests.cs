using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Stdio MCP transport tests. The loop is transport-shaped around an
/// in-memory TextReader/TextWriter so we can exercise framing, EOF, and
/// request/response parity without spawning a child process. The deeper
/// parity pass (running the Phase 2+3 self-host suite over stdio) lands
/// with §4.7.
/// </summary>
public class StdioTransportTests
{
    private static McpDispatcher BuildDispatcher()
    {
        var reg = new McpToolRegistry();
        reg.Register(
            new McpToolDescriptor("ping", "", new SchemaNode("object")),
            _ => new JsonObject { ["ok"] = true });
        reg.Register(
            new McpToolDescriptor("echo", "", new SchemaNode("object")),
            p => new JsonObject { ["echoed"] = DevtoolsTools.ReadString(p, "text") });
        return new McpDispatcher(reg);
    }

    [Fact]
    public void ProcessLine_ValidRequest_EmitsSingleLineJsonResponse()
    {
        var loop = new StdioMcpLoop(BuildDispatcher(), new StringReader(""), new StringWriter());
        var response = loop.ProcessLine(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"ping"}}""");

        // One-line JSON framing: no embedded newlines.
        Assert.DoesNotContain('\n', response);
        using var doc = JsonDocument.Parse(response);
        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32());
        Assert.True(doc.RootElement.GetProperty("result").GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void ProcessLine_MalformedRequest_ReturnsParseError()
    {
        var loop = new StdioMcpLoop(BuildDispatcher(), new StringReader(""), new StringWriter());
        var response = loop.ProcessLine("not json");
        using var doc = JsonDocument.Parse(response);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal(JsonRpcErrorCodes.ParseError, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public void Run_ProcessesAllLinesUntilEof()
    {
        // Three requests, each on its own line; the loop should emit three
        // response lines, preserving id order.
        var input =
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"ping"}}""" + "\n" +
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"echo","arguments":{"text":"hi"}}}""" + "\n" +
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"ping"}}""" + "\n";

        var writer = new StringWriter();
        var loop = new StdioMcpLoop(BuildDispatcher(), new StringReader(input), writer);
        loop.Run(CancellationToken.None);

        var lines = writer.ToString()
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            using var doc = JsonDocument.Parse(lines[i]);
            Assert.Equal(i + 1, doc.RootElement.GetProperty("id").GetInt32());
        }
    }

    [Fact]
    public void Run_BlankLinesAreSkipped()
    {
        var input = "\n\n" +
            """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"ping"}}""" + "\n\n";
        var writer = new StringWriter();
        var loop = new StdioMcpLoop(BuildDispatcher(), new StringReader(input), writer);
        loop.Run(CancellationToken.None);

        var lines = writer.ToString()
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        using var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal(7, doc.RootElement.GetProperty("id").GetInt32());
    }

    [Fact]
    public void Run_ExitsCleanlyWhenCancellationFiresBeforeRead()
    {
        // A pre-cancelled token terminates the loop without reading a line.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var writer = new StringWriter();
        var loop = new StdioMcpLoop(
            BuildDispatcher(),
            new StringReader("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"ping"}}""" + "\n"),
            writer);
        loop.Run(cts.Token);
        Assert.Empty(writer.ToString().Trim());
    }

    [Fact]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: reflection-based System.Text.Json serialization of a devtools/MCP dispatch payload (no source-gen context; DevtoolsMcpServer.JsonOpts). Issue #70 documents this devtools JSON surface as RUC/RDC-by-design and not-yet-AOT-clean; standard `dotnet test` is JIT. Behaviour-neutral.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test-only: reflection-based System.Text.Json serialization of a devtools/MCP payload (see IL2026). JIT only, not AOT-compiled. Behaviour-neutral.")]
    public void ResponseShape_MatchesHttpTransport()
    {
        // Parity: an identical dispatcher fed the same request line through
        // the stdio loop and the HTTP-path dispatcher must produce identical
        // response JSON. This keeps the transports honest.
        var request = """{"jsonrpc":"2.0","id":99,"method":"tools/call","params":{"name":"echo","arguments":{"text":"same"}}}""";

        var stdioLoop = new StdioMcpLoop(BuildDispatcher(), new StringReader(""), new StringWriter());
        var viaStdio = stdioLoop.ProcessLine(request);
        var viaHttp = JsonSerializer.Serialize(BuildDispatcher().Dispatch(request), DevtoolsMcpServer.JsonOpts);

        Assert.Equal(viaHttp, viaStdio);
    }

    // §4.1 / §4.7: broader parity across the method classes the dispatcher
    // handles — initialize handshake, tools/list, tools/call (ok + error),
    // resources/list, prompts/list, and direct-name invocation. Running the
    // full Phase 2/3 self-host suite over stdio still lands with §4.7, but
    // this pins the wire-shape equivalence at the unit level so the two
    // transports can't silently diverge.
    [Theory]
    [InlineData("initialize-handshake",
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""")]
    [InlineData("tools-list",
        """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""")]
    [InlineData("tools-call-ok",
        """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"ping"}}""")]
    [InlineData("tools-call-with-args",
        """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"echo","arguments":{"text":"parity"}}}""")]
    [InlineData("tools-call-unknown",
        """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"does-not-exist"}}""")]
    [InlineData("resources-list-stub",
        """{"jsonrpc":"2.0","id":6,"method":"resources/list"}""")]
    [InlineData("prompts-list-stub",
        """{"jsonrpc":"2.0","id":7,"method":"prompts/list"}""")]
    [InlineData("direct-name-invocation",
        """{"jsonrpc":"2.0","id":8,"method":"ping"}""")]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: reflection-based System.Text.Json serialization of a devtools/MCP dispatch payload (no source-gen context; DevtoolsMcpServer.JsonOpts). Issue #70 documents this devtools JSON surface as RUC/RDC-by-design and not-yet-AOT-clean; standard `dotnet test` is JIT. Behaviour-neutral.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test-only: reflection-based System.Text.Json serialization of a devtools/MCP payload (see IL2026). JIT only, not AOT-compiled. Behaviour-neutral.")]
    public void StdioAndHttpResponses_ByteIdentical_ForEveryMethodClass(string label, string request)
    {
        _ = label; // documented in the InlineData; shown in test output on failure

        var stdioLoop = new StdioMcpLoop(BuildDispatcher(), new StringReader(""), new StringWriter());
        var viaStdio = stdioLoop.ProcessLine(request);
        var viaHttp = JsonSerializer.Serialize(BuildDispatcher().Dispatch(request), DevtoolsMcpServer.JsonOpts);

        Assert.Equal(viaHttp, viaStdio);
    }

    [Fact]
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: reflection-based System.Text.Json serialization of a devtools/MCP dispatch payload (no source-gen context; DevtoolsMcpServer.JsonOpts). Issue #70 documents this devtools JSON surface as RUC/RDC-by-design and not-yet-AOT-clean; standard `dotnet test` is JIT. Behaviour-neutral.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test-only: reflection-based System.Text.Json serialization of a devtools/MCP payload (see IL2026). JIT only, not AOT-compiled. Behaviour-neutral.")]
    public void StdioRun_FeedingMixedSequence_ProducesHttpIdenticalPerLine()
    {
        // A realistic agent session: initialize, list tools, call a tool,
        // hit an error path. The stdio loop's per-line response must match
        // what the HTTP dispatcher would produce for each request in turn.
        var requests = new[]
        {
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""",
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""",
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"echo","arguments":{"text":"seq"}}}""",
            """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"missing"}}""",
        };

        var writer = new StringWriter();
        var stdioLoop = new StdioMcpLoop(
            BuildDispatcher(),
            new StringReader(string.Join('\n', requests) + "\n"),
            writer);
        stdioLoop.Run(CancellationToken.None);

        var actualLines = writer.ToString()
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(requests.Length, actualLines.Length);

        for (int i = 0; i < requests.Length; i++)
        {
            var expected = JsonSerializer.Serialize(BuildDispatcher().Dispatch(requests[i]), DevtoolsMcpServer.JsonOpts);
            Assert.Equal(expected, actualLines[i]);
        }
    }
}
