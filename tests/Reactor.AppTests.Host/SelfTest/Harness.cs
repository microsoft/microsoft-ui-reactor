using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest;

/// <summary>
/// Test harness that runs assertions against a WinUI window and outputs TAP results.
/// Each fixture receives a Harness instance and calls Check/CheckAsync to report results.
/// </summary>
internal sealed class Harness
{
    private readonly Window _window;
    private int _failures;

    // Persistent title bar with visual test-result segments
    private TextBlock? _subtitleText;
    private readonly List<Border> _testSegments = new();
    private Border? _contentArea;

    // Windows taskbar progress via ITaskbarList3 COM interface
    private IntPtr _hwnd;
    private ITaskbarList3? _taskbar;
    private int _totalTests;

    public Harness(Window window) { _window = window; _currentWindow = window; }
    public Window Window => _window;
    public int Failures => _failures;
    public void RecordFailure() => _failures++;

    // -- TitleBar setup ---------------------------------------------------

    public void SetupTitleBar(int totalTests)
    {
        _testSegments.Clear();

        // Grid of equal-width columns — one per test, colored on completion
        var segmentBar = new Grid { IsHitTestVisible = false };
        for (int i = 0; i < totalTests; i++)
        {
            segmentBar.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var seg = new Border
            {
                Background = new SolidColorBrush(
                    global::Windows.UI.Color.FromArgb(30, 200, 200, 200)),
            };
            Grid.SetColumn(seg, i);
            segmentBar.Children.Add(seg);
            _testSegments.Add(seg);
        }

        // Subtitle label in a semi-transparent pill for readability over the bar
        _subtitleText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        var textPill = new Border
        {
            Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(180, 0, 0, 0)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            IsHitTestVisible = false,
            Child = _subtitleText,
        };

        var titleBarArea = new Grid { Height = 48 };
        titleBarArea.Children.Add(segmentBar);
        titleBarArea.Children.Add(textPill);

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Grid.SetRow(titleBarArea, 0);
        rootGrid.Children.Add(titleBarArea);

        _contentArea = new Border();
        Grid.SetRow(_contentArea, 1);
        rootGrid.Children.Add(_contentArea);

        _window.Content = rootGrid;
        _window.ExtendsContentIntoTitleBar = true;
        _window.SetTitleBar(titleBarArea);

        // Initialize Windows taskbar progress overlay
        _totalTests = totalTests;
        try
        {
            _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            _taskbar = (ITaskbarList3)new TaskbarList();
            _taskbar.HrInit();
            _taskbar.SetProgressState(_hwnd, TaskbarProgressFlags.Normal);
            _taskbar.SetProgressValue(_hwnd, 0, (ulong)totalTests);
        }
        catch
        {
            _taskbar = null; // graceful fallback if COM init fails
        }
    }

    public void UpdateProgress(int current, string fixtureName)
    {
        if (_subtitleText is not null)
            _subtitleText.Text = $"{current}/{_testSegments.Count} \u2014 {fixtureName}";

        _taskbar?.SetProgressValue(_hwnd, (ulong)current, (ulong)_totalTests);
    }

    /// <summary>
    /// Sets the taskbar to green (complete) or red (error) when all tests finish.
    /// </summary>
    public void FinalizeTaskbarProgress()
    {
        if (_taskbar is null) return;
        _taskbar.SetProgressValue(_hwnd, (ulong)_totalTests, (ulong)_totalTests);
        _taskbar.SetProgressState(_hwnd,
            _failures > 0 ? TaskbarProgressFlags.Error : TaskbarProgressFlags.NoProgress);
    }

    /// <summary>
    /// Colors the segment at <paramref name="index"/> green (pass) or red (fail).
    /// </summary>
    public void MarkFixtureResult(int index, bool passed)
    {
        if (index < 0 || index >= _testSegments.Count) return;
        _testSegments[index].Background = new SolidColorBrush(
            passed
                ? global::Windows.UI.Color.FromArgb(255, 76, 175, 80)   // green
                : global::Windows.UI.Color.FromArgb(255, 244, 67, 54)); // red
    }

