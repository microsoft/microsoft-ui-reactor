---
name: cs-error-handling-review
description: >-
  Review C# code for error handling and null safety defects: catching base
  Exception without rethrowing, empty catch blocks, throw-ex resetting stack
  trace, throwing from finalizer/Dispose, static constructor exceptions
  permanently breaking types, async void swallowing exceptions, nullable
  reference type annotation mismatches, null-forgiving operator hiding real
  null paths, missing argument validation at public API boundaries, and
  guard clauses throwing wrong exception types.
  28 patterns from .NET team guidelines and Framework Design Guidelines.
  Covers exception patterns, null safety, and
  validation/contracts domains.
  Use this skill when reviewing C# code that handles exceptions, uses
  nullable reference types, validates parameters, or mixes async/sync
  error models.
keywords:
  - exception handling
  - catch
  - throw
  - "throw ex"
  - try/catch
  - finally
  - nullable
  - "null-forgiving"
  - "!"
  - "?."
  - ArgumentException
  - ArgumentNullException
  - InvalidOperationException
  - NotImplementedException
  - OperationCanceledException
  - async void
  - AggregateException
  - "[NotNull]"
  - "[MaybeNull]"
  - guard clause
  - validation
  - TryParse
  - "default!"
metadata:
  category: code-review
  subcategory: error-handling
  complexity: medium
  duration: 20-60 minutes per component
  author: Windows Engineering Systems
  version: "1.0"
  source: >-
    .NET team guidelines, Framework Design Guidelines
  pattern-count: 28
  domains:
    - exception-patterns
    - null-safety
    - validation-contracts
---

# C# Error Handling & Null Safety Code Review

## Quick Detection

**Primary Symptoms (in code under review)**:
- `catch (Exception)` without `throw;` (swallows all errors including `OutOfMemoryException`)
- `catch { }` with empty body (silent failure)
- `throw ex;` instead of `throw;` (stack trace reset)
- Throwing exceptions from `Dispose()` or finalizers
- `async void` methods (unobserved exceptions crash the process)
- Null-forgiving operator `!` used to silence nullable warnings without justification
- Missing null checks on public API parameters
- `ArgumentException` without `paramName` parameter
- `as` cast followed by member access without null check

**Key Code Patterns to Search For**:
```csharp
// Stack trace destroyed
catch (Exception ex)
{
    Log(ex);
    throw ex;  // BAD: resets stack trace -- use 'throw;'
}

// Silent failure
catch (Exception) { }  // BAD: swallows ALL exceptions including OOM, ThreadAbort

// async void -- unobserved exception
async void OnButtonClick(object sender, EventArgs e)  // BAD
{
    await DoWorkAsync();  // exception crashes the process
}

// Null-forgiving hiding real bug
string name = GetName()!;  // BAD: if GetName() returns null, NRE at runtime
name.ToUpper();

// Missing paramName
throw new ArgumentException("Value must be positive");  // BAD: which parameter?
```

## Analysis Workflow

### Step 1: Identify Error Handling Model

Determine the error handling strategy used by the component.

1. Search for exception handling patterns:
   ```
   // Check for catch blocks, throw statements, and error models
   Search for: catch, throw, try, finally, Result<T>, OneOf
   ```

2. Check for nullable context:
   ```
   // Is nullable reference types enabled?
   Search for: <Nullable>enable</Nullable> in .csproj, or #nullable enable in .cs files
   ```

3. Classify: **Exception-based** (standard .NET), **Result-pattern** (functional style), **Mixed** (requires extra care at boundaries)

### Step 2: Scan for Pattern Matches

Apply the 28 error handling and null safety patterns from the catalog below.

**Priority order** (by real-world impact):

1. **Exception swallowing & silent failures** (EH-EXC-01, EH-EXC-02) -- bugs disappear from production diagnostics
2. **Async void & unobserved exceptions** (EH-EXC-10) -- crashes production processes
3. **Static constructor exceptions** (EH-EXC-05) -- permanently breaks type for app lifetime
4. **Null safety violations** (EH-NULL-01 through EH-NULL-04) -- NullReferenceException in production
5. **Stack trace & diagnostic quality** (EH-EXC-03, EH-EXC-07, EH-VAL-03) -- hinders incident response
6. **Validation correctness** (EH-VAL-01, EH-VAL-02) -- allows invalid state to propagate

### Step 3: Classify Findings

For each potential match:

1. **Confirm**: Is the pattern actually present, or is there compensating logic (e.g., logged and re-thrown elsewhere)?
2. **Severity**: Prioritize by impact:
   - **Critical**: Exception in static constructor, throw from finalizer
   - **High**: Exception swallowing, async void, null safety violations
   - **Medium**: Stack trace loss, missing validation, control-flow exceptions
   - **Low**: Style issues, redundant null checks
3. **False Positive Check**: Is the empty catch deliberate (e.g., best-effort cleanup)? Is the null-forgiving operator justified (e.g., post-deserialization guarantee)?

### Step 4: Generate Fix

Use the fix strategy decision trees below. Each fix must follow the before/after format.

### Step 5: Verify Fix

1. **Static verification**: Confirm the fix handles all code paths (normal, exception, cancellation)
2. **Build verification**: Ensure no new warnings (especially CS8600, CS8602, CS8603 nullable warnings)
3. **Pattern regression**: Search for same pattern re-introduced elsewhere in the PR
4. **Test coverage**: Run existing unit tests; suggest new test if none covers the error path

---

## Pattern Catalog

### Exception Patterns

#### EH-EXC-01: Catching `Exception` (Base Class) Without Rethrowing

| Field | Detail |
|-------|--------|
| **ID** | EH-EXC-01 |
| **Severity** | High |
| **Signal** | `catch (Exception)` or `catch (Exception ex)` where the body does not contain `throw;` or `throw` with a wrapping exception |
| **Risk** | Swallows all exceptions including `OutOfMemoryException`, `StackOverflowException`, `ThreadAbortException`. Bugs silently disappear. System continues in corrupt state |
| **Fix** | Catch specific exception types. If catching `Exception` is truly needed (e.g., top-level handler), always log and rethrow or propagate |

