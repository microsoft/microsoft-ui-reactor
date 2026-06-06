using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>Spec 054 Phase 6 fixtures for the TaskbarItem facade.</summary>
internal static class Phase6WindowingFixtures
{
    private static void EnsureUIDispatcher()
    {
        if (ReactorApp.UIDispatcher is null)
            ReactorApp.UIDispatcher = DispatcherQueue.GetForCurrentThread();
        ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
    }

    private sealed class StubComponent : Component
    {
        public override Element Render() => TextBlock("ok");
    }

    private static async Task<ReactorWindow> OpenAndSettle(WindowSpec spec)
    {
        var win = ReactorApp.OpenWindow(spec, () => new StubComponent());
        await win.Host.WaitForIdleAsync();
        await Harness.Render(80);
        return win;
    }

    private static async Task CloseAndSettle(params ReactorWindow?[] windows)
    {
        foreach (var win in windows)
        {
            if (win is null) continue;
            try { win.Close(); } catch { }
        }
        await Task.Delay(100);
    }

    internal class TaskbarItemDescriptionRoundTrip(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "Taskbar Description", Width = 220, Height = 160 });
            try
            {
                var item = win.TaskbarItem;
                item.Description = "tip";
                H.Check("TaskbarItem_Description_RoundTrip_Set", item.Description == "tip");

                item.Description = null;
                H.Check("TaskbarItem_Description_RoundTrip_ClearNull", item.Description is null);

                item.Description = string.Empty;
                H.Check("TaskbarItem_Description_RoundTrip_ClearEmpty", item.Description == string.Empty);
            }
            finally { await CloseAndSettle(win); }
        }
    }

    internal class TaskbarItemProgressRegression(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            EnsureUIDispatcher();
            var win = await OpenAndSettle(new WindowSpec { Title = "Taskbar Progress Facade", Width = 220, Height = 160 });
            try
            {
                var item = win.TaskbarItem;
                H.Check("TaskbarItem_Progress_SameInstance", ReferenceEquals(item.Progress, win.Progress));
                H.Check("TaskbarItem_Overlay_SameInstance", ReferenceEquals(item.Overlay, win.Overlay));

                var progress = item.Progress;
                progress.State = TaskbarProgressState.Indeterminate;
                H.Check("TaskbarItem_Progress_StateIndeterminate", progress.State == TaskbarProgressState.Indeterminate);

                progress.Value = 0.42;
                H.Check("TaskbarItem_Progress_ValueRoundTrip", Math.Abs(progress.Value - 0.42) < 0.0001);

                progress.State = TaskbarProgressState.Paused;
                H.Check("TaskbarItem_Progress_StatePaused", progress.State == TaskbarProgressState.Paused);

                progress.Clear();
                H.Check("TaskbarItem_Progress_ClearedState", progress.State == TaskbarProgressState.None);
                H.Check("TaskbarItem_Progress_ClearedValue", Math.Abs(progress.Value) < 1e-9);
            }
            finally { await CloseAndSettle(win); }
        }
    }
}
