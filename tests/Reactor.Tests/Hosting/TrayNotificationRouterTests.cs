using Microsoft.UI.Reactor.Hosting.Shell;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Hosting;

/// <summary>
/// Regression guard for issue #1180 — tray <c>Click</c> and <c>RightClick</c>
/// fired twice per physical interaction because the callback switch routed
/// both the legacy mouse message and its NOTIFYICON_VERSION_4 counterpart.
/// </summary>
/// <remarks>
/// The notification ids are spelled as Win32 literals rather than read back
/// from <see cref="TrayIconComInterop"/> so the oracle is independent of the
/// product's own constants; <see cref="Notification_Ids_MatchWin32"/> pins the
/// two together.
/// </remarks>
public class TrayNotificationRouterTests
{
    // Win32 mouse messages (winuser.h). Explorer forwards these for a tray
    // interaction even under NOTIFYICON_VERSION_4.
    private const uint WM_CONTEXTMENU    = 0x007B;
    private const uint WM_LBUTTONDOWN    = 0x0201;
    private const uint WM_LBUTTONUP      = 0x0202;
    private const uint WM_LBUTTONDBLCLK  = 0x0203;
    private const uint WM_RBUTTONDOWN    = 0x0204;
    private const uint WM_RBUTTONUP      = 0x0205;

    // Shell notification ids (shellapi.h), based at WM_USER (0x0400).
    private const uint NIN_SELECT          = 0x0400;
    private const uint NIN_KEYSELECT       = 0x0401;
    private const uint NIN_BALLOONSHOW     = 0x0402;
    private const uint NIN_BALLOONHIDE     = 0x0403;
    private const uint NIN_BALLOONTIMEOUT  = 0x0404;
    private const uint NIN_BALLOONUSERCLICK = 0x0405;
    private const uint NIN_POPUPOPEN       = 0x0406;
    private const uint NIN_POPUPCLOSE      = 0x0407;

    /// <summary>
    /// Push a whole notification sequence through the real routing path and
    /// return how many times each tray event fired.
    /// </summary>
    private static (int Click, int DoubleClick, int RightClick) Feed(params uint[] notifications)
    {
        int click = 0, doubleClick = 0, rightClick = 0;
        var entry = new TrayCallbackEntry
        {
            OnClick = () => click++,
            OnDoubleClick = () => doubleClick++,
            OnRightClick = () => rightClick++,
        };

        foreach (var notification in notifications)
            TrayNotificationRouter.Dispatch(entry, TrayNotificationRouter.Classify(notification));

        return (click, doubleClick, rightClick);
    }

    // ── The issue #1180 oracles ──────────────────────────────────────────

    /// <summary>
    /// One physical left click delivers <c>WM_LBUTTONUP</c> <b>and</b>
    /// <c>NIN_SELECT</c>. Exactly one <c>Click</c> must come out — routing
    /// both arms is what made it two.
    /// </summary>
    [Fact]
    public void SingleLeftClick_RaisesClickExactlyOnce()
    {
        var fired = Feed(WM_LBUTTONDOWN, WM_LBUTTONUP, NIN_SELECT);

        Assert.Equal(1, fired.Click);
        Assert.Equal(0, fired.DoubleClick);
        Assert.Equal(0, fired.RightClick);
    }

    /// <summary>
    /// One physical right click delivers <c>WM_RBUTTONUP</c> <b>and</b>
    /// <c>WM_CONTEXTMENU</c>. Exactly one <c>RightClick</c> must come out.
    /// </summary>
    [Fact]
    public void SingleRightClick_RaisesRightClickExactlyOnce()
    {
        var fired = Feed(WM_RBUTTONDOWN, WM_RBUTTONUP, WM_CONTEXTMENU);

        Assert.Equal(1, fired.RightClick);
        Assert.Equal(0, fired.Click);
        Assert.Equal(0, fired.DoubleClick);
    }

    /// <summary>
    /// The legacy mouse messages carry no routing on their own — that is the
    /// whole fix. Feeding them in isolation must produce nothing.
    /// </summary>
    [Theory]
    [InlineData(WM_LBUTTONDOWN)]
    [InlineData(WM_LBUTTONUP)]
    [InlineData(WM_RBUTTONDOWN)]
    [InlineData(WM_RBUTTONUP)]
    public void LegacyMouseMessage_IsNotRouted(uint notification)
    {
        Assert.Equal(TrayCallbackKind.None, TrayNotificationRouter.Classify(notification));
        Assert.Equal((0, 0, 0), Feed(notification));
    }

    /// <summary>
    /// Repeated interactions still scale one-for-one, so a passing count of
    /// one cannot be an artifact of the handler running at most once.
    /// </summary>
    [Fact]
    public void RepeatedInteractions_RaiseOneEventEach()
    {
        var fired = Feed(
            WM_LBUTTONUP, NIN_SELECT,
            WM_LBUTTONUP, NIN_SELECT,
            WM_LBUTTONUP, NIN_SELECT,
            WM_RBUTTONUP, WM_CONTEXTMENU,
            WM_RBUTTONUP, WM_CONTEXTMENU);

        Assert.Equal(3, fired.Click);
        Assert.Equal(2, fired.RightClick);
        Assert.Equal(0, fired.DoubleClick);
    }

