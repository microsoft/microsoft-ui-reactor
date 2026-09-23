using System.Diagnostics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Coverage for the UI-turn continuity wiring in <see cref="WinAppUi"/>. Lives in the AppTests
/// assembly because <c>ResolveWorkflowId</c> and <c>ReleaseUiTurn</c> are internal.
///
/// Deliberately does <em>not</em> derive from <see cref="AppTestBase"/>: that base yields the UI
/// turn in its cleanup, which is one of the behaviours under test here. The id-resolution cases are
/// pure and headless; the two wiring cases spawn winapp but never take the desktop, so nothing here
/// needs a Host window.
/// </summary>
[TestClass]
public class WinAppWorkflowIdTests
{
    // ── ResolveWorkflowId ────────────────────────────────────────────────────

    [TestMethod]
    public void ResolveWorkflowId_InheritsAUsableAmbientValue()
    {
        // The agent-harness case: a caller that already named its workflow gets to keep it, so the
        // test run joins the surrounding workflow instead of competing with it for the desktop.
        var resolved = WinAppUi.ResolveWorkflowId("agent-42", pid: 1234, unique: Guid.NewGuid());

        Assert.AreEqual("agent-42", resolved);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void ResolveWorkflowId_SynthesizesWhenAmbientIsUnusable(string? ambient)
    {
        // winapp rejects an empty id with InvalidWorkflowId on *every* command, so propagating one
        // would fail the whole suite rather than merely lose continuity.
        var resolved = WinAppUi.ResolveWorkflowId(ambient, pid: 1234, unique: Guid.NewGuid());

        Assert.IsFalse(string.IsNullOrWhiteSpace(resolved));
        Assert.AreNotEqual(ambient, resolved);
    }

    [TestMethod]
    public void ResolveWorkflowId_SynthesizesWhenAmbientExceedsWinAppsCap()
    {
        var tooLong = new string('x', WinAppUi.MaxWorkflowIdLength + 1);

        var resolved = WinAppUi.ResolveWorkflowId(tooLong, pid: 1234, unique: Guid.NewGuid());

        Assert.AreNotEqual(tooLong, resolved);
        Assert.IsTrue(resolved.Length <= WinAppUi.MaxWorkflowIdLength);
    }

    [TestMethod]
    public void ResolveWorkflowId_AcceptsAnAmbientValueExactlyAtTheCap()
    {
        // The boundary belongs to the caller: winapp's check is `> Max`, so an id of exactly Max is
        // valid and must be inherited rather than replaced.
        var atCap = new string('x', WinAppUi.MaxWorkflowIdLength);

        Assert.AreEqual(atCap, WinAppUi.ResolveWorkflowId(atCap, pid: 1234, unique: Guid.NewGuid()));
    }

    /// <summary>
    /// An ambient id containing an unpaired UTF-16 surrogate must be replaced, not inherited.
    /// </summary>
    /// <remarks>
    /// <para>winapp hashes the workflow id with a UTF-8 encoder configured to throw rather than
    /// substitute U+FFFD — deliberately, so that distinct ill-formed ids cannot collapse onto one
    /// owner key. The consequence is that an unpaired surrogate is a hard <c>InvalidWorkflowId</c>
    /// on <em>every</em> command, so inheriting one would fail the entire E2E suite rather than
    /// merely lose the idle grace. Length and emptiness are the obvious rejection cases; this is
    /// the one that is easy to miss.</para>
    /// <para>The inputs are built here rather than passed as <c>[DataRow]</c> arguments on
    /// purpose. Custom attribute blobs store strings as UTF-8 (ECMA-335 <c>SerString</c>), which
    /// cannot represent an unpaired surrogate, so a <c>[DataRow]</c> silently delivers U+FFFD and
    /// the test ends up asserting against perfectly valid text. The precondition below is what
    /// makes that failure mode loud instead of invisible.</para>
    /// </remarks>
    [TestMethod]
    public void ResolveWorkflowId_ReplacesAnAmbientValueWinAppCannotEncode()
    {
        const char High = '\uD800';
        const char Low = '\uDC00';

        (string Ambient, string Why)[] cases =
        [
            (High.ToString(), "lone high surrogate"),
            (Low.ToString(), "lone low surrogate"),
            ("run-" + High + "-id", "high surrogate embedded in otherwise valid text"),
            ("run-" + Low + "-id", "low surrogate embedded in otherwise valid text"),
            (new string([High, High]), "two high surrogates, which never form a pair"),
            (new string([Low, High]), "a reversed pair, which is two unpaired surrogates"),
        ];

        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        foreach (var (ambient, why) in cases)
        {
            Assert.ThrowsExactly<EncoderFallbackException>(
                () => strict.GetByteCount(ambient),
                $"Precondition: '{why}' must really be an id winapp cannot encode.");

            var resolved = WinAppUi.ResolveWorkflowId(ambient, pid: 1234, unique: Guid.NewGuid());

            Assert.AreNotEqual(ambient, resolved, $"Inherited an id winapp rejects ({why}).");

            // The replacement must itself be encodable, or the fallback just moves the failure.
            strict.GetByteCount(resolved);
        }
    }

    /// <summary>
    /// Positive control for the surrogate rejection: a well-formed astral character is ordinary
    /// text and must still be inherited.
    /// </summary>
    /// <remarks>
    /// Without this, narrowing the check to "contains any surrogate code unit" would leave the
    /// rejection tests green while silently discarding valid ids.
    /// </remarks>
    [TestMethod]
    public void ResolveWorkflowId_KeepsAnAmbientValueWithAPairedSurrogate()
    {
        const string paired = "run-\uD83D\uDE80-id"; // U+1F680, a correctly paired surrogate.

        Assert.AreEqual(paired, WinAppUi.ResolveWorkflowId(paired, pid: 1234, unique: Guid.NewGuid()));
    }

    [TestMethod]
    public void ResolveWorkflowId_SeparatesConcurrentRunsFromTheSameCheckout()
    {
        // Two test processes started from one worktree must not share an id, or each would extend
        // the other's turn and the arbitration that keeps them apart would be defeated.
        var a = WinAppUi.ResolveWorkflowId(null, pid: 1111, unique: Guid.NewGuid());
        var b = WinAppUi.ResolveWorkflowId(null, pid: 2222, unique: Guid.NewGuid());

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void ResolveWorkflowId_SynthesizedValueIsWithinWinAppsCap()
    {
        var resolved = WinAppUi.ResolveWorkflowId(null, pid: int.MaxValue, unique: Guid.NewGuid());

        Assert.IsTrue(
            resolved.Length <= WinAppUi.MaxWorkflowIdLength,
            $"synthesized id was {resolved.Length} chars, over winapp's {WinAppUi.MaxWorkflowIdLength} cap: {resolved}");
    }

    [TestMethod]
    public void WorkflowId_IsAValueWinAppWillAccept()
    {
        // Continuity is identity-based, and WorkflowId is resolved once into a get-only property,
        // so stability is structural rather than something to assert. What is worth pinning is that
        // the resolved value clears winapp's validation — a null or over-long id would be rejected
        // on every command in the suite rather than merely losing the grace period.
        var id = WinAppUi.WorkflowId;

        Assert.IsFalse(string.IsNullOrWhiteSpace(id), "workflow id was blank.");
        Assert.IsTrue(
            id.Length <= WinAppUi.MaxWorkflowIdLength,
            $"workflow id was {id.Length} chars, over winapp's {WinAppUi.MaxWorkflowIdLength} cap.");
    }

    [TestMethod]
    public void CreateStartInfo_StampsTheWorkflowIdOnEveryChild()
    {
        // Covers the Run() path too: both Run and ReleaseUiTurn build their child through this one
        // helper, so this is the seam where a missing stamp would show up for every verb at once.
        var psi = WinAppUi.CreateStartInfo("inspect", "--json");

        Assert.IsTrue(
            psi.Environment.TryGetValue(WinAppUi.WorkflowIdEnvVar, out var stamped),
            $"{WinAppUi.WorkflowIdEnvVar} was not set on the child environment.");
        Assert.AreEqual(WinAppUi.WorkflowId, stamped);
    }

    // ── The wiring actually reaches the child process ────────────────────────
    //
    // `winapp ui yield` is the one verb whose exit code depends solely on whether
    // WINAPP_UI_WORKFLOW_ID reached the process: it refuses (non-zero) for an anonymous caller and
    // succeeds (zero) for a named workflow. That asymmetry is the differential oracle below — the
    // negative control proves the positive result is not simply "winapp always exits 0".
    //
    // Both are gated on the verb existing. Cooperative UI turns landed in winappCli#767, merged
    // 2026-09-09, and *every* published winapp predates it — v0.6.0 (2026-08-12) is the newest
    // stable and v0.6.1 (2026-08-19) the newest prerelease. There is therefore no version to pin
    // `setup-WinAppCli` to that would make this a hard requirement; that only becomes possible
    // once a release ships containing #767, at which point `RequireYieldVerb` should become an
    // assertion. CI installs whatever the action resolves as latest, so on CI `winapp ui yield`
    // is an unknown verb today. Without the gate the positive case fails outright and the
    // negative case passes for the wrong reason — an unknown verb also exits non-zero — which is
    // worse, because it reads as a working differential while measuring nothing. `ReleaseUiTurn`
    // itself is best-effort in production and is unaffected either way.

    private static void RequireYieldVerb()
    {
        var support = WinAppUi.UiYieldSupport;
        var present = support == WinAppUi.UiVerbSupport.Present;

        // Unconditional, so the capability is a recorded fact rather than an inference from a
        // test that quietly did not run. MTP reports `Assert.Inconclusive` as *passed with zero
        // skipped*, so without this line a run where winapp lacks the verb is indistinguishable
        // in every report from one where the differential was measured and held.
        //
        // The resolved path is logged beside it because the capability is a property of *that*
        // binary: CI's own diagnostic step once reported the verb present while this gate
        // reported it absent in the same job, which is only explicable if the two were asking
        // different winapps. Printing the path turns that from a guess into a comparison.
        Console.WriteLine(
            $"[Reactor.AppTests] winapp `ui yield` support: {support} (present: {present}). " +
            $"Resolved winapp: {WinAppUi.ResolvedWinAppExe}. " +
            $"Strict mode ({RequireYieldEnvVar}): {StrictYieldRequested()}.");

        if (present) return;

        var explanation = support == WinAppUi.UiVerbSupport.Unreadable
            ? "The resolved winapp did not return a readable `ui --help` command list, so " +
              "whether it implements `ui yield` was never established. This is not the same as " +
              "the verb being absent: it means the probe itself failed, and treating it as " +
              "absence would be reporting a measurement that was never taken."
            : "The resolved winapp has no `ui yield` verb, so its exit code cannot report " +
              "whether a workflow id arrived. Cooperative UI turns landed in winappCli#767 " +
              "(merged 2026-09-09); pin a winapp containing it to make this measurable.";

        // Opt-in enforcement. The verb cannot be required by default without turning every run
        // red against a dependency that has not shipped it, but a caller that *has* pinned a
        // build containing #767 needs a way to prove the continuity is live rather than take
        // the silent skip. Setting the variable converts this gate into a hard failure, which
        // is also the switch to flip in CI the day a release carries the verb.
        if (StrictYieldRequested())
        {
            Assert.Fail(
                $"{explanation} {RequireYieldEnvVar} is set, so anything short of a confirmed " +
                "verb is a failure: either pin a winapp containing #767 or unset the variable.");
        }

        Assert.Inconclusive(
            $"{explanation} Once a release ships with it, pin that version and set " +
            $"{RequireYieldEnvVar}=1 to make this an assertion.");
    }

    /// <summary>Opt-in switch that turns a missing <c>ui yield</c> verb into a failure.</summary>
    internal const string RequireYieldEnvVar = "REACTOR_E2E_REQUIRE_UI_YIELD";

    private static bool StrictYieldRequested() =>
        IsStrictYieldValue(Environment.GetEnvironmentVariable(RequireYieldEnvVar));

    /// <summary>
    /// Whether an environment variable value asks for strict enforcement.
    /// </summary>
    /// <remarks>
    /// Split out as a pure function so both directions are testable without mutating the
    /// process environment. Unset and empty must read as "not requested" — an unset variable is
    /// the default for every existing run, and a shell that exports an empty value means the
    /// same thing — while <c>0</c> and <c>false</c> are honoured because a caller disabling the
    /// switch explicitly must not silently enable it.
    /// </remarks>
    internal static bool IsStrictYieldValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        var trimmed = value.Trim();
        return !trimmed.Equals("0", StringComparison.Ordinal)
            && !trimmed.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The cleanup boundary must yield exactly when the test took a turn.
    /// </summary>
    /// <remarks>
    /// <para>The tests above prove <see cref="WinAppUi.ReleaseUiTurn"/> works by calling it
    /// directly, which is a different claim from "the teardown every E2E base class runs still
    /// calls it". Delete the call from <c>RecordAndYield</c> and those tests stay green while
    /// every real test ends its turn holding the workflow's idle grace — a throughput bug with
    /// no failing assertion anywhere. The seam exists so this decision is observable without
    /// spawning a <c>winapp.exe</c> child.</para>
    /// <para>Both directions are asserted: skipping the yield is the point of the
    /// <c>spawned &gt; 0</c> test (a headless test must not pay a process spawn), so an
    /// implementation that always yielded would be just as wrong as one that never did.</para>
    /// </remarks>
    [TestMethod]
    public void TheCleanupBoundaryYieldsOnlyWhenTheTestUsedWinApp()
    {
        var yields = 0;

        // Sampled "at start" equal to the current count, so the finished test spawned nothing.
        E2ETestCleanup.RecordAndYield(
            null, WinAppUi.InvocationCount, null, nameof(TheCleanupBoundaryYieldsOnlyWhenTheTestUsedWinApp),
            () => yields++);

        Assert.AreEqual(0, yields,
            "A test that never touched winapp released a turn it never took, paying a process " +
            "spawn per headless test in this assembly.");

        // One below the current count, so the finished test looks like it spawned exactly one.
        E2ETestCleanup.RecordAndYield(
            null, WinAppUi.InvocationCount - 1, null, nameof(TheCleanupBoundaryYieldsOnlyWhenTheTestUsedWinApp),
            () => yields++);

        Assert.AreEqual(1, yields,
            "The shared teardown did not hand the desktop back after a test that used winapp, " +
            "so every E2E test holds its workflow's idle grace until the next one starts and " +
            "concurrent agents serialise behind it.");
    }

    [TestMethod]
    public void ReleaseUiTurn_SkipsTheSpawnWhenWinAppHasNoYieldVerb()
    {
        // Environment-independent on purpose. The capability is injected rather than probed so
        // this measures the gate itself; reading the real winapp would make the assertion agree
        // with the product for whichever reason the local machine supplies, and would prove
        // nothing at all on a machine whose winapp does have the verb.
        var before = WinAppUi.InvocationCount;

        var exit = WinAppUi.ReleaseUiTurn(verbPresent: false);

        Assert.IsNull(exit, "A winapp with no `ui yield` verb reported an exit code, so it was run.");
        Assert.AreEqual(
            before,
            WinAppUi.InvocationCount,
            "The teardown spawned a winapp.exe to invoke a verb that does not exist, paying a " +
            "process launch per UI test for a command that can only fail.");
    }

    [TestMethod]
    public void ReleaseUiTurn_IsAcceptedBecauseTheWorkflowIdReachesWinApp()
    {
        RequireYieldVerb();

        var exit = WinAppUi.ReleaseUiTurn();

        if (exit is null)
            Assert.Inconclusive("winapp could not be launched; nothing to measure.");

        Assert.AreEqual(0, exit, "winapp rejected the yield, which means it saw no workflow id.");
    }

    [TestMethod]
    public void WinAppRejectsAYieldWithNoWorkflowId()
    {
        RequireYieldVerb();

        // Negative control for the test above. Without it, a winapp build that ignored the variable
        // (or exited 0 unconditionally) would let the positive case pass while continuity was in
        // fact never established. Built from the same helper and stripped of exactly one variable,
        // so the env var is the only difference between the two runs.
        var exit = RunYieldWithoutWorkflowId();

        if (exit is null)
            Assert.Inconclusive("winapp could not be launched; nothing to measure.");

        Assert.AreNotEqual(
            0,
            exit,
            "winapp accepted a yield from an anonymous caller, so the yield exit code cannot " +
            "distinguish a named workflow from an unnamed one and this suite's continuity check " +
            "is no longer meaningful.");
    }

    private static int? RunYieldWithoutWorkflowId()
    {
        try
        {
            var psi = WinAppUi.CreateStartInfo("yield", "--json");
            psi.Environment.Remove(WinAppUi.WorkflowIdEnvVar);

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            if (!proc.WaitForExit(10_000))
            {
                WinAppUi.TryKill(proc);
                return null;
            }

            return proc.ExitCode;
        }
        // Same narrow set as ReleaseUiTurn, for the same reason: null here means "winapp could not
        // be run", which makes the test inconclusive rather than failing it. A failure outside this
        // set is not winapp being unavailable and should not be disguised as it.
        catch (System.ComponentModel.Win32Exception) { return null; }
        catch (InvalidOperationException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (TypeInitializationException) { return null; }
    }

    // ── The strict-mode switch ───────────────────────────────────────────────
    //
    // The two tests above skip when winapp has no `ui yield` verb, and MTP reports a skip as
    // *passed with zero skipped*. The switch is what gives a caller who has pinned a build
    // containing #767 a way to demand the measurement instead of accepting that silence, so its
    // decision is asserted directly — otherwise the escape hatch is itself unverified.

    [TestMethod]
    [DataRow(null, DisplayName = "unset")]
    [DataRow("", DisplayName = "empty")]
    [DataRow("   ", DisplayName = "whitespace")]
    [DataRow("0", DisplayName = "zero")]
    [DataRow("false", DisplayName = "false")]
    [DataRow("False", DisplayName = "False")]
    public void StrictYield_IsNotRequestedByDefaultOrWhenExplicitlyDisabled(string? value)
    {
        Assert.IsFalse(
            WinAppWorkflowIdTests.IsStrictYieldValue(value),
            $"'{value ?? "<null>"}' enabled strict mode, so a run that never asked for it would " +
            "fail against a winapp that has not shipped the verb.");
    }

    [TestMethod]
    [DataRow("1", DisplayName = "one")]
    [DataRow("true", DisplayName = "true")]
    [DataRow("TRUE", DisplayName = "TRUE")]
    [DataRow(" 1 ", DisplayName = "padded")]
    [DataRow("yes", DisplayName = "yes")]
    public void StrictYield_IsRequestedWhenTheVariableIsSet(string value)
    {
        Assert.IsTrue(
            WinAppWorkflowIdTests.IsStrictYieldValue(value),
            $"'{value}' did not enable strict mode, so pinning a winapp with the verb still " +
            "could not turn the silent skip into a measurement.");
    }

    // ── Verb capability probe ────────────────────────────────────────────────
    //
    // The probe this covers replaced one that asked `winapp ui yield --help` and read the exit
    // code. Measured against winapp 0.6.3-prerelease.92, that oracle cannot discriminate:
    //
    //     winapp ui yield        --help  -> exit 0   (verb exists)
    //     winapp ui bogusverbxyz --help  -> exit 0   (verb does NOT exist)
    //     winapp ui bogusverbxyz         -> exit 1   (control: the non-help path still errors)
    //
    // An unrecognized verb is not rejected; winapp prints the *parent* help instead, output
    // byte-identical to `winapp ui --help`, never naming the token it did not understand. The old
    // probe therefore answered "present" for every verb. It still returned the right answer in
    // practice — the published builds lacking `yield` are old enough to reject unmatched tokens —
    // so nothing misbehaved, but it had stopped measuring, which would have made
    // REACTOR_E2E_REQUIRE_UI_YIELD a gate incapable of failing.
    //
    // The fixtures below are shaped after real `winapp ui --help` output.

    private const string HelpWithYield = """
        Description:
          Inspect and interact with any running Windows app using UI Automation (UIA).

        Usage:
          winapp ui [command] [options]

        Options:
          -?, -h, --help  Show help and usage information

        Commands:
          status                        Connect to a target app and display connection info.
          inspect <selector>            View the UI element tree with semantic slugs and bounds.
          invoke <selector>             Activate an element by slug or text search.
          yield                         Release the current workflow's idle UI turn early. A
        """;

    private const string HelpWithoutYield = """
        Description:
          Inspect and interact with any running Windows app using UI Automation (UIA).

        Usage:
          winapp ui [command] [options]

        Commands:
          status                        Connect to a target app and display connection info.
          inspect <selector>            View the UI element tree with semantic slugs and bounds.
          invoke <selector>             Activate an element by slug or text search.
        """;

    [TestMethod]
    public void VerbProbe_ReportsPresentWhenTheCommandListNamesTheVerb()
    {
        Assert.AreEqual(
            WinAppUi.UiVerbSupport.Present,
            WinAppUi.ParseUiVerbSupport(HelpWithYield, "yield"),
            "A winapp that lists `yield` must be reported as having it, or the suite skips the " +
            "continuity it is able to exercise.");
    }

    [TestMethod]
    public void VerbProbe_ReportsAbsentWhenTheCommandListOmitsTheVerb()
    {
        // The case the old exit-code probe could not see. `HelpWithoutYield` is what an older
        // winapp lists, and is exactly the text the previous implementation would have received a
        // `0` alongside.
        Assert.AreEqual(
            WinAppUi.UiVerbSupport.Absent,
            WinAppUi.ParseUiVerbSupport(HelpWithoutYield, "yield"),
            "A winapp with no `yield` verb was reported as having it. That is the vacuous " +
            "oracle this probe exists to replace: strict mode would then be unable to fail.");
    }

    [TestMethod]
    [DataRow("workflow", DisplayName = "word from a description")]
    [DataRow("Release", DisplayName = "first word of a description")]
    [DataRow("element", DisplayName = "word from another description")]
    public void VerbProbe_DoesNotMistakeDescriptionProseForAVerb(string word)
    {
        // Guards the obvious wrong fix. Searching the help *text* for a verb name looks like it
        // works and does not: the parent listing carries every verb's description, so prose
        // matches read as capabilities. Only the command names in the section count.
        Assert.AreEqual(
            WinAppUi.UiVerbSupport.Absent,
            WinAppUi.ParseUiVerbSupport(HelpWithYield, word),
            $"'{word}' appears only in description prose, so reporting it as a verb means the " +
            "probe is matching text rather than parsing the command list.");
    }

    [TestMethod]
    public void VerbProbe_ReportsUnreadableWhenThereIsNoCommandList()
    {
        // An error page, a pager, or a truncated read. Distinct from Absent on purpose: calling
        // this "the verb is missing" would report a measurement that never happened.
        Assert.AreEqual(
            WinAppUi.UiVerbSupport.Unreadable,
            WinAppUi.ParseUiVerbSupport("winapp: unknown option '--help'", "yield"),
            "Output with no command list was treated as proof the verb is absent.");
    }

    [TestMethod]
    public void VerbProbe_ReportsUnreadableWhenNoLongStandingVerbIsListed()
    {
        // The format-change guard. A `Commands:` section that contains none of the verbs that have
        // always existed means the parse is wrong, not that winapp lost its entire surface --
        // without this, a reformat would silently read as "yield was removed" forever.
        const string reshaped = """
            Commands:
              --status                      Connect to a target app.
              --inspect                     View the UI element tree.
            """;

        Assert.AreEqual(
            WinAppUi.UiVerbSupport.Unreadable,
            WinAppUi.ParseUiVerbSupport(reshaped, "yield"),
            "A command list whose entries no longer parse was read as a definitive answer.");
    }
}
