using System.Drawing;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Lightweight handle to a UIA element, addressed by selector (AutomationId — stable —
/// or a winapp slug). Mirrors the slice of the old Appium <c>WindowsElement</c> surface
/// the test suite actually used (<see cref="Click"/>, <see cref="Invoke"/>, <see cref="SendKeys"/>,
/// <see cref="Clear"/>, <see cref="Text"/>, <see cref="GetAttribute"/>, <see cref="Rect"/>)
/// so existing test bodies keep their shape.
///
/// Reads/actions go through <see cref="WinAppUi"/>; keystroke input goes through
/// <see cref="InputInjector"/>; UIA properties winapp can't surface fall back to
/// <see cref="UiaPropertyReader"/>.
/// </summary>
public sealed class UiElement
{
    private readonly WinAppUi _app;
    private readonly IUiaPropertyReader _uia;
    private readonly long _hwnd;
    private readonly UiRect? _cachedBounds;

    /// <summary>The selector used to address this element (AutomationId when available).</summary>
    public string Selector { get; }

    /// <summary>The AutomationId, when this handle was resolved from one (enables UIA fallback).</summary>
    public string? AutomationId { get; }

    internal UiElement(WinAppUi app, IUiaPropertyReader uia, string selector, string? automationId, long hwnd,
        UiRect? cachedBounds = null)
    {
        _app = app;
        _uia = uia;
        Selector = selector;
        AutomationId = automationId;
        _hwnd = hwnd;
        _cachedBounds = cachedBounds;
    }

    /// <summary>Perform a real pointer click at the element center.</summary>
    public void Click()
    {
        var rect = Rect;
        InputInjector.Foreground(_hwnd == 0 ? _app.HostHwnd : _hwnd);
        InputInjector.Click(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
    }

    /// <summary>Activate the element via UIA invoke/toggle/selection patterns.</summary>
    public void Invoke() => _app.Invoke(Selector, _hwnd);

    /// <summary>The element's text/value (TextPattern → ValuePattern → Name).</summary>
    public string? Text => _app.GetValue(Selector, _hwnd);

    /// <summary>
    /// Read a UIA property by name. Tries winapp <c>get-property</c> first; for properties
    /// winapp 0.3.2 returns null on (and when this handle has an AutomationId), falls back to
    /// the in-process UIA reader so the accessibility suite keeps parity with WinAppDriver.
    /// </summary>
    public string? GetAttribute(string name)
    {
        var viaWinApp = _app.GetProperty(Selector, name, _hwnd);
        if (viaWinApp != null) return viaWinApp;

        if (AutomationId != null && _uia.Handles(name))
            return _uia.ReadByAutomationId(AutomationId, name);

        return viaWinApp;
    }

    /// <summary>Bounding rectangle in physical screen pixels.</summary>
    public Rectangle Rect
    {
        get
        {
            // Prefer bounds captured at resolution time. Elements resolved by Name (FindByName)
            // are addressed by winapp's volatile semantic slug (e.g. "lbl-right-4732"), which
            // winapp cannot re-resolve as a search selector — the hash is a display hint, not a
            // stable handle — so a fresh GetBounds would return "not found". Caching the bounds
            // from the original search keeps drag-target resolution stable. Elements resolved by
            // a stable AutomationId have no cached bounds and re-resolve live (always findable).
            var b = _cachedBounds
                ?? _app.GetBounds(Selector, _hwnd)
                ?? throw new WinAppException($"Element '{Selector}' has no bounds (not found).");
            return new Rectangle(b.X, b.Y, b.Width, b.Height);
        }
    }

    /// <summary>
    /// Type into the element. Foregrounds the host window, focuses this element via UIA,
    /// then injects the keystrokes with <see cref="InputInjector"/> (winapp has no typing).
    /// </summary>
    public void SendKeys(string keys)
    {
        InputInjector.Foreground(_hwnd == 0 ? _app.HostHwnd : _hwnd);
        TryFocus();

        // UIA SetFocus select-alls the control's existing text. When two SUCCESSIVE SendKeys
        // calls target the SAME already-focused field, the second call's re-focus re-selects
        // everything typed so far, so the next characters overwrite instead of append — that is
        // the "typed '2' then '5', only '5' landed" failure. Collapse the selection to the end
        // (press End) before typing so consecutive sends append like a real user.
        //
        // This is gated to consecutive same-selector sends rather than applied to every text
        // send, because injecting End in the MIDDLE of a single multi-character send corrupts
        // it: an Immediate NumberBox re-renders mid-stream and a stray End reorders the digits
        // (a lone SendKeys("25") came out "52"). A single send — even multi-char — must be left
        // untouched; only a repeat send into the same field needs the leading collapse.
        //
        // The discriminator is the selector of the previous text send (not a winapp get-value
        // emptiness check): composite editors such as NumberBox expose their value via
        // RangeValuePattern, which winapp get-value cannot read, so a value-based guard is
        // unreliable for exactly the NumberBox case this protects.
        //
        // TODO: once winappCli #562 (native send-keys) ships, the focus/echo handling moves
        // into winapp and this collapse step can be dropped with the SendInput fallback.
        if (ContainsTypedText(keys))
        {
            if (RememberTextSendAndWasRepeat(Selector))
                InputInjector.CollapseSelectionToEnd();
        }

        InputInjector.TypeKeys(keys);
    }

    // Selector of the most recent literal-text SendKeys, used to detect consecutive sends into
    // the same already-focused field (the only case that needs collapse-to-end). Reset on
    // fixture navigation so equality can't leak across fixtures. Sentinel-only sends (Tab/Enter)
    // leave it unchanged — they carry no text and don't re-select.
    private static string? _lastTextSendSelector;

    /// <summary>Clears the consecutive-send tracking; call when navigating to a fresh fixture.</summary>
    internal static void ResetTypingContext() => _lastTextSendSelector = null;

    // Records this send's selector and reports whether it repeats the previous one (the only case
    // that needs collapse-to-end). Encapsulating the static read+write keeps the mutation off the
    // instance SendKeys path while preserving the process-global "last send" semantics.
    private static bool RememberTextSendAndWasRepeat(string selector)
    {
        var repeat = _lastTextSendSelector == selector;
        _lastTextSendSelector = selector;
        return repeat;
    }

    // True when the payload contains at least one literal character to type (as opposed to
    // only Keys.* sentinels, which live in the Unicode Private Use Area, e.g. Tab = '\ue004').
    private static bool ContainsTypedText(string keys) =>
        keys.Any(ch => ch < '\ue000' || ch > '\ue0ff');

    /// <summary>Clear the editable control (select-all + delete via injected keys).</summary>
    public void Clear()
    {
        InputInjector.Foreground(_hwnd == 0 ? _app.HostHwnd : _hwnd);
        TryFocus();
        InputInjector.ClearViaKeyboard();
    }

    /// <summary>Move keyboard focus to this element via UIA SetFocus.</summary>
    public void Focus() => TryFocus();

    private void TryFocus()
    {
        try { _app.Focus(Selector, _hwnd); }
        catch (WinAppException) { /* some elements reject SetFocus; typing still targets the foreground focus */ }
    }
}
