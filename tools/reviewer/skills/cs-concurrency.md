---
name: cs-concurrency-review
description: >-
  Review C# code for concurrency and thread safety defects: async/await
  misuse (async void, sync-over-async deadlocks, ValueTask double consumption),
  lock correctness (lock(this), lock ordering violations, await inside lock,
  SemaphoreSlim leaks), thread affinity violations (cross-thread UI access,
  missing Dispatcher marshaling, blocking UI thread), and shared state races
  (unsynchronized static fields, concurrent Dictionary/List access, TOCTOU
  check-then-act, bool flags without volatile).
  38 patterns from .NET Runtime team guidance covering async-await-correctness,
  synchronization-and-locking,
  thread-affinity-and-UI-threading, and shared-state-data-races domains.
  Use this skill when reviewing C# code that uses async/await, threads,
  locks, concurrent collections, or dispatches work across thread boundaries.
---

# C# Concurrency & Thread Safety Code Review

## Quick Detection

**Primary Symptoms (in code under review)**:
- `async void` methods that are not event handlers
- `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` called in async context
- `lock(this)` or `lock(typeof(T))` or `lock("string literal")`
- `await` expression inside a `lock` block (or `Monitor.Enter` equivalent)
- UI elements accessed from `Task.Run` or background threads
- `Dictionary<K,V>` or `List<T>` shared across threads without synchronization
- Static mutable fields with no lock or `volatile` annotation
- Fire-and-forget `_ = DoAsync()` without error handling

**Key Code Patterns to Search For**:
```csharp
// async void (non-event-handler) -- unobserved exception crashes process
async void ProcessData() { await Task.Delay(100); throw new Exception(); }

// Sync-over-async deadlock
var result = GetDataAsync().Result;  // Deadlock on UI/ASP.NET SynchronizationContext

// Lock on wrong target
lock (this) { ... }         // External code can lock on same instance
lock (typeof(MyClass)) { }  // Process-wide contention
lock ("cache") { }           // Interned strings share the same lock

// TOCTOU on shared state
if (_dict.ContainsKey(key))  // Thread A checks
    _dict[key].DoWork();     // Thread B may have removed key
```

## Analysis Workflow

### Step 1: Map Async Boundaries & Shared State

Identify all concurrency hazards in the component.

1. **Find async entry points**:
   - Search for `async void`, `async Task`, `async ValueTask` methods
   - Identify fire-and-forget calls: `_ = SomeAsync()` or `SomeAsync()` without await

2. **Find synchronization primitives**:
   - Search for `lock(`, `SemaphoreSlim`, `ReaderWriterLockSlim`, `Monitor.`, `Mutex`, `Interlocked.`
   - Map which shared state each primitive protects

3. **Find cross-thread boundaries**:
   - `Task.Run`, `ThreadPool.QueueUserWorkItem`, `new Thread(`
   - `Dispatcher.Invoke`, `DispatcherQueue.TryEnqueue`, `SynchronizationContext.Post`

4. **Build a Shared State Map**:
   | Variable | Type | Protected By | Accessed From |
   |----------|------|-------------|---------------|
   | `_cache` | `Dictionary<string, Data>` | `_lock` | UI thread, Task.Run |
   | `_isRunning` | `bool` | None (BUG) | Multiple threads |

### Step 2: Scan for Pattern Matches

Apply the 38 concurrency patterns below, in priority order.

**Search priority ranking** (by severity and frequency):

| Priority | Category | Pattern Count | Impact |
|----------|----------|---------------|--------|
| 1 | async/await Correctness | 12 | Deadlocks, crashes, silent failures |
| 2 | Synchronization & Locking | 10 | Deadlocks, resource leaks, data corruption |
| 3 | Thread Affinity & UI Threading | 8 | UI crashes, InvalidOperationException, hangs |
| 4 | Shared State & Data Races | 8 | Data corruption, intermittent crashes |

### Step 3: Classify Findings

For each potential match:

1. **Confirm the hazard**: Can two threads actually reach the conflicting operations concurrently?
   - Is the code only ever called from a single `SynchronizationContext`?
   - Is the parent caller already holding a lock?
   - Is this a single-threaded console app with no Task.Run?
2. **Severity**:
   - **Critical**: Process crash (async void exception), guaranteed deadlock, ValueTask double-consume
   - **High**: Likely deadlock, data corruption, UI thread violation
   - **Medium**: Performance issue, race on non-critical state, missing ConfigureAwait
   - **Low**: Style issue, unnecessary Task.Run, theoretical race on diagnostic data

---

## Pattern Catalog

### async/await Correctness (12 patterns)

---

#### CONC-ASYNC-01: `async void` method (non-event-handler)

**Severity**: Critical
**Why**: Unobserved exceptions in `async void` crash the process. There is no `Task` to observe, so the exception propagates to the `SynchronizationContext` and terminates the application.
**Source**: .NET async/await guidance

```csharp
// BAD: async void -- unobserved exception crashes process
async void ProcessData(string input)
{
    var data = await FetchDataAsync(input);
    await SaveAsync(data); // If this throws, process crashes
}

// GOOD: async Task -- caller can observe exception
async Task ProcessDataAsync(string input)
{
    var data = await FetchDataAsync(input);
    await SaveAsync(data); // Exception propagates to caller's Task
}
```

**Fix**: Change return type to `Task` or `Task<T>`. The only acceptable `async void` is a UI event handler (e.g., `async void Button_Click`).

