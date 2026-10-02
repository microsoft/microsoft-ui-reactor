using System.Text;

namespace Microsoft.UI.Reactor.Cli.Devtools;

/// <summary>One row of the removed-<c>mur devtools</c> → <c>winapp</c> table.</summary>
/// <param name="Key">Lookup key: a former verb, <c>session list</c>, <c>session clean</c>, <c>--print-config</c>, <c>--component</c>, an MCP tool reached through <c>call</c>, or <see cref="DevtoolsRemoved.LaunchKey"/>.</param>
/// <param name="MurForm">The removed <c>mur devtools</c> form, as shown in the table.</param>
/// <param name="WinappForm">What to run instead, or why there is no tool equivalent.</param>
/// <param name="HasEquivalent">False when there is deliberately no winapp command for it.</param>
internal sealed record DevtoolsVerbMapping(string Key, string MurForm, string WinappForm, bool HasEquivalent);

/// <summary>
/// <c>mur devtools</c> has been removed. Reactor's developer tools are the WinUI DevTools in
/// the winapp CLI (<c>winapp run . --devtools</c>). Every <c>mur devtools …</c> invocation —
/// including bare <c>mur devtools</c> and <c>--help</c> — does nothing but tell the user what to
/// run instead, on stderr, and exits with <see cref="ExitCode"/>.
/// </summary>
internal static class DevtoolsRemoved
{
    /// <summary>Exit code of every <c>mur devtools</c> invocation (documented, stable).</summary>
    internal const int ExitCode = 1;

    internal const string LaunchKey = "(launch)";
    internal const string LaunchEquivalent = "winapp run . --devtools";
    internal const string WatchEquivalent = "winapp run . --devtools --watch";
    internal const string PreviewEquivalent = "winapp run . --devtools -- --preview <Name>";
    internal const string ReferencesKey = "references";
    internal const string AgentSkill = "winui-devtools";

    private const string Prefix = "[mur devtools] ";
    private const string PreviewNote = PreviewEquivalent + "  (the preview host is in the app: for now the app must reference the Microsoft.UI.Reactor.Devtools package)";
    private const string NoMcp = "not carried over: use the matching winapp command with --json (agents: the " + AgentSkill + " skill)";
    private const string AppBehavior = "no tool equivalent: this is app behavior; drive the app's own UI with winapp ui";

    /// <summary>The full table, in help order. Every verb <c>mur devtools</c> accepted has a row.</summary>
    internal static readonly IReadOnlyList<DevtoolsVerbMapping> Mappings = new DevtoolsVerbMapping[]
    {
        new(LaunchKey, "mur devtools [project]", LaunchEquivalent + "  (add --watch to rebuild on rude edits and re-attach automatically; --detach for agents; no package or build switch needed)", true),
        new("--component", "mur devtools --component <Name>", PreviewNote, true),
        new("components", "components", PreviewNote, true),
        new("switch", "switch <component>", PreviewNote, true),
        new("windows.open", "windows.open <Component>", PreviewNote, true),
        new("--print-config", "mur devtools --print-config / --mcp-port", NoMcp, false),
        new("call", "call <tool>", NoMcp, false),
        new("session list", "session list", "winapp devtools list", true),
        new("session clean", "session clean", "not needed: winapp sessions use per-process named pipes, not lockfiles", false),
        new("version", "version", "winapp devtools call DevTools.negotiate  (reports the Reactor version; winapp devtools list only lists sessions)", true),
        new("windows", "windows", "winapp ui list-windows -a <app>", true),
        new("windows.list", "windows.list", "winapp ui list-windows -a <app>", true),
        new("windows.activate", "windows.activate <id>", "winapp ui focus <selector> -w <hwnd>", true),
        new("windows.close", "windows.close <id>", AppBehavior, false),
        new("shutdown", "shutdown", AppBehavior, false),
        new("docking.list", "call docking.list", AppBehavior, false),
        new("docking.snapshot", "call docking.snapshot", AppBehavior, false),
        new("docking.dock", "call docking.dock", AppBehavior, false),
        new("tree", "tree", "winapp devtools inspect", true),
        new("ancestors", "ancestors <selector>", "winapp devtools inspect <selector> --ancestors", true),
        new("screenshot", "screenshot", "winapp ui screenshot -a <app>", true),
        new("state", "state", "winapp devtools get-state <selector>  (any component; also set-state, rerender)", true),
        new("click", "click <selector>", "winapp ui click <selector>", true),
        new("type", "type <selector> <text>", "winapp ui set-value <selector> <text>  (or winapp ui send-keys)", true),
        new("focus", "focus <selector>", "winapp ui focus <selector>", true),
        new("invoke", "invoke <selector>", "winapp ui invoke <selector>", true),
        new("toggle", "toggle <selector>", "winapp ui invoke <selector> --action toggle", true),
        new("select", "select <selector> <item>", "winapp ui invoke <item-selector> --action select", true),
        new("expand", "expand <selector>", "winapp ui invoke <selector> --action expand", true),
        new("collapse", "collapse <selector>", "winapp ui invoke <selector> --action collapse", true),
        new("scroll", "scroll <selector>", "winapp ui scroll <selector>  (or winapp ui scroll-into-view <selector>)", true),
        new("wait", "wait <selector>", "winapp ui wait-for <selector>", true),
        new("fire", "fire <Component>.<event>", "winapp devtools invoke-handler <selector> <event>  (for example OnClick)", true),
        new("properties", "properties <selector>", "winapp devtools get-property <selector>", true),
        new("set-property", "set-property <selector> <prop> <value>", "winapp devtools set-property <selector> <prop> <value>", true),
        new("styles", "styles <selector>", "winapp devtools get-property <selector>  (shows where each value comes from)", true),
        new("resources", "resources", "winapp devtools get-resource <key> [selector]", true),
        new("set-resource", "set-resource <key> <value>", "winapp devtools set-resource <key> <value> [selector]", true),
        new(ReferencesKey, "call references", "winapp devtools get-property <selector>  (References section)", true),
        new("reload", "reload", WatchEquivalent + "  (.NET Hot Reload for edits; rebuilds and re-attaches on rude edits)", true),
        new("logs", "logs", "winapp devtools logs [--follow]", true),
    };

