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
    /// one let the id drift a whole minor behind <c>WindowsAppSDKVersion</c>, so bootstrap
    /// now derives it — and this test is what keeps the derivation honest, because the
    /// PowerShell script suites do not run when only Directory.Build.props changes.
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

        var bootstrap = Path.Join(root!, "bootstrap.ps1");
        var probe = InspectBootstrapRuntimeId(bootstrap, pinned.Groups[1].Value);

        // A literal id is the drift this test exists to prevent: it is what silently
        // decays when WindowsAppSDKVersion moves. Comments are excluded (the probe
        // filters them out via the PowerShell tokenizer) because bootstrap.ps1
        // deliberately *explains* the 1.x/2.x id shapes; documenting the trap is not
        // falling into it. Anything the script would actually execute must match.
        var literals = probe.Literals
            .Where(v => v != expectedId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            literals.Count == 0,
            $"bootstrap.ps1 must derive the Windows App Runtime winget id from "
                + $"WindowsAppSDKVersion ({pinned.Groups[1].Value} -> {expectedId}), not hardcode it. "
                + "Found in executable code:\n  " + string.Join("\n  ", literals));

        // ...and the derivation itself has to produce that id. Run the shipped function
        // rather than restating its logic, so a broken edit to bootstrap.ps1 fails here.
        Assert.Equal(expectedId, probe.DerivedId);
    }

    /// <summary>
    /// The mapping rule itself, exercised directly against the shipped function.
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

        var probe = InspectBootstrapRuntimeId(Path.Join(root!, "bootstrap.ps1"), sdkVersion);
        Assert.Equal(expectedId, probe.DerivedId);
    }

    private sealed record BootstrapRuntimeIdProbe(string DerivedId, IReadOnlyList<string> Literals);

    /// <summary>
    /// Runs <c>Get-WindowsAppRuntimeWingetId</c> out of bootstrap.ps1 and reports every
    /// <c>Microsoft.WindowsAppRuntime.&lt;n&gt;</c> token the script would execute.
    /// bootstrap.ps1 cannot simply be dot-sourced — that would run the whole install — so
    /// the function is lifted out by name. The literal scan uses the PowerShell tokenizer
    /// rather than a regex over raw text so comments are excluded properly, including a
    /// <c>#</c> that appears inside a string.
    /// </summary>
    private static BootstrapRuntimeIdProbe InspectBootstrapRuntimeId(string bootstrapPath, string sdkVersion)
    {
        var script = $@"
$ErrorActionPreference = 'Stop'
$text = [System.IO.File]::ReadAllText('{bootstrapPath.Replace("\\", "\\\\")}', [System.Text.Encoding]::UTF8)
$errors = $null
$tokens = $null
$ast = [System.Management.Automation.Language.Parser]::ParseInput($text, [ref]$tokens, [ref]$errors)
if (@($errors).Count -ne 0) {{ throw 'bootstrap.ps1 does not parse' }}
$fn = $ast.Find({{ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Get-WindowsAppRuntimeWingetId' }}, $true)
if (-not $fn) {{ throw 'Get-WindowsAppRuntimeWingetId not found in bootstrap.ps1' }}
. ([scriptblock]::Create($fn.Extent.Text))
$literals = New-Object System.Collections.Generic.List[string]
foreach ($t in $tokens) {{
    if ($t.Kind -eq 'Comment') {{ continue }}
    foreach ($m in [regex]::Matches($t.Text, 'Microsoft\.WindowsAppRuntime\.[0-9][0-9.]*')) {{
        $literals.Add($m.Value) | Out-Null
    }}
}}
[pscustomobject]@{{
    derivedId = [string](Get-WindowsAppRuntimeWingetId -SdkVersion '{sdkVersion}')
    literals  = @($literals | Sort-Object -Unique)
}} | ConvertTo-Json -Compress
";
        var psi = new global::System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);

        using var proc = global::System.Diagnostics.Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        Assert.True(proc.ExitCode == 0, $"Derivation probe failed (exit {proc.ExitCode}): {stderr}");

        using var json = JsonDocument.Parse(stdout);
        var derived = json.RootElement.GetProperty("derivedId").GetString() ?? string.Empty;
        var literals = json.RootElement.TryGetProperty("literals", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList()
            : arr.ValueKind == JsonValueKind.String
                ? new List<string> { arr.GetString() ?? string.Empty }
                : new List<string>();

        return new BootstrapRuntimeIdProbe(derived, literals);
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
