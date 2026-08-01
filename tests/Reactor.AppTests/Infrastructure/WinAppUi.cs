using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

/// <summary>
/// Bounds of a UIA element in physical screen pixels (winapp's BoundingRectangle).
/// </summary>
public readonly record struct UiRect(int X, int Y, int Width, int Height)
{
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
}

/// <summary>One element returned by <c>winapp ui search</c>.</summary>
public sealed record UiMatch(
    string? Type,
    string? Name,
    string? AutomationId,
    string? ClassName,
    bool IsEnabled,
    bool IsOffscreen,
    int X, int Y, int Width, int Height,
    string Selector,
    bool IsInvokable,
    string? InvokableAncestorType = null,
    string? InvokableAncestorSelector = null);

/// <summary>A visible top-level window returned by <c>winapp ui list-windows</c>.</summary>
public sealed record UiWindow(long Hwnd, int ProcessId, string? Title, string? ClassName, bool IsForeground);

/// <summary>
/// Thin wrapper over the <c>winapp ui</c> CLI (UI Automation). Each method spawns a
/// short-lived <c>winapp.exe</c> process targeting the Host app's window (by HWND for
/// stability — survives the multi-window state docking tear-off creates) and parses the
/// <c>--json</c> envelope with System.Text.Json.
///
/// This replaces the persistent Appium <c>WindowsDriver</c> session. winapp has no
/// persistent session (process-per-call), so polling helpers map onto winapp's own
/// internal <c>wait-for</c> (single process, 100ms internal poll) to avoid spawning a
/// process per poll tick.
/// </summary>
public sealed class WinAppUi
{
    private static readonly string WinAppExe = ResolveWinAppExe();

    private readonly int _pid;

    /// <summary>HWND of the primary Host window, captured at session start.</summary>
    public long HostHwnd { get; }

    public WinAppUi(int pid, long hostHwnd)
    {
        _pid = pid;
        HostHwnd = hostHwnd;
    }

    private static string ResolveWinAppExe()
    {
        // Explicit override — an absolute path to a winapp.exe, honored first.
        // Needed when the machine's `winapp.exe` app-execution-alias resolves to an
        // older build that lacks the `ui` UIA verb (alias drift from a sideloaded
        // winapp-dev package), while a newer ui-capable winapp is installed elsewhere
        // (e.g. %LOCALAPPDATA%\Microsoft\WindowsApps\winapp_8wekyb3d8bbwe\winapp.exe).
        // Also lets CI pin an exact winapp build. No effect unless the var is set.
        var overridePath = Environment.GetEnvironmentVariable("REACTOR_WINAPP_EXE");
        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
            return Path.GetFullPath(overridePath);

        var local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrEmpty(local))
        {
            var candidate = Path.Combine(local, "Microsoft", "WindowsApps", "winapp.exe");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(path))
        {
            foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                         .Where(Path.IsPathFullyQualified))
            {
                var candidate = Path.Combine(entry, "winapp.exe");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
        }