    // MCP tools reached through `mur devtools call <tool>` that have a row of their own. Other
    // tool names differ from the winapp commands, so a generic `call` gets the generic row.
    private static readonly HashSet<string> s_callTools = new(StringComparer.OrdinalIgnoreCase)
    {
        ReferencesKey, "docking.list", "docking.snapshot", "docking.dock",
    };

    private static readonly Dictionary<string, DevtoolsVerbMapping> s_byKey =
        Mappings.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Finds the row for an exact key, or null.</summary>
    internal static DevtoolsVerbMapping? Find(string key) =>
        s_byKey.TryGetValue(key, out var m) ? m : null;

    /// <summary>True for bare <c>mur devtools</c> and its help flags: print the whole table.</summary>
    internal static bool IsHelp(string[] args) =>
        args.Length == 0 || args.Any(a => a is "--help" or "-h" or "-?");

    /// <summary>Picks the row for a <c>mur devtools</c> argv, mirroring the old routing.</summary>
    internal static DevtoolsVerbMapping Resolve(string[] args)
    {
        if (args.Length > 0)
        {
            var first = args[0];
            if (string.Equals(first, "session", StringComparison.OrdinalIgnoreCase))
            {
                var sub = args.Length > 1 && string.Equals(args[1], "clean", StringComparison.OrdinalIgnoreCase)
                    ? "session clean"
                    : "session list";
                return s_byKey[sub];
            }
            if (string.Equals(first, "call", StringComparison.OrdinalIgnoreCase))
            {
                return args.Length > 1 && s_callTools.Contains(args[1])
                    ? s_byKey[args[1]]
                    : s_byKey["call"];
            }
            if (!first.StartsWith('-') && s_byKey.TryGetValue(first, out var verb))
                return verb;
        }
        if (args.Any(a => a is "--print-config" or "--mcp-port"))
            return s_byKey["--print-config"];
        if (args.Contains("--component"))
            return s_byKey["--component"];
        // Anything else was the launcher: `mur devtools [project]`.
        return s_byKey[LaunchKey];
    }

    /// <summary>The message for one invocation: what was removed and what to use instead.</summary>
    internal static string BuildRemovedMessage(string[] args)
    {
        var m = Resolve(args);
        var form = m.MurForm.StartsWith("mur ", StringComparison.Ordinal) ? m.MurForm : "mur devtools " + m.MurForm;
        var sb = new StringBuilder();
        sb.Append(Prefix).Append('`').Append(form).Append("` has been removed; use: ").AppendLine(m.WinappForm);
        sb.Append(Prefix).AppendLine($"Reactor's developer tools are the WinUI DevTools in the winapp CLI (`{LaunchEquivalent}`); AI agents use the `{AgentSkill}` skill.");
        sb.Append(Prefix).AppendLine("Full mapping: `mur devtools --help`.");
        return sb.ToString();
    }

    /// <summary>The full removed-verb table (bare <c>mur devtools</c> and <c>--help</c>).</summary>
    internal static string BuildMappingTable()
    {
        var width = Mappings.Max(m => m.MurForm.Length);
        var sb = new StringBuilder();
        sb.AppendLine("`mur devtools` has been removed. Reactor's developer tools are the WinUI DevTools in the winapp CLI:");
        sb.AppendLine($"`{LaunchEquivalent}` (AI agents: the `{AgentSkill}` skill). Add -a <app> to winapp commands when");
        sb.AppendLine($"more than one app is running. Every `mur devtools` command now only prints this guidance and exits {ExitCode}.");
        sb.AppendLine();
        sb.Append("  ").Append("mur devtools".PadRight(width)).AppendLine("  use instead");
        sb.Append("  ").Append(new string('-', width)).Append("  ").AppendLine(new string('-', 11));
        foreach (var m in Mappings)
            sb.Append("  ").Append(m.MurForm.PadRight(width)).Append("  ").AppendLine(m.WinappForm);
        return sb.ToString();
    }

    /// <summary>Entry point for <c>mur devtools …</c>: writes guidance to <paramref name="error"/>, returns <see cref="ExitCode"/>.</summary>
    internal static int Run(string[] args, TextWriter error)
    {
        error.Write(IsHelp(args) ? BuildMappingTable() : BuildRemovedMessage(args));
        return ExitCode;
    }
}