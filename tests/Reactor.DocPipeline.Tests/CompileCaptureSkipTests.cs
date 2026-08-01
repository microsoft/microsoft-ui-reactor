using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Issue #989 filed <c>mur docs compile --no-screenshots</c> as the thing that
/// replaced 103 committed screenshots with blank stubs. Phase 3 (capture) is the
/// pipeline's only binary writer and the flag skips it outright, so the flag is
/// non-destructive by construction — but "by construction" is exactly the kind
/// of property that quietly stops being true.
/// </summary>
/// <remarks>
/// <para>
/// What these tests actually prove, stated precisely so nobody reads more into
/// them than is there: the fixture reaches the Phase 3 <em>decision</em>, and on
/// the skip path no capture is attempted at all; on the non-skip path a capture
/// genuinely <em>is</em> attempted (the app is discovered, its manifest parsed,
/// and <c>CaptureAsync</c> entered), it fails for want of a project to launch,
/// the failure is counted and reported, the compile exits non-zero, and the
/// planted image is still byte-identical.
/// </para>
/// <para>
/// What they cannot prove: that a <em>successful</em> capture writes only where
/// it should. That needs a live WinUI desktop and a running doc app, so it lives
/// in the CI non-destructiveness gate (<c>docs-build</c> job in
/// <c>.github/workflows/ci.yml</c>), which runs the real binary against the real
/// corpus and diffs <c>docs/guide/images</c>.
/// </para>
/// <para>
/// <see cref="CompileCommand.Run"/> resolves the repo root from the process
/// working directory and writes to <see cref="Console"/>, both of which are
/// process-global, so this class shares the repo's console-isolation collection.
/// </para>
/// </remarks>
[Collection("ConsoleTests")]
public class CompileCaptureSkipTests
{
    [Fact]
    public void No_screenshots_leaves_committed_images_byte_identical()
    {
        using var repo = new FakeRepo();
        var planted = repo.PlantScreenshot("demo/widget.png");
        var before = global::System.IO.File.ReadAllBytes(planted);
        var beforeStamp = global::System.IO.File.GetLastWriteTimeUtc(planted);

        var (exitCode, output) = repo.Compile("--no-screenshots", "--no-build", "--skip-diagrams", "--skip-reference");

        Assert.Equal(0, exitCode);
        Assert.Contains("Phase 3: Capture (skipped", output);
        // The capture loop must not have run at all — no attempt, not merely a
        // failed one. This is the difference the flag is supposed to make, and
        // the paired test below shows the same fixture does reach it otherwise.
        Assert.DoesNotContain("Capturing for demo", output);
        Assert.Equal(before, global::System.IO.File.ReadAllBytes(planted));
        Assert.Equal(beforeStamp, global::System.IO.File.GetLastWriteTimeUtc(planted));
    }

    [Fact]
    public void Skip_screenshots_alias_behaves_the_same()
    {
        using var repo = new FakeRepo();
        var planted = repo.PlantScreenshot("demo/widget.png");
        var before = global::System.IO.File.ReadAllBytes(planted);

        var (exitCode, output) = repo.Compile("--skip-screenshots", "--no-build", "--skip-diagrams", "--skip-reference");

        Assert.Equal(0, exitCode);
        Assert.Contains("Phase 3: Capture (skipped", output);
        Assert.DoesNotContain("Capturing for demo", output);
        Assert.Equal(before, global::System.IO.File.ReadAllBytes(planted));
    }

