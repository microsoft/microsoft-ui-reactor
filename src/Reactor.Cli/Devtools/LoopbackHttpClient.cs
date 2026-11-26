using System.Net.Http;

namespace Microsoft.UI.Reactor.Cli.Devtools;

/// <summary>
/// Creates the <see cref="HttpClient"/>s the CLI uses to reach a devtools session.
/// <see cref="EndpointDiscovery"/> only accepts loopback endpoints, so these never
/// consult the system proxy. Mirrors <c>Microsoft.UI.Reactor.Hosting.Devtools.LoopbackHttpClient</c>.
/// </summary>
/// <remarks>
/// A default <see cref="HttpClient"/> resolves the system proxy for every request,
/// loopback included. With Windows "Automatically detect settings" on (the default),
/// that is a blocking <c>WinHttpGetProxyForUrl</c> call in the request path. WPAD
/// discovery on the first request in a process can take longer than the liveness
/// probe's 500 ms budget, and a PAC script that doesn't exempt loopback sends
/// <c>127.0.0.1</c> to a proxy that can't reach it. Either way a live session is
/// reported stale and its lockfile deleted (issue #1718).
/// </remarks>
internal static class LoopbackHttpClient
{
    internal static HttpClient Create(TimeSpan timeout) =>
        new(new SocketsHttpHandler { UseProxy = false }) { Timeout = timeout };
}
