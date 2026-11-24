using Microsoft.UI.Reactor.AppTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Tests;

[TestClass]
public sealed class ContentSizerOracleTests
{
    [TestMethod]
    [DataRow(140, 56.99613952636719, 1, 196)]
    [DataRow(196, -36.9, 1, 160)]
    [DataRow(140, 11.9, 2, 150)]
    [DataRow(150, -11.9, 2, 140)]
    public void Calculation_TruncatesNativeCumulativeRatherThanPointerDistance(
        double initial, double cumulative, double increment, double expected) =>
        Assert.AreEqual(expected, ContentSizerOracle.CalculateHeight(initial, cumulative, increment, 0, double.PositiveInfinity, 12));

    [TestMethod]
    public void NativeTrace_AcceptsIndependentForwardAndReverseMeasurements()
    {
        Assert.AreEqual(196, Evaluate(Trace(140, 196, 56.99613952636719)));
        Assert.AreEqual(160, Evaluate(Trace(196, 160, -36.9, gesture: 2), 2, -1, 196));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("stale")]
    [DataRow("incomplete")]
    [DataRow("no-deltas")]
    [DataRow("zero")]
    [DataRow("wrong-target")]
    [DataRow("noop")]
    [DataRow("wrong-advertised-size")]
    [DataRow("wrong-initial-size")]
    [DataRow("nonfinite")]
    [DataRow("wrong-direction")]
    [DataRow("changed-native-sibling")]
    public void NativeTrace_RejectsVacuousOrIncorrectEvidence(string fault)
    {
        string trace = Trace(140, 196, 56.99613952636719);
        trace = fault switch
        {
            "missing" => "No completed native manipulation.",
            "stale" => trace.Replace("gesture=1", "gesture=0"),
            "incomplete" => trace[..trace.IndexOf("\nphase=completed;", StringComparison.Ordinal)],
            "no-deltas" => trace[..trace.IndexOf("\nphase=delta;", StringComparison.Ordinal)] +
                trace[trace.IndexOf("\nphase=completed;", StringComparison.Ordinal)..],
            "zero" => Trace(140, 140, 0),
            "wrong-target" => trace.Replace("target-is-advertised=True", "target-is-advertised=False"),
            "noop" => Trace(140, 140, 56.99613952636719),
            "wrong-advertised-size" => trace.Replace("advertised-height=196", "advertised-height=140"),
            "wrong-initial-size" => trace.Replace("initial-target-actual-height=140", "initial-target-actual-height=0"),
            "nonfinite" => trace.Replace("56.99613952636719", "NaN"),
            "wrong-direction" => Trace(140, 104, -36.9),
            "changed-native-sibling" => trace[..trace.LastIndexOf("sibling-height=12", StringComparison.Ordinal)] +
                "sibling-height=18; sibling-width=360",
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        Assert.Throws<InvalidDataException>(() => Evaluate(trace), fault);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("wrong-window")]
    [DataRow("wrong-selector")]
    [DataRow("wrong-destination")]
    [DataRow("zero")]
    public void Gesture_RequiresSuccessfulIntendedCliCoordinates(string fault)
    {
        var drag = Drag(1);
        var json = fault switch
        {
            "missing" => "{}",
            "wrong-window" => drag.Json.Replace("\"hwnd\":12", "\"hwnd\":13"),
            "wrong-selector" => drag.Json.Replace("ContentSizer.Sizer", "ContentSizer.Target"),
            "wrong-destination" => drag.Json.Replace("\"toY\":66", "\"toY\":65"),
            "zero" => drag.Json.Replace("\"fromY\":6", "\"fromY\":66"),
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        Assert.Throws<InvalidDataException>(() => Evaluate(Trace(140, 196, 56.99613952636719), drag: drag with { Json = json }), fault);
    }

    [TestMethod]
    public void Sibling_SizeMustStayFixedButPositionMayFollowResizedTarget()
    {
        string trace = Trace(140, 196, 56.99613952636719);
        Assert.AreEqual(196, Evaluate(trace, sibling: new UiRect(0, 56, 360, 12)));
        Assert.Throws<InvalidDataException>(() => Evaluate(trace, sibling: new UiRect(0, 56, 361, 12)));
        Assert.Throws<InvalidDataException>(() => Evaluate(trace, sibling: new UiRect(0, 56, 360, 13)));
    }

    [TestMethod]
    public void Calculation_RejectsInvalidInputsAndNativeBoundsInsteadOfClamping()
    {
        Assert.Throws<InvalidDataException>(() => ContentSizerOracle.CalculateHeight(140, 60, 0, 0, 360, 12));
        Assert.Throws<InvalidDataException>(() => ContentSizerOracle.CalculateHeight(140, double.NaN, 1, 0, 360, 12));
        Assert.Throws<InvalidDataException>(() => ContentSizerOracle.CalculateHeight(140, 60, 1, 0, 180, 12));
        Assert.Throws<InvalidDataException>(() => ContentSizerOracle.CalculateHeight(140, -40, 1, 120, 360, 12));
        Assert.Throws<InvalidDataException>(() => ContentSizerOracle.CalculateHeight(140, -128, 1, 0, 360, 12));
    }

    private static double Evaluate(string trace, int gesture = 1, int direction = 1, double initial = 140,
        DragEvidence? drag = null, UiRect? sibling = null) =>
        ContentSizerOracle.ExpectedHeight(trace, gesture, direction, (initial, initial, 360),
            drag ?? Drag(direction), 12, sibling ?? new UiRect(0, 0, 360, 12));

    private static DragEvidence Drag(int direction)
    {
        int toY = direction > 0 ? 66 : -34;
        return new DragEvidence(new UiRect(0, 0, 360, 12), 1, 180, toY,
            $$"""{"from":"ContentSizer.Sizer","to":"180,{{toY}}","fromX":180,"fromY":6,"toX":180,"toY":{{toY}},"hwnd":12,"button":"left"}""");
    }

    private static string Trace(double initial, double final, double cumulative, int gesture = 1) =>
        FormattableString.Invariant($"""
            gesture={gesture}; orientation=Horizontal; drag-increment=1
            inverted=False; target-is-advertised=True
            initial-target-height={initial:R}; initial-target-actual-height={initial:R}
            initial-advertised-height={initial:R}; initial-advertised-actual-height={initial:R}
            minimum-height=0; maximum-height=Infinity; sibling-height=12; sibling-width=360
            phase=starting; delta=<not supplied>; cumulative=<not supplied>
            target-height={initial:R}; target-actual-height={initial:R}; advertised-height={initial:R}; advertised-actual-height={initial:R}
            sibling-height=12; sibling-width=360
            phase=started; delta=<not supplied>; cumulative=0,0
            target-height={initial:R}; target-actual-height={initial:R}; advertised-height={initial:R}; advertised-actual-height={initial:R}
            sibling-height=12; sibling-width=360
            phase=delta; delta=0,{cumulative:R}; cumulative=0,{cumulative:R}
            target-height={final:R}; target-actual-height={initial:R}; advertised-height={final:R}; advertised-actual-height={initial:R}
            sibling-height=12; sibling-width=360
            phase=completed; delta=<not supplied>; cumulative=0,{cumulative:R}
            target-height={final:R}; target-actual-height={final:R}; advertised-height={final:R}; advertised-actual-height={final:R}
            sibling-height=12; sibling-width=360
            """).Replace("\r\n", "\n");
}
