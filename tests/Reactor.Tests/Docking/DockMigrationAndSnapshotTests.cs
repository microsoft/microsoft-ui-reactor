using System.Text.Json.Nodes;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Docking.Persistence;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Spec 045 §5.4.4 / §8.11 + §2.26 — extra coverage for the migration
/// registry, snapshot builder, and DockHooks key-construction helper that
/// existing tests skim.
/// </summary>
public sealed class DockMigrationAndSnapshotTests
{
    // ── DockLayoutMigrationRegistry ─────────────────────────────────────

    [Fact]
    public void DetectSchema_MissingField_DefaultsToOne()
    {
        var root = JsonNode.Parse("{}")!;
        Assert.Equal(1, DockLayoutMigrationRegistry.DetectSchema(root));
    }

    [Fact]
    public void DetectSchema_ReadsExplicitVersion()
    {
        var root = JsonNode.Parse("{\"$schema\":3}")!;
        Assert.Equal(3, DockLayoutMigrationRegistry.DetectSchema(root));
    }

    [Fact]
    public void DetectSchema_NullNode_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => DockLayoutMigrationRegistry.DetectSchema(null!));
    }

    [Fact]
    public void TryUpgrade_SameVersion_PassesThrough()
    {
        var registry = new DockLayoutMigrationRegistry();
        var root = JsonNode.Parse("{\"$schema\":2}")!;
        Assert.True(registry.TryUpgrade(root, 2, 2, out var migrated, out var reason));
        Assert.Same(root, migrated);
        Assert.Null(reason);
    }

    [Fact]
    public void TryUpgrade_NewerSchemaThanTarget_ForwardTolerant()
    {
        var registry = new DockLayoutMigrationRegistry();
        var root = JsonNode.Parse("{\"$schema\":99}")!;
        Assert.True(registry.TryUpgrade(root, 99, 2, out var migrated, out var reason));
        Assert.Same(root, migrated);
        Assert.NotNull(reason); // "newer than loader target; best-effort"
    }

    [Fact]
    public void TryUpgrade_NoMigrationRegistered_ReportsFailure()
    {
        // Disable builtins so the v1→v2 step isn't there.
        var registry = new DockLayoutMigrationRegistry(includeBuiltins: false);
        var root = JsonNode.Parse("{}")!;
        Assert.False(registry.TryUpgrade(root, fromVersion: 1, toVersion: 2,
            out var migrated, out var reason));
        Assert.Null(migrated);
        Assert.NotNull(reason);
        Assert.Contains("no migration", reason);
    }

    [Fact]
    public void TryUpgrade_BuiltinV1ToV2_SynthesizesKeyFromTitle()
    {
        var registry = new DockLayoutMigrationRegistry();
        // P1 vendored format — has "version" instead of "$schema", panes
        // with title-only keys.
        var json = """
        {
            "version": 1,
            "root": {
                "kind": "tabgroup",
                "documents": [
                    { "title": "Output" },
                    { "title": "Errors", "key": "explicit-key" }
                ]
            },
            "leftSide": [ { "title": "Solution Explorer" } ]
        }
        """;
        var root = JsonNode.Parse(json)!;
        Assert.True(registry.TryUpgrade(root, 1, 2, out var migrated, out var reason));
        Assert.NotNull(migrated);
        Assert.Null(reason);

        var migratedObj = (JsonObject)migrated!;
        Assert.Equal(2, migratedObj["$schema"]!.GetValue<int>());
        Assert.Null(migratedObj["version"]); // renamed away

        // First doc had no key → synthesized from title.
        var docs = (JsonArray)migratedObj["root"]!["documents"]!;
        Assert.Equal("Output", docs[0]!["key"]!.GetValue<string>());
        // Second doc had an explicit key — preserved.
        Assert.Equal("explicit-key", docs[1]!["key"]!.GetValue<string>());

        // Side pane key also synthesized.
        var left = (JsonArray)migratedObj["leftSide"]!;
        Assert.Equal("Solution Explorer", left[0]!["key"]!.GetValue<string>());
    }

    [Fact]
    public void TryUpgrade_NullRoot_Throws()
    {
        var registry = new DockLayoutMigrationRegistry();
        Assert.Throws<ArgumentNullException>(
            () => registry.TryUpgrade(null!, 1, 2, out _, out _));
    }

    [Fact]
    public void Add_Null_Throws()
    {
        var registry = new DockLayoutMigrationRegistry();
        Assert.Throws<ArgumentNullException>(() => registry.Add(null!));
    }

    // ── DockSnapshotBuilder ─────────────────────────────────────────────

    [Fact]
    public void FromManager_NullManager_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => DockSnapshotBuilder.FromManager(null!));
    }

    [Fact]
    public void FromManager_EmptyManager_ProducesEmptySnapshot()
    {
        var snap = DockSnapshotBuilder.FromManager(new DockManager());
        Assert.Equal(string.Empty, snap.HostId);
        Assert.Null(snap.Root);
        Assert.Empty(snap.LeftSide);
        Assert.Empty(snap.TopSide);
        Assert.Empty(snap.RightSide);
        Assert.Empty(snap.BottomSide);
        Assert.Null(snap.ActiveKey);
    }

    [Fact]
    public void FromManager_RoleDiscrimination_DocumentVsToolWindowVsContent()
    {
        var doc = new Document { Title = "Doc", Key = "d" };
        var tw = new ToolWindow { Title = "TW", Key = "t" };
        var bare = new DockableContent("Bare", Key: "b");
        var manager = new DockManager
        {
            Layout = new DockSplit(Orientation.Horizontal, new DockNode[]
            {
                new DockTabGroup(new DockableContent[] { doc, tw, bare }),
            }),
        };
        var snap = DockSnapshotBuilder.FromManager(manager);
        var split = Assert.IsType<DockSnapshotSplit>(snap.Root);
        Assert.Equal("Horizontal", split.Orientation);
        var grp = Assert.IsType<DockSnapshotTabGroup>(split.Children[0]);
        Assert.Equal("document", grp.Documents[0].Role);
        Assert.Equal("toolwindow", grp.Documents[1].Role);
        Assert.Equal("content", grp.Documents[2].Role);
    }

    [Fact]
    public void FromManager_BareLeafRoot_ProducesLeafNode()
    {
        var doc = new Document { Title = "Solo", Key = "k" };
        var manager = new DockManager { Layout = doc };
        var snap = DockSnapshotBuilder.FromManager(manager);
        var leaf = Assert.IsType<DockSnapshotLeaf>(snap.Root);
        Assert.Equal("k", leaf.Pane.Key);
        Assert.Equal("Solo", leaf.Pane.Title);
    }

    [Fact]
    public void FromManager_ActiveDocument_StringifiesKey()
    {
        var doc = new Document { Title = "T", Key = 42 };
        var manager = new DockManager
        {
            Layout = new DockTabGroup(new DockableContent[] { doc }),
            ActiveDocument = doc,
        };
        var snap = DockSnapshotBuilder.FromManager(manager);
        Assert.Equal("42", snap.ActiveKey);
    }

    [Fact]
    public void FromManager_SideStrips_PopulateInSnapshot()
    {
        var l = new ToolWindow { Title = "Left", Key = "l" };
        var b = new ToolWindow { Title = "Bot", Key = "b" };
        var manager = new DockManager
        {
            LeftSide = new[] { l },
            BottomSide = new[] { b },
        };
        var snap = DockSnapshotBuilder.FromManager(manager);
        Assert.Single(snap.LeftSide);
        Assert.Equal("l", snap.LeftSide[0].Key);
        Assert.Single(snap.BottomSide);
        Assert.Equal("b", snap.BottomSide[0].Key);
        Assert.Empty(snap.RightSide);
        Assert.Empty(snap.TopSide);
    }

    [Fact]
    public void FromRecord_NullRecord_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => DockSnapshotBuilder.FromRecord(null!));
    }

    // ── DockHooks.BuildPersistedKey ─────────────────────────────────────

    [Fact]
    public void BuildPersistedKey_NullPaneKey_EncodesAsTypeNull()
    {
        var key = DockHooks.BuildPersistedKey(paneKey: null, userKey: "scrollOffset");
        Assert.Equal("pane:null|:scrollOffset", key);
    }

    [Fact]
    public void BuildPersistedKey_StringPaneKey_IncludesTypeFullName()
    {
        var key = DockHooks.BuildPersistedKey("k1", "scrollOffset");
        Assert.Contains("System.String", key);
        Assert.Contains("k1", key);
        Assert.Contains("scrollOffset", key);
    }

    [Fact]
    public void BuildPersistedKey_IntPaneKey_DifferentFromStringSameRepresentation()
    {
        // Spec §2.9 — two panes whose Keys stringify the same but differ in
        // type must get independent persisted slots. The encoding includes
        // the runtime type's FullName.
        var a = DockHooks.BuildPersistedKey(42, "x");
        var b = DockHooks.BuildPersistedKey("42", "x");
        Assert.NotEqual(a, b);
        Assert.Contains("Int32", a);
        Assert.Contains("String", b);
    }

    [Fact]
    public void BuildPersistedKey_SegmentsContainingDelimiters_AreEscaped()
    {
        var key = DockHooks.BuildPersistedKey("with:colons", "with|pipes");
        Assert.DoesNotContain("with:colons", key);
        Assert.DoesNotContain("with|pipes", key);
        Assert.Contains("%3A", key); // ':' escape
        Assert.Contains("%7C", key); // '|' escape
    }

    [Fact]
    public void BuildPersistedKey_PercentInSegment_EscapedFirst()
    {
        var key = DockHooks.BuildPersistedKey("pre%post", "u");
        // The raw '%' should be re-encoded so subsequent ':' / '|' escapes
        // don't get double-substituted.
        Assert.Contains("pre%25post", key);
    }
}
