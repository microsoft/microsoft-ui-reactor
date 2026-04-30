---
name: cs-api-design-review
description: >-
  Review C# / .NET code for API design and type system issues: mutable structs
  violating value semantics, large structs passed by value, missing sealed on
  non-inheritable classes, tuple returns on public APIs, string parameters where
  strong types belong, boolean parameters on public methods, missing nullable
  annotations, captive dependency (scoped in singleton), disposable transient
  services, breaking changes from added constructor parameters, enum serialization
  breakage, and premature abstraction via single-implementation interfaces.
  30 patterns across 4 categories sourced from .NET Framework Design Guidelines,
  .NET API review guidance, ASP.NET Core patterns, and C# language design notes.
  Use this skill when reviewing C# public APIs, library design, NuGet packages,
  type hierarchies, nullable annotations, DI registration, or versioning strategy.
version: "1.0.0"
owner: "Agentic Engineering System"
---

# C# API Design Code Review

## Quick Detection

**Primary Symptoms (in code under review)**:
- `struct` with mutable fields or property setters
- `struct` larger than 16 bytes passed without `in`/`ref`/`ref readonly`
- `public class` without `sealed` that has no virtual members or documented extension points
- Public method returning `(string, int, bool)` or similar unnamed tuple
- Public method with `bool` parameter controlling behavior (flag argument)
- Public API missing nullable reference type annotations in nullable-enabled project
- Constructor taking more than 5 injected dependencies
- `services.AddSingleton<T>()` where `T` depends on a scoped service
- Enum with new values inserted in the middle of existing members
- Public constructor gaining a new parameter (binary breaking change)

**Key Code Patterns to Search For**:
```csharp
// Mutable struct (violates value semantics)
public struct Point
{
    public int X { get; set; }  // BAD: mutable value type
    public int Y { get; set; }
}

// Tuple return on public API
public (string, int, bool) GetUserInfo(int id) { ... }  // BAD: unnamed positional args

// Boolean parameter
public void SendMessage(string text, bool urgent) { ... }  // BAD: bool flag

// Missing sealed
public class EmailValidator { ... }  // No virtual members, not designed for inheritance

// Captive dependency
services.AddSingleton<MySingleton>();
services.AddScoped<MyScopedDep>();
// MySingleton depends on MyScopedDep -- scoped service captured in singleton!

// Nullable annotation gap
public string GetName(int id) { ... }  // Returns null sometimes but not annotated
```

## Scope Boundaries

This skill focuses on **API shape, type design, and binary compatibility** -- problems that affect consumers of the API, cause runtime failures, or make breaking changes unavoidable. It does NOT flag:

- **Naming preferences** unless the name creates a demonstrable confusion risk (e.g., `Delete()` that actually archives). A name that is merely imprecise but unambiguous in context is not a finding.
- **Documentation style** unless a public API has zero doc-comments on a safety-critical or non-obvious member.
- **Internal implementation details** unless they leak through the public surface. Private code organization is team preference.
- **Formatting or style** (brace placement, expression-bodied members, etc.). These are team conventions, not API design issues.
- **Performance micro-optimizations** unless they affect the API contract (e.g., returning `Span<T>` vs `T[]` changes the API).

**When in doubt**: Would a consumer of this API be negatively affected (breaking change, runtime failure, confusing usage, misused lifetime)? If yes, it is a finding. If no, it is likely a preference.

## Analysis Workflow

### Step 1: Review Type Design

Check that types follow .NET design guidelines and C# type system conventions.

1. **Value type correctness**: Structs should be immutable, small, and represent values:
   ```csharp
   // BAD: Mutable struct -- copy semantics cause silent bugs
   public struct Config
   {
       public string ConnectionString { get; set; }  // Mutation on copy is lost
       public int Timeout { get; set; }
   }
   var c = GetConfig();
   c.Timeout = 30;  // Mutates local copy, not the original

   // GOOD: Immutable struct
   public readonly struct Config
   {
       public string ConnectionString { get; init; }
       public int Timeout { get; init; }
   }
   ```

2. **Struct size**: Structs larger than ~16 bytes should be passed by `in`/`ref` or reconsidered as classes.

3. **Sealed by default**: Classes without virtual members and not designed for inheritance should be `sealed` (performance benefit from devirtualization, clearer intent).

4. **Record vs record struct**: Use `record struct` for small, stack-allocated value records. Use `record` (class) when reference semantics, inheritance, or large size is needed.

### Step 2: Validate Public API Surface

Check method signatures, return types, and parameter design.

1. **Tuple returns**: Named tuples are acceptable internally, but public APIs should use dedicated types:
   ```csharp
   // BAD: Caller sees GetResult().Item1, GetResult().Item2
   public (string, int) GetResult() { ... }

   // ACCEPTABLE: Named tuple for internal use
   internal (string Name, int Count) GetResult() { ... }

   // GOOD: Dedicated type for public API
   public UserResult GetResult() { ... }
   public record UserResult(string Name, int Count);
   ```

2. **Boolean parameters**: Replace with enum or method overloads for readability at call sites:
   ```csharp
   // BAD: What does 'true' mean at the call site?
   SendMessage("Hello", true);

   // GOOD: Enum makes call site self-documenting
   SendMessage("Hello", Priority.Urgent);

   // GOOD: Overloads for simple cases
   SendMessage("Hello");
   SendUrgentMessage("Hello");
   ```

3. **Strong typing**: Use enums, value objects, or dedicated types instead of primitive strings/ints for domain concepts.

