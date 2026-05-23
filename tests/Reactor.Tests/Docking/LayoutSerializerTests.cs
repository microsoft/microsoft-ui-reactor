using System.Diagnostics.Tracing;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Persistence;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Tests for the Phase-2 layout JSON v2 reader/writer
/// (<see cref="DockLayoutSerializer"/>) and the migration ladder
/// (<see cref="DockLayoutMigrationRegistry"/>). Spec 045 §5.4 / §8.9 /
/// §8.10; tracking §2.7 / §2.11.
/// </summary>
public class LayoutSerializerTests
{
    // ── Round-trip ─────────────────────────────────────────────────────

    [Fact]
    public void Save_EmitsSchemaVersionAtRoot()
    {
        var json = DockLayoutSerializer.Save(root: null);
        var node = JsonNode.Parse(json)!;
        Assert.Equal(2, node["$schema"]!.GetValue<int>());
    }

    [Fact]
    public void RoundTrip_SimpleDocument()
    {
        var doc = new Document { Title = "MainView", Key = "main" };
        var json = DockLayoutSerializer.Save(doc);
        var result = DockLayoutSerializer.Load(json);

        Assert.True(result.Success);
        var loaded = Assert.IsType<Document>(result.Root);
        Assert.Equal("MainView", loaded.Title);
        Assert.Equal("main", loaded.Key);
        Assert.True(loaded.CanClose);  // role default for Document
        Assert.False(loaded.CanPin);
    }

    [Fact]
    public void RoundTrip_ToolWindow_PreservesRole()
    {
        var tw = new ToolWindow { Title = "Output", Key = "out" };
        var json = DockLayoutSerializer.Save(tw);
        var result = DockLayoutSerializer.Load(json);

        var loaded = Assert.IsType<ToolWindow>(result.Root);
        Assert.Equal("Output", loaded.Title);
        Assert.True(loaded.CanHide);
        Assert.True(loaded.CanAutoHide);
        Assert.True(loaded.CanDockAsDocument);
    }

    [Fact]
    public void RoundTrip_SplitWithChildren()
    {
        var a = new Document { Title = "A", Key = "a" };
        var b = new Document { Title = "B", Key = "b" };
        var split = new DockSplit(Orientation.Horizontal, new DockNode[] { a, b });

        var json = DockLayoutSerializer.Save(split);
        var result = DockLayoutSerializer.Load(json);

        var loaded = Assert.IsType<DockSplit>(result.Root);
        Assert.Equal(Orientation.Horizontal, loaded.Orientation);
        Assert.Equal(2, loaded.Children.Count);

        var leafA = Assert.IsType<Document>(loaded.Children[0]);
        var leafB = Assert.IsType<Document>(loaded.Children[1]);
        Assert.Equal("a", leafA.Key);
        Assert.Equal("b", leafB.Key);
    }

    [Fact]
    public void RoundTrip_TabGroupWithDocuments()
    {
        var tabs = new DockTabGroup(
            new DockableContent[]
            {
                new Document { Title = "T1", Key = "t1" },
                new Document { Title = "T2", Key = "t2" },
                new Document { Title = "T3", Key = "t3" },
            },
            TabPosition: TabPosition.Bottom,
            CompactTabs: true,
            SelectedIndex: 1);

        var json = DockLayoutSerializer.Save(tabs);
        var result = DockLayoutSerializer.Load(json);

        var loaded = Assert.IsType<DockTabGroup>(result.Root);
        Assert.Equal(3, loaded.Documents.Count);
        Assert.Equal(TabPosition.Bottom, loaded.TabPosition);
        Assert.True(loaded.CompactTabs);
        Assert.Equal(1, loaded.SelectedIndex);
    }

    // ── TabChrome (spec 045 §4.6) ──────────────────────────────────────

