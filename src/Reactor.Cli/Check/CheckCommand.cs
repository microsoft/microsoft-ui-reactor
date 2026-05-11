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
// `<path>` defaults to `.` and accepts a .csproj or a directory containing
// one. (`dotnet build` does not accept a bare .cs file as a target, so we
// don't either; CompilationLoader's per-file walk-up is for tooling/test
// seams only.)

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.Cli.Check.Rules;

namespace Microsoft.UI.Reactor.Cli.Check;

public static class CheckCommand
{
    // Spec 038 §11 risk row + §14 #8 — diagnostic-count gate.
    //
    // EC1 5×N (2026-05-10) showed Tier-2 setup overhead (~5–8s per `mur check`)
    // does not amortize on ~150-LoC projects: calc regressed +21% cost while
    // kanban won −24%. The gate suppresses Tier-2 suggestions when the
    // invocation surfaces fewer than this many CS-prefixed diagnostics — a
    // proxy for "this is a small/simple build the agent can resolve unaided."
    //
    // Conservative initial value picked against the EC1 data with a small
    // observational sample of failing builds per arm. Revisit at Data
    // Checkpoint C (≥ 500 pairs) when the full diagnostic-count distribution
    // by project size is known. Override via `--suggest-threshold <N>`; 0 = no
    // gate.
    internal const int DefaultSuggestThreshold = 3;

    public static int Run(string[] args)
    {
        if (args.Any(a => a == "--help" || a == "-h"))
        {
            Console.Write(CheckArgs.HelpText);
            return 0;
        }

        if (!ArgsParser.TryParse(args, out var parsed, out var error))
        {
            Console.Error.WriteLine($"mur check: {error}");
            return 2;
        }

        if (parsed.ListRules)
        {
            // --list-rules short-circuits before `dotnet build`: this is a
            // pure introspection of the registered ruleset. We attempt to
            // resolve targets against the compilation if the path resolves
            // to a buildable project, but fall through to a no-compilation
            // listing when it doesn't — `mur check --list-rules` works in any
            // directory.
            PrintRuleList(parsed);
            return 0;
        }

        var path = parsed.Path;
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            Console.Error.WriteLine($"mur check: '{path}' not found.");
            return 1;
        }

        // --disable-rule references that don't match any registered rule are
        // surfaced as warnings (not errors) so a typo doesn't fail a build,
        // but the agent / human sees the miss and can correct it.
        WarnOnUnknownDisabledRules(parsed.DisabledRules);

