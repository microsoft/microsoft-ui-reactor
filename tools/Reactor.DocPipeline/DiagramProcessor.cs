using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Indirection over the Mermaid CLI binary (<c>mmdc</c>) so unit tests can
/// stub the renderer without requiring it on PATH.
/// </summary>
internal interface IMermaidRunner
{
    /// <summary>True when an <c>mmdc</c> binary is on PATH.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Render <paramref name="inputPath"/> (<c>.mmd</c>) to
    /// <paramref name="outputPath"/> (<c>.svg</c>). Returns true on success;
    /// errors should be written to <paramref name="error"/>.
    /// </summary>
    bool Render(string inputPath, string outputPath, out string error);

    /// <summary>
    /// Command line the runner would invoke for the given paths. Exposed
    /// for unit tests to assert the assembled invocation.
    /// </summary>
    string CommandLine(string inputPath, string outputPath);
}

/// <summary>
/// Real <c>mmdc</c> runner. PATH-detection is cached for the process
/// lifetime so subsequent diagrams don't re-shell-out to <c>where</c>.
/// </summary>
internal sealed class MmdcRunner : IMermaidRunner
{
    private bool? _available;

    public bool IsAvailable
    {
        get
        {
            _available ??= DetectMmdc();
            return _available.Value;
        }
    }

    public string CommandLine(string inputPath, string outputPath) =>
        $"mmdc -i \"{inputPath}\" -o \"{outputPath}\"";

