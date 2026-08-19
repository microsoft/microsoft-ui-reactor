using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.SelfTests;

/// <summary>
/// Headless guards for the three-state fixture verdict added for issue #1061. Everything exercised
/// here is pure, so this class does not launch the Host and does not trigger
/// <see cref="SelfTestBatch"/>'s <c>[ClassInitialize]</c> — it runs in milliseconds under
/// <c>--filter "ClassName~SkipReportingTests"</c>.
///
/// <para><b>What these tests are actually defending.</b> <c>Harness.Skip</c> emits a TAP line
/// beginning <c>ok </c>. The parser branched on that prefix and set <c>sawChecksForCurrent</c> —
/// the flag that exists <i>specifically</i> to catch a fixture which asserted nothing. So a fixture
/// that skipped every check it owned reported <b>PASSED</b>, with zero <c>not ok</c> lines and a
/// <c># Total failures: 0</c> trailer: two anti-vacuity mechanisms cancelling each other out.</para>
///
/// <para><b>And what they are defending against the fix.</b> The obvious repair — don't set the
/// flag for a skip line — makes the verdict <c>Failed</c>, which reddens
/// <c>CenterOnCurrent_UsesCursorMonitor</c> and <c>CornerStyle_Apply</c> on exactly the machines
/// their skips were introduced to accommodate. Both directions are pinned below, so neither bug
/// can be reintroduced while the other stays fixed.</para>
/// </summary>
[TestClass]
public class SkipReportingTests
{
    private const string Skipped = "SkippedFixture";
    private const string Reason = "GetCursorPos unavailable (non-interactive desktop)";

    private static Dictionary<string, SelfTestBatch.FixtureOutcome> Parse(string tap)
    {
        var map = new Dictionary<string, SelfTestBatch.FixtureOutcome>();
        SelfTestBatch.ParseTap(tap, map);
        return map;
    }

    private static SelfTestBatch.FixtureOutcome Outcome(string tap, string fixture)
    {
        var map = Parse(tap);
        Assert.IsTrue(map.TryGetValue(fixture, out var outcome),
            $"Fixture '{fixture}' produced no verdict at all. Reported: " +
            $"[{string.Join(", ", map.Keys)}]");
        return outcome!;
    }

    // ------------------------------------------------------------------ the acceptance test

    /// <summary>
    /// Issue #1061's acceptance test, stated as it is stated in the issue: <i>a fixture emitting
    /// only skip lines must not be indistinguishable from one that ran and passed.</i>
    ///
    /// <para>Deliberately written as a <b>differential</b> oracle over the same fixture name and
    /// the same surrounding stream, so the only difference between the two arms is the
    /// <c># SKIP</c> directive itself. A parser that hard-codes a verdict, or that keys on
    /// anything other than the directive, fails here — a bare
    /// <c>AreEqual(Skipped, …)</c> would also pass against a stub that returns <c>Skipped</c> for
    /// everything.</para>
    /// </summary>
    [TestMethod]
    public void FixtureWhoseOnlyOutputIsASkip_IsNotReportedAsPassed()
    {
        var skipArm = Outcome(
            $"# Running: {Skipped}\nok {Skipped}_Check # SKIP {Reason}\n# Total failures: 0\n",
            Skipped);

        var checkArm = Outcome(
            $"# Running: {Skipped}\nok {Skipped}_Check\n# Total failures: 0\n",
            Skipped);

        Assert.AreEqual(SelfTestBatch.FixtureStatus.Passed, checkArm.Status,
            "Control arm: a real assertion must still pass, or the comparison below is between " +
            "two broken states rather than a healthy and a degraded one.");

        Assert.AreNotEqual(checkArm.Status, skipArm.Status,
            "A fixture that skipped its only check reports the SAME verdict as one that asserted " +
            "and passed. That is issue #1061: the TAP stream carries the distinction and the " +
            "consumer discards it.");

        Assert.AreEqual(SelfTestBatch.FixtureStatus.Skipped, skipArm.Status);
    }

