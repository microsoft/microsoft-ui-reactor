// Repository-content gate for the spec 044 swallowed-error audit ledger
// (docs/specs/044/swallowed-error-audit.md). Resolves issue #959.
//
// The problem these lock in: the audit's verdict-distribution table used to be
// hand-maintained prose. Its counts drifted until they were unreproducible from
// the document — `Narrow` claimed 37 against 9 verdict-bearing rows while `Keep`
// claimed 8 against 16, i.e. the same table was wrong in both directions at once.
// Three mechanisms caused it: two incompatible per-file table schemas (so a
// fixed-column reading silently dropped a whole section), rows collapsing an
// undeclared number of sites, and verdicts recorded in the sections that had no
// row in the summary table at all.
//
// The fix makes the summary a *derived* artifact: per-file rows declare their
// site count in a single canonical schema, and the table is the sum. These tests
// are what keep it derived. Every assertion below fails if its target is deleted,
// renamed, or silently emptied — including the parser itself, which is pinned by
// a non-vacuity floor so a regex that stops matching fails instead of passing.
//
// Namespace note: in Microsoft.UI.Reactor.Tests, `Microsoft.UI.System` shadows
// `System`, so any `System.`-qualified path must be written `global::System.`.
// Bare type names (Path, File, Directory) come in via ImplicitUsings and are fine.

using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

using Regex = global::System.Text.RegularExpressions.Regex;
using Ordinal = global::System.StringComparison;

namespace Microsoft.UI.Reactor.Tests.Docs;

public sealed class SwallowedErrorAuditTests
{
    const string AuditPath = "docs/specs/044/swallowed-error-audit.md";

    const string LedgerBegin = "<!-- ledger:begin -->";
    const string LedgerEnd = "<!-- ledger:end -->";
    const string DistributionBegin = "<!-- distribution:begin -->";
    const string DistributionEnd = "<!-- distribution:end -->";

    const string LedgerHeader = "| Site(s) | Sites | Verdict | Status | Notes |";
    const string DistributionHeader = "| Verdict | Sites | Shipped | Deferred |";

    const string Shipped = "shipped";
    const string Deferred = "deferred";

    // Floor, not the exact list: the vocabulary lives in the document and may
    // grow (it already grew by `TryFinally`, `Deleted` and `Trace`, which is what
    // issue #959 exposed). These eight must remain present, so "close the
    // vocabulary by deleting tokens until nothing is left over" is not a way to pass.
    static readonly string[] RequiredVocabulary =
    [
        "Keep", "Narrow", "Propagate", "TryFinally",
        "TryXxx", "PromoteEvent", "Deleted", "Trace",
    ];

    static readonly string[] RequiredKeepJustifications =
    [
        "sibling-independence", "user-callback isolation",
        "fail-safe-to-default", "framework-internal",
    ];

    // ── Assertion 1: the summary table is the sum of the ledger rows ────────
    //
    // The headline guard. If any row's Sites/Verdict/Status changes and the
    // table isn't re-derived, this fails and prints the correct table verbatim
    // so fixing it is a paste.

    [Fact]
    public void Distribution_table_is_derived_from_the_ledger_rows()
    {
        var audit = Load();
        var derived = Derive(audit.Rows);
        var published = audit.Distribution.Value;

        // A verdict must appear in both, with the same tally. Comparing
        // `GetValueOrDefault` alone is not enough: a phantom `| X | 0 | 0 | 0 |`
        // row in the summary compares equal to a verdict the ledger never used,
        // so the table could carry rows that are not derivations of anything.
        var mismatch = derived.Keys
            .Union(published.Keys)
            .Order(global::System.StringComparer.Ordinal)
            .Where(v =>
            {
                var inDerived = derived.TryGetValue(v, out var derivedTally);
                var inPublished = published.TryGetValue(v, out var publishedTally);
                return !inDerived || !inPublished || !derivedTally.Equals(publishedTally);
            })
            .ToList();

        Assert.True(
            mismatch.Count == 0,
            $"""
             The verdict-distribution table in {AuditPath} does not match the sum of
             its ledger rows. Mismatched verdicts: {string.Join(", ", mismatch)}.

             Do NOT hand-edit a cell to make this pass — replace the whole table
             between the {DistributionBegin} / {DistributionEnd} markers with:

             {RenderTable(derived)}
             """);
    }

