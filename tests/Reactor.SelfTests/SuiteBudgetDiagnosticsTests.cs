using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.SelfTests;

/// <summary>
/// Headless guards for the suite-budget diagnostics added for issue #988. Every member exercised
/// here is pure, so this class does not launch the Host and does not trigger
/// <see cref="SelfTestBatch"/>'s <c>[ClassInitialize]</c> — it runs in milliseconds under
/// <c>--filter "ClassName~SuiteBudgetDiagnosticsTests"</c>.
///
/// <para><b>What these tests are actually defending.</b> The bug in #988 was not that a timeout
/// existed; it was that the timeout <i>message</i> argued convincingly for the wrong conclusion,
/// so seven innocent fixtures were investigated across six PRs. The defect is therefore a property
/// of a string, and it regresses the moment someone "simplifies" the two timeout branches back
/// into one. These assertions are written to fail exactly then.</para>
/// </summary>
[TestClass]
public class SuiteBudgetDiagnosticsTests
{
    private const string Tail = "--- tail ---";

    // ---------------------------------------------------------------- budget resolution

    /// <summary>
    /// The regression guard with the most direct link to the bug: if someone reverts the budget
    /// toward the old 300 s, this fails. The bound is deliberately loose (600 s) so that a
    /// deliberate re-tuning between 600 s and any larger value stays green, while a slide back to
    /// the value that could not fit the suite does not.
    /// </summary>
    [TestMethod]
    public void DefaultBudget_IsWellClearOfMeasuredSuiteDuration()
    {
        var seconds = SelfTestBatch.ResolveTimeoutSeconds(null);

        Assert.IsTrue(seconds >= 600,
            $"Default suite budget is {seconds}s. Runs of 262-346s were measured on an unmodified " +
            $"main when issue #988 was filed, so a budget near that range kills healthy runs and " +
            $"blames an arbitrary fixture. Keep a large multiple of the real duration here; the " +
            $"Host's own per-fixture and 60s off-dispatcher watchdogs are what detect hangs.");
    }

    [TestMethod]
    public void EnvOverride_IsHonoured()
    {
        // Non-vacuity: differs from the default, so it fails if the resolver ignores its argument.
        Assert.AreEqual(1234, SelfTestBatch.ResolveTimeoutSeconds("1234"));
        Assert.AreEqual(1234, SelfTestBatch.ResolveTimeoutSeconds("  1234  "));
        Assert.AreNotEqual(SelfTestBatch.ResolveTimeoutSeconds(null),
            SelfTestBatch.ResolveTimeoutSeconds("1234"),
            "An override equal to the default proves nothing; pick a value the default is not.");
    }

    /// <summary>
    /// A malformed or hostile override must fall back, not produce a tiny budget. "0" and "-5" are
    /// the dangerous ones, and they are dangerous in two different ways — the budget is enforced by
    /// <c>Task.Delay(timeoutMs)</c> racing <c>WaitForExitAsync</c> in <c>RunProcess</c>, not by a
    /// <c>WaitForExit(timeoutMs)</c> overload: <c>Task.Delay(0)</c> completes at once, so the
    /// timeout arm wins the race and the Host is killed immediately, reproducing #988 on every run;
    /// a negative delay throws <c>ArgumentOutOfRangeException</c> instead, which fails the whole
    /// batch during <c>ClassInitialize</c>. The resolver must reject both.
    /// </summary>
    [TestMethod]
    public void MalformedOverride_FallsBackToDefault()
    {
        var expected = SelfTestBatch.ResolveTimeoutSeconds(null);

        foreach (var bad in new string?[] { null, "", "   ", "abc", "12s", "0", "-5", "9.5" })
        {
            Assert.AreEqual(expected, SelfTestBatch.ResolveTimeoutSeconds(bad),
                $"Override '{bad ?? "<null>"}' should fall back to the default budget.");
        }
    }

    /// <summary>
    /// The resolved value is multiplied by 1000 to reach milliseconds, so a plausible-looking
    /// large override silently overflows <see cref="int"/> and lands <b>negative</b>. That value
    /// reaches <c>Task.Delay</c> in <c>RunProcess</c>, which throws
    /// <c>ArgumentOutOfRangeException</c> for any negative delay — taking the entire batch down in
    /// <c>ClassInitialize</c> rather than running the suite. So the knob whose whole purpose is to
    /// raise the budget destroys the run instead, and the resolver must reject the value rather
    /// than return it.
    /// <para>
    /// The one negative <c>Task.Delay</c> accepts is <c>-1</c> (infinite), which would silently
    /// remove the cap altogether. It is unreachable here: <c>seconds * 1000</c> wrapped mod 2^32 is
    /// always a multiple of 8, and -1 is not — so every overflow lands in the throwing bucket.
    /// </para>
    /// </summary>
    [TestMethod]
    public void OversizedOverride_FallsBackRatherThanOverflowing()
    {
        var expected = SelfTestBatch.ResolveTimeoutSeconds(null);

        // int.MaxValue / 1000 == 2147483; one above it overflows, and 999999999 is the shape a
        // human types when they mean "effectively no timeout".
        foreach (var big in new[] { "2147484", "999999999", "2147483647" })
        {
            var seconds = SelfTestBatch.ResolveTimeoutSeconds(big);

            Assert.AreEqual(expected, seconds, $"Override '{big}' should fall back to the default.");
            Assert.IsTrue(seconds <= int.MaxValue / 1000,
                $"'{big}' resolved to {seconds}s, which is {(long)seconds * 1000}ms — outside int " +
                $"range, so the ms conversion wraps negative and Task.Delay rejects it, failing " +
                $"the batch in ClassInitialize.");
        }
    }

    /// <summary>
    /// Guards the conversion itself rather than the resolver: the constant the production code
    /// actually passes to <c>Task.Delay</c> must be positive. This fails if someone changes the
    /// units of <c>SelfTestTimeoutMs</c> or introduces the overflow above at the field, where
    /// <see cref="SelfTestBatch.ResolveTimeoutSeconds"/> tests cannot see it.
    /// </summary>
    [TestMethod]
    public void EffectiveBudget_IsPositiveAndMatchesTheResolvedDefault()
    {
        Assert.IsTrue(SelfTestBatch.EffectiveTimeoutSeconds > 0,
            $"Effective budget resolved to {SelfTestBatch.EffectiveTimeoutSeconds}s; a non-positive " +
            $"millisecond budget makes every run a budget kill.");

        // With no env override set in this process, the effective budget must be the default. If
        // the field stops flowing from the resolver, this diverges.
        if (Environment.GetEnvironmentVariable(SelfTestBatch.TimeoutEnvVar) is null)
        {
            Assert.AreEqual(SelfTestBatch.ResolveTimeoutSeconds(null), SelfTestBatch.EffectiveTimeoutSeconds,
                "The budget the wrapper actually uses must come from ResolveTimeoutSeconds, or the " +
                "resolver's tests guard a value production never reads.");
        }
    }

    // ---------------------------------------------------------------- message split

