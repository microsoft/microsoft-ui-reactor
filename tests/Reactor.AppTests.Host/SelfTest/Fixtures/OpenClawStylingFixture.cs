using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinUIGalleryReactor.ControlPages.Collections;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal sealed class OpenClawStylingFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        using var host = H.CreateHost();
        var entries = Enumerable.Range(0, 8).Select(i => new HistoryEntry($"row-{i}", "Assistant", $"Preview {i}")).ToArray();
        host.Mount(_ => VStack(8,
            Border(OpenClawChatStyles.Prose("User message", user: true))
                .Set(OpenClawChatStyles.UserBubble).AutomationId("OpenClawUserBubble"),
            OpenClawChatStyles.Composer(TextBox("Draft").AutomationName("Message"), TextBlock("Toolbar"), 900),
            TextBlock("Placeholder color probe").Foreground(Microsoft.UI.Reactor.Core.Theme.TertiaryText)
                .AutomationId("OpenClawPlaceholderProbe"),
            (SplitView(TextBlock("Details"), TextBlock("Conversation")) with { DisplayMode = SplitViewDisplayMode.Inline })
                .ResizablePane().Set(OpenClawChatStyles.ResizePane).Height(100).AutomationId("OpenClawPane"),
            AnnotatedScrollBar().ScrollRange(1).Width(40).Height(160)
                .Set(bar => OpenClawHistoryRail.Update(bar, entries, null, _ => { }))
                .OnUnmount(OpenClawHistoryRail.Detach).AutomationName("History"))
            .Width(900).Set(panel => panel.RequestedTheme = ElementTheme.Light));
        await Harness.Render();
        var composer = H.FindControl<Border>(b => AutomationProperties.GetAutomationId(b) == "ChatWritingSurface")!;
        var input = H.FindControl<TextBox>(_ => true)!;
        H.Check("OpenClaw_ComposerGeometry", composer.CornerRadius.TopLeft == 20
            && composer.MinHeight == 96 && composer.MaxWidth == 768);
        H.Check("OpenClaw_BorderlessEditor", input.BorderThickness == new Thickness(0)
            && input.Background is SolidColorBrush { Color.A: 0 }
            && input.MinHeight == 44 && input.MaxHeight == 200 && input.FontSize == 14);
        var placeholderProbe = H.FindControl<TextBlock>(text => AutomationProperties.GetAutomationId(text) == "OpenClawPlaceholderProbe")!;
        H.Check("OpenClaw_MutedPlaceholder", input.PlaceholderForeground is SolidColorBrush placeholder
            && placeholderProbe.Foreground is SolidColorBrush expected && placeholder.Color == expected.Color);
        var pane = H.FindControl<SplitView>(_ => true)!;
        var grip = ((Grid)pane.Pane).Children.OfType<Button>().Single();
        H.Check("OpenClaw_ResizeGrip", grip.Width == 12 && grip.Content is Border { Width: 4, Height: 40 });
        OpenClawChatStyles.ResizePane(pane);
        H.Check("OpenClaw_ResizeResourcesAreStable", grip.Resources.MergedDictionaries.Count == 1);
        var bubble = H.FindControl<Border>(b => AutomationProperties.GetAutomationId(b) == "OpenClawUserBubble")!;
        H.Check("OpenClaw_LightUserTint", bubble.Background is SolidColorBrush light && Math.Abs(light.Opacity - 0.08) < 0.001);
        bubble.RequestedTheme = ElementTheme.Dark;
        await Harness.Render();
        H.Check("OpenClaw_DarkUserTint", bubble.Background is SolidColorBrush dark && Math.Abs(dark.Opacity - 0.12) < 0.001);
        var bar = H.FindControl<AnnotatedScrollBar>(_ => true)!;
        H.Check("OpenClaw_HistoryLabelsMounted", await Harness.WaitForWithin(() => bar.Labels.Count == 8
            && OpenClawHistoryRail.Find<Border>(bar, "Tick") is not null, 2000));
        var labels = OpenClawHistoryRail.Find<Grid>(bar, "PART_LabelsGrid")!;
        var tickControl = OpenClawHistoryRail.Find<ContentControl>(labels.Children[0])!;
        var tick = OpenClawHistoryRail.Find<Border>(tickControl, "Tick")!;
        H.Check("OpenClaw_QuietTickGeometry", tick.Width == 8 && tick.Height == 2 && tick.CornerRadius.TopLeft == 1);
        VisualStateManager.GoToState(tickControl, "PointerOver", false);
        await Harness.Render();
        H.Check("OpenClaw_HoverTickWidth", tick.Width == 32);
        VisualStateManager.GoToState(tickControl, "HoverAdjacent", false);
        await Harness.Render();
        H.Check("OpenClaw_NeighborTickWidth", tick.Width == 24);
        H.Check("OpenClaw_NoNativeScrollButtons", OpenClawHistoryRail.Find<Microsoft.UI.Xaml.Controls.Primitives.RepeatButton>(bar) is null);
        var thumb = OpenClawHistoryRail.Find<Border>(bar, "PART_VerticalThumb")!;
        H.Check("OpenClaw_NoSecondVisibleThumb", thumb.Opacity == 0 && !thumb.IsHitTestVisible);

        host.Mount(_ => OpenClawTranscriptFade.Wrap(ScrollView(TextBlock("Fade content").Height(500)))
            .Height(120).AutomationId("OpenClawFade"));
        await Harness.Render();
        var fade = H.FindControl<Border>(b => AutomationProperties.GetAutomationId(b) == "OpenClawFade");
        H.Check("OpenClaw_FadeHostMounted", fade is not null,
            string.Join("; ", H.FindAllControls<TextBlock>(_ => true).Select(text => text.Text)));
        if (fade is null) return;
        var clipper = (Border)fade.Child;
        var highContrast = new global::Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        H.Check("OpenClaw_TranscriptAlphaBand",
            highContrast
                ? Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementChildVisual(fade) is null
                : Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementChildVisual(fade) is Microsoft.UI.Composition.SpriteVisual
                    && Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(clipper).Clip
                        is Microsoft.UI.Composition.InsetClip { BottomInset: 32 });
        host.Mount(_ => Empty());
        await Harness.Render();
        H.Check("OpenClaw_TranscriptFadeDetached",
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementChildVisual(fade) is null
            && Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(clipper).Clip is null);
    }
}