    /// <summary>
    /// Colors the segment at <paramref name="index"/> yellow to indicate a skipped fixture.
    /// </summary>
    public void MarkFixtureSkipped(int index)
    {
        if (index < 0 || index >= _testSegments.Count) return;
        _testSegments[index].Background = new SolidColorBrush(
            global::Windows.UI.Color.FromArgb(255, 255, 193, 7)); // amber/yellow
    }

    public ReactorHost CreateHost()
    {
        var host = new ReactorHost(_window);
        if (_contentArea is not null)
            host.ContentTarget = _contentArea;
        return host;
    }

    /// <summary>
    /// Places arbitrary content into the test content area (below the TitleBar).
    /// Use this instead of setting Window.Content directly to avoid overwriting
    /// the TitleBar and progress bar.
    /// </summary>
    public void SetContent(UIElement? content)
    {
        if (_contentArea is not null)
            _contentArea.Child = content;
        else
            _window.Content = content;
    }

    // -- TAP assertion helpers -------------------------------------------

    public void Check(string name, bool result)
    {
        if (result)
            Console.WriteLine($"ok {name}");
        else
        {
            Console.WriteLine($"not ok {name} - assertion failed");
            _failures++;
        }
    }

    /// <summary>
    /// Emits a TAP "skipped" line for a known-failing or deferred check
    /// without counting it as a pass OR a failure. Use for documented
    /// gaps that have a tracking item — the assertion is explicit in
    /// the log instead of being silently dropped.
    /// </summary>
    public void Skip(string name, string reason)
    {
        Console.WriteLine($"ok {name} # SKIP {reason}");
    }

    public void Check(string name, Func<bool> test)
    {
        try { Check(name, test()); }
        catch (Exception ex)
        {
            Console.WriteLine($"not ok {name} - {ex.GetType().Name}: {ex.Message}");
            _failures++;
        }
    }

    public async Task CheckAsync(string name, Func<Task<bool>> test)
    {
        try { Check(name, await test()); }
        catch (Exception ex)
        {
            Console.WriteLine($"not ok {name} - {ex.GetType().Name}: {ex.Message}");
            _failures++;
        }
    }

    // -- Render / timing -------------------------------------------------

    /// <summary>
    /// Waits for ReactorHost to finish all pending render passes, then forces a
    /// synchronous WinUI layout update so ActualWidth/ActualHeight are current.
    /// Pass a non-zero <paramref name="ms"/> only for genuinely async operations
    /// (e.g. WebView2 initialization) that need wall-clock time beyond the render.
    /// </summary>
    public static async Task Render(int ms = 0)
    {
        // Wait for Reactor's render loop to go idle (all pending + re-renders done)
        if (ReactorApp.PrimaryWindow?.Host is { } host)
        {
            await host.WaitForIdleAsync();
        }

        var dq = DispatcherQueue.GetForCurrentThread();

        // Force synchronous layout so ActualWidth/ActualHeight are ready.
        // This is also what triggers TabView's selected-tab content presenter
        // to schedule its content-realization work onto the dispatcher.
        (_currentWindow?.Content as UIElement)?.UpdateLayout();

        // Yield once at Low priority AFTER UpdateLayout. WaitForIdleAsync
        // short-circuits when Reactor reports idle; that left callers racing
        // the WinUI side because TabView lazy-realizes the selected pane's
        // body via Normal-priority dispatcher messages SCHEDULED BY the
        // layout pass we just forced. A Low-priority yield here guarantees
        // those messages have drained — without it, a 16ms wall-clock delay
        // is enough on CI but flakes on contended local machines (visible
        // in NativeDocking_* fixtures where the pane Memo subtree probes
        // returned null ~30–60% of the time on local 10x sweeps).
        var yieldTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dq.TryEnqueue(DispatcherQueuePriority.Low, () => yieldTcs.SetResult()))
            yieldTcs.SetResult();
        await yieldTcs.Task;