```csharp
// BAD -- swallows everything, including OOM
try
{
    ProcessData();
}
catch (Exception ex)
{
    _logger.LogError(ex, "Processing failed");
    // execution continues in potentially corrupt state!
}

// GOOD -- catch specific types
try
{
    ProcessData();
}
catch (IOException ex)
{
    _logger.LogError(ex, "IO error during processing");
    return Result.Failure("IO error");
}
catch (InvalidOperationException ex)
{
    _logger.LogError(ex, "Invalid state during processing");
    return Result.Failure("Invalid operation");
}

// GOOD -- if catching Exception, rethrow
try
{
    ProcessData();
}
catch (Exception ex)
{
    _logger.LogError(ex, "Processing failed");
    throw;  // rethrow preserving stack trace
}
```

---

#### EH-EXC-02: `catch` Block with Empty Body (Silent Failure)

| Field | Detail |
|-------|--------|
| **ID** | EH-EXC-02 |
| **Severity** | High |
| **Signal** | `catch` block with empty body `{ }`, or body containing only a comment |
| **Risk** | Complete information loss. Exceptions vanish without trace. When production breaks, there is no diagnostic evidence. Most dangerous when catching broad exception types |
| **Fix** | At minimum, log the exception. If truly intentional (best-effort cleanup), add explicit comment AND log at Debug/Trace level |

```csharp
// BAD -- silent failure
try
{
    file.Delete();
}
catch (IOException) { }  // if delete fails, nobody knows

// BAD -- comment does not help diagnostics
try
{
    file.Delete();
}
catch (IOException)
{
    // ignore -- best effort cleanup
}

// GOOD -- log even for best-effort
try
{
    file.Delete();
}
catch (IOException ex)
{
    _logger.LogDebug(ex, "Best-effort file cleanup failed for {Path}", file.Path);
}
```

---

#### EH-EXC-03: `throw ex;` Instead of `throw;` (Resets Stack Trace)

| Field | Detail |
|-------|--------|
| **ID** | EH-EXC-03 |
| **Severity** | High |
| **Signal** | `throw ex;` inside a catch block where `ex` is the caught exception variable |
| **Risk** | Resets the stack trace to the current catch block location. The original throw site is lost. Makes root-cause analysis of production incidents extremely difficult |
| **Fix** | Use bare `throw;` to preserve the original stack trace. If wrapping, use `new WrapperException("msg", ex)` to preserve as inner exception |

```csharp
// BAD -- stack trace lost
catch (Exception ex)
{
    _logger.LogError(ex, "Failed");
    throw ex;  // stack trace points HERE, not where exception originated
}

// GOOD -- preserves stack trace
catch (Exception ex)
{
    _logger.LogError(ex, "Failed");
    throw;  // stack trace preserved from original throw site
}

// GOOD -- wrapping with inner exception preserved
catch (Exception ex)
{
    throw new ServiceException("Data processing failed", ex);
    // ex.StackTrace preserved in InnerException
}
```

---

#### EH-EXC-04: Throwing from Finalizer/Dispose (Can Crash Process)

| Field | Detail |
|-------|--------|
| **ID** | EH-EXC-04 |
| **Severity** | Critical |
| **Signal** | `throw` statement inside `~ClassName()` finalizer, or inside `Dispose()` method that could be called from a `finally` block or `using` statement where another exception is already in flight |
| **Risk** | Exception from finalizer terminates the finalizer thread, preventing all subsequent finalization (resource leak cascade). Exception from Dispose during stack unwinding replaces the original exception, losing diagnostic information. In .NET, an unhandled finalizer exception crashes the process |
| **Fix** | Never throw from finalizers. In Dispose, catch and log/suppress exceptions from cleanup. If Dispose must report errors, use `IAsyncDisposable` with error reporting in the async path |

```csharp
// BAD -- finalizer throws
~ResourceHolder()
{
    if (_handle == IntPtr.Zero)
        throw new InvalidOperationException("Handle already freed");
    // throws crash the finalizer thread
    NativeMethods.FreeHandle(_handle);
}

// BAD -- Dispose throws, losing original exception during unwinding
public void Dispose()
{
    _connection.Close();  // may throw if connection is in bad state
    // if called from 'using' during exception unwinding,
    // this exception replaces the original
}

// GOOD -- finalizer never throws
~ResourceHolder()
{
    try
    {
        if (_handle != IntPtr.Zero)
            NativeMethods.FreeHandle(_handle);
    }
    catch
    {
        // suppress -- finalizer must never throw
    }
}

// GOOD -- Dispose suppresses cleanup failures
public void Dispose()
{
    try { _connection.Close(); }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Error closing connection during dispose");
    }
}
```

---

#### EH-EXC-05: Exception in Static Constructor (Type Permanently Broken)

| Field | Detail |
|-------|--------|
| **ID** | EH-EXC-05 |
| **Severity** | Critical |
| **Signal** | Code in `static ClassName()` constructor or static field initializers that can throw (file I/O, network calls, parsing environment variables, type loading) |
| **Risk** | If a static constructor throws, the CLR wraps it in `TypeInitializationException` and the type is **permanently unusable** for the lifetime of the `AppDomain`. Every subsequent access throws `TypeInitializationException`. No retry is possible |
| **Fix** | Move fallible initialization to a lazy static method or `Lazy<T>`. Catch and handle exceptions within the static constructor. Never do I/O or network calls in static constructors |

```csharp
// BAD -- file read in static constructor
class Config
{
    static readonly string _connectionString;
    static Config()
    {
        // If file is missing, Config type is PERMANENTLY broken
        _connectionString = File.ReadAllText("config.txt");
    }
}

// GOOD -- lazy initialization with error handling
class Config
{
    private static readonly Lazy<string> _connectionString = new(() =>
    {
        try
        {
            return File.ReadAllText("config.txt");
        }
        catch (IOException)
        {
            return "DefaultConnectionString";
        }
    });

    public static string ConnectionString => _connectionString.Value;
}
```

---

#### EH-EXC-06: Catching `OperationCanceledException` and Treating as Error

| Field | Detail |
|-------|--------|
| **ID** | EH-EXC-06 |
| **Severity** | Medium |
| **Signal** | `catch (OperationCanceledException)` with error logging, retry logic, or error result return. Also: catching `TaskCanceledException` (derives from `OperationCanceledException`) as an error |
| **Risk** | Cancellation is a normal control flow in .NET async. Treating it as an error generates noise in logs, triggers unnecessary alerts, and may cause retry storms (retrying an operation the user intentionally cancelled) |
| **Fix** | Let `OperationCanceledException` propagate. If catching, check `CancellationToken.IsCancellationRequested` to distinguish cancellation from timeout. Log at Information/Debug level, not Error |

