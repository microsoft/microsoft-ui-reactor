using System.Text;

namespace Microsoft.UI.Reactor.Cli.Devtools;

/// <summary>One row of the <c>mur devtools</c> → <c>winapp</c> migration table.</summary>
/// <param name="Key">Lookup key: a named verb, <c>session list</c>, <c>session clean</c>, <c>--print-config</c>, or <see cref="DevtoolsDeprecation.LaunchKey"/>.</param>
/// <param name="MurForm">The deprecated <c>mur devtools</c> form, as shown in the table.</param>
/// <param name="WinappForm">What to run instead, or why there is no direct replacement.</param>
/// <param name="HasEquivalent">False when winapp has no replacement yet (or deliberately none).</param>
internal sealed record DevtoolsVerbMapping(string Key, string MurForm, string WinappForm, bool HasEquivalent);

/// <summary>
/// Deprecation notice for <c>mur devtools</c>. Reactor's own DevTools (the
/// <c>Microsoft.UI.Reactor.Devtools</c> package and these verbs) are replaced by the
/// WinUI DevTools in the winapp CLI. Every invocation prints a notice to stderr naming
/// the winapp equivalent and then runs unchanged, so stdout (JSON, PNG bytes,
/// <c>--print-config</c> fragments, <c>MCP_ENDPOINT</c>) stays machine-readable.
/// </summary>
internal static class DevtoolsDeprecation
{
    /// <summary>Set to <c>1</c>/<c>true</c> to silence the notice (scripts, CI).</summary>
    internal const string SuppressEnvVar = "MUR_DEVTOOLS_NO_DEPRECATION_NOTICE";

    internal const string LaunchKey = "(launch)";
    internal const string LaunchEquivalent = "winapp run . --devtools";
    internal const string WatchEquivalent = "winapp run . --devtools --watch";
    internal const string ReferencesKey = "references";
    internal const string AgentSkill = "winui-devtools";

    private const string Prefix = "[mur devtools] ";
    private const string NoPreviewHostYet = "none yet: the component preview host is under review; keep using mur devtools for it";

    /// <summary>
    /// The full mapping, in help-table order. Every name in
    /// <see cref="DevtoolsVerbs.KnownVerbs"/> has a row (a unit test enforces it).
    /// </summary>
    internal static readonly IReadOnlyList<DevtoolsVerbMapping> Mappings = new DevtoolsVerbMapping[]
    {
        new(LaunchKey, "mur devtools [project]", LaunchEquivalent + "  (add --watch to rebuild on rude edits and re-attach automatically; --detach for agents; no package or build switch needed)", true),
        new("--component", "mur devtools --component <Name>", NoPreviewHostYet, false),
        new("--print-config", "mur devtools --print-config / --mcp-port", "none: the MCP server is not carried over; agents run winapp with --json and load the " + AgentSkill + " skill", false),
        new("session list", "session list", "winapp devtools list", true),
        new("session clean", "session clean", "not needed: winapp sessions use per-process named pipes, not lockfiles", false),
        new("version", "version", "winapp devtools call DevTools.negotiate  (reports the Reactor version; winapp devtools list only lists sessions)", true),
        new("windows", "windows", "winapp ui list-windows -a <app>", true),
        new("windows.list", "windows.list", "winapp ui list-windows -a <app>", true),
        new("windows.activate", "windows.activate <id>", "winapp ui focus <selector> -w <hwnd>", true),
        new("windows.open", "windows.open <Component>", NoPreviewHostYet, false),
        new("windows.close", "windows.close <id>", "none: close it through the app's UI (winapp ui invoke <close-button>)", false),
        new("components", "components", NoPreviewHostYet, false),
        new("switch", "switch <component>", NoPreviewHostYet, false),
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
        new("reload", "reload", WatchEquivalent + "  (.NET Hot Reload for edits; rebuilds and re-attaches on rude edits)", true),
        new("logs", "logs", "winapp devtools logs [--follow]", true),
        new("shutdown", "shutdown", "none: close the app through its UI or stop the winapp run process", false),
        new(ReferencesKey, "call references", "winapp devtools get-property <selector>  (References section)", true),
        new("call", "call <tool>", "none: use the matching winapp verb with --json", false),
    };

