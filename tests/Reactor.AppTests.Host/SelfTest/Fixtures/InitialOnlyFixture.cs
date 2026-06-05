using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 050 G6 — end-to-end coverage for <c>Prop.InitialOnly</c> (§7).
/// <para>Contract:</para>
/// <list type="bullet">
///   <item><b>Mount:</b> writes the value once.</item>
///   <item><b>Update:</b> never writes — even when the element's value
///   changes between renders the WinUI control retains its mount-time
///   value (the entire point of <c>InitialOnly</c>: the prop is a
///   one-shot configuration, not a live binding).</item>
/// </list>
/// <para>The unit-side PropEntry harness covers the shape (one entry per
/// descriptor); this fixture exercises the gate against a real WinUI
/// control through the descriptor handler.</para>
/// </summary>
internal static class InitialOnlyFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // Mount: writes "first" once.
            // Update with same value: still no write (no diff).
            // Update with new value: STILL no write (InitialOnly).
            // Update from a user-set control state: STILL no write.
            var button = new WinUI.Button();
            H.SetContent(button);
            await Harness.Render();

            var entry = new ControlDescriptor<InitialOnlyElement, WinUI.Button>()
                .InitialOnly(e => e.Tag, (c, v) => c.Tag = v)
                .Properties[0];

            var el1 = new InitialOnlyElement("first");
            entry.Mount(button, el1);
            await Harness.Render();
            H.Check(
                "InitialOnly_Mount_WritesValue",
                button.Tag as string == "first");

            // Update with a same-valued element. Must not write again
            // (no observable effect, but tests the gate consistency).
            entry.Update(button, el1, new InitialOnlyElement("first"));
            await Harness.Render();
            H.Check(
                "InitialOnly_Update_SameValue_NoOp",
                button.Tag as string == "first");

            // Update with a DIFFERENT value — the InitialOnly gate MUST
            // suppress the write so the WinUI control retains "first".
            entry.Update(button, el1, new InitialOnlyElement("second"));
            await Harness.Render();
            H.Check(
                "InitialOnly_Update_DifferentValue_NoWrite",
                button.Tag as string == "first");

            // Simulate a "user" mutation of the control's value (e.g.,
            // animation or external code), then Update again — still no
            // write, the user's mutation is preserved.
            button.Tag = "user-set";
            await Harness.Render();
            entry.Update(button, new InitialOnlyElement("second"), new InitialOnlyElement("third"));
            await Harness.Render();
            H.Check(
                "InitialOnly_Update_PreservesUserMutation",
                button.Tag as string == "user-set");
        }
    }

    private sealed record InitialOnlyElement(string Tag) : Element;
}
