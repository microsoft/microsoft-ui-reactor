using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Regression for https://github.com/microsoft/microsoft-ui-reactor/issues/1749
///
/// <c>ApplyPaddingModifiers</c> resolved <c>m.Padding ?? oldM?.Padding</c>, so a render that
/// dropped <c>.Padding(x)</c> and an inline edge together still resolved to <c>x</c>. The inline
/// edge changed, so the write arm fired and wrote <c>x</c> back as a local value, and the reset
/// (that arm's <c>else</c>) never ran.
/// </summary>
internal static class Issue1749PaddingDropBothFixture
{
    internal class DroppingPaddingAndInlineEdgeTogetherClears(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (padded, setPadded) = ctx.UseState(true);
                var button = Button("Issue1749_Button", () => { });
                var text = TextBlock("Issue1749_Text");
                var kept = TextBlock("Issue1749_KeepsPhysical");
                return VStack(
                    Button("Issue1749_Toggle", () => setPadded(!padded)),
                    padded ? button.Padding(30).PaddingInlineStart(9) : button,
                    // Never carries the modifier: the themed padding the button must return to.
                    Button("Issue1749_ThemedReference", () => { }),
                    padded ? text.Padding(30).PaddingInlineEnd(7) : text,
                    // Differential: drops only the inline edge. The write arm must still run here
                    // and restore the physical padding, so the fix cannot just skip the write.
                    padded ? kept.Padding(30).PaddingInlineStart(9) : kept.Padding(30));
            });

            await Harness.Render();

            var btn = H.FindControl<WinUI.Button>(b => (b.Content as string) == "Issue1749_Button");
            var reference = H.FindControl<WinUI.Button>(b => (b.Content as string) == "Issue1749_ThemedReference");
            var tb = H.FindText("Issue1749_Text");
            var keptTb = H.FindText("Issue1749_KeepsPhysical");
            H.Check("Issue1749_Mounted", btn is not null && reference is not null && tb is not null && keptTb is not null);
            if (btn is null || reference is null || tb is null || keptTb is null) return;

            // Without a non-zero themed padding, "cleared" and "written as a local zero" read the same.
            var themed = reference.Padding;
            H.Check("Issue1749_ThemedPaddingIsNonZero", themed != new Thickness(0));

            H.Check("Issue1749_Mount_ButtonPadding", btn.Padding == new Thickness(9, 30, 30, 30));
            H.Check("Issue1749_Mount_TextPadding", tb.Padding == new Thickness(30, 30, 7, 30));
            H.Check("Issue1749_Mount_KeptPadding", keptTb.Padding == new Thickness(9, 30, 30, 30));

            H.ClickButton("Issue1749_Toggle");
            await Harness.Render();

            // Identity first: TextBlock.Padding is released by the pool, so a recycled control
            // would read UnsetValue whether or not the reset arm ran.
            H.Check("Issue1749_ButtonSameInstance",
                ReferenceEquals(btn, H.FindControl<WinUI.Button>(b => (b.Content as string) == "Issue1749_Button")));
            H.Check("Issue1749_TextSameInstance", ReferenceEquals(tb, H.FindText("Issue1749_Text")));
            H.Check("Issue1749_KeptSameInstance", ReferenceEquals(keptTb, H.FindText("Issue1749_KeepsPhysical")));

            // Before the fix this stayed Thickness(30) as a local value.
            H.Check("Issue1749_ButtonLocalValueCleared",
                ReferenceEquals(DependencyProperty.UnsetValue, btn.ReadLocalValue(WinUI.Control.PaddingProperty)));
            H.Check("Issue1749_ButtonFallsBackToThemedPadding", btn.Padding == themed);

            H.Check("Issue1749_TextLocalValueCleared",
                ReferenceEquals(DependencyProperty.UnsetValue, tb.ReadLocalValue(WinUI.TextBlock.PaddingProperty)));

            H.Check("Issue1749_KeptPhysicalPaddingRestored", keptTb.Padding == new Thickness(30));
        }
    }
}