---

#### CONC-ASYNC-02: `Task.Result` / `.Wait()` / `.GetAwaiter().GetResult()` in async context

**Severity**: Critical
**Why**: Blocks the calling thread. If that thread owns a `SynchronizationContext` (UI thread, legacy ASP.NET), the awaited Task's continuation is posted back to the same context, causing a deadlock.
**Source**: .NET async/await guidance ("Don't Block on Async Code")

```csharp
// BAD: deadlock on UI thread or ASP.NET classic
public string GetData()
{
    // This blocks the UI thread waiting for the task
    // The task's continuation needs the UI thread to complete
    return GetDataAsync().Result; // DEADLOCK
}

// GOOD: async all the way
public async Task<string> GetDataAsync()
{
    var result = await FetchAsync();
    return result;
}

// ACCEPTABLE (last resort, no SynchronizationContext):
// .GetAwaiter().GetResult() in Main() or background thread with no SyncContext
```

**Fix**: Make the caller async ("async all the way up"). If impossible (e.g., interface constraint), ensure no `SynchronizationContext` is present or use `ConfigureAwait(false)` throughout the async chain.

---

#### CONC-ASYNC-03: Missing `ConfigureAwait(false)` in library code

**Severity**: High
**Why**: Library code that awaits without `ConfigureAwait(false)` captures the caller's `SynchronizationContext`. If the caller is a UI thread that then blocks on the result, this causes a deadlock.
**Source**: .NET Runtime team guidelines

```csharp
// BAD: library code captures SynchronizationContext
public async Task<Data> LoadDataAsync()
{
    var json = await httpClient.GetStringAsync(url);     // captures context
    var parsed = await ParseAsync(json);                  // captures context
    return parsed;
}

// GOOD: library code opts out of context capture
public async Task<Data> LoadDataAsync()
{
    var json = await httpClient.GetStringAsync(url).ConfigureAwait(false);
    var parsed = await ParseAsync(json).ConfigureAwait(false);
    return parsed;
}
```

**Fix**: Add `.ConfigureAwait(false)` to every `await` in library/shared code. Application-level code (UI event handlers, controller actions) should NOT use `ConfigureAwait(false)` because it needs the context.

---

#### CONC-ASYNC-04: `async` method without any `await`

**Severity**: Medium
**Why**: The compiler generates a full async state machine even though the method runs synchronously. This adds allocation overhead and misleads readers into thinking the method is asynchronous.

```csharp
// BAD: async keyword with no await -- state machine overhead for nothing
async Task<int> ComputeAsync(int x)
{
    return x * 2; // No await anywhere
}

// GOOD: return Task directly
Task<int> ComputeAsync(int x)
{
    return Task.FromResult(x * 2);
}
```

**Fix**: Remove `async` keyword and return `Task.FromResult`, `Task.CompletedTask`, or `ValueTask.FromResult`. Exception: if the method needs try/catch around the return, keeping `async` is acceptable for proper exception handling semantics.

---

#### CONC-ASYNC-05: Fire-and-forget `Task` without error handling

**Severity**: High
**Why**: `_ = DoAsync()` discards the Task. If it faults, the exception goes unobserved. In .NET 4.0 this crashed the process; in .NET 4.5+ it is silently swallowed (which may be worse -- silent data loss).

```csharp
// BAD: fire-and-forget, exception silently lost
_ = SendTelemetryAsync(data);

// BAD: same problem without discard
SendTelemetryAsync(data); // CS4014 warning, exception lost

// GOOD: fire-and-forget with error handling
_ = SendTelemetryAsync(data).ContinueWith(
    t => Logger.Error(t.Exception!),
    TaskContinuationOptions.OnlyOnFaulted);

// GOOD: dedicated helper method
static async void SafeFireAndForget(Task task, Action<Exception>? onError = null)
{
    try { await task.ConfigureAwait(false); }
    catch (Exception ex) { onError?.Invoke(ex); }
}
```

**Fix**: Either await the task, add a `.ContinueWith(OnlyOnFaulted)`, or use a `SafeFireAndForget` helper that logs exceptions.

---

#### CONC-ASYNC-06: `CancellationToken` not propagated through async chain

**Severity**: Medium
**Why**: If a long-running async chain does not accept and forward `CancellationToken`, the caller cannot cancel the operation. This leads to wasted resources and unresponsive cancellation UX.

```csharp
// BAD: CancellationToken not propagated
public async Task<Report> GenerateReportAsync()
{
    var data = await FetchDataAsync();           // no token
    var result = await ProcessAsync(data);        // no token
    return result;
}

// GOOD: CancellationToken flows through the entire chain
public async Task<Report> GenerateReportAsync(CancellationToken ct = default)
{
    var data = await FetchDataAsync(ct).ConfigureAwait(false);
    var result = await ProcessAsync(data, ct).ConfigureAwait(false);
    return result;
}
```

**Fix**: Add `CancellationToken` parameter (with `= default`) to all async methods and pass it to every awaited call and I/O operation.

---

#### CONC-ASYNC-07: `Task.Delay` used as timer instead of `PeriodicTimer`

**Severity**: Medium
**Why**: `while (true) { await Task.Delay(interval); DoWork(); }` allocates a new `Task` and `Timer` on every iteration. `PeriodicTimer` (introduced in .NET 6) is purpose-built, non-overlapping, and allocation-efficient.

