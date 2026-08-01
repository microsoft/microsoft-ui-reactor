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
        var findings = DiagramProcessor.ValidateImageRefs(
            "topic.md.dt", body, _images, Path.GetDirectoryName(_images)!);
        Assert.Contains(findings, f => f.Code == "REACTOR_DOC_IMAGE_001");
    }

    [Fact]
    public void Resolved_image_ref_is_clean()
    {
        Directory.CreateDirectory(Path.Combine(_images, "arch"));
        File.WriteAllText(Path.Combine(_images, "arch", "ok.svg"), "<svg/>");
        var body = "Body.\n\n![diagram](images/arch/ok.svg)\n";
        var findings = DiagramProcessor.ValidateImageRefs(
            "topic.md.dt", body, _images, Path.GetDirectoryName(_images)!);
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

    /// <summary>
    /// Pins the *complete* set of files the diagram phase writes under the
    /// images root, and that none of them is a <c>.png</c>.
    /// </summary>
    /// <remarks>
    /// This is the executable form of a claim that is otherwise only prose, in
    /// <c>docs/contributing/doc-pipeline.md</c> § "Screenshots and committed
    /// images" and in the Phase 3 skip comment in <c>CompileCommand</c>: that
    /// <c>--no-screenshots</c> guarantees committed *screenshots* are untouched,
    /// while this phase still writes text files into the same directory whose
    /// names cannot collide with a captured <c>.png</c>.
    ///
    /// Both of those comments originally enumerated two writers when there are
    /// three. The missing one is the <c>mmdc</c> render, and it was missing for
    /// a structural reason rather than by accident: it happens in a separate
    /// process, so it appears in no <c>File.Write*</c>/<c>File.Copy</c> search
    /// of this repository. An enumeration that can only be verified by reading
    /// is one that drifts, hence this test.
    ///
    /// What makes it non-vacuous: the stub runner writes to whatever path it is
    /// handed, so the <c>.svg</c> extension under assertion is the one the
    /// production call site hard-codes, not one the double chose. Changing that
    /// call site to <c>".png"</c> — which <c>mmdc</c> supports — fails this test.
    /// The exact-set assertion is deliberate: adding a fourth writer breaks a
    /// test that names both comments, instead of quietly outdating them.
    /// </remarks>
    [Fact]
    public void Diagram_phase_writes_three_text_files_and_no_png()
    {
        WriteSvg("arch", "passthrough", "<svg>copied</svg>");
        WriteMmd("arch", "rendered", "graph TD; A-->B;");

        DiagramProcessor.Process(_diagrams, _images, new FakeMermaid(available: true));

        var written = Directory.GetFiles(_images, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(_images, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        // Non-vacuity guard. Every assertion below is satisfied by an empty
        // directory for the wrong reason — "wrote nothing" and "wrote nothing
        // that is a .png" are the same output otherwise, which is the exact
        // collapse the gate under test exists to prevent.
        Assert.NotEmpty(written);

        Assert.Equal(
            new[]
            {
                "arch/.rendered.mmd.sha256",  // cache sidecar
                "arch/passthrough.svg",       // copied verbatim
                "arch/rendered.svg",          // rendered by mmdc, extension fixed here
            },
            written);

        Assert.DoesNotContain(written, p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
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
