using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Correctness oracle for <see cref="ChildReconciler.RunKeyedMiddleCore{TSink}"/>
/// — the keyed-middle reorder algorithm that drives which surviving rows move /
/// insert / patch.
///
/// Historically this path was "blocked by the selftest wall" (see
/// <c>ChildReconcilerReconcileTests.cs</c>): exercising the real
/// <c>ReconcileKeyedMiddle</c> needs live WinUI controls because
/// <c>new Border()</c> throws COMException headless. The fix for the keyed
/// identity bug (C1) / stale-index bug (H1) extracted the pure index logic into
/// <see cref="ChildReconciler.RunKeyedMiddleCore{TSink}"/> behind an
/// <see cref="ChildReconciler.IKeyedMiddleSink"/> seam. Production drives it with
/// a real-WinUI sink; this test drives the SAME method with a headless
/// <see cref="SimKeyedMiddleSink"/> over a list of identity cells — so the
/// reorder stepping logic is now unit-testable with exact production parity.
///
/// Each cell tracks BOTH which original control occupies a slot (identity) and
/// which new item's content it was last patched with. The oracle therefore
/// asserts the two invariants the bugs violated:
///   • final ORDER + per-key IDENTITY (the right control ends in the right slot);
///   • per-slot CONTENT (the right control is PATCHED with the right new item —
///     C1 patched survivors by a not-yet-reached final index, landing content on
///     the wrong control).
/// </summary>
public class ChildReconcilerKeyedMiddleCoreTests
{
    // Token spaces keep the four cell kinds distinguishable in assertions.
    private const int NewBase = 1000;    // new (mounted) item j  -> NewBase + j
    private const int PrefixBase = 2000; // untouched common-prefix slot k
    private const int SuffixBase = 3000; // untouched common-suffix slot k
    private const int Unpatched = -1;    // survivor not yet patched this pass

    private struct Cell
    {
        public int Control; // identity: old-relative index, or NewBase+j for new
        public int Content; // last new-item index patched in (-1 if none)
    }

    private readonly record struct PatchRecord(int OldRel, int NewIdx, int PanelIdx, int ControlAtPatch);

    /// <summary>
    /// Headless <see cref="ChildReconciler.IKeyedMiddleSink"/> over a
    /// <see cref="List{Cell}"/>. Mirrors the real sink's structural mutations
    /// (Insert / final-position Move / patch-in-place) so it steps through the
    /// identical core decisions.
    /// </summary>
    private struct SimKeyedMiddleSink : ChildReconciler.IKeyedMiddleSink
    {
        public List<Cell> Cells;
        public List<PatchRecord> Patches;
        public List<(int From, int To)> Moves;

        public bool MountInsert(int newIdx, int panelIdx)
        {
            Cells.Insert(panelIdx, new Cell { Control = NewBase + newIdx, Content = newIdx });
            return true;
        }

        public void MoveExisting(int fromIdx, int toIdx)
        {
            var cell = Cells[fromIdx];
            Cells.RemoveAt(fromIdx);     // final-position semantics: remove then
            Cells.Insert(toIdx, cell);   // insert into the post-removal list
            Moves.Add((fromIdx, toIdx));
        }

        public void Patch(int oldRelIdx, int newIdx, int panelIdx)
        {
            int controlAtPatch = panelIdx >= 0 && panelIdx < Cells.Count ? Cells[panelIdx].Control : int.MinValue;
            Patches.Add(new PatchRecord(oldRelIdx, newIdx, panelIdx, controlAtPatch));
            if (panelIdx >= 0 && panelIdx < Cells.Count)
            {
                var cell = Cells[panelIdx];
                cell.Content = newIdx;
                Cells[panelIdx] = cell;
            }
        }
    }

    private sealed class RunResult
    {
        public required List<Cell> Cells;
        public required List<PatchRecord> Patches;
        public required List<(int From, int To)> Moves;
        public required int[] NewToOld;
        public required int PrefixLen;
        public required int SuffixLen;
        public required int NewMidLen;
    }

