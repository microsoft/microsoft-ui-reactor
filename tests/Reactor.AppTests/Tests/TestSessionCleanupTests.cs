using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Reactor.AppTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Tests;

/// <summary>
/// Headless tests for the host-reaping helper that both session teardown and the bootstrap's
/// failure path rely on.
/// </summary>
/// <remarks>
/// <para>The bootstrap publishes <c>_appProcess</c>, <c>_app</c> and <c>_uia</c> only once all
/// three have been constructed, so a failure part-way through leaves a launched host that no
/// static refers to. Nothing else in the run will ever reap it: <c>ForceCleanup</c> reaps
/// <c>_appProcess</c>, which was never assigned. That host holds the liveness lease, and a held
/// lease defers the orphan sweep of every concurrent run of this checkout for as long as it
/// lives, so the reaping below is what stops one failed initialization stalling the machine.</para>
/// <para>These tests drive real processes rather than a fake, because the behaviour under test
/// is precisely the interaction with a live OS process — that it is killed, waited for, and that
/// the reference is cleared so a second call cannot touch a disposed object.</para>
/// </remarks>
[TestClass]
public sealed class TestSessionCleanupTests
{
    /// <summary>Starts a process that will outlive the test unless something kills it.</summary>
    /// <remarks>
    /// Deliberately without redirected standard input. An earlier version of this helper used
    /// <c>cmd /c pause</c> with a redirected stdin pipe, and that made the test vacuous: closing
    /// the pipe is part of <see cref="Process.Dispose"/>, so <c>pause</c> read EOF and the
    /// process exited whether or not the code under test had killed it. Mutation-testing caught
    /// it — deleting the <c>Kill</c> call left the test green. <c>ping</c> never reads stdin, so
    /// its only way out is being killed.
    /// </remarks>
    private static Process StartLongLivedProcess()
    {
        var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "ping.exe",
            Arguments = "-n 240 127.0.0.1",
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        Assert.IsNotNull(proc, "Could not start the probe process, so this test proves nothing.");
        Assert.IsFalse(
            proc.WaitForExit(500),
            "The probe process exited on its own, so a later 'it is dead' assertion would pass " +
            "whether or not the code under test killed anything.");

        return proc;
    }

    /// <summary>
    /// Proves the probe survives disposal alone, so "it is dead" can only mean it was killed.
    /// </summary>
    /// <remarks>
    /// This is the positive control for the helper above, kept as a test rather than a comment
    /// because the property it asserts is what makes the two tests below non-vacuous, and it is
    /// the exact property the first version of the probe silently lacked.
    /// </remarks>
    [TestMethod]
    public void TheProbeProcessOutlivesDisposalAlone()
    {
        var proc = StartLongLivedProcess();
        using var reaper = OpenPinnedHandle(proc.Id);

        try
        {
            proc.Dispose();

            Assert.IsFalse(
                IsGone(reaper, settleMs: 1000),
                "The probe exited from disposal alone, so KillAndDispose_TerminatesTheProcess " +
                "would pass even with the kill removed.");
        }
        finally
        {
            ForceKill(reaper);
        }
    }

    /// <summary>
    /// A host left behind by a failed initialization is killed, not merely dropped.
    /// </summary>
    [TestMethod]
    public void KillAndDispose_TerminatesTheProcess()
    {
        var proc = StartLongLivedProcess();
        var pid = proc.Id;
        using var reaper = OpenPinnedHandle(pid);

        try
        {
            Process? handle = proc;
            TestSession.KillAndDispose(ref handle);

            Assert.IsNull(handle, "The reference must be cleared so no later call can touch it.");

            // Asked of a separately owned handle rather than the disposed object, so the answer
            // comes from the OS rather than from cached state on a Process we just tore down -
            // and, because that handle has been open since before the kill, it is still about
            // this process and not whatever the id was recycled to.
            Assert.IsTrue(
                IsGone(reaper),
                $"PID {pid} is still running. A host abandoned by a failed bootstrap keeps the " +
                "liveness lease, which defers the orphan sweep of every concurrent run of this " +
                "checkout for as long as it lives.");
        }
        finally
        {
            ForceKill(reaper);
        }
    }

    /// <summary>
    /// Reaping something that has already exited is not an error, and still clears the field.
    /// </summary>
    /// <remarks>
    /// The bootstrap's failure path runs while an exception is in flight. If this threw, it
    /// would replace the exception that actually explains why initialization failed with a
    /// meaningless one from the cleanup.
    /// </remarks>
    [TestMethod]
    public void KillAndDispose_IsQuietWhenTheProcessAlreadyExited()
    {
        var proc = StartLongLivedProcess();
        proc.Kill();
        Assert.IsTrue(proc.WaitForExit(5000), "Setup failed: the probe process did not exit.");

        Process? handle = proc;
        TestSession.KillAndDispose(ref handle);

        Assert.IsNull(handle);
    }

    /// <summary>A null reference is a no-op, so the failure path need not check first.</summary>
    [TestMethod]
    public void KillAndDispose_AcceptsNull()
    {
        Process? handle = null;
        TestSession.KillAndDispose(ref handle);
        Assert.IsNull(handle);
    }

    /// <summary>
    /// A second, independently owned handle to the probe, used for every later question about
    /// it and for reaping it.
    /// </summary>
    /// <remarks>
    /// Holding an open handle keeps the process object alive as a zombie once it exits, which
    /// reserves its id for as long as this handle lives. That is what makes the id safe to act
    /// on: the tests dispose or kill the <em>first</em> <see cref="Process"/> as part of what
    /// they are testing, and re-opening the bare id afterwards would target whatever Windows had
    /// since given it to. Reaping is the dangerous half - it would kill a stranger - but the
    /// liveness question has the same flaw and would silently answer about the wrong process.
    /// <para>Touching <see cref="Process.SafeHandle"/> is what opens and retains the handle;
    /// <see cref="Process.GetProcessById"/> alone returns an object that owns nothing.</para>
    /// </remarks>
    private static Process OpenPinnedHandle(int pid)
    {
        var reaper = Process.GetProcessById(pid);
        _ = reaper.SafeHandle;
        return reaper;
    }

    private static bool IsGone(Process reaper, int settleMs = 5000)
    {
        // The process can take a moment to finish being torn down, so allow a short settle
        // rather than asserting on the first reading.
        var deadline = Environment.TickCount64 + settleMs;
        do
        {
            try
            {
                reaper.Refresh();
                if (reaper.HasExited) return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                return true;
            }

            Thread.Sleep(100);
        }
        while (Environment.TickCount64 < deadline);

        return false;
    }

    /// <summary>Reaps a probe the test itself is responsible for, so none outlive the run.</summary>
    private static void ForceKill(Process reaper)
    {
        try
        {
            reaper.Refresh();
            if (!reaper.HasExited)
            {
                reaper.Kill(entireProcessTree: true);
                reaper.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // The process exited on its own before or during the kill. This is a belt-and-braces
            // reaper for a probe the test already expects to have terminated, so both mean the
            // job is done.
        }
    }
}
