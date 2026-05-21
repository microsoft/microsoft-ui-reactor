using Microsoft.UI.Reactor.Docking;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 045 §2.21 — string-routing contract. Apps wire
/// <see cref="DockingStrings.Resolver"/> at startup to forward keys
/// into their <c>IntlAccessor</c>; without a resolver, callers get
/// the English default that mirrors <c>Reactor.Docking.resw</c>.
/// </summary>
public sealed class DockingStringsTests : IDisposable
{
    private readonly Func<string, string?>? _saved = DockingStrings.Resolver;

    public void Dispose() => DockingStrings.Resolver = _saved;

    [Fact]
    public void Get_NoResolver_ReturnsEnglishDefault()
    {
        DockingStrings.Resolver = null;
        Assert.Equal("Add as tab", DockingStrings.Get(DockingStringKeys.DropTargetCenter));
        Assert.Equal("Split left", DockingStrings.Get(DockingStringKeys.DropTargetSplitLeft));
        Assert.Equal("Dock bottom", DockingStrings.Get(DockingStringKeys.DropTargetDockBottom));
        Assert.Equal("Floating Window", DockingStrings.Get(DockingStringKeys.FloatingWindowDefaultTitle));
    }

    [Fact]
    public void Get_WithResolver_ForwardsKeyAndReturnsLocalized()
    {
        string? captured = null;
        DockingStrings.Resolver = k => { captured = k; return "Ajouter comme onglet"; };
        var result = DockingStrings.Get(DockingStringKeys.DropTargetCenter);
        Assert.Equal("Docking.DropTarget.Center", captured);
        Assert.Equal("Ajouter comme onglet", result);
    }

    [Fact]
    public void Get_ResolverReturnsNull_FallsBackToEnglish()
    {
        DockingStrings.Resolver = _ => null;
        Assert.Equal("Add as tab", DockingStrings.Get(DockingStringKeys.DropTargetCenter));
    }

    [Fact]
    public void Get_ResolverReturnsEmpty_FallsBackToEnglish()
    {
        DockingStrings.Resolver = _ => string.Empty;
        Assert.Equal("Split top", DockingStrings.Get(DockingStringKeys.DropTargetSplitTop));
    }

    [Fact]
    public void SidePinTooltip_NoResolver_FillsPlaceholder()
    {
        DockingStrings.Resolver = null;
        Assert.Equal("Show Solution Explorer", DockingStrings.SidePinTooltip("Solution Explorer"));
    }

    [Fact]
    public void SidePinTooltip_WithResolver_SubstitutesAfterLookup()
    {
        DockingStrings.Resolver = k => k == DockingStringKeys.SidePinTooltipPrefix
            ? "Afficher {paneTitle}"
            : null;
        Assert.Equal("Afficher Output", DockingStrings.SidePinTooltip("Output"));
    }

    [Fact]
    public void SidePinTooltip_NullPaneTitle_ReplacedWithEmpty()
    {
        DockingStrings.Resolver = null;
        Assert.Equal("Show ", DockingStrings.SidePinTooltip(null!));
    }

    [Fact]
    public void DropTargetKeysCoverEveryEnumValue()
    {
        var keys = new[]
        {
            DockingStringKeys.DropTargetCenter,
            DockingStringKeys.DropTargetSplitLeft, DockingStringKeys.DropTargetSplitRight,
            DockingStringKeys.DropTargetSplitTop, DockingStringKeys.DropTargetSplitBottom,
            DockingStringKeys.DropTargetDockLeft, DockingStringKeys.DropTargetDockRight,
            DockingStringKeys.DropTargetDockTop, DockingStringKeys.DropTargetDockBottom,
        };
        DockingStrings.Resolver = null;
        foreach (var k in keys)
        {
            var v = DockingStrings.Get(k);
            Assert.False(string.IsNullOrWhiteSpace(v));
            Assert.NotEqual(k, v); // English default not equal to key.
        }
    }

    [Fact]
    public void NavigatorAndMenuKeysHaveEnglishDefaults()
    {
        DockingStrings.Resolver = null;
        Assert.Equal("Documents", DockingStrings.Get(DockingStringKeys.NavigatorHeadingDocuments));
        Assert.Equal("Tool Windows", DockingStrings.Get(DockingStringKeys.NavigatorHeadingToolWindows));
        Assert.Equal("Close", DockingStrings.Get(DockingStringKeys.MenuClose));
        Assert.Equal("Float", DockingStrings.Get(DockingStringKeys.MenuFloat));
        Assert.StartsWith("Could not restore", DockingStrings.Get(DockingStringKeys.LayoutRestoreFailed));
    }
}
