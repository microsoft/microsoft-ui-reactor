using System.Runtime.InteropServices;
using global::System.Windows.Automation;
using global::System.Windows.Automation.Text;
using Microsoft.UI.Reactor.AppTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Tests;

[TestClass]
public sealed partial class MarkdownSelectionInputProofTests : AppTestBase
{
    private const int PointerSettleMs = 80;
    private const int SampleIntervalMs = 150;
    private const int PollIntervalMs = 100;
    private const int WaitSeconds = 8;
    private const uint MouseMove = 0x0001, MouseMoveNoCoalesce = 0x2000, MouseVirtualDesk = 0x4000, MouseAbsolute = 0x8000;
    private readonly HashSet<ushort> _heldKeys = [];
    [ClassInitialize]
    public static void Start(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void Stop() => TestSession.AssemblyCleanup();

    [TestMethod]
    [E2eRetry(3)]
    public void Selection_RoutedInputAndExternalEvents()
    {
        SessionInteractivityGuard.EnsureInputInjectable(nameof(Selection_RoutedInputAndExternalEvents));
        GetWindowThreadProcessId((nint)TestSession.HostHwnd, out uint windowPid);
        Assert.AreEqual((uint)TestSession.HostPid, windowPid, "Only foreground the owned host window");
        AcquireForeground();
        var window = AutomationElement.FromHandle((nint)TestSession.HostHwnd);
        AutomationElement Find(string id) => window.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, id))
            ?? throw new InvalidOperationException($"Missing {id}");
        GuardForeground();
        if (Find("FixtureStatus").Current.Name != "Ready")
        {
            ((InvokePattern)Find("ResetFixture").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Wait(() => Find("FixtureStatus").Current.Name == "Ready", "fresh fixture reset");
        }
        GuardForeground();
        ((InvokePattern)Find("Nav_Markdown_Selection_InputProof").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Wait(() => Find("FixtureStatus").Current.Name == "Loaded: Markdown_Selection_InputProof", "fixture navigation");
        var outer = Find("SelectionOuter");
        var inner = Find("SelectionInner");
        var outerText = (TextPattern)outer.GetCurrentPattern(TextPattern.Pattern);
        var innerText = (TextPattern)inner.GetCurrentPattern(TextPattern.Pattern);
        int textEvents = 0, selectionEvents = 0;
        var receivedText = new global::System.Collections.Concurrent.ConcurrentQueue<string>();
        var receivedSelection = new global::System.Collections.Concurrent.ConcurrentQueue<string>();
        var eventFailures = new global::System.Collections.Concurrent.ConcurrentQueue<Exception>();
        AutomationEventHandler textHandler = (_, _) =>
        {
            try
            {
                receivedText.Enqueue(innerText.DocumentRange.GetText(-1));
                Interlocked.Increment(ref textEvents);
            }
            catch (Exception ex) { eventFailures.Enqueue(ex); }
        };
        AutomationEventHandler selectionHandler = (_, _) =>
        {
            try
            {
                receivedSelection.Enqueue(innerText.GetSelection().Single().GetText(-1));
                Interlocked.Increment(ref selectionEvents);
            }
            catch (Exception ex) { eventFailures.Enqueue(ex); }
        };
        bool textRegistered = false, selectionRegistered = false, mouseHeld = false;
        nint previousDpiContext = 0;
        Exception? testFailure = null;
        try
        {
            previousDpiContext = SetThreadDpiAwarenessContext(-4);
            Assert.AreNotEqual((nint)0, previousDpiContext, "Per-monitor-aware input coordinates");
            Automation.AddAutomationEventHandler(TextPattern.TextChangedEvent, inner, TreeScope.Element, textHandler);
            textRegistered = true;
            Automation.AddAutomationEventHandler(TextPattern.TextSelectionChangedEvent, inner, TreeScope.Element, selectionHandler);
            selectionRegistered = true;
            void Point(double x, double y)
            {
                GuardForeground();
                int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
                int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
                Assert.IsTrue(width > 0 && height > 0, "Virtual desktop has an input extent");
                Assert.IsTrue(x >= left && x < left + width && y >= top && y < top + height,
                    "Pointer target is inside the virtual desktop");
                Send(new Input
                {
                    Union = new InputUnion
                    {
                        Mouse = new MouseInput
                        {
                            X = (int)Math.Clamp(Math.Floor((Math.Round(x) - left + .5) * 65536 / width), 0, 65535),
                            Y = (int)Math.Clamp(Math.Floor((Math.Round(y) - top + .5) * 65536 / height), 0, 65535),
                            Flags = MouseMove | MouseMoveNoCoalesce | MouseVirtualDesk | MouseAbsolute
                        }
                    }
                });
                Thread.Sleep(PointerSettleMs);
            }
            void Mouse(uint flags)
            {
                GuardForeground();
                Send(new Input { Type = 0, Union = new InputUnion { Mouse = new MouseInput { Flags = flags } } });
            }
            void Click(double x, double y)
            {
                Point(x, y);
                Mouse(2); mouseHeld = true;
                Mouse(4); mouseHeld = false;
            }
            static double[] Glyph(TextPatternRange document, int start)
            {
                var range = document.Clone();
                range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, start);
                range.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start);
                range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1);
                var bounds = range.GetBoundingRectangles();
                Assert.IsTrue(bounds.Length > 0 && bounds[0].Width > 0 && bounds[0].Height > 0);
                return [bounds[0].X, bounds[0].Y, bounds[0].Width, bounds[0].Height];
            }
            var link = outerText.DocumentRange.FindText("native link", false, false);
            Assert.IsNotNull(link);
            var linkGlyph = Glyph(link, 2);
            Console.WriteLine($"Link glyph screen bounds={string.Join(",", linkGlyph)}");
            Click(linkGlyph[0] + linkGlyph[2] / 2, linkGlyph[1] + linkGlyph[3] / 2);
            Wait(() => Find("SelectionClicks").Current.Name == "Clicks 1", "native hyperlink click");
            Console.WriteLine($"Hyperlink click: {Find("SelectionClicks").Current.Name}");
            Console.WriteLine($"After click routing: {outer.Current.HelpText}");
            Point(linkGlyph[0], linkGlyph[1] + linkGlyph[3] / 2);
            Mouse(2); mouseHeld = true;
            Wait(() => outer.Current.HelpText.Contains("press pressCount=2 ", StringComparison.Ordinal),
                "link press delivered before pointer moves");
            Point(linkGlyph[0] + 35, linkGlyph[1] + linkGlyph[3] / 2);
            Console.WriteLine($"Before drag release: selected={outerText.GetSelection().Single().GetText(-1)}; routing={outer.Current.HelpText}");
            Mouse(4); mouseHeld = false;
            Console.WriteLine($"After drag release: clicks={Find("SelectionClicks").Current.Name}; routing={outer.Current.HelpText}");
            Wait(() => outerText.GetSelection().Single().GetText(-1).Length > 0, "link drag selects");
            var dragged = outerText.GetSelection().Single();
            Assert.IsTrue(dragged.CompareEndpoints(TextPatternRangeEndpoint.Start, link, TextPatternRangeEndpoint.Start) >= 0
                && dragged.CompareEndpoints(TextPatternRangeEndpoint.End, link, TextPatternRangeEndpoint.End) <= 0,
                "Link drag range must stay inside the pressed hyperlink");
            Assert.AreEqual("Clicks 1", Find("SelectionClicks").Current.Name, "drag must not activate link");
            var outerSelection = outerText.GetSelection().Single().GetText(-1);
            var outerRangeBeforeInnerInput = outerText.GetSelection().Single().Clone();
            Console.WriteLine($"Link drag selected={outerSelection}");
            var innerGlyph = Glyph(innerText.DocumentRange, 1);
            Click(innerGlyph[0] + innerGlyph[2] / 2, innerGlyph[1] + innerGlyph[3] / 2);
            Thread.Sleep(SampleIntervalMs);
            uint clipboardSequence = GetClipboardSequenceNumber();
            Key(0x11, false);
            Key(0x41, false); Key(0x41, true);
            Key(0x43, false); Key(0x43, true);
            Key(0x11, true);
            Wait(() => innerText.GetSelection().Single().GetText(-1) == "inner text", "inner Ctrl+A");
            Wait(() => receivedSelection.Contains("inner text"), "Ctrl+A exact selection-change event");
            Assert.AreEqual("Clicks 1", Find("SelectionClicks").Current.Name, "drag did not activate link after timer settle");
            Assert.AreEqual(outerSelection, outerText.GetSelection().Single().GetText(-1), "outer selection unchanged");
            Assert.IsTrue(outerRangeBeforeInnerInput.Compare(outerText.GetSelection().Single()),
                "Nested input must not move either endpoint of the saved outer range");
            Wait(() => GetClipboardSequenceNumber() != clipboardSequence && ReadClipboard() == "inner text",
                "fresh inner clipboard write");
            var clipboard = ReadClipboard();
            Assert.AreEqual("inner text", clipboard, "inner clipboard");
            Console.WriteLine($"Nested Ctrl+A/C: inner={innerText.GetSelection().Single().GetText(-1)} outer={outerSelection} clipboard={clipboard}; selectionEvents={selectionEvents}");
            Click(innerGlyph[0], innerGlyph[1] + innerGlyph[3] / 2);
            Wait(() => innerText.GetSelection().Single().GetText(-1) == "", "plain click collapses selection");
            var shiftAnchor = innerText.GetSelection().Single().Clone();
            var shiftGlyph = Glyph(innerText.DocumentRange, 4);
            Key(0x10, false);
            Click(shiftGlyph[0], shiftGlyph[1] + shiftGlyph[3] / 2);
            Wait(() => innerText.GetSelection().Single().GetText(-1).Length > 0, "Shift+click extends selection");
            Key(0x10, true);
            Assert.AreEqual(0, innerText.GetSelection().Single().CompareEndpoints(
                TextPatternRangeEndpoint.Start, shiftAnchor, TextPatternRangeEndpoint.Start), "Shift+click keeps anchor");
            Console.WriteLine($"Shift+click selected={innerText.GetSelection().Single().GetText(-1)}");
            ((InvokePattern)Find("SelectionStream").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Wait(() => innerText.DocumentRange.GetText(-1) == "inner text streamed" &&
                receivedText.Contains("inner text streamed"), "streaming exact text-change event");
            Console.WriteLine($"Streaming text={innerText.DocumentRange.GetText(-1)} selected={innerText.GetSelection().Single().GetText(-1)}; textEvents={textEvents}");

            var longElement = Find("SelectionLong");
            var longText = (TextPattern)longElement.GetCurrentPattern(TextPattern.Pattern);
            var timeline = Find("SelectionTimeline");
            var scrollElement = timeline.FindFirst(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true));
            Assert.IsNotNull(scrollElement);
            var scroll = (ScrollPattern)scrollElement.GetCurrentPattern(ScrollPattern.Pattern);
            var firstGlyph = Glyph(longText.DocumentRange, 1);
            Point(firstGlyph[0] + firstGlyph[2] / 2, firstGlyph[1] + firstGlyph[3] / 2);
            Mouse(2); mouseHeld = true;
            Wait(() => longText.GetSelection().Single().CompareEndpoints(TextPatternRangeEndpoint.Start,
                longText.DocumentRange, TextPatternRangeEndpoint.Start) > 0, "long-message press delivered");
            var viewport = scrollElement.Current.BoundingRectangle;
            Point(viewport.Left + viewport.Width / 2, viewport.Bottom + 12);
            Assert.IsTrue(GetCursorPos(out var stationary));
            double previousScroll = scroll.Current.VerticalScrollPercent;
            int previousLength = longText.GetSelection().Single().GetText(-1).Length;
            Console.WriteLine($"Stationary drag baseline: pointer={stationary.X},{stationary.Y}; scrollPercent={previousScroll}; selectedLength={previousLength}");
            for (int sample = 1; sample <= 3; sample++)
            {
                Thread.Sleep(SampleIntervalMs);
                Wait(() =>
                {
                    GuardForeground();
                    Assert.IsTrue(GetCursorPos(out var current));
                    Assert.AreEqual(stationary.X, current.X, "Stationary pointer X unchanged");
                    Assert.AreEqual(stationary.Y, current.Y, "Stationary pointer Y unchanged");
                    return scroll.Current.VerticalScrollPercent > previousScroll &&
                        longText.GetSelection().Single().GetText(-1).Length > previousLength;
                }, $"stationary captured edge drag advance {sample}");
                previousScroll = scroll.Current.VerticalScrollPercent;
                previousLength = longText.GetSelection().Single().GetText(-1).Length;
                Console.WriteLine($"Stationary drag sample {sample}: pointer={stationary.X},{stationary.Y}; scrollPercent={previousScroll}; selectedLength={previousLength}");
            }
            Mouse(4); mouseHeld = false;
            Assert.AreEqual("Clicks 1", Find("SelectionClicks").Current.Name, "drag did not activate link at end");
            if (!eventFailures.IsEmpty) throw new AggregateException("External UIA event reads failed", eventFailures);
            Console.WriteLine($"Received TextChanged={string.Join("|", receivedText)}; TextSelectionChanged={string.Join("|", receivedSelection)}");
            Console.WriteLine($"External input checks passed; textEvents={textEvents}, selectionEvents={selectionEvents}, PID={TestSession.HostPid}");
        }
        catch (Exception ex)
        {
            testFailure = ex;
            SessionInteractivityGuard.RecheckAfterFailure(nameof(Selection_RoutedInputAndExternalEvents));
            throw;
        }
        finally
        {
            var cleanupFailures = new List<Exception>();
            void Cleanup(Action action)
            {
                try { action(); }
                catch (Exception ex) { cleanupFailures.Add(ex); }
            }
            if (mouseHeld) Cleanup(() => Send(new Input { Union = new InputUnion { Mouse = new MouseInput { Flags = 4 } } }));
            foreach (ushort key in _heldKeys)
                Cleanup(() => Send(new Input { Type = 1, Union = new InputUnion { Keyboard = new KeyboardInput { Key = key, Flags = 2 } } }));
            _heldKeys.Clear();
            if (selectionRegistered) Cleanup(() => Automation.RemoveAutomationEventHandler(TextPattern.TextSelectionChangedEvent, inner, selectionHandler));
            if (textRegistered) Cleanup(() => Automation.RemoveAutomationEventHandler(TextPattern.TextChangedEvent, inner, textHandler));
            if (previousDpiContext != 0)
                Cleanup(() => Assert.AreNotEqual((nint)0, SetThreadDpiAwarenessContext(previousDpiContext)));
            if (cleanupFailures.Count > 0)
            {
                if (testFailure is not null) cleanupFailures.Insert(0, testFailure);
                throw new AggregateException("Test and cleanup failures", cleanupFailures);
            }
        }
    }

    private static string ReadClipboard()
    {
        string? text = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { text = global::System.Windows.Clipboard.GetText(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(5)), "STA clipboard read completed");
        if (failure is not null) global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return text!;
    }

    private static void Wait(Func<bool> predicate, string operation)
    {
        var until = DateTime.UtcNow.AddSeconds(WaitSeconds);
        do { if (predicate()) return; Thread.Sleep(PollIntervalMs); } while (DateTime.UtcNow < until);
        Assert.Fail($"Timed out: {operation}");
    }

    private static void GuardForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        if (pid != (uint)TestSession.HostPid)
            SessionInteractivityGuard.RecheckAfterFailure("Owned foreground guard");
        Assert.AreEqual((uint)TestSession.HostPid, pid, "Abort input: owned host is not foreground");
    }

    private static bool HostOwnsForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        return pid == (uint)TestSession.HostPid;
    }

    /// <summary>
    /// Brings the owned host to the foreground. Unlike every other E2E test, this one does not reach
    /// the host through a winapp click (whose real pointer press activates the window); it asks for
    /// the foreground itself, from the long-lived test process. Windows' foreground lock refuses a
    /// bare <c>SetForegroundWindow</c> from that process after another process's input (such as an
    /// earlier class's winapp click) has activated a different window, so whether the call took
    /// depended on run order and timing, and every retry repeated the same refused call. A zero
    /// mouse input (no movement, no button) makes this process the last input source, which the
    /// lock honours. There is deliberately no <c>AttachThreadInput</c> bridge, and the restore is
    /// <c>ShowWindowAsync</c>: neither may block on a hung host or foreground thread past the deadline. The zero input
    /// is best-effort: if it is refused, the foreground predicate still decides, and the timeout
    /// reports the refusal alongside the foreground diagnostics.
    /// </summary>
    private static void AcquireForeground()
    {
        var host = (nint)TestSession.HostHwnd;
        bool requested = false;
        uint creditSent = 0;
        int creditError = 0;
        var until = DateTime.UtcNow.AddSeconds(WaitSeconds);
        do
        {
            if (HostOwnsForeground()) return;
            if (IsIconic(host)) ShowWindowAsync(host, SwRestore);
            creditSent = SendInput(1, [new Input { Type = 0 }], Marshal.SizeOf<Input>());
            creditError = creditSent == 1 ? 0 : Marshal.GetLastWin32Error();
            requested = SetForegroundWindow(host);
            Thread.Sleep(PollIntervalMs);
        } while (DateTime.UtcNow < until);
        if (HostOwnsForeground()) return;
        SessionInteractivityGuard.RecheckAfterFailure("Owned host foreground");
        Assert.Fail($"Timed out: owned host foreground; zeroInputSent={creditSent} (error {creditError}); " +
            DescribeForeground(host, requested));
    }

    private static unsafe string DescribeForeground(nint host, bool lastRequestResult)
    {
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out uint pid);
        string process;
        try { using var p = global::System.Diagnostics.Process.GetProcessById((int)pid); process = p.ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { process = "<exited>"; }
        char* buffer = stackalloc char[256];
        string className = foreground == 0 ? "" : new string(buffer, 0, Math.Max(0, GetClassName(foreground, buffer, 256)));
        string title = foreground == 0 ? "" : new string(buffer, 0, Math.Max(0, GetWindowText(foreground, buffer, 256)));
        var lastInput = new LastInputInfo { Size = (uint)sizeof(LastInputInfo) };
        string idle = GetLastInputInfo(ref lastInput) ? $"{unchecked((uint)Environment.TickCount - lastInput.Time)}ms" : "?";
        string lockTimeout = SystemParametersInfo(SpiGetForegroundLockTimeout, 0, out uint timeout, 0) ? $"{timeout}ms" : "?";
        return $"foreground hwnd=0x{foreground:X} pid={pid} process={process} class='{className}' title='{title}'; " +
            $"host hwnd=0x{host:X} pid={TestSession.HostPid} iconic={IsIconic(host)} visible={IsWindowVisible(host)}; " +
            $"lastSetForegroundWindow={lastRequestResult}; inputIdle={idle}; foregroundLockTimeout={lockTimeout}; " +
            SessionInteractivityGuard.Diagnose();
    }

    private void Key(ushort key, bool up)
    {
        GuardForeground();
        if (!up) _heldKeys.Add(key);
        Send(new Input { Type = 1, Union = new InputUnion { Keyboard = new KeyboardInput { Key = key, Flags = up ? 2u : 0u } } });
        if (up) _heldKeys.Remove(key);
    }

    private static void Send(Input input)
    {
        Assert.AreEqual(1u, SendInput(1, [input], Marshal.SizeOf<Input>()), $"SendInput failed: {Marshal.GetLastWin32Error()}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenPoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo { public uint Size, Time; }
    private const int SwRestore = 9;
    private const uint SpiGetForegroundLockTimeout = 0x2000;
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(nint window);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindowAsync(nint window, int command);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    private static unsafe partial int GetClassName(nint window, char* buffer, int capacity);
    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    private static unsafe partial int GetWindowText(nint window, char* buffer, int capacity);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo info);
    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfo(uint action, uint param, out uint value, uint winIni);
    [LibraryImport("user32.dll")] private static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);
    [LibraryImport("user32.dll")] private static partial uint GetClipboardSequenceNumber();
    [LibraryImport("user32.dll")] private static partial uint GetWindowThreadProcessId(nint window, out uint pid);
    [LibraryImport("user32.dll", SetLastError = true)] private static partial nint SetThreadDpiAwarenessContext(nint context);
    [LibraryImport("user32.dll")] private static partial int GetSystemMetrics(int index);
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out ScreenPoint point);
    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint count, [In] Input[] inputs, int size);
}