    [Theory]
    [InlineData(TabChrome.Win11)]
    [InlineData(TabChrome.Flat)]
    [InlineData(TabChrome.TitleBar)]
    public void RoundTrip_TabChrome(TabChrome chrome)
    {
        var tabs = new DockTabGroup(
            new DockableContent[] { new Document { Title = "T1", Key = "t1" } },
            TabChrome: chrome);

        var json = DockLayoutSerializer.Save(tabs);
        var loaded = Assert.IsType<DockTabGroup>(DockLayoutSerializer.Load(json).Root);
        Assert.Equal(chrome, loaded.TabChrome);
    }

    [Fact]
    public void Save_OmitsTabChromeFieldWhenDefault()
    {
        // §4.6 — Win11 is the back-compat default; serializer keeps JSON
        // small + legacy files untouched by skipping the field on default.
        var tabs = new DockTabGroup(
            new DockableContent[] { new Document { Title = "T1", Key = "t1" } });

        var json = DockLayoutSerializer.Save(tabs);
        Assert.DoesNotContain("tabChrome", json);
    }

    [Fact]
    public void Save_EmitsTabChromeFieldWhenNonDefault()
    {
        var tabs = new DockTabGroup(
            new DockableContent[] { new Document { Title = "T1", Key = "t1" } },
            TabChrome: TabChrome.Flat);

        var json = DockLayoutSerializer.Save(tabs);
        Assert.Contains("\"tabChrome\":\"flat\"", json);
    }

    [Fact]
    public void Load_LegacyJsonWithoutTabChrome_DefaultsToWin11()
    {
        // §4.6 back-compat: layouts written before TabChrome must still
        // load with the default chrome. Builds a synthetic JSON without
        // the field to lock in the contract.
        var json = """
        {
          "$schema": 2,
          "root": {
            "kind": "tabGroup",
            "documents": [ { "title": "T1", "key": "t1", "role": "document" } ],
            "tabPosition": "top"
          }
        }
        """;
        var loaded = Assert.IsType<DockTabGroup>(DockLayoutSerializer.Load(json).Root);
        Assert.Equal(TabChrome.Win11, loaded.TabChrome);
    }

    [Fact]
    public void Load_UnknownTabChrome_FailsValidationToFallback()
    {
        // Load() wraps validation errors into a fallback result rather
        // than throwing, so callers can recover layout gracefully (§2.7).
        // We still want the contract: unknown TabChrome strings are
        // not silently coerced — the load is reported as a failure with
        // a "tabChrome" mention in the diagnostic message.
        var json = """
        {
          "$schema": 2,
          "root": {
            "kind": "tabGroup",
            "documents": [ { "title": "T1", "key": "t1", "role": "document" } ],
            "tabPosition": "top",
            "tabChrome": "neon-pink"
          }
        }
        """;
        var result = DockLayoutSerializer.Load(json);
        Assert.True(result.IsFallback);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("tabChrome", result.FailureReason);
    }

    [Fact]
    public void RoundTrip_Sides()
    {
        var leftTw = new ToolWindow { Title = "SE", Key = "se" };
        var bottomTw = new ToolWindow { Title = "Errors", Key = "err" };
        var json = DockLayoutSerializer.Save(
            root: null,
            leftSide: new[] { leftTw },
            bottomSide: new[] { bottomTw });

        var result = DockLayoutSerializer.Load(json);
        Assert.True(result.Success);
        Assert.Single(result.LeftSide);
        Assert.Single(result.BottomSide);
        Assert.Equal("se", result.LeftSide[0].Key);
        Assert.Equal("err", result.BottomSide[0].Key);
    }

    [Fact]
    public void RoundTrip_FloatingWindows()
    {
        var floatPane = new Document { Title = "F", Key = "f" };
        var fw = new FloatingDockWindow
        {
            Id = "fw1",
            X = 100, Y = 200, Width = 800, Height = 600,
            Contents = new DockableContent[] { floatPane },
        };
        var json = DockLayoutSerializer.Save(root: null, floating: new[] { fw });

        var result = DockLayoutSerializer.Load(json);
        Assert.Single(result.Floating);
        var loaded = result.Floating[0];
        Assert.Equal("fw1", loaded.Id);
        Assert.Equal(100, loaded.X);
        Assert.Equal(200, loaded.Y);
        Assert.Equal(800, loaded.Width);
        Assert.Equal(600, loaded.Height);
        Assert.Single(loaded.Contents);
    }

