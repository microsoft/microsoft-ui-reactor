# Persistence

Stores and scopes that save window placement and persisted state.

**Learn more:** [Persistence](../../persistence.md)

## `ApplicationPersistedScope`

Process-lifetime persisted-state scope (spec 033 §2). Replaces the previous `ConcurrentDictionary`-based reject-on-full implementation with an LRU-bounded scope that:

```csharp
public sealed class ApplicationPersistedScope : IPersistedStateScope, IDisposable
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Remarks

The legacy internal-static `PersistedStateCache` shim is preserved for callers inside this assembly, delegating to [`ApplicationPersistedScope.Default`](#default).

### Constructors

#### `ApplicationPersistedScope`

```csharp
public ApplicationPersistedScope(int capacity)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `capacity` | `int` |  |

### Properties

#### `Capacity`

```csharp
public int Capacity  // get
```

#### `Count`

```csharp
public int Count  // get
```

#### `Default`

Process-wide singleton instance.

```csharp
public static ApplicationPersistedScope Default  // get
```

### Methods

#### `ApplyMemoryPressureTrim`

Public for tests: trim to 25% of capacity. Production callers use the memory-pressure event; tests invoke this directly to exercise the shrink path without depending on the OS callback.

```csharp
public int ApplyMemoryPressureTrim()
```

#### `Dispose`

Releases the resources that this object holds.

```csharp
public void Dispose()
```

#### `Remove`

