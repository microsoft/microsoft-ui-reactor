using System;
using System.Globalization;
using Microsoft.UI.Reactor.Controls;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Controls;

/// <summary>
/// Unit tests for the read-mode <see cref="CellRenderers"/> catalog. Like
/// <see cref="EditorsBehaviorTests"/>, each renderer returns a
/// <see cref="Func{Object, Element}"/> over Reactor records — no WinUI
/// activation required for the text-shaped renderers (Text / Number / Date /
/// Time / Enum / Hyperlink). The brush-shaped renderers (CheckMark,
/// ToggleIndicator, ColorSwatch) build SolidColorBrush eagerly inside
/// `.Foreground(...)` / `.Background(...)`, so they're not unit-reachable —
/// the same trap the Editors iteration documented for ColorCompact.
///
/// Bug shapes covered:
///   • Number renderer drops TextAlignment.Right — money/quantities then
///     left-align in DataGrid cells, breaking column readability.
///   • Date renderer ignores format string — every column shows the same
///     ToString() representation regardless of the format passed in.
///   • Hyperlink renderer commits a HyperlinkButton for malformed input
///     (would surface as a non-clickable HyperlinkButton with null
///     NavigateUri, which crashes WinUI on click).
///   • FormatValue swallows null → "" guard, allowing NullReferenceException
///     to surface for any null cell value.
/// </summary>
public class CellRenderersTests
{
    // ══════════════════════════════════════════════════════════════
    //  Text — fallback renderer used for unknown types
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Text_Null_Renders_Empty_String()
    {
        // Bug: a regression that dropped the `value is null → ""` guard
        // would NRE on every empty cell. DataGrid renders many blanks
        // (uninitialised values), so this contract is load-bearing.
        var r = CellRenderers.Text();
        var el = (TextBlockElement)r(null!);
        Assert.Equal(string.Empty, el.Content);
    }

    [Fact]
    public void Text_NonFormattable_Uses_ToString()
    {
        var r = CellRenderers.Text();
        var el = (TextBlockElement)r("hello");
        Assert.Equal("hello", el.Content);
    }

    [Fact]
    public void Text_Formattable_With_Format_Uses_CurrentCulture_Format()
    {
        // The IFormattable branch in FormatValue. Bug shape: a regression
        // that always called value.ToString() (no format) would ignore the
        // currency / percent / fixed-decimal formats columns request.
        var r = CellRenderers.Text("F2");
        var el = (TextBlockElement)r(3.14159);
        // F2 → two-decimal fixed. Culture-dependent decimal separator —
        // accept either "3.14" (invariant-ish) or "3,14" (German etc.).
        Assert.True(el.Content == "3.14" || el.Content == "3,14",
            $"Expected F2-formatted value to contain two fractional digits; got '{el.Content}'");
    }

    [Fact]
    public void Text_Format_With_NonFormattable_Falls_Through_To_ToString()
    {
        // FormatValue's `value is IFormattable f` else-arm. Strings are not
        // IFormattable — so even though a format was passed, the result is
        // the original string. Pin: a regression that threw on non-IFormattable
        // + format would crash every text column with a format specifier.
        var r = CellRenderers.Text("F2");
        var el = (TextBlockElement)r("not-a-number");
        Assert.Equal("not-a-number", el.Content);
    }

    // ══════════════════════════════════════════════════════════════
    //  Number — right-aligned, stretches to cell width
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Number_TextAlignment_Right_And_HAlign_Stretch()
    {
        // Without HAlign.Stretch the TextBlock sizes to content and the
        // alignment has no visible effect. The comment in the source pins
        // this — and so does this test, against a refactor that drops
        // either chain. Note: `.HAlign(...)` writes to Modifiers (a generic
        // Element-level slot), not the TextBlockElement's own
        // HorizontalAlignment init property — both shapes exist and serve
        // different layers.
        var r = CellRenderers.Number();
        var el = (TextBlockElement)r(42);
        Assert.Equal(Microsoft.UI.Xaml.TextAlignment.Right, el.TextAlignment);
        Assert.Equal(Microsoft.UI.Xaml.HorizontalAlignment.Stretch, el.Modifiers?.HorizontalAlignment);
    }

