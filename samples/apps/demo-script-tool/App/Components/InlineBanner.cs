using static Microsoft.UI.Reactor.Factories;

namespace DemoScriptTool.App.Components;

/// <summary>
/// Sticky inline banner for auth and parse failures (spec §Error Surfacing).
/// Uses shape (icon) + text — never colour alone — so the message is
/// distinguishable in HighContrast / NightSky.
/// </summary>
public static class InlineBanner
{
    public static Element Render(string message, BannerKind kind = BannerKind.Error) =>
        Border(
            HStack(12,
                TextBlock(SymbolFor(kind)).FontSize(16).VAlign(VerticalAlignment.Center),
                TextBlock(message)
                    .Foreground(Theme.PrimaryText)
                    .VAlign(VerticalAlignment.Center)))
        .Padding(horizontal: 16, vertical: 12)
        .CornerRadius(4)
        .Background(BackgroundFor(kind))
        .WithBorder(BorderFor(kind), 1)
        .AutomationName(message)
        .HeadingLevel(Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);

    static string SymbolFor(BannerKind kind) => kind switch
    {
        BannerKind.Error => "✕",
        BannerKind.Warning => "⚠",
        BannerKind.Info => "ⓘ",
        _ => "ⓘ",
    };

    static ThemeRef BackgroundFor(BannerKind kind) => kind switch
    {
        BannerKind.Error => Theme.SystemCriticalBackground,
        BannerKind.Warning => Theme.SystemCautionBackground,
        BannerKind.Info => Theme.SystemNeutralBackground,
        _ => Theme.SystemNeutralBackground,
    };

    static ThemeRef BorderFor(BannerKind kind) => kind switch
    {
        BannerKind.Error => Theme.SystemCritical,
        BannerKind.Warning => Theme.SystemCaution,
        BannerKind.Info => Theme.SystemNeutral,
        _ => Theme.SystemNeutral,
    };
}

public enum BannerKind { Info, Warning, Error }