```csharp
// BAD -- cancellation treated as error
try
{
    await httpClient.GetAsync(url, cancellationToken);
}
catch (OperationCanceledException ex)
{
    _logger.LogError(ex, "Request failed!");  // not a failure -- user cancelled
    throw new ServiceException("HTTP request failed", ex);  // wrong wrapping
}

// GOOD -- cancellation handled appropriately
try
{
    await httpClient.GetAsync(url, cancellationToken);
}
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    _logger.LogInformation("Request cancelled by caller");
    throw;  // let cancellation propagate naturally
}
catch (OperationCanceledException ex)
{
    // Not our token -- this is a timeout
    _logger.LogWarning(ex, "Request timed out");
    throw new TimeoutException("HTTP request timed out", ex);
}
```

---

#### EH-EXC-07: Not Preserving Inner Exception in Wrapper Exception

| Field | Detail |
|-------|--------|
| **ID** | EH-EXC-07 |
| **Severity** | Medium |
| **Signal** | `throw new SomeException("message")` inside a catch block without passing the caught exception as inner exception |
| **Risk** | Original exception details (type, message, stack trace) are lost. Debugging requires reproducing the issue to discover the root cause. Particularly harmful in layered architectures where exceptions are wrapped at each layer |
| **Fix** | Always pass the caught exception as the `innerException` parameter |

```csharp
// BAD -- original exception lost
catch (SqlException ex)
{
    throw new DataAccessException("Database query failed");
    // ex is gone -- no way to see the SQL error code, server, etc.
}

// GOOD -- inner exception preserved
catch (SqlException ex)
{
    throw new DataAccessException("Database query failed", ex);
    // ex preserved: ex.Number, ex.Server, ex.StackTrace all available
}
```

---

#### EH-EXC-08: Using Exceptions for Control Flow

| Field | Detail |
|-------|--------|
| **ID** | EH-EXC-08 |
| **Severity** | Medium |
| **Signal** | `try/catch` used to test conditions that have `TryParse`/`TryGetValue` alternatives. Common: `int.Parse` in try/catch instead of `int.TryParse`, `Dictionary[key]` in try/catch instead of `TryGetValue` |
| **Risk** | Exception creation is expensive (~1000x slower than a conditional check). In hot paths, causes severe performance degradation. Also clutters exception telemetry with expected non-errors |
| **Fix** | Use Try-pattern APIs: `TryParse`, `TryGetValue`, `TryCreate`. If no Try variant exists, check preconditions before the operation |

```csharp
// BAD -- exception for expected condition
public int ParseAge(string input)
{
    try
    {
        return int.Parse(input);
    }
    catch (FormatException)
    {
        return -1;  // exceptions are expensive control flow
    }
}

// GOOD -- TryParse pattern
public int ParseAge(string input)
{
    return int.TryParse(input, out int age) ? age : -1;
}

// BAD -- exception for key lookup
try
{
    var value = dictionary[key];
    Process(value);
}
catch (KeyNotFoundException) { }

// GOOD -- TryGetValue
if (dictionary.TryGetValue(key, out var value))
{
    Process(value);
}
```

---

#### EH-EXC-09: Missing `finally` for Cleanup When `using` Is Not Applicable

| Field | Detail |
|-------|--------|
| **ID** | EH-EXC-09 |
| **Severity** | Medium |
| **Signal** | Resource acquisition or state change (e.g., setting a flag, entering a mode, incrementing a counter) in a `try` block without corresponding cleanup in a `finally` block, where `using`/`IDisposable` is not applicable |
| **Risk** | If an exception occurs, the cleanup code is skipped. Leaves the system in an inconsistent state (flag set permanently, counter never decremented, temp file not deleted) |
| **Fix** | Add `finally` block for cleanup. Or: refactor into an `IDisposable` wrapper that can use `using` |

```csharp
// BAD -- flag not reset on exception
_isProcessing = true;
try
{
    ProcessBatch();  // if this throws, _isProcessing stays true forever
}
catch (Exception ex)
{
    _logger.LogError(ex, "Batch failed");
}
_isProcessing = false;  // skipped if ProcessBatch throws and catch rethrows

// GOOD -- finally ensures cleanup
_isProcessing = true;
try
{
    ProcessBatch();
}
catch (Exception ex)
{
    _logger.LogError(ex, "Batch failed");
    throw;
}
finally
{
    _isProcessing = false;  // always runs, even if exception is thrown
}
```

---

#### EH-EXC-10: `async void` Swallowing Exceptions

| Field | Detail |
|-------|--------|
| **ID** | EH-EXC-10 |
| **Severity** | High |
| **Signal** | Method declared `async void` (not `async Task`). Exception: event handlers in WPF/WinForms are legitimately `async void` but still need try/catch |
| **Risk** | Exceptions in `async void` methods are posted to the `SynchronizationContext`. If there is no context (console app, background thread), they become `UnobservedTaskException` and crash the process. Even with a context, the exception cannot be caught by the caller |
| **Fix** | Change to `async Task` and let caller await. For event handlers that must be `async void`, wrap the entire body in try/catch |

```csharp
// BAD -- async void, exception crashes process
async void ProcessMessage(Message msg)
{
    var result = await _service.HandleAsync(msg);
    // if HandleAsync throws, exception is unobservable
    SaveResult(result);
}

// GOOD -- async Task, caller can observe exceptions
async Task ProcessMessageAsync(Message msg)
{
    var result = await _service.HandleAsync(msg);
    SaveResult(result);
}

// ACCEPTABLE -- event handler must be async void, but has try/catch
async void OnButtonClick(object sender, EventArgs e)
{
    try
    {
        await ProcessAsync();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Button click handler failed");
        ShowErrorToUser(ex.Message);
    }
}
```

---

#### EH-EXC-11: Throwing `NotImplementedException` in Production Code

