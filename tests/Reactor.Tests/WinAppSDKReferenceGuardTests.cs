using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Guards the Windows App SDK dependency policy (see <c>Directory.Build.targets</c>).
/// Repo projects must not reference the full <c>Microsoft.WindowsAppSDK</c>
/// metapackage directly: the correct package is injected centrally —
/// <c>Microsoft.WindowsAppSDK.WinUI</c> for framework-dependent libraries, WinUI +
/// <c>Microsoft.WindowsAppSDK.Runtime</c> for framework-dependent apps, and the full
/// metapackage only for self-contained / MSIX projects. A stray direct reference
/// bypasses that rule and re-drags the unused AI / ML / Widgets / DWrite slices back
/// into the dependency graph.
///
/// The scaffolded end-user template is exempt: it ships to external consumers who do
/// not inherit this repo's central injection, so it pins the metapackage itself.
/// </summary>
public class WinAppSDKReferenceGuardTests
{
    private const string Metapackage = "Microsoft.WindowsAppSDK";

    // Repo-root-relative, '/'-separated csproj paths allowed to reference the
    // Microsoft.WindowsAppSDK metapackage directly.
    private static readonly HashSet<string> Allowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        "tools/Templates/templates/WinUIApp-CSharp/Company.ReactorApp1.csproj",
    };

    /// <summary>
    /// Source-level guard: no repo project may declare a direct
    /// <c>Microsoft.WindowsAppSDK</c> PackageReference (parsed as XML so whitespace,
    /// quote style, casing, and <c>Update=</c> variants are all covered).
    /// </summary>
    [Fact]
    public void No_repo_project_references_the_WindowsAppSDK_metapackage_directly()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);

        var offenders = new List<string>();
        var unparseable = new List<string>();
        foreach (var csproj in EnumerateProjectFiles(root!))
        {
            var rel = Path.GetRelativePath(root!, csproj).Replace('\\', '/');
            if (Allowlist.Contains(rel))
            {
                continue;
            }

            XDocument doc;
            try
            {
                doc = XDocument.Load(csproj);
            }
            catch (global::System.Xml.XmlException ex)
            {
                // A malformed project file must fail the guard loudly rather than
                // slip through — otherwise a broken csproj could silently bypass
                // the metapackage policy this test enforces.
                unparseable.Add($"{rel} ({ex.Message})");
                continue;
            }

            if (ReferencesMetapackage(doc))
            {
                offenders.Add(rel);
            }
        }

        Assert.True(
            unparseable.Count == 0,
            "These project files could not be parsed as XML — fix the malformed csproj:\n  "
                + string.Join("\n  ", unparseable.OrderBy(x => x, StringComparer.Ordinal)));

        Assert.True(
            offenders.Count == 0,
            "These projects reference the Microsoft.WindowsAppSDK metapackage directly. "
                + "Remove the reference — Directory.Build.targets injects "
                + "Microsoft.WindowsAppSDK.WinUI (+ .Runtime for apps) or the metapackage "
                + "(self-contained / MSIX) centrally. Offenders:\n  "
                + string.Join("\n  ", offenders.OrderBy(x => x, StringComparer.Ordinal)));
    }

    /// <summary>
    /// Outcome-level guard: the shipped <c>Microsoft.UI.Reactor</c> library must
    /// resolve the lean WinUI sub-package and must NOT flow the full metapackage to
    /// consumers. Reads Reactor's restore graph (present because this test project
    /// references Reactor), so it catches a central-rule regression wherever it is
    /// introduced — including in <c>Directory.Build.*</c>, which the source scan above
    /// does not cover.
    /// </summary>
    [Fact]
    public void Reactor_library_resolves_WinUI_subpackage_not_the_metapackage()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);

        var assets = Path.Join(root!, "src", "Reactor", "obj", "project.assets.json");
        Assert.True(File.Exists(assets), $"Reactor restore graph not found at {assets}");

        using var doc = JsonDocument.Parse(File.ReadAllText(assets));
        var winAppSdk = doc.RootElement.GetProperty("libraries")
            .EnumerateObject()
            .Select(p => p.Name) // e.g. "Microsoft.WindowsAppSDK.WinUI/2.1.0"
            .Where(k => k.StartsWith("Microsoft.WindowsAppSDK", StringComparison.Ordinal))
            .ToList();

        Assert.Contains(winAppSdk, k => k.StartsWith("Microsoft.WindowsAppSDK.WinUI/", StringComparison.Ordinal));
        Assert.DoesNotContain(winAppSdk, k => k.StartsWith("Microsoft.WindowsAppSDK/", StringComparison.Ordinal));
    }

    /// <summary>
    /// The Windows App Runtime that <c>bootstrap.ps1</c> installs must be able to load
    /// what this repo builds. Windows App SDK 2.x ships ONE framework package per major
    /// (<c>Microsoft.WindowsAppRuntime.2</c>, named by the SDK's own
    /// <c>WindowsAppSDK-VersionInfo.json</c>), serviced 2.0 → 2.1 → 2.3 in place, and the
    /// major-only winget id tracks that servicing. The major.minor ids are *separate*
    /// winget packages pinned to a single servicing line: <c>…WindowsAppRuntime.2.0</c>
    /// still installs 2.0.1, which cannot satisfy an app built against 2.1.3. Hardcoding
    /// one let the id drift a whole minor behind <c>WindowsAppSDKVersion</c>, so the rule
    /// now lives in <c>tools/WindowsAppRuntimeId.ps1</c> — and this test is what keeps it
    /// honest, because the PowerShell script suites do not run when only
    /// <c>Directory.Build.props</c> changes.
    ///
    /// Note the expected id is derived here from the props file independently of the
    /// PowerShell implementation, so the two can disagree and this will catch it.
    /// </summary>
    [Fact]
    public void Bootstrap_derives_the_WindowsAppRuntime_winget_id_from_the_pinned_SDK_version()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);

        var props = File.ReadAllText(Path.Join(root!, "Directory.Build.props"));
        var pinned = Regex.Match(
            props, @"<WindowsAppSDKVersion>\s*([^<\s]+)\s*</WindowsAppSDKVersion>");
        Assert.True(pinned.Success, "WindowsAppSDKVersion not found in Directory.Build.props");

        var version = Regex.Match(pinned.Groups[1].Value, @"^(\d+)\.(\d+)");
        Assert.True(version.Success, $"Unparseable WindowsAppSDKVersion '{pinned.Groups[1].Value}'");

        var major = int.Parse(version.Groups[1].Value);
        var expectedId = major >= 2
            ? $"Microsoft.WindowsAppRuntime.{major}"
            : $"Microsoft.WindowsAppRuntime.{major}.{version.Groups[2].Value}";

        var probe = InvokeRuntimeIdHelper(
            root!,
            "$v = Get-PinnedWindowsAppSdkVersion -PropsPath $props; "
                + "[pscustomobject]@{ sdkVersion = [string]$v; derivedId = [string](Get-WindowsAppRuntimeWingetId $v) } | ConvertTo-Json -Compress");

        // The helper must read the same version the props file actually pins — this is
        // what fails if Get-PinnedWindowsAppSdkVersion regresses, which the id
        // comparison alone would not catch (a null version yields a null id, and a
        // null id makes bootstrap SKIP the runtime check rather than misreport it).
        Assert.Equal(pinned.Groups[1].Value, probe.RootElement.GetProperty("sdkVersion").GetString());
        Assert.Equal(expectedId, probe.RootElement.GetProperty("derivedId").GetString());

        // A literal id anywhere in bootstrap.ps1's executable code is the drift this
        // test exists to prevent: it is what silently decays when WindowsAppSDKVersion
        // moves. Comments are excluded (the probe filters them out via the PowerShell
        // tokenizer) because the scripts deliberately *explain* the 1.x/2.x id shapes;
        // documenting the trap is not falling into it.
        var literals = ScanExecutableRuntimeIdLiterals(root!, "bootstrap.ps1")
            .Where(v => v != expectedId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            literals.Count == 0,
            $"bootstrap.ps1 must derive the Windows App Runtime winget id from "
                + $"WindowsAppSDKVersion ({pinned.Groups[1].Value} -> {expectedId}), not hardcode it. "
                + "Found in executable code:\n  " + string.Join("\n  ", literals));
    }

    /// <summary>
    /// The mapping rule itself, exercised directly against the shipped helper.
    ///
    /// This is separate from the test above because that one can only ever see the version
    /// this repo currently pins: raising <c>WindowsAppSDKVersion</c> to an unreleased value
    /// to prove the derivation follows it fails the *restore*, not the assertion, so it
    /// measures nothing. These cases pin the two branches that matter — 1.x shipped a
    /// side-by-side framework package per minor, 2.x ships one per major and services it in
    /// place — without needing any of them to be restorable.
    /// </summary>
    [Theory]
    [InlineData("2.1.3", "Microsoft.WindowsAppRuntime.2")]   // what this repo pins today
    [InlineData("2.0.1", "Microsoft.WindowsAppRuntime.2")]   // older 2.x -> same framework package
    [InlineData("2.3.1", "Microsoft.WindowsAppRuntime.2")]   // newer 2.x -> same framework package
    [InlineData("3.0.0", "Microsoft.WindowsAppRuntime.3")]   // next major -> follows automatically
    [InlineData("1.7.250909003", "Microsoft.WindowsAppRuntime.1.7")] // 1.x stays major.minor
    [InlineData("1.8.251003001", "Microsoft.WindowsAppRuntime.1.8")]
    public void Bootstrap_maps_an_SDK_version_to_the_matching_runtime_winget_id(string sdkVersion, string expectedId)
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);

        using var probe = InvokeRuntimeIdHelper(
            root!,
            $"[pscustomobject]@{{ derivedId = [string](Get-WindowsAppRuntimeWingetId -SdkVersion '{sdkVersion}') }} | ConvertTo-Json -Compress");

        Assert.Equal(expectedId, probe.RootElement.GetProperty("derivedId").GetString());
    }

    /// <summary>
    /// A 2.x winget id names one package for the whole major, so its mere presence does
    /// not prove the installed runtime is new enough — 2.0.1 and 2.3.1 both report as
    /// <c>Microsoft.WindowsAppRuntime.2</c>. bootstrap.ps1 therefore compares versions,
    /// and this pins that comparison, including the part-count normalization
    /// (<c>[Version]'2.1.3'</c> has Revision -1 and sorts below <c>'2.1.3.0'</c>, which
    /// would report a perfectly good runtime as too old).
    /// </summary>
    [Theory]
    [InlineData("2.3.1.0", "2.1.3", true)]    // serviced forward
    [InlineData("2.1.3.0", "2.1.3", true)]    // exact match, differing part counts
    [InlineData("2.0.1.0", "2.1.3", false)]   // the defect: present but too old
    [InlineData("1.8.9.0", "2.1.3", false)]   // previous generation
    [InlineData("3.0.0.0", "2.1.3", true)]    // next major
    public void Bootstrap_only_accepts_a_runtime_new_enough_for_the_pinned_SDK(
        string installed, string requiredSdkVersion, bool expected)
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);

        using var probe = InvokeRuntimeIdHelper(
            root!,
            $"[pscustomobject]@{{ ok = [bool](Test-WindowsAppRuntimeSatisfied -Installed ([Version]'{installed}') -RequiredSdkVersion '{requiredSdkVersion}') }} | ConvertTo-Json -Compress");

        Assert.Equal(expected, probe.RootElement.GetProperty("ok").GetBoolean());
    }

    /// <summary>
    /// The version bootstrap reads out of <c>Directory.Build.props</c>. A regression here
    /// is silent in the worst way: a null version yields a null id, and a null id makes
    /// bootstrap skip the runtime check rather than misreport it. The commented-out case
    /// is why this is parsed as XML rather than scraped with a regex; the conditional
    /// case is why "unconditional" has to mean the whole ancestor chain, since MSBuild
    /// convention puts <c>Condition</c> on the enclosing <c>PropertyGroup</c>.
    /// </summary>
    [Fact]
    public void Bootstrap_reads_the_pinned_SDK_version_from_props_ignoring_comments()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);

        var dir = Directory.CreateTempSubdirectory("reactor-props-");
        try
        {
            var withComment = Path.Join(dir.FullName, "commented.props");
            File.WriteAllText(withComment, """
                <Project>
                  <!-- <WindowsAppSDKVersion>9.9.9</WindowsAppSDKVersion> -->
                  <PropertyGroup>
                    <WindowsAppSDKVersion>2.1.3</WindowsAppSDKVersion>
                  </PropertyGroup>
                </Project>
                """);

            // The decoy is unconditional on the element but sits in a conditional
            // PropertyGroup, and comes last — so anything that only inspects the
            // element's own attributes picks it.
            var conditional = Path.Join(dir.FullName, "conditional.props");
            File.WriteAllText(conditional, """
                <Project>
                  <PropertyGroup>
                    <WindowsAppSDKVersion>2.1.3</WindowsAppSDKVersion>
                  </PropertyGroup>
                  <PropertyGroup Condition="'$(UseExperimental)' == 'true'">
                    <WindowsAppSDKVersion>9.9.9</WindowsAppSDKVersion>
                  </PropertyGroup>
                </Project>
                """);

            var missing = Path.Join(dir.FullName, "missing.props");
            File.WriteAllText(missing, "<Project><PropertyGroup /></Project>");

            using var probe = InvokeRuntimeIdHelper(
                root!,
                $"[pscustomobject]@{{ "
                    + $"commented = [string](Get-PinnedWindowsAppSdkVersion -PropsPath '{withComment.Replace("\\", "\\\\")}'); "
                    + $"conditional = [string](Get-PinnedWindowsAppSdkVersion -PropsPath '{conditional.Replace("\\", "\\\\")}'); "
                    + $"missing = [string](Get-PinnedWindowsAppSdkVersion -PropsPath '{missing.Replace("\\", "\\\\")}'); "
                    + $"absent = [string](Get-PinnedWindowsAppSdkVersion -PropsPath '{Path.Join(dir.FullName, "nope.props").Replace("\\", "\\\\")}') "
                    + "} | ConvertTo-Json -Compress");

            Assert.Equal("2.1.3", probe.RootElement.GetProperty("commented").GetString());
            Assert.Equal("2.1.3", probe.RootElement.GetProperty("conditional").GetString());
            Assert.Equal(string.Empty, probe.RootElement.GetProperty("missing").GetString());
            Assert.Equal(string.Empty, probe.RootElement.GetProperty("absent").GetString());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Dot-sources <c>tools/WindowsAppRuntimeId.ps1</c> — the single home of the runtime
    /// identity rule, shared with bootstrap.ps1 and the bootstrap workflow — and runs
    /// <paramref name="body"/> against it. The helper is a standalone file precisely so it
    /// can be loaded without executing an install, which is why this no longer has to lift
    /// a function out of bootstrap.ps1 by AST.
    /// </summary>
    private static JsonDocument InvokeRuntimeIdHelper(string root, string body)
    {
        var helper = Path.Join(root, "tools", "WindowsAppRuntimeId.ps1");
        Assert.True(File.Exists(helper), $"Runtime id helper not found at {helper}");

        var script = $@"
$ErrorActionPreference = 'Stop'
. '{helper.Replace("\\", "\\\\")}'
$props = '{Path.Join(root, "Directory.Build.props").Replace("\\", "\\\\")}'
{body}
";
        var stdout = RunWindowsPowerShell(script);
        return JsonDocument.Parse(stdout);
    }

    /// <summary>
    /// Every <c>Microsoft.WindowsAppRuntime.&lt;n&gt;</c> token the named script would
    /// actually execute. Uses the PowerShell tokenizer rather than a regex over raw text
    /// so comments are excluded properly — including a <c>#</c> that appears inside a
    /// string — because the scripts deliberately document the id shapes they must not
    /// hardcode.
    /// </summary>
    private static IReadOnlyList<string> ScanExecutableRuntimeIdLiterals(string root, string relativePath)
    {
        var target = Path.Join(root, relativePath);
        var script = $@"
$ErrorActionPreference = 'Stop'
$text = [System.IO.File]::ReadAllText('{target.Replace("\\", "\\\\")}', [System.Text.Encoding]::UTF8)
$errors = $null
$tokens = $null
[System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$tokens, [ref]$errors) | Out-Null
if (@($errors).Count -ne 0) {{ throw '{relativePath} does not parse' }}
$literals = New-Object System.Collections.Generic.List[string]
foreach ($t in $tokens) {{
    if ($t.Kind -eq 'Comment') {{ continue }}
    foreach ($m in [regex]::Matches($t.Text, 'Microsoft\.WindowsAppRuntime\.[0-9][0-9.]*')) {{
        $literals.Add($m.Value) | Out-Null
    }}
}}
ConvertTo-Json -Compress -InputObject @{{ literals = @($literals | Sort-Object -Unique) }}
";
        using var json = JsonDocument.Parse(RunWindowsPowerShell(script));
        var arr = json.RootElement.GetProperty("literals");
        return arr.ValueKind switch
        {
            // ConvertTo-Json collapses a single-element array to a scalar.
            JsonValueKind.Array => arr.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList(),
            JsonValueKind.String => new List<string> { arr.GetString() ?? string.Empty },
            _ => new List<string>(),
        };
    }

    private static string RunWindowsPowerShell(string script)
    {
        // A hung child must not hang the whole suite: a machine-execution-policy
        // prompt or a broken profile can leave powershell.exe waiting on input
        // forever. Generous enough that a slow cold start cannot trip it.
        const int TimeoutMs = 120_000;

        var psi = new global::System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);

        using var proc = global::System.Diagnostics.Process.Start(psi)!;
        // Close stdin so anything that does try to read gets EOF instead of blocking.
        proc.StandardInput.Close();
        // Read both pipes concurrently: a child that fills one pipe's buffer while
        // the parent blocks on the other deadlocks.
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        if (!proc.WaitForExit(TimeoutMs))
        {
            // Every documented failure of Kill(entireProcessTree: true) is tolerable
            // here — the timeout is already the failure being reported, and a probe we
            // could not reap is not more informative than one we could. Caught
            // individually rather than as Exception so a genuinely unexpected type
            // still surfaces, and recorded rather than swallowed so the assertion can
            // say whether a stray process was left behind:
            //   InvalidOperationException — already exited, or no associated process
            //   Win32Exception           — could not be terminated, or is terminating
            //   AggregateException       — part of the tree survived
            global::System.Exception? killFailure = null;
            try
            {
                proc.Kill(entireProcessTree: true);
            }
            catch (global::System.InvalidOperationException ex)
            {
                killFailure = ex;
            }
            catch (global::System.ComponentModel.Win32Exception ex)
            {
                killFailure = ex;
            }
            catch (global::System.AggregateException ex)
            {
                killFailure = ex;
            }

            var outcome = killFailure is null
                ? "and was terminated"
                : $"and could not be terminated ({killFailure.GetType().Name}: {killFailure.Message}) — a stray process may remain";

            Assert.Fail(
                $"PowerShell probe did not exit within {TimeoutMs / 1000}s {outcome}. Script:\n{script}");
        }

        // WaitForExit(int) returns as soon as the process ends; the parameterless
        // overload is what guarantees the redirected streams have drained.
        proc.WaitForExit();

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        Assert.True(proc.ExitCode == 0, $"PowerShell probe failed (exit {proc.ExitCode}): {stderr}");
        return stdout;
    }

    private static bool ReferencesMetapackage(XDocument doc)
    {
        return doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Any(e =>
                AttributeEquals(e, "Include", Metapackage)
                || AttributeEquals(e, "Update", Metapackage));
    }

    private static bool AttributeEquals(XElement element, string name, string value)
    {
        var attr = element.Attribute(name)?.Value;
        return attr is not null && attr.Trim().Equals(value, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateProjectFiles(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            var norm = path.Replace('\\', '/');
            if (norm.Contains("/bin/", StringComparison.Ordinal)
                || norm.Contains("/obj/", StringComparison.Ordinal)
                || norm.Contains("/node_modules/", StringComparison.Ordinal)
                || norm.Contains("/.git/", StringComparison.Ordinal))
            {
                continue;
            }

            yield return path;
        }
    }
}
