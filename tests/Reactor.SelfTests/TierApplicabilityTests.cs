using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.SelfTests;

/// <summary>
/// Headless guards for the tier-applicability reporting added for issue #1154. Everything here is
/// pure, so this class does not launch the Host and does not trigger <see cref="SelfTestBatch"/>'s
/// <c>[ClassInitialize]</c>.
///
/// <para><b>What is being defended.</b> Three <c>Packaged_*</c> fixtures used to run in the
/// unpackaged host, self-skip, and leave a permanent amber entry in
/// <c>SkippedFixtures_AreReported</c> — a channel whose value depends on being rare. They are now
/// declared <c>SelfTestTier.Packaged</c> and are not selected here at all. That is a fix by
/// <i>removal</i>, and the trouble with those is that success and catastrophe look identical from
/// outside: "no amber" is what you get whether the filter works or whether somebody deleted the
/// packaged corpus. The Host therefore names what it excluded, and
/// <c>NotApplicableFixtures_AreExcludedFromThisTier</c> asserts on it. These tests pin the parser
/// that assertion depends on.</para>
///
/// <para><b>The distinction that carries the weight</b> is <i>absent trailer</i> versus <i>zero
/// exclusions</i>. Absent means the reporting mechanism is silent; zero means the host ran
/// everything. Collapsing them would make a Host that stopped emitting the trailer entirely
/// indistinguishable from a healthy packaged run — and the packaged shim's mirror assertion
/// demands exactly zero, so it would then pass on silence. Several tests below exist only to keep
/// those two apart.</para>
/// </summary>
[TestClass]
public class TierApplicabilityTests
{
    private const string Count = SelfTestBatch.NotApplicableCountMarker;
    private const string List = SelfTestBatch.NotApplicableListMarker;

    private const string RunBody =
        "TAP version 14\n1..2\n# Running: A\nok A_Check\n# Running: B\nok B_Check\n# Total failures: 0\n";

    // ------------------------------------------------------------------ the happy path

    /// <summary>
    /// Stated as a differential over the same stream, so a parser that hard-coded the packaged
    /// fixture names — or returned a canned list for anything — fails here. Only the trailer
    /// differs between the two arms.
    /// </summary>
    [TestMethod]
    public void TrailerNamesTheExcludedFixtures()
    {
        var without = SelfTestBatch.ExtractNotApplicableFixtures(RunBody);
        var with = SelfTestBatch.ExtractNotApplicableFixtures(
            RunBody + $"{Count}2\n{List}Packaged_IdentityGuard, Packaged_SettingsStoreRoundTrip\n");

        Assert.IsNull(without.Count, "No trailer in the stream, so there is nothing to report.");
        Assert.AreEqual(2, with.Count);
        CollectionAssert.AreEqual(
            new[] { "Packaged_IdentityGuard", "Packaged_SettingsStoreRoundTrip" },
            with.Names.ToArray(),
            "The names are what the assertion cross-checks against discovery and against the " +
            "parsed results, so they have to survive the comma split with their whitespace " +
            "trimmed.");
    }

    // ------------------------------------------------------------------ absent vs zero

    /// <summary>
    /// The load-bearing distinction. <c>PackagedSelfTestBatch</c> asserts the count is <b>zero</b>
    /// in the packaged tier; if a missing trailer parsed as zero, a Host that stopped reporting
    /// altogether would satisfy that assertion by silence — the tier would go green having
    /// established nothing about its own corpus, which is the failure mode the packaged tier
    /// exists to remove.
    /// </summary>
    [TestMethod]
    public void MissingTrailer_IsNotTheSameAsZeroExclusions()
    {
        var missing = SelfTestBatch.ExtractNotApplicableFixtures(RunBody);
        var zero = SelfTestBatch.ExtractNotApplicableFixtures(RunBody + $"{Count}0\n");

        Assert.IsNull(missing.Count, "Absent trailer must not be reported as a count.");
        Assert.AreEqual(0, zero.Count, "An explicit zero is a measurement, not an absence.");
        Assert.AreEqual(0, zero.Names.Count, "A zero count legitimately carries no list line.");
    }

    /// <summary>
    /// A count that cannot be parsed must stay <see langword="null"/> rather than default to zero,
    /// for the same reason: zero is the answer the packaged tier wants to hear, so a malformed
    /// line must not be able to supply it.
    /// </summary>
    [TestMethod]
    public void MalformedCount_DoesNotMasqueradeAsZero()
    {
        var report = SelfTestBatch.ExtractNotApplicableFixtures(RunBody + $"{Count}three\n");

        Assert.IsNull(report.Count,
            "'three' is not a count. Reading it as 0 would let a garbled trailer assert that " +
            "every fixture applied to this tier.");
    }

