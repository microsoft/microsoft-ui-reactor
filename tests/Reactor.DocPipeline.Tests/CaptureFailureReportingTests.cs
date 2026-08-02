using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

/// <summary>
/// Pins that a per-screenshot capture failure is legible on <em>each stream on
/// its own</em>, rather than only when stdout and stderr are interleaved.
/// </summary>
/// <remarks>
/// <para>
/// <c>CaptureAsync</c> writes <c>"    Capturing &lt;id&gt;..."</c> to stdout with no
/// newline. The success path completes that line with <c>" ✓"</c> on the same
/// stream; the four failure paths used to complete it with <c>" ✗ ..."</c> on
/// <strong>stderr</strong>. Since the id is only ever written to stdout, a reader
/// of stderr alone saw <c>✗ no frame produced within deadline</c> with nothing
/// naming the screenshot — and a reader of stdout alone saw a
/// <c>Capturing &lt;id&gt;...</c> that never resolved.
/// </para>
/// <para>
/// The load-bearing assertion here is the <em>id on stderr</em>. Reverting
/// <see cref="ScreenshotCapture.ReportCaptureFailure"/> to the old
/// <c>Console.Error.WriteLine($" ✗ {detail}")</c> leaves the detail, the marker and
/// the failure count all intact and fails only that assertion — which is the whole
/// defect, so the test can come out the other way for exactly the right reason.
/// </para>
/// <para>
/// <see cref="Console"/> redirection is process-global, hence the shared
/// console-isolation collection.
/// </para>
/// </remarks>
[Collection("ConsoleTests")]
public class CaptureFailureReportingTests
{
    [Fact]
    public void Capture_failure_names_its_screenshot_on_stderr_and_closes_the_stdout_line()
    {
        var (stdout, stderr) = CaptureStreams(() =>
        {
            // Reproduces the real sequence: the progress line is opened on stdout,
            // then the failure is reported.
            Console.Write("    Capturing hero-shot...");
            ScreenshotCapture.ReportCaptureFailure("hero-shot", "no frame produced within deadline");
        });

        // stdout: the in-progress line is completed, so the next screenshot's
        // progress text cannot land on the same visual line.
        Assert.Contains("Capturing hero-shot...", stdout);
        Assert.EndsWith(Environment.NewLine, stdout);

        // stderr: self-contained. This is the assertion the old form fails —
        // it carried the detail but never the id.
        Assert.Contains("hero-shot", stderr);
        Assert.Contains("no frame produced within deadline", stderr);
    }

    /// <summary>
    /// Guards the premise of the test above: the id must reach stderr because
    /// <see cref="ScreenshotCapture.ReportCaptureFailure"/> puts it there, not
    /// because the stdout progress line leaked into the same buffer. Without this,
    /// a harness that merged the two streams would satisfy the assertions above
    /// while the defect was fully present.
    /// </summary>
    [Fact]
    public void The_id_reaches_stderr_even_with_no_stdout_progress_line()
    {
        var (stdout, stderr) = CaptureStreams(() =>
            ScreenshotCapture.ReportCaptureFailure("widget-thumb", "boom"));

        Assert.DoesNotContain("widget-thumb", stdout);
        Assert.Contains("widget-thumb", stderr);
        Assert.Contains("boom", stderr);
    }

    /// <summary>
    /// The exit race <see cref="ScreenshotCapture.TerminateProcessTree"/>'s first
    /// catch arm exists for: <c>HasExited</c> can raise rather than answer, and
    /// teardown must absorb that.
    /// </summary>
    /// <remarks>
    /// The premise is asserted first. If <c>HasExited</c> ever stops throwing on
    /// an unstarted process this test would pass without the filter ever being
    /// reached, which is the failure mode it is written to avoid — a guard test
    /// whose subject quietly stopped occurring.
    /// <para>
    /// Silence is the assertion, not merely "does not throw". Both catch arms
    /// swallow the throw, so a no-throw check alone cannot tell them apart and
    /// would survive deleting the filter entirely. Asserting the expected race
    /// produces <em>no console output</em> is what pins it to the first arm — and
    /// it is the behaviour that matters, since this runs on every successful
    /// capture. The arm does emit a <see cref="System.Diagnostics.Trace"/> line,
    /// which is deliberately not a console stream: absorbed is not the same as
    /// unrecorded, and this test is about what the user sees.
    /// </para>
    /// </remarks>
    [Fact]
    public void Teardown_absorbs_the_process_exit_race_silently()
    {
        using var never = new Process();

        Assert.Throws<InvalidOperationException>(() => _ = never.HasExited);

        var (stdout, stderr) = CaptureStreams(() =>
            ScreenshotCapture.TerminateProcessTree(never).GetAwaiter().GetResult());

        Assert.Equal(string.Empty, stderr);
        Assert.Equal(string.Empty, stdout);
    }