    /// <summary>
    /// Control for the two tests above. Without it they would pass just as well
    /// against a harness that never reached the Phase 3 decision — the planted
    /// file would be untouched for the wrong reason and the "skipped" assertion
    /// would be testing nothing.
    /// </summary>
    /// <remarks>
    /// The fixture deliberately ships a <c>.cs</c> file (required by
    /// <c>CompileCommand.DiscoverApps</c>) and a manifest with one screenshot,
    /// so dropping the flag drives a real <c>CaptureAsync</c> call rather than
    /// an empty loop. An earlier version of this fixture had neither, the app
    /// was never discovered, and every assertion here was vacuous.
    /// </remarks>
    [Fact]
    public void Without_the_flag_a_capture_is_attempted_and_its_failure_is_reported()
    {
        using var repo = new FakeRepo();
        var planted = repo.PlantScreenshot("demo/widget.png");
        var before = global::System.IO.File.ReadAllBytes(planted);

        var (exitCode, output) = repo.Compile("--no-build", "--skip-diagrams", "--skip-reference");

        Assert.Contains("═══ Phase 3: Capture ═══", output);
        Assert.DoesNotContain("Phase 3: Capture (skipped", output);

        // Proof the loop body ran: the app was discovered and CaptureAsync was
        // entered far enough to look for a project to launch.
        Assert.Contains("Capturing for demo", output);
        Assert.Contains("No .csproj found", output);

        // Every requested screenshot is accounted for (Requested == Written + Failed).
        Assert.Contains("Captured 0/1 screenshot(s).", output);
        Assert.Contains("1 screenshot(s) failed to capture", output);

        // A capture that produced nothing must not report success — that is how
        // a half-updated corpus reaches `git add -A` unnoticed.
        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("Documentation compiled successfully.", output);

        // And the failure still must not have disturbed the committed asset.
        Assert.Equal(before, global::System.IO.File.ReadAllBytes(planted));
    }

    /// <summary>
    /// The failed-capture exit code must not depend on <c>--ci</c>. A local
    /// <c>mur docs compile</c> that exits 0 after refreshing zero of N
    /// screenshots is exactly the silence issue #989 was reported through.
    /// </summary>
    [Fact]
    public void Failed_capture_fails_the_compile_without_ci()
    {
        using var repo = new FakeRepo();
        repo.PlantScreenshot("demo/widget.png");

        var (withoutCi, _) = repo.Compile("--no-build", "--skip-diagrams", "--skip-reference");
        var (withCi, _) = repo.Compile("--ci", "--no-build", "--skip-diagrams", "--skip-reference");

        Assert.Equal(1, withoutCi);
        Assert.Equal(withCi, withoutCi);
    }

    /// <summary>
    /// Proves the <c>REACTOR_DOC_IMAGE_002</c> gate is actually wired into
    /// <c>docs compile</c> Phase 6, not merely reachable from a unit test.
    /// Deleting the <c>ValidateImageRefs</c> call from <c>CompileCommand</c>
    /// leaves every direct <c>DiagramProcessor</c> test green; it fails this one.
    /// </summary>
    /// <remarks>
    /// <c>--no-screenshots</c> is deliberate: it removes the capture-failure exit
    /// so the exit code here is attributable to the gate alone. The
    /// <c>blank: false</c> theory case is the non-vacuity control — same fixture,
    /// same flags, one different pixel, opposite verdict.
    /// </remarks>
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void Blank_committed_screenshot_fails_the_compile_through_phase_6(bool blank, int expectedExit)
    {
        using var repo = new FakeRepo();
        repo.PlantProcessedScreenshot("demo/widget.png", blank);
        repo.AddTemplate("extra.md.dt", "![Widget](images/demo/widget.png)");

        var (exitCode, output) = repo.Compile(
            "--no-screenshots", "--no-build", "--skip-diagrams", "--skip-reference", "--ci");

        Assert.Equal(blank, output.Contains("REACTOR_DOC_IMAGE_002"));
        Assert.Equal(expectedExit, exitCode);
    }

    /// <summary>
    /// The image-reference gates run on the <em>assembled</em> page, and
    /// <c>DocAssembler</c> prefixes one <c>../</c> per level of topic nesting.
    /// A pattern anchored on a bare <c>images/</c> therefore skipped every nested
    /// page — 10 of them in the real guide — while still reporting a clean run.
    /// </summary>
    /// <remarks>
    /// The screenshot directive is used rather than a literal markdown link
    /// precisely because the <c>../</c> is something the assembler emits: a
    /// literal link would be copied through unchanged and would not exercise the
    /// nesting at all. The flat control pins that the fixture's only variable is
    /// the topic depth.
    /// </remarks>
    [Theory]
    [InlineData("extra.md.dt")]
    [InlineData("recipes/nested.md.dt")]
    public void Blank_screenshot_is_caught_at_every_topic_depth(string templatePath)
    {
        using var repo = new FakeRepo();
        repo.PlantProcessedScreenshot("demo/widget.png", blank: true);
        repo.AddTemplate(templatePath, "![Widget](screenshot://demo/widget)");

        var (exitCode, output) = repo.Compile(
            "--no-screenshots", "--no-build", "--skip-diagrams", "--skip-reference", "--ci");

        Assert.Contains("REACTOR_DOC_IMAGE_002", output);
        Assert.Equal(1, exitCode);
    }

