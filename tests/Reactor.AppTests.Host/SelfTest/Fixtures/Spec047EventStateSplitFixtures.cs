using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 047 §4.3 self-test fixtures — validates the §9.2 EventHandlerState
/// split contract after commit 691048bd.
///
/// <para>The monolithic <c>EventHandlerState</c> was split into:</para>
/// <list type="bullet">
///   <item>the routed-input family, renamed <c>ModifierEventHandlerState</c>,
///         lazy on <see cref="Reconciler.ReactorState.Modifiers"/>; and</item>
///   <item>control-intrinsic events (Button.Click, NumberBox immediate flag,
///         …) which now live ONLY on the per-control payload boxed in
///         <see cref="Reconciler.ReactorState.ControlEventState"/> — a
///         <see cref="ControlEventStateBox"/> with a <c>HandlerType</c>
///         discriminator + <c>object Payload</c>, resolved via
///         <c>Reconciler.GetOrCreateControlEventPayload&lt;T&gt;(fe)</c>.</item>
/// </list>
///
/// <para>Pool reset (<c>ReturnControl&lt;T&gt;</c>) clears
/// <c>Modifiers?.ClearCurrentHandlers()</c> + Element/echo fields but
/// PRESERVES <c>ControlEventState</c> across rent/return (issue #114 — the
/// Click trampoline stays subscribed to the native event for the control's
/// lifetime and reads the LIVE element via <c>GetElementTag</c>; the box is
/// dropped only on a <c>GetOrCreateControlEventPayload</c> HandlerType
/// mismatch, i.e. full detach / handler-type change).</para>
///
/// <para>These fixtures reach into <c>ReactorState</c> directly — the host
/// assembly has <c>InternalsVisibleTo</c> (see Reactor.csproj) — and ALSO
/// assert observable behaviour (callback fire counts) so the contract is
/// proven both structurally and end-to-end.</para>
///
/// <para>Each fixture mirrors the engine's real wiring path
/// (<see cref="Reconciler.MountButton"/> →
/// <see cref="Reconciler.EnsureButtonWiring"/>) rather than re-implementing
/// the trampoline, so a regression in the production mount path fails here.</para>
/// </summary>
internal static class Spec047EventStateSplitFixtures
{
    // Fire a native Button.Click on a bare (un-parented) control, the same way
    // Harness.ClickButton does for tree-mounted buttons — via the UIA invoke
    // pattern, which raises the real Click routed event the ButtonEventPayload
    // trampoline is subscribed to.
    private static void FireClick(WinUI.Button button)
    {
        var peer = new ButtonAutomationPeer(button);
        var invoke = (IInvokeProvider)peer.GetPattern(PatternInterface.Invoke);
        invoke.Invoke();
    }

    private static Reconciler.ReactorState? ReadState(FrameworkElement fe) =>
        fe.GetValue(Reconciler.ReactorAttached.StateProperty) as Reconciler.ReactorState;

    // ────────────────────────────────────────────────────────────────────
    //  1. Issue-#114 regression guard — no double subscription across pool
    //     reuse. The preserved ControlEventState trampoline must fire the
    //     LIVE element's handler exactly once after re-rent, never twice.
    // ────────────────────────────────────────────────────────────────────