    // ── Assertion 2: the verdict vocabulary is closed, both directions ──────
    //
    // Direction A catches a row inventing a token. Direction B catches the
    // original #959 defect: sections recorded `try/finally` and pure-trace
    // dispositions that the summary table had no row for, so those sites were
    // uncountable by construction.

    [Fact]
    public void Verdict_vocabulary_is_closed_in_both_directions()
    {
        var audit = Load();

        foreach (var required in RequiredVocabulary)
        {
            Assert.True(
                audit.Vocabulary.Contains(required),
                $"The verdict-vocabulary table in {AuditPath} no longer defines `{required}`.");
        }

        foreach (var row in audit.Rows)
        {
            Assert.True(
                audit.Vocabulary.Contains(row.Verdict),
                $"{row.Where}: verdict `{row.Verdict}` is not in the verdict-vocabulary table. "
                + $"Known tokens: {string.Join(", ", audit.Vocabulary.Order(global::System.StringComparer.Ordinal))}.");
        }

        var derived = Derive(audit.Rows);

        foreach (var (verdict, tally) in derived)
        {
            Assert.True(
                audit.Distribution.Value.ContainsKey(verdict),
                $"`{verdict}` has {tally.Sites} site(s) in the ledger but no row in the "
                + $"verdict-distribution table, so those sites are uncountable from the summary "
                + $"— the exact defect issue #959 reported. Add a row for it.");
        }

        foreach (var verdict in audit.Distribution.Value.Keys)
        {
            Assert.True(
                audit.Vocabulary.Contains(verdict),
                $"The verdict-distribution table has a row for `{verdict}`, which is not a "
                + $"token in the verdict-vocabulary table.");
        }
    }

    // ── Assertion 3: one schema, forever ───────────────────────────────────
    //
    // `ReactorWindow.cs` used to carry `| Group | Sites | Verdict | After |`
    // while every other section used `| Site | Verdict | Notes |`. Any single
    // column rule silently dropped one of them — and it was the section that
    // collapsed the most sites.

    [Fact]
    public void Every_ledger_table_uses_the_canonical_schema()
    {
        var audit = Load();

        Assert.NotEmpty(audit.Tables);

        foreach (var table in audit.Tables)
        {
            Assert.True(
                Normalize(table.Header) == Normalize(LedgerHeader),
                $"{AuditPath}:{table.LineNumber}: table under '{table.Section}' has header "
                + $"'{table.Header}' but the ledger schema is '{LedgerHeader}'. A second schema "
                + $"is what made the old counts unreproducible — qualifiers go in Notes.");
        }
    }

    // ── Assertion 4: non-vacuity floor + the cumulative ratchet ────────────
    //
    // Without this, breaking the parser turns every other assertion in this
    // file green (nothing parsed, so nothing disagrees).
    //
    // Sections and rows are *floors*, because regrouping is legitimate: two
    // rows can merge into one, or one split into two, with no change in what
    // was adjudicated.
    //
    // The site total is different — it is checked for **equality**, not `>=`.
    // The ledger is cumulative, so the total may only ever rise, and a floor
    // alone does not enforce that: once the ledger grows past a stale floor,
    // a later shrink back down to it passes silently. Equality makes every
    // change to the total a deliberate, reviewable edit in this file.
    const int LedgerSiteTotal = 136;
    const int LedgerSectionFloor = 19;
    const int LedgerRowFloor = 50;

