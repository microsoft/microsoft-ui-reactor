using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Shape tests for the Phase 3 full-view tree payload. Walking a real visual
/// tree requires a window — tests here operate on hand-built <see cref="TreeNode"/>
/// instances and assert the serialization matches spec §9. Self-host tests
/// (§3.11) cover end-to-end walking.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: reflection-based System.Text.Json serialization of devtools/MCP payload objects (no source-gen context; DevtoolsMcpServer.JsonOpts). Issue #70 documents this devtools JSON surface as RUC/RDC-by-design and not-yet-AOT-clean; the standard `dotnet test` path is JIT (never trimmed). Behaviour-neutral.")]
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Test-only: reflection-based System.Text.Json serialization of devtools/MCP payloads (see the IL2026 note). Standard test run is JIT, not AOT-compiled. Behaviour-neutral.")]
public class TreeFullViewTests
{
    [Fact]
    public void FullViewFields_SerializeWhenPopulated()
    {
        var node = new TreeNode
        {
            Id = "r:main/Counter.btn-inc",
            Type = "Button",
            Bounds = new BoundsBox(120, 80, 96, 32),
            IsVisible = true,
            TypeFullName = "Microsoft.UI.Xaml.Controls.Button",
            AutomationControlType = "Button",
            IsEnabled = true,
            IsKeyboardFocusable = true,
            DesiredSize = new SizeBox(96, 32),
            ActualSize = new SizeBox(96, 32),
            Layout = new LayoutInfo
            {
                Margin = new[] { 4.0, 4, 4, 4 },
                Padding = new[] { 8.0, 4, 8, 4 },
                HorizontalAlignment = "Left",
                VerticalAlignment = "Center",
                HorizontalContentAlignment = "Center",
                VerticalContentAlignment = "Center",
            },
            Context = new ContextInfo
            {
                ParentType = "StackPanel",
                StackOrientation = "Vertical",
            },
            Visual = new VisualInfo { Opacity = 0.9 },
        };

        var json = JsonSerializer.Serialize(node, DevtoolsMcpServer.JsonOpts);

        Assert.Contains("\"typeFullName\":\"Microsoft.UI.Xaml.Controls.Button\"", json);
        Assert.Contains("\"automationControlType\":\"Button\"", json);
        Assert.Contains("\"isEnabled\":true", json);
        Assert.Contains("\"isKeyboardFocusable\":true", json);
        Assert.Contains("\"desiredSize\"", json);
        Assert.Contains("\"actualSize\"", json);
        Assert.Contains("\"layout\"", json);
        Assert.Contains("\"margin\":[4,4,4,4]", json);
        Assert.Contains("\"horizontalContentAlignment\":\"Center\"", json);
        Assert.Contains("\"context\"", json);
        Assert.Contains("\"parentType\":\"StackPanel\"", json);
        Assert.Contains("\"stackOrientation\":\"Vertical\"", json);
        Assert.Contains("\"visual\"", json);
    }

    [Fact]
    public void SummaryView_OmitsFullViewFields()
    {
        // A TreeNode without any full-view properties set must serialize only
        // the summary subset — this backstops the JsonIgnoreCondition.WhenWritingNull
        // contract that DevtoolsMcpServer.JsonOpts relies on.
        var node = new TreeNode
        {
            Id = "r:main/Button",
            Type = "Button",
            Bounds = new BoundsBox(0, 0, 100, 40),
            IsVisible = true,
        };

        var json = JsonSerializer.Serialize(node, DevtoolsMcpServer.JsonOpts);

        Assert.DoesNotContain("typeFullName", json);
        Assert.DoesNotContain("desiredSize", json);
        Assert.DoesNotContain("actualSize", json);
        Assert.DoesNotContain("layout", json);
        Assert.DoesNotContain("context", json);
        Assert.DoesNotContain("visual", json);
        Assert.DoesNotContain("automationControlType", json);
        Assert.DoesNotContain("isEnabled", json);
        Assert.DoesNotContain("isKeyboardFocusable", json);
    }

    [Fact]
    public void VisualInfo_IdentityTransformAndNullClip_AreOmitted()
    {
        // The walker strips fully-default visual blocks. Simulate that by
        // showing what it emits: a non-default VisualInfo survives, a default
        // one never appears (the walker sets Visual = null).
        var defaultNode = new TreeNode { Visual = null };
        var nonDefaultNode = new TreeNode
        {
            Visual = new VisualInfo
            {
                Opacity = 0.5,
                Clip = new BoundsBox(0, 0, 10, 10),
                ZIndex = 3,
                RenderTransform = new[] { 1.0, 0, 0, 1, 5, 0 },
            },
        };

        var defaultJson = JsonSerializer.Serialize(defaultNode, DevtoolsMcpServer.JsonOpts);
        var nonDefaultJson = JsonSerializer.Serialize(nonDefaultNode, DevtoolsMcpServer.JsonOpts);

        Assert.DoesNotContain("visual", defaultJson);
        Assert.Contains("\"opacity\":0.5", nonDefaultJson);
        Assert.Contains("\"clip\"", nonDefaultJson);
        Assert.Contains("\"zIndex\":3", nonDefaultJson);
        Assert.Contains("\"renderTransform\":[1,0,0,1,5,0]", nonDefaultJson);
    }

    [Fact]
    public void LayoutAlignment_OmittedWhenNotSet()
    {
        // A node with no Control subclass has only margin + alignment on the
        // layout block; content alignment stays null and must not serialize.
        var node = new TreeNode
        {
            Id = "r:main/TextBlock",
            Type = "TextBlock",
            Layout = new LayoutInfo
            {
                Margin = new[] { 0.0, 0, 0, 0 },
                HorizontalAlignment = "Stretch",
                VerticalAlignment = "Top",
            },
        };

        var json = JsonSerializer.Serialize(node, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"horizontalAlignment\":\"Stretch\"", json);
        Assert.DoesNotContain("horizontalContentAlignment", json);
        Assert.DoesNotContain("verticalContentAlignment", json);
        Assert.DoesNotContain("padding", json);
    }

    [Fact]
    public void ContextInfo_GridSpans_OmittedWhenOne()
    {
        // The walker only populates RowSpan/ColumnSpan when != 1 — identity
        // values go unserialized to keep payload tight.
        var node = new TreeNode
        {
            Context = new ContextInfo { ParentType = "Grid", GridRow = 0, GridColumn = 1 },
        };

        var json = JsonSerializer.Serialize(node, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"gridRow\":0", json);
        Assert.Contains("\"gridColumn\":1", json);
        Assert.DoesNotContain("gridRowSpan", json);
        Assert.DoesNotContain("gridColumnSpan", json);
    }
}
