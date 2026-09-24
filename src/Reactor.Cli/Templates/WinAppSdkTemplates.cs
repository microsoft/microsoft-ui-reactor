// The Windows App SDK `dotnet new` template pack, which is where Reactor's app
// templates now live.
//
// Reactor used to ship its own `Microsoft.UI.Reactor.ProjectTemplates` pack
// (`dotnet new reactorapp`, unpackaged). The Windows App SDK template pack now
// carries first-class Reactor templates alongside the WinUI 3 XAML ones. The
// legacy pack's source has been deleted from this repo and it is no longer
// built or published — the versions already on NuGet.org are all that remain,
// and they are deprecated.
//
// Two user-visible differences from the legacy `reactorapp` template:
//   • the short name is `reactor` (plus `reactor-mvu` / `reactor-navview` /
//     `reactor-tabview` for the richer shells), and
//   • scaffolded apps are **packaged** (single-project MSIX) rather than
//     unpackaged, so `dotnet run` launches them with package identity.
//
// This type only *probes* — it answers "is the pack installed?" and "will
// `dotnet new reactor` resolve?" for `mur doctor` and `mur templates status`.
// Installing is the Windows App SDK CLI's job: `winapp new` installs the pack
// on demand and scaffolds in one step. Reactor used to carry its own installer
// (`mur templates install`) because `dotnet new install` has no `--prerelease`
// switch and resolves stable-only, which fails while the pack is prerelease-only;
// working around that meant resolving versions off the NuGet flat container and
// tiptoeing around `dotnet new install --force`, which uninstalls the existing
// package *before* downloading the replacement. `winapp` handles all of it, so
// that machinery is gone.

using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Templates;
public static class WinAppSdkTemplates
{
    /// <summary>NuGet id of the template pack that ships the Reactor templates.</summary>
    public const string PackageId = "Microsoft.WindowsAppSDK.WinUI.CSharp.Templates";

    /// <summary>`dotnet new` short name of the blank Reactor template.</summary>
    public const string BlankShortName = "reactor";

    /// <summary>
    /// The canonical `dotnet new` short name of each Reactor template — one per
    /// template, for scaffold guidance.
    /// </summary>
    /// <remarks>
    /// Deliberately not every registered short name. Each template also carries a
    /// <c>winui-</c>-prefixed alias (and the blank one a <c>reactor-blank</c>
    /// alias), so the pack registers nine names for four templates. Listing all
    /// nine as "scaffold an app with" would be noise; callers that need to
    /// recognise an arbitrary alias should match the listing, not this array.
    /// </remarks>
    public static readonly string[] ShortNames =
    [
        "reactor",
        "reactor-mvu",
        "reactor-navview",
        "reactor-tabview",
    ];

    /// True when the template *package* is registered with the `dotnet new`
    /// engine. Returns null when the installed-package list could not be
    /// enumerated at all (no `dotnet` on PATH, engine error) so callers can
    /// distinguish "definitely missing" from "couldn't tell".
    /// </summary>
    /// <remarks>
    /// This answers "is the pack installed?", NOT "can I run `dotnet new
    /// reactor`?" — the pack shipped versions (e.g. 0.0.6-alpha) that predate
    /// the Reactor templates, so it can be installed and still not provide
    /// them. Use <see cref="AreTemplatesAvailable"/> for the user-facing
    /// question; this one exists to decide whether an install would be
    /// replacing something.
    /// </remarks>
    public static bool? IsPackageInstalled()
    {
        // `dotnet new uninstall` with no arguments lists installed template
        // *packages* by id. `dotnet new list` only shows template short names,
        // which can't tell our legacy `reactorapp` pack apart from the Windows
        // App SDK one when both happen to be installed.
        var output = RunCapture("new", "uninstall");
        if (output is null) return null;
        return InterpretPackageInstalledOutput(output);
    }