    [Fact]
    public void RoundTrip_ActiveKey()
    {
        var json = DockLayoutSerializer.Save(root: null, activeKey: "active-pane-id");
        var result = DockLayoutSerializer.Load(json);
        Assert.Equal("active-pane-id", result.ActiveKey);
    }

    [Fact]
    public void RoundTrip_PerPanePersistenceState()
    {
        var doc = new Document
        {
            Title = "X",
            Key = "x",
            PersistenceState = """{"scrollOffset":1024}""",
        };
        var json = DockLayoutSerializer.Save(doc);
        var result = DockLayoutSerializer.Load(json);
        var loaded = Assert.IsType<Document>(result.Root);
        Assert.Equal("""{"scrollOffset":1024}""", loaded.PersistenceState);
    }

    // ── Permission override emission ────────────────────────────────────

    [Fact]
    public void Save_OmitsDefaultPermissionFlags()
    {
        var doc = new Document { Title = "X", Key = "x" }; // all defaults
        var json = DockLayoutSerializer.Save(doc);

        // The pane should not emit canClose=true (since that's the Document
        // default), nor canFloat/canMove (true is the base default).
        Assert.DoesNotContain("\"canClose\"", json);
        Assert.DoesNotContain("\"canFloat\"", json);
        Assert.DoesNotContain("\"canMove\"", json);
        Assert.DoesNotContain("\"canPin\"", json);
    }

    [Fact]
    public void Save_EmitsNonDefaultPermissionFlags()
    {
        var tw = new ToolWindow
        {
            Title = "X", Key = "x",
            CanHide = false,
            CanAutoHide = false,
            CanFloat = false,
        };
        var json = DockLayoutSerializer.Save(tw);
        Assert.Contains("\"canHide\":false", json);
        Assert.Contains("\"canAutoHide\":false", json);
        Assert.Contains("\"canFloat\":false", json);
    }

    // ── Security limits (spec §8.9) ─────────────────────────────────────

    [Fact]
    public void Load_OversizeInput_FallsBack()
    {
        // 1 MB + 1 byte
        var oversized = new string('x', DockLayoutSerializer.MaxBytes + 1);
        var result = DockLayoutSerializer.Load(oversized);

        Assert.True(result.IsFallback);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("byte limit", result.FailureReason);
    }

    [Fact]
    public void Load_ExcessivelyDeepNesting_FallsBack()
    {
        // Build > MaxDepth nested splits.
        var sb = new global::System.Text.StringBuilder();
        var depth = DockLayoutSerializer.MaxDepth + 5;
        sb.Append("""{"$schema":2,"root":""");
        for (int i = 0; i < depth; i++)
            sb.Append("""{"kind":"split","orientation":"horizontal","children":[""");
        for (int i = 0; i < depth; i++) sb.Append("]}");
        sb.Append("}");

        var result = DockLayoutSerializer.Load(sb.ToString());
        Assert.True(result.IsFallback);
    }