    /// <summary>
    /// <c>ImageProcessor.ContentRegionFor</c> infers "no chrome" from the
    /// <c>-thumb</c> filename suffix, so a full-size screenshot allowed to claim
    /// that name would be scored whole and could hide a blank capture behind its
    /// own border. The suffix is reserved to make that unrepresentable.
    /// </summary>
    /// <remarks>
    /// The dotted row is the one that used to slip through. The reservation
    /// tested the id with the <em>path</em> predicate, which strips from the last
    /// dot, so <c>widget.v2-thumb</c> was read as <c>widget</c> and passed — while
    /// the file it produced, <c>widget.v2-thumb.png</c>, still had its extension
    /// stripped to <c>widget.v2-thumb</c> and so <em>was</em> scored as a thumb.
    /// The two ends disagreed about one screenshot, which is exactly the
    /// collision this rule exists to prevent.
    /// </remarks>
    [Theory]
    [InlineData("widget-thumb", "screenshot", 1)]
    [InlineData("widget.v2-thumb", "screenshot", 1)]
    [InlineData("widget-thumb", "catalog-thumb", 0)]
    [InlineData("widget.v2-thumb", "catalog-thumb", 0)]
    [InlineData("widget", "screenshot", 0)]
    [InlineData("widget.v2", "screenshot", 0)]
    public void Reserved_thumb_suffix_is_rejected_for_non_catalog_screenshots(
        string id, string kind, int expectedExit)
    {
        using var repo = new FakeRepo();
        repo.WriteManifest(
            $"""
            app:
              title: "Demo"
              width: 400
              height: 300
              startup-delay: 0

            screenshots:
              - id: {id}
                description: "Fixture screenshot."
                component: WidgetDemo
                region: client
                format: png
                kind: {kind}

            """);

        var (exitCode, output) = repo.Compile(
            "--no-screenshots", "--no-build", "--skip-diagrams", "--skip-reference");

        Assert.Equal(expectedExit == 1, output.Contains("REACTOR_DOC_SHOT_002"));
        Assert.Equal(expectedExit, exitCode);
    }

    /// <summary>
    /// Phase 3 is the only phase that writes a <em>screenshot</em>, but it is
    /// not the only phase that writes under <c>docs/guide/images/</c>, and
    /// <c>--no-screenshots</c> does not skip the other one. Phase 5.5 (diagrams)
    /// copies <c>docs/_pipeline/diagrams/&lt;topic&gt;/*.svg</c> straight into
    /// that tree.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is here because the surrounding code and docs previously claimed
    /// capture was the only writer under that directory. The claim was wrong,
    /// and its danger is specific: it argues for narrowing the CI
    /// non-destructiveness gate to <c>*.png</c>, since on that reading nothing
    /// else can appear. Pinning the real behaviour keeps the gate's breadth
    /// justified by a test rather than by a comment that can rot silently —
    /// <c>DiagramTests</c> covers the copy, but against an arbitrary temp
    /// directory, so nothing there notices if <c>CompileCommand</c> stops
    /// passing the guide image root.
    /// </para>
    /// <para>
    /// Non-vacuity: the SVG is asserted absent before the compile and present
    /// after, so it fails if the copy stops happening, and it fails if the
    /// destination moves out of <c>docs/guide/images/</c>. The paired
    /// <c>--skip-diagrams</c> assertion below shows the same fixture produces
    /// nothing when the phase is skipped, so "present" is attributable to the
    /// phase rather than to the fixture having planted it there.
    /// </para>
    /// </remarks>
    [Fact]
    public void Diagrams_also_write_under_the_guide_image_tree_and_the_flag_does_not_skip_them()
    {
        const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"8\" height=\"8\"></svg>";

        using var skipped = new FakeRepo();
        skipped.PlantDiagramSource("demo", "overview", Svg);
        var notCopied = skipped.GuideImagePath("demo/overview.svg");
        Assert.False(global::System.IO.File.Exists(notCopied));

        skipped.Compile("--no-screenshots", "--no-build", "--skip-diagrams", "--skip-reference");
        Assert.False(global::System.IO.File.Exists(notCopied));

        using var repo = new FakeRepo();
        repo.PlantDiagramSource("demo", "overview", Svg);
        var copied = repo.GuideImagePath("demo/overview.svg");
        Assert.False(global::System.IO.File.Exists(copied));

        var (exitCode, _) = repo.Compile("--no-screenshots", "--no-build", "--skip-reference");

        Assert.Equal(0, exitCode);
        Assert.True(
            global::System.IO.File.Exists(copied),
            "Phase 5.5 must copy diagram SVGs into docs/guide/images/<topic>/. If this now " +
            "fails because the destination moved, the CI non-destructiveness gate's scope " +
            "comment and docs/contributing/doc-pipeline.md both need updating with it.");
        Assert.Equal(Svg, global::System.IO.File.ReadAllText(copied));
    }

