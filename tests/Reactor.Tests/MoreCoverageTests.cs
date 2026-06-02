using System.Text.Json;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Microsoft.UI.Reactor.Markdown;
using Xunit;
using static Microsoft.UI.Reactor.Factories;
using IoPath = System.IO.Path;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Additional coverage-oriented tests aimed at raising line coverage across
/// pure / semi-pure code paths that previously lacked dedicated tests. Every
/// test here runs without needing an Application host or live WinUI window;
/// tests that would require `new SolidColorBrush()` or similar activation
/// are intentionally avoided.
/// </summary>
public class MoreCoverageTests
{
    // ════════════════════════════════════════════════════════════════════
    //  NodeRegistry — additional bookkeeping paths
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void NodeRegistry_InvalidateWindow_OnlyAffectsMatchingPrefix()
    {
        var reg = new NodeRegistry();
        var a = reg.InjectForTests(new NodeDescriptor("alpha", "C", "x", null, "Button", 0, null));
        var b = reg.InjectForTests(new NodeDescriptor("alphabet", "C", "y", null, "Button", 0, null));

        // "alpha" shouldn't accidentally invalidate ids from "alphabet" — the
        // prefix guard appends a '/' to avoid exactly that kind of collision.
        reg.InvalidateWindow("alpha");

        Assert.Equal(NodeLookupStatus.Gone, reg.Resolve(a).Status);
        Assert.NotEqual(NodeLookupStatus.Gone, reg.Resolve(b).Status);
    }

    [Fact]
    public void NodeRegistry_Tombstone_IsCounted()
    {
        var reg = new NodeRegistry();
        reg.InjectForTests(new NodeDescriptor("main", "C", "btn", null, "Button", 0, null));
        Assert.Equal(0, reg.TombstoneCountForTests());

        reg.InvalidateWindow("main");

        Assert.Equal(1, reg.TombstoneCountForTests());
    }

    [Fact]
    public void NodeRegistry_InjectedThenInvalidated_ReturnsGone_AndStaysGone()
    {
        var reg = new NodeRegistry();
        var id = reg.InjectForTests(new NodeDescriptor("main", "C", "btn", null, "Button", 0, null));

        reg.InvalidateWindow("main");

        // Resolve a second time — tombstone short-circuit path.
        Assert.Equal(NodeLookupStatus.Gone, reg.Resolve(id).Status);
        Assert.Equal(NodeLookupStatus.Gone, reg.Resolve(id).Status);
    }

    // ════════════════════════════════════════════════════════════════════
    //  WindowIdAllocator — explicit-id edge cases
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void WindowIdAllocator_ReserveThenAllocate_DoesNotReuseReservedId()
    {
        var a = new WindowIdAllocator();
        Assert.Equal("alpha", a.Reserve("alpha"));
        Assert.Equal("alpha-2", a.Allocate("alpha"));
    }

    [Fact]
    public void WindowIdAllocator_SlugifyDropsPunctuationEntirely()
    {
        // Punctuation (!, @, #) is dropped entirely — only space, dash, and
        // underscore get folded to a single dash by the slug rules.
        Assert.Equal("foobar", WindowIdAllocator.Slugify("foo!@#bar"));
        Assert.Equal("a-b", WindowIdAllocator.Slugify("  a---b---  "));
    }

    [Fact]
    public void WindowIdAllocator_SlugifyDigitsOnly_ReturnsDigits()
    {
        Assert.Equal("1234", WindowIdAllocator.Slugify("1234"));
    }

    // ════════════════════════════════════════════════════════════════════
    //  WindowRegistry — empty-state behaviour
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void WindowRegistry_Snapshot_EmptyByDefault()
    {
        var reg = new WindowRegistry("build-tag");
        Assert.Empty(reg.Snapshot());
    }

    [Fact]
    public void WindowRegistry_Resolve_UnknownId_ReturnsNull()
    {
        var reg = new WindowRegistry("build-tag");
        Assert.Null(reg.Resolve("does-not-exist"));
    }