    [Fact]
    public void Parser_sees_a_substantial_ledger()
    {
        var audit = Load();

        Assert.True(audit.Sections.Count >= LedgerSectionFloor, $"Parsed only {audit.Sections.Count} ledger sections; the floor is {LedgerSectionFloor}.");
        Assert.True(audit.Tables.Count >= LedgerSectionFloor, $"Parsed only {audit.Tables.Count} ledger tables; the floor is {LedgerSectionFloor}.");
        Assert.True(audit.Rows.Count >= LedgerRowFloor, $"Parsed only {audit.Rows.Count} ledger rows; the floor is {LedgerRowFloor}.");
        Assert.True(audit.Distribution.Value.Count >= 6, $"Parsed only {audit.Distribution.Value.Count} distribution rows.");
        Assert.True(audit.Vocabulary.Count >= 8, $"Parsed only {audit.Vocabulary.Count} vocabulary tokens.");
        Assert.True(audit.KeepJustifications.Count >= 4, $"Parsed only {audit.KeepJustifications.Count} Keep justifications.");

        var distinctVerdicts = audit.Rows.Select(r => r.Verdict).Distinct(global::System.StringComparer.Ordinal).Count();
        Assert.True(distinctVerdicts >= 4, $"Parsed only {distinctVerdicts} distinct verdicts across all rows.");

        var total = audit.Rows.Sum(r => r.Sites);

        Assert.True(
            total == LedgerSiteTotal,
            total > LedgerSiteTotal
                ? $"The ledger totals {total} sites but LedgerSiteTotal records {LedgerSiteTotal}. You adjudicated "
                  + $"{total - LedgerSiteTotal} new site(s) — raise LedgerSiteTotal to {total} in the same commit. "
                  + $"This constant is the cumulative ratchet: it exists so that a later deletion cannot quietly "
                  + $"return the ledger to today's number."
                : $"The ledger totals {total} sites, below the recorded {LedgerSiteTotal}. The ledger is cumulative "
                  + $"— a row is only ever removed if it was recorded in error. If that is genuinely what happened, "
                  + $"lower LedgerSiteTotal to {total} in the same commit and say why in the commit message.");
    }

    // ── Assertion 4b: every table sits under its own section heading ───────
    //
    // Section attribution is what makes a row's path claim (assertion 7)
    // meaningful. Without this, deleting a heading silently reattributes its
    // table to the previous file, and every other assertion still passes.

    [Fact]
    public void Every_ledger_table_has_its_own_section_heading()
    {
        var audit = Load();

        Assert.NotEmpty(audit.Tables);

        foreach (var table in audit.Tables)
        {
            Assert.True(
                table.SectionIndex is not null,
                $"{AuditPath}:{table.LineNumber}: a ledger table appears before any '### ' section heading, "
                + $"so its rows adjudicate an unnamed file.");
        }

        var duplicated = audit.Tables
            .GroupBy(t => t.SectionIndex)
            .Where(g => g.Count() > 1)
            .ToList();

        Assert.True(
            duplicated.Count == 0,
            $"These sections carry more than one ledger table: "
            + $"{string.Join("; ", duplicated.Select(g => $"'{audit.Sections[g.Key!.Value].Heading}' ({g.Count()} tables at lines {string.Join(", ", g.Select(t => t.LineNumber))})"))}. "
            + $"One table per section keeps every row attributable to the path in its heading.");

        Assert.True(
            audit.Tables.Count == audit.Sections.Count,
            $"{audit.Sections.Count} section heading(s) but {audit.Tables.Count} ledger table(s). Every section "
            + $"must carry exactly one table — a section with no table contributes no counted sites, which is "
            + $"how a file silently drops out of the ledger.");
    }

    // ── Assertion 5: Status is binary ──────────────────────────────────────

    [Fact]
    public void Every_status_cell_is_shipped_or_deferred()
    {
        foreach (var row in Load().Rows)
        {
            Assert.True(
                row.Status is Shipped or Deferred,
                $"{row.Where}: Status is '{row.Status}'; it must be '{Shipped}' or '{Deferred}'. "
                + $"A partial delivery is '{Deferred}', with the shipped part described in Notes.");
        }
    }

    // ── Assertion 6: Sites is a declared positive integer ──────────────────
    //
    // The collapse factor used to hide in prose ("(6 sites)", "×2 collapsed
    // into 1", "5 pure-trace"), so summing rows and summing sites gave
    // different answers with no way to tell which the table meant.

    [Fact]
    public void Every_sites_cell_is_a_positive_integer()
    {
        var audit = Load();

        foreach (var row in audit.Rows)
        {
            Assert.True(row.Sites > 0, $"{row.Where}: Sites is {row.Sites}; it must be a positive integer.");
        }

        // Rows whose cells failed to parse never make it into Rows, so assert
        // none were dropped — and name them. A bare count would tell a
        // contributor that something is wrong without telling them where.
        Assert.True(
            audit.SkippedRows.Count == 0,
            $"{audit.SkippedRows.Count} ledger row(s) could not be parsed and were therefore not counted:\n"
            + string.Join("\n", audit.SkippedRows.Select(s => $"  {AuditPath}:{s.LineNumber}  {s.Text}"))
            + $"\n\nEvery row must be countable. The expected shape is exactly five cells:\n"
            + $"  | <site description> | <positive integer> | `<Verdict>` | {Shipped}|{Deferred} | <notes> |\n"
            + $"Write a literal pipe inside a cell as \\| so it is not read as a column break.");
    }

