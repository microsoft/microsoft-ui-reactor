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

/// <summary>OAlloc — Optional&lt;T&gt; migrated element allocation throughput.</summary>
public sealed class OptionalElementAllocBench : IBench
{
    public string Id => "OAlloc";
    public string Name => "Optional_Element_Alloc";

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        var textValue = TextBox("x", static _ => { });
        var textUnset = TextBox(Optional<string>.Unset, static _ => { });
        int? gridIndex = 1;
        var gridValue = GridView(gridIndex, static _ => { }, GridItems);
        var gridUnset = GridView(Optional<int>.Unset, static _ => { }, GridItems);

        GC.KeepAlive(textValue);
        GC.KeepAlive(textUnset);
        GC.KeepAlive(gridValue);
        GC.KeepAlive(gridUnset);
    }

    private static readonly Element[] GridItems = Enumerable.Range(0, 100)
        .Select(static i => TextBlock(i.ToString()))
        .ToArray();
}

/// <summary>OUpdate — Optional&lt;T&gt; controlled-prop reconciler update hot path.</summary>
public sealed class OptionalReconcilerUpdateBench : IBench
{
    public string Id => "OUpdate";
    public string Name => "Optional_Reconciler_Update";

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        var fixture = ctx.Scratch as Fixture;
        if (fixture is null)
        {
            fixture = new Fixture(ctx);
            ctx.Scratch = fixture;
            return;
        }

        fixture.Update(ctx);
    }

    private sealed class Fixture
    {
        private ToggleSwitchElement _hasValueElement;
        private ToggleSwitchElement _unsetElement;
        private readonly UIElement _hasValueControl;
        private readonly UIElement _unsetControl;

        public Fixture(BenchContext ctx)
        {
            _hasValueElement = ToggleSwitch(false, static _ => { }).Margin(0);
            _unsetElement = ToggleSwitch(Optional<bool>.Unset, static _ => { }).Margin(0);
            _hasValueControl = ctx.Reconciler.Mount(_hasValueElement, NoOp)!;
            _unsetControl = ctx.Reconciler.Mount(_unsetElement, NoOp)!;
            ctx.Parent.Children.Add(_hasValueControl);
            ctx.Parent.Children.Add(_unsetControl);
        }

        public void Update(BenchContext ctx)
        {
            var alt = (ctx.Iteration & 1) == 0;
            var nextHasValue = ToggleSwitch(alt, static _ => { }).Margin(alt ? 0 : 1);
            var nextUnset = ToggleSwitch(Optional<bool>.Unset, static _ => { }).Margin(alt ? 0 : 1);

            ctx.Reconciler.UpdateChild(_hasValueElement, nextHasValue, _hasValueControl, NoOp);
            ctx.Reconciler.UpdateChild(_unsetElement, nextUnset, _unsetControl, NoOp);

            _hasValueElement = nextHasValue;
            _unsetElement = nextUnset;
        }
    }

    private static readonly Action NoOp = static () => { };
}

/// <summary>
/// C207 — Issue #207. Change-event trampoline ReactorState DP-read coalescing.
///
/// Holds one tagged <see cref="WinUI.TextBox"/> off-tree and runs the
/// change-handler body N times, isolating the cost difference of the attached-DP
/// (<c>GetValue(StateProperty)</c>, a COM-interop read) traffic per event:
///   <list type="bullet">
///     <item><b>ReactorToday</b> = BEFORE #207 — two DP reads per event
///       (<c>ChangeEchoSuppressor.ShouldSuppress</c> + <c>Reconciler.GetElementTag</c>).</item>
///     <item><b>Reactor</b> = AFTER #207 — one DP read per event
///       (<c>Reconciler.TryGetReactorState</c> → reuse <c>state</c> for both the
///       suppression check and the live-element dispatch).</item>
///     <item><b>Direct</b> = floor — callback invoke only, no DP read.</item>
///   </list>
/// Encoding both patterns as variants makes this an in-process A/B: a single
/// build measures the BEFORE and AFTER under identical conditions, so the delta
/// is exactly the one eliminated <c>GetValue(StateProperty)</c> read per event.
/// </summary>
public sealed class C207_ChangeHandlerDpRead : IBench
{
    public string Id => "C207";
    public string Name => "ChangeHandler_DpRead_Coalesce";

    private sealed class Holder
    {
        public required WinUI.TextBox Control;
        public required TextBoxElement Element;
    }

    // Static sink incremented by the dispatched callback so the JIT cannot
    // elide the trampoline body as dead code.
    private static long _sink;

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        if (ctx.Scratch is not Holder h)
        {
            var tb = new WinUI.TextBox();
            var el = new TextBoxElement(Value: "x", OnChanged: static _ => _sink++);
            Reconciler.SetElementTag(tb, el);
            h = new Holder { Control = tb, Element = el };
            ctx.Scratch = h;
        }