| Field | Detail |
|-------|--------|
| **ID** | EH-EXC-11 |
| **Severity** | High |
| **Signal** | `throw new NotImplementedException()` in code that is not marked as draft/TODO/prototype, or in code being shipped to production |
| **Risk** | Runtime crash when the unimplemented code path is hit. Often left behind from interface scaffolding or feature stubs. `NotSupportedException` is the correct exception when an operation is intentionally unsupported |
| **Fix** | Implement the method, or throw `NotSupportedException` if the operation is intentionally unsupported. If it is a planned feature, add a tracking issue and throw `NotSupportedException` with a descriptive message |

```csharp
// BAD -- stub left in production code
public class FileExporter : IExporter
{
    public void Export(Data data)
    {
        throw new NotImplementedException();  // oops, forgot to implement
    }
}

// GOOD -- intentionally unsupported with clear exception
public class ReadOnlyStore : IDataStore
{
    public void Write(Data data)
    {
        throw new NotSupportedException(
            "ReadOnlyStore does not support write operations. Use ReadWriteStore instead.");
    }
}
```

---

#### EH-EXC-12: AggregateException Not Unwrapped from Task

| Field | Detail |
|-------|--------|
| **ID** | EH-EXC-12 |
| **Severity** | Medium |
| **Signal** | `Task.Wait()`, `Task.Result`, or `Task.WaitAll()` in a catch block that catches the specific inner exception type but not `AggregateException`. Or: `AggregateException` caught but inner exceptions not inspected |
| **Risk** | `Task.Wait()` and `.Result` wrap exceptions in `AggregateException`. Catch blocks for specific types (e.g., `HttpRequestException`) will not match. The exception appears as an unexpected `AggregateException` in logs |
| **Fix** | Use `await` instead of `.Wait()`/`.Result` (await unwraps automatically). If `Task.Wait()` is required, use `.GetAwaiter().GetResult()` or catch `AggregateException` and call `.Flatten().Handle()` |

```csharp
// BAD -- HttpRequestException wrapped in AggregateException, catch misses it
try
{
    task.Wait();  // wraps exceptions in AggregateException
}
catch (HttpRequestException ex)  // NEVER CATCHES -- wrapped in AggregateException
{
    HandleHttpError(ex);
}

// GOOD -- use await to unwrap
try
{
    await task;  // await unwraps AggregateException automatically
}
catch (HttpRequestException ex)
{
    HandleHttpError(ex);  // now this catches correctly
}

// ACCEPTABLE -- if await is not possible, use GetAwaiter().GetResult()
try
{
    task.GetAwaiter().GetResult();  // unwraps first inner exception
}
catch (HttpRequestException ex)
{
    HandleHttpError(ex);
}
```

---

### Null Safety

#### EH-NULL-01: Nullable Reference Type Annotation Mismatch

| Field | Detail |
|-------|--------|
| **ID** | EH-NULL-01 |
| **Severity** | High |
| **Signal** | Method return type is `T` (non-nullable) but code path returns `null`, `default`, or a nullable expression. Or: parameter declared as `T` but caller passes `T?` without compiler warning (nullable disabled in caller) |
| **Risk** | `NullReferenceException` at runtime. The compiler's nullable analysis trusts the annotations; incorrect annotations defeat the entire null-safety system and create a false sense of safety |
| **Fix** | Fix the annotation to match reality. If null is a valid return, declare `T?`. If null should not occur, add validation to ensure non-null. Enable nullable warnings as errors (`<WarningsAsErrors>nullable</WarningsAsErrors>`) |

```csharp
// BAD -- return type says non-null but can return null
public string GetDisplayName(User user)  // return type: string (non-nullable)
{
    return user.Profile?.DisplayName;  // can be null if Profile is null!
}

// GOOD -- annotation matches reality
public string? GetDisplayName(User user)
{
    return user.Profile?.DisplayName;  // correctly declared nullable
}

// GOOD -- ensure non-null if annotation says so
public string GetDisplayName(User user)
{
    return user.Profile?.DisplayName ?? user.Email;  // guaranteed non-null
}
```

---

#### EH-NULL-02: Null-Forgiving Operator `!` Hiding Real Null Path

| Field | Detail |
|-------|--------|
| **ID** | EH-NULL-02 |
| **Severity** | High |
| **Signal** | Null-forgiving operator `!` used to suppress nullable warning without a comment justifying why null is impossible at that point |
| **Risk** | Silences the compiler warning but does not prevent null. If the assumption is wrong, runtime `NullReferenceException` occurs with no compile-time warning. Essentially defeats nullable reference types |
| **Fix** | Remove `!` and fix the actual nullability issue. If the `!` is truly justified (e.g., guarantee from framework, post-deserialization), add a comment explaining the guarantee |

```csharp
// BAD -- hiding potential null
var user = _repository.FindById(id)!;  // if not found, NRE on next line
user.UpdateLastLogin();

// BAD -- suppressing warning instead of fixing root cause
string name = config["DisplayName"]!;  // key might not exist

// GOOD -- handle the null case
var user = _repository.FindById(id);
if (user is null)
    throw new NotFoundException($"User {id} not found");
user.UpdateLastLogin();

// ACCEPTABLE -- justified with comment
// Guaranteed non-null by JSON schema validation in middleware
var tenantId = httpContext.Items["TenantId"]! as string;
```

---

#### EH-NULL-03: Missing Null Check on Parameter in Public API

| Field | Detail |
|-------|--------|
| **ID** | EH-NULL-03 |
| **Severity** | Medium |
| **Signal** | Public or protected method with reference-type parameters that does not validate for null, especially when the parameter is dereferenced immediately |
| **Risk** | `NullReferenceException` with no indication of which parameter was null. In library code, callers get a confusing error from deep inside the implementation instead of a clear `ArgumentNullException` at the API boundary |
| **Fix** | Use `ArgumentNullException.ThrowIfNull()` (.NET 6+) or `?? throw new ArgumentNullException()` at the start of public methods |

```csharp
// BAD -- no null check, NRE deep in implementation
public void SendEmail(EmailMessage message)
{
    var recipient = message.To.First();  // NRE if message is null
    // caller sees "Object reference not set" with no param name
}

// GOOD -- guard clause (.NET 6+)
public void SendEmail(EmailMessage message)
{
    ArgumentNullException.ThrowIfNull(message);
    var recipient = message.To.First();
}

// GOOD -- pre-.NET 6 style
public void SendEmail(EmailMessage message)
{
    if (message is null)
        throw new ArgumentNullException(nameof(message));
    var recipient = message.To.First();
}
```

---

