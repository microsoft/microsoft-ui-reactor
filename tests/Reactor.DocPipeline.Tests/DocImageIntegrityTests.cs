using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Issue #989: <c>docs compile</c> once overwrote 103 committed screenshots with
/// ~3.5&#160;KB solid-white stubs produced by a capture whose doc-app window never
/// painted, and still exited 0. The only reason it was caught was a human
/// noticing the changed-file count in a PR.
/// </summary>
/// <remarks>
/// These tests cover the <c>REACTOR_DOC_IMAGE_002</c> gate that now runs in
/// Phase 6 of every compile. The gate is deliberately a <em>contentless</em>
/// predicate rather than a file-size floor: the committed corpus contains
/// legitimately tiny screenshots (<c>async-loading.png</c> is 89×40 / 2127&#160;B),
/// so any size threshold able to catch a stub would also condemn real assets.
/// </remarks>
public class DocImageIntegrityTests
{
    private readonly ITestOutputHelper _output;

    public DocImageIntegrityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Gate_flags_a_blank_screenshot()
    {
        using var tree = new TempGuideTree();
        tree.WriteImage("hooks/usestate.png", MakeCapturedStub(499, 196, blank: true));

        var findings = DiagramProcessor.ValidateImageRefs(
            "hooks.md.dt", "![UseState demo](images/hooks/usestate.png)", tree.ImagesDir, tree.GuideDir);

        var f = Assert.Single(findings);
        Assert.Equal("REACTOR_DOC_IMAGE_002", f.Code);
        Assert.Equal(TierLintSeverity.Error, f.Severity);
    }

    /// <summary>
    /// Positive control. The stub in <see cref="Gate_flags_a_blank_screenshot"/>
    /// carries the same border and drop shadow every processed screenshot does,
    /// so a gate that scored its own chrome as content would report zero
    /// findings on both inputs and this pair is what proves it does not. Only
    /// the painted interior differs between the two images.
    /// </summary>
    [Fact]
    public void Gate_accepts_a_painted_screenshot()
    {
        using var tree = new TempGuideTree();
        tree.WriteImage("hooks/usestate.png", MakeCapturedStub(499, 196, blank: false));

        var findings = DiagramProcessor.ValidateImageRefs(
            "hooks.md.dt", "![UseState demo](images/hooks/usestate.png)", tree.ImagesDir, tree.GuideDir);

        Assert.Empty(findings);
    }

    /// <summary>
    /// A platform with no image decoder reports that fact <em>once</em> and goes
    /// on checking everything that never needed a decoder, rather than reporting
    /// it per image or abandoning the page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>System.Drawing.Common</c> is Windows-only, so Phase 6 gained a decoder
    /// dependency when this gate was added and would have died on an unhandled
    /// exception at the first referenced PNG off-Windows.
    /// </para>
    /// <para>
    /// The platform branch itself cannot be exercised on Windows and this test
    /// does not claim to — it drives the verdict in through the injectable scan
    /// cache, the same seam the real per-run cache uses. What it pins is
    /// everything downstream: the once-only report, the <c>Warning</c> severity,
    /// and — the load-bearing part — that the suppression covers only the
    /// duplicate warning and not the scan itself.
    /// </para>
    /// <para>
    /// That is why the fixture puts a zero-byte <c>.png</c> and a missing file
    /// <em>after</em> the three undecodable ones. Both defects are found without
    /// a decoder: <c>_001</c> before the scan, <c>_003</c> by the pre-decode
    /// guards inside <c>ComputeRasterVerdict</c>. An implementation that skipped
    /// the rest of the loop once the decoder went missing — the obvious way to
    /// write "report it once" — still passes the count and severity assertions
    /// and fails on <c>_003</c>, which is the only assertion here that could not
    /// have passed before this behaviour existed.
    /// </para>
    /// <para>
    /// A cache key that failed to match would leave the real scan running, which
    /// returns <c>Ok</c> for these painted images and emits no <c>_004</c> — so
    /// a mis-keyed fixture fails this test rather than passing it quietly.
    /// </para>
    /// </remarks>
    [Fact]
    public void Missing_decoder_is_reported_once_and_does_not_stop_decoder_free_checks()
    {
        using var tree = new TempGuideTree();
        tree.WriteImage("hooks/a.png", MakeCapturedStub(499, 196, blank: false));
        tree.WriteImage("hooks/b.png", MakeCapturedStub(499, 196, blank: false));
        tree.WriteImage("hooks/c.png", MakeCapturedStub(499, 196, blank: false));
        tree.WriteImage("hooks/empty.png", global::System.Array.Empty<byte>());

        var pageFull = global::System.IO.Path.GetFullPath(tree.GuideDir);
        string Key(string rel) => global::System.IO.Path.GetFullPath(
            global::System.IO.Path.Join(
                pageFull, rel.Replace('/', global::System.IO.Path.DirectorySeparatorChar)));

        // empty.png is deliberately absent: it must reach the real scan, whose
        // pre-decode guards run on every platform.
        var cache = new Dictionary<string, DiagramProcessor.RasterVerdict>
        {
            [Key("images/hooks/a.png")] = DiagramProcessor.RasterVerdict.Unavailable,
            [Key("images/hooks/b.png")] = DiagramProcessor.RasterVerdict.Unavailable,
            [Key("images/hooks/c.png")] = DiagramProcessor.RasterVerdict.Unavailable,
        };

        var body =
            "![a](images/hooks/a.png)\n" +
            "![b](images/hooks/b.png)\n" +
            "![c](images/hooks/c.png)\n" +
            "![empty](images/hooks/empty.png)\n" +
            "![gone](images/hooks/missing.png)\n";

        var findings = DiagramProcessor.ValidateImageRefs(
            "hooks.md.dt", body, tree.ImagesDir, tree.GuideDir, cache);

        var unavailable = Assert.Single(findings, f => f.Code == "REACTOR_DOC_IMAGE_004");
        Assert.Equal(TierLintSeverity.Warning, unavailable.Severity);
        Assert.Equal(1, unavailable.Line);

        // Decoder-free, and reported after the decoder went missing.
        var zeroByte = Assert.Single(findings, f => f.Code == "REACTOR_DOC_IMAGE_003");
        Assert.Equal(4, zeroByte.Line);

        var broken = Assert.Single(findings, f => f.Code == "REACTOR_DOC_IMAGE_001");
        Assert.Equal(5, broken.Line);

        // No image may be scored *blank* while the decoder is missing: a verdict
        // about pixel content is precisely what this run cannot produce.
        Assert.DoesNotContain(findings, f => f.Code == "REACTOR_DOC_IMAGE_002");
        Assert.Equal(3, findings.Count);
    }

