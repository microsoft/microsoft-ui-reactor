namespace ReactorRegedit.Core;

/// <summary>Command-line handling: <c>--key &lt;path&gt;</c> opens the app at a key (used by Restart as administrator).</summary>
public static class CommandLine
{
    /// <summary>The normalized key passed with <c>--key</c>, or null.</summary>
    public static string? KeyArgument(IReadOnlyList<string> args)
    {
        for (int i = 0; i < args.Count - 1; i++)
        {
            if (args[i].Equals("--key", StringComparison.OrdinalIgnoreCase))
                return RegPath.Normalize(args[i + 1]) is { Length: > 0 } path ? path : null;
        }
        return null;
    }
}
