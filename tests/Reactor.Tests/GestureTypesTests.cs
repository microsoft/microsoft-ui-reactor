using global::Windows.Foundation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Input;
using Microsoft.UI.Xaml.Input;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for the Phase 3 gesture value types and ManipulationMode computation
/// (spec 027 §Tier 3). Record-struct equality, option plumbing through the
/// fluent extensions, and ManipulationMode flag union.
/// </summary>
public class GestureTypesTests
{
    // ── Record equality ─────────────────────────────────────────────

    [Fact]
    public void PanGesture_EqualityIsStructural()
    {
        var a = new PanGesture(
            Translation: new Point(10, 20),
            Delta: new Point(1, 2),
            Velocity: new Point(0, 0),
            Position: new Point(50, 60),
            StartPosition: new Point(40, 40),
            Phase: GesturePhase.Changed,
            IsInertial: false);

        var b = a with { };
        var c = a with { Phase = GesturePhase.Ended };

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void PinchGesture_EqualityIsStructural()
    {
        var a = new PinchGesture(Scale: 1.5, ScaleDelta: 1.01, Center: new Point(0, 0),
            Phase: GesturePhase.Changed, IsInertial: false);
        var b = a with { };
        Assert.Equal(a, b);
    }

    [Fact]
    public void RotateGesture_EqualityIsStructural()
    {
        var a = new RotateGesture(Angle: 30.0, AngleDelta: 1.0, Center: new Point(0, 0),
            Phase: GesturePhase.Changed, IsInertial: false);
        var b = a with { };
        Assert.Equal(a, b);
    }

    // ── Fluent extension plumbing ───────────────────────────────────

    [Fact]
    public void OnPan_StoresConfigOnModifiers()
    {
        var el = TextBlock("x").OnPan(
            onChanged: _ => { },
            minimumDistance: 8.0,
            axis: PanAxis.Horizontal,
            withInertia: true);

        var config = el.Modifiers!.Pan;
        Assert.NotNull(config);
        Assert.Equal(8.0, config!.MinimumDistance);
        Assert.Equal(PanAxis.Horizontal, config.Axis);
        Assert.True(config.WithInertia);
    }

    [Fact]
    public void OnPinch_StoresConfigOnModifiers()
    {
        var el = TextBlock("x").OnPinch(_ => { }, withInertia: true);
        Assert.NotNull(el.Modifiers!.Pinch);
        Assert.True(el.Modifiers!.Pinch!.WithInertia);
    }

    [Fact]
    public void OnRotate_StoresConfigOnModifiers()
    {
        var el = TextBlock("x").OnRotate(_ => { });
        Assert.NotNull(el.Modifiers!.Rotate);
        Assert.False(el.Modifiers!.Rotate!.WithInertia);
    }

    // ── ManipulationMode union ──────────────────────────────────────

    [Fact]
    public void ComputeManipulationMode_PanBoth_SetsBothTranslateFlags()
    {
        var m = new ElementModifiers
        {
            Pan = new PanGestureConfig(_ => { }) { Axis = PanAxis.Both },
        };
        var mode = Reconciler.ComputeManipulationMode(m);
        Assert.True(mode.HasFlag(ManipulationModes.TranslateX));
        Assert.True(mode.HasFlag(ManipulationModes.TranslateY));
        Assert.False(mode.HasFlag(ManipulationModes.TranslateInertia));
    }

    [Fact]
    public void ComputeManipulationMode_PanHorizontal_OmitsVertical()
    {
        var m = new ElementModifiers
        {
            Pan = new PanGestureConfig(_ => { }) { Axis = PanAxis.Horizontal },
        };
        var mode = Reconciler.ComputeManipulationMode(m);
        Assert.True(mode.HasFlag(ManipulationModes.TranslateX));
        Assert.False(mode.HasFlag(ManipulationModes.TranslateY));
    }

    [Fact]
    public void ComputeManipulationMode_PanInertia_AddsInertiaFlag()
    {
        var m = new ElementModifiers
        {
            Pan = new PanGestureConfig(_ => { }) { Axis = PanAxis.Vertical, WithInertia = true },
        };
        var mode = Reconciler.ComputeManipulationMode(m);
        Assert.True(mode.HasFlag(ManipulationModes.TranslateY));
        Assert.True(mode.HasFlag(ManipulationModes.TranslateInertia));
    }

    [Fact]
    public void ComputeManipulationMode_PanHorizontalPlusPinch_UnionsFlags()
    {
        var m = new ElementModifiers
        {
            Pan = new PanGestureConfig(_ => { }) { Axis = PanAxis.Horizontal },
            Pinch = new PinchGestureConfig(_ => { }),
        };
        var mode = Reconciler.ComputeManipulationMode(m);
        Assert.True(mode.HasFlag(ManipulationModes.TranslateX));
        Assert.False(mode.HasFlag(ManipulationModes.TranslateY));
        Assert.True(mode.HasFlag(ManipulationModes.Scale));
    }

    [Fact]
    public void ComputeManipulationMode_RotateWithInertia_AddsRotateInertia()
    {
        var m = new ElementModifiers
        {
            Rotate = new RotateGestureConfig(_ => { }) { WithInertia = true },
        };
        var mode = Reconciler.ComputeManipulationMode(m);
        Assert.True(mode.HasFlag(ManipulationModes.Rotate));
        Assert.True(mode.HasFlag(ManipulationModes.RotateInertia));
    }

    [Fact]
    public void ComputeManipulationMode_NoGestures_IsNone()
    {
        var mode = Reconciler.ComputeManipulationMode(new ElementModifiers());
        Assert.Equal(ManipulationModes.None, mode);
    }

    // ── LongPress (spec 027 Tier 3 Part 2) ──────────────────────────

    [Fact]
    public void LongPressGesture_EqualityIsStructural()
    {
        var a = new LongPressGesture(
            Position: new Point(10, 20),
            Duration: TimeSpan.FromMilliseconds(500),
            Phase: GesturePhase.Began);
        var b = a with { };
        var c = a with { Phase = GesturePhase.Ended };

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void OnLongPress_StoresConfigWithDefaults()
    {
        var el = TextBlock("x").OnLongPress(_ => { });
        var config = el.Modifiers!.LongPress;

        Assert.NotNull(config);
        Assert.Equal(TimeSpan.FromMilliseconds(500), config!.MinimumDuration);
        Assert.Equal(10.0, config.CancelDistance);
        Assert.False(config.EnableMouseEmulation);
    }

    [Fact]
    public void OnLongPress_OverridesDuration()
    {
        var el = TextBlock("x").OnLongPress(
            _ => { },
            minimumDuration: TimeSpan.FromSeconds(1),
            cancelDistance: 5.0,
            enableMouseEmulation: true);
        var config = el.Modifiers!.LongPress!;

        Assert.Equal(TimeSpan.FromSeconds(1), config.MinimumDuration);
        Assert.Equal(5.0, config.CancelDistance);
        Assert.True(config.EnableMouseEmulation);
    }

    [Fact]
    public void OnLongPress_ZeroArgOverload_RoutesToOnTriggered()
    {
        int count = 0;
        var el = TextBlock("x").OnLongPress(() => count++);

        // Invoke the wrapped action directly to verify the adapter closes over the zero-arg.
        el.Modifiers!.LongPress!.OnTriggered(new LongPressGesture(
            Position: new Point(0, 0), Duration: TimeSpan.Zero, Phase: GesturePhase.Began));
        Assert.Equal(1, count);
    }

    [Fact]
    public void OnDoubleTap_ZeroArg_WiresToOnDoubleTapped()
    {
        int count = 0;
        var el = TextBlock("x").OnDoubleTap(() => count++);
        Assert.NotNull(el.Modifiers!.OnDoubleTapped);
    }
}
