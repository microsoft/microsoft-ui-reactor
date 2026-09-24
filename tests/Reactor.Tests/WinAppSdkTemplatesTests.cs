// Unit coverage for the Windows App SDK `dotnet new` template pack helper
// (Templates/WinAppSdkTemplates.cs).
//
// Reactor's app templates moved out of this repo's own
// `Microsoft.UI.Reactor.ProjectTemplates` pack (`dotnet new reactorapp`) and
// into `Microsoft.WindowsAppSDK.WinUI.CSharp.Templates` (`dotnet new reactor`).
// bootstrap.ps1 §5, `mur upgrade`, and `mur doctor` all route through this
// helper.
//
// The load-bearing piece worth unit-testing is version *selection*. The install
// path can't just hand `dotnet new install` a bare package id: that command has
// no `--prerelease` switch and resolves stable-only, so it fails outright while
// the pack is publishing prereleases (it shipped 0.0.6-alpha before any stable).
// So we resolve a version ourselves and pass `<id>::<version>`. These tests pin
// the two rules that resolution has to get right:
//   1. prefer a stable version when one exists, and
//   2. otherwise fall back to the *newest* prerelease — numerically, not
//      lexically ("alpha.10" > "alpha.9").
//
// The network fetch and the `dotnet new install` invocation are not unit-tested
// (they hit nuget.org and the template engine); `mur templates status` and the
// bootstrap CI job cover those.