    /// <summary>
    /// A decoder-unavailable warning must not fail the compile, while the errors
    /// raised alongside it must.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>REACTOR_DOC_IMAGE_004</c> means the blank-image scan <em>could not
    /// run</em>, and it is raised precisely on a platform with no image decoder.
    /// Treating it as fatal fails a docs build over a missing codec on a tree
    /// where nothing is wrong with the docs — a verdict about the checker
    /// reported as a verdict about the content.
    /// </para>
    /// <para>
    /// The image-ref loop in <c>CompileCommand</c> set <c>hasErrors</c> for every
    /// finding it received, so the <see cref="TierLintSeverity.Warning"/> this
    /// gate declares was ignored by the only code that read it. Both halves are
    /// asserted from one finding set, so neither an always-fatal nor a
    /// never-fatal predicate survives: the first fails the warning assertion,
    /// the second fails the error assertion. Asserting only the warning would
    /// pass against a predicate that never breaks the build — which would
    /// disable the gate this PR exists to add.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_decoder_unavailable_warning_does_not_fail_the_compile()
    {
        using var tree = new TempGuideTree();
        tree.WriteImage("hooks/a.png", MakeCapturedStub(499, 196, blank: false));
        tree.WriteImage("hooks/empty.png", global::System.Array.Empty<byte>());

        var cache = new Dictionary<string, DiagramProcessor.RasterVerdict>
        {
            [global::System.IO.Path.GetFullPath(global::System.IO.Path.Join(
                global::System.IO.Path.GetFullPath(tree.GuideDir),
                global::System.IO.Path.Join("images", "hooks", "a.png")))]
                = DiagramProcessor.RasterVerdict.Unavailable,
        };

        var findings = DiagramProcessor.ValidateImageRefs(
            "hooks.md.dt",
            "![a](images/hooks/a.png)\n" +
            "![empty](images/hooks/empty.png)\n" +
            "![gone](images/hooks/missing.png)\n",
            tree.ImagesDir, tree.GuideDir, cache);

        // Premise: this fixture must actually produce both kinds. Without it a
        // filter that returned nothing would satisfy both assertions below.
        var warning = Assert.Single(findings, f => f.Code == "REACTOR_DOC_IMAGE_004");
        var errors = findings.Where(f => f.Code != "REACTOR_DOC_IMAGE_004").ToList();
        Assert.Equal(TierLintSeverity.Warning, warning.Severity);
        Assert.Equal(2, errors.Count);

        Assert.False(CompileCommand.IsBuildBreaking(warning));
        Assert.All(errors, f => Assert.True(CompileCommand.IsBuildBreaking(f)));
    }

    /// <summary>
    /// An image reference carrying a <c>:</c> is a broken reference, not a file
    /// to read. On Windows it names an alternate data stream, so the gate would
    /// otherwise score bytes that no reader of the docs can ever see.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fixture is built so it can only pass for the right reason. The
    /// committed <c>a.png</c> is <em>blank</em> — the exact thing this gate
    /// exists to catch — while its alternate stream <c>hidden</c> holds a
    /// <em>painted</em> PNG. Without the rejection, containment passes,
    /// <c>File.Exists</c> succeeds, <c>HasRasterMagic</c> sees the stream's
    /// valid PNG signature and the decoder scores the stream's content: the gate
    /// returns clean on a page whose rendered image is blank. That is a
    /// fail-open in the one gate this PR exists to make fail-closed, which is
    /// why the assertion is on the finding rather than on an exception.
    /// </para>
    /// <para>
    /// A test that merely referenced a non-existent stream would be vacuous:
    /// <c>File.Exists</c> is false either way, so <c>_001</c> is reported with
    /// or without the rejection. The stream has to be real, and it has to
    /// contain something the gate would have accepted.
    /// </para>
    /// <para>
    /// The control below is load-bearing for the same reason. If the filesystem
    /// did not create the alternate stream, the whole fixture degenerates into
    /// the vacuous version above — so it is asserted directly, before anything
    /// depends on it, rather than trusted.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_alternate_data_stream_reference_is_broken_not_scanned()
    {
        if (!global::System.OperatingSystem.IsWindows()) return;

        using var tree = new TempGuideTree();
        tree.WriteImage("hooks/a.png", MakeCapturedStub(499, 196, blank: true));

        var mainPath = global::System.IO.Path.Join(tree.ImagesDir, "hooks", "a.png");
        var adsPath = mainPath + ":hidden";
        var painted = MakeCapturedStub(499, 196, blank: false);
        global::System.IO.File.WriteAllBytes(adsPath, painted);

        // Control: the alternate stream must actually exist and carry the
        // painted bytes, or this test proves nothing about the rejection.
        Assert.True(global::System.IO.File.Exists(adsPath),
            "fixture did not create an alternate data stream — the rest of this test would be vacuous");
        Assert.Equal(painted, global::System.IO.File.ReadAllBytes(adsPath));
        Assert.NotEqual(painted, global::System.IO.File.ReadAllBytes(mainPath));

        var findings = DiagramProcessor.ValidateImageRefs(
            "hooks.md.dt", "![a](images/hooks/a.png:hidden)", tree.ImagesDir, tree.GuideDir);

        var broken = Assert.Single(findings);
        Assert.Equal("REACTOR_DOC_IMAGE_001", broken.Code);
        Assert.Equal(TierLintSeverity.Error, broken.Severity);

        // Not scored: a verdict about the stream's pixels is exactly what must
        // not be produced, and _002 is what the un-rejected path would emit if
        // the stream happened to be blank too.
        Assert.DoesNotContain(findings, f => f.Code is "REACTOR_DOC_IMAGE_002" or "REACTOR_DOC_IMAGE_003");
    }