### Step 3: Check Nullable Annotations and Contracts

Review nullable reference type annotations for completeness and correctness.

1. **Public API annotations**: Every public/protected member in a nullable-enabled project should have correct annotations.
2. **Try pattern**: `TryGet*` methods need `[NotNullWhen(true)]` or `[MaybeNullWhen(false)]`.
3. **Generic nullability**: `T?` on unconstrained generic is ambiguous between `Nullable<T>` (value) and nullable reference.

### Step 4: Review Dependency Injection and Composition

Check service lifetimes, dependency counts, and composition patterns.

1. **Captive dependency**: A singleton holding a scoped service keeps the scoped service alive forever, breaking its intended lifetime.
2. **Constructor dependencies**: More than 5 injected dependencies signals SRP violation.
3. **Service locator**: Injecting `IServiceProvider` and resolving services at runtime hides dependencies and makes testing harder.

### Step 5: Evaluate Versioning and Compatibility

Check for binary and source breaking changes.

1. **Added constructor parameters**: Adding a parameter to a public constructor is a binary breaking change.
2. **Enum member ordering**: Inserting values in the middle of an enum changes integer values of subsequent members.
3. **Obsolete before removal**: Mark APIs with `[Obsolete]` for at least one release before removing them.

## Pattern Catalog

### Type Design Patterns

#### API-TYPE-01: Mutable Struct (Value Type Semantics Violated)
**Severity**: High
**Source**: .NET Framework Design Guidelines

**Signal**: `struct` with mutable fields or property setters. Mutations on copies are silently lost because structs have value semantics (assignment copies the entire value).

```csharp
// BAD: Mutable struct -- mutations are silently lost on copies
public struct Rectangle
{
    public double Width { get; set; }
    public double Height { get; set; }

    public void Scale(double factor)
    {
        Width *= factor;   // If 'this' is a copy, mutation is lost
        Height *= factor;
    }
}

// This common pattern silently does nothing useful:
Rectangle r = GetRectangle();
r.Scale(2.0);  // Scales the local copy, not the original

// BAD: List<Rectangle>[i].Scale(2.0) won't compile -- indexer returns copy

// GOOD: Immutable struct with 'readonly' modifier
public readonly struct Rectangle
{
    public double Width { get; init; }
    public double Height { get; init; }

    public Rectangle Scale(double factor) =>
        new Rectangle { Width = Width * factor, Height = Height * factor };
}

// GOOD: record struct (immutable by convention, with-expression support)
public readonly record struct Rectangle(double Width, double Height)
{
    public Rectangle Scale(double factor) => this with
    {
        Width = Width * factor,
        Height = Height * factor
    };
}
```

**Fix**: Make structs `readonly`. Use `init` properties or constructors for initialization. Return new instances from transformation methods. Consider `readonly record struct` for value-semantic types with equality.

---

#### API-TYPE-02: Large Struct Passed by Value
**Severity**: Medium
**Source**: .NET Framework Design Guidelines, performance guidance

**Signal**: Struct larger than approximately 16 bytes passed by value (not by `in`, `ref`, or `ref readonly`). Each pass-by-value copies the entire struct.

```csharp
// BAD: Large struct copied on every call
public struct Matrix4x4  // 64 bytes (16 floats)
{
    public float M11, M12, M13, M14;
    public float M21, M22, M23, M24;
    public float M31, M32, M33, M34;
    public float M41, M42, M43, M44;
}

public Matrix4x4 Multiply(Matrix4x4 a, Matrix4x4 b)  // 128 bytes copied in
{
    // ...
}

// GOOD: Pass by 'in' to avoid copy
public Matrix4x4 Multiply(in Matrix4x4 a, in Matrix4x4 b)
{
    // a and b are read-only references -- no copy
}

// GOOD: Use ref readonly for return values in performance-critical paths
public ref readonly Matrix4x4 GetTransform() => ref _transform;

// ALTERNATIVE: If struct is large and needs mutability, consider using a class
public class Matrix4x4 { ... }
```

**Fix**: Pass large structs by `in` (read-only reference). Ensure the struct is `readonly` to avoid defensive copies when passed by `in`. For very large data, consider a class instead.

---

#### API-TYPE-03: Missing `sealed` on Non-Inheritable Class
**Severity**: Medium
**Source**: .NET Framework Design Guidelines

**Signal**: Public class without `sealed` that has no `virtual`/`abstract` members and no documented inheritance design. Unsealed classes implicitly promise extensibility that may not be intended.

```csharp
// BAD: Unsealed class with no virtual members
public class EmailValidator
{
    public bool IsValid(string email) => email.Contains('@');
}
// Consumers might subclass this, creating a coupling the author didn't intend

// GOOD: Sealed -- clear intent, enables devirtualization
public sealed class EmailValidator
{
    public bool IsValid(string email) => email.Contains('@');
}

// GOOD: Explicitly designed for inheritance
public abstract class ValidatorBase
{
    public bool Validate(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        return ValidateCore(input);
    }
    protected abstract bool ValidateCore(string input);
}
```

**Fix**: Add `sealed` to classes not designed for inheritance. If the class is designed for extension, make that explicit with `virtual`/`abstract` members and document the extension points. Sealing also gives the JIT a devirtualization opportunity.

---

#### API-TYPE-04: `record` vs `record struct` Mismatch
**Severity**: Low
**Source**: C# language design

