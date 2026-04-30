---
name: cs-memory-lifecycle-review
description: >-
  Review C# code for memory safety and object lifetime defects: missing
  IDisposable implementations, undisposed resources, event handler leaks,
  GC pressure from LOH allocations and boxing, unsafe code without bounds
  checking, P/Invoke marshaling errors, GCHandle leaks, Span/Memory escaping
  scope, ArrayPool rent without return, and object pool stale-state bugs.
  34 patterns from .NET Runtime team guidance and .NET Framework Design
  Guidelines. Covers IDisposable/Dispose,
  GC pressure/object lifetime, unsafe code/interop, and object pooling/reuse
  domains.
  Use this skill when reviewing C# code that manages unmanaged resources,
  uses unsafe/fixed/stackalloc, performs P/Invoke, uses object or array pools,
  or shows signs of GC pressure (LOH allocations, excessive Gen2 collections).
keywords:
  - IDisposable
  - Dispose
  - using statement
  - finalizer
  - GC pressure
  - Large Object Heap
  - LOH
  - unsafe
  - fixed
  - stackalloc
  - P/Invoke
  - DllImport
  - GCHandle
  - Span
  - Memory
  - ArrayPool
  - ObjectPool
  - MemoryPool
  - event handler leak
  - socket exhaustion
  - HttpClient
  - boxing
  - StringBuilder
  - WeakReference
  - IAsyncDisposable
metadata:
  category: code-review
  subcategory: memory-lifecycle
  complexity: medium
  duration: 20-60 minutes per component
  author: Windows Engineering Systems
  version: "1.0"
  source: >-
    .NET Runtime team, .NET Framework Design Guidelines
  pattern-count: 34
  domains:
    - idisposable-dispose
    - gc-pressure-lifetime
    - unsafe-interop
    - object-pooling-reuse
---

# C# Memory & Lifecycle Code Review

## Quick Detection

**Primary Symptoms (in code under review)**:
- Type holds unmanaged resources (handles, streams, connections) but does not implement `IDisposable`
- `IDisposable` field created but never disposed in the containing type
- `new SomeDisposable()` used without `using` statement or declaration
- Event handler subscribed (`+=`) without corresponding unsubscribe (`-=`)
- `HttpClient` created per-request instead of shared/reused
- Large arrays (>85KB) allocated in hot paths (LOH pressure)
- `unsafe` blocks or `fixed` statements without bounds checking
- `GCHandle.Alloc` without matching `GCHandle.Free`
- `ArrayPool<T>.Shared.Rent()` without corresponding `Return()`
- `Span<T>` or `Memory<T>` escaping their intended scope

**Key Code Patterns to Search For**:
```csharp
// Missing IDisposable — type holds unmanaged resource but no Dispose
class FileProcessor  // BAD: no IDisposable
{
    private FileStream _stream;  // unmanaged resource, never cleaned up
}

// Missing using statement
var stream = new FileStream(path, FileMode.Open);  // BAD: not in using
stream.Read(buffer, 0, buffer.Length);
// stream never disposed — handle leak

// Event handler leak
publisher.SomeEvent += subscriber.OnSomeEvent;  // BAD: never unsubscribed
// subscriber cannot be GC'd while publisher lives

// HttpClient per-request — socket exhaustion
var client = new HttpClient();  // BAD: per-request allocation
var result = await client.GetAsync(url);

// ArrayPool rent without return
var buffer = ArrayPool<byte>.Shared.Rent(1024);  // BAD: never returned
ProcessData(buffer);
// pool exhaustion over time
```

## Analysis Workflow

### Step 1: Identify Resource Ownership Model

Determine how resources are managed in the component under review.

1. Search for `IDisposable` implementations and `using` declarations:
   ```
   // Check for types that should implement IDisposable
   Search for: class/struct declarations holding Stream, Handle, Connection, DbContext fields
   ```

2. Check for unmanaged resource usage — P/Invoke, `GCHandle`, `unsafe`, `fixed`:
   ```
   // Search for unmanaged interop surface
   Search for: [DllImport], GCHandle, unsafe, fixed, stackalloc, Marshal.*
   ```

3. Classify: **Fully managed** (low risk), **Mixed managed/unmanaged** (high risk), **Heavy interop** (systematic risk)

### Step 2: Scan for Pattern Matches

Apply the 34 memory and lifecycle patterns from the catalog below.

**Priority order** (by real-world impact):

1. **IDisposable & Dispose** (12 patterns) -- resource leaks are the most common .NET production issue
2. **GC Pressure & Object Lifetime** (10 patterns) -- causes latency spikes and memory bloat
3. **Unsafe Code & Interop** (8 patterns) -- can cause crashes and security vulnerabilities
4. **Object Pooling & Reuse** (4 patterns) -- causes pool exhaustion and data leaks

### Step 3: Classify Findings

For each potential match:

1. **Confirm**: Trace the resource lifetime -- is the resource actually leaked or used after dispose?
2. **Severity**: Prioritize by production impact:
   - **Critical**: Buffer overrun in unsafe code, type safety violation via `Unsafe.As<T>`
   - **High**: Resource leak causing handle/socket exhaustion, use-after-dispose, GCHandle leak
   - **Medium**: GC pressure, missing SuppressFinalize, boxing in hot path
   - **Low**: Redundant patterns, minor style issues
3. **False Positive Check**: Is there a higher-level `using`, a DI container managing the lifetime, or a custom pool?

### Step 4: Generate Fix

Use the fix strategy decision trees below. Each fix must follow the before/after format.

### Step 5: Verify Fix

1. **Static verification**: Confirm `using`/`Dispose` covers the full resource lifetime
2. **Build verification**: Ensure no new warnings (especially CA2000, CA1816, CA2213)
3. **Pattern regression**: Search for same pattern re-introduced elsewhere in the PR
4. **Test coverage**: Run existing unit tests; suggest new test if none covers the fixed path

---

## Pattern Catalog

### IDisposable & Dispose Pattern

#### ML-DISP-01: Missing IDisposable on Type Holding Unmanaged Resources

| Field | Detail |
|-------|--------|
| **ID** | ML-DISP-01 |
| **Severity** | High |
| **Signal** | Class/struct has fields of type `Stream`, `SafeHandle`, `DbConnection`, `Socket`, `HttpClient`, native handle, or any `IDisposable`, but the containing type does not implement `IDisposable` |
| **Risk** | Resource leak -- handles, connections, file locks never released. Under load, causes handle exhaustion, connection pool starvation, or file-lock contention |
| **Fix** | Implement `IDisposable` (and `IAsyncDisposable` if async cleanup is needed). Dispose all owned resources in `Dispose()` |

