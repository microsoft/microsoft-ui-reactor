using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Behavior-parity unit tests for the reconcile-path COM-interop + allocation
/// perf fixes (issue: "perf: coalesce COM-interop reads + allocations in
/// reconcile path"). Each optimization replaced a per-element allocation or a
/// repeated COM read with a reused/coalesced equivalent; these tests pin the
/// observable behavior the optimizations must preserve:
///
///   * #24 BuildCacheKey reuses a thread-static scratch buffer + StringBuilder
///     across calls — verify no stale entries leak and the format is stable.
///   * #25 Context&lt;T&gt;.CurrentValueEquals reproduces the previous
///     object.Equals semantics without boxing the current typed value.
///   * #29 ContextScope.Push(context, value) single-pair overload matches the
///     dictionary-based push it replaced.
///   * #62-#67 the refactored accessibility scanner walk emits identical
///     findings (output parity / re-scan determinism).
/// </summary>
public class ReconcilerInteropPerfTests
{
    // ════════════════════════════════════════════════════════════════
    //  #24 — BuildCacheKey thread-static scratch reuse
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildCacheKey_LargeThenSmall_NoStaleTailLeaks()
    {
        var many = new Dictionary<string, ThemeRef>
        {
            { "Foreground", new ThemeRef("Fg") },
            { "Background", new ThemeRef("Bg") },
            { "BorderBrush", new ThemeRef("Border") },
        };
        // Ordinal sort: Background < BorderBrush < Foreground.
        Assert.Equal(
            "Button|Background=Bg|BorderBrush=Border|Foreground=Fg",
            Reconciler.BuildCacheKey("Button", many));

        // A subsequent smaller binding set reuses the same (larger) scratch
        // buffer — the key must not pick up leftovers from the prior call.
        var few = new Dictionary<string, ThemeRef> { { "Background", new ThemeRef("Bg") } };
        Assert.Equal("Button|Background=Bg", Reconciler.BuildCacheKey("Button", few));

        // And an empty set must collapse to just the target type.
        Assert.Equal("Button", Reconciler.BuildCacheKey("Button", new Dictionary<string, ThemeRef>()));
    }

    [Fact]
    public void BuildCacheKey_RepeatedCalls_AreDeterministic()
    {
        var bindings = new Dictionary<string, ThemeRef>
        {
            { "Foreground", new ThemeRef("TextPrimary") },
            { "Background", new ThemeRef("AccentBrush") },
        };

        var expected = "Grid|Background=AccentBrush|Foreground=TextPrimary";
        for (int i = 0; i < 100; i++)
            Assert.Equal(expected, Reconciler.BuildCacheKey("Grid", bindings));
    }

    // ════════════════════════════════════════════════════════════════
    //  #25 — Context<T>.CurrentValueEquals (no boxing of the current value)
    // ════════════════════════════════════════════════════════════════

    private static readonly Context<int> CountCtx = new(0);
    private static readonly Context<string> ThemeCtx = new("light");
    private static readonly Context<string?> SessionCtx = new(defaultValue: null);

