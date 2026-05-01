---
name: cs-performance-review
description: >-
  Review C# code for performance and efficiency defects: allocation and GC
  pressure (string concatenation in loops, unnecessary LINQ materialization,
  boxing, closure/delegate allocation, async state machine overhead), LINQ
  and collection inefficiency (multiple enumeration, wrong collection type,
  ContainsKey+indexer instead of TryGetValue, missing Span for slicing),
  UI and rendering performance (synchronous I/O on UI thread, disabled
  virtualization, excessive layout invalidation, image decoding on UI thread),
  and Span/zero-copy missed opportunities (Substring vs ReadOnlySpan,
  stackalloc without fallback, ArrayPool not used).
  35 patterns with cost suppression rules, performance impact estimation,
  and fix strategies. Sourced from .NET Runtime team guidance and the
  BenchmarkDotNet community.
  Use this skill when reviewing C# code on hot paths, per-request handlers,
  UI render loops, startup sequences, or anywhere latency and allocation matter.
---

# C# Performance & Efficiency Code Review

## Quick Detection

**Primary Symptoms (in code under review)**:
- String concatenation (`+` or `+=`) inside loops
- `.ToList()` / `.ToArray()` when only iterating the result
- LINQ method chains in tight loops (allocates enumerator per iteration)
- `Dictionary.ContainsKey()` followed by `dictionary[key]` (double lookup)
- `string.Substring()` where `ReadOnlySpan<char>` or `AsSpan()` would suffice
- Synchronous I/O on UI thread (`File.ReadAllText`, `HttpClient.Send`)
- `params` array parameter called in hot path
- Boxing: value type cast to `object` or interface

**Key Search Queries**:
```csharp
// String concat in loop
for (...) { result += item.ToString(); }

// LINQ materialization waste
items.Where(x => x.Active).ToList().ForEach(...)

// Double lookup
if (dict.ContainsKey(key)) { var v = dict[key]; }

// Boxing
object boxed = myStruct;
IComparable c = myInt;
```

## Cost Suppression Rules

Not all performance patterns deserve a finding. Suppress the pattern if ANY of these conditions apply:

| Rule | Condition | Rationale |
|------|-----------|-----------|
| **1. I/O-Dominated Scope** | File, network, or database operation in the same scope | I/O latency (ms) dwarfs allocation cost (ns). Noise finding. |
| **2. Cold Path / Startup** | Initialization, configuration load, app startup, shutdown | Runs once. Micro-optimizing startup paths rarely matters. |
| **3. Trivially Small Collections** | Collection has 16 or fewer elements | LINQ overhead on tiny collections is sub-microsecond. Not worth fixing. |
| **4. Test and Mock Code** | File is in a test project (`*Tests*`, `*Test.cs`, `*Mock*`) | Test performance is irrelevant unless it is a benchmark test. |
| **5. Frequency Gate** | Code runs less than ~100 times/second | Flag only if per-request (web), per-frame (UI at 60fps), or tight loop. |

**Apply suppression before reporting**: If a pattern match is suppressed, do NOT include it in findings. Mention suppressions only if the reviewer asks why a pattern was not flagged.

## Analysis Workflow

### Step 1: Identify Hot Paths

Focus review effort where it matters:

1. **Per-request paths**: ASP.NET controller actions, middleware, gRPC handlers
2. **Per-frame paths**: UI render callbacks, `DispatcherTimer` ticks, animation handlers
3. **Tight loops**: `for`/`foreach` over large collections (>100 elements)
4. **Frequently-called methods**: Called from event handlers, timers, or message pumps
5. **Startup/init**: `Main`, `ConfigureServices`, `OnLaunched` (only for cold-start perf)

### Step 2: Scan for Pattern Matches

Apply the 35 performance patterns below, in priority order by user impact.

**Search priority ranking**:

| Priority | Category | Pattern Count | Impact |
|----------|----------|---------------|--------|
| 1 | UI & Rendering Performance | 8 | Hangs, jank, dropped frames |
| 2 | Allocation & GC Pressure | 12 | Latency spikes from GC, memory pressure |
| 3 | LINQ & Collection Efficiency | 10 | CPU waste, hidden O(n) lookups |
| 4 | Span & Zero-Copy | 5 | Allocation reduction on hot paths |

### Step 3: Estimate Impact

**Performance Impact Estimation Table**:

| Pattern Category | Typical Cost Per Occurrence | When It Matters |
|-----------------|---------------------------|-----------------|
| String concat in loop (N items) | O(N^2) copies, ~N*avgLen bytes allocated | N > 10 |
| LINQ `.ToList()` waste | Array allocation + copy, 24B + N*sizeof(T) | Per-request or per-frame |
| Boxing | 12-24 bytes heap alloc + GC pressure per box | Tight loop (>1000/sec) |
| Lambda closure | ~64 bytes (delegate + closure object) per call | Hot path (>10K/sec) |
| `params` array | 32 + N*sizeof(T) bytes per call | Hot path (>10K/sec) |
| Double dictionary lookup | ~2x hash computation + bucket traversal | Large dictionaries in loops |
| Sync I/O on UI thread | 1ms - 10s+ hang (depends on I/O) | Always critical |
| Missing virtualization | O(N) UI elements instantiated | N > 50 items |
| Substring allocation | 40 + 2*len bytes per substring | String parsing in loops |
| async state machine | ~100-200 bytes if not elided | Frequently-synchronous paths |