    /// <summary>
    /// The other half of the pin, and the reason a third state was needed at all. Reporting the
    /// skip as a failure would turn the two windowing fixtures red on the non-interactive desktops
    /// their skip exists for — re-creating the regression the skip fixed, in the opposite
    /// direction.
    /// </summary>
    [TestMethod]
    public void FixtureWhoseOnlyOutputIsASkip_IsNotReportedAsFailed()
    {
        var outcome = Outcome(
            $"# Running: {Skipped}\nok {Skipped}_Check # SKIP {Reason}\n", Skipped);

        Assert.AreNotEqual(SelfTestBatch.FixtureStatus.Failed, outcome.Status,
            "An undeterminable precondition is not a product defect. Failing here makes the suite " +
            "red on exactly the machines the skip was introduced to accommodate.");
    }

    /// <summary>
    /// The reason has to survive into the verdict, because the verdict is all a reader gets: an
    /// MSTest skip with no explanation is only marginally better than a green tick with none.
    /// </summary>
    [TestMethod]
    public void SkipReason_ReachesTheReportedDetail()
    {
        var outcome = Outcome(
            $"# Running: {Skipped}\nok {Skipped}_Check # SKIP {Reason}\n", Skipped);

        Assert.IsTrue(outcome.Detail.Contains(Reason, StringComparison.Ordinal),
            $"The '# SKIP' payload is the whole point — it says WHY nothing was established.\n" +
            outcome.Detail);
        CollectionAssert.AreEqual(
            new[] { $"{Skipped}_Check — {Reason}" }, outcome.SkippedChecks.ToArray());
    }

    // ------------------------------------------------------------------ the other verdicts

    [TestMethod]
    public void FixtureWithARealAssertion_Passes()
    {
        var outcome = Outcome("# Running: F\nok F_Check\n", "F");

        Assert.AreEqual(SelfTestBatch.FixtureStatus.Passed, outcome.Status);
        Assert.AreEqual(0, outcome.SkippedChecks.Count);
    }

    /// <summary>
    /// A partially-skipped fixture is legitimately green — it asserted something, and what it
    /// asserted held. The skipped leg is still reported, because a gap nobody can see is a gap
    /// nobody closes, but it must not change the verdict.
    /// </summary>
    [TestMethod]
    public void FixtureWithBothAnAssertionAndASkip_PassesButKeepsTheSkip()
    {
        var outcome = Outcome(
            "# Running: F\nok F_Real\nok F_Deferred # SKIP covered by the E2E tier\nok F_Other\n", "F");

        Assert.AreEqual(SelfTestBatch.FixtureStatus.Passed, outcome.Status,
            "Skipping one leg of a fixture that asserted two others is not a degraded run.");
        CollectionAssert.AreEqual(
            new[] { "F_Deferred — covered by the E2E tier" }, outcome.SkippedChecks.ToArray());
    }

    [TestMethod]
    public void FixtureWithAFailure_Fails()
    {
        var outcome = Outcome(
            "# Running: F\nok F_Real\nnot ok F_Broken - assertion failed\n", "F");

        Assert.AreEqual(SelfTestBatch.FixtureStatus.Failed, outcome.Status);
        Assert.IsTrue(outcome.Detail.Contains("F_Broken", StringComparison.Ordinal), outcome.Detail);
    }

    /// <summary>
    /// A failure outranks a skip. A fixture that skipped one leg and failed another established
    /// something, and what it established is bad — the skip must not soften that to "could not
    /// tell".
    /// </summary>
    [TestMethod]
    public void FixtureThatBothSkippedAndFailed_Fails()
    {
        var outcome = Outcome(
            "# Running: F\nok F_Deferred # SKIP not observable here\nnot ok F_Broken - assertion failed\n",
            "F");

        Assert.AreEqual(SelfTestBatch.FixtureStatus.Failed, outcome.Status);
    }