**Signal**: `record` (class) used for small, value-semantic types that would benefit from stack allocation, or `record struct` used for large types that need reference semantics.

```csharp
// SUBOPTIMAL: record class for a small coordinate (heap allocation per instance)
public record Coordinate(double Latitude, double Longitude);
// Every instance is heap-allocated; GC pressure in hot paths

// BETTER: record struct for small, value-semantic types
public readonly record struct Coordinate(double Latitude, double Longitude);
// Stack-allocated; no GC pressure; value equality built-in

// SUBOPTIMAL: record struct for large type (expensive copies)
public record struct LargePayload(
    string Name, string Description, byte[] Data, List<string> Tags,
    Dictionary<string, object> Metadata);
// Copied on every assignment; reference semantics would be better

// BETTER: record class for large or reference-heavy types
public record LargePayload(
    string Name, string Description, byte[] Data, List<string> Tags,
    Dictionary<string, object> Metadata);
```

**Fix**: Use `readonly record struct` for small (<= ~16 bytes), immutable, value-semantic types. Use `record` (class) for larger types, types that need inheritance, or types with reference-heavy fields.

---

#### API-TYPE-05: Tuple Return Type on Public API
**Severity**: Medium
**Source**: .NET Framework Design Guidelines

**Signal**: Public method returning a `ValueTuple` with positional (or weakly-named) members. Callers see `Item1`, `Item2` at the call site unless they know the local names.

```csharp
// BAD: Unnamed tuple on public API
public (string, int, bool) GetUserStatus(int userId) { ... }
// Caller: var result = GetUserStatus(42);
// result.Item1, result.Item2, result.Item3 -- meaningless

// BAD: Named tuple -- names are advisory, not enforced in metadata
public (string Name, int Age, bool IsActive) GetUserStatus(int userId) { ... }
// Names may not survive across assemblies in all tooling

// GOOD: Dedicated return type
public UserStatus GetUserStatus(int userId) { ... }

public sealed class UserStatus
{
    public required string Name { get; init; }
    public required int Age { get; init; }
    public required bool IsActive { get; init; }
}

// GOOD: Record for concise value type
public record UserStatus(string Name, int Age, bool IsActive);
```

**Fix**: Replace tuple return types on public APIs with dedicated types (records, classes, or readonly structs). Tuples are acceptable for internal/private methods where context is clear.

---

#### API-TYPE-06: String Parameter Where Strong Type Appropriate
**Severity**: Medium
**Source**: .NET Framework Design Guidelines

**Signal**: String parameters representing constrained domain values (status codes, identifiers, file types, currencies) that could be an enum or strongly-typed wrapper.

```csharp
// BAD: String for a constrained set of values
public void SetStatus(string status) { ... }
// Caller: SetStatus("actve");  // Typo compiles fine, fails at runtime

// BAD: String for domain identifier
public Order GetOrder(string orderId) { ... }
// Caller: GetOrder(customerId);  // Wrong ID type, compiles fine

// GOOD: Enum for constrained values
public void SetStatus(OrderStatus status) { ... }
public enum OrderStatus { Pending, Active, Completed, Cancelled }

// GOOD: Strongly-typed identifier
public Order GetOrder(OrderId orderId) { ... }
public readonly record struct OrderId(string Value);
```

**Fix**: Use enums for fixed sets of values. Use strongly-typed wrappers (readonly record struct) for domain identifiers. This catches errors at compile time rather than runtime.

---

#### API-TYPE-07: Boolean Parameter on Public Method
**Severity**: Medium
**Source**: .NET Framework Design Guidelines ("Flag Arguments" section)

**Signal**: Public method with a `bool` parameter that controls behavior. At the call site, the boolean value has no semantic meaning.

```csharp
// BAD: What does 'true' mean here?
fileManager.Delete("report.pdf", true);
// Is 'true' recursive? Force? MoveToTrash?

// BAD: Multiple booleans are worse
connection.Open(true, false, true);

// GOOD: Enum parameter
fileManager.Delete("report.pdf", DeleteMode.Recursive);
public enum DeleteMode { SingleFile, Recursive }

// GOOD: Overloads for simple binary choice
fileManager.Delete("report.pdf");
fileManager.DeleteRecursive("report.pdf");

// GOOD: Options object for multiple flags
connection.Open(new ConnectionOptions
{
    UsePooling = true,
    AutoReconnect = false,
    EnableLogging = true
});
```

**Fix**: Replace boolean parameters with enums that name the choice, or use method overloads. For multiple flags, use an options class or `[Flags]` enum.

---

#### API-TYPE-08: Abstract Class Where Interface Would Be More Flexible
**Severity**: Medium
**Source**: .NET Framework Design Guidelines

**Signal**: Abstract class used as a contract with no shared implementation. Since C# only allows single inheritance, an abstract class without shared behavior unnecessarily restricts consumers.

```csharp
// BAD: Abstract class with no shared implementation
public abstract class IMessageSender  // "I" prefix is a naming smell too
{
    public abstract Task SendAsync(Message message);
    public abstract Task<bool> ValidateAsync(Message message);
}
// Consumers can't inherit from anything else

// GOOD: Interface when there is no shared implementation
public interface IMessageSender
{
    Task SendAsync(Message message);
    Task<bool> ValidateAsync(Message message);
}

// GOOD: Abstract class when there IS shared implementation
public abstract class MessageSenderBase : IMessageSender
{
    public async Task SendAsync(Message message)
    {
        if (!await ValidateAsync(message))
            throw new InvalidMessageException();
        await SendCoreAsync(message);  // Template method
    }

    public virtual Task<bool> ValidateAsync(Message message) =>
        Task.FromResult(message != null);

    protected abstract Task SendCoreAsync(Message message);
}
```

