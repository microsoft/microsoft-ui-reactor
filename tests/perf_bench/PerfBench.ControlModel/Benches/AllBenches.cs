// Spec 047 §15.3 — M1 through M13 micro suite.
//
// Each bench implements three variants per §15.2. The `Reactor` path is the
// production control model compared against the `ReactorToday` baseline.
//
// Several benches need real WinUI controls — they're constructed under
// `BenchContext.Parent` which the host arranges to live on the UI thread.
// Benches that exercise allocation behavior on the managed side (M10/M11)
// don't strictly need a UI control but stay on the UI thread for
// consistency with the reconciler invariant.
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PerfBench.ControlModel.Variants;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace PerfBench.ControlModel.Benches;

/// <summary>M1 — `Mount_Leaf_NoCallback`. TextBlockElement, no callbacks.</summary>
public sealed class M01_MountLeafNoCallback : IBench
{
    public string Id => "M1";
    public string Name => "Mount_Leaf_NoCallback";

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        if (variant == BenchVariant.Direct)
        {
            var tb = new TextBlock { Text = "hi" };
            ctx.Parent.Children.Add(tb);
            ctx.Parent.Children.Remove(tb);
        }
        else
        {
            // ReactorToday and Reactor share the same bench flow.
            var el = TextBlock("hi");
            var ui = ctx.Reconciler.Mount(el, NoOp);
            if (ui is not null)
            {
                ctx.Parent.Children.Add(ui);
                ctx.Parent.Children.Remove(ui);
                ctx.Reconciler.UnmountChild(ui);
            }
        }
    }

    public void DemoMount(BenchVariant variant, BenchContext ctx)
    {
        if (variant == BenchVariant.Direct)
            ctx.Parent.Children.Add(new TextBlock { Text = "M1 Direct: 'hi'", FontSize = 20 });
        else
        {
            var ui = ctx.Reconciler.Mount(TextBlock($"M1 {variant}: 'hi'").FontSize(20), NoOp);
            if (ui is not null) ctx.Parent.Children.Add(ui);
        }
    }

    private static readonly Action NoOp = static () => { };
}

/// <summary>M2 — `Mount_Leaf_OneCallback`. ToggleSwitchElement with OnIsOnChanged.</summary>
public sealed class M02_MountLeafOneCallback : IBench
{
    public string Id => "M2";
    public string Name => "Mount_Leaf_OneCallback";

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        if (variant == BenchVariant.Direct)
        {
            var ts = new WinUI.ToggleSwitch { IsOn = false };
            ts.Toggled += OnToggled;
            ctx.Parent.Children.Add(ts);
            ts.Toggled -= OnToggled;
            ctx.Parent.Children.Remove(ts);
        }
        else
        {
            var el = ToggleSwitch(isOn: false, onIsOnChanged: _ => { });
            var ui = ctx.Reconciler.Mount(el, NoOp);
            if (ui is not null)
            {
                ctx.Parent.Children.Add(ui);
                ctx.Parent.Children.Remove(ui);
                ctx.Reconciler.UnmountChild(ui);
            }
        }
    }

    public void DemoMount(BenchVariant variant, BenchContext ctx)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new TextBlock { Text = $"M2 {variant}: ToggleSwitch + OnIsOnChanged", FontSize = 16 });
        if (variant == BenchVariant.Direct)
        {
            var ts = new WinUI.ToggleSwitch { IsOn = false };
            ts.Toggled += OnToggled;
            stack.Children.Add(ts);
        }
        else
        {
            var ui = ctx.Reconciler.Mount(ToggleSwitch(false, onIsOnChanged: _ => { }), NoOp);
            if (ui is not null) stack.Children.Add(ui);
        }
        ctx.Parent.Children.Add(stack);
    }

    private static void OnToggled(object sender, RoutedEventArgs e) { }
    private static readonly Action NoOp = static () => { };
}

