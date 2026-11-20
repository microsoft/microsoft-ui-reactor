using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.SelfTests;

/// <summary>
/// Issue #1534 — resolving a theme resource for the theme the app is not using must return that
/// theme's brush and must not change the brush for the rest of the app.
/// </summary>
/// <remarks>
/// <para>WinUI creates a theme dictionary entry the first time anything reads it. When code
/// read the entry for the other theme, WinUI resolved it for the app theme and kept that brush
/// for every element. Whether a run can see this depends on what touched the entry first, so the
/// in-process selftests cannot gate it: the Host's <c>--themeref-probe</c> mode does the read
/// first, in a fresh process, and reports per key.</para>
/// <para><see cref="NoFirstRead_Borders_Show_Both_Themes"/> is the positive control. With no
/// read, the opposite-theme and app-theme borders differ, so a pass elsewhere is a measurement
/// and not a probe that cannot tell the themes apart.
/// <see cref="Reading_The_Other_Themes_Dictionary_From_Code_Still_Changes_The_App"/> is the
/// negative control: the old read must still fail the probe.</para>
/// </remarks>
[TestClass]
public class ThemeRefProbeProcessTests
{
    // Mirrors ThemeRefProbe in Reactor.AppTests.Host; SelfTests references the Host for build
    // ordering only, so the contract is duplicated, as in ShutdownPolicyProcessLifetimeTests.
    private const string ProbeFlag = "--themeref-probe";
    private const int PassExitCode = 40;
    private const int FailExitCode = 41;
    private const int SkipExitCode = 42;
    private const string PassMarker = "THEMEREF-PASS";
    private const string FailMarker = "THEMEREF-FAIL";
    private const string SkipMarker = "THEMEREF-SKIP";
    private const string KeyMarker = "THEMEREF-KEY";
    private const int ExpectedKeys = 3;
    private const int ProbeTimeoutMs = 60_000;

    private static (string Stdout, int ExitCode, MatchCollection KeyLines, string Detail) RunProbe(string scenario)
    {
        var exe = HostProcess.FindHostExe();
        var args = $"{ProbeFlag} {scenario}";
        var (stdout, stderr, exitCode, timedOut) = HostProcess.Run(exe, args, ProbeTimeoutMs);
        var detail = $"Host: {exe} {args}\nexit={exitCode}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}";

        Assert.IsFalse(timedOut, $"Probe did not exit within {ProbeTimeoutMs}ms.\n{detail}");
        StringAssert.Contains(stdout, $"SCENARIO {scenario}", $"The probe did not run the scenario.\n{detail}");
        if (exitCode == SkipExitCode && stdout.Contains(SkipMarker, StringComparison.Ordinal))
            Assert.Inconclusive($"Not measured: a Windows contrast theme is on, so Light and Dark share brushes.\n{detail}");

        // Every key must have been measured: a missing border reads null and fails below, but a
        // probe that reported no keys at all would otherwise pass on the marker alone.
        var keyLines = Regex.Matches(stdout, $"^{KeyMarker} .*$", RegexOptions.Multiline);
        Assert.AreEqual(ExpectedKeys, keyLines.Count, $"Expected one line per key.\n{detail}");
        return (stdout, exitCode, keyLines, detail);
    }

    private static void AssertProbePasses(string scenario)
    {
        var (stdout, exitCode, keyLines, detail) = RunProbe(scenario);
        foreach (Match line in keyLines)
        {
            StringAssert.Contains(line.Value, "distinct=True",
                $"The opposite-theme border shows the app theme's brush: the read changed WinUI's entry for the whole app.\n{detail}");
            StringAssert.Contains(line.Value, "readOk=True",
                $"The read did not return the brush an element in that theme gets.\n{detail}");
        }

        StringAssert.Contains(stdout, PassMarker, detail);
        Assert.AreEqual(PassExitCode, exitCode, detail);
    }

    [TestMethod]
    public void NoFirstRead_Borders_Show_Both_Themes() => AssertProbePasses("none");

    /// <summary>
    /// Negative control: the read Reactor did before the fix must still change WinUI's entry. If
    /// a WinUI update or new Host startup code stops that, this fails, instead of the other tests
    /// passing without being able to see the bug.
    /// </summary>
    [TestMethod]
    public void Reading_The_Other_Themes_Dictionary_From_Code_Still_Changes_The_App()
    {
        var (stdout, exitCode, keyLines, detail) = RunProbe("raw-read");
        foreach (Match line in keyLines)
        {
            // Assert the poisoning itself, not just "not distinct": a border that was never
            // found also reads as not distinct. Both borders must show the same, measured brush,
            // and the code read must have returned it.
            var colors = Regex.Match(line.Value, @"read=(\S+) opposite=(\S+) app=(\S+)");
            Assert.IsTrue(colors.Success, $"Unexpected key line: {line.Value}\n{detail}");
            var (read, opposite, app) = (colors.Groups[1].Value, colors.Groups[2].Value, colors.Groups[3].Value);
            Assert.AreNotEqual("null", opposite, $"The opposite-theme border was not measured.\n{detail}");
            Assert.AreNotEqual("null", app, $"The app-theme border was not measured.\n{detail}");
            Assert.AreEqual(app, opposite,
                $"Reading the other theme's dictionary from code no longer changes WinUI's entry, so this probe can no longer detect issue #1534.\n{detail}");
            Assert.AreEqual(app, read, $"The code read did not return the app theme's brush.\n{detail}");
        }
        StringAssert.Contains(stdout, FailMarker, detail);
        Assert.AreEqual(FailExitCode, exitCode, detail);
    }

    [TestMethod]
    public void Resolve_For_Other_Theme_Returns_It_And_Keeps_App_Correct() => AssertProbePasses("resolve");

    [TestMethod]
    public void Resolve_For_Element_In_Other_Theme_Returns_It_And_Keeps_App_Correct() => AssertProbePasses("resolve-element");

    [TestMethod]
    public void Resource_Override_Under_Other_Theme_Returns_It_And_Keeps_App_Correct() => AssertProbePasses("resource-override");

    [TestMethod]
    public void Resolve_Of_A_Missing_Key_Keeps_App_Correct() => AssertProbePasses("missing-key");
}
