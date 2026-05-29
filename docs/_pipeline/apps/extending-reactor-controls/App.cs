using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

System.AppContext.SetSwitch("Reactor.UseV1Protocol", true);
ReactorApp.Run<ExtendingApp>(
    "Extending Reactor", width: 540, height: 360, devtools: true,
    configure: host => StarMeterInterop.Register(host.Reconciler));

// ════════════════════════════════════════════════════════════════════════
//  Step 1 — Define the Element record
// ════════════════════════════════════════════════════════════════════════

// <snippet:star-meter-element>
// An Element subclass with one controlled prop (Value), three one-way
// props (MaxRating, Caption, IsClearEnabled), and one callback (OnValueChanged).
// Records give the reconciler value-equality for free — two StarMeterElement
// instances with identical fields compare equal and Update becomes a no-op.
public sealed record StarMeterElement : Element
{
    public double Value { get; init; }
    public int MaxRating { get; init; } = 5;
    public string? Caption { get; init; }
    public bool IsClearEnabled { get; init; } = true;
    public System.Action<double>? OnValueChanged { get; init; }
}
// </snippet:star-meter-element>

// ════════════════════════════════════════════════════════════════════════
//  Step 2 — Wire the descriptor
// ════════════════════════════════════════════════════════════════════════

// <snippet:star-meter-descriptor>
public static class StarMeterDescriptor
{
    public static readonly ControlDescriptor<StarMeterElement, WinUI.RatingControl> Descriptor =
        new ControlDescriptor<StarMeterElement, WinUI.RatingControl>
        {
            // Leaf control — no children. (See ChildrenStrategy survey for
            // the other shapes: SingleContent, Panel, NamedSlots, ItemsHost…)
            Children = new None<StarMeterElement, WinUI.RatingControl>(),
        }
        // OneWay props: written on Mount, diff-and-written on Update.
        .OneWay(
            get: static e => e.MaxRating,
            set: static (c, v) => c.MaxRating = v)
        .OneWay(
            get: static e => e.IsClearEnabled,
            set: static (c, v) => c.IsClearEnabled = v)
        // OneWayConditional skips the write when the predicate is false —
        // leaves Caption at the control's default for elements that didn't
        // supply one, rather than forcing it to null and losing a style.
        .OneWayConditional(
            get:         static e => e.Caption,
            set:         static (c, v) => c.Caption = v!,
            shouldWrite: static e => e.Caption is not null)
        // Controlled is the two-way binding shape: the framework writes the
        // element's value at Mount (and on diff), suppresses the echo when
        // the framework is the writer, and forwards user input back through
        // OnValueChanged. Subscription is gated on the callback being non-
        // null — if the caller didn't pass OnValueChanged, no trampoline
        // is wired and the per-fire dispatch cost stays at zero.
        .Controlled<double, object>(
            get:         static e => e.Value,
            set:         static (c, v) => c.Value = v,
            subscribe:   static (fe, h) => ((WinUI.RatingControl)fe).ValueChanged += (s, e) => h(s, e!),
            unsubscribe: static (fe, h) => { /* trampoline anchored for control lifetime */ },
            callback:    static e => e.OnValueChanged,
            readBack:    static c => c.Value);
}
// </snippet:star-meter-descriptor>

// ════════════════════════════════════════════════════════════════════════
//  Step 3 — Register
// ════════════════════════════════════════════════════════════════════════

// <snippet:star-meter-registration>
public static class StarMeterInterop
{
    // One call per Reactor host. RegisterHandler accepts any IElementHandler,
    // and DescriptorHandler<TElement,TControl> is the canonical interpreter
    // for a ControlDescriptor. Duplicate registration for the same element
    // type throws — register exactly once on each host you mount.
    public static void Register(Reconciler reconciler) =>
        reconciler.RegisterHandler<StarMeterElement, WinUI.RatingControl>(
            new DescriptorHandler<StarMeterElement, WinUI.RatingControl>(
                StarMeterDescriptor.Descriptor));
}
// </snippet:star-meter-registration>

// ════════════════════════════════════════════════════════════════════════
//  Step 4 — Use the element
// ════════════════════════════════════════════════════════════════════════

// <snippet:star-meter-usage>
class ExtendingApp : Component
{
    public override Element Render()
    {
        var (rating, setRating) = UseState(3.5);

        return VStack(16,
            TextBlock("StarMeter — custom element wrapping WinUI RatingControl")
                .FontSize(14).SemiBold(),

            new StarMeterElement
            {
                Value = rating,
                MaxRating = 5,
                Caption = "Rate this page",
                OnValueChanged = setRating,
            },

            TextBlock($"current rating: {rating:0.0}"),

            HStack(8,
                Button("Reset", () => setRating(0)),
                Button("5 stars", () => setRating(5)))
        ).Padding(20);
    }
}
// </snippet:star-meter-usage>
