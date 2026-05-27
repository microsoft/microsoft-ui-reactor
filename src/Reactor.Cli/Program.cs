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

if (arg == "--api" || arg == "api")
{
    return ShowApi();
}

if (arg == "--regen-api" || arg == "regen-api")
{
    return RegenApi();
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

if (arg == "check")
{
    return Microsoft.UI.Reactor.Cli.Check.CheckCommand.Run(args.Skip(1).ToArray());
}

if (arg == "pack-local")
{
    return Microsoft.UI.Reactor.Cli.Pack.PackLocalCommand.Run(args.Skip(1).ToArray());
}

if (arg == "clean-local")
{
    return Microsoft.UI.Reactor.Cli.Pack.CleanLocalCommand.Run(args.Skip(1).ToArray());
}

if (arg == "doctor")
{
    return Microsoft.UI.Reactor.Cli.Doctor.DoctorCommand.Run(args.Skip(1).ToArray());
}

if (arg == "upgrade")
{
    return Microsoft.UI.Reactor.Cli.Upgrade.UpgradeCommand.Run(args.Skip(1).ToArray());
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
    Console.WriteLine("  --api            Print the reactor.api.txt signatures index to stdout");
    Console.WriteLine("  --regen-api      Regenerate skills/reactor.api.txt from the built Reactor.dll");
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
    Console.WriteLine("  check [path]     Build and emit one-line diagnostics with skill-file pointers");
    Console.WriteLine("  pack-local       Pack the in-source framework to <repo>/local-nupkgs/ as 0.0.0-local");
    Console.WriteLine("  clean-local      Remove local packages, NuGet cache entries, and templates");
    Console.WriteLine("  doctor           Verify the install (SDK, mur, local feed, template, plugin)");
    Console.WriteLine("  upgrade          Re-pack framework + templates and refresh plugin after `git pull`");
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

int ShowApi()
{
    // Prefer the embedded copy (always up-to-date with this `mur` binary).
    // Fallback path: a NuGet consumer with no `mur` install can read the file
    // directly from their package cache:
    //   %USERPROFILE%\.nuget\packages\microsoft.ui.reactor\<version>\agentkit\reactor.api.txt
    using var stream = assembly.GetManifestResourceStream("reactor.api.txt");
    if (stream is null)
    {
        Console.Error.WriteLine("Error: Embedded reactor.api.txt resource not found.");
        Console.Error.WriteLine("Run `mur --regen-api` (selfhost) or read the file from the NuGet package cache.");
        return 1;
    }
    using var reader = new StreamReader(stream);
    Console.Write(reader.ReadToEnd());
    return 0;
}

int RegenApi()
{
    // Walk up from CWD first (so a globally-installed `mur` still resolves the
    // checkout the user is sitting in), then fall back to the running binary's
    // location for the legacy bin/<arch>/mur.exe layout. Selfhost only — NuGet
    // consumers don't have the source.
    static string? FindGenRoot(string start)
    {
        for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "tools", "Reactor.SignaturesGen"))
                && Directory.Exists(Path.Combine(d.FullName, "src", "Reactor")))
            {
                return d.FullName;
            }
        }
        return null;
    }
    var repoRoot = FindGenRoot(Directory.GetCurrentDirectory())
                ?? FindGenRoot(AppContext.BaseDirectory);
    if (repoRoot is null)
    {
        Console.Error.WriteLine("Error: --regen-api must be run from a Reactor source checkout (could not locate tools/Reactor.SignaturesGen).");
        return 1;
    }

    // Build the generator project — its AfterBuild target writes
    // skills/reactor.api.txt automatically.
    var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
    {
        WorkingDirectory = repoRoot,
        UseShellExecute = false,
    };
    psi.ArgumentList.Add("build");
    psi.ArgumentList.Add(Path.Combine("tools", "Reactor.SignaturesGen", "Reactor.SignaturesGen.csproj"));
    psi.ArgumentList.Add("--nologo");
    psi.ArgumentList.Add("-v:m");
    // WinUI projects require an explicit Platform — match the host arch so
    // the AfterBuild target can execute the freshly-built apphost.
    var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
        System.Runtime.InteropServices.Architecture.X64 => "x64",
        _ => null,
    };
    if (arch is not null) psi.ArgumentList.Add($"-p:Platform={arch}");

    using var proc = System.Diagnostics.Process.Start(psi)!;
    proc.WaitForExit();
    return proc.ExitCode;
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
    Console.WriteLine($"      src/Reactor/Reactor.csproj");
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
        <TargetFramework>net10.0-windows10.0.22621.0</TargetFramework>
        <Platforms>x64;ARM64</Platforms>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <UseWinUI>true</UseWinUI>
        <WindowsPackageType>None</WindowsPackageType>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="Microsoft.WindowsAppSDK" Version="2.0.1" />
      </ItemGroup>
      <ItemGroup>
        <ProjectReference Include="..\src\Reactor\Reactor.csproj" />
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
        $"Project(\"{csharpGuid}\") = \"Reactor\", \"..\\src\\Reactor\\Reactor.csproj\", \"{pg}\"",
        "EndProject",
        "Global",
        // x64 is listed first so `dotnet build` with no -p:Platform picks x64,
        // which matches the vast majority of host machines today. ARM64 hosts
        // can still build ARM64 explicitly with -p:Platform=ARM64.
        $"{t1}GlobalSection(SolutionConfigurationPlatforms) = preSolution",
        $"{t2}Debug|x64 = Debug|x64",
        $"{t2}Debug|ARM64 = Debug|ARM64",
        $"{t2}Release|x64 = Release|x64",
        $"{t2}Release|ARM64 = Release|ARM64",
        $"{t1}EndGlobalSection",
        $"{t1}GlobalSection(ProjectConfigurationPlatforms) = postSolution",
        $"{t2}{ag}.Debug|x64.ActiveCfg = Debug|x64",
        $"{t2}{ag}.Debug|x64.Build.0 = Debug|x64",
        $"{t2}{ag}.Debug|ARM64.ActiveCfg = Debug|ARM64",
        $"{t2}{ag}.Debug|ARM64.Build.0 = Debug|ARM64",
        $"{t2}{ag}.Release|x64.ActiveCfg = Release|x64",
        $"{t2}{ag}.Release|x64.Build.0 = Release|x64",
        $"{t2}{ag}.Release|ARM64.ActiveCfg = Release|ARM64",
        $"{t2}{ag}.Release|ARM64.Build.0 = Release|ARM64",
        $"{t2}{pg}.Debug|x64.ActiveCfg = Debug|x64",
        $"{t2}{pg}.Debug|x64.Build.0 = Debug|x64",
        $"{t2}{pg}.Debug|ARM64.ActiveCfg = Debug|ARM64",
        $"{t2}{pg}.Debug|ARM64.Build.0 = Debug|ARM64",
        $"{t2}{pg}.Release|x64.ActiveCfg = Release|x64",
        $"{t2}{pg}.Release|x64.Build.0 = Release|x64",
        $"{t2}{pg}.Release|ARM64.ActiveCfg = Release|ARM64",
        $"{t2}{pg}.Release|ARM64.Build.0 = Release|ARM64",
        $"{t1}EndGlobalSection",
        $"{t1}GlobalSection(SolutionProperties) = preSolution",
        $"{t2}HideSolutionNode = FALSE",
        $"{t1}EndGlobalSection",
        "EndGlobal",
        "");
}
