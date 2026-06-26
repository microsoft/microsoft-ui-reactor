using System;
using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Elements;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Unit tests for the PR-C structural-skip side-channel (<see cref="ChildDiffHints"/>
/// + <see cref="ChildDiffHint"/>). Pure: no reconciler, no WinUI controls — exercises
/// the reference-keyed registry and the theme-sensitivity predicate that gates the
/// positional fast path.
/// </summary>
public class ChildDiffHintsTests
{
    private static Element[] Cells(params string[] labels)
    {
        var arr = new Element[labels.Length];
        for (int i = 0; i < labels.Length; i++)
            arr[i] = new TextBlockElement(labels[i]);
        return arr;
    }

    // ── Registry round-trip ──────────────────────────────────────────

    [Fact]
    public void Publish_Then_TryGet_Returns_Same_Hint()
    {
        var children = Cells("a", "b", "c");
        var hint = new ChildDiffHint(new[] { 1 }, themeSensitiveCount: 0, previousChildren: Array.Empty<Element>());
        ChildDiffHints.Publish(children, hint);

        Assert.True(ChildDiffHints.TryGet(children, out var got));
        Assert.Same(hint, got);
        Assert.Equal(new[] { 1 }, got!.ChangedIndices);
    }

    [Fact]
    public void Hint_Exposes_PreviousChildren_Weakly()
    {
        var prev = Cells("a", "b");
        var hint = new ChildDiffHint(new[] { 0 }, themeSensitiveCount: 0, previousChildren: prev);
        Assert.True(hint.PreviousChildren.TryGetTarget(out var got));
        Assert.Same(prev, got);
    }

    [Fact]
    public void TryGet_Unpublished_Array_Misses()
    {
        var children = Cells("a", "b");
        Assert.False(ChildDiffHints.TryGet(children, out var got));
        Assert.Null(got);
    }

    [Fact]
    public void Hint_Is_Keyed_By_Reference_Not_Structural_Equality()
    {
        // Two distinct arrays with structurally-equal contents must not collide:
        // the CWT keys on the fresh-per-render array identity.
        var first = Cells("a", "b");
        var second = Cells("a", "b");
        ChildDiffHints.Publish(first, new ChildDiffHint(Array.Empty<int>(), 0, Array.Empty<Element>()));

        Assert.True(ChildDiffHints.TryGet(first, out _));
        Assert.False(ChildDiffHints.TryGet(second, out _));
    }

    [Fact]
    public void Publish_Twice_Overwrites_Prior_Hint()
    {
        var children = Cells("a", "b", "c");
        ChildDiffHints.Publish(children, new ChildDiffHint(new[] { 0 }, 0, Array.Empty<Element>()));
        var second = new ChildDiffHint(new[] { 2 }, 0, Array.Empty<Element>());
        ChildDiffHints.Publish(children, second);

        Assert.True(ChildDiffHints.TryGet(children, out var got));
        Assert.Same(second, got);
        Assert.Equal(new[] { 2 }, got!.ChangedIndices);
    }

    // ── AnyThemeSensitive flag ───────────────────────────────────────

    [Fact]
    public void AnyThemeSensitive_Tracks_Count()
    {
        Assert.False(new ChildDiffHint(Array.Empty<int>(), 0, Array.Empty<Element>()).AnyThemeSensitive);
        Assert.True(new ChildDiffHint(Array.Empty<int>(), 1, Array.Empty<Element>()).AnyThemeSensitive);
        Assert.True(new ChildDiffHint(Array.Empty<int>(), 5, Array.Empty<Element>()).AnyThemeSensitive);
    }

    // ── IsThemeSensitive predicate ───────────────────────────────────

    [Fact]
    public void IsThemeSensitive_False_For_Plain_Element()
    {
        Assert.False(ChildDiffHints.IsThemeSensitive(new TextBlockElement("plain")));
    }

    [Fact]
    public void IsThemeSensitive_True_For_ThemeBindings()
    {
        var el = new TextBlockElement("themed")
        {
            ThemeBindings = new Dictionary<string, ThemeRef> { ["Foreground"] = new ThemeRef("SystemAccentColor") },
        };
        Assert.True(ChildDiffHints.IsThemeSensitive(el));
    }

    [Fact]
    public void IsThemeSensitive_True_For_ResourceOverrides_ThemeRefs()
    {
        var el = new TextBlockElement("themed")
        {
            ResourceOverrides = new ResourceOverrides(
                Literals: new Dictionary<string, object>(),
                ThemeRefs: new Dictionary<string, ThemeRef> { ["ButtonBackground"] = new ThemeRef("AccentBrush") }),
        };
        Assert.True(ChildDiffHints.IsThemeSensitive(el));
    }

    [Fact]
    public void IsThemeSensitive_False_For_ResourceOverrides_With_Only_Literals()
    {
        // Literal overrides are NOT theme-reactive — only ThemeRefs re-resolve on
        // a theme flip, so a literal-only override must not block the fast path.
        var el = new TextBlockElement("literal")
        {
            ResourceOverrides = new ResourceOverrides(
                Literals: new Dictionary<string, object> { ["Pad"] = 4.0 },
                ThemeRefs: new Dictionary<string, ThemeRef>()),
        };
        Assert.False(ChildDiffHints.IsThemeSensitive(el));
    }
}