        // EffectiveBuildArgs already has default-merging applied — `--nologo`,
        // `-v:m`, and `-p:Platform={host arch}` are injected by ArgsParser
        // only if the user didn't supply the same flag in passthrough. See
        // spec 038 §8.
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in parsed.EffectiveBuildArgs) psi.ArgumentList.Add(arg);

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
                var modeTag = parsed.Mode.ToString().ToLowerInvariant();
                trace = TraceWriter.Open(parsed.TracePath, projectRoot, modeTag);
                // Spec 038 §8 "Tracing": write the effective dotnet build
                // command line first so replays can reproduce the invocation
                // even if default-merging changes between mur versions.
                trace.WriteCommand(parsed.EffectiveBuildArgs, modeTag);
            }

            var diagnostics = ParseDiagnostics(combined);

            // Spec 038 §8 — pre-emit ranker. Drop diagnostics that score
            // below the active threshold for the current mode (iteration:
            // 0.6, final: 0.0, etc., overridable via --emit-threshold). The
            // filter wraps stdout emission only; trace output is unaffected
            // — every parsed diagnostic is recorded so replays / mining /
            // suppressed-then-resurfaced telemetry (spec §8 "failure modes")
            // can see what was suppressed.
            var rankerCtx = new Ranker.RankerContext(parsed.Mode, parsed.EmitThreshold);
            Func<Diag, bool> stdoutFilter = d => Ranker.Ranker.ShouldEmit(d, rankerCtx);

            // Suggest-gate counts the FULL parsed list — not the post-ranker
            // emittable list. The gate's question (per spec §14 #8) is "is
            // this build complex enough to benefit from Tier-2 help"; that's
            // a property of what the compiler emitted, not of what stdout
            // shows. Counting against `emittable` over-suppresses: a build
            // surfacing 2 CS errors + 3 CS8602 nullable warnings has 5
            // unique CS codes — Tier-2 territory — but the ranker filters
            // CS8602 out of emittable, dropping the count to 2 and closing
            // the gate. EC2 (n=3) measured exactly this: kanban-variant
            // Tier-2 firing went from 80% under EC1 to 0% under the bugged
            // gate, costing the agent ~4 turns of manual name resolution.
            var effectiveThreshold = parsed.SuggestThreshold ?? DefaultSuggestThreshold;
            Func<Diag, Suggestion?>? suggest = null;
            if (ShouldEmitSuggestions(diagnostics, effectiveThreshold))
            {
                // Load the CSharpCompilation once for the whole invocation so
                // CompilationLoader.Load — which re-enumerates `.cs` files and
                // recomputes the file-set hash on every call — runs O(1) per
                // mur check, not O(diagnostics). Cache-hit on the second
                // invocation still benefits.
                Microsoft.CodeAnalysis.CSharp.CSharpCompilation? compilation = null;
                try { compilation = CompilationLoader.Instance.Load(path); }
                catch { /* loader is best-effort; fall through to no-suggest */ }
                if (compilation is not null && !ReferenceEquals(compilation, CompilationLoader.EmptyCompilation))
                {
                    var disabled = ToDisabledSet(parsed.DisabledRules);
                    // Self-disabled trace hook (spec 038 §3.1a residual).
                    // Dedup per-invocation: the registry calls back on every
                    // BestMatch invocation a rule's targets fail to resolve,
                    // but we only want one row per rule per `mur check` run.
                    // No trace open = no callback wired; stdout stays clean.
                    Action<string, string>? onRuleSelfDisabled = null;
                    if (trace is not null)
                    {
                        var traceRef = trace;
                        var reported = new HashSet<string>(StringComparer.Ordinal);
                        onRuleSelfDisabled = (name, target) =>
                        {
                            if (reported.Add(name))
                                traceRef.WriteRuleSelfDisabled(name, target);
                        };
                    }
                    var orchestrator = new SuggesterOrchestrator(
                        rules: RuleRegistry.Default,
                        disabledRules: disabled,
                        onRuleSelfDisabled: onRuleSelfDisabled);
                    suggest = diag => orchestrator.SuggestAgainst(diag, compilation);
                }
            }
            EmitDiagnostics(diagnostics, Console.Out, trace, suggest, stdoutFilter);
            if (diagnostics.Count == 0 && proc.ExitCode == 0)
                Console.WriteLine("ok");
        }
        finally
        {
            trace?.Dispose();
        }

        return proc.ExitCode;
    }

    /// <summary>
    /// Spec 038 §11 / §14 #8: gate Tier-2 suggestions by per-invocation
    /// CS-prefixed diagnostic count. Returns true if suggestions should run,
    /// false to skip the suggester for this invocation. Threshold 0 disables
    /// the gate. Counts unique (file, line, col, code) tuples — same dedup
    /// rule EmitDiagnostics applies — so MSBuild's per-project repeats don't
    /// inflate the count.
    /// </summary>
    internal static bool ShouldEmitSuggestions(IReadOnlyList<Diag> diagnostics, int threshold)
    {
        if (threshold <= 0) return true;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var count = 0;
        foreach (var d in diagnostics)
        {
            if (!d.Code.StartsWith("CS", StringComparison.Ordinal)) continue;
            var key = $"{d.File}:{d.Line}:{d.Col}:{d.Code}";
            if (!seen.Add(key)) continue;
            count++;
            if (count >= threshold) return true;
        }
        return false;
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

    internal static void EmitDiagnostics(IReadOnlyList<Diag> diagnostics, TextWriter stdout, TraceWriter? trace, Func<Diag, Suggestion?>? suggest = null, Func<Diag, bool>? stdoutFilter = null)
    {
        // Dedupe — MSBuild often prints the same diagnostic twice (per project).
        // The dedup pass is shared between stdout and trace, so the trace
        // never carries a duplicate row even when the ranker suppresses one
        // copy of a duplicate pair (the second copy hits the seen-set first).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in diagnostics)
        {
            var key = $"{d.File}:{d.Line}:{d.Col}:{d.Code}";
            if (!seen.Add(key)) continue;
            // Trace gets every unique parsed diagnostic, regardless of the
            // ranker (spec §0.3 + §8 "Failure modes the ranker must not
            // introduce" — suppressed-but-real diagnostics must be mineable).
            trace?.Write(d);
            if (stdoutFilter is not null && !stdoutFilter(d)) continue;
            var suggestion = suggest?.Invoke(d);
            stdout.WriteLine(d.Format(suggestion));
            if (suggestion is not null) Telemetry.OnSuggestionEmitted(d.Code, suggestion);
        }
    }

    static ISet<string>? ToDisabledSet(IReadOnlyList<string> disabledRules)
    {
        if (disabledRules.Count == 0) return null;
        return new HashSet<string>(disabledRules, StringComparer.Ordinal);
    }

    static void WarnOnUnknownDisabledRules(IReadOnlyList<string> disabledRules)
    {
        if (disabledRules.Count == 0) return;
        var registry = RuleRegistry.Default;
        foreach (var name in disabledRules)
        {
            if (!registry.TryGet(name, out _))
                Console.Error.WriteLine($"mur check: --disable-rule '{name}' does not match any registered rule (use --list-rules to see available rules).");
        }
    }

    static void PrintRuleList(CheckArgs parsed)
    {
        var registry = RuleRegistry.Default;
        // If the path resolves to a buildable project, attempt target
        // resolution so the listing distinguishes Enabled from
        // SelfDisabled-due-to-unresolved-target. Otherwise we list the
        // registered rules with no resolution status — better than nothing.
        Microsoft.CodeAnalysis.CSharp.CSharpCompilation? compilation = null;
        try { compilation = CompilationLoader.Instance.Load(parsed.Path); }
        catch { /* loader best-effort; render without resolution data. */ }
        if (compilation is not null && ReferenceEquals(compilation, CompilationLoader.EmptyCompilation))
            compilation = null;

        var disabled = ToDisabledSet(parsed.DisabledRules);
        var statuses = registry.Statuses(compilation, disabled);
        if (statuses.Count == 0)
        {
            Console.WriteLine("(no rules registered)");
            return;
        }
        // Column widths picked to fit the longest registered Name / Provenance
        // with a one-char minimum padding. Recomputed per invocation so adding
        // a long rule name doesn't break alignment.
        var nameWidth = Math.Max(4, statuses.Max(s => s.Name.Length));
        var provWidth = Math.Max(10, statuses.Max(s => s.Provenance.Length));
        Console.WriteLine($"{"Name".PadRight(nameWidth)}  {"Provenance".PadRight(provWidth)}  Status");
        Console.WriteLine($"{new string('-', nameWidth)}  {new string('-', provWidth)}  ------");
        foreach (var s in statuses)
        {
            var status = s.State switch
            {
                RuleState.Enabled => "enabled",
                RuleState.UserDisabled => "disabled (--disable-rule)",
                RuleState.SelfDisabled => $"self-disabled (unresolved: {s.UnresolvedTarget})",
                _ => "?",
            };
            Console.WriteLine($"{s.Name.PadRight(nameWidth)}  {s.Provenance.PadRight(provWidth)}  {status}");
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
        // File capture is reluctant so paths containing parentheses
        // (e.g. `C:\src\Reactor (test)\Program.cs`) still parse — anchor
        // is the (line,col): suffix immediately preceding the severity.
        static readonly Regex Pattern = new(
            @"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s*(?<sev>error|warning|info)\s+(?<code>[A-Z][A-Z0-9_]*\d):\s*(?<msg>.+?)(?:\s*\[[^\]]+\])?\s*$",
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
