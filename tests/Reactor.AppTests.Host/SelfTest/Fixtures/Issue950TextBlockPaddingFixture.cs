using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression for https://github.com/microsoft/microsoft-ui-reactor/issues/950
///
/// <c>Reconciler.ApplyModifiers</c> wrote the common <c>Padding</c> modifier to
/// <c>Control</c> / <c>Border</c> / <c>StackPanel</c> only. A <c>WinUI.TextBlock</c>
/// derives from <c>FrameworkElement</c>, so every arm missed and
/// <c>TextBlock("x").Padding(24)</c> compiled, ran, and silently discarded the value —
/// even though WinUI declares <c>TextBlock.PaddingProperty</c>.
///
/// The same gate feeds the BiDi <c>basePad</c> fallback, so <c>.PaddingInlineStart</c> /
/// <c>.PaddingInlineEnd</c> inherited the blind spot.
///
/// These checks cannot live in <c>Reactor.Tests</c>: reading <c>TextBlock.Padding</c>
/// back requires constructing a live WinUI control, which throws <c>COMException</c> in
/// the headless host.
/// </summary>
internal static class Issue950TextBlockPaddingFixture
{
    /// <summary>
    /// Mount / update / unset on one recycled control. The unpadded sibling rendered
    /// alongside is the differential: it fails if the new arm writes padding
    /// unconditionally rather than from the modifier.
    /// </summary>
    internal class PaddingMountUpdateUnset(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (step, setStep) = ctx.UseState(0);
                var padded = step switch
                {
                    0 => TextBlock("Issue950_padded").Padding(24),
                    1 => TextBlock("Issue950_padded").Padding(1, 2, 3, 4),
                    2 => TextBlock("Issue950_padded").Padding(8),
                    _ => TextBlock("Issue950_padded"),
                };
                return VStack(
                    Button("Issue950_Next", () => setStep(step + 1)),
                    padded,
                    TextBlock("Issue950_bare"));
            });

            await Harness.Render();

            var padded = H.FindText("Issue950_padded");
            var bare = H.FindText("Issue950_bare");
            H.Check("Issue950_Mounted", padded is not null && bare is not null);
            if (padded is null || bare is null) return;

            // The bug: this was Thickness(0) before the TextBlock arm existed.
            H.Check("Issue950_Mount_PaddingApplied", padded.Padding == new Thickness(24));

            // Differential sibling — same control type, same render pass, no modifier.
            // Without it, an arm that hard-wrote a padding would still pass above.
            H.Check("Issue950_Mount_SiblingWithoutModifierIsUnpadded",
                bare.Padding == new Thickness(0));

            H.ClickButton("Issue950_Next");
            await Harness.Render();

            // Assert identity FIRST so the checks below are provably the update path
            // (ApplyModifiers with a non-null oldM) and not a fresh mount.
            H.Check("Issue950_Update_SameControl",
                ReferenceEquals(padded, H.FindText("Issue950_padded")));

            // Asymmetric, so an arm that writes the right type but the wrong value fails.
            H.Check("Issue950_Update_AsymmetricThicknessRoundTrips",
                padded.Padding == new Thickness(1, 2, 3, 4));

            H.ClickButton("Issue950_Next");
            await Harness.Render();

            H.Check("Issue950_Update_NarrowsToUniform", padded.Padding == new Thickness(8));

            H.ClickButton("Issue950_Next");
            await Harness.Render();

            // The unset arm must ClearValue, not write a local Thickness(0): a local zero
            // would shadow a style/template value. Reading the raw local value is the only
            // way to tell the two apart — Padding itself is Thickness(0) either way.
            H.Check("Issue950_Unset_LocalValueCleared",
                ReferenceEquals(
                    DependencyProperty.UnsetValue,
                    padded.ReadLocalValue(WinUI.TextBlock.PaddingProperty)));
            H.Check("Issue950_Unset_PaddingIsZero", padded.Padding == new Thickness(0));
        }
    }

    /// <summary>
    /// The BiDi half of the bug. <c>.PaddingInlineStart</c> resolves against the control's
    /// live <c>FlowDirection</c> inside <c>ApplyModifiers</c>, so it is fixed by the gate
    /// widening and would NOT have been fixed by a descriptor-level Padding entry (which
    /// reads the raw <c>Padding</c> slot).
    /// </summary>
    internal class InlinePaddingResolvesPerFlowDirection(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            host.Mount(_ => VStack(
                TextBlock("Issue950_ltr")
                    .Set(tb => tb.FlowDirection = FlowDirection.LeftToRight)
                    .PaddingInlineStart(8),
                TextBlock("Issue950_rtl")
                    .Set(tb => tb.FlowDirection = FlowDirection.RightToLeft)
                    .PaddingInlineStart(8),
                // Padding written by a .Set escapes the modifier bag entirely, so
                // resolvedPadding is null here and the inline overlay has to read the
                // control's own Padding for the edges it does not own. That read is the
                // `basePad` ternary — the third site the gate widening touched.
                TextBlock("Issue950_overlay")
                    .Set(tb => tb.Padding = new Thickness(5, 6, 7, 8))
                    .PaddingInlineStart(1),
                // InlineEnd maps to the opposite physical edge from InlineStart, so a
                // resolution that ignored `m.PaddingInlineEnd` would still pass every
                // InlineStart check above.
                TextBlock("Issue950_end_ltr")
                    .Set(tb => tb.FlowDirection = FlowDirection.LeftToRight)
                    .PaddingInlineEnd(6),
                TextBlock("Issue950_end_rtl")
                    .Set(tb => tb.FlowDirection = FlowDirection.RightToLeft)
                    .PaddingInlineEnd(6),
                // basePad preservation under RTL: the surviving edge is the *left* one here,
                // which is the edge the LTR overlay case overwrites — so this fails if the
                // isRtl branch of the left/right resolution is wrong in either direction.
                TextBlock("Issue950_overlay_rtl")
                    .Set(tb =>
                    {
                        tb.FlowDirection = FlowDirection.RightToLeft;
                        tb.Padding = new Thickness(5, 6, 7, 8);
                    })
                    .PaddingInlineStart(1)));

            await Harness.Render();

            var ltr = H.FindText("Issue950_ltr");
            var rtl = H.FindText("Issue950_rtl");
            var overlay = H.FindText("Issue950_overlay");
            var endLtr = H.FindText("Issue950_end_ltr");
            var endRtl = H.FindText("Issue950_end_rtl");
            var overlayRtl = H.FindText("Issue950_overlay_rtl");
            H.Check("Issue950_BiDi_Mounted",
                ltr is not null && rtl is not null && overlay is not null
                && endLtr is not null && endRtl is not null && overlayRtl is not null);
            if (ltr is null || rtl is null || overlay is null
                || endLtr is null || endRtl is null || overlayRtl is null) return;

            H.Check("Issue950_BiDi_LtrInlineStartIsLeft", ltr.Padding == new Thickness(8, 0, 0, 0));
            H.Check("Issue950_BiDi_RtlInlineStartIsRight", rtl.Padding == new Thickness(0, 0, 8, 0));
            H.Check("Issue950_BiDi_LtrInlineEndIsRight", endLtr.Padding == new Thickness(0, 0, 6, 0));
            H.Check("Issue950_BiDi_RtlInlineEndIsLeft", endRtl.Padding == new Thickness(6, 0, 0, 0));

            // Left replaced by the inline value; top/right/bottom survive from the control.
            // If basePad fell through to `new Thickness()` this would be (1, 0, 0, 0).
            H.Check("Issue950_BiDi_OverlayPreservesUnownedEdges",
                overlay.Padding == new Thickness(1, 6, 7, 8));

            // Mirror image: right is the owned edge, left/top/bottom survive.
            H.Check("Issue950_BiDi_RtlOverlayPreservesUnownedEdges",
                overlayRtl.Padding == new Thickness(5, 6, 1, 8));
        }
    }

    /// <summary>
    /// Pool recycle. Mount calls <c>ApplyModifiers(fe, oldM: null, …)</c>, so the unset arm
    /// can never fire on a freshly rented control — without a matching reset in
    /// <c>ElementPool.CleanElement</c> the padding leaks into the next element that sets none.
    /// </summary>
    internal class PaddingDoesNotLeakAcrossPoolReuse(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                var next = Button("Issue950_Pool_Next", () => setPhase(phase + 1));
                return phase switch
                {
                    0 => VStack(next, TextBlock("Issue950_pooled").Padding(16)),
                    1 => VStack(next),
                    _ => VStack(next, TextBlock("Issue950_recycled")),
                };
            });

            await Harness.Render();
            var first = H.FindText("Issue950_pooled");
            H.Check("Issue950_Pool_FirstMountPadded",
                first is not null && first.Padding == new Thickness(16));

            H.ClickButton("Issue950_Pool_Next");
            await Harness.Render();

            H.ClickButton("Issue950_Pool_Next");
            await Harness.Render();

            var second = H.FindText("Issue950_recycled");
            // The whole point of the check below is that this IS the same control.
            H.Check("Issue950_Pool_ReusedInstance",
                first is not null && ReferenceEquals(first, second));
            H.Check("Issue950_Pool_PaddingResetOnRent",
                second is not null && second.Padding == new Thickness(0));
            H.Check("Issue950_Pool_PaddingLocalClearedOnRent",
                second is not null
                    && ReferenceEquals(
                        DependencyProperty.UnsetValue,
                        second.ReadLocalValue(WinUI.TextBlock.PaddingProperty)));
        }
    }

    /// <summary>
    /// The reset arm was dead code before this change: it tested
    /// <c>!resolvedPadding.HasValue &amp;&amp; oldM?.Padding.HasValue == true</c>, and
    /// <c>resolvedPadding = m.Padding ?? oldM?.Padding</c> is null only when <c>oldM</c> had no
    /// padding either — a contradiction. Dropping <c>.Padding(...)</c> therefore left the last
    /// value stuck on the control. These checks pin the repair, and that it clears rather than
    /// writing a local zero.
    /// </summary>
    internal class PaddingUnsetClearsInsteadOfZeroing(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (padded, setPadded) = ctx.UseState(true);
                var button = Button("Issue950_ThemedTarget", () => { });
                var inline = TextBlock("Issue950_inline_only");
                return VStack(
                    Button("Issue950_Unset_Toggle", () => setPadded(!padded)),
                    padded ? button.Padding(30) : button,
                        // Never carries the modifier — its padding is whatever the Button style
                        // supplies, which is the value the toggled button must fall back to.
                        Button("Issue950_ThemedReference", () => { }),
                        // oldM.Padding is null on this one — only the inline modifier was ever set —
                        // so a reset guarded on `oldM?.Padding.HasValue` would still miss it.
                        padded ? inline.PaddingInlineStart(9) : inline);
                });

                await Harness.Render();

                var btn = H.FindControl<WinUI.Button>(b => (b.Content as string) == "Issue950_ThemedTarget");
                var reference = H.FindControl<WinUI.Button>(b => (b.Content as string) == "Issue950_ThemedReference");
                var inlineTb = H.FindText("Issue950_inline_only");
                H.Check("Issue950_Unset_Mounted", btn is not null && reference is not null && inlineTb is not null);
                if (btn is null || reference is null || inlineTb is null) return;

                // Guards the fallback check below from being vacuous: if the Button style supplied
                // zero padding, "cleared" and "written as a local zero" would be indistinguishable.
                var themed = reference.Padding;
                H.Check("Issue950_Unset_ThemedPaddingIsNonZero", themed != new Thickness(0));

                H.Check("Issue950_Unset_ControlPaddingApplied", btn.Padding == new Thickness(30));
                H.Check("Issue950_Unset_InlinePaddingApplied", inlineTb.Padding == new Thickness(9, 0, 0, 0));

            H.ClickButton("Issue950_Unset_Toggle");
            await Harness.Render();

            H.Check("Issue950_Unset_ControlSameInstance",
                ReferenceEquals(btn, H.FindControl<WinUI.Button>(b => (b.Content as string) == "Issue950_ThemedTarget")));

            // Before the repair this stayed Thickness(30) forever.
            H.Check("Issue950_Unset_ControlLocalValueCleared",
                ReferenceEquals(
                    DependencyProperty.UnsetValue,
                    btn.ReadLocalValue(WinUI.Control.PaddingProperty)));

            // ClearValue, not `new Thickness(0)`: the Button style's padding must come back.
            // This is the check that distinguishes the two resets — Padding reads Thickness(0)
            // under a local zero and the themed value under a clear.
            H.Check("Issue950_Unset_ControlFallsBackToThemedPadding", btn.Padding == themed);

            // Same reason as the StackPanel pin below: TextBlock.Padding is released by
            // ElementPool.CleanElement, so a stale `inlineTb` would read UnsetValue because
            // the pool cleaned it — indistinguishable from the unset arm having cleared it
            // on the live control. Without this, the next check cannot fail for its reason.
            H.Check("Issue950_Unset_InlineSameInstance",
                ReferenceEquals(inlineTb, H.FindText("Issue950_inline_only")));

            H.Check("Issue950_Unset_InlineOnlyLocalValueCleared",
                ReferenceEquals(
                    DependencyProperty.UnsetValue,
                    inlineTb.ReadLocalValue(WinUI.TextBlock.PaddingProperty)));
        }
    }

    /// <summary>
    /// Pins the ownership rule that making the Padding reset arm reachable exposes: when a
    /// render drops <c>.Padding(...)</c> and writes the property through <c>.Set(...)</c>
    /// instead, the reset wins and the <c>.Set</c> value is discarded.
    /// <para>
    /// <c>DescriptorHandler</c> runs <c>ApplySetters</c> before <c>ApplyModifiers</c>, so the
    /// escape-hatch write lands first and the modifier reset clears it. That is not specific
    /// to Padding — <c>Width</c>, <c>Height</c>, <c>MinWidth</c> and <c>RequestedTheme</c> have
    /// always behaved this way, and the Width arm below holds them to the same answer. If a
    /// future change decides <c>.Set</c> should win, it has to decide that for the whole
    /// family, not for Padding alone. <c>REACTOR_MOD_002</c> already steers callers off this
    /// pattern toward the first-class modifier.
    /// </para>
    /// </summary>
    internal class ModifierResetOutranksASetterWrite(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (viaModifier, setViaModifier) = ctx.UseState(true);
                var contested = TextBlock("Issue950_Contested");
                return VStack(
                    Button("Issue950_Ownership_Toggle", () => setViaModifier(!viaModifier)),
                    viaModifier
                        ? contested.Padding(30)
                        : contested.Set(tb => tb.Padding = new Thickness(5)),
                    // Never carries the modifier, so the reset arm never runs on it. Without
                    // this arm the check below could pass simply because `.Set` never works.
                    TextBlock("Issue950_SetterOnly").Set(tb => tb.Padding = new Thickness(5)),
                    // Same transition on a sibling modifier that has always had a live reset.
                    viaModifier
                        ? Button("Issue950_WidthTarget", () => { }).Width(120)
                        : Button("Issue950_WidthTarget", () => { }).Set(b => b.Width = 55));
            });

            await Harness.Render();

            var contestedTb = H.FindText("Issue950_Contested");
            var setterOnly = H.FindText("Issue950_SetterOnly");
            var widthBtn = H.FindControl<WinUI.Button>(b => (b.Content as string) == "Issue950_WidthTarget");
            H.Check("Issue950_Ownership_Mounted",
                contestedTb is not null && setterOnly is not null && widthBtn is not null);
            if (contestedTb is null || setterOnly is null || widthBtn is null) return;

            H.Check("Issue950_Ownership_ModifierAppliedFirst", contestedTb.Padding == new Thickness(30));
            H.Check("Issue950_Ownership_SetterOnlyApplied", setterOnly.Padding == new Thickness(5));
            // Tolerance rather than `== 120`: the value round-trips through a double DP.
            // 1e-9 is deliberately tiny — this must still fail on the 55 the `.Set` arm
            // writes and on the NaN the reset produces, or the differential below is vacuous.
            H.Check("Issue950_Ownership_WidthModifierApplied", Math.Abs(widthBtn.Width - 120d) < 1e-9);

            H.ClickButton("Issue950_Ownership_Toggle");
            await Harness.Render();

            H.Check("Issue950_Ownership_SameInstance",
                ReferenceEquals(contestedTb, H.FindText("Issue950_Contested")));

            // The `.Set` wrote Thickness(5); the reset arm then cleared it.
            H.Check("Issue950_Ownership_ResetOutranksSetter",
                ReferenceEquals(
                    DependencyProperty.UnsetValue,
                    contestedTb.ReadLocalValue(WinUI.TextBlock.PaddingProperty)));
            H.Check("Issue950_Ownership_ClearedNotSetterValue", contestedTb.Padding != new Thickness(5));

            // Differential: `.Set` on its own is untouched, so the clear above is caused by the
            // dropped modifier and not by setters failing to run at all.
            H.Check("Issue950_Ownership_SetterOnlySurvives",
                H.FindText("Issue950_SetterOnly")?.Padding == new Thickness(5));

            // Width answers identically, which is the whole justification for the Padding answer.
            H.Check("Issue950_Ownership_WidthResetOutranksSetter",
                double.IsNaN(H.FindControl<WinUI.Button>(b => (b.Content as string) == "Issue950_WidthTarget")?.Width ?? 0));
        }
    }

    /// <summary>
    /// The reset arm has four type branches and the fixture above only reaches the
    /// <c>Control</c> and <c>TextBlock</c> ones. These cover the other two, plus the
    /// <c>PaddingInlineEnd</c>-only case that <c>hadPadding</c> has to notice — dropping
    /// <c>m.PaddingInlineEnd</c> from that expression leaves the overlay stuck on the control.
    /// <para>
    /// <c>Border</c> and <c>StackPanel</c> both default to zero padding, so reading
    /// <c>.Padding</c> back cannot distinguish "cleared" from "written as a local zero" the
    /// way the themed <c>Button</c> case can. <c>ReadLocalValue</c> is the distinguishing
    /// probe: it returns <c>UnsetValue</c> only after a real <c>ClearValue</c>.
    /// </para>
    /// </summary>
    internal class PaddingUnsetClearsOnEveryGatedType(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (padded, setPadded) = ctx.UseState(true);
                var border = Border(TextBlock("Issue950_AllTypes_BorderChild"));
                var stack = VStack(TextBlock("Issue950_AllTypes_StackChild"));
                var inlineEnd = TextBlock("Issue950_AllTypes_InlineEnd");
                return VStack(
                    Button("Issue950_AllTypes_Toggle", () => setPadded(!padded)),
                    padded ? border.Padding(21) : border,
                    padded ? stack.Padding(22) : stack,
                    padded ? inlineEnd.PaddingInlineEnd(23) : inlineEnd);
            });

            await Harness.Render();

            var bdr = H.FindControl<WinUI.Border>(b =>
                b.Child is WinUI.TextBlock c && c.Text == "Issue950_AllTypes_BorderChild");
            var sp = H.FindControl<WinUI.StackPanel>(s =>
                s.Children.Count == 1 && s.Children[0] is WinUI.TextBlock c
                && c.Text == "Issue950_AllTypes_StackChild");
            var tb = H.FindText("Issue950_AllTypes_InlineEnd");
            H.Check("Issue950_AllTypes_Mounted", bdr is not null && sp is not null && tb is not null);
            if (bdr is null || sp is null || tb is null) return;

            H.Check("Issue950_AllTypes_BorderApplied", bdr.Padding == new Thickness(21));
            H.Check("Issue950_AllTypes_StackApplied", sp.Padding == new Thickness(22));
            H.Check("Issue950_AllTypes_InlineEndApplied", tb.Padding == new Thickness(0, 0, 23, 0));

            H.ClickButton("Issue950_AllTypes_Toggle");
            await Harness.Render();

            H.Check("Issue950_AllTypes_BorderSameInstance",
                ReferenceEquals(bdr, H.FindControl<WinUI.Border>(b =>
                    b.Child is WinUI.TextBlock c && c.Text == "Issue950_AllTypes_BorderChild")));

            H.Check("Issue950_AllTypes_BorderCleared",
                ReferenceEquals(
                    DependencyProperty.UnsetValue,
                    bdr.ReadLocalValue(WinUI.Border.PaddingProperty)));

            // Load-bearing, not decorative: StackPanel.Padding is now released by
            // ElementPool.CleanElement (#985). That makes the absence check below
            // fail-open on its own — a recycled `sp` reads UnsetValue because the
            // pool cleaned it, which is indistinguishable from the unset arm having
            // cleared it on the live control. Pinning identity first is what keeps
            // the next assertion a statement about ApplyModifiers.
            H.Check("Issue950_AllTypes_StackSameInstance",
                ReferenceEquals(sp, H.FindControl<WinUI.StackPanel>(s =>
                    s.Children.Count == 1 && s.Children[0] is WinUI.TextBlock c
                    && c.Text == "Issue950_AllTypes_StackChild")));
            H.Check("Issue950_AllTypes_StackCleared",
                ReferenceEquals(
                    DependencyProperty.UnsetValue,
                    sp.ReadLocalValue(WinUI.StackPanel.PaddingProperty)));

            // Pins identity for the same reason as the StackPanel check above — TextBlock.Padding
            // is pool-released, so a recycled `tb` reads UnsetValue regardless of the unset arm.
            H.Check("Issue950_AllTypes_TextSameInstance",
                ReferenceEquals(tb, H.FindText("Issue950_AllTypes_InlineEnd")));

            // oldM.Padding is null on this one — only PaddingInlineEnd was ever set — so a
            // `hadPadding` that forgot the InlineEnd slot would leave (0,0,23,0) in place.
            H.Check("Issue950_AllTypes_InlineEndCleared",
                ReferenceEquals(
                    DependencyProperty.UnsetValue,
                    tb.ReadLocalValue(WinUI.TextBlock.PaddingProperty)));
        }
    }

    /// <summary>
    /// The neighbouring <c>RichTextBlock</c> reaches Padding through its descriptor's
    /// <c>Customize</c> hook, not through <c>ApplyModifiers</c>. Widening the reconciler
    /// gate must leave that path exactly as it was — including the fact that
    /// <c>NoOpModifierAnalyzer.DescriptorAppliedModifiers</c> still exempts it.
    /// </summary>
    internal class RichTextBlockPaddingStillFlowsThroughItsDescriptor(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (padded, setPadded) = ctx.UseState(true);
                var block = RichTextBlock("Issue950_rich");
                return VStack(
                    Button("Issue950_Rich_Toggle", () => setPadded(!padded)),
                    padded ? block.Padding(3, 4, 5, 6) : block);
            });

            await Harness.Render();

            var rtb = H.FindControl<WinUI.RichTextBlock>(_ => true);
            H.Check("Issue950_RichTextBlock_Mounted", rtb is not null);
            if (rtb is null) return;

            H.Check("Issue950_RichTextBlock_PaddingApplied", rtb.Padding == new Thickness(3, 4, 5, 6));

            H.ClickButton("Issue950_Rich_Toggle");
            await Harness.Render();

            H.Check("Issue950_RichTextBlock_SameControl",
                ReferenceEquals(rtb, H.FindControl<WinUI.RichTextBlock>(_ => true)));
            H.Check("Issue950_RichTextBlock_PaddingCleared",
                ReferenceEquals(
                    DependencyProperty.UnsetValue,
                    rtb.ReadLocalValue(WinUI.RichTextBlock.PaddingProperty)));
        }
    }
}