    /// <summary>
    /// The pre-existing guard this change must not weaken. A fixture that emitted nothing at all is
    /// still a <b>failure</b>: unlike a skip it carries no documented reason, so there is nothing
    /// to distinguish "deliberately deferred" from "the fixture is broken". Widening the verdict
    /// for skips is only safe if the silent case keeps reddening.
    /// </summary>
    [TestMethod]
    public void FixtureThatEmittedNothingAtAll_StillFails()
    {
        var outcome = Outcome("# Running: F\n# Total failures: 0\n", "F");

        Assert.AreEqual(SelfTestBatch.FixtureStatus.Failed, outcome.Status,
            "'Emitted no TAP checks' is the older sibling of this bug and must stay red. A skip " +
            "says why nothing was established; silence does not.");
        Assert.IsTrue(outcome.Detail.Contains("no TAP checks", StringComparison.Ordinal), outcome.Detail);
    }

    // ------------------------------------------------------------------ directive parsing

    /// <summary>
    /// The Host emits a bare-named runner-level <c>not ok</c> when a fixture completes without a
    /// single check or skip, so this asserts the wrapper attributes it to that fixture rather than
    /// inventing a phantom entry. It matters because only <c>_CRASH</c> and <c>_TIMEOUT</c> are
    /// stripped from runner-level names: any other decoration on the Host's line would land the
    /// failure on a fixture name that does not exist, and the real fixture would be reported
    /// against a message that never mentions why.
    /// </summary>
    [TestMethod]
    public void RunnerLevelNoChecksFailure_IsAttributedToTheSilentFixture()
    {
        const string Detail = "fixture ran to completion without emitting a single check or skip";

        var map = Parse($"# Running: F\nnot ok 7 F - {Detail}\n");

        Assert.IsTrue(map.TryGetValue("F", out var outcome),
            "The failure must land on 'F', not on a phantom name parsed out of the line.");

        Assert.AreEqual(SelfTestBatch.FixtureStatus.Failed, outcome!.Status);
        Assert.IsTrue(outcome.Detail.Contains(Detail, StringComparison.Ordinal),
            $"The reported detail must say why. Got: {outcome.Detail}");
        Assert.AreEqual(1, map.Count,
            $"Exactly one fixture should be recorded. Got: {string.Join(", ", map.Keys)}");
    }

    /// <summary>
    /// A fixture that skips and then crashes must keep its skip in the inventory. The crash
    /// arrives as a runner-level <c>not ok &lt;n&gt; &lt;fixture&gt;_CRASH</c>, and <c>Flush</c> has
    /// an early return for a fixture already stamped Failed — so the question is whether the crash
    /// lands in <c>failuresForCurrent</c> (early return skipped, skips preserved) or stamps the map
    /// directly (early return taken, skips silently dropped). It is the former, because
    /// <c>StripRunnerFailureSuffix</c> removes <c>_CRASH</c> and the name then matches the running
    /// fixture — but that is a two-step inference across two functions, so it is pinned here
    /// rather than left to be re-derived. Losing the skip would undercount the inventory in the
    /// one case where a reader most wants both facts: what it gave up on, and what then broke.
    /// </summary>
    [TestMethod]
    public void FixtureThatSkippedThenCrashed_IsFailedButKeepsItsSkip()
    {
        var map = Parse(
            "# Running: F\n" +
            "ok F_Deferred # SKIP live input not synthesizable headlessly\n" +
            "not ok 9 F_CRASH - InvalidOperationException: boom\n");

        Assert.IsTrue(map.TryGetValue("F", out var outcome),
            "The _CRASH suffix must be stripped so the failure lands on 'F'.");
        Assert.AreEqual(SelfTestBatch.FixtureStatus.Failed, outcome!.Status,
            "A crash outranks a skip: the fixture is broken, not merely unproven.");
        Assert.AreEqual(1, outcome.SkippedChecks.Count,
            $"The skip must survive the crash. Got: {string.Join("; ", outcome.SkippedChecks)}");
        Assert.IsTrue(outcome.SkippedChecks[0].Contains("F_Deferred", StringComparison.Ordinal),
            $"The skipped check name must survive. Got: {outcome.SkippedChecks[0]}");

        var inventory = SelfTestBatch.BuildSkipInventory(map);
        Assert.AreEqual(1, inventory.TotalSkippedChecks,
            "A crashed fixture's skip still counts toward the run's skip total.");
        Assert.AreEqual(0, inventory.FullySkippedFixtures.Count,
            "It is Failed, not Skipped, so it must not appear as a fully-skipped fixture.");
    }