    private static string[] Split(string csv) =>
        csv.Split(',', global::System.StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Build the post-Step-1 model exactly as production does — last-wins key
    /// map, matched survivors compacted in OLD order, LIS via the real
    /// <see cref="ChildReconciler.ComputeLISInto"/> — then run the REAL core.
    /// </summary>
    private static RunResult Run(string[] oldKeys, string[] newKeys, int prefixLen = 0, int suffixLen = 0)
    {
        int oldMidLen = oldKeys.Length;
        int newMidLen = newKeys.Length;

        // newToOld + matched (production semantics: last key wins).
        var oldKeyMap = new Dictionary<string, int>();
        for (int i = 0; i < oldMidLen; i++) oldKeyMap[oldKeys[i]] = i;

        var newToOld = new int[newMidLen];
        var matched = new bool[oldMidLen];
        for (int j = 0; j < newMidLen; j++)
        {
            if (oldKeyMap.TryGetValue(newKeys[j], out int r))
            {
                newToOld[j] = r;
                matched[r] = true;
            }
            else
            {
                newToOld[j] = -1;
            }
        }

        var inLis = new bool[newMidLen];
        ChildReconciler.ComputeLISInto(newToOld, newMidLen, inLis);

        // Survivors compacted in OLD order → live positions after removal.
        var oldRelToPanel = new int[oldMidLen];
        for (int r = 0; r < oldMidLen; r++) oldRelToPanel[r] = -1;

        var cells = new List<Cell>();
        for (int k = 0; k < prefixLen; k++) cells.Add(new Cell { Control = PrefixBase + k, Content = -2 });
        int compact = 0;
        for (int r = 0; r < oldMidLen; r++)
        {
            if (matched[r])
            {
                oldRelToPanel[r] = prefixLen + compact;
                cells.Add(new Cell { Control = r, Content = Unpatched });
                compact++;
            }
        }
        int initialAnchor = prefixLen + compact; // start of suffix / end of middle
        for (int k = 0; k < suffixLen; k++) cells.Add(new Cell { Control = SuffixBase + k, Content = -2 });

        var sink = new SimKeyedMiddleSink
        {
            Cells = cells,
            Patches = new List<PatchRecord>(),
            Moves = new List<(int, int)>(),
        };

        ChildReconciler.RunKeyedMiddleCore(
            ref sink, newToOld, inLis, newMidLen, oldMidLen, initialAnchor, oldRelToPanel);

        return new RunResult
        {
            Cells = sink.Cells,
            Patches = sink.Patches,
            Moves = sink.Moves,
            NewToOld = newToOld,
            PrefixLen = prefixLen,
            SuffixLen = suffixLen,
            NewMidLen = newMidLen,
        };
    }

    private static int ExpectedControl(int[] newToOld, int j) =>
        newToOld[j] >= 0 ? newToOld[j] : NewBase + j;

    /// <summary>Assert all four correctness invariants for a unique-key reorder.</summary>
    private static void AssertReconciled(string[] oldKeys, string[] newKeys, int prefixLen = 0, int suffixLen = 0)
    {
        var r = Run(oldKeys, newKeys, prefixLen, suffixLen);

        // (1) Collection length is exactly prefix + new-middle + suffix.
        Assert.Equal(prefixLen + r.NewMidLen + suffixLen, r.Cells.Count);

        // (2) Common prefix/suffix slots are never disturbed.
        for (int k = 0; k < prefixLen; k++)
            Assert.Equal(PrefixBase + k, r.Cells[k].Control);
        for (int k = 0; k < suffixLen; k++)
            Assert.Equal(SuffixBase + k, r.Cells[prefixLen + r.NewMidLen + k].Control);

        // (3) Each middle slot holds the RIGHT control (identity/order) showing
        //     the RIGHT content (the j-th new item). Content is the C1 teeth.
        for (int j = 0; j < r.NewMidLen; j++)
        {
            var cell = r.Cells[prefixLen + j];
            Assert.Equal(ExpectedControl(r.NewToOld, j), cell.Control);
            Assert.Equal(j, cell.Content);
        }

        // (4) Every patch landed on the survivor it claimed — never the wrong
        //     control at a not-yet-rearranged index (the direct C1/H1 check).
        foreach (var p in r.Patches)
            Assert.Equal(p.OldRel, p.ControlAtPatch);
    }

    [Theory]
    [InlineData("", "")]                          // empty → empty
    [InlineData("", "a,b")]                        // empty → non-empty (all new)
    [InlineData("a,b", "")]                        // non-empty → empty (all removed)
    [InlineData("a,b,c", "a,b,c")]                 // no-op (every survivor in LIS)
    [InlineData("a,b,c", "a,b,c,d")]               // append
    [InlineData("a,b,c", "d,a,b,c")]               // prepend
    [InlineData("a,b,c", "a,d,b,c")]               // insert middle
    [InlineData("a,b,c", "a,d,e,b,c")]             // insert two middle
    [InlineData("a,b,c,d", "a,c,d")]               // remove middle
    [InlineData("a,b,c,d", "b,d")]                 // remove two
    [InlineData("a,b,c,d", "a,c,b,d")]             // adjacent swap
    [InlineData("a,b,c,d", "b,c,d,a")]             // ROTATION (C1 repro)
    [InlineData("a,b,c,d", "d,a,b,c")]             // rotation other way
    [InlineData("a,b,c,d", "d,c,b,a")]             // full reverse
    [InlineData("a,b,c,d,e", "e,d,c,b,a")]         // reverse 5
    [InlineData("a,b,c", "c,a,b")]                 // all moved (rotate)
    [InlineData("a,b,c,d,e,f", "f,a,e,b,d,c")]     // scramble
    [InlineData("a,b,c,d", "c,d,a,b")]             // block swap
    [InlineData("a,b,c,d", "e,b,c,f")]             // replace ends, keep middle
    [InlineData("a,b,c,d", "d,x,b,y,a")]           // moves + inserts mixed
    public void RunKeyedMiddleCore_PreservesOrderIdentityAndContent(string oldCsv, string newCsv)
    {
        AssertReconciled(Split(oldCsv), Split(newCsv));
    }

    [Theory]
    [InlineData("a,b,c,d", "b,c,d,a")]             // rotation, offset
    [InlineData("a,b,c,d", "d,c,b,a")]             // reverse, offset
    [InlineData("a,b,c", "a,d,b,c")]               // insert middle, offset
    [InlineData("a,b,c", "x,y")]                   // full replace, offset
    public void RunKeyedMiddleCore_WithPrefixAndSuffix_OnlyTouchesMiddle(string oldCsv, string newCsv)
    {
        AssertReconciled(Split(oldCsv), Split(newCsv), prefixLen: 2, suffixLen: 3);
    }

    [Fact]
    public void Rotation_RealCore_PutsBFirst_AndPatchesEachControlWithItsOwnContent()
    {
        // [a,b,c,d] → [b,c,d,a]: the exact C1 repro. Post-fix, slot 0 holds the
        // 'b' control (old-relative index 1) showing the new 'b' content (0).
        var r = Run(Split("a,b,c,d"), Split("b,c,d,a"));

        Assert.Equal(new[] { 1, 2, 3, 0 }, r.Cells.ConvertAll(c => c.Control).ToArray());
        Assert.Equal(new[] { 0, 1, 2, 3 }, r.Cells.ConvertAll(c => c.Content).ToArray());
        Assert.All(r.Patches, p => Assert.Equal(p.OldRel, p.ControlAtPatch));
        // Minimal moves: only the single non-LIS survivor ('a') relocates.
        Assert.Single(r.Moves);
    }

    [Fact]
    public void Rotation_BuggyPreFixAlgorithm_MisPatchesControls_ProvingTheTestHasTeeth()
    {
        // Faithful reproduction of the PRE-FIX Step-2 loop: left-to-right,
        // survivors patched at their FINAL index (prefixLen + i) against a panel
        // that is only partially rearranged, with keyToIndex updated only for the
        // moved key (never shifted on insert). This is exactly the C1/H1 logic.
        var buggy = RunBuggyPreFix(Split("a,b,c,d"), Split("b,c,d,a"));

        // The buggy walk lands at least one patch on the WRONG control and
        // produces corrupted per-slot content — both of which AssertReconciled
        // checks, so the rotation test genuinely has teeth.
        Assert.Contains(buggy.Patches, p => p.OldRel != p.ControlAtPatch);
        var content = buggy.Cells.ConvertAll(c => c.Content).ToArray();
        Assert.NotEqual(new[] { 0, 1, 2, 3 }, content);
        // Documented symptom: slot 0 shows new item 'c' (index 1), not 'b' (0).
        Assert.Equal(1, content[0]);

        // And the REAL core never mis-patches on the same input.
        var real = Run(Split("a,b,c,d"), Split("b,c,d,a"));
        Assert.All(real.Patches, p => Assert.Equal(p.OldRel, p.ControlAtPatch));
    }

    [Fact]
    public void DuplicateNewKeys_DoNotThrow_AndNeverPatchANonSurvivor()
    {
        // Duplicate keys are ill-defined (keys must be unique), but the engine
        // must degrade gracefully: no exception, and every patch still lands on
        // a genuine surviving control (no wrong-control corruption).
        var ex = Record.Exception(() =>
        {
            var r = Run(Split("a"), Split("a,a"));
            Assert.All(r.Patches, p => Assert.Equal(p.OldRel, p.ControlAtPatch));
        });
        Assert.Null(ex);

        var ex2 = Record.Exception(() =>
        {
            var r = Run(Split("a,b"), Split("b,b,a"));
            Assert.All(r.Patches, p => Assert.Equal(p.OldRel, p.ControlAtPatch));
        });
        Assert.Null(ex2);
    }

    [Fact]
    public void DuplicateOldKeys_EarlierDuplicateIsTreatedAsRemoved()
    {
        // Last-wins key map: with old [a,a,b] → new [a,b], the new 'a' resolves
        // to the LAST old 'a'; the first 'a' is unmatched (removed in Step 1) and
        // never appears as a survivor. The two new slots reconcile cleanly.
        var r = Run(Split("a,a,b"), Split("a,b"));
        Assert.All(r.Patches, p => Assert.Equal(p.OldRel, p.ControlAtPatch));
        // Both surviving controls show their new content.
        Assert.Equal(new[] { 0, 1 }, r.Cells.ConvertAll(c => c.Content).ToArray());
    }

    // ── Faithful pre-fix (buggy) reproduction — used only to prove teeth ──────
    private RunResult RunBuggyPreFix(string[] oldKeys, string[] newKeys, int prefixLen = 0, int suffixLen = 0)
    {
        int oldMidLen = oldKeys.Length;
        int newMidLen = newKeys.Length;

        var oldKeyMap = new Dictionary<string, int>();
        for (int i = 0; i < oldMidLen; i++) oldKeyMap[oldKeys[i]] = i;

        var newToOld = new int[newMidLen];
        var matched = new bool[oldMidLen];
        for (int j = 0; j < newMidLen; j++)
        {
            if (oldKeyMap.TryGetValue(newKeys[j], out int rr)) { newToOld[j] = rr; matched[rr] = true; }
            else newToOld[j] = -1;
        }

        var inLis = new bool[newMidLen];
        ChildReconciler.ComputeLISInto(newToOld, newMidLen, inLis);

        var cells = new List<Cell>();
        for (int k = 0; k < prefixLen; k++) cells.Add(new Cell { Control = PrefixBase + k, Content = -2 });
        for (int r = 0; r < oldMidLen; r++)
            if (matched[r]) cells.Add(new Cell { Control = r, Content = Unpatched });
        for (int k = 0; k < suffixLen; k++) cells.Add(new Cell { Control = SuffixBase + k, Content = -2 });

        // keyToIndex built ONCE from the post-removal panel (key → first index),
        // and only the moved key is rewritten — exactly the stale-index (H1) bug.
        var keyToIndex = new Dictionary<string, int>();
        for (int i = prefixLen; i < cells.Count - suffixLen; i++)
        {
            int ctrl = cells[i].Control;
            if (ctrl >= 0 && ctrl < oldMidLen) keyToIndex.TryAdd(oldKeys[ctrl], i);
        }

        var patches = new List<PatchRecord>();
        var moves = new List<(int, int)>();

        void PatchAt(int oldRel, int newIdx, int target)
        {
            if (target < 0 || target >= cells.Count) return;
            patches.Add(new PatchRecord(oldRel, newIdx, target, cells[target].Control));
            var cell = cells[target];
            cell.Content = newIdx;
            cells[target] = cell;
        }

        // Pre-fix Step 2: left-to-right, patch survivor at the FINAL index.
        for (int i = 0; i < newMidLen; i++)
        {
            int target = prefixLen + i;
            if (newToOld[i] == -1)
            {
                cells.Insert(target, new Cell { Control = NewBase + i, Content = i });
            }
            else if (inLis[i])
            {
                PatchAt(newToOld[i], i, target); // BUG C1: control at `target` is wrong
            }
            else
            {
                var key = oldKeys[newToOld[i]];
                int cur = keyToIndex.TryGetValue(key, out int pos) ? pos : -1;
                if (cur >= 0 && cur != target)
                {
                    var cell = cells[cur];
                    cells.RemoveAt(cur);
                    cells.Insert(target, cell);
                    keyToIndex[key] = target; // H1: only the moved key is updated
                    moves.Add((cur, target));
                }
                PatchAt(newToOld[i], i, target);
            }
        }

        return new RunResult
        {
            Cells = cells,
            Patches = patches,
            Moves = moves,
            NewToOld = newToOld,
            PrefixLen = prefixLen,
            SuffixLen = suffixLen,
            NewMidLen = newMidLen,
        };
    }
}