        switch (variant)
        {
            case BenchVariant.Direct:
                // Floor: dispatch with no attached-DP read at all.
                h.Element.OnChanged?.Invoke("x");
                break;

            case BenchVariant.ReactorToday:
                // BEFORE #207: two GetValue(StateProperty) reads per event.
                if (ChangeEchoSuppressor.ShouldSuppress(h.Control)) return;
                (Reconciler.GetElementTag(h.Control) as TextBoxElement)?.OnChanged?.Invoke("x");
                break;

            case BenchVariant.Reactor:
                // AFTER #207: one GetValue(StateProperty) read per event, shared
                // by the suppression check and the live-element dispatch.
                if (!Reconciler.TryGetReactorState(h.Control, out var state)) return;
                if (ChangeEchoSuppressor.ShouldSuppress(state)) return;
                (state.Element as TextBoxElement)?.OnChanged?.Invoke("x");
                break;
        }
    }

    public void DemoMount(BenchVariant variant, BenchContext ctx)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new TextBlock
        {
            Text = $"C207 {variant}: change-handler ReactorState DP-read coalescing",
            FontSize = 16,
        });
        ctx.Parent.Children.Add(stack);
    }
}

/// <summary>
/// M14 — <c>Dsl_Rebuild_Cascade</c>. Rebuild a moderate leaf tree through a DEEP
/// fluent-modifier cascade (typed layout + visual modifiers AND a <c>.Set(...)</c>
/// setter) on EVERY iteration with no memoization, then reconcile it against the
/// structurally-equal prior tree.
///
/// <para>
/// Instrument for PR #665 ("restore diff skip-path + cut DSL/element per-render
/// allocations"). The macro (StocksGrid) leg memoizes unchanged cells and writes
/// <c>new TextBlockElement{…}</c> initializers, BYPASSING the fluent cascade; and
/// none of the M1–M13 / OAlloc / OUpdate micros rebuild a deep fluent + <c>.Set()</c>
/// tree and re-diff it against an equal prior tree. So #665's two effects had no
/// sensitive instrument:
/// </para>
/// <list type="number">
///   <item><b>DSL alloc cut</b> — each chained layout/visual modifier
///     (<c>.Foreground().Padding().Margin().Width().Height()</c>) merges its delta
///     into the <c>Layout</c> / <c>Visual</c> bucket instead of allocating a
///     throwaway parent <see cref="ElementModifiers"/> per step. Rebuilding the whole
///     tree every op makes that per-step <see cref="ElementModifiers"/> churn the
///     dominant, robustly-measured allocation, so M14's allocated-bytes/op drops
///     when #665 is applied.</item>
///   <item><b>SettersEqual diff-skip</b> — the <c>.Set(…)</c> on each cell drives the
///     reconciler down the <c>Setters</c>-array comparison arm that #665 reroutes
///     from a raw <c>ReferenceEquals</c> through <c>SettersEqual</c>, so the skip-path
///     restore is exercised on every re-diff.</item>
/// </list>
///
/// <para>
/// Most cells stay structurally unchanged across iterations (a rotating ~1/16 are
/// mutated) so the equal-tree re-diff is the common case — the same way
/// <see cref="OptionalReconcilerUpdateBench"/> (OUpdate) isolates a controlled-prop
/// update the macro buries. This bench changes no framework behavior; it only adds a
/// measurement surface.
/// </para>
/// </summary>
public sealed class M14_DslRebuildCascade : IBench
{
    public string Id => "M14";
    public string Name => "Dsl_Rebuild_Cascade";

    // Moderate leaf count (spec window 200–500). Big enough that the per-step
    // ElementModifiers churn dominates the alloc signal, small enough that the
    // reconcile stays comfortably inside the micro per-round budget.
    private const int TreeSize = 300;

    // A rotating ~1/16 of cells flip their text each iteration; the rest are
    // rebuilt structurally identical so SettersEqual / ShallowEquals see an equal
    // prior tree (the case #665 targets).
    private const int MutateStride = 16;

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        var fixture = ctx.Scratch as Fixture;
        if (fixture is null)
        {
            // First iteration of each repetition: build + mount the initial tree.
            // Negligible (1 of N) against the measured rebuild/reconcile loop.
            fixture = new Fixture(ctx, variant);
            ctx.Scratch = fixture;
            return;
        }