**Fix**: Use interfaces for pure contracts. Use abstract classes when you need shared implementation, template method patterns, or protected state. Combine both: interface for the contract, abstract base class for shared implementation.

---

#### API-TYPE-09: Interface with Single Implementation (Premature Abstraction)
**Severity**: Low
**Source**: .NET design guidance

**Signal**: Interface with exactly one implementation, created "just in case" rather than in response to an actual need for polymorphism.

```csharp
// SUSPICIOUS: Interface with sole implementation
public interface IOrderProcessor
{
    Task ProcessAsync(Order order);
}

public class OrderProcessor : IOrderProcessor  // Only implementation
{
    public Task ProcessAsync(Order order) { ... }
}

// Result: Two files to maintain for every change, no actual polymorphism benefit

// ACCEPTABLE: Single implementation but required for DI/testing
public interface IOrderProcessor  // Needed for mocking in unit tests
{
    Task ProcessAsync(Order order);
}

// BETTER: If the interface is only for testing, consider other approaches
// Option 1: Make the class non-sealed and virtual for test overrides
public class OrderProcessor
{
    public virtual Task ProcessAsync(Order order) { ... }
}

// Option 2: Extract interface only when a second implementation is needed
// (YAGNI principle)
```

**Fix**: Do not create interfaces preemptively for classes that will only ever have one implementation. Extract an interface when you actually need polymorphism (second implementation, cross-assembly boundary, or genuine test isolation requirement). For DI testing, consider whether making the class virtual or using a test double framework is simpler.

---

#### API-TYPE-10: Nested Public Type Without Strong Containment Reason
**Severity**: Medium
**Source**: .NET Framework Design Guidelines

**Signal**: Public type nested inside another public type without a strong conceptual "belongs-to" relationship (e.g., Builder, Enumerator, EventArgs).

```csharp
// BAD: Nested type that doesn't belong to the outer type
public class OrderService
{
    public class OrderDto  // No reason this should be nested
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class ValidationResult  // Generic concept, not specific to OrderService
    {
        public bool IsValid { get; set; }
        public string[] Errors { get; set; }
    }
}
// Usage: new OrderService.OrderDto() -- awkward

// GOOD: Nesting when there is strong containment
public class LinkedList<T>
{
    public sealed class Node  // Node only makes sense in context of LinkedList
    {
        public T Value { get; }
        public Node? Next { get; internal set; }
    }
}

// GOOD: Builder pattern nested in the type it builds
public class HttpRequest
{
    public sealed class Builder
    {
        public Builder WithUrl(string url) { ... }
        public HttpRequest Build() { ... }
    }
}
```

**Fix**: Move public nested types to the containing namespace as top-level types unless they have a strong "belongs-to" relationship (Builder, Enumerator, Node, EventArgs for the outer type).

---

### Nullability & Annotation Patterns

#### API-NULL-01: Public API Missing Nullable Annotations
**Severity**: High (in nullable-enabled project)
**Source**: .NET Framework Design Guidelines

**Signal**: Public method parameters or return types without nullable annotations in a project with `<Nullable>enable</Nullable>`. This leaves consumers guessing about null contracts.

```csharp
// BAD: Nullable not annotated -- can this return null?
public User GetUser(string username)
{
    return _users.FirstOrDefault(u => u.Name == username);
    // Returns null when not found, but signature says non-null
}

// BAD: Parameter nullability unclear
public void ProcessOrder(Order order, string couponCode)
{
    // Is null couponCode allowed? Signature doesn't say
}

// GOOD: Explicit nullable annotations
public User? GetUser(string username)
{
    return _users.FirstOrDefault(u => u.Name == username);
}

public void ProcessOrder(Order order, string? couponCode)
{
    ArgumentNullException.ThrowIfNull(order);
    // couponCode is explicitly nullable -- callers know it's optional
}
```

**Fix**: Annotate all public/protected members with correct nullability. Use `?` for nullable parameters and returns. Add null guards (`ArgumentNullException.ThrowIfNull`) for non-nullable parameters.

---

#### API-NULL-02: Missing `[NotNullWhen]`/`[MaybeNullWhen]` on Try Pattern
**Severity**: High
**Source**: .NET Framework Design Guidelines

**Signal**: `Try*` methods that use `out` parameters without nullability attributes. Without these, the compiler cannot narrow the null state after the method returns.

```csharp
// BAD: Compiler doesn't know result is non-null when returning true
public bool TryGetUser(string id, out User? user)
{
    user = _users.GetValueOrDefault(id);
    return user != null;
}
// Caller: if (TryGetUser(id, out var user)) { user.Name; }  // Warning: possible null

// GOOD: Annotated with [NotNullWhen]
public bool TryGetUser(string id, [NotNullWhen(true)] out User? user)
{
    user = _users.GetValueOrDefault(id);
    return user != null;
}
// Caller: if (TryGetUser(id, out var user)) { user.Name; }  // No warning

// GOOD: With MaybeNullWhen for the inverse pattern
public bool TryRemove(string key, [MaybeNullWhen(false)] out string value)
{
    return _dict.Remove(key, out value);
}
```