**Severity classification**:
- **Critical**: >100ms hang on UI thread, OOM-risk allocation pattern
- **High**: Measurable GC pauses, dropped frames, per-request allocation waste
- **Medium**: Measurable in BenchmarkDotNet but unlikely user-visible alone
- **Low**: Micro-optimization, only relevant in extreme hot paths

---

## Pattern Catalog

### Allocation & GC Pressure (12 patterns)

---

#### PERF-ALLOC-01: String concatenation in loop

**Severity**: Medium
**Impact**: O(N^2) total copies; N iterations with average string length L allocates ~N*L total bytes, with quadratic copy behavior.

```csharp
// BAD: O(N^2) -- each += allocates a new string and copies all previous content
string result = "";
foreach (var item in items)
{
    result += item.ToString() + ", ";  // New string allocated every iteration
}

// GOOD: StringBuilder -- O(N) amortized
var sb = new StringBuilder();
foreach (var item in items)
{
    sb.Append(item.ToString());
    sb.Append(", ");
}
string result = sb.ToString();

// GOOD: string.Join for simple cases
string result = string.Join(", ", items);
```

**Fix**: Use `StringBuilder` for loops, `string.Join` for simple concatenation of collections.

---

#### PERF-ALLOC-02: `params` array allocation on every call in hot path

**Severity**: Medium
**Impact**: Allocates a new array (32 + N*sizeof(T) bytes) on every call.

```csharp
// BAD: params allocates a new array every call
void Log(string format, params object[] args)
{
    _logger.Write(string.Format(format, args));
}

// Called 10,000 times/sec:
Log("User {0} did {1}", userId, action); // new object[2] every call + boxing

// GOOD: overloads for common arities
void Log(string format, object arg0) { ... }
void Log(string format, object arg0, object arg1) { ... }
void Log(string format, object arg0, object arg1, object arg2) { ... }
void Log(string format, params object[] args) { ... } // fallback

// GOOD (.NET 6+): use CallerArgumentExpression or source generators
// GOOD (.NET 8+): use params Span<object> (avoids heap allocation)
```

**Fix**: Provide overloads for 1-3 argument arities. Or use `Span`-based params on .NET 8+.

---

#### PERF-ALLOC-03: LINQ `.ToList()`/`.ToArray()` when only iterating

**Severity**: Medium
**Impact**: Allocates array + `List<T>` wrapper unnecessarily. For N items, wastes ~32 + N*sizeof(T) bytes.

```csharp
// BAD: materializes to list just to iterate
var activeUsers = users.Where(u => u.IsActive).ToList();
foreach (var user in activeUsers)
{
    SendEmail(user);
}

// GOOD: iterate the IEnumerable directly
foreach (var user in users.Where(u => u.IsActive))
{
    SendEmail(user);
}
```

**Fix**: Remove `.ToList()`/`.ToArray()` if the result is only iterated once. Keep it if you need: (a) multiple enumeration, (b) count before iterating, (c) index access, or (d) the source could change during enumeration.

---

#### PERF-ALLOC-04: Boxing value type

**Severity**: Medium
**Impact**: Each box operation allocates 12-24 bytes on the heap + GC tracking overhead.

```csharp
// BAD: boxing int to object
object boxed = 42; // Box allocation

// BAD: boxing via interface
IComparable comparable = 42; // Box allocation

// BAD: boxing in string interpolation (pre-.NET 6)
int count = 42;
string msg = $"Count: {count}"; // Boxes count (fixed in .NET 6 with handler)

// GOOD: use generic constraints to avoid boxing
void Process<T>(T value) where T : IComparable<T>
{
    // No boxing -- T is constrained, not cast to interface
}

// GOOD: use .ToString() explicitly in older TFMs
string msg = $"Count: {count.ToString()}"; // No boxing
```

**Fix**: Use generic constraints instead of interface casts. In older TFMs, call `.ToString()` explicitly in interpolations.

---

#### PERF-ALLOC-05: Lambda/closure allocating delegate + closure object per call

**Severity**: High (in hot path)
**Impact**: Each closure allocates a delegate (~64B) + a closure class instance (~32B+). In a per-request or per-frame path, this adds significant GC pressure.

```csharp
// BAD: closure captures 'threshold' -- new delegate + closure every call
void ProcessItems(List<Item> items, int threshold)
{
    var filtered = items.Where(x => x.Value > threshold); // closure over threshold
}

// GOOD: use static lambda if no captures needed (.NET 5+)
var filtered = items.Where(static x => x.Value > 0);

// GOOD: hoist delegate to a field if captures are constant
private static readonly Func<Item, bool> _isActive = static x => x.IsActive;
var filtered = items.Where(_isActive);

// GOOD: use a local function to avoid closure allocation
bool Filter(Item x) => x.Value > threshold; // compiler may optimize
var filtered = items.Where(Filter);
```

**Fix**: Use `static` lambdas when no captures are needed. Hoist constant delegates to fields. Consider local functions which the compiler can sometimes optimize to avoid closure allocation.

---

#### PERF-ALLOC-06: `string.Format` / interpolation in hot path

**Severity**: Medium
**Impact**: Each call allocates a new string. In logging, this happens even when the log level is disabled.

