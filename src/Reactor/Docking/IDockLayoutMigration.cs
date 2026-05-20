using System.Text.Json.Nodes;

namespace Microsoft.UI.Reactor.Docking;

/// <summary>
/// Layout-JSON schema migration step. Registered with the layout-migration
/// registry exposed on <see cref="DockManager"/> (Phase 2 wiring); the
/// loader resolves an ordered ladder
/// (<c>FromVersion</c>→<c>ToVersion</c>, applied sequentially) when a
/// persisted layout is loaded under a newer schema target.
/// </summary>
/// <remarks>
/// Spec 045 §5.3.4 / §5.4 / §8.11 (Phase 2 addition).
///
/// <para>
/// Backward read-compat: v1 readable forever; future v3+ readable through
/// all future versions via the migration ladder. Forward-tolerance:
/// newer-than-known schemas log a warning, best-effort parse what they
/// understand, fall back to default for unknown nodes.
/// </para>
/// </remarks>
public interface IDockLayoutMigration
{
    /// <summary>Schema version this migration accepts as input.</summary>
    int FromVersion { get; }

    /// <summary>Schema version this migration emits.</summary>
    int ToVersion { get; }

    /// <summary>
    /// Transforms the JSON tree in place (or returns a new root) advancing
    /// from <see cref="FromVersion"/> to <see cref="ToVersion"/>.
    /// </summary>
    /// <param name="root">The layout JSON's root node.</param>
    /// <returns>The migrated root (may be the same instance).</returns>
    JsonNode Migrate(JsonNode root);
}