**Fix**: Add `[NotNullWhen(true)]` on `out` parameters of Try-pattern methods that are non-null on success. Add `[MaybeNullWhen(false)]` when the parameter may be null on failure.

---

#### API-NULL-03: `#nullable disable` Sprinkled Through Nullable-Enabled Project
**Severity**: Medium
**Source**: .NET nullable adoption guidance

**Signal**: A project with `<Nullable>enable</Nullable>` in the csproj but individual files using `#nullable disable` to suppress warnings rather than fixing them.

```csharp
// BAD: Disabling nullable to avoid fixing warnings
#nullable disable
public class LegacyService
{
    public string GetName() => _cache.Get("name");  // May return null
    public void Process(object data) { ... }          // Null check missing
}
#nullable restore

// GOOD: Fix the nullability issues
#nullable enable
public class LegacyService
{
    public string? GetName() => _cache.Get("name");  // Honestly nullable

    public void Process(object data)
    {
        ArgumentNullException.ThrowIfNull(data);
        // ...
    }
}
```

**Fix**: Address nullable warnings rather than suppressing them. If a large file needs gradual migration, use `#nullable enable warnings` as an intermediate step to see warnings without making annotations required.

---

#### API-NULL-04: Ambiguous `T?` on Unconstrained Generic
**Severity**: High
**Source**: C# language design

**Signal**: `T?` used on an unconstrained generic type parameter. For value types, `T?` means `Nullable<T>`; for reference types, it means nullable reference. The behavior differs depending on the type argument.

```csharp
// BAD: Ambiguous T? -- different behavior for value vs reference types
public class Cache<T>
{
    public T? Get(string key)  // If T is int, returns Nullable<int>
    {                          // If T is string, returns string? (nullable ref)
        // These have different representations at runtime!
    }
}

// GOOD: Constrain the generic to clarify intent
public class Cache<T> where T : class
{
    public T? Get(string key) { ... }  // Clearly a nullable reference
}

public class Cache<T> where T : struct
{
    public T? Get(string key) { ... }  // Clearly Nullable<T>
}

// GOOD: Use default and [return: MaybeNull] for unconstrained case
public class Cache<T>
{
    [return: MaybeNull]
    public T Get(string key)
    {
        return _dict.TryGetValue(key, out var value) ? value : default;
    }
}
```

**Fix**: Add `where T : class` or `where T : struct` constraints when using `T?`. For unconstrained generics, use `[MaybeNull]` / `[AllowNull]` attributes or the `default` pattern.

---

#### API-NULL-05: Output Parameter Nullable Annotation Inconsistent with Behavior
**Severity**: High
**Source**: .NET nullable design guidelines

**Signal**: `out` parameter annotated as non-null but assigned `null` on some code paths, or annotated as nullable but never null when the method succeeds.

```csharp
// BAD: Annotated as non-null but can be null
public bool TryParse(string input, out MyType result)
{
    if (!IsValid(input))
    {
        result = null;  // CS8625 warning, but worse: caller trusts non-null annotation
        return false;
    }
    result = Parse(input);
    return true;
}

// GOOD: Correct annotation matching behavior
public bool TryParse(string input, [NotNullWhen(true)] out MyType? result)
{
    if (!IsValid(input))
    {
        result = null;  // Matches nullable annotation
        return false;
    }
    result = Parse(input);  // Non-null on success, matches [NotNullWhen(true)]
    return true;
}
```

**Fix**: Ensure `out` parameter annotations match actual behavior on all code paths. Use `[NotNullWhen]` and `[MaybeNullWhen]` to express conditional nullability.

---

#### API-NULL-06: `default!` in Public API Without Null Guard
**Severity**: High
**Source**: .NET nullable adoption guidance

**Signal**: `default!` (null-forgiving operator on default) used to initialize a field or return value in public API implementation, suppressing the nullable warning while introducing a null where the API promises non-null.

```csharp
// BAD: default! hides a null in a non-null position
public class UserService
{
    private readonly ILogger _logger = default!;  // Null at runtime
    private readonly IUserRepository _repo = default!;  // Null at runtime

    // If constructor injection fails or is bypassed, these are null
    // despite being typed as non-nullable
}

// BAD: default! in return value
public User GetCurrentUser()
{
    return default!;  // Returns null but signature says non-null
}

// GOOD: Proper constructor initialization with null guard
public class UserService
{
    private readonly ILogger _logger;
    private readonly IUserRepository _repo;

    public UserService(ILogger<UserService> logger, IUserRepository repo)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }
}

// GOOD: Use required keyword (.NET 7+)
public class UserService
{
    public required ILogger Logger { get; init; }
    public required IUserRepository Repo { get; init; }
}
```

**Fix**: Remove `default!` from field initialization. Use constructor injection with null guards, or the `required` modifier. If the field is legitimately initialized later (rare), annotate with `[MemberNotNull]` and initialize in the method that must be called first.

---

### Dependency Injection & Composition Patterns

#### API-DI-01: Service Locator Pattern
**Severity**: Medium
**Source**: Dependency injection design guidance

**Signal**: `IServiceProvider.GetService<T>()` or `IServiceProvider.GetRequiredService<T>()` called outside of composition root, factory, or middleware infrastructure code.

