// Phase-0 args parser for `mur check`. Recognises the `--trace <path>` flag
// (spec 038 §0.3) plus a single optional positional path; everything else is
// rejected with a clear error rather than silently forwarded. Phase 2 grows
// this into a full passthrough parser (`--strict`, `--final`, `--`...) — see
// docs/specs/038-mur-check-did-you-mean-design.md §8.
//
// `--suggest-threshold <N>` (spec 038 §11 risk row + §14 #8): override the
// minimum number of CS-prefixed diagnostics in a single invocation before
// Tier-2 suggestions are emitted. Default lives on CheckCommand; pass 0 to
// always emit. Motivated by the EC1 finding that per-invocation overhead
// does not amortize on ~150-LoC projects.

namespace Microsoft.UI.Reactor.Cli.Check;

internal sealed record CheckArgs(string Path, string? TracePath, int? SuggestThreshold)
{
    public static bool TryParse(string[] args, out CheckArgs parsed, out string? error)
    {
        string? path = null;
        string? tracePath = null;
        int? suggestThreshold = null;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--trace":
                    if (i + 1 >= args.Length)
                    {
                        parsed = new CheckArgs(".", null, null);
                        error = "--trace requires a path argument.";
                        return false;
                    }
                    tracePath = args[++i];
                    break;
                case "--suggest-threshold":
                    if (i + 1 >= args.Length)
                    {
                        parsed = new CheckArgs(".", null, null);
                        error = "--suggest-threshold requires an integer argument (0 disables the gate).";
                        return false;
                    }
                    var raw = args[++i];
                    if (!int.TryParse(raw, out var n) || n < 0)
                    {
                        parsed = new CheckArgs(".", null, null);
                        error = $"--suggest-threshold expects a non-negative integer, got '{raw}'.";
                        return false;
                    }
                    suggestThreshold = n;
                    break;
                default:
                    if (a.StartsWith('-'))
                    {
                        parsed = new CheckArgs(".", null, null);
                        error = $"unknown flag '{a}'.";
                        return false;
                    }
                    if (path is not null)
                    {
                        parsed = new CheckArgs(".", null, null);
                        error = $"only one positional path is supported (got '{path}' and '{a}').";
                        return false;
                    }
                    path = a;
                    break;
            }
        }

        parsed = new CheckArgs(path ?? ".", tracePath, suggestThreshold);
        error = null;
        return true;
    }

    public static string HelpText =>
        "mur check [<path>] [--trace <jsonl-path>] [--suggest-threshold <N>]\n" +
        "  <path>                     .csproj file or directory containing one (default: .)\n" +
        "  --trace <path>             Append one JSONL row per parsed diagnostic to <path>\n" +
        "                             (in addition to the normal stdout output)\n" +
        "  --suggest-threshold <N>    Emit Tier-2 did-you-mean suggestions only when at least\n" +
        "                             N CS-prefixed diagnostics are present in the invocation.\n" +
        "                             0 = always emit (no gate). Default tuned against EC1 (see\n" +
        "                             docs/specs/038-mur-check-did-you-mean-design.md §14 #8).\n";
}
