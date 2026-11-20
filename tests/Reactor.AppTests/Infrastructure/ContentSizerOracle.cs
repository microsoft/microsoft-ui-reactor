using static Microsoft.UI.Reactor.AppTests.Infrastructure.SizerOracleEvidence;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

internal static class ContentSizerOracle
{
    internal static double ExpectedHeight(
        string trace, int expectedGesture, int direction,
        (double Height, double ActualHeight, double Width) before,
        DragEvidence drag, long hostHwnd, UiRect siblingAfter)
    {
        Require(direction is -1 or 1, "A nonzero movement direction is required.");
        Require(double.IsFinite(before.Height) && double.IsFinite(before.ActualHeight) &&
            double.IsFinite(before.Width) && before.ActualHeight > 0 && before.Width > 0,
            "Missing initial advertised measurements.");
        ValidateDrag(drag, hostHwnd, direction, "ContentSizer.Sizer", horizontal: false);
        Require(siblingAfter.Width == drag.SourceBounds.Width && siblingAfter.Height == drag.SourceBounds.Height,
            "The sizer sibling changed size.");

        var sections = trace.Replace("\r\n", "\n").Split("\nphase=", StringSplitOptions.None);
        Require(sections.Length >= 5, "A complete starting/started/delta/completed trace is required.");
        var header = Fields(sections[0]);
        Require(int.TryParse(Value(header, "gesture"), out var gesture) && gesture == expectedGesture,
            "Native manipulation trace is stale or belongs to another gesture.");
        Require(Value(header, "orientation") == "Horizontal" && Value(header, "inverted") == "False",
            "Unexpected ContentSizer orientation or inversion.");
        Require(Value(header, "target-is-advertised") == "True", "The native target is not the advertised Border.");
        Require(Number(header, "initial-target-height") == before.Height &&
            Number(header, "initial-advertised-height") == before.Height &&
            Number(header, "initial-target-actual-height") == before.ActualHeight &&
            Number(header, "initial-advertised-actual-height") == before.ActualHeight,
            "Native start sizes do not match independently observed advertised sizes.");

        var increment = Number(header, "drag-increment");
        var minimum = Number(header, "minimum-height");
        var maximum = Number(header, "maximum-height", allowPositiveInfinity: true);
        var siblingHeight = Number(header, "sibling-height");
        var siblingWidth = Number(header, "sibling-width");
        Require(siblingHeight > 0 && siblingWidth > 0, "Missing native sibling dimensions.");
        double expected = before.Height;
        double lastCumulative = 0;
        for (int i = 1; i < sections.Length; i++)
        {
            int separator = sections[i].IndexOf(';');
            Require(separator > 0, "Malformed native event.");
            var phase = sections[i][..separator];
            var fields = Fields(sections[i][(separator + 1)..]);
            var expectedPhase = i == 1 ? "starting" : i == 2 ? "started" :
                i == sections.Length - 1 ? "completed" : "delta";
            Require(phase == expectedPhase, "Native lifecycle is missing, reordered or incomplete.");
            if (phase == "delta")
            {
                _ = AxisY(Value(fields, "delta"));
                lastCumulative = AxisY(Value(fields, "cumulative"));
                expected = CalculateHeight(before.ActualHeight, lastCumulative, increment,
                    minimum, maximum, siblingHeight);
            }
            else if (phase == "started")
            {
                _ = AxisY(Value(fields, "cumulative"));
            }
            else if (phase == "completed")
            {
                Require(Math.Sign(AxisY(Value(fields, "cumulative"))) == direction,
                    "Completed native movement has the wrong direction or is zero.");
            }

            Require(Number(fields, "target-height") == expected &&
                Number(fields, "advertised-height") == expected, "Native resize did not update the advertised target exactly.");
            Require(Number(fields, "target-actual-height") == Number(fields, "advertised-actual-height"),
                "Target and advertised layout measurements disagree.");
            Require(Number(fields, "sibling-height") == siblingHeight && Number(fields, "sibling-width") == siblingWidth,
                "The native sibling resized instead of remaining unchanged.");
        }

        Require(Math.Sign(lastCumulative) == direction && Math.Sign(expected - before.ActualHeight) == direction,
            "No nonzero applied native resize in the intended direction.");
        return expected;
    }

    internal static double CalculateHeight(double initial, double cumulative, double increment,
        double minimum, double maximum, double siblingHeight)
    {
        Require(double.IsFinite(initial) && initial > 0 && double.IsFinite(cumulative) &&
            double.IsFinite(increment) && increment > 0 &&
            double.IsFinite(minimum) && minimum >= 0 &&
            (double.IsFinite(maximum) || double.IsPositiveInfinity(maximum)) && maximum >= minimum &&
            double.IsFinite(siblingHeight) && siblingHeight > 0, "Invalid native sizing inputs.");
        // Toolkit SizerBase truncates each cumulative delta; ContentSizer rejects, not
        // clamps, candidates outside the target bounds or at/below the sizer's height.
        double result = initial + Math.Truncate(cumulative / increment) * increment;
        Require(double.IsFinite(result) && result >= minimum && result <= maximum && result > siblingHeight,
            "Observed delta violates the native ContentSizer bounds.");
        return result;
    }

}
