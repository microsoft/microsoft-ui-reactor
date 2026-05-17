using Microsoft.UI.Dispatching;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// Decides the dispatcher-queue priority for the next render enqueue based on
/// recent render duration.
/// <para>
/// When a render is faster than a 60 Hz frame, the next render is enqueued at
/// <see cref="DispatcherQueuePriority.Normal"/> — the default, lowest latency.
/// When the previous render exceeded the budget, subsequent renders are demoted
/// to <see cref="DispatcherQueuePriority.Low"/> so that input, layout, and paint
/// messages on the same UI thread are interleaved between renders. Without this,
/// a high-frequency state-change source (animation, simulation, streaming data)
/// can fill the dispatcher with back-to-back renders that starve pointer/keyboard
/// input — the app feels frozen even though renders are still committing pixels.
/// </para>
/// </summary>
internal static class RenderPriorityPolicy
{
    /// <summary>
    /// Render-duration ceiling beyond which subsequent renders are demoted to
    /// Low priority. 16 ms is one 60 Hz frame; a render past this point gives
    /// up its Normal-priority slot so input/layout/paint catch up.
    /// </summary>
    public const double DefaultFrameBudgetMs = 16.0;

    /// <summary>
    /// Decide the priority for the next render enqueue.
    /// Returns <see cref="DispatcherQueuePriority.Low"/> when the last render
    /// exceeded the budget, <see cref="DispatcherQueuePriority.Normal"/>
    /// otherwise (including the cold-start case where no render has run yet
    /// and <paramref name="lastRenderMs"/> is 0).
    /// </summary>
    public static DispatcherQueuePriority PickPriority(
        double lastRenderMs,
        double budgetMs = DefaultFrameBudgetMs)
        => lastRenderMs > budgetMs
            ? DispatcherQueuePriority.Low
            : DispatcherQueuePriority.Normal;
}