    // ── Assertion 7: section headings name files that exist ────────────────
    //
    // Five of sixteen headings named nonexistent paths when this gate was
    // written (three Persistence files were under Hosting/ not Core/, one was
    // in Reactor.Advanced, and one file had been deleted from the repo
    // outright). `(retired` is the single, explicit escape hatch, so a silently
    // stale path still fails.

    [Fact]
    public void Every_section_heading_names_an_existing_file_or_is_marked_retired()
    {
        foreach (var section in Load().Sections)
        {
            if (section.Heading.Contains("(retired", Ordinal.Ordinal))
            {
                continue;
            }

            var full = RepoPath(section.RelativePath, $"{AuditPath}:{section.LineNumber}: the section path");
            Assert.True(
                File.Exists(full),
                $"{AuditPath}:{section.LineNumber}: section names '{section.RelativePath}', which does not "
                + $"exist. Correct the path, or — if the file was deleted — add a '(retired: …)' marker to "
                + $"the heading so the entry stays counted under the cumulative-ledger definition.");
        }
    }

    // ── Assertion 8: every Keep row names which Keep justification applies ──
    //
    // `Keep` was defined three incompatible ways (the table label said
    // "iteration sibling-independence", the Method section said every Keep entry
    // shared one justification, and the second-pass prose added another). The
    // document names four, three of which rows currently use — `framework-internal`
    // is reserved for the §6.3 carve-out and no row uses it today. This fact
    // requires all four to stay *defined* (so the vocabulary can't be closed by
    // deletion) but only requires each row to open with one of them, so the
    // reserved tag going unused is not a failure. Tagging each row is what makes
    // the aggregate `Keep` row in the summary honest.

    [Fact]
    public void Every_keep_row_opens_its_notes_with_a_named_justification()
    {
        var audit = Load();

        foreach (var required in RequiredKeepJustifications)
        {
            Assert.True(
                audit.KeepJustifications.Contains(required),
                $"The `Keep` justifications table in {AuditPath} no longer defines '{required}'.");
        }

        var keepRows = audit.Rows.Where(r => r.Verdict == "Keep").ToList();
        Assert.NotEmpty(keepRows);

        foreach (var row in keepRows)
        {
            var tag = LeadingBoldTag(row.Notes);

            Assert.True(
                tag is not null && audit.KeepJustifications.Contains(tag),
                $"{row.Where}: a `Keep` row's Notes must open with one of the named justifications "
                + $"in bold ({string.Join(", ", audit.KeepJustifications.Order(global::System.StringComparer.Ordinal))}); "
                + $"found {(tag is null ? "no leading bold tag" : $"'{tag}'")}.");
        }
    }

    // ── Assertion 9: the live figure quoted in prose tracks the derivation ──
    //
    // Numbers restated in sentences are how the table drifted in the first
    // place. This is the only live figure quoted outside the table; historical
    // snapshots are quarantined under their own heading and deliberately not
    // checked.
    //
    // Uniqueness is enforced for the same reason as the ledger markers in
    // assertion 10: matching only the first occurrence would validate one copy
    // and let every later one drift, which is precisely the failure this file
    // exists to prevent. A second copy is also indistinguishable from a live
    // figure to a reader, so it cannot be waved through as harmless prose.

