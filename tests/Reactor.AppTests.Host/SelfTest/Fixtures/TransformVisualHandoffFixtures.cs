using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Composition;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// A facade transform modifier combined with a feature that takes the element's handoff
/// visual (#1613). WinUI refuses <c>GetElementVisual</c> once a facade transform holds a
/// non-identity value, so each case checks that the feature no longer throws and that the
/// transform still renders, using a RenderTargetBitmap bounding box of a red 100x40 target
/// laid out at (150,150) in a 400x400 white stage.
/// </summary>
/// <remarks>
/// Orders: <c>Mount</c> mounts the transform and the feature together, <c>Update</c> adds the
/// feature to an element that already has the transform, and <c>FeatureFirst</c> adds the
/// transform to an element whose feature already ran.
/// </remarks>
internal sealed class TransformVisualHandoffFixture(Harness h, string feature) : SelfTestFixtureBase(h)
{
    private const float Left = 150, Top = 150, Width = 100, Height = 40, Tolerance = 2;
    private static readonly Vector3 Center = new(50, 20, 0);
    private static readonly string[] Transforms = ["CenterPoint", "Rotation", "Scale", "Translation"];

    private readonly record struct Pose(Vector3 Scale, float Degrees, Vector3 Translation, Vector3 CenterPoint)
    {
        public static Pose Identity => new(Vector3.One, 0, Vector3.Zero, Vector3.Zero);
        public Pose Scaled(float factor) => this with { Scale = new Vector3(Scale.X * factor, Scale.Y * factor, Scale.Z) };
    }

    public override async Task RunAsync()
    {
        string[] orders = feature == "Animate" ? ["Mount", "Update"] : ["Mount", "Update", "FeatureFirst"];
        foreach (string transform in Transforms)
            foreach (string order in orders)
                await RunCase(transform, order);
        H.SetContent(null);
    }

    private static Element WithTransform(Element e, string transform) => transform switch
    {
        "CenterPoint" => e.CenterPoint(Center),
        "Rotation" => e.Rotation(90),
        "Scale" => e.Scale(1.5f),
        _ => e.Translation(20, 10, 0),
    };

    private static Pose Declared(string transform) => transform switch
    {
        "CenterPoint" => Pose.Identity with { CenterPoint = Center },
        "Rotation" => Pose.Identity with { Degrees = 90 },
        "Scale" => Pose.Identity with { Scale = new Vector3(1.5f, 1.5f, 1) },
        _ => Pose.Identity with { Translation = new Vector3(20, 10, 0) },
    };

    private Element WithFeature(Element e, string transform, bool longTransition) => feature switch
    {
        "InteractionStates" => e.InteractionStates(s => s.PointerOver(scale: 2f), Curve.Linear(30)),
        "Transition" => e.Transition(Animation.Transition.Scale(0.5f), Curve.Linear(longTransition ? 60000 : 30)),
        // Scale keyframes make the CenterPoint pivot visible. The other transforms play an
        // opacity pulse that ends opaque, so the declared transform must survive it unchanged.
        "Keyframes" => e.Keyframes("handoff", 1, k => transform == "CenterPoint"
            ? k.Duration(30).At(0f, scale: Vector3.One).At(1f, scale: new Vector3(2, 2, 1))
            : k.Duration(30).At(0f, opacity: 0.5f).At(1f, opacity: 1f)),
        _ => e.Animate(Curve.Linear(30)),
    };

    private static Element Target() => Border(TextBlock(" "))
        .Width(Width).Height(Height)
        .Background(new SolidColorBrush(Microsoft.UI.Colors.Red))
        .HAlign(HorizontalAlignment.Left)
        .Margin(Left, Top, 0, 0);

    private static Element Stage(Element? child) => VStack(child)
        .Width(400).Height(400)
        .Background(new SolidColorBrush(Microsoft.UI.Colors.White))
        .HAlign(HorizontalAlignment.Left).VAlign(VerticalAlignment.Top);

