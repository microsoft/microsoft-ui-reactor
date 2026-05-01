using Microsoft.UI.Reactor.Charting.D3;
using Microsoft.UI.Reactor.Charting;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Charting.D3Charts;

namespace NetPulse;

/// <summary>
/// Cached SolidColorBrush instances for the entire NetPulse app.
/// Lazy-initialized because SolidColorBrush (a DependencyObject) requires
/// the WinUI thread to exist before construction.
/// </summary>
static class Brushes
{
    // ── Gray shades ────────────────────────────────────────────────
    private static SolidColorBrush? _gray40, _gray60, _gray80, _gray100,
        _gray120, _gray128_40, _gray140, _gray160, _gray180, _gray200,
        _gray220, _gray230, _gray245, _gray100_180;

    public static SolidColorBrush Gray40 => _gray40 ??= Gray(40);
    public static SolidColorBrush Gray60 => _gray60 ??= Gray(60);
    public static SolidColorBrush Gray80 => _gray80 ??= Gray(80);
    public static SolidColorBrush Gray100 => _gray100 ??= Gray(100);
    public static SolidColorBrush Gray120 => _gray120 ??= Gray(120);
    public static SolidColorBrush Gray128A40 => _gray128_40 ??= Gray(128, 40);
    public static SolidColorBrush Gray140 => _gray140 ??= Gray(140);
    public static SolidColorBrush Gray160 => _gray160 ??= Gray(160);
    public static SolidColorBrush Gray180 => _gray180 ??= Gray(180);
    public static SolidColorBrush Gray200 => _gray200 ??= Gray(200);
    public static SolidColorBrush Gray220 => _gray220 ??= Gray(220);
    public static SolidColorBrush Gray230 => _gray230 ??= Gray(230);
    public static SolidColorBrush Gray245 => _gray245 ??= Gray(245);
    public static SolidColorBrush Gray100A180 => _gray100_180 ??= Gray(100, 180);

    // ── Named hex colors ───────────────────────────────────────────
    private static SolidColorBrush? _green, _orange, _red, _barBg,
        _purple, _blue, _grayMed, _darkOrange, _darkRed, _darkGray, _slate;

    public static SolidColorBrush Green => _green ??= Brush("#2ecc71");
    public static SolidColorBrush Orange => _orange ??= Brush("#f39c12");
    public static SolidColorBrush Red => _red ??= Brush("#e74c3c");
    public static SolidColorBrush BarBg => _barBg ??= Brush("#e0e0e0");
    public static SolidColorBrush Purple => _purple ??= Brush("#9b59b6");
    public static SolidColorBrush Blue => _blue ??= Brush("#3498db");
    public static SolidColorBrush GrayMed => _grayMed ??= Brush("#95a5a6");
    public static SolidColorBrush DarkOrange => _darkOrange ??= Brush("#e67e22");
    public static SolidColorBrush DarkRed => _darkRed ??= Brush("#c0392b");
    public static SolidColorBrush DarkGray => _darkGray ??= Brush("#7f8c8d");
    public static SolidColorBrush Slate => _slate ??= Brush("#34495e");

    // ── Palette brushes (D3 Category10 colors at various opacities) ─
    private static SolidColorBrush?[]? _palette;
    private static SolidColorBrush?[]? _palette085;
    private static SolidColorBrush?[]? _palette080;
    private static SolidColorBrush?[]? _palette015;
    private static SolidColorBrush?[]? _palette060;
    private static SolidColorBrush?[]? _palette070;

    private static SolidColorBrush GetPaletteBrush(ref SolidColorBrush?[]? cache, int index, double opacity)
    {
        cache ??= new SolidColorBrush[Palette.Count];
        int i = index % Palette.Count;
        return cache[i] ??= Brush(Palette[i], opacity);
    }

    public static SolidColorBrush PaletteFull(int index) => GetPaletteBrush(ref _palette, index, 1.0);
    public static SolidColorBrush Palette085(int index) => GetPaletteBrush(ref _palette085, index, 0.85);
    public static SolidColorBrush Palette080(int index) => GetPaletteBrush(ref _palette080, index, 0.80);
    public static SolidColorBrush Palette015(int index) => GetPaletteBrush(ref _palette015, index, 0.15);
    public static SolidColorBrush Palette060(int index) => GetPaletteBrush(ref _palette060, index, 0.60);
    public static SolidColorBrush Palette070(int index) => GetPaletteBrush(ref _palette070, index, 0.70);
}
