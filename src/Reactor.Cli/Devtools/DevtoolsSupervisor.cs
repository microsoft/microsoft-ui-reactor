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

// MCP client-config fragments emitted by `mur devtools --print-config`.
internal sealed record McpServerEntry(string type, string url);
internal sealed record McpServersConfig(Dictionary<string, McpServerEntry> mcpServers);
internal sealed record McpServersAltConfig(Dictionary<string, McpServerEntry> servers);

internal static class DevtoolsSupervisor
{
    /// <summary>Mirrors <c>DevtoolsHost.DevtoolsReloadExitCode</c> in the devtools child.</summary>
    internal const int ReloadExitCode = 42;

    /// <summary>
    /// The child exits with this when the MCP port it was given is in use by
    /// another process. Mirrors <c>DevtoolsHost.McpPortUnavailableExitCode</c>.
    /// </summary>
    internal const int McpPortUnavailableExitCode = 43;

    /// <summary>
    /// How many consecutive launches may find an auto-picked MCP port taken
    /// before the supervisor gives up.
    /// </summary>
    internal const int MaxConsecutivePortConflicts = 5;

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
            var port = parsed.McpPort ?? ProbeFreePort();
            Console.Write(BuildPrintConfigPayload(port));
            return 0;
        }

        var project = parsed.Project ?? FindDefaultProject(Directory.GetCurrentDirectory());
        var component = parsed.Component;
        if (project is null)
        {
            Console.Error.WriteLine("[mur devtools] No .csproj found in the current directory.");
            return 1;
        }

        return Supervise(
            parsed.McpPort,
            port => LaunchChild(project, component, port),
            () => RunDotnetBuild(project),
            ProbeFreePort,
            Console.Out,
            Console.Error);
    }

    /// <summary>
    /// The respawn loop: launches the child on the pinned MCP port, rebuilds and
    /// relaunches on <see cref="ReloadExitCode"/>, and propagates any other exit
    /// code. Takes the process plumbing as delegates so it can be unit-tested.
    /// </summary>
    internal static int Supervise(
        int? userPinnedPort,
        Func<int, int> launchChild,
        Func<bool> rebuild,
        Func<int> probePort,
        TextWriter output,
        TextWriter error)
    {
        // Pin an MCP port across respawns so the agent keeps a stable endpoint.
        // An auto-picked port is only a probe: the child binds it after
        // `dotnet run` builds, and on reload it stays unbound while the project
        // rebuilds (spec 024), so another process can take it first. The child
        // then exits with McpPortUnavailableExitCode and a new port is picked.
        // A port the user pinned with --mcp-port is never moved.
        var port = userPinnedPort ?? probePort();
        var portConflicts = 0;
        output.WriteLine($"[mur devtools] Using MCP port {port} across reloads.");

        while (true)
        {
            var exitCode = launchChild(port);
            if (exitCode == ReloadExitCode)
            {
                portConflicts = 0;
                output.WriteLine("[mur devtools] Reload requested — rebuilding...");
                var buildOk = rebuild();
                if (!buildOk)
                {
                    error.WriteLine("[mur devtools] Build failed. Waiting — fix the error and request reload again.");
                    // Deliberately do NOT respawn: the spec says the MCP port
                    // stays unbound on build failure so the agent sees a transport
                    // error. We exit the supervisor with the build-fail code; the
                    // user re-runs `mur devtools` when they're ready.
                    return 2;
                }
                continue;
            }
            if (exitCode == McpPortUnavailableExitCode)
            {
                if (userPinnedPort is not null)
                {
                    error.WriteLine(
                        $"[mur devtools] MCP port {port} (--mcp-port) is in use by another process. " +
                        "Free it, or pass a different --mcp-port.");
                    return exitCode;
                }
                if (++portConflicts >= MaxConsecutivePortConflicts)
                {
                    error.WriteLine(
                        $"[mur devtools] Another process took the MCP port before the app could bind it " +
                        $"{portConflicts} times in a row (last port {port}). Giving up.");
                    return exitCode;
                }
                var taken = port;
                port = probePort();
                output.WriteLine(
                    $"[mur devtools] MCP port {taken} was taken by another process before the app could bind it; " +
                    $"using MCP port {port} across reloads instead.");
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

    /// <summary>
    /// Returns a loopback port that was free when probed. A hint, not a
    /// reservation: the child binds it later and exits with
    /// <see cref="McpPortUnavailableExitCode"/> if another process took it first.
    /// Mirrors <c>LoopbackHttpListener.ProbeFreePort</c> in Reactor.Devtools,
    /// which the CLI can't reference (it would pull in WinUI).
    /// </summary>
    internal static int ProbeFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("mur devtools [project] [--component Name] [--mcp-port N]");
        Console.WriteLine("mur devtools --print-config [--mcp-port N]");
        Console.WriteLine();
        Console.WriteLine("  Launches the target project with --devtools run and respawns on reload.");
        Console.WriteLine("  When the child exits with code 42, rebuilds and relaunches. Any other");
        Console.WriteLine("  exit code propagates. The MCP port is pinned across respawns so an");
        Console.WriteLine("  agent can reconnect at the same endpoint. If another process holds the");
        Console.WriteLine("  port when the app starts (exit code 43), an auto-picked port is replaced");
        Console.WriteLine("  with a new one; a port pinned with --mcp-port fails instead.");
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

        var claudeCode = new McpServersConfig(new()
        {
            ["reactor"] = new McpServerEntry("http", url),
        });

        // VS Code's MCP config nests under `servers` (distinct from Claude Code's
        // `mcpServers`) per the VS Code MCP docs. Users paste this into their
        // `.vscode/mcp.json`.
        var vscode = new McpServersAltConfig(new()
        {
            ["reactor"] = new McpServerEntry("http", url),
        });

        // GitHub Copilot's workspace MCP config follows the VS Code shape today;
        // we emit the same fragment so the user can drop it into either. If the
        // format diverges later, bump this and leave the VS Code block alone.
        var copilot = new McpServersAltConfig(new()
        {
            ["reactor"] = new McpServerEntry("http", url),
        });

        var sb = new global::System.Text.StringBuilder();
        sb.AppendLine($"# Reactor MCP at {url}");
        sb.AppendLine();
        sb.AppendLine("## Claude Code — ~/.claude/settings.json");
        sb.AppendLine(global::System.Text.Json.JsonSerializer.Serialize(claudeCode, CliJsonIndentedContext.Default.McpServersConfig));
        sb.AppendLine();
        sb.AppendLine("## VS Code — .vscode/mcp.json");
        sb.AppendLine(global::System.Text.Json.JsonSerializer.Serialize(vscode, CliJsonIndentedContext.Default.McpServersAltConfig));
        sb.AppendLine();
        sb.AppendLine("## GitHub Copilot (workspace MCP)");
        sb.AppendLine(global::System.Text.Json.JsonSerializer.Serialize(copilot, CliJsonIndentedContext.Default.McpServersAltConfig));
        return sb.ToString();
    }
}
