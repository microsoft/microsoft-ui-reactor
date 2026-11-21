using System.Globalization;
using System.Numerics;

// Pure managed: linked into Reactor.Tests, so nothing here may touch WinUI.
namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.TransformsMatrix;

/// <summary>Declared transform values on one element.</summary>
internal readonly record struct Pose(Vector3 Scale, float Degrees, Vector3 Translation, Vector3 CenterPoint)
{
    public static Pose Identity => new(Vector3.One, 0, Vector3.Zero, Vector3.Zero);

    public Pose Scaled(float factor) => this with { Scale = new Vector3(Scale.X * factor, Scale.Y * factor, Scale.Z) };
}

/// <summary>An axis-aligned bounding box in stage DIPs.</summary>
internal readonly record struct Box(double X0, double Y0, double X1, double Y1)
{
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{X0:F0},{Y0:F0},{X1:F0},{Y1:F0}");

    public double this[int i] => i switch { 0 => X0, 1 => Y0, 2 => X1, _ => Y1 };

    public static bool TryParse(string? text, out Box box)
    {
        box = default;
        var parts = text?.Split(',');
        if (parts is not { Length: 4 }) return false;
        var v = new double[4];
        for (int i = 0; i < 4; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
        box = new Box(v[0], v[1], v[2], v[3]);
        return true;
    }

    public bool Near(Box other, double tolerance) =>
        Math.Abs(X0 - other.X0) <= tolerance && Math.Abs(Y0 - other.Y0) <= tolerance
        && Math.Abs(X1 - other.X1) <= tolerance && Math.Abs(Y1 - other.Y1) <= tolerance;
}

/// <summary>
/// The analytic composition model of spec 066 §12.1 B: what "compounds properly" means, written
/// down so each observation has something to be checked against. The matrix records whether each
/// step agrees with it, and a disagreement on <c>main</c> is recorded as a known difference with an
/// issue link (<c>transforms-ledger.known.json</c>) rather than bent into the model.
/// </summary>
/// <remarks>
/// The stage is a 400×400 white panel. The target is a 100×40 red panel laid out at (120,150) from
/// the stage's flow start, so RTL moves it and the mirrored oracle is not symmetric by accident.
/// </remarks>
internal static class CompositionModel
{
    public const double StageSize = 400, Width = 100, Height = 40, Left = 120, Top = 150;
    public const double LayoutShift = 20;
    public const float OpacityA = 1f, OpacityB = 0.8f;

    /// <summary>The facade scale a <c>.Set</c> writes in the <see cref="UserCodeAxis.SetFacade"/> cases.</summary>
    public const float SetterScale = 1.1f;

    /// <summary>The facade translation a <see cref="UserCodeAxis.TranslationDrag"/> case writes after mount.</summary>
    public const float DragX = 5f;

    public const string Animating = "ANIMATING", Jumped = "JUMPED", NotApplied = "NOT-APPLIED", Held = "HELD";

    /// <summary>
    /// Verdicts that say the oracle couldn't see the channel, not how it moved. They are recorded,
    /// but never compared with the model. <c>unobservable</c>: a XAML transition, which
    /// RenderTargetBitmap can't see in flight (§12.1 D). <c>invisible</c>: the channel doesn't change
    /// the pose (a lone <c>CenterPoint</c>). <c>running</c>: an animation exists on an invisible channel.
    /// </summary>
    public static readonly string[] Unmodelled = ["unobservable", "invisible", "running"];

    public static Pose Declared(TransformAxis transform, bool second) => transform switch
    {
        TransformAxis.Scale => Pose.Identity with { Scale = Uniform(second ? 2f : 1.5f) },
        TransformAxis.Rotation => Pose.Identity with { Degrees = second ? 90 : 30 },
        TransformAxis.Translation => Pose.Identity with { Translation = second ? new Vector3(60, 30, 0) : new Vector3(20, 10, 0) },
        TransformAxis.CenterPoint => Pose.Identity with { CenterPoint = second ? new Vector3(25, 10, 0) : new Vector3(50, 20, 0) },
        TransformAxis.ScaleRotationCenter => new Pose(Uniform(second ? 2f : 1.5f), second ? 90 : 30, Vector3.Zero,
            second ? new Vector3(25, 10, 0) : new Vector3(50, 20, 0)),
        _ => Pose.Identity,
    };

    public static bool DeclaresScale(TransformAxis t) => t is TransformAxis.Identity or TransformAxis.Scale or TransformAxis.ScaleRotationCenter;

    /// <summary>The pose at rest after <paramref name="step"/> (§12.1 B, per-channel precedence).</summary>
    public static Pose Expected(MatrixCase c, MatrixStep step)
    {
        var transform = step switch
        {
            MatrixStep.RemoveModifier or MatrixStep.Unmount or MatrixStep.RentPlain => TransformAxis.None,
            _ => c.Transform,
        };
        var pose = Declared(transform, step is MatrixStep.Change or MatrixStep.RemoveFeature or MatrixStep.AddFeature);
        if (step is MatrixStep.RentPlain or MatrixStep.RentTransformed) return pose;

        // A setter's facade value survives on any channel the modifiers don't declare.
        if (c.UserCode == UserCodeAxis.SetFacade && !DeclaresScale(transform))
            pose = pose with { Scale = Uniform(SetterScale) };
        // §4.3: a facade Translation from user code adds to the visual's, unless .Translation() owns it.
        if (c.UserCode == UserCodeAxis.TranslationDrag && transform != TransformAxis.Translation)
            pose = pose with { Translation = pose.Translation + new Vector3(DragX, 0, 0) };
        return pose;
    }

    /// <summary>
    /// The pose while a temporary owner holds the channel: hover (scale 1.25, absolute), and a
    /// linear scale enter or exit between 0.5× and 1× of the declared value (#1627), seeked to its
    /// midpoint, so 0.75×.
    /// </summary>
    /// <remarks>Hover and the enter are observed before a drag case's facade write; the exit plays on the last declaration.</remarks>
    public static Pose? Active(MatrixCase c, MatrixStep step)
    {
        var beforeDrag = c with { UserCode = c.UserCode == UserCodeAxis.TranslationDrag ? UserCodeAxis.None : c.UserCode };
        return (c.Feature, step) switch
        {
            (FeatureAxis.InteractionStates, MatrixStep.Mount) => Expected(beforeDrag, step) with { Scale = Uniform(1.25f) },
            (FeatureAxis.TransitionEnter, MatrixStep.Mount) => Expected(beforeDrag, step).Scaled(0.75f),
            (FeatureAxis.TransitionExit, MatrixStep.Unmount) => Expected(c, MatrixStep.RemoveModifier).Scaled(0.75f),
            _ => null,
        };
    }

    /// <summary>Layout moves only for <c>.LayoutAnimation()</c> cases, from the change step on.</summary>
    public static double Shift(MatrixCase c, MatrixStep step) =>
        c.Feature == FeatureAxis.LayoutAnimation && step is MatrixStep.Change or MatrixStep.RemoveFeature or MatrixStep.AddFeature or MatrixStep.RemoveModifier or MatrixStep.Unmount
            ? LayoutShift : 0;

    /// <summary>
    /// Expected green (BGRA byte) at the target's centre: red at opacity <paramref name="opacity"/>
    /// over white, where the change step's alternate background has green 0x20.
    /// </summary>
    public static double Green(double opacity, bool altBackground) => 255 * (1 - opacity) + (altBackground ? 0x20 : 0) * opacity;

    public static float Opacity(MatrixStep step) =>
        step is MatrixStep.Mount or MatrixStep.RentPlain or MatrixStep.RentTransformed ? OpacityA : OpacityB;

    /// <summary>
    /// Bounding box of the target in stage coordinates. The transform is
    /// <c>Translation · T(pivot) · R · S · T(−pivot)</c> with <c>pivot = Size × origin + CenterPoint</c>,
    /// and the transform space is flow-relative (Q9), so RTL mirrors the result about the stage.
    /// </summary>
    public static Box Bounds(Pose pose, bool rtl, double shift = 0, Vector2 origin = default)
    {
        double radians = pose.Degrees * Math.PI / 180, cos = Math.Cos(radians), sin = Math.Sin(radians);
        double px = Width * origin.X + pose.CenterPoint.X, py = Height * origin.Y + pose.CenterPoint.Y;
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var (cx, cy) in new (double, double)[] { (0, 0), (Width, 0), (0, Height), (Width, Height) })
        {
            var (x, y) = Transform(pose, cx, cy, px, py, cos, sin);
            x += Left + shift;
            y += Top;
            x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
        }

        return rtl ? new Box(StageSize - x1, y0, StageSize - x0, y1) : new Box(x0, y0, x1, y1);
    }

    /// <summary>Where an element-local point lands on the stage, for hit-test probes.</summary>
    public static (double X, double Y) Point(Pose pose, bool rtl, double shift, double localX, double localY)
    {
        double radians = pose.Degrees * Math.PI / 180;
        var (x, y) = Transform(pose, localX, localY, pose.CenterPoint.X, pose.CenterPoint.Y, Math.Cos(radians), Math.Sin(radians));
        x += Left + shift;
        y += Top;
        return (rtl ? StageSize - x : x, y);
    }

    /// <summary>The compositor property each transition animates.</summary>
    public static string? TransitionChannel(TransitionAxis transition) => transition switch
    {
        TransitionAxis.Opacity => "Opacity",
        TransitionAxis.Scale => "Scale",
        TransitionAxis.Rotation => "RotationAngle",
        TransitionAxis.Translation => "Translation",
        TransitionAxis.Background => "Background",
        _ => null,
    };

    /// <summary>Channels whose declared value the change step moves, in a fixed order.</summary>
    public static IReadOnlyList<string> ChangedChannels(MatrixCase c)
    {
        var a = Declared(c.Transform, false);
        var b = Declared(c.Transform, true);
        var channels = new List<string>();
        if (a.Scale != b.Scale) channels.Add("Scale");
        if (MathF.Abs(a.Degrees - b.Degrees) > 1e-4f) channels.Add("RotationAngle");
        if (a.Translation != b.Translation) channels.Add("Translation");
        if (a.CenterPoint != b.CenterPoint) channels.Add("CenterPoint");
        channels.Add("Opacity");
        if (c.Feature == FeatureAxis.LayoutAnimation) channels.Add("Offset");
        if (c.Transition == TransitionAxis.Background) channels.Add("Background");
        return channels;
    }

    /// <summary>
    /// How a declared change on <paramref name="channel"/> should move (§12.1 B): a playing keyframe
    /// entry holds the channel; otherwise <c>WithAnimation</c>, then <c>.Animate()</c>, then the
    /// implicit transition (which reduced motion turns into a jump, §5.5), else it jumps.
    /// </summary>
    public static string ExpectedMotion(MatrixCase c, string channel)
    {
        if (c.Feature == FeatureAxis.Keyframes && c.Variant == VariantAxis.B && channel == "Opacity") return Held;
        if (channel == "Offset") return c.Feature == FeatureAxis.LayoutAnimation ? Animating : Jumped;
        if (c.Feature is FeatureAxis.WithAnimation or FeatureAxis.Animate && channel != "Background") return Animating;
        if (TransitionChannel(c.Transition) == channel) return c.Motion == MotionAxis.Disabled ? Jumped : Animating;
        return Jumped;
    }

    private static (double X, double Y) Transform(Pose pose, double cx, double cy, double px, double py, double cos, double sin)
    {
        double vx = (cx - px) * pose.Scale.X, vy = (cy - py) * pose.Scale.Y;
        return (vx * cos - vy * sin + px + pose.Translation.X, vx * sin + vy * cos + py + pose.Translation.Y);
    }

    private static Vector3 Uniform(float s) => new(s, s, 1);
}
