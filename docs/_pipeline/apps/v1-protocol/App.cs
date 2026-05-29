using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinUI = Microsoft.UI.Xaml.Controls;
using Windows.UI;

System.AppContext.SetSwitch("Reactor.UseV1Protocol", true);
ReactorApp.Run<V1ProtocolApp>(
    "V1 Protocol Demo", width: 520, height: 360, devtools: true,
    configure: host => LedIndicatorRegistration.Register(host.Reconciler));

// <snippet:element-record>
// An Element record describes what you want on screen — no WinUI types, no
// mutable state. The reconciler diffs Element values across renders;
// records get value-equality for free, so unchanged subtrees skip Update.
public sealed record LedIndicatorElement : Element
{
    public required Color Color { get; init; }
    public bool IsOn { get; init; } = true;
    public double Size { get; init; } = 16;
}
// </snippet:element-record>

// <snippet:descriptor>
// A descriptor declares property bindings against the WinUI control the
// element targets. The framework's DescriptorHandler interprets the entries
// during Mount and Update — there is no per-element interpreter overhead
// beyond a dictionary lookup and the entry-loop iteration.
public static class LedIndicatorDescriptor
{
    public static readonly ControlDescriptor<LedIndicatorElement, WinUI.Border> Descriptor =
        new ControlDescriptor<LedIndicatorElement, WinUI.Border>
        {
            // The descriptor's children strategy says "no children" — this is
            // a leaf control. See ChildrenStrategy survey in the prose.
            Children = new None<LedIndicatorElement, WinUI.Border>(),
        }
        // OneWay: write on Mount, diff-and-write on Update. Equality skips
        // the write when the element value didn't change.
        .OneWay(
            get: static e => e.Size,
            set: static (c, v) => { c.Width = v; c.Height = v; c.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(v / 2); })
        // The IsOn → Background mapping coerces both inputs onto one WinUI
        // property. A single OneWay entry observes (Color, IsOn) jointly
        // by reading both off the element in the get/set lambdas.
        .OneWay(
            get: static e => (e.Color, e.IsOn),
            set: static (c, v) =>
                c.Background = new SolidColorBrush(
                    v.IsOn ? v.Color : Color.FromArgb(0x40, v.Color.R, v.Color.G, v.Color.B)));
}
// </snippet:descriptor>

// <snippet:registration>
// Registration is one call per Reactor host. RegisterDescriptor wraps
// RegisterHandler<...>(new DescriptorHandler<...>(descriptor)) — both shapes
// land on the same dispatch table. Duplicate registrations for the same
// element type throw.
public sealed class LedIndicatorRegistration
{
    public static void Register(Reconciler reconciler)
    {
        reconciler.RegisterHandler<LedIndicatorElement, WinUI.Border>(
            new DescriptorHandler<LedIndicatorElement, WinUI.Border>(
                LedIndicatorDescriptor.Descriptor));
    }
}
// </snippet:registration>

// <snippet:usage>
// Once registered, the element is used the same way as any built-in. The
// reconciler dispatches LedIndicatorElement to the registered handler;
// every other element type continues to flow through the built-in path.
class V1ProtocolApp : Component
{
    public override Element Render()
    {
        var (level, setLevel) = UseState(2);

        return VStack(12,
            TextBlock("LED indicator (custom V1 element)").FontSize(14).SemiBold(),
            HStack(8,
                Led(Colors.Red,    isOn: level >= 1),
                Led(Colors.Orange, isOn: level >= 2),
                Led(Colors.Yellow, isOn: level >= 3),
                Led(Colors.Green,  isOn: level >= 4)),
            HStack(8,
                Button("-", () => setLevel(System.Math.Max(0, level - 1))),
                Button("+", () => setLevel(System.Math.Min(4, level + 1))),
                TextBlock($"level = {level}").VAlign(VerticalAlignment.Center))
        ).Padding(20);

        Element Led(Color c, bool isOn) =>
            new LedIndicatorElement { Color = c, IsOn = isOn, Size = 28 };
    }
}
// </snippet:usage>

// Bootstrap: register the descriptor against the host's reconciler once,
// before the first render. ReactorApp.Run exposes the host via a startup
// callback; we use the simpler ConfigureHost extension that matches the
// pattern most app authors will follow.
//
// (In a real app this lives next to other interop registrations, e.g.
// DockingNativeInterop.Register. The doc-app shape keeps it inline.)
public static class Bootstrap
{
    static Bootstrap() { /* registration happens via app callback below */ }
}

public static class Colors
{
    public static readonly Color Red    = Color.FromArgb(0xFF, 0xE8, 0x1A, 0x1A);
    public static readonly Color Orange = Color.FromArgb(0xFF, 0xF5, 0x9E, 0x0B);
    public static readonly Color Yellow = Color.FromArgb(0xFF, 0xFA, 0xCC, 0x15);
    public static readonly Color Green  = Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E);
}