        if (variant == BenchVariant.Direct)
            fixture.RebuildDirect(ctx);
        else
            fixture.RebuildAndReconcile(ctx);
    }

    public void DemoMount(BenchVariant variant, BenchContext ctx)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new TextBlock
        {
            Text = $"M14 {variant}: deep DSL cascade + .Set ×{TreeSize}, rebuilt & reconciled",
            FontSize = 16,
        });
        const int demoCells = 6;
        for (int i = 0; i < demoCells; i++)
        {
            if (variant == BenchVariant.Direct)
            {
                var tb = new TextBlock();
                ApplyDirect(tb, Texts[i]);
                stack.Children.Add(tb);
            }
            else
            {
                var ui = ctx.Reconciler.Mount(BuildCell(i, mutated: false), NoOp);
                if (ui is not null) stack.Children.Add(ui);
            }
        }
        ctx.Parent.Children.Add(stack);
    }

    // Shared, cached brush so .Foreground(brush) binds the Brush overload (not the
    // color-string parser) and reuses one instance across cells/iterations — the
    // cascade's allocation profile stays dominated by the ElementModifiers churn
    // #665 targets, and ModifiersEqual sees an unchanged Foreground on re-diff.
    private static readonly Microsoft.UI.Xaml.Media.Brush CellBrush =
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.SteelBlue);

    // Non-capturing setter — the compiler caches it as a static singleton, so .Set
    // allocates only the fresh Setters array (the path #665's SettersEqual arm
    // compares), not a per-call delegate.
    private static readonly Action<TextBlock> WrapSetter =
        static tb => tb.TextWrapping = TextWrapping.NoWrap;

    private static readonly string[] Texts =
        Enumerable.Range(0, TreeSize).Select(static i => "row-" + i).ToArray();

    // Build one leaf through the deep fluent cascade. Layout-bucket modifiers
    // (.Padding/.Margin/.Width/.Height) + a visual-bucket modifier (.Foreground)
    // are exactly the chained steps #665 reroutes off the throwaway-ElementModifiers
    // path; FontSize/Bold/Opacity round out the cascade and the .Set hits the
    // Setters-array arm.
    private static TextBlockElement BuildCell(int index, bool mutated)
    {
        var text = mutated ? "row-*" : Texts[index];
        return TextBlock(text)
            .FontSize(14)
            .Bold()
            .Foreground(CellBrush)
            .Padding(4, 2)
            .Margin(2, 1)
            .Width(120)
            .Height(20)
            .Opacity(0.99)
            .Set(WrapSetter);
    }

    private static void ApplyDirect(TextBlock tb, string text)
    {
        // Imperative floor: the same property writes a hand-coded (no-Reactor) view
        // would do, so the Direct variant is a fair lower bound for the cascade.
        tb.Text = text;
        tb.FontSize = 14;
        tb.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        tb.Foreground = CellBrush;
        tb.Padding = new Thickness(4, 2, 4, 2);
        tb.Margin = new Thickness(2, 1, 2, 1);
        tb.Width = 120;
        tb.Height = 20;
        tb.Opacity = 0.99;
        tb.TextWrapping = TextWrapping.NoWrap;
    }

    private sealed class Fixture
    {
        // Reactor variants thread the prior element tree so each iteration diffs
        // fresh-vs-prior; Direct has no element tree (null) and re-applies props.
        private readonly TextBlockElement[]? _elements;
        private readonly UIElement[] _controls;

        public Fixture(BenchContext ctx, BenchVariant variant)
        {
            _controls = new UIElement[TreeSize];
            if (variant == BenchVariant.Direct)
            {
                for (int i = 0; i < TreeSize; i++)
                {
                    var tb = new TextBlock();
                    ApplyDirect(tb, Texts[i]);
                    _controls[i] = tb;
                    ctx.Parent.Children.Add(tb);
                }
            }
            else
            {
                _elements = new TextBlockElement[TreeSize];
                for (int i = 0; i < TreeSize; i++)
                {
                    var el = BuildCell(i, mutated: false);
                    _elements[i] = el;
                    var ui = ctx.Reconciler.Mount(el, NoOp);
                    if (ui is not null) { _controls[i] = ui; ctx.Parent.Children.Add(ui); }
                }
            }
        }

        public void RebuildAndReconcile(BenchContext ctx)
        {
            var elements = _elements!;
            for (int i = 0; i < TreeSize; i++)
            {
                bool mutated = ((i + ctx.Iteration) % MutateStride) == 0;
                var fresh = BuildCell(i, mutated);
                ctx.Reconciler.UpdateChild(elements[i], fresh, _controls[i], NoOp);
                elements[i] = fresh;
            }
        }

        public void RebuildDirect(BenchContext ctx)
        {
            for (int i = 0; i < TreeSize; i++)
            {
                bool mutated = ((i + ctx.Iteration) % MutateStride) == 0;
                if (_controls[i] is TextBlock tb)
                    ApplyDirect(tb, mutated ? "row-*" : Texts[i]);
            }
        }
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
        new OptionalElementAllocBench(),
        new OptionalReconcilerUpdateBench(),
        new C207_ChangeHandlerDpRead(),
        new M14_DslRebuildCascade(),
    };
}