    /// <summary>
    /// The single most important assertion in this file. The two timeout kinds shared one message
    /// and one abort reason before #988, which is precisely why a budget overrun was
    /// indistinguishable from a real hang without pulling the raw job log. If the branches are
    /// ever merged again, this fails.
    /// </summary>
    [TestMethod]
    public void CausalAndPositionalTimeouts_ProduceDifferentMessages()
    {
        var causal = SelfTestBatch.DescribeStarvationHang("SomeFixture", alsoTimedOut: true, 900_000, Tail);
        var positional = SelfTestBatch.DescribeBudgetOverrun("SomeFixture", 900_000, 901.2, 1200, 1401, Tail);

        Assert.AreNotEqual(causal, positional,
            "A dispatcher-starvation hang and a suite-budget overrun must not render identically: " +
            "one names a culprit, the other names a bystander.");
    }

    [TestMethod]
    public void AbortReasons_HaveDistinctPrefixes()
    {
        var causalOrdinary = SelfTestBatch.StarvationHangAbortReason("SomeFixture", alsoTimedOut: false);
        var causalWedged = SelfTestBatch.StarvationHangAbortReason("SomeFixture", alsoTimedOut: true);
        var positional = SelfTestBatch.BudgetOverrunAbortReason("SomeFixture", 900_000);

        Assert.AreNotEqual(causalOrdinary, positional);
        Assert.AreNotEqual(causalWedged, positional);

        // The prefix is what a reader sees on a skipped fixture, so the divergence must occur
        // early rather than in a trailing clause. Compare the first word after "Run aborted".
        Assert.IsTrue(causalOrdinary.StartsWith("Run aborted by", StringComparison.Ordinal), causalOrdinary);
        Assert.IsTrue(causalWedged.StartsWith("Run aborted by", StringComparison.Ordinal), causalWedged);
        Assert.IsTrue(positional.StartsWith("Run aborted:", StringComparison.Ordinal), positional);

        // Both causal variants share a prefix on purpose (same cause), but must remain
        // distinguishable, since only one of them means the process would not die.
        Assert.AreNotEqual(causalOrdinary, causalWedged,
            "FailFast landing vs not landing are different diagnoses: the second says the process " +
            "was wedged below the CLR, which is where a native lock investigation starts.");
    }

    /// <summary>
    /// The <c>--filter</c> repro is the specific line that made the old message actively harmful:
    /// running the blamed fixture alone removes the ~1400 fixtures that shared the budget, i.e.
    /// removes the cause, so it passes and reads as exoneration. It must not come back on the
    /// positional path.
    /// </summary>
    [TestMethod]
    public void PositionalMessage_DoesNotSuggestFilterRepro()
    {
        var positional = SelfTestBatch.DescribeBudgetOverrun("VictimFixture", 900_000, 901.2, 1200, 1401, Tail);

        Assert.IsFalse(positional.Contains("--filter VictimFixture", StringComparison.Ordinal),
            "The positional message must not suggest re-running the blamed fixture in isolation: " +
            "that removes the other fixtures sharing the budget, so it always passes.\n" + positional);
    }

    /// <summary>
    /// Differential counterpart to the test above: the same absence on the causal path would be a
    /// regression, because there the per-fixture repro genuinely reproduces. Asserting both
    /// directions is what makes the pair non-vacuous — a message builder that simply never emits
    /// <c>--filter</c> would satisfy one and fail the other.
    /// </summary>
    [TestMethod]
    public void CausalMessage_KeepsFilterRepro()
    {
        var causal = SelfTestBatch.DescribeStarvationHang("HangingFixture", alsoTimedOut: false, 900_000, Tail);

        Assert.IsTrue(causal.Contains("--filter HangingFixture", StringComparison.Ordinal),
            "A real dispatcher-starvation hang IS reproducible in isolation; keep the repro.\n" + causal);
    }

    /// <summary>
    /// The positional message must carry the numbers a reader needs to reach the right conclusion
    /// without the raw log: elapsed vs budget (is the suite at its cap?) and how far the run got
    /// (were the remaining fixtures run at all?).
    /// </summary>
    [TestMethod]
    public void PositionalMessage_CarriesElapsedBudgetAndProgress()
    {
        var positional = SelfTestBatch.DescribeBudgetOverrun("VictimFixture", 900_000, 901.2, 1200, 1401, Tail);

        Assert.IsTrue(positional.Contains("901.2", StringComparison.Ordinal), "missing elapsed\n" + positional);
        Assert.IsTrue(positional.Contains("900", StringComparison.Ordinal), "missing budget\n" + positional);
        Assert.IsTrue(positional.Contains("1200 of 1401", StringComparison.Ordinal), "missing progress\n" + positional);
        Assert.IsTrue(positional.Contains("NOT PROVEN", StringComparison.Ordinal), "missing verdict\n" + positional);
        Assert.IsTrue(positional.Contains("POSITIONAL", StringComparison.Ordinal),
            "missing the name of the attribution kind\n" + positional);
        Assert.IsTrue(positional.Contains(SelfTestBatch.TimeoutEnvVar, StringComparison.Ordinal),
            "missing the knob that resolves the problem\n" + positional);
    }

    /// <summary>
    /// The positional message must stop short of the opposite false claim. Absence of a
    /// <c>HANG_DETECTED</c> signal does not exonerate the fixture — the watchdog can be disabled by
    /// env or by an attached debugger, and a pathologically slow fixture can pump the dispatcher
    /// often enough never to trip it. A message that declared the fixture innocent would send the
    /// one reader who <i>is</i> looking at a genuine culprit away from it.
    /// </summary>
    [TestMethod]
    public void PositionalMessage_DoesNotClaimTheFixtureIsInnocent()
    {
        var positional = SelfTestBatch.DescribeBudgetOverrun("VictimFixture", 900_000, 901.2, 1200, 1401, Tail);

        foreach (var overclaim in new[] { "BYSTANDER", "is innocent", "not at fault", "nothing to do with" })
        {
            Assert.IsFalse(positional.Contains(overclaim, StringComparison.OrdinalIgnoreCase),
                $"'{overclaim}' asserts innocence the harness cannot establish.\n" + positional);
        }

        // ...and it must still describe the one scenario where the named fixture IS the cause,
        // or the de-overclaiming is only cosmetic.
        Assert.IsTrue(positional.Contains("could still turn out to be at fault", StringComparison.Ordinal),
            "The message must leave the reader a path to the case where the fixture is guilty.\n" + positional);
    }

