// Shared by the selftest hosts (Reactor.AppTests.Host, Reactor.PackagedTests.Host) and their
// MSTest wrappers (Reactor.SelfTests, Reactor.PackagedTests). The hosts partition the corpus with
// it; the wrappers parse the same spec and check the partition the host actually produced. One
// parser means the two sides cannot disagree about what "1/2" selects.
using System.Globalization;

namespace Reactor.Tests.Shared;

/// <summary>
/// One slice of the selftest corpus, chosen with <c>--shard &lt;k&gt;/&lt;n&gt;</c> so CI can split
/// the suite across runners.
/// </summary>
/// <remarks>
/// <para><b>A partition by construction.</b> Fixtures that are not pinned are dealt round-robin in
/// corpus order: the i-th goes to shard <c>(i mod n) + 1</c>. Every fixture lands in exactly one
/// shard, and a newly registered fixture is picked up without anyone editing a shard list. Round
/// robin stays balanced because fixture cost is spread fairly evenly through the registry. On the
/// AOT corpus, measured per-fixture times gave two shards of 175 s against a 168 s ideal.</para>
/// <para><b>Pinned fixtures run in every shard.</b> They are the harness's own controls, whose
/// presence the wrappers assert on every run, so a shard without them would fail for lack of a
/// control rather than because of anything it ran. See <see cref="PinnedFixtures"/>.</para>
/// <para>The host selects the shard <i>before</i> <c>--filter</c> narrows it, so a fixture's shard
/// never depends on what else was filtered in.</para>
/// </remarks>
internal readonly record struct SelfTestShard(int Index, int Count)
{
    /// <summary>The host's command-line flag.</summary>
    public const string Flag = "--shard";

    /// <summary>
    /// Environment variable the wrappers read, so CI can shard <c>dotnet test</c> without passing
    /// arguments through the test platform.
    /// </summary>
    public const string EnvVar = "REACTOR_SELFTEST_SHARD";

    /// <summary>
    /// Upper bound on <see cref="Count"/>. The corpus has ~1,600 fixtures, so this still leaves
    /// dozens per shard; anything larger is almost certainly a typo, not a plan.
    /// </summary>
    public const int MaxCount = 64;

    /// <summary>
    /// Prefix of the comment the host prints for a sharded run and a sharded listing:
    /// <c># Shard &lt;k&gt;/&lt;n&gt;: &lt;m&gt; of &lt;total&gt; fixtures</c>.
    /// </summary>
    public const string Marker = "# Shard ";

    /// <summary>
    /// Fixtures that run in every shard.
    /// </summary>
    /// <remarks>
    /// <c>SelfTestVerdict_OnlySkips_PositiveControl</c> exists to be fully skipped, and both
    /// <c>SelfTestBatch.SkippedFixtures_AreReported</c> and
    /// <c>SelfTestBatch.SkipDirectives_SurviveIntoTheReport</c> require it in every run.
    /// <c>Packaged_IdentityGuard</c> is what establishes that a packaged run had identity at all.
    /// Each costs milliseconds. A pin missing from a tier's corpus is simply not selected, which is
    /// why the guard can be listed even though the unpackaged tier never runs it. Literals rather
    /// than references because the wrappers cannot see the host's fixture types; the host checks
    /// them against its registry on every run (<see cref="ValidatePinnedFixtures"/>).
    /// </remarks>
    public static readonly string[] PinnedFixtures =
    [
        "SelfTestVerdict_OnlySkips_PositiveControl",
        "Packaged_IdentityGuard",
    ];

    public static bool IsPinned(string fixture) => Array.IndexOf(PinnedFixtures, fixture) >= 0;

    /// <summary>
    /// Parses <c>&lt;k&gt;/&lt;n&gt;</c> with <c>1 &lt;= k &lt;= n &lt;= <see cref="MaxCount"/></c>.
    /// </summary>
    /// <remarks>
    /// Strict on purpose. A spec that failed open would run the whole suite or none of it and
    /// still look like a shard, which is the silent coverage loss sharding must not introduce.
    /// </remarks>
    public static bool TryParse(string? spec, out SelfTestShard shard, out string error)
    {
        shard = default;
        if (string.IsNullOrWhiteSpace(spec))
        {
            error = "expected <index>/<count>, e.g. 1/2, but no value was given.";
            return false;
        }

        var parts = spec.Trim().Split('/');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            error = $"expected <index>/<count>, e.g. 1/2, but got '{spec}'.";
            return false;
        }

        if (count < 1 || count > MaxCount)
        {
            error = $"shard count must be between 1 and {MaxCount}, but got {count} in '{spec}'.";
            return false;
        }

        if (index < 1 || index > count)
        {
            error = $"shard index must be between 1 and {count}, but got {index} in '{spec}'.";
            return false;
        }

        shard = new SelfTestShard(index, count);
        error = "";
        return true;
    }

    /// <summary>This shard's slice of <paramref name="corpus"/>, in corpus order.</summary>
    public string[] Select(IReadOnlyList<string> corpus)
    {
        var selected = new List<string>();
        var dealt = 0;
        foreach (var fixture in corpus)
        {
            if (IsPinned(fixture))
            {
                selected.Add(fixture);
                continue;
            }

            if (dealt++ % Count == Index - 1)
                selected.Add(fixture);
        }

        return selected.ToArray();
    }

    /// <summary>
    /// Throws when a pinned name no longer matches a registered fixture.
    /// </summary>
    /// <remarks>
    /// Same shape and reasoning as <c>SelfTestRunner.ValidateDefaultSkipPatterns</c>: a stale entry
    /// pins nothing, and nothing else notices. The renamed control would be dealt to one shard, and
    /// every other shard would fail for its absence, loudly but naming the wrong cause. Checked
    /// against the full registry, not a tier's slice, because a name one tier does not run is still
    /// a valid pin.
    /// </remarks>
    public static void ValidatePinnedFixtures(IReadOnlyCollection<string> allFixtures)
    {
        var stale = PinnedFixtures.Where(p => !allFixtures.Contains(p)).ToArray();
        if (stale.Length == 0) return;

        throw new InvalidOperationException(
            $"STALE_SHARD_PIN: {string.Join(", ", stale)} {(stale.Length == 1 ? "is" : "are")} " +
            "pinned to every selftest shard but no longer registered. A renamed control would be " +
            "dealt to a single shard, and every other shard would fail for its absence. Update " +
            "SelfTestShard.PinnedFixtures to the fixture's current name.");
    }

    /// <summary>
    /// Checks that <paramref name="shards"/> (the host's <c>--list-fixtures --shard k/n</c> output
    /// for every k) partition <paramref name="corpus"/> (its unsharded output). Empty means healthy.
    /// </summary>
    /// <remarks>
    /// <para>This is a property check on what the host printed, not a recomputation of the
    /// partition, so it stays meaningful even though the host and this method share a file. It
    /// fails if a fixture is dropped, duplicated, or invented, if a pin is missing from a shard, or
    /// if a shard selects nothing beyond the pins.</para>
    /// <para>The last rule matters. A shard of pins alone would pass every per-fixture test, and a
    /// runner-count mistake would then look like a green run.</para>
    /// </remarks>
    public static IReadOnlyList<string> FindPartitionProblems(
        IReadOnlyList<string> corpus, IReadOnlyList<IReadOnlyList<string>> shards)
    {
        var problems = new List<string>();
        if (shards.Count == 0)
        {
            problems.Add("no shards were listed.");
            return problems;
        }

        var inCorpus = new HashSet<string>(corpus, StringComparer.Ordinal);
        var pins = PinnedFixtures.Where(inCorpus.Contains).ToHashSet(StringComparer.Ordinal);
        var owners = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (var k = 0; k < shards.Count; k++)
        {
            var label = $"{k + 1}/{shards.Count}";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var fixture in shards[k])
            {
                if (!seen.Add(fixture))
                    problems.Add($"shard {label} lists '{fixture}' more than once.");
                if (!inCorpus.Contains(fixture))
                    problems.Add($"shard {label} lists '{fixture}', which is not in the unsharded corpus.");
                if (!owners.TryGetValue(fixture, out var list))
                    owners[fixture] = list = [];
                list.Add(k + 1);
            }

            foreach (var pin in pins.Where(p => !seen.Contains(p)))
                problems.Add($"shard {label} is missing pinned fixture '{pin}'.");

            if (!seen.Any(f => !pins.Contains(f)))
                problems.Add($"shard {label} selects no fixtures beyond the pinned controls.");
        }

        foreach (var fixture in corpus)
        {
            if (pins.Contains(fixture)) continue;
            if (!owners.TryGetValue(fixture, out var list))
                problems.Add($"'{fixture}' is in no shard, so a sharded run never executes it.");
            else if (list.Distinct().Count() > 1)
                problems.Add($"'{fixture}' is in shards {string.Join(", ", list.Distinct())}; it must be in exactly one.");
        }

        return problems;
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Index}/{Count}");

    /// <summary>
    /// Renders <paramref name="problems"/> for an assertion message, listing at most
    /// <paramref name="max"/> of them. A host that ignored <c>--shard</c> produces one problem per
    /// fixture, and a 1,500-line message buries the cause.
    /// </summary>
    public static string Describe(IReadOnlyList<string> problems, int max = 20)
    {
        var shown = string.Join("\n  ", problems.Take(max));
        return problems.Count <= max ? shown : $"{shown}\n  ...and {problems.Count - max} more";
    }
}
