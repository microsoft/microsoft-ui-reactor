// `mur check <path>` — fast-feedback wrapper around `dotnet build`.
//
// Goal: give an agent a small, structured error stream instead of a 500-line
// MSBuild dump. For each diagnostic we emit one line:
//
//   path:line:col  RXNNN  short message  → hint
//
// where `hint` is a skill-file pointer for known Reactor analyzer IDs (so the
// agent can read 5 lines of guidance instead of grepping the codebase).
//
// `<path>` defaults to `.` and accepts a directory, a .csproj, or a single
// .cs file (uses `dotnet build <file>` against the file-level header).

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Microsoft.UI.Reactor.Cli.Check;

public static class CheckCommand
{
    public static int Run(string[] args)
    {
        if (args.Any(a => a == "--help" || a == "-h"))
        {
            Console.Write(CheckArgs.HelpText);
            return 0;
        }

        if (!CheckArgs.TryParse(args, out var parsed, out var error))
        {
            Console.Error.WriteLine($"mur check: {error}");
            return 2;
        }

        var path = parsed.Path;
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Console.Error.WriteLine($"mur check: '{path}' not found.");
            return 1;
        }

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add(path);
        psi.ArgumentList.Add("--nologo");
        psi.ArgumentList.Add("-v:m");  // -v:q hides warnings
        // WinUI projects require an explicit Platform — match the host arch.
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            _ => null,
        };
        if (arch is not null) psi.ArgumentList.Add($"-p:Platform={arch}");

        using var proc = Process.Start(psi)!;
        // Drain both pipes concurrently — `dotnet build` can write enough to
        // either stream to fill its pipe buffer, so reading them sequentially
        // (stdout to end, then stderr) deadlocks when the unread one fills up.
        var stdOutTask = proc.StandardOutput.ReadToEndAsync();
        var stdErrTask = proc.StandardError.ReadToEndAsync();
        Task.WaitAll(stdOutTask, stdErrTask);
        proc.WaitForExit();
        var combined = stdOutTask.Result + "\n" + stdErrTask.Result;

        var projectRoot = ResolveProjectRoot(path);
        TraceWriter? trace = null;
        try
        {
            if (parsed.TracePath is not null)
            {
                trace = TraceWriter.Open(parsed.TracePath, projectRoot);
            }

            var diagnostics = ParseDiagnostics(combined);
            var orchestrator = new SuggesterOrchestrator();
            EmitDiagnostics(diagnostics, Console.Out, trace, diag => orchestrator.Suggest(diag, path));
            if (diagnostics.Count == 0 && proc.ExitCode == 0)
                Console.WriteLine("ok");
        }
        finally
        {
            trace?.Dispose();
        }

        return proc.ExitCode;
    }

    internal static List<Diag> ParseDiagnostics(string combinedOutput)
    {
        var lines = combinedOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var diagnostics = new List<Diag>();
        foreach (var raw in lines)
        {
            var d = Diag.Parse(raw);
            if (d is not null) diagnostics.Add(d);
        }
        return diagnostics;
    }

    internal static void EmitDiagnostics(IReadOnlyList<Diag> diagnostics, TextWriter stdout, TraceWriter? trace, Func<Diag, Suggestion?>? suggest = null)
    {
        // Dedupe — MSBuild often prints the same diagnostic twice (per project).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in diagnostics)
        {
            var key = $"{d.File}:{d.Line}:{d.Col}:{d.Code}";
            if (!seen.Add(key)) continue;
            var suggestion = suggest?.Invoke(d);
            stdout.WriteLine(d.Format(suggestion));
            trace?.Write(d);
            if (suggestion is not null) Telemetry.OnSuggestionEmitted(d.Code, suggestion);
        }
    }

    static string ResolveProjectRoot(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            if (File.Exists(full))
                return Path.GetDirectoryName(full) ?? full;
            return full;
        }
        catch
        {
            return Path.GetFullPath(".");
        }
    }

    internal sealed record Diag(string File, int Line, int Col, string Severity, string Code, string Message)
    {
        // MSBuild diagnostic line:
        //   path(line,col): error|warning CODE: message [project]
        static readonly Regex Pattern = new(
            @"^(?<file>[^()]+)\((?<line>\d+),(?<col>\d+)\):\s*(?<sev>error|warning|info)\s+(?<code>[A-Z][A-Z0-9_]*\d):\s*(?<msg>.+?)(?:\s*\[[^\]]+\])?\s*$",
            RegexOptions.Compiled);

        public static Diag? Parse(string raw)
        {
            var m = Pattern.Match(raw.Trim());
            if (!m.Success) return null;
            return new Diag(
                m.Groups["file"].Value.Trim(),
                int.Parse(m.Groups["line"].Value),
                int.Parse(m.Groups["col"].Value),
                m.Groups["sev"].Value,
                m.Groups["code"].Value,
                m.Groups["msg"].Value.Trim());
        }

        public string Format(Suggestion? suggestion = null)
        {
            var hint = HintFor(Code);
            var msg = Message.Length > 100 ? Message[..97] + "..." : Message;
            string suffix;
            if (hint is not null)
            {
                // Tier 1 (analyzer-ID hint table) wins ties (spec §9).
                suffix = "  → " + hint;
            }
            else if (suggestion is not null)
            {
                suffix = $"  → try: {suggestion.Text}  // [{suggestion.Evidence}]";
            }
            else
            {
                suffix = "";
            }
            return $"{File}:{Line}:{Col}  {Severity[..1].ToUpperInvariant()}  {Code}  {msg}{suffix}";
        }

        // Known Reactor analyzer IDs → skill-file pointer. Add entries as new
        // analyzers ship. Unknown codes (CS*, NU*, IDE*) get no hint.
        static string? HintFor(string code) => code switch
        {
            "REACTOR_HOOKS_001" => "SKILL.md §Hooks (call hooks unconditionally)",
            "REACTOR_HOOKS_004" => "SKILL.md §Hooks (memoize deps; never freshly allocated)",
            "REACTOR_HOOKS_005" => "SKILL.md §Hooks (only from Render or a Use* method)",
            "REACTOR_HOOKS_006" => "skills/async.md §1 (UseResource is reads-only — use UseMutation)",
            "REACTOR_THEME_001" => "skills/design.md §1 (use Theme tokens, not hex)",
            "REACTOR_THEME_002" => "skills/design.md §1 (use Theme tokens, not hex)",
            "REACTOR_A11Y_001"  => "skills/design.md §a11y (set AutomationName on icon-only controls)",
            "REACTOR_DSL_001"   => "SKILL.md gotcha #6 (.WithKey on dynamic list items)",
            _ => null,
        };
    }
}