#### EH-NULL-04: Dereferencing After `as` Cast Without Null Check

| Field | Detail |
|-------|--------|
| **ID** | EH-NULL-04 |
| **Severity** | High |
| **Signal** | `var x = obj as SomeType;` followed by `x.Member` without null check. The `as` operator returns `null` if the cast fails (unlike direct cast which throws) |
| **Risk** | `NullReferenceException` when the cast fails. The developer likely intended a direct cast `(SomeType)obj` which throws `InvalidCastException` with a clear message, or forgot to check for null |
| **Fix** | Use pattern matching `if (obj is SomeType x)` or check for null after `as`. If the cast should never fail, use direct cast for a clear exception |

```csharp
// BAD -- as cast without null check
var handler = service as IMessageHandler;
handler.Handle(message);  // NRE if service is not IMessageHandler

// GOOD -- pattern matching
if (service is IMessageHandler handler)
{
    handler.Handle(message);
}
else
{
    throw new InvalidOperationException($"Service {service.GetType()} does not support message handling");
}

// GOOD -- direct cast when failure is unexpected
var handler = (IMessageHandler)service;  // InvalidCastException with clear message
handler.Handle(message);
```

---

#### EH-NULL-05: `if (x != null) x.Method()` -- Use `?.` or Check in Concurrent Context

| Field | Detail |
|-------|--------|
| **ID** | EH-NULL-05 |
| **Severity** | Medium |
| **Signal** | `if (x != null) x.Method()` pattern where `x` is a field or property that could be set to null between the check and the use (TOCTOU race in multi-threaded code) |
| **Risk** | In concurrent code, another thread can set `x` to null after the check but before the use, causing `NullReferenceException`. In single-threaded code, this is a style issue but not a bug |
| **Fix** | For fields in concurrent code, capture in a local: `var local = x; if (local != null) local.Method()`. Or use null-conditional: `x?.Method()` (thread-safe because it reads the field once). For events, use `Volatile.Read` + `?.Invoke` |

```csharp
// BAD -- TOCTOU race on field
class Publisher
{
    public event EventHandler? StatusChanged;

    void OnStatusChanged()
    {
        if (StatusChanged != null)        // Thread B sets to null here
            StatusChanged(this, EventArgs.Empty);  // NRE!
    }
}

// GOOD -- null-conditional (reads field once, thread-safe)
class Publisher
{
    public event EventHandler? StatusChanged;

    void OnStatusChanged()
    {
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}

// GOOD -- local variable capture for general fields
var handler = _handler;  // single read
if (handler != null)
    handler.Process();
```

---

#### EH-NULL-06: `[NotNull]`/`[MaybeNull]` Attribute Misuse or Contradiction

| Field | Detail |
|-------|--------|
| **ID** | EH-NULL-06 |
| **Severity** | High |
| **Signal** | `[NotNull]` attribute on a parameter or return that can actually be null, or `[MaybeNull]` on something that is always non-null. Contradictory attributes (e.g., `[NotNull, MaybeNull]`). Wrong attribute used (e.g., `[NotNullWhen]` with wrong boolean sense) |
| **Risk** | Incorrect nullable flow analysis. Callers trust the attributes and skip null checks, leading to `NullReferenceException`. Particularly dangerous because developers deliberately added attributes to communicate null contracts |
| **Fix** | Verify each attribute matches the actual runtime behavior. Test edge cases. Use `[NotNullWhen(true)]` for Try-patterns that guarantee non-null on success |

```csharp
// BAD -- [NotNull] on parameter that allows null on certain paths
public void Process([NotNull] string? input)
{
    if (string.IsNullOrEmpty(input))
        return;  // input could still be null after this method!
    // [NotNull] tells callers "after this method, input is non-null"
    // but early return does not guarantee that
}

// GOOD -- correct attribute usage
public bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
{
    if (_cache.TryGetValue(key, out value))
        return true;  // value is guaranteed non-null when returning true
    value = null;
    return false;  // value is null when returning false
}
```

---

#### EH-NULL-07: Null Check on Non-Nullable Parameter (Redundant Noise)

| Field | Detail |
|-------|--------|
| **ID** | EH-NULL-07 |
| **Severity** | Low |
| **Signal** | `ArgumentNullException.ThrowIfNull(param)` or `if (param is null) throw` on a parameter declared as non-nullable `T` (not `T?`) in a nullable-enabled context. Or: null check on a `struct` parameter |
| **Risk** | Code noise. Gives false impression that null is a realistic scenario. In nullable-enabled projects, the compiler already prevents null from being passed. However, this is acceptable for public library APIs consumed by nullable-disabled callers |
| **Fix** | In internal code with nullable enabled everywhere, remove redundant checks. In public library APIs, keep the checks as a defense-in-depth measure (callers may have nullable disabled) |

```csharp
// QUESTIONABLE -- redundant in nullable-enabled internal code
#nullable enable
internal void ProcessItem(Item item)  // Item is non-nullable
{
    ArgumentNullException.ThrowIfNull(item);  // compiler already prevents null
    // ...
}

// ACCEPTABLE -- public API, callers may not have nullable enabled
#nullable enable
public void ProcessItem(Item item)
{
    ArgumentNullException.ThrowIfNull(item);  // defense-in-depth for external callers
    // ...
}
```

---

#### EH-NULL-08: `default!` Suppression in Nullable-Enabled Context Without Justification