```csharp
// BAD: string allocated even if debug logging is off
logger.Debug($"Processing item {item.Id} with value {item.Value}");

// GOOD: structured logging (deferred formatting)
logger.LogDebug("Processing item {ItemId} with value {Value}", item.Id, item.Value);

// GOOD: guard check
if (logger.IsEnabled(LogLevel.Debug))
    logger.LogDebug($"Processing item {item.Id} with value {item.Value}");

// GOOD (.NET 6+): LoggerMessage source generator (zero alloc)
[LoggerMessage(Level = LogLevel.Debug, Message = "Processing item {ItemId} with value {Value}")]
partial void LogProcessing(int itemId, decimal value);
```

**Fix**: Use structured logging with placeholders (deferred formatting), level guards, or `LoggerMessage` source generators.

---

#### PERF-ALLOC-07: `async` state machine allocation

**Severity**: Medium
**Impact**: Each async method call that does not complete synchronously allocates ~100-200 bytes for the state machine + `Task<T>` object. For methods that frequently complete synchronously, this is waste.

```csharp
// BAD: always allocates Task<T> even when cache hit (synchronous completion)
public async Task<Data> GetDataAsync(string key)
{
    if (_cache.TryGetValue(key, out var cached))
        return cached; // Still allocates Task<Data> wrapper
    var data = await LoadFromDbAsync(key);
    _cache[key] = data;
    return data;
}

// GOOD: use ValueTask to avoid allocation on synchronous path
public ValueTask<Data> GetDataAsync(string key)
{
    if (_cache.TryGetValue(key, out var cached))
        return new ValueTask<Data>(cached); // No allocation
    return new ValueTask<Data>(LoadAndCacheAsync(key));
}

private async Task<Data> LoadAndCacheAsync(string key)
{
    var data = await LoadFromDbAsync(key);
    _cache[key] = data;
    return data;
}
```

**Fix**: Use `ValueTask<T>` for methods that frequently complete synchronously (cache hits, buffered reads). Keep `Task<T>` for methods that are almost always truly async.

---

#### PERF-ALLOC-08: `yield return` in hot path creating iterator state machine

**Severity**: Low
**Impact**: Each call to an iterator method allocates a state machine object (~80-120 bytes). Only matters in very hot paths (>100K calls/sec).

```csharp
// POTENTIALLY SLOW: iterator state machine allocated per call
IEnumerable<int> GetEvenNumbers(int[] source)
{
    foreach (var n in source)
        if (n % 2 == 0)
            yield return n;
}

// FASTER in hot path: return concrete collection
int[] GetEvenNumbers(int[] source)
{
    // Or use ArrayPool for very hot paths
    return Array.FindAll(source, n => n % 2 == 0);
}
```

**Fix**: In extremely hot paths, replace iterators with concrete collections. In most code, `yield return` is fine (suppressed by frequency gate).

---

#### PERF-ALLOC-09: Large struct copied on every method call

**Severity**: Medium
**Impact**: Large structs (>16 bytes) are copied on every pass-by-value. A 64-byte struct copied 10,000 times = 640KB of stack copying.

```csharp
// BAD: 64-byte struct copied on every call
public struct LargeState
{
    public Matrix4x4 Transform;  // 64 bytes
    public Vector4 Color;        // 16 bytes
}

void Process(LargeState state) // Copied on call
{
    Render(state);               // Copied again
}

// GOOD: pass by 'in' reference (readonly ref)
void Process(in LargeState state) // No copy, passed by reference
{
    Render(in state);
}

// GOOD: use 'ref' if mutation is needed
void Update(ref LargeState state)
{
    state.Transform = Matrix4x4.Identity;
}
```

**Fix**: Use `in` parameter for read-only large structs, `ref` when mutation is needed. Threshold: consider `in`/`ref` for structs larger than 16 bytes on hot paths.

---

#### PERF-ALLOC-10: `Tuple<T1,T2>` (class) instead of `ValueTuple`

**Severity**: Low
**Impact**: `Tuple<T1,T2>` is a class (heap allocation). `ValueTuple` / `(T1, T2)` is a struct (stack allocation).

```csharp
// BAD: Tuple<> allocates on heap
Tuple<string, int> GetResult()
{
    return Tuple.Create("hello", 42); // Heap allocation
}

// GOOD: ValueTuple is a struct
(string Name, int Value) GetResult()
{
    return ("hello", 42); // Stack allocation
}
```

**Fix**: Use C# tuple syntax `(T1, T2)` instead of `Tuple<T1, T2>`.

---

#### PERF-ALLOC-11: `Enum.ToString()` / `Enum.HasFlag()` boxing in older TFMs

**Severity**: Medium
**Impact**: In .NET Framework and older .NET Core, `Enum.ToString()` boxes the enum value. `Enum.HasFlag()` boxes both operands. Fixed in .NET 7+ (JIT optimization).

```csharp
// BAD (pre-.NET 7): boxing on every call
MyEnum value = MyEnum.Active;
string name = value.ToString();     // Boxes on .NET Framework
bool has = value.HasFlag(MyEnum.A); // Boxes both operands on .NET Framework

// GOOD: nameof for known values
string name = nameof(MyEnum.Active);

// GOOD: bitwise check instead of HasFlag
bool has = (value & MyEnum.A) == MyEnum.A; // No boxing

// OK on .NET 7+: JIT eliminates boxing for both patterns
```

