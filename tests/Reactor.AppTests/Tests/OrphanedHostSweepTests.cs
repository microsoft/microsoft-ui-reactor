using Microsoft.UI.Reactor.AppTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// Headless tests for the startup sweep that removes hosts left behind by a previous run.
/// </summary>
/// <remarks>
/// <para>The regression these pin is the one that makes every other part of the concurrency
/// work moot: the sweep used to match on process name alone, so a suite starting in one
/// checkout killed the live host of a suite already running in another. Stamping a winapp UI
/// workflow id arbitrates the desktop between two runs, but arbitration is worthless if one
/// run terminates the other run's process before the first command is even issued.</para>
/// <para>Deliberately not derived from <c>AppTestBase</c>: nothing here launches a host or
/// touches the desktop, so it must not take a UI turn.</para>
/// </remarks>
[TestClass]
public class OrphanedHostSweepTests
{
    private static string Ours => Path.Join(Path.GetTempPath(), "checkout-a", "Reactor.AppTests.Host.exe");
    private static string Theirs => Path.Join(Path.GetTempPath(), "checkout-b", "Reactor.AppTests.Host.exe");

    private static OrphanedHostSweep.Candidate At(int pid, string? path) => new(pid, path);

    /// <summary>
    /// The regression itself. Two checkouts build a host with the same file name, so process
    /// name cannot distinguish them; only the image path can.
    /// </summary>
    [TestMethod]
    public void A_Host_From_Another_Checkout_Is_Not_Swept()
    {
        var selected = OrphanedHostSweep
            .SelectOurs([At(100, Ours), At(200, Theirs)], Ours)
            .Select(c => c.Pid)
            .ToList();

        CollectionAssert.AreEqual(new[] { 100 }, selected,
            "Only the host running this checkout's build may be killed.");
    }

    /// <summary>
    /// The sweep still has to do its job: a leftover host from a previous run of this same
    /// build holds the window title the next run binds to, so leaving it would trade one
    /// flake for another.
    /// </summary>
    [TestMethod]
    public void Our_Own_Orphan_Is_Still_Swept()
    {
        Assert.AreEqual(1, OrphanedHostSweep.SelectOurs([At(100, Ours)], Ours).Count());
    }

    /// <summary>
    /// Path comparison must survive the spellings the two sides actually produce: the launch
    /// path is composed by the test host, while the candidate path is whatever Windows reports
    /// for a running image.
    /// </summary>
    [TestMethod]
    public void Spelling_Differences_In_One_Path_Still_Match()
    {
        var unnormalized = Path.Join(Path.GetTempPath(), "checkout-a", ".", "Reactor.AppTests.Host.exe");
        var cased = Ours.ToUpperInvariant();

        Assert.AreEqual(1, OrphanedHostSweep.SelectOurs([At(100, unnormalized)], Ours).Count(),
            "A redundant path segment names the same image.");
        Assert.AreEqual(1, OrphanedHostSweep.SelectOurs([At(100, cased)], Ours).Count(),
            "Windows paths are case-insensitive.");
    }

    /// <summary>
    /// Fails closed. A candidate whose path could not be read is left alone, because killing
    /// on "don't know" is exactly the machine-wide behaviour this replaced.
    /// </summary>
    [TestMethod]
    public void A_Candidate_With_An_Unreadable_Path_Is_Left_Alone()
    {
        Assert.AreEqual(0, OrphanedHostSweep.SelectOurs([At(100, null), At(200, "  ")], Ours).Count());
    }

    /// <summary>
    /// Guards the argument that would silently turn the sweep back into a match-everything
    /// sweep if it were ever allowed through.
    /// </summary>
    [TestMethod]
    public void An_Empty_Executable_Path_Is_Rejected()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => OrphanedHostSweep.SelectOurs([At(100, Ours)], "  ").ToList());
    }
    /// <summary>
    /// Path scoping alone does not separate two runs of the <em>same</em> checkout: their hosts
    /// share the image path exactly. The run claim is what supplies liveness, and this is the
    /// differential that proves it — the same path claimed twice must be refused the second
    /// time, or the second run would classify the first run's live host as an orphan.
    /// </summary>
    [TestMethod]
    public void A_Second_Run_Of_The_Same_Build_Output_Cannot_Claim_It()
    {
        var exe = Path.Join(Path.GetTempPath(), "claim-probe", "Reactor.AppTests.Host.exe");

        using var first = OrphanedHostSweep.TryClaimRun(exe);
        Assert.IsNotNull(first, "The first run of a build output must be able to claim it.");

        using (var second = OrphanedHostSweep.TryClaimRun(exe))
        {
            Assert.IsNull(second,
                "A second concurrent run of the same build output must be refused, otherwise " +
                "it would sweep the first run's live host as an orphan.");
        }
    }

    /// <summary>
    /// The refusal must be scoped to the build output, not global: a run in another checkout
    /// has to keep sweeping its own leftovers.
    /// </summary>
    [TestMethod]
    public void A_Run_Of_A_Different_Build_Output_Claims_Independently()
    {
        var mine = Path.Join(Path.GetTempPath(), "claim-probe-a", "Reactor.AppTests.Host.exe");
        var theirs = Path.Join(Path.GetTempPath(), "claim-probe-b", "Reactor.AppTests.Host.exe");

        using var held = OrphanedHostSweep.TryClaimRun(mine);
        Assert.IsNotNull(held);

        using var other = OrphanedHostSweep.TryClaimRun(theirs);
        Assert.IsNotNull(other,
            "A different build output is a different claim; refusing it would stop other " +
            "checkouts from ever cleaning up after themselves.");
    }

    /// <summary>
    /// Releasing must actually release, or a crashed run would wedge every later run out of
    /// sweeping its own leftovers forever.
    /// </summary>
    [TestMethod]
    public void Releasing_A_Claim_Lets_The_Next_Run_Take_It()
    {
        var exe = Path.Join(Path.GetTempPath(), "claim-probe-release", "Reactor.AppTests.Host.exe");

        var first = OrphanedHostSweep.TryClaimRun(exe);
        Assert.IsNotNull(first);
        first.Dispose();

        using var second = OrphanedHostSweep.TryClaimRun(exe);
        Assert.IsNotNull(second,
            "A released claim must be reusable; a process exit closes the handle the same way.");
    }

    /// <summary>An unnamed build output cannot be arbitrated, so it must not be guessed at.</summary>
    [TestMethod]
    public void Claiming_Without_An_Executable_Path_Throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() => OrphanedHostSweep.TryClaimRun("  "));
    }
}