using Microsoft.UI.Reactor.Cli.Templates;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public sealed class WinAppSdkTemplatesTests
{
    [Fact]
    public void PackageId_is_the_windows_app_sdk_template_pack()
    {
        // Anchors the identity bootstrap.ps1 installs and `mur doctor` probes for.
        // A rename here silently turns the doctor check into a permanent FAIL.
        Assert.Equal("Microsoft.WindowsAppSDK.WinUI.CSharp.Templates", WinAppSdkTemplates.PackageId);
    }

    [Fact]
    public void ShortNames_are_the_canonical_name_of_each_template_in_the_pack()
    {
        // One canonical name per template (microsoft/WindowsAppSDK#6620), NOT every
        // registered short name: the shipped pack also registers a `winui-` alias
        // for each, plus `reactor-blank`, so nine names cover four templates.
        // Verified against the published 0.0.7-alpha listing.
        Assert.Equal(
            new[] { "reactor", "reactor-mvu", "reactor-navview", "reactor-tabview" },
            WinAppSdkTemplates.ShortNames);
        Assert.Equal("reactor", WinAppSdkTemplates.BlankShortName);
        Assert.Contains(WinAppSdkTemplates.BlankShortName, WinAppSdkTemplates.ShortNames);
    }













    // ── Installed-version parsing ──────────────────────────────────────────

    [Fact]
    public void InterpretInstalledVersionOutput_reads_the_version_for_this_pack()
    {
        // Verbatim shape of `dotnet new uninstall` with two packs installed — the
        // other pack's id is a PREFIX-adjacent name, which a substring match would
        // confuse with ours.
        const string listing = """
            Currently installed items:
               Microsoft.WindowsAppSDK.Templates
                  Version: 0.1.11-prerelease.100
                  Details:
                     Author: Microsoft
               Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
                  Version: 0.0.7-alpha
                  Details:
                     Author: Microsoft
            """;

        Assert.Equal("0.0.7-alpha", WinAppSdkTemplates.InterpretInstalledVersionOutput(listing));
    }

    [Fact]
    public void InterpretInstalledVersionOutput_returns_null_when_this_pack_is_absent()
    {
        const string listing = """
            Currently installed items:
               Microsoft.WindowsAppSDK.Templates
                  Version: 0.1.11-prerelease.100
            """;

        Assert.Null(WinAppSdkTemplates.InterpretInstalledVersionOutput(listing));
    }

    [Fact]
    public void InterpretInstalledVersionOutput_returns_null_when_no_version_line_follows()
    {
        // Malformed / truncated listing must not return a neighbouring package's version.
        const string listing = """
            Currently installed items:
               Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
                  Details:
                     Author: Microsoft
            """;

        Assert.Null(WinAppSdkTemplates.InterpretInstalledVersionOutput(listing));
    }

    [Fact]
    public void InterpretTemplateListOutput_requires_the_exact_short_name()
    {
        // Three traps in one fixture, all from the real listing shape:
        //   • '-' is a word boundary, so `\breactor\b` / Contains("reactor") also
        //     matches `reactor-mvu` and the real `winui-reactor-mvu` alias;
        //   • the Template Name column carries the capitalised prose word
        //     "Reactor" as a standalone token, so a case-insensitive token match
        //     passes too;
        //   • so does the Tags column, where "Reactor" is slash-delimited.
        // None of them means the blank `reactor` template is installed.
        const string withoutBlank = """
            These templates matched your input: 'reactor'

            Template Name                              Short Name                             Language  Tags
            -----------------------------------------  -------------------------------------  --------  ------------------------------------------
            Reactor MVU App (Experimental)             reactor-mvu,winui-reactor-mvu          [C#]      Windows/WinUI/Desktop/Reactor/Experimental
            """;

        Assert.False(WinAppSdkTemplates.InterpretTemplateListOutput(withoutBlank, exitCode: 0));
    }

    [Fact]
    public void InterpretTemplateListOutput_accepts_the_short_name_in_a_comma_list()
    {
        // Verbatim from the published 0.0.7-alpha pack, so the fixture is measured
        // rather than imagined. Three things have to survive: the comma-separated
        // alias column, the capitalised prose "Reactor" in the Template Name
        // column, and "Reactor" again inside the slash-delimited Tags column.
        const string withBlank = """
            These templates matched your input: 'reactor'

            Template Name                              Short Name                             Language  Tags
            -----------------------------------------  -------------------------------------  --------  ------------------------------------------
            Reactor Blank App (Experimental)           reactor,reactor-blank,winui-reactor    [C#]      Windows/WinUI/Desktop/Reactor/Experimental
            Reactor MVU App (Experimental)             reactor-mvu,winui-reactor-mvu          [C#]      Windows/WinUI/Desktop/Reactor/Experimental
            """;

        Assert.True(WinAppSdkTemplates.InterpretTemplateListOutput(withBlank, exitCode: 0));
    }











    [Fact]
    public void Bootstrap_does_not_advertise_dotnet_new_reactor_when_templates_are_skipped()
    {
        // Two false promises to avoid: -SkipTemplates leaves the pack uninstalled,
        // and a *verified-unavailable* pack (0.0.6-alpha predates the Reactor
        // templates) is installed but cannot scaffold. Printing the command in
        // either case hands the user something that fails immediately.
        var (path, text) = ReadRepoFile("bootstrap.ps1");
        var normalized = text.Replace("\r\n", "\n");
        var next = normalized[normalized.LastIndexOf("Write-Host 'Next:'", StringComparison.Ordinal)..];
        Assert.True(
            next.Contains("if ($SkipTemplates)", StringComparison.Ordinal),
            $"'{path}' must gate the `dotnet new reactor` next-step guidance on -SkipTemplates.");
        Assert.True(
            next.Contains("elseif (-not $templatesVerified)", StringComparison.Ordinal),
            $"'{path}' must also gate that guidance on the step-5 verification result.");
    }

    [Fact]
    public void Upgrade_reports_template_availability_without_installing()
    {
        // `mur upgrade` no longer installs the pack — `winapp` owns that — so the
        // guard is that it still *probes* and points somewhere useful, rather than
        // silently dropping the check or claiming to have refreshed anything.
        var (path, text) = ReadRepoFile(global::System.IO.Path.Join("src", "Reactor.Cli", "Upgrade", "UpgradeCommand.cs"));
        Assert.True(
            text.Contains("AreTemplatesAvailable()", StringComparison.Ordinal),
            $"'{path}' must still probe template availability during upgrade.");
        Assert.True(
            text.Contains("winapp new", StringComparison.Ordinal),
            $"'{path}' must point at `winapp new` when the templates are missing.");
        // The installer is gone; nothing here may call it back into existence.
        Assert.DoesNotContain("WinAppSdkTemplates.Install", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_does_not_treat_an_unusable_pack_as_an_install_failure()
    {
        // Regression: a non-zero result for an installed-but-unusable pack (or an
        // absent winapp) made bootstrap Fail before it ever reached the
        // verification and the gated guidance below it. Exit 2 must fall through.
        var (path, text) = ReadRepoFile("bootstrap.ps1");
        Assert.True(
            global::System.Text.RegularExpressions.Regex.IsMatch(
                text.Replace("\r\n", "\n"),
                @"\$templatesExit -ne 0 -and \$templatesExit -ne 2"),
            $"'{path}' must let exit 2 through to the verification step.");
    }

    [Theory]
    // Three distinct "cannot scaffold" situations that need different advice:
    // telling someone their pack "predates the Reactor templates" when it is not
    // installed at all — or when the probe itself failed — sends them to re-pin a
    // version that was never the problem.
    [InlineData(true, null, 0)]      // resolves
    [InlineData(null, null, 1)]      // template engine unreadable
    [InlineData(false, true, 2)]     // pack present, short name absent → too old
    [InlineData(false, false, 3)]    // pack absent
    [InlineData(false, null, 1)]     // package list unreadable → probe failure, not "too old"
    public void StatusExitCode_separates_unusable_missing_and_probe_failure(
        bool? available, bool? packageInstalled, int expected)
    {
        Assert.Equal(expected, TemplatesCommand.StatusExitCode(available, packageInstalled));
    }

    [Fact]
    public void Bootstrap_gives_different_advice_per_status_outcome()
    {
        // Regression: a single warning claiming "installed but too old" fired for
        // all three, including "not installed" and "probe failed".
        var (path, text) = ReadRepoFile("bootstrap.ps1");
        var normalized = text.Replace("\r\n", "\n");
        var start = normalized.IndexOf("$templatesVerified = ($statusExit -eq 0)", StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{path}' must derive $templatesVerified from the status exit code.");
        var block = normalized[start..];

        Assert.True(
            global::System.Text.RegularExpressions.Regex.IsMatch(block, @"switch \(\$statusExit\)"),
            $"'{path}' must branch on the status exit code rather than emitting one warning for every failure.");
        Assert.Contains("is not installed", block, StringComparison.Ordinal);
        Assert.Contains("Could not enumerate", block, StringComparison.Ordinal);
    }





    [Fact]
    public void InterpretPackageInstalledOutput_matches_the_id_line_exactly()
    {
        // A substring search over the transcript also fires on a longer id that
        // merely contains ours, and on the hint lines that echo it — reporting an
        // old pack as installed and choosing the wrong remediation.
        const string otherPackOnly = """
            Currently installed items:
               Microsoft.WindowsAppSDK.WinUI.CSharp.Templates.Extras
                  Version: 1.0.0
               Uninstall command:
                  dotnet new uninstall Microsoft.WindowsAppSDK.WinUI.CSharp.Templates.Extras
            """;
        Assert.False(WinAppSdkTemplates.InterpretPackageInstalledOutput(otherPackOnly));

        const string thisPack = """
            Currently installed items:
               Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
                  Version: 0.0.7-alpha
            """;
        Assert.True(WinAppSdkTemplates.InterpretPackageInstalledOutput(thisPack));
    }

    [Fact]
    public void Doctor_treats_an_unreadable_package_list_as_a_warning_not_a_missing_pack()
    {
        // `IsPackageInstalled()` returns null when the installed-package list
        // could not be read. Comparing it to `true` alone sent that case to the
        // "not registered" FAIL, telling the developer to reinstall a pack that
        // may be perfectly fine — the same ProbeFailed distinction StatusExitCode
        // already draws.
        var (path, text) = ReadRepoFile(global::System.IO.Path.Join(
            "src", "Reactor.Cli", "Doctor", "DoctorCommand.cs"));
        Assert.True(
            text.Contains("packageInstalled is null", StringComparison.Ordinal),
            $"'{path}' must handle an unreadable installed-package list separately from a missing pack.");
        Assert.False(
            text.Contains("IsPackageInstalled() == true", StringComparison.Ordinal),
            $"'{path}' must not collapse null (couldn't tell) into false (not installed).");
    }

    [Fact]
    public void Bootstrap_invokes_the_resolved_winapp_path_not_a_bare_command()
    {
        // The CLI is also accepted via its app-execution alias, which exists on
        // disk without always being resolvable through this process's PATH. A
        // bare `winapp` therefore fails on exactly the machines the alias
        // fallback exists to support.
        var (path, text) = ReadRepoFile("bootstrap.ps1");
        var normalized = text.Replace("\r\n", "\n");
        Assert.True(
            normalized.Contains("function Get-WinAppCliPath", StringComparison.Ordinal),
            $"'{path}' must resolve the winapp CLI to a concrete path.");
        Assert.True(
            global::System.Text.RegularExpressions.Regex.IsMatch(normalized, @"&\s*\$winAppExe\s+@winAppNewArgs"),
            $"'{path}' must invoke the resolved winapp path, not a bare `winapp`.");
    }

    // ── False-PASS guard: "pack installed" != "templates usable" ───────────
    //
    // Observed live during the de-stale merge: the machine had
    // Microsoft.WindowsAppSDK.WinUI.CSharp.Templates 0.0.6-alpha installed —
    // a version published *before* the Reactor templates were added. A
    // package-id-only probe reported `mur doctor` PASS and `mur templates
    // status` "installed", while `dotnet new list reactor` said
    // "No templates found matching: 'reactor'." and exited 103.
    //
    // The trap is that the not-found message itself contains the search term
    // ("...matching: 'reactor'" plus a "dotnet new search reactor" hint), so a
    // naive `output.Contains("reactor")` returns true exactly when the template
    // is missing. These pin the negative-marker-first rule.

    [Fact]
    public void InterpretTemplateListOutput_reports_unknown_for_an_engine_failure()
    {
        // `dotnet new list` exits 103 for "no templates matched" — an answer. Any
        // other non-zero exit is an SDK/engine failure, and there is no reason to
        // believe its stderr describes the template state. Reporting `false` there
        // makes `mur doctor` fail and bootstrap advise a reinstall on the strength
        // of an error it never parsed.
        const string engineError = """
            The command could not be loaded, possibly because of a missing SDK.
            """;

        Assert.Null(WinAppSdkTemplates.InterpretTemplateListOutput(engineError, exitCode: 1));

        // The two interpretable outcomes still answer definitively.
        Assert.False(WinAppSdkTemplates.InterpretTemplateListOutput(
            "No templates found matching: 'reactor'.", WinAppSdkTemplates.NoTemplatesFoundExitCode));
        Assert.True(WinAppSdkTemplates.InterpretTemplateListOutput(
            "Reactor Blank App   reactor,reactor-blank   [C#]", exitCode: 0));
    }

    [Fact]
    public void InterpretTemplateListOutput_trusts_the_not_found_marker_over_the_exit_code()
    {
        // The marker is authoritative when present: some SDKs have reported the
        // no-match message with a non-103 exit, and that is still a real answer.
        Assert.False(WinAppSdkTemplates.InterpretTemplateListOutput(
            "No templates found matching: 'reactor'.", exitCode: 1));
    }

    [Fact]
    public void InterpretTemplateListOutput_reports_missing_for_the_not_found_message()
    {
        // Verbatim output captured from `dotnet new list reactor` against an
        // installed-but-too-old 0.0.6-alpha pack. Note it mentions "reactor"
        // three times — a substring match on the short name would say "found".
        const string notFound = """
            No templates found matching: 'reactor'.

            To search for the templates on NuGet.org, run:
               dotnet new search reactor

            For details on the exit code, refer to https://aka.ms/templating-exit-codes#103
            """;

        Assert.False(WinAppSdkTemplates.InterpretTemplateListOutput(notFound, WinAppSdkTemplates.NoTemplatesFoundExitCode));
    }

    [Fact]
    public void InterpretTemplateListOutput_reports_available_for_a_real_listing()
    {
        // Shape of a real `dotnet new list reactor` table.
        const string listing = """
            These templates matched your input: 'reactor'

            Template Name                      Short Name                     Language  Tags
            ---------------------------------  -----------------------------  --------  -------------
            Reactor Blank App (Experimental)   reactor,reactor-blank          [C#]      Windows/WinUI
            Reactor MVU App (Experimental)     reactor-mvu                    [C#]      Windows/WinUI
            """;

        Assert.True(WinAppSdkTemplates.InterpretTemplateListOutput(listing, exitCode: 0));
    }

    [Fact]
    public void Doctor_probes_template_availability_not_just_package_presence()
    {
        // Source-level guard on the call site. The whole point of the fix is
        // that DoctorCommand asks "can the user scaffold?" — if it reverts to
        // the package-id probe for its PASS branch, the false PASS returns.
        var (path, text) = ReadRepoFile(global::System.IO.Path.Join(
            "src", "Reactor.Cli", "Doctor", "DoctorCommand.cs"));
        Assert.Contains("AreTemplatesAvailable()", text, StringComparison.Ordinal);
    }


    // ── Bootstrap wiring guards ────────────────────────────────────────────
    //
    // Reactor's app templates live in the Windows App SDK `dotnet new` pack
    // (`Microsoft.WindowsAppSDK.WinUI.CSharp.Templates`, short name `reactor`).
    // The in-repo `Microsoft.UI.Reactor.ProjectTemplates` pack that used to
    // provide `dotnet new reactorapp` has been deleted. These two tests pin both
    // halves of that contract — the regression they guard is silent, since a
    // bootstrap that quietly re-registered `reactorapp` would hand new
    // developers an unpackaged template the docs no longer describe.

    [Fact]
    public void Bootstrap_installs_the_windows_app_sdk_template_pack()
    {
        var (path, text) = ReadRepoFile("bootstrap.ps1");
        var normalized = text.Replace("\r\n", "\n");
        Assert.Contains("Microsoft.WindowsAppSDK.WinUI.CSharp.Templates", text, StringComparison.Ordinal);
        // `winapp new --list` installs the pack on demand; --use-defaults keeps an
        // already-installed one and never prompts, which is bootstrap's
        // install-if-missing (not reinstall) semantics on a non-interactive run.
        Assert.True(
            global::System.Text.RegularExpressions.Regex.IsMatch(
                normalized, @"'new',\s*'--list',\s*'--use-defaults'"),
            $"'{path}' must install the Reactor templates via `winapp new --list --use-defaults`, which " +
            "installs the Windows App SDK template pack on demand. `dotnet new install` has no --prerelease " +
            "switch and resolves stable-only, so installing the bare package id fails while the pack is " +
            "prerelease-only — that is the whole reason this does not shell out to `dotnet new install`.");
        // The in-repo installer is gone; bootstrap may not call it back.
        Assert.False(
            global::System.Text.RegularExpressions.Regex.IsMatch(normalized, @"templates',\s*'install'"),
            $"'{path}' must not call the removed `mur templates install`.");
    }

    [Fact]
    public void Bootstrap_does_not_install_the_removed_reactorapp_template()
    {
        // The in-repo ProjectTemplates pack was deleted; nothing in bootstrap may
        // resurrect it by handing the id to `dotnet new install`.
        var (path, text) = ReadRepoFile("bootstrap.ps1");
        var normalized = text.Replace("\r\n", "\n");

        // Strip comment lines: the step documents the migration in prose, and
        // those mentions must not trip this guard.
        var code = string.Join('\n', normalized
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("#", StringComparison.Ordinal)));

        Assert.False(
            global::System.Text.RegularExpressions.Regex.IsMatch(
                code, @"new\s+install.*Microsoft\.UI\.Reactor\.ProjectTemplates"),
            $"'{path}' must not `dotnet new install` Microsoft.UI.Reactor.ProjectTemplates — that package was " +
            "removed from this repo. Scaffolding goes through the Windows App SDK pack (`dotnet new reactor`).");
    }

    [Fact]
    public void Repo_no_longer_ships_the_in_repo_template_package()
    {
        // The deletion itself. Assert on the tracked *source* rather than the
        // directory: bin/obj under tools/Templates are gitignored, so a
        // contributor who built the project before pulling this change still has
        // the folder on disk. Checking Directory.Exists would fail for them while
        // nothing is actually wrong.
        var root = FindRoot();
        foreach (var relative in new[]
                 {
                     global::System.IO.Path.Join("tools", "Templates", "Microsoft.UI.Reactor.Templates.csproj"),
                     global::System.IO.Path.Join("tools", "Templates", "templates", "WinUIApp-CSharp", ".template.config", "template.json"),
                 })
        {
            Assert.False(
                global::System.IO.File.Exists(global::System.IO.Path.Join(root, relative)),
                $"'{relative}' is back. The in-repo Microsoft.UI.Reactor.ProjectTemplates package was removed " +
                "in favour of the Windows App SDK `dotnet new reactor` templates.");
        }

        var (relPath, release) = ReadRepoFile(global::System.IO.Path.Join(".github", "workflows", "release.yml"));
        Assert.False(
            release.Contains("Microsoft.UI.Reactor.Templates.csproj", StringComparison.Ordinal),
            $"'{relPath}' packs the removed template project again.");
    }

    static (string path, string text) ReadRepoFile(string repoRelativePath)
    {
        var path = global::System.IO.Path.Join(FindRoot(), repoRelativePath);
        Assert.True(global::System.IO.File.Exists(path), $"Expected '{path}' to exist; file moved or removed?");
        return (path, global::System.IO.File.ReadAllText(path));
    }

    static string FindRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !global::System.IO.File.Exists(global::System.IO.Path.Join(dir, "Reactor.slnx")))
            dir = global::System.IO.Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return dir!;
    }
}

