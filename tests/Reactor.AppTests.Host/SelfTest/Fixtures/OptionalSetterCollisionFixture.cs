using Microsoft.UI.Reactor.Core;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 050 G3 — a <c>.Set(c =&gt; c.X = ...)</c> fluent setter that writes
/// the same DP as a controlled <see cref="Optional{T}"/> prop must not
/// strand the echo-suppression scope and must not silently swallow the
/// user's next real input.
///
/// <para>Setter scope semantics: <c>ApplySetters</c> wraps user setters in a
/// non-consuming <c>EchoSuppressScopeDepth</c> bracket — any change event
/// fired during the scope is suppressed without consuming a counter token.
/// After the scope exits, the next real user input must reach the
/// callback. This fixture proves both halves.</para>
/// </summary>
internal static class OptionalSetterCollisionFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await Setter_WritesSameDp_NoStrandOnNextUserInput();
            await Setter_OverridesControlledValue_LastWriteWins();
        }

        private async Task Setter_WritesSameDp_NoStrandOnNextUserInput()
        {
            // Both ApplySetters (writes Text="setter-value") AND the
            // controlled Value entry (writes "controlled-value") target
            // TextProperty. ApplySetters runs LAST in the descriptor
            // pipeline, so it wins for the visible value; the controlled
            // write's echo-suppression token must not strand.
            var calls = new List<string>();
            using var host = H.CreateHost();
            host.Mount(ctx => VStack(
                TextBox(
                    "controlled-value",
                    v => calls.Add(v))
                .Set(tb => tb.Text = "setter-value")));

            await Harness.Render();
            var tb = H.FindControl<WinUI.TextBox>(_ => true);
            H.Check("OptionalSetterCollision_Setter_ControlFound", tb is not null);
            if (tb is null) return;

            // ApplySetters ran last; setter value wins.
            H.Check(
                "OptionalSetterCollision_Setter_LastWriteWins",
                tb.Text == "setter-value");

            // No callback should have fired for either programmatic write.
            H.Check(
                "OptionalSetterCollision_Setter_NoCallbackForProgrammaticWrites",
                calls.Count == 0);

            // The user's next real edit MUST reach the callback (the
            // setter-scope suppress depth and any controlled-write counter
            // token must both have been resolved cleanly).
            calls.Clear();
            tb.Text = "user-typed";
            await Harness.Render();

            H.Check(
                "OptionalSetterCollision_Setter_RealUserInputReachesCallback",
                calls.Count == 1 && calls[^1] == "user-typed");
        }

        private async Task Setter_OverridesControlledValue_LastWriteWins()
        {
            // Sanity check: across an Update that changes both the
            // controlled value AND keeps the setter, the setter still wins.
            //
            // Note on echo semantics: the ApplySetters EchoSuppressScopeDepth
            // bracket is SYNCHRONOUS-ONLY — it consumes scope tokens within
            // the setter call and exits before deferred change events fire.
            // For deferred-event DPs like TextBox.TextProperty, the setter's
            // write WILL fire TextChanged after the scope has exited, and
            // the trampoline will invoke the user callback. This is
            // documented behavior (spec 047 §8.2): authors who want a setter
            // write to be echo-suppressed for a deferred-event DP must wrap
            // it in `ReactorBinding.WriteSuppressed`. This test confirms
            // the last-write-wins semantics; it does NOT assert echo
            // suppression for the deferred event.
            var calls = new List<string>();
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, setPhase) = ctx.UseState(0);
                return VStack(
                    Button("SetterCollision_Update_Next", () => setPhase(phase + 1)),
                    TextBox(
                        phase == 0 ? "ctl-a" : "ctl-b",
                        v => calls.Add(v))
                    .Set(tb => tb.Text = phase == 0 ? "set-a" : "set-b"));
            });

            await Harness.Render();
            var tb = H.FindControl<WinUI.TextBox>(_ => true);
            H.Check("OptionalSetterCollision_Update_ControlFound", tb is not null);
            if (tb is null) return;

            H.Check("OptionalSetterCollision_Update_Phase0", tb.Text == "set-a");

            H.ClickButton("SetterCollision_Update_Next");
            await Harness.Render();
            var b = await Harness.WaitFor(() => tb.Text == "set-b", 10, 20);
            H.Check("OptionalSetterCollision_Update_Phase1_SetterStillWins", b);

            // User input after the deferred-event echo storm is over still
            // reaches the callback (verifies no permanent strand).
            calls.Clear();
            tb.Text = "user-final";
            await Harness.Render();
            H.Check(
                "OptionalSetterCollision_Update_UserInputReachesCallback",
                calls.Count >= 1 && calls[^1] == "user-final");
        }
    }
}
