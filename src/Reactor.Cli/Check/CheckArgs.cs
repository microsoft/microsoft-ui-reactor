// Phase-0 args parser for `mur check`. Recognises the `--trace <path>` flag
// (spec 038 §0.3) plus a single optional positional path; everything else is
// rejected with a clear error rather than silently forwarded. Phase 2 grows
// this into a full passthrough parser (`--strict`, `--final`, `--`...) — see
// docs/specs/038-mur-check-did-you-mean-design.md §8.

namespace Microsoft.UI.Reactor.Cli.Check;

internal sealed record CheckArgs(string Path, string? TracePath)
{
    public static bool TryParse(string[] args, out CheckArgs parsed, out string? error)
    {
        string? path = null;
        string? tracePath = null;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--trace":
                    if (i + 1 >= args.Length)
                    {
                        parsed = new CheckArgs(".", null);
                        error = "--trace requires a path argument.";
                        return false;
                    }
                    tracePath = args[++i];
                    break;
                default:
                    if (a.StartsWith('-'))
                    {
                        parsed = new CheckArgs(".", null);
                        error = $"unknown flag '{a}'.";
                        return false;
                    }
                    if (path is not null)
                    {
                        parsed = new CheckArgs(".", null);
                        error = $"only one positional path is supported (got '{path}' and '{a}').";
                        return false;
                    }
                    path = a;
                    break;
            }
        }

        parsed = new CheckArgs(path ?? ".", tracePath);
        error = null;
        return true;
    }

    public static string HelpText =>
        "mur check [<path>] [--trace <jsonl-path>]\n" +
        "  <path>           Project, .csproj, .cs file, or directory (default: .)\n" +
        "  --trace <path>   Append one JSONL row per parsed diagnostic to <path>\n" +
        "                   (in addition to the normal stdout output)\n";
}
