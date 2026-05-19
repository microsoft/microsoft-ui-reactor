using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Hosting;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Hosting;

/// <summary>
/// The render-loop responsiveness fix for PerfStress at 250/500 elements:
/// when a render exceeds the 60 Hz frame budget, the host enqueues the next
/// render at <see cref="DispatcherQueuePriority.Low"/> so the message pump can
/// interleave input/layout/paint. Without this, back-to-back Normal-priority
/// renders triggered from <c>await Task.Delay</c> continuations starve UI
/// input and the app feels frozen until the work finishes.
///
/// These tests pin the policy's decisions independent of the dispatcher so
/// regressions show up at unit-test time rather than as a UI freeze in the
/// PerfStress demo.
/// </summary>
public class RenderPriorityPolicyTests
{
    [Fact]
    public void ColdStart_UsesNormalPriority()
    {
        // Before any render has run, lastRenderMs == 0. The host MUST use
        // Normal priority — Low-priority cold-start would queue behind every
        // pending dispatcher item and delay first paint noticeably.
        Assert.Equal(
            DispatcherQueuePriority.Normal,
            RenderPriorityPolicy.PickPriority(lastRenderMs: 0));
    }

    [Fact]
    public void FastRender_StaysAtNormalPriority()
    {
        // A render that fits inside one 60 Hz frame keeps Normal priority.
        // Demoting fast renders would add latency for no benefit.
        Assert.Equal(
            DispatcherQueuePriority.Normal,
            RenderPriorityPolicy.PickPriority(lastRenderMs: 8));
    }

    [Fact]
    public void RenderAtFrameBoundary_StaysAtNormalPriority()
    {
        // Exactly at the budget is treated as "fit" — only renders strictly
        // longer than the budget demote. This avoids flip-flopping when a
        // render lands right at the boundary.
        Assert.Equal(
            DispatcherQueuePriority.Normal,
            RenderPriorityPolicy.PickPriority(lastRenderMs: RenderPriorityPolicy.DefaultFrameBudgetMs));
    }

    [Fact]
    public void SlowRender_DemotesToLowPriority()
    {
        // This is the PerfStress fix: a render that exceeds one frame budget
        // moves the next enqueue to Low priority. The PerfStress scenario
        // tops out at ~100 ms/render at 500 elements — well past the budget.
        Assert.Equal(
            DispatcherQueuePriority.Low,
            RenderPriorityPolicy.PickPriority(lastRenderMs: 100));
    }

    [Fact]
    public void JustOverBudget_DemotesToLowPriority()
    {
        // Even a small overrun (just past the 16 ms ceiling) demotes —
        // the goal is to keep the dispatcher from monopolizing the UI thread.
        Assert.Equal(
            DispatcherQueuePriority.Low,
            RenderPriorityPolicy.PickPriority(lastRenderMs: RenderPriorityPolicy.DefaultFrameBudgetMs + 0.5));
    }

    [Fact]
    public void CustomBudget_DemotesPastCustomCeiling()
    {
        // A perf-sensitive host can raise or lower the ceiling. The policy
        // honors the override.
        Assert.Equal(
            DispatcherQueuePriority.Low,
            RenderPriorityPolicy.PickPriority(lastRenderMs: 12, budgetMs: 8));

        Assert.Equal(
            DispatcherQueuePriority.Normal,
            RenderPriorityPolicy.PickPriority(lastRenderMs: 12, budgetMs: 32));
    }

    [Fact]
    public void DefaultBudget_IsOneFrameAt60Hz()
    {
        // Pin the budget so a future "just bump it up" change is a conscious
        // edit, not a silent regression.
        Assert.Equal(16.0, RenderPriorityPolicy.DefaultFrameBudgetMs);
    }

    [Fact]
    public void ReactorHost_TracksLastRenderMs()
    {
        // The wiring contract: ReactorHost stores the most-recent render
        // duration so RequestRender can consult RenderPriorityPolicy.
        // Without this field, the policy can't see a slow render and the
        // PerfStress regression returns.
        var field = typeof(ReactorHost).GetField("_lastRenderMs",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        Assert.Equal(typeof(double), field!.FieldType);
    }

    [Fact]
    public void ReactorHostControl_TracksLastRenderMs()
    {
        // Symmetric contract with ReactorHost — embedded hosts must also
        // demote to Low priority on slow renders.
        var field = typeof(ReactorHostControl).GetField("_lastRenderMs",
            global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance);
        Assert.NotNull(field);
        Assert.Equal(typeof(double), field!.FieldType);
    }
}
