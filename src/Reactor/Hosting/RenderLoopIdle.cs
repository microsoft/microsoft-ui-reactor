using System.Diagnostics;
using Microsoft.UI.Dispatching;

namespace Microsoft.UI.Reactor.Hosting;

/// <summary>
/// The "wait until this host's render loop settles" loop shared by
/// <see cref="ReactorHost.WaitForIdleAsync"/> and
/// <see cref="ReactorHostControl.WaitForIdleAsync"/>. Both hosts run the same
/// coalescing render loop (Normal-priority first render, Low-priority re-renders), so the
/// wait is identical: yield to the dispatcher at Low priority until the host reports idle.
/// </summary>
/// <remarks>
/// The <c>tryEnqueue</c> parameter abstracts <see cref="DispatcherQueue.TryEnqueue(DispatcherQueuePriority, DispatcherQueueHandler)"/>
/// so the loop can be tested headlessly; production passes the host's queue.
/// </remarks>
internal static class RenderLoopIdle
{
    internal static Task WaitAsync(
        Func<bool> isIdle,
        Func<DispatcherQueuePriority, DispatcherQueueHandler, bool> tryEnqueue,
        int maxYields,
        Func<string> describeState)
    {
        if (isIdle()) return Task.CompletedTask;
        if (maxYields <= 0) return Task.CompletedTask;

        // RunContinuationsAsynchronously: TrySetResult is called from a
        // dispatcher callback, and without this flag any await continuation
        // would run inline on the dispatcher at Low priority — re-entering
        // UI logic inside the yield loop and partially defeating its purpose.
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Dispatcher yields queued so far; the initial enqueue below is the first, so the
        // wait yields at most maxYields times.
        int yields = 1;
        void CheckIdle()
        {
            if (isIdle())
            {
                tcs.TrySetResult();
                return;
            }
            if (yields >= maxYields)
            {
                // Returning early here is the classic flake source: callers
                // (e.g. selftest Harness.Render) move on against a half-settled
                // tree. Log so the next flake is greppable instead of silent.
                Debug.WriteLine($"[Reactor.WaitForIdle] yield cap hit ({maxYields}); {describeState()}");
                tcs.TrySetResult();
                return;
            }
            // Queue refused the enqueue (shutdown): complete rather than hang the caller.
            if (!tryEnqueue(DispatcherQueuePriority.Low, CheckIdle))
                tcs.TrySetResult();
            else
                yields++;
        }
        if (!tryEnqueue(DispatcherQueuePriority.Low, CheckIdle))
            tcs.TrySetResult();
        return tcs.Task;
    }
}
