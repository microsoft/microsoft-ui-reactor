using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// One phantom-symbol finding. Same shape as <see cref="CrossLinkFinding"/> so
/// the compile loop can print doc lints uniformly.
/// </summary>
internal sealed record PhantomFinding(
    string Code,
    string Message,
    string FilePath,
    int Line,
    TierLintSeverity Severity)
{
    public string Format() => $"{FilePath}:{Line} {Code}: {Message}";
}

/// <summary>
/// One API name that does not exist but keeps appearing in documentation,
/// together with the discriminator that separates the phantom from the real
/// members that share its name.
/// </summary>
/// <param name="Name">Display name used in the diagnostic message.</param>
/// <param name="Pattern">
/// Case-sensitive match. Every pattern MUST encode its own false-positive
/// discriminators — see the table in <see cref="PhantomSymbolLint.Phantoms"/>.
/// </param>
/// <param name="Suggestion">The correct spelling to steer the author to.</param>
internal sealed record PhantomSymbol(string Name, Regex Pattern, string Suggestion);

/// <summary>
/// Documentation-surface phantom-API lint (<c>REACTOR_DOC_PHANTOM_001</c>).
///
/// <para><b>Why this exists.</b> Reactor's toolchain validates executable code
/// (the compiler, plus <c>REACTOR_DYM_003</c> / <c>REACTOR_DYM_005</c>) and
/// validates snippet-backed doc blocks (they are compiled from the doc apps).
/// It validates nothing else. Four documentation surfaces are exempt from every
/// existing check, and all four have been observed to rot:</para>
/// <list type="number">
/// <item>bare <c>```csharp</c> blocks in <c>docs/_pipeline/templates/**</c>,</item>
/// <item>example source embedded in <b>string literals</b> inside doc apps
///   (compiles green forever — the file is valid C#, the string is not checked),</item>
/// <item>gallery <c>SourceCode</c> strings in <c>samples/ReactorGallery/**</c>,</item>
/// <item><c>///</c> XML doc comments in <c>src/**</c> — never validated, yet
///   shipped in <c>Reactor.xml</c> as the IntelliSense every NuGet consumer sees.</item>
/// </list>
///
/// <para>Surface 4 is the propagation source: guide templates have been observed
/// quoting a source doc comment almost verbatim, so fixing the copies without the
/// original just re-seeds them.</para>
///
/// <para><b>Scope discipline.</b> This lint deliberately does NOT look at
/// executable code. A did-you-mean that fires on ordinary contributor code is
/// noise, and noise is how a rule gets globally disabled — which costs more than
/// it ever saved. The line is mechanical: is the text inside a <c>///</c>
/// comment, a string literal, or a Markdown fence? Then nothing validates it and
/// this lint applies. Is it a statement the compiler parses? Then leave it alone.</para>
/// </summary>
internal static class PhantomSymbolLint
{
    public const string Code = "REACTOR_DOC_PHANTOM_001";

    /// <summary>
    /// Error. The staged roll-out (Warning while known occurrences were being
    /// cleared across branches) is complete: the tree now reports zero
    /// template and assembled-snippet findings, which is the condition this
    /// was waiting on. Leaving it at Warning meant a newly introduced
    /// <!-- phantom:skip "Text" --> <!-- phantom:skip "UseTheme" -->
    /// <c>Text("...")</c> or <c>UseTheme()</c> still passed
    /// <c>mur docs compile</c>, which is the exact regression the rule exists
    /// to stop. The <c>src/**</c> XML-doc backlog is gated separately by the
    /// ceiling-budget test, so raising this does not fail on the known
    /// historical occurrences.
    /// </summary>
    internal const TierLintSeverity DefaultSeverity = TierLintSeverity.Error;