    [Fact]
    public void Load_CorruptJson_FallsBack_NoThrow()
    {
        var result = DockLayoutSerializer.Load("{ this is { broken } not json");
        Assert.True(result.IsFallback);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void Load_EmptyInput_FallsBack()
    {
        Assert.True(DockLayoutSerializer.Load(null).IsFallback);
        Assert.True(DockLayoutSerializer.Load("").IsFallback);
        Assert.True(DockLayoutSerializer.Load("   ").IsFallback);
    }

    [Fact]
    public void Load_MissingSchemaField_TreatedAsV1AndMigrated()
    {
        // §5.4.4 — absent `$schema` is the convention for P1's vendored
        // save format. After integrating the migration registry into
        // Load (review Fix #9), an empty object is interpreted as a
        // v1 layout that migrates to v2 with no panes.
        var result = DockLayoutSerializer.Load("{}");
        Assert.True(result.Success);
        Assert.False(result.IsFallback);
        Assert.Equal(2, result.Schema);
        Assert.Null(result.Root);
    }

    [Fact]
    public void Load_UnknownNodeKind_FallsBack()
    {
        var json = """
            {"$schema":2,"root":{"kind":"someFutureKind"}}
            """;
        var result = DockLayoutSerializer.Load(json);
        Assert.True(result.IsFallback);
    }

    // ── Invariant culture (spec §8.8) ──────────────────────────────────

    [Fact]
    public void RoundTrip_InvariantCulture_AcrossDifferentLocales()
    {
        var split = new DockSplit(
            Orientation.Horizontal,
            new DockNode[] { new Document { Title = "A", Key = "a", Width = 1234.56 } });

        // Save under de-DE (comma decimal separator).
        var prevCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var json = DockLayoutSerializer.Save(split);

            // System.Text.Json defaults to invariant culture — the file must
            // contain the period decimal separator regardless of the current
            // thread culture.
            Assert.Contains("1234.56", json);
            Assert.DoesNotContain("1234,56", json);

            // Now load under en-US (period decimal separator).
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            var result = DockLayoutSerializer.Load(json);
            Assert.True(result.Success);
            var rootSplit = Assert.IsType<DockSplit>(result.Root);
            var leaf = Assert.IsType<Document>(rootSplit.Children[0]);
            Assert.Equal(1234.56, leaf.Width);
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
        }
    }

    // ── Migration ladder ───────────────────────────────────────────────

    [Fact]
    public void Registry_DetectSchema_DefaultsToOneWhenAbsent()
    {
        var v1 = JsonNode.Parse("""{"title":"X"}""")!;
        Assert.Equal(1, DockLayoutMigrationRegistry.DetectSchema(v1));
    }

    [Fact]
    public void Registry_DetectSchema_ReadsExplicit()
    {
        var v2 = JsonNode.Parse("""{"$schema":2}""")!;
        Assert.Equal(2, DockLayoutMigrationRegistry.DetectSchema(v2));
    }

    [Fact]
    public void Registry_BuiltInV1ToV2_StampsSchema()
    {
        var reg = new DockLayoutMigrationRegistry();
        var v1 = JsonNode.Parse("""
            {"root":{"kind":"pane","pane":{"title":"X"}}}
            """)!;

        var ok = reg.TryUpgrade(v1, fromVersion: 1, toVersion: 2, out var migrated, out var reason);
        Assert.True(ok);
        Assert.Null(reason);
        Assert.NotNull(migrated);
        Assert.Equal(2, migrated["$schema"]!.GetValue<int>());
    }

    [Fact]
    public void Registry_BuiltInV1ToV2_SynthesizesKeysFromTitles()
    {
        var reg = new DockLayoutMigrationRegistry();
        var v1 = JsonNode.Parse("""
            {"root":{"kind":"pane","pane":{"title":"MainView"}}}
            """)!;

        reg.TryUpgrade(v1, 1, 2, out var migrated, out _);
        Assert.NotNull(migrated);
        var paneKey = migrated["root"]!["pane"]!["key"]!.GetValue<string>();
        Assert.Equal("MainView", paneKey);
    }

    [Fact]
    public void Registry_NoMigrationForRequestedFrom_Fails()
    {
        var reg = new DockLayoutMigrationRegistry(includeBuiltins: false);
        var node = JsonNode.Parse("""{"k":"v"}""")!;
        var ok = reg.TryUpgrade(node, 1, 2, out _, out var reason);
        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.Contains("no migration registered", reason);
    }

    [Fact]
    public void Registry_NewerThanTarget_ForwardTolerant()
    {
        var reg = new DockLayoutMigrationRegistry();
        var node = JsonNode.Parse("""{"$schema":99}""")!;
        var ok = reg.TryUpgrade(node, 99, 2, out var migrated, out var reason);

        // Spec §8.11: forward-tolerant — log a warning, accept best-effort.
        Assert.True(ok);
        Assert.NotNull(reason);
        Assert.Contains("newer", reason);
        Assert.Same(node, migrated);
    }