    private static readonly Dictionary<string, DevtoolsVerbMapping> s_byKey =
        Mappings.ToDictionary(m => m.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Finds the row for an exact key, or null.</summary>
    internal static DevtoolsVerbMapping? Find(string key) =>
        s_byKey.TryGetValue(key, out var m) ? m : null;

    /// <summary>
    /// Picks the mapping row for a <c>mur devtools</c> argv, mirroring the
    /// routing in <see cref="DevtoolsSupervisor.Run"/>.
    /// </summary>
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
            // `call references` drove the references overlay; it has its own row.
            if (string.Equals(first, "call", StringComparison.OrdinalIgnoreCase) && args.Length > 1
                && string.Equals(args[1], ReferencesKey, StringComparison.OrdinalIgnoreCase))
                return s_byKey[ReferencesKey];
            if (DevtoolsVerbs.KnownVerbs.Contains(first) && s_byKey.TryGetValue(first, out var verb))
                return verb;
        }
        if (args.Contains("--print-config"))
            return s_byKey["--print-config"];
        if (args.Contains("--component"))
            return s_byKey["--component"];
        return s_byKey[LaunchKey];
    }

    /// <summary>True when <see cref="SuppressEnvVar"/> is set to 1/true/yes.</summary>
    internal static bool IsSuppressed(Func<string, string?> getEnv)
    {
        var v = getEnv(SuppressEnvVar)?.Trim();
        return v is not null && (v == "1"
            || v.Equals("true", StringComparison.OrdinalIgnoreCase)
            || v.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The short notice printed on every invocation.</summary>
    internal static string BuildNotice(string[] args)
    {
        var m = Resolve(args);
        var sb = new StringBuilder();
        sb.Append(Prefix).AppendLine("DEPRECATED: `mur devtools` and the Microsoft.UI.Reactor.Devtools package are deprecated and will be removed one Reactor minor release after this notice.");
        sb.Append(Prefix).AppendLine($"Use the WinUI DevTools in the winapp CLI instead: `{LaunchEquivalent}`. AI agents: use the `{AgentSkill}` skill.");
        sb.Append(Prefix).Append("Instead of `").Append(m.MurForm.StartsWith("mur ", StringComparison.Ordinal) ? m.MurForm : "mur devtools " + m.MurForm).Append("`: ");
        sb.AppendLine(m.HasEquivalent ? "`" + StripNote(m.WinappForm) + "`" + Note(m.WinappForm) : m.WinappForm);
        sb.Append(Prefix).AppendLine($"Full mapping: `mur devtools --help`. Hide this notice: set {SuppressEnvVar}=1. This command still works for now.");
        return sb.ToString();
    }

    /// <summary>The full migration table (printed by <c>mur devtools --help</c>).</summary>
    internal static string BuildMappingTable()
    {
        var width = Mappings.Max(m => m.MurForm.Length);
        var sb = new StringBuilder();
        sb.AppendLine("DEPRECATED: Reactor's built-in DevTools are replaced by the WinUI DevTools in the winapp CLI");
        sb.AppendLine($"(`{LaunchEquivalent}`; agents use the `{AgentSkill}` skill). `mur devtools` keeps working until it is");
        sb.AppendLine("removed one Reactor minor release after this notice. Add -a <app> to winapp commands when more than");
        sb.AppendLine("one app is running.");
        sb.AppendLine();
        sb.Append("  ").Append("mur devtools".PadRight(width)).AppendLine("  winapp");
        sb.Append("  ").Append(new string('-', width)).Append("  ").AppendLine(new string('-', 6));
        foreach (var m in Mappings)
            sb.Append("  ").Append(m.MurForm.PadRight(width)).Append("  ").AppendLine(m.WinappForm);
        return sb.ToString();
    }

    /// <summary>Writes <see cref="BuildNotice"/> unless suppressed.</summary>
    internal static void WriteNotice(TextWriter error, string[] args, Func<string, string?> getEnv)
    {
        if (IsSuppressed(getEnv)) return;
        error.Write(BuildNotice(args));
    }

    // "winapp ui click <s>  (note)" → command part / "  (note)" part.
    private static string StripNote(string form)
    {
        var i = form.IndexOf("  (", StringComparison.Ordinal);
        return i < 0 ? form : form[..i];
    }

    private static string Note(string form)
    {
        var i = form.IndexOf("  (", StringComparison.Ordinal);
        return i < 0 ? "" : " " + form[(i + 2)..];
    }
}