```csharp
// BAD -- type owns a stream but does not implement IDisposable
class LogWriter
{
    private readonly StreamWriter _writer;
    public LogWriter(string path) => _writer = new StreamWriter(path);
    public void Write(string msg) => _writer.WriteLine(msg);
    // _writer is never disposed -- file handle leaked
}

// GOOD -- proper IDisposable implementation
class LogWriter : IDisposable
{
    private readonly StreamWriter _writer;
    private bool _disposed;

    public LogWriter(string path) => _writer = new StreamWriter(path);
    public void Write(string msg)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _writer.WriteLine(msg);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _writer.Dispose();
            _disposed = true;
        }
    }
}
```

---

#### ML-DISP-02: IDisposable Field Not Disposed in Containing Type's Dispose

| Field | Detail |
|-------|--------|
| **ID** | ML-DISP-02 |
| **Severity** | High |
| **Signal** | Type implements `IDisposable` but one or more `IDisposable` fields are not disposed in `Dispose()` |
| **Risk** | Partial cleanup -- some resources leak even though the type appears to be properly disposable |
| **Fix** | Dispose all `IDisposable` fields in the `Dispose` method. Use CA2213 analyzer to catch this |

```csharp
// BAD -- _connection is not disposed
class DataService : IDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlCommand _command;

    public void Dispose()
    {
        _command.Dispose();
        // _connection is never disposed!
    }
}

// GOOD -- all fields disposed
class DataService : IDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlCommand _command;
    private bool _disposed;

    public void Dispose()
    {
        if (!_disposed)
        {
            _command.Dispose();
            _connection.Dispose();
            _disposed = true;
        }
    }
}
```

---

#### ML-DISP-03: Missing `using` Statement/Declaration for IDisposable Local

