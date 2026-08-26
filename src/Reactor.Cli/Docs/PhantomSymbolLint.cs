using System.Linq;
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
/// <param name="Name">Reported identifier.</param>
/// <param name="Pattern">
/// Line matcher. Ignored when <paramref name="Scanner"/> is supplied.
/// </param>
/// <param name="Suggestion">What to write instead.</param>
/// <param name="Scanner">
/// Optional structural matcher for shapes a regex cannot express. Supplied for
/// <c>Text</c>, whose argument is an arbitrary C# expression: enumerating
/// argument prefixes is open-ended by construction and kept missing shapes
/// (a ternary, a concatenation, a cast). Balanced scanning ends that.
/// <para>
/// Receives a lookahead window — the current line plus following code lines, so
/// an invocation wrapped across lines can still be closed — and the length of
/// the first line. It must only accept invocations that <i>start</i> within the
/// first line, otherwise every earlier line whose window reaches this call
/// would report it too.
/// </para>
/// </param>
internal sealed record PhantomSymbol(
    string Name,
    Regex Pattern,
    string Suggestion,
    global::System.Func<string, int, bool>? Scanner = null)
{
    /// <summary>Whether this phantom appears in the (already masked) text.</summary>
    internal bool Matches(string text, int firstLineLength) =>
        Scanner is not null ? Scanner(text, firstLineLength) : Pattern.IsMatch(text);
}

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
        //
        // The second alternative covers the dynamic-argument form
        // `Text(statusMessage)` — a real phantom fixed in ElementExtensions.cs
        // that (b) alone missed, so the budget gate did not hold it. A lone
        // identifier argument cannot be D3Charts.Text, which takes x, y and
        // text positionally, so requiring the closing paren keeps that excluded.
        //
        // The scanner replaces what used to be an enumeration of argument
        // prefixes (literal / bare identifier / call / indexer / null-coalesce).
        // That enumeration was open-ended by construction and kept missing
        // shapes — `Text(enabled ? "On" : "Off")`, `Text(prefix + value)`,
        // `Text((string)value)` — each of which needed another arm. Balanced
        // scanning asks the structural question instead: an unqualified `Text`
        // invoked with exactly one top-level argument.
        new("Text",
            new Regex(@"(?<![A-Za-z0-9_.])Text\s*\(", RegexOptions.Compiled),
            "TextBlock(...)",
            LooksLikeSingleArgTextCall),

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

        // VStack(...) and HStack(...) both return StackElement — there has never
        // been a VStackElement or HStackElement type. The identifier lookbehind
        // is load-bearing in the other direction here: LazyVStackElement<T> is
        // real, and a pattern without it would flag every mention of the type
        // that actually exists.
        new("VStackElement",
            new Regex(@"(?<![A-Za-z0-9_])VStackElement\b", RegexOptions.Compiled),
            "StackElement"),
        new("HStackElement",
            new Regex(@"(?<![A-Za-z0-9_])HStackElement\b", RegexOptions.Compiled),
            "StackElement"),

        // RenderContext exposes no static Current: a component reaches its
        // context through the ctx parameter it is handed, and inventing an
        // ambient accessor is the single most plausible-looking wrong answer.
        new("RenderContext.Current",
            new Regex(@"(?<![A-Za-z0-9_])RenderContext\.Current\b", RegexOptions.Compiled),
            "the ctx parameter passed to Render"),

        // No ElementDescription type exists. Scoped to the `.Of(` call shape
        // rather than the bare word so prose that describes "the element
        // description" in English cannot trip it.
        new("ElementDescription.Of",
            new Regex(@"(?<![A-Za-z0-9_])ElementDescription\.Of\s*\(", RegexOptions.Compiled),
            "ElementDescription(...) does not exist; use the accessibility modifiers"),

        // Not a real diagnostic id. A fabricated REACTOR_/A11Y_ code is worse
        // than a fabricated API: the reader greps for it, finds nothing, and has
        // no way to tell a typo from a version skew.
        new("A11Y_KEYBOARD_001",
            new Regex(@"(?<![A-Za-z0-9_])A11Y_KEYBOARD_001\b", RegexOptions.Compiled),
            "a real REACTOR_A11Y_* id"),

        // There is no ProgressBar(...) factory — the factories are Progress(value)
        // and ProgressIndeterminate(), both returning ProgressElement. Two
        // exclusions keep the real WinUI type usable: the '.'/identifier
        // lookbehind leaves qualified uses alone, and `new ProgressBar(` is
        // legitimate interop. `Action<ProgressBar>` and `.Set<ProgressBar>(`
        // never match because neither puts '(' straight after the name.
        new("ProgressBar",
            new Regex(@"(?<![A-Za-z0-9_.])(?<!new\s)ProgressBar\s*\(", RegexOptions.Compiled),
            "Progress(value) / ProgressIndeterminate()"),

        // There is no `UI` facade class: factories are imported with
        // `using static Microsoft.UI.Reactor.Factories;` and called bare. This
        // is the one phantom shape the other patterns are structurally blind to
        // — they all exclude member-qualified calls in order to spare real
        // members like D3Charts.Text(, so `UI.Text(` slipped past the agent-kit
        // sweep while it reported clean. Matching the *receiver* instead of the
        // member is what closes that hole.
        //
        // `Microsoft.UI.Xaml...` and `Microsoft.UI.Reactor...` cannot match:
        // the lookbehind excludes a preceding '.', and the trailing `\(`
        // requires the call to be on the segment straight after `UI.`.
        new("UI.",
            new Regex(@"(?<![A-Za-z0-9_.])UI\.[A-Z][A-Za-z0-9_]*\s*\(", RegexOptions.Compiled),
            "the bare factory (using static Microsoft.UI.Reactor.Factories)"),
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
        /// <summary>A <c>.md.dt</c> template or an assembled guide body. Fenced code
        /// blocks are linted in full, and outside a fence only inline code spans
        /// (<c>`like this`</c>) are — ordinary prose is never linted, so a sentence
        /// that names a phantom to warn against it stays silent unless it puts the
        /// phantom in code formatting, which reads as an endorsement.</summary>
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

                // A blank line outside a fence ends the region too, scoping a
                // prose marker to its own paragraph. Without this, a marker set
                // in prose survived until the *next closing* fence — so it also
                // silenced the whole unrelated code block in between, and an
                // accidental phantom in that block would have passed the gate.
                // Attaching the marker directly above a fence with no blank line
                // still annotates that fence, which is the documented use.
                if (!inFence && trimmed.Length == 0)
                {
                    skipAll = false;
                    skipNames.Clear();
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
            // Projected first so the "match -> argument" mapping is one step and
            // the loop below only decides scope (all phantoms vs. one).
            var skipArgs = SkipMarker.Matches(raw)
                .Cast<Match>()
                .Select(sk => sk.Groups[1].Success ? sk.Groups[1].Value.Trim() : "");

            foreach (var arg in skipArgs)
            {
                if (arg.Length == 0) skipAll = true;
                else skipNames.Add(arg);
            }

            var applicable = surface switch
            {
                Surface.Markdown => true,
                Surface.CSharpDocComments => isDocComment,
                _ => true,
            };
            if (!applicable || skipAll) continue;

            // Blank out compiler-validated cref spans so `<see cref="Foo.Text"/>`
            // can never trip the rule. Same-length filler keeps columns stable.
            var masked = CrefSpan.Replace(raw, m => new string(' ', m.Length));

            // Outside a fence, lint only what the author put in code formatting.
            // Prose must stay immune: a case-sensitive `Text\(` still matches
            // English like "Placeholder text (for TextBoxElement etc.)", so
            // linting whole prose lines would resurrect that false-positive
            // class wholesale. Blanking everything but the inline spans keeps
            // the rule pointed at text that claims to be code.
            if (surface == Surface.Markdown && !inFence)
                masked = MaskOutsideInlineCode(masked);

            // A structural scanner needs the whole invocation, and a doc example
            // routinely wraps one across lines:
            //     Text(
            //         BuildLabel(item))
            // Per-line scanning never finds the closing paren and reports
            // nothing. Give the scanner a bounded lookahead window instead.
            // Only where every line is genuinely code — inside a fence, or on
            // an all-code surface — because joining prose lines could let an
            // unbalanced "Text (" in one sentence close against a ')' in the
            // next and manufacture a false positive.
            var scannerInput = masked;
            bool wholeLineIsCode = surface == Surface.ExampleText
                || (surface == Surface.Markdown && inFence)
                || (surface == Surface.CSharpDocComments && isDocComment);

            if (wholeLineIsCode && masked.Contains('(', StringComparison.Ordinal))
            {
                var window = new global::System.Text.StringBuilder(masked);
                for (int k = i + 1; k < lines.Length && k <= i + ScannerLookaheadLines; k++)
                {
                    var next = lines[k];
                    if (surface == Surface.Markdown &&
                        next.Trim().StartsWith("```", StringComparison.Ordinal)) break;
                    if (surface == Surface.CSharpDocComments &&
                        !next.Trim().StartsWith("///", StringComparison.Ordinal)) break;
                    window.Append('\n').Append(CrefSpan.Replace(next, m => new string(' ', m.Length)));
                }
                scannerInput = window.ToString();
            }

            foreach (var phantom in Phantoms.Where(p =>
                         !skipNames.Contains(p.Name) &&
                         p.Matches(p.Scanner is not null ? scannerInput : masked, masked.Length)))
            {
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

    /// <summary>
    /// How many following lines a structural scanner may consume to close an
    /// invocation. Bounded so a stray '(' cannot drag the scan to end of file.
    /// </summary>
    private const int ScannerLookaheadLines = 8;

    /// <summary>
    /// Blanks every character that is not inside a single-backtick inline code
    /// span, preserving length so reported columns stay meaningful.
    /// </summary>
    /// <remarks>
    /// An unpaired trailing backtick opens nothing: the span must close on the
    /// same line, matching CommonMark and keeping a lone "`" in prose inert.
    /// </remarks>
    /// <summary>
    /// Structural matcher for the <c>Text</c> phantom: an <b>unqualified</b>
    /// <c>Text</c> invoked with exactly <b>one</b> top-level argument.
    /// </summary>
    /// <remarks>
    /// <para>Two exclusions carry the whole rule.</para>
    /// <para><b>Multi-argument calls are not this phantom.</b> The real members
    /// named Text that take more than one argument — notably
    /// <c>D3Charts.Text(x, y, text)</c> — are already excluded by the
    /// unqualified check, and requiring a single top-level argument keeps a bare
    /// <c>Text(16, 16, "hi")</c> out too.</para>
    /// <para><b>English prose is not an expression.</b> A case-sensitive
    /// <c>Text\(</c> matches sentences like "Text (the element) is set
    /// separately", and a naive one-argument rule would fire on every one of
    /// them. The discriminator is that a C# expression never contains two
    /// identifier tokens separated by nothing but whitespace: <c>prefix +
    /// value</c> has an operator between them, <c>the element</c> does not.
    /// That single test admits every expression shape — ternary, concatenation,
    /// cast, call, indexer, null-coalesce — without enumerating any of them.</para>
    /// </remarks>
    internal static bool LooksLikeSingleArgTextCall(string text, int firstLineLength)
    {
        foreach (var open in TextCallOpener.Matches(text)
                     .Cast<Match>()
                     .Where(m => m.Index < firstLineLength)
                     .Select(m => m.Index + m.Length - 1))
        {
            var arg = ExtractSingleArgument(text, open);
            if (arg is null) continue;
            if (arg.Trim().Length == 0) continue;
            // `Text(...)` / `Text(…)` is the universal "arguments elided" doc
            // convention, not a call — it is the shape prose uses to *name* a
            // signature. Treating it as an invocation would fire on every
            // signature mention in the docset, including the sentences warning
            // against this very phantom.
            if (IsElidedArgument(arg)) continue;
            if (HasAdjacentIdentifiers(arg)) continue;
            return true;
        }
        return false;
    }

    private static readonly Regex TextCallOpener = new(
        @"(?<![A-Za-z0-9_.])Text\s*\(", RegexOptions.Compiled);

    /// <summary>
    /// An argument made only of ellipsis punctuation — the documentation
    /// placeholder for elided arguments, never valid C#.
    /// </summary>
    private static bool IsElidedArgument(string arg) =>
        !arg.Any(c => c != '.' && c != '\u2026' && !char.IsWhiteSpace(c));

    /// <summary>
    /// Returns the argument text when the parenthesis at <paramref name="open"/>
    /// closes on the same line and holds exactly one top-level argument;
    /// otherwise null. String and char literals are skipped so a comma or paren
    /// inside them cannot break the scan.
    /// </summary>
    private static string? ExtractSingleArgument(string line, int open)
    {
        int depth = 0;
        for (int i = open; i < line.Length; i++)
        {
            var c = line[i];

            if (c == '"' || c == '\'')
            {
                i = SkipLiteral(line, i);
                if (i < 0) return null;
                continue;
            }

            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}')
            {
                depth--;
                if (depth == 0)
                    return line.Substring(open + 1, i - open - 1);
                if (depth < 0) return null;
            }
            // A top-level comma means more than one argument.
            else if (c == ',' && depth == 1) return null;
        }
        return null;
    }

    /// <summary>
    /// Index of the literal's closing quote, or -1 if unterminated.
    /// </summary>
    /// <remarks>
    /// <!-- phantom:skip "Text" -->
    /// A backslash is deliberately <b>not</b> treated as an escape. On these
    /// surfaces a quote is spelled three ways — <c>"</c>, <c>\"</c> (example
    /// code embedded in a C# string literal, the real docking defect) and
    /// <c>""</c> (the same inside a verbatim string) — so a backslash before a
    /// quote is part of the quote's spelling, not an escape of it. Honouring it
    /// as an escape swallows the closing quote and the whole call scans as
    /// unterminated, which is exactly how <c>Text(\"Hello\")</c> slipped past.
    /// The cost is a missed detection on the rare genuinely-escaped quote
    /// inside a string; that direction is safe, a false positive is not.
    /// </remarks>
    private static int SkipLiteral(string line, int start)
    {
        var quote = line[start];
        for (int i = start + 1; i < line.Length; i++)
            if (line[i] == quote) return i;
        return -1;
    }

    /// <summary>
    /// True when the argument contains two <i>non-keyword</i> identifier tokens
    /// separated only by whitespace — the shape of English prose, never of a C#
    /// expression. Literals are collapsed first so "Save the file" inside a
    /// string cannot make a real call look like prose.
    /// </summary>
    /// <remarks>
    /// The keyword exemption is load-bearing: C# has word-shaped operators, so
    /// <c>value is null</c> and <c>value as string</c> are adjacent identifier
    /// pairs by a purely lexical reading and would be dismissed as prose. Only
    /// a pair where <i>both</i> sides are ordinary identifiers is English —
    /// "the element" has no operator between the words, an expression always
    /// does, even when that operator is spelled with letters.
    /// </remarks>
    private static bool HasAdjacentIdentifiers(string arg)
    {
        var stripped = new global::System.Text.StringBuilder();
        for (int i = 0; i < arg.Length; i++)
        {
            if (arg[i] == '"' || arg[i] == '\'')
            {
                var end = SkipLiteral(arg, i);
                if (end < 0) break;
                stripped.Append('0');       // literals stand in as a value token
                i = end;
                continue;
            }
            stripped.Append(arg[i]);
        }

        foreach (Match m in AdjacentIdentifiers.Matches(stripped.ToString()))
            if (!CSharpWordTokens.Contains(m.Groups["a"].Value) &&
                !CSharpWordTokens.Contains(m.Groups["b"].Value))
                return true;

        return false;
    }

    private static readonly Regex AdjacentIdentifiers = new(
        @"(?<a>[A-Za-z_][A-Za-z0-9_]*)\s+(?<b>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    /// <summary>
    /// C# tokens that are spelled like identifiers but are operators, literals
    /// or type names — none of which can be an English word pair.
    /// </summary>
    private static readonly global::System.Collections.Generic.HashSet<string> CSharpWordTokens =
        new(global::System.StringComparer.Ordinal)
        {
            "is", "as", "not", "and", "or", "with", "when", "switch",
            "new", "null", "true", "false", "default", "this", "base",
            "typeof", "nameof", "sizeof", "await", "ref", "out", "in",
            "string", "int", "bool", "double", "float", "decimal", "long",
            "short", "byte", "char", "object", "var", "dynamic", "uint",
            "ulong", "ushort", "sbyte", "delegate", "static", "readonly",
        };

    private static string MaskOutsideInlineCode(string line)
    {
        var buf = new char[line.Length];
        for (int i = 0; i < buf.Length; i++) buf[i] = ' ';

        int pos = 0;
        while (pos < line.Length)
        {
            var open = line.IndexOf('`', pos);
            if (open < 0) break;

            // CommonMark delimits an inline span with a RUN of backticks, and
            // closes it on a run of the same length. Matching one backtick at a
            // time mis-parses ``Optional.Of(`x`)`` — the span would be read as
            // the empty string between the first two ticks, and the phantom
            // inside would be masked away as prose.
            int openLen = 0;
            while (open + openLen < line.Length && line[open + openLen] == '`') openLen++;

            int close = -1;
            for (int i = open + openLen; i < line.Length; i++)
            {
                if (line[i] != '`') continue;
                int runLen = 0;
                while (i + runLen < line.Length && line[i + runLen] == '`') runLen++;
                if (runLen == openLen) { close = i; break; }
                i += runLen - 1;
            }

            if (close < 0) break;

            for (int i = open + openLen; i < close; i++) buf[i] = line[i];
            pos = close + openLen;
        }

        return new string(buf);
    }
}
