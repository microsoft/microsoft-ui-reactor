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
// Controlled props use Optional<T>: Unset means the WinUI control owns the value.
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
    public Optional<double> Value { get; init; } = Optional<double>.Unset;
    public int MaxRating { get; init; } = 5;
    public string? Caption { get; init; }
    public bool IsClearEnabled { get; init; } = true;
    public System.Action<double>? OnValueChanged { get; init; }

    internal StarMeterElement(Optional<double> value, System.Action<double>? onValueChanged = null)
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
        // Controlled is the two-way binding shape. Its get lambda returns
        // Optional<double>: Unset skips framework writes so the control owns
        // the value; HasValue force-asserts on Mount and Update with echo
        // suppression, then forwards user input through OnValueChanged.
        // Subscription is gated on the callback being non-null.
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
        Of(Optional<double>.Of(value), onValueChanged, maxRating, caption, isClearEnabled);

    public static StarMeterElement Of(
        Optional<double> value,
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

class InfoBarEdgeTriggeredExample : Component
{
    public override Element Render()
    {
        // <snippet:infobar-edge-triggered>
        var (showBanner, setShowBanner) = UseState(true);

        return VStack(
            InfoBar("Saved", "Your changes were saved.").IsClosable() with
            {
                IsOpen  = showBanner,
                // Without this the dismissal never reaches app state, so a later
                // `setShowBanner(true)` is a no-op — it is already true.
                OnClosed = () => setShowBanner(false),
            },
            Button("Show again", () => setShowBanner(true)));
        // </snippet:infobar-edge-triggered>
    }
}

static class ReferenceDescriptorExample
{
    public static ControlDescriptor<ReferenceDemoElement, ReferenceDemoControl> AddReferenceEntries(
        ControlDescriptor<ReferenceDemoElement, ReferenceDemoControl> descriptor)
    {
        // <snippet:reference-properties>
        descriptor.Reference<FrameworkElement>(
            get: static e => e.Target,
            set: static (c, target) => c.Target = target);

        descriptor.ReferenceList<FrameworkElement>(
            get: static e => e.Related,
            apply: static (c, targets) =>
            {
                c.Related.Clear();
                foreach (var target in targets)
                    c.Related.Add(target);
            });
        // </snippet:reference-properties>

        return descriptor;
    }
}

sealed record ReferenceDemoElement : Element
{
    public Microsoft.UI.Reactor.Input.ElementRef<FrameworkElement>? Target { get; init; }
    public IReadOnlyList<Microsoft.UI.Reactor.Input.ElementRef<FrameworkElement>>? Related { get; init; }
}

sealed partial class ReferenceDemoControl : WinUI.Control
{
    public FrameworkElement? Target { get; set; }
    public List<FrameworkElement> Related { get; } = [];
}

static class GlobalRegistrationExample
{
    private static void RegisterGlobalButtonOverride()
    {
        // <snippet:global-registration>
        ControlRegistry.Register<ButtonElement, Microsoft.UI.Xaml.Controls.Button>(
            static () => new MyButtonHandler());
        // </snippet:global-registration>
    }
}

sealed class MyButtonHandler : IElementHandler<ButtonElement, Microsoft.UI.Xaml.Controls.Button>
{
    public Microsoft.UI.Xaml.Controls.Button Mount(MountContext ctx, ButtonElement element) =>
        new();

    public void Update(
        UpdateContext ctx,
        ButtonElement oldEl,
        ButtonElement newEl,
        Microsoft.UI.Xaml.Controls.Button control)
    {
    }
}

static class PerHostRegistrationExample
{
    private static void RegisterEditorHost()
    {
        // <snippet:per-host-registration>
        ReactorApp.Run<EditorApp>("Monaco Editor", configure: host =>
        {
            host.Reconciler.RegisterType<MonacoEditorElement, MonacoEditor>(
                mount: static (r, el, requestRerender) => new MonacoEditor { Text = el.Text },
                update: static (r, oldEl, newEl, editor, requestRerender) =>
                {
                    if (newEl.Text != oldEl.Text) editor.Text = newEl.Text;
                    return null;   // null = the same control was patched in place
                },
                unmount: static (r, editor) => editor.Dispose());
        });
        // </snippet:per-host-registration>
    }
}

sealed record MonacoEditorElement(string Text) : Element;

sealed partial class MonacoEditor : WinUI.Control, IDisposable
{
    public string Text { get; set; } = string.Empty;

    public void Dispose()
    {
    }
}

sealed class EditorApp : Component
{
    public override Element Render() => new MonacoEditorElement("editor text");
}
