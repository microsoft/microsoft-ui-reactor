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
            Input MoveInput(double x, double y)
            {
                GuardForeground();
                int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
                int width = GetSystemMetrics(78), height = GetSystemMetrics(79);
                Assert.IsTrue(width > 0 && height > 0, "Virtual desktop has an input extent");
                Assert.IsTrue(x >= left && x < left + width && y >= top && y < top + height,
                    "Pointer target is inside the virtual desktop");
                return new Input
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
                };
            }
            void Point(double x, double y)
            {
                Send(MoveInput(x, y));
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
            void MultiClick(double x, double y, int count)
            {
                for (int i = 0; i < count; i++) Click(x, y);
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
            var customLinkRange = outerText.DocumentRange.FindText("button link", false, false);
            Assert.IsNotNull(customLinkRange);
            var customButton = Find("SelectionCustomLink");
            var customGlyph = Glyph(customLinkRange, 2);
            double customX = customGlyph[0] + customGlyph[2] / 2;
            double customY = customGlyph[1] + customGlyph[3] / 2;
            var customDragGlyph = Glyph(customLinkRange, 9);
            double customDragX = customDragGlyph[0] + customDragGlyph[2] / 2;
            double customDragY = customDragGlyph[1] + customDragGlyph[3] / 2;
            Console.WriteLine($"Custom link drag screen positions: ({customX},{customY}) to ({customDragX},{customDragY})");
            Assert.IsTrue(Math.Abs(customDragX - customX) > 4, "Custom link glyphs must be farther apart than the drag threshold");
            string routingBeforeClick = outer.Current.HelpText;
            Point(customX, customY);
            Mouse(2); mouseHeld = true;
            Wait(() => outer.Current.HelpText != routingBeforeClick &&
                outer.Current.HelpText.Split(" | ")[^1].StartsWith("press ", StringComparison.Ordinal),
                "custom button click press delivered");
            Assert.IsTrue(outer.Current.HelpText.Split(" | ")[^1]
                .Contains("nativePending=True tracking=True", StringComparison.Ordinal),
                "A pending native click must keep selection layout tracking active");
            Mouse(4); mouseHeld = false;
            Wait(() => outer.Current.HelpText.Split(" | ")[^1].StartsWith("release ", StringComparison.Ordinal) &&
                outer.Current.HelpText.Split(" | ")[^1].Contains("nativePending=False", StringComparison.Ordinal),
                "custom button release clears the pending native press");
            Wait(() => customButton.Current.HelpText == "Custom clicks 1", "custom link button click");
            Thread.Sleep((int)GetDoubleClickTime() + 1);
            Point(customX, customY);
            string routingBeforeCustomPress = outer.Current.HelpText;
            Mouse(2); mouseHeld = true;
            Wait(() => outer.Current.HelpText != routingBeforeCustomPress &&
                outer.Current.HelpText.Split(" | ")[^1].StartsWith("press ", StringComparison.Ordinal) &&
                outerText.GetSelection().Single().GetText(-1) == "",
                "custom button press was routed without selecting a word");
            string customPress = outer.Current.HelpText.Split(" | ")[^1];
            Assert.IsTrue(customPress.Contains("nativePending=True tracking=True", StringComparison.Ordinal),
                "A pending native press must keep selection layout tracking active");
            Point(customDragX, customDragY);
            Assert.IsTrue(GetCursorPos(out var customDragCursor));
            Assert.IsTrue(Math.Abs(customDragCursor.X - customDragX) <= 1 &&
                Math.Abs(customDragCursor.Y - customDragY) <= 1, "Cursor reached the second custom link glyph");
            Console.WriteLine($"Custom button drag before release: routing={outer.Current.HelpText}");
            Wait(() => outerText.GetSelection().Single().GetText(-1).Length > 0,
                "custom link button drag selects before release");
            Mouse(4); mouseHeld = false;
            Wait(() => outerText.GetSelection().Single().GetText(-1).Length > 0,
                "custom link button drag selects");
            var customDragged = outerText.GetSelection().Single();
            Assert.IsTrue(customDragged.CompareEndpoints(TextPatternRangeEndpoint.Start,
                    customLinkRange, TextPatternRangeEndpoint.Start) >= 0
                && customDragged.CompareEndpoints(TextPatternRangeEndpoint.End,
                    customLinkRange, TextPatternRangeEndpoint.End) <= 0,
                "Custom link drag range must stay inside the link label");
            Assert.AreEqual("Custom clicks 1", customButton.Current.HelpText,
                "custom link drag must not activate its release-mode button");
            var outerSelection = outerText.GetSelection().Single().GetText(-1);
            Console.WriteLine($"Link drag selected={outerSelection}");
            var innerGlyph = Glyph(innerText.DocumentRange, 1);
            double innerX = innerGlyph[0] + innerGlyph[2] / 2;
            double innerY = innerGlyph[1] + innerGlyph[3] / 2;
            MultiClick(innerX, innerY, 2);
            Console.WriteLine($"Inner double-click selection={innerText.GetSelection().Single().GetText(-1)}; routing={inner.Current.HelpText}");
            Wait(() => innerText.GetSelection().Single().GetText(-1) == "inner ",
                "double-click selects the word with trailing whitespace");
            Click(innerX, innerY);
            Wait(() => innerText.GetSelection().Single().GetText(-1) == "inner text",
                "triple-click selects the Markdown paragraph");
            Key(0x27, false); Key(0x27, true);
            Wait(() => innerText.GetSelection().Single().GetText(-1) == "" &&
                receivedSelection.Contains(""), "collapse before testing Ctrl+A");
            receivedSelection.Clear();
            uint clipboardSequence = GetClipboardSequenceNumber();
            Key(0x11, false);
            Key(0x41, false); Key(0x41, true);
            Key(0x43, false); Key(0x43, true);
            Key(0x11, true);
            Wait(() => innerText.GetSelection().Single().GetText(-1) == "inner text", "inner Ctrl+A");
            Wait(() => receivedSelection.Contains("inner text"), "Ctrl+A exact selection-change event");
            Assert.AreEqual("Clicks 1", Find("SelectionClicks").Current.Name, "drag did not activate link after timer settle");
            var outerRangeAfterClickAway = outerText.GetSelection().Single();
            Assert.AreEqual("", outerRangeAfterClickAway.GetText(-1),
                "Clicking another selection scope clears the old selection");
            Assert.AreEqual(0, outerRangeAfterClickAway.CompareEndpoints(
                TextPatternRangeEndpoint.Start, outerRangeAfterClickAway, TextPatternRangeEndpoint.End),
                "Click-away leaves no selected accessibility range");
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
            var lastWord = Glyph(innerText.DocumentRange, 7);
            double lastX = lastWord[0] + lastWord[2] / 2;
            double lastY = lastWord[1] + lastWord[3] / 2;
            Click(lastX, lastY);
            Mouse(2); mouseHeld = true;
            Wait(() => innerText.GetSelection().Single().GetText(-1) == "text",
                "second press anchors the final word before streaming");
            ((InvokePattern)Find("SelectionStream").GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Wait(() => innerText.DocumentRange.GetText(-1) == "new inner text" &&
                receivedText.Contains("new inner text"), "streaming exact text-change event");
            Assert.AreEqual("text", innerText.GetSelection().Single().GetText(-1),
                "layout during a held double-click must preserve the whole word");
            innerGlyph = Glyph(innerText.DocumentRange, 5);
            innerX = innerGlyph[0] + innerGlyph[2] / 2;
            innerY = innerGlyph[1] + innerGlyph[3] / 2;
            Point(innerX, innerY);
            Wait(() => innerText.GetSelection().Single().GetText(-1) == "inner text",
                "reverse word drag retains its anchor after a streaming prefix edit");
            Mouse(4); mouseHeld = false;
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
            var currentLink = outerText.DocumentRange.FindText("native link", false, false);
            Assert.IsNotNull(currentLink);
            var currentLinkGlyph = Glyph(currentLink, 2);
            Click(currentLinkGlyph[0] + currentLinkGlyph[2] / 2, currentLinkGlyph[1] + currentLinkGlyph[3] / 2);
            Wait(() => Find("SelectionClicks").Current.Name == "Clicks 2", "link click after selection");
            var dismissed = longText.GetSelection().Single();
            Assert.AreEqual("", dismissed.GetText(-1), "Clicking another message collapses the old selection");
            Assert.AreEqual(0, dismissed.CompareEndpoints(TextPatternRangeEndpoint.Start,
                dismissed, TextPatternRangeEndpoint.End), "Click-away leaves no selected accessibility range");
            void SelectInnerText()
            {
                innerText.DocumentRange.Select();
                Wait(() => innerText.GetSelection().Single().GetText(-1) == "new inner text",
                    "reselect inner message before click-away");
            }
            SelectInnerText();
            var blank = Find("SelectionBlank");
            var blankBounds = blank.Current.BoundingRectangle;
            Click(blankBounds.Left + blankBounds.Width / 2, blankBounds.Top + blankBounds.Height / 2);
            Wait(() => innerText.GetSelection().Single().GetText(-1) == "" &&
                blank.Current.HelpText == "Blank clicks 1", "blank chat click dismisses selection and reaches target");
            var currentInnerGlyph = Glyph(innerText.DocumentRange, 5);
            double currentInnerX = currentInnerGlyph[0] + currentInnerGlyph[2] / 2;
            double currentInnerY = currentInnerGlyph[1] + currentInnerGlyph[3] / 2;
            var innerMove = MoveInput(currentInnerX, currentInnerY);
            var blankMove = MoveInput(blankBounds.Left + blankBounds.Width / 2, blankBounds.Top + blankBounds.Height / 2);
            var down = new Input { Union = new InputUnion { Mouse = new MouseInput { Flags = 2 } } };
            var up = new Input { Union = new InputUnion { Mouse = new MouseInput { Flags = 4 } } };
            static int PressCount(string routing)
            {
                string press = routing.Split(" | ").Last(entry => entry.StartsWith("press pressCount=", StringComparison.Ordinal));
                int start = "press pressCount=".Length;
                return int.Parse(press.AsSpan(start, press.IndexOf(' ', start) - start),
                    global::System.Globalization.CultureInfo.InvariantCulture);
            }
            int finalPress = PressCount(inner.Current.HelpText) + 3;
            // One batch keeps all four clicks in the same double-click interval.
            Input[] clickAwaySequence = [innerMove, down, up, blankMove, down, up, innerMove, down, up, down, up];
            mouseHeld = true;
            Assert.AreEqual((uint)clickAwaySequence.Length,
                SendInput((uint)clickAwaySequence.Length, clickAwaySequence, Marshal.SizeOf<Input>()));
            mouseHeld = false;
            Wait(() => blank.Current.HelpText == "Blank clicks 2",
                "blank chat click resets pending multiclick state");
            Wait(() => inner.Current.HelpText.Split(" | ")[^1].StartsWith(
                    $"release pressCount={finalPress} ", StringComparison.Ordinal),
                "all three inner clicks finished");
            Assert.AreEqual("inner ", innerText.GetSelection().Single().GetText(-1),
                "two presses after click-away select a word rather than a paragraph");
            SelectInnerText();
            var composer = Find("SelectionComposer");
            var composerBounds = composer.Current.BoundingRectangle;
            Click(composerBounds.Left + composerBounds.Width / 2, composerBounds.Top + composerBounds.Height / 2);
            Wait(() => innerText.GetSelection().Single().GetText(-1) == "" && composer.Current.HasKeyboardFocus,
                "composer click dismisses selection and receives keyboard focus");
            var membership = Find("SelectionMembership");
            var membershipText = (TextPattern)membership.GetCurrentPattern(TextPattern.Pattern);
            Assert.AreEqual("first\nsecond", membershipText.DocumentRange.GetText(-1));
            var membershipEvents = new global::System.Collections.Concurrent.ConcurrentQueue<string>();
            AutomationEventHandler membershipHandler = (_, _) =>
            {
                try { membershipEvents.Enqueue(membershipText.DocumentRange.GetText(-1)); }
                catch (Exception ex) { eventFailures.Enqueue(ex); }
            };
            Automation.AddAutomationEventHandler(TextPattern.TextChangedEvent, membership, TreeScope.Element, membershipHandler);
            try
            {
                var add = Find("SelectionAddMember");
                add.SetFocus();
                Wait(() => add.Current.HasKeyboardFocus, "membership scope is idle");
                void Invoke(string id) => ((InvokePattern)Find(id).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                Invoke("SelectionAddMember");
                Wait(() => membershipEvents.Contains("first\nsecond\nthird"), "idle child mount TextChanged");
                Invoke("SelectionAddMember");
                Wait(() => membershipEvents.Contains("first\nsecond"), "idle child unmount TextChanged");
                Invoke("SelectionToggleMemberVisibility");
                Wait(() => membershipEvents.Contains("first"), "idle native collapse TextChanged");
                Invoke("SelectionToggleMemberVisibility");
                Wait(() => membershipEvents.Count(value => value == "first\nsecond") >= 2,
                    "idle native expansion TextChanged");
                Invoke("SelectionAddMember");
                Wait(() => membershipEvents.Count(value => value == "first\nsecond\nthird") >= 2,
                    "idle remount TextChanged");
                Invoke("SelectionReverseMembers");
                Wait(() => membershipEvents.Contains("third\nsecond\nfirst"), "idle keyed reorder TextChanged");
            }
            finally { Automation.RemoveAutomationEventHandler(TextPattern.TextChangedEvent, membership, membershipHandler); }
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

    [LibraryImport("user32.dll")]
    private static partial uint GetDoubleClickTime();
}
