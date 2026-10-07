using System.ComponentModel;
using Microsoft.UI.Reactor.Core;

namespace Microsoft.UI.Reactor.Diagnostics;

/// <summary>
/// Root mount call sites — where an app called <c>ReactorApp.Run</c>,
/// <c>ReactorApp.OpenWindow</c>, <c>ReactorWindow.Mount</c>, <c>ReactorHost.Mount</c> or
/// <c>ReactorHostControl.Mount</c>. Surfaced to inspectors through
/// <see cref="Microsoft.UI.Reactor.Core.Diagnostics.ReactorHostInfo.MountSite"/>.
///
/// <para><b>How the site gets here.</b> A root is not an element, so there is no
/// <see cref="Element.CallSite"/> to stamp. Instead the source-map generator intercepts
/// those entry points and brackets the real call with <see cref="EnterRootMountSite"/> /
/// <see cref="ExitRootMountSite"/>. No public signature changes, and no
/// <c>[CallerFilePath]</c> literal is baked into builds that did not opt in to source
/// mapping.</para>
///
/// <para><b>Who claims a site.</b> Each intercepted entry point claims its OWN scope as
/// the first thing it does (<see cref="TakeRootMountSite"/>) and then hands the site down
/// explicitly to the host it mounts. A site is therefore never "the next mount wins": by
/// the time any other code runs inside an intercepted call — a <c>configure</c> callback
/// that mounts a second host, a framework-created window — that call's scope is already
/// claimed, so the nested mount sees no site rather than a wrong one. <c>Run</c> claims on
/// its caller's thread before it starts WinUI and carries the site through its startup
/// options, so nothing ever has to cross threads; the scope stack is therefore
/// per-thread.</para>
///
/// <para><b>When the flag is read.</b> The scope opens whatever
/// <see cref="Enabled"/> says at call time, and the host decides at mount time
/// (<see cref="KeepIfEnabled"/>). That ordering matters for <c>ReactorApp.Run</c>: under
/// <c>--devtools app</c> the flag is switched on by the devtools bootstrap inside Run,
/// after Run's own call site has already been taken, and the primary window mounts later
/// still. Gating the capture on the flag would lose exactly the site an inspector came
/// for; gating the store keeps an ordinary launch from recording anything.</para>
///
/// <para>Cost: one small allocation per intercepted root mount call (a handful per
/// process), and only in builds compiled with source mapping, since nothing else
/// calls these hooks.</para>
/// </summary>
public static partial class ReactorSourceMap
{
    [ThreadStatic] private static RootMountFrame? t_rootMountTop;

    /// <summary>
    /// Test seam: observes every site <see cref="EnterRootMountSite"/> opens. Lets a
    /// headless test prove an interceptor ran even when the intercepted call throws
    /// before reaching a host.
    /// </summary>
#pragma warning disable CS0649 // Assigned via InternalsVisibleTo (Reactor.SourceMap.Tests), never inside this assembly.
    internal static Action<SourceLocation>? RootMountSiteEnteredForTest;

    /// <summary>
    /// Test seam: observes every site <see cref="TakeRootMountSite"/> hands out, so a test
    /// can prove the intercepted entry point claimed its own scope.
    /// </summary>
    internal static Action<SourceLocation>? RootMountSiteClaimedForTest;
#pragma warning restore CS0649

    /// <summary>
    /// Infrastructure for generated source-map interceptors; not intended to be
    /// called directly. Opens a root mount scope for the call written at
    /// <paramref name="filePath"/>:<paramref name="lineNumber"/>, and returns a token
    /// for <see cref="ExitRootMountSite"/>. The scope opens regardless of
    /// <see cref="Enabled"/>; whether the site is kept is decided when the host mounts.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static object? EnterRootMountSite(string filePath, int lineNumber)
    {
        var site = new SourceLocation(filePath, lineNumber);
        var frame = new RootMountFrame(site, t_rootMountTop);
        t_rootMountTop = frame;
        RootMountSiteEnteredForTest?.Invoke(site);
        return frame;
    }

    /// <summary>
    /// Infrastructure for generated source-map interceptors; not intended to be
    /// called directly. Closes the scope <paramref name="token"/> opened. A
    /// <c>null</c> or foreign token is a no-op, and an out-of-order close unlinks just
    /// that scope, so a throwing call never leaves a stale site behind.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void ExitRootMountSite(object? token)
    {
        if (token is not RootMountFrame frame) return;

        if (ReferenceEquals(t_rootMountTop, frame))
        {
            t_rootMountTop = frame.Previous;
            return;
        }

        for (var node = t_rootMountTop; node is not null; node = node.Previous)
        {
            if (ReferenceEquals(node.Previous, frame))
            {
                node.Previous = frame.Previous;
                return;
            }
        }
    }

    /// <summary>
    /// Claims the innermost open root mount scope on this thread, or returns null when
    /// none is open or it was already claimed. Called first thing by every intercepted
    /// entry point, so it only ever claims the scope that entry point's own interceptor
    /// opened.
    /// </summary>
    internal static SourceLocation? TakeRootMountSite()
    {
        var top = t_rootMountTop;
        if (top is null || top.Claimed) return null;
        top.Claimed = true;
        RootMountSiteClaimedForTest?.Invoke(top.Site);
        return top.Site;
    }

    /// <summary>
    /// The site a host stores for its root: <paramref name="site"/> while source mapping
    /// is on at mount time, otherwise null — so a launch nobody is inspecting records
    /// nothing, while a site taken before the flag came on (see the remarks on this type)
    /// survives.
    /// </summary>
    internal static SourceLocation? KeepIfEnabled(SourceLocation? site) => Enabled ? site : null;

    /// <summary>Test-only: number of open root mount scopes on this thread.</summary>
    internal static int OpenRootMountScopeCountForTest
    {
        get
        {
            int count = 0;
            for (var node = t_rootMountTop; node is not null; node = node.Previous) count++;
            return count;
        }
    }

    private sealed class RootMountFrame(SourceLocation site, RootMountFrame? previous)
    {
        public SourceLocation Site { get; } = site;
        public RootMountFrame? Previous { get; set; } = previous;
        public bool Claimed { get; set; }
    }
}

/// <summary>
/// A host's root mount site, published so <c>ReactorDiagnostics.GetHosts()</c> can read it
/// from any thread. A <see cref="SourceLocation"/>? is several words (a reference, an
/// <c>int</c> and a has-value flag), so a plain field can be read half-old, half-new while
/// the UI thread remounts. Storing it behind one immutable box makes every write and read a
/// single atomic reference operation.
/// </summary>
internal sealed class RootMountSiteSlot
{
    private sealed class Box(SourceLocation site)
    {
        public SourceLocation Site { get; } = site;
    }

    private Box? _box;

    public SourceLocation? Value
    {
        get => Volatile.Read(ref _box)?.Site;
        set => Volatile.Write(ref _box, value is { } site ? new Box(site) : null);
    }
}
