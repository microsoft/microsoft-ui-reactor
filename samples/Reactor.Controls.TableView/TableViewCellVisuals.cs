using System;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using DataTemplate = Microsoft.UI.Xaml.DataTemplate;

namespace Reactor.Controls;

/// <summary>
/// Visual style for a <see cref="TableColumn"/> cell. Lets a code-only Reactor consumer render the
/// native gallery's signature cell visuals — colored Department pills, Status chips, and stoplight
/// Salary tints — through the first-class <c>TableView(...)</c> control, with no XAML files.
/// </summary>
public enum CellStyle
{
    /// <summary>Plain text cell (a native <c>TableViewTextColumn</c>).</summary>
    Text,
    /// <summary>A rounded pill with a colored dot + label, tinted by the value (e.g. Department).</summary>
    Pill,
    /// <summary>A rounded chip tinted by a boolean/status value (e.g. Active / Inactive).</summary>
    Chip,
    /// <summary>A full-cell stoplight tint by a numeric value (e.g. Salary), with currency text.</summary>
    Tint,
}

/// <summary>
/// Builds the runtime <see cref="DataTemplate"/> for a styled <see cref="CellStyle"/> column and
/// registers the shared value-converter. Templates are parsed with <see cref="XamlReader"/> (classic
/// <c>{Binding}</c>, since <c>x:Bind</c> is unavailable in loose XAML); the converter must be in
/// <see cref="Application"/>.Resources before the template loads.
/// </summary>
internal static class TableViewCellTemplates
{
    internal const string ConverterKey = "ReactorTableViewCellVisualConverter";

    internal static void EnsureRegistered()
    {
        var app = Application.Current;
        if (app == null)
            return;
        var res = app.Resources;
        // Application.Resources may itself be a XamlControlsResources (Source set), which rejects local
        // values via the indexer ("Local values are not allowed in resource dictionary with Source set").
        // Register the converter through a code-created merged dictionary instead (same path the style
        // closure uses) so {StaticResource} resolves it during XamlReader.Load.
        if (res.ContainsKey(ConverterKey))
            return;
        foreach (var md in res.MergedDictionaries)
            if (md.ContainsKey(ConverterKey))
                return;
        var dict = new ResourceDictionary { [ConverterKey] = new TableViewCellVisualConverter() };
        res.MergedDictionaries.Add(dict);
    }

    internal static DataTemplate? Create(string propertyPath, CellStyle style)
    {
        EnsureRegistered();
        var path = XamlEscape(propertyPath);
        var xaml = style switch
        {
            CellStyle.Pill => Pill(path),
            CellStyle.Chip => Chip(path),
            CellStyle.Tint => Tint(path),
            _ => null,
        };
        return xaml == null ? null : (DataTemplate)XamlReader.Load(xaml);
    }

    private const string Ns =
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private static string Pill(string p) =>
        $"<DataTemplate {Ns}>" +
        "<Border CornerRadius=\"10\" Padding=\"10,3\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" " +
        $"Background=\"{{Binding {p}, Converter={{StaticResource {ConverterKey}}}, ConverterParameter=PillBg}}\">" +
        "<StackPanel Orientation=\"Horizontal\" Spacing=\"7\" VerticalAlignment=\"Center\">" +
        $"<Ellipse Width=\"8\" Height=\"8\" VerticalAlignment=\"Center\" Fill=\"{{Binding {p}, Converter={{StaticResource {ConverterKey}}}, ConverterParameter=PillDot}}\" />" +
        $"<TextBlock Text=\"{{Binding {p}}}\" VerticalAlignment=\"Center\" />" +
        "</StackPanel></Border></DataTemplate>";