    // ------------------------------------------------------------------ truncation

    /// <summary>
    /// The count is parsed independently of the list precisely so the two can be compared. A
    /// stream cut between the two trailer lines is a realistic failure here — the Host ends its
    /// run with a teardown-free <c>TerminateProcess</c> (issue #680) — and a parser that derived
    /// the count from the list would report a consistent, wrong, smaller answer instead.
    /// </summary>
    [TestMethod]
    public void CountWithoutItsList_IsVisibleAsADisagreement()
    {
        var report = SelfTestBatch.ExtractNotApplicableFixtures(RunBody + $"{Count}3\n");

        Assert.AreEqual(3, report.Count);
        Assert.AreEqual(0, report.Names.Count,
            "The list line never arrived, and the count must not be back-filled from it — the " +
            "gap is the finding.");
    }

    /// <summary>
    /// A re-entered Host can emit the trailer twice; the final one describes the run being
    /// reported on. Same last-wins rule as <c>ExtractSuiteElapsedSeconds</c>.
    /// </summary>
    [TestMethod]
    public void LastTrailerWins()
    {
        var report = SelfTestBatch.ExtractNotApplicableFixtures(
            $"{Count}1\n{List}Stale_Fixture\n" + RunBody + $"{Count}2\n{List}Real_One, Real_Two\n");

        Assert.AreEqual(2, report.Count);
        CollectionAssert.AreEqual(new[] { "Real_One", "Real_Two" }, report.Names.ToArray());
    }

    /// <summary>
    /// A malformed later marker must not discard a value already parsed, or a stray line could
    /// silence the whole report. Mirrors the rule <c>SuiteElapsed_LastParseableMarkerWins</c>
    /// pins for the duration trailer.
    /// </summary>
    [TestMethod]
    public void MalformedLaterMarker_DoesNotDiscardAParsedCount()
    {
        var report = SelfTestBatch.ExtractNotApplicableFixtures(
            $"{Count}2\n{List}Real_One, Real_Two\n" + RunBody + $"{Count}\n");

        Assert.AreEqual(2, report.Count,
            "An unparseable marker is skipped, not treated as a new answer.");
    }

    // ------------------------------------------------------------------ marker isolation

    /// <summary>
    /// The new prefixes share a shape with the skip trailers that sit two lines above them in the
    /// same stream. TESTING.md states the rule that a grep for one must not match the others, and
    /// this is where it is enforced: <c># Total skipped fixtures:</c> and
    /// <c># Skipped fixture list:</c> must be inert here.
    /// </summary>
    [TestMethod]
    public void SkipTrailers_AreNotMistakenForTheExclusionTrailer()
    {
        var report = SelfTestBatch.ExtractNotApplicableFixtures(
            RunBody +
            "# Total skipped fixtures: 1\n" +
            "# Skipped fixture list: SelfTestVerdict_OnlySkips_PositiveControl\n");

        Assert.IsNull(report.Count,
            "The skip trailers were read as the exclusion trailer. Those two reports mean " +
            "opposite things — one names fixtures that ran and established nothing, the other " +
            "names fixtures that deliberately did not run.");
    }

    // ------------------------------------------------------------------ discovery

    /// <summary>
    /// Discovery reads the same stream the trailer is appended to, so a comment must not become a
    /// fixture name. It would get a <c>[TestMethod]</c> of its own that could only ever fail as
    /// "was not reported by the Host" — a red pointing at a fixture that does not exist.
    /// </summary>
    [TestMethod]
    public void FixtureNameParsing_DropsTapComments()
    {
        var names = SelfTestBatch.ParseFixtureNames(
            $"Alpha_One\nBeta_Two\n{Count}1\n{List}Packaged_IdentityGuard\n");

        CollectionAssert.AreEqual(new[] { "Alpha_One", "Beta_Two" }, names,
            "Only the two real names are fixtures; both trailer lines are TAP comments.");
    }

    /// <summary>
    /// The other direction: the comment filter must key on the comment marker, not on anything
    /// resembling the excluded names, or a legitimately-named fixture could be dropped from
    /// discovery and silently never run.
    /// </summary>
    [TestMethod]
    public void FixtureNameParsing_KeepsNamesThatResembleTheTrailer()
    {
        var names = SelfTestBatch.ParseFixtureNames(
            "Packaged_IdentityGuard\nNotApplicable_Fixture\nTotal_Fixture\n");

        CollectionAssert.AreEqual(
            new[] { "Packaged_IdentityGuard", "NotApplicable_Fixture", "Total_Fixture" }, names,
            "These are fixture names, not comments — the packaged host lists the first one for " +
            "real, so dropping it would empty the tier this whole mechanism exists to feed.");
    }
}