    [Fact]
    public void Worry_threshold_sentence_quotes_the_derived_propagate_total()
    {
        var audit = Load();
        var derived = Derive(audit.Rows);

        var matches = Regex.Matches(
            audit.Text,
            @"worry-threshold for `Propagate` is (?<threshold>\d+); we're at (?<actual>\d+)\.");

        Assert.True(matches.Count > 0, $"The §6.7.4 worry-threshold sentence is missing from {AuditPath}.");

        Assert.True(
            matches.Count == 1,
            $"The §6.7.4 worry-threshold sentence appears {matches.Count} times in {AuditPath}. Quote "
            + "the live figure exactly once: this check would otherwise validate the first copy and "
            + "leave the others to drift, which is the defect this gate exists to prevent. If you "
            + "meant to record a past value, move it under `### Historical snapshots (do not update)` "
            + "and reword it so it no longer reads as the current figure.");

        var match = matches[0];

        var actual = int.Parse(match.Groups["actual"].Value);
        var threshold = int.Parse(match.Groups["threshold"].Value);
        var propagate = derived.GetValueOrDefault("Propagate").Sites;

        Assert.True(
            actual == propagate,
            $"The worry-threshold sentence says we're at {actual} `Propagate` sites, but the ledger "
            + $"derives {propagate}.");

        Assert.True(
            propagate < threshold,
            $"`Propagate` is at {propagate}, at or over the spec §6.7.4 worry threshold of {threshold}. "
            + $"That is a design signal, not a doc bug — re-read §6.7.4 before changing this test.");
    }

    // ── Assertion 10: the machine-readable markers are load-bearing ─────────
    //
    // Deleting a marker zeroes the parse; *duplicating* one is worse, because
    // `Region()` takes the first occurrence — an early stray `ledger:end`
    // silently truncates the ledger, and re-deriving the summary against the
    // truncated set makes every other assertion agree with itself. So this
    // checks whole-line uniqueness and ordering, not mere presence. (Presence
    // alone would also be vacuous: the Derivation prose names both ledger
    // markers inline.) The match trims, so surrounding whitespace does not
    // create a second marker or hide one — only a line whose entire content is
    // the marker counts. That mirrors `Region()`, which trims the same way.

    [Fact]
    public void Region_markers_are_unique_whole_lines_in_the_right_order()
    {
        var lines = File.ReadAllLines(RepoPath(AuditPath, "The audit path"));

        var at = new Dictionary<string, List<int>>(global::System.StringComparer.Ordinal);

        foreach (var marker in new[] { DistributionBegin, DistributionEnd, LedgerBegin, LedgerEnd })
        {
            at[marker] = Enumerable.Range(0, lines.Length).Where(i => lines[i].Trim() == marker).ToList();

            Assert.True(
                at[marker].Count == 1,
                $"{AuditPath} contains {at[marker].Count} line(s) whose entire content — ignoring leading "
                + $"and trailing whitespace — is '{marker}' "
                + $"(lines {string.Join(", ", at[marker].Select(i => i + 1))}); it must contain exactly one. "
                + $"The markers delimit what the derivation counts — a duplicate silently truncates or "
                + $"extends the counted region, and a missing one zeroes it.");
        }

        Assert.True(
            at[DistributionBegin][0] < at[DistributionEnd][0],
            $"'{DistributionBegin}' must precede '{DistributionEnd}'.");
        Assert.True(
            at[LedgerBegin][0] < at[LedgerEnd][0],
            $"'{LedgerBegin}' must precede '{LedgerEnd}'.");
        Assert.True(
            at[DistributionEnd][0] < at[LedgerBegin][0],
            $"The distribution region must close before the ledger region opens; overlapping regions "
            + $"would let the summary table count itself.");
    }

    // ── Parsing ────────────────────────────────────────────────────────────

    readonly record struct Tally(int Sites, int ShippedSites, int DeferredSites);

    sealed record Row(string Where, string Site, int Sites, string Verdict, string Status, string Notes);

    sealed record Section(string Heading, string RelativePath, int LineNumber);

    sealed record Table(string Section, int? SectionIndex, string Header, int LineNumber);

    sealed record SkippedRow(int LineNumber, string Text);

    sealed record Audit(
        string Text,
        IReadOnlyList<Row> Rows,
        IReadOnlyList<SkippedRow> SkippedRows,
        IReadOnlyList<Section> Sections,
        IReadOnlyList<Table> Tables,
        Lazy<IReadOnlyDictionary<string, Tally>> Distribution,
        IReadOnlySet<string> Vocabulary,
        IReadOnlySet<string> KeepJustifications);