**Fix**: Use bitwise operators for `HasFlag` checks. Use `nameof` for known enum values. On .NET 7+, this is optimized by the JIT.

---

#### PERF-ALLOC-12: `ArrayPool` not used for temporary large arrays

**Severity**: Medium
**Impact**: Temporary arrays >1KB that are allocated and discarded frequently cause GC pressure. Arrays >85KB go to the Large Object Heap (LOH), which is only collected in Gen2 GC.

```csharp
// BAD: large temporary array, GC pressure
byte[] buffer = new byte[8192];
int read = stream.Read(buffer);
Process(buffer.AsSpan(0, read));
// buffer becomes garbage

// GOOD: rent from ArrayPool
byte[] buffer = ArrayPool<byte>.Shared.Rent(8192);
try
{
    int read = stream.Read(buffer);
    Process(buffer.AsSpan(0, read));
}
finally
{
    ArrayPool<byte>.Shared.Return(buffer);
}
```

**Fix**: Use `ArrayPool<T>.Shared.Rent/Return` for temporary arrays, especially in per-request or per-frame code. Always `Return` in a `finally` block.

---

### LINQ & Collection Efficiency (10 patterns)

---

#### PERF-LINQ-01: Multiple LINQ enumerations of same source

**Severity**: High
**Impact**: LINQ is lazy (deferred execution). Each `foreach`, `.Count()`, `.Any()`, etc. re-executes the entire query chain from scratch. If the source is a database query or expensive computation, this multiplies cost.

```csharp
// BAD: filters re-evaluated twice
var activeUsers = users.Where(u => u.IsActive);
int count = activeUsers.Count();          // First enumeration
foreach (var u in activeUsers) { ... }    // Second enumeration (re-filters)

// GOOD: materialize once
var activeUsers = users.Where(u => u.IsActive).ToList();
int count = activeUsers.Count;            // O(1) on List<T>
foreach (var u in activeUsers) { ... }    // Iterates materialized list
```

**Fix**: Materialize with `.ToList()` or `.ToArray()` if the result will be consumed more than once.

---

#### PERF-LINQ-02: LINQ `.Count()` when `.Any()` suffices

**Severity**: Medium
**Impact**: `.Count()` enumerates the entire sequence. `.Any()` short-circuits after the first element. For large or expensive sequences, this is a significant difference.

```csharp
// BAD: enumerates entire sequence to check if non-empty
if (users.Where(u => u.IsActive).Count() > 0)
{
    // ...
}

// GOOD: short-circuits after first match
if (users.Any(u => u.IsActive))
{
    // ...
}

// BAD: Count() == 0 to check emptiness
if (results.Count() == 0) { ShowNoResults(); }

// GOOD:
if (!results.Any()) { ShowNoResults(); }
```

**Fix**: Replace `.Count() > 0` with `.Any()` and `.Count() == 0` with `!.Any()`.

---

#### PERF-LINQ-03: `.Where().First()` instead of `.First(predicate)`

**Severity**: Low
**Impact**: `.Where().First()` allocates an intermediate `WhereEnumerator`. `.First(predicate)` is a single scan with no intermediate allocation. Difference is ~64 bytes per call.

```csharp
// BAD: unnecessary intermediate iterator
var user = users.Where(u => u.Id == targetId).First();

// GOOD: direct predicate
var user = users.First(u => u.Id == targetId);
```

**Fix**: Use `.First(predicate)`, `.FirstOrDefault(predicate)`, `.Single(predicate)`.

---

#### PERF-LINQ-04: LINQ in tight loop

**Severity**: High
**Impact**: Each LINQ call allocates an enumerator object. Inside a tight loop of N iterations, that is N allocations of ~32-64 bytes each, plus the GC pressure.

```csharp
// BAD: LINQ inside tight loop -- N enumerator allocations
for (int i = 0; i < items.Count; i++)
{
    var match = lookup.FirstOrDefault(x => x.Key == items[i].Category);
    // Allocates delegate + enumerator every iteration
}

// GOOD: pre-build lookup outside loop
var categoryMap = lookup.ToDictionary(x => x.Key);
for (int i = 0; i < items.Count; i++)
{
    categoryMap.TryGetValue(items[i].Category, out var match);
}
```

**Fix**: Hoist LINQ queries out of loops. Pre-build lookup structures (`Dictionary`, `HashSet`, `ToLookup`) before the loop.

---

#### PERF-LINQ-05: `.OrderBy().First()` instead of custom min scan

**Severity**: Medium
**Impact**: `.OrderBy().First()` is O(N log N) to find the minimum. A manual scan or `.MinBy()` (.NET 6+) is O(N).

```csharp
// BAD: O(N log N) to find minimum
var cheapest = products.OrderBy(p => p.Price).First();

// GOOD (.NET 6+): O(N) MinBy
var cheapest = products.MinBy(p => p.Price);

// GOOD (any version): manual scan
Product cheapest = products[0];
foreach (var p in products)
{
    if (p.Price < cheapest.Price) cheapest = p;
}
```

**Fix**: Use `.MinBy()` (.NET 6+) or a manual scan instead of `.OrderBy().First()`.

---

#### PERF-LINQ-06: `.Select().ToList()` where `ConvertAll` or loop is lighter

**Severity**: Low
**Impact**: `.Select().ToList()` allocates a `SelectIterator` + `List<T>`. `List<T>.ConvertAll` or a pre-sized list with a loop avoids the intermediate allocation.

