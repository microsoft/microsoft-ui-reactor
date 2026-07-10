using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Contract tests for <see cref="DevtoolsLogger"/> and its integration with
/// <see cref="McpDispatcher"/>. Uses a per-test temp directory so rotation
/// artifacts don't leak between runs.
/// </summary>
public class LoggingTests : IDisposable
{
    private readonly string _tempDir;

    public LoggingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "reactor-devtools-logs-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public void LogCall_WritesOneLinePerCall()
    {
        using var logger = new DevtoolsLogger(_tempDir, pid: 1001, DevtoolsLogLevel.Call);
        for (int i = 0; i < 5; i++)
            logger.LogCall("tree", "#foo", latencyMs: i, success: true, resultCode: 0);
        logger.Dispose();

        var lines = File.ReadAllLines(logger.Path);
        Assert.Equal(5, lines.Length);
        foreach (var line in lines)
        {
            Assert.Contains("tree", line);
            Assert.Contains("#foo", line);
            Assert.Contains("ok", line);
        }
    }

    [Fact]
    public void LogCall_OffLevel_WritesNothing()
    {
        using var logger = new DevtoolsLogger(_tempDir, pid: 1002, DevtoolsLogLevel.Off);
        logger.LogCall("tree", "#foo", 5, success: true, resultCode: 0);
        logger.Dispose();

        // Off level skips directory creation too — the file should not exist.
        Assert.False(File.Exists(logger.Path));
    }

    [Fact]
    public void LogCall_ErrorLevel_KeepsOnlyFailures()
    {
        using var logger = new DevtoolsLogger(_tempDir, pid: 1003, DevtoolsLogLevel.Error);
        logger.LogCall("tree", "#foo", 5, success: true, resultCode: 0);
        logger.LogCall("click", "#bar", 8, success: false, resultCode: -32001);
        logger.LogCall("tree", "#baz", 3, success: true, resultCode: 0);
        logger.Dispose();

        var lines = File.ReadAllLines(logger.Path);
        var single = Assert.Single(lines);
        Assert.Contains("click", single);
        Assert.Contains("err", single);
    }

    [Fact]
    public void ParseLevel_HandlesCaseAndSynonyms()
    {
        Assert.Equal(DevtoolsLogLevel.Off, DevtoolsLogger.ParseLevel("off"));
        Assert.Equal(DevtoolsLogLevel.Off, DevtoolsLogger.ParseLevel("OFF"));
        Assert.Equal(DevtoolsLogLevel.Error, DevtoolsLogger.ParseLevel("error"));
        Assert.Equal(DevtoolsLogLevel.Call, DevtoolsLogger.ParseLevel("call"));
        Assert.Equal(DevtoolsLogLevel.Call, DevtoolsLogger.ParseLevel(null));
        Assert.Equal(DevtoolsLogLevel.Call, DevtoolsLogger.ParseLevel(""));
        Assert.Equal(DevtoolsLogLevel.Trace, DevtoolsLogger.ParseLevel("trace"));
        Assert.Equal(DevtoolsLogLevel.Call, DevtoolsLogger.ParseLevel("something-weird"));
    }

    [Fact]
    public void LineShape_TabSeparated_WithTimestampToolSelectorLatencyStatusCode()
    {
        // One line is a tab-separated record so a tail/grep workflow works.
        // Columns: ts, tool, selector, latency, ok|err, code
        using var logger = new DevtoolsLogger(_tempDir, pid: 1004, DevtoolsLogLevel.Call);
        logger.LogCall("tree", "r:main/btn-inc", 42, success: true, resultCode: 0);
        logger.Dispose();

        var line = File.ReadAllLines(logger.Path)[0];
        var cols = line.Split('\t');
        Assert.Equal(6, cols.Length);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$", cols[0]);
        Assert.Equal("tree", cols[1]);
        Assert.Equal("r:main/btn-inc", cols[2]);
        Assert.Equal("42ms", cols[3]);
        Assert.Equal("ok", cols[4]);
        Assert.Equal("0", cols[5]);
    }