```csharp
// BAD: Task.Delay loop -- new allocation every tick
while (!ct.IsCancellationRequested)
{
    await Task.Delay(TimeSpan.FromSeconds(5), ct);
    await PollAsync(ct);
}

// GOOD: PeriodicTimer -- single allocation, non-overlapping ticks
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
while (await timer.WaitForNextTickAsync(ct))
{
    await PollAsync(ct);
}
```

**Fix**: Replace `while + Task.Delay` pattern with `PeriodicTimer` on .NET 6+.

---

#### CONC-ASYNC-08: `async` lambda passed to `void`-returning delegate

**Severity**: Critical
**Why**: If a delegate parameter is typed as `Action` (not `Func<Task>`), an `async` lambda becomes `async void` -- with all the same crash risks as CONC-ASYNC-01.

```csharp
// BAD: Action parameter turns async lambda into async void
someList.ForEach(async item =>
{
    await ProcessAsync(item); // This is async void! Unobserved exceptions crash.
});

// GOOD: Use proper async iteration
foreach (var item in someList)
{
    await ProcessAsync(item);
}

// GOOD: Or use Task.WhenAll for parallelism
await Task.WhenAll(someList.Select(item => ProcessAsync(item)));
```

**Fix**: Never pass `async` lambdas to methods expecting `Action` or `Action<T>`. Use `foreach` with `await`, or APIs that accept `Func<Task>`.

---

#### CONC-ASYNC-09: `ValueTask` consumed more than once

**Severity**: Critical
**Why**: Unlike `Task`, a `ValueTask` may be backed by a pooled `IValueTaskSource` that is recycled after the first consumption. Awaiting it twice, or calling `.Result` after `await`, causes undefined behavior (corruption, exceptions, wrong results).
**Source**: .NET blog

```csharp
// BAD: ValueTask consumed twice
ValueTask<int> vt = GetValueAsync();
int a = await vt;
int b = await vt; // UNDEFINED BEHAVIOR -- IValueTaskSource may be recycled

// BAD: Result after await
ValueTask<int> vt = GetValueAsync();
int a = await vt;
int b = vt.Result; // UNDEFINED BEHAVIOR

// GOOD: consume exactly once
int result = await GetValueAsync();

// GOOD: if you need the value twice, convert to Task first
Task<int> task = GetValueAsync().AsTask();
int a = await task;
int b = await task; // Task is safe to await multiple times
```

**Fix**: Always consume a `ValueTask` exactly once. If you need the result multiple times, call `.AsTask()` first.

---

#### CONC-ASYNC-10: Unnecessary `Task.Run` wrapping already-async method

**Severity**: Medium
**Why**: Wrapping an already-async method in `Task.Run` adds an unnecessary thread pool hop and task allocation. The async method already returns a Task that can be awaited directly.

```csharp
// BAD: unnecessary Task.Run wrapper
var data = await Task.Run(() => LoadDataAsync());

// GOOD: await directly
var data = await LoadDataAsync();

// EXCEPTION: Task.Run IS appropriate to offload CPU-bound sync work from UI thread
var result = await Task.Run(() => ComputeExpensiveSync(input));
```

**Fix**: Remove `Task.Run` wrapper around async methods. Only use `Task.Run` to offload synchronous CPU-bound work from the UI thread.

---

#### CONC-ASYNC-11: `Task.WhenAll` without handling individual task exceptions

**Severity**: Medium
**Why**: `Task.WhenAll` throws only the first exception via `AggregateException.InnerException`. If multiple tasks fail, subsequent exceptions are silently lost unless you inspect each task individually.

```csharp
// BAD: only first exception surfaces
try
{
    await Task.WhenAll(task1, task2, task3);
}
catch (Exception ex)
{
    Log(ex); // Only logs first failure
}

// GOOD: inspect all tasks
var allTasks = new[] { task1, task2, task3 };
try
{
    await Task.WhenAll(allTasks);
}
catch
{
    foreach (var t in allTasks.Where(t => t.IsFaulted))
    {
        Log(t.Exception!); // Logs every failure
    }
}
```

**Fix**: After catching the exception from `Task.WhenAll`, iterate over the task array and inspect each `.Exception` property.

---

#### CONC-ASYNC-12: Returning `Task` from `using` scope -- disposed resource used after return

**Severity**: High
**Why**: If an async method returns a `Task` without awaiting it inside a `using` block, the resource is disposed before the task completes.

```csharp
// BAD: stream disposed before task completes
Task<string> ReadDataAsync(string path)
{
    using var stream = File.OpenRead(path);
    using var reader = new StreamReader(stream);
    return reader.ReadToEndAsync(); // Returns task, then disposes stream!
}

// GOOD: await inside the using scope
async Task<string> ReadDataAsync(string path)
{
    using var stream = File.OpenRead(path);
    using var reader = new StreamReader(stream);
    return await reader.ReadToEndAsync(); // Awaits before disposal
}
```

**Fix**: Make the method `async` and `await` the returned task inside the `using` scope so disposal happens after completion.

---

### Synchronization & Locking (10 patterns)

---

#### CONC-LOCK-01: `lock(this)`

**Severity**: High
**Why**: External code can lock on the same object instance, causing unexpected contention or deadlock. The lock target should be a private, dedicated object.
**Source**: .NET Runtime team, Framework Design Guidelines

```csharp
// BAD: external code can do lock(myObj) and contend/deadlock
public void DoWork()
{
    lock (this)
    {
        // critical section
    }
}

// GOOD: private dedicated lock object
private readonly object _syncRoot = new();
public void DoWork()
{
    lock (_syncRoot)
    {
        // critical section
    }
}
```

