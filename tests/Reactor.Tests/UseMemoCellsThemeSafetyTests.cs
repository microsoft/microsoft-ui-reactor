using System.Collections.Generic;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #659 review — adjudicating tests for the UseMemoCells full-cache-hit
/// early-out vs the reconciler's container-level structural skip.
///
/// Mechanism (load-bearing): a container element's <see cref="Element.ShallowEquals"/>
/// reference-compares its <c>Children</c> array (Element.cs ~622-699: "Same children
/// reference = truly unchanged subtree = safe to skip entirely"). When it returns
/// true, <c>Reconciler.Update</c> (Update.cs:81-99) keeps the existing control and
/// re-applies ONLY the container's OWN ThemeBindings/ResourceOverrides — it does NOT
/// recurse into the children, so a theme-sensitive DESCENDANT inside the memo'd
/// children never re-resolves its brush on a theme change.
///
/// Therefore: returning a reference-equal children array from a full-cache-hit is
/// only safe when NO child is theme-sensitive. When any child IS theme-sensitive,
/// the hook must return a FRESH array so the container falls through to the recursive
/// update and the theme re-applies (the old "fresh array every render" behavior).
/// </summary>
public class UseMemoCellsThemeSafetyTests
{
    private record Cell(string Content) : Element;

    private static readonly IReadOnlyDictionary<string, ThemeRef> ThemeBinding =
        new Dictionary<string, ThemeRef> { ["Foreground"] = Theme.PrimaryText };

    private static Cell ThemeSensitiveCell(string content) =>
        new Cell(content) { ThemeBindings = ThemeBinding };

    // ── Mechanism: ShallowEquals reference-compares Children ──────────────

    [Fact]
    public void Container_With_ReferenceEqual_Children_Is_ShallowEqual_Fresh_Array_Is_Not()
    {
        var child = ThemeSensitiveCell("a");
        var children = new Element[] { child };

        // Same array reference (what the full-cache-hit early-out returned) →
        // ShallowEquals true → Reconciler.Update skips the subtree → theme-sensitive
        // descendants never re-apply on a theme change.
        var stackA = new StackElement(Orientation.Vertical, children);
        var stackB = new StackElement(Orientation.Vertical, children);
        Assert.True(Element.ShallowEquals(stackA, stackB));

        // Fresh array (same child references) → ShallowEquals false → the reconciler
        // recurses into the children, so a theme-sensitive child re-applies.
        var stackC = new StackElement(Orientation.Vertical, new Element[] { child });
        Assert.False(Element.ShallowEquals(stackA, stackC));
    }

    // ── Invariant: theme-sensitive full-hit must NOT reuse the array ──────

    [Fact]
    public void FullCacheHit_With_ThemeSensitive_Children_Returns_Fresh_Array()
    {
        var ctx = new RenderContext();
        var items = new[] { 1, 2, 3 };

        ctx.BeginRender(() => { });
        var first = ctx.UseMemoCells<int>(items, (v, i) => ThemeSensitiveCell($"v={v}"), "deps");

        ctx.BeginRender(() => { });
        var second = ctx.UseMemoCells<int>(items, (v, i) => ThemeSensitiveCell($"v={v}"), "deps");

        // Full value-equal cache hit, but children ARE theme-sensitive: the array
        // MUST be a fresh reference so the container's ShallowEquals(Children) does
        // not skip the subtree and drop theme re-application. (Element references are
        // still reused, so per-child reconcile stays cheap.)
        Assert.NotSame(first, second);
        for (int i = 0; i < first.Length; i++)
            Assert.Same(first[i], second[i]);
    }

    // ── Win preserved: non-theme-sensitive full-hit may reuse the array ───

    [Fact]
    public void FullCacheHit_NonThemeSensitive_May_Reuse_Array()
    {
        var ctx = new RenderContext();
        var items = new[] { 1, 2, 3 };

        ctx.BeginRender(() => { });
        var first = ctx.UseMemoCells<int>(items, (v, i) => new Cell($"v={v}"), "deps");

        ctx.BeginRender(() => { });
        var second = ctx.UseMemoCells<int>(items, (v, i) => new Cell($"v={v}"), "deps");

        // No theme-sensitive children → the container skip is harmless (nothing to
        // re-apply), so the zero-allocation same-array fast path is retained.
        Assert.Same(first, second);
    }
}