    [Fact]
    public void Rotation_CreatesArchivesAndKeepsCurrentFresh()
    {
        // Drive rotation with a tiny line count but a helper that shovels
        // oversized filler — we bypass the 10 MB guard by asking the logger
        // to rotate directly through repeated large writes. We can't easily
        // write 10 MB without slow disk IO, so this test uses a bespoke
        // subclass-free helper: big selector strings that push the file over
        // the threshold predictably.
        using var logger = new DevtoolsLogger(_tempDir, pid: 1005, DevtoolsLogLevel.Call);
        var bigSelector = new string('x', 900); // ~900 bytes per line including truncation
        // Write ~12 MB worth of lines — enough for a full shift across 5 archives.
        // Per-line size after truncation is ~100 bytes (the cap), so we need lots.
        // Instead, reflect-invoke the internal rotate to sidestep large-IO test time.
        var rotateMethod = typeof(DevtoolsLogger).GetMethod(
            "Rotate",
            global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic)!;

        logger.LogCall("t1", "a", 1, true, 0);
        rotateMethod.Invoke(logger, null);
        logger.LogCall("t2", "b", 1, true, 0);
        rotateMethod.Invoke(logger, null);
        logger.LogCall("t3", "c", 1, true, 0);
        rotateMethod.Invoke(logger, null);
        logger.LogCall("t4", "d", 1, true, 0);
        rotateMethod.Invoke(logger, null);
        logger.LogCall("t5", "e", 1, true, 0);
        rotateMethod.Invoke(logger, null);
        logger.LogCall("t6", "f", 1, true, 0);
        logger.Dispose();

        // The active log now holds t6. The five archives .1..5 hold the
        // previous rotations newest-first.
        Assert.Contains("t6", File.ReadAllText(logger.Path));
        Assert.True(File.Exists(logger.Path + ".1"));
        Assert.True(File.Exists(logger.Path + ".5"));
        // No sixth archive — the rotation caps at 5.
        Assert.False(File.Exists(logger.Path + ".6"));

        _ = bigSelector; // silence unused-local warning; kept to document the size model.
    }

    [Fact]
    public void McpDispatcher_WithLogger_RecordsEachToolCall()
    {
        using var logger = new DevtoolsLogger(_tempDir, pid: 1006, DevtoolsLogLevel.Call);
        var reg = new McpToolRegistry();
        reg.Register(
            new McpToolDescriptor("ping", "", new SchemaNode("object")),
            _ => new JsonObject { ["ok"] = true });
        reg.Register(
            new McpToolDescriptor("explode", "", new SchemaNode("object")),
            _ => throw new McpToolException("boom", JsonRpcErrorCodes.ToolExecution));

        var dispatcher = new McpDispatcher(reg, logger);
        dispatcher.Dispatch("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"ping","arguments":{"selector":"#a"}}}""");
        dispatcher.Dispatch("""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"explode","arguments":{"selector":"#b"}}}""");
        logger.Dispose();

        var lines = File.ReadAllLines(logger.Path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("ping", lines[0]);
        Assert.Contains("#a", lines[0]);
        Assert.Contains("ok", lines[0]);
        Assert.Contains("explode", lines[1]);
        Assert.Contains("#b", lines[1]);
        Assert.Contains("err", lines[1]);
    }

    [Fact]
    public void Timestamps_AreMonotonic()
    {
        using var logger = new DevtoolsLogger(_tempDir, pid: 1007, DevtoolsLogLevel.Call);
        for (int i = 0; i < 20; i++)
            logger.LogCall("tree", null, i, true, 0);
        logger.Dispose();

        var lines = File.ReadAllLines(logger.Path);
        var timestamps = lines.Select(l => DateTime.Parse(l.Split('\t')[0], null, global::System.Globalization.DateTimeStyles.RoundtripKind)).ToArray();
        for (int i = 1; i < timestamps.Length; i++)
            Assert.True(timestamps[i] >= timestamps[i - 1], $"Line {i} went backwards in time.");
    }

    [Fact]
    public void Latencies_AreNonNegative()
    {
        using var logger = new DevtoolsLogger(_tempDir, pid: 1008, DevtoolsLogLevel.Call);
        logger.LogCall("tree", null, 0, true, 0);
        logger.LogCall("tree", null, 100, true, 0);
        logger.Dispose();

        var lines = File.ReadAllLines(logger.Path);
        foreach (var line in lines)
        {
            var cols = line.Split('\t');
            var latMs = int.Parse(cols[3].Replace("ms", ""));
            Assert.True(latMs >= 0);
        }
    }
}