/// <summary>M3 — `Mount_Leaf_ThreeCallbacks`. Button with OnClick + OnPointerPressed + OnTapped.</summary>
public sealed class M03_MountLeafThreeCallbacks : IBench
{
    public string Id => "M3";
    public string Name => "Mount_Leaf_ThreeCallbacks";

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        if (variant == BenchVariant.Direct)
        {
            var btn = new WinUI.Button { Content = "x" };
            btn.Click += OnClick;
            btn.PointerPressed += OnPointer;
            btn.Tapped += OnTapped;
            ctx.Parent.Children.Add(btn);
            btn.Click -= OnClick;
            btn.PointerPressed -= OnPointer;
            btn.Tapped -= OnTapped;
            ctx.Parent.Children.Remove(btn);
        }
        else
        {
            var el = Button("x", () => { })
                .OnPointerPressed((_, _) => { })
                .OnTapped((_, _) => { });
            var ui = ctx.Reconciler.Mount(el, NoOp);
            if (ui is not null)
            {
                ctx.Parent.Children.Add(ui);
                ctx.Parent.Children.Remove(ui);
                ctx.Reconciler.UnmountChild(ui);
            }
        }
    }

    public void DemoMount(BenchVariant variant, BenchContext ctx)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new TextBlock { Text = $"M3 {variant}: Button + 3 callbacks", FontSize = 16 });
        if (variant == BenchVariant.Direct)
        {
            var btn = new WinUI.Button { Content = "Click" };
            btn.Click += OnClick;
            btn.PointerPressed += OnPointer;
            btn.Tapped += OnTapped;
            stack.Children.Add(btn);
        }
        else
        {
            var el = Button("Click", () => { })
                .OnPointerPressed((_, _) => { })
                .OnTapped((_, _) => { });
            var ui = ctx.Reconciler.Mount(el, NoOp);
            if (ui is not null) stack.Children.Add(ui);
        }
        ctx.Parent.Children.Add(stack);
    }

    private static void OnClick(object sender, RoutedEventArgs e) { }
    private static void OnPointer(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { }
    private static void OnTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) { }
    private static readonly Action NoOp = static () => { };
}

/// <summary>
/// M4 — `Dispatch_Switch_Cold`. First mount of each of N element types,
/// measured per-arm. At Phase 0 we use a small representative subset (8
/// element types) so the cold-PGO behavior is well-defined without
/// requiring all 70 element factories to be hand-rolled.
/// </summary>
public sealed class M04_DispatchSwitchCold : IBench
{
    public string Id => "M4";
    public string Name => "Dispatch_Switch_Cold";

    private static readonly Microsoft.UI.Reactor.GridSize[] _emptyGridTracks =
        [Microsoft.UI.Reactor.GridSize.Star()];

    private static readonly Func<Element>[] _factories =
    [
        () => TextBlock("a"),
        () => Button("b"),
        () => ToggleSwitch(false),
        () => CheckBox(false),
        () => Slider(0, 0, 100),
        () => HStack(),
        () => VStack(),
        () => Grid(_emptyGridTracks, _emptyGridTracks),
    ];

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        var el = _factories[ctx.Iteration % _factories.Length]();
        if (variant == BenchVariant.Direct)
        {
            // Direct equivalent: a switch on a synthetic type tag, modeling
            // the hand-written code-behind for each element. We approximate
            // the cost by allocating the same WinUI control directly.
            UIElement? ui = el switch
            {
                TextBlockElement => new TextBlock(),
                ButtonElement => new WinUI.Button(),
                ToggleSwitchElement => new WinUI.ToggleSwitch(),
                CheckBoxElement => new WinUI.CheckBox(),
                SliderElement => new WinUI.Slider(),
                _ => new WinUI.StackPanel(),
            };
            if (ui is not null) ctx.Parent.Children.Add(ui);
            if (ui is not null) ctx.Parent.Children.Remove(ui);
        }
        else
        {
            var ui = ctx.Reconciler.Mount(el, NoOp);
            if (ui is not null)
            {
                ctx.Parent.Children.Add(ui);
                ctx.Parent.Children.Remove(ui);
                ctx.Reconciler.UnmountChild(ui);
            }
        }
    }

    public void DemoMount(BenchVariant variant, BenchContext ctx)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        stack.Children.Add(new TextBlock { Text = $"M4 {variant}: one of each element type (cold)", FontSize = 16 });
        for (int i = 0; i < _factories.Length; i++)
        {
            var el = _factories[i]();
            UIElement? ui;
            if (variant == BenchVariant.Direct)
            {
                ui = el switch
                {
                    TextBlockElement => new TextBlock { Text = el.GetType().Name },
                    ButtonElement => new WinUI.Button { Content = el.GetType().Name },
                    ToggleSwitchElement => new WinUI.ToggleSwitch(),
                    CheckBoxElement => new WinUI.CheckBox { Content = el.GetType().Name },
                    SliderElement => new WinUI.Slider { Minimum = 0, Maximum = 100 },
                    _ => new TextBlock { Text = el.GetType().Name },
                };
            }
            else
            {
                ui = ctx.Reconciler.Mount(el, NoOp);
            }
            if (ui is not null) stack.Children.Add(ui);
        }
        ctx.Parent.Children.Add(stack);
    }

    private static readonly Action NoOp = static () => { };
}

