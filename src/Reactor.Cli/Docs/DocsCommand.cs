namespace Microsoft.UI.Reactor.Cli.Docs;

/// <summary>
/// Entry point for <c>duct docs</c> subcommands.
/// </summary>
internal static class DocsCommand
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
            "compile" => CompileCommand.Run(subArgs),
            "check-tier" => CheckTierCommand.Run(subArgs),
            "render-diagrams" => RenderDiagramsCommand.Run(subArgs),
            "new-diagram" => NewDiagramCommand.Run(subArgs),
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
        Console.WriteLine("duct docs — Documentation CLI");
        Console.WriteLine();
        Console.WriteLine("Usage: duct docs <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  compile           Compile documentation from templates and doc apps");
        Console.WriteLine("  check-tier        Run only §11 tier-lint (no cross-link / reference / emit)");
        Console.WriteLine("  render-diagrams   Re-render only the diagrams under docs/_pipeline/diagrams/");
        Console.WriteLine("  new-diagram       Scaffold a new Mermaid diagram (.mmd)");
        Console.WriteLine();
        Console.WriteLine("Compile options:");
        Console.WriteLine("  --topic <name>          Compile only a specific topic");
        Console.WriteLine("  --no-screenshots        Skip screenshot capture (alias: --skip-screenshots)");
        Console.WriteLine("  --skip-diagrams         Skip diagram processing (SVG passthrough + Mermaid)");
        Console.WriteLine("  --no-ai                 Skip AI authoring");
        Console.WriteLine("  --no-build              Skip building doc apps");
        Console.WriteLine("  --validate-only         Check references + run tier lint without emitting");
        Console.WriteLine("  --tier <stub|solid|comprehensive>  Restrict validation to templates declaring this tier");
        Console.WriteLine("  --ci                    Strict mode: fail on warnings");
        Console.WriteLine();
        Console.WriteLine("Check-tier options:");
        Console.WriteLine("  --topic <name>          Lint only a specific topic");
        Console.WriteLine("  --tier <stub|solid|comprehensive>  Restrict to templates declaring this tier");
        Console.WriteLine("  --ci                    Strict mode: non-zero exit on warnings");
        Console.WriteLine("  --quiet                 Suppress info-level findings and the summary line");
    }

    private static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: duct docs {cmd}");
        Console.Error.WriteLine();
        ShowHelp();
        return 1;
    }
}