**Fix**: Replace `lock(this)` with `lock(_privateField)` where `_privateField` is a `private readonly object`.

---

#### CONC-LOCK-02: `lock(typeof(T))`

**Severity**: High
**Why**: `typeof(T)` returns the same `Type` object process-wide. Any code in the process can lock on it, causing unrelated contention and potential deadlock.

```csharp
// BAD: process-wide lock
lock (typeof(MyService))
{
    _cache.Clear();
}

// GOOD: private static lock object
private static readonly object s_lock = new();
lock (s_lock)
{
    _cache.Clear();
}
```

**Fix**: Use a `private static readonly object` instead of `typeof(T)`.

---

#### CONC-LOCK-03: `lock` on string literal

**Severity**: High
**Why**: String literals are interned by the CLR. `lock("myLock")` in one class and `lock("myLock")` in a completely unrelated class share the same lock object.

```csharp
// BAD: interned strings share lock identity
lock ("CacheLock")
{
    // Another class might also lock("CacheLock") -- unintended contention
}

// GOOD: dedicated lock object
private readonly object _cacheLock = new();
lock (_cacheLock) { }
```

**Fix**: Always use a `private readonly object` as the lock target.

---

#### CONC-LOCK-04: Lock ordering violation between multiple locks

**Severity**: High (deadlock)
**Why**: If thread A acquires lock1 then lock2, and thread B acquires lock2 then lock1, both threads can block forever waiting for the other's lock.

```csharp
// BAD: inconsistent lock ordering -- Thread A and Thread B can deadlock
// Thread A path:
lock (_lockA)
{
    lock (_lockB) { Transfer(); }
}
// Thread B path:
lock (_lockB)
{
    lock (_lockA) { Transfer(); } // DEADLOCK with Thread A
}

// GOOD: consistent ordering -- always acquire _lockA before _lockB
// Thread A path:
lock (_lockA)
{
    lock (_lockB) { Transfer(); }
}
// Thread B path:
lock (_lockA) // Same order as Thread A
{
    lock (_lockB) { Transfer(); }
}
```

**Fix**: Establish and document a global lock ordering. All code paths must acquire locks in the same order. Alternatively, reduce to a single lock.

---

#### CONC-LOCK-05: `await` inside `lock` block

**Severity**: Critical
**Why**: The C# compiler forbids `await` inside `lock`. But developers sometimes work around this using `Monitor.Enter/Exit` directly, which is even worse -- the `await` may resume on a different thread, and `Monitor.Exit` requires the same thread that called `Enter`.

```csharp
// BAD: Monitor used to work around lock+await restriction
Monitor.Enter(_syncRoot);
try
{
    await DoWorkAsync(); // May resume on different thread!
    // Monitor.Exit will throw SynchronizationLockException
}
finally
{
    Monitor.Exit(_syncRoot); // WRONG THREAD -- exception or undefined behavior
}

// GOOD: use SemaphoreSlim for async-compatible locking
private readonly SemaphoreSlim _semaphore = new(1, 1);

await _semaphore.WaitAsync();
try
{
    await DoWorkAsync();
}
finally
{
    _semaphore.Release();
}
```

**Fix**: Use `SemaphoreSlim(1, 1)` with `WaitAsync()` for async-compatible mutual exclusion. Never use `Monitor` across `await` boundaries.

---

#### CONC-LOCK-06: `SemaphoreSlim.WaitAsync()` without `try/finally Release()`

**Severity**: High (semaphore leak)
**Why**: If the code between `WaitAsync()` and `Release()` throws, the semaphore count is permanently decremented. Eventually, no thread can acquire it.

```csharp
// BAD: exception leaves semaphore acquired forever
await _semaphore.WaitAsync();
await DoRiskyWorkAsync(); // If this throws, semaphore is leaked
_semaphore.Release();

// GOOD: try/finally ensures release
await _semaphore.WaitAsync();
try
{
    await DoRiskyWorkAsync();
}
finally
{
    _semaphore.Release();
}
```

**Fix**: Always wrap the critical section in `try/finally` with `Release()` in the `finally` block.

---

#### CONC-LOCK-07: `ReaderWriterLockSlim` without `try/finally` exit

**Severity**: High
**Why**: Same as CONC-LOCK-06 -- if the code throws between `Enter*Lock()` and `Exit*Lock()`, the lock is held forever, causing deadlocks for all subsequent acquirers.

```csharp
// BAD: exception leaves lock acquired
_rwLock.EnterReadLock();
var data = LoadData(); // If this throws, read lock is never released
_rwLock.ExitReadLock();

// GOOD: try/finally
_rwLock.EnterReadLock();
try
{
    var data = LoadData();
}
finally
{
    _rwLock.ExitReadLock();
}
```

**Fix**: Always wrap `ReaderWriterLockSlim` usage in `try/finally`.

---

#### CONC-LOCK-08: Double-checked locking without `volatile` or `Lazy<T>`

**Severity**: High
**Why**: Without `volatile` (or `Lazy<T>`), the compiler/CPU may reorder writes such that another thread sees a non-null reference to a partially constructed object.

