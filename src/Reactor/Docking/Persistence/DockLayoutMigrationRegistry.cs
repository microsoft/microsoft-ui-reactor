using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.UI.Reactor.Docking.Persistence;

/// <summary>
/// Ordered ladder of <see cref="IDockLayoutMigration"/> steps applied
/// when a persisted layout's <c>$schema</c> is older than the current
/// loader target. Spec 045 §5.3.4 / §5.4.4 / §8.11; tracking §2.11.
/// </summary>
/// <remarks>
/// Apps register migrations at startup with <see cref="Add"/>. The loader
/// resolves the shortest <c>fromVersion → currentSchema</c> chain by
/// walking <c>FromVersion → ToVersion</c> edges greedily; circular or
/// missing chains report a clean failure via <see cref="TryUpgrade"/>.
///
/// <para>
/// Phase 2 ships a built-in v1→v2 migration registered by default.
/// "v1" denotes the P1 wrapper's vendored save format (no <c>$schema</c>
/// field; <c>title</c>-as-key). The migration synthesizes <c>key</c>
/// fields from titles and sets <c>$schema = 2</c> (§5.4.4).
/// </para>
/// </remarks>
public sealed class DockLayoutMigrationRegistry
{
    private readonly List<IDockLayoutMigration> _migrations = new();

    /// <summary>Constructs a fresh registry with the built-in v1→v2 migration pre-registered.</summary>
    public DockLayoutMigrationRegistry() : this(includeBuiltins: true) { }

    /// <summary>Constructs a registry; <paramref name="includeBuiltins"/> toggles the v1→v2 default.</summary>
    public DockLayoutMigrationRegistry(bool includeBuiltins)
    {
        if (includeBuiltins) _migrations.Add(new BuiltinV1ToV2Migration());
    }

    /// <summary>Registers an additional migration step.</summary>
    public DockLayoutMigrationRegistry Add(IDockLayoutMigration migration)
    {
        ArgumentNullException.ThrowIfNull(migration);
        _migrations.Add(migration);
        return this;
    }

    /// <summary>
    /// Reads the schema marker from a persisted layout's root. Returns 1
    /// when no <c>$schema</c> field is present (the convention for P1's
    /// vendored format per spec §5.4.4).
    /// </summary>
    public static int DetectSchema(JsonNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root is JsonObject obj && obj.TryGetPropertyValue("$schema", out var schemaNode)
            && schemaNode is not null && schemaNode.GetValue<JsonElement>().TryGetInt32(out var v))
        {
            return v;
        }
        return 1; // §5.4.4 — phase-1 format has no $schema marker
    }

    /// <summary>
    /// Walks the ladder from <paramref name="fromVersion"/> up to
    /// <paramref name="toVersion"/>, applying each step in order. Returns
    /// the migrated root or null when the ladder can't reach the target.
    /// </summary>
    /// <param name="root">The layout JSON tree to migrate.</param>
    /// <param name="fromVersion">Detected source schema version.</param>
    /// <param name="toVersion">Target schema version.</param>
    /// <param name="migrated">Migrated root on success; null on failure.</param>
    /// <param name="failureReason">Diagnostic when migration fails.</param>
    public bool TryUpgrade(JsonNode root, int fromVersion, int toVersion, out JsonNode? migrated, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (fromVersion == toVersion)
        {
            migrated = root;
            failureReason = null;
            return true;
        }
        if (fromVersion > toVersion)
        {
            // Newer-than-known: spec §8.11 forward tolerance — the loader
            // logs a warning and best-effort parses what it understands.
            // Migration ladder is for upgrade-only; downgrade isn't a thing.
            migrated = root;
            failureReason = $"input schema {fromVersion} is newer than loader target {toVersion}; best-effort parse";
            return true; // forward-tolerant: still accept
        }

        var current = root;
        var currentVersion = fromVersion;
        var safetyCounter = 0;
        while (currentVersion < toVersion)
        {
            if (++safetyCounter > 64)
            {
                migrated = null;
                failureReason = $"migration ladder safety limit hit at v{currentVersion}";
                return false;
            }

            var step = _migrations.FirstOrDefault(m => m.FromVersion == currentVersion);
            if (step is null)
            {
                migrated = null;
                failureReason = $"no migration registered from v{currentVersion}";
                return false;
            }

            current = step.Migrate(current);
            currentVersion = step.ToVersion;
        }

        migrated = current;
        failureReason = null;
        return true;
    }

    /// <summary>
    /// Built-in v1→v2 migration. v1 is the P1 wrapper's vendored format
    /// (no <c>$schema</c> field; titles serve as keys). The migration sets
    /// <c>$schema = 2</c>, propagates a <c>key</c> equal to the v1 title
    /// when no explicit key is present, and renames "version"→"$schema"
    /// when the v1 file happened to have one.
    /// </summary>
    private sealed class BuiltinV1ToV2Migration : IDockLayoutMigration
    {
        public int FromVersion => 1;
        public int ToVersion   => 2;

        public JsonNode Migrate(JsonNode root)
        {
            if (root is JsonObject obj)
            {
                if (obj.TryGetPropertyValue("version", out _)) obj.Remove("version");
                obj["$schema"] = 2;

                // Walk pane leaves and ensure each has a "key" — infer from title.
                if (obj.TryGetPropertyValue("root", out var rootNode) && rootNode is not null)
                    SynthesizeKeys(rootNode);

                foreach (var side in new[] { "leftSide", "topSide", "rightSide", "bottomSide" })
                {
                    if (obj.TryGetPropertyValue(side, out var sideNode) && sideNode is JsonArray arr)
                    {
                        foreach (var pane in arr) if (pane is not null) SynthesizePaneKey(pane);
                    }
                }
            }
            return root;
        }

        private static void SynthesizeKeys(JsonNode node)
        {
            switch (node)
            {
                case JsonObject obj:
                    var kind = obj["kind"]?.GetValue<string>();
                    if (kind == "pane" && obj["pane"] is JsonObject pane) SynthesizePaneKey(pane);
                    if (obj["children"] is JsonArray children)
                        foreach (var child in children) if (child is not null) SynthesizeKeys(child);
                    if (obj["documents"] is JsonArray docs)
                        foreach (var doc in docs) if (doc is not null) SynthesizePaneKey(doc);
                    break;
            }
        }

        private static void SynthesizePaneKey(JsonNode pane)
        {
            if (pane is not JsonObject paneObj) return;
            if (paneObj["key"] is not null) return;
            // §5.4.4: "phase-1-format files (no $schema) infer keys from title."
            paneObj["key"] = paneObj["title"]?.GetValue<string>() ?? string.Empty;
        }
    }
}
