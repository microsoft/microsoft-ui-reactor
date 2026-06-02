using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 048 §8 — global <see cref="ControlRegistry"/> dispatch
/// precedence, live-WinUI fixtures. The unit tests under
/// <c>tests/Reactor.Tests/Spec048/V1Protocol/ControlRegistryTests.cs</c>
/// exercise the registry primitive and the
/// <see cref="Reconciler.TryResolveFromControlRegistry"/> helper directly;
/// these fixtures exercise the same dispatch path through real Mount /
/// Update against a live WinUI control, the way an end-user app would.
///
/// <para><b>Why nested probe types instead of <c>MarqueeElement</c>.</b>
/// Spec 047's external proof control was converted to Pattern A in
/// Phase 2 — <c>Marquee</c>'s static cctor registers <c>MarqueeHandler</c>
/// globally on the first <c>Marquee.Of(...)</c> call, and the CLR runs
/// each type initializer at most once per process. If these fixtures
/// shared the <c>MarqueeElement</c> slot in the global table they would
/// fight the Pattern A registration (the counted factories below would
/// race the static lambda). Each fixture below therefore declares its own
/// nested element + handler pair, registered directly via
/// <see cref="ControlRegistry.Register{TElement,TControl}"/>; this keeps
/// every fixture hermetic and independent of test ordering.</para>
///
/// <para>Coverage:</para>
/// <list type="bullet">
///   <item><b>Arm 3 dispatch</b> — register a probe handler globally via
///         <see cref="ControlRegistry.Register{TElement,TControl}"/>,
///         mount a tree containing the probe element with <i>no</i>
///         explicit <see cref="Reconciler.RegisterHandler{TElement,TControl}"/>
///         call. The arm 3 fallback in <see cref="Reconciler.Mount"/>
///         resolves the handler from the global table and the control
///         renders end-to-end.</item>
///   <item><b>Per-host cache</b> — after the first mount, the global
///         registry's factory delegate is not invoked again on the same
///         host. Subsequent updates / mounts of the same element type
///         short-circuit in <c>_v1Handlers</c>.</item>
///   <item><b>Per-host shadow precedence</b> — an explicit
///         <see cref="Reconciler.RegisterHandler{TElement,TControl}"/>
///         call (made before any dispatch) wins over the globally
///         registered factory: the global factory delegate is never
///         invoked on that host.</item>
/// </list>
/// </summary>
internal static class Spec048ControlRegistryFixtures
{
    // ────────────────────────────────────────────────────────────────────
    //  Shared probe shape — a value-bearing Caption written into a TextBlock.
    //  Each fixture nests its own element record so the global registry
    //  slot is unique per fixture (no cross-fixture contention).
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Probe handler driving <typeparamref name="TProbe"/> ↦
    /// <see cref="TextBlock"/>. Writes the element's caption into the
    /// TextBlock on mount and on update.</summary>
    private sealed class ProbeHandler<TProbe> : IElementHandler<TProbe, TextBlock>
        where TProbe : Element
    {
        private readonly Func<TProbe, string> _captionAccessor;
        public ProbeHandler(Func<TProbe, string> captionAccessor) => _captionAccessor = captionAccessor;

        public TextBlock Mount(MountContext ctx, TProbe element)
        {
            var tb = ctx.RentControl<TextBlock>();
            tb.Text = _captionAccessor(element);
            return tb;
        }

        public void Update(UpdateContext ctx, TProbe oldEl, TProbe newEl, TextBlock control)
        {
            var caption = _captionAccessor(newEl);
            if (control.Text != caption) control.Text = caption;
        }

        public ChildrenStrategy<TProbe, TextBlock>? Children => null;
    }

    // ────────────────────────────────────────────────────────────────────
    //  Arm 3 dispatch — global registration suffices, no per-host call.
    // ────────────────────────────────────────────────────────────────────

    /// <summary>Element used exclusively by
    /// <see cref="GlobalRegistry_Mount_DispatchesThroughArm3"/>.</summary>
    private sealed record ArmThreeProbe(string Caption) : Element;