```csharp
// BAD: classic double-checked locking bug
private static MyService _instance;
private static readonly object _lock = new();

public static MyService Instance
{
    get
    {
        if (_instance == null)
        {
            lock (_lock)
            {
                if (_instance == null)
                    _instance = new MyService(); // May be reordered!
            }
        }
        return _instance; // Other thread may see partially constructed object
    }
}

// GOOD: use Lazy<T>
private static readonly Lazy<MyService> _instance =
    new(() => new MyService(), LazyThreadSafetyMode.ExecutionAndPublication);

public static MyService Instance => _instance.Value;

// ALSO GOOD: volatile field
private static volatile MyService _instance;
```

**Fix**: Use `Lazy<T>` (preferred) or add `volatile` to the field. `Lazy<T>` handles all the complexity correctly.

---

#### CONC-LOCK-09: Lock held across I/O or async operation

**Severity**: High (contention, potential deadlock)
**Why**: Holding a lock while performing I/O means all other threads waiting for that lock are blocked for the entire I/O duration (potentially hundreds of milliseconds). This kills throughput and can trigger cascading timeouts.

```csharp
// BAD: lock held across network I/O
lock (_syncRoot)
{
    var data = httpClient.GetStringAsync(url).Result; // Blocks thread + holds lock
    _cache[url] = data;
}

// GOOD: fetch outside lock, update under lock
var data = await httpClient.GetStringAsync(url).ConfigureAwait(false);
lock (_syncRoot)
{
    _cache[url] = data; // Lock held only for fast in-memory update
}
```

**Fix**: Restructure to perform I/O outside the lock. Acquire the lock only for the fast in-memory state mutation.

---

#### CONC-LOCK-10: `Interlocked.CompareExchange` loop without volatile read

**Severity**: Medium
**Why**: In a CAS loop, if the initial read of the shared variable is not volatile, the compiler/CPU may cache a stale value, causing the loop to spin on outdated data or miss updates.

```csharp
// BAD: non-volatile read may be stale
int current = _counter; // May be cached
while (true)
{
    int updated = current + 1;
    int original = Interlocked.CompareExchange(ref _counter, updated, current);
    if (original == current) break;
    current = original; // This is fine (Interlocked gives fresh value)
}

// GOOD: volatile read for initial value
int current = Volatile.Read(ref _counter);
while (true)
{
    int updated = current + 1;
    int original = Interlocked.CompareExchange(ref _counter, updated, current);
    if (original == current) break;
    current = original;
}
```

**Fix**: Use `Volatile.Read` for the initial read. Subsequent reads from `Interlocked.CompareExchange` return values are already fresh.

---

### Thread Affinity & UI Threading (8 patterns)

---

#### CONC-UI-01: Accessing UI element from background thread

**Severity**: Critical (InvalidOperationException or silent corruption)
**Why**: UI frameworks (WPF, WinUI, WinForms) require all UI element access from the UI thread. Accessing from a background thread throws `InvalidOperationException` (WPF) or causes silent corruption (WinUI/WinForms in some cases).

```csharp
// BAD: UI access from background thread
await Task.Run(() =>
{
    var data = LoadData();
    textBox.Text = data.Summary; // CRASH: cross-thread UI access
});

// GOOD: marshal back to UI thread
var data = await Task.Run(() => LoadData());
textBox.Text = data.Summary; // Back on UI thread after await
```

**Fix**: Perform computation on background thread, then update UI after `await` returns to the UI context. Or use `Dispatcher`/`DispatcherQueue` to marshal explicitly.

---

#### CONC-UI-02: Missing `DispatcherQueue.TryEnqueue` / `Dispatcher.Invoke` for cross-thread UI update

**Severity**: Critical
**Why**: When a background callback (timer, event, completion) needs to update UI, it must marshal to the UI thread. Without marshaling, the update either crashes or corrupts visual state.

```csharp
// BAD: event handler fires on background thread, updates UI directly
void OnDataReceived(object sender, DataEventArgs e)
{
    statusLabel.Text = e.Message; // May be on wrong thread!
}

// GOOD (WinUI 3): marshal via DispatcherQueue
void OnDataReceived(object sender, DataEventArgs e)
{
    DispatcherQueue.TryEnqueue(() =>
    {
        statusLabel.Text = e.Message;
    });
}

// GOOD (WPF): marshal via Dispatcher
void OnDataReceived(object sender, DataEventArgs e)
{
    Dispatcher.Invoke(() =>
    {
        statusLabel.Text = e.Message;
    });
}
```

**Fix**: Use `DispatcherQueue.TryEnqueue` (WinUI 3) or `Dispatcher.Invoke`/`BeginInvoke` (WPF) to marshal UI updates.

---

#### CONC-UI-03: Blocking UI thread with synchronous I/O

**Severity**: High (UI hang/freeze)
**Why**: Any synchronous I/O (file, network, registry, database) on the UI thread blocks the message pump. The UI freezes for the duration of the I/O, causing "Not Responding" in Windows.

```csharp
// BAD: synchronous file read on UI thread
void LoadButton_Click(object sender, RoutedEventArgs e)
{
    var content = File.ReadAllText(path); // UI frozen during read
    textBox.Text = content;
}

// GOOD: async I/O
async void LoadButton_Click(object sender, RoutedEventArgs e)
{
    var content = await File.ReadAllTextAsync(path); // UI stays responsive
    textBox.Text = content; // Back on UI thread
}
```

**Fix**: Replace synchronous I/O methods with their `Async` counterparts and `await` them.

---

#### CONC-UI-04: Long-running computation on UI thread without `Task.Run`

