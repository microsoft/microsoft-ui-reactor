using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reactor.Tests.Shared;

namespace Microsoft.UI.Reactor.SelfTests;

/// <summary>
/// Headless tests for the CI shard partition (<c>tests/_shared/SelfTestShard.cs</c>), which the
/// Host uses to select a shard and both wrappers use to check it. Pure, so this class does not
/// launch the Host or trigger <see cref="SelfTestBatch"/>'s <c>[ClassInitialize]</c>. The live
/// counterpart is <see cref="SelfTestBatch.Shards_PartitionTheCorpus"/>, which checks what the real
/// Host printed.
/// </summary>
[TestClass]
public class SelfTestShardTests
{
    private const string Control = SelfTestBatch.SkipVerdictControlFixture;

    private static string[] Corpus(int size, params string[] pinsAt)
    {
        var names = Enumerable.Range(0, size).Select(i => $"F{i:D3}").ToList();
        // Scatter the pins through the corpus rather than at the ends, so a Select that only
        // handled a leading or trailing pin would fail.
        for (var i = 0; i < pinsAt.Length; i++)
            names.Insert(Math.Min(names.Count, (i + 1) * size / (pinsAt.Length + 1)), pinsAt[i]);
        return names.ToArray();
    }

    // ------------------------------------------------------------------ parsing

    [TestMethod]
    public void TryParse_AcceptsWellFormedSpecs()
    {
        foreach (var (spec, index, count) in new[] { ("1/2", 1, 2), (" 2/2 ", 2, 2), ("1/1", 1, 1), ("64/64", 64, 64), ("07/10", 7, 10) })
        {
            Assert.IsTrue(SelfTestShard.TryParse(spec, out var shard, out var error), $"'{spec}' should parse: {error}");
            Assert.AreEqual(new SelfTestShard(index, count), shard, $"'{spec}'");
        }
    }