    /// <summary>
    /// TAP 14 specifies directives case-insensitively. The Host emits upper case today, and a
    /// parser that accepts only what the current emitter happens to produce is one rename away from
    /// silently reporting every skip as a pass again.
    /// </summary>
    [TestMethod]
    public void SkipDirective_IsMatchedCaseInsensitively()
    {
        foreach (var directive in new[] { "# SKIP", "# skip", "# Skip" })
        {
            var outcome = Outcome($"# Running: F\nok F_Check {directive} reason text\n", "F");
            Assert.AreEqual(SelfTestBatch.FixtureStatus.Skipped, outcome.Status,
                $"Directive '{directive}' was not recognised.");
        }
    }

    /// <summary>
    /// The directive is the bare word <c>SKIP</c>, so a comment that merely begins with those
    /// letters is not one. Without this, a check whose trailing comment happened to start
    /// "skipping…" would silently stop counting as an assertion — the same class of bug in reverse.
    /// </summary>
    [TestMethod]
    public void CommentThatMerelyStartsWithSkip_IsNotADirective()
    {
        Assert.IsFalse(
            SelfTestBatch.TryParseSkipDirective("F_Check # SKIPPABLE later", out _, out _),
            "'SKIPPABLE' is a comment, not a SKIP directive.");

        var outcome = Outcome("# Running: F\nok F_Check # SKIPPABLE later\n", "F");
        Assert.AreEqual(SelfTestBatch.FixtureStatus.Passed, outcome.Status);
    }

    [TestMethod]
    public void SkipWithNoReason_StillParsesAndSaysSo()
    {
        Assert.IsTrue(SelfTestBatch.TryParseSkipDirective("F_Check # SKIP", out var name, out var reason));
        Assert.AreEqual("F_Check", name);
        Assert.AreEqual("(no reason given)", reason);
    }

    /// <summary>
    /// A check name that embeds a tracking number — the repo spells these
    /// <c>ContentDialogLive_Rerender_TextAdvanced_#1069</c>, <c>..._#948</c>, <c>..._#246</c> —
    /// puts a <c>#</c> in the name itself. A parser anchored on the FIRST <c>#</c> reads the
    /// directive as <c>1069 # SKIP …</c>, fails to match, and files the line as an ordinary pass:
    /// issue #1061 restored, for exactly the checks whose names say they are already known to be
    /// trouble.
    ///
    /// <para>Differential, so it cannot pass vacuously: the same fixture, the same skip, the same
    /// wrapping — only the check NAME differs between the two arms. A parser that ignores names
    /// makes both arms Skipped and this test cannot fail; a parser that anchors on the first hash
    /// splits them, which is the defect.</para>
    /// </summary>
    [TestMethod]
    public void SkippedCheckWhoseNameEmbedsAnIssueNumber_IsStillASkip()
    {
        const string GapReason = "decorator retags the target";

        var plain = Outcome($"# Running: F\nok F_Check # SKIP {GapReason}\n", "F");
        var hashed = Outcome($"# Running: F\nok F_Check_#1069 # SKIP {GapReason}\n", "F");

        Assert.AreEqual(SelfTestBatch.FixtureStatus.Skipped, plain.Status,
            "Control arm: a skip with an ordinary check name must be Skipped.");
        Assert.AreEqual(plain.Status, hashed.Status,
            "A '#' in the CHECK NAME must not change the verdict. Anchoring on the first '#' " +
            "makes this Passed — a fixture that asserted nothing reported green, which is #1061.");

        Assert.IsTrue(SelfTestBatch.TryParseSkipDirective($"F_Check_#1069 # SKIP {GapReason}",
            out var name, out var parsedReason));
        Assert.AreEqual("F_Check_#1069", name, "The name must keep its own hash.");
        Assert.AreEqual(GapReason, parsedReason, "The reason must not absorb the name's hash.");
    }

