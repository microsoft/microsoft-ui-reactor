using System.Globalization;
using System.Text.Json;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

internal sealed record DragEvidence(UiRect SourceBounds, double Dpi, int ToX, int ToY, string Json);

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
        ValidateDrag(drag, hostHwnd, direction);
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

    private static void ValidateDrag(DragEvidence drag, long hostHwnd, int direction)
    {
        Require(double.IsFinite(drag.Dpi) && drag.Dpi > 0 &&
            drag.SourceBounds.Width > 0 && drag.SourceBounds.Height > 0, "Invalid input geometry.");
        using var document = JsonDocument.Parse(drag.Json);
        var root = document.RootElement;
        Require(root.TryGetProperty("from", out var from) && from.GetString() == "ContentSizer.Sizer" &&
            root.TryGetProperty("to", out var to) && to.GetString() == $"{drag.ToX},{drag.ToY}" &&
            root.TryGetProperty("hwnd", out var hwnd) && hwnd.TryGetInt64(out var actualHwnd) &&
            hostHwnd != 0 && actualHwnd == hostHwnd &&
            root.TryGetProperty("button", out var button) && button.GetString() == "left",
            "Missing or incorrect successful CLI gesture identity.");
        Require(root.TryGetProperty("fromX", out var fromX) && fromX.TryGetInt32(out var x) &&
            root.TryGetProperty("fromY", out var fromY) && fromY.TryGetInt32(out var y) &&
            root.TryGetProperty("toX", out var toX) && toX.TryGetInt32(out var tx) && tx == drag.ToX &&
            root.TryGetProperty("toY", out var toY) && toY.TryGetInt32(out var ty) && ty == drag.ToY &&
            tx == x && Math.Sign((long)ty - y) == direction,
            "Successful CLI coordinates do not describe the intended vertical gesture.");
    }

    private static Dictionary<string, string> Fields(string text)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in text.Split([';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = token.IndexOf('=');
            Require(equals > 0 && equals < token.Length - 1, $"Malformed native field '{token}'.");
            Require(fields.TryAdd(token[..equals], token[(equals + 1)..]), "Duplicate native field.");
        }
        return fields;
    }

    private static string Value(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : throw new InvalidDataException($"Missing native field '{key}'.");

    private static double Number(Dictionary<string, string> fields, string key, bool allowPositiveInfinity = false)
    {
        Require(double.TryParse(Value(fields, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            (double.IsFinite(value) || allowPositiveInfinity && double.IsPositiveInfinity(value)), $"Invalid native number '{key}'.");
        return value;
    }

    private static double AxisY(string point)
    {
        var axes = point.Split(',');
        Require(axes.Length == 2 &&
            double.TryParse(axes[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.IsFinite(x) &&
            double.TryParse(axes[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && double.IsFinite(y),
            "Missing or invalid native translation.");
        return double.Parse(axes[1], CultureInfo.InvariantCulture);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
