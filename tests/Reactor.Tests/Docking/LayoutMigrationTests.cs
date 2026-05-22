using System;
using System.Text.Json.Nodes;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Persistence;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Tests for the <see cref="IDockLayoutMigration"/> contract exercised
/// through the production <see cref="DockLayoutMigrationRegistry"/> —
/// covers the built-in v1→v2 step that the loader applies when a
/// persisted layout's <c>$schema</c> is older than the current target.
/// Spec 045 §5.3.4, §5.4.4, §8.11; tracking §2.11.
/// </summary>
public class LayoutMigrationTests
{
    [Fact]
    public void DefaultRegistry_HasBuiltinV1ToV2_StampsSchemaAndPreservesTitle()
    {
        var registry = new DockLayoutMigrationRegistry(); // built-in v1→v2 pre-registered
        var v1 = JsonNode.Parse("""{"title":"X","root":null}""")!;

        var ok = registry.TryUpgrade(v1, fromVersion: 1, toVersion: 2,
            out var migrated, out var failureReason);

        Assert.True(ok);
        Assert.Null(failureReason);
        Assert.NotNull(migrated);
        Assert.Equal(2, migrated!["$schema"]?.GetValue<int>());
        Assert.Equal("X", migrated["title"]?.GetValue<string>());
    }

    [Fact]
    public void DefaultRegistry_V1ToV2_SynthesizesKeyFromTitleOnPaneLeaves()
    {
        var registry = new DockLayoutMigrationRegistry();
        // P1 wrapper saved panes without explicit keys — v1→v2 must
        // synthesize "key" from "title" so the v2 reload path can use
        // key-based pane resolution.
        var v1 = JsonNode.Parse("""
            {
              "title":"layout",
              "root":{ "kind":"pane", "pane":{ "title":"Solution Explorer" } }
            }
            """)!;

        Assert.True(registry.TryUpgrade(v1, 1, 2, out var migrated, out _));
        var pane = migrated!["root"]!["pane"]!;
        Assert.Equal("Solution Explorer", pane["key"]?.GetValue<string>());
    }

    [Fact]
    public void DefaultRegistry_V1ToV2_DropsLegacyVersionField()
    {
        var registry = new DockLayoutMigrationRegistry();
        // Some P1 builds wrote a "version" field. v2 uses "$schema" — the
        // legacy field must be removed (otherwise the JSON shape carries
        // two version markers, one of which will drift).
        var v1 = JsonNode.Parse("""{"version":1,"title":"X"}""")!;

        Assert.True(registry.TryUpgrade(v1, 1, 2, out var migrated, out _));
        Assert.Null(migrated!["version"]);
        Assert.Equal(2, migrated["$schema"]?.GetValue<int>());
    }

    [Fact]
    public void Registry_NoMigrationForUnknownStartVersion_FailsWithReason()
    {
        var registry = new DockLayoutMigrationRegistry();
        var stranded = JsonNode.Parse("""{"$schema":0}""")!;

        var ok = registry.TryUpgrade(stranded, fromVersion: 0, toVersion: 2,
            out var migrated, out var failureReason);

        Assert.False(ok);
        Assert.Null(migrated);
        Assert.NotNull(failureReason);
        Assert.Contains("v0", failureReason);
    }

    [Fact]
    public void Registry_NewerSchemaThanTarget_AcceptsAsForwardTolerant()
    {
        // §8.11 forward tolerance: a v3 file under a v2 loader is read
        // best-effort with a diagnostic message.
        var registry = new DockLayoutMigrationRegistry();
        var v3 = JsonNode.Parse("""{"$schema":3,"title":"future"}""")!;

        var ok = registry.TryUpgrade(v3, fromVersion: 3, toVersion: 2,
            out var migrated, out var diagnostic);

        Assert.True(ok);
        Assert.NotNull(migrated);
        Assert.NotNull(diagnostic);
        Assert.Contains("newer", diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Registry_AppRegisteredStep_ChainsAfterBuiltins()
    {
        var registry = new DockLayoutMigrationRegistry();
        // App contributes a v2→v3 step. The ladder applies built-in
        // v1→v2 first, then the app's step, returning a v3 result.
        registry.Add(new V2ToV3Stamper());

        var v1 = JsonNode.Parse("""{"title":"X"}""")!;
        Assert.True(registry.TryUpgrade(v1, 1, 3, out var migrated, out _));
        Assert.Equal(3, migrated!["$schema"]?.GetValue<int>());
        Assert.True(migrated["stampedByV2ToV3"]?.GetValue<bool>() ?? false);
    }

    [Fact]
    public void DetectSchema_NoMarker_DefaultsToV1()
    {
        // §5.4.4 — P1 wrapper files lacked $schema; treat as v1.
        var phase1 = JsonNode.Parse("""{"title":"phase-1"}""")!;
        Assert.Equal(1, DockLayoutMigrationRegistry.DetectSchema(phase1));
    }

    [Fact]
    public void DetectSchema_ExplicitMarker_ReturnsValue()
    {
        var v2 = JsonNode.Parse("""{"$schema":2}""")!;
        Assert.Equal(2, DockLayoutMigrationRegistry.DetectSchema(v2));
    }

    private sealed class V2ToV3Stamper : IDockLayoutMigration
    {
        public int FromVersion => 2;
        public int ToVersion   => 3;
        public JsonNode Migrate(JsonNode root)
        {
            var obj = root.AsObject();
            obj["$schema"] = 3;
            obj["stampedByV2ToV3"] = true;
            return obj;
        }
    }
}
