using System.Text.Json;
using Microsoft.UI.Reactor.Cli;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Contract tests for CLI JSON paths that use the source-generated
/// <see cref="CliJsonContext"/> instead of the reflection-based <c>JsonSerializer</c>
/// (AOT-cleanup for PR "treat warnings as errors"). They pin the exact wire shape.
/// The `mur devtools` cases left with the removed devtools CLI.
/// </summary>
public class CliJsonContractTests
{
    // ── FigmaEvent (the `@event` member must serialize as `event`) ──────────

    [Fact]
    public void FigmaEvent_Serializes_Event_Key_And_Drops_Null_NodeId()
    {
        var evt = new global::Microsoft.UI.Reactor.Cli.Figma.FigmaEvent(
            @event: "updated",
            fileKey: "abc",
            nodeId: null,
            fileName: "Design",
            lastModified: "2026-01-01T00:00:00.0000000",
            version: "1",
            figmaUrl: "https://www.figma.com/design/abc");

        var json = JsonSerializer.Serialize(evt, CliJsonContext.Default.FigmaEvent);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("updated", doc.RootElement.GetProperty("event").GetString());
        Assert.Equal("abc", doc.RootElement.GetProperty("fileKey").GetString());
        Assert.False(doc.RootElement.TryGetProperty("nodeId", out _));
    }
}
