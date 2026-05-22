using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 045 §2.22 — accessibility coverage for pieces that don't need
/// a real WinUI host: stable AutomationId derivation from
/// <see cref="DockableContent.Key"/>, and the landmark string key.
/// AT-tree walking + UIA role assertions live in self-host fixtures
/// because they require a realized control tree.
/// </summary>
[Collection("DockingGlobals")]
public sealed class DockA11yTests : IDisposable
{
    private readonly Func<string, string?>? _savedResolver = DockingStrings.Resolver;

    public void Dispose() => DockingStrings.Resolver = _savedResolver;

    [Fact]
    public void AutomationIdForPane_NonNullKey_PrefixedWithPane()
    {
        var doc = new Document { Title = "Editor", Key = "doc:editor.cs" };
        var id = DockHostNativeComponent.AutomationIdForPane(doc);
        Assert.Equal("pane:doc:editor.cs", id);
    }

    [Fact]
    public void AutomationIdForPane_StableAcrossEquivalentInstances()
    {
        var a = new Document { Title = "A", Key = "k" };
        var b = new Document { Title = "B", Key = "k" };
        Assert.Equal(
            DockHostNativeComponent.AutomationIdForPane(a),
            DockHostNativeComponent.AutomationIdForPane(b));
    }

    [Fact]
    public void AutomationIdForPane_NullKey_ReturnsNull()
    {
        var doc = new Document { Title = "Untitled", Key = null };
        Assert.Null(DockHostNativeComponent.AutomationIdForPane(doc));
    }

    [Fact]
    public void AutomationIdForPane_EmptyStringKey_ReturnsNull()
    {
        var doc = new Document { Title = "X", Key = string.Empty };
        Assert.Null(DockHostNativeComponent.AutomationIdForPane(doc));
    }

    [Fact]
    public void AutomationIdForPane_NonStringKey_UsesToString()
    {
        var doc = new Document { Title = "X", Key = 42 };
        Assert.Equal("pane:42", DockHostNativeComponent.AutomationIdForPane(doc));
    }

    [Fact]
    public void DockHostLandmarkKey_HasEnglishDefault()
    {
        DockingStrings.Resolver = null;
        Assert.Equal("Docking area", DockingStrings.Get(DockingStringKeys.DockHostLandmark));
    }
}