    /// <summary>
    /// The phantom table. Add an entry here to cover a new phantom; every
    /// consumer picks it up automatically.
    ///
    /// <para><b>Every pattern is case-sensitive on purpose.</b> C# identifiers
    /// are, and a case-insensitive <c>Text\(</c> matches ordinary English prose
    /// such as "Placeholder text (for TextBoxElement etc.)" — an entire
    /// false-positive class that only appears once you look.</para>
    /// </summary>
    internal static readonly PhantomSymbol[] Phantoms =
    [
        // Text: there is no core `Text(...)` element factory. The real members
        // named Text are CellRenderers.Text / Editors.Text (both
        // Func<object, Element>), DragData.Text, and D3Charts.Text(x, y, text, …).
        // Two discriminators, either of which alone would be enough:
        //   (a) unqualified — no leading '.', so `D3Charts.Text(` and
        //       `CellRenderers.Text(` are excluded even under `using static`;
        //   (b) string-literal first argument — so the positional
        //       `Text(16, 16, "hi")` shape of D3Charts.Text is excluded too.
        // The opening quote is matched in all three spellings it takes on these
        // surfaces: plain `"`, backslash-escaped `\"` (example code embedded in
        // a C# string literal — the docking defect), and doubled `""` (the same
        // inside a verbatim string). Matching only the plain form would blind the
        // rule to the exact surface it was written for.
        new("Text",
            new Regex(@"(?<![A-Za-z0-9_.])Text\s*\(\s*(?:\$|@)?(?:\\""|""""|"")", RegexOptions.Compiled),
            "TextBlock(...)"),

        // UseTheme: no such hook. Theme values are read from the Theme statics.
        new("UseTheme",
            new Regex(@"(?<![A-Za-z0-9_])UseTheme\s*\(", RegexOptions.Compiled),
            "the Theme.* tokens"),

        // The three transition modifiers below never existed under these names.
        new("WithOpacityTransition",
            new Regex(@"(?<![A-Za-z0-9_])WithOpacityTransition\b", RegexOptions.Compiled),
            "the Animate/Transition modifiers"),
        new("WithThemeTransitions",
            new Regex(@"(?<![A-Za-z0-9_])WithThemeTransitions\b", RegexOptions.Compiled),
            "the Animate/Transition modifiers"),
        new("WithImplicitTransition",
            new Regex(@"(?<![A-Za-z0-9_])WithImplicitTransition\b", RegexOptions.Compiled),
            "the Animate/Transition modifiers"),

        // Optional.Of: only Optional<T>.Of(T) exists — there is no non-generic
        // static Optional class, so `Optional.Of(-1)` does not bind. The
        // lookbehind must exclude identifier characters ONLY: excluding '>'
        // would also swallow the `<c>Optional.Of(` form this phantom almost
        // always appears in, silently zeroing the rule.
        new("Optional.Of",
            new Regex(@"(?<![A-Za-z0-9_])Optional\.Of\s*\(", RegexOptions.Compiled),
            "Optional<T>.Of(...)"),
    ];

    // <!-- phantom:skip -->            → silence the rest of this doc region
    // <!-- phantom:skip "Text" -->     → silence just one phantom
    //
    // Same shape and spelling convention as the pipeline's existing
    // <!-- xlink:skip --> marker, and valid inside both Markdown and XML doc
    // comments. This is the sanctioned way to keep a sentence that *names* a
    // phantom in order to warn against it — the alternative (weakening the
    // pattern until the warning sentence slips through) would blind the rule to
    // the real defect it is there to catch.
    private static readonly Regex SkipMarker = new(
        @"<!--\s*phantom:skip(?:\s+""([^""]+)"")?\s*-->", RegexOptions.Compiled);

    // <see cref="..."/> and <seealso cref="..."/> are compiler-validated
    // (CS1574 fires on an unresolvable cref), so they are NOT an unvalidated
    // surface and must never be matched. <c>/<code> are not validated and are
    // exactly where the phantoms live.
    private static readonly Regex CrefSpan = new(
        @"<(see|seealso)\b[^>]*?/?>", RegexOptions.Compiled);

    /// <summary>What kind of text is being linted, which decides the gate.</summary>
    internal enum Surface
    {
        /// <summary>A <c>.md.dt</c> template or an assembled guide body. Only
        /// fenced code blocks are linted; ordinary prose is not, so a sentence
        /// that names a phantom to warn against it stays silent.</summary>
        Markdown,

        /// <summary>A C# file: only <c>///</c> doc-comment lines are linted.
        /// Executable statements are the compiler's job.</summary>
        CSharpDocComments,

        /// <summary>Raw example text — a gallery <c>SourceCode</c> string or an
        /// extracted snippet body. Every line is code, so every line is linted.</summary>
        ExampleText,
    }

    /// <summary>
    /// Lint one document. Pure: no file-system or process access, so the same
    /// matcher backs the docs-compile lint and the test gate without drift.
    /// </summary>
    public static List<PhantomFinding> Lint(string filePath, string text, Surface surface)
    {
        var findings = new List<PhantomFinding>();
        if (string.IsNullOrEmpty(text)) return findings;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var inFence = false;
        var skipAll = false;
        var skipNames = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var trimmed = raw.Trim();
            var isDocComment = trimmed.StartsWith("///", StringComparison.Ordinal);

            if (surface == Surface.Markdown)
            {
                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    // A closing fence ends the region a marker applied to.
                    if (!inFence) { skipAll = false; skipNames.Clear(); }
                    continue;
                }
            }
            else if (surface == Surface.CSharpDocComments && !isDocComment)
            {
                // Leaving a contiguous `///` block ends the region a marker
                // applied to — the doc-comment analogue of a closing fence.
                // Without this a single skip marker would blind the lint to
                // every later doc comment in the file.
                skipAll = false;
                skipNames.Clear();
            }

            // Honor skip markers wherever they appear, including immediately
            // before a fence, so an author can annotate the block from prose.
            foreach (Match sk in SkipMarker.Matches(raw))
            {
                var arg = sk.Groups[1].Success ? sk.Groups[1].Value.Trim() : "";
                if (arg.Length == 0) skipAll = true;
                else skipNames.Add(arg);
            }

            var applicable = surface switch
            {
                Surface.Markdown => inFence,
                Surface.CSharpDocComments => isDocComment,
                _ => true,
            };
            if (!applicable || skipAll) continue;

            // Blank out compiler-validated cref spans so `<see cref="Foo.Text"/>`
            // can never trip the rule. Same-length filler keeps columns stable.
            var masked = CrefSpan.Replace(raw, m => new string(' ', m.Length));

            foreach (var phantom in Phantoms)
            {
                if (skipNames.Contains(phantom.Name)) continue;
                if (!phantom.Pattern.IsMatch(masked)) continue;

                findings.Add(new PhantomFinding(
                    Code,
                    $"'{phantom.Name}' does not exist; use {phantom.Suggestion}. " +
                    "This is a documentation surface, so nothing else validates it — " +
                    "fix it, or add <!-- phantom:skip \"" + phantom.Name + "\" --> if the " +
                    "text names the phantom in order to warn against it.",
                    filePath,
                    i + 1,
                    DefaultSeverity));
            }
        }

        return findings;
    }
}
