using CommunityToolkit.WinUI.Controls;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.UI.Reactor;
using WctControls;
using static Microsoft.UI.Reactor.Factories;

namespace ToolkitCompatibility;

internal static class CameraSampleChecks
{
    internal static async Task RunAsync(CompatibilityRoot root, bool allowCamera)
    {
        Program.Check(allowCamera, "Camera probe requires explicit permission-approved --allow-camera true.");
        var first = await CheckPreviewAsync(root, 1, null, null);
        await CheckPreviewAsync(root, 2, first.Control, first.Helper);
    }

    private static async Task<(CameraPreview Control, CameraHelper Helper)> CheckPreviewAsync(
        CompatibilityRoot root, int generation, CameraPreview? previousControl, CameraHelper? previousHelper)
    {
        await root.ShowAsync(SamplePages.CreatePage("camera"));
        var control = CompatibilityRoot.Descendants(ReactorApp.PrimaryWindow!.NativeWindow.Content)
            .OfType<CameraPreview>().Single();
        var lifetime = CameraPreviewElement.GetLifetime(control);
        FrameCounter? frames = null;
        try
        {
            Program.Check(!ReferenceEquals(previousControl, control), "Re-entry reused the native preview.");
            await CompatibilityRoot.WaitAsync(() => control.CameraHelper is not null || lifetime.HasErrors,
                "Camera helper creation");
            Program.Check(!lifetime.HasErrors, "Camera startup failed; inspect PreviewFailed stderr for denied/no-device details.");
            var helper = control.CameraHelper;
            Program.Check(helper is not null && !ReferenceEquals(previousHelper, helper),
                "Re-entry did not acquire a fresh owned CameraHelper.");
            frames = new FrameCounter(helper!);
            await CompatibilityRoot.WaitAsync(() => frames.Count >= 3 || lifetime.HasErrors,
                "Three live frame events (no pixels retained)");
            Program.Check(!lifetime.HasErrors && frames.Count >= 3,
                "No successful live preview; denied/no-device is not a pass.");
            Program.Results.Note($"camera generation={generation}; live-frame-events={frames.Count}");

            await root.ShowAsync(TextBlock("Camera removed"));
            await lifetime.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Program.Check(!lifetime.HasErrors, "Camera lifecycle reported failure; cleanup completion is not success.");
            Program.Check(!control.IsLoaded, "Camera is still attached after navigation.");

            // Let an already-dispatched frame callback finish before measuring silence.
            await Task.Delay(150);
            int afterCleanup = frames.Count;
            await Task.Delay(350);
            int later = frames.Count;
            Program.Results.Note($"camera generation={generation}; frames-after-cleanup={afterCleanup}; frames-later={later}");
            Program.Check(afterCleanup >= 3 && later == afterCleanup,
                "Capture continued delivering frames after awaited cleanup.");
            return (control, helper!);
        }
        finally
        {
            // Failure paths still request release. This cannot make a timed-out
            // navigation/cleanup assertion above pass.
            CameraPreviewElement.StopPreview(control);
            try
            {
                await lifetime.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                frames?.Dispose();
            }
        }
    }

    private sealed class FrameCounter : IDisposable
    {
        private readonly CameraHelper _helper;
        private int _count;

        public FrameCounter(CameraHelper helper)
        {
            _helper = helper;
            _helper.FrameArrived += OnFrameArrived;
        }

        public int Count => Volatile.Read(ref _count);

        private void OnFrameArrived(object? sender, FrameEventArgs args)
        {
            // Count delivery only. Do not copy, encode, display, or retain pixels.
            args.VideoFrame.Dispose();
            Interlocked.Increment(ref _count);
        }

        public void Dispose() => _helper.FrameArrived -= OnFrameArrived;
    }
}