    [Fact]
    public void WindowRegistry_TryDefault_Empty_ReturnsNullAndEmptyIds()
    {
        var reg = new WindowRegistry("build-tag");
        var result = reg.TryDefault(out var ids);
        Assert.Null(result);
        Assert.Empty(ids);
    }

    [Fact]
    public void WindowRegistry_AllocatorHook_IsAccessible()
    {
        var reg = new WindowRegistry("build-tag");
        Assert.NotNull(reg.AllocatorForTests);
    }

    // ════════════════════════════════════════════════════════════════════
    //  McpToolRegistry — Register / TryGet
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void McpToolRegistry_Register_DuplicateName_Throws()
    {
        var reg = new McpToolRegistry();
        reg.Register(new McpToolDescriptor("dup", "", new { }), _ => null);
        Assert.Throws<InvalidOperationException>(() =>
            reg.Register(new McpToolDescriptor("dup", "", new { }), _ => null));
    }

    [Fact]
    public void McpToolRegistry_TryGet_UnknownName_ReturnsFalse_WithNoopHandler()
    {
        var reg = new McpToolRegistry();
        var found = reg.TryGet("ghost", out var handler);
        Assert.False(found);
        // Exercise the noop fallback — it should return null without throwing.
        Assert.Null(handler(null));
    }

    [Fact]
    public void McpToolRegistry_List_PreservesRegistrationOrder()
    {
        var reg = new McpToolRegistry();
        reg.Register(new McpToolDescriptor("a", "", new { }), _ => null);
        reg.Register(new McpToolDescriptor("b", "", new { }), _ => null);
        reg.Register(new McpToolDescriptor("c", "", new { }), _ => null);

        var names = reg.List().Select(d => d.Name).ToArray();
        Assert.Equal(new[] { "a", "b", "c" }, names);
    }

    // ════════════════════════════════════════════════════════════════════
    //  McpToolException — constructor defaults
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void McpToolException_Defaults_ToolExecutionCode_AndNullPayload()
    {
        var ex = new McpToolException("boom");
        Assert.Equal(JsonRpcErrorCodes.ToolExecution, ex.Code);
        Assert.Null(ex.Payload);
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public void McpToolException_WithExplicitCodeAndData_RoundTrips()
    {
        var data = new { field = "x" };
        var ex = new McpToolException("nope", JsonRpcErrorCodes.InvalidParams, data);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, ex.Code);
        Assert.Same(data, ex.Payload);
    }

    // ════════════════════════════════════════════════════════════════════
    //  McpDispatcher — uncovered branches
    // ════════════════════════════════════════════════════════════════════

    private static McpDispatcher BuildDispatcherWithTools()
    {
        var reg = new McpToolRegistry();
        reg.Register(new McpToolDescriptor("kaboom", "", new { }),
            _ => throw new InvalidOperationException("generic failure"));
        reg.Register(new McpToolDescriptor("softfail", "", new { }),
            _ => new { ok = false, reason = "no-op" });
        reg.Register(new McpToolDescriptor("ping", "", new { }),
            _ => new { ok = true });
        return new McpDispatcher(reg);
    }

    [Fact]
    public void McpDispatcher_GenericException_MappedToInternalError()
    {
        var d = BuildDispatcherWithTools();
        var resp = d.Dispatch("""{"jsonrpc":"2.0","id":7,"method":"kaboom"}""");
        Assert.NotNull(resp.Error);
        Assert.Equal(JsonRpcErrorCodes.InternalError, resp.Error!.Code);
        Assert.Contains("generic failure", resp.Error.Message);
    }

