using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Core.Internal;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Xunit;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.Tests.Internal;

/// <summary>
/// Keyed-list diagnostics that were silent or anonymous: a list seeded with a null or
/// duplicated key (at mount, before any diff runs), duplicate <c>.WithKey</c> siblings in a
/// panel, and the component / call site every warning now carries. Observed by content in the
/// shared <see cref="ReactorDiagnostics.RecentKeyedListWarnings"/> buffer, with a unique context
/// per test, like <see cref="ReactorDiagnosticsTests"/>.
/// </summary>
public class KeyedListAttributionTests
{
    private static string NewId() => global::System.Guid.NewGuid().ToString("N");

    private static KeyedListDiagnostic? WarningFor(string context, KeyedListDiagnosticKind kind)
        => ReactorDiagnostics.RecentKeyedListWarnings
            .FirstOrDefault(w => w.ControlContext == context && w.Kind == kind);

    private static readonly SourceLocation Site = new("Views/List.cs", 12, 9);

    [Fact]
    public void SeedKeys_NullKey_IsReported_AndReplacedBySentinel()
    {
        var ctx = $"Seed_{NewId()}";
        string?[] keys = ["a", null, "c"];

        var seeded = KeyedListDiff.SeedKeys(
            keys, keys.Length, static (k, i) => k[i], logger: null, ctx, new object(),
            new KeyedListAttribution("Owner", Site));

        Assert.Equal(["a", "__null_1", "c"], seeded.Select(s => s.Key));
        var warning = WarningFor(ctx, KeyedListDiagnosticKind.NullKey);
        Assert.NotNull(warning);
        Assert.Equal("Owner", warning!.Component);
        Assert.Equal(Site, warning.CallSite);
        Assert.Null(WarningFor(ctx, KeyedListDiagnosticKind.DuplicateKey));
    }

    [Fact]
    public void SeedKeys_DuplicateKeys_AreReported_NotTheSentinels()
    {
        var ctx = $"SeedDup_{NewId()}";
        // Two nulls produce distinct sentinels; the real duplicate is "a".
        string?[] keys = ["a", null, "a", null, "b"];

        KeyedListDiff.SeedKeys(keys, keys.Length, static (k, i) => k[i], logger: null, ctx, new object(), default);

        var warning = WarningFor(ctx, KeyedListDiagnosticKind.DuplicateKey);
        Assert.NotNull(warning);
        Assert.Equal(["a"], warning!.SampleKeys);
        Assert.Null(warning.Component);
        Assert.Null(warning.CallSite);
    }

    [Fact]
    public void SeedKeys_ValidKeys_ReportNothing()
    {
        var ctx = $"SeedOk_{NewId()}";
        string?[] keys = ["a", "b", "c"];

        KeyedListDiff.SeedKeys(keys, keys.Length, static (k, i) => k[i], logger: null, ctx, new object(), default);

        Assert.Null(WarningFor(ctx, KeyedListDiagnosticKind.NullKey));
        Assert.Null(WarningFor(ctx, KeyedListDiagnosticKind.DuplicateKey));
    }

    [Fact]
    public void Apply_NullKeyBailout_CarriesAttribution()
    {
        var ctx = $"Apply_{NewId()}";
        var state = new ReactorListState();
        state.Reset([(0, "a")]);
        string?[] next = ["a", null];

        var stats = KeyedListDiff.Apply(
            state, next, static (k, _) => k!, logger: null, ctx, ambient: null, controlInstance: new object(),
            attribution: new KeyedListAttribution("ListOwner", Site));

        Assert.True(stats.Bailout);
        var warning = WarningFor(ctx, KeyedListDiagnosticKind.NullKey);
        Assert.Equal("ListOwner", warning?.Component);
        Assert.Equal(Site, warning?.CallSite);
    }

    [Fact]
    public void DuplicateSiblingKeys_AreReported_WithTheFirstDuplicatesCallSite()
    {
        var parent = new object();
        var dupSite = new SourceLocation("Views/Panel.cs", 30, 13);
        Element?[] children =
        [
            TextBlock("a").WithKey("same"),
            TextBlock("b") with { Key = "same", CallSite = dupSite },
            TextBlock("c").WithKey("other"),
            TextBlock("d"),
            null,
        ];

        KeyedListDiff.ReportDuplicateSiblingKeys(children, reconciler: null, parent);

        var warning = ReactorDiagnostics.RecentKeyedListWarnings
            .FirstOrDefault(w => w.Kind == KeyedListDiagnosticKind.DuplicateKey && w.CallSite == dupSite);
        Assert.NotNull(warning);
        Assert.Equal(["same"], warning!.SampleKeys);
        Assert.Equal(nameof(Object), warning.ControlContext);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(40)] // past the small-panel pairwise scan: the pooled-set path
    public void DuplicateSiblingKeys_UniqueKeys_ReportNothing(int count)
    {
        var parent = new object();
        var site = new SourceLocation($"Views/{NewId()}.cs", 1, 1);
        var children = Enumerable.Range(0, count)
            .Select(i => (Element?)(TextBlock("x") with { Key = "k" + i, CallSite = site }))
            .ToArray();

        KeyedListDiff.ReportDuplicateSiblingKeys(children, reconciler: null, parent);

        Assert.DoesNotContain(ReactorDiagnostics.RecentKeyedListWarnings, w => w.CallSite == site);
    }

    [Fact]
    public void DuplicateSiblingKeys_LargePanel_IsReported()
    {
        var parent = new object();
        var site = new SourceLocation($"Views/{NewId()}.cs", 2, 1);
        var children = Enumerable.Range(0, 40)
            .Select(i => (Element?)(TextBlock("x") with { Key = i == 39 ? "k3" : "k" + i, CallSite = site }))
            .ToArray();

        KeyedListDiff.ReportDuplicateSiblingKeys(children, reconciler: null, parent);

        var warning = ReactorDiagnostics.RecentKeyedListWarnings.FirstOrDefault(w => w.CallSite == site);
        Assert.Equal(["k3"], warning?.SampleKeys);
    }

    [Fact]
    public void MenuAndCommandItems_EqualityIgnoresCallSite()
    {
        Action click = static () => { };
        var a = MenuItem("Alpha", click) with { CallSite = new SourceLocation("A.cs", 1, 1) };
        var b = MenuItem("Alpha", click) with { CallSite = new SourceLocation("B.cs", 2, 2) };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, MenuItem("Beta", click));
        Assert.NotEqual<MenuFlyoutItemBase>(MenuSeparator(), MenuItem("Alpha", click));

        var cut = AppBarButton("Cut", click) with { CallSite = new SourceLocation("A.cs", 1, 1) };
        Assert.Equal(cut, AppBarButton("Cut", click));
        Assert.NotEqual(cut, AppBarButton("Copy", click));
        Assert.NotEqual<AppBarItemBase>(AppBarSeparator(), AppBarButton("Cut", click));
    }

    [Theory]
    [InlineData(typeof(MenuFlyoutItemData), "MenuFlyoutItem")]
    [InlineData(typeof(ToggleMenuFlyoutItemData), "ToggleMenuFlyoutItem")]
    [InlineData(typeof(AppBarButtonData), "AppBarButton")]
    [InlineData(typeof(AppBarSeparatorData), "AppBarSeparator")]
    public void ItemKind_DropsTheDataSuffix(Type itemType, string kind)
        => Assert.Equal(kind, MenuCommandFactory.ItemKind(itemType));
}
