using static Microsoft.UI.Reactor.AppTests.Infrastructure.SizerOracleEvidence;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

internal static class GridSplitterOracle
{
    internal static (double Left, double Right) ExpectedWidths(
        string trace, int expectedGesture, int direction, double[] before, DragEvidence drag, long hostHwnd)
    {
        Require(direction is -1 or 1, "A nonzero movement direction is required.");
        Require(before.Length == 7 && before.All(n => double.IsFinite(n) && n > 0),
            "Missing advertised grid and pane measurements.");
        ValidateDrag(drag, hostHwnd, direction, "ToolkitInput.GridSplitter", horizontal: true);
        var sections = trace.Replace("\r\n", "\n").Split("\nphase=", StringSplitOptions.None);
        Require(sections.Length >= 5, "A complete GridSplitter native lifecycle is required.");
        var header = Fields(sections[0]);
        Require(int.TryParse(Value(header, "gesture"), out var gesture) && gesture == expectedGesture,
            "GridSplitter trace is stale or belongs to another gesture.");
        Require(Value(header, "resize-direction") == "Auto" &&
            Value(header, "resize-behavior") == "BasedOnAlignment" &&
            Value(header, "horizontal-alignment") == "Stretch" && Value(header, "vertical-alignment") == "Stretch" &&
            Value(header, "orientation") == "Vertical" && Number(header, "parent-level") == 0 &&
            Number(header, "splitter-width") <= Number(header, "splitter-height"),
            "Fixture no longer resolves to Columns/PreviousAndNext.");
        string flow = Value(header, "flow");
        Require(flow is "LeftToRight" or "RightToLeft", "Missing flow direction.");
        bool rtl = flow == "RightToLeft";
        var increment = Number(header, "drag-increment");
        var splitterWidth = Number(header, "splitter-width");
        var minimumLeft = Number(header, "column-0-min");
        var maximumLeft = Number(header, "column-0-max", allowPositiveInfinity: true);
        var minimumRight = Number(header, "column-2-min");
        var maximumRight = Number(header, "column-2-max", allowPositiveInfinity: true);
        Require(Number(header, "column-0-actual") == before[0] &&
            Number(header, "column-1-actual") == before[1] && Number(header, "column-2-actual") == before[2] &&
            Number(header, "left-pane-width") == before[3] && Number(header, "right-pane-width") == before[4] &&
            Number(header, "grid-width") == before[5] && Number(header, "grid-height") == before[6],
            "Native start sizes differ from independent advertised observations.");
        Require(Math.Abs(before[0] - before[3]) <= 2 && Math.Abs(before[2] - before[4]) <= 2 &&
            Math.Abs(before[0] + before[1] + before[2] - before[5]) <= 2,
            "Advertised panes/grid do not agree with the column layout.");
        Require(Number(header, "column-0-value") == 1 && Number(header, "column-2-value") == 1,
            "Expected the fixture's initial 1*/11px/1* columns.");
        ValidateStructure(header);

        double leftWeight = 1, rightWeight = 1, lastCumulative = 0;
        for (int i = 1; i < sections.Length; i++)
        {
            int separator = sections[i].IndexOf(';');
            Require(separator > 0, "Malformed GridSplitter event.");
            string phase = sections[i][..separator];
            var fields = Fields(sections[i][(separator + 1)..]);
            string expectedPhase = i == 1 ? "starting" : i == 2 ? "started" :
                i == sections.Length - 1 ? "completed" : "delta";
            Require(phase == expectedPhase, "GridSplitter lifecycle is missing, reordered or incomplete.");
            ValidateStructure(fields);
            if (phase == "delta")
            {
                _ = AxisX(Value(fields, "delta"));
                lastCumulative = AxisX(Value(fields, "cumulative"));
                (leftWeight, rightWeight) = CalculateStarWeights(before[0], before[2], lastCumulative,
                    increment, rtl, minimumLeft, maximumLeft, minimumRight, maximumRight, splitterWidth);
            }
            else if (phase == "started")
            {
                _ = AxisX(Value(fields, "cumulative"));
            }
            else if (phase == "completed")
            {
                Require(Math.Sign(AxisX(Value(fields, "cumulative"))) == direction,
                    "Completed GridSplitter movement is zero or reversed.");
            }

            Require(Number(fields, "column-0-value") == leftWeight && Number(fields, "column-2-value") == rightWeight,
                "The advertised definitions did not receive the exact native star weights.");
            Require(Number(fields, "grid-width") == before[5] && Number(fields, "grid-height") == before[6] &&
                Number(fields, "row-value") == Number(header, "row-value") &&
                Number(fields, "row-actual") == Number(header, "row-actual"),
                "Grid or row extent changed during a column-only gesture.");
            for (int column = 0; column < 3; column++)
                Require(Number(fields, $"column-{column}-min") == Number(header, $"column-{column}-min") &&
                    Number(fields, $"column-{column}-max", true) == Number(header, $"column-{column}-max", true),
                    "Column bounds changed during the gesture.");
        }

        int appliedDirection = rtl ? -direction : direction;
        Require(Math.Sign(lastCumulative) == direction && Math.Sign(leftWeight - before[0]) == appliedDirection &&
            Math.Sign(rightWeight - before[2]) == -appliedDirection, "No nonzero resize of both intended columns.");
        // The two weights sum to the original star allocation. The fixed middle
        // column/grid extent stay unchanged; settled geometry is checked separately.
        return (leftWeight, rightWeight);
    }

