using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Microsoft.UI.Reactor.Cli.Devtools;

/// <summary>
/// <c>mur devtools [project] [--component Name] [--mcp-port N]</c>.
/// Launches <c>dotnet run -- --devtools run</c> against the target project and
/// respawns after each exit-code-42 from the child (the reload sentinel).
/// </summary>
internal sealed record SupervisorArgs(
    string? Project,
    string? Component,
    int? McpPort,
    bool Help,
    bool PrintConfig,
    string? Error);

internal static class DevtoolsSupervisor
{
    private const int ReloadExitCode = 42;

    internal static SupervisorArgs ParseArgs(string[] args)
    {
        string? project = null;
        string? component = null;
        int? mcpPort = null;
        bool printConfig = false;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--component" && i + 1 < args.Length)
            {
                component = args[++i];
            }
            else if (a == "--mcp-port" && i + 1 < args.Length)
            {
                if (!int.TryParse(args[++i], out var p))
                    return new SupervisorArgs(null, null, null, false, false, "Invalid --mcp-port value.");
                mcpPort = p;
            }
            else if (a == "--print-config")
            {
                printConfig = true;
            }
            else if (a == "--help" || a == "-h")
            {
                return new SupervisorArgs(null, null, null, true, false, null);
            }
            else if (a.StartsWith("-"))
            {
                return new SupervisorArgs(null, null, null, false, false, $"Unknown flag: {a}");
            }
            else if (project is null)
            {
                project = a;
            }
            else
            {
                return new SupervisorArgs(null, null, null, false, false, $"Unexpected argument: {a}");
            }
        }