/// <summary>M5 — `Dispatch_Switch_Warm`. After 10k mounts, measure hot dispatch.</summary>
public sealed class M05_DispatchSwitchWarm : IBench
{
    public string Id => "M5";
    public string Name => "Dispatch_Switch_Warm";

    // Same shape as M4 but the runner's warm-up phase ensures PGO is hot.
    private readonly M04_DispatchSwitchCold _inner = new();

    public void RunOne(BenchVariant variant, BenchContext ctx) => _inner.RunOne(variant, ctx);
    public void DemoMount(BenchVariant variant, BenchContext ctx)
    {
        _inner.DemoMount(variant, ctx);
        // Patch the header so M5 doesn't show M4's label.
        if (ctx.Parent.Children.Count > 0
            && ctx.Parent.Children[0] is StackPanel sp
            && sp.Children.Count > 0
            && sp.Children[0] is TextBlock tb)
        {
            tb.Text = $"M5 {variant}: one of each element type (warm — PGO hot)";
        }
    }
}

/// <summary>M6 — `Dispatch_ExternalType`. RegisterType external mount.</summary>
public sealed class M06_DispatchExternalType : IBench
{
    public string Id => "M6";
    public string Name => "Dispatch_ExternalType";

    public sealed record ExtElement(string Label) : Element;

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        if (variant == BenchVariant.Direct)
        {
            // No registry path under Direct — model the floor as direct construction.
            var tb = new TextBlock { Text = "ext" };
            ctx.Parent.Children.Add(tb);
            ctx.Parent.Children.Remove(tb);
        }
        else
        {
            var el = new ExtElement("ext");
            var ui = ctx.Reconciler.Mount(el, NoOp);
            if (ui is not null)
            {
                ctx.Parent.Children.Add(ui);
                ctx.Parent.Children.Remove(ui);
                ctx.Reconciler.UnmountChild(ui);
            }
        }
    }

    public void DemoMount(BenchVariant variant, BenchContext ctx)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new TextBlock { Text = $"M6 {variant}: RegisterType external", FontSize = 16 });
        if (variant == BenchVariant.Direct)
            stack.Children.Add(new TextBlock { Text = "(no registry under Direct)" });
        else
        {
            var ui = ctx.Reconciler.Mount(new ExtElement("registered ExtElement"), NoOp);
            if (ui is not null) stack.Children.Add(ui);
        }
        ctx.Parent.Children.Add(stack);
    }

    private static readonly Action NoOp = static () => { };
}

/// <summary>M7 — `Update_NoChange`. 1000-element tree, no-op re-render.</summary>
public sealed class M07_UpdateNoChange : IBench
{
    public string Id => "M7";
    public string Name => "Update_NoChange";
    private const int TreeSize = 1000;

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        // Setup once per repetition: scratch holds the mounted controls.
        var fixture = ctx.Scratch as Fixture;
        if (fixture is null)
        {
            fixture = new Fixture(ctx, variant);
            ctx.Scratch = fixture;
            return;
        }