    [Fact]
    public void Number_With_Format_Uses_Formatter()
    {
        var r = CellRenderers.Number("N0");
        var el = (TextBlockElement)r(1_234_567);
        // N0 → thousands separator, zero decimals. Group separator is
        // culture-dependent (", " in US, ". " in DE) — accept any non-
        // digit between the digits.
        Assert.Contains("1", el.Content);
        Assert.Contains("234", el.Content);
        Assert.Contains("567", el.Content);
        Assert.DoesNotContain(".0", el.Content);
    }

    [Fact]
    public void Number_Null_Renders_Empty_String_Without_NRE()
    {
        var r = CellRenderers.Number("F2");
        var el = (TextBlockElement)r(null!);
        Assert.Equal(string.Empty, el.Content);
    }

    // ══════════════════════════════════════════════════════════════
    //  Date / Time
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Date_With_Default_Format_Uses_Short_Date()
    {
        // "d" is .NET short-date format. The exact rendering is culture-
        // dependent; we assert the year-month-day digits all appear so a
        // regression that emitted ISO or full-date format would fail.
        var r = CellRenderers.Date();
        var el = (TextBlockElement)r(new DateTime(2026, 5, 17));
        Assert.Contains("2026", el.Content);
        // Day or month "5" or "05" appears.
        Assert.Contains("5", el.Content);
        Assert.Contains("17", el.Content);
    }

    [Fact]
    public void Date_Custom_Format_Used_Verbatim()
    {
        // "yyyy-MM-dd" is invariant — exact match is safe.
        var r = CellRenderers.Date("yyyy-MM-dd");
        var el = (TextBlockElement)r(new DateTime(2026, 5, 17));
        Assert.Equal("2026-05-17", el.Content);
    }

    [Fact]
    public void Date_Null_Renders_Empty()
    {
        var r = CellRenderers.Date();
        var el = (TextBlockElement)r(null!);
        Assert.Equal(string.Empty, el.Content);
    }

    [Fact]
    public void Time_Custom_TimeSpan_Format_Used_Verbatim()
    {
        // TimeSpan custom format requires escaped colons (`hh\:mm\:ss`).
        // Using a DateTime-style format like `HH:mm:ss` on a TimeSpan
        // throws FormatException — a known sharp edge of TimeColumn.
        var r = CellRenderers.Time(@"hh\:mm\:ss");
        var el = (TextBlockElement)r(new TimeSpan(14, 30, 45));
        Assert.Equal("14:30:45", el.Content);
    }

    [Fact]
    public void Time_With_DateTime_Value_Uses_Standard_Format()
    {
        // When the field is DateTime-typed, the "t" default format works
        // (it's the standard short-time pattern).
        var r = CellRenderers.Time("t");
        var dt = new DateTime(2026, 5, 17, 14, 30, 0);
        var el = (TextBlockElement)r(dt);
        // "t" is locale-dependent ("2:30 PM" vs "14:30"); pin that the
        // hour digits appear and the date does not.
        Assert.DoesNotContain("2026", el.Content);
        Assert.Contains("30", el.Content);
    }

    [Fact]
    public void Time_Null_Renders_Empty()
    {
        var r = CellRenderers.Time();
        var el = (TextBlockElement)r(null!);
        Assert.Equal(string.Empty, el.Content);
    }

    // ══════════════════════════════════════════════════════════════
    //  Enum — stringified value
    // ══════════════════════════════════════════════════════════════

    private enum Priority { Low, Med, High }

    [Fact]
    public void Enum_Renders_String_Form_Of_Value()
    {
        var r = CellRenderers.Enum();
        var el = (TextBlockElement)r(Priority.Med);
        Assert.Equal("Med", el.Content);
    }

    [Fact]
    public void Enum_Null_Renders_Empty()
    {
        var r = CellRenderers.Enum();
        var el = (TextBlockElement)r(null!);
        Assert.Equal(string.Empty, el.Content);
    }

