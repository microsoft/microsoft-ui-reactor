using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Coverage for the keyed prefix/suffix <c>CanSkipUpdate</c> fast-path added by
/// issue #653 (fix #30). A stable keyed list — e.g. grid rows whose keys never
/// change between renders — must short-circuit the per-row <c>UpdateChild</c>
/// COM round-trip exactly like the positional path already does, instead of
/// re-diffing every row on every tick. Before #30 the keyed prefix/suffix loops
/// always called <c>UpdateChild</c>; these tests pin the new skip behavior so a
/// regression that re-introduces the per-row work (or, conversely, wrongly skips
/// a row that needed updating) is caught.
///
/// These exercise the callback-free path, where the skip must avoid touching the
/// realized control collection entirely — the mock's <c>Get</c> throws, so a
/// passing test proves no <c>children.Get</c> COM call happened.
/// </summary>
public class ChildReconcilerKeyedSkipTests
{
    private static readonly Action NoOp = () => { };

    /// <summary>
    /// Records structural ops and counts <see cref="Get"/> calls; <see cref="Get"/>
    /// throws because the #30 skip for callback-free rows must never reach a
    /// realized control (creating one would also throw COMException headless).
    /// </summary>
    private sealed class ThrowingGetChildCollection : IChildCollection
    {
        private int _count;
        public List<string> Operations { get; } = new();
        public int GetCalls { get; private set; }

        public ThrowingGetChildCollection(int count) => _count = count;

        public int Count => _count;

        public UIElement Get(int index)
        {
            GetCalls++;
            throw new InvalidOperationException(
                $"Get({index}) must not be called when a keyed row is skipped via CanSkipUpdate.");
        }

        public void Insert(int index, UIElement element) { Operations.Add($"Insert({index})"); _count++; }
        public void RemoveAt(int index) { Operations.Add($"RemoveAt({index})"); _count--; }
        public void Move(int oldIndex, int newIndex) => Operations.Add($"Move({oldIndex},{newIndex})");
        public void Replace(int index, UIElement element) => Operations.Add($"Replace({index})");
    }

    // Distinct element instances each call (not reference-equal) with identical
    // key + content, so the skip is driven by structural CanSkipUpdate — the
    // realistic re-render shape, where every render allocates fresh records.
    private static Element[] KeyedRows(int count)
    {
        var rows = new Element[count];
        for (int i = 0; i < count; i++)
            rows[i] = new TextBlockElement($"row-{i}") { Key = $"k{i}" };
        return rows;
    }

    [Theory]
    [InlineData(1)]   // single-row boundary (prefixLen < childCount guard)
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(32)]  // steady-state grid: many stable rows
    public void Identical_Keyed_List_Skips_Every_Row_With_No_Ops_And_No_ControlAccess(int count)
    {
        var oldRows = KeyedRows(count);
        var newRows = KeyedRows(count);
        var mock = new ThrowingGetChildCollection(count);
        var reconciler = new Reconciler();

        ChildReconciler.Reconcile(oldRows, newRows, mock, reconciler, NoOp);

        Assert.Empty(mock.Operations);                    // no insert/move/remove/replace
        Assert.Equal(0, mock.GetCalls);                   // callback-free rows never touch controls
        Assert.Equal(count, reconciler.DebugElementsSkipped); // every row hit the #30 fast-path
    }

    [Fact]
    public void Repeated_Reconcile_Of_Stable_Keyed_List_Keeps_Skipping()
    {
        // Simulates several frames of a grid whose keys/content are unchanged:
        // each pass must skip all rows and never fall back to UpdateChild.
        var mock = new ThrowingGetChildCollection(8);
        var reconciler = new Reconciler();
        var previous = KeyedRows(8);

        for (int frame = 0; frame < 4; frame++)
        {
            var next = KeyedRows(8);
            ChildReconciler.Reconcile(previous, next, mock, reconciler, NoOp);
            previous = next;
        }

        Assert.Empty(mock.Operations);
        Assert.Equal(0, mock.GetCalls);
        Assert.Equal(8 * 4, reconciler.DebugElementsSkipped);
    }
}