```csharp
public void Remove(string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |

#### `Set`

```csharp
public void Set<T>(string key, T value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `T` |  |

#### `Trim`

Trims to the given target count. Exposed publicly so tests and callers that need a clean slate (e.g. test harnesses between cases) can drive the cache below its 25%-of-capacity memory-pressure floor without disposing the singleton.

```csharp
public int Trim(int targetCount)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `targetCount` | `int` |  |

#### `TryGet`

```csharp
public bool TryGet<T>(string key, out T value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `T` |  |

### Fields

#### `DefaultCapacity`

Default capacity for the singleton process-wide scope.

```csharp
public const int DefaultCapacity = 4096
```

## `IPersistedStateScope`

A keyed object cache used by `UsePersisted`. Two implementations: [`ApplicationPersistedScope`](#applicationpersistedscope) (process-global, LRU-bounded, memory-pressure-aware) and [`WindowPersistedScope`](#windowpersistedscope) (host-scoped, LRU-bounded, disposed on host unload).

```csharp
public interface IPersistedStateScope : IDisposable
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Remarks

Spec 033 §2. `UsePersisted` hooks consult these scopes at hook-entry time on the UI thread; the underlying `LruCache` synchronizes its own mutations so background-thread invalidations (memory pressure events) are safe.

### Properties

#### `Capacity`

The maximum number of entries this scope retains.

```csharp
public abstract int Capacity  // get
```

#### `Count`

The number of entries currently held in the scope.

```csharp
public abstract int Count  // get
```

### Methods

#### `Remove`

Removes the entry under `key`, if any.

```csharp
public void Remove(string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |

#### `Set`

Stores `value` under `key`. Promotes the entry to MRU. When the scope is at capacity, evicts the LRU entry before inserting (no refusal).

```csharp
public void Set<T>(string key, T value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `T` |  |

#### `TryGet`

Try to read a previously-stored value for `key`. Returns `false` when the key is absent or the stored value is not assignable to `T`. Reads promote the entry to MRU.

```csharp
public bool TryGet<T>(string key, out T value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `T` |  |

## `IWindowPersistenceStore`

Storage adapter for [`WindowSpec.PersistenceId`](WindowSpec.md#persistenceid). Implementations retain opaque per-id byte payloads; Reactor handles the serialization shape (`WINDOWPLACEMENT` + monitor-layout fingerprint). (spec 036 §8)

```csharp
public interface IWindowPersistenceStore
```

Namespace: `Microsoft.UI.Reactor.Hosting.Persistence`.

### Remarks

Implementations must be thread-safe for concurrent reads from arbitrary threads (Reactor reads on the UI thread today, but production callers may share a store across multiple processes / consoles). Writes happen on the UI thread on close.

Implementations must never throw into the caller. Read failures return `false` with the out-parameter `data` set to `null`; write failures are swallowed with a diagnostic log line. Reactor treats the store as best-effort — a corrupt or missing entry simply falls back to the spec's default placement.

### Methods

#### `TryRead`

Try to read the payload stored under `id`. Returns `true` when an entry exists and was deserialized successfully.

```csharp
public bool TryRead(string id, out byte[]? data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `id` | `string` |  |
| `data` | `byte[]?` |  |

#### `Write`

Persist `data` under `id`. Best effort — implementations must not throw.

```csharp
public void Write(string id, byte[] data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `id` | `string` |  |
| `data` | `byte[]` |  |

## `JsonFileStore`

Default unpackaged-app persistence store. Writes a JSON document (one base64 string per persistence id) to `%LOCALAPPDATA%/<ProcessName>/reactor-windows.json`. Atomic via write-then-rename; bounded at [`JsonFileStore.MaxFileSizeBytes`](#maxfilesizebytes) (1 MB) on read to refuse pathological payloads. (spec 036 §8 / §0.5)

```csharp
public sealed class JsonFileStore : IWindowPersistenceStore
```

Namespace: `Microsoft.UI.Reactor.Hosting.Persistence`.

### Remarks

Read failures (missing file, malformed JSON, oversize) return `false` from [`JsonFileStore.TryRead`](#tryread_1) with no exception bubbling out — the spec calls for "warn-and-default" semantics on corruption.

Writes are best-effort and survive disk-full / permission denied without throwing.

**Concurrency.** Read-merge-write is serialized two ways: an in-process lock orders threads sharing an instance, and a named `CrossProcessWriteGuard` extends that ordering across separate instances and separate processes. Without the latter, two writers could each read the pre-merge document and the second rename would silently discard the first's entry — even when the two wrote *different* persistence ids. Each write stages through a temp file named for the writing process and thread, so a crash mid-write cannot leave a shared temp that another process renames over the real file.

Readers take no guard, but they do open with [`FileShare.Delete`](https://learn.microsoft.com/dotnet/api/system.io.fileshare.delete) in addition to [`FileShare.Read`](https://learn.microsoft.com/dotnet/api/system.io.fileshare.read), and the commit uses `File.Replace` rather than `File.Move(overwrite: true)`. Measured on Windows: `Move` cannot replace a destination any process still holds open — it fails even when that reader granted delete sharing — whereas `Replace` succeeds precisely when the reader does grant it. Both halves are required; with only one, a concurrent reader in a second app instance silently kills the write, because write failures are swallowed by contract. The reader then continues against the file it opened, seeing either the previous document or the next one, never a torn one.

### Constructors

#### `JsonFileStore`

##### `JsonFileStore()`

Construct the default unpackaged store rooted at `%LOCALAPPDATA%/<ProcessName>/reactor-windows.json`.

```csharp
public JsonFileStore()
```

##### `JsonFileStore(string)`

Construct with an explicit file path. Used by unit tests.

```csharp
public JsonFileStore(string path)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `path` | `string` |  |

### Properties

#### `Path`

The on-disk file path.

```csharp
public string Path  // get
```

### Methods

#### `DefaultPath`

Compute the default unpackaged-store path. Uses a sanitized process name segment so a process named `foo/bar` can't traverse out of LocalAppData. (spec 036 §0.5 / §9.5)

```csharp
public static string DefaultPath()
```

#### `TryRead`

```csharp
public bool TryRead(string id, out byte[]? data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `id` | `string` |  |
| `data` | `byte[]?` |  |

#### `Write`

```csharp
public void Write(string id, byte[] data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `id` | `string` |  |
| `data` | `byte[]` |  |

### Fields

#### `MaxFileSizeBytes`

Hard ceiling on the persistence file size. A 1 MB cap is generous — real-world payloads are ~80 bytes per window — and rejects pathological payloads from a tampered file. (spec 036 §0.5)

```csharp
public const long MaxFileSizeBytes = 1048576
```

## `PackagedSettingsStore`

Default packaged-app persistence store. Routes through `ApplicationData.Current.LocalSettings` using a "Reactor" container and `WindowPersistence_<id>` keys. These differ from WinUIEx's container and keys, so placements saved by WinUIEx are not picked up. (spec 036 §8)

```csharp
public sealed class PackagedSettingsStore : IWindowPersistenceStore
```

Namespace: `Microsoft.UI.Reactor.Hosting.Persistence`.

### Remarks

Constructing this store from an unpackaged process throws on first access (the WinRT API requires package identity). Callers must check [`PackagedSettingsStore.IsAvailable`](#isavailable) before instantiating, or fall back to [`JsonFileStore`](#jsonfilestore).

All read failures (missing key, type mismatch) return `false`; write failures are swallowed with a stderr diagnostic.

### Constructors

#### `PackagedSettingsStore`

```csharp
public PackagedSettingsStore()
```

### Methods

#### `IsAvailable`

Detect whether the current process has WinRT package identity. Constructing [`PackagedSettingsStore`](#packagedsettingsstore) in an unpackaged context still works at construction time; this static check lets auto-detection logic pick the right default.

```csharp
public static bool IsAvailable()
```

#### `TryRead`

```csharp
public bool TryRead(string id, out byte[]? data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `id` | `string` |  |
| `data` | `byte[]?` |  |

#### `Write`

```csharp
public void Write(string id, byte[] data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `id` | `string` |  |
| `data` | `byte[]` |  |

## `PersistedScope`

Selector for the lifetime/visibility scope of [`UsePersisted`](../hooks/UsePersisted.md#usepersistedtstring-t) values. Spec 033 §2.

```csharp
public enum PersistedScope
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Values

| Name | Value | Description |
| --- | --- | --- |
| `Window` | `0` | Bound to the lifetime of the hosting [`ReactorHost`](ReactorHost.md) or [`ReactorHostControl`](ReactorHostControl.md). State is dropped when the host unloads. Recommended default for new code. |
| `Application` | `1` | Process-lifetime. State persists across windows in the same process. Replaces the legacy default (which was implicit application-scope). |

## `UnpackagedAppDataStore`

Unpackaged-app persistence store backed by the Windows App SDK's first-class app-data root, `Microsoft.Windows.Storage.ApplicationData.GetForUnpackaged()` (Windows App SDK ≥ 2.2.0). Persists to `<ApplicationData.LocalPath>/reactor-windows.json`. (spec 063 §4)

```csharp
public sealed class UnpackagedAppDataStore : IWindowPersistenceStore
```

Namespace: `Microsoft.UI.Reactor.Hosting.Persistence`.

### Remarks

**Opt-in.** Auto-detection still selects [`JsonFileStore`](#jsonfilestore) for unpackaged processes; assign an instance to [`ReactorApp.WindowPersistenceStore`](ReactorApp.md#windowpersistencestore) before the first `OpenWindow` to use this one instead. The two stores key their data differently — this one by the `publisher`/`product` pair you supply, [`JsonFileStore`](#jsonfilestore) by the entry process's name — so switching does not migrate an existing app's saved layouts. (spec 063 §6, D2 / D3)

**Why the path surface and not `LocalSettings`.** This store uses `ApplicationData.LocalPath` and deliberately does **not** use the sibling `LocalSettings` key/value surface. On the runtime this repo pins, `GetForUnpackaged().LocalSettings` opens `HKCU\SOFTWARE\<publisher>\<product>` — a **roaming** hive — rather than the machine-local `HKCU\SOFTWARE\Classes\Local Settings\Software\…` it is contracted to use (WindowsAppSDK issue 6559, RuntimeCompatibilityChange `ApplicationData_GetForUnpackaged_LocalSettings`). Window placement is monitor-topology- and DPI-dependent, so roaming it between a user's machines restores windows onto monitors that do not exist there. `LocalPath` resolves under `%LOCALAPPDATA%`, which does not roam. (spec 063 §3.1)

That behaviour is a property of the installed 2.x Windows App Runtime, which services in place, rather than of the SDK version an app pins — so it can differ machine to machine for the same build. `LocalPath` has no such variance, which is a second reason this store is built on it. (spec 063 §3.2)

**Concurrency.** Safe to share across instances and processes: the underlying [`JsonFileStore`](#jsonfilestore) serializes read-merge-write with a named cross-process guard. That matters more for this store than for [`JsonFileStore`](#jsonfilestore)'s own default path, because this identity is deliberately *shared* — two differently-named executables from one publisher/product resolve the same file, where the process-name-keyed default would have given them separate ones. (spec 063 §5)

Read/write failures follow the [`IWindowPersistenceStore`](#iwindowpersistencestore) "warn-and-default" contract — they never throw into the caller. The *constructor* may throw, matching [`JsonFileStore`](#jsonfilestore): an invalid publisher/product is an authoring error, not a runtime condition.

### Constructors

#### `UnpackagedAppDataStore`

Resolve the Windows App SDK app-data root for `publisher`/`product` and persist beneath it.

```csharp
public UnpackagedAppDataStore(string publisher, string product)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `publisher` | `string` | Publisher segment of the app-data root. The SDK validates this and rejects path-traversal, rooted, and empty values with an [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception); Reactor does not sanitize it further. |
| `product` | `string` | Product segment of the app-data root. |

**Exceptions**

| Exception | Condition |
| --- | --- |
| [`ArgumentException`](https://learn.microsoft.com/dotnet/api/system.argumentexception) | `publisher` or `product` is empty, or the SDK rejected it as invalid. |

### Methods

#### `TryRead`

```csharp
public bool TryRead(string id, out byte[]? data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `id` | `string` |  |
| `data` | `byte[]?` |  |

#### `Write`

```csharp
public void Write(string id, byte[] data)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `id` | `string` |  |
| `data` | `byte[]` |  |

## `WindowPersistedScope`

Per-host (per-window) persisted-state scope. Owned by a [`ReactorHost`](ReactorHost.md) / [`ReactorHostControl`](ReactorHostControl.md) and disposed when the host unloads — so window-scoped state is bounded by window lifetime, not process lifetime.

```csharp
public sealed class WindowPersistedScope : IPersistedStateScope, IDisposable
```

Namespace: `Microsoft.UI.Reactor.Core`.

### Remarks

Spec 033 §2. Bounded LRU (default capacity 1024). Window-scoped state is the recommended default for new [`UsePersisted`](../hooks/UsePersisted.md#usepersistedtstring-t) calls because most apps want "preserved across an unmount/remount within this window" rather than "preserved across every window in the process." Memory-pressure registration is deliberately omitted — the lifetime is already bounded by the host.

### Constructors

#### `WindowPersistedScope`

```csharp
public WindowPersistedScope()
public WindowPersistedScope(int capacity)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `capacity` | `int` |  |

### Properties

#### `Capacity`

```csharp
public int Capacity  // get
```

#### `Count`

```csharp
public int Count  // get
```

### Methods

#### `Dispose`

Releases the resources that this object holds.

```csharp
public void Dispose()
```

#### `Remove`

```csharp
public void Remove(string key)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |

#### `Set`

```csharp
public void Set<T>(string key, T value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `T` |  |

#### `TryGet`

```csharp
public bool TryGet<T>(string key, out T value)
```

**Parameters**

| Name | Type | Description |
| --- | --- | --- |
| `key` | `string` |  |
| `value` | `T` |  |

### Fields

#### `DefaultCapacity`

Default capacity for a per-window scope.

```csharp
public const int DefaultCapacity = 1024
```
