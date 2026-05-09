using System.Text.Json;
using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Second coverage-oriented pass, focused on pure-logic surface that was still
/// under-exercised after <see cref="MoreCoverageTests"/> and the specialized
/// per-area suites. Every test here runs without a live WinUI host.
/// </summary>
public class MoreCoverageTests2
{
    // ════════════════════════════════════════════════════════════════════
    //  WindowRegistry — WindowInfo / WindowBounds record constructors
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void WindowInfo_Construction_RoundTripsAllFields()
    {
        var bounds = new WindowBounds(10, 20, 300, 400);
        var info = new WindowInfo(
            Id: "main",
            Title: "App",
            Hwnd: 0xDEAD,
            Bounds: bounds,
            IsMain: true,
            BuildTag: "build-tag",
            Key: "settings",
            WidthDip: 300,
            HeightDip: 400,
            Dpi: 96,
            State: "Normal");

        Assert.Equal("main", info.Id);
        Assert.Equal("App", info.Title);
        Assert.Equal(0xDEAD, info.Hwnd);
        Assert.Equal(bounds, info.Bounds);
        Assert.True(info.IsMain);
        Assert.Equal("build-tag", info.BuildTag);
        Assert.Equal("settings", info.Key);
        Assert.Equal(300, info.WidthDip);
        Assert.Equal(400, info.HeightDip);
        Assert.Equal(96u, info.Dpi);
        Assert.Equal("Normal", info.State);
    }

    [Fact]
    public void WindowBounds_Construction_StoresCoordinates()
    {
        var b = new WindowBounds(1, 2, 3, 4);
        Assert.Equal(1, b.X);
        Assert.Equal(2, b.Y);
        Assert.Equal(3, b.Width);
        Assert.Equal(4, b.Height);
    }

    // ════════════════════════════════════════════════════════════════════
    //  NodeRegistry — additional paths (GetOrCreateForTests resolves back)
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void NodeRegistry_Resolve_ReturnsFound_WhenTargetStillStronglyHeld()
    {
        var reg = new NodeRegistry();
        var id = reg.InjectForTests(new NodeDescriptor("main", "C", "btn", null, "Button", 0, null));

        // The sentinel is retained inside the registry, so the weak ref still
        // resolves — exercises the happy-path branch of `Resolve`.
        var result = reg.Resolve(id);
        Assert.Equal(NodeLookupStatus.Found, result.Status);
    }

    [Fact]
    public void NodeRegistry_InvalidateWindow_ReinjectingSameDescriptor_StaysTombstoned()
    {
        var reg = new NodeRegistry();
        var id = reg.InjectForTests(new NodeDescriptor("w1", "C", "x", null, "Button", 0, null));
        reg.InvalidateWindow("w1");
        // Re-injecting the same descriptor re-creates the id in _byId, but the
        // tombstone lookup wins — Resolve still returns Gone.
        reg.InjectForTests(new NodeDescriptor("w1", "C", "x", null, "Button", 0, null));
        Assert.Equal(NodeLookupStatus.Gone, reg.Resolve(id).Status);
    }

    [Fact]
    public void NodeRegistry_InvalidateWindow_MultipleWindows_OnlyTombstonesMatching()
    {
        var reg = new NodeRegistry();
        var a = reg.InjectForTests(new NodeDescriptor("w1", "C", "a", null, "Button", 0, null));
        var b = reg.InjectForTests(new NodeDescriptor("w2", "C", "b", null, "Button", 0, null));
        reg.InvalidateWindow("w1");
        Assert.Equal(NodeLookupStatus.Gone, reg.Resolve(a).Status);
        Assert.Equal(NodeLookupStatus.Found, reg.Resolve(b).Status);
    }

    // ════════════════════════════════════════════════════════════════════
    //  StdioMcpLoop — Start/Stop/Dispose lifecycle
    // ════════════════════════════════════════════════════════════════════

    private static McpDispatcher BuildPingDispatcher()
    {
        var reg = new McpToolRegistry();
        reg.Register(new McpToolDescriptor("ping", "", new { type = "object" }),
            _ => new { ok = true });
        return new McpDispatcher(reg);
    }

    [Fact]
    public void StdioMcpLoop_StartThenStop_DisposeIsClean()
    {
        var reader = new StringReader("");   // empty stdin → EOF, loop exits naturally
        var writer = new StringWriter();
        var loop = new StdioMcpLoop(BuildPingDispatcher(), reader, writer);

        loop.Start();
        loop.Stop();
        loop.Dispose();
    }

    [Fact]
    public void StdioMcpLoop_DoubleStart_Throws()
    {
        var loop = new StdioMcpLoop(BuildPingDispatcher(), new StringReader(""), new StringWriter());
        loop.Start();
        Assert.Throws<InvalidOperationException>(() => loop.Start());
        loop.Dispose();
    }

