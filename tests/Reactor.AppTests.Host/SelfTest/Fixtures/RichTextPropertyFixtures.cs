using System;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;
using WinBlock = Microsoft.UI.Xaml.Documents.Block;
using WinHyperlink = Microsoft.UI.Xaml.Documents.Hyperlink;
using WinInlineUIContainer = Microsoft.UI.Xaml.Documents.InlineUIContainer;
using WinLineBreak = Microsoft.UI.Xaml.Documents.LineBreak;
using WinParagraph = Microsoft.UI.Xaml.Documents.Paragraph;
using WinRichTextBlock = Microsoft.UI.Xaml.Controls.RichTextBlock;
using WinRun = Microsoft.UI.Xaml.Documents.Run;
using WinTextElement = Microsoft.UI.Xaml.Documents.TextElement;
using WinUnderlineStyle = Microsoft.UI.Xaml.Documents.UnderlineStyle;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class RichTextPropertyFixtures
{
    private static readonly FontFamily s_consolas = WinRTCache.GetFontFamily("Consolas");

    internal class RichTextProps_Block_MountUpdateClear(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (styled, setStyled) = ctx.UseState(true);
                var block = RichTextBlock("root") with { IsTextSelectionEnabled = true };
                if (styled)
                {
                    block = block with
                    {
                        FontSize = 23,
                        FontFamily = s_consolas,
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        FontStyle = global::Windows.UI.Text.FontStyle.Italic,
                        FontStretch = global::Windows.UI.Text.FontStretch.Expanded,
                        Foreground = Brush(200, 20, 30),
                        MaxLines = 3,
                        LineHeight = 31,
                        TextAlignment = TextAlignment.Center,
                        HorizontalTextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.WordEllipsis,
                        CharacterSpacing = 120,
                        TextDecorations = global::Windows.UI.Text.TextDecorations.Underline,
                        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                        TextIndent = 11,
                        TextLineBounds = TextLineBounds.Tight,
                        TextReadingOrder = TextReadingOrder.DetectFromContent,
                        IsTextScaleFactorEnabled = false,
                        IsColorFontEnabled = false,
                        OpticalMarginAlignment = OpticalMarginAlignment.TrimSideBearings,
                        SelectionHighlightColor = Brush(10, 20, 200),
                        Padding = new Thickness(1, 2, 3, 4),
                    };
                }

                return VStack(
                    Button("ToggleRootProps", () => setStyled(!styled)),
                    block);
            });

            await Harness.Render();

            var rtb = H.FindControl<WinRichTextBlock>(_ => true);
            H.Check("RichTextProps_Block_Mounted", rtb is not null);
            if (rtb is null) return;

            H.Check("RichTextProps_Block_FontSize", IsClose(rtb.FontSize, 23));
            H.Check("RichTextProps_Block_FontFamily", rtb.FontFamily.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase));
            H.Check("RichTextProps_Block_FontWeight", rtb.FontWeight.Weight >= 700);
            H.Check("RichTextProps_Block_FontStyle", rtb.FontStyle == global::Windows.UI.Text.FontStyle.Italic);
            H.Check("RichTextProps_Block_FontStretch", rtb.FontStretch == global::Windows.UI.Text.FontStretch.Expanded);
            H.Check("RichTextProps_Block_Foreground", IsColor(rtb.Foreground, 200, 20, 30));
            H.Check("RichTextProps_Block_MaxLines", rtb.MaxLines == 3);
            H.Check("RichTextProps_Block_LineHeight", IsClose(rtb.LineHeight, 31));
            H.Check("RichTextProps_Block_TextAlignment", rtb.TextAlignment == TextAlignment.Center);
            H.Check("RichTextProps_Block_HorizontalTextAlignment", rtb.HorizontalTextAlignment == TextAlignment.Center);
            H.Check("RichTextProps_Block_TextTrimming", rtb.TextTrimming == TextTrimming.WordEllipsis);
            H.Check("RichTextProps_Block_CharacterSpacing", rtb.CharacterSpacing == 120);
            H.Check("RichTextProps_Block_TextDecorations", rtb.TextDecorations == global::Windows.UI.Text.TextDecorations.Underline);
            H.Check("RichTextProps_Block_LineStackingStrategy", rtb.LineStackingStrategy == LineStackingStrategy.BlockLineHeight);
            H.Check("RichTextProps_Block_TextIndent", IsClose(rtb.TextIndent, 11));
            H.Check("RichTextProps_Block_TextLineBounds", rtb.TextLineBounds == TextLineBounds.Tight);
            H.Check("RichTextProps_Block_TextReadingOrder", rtb.TextReadingOrder == TextReadingOrder.DetectFromContent);
            H.Check("RichTextProps_Block_IsTextScaleFactorEnabled", !rtb.IsTextScaleFactorEnabled);
            H.Check("RichTextProps_Block_IsColorFontEnabled", !rtb.IsColorFontEnabled);
            H.Check("RichTextProps_Block_OpticalMarginAlignment", rtb.OpticalMarginAlignment == OpticalMarginAlignment.TrimSideBearings);
            H.Check("RichTextProps_Block_SelectionHighlightColor", IsColor(rtb.SelectionHighlightColor, 10, 20, 200));
            H.Check("RichTextProps_Block_Padding", rtb.Padding == new Thickness(1, 2, 3, 4));

            H.ClickButton("ToggleRootProps");
            await Harness.Render();

            H.Check("RichTextProps_Block_IdentityPreserved", ReferenceEquals(rtb, H.FindControl<WinRichTextBlock>(_ => true)));
            H.Check("RichTextProps_Block_FontSizeCleared", IsUnset(rtb, WinRichTextBlock.FontSizeProperty));
            H.Check("RichTextProps_Block_FontFamilyCleared", IsUnset(rtb, WinRichTextBlock.FontFamilyProperty));
            H.Check("RichTextProps_Block_FontWeightCleared", IsUnset(rtb, WinRichTextBlock.FontWeightProperty));
            H.Check("RichTextProps_Block_FontStyleCleared", IsUnset(rtb, WinRichTextBlock.FontStyleProperty));
            H.Check("RichTextProps_Block_FontStretchCleared", IsUnset(rtb, WinRichTextBlock.FontStretchProperty));
            H.Check("RichTextProps_Block_ForegroundCleared", IsUnset(rtb, WinRichTextBlock.ForegroundProperty));
            H.Check("RichTextProps_Block_MaxLinesReset", rtb.MaxLines == 0);
            H.Check("RichTextProps_Block_LineHeightCleared", IsUnset(rtb, WinRichTextBlock.LineHeightProperty));
            H.Check("RichTextProps_Block_TextAlignmentCleared", IsUnset(rtb, WinRichTextBlock.TextAlignmentProperty));
            H.Check("RichTextProps_Block_HorizontalTextAlignmentCleared", IsUnset(rtb, WinRichTextBlock.HorizontalTextAlignmentProperty));
            H.Check("RichTextProps_Block_TextTrimmingCleared", IsUnset(rtb, WinRichTextBlock.TextTrimmingProperty));
            H.Check("RichTextProps_Block_CharacterSpacingReset", rtb.CharacterSpacing == 0);
            H.Check("RichTextProps_Block_TextDecorationsCleared", IsUnset(rtb, WinRichTextBlock.TextDecorationsProperty));
            H.Check("RichTextProps_Block_LineStackingStrategyCleared", IsUnset(rtb, WinRichTextBlock.LineStackingStrategyProperty));
            H.Check("RichTextProps_Block_TextIndentCleared", IsUnset(rtb, WinRichTextBlock.TextIndentProperty));
            H.Check("RichTextProps_Block_TextLineBoundsCleared", IsUnset(rtb, WinRichTextBlock.TextLineBoundsProperty));
            H.Check("RichTextProps_Block_TextReadingOrderCleared", IsUnset(rtb, WinRichTextBlock.TextReadingOrderProperty));
            H.Check("RichTextProps_Block_IsTextScaleFactorEnabledCleared", IsUnset(rtb, WinRichTextBlock.IsTextScaleFactorEnabledProperty));
            H.Check("RichTextProps_Block_IsColorFontEnabledCleared", IsUnset(rtb, WinRichTextBlock.IsColorFontEnabledProperty));
            H.Check("RichTextProps_Block_OpticalMarginAlignmentCleared", IsUnset(rtb, WinRichTextBlock.OpticalMarginAlignmentProperty));
            H.Check("RichTextProps_Block_SelectionHighlightColorCleared", IsUnset(rtb, WinRichTextBlock.SelectionHighlightColorProperty));
            H.Check("RichTextProps_Block_PaddingCleared", IsUnset(rtb, WinRichTextBlock.PaddingProperty));
        }
    }

    internal class RichTextProps_Paragraph_MountUpdateClear(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (styled, setStyled) = ctx.UseState(true);
                var paragraph = Paragraph(Run("paragraph"));
                if (styled)
                {
                    paragraph = paragraph with
                    {
                        Margin = new Thickness(2, 3, 4, 5),
                        TextIndent = 9,
                        TextAlignment = TextAlignment.Right,
                        HorizontalTextAlignment = TextAlignment.Right,
                        LineHeight = 28,
                        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                        FontSize = 18,
                        FontFamily = "Consolas",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        FontStyle = global::Windows.UI.Text.FontStyle.Italic,
                        FontStretch = global::Windows.UI.Text.FontStretch.Condensed,
                        Foreground = Brush(20, 160, 80),
                        CharacterSpacing = 75,
                        TextDecorations = global::Windows.UI.Text.TextDecorations.Underline,
                        IsTextScaleFactorEnabled = false,
                        Language = "fr-FR",
                    };
                }

                return VStack(
                    Button("ToggleParagraphProps", () => setStyled(!styled)),
                    RichTextBlock(new[] { paragraph }));
            });

            await Harness.Render();

            var rtb = H.FindControl<WinRichTextBlock>(_ => true);
            H.Check("RichTextProps_Paragraph_RTBMounted", rtb is not null);
            if (rtb is null) return;

            var paragraph = FirstParagraph(rtb);
            H.Check("RichTextProps_Paragraph_Mounted", paragraph is not null);
            if (paragraph is null) return;

            H.Check("RichTextProps_Paragraph_Margin", paragraph.Margin == new Thickness(2, 3, 4, 5));
            H.Check("RichTextProps_Paragraph_TextIndent", IsClose(paragraph.TextIndent, 9));
            H.Check("RichTextProps_Paragraph_TextAlignment", paragraph.TextAlignment == TextAlignment.Right);
            H.Check("RichTextProps_Paragraph_HorizontalTextAlignment", paragraph.HorizontalTextAlignment == TextAlignment.Right);
            H.Check("RichTextProps_Paragraph_LineHeight", IsClose(paragraph.LineHeight, 28));
            H.Check("RichTextProps_Paragraph_LineStackingStrategy", paragraph.LineStackingStrategy == LineStackingStrategy.BlockLineHeight);
            H.Check("RichTextProps_Paragraph_FontSize", IsClose(paragraph.FontSize, 18));
            H.Check("RichTextProps_Paragraph_FontFamily", paragraph.FontFamily.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase));
            H.Check("RichTextProps_Paragraph_FontWeight", paragraph.FontWeight.Weight >= 600);
            H.Check("RichTextProps_Paragraph_FontStyle", paragraph.FontStyle == global::Windows.UI.Text.FontStyle.Italic);
            H.Check("RichTextProps_Paragraph_FontStretch", paragraph.FontStretch == global::Windows.UI.Text.FontStretch.Condensed);
            H.Check("RichTextProps_Paragraph_Foreground", IsColor(paragraph.Foreground, 20, 160, 80));
            H.Check("RichTextProps_Paragraph_CharacterSpacing", paragraph.CharacterSpacing == 75);
            H.Check("RichTextProps_Paragraph_TextDecorations", paragraph.TextDecorations == global::Windows.UI.Text.TextDecorations.Underline);
            H.Check("RichTextProps_Paragraph_IsTextScaleFactorEnabled", !paragraph.IsTextScaleFactorEnabled);
            H.Check("RichTextProps_Paragraph_Language", paragraph.Language == "fr-FR");

            H.ClickButton("ToggleParagraphProps");
            await Harness.Render();

            H.Check("RichTextProps_Paragraph_IdentityPreserved", ReferenceEquals(paragraph, FirstParagraph(rtb)));
            H.Check("RichTextProps_Paragraph_MarginCleared", IsUnset(paragraph, WinBlock.MarginProperty));
            H.Check("RichTextProps_Paragraph_TextIndentCleared", IsUnset(paragraph, WinParagraph.TextIndentProperty));
            H.Check("RichTextProps_Paragraph_TextAlignmentCleared", IsUnset(paragraph, WinBlock.TextAlignmentProperty));
            H.Check("RichTextProps_Paragraph_HorizontalTextAlignmentCleared", IsUnset(paragraph, WinBlock.HorizontalTextAlignmentProperty));
            H.Check("RichTextProps_Paragraph_LineHeightCleared", IsUnset(paragraph, WinBlock.LineHeightProperty));
            H.Check("RichTextProps_Paragraph_LineStackingStrategyCleared", IsUnset(paragraph, WinBlock.LineStackingStrategyProperty));
            H.Check("RichTextProps_Paragraph_FontSizeCleared", IsUnset(paragraph, WinTextElement.FontSizeProperty));
            H.Check("RichTextProps_Paragraph_FontFamilyCleared", IsUnset(paragraph, WinTextElement.FontFamilyProperty));
            H.Check("RichTextProps_Paragraph_FontWeightCleared", IsUnset(paragraph, WinTextElement.FontWeightProperty));
            H.Check("RichTextProps_Paragraph_FontStyleCleared", IsUnset(paragraph, WinTextElement.FontStyleProperty));
            H.Check("RichTextProps_Paragraph_FontStretchCleared", IsUnset(paragraph, WinTextElement.FontStretchProperty));
            H.Check("RichTextProps_Paragraph_ForegroundCleared", IsUnset(paragraph, WinTextElement.ForegroundProperty));
            H.Check("RichTextProps_Paragraph_CharacterSpacingCleared", IsUnset(paragraph, WinTextElement.CharacterSpacingProperty));
            H.Check("RichTextProps_Paragraph_TextDecorationsCleared", IsUnset(paragraph, WinTextElement.TextDecorationsProperty));
            H.Check("RichTextProps_Paragraph_IsTextScaleFactorEnabledCleared", IsUnset(paragraph, WinTextElement.IsTextScaleFactorEnabledProperty));
            H.Check("RichTextProps_Paragraph_LanguageCleared", IsUnset(paragraph, WinTextElement.LanguageProperty));
        }
    }

    internal class RichTextProps_Run_MountUpdateClear(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (styled, setStyled) = ctx.UseState(true);
                var run = Run("run");
                if (styled)
                {
                    run = run with
                    {
                        FontSize = 19,
                        FontFamily = "Consolas",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        FontStyle = global::Windows.UI.Text.FontStyle.Italic,
                        FontStretch = global::Windows.UI.Text.FontStretch.Expanded,
                        Foreground = Brush(140, 40, 220),
                        CharacterSpacing = 90,
                        TextDecorations = global::Windows.UI.Text.TextDecorations.Underline | global::Windows.UI.Text.TextDecorations.Strikethrough,
                        IsTextScaleFactorEnabled = false,
                        Language = "es-ES",
                        FlowDirection = FlowDirection.RightToLeft,
                    };
                }

                return VStack(
                    Button("ToggleRunProps", () => setStyled(!styled)),
                    RichTextBlock(new[] { Paragraph(run) }));
            });

            await Harness.Render();

            var rtb = H.FindControl<WinRichTextBlock>(_ => true);
            H.Check("RichTextProps_Run_RTBMounted", rtb is not null);
            if (rtb is null) return;

            var run = FirstRun(rtb);
            H.Check("RichTextProps_Run_Mounted", run is not null);
            if (run is null) return;

            H.Check("RichTextProps_Run_FontSize", IsClose(run.FontSize, 19));
            H.Check("RichTextProps_Run_FontFamily", run.FontFamily.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase));
            H.Check("RichTextProps_Run_FontWeight", run.FontWeight.Weight >= 600);
            H.Check("RichTextProps_Run_FontStyle", run.FontStyle == global::Windows.UI.Text.FontStyle.Italic);
            H.Check("RichTextProps_Run_FontStretch", run.FontStretch == global::Windows.UI.Text.FontStretch.Expanded);
            H.Check("RichTextProps_Run_Foreground", IsColor(run.Foreground, 140, 40, 220));
            H.Check("RichTextProps_Run_CharacterSpacing", run.CharacterSpacing == 90);
            H.Check("RichTextProps_Run_TextDecorations", run.TextDecorations.HasFlag(global::Windows.UI.Text.TextDecorations.Underline)
                && run.TextDecorations.HasFlag(global::Windows.UI.Text.TextDecorations.Strikethrough));
            H.Check("RichTextProps_Run_IsTextScaleFactorEnabled", !run.IsTextScaleFactorEnabled);
            H.Check("RichTextProps_Run_Language", run.Language == "es-ES");
            H.Check("RichTextProps_Run_FlowDirection", run.FlowDirection == FlowDirection.RightToLeft);

            H.ClickButton("ToggleRunProps");
            await Harness.Render();

            H.Check("RichTextProps_Run_IdentityPreserved", ReferenceEquals(run, FirstRun(rtb)));
            H.Check("RichTextProps_Run_FontSizeCleared", IsUnset(run, WinTextElement.FontSizeProperty));
            H.Check("RichTextProps_Run_FontFamilyCleared", IsUnset(run, WinTextElement.FontFamilyProperty));
            H.Check("RichTextProps_Run_FontWeightCleared", IsUnset(run, WinTextElement.FontWeightProperty));
            H.Check("RichTextProps_Run_FontStyleCleared", IsUnset(run, WinTextElement.FontStyleProperty));
            H.Check("RichTextProps_Run_FontStretchCleared", IsUnset(run, WinTextElement.FontStretchProperty));
            H.Check("RichTextProps_Run_ForegroundCleared", IsUnset(run, WinTextElement.ForegroundProperty));
            H.Check("RichTextProps_Run_CharacterSpacingCleared", IsUnset(run, WinTextElement.CharacterSpacingProperty));
            H.Check("RichTextProps_Run_TextDecorationsCleared", IsUnset(run, WinTextElement.TextDecorationsProperty));
            H.Check("RichTextProps_Run_IsTextScaleFactorEnabledCleared", IsUnset(run, WinTextElement.IsTextScaleFactorEnabledProperty));
            H.Check("RichTextProps_Run_LanguageCleared", IsUnset(run, WinTextElement.LanguageProperty));
            H.Check("RichTextProps_Run_FlowDirectionCleared", IsUnset(run, WinRun.FlowDirectionProperty));
        }
    }

    internal class RichTextProps_Hyperlink_MountUpdateClear(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (styled, setStyled) = ctx.UseState(true);
                var link = Hyperlink("link", new Uri("https://example.com"));
                if (styled)
                {
                    link = link with
                    {
                        FontSize = 20,
                        FontFamily = "Consolas",
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        FontStyle = global::Windows.UI.Text.FontStyle.Italic,
                        FontStretch = global::Windows.UI.Text.FontStretch.Expanded,
                        Foreground = Brush(220, 110, 20),
                        CharacterSpacing = 130,
                        TextDecorations = global::Windows.UI.Text.TextDecorations.Underline,
                        IsTextScaleFactorEnabled = false,
                        Language = "de-DE",
                        UnderlineStyle = WinUnderlineStyle.None,
                        IsTabStop = false,
                        TabIndex = 7,
                    };
                }

                return VStack(
                    Button("ToggleHyperlinkProps", () => setStyled(!styled)),
                    RichTextBlock(new[] { Paragraph(link) }));
            });

            await Harness.Render();

            var rtb = H.FindControl<WinRichTextBlock>(_ => true);
            H.Check("RichTextProps_Hyperlink_RTBMounted", rtb is not null);
            if (rtb is null) return;

            var link = FirstHyperlink(rtb);
            var linkRun = FirstHyperlinkRun(rtb);
            H.Check("RichTextProps_Hyperlink_Mounted", link is not null);
            if (link is null) return;

            H.Check("RichTextProps_Hyperlink_FontSize", IsClose(link.FontSize, 20));
            H.Check("RichTextProps_Hyperlink_FontFamily", link.FontFamily.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase));
            H.Check("RichTextProps_Hyperlink_FontWeight", link.FontWeight.Weight >= 700);
            H.Check("RichTextProps_Hyperlink_FontStyle", link.FontStyle == global::Windows.UI.Text.FontStyle.Italic);
            H.Check("RichTextProps_Hyperlink_FontStretch", link.FontStretch == global::Windows.UI.Text.FontStretch.Expanded);
            H.Check("RichTextProps_Hyperlink_Foreground", IsColor(link.Foreground, 220, 110, 20));
            H.Check("RichTextProps_Hyperlink_CharacterSpacing", link.CharacterSpacing == 130);
            H.Check("RichTextProps_Hyperlink_TextDecorations", linkRun?.TextDecorations == global::Windows.UI.Text.TextDecorations.Underline);
            H.Check("RichTextProps_Hyperlink_IsTextScaleFactorEnabled", !link.IsTextScaleFactorEnabled);
            H.Check("RichTextProps_Hyperlink_Language", link.Language == "de-DE");
            H.Check("RichTextProps_Hyperlink_UnderlineStyle", link.UnderlineStyle == WinUnderlineStyle.None);
            H.Check("RichTextProps_Hyperlink_IsTabStop", !link.IsTabStop);
            H.Check("RichTextProps_Hyperlink_TabIndex", link.TabIndex == 7);

            H.ClickButton("ToggleHyperlinkProps");
            await Harness.Render();

            H.Check("RichTextProps_Hyperlink_IdentityPreserved", ReferenceEquals(link, FirstHyperlink(rtb)));
            H.Check("RichTextProps_Hyperlink_FontSizeCleared", IsUnset(link, WinTextElement.FontSizeProperty));
            H.Check("RichTextProps_Hyperlink_FontFamilyCleared", IsUnset(link, WinTextElement.FontFamilyProperty));
            H.Check("RichTextProps_Hyperlink_FontWeightCleared", IsUnset(link, WinTextElement.FontWeightProperty));
            H.Check("RichTextProps_Hyperlink_FontStyleCleared", IsUnset(link, WinTextElement.FontStyleProperty));
            H.Check("RichTextProps_Hyperlink_FontStretchCleared", IsUnset(link, WinTextElement.FontStretchProperty));
            H.Check("RichTextProps_Hyperlink_ForegroundCleared", IsUnset(link, WinTextElement.ForegroundProperty));
            H.Check("RichTextProps_Hyperlink_CharacterSpacingCleared", IsUnset(link, WinTextElement.CharacterSpacingProperty));
            H.Check("RichTextProps_Hyperlink_TextDecorationsCleared", linkRun is not null && IsUnset(linkRun, WinTextElement.TextDecorationsProperty));
            H.Check("RichTextProps_Hyperlink_IsTextScaleFactorEnabledCleared", IsUnset(link, WinTextElement.IsTextScaleFactorEnabledProperty));
            H.Check("RichTextProps_Hyperlink_LanguageCleared", IsUnset(link, WinTextElement.LanguageProperty));
            H.Check("RichTextProps_Hyperlink_UnderlineStyleCleared", IsUnset(link, WinHyperlink.UnderlineStyleProperty));
            H.Check("RichTextProps_Hyperlink_IsTabStopCleared", IsUnset(link, WinHyperlink.IsTabStopProperty));
            H.Check("RichTextProps_Hyperlink_TabIndexCleared", IsUnset(link, WinHyperlink.TabIndexProperty));
        }
    }

    internal class RichTextProps_LineBreakInlineUI_MountUpdateClear(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (styled, setStyled) = ctx.UseState(true);
                var lineBreak = new RichTextLineBreak();
                var inlineUi = InlineUI(Button("InlineChild", () => { }));
                if (styled)
                {
                    lineBreak = lineBreak with
                    {
                        FontSize = 16,
                        FontFamily = "Consolas",
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                        FontStyle = global::Windows.UI.Text.FontStyle.Italic,
                        FontStretch = global::Windows.UI.Text.FontStretch.Expanded,
                        Foreground = Brush(30, 60, 180),
                        CharacterSpacing = 42,
                        TextDecorations = global::Windows.UI.Text.TextDecorations.Underline,
                        IsTextScaleFactorEnabled = false,
                        Language = "it-IT",
                    };
                    inlineUi = inlineUi with
                    {
                        FontSize = 17,
                        FontFamily = "Consolas",
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        FontStyle = global::Windows.UI.Text.FontStyle.Italic,
                        FontStretch = global::Windows.UI.Text.FontStretch.Condensed,
                        Foreground = Brush(80, 30, 160),
                        CharacterSpacing = 55,
                        TextDecorations = global::Windows.UI.Text.TextDecorations.Strikethrough,
                        IsTextScaleFactorEnabled = false,
                        Language = "pt-BR",
                    };
                }

                return VStack(
                    Button("ToggleLineBreakInlineUIProps", () => setStyled(!styled)),
                    RichTextBlock(new[] { Paragraph(Run("before"), lineBreak, inlineUi, Run("after")) }));
            });

            await Harness.Render();

            var rtb = H.FindControl<WinRichTextBlock>(_ => true);
            H.Check("RichTextProps_LineBreakInlineUI_RTBMounted", rtb is not null);
            if (rtb is null) return;

            var lineBreak = FirstLineBreak(rtb);
            var inlineUi = FirstInlineUIContainer(rtb);
            H.Check("RichTextProps_LineBreak_Mounted", lineBreak is not null);
            H.Check("RichTextProps_InlineUI_Mounted", inlineUi is not null);
            if (lineBreak is null || inlineUi is null) return;

            H.Check("RichTextProps_LineBreak_FontSize", IsClose(lineBreak.FontSize, 16));
            H.Check("RichTextProps_LineBreak_FontFamily", lineBreak.FontFamily.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase));
            H.Check("RichTextProps_LineBreak_FontWeight", lineBreak.FontWeight.Weight >= 700);
            H.Check("RichTextProps_LineBreak_FontStyle", lineBreak.FontStyle == global::Windows.UI.Text.FontStyle.Italic);
            H.Check("RichTextProps_LineBreak_FontStretch", lineBreak.FontStretch == global::Windows.UI.Text.FontStretch.Expanded);
            H.Check("RichTextProps_LineBreak_Foreground", IsColor(lineBreak.Foreground, 30, 60, 180));
            H.Check("RichTextProps_LineBreak_CharacterSpacing", lineBreak.CharacterSpacing == 42);
            H.Check("RichTextProps_LineBreak_TextDecorations", lineBreak.TextDecorations == global::Windows.UI.Text.TextDecorations.Underline);
            H.Check("RichTextProps_LineBreak_IsTextScaleFactorEnabled", !lineBreak.IsTextScaleFactorEnabled);
            H.Check("RichTextProps_LineBreak_Language", lineBreak.Language == "it-IT");

            H.Check("RichTextProps_InlineUI_FontSize", IsClose(inlineUi.FontSize, 17));
            H.Check("RichTextProps_InlineUI_FontFamily", inlineUi.FontFamily.Source.Contains("Consolas", StringComparison.OrdinalIgnoreCase));
            H.Check("RichTextProps_InlineUI_FontWeight", inlineUi.FontWeight.Weight >= 600);
            H.Check("RichTextProps_InlineUI_FontStyle", inlineUi.FontStyle == global::Windows.UI.Text.FontStyle.Italic);
            H.Check("RichTextProps_InlineUI_FontStretch", inlineUi.FontStretch == global::Windows.UI.Text.FontStretch.Condensed);
            H.Check("RichTextProps_InlineUI_Foreground", IsColor(inlineUi.Foreground, 80, 30, 160));
            H.Check("RichTextProps_InlineUI_CharacterSpacing", inlineUi.CharacterSpacing == 55);
            H.Check("RichTextProps_InlineUI_TextDecorations", inlineUi.TextDecorations == global::Windows.UI.Text.TextDecorations.Strikethrough);
            H.Check("RichTextProps_InlineUI_IsTextScaleFactorEnabled", !inlineUi.IsTextScaleFactorEnabled);
            H.Check("RichTextProps_InlineUI_Language", inlineUi.Language == "pt-BR");

            H.ClickButton("ToggleLineBreakInlineUIProps");
            await Harness.Render();

            H.Check("RichTextProps_LineBreak_IdentityPreserved", ReferenceEquals(lineBreak, FirstLineBreak(rtb)));
            H.Check("RichTextProps_InlineUI_IdentityPreserved", ReferenceEquals(inlineUi, FirstInlineUIContainer(rtb)));
            H.Check("RichTextProps_LineBreak_FontSizeCleared", IsUnset(lineBreak, WinTextElement.FontSizeProperty));
            H.Check("RichTextProps_LineBreak_FontFamilyCleared", IsUnset(lineBreak, WinTextElement.FontFamilyProperty));
            H.Check("RichTextProps_LineBreak_FontWeightCleared", IsUnset(lineBreak, WinTextElement.FontWeightProperty));
            H.Check("RichTextProps_LineBreak_FontStyleCleared", IsUnset(lineBreak, WinTextElement.FontStyleProperty));
            H.Check("RichTextProps_LineBreak_FontStretchCleared", IsUnset(lineBreak, WinTextElement.FontStretchProperty));
            H.Check("RichTextProps_LineBreak_ForegroundCleared", IsUnset(lineBreak, WinTextElement.ForegroundProperty));
            H.Check("RichTextProps_LineBreak_CharacterSpacingCleared", IsUnset(lineBreak, WinTextElement.CharacterSpacingProperty));
            H.Check("RichTextProps_LineBreak_TextDecorationsCleared", IsUnset(lineBreak, WinTextElement.TextDecorationsProperty));
            H.Check("RichTextProps_LineBreak_IsTextScaleFactorEnabledCleared", IsUnset(lineBreak, WinTextElement.IsTextScaleFactorEnabledProperty));
            H.Check("RichTextProps_LineBreak_LanguageCleared", IsUnset(lineBreak, WinTextElement.LanguageProperty));

            H.Check("RichTextProps_InlineUI_FontSizeCleared", IsUnset(inlineUi, WinTextElement.FontSizeProperty));
            H.Check("RichTextProps_InlineUI_FontFamilyCleared", IsUnset(inlineUi, WinTextElement.FontFamilyProperty));
            H.Check("RichTextProps_InlineUI_FontWeightCleared", IsUnset(inlineUi, WinTextElement.FontWeightProperty));
            H.Check("RichTextProps_InlineUI_FontStyleCleared", IsUnset(inlineUi, WinTextElement.FontStyleProperty));
            H.Check("RichTextProps_InlineUI_FontStretchCleared", IsUnset(inlineUi, WinTextElement.FontStretchProperty));
            H.Check("RichTextProps_InlineUI_ForegroundCleared", IsUnset(inlineUi, WinTextElement.ForegroundProperty));
            H.Check("RichTextProps_InlineUI_CharacterSpacingCleared", IsUnset(inlineUi, WinTextElement.CharacterSpacingProperty));
            H.Check("RichTextProps_InlineUI_TextDecorationsCleared", IsUnset(inlineUi, WinTextElement.TextDecorationsProperty));
            H.Check("RichTextProps_InlineUI_IsTextScaleFactorEnabledCleared", IsUnset(inlineUi, WinTextElement.IsTextScaleFactorEnabledProperty));
            H.Check("RichTextProps_InlineUI_LanguageCleared", IsUnset(inlineUi, WinTextElement.LanguageProperty));
        }
    }

    private static WinParagraph? FirstParagraph(WinRichTextBlock rtb)
        => rtb.Blocks.OfType<WinParagraph>().FirstOrDefault();

    private static WinRun? FirstRun(WinRichTextBlock rtb)
        => FirstParagraph(rtb)?.Inlines.OfType<WinRun>().FirstOrDefault();

    private static WinHyperlink? FirstHyperlink(WinRichTextBlock rtb)
        => FirstParagraph(rtb)?.Inlines.OfType<WinHyperlink>().FirstOrDefault();

    private static WinRun? FirstHyperlinkRun(WinRichTextBlock rtb)
        => FirstHyperlink(rtb)?.Inlines.OfType<WinRun>().FirstOrDefault();

    private static WinLineBreak? FirstLineBreak(WinRichTextBlock rtb)
        => FirstParagraph(rtb)?.Inlines.OfType<WinLineBreak>().FirstOrDefault();

    private static WinInlineUIContainer? FirstInlineUIContainer(WinRichTextBlock rtb)
        => FirstParagraph(rtb)?.Inlines.OfType<WinInlineUIContainer>().FirstOrDefault();

    private static SolidColorBrush Brush(byte r, byte g, byte b) =>
        new(global::Windows.UI.Color.FromArgb(255, r, g, b));

    private static bool IsColor(Brush? brush, byte r, byte g, byte b) =>
        brush is SolidColorBrush sb
        && sb.Color.A == 255
        && sb.Color.R == r
        && sb.Color.G == g
        && sb.Color.B == b;

    private static bool IsUnset(DependencyObject target, DependencyProperty property) =>
        ReferenceEquals(target.ReadLocalValue(property), DependencyProperty.UnsetValue);

    private static bool IsClose(double actual, double expected) =>
        Math.Abs(actual - expected) < 0.001d;
}