```csharp
// OK but allocates iterator:
var names = users.Select(u => u.Name).ToList();

// SLIGHTLY BETTER: ConvertAll (no intermediate iterator)
var names = users.ConvertAll(u => u.Name); // Only works on List<T>

// BEST for hot paths: pre-sized list
var names = new List<string>(users.Count);
foreach (var u in users) names.Add(u.Name);
```

**Fix**: For hot paths on `List<T>`, use `ConvertAll`. For extreme performance, use a pre-sized loop. For normal code, `.Select().ToList()` is fine.

---

#### PERF-LINQ-07: Dictionary lookup: `.ContainsKey()` + indexer instead of `.TryGetValue()`

**Severity**: Medium
**Impact**: Double hash computation + double bucket traversal. For large dictionaries, this measurably increases lookup time.

```csharp
// BAD: two lookups
if (dict.ContainsKey(key))
{
    var value = dict[key]; // Second hash + traversal
    Process(value);
}

// GOOD: single lookup
if (dict.TryGetValue(key, out var value))
{
    Process(value);
}
```

**Fix**: Replace `ContainsKey` + indexer with `TryGetValue`.

---

#### PERF-LINQ-08: Wrong collection type for workload

**Severity**: Medium
**Impact**: `List<T>` lookup is O(N). `HashSet<T>` / `Dictionary<K,V>` lookup is O(1). For lookup-heavy workloads with >16 elements, this is a significant difference.

```csharp
// BAD: List<T> used for frequent lookups
var allowedIds = new List<int> { 1, 2, 3, ..., 500 };
if (allowedIds.Contains(userId)) { ... } // O(N) scan

// GOOD: HashSet<T> for O(1) lookup
var allowedIds = new HashSet<int> { 1, 2, 3, ..., 500 };
if (allowedIds.Contains(userId)) { ... } // O(1)
```

**Fix**: Use `HashSet<T>` for membership checks, `Dictionary<K,V>` for key-value lookups, `List<T>` for ordered/indexed access.

---

#### PERF-LINQ-09: `IEnumerable<T>` parameter causing repeated materialization

**Severity**: Medium
**Impact**: If the caller passes a LINQ query as `IEnumerable<T>`, every enumeration inside the method re-executes the query. Multiple enumerations multiply cost.

```csharp
// BAD: IEnumerable may be re-evaluated on each use
void ProcessItems(IEnumerable<Item> items)
{
    if (!items.Any()) return;         // First enumeration
    var count = items.Count();         // Second enumeration
    foreach (var item in items) { }    // Third enumeration
}

// GOOD: accept IReadOnlyList<T> or materialize inside
void ProcessItems(IReadOnlyList<Item> items)
{
    if (items.Count == 0) return;     // O(1)
    foreach (var item in items) { }   // Single enumeration
}

// GOOD: materialize at the boundary
void ProcessItems(IEnumerable<Item> items)
{
    var list = items as IList<Item> ?? items.ToList();
    // Now safe to enumerate multiple times
}
```

**Fix**: Use `IReadOnlyList<T>` or `IReadOnlyCollection<T>` as parameter types when multiple enumeration is needed. Or materialize at method entry with `as IList<T> ?? .ToList()`.

---

#### PERF-LINQ-10: Missing `.AsSpan()` for string slicing

**Severity**: Medium
**Impact**: `string.Substring()` allocates a new string (40 + 2*length bytes). `AsSpan().Slice()` or `AsSpan(start, length)` is zero-allocation.

```csharp
// BAD: allocates new string for each slice
string header = line.Substring(0, colonIndex);        // New string
string value = line.Substring(colonIndex + 1).Trim();  // Two new strings

// GOOD: zero-allocation span slicing
ReadOnlySpan<char> header = line.AsSpan(0, colonIndex);
ReadOnlySpan<char> value = line.AsSpan(colonIndex + 1).Trim();

// Use spans with parsing APIs
if (int.TryParse(value, out int result)) { ... }
```

**Fix**: Use `AsSpan()` for string slicing when the result is consumed immediately (parsing, comparison, writing to output) and does not need to be stored as a string.

---

### UI & Rendering Performance (8 patterns)

---

#### PERF-UI-01: Synchronous I/O on UI thread

**Severity**: Critical
**Impact**: Any synchronous I/O (file, network, registry, database) blocks the UI thread and message pump. The UI freezes for the entire I/O duration. Even fast I/O (~5ms) causes visible jank at 60fps.

```csharp
// BAD: file read on UI thread
void LoadSettings_Click(object sender, RoutedEventArgs e)
{
    var json = File.ReadAllText("settings.json"); // UI frozen
    ApplySettings(JsonSerializer.Deserialize<Settings>(json));
}

// GOOD: async I/O
async void LoadSettings_Click(object sender, RoutedEventArgs e)
{
    using var stream = File.OpenRead("settings.json");
    var settings = await JsonSerializer.DeserializeAsync<Settings>(stream);
    ApplySettings(settings); // Back on UI thread
}
```

**Fix**: Replace all synchronous I/O on UI thread with async equivalents.

---

#### PERF-UI-02: Excessive property change notifications

**Severity**: Medium
**Impact**: Each `PropertyChanged` notification triggers binding evaluation, layout, and potentially rendering. Firing per-property when setting 10 properties causes 10 binding evaluations instead of one batch update.

