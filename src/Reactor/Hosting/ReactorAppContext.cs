using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;

namespace Microsoft.UI.Reactor;

/// <summary>
/// The argument to <see cref="ReactorApp.Run(Action{ReactorAppContext})"/>'s
/// startup callback. Carries the launch activation; all surface-management
/// operations (open / find a window or tray icon, register events) go directly
/// through the static <see cref="ReactorApp"/> API — call sites mix freely.
/// (spec 036 §4.3)
/// </summary>
/// <remarks>
/// Keep the context small on purpose: the static <see cref="ReactorApp"/>
/// surface is the single source of truth for windows / tray icons / shutdown
/// policy, and remains valid before <c>Run</c> enters this callback and after
/// it returns. Doubling the API on the context just doubled the discovery
/// cost without adding capability.
/// </remarks>
public sealed class ReactorAppContext
{
    /// <summary>How the process was launched. See <see cref="Microsoft.UI.Reactor.LaunchActivation"/>.</summary>
    public LaunchActivation LaunchActivation { get; }

    internal ReactorAppContext(LaunchActivation activation)
    {
        LaunchActivation = activation;
    }
}

/// <summary>
/// How the process was launched.
/// </summary>
/// <remarks>
/// <para>The OS shell presents jump-list entries, tray "Open" / double-click
/// commands, and thumbnail-toolbar buttons as plain process re-launches —
/// all three arrive at <c>OnLaunched</c> with the same shape (a non-empty
/// argument string and no extended activation kind). Reactor cannot
/// distinguish them from the WinUI surface, so all three roll up under
/// <see cref="JumpList"/>. Apps that need finer granularity should encode
/// it in the argument URI itself (e.g. a path prefix or query parameter)
/// and inspect <see cref="LaunchActivation.Arguments"/>.</para>
/// </remarks>
public enum LaunchKind
{
    /// <summary>Launched via the standard executable / shortcut entry point.</summary>
    Normal,

    /// <summary>
    /// Launched with a non-empty argument string from a shell re-launch — jump
    /// list, tray "Open", or thumbnail-toolbar button. The three sources are
    /// indistinguishable at the WinUI activation surface.
    /// </summary>
    JumpList,

    /// <summary>Launched in response to a toast click.</summary>
    Toast,

    /// <summary>Launched via a custom URI protocol handler.</summary>
    Protocol,

    /// <summary>Launched via file association.</summary>
    File,
}

/// <summary>
/// Parsed launch-activation payload. The argument string and file list are
/// best-effort — both can be empty when the OS surface didn't provide them.
/// </summary>
public sealed record LaunchActivation(
    LaunchKind Kind,
    string? Arguments,
    IReadOnlyList<string> Files)
{
    /// <summary>Sentinel for Phase 1, when no real activation parsing exists yet.</summary>
    public static LaunchActivation Normal { get; } = new(LaunchKind.Normal, null, Array.Empty<string>());

    /// <summary>
    /// Resolve <see cref="Arguments"/> as a deep-link URI through the supplied
    /// <see cref="DeepLinkMap{TRoute}"/>. Returns <c>true</c> only when
    /// <see cref="Arguments"/> is non-empty <b>and</b> the map matched a
    /// registered pattern. The shell-launch convention for Reactor is that
    /// jump-list / tray / thumbnail-toolbar entries carry deep-link URIs in
    /// their argument strings (see <see cref="JumpListItem.ForUri"/>); this
    /// helper plumbs that convention into the navigation system. (spec 036
    /// §11.6)
    /// </summary>
    public bool TryResolve<TRoute>(DeepLinkMap<TRoute> map, out DeepLinkResult<TRoute> result)
        where TRoute : notnull
    {
        ArgumentNullException.ThrowIfNull(map);
        if (string.IsNullOrEmpty(Arguments))
        {
            result = default;
            return false;
        }
        result = map.Resolve(Arguments!);
        return result.Matched;
    }
}
