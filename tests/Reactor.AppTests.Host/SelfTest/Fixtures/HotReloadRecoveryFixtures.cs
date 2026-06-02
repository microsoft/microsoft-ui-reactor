using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Selftests for spec 049 Phase 1: tree-wide hot-reload hook-order recovery.
///
/// Before Phase 1, a hot-reload edit that reordered/retyped a hook in a
/// <em>non-root</em> child surfaced the resulting <see cref="HookOrderException"/>
/// through the reconciler's generic render catch, replacing that child's
/// subtree with the render-error fallback. Phase 1 makes the reconciler, while
/// inside a hot-reload pass (<see cref="HotReloadService.WithinUpdatePass"/>),
/// reset just that child's hook state and re-render it once — so the edit
/// applies cleanly and sibling subtrees keep their state.
///
/// The fixture simulates the "edit" with a static shape flag the child reads:
/// flipping it changes the child's hook call sequence, exactly as a code edit
/// would after a metadata update. A real hot-reload pass is driven through the
/// host by raising the pending-update flag via
/// <see cref="HotReloadService.UpdateApplication"/>.
/// </summary>
internal static class HotReloadRecoveryFixtures
{
    // Drives the edited child's hook shape. 0 = pre-edit, 1 = post-edit.
    private static int _childShape;

    // Sibling component: owns state that must survive a sibling's hook-order
    // recovery. Increment via the button, then assert the value persists.
    private sealed class SiblingComponent : Component
    {
        public override Element Render()
        {
            var (count, set) = UseState(0);
            return VStack(
                TextBlock($"Sibling: {count}"),
                Button("SiblingInc", () => set(count + 1))
            );
        }
    }

    // Edited child: shape 0 declares a UseState at index 0; shape 1 declares a
    // UseEffect at index 0 (type mismatch against the prior ValueHookState) →
    // HookOrderException on the first post-edit render. After recovery resets
    // its hook list, the shape-1 sequence is accepted as a fresh mount.
    private sealed class EditedChildComponent : Component
    {
        public override Element Render()
        {
            if (Volatile.Read(ref _childShape) == 0)
            {
                UseState(0);
                return TextBlock("Child: v1");
            }

            UseEffect(() => { }, "hot-reload");
            UseState(0);
            return TextBlock("Child: v2");
        }
    }

    internal sealed class ChildRecoversAndSiblingStateSurvives(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            Volatile.Write(ref _childShape, 0);

            var host = H.CreateHost();
            host.Mount(_ => VStack(
                Component<SiblingComponent>(),
                Component<EditedChildComponent>()
            ));

            await Harness.Render();
            H.Check("HotReload_Child_Initial_v1", H.FindText("Child: v1") is not null);
            H.Check("HotReload_Sibling_Initial", H.FindText("Sibling: 0") is not null);

            // Mutate sibling state so we can prove it survives the recovery.
            H.ClickButton("SiblingInc");
            await Harness.Render();
            H.Check("HotReload_Sibling_Incremented", H.FindText("Sibling: 1") is not null);

            // Simulate the developer's edit: the child's hook order changes.
            Volatile.Write(ref _childShape, 1);

            // Drive a real hot-reload pass: raise the pending-update flag and
            // force a render. The reconciler should recover the child's
            // hook-order break in-pass instead of showing the error fallback.
            HotReloadService.UpdateApplication(null);
            host.RequestRender(force: true);
            await Harness.Render();

            H.Check("HotReload_Child_Recovered_v2", H.FindText("Child: v2") is not null);
            H.Check("HotReload_Child_NoErrorFallback",
                H.FindText("\u26A0 Render error") is null);
            H.Check("HotReload_Sibling_State_Preserved", H.FindText("Sibling: 1") is not null);

            Volatile.Write(ref _childShape, 0);
        }
    }
}
