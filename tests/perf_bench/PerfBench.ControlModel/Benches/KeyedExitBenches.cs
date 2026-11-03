// Keyed child reconciliation while exits are retained or children mount nothing.
// Each iteration re-renders a 500-child keyed StackPanel with fresh records of the same
// content. A Border compares its child by reference, so every row takes the update path
// (one control read and a property diff each, no mount); the benches measure what the
// keyed pass adds on top of that — exit detection and the logical-to-physical map.
// An exit started during setup stays in flight for the whole rep: its completion callback
// needs the dispatcher, which the bench loop holds.
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Animation;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PerfBench.ControlModel.Variants;
using static Microsoft.UI.Reactor.Factories;

namespace PerfBench.ControlModel.Benches;

public abstract class KeyedExitBenchBase : IBench
{
    protected const int N = 500;

    public abstract string Id { get; }
    public abstract string Name { get; }

    protected virtual bool UnrelatedExit => false;
    protected virtual bool LocalExit => false;
    protected virtual Element? Leading => null;

    private Element[]? _a, _b;

    public void RunOne(BenchVariant variant, BenchContext ctx)
    {
        _a ??= Children(Leading);
        _b ??= Children(Leading);

        if (ctx.Scratch is not Fixture f)
        {
            var rec = ctx.Reconciler;
            if (UnrelatedExit)
            {
                var otherInitial = VStack(Leaving("other"));
                var other = (StackPanel)rec.Mount(otherInitial, NoOp)!;
                ctx.Parent.Children.Add(other);
                rec.UpdateChild(otherInitial, VStack(), other, NoOp);
                if (other.Children.Count != 1)
                    throw new InvalidOperationException("unrelated exit did not stay in flight");
            }

            Element initial = LocalExit ? VStack([Leaving("local"), .. _a]) : VStack(_a);
            var panel = (StackPanel)rec.Mount(initial, NoOp)!;
            ctx.Parent.Children.Add(panel);
            Element current = initial;
            if (LocalExit)
            {
                current = VStack(_a);
                rec.UpdateChild(initial, current, panel, NoOp);
            }

            int expected = N + (LocalExit ? 1 : 0);
            if (panel.Children.Count != expected)
                throw new InvalidOperationException($"setup expected {expected} realized children, found {panel.Children.Count}");
            ctx.Scratch = new Fixture { Element = current, Panel = panel };
            return;
        }

        // Setup rendered _a, so odd iterations switch to _b. The Margin toggle keeps the host
        // from short-circuiting, matching IH1.
        var next = VStack((ctx.Iteration & 1) == 1 ? _b : _a).Margin(ctx.Iteration & 1);
        ctx.Reconciler.UpdateChild(f.Element, next, f.Panel, NoOp);
        f.Element = next;
    }

    private static Element Leaving(string key) =>
        Border(TextBlock(key)).WithKey(key).Transition(Transition.Fade, Curve.Ease(600_000));

    private static Element[] Children(Element? leading)
    {
        int offset = leading is null ? 0 : 1;
        var a = new Element[N + offset];
        if (leading is not null) a[0] = leading;
        for (int i = 0; i < N; i++)
        {
            var key = "row" + i;
            a[i + offset] = Border(TextBlock(key)).WithKey(key);
        }
        return a;
    }

    private sealed class Fixture
    {
        public Element Element = null!;
        public StackPanel Panel = null!;
    }

    private static readonly Action NoOp = static () => { };
}

/// <summary>Control: 500 keyed children, no exits anywhere, no null-mounted children.</summary>
public sealed class KX1_KeyedNoExit : KeyedExitBenchBase
{
    public override string Id => "KX1";
    public override string Name => "Keyed500_NoExit_Unchanged";
}

/// <summary>500 keyed children while a different panel on the same reconciler holds an exit.</summary>
public sealed class KX2_KeyedUnrelatedExit : KeyedExitBenchBase
{
    public override string Id => "KX2";
    public override string Name => "Keyed500_UnrelatedPanelExit_Unchanged";
    protected override bool UnrelatedExit => true;
}

/// <summary>500 keyed children while this panel retains one exiting control.</summary>
public sealed class KX3_KeyedLocalExit : KeyedExitBenchBase
{
    public override string Id => "KX3";
    public override string Name => "Keyed500_LocalExit_Unchanged";
    protected override bool LocalExit => true;
}

/// <summary>500 keyed children plus a leading keyed Memo that mounts nothing; no exits.</summary>
public sealed class KX4_KeyedEmptyMemo : KeyedExitBenchBase
{
    public override string Id => "KX4";
    public override string Name => "Keyed500_EmptyMemo_Unchanged";
    private static readonly Element EmptyMemo = Memo("empty", () => Empty()).WithKey("empty");
    protected override Element? Leading => EmptyMemo;
}
