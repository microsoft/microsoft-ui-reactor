using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #949 — pins the mount-time half of the descriptor teardown seam
/// (<c>ControlDescriptor.OnUnmount</c> / <c>.WithUnmount(...)</c>).
///
/// <para><b>Why this needs a live control.</b> The engine's unmount dispatch is tag-gated:
/// <c>Reconciler.UnmountRecursive</c> only reaches the V1 handler when <c>GetElementTag</c>
/// returns an element, and <c>SetElementTagIfNeeded</c> allocates <c>ReactorState</c> only for
/// elements carrying callbacks, a key, extensions, or reference modifiers. So
/// <c>DescriptorHandler.Mount</c> forces the state when a descriptor declares <c>OnUnmount</c>,
/// or the hook would fire for callback-bearing elements of a type and silently not for
/// callback-free ones.</para>
///
/// <para><b>Why the TeachingTip fixture does not cover this.</b> A TeachingTip always ends up
/// with <c>ReactorState</c> anyway — its <c>Target</c> reference entry calls
/// <c>WireReferenceEdge</c> on every mount, and arming the deferred open allocates the payload
/// box. Both mask the tag-forcing. The probes below are deliberately barren so the forcing is
/// the only thing that can tag them.</para>
///
/// <para><b>How "barren" is proved.</b> Not by re-stating <c>Reconciler.NeedsTag</c>'s clauses in
/// the test — that mirror would silently rot the moment either side changed. Instead the two
/// probes differ <i>only</i> by the presence of the hook, and the no-hook one is asserted to come
/// back untagged. That is the same predicate the engine actually ran, so the tagged/untagged
/// split can only be attributed to <c>OnUnmount</c>.</para>
/// </summary>
internal static class DescriptorUnmountHookFixtures
{
    /// <summary>Barren by design — no callbacks, key, extensions or modifiers. Adding any of
    /// those would make <c>NeedsTag</c> true on its own and quietly defeat the test.</summary>
    private sealed record UnmountHookProbe : Element;

    /// <summary>Identical shape to <see cref="UnmountHookProbe"/>; its descriptor declares no
    /// hook. The control half of the differential.</summary>
    private sealed record NoHookProbe : Element;

    private static int _unmountCalls;

    private sealed class HookHandler : DescriptorHandler<UnmountHookProbe, TextBlock>
    {
        public HookHandler() : base(HookDescriptor) { }

        // Named to avoid hiding DescriptorHandler<,>.Descriptor (CS0108 — a warning locally,
        // an error under CI's warnings-as-errors).
        private static readonly ControlDescriptor<UnmountHookProbe, TextBlock> HookDescriptor =
            new ControlDescriptor<UnmountHookProbe, TextBlock>()
                .WithUnmount(static (in UnmountContext _, TextBlock _) => _unmountCalls++);
    }

    private sealed class NoHookHandler : DescriptorHandler<NoHookProbe, TextBlock>
    {
        public NoHookHandler() : base(new ControlDescriptor<NoHookProbe, TextBlock>()) { }
    }

    internal sealed class HookFiresForCallbackFreeElement(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ControlRegistry.Register<UnmountHookProbe, TextBlock>(static () => new HookHandler());
            ControlRegistry.Register<NoHookProbe, TextBlock>(static () => new NoHookHandler());

            var rec = new Reconciler();
            var parent = new Grid();
            H.SetContent(parent);

            _unmountCalls = 0;

            if (rec.Mount(new NoHookProbe(), static () => { }) is not TextBlock baseline ||
                rec.Mount(new UnmountHookProbe(), static () => { }) is not TextBlock control)
            {
                H.Check("DescriptorUnmount_ProbesMounted", false);
                return;
            }

            parent.Children.Add(baseline);
            parent.Children.Add(control);
            await Harness.Render();

            // The engine's own verdict on this element shape: no hook -> no state -> unmount
            // dispatch could never reach a handler. This is the load-bearing precondition, and it
            // is measured rather than re-derived from NeedsTag's clauses.
            var baselineUntagged = Reconciler.GetElementTag(baseline) is null;
            H.Check("DescriptorUnmount_BarrenElementIsUntaggedWithoutTheHook", baselineUntagged);

            // Same shape, hook declared -> state forced. Paired with the line above, the only
            // difference that can explain the tag is OnUnmount.
            H.Check("DescriptorUnmount_DeclaringTheHookForcesReactorState",
                baselineUntagged && Reconciler.GetElementTag(control) is not null);

            H.Check("DescriptorUnmount_HookNotCalledBeforeUnmount", _unmountCalls == 0);

            rec.UnmountChild(control);
            await Harness.Render();

            H.Check("DescriptorUnmount_HookFiresOnceForCallbackFreeElement",
                baselineUntagged && _unmountCalls == 1);

            // The no-hook probe must contribute nothing on the way out either.
            rec.UnmountChild(baseline);
            await Harness.Render();

            H.Check("DescriptorUnmount_NoHookDeclaredInvokesNothing", _unmountCalls == 1);

            parent.Children.Clear();
        }
    }
}
