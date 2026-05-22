using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace Microsoft.UI.Reactor.Docking.Native;

// ════════════════════════════════════════════════════════════════════════
//  Spec 045 §2.10 — Ctrl+Tab pane navigator overlay.
//
//  VS-style pane picker: a single Popup anchored over the host Border,
//  containing a vertical list of pane titles. Activated by the
//  Ctrl+Tab / Ctrl+Shift+Tab chord; each subsequent chord press cycles
//  the selection by ±1. The overlay commits its selection when Ctrl is
//  released — the chord modifier — and cancels on Esc.
//
//  The overlay is not driven through the Reactor reconciler so that
//  opening it doesn't perturb the host's render tree (M19 / M20 control-
//  identity contract). Instead, a single instance per host Border is
//  attached lazily and reused across chord presses; closing the popup
//  detaches the global Ctrl-release listener but keeps the instance for
//  the next chord.
//
//  The instance lifecycle is keyed on the host `Border` ref so app code
//  that rebuilds `new DockManager` each render rotates element refs
//  without leaking popups (one Border per host mount).
// ════════════════════════════════════════════════════════════════════════

internal sealed class DockNavigatorPopup
{
    /// <summary>One entry in the navigator list — pane key + display title.</summary>
    public readonly record struct Entry(object? Key, string Title);

    private static readonly ConditionalWeakTable<FrameworkElement, DockNavigatorPopup> _table = new();

    private readonly FrameworkElement _host;
    private readonly Popup _popup;
    private readonly StackPanel _list;
    private Entry[] _entries = Array.Empty<Entry>();
    private int _selected = -1;
    private Action<object?>? _onCommit;
    private KeyEventHandler? _globalKeyUpHandler;
    private UIElement? _globalKeyUpTarget;
    private KeyEventHandler? _globalKeyDownHandler;
    private UIElement? _globalKeyDownTarget;
    private bool _isOpen;

    /// <summary>True when the navigator popup is currently visible.</summary>
    public bool IsOpen => _isOpen;

    /// <summary>Resolve (or lazily create) the navigator for a given host element.</summary>
    public static DockNavigatorPopup For(FrameworkElement host)
    {
        if (_table.TryGetValue(host, out var existing)) return existing;
        var created = new DockNavigatorPopup(host);
        _table.Add(host, created);
        return created;
    }

    /// <summary>
    /// Close any in-flight navigator popup attached to <paramref name="host"/>
    /// and drop the CWT entry. Called by <c>DockingNativeInterop</c>'s unmount
    /// path so global Ctrl/Esc handlers attached to <c>XamlRoot.Content</c>
    /// release their strong reference to this popup (and transitively to
    /// the host Border) when the host unmounts mid-chord.
    /// </summary>
    public static void CleanupFor(FrameworkElement host)
    {
        if (!_table.TryGetValue(host, out var existing)) return;
        existing.Close(commit: false);
        _table.Remove(host);
    }

