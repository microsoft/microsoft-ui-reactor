using System.Globalization;
using Microsoft.UI.Reactor.AppTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Tests;

[TestClass]
public sealed class ToolkitInputTests : AppTestBase
{
    [ClassInitialize]
    public static void StartAppSession(TestContext context) => TestSession.AssemblyInit(context);

    [ClassCleanup]
    public static void StopAppSession() => TestSession.AssemblyCleanup();

    [TestMethod]
    [E2eRetry(3)]
    public void RangeSelector_DragBothThumbs_AcceptsAndRoutesCallbacks()
    {
        NavigateToFixtureFresh("ToolkitInput_RangeSelector");
        WaitForObservation("20|80");
        WaitForText("Range.Callbacks", "Endpoint callbacks: start 0, end 0");

        DragRangeThumb("MinThumb", toMinimum: true);
        WaitForObservation("0|80");
        WaitForText("Range.Parent", "Parent range: 0 \u2013 80");
        WaitForText("Range.Callbacks", "Endpoint callbacks: start 1, end 0");

        DragRangeThumb("MaxThumb", toMinimum: false);
        WaitForObservation("0|100");
        WaitForText("Range.Parent", "Parent range: 0 \u2013 100");
        WaitForText("Range.Callbacks", "Endpoint callbacks: start 1, end 1");

        App.Click("Range.Reset");
        WaitForText("Range.Parent", "Parent range: 20 \u2013 80");
        WaitForObservation("20|80");
        WaitForText("Range.Callbacks", "Endpoint callbacks: start 1, end 1");
    }

    [TestMethod]
    [E2eRetry(3)]
    public void RangeSelector_DragBothThumbs_RejectsAndReassertsParentValues()
    {
        NavigateToFixtureFresh("ToolkitInput_RangeSelector");
        WaitForObservation("20|80");
        App.Click("Range.AcceptChanges");

        DragRangeThumb("MinThumb", toMinimum: true);
        WaitForText("Range.LastChange", "Start requested 0: rejected.");
        WaitForText("Range.Callbacks", "Endpoint callbacks: start 1, end 0");
        WaitForObservation("20|80");

        DragRangeThumb("MaxThumb", toMinimum: false);
        WaitForText("Range.LastChange", "End requested 100: rejected.");
        WaitForText("Range.Callbacks", "Endpoint callbacks: start 1, end 1");
        WaitForObservation("20|80");
        WaitForText("Range.Parent", "Parent range: 20 \u2013 80");
    }

    [TestMethod]
    [E2eRetry(3)]
    public void RichSuggestBox_DownEnter_CommitsFullTokensAcrossRenders()
    {
        // Direct navigation also exercises disposal of the previous route's observer.
        NavigateToFixtureFresh("ToolkitInput_RangeSelector");
        WaitForObservation("20|80");
        NavigateToFixture("ToolkitInput_RichSuggestBox");
        WaitForObservation(value => value.Split('\n') is ["", "", _], "empty rich document and tokens");
        WaitForText("RichSuggestBox.LastChoice", "Last chosen: none (0 selections)");
        App.Click("ToolkitInput.RichEditor");

        for (int count = 1; count <= 2; count++)
        {
            App.SendKeys(count == 1 ? "text=@al" : @"text=\s@al", viaSendInput: true);
            WaitForObservation(value => value.Split('\n') is [_, var text, "Alex Wilber"] &&
                text.EndsWith("@al", StringComparison.Ordinal), "typed query and filtered Alex Wilber suggestion");
            App.SendKeys("down", viaSendInput: true);
            App.SendKeys("enter", viaSendInput: true);

            var expectedTokens = string.Join('|', Enumerable.Repeat("@Alex Wilber=Alex Wilber", count));
            WaitForObservation(value => value.Split('\n') is [var tokens, var text, _] &&
                tokens == expectedTokens &&
                text.Split("@Alex Wilber", StringSplitOptions.None).Length == count + 1 &&
                !text.Contains("@al", StringComparison.Ordinal), $"{count} full native tokens and document mentions");
            WaitForText("RichSuggestBox.LastChoice", $"Last chosen: Alex Wilber ({count} selections)");
        }
    }

    [TestMethod]
    [E2eRetry(3)]
    public void TokenizingTextBox_TypesConvertsAndRemovesOnlyChosenToken()
    {
        NavigateToFixtureFresh("ToolkitInput_TokenizingTextBox");
        WaitForObservation(value => value.Split('\n') is ["", "", _], "empty token list and editor");
        App.Click("ToolkitInput.TokenEditor");
        App.SendKeys("text=cal", viaSendInput: true);
        WaitForText("Tokens.Status", "Editing: cal");
        WaitForObservation(value => value.Split('\n') is ["", "cal", "Calendar"], "filtered Calendar suggestion");

        App.SendKeys("text=,", viaSendInput: true);
        WaitForObservation(value => value.Split('\n') is ["Calendar", "", _], "converted Calendar token");
        App.SendKeys("text=zz-no-match,", viaSendInput: true);
        WaitForObservation(value => value.Split('\n') is ["Calendar|zz-no-match", "", _], "converted and free-text tokens");

        App.Click("ToolkitInput.Remove.Calendar");
        WaitForObservation(value => value.Split('\n') is ["zz-no-match", "", _], "only Calendar removed");
        App.Click("ToolkitInput.Remove.zz-no-match");
        WaitForObservation(value => value.Split('\n') is ["", "", _], "all tokens removed");
    }

