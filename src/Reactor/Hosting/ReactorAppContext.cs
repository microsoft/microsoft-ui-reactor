using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Navigation;

namespace Microsoft.UI.Reactor;

/// <summary>
/// The argument to <see cref="ReactorApp.Run(Action{ReactorAppContext})"/>'s
/// startup callback. A thin facade over the static <see cref="ReactorApp"/>
/// surface giving access to the launch activation. The instance has no
/// lifetime of its own — calls forward to <see cref="ReactorApp"/> and remain
/// valid after <c>Run</c> returns control. (spec 036 §4.3)
/// </summary>
public sealed class ReactorAppContext
{
    /// <summary>
    /// Information about how the process was launched. Populated in Phase 8;
    /// returns a <see cref="LaunchKind.Normal"/> sentinel until then.
    /// </summary>
    public LaunchActivation LaunchActivation { get; }

    internal ReactorAppContext(LaunchActivation activation)
    {
        LaunchActivation = activation;
    }

    /// <summary>Open a window with a <see cref="Component"/> root. UI-thread only.</summary>
    public ReactorWindow OpenWindow(WindowSpec spec, Func<Component> root)
        => ReactorApp.OpenWindow(spec, root);

    /// <summary>Open a window with a render-function root. UI-thread only.</summary>
    public ReactorWindow OpenWindow(WindowSpec spec, Func<RenderContext, Element> render)
        => ReactorApp.OpenWindow(spec, render);

    /// <summary>Look up an open window by <see cref="WindowKey"/>.</summary>
    public ReactorWindow? FindWindow(WindowKey key) => ReactorApp.FindWindow(key);

    /// <summary>Open a system-tray icon. UI-thread only. (spec 036 §11.4)</summary>
    public ReactorTrayIcon OpenTrayIcon(TrayIconSpec spec) => ReactorApp.OpenTrayIcon(spec);

    /// <summary>Look up an open tray icon by <see cref="WindowKey"/>.</summary>
    public ReactorTrayIcon? FindTrayIcon(WindowKey key) => ReactorApp.FindTrayIcon(key);
}

/// <summary>
/// How the process was launched. Phase 1 only carries the
/// <see cref="LaunchKind.Normal"/> sentinel; Phase 8 wires real activation.
/// </summary>
public enum LaunchKind
{
    /// <summary>Launched via the standard executable / shortcut entry point.</summary>
    Normal,

    /// <summary>Launched from a jump-list entry. (Phase 8)</summary>
    JumpList,

    /// <summary>Launched in response to a toast click. (Phase 8)</summary>
    Toast,

    /// <summary>Launched via a custom URI protocol handler. (Phase 8)</summary>
    Protocol,

    /// <summary>Launched via file association. (Phase 8)</summary>
    File,

    /// <summary>Launched via a system-tray "Open"/double-click. (Phase 8)</summary>
    Tray,
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
