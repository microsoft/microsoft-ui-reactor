using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression for https://github.com/microsoft/microsoft-ui-reactor/issues/522
///
/// When the same recycled <c>TextBlock</c> control transitions between an
/// element that sets a styling prop (e.g. <c>Heading</c> → FontSize=28,
/// Weight=700) and one that does not (plain <c>TextBlock</c>), the
/// descriptor's <c>OneWayConditional</c> entry used to return early on
/// <c>!shouldWrite(newEl)</c> and never reset the dependency property —
/// so the old FontSize/Weight bled into the new render.
///
/// A second arm covers the matching <c>.Foreground(ThemeRef(...))</c> bleed:
/// <c>Reconciler.Update</c> only applied <c>ThemeBindings</c> when the
/// <i>new</i> element had bindings, so a previously-applied themed Style
/// remained on the control after a transition back to "no bindings".
/// </summary>
internal static class Issue522TextBlockStyleResetFixture
{
    /// <summary>
    /// Heading → plain TextBlock recycles the same control; FontSize / Weight
    /// must reset (local DP cleared) so the plain TextBlock renders at the
    /// theme defaults.
    /// </summary>
    internal class HeadingToPlainResetsFontProps(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            Action<bool>? setShowHeading = null;
            host.Mount(ctx =>
            {
                var (showHeading, setter) = ctx.UseState(true);
                setShowHeading = setter;
                return showHeading
                    ? Heading("Loading url...")
                    : TextBlock("Valid URL: https://example.com/");
            });

            await Harness.Render();

            var heading = H.FindText("Loading url...");
            H.Check("Issue522_HeadingMounted", heading is not null);
            H.Check("Issue522_HeadingHasFontSize28",
                heading is not null && Math.Abs(heading.FontSize - 28) < 0.01);
            H.Check("Issue522_HeadingHasWeight700",
                heading is not null && heading.FontWeight.Weight == 700);

            // Capture the WinUI control reference so we can assert directly that
            // the recycler reset its DPs (vs. having mounted a new control).
            var headingControl = heading;

            setShowHeading!(false);
            await Harness.Render();

            var plain = H.FindText("Valid URL: https://example.com/");
            H.Check("Issue522_PlainMounted", plain is not null);

            // Same control reused (recycler hit) — verifies we're testing the
            // descriptor-update path, not a fresh mount.
            H.Check("Issue522_SameControlReused",
                plain is not null && ReferenceEquals(plain, headingControl));

            // The actual bug: FontSize / FontWeight from the prior render
            // must not bleed into the plain TextBlock. Assert the local DP
            // is cleared (UnsetValue) — that's the contract for "no value
            // → fall back to theme default".
            H.Check("Issue522_FontSizeLocalCleared",
                plain is not null
                    && ReferenceEquals(
                        DependencyProperty.UnsetValue,
                        plain.ReadLocalValue(WinUI.TextBlock.FontSizeProperty)));
            H.Check("Issue522_FontWeightLocalCleared",
                plain is not null
                    && ReferenceEquals(
                        DependencyProperty.UnsetValue,
                        plain.ReadLocalValue(WinUI.TextBlock.FontWeightProperty)));
        }
    }

    /// <summary>
    /// Toggling between two stylings (FontSize=28/Weight=700 ↔ FontSize=12/no Weight
    /// ↔ none) must end at the theme default, not retain the most recent set value.
    /// </summary>
    internal class StylingChainReturnsToDefault(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            Action<int>? setIdx = null;
            host.Mount(ctx =>
            {
                var (idx, setter) = ctx.UseState(0);
                setIdx = setter;
                return idx switch
                {
                    0 => Heading("step"),
                    1 => Caption("step"),
                    _ => TextBlock("step"),
                };
            });

            await Harness.Render();
            var step0 = H.FindText("step");
            H.Check("Issue522_Chain_Step0_FontSize28",
                step0 is not null && Math.Abs(step0.FontSize - 28) < 0.01);

            setIdx!(1);
            await Harness.Render();
            var step1 = H.FindText("step");
            H.Check("Issue522_Chain_Step1_FontSize12",
                step1 is not null && Math.Abs(step1.FontSize - 12) < 0.01);
            H.Check("Issue522_Chain_Step1_WeightCleared_AfterHeading",
                step1 is not null
                    && ReferenceEquals(
                        DependencyProperty.UnsetValue,
                        step1.ReadLocalValue(WinUI.TextBlock.FontWeightProperty)));

            setIdx!(2);
            await Harness.Render();
            var step2 = H.FindText("step");
            H.Check("Issue522_Chain_Step2_FontSizeCleared",
                step2 is not null
                    && ReferenceEquals(
                        DependencyProperty.UnsetValue,
                        step2.ReadLocalValue(WinUI.TextBlock.FontSizeProperty)));
        }
    }

    /// <summary>
    /// The <c>.Foreground(ThemeRef(...))</c> path applies a cached Style; when
    /// the next render drops the theme binding the recycler must clear that
    /// Style on the same recycled control (in-place — no full remount so
    /// subtree state is preserved). See <c>Reconciler.ClearThemeBindings</c>.
    /// </summary>
    internal class ThemeForegroundClearedOnRemoval(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            Action<bool>? setShowError = null;
            host.Mount(ctx =>
            {
                var (showError, setter) = ctx.UseState(true);
                setShowError = setter;
                return showError
                    ? TextBlock("payload").Foreground(new ThemeRef("SystemFillColorCriticalBrush"))
                    : TextBlock("payload");
            });

            await Harness.Render();

            var errorTb = H.FindText("payload");
            H.Check("Issue522_Theme_ErrorMounted", errorTb is not null);
            H.Check("Issue522_Theme_ErrorHasStyleApplied",
                errorTb is not null && errorTb.Style is not null);

            var priorControl = errorTb;

            setShowError!(false);
            await Harness.Render();

            var plainTb = H.FindText("payload");
            H.Check("Issue522_Theme_PlainMounted", plainTb is not null);

            // In-place clear: the same recycled control should be reused, NOT
            // remounted. Forcing a fresh mount on every ThemeBindings removal
            // would needlessly lose subtree state for container elements (e.g.
            // .Background(ThemeRef(...)) on a VStack wrapping a form).
            H.Check("Issue522_Theme_SameControlReused",
                plainTb is not null && ReferenceEquals(plainTb, priorControl));

            // The synthesized themed Style must be cleared so the Foreground
            // falls back to the theme default.
            H.Check("Issue522_Theme_StyleClearedAfterRemoval",
                plainTb is not null && plainTb.Style is null);
            H.Check("Issue522_Theme_ForegroundLocalCleared",
                plainTb is not null
                    && ReferenceEquals(
                        DependencyProperty.UnsetValue,
                        plainTb.ReadLocalValue(WinUI.TextBlock.ForegroundProperty)));
        }
    }

    /// <summary>
    /// End-to-end repro mirroring the user's failing app (issue body): drive an
    /// <c>AsyncValue&lt;T&gt;.Match</c> through Loading → Data → Error → Loading
    /// → Data via <c>UseResource</c>, and check that the recycled TextBlock
    /// ends each transition at the styling its current arm declared — never
    /// carrying FontSize / FontWeight / Foreground over from a previous arm.
    /// </summary>
    internal class UseResourceMatchTransitionsNoStyleBleed(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var cache = new QueryCache();
            // Sequence of TCS so we can step the resource through its states.
            var step1 = new TaskCompletionSource<string>();
            var step2 = new TaskCompletionSource<string>();
            var step3 = new TaskCompletionSource<string>();
            Action<int>? bumpDep = null;

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (d, setDep) = ctx.UseState(0);
                bumpDep = setDep;
                var value = ctx.UseResource(_ => d switch
                {
                    0 => step1.Task,
                    1 => step2.Task,
                    _ => step3.Task,
                }, cache, new object[] { d });

                return value.Match<Element>(
                    loading: () => Heading("Loading url..."),
                    data: (s) => TextBlock("Valid URL: " + s),
                    error: (e) => TextBlock("Error: " + e.Message).Foreground(new ThemeRef("SystemFillColorCriticalBrush")),
                    reloading: (s) => Heading("Reloading url..."));
            });

            await Harness.Render();

            // ── Initial: Loading (Heading) ──
            var loading = H.FindText("Loading url...");
            H.Check("Issue522_E2E_LoadingMounted", loading is not null);
            H.Check("Issue522_E2E_LoadingHasHeadingFontSize",
                loading is not null && Math.Abs(loading.FontSize - 28) < 0.01);

            // ── Loading → Data ──
            step1.SetResult("https://example.com/");
            await Harness.Render();

            var data = H.FindTextContaining("Valid URL:");
            H.Check("Issue522_E2E_DataMounted", data is not null);
            H.Check("Issue522_E2E_DataFontSizeReset",
                data is not null
                    && ReferenceEquals(
                        DependencyProperty.UnsetValue,
                        data.ReadLocalValue(WinUI.TextBlock.FontSizeProperty)));
            H.Check("Issue522_E2E_DataFontWeightReset",
                data is not null
                    && ReferenceEquals(
                        DependencyProperty.UnsetValue,
                        data.ReadLocalValue(WinUI.TextBlock.FontWeightProperty)));

            // ── Trigger a new dep → Loading again, then resolve to Error ──
            bumpDep!(1);
            await Harness.Render();
            step2.SetException(new InvalidOperationException("bad uri"));
            await Harness.Render();

            var error = H.FindTextContaining("Error:");
            H.Check("Issue522_E2E_ErrorMounted", error is not null);
            H.Check("Issue522_E2E_ErrorHasThemedStyle",
                error is not null && error.Style is not null);

            // ── Bump dep again → Loading (Heading) → Data ──
            bumpDep!(2);
            await Harness.Render();

            var loading2 = H.FindText("Loading url...");
            H.Check("Issue522_E2E_LoadingAfterError_Mounted", loading2 is not null);
            // The Error arm's themed Style must not survive into Loading's Heading.
            H.Check("Issue522_E2E_LoadingAfterError_StyleCleared",
                loading2 is not null && loading2.Style is null);
            H.Check("Issue522_E2E_LoadingAfterError_ForegroundCleared",
                loading2 is not null
                    && ReferenceEquals(
                        DependencyProperty.UnsetValue,
                        loading2.ReadLocalValue(WinUI.TextBlock.ForegroundProperty)));
            H.Check("Issue522_E2E_LoadingAfterError_FontSize28",
                loading2 is not null && Math.Abs(loading2.FontSize - 28) < 0.01);

            step3.SetResult("https://example.com/ok");
            await Harness.Render();

            var data2 = H.FindTextContaining("Valid URL:");
            H.Check("Issue522_E2E_FinalDataMounted", data2 is not null);
            // Final data arm must have no stale styling from either Heading or Error arms.
            H.Check("Issue522_E2E_FinalData_FontSizeCleared",
                data2 is not null
                    && ReferenceEquals(
                        DependencyProperty.UnsetValue,
                        data2.ReadLocalValue(WinUI.TextBlock.FontSizeProperty)));
            H.Check("Issue522_E2E_FinalData_FontWeightCleared",
                data2 is not null
                    && ReferenceEquals(
                        DependencyProperty.UnsetValue,
                        data2.ReadLocalValue(WinUI.TextBlock.FontWeightProperty)));
            H.Check("Issue522_E2E_FinalData_StyleCleared",
                data2 is not null && data2.Style is null);
            H.Check("Issue522_E2E_FinalData_ForegroundCleared",
                data2 is not null
                    && ReferenceEquals(
                        DependencyProperty.UnsetValue,
                        data2.ReadLocalValue(WinUI.TextBlock.ForegroundProperty)));
        }
    }

    /// <summary>
    /// Regression: an earlier draft of the fix forced a fresh Mount whenever
    /// ThemeBindings transitioned set → unset, which would tear down the entire
    /// subtree of a themed container element (e.g. <c>.Background(ThemeRef(...))</c>
    /// on a Border wrapping a form). This fixture pins the in-place clear by
    /// asserting the container control identity survives the transition. A
    /// real subtree-state assertion (preserving a UseState counter or focus)
    /// would require a deeper harness; control identity is the proxy.
    /// </summary>
    internal class ContainerThemeBindingTransition_PreservesControlIdentity(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            Action<bool>? setThemed = null;
            host.Mount(ctx =>
            {
                var (themed, setter) = ctx.UseState(true);
                setThemed = setter;
                var border = Border(TextBlock("inner-content"));
                return themed
                    ? border.Background(new ThemeRef("SystemFillColorCriticalBrush"))
                    : border;
            });

            await Harness.Render();

            var themedBorder = H.FindControl<Microsoft.UI.Xaml.Controls.Border>(_ => true);
            H.Check("Issue522_Container_ThemedMounted", themedBorder is not null);
            H.Check("Issue522_Container_HasStyle",
                themedBorder is not null && themedBorder.Style is not null);

            setThemed!(false);
            await Harness.Render();

            var plainBorder = H.FindControl<Microsoft.UI.Xaml.Controls.Border>(_ => true);
            H.Check("Issue522_Container_PlainMounted", plainBorder is not null);
            H.Check("Issue522_Container_BorderIdentityPreserved",
                plainBorder is not null && ReferenceEquals(plainBorder, themedBorder));
            H.Check("Issue522_Container_StyleCleared",
                plainBorder is not null && plainBorder.Style is null);
            H.Check("Issue522_Container_InnerTextStillPresent",
                H.FindText("inner-content") is not null);
        }
    }

    /// <summary>
    /// Three TextBlocks each have <c>.Foreground(ThemeRef("Critical"))</c>.
    /// Removing the theme from the middle one must NOT affect the other two.
    /// Pinned because <c>_styleCache</c> is content-addressed and a single
    /// Style instance is shared across all three controls — clearing one
    /// must not be observable on the siblings.
    /// </summary>
    internal class SharedStyleAcrossMultipleElements_IsolatedRemoval(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            Action<bool>? setMiddleThemed = null;
            host.Mount(ctx =>
            {
                var (middleThemed, setter) = ctx.UseState(true);
                setMiddleThemed = setter;
                Element themed(string t) => TextBlock(t).Foreground(new ThemeRef("SystemFillColorCriticalBrush"));
                Element plain(string t) => TextBlock(t);
                return FlexColumn(
                    themed("tb-a"),
                    middleThemed ? themed("tb-b") : plain("tb-b"),
                    themed("tb-c"));
            });

            await Harness.Render();

            var a0 = H.FindText("tb-a");
            var b0 = H.FindText("tb-b");
            var c0 = H.FindText("tb-c");
            H.Check("Issue522_Shared_AllMounted",
                a0 is not null && b0 is not null && c0 is not null);
            H.Check("Issue522_Shared_AllHaveStyle_Initial",
                a0?.Style is not null && b0?.Style is not null && c0?.Style is not null);
            // Style sharing — content-addressed cache means a single Style instance.
            H.Check("Issue522_Shared_StyleInstanceShared",
                a0 is not null && b0 is not null && c0 is not null
                    && ReferenceEquals(a0.Style, b0.Style)
                    && ReferenceEquals(b0.Style, c0.Style));

            setMiddleThemed!(false);
            await Harness.Render();

            var a1 = H.FindText("tb-a");
            var b1 = H.FindText("tb-b");
            var c1 = H.FindText("tb-c");
            // Sibling controls are reused (no remount cascade from a peer's transition).
            H.Check("Issue522_Shared_SiblingsReused",
                ReferenceEquals(a1, a0) && ReferenceEquals(c1, c0));
            // Sibling styles survive.
            H.Check("Issue522_Shared_SiblingAStyleSurvives", a1?.Style is not null);
            H.Check("Issue522_Shared_SiblingCStyleSurvives", c1?.Style is not null);
            // Middle gets cleared in place.
            H.Check("Issue522_Shared_MiddleReused", ReferenceEquals(b1, b0));
            H.Check("Issue522_Shared_MiddleStyleCleared", b1?.Style is null);
        }
    }

    /// <summary>
    /// A theme change (<c>ActualThemeChanged</c>) historically called
    /// <c>Reconciler.ClearStyleCache</c>, but that breaks <c>ClearThemeBindings</c>
    /// when a coincident ThemeBindings removal happens in the same render frame
    /// (the post-clear cache returns a different Style instance from the one
    /// currently on the control). This fixture simulates the sequence
    /// programmatically — clears the cache, then drives a transition away from
    /// theme bindings — and asserts the in-place clear still fires.
    /// </summary>
    internal class ThemeBindingsRemoval_AfterCacheClear_StillWorks(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            Action<bool>? setThemed = null;
            host.Mount(ctx =>
            {
                var (themed, setter) = ctx.UseState(true);
                setThemed = setter;
                return themed
                    ? TextBlock("cache-clear-victim").Foreground(new ThemeRef("SystemFillColorCriticalBrush"))
                    : TextBlock("cache-clear-victim");
            });

            await Harness.Render();
            var tb0 = H.FindText("cache-clear-victim");
            H.Check("Issue522_CacheClear_ThemedMounted",
                tb0 is not null && tb0.Style is not null);

            // Simulate the historical ClearStyleCache call. After this, the
            // next ApplyThemeBindings (if any) would create a NEW Style
            // instance for the same key. We're testing that the clear path
            // doesn't depend on the cache surviving across the transition.
            Microsoft.UI.Reactor.Core.Reconciler.ClearStyleCache();

            setThemed!(false);
            await Harness.Render();

            var tb1 = H.FindText("cache-clear-victim");
            H.Check("Issue522_CacheClear_SameControl",
                tb1 is not null && ReferenceEquals(tb1, tb0));
            H.Check("Issue522_CacheClear_StyleClearedDespiteCacheClear",
                tb1 is not null && tb1.Style is null);
        }
    }

    /// <summary>
    /// Cycle ThemeRef A → B → A on the same control. Catches the case where
    /// <c>ApplyStyleToElement</c> replaces the Style and any property that
    /// existed only in the previous Style's setters falls back correctly.
    /// </summary>
    internal class ThemeRef_CycleAcrossDifferentKeys(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            Action<int>? setStep = null;
            host.Mount(ctx =>
            {
                var (step, setter) = ctx.UseState(0);
                setStep = setter;
                // Use distinct ThemeRefs so each step is a different cache key.
                return step switch
                {
                    0 => TextBlock("cycle").Foreground(new ThemeRef("SystemFillColorCriticalBrush")),
                    1 => TextBlock("cycle").Foreground(new ThemeRef("SystemFillColorSuccessBrush")),
                    _ => TextBlock("cycle").Foreground(new ThemeRef("SystemFillColorCriticalBrush")),
                };
            });

            await Harness.Render();
            var step0 = H.FindText("cycle");
            var styleA = step0?.Style;
            H.Check("Issue522_Cycle_Step0_HasStyleA", styleA is not null);

            setStep!(1);
            await Harness.Render();
            var step1 = H.FindText("cycle");
            H.Check("Issue522_Cycle_Step1_SameControl",
                step1 is not null && ReferenceEquals(step1, step0));
            H.Check("Issue522_Cycle_Step1_DifferentStyle",
                step1?.Style is not null && !ReferenceEquals(step1.Style, styleA));
            var styleB = step1?.Style;

            setStep!(2);
            await Harness.Render();
            var step2 = H.FindText("cycle");
            H.Check("Issue522_Cycle_Step2_SameControl",
                step2 is not null && ReferenceEquals(step2, step0));
            // Returning to A: content-addressed cache returns the same Style
            // instance we used in step 0.
            H.Check("Issue522_Cycle_Step2_StyleA_Reused",
                step2?.Style is not null && ReferenceEquals(step2.Style, styleA));
            H.Check("Issue522_Cycle_Step2_NotStyleB",
                step2?.Style is not null && !ReferenceEquals(step2.Style, styleB));
        }
    }
}