        // Re-apply the same element tree as a diff.
        if (variant == BenchVariant.Direct)
        {
            // Direct update path: no Reactor; mimic the no-op cost as a loop over Children.
            foreach (var c in ctx.Parent.Children)
            {
                if (c is TextBlock tb) tb.Text = tb.Text; // no-op assignment
            }
        }
        else
        {
            fixture.ReRender(ctx);
        }
    }

    private sealed class Fixture
    {
        public readonly Element[] Elements;
        public readonly UIElement[] Controls;

        public Fixture(BenchContext ctx, BenchVariant variant)
        {
            Elements = new Element[TreeSize];
            Controls = new UIElement[TreeSize];
            for (int i = 0; i < TreeSize; i++)
            {
                Elements[i] = TextBlock("x");
                if (variant == BenchVariant.Direct)
                {
                    Controls[i] = new TextBlock { Text = "x" };
                    ctx.Parent.Children.Add(Controls[i]);
                }
                else
                {
                    var ui = ctx.Reconciler.Mount(Elements[i], NoOp);
                    if (ui is not null) { Controls[i] = ui; ctx.Parent.Children.Add(ui); }
                }
            }
        }

        public void ReRender(BenchContext ctx)
        {
            for (int i = 0; i < TreeSize; i++)
            {
                ctx.Reconciler.UpdateChild(Elements[i], Elements[i], Controls[i], NoOp);
            }
        }
    }

    private static readonly Action NoOp = static () => { };
}

/// <summary>M8 — `Update_OneLeafChanged`. Depth-5 leaf delta.</summary>
public sealed class M08_UpdateOneLeafChanged : IBench
{
    public string Id => "M8";
    public string Name => "Update_OneLeafChanged";
    private const int TreeSize = 1000;

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        var fixture = ctx.Scratch as Fixture;
        if (fixture is null)
        {
            fixture = new Fixture(ctx, variant);
            ctx.Scratch = fixture;
            return;
        }

        if (variant == BenchVariant.Direct)
        {
            var idx = ctx.Iteration % TreeSize;
            if (ctx.Parent.Children[idx] is TextBlock tb)
                tb.Text = (ctx.Iteration & 1) == 0 ? "a" : "b";
        }
        else
        {
            fixture.MutateOne(ctx);
        }
    }

    private sealed class Fixture
    {
        public readonly TextBlockElement[] Elements;
        public readonly UIElement[] Controls;

        public Fixture(BenchContext ctx, BenchVariant variant)
        {
            Elements = new TextBlockElement[TreeSize];
            Controls = new UIElement[TreeSize];
            for (int i = 0; i < TreeSize; i++)
            {
                Elements[i] = TextBlock("a");
                if (variant == BenchVariant.Direct)
                {
                    Controls[i] = new TextBlock { Text = "a" };
                    ctx.Parent.Children.Add(Controls[i]);
                }
                else
                {
                    var ui = ctx.Reconciler.Mount(Elements[i], NoOp);
                    if (ui is not null) { Controls[i] = ui; ctx.Parent.Children.Add(ui); }
                }
            }
        }

        public void MutateOne(BenchContext ctx)
        {
            var idx = ctx.Iteration % TreeSize;
            var old = Elements[idx];
            var fresh = TextBlock((ctx.Iteration & 1) == 0 ? "a" : "b");
            ctx.Reconciler.UpdateChild(old, fresh, Controls[idx], NoOp);
            Elements[idx] = fresh;
        }
    }

    private static readonly Action NoOp = static () => { };
}

/// <summary>M9 — `Update_AllChanged`. Every value-bearing prop differs.</summary>
public sealed class M09_UpdateAllChanged : IBench
{
    public string Id => "M9";
    public string Name => "Update_AllChanged";
    private const int TreeSize = 1000;

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        var fixture = ctx.Scratch as Fixture;
        if (fixture is null)
        {
            fixture = new Fixture(ctx, variant);
            ctx.Scratch = fixture;
            return;
        }

