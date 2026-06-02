using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml.Controls;
using Reactor.External.TestControl;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 047 §14 Phase 1 (1.16) — external-assembly proof, live-WinUI fixtures.
///
/// These fixtures exercise the <see cref="MarqueeHandler"/> (authored in
/// the external <c>Reactor.External.TestControl</c> assembly, against the
/// public V1 protocol surface only — no <c>InternalsVisibleTo</c>). The
/// hermetic xUnit-style tests in
/// <c>tests/external_proof/Reactor.External.TestControl.Tests/</c> handle
/// the no-dispatcher invariants (registration semantics, no-IVT audit);
/// these fixtures handle everything that requires real WinUI control
/// activation.
///
/// <para>Coverage per Phase 1 exit gate item 2 (spec §14):</para>
/// <list type="bullet">
///   <item><b>Value-bearing prop write + readback</b> — Mount + Update via
///         the public Reactor host, assert <see cref="MarqueeControl.Caption"/>
///         tracks the element.</item>
///   <item><b>Custom event subscribed via <c>BindFor.OnCustomEvent</c></b> —
///         programmatic write inside <c>WriteSuppressed</c> does not echo;
///         direct write outside does.</item>
///   <item><b>Modifier chain applied</b> — <c>.Margin(...)</c> / etc. flow
///         through the engine's modifier pipeline (V1-flag independent).</item>
///   <item><b>Setter chain applied</b> — <c>el.Setters</c> handed to
///         <c>ctx.ApplySetters</c> mutates the control.</item>
///   <item><b>Pool rent/return cycle</b> — through the public surface,
///         observable on a poolable type (Border).</item>
/// </list>
///
/// <para>Spec 048 §6 update: registration now happens implicitly via
/// the <see cref="Marquee"/> static cctor on the first
/// <see cref="Marquee.Of(string)"/> call — no per-host
/// <see cref="Reconciler.RegisterHandler{TElement,TControl}"/> is needed
/// or written. Dispatch reaches the handler through arm 3
/// (<see cref="Microsoft.UI.Reactor.Core.V1Protocol.ControlRegistry"/>);
/// the cached adapter is hoisted into <c>_v1Handlers</c> on first hit so
/// subsequent dispatches use the fast arm 1 path.</para>
/// </summary>
internal static class Spec047ExternalProofFixtures
{
    // ────────────────────────────────────────────────────────────────────
    //  Mount + Update — value-bearing prop tracking.
    // ────────────────────────────────────────────────────────────────────

