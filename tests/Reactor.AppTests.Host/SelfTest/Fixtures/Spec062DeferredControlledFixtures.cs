using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Reactor.External.TestControl;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 062 §14 — live-WinUI proof that a source-generated
/// <c>[WrapControlled(Deferred = true)]</c> wrapper authored in an EXTERNAL
/// assembly (no <c>InternalsVisibleTo</c> from Reactor.dll) round-trips its
/// two-way value through the suppress-counter echo channel using only Reactor's
/// PUBLIC surface.
///
/// <para><see cref="ExternalEchoTextBoxElement"/> (over
/// <see cref="EchoTextBox"/>) is filled in by <c>Reactor.Wrappers.Generator</c>;
/// its deferred-controlled trampoline gates the echo on the public
/// <c>ReactorBinding.ShouldSuppressEcho</c> primitive. This fixture exercises that
/// exact generated code path end-to-end:</para>
/// <list type="bullet">
///   <item>a framework-driven reconcile write (the generated Update wraps the set
///         in <c>WriteSuppressed</c>) does NOT fire the user's OnTextChanged; and</item>
///   <item>a genuine user edit (direct setter, outside the suppression scope) DOES
///         fire it — exactly once, with no stranded token on the coincident
///         re-render the callback triggers.</item>
/// </list>
/// The hermetic no-dispatcher half (registration + no-IVT audit) lives in
/// <c>tests/external_proof/Reactor.External.TestControl.Tests</c> as
/// <c>ExternalEchoBoxWrapperSelftests</c>.
/// </summary>
internal static class Spec062DeferredControlledFixtures
{
    internal class Echo(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int fireCount = 0;
            var host = H.CreateHost();

            // No-Reactor-state path: a fresh, unmounted control carries no attached
            // ReactorState and no pending suppress token, so the public read-side
            // primitive must report false. This pins the false-return branch that the
            // mounted round-trip below only exercises implicitly.
            H.Check("Spec062_Deferred_FreshControlNotSuppressed",
                !ReactorBinding.ShouldSuppressEcho(new EchoTextBox()));

            host.Mount(ctx =>
            {
                var (text, setText) = ctx.UseState("a");
                return VStack(
                    ExternalEchoTextBoxElement.EchoTextBox(text, t => { fireCount++; setText(t); }),
                    Button("Reconcile", () => setText("b"))
                );
            });

            await Harness.Render();
            var ec = H.FindControl<EchoTextBox>(_ => true);
            H.Check("Spec062_Deferred_Mounted", ec is not null);
            H.Check("Spec062_Deferred_InitialText", ec?.Text == "a");

            // Framework-driven reconcile — the generated deferred Update writes Text
            // via WriteSuppressed; the trampoline's ShouldSuppressEcho drops the
            // echo. The user callback must NOT fire.
            int beforeReconcile = fireCount;
            H.ClickButton("Reconcile");
            await Harness.Render();
            H.Check("Spec062_Deferred_NoEchoOnReconcile", fireCount == beforeReconcile);
            ec = H.FindControl<EchoTextBox>(_ => true);
            H.Check("Spec062_Deferred_ReconcileApplied", ec?.Text == "b");

            // Genuine user edit — direct write OUTSIDE the suppression scope. The
            // callback must fire exactly once. Its setText drives a coincident
            // re-render where newEl.Text == ctrl.Text already, so the readback-gated
            // Update performs NO write and arms NO suppression token.
            int beforeUser = fireCount;
            if (ec is not null) ec.Text = "user";
            await Harness.Render();
            H.Check("Spec062_Deferred_FiresOnUserEdit", fireCount == beforeUser + 1);

            // Token-stranding pin — if the coincident reconcile had armed a stray
            // token, the NEXT real user edit would be swallowed. Assert it still
            // fires exactly once.
            int beforeSecond = fireCount;
            ec = H.FindControl<EchoTextBox>(_ => true);
            if (ec is not null) ec.Text = "user2";
            await Harness.Render();
            H.Check("Spec062_Deferred_NoStrandedTokenAfterCoincident",
                fireCount == beforeSecond + 1);
        }
    }
}
