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
}