        if (variant == BenchVariant.Direct)
        {
            var label = (ctx.Iteration & 1) == 0 ? "a" : "b";
            for (int i = 0; i < TreeSize; i++)
            {
                if (ctx.Parent.Children[i] is TextBlock tb) tb.Text = label;
            }
        }
        else
        {
            fixture.MutateAll(ctx);
        }
    }

    private sealed class Fixture
    {
        public readonly TextBlockElement[] Elements;
        public readonly UIElement[] Controls;

        public Fixture(BenchContext ctx, BenchVariant variant)
        {
            Elements = new TextBlockElement[TreeSize];
            Controls = new UIElement[TreeSize];
            for (int i = 0; i < TreeSize; i++)
            {
                Elements[i] = TextBlock("a");
                if (variant == BenchVariant.Direct)
                {
                    Controls[i] = new TextBlock { Text = "a" };
                    ctx.Parent.Children.Add(Controls[i]);
                }
                else
                {
                    var ui = ctx.Reconciler.Mount(Elements[i], NoOp);
                    if (ui is not null) { Controls[i] = ui; ctx.Parent.Children.Add(ui); }
                }
            }
        }

        public void MutateAll(BenchContext ctx)
        {
            var label = (ctx.Iteration & 1) == 0 ? "a" : "b";
            for (int i = 0; i < TreeSize; i++)
            {
                var fresh = TextBlock(label);
                ctx.Reconciler.UpdateChild(Elements[i], fresh, Controls[i], NoOp);
                Elements[i] = fresh;
            }
        }
    }

    private static readonly Action NoOp = static () => { };
}

/// <summary>
/// M10 — `EventHandlerState_Alloc`. Wire one event, measure allocation
/// count + bytes. The headline §9 win for V2. Today's number is dominated
/// by `ModifierEventHandlerState` allocation; V2 should not allocate
/// `ModifierEventHandlerState` for a control whose only event is
/// control-intrinsic.
/// </summary>
public sealed class M10_EventHandlerStateAlloc : IBench
{
    public string Id => "M10";
    public string Name => "EventHandlerState_Alloc";

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        if (variant == BenchVariant.Direct)
        {
            var ts = new WinUI.ToggleSwitch();
            ts.Toggled += OnToggled;
            ctx.Parent.Children.Add(ts);
            ts.Toggled -= OnToggled;
            ctx.Parent.Children.Remove(ts);
        }
        else
        {
            // ReactorToday allocates EventHandlerState eagerly for the Toggled wiring;
            // Reactor is measured against that baseline.
            var el = ToggleSwitch(false, onIsOnChanged: _ => { });
            var ui = ctx.Reconciler.Mount(el, NoOp);
            if (ui is not null)
            {
                ctx.Parent.Children.Add(ui);
                ctx.Parent.Children.Remove(ui);
                ctx.Reconciler.UnmountChild(ui);
            }
        }
    }

    public void DemoMount(BenchVariant variant, BenchContext ctx)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new TextBlock { Text = $"M10 {variant}: ToggleSwitch + alloc-counted wiring", FontSize = 16 });
        if (variant == BenchVariant.Direct)
        {
            var ts = new WinUI.ToggleSwitch();
            ts.Toggled += OnToggled;
            stack.Children.Add(ts);
        }
        else
        {
            var ui = ctx.Reconciler.Mount(ToggleSwitch(false, onIsOnChanged: _ => { }), NoOp);
            if (ui is not null) stack.Children.Add(ui);
        }
        ctx.Parent.Children.Add(stack);
    }

    private static void OnToggled(object sender, RoutedEventArgs e) { }
    private static readonly Action NoOp = static () => { };
}

/// <summary>
/// M11 — `ModifierEHS_Frequency`. Mount a 1000-element representative tree;
/// count ModifierEventHandlerState allocations. Validates the §9.4
/// "rare in practice" hypothesis. Counter is reported as
/// `ModifierEHSAllocations`.
/// </summary>
public sealed class M11_ModifierEHSFrequency : IBench, ICounterCarrier
{
    public string Id => "M11";
    public string Name => "ModifierEHS_Frequency";
    public long Value { get; private set; }
    public string Label => "ModifierEHSAllocations";

    // Phase 0: no public counter exposes ModifierEHS allocation count.
    // The bench produces a synthetic placeholder — Phase 1 wires up an
    // EventSource counter or replaces this with a heap-sample diff.
    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        // Mount a representative mix once per repetition's first iteration.
        if (ctx.Iteration != 0) return;
        if (variant == BenchVariant.Direct) return; // Direct doesn't allocate ModifierEHS.

