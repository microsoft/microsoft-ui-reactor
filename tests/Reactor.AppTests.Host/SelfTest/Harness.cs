using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Hosting.Shell;
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
            // Reuse core's AOT-safe [GeneratedComInterface] singleton (which calls
            // HrInit internally) instead of a duplicate [ComImport] activation.
            _taskbar = TaskbarComSingleton.TryGet();
            if (_taskbar is not null)
            {
                _taskbar.SetProgressState(_hwnd, NativeTaskbarProgressState.Normal);
                _taskbar.SetProgressValue(_hwnd, 0, (ulong)totalTests);
            }
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
            _failures > 0 ? NativeTaskbarProgressState.Error : NativeTaskbarProgressState.NoProgress);
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
        // Bounded convergence loop. A single (WaitForIdle → UpdateLayout →
        // Low-yield → UpdateLayout) pass drains one wave of WinUI's lazy
        // realization: TabView selecting a pane → its ContentPresenter
        // realizes → the pane's body mounts. But composed surfaces (a Reactor
        // DockManager whose pane content is another DockManager, or a TabView
        // inside a TabView) need MULTIPLE waves because each wave's mount
        // schedules the next wave's Normal-priority realization message.
        //
        // We loop until Reactor reports idle AND we've done at least
        // `minPasses` rounds (so purely WinUI-driven multi-wave realization
        // — which doesn't necessarily touch Reactor's renderPending flag —
        // still gets enough dispatcher drain). Steady state (one-wave trees)
        // exits after pass 2 with no real cost: WaitForIdleAsync short-
        // circuits when Reactor was already idle and UpdateLayout is a no-op
        // when nothing's dirty.
        //
        // The single Low-yield + 16ms Delay combo was previously load-
        // bearing for ~98.4% of fixtures; the multi-wave NativeDocking sites
        // (TabView → nested DockManager → nested TabView) were the remaining
        // ~1.6% flake source observed in 1000-iteration stress runs.
        var host = ReactorApp.PrimaryWindow?.Host;
        var dq = DispatcherQueue.GetForCurrentThread();
        const int minPasses = 2;
        const int maxPasses = 4;

        int pass = 0;
        for (; pass < maxPasses; pass++)
        {
            if (host is not null) await host.WaitForIdleAsync();

            // Re-read window content each pass — an async mount may have
            // replaced it between passes.
            (_currentWindow?.Content as UIElement)?.UpdateLayout();

            // Yield once at Low priority AFTER UpdateLayout so any
            // Normal-priority TabView content-realization messages scheduled
            // by the layout pass drain before we probe.
            var yieldTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!dq.TryEnqueue(DispatcherQueuePriority.Low, () => yieldTcs.SetResult()))
                yieldTcs.SetResult();
            await yieldTcs.Task;

            // Re-run layout in case the just-realized content needs an
            // arrangement pass (e.g. a Memo body that mounted during the
            // yield needs to size its TextBlocks before FindText can match
            // by exact-text).
            (_currentWindow?.Content as UIElement)?.UpdateLayout();

            // Stability gate: at least `minPasses` rounds AND Reactor idle.
            // A nested sub-host that bumpTicks during realization flips
            // IsIdle false → loop until it settles or we hit the cap.
            if (pass + 1 >= minPasses && (host is null || host.IsIdle)) break;
        }

        // Diagnostic: if we exited at the cap and the host is still
        // non-idle, surface a TAP comment so the next flake is greppable
        // instead of silent. Mirrors WaitForIdleAsync's yield-cap log.
        if (pass >= maxPasses && host is not null && !host.IsIdle)
            Console.WriteLine("# Harness.Render exited at maxPasses while host was still non-idle");

        // Small breathing room for the compositor to finish processing
        // visual tree changes. Without this, rapid fixture transitions can
        // outpace the WinUI compositor and cause native segfaults.
        await Task.Delay(16 + ms);
    }

    /// <summary>
    /// Pumps render passes until <paramref name="condition"/> holds, re-evaluating
    /// it on the live visual tree after each pass. Returns true once it holds, or
    /// false if it never does within <paramref name="maxPasses"/>.
    ///
    /// This is the contention-proof alternative to asserting against a one-shot
    /// snapshot taken right after a single <see cref="Render"/>. WinUI surfaces
    /// like nested DockManager/TabView realize their inner content over multiple
    /// dispatcher waves that Reactor's idle predicate cannot observe, so a fixed
    /// wave count can be outpaced on a contended CI runner. Looping a re-queried
    /// predicate against real pumps converges regardless of how many waves the
    /// runtime takes (the NativeAOT host consistently needs one more than JIT).
    ///
    /// The predicate MUST re-query the tree each call (e.g. call
    /// <see cref="FindText"/>/<see cref="FindControl{T}"/> inside it) — passing a
    /// value captured from an earlier snapshot defeats the loop.
    ///
    /// Pass a non-zero <paramref name="perPassMs"/> for conditions that need
    /// wall-clock time per pass (e.g. waiting for an exit transition to play out).
    /// </summary>
    public static async Task<bool> WaitFor(Func<bool> condition, int maxPasses = 25, int perPassMs = 0)
    {
        for (int i = 0; i < maxPasses; i++)
        {
            if (condition()) return true;
            await Render(perPassMs);
        }
        return condition();
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

    /// <summary>
    /// Invokes the <see cref="Button"/> whose Content equals <paramref name="label"/>.
    ///
    /// <para>Throws when the button is missing OR disabled. A fixture is a stimulus followed
    /// by an assertion, so a stimulus that silently does not land leaves the assertion
    /// measuring the UNSTIMULATED state — and every assertion of the form "X was left
    /// alone" / "X was restored" / "X is still within tolerance" passes on that state. The
    /// fixture then goes green having exercised nothing, and "clicked it" is byte-identical
    /// to "silently did nothing" at the call site (issue #1063).</para>
    ///
    /// <para>To assert that a disabled button ignores clicks, use
    /// <see cref="RequireButtonDisabled"/> — it too throws when the label is wrong, so the
    /// assertion cannot pass for the wrong reason.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">No such button, or the button is disabled.</exception>
    public void ClickButton(string label)
    {
        var btn = RequireButton(nameof(ClickButton), label);
        if (!btn.IsEnabled)
            throw new InvalidOperationException(
                $"{nameof(ClickButton)}(\"{OneLine(label)}\"): the Button is disabled, so the click was NOT " +
                $"delivered. If the fixture means to prove that a disabled button ignores clicks, " +
                $"call {nameof(RequireButtonDisabled)} instead.");

        InvokeButton(btn);
    }

    /// <summary>
    /// Asserts that the button carrying <paramref name="label"/> is present but disabled —
    /// and therefore that no click can land on it. Deliberately does NOT click.
    ///
    /// <para>This is the sanctioned way to prove "a disabled button ignores clicks". It
    /// throws in BOTH failure directions: a wrong label and an unexpectedly ENABLED button
    /// are each a broken fixture. That is why it returns <c>void</c> rather than a
    /// <c>bool</c> — a returned flag can be dropped at the call site, and a silently
    /// ignorable signal is precisely the defect this guard exists to prevent (issue #1063).
    /// Reintroducing one here would rebuild the bug inside its own fix.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">No such button, or the button is enabled.</exception>
    public void RequireButtonDisabled(string label)
    {
        var btn = RequireButton(nameof(RequireButtonDisabled), label);
        if (btn.IsEnabled)
            throw new InvalidOperationException(
                $"{nameof(RequireButtonDisabled)}(\"{OneLine(label)}\"): the Button is ENABLED, but the " +
                $"fixture expected it to be disabled. A real click would land here, so whatever the " +
                $"fixture asserts next about nothing having happened would be measuring the wrong thing.");
    }

    /// <summary>
    /// Flips the <see cref="CheckBox"/> whose Content equals <paramref name="label"/>.
    /// Throws when there is no such CheckBox, for the reasons in <see cref="ClickButton"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">No CheckBox carries <paramref name="label"/>.</exception>
    public void ToggleCheckBox(string label)
    {
        var cb = FindControl<CheckBox>(c => c.Content is string s && s == label)
            ?? throw new InvalidOperationException(
                $"{nameof(ToggleCheckBox)}(\"{OneLine(label)}\"): no CheckBox with that Content is in the " +
                $"visual tree. {DescribeContent<CheckBox>("CheckBox")}");

        cb.IsChecked = cb.IsChecked is not true;
    }

    private Button RequireButton(string caller, string label)
        => FindButton(label)
           ?? throw new InvalidOperationException(
               $"{caller}(\"{OneLine(label)}\"): no Button with that Content is in the visual tree. " +
               DescribeContent<Button>("Button"));

    private static void InvokeButton(Button btn)
    {
        var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(btn);
        var invokeProvider = (Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)
            peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke);
        invokeProvider.Invoke();
    }

    /// <summary>
    /// "3 Button(s) present: "Save", "Cancel", "Reset"." — turns a crash line into a fix
    /// without a debugger attach, the way <c>UiElementResolver.FindByName</c> does for E2E.
    /// </summary>
    private string DescribeContent<T>(string kind) where T : ContentControl
    {
        var all = FindAllControls<T>(_ => true);
        if (all.Count == 0) return $"No {kind} is mounted at all.";

        var labels = all
            .Select(c => c.Content is string s ? $"\"{OneLine(s)}\"" : $"<{c.Content?.GetType().Name ?? "null"}>")
            .Take(20)
            .ToList();
        var more = all.Count > labels.Count ? $", \u2026 (+{all.Count - labels.Count} more)" : "";
        return $"{all.Count} {kind}(s) present: {string.Join(", ", labels)}{more}.";
    }

    /// <summary>
    /// Escapes the characters that would break the single-line TAP record this text ends up
    /// in. A throw from here surfaces as <c>not ok &lt;n&gt; &lt;fixture&gt;_CRASH - &lt;msg&gt;</c>
    /// (SelfTestRunner.cs), and SelfTestBatch.ParseTap reads that stream line by line — so a
    /// raw newline inside a control label would split one failure into two records and the
    /// tail could be re-read as a forged <c>ok</c> / <c># Total failures:</c> line.
    /// </summary>
    private static string OneLine(string s)
        => s.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

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

// Windows taskbar progress uses core's AOT-safe
// Microsoft.UI.Reactor.Hosting.Shell.ITaskbarList3 / TaskbarComSingleton
// (see _taskbar init in SetupTitleBar) — no duplicate [ComImport] shim here.