    /// <summary>
    /// The read side and the write side must refuse the same references. Both
    /// ask "does this segment carry a drive or stream separator", and the read
    /// side used to answer it with a comment instead of a predicate.
    /// </summary>
    /// <remarks>
    /// This is the test that fails when the two diverge, which is the property
    /// worth holding — not that either is correct today. It feeds both call
    /// paths the same adversarial inputs and asserts they agree, so a future fix
    /// to one that is not applied to the other is caught here rather than
    /// discovered as a bypass in whichever caller kept the stale rule.
    /// </remarks>
    /// <remarks>
    /// Measured limits, so a green is not over-read. Disabling the refusal
    /// inside <c>ResolveContained</c> fails exactly the five colon-bearing rows
    /// and correctly leaves the three ordinary ones passing — the discriminating
    /// outcome; all eight failing would have been a bug report about this test.
    /// But mutating the <em>shared</em> predicate moves both sides at once and
    /// they agree by both being wrong, so this theory scores zero against that.
    /// Re-divergence is what it convicts, and re-divergence is how the defect
    /// arrived: the read side held the rule in a comment while only the write
    /// side implemented it.
    /// </remarks>
    [Theory]
    [InlineData("a.png:hidden")]
    [InlineData("C:/x/a.png")]
    [InlineData("a:b:c.png")]
    [InlineData(":leading.png")]
    [InlineData("trailing.png:")]
    [InlineData("plain.png")]
    [InlineData("sub/dir/plain.png")]
    [InlineData("../escape.png")]
    public void Both_containment_paths_agree_about_stream_separators(string segment)
    {
        if (!global::System.OperatingSystem.IsWindows()) return;

        var writeSideRefuses = false;
        try
        {
            DocPaths.ResolveContained(global::System.IO.Path.GetTempPath(), segment, "segment");
        }
        catch (global::System.InvalidOperationException ex)
        {
            writeSideRefuses = ex.Message.Contains("':'", global::System.StringComparison.Ordinal);
        }

        Assert.Equal(writeSideRefuses, DocPaths.HasStreamOrDriveSeparator(segment));
    }

    /// <summary>
    /// Catalog thumbnails are written by <c>ProcessThumb</c>, which draws no
    /// border and no drop shadow, so the chrome inset must not be applied to
    /// them. A thumbnail whose only content sits in the strip the inset would
    /// trim is a real, non-blank asset; scoring it with the full-size inset
    /// would condemn it and tell an author to restore a file that was never
    /// broken.
    /// </summary>
    [Fact]
    public void Gate_does_not_flag_a_thumbnail_with_edge_content()
    {
        using var tree = new TempGuideTree();
        tree.WriteImage("controls/button-thumb.png", MakeEdgeContentThumb(320, 240));

        var findings = DiagramProcessor.ValidateImageRefs(
            "controls.md.dt", "![Button](images/controls/button-thumb.png)", tree.ImagesDir, tree.GuideDir);

        Assert.Empty(findings);
    }

    /// <summary>
    /// Non-vacuity pair for <see cref="Gate_does_not_flag_a_thumbnail_with_edge_content"/>.
    /// Byte-for-byte the same image under a name without the <c>-thumb</c>
    /// suffix <em>is</em> flagged, because a full-size capture with nothing but
    /// chrome-strip pixels really is a blank frame. Only the filename differs,
    /// so this proves the suffix branch is what carries the behaviour rather
    /// than the gate simply passing everything.
    /// </summary>
    [Fact]
    public void Gate_flags_the_same_image_when_it_is_not_a_thumbnail()
    {
        using var tree = new TempGuideTree();
        tree.WriteImage("controls/button.png", MakeEdgeContentThumb(320, 240));

        var findings = DiagramProcessor.ValidateImageRefs(
            "controls.md.dt", "![Button](images/controls/button.png)", tree.ImagesDir, tree.GuideDir);

        Assert.Equal("REACTOR_DOC_IMAGE_002", Assert.Single(findings).Code);
    }

    /// <summary>
    /// A transparent PNG composites to white wherever it is drawn, so it is as
    /// blank as a solid-white one — and it is the exact shape a never-rendered
    /// composition surface produces.
    /// </summary>
    [Fact]
    public void Gate_flags_a_fully_transparent_image()
    {
        using var tree = new TempGuideTree();
        tree.WriteImage("hooks/usestate.png", MakeSolidPng(499, 196, Color.FromArgb(0, 0, 0, 0)));

        var findings = DiagramProcessor.ValidateImageRefs(
            "hooks.md.dt", "![UseState demo](images/hooks/usestate.png)", tree.ImagesDir, tree.GuideDir);

        Assert.Equal("REACTOR_DOC_IMAGE_002", Assert.Single(findings).Code);
    }

    [Fact]
    public void Gate_ignores_vector_references()
    {        // SVG diagrams are authored, not captured, and System.Drawing cannot
        // decode them — reporting them as blank would be a false alarm on
        // every compile.
        using var tree = new TempGuideTree();
        tree.WriteText("architecture/overview.svg",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"></svg>");

        var findings = DiagramProcessor.ValidateImageRefs(
            "arch.md.dt", "![Overview](images/architecture/overview.svg)", tree.ImagesDir, tree.GuideDir);

        Assert.Empty(findings);
    }

