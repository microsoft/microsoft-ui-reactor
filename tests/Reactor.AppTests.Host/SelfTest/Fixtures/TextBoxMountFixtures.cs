using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Verifies that TextBox mount correctly applies AcceptsReturn / TextWrapping
/// BEFORE setting Text. WinUI TextBox silently strips \r\n when in single-line
/// mode (AcceptsReturn=false), so the property order in MountTextBox matters.
/// </summary>
internal static class TextBoxMountFixtures
{
    // Multi-paragraph value used across tests.
    const string MultiLine = "Line one\r\nLine two\r\nLine three";

    /// <summary>
    /// AcceptsReturn=true with multi-line text: all lines must survive the mount.
    /// Regression test for the MountTextBox property-ordering bug where Text was
    /// set before AcceptsReturn, causing WinUI to silently drop lines 2+.
    /// </summary>
    internal class MultiLineTextPreserved(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ =>
                (TextBox(MultiLine, placeholderText: "ml-test")
                    with { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap })
                .MinHeight(80)
                .AutomationName("ml-target")
            );

            await Harness.Render();

            var tb = H.FindControl<TextBox>(t => t.PlaceholderText == "ml-test");
            H.Check("TB_MultiLine_Mounted", tb is not null);

            // All three lines must be present. WinUI normalizes \r\n → \r internally,
            // so we check for \r rather than \r\n.
            var text = tb?.Text ?? "";
            H.Check("TB_MultiLine_Line1Present", text.Contains("Line one"));
            H.Check("TB_MultiLine_Line2Present", text.Contains("Line two"));
            H.Check("TB_MultiLine_Line3Present", text.Contains("Line three"));
            H.Check("TB_MultiLine_AcceptsReturn", tb?.AcceptsReturn == true);
        }
    }

    /// <summary>
    /// Single-line TextBox (AcceptsReturn omitted/false): Text is set in single-line
    /// mode which is correct. Verifies single-line mounts without regression.
    /// </summary>
    internal class SingleLineMountCorrect(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(_ =>
                TextBox("hello world", placeholderText: "sl-test")
                    .AutomationName("sl-target")
            );

            await Harness.Render();

            var tb = H.FindControl<TextBox>(t => t.PlaceholderText == "sl-test");
            H.Check("TB_SingleLine_Mounted", tb is not null);
            H.Check("TB_SingleLine_TextCorrect", tb?.Text == "hello world");
            H.Check("TB_SingleLine_AcceptsReturnFalse", tb?.AcceptsReturn == false);
        }
    }

    /// <summary>
    /// Update path: after mount with multi-line text, a state change that updates
    /// the value preserves all lines via UpdateTextBox.
    /// </summary>
    internal class MultiLineUpdatePreserved(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, set) = ctx.UseState(0);
                var value = phase == 0 ? "First" : MultiLine;
                return VStack(
                    Button("TB_ML_Upd", () => set(1)),
                    (TextBox(value, placeholderText: "ml-upd-test")
                        with { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap })
                    .MinHeight(80));
            });

            await Harness.Render();
            var tb = H.FindControl<TextBox>(t => t.PlaceholderText == "ml-upd-test");
            H.Check("TB_MLUpd_InitialText", tb?.Text == "First");

            H.ClickButton("TB_ML_Upd");
            await Harness.Render();

            var text = tb?.Text ?? "";
            H.Check("TB_MLUpd_Line1", text.Contains("Line one"));
            H.Check("TB_MLUpd_Line2", text.Contains("Line two"));
            H.Check("TB_MLUpd_Line3", text.Contains("Line three"));
        }
    }
}