    /// <summary>
    /// Fixture discovery can fail independently of the timeout, and when it does the message must
    /// degrade rather than throw or print "1200 of ". Renders the count-unknown branch.
    ///
    /// <para>Also pins that the missing denominator is <i>named</i>, not merely omitted: a bare
    /// count reads as if it were the total. Asserted differentially against the known-count
    /// rendering, so hardcoding the note unconditionally fails just as loudly as dropping it —
    /// the presence check alone would survive both.</para>
    /// </summary>
    [TestMethod]
    public void PositionalMessage_HandlesUnknownFixtureCount()
    {
        var positional = SelfTestBatch.DescribeBudgetOverrun("VictimFixture", 900_000, 901.2, 1200, null, Tail);

        Assert.IsTrue(positional.Contains("1200 fixtures had TAP output parsed", StringComparison.Ordinal),
            positional);
        Assert.IsFalse(positional.Contains("1200 of", StringComparison.Ordinal),
            "With no total available the message must not render a half-formed ratio.\n" + positional);

        Assert.IsTrue(positional.Contains("total is UNKNOWN", StringComparison.Ordinal),
            "A missing denominator must be stated, not silently dropped — otherwise the bare count "
            + "reads as the total and a failed discovery leaves no trace anywhere.\n" + positional);

        var known = SelfTestBatch.DescribeBudgetOverrun("VictimFixture", 900_000, 901.2, 1200, 1401, Tail);
        Assert.IsFalse(known.Contains("total is UNKNOWN", StringComparison.Ordinal),
            "The note must be conditional on the count actually being unavailable.\n" + known);
    }

    /// <summary>
    /// The progress line counts fixtures whose TAP was <i>parsed</i>, which includes the in-flight
    /// one — <c>ParseTap</c>'s final <c>Flush()</c> records it. Calling that "completed" is off by
    /// one in the direction that matters: it implies the named fixture finished, which is exactly
    /// the false impression #988 is about.
    /// </summary>
    [TestMethod]
    public void PositionalMessage_DoesNotClaimTheInFlightFixtureCompleted()
    {
        var positional = SelfTestBatch.DescribeBudgetOverrun("VictimFixture", 900_000, 901.2, 1200, 1401, Tail);

        Assert.IsFalse(positional.Contains("completed", StringComparison.OrdinalIgnoreCase),
            "The count includes the fixture that was still running, so it is not a completion count.\n"
            + positional);
        Assert.IsTrue(positional.Contains("still running", StringComparison.Ordinal),
            "Say that the in-flight fixture is inside the count, or the number reads as one too many " +
            "finished fixtures.\n" + positional);
    }

    // ---------------------------------------------------------------- classification wiring

    /// <summary>
    /// The message builders above are pure and individually tested, which is necessary but not
    /// sufficient: they would all still pass if production stopped calling them. <see
    /// cref="SelfTestBatch.ClassifyAbort"/> is the seam production actually goes through, so these
    /// cases fail if the branch that chooses between causal and positional is removed or inverted.
    /// </summary>
    [TestMethod]
    public void ClassifyAbort_HangSignal_IsCausalEvenWhenTheProcessDiedOnItsOwn()
    {
        // The ordinary hang path: the Host's watchdog fast-failed, so the wrapper never timed out.
        // Before #988 this path bypassed the shared wording entirely.
        var outcome = SelfTestBatch.ClassifyAbort(
            timedOut: false, hangFixture: "HangingFixture", lastRunningFixture: "SomethingElse",
            budgetMs: 900_000, elapsedSeconds: 51.0, fixturesReported: 12, fixturesTotal: 1401, tail: Tail);

        Assert.IsNotNull(outcome);
        Assert.AreEqual("HangingFixture", outcome!.Fixture,
            "A HANG_DETECTED signal names the culprit; it must win over the positional fallback.");
        Assert.IsTrue(outcome.AbortReason.StartsWith("Run aborted by", StringComparison.Ordinal),
            outcome.AbortReason);
        Assert.IsTrue(outcome.Detail.Contains("--filter HangingFixture", StringComparison.Ordinal),
            outcome.Detail);
    }

    [TestMethod]
    public void ClassifyAbort_HangSignalPlusTimeout_SaysFailFastDidNotLand()
    {
        var outcome = SelfTestBatch.ClassifyAbort(
            timedOut: true, hangFixture: "HangingFixture", lastRunningFixture: null,
            budgetMs: 900_000, elapsedSeconds: 901.0, fixturesReported: 12, fixturesTotal: 1401, tail: Tail);

        Assert.IsNotNull(outcome);
        Assert.AreEqual("HangingFixture", outcome!.Fixture);

        var ordinary = SelfTestBatch.ClassifyAbort(
            timedOut: false, hangFixture: "HangingFixture", lastRunningFixture: null,
            budgetMs: 900_000, elapsedSeconds: 51.0, fixturesReported: 12, fixturesTotal: 1401, tail: Tail);

        Assert.AreNotEqual(ordinary!.AbortReason, outcome.AbortReason,
            "A hang whose FailFast did not take the process down is a different diagnosis from one " +
            "that did, and the abort reason is the only place a triager sees it.");
    }

    [TestMethod]
    public void ClassifyAbort_TimeoutWithNoHangSignal_IsPositional()
    {
        var outcome = SelfTestBatch.ClassifyAbort(
            timedOut: true, hangFixture: null, lastRunningFixture: "VictimFixture",
            budgetMs: 900_000, elapsedSeconds: 901.2, fixturesReported: 1200, fixturesTotal: 1401, tail: Tail);

        Assert.IsNotNull(outcome);
        Assert.AreEqual("VictimFixture", outcome!.Fixture);
        Assert.IsTrue(outcome.AbortReason.StartsWith("Run aborted:", StringComparison.Ordinal),
            outcome.AbortReason);
        Assert.IsFalse(outcome.Detail.Contains("--filter VictimFixture", StringComparison.Ordinal),
            outcome.Detail);
    }

    /// <summary>
    /// The two "nothing to attribute" cases. Returning an outcome here would stamp an abort reason
    /// onto a healthy run (first case) or invent a culprit out of an empty stdout (second).
    /// </summary>
    [TestMethod]
    public void ClassifyAbort_ReturnsNullWhenThereIsNothingToAttribute()
    {
        Assert.IsNull(SelfTestBatch.ClassifyAbort(
            timedOut: false, hangFixture: null, lastRunningFixture: "AnyFixture",
            budgetMs: 900_000, elapsedSeconds: 300.0, fixturesReported: 1401, fixturesTotal: 1401, tail: Tail),
            "A run that neither hung nor timed out is not aborted, whatever ran last.");

        Assert.IsNull(SelfTestBatch.ClassifyAbort(
            timedOut: true, hangFixture: null, lastRunningFixture: null,
            budgetMs: 900_000, elapsedSeconds: 901.2, fixturesReported: 0, fixturesTotal: null, tail: Tail),
            "With no fixture in flight there is nobody to blame, and inventing one is the bug.");
    }

    // ---------------------------------------------------------------- duration gate

    /// <summary>
    /// The warn threshold asserted on <b>both</b> sides of its boundary. Only asserting the warning
    /// side would pass against a builder that warns unconditionally — which is the failure mode
    /// that makes a warning worthless.
    /// </summary>
    [TestMethod]
    public void DurationGate_WarnsOnlyAboveThreshold()
    {
        const int warn = 420;
        const int budget = 900;

        var below = SelfTestBatch.DescribeSuiteDuration(warn - 1, warn, budget);
        var at = SelfTestBatch.DescribeSuiteDuration(warn, warn, budget);
        var above = SelfTestBatch.DescribeSuiteDuration(warn + 1, warn, budget);

        Assert.IsFalse(below.Warn, "Below the threshold must not warn.");
        Assert.IsFalse(at.Warn, "At the threshold must not warn (strictly-greater boundary).");
        Assert.IsTrue(above.Warn, "Above the threshold must warn.");
    }