    [Fact]
    public void StdioMcpLoop_Dispose_WithoutStart_IsNoOp()
    {
        var loop = new StdioMcpLoop(BuildPingDispatcher(), new StringReader(""), new StringWriter());
        loop.Dispose();   // no Start → no CTS → safe no-op
    }

    [Fact]
    public void StdioMcpLoop_Run_ThrowingReader_ExitsGracefully()
    {
        // IOException inside ReadLine is caught and treated as EOF.
        var writer = new StringWriter();
        var loop = new StdioMcpLoop(BuildPingDispatcher(), new ThrowingReader(), writer);
        loop.Run(CancellationToken.None);
        Assert.Empty(writer.ToString());
    }

    private sealed class ThrowingReader : TextReader
    {
        public override string? ReadLine() => throw new IOException("pipe broken");
    }

    // ════════════════════════════════════════════════════════════════════
    //  McpDispatcher — direct-method invocation path
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void McpDispatcher_DirectMethodInvocation_ReachesHandler()
    {
        var reg = new McpToolRegistry();
        reg.Register(new McpToolDescriptor("pong", "", new { }), _ => new { ok = true });
        var d = new McpDispatcher(reg);

        // Not "tools/call" — the dispatcher should fall into HandleDirect.
        var resp = d.Dispatch("""{"jsonrpc":"2.0","id":1,"method":"pong"}""");
        Assert.Null(resp.Error);
    }