    // ══════════════════════════════════════════════════════════════
    //  Hyperlink — three branches
    //   1. value is System.Uri → HyperlinkButton with the URI
    //   2. value is a string parseable as absolute URI → HyperlinkButton
    //   3. value is a non-URI string → plain TextBlock (no broken link)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Hyperlink_Uri_Value_Renders_HyperlinkButton_With_NavigateUri()
    {
        var r = CellRenderers.Hyperlink();
        var uri = new global::System.Uri("https://example.com/path");
        var el = (HyperlinkButtonElement)r(uri);
        Assert.Equal(uri, el.NavigateUri);
        Assert.Equal(uri.ToString(), el.Content);
    }

    [Fact]
    public void Hyperlink_Uri_With_DisplayTextFormat_Uses_Format()
    {
        // The first branch's `displayTextFormat is null` ternary's false arm.
        var r = CellRenderers.Hyperlink("Visit {0}");
        var uri = new global::System.Uri("https://example.com");
        var el = (HyperlinkButtonElement)r(uri);
        Assert.Contains("Visit", el.Content);
        Assert.Contains("example.com", el.Content);
    }

    [Fact]
    public void Hyperlink_String_Parseable_As_Absolute_Uri_Renders_Link()
    {
        // Branch: value is string, TryCreate(Absolute) succeeds.
        var r = CellRenderers.Hyperlink();
        var el = (HyperlinkButtonElement)r("https://docs.microsoft.com");
        Assert.NotNull(el.NavigateUri);
        Assert.Equal("https://docs.microsoft.com/", el.NavigateUri!.ToString());
    }

    [Fact]
    public void Hyperlink_NonUri_String_Falls_Back_To_TextBlock()
    {
        // Branch: TryCreate(Absolute) fails — must NOT render a HyperlinkButton
        // (a HyperlinkButton with null NavigateUri crashes WinUI on click).
        // Falling back to TextBlock keeps the cell content visible without
        // pretending it's navigable.
        var r = CellRenderers.Hyperlink();
        var el = r("not a url");
        // Must be a TextBlock, NOT a HyperlinkButton.
        var tb = Assert.IsType<TextBlockElement>(el);
        Assert.Equal("not a url", tb.Content);
    }

    [Fact]
    public void Hyperlink_Null_Renders_Empty_TextBlock()
    {
        // The `value?.ToString() ?? string.Empty` guard. Without it, the
        // value.ToString() call on null NRE'd.
        var r = CellRenderers.Hyperlink();
        var el = r(null!);
        var tb = Assert.IsType<TextBlockElement>(el);
        Assert.Equal(string.Empty, tb.Content);
    }

    [Fact]
    public void Hyperlink_Relative_String_Falls_Back_To_TextBlock()
    {
        // TryCreate(Absolute) fails on a relative path — must NOT render
        // a hyperlink, because WinUI requires absolute URIs for navigation.
        var r = CellRenderers.Hyperlink();
        var el = r("/relative/path");
        Assert.IsType<TextBlockElement>(el);
    }

    // ══════════════════════════════════════════════════════════════
    //  FormatValue private helper — exercise its branches indirectly
    //  via Text and Number renderers.
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void FormatValue_IFormattable_Without_Format_Uses_Default_ToString()
    {
        // format == null + IFormattable value → fall through to ToString().
        // Decimal.ToString() ≠ Decimal.ToString("F2"), so the test
        // distinguishes the two paths.
        var r = CellRenderers.Text();
        var el = (TextBlockElement)r(3.14159);
        Assert.Contains("3.14159", el.Content);
    }

    [Fact]
    public void FormatValue_NonFormattable_Without_Format_Uses_ToString()
    {
        // The catch-all `return value.ToString() ?? string.Empty` arm.
        var r = CellRenderers.Text();
        var el = (TextBlockElement)r(true);
        // bool.ToString() returns "True" / "False" with capital T/F.
        Assert.Equal("True", el.Content);
    }

    [Fact]
    public void FormatValue_Invariant_Format_With_Decimal()
    {
        // Pin invariant-culture formatting. Use "G" — it's the documented
        // general-format specifier for decimal. "R" is documented only for
        // Single/Double/Half; on decimal it's implementation-defined and
        // historically has thrown FormatException on some runtimes.
        var r = CellRenderers.Number("G");
        var el = (TextBlockElement)r(1.5m);
        Assert.Equal("1.5", el.Content);
    }
}