```csharp
// BAD: 5 separate notifications = 5 binding evaluations
public void UpdateUser(UserDto dto)
{
    Name = dto.Name;         // PropertyChanged("Name")
    Email = dto.Email;       // PropertyChanged("Email")
    Age = dto.Age;           // PropertyChanged("Age")
    Title = dto.Title;       // PropertyChanged("Title")
    Avatar = dto.Avatar;     // PropertyChanged("Avatar")
}

// GOOD: batch update with single notification
public void UpdateUser(UserDto dto)
{
    _name = dto.Name;
    _email = dto.Email;
    _age = dto.Age;
    _title = dto.Title;
    _avatar = dto.Avatar;
    OnPropertyChanged(string.Empty); // Refreshes all bindings once
}
```

**Fix**: Set backing fields directly and raise a single `PropertyChanged(string.Empty)` or `PropertyChanged(null)` to refresh all bindings at once.

---

#### PERF-UI-03: Virtualization disabled on large ItemsControl

**Severity**: High
**Impact**: Without virtualization, the framework instantiates a UI element for every item in the collection. For 10,000 items, this creates 10,000 elements at load time, consuming massive memory and causing multi-second load delays.

```xml
<!-- BAD: StackPanel does not virtualize -->
<ItemsControl ItemsSource="{Binding LargeList}">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate>
            <StackPanel /> <!-- All items instantiated -->
        </ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
</ItemsControl>

<!-- GOOD: VirtualizingStackPanel only creates visible items -->
<ListBox ItemsSource="{Binding LargeList}">
    <ListBox.ItemsPanel>
        <ItemsPanelTemplate>
            <VirtualizingStackPanel />
        </ItemsPanelTemplate>
    </ListBox.ItemsPanel>
</ListBox>

<!-- GOOD (WPF): Enable recycling for even better performance -->
<ListBox VirtualizingPanel.IsVirtualizing="True"
         VirtualizingPanel.VirtualizationMode="Recycling"
         ItemsSource="{Binding LargeList}" />
```

**Fix**: Use `VirtualizingStackPanel` (or `ItemsStackPanel` in WinUI) with recycling for large collections. Avoid `StackPanel` or `WrapPanel` as `ItemsPanel` for large data sets.

---

#### PERF-UI-04: Complex binding expressions re-evaluated every frame

**Severity**: Medium
**Impact**: Multi-binding converters or complex binding paths that are re-evaluated on every render frame add CPU cost per frame.

```csharp
// BAD: MultiBinding with expensive converter fires every layout pass
public class ExpensiveConverter : IMultiValueConverter
{
    public object Convert(object[] values, ...)
    {
        // Heavy computation here, runs on every binding update
        return ComputeComplexLayout(values);
    }
}

// GOOD: cache result and only recompute when inputs change
public class CachedConverter : IMultiValueConverter
{
    private object[] _lastInputs;
    private object _cachedResult;

    public object Convert(object[] values, ...)
    {
        if (InputsUnchanged(values, _lastInputs))
            return _cachedResult;
        _lastInputs = values.ToArray();
        _cachedResult = ComputeComplexLayout(values);
        return _cachedResult;
    }
}

// BETTER: compute in ViewModel, bind to simple property
```

**Fix**: Move complex logic to the ViewModel. Use simple property bindings. If converters are necessary, cache results.

---

#### PERF-UI-05: DataTemplate with heavy constructor logic

**Severity**: Medium
**Impact**: DataTemplates are instantiated for each visible item (and recycled items with virtualization). Heavy constructor logic (I/O, computation, large allocations) runs for each instantiation.

```csharp
// BAD: heavy work in UserControl constructor used as DataTemplate
public partial class ItemCard : UserControl
{
    public ItemCard()
    {
        InitializeComponent();
        _analyzer = new HeavyAnalyzer();   // Expensive per item
        _cache = LoadTemplateCache();       // I/O in constructor
    }
}

// GOOD: defer heavy work, share resources
public partial class ItemCard : UserControl
{
    private static readonly HeavyAnalyzer _sharedAnalyzer = new(); // Shared

    public ItemCard()
    {
        InitializeComponent();
        // Heavy work deferred to Loaded event or explicit init
    }
}
```

**Fix**: Keep DataTemplate constructors lightweight. Share expensive resources via static fields. Defer heavy work to `Loaded` event.

---

#### PERF-UI-06: Unnecessary layout invalidation

**Severity**: Medium
**Impact**: Setting properties that trigger `Measure`/`Arrange` (Width, Height, Margin, Padding, Visibility, etc.) causes a layout pass. Multiple changes without batching cause multiple layout passes per frame.

```csharp
// BAD: each property set triggers layout pass
item.Width = 100;      // Layout pass 1
item.Height = 50;      // Layout pass 2
item.Margin = new(10); // Layout pass 3

// GOOD: batch changes or use a single transform
// In WPF/WinUI, layout is batched per frame automatically in most cases,
// but explicit SuspendLayout patterns help in WinForms:
panel.SuspendLayout();
item.Width = 100;
item.Height = 50;
item.Margin = new(10);
panel.ResumeLayout();

// BETTER: avoid changing layout properties when values haven't changed
if (item.Width != newWidth) item.Width = newWidth;
```

