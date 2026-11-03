// ItemsHost update benchmarks: ComboBox Element and raw-string items.
// Covers unchanged sources, equal sources and single-item edits. Each iteration re-renders
// the host with a fresh parent element so the handler's Update path always runs.
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using PerfBench.ControlModel.Variants;
using static Microsoft.UI.Reactor.Factories;

namespace PerfBench.ControlModel.Benches;

public abstract class ItemsHostBenchBase : IBench
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    protected abstract Element Build(int iteration);

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        if (ctx.Scratch is not Fixture f)
        {
            var el = Build(0);
            var ui = ctx.Reconciler.Mount(el, NoOp)!;
            ctx.Parent.Children.Add(ui);
            ctx.Scratch = new Fixture { Element = el, Control = ui };
            return;
        }
        var next = Build(ctx.Iteration + 1);
        ctx.Reconciler.UpdateChild(f.Element, next, f.Control, NoOp);
        f.Element = next;
    }

    private sealed class Fixture
    {
        public Element Element = null!;
        public UIElement Control = null!;
    }

    protected static Element[] TextItems(int n, int changedIndex = -1)
    {
        var a = new Element[n];
        for (int i = 0; i < n; i++)
            a[i] = TextBlock(i == changedIndex ? "changed" : "item " + i);
        return a;
    }

    protected static string[] StringItems(int n, int changedIndex = -1)
    {
        var a = new string[n];
        for (int i = 0; i < n; i++)
            a[i] = i == changedIndex ? "changed" : "item " + i;
        return a;
    }

    private static readonly Action NoOp = static () => { };
}

/// <summary>ComboBox ItemElements, 100 Element items, same array every render; only a host property
/// (Margin) toggles, so the host updates but its list is untouched.</summary>
public sealed class IH1_ComboBoxElementsSameArray : ItemsHostBenchBase
{
    public override string Id => "IH1";
    public override string Name => "ComboBoxEl100_SameItemsArray";
    private static readonly Element[] Items = TextItems(100);
    protected override Element Build(int iteration) => ComboBox(Items, default, null).Margin(iteration & 1);
}

/// <summary>ComboBox ItemElements, 100 Element items, fresh-but-equal element instances every render (typical Render()).</summary>
public sealed class IH2_ComboBoxElementsEqualFresh : ItemsHostBenchBase
{
    public override string Id => "IH2";
    public override string Name => "ComboBoxEl100_EqualFreshItems";
    private static readonly Element[] A = TextItems(100);
    private static readonly Element[] B = TextItems(100);
    protected override Element Build(int iteration) => ComboBox((iteration & 1) == 0 ? A : B, default, null);
}

/// <summary>ComboBox ItemElements, 100 Element items, one item's text toggles every render.</summary>
public sealed class IH3_ComboBoxElementsOneChanged : ItemsHostBenchBase
{
    public override string Id => "IH3";
    public override string Name => "ComboBoxEl100_OneItemChanged";
    private static readonly Element[] A = TextItems(100);
    private static readonly Element[] B = TextItems(100, changedIndex: 50);
    protected override Element Build(int iteration) => ComboBox((iteration & 1) == 0 ? A : B, default, null);
}

/// <summary>ComboBox, 100 string items, fresh-but-equal arrays every render.</summary>
public sealed class IH4_ComboBoxEqualStrings100 : ItemsHostBenchBase
{
    public override string Id => "IH4";
    public override string Name => "ComboBox100_EqualStrings";
    private static readonly string[] A = StringItems(100);
    private static readonly string[] B = StringItems(100);
    protected override Element Build(int iteration) => ComboBox((iteration & 1) == 0 ? A : B);
}

/// <summary>ComboBox, 1000 string items, fresh-but-equal arrays every render (raw-item matching scale).</summary>
public sealed class IH5_ComboBoxEqualStrings1000 : ItemsHostBenchBase
{
    public override string Id => "IH5";
    public override string Name => "ComboBox1000_EqualStrings";
    private static readonly string[] A = StringItems(1000);
    private static readonly string[] B = StringItems(1000);
    protected override Element Build(int iteration) => ComboBox((iteration & 1) == 0 ? A : B);
}

/// <summary>ComboBox, 100 string items, one string toggles every render.</summary>
public sealed class IH6_ComboBoxOneChanged : ItemsHostBenchBase
{
    public override string Id => "IH6";
    public override string Name => "ComboBox100_OneStringChanged";
    private static readonly string[] A = StringItems(100);
    private static readonly string[] B = StringItems(100, changedIndex: 50);
    protected override Element Build(int iteration) => ComboBox((iteration & 1) == 0 ? A : B);
}
