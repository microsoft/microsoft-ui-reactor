using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using Microsoft.UI.Reactor;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Wct = CommunityToolkit.WinUI.Controls;

namespace ToolkitCompatibility;

internal static class ImageCropperSampleChecks
{
    internal static async Task RunAsync(CompatibilityRoot root)
    {
        await root.ShowAsync(WctControls.SamplePages.CreatePage("cropper"));
        var cropper = CompatibilityRoot.Descendants(ReactorApp.PrimaryWindow!.NativeWindow.Content)
            .OfType<Wct.ImageCropper>().Single();
        await CompatibilityRoot.WaitAsync(() => cropper.IsLoaded && cropper.Source is not null,
            "actual sample cropper source");
        var source = cropper.Source!;
        Program.Check(source.PixelWidth == 480 && source.PixelHeight == 360,
            $"Expected the sample's deterministic 480x360 bitmap, got {source.PixelWidth}x{source.PixelHeight}.");
        Program.Check(cropper.TrySetCroppedRegion(new Windows.Foundation.Rect(40, 40, 160, 120)),
            "Toolkit rejected the deterministic crop rectangle.");
        using var output = new InMemoryRandomAccessStream();
        await cropper.SaveAsync(output, Wct.BitmapFileFormat.Png, true).WaitAsync(TimeSpan.FromSeconds(10));
        output.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(output);
        Program.Check(decoder.PixelWidth == 160 && decoder.PixelHeight == 120,
            $"Crop export dimensions were {decoder.PixelWidth}x{decoder.PixelHeight}, expected 160x120.");
        var decoded = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var pixels = decoded.DetachPixelData();
        CheckPixel(pixels, 0, 40, 40);
        CheckPixel(pixels, (160 * 120 - 1) * 4, 199, 159);
        output.Seek(0);
        string path = Path.Combine(Path.GetDirectoryName(Program.Options.Output)!, "ImageCropper.crop.png");
        using (var file = File.Create(path))
            await output.AsStreamForRead().CopyToAsync(file);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Program.Results.Note($"crop=160x120; source-rect=40,40,160,120; sha256={hash}; file={path}");
    }

    private static void CheckPixel(byte[] pixels, int offset, int x, int y)
    {
        int[] expected = [220 - y * 180 / 360, x * 200 / 480, 60 + y * 160 / 360, 255];
        for (int channel = 0; channel < 4; channel++)
            Program.Check(Math.Abs(pixels[offset + channel] - expected[channel]) <= 1,
                $"Crop pixel ({x},{y}) channel {channel}: expected {expected[channel]}, actual {pixels[offset + channel]}.");
    }
}
