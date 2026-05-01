using System.Text.Json;
namespace Microsoft.UI.Reactor.Cli.Devtools;

/// <summary>
/// <c>mur devtools session list</c> and <c>mur devtools session clean</c>.
/// Spec 025 §7.
/// </summary>
internal static class SessionCommands
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return (int)DevtoolsCliExit.Usage;
        }
        var sub = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return sub switch
        {
            "list" => RunList(rest),
            "clean" => RunClean(rest),
            "--help" or "-h" => PrintHelpOk(),
            _ => UsageError($"Unknown session subverb: {sub}"),
        };
    }

    private static int PrintHelpOk()
    {
        PrintHelp();
        return (int)DevtoolsCliExit.Success;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("mur devtools session list [--pretty]");
        Console.WriteLine("mur devtools session clean [--dry-run]");
    }

    private static int UsageError(string msg)
    {
        Console.Error.WriteLine($"[mur devtools session] {msg}");
        return (int)DevtoolsCliExit.Usage;
    }

    private static int RunList(string[] args)
    {
        bool pretty = args.Contains("--pretty");

        var rows = new List<LockfileEntry>();
        foreach (var (path, entry) in LockfileReader.EnumerateAll())
        {
            if (entry is null)
            {
                LockfileReader.TryDelete(path);
                continue;
            }
            if (!LockfileReader.IsLive(entry))
            {
                LockfileReader.TryDelete(path);
                continue;
            }
            rows.Add(entry);
        }

        if (rows.Count == 0) return (int)DevtoolsCliExit.NoLiveSession;

        if (pretty)
        {
            // SECURITY (TASK-032): sanitize same-user-controlled lockfile
            // fields before printing so a hostile co-tenant can't slip ANSI
            // escapes or bidi-overrides past a downstream LLM agent.
            Console.WriteLine($"{"PID",-8} {"PORT",-6} {"TRANSPORT",-10} {"ENDPOINT",-40} PROJECT");
            foreach (var e in rows)
                Console.WriteLine($"{e.Pid,-8} {e.Port,-6} {LockfileReader.SafeForTerminal(e.Transport, 16),-10} {LockfileReader.SafeForTerminal(e.Endpoint, 80),-40} {LockfileReader.SafeForTerminal(e.Project, 200)}");
        }
        else
        {
            // JSON output: don't echo the bearer token. JSON itself escapes
            // control characters, but the token would round-trip plaintext to
            // anyone reading the output.
            var opts = new JsonSerializerOptions { DefaultIgnoreCondition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
            foreach (var e in rows)
            {
                var redacted = new LockfileEntry
                {
                    Schema = e.Schema,
                    Endpoint = e.Endpoint,
                    Transport = e.Transport,
                    Port = e.Port,
                    Pid = e.Pid,
                    BuildTag = e.BuildTag,
                    Project = e.Project,
                    StartedAt = e.StartedAt,
                    Token = string.IsNullOrEmpty(e.Token) ? "" : "***",
                };
                Console.WriteLine(JsonSerializer.Serialize(redacted, opts));
            }
        }
        return (int)DevtoolsCliExit.Success;
    }

    private static int RunClean(string[] args)
    {
        bool dryRun = args.Contains("--dry-run");
        int removed = 0;
        foreach (var (path, entry) in LockfileReader.EnumerateAll())
        {
            bool stale = entry is null || !LockfileReader.IsLive(entry);
            if (!stale) continue;
            if (dryRun)
            {
                Console.Error.WriteLine($"[dry-run] would remove {path}");
            }
            else
            {
                LockfileReader.TryDelete(path);
            }
            removed++;
        }
        Console.Error.WriteLine(dryRun
            ? $"would remove {removed} stale entr{(removed == 1 ? "y" : "ies")}"
            : $"removed {removed} stale entr{(removed == 1 ? "y" : "ies")}");
        return (int)DevtoolsCliExit.Success;
    }
}