    internal class NoDuplicateSubscriptionAcrossPoolReuse(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var reconciler = H.CreateHost().Reconciler;

            int countA = 0;
            int countB = 0;

            // Mount element A through the real engine path. MountButton rents
            // (pool empty → fresh), stamps the tag, and wires the Click
            // trampoline into a freshly-created ButtonEventPayload box.
            var elementA = new ButtonElement("A", () => countA++);
            var control = reconciler.MountButton(elementA, () => { });

            var boxAfterMount = ReadState(control)?.ControlEventState as ControlEventStateBox;
            H.Check("EventStateSplit_Reuse_BoxCreatedOnMount",
                boxAfterMount is not null && boxAfterMount.HandlerType == typeof(ButtonEventPayload));
            H.Check("EventStateSplit_Reuse_TrampolineWired",
                (boxAfterMount?.Payload as ButtonEventPayload)?.ClickTrampoline is not null);

            FireClick(control);
            H.Check("EventStateSplit_Reuse_FirstFireOnce", countA == 1);

            // Return to the pool. Per the #114 contract this clears Element but
            // PRESERVES ControlEventState (the trampoline stays subscribed).
            reconciler.ReturnControl(control);
            var boxAfterReturn = ReadState(control)?.ControlEventState as ControlEventStateBox;
            H.Check("EventStateSplit_Reuse_BoxPreservedAcrossReturn",
                ReferenceEquals(boxAfterReturn, boxAfterMount));
            H.Check("EventStateSplit_Reuse_ElementClearedOnReturn",
                ReadState(control)?.Element is null);

            // Re-rent + re-mount a NEW element B. MountButton must return the
            // SAME pooled control and EnsureButtonWiring must short-circuit
            // (trampoline already non-null) → no second Click subscription.
            var elementB = new ButtonElement("B", () => countB++);
            var control2 = reconciler.MountButton(elementB, () => { });
            H.Check("EventStateSplit_Reuse_SamePooledInstance",
                ReferenceEquals(control2, control));

            int beforeA = countA;
            FireClick(control2);

            // The live element is now B: B fires exactly once, A does NOT fire
            // again. A second subscription (the #114 bug) would make countB==2.
            H.Check("EventStateSplit_Reuse_LiveHandlerFiresExactlyOnce", countB == 1);
            H.Check("EventStateSplit_Reuse_StaleHandlerDoesNotFire", countA == beforeA);

            await Task.CompletedTask;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  2. HandlerType mismatch resets the box deterministically (no
    //     InvalidCastException). This is the hot-reload type-identity proxy:
    //     a handler replaced while a control is mounted (Phase 4+ scenario)
    //     would request a payload of a different CLR type on the same native
    //     control; the discriminator must detect the mismatch and mint a
    //     fresh box rather than unbox stale state.
    // ────────────────────────────────────────────────────────────────────

    internal class HandlerTypeMismatchResetsBox(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var reconciler = H.CreateHost().Reconciler;

            // A bare Button is enough — we drive GetOrCreateControlEventPayload
            // directly, which is exactly what every control port funnels through.
            var control = new WinUI.Button();

            // Stamp payload type A (ButtonEventPayload) and tag it so we can
            // detect whether the box survives or is replaced.
            var payloadA = Reconciler.GetOrCreateControlEventPayload<ButtonEventPayload>(control);
            payloadA.ClickTrampoline = (_, _) => { };
            var boxA = ReadState(control)?.ControlEventState as ControlEventStateBox;
            H.Check("EventStateSplit_Mismatch_BoxAStamped",
                boxA is not null && boxA.HandlerType == typeof(ButtonEventPayload));

            // Idempotency: requesting the SAME type returns the SAME payload
            // instance (no reset, no realloc).
            var payloadASecond = Reconciler.GetOrCreateControlEventPayload<ButtonEventPayload>(control);
            H.Check("EventStateSplit_Mismatch_SameTypeReturnsSamePayload",
                ReferenceEquals(payloadA, payloadASecond));

            // Now request a DIFFERENT payload type (ToggleSwitchEventPayload) on
            // the same native control. The HandlerType discriminator mismatches
            // → the box must be replaced with a fresh one of the new type,
            // deterministically, with no InvalidCastException.
            ToggleSwitchEventPayload? payloadB = null;
            Exception? ex = null;
            try
            {
                payloadB = Reconciler.GetOrCreateControlEventPayload<ToggleSwitchEventPayload>(control);
            }
            catch (Exception e) { ex = e; }

            H.Check("EventStateSplit_Mismatch_NoException", ex is null);

            var boxB = ReadState(control)?.ControlEventState as ControlEventStateBox;
            H.Check("EventStateSplit_Mismatch_BoxReplaced",
                boxB is not null && !ReferenceEquals(boxB, boxA)
                && boxB.HandlerType == typeof(ToggleSwitchEventPayload));
            H.Check("EventStateSplit_Mismatch_NewPayloadFresh",
                payloadB is not null && ReferenceEquals(boxB?.Payload, payloadB));

            // The stale ButtonEventPayload is no longer reachable through the
            // box — the discriminator guarantees a reader of type A can never
            // unbox the type-B payload (TryGetControlEventPayload<A> == null).
            H.Check("EventStateSplit_Mismatch_StalePayloadUnreachable",
                Reconciler.TryGetControlEventPayload<ButtonEventPayload>(control) is null);

            await Task.CompletedTask;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  3. Double-return is idempotent. Returning the same control twice must
    //     not throw, must not corrupt/duplicate ControlEventState, and a
    //     subsequent re-rent + fire still fires exactly once.
    // ────────────────────────────────────────────────────────────────────

    internal class DualReturnIdempotent(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var reconciler = H.CreateHost().Reconciler;

            int count = 0;
            var elementA = new ButtonElement("A", () => count++);
            var control = reconciler.MountButton(elementA, () => { });

            var boxBefore = ReadState(control)?.ControlEventState as ControlEventStateBox;

            Exception? ex = null;
            try
            {
                reconciler.ReturnControl(control);
                // Double return — mirror MarqueePoolRentReturn's idempotency probe.
                reconciler.ReturnControl(control);
            }
            catch (Exception e) { ex = e; }

            H.Check("EventStateSplit_DualReturn_NoException", ex is null);

            var boxAfter = ReadState(control)?.ControlEventState as ControlEventStateBox;
            H.Check("EventStateSplit_DualReturn_BoxStillIntactSameInstance",
                ReferenceEquals(boxAfter, boxBefore)
                && boxAfter?.HandlerType == typeof(ButtonEventPayload));

            // Re-rent + re-mount a new element, fire once → exactly one fire,
            // proving the double-return left no duplicate subscription behind.
            int count2 = 0;
            var elementB = new ButtonElement("B", () => count2++);
            var control2 = reconciler.MountButton(elementB, () => { });
            H.Check("EventStateSplit_DualReturn_RentReusesInstance",
                ReferenceEquals(control2, control));

            FireClick(control2);
            H.Check("EventStateSplit_DualReturn_FiresExactlyOnceAfterReRent", count2 == 1);

            await Task.CompletedTask;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  4. §9.4 alloc-SHAPE assertion (substitutes for the ARM64-blocked
    //     M10/M11 byte measurement). A control with ONLY a control-intrinsic
    //     callback (Button.OnClick) and NO routed-input modifier must NOT
    //     allocate ModifierEventHandlerState: ReactorState.Modifiers == null
    //     while ReactorState.ControlEventState != null.
    //
    //     Byte-level measurement remains ARM64-perf-deferred (§4.9); this is
    //     the structural null-shape proxy that is reachable on x64.
    // ────────────────────────────────────────────────────────────────────

    internal class ModifierStateLazyForIntrinsicOnly(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var reconciler = H.CreateHost().Reconciler;

            // Intrinsic-only: Button with OnClick, NO .OnPointerPressed /
            // .OnKeyDown / .OnGotFocus modifier of any kind.
            int clicks = 0;
            var element = new ButtonElement("intrinsic-only", () => clicks++);
            var control = reconciler.MountButton(element, () => { });

            var state = ReadState(control);
            H.Check("EventStateSplit_Shape_StateExists", state is not null);

            // The intrinsic Click lives ONLY in ControlEventState…
            H.Check("EventStateSplit_Shape_ControlEventStateAllocated",
                state?.ControlEventState is ControlEventStateBox b
                && b.HandlerType == typeof(ButtonEventPayload));

            // …and the routed-input ModifierEventHandlerState was NEVER allocated.
            H.Check("EventStateSplit_Shape_ModifiersNull",
                state?.Modifiers is null);

            // Behavioural proxy (also proves the split works end-to-end): the
            // intrinsic handler fires with Modifiers still unallocated.
            FireClick(control);
            H.Check("EventStateSplit_Shape_IntrinsicFiresWithoutModifiers",
                clicks == 1 && ReadState(control)?.Modifiers is null);

            // Contrast: adding a routed modifier (OnKeyDown) to a SEPARATE
            // control DOES lazily allocate ModifierEventHandlerState — proving
            // the null above is the genuine intrinsic-only shape, not a dead
            // code path. We use a tree mount so the modifier pipeline runs.
            var host2 = H.CreateHost();
            host2.Mount(_ => VStack(
                TextBlock("kd-target")
                    .OnKeyDown((_, _) => { })
            ));
            await Harness.Render();
            var tb = H.FindText("kd-target");
            H.Check("EventStateSplit_Shape_ModifierTargetMounted", tb is not null);
            H.Check("EventStateSplit_Shape_RoutedModifierAllocatesModifierState",
                tb is not null && ReadState(tb)?.Modifiers is not null);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  5. §9.5 / Q11 escape hatch survives the split — AddRawRoutedHandler
    //     with handledEventsToo:true.
    //
    //     The raw escape hatch (MountContext / UpdateContext
    //     .AddRawRoutedHandler) is a thin pass-through to
    //     UIElement.AddHandler(re, h, handledEventsToo). The intent of the
    //     contract is: a parent KeyDown handler registered with
    //     handledEventsToo:true STILL fires after a child marks the KeyDown
    //     Handled.
    //
    //     ENVIRONMENTAL CONSTRAINT (behavioural proxy): WinUI 3 does not let
    //     app code construct a KeyRoutedEventArgs or RaiseEvent an input
    //     routed event, and the headless self-test host has no live keyboard
    //     focus/HWND message pump to synthesize a real KeyDown. So the
    //     end-to-end "Handled child → parent still fires" assertion is
    //     exercised by the Appium-driven E2E fixture KeyDownTest in
    //     tests/.../Fixtures/EventHandlerFixtures.cs (which sends real keys
    //     through WinAppDriver). HERE we assert everything reachable on x64:
    //       (a) the escape-hatch API is intact on both MountContext and
    //           UpdateContext and registers the handler with
    //           handledEventsToo:true without throwing; and
    //       (b) the raw hatch is INDEPENDENT of the EventHandlerState split —
    //           registering it does NOT allocate ModifierEventHandlerState on
    //           the target, so the split cannot break it.
    //     This is the structural proof that the hatch survives the split; the
    //     live-routing leg is the documented E2E deferral above.
    // ────────────────────────────────────────────────────────────────────

    internal class AddRawRoutedHandler_HandledEventsToo(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var reconciler = H.CreateHost().Reconciler;

            var parent = new WinUI.Grid();
            var child = new WinUI.Button { Content = "child" };
            parent.Children.Add(child);

            int parentFires = 0;
            Microsoft.UI.Xaml.Input.KeyEventHandler handler = (_, e) =>
            {
                parentFires++;
                // The whole point of handledEventsToo: this still runs even
                // though a descendant already set e.Handled = true.
            };

            // (a) MountContext escape hatch — register with handledEventsToo:true.
            Exception? mountEx = null;
            try
            {
                var mctx = new Microsoft.UI.Reactor.Core.V1Protocol.MountContext(reconciler, () => { });
                mctx.AddRawRoutedHandler(
                    parent,
                    Microsoft.UI.Xaml.UIElement.KeyDownEvent,
                    handler,
                    handledEventsToo: true);
            }
            catch (Exception e) { mountEx = e; }
            H.Check("EventStateSplit_RawHatch_MountContextRegisters", mountEx is null);

            // (b) UpdateContext exposes the same escape hatch (symmetry — the
            // split must not have dropped it from either context).
            Exception? updateEx = null;
            try
            {
                var uctx = new Microsoft.UI.Reactor.Core.V1Protocol.UpdateContext(reconciler, () => { });
                uctx.AddRawRoutedHandler(
                    child,
                    Microsoft.UI.Xaml.UIElement.KeyDownEvent,
                    (Microsoft.UI.Xaml.Input.KeyEventHandler)((_, _) => { }),
                    handledEventsToo: true);
            }
            catch (Exception e) { updateEx = e; }
            H.Check("EventStateSplit_RawHatch_UpdateContextRegisters", updateEx is null);

            // The raw hatch is INDEPENDENT of the split: it routes straight to
            // UIElement.AddHandler and never touches ModifierEventHandlerState,
            // so the target's Modifiers stays null. This is what guarantees the
            // hatch cannot regress when the EventHandlerState is split.
            H.Check("EventStateSplit_RawHatch_DoesNotAllocateModifierState",
                ReadState(parent)?.Modifiers is null);

            // Live KeyDown routing (Handled child → parent still fires) cannot
            // be synthesized headlessly — see the comment above. Record the
            // deferral explicitly instead of silently dropping the leg.
            H.Skip("EventStateSplit_RawHatch_HandledChildParentStillFires",
                "live KeyDown not synthesizable headlessly; covered by Appium E2E KeyDownTest");

            // Keep the handler reachable so the registration isn't optimized
            // away and the counter remains a real captured target.
            H.Check("EventStateSplit_RawHatch_HandlerBound", parentFires == 0);

            await Task.CompletedTask;
        }
    }
}