    /// <summary>
    /// Every one of these must be rejected rather than coerced. A lenient parser would turn
    /// "3/2" into some shard and silently run the wrong slice.
    /// </summary>
    [TestMethod]
    public void TryParse_RejectsMalformedSpecs()
    {
        foreach (var spec in new[] { null, "", " ", "0/2", "3/2", "1/0", "1/65", "-1/2", "+1/2", "1/-2", "a/b", "1/2/3", "1", "/2", "1/", "1 /2", "1.0/2" })
        {
            Assert.IsFalse(SelfTestShard.TryParse(spec, out _, out var error), $"'{spec ?? "<null>"}' should be rejected.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(error), $"'{spec ?? "<null>"}' was rejected without saying why.");
        }
    }

    [TestMethod]
    public void ToString_RoundTripsThroughTryParse()
    {
        var shard = new SelfTestShard(3, 7);
        Assert.AreEqual("3/7", shard.ToString());
        Assert.IsTrue(SelfTestShard.TryParse(shard.ToString(), out var reparsed, out _));
        Assert.AreEqual(shard, reparsed);
    }

    // ------------------------------------------------------------------ selection

    /// <summary>
    /// The exact deal for a small corpus, so the rule is pinned down rather than just its
    /// properties: round-robin over non-pinned fixtures in corpus order, with the control in both.
    /// </summary>
    [TestMethod]
    public void Select_DealsRoundRobinAndKeepsPinsInEveryShard()
    {
        var corpus = new[] { "a", "b", Control, "c", "d", "e" };

        CollectionAssert.AreEqual(new[] { "a", Control, "c", "e" }, new SelfTestShard(1, 2).Select(corpus));
        CollectionAssert.AreEqual(new[] { "b", Control, "d" }, new SelfTestShard(2, 2).Select(corpus));
        CollectionAssert.AreEqual(corpus, new SelfTestShard(1, 1).Select(corpus), "One shard is the whole corpus.");
    }

    /// <summary>
    /// For every shard count, the shards Select produces pass the same partition check the wrappers
    /// run against the live Host, and differ in size by at most one fixture.
    /// </summary>
    [TestMethod]
    public void Select_PartitionsTheCorpusForEveryCount()
    {
        var corpus = Corpus(37, Control, "Packaged_IdentityGuard");
        for (var count = 1; count <= 9; count++)
        {
            var shards = Enumerable.Range(1, count)
                .Select(k => (IReadOnlyList<string>)new SelfTestShard(k, count).Select(corpus))
                .ToArray();

            var problems = SelfTestShard.FindPartitionProblems(corpus, shards);
            Assert.AreEqual(0, problems.Count, $"count={count}:\n  {string.Join("\n  ", problems)}");

            var sizes = shards.Select(s => s.Count(f => !SelfTestShard.IsPinned(f))).ToArray();
            Assert.AreEqual(37, sizes.Sum(), $"count={count}: every non-pinned fixture is dealt exactly once.");
            Assert.IsTrue(sizes.Max() - sizes.Min() <= 1, $"count={count}: sizes {string.Join(", ", sizes)} are unbalanced.");
        }
    }

    [TestMethod]
    public void PinnedFixtures_IncludeTheControlsTheWrappersAssertOn()
    {
        CollectionAssert.Contains(SelfTestShard.PinnedFixtures, Control,
            "SelfTestBatch requires the skip positive control in every run.");
        CollectionAssert.Contains(SelfTestShard.PinnedFixtures, "Packaged_IdentityGuard",
            "PackagedSelfTestBatch requires the identity guard in every run.");
    }

    [TestMethod]
    public void ValidatePinnedFixtures_ThrowsOnlyForAStalePin()
    {
        SelfTestShard.ValidatePinnedFixtures(new[] { "x", Control, "Packaged_IdentityGuard" });

        var threw = false;
        try { SelfTestShard.ValidatePinnedFixtures(new[] { "x", "Packaged_IdentityGuard" }); }
        catch (InvalidOperationException ex)
        {
            threw = true;
            StringAssert.Contains(ex.Message, "STALE_SHARD_PIN");
            StringAssert.Contains(ex.Message, Control);
        }
        Assert.IsTrue(threw, "A registry without the pinned control must be reported, not ignored.");
    }

    // ------------------------------------------------------------------ the partition check

    /// <summary>
    /// Each broken partition must be caught, and caught for the right reason. Differential: every
    /// case starts from the healthy 2-way split below and breaks exactly one property of it.
    /// </summary>
    [TestMethod]
    public void FindPartitionProblems_CatchesEachWayAPartitionCanBreak()
    {
        var corpus = new[] { "a", "b", Control, "c", "d" };
        IReadOnlyList<string> s1 = ["a", Control, "c"];
        IReadOnlyList<string> s2 = ["b", Control, "d"];

        Assert.AreEqual(0, SelfTestShard.FindPartitionProblems(corpus, [s1, s2]).Count, "The healthy split is clean.");

        AssertProblem(corpus, [["a", Control], s2], "'c' is in no shard");
        AssertProblem(corpus, [["a", Control, "c", "b"], s2], "'b' is in shards 1, 2");
        AssertProblem(corpus, [["a", Control, "c", "c"], s2], "lists 'c' more than once");
        AssertProblem(corpus, [["a", Control, "c", "zz"], s2], "'zz', which is not in the unsharded corpus");
        AssertProblem(corpus, [["a", "c"], s2], $"missing pinned fixture '{Control}'");
        AssertProblem(corpus, [[Control], ["a", "b", Control, "c", "d"]], "selects no fixtures beyond the pinned controls");
        AssertProblem(corpus, [], "no shards were listed");
    }

    private static void AssertProblem(
        IReadOnlyList<string> corpus, IReadOnlyList<IReadOnlyList<string>> shards, string expected)
    {
        var problems = SelfTestShard.FindPartitionProblems(corpus, shards);
        Assert.IsTrue(problems.Any(p => p.Contains(expected, StringComparison.Ordinal)),
            $"Expected a problem containing \"{expected}\", got:\n  {string.Join("\n  ", problems)}");
    }

    // ------------------------------------------------------------------ wrapper wiring

    [TestMethod]
    public void ResolveShard_UnsetRunsEverythingAndGarbageFailsLoudly()
    {
        Assert.IsNull(SelfTestBatch.ResolveShard(null));
        Assert.IsNull(SelfTestBatch.ResolveShard("  "));
        Assert.AreEqual(new SelfTestShard(2, 3), SelfTestBatch.ResolveShard("2/3"));

        var threw = false;
        try { SelfTestBatch.ResolveShard("2/1"); }
        catch (InvalidOperationException ex)
        {
            threw = true;
            StringAssert.Contains(ex.Message, SelfTestShard.EnvVar);
        }
        Assert.IsTrue(threw, "A malformed shard must fail discovery, not fall back to the whole corpus.");
    }

    [TestMethod]
    public void WithShard_AppendsTheFlagOnlyWhenSharded()
    {
        Assert.AreEqual("--self-test", SelfTestBatch.WithShard("--self-test", null));
        Assert.AreEqual("--self-test --shard 1/2", SelfTestBatch.WithShard("--self-test", new SelfTestShard(1, 2)));
    }

    [TestMethod]
    public void ExtractPlanCount_ReadsTheFirstPlanLine()
    {
        Assert.AreEqual(760, SelfTestBatch.ExtractPlanCount("TAP version 14\n1..760\n# Shard 1/2: 760 of 1518 fixtures\n# Running: A\n"));
        Assert.AreEqual(3, SelfTestBatch.ExtractPlanCount("TAP version 14\r\n1..3\r\n# Running: A\r\n1..9\r\n"));
        Assert.IsNull(SelfTestBatch.ExtractPlanCount("TAP version 14\n# Running: A\nok A_Check\n"));
        Assert.IsNull(SelfTestBatch.ExtractPlanCount(""));
    }
}
