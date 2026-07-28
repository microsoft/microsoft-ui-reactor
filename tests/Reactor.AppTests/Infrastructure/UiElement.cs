using System.Drawing;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Lightweight handle to a UIA element, addressed by selector (AutomationId — stable —
/// or a winapp slug). Mirrors the slice of the old Appium <c>WindowsElement</c> surface
/// the test suite actually used (<see cref="Click"/>, <see cref="Invoke"/>, <see cref="SendKeys"/>,
/// <see cref="Clear"/>, <see cref="Text"/>, <see cref="GetAttribute"/>, <see cref="Rect"/>)
/// so existing test bodies keep their shape.
///
/// Reads/actions go through <see cref="WinAppUi"/>; keystroke input goes through the native
/// <c>winapp ui send-keys</c> verb (<c>--via send-input</c> for a real per-character KeyDown);
/// UIA properties winapp can't surface fall back to <see cref="UiaPropertyReader"/>.
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
    public void Click() => _app.Click(Selector, hwnd: _hwnd == 0 ? null : _hwnd);

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
    /// Type into the element. Focuses this element via UIA (best-effort — a control that rejects
    /// SetFocus still receives the keys through the foreground focus), then injects the keystrokes
    /// through the native <c>winapp ui send-keys</c> verb with <c>--via send-input</c> so
    /// keystroke-observing handlers see a real per-character KeyDown.
    /// </summary>
    public void SendKeys(string keys)
    {
        TryFocus();

        var tokens = ToSendKeysTokens(keys);

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
        // Gate on an actual literal-text run — a `text=` token, the only token kind a typed run
        // produces — NOT on "the payload contains a non-sentinel char". The latter also matches the
        // letter in a Ctrl+<letter> chord, which would wrongly prepend End and collapse the very
        // selection a chord such as Ctrl+C is meant to act on.
        if (tokens.Contains("text=") && RememberTextSendAndWasRepeat(Selector))
            tokens = "end " + tokens;

        _app.SendKeys(tokens, viaSendInput: true, hwnd: _hwnd == 0 ? null : _hwnd);
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

    /// <summary>Clear the editable control (select-all + delete via native send-keys).</summary>
    public void Clear()
    {
        TryFocus();
        _app.SendKeys("ctrl+a delete", viaSendInput: true, hwnd: _hwnd == 0 ? null : _hwnd);
    }

    /// <summary>Move keyboard focus to this element via UIA SetFocus.</summary>
    public void Focus() => TryFocus();

    private void TryFocus()
    {
        try { _app.Focus(Selector, _hwnd); }
        catch (WinAppException) { /* some elements reject SetFocus; typing still targets the foreground focus */ }
    }

    // Translate a UiElement.SendKeys payload — literal text interleaved with Keys.* Private-Use-Area
    // sentinels — into the winapp send-keys token grammar. Literal runs become a single `text=<escaped>`
    // token (whitespace/backslash escaped so the tokenizer keeps the run intact); Tab/Enter/Space/Esc/
    // Backspace/Delete sentinels become the matching named key; a Shift/Ctrl sentinel binds to the next
    // single key as a modifier chord, mirroring the old per-keystroke InputInjector.TypeKeys (which held
    // a modifier for exactly the following character). Internal for direct unit testing.
    internal static string ToSendKeysTokens(string keys)
    {
        var tokens = new List<string>();
        var literal = new System.Text.StringBuilder();
        var mods = new List<string>();

        void FlushLiteral()
        {
            if (literal.Length == 0) return;
            tokens.Add("text=" + EscapeTextToken(literal.ToString()));
            literal.Clear();
        }

        foreach (var ch in keys)
        {
            if (TryModifierName(ch, out var mod))
            {
                FlushLiteral();
                mods.Add(mod);
                continue;
            }

            if (TryNamedKey(ch, out var named))
            {
                FlushLiteral();
                tokens.Add(Chord(mods, named));
                mods.Clear();
                continue;
            }

            // Literal character: a pending modifier binds to just this one char (Ctrl+A), then releases.
            if (mods.Count > 0)
            {
                FlushLiteral();
                tokens.Add(Chord(mods, ch.ToString()));
                mods.Clear();
            }
            else
            {
                literal.Append(ch);
            }
        }

        FlushLiteral();

        // A modifier sentinel with no following key (dangling at the end of the payload) can't form a
        // chord. Emit it as a bare virtual-key tap (vk=) — matching the old InputInjector, whose
        // finally-block pressed and released a held modifier that never bound to a character — rather
        // than silently dropping it. mods is only non-empty here when the literal buffer is already
        // empty, because adding a modifier always flushes the pending literal first.
        foreach (var mod in mods)
            tokens.Add(ModifierVkToken(mod));

        return string.Join(' ', tokens);
    }

    private static string Chord(List<string> mods, string key) =>
        mods.Count == 0 ? key : string.Join('+', mods) + "+" + key;

    // Bare virtual-key token for a dangling modifier (VK_SHIFT 0x10 / VK_CONTROL 0x11), used to
    // press+release a modifier sentinel that had no following key to chord with.
    private static string ModifierVkToken(string modifier) => modifier switch
    {
        "shift" => "vk=0x10",
        "ctrl" => "vk=0x11",
        _ => throw new global::System.ArgumentOutOfRangeException(nameof(modifier), modifier, "Unknown modifier."),
    };

    private static bool TryModifierName(char ch, out string name)
    {
        name = ch switch
        {
            '\ue008' => "shift",   // Keys.Shift
            '\ue009' => "ctrl",    // Keys.Control
            _ => "",
        };
        return name.Length != 0;
    }

    private static bool TryNamedKey(char ch, out string name)
    {
        name = ch switch
        {
            '\ue004' => "tab",             // Keys.Tab
            '\ue006' or '\ue007' => "enter", // Keys.Return / Keys.Enter
            '\ue00d' => "space",           // Keys.Space
            '\ue00c' => "esc",             // Keys.Escape
            '\ue003' => "backspace",       // Keys.Backspace
            '\ue017' => "delete",          // Keys.Delete
            _ => "",
        };
        return name.Length != 0;
    }

    // Escape a literal run for a `text=` token. The send-keys tokenizer splits on whitespace, so spaces,
    // tabs and newlines inside the run must be backslash-escaped (\s \t \n \r) to survive tokenizing, and
    // a literal backslash doubled so it isn't read as an escape.
    private static string EscapeTextToken(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case ' ': sb.Append("\\s"); break;
                case '\t': sb.Append("\\t"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }
}