    /// <summary>
    /// The scan must not stop at a hash whose word merely starts with "skip": a later hash can
    /// still carry the real directive. This is the interaction between the multi-hash scan and the
    /// bare-word guard, which a single-hash parser never had to resolve.
    /// </summary>
    [TestMethod]
    public void NameContainingSkippableWord_DoesNotSwallowTheRealDirective()
    {
        Assert.IsTrue(
            SelfTestBatch.TryParseSkipDirective("F_#SKIPPABLE # SKIP the real reason",
                out var name, out var reason),
            "A 'SKIPPABLE' token in the name must not consume the scan.");
        Assert.AreEqual("F_#SKIPPABLE", name);
        Assert.AreEqual("the real reason", reason);
    }

    /// <summary>
    /// The other direction of the same change: a PASSING check whose name embeds a tracking number
    /// must not be mistaken for a skip. Without this, widening the scan could turn every
    /// <c>#</c>-bearing pass into a phantom skip and mark healthy fixtures Skipped.
    /// </summary>
    [TestMethod]
    public void PassingCheckWhoseNameEmbedsAnIssueNumber_IsStillAPass()
    {
        Assert.IsFalse(
            SelfTestBatch.TryParseSkipDirective("F_Check_#1069", out _, out _),
            "A bare name with a tracking number carries no directive.");

        var outcome = Outcome("# Running: F\nok F_Check_#1069\n", "F");
        Assert.AreEqual(SelfTestBatch.FixtureStatus.Passed, outcome.Status);
    }

    [TestMethod]
    public void OrdinaryOkLine_IsNotASkip()
    {
        Assert.IsFalse(SelfTestBatch.TryParseSkipDirective("F_Check", out _, out _));
    }

    // ------------------------------------------------------------------ runner-level skips

    /// <summary>
    /// The runner's AOT skip list emits <c>ok &lt;index&gt; &lt;fixture&gt; # SKIP …</c> with
    /// <b>no</b> <c># Running:</c> marker, so `current` is still the previous, already-finished
    /// fixture. Crediting that fixture with a skip it never emitted would misattribute the reason
    /// text and — worse — could turn a genuinely silent fixture into a plausible-looking skip.
    /// </summary>
    [TestMethod]
    public void RunnerLevelSkip_IsAttributedToItsOwnFixture_NotThePrecedingOne()
    {
        var map = Parse(
            "# Running: Earlier\nok Earlier_Check\n" +
            "ok 42 AotHostile # SKIP crashes/hangs under NativeAOT\n" +
            "# Total failures: 0\n");

        Assert.IsTrue(map.TryGetValue("AotHostile", out var skipped),
            "A fixture the runner skipped before it ran is otherwise reported as 'not reported by " +
            "the Host', which is a hard failure for a deliberate skip.");
        Assert.AreEqual(SelfTestBatch.FixtureStatus.Skipped, skipped!.Status);
        Assert.IsTrue(skipped.Detail.Contains("NativeAOT", StringComparison.Ordinal), skipped.Detail);

        Assert.AreEqual(SelfTestBatch.FixtureStatus.Passed, map["Earlier"].Status);
        Assert.AreEqual(0, map["Earlier"].SkippedChecks.Count,
            "The preceding fixture emitted no skip of its own, so it must not be credited with one.");
    }

