namespace Microsoft.UI.Reactor.Cli.Loc;

/// <summary>
/// Entry point for `mur loc` subcommands: extract, translate, validate, status, prune.
/// </summary>
internal static class LocCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        var subcommand = args[0].ToLowerInvariant();
        var subArgs = args.Skip(1).ToArray();

        return subcommand switch
        {
            "extract" => ExtractCommand.Run(subArgs),
            "translate" => TranslateCommand.Run(subArgs),
            "validate" => ValidateCommand.Run(subArgs),
            "status" => StatusCommand.Run(subArgs),
            "prune" => PruneCommand.Run(subArgs),
            "--help" or "-h" => ShowHelpAndReturn(),
            _ => Unknown(subcommand),
        };
    }

    private static int ShowHelpAndReturn()
    {
        ShowHelp();
        return 0;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("mur loc — Localization CLI");
        Console.WriteLine();
        Console.WriteLine("Usage: mur loc <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  extract      Extract localizable strings from source files");
        Console.WriteLine("  translate    AI-translate .resw files to target locales");
        Console.WriteLine("  validate     Check ICU syntax and parameter consistency");
        Console.WriteLine("  status       Show translation coverage per locale");
        Console.WriteLine("  prune        Find unused localization keys");
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: mur loc {cmd}");
        Console.Error.WriteLine();
        ShowHelp();
        return 1;
    }
}
