using System.Net.Http;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// Creates the <see cref="HttpClient"/>s that talk to the devtools' own
/// <c>http://127.0.0.1</c> endpoints. They never consult the system proxy or follow redirects.
/// </summary>
/// <remarks>
/// A default <see cref="HttpClient"/> resolves the system proxy for every request,
/// loopback included. With Windows "Automatically detect settings" on (the default),
/// that is a blocking <c>WinHttpGetProxyForUrl</c> call in the request path. WPAD
/// discovery on the first request in a process can take longer than a short probe's
/// whole timeout, and a PAC script that doesn't exempt loopback sends <c>127.0.0.1</c>
/// to a proxy that can't reach it (issue #1718).
/// </remarks>
internal static class LoopbackHttpClient
{
    internal static HttpClient Create(TimeSpan timeout) =>
        new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = timeout };
}
