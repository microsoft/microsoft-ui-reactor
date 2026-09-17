using System;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selftests for spec 049 Phase 3: subtree migration on component type-identity
/// change. When a <see cref="Component"/> subclass is edited under Hot Reload the
/// runtime mints a new <see cref="System.Type"/> token, so <c>CanUpdate</c>
/// returns false and the steady-state reconciler would unmount the whole subtree
/// — discarding every descendant's <c>UseState</c>. Phase 3 migrates the node in
/// place: it constructs the post-edit instance, copies surviving instance fields
/// old → new, transfers the live <see cref="RenderContext"/> (hooks survive), and
/// re-renders against the preserved wrapper so the <c>FrameworkElement</c>
/// identity is stable.
///
/// <para>The real entry point (<c>Reconciler.TryHotReloadMigrateComponent</c>) is
/// gated behind <c>HotReloadService.IsHotReloadLive</c>, which requires
/// <c>MetadataUpdater.IsSupported</c> — always false in-process — so the fixture
/// drives a bare reconciler and invokes the (InternalsVisibleTo) migration method
/// directly. Because the guard self-matches on <c>Type.FullName</c>, a fresh
/// same-type element exercises every mechanic (construct, copy, transfer,
/// re-render) without needing two runtime Types of identical name.</para>
/// </summary>
internal static class HotReloadComponentMigrationFixtures
{
    private sealed class CounterComponent : Component
    {
        internal static int Constructed;
        internal static CounterComponent? Last;
        internal static Action<int>? Setter;

        // A plain instance field the copier must carry old → new (a fresh
        // instance defaults it to 0, so a non-zero value proves the copy ran).
        internal int Marker;

        public CounterComponent()
        {
            Constructed++;
            Last = this;
        }

        public override Element Render()
        {
            var (count, set) = UseState(100);
            Setter = set;
            return TextBlock($"count={count},marker={Marker}");
        }
    }

    private sealed class OtherComponent : Component
    {
        public override Element Render() => TextBlock("other");
    }

    internal sealed class MigratesPreservingState(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            CounterComponent.Constructed = 0;
            CounterComponent.Last = null;
            CounterComponent.Setter = null;

            var reconciler = new Reconciler();
            Action noop = () => { };

            // Spec 010 — capture the two call sites so the migration's effect on the
            // reported location is observable. Mapping must be on BEFORE the elements
            // are built: the interceptor checks the flag at creation time.
            var previousMapping = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled;
            Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = true;
            try
            {

            var el0 = Component<CounterComponent>(); var line0 = Line();
            var wrapper = (Border)reconciler.Mount(el0, noop)!;
            var instanceA = CounterComponent.Last!;

            // Diverge instance state from a fresh default and mutate the
            // UseState cell synchronously (Setter writes the cell on the UI
            // thread before requesting a no-op re-render).
            instanceA.Marker = 42;
            CounterComponent.Setter!(555);

            H.Check("HRMig_Mounted_OneInstance", CounterComponent.Constructed == 1);

            // Migrate. Fresh same-type element ⇒ FullName self-matches the guard,
            // exercising construct + field-copy + context-transfer + re-render.
            var el1 = Component<CounterComponent>(); var line1 = Line();
            bool migrated = reconciler.TryHotReloadMigrateComponent(el0, el1, wrapper, noop);

            H.Check("HRMig_Returned_True", migrated);
            H.Check("HRMig_New_Instance_Constructed", CounterComponent.Constructed == 2);
            H.Check("HRMig_Instance_Swapped",
                !ReferenceEquals(CounterComponent.Last, instanceA));

            // Wrapper identity is the same control we passed in (FrameworkElement
            // continuity), and its re-rendered child reflects BOTH the preserved
            // UseState value (555) and the copied instance field (42).
            var tb = wrapper.Child as TextBlock;
            H.Check("HRMig_Wrapper_Identity_Stable",
                ReferenceEquals(CounterComponent.Last?.Context, instanceA.Context));
            H.Check("HRMig_State_And_Field_Preserved", tb?.Text == "count=555,marker=42");

            // Guard: a different component FullName must NOT migrate (the caller
            // would unmount/mount instead).
            var elOther = Component<OtherComponent>();
            bool guard = reconciler.TryHotReloadMigrateComponent(el1, elOther, wrapper, noop);
            H.Check("HRMig_DifferentType_NoMigrate", !guard);

#if REACTOR_SOURCEMAP
            // Spec 010 — hot reload is the scenario where a call site MOVES, so a
            // preserved wrapper reporting the pre-edit line is the exact staleness this
            // route claims to avoid. The two elements differ only in where they were
            // built, so this cannot pass by coincidence.
            H.Check("HRMig_CallSitesDiffer", line0 != line1 && line0 != 0);
            var reported = Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.GetSource(wrapper)?.LineNumber;
            H.Check("HRMig_LocationFollowsTheMigratedElement", reported == line1);
            H.Check("HRMig_LocationIsNotStale", reported != line0);
#else
            _ = line0; _ = line1;
            H.Skip("HRMig_CallSitesDiffer", SourceMapSkipReason);
            H.Skip("HRMig_LocationFollowsTheMigratedElement", SourceMapSkipReason);
            H.Skip("HRMig_LocationIsNotStale", SourceMapSkipReason);
#endif

            CounterComponent.Constructed = 0;
            CounterComponent.Last = null;
            CounterComponent.Setter = null;
            return Task.CompletedTask;
            }
            finally
            {
                Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = previousMapping;
            }
        }

        private const string SourceMapSkipReason =
            "built without REACTOR_SOURCEMAP, so no interceptors exist to stamp a location";

        private static int Line([global::System.Runtime.CompilerServices.CallerLineNumber] int line = 0) => line;
    }
}