**Fix**: Check if value changed before setting layout properties. In WinForms, use `SuspendLayout`/`ResumeLayout`. In WPF/WinUI, changes within a single synchronous block are typically batched automatically.

---

#### PERF-UI-07: Image decoding on UI thread

**Severity**: High
**Impact**: Decoding a large image (e.g., 4000x3000 JPEG) can take 50-200ms. On the UI thread, this freezes the application. Additionally, loading at full resolution when displaying a thumbnail wastes memory.

```csharp
// BAD: full-resolution decode on UI thread
var bitmap = new BitmapImage(new Uri(path));
myImage.Source = bitmap; // Decodes full image on UI thread

// GOOD: decode at display size, async
var bitmap = new BitmapImage();
bitmap.DecodePixelWidth = 200; // Decode at thumbnail size
bitmap.UriSource = new Uri(path);
myImage.Source = bitmap;

// GOOD: fully async loading
var bitmap = new BitmapImage();
bitmap.DecodePixelWidth = 200;
bitmap.CreateOptions = BitmapCreateOptions.DelayCreation;
bitmap.UriSource = new Uri(path);
myImage.Source = bitmap;
```

**Fix**: Always set `DecodePixelWidth` or `DecodePixelHeight` to match the display size. Use delayed creation or async loading for large images.

---

#### PERF-UI-08: Timer-driven UI updates without dirty checking

**Severity**: High
**Impact**: Updating UI elements on every timer tick (e.g., 60fps) even when the data has not changed wastes CPU on unnecessary layout and rendering.

```csharp
// BAD: updates UI every tick regardless of change
timer.Tick += (s, e) =>
{
    progressText.Text = $"{_progress}%";     // Causes layout even if unchanged
    progressBar.Value = _progress;            // Causes render even if unchanged
};

// GOOD: only update when value changes
private double _lastProgress;
timer.Tick += (s, e) =>
{
    if (_progress != _lastProgress)
    {
        _lastProgress = _progress;
        progressText.Text = $"{_progress}%";
        progressBar.Value = _progress;
    }
};
```

**Fix**: Track previous values and only update UI elements when the backing data has actually changed.

---

### Span<T> & Zero-Copy (5 patterns)

---

#### PERF-SPAN-01: `string.Substring()` where `ReadOnlySpan<char>` suffices

**Severity**: Medium
**Impact**: Each `Substring` call allocates a new string (40 + 2*length bytes). For parsing loops processing thousands of strings, this adds significant GC pressure.

```csharp
// BAD: allocates new string per token
string[] ParseCsv(string line)
{
    var tokens = new List<string>();
    int start = 0;
    for (int i = 0; i < line.Length; i++)
    {
        if (line[i] == ',')
        {
            tokens.Add(line.Substring(start, i - start)); // Allocation
            start = i + 1;
        }
    }
    tokens.Add(line.Substring(start));
    return tokens.ToArray();
}

// GOOD: use Span for intermediate parsing, allocate only final results
void ParseCsv(ReadOnlySpan<char> line, List<string> results)
{
    while (true)
    {
        int idx = line.IndexOf(',');
        if (idx < 0)
        {
            results.Add(line.ToString()); // Only allocate final strings
            break;
        }
        results.Add(line[..idx].ToString());
        line = line[(idx + 1)..];
    }
}

// BEST: if consumer can work with spans, avoid ToString entirely
```

**Fix**: Use `ReadOnlySpan<char>` for intermediate string parsing. Only call `.ToString()` on the final result that needs to be stored.

---

#### PERF-SPAN-02: Array copy where Span slice would work

**Severity**: Medium
**Impact**: `Array.Copy` or LINQ `.Skip().Take().ToArray()` allocates a new array. `Span<T>.Slice()` provides a zero-allocation view into the original array.

```csharp
// BAD: allocates new array for a sub-range
byte[] header = new byte[4];
Array.Copy(packet, 0, header, 0, 4); // 4-byte allocation + copy
ProcessHeader(header);

// GOOD: zero-allocation slice
Span<byte> header = packet.AsSpan(0, 4);
ProcessHeader(header);

// BAD: LINQ slice allocates
var page = items.Skip(offset).Take(pageSize).ToArray(); // New array

// GOOD: span slice (if items is an array)
var page = items.AsSpan(offset, pageSize);
```

**Fix**: Use `AsSpan(start, length)` or `Span<T>.Slice()` instead of `Array.Copy` or LINQ slice patterns.

---

#### PERF-SPAN-03: `Encoding.GetString(byte[])` where `Span<byte>` overload available

**Severity**: Low
**Impact**: Minor -- avoids potential array copy if the source is already a `Span<byte>` or `Memory<byte>`.

```csharp
// BAD: converting Span to array just to call GetString
ReadOnlySpan<byte> buffer = GetBuffer();
string text = Encoding.UTF8.GetString(buffer.ToArray()); // Unnecessary array copy

// GOOD: use Span overload directly
ReadOnlySpan<byte> buffer = GetBuffer();
string text = Encoding.UTF8.GetString(buffer); // No intermediate array
```

**Fix**: Use the `ReadOnlySpan<byte>` overload of `Encoding.GetString` when available (.NET Core 2.1+).

---

#### PERF-SPAN-04: `MemoryMarshal` without verifying alignment requirements