    public bool Render(string inputPath, string outputPath, out string error)
    {
        error = "";
        if (!IsAvailable)
        {
            error = "mmdc not on PATH";
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = "mmdc",
            Arguments = $"-i \"{inputPath}\" -o \"{outputPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) { error = "failed to start mmdc"; return false; }
            error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool DetectMmdc()
    {
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "where" : "which",
            Arguments = "mmdc",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            proc.WaitForExit();
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Spec §10.3 diagram pipeline:
/// <list type="bullet">
///   <item>Copies <c>*.svg</c> from <c>docs/_pipeline/diagrams/&lt;topic&gt;/</c>
///         to <c>docs/guide/images/&lt;topic&gt;/</c> (idempotent — SHA-256
///         hash compare).</item>
///   <item>Invokes <c>mmdc</c> for each <c>*.mmd</c> with content-hash
///         caching so unchanged diagrams don't re-render.</item>
///   <item>Validates <c>![..](images/&lt;topic&gt;/...)</c> references in
///         compiled output.</item>
/// </list>
/// </summary>
internal static class DiagramProcessor
{
    /// <summary>
    /// Aggregate of files processed during one diagram pass; surfaced so
    /// callers can write a clean summary line.
    /// </summary>
    internal sealed class DiagramResult
    {
        public List<string> CopiedSvgs { get; } = [];
        public List<string> SkippedSvgs { get; } = [];
        public List<string> RenderedMermaid { get; } = [];
        public List<string> CachedMermaid { get; } = [];
        public List<TierLintFinding> Findings { get; } = [];
    }

    /// <summary>
    /// Process all diagrams in <paramref name="diagramsRoot"/>. When
    /// <paramref name="topic"/> is non-null only that subdirectory is
    /// processed.
    /// </summary>
    public static DiagramResult Process(
        string diagramsRoot,
        string outputImagesRoot,
        IMermaidRunner mermaid,
        string? topic = null)
    {
        var result = new DiagramResult();
        if (!Directory.Exists(diagramsRoot)) return result;

        var topics = topic is null
            ? Directory.GetDirectories(diagramsRoot)
            : new[] { Path.Combine(diagramsRoot, topic) }.Where(Directory.Exists).ToArray();

        foreach (var topicDir in topics)
        {
            var topicId = Path.GetFileName(topicDir);
            var outDir = Path.Combine(outputImagesRoot, topicId);
            Directory.CreateDirectory(outDir);

            // SVG passthrough — hash-compare so identical content is skipped.
            foreach (var svg in Directory.GetFiles(topicDir, "*.svg"))
            {
                var dest = Path.Combine(outDir, Path.GetFileName(svg));
                if (File.Exists(dest) && FilesIdentical(svg, dest))
                {
                    result.SkippedSvgs.Add(Path.GetFileName(svg));
                    continue;
                }
                File.Copy(svg, dest, overwrite: true);
                result.CopiedSvgs.Add(Path.GetFileName(svg));
            }

            // Mermaid render — cache-hash → only re-render on change.
            var mmds = Directory.GetFiles(topicDir, "*.mmd");
            if (mmds.Length > 0 && !mermaid.IsAvailable)
            {
                result.Findings.Add(new TierLintFinding(
                    "REACTOR_DOC_DIAGRAM_001",
                    "mermaid-cli not installed; see docs/contributing/doc-pipeline.md",
                    topicDir, 1, TierLintSeverity.Error));
                continue;
            }

            foreach (var mmd in mmds)
            {
                var name = Path.GetFileNameWithoutExtension(mmd);
                var dest = Path.Combine(outDir, name + ".svg");
                var hashFile = Path.Combine(outDir, "." + name + ".mmd.sha256");
                var currentHash = HashFile(mmd);

                if (File.Exists(dest) && File.Exists(hashFile) &&
                    File.ReadAllText(hashFile).Trim() == currentHash)
                {
                    result.CachedMermaid.Add(name);
                    continue;
                }

                if (!mermaid.Render(mmd, dest, out var err))
                {
                    result.Findings.Add(new TierLintFinding(
                        "REACTOR_DOC_DIAGRAM_001",
                        $"mmdc render failed for {Path.GetFileName(mmd)}: {err}",
                        mmd, 1, TierLintSeverity.Error));
                    continue;
                }

                File.WriteAllText(hashFile, currentHash);
                result.RenderedMermaid.Add(name);
            }
        }

        return result;
    }

    /// <summary>
    /// Validate every <c>![...](images/&lt;topic&gt;/...)</c> reference in
    /// <paramref name="body"/> resolves to a file under
    /// <paramref name="imagesRoot"/>. Missing files raise
    /// <c>REACTOR_DOC_IMAGE_001</c>; a raster image whose interior is blank
    /// raises <c>REACTOR_DOC_IMAGE_002</c>.
    /// </summary>
    /// <param name="blankCache">
    /// Optional path-keyed cache of the blank verdict. The same screenshot is
    /// referenced from several pages, and this runs once per compiled page, so
    /// without a cache the corpus would be decoded many times over. Pass the
    /// same instance across every call in one compile.
    /// </param>
    /// <param name="pageDir">
    /// Directory the assembled page is written to. References resolve relative
    /// to this, exactly as a markdown renderer resolves them — which is what
    /// makes the <c>../</c> run in a nested page's reference *checked* rather
    /// than assumed. Stripping the run instead would normalise away the one
    /// thing that can be wrong about it: a reference carrying the wrong number
    /// of <c>../</c> for its depth resolves somewhere real for the validator
    /// and 404s in the rendered page.
    /// </param>
    public static List<TierLintFinding> ValidateImageRefs(
        string filePath, string body, string imagesRoot, string pageDir,
        Dictionary<string, RasterVerdict>? blankCache = null)
    {
        var findings = new List<TierLintFinding>();
        var imagesFull = Path.GetFullPath(imagesRoot);
        var pageFull = Path.GetFullPath(pageDir);
        // Set once the platform proves it has no decoder, so a machine that
        // cannot run this gate says so once instead of once per image.
        var decoderMissing = false;
        foreach (Match m in ImagePattern.Matches(body))
        {
            var rel = m.Groups[1].Value;
            var line = body[..m.Index].Count(c => c == '\n') + 1;

            // A ':' never survives to the filesystem. On Windows it is a stream
            // or drive separator, not an ordinary filename character, so
            // 'images/t/a.png:hidden' resolves to a path that IS textually under
            // imagesRoot — containment passes — and File.Exists succeeds, while
            // every byte read comes from the alternate data stream rather than
            // from the file the page appears to reference.
            //
            // Measured on .NET 10/Windows against a real ADS: IsUnder=True,
            // File.Exists=True, and ReadAllBytes returns the stream's bytes, not
            // the main stream's. So the gate would score content that no reader
            // of the docs will ever see, and a blank committed screenshot whose
            // alternate stream holds a painted image passes clean — a fail-open
            // in the one gate this PR exists to make fail-closed.
            //
            // DocPaths.ResolveContained already refused this on the write side.
            // It is the same rule, so it is the same predicate: a second copy
            // here is how the two would drift.
            if (DocPaths.HasStreamOrDriveSeparator(rel))
            {
                findings.Add(new TierLintFinding(
                    "REACTOR_DOC_IMAGE_001",
                    $"broken image reference: {rel}",
                    filePath, line, TierLintSeverity.Error));
                continue;
            }

            // Path.Join rather than Path.Combine: Combine drops everything before
            // a rooted segment, which would hand IsUnder an absolute path derived
            // entirely from page content. Join keeps the base, so the containment
            // check below stays the thing that decides.
            //
            // The resolve is guarded because `rel` is page content, not a path the
            // pipeline authored: ImagePattern's [^)]+ tail admits anything but a
            // closing paren. Measured on .NET 10/Windows, only a NUL actually
            // throws here — '|', '"', '<', '*', '?', a 400-character name and a
            // reserved name like `con` all resolve and then fail File.Exists,
            // which is already the right answer. A NUL is not hypothetical
            // either: a doc file saved as UTF-16 and read as UTF-8 is
            // NUL-interleaved throughout. (':' no longer reaches here — it is
            // refused above, because "resolve then fail File.Exists" is exactly
            // what it does *not* do.)
            //
            // Letting it escape would be worse than the broken reference it
            // describes. Nothing between here and Main catches it, so the compile
            // dies on the first offending page and every page after it loses its
            // IMAGE_001/_002/_003 pass entirely — the blank-screenshot gate stops
            // covering the rest of the corpus while still reporting a failure that
            // names Path.GetFullPath rather than the file and line at fault.
            // A reference that cannot be turned into a path is broken, so say so
            // and keep scanning.
            string full;
            try
            {
                full = Path.GetFullPath(Path.Join(
                    pageFull, rel.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
            {
                // ArgumentException covers the empty/whitespace/embedded-NUL
                // shapes. PathTooLongException is separate — it derives from
                // IOException, not ArgumentException — and GetFullPath does
                // raise it: measured on .NET 10, a ~40,000-character reference
                // throws it while 5,000 does not. Without this arm one
                // pathological reference aborts the scan, which is precisely the
                // failure this catch exists to prevent.
                //
                // NotSupportedException is deliberately absent. It was the
                // .NET Framework behaviour for a colon mid-path, and on .NET 10
                // it is not raised at all: 'C:\x\a:b:c', 'http://x/y',
                // 'C:\x\a|b', '?' and '*' all return normally. Catching it would
                // be a dead arm — which is also why the colon is rejected by an
                // explicit test above rather than left to fall out of this catch.
                findings.Add(new TierLintFinding(
                    "REACTOR_DOC_IMAGE_001",
                    $"broken image reference: {rel}",
                    filePath, line, TierLintSeverity.Error));
                continue;
            }

            // Anything that resolves outside the images tree is a malformed
            // reference — including one whose ../ run doesn't match its page
            // depth — not a file to go probing for.
            if (!DocPaths.IsUnder(full, imagesFull) || !File.Exists(full))
            {
                findings.Add(new TierLintFinding(
                    "REACTOR_DOC_IMAGE_001",
                    $"broken image reference: {rel}",
                    filePath, line, TierLintSeverity.Error));
                continue;
            }

            switch (ScanRaster(full, blankCache))
            {
                case RasterVerdict.Blank:
                    findings.Add(new TierLintFinding(
                        "REACTOR_DOC_IMAGE_002",
                        $"blank screenshot: {rel} has no visible content — it was most likely " +
                        "overwritten by a capture whose doc-app window never painted. Restore it " +
                        "from git and re-capture on an interactive desktop.",
                        filePath, line, TierLintSeverity.Error));
                    break;

                case RasterVerdict.NotAnImage:
                    findings.Add(new TierLintFinding(
                        "REACTOR_DOC_IMAGE_003",
                        $"not an image: {rel} is named as a raster but its bytes carry no PNG or " +
                        "JPEG signature (or it is empty), so it will not render anywhere. Common " +
                        "causes are a checkout of an LFS-tracked file made without Git LFS " +
                        "(run `git lfs pull`), a saved HTML error page, or an SVG given a .png name.",
                        filePath, line, TierLintSeverity.Error));
                    break;

                case RasterVerdict.Undecodable:
                    findings.Add(new TierLintFinding(
                        "REACTOR_DOC_IMAGE_003",
                        $"unreadable image: {rel} exists but could not be read or decoded, so it " +
                        "cannot be checked for content. If the file is intact, check that it is not " +
                        "locked by another process and that its permissions allow reading; otherwise " +
                        "it is corrupt and will not render — restore it from git and re-capture.",
                        filePath, line, TierLintSeverity.Error));
                    break;

                case RasterVerdict.Unavailable:
                    // Reported once per page. The condition is a property of the
                    // machine, not of any image, so one finding per screenshot in
                    // the corpus would bury the single fact that matters.
                    //
                    // Suppresses only this finding, not the scan: the pre-decode
                    // guards in ComputeRasterVerdict need no decoder, so a
                    // zero-byte or signature-less file still earns its
                    // REACTOR_DOC_IMAGE_003 on a machine that cannot decode.
                    // Skipping the rest of the loop here would have thrown those
                    // away to save a duplicate warning.
                    if (decoderMissing) break;
                    decoderMissing = true;
                    findings.Add(new TierLintFinding(
                        "REACTOR_DOC_IMAGE_004",
                        "blank-image gate skipped: image decoding is unavailable on this " +
                        "platform (System.Drawing.Common is Windows-only), so referenced images " +
                        "were not checked for content on this run. No image is implicated — " +
                        "references were still validated. Compile on Windows for the full check.",
                        filePath, line, TierLintSeverity.Warning));
                    break;
            }
        }
        return findings;
    }

    /// <summary>
    /// Classifies <paramref name="path"/> as scannable-and-fine, blank, or
    /// undecodable. "Blank" means a raster image whose interior (excluding the
    /// border + drop shadow the capture pipeline draws) contains no content at
    /// all. Non-raster references (SVG) and files rejected by a pre-decode cap
    /// are reported <see cref="RasterVerdict.Ok"/> — this gate exists to catch
    /// the specific solid-white stub a failed capture produces, not to
    /// second-guess authored assets.
    /// </summary>
    /// <remarks>
    /// The predicate is strictly "zero content pixels", never "small". The
    /// committed corpus contains legitimately tiny screenshots — the smallest
    /// is 89×40 / 2127&#160;B — so any byte-size floor able to catch a ~3&#160;KB stub
    /// would also condemn real assets. Measured margin: across all 227
    /// committed PNGs the *sparsest* interior is 0.6084&#160;% content pixels
    /// (<c>navigation/navigation-view.png</c>), i.e. ~600× the zero threshold.
    /// <c>DocImageIntegrityTests.Committed_screenshot_corpus_has_no_blank_images</c>
    /// re-measures this on every run and logs it.
    /// </remarks>
    private static RasterVerdict ScanRaster(string path, Dictionary<string, RasterVerdict>? cache)
    {
        if (cache is not null && cache.TryGetValue(path, out var cached)) return cached;

        var verdict = ComputeRasterVerdict(path);
        cache?[path] = verdict;
        return verdict;
    }

    /// <summary>
    /// Outcome of scanning a referenced image. Four states rather than two
    /// because "not blank" and "could not tell" must not be spelled the same
    /// way: a gate that reports the second as the first passes silently on
    /// exactly the corrupt files it exists to notice. The two failure states
    /// are split because the gate genuinely knows which it is, and folding them
    /// together would make the finding text offer a remedy it can already rule
    /// out — telling someone to check file permissions on a Git-LFS pointer.
    /// </summary>
    internal enum RasterVerdict
    {
        /// <summary>Decoded and has visible content, or deliberately not scanned (SVG, over-cap).</summary>
        Ok,

        /// <summary>Decoded, and the scanned region contains no content pixel at all.</summary>
        Blank,

        /// <summary>
        /// Rejected before the decode step by a check about the file's own
        /// content: empty, or carrying no PNG/JPEG signature despite a raster
        /// extension. The file was read successfully — it simply is not an
        /// image, so it will not render for anyone, anywhere. A Git-LFS pointer,
        /// a saved HTML error page, or a mislabelled SVG all land here.
        /// </summary>
        NotAnImage,

        /// <summary>
        /// Admitted to the decode step and the read or decode faulted. Spans
        /// corruption and a file that is merely unreadable right now (locked,
        /// permission-denied) — those raise from the same call and the gate
        /// cannot tell them apart, so the finding text offers both remedies.
        /// </summary>
        Undecodable,

        /// <summary>
        /// No decoder on this platform. <c>System.Drawing.Common</c> is
        /// Windows-only, so off-Windows there is no decoder and the gate never
        /// saw the file's pixels. Says nothing about the file itself.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="Undecodable"/> on purpose. Undecodable is a
        /// statement about the file; this is a statement about the run, and
        /// folding it into the other would blame every image in the corpus for a
        /// property of the machine — telling an author to `git checkout` files
        /// that are perfectly fine. It is equally not <see cref="Ok"/>: a gate
        /// that could not run has not found nothing, and reporting those as the
        /// same outcome is the exact defect this gate exists to close.
        ///
        /// Reached by a platform test, not by catching an exception: which
        /// exception System.Drawing.Common raises off-Windows has already
        /// changed between runtimes, so a catch clause naming one of them is a
        /// guard that would quietly stop firing. See ComputeRasterVerdict.
        /// </remarks>
        Unavailable,
    }

    private static RasterVerdict ComputeRasterVerdict(string path)
    {
        var ext = Path.GetExtension(path);
        if (!ext.Equals(".png", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return RasterVerdict.Ok;
        }

        try
        {
            // Pre-decode guards, mirroring ImageProcessor.Process (TASK-044).
            // This walks every referenced file in the committed corpus and hands
            // it to GDI+, so a hostile or corrupt image must be rejected on
            // cheap metadata before a decoder ever sees it.
            //
            // Only one of these is a *skip*. Over-cap means the gate declined to
            // scan a file that is still a real image, so reporting it would blame
            // the file for a decision this gate made about it; a missing file is
            // REACTOR_DOC_IMAGE_001's business and is already reported there.
            //
            // The other two are verdicts. The extension filter above has already
            // established that this path ends in .png/.jpg/.jpeg, so "empty" and
            // "carries no PNG or JPEG signature" are statements about the file's
            // content: it will not render, wherever it came from. Returning Ok
            // for those let the gate finish clean on a page with a broken image —
            // the outcome it exists to prevent. A checkout of an LFS-tracked repo
            // without LFS is the ordinary way to get there: every image is a short
            // text pointer named .png, and the whole corpus passed silently.
            //
            // They report NotAnImage rather than Undecodable because the file read
            // fine and the gate knows it: both spell REACTOR_DOC_IMAGE_003, but the
            // message must not send someone to check file locks and permissions on
            // a file whose problem is that it is a line of text.
            //
            // That split only holds while HasRasterMagic answers a question about
            // the file's *content*. It must not answer "false" because it could
            // not read the file — that is a check that did not run, not a file
            // that is not a raster, and it lands in the catch below with every
            // other read fault. See the remarks on HasRasterMagic.
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > ImageProcessor.MaxImageBytes) return RasterVerdict.Ok;
            if (info.Length == 0) return RasterVerdict.NotAnImage;
            if (!HasRasterMagic(path)) return RasterVerdict.NotAnImage;

            // Everything above is decoder-free, so it keeps working off-Windows;
            // everything below needs GDI+. Gate on the platform rather than on an
            // exception type, because *which* exception System.Drawing.Common
            // raises elsewhere is a moving target: .NET 6 documented a
            // TypeInitializationException wrapping PlatformNotSupportedException
            // (thrown from Gdip's .cctor, so the inner type is not what a catch
            // clause sees), and the net10.0 assembly this repo resolves does not
            // construct PlatformNotSupportedException at all — measured against
            // both flavours in the package, the net8.0 one builds it inside a
            // .cctor lambda and the net10.0 one builds it nowhere.
            //
            // A catch written against any single one of those spellings is a
            // guard that stops firing when the next runtime changes it, and says
            // nothing when it does. That is worse than no guard here, because
            // this one exists to keep Phase 6 from dying outright: the assembly
            // is [SupportedOSPlatform("windows")] and net10.0 exists only because
            // PackAsTool requires it, but Phase 6 had no decoder dependency
            // before this gate added one, and a phase that used to run has no
            // business terminating a compile when it stops being able to.
            //
            // Placed after the pre-decode guards on purpose: a zero-byte or
            // signature-less file is still REACTOR_DOC_IMAGE_003 on every
            // platform. Returning Unavailable from the top of the method would
            // have surrendered checks that never needed a decoder.
            if (!OperatingSystem.IsWindows()) return RasterVerdict.Unavailable;

            using var bmp = new global::System.Drawing.Bitmap(path);
            if (bmp.Width > ImageProcessor.MaxImageDimension ||
                bmp.Height > ImageProcessor.MaxImageDimension)
            {
                return RasterVerdict.Ok;
            }

            var region = ImageProcessor.ContentRegionFor(path, bmp.Width, bmp.Height);
            if (!ImageProcessor.HasContentPixel(bmp, region)) return RasterVerdict.Blank;

            // A committed image whose scored region is one flat colour is as
            // blank as a white one — see ImageProcessor.IsUniformFill for why
            // this is uniformity and not a coverage floor.
            return ImageProcessor.IsUniformFill(bmp, region) ? RasterVerdict.Blank : RasterVerdict.Ok;
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException or IOException
                                      or UnauthorizedAccessException
                                      or global::System.Runtime.InteropServices.ExternalException)
        {
            // The file cleared every pre-decode guard and the read or decode
            // still faulted, so the gate could not score it. GDI+ surfaces
            // decode faults as ArgumentException *or* ExternalException
            // depending on the fault, so both are caught here: a compile must
            // not die on one bad byte in one image, but it must not pass
            // silently either, which is what returning "not blank" used to do.
            //
            // IOException and UnauthorizedAccessException are caught too, and
            // they are *not* corruption — a file locked by another process or
            // denied by permissions is intact and will render fine once the
            // condition clears. They land here because the alternative is
            // worse: letting them escape kills the whole compile over one
            // transiently-locked file, and excluding them from the catch would
            // do exactly that. So the verdict deliberately spans "corrupt" and
            // "couldn't read right now", and the finding text must offer both
            // remedies rather than sending someone to `git checkout` a file
            // whose only problem is a file handle. Naming one cause when the
            // catch admits several is the same over-claim this gate exists to
            // stop, one level up.
            //
            // Reported separately from IMAGE_002 because the remedy differs —
            // a blank capture is re-captured, an unreadable file is unlocked or
            // restored — and because a decode fault misreported as "blank"
            // sends an author chasing a rendering problem that isn't there.
            return RasterVerdict.Undecodable;
        }
    }

    /// <summary>
    /// Reads only the leading signature bytes to confirm a file really is a PNG
    /// or JPEG, so a mislabelled or crafted <c>.png</c> is never handed to GDI+.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ReadExactly</c> rather than <c>Read</c>: a single <c>Read</c> may
    /// legally return fewer bytes than asked for without being at EOF, and the
    /// old <c>read == head.Length</c> test turned that into "no magic" — which
    /// this gate treats as "not a raster" and skips. A short read would
    /// therefore have silently disabled blank-frame validation for a perfectly
    /// good PNG, which is the same fail-open this pipeline reports as
    /// REACTOR_DOC_IMAGE_003 elsewhere. <c>ReadExactly</c> either fills the
    /// buffer or throws, so the only remaining way out is a genuinely short
    /// file, which is handled below and really is not an image.
    /// </para>
    /// <para>
    /// <strong>Read faults are not answers.</strong> This method deliberately
    /// does <em>not</em> catch <c>IOException</c> or
    /// <c>UnauthorizedAccessException</c>. It used to, returning <c>false</c>,
    /// and the caller reads <c>false</c> as "not a raster" and skips the file —
    /// so a locked or permission-denied image produced a clean compile. That
    /// contradicted <see cref="ComputeRasterVerdict"/>, whose catch admits those
    /// two exact types and reports them, on the stated reasoning that the
    /// verdict spans "corrupt" and "couldn't read right now". Both could not be
    /// right, and the one that won was the silent one. Letting them propagate
    /// puts the decision back in the single place that documents it.
    /// </para>
    /// </remarks>
    private static bool HasRasterMagic(string path)
    {
        using var fs = File.OpenRead(path);
        var head = new byte[8];
        try
        {
            fs.ReadExactly(head);
        }
        catch (EndOfStreamException)
        {
            // Shorter than any signature we recognise. Distinct from a read
            // fault: there is nothing more to wait for, so this is a real
            // answer ("not a raster") rather than a check that did not run.
            //
            // This catch is now load-bearing, which it was not when written.
            // EndOfStreamException derives from IOException, so with the old
            // blanket catch below it killed no test — measured, and recorded as
            // such. Removing that blanket catch to stop swallowing read faults
            // is exactly the tightening its comment anticipated: without this
            // inner catch a 4-byte stub would now escape as an IOException and
            // be reported as undecodable rather than skipped.
            return false;
        }

        return ImageProcessor.HasKnownImageMagic(head);
    }

    /// <summary>
    /// Create a starter Mermaid flowchart file at
    /// <c>docs/_pipeline/diagrams/&lt;topic&gt;/&lt;id&gt;.mmd</c>. Returns
    /// the absolute path written.
    /// </summary>
    public static string ScaffoldDiagram(string diagramsRoot, string topic, string id)
    {
        var topicDir = Path.Combine(diagramsRoot, topic);
        Directory.CreateDirectory(topicDir);
        var path = Path.Combine(topicDir, id + ".mmd");
        if (File.Exists(path))
            throw new DocPipelineException(
                "REACTOR_DOC_DIAGRAM_002",
                $"diagram already exists: {path}");
        File.WriteAllText(path, StarterTemplate);
        return path;
    }

    private const string StarterTemplate = """
        %% Replace with your diagram. Author light + dark themes by keeping
        %% palette-neutral colors (GitHub renders SVG with its own theme).
        flowchart LR
            A[Start] --> B{Decision}
            B -- yes --> C[Do thing]
            B -- no  --> D[Other thing]
            C --> E[End]
            D --> E
        """;

    /// <summary>
    /// Matches a markdown image reference into the guide's image tree.
    /// </summary>
    /// <remarks>
    /// The <c>(\.\./)*</c> prefix is load-bearing. <c>DocAssembler</c> prepends
    /// one <c>../</c> per level of nesting for topic ids containing <c>/</c>
    /// (e.g. <c>recipes/login</c>), and validation runs on the <em>assembled</em>
    /// output, not the template. Anchoring on a bare <c>images/</c> therefore
    /// skipped every nested page — 10 of them today — so neither the broken-link
    /// check nor the blank-screenshot gate ever saw their images. A gate with a
    /// silent blind spot is worse than no gate, because its clean run reads as
    /// coverage.
    /// </remarks>
    private static readonly Regex ImagePattern =
        new(@"!\[[^\]]*\]\(((?:\.\./)*images/[^)]+)\)", RegexOptions.Compiled);

    private static bool FilesIdentical(string a, string b)
    {
        try
        {
            if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
            return HashFile(a) == HashFile(b);
        }
        catch
        {
            return false;
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(stream, hash);
        var sb = new StringBuilder(64);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