    [Fact]
    public void CurrentValueEquals_ValueType_EqualAndDifferent()
    {
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [CountCtx] = 7 });

        Assert.True(CountCtx.CurrentValueEquals(scope, 7));    // unchanged
        Assert.False(CountCtx.CurrentValueEquals(scope, 8));   // changed
        Assert.False(CountCtx.CurrentValueEquals(scope, null)); // current 7 vs null

        scope.Pop(1);
    }

    [Fact]
    public void CurrentValueEquals_Default_ReadsDefaultValue()
    {
        var scope = new ContextScope();
        // No push → reads the context default (0). Matches object.Equals(0, 0).
        Assert.True(CountCtx.CurrentValueEquals(scope, 0));
        Assert.False(CountCtx.CurrentValueEquals(scope, 5));
    }

    [Fact]
    public void CurrentValueEquals_ReferenceType_NullHandling()
    {
        var scope = new ContextScope();

        // Default session value is null → current null.
        Assert.True(SessionCtx.CurrentValueEquals(scope, null));    // null == null
        Assert.False(SessionCtx.CurrentValueEquals(scope, "abc"));  // null vs "abc"

        scope.Push(new Dictionary<ContextBase, object?> { [SessionCtx] = "abc" });
        Assert.True(SessionCtx.CurrentValueEquals(scope, "abc"));   // unchanged
        Assert.False(SessionCtx.CurrentValueEquals(scope, null));   // "abc" vs null
        scope.Pop(1);
    }

    // ════════════════════════════════════════════════════════════════
    //  #29 — ContextScope.Push(context, value) single-pair overload
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void SinglePairPush_ReadsValue_ThenPopRestoresDefault()
    {
        var scope = new ContextScope();
        scope.Push(ThemeCtx, "dark");
        Assert.Equal("dark", scope.Read(ThemeCtx));

        scope.Pop(1);
        Assert.Equal("light", scope.Read(ThemeCtx)); // default
    }

    [Fact]
    public void SinglePairPush_Nested_InnerShadowsOuter()
    {
        var scope = new ContextScope();
        scope.Push(ThemeCtx, "dark");
        scope.Push(ThemeCtx, "high-contrast");

        Assert.Equal("high-contrast", scope.Read(ThemeCtx));
        scope.Pop(1);
        Assert.Equal("dark", scope.Read(ThemeCtx));
        scope.Pop(1);
        Assert.Equal("light", scope.Read(ThemeCtx));
    }

    [Fact]
    public void SinglePairPush_MatchesDictionaryPush()
    {
        var single = new ContextScope();
        single.Push(ThemeCtx, "dark");

        var dict = new ContextScope();
        dict.Push(new Dictionary<ContextBase, object?> { [ThemeCtx] = "dark" });

        Assert.Equal(dict.Read(ThemeCtx), single.Read(ThemeCtx));
    }

    // ════════════════════════════════════════════════════════════════
    //  #62-#67 — accessibility scanner output parity
    // ════════════════════════════════════════════════════════════════

    private static Element BuildScanTree() => VStack(
        Button(TextBlock("🔍"), null),          // A11Y_001: icon-only button
        Image("ms-appx:///Assets/photo.png"),   // A11Y_002: image without name
        Button("A").TabIndex(1),
        Button("B").TabIndex(7));                // A11Y_007: tab-index gap 1 → 7

    [Fact]
    public void Scanner_ReScan_ProducesIdenticalFindings()
    {
        var first = AccessibilityScanner.Scan(BuildScanTree());
        var second = AccessibilityScanner.Scan(BuildScanTree());

        // The refactored GetChildren-once walk, sibling-array cache and
        // single-pass BuildContext must emit byte-identical findings.
        var firstKeys = first.Select(f => $"{f.Id}|{f.Message}").OrderBy(s => s, StringComparer.Ordinal);
        var secondKeys = second.Select(f => $"{f.Id}|{f.Message}").OrderBy(s => s, StringComparer.Ordinal);
        Assert.Equal(firstKeys, secondKeys);

        // Spot-check the expected findings are actually present.
        Assert.Contains(first, f => f.Id == "A11Y_001");
        Assert.Contains(first, f => f.Id == "A11Y_002");
        Assert.Contains(first, f => f.Id == "A11Y_007");
    }

    [Fact]
    public void Scanner_EmitsFindings_InStableWalkOrder()
    {
        // #62-66 refactored the GetChildren-once walk, sibling cache and single-pass
        // BuildContext. Scanner_ReScan_ProducesIdenticalFindings sorts before comparing,
        // so it cannot catch an emission-ORDER regression. Pin the natural walk order:
        // per-element findings emit depth-first during Walk (icon button, then image),
        // and the tree-wide tab-index-gap check emits afterward in the post-walk phase.
        var ids = AccessibilityScanner.Scan(BuildScanTree())
            .Select(f => f.Id)
            .Where(id => id is "A11Y_001" or "A11Y_002" or "A11Y_007")
            .ToArray();

        Assert.Equal(new[] { "A11Y_001", "A11Y_002", "A11Y_007" }, ids);
    }

    [Fact]
    public void Scanner_IconButton_StillCarriesChildTypeContext()
    {
        // Exercises the single-pass BuildContext child extraction (#64).
        var findings = AccessibilityScanner.Scan(VStack(Button(TextBlock("🔍"), null)));

        var iconFinding = findings.FirstOrDefault(f => f.Id == "A11Y_001");
        Assert.NotNull(iconFinding);
        Assert.NotNull(iconFinding!.Context.ChildTypes);
        Assert.Contains("TextBlockElement", iconFinding.Context.ChildTypes!);
    }

    [Fact]
    public void Scanner_SequentialTabIndex_NoGapFinding()
    {
        // Exercises the Array.Sort-over-rented-buffer tab-index scan (#66).
        var findings = AccessibilityScanner.Scan(VStack(
            Button("A").TabIndex(1),
            Button("B").TabIndex(2),
            Button("C").TabIndex(3)));

        Assert.DoesNotContain(findings, f => f.Id == "A11Y_007");
    }

    [Fact]
    public void Scanner_EmitsExactExpectedMessages()
    {
        // Pin the literal finding messages (not just IDs / re-scan determinism) so a
        // regression in the refactored walk that altered every scan identically would
        // still be caught (#62-#66 output parity).
        var findings = AccessibilityScanner.Scan(BuildScanTree());

        Assert.Contains(findings, f =>
            f.Id == "A11Y_001" &&
            f.Message == "Icon-only Button has no accessible name — screen readers cannot describe this control");
        Assert.Contains(findings, f =>
            f.Id == "A11Y_002" &&
            f.Message == "Image has no accessible name and is not hidden from assistive technology");
        Assert.Contains(findings, f =>
            f.Id == "A11Y_007" &&
            f.Message == "TabIndex gap: 1 → 7. Non-sequential values may confuse keyboard navigation order");
    }

    // ════════════════════════════════════════════════════════════════
    //  #25 (H1) — reference types implementing IEquatable<T> WITHOUT an
    //  object.Equals override keep the prior reference-equality change
    //  detection. A value-equality collapse here would skip a required
    //  context rerender, so the hybrid compare must NOT route ref types
    //  through EqualityComparer<T>.Default.
    // ════════════════════════════════════════════════════════════════

    private sealed class RefId : global::System.IEquatable<RefId>
    {
        private readonly int _v;
        public RefId(int v) => _v = v;
        // IEquatable<T> value equality, but intentionally NO object.Equals/GetHashCode
        // override — mirrors a hand-rolled ref type used as a context value.
        public bool Equals(RefId? other) => other is not null && other._v == _v;
    }

    private static readonly Context<RefId> RefIdCtx = new(new RefId(0));

    [Fact]
    public void CurrentValueEquals_ReferenceIEquatable_UsesReferenceEquality()
    {
        var a = new RefId(1);
        var b = new RefId(1); // IEquatable-equal to a, but a distinct instance

        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [RefIdCtx] = b });

        // The old object.Equals path was the virtual object.Equals (reference equality
        // for this type), so a freshly-built-but-equal instance counts as CHANGED.
        Assert.False(RefIdCtx.CurrentValueEquals(scope, a));
        // The same reference is unchanged.
        Assert.True(RefIdCtx.CurrentValueEquals(scope, b));

        scope.Pop(1);
    }

    // A reference type whose object.Equals is NON-reflexive (returns false even for the
    // same instance). object.Equals(x, x) short-circuits via ReferenceEquals and returns
    // true, so the context compare must keep that fast-path, NOT call x.Equals(x).
    private sealed class NonReflexive
    {
        public override bool Equals(object? obj) => false;
        public override int GetHashCode() => 0;
    }

    private static readonly Context<NonReflexive> NonReflexiveCtx = new(new NonReflexive());

    [Fact]
    public void CurrentValueEquals_ReferenceType_SameInstance_UsesReferenceEqualityFastPath()
    {
        var x = new NonReflexive();
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [NonReflexiveCtx] = x });

        // Same instance recorded and current: object.Equals(x, x) == true via the
        // ReferenceEquals short-circuit even though x.Equals(x) == false. Match it exactly.
        Assert.Equal(object.Equals(x, x), NonReflexiveCtx.CurrentValueEquals(scope, x));
        Assert.True(NonReflexiveCtx.CurrentValueEquals(scope, x));

        scope.Pop(1);
    }

    [Fact]
    public void CurrentValueEquals_ValueType_TypeMismatch_ReturnsFalseWithoutThrowing()
    {
        // A same-slot context-type swap can leave a boxed value of a DIFFERENT type as
        // the recorded LastValue. The old object.Equals returned false there (it never
        // threw); current.Equals(lastBoxed) — the Equals(object) override — does the same
        // (int.Equals(object) returns false for a non-int), not an InvalidCastException.
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [CountCtx] = 7 });

        Assert.False(CountCtx.CurrentValueEquals(scope, "not-an-int"));

        scope.Pop(1);
    }

    // A struct whose IEquatable<T>.Equals INTENTIONALLY disagrees with its object.Equals
    // override: object.Equals compares Value AND Tag; IEquatable<T> ignores Tag. The context
    // change-detection must follow object.Equals (the documented pre-#25 semantics), NOT
    // IEquatable<T> — otherwise EqualityComparer<T>.Default would mask a real Tag change and
    // skip a required rerender.
    private readonly struct SplitEq : global::System.IEquatable<SplitEq>
    {
        public readonly int Value;
        public readonly int Tag;
        public SplitEq(int value, int tag) { Value = value; Tag = tag; }

        public bool Equals(SplitEq other) => Value == other.Value;                 // ignores Tag
        public override bool Equals(object? obj) => obj is SplitEq o && o.Value == Value && o.Tag == Tag;
        public override int GetHashCode() => Value;
    }

    private static readonly Context<SplitEq> SplitEqCtx = new(default);

    [Fact]
    public void CurrentValueEquals_ValueIEquatable_UsesObjectEqualsNotIEquatable()
    {
        // Current value = (Value 5, Tag 1).
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [SplitEqCtx] = new SplitEq(5, 1) });

        // Same Value, DIFFERENT Tag: object.Equals => CHANGED. EqualityComparer<T>.Default
        // (IEquatable<T>) would wrongly say UNCHANGED. Assert we match object.Equals exactly.
        Assert.Equal(
            object.Equals(new SplitEq(5, 1), (object)new SplitEq(5, 2)),
            SplitEqCtx.CurrentValueEquals(scope, new SplitEq(5, 2)));
        Assert.False(SplitEqCtx.CurrentValueEquals(scope, new SplitEq(5, 2)));

        // Fully equal (Value AND Tag) is unchanged.
        Assert.True(SplitEqCtx.CurrentValueEquals(scope, new SplitEq(5, 1)));

        scope.Pop(1);
    }

    private static readonly Context<int?> NullableCountCtx = new(defaultValue: null);

    [Fact]
    public void CurrentValueEquals_NullableValueType_BoxedUnderlying_MatchesExactly()
    {
        // Guards the boxed-Nullable<T> edge: a non-null int? boxes to a boxed underlying
        // int, and Nullable<int>.Equals(object) (the constrained-call target) unwraps and
        // compares it, so an UNCHANGED nullable context value is not spuriously treated as
        // changed.
        var scope = new ContextScope();
        scope.Push(new Dictionary<ContextBase, object?> { [NullableCountCtx] = 5 });

        Assert.True(NullableCountCtx.CurrentValueEquals(scope, 5));      // unchanged (boxed int 5)
        Assert.False(NullableCountCtx.CurrentValueEquals(scope, 6));     // changed
        Assert.False(NullableCountCtx.CurrentValueEquals(scope, null));  // 5 vs null
        scope.Pop(1);

        // Default (null) current vs a null record is unchanged.
        Assert.True(NullableCountCtx.CurrentValueEquals(scope, null));
    }

    // ════════════════════════════════════════════════════════════════
    //  #15 — component rerender delegate caching
    //  (and #20 — unmount tombstone refuses a late self-trigger)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetOrCreateComponentRerender_ReusesDelegate_UntilSourceIdentityChanges()
    {
        var reconciler = new Reconciler();
        var node = new Reconciler.ComponentNode();

        global::System.Action source = () => { };
        var first = reconciler.GetOrCreateComponentRerender(node, source);
        var second = reconciler.GetOrCreateComponentRerender(node, source);

        // Same upstream requestRerender ⇒ same wrapped delegate (no per-frame closure alloc).
        Assert.Same(first, second);
        Assert.Same(source, node.CachedRerenderSource);

        // A new upstream identity ⇒ a freshly built wrapper, and the source is updated.
        global::System.Action source2 = () => { };
        var third = reconciler.GetOrCreateComponentRerender(node, source2);
        Assert.NotSame(first, third);
        Assert.Same(source2, node.CachedRerenderSource);
    }

    [Fact]
    public void CachedRerender_WhenInvoked_MarksSelfTriggered_AndBubblesToSource()
    {
        var reconciler = new Reconciler();
        var node = new Reconciler.ComponentNode();

        int sourceCalls = 0;
        global::System.Action source = () => sourceCalls++;
        var wrapped = reconciler.GetOrCreateComponentRerender(node, source);
        Assert.NotNull(wrapped);

        Assert.False(node.SelfTriggered);
        wrapped(); // headless: no UI dispatcher captured ⇒ runs inline

        Assert.True(node.SelfTriggered);
        Assert.Equal(1, sourceCalls);
    }

    [Fact]
    public void CachedRerender_AfterUnmountTombstone_DoesNotReSelfTrigger()
    {
        // #20: a cached closure can outlive the node and fire after teardown. The
        // Unmounted tombstone must stop it re-adding the node to the self-triggered set
        // (which the dirty-path pass would otherwise never clear again, pinning the tree).
        var reconciler = new Reconciler();
        var node = new Reconciler.ComponentNode();

        int sourceCalls = 0;
        global::System.Action source = () => sourceCalls++;
        var wrapped = reconciler.GetOrCreateComponentRerender(node, source);
        Assert.NotNull(wrapped);

        node.Unmounted = true; // mirrors ClearSelfTriggered(node, unmounting: true)
        wrapped();

        Assert.False(node.SelfTriggered); // refused by the tombstone guard
        Assert.Equal(1, sourceCalls);     // the upstream callback itself still runs (unchanged)
    }
}
