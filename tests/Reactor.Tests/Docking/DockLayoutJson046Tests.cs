using System;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Persistence;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 046 §6.7 — JSON round-trip for the new <see cref="DockTabGroup.Role"/>
/// and <see cref="ToolWindow.AllowedSides"/> fields. Defaults are omitted;
/// unknown values are forward-compat (default + warning).
/// </summary>
public class DockLayoutJson046Tests
{
    [Fact]
    public void RoundTrip_AllThreeRoles()
    {
        var layout = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea),
            new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.General),
        });
        var json = DockLayoutSerializer.Save(layout);
        var result = DockLayoutSerializer.Load(json);
        Assert.True(result.Success);
        var split = Assert.IsType<DockSplit>(result.Root);
        Assert.Equal(DockGroupRole.ToolWindowStrip, ((DockTabGroup)split.Children[0]).Role);
        Assert.Equal(DockGroupRole.DocumentArea, ((DockTabGroup)split.Children[1]).Role);
        Assert.Equal(DockGroupRole.General, ((DockTabGroup)split.Children[2]).Role);
    }

    [Fact]
    public void RoundTrip_AllowedSides_LeftBottom()
    {
        var tw = new ToolWindow { Title = "X", Key = "x", AllowedSides = DockSides.Left | DockSides.Bottom };
        var json = DockLayoutSerializer.Save(tw);
        var result = DockLayoutSerializer.Load(json);
        var loaded = Assert.IsType<ToolWindow>(result.Root);
        Assert.Equal(DockSides.Left | DockSides.Bottom, loaded.AllowedSides);
    }

    [Fact]
    public void RoundTrip_AllowedSides_None()
    {
        var tw = new ToolWindow { Title = "X", Key = "x", AllowedSides = DockSides.None };
        var json = DockLayoutSerializer.Save(tw);
        var result = DockLayoutSerializer.Load(json);
        var loaded = Assert.IsType<ToolWindow>(result.Root);
        Assert.Equal(DockSides.None, loaded.AllowedSides);
    }

    [Fact]
    public void DefaultRole_IsOmittedFromJson()
    {
        var grp = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.General);
        var json = DockLayoutSerializer.Save(grp);
        // The JSON for the group should not contain a "role" field.
        var node = JsonNode.Parse(json)!;
        var root = node["root"];
        Assert.NotNull(root);
        Assert.Null(root!["role"]);
    }

    [Fact]
    public void DefaultAllowedSides_IsOmittedFromJson()
    {
        var tw = new ToolWindow { Title = "X", Key = "x" };  // AllowedSides=All default
        var json = DockLayoutSerializer.Save(tw);
        var node = JsonNode.Parse(json)!;
        var paneNode = node["root"]!["pane"];
        Assert.NotNull(paneNode);
        Assert.Null(paneNode!["allowedSides"]);
    }

    [Fact]
    public void EmittedRole_UsesLowercaseInvariantString()
    {
        var grp = new DockTabGroup(Array.Empty<DockableContent>(), Role: DockGroupRole.DocumentArea);
        var json = DockLayoutSerializer.Save(grp);
        var node = JsonNode.Parse(json)!;
        Assert.Equal("documentArea", node["root"]!["role"]!.GetValue<string>());
    }

    [Fact]
    public void EmittedAllowedSides_IsLowercaseStringArray()
    {
        var tw = new ToolWindow { Title = "X", Key = "x", AllowedSides = DockSides.Top | DockSides.Right };
        var json = DockLayoutSerializer.Save(tw);
        var node = JsonNode.Parse(json)!;
        var arr = node["root"]!["pane"]!["allowedSides"]!.AsArray();
        var values = arr.Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains("top", values);
        Assert.Contains("right", values);
        Assert.DoesNotContain("left", values);
        Assert.DoesNotContain("bottom", values);
    }

    [Fact]
    public void ForwardCompat_UnknownRoleString_DefaultsToGeneral()
    {
        var json = """
        {
          "$schema": 2,
          "root": {
            "kind": "tabGroup",
            "documents": [],
            "tabPosition": "top",
            "role": "futureRole"
          }
        }
        """;
        var result = DockLayoutSerializer.Load(json);
        Assert.True(result.Success);
        var grp = Assert.IsType<DockTabGroup>(result.Root);
        Assert.Equal(DockGroupRole.General, grp.Role);
    }

    [Fact]
    public void ForwardCompat_UnknownSideString_IsIgnored()
    {
        var json = """
        {
          "$schema": 2,
          "root": {
            "kind": "pane",
            "pane": {
              "title": "X",
              "key": "x",
              "role": "toolWindow",
              "allowedSides": ["left", "diagonal"]
            }
          }
        }
        """;
        var result = DockLayoutSerializer.Load(json);
        var tw = Assert.IsType<ToolWindow>(result.Root);
        Assert.Equal(DockSides.Left, tw.AllowedSides);
    }

    [Fact]
    public void OldLayoutWithoutNewFields_DeserializesDefaults()
    {
        // A pre-046 JSON: no role on group, no allowedSides on pane.
        var json = """
        {
          "$schema": 2,
          "root": {
            "kind": "tabGroup",
            "documents": [
              { "title": "X", "key": "x", "role": "toolWindow" }
            ],
            "tabPosition": "top"
          }
        }
        """;
        var result = DockLayoutSerializer.Load(json);
        var grp = Assert.IsType<DockTabGroup>(result.Root);
        Assert.Equal(DockGroupRole.General, grp.Role);
        var tw = Assert.IsType<ToolWindow>(grp.Documents[0]);
        Assert.Equal(DockSides.All, tw.AllowedSides);
    }

    [Fact]
    public void RoundTrip_NestedLayoutWithRolesAndSides()
    {
        var layout = new DockSplit(Orientation.Horizontal, new DockNode[]
        {
            new DockTabGroup(new[]
            {
                (DockableContent)new ToolWindow { Title = "Errors", Key = "errors", AllowedSides = DockSides.Bottom },
            }, Role: DockGroupRole.ToolWindowStrip),
            new DockTabGroup(new[]
            {
                (DockableContent)new Document { Title = "MainView", Key = "main" },
            }, Role: DockGroupRole.DocumentArea, SelectedIndex: 0),
        });
        var json = DockLayoutSerializer.Save(layout);
        var result = DockLayoutSerializer.Load(json);
        var split = Assert.IsType<DockSplit>(result.Root);
        Assert.Equal(DockGroupRole.ToolWindowStrip, ((DockTabGroup)split.Children[0]).Role);
        Assert.Equal(DockGroupRole.DocumentArea, ((DockTabGroup)split.Children[1]).Role);
        var errors = Assert.IsType<ToolWindow>(((DockTabGroup)split.Children[0]).Documents[0]);
        Assert.Equal(DockSides.Bottom, errors.AllowedSides);
    }
}