    /// <summary>
    /// A runner-level skip has no check name to record: the fixture never ran, so no
    /// <c>H.Skip</c> was reached and the name on the TAP line is the FIXTURE's. Recording it as
    /// the check name renders as <c>`AotHostile` — AotHostile — reason</c> in the job summary,
    /// which repeats the fixture and asserts a check by that name was skipped. There is no such
    /// check, and the inventory is the only place a reader learns what a run did not establish.
    /// </summary>
    [TestMethod]
    public void RunnerLevelSkip_IsLabelled_NotNamedAfterItsFixture()
    {
        var map = Parse("ok 42 AotHostile # SKIP crashes/hangs under NativeAOT\n");
        var entry = Assert.ContainsSingle(map["AotHostile"].SkippedChecks);

        Assert.IsFalse(entry.Contains("AotHostile", StringComparison.Ordinal),
            $"The fixture name must not be repeated as the check name. Got: {entry}");
        Assert.IsTrue(entry.StartsWith(SelfTestBatch.RunnerLevelSkipLabel, StringComparison.Ordinal),
            $"Expected the '{SelfTestBatch.RunnerLevelSkipLabel}' label. Got: {entry}");
        Assert.IsTrue(entry.Contains("NativeAOT", StringComparison.Ordinal),
            $"The reason must survive. Got: {entry}");

        // The rendered inventory line is what a reader actually sees, so assert on that too —
        // the label above is only correct if it composes into a truthful entry.
        var rendered = SelfTestBatch.BuildSkipInventory(map).FullySkippedFixtures.Single();
        Assert.AreEqual(1, CountOccurrences(rendered, "AotHostile"),
            $"The fixture name should appear exactly once in the rendered entry. Got: {rendered}");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    /// <summary>
    /// The discriminator is the leading all-digits index, matching
    /// <c>TryParseRunnerLevelFailure</c>. Harness check names are C# identifiers, so this cannot
    /// collide with one — but a check-level skip must still land on the running fixture.
    /// </summary>
    [TestMethod]
    public void CheckLevelSkip_LandsOnTheRunningFixture()
    {
        var map = Parse($"# Running: {Skipped}\nok {Skipped}_Check # SKIP {Reason}\n");

        Assert.AreEqual(1, map.Count,
            $"A check-level skip must not invent a fixture. Reported: [{string.Join(", ", map.Keys)}]");
        Assert.AreEqual(SelfTestBatch.FixtureStatus.Skipped, map[Skipped].Status);
    }

    // ------------------------------------------------------------------ run-level inventory

    [TestMethod]
    public void SkipInventory_SeparatesFullyFromPartiallySkipped()
    {
        var inventory = SelfTestBatch.BuildSkipInventory(Parse(
            "# Running: FullySkipped\nok FullySkipped_Only # SKIP cannot observe\n" +
            "# Running: PartlySkipped\nok PartlySkipped_Real\nok PartlySkipped_Leg # SKIP deferred\n" +
            "# Running: Clean\nok Clean_Real\n"));

        CollectionAssert.AreEqual(
            new[] { "`FullySkipped` — FullySkipped_Only — cannot observe" },
            inventory.FullySkippedFixtures.ToArray());
        CollectionAssert.AreEqual(
            new[] { "`PartlySkipped` — PartlySkipped_Leg — deferred" },
            inventory.PartiallySkippedFixtures.ToArray());
        Assert.AreEqual(2, inventory.TotalSkippedChecks);
        Assert.IsFalse(inventory.Text.Contains("Clean", StringComparison.Ordinal),
            "A fixture with no skips has nothing to report.");
    }

    /// <summary>
    /// The counter that <c>SkipDirectives_SurviveIntoTheReport</c> gates on. If it could not reach
    /// zero, that guard would be a tautology; if it could not exceed zero, the guard would be
    /// permanently red. Both directions are pinned here so the guard is known to be an instrument
    /// that can come out either way.
    /// </summary>
    [TestMethod]
    public void SkipInventory_CountsZeroForARunWithNoSkips()
    {
        var inventory = SelfTestBatch.BuildSkipInventory(Parse("# Running: F\nok F_Check\n"));

        Assert.AreEqual(0, inventory.TotalSkippedChecks);
        Assert.AreEqual(0, inventory.FullySkippedFixtures.Count);
        Assert.AreEqual(0, inventory.PartiallySkippedFixtures.Count);
        Assert.IsTrue(inventory.Text.Contains("No checks were skipped", StringComparison.Ordinal),
            inventory.Text);
    }

    /// <summary>
    /// The report has to name the fixtures and their reasons, not just count them. A summary that
    /// says "2 fixtures skipped" without saying which is the same silence in a shorter costume.
    /// </summary>
    [TestMethod]
    public void SkipReport_NamesTheFixturesAndTheirReasons()
    {
        var report = SelfTestBatch.PublishSkipReport(Parse(
            $"# Running: {Skipped}\nok {Skipped}_Check # SKIP {Reason}\n"), summaryPath: null);

        Assert.IsTrue(report.Markdown.Contains(Skipped, StringComparison.Ordinal), report.Markdown);
        Assert.IsTrue(report.Markdown.Contains(Reason, StringComparison.Ordinal), report.Markdown);
        Assert.IsFalse(report.Delivered,
            "There is no summary file on this path, so nothing can have been delivered.");
    }

    /// <summary>
    /// Delivery is the load-bearing half — an <c>Assert.Inconclusive</c> message never reaches the
    /// Actions log, so the job summary is the only channel that renders the reasons. Composing a
    /// report nobody receives would look identical to working.
    /// </summary>
    [TestMethod]
    public void SkipReport_LandsOnTheJobSummaryWhenThereIsOne()
    {
        var path = global::System.IO.Path.Join(
            global::System.IO.Path.GetTempPath(), $"reactor-skip-summary-{Guid.NewGuid():N}.md");
        try
        {
            var report = SelfTestBatch.PublishSkipReport(Parse(
                $"# Running: {Skipped}\nok {Skipped}_Check # SKIP {Reason}\n"), path);

            Assert.IsTrue(report.Delivered);
            var written = global::System.IO.File.ReadAllText(path);
            Assert.IsTrue(written.Contains(Skipped, StringComparison.Ordinal), written);
            Assert.IsTrue(written.Contains(Reason, StringComparison.Ordinal), written);
        }
        finally
        {
            if (global::System.IO.File.Exists(path)) global::System.IO.File.Delete(path);
        }
    }

    // ------------------------------------------------------------------ regression guards

    /// <summary>
    /// A recorded runner-level failure must survive the flush that follows it. This is pre-existing
    /// behaviour rather than new, but the flush was rewritten around a three-state verdict, and the
    /// failure mode — a crash downgraded to a pass — is silent.
    /// </summary>
    [TestMethod]
    public void RunnerLevelFailure_IsNotOverwrittenByTheFlush()
    {
        var outcome = Outcome("# Running: F\nnot ok 7 F_CRASH - InvalidOperationException: boom\n", "F");

        Assert.AreEqual(SelfTestBatch.FixtureStatus.Failed, outcome.Status);
        Assert.IsTrue(outcome.Detail.Contains("boom", StringComparison.Ordinal), outcome.Detail);
    }

    /// <summary>
    /// The trailer detector must be indifferent to everything added around it: <c>ParseTap</c>'s
    /// <c>SawTotalFailures</c> is the documented discriminator between a suite-budget kill (#988)
    /// and a Host that died mid-run (#978), and the new <c># Total skipped fixtures:</c> line sits
    /// directly beside it.
    /// </summary>
    [TestMethod]
    public void TotalFailuresTrailer_IsStillDetectedAlongsideTheSkipTrailer()
    {
        var withTrailer = SelfTestBatch.ParseTap(
            "# Running: F\nok F_Check\n# Total failures: 0\n# Total skipped fixtures: 0\n",
            new Dictionary<string, SelfTestBatch.FixtureOutcome>());
        Assert.IsTrue(withTrailer.SawTotalFailures);

        var truncated = SelfTestBatch.ParseTap(
            "# Running: F\nok F_Check\n",
            new Dictionary<string, SelfTestBatch.FixtureOutcome>());
        Assert.IsFalse(truncated.SawTotalFailures,
            "Non-vacuity: the detector must be able to come out false, or the assertion above " +
            "proves nothing.");
    }
}