    private static string Chip(string p) =>
        $"<DataTemplate {Ns}>" +
        "<Border CornerRadius=\"8\" Padding=\"10,3\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Center\" " +
        $"Background=\"{{Binding {p}, Converter={{StaticResource {ConverterKey}}}, ConverterParameter=ChipBg}}\">" +
        $"<TextBlock VerticalAlignment=\"Center\" Text=\"{{Binding {p}, Converter={{StaticResource {ConverterKey}}}, ConverterParameter=ChipText}}\" />" +
        "</Border></DataTemplate>";

    private static string Tint(string p) =>
        $"<DataTemplate {Ns}>" +
        "<Border CornerRadius=\"4\" Padding=\"10,3\" HorizontalAlignment=\"Stretch\" VerticalAlignment=\"Center\" " +
        $"Background=\"{{Binding {p}, Converter={{StaticResource {ConverterKey}}}, ConverterParameter=TintBg}}\">" +
        $"<TextBlock VerticalAlignment=\"Center\" Text=\"{{Binding {p}, Converter={{StaticResource {ConverterKey}}}, ConverterParameter=TintText}}\" />" +
        "</Border></DataTemplate>";

    private static string XamlEscape(string v) =>
        v.Replace("&", "&amp;", StringComparison.Ordinal)
         .Replace("\"", "&quot;", StringComparison.Ordinal)
         .Replace("<", "&lt;", StringComparison.Ordinal)
         .Replace(">", "&gt;", StringComparison.Ordinal);
}

/// <summary>
/// Maps a cell value to the brush/text for a styled cell. Colors mirror the native TableViewSamples
/// Showcase exactly (Department hues, stoplight Salary tiers, Active/Inactive chip).
/// </summary>
public sealed partial class TableViewCellVisualConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        (parameter as string) switch
        {
            "PillBg" => Brush(DeptHex(Str(value), translucent: true)),
            "PillDot" => Brush(DeptHex(Str(value), translucent: false)),
            "ChipBg" => Brush(IsTrue(value) ? "#5916A34A" : "#5964748B"),
            "ChipText" => IsTrue(value) ? "Active" : "Inactive",
            "TintBg" => Brush(SalaryTint(ToDouble(value))),
            "TintText" => ToDouble(value).ToString("C0", CultureInfo.CurrentCulture),
            _ => Brush("#00000000"),
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static string Str(object v) => v?.ToString()?.Trim() ?? "";

    private static bool IsTrue(object v) =>
        v is bool b ? b : bool.TryParse(Str(v), out var p) && p;

    private static double ToDouble(object v) =>
        v is IConvertible c ? SafeToDouble(c)
        : double.TryParse(Str(v), NumberStyles.Any, CultureInfo.CurrentCulture, out var d) ? d : 0;

    private static double SafeToDouble(IConvertible c)
    {
        try { return c.ToDouble(CultureInfo.CurrentCulture); } catch { return 0; }
    }

    // Department hues — exact Showcase map (translucent #4D pill bg, full-opacity #FF dot).
    private static string DeptHex(string dept, bool translucent)
    {
        var a = translucent ? "4D" : "FF";
        return dept.ToLowerInvariant() switch
        {
            "engineering" => "#" + a + "0078D4",
            "sales" => "#" + a + "14B8A6",
            "marketing" => "#" + a + "A855F7",
            "hr" or "human resources" => "#" + a + "F59E0B",
            "operations" => "#" + a + "EF4444",
            "design" => "#" + a + "EC4899",
            "product" => "#" + a + "0EA5E9",
            "finance" => "#" + a + "22C55E",
            _ => "#" + a + "64748B",
        };
    }

    // Stoplight salary tiers — exact Showcase thresholds.
    private static string SalaryTint(double salary) =>
        salary >= 100_000 ? "#4D16A34A" : salary >= 60_000 ? "#4DF59E0B" : "#4DDC2626";

    private static SolidColorBrush Brush(string hex)
    {
        hex = hex.TrimStart('#');
        byte H(int i) => byte.Parse(hex.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new SolidColorBrush(Color.FromArgb(H(0), H(2), H(4), H(6)));
    }
}
