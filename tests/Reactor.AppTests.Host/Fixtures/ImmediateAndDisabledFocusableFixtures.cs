using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Controls.Validation;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.Fixtures;

/// <summary>
/// TestHost fixture for the validation pit-of-success APIs (NumberBox
/// .Immediate() and Button .IsDisabledFocusable()). Drives the Appium tier in
/// <c>Reactor.AppTests.Tests.ImmediateAndDisabledFocusableTests</c> with real
/// keystroke input so we cover the real-typing path that the in-process self-
/// tests cannot reach (programmatic <c>NumberBox.Text =</c> also commits Value,
/// which masks the difference between Immediate and non-Immediate wiring).
///
/// The visible probes are wired so the E2E test can observe state per
/// keystroke:
///   • ValImmediate_AgeDisplay — current age value as the user types.
///   • ValImmediate_FireCount — total OnValueChanged fires (rises mid-typing
///     with Immediate; would only rise on blur without it).
///   • ValImmediate_Submit — Button under test. Carries DisabledFocusable
///     while the form is invalid; the trampoline drops invokes in that mode.
///   • ValImmediate_SubmitCount — submit-handler-fired counter.
/// </summary>
internal static class ImmediateAndDisabledFocusableFixtures
{
    internal class DemoComponent : Component
    {
        public override Element Render()
        {
            var (email, setEmail) = UseState("");
            var (age, setAge) = UseState(0.0);
            var (fireCount, setFireCount) = UseState(0);
            var (submitCount, setSubmitCount) = UseState(0);

            var emailValid = email.Contains('@') && email.Contains('.');
            var ageValid = age >= 18 && age <= 120;
            var formValid = emailValid && ageValid;

            return VStack(8,
                TextBox(email, setEmail)
                    .AutomationId("ValImmediate_Email"),

                NumberBox(age, v =>
                    {
                        setAge(v);
                        setFireCount(fireCount + 1);
                    })
                    .Immediate()
                    .AutomationId("ValImmediate_Age"),

                TextBlock($"Age: {age:F0}")
                    .AutomationId("ValImmediate_AgeDisplay"),

                TextBlock($"Fires: {fireCount}")
                    .AutomationId("ValImmediate_FireCount"),

                Button("Submit", () => setSubmitCount(submitCount + 1))
                    .IsDisabledFocusable(!formValid)
                    .AutomationId("ValImmediate_Submit"),

                TextBlock($"Submits: {submitCount}")
                    .AutomationId("ValImmediate_SubmitCount"),

                TextBlock(formValid ? "valid" : "invalid")
                    .AutomationId("ValImmediate_FormValid")
            ).Padding(12);
        }
    }

    internal static Element Demo(RenderContext ctx) =>
        Component<DemoComponent>();
}