    [TestMethod]
    public void DurationGate_ReportsElapsedAndPercentage()
    {
        var (warn, text) = SelfTestBatch.DescribeSuiteDuration(450, 420, 900);

        Assert.IsTrue(warn);
        Assert.IsTrue(text.Contains("450.0s", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("50.0%", StringComparison.Ordinal),
            "450s of a 900s budget is 50%; the percentage is what makes erosion legible.\n" + text);
        Assert.IsTrue(text.Contains("900", StringComparison.Ordinal), text);
    }

    /// <summary>
    /// The warning text must differ from the healthy text, or the annotation carries no signal.
    /// </summary>
    [TestMethod]
    public void DurationGate_WarningTextDiffersFromHealthyText()
    {
        var healthy = SelfTestBatch.DescribeSuiteDuration(100, 420, 900).Text;
        var warning = SelfTestBatch.DescribeSuiteDuration(500, 420, 900).Text;

        Assert.AreNotEqual(healthy, warning);
        Assert.IsTrue(warning.Length > healthy.Length,
            "The warning must add the explanation of what happens at the budget, not just a flag.");
    }

    /// <summary>
    /// The configured warn threshold must sit below the budget <b>actually in effect</b>, otherwise
    /// the warning can only fire after the kill it is supposed to pre-empt — a silently useless
    /// gate. Asserting against <c>ResolveTimeoutSeconds(null)</c> alone would miss the case that
    /// matters most: a CI shard that lowers the budget via <see cref="SelfTestBatch.TimeoutEnvVar"/>
    /// past the warn threshold gets no warning at all, and the first symptom is a red PR.
    /// </summary>
    [TestMethod]
    public void ConfiguredWarnThreshold_IsBelowConfiguredBudget()
    {
        var budget = SelfTestBatch.EffectiveTimeoutSeconds;

        // Probed through the builder rather than compared against the constant inline (a const
        // comparison is const-folded and flagged as a known-true assertion). The probe elapsed
        // must be POSITIVE-but-tiny, never 0.0: the builder warns on `elapsed > warnSeconds`, so
        // a 0.0 probe still passes against a threshold regressed to 0 — the exact value this
        // guard names — and would only catch a negative one, while every real run (hundreds of
        // seconds) warned. Half a second is below any threshold worth configuring and above
        // every non-positive one, so it fails on both regressions.
        const double InstantSuiteSeconds = 0.5;

        Assert.IsFalse(
            SelfTestBatch.DescribeSuiteDuration(
                InstantSuiteSeconds, SelfTestBatch.SuiteDurationWarnSeconds, budget).Warn,
            $"A {InstantSuiteSeconds}s suite triggered the duration warning, so the configured " +
            $"threshold ({SelfTestBatch.SuiteDurationWarnSeconds}s) is not positive and every real " +
            $"run warns — training readers to ignore the one signal this gate produces.");

        Assert.IsTrue(SelfTestBatch.SuiteDurationWarnSeconds < budget,
            $"Warn threshold {SelfTestBatch.SuiteDurationWarnSeconds}s must be below the {budget}s " +
            $"budget in effect ({SelfTestBatch.TimeoutEnvVar}=" +
            $"{Environment.GetEnvironmentVariable(SelfTestBatch.TimeoutEnvVar) ?? "<unset>"}); " +
            $"a warning that can only fire after the process is killed never fires.");
    }

    // ---------------------------------------------------------------- report delivery

    /// <summary>
    /// The duration report's delivery channel, guarded because the obvious channel is broken.
    ///
    /// <para>Measured: under <c>dotnet test</c> the tests run in a child <c>testhost</c> whose
    /// stdout the runner does not forward. A probe writing a <c>::warning::</c> line via
    /// <c>Console.WriteLine</c> <i>and</i> via the raw standard-output handle produced neither
    /// marker in the run output, and <c>Assert.Inconclusive</c>'s message was not shown either.
    /// So the gate reports through <c>GITHUB_STEP_SUMMARY</c>, which is ordinary file I/O done by
    /// this process. If that write silently no-ops, the erosion this feature exists to surface
    /// goes back to being invisible — hence a test that the bytes actually land.</para>
    /// </summary>
    [TestMethod]
    public void StepSummary_WritesTheReportWhenAPathIsProvided()
    {
        var path = TempSummaryPath();

        try
        {
            Assert.IsTrue(SelfTestBatch.TryAppendSummary(path, "FIRST-BLOCK"),
                "A writable path must report success, or the caller cannot tell a missing summary " +
                "from a failed write.");
            Assert.IsTrue(SelfTestBatch.TryAppendSummary(path, "SECOND-BLOCK"));

            var written = global::System.IO.File.ReadAllText(path);

            Assert.IsTrue(written.Contains("FIRST-BLOCK", StringComparison.Ordinal), written);
            Assert.IsTrue(written.Contains("SECOND-BLOCK", StringComparison.Ordinal),
                "The summary must be appended, not overwritten: other steps share this file.\n" + written);
            Assert.IsTrue(written.IndexOf("FIRST-BLOCK", StringComparison.Ordinal)
                          < written.IndexOf("SECOND-BLOCK", StringComparison.Ordinal),
                "Appends must preserve order.\n" + written);
        }
        finally
        {
            if (global::System.IO.File.Exists(path)) global::System.IO.File.Delete(path);
        }
    }

    /// <summary>
    /// The failure direction that matters: this is a diagnostic channel, so an unusable path must
    /// degrade to "no report", never to an exception. A throw here would convert a perfectly
    /// healthy suite into a red build — worse than the silence being fixed.
    /// </summary>
    /// <remarks>
    /// The unwritable case is a missing directory under the machine's own temp path, not a drive
    /// letter. A drive letter is an assumption about the environment rather than a property of the
    /// code: on a box where it happens to be mapped the write succeeds, and this test goes red on a
    /// <c>TryAppendSummary</c> that behaved correctly. That is the same environment-dependent
    /// false accusation issue #988 is about, so it does not belong in the fix for it. A freshly
    /// named subdirectory that was never created cannot exist anywhere.
    /// </remarks>
    [TestMethod]
    public void StepSummary_DegradesQuietlyWhenTheChannelIsUnusable()
    {
        var missingDir = global::System.IO.Path.Join(
            global::System.IO.Path.GetTempPath(), $"reactor-988-absent-{Guid.NewGuid():N}");

        Assert.IsFalse(global::System.IO.Directory.Exists(missingDir),
            "Guard on the premise: this case only tests anything if the directory really is absent.");

        var underMissingDir = global::System.IO.Path.Join(missingDir, "summary.md");

        foreach (var unusable in new[]
                 {
                     null,             // not running under Actions
                     "",
                     "   ",
                     underMissingDir,  // parent directory does not exist
                     "\0invalid",      // rejected by the filesystem APIs
                 })
        {
            Assert.IsFalse(SelfTestBatch.TryAppendSummary(unusable, "IRRELEVANT"),
                $"'{unusable ?? "<null>"}' is not a usable summary path, so the write must report " +
                $"failure rather than claim success.");
        }

        Assert.IsFalse(global::System.IO.Directory.Exists(missingDir),
            "A diagnostic channel must not create directories as a side effect of failing.");
    }

    // ---------------------------------------------------------------- host elapsed parsing

    [TestMethod]
    public void SuiteElapsed_ParsedFromHostTrailer()
    {
        const string stdout = "TAP version 14\n1..3\nok 1 A\n# Total failures: 0\n# Suite elapsed: 312.4\n";

        Assert.AreEqual(312.4, SelfTestBatch.ExtractSuiteElapsedSeconds(stdout)!.Value, 0.001);
    }

    /// <summary>
    /// Absent marker, malformed value, and empty input must all return null so the wrapper's own
    /// stopwatch is used instead. A parser that returned 0 here would render "0.0s (0.0% of
    /// budget)" on every run — a confident, wrong, and self-consistent number, which is worse than
    /// no number at all.
    /// </summary>
    [TestMethod]
    public void SuiteElapsed_ReturnsNullWhenUnavailable()
    {
        Assert.IsNull(SelfTestBatch.ExtractSuiteElapsedSeconds(""));
        Assert.IsNull(SelfTestBatch.ExtractSuiteElapsedSeconds("TAP version 14\nok 1 A\n"));
        Assert.IsNull(SelfTestBatch.ExtractSuiteElapsedSeconds("# Suite elapsed: not-a-number\n"));
        Assert.IsNull(SelfTestBatch.ExtractSuiteElapsedSeconds("# Suite elapsed:\n"));
    }

    /// <summary>
    /// Two behaviours the parser has always had and nothing pinned, which a refactor could silently
    /// drop: the <b>last</b> parseable marker wins, and an <b>unparseable</b> marker is skipped
    /// rather than allowed to overwrite a good value.
    /// <para>
    /// Both matter because the wrapper falls back to its own stopwatch on null. Reading the first
    /// marker would report a stale figure as if it were current, and letting a malformed later line
    /// null out a good one would silently swap the Host's figure for the wrapper's — which is ≈2.5 s
    /// higher and is exactly the discrepancy that made a 300 s kill read as 302.5 s in #988.
    /// </para>
    /// </summary>
    [TestMethod]
    public void SuiteElapsed_LastParseableMarkerWins()
    {
        Assert.AreEqual(250.5,
            SelfTestBatch.ExtractSuiteElapsedSeconds(
                "# Suite elapsed: 100.0\nok 1 A\n# Suite elapsed: 250.5\n")!.Value, 0.001,
            "With two markers the later one is the run being reported on; reading the first " +
            "reports a stale duration as though it were current.");

        Assert.AreEqual(250.5,
            SelfTestBatch.ExtractSuiteElapsedSeconds(
                "# Suite elapsed: 250.5\n# Suite elapsed: not-a-number\n")!.Value, 0.001,
            "A malformed marker must be skipped, not allowed to discard a value already parsed — " +
            "otherwise one truncated line silently demotes the Host's figure to the wrapper's.");
    }

    /// <summary>
    /// The marker is parsed with <c>\n</c> splitting but the Host writes CRLF on Windows, so a
    /// naive implementation leaves a stray <c>\r</c> and fails to parse. Guard the real shape,
    /// and the leading-whitespace tolerance alongside it — a trailing <c>\r</c> alone is caught
    /// downstream by the value's own <c>Trim</c>, so it does not actually exercise the per-line
    /// trim, and a line that lost it would regress silently.
    /// </summary>
    [TestMethod]
    public void SuiteElapsed_ToleratesWindowsLineEndingsAndIndentation()
    {
        Assert.AreEqual(287.0,
            SelfTestBatch.ExtractSuiteElapsedSeconds("# Total failures: 0\r\n# Suite elapsed: 287.0\r\n")!.Value,
            0.001);

        Assert.AreEqual(287.0,
            SelfTestBatch.ExtractSuiteElapsedSeconds("\t  # Suite elapsed: 287.0\r\n")!.Value,
            0.001,
            "The per-line trim is what makes this parse: the marker is matched with StartsWith, " +
            "so if the trim is ever dropped an indented line becomes invisible and the wrapper " +
            "silently falls back to its own elapsed time — which is ~2s longer, because it " +
            "includes Host process startup.");
    }

    // ------------------------------------------------------- classify -> apply wiring

    /// <summary>
    /// <see cref="SelfTestBatch.ClassifyAbort"/> being right in isolation says nothing about
    /// whether production still routes through it. These exercise the composed
    /// classify-then-apply path — the same two calls <c>RunSelfTests</c> makes — so reverting the
    /// caller to the old inline branches fails here rather than passing quietly.
    ///
    /// <para>What remains untestable in-process is the single line in <c>RunSelfTests</c> that
    /// invokes them, because that method is a <c>[ClassInitialize]</c> which launches a
    /// five-minute Host subprocess. That is the irreducible residue; everything downstream of it
    /// is covered here.</para>
    /// </summary>
    [TestMethod]
    public void ApplyAbortOutcome_BudgetKill_StampsTheVictimAndSuppressesTheEarlyAbortScan()
    {
        var map = new Dictionary<string, (bool Passed, string Detail)>();

        var outcome = SelfTestBatch.ClassifyAbort(
            timedOut: true, hangFixture: null, lastRunningFixture: "VictimFixture",
            budgetMs: 900_000, elapsedSeconds: 901.2, fixturesReported: 1200, fixturesTotal: 1401, tail: Tail);

        var disposition = SelfTestBatch.ApplyAbortOutcome(
            outcome, timedOut: true, budgetMs: 900_000, fullOutput: "OUT", byFixture: map);

        Assert.IsTrue(map.TryGetValue("VictimFixture", out var victim),
            "The attribution has to land in the map the Fixture test method reads, or the run " +
            "reports every fixture as simply 'not reported'.");
        Assert.IsFalse(victim.Passed);
        Assert.IsTrue(victim.Detail.Contains("NOT PROVEN", StringComparison.Ordinal),
            victim.Detail);

        Assert.IsNotNull(disposition.AbortReason);
        Assert.IsNull(disposition.InitError);
        Assert.IsFalse(disposition.RunEarlyAbortCheck,
            "A killed process is truncated by definition, so the early-abort scan would 'discover' " +
            "an early abort on every timeout and overwrite the budget attribution with a weaker one.");
    }

    [TestMethod]
    public void ApplyAbortOutcome_TimeoutWithNothingToBlame_FallsThroughToAnInitError()
    {
        var map = new Dictionary<string, (bool Passed, string Detail)>();

        var outcome = SelfTestBatch.ClassifyAbort(
            timedOut: true, hangFixture: null, lastRunningFixture: null,
            budgetMs: 900_000, elapsedSeconds: 901.2, fixturesReported: 0, fixturesTotal: null, tail: Tail);

        Assert.IsNull(outcome, "Precondition: nothing in flight means nothing to attribute.");

        var disposition = SelfTestBatch.ApplyAbortOutcome(
            outcome, timedOut: true, budgetMs: 900_000, fullOutput: "RAW-OUTPUT", byFixture: map);

        Assert.AreEqual(0, map.Count, "There is no victim, so nothing may be stamped.");
        Assert.IsNull(disposition.AbortReason);
        Assert.IsNotNull(disposition.InitError,
            "An unattributable timeout must still surface as a hard error; silently reporting a " +
            "clean run would be the worst outcome available.");
        Assert.IsTrue(disposition.InitError!.Contains("RAW-OUTPUT", StringComparison.Ordinal),
            "The raw output is the only evidence left on this path.");
        Assert.IsFalse(disposition.RunEarlyAbortCheck);
    }

    [TestMethod]
    public void ApplyAbortOutcome_CleanRun_TouchesNothingAndLetsTheEarlyAbortScanRun()
    {
        var map = new Dictionary<string, (bool Passed, string Detail)>();

        var disposition = SelfTestBatch.ApplyAbortOutcome(
            outcome: null, timedOut: false, budgetMs: 900_000, fullOutput: "OUT", byFixture: map);

        Assert.AreEqual(0, map.Count);
        Assert.IsNull(disposition.AbortReason);
        Assert.IsNull(disposition.InitError);
        Assert.IsTrue(disposition.RunEarlyAbortCheck,
            "A run that did not time out still needs the early-abort scan — that is how a Host " +
            "that died mid-fixture without tripping any watchdog gets attributed at all.");
    }

    [TestMethod]
    public void ApplyAbortOutcome_HangThatKilledItself_StillAllowsTheEarlyAbortScan()
    {
        var map = new Dictionary<string, (bool Passed, string Detail)>();

        var outcome = SelfTestBatch.ClassifyAbort(
            timedOut: false, hangFixture: "HangingFixture", lastRunningFixture: null,
            budgetMs: 900_000, elapsedSeconds: 51.0, fixturesReported: 12, fixturesTotal: 1401, tail: Tail);

        var disposition = SelfTestBatch.ApplyAbortOutcome(
            outcome, timedOut: false, budgetMs: 900_000, fullOutput: "OUT", byFixture: map);

        Assert.IsTrue(map.TryGetValue("HangingFixture", out var stamped),
            "A hang names its culprit causally, so that name must reach the map even though the " +
            "wrapper's own budget never fired.");
        Assert.IsFalse(stamped.Passed);
        Assert.IsTrue(stamped.Detail.Contains("HangingFixture", StringComparison.Ordinal),
            "Unlike a budget kill this attribution IS causal, so the detail has to name the " +
            "fixture rather than hedge about position.\n" + stamped.Detail);
        Assert.IsTrue(disposition.RunEarlyAbortCheck,
            "Only a timeout suppresses the scan. The scan itself is a no-op once an abort reason " +
            "exists, so gating on the outcome instead of on timedOut would silently change which " +
            "runs get scanned.");
    }

    // ------------------------------------------------------- budget resolution wiring

    [TestMethod]
    public void ResolveTimeoutMilliseconds_ConvertsSecondsAndPreservesTheFallback()
    {
        Assert.AreEqual(1_234_000, SelfTestBatch.ResolveTimeoutMilliseconds("1234"));
        Assert.AreEqual(900_000, SelfTestBatch.ResolveTimeoutMilliseconds(null));
        Assert.AreEqual(900_000, SelfTestBatch.ResolveTimeoutMilliseconds("0"),
            "A zero override must not produce a zero millisecond budget — that kills the run " +
            "instantly, which is issue #988 only faster.");
        Assert.AreEqual(900_000, SelfTestBatch.ResolveTimeoutMilliseconds("99999999"),
            "An override large enough to overflow the millisecond conversion must fall back, not " +
            "wrap to a negative timeout.");
    }

    /// <summary>
    /// The wiring from the environment into the budget the watchdog actually uses.
    ///
    /// <para><b>Known limit, stated rather than papered over:</b> when
    /// <c>REACTOR_SELFTEST_TIMEOUT_SECONDS</c> is unset — the normal case — both sides of this
    /// comparison are the default, so it cannot fail. It has teeth only on a machine that sets the
    /// override, where hardcoding the field would diverge from the resolver. The static field is
    /// initialized once at type load, so no in-process test can re-enter it with a different value;
    /// closing the gap fully would need a child process, which is not worth it for one assignment.
    /// The value coverage above is what carries the weight.</para>
    /// </summary>
    [TestMethod]
    public void EffectiveBudget_TracksTheEnvironmentSeamRatherThanAConstant()
    {
        var expected = SelfTestBatch.ResolveTimeoutMilliseconds(
            Environment.GetEnvironmentVariable(SelfTestBatch.TimeoutEnvVar)) / 1000;

        Assert.AreEqual(expected, SelfTestBatch.EffectiveTimeoutSeconds,
            $"The budget in force must come from {SelfTestBatch.TimeoutEnvVar} via the resolver.");
    }

    // ------------------------------------------------------- job-summary delivery

    /// <summary>
    /// Composition and delivery together. The formatter and the file append were already covered
    /// separately, which left the join between them — the only part that can stop reporting
    /// without anything else changing — uncovered.
    /// </summary>
    [TestMethod]
    public void PublishSuiteDuration_HealthyRun_LandsTheBlockInTheSummaryFile()
    {
        var path = TempSummaryPath();
        try
        {
            var report = SelfTestBatch.PublishSuiteDuration(
                elapsedSeconds: 302.1, warnSeconds: 420, budgetSeconds: 900,
                source: "Host-reported", summaryPath: path);

            Assert.IsFalse(report.Warn, "302.1s is under the 420s warn threshold.");
            Assert.IsTrue(report.Delivered, "The report is the whole mechanism; it has to arrive.");

            var written = global::System.IO.File.ReadAllText(path);
            Assert.IsTrue(written.Contains("302.1s", StringComparison.Ordinal), written);
            Assert.IsTrue(written.Contains("Host-reported", StringComparison.Ordinal),
                "Which clock produced the number decides whether ~2.4s of wrapper overhead is " +
                "included, which is the entire margin at issue in #988.\n" + written);
            Assert.IsTrue(written.Contains("#988", StringComparison.Ordinal), written);
        }
        finally
        {
            if (global::System.IO.File.Exists(path)) global::System.IO.File.Delete(path);
        }
    }

    [TestMethod]
    public void PublishSuiteDuration_WarnRun_IsDistinguishableFromAHealthyOne()
    {
        var path = TempSummaryPath();
        try
        {
            var healthy = SelfTestBatch.PublishSuiteDuration(
                302.1, 420, 900, "Host-reported", summaryPath: null);
            var warned = SelfTestBatch.PublishSuiteDuration(
                450.0, 420, 900, "Host-reported", summaryPath: path);

            Assert.IsTrue(warned.Warn);
            Assert.IsTrue(warned.Delivered);
            Assert.AreNotEqual(healthy.Markdown, warned.Markdown,
                "A reader scanning the job summary has to be able to tell the two apart at a " +
                "glance, or the warning is decoration.");

            var written = global::System.IO.File.ReadAllText(path);
            Assert.IsTrue(written.Contains("450.0s", StringComparison.Ordinal), written);
            Assert.IsTrue(written.Contains("#988", StringComparison.Ordinal), written);
        }
        finally
        {
            if (global::System.IO.File.Exists(path)) global::System.IO.File.Delete(path);
        }
    }

    [TestMethod]
    public void PublishSuiteDuration_ReportsNonDeliveryInsteadOfThrowing()
    {
        var report = SelfTestBatch.PublishSuiteDuration(
            302.1, 420, 900, "wrapper-measured", summaryPath: null);

        Assert.IsFalse(report.Delivered,
            "Running outside Actions is normal; it must report 'not delivered' rather than throw " +
            "or claim success.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(report.Text));
    }

    [TestMethod]
    public void PublishBudgetKill_DeliversThePositionalCaveat()
    {
        var path = TempSummaryPath();
        try
        {
            var (markdown, delivered) = SelfTestBatch.PublishBudgetKill(900, path);

            Assert.IsTrue(delivered);
            Assert.IsTrue(markdown.Contains("positional", StringComparison.OrdinalIgnoreCase),
                "The whole point of #988 is that this attribution is positional; a summary that " +
                "omits the caveat recreates the misreading.\n" + markdown);

            var written = global::System.IO.File.ReadAllText(path);
            Assert.IsTrue(written.Contains("900s", StringComparison.Ordinal), written);
        }
        finally
        {
            if (global::System.IO.File.Exists(path)) global::System.IO.File.Delete(path);
        }
    }

    // ------------------------------------------------------- silent mid-run death (#978)

    /// <summary>
    /// A Host that dies mid-fixture produces the same outward signature as a budget kill — one
    /// arbitrary victim, everything downstream missing, victim moves between runs — so it gets
    /// filed as #988 and "fixed" by raising a budget that was never the constraint. The
    /// <c># Total failures:</c> trailer is what separates them, and these assert the message
    /// actually says so.
    /// </summary>
    [TestMethod]
    public void SilentDeath_SaysItIsNotABudgetKillAndNamesTheDiscriminator()
    {
        var text = SelfTestBatch.DescribeSilentDeath(
            fixture: "LT_EffectCleanupBalanced", exitCode: -1, sawTotalFailures: false,
            elapsedSeconds: 57.7, budgetMs: 900_000, tail: Tail);

        Assert.IsTrue(text.Contains("NOT a suite-budget kill", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("# Total failures:", StringComparison.Ordinal),
            "The trailer is the discriminator; naming it is what stops the next reader re-deriving " +
            "it.\n" + text);
        Assert.IsTrue(text.Contains("#978", StringComparison.Ordinal), text);
        Assert.IsTrue(text.Contains("57.7s", StringComparison.Ordinal), text);
    }

    /// <summary>
    /// The timing sentence must not over-claim. A run that died at 6% of budget can say so; one
    /// that died near the cap cannot, because timing alone no longer separates the two causes.
    /// </summary>
    [TestMethod]
    public void SilentDeath_HedgesTheTimingClaimWhenTheRunLandedNearTheCap()
    {
        var early = SelfTestBatch.DescribeSilentDeath(
            "F", -1, sawTotalFailures: false, elapsedSeconds: 57.7, budgetMs: 900_000, tail: Tail);
        var late = SelfTestBatch.DescribeSilentDeath(
            "F", -1, sawTotalFailures: false, elapsedSeconds: 880.0, budgetMs: 900_000, tail: Tail);

        Assert.IsTrue(early.Contains("well short of the cap", StringComparison.Ordinal), early);
        Assert.IsFalse(late.Contains("well short of the cap", StringComparison.Ordinal),
            "At 97.8% of budget the run did not end well short of anything, and asserting it would " +
            "send a triager past the one explanation still open.\n" + late);
    }

    [TestMethod]
    public void SilentDeath_TrailerPresentMeansTeardown_NotAMidRunDeath()
    {
        var midRun = SelfTestBatch.SilentDeathAbortReason("F", sawTotalFailures: false);
        var teardown = SelfTestBatch.SilentDeathAbortReason("F", sawTotalFailures: true);

        Assert.AreNotEqual(midRun, teardown,
            "These are different diagnoses and the abort reason is stamped verbatim onto every " +
            "skipped fixture, so it is where a triager reads them.");
        Assert.IsTrue(midRun.StartsWith("Run aborted after fixture 'F'", StringComparison.Ordinal),
            "The existing prefix is load-bearing: published triage procedures grep for it.\n" + midRun);
        Assert.IsTrue(teardown.StartsWith("Run aborted after fixture 'F'", StringComparison.Ordinal),
            teardown);
    }

    /// <summary>
    /// The two abort-reason prefixes are a published interface, not an implementation detail.
    ///
    /// <para>They are stamped verbatim onto every skipped fixture, so triage reads them straight
    /// off the trailer with no re-run. The <c>after</c> arm is pinned above; this pins the
    /// <c>before</c> arm, which was inline and therefore had no seam and no assertion. The comment
    /// on <c>TrailerDiscriminator</c> claims BOTH prefixes are stable, and a two-part claim with
    /// one part checked rots on the unchecked side.</para>
    /// </summary>
    [TestMethod]
    public void EarlyAbort_KeepsTheGreppablePrefix_AndStillCarriesTheDiscriminator()
    {
        var midRun = SelfTestBatch.EarlyAbortReason("F", sawTotalFailures: false);
        var teardown = SelfTestBatch.EarlyAbortReason("F", sawTotalFailures: true);

        Assert.IsTrue(midRun.StartsWith("Run aborted before fixture 'F'", StringComparison.Ordinal),
            "Published triage procedures grep this prefix. Changing it returns zero matches, which " +
            "reads as 'no abort happened' rather than 'the wording moved'.\n" + midRun);
        Assert.IsTrue(teardown.StartsWith("Run aborted before fixture 'F'", StringComparison.Ordinal),
            teardown);
        Assert.AreNotEqual(midRun, teardown,
            "The discriminator has to reach this arm too. Without it the prefix alone is not a " +
            "diagnosis: an issue #978 mid-run death and an ordinary early abort read identically.\n"
            + midRun);
    }

    /// <summary>
    /// The budget-kill publish must not be able to displace the budget kill.
    ///
    /// <para><c>TryAppendSummary</c>'s catch is deliberately narrow, so an exception type it has no
    /// reason to see escapes rather than being swallowed. On the asserted path that is correct. On
    /// the failure path the next statement is the <c>Assert.Inconclusive</c> carrying the
    /// attribution caveat, so an escape there reports a summary-file problem <em>instead of</em> the
    /// budget kill — the finding buried by its own diagnostics, which is the failure this PR
    /// exists to remove.</para>
    /// </summary>
    [TestMethod]
    public void BestEffortPublish_AbsorbsWhatTheNarrowCatchLetsThrough()
    {
        var absorbed = SelfTestBatch.PublishBestEffort(
            () => throw new OutOfMemoryException("simulated"), "the test summary");
        var completed = SelfTestBatch.PublishBestEffort(() => { }, "the test summary");

        Assert.IsFalse(absorbed,
            "An exception escaping here skips the caller's Assert.Inconclusive, so the run reports " +
            "a summary-file problem instead of the budget kill it was written to explain.");
        Assert.IsTrue(completed,
            "A real publish must report as having happened, or the return value cannot separate " +
            "'absorbed a fault' from 'ran normally' and carries no information at all.");
    }

    /// <summary>
    /// The trailer decides the whole message, not one line of it.
    ///
    /// <para>This test exists because it was missing. Every other case here passes
    /// <c>sawTotalFailures: false</c>, so the <c>true</c> arm was written and never asserted — and
    /// it contradicted itself: a headline of "STOPPED MID-RUN" and a remaining line of "never RUN"
    /// sat directly above a trailer line saying the Host reached the end of its run. A triager
    /// reading top-down would have started hunting a mid-run crash that did not happen.</para>
    /// </summary>
    [TestMethod]
    public void SilentDeath_TrailerPresent_DoesNotAlsoClaimTheRunStoppedMidWay()
    {
        var finished = SelfTestBatch.DescribeSilentDeath(
            "F", exitCode: -1, sawTotalFailures: true,
            elapsedSeconds: 57.7, budgetMs: 900_000, tail: Tail);

        Assert.IsTrue(finished.Contains("reached the end of its run", StringComparison.Ordinal),
            finished);

        Assert.IsFalse(finished.Contains("STOPPED MID-RUN", StringComparison.Ordinal),
            "The headline is the first thing read and it must agree with the diagnosis three " +
            "lines below it.\n" + finished);
        Assert.IsFalse(finished.Contains("never RUN", StringComparison.Ordinal),
            "The Host printed its trailer, so it did not stop before these fixtures — they were " +
            "never REPORTED, which is a different and much more specific fault.\n" + finished);
        Assert.IsFalse(finished.Contains("#978", StringComparison.Ordinal),
            "#978 is the no-trailer failure specifically; citing it here sends the reader to the " +
            "wrong issue.\n" + finished);

        var midRun = SelfTestBatch.DescribeSilentDeath(
            "F", exitCode: -1, sawTotalFailures: false,
            elapsedSeconds: 57.7, budgetMs: 900_000, tail: Tail);

        Assert.IsTrue(midRun.Contains("STOPPED MID-RUN", StringComparison.Ordinal), midRun);
        Assert.IsTrue(midRun.Contains("#978", StringComparison.Ordinal), midRun);
        Assert.IsTrue(
            finished.Contains("NOT a suite-budget kill", StringComparison.Ordinal)
            && midRun.Contains("NOT a suite-budget kill", StringComparison.Ordinal),
            "Both arms must still rule out #988 — that is the whole reason this message exists.");
    }

    // ------------------------------------------------------- locale independence

    /// <summary>
    /// Diagnostics are parsed back with <c>InvariantCulture</c> and the documented triage snippets
    /// assume a <c>'.'</c> separator, so they must be produced invariantly too. On a comma-decimal
    /// machine a current-culture <c>:F1</c> emits "901,2s", which fails this suite's own assertions
    /// — a red build caused by the reviewer's locale rather than by anything the suite measured.
    /// </summary>
    [TestMethod]
    public void Diagnostics_FormatNumbersInvariantlyOnACommaDecimalMachine()
    {
        var original = global::System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            global::System.Globalization.CultureInfo.CurrentCulture =
                new global::System.Globalization.CultureInfo("de-DE");

            var (_, text) = SelfTestBatch.DescribeSuiteDuration(450.0, 420, 900);
            Assert.IsTrue(text.Contains("450.0s", StringComparison.Ordinal), text);
            Assert.IsTrue(text.Contains("50.0%", StringComparison.Ordinal), text);

            var overrun = SelfTestBatch.DescribeBudgetOverrun(
                "F", 900_000, 901.2, 1200, 1401, Tail);
            Assert.IsTrue(overrun.Contains("901.2s", StringComparison.Ordinal), overrun);
        }
        finally
        {
            global::System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// <c>FixtureCountIfKnown</c> reads the count only when it is already resolved. The property
    /// under test is the <i>absence</i> of a force: this runs while reporting a run that already
    /// failed, and forcing the value there launches a Host subprocess from a failure path.
    /// </summary>
    [TestMethod]
    public void FixtureCount_NeverForcesTheLazy()
    {
        var invoked = 0;
        var names = new Lazy<string[]>(() => { invoked++; return ["A", "B", "C"]; });

        Assert.IsNull(SelfTestBatch.FixtureCountIfKnown(names),
            "An unresolved count must read as unknown, not be materialised on demand.");
        Assert.AreEqual(0, invoked,
            "Reporting a failed run must not launch `--list-fixtures` to decorate the message.");

        _ = names.Value;

        Assert.AreEqual(3, SelfTestBatch.FixtureCountIfKnown(names),
            "Once discovery has resolved the names — the normal case — the count must be reported.");
        Assert.AreEqual(1, invoked);
    }

    /// <summary>
    /// The case the deleted <c>catch { return null; }</c> existed for: a discovery failure must
    /// degrade to "unknown" rather than replace the timeout message with a discovery exception.
    /// A <see cref="Lazy{T}"/> whose factory threw reports <c>IsValueCreated == false</c>, so the
    /// guard covers it without a catch — but that is a claim about the BCL, so it is asserted.
    /// </summary>
    [TestMethod]
    public void FixtureCount_SurvivesADiscoveryFailureWithoutRethrowing()
    {
        var names = new Lazy<string[]>(
            () => throw new InvalidOperationException("`--list-fixtures` failed with exit code 3."));

        Assert.Throws<InvalidOperationException>(() => _ = names.Value,
            "Guard on the premise: this Lazy must genuinely be in the faulted state.");

        Assert.IsNull(SelfTestBatch.FixtureCountIfKnown(names),
            "A faulted discovery must not propagate out of the diagnostic path.");
    }

    // Path.Join, not Path.Combine: Combine silently discards everything before a rooted segment,
    // so a name that ever gains a leading separator would quietly write outside the temp directory.
    private static string TempSummaryPath() => global::System.IO.Path.Join(
        global::System.IO.Path.GetTempPath(), $"reactor-988-summary-{Guid.NewGuid():N}.md");
}