```csharp
// BAD: Service locator -- hidden dependency
public class OrderProcessor
{
    private readonly IServiceProvider _serviceProvider;

    public OrderProcessor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void Process(Order order)
    {
        var validator = _serviceProvider.GetRequiredService<IOrderValidator>();
        var repository = _serviceProvider.GetRequiredService<IOrderRepository>();
        // Dependencies are hidden -- not visible in constructor
    }
}

// GOOD: Explicit constructor injection
public class OrderProcessor
{
    private readonly IOrderValidator _validator;
    private readonly IOrderRepository _repository;

    public OrderProcessor(IOrderValidator validator, IOrderRepository repository)
    {
        _validator = validator;
        _repository = repository;
    }

    public void Process(Order order)
    {
        _validator.Validate(order);
        _repository.Save(order);
    }
}

// ACCEPTABLE: Service locator in factory pattern
public class HandlerFactory
{
    private readonly IServiceProvider _provider;

    public IHandler Create(string handlerType) =>
        handlerType switch
        {
            "email" => _provider.GetRequiredService<EmailHandler>(),
            "sms" => _provider.GetRequiredService<SmsHandler>(),
            _ => throw new ArgumentException($"Unknown handler: {handlerType}")
        };
}
```

**Fix**: Replace `IServiceProvider` injection with explicit constructor dependencies. Service locator is acceptable in composition roots, factories, and middleware that need to resolve services dynamically.

---

#### API-DI-02: Constructor with Too Many Dependencies
**Severity**: Medium
**Source**: .NET Framework Design Guidelines, SOLID principles

**Signal**: Constructor with more than 5 injected dependencies, indicating the class likely has too many responsibilities (SRP violation).

```csharp
// BAD: Too many dependencies -- SRP violation
public class OrderController
{
    public OrderController(
        IOrderService orderService,
        IPaymentService paymentService,
        IInventoryService inventoryService,
        IShippingService shippingService,
        INotificationService notificationService,
        IAuditService auditService,
        IDiscountService discountService,
        ILogger<OrderController> logger)
    { }
}

// GOOD: Decompose into focused collaborators
public class OrderController
{
    public OrderController(
        IOrderOrchestrator orchestrator,
        ILogger<OrderController> logger)
    { }
}

public class OrderOrchestrator : IOrderOrchestrator
{
    public OrderOrchestrator(
        IOrderService orderService,
        IPaymentService paymentService,
        IShippingService shippingService)
    { }
}
```

**Fix**: Decompose the class into smaller, focused classes. Introduce a mediator, orchestrator, or aggregate service that encapsulates related dependencies. The threshold is a guideline -- context matters, but consistently exceeding 5-6 dependencies warrants investigation.

---

#### API-DI-03: Captive Dependency (Scoped Service in Singleton)
**Severity**: Critical
**Source**: ASP.NET Core documentation

**Signal**: A singleton service that depends on a scoped service (or a scoped service depending on a transient disposable). The scoped service is "captured" and lives as long as the singleton, defeating its intended lifetime.

```csharp
// BAD: Scoped DbContext captured by singleton -- context never disposed
services.AddDbContext<AppDbContext>();  // Scoped by default
services.AddSingleton<CacheService>();  // Singleton

public class CacheService  // Lives forever
{
    private readonly AppDbContext _db;  // Captured scoped service -- stale connection

    public CacheService(AppDbContext db)
    {
        _db = db;  // This DbContext will never be disposed or refreshed
    }
}

// GOOD: Use IServiceScopeFactory in singleton
public class CacheService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public CacheService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task RefreshCache()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // db has proper scoped lifetime within this block
    }
}

// GOOD: Enable validation to catch at startup
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});
```

**Fix**: Never inject scoped services into singletons directly. Use `IServiceScopeFactory` to create scopes on demand. Enable `ValidateScopes` and `ValidateOnBuild` in development to catch these errors at startup.

---

#### API-DI-04: Disposable Transient Service Without Scope Management
**Severity**: High
**Source**: ASP.NET Core documentation

**Signal**: Transient service implementing `IDisposable` registered without explicit scope management. The DI container tracks disposable transients and holds references until the scope ends, causing memory pressure.

```csharp
// BAD: Disposable transient -- container holds reference until scope ends
services.AddTransient<IDbConnection, SqlConnection>();
// Each resolution creates a new SqlConnection
// Container tracks all of them for disposal -- memory accumulates

// BAD: In a singleton context, transient disposables are never disposed
services.AddSingleton<BackgroundWorker>();
// BackgroundWorker resolves IDbConnection (transient)
// Root container holds all connections forever

// GOOD: Use a factory pattern
services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();

public class SqlConnectionFactory : IDbConnectionFactory
{
    public IDbConnection CreateConnection()
    {
        return new SqlConnection(_connectionString);
        // Caller is responsible for disposal via 'using'
    }
}

// GOOD: Register as scoped if request-scoped lifetime is appropriate
services.AddScoped<IDbConnection>(sp =>
    new SqlConnection(sp.GetRequiredService<IConfiguration>()
        .GetConnectionString("Default")));
```

**Fix**: Avoid registering `IDisposable` types as transient. Use factory pattern (caller owns disposal), scoped registration, or pooling. If transient is necessary, ensure consumers explicitly dispose via `using`.

---

#### API-DI-05: Concrete Type Injected Without Interface
**Severity**: Low
**Source**: .NET DI best practices

**Signal**: Concrete class registered and injected directly without an interface, limiting testability and substitutability.