    /// <summary>
    /// Minimal repo the doc compiler will accept: a <c>.git</c> marker for root
    /// discovery, a <c>Directory.Build.props</c> carrying the version token
    /// source, one doc app (with the <c>.cs</c> file discovery requires and a
    /// manifest requesting one screenshot), one template, and a committed
    /// screenshot to guard.
    /// </summary>
    private sealed class FakeRepo : global::System.IDisposable
    {
        private readonly string _root;
        private readonly string _originalCwd;

        public FakeRepo()
        {
            _originalCwd = global::System.IO.Directory.GetCurrentDirectory();
            _root = global::System.IO.Path.Join(
                global::System.IO.Path.GetTempPath(),
                "reactor-doc-compile-" + global::System.Guid.NewGuid().ToString("N"));

            global::System.IO.Directory.CreateDirectory(global::System.IO.Path.Join(_root, ".git"));
            global::System.IO.File.WriteAllText(
                global::System.IO.Path.Join(_root, "Directory.Build.props"),
                "<Project>\n  <PropertyGroup>\n    <ReactorPublicVersion>0.1.0-test</ReactorPublicVersion>\n  </PropertyGroup>\n</Project>\n");

            var appDir = global::System.IO.Path.Join(_root, "docs", "_pipeline", "apps", "demo");
            global::System.IO.Directory.CreateDirectory(appDir);

            // DiscoverApps requires at least one .cs file; without it the app is
            // skipped and Phase 3's loop never executes.
            global::System.IO.File.WriteAllText(
                global::System.IO.Path.Join(appDir, "App.cs"),
                "// Fixture marker for CompileCommand.DiscoverApps.\n");

            // One requested screenshot, and deliberately no .csproj: capture is
            // genuinely attempted and fails at the launch step, which is the
            // furthest a headless test can drive it.
            global::System.IO.File.WriteAllText(
                global::System.IO.Path.Join(appDir, "doc-manifest.yaml"),
                """
                app:
                  title: "Demo"
                  width: 400
                  height: 300
                  startup-delay: 0

                screenshots:
                  - id: widget
                    description: "Fixture screenshot."
                    component: WidgetDemo
                    region: client
                    format: png

                """);

            var templatesDir = global::System.IO.Path.Join(_root, "docs", "_pipeline", "templates");
            global::System.IO.Directory.CreateDirectory(templatesDir);
            global::System.IO.File.WriteAllText(
                global::System.IO.Path.Join(templatesDir, "demo.md.dt"),
                """
                ---
                title: "Demo"
                app: demo
                order: 1
                audience: beginner
                goal: |
                  Fixture template for the capture-skip tests.
                tier: stub
                ---

                # Demo

                Placeholder body.

                """);

            global::System.IO.Directory.CreateDirectory(
                global::System.IO.Path.Join(_root, "docs", "guide", "images"));
        }