// ── `mur templates install` argv parsing ──────────────────────────────────
//
// Only the branches that return *before* touching the machine are exercised:
// help, unknown option, missing value, and a bare positional. A real install is
// a global `dotnet new install`, which these tests must never trigger.
//
// Strict parsing is load-bearing: `--sorce ./pkgs` silently ignored would
// install from the configured feeds instead of the folder the user named, and
// the install would look successful.
[Collection("ConsoleTests")]
public sealed class TemplatesCommandArgvTests
{
    static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new global::System.IO.StringWriter();
        using var stderr = new global::System.IO.StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exitCode = TemplatesCommand.Run(args);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }



    [Fact]
    public void Unknown_subcommand_and_no_subcommand_both_show_help()
    {
        var (missing, missingOut, _) = Run();
        Assert.Equal(1, missing);
        Assert.Contains("mur templates", missingOut, StringComparison.Ordinal);

        var (unknown, _, unknownErr) = Run("instal");
        Assert.Equal(1, unknown);
        Assert.Contains("instal", unknownErr, StringComparison.Ordinal);
    }

    [Fact]
    public void Removed_install_subcommand_names_its_replacement()
    {
        // `install` is handled explicitly rather than falling into "unknown
        // subcommand": it existed, bootstrap and the docs called it, and anyone
        // with it in muscle memory or a script needs to be sent to `winapp new`
        // — not left hunting for a typo.
        var (exitCode, _, stderr) = Run("install");

        Assert.Equal(1, exitCode);
        Assert.Contains("has been removed", stderr, StringComparison.Ordinal);
        Assert.Contains("winapp new", stderr, StringComparison.Ordinal);
        // A bare "unknown subcommand 'install'" would be the unhelpful outcome.
        Assert.DoesNotContain("unknown subcommand", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_documents_the_status_exit_codes()
    {
        // bootstrap.ps1 branches on these, so they are contract, not cosmetics.
        var (exitCode, stdout, _) = Run("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("Exit codes:", stdout, StringComparison.Ordinal);
        Assert.Contains("winapp new", stdout, StringComparison.Ordinal);
        // The removed installer must not be advertised as an option any more.
        Assert.DoesNotContain("mur templates install", stdout, StringComparison.Ordinal);
    }
}
