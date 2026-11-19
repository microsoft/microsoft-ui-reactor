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
/// and not a probe that cannot tell the themes apart.</para>
/// </remarks>
[TestClass]
public class ThemeRefProbeProcessTests
{
    // Mirrors ThemeRefProbe in Reactor.AppTests.Host; SelfTests references the Host for build
    // ordering only, so the contract is duplicated, as in ShutdownPolicyProcessLifetimeTests.
    private const string ProbeFlag = "--themeref-probe";
    private const int PassExitCode = 40;
    private const string PassMarker = "THEMEREF-PASS";
    private const string KeyMarker = "THEMEREF-KEY";
    private const int ExpectedKeys = 3;
    private const int ProbeTimeoutMs = 60_000;

    private static void AssertProbePasses(string scenario)
    {
        var exe = HostProcess.FindHostExe();
        var args = $"{ProbeFlag} {scenario}";
        var (stdout, stderr, exitCode, timedOut) = HostProcess.Run(exe, args, ProbeTimeoutMs);
        var detail = $"Host: {exe} {args}\nexit={exitCode}\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}";

        Assert.IsFalse(timedOut, $"Probe did not exit within {ProbeTimeoutMs}ms.\n{detail}");
        StringAssert.Contains(stdout, $"SCENARIO {scenario} ", $"The probe did not run the scenario.\n{detail}");

        // Every key must have been measured: a missing border reads null and fails below, but a
        // probe that reported no keys at all would otherwise pass on the marker alone.
        var keyLines = Regex.Matches(stdout, $"^{KeyMarker} .*$", RegexOptions.Multiline);
        Assert.AreEqual(ExpectedKeys, keyLines.Count, $"Expected one line per key.\n{detail}");
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

    [TestMethod]
    public void Resolve_For_Other_Theme_Returns_It_And_Keeps_App_Correct() => AssertProbePasses("resolve");

    [TestMethod]
    public void Resolve_For_Element_In_Other_Theme_Returns_It_And_Keeps_App_Correct() => AssertProbePasses("resolve-element");

    [TestMethod]
    public void Resource_Override_Under_Other_Theme_Returns_It_And_Keeps_App_Correct() => AssertProbePasses("resource-override");
}
