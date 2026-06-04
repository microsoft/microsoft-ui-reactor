using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Reactor.Core.V1Protocol.Descriptor;
using static Microsoft.UI.Reactor.Factories;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;

// No reconciler-side registration call — Pattern A (spec 048 §6) wires
// StarMeter's handler into the global ControlRegistry on the first
// StarMeter.Of() call (whose class-init runs the static cctor below).
ReactorApp.Run<ExtendingApp>(
    "Extending Reactor", width: 540, height: 360);

// ════════════════════════════════════════════════════════════════════════
//  Step 1 — Define the Element record
// ════════════════════════════════════════════════════════════════════════

// <snippet:star-meter-element>
// An Element subclass with one controlled prop (Value), three one-way
// props (MaxRating, Caption, IsClearEnabled), and one callback (OnValueChanged).
// Records give the reconciler value-equality for free — two StarMeterElement
// instances with identical fields compare equal and Update becomes a no-op.
//
// The primary constructor is `internal` (spec 048 §6 construction discipline):
// external callers cannot `new StarMeterElement(...)` directly, so the only
// reachable construction path is `StarMeter.Of(...)` below — whose class-init
// installs the global handler registration. Init properties stay `public` so
// `Of(...)` and its callers can configure the optional fields, and `with`
// expressions still work across the assembly boundary.
public sealed record StarMeterElement : Element
{
    public double Value { get; init; }
    public int MaxRating { get; init; } = 5;
    public string? Caption { get; init; }
    public bool IsClearEnabled { get; init; } = true;
    public System.Action<double>? OnValueChanged { get; init; }

    internal StarMeterElement(double value, System.Action<double>? onValueChanged = null)
    {
        Value = value;
        OnValueChanged = onValueChanged;
    }
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

    // The thin `new()`-able handler subclass that the `static` lambda in
    // StarMeter's cctor instantiates. Subclassing DescriptorHandler keeps
    // the descriptor accessible *only* through this handler — the trimmer
    // can drop both if the StarMeter factory is never called.
    internal sealed class Handler : DescriptorHandler<StarMeterElement, WinUI.RatingControl>
    {
        public Handler() : base(StarMeterDescriptor.Descriptor) { }
    }
}
// </snippet:star-meter-descriptor>

// ════════════════════════════════════════════════════════════════════════
//  Step 3 — Wrap the constructor in a factory holder
// ════════════════════════════════════════════════════════════════════════

// <snippet:star-meter-registration>
// Spec 048 §6 Pattern A — the factory holder *is* the registration trigger.
// The static cctor runs the first time any member of `StarMeter` is touched
// (CLR-guaranteed precise-init), which means the global ControlRegistry
// entry is in place before the first Of() call returns its element.
//
// The `static` keyword on the lambda is MANDATORY (not stylistic): it
// guarantees the delegate is cached in a static field (one allocation,
// ever) and captures nothing. A non-static lambda compiles but allocates
// a closure per Register call AND defeats the trimmer's ability to follow
// the holder→handler→control chain. The static lambda is what makes
// Pattern A trim-clean.
public static class StarMeter
{
    static StarMeter() =>
        ControlRegistry.Register<StarMeterElement, WinUI.RatingControl>(
            static () => new StarMeterDescriptor.Handler());

    // Sole construction path for StarMeterElement (spec §6 construction
    // discipline). Calling Of() guarantees the handler is registered before
    // the returned element is mounted — the cctor above runs before any
    // member of this type, including Of, can be invoked.
    public static StarMeterElement Of(
        double value,
        System.Action<double>? onValueChanged = null,
        int maxRating = 5,
        string? caption = null,
        bool isClearEnabled = true) =>
        new(value, onValueChanged)
        {
            MaxRating = maxRating,
            Caption = caption,
            IsClearEnabled = isClearEnabled,
        };
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

            // StarMeter.Of(...) is the sole construction path: it returns a
            // StarMeterElement AND ensures (via its cctor) that the global
            // ControlRegistry has the handler. No reconciler.RegisterHandler
            // call lives anywhere in this app.
            StarMeter.Of(rating, setRating, caption: "Rate this page"),

            TextBlock($"current rating: {rating:0.0}"),

            HStack(8,
                Button("Reset", () => setRating(0)),
                Button("5 stars", () => setRating(5)))
        ).Padding(20);
    }
}
// </snippet:star-meter-usage>