    [TestMethod]
    [E2eRetry(3)]
    public void ContentSizer_Drag_ResizesAdvertisedTarget()
    {
        NavigateToFixtureFresh("ToolkitInput_ContentSizer");
        WaitForObservation("140|140|360");
        int gesture = 0;
        foreach (double dy in new[] { 60.0, -40.0 })
        {
            var before = Numbers(ReadObservation().Value);
            var drag = DragBy("ContentSizer.Sizer", dx: 0, dy);
            var trace = WaitForNativeSizerTrace("ContentSizer.Sizer", ++gesture);
            TestContext?.WriteLine($"native-manipulation-trace={trace}");
            double expected = ContentSizerOracle.ExpectedHeight(trace, gesture, Math.Sign(dy),
                (before[0], before[1], before[2]), drag, HostHwnd, Bounds("ContentSizer.Sizer"));
            TestContext?.WriteLine(FormattableString.Invariant($"native-contract-expected-height={expected:R}"));
            WaitForObservation(value => Numbers(value) is [var height, var actual, var width] &&
                height == expected && double.IsFinite(actual) && Math.Abs(actual - expected) <= 2 &&
                width == before[2], $"advertised Height={expected:R}, settled layout within 2 DIPs and unchanged width");
        }
    }

    [TestMethod]
    [E2eRetry(3)]
    public void PropertySizer_Drag_UpdatesTargetAndClampsBothBounds()
    {
        NavigateToFixtureFresh("ToolkitInput_PropertySizer");
        WaitForObservation("220|220|220");
        DragBy("ToolkitInput.PropertySizer", dx: 240, dy: 0);
        WaitForObservation("360|360|360");
        DragBy("ToolkitInput.PropertySizer", dx: -300, dy: 0);
        WaitForObservation("120|120|120");
    }

    [TestMethod]
    [E2eRetry(3)]
    public void GridSplitter_Drag_ResizesBothColumns()
    {
        NavigateToFixtureFresh("ToolkitInput_GridSplitter");
        var before = Numbers(WaitForObservation(value =>
            value != "Waiting" && Numbers(value) is [> 100, var middle, > 100, > 100, > 100, > 0, > 0] &&
            Math.Abs(middle - 11) <= 1, "advertised grid, three columns and two panes"));
        var drag = DragBy("ToolkitInput.GridSplitter", dx: 60, dy: 0);
        var trace = WaitForNativeSizerTrace("ToolkitInput.GridSplitter", 1);
        TestContext?.WriteLine($"native-grid-manipulation-trace={trace}");
        var expected = GridSplitterOracle.ExpectedWidths(trace, 1, 1, before, drag, HostHwnd);
        WaitForObservation(value =>
        {
            var actual = Numbers(value);
            return actual.Length == 7 && actual.All(double.IsFinite) &&
                Math.Abs(actual[0] - expected.Left) <= 2 && Math.Abs(actual[2] - expected.Right) <= 2 &&
                Math.Abs(actual[3] - expected.Left) <= 2 && Math.Abs(actual[4] - expected.Right) <= 2 &&
                Math.Abs(actual[1] - before[1]) <= 2 &&
                Math.Abs(actual[0] + actual[2] - before[0] - before[2]) <= 2 &&
                actual[5] == before[5] && actual[6] == before[6] &&
                Math.Sign(actual[3] - before[3]) == Math.Sign(expected.Left - before[0]) &&
                Math.Sign(actual[4] - before[4]) == Math.Sign(expected.Right - before[2]);
        }, $"native star allocation {expected.Left:R}/{expected.Right:R}, matching advertised panes and fixed sibling");
    }

    [TestMethod]
    [E2eRetry(3)]
    public void RangeSelector_TabAndShiftTab_MoveActualKeyboardFocus()
    {
        NavigateToFixtureFresh("ToolkitInput_RangeSelector");
        WaitForObservation("20|80");
        // A pointer press on the Toolkit thumb starts manipulation without focusing it.
        // Enter its tab stops from the real button immediately after the range instead.
        App.Click("Range.Reset");
        WaitForText("ToolkitInput.Focus", "Range.Reset");
        App.SendKeys("shift+tab", viaSendInput: true);
        WaitForText("ToolkitInput.Focus", "ToolkitInput.MaxThumb");
        App.SendKeys("shift+tab", viaSendInput: true);
        WaitForText("ToolkitInput.Focus", "ToolkitInput.MinThumb");
        // No --target or UiElement.SendKeys: either could focus the destination and
        // conceal broken Tab routing. This checks focus, not unsupported keyboard values.
        App.SendKeys("tab", viaSendInput: true);
        WaitForText("ToolkitInput.Focus", "ToolkitInput.MaxThumb");
        App.SendKeys("tab", viaSendInput: true);
        WaitForText("ToolkitInput.Focus", "Range.Reset");
        App.SendKeys("shift+tab", viaSendInput: true);
        WaitForText("ToolkitInput.Focus", "ToolkitInput.MaxThumb");
        App.SendKeys("shift+tab", viaSendInput: true);
        WaitForText("ToolkitInput.Focus", "ToolkitInput.MinThumb");
    }

