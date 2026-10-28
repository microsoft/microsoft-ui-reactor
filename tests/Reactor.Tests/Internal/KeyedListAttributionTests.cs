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

        Assert.Equal(["a", KeyedListDiff.NullKeySentinel(1), "c"], seeded.Select(s => s.Key));
        var warning = WarningFor(ctx, KeyedListDiagnosticKind.NullKey);
        Assert.NotNull(warning);
        Assert.Equal("Owner", warning!.ComponentName);
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
        Assert.Null(warning.ComponentName);
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
    public void Apply_NullKey_CarriesAttribution()
    {
        var ctx = $"Apply_{NewId()}";
        var state = new ReactorListState();
        state.Reset([(0, "a")]);
        string?[] next = ["a", null];

        var stats = KeyedListDiff.Apply(
            state, next, static (k, _) => k!, logger: null, ctx, ambient: null, controlInstance: new object(),
            attribution: new KeyedListAttribution("ListOwner", Site));

        Assert.False(stats.Bailout);
        var warning = WarningFor(ctx, KeyedListDiagnosticKind.NullKey);
        Assert.Equal("ListOwner", warning?.ComponentName);
        Assert.Equal(Site, warning?.CallSite);
    }

    [Fact]
    public void Record_InterleavedControls_UpdateTheirOwnEntries()
    {
        // Two lists with the same offending keys and context, reported A, B, A: A's repeat must
        // bump A's entry, not the newer B entry that shares its label, kind and samples.
        var ctx = $"ABA_{NewId()}";
        var a = new object();
        var b = new object();
        var siteA = new SourceLocation("Views/A.cs", 1, 1);
        var siteB = new SourceLocation("Views/B.cs", 2, 2);
        string[] samples = ["k"];

        ReactorDiagnostics.Record(a, ctx, KeyedListDiagnosticKind.DuplicateKey, samples, "ListA", siteA);
        ReactorDiagnostics.Record(b, ctx, KeyedListDiagnosticKind.DuplicateKey, samples, "ListB", siteB);
        ReactorDiagnostics.Record(a, ctx, KeyedListDiagnosticKind.DuplicateKey, samples, "ListA", siteA);

        var entries = ReactorDiagnostics.RecentKeyedListWarnings.Where(w => w.ControlContext == ctx).ToList();
        Assert.Equal(2, entries.Count);
        var entryA = Assert.Single(entries, w => w.ComponentName == "ListA");
        var entryB = Assert.Single(entries, w => w.ComponentName == "ListB");
        Assert.Equal((2, siteA), (entryA.Count, entryA.CallSite!.Value));
        Assert.Equal((1, siteB), (entryB.Count, entryB.CallSite!.Value));
    }

    [Fact]
    public void Record_Repeat_TakesTheLatestAttribution()
    {
        // One reused control, bound first from one call site and then from another (a
        // conditional branch): the entry names the latest; a report with no attribution keeps it.
        var ctx = $"Rebind_{NewId()}";
        var control = new object();
        var first = new SourceLocation("Views/First.cs", 4, 2);
        var second = new SourceLocation("Views/Second.cs", 8, 6);
        string[] samples = ["k"];

        ReactorDiagnostics.Record(control, ctx, KeyedListDiagnosticKind.DuplicateKey, samples, "RootA", first);
        ReactorDiagnostics.Record(control, ctx, KeyedListDiagnosticKind.DuplicateKey, samples, "RootB", second);
        ReactorDiagnostics.Record(control, ctx, KeyedListDiagnosticKind.DuplicateKey, samples);

        var entry = Assert.Single(ReactorDiagnostics.RecentKeyedListWarnings, w => w.ControlContext == ctx);
        Assert.Equal(3, entry.Count);
        Assert.Equal("RootB", entry.ComponentName);
        Assert.Equal(second, entry.CallSite);
    }

    [Fact]
    public void Record_Repeat_RefreshesTheOverflowSummary()
    {
        // Only the first eight keys feed the dedup key, so nine and then ten keys with the same
        // first eight are one entry; its summary must follow the latest report.
        var ctx = $"Overflow_{NewId()}";
        var control = new object();
        var nine = Enumerable.Range(0, 9).Select(i => "k" + i).ToArray();
        var ten = Enumerable.Range(0, 10).Select(i => "k" + i).ToArray();

        ReactorDiagnostics.Record(control, ctx, KeyedListDiagnosticKind.DuplicateKey, nine);
        ReactorDiagnostics.Record(control, ctx, KeyedListDiagnosticKind.DuplicateKey, ten);

        var entry = Assert.Single(ReactorDiagnostics.RecentKeyedListWarnings, w => w.ControlContext == ctx);
        Assert.Equal(2, entry.Count);
        Assert.Equal("…and 2 more", entry.SampleKeys[^1]);
    }

    [Theory]
    [InlineData(6)]  // the small-panel pairwise scan
    [InlineData(40)] // the pooled-set scan
    public void DuplicateSiblingKeys_SampleIsDistinctAndSorted(int count)
    {
        var parent = new object();
        var site = new SourceLocation($"Views/{NewId()}.cs", 3, 1);
        // "z" three times and "m" twice, "z" first: each named once, in ordinal order.
        var keys = new[] { "z", "m", "z", "m", "z" }.Concat(Enumerable.Range(0, count - 5).Select(i => "u" + i));
        var children = keys.Select(k => (Element?)(TextBlock("x") with { Key = k, CallSite = site })).ToArray();

        KeyedListDiff.ReportDuplicateSiblingKeys(children, reconciler: null, parent);

        var warning = ReactorDiagnostics.RecentKeyedListWarnings.FirstOrDefault(w => w.CallSite == site);
        Assert.Equal(["m", "z"], warning?.SampleKeys);
    }

    [Fact]
    public void SeedKeys_RepeatedDuplicates_AreNamedOnceSorted()
    {
        var ctx = $"SeedMany_{NewId()}";
        string?[] keys = ["b", "a", "b", "a", "b", "c"];

        KeyedListDiff.SeedKeys(keys, keys.Length, static (k, i) => k[i], logger: null, ctx, new object(), default);

        Assert.Equal(["a", "b"], WarningFor(ctx, KeyedListDiagnosticKind.DuplicateKey)?.SampleKeys);
    }

    [Theory]
    [InlineData("__null_1")] // the placeholder format this replaced
    [InlineData("\u00001")]  // the current placeholder text for index 1, authored
    [InlineData("\u0000\u00001")]
    public void AuthoredKeyLikeAPlaceholder_NeverCollides_AndStaysIncremental(string authored)
    {
        // A null key at index 1 next to an authored key spelled like a placeholder: seed and an
        // unchanged update must report no duplicate and must not bail out (reset the list).
        var ctx = $"Collide_{NewId()}";
        var control = new object();
        string?[] keys = [authored, null];

        var state = new ReactorListState();
        state.Reset(KeyedListDiff.SeedKeys(keys, keys.Length, static (k, i) => k[i], logger: null, ctx, control, default));
        var stats = KeyedListDiff.Apply(state, keys, static (k, _) => k!, logger: null, ctx, ambient: null, controlInstance: control);
        // A real change after it stays incremental too.
        string?[] grown = [authored, null, "z"];
        var grownStats = KeyedListDiff.Apply(state, grown, static (k, _) => k!, logger: null, ctx, ambient: null, controlInstance: control);

        Assert.Null(WarningFor(ctx, KeyedListDiagnosticKind.DuplicateKey));
        Assert.False(stats.Bailout);
        Assert.False(grownStats.Bailout);
        Assert.Equal(1, grownStats.Inserts);
        Assert.Equal(3, state.ByKey.Count);
    }

    [Fact]
    public void DuplicateNulPrefixedKeys_AreReportedByTheirAuthoredText_AtSeedAndUpdate()
    {
        // The escape is internal: seed and diff both report the authored key, so one entry.
        var ctx = $"NulDup_{NewId()}";
        var control = new object();
        string[] keys = ["\u0000k", "\u0000k"];

        var state = new ReactorListState();
        state.Reset(KeyedListDiff.SeedKeys(keys, keys.Length, static (k, i) => k[i], logger: null, ctx, control, default));
        string[] next = ["\u0000k", "\u0000k", "x"];
        KeyedListDiff.Apply(state, next, static (k, _) => k, logger: null, ctx, ambient: null, controlInstance: control);

        var entry = Assert.Single(ReactorDiagnostics.RecentKeyedListWarnings,
            w => w.ControlContext == ctx && w.Kind == KeyedListDiagnosticKind.DuplicateKey);
        Assert.Equal(["\u0000k"], entry.SampleKeys);
        Assert.Equal(2, entry.Count);
    }

    [Fact]
    public void SeedThenApply_SameDuplicates_ShareOneEntry()
    {
        var ctx = $"SeedApply_{NewId()}";
        var control = new object();
        string[] keys = ["b", "a", "b", "a"];

        var state = new ReactorListState();
        state.Reset(KeyedListDiff.SeedKeys(keys, keys.Length, static (k, i) => k[i], logger: null, ctx, control, default));
        string[] next = ["b", "a", "b", "a", "c"];
        KeyedListDiff.Apply(state, next, static (k, _) => k, logger: null, ctx, ambient: null, controlInstance: control);

        var entries = ReactorDiagnostics.RecentKeyedListWarnings
            .Where(w => w.ControlContext == ctx && w.Kind == KeyedListDiagnosticKind.DuplicateKey).ToList();
        var entry = Assert.Single(entries);
        Assert.Equal(["a", "b"], entry.SampleKeys);
        Assert.Equal(2, entry.Count);
    }

    [Fact]
    public void SeedKeys_PooledSet_IsClearedBetweenSeeds()
    {
        string?[] keys = ["a", "b"];
        var first = $"SeedReuseA_{NewId()}";
        var second = $"SeedReuseB_{NewId()}";

        KeyedListDiff.SeedKeys(keys, keys.Length, static (k, i) => k[i], logger: null, first, new object(), default);
        KeyedListDiff.SeedKeys(keys, keys.Length, static (k, i) => k[i], logger: null, second, new object(), default);

        // A set carried over from the first seed would report "a" and "b" as duplicates here.
        Assert.Null(WarningFor(second, KeyedListDiagnosticKind.DuplicateKey));
    }

    [Fact]
    public void LoggedWarnings_NameComponentAndShortCallSite_AndBoundTheKeys()
    {
        var logger = new CapturingLogger();
        var site = new SourceLocation(@"C:\Users\someone\src\App\Views\List.cs", 42, 5);
        var attribution = new KeyedListAttribution("ListOwner", site);

        // A null key met during an update: reported, not a bailout.
        KeyedListDiff.ReportNullKey(new object(), $"Log_{NewId()}", logger, index: 3, attribution);
        var nullMessage = Assert.Single(logger.Messages);
        Assert.Contains("invalid list key", nullMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("bailed out", nullMessage, StringComparison.Ordinal);
        Assert.Contains("null key at index 3", nullMessage, StringComparison.Ordinal);
        Assert.Contains("ListOwner", nullMessage, StringComparison.Ordinal);
        Assert.Contains("List.cs:42", nullMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", nullMessage, StringComparison.Ordinal);

        // A duplicate bailout from Apply: many duplicated keys are bounded in the message.
        logger.Messages.Clear();
        var state = new ReactorListState();
        state.Reset([(0, "seed")]);
        var many = Enumerable.Range(0, 12).SelectMany(i => new[] { "d" + i, "d" + i }).ToArray();
        KeyedListDiff.Apply(state, many, static (k, _) => k, logger, $"LogDup_{NewId()}", ambient: null,
            controlInstance: new object(), attribution: attribution);
        var dupMessage = Assert.Single(logger.Messages);
        Assert.Contains("bailed out", dupMessage, StringComparison.Ordinal);
        Assert.Contains("(4 more)", dupMessage, StringComparison.Ordinal);
        Assert.Contains("List.cs:42", dupMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("someone", dupMessage, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Messages { get; } = new();
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NullScope();
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, global::System.Exception? exception, Func<TState, global::System.Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
        private sealed class NullScope : IDisposable { public void Dispose() { } }
    }

    [Fact]
    public void ReportNullKey_RecordsWithoutBailingOut_AndCountsRepeats()
    {
        var ctx = $"NullReport_{NewId()}";
        var control = new object();

        KeyedListDiff.ReportNullKey(control, ctx, logger: null, index: 2, new KeyedListAttribution("Owner", Site));
        KeyedListDiff.ReportNullKey(control, ctx, logger: null, index: 2, new KeyedListAttribution("Owner", Site));

        var warning = WarningFor(ctx, KeyedListDiagnosticKind.NullKey);
        Assert.Equal(2, warning?.Count);
        Assert.Equal("Owner", warning?.ComponentName);
    }

    [Fact]
    public void Apply_NullKeys_ReportedOncePerDiff_AndStayIncremental()
    {
        var ctx = $"ApplySub_{NewId()}";
        var control = new object();
        var state = new ReactorListState();
        state.Reset([(0, "a"), (1, KeyedListDiff.NullKeySentinel(1)), (2, KeyedListDiff.NullKeySentinel(2))]);
        // Two null keys, plus an append: a real (incremental) change.
        string?[] next = ["a", null, null, "d"];

        var stats = KeyedListDiff.Apply(
            state, next, static (k, _) => k!, logger: null, ctx, ambient: null, controlInstance: control,
            attribution: new KeyedListAttribution("ListOwner", Site));

        Assert.False(stats.Bailout);
        Assert.Equal(1, stats.Inserts);
        Assert.Equal(["a", KeyedListDiff.NullKeySentinel(1), KeyedListDiff.NullKeySentinel(2), "d"], state.LastKeys);
        var warning = WarningFor(ctx, KeyedListDiagnosticKind.NullKey);
        Assert.Equal(1, warning?.Count); // once for the diff, not once per null item
        Assert.Equal("ListOwner", warning?.ComponentName);
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