**Severity**: High
**Why**: CPU-bound work over ~16ms blocks the UI thread, dropping frames (60fps = 16.67ms/frame). Over ~100ms, the UI feels sluggish. Over ~5s, Windows shows "Not Responding".

```csharp
// BAD: heavy computation on UI thread
void AnalyzeButton_Click(object sender, RoutedEventArgs e)
{
    var result = AnalyzeLargeDataset(data); // Blocks UI for seconds
    resultsGrid.ItemsSource = result;
}

// GOOD: offload to thread pool
async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
{
    var result = await Task.Run(() => AnalyzeLargeDataset(data));
    resultsGrid.ItemsSource = result; // Back on UI thread
}
```

**Fix**: Wrap CPU-bound work in `Task.Run` and `await` it.

---

#### CONC-UI-05: `SynchronizationContext.Post` vs `.Send` misuse

**Severity**: Medium
**Why**: `Send` is synchronous (blocks the calling thread until the delegate executes on the target thread). `Post` is asynchronous (queues and returns immediately). Using `Send` from a background thread to the UI thread can deadlock if the UI thread is waiting for that background thread.

```csharp
// BAD: Send can deadlock if UI thread is waiting for this thread
SynchronizationContext.Current!.Send(_ =>
{
    label.Text = "Updated"; // Blocks until UI thread processes this
}, null);

// GOOD: Post is non-blocking
SynchronizationContext.Current!.Post(_ =>
{
    label.Text = "Updated"; // Queued, returns immediately
}, null);
```

**Fix**: Prefer `Post` over `Send` unless you specifically need the synchronous guarantee and have verified no deadlock risk.

---

#### CONC-UI-06: Timer callback not marshaled to UI thread

**Severity**: High
**Why**: `System.Threading.Timer` and `System.Timers.Timer` fire callbacks on thread pool threads. Updating UI from these callbacks without marshaling causes crashes or corruption.

```csharp
// BAD: System.Threading.Timer fires on thread pool
var timer = new System.Threading.Timer(_ =>
{
    progressBar.Value = GetProgress(); // CRASH: wrong thread
}, null, 0, 1000);

// GOOD: Use DispatcherTimer (WPF) or DispatcherQueueTimer (WinUI)
var timer = DispatcherQueue.CreateTimer();
timer.Interval = TimeSpan.FromSeconds(1);
timer.Tick += (s, e) =>
{
    progressBar.Value = GetProgress(); // Already on UI thread
};
timer.Start();
```

**Fix**: Use `DispatcherTimer` (WPF), `DispatcherQueueTimer` (WinUI), or marshal from `System.Threading.Timer` via `DispatcherQueue.TryEnqueue`.

---

#### CONC-UI-07: Property change notification from background thread in MVVM

**Severity**: High
**Why**: `INotifyPropertyChanged.PropertyChanged` events bound to UI elements must fire on the UI thread. WPF silently marshals for simple bindings but NOT for collection changes. WinUI does not marshal at all.

```csharp
// BAD: PropertyChanged fired from background thread
public async Task RefreshAsync()
{
    var items = await Task.Run(() => LoadItems());
    Items = items; // Setter fires PropertyChanged on background thread!
}

// GOOD: ensure UI thread for property update
public async Task RefreshAsync()
{
    var items = await Task.Run(() => LoadItems());
    // After await, we're back on UI thread (if called from UI context)
    Items = items; // PropertyChanged fires on UI thread
}

// GOOD: explicit marshal if context is unknown
public void UpdateFromBackground(List<Item> items)
{
    _dispatcherQueue.TryEnqueue(() =>
    {
        Items = items;
    });
}
```

**Fix**: Ensure property setters that raise `PropertyChanged` execute on the UI thread. Structure async methods so the `await` resumes on the UI context, or explicitly marshal.

---

#### CONC-UI-08: `DispatcherTimer` with heavy callback blocking message pump

**Severity**: Medium
**Why**: `DispatcherTimer` runs its `Tick` handler on the UI thread. If the handler performs heavy work, it blocks the message pump for that duration, causing jank.

```csharp
// BAD: heavy work in DispatcherTimer callback
timer.Tick += (s, e) =>
{
    var report = GenerateComplexReport(); // Blocks UI thread
    reportView.Content = report;
};

// GOOD: offload heavy work, update UI with result
timer.Tick += async (s, e) =>
{
    var report = await Task.Run(() => GenerateComplexReport());
    reportView.Content = report;
};
```

**Fix**: Keep `DispatcherTimer` callbacks lightweight. Offload heavy work to `Task.Run` and update UI after `await`.

---

### Shared State & Data Races (8 patterns)

---

#### CONC-RACE-01: Static mutable field without synchronization

**Severity**: High
**Why**: Static mutable fields are shared across all threads in the process. Without synchronization, concurrent reads and writes cause data races (torn reads, lost updates, corrupted state).

```csharp
// BAD: static mutable field, no synchronization
private static int _requestCount;
public void HandleRequest()
{
    _requestCount++; // NOT atomic: read-modify-write race
}

// GOOD: use Interlocked for simple counters
private static int _requestCount;
public void HandleRequest()
{
    Interlocked.Increment(ref _requestCount);
}

// GOOD: use lock for complex state
private static readonly object _lock = new();
private static Dictionary<string, int> _stats = new();
public void RecordStat(string key)
{
    lock (_lock) { _stats[key] = _stats.GetValueOrDefault(key) + 1; }
}
```

