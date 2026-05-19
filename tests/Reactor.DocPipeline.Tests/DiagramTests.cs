using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

public class DiagramTests : IDisposable
{
    private readonly string _root;
    private readonly string _diagrams;
    private readonly string _images;

    public DiagramTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "reactor-doc-diagram-tests-" + Guid.NewGuid().ToString("N"));
        _diagrams = Path.Combine(_root, "diagrams");
        _images = Path.Combine(_root, "images");
        Directory.CreateDirectory(_diagrams);
        Directory.CreateDirectory(_images);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string WriteSvg(string topic, string name, string content)
    {
        var dir = Path.Combine(_diagrams, topic);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".svg");
        File.WriteAllText(path, content);
        return path;
    }

    private string WriteMmd(string topic, string name, string content)
    {
        var dir = Path.Combine(_diagrams, topic);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name + ".mmd");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Svg_passthrough_copies_file()
    {
        WriteSvg("arch", "overview", "<svg/>");
        var result = DiagramProcessor.Process(_diagrams, _images, new FakeMermaid(available: true));
        Assert.Equal(new[] { "overview.svg" }, result.CopiedSvgs.ToArray());
        Assert.True(File.Exists(Path.Combine(_images, "arch", "overview.svg")));
    }

    [Fact]
    public void Svg_identical_content_is_skipped()
    {
        WriteSvg("arch", "overview", "<svg/>");
        var fake = new FakeMermaid(available: true);

        var first = DiagramProcessor.Process(_diagrams, _images, fake);
        Assert.Single(first.CopiedSvgs);

        var second = DiagramProcessor.Process(_diagrams, _images, fake);
        Assert.Empty(second.CopiedSvgs);
        Assert.Single(second.SkippedSvgs);
    }

    [Fact]
    public void Svg_changed_content_is_recopied()
    {
        WriteSvg("arch", "overview", "<svg>v1</svg>");
        var fake = new FakeMermaid(available: true);
        DiagramProcessor.Process(_diagrams, _images, fake);

        WriteSvg("arch", "overview", "<svg>v2</svg>");
        var second = DiagramProcessor.Process(_diagrams, _images, fake);
        Assert.Single(second.CopiedSvgs);
    }

    [Fact]
    public void Mermaid_render_with_missing_mmdc_emits_diagram_001()
    {
        WriteMmd("arch", "overview", "flowchart LR\nA-->B");
        var fake = new FakeMermaid(available: false);
        var result = DiagramProcessor.Process(_diagrams, _images, fake);
        Assert.Contains(result.Findings, f => f.Code == "REACTOR_DOC_DIAGRAM_001");
    }

    [Fact]
    public void Mermaid_render_invokes_runner_and_caches_by_content_hash()
    {
        WriteMmd("arch", "overview", "flowchart LR\nA-->B");
        var fake = new FakeMermaid(available: true);

        var first = DiagramProcessor.Process(_diagrams, _images, fake);
        Assert.Single(first.RenderedMermaid);
        Assert.Equal(1, fake.RenderCallCount);

        // Re-run with no .mmd change → cache hit.
        var second = DiagramProcessor.Process(_diagrams, _images, fake);
        Assert.Empty(second.RenderedMermaid);
        Assert.Single(second.CachedMermaid);
        Assert.Equal(1, fake.RenderCallCount);

        // Change content → re-render.
        WriteMmd("arch", "overview", "flowchart LR\nA-->C");
        var third = DiagramProcessor.Process(_diagrams, _images, fake);
        Assert.Single(third.RenderedMermaid);
        Assert.Equal(2, fake.RenderCallCount);
    }

    [Fact]
    public void Mermaid_runner_command_line_is_well_formed()
    {
        var runner = new MmdcRunner();
        var cmd = runner.CommandLine("a.mmd", "out/b.svg");
        Assert.Contains("mmdc", cmd);
        Assert.Contains("-i \"a.mmd\"", cmd);
        Assert.Contains("-o \"out/b.svg\"", cmd);
    }

    [Fact]
    public void Broken_image_ref_raises_IMAGE_001()
    {
        var body = "Body.\n\n![diagram](images/arch/missing.svg)\n";
        var findings = DiagramProcessor.ValidateImageRefs("topic.md.dt", body, _images);
        Assert.Contains(findings, f => f.Code == "REACTOR_DOC_IMAGE_001");
    }

    [Fact]
    public void Resolved_image_ref_is_clean()
    {
        Directory.CreateDirectory(Path.Combine(_images, "arch"));
        File.WriteAllText(Path.Combine(_images, "arch", "ok.svg"), "<svg/>");
        var body = "Body.\n\n![diagram](images/arch/ok.svg)\n";
        var findings = DiagramProcessor.ValidateImageRefs("topic.md.dt", body, _images);
        Assert.DoesNotContain(findings, f => f.Code == "REACTOR_DOC_IMAGE_001");
    }

    [Fact]
    public void Scaffold_creates_starter_template_at_expected_path()
    {
        var path = DiagramProcessor.ScaffoldDiagram(_diagrams, "arch", "overview");
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("flowchart LR", content);
    }

    [Fact]
    public void Scaffold_refuses_to_overwrite()
    {
        DiagramProcessor.ScaffoldDiagram(_diagrams, "arch", "overview");
        var ex = Assert.Throws<DocPipelineException>(() =>
            DiagramProcessor.ScaffoldDiagram(_diagrams, "arch", "overview"));
        Assert.Equal("REACTOR_DOC_DIAGRAM_002", ex.Code);
    }

    // ── Test double ───────────────────────────────────────────────────────

    /// <summary>
    /// Stub runner: records calls + writes a tiny placeholder SVG on render
    /// so the cache-hit path has a real file to detect.
    /// </summary>
    private sealed class FakeMermaid : IMermaidRunner
    {
        public FakeMermaid(bool available) { IsAvailable = available; }
        public bool IsAvailable { get; }
        public int RenderCallCount { get; private set; }

        public string CommandLine(string input, string output) => $"mmdc -i {input} -o {output}";

        public bool Render(string inputPath, string outputPath, out string error)
        {
            RenderCallCount++;
            error = "";
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(outputPath, "<svg>generated</svg>");
            return true;
        }
    }
}