    internal class GlobalRegistry_Mount_DispatchesThroughArm3(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int factoryCalls = 0;
            ControlRegistry.Register<ArmThreeProbe, TextBlock>(() =>
            {
                Interlocked.Increment(ref factoryCalls);
                return new ProbeHandler<ArmThreeProbe>(static p => p.Caption);
            });

            var host = H.CreateHost();
            // NOTE: no host.Reconciler.RegisterHandler call. The dispatch
            // path must reach arm 3 (ControlRegistry) on the first Mount.
            host.Mount(_ => VStack(new ArmThreeProbe("via-global-registry")));

            await Harness.Render();

            var tb = H.FindControl<TextBlock>(t => t.Text == "via-global-registry");
            H.Check("Spec048_GlobalRegistry_Mounted", tb is not null);
            H.Check("Spec048_GlobalRegistry_CaptionApplied",
                tb?.Text == "via-global-registry");
            H.Check("Spec048_GlobalRegistry_FactoryInvokedOnce",
                factoryCalls == 1);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Per-host caching — factory delegate runs exactly once per host
    //  regardless of how many subsequent mounts / updates dispatch for
    //  that element type.
    // ────────────────────────────────────────────────────────────────────

    private sealed record CacheProbe(string Caption) : Element;

    internal class GlobalRegistry_FactoryCachedAfterFirstHit(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int factoryCalls = 0;
            ControlRegistry.Register<CacheProbe, TextBlock>(() =>
            {
                Interlocked.Increment(ref factoryCalls);
                return new ProbeHandler<CacheProbe>(static p => p.Caption);
            });

            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (caption, setCaption) = ctx.UseState("first");
                return VStack(
                    new CacheProbe(caption),
                    Button("Update", () => setCaption("second"))
                );
            });

            await Harness.Render();
            H.Check("Spec048_Cache_InitialFactoryCall", factoryCalls == 1);

            // Trigger an update. Dispatch arm 1 (_v1Handlers cache) hits —
            // the registry's factory must not run again.
            H.ClickButton("Update");
            await Harness.Render();

            var tb = H.FindControl<TextBlock>(t => t.Text == "second");
            H.Check("Spec048_Cache_UpdateApplied", tb is not null);
            H.Check("Spec048_Cache_FactoryNotReinvokedOnUpdate",
                factoryCalls == 1);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Shadow precedence — explicit RegisterHandler wins over the global
    //  table on the same host. The global factory delegate never runs on
    //  that host because the per-host arm 1 cache hits first.
    // ────────────────────────────────────────────────────────────────────

    private sealed record ShadowProbe(string Caption) : Element;

    internal class PerHost_RegisterHandler_ShadowsGlobal(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            int globalFactoryCalls = 0;
            ControlRegistry.Register<ShadowProbe, TextBlock>(() =>
            {
                Interlocked.Increment(ref globalFactoryCalls);
                return new ProbeHandler<ShadowProbe>(static p => "GLOBAL-" + p.Caption);
            });

            var host = H.CreateHost();
            // Per-host registration installed BEFORE first Mount so arm 1
            // wins for the first dispatch. The shadow handler tags its
            // output with "PERHOST-" so we can distinguish at the control.
            host.Reconciler.RegisterHandler<ShadowProbe, TextBlock>(
                new ProbeHandler<ShadowProbe>(static p => "PERHOST-" + p.Caption));

            host.Mount(_ => VStack(new ShadowProbe("shadowed")));
            await Harness.Render();

            var tb = H.FindControl<TextBlock>(t =>
                t.Text == "PERHOST-shadowed" || t.Text == "GLOBAL-shadowed");
            H.Check("Spec048_Shadow_Mounted", tb is not null);
            H.Check("Spec048_Shadow_PerHostHandlerWon",
                tb?.Text == "PERHOST-shadowed");
            // The defining assertion: the global factory delegate never
            // ran on this host because arm 1 shadowed arm 3.
            H.Check("Spec048_Shadow_GlobalFactoryNeverInvoked",
                globalFactoryCalls == 0);
        }
    }
}
