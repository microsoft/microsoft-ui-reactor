// AI-HINT: Top-level Program.cs for the `mur` CLI tool.
// Dispatches subcommands: loc (localization), --create (project scaffolding),
// --skill (output SKILL.md for AI agents), --version, --help.
// Uses top-level statements pattern. Entry point returns int exit code.
using System.Reflection;

var assembly = Assembly.GetExecutingAssembly();
var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? assembly.GetName().Version?.ToString() ?? "0.1.0";

if (args.Length == 0)
{
    ShowHelp();
    return 0;
}

var arg = args[0].ToLowerInvariant();

if (arg is "--help" or "-h" or "-?")
{
    ShowHelp();
    return 0;
}

if (arg is "--version" or "-v")
{
    Console.WriteLine($"mur {version}");
    return 0;
}

if (arg == "--skill")
{
    return ShowSkill();
}

if (arg == "--create")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Error: --create requires a project name.");
        Console.Error.WriteLine("Usage: mur --create <ProjectName>");
        return 1;
    }
    return CreateProject(args[1]);
}

if (arg == "loc")
{
    return Microsoft.UI.Reactor.Cli.Loc.LocCommand.Run(args.Skip(1).ToArray());
}

if (arg == "docs")
{
    return Microsoft.UI.Reactor.Cli.Docs.DocsCommand.Run(args.Skip(1).ToArray());
}

if (arg == "devtools")
{
    return Microsoft.UI.Reactor.Cli.Devtools.DevtoolsSupervisor.Run(args.Skip(1).ToArray());
}

Console.Error.WriteLine($"Unknown option: {args[0]}");
Console.Error.WriteLine();
ShowHelp();
return 1;

// ---------------------------------------------------------------------------
//  Helpers
// ---------------------------------------------------------------------------

void ShowHelp()
{
    Console.WriteLine($"mur {version} — Microsoft.UI.Reactor (Functional UI) CLI");
    Console.WriteLine();
    Console.WriteLine("Usage: mur [option]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --help, -h       Show this help message");
    Console.WriteLine("  --version, -v    Show version information");
    Console.WriteLine("  --skill          Print the SKILL.md AI reference to stdout");
    Console.WriteLine("  --create <name>  Scaffold a new Reactor project");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  loc extract      Extract localizable strings from source files");
    Console.WriteLine("  loc translate    AI-translate .resw files to target locales");
    Console.WriteLine("  loc validate     Check ICU syntax and parameter consistency");
    Console.WriteLine("  loc status       Show translation coverage per locale");
    Console.WriteLine("  loc prune        Find unused localization keys");
    Console.WriteLine("  docs compile     Compile documentation from templates and doc apps");
    Console.WriteLine("  devtools         Launch project with --devtools run and supervise reloads");
}

int ShowSkill()
{
    using var stream = assembly.GetManifestResourceStream("SKILL.md");
    if (stream is null)
    {
        Console.Error.WriteLine("Error: Embedded SKILL.md resource not found.");
        return 1;
    }
    using var reader = new StreamReader(stream);
    Console.Write(reader.ReadToEnd());
    return 0;
}

int CreateProject(string name)
{
    if (!global::System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_\.]*$"))
    {
        Console.Error.WriteLine($"Error: Invalid project name '{name}'. Use only letters, digits, underscores, and dots. Must start with a letter or underscore.");
        return 1;
    }

    var dir = Path.Combine(Directory.GetCurrentDirectory(), name);

    if (Directory.Exists(dir))
    {
        Console.Error.WriteLine($"Error: Directory '{name}' already exists.");
        return 1;
    }

    Directory.CreateDirectory(dir);

    var appGuid = Guid.NewGuid().ToString("D").ToUpperInvariant();
    var patchGuid = Guid.NewGuid().ToString("D").ToUpperInvariant();

    File.WriteAllText(Path.Combine(dir, "Program.cs"), GenerateProgram(name));
    File.WriteAllText(Path.Combine(dir, $"{name}.csproj"), GenerateCsproj());
    File.WriteAllText(Path.Combine(dir, $"{name}.sln"), GenerateSln(name, appGuid, patchGuid));

    Console.WriteLine($"Created Reactor project '{name}' in .{Path.DirectorySeparatorChar}{name}{Path.DirectorySeparatorChar}");
    Console.WriteLine();
    Console.WriteLine("  Files:");
    Console.WriteLine($"    {name}{Path.DirectorySeparatorChar}{name}.sln");
    Console.WriteLine($"    {name}{Path.DirectorySeparatorChar}{name}.csproj");
    Console.WriteLine($"    {name}{Path.DirectorySeparatorChar}Program.cs");
    Console.WriteLine();
    Console.WriteLine("NOTE: The generated .sln assumes this project is a sibling of the Reactor directory:");
    Console.WriteLine($"    parent/");
    Console.WriteLine($"      Reactor/Reactor.csproj");
    Console.WriteLine($"      {name}/{name}.csproj");
    Console.WriteLine();
    Console.WriteLine("To build and run:");
    Console.WriteLine($"    cd {name}");
    Console.WriteLine($"    dotnet build {name}.sln");
    Console.WriteLine($"    dotnet run");
    Console.WriteLine();
    Console.WriteLine("To develop with an AI agent (MCP) and a VS Code preview panel:");
    Console.WriteLine($"    mur devtools                 # prints MCP_ENDPOINT to stdout");
    Console.WriteLine($"    mur devtools --mcp-port 9000 # pin the port across reloads");
    Console.WriteLine($"    mur devtools --print-config  # emit MCP config for Claude Code / VS Code / Copilot");

    return 0;
}

