# C# Language Improvements for Declarative UI

**Status:** Brainstorm / Proposal Draft  
**Date:** 2026-04-04  
**Goal:** Identify the smallest set of C# language changes that would make Reactor (and declarative UI frameworks generally) dramatically better. Each proposal includes current syntax, proposed syntax, a rough feature definition, and references to prior art.

### Tier Ratings

Each proposal is rated on three axes — benefit to Reactor, likelihood of landing in C#, and positive impact on the broader C# community — then combined into a single tier:

- **S Tier** — High value across all three axes. Invest here first.
- **A Tier** — Strong on two axes, viable on the third. Worth championing.
- **B Tier** — Compelling idea, but blocked by feasibility or narrow scope. Watch and support.
- **C Tier** — Great concept with fatal practical obstacles. Aspirational; don't plan around it.

---

## Table of Contents

1. [Discriminated Unions — S Tier](#1-discriminated-unions)
2. [Expression Blocks — A Tier](#2-expression-blocks)
3. [Let-Binding Expressions — A Tier](#3-let-binding-expressions)
4. [Render Method Compiler Transform — A Tier](#4-render-method-compiler-transform)
5. [Collection Initializer Trees — B Tier](#5-collection-initializer-trees)
6. [Trailing Lambdas — B Tier](#6-trailing-lambdas)
7. [Result Builders — B Tier](#7-result-builders)
8. [Scoped Extension Receivers — B Tier](#8-scoped-extension-receivers)
9. [Property Wrappers — C Tier](#9-property-wrappers)
10. [Markup Expressions — C Tier](#10-markup-expressions)

---

## 1. Discriminated Unions — S Tier

**Inspiration:** Swift enums with associated values, Kotlin sealed classes, Rust enums  
**Prior art:** [dotnet/csharplang#9662](https://github.com/dotnet/csharplang/issues/9662), [#9663](https://github.com/dotnet/csharplang/discussions/9663)

### Problem

Reactor components sometimes need to represent a fixed set of UI states (loading/error/success, different view modes, navigation destinations). Today this requires separate classes or enums plus separate data carriers, and pattern matching doesn't guarantee exhaustiveness across the data.

### Current Reactor Syntax

```csharp
// Must define separate types and manually associate data with states
abstract record FetchResult;
record Loading : FetchResult;
record ErrorResult(string Message, Exception? Ex) : FetchResult;
record Success(List<Item> Items) : FetchResult;

class DataView : Component
{
    public override Element Render()
    {
        var (result, setResult) = UseState<FetchResult>(new Loading());

        // Pattern matching works but compiler can't verify exhaustiveness
        // Adding a new case doesn't produce a warning
        return result switch
        {
            Loading => Spinner(),
            ErrorResult err => VStack(4,
                Text(err.Message).Foreground("red"),
                Button("Retry", () => Fetch(setResult))
            ),
            Success s => VStack(4,
                s.Items.Select(i => Text(i.Name)).ToArray()
            ),
            // If we forget a case, no compiler warning (base type isn't sealed
            // to the switch in a way the compiler tracks)
            _ => Empty()  // Defensive default — hides missing cases
        };
    }
}
```

### Proposed Syntax

```csharp
// Discriminated union — compiler knows all cases, enforces exhaustive matching
union FetchResult
{
    Loading,
    Error(string Message, Exception? Ex),
    Success(List<Item> Items)
}

class DataView : Component
{
    public override Element Render()
    {
        var (result, setResult) = UseState(FetchResult.Loading);

        // Exhaustive match — compiler error if a case is missing
        // No need for _ default — all cases covered
        return result switch
        {
            FetchResult.Loading => Spinner(),
            FetchResult.Error(var msg, _) => VStack(4,
                Text(msg).Foreground("red"),
                Button("Retry", () => Fetch(setResult))
            ),
            FetchResult.Success(var items) => VStack(4,
                items.Select(i => Text(i.Name)).ToArray()
            )
            // COMPILER ERROR if we add a new case to the union
            // and don't handle it here
        };
    }
}
```

### Rough Definition

- A **discriminated union** is declared with `union TypeName { Case1, Case2(Data), ... }`.
- Each case can carry associated data (positional or named).
- Pattern matching on a union is **exhaustive** — the compiler errors if any case is unhandled and no default `_` arm is present.
- Adding a new case to a union produces compile errors at all incomplete match sites.
- This is an [active proposal](https://github.com/dotnet/csharplang/issues/9662) for C# and widely requested by the community.
- For Reactor, this enables type-safe navigation state, fetch results, form validation states, and view mode switching without defensive defaults.

### Why Not

**The C# DU working group found they need four separate designs.** The [working group notes](https://github.com/dotnet/csharplang/blob/main/meetings/working-groups/discriminated-unions/TypeUnions.md) concluded that no single DU design satisfies all use cases. They identified the need for union classes, union structs, ad hoc unions, and custom unions — four distinct type kinds. This fragmentation itself signals unresolved tensions: value types vs reference types, named vs anonymous, new hierarchies vs existing ones, open vs closed. The feature may be too large to ship as a coherent unit.

**Closed hierarchies are foreign to C#'s type system.** C# is fundamentally open for inheritance — any class can be extended by anyone. DUs require closed hierarchies where the compiler knows every possible case. The working group identified this as a core challenge: "there's no way to mark the base class as 'sealed, except for these'" in current C#. Without true closedness (which requires a new concept in the type system), you lose exhaustiveness checking — the main benefit of DUs. Retrofitting closedness across assembly boundaries, with nullable reference types, partial classes, and existing `sealed` semantics, is a massive undertaking.

**The expression problem tension.** DUs and class hierarchies solve opposite halves of the [expression problem](https://en.wikipedia.org/wiki/Expression_problem). DUs make it easy to add new operations over existing cases, but adding a new case requires modifying every match site. Class hierarchies make it easy to add new types, but adding a new operation requires modifying every class. Adding DUs to C# means developers must now choose which axis of extension matters more — without clear guidance. This is a genuine architectural decision that many C# developers are not equipped to make, and getting it wrong means painful refactoring later.

**`default` and the undefined state problem.** Union structs can enter an "undefined state" via `default(MyUnion)`, which is always valid for value types in C#. This means an exhaustive switch on a union struct can still fail at runtime when the value is `default` — undermining the compile-time safety guarantee that is the entire point of the feature. The working group acknowledged this problem but has not resolved it.

**Record hierarchies already work.** C# records with `abstract` base and sealed derived types provide most of what DUs offer: `abstract record FetchResult; sealed record Loading : FetchResult; sealed record Error(string Msg) : FetchResult; sealed record Success(List<Item> Items) : FetchResult;` Pattern matching works, deconstruction works, equality works. The compiler doesn't enforce exhaustiveness, but that's a single diagnostic away from being solvable with an analyzer — no new type kind needed.

**Historical precedent against.** Bjarne Stroustrup deliberately omitted discriminated unions from C++ because he considered them "poor programming practice" in an OOP context. This decision propagated through C++, Java, and C#. While modern language design has shifted (Rust, Swift, Kotlin all have them), the argument that DUs create tension with virtual dispatch and open/closed principles has merit. Adding DUs to C# 25 years into the language's life means retrofitting a concept that the original type system was designed to not need.

**Not specific to UI frameworks.** DUs are a general-purpose type system feature with broad applicability (error handling, state machines, protocol messages, ASTs). While this means they have a large audience, it also means the design must satisfy many different use cases beyond UI. This makes it harder to ship quickly and more likely to be delayed by design debates. The C# team has been discussing DUs since at least 2017 with no committed release target — a strong signal that the design space is genuinely hard.

### Expanded Use Cases

Discriminated unions have the broadest non-UI applicability of any proposal here. The overarching argument across all use cases: the compiler enforces exhaustiveness — when you add a new variant, every `switch` that handles that union produces a warning. This is the "make illegal states unrepresentable" principle from Scott Wlaschin's *[Domain Modeling Made Functional](https://pragprog.com/titles/swdddf/domain-modeling-made-functional/)* — bugs become compile errors instead of runtime surprises. See also [F# for Fun and Profit: Making illegal states unrepresentable](https://fsharpforfunandprofit.com/posts/designing-with-types-making-illegal-states-unrepresentable/).

**Error handling (`Result<T, E>`).** The most universal use case. Replaces exceptions for expected failures, forces callers to handle both paths. Every Rust function returns `Result` or `Option`.

```csharp
union Result<T, E> { Ok(T Value), Error(E Err) }

Result<User, DbError> result = repo.FindUser(id);
var msg = result switch {
    Ok(var u)    => $"Hello {u.Name}",
    Error(var e) => $"Failed: {e.Code}"
};
```

**AST / compiler construction.** Recursive DUs are the canonical compiler building block. Every expression node is a variant. This is F#'s signature use case and the reason discriminated unions exist in ML-family languages.

```csharp
union Expr {
    Literal(double Value),
    BinaryOp(Expr Left, Op Operator, Expr Right),
    FunctionCall(string Name, IReadOnlyList<Expr> Args)
}

double Eval(Expr e) => e switch {
    Literal(var v)                 => v,
    BinaryOp(var l, Op.Add, var r) => Eval(l) + Eval(r),
    FunctionCall(var n, var a)     => Invoke(n, a)
};
```

**Protocol messages / network packets.** Each message type carries different payload data. A union prevents "check the type field, then cast" anti-patterns.

```csharp
union ServerMessage {
    Handshake(int ProtocolVersion),
    Data(ReadOnlyMemory<byte> Payload),
    Heartbeat,
    Disconnect(string Reason)
}

void Handle(ServerMessage m) => m switch {
    Handshake(var v) => NegotiateVersion(v),
    Data(var p)      => ProcessPayload(p),
    Heartbeat        => ResetTimer(),
    Disconnect(var r)=> Log(r)
};
```

**State machines.** Each state carries only the data relevant to that state. Illegal transitions become compile errors. The compiler tracks "state A uses field X, state B uses field Y" so you never access `Stream` on a `Disconnected` value. See [corrode.dev: Using Enums to Represent State](https://corrode.dev/blog/enums/).

```csharp
union ConnState {
    Disconnected,
    Connecting(Uri Endpoint, CancellationToken Ct),
    Connected(TcpClient Client, Stream Stream),
    Failed(Exception Error, int RetryCount)
}
```

**Command / event patterns (CQRS).** Commands and events are natural sum types — a finite set of things that can happen.

```csharp
union OrderCommand {
    PlaceOrder(CartId Cart, Address Shipping),
    CancelOrder(OrderId Id, string Reason),
    AddLineItem(OrderId Id, Sku Sku, int Qty)
}

union OrderEvent {
    OrderPlaced(OrderId Id, DateTime At),
    OrderCancelled(OrderId Id, string Reason)
}
```

**Domain modeling (making illegal states unrepresentable).** The core argument from Scott Wlaschin: encode business rules in the type system. No "PaymentType" enum + nullable fields. Every case carries exactly its data.

```csharp
union PaymentMethod {
    CreditCard(string Last4, DateOnly Expiry),
    BankTransfer(string Iban),
    PayPal(string Email)
}

union ContactInfo {
    EmailOnly(string Email),
    PostOnly(Address Addr),
    EmailAndPost(string Email, Address Addr)
    // The invalid "neither" state is impossible — a class with two nullable fields would allow it
}
```

**JSON / API response parsing.** APIs often return different shapes under a single field. A union maps directly to this.

```csharp
union ApiResponse<T> {
    Success(T Data),
    ValidationError(IReadOnlyList<FieldError> Errors),
    RateLimited(TimeSpan RetryAfter),
    Unauthorized
}
```

**Option / Maybe types.** The simplest DU. Eliminates null reference exceptions by forcing explicit handling.

```csharp
union Option<T> { Some(T Value), None }

Option<User> user = TryFind(id);
var name = user switch {
    Some(var u) => u.Name,
    None        => "Guest"
};
```

Further reading: [Enterprise Craftsmanship: C# and F# approaches to illegal state](https://enterprisecraftsmanship.com/posts/c-and-f-approaches-to-illegal-state/), [Chris Krycho: Making Illegal States Unrepresentable in TypeScript](https://v5.chriskrycho.com/journal/making-illegal-states-unrepresentable-in-ts/), [Thinktecture: Discriminated Unions in .NET](https://www.thinktecture.com/en/net/discriminated-unions-representation-of-alternative-types-in-dotnet/).

---

## 2. Expression Blocks — A Tier

**Inspiration:** Kotlin (everything is an expression), Rust (`let x = if ... { } else { }`), Ruby  
**Prior art:** [dotnet/csharplang#9243](https://github.com/dotnet/csharplang/issues/9243) (championed March 2025), [#8411](https://github.com/dotnet/csharplang/discussions/8411)

### Problem

Reactor render methods must return expressions, but complex conditional logic is naturally expressed with statements. Developers must choose between awkward ternary chains, helper methods that fragment the render logic, or the `When()` helper that hides the condition.

### Current Reactor Syntax

```csharp
return VStack(12,
    // Simple conditional — fine as ternary
    isLoggedIn ? Text($"Welcome, {user}") : Text("Please log in"),
    
    // Multi-branch — switch expression works but gets unwieldy
    status switch
    {
        Status.Loading => VStack(4,
            Spinner(),
            Text("Loading...")
        ),
        Status.Error => VStack(4,
            Icon("Error"),
            Text(errorMessage).Foreground("red"),
            Button("Retry", onRetry)
        ),
        Status.Ok => VStack(4,
            Text("Success!"),
            content
        ),
        _ => Empty()
    },
    
    // Complex conditional — forced into helper method or ugly ternary
    // Can't use if/else inline because it's a statement, not expression
    GetStatusBadge(items, isAdmin)  // Extracted to helper, fragmenting the tree
);

// Helper that exists only because if/else isn't an expression
static Element GetStatusBadge(List<Item> items, bool isAdmin)
{
    if (items.Count == 0)
        return Text("No items").Opacity(0.5);
    if (isAdmin && items.Any(i => i.Flagged))
        return Badge("Needs review").Background("orange");
    if (items.All(i => i.Complete))
        return Badge("All complete").Background("green");
    return Badge($"{items.Count(i => !i.Complete)} remaining");
}
```

### Proposed Syntax

```csharp
return VStack(12,
    isLoggedIn ? Text($"Welcome, {user}") : Text("Please log in"),
    
    // Expression block — if/else as expression, last expression is the value
    {
        if (items.Count == 0)
            yield Text("No items").Opacity(0.5);
        else if (isAdmin && items.Any(i => i.Flagged))
            yield Badge("Needs review").Background("orange");
        else if (items.All(i => i.Complete))
            yield Badge("All complete").Background("green");
        else
            yield Badge($"{items.Count(i => !i.Complete)} remaining");
    },
    
    // Can also use for computed values inline
    {
        var total = items.Sum(i => i.Price);
        var tax = total * 0.08;
        yield Text($"Total: ${total + tax:F2}").Bold();
    }
);
```

### Rough Definition

- An **expression block** is a brace-delimited block `{ statements; yield expr; }` that evaluates to a value.
- The `yield` keyword (or a new keyword like `give` or `eval`) specifies the block's resulting value.
- All branches must yield the same type (or a common base type).
- Local variables declared inside the block are scoped to the block.
- Expression blocks can appear anywhere an expression is expected: function arguments, variable initializers, return values, array elements.
- This is the [championed proposal #9243](https://github.com/dotnet/csharplang/issues/9243) — the most likely of these proposals to actually ship in C#.

### Why Not

**Syntactic ambiguity with existing braces.** C# already uses `{}` in at least 6 syntactic positions (blocks, initializers, collection expressions, lambdas, property accessors, switch expression arms). Expression blocks add a 7th meaning. The parser must distinguish `{ var x = 1; yield x; }` (expression block) from `{ var x = 1; yield return x; }` (iterator block) and `{ var x = 1; }` (statement block). The LDM has raised this ambiguity as a significant design challenge.

**`yield` keyword conflict.** C# already uses `yield return` and `yield break` in iterator methods. Reusing `yield` in expression blocks with different semantics (producing a block's value vs. producing a sequence element) would be confusing. Alternative keywords (`give`, `eval`, `result`) are all either awkward or conflict with common identifier names. The keyword choice alone is a contentious design debate.

**Encourages overly clever code.** Expression blocks enable embedding arbitrary statement logic inside expressions — function arguments, ternary arms, array initializers. The C# team has expressed concern that this encourages deeply nested, hard-to-read code where a local variable or helper method would be clearer. The feature optimizes for writing convenience at the expense of reading clarity. "Just because you *can* inline it doesn't mean you *should*" is a real code review concern.

**Interaction with `ref`, `await`, definite assignment, and nullable analysis.** Variables declared inside an expression block and their lifetime/scope semantics need careful definition. Can you `await` inside an expression block? Can you capture `ref` locals? How does nullable flow analysis propagate through `yield`? Each interaction multiplies design surface area and the LDM has flagged these as requiring significant work.

**The problem has existing solutions.** Complex conditional logic in Reactor already works via: (1) switch expressions for multi-branch, (2) ternary for two-branch, (3) helper methods for complex cases, (4) the `When()` DSL helper. These are well-understood C# patterns. Expression blocks would add a new way to do what's already possible, creating "two ways to do it" fragmentation that the C# team has historically tried to avoid.

**Modest impact on Reactor specifically.** Looking at the actual Reactor test app code, most conditional rendering uses switch expressions or ternaries that are already clean enough. The cases where expression blocks would help — multi-statement computed values inline — are relatively rare and arguably better served by local methods that name the computation.

### Expanded Use Cases

The strongest non-UI argument for expression blocks is **immutability by default** — they let you assign a variable once with arbitrarily complex logic, eliminating the "declare mutable, then assign in branches" anti-pattern. Rust and Kotlin developers consistently cite this as the feature they miss most in statement-oriented languages. The pattern-matching-with-computation case was the most upvoted example in [csharplang #8411](https://github.com/dotnet/csharplang/discussions/8411).

**Complex variable initialization.** Multi-step computation to initialize an immutable variable. Today you must use a mutable variable or extract a helper method.

```csharp
// Current: mutable variable or helper
string connectionString;
if (env.IsDevelopment())
    connectionString = config["Dev:ConnStr"]!;
else if (env.IsStaging())
    connectionString = config["Staging:ConnStr"]! + ";Encrypt=true";
else
{
    var vault = new SecretClient(vaultUri);
    connectionString = vault.GetSecret("db-conn").Value.Value;
}

// Proposed: single immutable binding
var connectionString = {
    if (env.IsDevelopment())
        yield config["Dev:ConnStr"]!;
    else if (env.IsStaging())
        yield config["Staging:ConnStr"]! + ";Encrypt=true";
    else {
        var vault = new SecretClient(vaultUri);
        yield vault.GetSecret("db-conn").Value.Value;
    }
};
```

**Pattern matching with computation.** Switch expressions cannot contain statements. If a branch needs a local variable, you must extract a method today. This was the most upvoted use case in [csharplang #8411](https://github.com/dotnet/csharplang/discussions/8411).

```csharp
// Current: forced helper for one branch
var discount = customer.Tier switch {
    Tier.Gold => 0.15,
    Tier.Silver => 0.10,
    Tier.Bronze => CalculateBronzeDiscount(customer), // helper exists only for this
    _ => 0.0
};

// Proposed: inline the computation
var discount = customer.Tier switch {
    Tier.Gold => 0.15,
    Tier.Silver => 0.10,
    Tier.Bronze => {
        var months = (DateTime.Now - customer.JoinDate).Days / 30;
        yield Math.Min(0.05 + months * 0.005, 0.10);
    },
    _ => 0.0
};
```

**Data transformation pipelines (scope hygiene).** Intermediate variables are confined to the block — `raw` and `parsed` don't leak into the enclosing scope.

```csharp
var items = {
    var raw = await http.GetStringAsync(url);
    var parsed = JsonDocument.Parse(raw);
    yield parsed.RootElement.GetProperty("data")
        .EnumerateArray()
        .Select(e => e.GetProperty("name").GetString()!)
        .Where(n => !string.IsNullOrEmpty(n))
        .ToList();
};
```

**API response construction.** Eliminates the "declare then mutate" pattern.

```csharp
// Current: early declaration then conditional mutation
var response = new ApiResponse { Status = 200 };
if (result.Errors.Any()) {
    response.Status = 400;
    response.Body = new { errors = result.Errors };
} else {
    response.Body = new { data = result.Value };
}
return response;

// Proposed
return {
    if (result.Errors.Any())
        yield new ApiResponse(400, new { errors = result.Errors });
    else
        yield new ApiResponse(200, new { data = result.Value });
};
```

**Business rule evaluation.** Readable multi-step logic replaces deeply nested ternaries.

```csharp
var shippingCost = {
    if (order.Total > 100)
        yield 0m;  // free shipping over $100
    else if (order.IsExpress)
        yield 15m;
    else {
        var baseCost = 5m;
        var weightSurcharge = Math.Max(0, order.Weight - 10) * 0.50m;
        yield baseCost + weightSurcharge;
    }
};
```

**Core arguments from the literature:** Expression blocks enable composability (as noted on [F# for Fun and Profit](https://fsharpforfunandprofit.com/posts/expressions-vs-statements/) and the [Wikipedia article on expression-oriented languages](https://en.wikipedia.org/wiki/Expression-oriented_programming_language)) — expressions compose naturally; you can nest them, pass them, and assign them without restructuring code. See also the [Rust block expressions reference](https://doc.rust-lang.org/reference/expressions/block-expr.html).

---

## 3. Let-Binding Expressions — A Tier

**Inspiration:** F#/OCaml `let x = expr in body`, Kotlin scope functions (`let`, `run`), Rust block-scoped `let`  
**Prior art:** [dotnet/csharplang#973](https://github.com/dotnet/csharplang/issues/973) (Declaration Expressions, championed), [#5632](https://github.com/dotnet/csharplang/discussions/5632) (Narrowly-scoped pattern variables), [#9243](https://github.com/dotnet/csharplang/issues/9243) (Expression Blocks)

### Problem

C# has no way to introduce a named intermediate value inside an expression. When an expression needs to compute a value and use it multiple times — or when a subexpression is complex enough to deserve a name — the developer must either pre-declare a variable (polluting the outer scope and separating the computation from its use), extract a helper method (fragmenting the logic), or use the `is var` hack (confusing semantics).

This is a general-purpose gap that affects every C# developer, but it's especially painful in declarative UI where the entire render tree is one large expression.

### Current Workarounds

```csharp
// Workaround 1: Pre-declare variables — pollutes outer scope, separates computation from use
var total = items.Sum(i => i.Price);
var tax = total * 0.08;
return VStack(16,
    Text($"Total: {total + tax:F2}").Bold(),
    Text($"Tax: {tax:F2}"),
    Text($"Subtotal: {total:F2}")
);

// Workaround 2: Repeat the computation (DRY violation)
return VStack(16,
    Text($"Total: {items.Sum(i => i.Price) * 1.08:F2}").Bold(),
    Text($"Tax: {items.Sum(i => i.Price) * 0.08:F2}")  // computed twice
);

// Workaround 3: The "is var" hack — always-true pattern match as pseudo-let
items.Where(i => i.Price is var p && p > 0 ? p * taxRate > threshold : false)

// Workaround 4: Helper method — fragments the logic
return VStack(16, BuildPriceSummary(items));
```

### Proposed Syntax — Flavor 1: `let...in` (ML-style, narrowly scoped)

```csharp
// "let X = expr in body" — X is scoped only to body
var greeting =
    let title = user.IsAdmin ? "Admin" : "User" in
    let name = $"{user.First} {user.Last}" in
    $"Welcome, {title} {name}!";

// In a Reactor render tree — name subexpressions inline
return VStack(16,
    let total = items.Sum(i => i.Price) in
    let tax = total * 0.08 in
    Text($"Total: {total + tax:F2}").Bold(),
    Text($"Tax: {tax:F2}"),         // tax still in scope? Depends on scoping rules
    Text($"Subtotal: {total:F2}")
);
```

**Translation:** `let x = E1 in E2` desugars to `((Func<T>)(() => { var x = E1; return E2; }))()` — or more likely, the compiler inlines it without the lambda overhead.

**Scoping:** `x` is visible only within the `in` body expression. This is the key difference from declaration expressions (#973), where variables leak to the enclosing block.

### Proposed Syntax — Flavor 2: Inline declaration in argument lists

```csharp
// "let X = expr" as a pseudo-argument — X is visible to all subsequent arguments
return VStack(16,
    Heading("Price Summary"),
    let total = items.Sum(i => i.Price),
    let tax = total * 0.08,
    Text($"Total: {total + tax:F2}").Bold(),
    Text($"Tax: {tax:F2}"),
    Text($"Subtotal: {total:F2}")
);
```

**Translation:** The compiler hoists `let` bindings into local variables before the method call:

```csharp
var __total = items.Sum(i => i.Price);
var __tax = __total * 0.08;
return VStack(16,
    Heading("Price Summary"),
    Text($"Total: {__total + __tax:F2}").Bold(),
    Text($"Tax: {__tax:F2}"),
    Text($"Subtotal: {__total:F2}")
);
```

**Scoping:** The binding is visible to subsequent arguments in the same call, but not outside the call. This is similar to how LINQ `let` scopes to subsequent clauses.

### Rough Definition

- A **let-binding expression** introduces a named value within expression context: `let name = initializer in body` (Flavor 1) or `let name = initializer` within argument lists (Flavor 2).
- The binding is **narrowly scoped** — it does not leak into the enclosing block (unlike `out var` and `is var` patterns).
- The type of `name` is inferred from `initializer`.
- Multiple let-bindings can chain: `let a = x in let b = f(a) in g(a, b)`.
- The `let` keyword is already contextual in C# (used in LINQ query syntax), so this extension is syntactically feasible.
- Flavor 1 is a pure expression form — it can appear anywhere an expression is expected.
- Flavor 2 is specific to argument lists and requires the compiler to understand `let` as a special pseudo-argument.

### Relationship to Expression Blocks

Let-binding expressions and expression blocks (proposal 2) are **complementary, not competing**:

- **Expression blocks** give you "statements in expression position" — full `if/else`, `foreach`, `using`, arbitrary logic.
- **Let-bindings** give you "named intermediate values without leaving expression context" — lighter, no block nesting, stays within the flow of the expression.

If both ship, they cover different needs. If only one ships, expression blocks subsume let-bindings (since you can always write `{ var x = expr; yield f(x); }`), but let-bindings are a much smaller language change that could ship sooner.

### Why Not

**Declaration expressions (#973) already cover this.** The [championed proposal](https://github.com/dotnet/csharplang/issues/973) for declaration expressions (`(var x = expr)` as an expression) has been open since 2017. It uses wider scope (variables leak to the enclosing block, like `out var`), which the LDM [considered and accepted](https://github.com/dotnet/csharplang/issues/1741). Adding a separate narrowly-scoped `let` form would create two similar-but-different binding mechanisms, increasing the language's surface area. The LDM may prefer to ship declaration expressions first and assess whether narrow scoping is needed.

**Ambiguity with LINQ `let`.** C# already uses `let` in query syntax (`from x in xs let y = f(x) select ...`). Reusing the keyword in general expression position risks confusion about whether you're in query context. The parser can likely disambiguate (query `let` only appears after `from`/`select`/etc.), but the cognitive load on developers reading the code is real.

**Scope leakage vs. narrow scope is a contested design question.** The [#1741 discussion](https://github.com/dotnet/csharplang/issues/1741) and [#5632 discussion](https://github.com/dotnet/csharplang/discussions/5632) show genuine disagreement. Wide scope (like `out var`) is more flexible but pollutes the enclosing block. Narrow scope (like ML `let...in`) is cleaner but unfamiliar to C# developers and requires the `in` keyword, which already means something in `foreach` and LINQ.

**Flavor 2's argument-list scoping is novel and complex.** No C# construct currently has bindings that "flow forward" through a method's argument list. This would be a new scoping concept that interacts with argument evaluation order (which C# specifies as left-to-right, so it's technically safe, but it's still surprising). The LDM may find this too clever.

**Expression blocks (#9243) subsume this.** If expression blocks ship, you get scoped bindings as a special case: `{ var x = expr; yield f(x); }`. The LDM may decide that one general mechanism is better than two specialized ones.

**Existing patterns work well enough.** Pre-declaring `var total = ...` one line above the expression is simple, explicit, and well-understood. The "scope pollution" argument is real but modest — local variables in a method body are already scoped to the method, and extracting a local is standard C# practice.

### Expanded Use Cases

The need for inline named values is **universal in C#**, not UI-specific. The `is var` hack's widespread use proves demand.

**Complex LINQ chains needing intermediates.** Today you must break into a multi-statement lambda or use `is var`:

```csharp
// Current: is var hack
items.Select(x => x.Price is var p ? (p, tax: p * 0.08, total: p * 1.08) : default)

// Proposed
items.Select(x =>
    let p = x.Price in
    (p, tax: p * 0.08, total: p * 1.08))
```

**Conditional expressions with shared subexpressions.** Computing a value once for use in both branches:

```csharp
// Current: pre-declare or compute twice
var user = GetUser();
var display = user != null ? $"{user.First} {user.Last}" : "Guest";

// Proposed
var display = let u = GetUser() in u != null ? $"{u.First} {u.Last}" : "Guest";
```

**String interpolation with computed parts.** Reusing a computed value within an interpolated string:

```csharp
// Current: pre-declare
var count = items.Count();
Console.WriteLine($"Found {count} items ({count * 100 / total}%)");

// Proposed
Console.WriteLine(let c = items.Count() in $"Found {c} items ({c * 100 / total}%)");
```

**Expression-bodied members.** Let-bindings keep you in expression context, preserving the `=>` form:

```csharp
// Current: must switch from expression body to block body
public string DisplayName
{
    get
    {
        var title = IsAdmin ? "Admin" : "User";
        return $"[{title}] {FirstName} {LastName}";
    }
}

// Proposed: stays expression-bodied
public string DisplayName =>
    let title = IsAdmin ? "Admin" : "User" in
    $"[{title}] {FirstName} {LastName}";
```

**Logging and diagnostics.** Bind a result to log it and return it in one expression:

```csharp
return let result = ComputeResult() in
    (logger.LogInformation("Result: {R}", result), result).result;
```

**Prior art:** F#, OCaml, Haskell, and Scala all have `let` as a core expression form. Kotlin's scope functions (`let`, `run`, `with`, `also`, `apply`) serve a similar purpose via lambdas — `value.let { v -> compute(v) }`. The `is var` pattern in C# is a de facto demand signal for this feature. [Declaration Expressions #973](https://github.com/dotnet/csharplang/issues/973), [Narrowly-scoped let #5632](https://github.com/dotnet/csharplang/discussions/5632), [Declaration Expressions discussion #595](https://github.com/dotnet/csharplang/discussions/595).

---

## 4. Render Method Compiler Transform — A Tier

**Inspiration:** Kotlin `@Composable` compiler plugin, C# `async/await` state machine transform  
**Prior art:** [Jetpack Compose compiler](https://developer.android.com/develop/ui/compose/mental-model), Roslyn source generators

### Problem

Reactor's hook system relies on call-order stability: hooks must be called in the same order every render. This is a runtime contract — violating it throws `InvalidOperationException`. The compiler could enforce this statically and optimize re-renders by knowing which parameters changed.

### Current Reactor Syntax

```csharp
class CounterDemo : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);     // Hook #0
        var (step, setStep) = UseState(1);        // Hook #1

        // BUG: This conditional hook breaks hook ordering!
        // Runtime error on second render if condition changes.
        if (count > 5)
        {
            var (extra, setExtra) = UseState(""); // Hook #2 — only sometimes!
        }

        return VStack(12,
            Text($"Count: {count}"),
            Button("+", () => setCount(count + step))
        );
    }
}
```

### Proposed: Compiler-Enforced Hook Safety

```csharp
// The [Render] attribute (or Component base class override) tells the compiler
// to analyze hook call ordering and reject conditional/loop hooks at compile time.

class CounterDemo : Component
{
    public override Element Render()
    {
        var (count, setCount) = UseState(0);
        var (step, setStep) = UseState(1);

        // COMPILER ERROR CS9901: UseState() call inside conditional block.
        // Hook calls must be unconditional and in consistent order.
        if (count > 5)
        {
            var (extra, setExtra) = UseState(""); // ← red squiggle
        }

        return VStack(12,
            Text($"Count: {count}"),
            Button("+", () => setCount(count + step))
        );
    }
}
```

### Extended: Skip Optimization (Compose-style)

```csharp
// Compiler generates a $changed bitmask parameter, enabling the framework
// to skip re-rendering when no inputs have changed.

// What the developer writes:
class Greeting : Component<GreetingProps>
{
    public override Element Render()
    {
        return Text($"Hello, {Props.Name}!").FontSize(Props.Size);
    }
}

// What the compiler generates (conceptual):
class Greeting : Component<GreetingProps>
{
    public override Element Render(int $changed)
    {
        if ($changed == 0) return _cachedElement; // Skip — nothing changed
        
        var element = Text($"Hello, {Props.Name}!").FontSize(Props.Size);
        _cachedElement = element;
        return element;
    }
}
```

### Rough Definition

- **Phase 1 (Analyzer):** A Roslyn analyzer that detects hook calls (`UseState`, `UseReducer`, `UseEffect`, `UseMemo`, `UseCallback`, `UseRef`) inside conditionals, loops, try/catch, or after early returns, and reports a compile-time error. This requires no language change — it's a diagnostic.
- **Phase 2 (Compiler transform):** A source generator or compiler plugin that rewrites `Render()` methods on `Component` subclasses to add a `$changed` bitmask tracking which props/state slots changed since last render. When nothing changed, the framework skips the render entirely. This is similar to how `async` rewrites methods into state machines.
- Phase 1 is achievable today with a Roslyn analyzer. Phase 2 would benefit from a formalized compiler plugin API (see Result Builders interaction).

### Why Not

**Phase 1 doesn't need a language change at all.** A Roslyn analyzer can detect conditional/loop hook calls today — no proposal, no LDM approval, no language spec change required. If the main value is catching hook ordering bugs, we should just build the analyzer. Framing it as a "language improvement" overstates what's needed.

**Phase 2 creates the Kotlin version compatibility nightmare.** Before Kotlin 2.0, the Compose compiler plugin created a "triangle of doom" — three interdependent versions (Kotlin, Compose, Compose Compiler) that had to be kept in exact lockstep. Upgrading Kotlin by a patch version (e.g., 1.9.21 to 1.9.22) would break Compose with errors like *"This version (1.5.4) of the Compose Compiler requires Kotlin version 1.9.21 but you appear to be using Kotlin version 1.9.22 which is not known to be compatible."* Developers had to consult a compatibility matrix for every upgrade. This was bad enough that Google eventually merged the Compose compiler into the Kotlin repository in 2.0 ([Google Blog](https://android-developers.googleblog.com/2024/04/jetpack-compose-compiler-moving-to-kotlin-repository.html)). C# has no compiler plugin API, and the Roslyn team has deliberately kept compilation deterministic and closed to plugins.

**The function coloring problem.** `@Composable` in Kotlin creates a two-world system: you cannot call `@Composable` functions from non-composable functions. The error "@Composable invocations can only happen from the context of a @Composable function" is one of the most common Compose errors. This mirrors the `async/await` "colored function" problem ([Bob Nystrom](https://journal.stuffwithstuff.com/2015/02/01/what-color-is-your-function/)). C# already has one colored function system (`async`). Adding a second (`[Render]`) means developers now have two axes of function coloring to reason about, with potential interactions between them. Functions could end up annotated with `[Render]`, `async`, and other attributes, creating a combinatorial explosion.

**Stability inference is a footgun.** The Compose compiler infers whether types are "stable" at compile time to enable skip optimization. When it guesses wrong, composables recompose unnecessarily, causing performance problems that are invisible and hard to diagnose. Concrete issues: all standard `List` types are treated as unstable (forcing unnecessary recomposition), data classes from non-Compose modules are unstable (because the plugin only runs within Compose-enabled modules), and lambdas capturing unstable types break skipping ([Stitch Fix: Gotchas in Compose Recomposition](https://multithreaded.stitchfix.com/blog/2022/08/05/jetpack-compose-recomposition/)). Compose 2.0.0 had a bug where it incorrectly inferred stability in multiplatform projects, causing "unnecessary or even endless recompositions."

**C# source generators cannot transform method signatures.** Unlike Kotlin's compiler plugin, C# source generators can only generate new code — they cannot modify existing method signatures to inject hidden `$composer` and `$changed` parameters. Achieving Phase 2 would require either a new compiler plugin API (which the Roslyn team has resisted) or a fundamentally different approach. The gap between "analyzer that warns" and "compiler that transforms" is enormous.

**The benefit may not justify the magic.** Reactor's virtual element tree is already lightweight — Render() produces immutable records that are cheap to diff. The Compose-style skip optimization matters most when rendering is expensive (Compose creates real Android Views). For Reactor, where the reconciler already efficiently diffs elements, the performance benefit of skipping Render() entirely may be marginal compared to the debugging complexity of invisible compiler transforms.

### Expanded Use Cases

All of these are "colored function" problems — a hidden parameter or context that must propagate through call chains. C#'s `async/await` proved the pattern works at scale; extending it to other domains is the natural next step.

**Automatic memoization / caching.** The compiler rewrites pure functions to cache results by arguments, similar to how Compose skips recomposition when inputs haven't changed. Facebook's **Relay compiler** memoizes GraphQL fragment reads. **Salsa** (used in rust-analyzer) is an incremental computation framework built on function memoization with automatic cache invalidation.

```csharp
[Memoize]
int Fib(int n) => n <= 1 ? n : Fib(n - 1) + Fib(n - 2);

// Compiler transforms to:
int Fib(int n, MemoCache __cache) {
    if (__cache.TryGet(n, out var cached)) return cached;
    var result = n <= 1 ? n : Fib(n - 1, __cache) + Fib(n - 2, __cache);
    return __cache.Set(n, result);
}
```

**Distributed tracing / context propagation.** Inject correlation IDs and span context through call chains without manual threading. Go's context propagation problem (the infamous `ctx context.Context` first parameter everywhere) is exactly what this solves. **OpenTelemetry** Java agent uses bytecode weaving to do this at runtime; a compile-time transform would be zero-overhead. Kotlin's **coroutine context** is compiler-propagated via the hidden `Continuation` parameter.

```csharp
[Traced]
Order ProcessOrder(OrderRequest req) {
    var validated = Validate(req);
    var charged = ChargePay(validated);
    return Fulfill(charged);
}

// Compiler rewrites calls to:
Order ProcessOrder(OrderRequest req, TraceContext __ctx) {
    using var __span = __ctx.StartSpan("ProcessOrder");
    var validated = Validate(req, __ctx);
    var charged = ChargePay(validated, __ctx);
    return Fulfill(charged, __ctx);
}
```

**Transaction management.** Spring's `@Transactional` does this via runtime proxies (with well-known pitfalls around self-calls). A compiler transform eliminates the proxy overhead and fixes the self-call problem since the transform is applied to the method body itself. **Arrow Meta** (Kotlin) explored compile-time injection of this pattern.

```csharp
[Transactional]
void TransferFunds(Account from, Account to, decimal amount) {
    from.Debit(amount);
    to.Credit(amount);  // if this throws, Debit auto-rolls back
}
```

**Algebraic effects / effect systems.** The compiler rewrites effectful functions to thread effect handlers, like `async/await` but generalized to any effect (IO, logging, randomness). **Unison** language compiles all functions this way. **Koka** language has native algebraic effects with compiler transforms. This is arguably the most powerful generalization — `async/await` is just one specific effect (asynchrony).

```csharp
[Effectful<Log, Database>]
string GetUserName(int id) {
    Log.Info($"Looking up {id}");
    var user = Database.Query<User>(id);
    return user.Name;
}

// Enables pure testing:
var name = GetUserName(42)
    .Handle(new TestLog(), new InMemoryDb(testData));
```

**Incremental computation.** The compiler tracks dependencies; when one input changes, only affected computations re-execute. **Salsa** (Rust) and **Adapton** are the canonical examples. **Skip** (Facebook's language) had compiler-native incremental computation. Jetpack Compose itself is an incremental computation framework wearing a UI hat.

```csharp
[Incremental]
Report BuildReport(IEnumerable<Transaction> txns) {
    var filtered = txns.Where(t => t.Amount > 100);
    var grouped = filtered.GroupBy(t => t.Category);
    return new Report(grouped.Select(Summarize));
}
// When one Transaction changes, only affected groups re-summarize.
```

**Contract verification.** **Code Contracts** for .NET (Microsoft Research, now defunct) did exactly this via IL rewriting. **Spec#** was a research C# extension. Kotlin **contracts** are compiler-understood.

```csharp
[Requires(nameof(amount) + " > 0")]
[Ensures("Balance >= 0")]
void Withdraw(decimal amount) {
    Balance -= amount;
}
// Compiler injects runtime checks + generates static analysis hints
```

The strongest argument comes from Kotlin's ecosystem: `kotlinx.serialization`, Compose, and coroutines are all compiler plugins, and they are the language's most popular features.

---

## 5. Collection Initializer Trees — B Tier

**Inspiration:** C# collection initializers, LINQ to XML `XElement` nesting, Flutter widget constructors  
**Prior art:** [dotnet/csharplang#6602](https://github.com/dotnet/csharplang/discussions/6602) (Factory method initializers), [#9528](https://github.com/dotnet/csharplang/discussions/9528) (Init-parameters), [#5654](https://github.com/dotnet/csharplang/discussions/5654) (Initializer blocks for UI composition), [#1689](https://github.com/dotnet/csharplang/issues/1689) (Builder-based initialization)

### Problem

Reactor builds element trees with nested function calls closed by `)`. Deep nesting produces a wall of closing parentheses — `), ), )` — that is hard to match visually. C# developers are more accustomed to `{ }` for nested scopes. The language already has a mechanism for adding items to a container — collection initializers — but it only works with `new`, not with factory methods.

### What Works Today (with `new`)

C# collection initializers already enable tree-like nesting if the types implement `IEnumerable` and have `Add()`:

```csharp
// This compiles TODAY — collection initializer pattern
return new VStack(16) {
    new Heading("Registration Form"),
    new VStack(8) {
        new Text("Name"),
        new TextField { Value = name, OnChanged = setName, Placeholder = "Enter your name", Width = 300 }
    },
    new VStack(8) {
        new Text("Email"),
        new TextField { Value = email, OnChanged = setEmail, Placeholder = "you@example.com", Width = 300 }
    },
    new CheckBox { IsChecked = agreeToTerms, OnChanged = setAgree, Label = "I agree to the terms" },
    isValid ? null : new Text("Please fill all fields") { Opacity = 0.6 },
    new Button("Submit") { OnClick = () => setSubmitted(true), Disabled = !isValid }
};
```

Properties and collection items coexist naturally in the same `{ }` — the compiler uses `PropertyName = value` for object initializer assignments and bare expressions for `Add()` calls. This is [documented behavior](https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/classes-and-structs/object-and-collection-initializers).

The hierarchy is visually clear: `{ }` for children, properties set inline, nesting matches the tree structure. The `}` closings align better than `)` closings for C# developers used to block-scoped code.

### The Key Gap: Factory Methods

The `new` keyword is noisy. Reactor's existing DSL uses factory methods via `using static Reactor.UI` — `VStack(16, ...)` instead of `new VStackElement(16)`. But **collection initializers only work after `new` expressions**, not factory method calls:

```csharp
// THIS DOES NOT COMPILE — VStack() is a method call, not a constructor
return VStack(16) {
    Heading("Registration Form"),
    VStack(8) {
        Text("Name"),
        TextField(name, setName, placeholder: "Enter your name").Width(300)
    },
    Button("Submit", () => setSubmitted(true)).Disabled(!isValid)
};
```

This is the single most impactful version of this syntax — clean, no `new`, `{ }` for children, factory methods for elements. But C# doesn't allow it.

### Proposed: Initializer Syntax After Factory Methods

The language change needed: allow object/collection initializer syntax `{ }` after any expression that returns a compatible type, not just `new` expressions.

```csharp
// Proposed — factory methods + collection initializer
return VStack(16) {
    Heading("Registration Form"),
    VStack(8) {
        Text("Name"),
        TextField(name, setName) { Placeholder = "Enter your name", Width = 300 }
    },
    VStack(8) {
        Text("Email"),
        TextField(email, setEmail) { Placeholder = "you@example.com", Width = 300 }
    },
    CheckBox(agreeToTerms, setAgree) { Label = "I agree to the terms" },
    isValid ? null : Text("Please fill all fields") { Opacity = 0.6 },
    Button("Submit", () => setSubmitted(true)) { Disabled = !isValid }
};
```

**Translation:** `VStack(16) { X, Y }` desugars to:

```csharp
var __tmp = VStack(16);  // call factory method
__tmp.Add(X);            // collection initializer Add()
__tmp.Add(Y);
// __tmp is the expression's value
```

And `TextField(name, setName) { Placeholder = "Enter your name" }` desugars to:

```csharp
var __tmp = TextField(name, setName);  // call factory method
__tmp.Placeholder = "Enter your name"; // object initializer property set
// __tmp is the expression's value
```

### Alternative: `with` Keyword for Disambiguation

One csharplang variant ([#6602](https://github.com/dotnet/csharplang/discussions/6602)) proposes using `with` to avoid the `{ }` parsing ambiguity:

```csharp
return VStack(16) with {
    Heading("Registration Form"),
    VStack(8) with {
        Text("Name"),
        TextField(name, setName) with { Placeholder = "Enter your name", Width = 300 }
    },
    Button("Submit", () => setSubmitted(true)) with { Disabled = !isValid }
};
```

The `with` keyword is already used for record copy-mutation (`record with { Prop = val }`), so this extends an existing concept. It eliminates the `Foo() { ... }` ambiguity entirely — `with { }` is unambiguously an initializer.

### Rough Definition

- **Collection/object initializer syntax** is extended to apply after any expression whose type supports it, not only `new` expressions.
- If the expression's type implements `IEnumerable` and has `Add(T)`, bare expressions in `{ }` are collection items.
- If the expression's type has settable properties, `PropertyName = value` entries in `{ }` are object initializer assignments.
- Both can coexist in the same `{ }` block (as they already do with `new`).
- If parsing ambiguity arises (see Why Not), the `with` keyword can be required for disambiguation.
- For immutable element trees, a `[CollectionBuilder]` attribute (like C# 12 collection expressions) or a builder pattern could avoid the `IEnumerable` + `Add()` requirement.

### Why Not

**The `{ }` ambiguity is the same fundamental problem as trailing lambdas.** `Foo() { ... }` at statement level: is it a method call followed by a statement block, or a method call with a collection initializer? The C# language design team has flagged this as a fundamental syntactic conflict. This is why the proposals ([#6602](https://github.com/dotnet/csharplang/discussions/6602), [#9528](https://github.com/dotnet/csharplang/discussions/9528), [#5654](https://github.com/dotnet/csharplang/discussions/5654)) have been open since 2020+ without resolution. The `with` keyword variant solves the ambiguity but adds visual noise.

**`IEnumerable` + `Add()` is a poor fit for immutable element trees.** Collection initializers call `Add()` sequentially on a mutable object. For Reactor's immutable element model, each `Add()` would need to return a new object (wasteful O(n²) copying) or the type must use a mutable builder internally. [This analysis](https://smellegantcode.wordpress.com/2009/01/29/using-collection-initializers-with-immutable-lists/) documents the cost. Reactor would likely need internal mutability behind an immutable façade, or a `[CollectionBuilder]` pattern that collects items into a `ReadOnlySpan` before constructing the element.

**Properties and children interleaved in `{ }` can be confusing.** `Panel { Margin = 4, Label("hi"), Background = "red", Button("OK") }` interleaves property assignments and collection adds. While the compiler can disambiguate, human readers may find it hard to scan. A convention of "properties first, then children" helps but isn't enforced.

**Factory method initializers have been deferred for 6+ years.** The LDM [discussed factory initializers](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-04-20.md) in April-May 2020 and deferred. No champion has emerged. The proposals remain community discussions, not championed issues.

**Closing `}` vs `)` may not actually solve the readability problem.** Deep nesting with `}` is no more readable than `)` per se. The real readability gain in Kotlin/Swift comes from the trailing lambda being the *last* argument, visually separating configuration from children. Collection initializers don't enforce this separation — properties and children mix freely.

**No major .NET UI framework uses this pattern.** WPF, MAUI, Avalonia, Blazor — none use collection initializers for element trees. [Avalonia.Markup.Declarative](https://github.com/AvaloniaUI/Avalonia.Markup.Declarative) uses fluent method chaining instead. This isn't proof it won't work, but it means there's no ecosystem precedent to build on.

### Expanded Use Cases

Collection initializer trees are useful anywhere code constructs hierarchical data structures — not just UI.

**HTML/XML document building.** Cleaner than LINQ to XML's constructor nesting.

```csharp
var page = Html() {
    Head() {
        Title("My Page"),
        Meta() { Charset = "utf-8" }
    },
    Body() {
        H1("Welcome"),
        Div() { ClassName = "content",
            P("Hello, world!"),
            A("Click here") { Href = "/next" }
        }
    }
};
```

**File system tree construction.** Test setup, scaffolding tools, virtual file systems.

```csharp
var project = Directory("src") {
    File("Program.cs", content: mainCode),
    Directory("Models") {
        File("User.cs", content: userModel),
        File("Order.cs", content: orderModel)
    },
    Directory("Tests") {
        File("UserTests.cs", content: tests)
    }
};
```

**Menu / navigation structures.**

```csharp
var menu = Menu("File") {
    MenuItem("New", onNew),
    MenuItem("Open", onOpen),
    Separator(),
    SubMenu("Recent") {
        MenuItem("doc1.txt", () => Open("doc1.txt")),
        MenuItem("doc2.txt", () => Open("doc2.txt"))
    },
    MenuItem("Exit", onExit)
};
```

**AST construction.** Compiler and interpreter trees.

```csharp
var ast = BinaryExpr("+") {
    Literal(1),
    Call("multiply") {
        Literal(2),
        Variable("x")
    }
};
```

**Configuration hierarchies.**

```csharp
var config = AppConfig("myapp") {
    Section("database") {
        Setting("host", "localhost"),
        Setting("port", 5432)
    },
    Section("logging") {
        Setting("level", "info"),
        Setting("format", "json")
    }
};
```

**Prior art:** LINQ to XML (`XElement` nesting) is the closest existing .NET pattern. Flutter uses pure constructor nesting with named `children` parameters. Kotlin's type-safe builders achieve the same visual result via trailing lambdas + receivers. The [#6602](https://github.com/dotnet/csharplang/discussions/6602), [#9528](https://github.com/dotnet/csharplang/discussions/9528), and [#5654](https://github.com/dotnet/csharplang/discussions/5654) discussions have multiple community members requesting this for UI scenarios.

---

## 6. Trailing Lambdas — B Tier

**Inspiration:** Kotlin trailing lambdas, Swift trailing closures  
**Prior art:** [dotnet/csharplang#2781](https://github.com/dotnet/csharplang/issues/2781), [#6122](https://github.com/dotnet/csharplang/discussions/6122), [#9669](https://github.com/dotnet/csharplang/discussions/9669)

### Problem

UI frameworks frequently pass child-building lambdas, slot lambdas, and event callbacks. In C#, every lambda must appear inside parentheses, creating deeply nested `), ),` closing sequences that obscure tree structure.

### Current Reactor Syntax

```csharp
// Component with children as params array — not too bad
VStack(12,
    Text("Hello"),
    Button("Click", () => setCount(count + 1))
)

// But slot patterns get ugly fast
Scaffold(
    header: () => HStack(8,
        Text("My App").Bold(),
        Button("Menu", () => setOpen(true))
    ),
    footer: () => Text("v1.0"),
    content: () => VStack(12,
        Text("Main content"),
        items.Select(i => Card(i)).ToArray()
    )
)

// ForEach with builder lambda
ForEach(items, item =>
    HStack(8,
        Text(item.Name),
        Button("Delete", () => remove(item))
    )
)
```

### Proposed Syntax

```csharp
// Trailing lambda for last parameter
VStack(12) {
    Text("Hello"),
    Button("Click") { setCount(count + 1) }
}

// Named slot parameters + trailing lambda for content (last param)
Scaffold(
    header: {
        HStack(8) {
            Text("My App").Bold(),
            Button("Menu") { setOpen(true) }
        }
    },
    footer: { Text("v1.0") }
) {
    VStack(12) {
        Text("Main content"),
        items.Select(i => Card(i)).ToArray()
    }
}

// ForEach with trailing lambda
ForEach(items) { item =>
    HStack(8) {
        Text(item.Name),
        Button("Delete") { remove(item) }
    }
}
```

### Rough Definition

- When the **last parameter** of a method is a delegate type (`Action`, `Func<>`, or compatible), the argument may be written as a brace-delimited block **after** the closing parenthesis.
- If the trailing lambda is the **only argument**, parentheses may be omitted: `Button("Go") { doThing() }` but also `Box { Text("hi") }`.
- For **named parameters** of delegate type, a brace block can replace the lambda expression: `header: { ... }` is equivalent to `header: () => ...`.
- Inside the trailing block, `=>` after parameters gives the lambda's parameter list: `{ item => ... }`.
- Single-expression trailing lambdas do not need `return`: the last expression is the return value.
- This interacts naturally with `params` — a trailing lambda for `params Element?[] children` would be a block whose comma-separated expressions form the array.

### Interaction with Markup Expressions

If both features ship, markup expressions handle the tree structure and trailing lambdas handle slot/callback ergonomics. They are complementary but either alone would be a significant improvement. If only one ships, trailing lambdas have broader utility beyond UI.

### Why Not

**Fatal ambiguity with braces.** C# already uses `{}` for object initializers, collection initializers, collection expressions, statement blocks, property accessors, and lambda bodies. A trailing `{}` after a method call is syntactically ambiguous with collection initializers — `Foo(x) { ... }` could be constructing `Foo(x)` and then initializing a collection, or passing a trailing lambda. The C# language design team has flagged this as a fundamental syntactic conflict in the language, not merely a tooling inconvenience. Resolving it would require either a new delimiter (ugly) or complex contextual disambiguation rules (fragile).

**The parentheses placement trap.** Kotlin's experience reveals a real pitfall: when a function has multiple lambda parameters with defaults, whether the lambda is inside or outside parentheses changes which parameter receives it. `runBlocks({ println("X") })` passes to `block1`, but `runBlocks { println("X") }` passes to `block2`. This is "quite misleading for people reading the code" ([Kotlin Discussions](https://discuss.kotlinlang.org/t/a-pitfall-with-multiple-default-function-arguments-and-passing-lambdas/19535)). C# would inherit this confusion.

**SAM conversion ambiguity.** When methods have overloads accepting different delegate types (e.g., `Action` vs `Func<Task>`), trailing lambda syntax compounds existing overload resolution complexity. Kotlin users regularly hit this in Java interop ([assertj#2357](https://github.com/assertj/assertj/issues/2357)).

**LDM has repeatedly declined to champion this.** The proposal has been raised at least three times ([#2781](https://github.com/dotnet/csharplang/issues/2781), [#6122](https://github.com/dotnet/csharplang/discussions/6122), [#9669](https://github.com/dotnet/csharplang/discussions/9669)) over multiple years. The LDM has consistently concluded "not enough motivation relative to the complexity and ambiguity costs." No team member has volunteered to champion it, which in the C# design process is a strong signal against adoption.

**Limited payoff in practice.** Unlike Kotlin, where trailing lambdas enable the entire DSL builder pattern that Compose is built on, C# already has LINQ query syntax, expression-bodied members, and `params` arrays that reduce the pressure. Reactor's existing `VStack(12, Text("A"), Text("B"))` syntax with `params Element?[]` is already quite clean — the trailing lambda version `VStack(12) { Text("A"), Text("B") }` is only marginally better for the common case, while adding grammar complexity that every C# developer must learn.

**Nested trailing lambdas degrade readability.** In deeply nested DSLs, nested trailing lambdas with implicit `it` parameters and scope leakage from outer receivers create confusing code. Kotlin had to add `@DslMarker` after the fact to mitigate this, and the problem persists in practice.

**Every C# developer pays the complexity cost.** Trailing lambdas primarily benefit UI DSL builders — a small fraction of C# usage. Server developers, library authors, and CLI tool writers gain nothing but must understand the syntax when reading code. The C# team has historically been conservative about features that serve a narrow audience at the expense of language simplicity.

### Expanded Use Cases

The "narrow audience" argument against trailing lambdas is substantially weakened by Kotlin's ecosystem, where the feature's highest-impact uses are outside UI. Gradle, Ktor, Kotest, and coroutine scoping are all non-UI and affect every Kotlin developer daily.

**Testing DSLs (assertion builders).** Kotest uses trailing lambdas for soft assertions and collection inspectors. The block-after-call syntax makes test setup read like a specification.

```csharp
// Current C#
assertSoftly(result, r => {
    r.Name.Should().Be("Alice");
    r.Age.Should().BeGreaterThan(18);
});

// Proposed
assertSoftly(result) {
    it.Name.Should().Be("Alice");
    it.Age.Should().BeGreaterThan(18);
}
```

**Real projects:** [Kotest](https://kotest.io/docs/assertions/soft-assertions.html), Swift Testing's `#expect(throws:)` with trailing closures.

**Builder patterns (HTTP clients, routing, configuration).** Ktor's routing and server configuration are the canonical example. Gradle's entire Kotlin DSL is built on trailing lambdas.

```csharp
// Current C#
app.MapGroup("/api", group => {
    group.MapGet("/users", ctx => { /* ... */ });
    group.MapPost("/users", ctx => { /* ... */ });
});

// Proposed
app.MapGroup("/api") {
    MapGet("/users") { /* ... */ }
    MapPost("/users") { /* ... */ }
}
```

**Real projects:** [Ktor routing DSL](https://ktor.io/), [Gradle Kotlin DSL](https://docs.gradle.org/current/userguide/kotlin_dsl.html), [Spring Integration Kotlin DSL](https://docs.spring.io/spring-integration/reference/kotlin-dsl.html).

**Resource management (scoped cleanup).** Kotlin's `use` and scoping functions work this way. The trailing lambda naturally scopes the lifetime of a resource.

```csharp
// Current C#
Retry(3, async () => { await httpClient.GetAsync(url); });

// Proposed
Retry(3) { await httpClient.GetAsync(url); }
```

**Structured concurrency.** Kotlin's `coroutineScope`, `runBlocking`, and `supervisorScope` all use trailing lambdas as their primary API. The pattern is so central that structured concurrency is essentially impossible to express cleanly without it.

```csharp
// Current C#
await Parallel.ForEachAsync(items, async (item, ct) => { await Process(item); });

// Proposed
await Parallel.ForEachAsync(items) { await Process(it.Item); }
```

**Real projects:** [Kotlin coroutines](https://kotlinlang.org/docs/coroutines-basics.html) — `runBlocking {}`, `coroutineScope {}`, `withContext(Dispatchers.IO) {}` are all trailing lambda calls.

**Collection operations.** The most-cited example in [csharplang #6122](https://github.com/dotnet/csharplang/discussions/6122). RemObjects' C# compiler already supports this syntax.

```csharp
// Current C#
var adults = people.Where(p => p.Age >= 18).Select(p => p.Name).ToList();

// Proposed
var adults = people.Where { it.Age >= 18 }.Select { it.Name }.ToList();
```

**Critical gap noted in csharplang discussions:** The C# team's ask for a "UI framework prototype" may undervalue the non-UI cases. Gradle, Ktor, Kotest, and coroutine scoping are arguably stronger justifications since they affect every Kotlin developer daily, not just UI authors.

---

## 7. Result Builders — B Tier

**Inspiration:** Swift `@resultBuilder` / `@ViewBuilder`  
**Prior art:** [dotnet/csharplang#9243](https://github.com/dotnet/csharplang/issues/9243) (Expression Blocks, championed 2025), [Swift Result Builders](https://docs.swift.org/swift-book/documentation/the-swift-programming-language/advancedoperators/#Result-Builders)

### Problem

Reactor uses `params Element?[]` to collect children. This works but has limitations: `if/else` can't appear inline (must use ternary or `When()`), `foreach` can't appear inline (must use `.Select().ToArray()`), and early returns from a child block are impossible. The developer must think in expressions, not statements.

### Current Reactor Syntax

```csharp
return VStack(12,
    Heading("Dashboard"),
    
    // Conditional: must use ternary or When()
    isAdmin
        ? Button("Admin Panel", () => navigate("/admin"))
        : null,
    
    // Must use When() helper for conditional with side-effect-free builder
    When(hasNotifications, () => Badge("3 new")),
    
    // List: must use Select + ToArray
    items.Select(item => 
        Card(item.Title, item.Description)
    ).ToArray(),
    
    // Nested conditional is awkward
    status switch
    {
        Status.Loading => Spinner(),
        Status.Error => Text("Error").Foreground("red"),
        Status.Ok => Text("All good"),
        _ => Empty()
    }
);
```

### Proposed Syntax

```csharp
[ElementBuilder]
public override Element Render()
{
    var (isAdmin, _) = UseState(true);

    Heading("Dashboard");
    
    // if/else just works — compiler calls buildOptional / buildEither
    if (isAdmin)
        Button("Admin Panel", () => navigate("/admin"));
    
    if (hasNotifications)
        Badge("3 new");

    // for/foreach just works — compiler calls buildArray
    foreach (var item in items)
        Card(item.Title, item.Description);
    
    // switch just works
    switch (status)
    {
        case Status.Loading: Spinner(); break;
        case Status.Error: Text("Error").Foreground("red"); break;
        case Status.Ok: Text("All good"); break;
    }
}
```

### Rough Definition

- A **result builder** is a type annotated with `[ResultBuilder]` that implements static methods: `BuildBlock(params T[] items)`, `BuildOptional(T? item)`, `BuildEither(bool condition, T first, T second)`, `BuildArray(T[] items)`.
- A method or lambda annotated with the result builder attribute (e.g., `[ElementBuilder]`) has its body transformed: expression statements are collected, `if/else` is transformed to `BuildEither`/`BuildOptional`, `foreach` to `BuildArray`, etc.
- The result builder for Reactor would be `ElementBuilder` that collects `Element` values and produces a parent container (or fragment).
- Local variable declarations, `using` statements, and `return` work normally — only bare expression statements are captured.
- This is a compile-time transformation (like `async/await` state machines), not a runtime concept.

### Tradeoff

Result builders are powerful but add cognitive complexity — the code *looks* like imperative statements but is really a declarative builder. Developers familiar with SwiftUI find this natural; developers coming from traditional C# may find it confusing. Reactor's current `params` approach is simple and explicit. This proposal is most valuable if combined with trailing lambdas for building child blocks.

### Why Not

**SwiftUI's result builders are the #1 source of developer pain.** The single most common SwiftUI compiler error is: *"The compiler is unable to type-check this expression in reasonable time; try breaking up the expression into distinct sub-expressions."* This fires on moderately complex views, and the only fix is to manually extract sub-views — defeating the purpose of having a builder DSL. The Swift compiler's type-checker has exponential time complexity when resolving overloaded operators inside result builder bodies. A 12-line string concatenation inside a builder can take **42 seconds** to compile on an M1 Pro ([Daniel Hooper: Why Swift's Type Checker Is So Slow](https://danielchasehooper.com/posts/why-swift-is-slow/)). C# would need to solve this before shipping or inherit the same problem.

**Error messages become incomprehensible.** When anything goes wrong inside a builder body, the compiler cannot pinpoint which line failed. Instead it reports that the **entire block** cannot be type-checked. Swift developers describe debugging as "commenting out items one at a time until the compiler identifies the issue" — binary search by hand. The Swift Forums have extensive threads on this pain ([resultBuilder debugging pain](https://forums.swift.org/t/resultbuilder-debugging-pain/76275), [Troubleshooting result builders](https://forums.swift.org/t/troubleshooting-result-builders/60797)).

**Code that looks like statements but isn't.** A result builder body looks like imperative code — `if`, `for`, expression statements — but has completely different semantics. `Text("Hello");` as an expression statement normally discards the value, but in a builder it captures it. This violates C#'s existing semantics for expression statements and would confuse developers who expect familiar syntax to have familiar behavior. The mental model shift from "statements execute in order" to "statements are collected by a builder" is genuinely hard.

**The 10-view limit problem.** Swift's `ViewBuilder` historically limited containers to 10 children because it used `TupleView` overloads with up to 10 generic parameters. Exceeding this required wrapping in `Group`. Swift 5.7's `buildPartialBlock` theoretically fixes this, but the limitation demonstrates how result builders impose surprising artificial constraints that leak implementation details.

**C# already has `params` and collection expressions.** Reactor's `VStack(12, Text("A"), isAdmin ? Text("Admin") : null, Text("B"))` works today. The ternary for conditionals and `.Select().ToArray()` for lists are well-understood C# patterns. Result builders trade familiar idioms for magic syntax that requires learning a new mental model. The incremental improvement over `params` may not justify the complexity.

**No formal C# proposal exists.** The C# LDM has not discussed Swift-style result builders as a feature. The closest related proposal ([#9243: Expression Blocks](https://github.com/dotnet/csharplang/issues/9243)) is much more conservative. The team's general philosophy has been to avoid features that transform method body semantics in ways that are invisible to the reader. `async/await` was the exception, and even that was controversial.

**Interaction with existing C# features is underspecified.** How do result builders interact with `ref` returns, `await`, `yield return` (already a keyword!), `using` statements, definite assignment analysis, and nullable flow analysis? Each interaction multiplies design surface area. Swift has been iterating on result builder edge cases for 5+ years and still has open issues.

### Expanded Use Cases

The strongest evidence that result builders are general-purpose: Apple shipped regex composition ([SE-0351](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0351-regex-builder.md)) as a result builder in the Swift standard library. The [awesome-result-builders](https://github.com/carson-katri/awesome-result-builders) catalog lists 50+ non-UI Swift libraries spanning GraphQL, HTML, networking, testing, validation, CSS, and code generation.

**HTML / document generation.** Swift's [Plot](https://github.com/JohnSundell/Plot) (powers swiftbysundell.com) and [swift-html](https://github.com/binarybirds/swift-html) use result builders to compose HTML as typed node trees with full `if`/`foreach` support.

```csharp
[HtmlBuilder]
static HtmlNode RenderPage(string title, List<Post> posts) {
    Html();
    Head(Meta(charset: "utf-8"), Title(title));
    Body(H1(title));
    foreach (var p in posts)
        Article(H2(p.Title), P(p.Summary));
}
```

**Regular expression composition.** Apple shipped this in the standard library as [SE-0351](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0351-regex-builder.md) — the strongest proof that result builders are general-purpose. `RegexComponentBuilder` concatenates patterns with full type-safe captures.

```csharp
[RegexBuilder]
static Regex<(string date, string user)> LogPattern() {
    Literal("[");
    Capture<string>(OneOrMore(Digit()), name: "date");
    Literal("] ");
    Capture<string>(OneOrMore(Word()), name: "user");
    Literal(": ");
    OneOrMore(Any());
}
```

**SQL query builders.** Eliminates string concatenation and injection risk. The `if` support is the key differentiator over fluent builder APIs — conditionally adding `WHERE` clauses reads naturally.

```csharp
[QueryBuilder]
static Query GetActiveUsers(int minAge) {
    Select("id", "name", "email");
    From("users");
    Where("active = true");
    if (minAge > 0)
        Where($"age >= {minAge}");
    OrderBy("name");
    Limit(50);
}
```

**State machine / workflow definitions.** Declarative transition tables are a natural tree structure where conditional transitions (enabled only in certain configurations) benefit from `if` support.

```csharp
[StateMachineBuilder]
static StateMachine<OrderState> DefineOrderFlow() {
    State(OrderState.Created);
    Transition(OrderState.Created, OrderEvent.Pay, OrderState.Paid);
    Transition(OrderState.Paid, OrderEvent.Ship, OrderState.Shipped);
    Transition(OrderState.Shipped, OrderEvent.Deliver, OrderState.Delivered);
    if (allowCancellation)
        Transition(OrderState.Created, OrderEvent.Cancel, OrderState.Cancelled);
}
```

**Test case generation.** Builders let `if`/`foreach` generate test rows conditionally, which fluent APIs cannot do cleanly.

```csharp
[TestCaseBuilder]
static IEnumerable<TestCase> DiscountTests() {
    TestCase("No discount", price: 100, discount: 0, expected: 100);
    TestCase("10% off", price: 100, discount: 10, expected: 90);
    foreach (var edge in new[] { 0m, 0.01m, 99999m })
        TestCase($"Edge: {edge}", price: edge, discount: 50, expected: edge / 2);
}
```

**Configuration DSLs.** Environment-conditional configuration reads naturally with `if`/`else` instead of ternaries or separate builder methods.

```csharp
[ConfigBuilder]
static AppConfig Configure(IEnvironment env) {
    ConnectionString("Server=db;Database=app");
    if (env.IsDevelopment())
        Logging(LogLevel.Debug);
    else
        Logging(LogLevel.Warning);
    Feature("dark-mode", enabled: true);
    Feature("beta-export", enabled: env.HasFlag("BETA"));
}
```

**GraphQL schema builders.** [Artemis](https://github.com/nicklockwood/Artemis), [SociableWeaver](https://github.com/nicklockwood/SociableWeaver), and [Graphiti](https://github.com/GraphQLSwift/Graphiti) all use result builders for type-safe GraphQL schema and query construction.

```csharp
[SchemaBuilder]
static Schema DefineApi() {
    ObjectType<User>("User",
        Field("id", ScalarType.ID),
        Field("name", ScalarType.String),
        Field("posts", ListOf<Post>()));
    QueryType(
        Resolver<User>("user", args: Arg<string>("id")),
        Resolver<List<User>>("users"));
}
```

**The [SE-0289 proposal](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0289-result-builders.md)** explicitly frames result builders as targeting "declarative list and tree structures" across problem domains, with SwiftUI as merely the most visible application.

---

## 8. Scoped Extension Receivers — B Tier

**Inspiration:** Kotlin receiver lambdas (`ColumnScope.() -> Unit`), Kotlin `@DslMarker`  
**Prior art:** [dotnet/csharplang#5497](https://github.com/dotnet/csharplang/issues/5497) (Extensions), Kotlin [type-safe builders](https://kotlinlang.org/docs/type-safe-builders.html)

### Problem

In Reactor, all `UI.*` factory methods are available everywhere via `using static`. But some modifiers only make sense in certain contexts — `.Grid(row: 1)` only makes sense inside a `Grid()`, `.Flex(grow: 1)` only makes sense inside a `FlexPanel()`. Today these are available on all elements, and using them in the wrong context silently does nothing.

### Current Reactor Syntax

```csharp
// Grid attached properties — available everywhere, no compile-time scoping
Grid(
    columnDefinitions: "200 *",
    rowDefinitions: "Auto Auto *",
    children: [
        Text("Label").Grid(row: 0, column: 0),
        TextField(value, setValue).Grid(row: 0, column: 1),
        Text("Description").Grid(row: 1, column: 0),
        TextArea(desc, setDesc).Grid(row: 1, column: 1),
        
        // BUG: .Flex() inside Grid — no compiler error, just silently ignored
        Button("Submit").Grid(row: 2, column: 1).Flex(grow: 1)
    ]
)
```

### Proposed Syntax

```csharp
// Children lambda receives a GridScope receiver — only grid-relevant 
// modifiers are in scope
Grid(
    columnDefinitions: "200 *",
    rowDefinitions: "Auto Auto *"
) { grid =>                              // grid is GridScope
    grid.Cell(row: 0, column: 0, Text("Label"));
    grid.Cell(row: 0, column: 1, TextField(value, setValue));
    grid.Cell(row: 1, column: 0, Text("Description"));
    grid.Cell(row: 1, column: 1, TextArea(desc, setDesc));
    
    // COMPILER ERROR: GridScope does not contain Flex()
    grid.Cell(row: 2, column: 1, Button("Submit").Flex(grow: 1));
}

// With Kotlin-style receiver (if C# adds receiver lambdas):
Grid(
    columnDefinitions: "200 *",
    rowDefinitions: "Auto Auto *"
) {
    // 'this' is GridScope — Cell() is directly available
    Cell(row: 0, column: 0, Text("Label"));
    Cell(row: 0, column: 1, TextField(value, setValue));
}
```

### Rough Definition

- A **receiver lambda** is a lambda where `this` refers to a specified type: `Grid(children: GridScope.() => { ... })`.
- Inside the receiver lambda, instance members of the scope type are available without qualification.
- A `[DslScope]` attribute (analogous to Kotlin's `@DslMarker`) prevents accidental use of outer scope receivers — you can't call `Cell()` from a nested `FlexPanel` scope without explicit qualification.
- For Reactor, `GridScope`, `FlexScope`, `CanvasScope` would expose only the layout modifiers relevant to that container.
- **Minimal version:** Even without full receiver lambdas, C# 14's extensions feature could enable extension methods scoped to a type parameter, providing some of the benefit: `extension GridChildren for Element[] { void Cell(...) { } }`.

### Why Not

**C# has no receiver lambda concept and adding one is a deep language change.** Changing what `this` means inside a lambda fundamentally alters C#'s scoping model. In C#, `this` always refers to the enclosing type — this is a foundational assumption that every C# developer relies on, and that tools, analyzers, and debuggers depend on. Receiver lambdas would make `this` context-dependent, breaking 20+ years of consistent behavior.

**The problem is solvable without a language change.** Reactor could simply not expose `.Grid()` and `.Flex()` as extension methods on all elements. Instead, `Grid()` could accept children via a delegate that takes a `GridScope` parameter: `Grid(..., (grid) => [ grid.Cell(0, 0, Text("Hi")) ])`. This is verbose but works today, requires no language change, and provides full compile-time safety. The "silent no-op" problem is a Reactor API design choice, not a language limitation.

**Kotlin needed @DslMarker to fix scope leakage.** Even with receiver lambdas, Kotlin found that nested builders could accidentally access methods from outer scopes — e.g., calling `Row { Text("hi").weight(1f) }` from inside a `Column` scope, where `weight` is a `RowScope` method, not a `ColumnScope` method. Kotlin had to add `@DslMarker` after the fact to prevent this. The feature created a new class of bugs while trying to prevent others.

**Narrow audience.** Scoped receivers primarily benefit DSL authors (UI frameworks, HTML builders, configuration DSLs). The vast majority of C# code — web APIs, business logic, data processing, infrastructure — would never use this feature. The C# team has historically been reluctant to add features with such narrow applicability.

**C# 14 extensions may partially solve this.** The [extensions proposal](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-14.0/extensions.md) being developed for C# 14 allows extension members beyond just methods. While it doesn't provide full receiver lambda semantics, it could enable scoped extension methods that are only available in certain contexts, providing some of the type-safety benefit without a new lambda form.

### Expanded Use Cases

The recurring argument across [dotnet/csharplang #5497](https://github.com/dotnet/csharplang/issues/5497) and related threads: receiver lambdas provide **discoverability via IntelliSense** (you type `.` and see only what's valid in that scope), **compile-time safety** (wrong nesting is a type error), and **reduced syntactic noise** (no repeated variable names). The Gradle Kotlin DSL migration is cited as the strongest existence proof that this pattern scales to millions of users in non-UI contexts.

**HTML builders.** JetBrains' [kotlinx.html](https://github.com/Kotlin/kotlinx.html) uses receiver lambdas so the compiler validates tag nesting — you can't put a `<title>` inside a `<div>`.

```csharp
var page = Html.Build(html => {
    html.Head(head => {
        head.Title("My Page");         // only head-legal tags available
    });
    html.Body(body => {
        body.Div(div => {
            div.P("Hello world");       // P is available on DivScope, not HeadScope
        });
    });
});
```

**Build systems (Gradle Kotlin DSL).** The single highest-profile receiver lambda DSL. Every `build.gradle.kts` file is a receiver lambda on `Project`. Used by every Android project.

```csharp
Project.Configure(project => {
    project.Dependencies(deps => {
        deps.Implementation("Newtonsoft.Json", "13.0.1");
        deps.TestImplementation("xunit", "2.4.1");
    });
    project.Tasks.Register<PublishTask>("publish", task => {
        task.DependsOn("build");
        task.Repository = "https://nuget.org";
    });
});
```

**HTTP client configuration.** [Ktor](https://ktor.io/) uses nested receivers to configure HTTP clients and requests with discoverability — you only see what's relevant at each level.

```csharp
var client = new HttpClientBuilder(client => {
    client.Install<JsonPlugin>(json => {
        json.Serializer = new SystemTextJsonSerializer();
    });
    client.DefaultRequest(req => {
        req.BaseUrl = "https://api.example.com";
        req.Header("Authorization", $"Bearer {token}");
    });
});
```

**Database query DSLs.** JetBrains [Exposed](https://github.com/JetBrains/Exposed) uses receiver lambdas so that inside a query block, columns resolve against the table in scope — preventing accidental cross-table references.

```csharp
var results = Users.Select(row => {
    row.Where(Users.Age > 21);
    row.OrderBy(Users.Name.Asc());
});
```

**Real projects:** JetBrains Exposed, Ktorm, jOOQ Kotlin extensions.

**Testing / assertion scopes.** Inside an assertion scope, the receiver provides matchers. Eliminates repeated `Assert.` prefixes and groups failures for better diagnostics.

```csharp
user.Should(expect => {
    expect.Name.Be("Alice");
    expect.Age.BeGreaterThan(18);
    expect.Roles.Contain("admin");
    // all assertions run; failures reported together
});
```

**Real projects:** [Kotest `assertSoftly`](https://kotest.io/), Strikt, kotlin-test.

**Routing DSLs.** Web framework routing is naturally hierarchical — scoped receivers prevent defining handlers in the wrong route context.

```csharp
app.Routing(route => {
    route.Get("/users", HandleListUsers);
    route.Route("/users/{id}", sub => {
        sub.Get(HandleGetUser);
        sub.Delete(HandleDeleteUser);
    });
});
```

**Real projects:** Ktor routing, http4k, Spring WebFlux Kotlin DSL.

**State machine definitions.** Each state's scope only exposes valid transitions, preventing illegal transition definitions at compile time.

```csharp
var machine = StateMachine.Define<TrafficLight>(sm => {
    sm.State(Green, state => {
        state.OnEnter(() => StartTimer(30));
        state.On(TimerExpired).TransitionTo(Yellow);
    });
    sm.State(Yellow, state => {
        state.On(TimerExpired).TransitionTo(Red);
    });
});
```

**Real projects:** [Tinder StateMachine](https://github.com/Tinder/StateMachine), kstatemachine.

**Protocol / message builders.** Google's official [protobuf-kotlin DSL](https://protobuf.dev/reference/kotlin/kotlin-generated/) uses receiver lambdas so that inside a message builder, only that message's fields are available.

```csharp
var packet = Protobuf.Build<SearchRequest>(msg => {
    msg.Query = "kotlin DSL";
    msg.PageNumber = 1;
    msg.Options(opts => {
        opts.CaseSensitive = false;   // nested message scope
    });
});
```

---

## 9. Property Wrappers — C Tier

**Inspiration:** Swift `@propertyWrapper` (`@State`, `@Binding`, `@Environment`), Kotlin delegated properties (`by remember { mutableStateOf() }`)  
**Prior art:** [dotnet/csharplang#2657](https://github.com/dotnet/csharplang/issues/2657), discussed in [LDM 2020-10-07](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-10-07.md)

### Problem

Reactor's hooks-based state (`UseState`, `UseReducer`) returns tuples that must be destructured. This is ergonomic for simple cases but becomes verbose when a component has many state variables. There's also no compile-time connection between a state value and its setter — they're just two local variables.

### Current Reactor Syntax

```csharp
class FormDemo : Component
{
    public override Element Render()
    {
        // Each state requires a destructured tuple
        var (name, setName) = UseState("");
        var (email, setEmail) = UseState("");
        var (agreeToTerms, setAgree) = UseState(false);
        var (darkMode, setDarkMode) = UseState(false);
        var (fontSize, setFontSize) = UseState(14.0);
        var (submitted, setSubmitted) = UseState(false);

        // 6 state variables = 12 local names to manage
        // Setter names are ad-hoc conventions (setX, updateX, etc.)
        
        return VStack(16,
            TextField(name, setName, placeholder: "Name"),
            ToggleSwitch(darkMode, setDarkMode),
            Slider(fontSize, 10, 30, setFontSize),
            Button("Submit", () => setSubmitted(true))
        );
    }
}
```

### Proposed Syntax

```csharp
class FormDemo : Component
{
    public override Element Render()
    {
        // Property wrapper syntax — compiler generates backing storage + setter
        @State var name = "";
        @State var email = "";
        @State var agreeToTerms = false;
        @State var darkMode = false;
        @State var fontSize = 14.0;
        @State var submitted = false;

        // Read: just use the variable name
        // Write: assign directly — the wrapper intercepts and triggers re-render
        
        return VStack(16,
            TextField(name, v => name = v, placeholder: "Name"),
            ToggleSwitch(darkMode, v => darkMode = v),
            Slider(fontSize, 10, 30, v => fontSize = v),
            Button("Submit", () => submitted = true)
        );
    }
}
```

### Alternate Syntax (field-level, closer to Swift)

```csharp
class FormDemo : Component
{
    // Declared as fields — framework manages lifecycle
    [State] string name = "";
    [State] string email = "";
    [State] bool agreeToTerms;
    [State] double fontSize = 14.0;

    public override Element Render()
    {
        // Direct reads and writes — wrapper handles change notification + re-render
        return VStack(16,
            TextField(name, v => name = v, placeholder: "Name"),
            Slider(fontSize, 10, 30, v => fontSize = v)
        );
    }
}
```

### Rough Definition

- A **property wrapper** is a generic type annotated with `[PropertyWrapper]` that exposes `T WrappedValue { get; set; }` and optionally `TProjected ProjectedValue { get; }`.
- When applied to a variable or field declaration, the compiler generates a backing field of the wrapper type and rewrites reads/writes to go through `WrappedValue`.
- The `$` prefix (or another sigil) accesses the `ProjectedValue` — e.g., `$name` could return a `Binding<string>` for two-way binding to child components.
- For Reactor, `[State]` would wrap `UseState` — the wrapper registers with the component's hook context and triggers re-render on set.
- `[Binding]` would accept a projected value from a parent's `[State]`, enabling child components to modify parent state.
- `[Environment]` would read from a context provider higher in the tree.

### Compatibility with Hooks

Property wrappers would be an alternative syntax for hooks, not a replacement. The tuple-destructuring `UseState` pattern would remain available. Property wrappers would be sugar that the compiler lowers to equivalent hook calls, preserving the hook ordering contract.

### Why Not

**The C# LDM discussed this in 2020 and effectively shelved it.** The [LDM-2020-10-07](https://github.com/dotnet/csharplang/blob/main/meetings/2020/LDM-2020-10-07.md) meeting examined [#2657](https://github.com/dotnet/csharplang/issues/2657) and concluded the feature has a high complexity-to-benefit ratio. The team found that property wrappers interact poorly with `init` accessors, `required` members, nullable reference types, and serialization patterns — each interaction multiplying design surface area. No champion has emerged in the 5+ years since.

**The `field` keyword solves the most common motivation.** C# 13 introduced the `field` keyword for semi-auto properties ([#140](https://github.com/dotnet/csharplang/issues/140)), which addresses the primary pain point — accessing the backing field in a property accessor — with far less language complexity. The LDM explicitly prioritized `field` over property wrappers as the simpler solution.

**Source generators already cover the key scenarios.** CommunityToolkit.Mvvm's `[ObservableProperty]` generates `INotifyPropertyChanged` boilerplate. MAUI's `[BindableProperty]` generators handle dependency properties. These solve the same problem at the library level without permanent language complexity. The C# team noted this overlap when deprioritizing property wrappers.

**SwiftUI's property wrapper confusion is well-documented.** The sheer number of articles titled "Stop Guessing: Here's Exactly When to Use @State vs @StateObject vs @ObservedObject" tells the story. Swift has at least 5 property wrappers for state management (`@State`, `@Binding`, `@StateObject`, `@ObservedObject`, `@EnvironmentObject`), with subtle and catastrophic differences between them. The most common bug: **using `@ObservedObject` when you should use `@StateObject` silently destroys your state** because SwiftUI may recreate views at any time, re-instantiating the wrapper and resetting all data ([SwiftLee: StateObject vs ObservedObject](https://www.avanderlee.com/swiftui/stateobject-observedobject-differences/)). These wrappers look nearly identical but have fundamentally different lifecycle semantics. C# would create the same confusion matrix.

**Assignment semantics become magical.** With property wrappers, `count = 5` no longer means "store 5 in count" — it means "call the wrapper's setter, which may trigger re-renders, validation, network calls, or anything." This violates the principle of least surprise. In Swift, `@Published` properties have a known bug where `didSet` fires twice on iOS 15 when bound to a `TextField`, and property wrapper `didSet` handlers can be re-entrant. These bugs are extremely hard to diagnose because the abstraction hides what's actually happening.

**Reactor's tuple destructuring is already quite ergonomic.** `var (count, setCount) = UseState(0)` is one line that makes both the value and setter explicit. It's a well-understood C# pattern (tuple deconstruction) with no magic. The proposed `@State var count = 0` saves a few characters but hides the setter entirely — how does the framework know to re-render when `count` changes? The explicit tuple makes the reactivity contract visible.

**Hook ordering becomes invisible.** Property wrappers on fields would obscure hook call order — a critical invariant in Reactor's rendering model. With `UseState()` calls in the method body, the ordering is visible and explicit. With `[State]` fields, the ordering depends on field declaration order, which is a much weaker and less obvious contract.

### Expanded Use Cases

Property wrappers have the broadest non-UI adoption of any proposal here. Kotlin's `by lazy`, `by Delegates.observable()`, and `by inject()` are used in virtually every Kotlin project. Swift's [SE-0258 proposal](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0258-property-wrappers.md) explicitly lists validation, persistence, and thread safety as primary non-UI motivations.

**Validation (`@Clamped`, `@NonEmpty`).** Constrain values at the point of declaration. Values are silently pinned to a range on every set. Kotlin's `Delegates.vetoable()` rejects invalid assignments outright.

```csharp
[Clamped(0, 100)] int volume = 50;
volume = 200; // silently becomes 100

[NonEmpty] string username = "default";
username = ""; // throws or reverts
```

**Real projects:** [Burritos](https://github.com/guillermomuntaner/Burritos) Swift library includes `@Clamping`. Kotlin stdlib provides `Delegates.vetoable()`.

**Lazy initialization / caching.** Kotlin's `by lazy {}` is the poster child — thread-safe by default, computed once on first access. Used in virtually every Kotlin codebase.

```csharp
[Lazy] ExpensiveService service; // computed on first read, cached thereafter
```

**Thread safety (`@Atomic`).** Wraps individual reads/writes in a lock or uses atomics. However, the Swift Forums [rejected a stdlib @Atomic pitch](https://forums.swift.org/t/atomic-property-wrapper-for-standard-library/30468) because property wrappers cannot guarantee compound atomicity — `counter += 1` is still a get-then-set race. This is the fundamental limitation.

```csharp
[Atomic] int counter = 0;
// Each read/write is individually locked, but counter++ is NOT atomic
```

**Persistence (`@UserDefault`, `@Keychain`).** Map a property directly to a storage backend. One of the most popular non-UI uses in Swift.

```csharp
[UserDefault("theme")] string theme = "light";
[Keychain("api_token")] string apiToken = "";
// Reads/writes go directly to the backing store
```

**Real projects:** SwiftUI's built-in `@AppStorage`. [SecurePropertyStorage](https://swiftpackageregistry.com/alexruperez/SecurePropertyStorage) (SHA512-hashed keys, AES-GCM encrypted values).

**Dependency injection (`@Injected`).** Resolve a dependency from a container at the point of declaration instead of constructor injection.

```csharp
[Injected] ILogger logger;
[Injected] IUserRepository users;
```

**Real projects:** Kotlin's Koin uses `by inject()`. Swift's [Resolver](https://github.com/hmlongco/Resolver) provides `@Injected`. This pattern trades explicit constructor injection for convenience — controversial in DI circles.

**Logging / auditing (`@Logged`).** Intercept every set and emit a log entry. Useful for debugging or compliance auditing. Kotlin's `Delegates.observable()` is the stdlib version — fires a callback on every change.

```csharp
[Logged] decimal balance = 0m;
balance = 100m; // logs: "balance changed from 0 to 100 at 2026-04-04T..."
```

**Environment variable binding (`@EnvVar`).** Bind a property to a system environment variable with a typed default.

```csharp
[EnvVar("PORT")] int port = 8080;
[EnvVar("DATABASE_URL")] string dbUrl = "localhost:5432";
```

**Feature flags (`@FeatureFlag`).** Read from a remote config service or local toggle file. [SE-0258](https://github.com/swiftlang/swift-evolution/blob/main/proposals/0258-property-wrappers.md) specifically mentions feature flags as a motivating use case.

```csharp
[FeatureFlag("dark-mode-v2")] bool isDarkModeV2 = false;
```

**Encryption / redaction (`@Encrypted`, `@Redacted`).** Encrypt on write, decrypt on read. Redact in logs/serialization. [SecurePropertyStorage](https://swiftpackageregistry.com/alexruperez/SecurePropertyStorage) implements encrypted property wrappers with AES-GCM.

```csharp
[Encrypted(Algorithm.AesGcm)] string ssn = "";
[Redacted] string password = "secret"; // ToString() yields "***"
```

---

## 10. Markup Expressions — C Tier

**Inspiration:** JSX/TSX (React), Kotlin's trailing lambda DSL  
**Prior art:** [dotnet/csharplang#2529](https://github.com/dotnet/csharplang/discussions/2529), [WinUI#7875](https://github.com/microsoft/microsoft-ui-xaml/discussions/7875)

### Problem

Reactor builds element trees with nested function calls. Deep nesting produces a wall of parentheses and commas that is hard to scan visually. The tree structure is obscured by C# call syntax.

### Current Reactor Syntax

```csharp
class FormDemo : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");
        var (email, setEmail) = UseState("");
        var (agreeToTerms, setAgree) = UseState(false);

        var isValid = !string.IsNullOrWhiteSpace(name)
            && !string.IsNullOrWhiteSpace(email)
            && agreeToTerms;

        return VStack(16,
            Heading("Registration Form"),
            VStack(8,
                Text("Name"),
                TextField(name, setName, placeholder: "Enter your name").Width(300)
            ),
            VStack(8,
                Text("Email"),
                TextField(email, setEmail, placeholder: "you@example.com").Width(300)
            ),
            CheckBox(agreeToTerms, setAgree, label: "I agree to the terms"),
            When(!isValid, () =>
                Text("Please fill all fields and agree to terms").Opacity(0.6)),
            Button("Submit", () => setSubmitted(true)).Disabled(!isValid)
        );
    }
}
```

### Proposed Syntax

```csharp
class FormDemo : Component
{
    public override Element Render()
    {
        var (name, setName) = UseState("");
        var (email, setEmail) = UseState("");
        var (agreeToTerms, setAgree) = UseState(false);

        var isValid = !string.IsNullOrWhiteSpace(name)
            && !string.IsNullOrWhiteSpace(email)
            && agreeToTerms;

        return
            <VStack Spacing={16}>
                <Heading>Registration Form</Heading>
                <VStack Spacing={8}>
                    <Text>Name</Text>
                    <TextField Value={name} OnChanged={setName}
                               Placeholder="Enter your name" Width={300} />
                </VStack>
                <VStack Spacing={8}>
                    <Text>Email</Text>
                    <TextField Value={email} OnChanged={setEmail}
                               Placeholder="you@example.com" Width={300} />
                </VStack>
                <CheckBox IsChecked={agreeToTerms} OnChanged={setAgree}
                          Label="I agree to the terms" />
                {!isValid &&
                    <Text Opacity={0.6}>Please fill all fields and agree to terms</Text>}
                <Button OnClick={() => setSubmitted(true)} Disabled={!isValid}>
                    Submit
                </Button>
            </VStack>;
    }
}
```

### Rough Definition

- **Markup expressions** are a new expression form: `<TypeName Props...>Children</TypeName>` or `<TypeName Props... />`.
- The type must have a parameterless constructor (or a recognized factory pattern).
- Named attributes map to settable properties or constructor parameters.
- `{expression}` escapes into C# expressions (identical to JSX curly braces).
- String attribute values (`Placeholder="text"`) are syntactic sugar for string properties.
- Children between open/close tags are collected into a `children` parameter (or `params Element?[]`).
- A string child is implicitly wrapped in a `Text()` element (configurable via attribute).
- Conditional rendering: `{condition && <Element />}` returns `null` when false.
- List rendering: `{items.Select(i => <Item Key={i.Id} ... />)}` works naturally.
- The compiler lowers markup expressions to the equivalent factory method calls or constructor + property-set sequences.

### Key Design Question

Should markup expressions lower to factory methods (`UI.VStack(...)`) or to constructors (`new VStackElement { ... }`)? Factory methods are more flexible (can return different types), but constructors are simpler to specify. Reactor's existing DSL uses factory methods via `using static Reactor.UI`, so lowering to factory calls would be a natural fit.

### Why Not

**The C# language team has never championed this.** The [csharplang#2529](https://github.com/dotnet/csharplang/discussions/2529) discussion has remained a community proposal with no LDM triage or team engagement. The C# team's consistent position has been that UI markup belongs in dedicated DSLs (Razor, XAML) rather than embedded in the language grammar. Blazor deliberately chose separate `.razor` files with a distinct parser rather than pushing for inline markup in C#, even though the Roslyn team had the technical capability.

**Parser complexity is enormous.** Embedding XML grammar inside C# means the parser must switch between two radically different lexing modes mid-expression. TypeScript's experience is instructive: JSX required special parser rules, and even after years of investment, the type-checking interaction between JSX and TypeScript generics remains a source of bugs and complexity. The TypeScript team described early JSX type-checking as "imperfect" and needed until TS 5.1 to properly support "decoupled type-checking" for non-React JSX libraries. C# would inherit all of these problems on day one.

**Angle brackets conflict with generics.** C# already uses `<T>` for generics. Distinguishing `List<Item>` from `<Item prop="x">` requires unbounded lookahead or contextual parsing rules. TypeScript sidesteps this because its generics use a different syntax position. C# cannot.

**JSX has no native control flow.** JSX is frequently criticized for forcing ternary expressions and `.map()` instead of `if/else` and `for` loops — exactly the same problem Reactor already has. We'd be adding massive language complexity to get... the same limitations we have today, just with angle brackets instead of parentheses. The control flow problem is better solved by result builders or expression blocks.

**Separation of concerns.** The original criticism of JSX — that mixing markup and logic violates separation of concerns — would be amplified in C#, where the community has 20+ years of muscle memory around XAML/Razor separation. Many C# developers actively prefer markup in separate files. We would be making the language harder for every C# developer (server, library, CLI) to benefit a subset writing UI code.

**Tooling burden.** Every tool in the C# ecosystem — IDE syntax highlighting, refactoring, code formatters, linters, code review tools, AI coding assistants — would need to understand a second grammar embedded in C#. The ongoing maintenance cost is permanent and affects the entire ecosystem, not just UI developers.

**The problem it solves may not be real.** Reactor's current function-call syntax (`VStack(12, Text("Hello"), Button("Go", onClick))`) is already quite readable. The parenthesis nesting that markup expressions solve is largely an artifact of deeply nested trees — which might better be solved by component extraction (good practice anyway) or trailing lambdas (a much simpler language change).

### Expanded Use Cases

The common thread across all non-UI use cases is **tree-structured output**. Anywhere code builds a tree of named nodes with attributes and children — documents, configs, test expectations, serialized messages — markup syntax reduces noise and makes the structure visually match the output.

**Email generation.** The most mature non-UI JSX ecosystem. [React Email](https://react.email) (17k+ GitHub stars) and [JSX Email](https://jsx.email/) use JSX components to build standards-compliant HTML emails, then compile to email-client-safe HTML. Emails are deeply nested table-based HTML where markup makes structure scannable.

```csharp
string RenderWelcomeEmail(User user) =>
    Render(
        <Email>
            <Head />
            <Body Style={emailBody}>
                <Container>
                    <Heading>Welcome, {user.Name}!</Heading>
                    <Text>Your account is ready.</Text>
                    <Button Href={user.ActivationUrl}>Activate Account</Button>
                    <Hr />
                    <Text Style={footer}>Questions? Reply to this email.</Text>
                </Container>
            </Body>
        </Email>
    );
```

**PDF document generation.** [@react-pdf/renderer](https://react-pdf.org/) and [jsx-pdf](https://github.com/schibsted/jsx-pdf) (Schibsted) define PDF documents as JSX component trees. The tree structure of a document (pages, sections, paragraphs, tables) maps naturally to markup.

```csharp
Document GenerateInvoice(Invoice inv) =>
    <Document>
        <Page Size="A4" Style={page}>
            <View Style={header}>
                <Text>Invoice #{inv.Number}</Text>
                <Text>{inv.Date:d}</Text>
            </View>
            {inv.Lines.Select(line =>
                <View Style={row}>
                    <Text Style={col}>{line.Description}</Text>
                    <Text Style={col}>{line.Amount:C}</Text>
                </View>
            )}
            <View Style={totalRow}>
                <Text>Total: {inv.Total:C}</Text>
            </View>
        </Page>
    </Document>;
```

**Infrastructure / configuration as code.** Kubernetes manifests and cloud resources are deeply nested trees of named properties — exactly what markup excels at. [Pulumi](https://www.pulumi.com/) and [cdk8s](https://cdk8s.io/) already generate infrastructure from TypeScript constructs.

```csharp
Chart DefineApp() =>
    <Chart>
        <Deployment Name="api" Replicas={3}>
            <Container Image="myapp:latest" Port={8080}>
                <EnvVar Name="DB_HOST" Value={dbHost} />
                <Resources RequestCpu="100m" LimitCpu="500m" />
            </Container>
        </Deployment>
        <Service Name="api-svc" Port={80} TargetPort={8080}
                 Type={ServiceType.LoadBalancer} />
    </Chart>;
```

**Test assertions / expected output.** Algolia's [expect-jsx](https://github.com/algolia/expect-jsx) lets tests compare component output as readable markup instead of deeply nested object comparisons. When the expected output is a tree, expressing it as markup is far more readable than constructor calls.

```csharp
[Fact]
void RendersBreadcrumb()
{
    var result = RenderComponent<Breadcrumb>(new { Path = "/docs/api" });
    Assert.MarkupEqual(result,
        <Nav AriaLabel="breadcrumb">
            <Link Href="/">Home</Link>
            <Link Href="/docs">Docs</Link>
            <Span>API</Span>
        </Nav>
    );
}
```

**API response / serialization building.** Building structured API responses (XML, SOAP envelopes, RSS feeds) with markup is a natural fit. VB.NET shipped XML literals in VB 9 (2008) and developers immediately used them for SOAP message construction, RSS feed generation, and LINQ-to-XML data transforms — even without generalization beyond `XElement`. Scott Hanselman [demonstrated using VB XML literals as an ASP.NET MVC view engine](https://www.hanselman.com/blog/the-weekly-source-code-30-vbnet-with-xml-literals-as-a-view-engine-for-aspnet-mvc). The [Pattern-Based XML Literals proposal (dotnet/vblang#483)](https://github.com/dotnet/vblang/issues/483) by Anthony D. Green proposed generalizing this to arbitrary target types — exactly the same idea as C# markup expressions.

```csharp
XElement BuildSoapEnvelope(Order order) =>
    <Envelope xmlns={Namespaces.Soap}>
        <Body>
            <SubmitOrder xmlns={Namespaces.Orders}>
                <OrderId>{order.Id}</OrderId>
                {order.Items.Select(i =>
                    <Item Sku={i.Sku} Qty={i.Quantity} />
                )}
            </SubmitOrder>
        </Body>
    </Envelope>;
```

---

## Summary and Prioritization

| Tier | Feature | Reactor Benefit | Likelihood of Shipping | Broader C# Impact |
|------|---------|-------------|----------------------|-------------------|
| **S** | **Discriminated Unions** | Medium — type-safe state modeling | Medium-High — active proposal #9662, working group, huge community demand | Very High — error handling, domain modeling, protocols, CQRS; every C# dev benefits |
| **A** | **Expression Blocks** | Medium — eliminates helper method fragmentation | High — #9243 championed (March 2025), low complexity | High — immutable init, scope hygiene, switch arms; general purpose |
| **A** | **Let-Binding Expressions** | Medium — name subexpressions inline in render trees | Medium — #973 championed since 2017; lighter than expression blocks, could ship alongside or instead | High — universal need; `is var` hack proves demand; LINQ, logging, expression-bodied members all benefit |
| **A** | **Render Compiler Transform** | Medium-High — Phase 1 hook safety, Phase 2 skip optimization | Phase 1: Very High (just a Roslyn analyzer); Phase 2: Very Low (no plugin API) | Medium — Phase 1 is Reactor-specific; Phase 2 concepts (tracing, memoization) are broad but blocked |
| **B** | **Collection Initializer Trees** | High — replaces `)` nesting with `{ }`, visually cleaner trees | Low-Medium — #6602/#9528/#5654 deferred since 2020; `{ }` ambiguity same as trailing lambdas | Medium-High — HTML builders, file trees, ASTs, config hierarchies; extends existing C# pattern |
| **B** | **Trailing Lambdas** | High — cleans up slot APIs, event handlers, builders | Low — LDM declined 3x, fatal brace ambiguity | High — testing, builders, concurrency, collections |
| **B** | **Result Builders** | High — enables if/foreach in element trees natively | Low — no formal proposal, SwiftUI pain is cautionary | High — regex, HTML, SQL, config DSLs; 50+ non-UI Swift libraries |
| **B** | **Scoped Extension Receivers** | Medium — catches layout bugs at compile time | Low-Medium — no receiver lambda concept in C#; C# 14 extensions partial | Medium-High — Gradle, Ktor, testing, routing DSLs |
| **C** | **Property Wrappers** | Medium-High — reduces state boilerplate | Low — LDM shelved 2020, `field` keyword prioritized, source generators cover it | High — validation, lazy, persistence, DI, feature flags; broad non-UI adoption in Kotlin/Swift |
| **C** | **Markup Expressions** | Very High — eliminates parenthesis nesting, makes trees scannable | Very Low — never championed, parser conflict with generics, tooling burden on entire ecosystem | Medium — email, PDF, IaC; but forces second grammar on all C# developers |

### Recommended Investment Strategy

**Do now (S + A Tier):**

1. **Discriminated Unions** — Support the active proposal (#9662). Build Reactor examples that demonstrate the value for UI state modeling to add weight to the community case. This feature will ship eventually; our job is to ensure Reactor's patterns are represented in the design discussions.
2. **Expression Blocks** — Already championed (#9243). Low risk, high utility. Write up Reactor-specific use cases to feed into the proposal. This is the most likely feature to ship in C# 14/15.
3. **Let-Binding Expressions** — Advocate for this as a lightweight companion to expression blocks. The declaration expressions proposal (#973) is already championed; push for narrow scoping (Flavor 1) which is more useful in expression-heavy code. The `is var` hack's popularity is a strong demand signal to cite.
4. **Render Compiler Transform (Phase 1)** — Build the Roslyn analyzer now. No language change needed, no LDM approval required. Ship it as part of the Reactor SDK. Phase 2 is aspirational; don't block on it.

**Watch and support (B Tier):**

5. **Collection Initializer Trees** — The most "C# native" tree syntax, but blocked by the same `{ }` ambiguity as trailing lambdas. The `with { }` variant (#6602) could break the logjam. Contribute Reactor examples to the factory initializer discussions. Worth prototyping with `new`-based syntax today to validate the ergonomics.
6. **Trailing Lambdas, Result Builders, Scoped Receivers** — These have the strongest non-UI justifications but face real language design obstacles. Contribute to the csharplang discussions with Reactor examples and non-UI use cases. Don't design Reactor's API around features that may never ship.

**Don't plan around (C Tier):**

7. **Property Wrappers** — Source generators and the `field` keyword cover the practical need. The expanded use cases (validation, persistence, DI) are compelling but the LDM has moved on.
8. **Markup Expressions** — The highest Reactor-specific impact but the lowest feasibility. The parser complexity, generic conflicts, and tooling burden make this a non-starter with the C# team. Reactor's function-call syntax is already clean enough; invest in component extraction patterns and IDE tooling instead.

### Next Steps

1. **Ship the Phase 1 hook-safety analyzer** — immediate, zero-dependency win.
2. **Write csharplang discussion posts** for discriminated unions, expression blocks, and let-bindings, using Reactor examples + the non-UI use cases documented here.
3. **Prototype collection initializer trees** using `new`-based syntax to validate the `{ }` ergonomics before advocating for factory method support.
4. For each S/A tier feature, produce a detailed spec including:
   - Full type system changes and grammar definition
   - Interaction with existing C# features (generics, nullability, type inference)
   - Error messages and diagnostics
   - Breaking change analysis
   - Prototype implementation strategy (Roslyn fork or source generator)
5. Build prototypes demonstrating S/A tier features on real Reactor code.