**Severity**: High
**Impact**: `MemoryMarshal.Cast<TFrom, TTo>` reinterprets bytes without alignment checks. On architectures that require aligned access, this causes `DataMisalignedException` or silent corruption. Even on x86, misaligned access has a performance penalty.

```csharp
// BAD: no alignment check
ReadOnlySpan<byte> data = GetPacket();
ReadOnlySpan<int> ints = MemoryMarshal.Cast<byte, int>(data);
// If data is not 4-byte aligned, this may fault or corrupt on ARM

// GOOD: verify alignment or use BinaryPrimitives
ReadOnlySpan<byte> data = GetPacket();
int value = BinaryPrimitives.ReadInt32LittleEndian(data);

// GOOD: if you must use Cast, ensure alignment
if (((nint)Unsafe.AsPointer(ref MemoryMarshal.GetReference(data)) & 3) != 0)
    throw new InvalidOperationException("Data is not 4-byte aligned");
var ints = MemoryMarshal.Cast<byte, int>(data);
```

**Fix**: Prefer `BinaryPrimitives.ReadXxx` for reading individual values. If `MemoryMarshal.Cast` is needed for batch processing, verify alignment first.

---

#### PERF-SPAN-05: `stackalloc` without fallback for large sizes

**Severity**: High
**Impact**: `stackalloc` allocates on the thread stack, which is typically 1MB. Allocating a large buffer (e.g., based on untrusted input size) causes a `StackOverflowException`, which cannot be caught and terminates the process.

```csharp
// BAD: unbounded stackalloc -- StackOverflowException risk
void ProcessData(int size)
{
    Span<byte> buffer = stackalloc byte[size]; // If size > ~500KB, stack overflow!
    FillBuffer(buffer);
}

// GOOD: threshold with ArrayPool fallback
void ProcessData(int size)
{
    const int StackThreshold = 256; // bytes
    byte[]? rented = null;
    Span<byte> buffer = size <= StackThreshold
        ? stackalloc byte[size]
        : (rented = ArrayPool<byte>.Shared.Rent(size));
    try
    {
        FillBuffer(buffer[..size]);
    }
    finally
    {
        if (rented is not null)
            ArrayPool<byte>.Shared.Return(rented);
    }
}
```

**Fix**: Always use a size threshold (typically 128-512 bytes) for `stackalloc` and fall back to `ArrayPool` for larger sizes.

---

## Fix Strategy Decision Tree

```
What kind of performance issue?
├── Allocation / GC Pressure
│   ├── String concat in loop? → StringBuilder or string.Join (PERF-ALLOC-01)
│   ├── LINQ materializing needlessly? → Remove .ToList()/.ToArray() (PERF-ALLOC-03)
│   ├── Boxing? → Generic constraints, explicit .ToString() (PERF-ALLOC-04)
│   ├── Closure in hot path? → static lambda, hoist delegate (PERF-ALLOC-05)
│   ├── Logging allocations? → Structured logging, source generators (PERF-ALLOC-06)
│   ├── Frequently-sync async? → ValueTask (PERF-ALLOC-07)
│   ├── Large temp arrays? → ArrayPool (PERF-ALLOC-12)
│   └── Large struct copies? → in/ref parameters (PERF-ALLOC-09)
├── LINQ / Collection
│   ├── Multiple enumeration? → Materialize once (PERF-LINQ-01)
│   ├── Count when Any suffices? → .Any() (PERF-LINQ-02)
│   ├── LINQ in tight loop? → Hoist or pre-build lookup (PERF-LINQ-04)
│   ├── Double dictionary lookup? → TryGetValue (PERF-LINQ-07)
│   ├── Wrong collection? → HashSet for lookup, List for iteration (PERF-LINQ-08)
│   └── Substring allocations? → AsSpan (PERF-LINQ-10)
├── UI / Rendering
│   ├── Sync I/O on UI thread? → Async I/O methods (PERF-UI-01)
│   ├── Missing virtualization? → VirtualizingStackPanel (PERF-UI-03)
│   ├── Image decode on UI? → DecodePixelWidth + async load (PERF-UI-07)
│   └── Timer without dirty check? → Track previous values (PERF-UI-08)
└── Span / Zero-Copy
    ├── Substring in parsing? → ReadOnlySpan<char> (PERF-SPAN-01)
    ├── Array.Copy for slice? → AsSpan().Slice() (PERF-SPAN-02)
    └── Unbounded stackalloc? → Threshold + ArrayPool fallback (PERF-SPAN-05)
```

## Verification Checklist

For each fix:
- [ ] BenchmarkDotNet confirms improvement (or manual timing for UI scenarios)
- [ ] No behavior change (same output, fewer resources)
- [ ] Cost suppression rules checked (not a false positive on cold path / small collection / test code)
- [ ] Fix does not reduce readability disproportionately to the gain
- [ ] Build succeeds; no new warnings
- [ ] Existing tests pass

## References

1. .NET Blog -- "Performance Improvements in .NET" (annual series) (https://devblogs.microsoft.com/dotnet/)
2. .NET Runtime team -- Performance best practices (https://learn.microsoft.com/en-us/dotnet/framework/performance/performance-tips)
3. BenchmarkDotNet -- .NET benchmarking framework (https://benchmarkdotnet.org/)
4. .NET API documentation -- ArrayPool, Span, ValueTask (https://learn.microsoft.com/en-us/dotnet/api/)
