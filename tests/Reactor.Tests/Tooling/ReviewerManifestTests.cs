using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// Gate on <c>tools/reviewer/manifest.json</c>: every path it lists must resolve to a real file.
/// </summary>
/// <remarks>
/// <para>
/// The reviewer never opens these files itself — <c>run-review.ps1</c> interpolates the list into
/// an agent prompt and asks the agent to read them — so a path that resolves to nothing is
/// silently unreviewed code rather than a crash. Nothing fails, and the batch still reports the
/// file as reviewed.
/// </para>
/// <para>
/// It rots continuously, from ordinary file movement between the rare occasions anyone edits the
/// manifest, not from any single bad commit. When this gate was added 147 of 633 entries pointed
/// at nothing and the reporting counted all 633 as reviewed; that had gone unnoticed for months.
/// A one-time path fix would not have prevented the next drift, which is the whole reason this
/// test exists.
/// </para>
/// <para>
/// <c>run-review.ps1</c> deliberately does not fail fast on a stale path — aborting a 91-batch LLM
/// run over one bad entry would trade a reporting defect for an availability one. Blocking is
/// cheap here and expensive there, so the strict check lives in CI and the run reports a shortfall.
/// </para>
/// </remarks>
public class ReviewerManifestTests
{
    const string ManifestRelativePath = "tools/reviewer/manifest.json";

    readonly record struct Entry(string BatchId, string Path);

    static string ManifestPath() => global::System.IO.Path.Join(GallerySources.RepoRoot(), "tools", "reviewer", "manifest.json");

    static IReadOnlyList<(string Id, string[] Files)> Batches()
    {
        var path = ManifestPath();
        Assert.True(File.Exists(path), $"reviewer manifest not found at {path}");

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(
            doc.RootElement.TryGetProperty("batches", out var batches),
            $"{ManifestRelativePath} has no 'batches' property — the manifest shape changed and this gate needs updating.");
        // Checked explicitly so a shape change reports this message rather than an
        // InvalidOperationException from EnumerateArray().
        Assert.True(
            batches.ValueKind == JsonValueKind.Array,
            $"{ManifestRelativePath} has a 'batches' property of kind {batches.ValueKind}, expected an array — the manifest shape changed and this gate needs updating.");

        return batches.EnumerateArray()
            .Select(b => (
                Id: b.GetProperty("id").GetString()!,
                Files: b.GetProperty("files").EnumerateArray().Select(f => f.GetString()!).ToArray()))
            .ToList();
    }

    static IReadOnlyList<Entry> Entries() =>
        Batches().SelectMany(b => b.Files.Select(f => new Entry(b.Id, f))).ToList();

    /// <summary>
    /// Guards the other tests in this class against passing vacuously. Each one asserts "no
    /// offenders in this collection", which an empty collection satisfies for free — so if the
    /// manifest ever fails to load, or its shape changes such that no entries are read, the
    /// coverage checks would go green while checking nothing at all.
    /// </summary>
    [Fact]
    public void ManifestLoadsAndIsNonTrivial()
    {
        var batches = Batches();
        var entryCount = batches.Sum(b => b.Files.Length);

        Assert.True(batches.Count >= 50, $"expected the reviewer manifest to define many batches, found {batches.Count}");
        Assert.True(entryCount >= 400, $"expected the reviewer manifest to list many entries, found {entryCount}");
    }

    [Fact]
    public void EveryManifestPathResolvesFromRepoRoot()
    {
        var root = GallerySources.RepoRoot();

        var missing = Entries()
            .Where(e => !File.Exists(global::System.IO.Path.Join(root, e.Path)))
            .ToList();

        Assert.True(missing.Count == 0, BuildMissingMessage(missing));
    }

    static string BuildMissingMessage(IReadOnlyList<Entry> missing)
    {
        var lines = missing.Select(m => $"  {m.BatchId}: {m.Path}");
        return $"""
            {missing.Count} path(s) in {ManifestRelativePath} do not resolve to a file in the repo.

            The reviewer hands these to an agent that cannot open them, and still counts them as
            reviewed — so this is unreviewed code, not just a bad path. Retarget each one to where
            the file lives now, or drop it if the file is genuinely gone.

            Paths are relative to the repo root (note: NOT to src/).

            {string.Join("\n", lines)}
            """;
    }

    [Fact]
    public void NoBatchIsEmpty()
    {
        var empty = Batches().Where(b => b.Files.Length == 0).Select(b => b.Id).ToList();

        Assert.True(
            empty.Count == 0,
            $"""
            {empty.Count} batch(es) in {ManifestRelativePath} list no files: {string.Join(", ", empty)}

            An empty batch still spawns an agent and still produces a report, so it burns a run and
            reports zero findings as though the code were clean. Give the batch files or remove it.
            """);
    }

    [Fact]
    public void NoBatchListsTheSamePathTwice()
    {
        var dupes = Batches()
            .SelectMany(b => b.Files
                .GroupBy(f => f)
                .Where(g => g.Count() > 1)
                .Select(g => $"  {b.Id}: {g.Key} (x{g.Count()})"))
            .ToList();

        Assert.True(
            dupes.Count == 0,
            $"""
            {ManifestRelativePath} lists the same path more than once within a batch:

            {string.Join("\n", dupes)}

            A duplicate inflates the reviewed count and asks the agent to read the same file twice.
            (The same path appearing in *different* batches is intentional — several agents review
            the same file from different angles.)
            """);
    }

    /// <summary>
    /// Keeps the single-convention invariant enforceable. The manifest previously mixed
    /// repo-root-relative and <c>src/</c>-relative paths, which made any single-base audit report
    /// hundreds of false misses, and would force this gate into a two-base resolver that silently
    /// accepts a wrong path whenever it happens to resolve under the other base.
    /// </summary>
    /// <remarks>
    /// Rooted paths are rejected here for the sake of the diagnosis, not because they can escape
    /// the repo root. This gate composes with <c>Path.Join</c>, which concatenates rather than
    /// letting a rooted second argument win, so <c>C:/x.cs</c> already fails <c>File.Exists</c>
    /// and <see cref="EveryManifestPathResolvesFromRepoRoot"/> catches it. <c>Path.Combine</c>
    /// would let it win — this gate deliberately does not use it, so don't "harden" it by
    /// switching. Rejecting rooted paths here only means the failure names the real defect, a
    /// malformed path, instead of reporting a missing file.
    /// </remarks>
    [Fact]
    public void PathsUseRepoRootRelativeForwardSlashForm()
    {
        var malformed = Entries()
            .Where(e => e.Path.Contains('\\')
                     || global::System.IO.Path.IsPathRooted(e.Path)
                     || e.Path.StartsWith("./")
                     || HasTraversalSegment(e.Path))
            .Select(e => $"  {e.BatchId}: {e.Path}")
            .ToList();

        Assert.True(
            malformed.Count == 0,
            $"""
            {ManifestRelativePath} contains path(s) that are not plain repo-root-relative,
            forward-slash form:

            {string.Join("\n", malformed)}
            """);
    }

    /// <summary>
    /// True when <c>..</c> appears as a whole path segment, i.e. actual parent-directory
    /// traversal. Deliberately not a substring test: a file legitimately named
    /// <c>foo..bar.cs</c> is not traversal, and flagging it would be a false positive
    /// against a rule whose entire purpose is to keep paths anchored to the repo root.
    /// </summary>
    static bool HasTraversalSegment(string path) =>
        path.Split('/').Any(segment => segment == "..");
}
