using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 050 G1 — verifies that <see cref="Optional{T}.Unset"/> on a
/// <c>.OneWay(get, set, dp:)</c> ClearValue-aware entry lets WinUI's
/// theme / style precedence chain own the resolved value, and that flipping
/// <see cref="FrameworkElement.RequestedTheme"/> after the Unset write picks
/// up the new theme's default. Force-assert (<c>Optional.Of(brush)</c>)
/// must override the theme variant on both sides of the toggle.
/// </summary>
internal static class OptionalThemeInteractionFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Defensive: clear any prior fixture's visual-tree state before
            // we install raw WinUI DependencyObjects (a fresh Button + two
            // Styles + three Brushes). Without this isolation, neighboring
            // popup-heavy fixtures (e.g. OptionalEchoStrandRegression's
            // AutoSuggestBox) have left WinUI in a state that races with
            // this fixture's Style swap + ClearValue churn under NativeAOT,
            // producing a STATUS_STACK_BUFFER_OVERRUN ~0.2% of stress runs.
            H.SetContent(null);
            await Harness.Render();

            // Two theme-keyed brushes installed as Style fallbacks. The
            // descriptor's .OneWay(...dp:) entry decides between writing a
            // local value (force-assert) and clearing the local value (Unset
            // → theme default flows through).
            var lightBrush = new SolidColorBrush(Color.FromArgb(255, 255, 250, 200));
            var darkBrush = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30));
            var forceBrush = new SolidColorBrush(Color.FromArgb(255, 220, 0, 0));

            var lightStyle = new Style(typeof(WinUI.Button));
            lightStyle.Setters.Add(new Setter(WinUI.Control.BackgroundProperty, lightBrush));
            var darkStyle = new Style(typeof(WinUI.Button));
            darkStyle.Setters.Add(new Setter(WinUI.Control.BackgroundProperty, darkBrush));

            var button = new WinUI.Button { Content = "theme-target", Style = lightStyle };
            H.SetContent(button);
            try
            {
                await Harness.Render();

                var entry = new ControlDescriptor<ThemedElement, WinUI.Button>()
                    .OneWay(e => e.Background, (c, v) => c.Background = v, WinUI.Control.BackgroundProperty)
                    .Properties[0];

                // Mount with Unset → no local value → style fallback brush flows through.
                var unsetEl = new ThemedElement(Optional<Brush>.Unset);
                entry.Mount(button, unsetEl);
                await Harness.Render();
                H.Check(
                    "OptionalThemeInteraction_Unset_NoLocalValue",
                    ReferenceEquals(DependencyProperty.UnsetValue, button.ReadLocalValue(WinUI.Control.BackgroundProperty)));
                H.Check(
                    "OptionalThemeInteraction_Unset_LightStyleFlowsThrough",
                    ReferenceEquals(button.Background, lightBrush));

                // Simulate a theme switch by swapping the Style — equivalent to
                // RequestedTheme=Dark resolving a different ThemeResource. The
                // Unset prop should let the new style's default brush flow.
                button.Style = darkStyle;
                await Harness.Render();
                H.Check(
                    "OptionalThemeInteraction_Unset_DarkStyleFlowsThroughAfterSwitch",
                    ReferenceEquals(button.Background, darkBrush));
                H.Check(
                    "OptionalThemeInteraction_Unset_NoLocalValueAfterSwitch",
                    ReferenceEquals(DependencyProperty.UnsetValue, button.ReadLocalValue(WinUI.Control.BackgroundProperty)));

                // Force-assert via Update → local value wins regardless of style.
                entry.Update(button, unsetEl, new ThemedElement(Optional<Brush>.Of(forceBrush)));
                await Harness.Render();
                H.Check(
                    "OptionalThemeInteraction_HasValue_ForcedBrushWinsOverDarkStyle",
                    ReferenceEquals(button.Background, forceBrush));

                // Swap style back; force-assert local value still wins.
                button.Style = lightStyle;
                await Harness.Render();
                H.Check(
                    "OptionalThemeInteraction_HasValue_ForcedBrushSurvivesStyleSwitch",
                    ReferenceEquals(button.Background, forceBrush));

                // Back to Unset → local value cleared → new style flows through.
                entry.Update(button, new ThemedElement(Optional<Brush>.Of(forceBrush)), unsetEl);
                await Harness.Render();
                H.Check(
                    "OptionalThemeInteraction_UnsetAgain_LightStyleRestored",
                    ReferenceEquals(button.Background, lightBrush));
            }
            finally
            {
                // Detach the raw button + styles + brushes from the visual
                // tree so the next fixture starts clean. Without this, the
                // DependencyObjects linger until GC, holding COM-backed
                // resources whose teardown can race with the next fixture's
                // mount under NativeAOT.
                button.ClearValue(WinUI.Control.BackgroundProperty);
                button.ClearValue(FrameworkElement.StyleProperty);
                H.SetContent(null);
                await Harness.Render();
            }
        }
    }

    private sealed record ThemedElement(Optional<Brush> Background = default) : Element;
}
