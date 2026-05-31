using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 048 §3.3 — proves the per-factory <c>Reg&lt;&gt;.Done</c> registration
/// touch on the <b>Text control group</b> populates the global
/// <see cref="ControlRegistry"/>. Each factory's first invocation runs the
/// <c>Reg&lt;TElement,TControl,THandler&gt;</c> type initializer exactly once
/// per process, which calls
/// <see cref="ControlRegistry.Register{TElement,TControl}"/>.
///
/// <para>This must be a <b>selftest</b> (separate process), not an xunit test:
/// the registration cctor runs at most once per process, and the registry
/// unit tests under <c>tests/Reactor.Tests/Spec048/V1Protocol/</c> call
/// <see cref="ControlRegistry.ResetForTesting"/>, which would strip these
/// built-in entries for the rest of that process. The selftest host never
/// resets the registry, so the touch is observable end-to-end.</para>
///
/// <para>While <c>RegisterV1BuiltInHandlers</c> is still intact these global
/// registrations are dormant (per-host arm 1 wins dispatch); this fixture
/// asserts only that the registration <i>happened</i>, independent of which
/// dispatch arm production currently uses.</para>
/// </summary>
internal static class Spec048RegistrationFixtures
{
    internal class TextGroupFactoriesRegisterHandlers(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            // Invoke each Text-group factory once. The mere call runs the
            // factory body, which touches Reg<>.Done and registers the handler.
            _ = TextBlock("probe");
            _ = Heading("probe");
            _ = SubHeading("probe");
            _ = Caption("probe");
            _ = RichTextBlock("probe");
            _ = RichEditBox();
            _ = TextBox("probe");
            _ = PasswordBox("probe");
            _ = AutoSuggestBox("probe");

            H.Check("Spec048_Reg_TextBlock",
                ControlRegistry.Contains(typeof(TextBlockElement)));
            H.Check("Spec048_Reg_RichTextBlock",
                ControlRegistry.Contains(typeof(RichTextBlockElement)));
            H.Check("Spec048_Reg_RichEditBox",
                ControlRegistry.Contains(typeof(RichEditBoxElement)));
            H.Check("Spec048_Reg_TextBox",
                ControlRegistry.Contains(typeof(TextBoxElement)));
            H.Check("Spec048_Reg_PasswordBox",
                ControlRegistry.Contains(typeof(PasswordBoxElement)));
            H.Check("Spec048_Reg_AutoSuggestBox",
                ControlRegistry.Contains(typeof(AutoSuggestBoxElement)));

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Spec 048 §3.3 — Input-group factory <c>Reg&lt;&gt;.Done</c> registration
    /// touch. Mirrors the Text-group fixture pattern. Covers the 15 element
    /// types whose handler is <c>IElementHandler&lt;TElement,TControl&gt;</c>
    /// (hand-coded ToggleSwitch / Slider; descriptor-backed RepeatButton,
    /// HyperlinkButton, DropDownButton, SplitButton, ToggleSplitButton,
    /// ToggleButton, RadioButton, RadioButtons, NumberBox, RatingControl,
    /// PipsPager, ColorPicker, SelectorBar).
    ///
    /// <para><b>Excluded:</b> <c>Button</c> and <c>CheckBox</c> use
    /// <c>IDecoratorElementHandler&lt;TElement&gt;</c> which is not assignable
    /// to <c>Reg&lt;&gt;</c>'s <c>THandler : IElementHandler&lt;…&gt;</c>
    /// constraint; the global-registry decorator path is a §3.4 blocker
    /// tracked separately.</para>
    /// </summary>
    internal class InputGroupFactoriesRegisterHandlers(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            // Invoke each Input-group factory once. The factory body's
            // Reg<>.Done touch runs the closed-generic cctor exactly once
            // per process and registers the handler.
            _ = HyperlinkButton("probe");
            _ = RepeatButton("probe");
            _ = ToggleButton("probe");
            _ = DropDownButton("probe");
            _ = SplitButton("probe");
            _ = ToggleSplitButton("probe");
            _ = NumberBox(0);
            _ = RadioButton("probe");
            _ = RadioButtons(["a"]);
            _ = Slider(0);
            _ = ToggleSwitch(false);
            _ = RatingControl();
            _ = ColorPicker(default);
            _ = SelectorBar([SelectorBarItem("a")]);
            _ = PipsPager(1);

            H.Check("Spec048_Reg_HyperlinkButton",
                ControlRegistry.Contains(typeof(HyperlinkButtonElement)));
            H.Check("Spec048_Reg_RepeatButton",
                ControlRegistry.Contains(typeof(RepeatButtonElement)));
            H.Check("Spec048_Reg_ToggleButton",
                ControlRegistry.Contains(typeof(ToggleButtonElement)));
            H.Check("Spec048_Reg_DropDownButton",
                ControlRegistry.Contains(typeof(DropDownButtonElement)));
            H.Check("Spec048_Reg_SplitButton",
                ControlRegistry.Contains(typeof(SplitButtonElement)));
            H.Check("Spec048_Reg_ToggleSplitButton",
                ControlRegistry.Contains(typeof(ToggleSplitButtonElement)));
            H.Check("Spec048_Reg_NumberBox",
                ControlRegistry.Contains(typeof(NumberBoxElement)));
            H.Check("Spec048_Reg_RadioButton",
                ControlRegistry.Contains(typeof(RadioButtonElement)));
            H.Check("Spec048_Reg_RadioButtons",
                ControlRegistry.Contains(typeof(RadioButtonsElement)));
            H.Check("Spec048_Reg_Slider",
                ControlRegistry.Contains(typeof(SliderElement)));
            H.Check("Spec048_Reg_ToggleSwitch",
                ControlRegistry.Contains(typeof(ToggleSwitchElement)));
            H.Check("Spec048_Reg_RatingControl",
                ControlRegistry.Contains(typeof(RatingControlElement)));
            H.Check("Spec048_Reg_ColorPicker",
                ControlRegistry.Contains(typeof(ColorPickerElement)));
            H.Check("Spec048_Reg_SelectorBar",
                ControlRegistry.Contains(typeof(SelectorBarElement)));
            H.Check("Spec048_Reg_PipsPager",
                ControlRegistry.Contains(typeof(PipsPagerElement)));

            return Task.CompletedTask;
        }
    }
}
