namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Path predicates shared by the doc pipeline's containment checks.
/// </summary>
/// <remarks>
/// Both callers use this to decide whether a path derived from repository
/// content — a topic id from <c>GetRelativePath</c>, an image reference out of
/// a compiled page — is allowed to be written to or read from. Two copies of a
/// containment rule drift, and the copy that stops being fixed is the one that
/// decides a security-relevant question, so it lives in exactly one place.
/// </remarks>
internal static class DocPaths
{
    /// <summary>
    /// The comparison used to decide whether one path sits inside another.
    /// Case-insensitive on Windows, case-sensitive elsewhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Reactor.Cli</c> targets <c>net10.0</c>, not <c>net10.0-windows</c>, so
    /// this code compiles and can run on a case-sensitive filesystem. There a
    /// fixed <c>OrdinalIgnoreCase</c> is <em>fail-open</em>: it reports
    /// <c>/docs/Guide/x</c> as inside <c>/docs/guide</c>, and on Linux those are
    /// two different directories, so a segment could reach a sibling tree while
    /// the containment check says it did not.
    /// </para>
    /// <para>
    /// The macOS default volume is case-<em>insensitive</em>, so
    /// <c>Ordinal</c> is conservative there — it can reject a path that is in
    /// fact the same directory. That direction is a loud throw rather than a
    /// silent escape, which is the correct way for a containment guard to be
    /// wrong.
    /// </para>
    /// </remarks>
    internal static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    /// <see cref="PathComparison"/> as a <see cref="StringComparer"/>, for
    /// path-keyed dictionaries and sets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Derived from <see cref="PathComparison"/> rather than written out again,
    /// so the two cannot disagree. A second literal would be a second decision
    /// about whether this platform's filesystem is case-sensitive, and the whole
    /// point of having one is that a caller never has to make that call.
    /// </para>
    /// <para>
    /// The direction of a wrong answer here is worth naming, because it is the
    /// opposite of the containment guard's. A case-<em>insensitive</em> comparer
    /// on a case-sensitive filesystem <em>collapses</em> two distinct files into
    /// one key, so a cached verdict computed for one is served for the other —
    /// a blank screenshot inheriting a clean neighbour's <c>Ok</c> and never
    /// reaching <c>REACTOR_DOC_IMAGE_002</c>. That is a gate skipping analysis,
    /// which is a gate passing. Containment errs loud; a cache errs silent.
    /// </para>
    /// <para>
    /// Only genuinely path-keyed collections should use this. The id-keyed
    /// dictionaries in <c>CompileCommand</c> (snippets, screenshots, CLI arg
    /// names) are authored identifiers, not filenames, and their
    /// case-insensitivity is deliberate on every platform.
    /// </para>
    /// </remarks>
    internal static readonly StringComparer PathComparer =
        StringComparer.FromComparison(PathComparison);

    /// <summary>
    /// True when <paramref name="candidate"/> sits inside <paramref name="root"/>.
    /// Both must already be absolute (call <c>Path.GetFullPath</c> first) —
    /// this compares text and does no normalisation of its own.
    /// </summary>
    /// <remarks>
    /// The trailing separator is load-bearing: without it a sibling directory
    /// sharing a prefix, such as <c>docs/guide-old</c> against <c>docs/guide</c>,
    /// satisfies the check and escapes the tree.
    /// </remarks>
    internal static bool IsUnder(string candidate, string root) =>
        IsUnder(candidate, root, PathComparison);

    /// <summary>
    /// <see cref="IsUnder(string,string)"/> with an explicit comparison. Exists
    /// so the case-sensitive arm — the one that ships on Linux and that a
    /// Windows-only test run can never otherwise reach — is still executable
    /// under test.
    /// </summary>
    internal static bool IsUnder(string candidate, string root, StringComparison comparison)
    {
        var rooted = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rooted, comparison);
    }

    /// <summary>
    /// True when <paramref name="segment"/> carries a <c>:</c>, which Windows
    /// resolves as a drive or alternate-data-stream reference rather than as
    /// part of a file name.
    /// </summary>
    /// <remarks>
    /// Extracted so the rule has one home. It was previously spelled inline in
    /// <see cref="ResolveContained"/> only, and the second place that needed it
    /// — the read side, in <c>DiagramProcessor.ValidateImageRefs</c> — had a
    /// comment asserting the hazard was "handled where it is real, in
    /// ResolveContained" while never calling it. Duplication by omission is
    /// still duplication: two sites held the same rule and only one implemented
    /// it.
    /// </remarks>
    internal static bool HasStreamOrDriveSeparator(string segment) =>
        OperatingSystem.IsWindows() && segment.Contains(':');

    /// <summary>
    /// Appends <paramref name="segment"/> to <paramref name="root"/> and returns
    /// the absolute result, throwing when it lands outside
    /// <paramref name="root"/>. <paramref name="describe"/> names the offending
    /// input in the exception.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Path.Join</c>, never <c>Path.Combine</c>. Combine silently discards
    /// everything before a rooted segment, so a content-derived value like
    /// <c>C:/x</c> relocates the result <em>before</em> any containment test can
    /// see it — and the test then compares an already-escaped path against an
    /// already-escaped root and passes. That is a guard which runs, returns a
    /// correct answer, and answers the wrong question. Join concatenates
    /// unconditionally, which leaves this check as the sole decider.
    /// </para>
    /// <para>
    /// Both steps are needed, and neither subsumes the other: Join alone still
    /// admits a <c>..</c> segment that walks back out, and a containment test
    /// alone is defeated by rooting. Callers previously spelled the pair inline,
    /// which meant each site's safety depended on remembering both halves.
    /// </para>
    /// <para>
    /// A third step is needed on Windows, because containment is not the only
    /// way a path can mean something other than it looks like. <c>:</c> is a
    /// stream and drive separator, not an ordinary filename character, so
    /// <c>Path.GetFullPath(Path.Join(root, "topic:hidden"))</c> yields a path
    /// that <em>is</em> textually under <paramref name="root"/> — containment
    /// passes — while a write to it lands in the alternate data stream
    /// <c>hidden</c> on a file named <c>topic</c>. Measured: the directory then
    /// lists a single <c>topic</c> of length&#160;0 and the real bytes are
    /// invisible to a listing, to <c>git</c>, and to any size check. That is the
    /// same defect this helper exists to close, one layer down — a guard that
    /// runs, returns a correct answer, and answers the wrong question — so the
    /// segment is rejected, via <see cref="HasStreamOrDriveSeparator"/>, before
    /// it can be joined.
    /// </para>
    /// </remarks>
    internal static string ResolveContained(string root, string segment, string describe, string? hint = null)
    {
        var suffix = hint is null ? "" : " " + hint;

        if (HasStreamOrDriveSeparator(segment))
            throw new InvalidOperationException(
                $"{describe} contains ':', which Windows resolves as a drive or " +
                "alternate-data-stream reference rather than part of a file name." + suffix);

        var rootFull = Path.GetFullPath(root);
        var full = Path.GetFullPath(Path.Join(rootFull, segment));
        if (!IsUnder(full, rootFull))
            throw new InvalidOperationException($"{describe} would escape '{rootFull}'.{suffix}");
        return full;
    }
}