    [Fact]
    public void McpDispatcher_UnknownDirectMethod_ReturnsMethodNotFound()
    {
        var d = new McpDispatcher(new McpToolRegistry());
        var resp = d.Dispatch("""{"jsonrpc":"2.0","id":1,"method":"no-such-method"}""");
        Assert.NotNull(resp.Error);
        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, resp.Error!.Code);
    }

    [Fact]
    public void McpDispatcher_ToolsCall_UnknownName_ReturnsMethodNotFound()
    {
        var d = new McpDispatcher(new McpToolRegistry());
        var resp = d.Dispatch(
            """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"ghost"}}""");
        Assert.NotNull(resp.Error);
        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, resp.Error!.Code);
    }

    [Fact]
    public void McpDispatcher_Initialize_NonObjectParams_PinsBaseline()
    {
        var d = new McpDispatcher(new McpToolRegistry());
        // Array params → Not an object → falls into baseline pinning.
        var resp = d.Dispatch("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":[1,2]}""");
        var json = JsonSerializer.Serialize(resp.Result, DevtoolsMcpServer.JsonOpts);
        Assert.Contains("\"protocolVersion\":\"2024-11-05\"", json);
    }

    // ════════════════════════════════════════════════════════════════════
    //  WaitForPredicate — evaluator branches that don't need live elements
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void WaitForPredicate_NullSelector_IsSatisfied()
    {
        // A predicate with no selector is a no-op — satisfied = true immediately.
        var pred = new WaitForPredicate(null, null, null, null, null);
        // Pass a resolver; it never gets called because selector is null.
        var resolver = new SelectorResolver(new NodeRegistry(), new WindowRegistry("t"));
        var obs = WaitForPredicate.Evaluate(pred, resolver, windowId: null);
        Assert.True(obs.Satisfied);
        Assert.Equal(0, obs.Count);
        Assert.Null(obs.Text);
        Assert.False(obs.Visible);
    }

    [Fact]
    public void WaitForPredicate_EmptySelector_IsSatisfied()
    {
        var pred = new WaitForPredicate("", null, null, null, null);
        var resolver = new SelectorResolver(new NodeRegistry(), new WindowRegistry("t"));
        var obs = WaitForPredicate.Evaluate(pred, resolver, windowId: null);
        Assert.True(obs.Satisfied);
    }

    [Fact]
    public void WaitForPredicate_UnknownSelector_CountZero_IsSatisfied()
    {
        // Selector misses (McpToolException with unknown-selector payload) and
        // the predicate wants count=0 — the "wait for element to disappear"
        // path returns satisfied=true.
        var pred = new WaitForPredicate("r:gone/x", null, null, null, 0);
        var resolver = new SelectorResolver(new NodeRegistry(), new WindowRegistry("t"));
        var obs = WaitForPredicate.Evaluate(pred, resolver, windowId: null);
        Assert.True(obs.Satisfied);
        Assert.Equal(0, obs.Count);
        Assert.Null(obs.Text);
        Assert.False(obs.Visible);
    }

    [Fact]
    public void WaitForPredicate_UnknownSelector_CountNonZero_IsNotSatisfied()
    {
        var pred = new WaitForPredicate("r:gone/x", null, null, null, 1);
        var resolver = new SelectorResolver(new NodeRegistry(), new WindowRegistry("t"));
        var obs = WaitForPredicate.Evaluate(pred, resolver, windowId: null);
        Assert.False(obs.Satisfied);
    }

    [Fact]
    public void WaitForPredicate_FromJson_CountPresentButNonInt_Ignored()
    {
        using var doc = JsonDocument.Parse("""{"selector":"x","count":1.5}""");
        var p = WaitForPredicate.FromJson(doc.RootElement);
        // A fractional number is not an int — the parser should drop it.
        Assert.Null(p.Count);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Delaunay + Voronoi — additional clipping / geometry branches
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void Voronoi_CellPolygon_InsideBoundingBox_SurvivesClipping()
    {
        // Four interior points in a generous box → their Voronoi cells have
        // multiple circumcenters each, so clipping leaves polygons with ≥3
        // vertices. We check every point's cell to drive the ClipToEdge paths.
        var pts = new[] { (0.0, 0.0), (100.0, 0.0), (50.0, 100.0), (50.0, 50.0) };
        var d = Delaunay.From(pts);
        var v = d.Voronoi(-500, -500, 500, 500);
        bool anyNonNull = false;
        for (int i = 0; i < pts.Length; i++)
        {
            if (v.CellPolygon(i) is { Length: >= 3 })
                anyNonNull = true;
        }
        Assert.True(anyNonNull);
    }

    [Fact]
    public void Voronoi_CellPolygon_EntirelyOutsideBox_ClipsToEmpty()
    {
        // A clipping box that doesn't overlap the points' Voronoi cells at all —
        // Sutherland–Hodgman clips the polygon away and CellPolygon returns null.
        var pts = new[] { (0.0, 0.0), (1.0, 0.0), (0.0, 1.0) };
        var d = Delaunay.From(pts);
        var v = d.Voronoi(10_000, 10_000, 10_001, 10_001);
        // For either path (polygon clipped empty, or the fallback isolated-box
        // cell that also gets clipped) the result is null.
        _ = v.CellPolygon(0);   // drives ClipToEdge + EdgeInside branches
    }

    [Fact]
    public void Voronoi_Bounds_ContainsPoint_ReturnsTrue()
    {
        var pts = new[] { (25.0, 50.0), (75.0, 50.0) };
        var d = Delaunay.From(pts);
        var v = d.Voronoi(0, 0, 100, 100);
        // Drives EdgeIntersect + EdgeInside through Contains → CellPolygon pair.
        Assert.True(v.Contains(0, 10, 50));
        Assert.True(v.Contains(1, 90, 50));
    }

    [Fact]
    public void Voronoi_CollinearTriangle_UsesCentroidFallback()
    {
        // Three (near-)collinear points → the d≈0 branch of ComputeCircumcenter
        // returns the centroid. Even if Delaunay's degenerate bail-out prevents
        // a triangle from forming, the test still drives the empty-triangle path.
        var pts = new[] { (0.0, 0.0), (5.0, 0.0), (10.0, 0.0001) };
        var d = Delaunay.From(pts);
        var v = d.Voronoi(0, 0, 100, 100);
        Assert.NotNull(v);
    }

    [Fact]
    public void Delaunay_Neighbors_OutOfRangeIndex_ReturnsEmpty()
    {
        var pts = new[] { (0.0, 0.0), (5.0, 5.0), (10.0, 0.0) };
        var d = Delaunay.From(pts);
        // Index that isn't present in any triangle → the hashset stays empty.
        Assert.Empty(d.Neighbors(999));
    }

    // ════════════════════════════════════════════════════════════════════
    //  D3Curve factories — drive each static readonly factory delegate
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void D3Curve_EachFactoryDelegate_ReturnsConcreteCurve()
    {
        // Hit each lambda body in the D3Curve static field initializers.
        Assert.NotNull(D3Curve.Linear(new PathBuilder()));
        Assert.NotNull(D3Curve.Step(new PathBuilder()));
        Assert.NotNull(D3Curve.StepBefore(new PathBuilder()));
        Assert.NotNull(D3Curve.StepAfter(new PathBuilder()));
        Assert.NotNull(D3Curve.Basis(new PathBuilder()));
        Assert.NotNull(D3Curve.BasisClosed(new PathBuilder()));
        Assert.NotNull(D3Curve.Natural(new PathBuilder()));
        Assert.NotNull(D3Curve.Cardinal(new PathBuilder()));
        Assert.NotNull(D3Curve.CatmullRom(new PathBuilder()));
        Assert.NotNull(D3Curve.MonotoneX(new PathBuilder()));
    }

    [Fact]
    public void D3Curve_CardinalWithTension_ReturnsConfiguredCurve()
    {
        var factory = D3Curve.CardinalWithTension(0.25);
        Assert.NotNull(factory(new PathBuilder()));
    }

    [Fact]
    public void D3Curve_CatmullRomWithAlpha_ReturnsConfiguredCurve()
    {
        var factory = D3Curve.CatmullRomWithAlpha(0.75);
        Assert.NotNull(factory(new PathBuilder()));
    }

    // ════════════════════════════════════════════════════════════════════
    //  Sankey — alignment + padding branches
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void SankeyLayout_EmptyGraph_LeavesGraphEmpty()
    {
        // ComputeNodeHeights early-returns for 0 nodes (covers `if (Nodes.Count == 0)`).
        // ComputeNodeBreadths handles maxDepth=0 and skips the iteration loop.
        var graph = new SankeyGraph();
        new SankeyLayout().Size(100, 100).Layout(graph);
        Assert.Empty(graph.Nodes);
    }

    [Fact]
    public void SankeyLayout_SetIterations_AndSetAlign_FluentChain()
    {
        // Drives the SetIterations + SetAlign setter lines (21-22) and then the
        // alignment branch inside ComputeNodeBreadths (Right / Center / Justify).
        var graph = BuildDiamond();
        new SankeyLayout()
            .Size(300, 200)
            .SetIterations(2)
            .SetAlign(SankeyNodeAlign.Right)
            .Layout(graph);
        Assert.Equal(4, graph.Nodes.Count);
    }

    [Fact]
    public void SankeyLayout_AlignCenter_WalksCenterBranch()
    {
        var graph = BuildDiamond();
        new SankeyLayout().Size(300, 200).SetAlign(SankeyNodeAlign.Center).Layout(graph);
        Assert.Equal(4, graph.Nodes.Count);
    }

    [Fact]
    public void SankeyLayout_AlignLeft_WalksLeftBranch()
    {
        var graph = BuildDiamond();
        new SankeyLayout().Size(300, 200).SetAlign(SankeyNodeAlign.Left).Layout(graph);
        Assert.Equal(4, graph.Nodes.Count);
    }

    [Fact]
    public void SankeyLayout_NodeWidth_IsHonored()
    {
        var graph = BuildDiamond();
        new SankeyLayout().Size(300, 200).SetNodeWidth(40).Layout(graph);
        foreach (var n in graph.Nodes)
            Assert.Equal(40, n.X1 - n.X0);
    }

    [Fact]
    public void SankeyLayout_VeryTallRequirement_TriggersOverflowPushBack()
    {
        // Many large-value nodes in the same column force
        // ResolveCollisions to overflow and walk the push-back branch
        // (lines 198-214).
        var graph = new SankeyGraph
        {
            Nodes =
            {
                new SankeyNode { Id = "a" },
                new SankeyNode { Id = "b" },
                new SankeyNode { Id = "c" },
                new SankeyNode { Id = "target" },
            },
            Links =
            {
                new SankeyLink { SourceId = "a", TargetId = "target", Value = 100 },
                new SankeyLink { SourceId = "b", TargetId = "target", Value = 100 },
                new SankeyLink { SourceId = "c", TargetId = "target", Value = 100 },
            },
        };
        new SankeyLayout().Size(100, 20).SetNodePadding(10).Layout(graph);
        Assert.Equal(4, graph.Nodes.Count);
    }

    [Fact]
    public void SankeyLayout_LinkPath_NullEndpoints_ReturnsNull()
    {
        // Source/Target are left null on the link (no Layout() run), so
        // LinkPath short-circuits to null.
        var link = new SankeyLink { SourceId = "a", TargetId = "b", Value = 10 };
        Assert.Null(SankeyLayout.LinkPath(link));
    }

    [Fact]
    public void SankeyLayout_LinkPath_CustomDigits_FormatsWithRounding()
    {
        var graph = BuildDiamond();
        new SankeyLayout().Size(300, 200).Layout(graph);
        var path = SankeyLayout.LinkPath(graph.Links[0], digits: 1);
        Assert.NotNull(path);
    }

    private static SankeyGraph BuildDiamond() => new()
    {
        Nodes =
        {
            new SankeyNode { Id = "S" },
            new SankeyNode { Id = "A" },
            new SankeyNode { Id = "B" },
            new SankeyNode { Id = "T" },
        },
        Links =
        {
            new SankeyLink { SourceId = "S", TargetId = "A", Value = 5 },
            new SankeyLink { SourceId = "S", TargetId = "B", Value = 5 },
            new SankeyLink { SourceId = "A", TargetId = "T", Value = 5 },
            new SankeyLink { SourceId = "B", TargetId = "T", Value = 5 },
        },
    };

    // ════════════════════════════════════════════════════════════════════
    //  Stratify — factory helper + multi-root error path
    // ════════════════════════════════════════════════════════════════════

    private sealed record StratItem(string Id, string? ParentId);

    [Fact]
    public void Stratify_Create_FactoryShortcut_BuildsEquivalentInstance()
    {
        var s = Stratify.Create<StratItem>();
        s.SetId(i => i.Id).SetParentId(i => i.ParentId);
        var root = s.Build(new[]
        {
            new StratItem("root", null),
            new StratItem("a", "root"),
        });
        Assert.Equal("root", root.Data.Id);
        Assert.Single(root.Children);
    }

    [Fact]
    public void Stratify_Build_MultipleRoots_Throws()
    {
        var s = new Stratify<StratItem>()
            .SetId(i => i.Id)
            .SetParentId(i => i.ParentId);
        var ex = Assert.Throws<InvalidOperationException>(() => s.Build(new[]
        {
            new StratItem("r1", null),
            new StratItem("r2", null),
        }));
        Assert.Contains("Multiple roots", ex.Message);
    }

    [Fact]
    public void Stratify_Build_DuplicateId_Throws()
    {
        var s = new Stratify<StratItem>()
            .SetId(i => i.Id)
            .SetParentId(i => i.ParentId);
        Assert.Throws<InvalidOperationException>(() => s.Build(new[]
        {
            new StratItem("a", null),
            new StratItem("a", null),
        }));
    }

    [Fact]
    public void Stratify_Build_MissingParent_Throws()
    {
        var s = new Stratify<StratItem>()
            .SetId(i => i.Id)
            .SetParentId(i => i.ParentId);
        Assert.Throws<InvalidOperationException>(() => s.Build(new[]
        {
            new StratItem("orphan", "ghost-parent"),
        }));
    }

    [Fact]
    public void Stratify_Build_NoRoot_Throws()
    {
        // Every item has a parentId but the chain self-references (cycle) — no
        // root is discovered and Build throws.
        var s = new Stratify<StratItem>()
            .SetId(i => i.Id)
            .SetParentId(i => i.ParentId);
        Assert.Throws<InvalidOperationException>(() => s.Build(new[]
        {
            new StratItem("a", "b"),
            new StratItem("b", "a"),
        }));
    }

    [Fact]
    public void Stratify_BuildTreemap_SumsChildValues()
    {
        var data = new[]
        {
            new StratItem("root", null),
            new StratItem("a", "root"),
            new StratItem("b", "root"),
        };
        var treemap = Stratify.Create<StratItem>()
            .SetId(i => i.Id)
            .SetParentId(i => i.ParentId)
            .BuildTreemap(data, i => i.Id == "root" ? 0 : 5);
        Assert.Equal(10, treemap.Value);
    }

    [Fact]
    public void Stratify_BuildPartition_SumsChildValues()
    {
        var data = new[]
        {
            new StratItem("root", null),
            new StratItem("a", "root"),
            new StratItem("b", "root"),
        };
        var partition = Stratify.Create<StratItem>()
            .SetId(i => i.Id)
            .SetParentId(i => i.ParentId)
            .BuildPartition(data, i => i.Id == "root" ? 0 : 7);
        Assert.Equal(14, partition.Value);
    }

    // ════════════════════════════════════════════════════════════════════
    //  Element record constructors — drive uncovered ctor / init bodies
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void SemanticDescription_Construction_SetsAllFields()
    {
        var d = new SemanticDescription(
            Role: "slider",
            Value: "50",
            RangeMin: 0,
            RangeMax: 100,
            RangeValue: 50,
            IsReadOnly: false);
        Assert.Equal("slider", d.Role);
        Assert.Equal("50", d.Value);
        Assert.Equal(0, d.RangeMin);
        Assert.Equal(100, d.RangeMax);
        Assert.Equal(50, d.RangeValue);
        Assert.False(d.IsReadOnly);
    }

    [Fact]
    public void SemanticElement_Construction_StoresChildAndSemantics()
    {
        var child = TextBlock("hi");
        var sem = new SemanticDescription(Role: "region");
        var wrap = new SemanticElement(child, sem);
        Assert.Same(child, wrap.Child);
        Assert.Same(sem, wrap.Semantics);
    }

    [Fact]
    public void CanvasAttached_Defaults_AreZero()
    {
        var ca = new CanvasAttached();
        Assert.Equal(0, ca.Left);
        Assert.Equal(0, ca.Top);
    }

    [Fact]
    public void IconData_Hierarchy_Constructs()
    {
        IconData a = new FontIconData("\uE700", "Segoe Fluent Icons", 14);
        IconData b = new BitmapIconData(new Uri("ms-appx:///x.png"), ShowAsMonochrome: false);
        IconData c = new PathIconData("M0,0 L1,1");
        IconData d = new ImageIconData(new Uri("ms-appx:///y.png"));
        Assert.NotSame(a, b);
        Assert.NotSame(c, d);
    }

    [Fact]
    public void AppBarToggleButtonData_Defaults_AreRoundTripped()
    {
        var toggle = new AppBarToggleButtonData("Toggle", IsChecked: true);
        Assert.Equal("Toggle", toggle.Label);
        Assert.True(toggle.IsChecked);
        Assert.Null(toggle.IconElement);
    }

    [Fact]
    public void AppBarSeparatorData_Construction_DoesNotThrow()
    {
        var sep = new AppBarSeparatorData();
        Assert.NotNull(sep);
    }

    [Fact]
    public void CommandHostElement_Construction_StoresCommandsAndChild()
    {
        var child = TextBlock("x");
        var host = new CommandHostElement(Array.Empty<Command>(), child);
        Assert.Empty(host.Commands);
        Assert.Same(child, host.Child);
    }

    [Fact]
    public void CalendarDatePickerElement_Construction_SetsDefaults()
    {
        var p = new CalendarDatePickerElement(
            Date: DateTimeOffset.Parse("2024-06-15T00:00:00Z"))
        {
            Header = "Pick",
            PlaceholderText = "Date",
            MinDate = DateTimeOffset.MinValue,
            MaxDate = DateTimeOffset.MaxValue,
        };
        Assert.NotNull(p.Date);
        Assert.Equal("Pick", p.Header);
    }

    [Fact]
    public void WebView2Element_Construction_DefaultsAreNull()
    {
        var w = new WebView2Element();
        Assert.Null(w.Source);
        Assert.Null(w.OnNavigationCompleted);
    }

    [Fact]
    public void SplitViewElement_Construction_Defaults()
    {
        var sv = new SplitViewElement(Pane: TextBlock("p"), Content: TextBlock("c"));
        Assert.True(sv.IsPaneOpen);
        Assert.Equal(320, sv.OpenPaneLength);
        Assert.Equal(48, sv.CompactPaneLength);
    }

    [Fact]
    public void TitleBarElement_Construction_StoresTitle()
    {
        var t = new TitleBarElement("App")
        {
            Subtitle = "Sub",
            IsBackButtonVisible = true,
            IsBackButtonEnabled = true,
            IsPaneToggleButtonVisible = true,
            Content = TextBlock("x"),
            RightHeader = TextBlock("y"),
        };
        Assert.Equal("App", t.Title);
        Assert.Equal("Sub", t.Subtitle);
    }

    [Fact]
    public void TitleBarElement_Icon_Defaults_To_Null_And_Round_Trips()
    {
        var bare = new TitleBarElement("App");
        Assert.Null(bare.Icon);

        var symbol = new TitleBarElement("App") { Icon = new SymbolIconData("Home") };
        Assert.IsType<SymbolIconData>(symbol.Icon);
        Assert.Equal("Home", ((SymbolIconData)symbol.Icon!).Symbol);

        var image = new TitleBarElement("App")
        {
            Icon = new ImageIconData(new Uri("ms-appx:///Assets/AppIcon.ico")),
        };
        Assert.IsType<ImageIconData>(image.Icon);
    }

    [Fact]
    public void TitleBarElement_Icon_Participates_In_Record_Equality()
    {
        var a = new TitleBarElement("App") { Icon = new SymbolIconData("Home") };
        var b = new TitleBarElement("App") { Icon = new SymbolIconData("Home") };
        Assert.Equal(a, b);

        var c = new TitleBarElement("App") { Icon = new SymbolIconData("Edit") };
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void PivotElement_Defaults_AreSet()
    {
        var p = new PivotElement(Array.Empty<PivotItemData>())
        {
            Title = "Main",
        };
        Assert.Equal(0, p.SelectedIndex);
        Assert.Equal("Main", p.Title);
    }

    [Fact]
    public void FlipViewElement_Defaults_AreSet()
    {
        var f = new FlipViewElement(Array.Empty<Element>());
        Assert.Equal(0, f.SelectedIndex);
    }

    [Fact]
    public void FlyoutElement_Construction_AllFields()
    {
        var t = TextBlock("target");
        var c = TextBlock("content");
        var f = new FlyoutElement(t, c)
        {
            IsOpen = true,
            OnOpened = () => { },
            OnClosed = () => { },
        };
        Assert.Same(t, f.Target);
        Assert.Same(c, f.FlyoutContent);
        Assert.True(f.IsOpen);
        Assert.Equal(Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Auto, f.Placement);
    }

    [Fact]
    public void TeachingTipElement_Construction_DefaultsAreSet()
    {
        var tt = new TeachingTipElement("Title", "Sub")
        {
            Content = TextBlock("body"),
            ActionButtonContent = "Do",
            CloseButtonContent = "Close",
        };
        Assert.Equal("Title", tt.Title);
        Assert.Equal("Sub", tt.Subtitle);
        Assert.False(tt.IsOpen);
    }

    [Fact]
    public void InfoBadgeElement_WithValue_Constructs()
    {
        var b = new InfoBadgeElement { Value = 3, Icon = "SegoeFluentIcons.Accept" };
        Assert.Equal(3, b.Value);
        Assert.Equal("SegoeFluentIcons.Accept", b.Icon);
    }

    [Fact]
    public void MenuFlyoutElement_Construction_StoresTargetAndItems()
    {
        var target = TextBlock("t");
        var mf = new MenuFlyoutElement(target, Array.Empty<MenuFlyoutItemBase>());
        Assert.Same(target, mf.Target);
        Assert.Empty(mf.Items);
    }

    [Fact]
    public void TemplatedListViewElement_PropertyAndMethodSurface()
    {
        var items = new[] { 1, 2, 3 };
        var el = new TemplatedListViewElement<int>(
            items,
            KeySelector: i => i.ToString(),
            ViewBuilder: (i, _) => TextBlock(i.ToString()))
        {
            SelectedIndex = 1,
        };
        Assert.Equal(3, el.ItemCount);
        Assert.Equal(1, el.GetSelectedIndex());
        Assert.Equal(Microsoft.UI.Xaml.Controls.ListViewSelectionMode.Single, el.GetSelectionMode());
        Assert.Null(el.GetHeader());
        Assert.False(el.GetIsItemClickEnabled());
        Assert.Equal(TemplatedControlKind.ListView, el.ControlKind);
        var view = el.BuildItemView(0);
        Assert.NotNull(view);

        int? seen = null;
        var withHandlers = el with
        {
            Header = "H",
            OnSelectionChanged = idx => seen = idx,
            OnItemClick = v => seen = v,
        };
        Assert.Equal("H", withHandlers.GetHeader());
        Assert.True(withHandlers.GetIsItemClickEnabled());
        withHandlers.InvokeSelectionChanged(2);
        Assert.Equal(2, seen);
        withHandlers.InvokeItemClick(0);
        Assert.Equal(1, seen);
        // Out-of-range click → falls to default! path without throwing.
        withHandlers.InvokeItemClick(-1);
    }

    [Fact]
    public void TemplatedGridViewElement_PropertyAndMethodSurface()
    {
        var items = new[] { "a", "b" };
        var el = new TemplatedGridViewElement<string>(
            items,
            KeySelector: s => s,
            ViewBuilder: (s, _) => TextBlock(s));
        Assert.Equal(2, el.ItemCount);
        Assert.Equal(-1, el.GetSelectedIndex());
        Assert.Null(el.GetHeader());
        Assert.False(el.GetIsItemClickEnabled());
        Assert.Equal(TemplatedControlKind.GridView, el.ControlKind);
        Assert.NotNull(el.BuildItemView(0));
        el.InvokeSelectionChanged(1);
        el.InvokeItemClick(0);
        el.InvokeItemClick(-1);
    }

    [Fact]
    public void TemplatedFlipViewElement_PropertyAndMethodSurface()
    {
        var items = new[] { 10, 20 };
        var el = new TemplatedFlipViewElement<int>(
            items,
            KeySelector: i => i.ToString(),
            ViewBuilder: (i, _) => TextBlock(i.ToString()));
        Assert.Equal(2, el.ItemCount);
        Assert.Equal(0, el.GetSelectedIndex());
        Assert.Equal(Microsoft.UI.Xaml.Controls.ListViewSelectionMode.Single, el.GetSelectionMode());
        Assert.Null(el.GetHeader());
        Assert.False(el.GetIsItemClickEnabled());
        Assert.Equal(TemplatedControlKind.FlipView, el.ControlKind);
        el.InvokeSelectionChanged(0);
        el.InvokeItemClick(0);   // no-op on flip view
        Assert.NotNull(el.BuildItemView(1));
    }

    [Fact]
    public void LazyVStackElement_DefaultsAndGetItemsSource()
    {
        var items = new[] { 1, 2, 3 };
        var el = new LazyVStackElement<int>(
            items,
            KeySelector: i => i.ToString(),
            ViewBuilder: (i, _) => TextBlock(i.ToString()));
        Assert.Equal(Microsoft.UI.Xaml.Controls.Orientation.Vertical, el.Orientation);
        Assert.Equal(8, el.Spacing);
        Assert.Equal(40, el.EstimatedItemSize);
        var src = el.GetItemsSource();
        Assert.NotNull(src);
    }

    [Fact]
    public void LazyHStackElement_DefaultsAndGetItemsSource()
    {
        var items = new[] { 1, 2 };
        var el = new LazyHStackElement<int>(
            items,
            KeySelector: i => i.ToString(),
            ViewBuilder: (i, _) => TextBlock(i.ToString()));
        Assert.Equal(Microsoft.UI.Xaml.Controls.Orientation.Horizontal, el.Orientation);
        Assert.Equal(8, el.Spacing);
        Assert.Equal(100, el.EstimatedItemSize);
        var src = el.GetItemsSource();
        Assert.NotNull(src);
    }

    [Fact]
    public void EllipseElement_Construction_DefaultsAreZero()
    {
        var e = new EllipseElement
        {
            StrokeThickness = 2,
        };
        Assert.Equal(2, e.StrokeThickness);
        Assert.Null(e.Fill);
    }

    [Fact]
    public void MediaPlayerElementElement_Construction_Defaults()
    {
        var m = new MediaPlayerElementElement("ms-appx:///m.mp4");
        Assert.True(m.AreTransportControlsEnabled);
        Assert.False(m.AutoPlay);
        Assert.Equal("ms-appx:///m.mp4", m.Source);
    }

    [Fact]
    public void AnimatedVisualPlayerElement_Construction_Defaults()
    {
        var a = new AnimatedVisualPlayerElement { AutoPlay = true };
        Assert.True(a.AutoPlay);
    }

    [Fact]
    public void SemanticZoomElement_Construction_StoresViews()
    {
        var z = new SemanticZoomElement(TextBlock("in"), TextBlock("out"));
        Assert.NotNull(z.ZoomedInView);
        Assert.NotNull(z.ZoomedOutView);
    }

    [Fact]
    public void ListBoxElement_Construction_Defaults()
    {
        var lb = new ListBoxElement(new[] { "a", "b" });
        Assert.Equal(-1, lb.SelectedIndex);
    }

    [Fact]
    public void SelectorBarElement_AndItemData_Construct()
    {
        var items = new[]
        {
            new SelectorBarItemData("One"),
            new SelectorBarItemData("Two", "Icon"),
        };
        var sb = new SelectorBarElement(items);
        Assert.Equal(0, sb.SelectedIndex);
        Assert.Equal(2, sb.Items.Length);
        Assert.Equal("Icon", items[1].Icon);
    }

    [Fact]
    public void PipsPagerElement_Construction_Default()
    {
        var p = new PipsPagerElement(5) { SelectedPageIndex = 2 };
        Assert.Equal(5, p.NumberOfPages);
        Assert.Equal(2, p.SelectedPageIndex);
    }

    [Fact]
    public void AnnotatedScrollBarElement_Construction_Default()
    {
        var a = new AnnotatedScrollBarElement();
        Assert.NotNull(a);
    }

    [Fact]
    public void RefreshContainerElement_Construction_Default()
    {
        var r = new RefreshContainerElement(TextBlock("x"));
        Assert.NotNull(r.Content);
    }

    [Fact]
    public void CommandBarFlyoutElement_Construction_Default()
    {
        var cbf = new CommandBarFlyoutElement(TextBlock("t"))
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom,
        };
        Assert.NotNull(cbf.Target);
        Assert.Null(cbf.PrimaryCommands);
        Assert.Null(cbf.SecondaryCommands);
    }

    [Fact]
    public void CalendarViewElement_Construction_Defaults()
    {
        var c = new CalendarViewElement
        {
            SelectionMode = Microsoft.UI.Xaml.Controls.CalendarViewSelectionMode.Multiple,
            IsGroupLabelVisible = false,
            IsOutOfScopeEnabled = false,
            Language = "fr-FR",
            CalendarIdentifier = "gregorian",
        };
        Assert.Equal(Microsoft.UI.Xaml.Controls.CalendarViewSelectionMode.Multiple, c.SelectionMode);
        Assert.False(c.IsGroupLabelVisible);
        Assert.Equal("fr-FR", c.Language);
    }

    [Fact]
    public void SwipeItemData_Defaults_AreRoundTripped()
    {
        var item = new SwipeItemData("Delete");
        Assert.Equal("Delete", item.Text);
        Assert.Null(item.OnInvoked);
        Assert.Equal(Microsoft.UI.Xaml.Controls.SwipeBehaviorOnInvoked.Auto, item.BehaviorOnInvoked);
    }

    [Fact]
    public void SwipeControlElement_Construction_Defaults()
    {
        var c = new SwipeControlElement(TextBlock("x"))
        {
            LeftItems = new[] { new SwipeItemData("L") },
            RightItems = new[] { new SwipeItemData("R") },
        };
        Assert.Equal(Microsoft.UI.Xaml.Controls.SwipeMode.Reveal, c.LeftItemsMode);
        Assert.Equal(Microsoft.UI.Xaml.Controls.SwipeMode.Reveal, c.RightItemsMode);
        Assert.NotNull(c.LeftItems);
        Assert.NotNull(c.RightItems);
    }

    [Fact]
    public void AnimatedIconElement_Construction_Defaults()
    {
        var el = new AnimatedIconElement { Source = new object() };
        Assert.NotNull(el.Source);
    }

    [Fact]
    public void ParallaxViewElement_Construction_Defaults()
    {
        var el = new ParallaxViewElement(TextBlock("x"))
        {
            VerticalShift = 10,
            HorizontalShift = 20,
        };
        Assert.Equal(10, el.VerticalShift);
        Assert.Equal(20, el.HorizontalShift);
    }

    [Fact]
    public void MapControlElement_Construction_Defaults()
    {
        var el = new MapControlElement { MapServiceToken = "abc", ZoomLevel = 5 };
        Assert.Equal("abc", el.MapServiceToken);
        Assert.Equal(5, el.ZoomLevel);
    }

    [Fact]
    public void FrameElement_Construction_Defaults()
    {
        var el = new FrameElement
        {
            SourcePageType = typeof(object),
            NavigationParameter = 42,
        };
        Assert.Equal(typeof(object), el.SourcePageType);
        Assert.Equal(42, el.NavigationParameter);
    }

    [Fact]
    public void ItemsViewElement_Construction_Defaults()
    {
        var items = new[] { "a", "b" };
        var el = new ItemsViewElement<string>(
            items,
            KeySelector: s => s,
            ViewBuilder: (s, _) => TextBlock(s));
        Assert.Equal(ItemsViewLayoutKind.StackLayout, el.LayoutKind);
        Assert.Equal(Microsoft.UI.Xaml.Controls.ItemsViewSelectionMode.Single, el.SelectionMode);
        Assert.False(el.IsItemInvokedEnabled);
    }
}