```csharp
// QUESTIONABLE: Concrete injection
services.AddScoped<OrderRepository>();

public class OrderService
{
    public OrderService(OrderRepository repo) { }  // Coupled to concrete type
}

// GOOD: Interface-based when substitutability is needed
services.AddScoped<IOrderRepository, OrderRepository>();

public class OrderService
{
    public OrderService(IOrderRepository repo) { }  // Decoupled
}

// ACCEPTABLE: Concrete injection for types that won't need substitution
services.AddSingleton<MetricsCollector>();  // Configuration/infrastructure type
services.AddSingleton(TimeProvider.System);  // Framework abstraction
```

**Fix**: Use interface-based registration for services that may need substitution in tests or alternative implementations. Concrete injection is acceptable for infrastructure, configuration, and utility types that will not change.

---

#### API-DI-06: Ambient Context / Static Service Accessor in Library Code
**Severity**: Medium
**Source**: .NET DI guidelines

**Signal**: Static property or method providing access to a service instance (ambient context pattern), hiding the dependency and making testing difficult.

```csharp
// BAD: Ambient context -- hidden global state
public static class ServiceLocator
{
    public static IServiceProvider Current { get; set; }
}

public class OrderProcessor
{
    public void Process(Order order)
    {
        var logger = ServiceLocator.Current.GetRequiredService<ILogger>();
        // Dependency is hidden -- not in constructor, not testable
    }
}

// BAD: Static accessor for "convenience"
public class DbHelper
{
    public static IDbConnection Connection =>
        HttpContext.Current.RequestServices.GetRequiredService<IDbConnection>();
}

// GOOD: Explicit dependency injection
public class OrderProcessor
{
    private readonly ILogger<OrderProcessor> _logger;

    public OrderProcessor(ILogger<OrderProcessor> logger)
    {
        _logger = logger;
    }
}
```

**Fix**: Replace static service accessors with constructor injection. If a dependency is truly cross-cutting (logging, cancellation), inject it through the constructor. Static accessors hide dependencies and make code untestable.

---

### Versioning & Compatibility Patterns

#### API-VER-01: Public Virtual Method Added Without Override Impact Analysis
**Severity**: Medium
**Source**: .NET Framework Design Guidelines

**Signal**: Adding a new `virtual` method to a non-sealed public class. Existing derived classes may need to override it, or the new default behavior may conflict with overrides of related methods.

```csharp
// RISKY: New virtual method may conflict with existing overrides
public class MessageHandler  // Existing unsealed class
{
    public virtual void Handle(Message msg) { ... }

    // V2: New virtual method added
    public virtual void HandleBatch(IEnumerable<Message> messages)
    {
        foreach (var msg in messages)
            Handle(msg);  // Calls virtual Handle -- override might not expect batch context
    }
}

// Existing derived class doesn't know about HandleBatch:
public class AuditingHandler : MessageHandler
{
    public override void Handle(Message msg)
    {
        LogToAudit(msg);      // Works for single messages
        base.Handle(msg);     // But HandleBatch calls this in a loop
        // Audit log now has N entries instead of 1 batch entry
    }
}

// GOOD: Make new method non-virtual, or coordinate with derived types
public virtual void HandleBatch(IEnumerable<Message> messages)
{
    // Document: derived classes should override this for batch-specific behavior
    // Default implementation calls Handle per item
}
```

**Fix**: Before adding virtual methods to existing types, analyze all existing overrides. Document whether derived classes should override the new method. Consider whether the new method should be non-virtual or abstract.

---

#### API-VER-02: Enum Value Added to Middle of Existing Enum
**Severity**: High
**Source**: .NET Framework Design Guidelines

**Signal**: New enum member inserted between existing members, changing the integer values of subsequent members. This breaks binary serialization, database storage, and any code that persists enum values as integers.

```csharp
// BEFORE (v1):
public enum OrderStatus
{
    Pending,    // 0
    Active,     // 1
    Completed,  // 2
    Cancelled   // 3
}

// BAD: Inserting in the middle changes subsequent values
public enum OrderStatus
{
    Pending,     // 0
    Active,      // 1
    Processing,  // 2  <-- NEW, shifts Completed and Cancelled
    Completed,   // 3  <-- WAS 2, serialized data is now wrong
    Cancelled    // 4  <-- WAS 3
}

// GOOD: Add new values at the end
public enum OrderStatus
{
    Pending,     // 0
    Active,      // 1
    Completed,   // 2  -- unchanged
    Cancelled,   // 3  -- unchanged
    Processing   // 4  -- new value at end
}

// BETTER: Use explicit integer values for stability
public enum OrderStatus
{
    Pending = 0,
    Active = 1,
    Completed = 2,
    Cancelled = 3,
    Processing = 4  // Explicit value, safe to reorder in source
}
```

**Fix**: Always add new enum values at the end, or use explicit integer values. Never insert values between existing members. For enums persisted to databases or serialized, always use explicit values.

---

#### API-VER-03: Parameter Added to Public Constructor (Binary Breaking Change)
**Severity**: High
**Source**: .NET Framework Design Guidelines

**Signal**: New parameter added to an existing public constructor. This is a binary breaking change (existing compiled callers will fail at runtime with `MissingMethodException`) and a source breaking change.

```csharp
// BEFORE (v1):
public class HttpClient
{
    public HttpClient(HttpMessageHandler handler) { }
}

// BAD: Added parameter breaks all existing callers
public class HttpClient
{
    public HttpClient(HttpMessageHandler handler, TimeSpan timeout) { }
    // Existing compiled code calls HttpClient(HttpMessageHandler) -- MissingMethodException
}

// GOOD: Add new constructor overload, keep existing
public class HttpClient
{
    public HttpClient(HttpMessageHandler handler) : this(handler, TimeSpan.FromSeconds(100)) { }
    public HttpClient(HttpMessageHandler handler, TimeSpan timeout) { }
}

// GOOD: Use optional parameter (source compatible but still binary breaking)
// Only acceptable if all consumers are recompiled together
public HttpClient(HttpMessageHandler handler, TimeSpan? timeout = null) { }
```

