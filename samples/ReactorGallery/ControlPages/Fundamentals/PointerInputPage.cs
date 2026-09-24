using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.Fundamentals;

class PointerInputPage : Component
{
    public override Element Render()
    {
        var (isHovered, setIsHovered) = UseState(false);

        var (tapLog, setTapLog) = UseState("Tap, double-tap, or right-tap the card.");

        var (offsetX, moveX) = UseReducer(0.0);
        var (offsetY, moveY) = UseReducer(0.0);

        var (scale, scaleBy) = UseReducer(1.0f);
        var (angle, rotateBy) = UseReducer(0f);

        var (resting, setResting) = UseState("Drag the card, then let go.");
        var smoothRef = this.UseElementRef<FrameworkElement>();

        return ScrollView(VStack(16,
            PageHeader("Pointer and gestures",
                "Attach .On* modifiers for pointer, tap, and continuous gestures. Reactor auto-enables the underlying WinUI flag — ManipulationMode, IsDoubleTapEnabled — when a handler is attached."),

            SampleCard("Pointer enter and exit",
                VStack(8,
                    Border(TextBlock(isHovered ? "Pointer is over me." : "Move the pointer here.")
                            .Foreground(Theme.SecondaryText))
                        .Background(isHovered ? Theme.SubtleFill : Theme.LayerFill)
                        .WithBorder(Theme.SurfaceStroke)
                        .Padding(24)
                        .OnPointerEntered((_, _) => setIsHovered(true))
                        .OnPointerExited((_, _) => setIsHovered(false)),
                    Caption("A decorative overlay that should let clicks fall through takes .IsHitTestVisible(false), which opts the element and its whole subtree out of hit-testing.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (isHovered, setIsHovered) = UseState(false);

Border(TextBlock(isHovered ? ""Pointer is over me."" : ""Move the pointer here.""))
    .Background(isHovered ? Theme.SubtleFill : Theme.LayerFill)
    .OnPointerEntered((_, _) => setIsHovered(true))
    .OnPointerExited((_, _) => setIsHovered(false))
"),

            SampleCard("Tap, double-tap, right-tap",
                VStack(8,
                    Border(TextBlock(tapLog).Foreground(Theme.SecondaryText))
                        .Background(Theme.LayerFill)
                        .WithBorder(Theme.SurfaceStroke)
                        .Padding(24)
                        .IsTabStop(true)
                        .AutomationName("Tap surface")
                        .OnTapped((_, _) => setTapLog("Tapped."))
                        .OnDoubleTapped((_, _) => setTapLog("Double-tapped."))
                        .OnRightTapped((_, _) => setTapLog("Right-tapped."))
                        .OnLongPress(onTriggered: () => setTapLog("Long-pressed."), enableMouseEmulation: true)
                        .OnKeyDown((_, e) =>
                        {
                            if (e.Key is not (VirtualKey.Enter or VirtualKey.Space)) return;
                            setTapLog("Activated from the keyboard.");
                            e.Handled = true;
                        }),
                    Caption("A Border is not a tab stop by default, so a tappable one needs .IsTabStop(true) and a name. Being reachable by Tab is not enough — without an Enter/Space handler the keyboard can focus it but never activate it. Reach for a Button whenever the gesture is really a command.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (tapLog, setTapLog) = UseState(""Tap, double-tap, or right-tap the card."");

Border(TextBlock(tapLog))
    .IsTabStop(true)
    .AutomationName(""Tap surface"")
    .OnTapped((_, _) => setTapLog(""Tapped.""))
    .OnDoubleTapped((_, _) => setTapLog(""Double-tapped.""))
    .OnRightTapped((_, _) => setTapLog(""Right-tapped.""))
    .OnLongPress(onTriggered: () => setTapLog(""Long-pressed.""), enableMouseEmulation: true)
    // Reachable by Tab is not the same as operable — pair the tap with Enter/Space.
    .OnKeyDown((_, e) =>
    {
        if (e.Key is not (VirtualKey.Enter or VirtualKey.Space)) return;
        setTapLog(""Activated from the keyboard."");
        e.Handled = true;
    })
"),

            SampleCard("Pan — drag with the phase lifecycle",
                VStack(8,
                    Border(TextBlock("Drag me").Foreground(Theme.SecondaryText))
                        .Background(Theme.LayerFill)
                        .WithBorder(Theme.SurfaceStroke)
                        .Padding(24)
                        .Width(160)
                        .OnPan(
                            onChanged: e =>
                            {
                                moveX(x => x + e.Delta.X);
                                moveY(y => y + e.Delta.Y);
                            },
                            minimumDistance: 4)
                        .Translation((float)offsetX, (float)offsetY, 0),
                    Button("Recentre", () =>
                    {
                        moveX(_ => 0);
                        moveY(_ => 0);
                    }),
                    Caption("Gestures run Began then Changed (repeatedly) then Ended or Cancelled. PanGesture.Delta is a flat Point — Reactor does not surface WinUI's nested ManipulationDelta. Note the functional updaters: onChanged fires far faster than the component re-renders, so a setter reading the render's captured offset would drop every delta that arrived before the next commit.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (offsetX, moveX) = UseReducer(0.0);
var (offsetY, moveY) = UseReducer(0.0);

Border(TextBlock(""Drag me""))
    .OnPan(
        onChanged: e =>
        {
            // Functional updaters, NOT setOffsetX(offsetX + ...): several onChanged
            // callbacks can land between two renders, and each would read the same
            // stale captured value and overwrite the one before it.
            moveX(x => x + e.Delta.X);
            moveY(y => y + e.Delta.Y);
        },
        minimumDistance: 4)
    .Translation((float)offsetX, (float)offsetY, 0)
"),

            SampleCard("Pinch and rotate",
                VStack(8,
                    Border(TextBlock("Pinch or rotate me").Foreground(Theme.SecondaryText))
                        .Background(Theme.LayerFill)
                        .WithBorder(Theme.SurfaceStroke)
                        .Padding(24)
                        .Width(180)
                        .OnPinch(onChanged: e => scaleBy(s => s * (float)e.ScaleDelta))
                        .OnRotate(onChanged: e => rotateBy(a => a + (float)e.AngleDelta))
                        .Scale(scale)
                        .Rotation(angle),
                    HStack(8,
                        Button("Reset scale", () => scaleBy(_ => 1.0f)),
                        Button("Reset angle", () => rotateBy(_ => 0f))),
                    TextBlock($"Scale: {scale:F2} — angle: {angle:F0}°").Foreground(Theme.SecondaryText),
                    Caption("Both gestures carry an absolute value and a per-event delta as flat doubles: Scale/ScaleDelta and Angle/AngleDelta. Neither has a nested e.Delta object. Deltas are folded in with functional updaters for the same reason as the pan card.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (scale, scaleBy) = UseReducer(1.0f);
var (angle, rotateBy) = UseReducer(0f);

Border(TextBlock(""Pinch or rotate me""))
    .OnPinch(onChanged: e => scaleBy(s => s * (float)e.ScaleDelta))
    .OnRotate(onChanged: e => rotateBy(a => a + (float)e.AngleDelta))
    .Scale(scale)
    .Rotation(angle)
"),

            SampleCard("60Hz drag — write through an element ref",
                VStack(8,
                    Border(TextBlock("Drag me smoothly").Foreground(Theme.SecondaryText))
                        .Background(Theme.LayerFill)
                        .WithBorder(Theme.SurfaceStroke)
                        .Padding(24)
                        .Width(180)
                        .Ref(smoothRef)
                        .OnPan(
                            onChanged: e =>
                            {
                                // Direct property write — no re-render per frame.
                                if (smoothRef.Current is { } el)
                                {
                                    var t = el.Translation;
                                    el.Translation = new global::System.Numerics.Vector3(
                                        t.X + (float)e.Delta.X, t.Y + (float)e.Delta.Y, t.Z);
                                }
                            },
                            onEnded: _ =>
                            {
                                if (smoothRef.Current is { } el)
                                    setResting($"Rested at {el.Translation.X:F0}, {el.Translation.Y:F0}");
                            }),
                    TextBlock(resting).Foreground(Theme.SecondaryText),
                    Caption("The cell must come from UseElementRef<T> — that is what the reconciler populates. A UseRef box is a different type and .Ref(...) will not take it.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var smoothRef = this.UseElementRef<FrameworkElement>();

Border(TextBlock(""Drag me smoothly""))
    .Ref(smoothRef)
    .OnPan(
        onChanged: e =>
        {
            // Direct property write — no re-render per frame.
            if (smoothRef.Current is { } el)
            {
                var t = el.Translation;
                el.Translation = new System.Numerics.Vector3(
                    t.X + (float)e.Delta.X, t.Y + (float)e.Delta.Y, t.Z);
            }
        },
        onEnded: _ =>
        {
            // One state write, at the end of the gesture.
            if (smoothRef.Current is { } el)
                setResting($""Rested at {el.Translation.X:F0}, {el.Translation.Y:F0}"");
        })
")
        ).Margin(36, 24, 36, 36));
    }
}
