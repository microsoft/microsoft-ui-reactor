namespace Microsoft.UI.Reactor.Cli.Devtools;

/// <summary>
/// CLI exit codes. Spec 025 §9.
/// </summary>
internal enum DevtoolsCliExit
{
    Success = 0,
    Usage = 1,
    Transport = 2,
    AnotherSessionActive = 3,
    NoLiveSession = 4,
    ToolError = 5,
}

/// <summary>
/// Outcome of endpoint resolution. <see cref="Endpoint"/> is populated only
/// when <see cref="Exit"/> is <see cref="DevtoolsCliExit.Success"/>; errors
/// carry the stderr-ready message in <see cref="ErrorMessage"/>.
/// </summary>
internal sealed record EndpointResolution(
    string? Endpoint,
    DevtoolsCliExit Exit,
    string? ErrorMessage,
    string? Token = null);

/// <summary>
/// Resolves the MCP endpoint the CLI should talk to. Spec 025 §5.
///
/// Precedence: <c>--endpoint</c> &gt; lockfile scan &gt; <c>--auto</c> port scan (future).
/// </summary>
internal static class EndpointDiscovery
{
    public static EndpointResolution Resolve(string? explicitEndpoint, bool autoScan)
    {
        if (!string.IsNullOrEmpty(explicitEndpoint))
        {
            // SECURITY (TASK-031): even when the user passes --endpoint, refuse
            // anything that's not loopback HTTP. Otherwise the flag becomes a
            // confused-deputy primitive ("send the agent's token to attacker.com").
            if (!LockfileReader.IsLoopbackHttpEndpoint(explicitEndpoint))
            {
                return new EndpointResolution(
                    null, DevtoolsCliExit.Usage,
                    $"--endpoint must be a loopback http URL; got '{LockfileReader.SafeForTerminal(explicitEndpoint)}'");
            }
            // Explicit endpoint without lockfile means we have no token. The
            // server will 401 unless --endpoint is paired with the token.
            return new EndpointResolution(explicitEndpoint, DevtoolsCliExit.Success, null, Token: null);
        }

        var live = FindLiveHttpSessions();
        if (live.Count == 0)
        {
            return new EndpointResolution(
                Endpoint: null,
                Exit: DevtoolsCliExit.NoLiveSession,
                ErrorMessage: "no running Reactor devtools session; run `mur devtools <project>` to start one");
        }
        if (live.Count == 1)
        {
            var entry = live[0].Entry!;
            return new EndpointResolution(entry.Endpoint, DevtoolsCliExit.Success, null, Token: entry.Token);
        }

        // Multiple live: disambiguate. Spec §5 item 2.
        var sb = new global::System.Text.StringBuilder();
        sb.AppendLine("multiple live devtools sessions found; pass --endpoint to pick one:");
        foreach (var (_, entry) in live)
            sb.AppendLine($"  {LockfileReader.SafeForTerminal(entry!.Endpoint)}  pid={entry.Pid}  {LockfileReader.SafeForTerminal(entry.Project)}");
        return new EndpointResolution(
            Endpoint: null,
            Exit: DevtoolsCliExit.Usage,
            ErrorMessage: sb.ToString().TrimEnd());
    }

    /// <summary>
    /// Enumerates live HTTP sessions under <c>%TEMP%/reactor-devtools/</c>.
    /// Opportunistically GC's stale lockfiles on the way through (spec §5
    /// "Auto-cleanup on read") so readers don't need to run `session clean`
    /// explicitly.
    /// </summary>
    public static List<(string Path, LockfileEntry? Entry)> FindLiveHttpSessions()
    {
        var live = new List<(string, LockfileEntry?)>();
        foreach (var (path, entry) in LockfileReader.EnumerateAll())
        {
            if (entry is null)
            {
                // Parse failure — treat as stale.
                LockfileReader.TryDelete(path);
                continue;
            }
            if (!LockfileReader.IsLive(entry))
            {
                LockfileReader.TryDelete(path);
                continue;
            }
            if (!string.Equals(entry.Transport, "http", StringComparison.OrdinalIgnoreCase))
                continue;
            live.Add((path, entry));
        }
        return live;
    }
}