**Fix**: Use `Interlocked` for simple numeric operations, `lock` for complex state, or `ConcurrentDictionary`/`ConcurrentBag` for concurrent collections.

---

#### CONC-RACE-02: `Dictionary<K,V>` accessed from multiple threads

**Severity**: High
**Why**: `Dictionary<K,V>` is not thread-safe. Concurrent writes corrupt internal state (infinite loops in bucket chains have been observed). Concurrent read + write can return wrong values or throw.

```csharp
// BAD: shared Dictionary without synchronization
private static readonly Dictionary<string, UserData> _cache = new();

void CacheUser(UserData user)
{
    _cache[user.Id] = user; // Race: concurrent writes corrupt internal arrays
}

UserData? GetUser(string id)
{
    return _cache.TryGetValue(id, out var u) ? u : null; // Race with writes
}

// GOOD: ConcurrentDictionary
private static readonly ConcurrentDictionary<string, UserData> _cache = new();

void CacheUser(UserData user)
{
    _cache[user.Id] = user;
}

UserData? GetUser(string id)
{
    return _cache.TryGetValue(id, out var u) ? u : null;
}
```

**Fix**: Replace `Dictionary<K,V>` with `ConcurrentDictionary<K,V>` or protect all accesses with a lock.

---

#### CONC-RACE-03: `List<T>` modified during enumeration from another thread

**Severity**: High
**Why**: `List<T>` is not thread-safe. Modifying it while another thread enumerates throws `InvalidOperationException` or, worse, returns corrupt data without throwing.

```csharp
// BAD: List modified while enumerated
private readonly List<Connection> _connections = new();

void BroadcastMessage(string msg)
{
    foreach (var conn in _connections) // Thread A enumerates
        conn.Send(msg);
}
void AddConnection(Connection c)
{
    _connections.Add(c); // Thread B modifies -- CRASH or corruption
}

// GOOD: snapshot under lock
private readonly List<Connection> _connections = new();
private readonly object _lock = new();

void BroadcastMessage(string msg)
{
    Connection[] snapshot;
    lock (_lock) { snapshot = _connections.ToArray(); }
    foreach (var conn in snapshot)
        conn.Send(msg);
}
void AddConnection(Connection c)
{
    lock (_lock) { _connections.Add(c); }
}
```

**Fix**: Protect with a lock and snapshot before enumeration, or use `ImmutableList<T>` / `ConcurrentBag<T>`.

---

#### CONC-RACE-04: TOCTOU: check-then-act on shared state without atomic operation

**Severity**: High
**Why**: The gap between checking a condition and acting on it allows another thread to invalidate the condition. Classic example: "if key exists, use it" without atomicity.

```csharp
// BAD: TOCTOU on ConcurrentDictionary
if (_cache.ContainsKey(key))        // Thread A checks
{
    var value = _cache[key];         // Thread B removes key -- KeyNotFoundException!
    Process(value);
}

// GOOD: atomic TryGetValue
if (_cache.TryGetValue(key, out var value))
{
    Process(value);
}

// BAD: TOCTOU on file existence
if (File.Exists(path))
{
    var content = File.ReadAllText(path); // File may be deleted between check and read
}

// GOOD: try/catch instead of check-then-act
try
{
    var content = File.ReadAllText(path);
}
catch (FileNotFoundException) { /* handle */ }
```

**Fix**: Use atomic operations (`TryGetValue`, `GetOrAdd`, `TryRemove`) instead of check-then-act. For I/O, use try/catch instead of existence checks.

---

#### CONC-RACE-05: `Lazy<T>` with `LazyThreadSafetyMode.None` in concurrent context

**Severity**: High
**Why**: `LazyThreadSafetyMode.None` means the factory can be called from multiple threads concurrently, potentially creating multiple instances and causing race conditions.

```csharp
// BAD: Lazy with no thread safety, used from multiple threads
private static readonly Lazy<ExpensiveResource> _resource =
    new(() => new ExpensiveResource(), LazyThreadSafetyMode.None);

// Multiple threads call _resource.Value simultaneously:
// Factory runs multiple times, object may be partially constructed

// GOOD: use default (ExecutionAndPublication) for thread safety
private static readonly Lazy<ExpensiveResource> _resource =
    new(() => new ExpensiveResource()); // Default is ExecutionAndPublication
```

**Fix**: Use the default `LazyThreadSafetyMode.ExecutionAndPublication` (or omit the parameter). Only use `None` if you can guarantee single-threaded access.

---

#### CONC-RACE-06: `bool` flag used for cross-thread signaling without `volatile`

**Severity**: Medium
**Why**: Without `volatile`, the JIT compiler may cache the field in a register. A loop checking `_shouldStop` may never see the update from another thread (especially in Release builds).

```csharp
// BAD: non-volatile bool, JIT may optimize away the read
private bool _shouldStop;

void WorkerThread()
{
    while (!_shouldStop) // May be hoisted out of loop in Release mode
    {
        DoWork();
    }
}
void Stop() => _shouldStop = true;

// GOOD: volatile ensures visibility
private volatile bool _shouldStop;

void WorkerThread()
{
    while (!_shouldStop)
    {
        DoWork();
    }
}
void Stop() => _shouldStop = true;

// ALSO GOOD: use CancellationToken (preferred .NET pattern)
void WorkerThread(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        DoWork();
    }
}
```

**Fix**: Use `volatile`, `Volatile.Read`/`Volatile.Write`, or (preferred) `CancellationToken`.

---