    [Fact]
    public void Registry_SameVersion_ShortCircuits()
    {
        var reg = new DockLayoutMigrationRegistry();
        var node = JsonNode.Parse("""{"$schema":2}""")!;
        var ok = reg.TryUpgrade(node, 2, 2, out var migrated, out _);
        Assert.True(ok);
        Assert.Same(node, migrated);
    }

    [Fact]
    public void Registry_CustomMigrationStacksOnLadder()
    {
        // App registers v2→v3 on top of built-in v1→v2; the ladder chains.
        var reg = new DockLayoutMigrationRegistry()
            .Add(new V2ToV3Stamp());

        var v1 = JsonNode.Parse("""
            {"root":{"kind":"pane","pane":{"title":"X"}}}
            """)!;
        reg.TryUpgrade(v1, 1, 3, out var migrated, out _);
        Assert.NotNull(migrated);
        Assert.Equal(3, migrated["$schema"]!.GetValue<int>());
        Assert.Equal("stamped", migrated["v3marker"]!.GetValue<string>());
    }

    private sealed class V2ToV3Stamp : IDockLayoutMigration
    {
        public int FromVersion => 2;
        public int ToVersion   => 3;
        public JsonNode Migrate(JsonNode root)
        {
            var obj = root.AsObject();
            obj["$schema"] = 3;
            obj["v3marker"] = "stamped";
            return obj;
        }
    }

    // ── Load integration: actual `Load` path runs the migration ladder ──

    [Fact]
    public void Load_V1NoSchema_SynthesizesKeysAndUpgrades()
    {
        // P1's wrapper saved layouts without a $schema marker and used
        // `title` as the persisted key. The Load path must route those
        // through the built-in v1→v2 migration before deserialization
        // (§5.4.4), so a v1 file produces a usable result with keys
        // synthesized from titles.
        var v1Json = """
            {
              "root": {
                "kind": "pane",
                "pane": { "title": "MainView", "role": "document" }
              }
            }
            """;
        var result = DockLayoutSerializer.Load(v1Json);

        Assert.True(result.Success, $"expected success, got failure: {result.FailureReason}");
        Assert.Equal(2, result.Schema);
        var doc = Assert.IsType<Document>(result.Root);
        Assert.Equal("MainView", doc.Title);
        Assert.Equal("MainView", doc.Key);
    }

    [Fact]
    public void Load_SchemaNewerThanCurrent_EmitsForwardToleranceCategory()
    {
        // §8.11 — newer-than-known schemas log a category but still
        // return a best-effort result. The event has to land via the
        // actual Load path (the registry exposes the warning, but it's
        // the serializer that emits the ETW event).
        var futureJson = """
            { "$schema": 99, "root": null }
            """;
        using var listener = new DockingFallbackListener();
        listener.EnableEvents(ReactorEventSource.Log, EventLevel.Warning, EventKeywords.All);

        var result = DockLayoutSerializer.Load(futureJson);

        Assert.True(result.Success);
        Assert.Contains("schema-newer", listener.WaitForCategory("schema-newer"));
    }

    [Fact]
    public void Load_V0Schema_FallsBackWithNoMigration()
    {
        // A schema=0 marker can't be migrated (no v0→v2 registered) and
        // is treated as a hard failure rather than being silently passed
        // through as v0.
        var v0Json = """{ "$schema": 0 }""";
        var result = DockLayoutSerializer.Load(v0Json);

        Assert.True(result.IsFallback);
        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
    }

    // ── Load latency budget (spec §8.1 — ≤ 50 ms for 200 panes) ────────

    [Fact]
    public void Load_TwoHundredPanes_UnderFiftyMilliseconds()
    {
        // Build a 200-pane tree as a single big tab group. The benchmark
        // budget in spec §8.1 is 50 ms; this xUnit check is a regression
        // guard, not a microbench — give it some slack but still catch
        // O(n²) regressions.
        var panes = Enumerable.Range(0, 200)
            .Select(i => (DockableContent)new Document { Title = $"P{i}", Key = $"p{i}" })
            .ToList();
        var tabs = new DockTabGroup(panes);
        var json = DockLayoutSerializer.Save(tabs);

        var sw = global::System.Diagnostics.Stopwatch.StartNew();
        var result = DockLayoutSerializer.Load(json);
        sw.Stop();

        Assert.True(result.Success);
        Assert.True(sw.ElapsedMilliseconds < 250,
            $"200-pane load took {sw.ElapsedMilliseconds}ms — well above the 50ms perf budget (test threshold 250ms to absorb CI jitter; perf bench enforces the actual budget)");
    }