**Fix**: Never remove or modify existing public constructor signatures. Add new overloads that chain to the original. Use optional parameters only when you control all consumers.

---

#### API-VER-04: Return Type Changed on Public Method
**Severity**: High
**Source**: .NET Framework Design Guidelines

**Signal**: Changing the return type of a public method (widening, narrowing, or changing entirely). Both binary and source breaking change.

```csharp
// BEFORE (v1):
public List<Order> GetOrders() { ... }

// BAD: Narrowing return type (binary + source breaking)
public IReadOnlyList<Order> GetOrders() { ... }
// Existing code: List<Order> orders = service.GetOrders(); // Compile error

// BAD: Widening return type is also binary breaking
public IEnumerable<Order> GetOrders() { ... }
// Existing compiled code expects List<Order> on stack -- TypeLoadException

// GOOD: Keep original, add new method
public List<Order> GetOrders() { ... }

[EditorBrowsable(EditorBrowsableState.Never)]
public IAsyncEnumerable<Order> GetOrdersAsync() { ... }  // New streaming API

// GOOD: Design return types for evolution from the start
// Return IReadOnlyList<T> rather than List<T> for flexibility
public IReadOnlyList<Order> GetOrders() { ... }
```

**Fix**: Never change the return type of a public method. Add a new method with a different name. Design initial return types for flexibility (prefer `IReadOnlyList<T>` over `List<T>`, `IEnumerable<T>` for streaming).

---

#### API-VER-05: Missing `[Obsolete]` Before Planned Removal
**Severity**: Medium
**Source**: .NET Framework Design Guidelines

**Signal**: Public API member removed without a prior release marking it `[Obsolete]`. Consumers have no warning or migration guidance.

```csharp
// BAD: Removing without obsolete warning
// v1: public void SendEmail(string to, string body) { ... }
// v2: method deleted -- all callers break with no guidance

// GOOD: Deprecation cycle with migration guidance
// v2: Mark obsolete with replacement guidance
[Obsolete("Use IEmailService.SendAsync instead. This method will be removed in v4.0.")]
public void SendEmail(string to, string body)
{
    // Keep working implementation during deprecation period
    SendAsync(to, body).GetAwaiter().GetResult();
}

// v3: Escalate to error
[Obsolete("Use IEmailService.SendAsync instead. Will be removed in v4.0.", error: true)]
public void SendEmail(string to, string body) { ... }

// v4: Remove the method
```

**Fix**: Always mark APIs as `[Obsolete]` for at least one release before removal. Include the replacement API in the message. Use `error: true` in the second deprecation release to force migration.

---

#### API-VER-06: `[EditorBrowsable(Never)]` Hiding Design Issues
**Severity**: Low
**Source**: .NET Framework Design Guidelines

**Signal**: `[EditorBrowsable(EditorBrowsableState.Never)]` used to hide poorly-designed API members from IntelliSense rather than fixing the underlying design problem.

```csharp
// SUSPICIOUS: Hiding complexity rather than fixing it
public class OrderService
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void ProcessOrderInternal(Order order, bool skipValidation,
        bool forceCommit, int retryCount, string overrideStatus) { ... }

    public void ProcessOrder(Order order) =>
        ProcessOrderInternal(order, false, false, 3, null);
}
// The method is still public and callable -- hiding it doesn't fix the API

// ACCEPTABLE: Hiding infrastructure methods that must be public for framework reasons
public class MyComponent : IAsyncDisposable
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public ValueTask DisposeAsync() { ... }  // Must be public for interface, but not primary API
}

// ACCEPTABLE: Hiding explicit interface implementations or backward-compat methods
[EditorBrowsable(EditorBrowsableState.Never)]
[Obsolete("Use ProcessAsync instead.")]
public void Process(Order order) { ... }  // Kept for binary compatibility
```

**Fix**: Use `[EditorBrowsable(Never)]` only for legitimate framework requirements (explicit interface implementation shims, backward-compatibility methods). If an API is bad enough to hide, redesign it.

---

## Related Skills

- [C# Security Review](../cs-security/SKILL.md) -- Security implications of API design choices
- [C# Error Handling Review](../cs-error-handling/SKILL.md) -- Error type design and exception patterns
- [C# Performance Review](../cs-performance/SKILL.md) -- Performance implications of type design (struct vs class, allocation)
- [C# Memory & Lifecycle Review](../cs-memory-lifecycle/SKILL.md) -- Disposable patterns, lifetime management

## References

1. .NET Framework Design Guidelines, 3rd Edition (Addison-Wesley)
2. .NET API Design Guidelines -- https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/
3. C# Language Design -- https://github.com/dotnet/csharplang
4. ASP.NET Core Diagnostic Scenarios -- https://github.com/davidfowl/AspNetCoreDiagnosticScenarios
5. Nullable Reference Types -- https://learn.microsoft.com/en-us/dotnet/csharp/nullable-references
6. Breaking Changes in .NET -- https://learn.microsoft.com/en-us/dotnet/core/compatibility/
7. Roslyn Analyzers -- https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/overview