        /// <summary>Writes a screenshot with known bytes and returns its path.</summary>
        public string PlantScreenshot(string relative)
        {
            var full = ImagePath(relative);
            global::System.IO.Directory.CreateDirectory(global::System.IO.Path.GetDirectoryName(full)!);

            // Real PNG bytes rather than a sentinel string: a future guard that
            // decodes committed images must not skip this file as undecodable.
            using var bmp = new global::System.Drawing.Bitmap(40, 30,
                global::System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = global::System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(global::System.Drawing.Color.White);
                using var ink = new global::System.Drawing.SolidBrush(global::System.Drawing.Color.Black);
                g.FillRectangle(ink, 8, 8, 12, 12);
            }
            bmp.Save(full, global::System.Drawing.Imaging.ImageFormat.Png);

            // Backdate so a rewrite with identical bytes would still be caught
            // by the timestamp assertion.
            global::System.IO.File.SetLastWriteTimeUtc(full,
                global::System.DateTime.UtcNow.AddHours(-1));
            return full;
        }

        /// <summary>
        /// Writes a processed-screenshot-shaped PNG (border + drop shadow) that
        /// is either blank inside its chrome or carries a mark of real content.
        /// </summary>
        public string PlantProcessedScreenshot(string relative, bool blank)
        {
            var full = ImagePath(relative);
            global::System.IO.Directory.CreateDirectory(global::System.IO.Path.GetDirectoryName(full)!);
            global::System.IO.File.WriteAllBytes(full, TestImages.CapturedStub(120, 90, blank));
            return full;
        }

        /// <summary>
        /// Adds a template at <paramref name="relativePath"/> (which may name a
        /// subdirectory, producing a nested topic id) whose body is
        /// <paramref name="body"/>.
        /// </summary>
        public void AddTemplate(string relativePath, string body)
        {
            var full = global::System.IO.Path.Join(_root, "docs", "_pipeline", "templates",
                relativePath.Replace('/', global::System.IO.Path.DirectorySeparatorChar));
            global::System.IO.Directory.CreateDirectory(global::System.IO.Path.GetDirectoryName(full)!);
            global::System.IO.File.WriteAllText(full,
                $"""
                ---
                title: "Extra"
                app: demo
                order: 2
                audience: beginner
                goal: |
                  Fixture template.
                tier: stub
                ---

                # Extra

                {body}

                """);
        }

        /// <summary>Rewrites the demo manifest verbatim.</summary>
        public void WriteManifest(string yaml) =>
            global::System.IO.File.WriteAllText(
                global::System.IO.Path.Join(_root, "docs", "_pipeline", "apps", "demo", "doc-manifest.yaml"),
                yaml);

        /// <summary>
        /// Writes a diagram source SVG at
        /// <c>docs/_pipeline/diagrams/&lt;topic&gt;/&lt;name&gt;.svg</c> — the
        /// input Phase 5.5 copies into the guide image tree.
        /// </summary>
        public void PlantDiagramSource(string topic, string name, string svg)
        {
            var dir = global::System.IO.Path.Join(_root, "docs", "_pipeline", "diagrams", topic);
            global::System.IO.Directory.CreateDirectory(dir);
            global::System.IO.File.WriteAllText(global::System.IO.Path.Join(dir, name + ".svg"), svg);
        }

        /// <summary>Absolute path of a file under the guide image tree.</summary>
        public string GuideImagePath(string relative) => ImagePath(relative);

        private string ImagePath(string relative) =>
            global::System.IO.Path.Join(_root, "docs", "guide", "images",
                relative.Replace('/', global::System.IO.Path.DirectorySeparatorChar));

        public (int ExitCode, string Output) Compile(params string[] args)
        {
            var stdout = Console.Out;
            var stderr = Console.Error;
            using var buffer = new StringWriter();
            try
            {
                global::System.IO.Directory.SetCurrentDirectory(_root);
                Console.SetOut(buffer);
                Console.SetError(buffer);
                var exit = CompileCommand.Run(args);
                return (exit, buffer.ToString());
            }
            finally
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                global::System.IO.Directory.SetCurrentDirectory(_originalCwd);
            }
        }

        public void Dispose() => FixtureCleanup.DeleteTree(_root);
    }
}