| Field | Detail |
|-------|--------|
| **ID** | ML-DISP-03 |
| **Severity** | High |
| **Signal** | `new` expression creating an `IDisposable` type assigned to a local variable without `using` keyword |
| **Risk** | If an exception occurs between creation and manual `Dispose()` call, the resource leaks. Even without exceptions, forgetting the `Dispose()` call is common |
| **Fix** | Use `using` declaration (C# 8+) or `using` statement |

```csharp
// BAD -- no using, exception between new and Dispose leaks
var stream = new FileStream(path, FileMode.Open);
var data = stream.Read(buffer, 0, buffer.Length);  // if this throws, stream leaks
stream.Dispose();

// GOOD -- using declaration (C# 8+)
using var stream = new FileStream(path, FileMode.Open);
var data = stream.Read(buffer, 0, buffer.Length);
// stream.Dispose() called automatically at end of scope

// GOOD -- using statement (older style)
using (var stream = new FileStream(path, FileMode.Open))
{
    var data = stream.Read(buffer, 0, buffer.Length);
}
```

---

#### ML-DISP-04: Dispose Pattern Without GC.SuppressFinalize

| Field | Detail |
|-------|--------|
| **ID** | ML-DISP-04 |
| **Severity** | Medium |
| **Signal** | Type has a finalizer (`~ClassName`) and `Dispose()` but `Dispose()` does not call `GC.SuppressFinalize(this)` |
| **Risk** | Object is finalized even after Dispose -- unnecessary GC overhead, and finalizer may run on already-disposed state |
| **Fix** | Call `GC.SuppressFinalize(this)` at the end of `Dispose()` |

```csharp
// BAD -- missing SuppressFinalize
class NativeWrapper : IDisposable
{
    private IntPtr _handle;
    ~NativeWrapper() => ReleaseHandle();
    public void Dispose() => ReleaseHandle();  // Missing GC.SuppressFinalize!
    private void ReleaseHandle() { /* release _handle */ }
}

// GOOD -- SuppressFinalize called
class NativeWrapper : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    ~NativeWrapper() => Dispose(disposing: false);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing) { /* dispose managed resources */ }
            ReleaseHandle();
            _disposed = true;
        }
    }

    private void ReleaseHandle() { /* release _handle */ }
}
```

---

#### ML-DISP-05: Finalizer Without Dispose Pattern

| Field | Detail |
|-------|--------|
| **ID** | ML-DISP-05 |
| **Severity** | High |
| **Signal** | Type has a finalizer (`~ClassName`) but does not implement `IDisposable` |
| **Risk** | Users cannot deterministically clean up resources. Finalization is non-deterministic, delays cleanup, and adds GC overhead. Finalizable objects survive an extra GC generation |
| **Fix** | Implement the full Dispose pattern with `IDisposable`, `Dispose(bool)`, and `GC.SuppressFinalize` |

```csharp
// BAD -- finalizer without IDisposable
class NativeBuffer
{
    private IntPtr _buffer;
    public NativeBuffer(int size) => _buffer = Marshal.AllocHGlobal(size);
    ~NativeBuffer() => Marshal.FreeHGlobal(_buffer);
    // No way for callers to deterministically free!
}

// GOOD -- full Dispose pattern
class NativeBuffer : IDisposable
{
    private IntPtr _buffer;
    private bool _disposed;

    public NativeBuffer(int size) => _buffer = Marshal.AllocHGlobal(size);

    ~NativeBuffer() => Dispose(disposing: false);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
            _disposed = true;
        }
    }
}
```

---

#### ML-DISP-06: Calling Dispose on Object Still Referenced Elsewhere (Use-After-Dispose)

| Field | Detail |
|-------|--------|
| **ID** | ML-DISP-06 |
| **Severity** | High |
| **Signal** | `Dispose()` called on object that is still referenced by other code paths, fields, or collections |
| **Risk** | `ObjectDisposedException` at runtime, data corruption if disposed object is reused without checks |
| **Fix** | Ensure single ownership or reference counting. Set field to `null` after dispose. Use `ObjectDisposedException.ThrowIf` in methods |

```csharp
// BAD -- stream disposed while reader still holds reference
var stream = new MemoryStream(data);
var reader = new StreamReader(stream);
stream.Dispose();  // reader's underlying stream is now disposed
var line = reader.ReadLine();  // ObjectDisposedException!

// GOOD -- let the owner dispose (reader owns stream by default)
using var stream = new MemoryStream(data);
using var reader = new StreamReader(stream, leaveOpen: false);
var line = reader.ReadLine();
// reader disposes stream when reader is disposed
```

---

#### ML-DISP-07: IDisposable Returned from Method Without Ownership Transfer Documentation

| Field | Detail |
|-------|--------|
| **ID** | ML-DISP-07 |
| **Severity** | Medium |
| **Signal** | Method creates and returns an `IDisposable` object. Caller may not realize they own the lifetime |
| **Risk** | Resource leak -- caller does not dispose the returned object because ownership is unclear |
| **Fix** | Document ownership transfer. Name methods clearly (e.g., `CreateStream`, `OpenConnection`). Consider factory pattern with `using` in callers |

```csharp
// BAD -- unclear ownership, caller may forget to dispose
public Stream GetData()
{
    var stream = new MemoryStream();
    WriteDataTo(stream);
    stream.Position = 0;
    return stream;  // who disposes this?
}

// GOOD -- clear ownership via naming and documentation
/// <summary>
/// Creates a new stream containing the data. Caller is responsible for disposal.
/// </summary>
public Stream CreateDataStream()
{
    var stream = new MemoryStream();
    WriteDataTo(stream);
    stream.Position = 0;
    return stream;  // "Create" prefix signals caller owns it
}
```

---

#### ML-DISP-08: Event Handler Not Unsubscribed Causing Leak

| Field | Detail |
|-------|--------|
| **ID** | ML-DISP-08 |
| **Severity** | High |
| **Signal** | `+=` event subscription without corresponding `-=` unsubscription, especially when publisher outlives subscriber |
| **Risk** | Publisher holds strong reference to subscriber delegate, preventing GC. In long-running apps, causes unbounded memory growth |
| **Fix** | Unsubscribe in `Dispose()` or use weak event pattern. For static events, always unsubscribe |

```csharp
// BAD -- subscriber leaks because publisher holds reference
class DataMonitor
{
    public DataMonitor(EventSource source)
    {
        source.DataChanged += OnDataChanged;  // never unsubscribed!
    }

    private void OnDataChanged(object? sender, EventArgs e) { /* ... */ }
}

// GOOD -- unsubscribe in Dispose
class DataMonitor : IDisposable
{
    private readonly EventSource _source;
    private bool _disposed;

    public DataMonitor(EventSource source)
    {
        _source = source;
        _source.DataChanged += OnDataChanged;
    }

    private void OnDataChanged(object? sender, EventArgs e) { /* ... */ }

    public void Dispose()
    {
        if (!_disposed)
        {
            _source.DataChanged -= OnDataChanged;
            _disposed = true;
        }
    }
}
```

---

#### ML-DISP-09: Dispose Called Inside Lock (Potential Deadlock in Finalizer)

| Field | Detail |
|-------|--------|
| **ID** | ML-DISP-09 |
| **Severity** | Medium |
| **Signal** | `Dispose()` call inside a `lock` block, or Dispose implementation that acquires a lock |
| **Risk** | If finalizer runs (e.g., due to missed Dispose), the finalizer thread may deadlock trying to acquire the same lock that another thread holds. Finalizer thread deadlock halts all finalization |
| **Fix** | Avoid locks in Dispose/Finalizer. Use atomic state flags (`Interlocked.Exchange`) instead of locks for dispose guards |

```csharp
// BAD -- Dispose acquires lock, risky if called from finalizer
class ManagedResource : IDisposable
{
    private readonly object _lock = new();
    private Stream? _stream;

    public void Dispose()
    {
        lock (_lock)  // DANGER: if finalizer calls this, potential deadlock
        {
            _stream?.Dispose();
            _stream = null;
        }
    }
}

// GOOD -- use Interlocked for dispose guard, no lock
class ManagedResource : IDisposable
{
    private Stream? _stream;
    private int _disposed;  // 0 = not disposed, 1 = disposed

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _stream?.Dispose();
            _stream = null;
        }
    }
}
```

---

#### ML-DISP-10: Double-Dispose Without Idempotency Guard

| Field | Detail |
|-------|--------|
| **ID** | ML-DISP-10 |
| **Severity** | Medium |
| **Signal** | `Dispose()` implementation without a boolean or `Interlocked` guard, and the type could be disposed from multiple paths (e.g., `using` + manual Dispose, or DI container + explicit) |
| **Risk** | `ObjectDisposedException` or other exception on second dispose. Well-behaved types should be idempotent per .NET guidelines |
| **Fix** | Add `_disposed` boolean guard. Check at entry to `Dispose()` |

```csharp
// BAD -- not idempotent
class Connection : IDisposable
{
    private Socket _socket;
    public void Dispose()
    {
        _socket.Close();  // throws on second call!
    }
}

// GOOD -- idempotent dispose
class Connection : IDisposable
{
    private Socket? _socket;
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _socket?.Close();
        _socket = null;
        _disposed = true;
    }
}
```

---

#### ML-DISP-11: Async Disposal (IAsyncDisposable) Not Awaited or Pattern Incomplete

| Field | Detail |
|-------|--------|
| **ID** | ML-DISP-11 |
| **Severity** | High |
| **Signal** | Type implements `IAsyncDisposable` but callers use synchronous `Dispose()` instead of `await using`. Or type has async cleanup needs but only implements `IDisposable` |
| **Risk** | Synchronous dispose of async resources may block or skip async cleanup entirely. In ASP.NET Core, can cause request thread starvation |
| **Fix** | Use `await using` for `IAsyncDisposable` types. Implement both `IDisposable` and `IAsyncDisposable` when the type may be used in both sync and async contexts |

```csharp
// BAD -- async disposable used synchronously
var connection = new AsyncDbConnection();
using (connection)  // calls sync Dispose, not DisposeAsync!
{
    await connection.ExecuteAsync(query);
}

// GOOD -- await using for async disposal
var connection = new AsyncDbConnection();
await using (connection)  // calls DisposeAsync
{
    await connection.ExecuteAsync(query);
}

// GOOD -- C# 8+ declaration syntax
await using var connection = new AsyncDbConnection();
await connection.ExecuteAsync(query);
```

---

#### ML-DISP-12: HttpClient Created Per-Request Instead of Reused

| Field | Detail |
|-------|--------|
| **ID** | ML-DISP-12 |
| **Severity** | High |
| **Signal** | `new HttpClient()` inside a method that runs per-request, per-loop-iteration, or per-operation |
| **Risk** | Socket exhaustion (TIME_WAIT state). Each `HttpClient` instance holds its own connection pool. Even after `Dispose()`, sockets linger in TIME_WAIT for 240 seconds. Under load, exhausts ephemeral port range |
| **Fix** | Use `IHttpClientFactory` (preferred in DI scenarios), a static/shared `HttpClient` instance, or `SocketsHttpHandler` with `PooledConnectionLifetime` |

```csharp
// BAD -- new HttpClient per request
public async Task<string> FetchDataAsync(string url)
{
    using var client = new HttpClient();  // BAD: socket exhaustion
    return await client.GetStringAsync(url);
}

// GOOD -- IHttpClientFactory (ASP.NET Core / DI)
public class DataService
{
    private readonly IHttpClientFactory _httpClientFactory;
    public DataService(IHttpClientFactory factory) => _httpClientFactory = factory;

    public async Task<string> FetchDataAsync(string url)
    {
        using var client = _httpClientFactory.CreateClient();
        return await client.GetStringAsync(url);
    }
}

// GOOD -- static shared instance (console apps / simple cases)
private static readonly HttpClient s_httpClient = new()
{
    Timeout = TimeSpan.FromSeconds(30)
};
```

---

### GC Pressure & Object Lifetime

#### ML-GC-01: Large Object Heap Allocation in Hot Path (>85KB Arrays)

| Field | Detail |
|-------|--------|
| **ID** | ML-GC-01 |
| **Severity** | Medium |
| **Signal** | `new byte[N]`, `new T[N]` where N * sizeof(T) > 85,000 bytes, in a method called frequently (request handler, loop body, event handler) |
| **Risk** | LOH allocations are expensive and not compacted by default. In hot paths, causes LOH fragmentation and Gen2 GC collections, leading to latency spikes |
| **Fix** | Use `ArrayPool<T>.Shared.Rent()` / `Return()` for temporary large buffers. Or use `RecyclableMemoryStreamManager` for streams |

```csharp
// BAD -- allocates 1MB on LOH every call
public byte[] ProcessRequest(Request req)
{
    var buffer = new byte[1_048_576];  // 1MB -- LOH allocation
    FillBuffer(buffer, req);
    return Transform(buffer);
}

// GOOD -- rent from pool
public byte[] ProcessRequest(Request req)
{
    var buffer = ArrayPool<byte>.Shared.Rent(1_048_576);
    try
    {
        FillBuffer(buffer, req);
        return Transform(buffer);
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

---

#### ML-GC-02: Pinned Objects Preventing GC Compaction

| Field | Detail |
|-------|--------|
| **ID** | ML-GC-02 |
| **Severity** | Medium |
| **Signal** | `GCHandle.Alloc(obj, GCHandleType.Pinned)` or `fixed` statement held for extended duration, or many pinned objects concurrently |
| **Risk** | Pinned objects prevent GC heap compaction, causing fragmentation. With many short-lived pins, the GC heap becomes swiss-cheese, increasing memory usage and GC pause times |
| **Fix** | Minimize pin duration. Use `Memory<T>` / `Span<T>` instead of pinning when possible. For P/Invoke, let the marshaler handle pinning automatically |

```csharp
// BAD -- long-lived pin prevents compaction
var handle = GCHandle.Alloc(largeBuffer, GCHandleType.Pinned);
// ... handle stays pinned for minutes while processing ...

// GOOD -- pin only for the duration needed
fixed (byte* ptr = largeBuffer)
{
    NativeMethod(ptr, largeBuffer.Length);
}  // unpinned immediately after native call
```

---

#### ML-GC-03: Excessive Gen2 Collections from Long-Lived Temporary Objects

| Field | Detail |
|-------|--------|
| **ID** | ML-GC-03 |
| **Severity** | Medium |
| **Signal** | Objects allocated in Gen0 but referenced long enough to survive to Gen2 (e.g., cached in a growing dictionary, stored in a timer callback closure), then abandoned |
| **Risk** | Gen2 collections are expensive (full blocking GC in workstation mode). Objects that live "just long enough" to promote but are then abandoned cause the worst GC profile |
| **Fix** | Use object pooling for objects with medium lifetimes. Review caches for unbounded growth. Consider `ConcurrentDictionary` with TTL eviction |

```csharp
// BAD -- temporary results cached without eviction, promote to Gen2
private static readonly Dictionary<string, ExpensiveResult> _cache = new();
public ExpensiveResult GetResult(string key)
{
    if (!_cache.TryGetValue(key, out var result))
    {
        result = ComputeExpensive(key);
        _cache[key] = result;  // never evicted -- unbounded Gen2 growth
    }
    return result;
}

// GOOD -- bounded cache with eviction
private static readonly MemoryCache _cache = new(new MemoryCacheOptions
{
    SizeLimit = 1000
});
```

---

#### ML-GC-04: WeakReference to Short-Lived Object

| Field | Detail |
|-------|--------|
| **ID** | ML-GC-04 |
| **Severity** | Low |
| **Signal** | `WeakReference<T>` wrapping an object that is short-lived or has no other strong references |
| **Risk** | The referent is collected immediately on next GC, making the `WeakReference` always return `null`. Code that checks `TryGetTarget` always takes the fallback path, adding overhead with no benefit |
| **Fix** | Only use `WeakReference<T>` for objects that have long-lived strong references elsewhere (e.g., caches, observer patterns). If the object is always short-lived, use a direct reference |

```csharp
// BAD -- object collected immediately, WeakReference is pointless
var weak = new WeakReference<ExpensiveObject>(new ExpensiveObject());
// At next GC, target is collected -- TryGetTarget always returns false
if (weak.TryGetTarget(out var obj))
{
    obj.Use();  // never reaches here
}

// GOOD -- WeakReference for object with strong references elsewhere
private readonly List<ExpensiveObject> _activeObjects = new();
private readonly List<WeakReference<ExpensiveObject>> _observers = new();

public void Track(ExpensiveObject obj)
{
    _activeObjects.Add(obj);  // strong reference keeps it alive
    _observers.Add(new WeakReference<ExpensiveObject>(obj));
}
```

---

#### ML-GC-05: Static Event Handlers Preventing Collection of Subscribers

| Field | Detail |
|-------|--------|
| **ID** | ML-GC-05 |
| **Severity** | High |
| **Signal** | Static event (`static event EventHandler Foo`) with instance method subscriptions, or subscription to `AppDomain.ProcessExit`, `Console.CancelKeyPress`, or similar static events |
| **Risk** | Static events hold strong references to all subscribers forever. Instance subscribers cannot be GC'd, causing unbounded memory growth proportional to subscriber count over application lifetime |
| **Fix** | Unsubscribe explicitly. Consider `WeakEventManager` (WPF) or a custom weak event pattern. Avoid static events where possible |

```csharp
// BAD -- static event holds reference to every Widget created
class EventBus
{
    public static event EventHandler? GlobalEvent;
}

class Widget  // every Widget instance leaks!
{
    public Widget()
    {
        EventBus.GlobalEvent += OnGlobalEvent;
    }

    private void OnGlobalEvent(object? sender, EventArgs e) { }
    // No unsubscription -- Widget can never be GC'd
}

// GOOD -- unsubscribe in Dispose
class Widget : IDisposable
{
    public Widget() => EventBus.GlobalEvent += OnGlobalEvent;
    private void OnGlobalEvent(object? sender, EventArgs e) { }

    public void Dispose()
    {
        EventBus.GlobalEvent -= OnGlobalEvent;
    }
}
```

---

#### ML-GC-06: Closure Capturing `this` Preventing Collection

| Field | Detail |
|-------|--------|
| **ID** | ML-GC-06 |
| **Severity** | High |
| **Signal** | Lambda or delegate capturing `this` (by referencing an instance member) passed to a long-lived object (timer, event, cache, static field, ConcurrentDictionary) |
| **Risk** | The closure holds a strong reference to `this`, preventing the enclosing object from being collected. If the long-lived object accumulates closures, causes memory leak |
| **Fix** | Capture only the needed value (not `this`). Use a local variable for the member value. Or use a weak reference pattern |

```csharp
// BAD -- lambda captures 'this' via _id field access
class Worker
{
    private readonly string _id;
    public void Register(TaskScheduler scheduler)
    {
        scheduler.OnTick += () => Console.WriteLine(_id);
        // closure captures 'this' to access _id
        // Worker cannot be GC'd while scheduler lives
    }
}

// GOOD -- capture only the value, not 'this'
class Worker
{
    private readonly string _id;
    public void Register(TaskScheduler scheduler)
    {
        var id = _id;  // capture the value, not 'this'
        scheduler.OnTick += () => Console.WriteLine(id);
    }
}
```

---

#### ML-GC-07: String Concatenation in Loop Creating GC Pressure

| Field | Detail |
|-------|--------|
| **ID** | ML-GC-07 |
| **Severity** | Medium |
| **Signal** | `string +=` or `string.Concat` inside a loop body |
| **Risk** | Each concatenation allocates a new string, copying all previous content. In a loop of N iterations, creates N intermediate strings with O(N^2) total bytes allocated. Causes GC pressure and latency |
| **Fix** | Use `StringBuilder` for loops. For known-count joins, use `string.Join`. For interpolation, use `string.Create` or `DefaultInterpolatedStringHandler` |

```csharp
// BAD -- O(N^2) allocations
string result = "";
foreach (var item in items)
{
    result += item.ToString() + ", ";  // new string each iteration
}

// GOOD -- StringBuilder
var sb = new StringBuilder();
foreach (var item in items)
{
    sb.Append(item).Append(", ");
}
string result = sb.ToString();

// GOOD -- string.Join for simple cases
string result = string.Join(", ", items);
```

---

#### ML-GC-08: Boxing Value Types in Hot Path

| Field | Detail |
|-------|--------|
| **ID** | ML-GC-08 |
| **Severity** | Medium |
| **Signal** | Value type (struct, int, enum) passed to parameter typed as `object`, `IComparable`, or other interface without generic overload. Common in `string.Format`, `Console.WriteLine`, non-generic collections |
| **Risk** | Each boxing allocates a small Gen0 object. In hot paths, high-frequency boxing creates GC pressure. Can also cause subtle equality bugs (comparing boxed values with `==` gives reference equality) |
| **Fix** | Use generic overloads. Use `string.Concat` overloads or interpolation (C# 10+ has interpolated string handlers that avoid boxing). Avoid non-generic collections for value types |

```csharp
// BAD -- boxing int to object in hot path
for (int i = 0; i < 1_000_000; i++)
{
    object boxed = i;  // boxing allocation
    dictionary[boxed] = Process(i);
}

// BAD -- implicit boxing in string format
int count = 42;
string msg = string.Format("Count: {0}", count);  // boxes 'count'

// GOOD -- generic collection, no boxing
var dictionary = new Dictionary<int, Result>();
for (int i = 0; i < 1_000_000; i++)
{
    dictionary[i] = Process(i);  // no boxing
}

// GOOD -- string interpolation (C# 10+ avoids boxing)
int count = 42;
string msg = $"Count: {count}";  // DefaultInterpolatedStringHandler -- no boxing
```

---

#### ML-GC-09: LINQ `.ToList()`/`.ToArray()` in Hot Path Creating Unnecessary Allocations

| Field | Detail |
|-------|--------|
| **ID** | ML-GC-09 |
| **Severity** | Medium |
| **Signal** | `.ToList()`, `.ToArray()`, `.ToDictionary()` called on LINQ query in a hot path when the result is only iterated once or could use `IEnumerable<T>` |
| **Risk** | Materializes the entire sequence into a new collection, allocating backing array. In hot paths, creates GC pressure. `.ToList()` also over-allocates due to list growth strategy |
| **Fix** | Use deferred execution (`IEnumerable<T>`) when the result is iterated once. If count is known, pre-size the collection. Consider `Span<T>` or `stackalloc` for small known-size results |

```csharp
// BAD -- materializes to list just to iterate
var filtered = items.Where(x => x.IsActive).ToList();  // unnecessary allocation
foreach (var item in filtered)
{
    Process(item);
}

// GOOD -- deferred execution, no allocation
var filtered = items.Where(x => x.IsActive);
foreach (var item in filtered)
{
    Process(item);
}

// GOOD -- if you need count, check without materializing
int activeCount = items.Count(x => x.IsActive);
```

---

#### ML-GC-10: ConditionalWeakTable Misuse -- Wrong Key/Value Semantics

| Field | Detail |
|-------|--------|
| **ID** | ML-GC-10 |
| **Severity** | Medium |
| **Signal** | `ConditionalWeakTable<TKey, TValue>` where the key and value have inverted lifetimes, or where value holds a strong reference back to key (creating an implicit strong reference cycle that defeats weak collection) |
| **Risk** | Key is never collected because value (which has an implicitly attached lifetime) references it back. Defeats the purpose of the weak table. Or: key is short-lived and values are expensive, causing repeated re-creation |
| **Fix** | Ensure key is the long-lived "anchor" object and value is the attached metadata. Never store back-references from value to key |

```csharp
// BAD -- value holds strong reference to key, defeats weak collection
var table = new ConditionalWeakTable<Target, Metadata>();
var target = new Target();
table.Add(target, new Metadata(target));  // Metadata holds ref to Target!
// Target cannot be GC'd because Metadata (attached to its CWT entry) references it

// GOOD -- value does not reference key
var table = new ConditionalWeakTable<Target, Metadata>();
var target = new Target();
table.Add(target, new Metadata(target.Id));  // stores ID, not the object
```

---

### Unsafe Code & Interop

#### ML-UNSAFE-01: `unsafe` Block Without Documented Safety Justification

| Field | Detail |
|-------|--------|
| **ID** | ML-UNSAFE-01 |
| **Severity** | High |
| **Signal** | `unsafe` keyword in method or block without a comment explaining why unsafe is required and what invariants maintain safety |
| **Risk** | Unsafe code opts out of CLR memory safety guarantees. Without documented safety contracts, future refactors can silently introduce buffer overruns, type confusion, or dangling pointer use |
| **Fix** | Add safety documentation comment explaining: (1) why unsafe is needed, (2) what invariants ensure safety, (3) what bounds are guaranteed. Consider if `Span<T>` can replace the unsafe code |

```csharp
// BAD -- no justification or safety documentation
unsafe void ProcessBuffer(byte* data, int length)
{
    for (int i = 0; i < length; i++)
        data[i] ^= 0xFF;
}

// GOOD -- documented safety contract
/// <summary>
/// XORs each byte in the buffer. Uses unsafe for performance-critical
/// inner loop (measured 3x faster than Span for this workload).
/// </summary>
/// <remarks>
/// SAFETY: Caller guarantees data points to a valid buffer of at least
/// 'length' bytes. Length is validated non-negative. Buffer is pinned
/// for the duration of this call.
/// </remarks>
unsafe void ProcessBuffer(byte* data, int length)
{
    ArgumentOutOfRangeException.ThrowIfNegative(length);
    for (int i = 0; i < length; i++)
        data[i] ^= 0xFF;
}
```

---

#### ML-UNSAFE-02: Buffer Overrun in `fixed`/`stackalloc` -- No Bounds Checking

| Field | Detail |
|-------|--------|
| **ID** | ML-UNSAFE-02 |
| **Severity** | Critical |
| **Signal** | Pointer arithmetic in `fixed` or `stackalloc` block without bounds validation. Indexing beyond allocated size |
| **Risk** | Buffer overrun -- reads/writes arbitrary memory. Can corrupt GC heap, overwrite return addresses (stack buffer overflow), or leak sensitive data. Exploitable for code execution |
| **Fix** | Validate all indices against allocated size. Prefer `Span<T>` over raw pointers (Span has built-in bounds checking). If pointer math is required, assert bounds at every access |

```csharp
// BAD -- no bounds check on index
unsafe void CopyData(byte* source, int sourceLen, int offset)
{
    fixed (byte* dest = _buffer)
    {
        dest[offset] = source[0];  // offset not validated -- overrun!
    }
}

// GOOD -- bounds checked, prefer Span
void CopyData(ReadOnlySpan<byte> source, int offset)
{
    var dest = _buffer.AsSpan();
    if (offset < 0 || offset >= dest.Length)
        throw new ArgumentOutOfRangeException(nameof(offset));
    dest[offset] = source[0];  // Span also does runtime bounds check
}
```

---

#### ML-UNSAFE-03: P/Invoke Without Proper Marshaling Attributes

| Field | Detail |
|-------|--------|
| **ID** | ML-UNSAFE-03 |
| **Severity** | High |
| **Signal** | `[DllImport]` or `[LibraryImport]` declaration with string parameters missing `[MarshalAs]`, or struct parameters without `[StructLayout]` |
| **Risk** | Incorrect marshaling causes data corruption at the managed/native boundary. Strings may be marshaled as ANSI instead of Unicode. Struct layout may not match native expectations, causing field misalignment |
| **Fix** | Explicitly specify `CharSet.Unicode` or `[MarshalAs(UnmanagedType.LPWStr)]`. Use `[StructLayout(LayoutKind.Sequential)]` on interop structs. Prefer `[LibraryImport]` (source-generated, .NET 7+) over `[DllImport]` |

```csharp
// BAD -- no CharSet, defaults to ANSI on DllImport
[DllImport("kernel32.dll")]
static extern bool CreateDirectoryW(string path, IntPtr security);
// 'path' marshaled as ANSI by default -- CreateDirectoryW expects UTF-16!

// GOOD -- explicit CharSet
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
static extern bool CreateDirectory(string path, IntPtr security);

// BETTER -- LibraryImport (source-generated, .NET 7+)
[LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
[return: MarshalAs(UnmanagedType.Bool)]
static partial bool CreateDirectory(string path, IntPtr security);
```

---

#### ML-UNSAFE-04: `GCHandle.Alloc` Without Corresponding `Free`

| Field | Detail |
|-------|--------|
| **ID** | ML-UNSAFE-04 |
| **Severity** | High |
| **Signal** | `GCHandle.Alloc(...)` without matching `handle.Free()` on all code paths (including exception paths) |
| **Risk** | GC handle leak. Leaked handles pin or track objects indefinitely, preventing GC collection and compaction. Under load, causes unbounded memory growth |
| **Fix** | Use `try/finally` to ensure `Free()`. Better: wrap in a custom `IDisposable` or use `SafeHandle`-derived type |

```csharp
// BAD -- Free not called on exception path
var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
IntPtr ptr = handle.AddrOfPinnedObject();
NativeMethod(ptr);  // if this throws, handle leaks!
handle.Free();

// GOOD -- try/finally ensures Free
var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
try
{
    IntPtr ptr = handle.AddrOfPinnedObject();
    NativeMethod(ptr);
}
finally
{
    handle.Free();
}
```

---

#### ML-UNSAFE-05: Span&lt;T&gt;/Memory&lt;T&gt; Escaping Intended Scope

| Field | Detail |
|-------|--------|
| **ID** | ML-UNSAFE-05 |
| **Severity** | High |
| **Signal** | `Span<T>` stored in a field, returned from an async method, captured in a lambda, or passed to a method that stores it. `Memory<T>` from `stackalloc` escaping the stack frame |
| **Risk** | `Span<T>` is a stack-only type by design. If it escapes (through async state machine, closure, or field), it can reference freed stack memory or moved heap memory, causing undefined behavior |
| **Fix** | Never store `Span<T>` in fields or closures. Use `Memory<T>` for heap-based scenarios. Never create `Memory<T>` from `stackalloc`. The compiler catches most cases, but `Unsafe` APIs can circumvent this |

```csharp
// BAD -- Span cannot be used in async method
async Task ProcessAsync(Span<byte> data)  // COMPILER ERROR: Span in async
{
    await Task.Delay(100);
    Process(data);  // data could reference freed stack after await
}

// BAD -- stackalloc memory escaping via Memory<T>
Memory<byte> GetBuffer()
{
    Span<byte> span = stackalloc byte[256];
    return span.ToArray();  // This allocates (safe but defeats purpose)
    // Never try: Unsafe.As<Span<byte>, Memory<byte>>(ref span) -- undefined behavior
}

// GOOD -- use Memory<T> for async scenarios
async Task ProcessAsync(Memory<byte> data)
{
    await Task.Delay(100);
    Process(data.Span);  // safe: Memory<T> is heap-backed
}
```

---

#### ML-UNSAFE-06: `Unsafe.As&lt;T&gt;` Without Type Compatibility Guarantee

| Field | Detail |
|-------|--------|
| **ID** | ML-UNSAFE-06 |
| **Severity** | Critical |
| **Signal** | `Unsafe.As<TFrom, TTo>()` or `Unsafe.As<T>()` used to reinterpret memory between types that do not have guaranteed layout compatibility |
| **Risk** | Type confusion -- reads fields at wrong offsets, corrupts GC tracked references, or creates invalid object states. Can crash the runtime or be exploited for code execution. The GC may corrupt the heap if reference types are reinterpreted incorrectly |
| **Fix** | Only use `Unsafe.As` between types with identical layout (same `[StructLayout]`, same field order/sizes). Document the layout guarantee. Prefer `MemoryMarshal.Cast` for span reinterpretation |

```csharp
// BAD -- reinterpreting unrelated types
var intArray = new int[] { 1, 2, 3 };
ref float f = ref Unsafe.As<int, float>(ref intArray[0]);
// type confusion -- float and int have different bit interpretations
// GC may misinterpret reference fields

// GOOD -- safe reinterpretation via MemoryMarshal for blittable types
ReadOnlySpan<int> intSpan = new int[] { 1, 2, 3 };
ReadOnlySpan<byte> byteSpan = MemoryMarshal.AsBytes(intSpan);
// Safe: both are blittable, no reference types involved
```

---

#### ML-UNSAFE-07: `stackalloc` Without Size Guard (Stack Overflow Risk)

| Field | Detail |
|-------|--------|
| **ID** | ML-UNSAFE-07 |
| **Severity** | Critical |
| **Signal** | `stackalloc` with a size parameter derived from user input, method parameter, or any non-constant value, without an upper bound check |
| **Risk** | Stack overflow -- if size is large or negative (when cast to unsigned), allocates beyond the stack limit, crashing the process. Stack overflow in .NET is unrecoverable (no catch) |
| **Fix** | Always validate maximum size before `stackalloc`. Use a threshold pattern: `stackalloc` for small sizes, fall back to `ArrayPool` for large. Common threshold: 256 or 512 bytes |

```csharp
// BAD -- user-controlled size, no guard
unsafe void Format(int length)
{
    char* buffer = stackalloc char[length];  // stack overflow if length is huge!
    // ...
}

// GOOD -- threshold pattern with fallback
void Format(int length)
{
    const int StackAllocThreshold = 256;
    char[]? rented = null;
    Span<char> buffer = length <= StackAllocThreshold
        ? stackalloc char[StackAllocThreshold]
        : (rented = ArrayPool<char>.Shared.Rent(length));
    try
    {
        var usable = buffer[..length];
        // ... use usable ...
    }
    finally
    {
        if (rented is not null)
            ArrayPool<char>.Shared.Return(rented);
    }
}
```

---

#### ML-UNSAFE-08: P/Invoke `SetLastError=true` Without Checking `Marshal.GetLastWin32Error()`

| Field | Detail |
|-------|--------|
| **ID** | ML-UNSAFE-08 |
| **Severity** | Medium |
| **Signal** | `[DllImport(..., SetLastError = true)]` but the calling code checks only the return value without calling `Marshal.GetLastWin32Error()` for detailed error information |
| **Risk** | Diagnostic loss -- when the native function fails, the specific Win32 error code is available but ignored. Makes debugging production failures significantly harder. Also, if `SetLastError = true` is specified but not needed, it adds unnecessary overhead (runtime captures `GetLastError` after every call) |
| **Fix** | When `SetLastError = true`, call `Marshal.GetLastWin32Error()` on failure and include it in the exception. If the error code is not needed, remove `SetLastError = true` to avoid overhead |

```csharp
// BAD -- SetLastError specified but error code ignored
[DllImport("kernel32.dll", SetLastError = true)]
static extern IntPtr CreateFile(string name, uint access, uint share,
    IntPtr security, uint creation, uint flags, IntPtr template);

var handle = CreateFile(path, 0x80000000, 1, IntPtr.Zero, 3, 0, IntPtr.Zero);
if (handle == new IntPtr(-1))
    throw new Exception("CreateFile failed");  // no error code!

// GOOD -- error code captured and reported
var handle = CreateFile(path, 0x80000000, 1, IntPtr.Zero, 3, 0, IntPtr.Zero);
if (handle == new IntPtr(-1))
{
    int error = Marshal.GetLastWin32Error();
    throw new Win32Exception(error);  // includes error message
}
```

---

### Object Pooling & Reuse

#### ML-POOL-01: ArrayPool&lt;T&gt;.Shared.Rent Without Return

| Field | Detail |
|-------|--------|
| **ID** | ML-POOL-01 |
| **Severity** | High |
| **Signal** | `ArrayPool<T>.Shared.Rent(...)` without corresponding `ArrayPool<T>.Shared.Return(...)` on all code paths |
| **Risk** | Pool exhaustion. The shared pool has a limited number of buffers. Unreturned arrays are effectively leaked. Under sustained load, the pool creates new arrays for every rent, defeating the purpose and increasing GC pressure |
| **Fix** | Use `try/finally` to guarantee return. Or wrap in a disposable helper |

```csharp
// BAD -- no return on exception path
var buffer = ArrayPool<byte>.Shared.Rent(4096);
ReadIntoBuffer(buffer);  // if this throws, buffer not returned
ProcessBuffer(buffer);
ArrayPool<byte>.Shared.Return(buffer);

// GOOD -- try/finally guarantees return
var buffer = ArrayPool<byte>.Shared.Rent(4096);
try
{
    ReadIntoBuffer(buffer);
    ProcessBuffer(buffer);
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer);
}
```

---

#### ML-POOL-02: ObjectPool Returning Object with Stale State

| Field | Detail |
|-------|--------|
| **ID** | ML-POOL-02 |
| **Severity** | High |
| **Signal** | Object returned to pool without clearing sensitive or request-specific state. Next rent gets object with previous user's data |
| **Risk** | Data leak between requests/users. Security vulnerability in multi-tenant systems. Stale state causes logic bugs when next consumer assumes fresh state |
| **Fix** | Implement `IResettable` or clear state in the pool's `Return` policy. Validate state on rent. Clear security-sensitive fields (auth tokens, user IDs) before returning to pool |

```csharp
// BAD -- returning to pool without clearing state
class RequestContext
{
    public string? UserId { get; set; }
    public string? AuthToken { get; set; }
    public List<string> Errors { get; } = new();
}

pool.Return(context);  // next request gets previous user's ID and token!

// GOOD -- clear state before returning
class RequestContext : IResettable
{
    public string? UserId { get; set; }
    public string? AuthToken { get; set; }
    public List<string> Errors { get; } = new();

    public bool TryReset()
    {
        UserId = null;
        AuthToken = null;
        Errors.Clear();
        return true;
    }
}
```

---

#### ML-POOL-03: MemoryPool Allocation Not Disposed

| Field | Detail |
|-------|--------|
| **ID** | ML-POOL-03 |
| **Severity** | High |
| **Signal** | `MemoryPool<T>.Shared.Rent(...)` returning `IMemoryOwner<T>` without `using` or `Dispose()` |
| **Risk** | Same as ArrayPool -- pool exhaustion and memory leak. `IMemoryOwner<T>` implements `IDisposable`; failing to dispose prevents the memory from being returned to the pool |
| **Fix** | Always use `using` with `IMemoryOwner<T>` |

```csharp
// BAD -- IMemoryOwner not disposed
IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(1024);
Memory<byte> memory = owner.Memory;
ProcessData(memory);
// owner never disposed -- memory not returned to pool

// GOOD -- using ensures disposal
using IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(1024);
Memory<byte> memory = owner.Memory;
ProcessData(memory);
// owner.Dispose() called automatically, returns memory to pool
```

---

#### ML-POOL-04: StringBuilder from Pool Not Cleared Before Reuse

| Field | Detail |
|-------|--------|
| **ID** | ML-POOL-04 |
| **Severity** | Medium |
| **Signal** | `ObjectPool<StringBuilder>` or `StringBuilderCache` used without clearing contents before reuse. Or: `StringBuilder` returned to pool with large internal buffer causing memory retention |
| **Risk** | Stale content leaks into next usage. Large retained capacity wastes memory. If using `StringBuilderCache` (internal .NET pattern), exceeding the cache size silently skips caching |
| **Fix** | Clear `StringBuilder` on acquire (not just return). Set maximum retained capacity. Use `StringBuilder.Clear()` and check `Capacity` before returning |

```csharp
// BAD -- stale content from previous use
var sb = _pool.Get();
sb.Append("Current: ");  // may contain "Previous: some old data" still!
var result = sb.ToString();
_pool.Return(sb);

// GOOD -- clear on acquire, cap capacity on return
var sb = _pool.Get();
sb.Clear();  // ensure clean state
sb.Append("Current: ");
var result = sb.ToString();
if (sb.Capacity > 1024)
    sb.Capacity = 1024;  // prevent memory retention from one-time large use
_pool.Return(sb);
```

---

## Search Priority

Rank patterns by real-world impact when time is limited. Start with the highest-impact categories first.

| Priority | Category | Patterns | Rationale |
|----------|----------|----------|-----------|
| 1 | **HttpClient per-request** | ML-DISP-12 | Socket exhaustion is the single most common production outage from .NET resource misuse |
| 2 | **Missing IDisposable / using** | ML-DISP-01, ML-DISP-02, ML-DISP-03 | Handle/connection/file leaks cause cascading failures under load |
| 3 | **Event handler leaks** | ML-DISP-08, ML-GC-05, ML-GC-06 | Memory growth in long-running services (web servers, Windows services) |
| 4 | **ArrayPool / MemoryPool leaks** | ML-POOL-01, ML-POOL-03 | Pool exhaustion defeats the allocation reduction that pooling provides |
| 5 | **Unsafe code safety** | ML-UNSAFE-02, ML-UNSAFE-06, ML-UNSAFE-07 | Security vulnerabilities -- buffer overruns and type confusion |
| 6 | **P/Invoke correctness** | ML-UNSAFE-03, ML-UNSAFE-04, ML-UNSAFE-08 | Data corruption at native boundary, handle leaks |
| 7 | **GC pressure in hot paths** | ML-GC-01, ML-GC-07, ML-GC-08, ML-GC-09 | Latency spikes under load from frequent GC |
| 8 | **Dispose pattern correctness** | ML-DISP-04, ML-DISP-05, ML-DISP-10 | Correctness for types with finalizers |
| 9 | **Async dispose** | ML-DISP-11 | Thread starvation in async web applications |
| 10 | **Object pool data safety** | ML-POOL-02, ML-POOL-04 | Security in multi-tenant systems |

---

## Fix Strategy Decision Trees

### Decision Tree 1: Resource Leak Fix

```
Is the resource an IDisposable local variable?
├── YES → Wrap in `using` declaration or statement
│   ├── Is the method async and the resource IAsyncDisposable?
│   │   ├── YES → Use `await using`
│   │   └── NO → Use `using`
│   └── Is the resource returned to the caller?
│       ├── YES → Document ownership transfer, caller must dispose
│       └── NO → Scope the using to the smallest needed block
├── NO → Is the resource a field of a class?
│   ├── YES → Implement IDisposable on the containing type
│   │   ├── Does the type have a finalizer?
│   │   │   ├── YES → Full Dispose pattern: Dispose(bool) + GC.SuppressFinalize
│   │   │   └── NO → Simple IDisposable with _disposed guard
│   │   └── Dispose all IDisposable fields in Dispose()
│   └── Is the resource from a pool (ArrayPool, MemoryPool)?
│       ├── YES → Use try/finally to guarantee Return/Dispose
│       └── NO → Identify the owner and ensure it manages the lifetime
```

### Decision Tree 2: GC Pressure Fix

```
What type of allocation is causing pressure?
├── Large array (>85KB) in hot path
│   ├── Temporary buffer → Use ArrayPool<T>.Shared.Rent/Return
│   ├── Stream → Use RecyclableMemoryStreamManager
│   └── Persistent but resizable → Pre-allocate, reuse via field
├── String concatenation in loop
│   ├── Building one result → StringBuilder
│   ├── Joining with separator → string.Join
│   └── Formatting → string.Create or interpolated string handler
├── Boxing in hot path
│   ├── Collection → Use generic collection (List<T> not ArrayList)
│   ├── String formatting → Use typed overloads or interpolation
│   └── Interface dispatch → Use generic constraints (where T : IComparable<T>)
├── LINQ materializing (.ToList/.ToArray)
│   ├── Only iterating once → Remove materialization, use IEnumerable<T>
│   ├── Need count → Use .Count() without materializing
│   └── Need indexed access → Materialize but consider Span for small sets
└── Closures capturing this
    ├── Capture only needed value → Extract to local variable
    ├── Need weak reference → Use WeakReference<T> pattern
    └── Timer/event callback → Unsubscribe in Dispose
```

### Decision Tree 3: Unsafe Code Fix

```
Is 'unsafe' actually needed?
├── Can Span<T> replace the pointer use?
│   ├── YES → Replace unsafe with Span<T>, get bounds checking for free
│   └── NO → Document why unsafe is required
├── Is this P/Invoke?
│   ├── YES →
│   │   ├── Use [LibraryImport] instead of [DllImport] (.NET 7+)
│   │   ├── Specify CharSet/StringMarshalling explicitly
│   │   ├── Use SafeHandle instead of IntPtr for handles
│   │   └── If SetLastError=true, check Marshal.GetLastWin32Error()
│   └── NO → Is this stackalloc?
│       ├── YES → Add size guard with threshold pattern (stackalloc for small, pool for large)
│       └── NO → Is this GCHandle?
│           ├── YES → Use try/finally for Free(), minimize pin duration
│           └── NO → Is this Unsafe.As<T>?
│               ├── YES → Document layout compatibility guarantee, prefer MemoryMarshal
│               └── NO → Review case by case, document safety contract
```

---

## Analyzer Coverage

Many patterns in this catalog are detectable by .NET analyzers. Enable these in your `.editorconfig` or `Directory.Build.props`:

| Analyzer Rule | Pattern(s) Covered | Severity |
|---------------|-------------------|----------|
| CA2000 | ML-DISP-03 (Dispose objects before losing scope) | Warning |
| CA1816 | ML-DISP-04 (Call GC.SuppressFinalize correctly) | Warning |
| CA2213 | ML-DISP-02 (Disposable fields should be disposed) | Warning |
| CA2215 | ML-DISP-02 (Dispose methods should call base) | Warning |
| CA1001 | ML-DISP-01 (Types that own disposable fields) | Warning |
| CA2216 | ML-DISP-05 (Disposable types should declare finalizer) | Warning |
| CA1063 | ML-DISP-04, ML-DISP-05, ML-DISP-10 (Implement IDisposable correctly) | Warning |
| CA1806 | ML-UNSAFE-08 (Do not ignore method results) | Info |
| IDE0058 | ML-POOL-01 (Expression value is never used) | Suggestion |

---

## References

1. .NET Framework Design Guidelines -- IDisposable Pattern (Microsoft Learn)
2. Microsoft Learn -- "Implement a Dispose method" (https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/implementing-dispose)
3. Microsoft Learn -- "Memory management and garbage collection" (https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/)
4. Microsoft Learn -- "Unsafe code and pointers" (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/unsafe-code)
5. .NET API Analyzers -- CA2000, CA2213, CA1001 series (https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/)