    // ── §2.7 corrupt-JSON ReactorEventSource emission ────────────────────

    private sealed class DockingFallbackListener : EventListener
    {
        private readonly List<string> _categories = new();
        public IReadOnlyList<string> Categories
        {
            get { lock (_categories) return _categories.ToArray(); }
        }

        /// <summary>
        /// Waits for at least one category matching <paramref name="expected"/>
        /// to land before returning the current snapshot. EventListener
        /// dispatch is normally synchronous for in-process EventSource,
        /// but a tight retry absorbs CI environments where dispatch is
        /// queued on a background thread.
        /// </summary>
        public IReadOnlyList<string> WaitForCategory(string expected, int timeoutMs = 250)
        {
            var sw = global::System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var snapshot = Categories;
                if (snapshot.Contains(expected)) return snapshot;
                global::System.Threading.Thread.Sleep(5);
            }
            return Categories;
        }

        protected override void OnEventWritten(EventWrittenEventArgs e)
        {
            if (e.EventName != nameof(ReactorEventSource.DockingLayoutLoadFallback)) return;
            var payload = e.Payload is { Count: > 0 } ? e.Payload[0]?.ToString() ?? string.Empty : string.Empty;
            lock (_categories) _categories.Add(payload);
        }
    }

    [Theory]
    [InlineData("",                                  "empty")]
    [InlineData("    \t\n  ",                        "empty")]
    [InlineData("{not valid json",                   "json-parse")]
    [InlineData("null",                              "null-document")]
    [InlineData("{\"$schema\": 0}",                  "schema-missing")]
    public void Load_CorruptInput_EmitsReactorEventSourceFallback(string json, string expectedCategory)
    {
        // Spec 045 §2.7 / §8.10 — corrupt JSON must (a) return a fallback
        // result, (b) emit a Microsoft-UI-Reactor ETW event with a
        // PII-safe coarse category. The in-process FailureReason still
        // carries the full message for app-level diagnostics.
        using var listener = new DockingFallbackListener();
        listener.EnableEvents(ReactorEventSource.Log, EventLevel.Warning, EventKeywords.All);

        var result = DockLayoutSerializer.Load(json);

        Assert.True(result.IsFallback);
        Assert.False(result.Success);
        Assert.NotNull(result.FailureReason);
        Assert.Contains(expectedCategory, listener.WaitForCategory(expectedCategory));
    }

    [Fact]
    public void Load_OversizeInput_EmitsOversizeCategory()
    {
        // Craft a string just above the 1 MB ceiling. The exact bytes
        // matter only to trip the boundary check; payload content is
        // irrelevant because the size gate fires before parsing.
        var oversize = new string('a', DockLayoutSerializer.MaxBytes + 8);
        using var listener = new DockingFallbackListener();
        listener.EnableEvents(ReactorEventSource.Log, EventLevel.Warning, EventKeywords.All);

        var result = DockLayoutSerializer.Load(oversize);

        Assert.True(result.IsFallback);
        Assert.Contains("oversize", listener.WaitForCategory("oversize"));
    }

    [Fact]
    public void Load_ValidInput_EmitsNoFallbackEvent()
    {
        // The success path must stay silent on the ReactorEventSource
        // fallback channel — only failures emit. Regression guard against
        // accidentally inverting the Fail() vs success branches.
        var json = DockLayoutSerializer.Save(new DockTabGroup(
            new DockableContent[] { new("X", Key: "x") }));
        using var listener = new DockingFallbackListener();
        listener.EnableEvents(ReactorEventSource.Log, EventLevel.Warning, EventKeywords.All);

        var result = DockLayoutSerializer.Load(json);

        Assert.True(result.Success);
        Assert.Empty(listener.Categories);
    }
}
