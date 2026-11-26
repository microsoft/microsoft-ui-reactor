using Microsoft.UI.Reactor.AppTests.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.UI.Reactor.AppTests.Tests;

[TestClass]
public sealed class GridSplitterOracleTests
{
    [TestMethod]
    [DataRow(56.99613952636719, 1, false, 386, 244)]
    [DataRow(-36.9, 1, false, 294, 336)]
    [DataRow(11.9, 2, false, 340, 290)]
    [DataRow(11.9, 2, true, 320, 310)]
    public void StarWeights_UseInitialActualWidthsNativeDeltaAndFlow(
        double cumulative, double increment, bool rtl, double left, double right)
    {
        var result = GridSplitterOracle.CalculateStarWeights(330, 300, cumulative, increment, rtl,
            0, double.PositiveInfinity, 0, double.PositiveInfinity, 11);
        Assert.AreEqual(left, result.Left);
        Assert.AreEqual(right, result.Right);
    }

    [TestMethod]
    public void NativeTrace_AcceptsBothStarWeightsAndRtlDirection()
    {
        Assert.AreEqual((386.0, 274.0), Evaluate(Trace()));
        Assert.AreEqual((274.0, 386.0), Evaluate(Trace(left: 274, right: 386, flow: "RightToLeft")));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("stale")]
    [DataRow("incomplete")]
    [DataRow("zero")]
    [DataRow("wrong-grid")]
    [DataRow("replaced-definition")]
    [DataRow("swapped-pane")]
    [DataRow("wrong-direction-mode")]
    [DataRow("pixel-instead-of-star")]
    [DataRow("changed-middle")]
    [DataRow("noop-weights")]
    [DataRow("wrong-pair")]
    [DataRow("changed-row")]
    [DataRow("nonfinite")]
    public void NativeTrace_RejectsMissingWrongOrVacuousEvidence(string fault)
    {
        string trace = Trace();
        trace = fault switch
        {
            "missing" => "No completed native manipulation.",
            "stale" => trace.Replace("gesture=1", "gesture=0"),
            "incomplete" => trace[..trace.IndexOf("\nphase=completed;", StringComparison.Ordinal)],
            "zero" => Trace(cumulative: 0, left: 330, right: 330),
            "wrong-grid" => trace.Replace("parent-is-advertised=True", "parent-is-advertised=False"),
            "replaced-definition" => trace.Replace("definitions-unchanged=True", "definitions-unchanged=False"),
            "swapped-pane" => trace.Replace("left-column=0", "left-column=2"),
            "wrong-direction-mode" => trace.Replace("orientation=Vertical", "orientation=Horizontal"),
            "pixel-instead-of-star" => trace.Replace("column-0-unit=Star", "column-0-unit=Pixel"),
            "changed-middle" => trace.Replace("column-1-value=11", "column-1-value=12"),
            "noop-weights" => Trace(left: 1, right: 1),
            "wrong-pair" => Trace(left: 386, right: 275),
            "changed-row" => trace[..trace.LastIndexOf("row-value=1", StringComparison.Ordinal)] +
                trace[trace.LastIndexOf("row-value=1", StringComparison.Ordinal)..].Replace("row-value=1", "row-value=2"),
            "nonfinite" => trace.Replace("56.99613952636719", "NaN"),
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        Assert.Throws<InvalidDataException>(() => Evaluate(trace), fault);
    }

    [TestMethod]
    public void CliEvidence_RequiresTheOwnedGridSplitterAndHorizontalGesture()
    {
        var drag = Drag();
        Assert.Throws<InvalidDataException>(() => Evaluate(Trace(),
            drag with { Json = drag.Json.Replace("ToolkitInput.GridSplitter", "ContentSizer.Sizer") }));
        Assert.Throws<InvalidDataException>(() => Evaluate(Trace(),
            drag with { Json = drag.Json.Replace("\"hwnd\":12", "\"hwnd\":13") }));
        Assert.Throws<InvalidDataException>(() => Evaluate(Trace(),
            drag with { Json = drag.Json.Replace("\"fromY\":130", "\"fromY\":129") }));
    }

    [TestMethod]
    public void NativeBounds_RejectThePairRatherThanClampOneSide()
    {
        Assert.Throws<InvalidDataException>(() =>
            GridSplitterOracle.CalculateStarWeights(330, 300, 56.9, 1, false, 0, 350, 0, 500, 11));
        Assert.Throws<InvalidDataException>(() =>
            GridSplitterOracle.CalculateStarWeights(330, 300, 56.9, 1, false, 0, 500, 280, 500, 11));
        Assert.Throws<InvalidDataException>(() =>
            GridSplitterOracle.CalculateStarWeights(20, 300, -9, 1, false, 0, 500, 0, 500, 11));
        Assert.Throws<InvalidDataException>(() =>
            GridSplitterOracle.CalculateStarWeights(330, 300, 56.9, 0, false, 0, 500, 0, 500, 11));
    }

    private static (double Left, double Right) Evaluate(string trace, DragEvidence? drag = null) =>
        GridSplitterOracle.ExpectedWidths(trace, 1, 1, [330, 11, 330, 330, 330, 671, 260], drag ?? Drag(), 12);

    private static DragEvidence Drag() => new(new UiRect(300, 0, 11, 260), 1, 365, 130,
        """{"from":"ToolkitInput.GridSplitter","to":"365,130","fromX":305,"fromY":130,"toX":365,"toY":130,"hwnd":12,"button":"left"}""");

    private static string Trace(double cumulative = 56.99613952636719, double left = 386,
        double right = 274, string flow = "LeftToRight") =>
        (FormattableString.Invariant($"""
            gesture=1; resize-direction=Auto; resize-behavior=BasedOnAlignment; parent-level=0
            orientation=Vertical; horizontal-alignment=Stretch; vertical-alignment=Stretch
            flow={flow}; drag-increment=1; splitter-width=11; splitter-height=260
            """) + "\n" + Snapshot(1, 1, 330, 330) +
            "\nphase=starting; delta=<not supplied>; cumulative=<not supplied>\n" + Snapshot(1, 1, 330, 330) +
            "\nphase=started; delta=<not supplied>; cumulative=0,0\n" + Snapshot(1, 1, 330, 330) +
            FormattableString.Invariant($"\nphase=delta; delta={cumulative:R},0; cumulative={cumulative:R},0\n") +
            Snapshot(left, right, 330, 330) +
            FormattableString.Invariant($"\nphase=completed; delta=<not supplied>; cumulative={cumulative:R},0\n") +
            Snapshot(left, right, left, right)).Replace("\r\n", "\n");

    private static string Snapshot(double leftWeight, double rightWeight, double leftActual, double rightActual) =>
        FormattableString.Invariant($"""
            parent-is-advertised=True; panes-associated=True; definitions-unchanged=True
            splitter-column=1; splitter-row=0; left-column=0; left-row=0; right-column=2; right-row=0
            grid-width=671; grid-height=260; left-pane-width={leftActual:R}; right-pane-width={rightActual:R}
            row-unit=Star; row-value=1; row-actual=260
            column-0-unit=Star; column-0-value={leftWeight:R}; column-0-actual={leftActual:R}; column-0-min=0; column-0-max=Infinity
            column-1-unit=Pixel; column-1-value=11; column-1-actual=11; column-1-min=0; column-1-max=Infinity
            column-2-unit=Star; column-2-value={rightWeight:R}; column-2-actual={rightActual:R}; column-2-min=0; column-2-max=Infinity
            """);
}