        ctx.Scratch = this;
        Value = 0;
        const int treeSize = 1000;
        for (int i = 0; i < treeSize; i++)
        {
            Element el = (i % 5) switch
            {
                0 => TextBlock("x"),
                1 => Button("y"),
                2 => ToggleSwitch(false),
                3 => Slider(0, 0, 100),
                _ => Border(TextBlock("z")),
            };
            // Mix in a routed-input modifier on ~10% of elements (matches the §9.4
            // assumption that most leaves have no user-added input handlers).
            if (i % 10 == 0)
                el = el.OnPointerPressed((_, _) => { });

            var ui = ctx.Reconciler.Mount(el, NoOp);
            if (ui is not null) ctx.Parent.Children.Add(ui);
        }
        // Placeholder counter — Phase 1 replaces with the real EventSource counter.
        Value = -1;
    }

    private static readonly Action NoOp = static () => { };
}

/// <summary>
/// M12 — `Pool_Rent_HotPath`. ListView recycle: 100 element instances
/// cycling through 20 pool slots. Modeled as repeated mount/unmount of
/// poolable controls.
/// </summary>
public sealed class M12_PoolRentHotPath : IBench
{
    public string Id => "M12";
    public string Name => "Pool_Rent_HotPath";
    private const int Slots = 20;

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        // TextBlock is poolable in the current pool policy.
        if (variant == BenchVariant.Direct)
        {
            // No pooling under Direct — each iteration allocates.
            var tb = new TextBlock { Text = "x" };
            ctx.Parent.Children.Add(tb);
            ctx.Parent.Children.Remove(tb);
        }
        else
        {
            var el = TextBlock("x");
            var ui = ctx.Reconciler.Mount(el, NoOp);
            if (ui is not null)
            {
                ctx.Parent.Children.Add(ui);
                ctx.Parent.Children.Remove(ui);
                ctx.Reconciler.UnmountChild(ui); // returns to pool when poolable
            }
        }
    }

    public void DemoMount(BenchVariant variant, BenchContext ctx)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new TextBlock { Text = $"M12 {variant}: poolable TextBlock cycle", FontSize = 16 });
        if (variant == BenchVariant.Direct)
            stack.Children.Add(new TextBlock { Text = "(no pool under Direct)" });
        else
        {
            var ui = ctx.Reconciler.Mount(TextBlock("rented/returned x"), NoOp);
            if (ui is not null) stack.Children.Add(ui);
        }
        ctx.Parent.Children.Add(stack);
    }

    private static readonly Action NoOp = static () => { };
}

/// <summary>
/// M13 — `Setters_Suppression_Scope`. `Set(ts => ts.IsOn = true)` on a
/// ToggleSwitch with `OnIsOnChanged`. **Correctness**, not perf — verify
/// callback fires exactly once today (the §8.2 bug); zero times after
/// Phase 1's fix. Phase-0 records the failing behavior as the baseline.
/// </summary>
public sealed class M13_SettersSuppressionScope : IBench, ICounterCarrier
{
    public string Id => "M13";
    public string Name => "Setters_Suppression_Scope";
    public long Value { get; private set; }
    public string Label => "OnIsOnChangedFireCount";

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        // Only meaningful for Reactor variants — Direct has no setter scope.
        if (variant == BenchVariant.Direct) return;
        if (ctx.Iteration != 0) return;

        ctx.Scratch = this;
        Value = 0;

        int fires = 0;
        var el = ToggleSwitch(isOn: false, onIsOnChanged: _ => fires++)
            .Set(ts => ts.IsOn = true);
        var ui = ctx.Reconciler.Mount(el, NoOp);
        if (ui is not null) ctx.Parent.Children.Add(ui);

        Value = fires;
        // Phase 0 expectation: fires == 1 (the §8.2 bug).
        // Phase 1 expectation: fires == 0 (the fix).
    }

    private static readonly Action NoOp = static () => { };
}

public static class BenchCatalog
{
    public static IReadOnlyList<IBench> All { get; } = new IBench[]
    {
        new M01_MountLeafNoCallback(),
        new M02_MountLeafOneCallback(),
        new M03_MountLeafThreeCallbacks(),
        new M04_DispatchSwitchCold(),
        new M05_DispatchSwitchWarm(),
        new M06_DispatchExternalType(),
        new M07_UpdateNoChange(),
        new M08_UpdateOneLeafChanged(),
        new M09_UpdateAllChanged(),
        new M10_EventHandlerStateAlloc(),
        new M11_ModifierEHSFrequency(),
        new M12_PoolRentHotPath(),
        new M13_SettersSuppressionScope(),
    };
}
