# IDockLayoutMigration

Layout-JSON schema migration step. Registered with the layout-migration registry exposed on [`DockManager`](../elements/docking.md#dockmanager) (Phase 2 wiring); the loader resolves an ordered ladder (`FromVersion`→`ToVersion`, applied sequentially) when a persisted layout is loaded under a newer schema target.

```csharp
public interface IDockLayoutMigration
```

Namespace: `Microsoft.UI.Reactor.Docking`. Package: `Microsoft.UI.Reactor.Advanced`.

**Learn more:** [Docking Windows](../../docking.md)

## Remarks

Spec 045 §5.3.4 / §5.4 / §8.11 (Phase 2 addition).

Backward read-compat: v1 readable forever; future v3+ readable through all future versions via the migration ladder. Forward-tolerance: newer-than-known schemas log a warning, best-effort parse what they understand, fall back to default for unknown nodes.

## Properties

### `FromVersion`

Schema version this migration accepts as input.

```csharp
public abstract int FromVersion  // get
```

### `ToVersion`

Schema version this migration emits.

```csharp
public abstract int ToVersion  // get
```

## Methods

### `Migrate`

Transforms the JSON tree in place (or returns a new root) advancing from [`IDockLayoutMigration.FromVersion`](#fromversion) to [`IDockLayoutMigration.ToVersion`](#toversion).

```csharp
public JsonNode Migrate(JsonNode root)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | `JsonNode` | The layout JSON's root node. |

**Returns:** The migrated root (may be the same instance).
