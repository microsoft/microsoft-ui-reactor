using System.Diagnostics;
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

    [TestMethod]
    public void ReleaseUiTurn_IsAcceptedBecauseTheWorkflowIdReachesWinApp()
    {
        var exit = WinAppUi.ReleaseUiTurn();

        if (exit is null)
            Assert.Inconclusive("winapp could not be launched; nothing to measure.");

        Assert.AreEqual(0, exit, "winapp rejected the yield, which means it saw no workflow id.");
    }

    [TestMethod]
    public void WinAppRejectsAYieldWithNoWorkflowId()
    {
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
                try { proc.Kill(true); } catch { }
                return null;
            }

            return proc.ExitCode;
        }
        catch
        {
            return null;
        }
    }
}
