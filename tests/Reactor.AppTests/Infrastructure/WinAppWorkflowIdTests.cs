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

    /// <summary>
    /// Whether the resolved winapp understands <c>ui yield</c> at all, as opposed to
    /// understanding it and refusing this caller.
    /// </summary>
    private static bool YieldVerbExists()
    {
        try
        {
            var psi = WinAppUi.CreateStartInfo("yield", "--help");
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using var proc = Process.Start(psi);
            if (proc is null) return false;

            if (!proc.WaitForExit(10_000))
            {
                WinAppUi.TryKill(proc);
                return false;
            }

            // `--help` for a verb that exists succeeds and never consults the environment, so
            // this separates "no such verb" from "verb present, caller refused".
            return proc.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (NotSupportedException) { return false; }
        catch (TypeInitializationException) { return false; }
    }

    private static void RequireYieldVerb()
    {
        if (!YieldVerbExists())
        {
            Assert.Inconclusive(
                "The resolved winapp has no `ui yield` verb, so its exit code cannot report " +
                "whether a workflow id arrived. Cooperative UI turns landed in winappCli#767 " +
                "(merged 2026-09-09) and no published winapp contains it yet — v0.6.0 is the " +
                "newest stable and v0.6.1 the newest prerelease, both from August 2026 — so " +
                "there is no version to pin `setup-WinAppCli` to. Once a release ships with it, " +
                "pin that version and turn this gate into an assertion.");
        }
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
}