        return new SupervisorArgs(project, component, mcpPort, false, printConfig, null);
    }

    public static int Run(string[] args)
    {
        // Spec 025 routes: session subverbs and the named-verb surface dispatch
        // before the launcher parser so the launcher doesn't misread them as
        // project paths.
        if (args.Length > 0)
        {
            var first = args[0].ToLowerInvariant();
            if (first == "session")
                return SessionCommands.Run(args.Skip(1).ToArray());
            if (DevtoolsVerbs.KnownVerbs.Contains(first))
            {
                // --launch opts into the old spawn-per-invocation behavior for
                // verbs that existed as launcher subverbs (tree, screenshot,
                // list/components). Today those one-shot forms are still served
                // by the launcher path; named-verb mode without --launch attaches
                // to the running session via lockfile discovery.
                bool launch = args.Contains("--launch");
                if (!launch)
                {
                    var verbArgs = args.Skip(1).ToArray();
                    return DevtoolsVerbs.Run(first, verbArgs);
                }
                // Fall through to launcher parsing with --launch stripped;
                // launcher subverbs don't know the flag.
                args = args.Where(a => !string.Equals(a, "--launch", StringComparison.Ordinal)).ToArray();
            }
        }

        var parsed = ParseArgs(args);
        if (parsed.Help)
        {
            PrintHelp();
            return 0;
        }
        if (parsed.Error is not null)
        {
            Console.Error.WriteLine($"[mur devtools] {parsed.Error}");
            return 1;
        }
        if (parsed.PrintConfig)
        {
            var port = parsed.McpPort ?? FindFreePort();
            Console.Write(BuildPrintConfigPayload(port));
            return 0;
        }

        var project = parsed.Project ?? FindDefaultProject(Directory.GetCurrentDirectory());
        var component = parsed.Component;
        var mcpPort = parsed.McpPort;
        if (project is null)
        {
            Console.Error.WriteLine("[mur devtools] No .csproj found in the current directory.");
            return 1;
        }

        // Pin an MCP port across respawns so the agent keeps a stable endpoint.
        var pinnedPort = mcpPort ?? FindFreePort();
        Console.WriteLine($"[mur devtools] Using MCP port {pinnedPort} across reloads.");

        while (true)
        {
            var exitCode = LaunchChild(project, component, pinnedPort);
            if (exitCode == ReloadExitCode)
            {
                Console.WriteLine("[mur devtools] Reload requested — rebuilding...");
                var buildOk = RunDotnetBuild(project);
                if (!buildOk)
                {
                    Console.Error.WriteLine("[mur devtools] Build failed. Waiting — fix the error and request reload again.");
                    // Deliberately do NOT respawn: the spec says the MCP port
                    // stays unbound on build failure so the agent sees a transport
                    // error. We exit the supervisor with the build-fail code; the
                    // user re-runs `mur devtools` when they're ready.
                    return 2;
                }
                continue;
            }
            return exitCode;
        }
    }

    private static int LaunchChild(string project, string? component, int mcpPort)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = false, // Stream verbatim; don't buffer.
            RedirectStandardError = false,
            WorkingDirectory = Directory.GetCurrentDirectory(),
        };
        foreach (var a in BuildChildArguments(project, component, mcpPort))
            psi.ArgumentList.Add(a);

        Console.WriteLine($"[mur devtools] Launching: dotnet {string.Join(' ', psi.ArgumentList)}");
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet.");
        proc.WaitForExit();
        return proc.ExitCode;
    }

    /// <summary>
    /// Builds the argv passed to <c>dotnet</c> for the child run. The child's
    /// <c>DevtoolsCliParser</c> treats the first positional following
    /// <c>--devtools run</c> as the component name — so the component positional
    /// must come BEFORE any <c>--flag value</c> pair whose value could be
    /// mistaken for a positional (notably <c>--mcp-port N</c>, whose <c>N</c>
    /// doesn't start with <c>-</c> and would otherwise be picked up as the
    /// component name).
    /// </summary>
    internal static IReadOnlyList<string> BuildChildArguments(string project, string? component, int mcpPort)
    {
        var args = new List<string>
        {
            "run",
            "--project",
            project,
            "--",
            "--devtools",
            "run",
        };
        if (!string.IsNullOrEmpty(component))
            args.Add(component);
        args.Add("--mcp-port");
        args.Add(mcpPort.ToString());
        args.Add("--devtools-project");
        args.Add(Path.GetFullPath(project));
        return args;
    }

    private static bool RunDotnetBuild(string project)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            WorkingDirectory = Directory.GetCurrentDirectory(),
        };
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add(project);

        using var proc = Process.Start(psi);
        if (proc is null) return false;
        proc.WaitForExit();
        return proc.ExitCode == 0;
    }

    private static string? FindDefaultProject(string dir)
    {
        try
        {
            var hits = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly);
            return hits.Length == 1 ? hits[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private static int FindFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("mur devtools [project] [--component Name] [--mcp-port N]");
        Console.WriteLine("mur devtools --print-config [--mcp-port N]");
        Console.WriteLine();
        Console.WriteLine("  Launches the target project with --devtools run and respawns on reload.");
        Console.WriteLine("  When the child exits with code 42, rebuilds and relaunches. Any other");
        Console.WriteLine("  exit code propagates. The MCP port is pinned across respawns so an");
        Console.WriteLine("  agent can reconnect at the same endpoint.");
        Console.WriteLine();
        Console.WriteLine("  --print-config   Emit MCP config fragments (Claude Code, Copilot, VS Code)");
        Console.WriteLine("                   wired to the given --mcp-port; prints to stdout only.");
        Console.WriteLine();
        Console.WriteLine("Session management:");
        Console.WriteLine("  mur devtools session list [--pretty]      List running devtools sessions");
        Console.WriteLine("  mur devtools session clean [--dry-run]    Remove stale lockfiles");
        Console.WriteLine();
        Console.WriteLine("Named verbs (attach to the running session via lockfile discovery):");
        Console.WriteLine("  version                             App build tag, pid, and MCP port");
        Console.WriteLine("  windows                             List active windows with bounds and mounted component");
        Console.WriteLine("  windows.list                        List active windows with id, key, DIP size, DPI, state, isMain");
        Console.WriteLine("  windows.activate <id>               Activate (focus) a window by id");
        Console.WriteLine("  windows.close <id>                  Close a window by id (honors UseClosingGuard)");
        Console.WriteLine("  windows.open <Component>            Open a new window mounting an allowlisted Component");
        Console.WriteLine("              [--title T] [--width W] [--height H] [--key K]");
        Console.WriteLine("  components                          List Component classes; marks which is mounted");
        Console.WriteLine("  switch <component>                  Swap the root component (invalidates node ids)");
        Console.WriteLine("  tree [--selector S] [--window W]    Dump the visual tree as JSON");
        Console.WriteLine("       [--view summary|full] [--include-reactor-source]");
        Console.WriteLine("  screenshot [--selector S] [--out P]  Capture a PNG of the window (or selector region)");
        Console.WriteLine("             [--wait-idle] [--include-chrome]");
        Console.WriteLine("  state [--selector S]                Read reactive hook state from the mounted component");
        Console.WriteLine("  click <selector>                    Click the element via UIA Invoke/Toggle/Selection");
        Console.WriteLine("  type <selector> <text> [--clear]    Set text on a value-bearing control");
        Console.WriteLine("  focus <selector>                    Programmatically focus the element");
        Console.WriteLine("  invoke <selector>                   Call IInvokeProvider.Invoke directly");
        Console.WriteLine("  toggle <selector>                   Call IToggleProvider.Toggle; returns new state");
        Console.WriteLine("  select <selector> <item-selector>   Select an item in a ListView / ComboBox");
        Console.WriteLine("  scroll <selector> [--by H%,V%]      Scroll by percent deltas or scroll an item into view");
        Console.WriteLine("         [--to <item-selector>]");
        Console.WriteLine("  expand <selector>                   Expand an ExpandCollapse element (ComboBox, TreeView)");
        Console.WriteLine("  collapse <selector>                 Collapse an ExpandCollapse element");
        Console.WriteLine("  wait <selector> [--text X]          Poll until a predicate matches or timeout");
        Console.WriteLine("       [--text-matches RE] [--visible]");
        Console.WriteLine("       [--count N] [--timeout MS]");
        Console.WriteLine("  fire <Component>.<event>            Invoke a named handler method on a Component");
        Console.WriteLine("       [--args JSON]                  (escape hatch — prefer UIA verbs when possible)");
        Console.WriteLine("  reload [--component Name]           Rebuild + relaunch via the supervisor sentinel");
        Console.WriteLine("  shutdown                            Close the app cleanly (supervisor exits 0)");
        Console.WriteLine("  call <tool|method> [--args JSON]    Generic JSON-RPC passthrough (parity escape hatch)");
        Console.WriteLine();
        Console.WriteLine("  Shared flags: --endpoint <url>  override discovery");
        Console.WriteLine("                --auto            opt-in loopback port scan (slow)");
        Console.WriteLine("                --pretty          indent JSON output");
    }

    /// <summary>
    /// Builds the <c>--print-config</c> output: one JSON fragment per supported
    /// agent, each valid JSON on its own, separated by human-readable headers.
    /// The user picks the one they need and pastes it into the target config
    /// file themselves — this tool never writes to disk.
    /// </summary>
    internal static string BuildPrintConfigPayload(int mcpPort)
    {
        var url = $"http://127.0.0.1:{mcpPort}/mcp";

        var claudeCode = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["reactor"] = new { type = "http", url },
            },
        };

        // VS Code's MCP config nests under `servers` (distinct from Claude Code's
        // `mcpServers`) per the VS Code MCP docs. Users paste this into their
        // `.vscode/mcp.json`.
        var vscode = new
        {
            servers = new Dictionary<string, object>
            {
                ["reactor"] = new { type = "http", url },
            },
        };

        // GitHub Copilot's workspace MCP config follows the VS Code shape today;
        // we emit the same fragment so the user can drop it into either. If the
        // format diverges later, bump this and leave the VS Code block alone.
        var copilot = new
        {
            servers = new Dictionary<string, object>
            {
                ["reactor"] = new { type = "http", url },
            },
        };

        var opts = new global::System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
        };

        var sb = new global::System.Text.StringBuilder();
        sb.AppendLine($"# Reactor MCP at {url}");
        sb.AppendLine();
        sb.AppendLine("## Claude Code — ~/.claude/settings.json");
        sb.AppendLine(global::System.Text.Json.JsonSerializer.Serialize(claudeCode, opts));
        sb.AppendLine();
        sb.AppendLine("## VS Code — .vscode/mcp.json");
        sb.AppendLine(global::System.Text.Json.JsonSerializer.Serialize(vscode, opts));
        sb.AppendLine();
        sb.AppendLine("## GitHub Copilot (workspace MCP)");
        sb.AppendLine(global::System.Text.Json.JsonSerializer.Serialize(copilot, opts));
        return sb.ToString();
    }
}