    internal static (double Left, double Right) CalculateStarWeights(double left, double right,
        double cumulative, double increment, bool rtl,
        double minimumLeft, double maximumLeft, double minimumRight, double maximumRight, double splitterWidth)
    {
        Require(double.IsFinite(left) && double.IsFinite(right) && double.IsFinite(cumulative) &&
            double.IsFinite(increment) && increment > 0 && double.IsFinite(splitterWidth) && splitterWidth > 0,
            "Invalid GridSplitter sizing inputs.");
        double change = Math.Truncate(cumulative / increment) * increment;
        if (rtl) change = -change;
        double nextLeft = left + change;
        double nextRight = right - change;
        Require(WithinBounds(nextLeft, minimumLeft, maximumLeft, splitterWidth) &&
            WithinBounds(nextRight, minimumRight, maximumRight, splitterWidth),
            "GridSplitter rejects the candidate pair before SetColumnWidth's defensive clamps.");
        return (nextLeft, nextRight);
    }

    private static bool WithinBounds(double value, double minimum, double maximum, double splitterWidth) =>
        double.IsFinite(value) && double.IsFinite(minimum) && minimum >= 0 &&
        (double.IsFinite(maximum) || double.IsPositiveInfinity(maximum)) && maximum >= minimum &&
        value >= minimum && value <= maximum && value > splitterWidth;

    private static void ValidateStructure(Dictionary<string, string> fields)
    {
        Require(Value(fields, "parent-is-advertised") == "True" && Value(fields, "panes-associated") == "True" &&
            Value(fields, "definitions-unchanged") == "True" && Number(fields, "splitter-column") == 1 &&
            Number(fields, "splitter-row") == 0 && Number(fields, "left-column") == 0 &&
            Number(fields, "right-column") == 2 && Number(fields, "left-row") == 0 && Number(fields, "right-row") == 0,
            "Wrong advertised grid, definitions or pane association.");
        Require(Value(fields, "column-0-unit") == "Star" && Value(fields, "column-2-unit") == "Star" &&
            Value(fields, "column-1-unit") == "Pixel" && Number(fields, "column-1-value") == 11 &&
            Value(fields, "row-unit") == "Star",
            "GridSplitter changed the column unit contract or fixed sibling.");
    }
}
