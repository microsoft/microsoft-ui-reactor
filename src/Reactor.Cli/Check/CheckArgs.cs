// Parsed result of `mur check` argument parsing. The parser itself lives in
// ArgsParser; this record is pure data plus the help text shown by `--help`.
//
// Field map:
//   Path                project path (.csproj or directory). Default ".".
//   TracePath           --trace <path>; spec §0.3.
//   SuggestThreshold    --suggest-threshold <N>; spec §11 + §14 #8. Null →
//                       CheckCommand applies its own default.
//   Mode                --strict / --final / --quiet (default Iteration);
//                       spec §8.
//   EmitThreshold       --emit-threshold <float>; spec §8. Null → ranker
//                       uses the mode's documented default.
//   DisabledRules       --disable-rule <Name> (repeatable); spec §6 + tasks §3.1.
//                       Each occurrence appends one rule Name; matching is
//                       case-sensitive (rule Names are stable identifiers).
//   ListRules           --list-rules (boolean); when set, the command prints
//                       the discovered rule table and exits without running
//                       dotnet build. tasks §3.1.
//   Passthrough         everything after the first bare `--`, forwarded
//                       verbatim to `dotnet build`. spec §8 passthrough.
//   EffectiveBuildArgs  the materialised `dotnet <args...>` vector after
//                       default-merging mur's `--nologo`, `-v:m`, and
//                       `-p:Platform={host arch}` defaults around the
//                       passthrough. spec §8.

namespace Microsoft.UI.Reactor.Cli.Check;

internal sealed record CheckArgs(
    string Path,
    string? TracePath,
    int? SuggestThreshold,
    Mode Mode,
    double? EmitThreshold,
    IReadOnlyList<string> DisabledRules,
    bool ListRules,
    IReadOnlyList<string> Passthrough,
    IReadOnlyList<string> EffectiveBuildArgs)
{
    internal static readonly CheckArgs Empty = new(
        ".", null, null, Mode.Iteration, null,
        Array.Empty<string>(), false,
        Array.Empty<string>(), Array.Empty<string>());

    /// <summary>
    /// Back-compat entry point so existing tests can keep calling
    /// CheckArgs.TryParse. New code should call ArgsParser.TryParse.
    /// </summary>
    public static bool TryParse(string[] args, out CheckArgs parsed, out string? error)
        => ArgsParser.TryParse(args, out parsed, out error);

    public static string HelpText =>
        "mur check [<path>] [mur-flags...] [-- <msbuild args>...]\n" +
        "  <path>                     .csproj file or directory containing one (default: .)\n" +
        "  --trace <path>             Append one JSONL row per parsed diagnostic to <path>\n" +
        "                             (in addition to the normal stdout output)\n" +
        "  --suggest-threshold <N>    Emit Tier-2 did-you-mean suggestions only when at least\n" +
        "                             N CS-prefixed diagnostics are present in the invocation.\n" +
        "                             0 = always emit (no gate). Default tuned against EC1 (see\n" +
        "                             docs/specs/038-mur-check-did-you-mean-design.md §14 #8).\n" +
        "  --strict                   Promote warnings to errors. CI gates; not the inner loop.\n" +
        "  --final                    Emit every diagnostic, no ranker suppression. Run once\n" +
        "                             before declaring iteration done.\n" +
        "  --quiet                    Errors only. Maximally aggressive suppression.\n" +
        "  --emit-threshold <float>   Override the ranker threshold (0.0–1.0). Default 0.6 in\n" +
        "                             iteration mode, 0.0 in final mode.\n" +
        "  --disable-rule <Name>      Disable a Tier-3 rule by Name. Repeatable.\n" +
        "  --list-rules               Print the rule table (Name / Provenance / Status) and exit.\n" +
        "  --                         Boundary: everything after is forwarded verbatim to\n" +
        "                             `dotnet build`. mur injects `--nologo`, `-v:m`, and\n" +
        "                             `-p:Platform={host arch}` only if the same flag is not\n" +
        "                             present in the passthrough section.\n" +
        "\n" +
        "Examples:\n" +
        "  mur check                                # iteration mode, host-arch platform\n" +
        "  mur check -- -p:Platform=x64             # override platform\n" +
        "  mur check --final -- -c Release          # final pre-merge gate, Release config\n" +
        "  mur check ./MyApp -- -c Release -p:Platform=x64\n";
}