    /// <summary>
    /// Whether this pack's id appears as a listed package in `dotnet new uninstall`
    /// output. Split out (and internal) so the matching rule is testable.
    /// </summary>
    /// <remarks>
    /// Matches the id line exactly rather than searching the transcript: a
    /// substring hit also fires on a longer id that merely contains ours, and on
    /// the uninstall hint lines that echo the id. Either would report an old pack
    /// as installed and send the caller to the wrong remediation.
    /// </remarks>
    internal static bool InterpretPackageInstalledOutput(string output) =>
        output
            .Split('\n')
            .Any(line => string.Equals(line.Trim(), PackageId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// True when `dotnet new reactor` will actually resolve — i.e. the blank
    /// Reactor template short name is registered. Returns null when the
    /// template engine could not be queried at all.
    /// </summary>
    /// <remarks>
    /// Checking the package id alone is a false PASS: `dotnet new list reactor`
    /// reports "No templates found" against an installed-but-too-old pack, so a
    /// package-only probe tells a developer they're ready to scaffold when the
    /// very next command fails. Ask the engine the question the user cares about.
    /// </remarks>
    public static bool? AreTemplatesAvailable()
    {
        // `dotnet new list <name>` exits 103 and prints "No templates found
        // matching" when nothing matches. Any *other* non-zero exit is an engine
        // or SDK failure, not an answer — reporting it as "definitely missing"
        // sends `mur doctor` and bootstrap to the wrong remediation.
        var (output, exitCode) = RunCaptureWithExit("new", "list", BlankShortName);
        if (output is null) return null;
        return InterpretTemplateListOutput(output, exitCode);
    }

    /// <summary>
    /// Interprets `dotnet new list reactor` output. Split out (and internal) so
    /// the rule is unit-testable without shelling out to the template engine.
    /// </summary>
    /// <param name="exitCode">
    /// The template engine's exit code. 0 is a listing; 103 is its documented
    /// "no templates matched". Anything else is a failure we cannot interpret,
    /// so the answer is null ("couldn't tell") rather than false.
    /// </param>
    internal static bool? InterpretTemplateListOutput(string output, int exitCode)
    {
        // The negative marker is authoritative when the engine reported a
        // no-match, and is checked first because the message repeats the search
        // term (and prints a `dotnet new search reactor` hint), so a naive
        // short-name match reports the template as present exactly when absent.
        if (output.Contains("No templates found", StringComparison.OrdinalIgnoreCase))
            return false;

        // A non-zero exit without that marker is an engine/SDK error: say
        // "couldn't tell" instead of inventing a definite answer from its stderr.
        if (exitCode != 0 && exitCode != NoTemplatesFoundExitCode)
            return null;

        return MatchesBlankShortName(output);
    }

    /// <summary>
    /// `dotnet new list` exit code for "no templates matched the input" — the one
    /// non-zero result that is an answer rather than a failure.
    /// </summary>
    internal const int NoTemplatesFoundExitCode = 103;

    /// <summary>
    /// Whether the listing registers <see cref="BlankShortName"/> as a short name.
    /// </summary>
    internal static bool MatchesBlankShortName(string output)
    {
        // Match the short name as a whole token. A plain Contains (or a \b regex)
        // also matches `reactor-mvu` and `winui-reactor`, because '-' is a word
        // boundary — so a listing that has the richer shells but not the blank
        // template would be read as success. Short names are comma-separated
        // within a whitespace-delimited column, so require one of those delimiters
        // on each side.
        //
        // Deliberately case-sensitive: the Template Name column carries the
        // capitalised prose word ("Reactor MVU App"), which is a standalone token
        // and would match case-insensitively even when the blank template is
        // absent. Short names are lowercase and BlankShortName is a constant.
        return Regex.IsMatch(output, BlankShortNameTokenPattern);
    }

    /// <summary>
    /// <see cref="BlankShortName"/> as a whole token: preceded and followed by a
    /// comma, whitespace, or the edge of the text.
    /// </summary>
    private static readonly string BlankShortNameTokenPattern =
        @"(?<![^\s,])" + Regex.Escape(BlankShortName) + @"(?![^\s,])";

    /// <summary>
    /// The installed version of the template pack, or null when it isn't
    /// installed (or the listing couldn't be read).
    /// </summary>
    public static string? GetInstalledVersion()
    {
        var output = RunCapture("new", "uninstall");
        return output is null ? null : InterpretInstalledVersionOutput(output);
    }

    /// <summary>
    /// Pulls this pack's version out of `dotnet new uninstall` output. Split out
    /// (and internal) so the parser is testable against captured CLI text without
    /// invoking the template engine.
    /// </summary>
    internal static string? InterpretInstalledVersionOutput(string output)
    {
        // The listing indents each package id, then its metadata:
        //     Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
        //        Version: 0.0.6-alpha
        // Several packages can be listed, so match the id line exactly rather than
        // by substring — other ids legitimately contain this one as a prefix.
        var lines = output.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Trim().Equals(PackageId, StringComparison.OrdinalIgnoreCase))
                continue;

            for (var j = i + 1; j < lines.Length && j <= i + 4; j++)
            {
                var trimmed = lines[j].Trim();
                if (trimmed.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
                    return trimmed["Version:".Length..].Trim();
            }
            return null;
        }
        return null;
    }

    static string? RunCapture(params string[] arguments) => RunCaptureWithExit(arguments).Output;

    /// <summary>
    /// Runs `dotnet <paramref name="arguments"/>` and returns its combined output
    /// plus exit code. Output is null when the process could not be started.
    /// </summary>
    /// <remarks>
    /// The exit code is returned rather than swallowed because `dotnet new list`
    /// uses it to distinguish "no templates matched" (103) from an engine
    /// failure, and collapsing the two turns a probe failure into a confident
    /// wrong answer. `dotnet new uninstall` meanwhile exits non-zero when nothing
    /// is installed while still printing a usable listing, so callers that only
    /// want the text keep ignoring it.
    /// </remarks>
    static (string? Output, int ExitCode) RunCaptureWithExit(params string[] arguments)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return (null, -1);
            // Drain both pipes concurrently. Reading stdout to the end first lets
            // `dotnet new` fill the unread stderr pipe and block before it exits —
            // a deadlock, not a slow path. CheckCommand documents the same hazard.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            global::System.Threading.Tasks.Task.WaitAll(stdoutTask, stderrTask);
            proc.WaitForExit();
            return (stdoutTask.Result + stderrTask.Result, proc.ExitCode);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return (null, -1);
        }
    }
}