    /// <summary>
    /// A raster file that exists, carries valid magic and sits inside the size
    /// caps but cannot be decoded must be reported, not silently accepted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the gate's own fail-open. The blankness scan is wrapped in a
    /// catch that returns "not blank" on any decode fault, so before this
    /// change an undecodable image produced <em>zero</em> findings — the
    /// compile printed nothing and exited 0, which is the same silent-success
    /// shape as issue #989 itself, one layer up.
    /// </para>
    /// <para>
    /// It is reachable rather than theoretical: any corruption that survives
    /// the magic check but defeats the decoder lands here. The fixture keeps a
    /// real PNG signature and replaces the body, because a file with no
    /// signature at all is turned away by <c>HasRasterMagic</c> before the
    /// decode step and would not exercise the catch. Note the sibling test
    /// below: a *truncated* PNG does not reach this path — GDI+ decodes it —
    /// so this branch and the blank branch each own a distinct real shape.
    /// </para>
    /// </remarks>
    [Fact]
    public void Gate_reports_an_undecodable_image_instead_of_passing_it()
    {
        using var tree = new TempGuideTree();
        var valid = MakeCapturedStub(200, 150, blank: false);

        // Keep the 8-byte PNG signature so HasRasterMagic still admits the file
        // to the decode step, but replace the body so the decoder cannot read it.
        var corrupt = new byte[valid.Length];
        global::System.Array.Copy(valid, corrupt, 8);
        for (var i = 8; i < corrupt.Length; i++) corrupt[i] = (byte)(i * 31 % 251);
        tree.WriteImage("controls/half-written.png", corrupt);

        var findings = DiagramProcessor.ValidateImageRefs(
            "controls.md.dt",
            "![Half written](images/controls/half-written.png)",
            tree.ImagesDir,
            tree.GuideDir);

        // Guard the premise: if the fixture had not written the file, or the
        // magic check had turned it away, this test would be asserting
        // something other than what it claims.
        Assert.True(
            global::System.IO.File.Exists(
                global::System.IO.Path.Join(tree.ImagesDir, "controls", "half-written.png")),
            "fixture did not write the file — every assertion below would be about a missing file");

        var finding = Assert.Single(findings);
        Assert.Equal("REACTOR_DOC_IMAGE_003", finding.Code);
        Assert.Contains("decode", finding.Message, global::System.StringComparison.OrdinalIgnoreCase);

        // The other arm of the same code. This file has a valid PNG signature
        // and faulted inside the decoder, so it must not be reported as "not an
        // image" — that message would tell the reader to run `git lfs pull` on a
        // file that is a real, corrupt PNG.
        Assert.DoesNotContain("not an image", finding.Message, global::System.StringComparison.Ordinal);
    }

    /// <summary>
    /// A <c>.png</c> reference whose bytes are not an image at all must be
    /// convicted, not skipped. The pre-decode guards exist so a hostile or
    /// oversized file never reaches GDI+, but "these bytes are not PNG or
    /// JPEG" is a finding about the file's content, not a decision the gate
    /// made to leave it alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The extension filter above this guard means the file is already named
    /// <c>.png</c>/<c>.jpg</c>, so failing the magic check is unambiguous: it
    /// will not render. Reporting <c>Ok</c> let the gate return a clean run on
    /// a page with a broken image — the precise outcome it exists to prevent.
    /// </para>
    /// <para>
    /// The remark on <c>Gate_reports_an_undecodable_image_instead_of_passing_it</c>
    /// above stated this behaviour as a fact — "a file with no signature at all
    /// is turned away by HasRasterMagic before the decode step" — to explain why
    /// its fixture keeps a real signature. It was accurate, and it went four
    /// commits without anyone asking whether "turned away" was the right
    /// verdict. Describing a branch is not the same as agreeing with it.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("html", "<html><body>404 Not Found</body></html>")]
    [InlineData("svg-mislabelled", "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect/></svg>")]
    [InlineData("lfs-pointer", "version https://git-lfs.github.com/spec/v1\noid sha256:ab\nsize 12\n")]
    public void Gate_reports_a_png_reference_that_is_not_an_image(string name, string content)
    {
        using var tree = new TempGuideTree();
        tree.WriteBytes($"controls/{name}.png", global::System.Text.Encoding.UTF8.GetBytes(content));

        var findings = DiagramProcessor.ValidateImageRefs(
            "controls.md.dt",
            $"![Not an image](images/controls/{name}.png)",
            tree.ImagesDir,
            tree.GuideDir);

        // Premise guard: a missing file reports IMAGE_001, which would satisfy
        // "something was reported" while testing a different rule entirely.
        Assert.True(
            global::System.IO.File.Exists(
                global::System.IO.Path.Join(tree.ImagesDir, "controls", $"{name}.png")),
            "fixture did not write the file — the finding below would be IMAGE_001 about a missing file");

        Assert.Equal("REACTOR_DOC_IMAGE_003", Assert.Single(findings).Code);

        // The code is shared with the read/decode-fault path, so the code alone
        // cannot show the two are told apart. The message is where the split is
        // observable, and the wrong one sends a reader to check file locks and
        // permissions on a file whose problem is that it is a line of text.
        var message = findings[0].Message;
        Assert.Contains("not an image", message, global::System.StringComparison.Ordinal);
        Assert.DoesNotContain("locked", message, global::System.StringComparison.Ordinal);
    }

    /// <summary>
    /// The other half of the split above: an over-cap file must be *skipped*,
    /// and skipped ahead of the content checks. The cap is a decision this gate
    /// makes about how much work it will do, so converting it into a verdict
    /// would report a file as broken on the strength of its size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fixture is deliberately an over-cap file that is <em>also</em> not a
    /// raster, and that is the only shape that can fail. A genuine 64 MiB PNG
    /// scores <c>Ok</c> whether or not the cap is honoured — deleting the cap
    /// entirely would leave such a test green — so it would assert nothing. Here
    /// the two guards disagree about the same file, and only the ordering the
    /// production code actually has produces zero findings.
    /// </para>
    /// <para>
    /// Written because a mutation run found this side unpinned: turning both
    /// skips into <c>Undecodable</c> killed no test, while the comment above
    /// them argued at length for why they are skips. The claim was careful, and
    /// carefully wrong things are the ones that survive review.
    /// </para>
    /// </remarks>
    [Fact]
    public void Gate_skips_an_over_cap_file_before_judging_its_content()
    {
        using var tree = new TempGuideTree();
        var path = global::System.IO.Path.Join(tree.ImagesDir, "controls", "huge.png");
        global::System.IO.Directory.CreateDirectory(global::System.IO.Path.GetDirectoryName(path)!);

        // SetLength gives an over-cap file without writing 64 MiB; the content is
        // zeros, so it carries no PNG or JPEG signature.
        using (var fs = new global::System.IO.FileStream(path, global::System.IO.FileMode.Create))
        {
            fs.SetLength((long)ImageProcessor.MaxImageBytes + 1);
        }

        // Premise: the file must actually be over the cap and actually lack a
        // signature, or "zero findings" below is about neither guard.
        var len = new global::System.IO.FileInfo(path).Length;
        Assert.True(len > ImageProcessor.MaxImageBytes, $"fixture is only {len} bytes — not over the cap");

        var findings = DiagramProcessor.ValidateImageRefs(
            "controls.md.dt",
            "![Huge](images/controls/huge.png)",
            tree.ImagesDir,
            tree.GuideDir);

        Assert.Empty(findings.Select(f => $"{f.Code} {f.Message}"));
    }

