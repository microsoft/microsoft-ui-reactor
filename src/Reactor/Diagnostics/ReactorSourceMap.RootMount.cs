using System.ComponentModel;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Diagnostics;

/// <summary>
/// Root mount call sites — where an app called <c>ReactorApp.Run</c>,
/// <c>ReactorApp.OpenWindow</c>, <c>ReactorHost.Mount</c> or
/// <c>ReactorHostControl.Mount</c>. Surfaced to inspectors through
/// <see cref="Microsoft.UI.Reactor.Core.Diagnostics.ReactorHostInfo.MountSite"/>.
///
/// <para><b>How the site gets here.</b> A root is not an element, so there is no
/// <see cref="Element.CallSite"/> to stamp. Instead the source-map generator
/// intercepts those entry points and brackets the real call with
/// <see cref="EnterRootMountSite"/> / <see cref="ExitRootMountSite"/>; the host
/// claims the innermost open site when it mounts. No public signature changes, and
/// no <c>[CallerFilePath]</c> literal is baked into builds that did not opt in to
/// source mapping.</para>
///
/// <para><b>Why a process-wide stack and not a thread-static.</b>
/// <c>ReactorApp.Run</c> may start the UI on a fresh STA thread and blocks until the
/// app exits, so the primary window mounts on a different thread from the one that
/// opened the scope. Scopes nest (an intercepted <c>OpenWindow</c> inside Run's
/// startup), and each site is claimed at most once, so Run's site names only the
/// window Run itself creates.</para>
///
/// <para>Cost: nothing when <see cref="Enabled"/> is false — enter returns
/// <c>null</c> without allocating or locking. When enabled, one small allocation
/// per root mount, which happens a handful of times per process.</para>
/// </summary>
public static partial class ReactorSourceMap
{
    private static readonly object s_rootMountGate = new();
    private static RootMountFrame? s_rootMountTop;

    /// <summary>
    /// Test seam: observes every site <see cref="EnterRootMountSite"/> opens. Lets a
    /// headless test prove an interceptor ran even when the intercepted call throws
    /// before reaching a host.
    /// </summary>
#pragma warning disable CS0649 // Assigned via InternalsVisibleTo (Reactor.SourceMap.Tests), never inside this assembly.
    internal static Action<SourceLocation>? RootMountSiteEnteredForTest;
#pragma warning restore CS0649

    /// <summary>
    /// Infrastructure for generated source-map interceptors; not intended to be
    /// called directly. Opens a root mount scope for the call written at
    /// <paramref name="filePath"/>:<paramref name="lineNumber"/>, and returns a token
    /// for <see cref="ExitRootMountSite"/>. Returns <c>null</c> (and records nothing)
    /// when <see cref="Enabled"/> is false.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static object? EnterRootMountSite(string filePath, int lineNumber)
    {
        if (!Enabled) return null;

        var site = new SourceLocation(filePath, lineNumber);
        RootMountFrame frame;
        lock (s_rootMountGate)
        {
            frame = new RootMountFrame(site, s_rootMountTop);
            s_rootMountTop = frame;
        }
        RootMountSiteEnteredForTest?.Invoke(site);
        return frame;
    }

    /// <summary>
    /// Infrastructure for generated source-map interceptors; not intended to be
    /// called directly. Closes the scope <paramref name="token"/> opened. A
    /// <c>null</c> token is a no-op, and an out-of-order close unlinks just that
    /// scope, so a throwing call never leaves a stale site for an unrelated mount.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void ExitRootMountSite(object? token)
    {
        if (token is not RootMountFrame frame) return;

        lock (s_rootMountGate)
        {
            if (ReferenceEquals(s_rootMountTop, frame))
            {
                s_rootMountTop = frame.Previous;
                return;
            }

            for (var node = s_rootMountTop; node is not null; node = node.Previous)
            {
                if (ReferenceEquals(node.Previous, frame))
                {
                    node.Previous = frame.Previous;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Claims the innermost open root mount site, or returns null when none is open
    /// or it was already claimed. Called by the hosts' <c>Mount</c>.
    /// </summary>
    internal static SourceLocation? TakeRootMountSite()
    {
        if (Volatile.Read(ref s_rootMountTop) is null) return null;

        lock (s_rootMountGate)
        {
            var top = s_rootMountTop;
            if (top is null || top.Claimed) return null;
            top.Claimed = true;
            return top.Site;
        }
    }

    /// <summary>Test-only: number of open root mount scopes.</summary>
    internal static int OpenRootMountScopeCountForTest
    {
        get
        {
            lock (s_rootMountGate)
            {
                int count = 0;
                for (var node = s_rootMountTop; node is not null; node = node.Previous) count++;
                return count;
            }
        }
    }

    private sealed class RootMountFrame(SourceLocation site, RootMountFrame? previous)
    {
        public SourceLocation Site { get; } = site;
        public RootMountFrame? Previous { get; set; } = previous;
        public bool Claimed { get; set; }
    }
}