| Field | Detail |
|-------|--------|
| **ID** | EH-NULL-08 |
| **Severity** | Medium |
| **Signal** | `= default!` or `= null!` used to initialize non-nullable fields or properties, typically in constructors or DTOs |
| **Risk** | Creates a "lie" in the type system -- the field is declared non-nullable but actually holds null until properly initialized. Any access before initialization causes `NullReferenceException` with no compiler warning |
| **Fix** | Use `required` keyword (C# 11+) to ensure initialization. Use constructor parameters to guarantee initialization. If `default!` is needed for serialization, use `[JsonConstructor]` or `init` properties with `required` |

```csharp
// BAD -- default! creates hidden null
public class UserDto
{
    public string Name { get; set; } = default!;  // actually null!
    public string Email { get; set; } = default!;
}

var user = new UserDto();  // Name and Email are null despite non-nullable declaration
Console.WriteLine(user.Name.Length);  // NRE!

// GOOD -- required properties (C# 11+)
public class UserDto
{
    public required string Name { get; init; }
    public required string Email { get; init; }
}

var user = new UserDto { Name = "Alice", Email = "alice@example.com" };
// compiler enforces Name and Email are set

// GOOD -- constructor initialization
public class UserDto
{
    public string Name { get; }
    public string Email { get; }

    public UserDto(string name, string email)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Email = email ?? throw new ArgumentNullException(nameof(email));
    }
}
```

---

### Validation & Contracts

#### EH-VAL-01: Missing Argument Validation at Public API Boundary

| Field | Detail |
|-------|--------|
| **ID** | EH-VAL-01 |
| **Severity** | Medium |
| **Signal** | Public method with parameters that have implicit constraints (must be positive, must be non-empty, must be valid email) but no validation at entry |
| **Risk** | Invalid data propagates deep into the system before failing. Error messages are cryptic and point to internal implementation details instead of the invalid argument. Makes debugging harder for API consumers |
| **Fix** | Validate all parameters at the entry point of public APIs. Use guard clauses. Throw `ArgumentException`, `ArgumentNullException`, or `ArgumentOutOfRangeException` as appropriate |

```csharp
// BAD -- no validation, fails deep inside
public void TransferFunds(string fromAccount, string toAccount, decimal amount)
{
    var from = _repository.GetAccount(fromAccount);  // fails here with generic error
    from.Debit(amount);  // or fails here with "balance too low" when amount is -100
}

// GOOD -- guard clauses validate at entry
public void TransferFunds(string fromAccount, string toAccount, decimal amount)
{
    ArgumentException.ThrowIfNullOrEmpty(fromAccount);
    ArgumentException.ThrowIfNullOrEmpty(toAccount);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);

    if (fromAccount == toAccount)
        throw new ArgumentException("Source and destination accounts must differ", nameof(toAccount));

    var from = _repository.GetAccount(fromAccount);
    from.Debit(amount);
}
```

---

#### EH-VAL-02: Validation After Use (Check-Then-Use Ordering Wrong)

| Field | Detail |
|-------|--------|
| **ID** | EH-VAL-02 |
| **Severity** | High |
| **Signal** | Parameter is used (dereferenced, passed to another method, stored) before its validation check appears in the code |
| **Risk** | The validation never prevents the invalid use -- the damage is done before the check. If the use throws, the error message is from the use site, not the validation, giving poor diagnostics. In some cases, side effects from the invalid use cannot be rolled back |
| **Fix** | Move all validation to the top of the method, before any use of the parameters |

```csharp
// BAD -- email used before validation
public void CreateUser(string email, string name)
{
    _audit.Log($"Creating user {email}");  // email used (logged) before validation
    _repository.Reserve(email);            // side effect before validation!

    if (string.IsNullOrEmpty(email))       // too late -- already used above
        throw new ArgumentException("Email required", nameof(email));
}

// GOOD -- validate first
public void CreateUser(string email, string name)
{
    ArgumentException.ThrowIfNullOrEmpty(email);
    ArgumentException.ThrowIfNullOrEmpty(name);

    _audit.Log($"Creating user {email}");
    _repository.Reserve(email);
}
```

---

#### EH-VAL-03: ArgumentException Without `paramName`

| Field | Detail |
|-------|--------|
| **ID** | EH-VAL-03 |
| **Severity** | Low |
| **Signal** | `throw new ArgumentException("message")` without the `paramName` parameter. Also: `new ArgumentNullException("message that is not a param name")` (constructor overload confusion) |
| **Risk** | Poor diagnostics. When the exception is caught in logs or telemetry, there is no indication of which parameter was invalid. In APIs with many parameters, this makes debugging significantly harder |
| **Fix** | Always include `nameof(parameterName)` as the `paramName` argument. Be careful with `ArgumentNullException` constructor order: `(string paramName)` vs `(string paramName, string message)` |

```csharp
// BAD -- no paramName
throw new ArgumentException("Value must be positive");
// Which parameter? Impossible to tell from the exception.

// BAD -- constructor confusion (message passed as paramName)
throw new ArgumentNullException("User email cannot be null");
// paramName becomes "User email cannot be null" -- nonsensical

// GOOD -- paramName included
throw new ArgumentException("Value must be positive", nameof(amount));

// GOOD -- correct constructor usage
throw new ArgumentNullException(nameof(email), "User email cannot be null");

// BEST (.NET 8+) -- static helper
ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
// automatically includes param name via CallerArgumentExpression
```

---

#### EH-VAL-04: Inconsistent Validation Between Overloads

| Field | Detail |
|-------|--------|
| **ID** | EH-VAL-04 |
| **Severity** | Medium |
| **Signal** | Method overloads where one validates a parameter but another does not. Or: overloads that apply different validation rules to the same logical parameter |
| **Risk** | Callers get different behavior depending on which overload they call. Some callers get clear validation errors, others get cryptic internal exceptions. Violates the principle of least surprise |
| **Fix** | Route all overloads through a single validation method or have all overloads call the most-parameterized overload that contains all validation |

```csharp
// BAD -- only one overload validates
public void Save(string path)
{
    ArgumentException.ThrowIfNullOrEmpty(path);
    Save(path, Encoding.UTF8);  // validated
}

public void Save(string path, Encoding encoding)
{
    // no validation! Caller using this overload gets NRE on path.Length
    using var writer = new StreamWriter(path, false, encoding);
    writer.Write(_content);
}

// GOOD -- most-parameterized overload validates, others delegate
public void Save(string path) => Save(path, Encoding.UTF8);

public void Save(string path, Encoding encoding)
{
    ArgumentException.ThrowIfNullOrEmpty(path);
    ArgumentNullException.ThrowIfNull(encoding);

    using var writer = new StreamWriter(path, false, encoding);
    writer.Write(_content);
}
```

---

#### EH-VAL-05: Guard Clause Throwing Wrong Exception Type

| Field | Detail |
|-------|--------|
| **ID** | EH-VAL-05 |
| **Severity** | Medium |
| **Signal** | `throw new ArgumentException` when `ArgumentNullException` is appropriate (null parameter), or `throw new InvalidOperationException` for a parameter validation (should be `ArgumentException`), or `throw new ArgumentNullException` for a non-null but invalid value |
| **Risk** | Misleading exception types confuse callers and automated error handling. `ArgumentNullException` is commonly caught separately from `ArgumentException`. Using the wrong type breaks expected catch hierarchies |
| **Fix** | Use `ArgumentNullException` for null parameters, `ArgumentOutOfRangeException` for out-of-range values, `ArgumentException` for other invalid arguments, and `InvalidOperationException` for invalid object state (not parameter issues) |

```csharp
// BAD -- wrong exception types
public void SetTimeout(TimeSpan? timeout)
{
    if (timeout == null)
        throw new ArgumentException("Timeout required");  // should be ArgumentNullException

    if (timeout.Value.TotalSeconds < 0)
        throw new ArgumentNullException(nameof(timeout));  // should be ArgumentOutOfRangeException

    if (_isRunning)
        throw new ArgumentException("Cannot change timeout while running");  // should be InvalidOperationException
}

// GOOD -- correct exception hierarchy
public void SetTimeout(TimeSpan? timeout)
{
    ArgumentNullException.ThrowIfNull(timeout);
    ArgumentOutOfRangeException.ThrowIfLessThan(timeout.Value.TotalSeconds, 0, nameof(timeout));

    if (_isRunning)
        throw new InvalidOperationException("Cannot change timeout while running");

    _timeout = timeout.Value;
}
```

---

#### EH-VAL-06: `Debug.Assert` Used Instead of Proper Validation in Production Code

| Field | Detail |
|-------|--------|
| **ID** | EH-VAL-06 |
| **Severity** | Medium |
| **Signal** | `Debug.Assert(condition)` used to validate public API parameters or externally-sourced data. `Debug.Assert` is stripped in Release builds |
| **Risk** | Validation disappears in production. Invalid data passes through unchecked in Release builds, causing failures deeper in the code with cryptic errors. Gives false confidence during development testing |
| **Fix** | Use `Debug.Assert` only for internal invariants (conditions that should be impossible if the code is correct). Use `ArgumentException` / guard clauses for parameter validation. Use runtime checks for external data |

```csharp
// BAD -- Debug.Assert stripped in Release build
public void ProcessOrder(Order order)
{
    Debug.Assert(order != null);         // gone in Release!
    Debug.Assert(order.Items.Count > 0); // gone in Release!
    SubmitOrder(order);  // NRE in production
}

// GOOD -- proper runtime validation
public void ProcessOrder(Order order)
{
    ArgumentNullException.ThrowIfNull(order);
    if (order.Items.Count == 0)
        throw new ArgumentException("Order must have at least one item", nameof(order));
    SubmitOrder(order);
}

// ACCEPTABLE -- Debug.Assert for internal invariants
private void ProcessInternal(Order order)
{
    Debug.Assert(_isInitialized, "ProcessInternal called before Initialize");
    // _isInitialized is controlled by our code, not external input
    // This is a programming error if false, not a validation issue
}
```

---

#### EH-VAL-07: Missing Range Validation on Numeric Parameters

| Field | Detail |
|-------|--------|
| **ID** | EH-VAL-07 |
| **Severity** | Medium |
| **Signal** | Numeric parameter (int, double, decimal) used for array index, count, timeout, or other value with implicit range constraints, without range validation |
| **Risk** | Negative values cause `ArgumentOutOfRangeException` deep in .NET internals. Extremely large values cause `OutOfMemoryException` on allocation. Zero values cause `DivideByZeroException`. All produce poor diagnostics pointing to internal code |
| **Fix** | Use `ArgumentOutOfRangeException.ThrowIfNegative`, `ThrowIfZero`, `ThrowIfGreaterThan`, etc. (.NET 8+). Or manual range checks with `ArgumentOutOfRangeException` |

```csharp
// BAD -- no range check
public byte[] CreateBuffer(int size)
{
    return new byte[size];  // size=-1: OverflowException; size=int.MaxValue: OOM
}

// GOOD -- range validation
public byte[] CreateBuffer(int size)
{
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
    ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaxBufferSize);
    return new byte[size];
}

// GOOD -- pre-.NET 8 style
public byte[] CreateBuffer(int size)
{
    if (size <= 0)
        throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be positive");
    if (size > MaxBufferSize)
        throw new ArgumentOutOfRangeException(nameof(size), size, $"Size must not exceed {MaxBufferSize}");
    return new byte[size];
}
```

---

#### EH-VAL-08: `Enum.IsDefined` Missing for Enum Parameters from External Input

| Field | Detail |
|-------|--------|
| **ID** | EH-VAL-08 |
| **Severity** | Medium |
| **Signal** | Enum parameter sourced from external input (HTTP request, configuration, deserialization, user input) used in a `switch` statement without validation that the value is a defined enum member |
| **Risk** | Undefined enum values bypass all switch cases and hit the default (or no default), causing silent incorrect behavior. In [Flags] enums, any integer is valid, making this especially dangerous |
| **Fix** | Validate with `Enum.IsDefined` for non-flags enums. For `[Flags]` enums, validate against known combinations. Add a `default` case to switch statements that throws `ArgumentOutOfRangeException` |

```csharp
// BAD -- external input used as enum without validation
public void SetPriority(int priorityValue)  // from HTTP request
{
    var priority = (Priority)priorityValue;
    switch (priority)
    {
        case Priority.Low: /* ... */ break;
        case Priority.Medium: /* ... */ break;
        case Priority.High: /* ... */ break;
        // priorityValue=999 falls through silently!
    }
}

// GOOD -- validated
public void SetPriority(int priorityValue)
{
    if (!Enum.IsDefined<Priority>((Priority)priorityValue))
        throw new ArgumentOutOfRangeException(nameof(priorityValue),
            priorityValue, $"Not a valid {nameof(Priority)} value");

    var priority = (Priority)priorityValue;
    switch (priority)
    {
        case Priority.Low: /* ... */ break;
        case Priority.Medium: /* ... */ break;
        case Priority.High: /* ... */ break;
        default:
            throw new ArgumentOutOfRangeException(nameof(priorityValue),
                priority, "Unhandled priority value");
    }
}
```

---

## Search Priority

Rank patterns by real-world impact when time is limited.

| Priority | Category | Patterns | Rationale |
|----------|----------|----------|-----------|
| 1 | **Exception swallowing** | EH-EXC-01, EH-EXC-02 | Bugs that vanish from production telemetry are the hardest to diagnose |
| 2 | **async void** | EH-EXC-10 | Unobserved exceptions crash production processes |
| 3 | **Static constructor exceptions** | EH-EXC-05 | Permanently breaks types -- requires app restart |
| 4 | **Throw from finalizer/Dispose** | EH-EXC-04 | Crashes finalizer thread or loses original exception |
| 5 | **Null safety violations** | EH-NULL-01, EH-NULL-02, EH-NULL-04 | NullReferenceException is the most common .NET runtime error |
| 6 | **Stack trace loss** | EH-EXC-03, EH-EXC-07 | Slows incident response by hiding root cause |
| 7 | **Missing validation** | EH-VAL-01, EH-VAL-02 | Invalid data propagates, causing cascading failures |
| 8 | **Cancellation mishandling** | EH-EXC-06 | Retry storms and log noise from normal cancellation |
| 9 | **Exception for control flow** | EH-EXC-08 | Performance degradation in hot paths |
| 10 | **Null-forgiving / default!** | EH-NULL-02, EH-NULL-08 | Defeats nullable reference type safety net |

---

## Fix Strategy Decision Trees

### Decision Tree 1: Exception Handling Fix

```
What kind of exception handling problem?
├── Catching too broadly (Exception base class)
│   ├── Is this a top-level handler (Main, middleware, background service)?
│   │   ├── YES → Keep catch(Exception), but always log AND rethrow/propagate
│   │   └── NO → Catch specific exception types only
│   └── Does the catch body do nothing (empty)?
│       ├── YES → Add logging. If truly best-effort, log at Debug level
│       └── NO → Review: does it rethrow? Does it wrap correctly?
├── Stack trace lost (throw ex)
│   └── Replace `throw ex;` with `throw;`
│       └── Need to wrap? → `throw new WrapperException("msg", ex)`
├── async void
│   ├── Is it an event handler?
│   │   ├── YES → Keep async void, wrap body in try/catch
│   │   └── NO → Change to async Task, let caller await
│   └── Is it a fire-and-forget?
│       └── Use Task.Run + error handling, or IHostedService
├── Throw from Dispose/Finalizer
│   ├── Finalizer → Wrap in try/catch, never throw
│   └── Dispose → Catch and log cleanup failures, do not propagate
└── Static constructor exception
    └── Move fallible code to Lazy<T> or static method with error handling
```

### Decision Tree 2: Null Safety Fix

```
What kind of null safety problem?
├── Annotation mismatch (method returns null but declared non-nullable)
│   ├── Can the null be eliminated? → Add null-coalescing or validation
│   └── Null is valid → Change return type to T?
├── Null-forgiving operator (!) hiding real null
│   ├── Is null actually impossible here?
│   │   ├── YES → Add comment explaining the guarantee
│   │   └── NO → Remove !, handle the null case properly
│   └── Is this default! on a property?
│       └── Use required keyword (C# 11+) or constructor initialization
├── Missing null check on public parameter
│   └── Add ArgumentNullException.ThrowIfNull at method entry
├── as-cast without null check
│   └── Use pattern matching: if (x is Type t) { use t; }
└── TOCTOU race on nullable field
    └── Use null-conditional (?.) or capture to local variable
```

### Decision Tree 3: Validation Fix

```
What kind of validation problem?
├── Missing validation at API boundary
│   └── Add guard clauses at top of method, before any use of parameters
│       ├── Null → ArgumentNullException.ThrowIfNull
│       ├── Empty string → ArgumentException.ThrowIfNullOrEmpty
│       ├── Out of range → ArgumentOutOfRangeException.ThrowIfNegative/etc
│       └── Invalid enum → Enum.IsDefined check
├── Validation after use
│   └── Move ALL validation before ALL usage
├── Wrong exception type
│   ├── Null parameter → ArgumentNullException
│   ├── Out of range → ArgumentOutOfRangeException
│   ├── Other bad argument → ArgumentException
│   └── Invalid object state → InvalidOperationException
├── Missing paramName
│   └── Add nameof(parameter) to exception constructor
├── Inconsistent between overloads
│   └── Route all overloads through most-parameterized version
└── Debug.Assert for external validation
    └── Replace with runtime guard clause (Debug.Assert only for internal invariants)
```

---

## Analyzer Coverage

Enable these analyzers to catch patterns at compile time:

| Analyzer Rule | Pattern(s) Covered | Severity |
|---------------|-------------------|----------|
| CS8600-CS8605 | EH-NULL-01 (Nullable reference type warnings) | Warning/Error |
| CS8618 | EH-NULL-08 (Non-nullable field not initialized) | Warning |
| CS8625 | EH-NULL-01 (Null literal to non-nullable) | Warning |
| CA1031 | EH-EXC-01 (Do not catch general exception types) | Warning |
| CA2200 | EH-EXC-03 (Rethrow to preserve stack details) | Warning |
| CA1065 | EH-EXC-04 (Do not raise exceptions in unexpected locations) | Warning |
| CA2007 | EH-EXC-10 (Consider calling ConfigureAwait) | Info |
| CA1062 | EH-NULL-03, EH-VAL-01 (Validate arguments of public methods) | Warning |
| CA2208 | EH-VAL-03 (Instantiate argument exceptions correctly) | Warning |
| CA1069 | EH-VAL-08 (Enums values should not be duplicated) | Warning |
| VSTHRD100 | EH-EXC-10 (Avoid async void methods) | Warning |
| VSTHRD002 | EH-EXC-12 (Avoid problematic synchronous waits) | Warning |

---

## References

1. .NET Framework Design Guidelines -- Exception Throwing (3rd edition)
2. MSDN Magazine -- "Async/Await Best Practices" (https://learn.microsoft.com/en-us/archive/msdn-magazine/2013/march/async-await-best-practices-in-asynchronous-programming)
3. Microsoft Learn -- "Nullable reference types" (https://learn.microsoft.com/en-us/dotnet/csharp/nullable-references)
4. Microsoft Learn -- "Exception handling best practices" (https://learn.microsoft.com/en-us/dotnet/standard/exceptions/best-practices-for-exceptions)
5. Microsoft Learn -- "Nullable analysis attributes" (https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/nullable-analysis)
6. .NET API Analyzers -- CA1031, CA2200, CA1062, CA2208 series (https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/)
7. .NET Source Generators -- Interceptors and CallerArgumentExpression for validation helpers
