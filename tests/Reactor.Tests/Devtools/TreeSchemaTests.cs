using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Serialization-shape tests for the tree payload. Walking a real visual tree
/// needs a WinUI window and is covered by the self-host fixture (§2.17); these
/// tests assert the schema pin, the summary-only field set, and cross-ref
/// plumbing between ParentId/ChildIds.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: reflection-based System.Text.Json serialization of devtools/MCP payload objects (no source-gen context; DevtoolsMcpServer.JsonOpts). Issue #70 documents this devtools JSON surface as RUC/RDC-by-design and not-yet-AOT-clean; the standard `dotnet test` path is JIT (never trimmed). Behaviour-neutral.")]
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test-only: reflection-based System.Text.Json serialization of devtools/MCP payloads (see the IL2026 note). Standard test run is JIT, not AOT-compiled. Behaviour-neutral.")]
public class TreeSchemaTests
{
    [Fact]
    public void TreeResult_HasPinnedSchemaTag()
    {
        var payload = new TreeResult { WindowId = "main" };
        var json = JsonSerializer.Serialize(payload, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"$schema\":\"reactor-tree/1\"", json);
    }

    [Fact]
    public void TreeNode_SummaryView_OmitsFullViewFields()
    {
        // The summary shape is fixed: id, type, name, automationId,
        // automationName, bounds, text, isVisible, parentId, childIds,
        // reactor? — nothing from the Phase 3 full view leaks in.
        var node = new TreeNode
        {
            Id = "r:main/Button",
            Type = "Button",
            Bounds = new BoundsBox(0, 0, 100, 40),
            IsVisible = true,
        };
        var json = JsonSerializer.Serialize(node, DevtoolsMcpServer.JsonOpts);

        // Spot-check a few Phase 3 fields that must NOT appear.
        Assert.DoesNotContain("desiredSize", json);
        Assert.DoesNotContain("actualSize", json);
        Assert.DoesNotContain("layout", json);
        Assert.DoesNotContain("visual", json);
        Assert.DoesNotContain("context", json);
        Assert.DoesNotContain("supportedPatterns", json);
    }

    [Fact]
    public void TreeNode_FullView_CarriesSupportedPatterns()
    {
        // Usability feedback: agents need a way to look up "what verbs can I
        // call on this element?" without trial-and-error. Full view emits
        // the UIA pattern names the automation peer exposes; summary omits it.
        var node = new TreeNode
        {
            Id = "r:main/Button",
            Type = "Button",
            SupportedPatterns = new List<string> { "Invoke" },
        };
        var json = JsonSerializer.Serialize(node, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"supportedPatterns\":[\"Invoke\"]", json);
    }

    [Fact]
    public void TreeNode_ChildIdsCrossReferenceParentId()
    {
        var parent = new TreeNode { Id = "r:main/Panel", Type = "StackPanel" };
        var child = new TreeNode { Id = "r:main/Panel/~Button[0]", Type = "Button", ParentId = parent.Id };
        parent.ChildIds.Add(child.Id);

        var nodes = new[] { parent, child };
        // Each non-root node's ParentId must appear in exactly one other node's ChildIds.
        foreach (var n in nodes)
        {
            if (n.ParentId is null) continue;
            var p = nodes.Single(x => x.Id == n.ParentId);
            Assert.Contains(n.Id, p.ChildIds);
        }
    }

    [Fact]
    public void Bounds_SerializeAsXYWidthHeight()
    {
        var node = new TreeNode { Bounds = new BoundsBox(1, 2, 3, 4) };
        var json = JsonSerializer.Serialize(node, DevtoolsMcpServer.JsonOpts);
        // System.Text.Json emits record-struct members with camelCase names.
        Assert.Contains("\"x\":1", json);
        Assert.Contains("\"width\":3", json);
    }
}
