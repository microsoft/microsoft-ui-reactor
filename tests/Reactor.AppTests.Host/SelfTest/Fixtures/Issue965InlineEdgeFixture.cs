using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression for https://github.com/microsoft/microsoft-ui-reactor/issues/965 (item 2).
///
/// When no physical <c>Margin</c> / <c>Padding</c> / <c>BorderThickness</c> is declared,
/// the BiDi inline overlay takes the edges it does not own from the control. On an update
/// that control still carries the previous render's overlay, so the old inline edge was
/// re-adopted as the base: <c>.PaddingInlineStart(8)</c> → <c>.PaddingInlineEnd(4)</c>
/// resolved to <c>8,0,4,0</c>, and flipping <c>FlowDirection</c> under an unchanged
/// <c>.PaddingInlineStart(8)</c> resolved to <c>8,0,8,0</c>.
/// </summary>
internal static class Issue965InlineEdgeFixture
{
    internal class InlineEdgesDoNotReadBackPreviousOverlay(Harness h) : SelfTestFixtureBase(h)
    {
        private static bool SameInstance(object? a, object? b) => ReferenceEquals(a, b);

        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var dir = phase == 1 ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

                return VStack(
                    Button("Issue965_Next", () => setPhase(phase + 1)),
                    // Swap which inline edge is declared.
                    phase == 0
                        ? Border(TextBlock("Issue965_padSwap")).PaddingInlineStart(8)
                        : Border(TextBlock("Issue965_padSwap")).PaddingInlineEnd(4),
                    phase == 0
                        ? TextBlock("Issue965_marginSwap").MarginInlineStart(8)
                        : TextBlock("Issue965_marginSwap").MarginInlineEnd(4),
                    phase == 0
                        ? Button("Issue965_buttonSwap", () => { }).PaddingInlineStart(8)
                        : Button("Issue965_buttonSwap", () => { }).PaddingInlineEnd(4),
                    // A flyout decorator applies its modifiers to its target's control in the
                    // same pass, so both layers overlay one Margin. Each must base its edges on
                    // its own previous overlay, not on the other layer's.
                    Flyout(
                        Button("Issue965_flyoutTarget", () => { }).MarginInlineStart(phase == 0 ? 8 : 6),
                        TextBlock("Issue965_flyoutMenu")).MarginInlineEnd(4),
                    // The decorator's overlay sits on top of whatever its target wrote this pass,
                    // so it must read that, not a remembered base: here its previous inline
                    // values equal the target's.
                    phase == 0
                        ? Flyout(
                            Button("Issue965_flyoutCollide", () => { }).Margin(0).MarginInlineEnd(4),
                            TextBlock("Issue965_flyoutCollideMenu")).MarginInlineEnd(4)
                        : Flyout(
                            Button("Issue965_flyoutCollide", () => { }).Margin(9).MarginInlineEnd(4),
                            TextBlock("Issue965_flyoutCollideMenu")).MarginInlineStart(8),
                    // ...and here the target's new physical margin equals the decorator's
                    // previous output, so "the control still holds what I wrote" is not proof.
                    phase == 0
                        ? Flyout(
                            Button("Issue965_flyoutEqual", () => { }).Margin(0),
                            TextBlock("Issue965_flyoutEqualMenu")).MarginInlineStart(8)
                        : Flyout(
                            Button("Issue965_flyoutEqual", () => { }).Margin(8, 0, 0, 0),
                            TextBlock("Issue965_flyoutEqualMenu")).MarginInlineEnd(4),
                    // Same inline edge, FlowDirection flipped LTR → RTL → LTR.
                    TextBlock("Issue965_flipPad")
                        .Set(tb => tb.FlowDirection = dir)
                        .PaddingInlineStart(8),
                    TextBlock("Issue965_flipMargin")
                        .Set(tb => tb.FlowDirection = dir)
                        .MarginInlineStart(8),
                    Border(TextBlock("Issue965_flipBorder"))
                        .Set(b => b.FlowDirection = dir)
                        .BorderInlineStart(new Thickness(9)),
                    // With a physical value declared, LTR resolves back to exactly that value,
                    // so a guard comparing only against the old physical slot skips the
                    // RTL → LTR write and strands the RTL overlay.
                    TextBlock("Issue965_flipDeclared")
                        .Set(tb => tb.FlowDirection = dir)
                        .Padding(5, 0, 9, 0)
                        .PaddingInlineStart(5),
                    // Owners other than a plain control: a Memo's own Border wrapper, and an
                    // Expander, whose decorator handler creates the control itself.
                    phase == 0
                        ? Memo(_ => TextBlock("Issue965_memoChild")).MarginInlineStart(8)
                        : Memo(_ => TextBlock("Issue965_memoChild")).MarginInlineEnd(4),
                    Expander("Issue965_expander", TextBlock("Issue965_expanderBody"))
                        .Set(e => e.FlowDirection = dir)
                        .Padding(5, 0, 9, 0)
                        .PaddingInlineStart(5),
                    // A setter-written base is rewritten every render, so a changed setter
                    // value must still win over the base remembered from the last overlay.
                    TextBlock("Issue965_setterBase")
                        .Set(tb => tb.Padding = phase == 0 ? new Thickness(5, 6, 7, 8) : new Thickness(9))
                        .PaddingInlineStart(1));
            });

            await Harness.Render();

            WinUI.Border? FindBorder(string childText) => H.FindControl<WinUI.Border>(
                b => b.Child is WinUI.TextBlock tb && tb.Text == childText);

            var padSwap = FindBorder("Issue965_padSwap");
            var marginSwap = H.FindText("Issue965_marginSwap");
            var buttonSwap = H.FindButton("Issue965_buttonSwap");
            var flipPad = H.FindText("Issue965_flipPad");
            var flipMargin = H.FindText("Issue965_flipMargin");
            var flipBorder = FindBorder("Issue965_flipBorder");
            var setterBase = H.FindText("Issue965_setterBase");
            var flipDeclared = H.FindText("Issue965_flipDeclared");
            var flyoutTarget = H.FindButton("Issue965_flyoutTarget");
            var flyoutCollide = H.FindButton("Issue965_flyoutCollide");
            var flyoutEqual = H.FindButton("Issue965_flyoutEqual");
            WinUI.Border? FindMemoWrapper() => H.FindControl<WinUI.Border>(
                b => b.Child is WinUI.TextBlock tb && tb.Text == "Issue965_memoChild");
            WinUI.Expander? FindExpander() => H.FindControl<WinUI.Expander>(
                e => e.Header is string s && s == "Issue965_expander");
            var memoWrapper = FindMemoWrapper();
            var expander = FindExpander();
            H.Check("Issue965_Mounted",
                padSwap is not null && marginSwap is not null && buttonSwap is not null
                && flipPad is not null && flipMargin is not null && flipBorder is not null
                && setterBase is not null && flipDeclared is not null && flyoutTarget is not null
                && flyoutCollide is not null && flyoutEqual is not null
                && memoWrapper is not null && expander is not null);
            if (padSwap is null || marginSwap is null || buttonSwap is null || flipPad is null
                || flipMargin is null || flipBorder is null || setterBase is null
                || flipDeclared is null || flyoutTarget is null || flyoutCollide is null
                || flyoutEqual is null || memoWrapper is null || expander is null) return;

            // Phase 0 has to put the inline edge on the control, or phase 1 proves nothing.
            H.Check("Issue965_Phase0_PadSwapApplied", padSwap.Padding == new Thickness(8, 0, 0, 0));
            H.Check("Issue965_Phase0_MarginSwapApplied", marginSwap.Margin == new Thickness(8, 0, 0, 0));
            // The overlay runs before the Button's style is applied, so its mount base is zero.
            H.Check("Issue965_Phase0_ButtonSwapApplied", buttonSwap.Padding == new Thickness(8, 0, 0, 0));
            H.Check("Issue965_Phase0_FlipPadLtr", flipPad.Padding == new Thickness(8, 0, 0, 0));
            H.Check("Issue965_Phase0_FlipMarginLtr", flipMargin.Margin == new Thickness(8, 0, 0, 0));
            H.Check("Issue965_Phase0_FlipBorderLtr", flipBorder.BorderThickness == new Thickness(9, 0, 0, 0));
            H.Check("Issue965_Phase0_SetterBaseOverlaid", setterBase.Padding == new Thickness(1, 6, 7, 8));
            H.Check("Issue965_Phase0_FlipDeclaredLtr", flipDeclared.Padding == new Thickness(5, 0, 9, 0));
            H.Check("Issue965_Phase0_FlyoutLayersCombined", flyoutTarget.Margin == new Thickness(8, 0, 4, 0));
            H.Check("Issue965_Phase0_FlyoutCollideCombined", flyoutCollide.Margin == new Thickness(0, 0, 4, 0));
            H.Check("Issue965_Phase0_FlyoutEqualCombined", flyoutEqual.Margin == new Thickness(8, 0, 0, 0));
            H.Check("Issue965_Phase0_MemoWrapperApplied", memoWrapper.Margin == new Thickness(8, 0, 0, 0));
            H.Check("Issue965_Phase0_ExpanderLtr", expander.Padding == new Thickness(5, 0, 9, 0));

            H.ClickButton("Issue965_Next");
            await Harness.Render();

            H.Check("Issue965_Phase1_SameInstances",
                SameInstance(padSwap, FindBorder("Issue965_padSwap"))
                && SameInstance(marginSwap, H.FindText("Issue965_marginSwap"))
                && SameInstance(buttonSwap, H.FindButton("Issue965_buttonSwap"))
                && SameInstance(flipPad, H.FindText("Issue965_flipPad"))
                && SameInstance(flipMargin, H.FindText("Issue965_flipMargin"))
                && SameInstance(flipBorder, FindBorder("Issue965_flipBorder"))
                && SameInstance(setterBase, H.FindText("Issue965_setterBase"))
                && SameInstance(flyoutTarget, H.FindButton("Issue965_flyoutTarget"))
                && SameInstance(flyoutCollide, H.FindButton("Issue965_flyoutCollide"))
                && SameInstance(flyoutEqual, H.FindButton("Issue965_flyoutEqual"))
                && SameInstance(memoWrapper, FindMemoWrapper())
                && SameInstance(expander, FindExpander()));

            // The stale value was 8,0,4,0: the old inline-start edge read back as the base.
            H.Check("Issue965_Phase1_PadSwapDropsOldEdge", padSwap.Padding == new Thickness(0, 0, 4, 0));
            H.Check("Issue965_Phase1_MarginSwapDropsOldEdge", marginSwap.Margin == new Thickness(0, 0, 4, 0));
            // The stale value kept the old 8 on the left.
            H.Check("Issue965_Phase1_ButtonSwapDropsOldEdge",
                buttonSwap.Padding == new Thickness(0, 0, 4, 0));
            // The stale value was 8,0,8,0: the LTR overlay read back as the RTL base.
            H.Check("Issue965_Phase1_FlipPadRtl", flipPad.Padding == new Thickness(0, 0, 8, 0));
            H.Check("Issue965_Phase1_FlipMarginRtl", flipMargin.Margin == new Thickness(0, 0, 8, 0));
            H.Check("Issue965_Phase1_FlipBorderRtl", flipBorder.BorderThickness == new Thickness(0, 0, 9, 0));
            H.Check("Issue965_Phase1_SetterBaseFollowsSetter", setterBase.Padding == new Thickness(1, 9, 9, 9));
            H.Check("Issue965_Phase1_FlipDeclaredRtl", flipDeclared.Padding == new Thickness(5, 0, 5, 0));
            // Sharing one record, the decorator re-adopted the target's old 8 as its base.
            H.Check("Issue965_Phase1_FlyoutTargetEdgeUpdated", flyoutTarget.Margin == new Thickness(6, 0, 4, 0));
            // Taking the target's record as its own, the decorator lost the target's right edge
            // (8,9,9,9).
            H.Check("Issue965_Phase1_FlyoutCollideKeepsTargetEdge", flyoutCollide.Margin == new Thickness(8, 9, 4, 9));
            // Trusting its remembered base, the decorator dropped the target's new 8 (0,0,4,0).
            H.Check("Issue965_Phase1_FlyoutEqualKeepsTargetMargin", flyoutEqual.Margin == new Thickness(8, 0, 4, 0));
            H.Check("Issue965_Phase1_MemoWrapperDropsOldEdge", memoWrapper.Margin == new Thickness(0, 0, 4, 0));
            H.Check("Issue965_Phase1_ExpanderRtl", expander.Padding == new Thickness(5, 0, 5, 0));

            H.ClickButton("Issue965_Next");
            await Harness.Render();

            H.Check("Issue965_Phase2_SameInstances",
                SameInstance(flipPad, H.FindText("Issue965_flipPad"))
                && SameInstance(flipMargin, H.FindText("Issue965_flipMargin"))
                && SameInstance(flipBorder, FindBorder("Issue965_flipBorder"))
                && SameInstance(flipDeclared, H.FindText("Issue965_flipDeclared"))
                && SameInstance(expander, FindExpander()));
            H.Check("Issue965_Phase2_FlipPadBackToLtr", flipPad.Padding == new Thickness(8, 0, 0, 0));
            H.Check("Issue965_Phase2_FlipMarginBackToLtr", flipMargin.Margin == new Thickness(8, 0, 0, 0));
            H.Check("Issue965_Phase2_FlipBorderBackToLtr", flipBorder.BorderThickness == new Thickness(9, 0, 0, 0));
            H.Check("Issue965_Phase2_FlipDeclaredBackToLtr", flipDeclared.Padding == new Thickness(5, 0, 9, 0));
            H.Check("Issue965_Phase2_ExpanderBackToLtr", expander.Padding == new Thickness(5, 0, 9, 0));
        }
    }
}