    private async Task RunCase(string transform, string order)
    {
        string name = $"TransformHandoff_{feature}_{transform}_{order}";
        using var reconciler = new Reconciler();
        bool longTransition = feature == "Transition" && order == "Mount";
        Element transformed = WithTransform(Target(), transform);
        Element full = WithFeature(transformed, transform, longTransition);
        Element initial = order switch
        {
            "Mount" => full,
            "Update" => transformed,
            _ => WithFeature(Target(), transform, longTransition),
        };

        StackPanel stage;
        try
        {
            stage = (StackPanel)reconciler.Mount(Stage(initial), static () => { })!;
            H.SetContent(stage);
            await Settle();
            if (order != "Mount")
            {
                reconciler.Update(Stage(initial), Stage(full), stage, static () => { });
                await Settle();
            }
        }
        catch (Exception ex)
        {
            H.Check(name + "_NoThrow", false, $"{ex.GetType().Name}: {ex.Message}");
            H.SetContent(null);
            return;
        }

        var target = (Border)stage.Children[0];
        var declared = Declared(transform);
        try
        {
            switch (feature)
            {
                case "InteractionStates":
                    Reconciler.SimulatePointerOverForTests(target, isOver: true);
                    await CheckPose(name + "_HoverIn", stage, declared with { Scale = new Vector3(2, 2, 1) });
                    Reconciler.SimulatePointerOverForTests(target, isOver: false);
                    await CheckPose(name + "_HoverOut", stage, declared);
                    break;

                case "Transition" when order == "Mount":
                {
                    // The enter scale runs for a minute. Pausing it at each end makes the frame
                    // deterministic: it starts at half the declared size and ends at it.
                    var controller = ElementCompositionPreview.GetElementVisual(target).TryGetAnimationController("Scale");
                    H.Check(name + "_EnterStarted", controller is not null);
                    if (controller is null) break;
                    controller.Pause();
                    controller.Progress = 0;
                    await CheckPose(name + "_EnterStart", stage, declared.Scaled(0.5f));
                    controller.Progress = 1;
                    await CheckPose(name + "_EnterEnd", stage, declared);
                    break;
                }

                case "Transition" when order == "Update":
                {
                    // The enter side only runs on mount. The exit takes the visual on removal.
                    var withExit = WithTransform(Target(), transform)
                        .Transition(Animation.Transition.Scale(0.5f), Curve.Linear(60000));
                    reconciler.Update(Stage(full), Stage(withExit), stage, static () => { });
                    await CheckExit(name, reconciler, stage, target, withExit, declared);
                    break;
                }

                case "Keyframes" when transform == "CenterPoint":
                    await CheckPose(name + "_Played", stage, declared with { Scale = new Vector3(2, 2, 1) });
                    break;

                case "Animate" when transform == "Rotation" && order == "Mount":
                    H.Skip(name + "_Rendered", "#1615: .Rotation() mounted under .Animate() animates RotationAngle in degrees");
                    break;

                case "Transition":
                case "Keyframes":
                case "Animate":
                    await CheckPose(name + "_Rendered", stage, declared);
                    break;
            }

            if (transform == "CenterPoint" && target.Parent is not null)
            {
                var visual = ElementCompositionPreview.GetElementVisual(target);
                H.Check(name + "_VisualCenterPoint", visual.CenterPoint == Center, $"center={visual.CenterPoint}");
            }

            if (feature == "Transition" && order == "Mount")
            {
                // The exit must scale from the declared size, not from the enter's start value.
                await CheckExit(name, reconciler, stage, target, full, declared);
            }
            else if (target.Parent is not null)
            {
                // Removing the transform must reset the value that moved to the visual.
                var residual = feature == "Keyframes" && transform == "CenterPoint"
                    ? Pose.Identity with { Scale = new Vector3(2, 2, 1) }
                    : Pose.Identity;
                reconciler.Update(Stage(full), Stage(WithFeature(Target(), transform, longTransition)), stage, static () => { });
                await CheckPose(name + "_Unset", stage, residual);
            }
        }
        catch (Exception ex)
        {
            H.Check(name + "_NoThrow", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            H.SetContent(null);
        }
    }

    private async Task CheckExit(string name, Reconciler reconciler, StackPanel stage, Border target, Element mounted, Pose declared)
    {
        reconciler.Update(Stage(mounted), Stage(null), stage, static () => { });
        var controller = ElementCompositionPreview.GetElementVisual(target).TryGetAnimationController("Scale");
        H.Check(name + "_ExitStarted", controller is not null);
        if (controller is null) return;
        controller.Pause();
        controller.Progress = 0;
        await CheckPose(name + "_ExitStart", stage, declared);
        controller.Progress = 1;
        await CheckPose(name + "_ExitEnd", stage, declared.Scaled(0.5f));
        // Stopping the animation completes the exit batch, which removes the child.
        ElementCompositionPreview.GetElementVisual(target).StopAnimation("Scale");
        for (int i = 0; i < 20 && stage.Children.Count > 0; i++) await Settle(50);
        H.Check(name + "_ExitRemoved", stage.Children.Count == 0, $"children={stage.Children.Count}");
    }

    private async Task CheckPose(string name, FrameworkElement stage, Pose pose)
    {
        await Settle();
        var actual = await RedBounds(stage);
        var expected = Bounds(pose);
        bool ok = actual.Count > 0
            && Math.Abs(actual.X0 - expected.X0) <= Tolerance && Math.Abs(actual.Y0 - expected.Y0) <= Tolerance
            && Math.Abs(actual.X1 - expected.X1) <= Tolerance && Math.Abs(actual.Y1 - expected.Y1) <= Tolerance;
        H.Check(name, ok,
            $"actual=x[{actual.X0:F0},{actual.X1:F0}] y[{actual.Y0:F0},{actual.Y1:F0}] n={actual.Count} "
            + $"expected=x[{expected.X0:F0},{expected.X1:F0}] y[{expected.Y0:F0},{expected.Y1:F0}]");
    }

    // Visual transform: translate(-CenterPoint), scale, rotate, translate(CenterPoint + Translation).
    private static (double X0, double Y0, double X1, double Y1) Bounds(Pose pose)
    {
        double radians = pose.Degrees * Math.PI / 180, cos = Math.Cos(radians), sin = Math.Sin(radians);
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var (cx, cy) in new (double, double)[] { (0, 0), (Width, 0), (0, Height), (Width, Height) })
        {
            double vx = (cx - pose.CenterPoint.X) * pose.Scale.X, vy = (cy - pose.CenterPoint.Y) * pose.Scale.Y;
            double x = vx * cos - vy * sin + pose.CenterPoint.X + pose.Translation.X + Left;
            double y = vx * sin + vy * cos + pose.CenterPoint.Y + pose.Translation.Y + Top;
            x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
        }
        return (x0, y0, x1, y1);
    }

    private static async Task<(double X0, double Y0, double X1, double Y1, int Count)> RedBounds(FrameworkElement stage)
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(stage);
        var pixels = (await rtb.GetPixelsAsync()).ToArray();
        int width = rtb.PixelWidth, height = rtb.PixelHeight;
        double sx = width / stage.ActualWidth, sy = height / stage.ActualHeight;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1, count = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                if (pixels[i + 2] > 200 && pixels[i + 1] < 90 && pixels[i] < 90)
                {
                    count++;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                }
            }
        }
        return count == 0 ? (0, 0, 0, 0, 0) : (minX / sx, minY / sy, (maxX + 1) / sx, (maxY + 1) / sy, count);
    }

    private static async Task Settle(int ms = 150)
    {
        int frames = 0;
        var done = new TaskCompletionSource();
        EventHandler<object> onRendering = (_, _) => { if (++frames >= 4) done.TrySetResult(); };
        CompositionTarget.Rendering += onRendering;
        try { await Task.WhenAny(done.Task, Task.Delay(2000)); }
        finally { CompositionTarget.Rendering -= onRendering; }
        await Task.Delay(ms);
    }
}
