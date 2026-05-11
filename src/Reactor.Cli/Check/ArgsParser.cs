// Phase-2 args parser for `mur check` (spec 038 §8).
//
// Grammar: `mur check [<path>] [mur-flags...] [-- <msbuild args>...]`. The
// first bare `--` is the boundary — tokens to its left are parsed against
// `mur`'s own flag grammar, tokens to its right are forwarded verbatim to
// `dotnet build`. Unknown `mur` flags before `--` are an error, never silently
// forwarded; agents and humans both routinely typo their own flags
// (`--quie` for `--quiet`) and silent forwarding hides those typos as
// MSBuild "unknown property" warnings.
//
// Default-merging: `mur` injects `--nologo`, `-v:m`, and `-p:Platform={host
// arch}` only if the user did not supply the same flag *by name* in the
// passthrough section. Detection is flag-name only — `-p:Platform=x64` in
// passthrough wins over the auto-injected host arch; `-v:n` wins over `-v:m`.
// This means the value is the user's; we just stop injecting our default for
// that slot.
//
// EffectiveBuildArgs is the materialised result: `[<path>, ...mur-defaults
// minus suppressed, ...passthrough]`. CheckCommand passes it straight to
// `dotnet build` so trace output (spec §8 "Tracing") can echo the exact
// command line.

namespace Microsoft.UI.Reactor.Cli.Check;

internal static class ArgsParser
{
    public static bool TryParse(string[] args, out CheckArgs parsed, out string? error)
    {
        var (left, right) = SplitOnPassthrough(args);

        string? path = null;
        string? tracePath = null;
        int? suggestThreshold = null;
        Mode mode = Mode.Iteration;
        double? emitThreshold = null;
        bool modeFlagSeen = false;
        List<string>? disabledRules = null;
        bool listRules = false;

        for (int i = 0; i < left.Length; i++)
        {
            var a = left[i];
            switch (a)
            {
                case "--trace":
                    if (i + 1 >= left.Length)
                    {
                        parsed = CheckArgs.Empty;
                        error = "--trace requires a path argument.";
                        return false;
                    }
                    tracePath = left[++i];
                    break;
                case "--suggest-threshold":
                    if (i + 1 >= left.Length)
                    {
                        parsed = CheckArgs.Empty;
                        error = "--suggest-threshold requires an integer argument (0 disables the gate).";
                        return false;
                    }
                    var rawSt = left[++i];
                    if (!int.TryParse(rawSt, out var n) || n < 0)
                    {
                        parsed = CheckArgs.Empty;
                        error = $"--suggest-threshold expects a non-negative integer, got '{rawSt}'.";
                        return false;
                    }
                    suggestThreshold = n;
                    break;
                case "--emit-threshold":
                    if (i + 1 >= left.Length)
                    {
                        parsed = CheckArgs.Empty;
                        error = "--emit-threshold requires a float argument (0.0 = emit everything).";
                        return false;
                    }
                    var rawEt = left[++i];
                    if (!double.TryParse(rawEt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) || f < 0.0 || f > 1.0)
                    {
                        parsed = CheckArgs.Empty;
                        error = $"--emit-threshold expects a number in [0.0, 1.0], got '{rawEt}'.";
                        return false;
                    }
                    emitThreshold = f;
                    break;
                case "--disable-rule":
                    if (i + 1 >= left.Length)
                    {
                        parsed = CheckArgs.Empty;
                        error = "--disable-rule requires a rule Name argument.";
                        return false;
                    }
                    var ruleName = left[++i];
                    if (string.IsNullOrWhiteSpace(ruleName) || ruleName.StartsWith('-'))
                    {
                        parsed = CheckArgs.Empty;
                        error = $"--disable-rule expects a non-empty rule Name, got '{ruleName}'.";
                        return false;
                    }
                    (disabledRules ??= new List<string>()).Add(ruleName);
                    break;
                case "--list-rules":
                    listRules = true;
                    break;
                case "--strict":
                case "--final":
                case "--quiet":
                    if (modeFlagSeen)
                    {
                        parsed = CheckArgs.Empty;
                        error = "only one of --strict / --final / --quiet may be set.";
                        return false;
                    }
                    modeFlagSeen = true;
                    mode = a switch
                    {
                        "--strict" => Mode.Strict,
                        "--final" => Mode.Final,
                        "--quiet" => Mode.Quiet,
                        _ => Mode.Iteration,
                    };
                    break;
                default:
                    if (a.StartsWith('-'))
                    {
                        parsed = CheckArgs.Empty;
                        error = $"unknown flag '{a}'. (Did you forget the '--' separator before MSBuild args?)";
                        return false;
                    }
                    if (path is not null)
                    {
                        parsed = CheckArgs.Empty;
                        error = $"only one positional path is supported (got '{path}' and '{a}').";
                        return false;
                    }
                    path = a;
                    break;
            }
        }

        var effectivePath = path ?? ".";
        var passthrough = right;
        var effective = BuildEffectiveDotnetArgs(effectivePath, passthrough);

        parsed = new CheckArgs(
            effectivePath,
            tracePath,
            suggestThreshold,
            mode,
            emitThreshold,
            (IReadOnlyList<string>?)disabledRules ?? Array.Empty<string>(),
            listRules,
            passthrough,
            effective);
        error = null;
        return true;
    }

