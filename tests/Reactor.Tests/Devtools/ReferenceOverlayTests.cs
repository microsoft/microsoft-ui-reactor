using System.Linq;
using System.Text.Json;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Spec 057 §11 Phase 3 (3.1) — shape + diagnostics tests for the reference-graph
/// overlay. Building the overlay against a live tree needs a WinUI window and is
/// covered end-to-end by the <c>ReferenceOverlay_*</c> self-host fixtures; these
/// tests pin the serialization shape, the slot→label mapping, and the cycle /
/// unresolved diagnostic logic on hand-built edge lists.
/// </summary>
public class ReferenceOverlayTests
{
    [Fact]
    public void ReferenceGraphResult_HasPinnedSchemaTag()
    {
        var payload = new ReferenceGraphResult { WindowId = "main" };
        var json = JsonSerializer.Serialize(payload, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"$schema\":\"reactor-references/1\"", json);
    }

    [Fact]
    public void ReferenceEdgeInfo_SerializesCamelCase_AndOmitsNullOutOfTree()
    {
        var edge = new ReferenceEdgeInfo
        {
            From = "r:main/A",
            To = "r:main/B",
            Label = "LabeledBy",
            Slot = 200_000,
            Kind = "scalar",
            Resolved = true,
        };
        var json = JsonSerializer.Serialize(edge, DevtoolsMcpServer.JsonOpts);

        Assert.Contains("\"from\":\"r:main/A\"", json);
        Assert.Contains("\"to\":\"r:main/B\"", json);
        Assert.Contains("\"label\":\"LabeledBy\"", json);
        Assert.Contains("\"resolved\":true", json);
        Assert.DoesNotContain("outOfTree", json); // null is omitted
    }

    [Theory]
    [InlineData(200_000, "LabeledBy")]
    [InlineData(200_001, "DescribedBy")]
    [InlineData(200_002, "FlowsTo")]
    [InlineData(200_003, "FlowsFrom")]
    [InlineData(200_010, "XYFocusUp")]
    [InlineData(200_011, "XYFocusDown")]
    [InlineData(200_012, "XYFocusLeft")]
    [InlineData(200_013, "XYFocusRight")]
    [InlineData(0, "reference#0")]
    [InlineData(2, "reference#2")]
    [InlineData(100_000, "binding#0")]
    [InlineData(100_005, "binding#5")]
    [InlineData(200_099, "modifier#200099")]
    public void LabelForSlot_MapsKnownSlots(int slot, string expected)
        => Assert.Equal(expected, ReferenceOverlay.LabelForSlot(slot));

    [Fact]
    public void Diagnostics_FlagUnresolvedEdges()
    {
        var edges = new List<ReferenceEdgeInfo>
        {
            new() { From = "r:main/A", To = "r:main/B", Label = "reference#0", Resolved = true },
            new() { From = "r:main/A", To = null, Label = "DescribedBy", Resolved = false },
        };

        var diags = ReferenceOverlay.BuildDiagnostics(edges);

        var unresolved = Assert.Single(diags, d => d.Kind == "unresolved");
        Assert.Contains("DescribedBy", unresolved.Message);
        Assert.Equal(new[] { "r:main/A" }, unresolved.NodeIds);
    }

    [Fact]
    public void Diagnostics_DetectBidirectionalCycle()
    {
        var edges = new List<ReferenceEdgeInfo>
        {
            new() { From = "r:main/A", To = "r:main/B", Label = "reference#0", Resolved = true },
            new() { From = "r:main/B", To = "r:main/A", Label = "reference#0", Resolved = true },
        };

        var diags = ReferenceOverlay.BuildDiagnostics(edges);

        var cycle = Assert.Single(diags, d => d.Kind == "cycle");
        Assert.Contains("r:main/A", cycle.NodeIds);
        Assert.Contains("r:main/B", cycle.NodeIds);
        Assert.Equal(2, cycle.NodeIds.Count); // reported once, not once per direction
    }

    [Fact]
    public void Diagnostics_DetectThreeNodeCycle_ReportedOnce()
    {
        var edges = new List<ReferenceEdgeInfo>
        {
            new() { From = "A", To = "B", Resolved = true },
            new() { From = "B", To = "C", Resolved = true },
            new() { From = "C", To = "A", Resolved = true },
        };

        var diags = ReferenceOverlay.BuildDiagnostics(edges);

        var cycle = Assert.Single(diags, d => d.Kind == "cycle");
        Assert.Equal(3, cycle.NodeIds.Count);
        Assert.Equal(new[] { "A", "B", "C" }, cycle.NodeIds.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Diagnostics_SelfReference_IsACycle()
    {
        var edges = new List<ReferenceEdgeInfo>
        {
            new() { From = "A", To = "A", Resolved = true },
        };

        var diags = ReferenceOverlay.BuildDiagnostics(edges);

        var cycle = Assert.Single(diags, d => d.Kind == "cycle");
        Assert.Equal(new[] { "A" }, cycle.NodeIds);
    }

    [Fact]
    public void Diagnostics_AcyclicGraph_HasNoCycle()
    {
        var edges = new List<ReferenceEdgeInfo>
        {
            new() { From = "A", To = "B", Resolved = true },
            new() { From = "B", To = "C", Resolved = true },
            new() { From = "A", To = "C", Resolved = true }, // diamond, no cycle
        };

        var diags = ReferenceOverlay.BuildDiagnostics(edges);

        Assert.DoesNotContain(diags, d => d.Kind == "cycle");
    }

    [Fact]
    public void Diagnostics_UnresolvedEdges_DoNotFormCycles()
    {
        // An edge to a null target is not a graph edge, so it cannot close a cycle.
        var edges = new List<ReferenceEdgeInfo>
        {
            new() { From = "A", To = "B", Resolved = true },
            new() { From = "B", To = null, Resolved = false },
        };

        var diags = ReferenceOverlay.BuildDiagnostics(edges);

        Assert.DoesNotContain(diags, d => d.Kind == "cycle");
        Assert.Single(diags, d => d.Kind == "unresolved");
    }
}