        // Re-run layout in case the just-realized content needs an arrangement
        // pass (e.g. a Memo body that mounted during the yield needs to size
        // its TextBlocks before FindText can match by exact-text).
        (_currentWindow?.Content as UIElement)?.UpdateLayout();

        // Small breathing room for the compositor to finish processing
        // visual tree changes. Without this, rapid fixture transitions can
        // outpace the WinUI compositor and cause native segfaults.
        await Task.Delay(16 + ms);
    }

    private static Window? _currentWindow;

    // -- VisualTree query helpers ----------------------------------------

    /// <summary>Search root: the content area (below TitleBar) if set up, else Window.Content.</summary>
    private DependencyObject? SearchRoot => (DependencyObject?)_contentArea?.Child ?? _window.Content;

    public T? FindControl<T>(Func<T, bool> predicate) where T : DependencyObject
    {
        var root = SearchRoot;
        if (root is null) return default;
        return FindInTree(root, predicate);
    }

    public List<T> FindAllControls<T>(Func<T, bool> predicate) where T : DependencyObject
    {
        var results = new List<T>();
        var root = SearchRoot;
        if (root is not null)
            FindAllInTree(root, predicate, results);
        return results;
    }

    public Button? FindButton(string label)
        => FindControl<Button>(b => b.Content is string s && s == label);

    public TextBlock? FindText(string text)
        => FindControl<TextBlock>(tb => tb.Text == text);

    public TextBlock? FindTextContaining(string substring)
        => FindControl<TextBlock>(tb => tb.Text?.Contains(substring) == true);

    public int CountControls<T>() where T : DependencyObject
        => FindAllControls<T>(_ => true).Count;

    // -- Interaction helpers ----------------------------------------------

    public void ClickButton(string label)
    {
        var btn = FindButton(label);
        if (btn is not null && btn.IsEnabled)
        {
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(btn);
            var invokeProvider = (Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)
                peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
            invokeProvider.Invoke();
        }
    }

    public void ToggleCheckBox(string label)
    {
        var cb = FindControl<CheckBox>(c => c.Content is string s && s == label);
        if (cb is not null)
            cb.IsChecked = cb.IsChecked != true;
    }

    // -- Tree walking ----------------------------------------------------

    private static T? FindInTree<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        if (root is T match && predicate(match)) return match;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindInTree(VisualTreeHelper.GetChild(root, i), predicate);
            if (found is not null) return found;
        }
        return null;
    }

    private static void FindAllInTree<T>(DependencyObject root, Func<T, bool> predicate, List<T> results) where T : DependencyObject
    {
        if (root is T match && predicate(match)) results.Add(match);
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
            FindAllInTree(VisualTreeHelper.GetChild(root, i), predicate, results);
    }
}

// -- ITaskbarList3 COM interop for taskbar progress overlay ---------------

[Flags]
internal enum TaskbarProgressFlags
{
    NoProgress = 0x00,
    Indeterminate = 0x01,
    Normal = 0x02,
    Error = 0x04,
    Paused = 0x08,
}

[ComImport]
[Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarList3
{
    // ITaskbarList
    void HrInit();
    void AddTab(IntPtr hwnd);
    void DeleteTab(IntPtr hwnd);
    void ActivateTab(IntPtr hwnd);
    void SetActiveAlt(IntPtr hwnd);

    // ITaskbarList2
    void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

    // ITaskbarList3
    void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
    void SetProgressState(IntPtr hwnd, TaskbarProgressFlags flags);
}

[ComImport]
[Guid("56fdf344-fd6d-11d0-958a-006097c9a090")]
[ClassInterface(ClassInterfaceType.None)]
internal class TaskbarList { }
