using Microsoft.UI.Reactor.Core.Internal;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Internal;

/// <summary>
/// Tests for the internal collection-delta state container used by Phase 1
/// of spec 042. Validates the Reset helper used by mount and bulk-replace
/// bailout, plus the cardinality invariants the diff algorithm depends on.
/// </summary>
public class ReactorListStateTests
{
    [Fact]
    public void Reset_Empty_Leaves_All_Collections_Empty()
    {
        var state = new ReactorListState();
        state.Reset([]);

        Assert.Empty(state.Source);
        Assert.Empty(state.ByKey);
        Assert.Empty(state.LastKeys);
    }

    [Fact]
    public void Reset_Populates_All_Three_Collections_With_Matching_Counts()
    {
        var state = new ReactorListState();
        state.Reset([(0, "a"), (1, "b"), (2, "c")]);

        Assert.Equal(3, state.Source.Count);
        Assert.Equal(3, state.ByKey.Count);
        Assert.Equal(3, state.LastKeys.Count);
    }

    [Fact]
    public void Reset_Assigns_Indices_Positionally_Ignoring_Input_Index()
    {
        var state = new ReactorListState();
        // Caller passes nonsensical input indices — Reset must overwrite them
        // with the row's actual position so callers don't have to pre-sort
        // or normalize.
        state.Reset([(99, "a"), (-1, "b"), (50, "c")]);

        Assert.Equal(0, state.Source[0].Index);
        Assert.Equal(1, state.Source[1].Index);
        Assert.Equal(2, state.Source[2].Index);
    }

    [Fact]
    public void Reset_LastKeys_Preserves_Input_Order()
    {
        var state = new ReactorListState();
        state.Reset([(0, "alpha"), (1, "beta"), (2, "gamma")]);

        Assert.Equal(["alpha", "beta", "gamma"], state.LastKeys);
    }

    [Fact]
    public void Reset_ByKey_Maps_Each_Key_To_Its_Row()
    {
        var state = new ReactorListState();
        state.Reset([(0, "a"), (1, "b")]);

        Assert.Same(state.Source[0], state.ByKey["a"]);
        Assert.Same(state.Source[1], state.ByKey["b"]);
    }

    [Fact]
    public void Reset_Twice_Replaces_Previous_State()
    {
        var state = new ReactorListState();
        state.Reset([(0, "a"), (1, "b"), (2, "c")]);
        state.Reset([(0, "x"), (1, "y")]);

        Assert.Equal(2, state.Source.Count);
        Assert.Equal(["x", "y"], state.LastKeys);
        Assert.True(state.ByKey.ContainsKey("x"));
        Assert.True(state.ByKey.ContainsKey("y"));
        Assert.False(state.ByKey.ContainsKey("a"));
        Assert.False(state.ByKey.ContainsKey("b"));
        Assert.False(state.ByKey.ContainsKey("c"));
    }

    [Fact]
    public void Reset_With_Duplicate_Keys_Keeps_First_Row_In_ByKey()
    {
        var state = new ReactorListState();
        // The bulk-replace bailout calls Reset with the user's current items;
        // if those keys collide, Reset must remain tolerant rather than throw.
        // The diagnostic surfaces at the bailout call site; Reset's job is to
        // produce *some* coherent state so subsequent diffs can recover.
        state.Reset([(0, "dup"), (1, "dup"), (2, "ok")]);

        Assert.Equal(3, state.Source.Count);
        Assert.Equal(3, state.LastKeys.Count);
        // ByKey has only two entries because "dup" collapses; the first
        // occurrence wins (canonical lookup target).
        Assert.Equal(2, state.ByKey.Count);
        Assert.Same(state.Source[0], state.ByKey["dup"]);
    }

    [Fact]
    public void Source_Is_Observable_Collection_Raises_CollectionChanged_On_Add()
    {
        var state = new ReactorListState();
        int adds = 0;
        state.Source.CollectionChanged += (_, args) =>
        {
            if (args.Action == global::System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                adds++;
        };

        state.Source.Add(new ReactorRow { Index = 0, Key = "a" });
        state.Source.Add(new ReactorRow { Index = 1, Key = "b" });

        Assert.Equal(2, adds);
    }

    [Fact]
    public void Source_Move_Raises_Move_Action_Not_Insert_Plus_Remove()
    {
        // This is the WinUI contract — Move on the OC produces a single Move
        // event, not a remove + insert. WinUI's ListView animation reads this
        // event as RepositionThemeTransition; if we re-emit it as Remove/Insert
        // the visual breaks.
        var state = new ReactorListState();
        state.Source.Add(new ReactorRow { Index = 0, Key = "a" });
        state.Source.Add(new ReactorRow { Index = 1, Key = "b" });
        state.Source.Add(new ReactorRow { Index = 2, Key = "c" });

        global::System.Collections.Specialized.NotifyCollectionChangedAction? lastAction = null;
        state.Source.CollectionChanged += (_, args) => lastAction = args.Action;

        state.Source.Move(0, 2);

        Assert.Equal(global::System.Collections.Specialized.NotifyCollectionChangedAction.Move, lastAction);
    }

    [Fact]
    public void Row_Identity_Survives_Move()
    {
        // The whole point of using a reference-typed ReactorRow is that WinUI
        // sees the same object identity before and after a Move. Pin that here.
        var state = new ReactorListState();
        var rowA = new ReactorRow { Index = 0, Key = "a" };
        var rowB = new ReactorRow { Index = 1, Key = "b" };
        state.Source.Add(rowA);
        state.Source.Add(rowB);

        state.Source.Move(0, 1);

        Assert.Same(rowA, state.Source[1]);
        Assert.Same(rowB, state.Source[0]);
    }

    [Fact]
    public void Row_ToString_Includes_Index_And_Key_For_Diagnostics()
    {
        var row = new ReactorRow { Index = 7, Key = "hello" };
        var text = row.ToString();
        Assert.Contains("7", text);
        Assert.Contains("hello", text);
    }

    [Fact]
    public void Scratch_Dictionary_Is_Reusable_And_Initially_Empty()
    {
        var state = new ReactorListState();
        Assert.Empty(state.Scratch);
        state.Scratch["x"] = new ReactorRow();
        Assert.Single(state.Scratch);
        state.Scratch.Clear();
        Assert.Empty(state.Scratch);
    }

    [Fact]
    public void ByKey_Uses_Ordinal_String_Comparison()
    {
        // Keys come from user data via KeySelector and may legitimately
        // differ by case (e.g. database IDs that distinguish "A" and "a").
        // Ordinal comparison is required so the diff doesn't accidentally
        // merge logically-distinct rows.
        var state = new ReactorListState();
        state.Reset([(0, "A"), (1, "a")]);

        Assert.True(state.ByKey.ContainsKey("A"));
        Assert.True(state.ByKey.ContainsKey("a"));
        Assert.NotSame(state.ByKey["A"], state.ByKey["a"]);
    }
}