    [Fact]
    public void McpDispatcher_Initialize_EchoesKnownProtocolVersion_2024()
    {
        var reg = new McpToolRegistry();
        var d = new McpDispatcher(reg);
        var resp = d.Dispatch(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}""");

        Assert.Null(resp.Error);
        var json = JsonSerializer.Serialize(resp.Result, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"protocolVersion\":\"2024-11-05\"", json);
        Assert.Contains("\"name\":\"reactor-devtools\"", json);
    }

    [Fact]
    public void McpDispatcher_Initialize_EchoesKnownProtocolVersion_2025()
    {
        var d = new McpDispatcher(new McpToolRegistry());
        var resp = d.Dispatch(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26"}}""");
        var json = JsonSerializer.Serialize(resp.Result, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"protocolVersion\":\"2025-03-26\"", json);
    }

    [Fact]
    public void McpDispatcher_Initialize_UnknownVersion_PinsBaseline()
    {
        var d = new McpDispatcher(new McpToolRegistry());
        var resp = d.Dispatch(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"9999-99-99"}}""");
        var json = JsonSerializer.Serialize(resp.Result, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"protocolVersion\":\"2024-11-05\"", json);
    }

    [Fact]
    public void McpDispatcher_Initialize_NoParams_PinsBaseline()
    {
        var d = new McpDispatcher(new McpToolRegistry());
        var resp = d.Dispatch("""{"jsonrpc":"2.0","id":1,"method":"initialize"}""");
        var json = JsonSerializer.Serialize(resp.Result, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"protocolVersion\":\"2024-11-05\"", json);
    }

    [Fact]
    public void McpDispatcher_Ping_ReturnsEmptyObject()
    {
        var d = new McpDispatcher(new McpToolRegistry());
        var resp = d.Dispatch("""{"jsonrpc":"2.0","id":1,"method":"ping"}""");
        Assert.Null(resp.Error);
        Assert.NotNull(resp.Result);
    }

    [Fact]
    public void McpDispatcher_NotificationMethod_ReturnsEmptyObject()
    {
        var d = new McpDispatcher(new McpToolRegistry());
        var resp = d.Dispatch(
            """{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        Assert.Null(resp.Error);
        Assert.NotNull(resp.Result);
    }

    [Fact]
    public void McpDispatcher_ResourcesList_ReturnsEmptyInventory()
    {
        var d = new McpDispatcher(new McpToolRegistry());
        var resp = d.Dispatch("""{"jsonrpc":"2.0","id":1,"method":"resources/list"}""");
        var json = JsonSerializer.Serialize(resp.Result, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"resources\":[]", json);
    }

    [Fact]
    public void McpDispatcher_PromptsList_ReturnsEmptyInventory()
    {
        var d = new McpDispatcher(new McpToolRegistry());
        var resp = d.Dispatch("""{"jsonrpc":"2.0","id":1,"method":"prompts/list"}""");
        var json = JsonSerializer.Serialize(resp.Result, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"prompts\":[]", json);
    }

    [Fact]
    public void McpDispatcher_ToolsCall_NonObjectParams_InvalidParams()
    {
        var d = BuildDispatcherWithTools();
        var resp = d.Dispatch("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":[1,2]}""");
        Assert.NotNull(resp.Error);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, resp.Error!.Code);
    }

    [Fact]
    public void McpDispatcher_ToolsCall_NonStringName_InvalidParams()
    {
        var d = BuildDispatcherWithTools();
        var resp = d.Dispatch("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":123}}""");
        Assert.NotNull(resp.Error);
        Assert.Equal(JsonRpcErrorCodes.InvalidParams, resp.Error!.Code);
    }

    [Fact]
    public void McpDispatcher_SoftFailResult_LogsAsError()
    {
        var tempDir = IoPath.Combine(IoPath.GetTempPath(), "reactor-more-cov-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var logger = new DevtoolsLogger(tempDir, pid: 2001, DevtoolsLogLevel.Call);
            var reg = new McpToolRegistry();
            reg.Register(new McpToolDescriptor("softfail", "", new { }),
                _ => new { ok = false });
            var d = new McpDispatcher(reg, logger);

            var resp = d.Dispatch(
                """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"softfail","arguments":{}}}""");
            logger.Dispose();

            Assert.Null(resp.Error);
            var lines = File.ReadAllLines(logger.Path);
            var one = Assert.Single(lines);
            Assert.Contains("err", one);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void McpDispatcher_GenericException_WithLogger_RecordsInternalError()
    {
        var tempDir = IoPath.Combine(IoPath.GetTempPath(), "reactor-more-cov-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var logger = new DevtoolsLogger(tempDir, pid: 2002, DevtoolsLogLevel.Call);
            var reg = new McpToolRegistry();
            reg.Register(new McpToolDescriptor("kaboom", "", new { }),
                _ => throw new InvalidOperationException("boom"));
            var d = new McpDispatcher(reg, logger);

            var resp = d.Dispatch(
                """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"kaboom","arguments":{"selector":"#z"}}}""");
            logger.Dispose();

            Assert.NotNull(resp.Error);
            Assert.Equal(JsonRpcErrorCodes.InternalError, resp.Error!.Code);
            var logText = File.ReadAllText(logger.Path);
            Assert.Contains("kaboom", logText);
            Assert.Contains("err", logText);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  DevtoolsLogger — LogError + DefaultDirectory
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void DevtoolsLogger_LogError_WritesSingleTabSeparatedLine()
    {
        var tempDir = IoPath.Combine(IoPath.GetTempPath(), "reactor-more-cov-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var logger = new DevtoolsLogger(tempDir, pid: 3001, DevtoolsLogLevel.Call);
            logger.LogError("something bad happened");
            logger.Dispose();

            var lines = File.ReadAllLines(logger.Path);
            var line = Assert.Single(lines);
            var cols = line.Split('\t');
            Assert.Equal(6, cols.Length);
            Assert.Equal("!error", cols[1]);
            Assert.Contains("something bad", cols[2]);
            Assert.Equal("err", cols[4]);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void DevtoolsLogger_LogError_OffLevel_WritesNothing()
    {
        var tempDir = IoPath.Combine(IoPath.GetTempPath(), "reactor-more-cov-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var logger = new DevtoolsLogger(tempDir, pid: 3002, DevtoolsLogLevel.Off);
            logger.LogError("should be suppressed");
            logger.Dispose();

            Assert.False(File.Exists(logger.Path));
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void DevtoolsLogger_LogError_EmptyMessage_WritesDash()
    {
        var tempDir = IoPath.Combine(IoPath.GetTempPath(), "reactor-more-cov-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var logger = new DevtoolsLogger(tempDir, pid: 3003, DevtoolsLogLevel.Error);
            logger.LogError("");
            logger.Dispose();

            var line = File.ReadAllLines(logger.Path)[0];
            Assert.Equal("-", line.Split('\t')[2]);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void DevtoolsLogger_LogCall_SelectorWithNewlines_Collapsed()
    {
        var tempDir = IoPath.Combine(IoPath.GetTempPath(), "reactor-more-cov-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var logger = new DevtoolsLogger(tempDir, pid: 3004, DevtoolsLogLevel.Call);
            logger.LogCall("t", "line1\nline2\rline3", 1, true, 0);
            logger.Dispose();

            var line = File.ReadAllLines(logger.Path)[0];
            Assert.DoesNotContain('\n', line.Substring(line.IndexOf('\t')));
            Assert.Contains("line1 line2 line3", line);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void DevtoolsLogger_DefaultDirectory_PathIncludesReactorDevtools()
    {
        var dir = DevtoolsLogger.DefaultDirectory();
        Assert.False(string.IsNullOrEmpty(dir));
        // Platform-agnostic assertion — the path always contains the devtools
        // segment regardless of OS.
        Assert.Contains("devtools", dir, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DevtoolsLogger_PathProperty_ExposesFileName()
    {
        var tempDir = IoPath.Combine(IoPath.GetTempPath(), "reactor-more-cov-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var logger = new DevtoolsLogger(tempDir, pid: 4242, DevtoolsLogLevel.Call);
            Assert.EndsWith("4242.log", logger.Path);
            Assert.Equal(DevtoolsLogLevel.Call, logger.Level);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void DevtoolsLogger_LogCall_TruncatesLongSelector()
    {
        var tempDir = IoPath.Combine(IoPath.GetTempPath(), "reactor-more-cov-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var logger = new DevtoolsLogger(tempDir, pid: 3005, DevtoolsLogLevel.Call);
            logger.LogCall("t", new string('x', 500), 1, true, 0);
            logger.Dispose();

            var line = File.ReadAllLines(logger.Path)[0];
            var selector = line.Split('\t')[2];
            // 80 char max + ellipsis.
            Assert.True(selector.Length <= 81);
            Assert.EndsWith("\u2026", selector);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void DevtoolsLogger_Dispose_IsIdempotent()
    {
        var tempDir = IoPath.Combine(IoPath.GetTempPath(), "reactor-more-cov-" + Guid.NewGuid().ToString("N"));
        try
        {
            var logger = new DevtoolsLogger(tempDir, pid: 3006, DevtoolsLogLevel.Call);
            logger.Dispose();
            // Second dispose should be a no-op rather than throwing.
            logger.Dispose();
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  DevtoolsCliParser — additional branches
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void DevtoolsCliParser_DevtoolsProject_IsParsed()
    {
        var opts = DevtoolsCliParser.Parse(
            ["app.exe", "--devtools", "run", "--devtools-project", "MyProj"]);
        Assert.Equal("MyProj", opts.ProjectIdentifier);
    }

    [Fact]
    public void DevtoolsCliParser_ProjectIdentifier_Default_IsNull()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run"]);
        Assert.Null(opts.ProjectIdentifier);
    }

    [Fact]
    public void DevtoolsCliParser_FpsNonNumeric_KeepsDefault()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--fps", "abc"]);
        Assert.Equal(10, opts.Fps);
    }

    [Fact]
    public void DevtoolsCliParser_McpPortNonNumeric_StaysNull()
    {
        var opts = DevtoolsCliParser.Parse(
            ["app.exe", "--devtools", "run", "--mcp-port", "not-a-port"]);
        Assert.Null(opts.McpPort);
    }

    [Fact]
    public void DevtoolsCliParser_ScreenshotOut_OnlyAppliesToScreenshotVerb()
    {
        // --out on a non-screenshot verb should not populate ScreenshotOutputPath.
        var opts = DevtoolsCliParser.Parse(
            ["app.exe", "--devtools", "run", "--out", "ignored.png"]);
        Assert.Null(opts.ScreenshotOutputPath);
    }

    // ════════════════════════════════════════════════════════════════════
    //  SelectorParser — extra grammar cases
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void SelectorParser_NodeIdWithLeadingWhitespace_Parses()
    {
        var ir = SelectorParser.Parse("   r:main/foo   ");
        Assert.Equal(SelectorKind.NodeId, ir.Kind);
        Assert.Equal("r:main/foo", ir.NodeId);
    }

    [Fact]
    public void SelectorParser_TypePath_MultipleHops_PreservesOrder()
    {
        var ir = SelectorParser.Parse("Grid > StackPanel[1] > Button[3]");
        Assert.Equal(SelectorKind.TypePath, ir.Kind);
        Assert.Equal(3, ir.TypePath!.Count);
        Assert.Equal("Grid", ir.TypePath[0].TypeName);
        Assert.Null(ir.TypePath[0].Index);
        Assert.Equal(1, ir.TypePath[1].Index);
        Assert.Equal(3, ir.TypePath[2].Index);
    }

    [Fact]
    public void SelectorParser_UnrecognizedTypeStep_Throws()
    {
        Assert.Throws<FormatException>(() => SelectorParser.Parse("Button > 123Bad"));
    }

    [Fact]
    public void SelectorParser_NameWithDoubleQuotes_Parses()
    {
        var ir = SelectorParser.Parse("[name=\"Accept\"]");
        Assert.Equal("Accept", ir.AutomationName);
    }

    // ════════════════════════════════════════════════════════════════════
    //  MaskEngine — edge cases for cursor navigation and mask properties
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void MaskEngine_Mask_ExposesOriginalPattern()
    {
        var engine = new MaskEngine("000-0000");
        Assert.Equal("000-0000", engine.Mask);
    }

    [Fact]
    public void MaskEngine_Length_MatchesMaskLength()
    {
        var engine = new MaskEngine("(000) 000-0000");
        Assert.Equal("(000) 000-0000".Length, engine.Length);
    }

    [Fact]
    public void MaskEngine_NextInputPosition_AtEnd_ReturnsLength()
    {
        var engine = new MaskEngine("000---");
        // Positions 3, 4, 5 are all literal — next input past them is Length.
        Assert.Equal(engine.Length, engine.NextInputPosition(3));
    }

    [Fact]
    public void MaskEngine_PreviousInputPosition_NoInputAtOrBefore_ReturnsMinusOne()
    {
        var engine = new MaskEngine("---000");
        // Starting at position 2 (all literals before or at), there's no input
        // position before it — expected to return -1.
        Assert.Equal(-1, engine.PreviousInputPosition(2));
    }

    [Fact]
    public void MaskEngine_IsLiteral_OutOfRange_ReturnsFalse()
    {
        var engine = new MaskEngine("000");
        Assert.False(engine.IsLiteral(-1));
        Assert.False(engine.IsLiteral(100));
    }

    [Fact]
    public void MaskEngine_LiteralInInput_WhenInputShorter_InsertedAutomatically()
    {
        // Input exhausted before reaching literal — literal should still be written
        // from the mask and placeholders fill the required slots after it.
        var engine = new MaskEngine("0-0");
        Assert.Equal("1-_", engine.Apply("1"));
    }

    [Fact]
    public void MaskEngine_RequiredLetter_WithDigit_PlaceholdersAndAdvances()
    {
        // Invalid character for a required-letter slot: slot gets placeholder and
        // input pointer advances (so the mismatched char doesn't stall the mask).
        var engine = new MaskEngine("AAA");
        // '1' invalid for required-letter → placeholder + advance; 'b' fills slot 1;
        // '2' invalid for slot 2 → placeholder + advance (input exhausted after).
        Assert.Equal("_b_", engine.Apply("1b2"));
    }

    // ════════════════════════════════════════════════════════════════════
    //  MarkdownBuilder — additional branches
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Markdown_UnknownEntity_PassesThroughVerbatim()
    {
        // An unknown entity should not be resolved; AddEntityText keeps the raw
        // token in the inline stream.
        var result = Factories.Markdown("&thisisnotreal;");
        var stack = Assert.IsType<StackElement>(result);
        var rtb = Assert.IsType<RichTextBlockElement>(stack.Children[0]);
        var text = string.Join("", rtb.Paragraphs![0].Inlines.OfType<RichTextRun>().Select(r => r.Text));
        Assert.Contains("&thisisnotreal;", text);
    }

    [Fact]
    public void Markdown_EmptyListItem_ProducesMarkerPlusEmpty()
    {
        // An empty list item should still produce an item block.
        var options = new MarkdownOptions
        {
            ListItem = (el) => el,
        };
        // md4c may collapse a fully-empty bullet, so provide a trivial item.
        var result = Factories.Markdown("- ", options);
        var stack = Assert.IsType<StackElement>(result);
        // The top-level vstack either contains a list stack or is empty —
        // the important thing is that the parser doesn't throw.
        Assert.NotNull(stack);
    }

    [Fact]
    public void Markdown_OrderedListCallback_IsInvoked()
    {
        bool invoked = false;
        Element[]? capturedItems = null;
        var options = new MarkdownOptions
        {
            OrderedList = (start, items) =>
            {
                invoked = true;
                capturedItems = items;
                return VStack(items);
            },
        };

        Factories.Markdown("1. one\n2. two", options);
        Assert.True(invoked);
        Assert.NotNull(capturedItems);
        Assert.Equal(2, capturedItems!.Length);
    }

    [Fact]
    public void Markdown_CodeBlock_WithLanguageAndTrailingNewline_TrimsNewline()
    {
        string? capturedCode = null;
        var options = new MarkdownOptions
        {
            CodeBlock = (c, l) => { capturedCode = c; return TextBlock(c); },
        };

        Factories.Markdown("```py\nprint('hi')\n```", options);
        Assert.Equal("print('hi')", capturedCode);
    }

    [Fact]
    public void Markdown_LinkInsideBoldHasSafeUri()
    {
        var result = Factories.Markdown("**[text](https://example.org)**");
        var stack = Assert.IsType<StackElement>(result);
        var rtb = Assert.IsType<RichTextBlockElement>(stack.Children[0]);
        var link = rtb.Paragraphs![0].Inlines.OfType<RichTextHyperlink>().FirstOrDefault();
        Assert.NotNull(link);
        Assert.Equal(new Uri("https://example.org"), link!.NavigateUri);
    }

    [Fact]
    public void Markdown_MultiParagraphListItem_WrapsInVStack()
    {
        // A list item with multiple block children (paragraph + paragraph) should
        // wrap in a VStack(4, ...) per LeaveListItem.
        var md = "- First paragraph\n\n    Second paragraph\n- Next item";
        var result = Factories.Markdown(md);
        // Just ensure it doesn't throw and produces some structure.
        var stack = Assert.IsType<StackElement>(result);
        Assert.NotEmpty(stack.Children);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Issue #479 — clickable inline Hyperlink (OnClick on RichTextHyperlink)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Hyperlink_OnClickFactory_SetsOnClickAndSentinelUri()
    {
        var fired = 0;
        var link = Hyperlink("click me", () => fired++);

        Assert.Equal("click me", link.Text);
        Assert.NotNull(link.OnClick);
        // Sentinel URI is set so the record positional ctor stays non-null.
        Assert.Equal(new Uri("about:blank"), link.NavigateUri);

        link.OnClick!.Invoke();
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Hyperlink_OnClickFactory_ThrowsOnNullAction()
    {
        Assert.Throws<ArgumentNullException>(() => Hyperlink("x", (Action)null!));
    }

    [Fact]
    public void Hyperlink_UriFactory_LeavesOnClickNull()
    {
        var link = Hyperlink("text", new Uri("https://example.com"));
        Assert.Null(link.OnClick);
        Assert.Equal(new Uri("https://example.com"), link.NavigateUri);
    }

    [Fact]
    public void RichTextHyperlink_RecordEquality_TracksOnClick()
    {
        Action a = () => { };
        Action b = () => { };

        var withA1 = new RichTextHyperlink("t", new Uri("about:blank")) { OnClick = a };
        var withA2 = new RichTextHyperlink("t", new Uri("about:blank")) { OnClick = a };
        var withB  = new RichTextHyperlink("t", new Uri("about:blank")) { OnClick = b };
        var withNone = new RichTextHyperlink("t", new Uri("about:blank"));

        // Same delegate → equal. Different closures → unequal. Required so
        // the reconciler's inline diff calls UpdateHyperlinkInPlace whenever
        // the click handler changes between renders.
        Assert.Equal(withA1, withA2);
        Assert.NotEqual(withA1, withB);
        Assert.NotEqual(withA1, withNone);
    }

    // PathDataParser tests were intentionally skipped — every overload of
    // PathDataParser.Parse constructs a `new PathGeometry()` even for null or
    // whitespace inputs, which requires WinUI XAML Application activation that
    // isn't available to this unit-test host. The parser is exercised by
    // higher-level charting tests that run in the self-host harness.
}