    private void DragRangeThumb(string name, bool toMinimum)
    {
        var track = Bounds("Toolkit.RangeSelector");
        var thumb = Bounds($"ToolkitInput.{name}");
        var destination = $"{(toMinimum ? track.X : track.X + track.Width - 1)},{thumb.Y + thumb.Height / 2}";
        TestContext?.WriteLine(
            $"drag-request source=ToolkitInput.{name}; source-bounds={thumb}; track-bounds={track}; " +
            $"destination={destination}; dpi={App.GetValue("ToolkitInput.Dpi")} (endpoints use screen pixels)");
        var result = App.Drag($"ToolkitInput.{name}", destination);
        TestContext?.WriteLine($"drag-result-json={result}");
    }

    private DragEvidence DragBy(string id, double dx, double dy)
    {
        var bounds = Bounds(id);
        double dpi = double.Parse(App.GetValue("ToolkitInput.Dpi")!, CultureInfo.InvariantCulture);
        Assert.IsTrue(double.IsFinite(dpi) && dpi > 0, $"Invalid fixture DPI {dpi}.");
        int x = bounds.X + bounds.Width / 2 + (int)Math.Round(dx * dpi);
        int y = bounds.Y + bounds.Height / 2 + (int)Math.Round(dy * dpi);
        TestContext?.WriteLine(FormattableString.Invariant(
            $"drag-request source={id}; source-bounds={bounds}; dpi={dpi:R}; delta-dip={dx:R},{dy:R}; destination={x},{y}"));
        var result = App.Drag(id, $"{x},{y}");
        TestContext?.WriteLine($"drag-result-json={result}");
        return new DragEvidence(bounds, dpi, x, y, result);
    }

    private UiRect Bounds(string id)
    {
        var bounds = App.GetBounds(id) ?? throw new AssertFailedException($"No bounds for {id}.");
        Assert.IsTrue(bounds.Width > 0 && bounds.Height > 0, $"Empty bounds for {id}: {bounds}.");
        return bounds;
    }

    private void WaitForObservation(string expected) => WaitForObservation(value => value == expected, expected);

    private string WaitForObservation(Func<string, bool> matches, string expected)
    {
        var first = ReadObservation();
        var latest = first;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        do
        {
            Thread.Sleep(100);
            latest = ReadObservation();
            if (latest.Sequence > first.Sequence && matches(latest.Value))
                return latest.Value;
        } while (DateTime.UtcNow < deadline);
        throw new AssertFailedException(
            $"Expected fresh Toolkit observation '{expected}', got '{latest.Value}' " +
            $"(sequence {first.Sequence} -> {latest.Sequence}).");
    }

    private (long Sequence, string Value) ReadObservation()
    {
        var text = App.GetValue("ToolkitInput.Observation")
            ?? throw new AssertFailedException("Toolkit observation label is missing.");
        if (text == "Waiting") return (0, text);
        var parts = text.Split('\n', 2);
        Assert.AreEqual(2, parts.Length, $"Malformed Toolkit observation '{text}'.");
        return (long.Parse(parts[0], CultureInfo.InvariantCulture), parts[1]);
    }

    private static double[] Numbers(string value) =>
        value.Split('|').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();

    private string WaitForNativeSizerTrace(string id, int gesture)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        string? trace;
        do
        {
            trace = App.GetProperty(id, "HelpText");
            if (trace is not null && trace.StartsWith($"gesture={gesture};", StringComparison.Ordinal) &&
                trace.Contains("phase=completed;", StringComparison.Ordinal))
                return trace;
            Thread.Sleep(100);
        } while (DateTime.UtcNow < deadline);
        throw new AssertFailedException($"No fresh completed native gesture {gesture} for {id}. Last trace: {trace ?? "<unavailable>"}");
    }

    private void WaitForSizes(double[] expected) => WaitForObservation(value =>
    {
        if (value == "Waiting") return false;
        var actual = Numbers(value);
        // At most two DIPs for integer screen-coordinate rounding and native drag snapping.
        return actual.Length == expected.Length &&
            actual.Zip(expected).All(pair => double.IsFinite(pair.First) && Math.Abs(pair.First - pair.Second) <= 2);
    }, string.Join('|', expected.Select(n => n.ToString("R", CultureInfo.InvariantCulture))));
}
