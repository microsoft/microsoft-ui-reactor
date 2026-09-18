using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.PackagedTests;

/// <summary>
/// Headless tests for the file lock that serialises packaged runs sharing one layout.
/// </summary>
/// <remarks>
/// <para>The lock is what stops a second run of this checkout from unregistering and
/// re-registering the package underneath a run already in progress. A normal packaged run is
/// the only run on its layout, so it never contends and would pass just as happily if the
/// "lock" granted everyone access — the failure is invisible from inside the tier, and only
/// appears as an unexplained mid-run identity change when two runs do overlap.</para>
/// <para>These tests therefore assert the two properties the lock is actually bought for:
/// a second acquisition is <b>refused</b> while the first is held, and it becomes available
/// again once released. The <c>FileShare.Read</c> choice is pinned too — a contender must be
/// able to read the owner record, because that record is the whole diagnostic when a run
/// fails to acquire.</para>
/// </remarks>
[TestClass]
public class PackagedLayoutLockTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Join(Path.GetTempPath(), "reactor-lock-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a leaked handle here must not mask the test's own verdict */ }
        catch (UnauthorizedAccessException) { }
    }

    private string LockPath => Path.Join(_root, "layout.lock");

    [TestMethod]
    public void A_Second_Acquisition_Is_Refused_While_The_First_Is_Held()
    {
        using var held = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=1");

        Assert.IsNotNull(held, "Precondition: the first acquisition must succeed.");
        Assert.IsNull(
            AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=2"),
            "A second run acquired the same layout lock while the first still held it, so " +
            "nothing prevents it from re-registering the package under a live run.");
    }

    [TestMethod]
    public void Releasing_The_Lock_Lets_The_Next_Run_Acquire_It()
    {
        var first = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=1");
        Assert.IsNotNull(first, "Precondition: the first acquisition must succeed.");
        first.Dispose();

        using var second = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=2");

        Assert.IsNotNull(second,
            "The lock stayed unavailable after its holder released it, so one finished run " +
            "would block every later run on this layout until the file was deleted by hand.");
    }

    /// <summary>
    /// The owner record must stay readable under contention: it is the only diagnostic a
    /// blocked run can report. Exercised through the production reader, because the share
    /// mode is the subtle part — a reader sharing only <c>Read</c> is refused outright.
    /// </summary>
    [TestMethod]
    public void A_Blocked_Contender_Can_Still_Read_The_Owner_Record()
    {
        const string Owner = "pid=4242 layout=C:\\checkout\\layout";

        using var held = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, Owner);
        Assert.IsNotNull(held, "Precondition: the first acquisition must succeed.");

        Assert.AreEqual(Owner, AppxLooseLayoutDeployment.ReadOwner(LockPath),
            "The lock file did not carry a readable owner record, so a run that fails to " +
            "acquire cannot say who is holding it.");
    }

    /// <summary>
    /// Re-stamping must not append. A lock file reused across runs would otherwise accumulate
    /// records and report a long-dead owner first.
    /// </summary>
    [TestMethod]
    public void Re_Acquiring_A_Released_Lock_Replaces_The_Owner_Record()
    {
        var first = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=1111");
        Assert.IsNotNull(first, "Precondition: the first acquisition must succeed.");
        first.Dispose();

        using var second = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=2222");
        Assert.IsNotNull(second, "Precondition: the second acquisition must succeed.");

        Assert.AreEqual("pid=2222", AppxLooseLayoutDeployment.ReadOwner(LockPath),
            "Re-acquiring appended instead of replacing, so the lock file names a dead owner " +
            "first and every later diagnostic is misleading.");
    }

    [TestMethod]
    public void Waiting_Gives_Up_And_Reports_Failure_When_The_Lock_Is_Never_Released()
    {
        using var held = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=1");
        Assert.IsNotNull(held, "Precondition: the first acquisition must succeed.");

        var started = DateTime.UtcNow;
        var waited = AppxLooseLayoutDeployment.WaitForLockFile(
            LockPath, "pid=2", TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(50));
        var elapsed = DateTime.UtcNow - started;

        Assert.IsNull(waited,
            "The wait reported success against a lock that was never released.");
        Assert.IsTrue(elapsed >= TimeSpan.FromMilliseconds(250),
            $"The wait returned after {elapsed.TotalMilliseconds:F0}ms without honouring its " +
            "300ms deadline, so it never actually retried.");
    }

    [TestMethod]
    public void Waiting_Succeeds_Once_The_Holder_Releases()
    {
        var held = AppxLooseLayoutDeployment.TryOpenLockFile(LockPath, "pid=1");
        Assert.IsNotNull(held, "Precondition: the first acquisition must succeed.");

        // Released on another thread while the wait is already polling, which is the
        // handover this loop exists for.
        var release = Task.Run(async () =>
        {
            await Task.Delay(100);
            held.Dispose();
        });

        using var acquired = AppxLooseLayoutDeployment.WaitForLockFile(
            LockPath, "pid=2", TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(25));

        release.GetAwaiter().GetResult();

        Assert.IsNotNull(acquired,
            "The wait never picked up a lock that was released well inside its deadline.");
    }
}
