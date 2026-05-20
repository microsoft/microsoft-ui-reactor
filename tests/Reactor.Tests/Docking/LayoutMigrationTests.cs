using System.Text.Json.Nodes;
using Microsoft.UI.Reactor.Docking;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Docking;

/// <summary>
/// Tests for <see cref="IDockLayoutMigration"/> — the layout JSON schema
/// migration step. Spec 045 §5.3.4, §5.4; tracking §2.11.
/// </summary>
public class LayoutMigrationTests
{
    [Fact]
    public void Migration_ExposesFromAndToVersion()
    {
        IDockLayoutMigration m = new V1ToV2Migration();
        Assert.Equal(1, m.FromVersion);
        Assert.Equal(2, m.ToVersion);
    }

    [Fact]
    public void Migration_TransformsRoot()
    {
        IDockLayoutMigration m = new V1ToV2Migration();
        var v1 = JsonNode.Parse("""{"version":1,"title":"X"}""")!;
        var v2 = m.Migrate(v1);

        Assert.NotNull(v2);
        Assert.Equal(2, v2["$schema"]?.GetValue<int>());
        Assert.Equal("X", v2["title"]?.GetValue<string>());
    }

    [Fact]
    public void Migration_CanReturnNewRoot()
    {
        // Migrations are free to return a new instance — registry mustn't
        // assume in-place mutation.
        IDockLayoutMigration m = new ReplacingMigration();
        var v1 = JsonNode.Parse("""{"k":"v"}""")!;
        var result = m.Migrate(v1);
        Assert.NotSame(v1, result);
    }

    private sealed class V1ToV2Migration : IDockLayoutMigration
    {
        public int FromVersion => 1;
        public int ToVersion   => 2;
        public JsonNode Migrate(JsonNode root)
        {
            // Translate the v1 "version":1 marker to v2's "$schema":2.
            var obj = root.AsObject();
            obj.Remove("version");
            obj["$schema"] = 2;
            return obj;
        }
    }

    private sealed class ReplacingMigration : IDockLayoutMigration
    {
        public int FromVersion => 99;
        public int ToVersion   => 100;
        public JsonNode Migrate(JsonNode root) =>
            JsonNode.Parse("""{"$schema":100,"replaced":true}""")!;
    }
}