    /// <summary>
    /// A zero-byte <c>.png</c> is the same class as the above and the most
    /// likely one to occur by accident — an interrupted or truncated write
    /// leaves exactly this. It was skipped by the length guard, which is there
    /// to keep an empty read away from the magic check, not to bless the file.
    /// </summary>
    [Fact]
    public void Gate_reports_an_empty_png_reference()
    {
        using var tree = new TempGuideTree();
        tree.WriteBytes("controls/empty.png", []);

        var findings = DiagramProcessor.ValidateImageRefs(
            "controls.md.dt",
            "![Empty](images/controls/empty.png)",
            tree.ImagesDir,
            tree.GuideDir);

        Assert.Equal("REACTOR_DOC_IMAGE_003", Assert.Single(findings).Code);
        Assert.Contains("not an image", findings[0].Message, global::System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Non-vacuity pair: the intact original of the very same fixture is
    /// accepted. Only the body bytes differ, so the two together show the new
    /// code fires on undecodability rather than on the fixture in general.
    /// </summary>
    [Fact]
    public void Gate_accepts_the_intact_original_of_that_image()
    {
        using var tree = new TempGuideTree();
        tree.WriteImage("controls/fully-written.png", MakeCapturedStub(200, 150, blank: false));

        var findings = DiagramProcessor.ValidateImageRefs(
            "controls.md.dt",
            "![Fully written](images/controls/fully-written.png)",
            tree.ImagesDir,
            tree.GuideDir);

        Assert.Empty(findings);
    }

    /// <summary>
    /// A PNG cut off mid-write — what an interrupted capture leaves behind — is
    /// reported as <c>REACTOR_DOC_IMAGE_002</c>, not as a decode failure.
    /// </summary>
    /// <remarks>
    /// This is a measurement, not an aspiration, and it is the reason the
    /// undecodable branch above uses a corrupted body rather than a truncation:
    /// GDI+ decodes a truncated PNG rather than throwing, yielding the
    /// unwritten scanlines as blank, so the realistic interrupted-write shape
    /// lands on the blank gate and never reaches the catch. Pinning it here
    /// means that if a future decoder change starts throwing instead, this test
    /// moves rather than silently swapping which code fires.
    /// </remarks>
    [Fact]
    public void Gate_reports_a_truncated_capture_as_blank_not_as_corrupt()
    {
        using var tree = new TempGuideTree();
        var valid = MakeCapturedStub(200, 150, blank: false);
        var truncated = new byte[valid.Length / 3];
        global::System.Array.Copy(valid, truncated, truncated.Length);
        tree.WriteImage("controls/interrupted.png", truncated);

        var findings = DiagramProcessor.ValidateImageRefs(
            "controls.md.dt",
            "![Interrupted](images/controls/interrupted.png)",
            tree.ImagesDir,
            tree.GuideDir);

        Assert.Equal("REACTOR_DOC_IMAGE_002", Assert.Single(findings).Code);
    }

    /// <summary>
    /// A file too short to hold a signature is reported, not skipped and not
    /// crashed on. Four bytes named <c>.png</c> is a broken image by any
    /// reading, and it belongs with the empty and wrong-magic cases above.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This guards the regression risk introduced by reading the header with
    /// <c>ReadExactly</c> instead of <c>Read</c>: <c>ReadExactly</c> throws
    /// <c>EndOfStreamException</c> on a file shorter than the buffer, a throw
    /// site the old code did not have. The test proves that throw is absorbed
    /// rather than escaping <c>ValidateImageRefs</c> and taking a whole compile
    /// down on one stray short file.
    /// </para>
    /// <para>
    /// It asserted the opposite until the gate stopped treating "carries no
    /// raster signature" as a skip. That earlier expectation was this file's own
    /// contribution to the fail-open: the assertion was real, it ran, and it
    /// held the wrong behaviour in place — a 4-byte stub sailing through is the
    /// same defect as an HTML 404 page named <c>.png</c>, and the test named it
    /// "skips" without anyone asking whether skipping was right.
    /// </para>
    /// <para>
    /// Third revision of what the dedicated <c>EndOfStreamException</c> catch in
    /// <c>HasRasterMagic</c> proves, so the honest answer is that it currently
    /// proves nothing. It was claimed load-bearing when written and measured not
    /// to be (a blanket <c>IOException</c> handler covered it); removing that
    /// blanket handler made it load-bearing; and now both routes converge on
    /// <c>Undecodable</c> — the inner catch returns false, which is a verdict,
    /// and the outer catch reports the same verdict for the escaping
    /// <c>EndOfStreamException</c>. No test can separate them. It is kept
    /// because a predicate asked "does this file carry raster magic?" should
    /// answer "no" for a 4-byte file rather than throw, which is a claim about
    /// the shape of the API and not one this test measures.
    /// </para>
    /// <para>
    /// Nor does it prove the fail-open the <c>ReadExactly</c> change actually
    /// fixes — a short read returning fewer bytes without being at EOF, which
    /// silently skipped blank-frame validation for a valid PNG.
    /// <c>HasRasterMagic</c> takes a path and opens its own
    /// <c>FileStream</c>, so there is no seam to inject a stream that
    /// under-reads, and a local <c>FileStream</c> will not do it on demand.
    /// That change is kept because the contract of <c>Stream.Read</c> permits
    /// the short read, not because anything here demonstrates one.
    /// </para>
    /// </remarks>
    [Fact]
    public void Gate_reports_a_file_too_short_to_carry_a_signature()
    {
        using var tree = new TempGuideTree();
        tree.WriteImage("controls/stub.png", [0x89, 0x50, 0x4E, 0x47]);

        var findings = DiagramProcessor.ValidateImageRefs(
            "controls.md.dt",
            "![Stub](images/controls/stub.png)",
            tree.ImagesDir,
            tree.GuideDir);

        // Premise guard: a missing file would report IMAGE_001, which is a
        // different rule reaching the same "something was found" shape.
        var path = global::System.IO.Path.Join(tree.ImagesDir, "controls", "stub.png");
        Assert.True(
            global::System.IO.File.Exists(path),
            "fixture did not write the file — the assertion below would be about a missing file");
        Assert.Equal(4, new global::System.IO.FileInfo(path).Length);

        Assert.Equal("REACTOR_DOC_IMAGE_003", Assert.Single(findings).Code);
    }

    [Fact]
    public void Gate_does_not_report_an_undecodable_file_as_blank()
    {
        // A truncated or corrupt PNG is a different problem with a different
        // fix. Misfiling it as "blank screenshot — restore it from git" would
        // send an author chasing the wrong thing.
        using var tree = new TempGuideTree();
        tree.WriteBytes("hooks/broken.png", [0x89, 0x50, 0x4E, 0x47, 0x00, 0x00]);

        var findings = DiagramProcessor.ValidateImageRefs(
            "hooks.md.dt", "![Broken](images/hooks/broken.png)", tree.ImagesDir, tree.GuideDir);

        Assert.DoesNotContain(findings, f => f.Code == "REACTOR_DOC_IMAGE_002");
    }

    /// <summary>
    /// A raster the gate cannot open — locked by another process — is reported
    /// as <c>REACTOR_DOC_IMAGE_003</c>, not skipped as "not a raster".
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the gate's own fail-open, one level below the one
    /// <c>Gate_reports_an_undecodable_image_instead_of_passing_it</c> closes.
    /// <c>ComputeRasterVerdict</c>'s catch deliberately admits
    /// <c>IOException</c> and <c>UnauthorizedAccessException</c> and reports
    /// them, on the stated reasoning that the verdict spans "corrupt" and
    /// "couldn't read right now". But the magic-bytes pre-check runs
    /// <em>first</em> and used to swallow those same two exceptions and return
    /// <c>false</c>, which the caller reads as "not a raster" and skips. So a
    /// locked file never reached the catch that was written for it: the
    /// documented behaviour and the actual control flow disagreed, and the
    /// direction of the disagreement was silent success.
    /// </para>
    /// <para>
    /// That is the exact shape this pipeline exists to stop — a gate that skips
    /// analysis is a gate that passes — and it is invisible to every other test
    /// here because they all hand the gate a readable file.
    /// </para>
    /// </remarks>
    [Fact]
    public void Gate_reports_a_locked_image_instead_of_skipping_it()
    {
        using var tree = new TempGuideTree();
        tree.WriteImage("controls/locked.png", MakeCapturedStub(200, 150, blank: false));
        var path = global::System.IO.Path.Join(tree.ImagesDir, "controls", "locked.png");

        // Hold an exclusive handle for the duration of the scan, which is what
        // another process mid-write looks like to the gate.
        using (var hold = new global::System.IO.FileStream(
                   path,
                   global::System.IO.FileMode.Open,
                   global::System.IO.FileAccess.Read,
                   global::System.IO.FileShare.None))
        {
            // Premise guard: if this platform let a second reader in, the test
            // would be scanning a perfectly readable file and asserting nothing.
            var lockHolds = false;
            try
            {
                using var probe = global::System.IO.File.OpenRead(path);
            }
            catch (global::System.IO.IOException)
            {
                lockHolds = true;
            }

            Assert.True(lockHolds, "the exclusive handle did not block a second reader — this test cannot measure what it claims");

            var findings = DiagramProcessor.ValidateImageRefs(
                "controls.md.dt",
                "![Locked](images/controls/locked.png)",
                tree.ImagesDir,
                tree.GuideDir);

            Assert.Equal("REACTOR_DOC_IMAGE_003", Assert.Single(findings).Code);
        }

        // Non-vacuity: the same file, same fixture, once the handle is gone.
        // Only the lock differs, so the finding above turns on the lock and not
        // on anything about the image.
        Assert.Empty(DiagramProcessor.ValidateImageRefs(
            "controls.md.dt",
            "![Locked](images/controls/locked.png)",
            tree.ImagesDir,
            tree.GuideDir));
    }

    /// <summary>
    /// A reference's <c>../</c> run is page-relative escaping emitted by
    /// DocAssembler for the page's depth. Resolving it the way a renderer does
    /// — against the page's own directory — is what makes a wrong-depth prefix
    /// detectable: normalising the run away instead would land every variant
    /// below on the same existing file and report nothing, while the rendered
    /// page 404s. Each row differs from the passing one only in the prefix, so
    /// the assertion turns on the traversal and nothing else.
    /// </summary>
    [Theory]
    // page depth 0 (docs/guide/hooks.md) — no escaping needed
    [InlineData("", "images/x/shot.png", true)]
    [InlineData("", "../images/x/shot.png", false)]          // one ../ too many
    [InlineData("", "../../images/x/shot.png", false)]       // two too many
    // page depth 1 (docs/guide/recipes/login.md) — exactly one ../
    [InlineData("recipes", "../images/x/shot.png", true)]
    [InlineData("recipes", "images/x/shot.png", false)]      // missing the ../
    [InlineData("recipes", "../../images/x/shot.png", false)] // one too many
    // page depth 2 (docs/guide/recipes/auth/oauth.md) — exactly two
    [InlineData("recipes/auth", "../../images/x/shot.png", true)]
    [InlineData("recipes/auth", "../images/x/shot.png", false)]
    public void Image_ref_must_carry_the_right_traversal_for_its_page_depth(
        string pageSubdir, string reference, bool shouldResolve)
    {
        using var tree = new TempGuideTree();
        tree.WriteImage("x/shot.png", MakeCapturedStub(499, 196, blank: false));

        var pageDir = pageSubdir.Length == 0
            ? tree.GuideDir
            : global::System.IO.Path.Join(
                tree.GuideDir, pageSubdir.Replace('/', global::System.IO.Path.DirectorySeparatorChar));

        var findings = DiagramProcessor.ValidateImageRefs(
            "topic.md.dt", $"![shot]({reference})", tree.ImagesDir, pageDir);

        if (shouldResolve)
        {
            Assert.Empty(findings);
        }
        else
        {
            Assert.Equal("REACTOR_DOC_IMAGE_001", Assert.Single(findings).Code);
        }
    }

    /// <summary>
    /// The reference text is page content, not a path the pipeline authored:
    /// <c>ImagePattern</c>'s <c>[^)]+</c> tail admits anything but a closing
    /// paren, so a reference can be text that no path API will resolve. Measured
    /// on .NET 10/Windows, only a NUL actually reaches that state — every other
    /// hostile character resolves and then fails <c>File.Exists</c>, which is
    /// already the right answer. A NUL is not contrived: a doc file saved as
    /// UTF-16 and read as UTF-8 is NUL-interleaved throughout.
    /// </summary>
    /// <remarks>
    /// The second reference is the point of the test, not padding. Before the
    /// guard the <c>GetFullPath</c> throw escaped <c>ValidateImageRefs</c>
    /// entirely — nothing between it and <c>Main</c> catches — so the compile
    /// died on the first offending page and every page after it lost its
    /// IMAGE_001/_002/_003 pass. Asserting only that the bad reference is
    /// reported would pass just as well against <c>catch { break; }</c>, which
    /// fixes the crash and keeps the blind spot. Requiring the *later* blank
    /// image to be convicted is what makes the scan's continuation the subject.
    /// </remarks>
    [Fact]
    public void An_unresolvable_reference_is_reported_without_ending_the_scan()
    {
        using var tree = new TempGuideTree();
        tree.WriteImage("x/blank.png", MakeCapturedStub(499, 196, blank: true));

        var body =
            "![bad](images/\0broken.png)\n" +
            "![blank](images/x/blank.png)";

        var findings = DiagramProcessor.ValidateImageRefs(
            "topic.md.dt", body, tree.ImagesDir, tree.GuideDir);

        Assert.Equal(
            ["REACTOR_DOC_IMAGE_001", "REACTOR_DOC_IMAGE_002"],
            findings.Select(f => f.Code));

        // The line number has to survive the guard too — it is the half of a
        // TierLintFinding that an escaping exception could never have supplied.
        Assert.Equal(1, findings[0].Line);
        Assert.Equal(2, findings[1].Line);
    }

    /// <summary>
    /// The same guarantee for the other exception <c>GetFullPath</c> actually
    /// raises: an over-long reference is convicted, and the scan continues.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>PathTooLongException</c> derives from <c>IOException</c>, not
    /// <c>ArgumentException</c>, so the original single-type catch did not cover
    /// it and one pathological reference would have aborted the pass for every
    /// page after it — the same blind spot the test above closes, reached by a
    /// different exception type.
    /// </para>
    /// <para>
    /// The threshold is measured rather than assumed. On .NET 10 a 5,000-character
    /// path returns normally and 40,000 throws, so the length below is chosen to
    /// be past the real boundary rather than past the historical <c>MAX_PATH</c>.
    /// The neighbouring claim in the same review — that
    /// <c>NotSupportedException</c> also needs catching — is a .NET Framework-era
    /// assumption and has no test here because it cannot be provoked:
    /// <c>'C:\x\a:b:c'</c>, <c>'http://x/y'</c>, <c>'C:\x\a|b'</c>, <c>'?'</c> and
    /// <c>'*'</c> all resolve without throwing.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_over_long_reference_is_reported_without_ending_the_scan()
    {
        using var tree = new TempGuideTree();
        tree.WriteImage("x/blank.png", MakeCapturedStub(499, 196, blank: true));

        var body =
            "![bad](images/" + new string('a', 40_000) + ".png)\n" +
            "![blank](images/x/blank.png)";

        var findings = DiagramProcessor.ValidateImageRefs(
            "topic.md.dt", body, tree.ImagesDir, tree.GuideDir);

        Assert.Equal(
            ["REACTOR_DOC_IMAGE_001", "REACTOR_DOC_IMAGE_002"],
            findings.Select(f => f.Code));
        Assert.Equal(1, findings[0].Line);
        Assert.Equal(2, findings[1].Line);
    }

    /// <summary>
    /// The real corpus must pass. This is the calibration test: it fails if the
    /// gate is ever tightened past what genuine screenshots satisfy, and the
    /// logged minimum is the documented margin behind the <c>== 0</c> threshold.
    /// </summary>
    [Fact]
    public void Committed_screenshot_corpus_has_no_blank_images()
    {
        var imagesDir = global::System.IO.Path.Join(FindRepoRoot(), "docs", "guide", "images");
        Assert.True(global::System.IO.Directory.Exists(imagesDir), $"images dir not found: {imagesDir}");

        var files = global::System.IO.Directory
            .GetFiles(imagesDir, "*.png", global::System.IO.SearchOption.AllDirectories);

        // Guard against a mis-resolved path producing a confident false
        // all-clear: an empty enumeration would otherwise "pass" below.
        Assert.True(files.Length >= 200,
            $"expected the full committed corpus, found only {files.Length} PNGs under {imagesDir}");

        var blank = new List<string>();
        var minRatio = double.MaxValue;
        var minFile = "";
        var thumbs = 0;

        foreach (var file in files)
        {
            using var bmp = new Bitmap(file);
            // Same region selection the gate uses, so a thumbnail whose content
            // lives in the chrome-inset strip is scored the way it is in
            // production rather than by a stricter rule only this test applies.
            var region = ImageProcessor.ContentRegionFor(file, bmp.Width, bmp.Height);
            if (region == new Rectangle(0, 0, bmp.Width, bmp.Height) &&
                bmp.Width > 20 && bmp.Height > 20)
            {
                thumbs++;
            }
            var content = ImageProcessor.CountContentPixels(bmp, region);
            if (content == 0)
            {
                blank.Add(global::System.IO.Path.GetRelativePath(imagesDir, file));
                continue;
            }

            var ratio = (double)content / ((double)region.Width * region.Height);
            if (ratio < minRatio)
            {
                minRatio = ratio;
                minFile = global::System.IO.Path.GetRelativePath(imagesDir, file);
            }
        }

        _output.WriteLine(
            $"scanned {files.Length} PNGs ({thumbs} scored whole); sparsest interior = {minRatio:P4} ({minFile})");
        Assert.Empty(blank);
    }

    /// <summary>
    /// Calibration for the pre-decode verdicts, through the gate's own entry
    /// point rather than around it. Every committed PNG is referenced from a
    /// synthetic depth-0 page and run through <c>ValidateImageRefs</c>, so the
    /// real corpus meets the real code path.
    /// </summary>
    /// <remarks>
    /// The sibling above decodes each file with <c>new Bitmap</c> directly,
    /// which is the right shape for calibrating the blankness threshold and the
    /// wrong shape for this: it cannot observe a verdict the gate reaches
    /// *before* decoding. When "carries no raster signature" and "is empty"
    /// stopped being skips and became <c>REACTOR_DOC_IMAGE_003</c>, nothing in
    /// the suite would have noticed them false-firing across the committed
    /// corpus — the tests for the change all use synthesized files, and a rule
    /// validated only against inputs built to trip it has never been shown not
    /// to trip everything else.
    /// </remarks>
    [Fact]
    public void Committed_corpus_passes_the_gate_end_to_end()
    {
        var imagesDir = global::System.IO.Path.Join(FindRepoRoot(), "docs", "guide", "images");
        var guideDir = global::System.IO.Path.Join(FindRepoRoot(), "docs", "guide");

        var files = global::System.IO.Directory
            .GetFiles(imagesDir, "*.png", global::System.IO.SearchOption.AllDirectories);
        Assert.True(files.Length >= 200,
            $"expected the full committed corpus, found only {files.Length} PNGs under {imagesDir}");

        var body = string.Join('\n', files.Select(f =>
        {
            var rel = global::System.IO.Path.GetRelativePath(imagesDir, f).Replace('\\', '/');
            return $"![shot](images/{rel})";
        }));

        var findings = DiagramProcessor.ValidateImageRefs(
            "corpus.md.dt", body, imagesDir, guideDir);

        // Report the inputs, not just the verdict: a body that referenced
        // nothing would produce zero findings and look identical to a clean run.
        _output.WriteLine($"referenced {files.Length} committed PNGs; findings = {findings.Count}");
        Assert.Equal(files.Length, body.Split('\n').Length);

        Assert.Empty(findings.Select(f => $"{f.Code} {f.Message}"));

        // ...and prove the scanner reached this body rather than skipping it.
        // Same call, same tree, one extra reference that must be convicted: if
        // the regex stopped matching these paths the clean result above would be
        // clean for the wrong reason, and this line is what separates the two.
        var withBogus = DiagramProcessor.ValidateImageRefs(
            "corpus.md.dt", body + "\n![shot](images/no-such-file-in-corpus.png)", imagesDir, guideDir);
        var extra = Assert.Single(withBogus);
        Assert.Equal("REACTOR_DOC_IMAGE_001", extra.Code);
        Assert.Contains("no-such-file-in-corpus.png", extra.Message);
    }

    /// <summary>
    /// Delegates to <see cref="TestImages.CapturedStub"/>, which these tests pin
    /// the fidelity of (see its remarks).
    /// </summary>
    private static byte[] MakeCapturedStub(int w, int h, bool blank)
        => TestImages.CapturedStub(w, h, blank);

    /// <summary>
    /// A thumbnail-shaped image whose only content sits inside the strip the
    /// full-size chrome inset would trim (2&#160;px leading, 10&#160;px trailing).
    /// Real: scored whole it has content. Scored with the inset it is blank.
    /// That difference is the whole point of the <c>-thumb</c> branch.
    /// </summary>
    private static byte[] MakeEdgeContentThumb(int w, int h)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            using var ink = new SolidBrush(Color.FromArgb(32, 32, 32));
            g.FillRectangle(ink, w - 5, h - 5, 4, 4);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] MakeSolidPng(int w, int h, Color color)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CompositingMode = global::System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.Clear(color);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static string FindRepoRoot()
    {
        var dir = new global::System.IO.DirectoryInfo(global::System.AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (global::System.IO.File.Exists(global::System.IO.Path.Join(dir.FullName, "Reactor.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new global::System.InvalidOperationException(
            "Could not locate repo root (Reactor.slnx) from test base dir.");
    }

    /// <summary>
    /// Minimal <c>docs/guide/images</c> tree, laid out to mirror the real one.
    /// </summary>
    /// <remarks>
    /// <c>ValidateImageRefs</c> resolves a reference against the directory of
    /// the <em>page</em> that carries it, so this fixture exposes the images
    /// root and the page directory separately and callers pass whichever page
    /// depth they mean. <see cref="GuideDir"/> is only the depth-0 case.
    /// <para>
    /// This said "resolves against the parent of the images root" until now,
    /// which was true of the pre-<c>7c1806bf</c> implementation and remains
    /// accidentally true for a top-level page, because that page's directory
    /// <em>is</em> that parent. It is wrong for every other depth, and
    /// <c>Image_ref_must_carry_the_right_traversal_for_its_page_depth</c>
    /// already relies on the real rule. The distinction is the whole point of
    /// that test: "resolve against the images tree" is exactly the model that
    /// made a wrong <c>../</c> count undetectable, so describing the fixture
    /// that way re-documents the defect as the design for whoever writes the
    /// next test against it.
    /// </para>
    /// </remarks>
    private sealed class TempGuideTree : global::System.IDisposable
    {
        private readonly string _root;

        public TempGuideTree()
        {
            _root = global::System.IO.Path.Join(
                global::System.IO.Path.GetTempPath(),
                "reactor-doc-images-" + global::System.Guid.NewGuid().ToString("N"));
            global::System.IO.Directory.CreateDirectory(ImagesDir);
        }

        public string ImagesDir => global::System.IO.Path.Join(_root, "guide", "images");

        /// <summary>
        /// Directory a top-level (depth-0) guide page compiles to. References
        /// resolve relative to the page directory, so <c>images/x.png</c> from
        /// here lands in <see cref="ImagesDir"/> exactly as it does in the real
        /// tree. A nested page passes a deeper directory instead, and needs the
        /// matching <c>../</c> run to reach the same place.
        /// </summary>
        public string GuideDir => global::System.IO.Path.Join(_root, "guide");

        public void WriteImage(string relative, byte[] png) => WriteBytes(relative, png);

        public void WriteBytes(string relative, byte[] bytes)
        {
            var full = Prepare(relative);
            global::System.IO.File.WriteAllBytes(full, bytes);
        }

        public void WriteText(string relative, string text)
        {
            var full = Prepare(relative);
            global::System.IO.File.WriteAllText(full, text);
        }

        private string Prepare(string relative)
        {
            var full = global::System.IO.Path.Join(ImagesDir, relative.Replace('/', global::System.IO.Path.DirectorySeparatorChar));
            global::System.IO.Directory.CreateDirectory(global::System.IO.Path.GetDirectoryName(full)!);
            return full;
        }

        public void Dispose() => FixtureCleanup.DeleteTree(_root);
    }
}