#### CONC-RACE-07: Event raise pattern without local copy

**Severity**: Medium
**Why**: Between checking an event for null and invoking it, another thread can unsubscribe, setting the delegate to null. The null-conditional operator (`?.Invoke`) is safe; the old `if (handler != null) handler(...)` pattern is not.

```csharp
// BAD: race between null check and invocation
public event EventHandler DataReady;
protected void OnDataReady()
{
    if (DataReady != null)
        DataReady(this, EventArgs.Empty); // Thread B sets DataReady = null here --> NRE
}

// GOOD: null-conditional operator (C# 6+)
protected void OnDataReady()
{
    DataReady?.Invoke(this, EventArgs.Empty); // Atomic read + invoke
}

// ALSO GOOD: local copy pattern (pre-C# 6)
protected void OnDataReady()
{
    var handler = DataReady; // Local copy -- won't become null
    if (handler != null)
        handler(this, EventArgs.Empty);
}
```

**Fix**: Use `EventName?.Invoke(...)` or capture to a local variable before null-checking.

---

#### CONC-RACE-08: `ConcurrentDictionary.GetOrAdd` with factory that has side effects

**Severity**: Medium
**Why**: `GetOrAdd`'s factory delegate may be called by multiple threads simultaneously for the same key. Only one result is stored, but the factory side effects execute multiple times.

```csharp
// BAD: factory has side effects (file creation, counter increment)
var conn = _connectionPool.GetOrAdd(host, key =>
{
    connectionCount++; // Race: may increment multiple times
    return new TcpClient(key, 443); // May create multiple connections, only one kept
});

// GOOD: use Lazy<T> to ensure single execution
var lazyConn = _connectionPool.GetOrAdd(host,
    key => new Lazy<TcpClient>(() =>
    {
        connectionCount++;
        return new TcpClient(key, 443);
    }));
var conn = lazyConn.Value; // Factory runs exactly once
```

**Fix**: Wrap the value in `Lazy<T>` so the factory executes exactly once, or use `GetOrAdd` overload with `factoryArgument` and ensure the factory is side-effect-free.

---

## Fix Strategy Decision Tree

```
What kind of concurrency bug?
├── async/await Misuse
│   ├── async void? → Change to async Task (CONC-ASYNC-01, 08)
│   ├── Sync-over-async? → Make caller async ("async all the way") (CONC-ASYNC-02)
│   ├── Missing ConfigureAwait? → Add .ConfigureAwait(false) in library code (CONC-ASYNC-03)
│   ├── ValueTask reuse? → Consume once, or .AsTask() if needed twice (CONC-ASYNC-09)
│   └── Fire-and-forget? → Add error handler or await (CONC-ASYNC-05)
├── Locking Issue
│   ├── Wrong lock target? → Use private readonly object (CONC-LOCK-01, 02, 03)
│   ├── Lock ordering violation? → Establish consistent order or reduce to one lock (CONC-LOCK-04)
│   ├── await inside lock? → Switch to SemaphoreSlim(1,1) (CONC-LOCK-05)
│   ├── Missing try/finally? → Add try/finally around Release/Exit (CONC-LOCK-06, 07)
│   └── Lock held across I/O? → Move I/O outside lock (CONC-LOCK-09)
├── UI Thread Violation
│   ├── UI access from background? → await Task.Run then update (CONC-UI-01)
│   ├── Need explicit marshal? → DispatcherQueue.TryEnqueue / Dispatcher.Invoke (CONC-UI-02)
│   ├── Sync I/O on UI thread? → Use async I/O methods (CONC-UI-03)
│   └── Timer on wrong thread? → Use DispatcherTimer/DispatcherQueueTimer (CONC-UI-06)
└── Data Race
    ├── Simple counter? → Interlocked.Increment (CONC-RACE-01)
    ├── Shared Dictionary? → ConcurrentDictionary (CONC-RACE-02)
    ├── Shared List? → lock + snapshot, or ImmutableList (CONC-RACE-03)
    ├── Check-then-act? → Use atomic API (TryGetValue, GetOrAdd) (CONC-RACE-04)
    └── Bool flag? → volatile or CancellationToken (CONC-RACE-06)
```

## Verification Checklist

For each fix:
- [ ] No `async void` except UI event handlers
- [ ] No `.Result` / `.Wait()` in async context
- [ ] `ConfigureAwait(false)` in all library awaits
- [ ] Lock targets are `private readonly object`
- [ ] All lock acquisitions follow documented ordering
- [ ] All `SemaphoreSlim`/`ReaderWriterLockSlim` usage has `try/finally`
- [ ] No UI access from background threads
- [ ] All shared mutable state has synchronization
- [ ] `ConcurrentDictionary` operations are atomic (no check-then-act)
- [ ] Build succeeds; no new warnings
- [ ] Concurrency stress tests pass (if available)

## References

1. .NET Blog -- "ConfigureAwait FAQ" (https://devblogs.microsoft.com/dotnet/configureawait-faq/)
2. MSDN Magazine -- "Async/Await Best Practices" (https://docs.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)
3. .NET Runtime team -- Threading and async documentation (https://learn.microsoft.com/en-us/dotnet/standard/threading/)
4. .NET Blog -- "Understanding the Whys, Whats, and Whens of ValueTask" (https://devblogs.microsoft.com/dotnet/understanding-the-whys-whats-and-whens-of-valuetask/)
5. .NET Framework Design Guidelines -- Lock usage patterns
