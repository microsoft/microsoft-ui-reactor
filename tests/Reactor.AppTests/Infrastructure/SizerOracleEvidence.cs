using System.Globalization;
using System.Text.Json;

namespace Microsoft.UI.Reactor.AppTests.Infrastructure;

internal sealed record DragEvidence(UiRect SourceBounds, double Dpi, int ToX, int ToY, string Json);

internal static class SizerOracleEvidence
{
    internal static void ValidateDrag(DragEvidence drag, long hostHwnd, int direction, string source, bool horizontal)
    {
        Require(double.IsFinite(drag.Dpi) && drag.Dpi > 0 &&
            drag.SourceBounds.Width > 0 && drag.SourceBounds.Height > 0, "Invalid input geometry.");
        using var document = JsonDocument.Parse(drag.Json);
        var root = document.RootElement;
        Require(root.TryGetProperty("from", out var from) && from.GetString() == source &&
            root.TryGetProperty("to", out var to) && to.GetString() == $"{drag.ToX},{drag.ToY}" &&
            root.TryGetProperty("hwnd", out var hwnd) && hwnd.TryGetInt64(out var actualHwnd) &&
            hostHwnd != 0 && actualHwnd == hostHwnd &&
            root.TryGetProperty("button", out var button) && button.GetString() == "left",
            "Missing or incorrect successful CLI gesture identity.");
        Require(root.TryGetProperty("fromX", out var fromX) && fromX.TryGetInt32(out var x) &&
            root.TryGetProperty("fromY", out var fromY) && fromY.TryGetInt32(out var y) &&
            root.TryGetProperty("toX", out var toX) && toX.TryGetInt32(out var tx) && tx == drag.ToX &&
            root.TryGetProperty("toY", out var toY) && toY.TryGetInt32(out var ty) && ty == drag.ToY &&
            (horizontal ? ty == y && Math.Sign((long)tx - x) == direction : tx == x && Math.Sign((long)ty - y) == direction),
            "Successful CLI coordinates do not describe the intended gesture.");
    }

    internal static Dictionary<string, string> Fields(string text)
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

    internal static string Value(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : throw new InvalidDataException($"Missing native field '{key}'.");

    internal static double Number(Dictionary<string, string> fields, string key, bool allowPositiveInfinity = false)
    {
        Require(double.TryParse(Value(fields, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            (double.IsFinite(value) || allowPositiveInfinity && double.IsPositiveInfinity(value)), $"Invalid native number '{key}'.");
        return value;
    }

    internal static double AxisX(string point) => Axis(point, 0);
    internal static double AxisY(string point) => Axis(point, 1);

    private static double Axis(string point, int index)
    {
        var axes = point.Split(',');
        Require(axes.Length == 2 &&
            double.TryParse(axes[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.IsFinite(x) &&
            double.TryParse(axes[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && double.IsFinite(y),
            "Missing or invalid native translation.");
        return double.Parse(axes[index], CultureInfo.InvariantCulture);
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