    /// <summary>
    /// Returns (left, right) where right is everything after the first bare
    /// `--` token (exclusive). If no bare `--` is present, right is empty.
    /// </summary>
    static (string[] Left, string[] Right) SplitOnPassthrough(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--")
            {
                var left = new string[i];
                var right = new string[args.Length - i - 1];
                Array.Copy(args, 0, left, 0, i);
                Array.Copy(args, i + 1, right, 0, right.Length);
                return (left, right);
            }
        }
        return (args, Array.Empty<string>());
    }

    /// <summary>
    /// Build the full `dotnet build` argument vector: `["build", <path>,
    /// ...injected-defaults, ...passthrough]`. Defaults are injected only if
    /// the user didn't supply the same flag *by name* in passthrough — see
    /// spec §8 "Default-merging rules".
    /// </summary>
    internal static IReadOnlyList<string> BuildEffectiveDotnetArgs(string path, IReadOnlyList<string> passthrough)
    {
        var output = new List<string> { "build", path };

        if (!PassthroughHasFlag(passthrough, "nologo"))
            output.Add("--nologo");

        if (!PassthroughHasFlag(passthrough, "v") && !PassthroughHasFlag(passthrough, "verbosity"))
            output.Add("-v:m");

        if (!PassthroughHasProperty(passthrough, "Platform"))
        {
            var arch = HostArch();
            if (arch is not null) output.Add($"-p:Platform={arch}");
        }

        foreach (var p in passthrough) output.Add(p);
        return output;
    }

    /// <summary>
    /// True if <paramref name="passthrough"/> contains a token that names
    /// <paramref name="flag"/> regardless of value. Accepts the MSBuild
    /// dash-styles `-flag`, `--flag`, `/flag`, with optional `:` or space
    /// before the value. Case-insensitive (matches MSBuild's own parser).
    /// </summary>
    internal static bool PassthroughHasFlag(IReadOnlyList<string> passthrough, string flag)
    {
        // MSBuild accepts `-`, `--`, and `/` prefixes (yes really). Match any.
        // Token shapes we care about:
        //   -flag           (boolean form)
        //   --flag          (boolean form)
        //   /flag           (boolean form)
        //   -flag:value     (colon-joined)
        //   -flag value     (space-joined; we see the bare `-flag` token)
        for (int i = 0; i < passthrough.Count; i++)
        {
            var t = passthrough[i];
            if (t.Length < 2) continue;
            var prefixLen = t[0] switch
            {
                '/' => 1,
                '-' when t.Length >= 2 && t[1] == '-' => 2,
                '-' => 1,
                _ => 0,
            };
            if (prefixLen == 0) continue;
            var body = t.AsSpan(prefixLen);
            // Trim at `:` or `=` so `-v:m` and `-p:k=v` both compare on the name only.
            var colon = body.IndexOfAny(':', '=');
            var name = colon >= 0 ? body[..colon] : body;
            if (name.Equals(flag, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True if passthrough sets a specific MSBuild property by name. Looks
    /// for `-p:<name>=...`, `--property:<name>=...`, `/p:<name>=...`. Case-
    /// insensitive property name match (MSBuild treats property names case-
    /// insensitively too).
    /// </summary>
    internal static bool PassthroughHasProperty(IReadOnlyList<string> passthrough, string propertyName)
    {
        foreach (var t in passthrough)
        {
            if (t.Length < 3) continue;
            int prefixLen = t[0] switch
            {
                '/' => 1,
                '-' when t.Length >= 2 && t[1] == '-' => 2,
                '-' => 1,
                _ => 0,
            };
            if (prefixLen == 0) continue;
            var body = t.AsSpan(prefixLen);
            var colon = body.IndexOf(':');
            if (colon < 0) continue;
            var flag = body[..colon];
            if (!flag.Equals("p", StringComparison.OrdinalIgnoreCase) &&
                !flag.Equals("property", StringComparison.OrdinalIgnoreCase))
                continue;
            var rest = body[(colon + 1)..];
            var eq = rest.IndexOf('=');
            var name = eq >= 0 ? rest[..eq] : rest;
            if (name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static string? HostArch() => System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        _ => null,
    };
}