    // ── Surviving routes ─────────────────────────────────────────────────

    /// <summary>
    /// A physical double click raises <c>Click</c> once (the first click's
    /// <c>NIN_SELECT</c>) and <c>DoubleClick</c> once. Both are real events,
    /// not a duplicate — <c>WM_LBUTTONDBLCLK</c> has no v4 counterpart.
    /// </summary>
    [Fact]
    public void DoubleClick_RaisesClickOnceAndDoubleClickOnce()
    {
        var fired = Feed(
            WM_LBUTTONDOWN, WM_LBUTTONUP, NIN_SELECT,
            WM_LBUTTONDBLCLK, WM_LBUTTONUP);

        Assert.Equal(1, fired.DoubleClick);
        Assert.Equal(1, fired.Click);
        Assert.Equal(0, fired.RightClick);
    }

    /// <summary>Keyboard activation (SPACE / ENTER) routes to <c>Click</c>.</summary>
    [Fact]
    public void KeyboardSelect_RaisesClickExactlyOnce()
    {
        var fired = Feed(NIN_KEYSELECT);

        Assert.Equal(1, fired.Click);
        Assert.Equal(0, fired.DoubleClick);
        Assert.Equal(0, fired.RightClick);
    }

    /// <summary>Balloon and popup lifecycle notifications surface no event.</summary>
    [Theory]
    [InlineData(NIN_BALLOONSHOW)]
    [InlineData(NIN_BALLOONHIDE)]
    [InlineData(NIN_BALLOONTIMEOUT)]
    [InlineData(NIN_BALLOONUSERCLICK)]
    [InlineData(NIN_POPUPOPEN)]
    [InlineData(NIN_POPUPCLOSE)]
    public void BalloonAndPopupNotification_IsNotRouted(uint notification)
    {
        Assert.Equal(TrayCallbackKind.None, TrayNotificationRouter.Classify(notification));
        Assert.Equal((0, 0, 0), Feed(notification));
    }

    // ── Mapping + wiring ─────────────────────────────────────────────────

    [Fact]
    public void Classify_MapsNotificationToKind()
    {
        Assert.Equal(TrayCallbackKind.Click, TrayNotificationRouter.Classify(NIN_SELECT));
        Assert.Equal(TrayCallbackKind.Click, TrayNotificationRouter.Classify(NIN_KEYSELECT));
        Assert.Equal(TrayCallbackKind.DoubleClick, TrayNotificationRouter.Classify(WM_LBUTTONDBLCLK));
        Assert.Equal(TrayCallbackKind.RightClick, TrayNotificationRouter.Classify(WM_CONTEXTMENU));
        Assert.Equal(TrayCallbackKind.None, TrayNotificationRouter.Classify(0u));
        Assert.Equal(TrayCallbackKind.None, TrayNotificationRouter.Classify(0xFFFFu));
    }

    /// <summary>
    /// Each kind reaches its own sink, so a routed event cannot be credited to
    /// the wrong handler.
    /// </summary>
    [Fact]
    public void Dispatch_InvokesOnlyTheMatchingSink()
    {
        Assert.Equal((1, 0, 0), DispatchOne(TrayCallbackKind.Click));
        Assert.Equal((0, 1, 0), DispatchOne(TrayCallbackKind.DoubleClick));
        Assert.Equal((0, 0, 1), DispatchOne(TrayCallbackKind.RightClick));
        Assert.Equal((0, 0, 0), DispatchOne(TrayCallbackKind.None));
    }

    private static (int Click, int DoubleClick, int RightClick) DispatchOne(TrayCallbackKind kind)
    {
        int click = 0, doubleClick = 0, rightClick = 0;
        var entry = new TrayCallbackEntry
        {
            OnClick = () => click++,
            OnDoubleClick = () => doubleClick++,
            OnRightClick = () => rightClick++,
        };

        TrayNotificationRouter.Dispatch(entry, kind);

        return (click, doubleClick, rightClick);
    }

    /// <summary>
    /// Pins the literals above to the product's interop constants — the tests
    /// only measure routing if both sides name the same Win32 message.
    /// </summary>
    [Fact]
    public void Notification_Ids_MatchWin32()
    {
        Assert.Equal(TrayIconComInterop.NIN_SELECT, NIN_SELECT);
        Assert.Equal(TrayIconComInterop.NIN_KEYSELECT, NIN_KEYSELECT);
        Assert.Equal(TrayIconComInterop.WM_LBUTTONUP, WM_LBUTTONUP);
        Assert.Equal(TrayIconComInterop.WM_LBUTTONDBLCLK, WM_LBUTTONDBLCLK);
        Assert.Equal(TrayIconComInterop.WM_RBUTTONUP, WM_RBUTTONUP);
        Assert.Equal(TrayIconComInterop.WM_CONTEXTMENU, WM_CONTEXTMENU);
    }
}