    private DockNavigatorPopup(FrameworkElement host)
    {
        _host = host;
        _list = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Padding = new Thickness(8),
            Spacing = 2,
        };
        var border = new Border
        {
            Child = _list,
            Background = new SolidColorBrush(Color.FromArgb(0xF5, 0x24, 0x24, 0x24)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x99, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4),
            MinWidth = 240,
            MinHeight = 80,
        };
        // §2.22 — a11y surface so screen readers announce the picker.
        AutomationProperties.SetName(border,
            DockingStrings.Get(DockingStringKeys.NavigatorHeadingActive));
        AutomationProperties.SetLandmarkType(border, AutomationLandmarkType.Custom);
        _popup = new Popup
        {
            Child = border,
            IsLightDismissEnabled = false,
            // Anchor at the host's top-left; HorizontalOffset/VerticalOffset
            // are recomputed each Open() to center over the host.
        };
    }

    /// <summary>
    /// Open or update the navigator. When already open, cycles the
    /// selection by <paramref name="delta"/>; when closed, builds the
    /// list from <paramref name="entries"/>, seeds selection at
    /// (<paramref name="currentIndex"/> + <paramref name="delta"/>) wrapped,
    /// and shows the popup. <paramref name="onCommit"/> is invoked with the
    /// selected key on Ctrl release; with null when the user cancels (Esc).
    /// </summary>
    public void OpenOrAdvance(
        IReadOnlyList<Entry> entries,
        int currentIndex,
        int delta,
        Action<object?> onCommit)
    {
        if (entries is null || entries.Count == 0) return;

        if (_isOpen)
        {
            // Already showing — just advance the selection.
            AdvanceSelection(delta);
            return;
        }

        _entries = entries.ToArray();
        _onCommit = onCommit;
        _selected = Wrap(currentIndex + delta, _entries.Length);

        // Rebuild list contents.
        _list.Children.Clear();
        for (int i = 0; i < _entries.Length; i++)
        {
            var row = BuildRow(_entries[i], i);
            _list.Children.Add(row);
        }
        UpdateHighlight();

        // Anchor: place under the host top-center, offset 12 DIP down.
        if (_host.XamlRoot is { } root)
        {
            _popup.XamlRoot = root;
            _popup.HorizontalOffset = Math.Max(0, (_host.ActualWidth - 260) / 2);
            _popup.VerticalOffset = 32;
        }

        // Hook global Ctrl-release + Esc listeners BEFORE opening so the
        // first keystroke after open lands on us.
        HookGlobalListeners();
        _popup.IsOpen = true;
        _isOpen = true;
    }

    /// <summary>Force-close the navigator without committing. Test hook.</summary>
    public void CancelForTest() => Close(commit: false);

    /// <summary>Commit the current selection now (test hook).</summary>
    public void CommitForTest() => Close(commit: true);

    /// <summary>Read the currently-selected entry (test hook). Empty when closed.</summary>
    public Entry? SelectedEntry =>
        _isOpen && _selected >= 0 && _selected < _entries.Length ? _entries[_selected] : null;

    private FrameworkElement BuildRow(Entry e, int index)
    {
        var text = new TextBlock
        {
            Text = string.IsNullOrEmpty(e.Title) ? "(unnamed)" : e.Title,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xEE, 0xEE, 0xEE)),
            Padding = new Thickness(6, 4, 6, 4),
        };
        var row = new Border
        {
            Child = text,
            Background = new SolidColorBrush(Colors.Transparent),
            CornerRadius = new CornerRadius(2),
            // Test hook — selftests find the row by AutomationId.
        };
        AutomationProperties.SetAutomationId(row, $"docknav:{index}");
        AutomationProperties.SetName(row, e.Title ?? string.Empty);
        return row;
    }

    private void UpdateHighlight()
    {
        for (int i = 0; i < _list.Children.Count; i++)
        {
            if (_list.Children[i] is not Border row) continue;
            if (row.Background is not SolidColorBrush brush) continue;
            brush.Color = i == _selected
                ? Color.FromArgb(0xFF, 0x33, 0x66, 0xAA)
                : Colors.Transparent;
        }
    }

    private void AdvanceSelection(int delta)
    {
        if (_entries.Length == 0) return;
        _selected = Wrap(_selected + delta, _entries.Length);
        UpdateHighlight();
    }

    private static int Wrap(int value, int count)
    {
        if (count <= 0) return 0;
        var m = value % count;
        return m < 0 ? m + count : m;
    }

    private void HookGlobalListeners()
    {
        if (_host.XamlRoot?.Content is not UIElement root) return;
        UnhookGlobalListeners();
        _globalKeyUpTarget = root;
        _globalKeyUpHandler = OnGlobalKeyUp;
        root.AddHandler(UIElement.KeyUpEvent, _globalKeyUpHandler, handledEventsToo: true);
        _globalKeyDownTarget = root;
        _globalKeyDownHandler = OnGlobalKeyDown;
        root.AddHandler(UIElement.KeyDownEvent, _globalKeyDownHandler, handledEventsToo: true);
    }

    private void UnhookGlobalListeners()
    {
        if (_globalKeyUpHandler is not null && _globalKeyUpTarget is not null)
        {
            try { _globalKeyUpTarget.RemoveHandler(UIElement.KeyUpEvent, _globalKeyUpHandler); }
            catch { /* best-effort */ }
        }
        if (_globalKeyDownHandler is not null && _globalKeyDownTarget is not null)
        {
            try { _globalKeyDownTarget.RemoveHandler(UIElement.KeyDownEvent, _globalKeyDownHandler); }
            catch { /* best-effort */ }
        }
        _globalKeyUpHandler = null;
        _globalKeyUpTarget = null;
        _globalKeyDownHandler = null;
        _globalKeyDownTarget = null;
    }

    private void OnGlobalKeyUp(object sender, KeyRoutedEventArgs e)
    {
        // Commit on Control release — the chord modifier. Both left and
        // right Ctrl variants map to VirtualKey.Control in WinUI key-up
        // events (the modifier is reported, not the physical key code).
        if (e.Key != VirtualKey.Control) return;
        Close(commit: true);
    }

    private void OnGlobalKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Esc cancels the navigator without committing.
        if (e.Key == VirtualKey.Escape)
        {
            Close(commit: false);
            e.Handled = true;
        }
    }

    private void Close(bool commit)
    {
        if (!_isOpen) return;
        _isOpen = false;
        UnhookGlobalListeners();
        _popup.IsOpen = false;
        if (commit && _selected >= 0 && _selected < _entries.Length)
        {
            try { _onCommit?.Invoke(_entries[_selected].Key); }
            catch (Exception ex)
            {
                // Commit callback is app code; surface the failure for
                // debugging rather than swallowing it silently.
                global::System.Diagnostics.Debug.WriteLine(
                    $"[Docking] DockNavigatorPopup commit callback threw: {ex.GetType().Name}: {ex.Message}");
            }
        }
        _onCommit = null;
        _entries = Array.Empty<Entry>();
        _selected = -1;
        // Drop the transient row visuals so closed-but-cached popup
        // instances don't retain row TextBlocks / Border refs and the
        // pane keys they captured. The next OpenOrAdvance rebuilds the
        // list from fresh entries.
        _list.Children.Clear();
    }

    /// <summary>
    /// Test hook — directly seed the navigator with entries and selection,
    /// bypassing the Open dance. Used by unit tests for keyboard-event
    /// dispatch shape without needing a real XamlRoot.
    /// </summary>
    internal void SeedForTest(IReadOnlyList<Entry> entries, int selectedIndex, Action<object?> onCommit)
    {
        _entries = entries.ToArray();
        _selected = Wrap(selectedIndex, _entries.Length);
        _onCommit = onCommit;
        _isOpen = true;
    }
}
