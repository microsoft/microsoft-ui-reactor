using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;

namespace ParticleStorm;

public sealed record SidebarProps(
    int Count,
    Action<int> SetCount,
    double Gravity,
    Action<double> SetGravity,
    double Drag,
    Action<double> SetDrag,
    bool Paused,
    Action<bool> SetPaused,
    Palette Palette,
    Action<Palette> SetPalette,
    Ref<int> Fps,
    Ref<CursorState> Cursor,
    Ref<ParticleField?> Field);

public sealed class Sidebar : Component<SidebarProps>
{
    static readonly string[] PaletteNames = Enum.GetNames<Palette>();

    public override Element Render()
    {
        var props = Props;
        var (displayFps, setDisplayFps) = UseState(props.Fps.Current);
        var (displayCount, setDisplayCount) = UseState(props.Count);

        UseEffect(() =>
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (_, _) =>
            {
                setDisplayFps(props.Fps.Current);
                setDisplayCount(props.Field.Current?.ActiveCount ?? props.Count);
            };
            timer.Start();
            return () => timer.Stop();
        }, Array.Empty<object>());

        return Border(
            VStack(14,
                TextBlock("Particle Storm").FontSize(24).SemiBold(),
                TextBlock("Win2D CanvasAnimatedControl driven by Reactor state.")
                    .Foreground(Theme.SecondaryText)
                    .TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap),

                StatLine("FPS", displayFps.ToString("N0")),
                StatLine("Active", displayCount.ToString("N0")),

                Slider(props.Count, 1, ParticleField.Capacity, v => props.SetCount((int)Math.Clamp(Math.Round(v), 1, ParticleField.Capacity)))
                    .Header($"Particles: {props.Count:N0}")
                    .StepFrequency(1_000)
                    .TickFrequency(25_000)
                    .TickPlacement(TickPlacement.Outside)
                    .HAlign(HorizontalAlignment.Stretch),

                Slider(props.Gravity, 0, 250, props.SetGravity)
                    .Header($"Gravity: {props.Gravity:F0}")
                    .StepFrequency(1)
                    .HAlign(HorizontalAlignment.Stretch),

                Slider(props.Drag, 0, 0.12, props.SetDrag)
                    .Header($"Drag: {props.Drag:F3}")
                    .StepFrequency(0.005)
                    .HAlign(HorizontalAlignment.Stretch),

                ToggleSwitch(props.Paused, props.SetPaused, onContent: "Paused", offContent: "Running", header: "Simulation"),

                ComboBox(PaletteNames, (int)props.Palette, i => props.SetPalette((Palette)i))
                    .Header("Palette")
                    .HAlign(HorizontalAlignment.Stretch),

                Button("Burst +1,000", () =>
                {
                    var field = props.Field.Current;
                    if (field is null)
                        return;

                    var p = props.Cursor.Current.Position;
                    field.Burst((float)p.X, (float)p.Y, 1_000, Palettes.For(props.Palette));
                    // Burst is queued and applied on the next game-thread tick;
                    // the live count display is driven by the existing 250 ms
                    // DispatcherTimer polling field.ActiveCount, which will pick
                    // up the new count one cycle later. No SetCount here — that
                    // would override the slider value the user just set.
                })
                .HAlign(HorizontalAlignment.Stretch)
            )
            .Padding(18)
            .HAlign(HorizontalAlignment.Stretch)
        )
        .Width(280)
        .Background(Theme.CardBackground)
        .WithBorder(Theme.CardStroke, thickness: 1)
        .HAlign(HorizontalAlignment.Stretch)
        .VAlign(VerticalAlignment.Stretch);
    }

    static Element StatLine(string label, string value) =>
        HStack(8,
            TextBlock(label).Foreground(Theme.SecondaryText),
            TextBlock(value).SemiBold().HAlign(HorizontalAlignment.Right)
        ).HAlign(HorizontalAlignment.Stretch);
}