        throw new WinAppException(
            "Could not find winapp.exe. Install the winapp CLI (winget install Microsoft.WinAppCli) " +
            "or ensure an absolute PATH entry contains winapp.exe, or set REACTOR_WINAPP_EXE to an " +
            "absolute path to a ui-capable winapp.exe.");
    }

    /// <summary>
    /// Total number of <c>winapp.exe</c> processes spawned since process start. winapp has no
    /// persistent session — every verb is a fresh process — so this is the headline
    /// process-spawn-overhead metric vs the old single long-lived WinAppDriver session.
    /// <see cref="AppTestBase"/> snapshots this around each test to report a per-test count.
    /// </summary>
    public static long InvocationCount;

    // ─── Process plumbing ────────────────────────────────────────────────────

    private readonly record struct RunResult(int ExitCode, string StdOut, string StdErr);

    // Bumps the process-global spawn counter from a static scope, keeping the mutation off the
    // instance Run path while still aggregating across the many short-lived WinAppUi instances.
    private static void RecordInvocation() => Interlocked.Increment(ref InvocationCount);

    private RunResult Run(int processTimeoutMs, params string[] args)
    {
        RecordInvocation();

        var psi = new ProcessStartInfo(WinAppExe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("ui");
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        var sbOut = new StringBuilder();
        var sbErr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            throw new WinAppException(
                $"Failed to launch winapp ({WinAppExe}). Ensure winapp CLI is installed and on PATH.", ex);
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (!proc.WaitForExit(processTimeoutMs))
        {
            try { proc.Kill(true); } catch { }
            throw new WinAppTimeoutException(
                $"winapp ui {string.Join(' ', args)} did not exit within {processTimeoutMs}ms.");
        }
        // Ensure async buffers are flushed.
        proc.WaitForExit();

        return new RunResult(proc.ExitCode, sbOut.ToString(), sbErr.ToString());
    }

    /// <summary>Append the window target + --json to a verb's args.</summary>
    private string[] Args(string verb, long hwnd, params string[] rest)
        => BuildArgs(verb, hwnd, rest);

    internal static string[] BuildArgs(string verb, long hwnd, params string[] rest)
    {
        var list = new List<string>(rest.Length + 5) { verb };
        list.AddRange(rest);
        list.Add("-w");
        list.Add(hwnd.ToString(CultureInfo.InvariantCulture));
        list.Add("--json");
        return list.ToArray();
    }

    private static JsonDocument Parse(RunResult r)
        => ParseJson(r.ExitCode, r.StdOut, r.StdErr);

    internal static JsonDocument ParseJson(int exitCode, string stdOut, string stdErr)
    {
        var text = stdOut.Trim();
        if (text.Length == 0)
            throw new WinAppException($"winapp returned empty output (exit {exitCode}). stderr: {stdErr.Trim()}");
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new WinAppException($"Could not parse winapp JSON (exit {exitCode}): {text}", ex);
        }
    }

    // ─── Connection ──────────────────────────────────────────────────────────

    /// <summary>Resolve the primary Host window HWND for a process by title.</summary>
    public static long FindWindowHwnd(int pid, string title, int timeoutMs = 10000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var ui = new WinAppUi(pid, 0);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                foreach (var w in ui.ListWindowsForPid()
                             .Where(w => w.ProcessId == pid &&
                                         (w.Title?.Contains(title, StringComparison.OrdinalIgnoreCase) ?? false)))
                    return w.Hwnd;
            }
            catch (Exception ex) { last = ex; }
            Thread.Sleep(200);
        }
        throw new WinAppTimeoutException(
            $"Host window '{title}' (pid {pid}) did not appear within {timeoutMs}ms." +
            (last is null ? "" : $" Last error: {last.Message}"));
    }

    private IEnumerable<UiWindow> ListWindowsForPid()
    {
        var r = Run(15000, "list-windows", "-a", _pid.ToString(CultureInfo.InvariantCulture), "--json");
        using var doc = Parse(r);
        foreach (var w in EnumerateWindows(doc.RootElement)) yield return w;
    }

    /// <summary>All visible windows belonging to the Host process (host + floating tear-off windows).</summary>
    public IReadOnlyList<UiWindow> ListWindows()
    {
        var result = new List<UiWindow>();
        var r = Run(15000, "list-windows", "-a", _pid.ToString(CultureInfo.InvariantCulture), "--json");
        using var doc = Parse(r);
        result.AddRange(EnumerateWindows(doc.RootElement));
        return result;
    }

    private static IEnumerable<UiWindow> EnumerateWindows(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array) yield break;
        foreach (var w in root.EnumerateArray())
        {
            yield return new UiWindow(
                GetLong(w, "hwnd"),
                (int)GetLong(w, "processId"),
                GetString(w, "title"),
                GetString(w, "className"),
                GetBool(w, "isForeground"));
        }
    }

    // ─── Search / existence ──────────────────────────────────────────────────

    /// <summary>Run <c>winapp ui search</c> against the given window. Empty list on miss.</summary>
    public IReadOnlyList<UiMatch> Search(string selector, long? hwnd = null)
    {
        var r = Run(15000, Args("search", hwnd ?? HostHwnd, selector));
        var matches = new List<UiMatch>();
        if (r.StdOut.Trim().Length == 0) return matches;
        using var doc = Parse(r);
        if (!doc.RootElement.TryGetProperty("matches", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return matches;
        foreach (var m in arr.EnumerateArray())
        {
            // winapp reports, in the SAME search call, the nearest invokable ancestor of a
            // non-invokable match (e.g. a tab caption TextBlock → its owning TabItem). Capturing
            // it here lets callers resolve the tab without a second `inspect` call — which would
            // open a re-render race window where the caption's hash slug can go stale (exactly
            // what broke SelectTab on an inactive pinnable docking tab).
            string? ancType = null, ancSel = null;
            if (m.TryGetProperty("invokableAncestor", out var anc) && anc.ValueKind == JsonValueKind.Object)
            {
                ancType = GetString(anc, "type");
                ancSel = GetString(anc, "selector");
            }
            matches.Add(new UiMatch(
                GetString(m, "type"), GetString(m, "name"), GetString(m, "automationId"),
                GetString(m, "className"), GetBool(m, "isEnabled"), GetBool(m, "isOffscreen"),
                (int)GetLong(m, "x"), (int)GetLong(m, "y"), (int)GetLong(m, "width"), (int)GetLong(m, "height"),
                GetString(m, "selector") ?? selector, GetBool(m, "isInvokable"),
                ancType, ancSel));
        }
        return matches;
    }

    /// <summary>True if any element matches the selector in the target window.</summary>
    public bool Exists(string selector, long? hwnd = null) => Search(selector, hwnd).Count > 0;

    // ─── Reads ───────────────────────────────────────────────────────────────

    /// <summary>Smart value read (TextPattern → ValuePattern → Name). Null if absent.</summary>
    public string? GetValue(string selector, long? hwnd = null)
    {
        var r = Run(15000, Args("get-value", hwnd ?? HostHwnd, selector));
        if (r.ExitCode != 0 || r.StdOut.Trim().Length == 0) return null;
        using var doc = Parse(r);
        return GetString(doc.RootElement, "text");
    }

    /// <summary>Read a single UIA property. Null if winapp can't surface it (caller may fall back to UIA).</summary>
    public string? GetProperty(string selector, string property, long? hwnd = null)
    {
        var r = Run(15000, Args("get-property", hwnd ?? HostHwnd, selector, "-p", property));
        if (r.ExitCode != 0 || r.StdOut.Trim().Length == 0) return null;
        using var doc = Parse(r);
        if (!doc.RootElement.TryGetProperty("properties", out var props)) return null;
        if (!props.TryGetProperty(property, out var val)) return null;
        return val.ValueKind == JsonValueKind.Null ? null : val.GetString();
    }

    /// <summary>Bounds of the first element matching the selector. Null if absent.</summary>
    public UiRect? GetBounds(string selector, long? hwnd = null)
    {
        var matches = Search(selector, hwnd);
        if (matches.Count == 0) return null;
        var m = matches[0];
        return new UiRect(m.X, m.Y, m.Width, m.Height);
    }

    /// <summary>
    /// Walks up the UIA tree from <paramref name="childSelector"/> and returns the slug of the
    /// nearest ancestor whose control type is <c>TabItem</c> (a WinUI <c>TabViewItem</c>), or
    /// null if none is found. Used to resolve a tab from its caption: a docking tab with a pin
    /// affordance renders a composite (StackPanel) header, so the <c>TabViewItem</c> itself has
    /// no Name and is not returned by a text search for the caption — but its caption TextBlock
    /// child is, and the TabItem is its ancestor. The returned slug invokes the tab's
    /// SelectionItemPattern (selects the tab) without colliding with the pin button that a plain
    /// caption-text Invoke would hit.
    /// </summary>
    public string? ResolveAncestorTab(string childSelector, long? hwnd = null)
    {
        var r = Run(15000, Args("inspect", hwnd ?? HostHwnd, childSelector, "--ancestors"));
        if (r.ExitCode != 0 || r.StdOut.Trim().Length == 0) return null;
        using var doc = Parse(r);
        if (!doc.RootElement.TryGetProperty("windows", out var windows) ||
            windows.ValueKind != JsonValueKind.Array)
            return null;
        // --ancestors emits the root→target lineage as a nested children chain; the deepest
        // TabItem on the path is the tab that owns this caption.
        string? found = null;
        foreach (var win in windows.EnumerateArray())
            if (win.TryGetProperty("elements", out var els))
                WalkForTab(els, ref found);
        return found;
    }

    private static void WalkForTab(JsonElement nodes, ref string? found)
    {
        if (nodes.ValueKind != JsonValueKind.Array) return;
        foreach (var n in nodes.EnumerateArray())
        {
            if (string.Equals(GetString(n, "type"), "TabItem", StringComparison.OrdinalIgnoreCase) &&
                GetString(n, "selector") is { } sel)
                found = sel; // keep the deepest TabItem on the lineage
            if (n.TryGetProperty("children", out var kids))
                WalkForTab(kids, ref found);
        }
    }

    // ─── Actions ─────────────────────────────────────────────────────────────

    /// <summary>Activate via UIA patterns (Invoke → Toggle → SelectionItem → ExpandCollapse).</summary>
    public void Invoke(string selector, long? hwnd = null)
    {
        var r = Run(15000, Args("invoke", hwnd ?? HostHwnd, selector));
        if (r.ExitCode == 0)
            return;

        if (CanFallbackToClick(r))
            Click(selector, hwnd: hwnd);
        else
            throw new WinAppException($"winapp ui invoke '{selector}' failed: {r.StdErr.Trim()} {r.StdOut.Trim()}");
    }

    private static bool CanFallbackToClick(RunResult r)
    {
        var text = $"{r.StdErr}\n{r.StdOut}";
        return text.Contains("invoke pattern", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("invokable pattern", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("not invokable", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("not invokeable", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("does not support invoke", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("no supported pattern", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("no supported action", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Mouse-simulation click (for elements without InvokePattern).</summary>
    public void Click(string selector, bool doubleClick = false, bool rightClick = false, long? hwnd = null)
    {
        // winapp's click verb is real SendInput under the hood — it fails with
        // ACCESS_DENIED off the interactive input desktop (non-uiAccess / disconnected
        // session). Surface that as Inconclusive (not Failed) via the interactivity guard.
        SessionInteractivityGuard.EnsureInputInjectable($"click '{selector}'");

        var extra = new List<string> { selector };
        if (doubleClick) extra.Add("--double");
        if (rightClick) extra.Add("--right");
        var r = Run(15000, Args("click", hwnd ?? HostHwnd, extra.ToArray()));
        if (r.ExitCode != 0)
            throw new WinAppException($"winapp ui click '{selector}' failed: {r.StdErr.Trim()} {r.StdOut.Trim()}");
    }

    /// <summary>
    /// Synthesize keyboard input through the native <c>winapp ui send-keys</c> verb. <paramref name="keys"/>
    /// uses winapp's token grammar: named keys (<c>tab</c>, <c>enter</c>, <c>home</c>, <c>f1</c>), modifier
    /// combos (<c>ctrl+a</c>, <c>shift+tab</c>), raw virtual keys (<c>vk=0x6B</c>), and <c>text=</c> literals.
    /// With <paramref name="viaSendInput"/> the OS-wide send-input transport raises a real per-character
    /// KeyDown (required by keystroke-observing handlers); the default post-message transport only raises
    /// TextChanged. <paramref name="target"/>, when set, is focused (UIA SetFocus) before the keys are sent.
    /// </summary>
    public void SendKeys(string keys, bool viaSendInput = false, string? target = null, long? hwnd = null)
    {
        // This verb takes the winapp TOKEN grammar ("tab", "enter", "ctrl+a delete", "text=..."),
        // not the private-use key constants in Keys. Those are a UiElement.SendKeys convenience —
        // UiElement.ToSendKeysTokens translates them; this path does not. Passing one here used to
        // forward the raw PUA character as literal text: the CLI typed an unmapped glyph, exited 0,
        // and nothing moved. A no-op that reports success is the worst possible failure mode for an
        // input primitive — an E2E built on it fails much later, somewhere else, claiming the
        // product did not respond. Reject it loudly at the call instead, and name the fix.
        foreach (var ch in keys.Where(c => c is >= '\ue000' and <= '\uf8ff'))
        {
            throw new global::System.ArgumentException(
                $"SendKeys received the private-use key constant U+{(int)ch:X4}. WinAppUi.SendKeys takes " +
                "winapp token syntax — pass \"tab\"/\"enter\"/\"esc\" directly, or run the string through " +
                "UiElement.ToSendKeysTokens first. Keys.* constants only work with UiElement.SendKeys.",
                nameof(keys));
        }

        // send-input routes OS-wide and fails with ACCESS_DENIED off the interactive input desktop, just
        // like the click verb — surface that as Inconclusive (not Failed). post-message posts
        // straight to the window's message queue and needs no interactive desktop, so only guard send-input.
        if (viaSendInput)
            SessionInteractivityGuard.EnsureInputInjectable($"send-keys '{keys}'");

        var extra = new List<string> { keys };
        if (viaSendInput) { extra.Add("--via"); extra.Add("send-input"); }
        if (!string.IsNullOrEmpty(target)) { extra.Add("--target"); extra.Add(target); }
        var r = Run(15000, Args("send-keys", hwnd ?? HostHwnd, extra.ToArray()));
        if (r.ExitCode != 0)
            throw new WinAppException($"winapp ui send-keys '{keys}' failed: {r.StdErr.Trim()} {r.StdOut.Trim()}");
    }

    /// <summary>
    /// Drag from one point to another through the native <c>winapp ui drag</c> verb. <paramref name="from"/>
    /// and <paramref name="to"/> are each an element selector (drags from/to the element's center) or
    /// <c>"x,y"</c> screen coordinates in the same space <see cref="GetBounds"/> / <c>UiElement.Rect</c>
    /// report. The CLI interpolates the motion internally (crossing WinUI's 4-DIP drag threshold) and
    /// re-resolves element endpoints after foregrounding. <paramref name="holdMs"/> presses and holds the
    /// button before moving — with <c>from == to</c> (no movement) this is a press-and-hold / long-press;
    /// <paramref name="dwellMs"/> settles on the destination before releasing so hover-armed drop targets /
    /// merge overlays can latch.
    /// </summary>
    public void Drag(string from, string to, int holdMs = 0, int dwellMs = 0, bool rightButton = false,
        long? hwnd = null)
    {
        // Native drag is real SendInput mouse input — ACCESS_DENIED off the interactive desktop, as click.
        SessionInteractivityGuard.EnsureInputInjectable($"drag '{from}' -> '{to}'");

        var extra = new List<string> { from, to };
        if (rightButton) extra.Add("--right");
        if (holdMs > 0) { extra.Add("--hold-ms"); extra.Add(holdMs.ToString(CultureInfo.InvariantCulture)); }
        if (dwellMs > 0) { extra.Add("--dwell-ms"); extra.Add(dwellMs.ToString(CultureInfo.InvariantCulture)); }
        // Give the process budget for the hold + dwell it will spend inside the drag on top of the base
        // stabilize/interpolate work, so a long-press/merge dwell can't trip the process timeout.
        var r = Run(15000 + holdMs + dwellMs, Args("drag", hwnd ?? HostHwnd, extra.ToArray()));
        if (r.ExitCode != 0)
            throw new WinAppException($"winapp ui drag '{from}' -> '{to}' failed: {r.StdErr.Trim()} {r.StdOut.Trim()}");
    }

    /// <summary>Set a value via UIA ValuePattern (TextBox/ComboBox/Slider).</summary>
    public void SetValue(string selector, string value, long? hwnd = null)
    {
        var r = Run(15000, Args("set-value", hwnd ?? HostHwnd, selector, value));
        if (r.ExitCode != 0)
            throw new WinAppException($"winapp ui set-value '{selector}' failed: {r.StdErr.Trim()} {r.StdOut.Trim()}");
    }

    /// <summary>Move keyboard focus to the element via UIA SetFocus.</summary>
    public void Focus(string selector, long? hwnd = null)
    {
        var r = Run(15000, Args("focus", hwnd ?? HostHwnd, selector));
        if (r.ExitCode != 0)
            throw new WinAppException($"winapp ui focus '{selector}' failed: {r.StdErr.Trim()} {r.StdOut.Trim()}");
    }

    /// <summary>
    /// Selector (volatile slug) of the first editable text control — UIA type <c>Edit</c> — in the
    /// window. Used to locate the DataGrid inline editor, which renders without an AutomationId and so
    /// can only be addressed by winapp's semantic slug. The slug must be consumed immediately (focus /
    /// type) before the next re-render, since it is a display hint, not a stable handle.
    /// </summary>
    public string? FindFirstEditableSelector(long? hwnd = null)
    {
        var r = Run(15000, Args("inspect", hwnd ?? HostHwnd, "-i"));
        if (r.ExitCode != 0 || r.StdOut.Trim().Length == 0) return null;
        using var doc = Parse(r);
        if (!doc.RootElement.TryGetProperty("windows", out var wins) || wins.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var w in wins.EnumerateArray())
        {
            if (!w.TryGetProperty("elements", out var els) || els.ValueKind != JsonValueKind.Array)
                continue;
            if (FindFirstEditableSelector(els) is { } selector)
                return selector;
        }
        return null;
    }

    internal static string? FindFirstEditableSelector(JsonElement nodes)
    {
        if (nodes.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var node in nodes.EnumerateArray())
        {
            if (string.Equals(GetString(node, "type"), "Edit", StringComparison.OrdinalIgnoreCase) &&
                GetString(node, "selector") is { } selector)
                return selector;

            if (node.TryGetProperty("children", out var children) &&
                FindFirstEditableSelector(children) is { } childSelector)
                return childSelector;
        }

        return null;
    }

    // ─── Waits (winapp-internal polling) ─────────────────────────────────────

    /// <summary>Wait for the element to exist. Returns false on timeout.</summary>
    public bool WaitForExists(string selector, int timeoutMs = 5000, long? hwnd = null)
    {
        var r = Run(timeoutMs + 30000,
            Args("wait-for", hwnd ?? HostHwnd, selector, "--timeout", timeoutMs.ToString(CultureInfo.InvariantCulture)));
        return r.ExitCode == 0;
    }

    /// <summary>Wait for the element to disappear. Returns false on timeout.</summary>
    public bool WaitForGone(string selector, int timeoutMs = 5000, long? hwnd = null)
    {
        var r = Run(timeoutMs + 30000,
            Args("wait-for", hwnd ?? HostHwnd, selector, "--gone",
                "--timeout", timeoutMs.ToString(CultureInfo.InvariantCulture)));
        return r.ExitCode == 0;
    }

    /// <summary>Wait until the element's value equals (or contains) the target. False on timeout.</summary>
    public bool WaitForValue(string selector, string value, bool contains = false,
        int timeoutMs = 5000, long? hwnd = null)
    {
        var args = new List<string> { selector, "--value", value, "--timeout",
            timeoutMs.ToString(CultureInfo.InvariantCulture) };
        if (contains) args.Add("--contains");
        var r = Run(timeoutMs + 30000, Args("wait-for", hwnd ?? HostHwnd, args.ToArray()));
        return r.ExitCode == 0;
    }

    // ─── JSON helpers ────────────────────────────────────────────────────────

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static long GetLong(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetInt64(out var l) ? l : 0,
            JsonValueKind.String => long.TryParse(v.GetString(), out var l) ? l : 0,
            _ => 0,
        };
    }

    private static bool GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False && v.GetBoolean();
}
