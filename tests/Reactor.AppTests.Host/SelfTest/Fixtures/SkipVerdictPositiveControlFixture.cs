using System.Threading.Tasks;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Positive control for the three-state fixture verdict (issue #1061).
///
/// <para><b>Why this exists.</b> A fixture whose only TAP output is <c>H.Skip</c> used to be
/// reported <b>PASSED</b>: <c>Harness.Skip</c> emits a line beginning <c>ok </c>, and the parser
/// set its "did you assert anything?" flag for any such line — so the one signal meaning *I did
/// not assert* satisfied the one guard meaning *did you assert*. The fix added a real SKIPPED
/// verdict, spanning the Host (which decides a fixture asserted nothing) and the MSTest wrapper
/// (which reports it). The Host half is not reachable from any test project: both
/// <c>Reactor.SelfTests</c> and <c>Reactor.AppTests</c> reference this Host with
/// <c>ReferenceOutputAssembly=false</c>, so nothing can call <c>SelfTestRunner</c> directly. The
/// only way to exercise it is to be a fixture — this one.</para>
///
/// <para><b>What it proves, every run.</b> That the whole path still works end to end: the Host
/// notices zero assertions, paints the segment amber, writes
/// <c>&#35; Fully skipped fixture:</c> and counts it into <c>&#35; Total skipped fixtures:</c>;
/// and the wrapper parses the directive and reports SKIPPED rather than green. Its verdict is
/// asserted by <c>SelfTestBatch.SkippedFixtures_AreReported</c>, so if any link breaks — the Host
/// stops classifying, the <c>&#35; SKIP</c> literal drifts across the duplicated boundary, the
/// parser regresses — that test fails and names this fixture.</para>
///
/// <para><b>It is a control, so its behaviour is fixed on purpose.</b> It must skip
/// unconditionally and assert nothing: a control that can pass proves nothing on the runs where
/// it passes. This is the one fixture in the suite whose amber is the healthy result. Do not
/// "fix" it by giving it a check, and do not copy it as a template for a real fixture — see
/// <c>Harness.Skip</c> for the shape a real fixture should use (assert the precondition, then
/// skip only the leg that cannot be observed).</para>
/// </summary>
internal class SkipVerdictPositiveControl(Harness h) : SelfTestFixtureBase(h)
{
    /// <summary>
    /// The fixture name, shared with <c>SelfTestBatch</c> by convention only — the two projects
    /// do not share an assembly, so this literal is duplicated there. Drift is caught rather than
    /// prevented: the wrapper's assertion fails naming this fixture when it stops appearing.
    /// </summary>
    public const string FixtureName = "SelfTestVerdict_OnlySkips_PositiveControl";

    public override Task RunAsync()
    {
        H.Skip("SelfTestVerdict_OnlySkips",
            "positive control for issue #1061 - this fixture asserts nothing on purpose, so that " +
            "the SKIPPED verdict is proven to still work on every run");
        return Task.CompletedTask;
    }
}
