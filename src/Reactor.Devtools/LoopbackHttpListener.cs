using System.Net;
using System.Net.Sockets;

namespace Microsoft.UI.Reactor.Hosting.Devtools;

/// <summary>
/// Starts the devtools <see cref="HttpListener"/>s on <c>http://127.0.0.1:{port}/</c>
/// without a probe-then-bind race.
/// </summary>
/// <remarks>
/// Probing for a free port (bind a socket to port 0, read the port, release the
/// socket) and binding <see cref="HttpListener"/> to it later leaves a window in
/// which another process can take the port. Several selftest hosts or devtools
/// sessions on one machine hit it. Keeping the probe socket open does not close
/// the window: HTTP.sys refuses to bind a port that any other socket owns
/// (<see cref="ErrorSharingViolation"/>), so the probe must be released before
/// <see cref="HttpListener.Start"/>. Instead, an unpinned start that loses the
/// race probes a new port and binds again. A pinned port is tried exactly once.
/// </remarks>
internal static class LoopbackHttpListener
{
    /// <summary>
    /// <c>ERROR_SHARING_VIOLATION</c>: another socket, in any process, owns the port.
    /// </summary>
    internal const int ErrorSharingViolation = 32;

    /// <summary>
    /// <c>ERROR_ALREADY_EXISTS</c>: another <see cref="HttpListener"/>, in any
    /// process, already registered the same prefix.
    /// </summary>
    internal const int ErrorAlreadyExists = 183;

    /// <summary>Upper bound on bind attempts for an unpinned port.</summary>
    internal const int MaxAttempts = 16;

    internal static string Prefix(int port) => $"http://127.0.0.1:{port}/";

    /// <summary>
    /// Returns a loopback port that was free when probed. This is a hint, not a
    /// reservation: <see cref="Start"/> recovers if another process takes it first.
    /// </summary>
    internal static int ProbeFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    /// <summary>
    /// True when <see cref="HttpListener.Start"/> failed because another process
    /// holds the port.
    /// </summary>
    internal static bool IsPortInUse(HttpListenerException ex) =>
        ex.ErrorCode is ErrorSharingViolation or ErrorAlreadyExists;

    /// <summary>
    /// Starts a listener on <paramref name="port"/> and returns it with the port it
    /// bound. When <paramref name="pinned"/> is false and another process holds the
    /// port, probes a new one with <paramref name="probePort"/> and tries again, up
    /// to <see cref="MaxAttempts"/> binds. <paramref name="configure"/> runs on each
    /// attempt's listener before it starts. Other bind failures propagate unchanged.
    /// </summary>
    /// <exception cref="LoopbackPortUnavailableException">
    /// The pinned port, or every port tried for an unpinned start, was in use.
    /// </exception>
    internal static (HttpListener Listener, int Port) Start(
        int port,
        bool pinned,
        Action<HttpListener>? configure = null,
        Func<int>? probePort = null)
    {
        for (int attempt = 1; ; attempt++)
        {
            var listener = new HttpListener();
            try
            {
                listener.Prefixes.Add(Prefix(port));
                configure?.Invoke(listener);
                listener.Start();
                return (listener, port);
            }
            catch (HttpListenerException ex) when (IsPortInUse(ex))
            {
                // A failed Start leaves the listener closed; it can't be restarted.
                listener.Close();
                if (pinned || attempt >= MaxAttempts)
                    throw new LoopbackPortUnavailableException(port, pinned, attempt, ex);
            }
            catch
            {
                listener.Close();
                throw;
            }

            port = (probePort ?? ProbeFreePort)();
        }
    }
}

/// <summary>
/// <see cref="LoopbackHttpListener.Start"/> could not bind because another process
/// holds the port: the pinned port, or every port tried for an unpinned start.
/// </summary>
internal sealed class LoopbackPortUnavailableException : IOException
{
    internal LoopbackPortUnavailableException(int port, bool pinned, int attempts, HttpListenerException inner)
        : base(
            pinned
                ? $"Port {port} on 127.0.0.1 is already in use by another process."
                : $"Every loopback port tried was taken by another process before it could be bound ({attempts} attempts, last port {port}).",
            inner)
    {
        Port = port;
        Pinned = pinned;
        Attempts = attempts;
    }

    /// <summary>The pinned port, or the last port tried for an unpinned start.</summary>
    public int Port { get; }

    /// <summary>True when the caller pinned the port, so it was tried exactly once.</summary>
    public bool Pinned { get; }

    /// <summary>How many binds were attempted.</summary>
    public int Attempts { get; }
}