// ---------------------------------------------------------------------------
//  Template generators
// ---------------------------------------------------------------------------

string GenerateProgram(string name) =>
    $$"""
    using Microsoft.UI.Reactor;
    using Microsoft.UI.Reactor.Core;
    using static Microsoft.UI.Reactor.Factories;
    using Microsoft.UI.Xaml;

    ReactorApp.Run<App>("{{name}}", width: 800, height: 600
    #if DEBUG
        // Enables `mur devtools` (agent tools over MCP + VS Code preview panel).
        // Run `mur devtools` to launch with component switching, hot reload, and
        // an MCP endpoint printed to stdout (MCP_ENDPOINT=http://127.0.0.1:NNNN/mcp).
        , devtools: true
    #endif
    );

    class App : Component
    {
        public override Element Render()
        {
            return TextBlock("Hello, World!").FontSize(24).Margin(20);
        }
    }
    """;

string GenerateCsproj() =>
    """
    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>WinExe</OutputType>
        <TargetFramework>net9.0-windows10.0.22621.0</TargetFramework>
        <Platforms>x64;ARM64</Platforms>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <UseWinUI>true</UseWinUI>
        <WindowsPackageType>None</WindowsPackageType>
        <WindowsSdkPackageVersion>10.0.22621.52</WindowsSdkPackageVersion>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="Microsoft.WindowsAppSDK" Version="2.0.1" />
      </ItemGroup>
      <ItemGroup>
        <ProjectReference Include="..\Reactor\Reactor.csproj" />
      </ItemGroup>
    </Project>
    """;

/// <summary>
/// Generates a .sln that references Reactor via a relative project path.
/// This assumes the new project directory is a sibling of the Reactor/ directory.
/// When Reactor is published as a NuGet package, this should switch to a PackageReference.
/// </summary>
string GenerateSln(string name, string appGuid, string patchGuid)
{
    const string csharpGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}";
    var ag = $"{{{appGuid}}}";
    var pg = $"{{{patchGuid}}}";
    var t1 = "\t";
    var t2 = "\t\t";

    return string.Join("\r\n",
        "",
        "Microsoft Visual Studio Solution File, Format Version 12.00",
        "# Visual Studio Version 18",
        "VisualStudioVersion = 18.4.11605.240",
        "MinimumVisualStudioVersion = 10.0.40219.1",
        $"Project(\"{csharpGuid}\") = \"{name}\", \"{name}.csproj\", \"{ag}\"",
        "EndProject",
        $"Project(\"{csharpGuid}\") = \"Reactor\", \"..\\Reactor\\Reactor.csproj\", \"{pg}\"",
        "EndProject",
        "Global",
        $"{t1}GlobalSection(SolutionConfigurationPlatforms) = preSolution",
        $"{t2}Debug|ARM64 = Debug|ARM64",
        $"{t2}Debug|x64 = Debug|x64",
        $"{t2}Release|ARM64 = Release|ARM64",
        $"{t2}Release|x64 = Release|x64",
        $"{t1}EndGlobalSection",
        $"{t1}GlobalSection(ProjectConfigurationPlatforms) = postSolution",
        $"{t2}{ag}.Debug|ARM64.ActiveCfg = Debug|ARM64",
        $"{t2}{ag}.Debug|ARM64.Build.0 = Debug|ARM64",
        $"{t2}{ag}.Debug|x64.ActiveCfg = Debug|x64",
        $"{t2}{ag}.Debug|x64.Build.0 = Debug|x64",
        $"{t2}{ag}.Release|ARM64.ActiveCfg = Release|ARM64",
        $"{t2}{ag}.Release|ARM64.Build.0 = Release|ARM64",
        $"{t2}{ag}.Release|x64.ActiveCfg = Release|x64",
        $"{t2}{ag}.Release|x64.Build.0 = Release|x64",
        $"{t2}{pg}.Debug|ARM64.ActiveCfg = Debug|ARM64",
        $"{t2}{pg}.Debug|ARM64.Build.0 = Debug|ARM64",
        $"{t2}{pg}.Debug|x64.ActiveCfg = Debug|x64",
        $"{t2}{pg}.Debug|x64.Build.0 = Debug|x64",
        $"{t2}{pg}.Release|ARM64.ActiveCfg = Release|ARM64",
        $"{t2}{pg}.Release|ARM64.Build.0 = Release|ARM64",
        $"{t2}{pg}.Release|x64.ActiveCfg = Release|x64",
        $"{t2}{pg}.Release|x64.Build.0 = Release|x64",
        $"{t1}EndGlobalSection",
        $"{t1}GlobalSection(SolutionProperties) = preSolution",
        $"{t2}HideSolutionNode = FALSE",
        $"{t1}EndGlobalSection",
        "EndGlobal",
        "");
}