    internal class MarqueeMountUpdate(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            host.Mount(ctx =>
            {
                var (caption, setCaption) = ctx.UseState("initial");
                return VStack(
                    Marquee.Of(caption),
                    Button("Update", () => setCaption("updated"))
                );
            });

            await Harness.Render();
            var mc = H.FindControl<MarqueeControl>(_ => true);
            H.Check("ExtProof_Marquee_Mounted", mc is not null);
            H.Check("ExtProof_Marquee_InitialCaption", mc?.Caption == "initial");

            H.ClickButton("Update");
            await Harness.Render();
            mc = H.FindControl<MarqueeControl>(_ => true);
            H.Check("ExtProof_Marquee_CaptionUpdated", mc?.Caption == "updated");
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  WriteSuppressed + custom event subscription.
    //
    //  - Programmatic Caption write through the handler's reconcile path
    //    happens inside WriteSuppressed → no callback echo.
    //  - Direct ctrl.Caption = "x" write OUTSIDE the suppression scope →
    //    callback fires once.
    // ────────────────────────────────────────────────────────────────────

    internal class MarqueeWriteSuppressedEcho(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int fireCount = 0;
            var host = H.CreateHost();

            host.Mount(ctx =>
            {
                var (caption, setCaption) = ctx.UseState("a");
                return VStack(
                    Marquee.Of(caption, c => { fireCount++; setCaption(c); }),
                    Button("Reconcile", () => setCaption("b"))
                );
            });

            await Harness.Render();
            var mc = H.FindControl<MarqueeControl>(_ => true);
            H.Check("ExtProof_Marquee_WriteSuppressed_Mounted", mc is not null);

            // Reconcile path — handler's Update writes Caption via
            // WriteSuppressed. The callback must not fire.
            int beforeReconcile = fireCount;
            H.ClickButton("Reconcile");
            await Harness.Render();
            H.Check("ExtProof_Marquee_WriteSuppressed_NoEchoOnReconcile",
                fireCount == beforeReconcile);
            mc = H.FindControl<MarqueeControl>(_ => true);
            H.Check("ExtProof_Marquee_WriteSuppressed_ReconcileApplied",
                mc?.Caption == "b");

            // Direct write OUTSIDE the suppression scope — simulates a
            // user-initiated change. Callback must fire exactly once.
            int beforeDirect = fireCount;
            if (mc is not null) mc.Caption = "user-typed";
            await Harness.Render();
            H.Check("ExtProof_Marquee_WriteSuppressed_FiresOutsideScope",
                fireCount == beforeDirect + 1);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Modifier chain — .Margin / .Width / ... applied by the engine's
    //  modifier pipeline (V1-independent, but worth proving on a non-
    //  built-in element type).
    // ────────────────────────────────────────────────────────────────────

    internal class MarqueeModifierChain(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            host.Mount(_ => VStack(
                Marquee.Of("modded").Margin(8).Width(120)
            ));

            await Harness.Render();
            var mc = H.FindControl<MarqueeControl>(_ => true);
            H.Check("ExtProof_Marquee_Modifier_Mounted", mc is not null);
            H.Check("ExtProof_Marquee_Margin",
                mc?.Margin.Left == 8 && mc?.Margin.Top == 8);
            H.Check("ExtProof_Marquee_Width", mc?.Width == 120);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Setter chain — ctx.ApplySetters in the handler runs the lambdas
    //  declared on the element. Confirms the public ApplySetters helper
    //  (promoted in §1.3) is usable by external authors.
    // ────────────────────────────────────────────────────────────────────

    internal class MarqueeSetterChain(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();

            bool setterRan = false;
            host.Mount(_ => VStack(
                Marquee.Of("set-base") with
                {
                    Setters =
                    [
                        c => c.Tag = "set-by-author",
                        c => { setterRan = true; },
                    ],
                }
            ));

            await Harness.Render();
            var mc = H.FindControl<MarqueeControl>(_ => true);
            H.Check("ExtProof_Marquee_Setter_Mounted", mc is not null);
            H.Check("ExtProof_Marquee_Setter_TagApplied",
                mc?.Tag is string s && s == "set-by-author");
            H.Check("ExtProof_Marquee_Setter_LambdaRan", setterRan);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Pool rent/return — MarqueeControl is NOT in PoolableTypes; this
    //  asserts the documented "fresh instance every time, no exception"
    //  behavior holds when called from external code.
    // ────────────────────────────────────────────────────────────────────

    internal class MarqueePoolRentReturn(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var reconciler = host.Reconciler;

            MarqueeControl? a = null;
            MarqueeControl? b = null;
            Exception? rentEx = null;
            try
            {
                a = reconciler.RentControl<MarqueeControl>();
                b = reconciler.RentControl<MarqueeControl>();
            }
            catch (Exception e) { rentEx = e; }

            H.Check("ExtProof_Marquee_Rent_NoException", rentEx is null);
            H.Check("ExtProof_Marquee_Rent_FreshInstance",
                a is not null && b is not null && !ReferenceEquals(a, b));

            Exception? returnEx = null;
            try
            {
                if (a is not null) reconciler.ReturnControl(a);
                // Idempotency — double return must be safe.
                if (a is not null) reconciler.ReturnControl(a);
            }
            catch (Exception e) { returnEx = e; }
            H.Check("ExtProof_Marquee_Return_NoException", returnEx is null);

            await Task.CompletedTask;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Pool reset contract observability — for a POOLABLE type (Border),
    //  the external author can observe the engine's reset contract end-
    //  to-end through public surface only.
    // ────────────────────────────────────────────────────────────────────

    internal class MarqueePoolResetContract(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var host = H.CreateHost();
            var reconciler = host.Reconciler;

            // Allocate fresh; pool starts empty. Border IS in
            // PoolableTypes so the second rent (post-return) MAY return
            // the same instance — but the assertion is on the tag, not
            // instance identity, because pool stack capacity / dirty
            // detach may drop the return silently.
            var border = reconciler.RentControl<Border>();
            Reconciler.SetElementTag(border, Marquee.Of("tagged"));
            H.Check("ExtProof_Pool_TagSet",
                Reconciler.GetElementTag(border) is MarqueeElement m && m.Caption == "tagged");

            reconciler.ReturnControl(border);
            H.Check("ExtProof_Pool_TagClearedOnReturn",
                Reconciler.GetElementTag(border) is null);

            await Task.CompletedTask;
        }
    }
}
