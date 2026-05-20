using WinUIDock = WinUI.Dock;

namespace Microsoft.UI.Reactor.Docking.Internal;

/// <summary>
/// Forwards upstream <see cref="WinUIDock.IDockBehavior"/> callbacks to the
/// Reactor-side <see cref="IDockBehavior"/>. The two upstream <c>OnDocked</c>
/// overloads (DockManager-destination, DocumentGroup-destination) both map
/// onto the single Reactor <see cref="IDockBehavior.OnDocked"/>; the
/// distinction is exposed through the <see cref="DockTarget"/> arg (edge
/// targets always reach the manager; split / center targets reach a group).
///
/// Phase 2 collapses this into Action props on <c>DockHost</c> per spec 045
/// §5.3.5; the interface remains as a [Obsolete] forwarder for one release.
/// </summary>
internal sealed class BehaviorBridge : WinUIDock.IDockBehavior
{
    private readonly IDockBehavior _behavior;
    private readonly HostState _host;
    private readonly DockingXamlInterop.PaneKeyResolver _resolveKey;

    public BehaviorBridge(IDockBehavior behavior, HostState host, DockingXamlInterop.PaneKeyResolver resolveKey)
    {
        _behavior = behavior;
        _host = host;
        _resolveKey = resolveKey;
    }

    public void ActivateMainWindow()
    {
        // Absorbed by Reactor's window topology (spec 045 §4.3 note). Without a
        // Reactor PrimaryWindow.Activate() hook wired through to this scope yet
        // (P3 fold-in territory), no-op for P1. Cross-DockManager drag is also
        // out of scope for P1 (§4.6) so we don't need this path to be live.
    }

    public void OnDocked(WinUIDock.Document src, WinUIDock.DockManager dest, WinUIDock.DockTarget target)
    {
        var dc = MakeDc(src);
        _behavior.OnDocked(dc, MapTarget(target));
    }

    public void OnDocked(WinUIDock.Document src, WinUIDock.DocumentGroup dest, WinUIDock.DockTarget target)
    {
        var dc = MakeDc(src);
        _behavior.OnDocked(dc, MapTarget(target));
    }

    public void OnFloating(WinUIDock.Document document)
    {
        var dc = MakeDc(document);
        _behavior.OnFloating(dc);
    }

    private DockableContent MakeDc(WinUIDock.Document src) => new(
        Title: src.Title,
        Key: _resolveKey(src),
        CanClose: src.CanClose,
        CanPin: src.CanPin);

    internal static DockTarget MapTarget(WinUIDock.DockTarget t) => t switch
    {
        WinUIDock.DockTarget.Center      => DockTarget.Center,
        WinUIDock.DockTarget.SplitLeft   => DockTarget.SplitLeft,
        WinUIDock.DockTarget.SplitTop    => DockTarget.SplitTop,
        WinUIDock.DockTarget.SplitRight  => DockTarget.SplitRight,
        WinUIDock.DockTarget.SplitBottom => DockTarget.SplitBottom,
        WinUIDock.DockTarget.DockLeft    => DockTarget.DockLeft,
        WinUIDock.DockTarget.DockTop     => DockTarget.DockTop,
        WinUIDock.DockTarget.DockRight   => DockTarget.DockRight,
        WinUIDock.DockTarget.DockBottom  => DockTarget.DockBottom,
        _ => DockTarget.Center,
    };

    internal static WinUIDock.DockTarget UnmapTarget(DockTarget t) => t switch
    {
        DockTarget.Center      => WinUIDock.DockTarget.Center,
        DockTarget.SplitLeft   => WinUIDock.DockTarget.SplitLeft,
        DockTarget.SplitTop    => WinUIDock.DockTarget.SplitTop,
        DockTarget.SplitRight  => WinUIDock.DockTarget.SplitRight,
        DockTarget.SplitBottom => WinUIDock.DockTarget.SplitBottom,
        DockTarget.DockLeft    => WinUIDock.DockTarget.DockLeft,
        DockTarget.DockTop     => WinUIDock.DockTarget.DockTop,
        DockTarget.DockRight   => WinUIDock.DockTarget.DockRight,
        DockTarget.DockBottom  => WinUIDock.DockTarget.DockBottom,
        _ => WinUIDock.DockTarget.Center,
    };
}
