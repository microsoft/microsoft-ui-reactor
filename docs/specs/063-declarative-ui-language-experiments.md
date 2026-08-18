# Declarative UI Construction — Compiler Experiments for the C# Working Group

## Status

**Experimental results** — 2026-08-18. Input to the working group formed by
[LDM 2026-07-15](https://github.com/dotnet/csharplang/blob/main/meetings/2026/LDM-2026-07-15.md).

This spec reports on a **working Roslyn fork** built to answer the questions the LDM left open. It supersedes
the speculation in [spec 019 Part 6B](019-collection-initializer-api-exploration.md#part-6b-option-f--factory-initializers-prototype-of-csharplang-6602),
which described a `dotnet/roslyn` branch `features/reactor-extensions` that **does not exist** — the branch
404s, and no PR or branch in `dotnet/roslyn` matches. Everything below was implemented from scratch against
`dotnet/roslyn` `main` @ `582bdfd5`.

The three csharplang references the LDM cited were verified to exist:
[discussions/10207 "[Proposal] Factory Initializers"](https://github.com/dotnet/csharplang/discussions/10207),
[issues/10185 "[Proposal]: Mixed object and collection initializers"](https://github.com/dotnet/csharplang/issues/10185) (open),
and [issues/9003 "[Proposal]: Nested members in `with` and object creation"](https://github.com/dotnet/csharplang/issues/9003) (open).
Only the compiler prototype was fictional.

---

## 1. What the LDM asked for

> The group will first describe an idealized syntax without limiting itself to features already available in
> C#, survey relevant languages and frameworks such as Kotlin, and then **prototype promising combinations as
> experimental compiler features**. Those experiments will be **applied to real Reactor applications** so we
> can compare representative code…
>
> We agreed that future examples must emphasize **non-golden paths**… We should compare before-and-after
> versions of complete applications and account for **generated API size, allocations, discoverability,
> refactorability, and readability**.

Four candidate features were named: factory initializers, mixed object/collection initializers, nested member
assignment, and `init` methods. The LDM's own summary of the crux:

> A narrow feature is easier to understand and may be useful outside UI construction, but the motivating
> scenario **remains noticeably incomplete without ergonomic children**.

That sentence is the hypothesis this experiment tests, and the measurements below confirm it — quantitatively,
and more strongly than expected.

---

## 2. Framing: three dimensions, not four features

The four candidates are point solutions along three orthogonal axes, plus one cross-cutting constraint the LDM
named but did not turn into a feature:

| Axis | Question | Candidate |
|---|---|---|
| **1. Construction** | How is the node named and allocated? | Factory initializers (`Factory(args) { … }`) |
| **2. Configuration** | How are properties set? | Nested member paths; `init` methods (styles) |
| **3. Content** | How are children supplied? | Mixed object/collection initializers |
| **4. Composability** *(cross-cutting)* | Can a subtree or a style be extracted into a method **without changing call sites**? | *(no candidate proposed)* |

Axis 4 is the one that decides whether a design survives real code, and it is the axis on which the reference
design for axis 1 **fails**. See §5.

---

## 3. What was built

A real compiler feature in a `dotnet/roslyn` fork, gated behind experimental `/features:` flags so that with
the flags off the compiler is byte-for-byte the shipping language.

| Stage | Feature | Flag | Status |
|---|---|---|---|
| 1 | Factory initializers — trailing `{ … }` after a factory call, at **primary-expression precedence**; parens-optional form | `FactoryInitializers` | ✅ working |
| 2 | **Type-level opt-in** (`[FactoryInitializable]` on the produced type) alongside member-level opt-in (`[Factory]`) | `FactoryInitializers` | ✅ working |
| 3 | **Content elements** — bare children inside the initializer, with spread, assigned to a `[ContentProperty]` member | `FactoryInitializerContent` | ✅ working |
| 4 | Nested member paths (`Layout.Padding.Left = v`) | `NestedMemberInitializers` | ⛔ not implemented (flag reserved) |
| 5 | `init` / extension-`init` methods (styles) | — | ⛔ not implemented |

**Implementation size:** 16 hand-edited compiler files, **702 inserted lines** (plus generated syntax/bound-node
code and localization stubs). The feature is modelled throughout on `WithExpressionSyntax` /
`BoundWithExpression`, which is the closest existing construct: a trailing initializer applied to an existing
value. A factory initializer is *`with` minus the clone, minus the keyword, at primary-expression precedence.*

### Grammar

```
primary_expression
    : …
    | primary_expression initializer_block      // new
    ;
```

Consumed in `LanguageParser.parsePostFixExpression` alongside `(`, `[`, `++`, `.` — which is what makes
`Factory(x) { P = v }.Chained()` parse as `(Factory(x) { P = v }).Chained()` with no parentheses, deliberately
unlike `with`.

### Verification

| Check | Result |
|---|---|
| Roslyn syntax unit tests | **10,551 / 10,551 pass** — 0 parser regressions |
| Roslyn semantic unit tests | **19,763 / 19,763 pass** |
| Roslyn emit unit tests | **7,153 / 7,153 pass** |
| Roslyn IOperation unit tests | **2,501 / 2,501 pass** |
| **Total** | **39,968 compiler tests, 0 failures** |
| Feature flags **off** | Existing behaviour unchanged; `Factory() { … }` produces today's `CS1002: ; expected` |
| Real `src/Reactor` (WinUI, source generators, analyzers) built with the prototype `csc` | **Build succeeded** |
| Real `src/Reactor` built with the **stock** SDK compiler after the opt-in attributes were added | **Build succeeded** (the attributes are inert without the feature flags) |
| `dotnet test tests/Reactor.Tests` | **Not run** — blocked in this environment by `MSB3923`, an npm-registry TLS failure while `Reactor.Cli` downloads a native binary. Confirmed pre-existing: the identical failure reproduces at clean `HEAD` with these changes stashed. |
| Every new diagnostic | Verified to actually fire (§7) |

The parser change is guarded by the experimental flag, so the zero-regression result is a statement about the
*implementation*, not about eventual breaking-change risk; §8 records the residual risk.

---

## 4. The measured comparison

The LDM demanded non-golden-path code. The harness expresses **one** component — a folder pane with `.Where()`
partitioning, two `.Select()` spreads, a `switch` expression, a conditional `null` child, a nested single-child
container, and fluent modifiers — **five ways**, and asserts all five produce a structurally identical tree
across six input configurations. **A mis-ported variant fails the run**, so the token counts below describe code
that is known to be equivalent, not code that merely looks equivalent.

```
ORACLE: all 5 variants produce structurally identical trees across 6 configurations.
```

Token and character counts come from **the prototype compiler's own tokenizer** (`DescendantTokens()` over the
returned expression), not a regex approximation. Whitespace is excluded from `chars`.

| Variant | tokens | Δ vs current | chars | Δ | ceremony | alloc/render |
|---|---:|---:|---:|---:|---:|---:|
| **1. Current** — factories + fluent modifiers | 250 | — | 695 | — | 0 | 3,152 B |
| **2. Option A′** — `new T { …, Children = [ … ] }` (ships today) | 328 | **+31 %** | 1,083 | **+56 %** | 22 | 2,496 B |
| **3. Factory initializers v1** — the LDM reference design | 293 | **+17 %** | 802 | **+15 %** | 26 | 2,928 B |
| **4. Factory initializers + content elements** | 261 | **+4 %** | 724 | **+4 %** | **0** | 2,928 B |
| **5. Variant 4, properties set in the block** | 301 | +20 % | 939 | +35 % | 7 | **2,496 B** |

*"Ceremony" counts tokens that exist only to satisfy the construction shape rather than to describe UI: the
`new` keyword, the `Children`/`Child` property name and its `=`, and the `[` `]` wrapping children.*

### 4.1 The narrow feature makes real code bigger

This is the headline result and it is not what the reference design assumes.

**Factory initializers *without* bare children (variant 3) costs +17 % tokens against the API Reactor ships
today.** Dropping `new` saves 4 characters per container; re-introducing `Children = [ … ]` at every nesting
level costs far more than that. Spec 019 called v1 "strictly better than Option A′," and that is true — but
both are *worse than doing nothing*. On this component, v1's ceremony count (26) is actually **higher** than
Option A′'s (22), because `Children = [ … ]` is paid at every level while `new` is only 3 characters.

The LDM's instinct that "the motivating scenario remains noticeably incomplete without ergonomic children" is
correct, and understated: **shipping the narrow feature alone would be a net ergonomic regression for the
motivating scenario.**

### 4.2 Content elements are what pay for the feature

Adding bare children (variant 4) collapses ceremony to **zero** and lands within 4 % of today's fluent API on
both tokens and characters — while gaining brace-delimited grouping, eliminating `.Set()`, and removing the
need to generate a modifier method per property. Content elements are not an "ergonomic follow-up"; they are
the component that makes the whole feature break even.

**Recommendation: factory initializers and content elements should ship together, or neither should ship.**

### 4.3 The allocation win has nothing to do with syntax

Spec 019 §8.5 attributed a performance win to the new construction syntax. That is wrong, and the harness
isolates why.

Variants 3 and 4 allocate **2,928 B**; variant 5 — identical syntax to variant 4, differing only in that
layout/styling properties are set inside the initializer instead of through fluent modifier calls — allocates
**2,496 B**, exactly matching Option A′. Factory initializers are **allocation-neutral** with respect to
`new` + object initializers; they lower to the same temp-plus-assignments sequence
(`MakeExpressionWithInitializer`, shared with `with`).

**The 21 % allocation reduction comes entirely from moving properties onto the record so a chain of
`ElementModifiers.Merge` copies is not needed.** Reactor can capture that win today, on the shipping compiler,
with no language change. It should not be counted as a benefit of any proposal on this list.

---

## 5. Composability: the reference opt-in design fails the LDM's own test

The LDM was explicit:

> It must remain easy to extract an arbitrary subtree into a method without changing how callers use it.

Under a **member-level** opt-in (a `factory` modifier or `[Factory]` attribute on each method), it is not.
Extract a subtree into a helper and every call site that configured it breaks until the *helper's author*
remembers to opt in. Verified — this is a real compiler error from the prototype:

```
error CS9700: 'F.Plain(string)' cannot be used with a trailing initializer because neither the invoked
member nor its return type 'TextElement' opts in.
```

So the prototype implements a second opt-in: **`[FactoryInitializable]` on the produced type**, inherited by
derived types. Any call producing that type — including a helper the library has never seen — accepts a
trailing initializer.

```csharp
[FactoryInitializable]                      // ← applied once, to the base record
public abstract record Element { … }

// Not marked as a factory by anyone. Works anyway:
public static Panel MyExtractedSubtree(double s) => new Panel { Spacing = s };

var x = MyExtractedSubtree(4) { Margin = 8 };   // ✅ compiles
```

This is also the correct model of the underlying invariant. "This call returns a fresh, uniquely-owned
instance" is a property of a type's construction discipline for immutable node trees, not a property that 203
individual methods each independently assert.

### Measured library churn

Applied to the **real** Reactor tree, not a mock:

| Opt-in design | Edits to Reactor | Third-party/user factories |
|---|---|---|
| Member-level (`factory` modifier) | **203** factory methods, and growing | Each author must opt in |
| **Type-level (`[FactoryInitializable]`)** | **1** attribute on `record Element` | Work automatically |

Content properties are a separate, genuinely per-type cost: ~31 applications of `[ContentProperty]` across
Reactor's 9 `Children`, 17 `Child` and 5 `Content` containers. The prototype supports **singular** content
properties (a lone content element assigned to `Element? Child`) so the same `Factory { …props, child }` shape
serves single- and multi-child containers.

Both opt-ins are implemented and both are supported simultaneously; they are not mutually exclusive.

---

## 6. Design decisions worth taking to the LDM

### 6.1 Content elements must be trailing

`{ Spacing = 0, childA, childB }` is legal; `{ childA, Spacing = 0 }` is an error
(`CS9704: Content elements must follow all member assignments in an initializer`).

This directly answers the readability objection recorded in spec 008 §5 ("properties and children interleaved
in `{ }` can be confusing"). Every relevant prior art — XAML attributes-then-content, JSX props-then-children,
SwiftUI/Kotlin trailing blocks — already puts content last. Requiring it costs nothing real, makes the feature
easier to specify, and means a reader never has to scan past a subtree to find its parent's properties.

### 6.2 Immutability is preserved with no `Add`

Spec 019 §8.2 catalogued five unsatisfying ways to reconcile collection initializers with immutable records,
because classic collection initializers require a mutating `Add`. The prototype needs none of them: content
elements are gathered into a **collection expression** which is assigned to a normal `init` member. Nothing is
mutated, `[CollectionBuilder]` targets work, and `Element?[]`, `ImmutableArray<T>` and spans are all reachable
through the existing conversion rules. This is Option A′'s mechanism with the property name elided.

### 6.3 Spread needs no new syntax

`..expr` inside an initializer block already parses as a `RangeExpressionSyntax` with no left operand. The
binder recognises that shape in content position and routes it through the *same* spread binding a collection
expression uses (hoisted out of `BindCollectionExpression`, so the semantics cannot drift). `{ ..items.Select(…) }`
required zero parser changes.

### 6.4 A statement-form content block is the next thing worth prototyping

The LDM discussed Kotlin-style trailing lambdas with an implicit receiver and worried, correctly, about name
lookup. The valuable half of that idea for UI code is not the implicit receiver — it is **control flow while
producing children** (`if`, `foreach`, `switch` as content). That is Swift's `@ViewBuilder`.

Spec 008 §7 rejected result builders largely on Swift's type-checker blowup. That objection **does not
transfer**, and the reason is structural: Swift infers the builder body's type, whereas here the content
property's type is known before the elements are bound, so content is **target-typed**, exactly like a
collection expression. C# can afford the construct at the point where Swift cannot. This should be prototyped
as `FactoryInitializerContentFlow` and measured against the imperative-`List<Element>` and nested-ternary
patterns in spec 019 §7B.3 / §7B.4, which are the shapes all five variants above still handle badly.

### 6.5 Nullable children are a real migration blocker for Reactor

Reactor's `StackElement.Children` is `Element[]`, with null filtering done inside the `params Element?[]`
factories. A conditional `cond ? child : null` therefore cannot be a content element without either retyping
`Children` as `Element?[]` (and filtering in the reconciler) or forcing callers into
`..(cond ? [x] : [])`. This is a Reactor decision, not a language one, but it must be made before any
migration.

---

## 7. Every diagnostic was verified to fire

A gate that never fires proves nothing. Each of these is a real compiler error observed from the prototype,
with the neighbouring positive case compiling in the same file:

| Code | Fires on |
|---|---|
| `CS9700` | Trailing initializer on a call whose target has not opted in |
| `CS9702` | Content elements on a type with no `[ContentProperty]` |
| `CS9703` | `[ContentProperty("Nope")]` naming a member that does not exist |
| `CS9704` | Content element appearing before a member assignment |

---

## 8. Known gaps and residual risk

1. **`factory` modifier not implemented.** Opt-in is spelled with attributes. The receiver-shape change is the
   feature; the spelling is a separable decision, and attributes work across assemblies today without new
   metadata format work. A real modifier keyword would need `DeclarationModifiers`, modifier parsing and a
   persistence mechanism.
2. **Breaking-change surface not fully characterised.** With the flag on, `Foo()` followed by `{` on the next
   line — today a missing-semicolon error — now parses as a factory initializer, degrading that recovery path.
   The pattern parser runs before expression parsing, so `o is Shape { … }` and `o is Shape() { … }` are
   unaffected; that is by construction and is covered by the 10,551 passing syntax tests, but a shipping
   design needs a deliberate decision here.
3. **No IDE support.** `CSharpOperationFactory` has no case for the new bound node, so IOperation consumers
   will not see it (the 2,501 IOperation tests pass because the feature is off by default — that is a
   statement about non-regression, not about IOperation coverage of the new node). Formatting, classification
   and completion are untouched.
4. **Not applied to a complete application.** The real Reactor library compiles with the prototype, and the
   opt-in attributes are applied to `Element`, `StackElement`, `FlexElement` and `BorderElement` — but
   migrating call sites needs the property-surface promotion Option A′ also requires (~60 `ElementModifiers`
   properties moved onto records). That is a multi-day migration, and until it is done the LDM's
   "before-and-after versions of complete applications" remains unsatisfied. The five-variant harness with an
   equality oracle is the best available substitute and should be labelled as such.
5. **Stages 4 and 5 unimplemented** — nested member paths and `init` methods. Nested paths interact directly
   with Reactor's `Modifiers.Layout` / `Modifiers.Visual` buckets and would remove the need to promote 60
   properties, which makes them the highest-value remaining experiment after §6.4.

---

## 9. Recommendations to the working group

1. **Do not ship factory initializers as a standalone narrow feature.** Measured on non-golden-path code it is
   a 17 % regression against the API this framework ships today. Bundle it with content elements or drop it.
2. **Adopt type-level opt-in.** Member-level opt-in fails the LDM's own composability requirement; the fix is
   one attribute instead of 203 and it is already implemented.
3. **Require content elements to be trailing.** Cheap, well-precedented, and it retires a standing objection.
4. **Stop attributing the allocation win to construction syntax.** It comes from property placement and Reactor
   can have it today.
5. **Prototype target-typed content control flow next** (§6.4), then nested member paths (§6.5). These attack
   the patterns that every current variant handles badly.

---

## Appendix A — Reproducing

```powershell
git clone --depth 1 https://github.com/dotnet/roslyn.git C:\src\roslyn   # main @ 582bdfd5
cd C:\src\roslyn
git apply <session-artifacts>\roslyn-factory-initializers.patch
.\eng\generate-compiler-code.cmd
.\eng\build.ps1 -restore -build -solution Compilers.slnf -configuration Debug

# harness
.\build.ps1 -Sources Compare.cs -Out Compare.exe `
    -Features @("FactoryInitializers","FactoryInitializerContent")
dotnet .\Compare.exe          # oracle + allocation table
dotnet .\MeasureSource.exe    # token / char / ceremony table
```

Build Reactor itself with the prototype compiler:

```powershell
dotnet build src\Reactor\Reactor.csproj -c Debug -p:SkipSignaturesGen=true `
    -p:CscToolPath=C:\src\roslyn\artifacts\bin\csc\Debug\net10.0 -p:CscToolExe=csc.exe `
    '-p:Features=FactoryInitializers%3BFactoryInitializerContent'
```

Artifacts: `roslyn-factory-initializers.patch`, `Compare.cs`, `MeasureSource.cs`, `Stage1.cs`,
`Stage1Negative.cs`, `Stage3.cs`, `Stage3Negative.cs`, `build.ps1`.

## Appendix B — The shape, end to end

```csharp
return VStack {
    Spacing = 0,
    Modifiers = new ElementModifiers { Background = "#202020" },

    Text("Folders") { Modifiers = new ElementModifiers { Margin = 8 } },

    VStack {
        Spacing = 2,
        ..pinned.Select(f => HStack {
            Spacing = 6,
            Key = $"p-{f.Name}",
            Text(f.Name),
            f.Unread > 0 ? Text($"{f.Unread}").Background("#0a5") : null,
        }),
    },

    Border {                                  // singular content property
        HStack {
            Spacing = 4,
            Text(filter switch { "all" => "All mail", _ => "Custom" }),
            Button("Change").IsEnabled(filter != "all"),
        },
    }.Margin(4),                              // fluent chaining after `}`, no parentheses

    showFooter ? Text($"{unread} unread").Width(120) : null,
};
```

No `new`. No `Children = [ … ]`. No `.Set()`. Spread, `switch`, conditional `null` children and fluent
modifiers all compose. This compiles and runs on the prototype compiler today.
