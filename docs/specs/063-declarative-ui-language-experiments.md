# Declarative UI Construction — Compiler Experiments for the C# Working Group

## Status

**Experimental results** — 2026-08-18. Input to the working group formed by
[LDM 2026-07-15](https://github.com/dotnet/csharplang/blob/main/meetings/2026/LDM-2026-07-15.md).

> **Update — [LDM 2026-08-05](https://github.com/dotnet/csharplang/blob/main/meetings/2026/LDM-2026-08-05.md)
> (13 days after that meeting, found while fact-checking this spec).** In the C# 16 feature-focus
> discussion the LDM wrote:
>
> > We remain interested in improving C# for declarative UI construction… This area is still exploratory.
> > We do not yet know which programming model will ultimately be pursued or which language features would
> > best support it. We should continue collaborating on the exploration and use it to inform future
> > language design, but **we do not consider declarative UI work a C# 16 delivery commitment.**
>
> So the exploration is explicitly wanted and explicitly uncommitted. Two consequences for this document:
> the value of these experiments is in *informing* the design rather than in shipping against it, and
> §10's urgency argument narrows — it applies to the in-flight `#10185` lowering (§8), which is being
> implemented now, not to a declarative-UI feature on a schedule.

**Lineage — this work did not originate the initializer proposals.** The chain `#10185` sits on traces to
[LDM 2026-04-27](https://github.com/dotnet/csharplang/blob/main/meetings/2026/LDM-2026-04-27.md), which took
up `compound-assignment-in-initializer-and-with` motivated by *events* in declarative UI frameworks
(`new Button { OnClick += this.ClickHandler }`) — three months before the Reactor meeting and not about
Reactor. LDM 2026-07-15 was convened to review Reactor's exploration; that is the extent of the connection.

This spec reports on a **working Roslyn fork** built to answer the questions the LDM left open. It supersedes
the speculation in [spec 019 Part 6B](019-collection-initializer-api-exploration.md#part-6b-option-f--factory-initializers-prototype-of-csharplang-6602),
which described a `dotnet/roslyn` branch `features/reactor-extensions` that **does not exist** — the branch
404s, and no PR or branch in `dotnet/roslyn` matches. Everything below was implemented from scratch against
`dotnet/roslyn` `main` @ `582bdfd5`.

The three csharplang references the LDM cited were verified, and their **current** status matters:

| LDM reference | Actual state |
|---|---|
| [discussions/10207 "[Proposal] Factory Initializers"](https://github.com/dotnet/csharplang/discussions/10207) | **Closed as a duplicate.** Redirects to [#6602](https://github.com/dotnet/csharplang/discussions/6602) and to the formal champion issue [**#10292**](https://github.com/dotnet/csharplang/issues/10292), specced at [`proposals/factory-methods.md`](https://github.com/dotnet/csharplang/blob/main/proposals/factory-methods.md). **The championed design uses a `[Factory]` attribute, not a `factory` modifier.** |
| [issues/10185 "Mixed object and collection initializers"](https://github.com/dotnet/csharplang/issues/10185) | Open, *Proposal champion*, specced at [`proposals/mixed-object-and-collection-initializers.md`](https://github.com/dotnet/csharplang/blob/main/proposals/mixed-object-and-collection-initializers.md). **Being implemented right now** — see §8. |
| [issues/9003 "Nested members in `with` and object creation"](https://github.com/dotnet/csharplang/issues/9003) | Open, *Proposal champion*, assigned **Needs More Work**, and states **"Specification: None yet."** No defined lowering exists. |

Only the compiler prototype in spec 019 was fictional; the proposals are real.

Two consequences for this experiment. First, the prototype's attribute-based opt-in is **not** a shortcut around a
`factory` modifier — it is the spelling the championed proposal actually uses. Second, spec 019's claim that v1 is
"honor-system" (no verification that a factory returns a fresh instance) is **out of date**: `factory-methods.md`
restricts factory returns to construction-like expressions, requires matching annotation across overrides and
interface implementations, and proposes an `IsFactory` modreq for virtual members.

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
design for axis 1 **fails** — and the obvious fix for it turns out to be unsound, which §5 works through.

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
*implementation*, not about eventual breaking-change risk; §9 records the residual risk.

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

## 5. Composability vs. safety: neither opt-in design is right as specified

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

### 5.1 Counter-evidence: type-level opt-in is unsound on its own

The championed proposal pairs `[Factory]` with a **body restriction** — a factory's `return` expression must be a
`new` expression, a `with` expression, a call to another `[Factory]` member, a struct value, or `null`
(`factory-methods.md`). Assigning to a local and returning it is rejected. That restriction is what makes it safe to
run `init` setters on the result: the value is known to be freshly allocated and uniquely owned.

**Type-level opt-in has no such guarantee, and Reactor contains a live counterexample.** Auditing all
**198** public static methods on `Factories` (syntax-only classification against the proposal's rule):

| Verdict | Count | |
|---|---:|---|
| Returns `new` / `with` / `default` / `null` — legal | 180 | 90.9 % |
| Returns an invocation — legal **only if that callee is also `[Factory]`** | 12 | 6.1 % |
| **Rejected outright** | **6** | **3.0 %** |

The six rejects are `Empty`, `When`, `If`, `Expr`, `DevtoolsMenu` and `AcrylicBrush`. Four of them return
`EmptyElement.Instance` — a `static readonly` **shared singleton**
(`src/Reactor/Core/Element.cs:1447`, `src/Reactor/Elements/Dsl.cs:1351`).

With `[FactoryInitializable]` on `Element` and no body restriction, this compiles — and corrupts process-wide
state. Demonstrated, not hypothesised:

```
before           : Instance.Margin = 0
after Empty(){4} : Instance.Margin = 4
unrelated Empty(): Margin = 4   <-- should be 0
HAZARD CONFIRMED: the shared singleton was mutated through the trailing initializer.
```

The 12 invocation-returning factories are a second, quieter cost. `Card` is
`Border(child).Background(…).WithBorder(…)`; `Title` is `TextBlock(content).ApplyStyle(…)`. Their return
expressions are calls to **fluent modifier extension methods**, so under member-level opt-in those extensions
would each need `[Factory]` too — pushing the annotation out of the 203 factories and into the 473 extension
methods. The "203 edits" figure in the table above is therefore a **lower** bound for the member-level design.

### 5.2 Proposed synthesis: infer factory-ness for opted-in types

Neither pure design is right. Member-level opt-in is safe but fails the LDM's composability requirement and
under-counts its own churn; type-level opt-in is composable and cheap but unsound.

The resolution is to keep the *permission* at the type and the *proof* at the member, and stop making authors
write either:

> When a method returns a type marked `[FactoryInitializable]`, the compiler checks its return expressions
> against the `factory-methods.md` restriction. If they satisfy it, the compiler emits `[Factory]`
> automatically. If they do not, it does not, and a trailing initializer on that call is an error.
> An explicit `[Factory]` remains available to *assert* the intent and move the diagnostic to the declaration.

This gets both properties at once. Extract a subtree into a helper whose body is a `new` expression or another
factory call and callers keep working with no annotation anywhere — the composability test passes. `Empty()`
returns a field, is never inferred as a factory, and `Empty() { Margin = 4 }` is rejected — the safety test
passes. Reactor's opt-in cost becomes **one attribute**, with the 6 unsafe factories rejected at their call
sites rather than silently corrupting a singleton.

The known risk is that factory-ness becomes an inferred, and therefore silently breakable, part of a method's
contract: adding a statement to a helper body would break its callers with no diagnostic at the declaration.
That is why the explicit `[Factory]` opt-in must remain, and why an analyzer suggesting it on inferred
factories is probably part of the design. **This is not implemented** — it is the recommendation this
experiment produces, and the next thing worth prototyping alongside §6.4.

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

This is genuinely open ground. There is **no championed csharplang proposal** for `if`/`foreach` inside a
collection expression: [#9754 "Immediately Enumerated Collection Expressions"](https://github.com/dotnet/csharplang/blob/main/proposals/immediately-enumerated-collection-expressions.md)
(open, *Proposal champion*) enables `foreach (bool b in [true, false])` and nested targetless spreads, and
explicitly lists conditional inclusion as future work; #9739 was closed in its favour; the 2021 LDM's interest
in list comprehensions never produced a proposal; and no Swift-style result-builder proposal exists at all.

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

## 8. How this relates to the mixed-initializer work already underway

`#10185` is not merely a proposal — there is a **live upstream implementation stack** in `dotnet/roslyn`,
targeting `features/compound-assignment-in-initializer`: PRs **#83750** (`mixed-init-syntax`), **#83751**
(`mixed-init-binding`), **#83752**, **#83753** (semantic model), **#83754** (IDE, gated by
`SupportsMixedObjectAndCollectionInitializers`), and #83755–#83761 (analyzer/IDE polish). #83750 notes the
parser already classifies mixed lists correctly and binding rejects them until the gate opens; #83751 gates
binder/lowering/flow/emit to `LanguageVersion.Preview`.

That work and this prototype agree on the disambiguation rule and disagree on everything downstream of it.
The differences are the substance of what this experiment contributes:

| | `#10185` as specced | This prototype |
|---|---|---|
| Member vs. element rule | Grammar-based: `X = v` is a member initializer, a bare expression is an element | **Same** — grammar-based, and `(a = b)` parenthesized becomes content |
| Where elements go | `Add(…)` on the initialized object; requires `IEnumerable` | **A `[ContentProperty]` `init` member**, assigned once via a collection expression |
| Mutability | Mutating `Add` calls | **No mutation** — this is what makes it usable for immutable records |
| Spread `..expr` inside `{ }` | **Not added**; spread stays in `[ … ]` | **Supported**, routed through the collection-expression spread binder |
| Ordering | Arbitrary interleaving, because evaluation order is observable | **Content must be trailing** (§6.1) |
| `required` members | Bare elements do not satisfy them | Same |

The ordering divergence deserves an explicit LDM decision. `#10185`'s argument for interleaving — evaluation
order is observable, so do not constrain it — is sound *for `Add`-based initialization of a mutable object*.
It does not apply here: content elements are gathered into one collection expression assigned once, so there
is no per-element observable interleaving with the member assignments to preserve. Trailing-only is therefore
free in this design and, per §6.1, better to read.

The immutability divergence is the load-bearing one, and it is sharper than "a difference of approach": the
proposal's stated motivation is **this exact scenario** —

> …where a parent control both has configurable properties and a list of children (Avalonia, MAUI, Windows
> Forms, HTML-builder libraries, etc.)

— but the mechanism it specifies rules out the immutable half of that space. The normative text is explicit:

> **When the enclosing *initializer_element_list* contains at least one *element_initializer*, the object being
> initialized** shall be of a type that implements `System.Collections.IEnumerable` or a compile-time error
> occurs.

and

> An *element_initializer* invokes an `Add` method on the object being initialized.

`StackElement` is an immutable record with an `init`-only `Element[] Children`. It implements no `IEnumerable`
and has no `Add`, and cannot gain a meaningful one.

**This was verified against the implementation, not inferred from the spec.** The head of dotnet/roslyn
[#83751](https://github.com/dotnet/roslyn/pull/83751) (`mixed-init-binding`, `e6f9127b5`) was built locally
and run:

| | Result |
|---|---|
| Positive control — `new Form { Title = "Hello", "a", "b" }` on an `IEnumerable` + `Add` type | **compiles** — the feature is live |
| Reactor's `StackElement`, mixed shape `{ Spacing = 4, new TextElement("child") }` | **CS1922** |
| Same record, pure element list `{ new TextElement("child") }` | **CS1922** |
| Same mixed shape on a shipping compiler | CS0747 |

The positive control is load-bearing: without it, "it errors" would be indistinguishable from failing to
enable the feature. The gate is `LanguageVersion.Preview`, not a `/features:` flag.

So the grammar change lands and the mixed shape becomes well-formed — and is then rejected by the
`IEnumerable` rule. The error moves from `CS0747` to `CS1922`; the outcome for an immutable tree does not
change. Their binder agrees: in `BindObjectInitializerExpression`, the first non-member-shape child calls
`CollectionInitializerTypeImplementsIEnumerable` and reports `ERR_CollectionInitRequiresIEnumerable` when it
is false.

Adopting `#10185` would therefore require every container to implement `IEnumerable` **and** expose a
mutating `Add` — which is precisely spec 019 §8.2's "internal mutable builder, freeze on read" and "accept
the internal mutability", the two approaches that section rejected.

Routing content to an `init` collection member removes the problem outright, and is the same mechanism
Option A′ already uses — just with the property name elided. Notably, it also makes `[CollectionBuilder]`
types, `ImmutableArray<T>` and spans reachable as content properties, none of which `Add` can serve.

### 8.1 This is not a critique — it is the mechanism that was built

The alternative was implemented first (Stage 3, §3); the comparison above is why it is necessary rather than
redundant. On the **same** type declaration that produces `CS1922`, plus two attribute applications and
nothing else:

```csharp
[FactoryInitializable]                              // +1 line
public abstract record Element;

[ContentProperty(nameof(Children))]                 // +1 line
public record StackElement(Orientation Orientation, Element[] Children) : Element
{
    public double Spacing { get; init; } = 8;
}

var tree = VStack {
    Spacing = 4,
    new TextElement("a"),
    new TextElement("b"),
    ..extra.Select(s => new TextElement(s)),
};
```

```
Spacing  = 4
Children = [a, b, c, d]
implements IEnumerable = False   has Add = False
OK: immutable record initialized with properties + bare children + spread, no Add, no IEnumerable.
```

The `IEnumerable`/`Add` state is asserted at runtime, so the run fails if the type ever acquires either.
Repro: `ContentPropertyAnswer.cs` against `MixedInitLimit2.cs`.

---

## 9. Known gaps and residual risk

1. **No `factory` modifier — and that is now the aligned choice, not a shortcut.** Opt-in is spelled with
   attributes, which is what the championed proposal
   ([`factory-methods.md`](https://github.com/dotnet/csharplang/blob/main/proposals/factory-methods.md)) also
   does. What the prototype does **not** implement from that proposal: the restriction that a factory body
   must be a construction-like expression (fresh-instance verification), the requirement that the annotation
   match across overrides and interface implementations, and the `IsFactory` modreq for virtual members.
   Reactor's factories all satisfy the fresh-instance rule today, so this does not affect the measurements,
   but a shipping feature needs it.
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

## 10. Recommendations to the working group

1. **Do not ship factory initializers as a standalone narrow feature.** Measured on non-golden-path code it is
   a 17 % regression against the API this framework ships today. Bundle it with content elements or drop it.
2. **Do not adopt either opt-in as specified.** Member-level `[Factory]` fails the LDM's composability
   requirement and its churn is larger than it looks (§5.1: the annotation spreads from 203 factories into
   the 473 fluent extension methods). Type-level `[FactoryInitializable]` is composable and costs one
   attribute, but is **unsound without the body restriction** — demonstrated by corrupting Reactor's
   `EmptyElement.Instance` singleton. Adopt the synthesis in §5.2: permission at the type, proof inferred at
   the member, explicit `[Factory]` retained as an assertion.
3. **Require content elements to be trailing.** Cheap, well-precedented, and it retires a standing objection.
4. **Stop attributing the allocation win to construction syntax.** It comes from property placement and Reactor
   can have it today.
5. **Prototype target-typed content control flow next** (§6.4), then nested member paths (§6.5). These attack
   the patterns that every current variant handles badly. Note that #9003 (nested paths) currently says
   "Specification: None yet" and is marked *Needs More Work*, so a prototype there is defining the design,
   not implementing one.
6. **Engage the in-flight mixed-initializer stack** (`dotnet/roslyn` #83750–#83761, §8). The disambiguation
   rule is already settled and agrees with this prototype; the open questions that decide whether the feature
   is usable for immutable UI trees — `Add` versus a content property, spread inside `{ }`, and trailing-only
   ordering — are still live and are exactly where this experiment has data.

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

> **Gotcha:** `ParseOptions.HasFeature` only checks that a value is present, so `/features:FactoryInitializers=false`
> still *enables* the feature. Omit the flag entirely to disable it. The "flags off" verification in §3 was run
> by omitting the flags, not by setting them to `false`.

## Appendix B — Before and after, per experiment

Every "after" below compiles. Where it compiles against the **real** `Reactor.dll` that is stated; where it
was validated on the faithful mini-model instead, that is stated too, with the reason.

### B.1 Stage 1 — factory initializers

Configuration moves out of `.Set()` callbacks and fluent chains into one brace-delimited block on the
element it configures.

```csharp
// before
Text("todos").FontSize(36)
    .Set(t => t.FontWeight = FontWeights.Light)
    .Foreground(AccentText)
    .HAlign(HorizontalAlignment.Center)

// after
Text("todos") { FontSize = 36, Weight = FontWeights.Light,
                Foreground = AccentText, HAlign = HorizontalAlignment.Center }
```

Because the initializer binds at primary-expression precedence, a fluent chain still attaches on either side
with no parentheses — unlike `with`, which forces the chain to go first:

```csharp
Text("hi").Bold() { FontSize = 14 }          // chain before
Text("hi") { FontSize = 14 }.Flex(grow: 1)   // chain after — no parens needed
```

Parens may be omitted when an overload resolves with zero arguments. Against real Reactor this works for
`VStack`/`HStack` (`params Element?[]`) but **not** for single-child factories:

```
Border { child }
  -> error CS7036: no argument given for required parameter 'child' of Factories.Border(Element?)
```

Migration requirement: single-child factories need a `= null` default before the parens-omitted form is
available for them.

### B.2 Stage 2 — type-level opt-in

Nothing changes at the call site. The whole delta is at the library:

```csharp
// after — the member-level design, applied to every factory
[Factory] public static StackElement VStack(params Element?[] children) => …   // ×203
[Factory] public static TextBlockElement TextBlock(string content) => …
[Factory] public static ButtonElement Button(string label, Action? onClick = null) => …

// after — the type-level design
[FactoryInitializable]                       // ×1
public abstract record Element { … }
```

What this buys is composability. Extract a subtree into a helper and, under type-level opt-in, callers are
unaffected:

```csharp
static Panel TodoRow(TodoItem item) => new Panel { … };   // no annotation anywhere
var row = TodoRow(item) { Margin = 4 };                   // still compiles
```

Under member-level opt-in the same extraction fails until the helper's author remembers the attribute
(`CS9700`). See §5.1 for why type-level opt-in is nonetheless unsound on its own, and §5.2 for the synthesis.

### B.3 Stage 3 — content elements

```csharp
// before
VStack(0,
    Text("todos").FontSize(36),
    HStack(8,
        TextField(state.NewItemText, setText),
        Button(addCmd)
    ).Padding(16, 8, 16, 8),
    ScrollViewer(
        VStack(0, filtered.Select(item => TodoRow(item, dispatch)).ToArray())
    )
)

// after
VStack {
    Spacing = 0,
    Text("todos") { FontSize = 36 },
    HStack {
        Spacing = 8,
        TextField(state.NewItemText, setText),
        Button(addCmd),
    }.Padding(16, 8, 16, 8),
    ScrollViewer(
        VStack { Spacing = 0, ..filtered.Select(item => TodoRow(item, dispatch)) }
    ),
}
```

Note what did **not** appear: no `new`, no `Children = [ … ]`, and `.ToArray()` is gone because the spread
consumes the `IEnumerable<Element>` directly.

Verified against the real `Reactor.dll`: `VStack { Spacing = 12, … }`, `HStack { Spacing = 8, Key = …, … }`
and `..Items.Select(…)` in content position all compile against Reactor's actual `StackElement`,
`TextBlockElement` and `ButtonElement`. Runtime comparison of the two trees is **not** possible headlessly —
`TextBlockElement`'s generated static constructor touches `TextBlock.FontFamilyProperty` and throws
`REGDB_E_CLASSNOTREG` outside a WinUI-initialised process, which is exactly why this repo has a selftest
tier. The runtime equality oracle therefore runs on the mini-model (§4), and a real-Reactor version belongs
in `tests/Reactor.AppTests.Host` as a selftest fixture.

### B.4 The measured comparison (§4)

```csharp
// Option A' — ships today, no language change
new StackElement(Orientation.Vertical, [
    new TextElement("Folders") { Modifiers = new ElementModifiers { Margin = 8 } },
]) { Spacing = 0 }

// Factory initializers v1 — the LDM reference design
VStack { Spacing = 0, Children = [ Text("Folders").Margin(8) ] }

// Factory initializers + content elements
VStack { Spacing = 0, Text("Folders").Margin(8) }
```

### B.5 The singleton hazard (§5.1)

```csharp
// before — a real Reactor factory, src/Reactor/Elements/Dsl.cs:1351
public static Element Empty() => EmptyElement.Instance;   // static readonly singleton

// after — with type-level opt-in and no body restriction, this compiles:
var spacer = Empty() { Margin = 4 };
```

```
before           : Instance.Margin = 0
after Empty(){4} : Instance.Margin = 4
unrelated Empty(): Margin = 4   <-- should be 0
```

6 of Reactor's 198 factories have this shape. The fix is §5.2, not "be careful".

### B.6 `#10185`'s mechanism vs. this one (§8)

```csharp
// #10185's mechanism, applied to Reactor's StackElement
new StackElement(Orientation.Vertical, []) { Spacing = 4, new TextElement("child") }
  -> error CS1922: Cannot initialize type 'StackElement' with a collection initializer
                   because it does not implement 'System.Collections.IEnumerable'

// this prototype's mechanism, same type, +2 attributes
VStack { Spacing = 4, new TextElement("child"), ..extra.Select(s => new TextElement(s)) }
  -> Children = [child, c, d];  implements IEnumerable = False;  has Add = False
```

### B.7 Everything together

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
modifiers all compose. This compiles and runs on the prototype compiler today (mini-model; see B.3 for the
real-`Reactor.dll` compile result and why the runtime half needs a selftest).
