using System.Text.Json;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Pure-payload coverage for the spec 045 §2.26 docking.* MCP tools. The
/// live dispatcher hop + the JSON-RPC transport are out of scope here —
/// these tests drive <c>BuildListPayload</c> / <c>BuildSnapshotPayload</c> /
/// <c>BuildDockPayload</c> directly so the contract is testable without
/// spinning up an MCP server.
/// </summary>
[Collection("DockingGlobals")]
public class DevtoolsDockingToolsTests : IDisposable
{
    public DevtoolsDockingToolsTests() => DockHostRegistry.ResetForTest();
    public void Dispose() => DockHostRegistry.ResetForTest();

    [Fact]
    public void BuildListPayload_EmptyRegistry_ReturnsEmptyHosts()
    {
        var result = DevtoolsDockingTools.BuildListPayload();
        Assert.Empty(HostsArray(result));
    }

    [Fact]
    public void BuildListPayload_OneHost_IncludesIdAndPaneCount()
    {
        var manager = new DockManager
        {
            Layout = new DockTabGroup(new DockableContent[]
            {
                new("Alpha", Key: "a"),
                new("Beta",  Key: "b"),
            }),
        };
        DockHostRegistry.Register(manager);

        var result = DevtoolsDockingTools.BuildListPayload();
        var hosts = HostsArray(result);
        Assert.Single(hosts);
        var props = hosts[0].GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(hosts[0]));
        Assert.Equal("dh:1", props["id"]);
        Assert.Equal(2, props["paneCount"]);
    }

    [Fact]
    public void BuildSnapshotPayload_UnknownHost_Throws()
    {
        using var doc = JsonDocument.Parse("""{"hostId":"dh:999"}""");
        var ex = Assert.Throws<McpToolException>(
            () => DevtoolsDockingTools.BuildSnapshotPayload(doc.RootElement));
        Assert.Contains("dh:999", ex.Message);
    }

    [Fact]
    public void BuildSnapshotPayload_LiveHost_ReturnsTreeShape()
    {
        var manager = new DockManager
        {
            Layout = new DockTabGroup(new DockableContent[]
            {
                new Document   { Title = "Main.cs", Key = "m" },
                new ToolWindow { Title = "Output",  Key = "o" },
            }),
        };
        var record = DockHostRegistry.Register(manager);

        using var doc = JsonDocument.Parse($$"""{"hostId":"{{record.Id}}"}""");
        var result = DevtoolsDockingTools.BuildSnapshotPayload(doc.RootElement);

        var props = result.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(result));
        Assert.Equal(record.Id, props["hostId"]);
        Assert.NotNull(props["root"]);

        // Round-trip the payload through the JSON serializer so a
        // shape regression — e.g. a snapshot that drops pane keys, or a
        // tree-builder that emits an empty root — surfaces here rather
        // than as a silent gap in MCP consumers.
        var json = JsonSerializer.Serialize(result);
        Assert.Contains("\"m\"", json);
        Assert.Contains("\"o\"", json);
        // §2.26 — the snapshot exposes a discriminator field describing
        // the node kind (leaf / tabGroup / split). Both panes carry a
        // role marker; the root tabGroup carries one too. We don't pin
        // the exact role string (interface evolution) but we require at
        // least one role discriminator key to be present.
        Assert.Contains("role", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDockPayload_UnknownHost_Throws()
    {
        using var doc = JsonDocument.Parse("""{"hostId":"dh:999","paneKey":"x","action":"activate"}""");
        var ex = Assert.Throws<McpToolException>(
            () => DevtoolsDockingTools.BuildDockPayload(doc.RootElement));
        Assert.Contains("unknown", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDockPayload_UnknownPane_Throws()
    {
        var manager = new DockManager { Layout = new DockTabGroup(new DockableContent[] { new("A", Key: "a") }) };
        var record = DockHostRegistry.Register(manager);
        var model = new DockHostModel { Root = manager.Layout };
        DockHostModelBridge.Set(manager, model);

        using var doc = JsonDocument.Parse($$"""{"hostId":"{{record.Id}}","paneKey":"missing","action":"activate"}""");
        var ex = Assert.Throws<McpToolException>(
            () => DevtoolsDockingTools.BuildDockPayload(doc.RootElement));
        Assert.Contains("missing", ex.Message);
    }

    [Fact]
    public void BuildDockPayload_ActivateAction_QueuesActivateMutation()
    {
        var manager = new DockManager { Layout = new DockTabGroup(new DockableContent[] { new("A", Key: "a") }) };
        var record = DockHostRegistry.Register(manager);
        var model = new DockHostModel { Root = manager.Layout };
        DockHostModelBridge.Set(manager, model);

        using var doc = JsonDocument.Parse($$"""{"hostId":"{{record.Id}}","paneKey":"a","action":"activate"}""");
        var result = DevtoolsDockingTools.BuildDockPayload(doc.RootElement);

        var props = result.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(result));
        Assert.Equal(true, props["ok"]);
        Assert.Single(model.Pending);
    }

    [Fact]
    public void BuildDockPayload_DockAction_RequiresTarget()
    {
        var manager = new DockManager { Layout = new DockTabGroup(new DockableContent[] { new("A", Key: "a") }) };
        var record = DockHostRegistry.Register(manager);
        var model = new DockHostModel { Root = manager.Layout };
        DockHostModelBridge.Set(manager, model);

        using var doc = JsonDocument.Parse($$"""{"hostId":"{{record.Id}}","paneKey":"a","action":"dock"}""");
        var ex = Assert.Throws<McpToolException>(
            () => DevtoolsDockingTools.BuildDockPayload(doc.RootElement));
        Assert.Contains("target", ex.Message);
    }

    [Fact]
    public void BuildDockPayload_PinToSide_RequiresToolWindow()
    {
        // Documents can't be pinned — pass a Document and expect a clear refusal.
        var manager = new DockManager
        {
            Layout = new DockTabGroup(new DockableContent[]
            {
                new Document { Title = "Main.cs", Key = "m" },
            }),
        };
        var record = DockHostRegistry.Register(manager);
        var model = new DockHostModel { Root = manager.Layout };
        DockHostModelBridge.Set(manager, model);

        using var doc = JsonDocument.Parse($$"""{"hostId":"{{record.Id}}","paneKey":"m","action":"pinToSide"}""");
        var ex = Assert.Throws<McpToolException>(
            () => DevtoolsDockingTools.BuildDockPayload(doc.RootElement));
        Assert.Contains("ToolWindow", ex.Message);
    }

    [Fact]
    public void BuildDockPayload_PinToSide_OnToolWindow_QueuesPinMutation()
    {
        var manager = new DockManager
        {
            Layout = new DockTabGroup(new DockableContent[]
            {
                new ToolWindow { Title = "Output", Key = "o" },
            }),
        };
        var record = DockHostRegistry.Register(manager);
        var model = new DockHostModel { Root = manager.Layout };
        DockHostModelBridge.Set(manager, model);

        using var doc = JsonDocument.Parse($$"""{"hostId":"{{record.Id}}","paneKey":"o","action":"pinToSide","side":"Bottom"}""");
        var result = DevtoolsDockingTools.BuildDockPayload(doc.RootElement);

        var props = result.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(result));
        Assert.Equal(true, props["ok"]);
        Assert.Single(model.Pending);
    }

    [Fact]
    public void BuildDockPayload_UnknownAction_Throws()
    {
        var manager = new DockManager { Layout = new DockTabGroup(new DockableContent[] { new("A", Key: "a") }) };
        var record = DockHostRegistry.Register(manager);
        var model = new DockHostModel { Root = manager.Layout };
        DockHostModelBridge.Set(manager, model);

        using var doc = JsonDocument.Parse($$"""{"hostId":"{{record.Id}}","paneKey":"a","action":"teleport"}""");
        var ex = Assert.Throws<McpToolException>(
            () => DevtoolsDockingTools.BuildDockPayload(doc.RootElement));
        Assert.Contains("teleport", ex.Message);
    }

    private static object[] HostsArray(object payload)
    {
        var prop = payload.GetType().GetProperty("hosts")!;
        return (object[])prop.GetValue(payload)!;
    }
}
