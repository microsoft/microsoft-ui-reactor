# Layout persistence

Save, load and migrate dock layouts.

**Learn more:** [Docking Windows](../../docking.md)

## `DockLayoutLoadResult`

Result envelope returned by [`DockLayoutSerializer.Load`](#loadstring). Always non-null; [`DockLayoutLoadResult.IsFallback`](#isfallback) indicates whether the input was usable.

```csharp
public sealed class DockLayoutLoadResult
```

Namespace: `Microsoft.UI.Reactor.Docking.Persistence`. Package: `Microsoft.UI.Reactor.Advanced`.

### Constructors

#### `DockLayoutLoadResult`

```csharp
public DockLayoutLoadResult()
```

### Properties

#### `ActiveKey`

Active pane key (stringified) at save time.

```csharp
public string? ActiveKey  // get, init
```

#### `BottomSide`

Parsed bottom-side ToolWindows.

```csharp
public IReadOnlyList<ToolWindow> BottomSide  // get, init
```

#### `FailureReason`

If [`DockLayoutLoadResult.IsFallback`](#isfallback), the reason — suitable for logging.

```csharp
public string? FailureReason  // get, init
```

#### `Floating`

Parsed floating windows.

```csharp
public IReadOnlyList<FloatingDockWindow> Floating  // get, init
```

#### `IsFallback`

True when the load failed and the caller should fall back to default.

```csharp
public bool IsFallback  // get, init
```

#### `LeftSide`

Parsed left-side ToolWindows.

```csharp
public IReadOnlyList<ToolWindow> LeftSide  // get, init
```

#### `RightSide`

Parsed right-side ToolWindows.

```csharp
public IReadOnlyList<ToolWindow> RightSide  // get, init
```

#### `Root`

Parsed root, or null if no root or fallback.

```csharp
public DockNode? Root  // get, init
```

#### `Schema`

Schema version detected in the input (0 if fallback).

```csharp
public int Schema  // get, init
```

#### `Success`

Whether the parse succeeded.

```csharp
public bool Success  // get, init
```

#### `TopSide`

Parsed top-side ToolWindows.

```csharp
public IReadOnlyList<ToolWindow> TopSide  // get, init
```

### Methods

#### `Fallback`

Constructs a fallback (failure) result with the given reason.

```csharp
public static DockLayoutLoadResult Fallback(string reason)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `reason` | `string` |  |

## `DockLayoutMigrationRegistry`

Ordered ladder of [`IDockLayoutMigration`](IDockLayoutMigration.md) steps applied when a persisted layout's `$schema` is older than the current loader target. Spec 045 §5.3.4 / §5.4.4 / §8.11; tracking §2.11.

```csharp
public sealed class DockLayoutMigrationRegistry
```

Namespace: `Microsoft.UI.Reactor.Docking.Persistence`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks

Apps register migrations at startup with [`DockLayoutMigrationRegistry.Add`](#add). The loader resolves the shortest `fromVersion → currentSchema` chain by walking `FromVersion → ToVersion` edges greedily; circular or missing chains report a clean failure via [`DockLayoutMigrationRegistry.TryUpgrade`](#tryupgrade).

Phase 2 ships a built-in v1→v2 migration registered by default. "v1" denotes the P1 wrapper's vendored save format (no `$schema` field; `title`-as-key). The migration synthesizes `key` fields from titles and sets `$schema = 2` (§5.4.4).

### Constructors

#### `DockLayoutMigrationRegistry`

##### `DockLayoutMigrationRegistry()`

Constructs a fresh registry with the built-in v1→v2 migration pre-registered.

```csharp
public DockLayoutMigrationRegistry()
```

##### `DockLayoutMigrationRegistry(bool)`

Constructs a registry; `includeBuiltins` toggles the v1→v2 default.

```csharp
public DockLayoutMigrationRegistry(bool includeBuiltins)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `includeBuiltins` | `bool` |  |

### Methods

#### `Add`

Registers an additional migration step.

```csharp
public DockLayoutMigrationRegistry Add(IDockLayoutMigration migration)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `migration` | [`IDockLayoutMigration`](IDockLayoutMigration.md) |  |

#### `DetectSchema`

Reads the schema marker from a persisted layout's root. Returns 1 when no `$schema` field is present (the convention for P1's vendored format per spec §5.4.4).

```csharp
public static int DetectSchema(JsonNode root)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | `JsonNode` |  |

#### `TryUpgrade`

Walks the ladder from `fromVersion` up to `toVersion`, applying each step in order. Returns the migrated root or null when the ladder can't reach the target.

```csharp
public bool TryUpgrade(JsonNode root, int fromVersion, int toVersion, out JsonNode? migrated, out string? failureReason)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `root` | `JsonNode` | The layout JSON tree to migrate. |
| `fromVersion` | `int` | Detected source schema version. |
| `toVersion` | `int` | Target schema version. |
| `migrated` | `JsonNode?` | Migrated root on success; null on failure. |
| `failureReason` | `string?` | Diagnostic when migration fails. |

## `DockLayoutSerializer`

Reactor-native docking layout persistence — v2 schema reader/writer. Spec 045 §5.4 / §8.9 (security limits) / §8.10 (safe-fallback); tracking §2.7 / §2.11.

```csharp
public static class DockLayoutSerializer
```

Namespace: `Microsoft.UI.Reactor.Docking.Persistence`. Package: `Microsoft.UI.Reactor.Advanced`.

### Remarks

AOT-clean: all parsing goes through `DockLayoutJsonContext`; no reflection at runtime, no type-name instantiation from JSON, no external schema URLs. Inputs above 1 MB or nested deeper than 32 are rejected at the boundary (spec §8.9).

The reader's failure mode never throws on the load path: corrupt JSON causes a log event (via `ReactorEventSource` in callers) and a fallback to the default layout — the load returns [`DockLayoutLoadResult.Fallback`](#fallback). The caller decides what the default is (spec §8.10).

### Methods

#### `Load`

##### `Load(string?)`

Parses a persisted layout JSON string. Always returns a result — corruption / oversize / depth-overflow yield [`DockLayoutLoadResult.Fallback`](#fallback) with the reason logged (caller is responsible for the actual log emit via [`DockLayoutLoadResult.FailureReason`](#failurereason)).

```csharp
public static DockLayoutLoadResult Load(string? json)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `json` | `string?` |  |

**Remarks**

Spec 045 §5.4, §8.9, §8.10.

##### `Load(string?, DockLayoutMigrationRegistry?)`

Parses a persisted layout JSON string, routing pre-v[`DockLayoutSerializer.CurrentSchemaVersion`](#currentschemaversion) payloads through the supplied [`DockLayoutMigrationRegistry`](#docklayoutmigrationregistry) (§5.4.4 / §8.11). When `migrations` is null, a fresh registry with the built-in v1→v2 step is used so the common case — loading a P1 file with no `$schema` marker — succeeds without requiring callers to pre-build a registry.

```csharp
public static DockLayoutLoadResult Load(string? json, DockLayoutMigrationRegistry? migrations)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `json` | `string?` |  |
| `migrations` | [`DockLayoutMigrationRegistry?`](#docklayoutmigrationregistry) |  |

#### `Save`

Serializes the supplied tree + side/floating state into a v2 JSON string.

```csharp
public static string Save(DockNode? root, IReadOnlyList<ToolWindow>? leftSide = null, IReadOnlyList<ToolWindow>? topSide = null, IReadOnlyList<ToolWindow>? rightSide = null, IReadOnlyList<ToolWindow>? bottomSide = null, IReadOnlyList<FloatingDockWindow>? floating = null, object? activeKey = null)
```

**Parameters**

| Name | Type | Default | Description |
| --- | --- | --- | --- |
| `root` | [`DockNode?`](layout-model.md#docknode) |  | The docked layout root (Split / TabGroup / leaf). |
| `leftSide` | `IReadOnlyList<ToolWindow>?` | `null` | Left-side pinned tool windows. |
| `topSide` | `IReadOnlyList<ToolWindow>?` | `null` | Top-side pinned tool windows. |
| `rightSide` | `IReadOnlyList<ToolWindow>?` | `null` | Right-side pinned tool windows. |
| `bottomSide` | `IReadOnlyList<ToolWindow>?` | `null` | Bottom-side pinned tool windows. |
| `floating` | `IReadOnlyList<FloatingDockWindow>?` | `null` | Floating-window state. |
| `activeKey` | `object?` | `null` | Stringified key of the currently-active pane, or null. |

### Fields

#### `CurrentSchemaVersion`

Current schema version emitted by [`DockLayoutSerializer.Save`](#save).

```csharp
public const int CurrentSchemaVersion = 2
```

#### `MaxBytes`

Maximum bytes a persisted layout JSON is allowed to occupy. Spec §8.9 security limit; exceeded inputs are rejected as corrupt.

```csharp
public const int MaxBytes = 1048576
```

#### `MaxDepth`

Maximum JSON nesting depth allowed in a layout document. Spec §8.9 security limit; exceeded inputs are rejected as corrupt.

```csharp
public const int MaxDepth = 32
```