    /// <summary>
    /// Positive control for the test above. Absorbing exceptions is trivially
    /// satisfied by a method that does nothing, so this pins that teardown still
    /// performs the kill it is named for.
    /// </summary>
    /// <remarks>
    /// <strong>The elapsed-time bound is the assertion; <c>HasExited</c> alone is
    /// not.</strong> Removing the <c>Kill</c> call leaves <c>WaitForExitAsync</c>
    /// in place, which returns when the child finishes on its own — so the child
    /// <em>has</em> exited by the time teardown returns and a bare
    /// <c>Assert.True(HasExited)</c> passes on a mutant that kills nothing.
    /// Measured: that mutation ran green in <strong>59 s</strong> against a 174 ms
    /// baseline, and the only thing separating the two was the clock. The child is
    /// given a 60 s life precisely so "killed" and "waited out" are far apart, and
    /// the bound sits between them with room for a slow agent.
    /// </remarks>
    [Fact]
    public async Task Teardown_kills_a_process_that_is_still_running()
    {
        using var live = StartLongRunningChild();
        try
        {
            Assert.False(live.HasExited);

            var sw = Stopwatch.StartNew();
            await ScreenshotCapture.TerminateProcessTree(live);
            sw.Stop();

            Assert.True(live.HasExited);
            Assert.True(
                sw.Elapsed < ChildLifetime / 3,
                $"teardown returned after {sw.Elapsed.TotalSeconds:F1}s; the child "
                    + $"exits unaided after {ChildLifetime.TotalSeconds:F0}s, so this "
                    + "was a wait, not a kill");
        }
        finally
        {
            if (!live.HasExited) live.Kill(entireProcessTree: true);
        }
    }

    /// <summary>
    /// The second catch arm: an exception the filter does not admit is reported
    /// and not rethrown.
    /// </summary>
    /// <remarks>
    /// A null process raises <see cref="NullReferenceException"/>, which is
    /// deliberately outside the filter — it stands in for the disposed-handle and
    /// type-load faults that a bare <c>catch</c> would have made indistinguishable
    /// from the ordinary exit race. Both halves matter and fail for different
    /// mutations: a bare <c>catch { }</c> leaves stderr empty, and a rethrow (or
    /// widening the filter to <c>Exception</c>… then throwing) fails the call
    /// itself, which would discard an already-computed capture result.
    /// </remarks>
    [Fact]
    public void Teardown_reports_an_unexpected_fault_without_rethrowing()
    {
        var (stdout, stderr) = CaptureStreams(() =>
            ScreenshotCapture.TerminateProcessTree(null!).GetAwaiter().GetResult());

        Assert.Contains("capture teardown", stderr);
        Assert.Contains(nameof(NullReferenceException), stderr);
        Assert.DoesNotContain("capture teardown", stdout);
    }

    /// <summary>
    /// How long the child in <see cref="Teardown_kills_a_process_that_is_still_running"/>
    /// survives without intervention. Long enough that "teardown killed it" and
    /// "teardown waited for it" are separated by a wide, unambiguous margin.
    /// </summary>
    private static readonly TimeSpan ChildLifetime = TimeSpan.FromSeconds(60);

    private static Process StartLongRunningChild()
    {
        var seconds = (int)ChildLifetime.TotalSeconds;
        var psi = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", $"/c ping -n {seconds} 127.0.0.1")
            : new ProcessStartInfo("sleep", seconds.ToString());
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;

        var p = Process.Start(psi);
        Assert.NotNull(p);
        return p;
    }

    private static (string Stdout, string Stderr) CaptureStreams(Action body)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        try
        {
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            body();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        return (outWriter.ToString(), errWriter.ToString());
    }
}