    static string RepoRoot()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.True(root is not null, "Could not locate the repository root from the test assembly directory.");
        return root!;
    }

    /// <summary>
    /// Anchors a documented path under the repository root.
    /// </summary>
    /// <remarks>
    /// Every path this gate resolves comes out of the audit document, so it is
    /// author-controlled, and two shapes escape the tree: an absolute path, and
    /// a relative path that climbs out with <c>..</c> segments. Either would let
    /// a heading satisfy the existence check by naming an unrelated file
    /// elsewhere on the machine. Both are rejected here rather than followed.
    /// </remarks>
    static string RepoPath(string relative, string what)
    {
        var root = Path.GetFullPath(RepoRoot());
        var native = relative.Replace('/', Path.DirectorySeparatorChar);

        Assert.False(
            Path.IsPathRooted(native),
            $"{what} is '{relative}', which is an absolute path. Paths in the audit are relative to the "
            + $"repository root — an absolute one would resolve outside the tree and could satisfy the "
            + $"existence check by naming an unrelated file.");

        // Resolving against an explicit base states the intent in one call. The
        // Path.Combine spelling of this reads as if the root is always part of
        // the answer when it is not: Combine returns only its last argument once
        // that argument is rooted, so the root would drop out silently. The
        // assertion above is what closes that case; not writing Combine at all
        // keeps a future edit from reintroducing it by moving the assertion.
        var full = Path.GetFullPath(native, root);

        // Path.GetRelativePath applies the platform's own path-comparison rules,
        // so the containment check stays correct without this file having to
        // pick a StringComparison. A path that escapes the root comes back
        // either rooted (no relative path exists) or leading with "..".
        var relativeToRoot = Path.GetRelativePath(root, full);

        Assert.False(
            Path.IsPathRooted(relativeToRoot)
            || relativeToRoot == ".."
            || relativeToRoot.StartsWith(".." + Path.DirectorySeparatorChar, Ordinal.Ordinal),
            $"{what} is '{relative}', which resolves to '{full}' — outside the repository root '{root}'. "
            + $"Paths in the audit must stay inside the tree; a '..' segment that climbs out could satisfy "
            + $"the existence check by naming an unrelated file.");

        return full;
    }

    static Audit Load()
    {
        var path = RepoPath(AuditPath, "The audit path");
        Assert.True(File.Exists(path), $"Expected the audit file at '{path}'.");

        var text = File.ReadAllText(path);
        var lines = File.ReadAllLines(path);

        var (ledgerFrom, ledgerTo) = Region(lines, LedgerBegin, LedgerEnd);
        var (distFrom, distTo) = Region(lines, DistributionBegin, DistributionEnd);

        var sections = new List<Section>();
        var tables = new List<Table>();
        var rows = new List<Row>();
        var skipped = new List<SkippedRow>();
        var section = "(before any section heading)";
        int? sectionIndex = null;

        for (var i = ledgerFrom; i < ledgerTo; i++)
        {
            var line = lines[i];

            if (line.StartsWith("### ", Ordinal.Ordinal))
            {
                var heading = line[4..].Trim();
                section = heading;
                sections.Add(new Section(heading, BacktickedPath(heading), i + 1));
                sectionIndex = sections.Count - 1;
                continue;
            }

            if (!IsTableHeader(lines, i))
            {
                continue;
            }

            var dataFrom = i + 2;
            var dataTo = dataFrom;
            while (dataTo < ledgerTo && lines[dataTo].TrimStart().StartsWith('|'))
            {
                dataTo++;
            }

            tables.Add(new Table(section, sectionIndex, line.Trim(), i + 1));

            for (var j = dataFrom; j < dataTo; j++)
            {
                if (TryParseRow(lines[j], section, j + 1, out var row))
                {
                    rows.Add(row);
                }
                else
                {
                    skipped.Add(new SkippedRow(j + 1, lines[j].Trim()));
                }
            }

            i = dataTo - 1;
        }

        return new Audit(
            text,
            rows,
            skipped,
            sections,
            tables,

            // Lazy so that a malformed summary table fails only the facts that
            // actually read it, instead of taking down every fact with a
            // message about a table they don't examine.
            new Lazy<IReadOnlyDictionary<string, Tally>>(() => ParseDistribution(lines, distFrom, distTo)),
            ParseFirstColumnTokens(lines, "## Verdict vocabulary", "| Token | Meaning |"),
            ParseFirstColumnTokens(lines, "### `Keep` justifications", "| Tag | Meaning |"));
    }

    static (int From, int To) Region(string[] lines, string begin, string end)
    {
        var from = global::System.Array.FindIndex(lines, l => l.Trim() == begin);
        var to = global::System.Array.FindIndex(lines, l => l.Trim() == end);

        Assert.True(from >= 0, $"{AuditPath} is missing the '{begin}' marker.");
        Assert.True(to > from, $"{AuditPath} is missing the '{end}' marker, or it precedes '{begin}'.");

        return (from + 1, to);
    }

    // A markdown table header is a `|`-row immediately followed by a delimiter
    // row of the same width. Keying off structure rather than off the expected
    // header text is what lets the schema assertion *see* a non-canonical table
    // instead of skipping it; matching widths stops `|---|` from certifying a
    // five-column header.
    static bool IsTableHeader(string[] lines, int i)
    {
        if (i + 1 >= lines.Length)
        {
            return false;
        }

        var header = Cells(lines[i]);
        if (header is null || header.Length < 2)
        {
            return false;
        }

        var next = lines[i + 1].Trim();
        if (!Regex.IsMatch(next, @"^\|[\s:|-]*-[\s:|-]*\|$"))
        {
            return false;
        }

        var delimiter = Cells(next);
        return delimiter is not null && delimiter.Length == header.Length;
    }

    static bool TryParseRow(string line, string section, int lineNumber, out Row row)
    {
        row = null!;

        var cells = Cells(line);
        if (cells is null || cells.Length != 5)
        {
            return false;
        }

        // Parse sign-agnostically and let the Fact judge positivity — rejecting
        // `0` or `-3` here would drop the row and make that assertion
        // unfalsifiable.
        if (!int.TryParse(cells[1], out var sites))
        {
            return false;
        }

        row = new Row(
            $"{AuditPath}:{lineNumber} ({section})",
            cells[0],
            sites,
            cells[2].Trim('`'),
            cells[3],
            cells[4]);

        return true;
    }

    // Splits a markdown table row on unescaped `|`. A literal pipe in a cell
    // must be written `\|`, which is what markdown requires anyway — folding
    // surplus columns into the last cell instead would let a row grow a sixth
    // column silently, and a stray pipe in an early cell would shift every
    // column right while still looking well-formed.
    static string[]? Cells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 2 || !trimmed.StartsWith('|') || !trimmed.EndsWith('|'))
        {
            return null;
        }

        return Regex.Split(trimmed[1..^1], @"(?<!\\)\|")
            .Select(p => p.Replace(@"\|", "|").Trim())
            .ToArray();
    }

    static Dictionary<string, Tally> ParseDistribution(string[] lines, int from, int to)
    {
        var result = new Dictionary<string, Tally>(global::System.StringComparer.Ordinal);

        var headerAt = Enumerable.Range(from, to - from)
            .Where(i => IsTableHeader(lines, i))
            .ToList();

        Assert.True(
            headerAt.Count == 1,
            $"The distribution region must contain exactly one table; found {headerAt.Count}.");

        var h = headerAt[0];
        Assert.True(
            Normalize(lines[h]) == Normalize(DistributionHeader),
            $"{AuditPath}:{h + 1}: the verdict-distribution table's header is '{lines[h].Trim()}' but must be "
            + $"exactly '{DistributionHeader}'.");

        // Every `|`-line after the delimiter must be a well-formed data row.
        // Skipping unparseable lines would let a contradictory row sit in the
        // published table while the parse stayed correct.
        for (var i = h + 2; i < to && lines[i].TrimStart().StartsWith('|'); i++)
        {
            var cells = Cells(lines[i]);

            Assert.True(
                cells is { Length: 4 },
                $"{AuditPath}:{i + 1}: distribution row has {cells?.Length.ToString() ?? "malformed"} cell(s); "
                + $"the table is four columns.");

            var verdict = cells![0].Trim('`');

            // Parsed before the assertion, not inside it: `&&` short-circuits, so
            // a non-integer in the first cell would leave the later out-variables
            // unassigned and the compiler rejects reading them afterwards. Three
            // separate calls keep all three assigned whatever fails.
            var okSites = int.TryParse(cells[1], out var sites);
            var okShipped = int.TryParse(cells[2], out var shipped);
            var okDeferred = int.TryParse(cells[3], out var deferred);

            Assert.True(
                okSites && okShipped && okDeferred,
                $"{AuditPath}:{i + 1}: distribution row for '{verdict}' has non-integer counts.");

            Assert.True(
                result.TryAdd(verdict, new Tally(sites, shipped, deferred)),
                $"{AuditPath}:{i + 1}: '{verdict}' already has a row in the distribution table. Duplicate rows "
                + $"make the published table self-contradictory while still parsing.");
        }

        Assert.NotEmpty(result);

        return result;
    }

    static HashSet<string> ParseFirstColumnTokens(string[] lines, string heading, string expectedHeader)
    {
        var start = global::System.Array.FindIndex(lines, l => l.Trim() == heading);
        Assert.True(start >= 0, $"{AuditPath} is missing the '{heading}' heading.");

        var result = new HashSet<string>(global::System.StringComparer.Ordinal);
        var found = false;

        for (var i = start + 1; i < lines.Length; i++)
        {
            // Stop at the next heading of the same or higher level.
            if (lines[i].StartsWith("## ", Ordinal.Ordinal)
                || (heading.StartsWith("###", Ordinal.Ordinal) && lines[i].StartsWith("### ", Ordinal.Ordinal)))
            {
                break;
            }

            // Match the table by its header rather than taking whichever table
            // comes first. These two tables define the closed vocabularies the
            // gate validates against, so reading the wrong one silently swaps
            // the standard rather than failing — an example or auxiliary table
            // added above would quietly become the vocabulary.
            if (!IsTableHeader(lines, i) || Normalize(lines[i]) != Normalize(expectedHeader))
            {
                continue;
            }

            found = true;

            for (var j = i + 2; j < lines.Length && lines[j].TrimStart().StartsWith('|'); j++)
            {
                var cells = Cells(lines[j]);
                if (cells is { Length: >= 2 })
                {
                    result.Add(cells[0].Trim('`').Trim('*').Trim());
                }
            }

            break;
        }

        Assert.True(
            found,
            $"{AuditPath}: the '{heading}' section has no table headed '{expectedHeader}'. That table is the "
            + $"vocabulary this gate validates against; if it was renamed or restructured, update the expected "
            + $"header here in the same change so the gate keeps reading the intended token set.");

        return result;
    }

    static string BacktickedPath(string heading)
    {
        var match = Regex.Match(heading, "`(?<path>[^`]+)`");
        return match.Success ? match.Groups["path"].Value : heading;
    }

    static string? LeadingBoldTag(string notes)
    {
        var match = Regex.Match(notes.Trim(), @"^\*\*(?<tag>[^*]+)\*\*");
        return match.Success ? match.Groups["tag"].Value.TrimEnd('.', ' ') : null;
    }

    static Dictionary<string, Tally> Derive(IReadOnlyList<Row> rows)
    {
        var result = new Dictionary<string, Tally>(global::System.StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var current = result.GetValueOrDefault(row.Verdict);
            result[row.Verdict] = new Tally(
                current.Sites + row.Sites,
                current.ShippedSites + (row.Status == Shipped ? row.Sites : 0),
                current.DeferredSites + (row.Status == Deferred ? row.Sites : 0));
        }

        return result;
    }

    // Ordered to match the vocabulary table's reading order so the printed fix
    // is a drop-in replacement, not a reshuffle. Iterating the tallies directly
    // means presence needs no filtering step at all: a required verdict the
    // ledger never used simply is not in `derived`, so it contributes no row.
    static string RenderTable(IReadOnlyDictionary<string, Tally> derived)
    {
        var rank = RequiredVocabulary
            .Select((token, index) => (token, index))
            .ToDictionary(x => x.token, x => x.index, global::System.StringComparer.Ordinal);

        var body = derived
            .OrderBy(kv => rank.TryGetValue(kv.Key, out var i) ? i : int.MaxValue)
            .ThenBy(kv => kv.Key, global::System.StringComparer.Ordinal)
            .Select(kv => $"| `{kv.Key}` | {kv.Value.Sites} | {kv.Value.ShippedSites} | {kv.Value.DeferredSites} |");

        return string.Join(
            global::System.Environment.NewLine,
            [DistributionHeader, "|---|---|---|---|", .. body]);
    }

    static string Normalize(string line) =>
        Regex.Replace(line.Trim(), @"\s+", " ");
}
