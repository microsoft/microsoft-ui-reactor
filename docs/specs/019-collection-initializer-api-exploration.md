# Collection Initializers & Properties as an Alternative to Fluent Modifiers

## Status

**Exploration / RFC** — 2026-04-15.

---

## Motivation

Reactor's current API is built on **fluent method chaining**: factory methods return immutable records, and
extension methods return modified copies via `el with { ... }`. This model is clean for simple
cases but introduces friction at scale:

1. **Three distinct syntax modes** coexist in user code — fluent methods (`.Padding(12)`), `with { }`
   for record properties (`with { ColumnGap = 8 }`), and `.Set()` callbacks for native WinUI
   properties — forcing developers to learn when to use which.
2. **Long chains are hard to scan** — a 6-modifier chain reads linearly, but properties aren't
   visually grouped (layout vs. styling vs. events).
3. **Every new property requires a new extension method** — the API surface (1400+ lines in
   `ElementExtensions.cs`) grows linearly with the platform it wraps.

This spec explores what the Reactor API would look like if it leaned into **C# object/collection
initializers and properties** instead of (or alongside) fluent methods. It does not propose
replacing the current API wholesale — it explores the design space to understand trade-offs.

### Prior Art Within This Project

[Spec 008 §5 — Collection Initializer Trees](008-csharp-language-improvements.md#5-collection-initializer-trees)
already explored the C# language gap where collection initializers only work after `new`, not after
factory methods. This spec takes a different angle: **what can we do today with existing C# syntax**,
and what are the trade-offs vs. the current fluent model?

---

## Part 1: The Current Model (Baseline)

### TodoApp — Current Fluent API

```csharp
return VStack(0,
    Text("todos").FontSize(36)
        .Set(t => t.FontWeight = FontWeights.Light)
        .Foreground(AccentText)
        .HAlign(HorizontalAlignment.Center)
        .Margin(0, 16, 0, 8),

    HStack(8,
        TextField(state.NewItemText, v => dispatch(new SetNewItemText(v)))
            .Set(tb => tb.PlaceholderText = "What needs to be done?")
            .HAlign(HorizontalAlignment.Stretch),
        Button(addCmd)
    ).Padding(16, 8, 16, 8)
     .Background(CardBackground),

    ScrollViewer(
        VStack(0,
            filtered.Select(item => TodoRow(item, dispatch)).ToArray()
        )
    ).Flex(grow: 1, basis: 0),

    HStack(8,
        Text($"{remaining} items left").FontSize(12).Foreground(SecondaryText)
            .VAlign(VerticalAlignment.Center),
        Empty().HAlign(HorizontalAlignment.Stretch),
        FilterButton("All", "all", state.Filter, dispatch),
        FilterButton("Active", "active", state.Filter, dispatch),
        FilterButton("Completed", "completed", state.Filter, dispatch)
    ).Padding(12, 8, 12, 8)
     .WithBorder(DividerStroke)
).Background(SolidBackground)
 .MaxWidth(600)
 .HAlign(HorizontalAlignment.Center);
```

### Outlook MessageRow — Current Fluent API

```csharp
var senderLine = FlexRow(
    Text(msg.SenderName).FontSize(14)
        .Set(t => { t.FontWeight = bold; t.TextTrimming = TextTrimming.CharacterEllipsis; })
        .Flex(grow: 1),
    Text(dateStr).FontSize(12).Foreground(TertiaryText)
) with { ColumnGap = 8 };

return Button(
    Grid(["*"], ["*"],
        content.Padding(14, 10, 14, 10).Grid(row: 0, column: 0),
        unreadBar.Grid(row: 0, column: 0)
    ),
    Props.OnSelected
).Set(b =>
{
    b.Background = bg;
    b.BorderThickness = new Thickness(0, 0, 0, 1);
    b.BorderBrush = BorderBrush;
    b.Padding = new Thickness(0);
    b.HorizontalAlignment = HorizontalAlignment.Stretch;
    b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
    b.CornerRadius = new CornerRadius(0);
    b.Resources["ButtonBackgroundPointerOver"] = HoverBrush;
    b.Resources["ButtonBackgroundPressed"] = SelectedBrush;
});
```

### What the Current Model Does Well

- **Concise for simple cases** — `Text("Hello").Bold().Margin(8)` is hard to beat.
- **Type preservation** — generic `T` extensions preserve the concrete type through chains, so
  `Text("hi").Bold().Set(tb => ...)` still knows it's a `TextElement`.
- **Composable** — modifiers can be applied at any point, conditionally, from helper methods.
- **Discoverable** — IntelliSense on `.` shows all available modifiers.

### Where the Current Model Has Friction

| Pain point | Example | Frequency |
|---|---|---|
| **Three syntax modes** | `.Padding()` vs `with { ColumnGap = 8 }` vs `.Set(b => ...)` | Every complex component |
| **`.Set()` as escape hatch** | `.Set(t => t.FontWeight = FontWeights.Light)` for a property that could be `.FontWeight(FontWeights.Light)` | ~30% of real-world components |
| **API surface bloat** | 1400+ lines of extension methods, 500+ `.Set()` overloads, growing with every new WinUI property | Maintenance burden |
| **Record property gap** | FlexElement's `ColumnGap`, `RowGap`, `JustifyContent` require `with { }` not fluent methods | Every FlexRow usage |
| **Modifier order is invisible** | `.Background().CornerRadius().Padding()` — is layout mixed with styling? | Code review |
| **Allocated ModifierObjects** | Each `.Margin()` creates a new `ElementModifiers` record and merges — O(n) per modifier | Hot render paths |

---

## Part 2: Option A — `new` + Object/Collection Initializers (Works Today)

### The Idea

Replace factory methods (`VStack(...)`, `Text(...)`) with `new` expressions and use C#'s native
object/collection initializer syntax. Container elements get `IEnumerable<Element>` + `Add()`
so children go inside `{ }`. Properties are set via object initializer syntax.

### Required Type Changes

```csharp
// StackElement adds IEnumerable<Element> + Add() for collection initializer support
public record StackElement : Element, IEnumerable<Element>
{
    public Orientation Orientation { get; init; } = Orientation.Vertical;
    public double Spacing { get; init; } = 8;

    // Layout modifiers promoted to properties
    public double? Width { get; init; }
    public double? Height { get; init; }
    public Thickness? Margin { get; init; }
    public Thickness? Padding { get; init; }
    public Brush? Background { get; init; }
    public Brush? Foreground { get; init; }
    public HorizontalAlignment? HAlign { get; init; }
    public VerticalAlignment? VAlign { get; init; }
    // ... all ElementModifiers promoted to settable init properties

    // Mutable child list for collection initializer, frozen on first read
    private List<Element>? _children;
    public void Add(Element? child) { if (child != null) (_children ??= new()).Add(child); }
    public Element[] Children => _children?.ToArray() ?? [];
    public IEnumerator<Element> GetEnumerator() => ((IEnumerable<Element>)(Children)).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// TextElement — leaf, no collection initializer needed
public record TextElement : Element
{
    public string Content { get; init; } = "";
    public double? FontSize { get; init; }
    public FontWeight? Weight { get; init; }
    public bool Bold { get; init; }
    // ... modifiers as properties
}
```

### TodoApp — Option A

```csharp
return new VStack {
    Spacing = 0,
    Background = SolidBackground,
    MaxWidth = 600,
    HAlign = HorizontalAlignment.Center,

    // Header
    new Text("todos") {
        FontSize = 36,
        Weight = FontWeights.Light,
        Foreground = AccentText,
        HAlign = HorizontalAlignment.Center,
        Margin = Thick(0, 16, 0, 8),
    },

    // Input bar
    new HStack {
        Spacing = 8,
        Padding = Thick(16, 8, 16, 8),
        Background = CardBackground,

        new TextField {
            Value = state.NewItemText,
            OnChanged = v => dispatch(new SetNewItemText(v)),
            Placeholder = "What needs to be done?",
            HAlign = HorizontalAlignment.Stretch,
        },
        new Button(addCmd),
    },

    // List
    new ScrollView {
        Flex = new(grow: 1, basis: 0),
        new VStack {
            Spacing = 0,
            filtered.Select(item => TodoRow(item, dispatch)).ToArray(),
        },
    },

    // Footer
    new HStack {
        Spacing = 8,
        Padding = Thick(12, 8, 12, 8),
        Border = new(DividerStroke),

        new Text($"{remaining} items left") {
            FontSize = 12, Foreground = SecondaryText,
            VAlign = VerticalAlignment.Center,
        },
        new Spacer(),
        FilterButton("All", "all", state.Filter, dispatch),
        FilterButton("Active", "active", state.Filter, dispatch),
        FilterButton("Completed", "completed", state.Filter, dispatch),
    },
};
```

### Outlook MessageRow — Option A

```csharp
var senderLine = new FlexRow {
    ColumnGap = 8,

    new Text(msg.SenderName) {
        FontSize = 14,
        Weight = bold,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Flex = new(grow: 1),
    },
    new Text(dateStr) {
        FontSize = 12,
        Foreground = TertiaryText,
    },
};

return new Button(Props.OnSelected) {
    Background = bg,
    BorderThickness = Thick(0, 0, 0, 1),
    BorderBrush = BorderBrush,
    Padding = Thick(0),
    HAlign = HorizontalAlignment.Stretch,
    HContentAlign = HorizontalAlignment.Stretch,
    CornerRadius = 0,
    Resources = r => r
        .Set("ButtonBackgroundPointerOver", HoverBrush)
        .Set("ButtonBackgroundPressed", SelectedBrush),

    Content = new Grid(["*"], ["*"]) {
        content.At(row: 0, column: 0).WithPadding(14, 10, 14, 10),
        unreadBar.At(row: 0, column: 0),
    },
};
```

### Analysis — Option A

**What's better:**
- **No more `.Set()` escape hatch** — all properties are directly settable via object initializer.
  `FontWeight`, `TextTrimming`, `HorizontalContentAlignment` — just assign them.
- **Properties are visually grouped** — layout, styling, and content are naturally grouped in
  `{ }` blocks rather than strung along a chain.
- **`{ }` closes better than `)`** — IDE brace matching, folding, and indentation all work better
  with braces. No more walls of `), ), )`.
- **Eliminates the three-syntax split** — no more choosing between `.Padding()` vs
  `with { ColumnGap = 8 }` vs `.Set()`. Everything is `Property = value` or a child element.
- **Drastically smaller API surface** — no need for 1400+ lines of extension methods. Properties
  are just `{ get; init; }` on the record. Adding a new WinUI property means adding one line.
- **`FlexRow { ColumnGap = 8 }` is cleaner than `FlexRow(...) with { ColumnGap = 8 }`** — the
  awkward `with { }` on factory results disappears.

**What's worse:**
- **`new` keyword noise** — every element gets `new`. The fluent factory methods (`VStack`, `Text`,
  `Button`) are cleaner than `new VStack`, `new Text`, `new Button`. This is significant visual
  noise in deep trees.
- **Children arrays are awkward** — inserting a computed `filtered.Select(...).ToArray()` inside
  `{ }` needs the collection initializer `Add()` to handle arrays, or developers wrap in a spread.
  Today's `params Element?[]` is more natural.
- **Loss of type-safe chains** — `Text("hi").Bold().Margin(8)` is concise and type-safe. The
  initializer equivalent `new Text("hi") { Bold = true, Margin = Thick(8) }` is more verbose for
  simple cases.
- **Immutability tension** — collection initializers call `Add()` mutably. This conflicts with
  records being immutable value types. Requires internal mutability behind an immutable facade,
  which is a correctness risk (concurrent reads during build, re-entrancy).
- **Loss of conditional modifier composition** — fluent chains can conditionally apply modifiers:
  `el.Margin(8).If(isActive, e => e.Background("red"))`. Initializers are static declarations —
  conditional properties require ternary inline: `Background = isActive ? "red" : null`.
- **`null` children** — `condition ? Text("yes") : null` works in `params Element?[]` because
  the factory method's `FilterChildren` strips nulls. Collection initializers call `Add(null)` which
  the `Add()` method must handle (guard check on every call).
- **Helper methods can't return partially-configured elements** — today you can write
  `static Element StyledButton(string label) => Button(label).Padding(8).Background(Accent)`.
  With initializers, the properties are set at the call site, not inside the helper, unless the
  helper returns `new Button { Padding = ..., Background = ... }` which loses the collection
  initializer capability.

**Verdict:** Strong improvement for complex components (Outlook, regedit), worse for simple ones
(TodoApp helpers, one-liners). The `new` keyword noise is the biggest downside. The `IEnumerable` +
`Add()` approach also has a fundamental limitation: you can't splat `.Select().ToArray()` inline
(see Part 7B stress tests).

---

## Part 2B: Option A' — `new` + Collection Expressions for Children (Works Today, C# 12+)

### The Idea

A variant of Option A that drops the `IEnumerable` + `Add()` collection initializer pattern
entirely. Instead, container elements have an explicit `Children` property typed as `Element?[]`
(with `init`), and developers use **C# 12 collection expressions** (`[a, b, c]`) to populate it.

This fixes Option A's two biggest flaws:
1. **No immutability hack** — no mutable `Add()` method. `Children` is a pure `init` property.
2. **Spread works** — `[..items.Select(Render)]` splats LINQ results inline, mixing freely
   with static children.

### Required Type Changes

```csharp
// No IEnumerable, no Add() — just an init property for children
public record StackElement : Element
{
    public Orientation Orientation { get; init; } = Orientation.Vertical;
    public double Spacing { get; init; } = 8;
    public Element?[] Children { get; init; } = [];

    // Layout modifiers promoted to properties (same as Option A)
    public double? Width { get; init; }
    public double? Height { get; init; }
    public Thickness? Margin { get; init; }
    public Thickness? Padding { get; init; }
    public Brush? Background { get; init; }
    // ... all ElementModifiers promoted to init properties
}

public record FlexElement : Element
{
    public Flex.FlexDirection Direction { get; init; } = Flex.FlexDirection.Row;
    public Flex.FlexJustify JustifyContent { get; init; }
    public double ColumnGap { get; init; }
    public double RowGap { get; init; }
    public Flex.FlexWrap Wrap { get; init; }
    public Element?[] Children { get; init; } = [];
    // ...
}

// Leaf elements — no Children property
public record TextElement : Element
{
    public string Content { get; init; } = "";
    public double? FontSize { get; init; }
    public FontWeight? Weight { get; init; }
    // ...
}
```

### TodoApp — Option A'

```csharp
return new VStack {
    Spacing = 0,
    Background = SolidBackground,
    MaxWidth = 600,
    HAlign = HorizontalAlignment.Center,
    Children = [
        // Header
        new Text("todos") {
            FontSize = 36,
            Weight = FontWeights.Light,
            Foreground = AccentText,
            HAlign = HorizontalAlignment.Center,
            Margin = Thick(0, 16, 0, 8),
        },

        // Input bar
        new HStack {
            Spacing = 8,
            Padding = Thick(16, 8, 16, 8),
            Background = CardBackground,
            Children = [
                new TextField {
                    Value = state.NewItemText,
                    OnChanged = v => dispatch(new SetNewItemText(v)),
                    Placeholder = "What needs to be done?",
                    HAlign = HorizontalAlignment.Stretch,
                },
                new Button(addCmd),
            ],
        },

        // List — spread shines here
        new ScrollView {
            Flex = new(grow: 1, basis: 0),
            Child = new VStack {
                Spacing = 0,
                Children = [..filtered.Select(item => TodoRow(item, dispatch))],
            },
        },

        // Footer
        new HStack {
            Spacing = 8,
            Padding = Thick(12, 8, 12, 8),
            Border = new(DividerStroke),
            Children = [
                new Text($"{remaining} items left") {
                    FontSize = 12, Foreground = SecondaryText,
                    VAlign = VerticalAlignment.Center,
                },
                new Spacer(),
                FilterButton("All", "all", state.Filter, dispatch),
                FilterButton("Active", "active", state.Filter, dispatch),
                FilterButton("Completed", "completed", state.Filter, dispatch),
            ],
        },
    ],
};
```

### Outlook MessageRow — Option A'

```csharp
var senderLine = new FlexRow {
    ColumnGap = 8,
    Children = [
        new Text(msg.SenderName) {
            FontSize = 14,
            Weight = bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Flex = new(grow: 1),
        },
        new Text(dateStr) { FontSize = 12, Foreground = TertiaryText },
    ],
};

return new Button(Props.OnSelected) {
    Background = bg,
    BorderThickness = Thick(0, 0, 0, 1),
    BorderBrush = BorderBrush,
    Padding = Thick(0),
    HAlign = HorizontalAlignment.Stretch,
    HContentAlign = HorizontalAlignment.Stretch,
    CornerRadius = 0,
    Resources = r => r
        .Set("ButtonBackgroundPointerOver", HoverBrush)
        .Set("ButtonBackgroundPressed", SelectedBrush),
    Content = new Grid(["*"], ["*"]) {
        Children = [
            content with { Padding = Thick(14, 10, 14, 10), Grid = new(0, 0) },
            unreadBar with { Grid = new(0, 0) },
        ],
    },
};
```

### Analysis — Option A'

**Compared to Option A, what's different:**

| | Option A (`Add()`) | Option A' (`Children = [...]`) |
|---|---|---|
| Immutability | Requires mutable `Add()` hack | Pure `init` property — fully immutable |
| Spread / splat | Can't splat arrays inline | `[..expr]` works natively |
| Mixing static + dynamic | Broken | `[static1, ..dynamic, static2]` works |
| Null handling | `Add(null)` custom no-op | Needs `Element?[]` or filter post-init |
| Bare children in `{ }` | `new VStack { child1, child2 }` — clean | `new VStack { Children = [child1, child2] }` — noisier |
| Syntax weight | Lighter for static trees | `Children = [` adds ~15 chars per container |

**What's better than Option A:**
- **No immutability tension** — `Children` is a pure `init` property. The record is never mutated.
- **Spread solves the LINQ problem** — `[..items.Select(Render)]` works inline, mixing freely
  with static children. This was Option A's fatal flaw.
- **`.SelectMany()` works** — `[..grid, ..series.SelectMany(...)]` is natural.
- **Conditional children via spread** — `[header, ..(condition ? [extra] : []), footer]`.

**What's worse than Option A:**
- **`Children = [` boilerplate** — every container needs this prefix. For a 2-child VStack,
  `new VStack { child1, child2 }` (Option A) is cleaner than
  `new VStack { Children = [child1, child2] }` (A'). The extra `Children = [` and `]` add noise.
- **Nested `Children = [` gets deep** — each nesting level adds another `Children = [` line,
  increasing indentation pressure.

**What's the same as Option A:**
- **`new` keyword noise** — still present on every element.
- **Properties via object initializer** — `{ FontSize = 14, Weight = bold }` — same.
- **No `.Set()` needed** — same as A.
- **API surface reduction** — same as A.

**Compared to other options:**
- **Vs. Option B** (factory + `with { }`): A' has clearer visual grouping (`{ }` for everything)
  but more noise (`new`, `Children = [`). B has cleaner simple cases but the `with { }` precedence
  gotcha.
- **Vs. Current**: A' eliminates `.Set()` and the three-syntax split but adds `new` noise
  and `Children = [` boilerplate. Strictly better for 5+ property elements, strictly worse for
  1-2 property one-liners.

**Verdict:** A' fixes Option A's two fatal flaws (immutability and LINQ splatting) while retaining
its structural clarity. The `Children = [...]` boilerplate is the main cost — it's ~15 extra
characters per container element. Whether that cost is acceptable depends on how much you value
the visual consistency of `{ }` blocks over the terseness of `params` arrays.

---

## Part 3: Option B — Factory Methods + `with { }` for Everything

### The Idea

Keep factory methods (`VStack`, `Text`, `Button`) but promote all common modifiers to `init`
properties on the element records. Use `with { }` consistently instead of fluent chains for
anything beyond the factory method's positional parameters.

This already partially works today — `FlexRow(...) with { ColumnGap = 8 }` is valid C#.
The change is to **make this the primary style** by putting layout/style properties directly on
each element record instead of hiding them in `ElementModifiers`.

### Required Changes

```csharp
// Element base gets all common modifiers as init properties
public abstract record Element
{
    public string? Key { get; init; }
    public Thickness? Margin { get; init; }
    public Thickness? Padding { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public Brush? Background { get; init; }
    public Brush? Foreground { get; init; }
    public double? Opacity { get; init; }
    public HorizontalAlignment? HAlign { get; init; }
    public VerticalAlignment? VAlign { get; init; }
    // ... all former ElementModifiers become init properties on Element
}
```

### TodoApp — Option B

```csharp
return VStack(0,
    Text("todos") with {
        FontSize = 36,
        Weight = FontWeights.Light,
        Foreground = AccentText,
        HAlign = HorizontalAlignment.Center,
        Margin = Thick(0, 16, 0, 8),
    },

    HStack(8,
        TextField(state.NewItemText, v => dispatch(new SetNewItemText(v)))
            with { Placeholder = "What needs to be done?", HAlign = HorizontalAlignment.Stretch },
        Button(addCmd)
    ) with { Padding = Thick(16, 8, 16, 8), Background = CardBackground },

    ScrollViewer(
        VStack(0, filtered.Select(item => TodoRow(item, dispatch)).ToArray())
    ) with { Flex = new(grow: 1, basis: 0) },

    HStack(8,
        Text($"{remaining} items left")
            with { FontSize = 12, Foreground = SecondaryText, VAlign = VerticalAlignment.Center },
        Spacer(),
        FilterButton("All", "all", state.Filter, dispatch),
        FilterButton("Active", "active", state.Filter, dispatch),
        FilterButton("Completed", "completed", state.Filter, dispatch)
    ) with { Padding = Thick(12, 8, 12, 8), Border = new(DividerStroke) }
) with { Background = SolidBackground, MaxWidth = 600, HAlign = HorizontalAlignment.Center };
```

### Outlook MessageRow — Option B

```csharp
var senderLine = FlexRow(
    Text(msg.SenderName) with {
        FontSize = 14,
        Weight = bold,
        TextTrimming = TextTrimming.CharacterEllipsis,
        Flex = new(grow: 1),
    },
    Text(dateStr) with { FontSize = 12, Foreground = TertiaryText }
) with { ColumnGap = 8 };

return Button(
    Grid(["*"], ["*"],
        content with { Padding = Thick(14, 10, 14, 10), Grid = new(0, 0) },
        unreadBar with { Grid = new(0, 0) }
    ),
    Props.OnSelected
) with {
    Background = bg,
    BorderThickness = Thick(0, 0, 0, 1),
    BorderBrush = BorderBrush,
    Padding = Thick(0),
    HAlign = HorizontalAlignment.Stretch,
    HContentAlign = HorizontalAlignment.Stretch,
    CornerRadius = 0,
    Resources = r => r
        .Set("ButtonBackgroundPointerOver", HoverBrush)
        .Set("ButtonBackgroundPressed", SelectedBrush),
};
```

### Analysis — Option B

**What's better:**
- **No `new` keyword** — keeps the clean factory method names.
- **Eliminates `.Set()` for common properties** — `FontWeight`, `TextTrimming`, `CornerRadius`
  all become direct init properties via `with { }`.
- **Consistent with existing `with { }` usage** — the codebase already uses
  `FlexRow(...) with { ColumnGap = 8 }`. This just makes it universal.
- **Children remain as params arrays** — no `IEnumerable`/`Add()` complexity.
  `VStack(0, child1, child2, child3)` still works.
- **Helper methods still work** — `static Element StyledButton(string label) => Button(label) with { Padding = Thick(8), Background = Accent }` is natural.
- **Record copy semantics work correctly** — `with { }` is the intended way to copy-mutate
  records. No immutability hacks.

**What's worse:**
- **Verbosity for simple cases** — `Text("hi").Bold().Margin(8)` becomes
  `Text("hi") with { Bold = true, Margin = Thick(8) }`. More characters, less fluid.
- **Parentheses + `with { }` is noisy** — `VStack(0, ...) with { Background = red }` has both
  `)` and `}` delimiters at the same nesting level. It's two distinct syntactic constructs on one
  line.
- **`with { }` creates a full record copy** — every `with { }` allocates a new record instance.
  For a 5-property `with { }` block, C# copies all fields then overwrites 5. This is roughly
  the same cost as 5 chained modifier calls, so no worse, but also no better.
- **Property explosion on Element base** — moving all modifiers from `ElementModifiers` to `Element`
  adds ~40 properties to the abstract record. Every concrete element inherits them all. The
  `ShallowEquals` / `DiffProps` logic in the reconciler must compare all of them.
- **No IntelliSense after `with`** — while `with { }` does offer IntelliSense for properties, the
  experience is worse than method chaining. After `.`, IntelliSense shows all available methods
  filtered by type. After `with {`, it shows ALL properties on the type (including inherited ones),
  which is noisier.
- **Loses the fluent chain aesthetic** — the current API reads top-to-bottom:
  `.FontSize(14).Bold().Margin(8)`. The `with { }` block reads as a clump of assignments
  without that flowing quality.
- **Harder to conditionally compose** — you can't easily conditionally add a property to a `with`
  block. You'd need: `var el = Text("hi") with { FontSize = 14 }; if (cond) el = el with { Bold = true };`
  which is much worse than `Text("hi").FontSize(14).If(cond, Bold)`.

**Verdict:** A solid middle ground. The `with { }` block is great for complex configuration
(MessageRow) but feels heavy for simple chains. Biggest win: eliminating the
fluent/with/Set three-way split. Biggest loss: simple cases get more verbose.

---

## Part 4: Option C — Hybrid (Fluent + Properties for What Fluent Can't Reach)

### The Idea

Keep the current fluent API for common modifiers but **promote element-specific properties** to
`init` properties on the records, so `with { }` becomes the standard way to set element-specific
configuration. Fluent methods handle cross-cutting concerns (layout, styling); `with { }` handles
type-specific config.

The rule: if a property is common across many element types (Margin, Padding, Background, Foreground,
Width, Height, Opacity, etc.), it stays as a fluent extension method. If it's type-specific
(ColumnGap, TextTrimming, FontWeight, PlaceholderText, IsReadOnly), it's an init property accessed
via `with { }`.

### Required Changes

Minimal. Most type-specific properties are *already* init properties on the records. The change is:
- Remove the `.Set()` overloads
- Remove fluent extension methods that duplicate init properties (e.g., `.Spacing()` on StackElement)
- Promote the most common `.Set()` targets to init properties on their element records
- Document the convention: fluent for layout/style, `with { }` for element config

### TodoApp — Option C

```csharp
return VStack(0,
    // Header
    Text("todos")
        .FontSize(36)
        .Foreground(AccentText)
        .HAlign(HorizontalAlignment.Center)
        .Margin(0, 16, 0, 8)
        with { Weight = FontWeights.Light },

    // Input bar
    HStack(8,
        TextField(state.NewItemText, v => dispatch(new SetNewItemText(v)))
            with { Placeholder = "What needs to be done?" }
            .HAlign(HorizontalAlignment.Stretch),
        Button(addCmd)
    ).Padding(16, 8, 16, 8)
     .Background(CardBackground),

    // List
    ScrollViewer(
        VStack(0, filtered.Select(item => TodoRow(item, dispatch)).ToArray())
    ).Flex(grow: 1, basis: 0),

    // Footer
    HStack(8,
        Text($"{remaining} items left")
            .FontSize(12).Foreground(SecondaryText).VAlign(VerticalAlignment.Center),
        Spacer(),
        FilterButton("All", "all", state.Filter, dispatch),
        FilterButton("Active", "active", state.Filter, dispatch),
        FilterButton("Completed", "completed", state.Filter, dispatch)
    ).Padding(12, 8, 12, 8).WithBorder(DividerStroke)
).Background(SolidBackground).MaxWidth(600).HAlign(HorizontalAlignment.Center);
```

### Analysis — Option C

**What's better:**
- **Minimal migration** — keep 90% of the current API. Only add init properties for what `.Set()`
  is currently used for.
- **Simple cases stay simple** — `Text("hi").Bold().Margin(8)` unchanged.
- **Eliminates `.Set()` for known properties** — `FontWeight`, `TextTrimming`, `PlaceholderText`
  become init properties, so `with { Weight = FontWeights.Light }` replaces
  `.Set(t => t.FontWeight = FontWeights.Light)`.
- **Familiar** — existing Reactor developers don't need to relearn anything.

**What's worse:**
- **Still two syntax modes** — fluent for modifiers, `with { }` for type-specific. Better than
  three (no more `.Set()`), but not unified.
- **Chaining after `with { }` is questionable** —
  `TextField(...) with { Placeholder = "..." }.HAlign(Stretch)` — does this work? Yes, but the
  semantics are: create a TextField, copy it with Placeholder set, then copy it again with HAlign.
  Three copies. And visually it reads oddly — a `with { }` block then a `.Method()` tail.
- **Where to draw the line** — which properties are "common enough" for fluent methods vs.
  "type-specific enough" for `with { }`? This is subjective and will lead to inconsistencies.
  Is `.Foreground()` common (on Text, Button, etc.)? Yes. Is `.TextWrapping()`? It's on Text and
  TextField only — fluent or property?

**Verdict:** Lowest risk, smallest gain. Reduces the three-syntax problem to two-syntax. Good
as an incremental improvement but doesn't address the fundamental API shape question.

---

## Part 5: Option D — `new` With Factory Aliases (Best of A + B)

### The Idea

Use `new` + object/collection initializers (Option A) but add `using` aliases to hide the noise:

```csharp
using V = Reactor.Core.StackElement;   // or: using VStack = Reactor.Core.StackElement;
using H = Reactor.Core.HStackElement;
using T = Reactor.Core.TextElement;
```

Or, more practically, leverage C# 12's **`using` type aliases** and provide short names that
look identical to the current factory methods:

```csharp
// Reactor provides these as global usings or a well-known import
global using VStack = Reactor.Core.VStackElement;
global using HStack = Reactor.Core.HStackElement;
global using Text = Reactor.Core.TextElement;
global using Button = Reactor.Core.ButtonElement;
```

### Problem: Name Collisions

This immediately collides with `System.Text`, `System.Windows.Controls.Button`, etc. Global using
aliases with short names would be hostile to any codebase. This is a non-starter for types that
share names with the BCL or WinUI.

### Alternative: Implicit `new`

C# supports target-typed `new()` where the type can be inferred. If element records had
parameterless constructors and the context expected `Element`:

```csharp
Element[] children = [
    new Text { Content = "Hello", FontSize = 14 },
    new Button { Label = "Click", OnClick = handler },
];
```

But this only works when there's a target type. Inside `params Element?[]` it works:

```csharp
return new VStack {
    Spacing = 0,
    Children = [
        new() { Content = "Hello", FontSize = 14 },  // Ambiguous! What type?
    ]
};
```

Target-typed new fails because the compiler can't infer which `Element` subtype to create from
`new()`. The concrete type carries semantic meaning (Text vs. Button vs. Border).

**Verdict:** Alias approaches create more problems than they solve. `new` noise is inherent to
the initializer model and can't be cleanly abstracted away.

---

## Part 6: Option E — Collection Expression Builders (C# 12+)

### The Idea

C# 12 introduced **collection expressions** (`[a, b, c]`) with the `[CollectionBuilder]` attribute.
This allows a type to control how `[item1, item2, ...]` syntax desugars. Combined with an
element type, children could use `[ ]` syntax:

```csharp
[CollectionBuilder(typeof(StackBuilder), "Create")]
public record StackElement : Element
{
    public Element[] Children { get; init; }
    public double Spacing { get; init; } = 8;
    // ...
}

public static class StackBuilder
{
    public static StackElement Create(ReadOnlySpan<Element> children) =>
        new() { Children = children.ToArray() };
}
```

### Usage

```csharp
StackElement items = [
    Text("Hello").Bold(),
    Text("World"),
    Button("Click", handler),
];
// items is a StackElement with Children = [Text, Text, Button]
```

### Problem: Properties

Collection expressions only handle the child list. You can't set `Spacing`, `Background`, etc.
via the collection expression. You'd need `with { }` on top:

```csharp
StackElement items = ([
    Text("Hello").Bold(),
    Text("World"),
]) with { Spacing = 0, Background = CardBackground };
```

This is awkward — the `[ ]` gives you children, then `with { }` gives you properties. Two
distinct blocks for one element. And the cast/type annotation is required because `[...]` alone
can't infer the target type in all contexts.

### Problem: Nesting

Nested containers are the real test:

```csharp
StackElement ui = [
    Text("Title"),
    (StackElement)[       // Must cast nested collections to the right type
        Text("Inner 1"),
        Text("Inner 2"),
    ] with { Spacing = 4 },
    Button("Go", handler),
];
```

The nesting is ugly. Every nested container needs an explicit cast to its element type because
`[...]` is polymorphic — the compiler needs to know whether `[a, b]` is a `StackElement`,
`List<Element>`, `Element[]`, etc.

**Verdict:** Collection expressions solve the child-list half of the problem cleanly but don't
help with properties. They create a split between child specification (`[ ]`) and configuration
(`with { }`). Worse than Option B for holistic element construction.

---

## Part 6B: Option F — Factory Initializers (prototype of csharplang #6602)

### Status

**Working compiler prototype** on `dotnet/roslyn` branch
[`features/reactor-extensions`](https://github.com/dotnet/roslyn/tree/features/reactor-extensions),
with an LDM-facing request at
[`docs/features/reactor-extensions/ldm-request.md`](https://github.com/dotnet/roslyn/blob/features/reactor-extensions/docs/features/reactor-extensions/ldm-request.md).
74 tests green across parsing / semantic / emit / IOperation / classification; a sample solution
covers seven scenarios including a TodoApp, an Outlook-style MessageRow, and the other shapes used
throughout this document. This is the C# language change that Spec 008 §5 and Part 9 ("Long Term")
below speculate about, now with a concrete design and a working compiler.

### The Idea

Let object-initializer syntax follow a method call, not just `new`:

```csharp
var v = Factory(args) { X = value, Y = value };
```

A library author opts a method into this by marking it with a new `factory` modifier; the trailing
`{ … }` behaves exactly like the initializer block on `new T(args) { … }` — `init` and `required`
members are assignable inside the block, and the returned instance is sealed afterwards. Parens
can be dropped when a zero-arg overload resolves:

```csharp
var v = Factory { X = value };   // binds to Factory() when an overload exists
```

The `factory` modifier is the gate: trailing initializers are legal only on calls that resolve to a
`factory` method. This preserves immutability by default — a library author has to opt a method in
before callers can use this shape.

### What v1 deliberately does NOT include

The prototype's v1 scope is the **receiver-shape change only**. One ergonomic follow-up that Reactor
would especially care about is **explicitly deferred**:

- **Bare children in `{ }`.** `VStack { child1, child2 }` (children listed directly inside the block
  without `Children =`) is rejected in v1. Callers must write `VStack { Children = [child1, child2] }`.
  The LDM request calls this "the single biggest further ergonomic win for the Reactor scenarios"
  and explicitly flags it as a follow-up feature that stacks cleanly on top of v1.

So for Reactor today, Option F delivers **Option A' minus the `new` keyword** — not the dream
`VStack { child1, child2 }` shape that earlier parts of this document speculated about. That shape
remains one language feature away.

### Required Library Changes

Two things on the library side:

1. **Add the `factory` modifier** to every factory method that should accept a trailing initializer.
2. **Keep `Children` as an `init` collection property** (same as Option A'). Factory Initializers
   v1 does not change how children are passed — `Children = [...]` is still the way.

```csharp
// Before (current Reactor):
public static StackElement VStack(double spacing = 8, params Element?[] children) =>
    new StackElement { Spacing = spacing, Children = children };

// After (Option F):
public static factory StackElement VStack(double spacing = 8) =>
    new StackElement { Spacing = spacing };
// Children are passed through the trailing { Children = [...] } initializer, not through params.

public static factory TextElement Text(string content) =>
    new TextElement { Content = content };

public static factory ButtonElement Button(string label, Action? onClick = null) =>
    new ButtonElement { Label = label, OnClick = onClick };
```

All element records need the same property-surface promotion Option A' requires (layout/styling
properties as `init` members on the record). Nothing else changes.

The `factory` modifier works on ordinary static methods, extension methods, instance methods, and
local functions — the prototype covers all of those.

### TodoApp — Option F

```csharp
return VStack {
    Spacing = 0,
    Background = SolidBackground,
    MaxWidth = 600,
    HAlign = HorizontalAlignment.Center,
    Children = [
        // Header
        Text("todos") {
            FontSize = 36,
            Weight = FontWeights.Light,
            Foreground = AccentText,
            HAlign = HorizontalAlignment.Center,
            Margin = Thick(0, 16, 0, 8),
        },

        // Input bar
        HStack {
            Spacing = 8,
            Padding = Thick(16, 8, 16, 8),
            Background = CardBackground,
            Children = [
                TextField(state.NewItemText, v => dispatch(new SetNewItemText(v))) {
                    Placeholder = "What needs to be done?",
                    HAlign = HorizontalAlignment.Stretch,
                },
                Button(addCmd),
            ],
        },

        // List — spread still works
        ScrollView {
            Flex = new(grow: 1, basis: 0),
            Child = VStack {
                Spacing = 0,
                Children = [..filtered.Select(item => TodoRow(item, dispatch))],
            },
        },

        // Footer
        HStack {
            Spacing = 8,
            Padding = Thick(12, 8, 12, 8),
            Border = new(DividerStroke),
            Children = [
                Text($"{remaining} items left") {
                    FontSize = 12, Foreground = SecondaryText,
                    VAlign = VerticalAlignment.Center,
                },
                Spacer(),
                FilterButton("All", "all", state.Filter, dispatch),
                FilterButton("Active", "active", state.Filter, dispatch),
                FilterButton("Completed", "completed", state.Filter, dispatch),
            ],
        },
    ],
};
```

### Outlook MessageRow — Option F

```csharp
var senderLine = FlexRow {
    ColumnGap = 8,
    Children = [
        Text(msg.SenderName) {
            FontSize = 14,
            Weight = bold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Flex = new(grow: 1),
        },
        Text(dateStr) { FontSize = 12, Foreground = TertiaryText },
    ],
};

return Button(Props.OnSelected) {
    Background = bg,
    BorderThickness = Thick(0, 0, 0, 1),
    BorderBrush = BorderBrush,
    Padding = Thick(0),
    HAlign = HorizontalAlignment.Stretch,
    HContentAlign = HorizontalAlignment.Stretch,
    CornerRadius = 0,
    Resources = r => r
        .Set("ButtonBackgroundPointerOver", HoverBrush)
        .Set("ButtonBackgroundPressed", SelectedBrush),
    Content = Grid(["*"], ["*"]) {
        Children = [
            content with { Padding = Thick(14, 10, 14, 10), Grid = new(0, 0) },
            unreadBar with { Grid = new(0, 0) },
        ],
    },
};
```

### Analysis — Option F

**Compared to Option A' (`new VStack { Children = [...] }`), what's different:**

| | Option A' (`new VStack { … }`) | Option F (`VStack { … }`) |
|---|---|---|
| Construction keyword | `new` on every container | None — factory call reads as the name |
| Library opt-in | None — any `new T` works today | `factory` modifier on every exposed factory |
| Positional + named mix | `new Text("hi") { FontSize = 14 }` works | `Text("hi") { FontSize = 14 }` works |
| Parens-optional | N/A (`new` always present) | `VStack { … }` when a zero-arg overload exists |
| Bare children `{ c1, c2 }` | No (requires `Children = [...]`) | **No in v1** — requires `Children = [...]` (follow-up feature) |
| Spread via `[..expr]` | Works | Works (identical — property side is unchanged) |
| C# version required | C# 12+ (collection expressions) | New C# version shipping this feature |
| Post-initializer chaining (`expr.M()` after `{ }`) | Works without parens — `new T { X = V }.M()` parses as `(new T { X = V }).M()` | Works without parens — `Factory() { X = V }.M()` parses as `(Factory() { X = V }).M()` (primary-expression precedence, deliberately unlike `with`) |

**What's better than Option A':**
- **No `new` keyword.** Every container call-site is 4 characters shorter and reads as the
  factory name rather than an allocation. Over a 100-line component that is real.
- **Factories keep their positional ergonomics.** `Text("hi")`, `Button(cmd)`, `Grid(rows, cols)`
  stay as they are — the positional parameters the library already exposes are preserved, and the
  trailing `{ … }` only covers the additional config. A' loses this unless you also add a
  constructor that mirrors every factory's signature.
- **Parens-optional `Name { … }`.** For the "configuration-only" case (`VStack { Children = [...] }`,
  `FlexRow { ColumnGap = 8, Children = [...] }`), call-sites drop one more level of noise. The
  prototype allows this only when an overload resolves with zero args, so it's opt-in on the
  library side via default-valued parameters.
- **Library surface stays factory-shaped.** No need to expose a public constructor taking every
  permutation of factory parameters — existing `VStack(spacing)` / `Text(content)` / `Button(label)`
  signatures keep working.

**What's worse than Option A':**
- **Requires an unshipped C# feature.** A' works on C# 12 today; F requires a C# language version
  that ships this. Until then it's a prototype-compiler-only shape.
- **Library opt-in per method.** Every factory needs `factory` added to its signature. For Reactor
  that's a one-time mechanical change to the element factories and extension methods, but it is
  churn — and any third-party factory a consumer writes must also be marked `factory` to be
  usable in the new shape.
- **Honor-system v1.** The prototype does not verify that a `factory` method actually returns a
  freshly-constructed instance; verification is planned as a follow-up. A library author could
  mark a method `factory` that returns a shared cached instance, and callers would then mutate
  that shared instance through the trailing initializer. Reactor would need to avoid this pattern
  (not hard — factories already allocate).

**What's the same as Option A':**
- **Property setting** (`{ FontSize = 14, Weight = bold }`) — identical.
- **Children mechanism** — still `Children = [...]` in v1. The deep nesting pressure from
  repeated `Children = [` is the same as A'.
- **No `.Set()` needed** — same as A'.
- **API surface reduction** — same as A'. The 1400-line `ElementExtensions.cs` collapses the same
  way, since properties move onto records in both options.
- **`with { }` still works for post-construction overrides** on existing instances (`content with
  { … }` in the MessageRow sample above).

**Compared to Option B** (factory + `with { }`):
- F replaces the `with { }` copy with an in-place initializer on the just-constructed instance.
  That's both clearer (one construction, one config block, zero copies) and cheaper (no record
  clone per property-setting round).
- F fully sidesteps B's `with { }` precedence gotcha. The trailing initializer is specified at
  **primary-expression precedence**, so `Factory(args) { X = V }.Method()` parses as
  `(Factory(args) { X = V }).Method()` — the fluent chain attaches to the whole initialized
  instance, no parentheses required. This is explicitly called out in the prototype's speclet as
  a deliberate divergence from `with`, and covered by the prototype's `Chained_*` regression
  tests. In practical terms: Reactor can freely mix initializer-then-fluent ordering
  (`Text("hi") { FontSize = 14 }.Flex(grow: 1)`) without the parenthesization tax that `with` demands.

**Compared to Current** (fluent):
- Same visual grouping win as A' — `{ }` groups everything belonging to one element, instead
  of splitting across fluent chain / `with { }` / `.Set()`.
- Unlike A', the factory-call shape is preserved, so `.Bold()`-style one-liners keep working
  either side of the initializer: `Text("hi").Bold() { FontSize = 14 }` puts the fluent chain
  before, and `Text("hi") { FontSize = 14 }.Flex(grow: 1)` puts it after. Because the trailing
  initializer binds at primary-expression precedence, **both orderings work without parentheses**
  — a strict improvement over Option B's `with { }`, which forces the fluent chain to go first
  (or requires wrapping the `with { }` expression in parens to chain after it).
- This matters directly for the attached-property question: third-party panels that contribute
  `.Flex(...)` / `.Grid(...)` / `.Dock(...)` extension methods compose naturally with Factory
  Initializers without the consumer having to pick a strict ordering. The preferred Reactor
  call-site becomes "element's own properties in the `{ }` block, attached-property extensions
  chained after" — which reads left-to-right with no parens.

### Open questions for Reactor specifically

1. **Is "Option A' minus `new`" enough of a win on its own to bet on an unshipped C# feature?**
   Concretely: if `Children = [...]` boilerplate stays, the savings are the `new` keyword plus
   parens-optional on zero-arg factories. That's a real per-call-site win but not a structural
   one. The structural win (bare children `VStack { c1, c2 }`) is deferred to a follow-up language
   feature that has no prototype yet.

2. **Appetite for a prototype migration.** Option A' was already flagged in Part 9 as worth a
   prototype branch migrating 2–3 production components. A parallel branch using the
   `features/reactor-extensions` compiler would answer the ergonomics question at the
   component level, and the diff between the A' branch and the F branch is exactly the ergonomic
   delta this option provides.

3. **Signal back to the LDM.** The Roslyn prototype's design decisions (especially the deferral of
   bare children, and the honor-system v1) have direct implications for Reactor. If Reactor is the
   motivating scenario, the LDM request is the place to push on whether bare-children should be
   v1 rather than a follow-up.

### Verdict

Option F is **strictly better than Option A' for v1 and strictly weaker than the "ideal" factory
initializer the earlier parts of this document hoped for**. It delivers the `new`-less factory
call with trailing initializer, it keeps positional parameters on factories, and it works in
combination with collection expressions for children — but the bare-children form (`VStack { c1,
c2 }`) that would make this feel like SwiftUI/JSX is explicitly deferred, and the whole thing
depends on an unshipped C# feature.

For a **Reactor v2 that plans to land 12–24 months out**, Option F is the right target: every
element record would already be A'-shaped (so the migration cost is paid once, not twice), and
flipping from `new VStack { … }` to `VStack { … }` once the language ships is a mechanical rename.
The practical short-term plan is therefore: **adopt Option A' now, ensure every Reactor factory is
trivially addable to Option F later** (single-responsibility allocation, no hidden state, no
shared-instance returns), and feed concrete ergonomic feedback into the
`features/reactor-extensions` LDM discussion.

For **Reactor v1 / the currently-shipping API**, Option F is premature: shipping against a
prototype compiler is not viable, and migrating the API surface to the `factory` modifier before
the language feature is approved risks reworking twice if design details change.

---

## Part 7: Comparative Summary

### TodoRow helper — all options side-by-side

**Current (fluent):**
```csharp
static Element TodoRow(TodoItem item, Action<TodoAction> dispatch) =>
    HStack(8,
        CheckBox(item.IsCompleted, _ => dispatch(new ToggleItem(item.Id))),
        Text(item.Text)
            .FontSize(14)
            .Opacity(item.IsCompleted ? 0.5 : 1)
            .Set(t => { if (item.IsCompleted) t.TextDecorations = TextDecorations.Strikethrough; })
            .VAlign(VerticalAlignment.Center),
        Empty().HAlign(HorizontalAlignment.Stretch),
        Button("✕", () => dispatch(new DeleteItem(item.Id)))
            .Set(b => { b.Padding = new Thickness(6, 2, 6, 2); b.MinWidth = 0; b.MinHeight = 0; })
    ).Padding(12, 6, 12, 6).WithKey(item.Id);
```

**Option A (`new` + initializers):**
```csharp
static Element TodoRow(TodoItem item, Action<TodoAction> dispatch) =>
    new HStack {
        Key = item.Id,
        Spacing = 8,
        Padding = Thick(12, 6, 12, 6),

        new CheckBox { IsChecked = item.IsCompleted, OnChanged = _ => dispatch(new ToggleItem(item.Id)) },
        new Text(item.Text) {
            FontSize = 14,
            Opacity = item.IsCompleted ? 0.5 : 1,
            TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : TextDecorations.None,
            VAlign = VerticalAlignment.Center,
        },
        new Spacer(),
        new Button("✕") {
            OnClick = () => dispatch(new DeleteItem(item.Id)),
            Padding = Thick(6, 2, 6, 2),
            MinWidth = 0,
            MinHeight = 0,
        },
    };
```

**Option B (factory + `with { }`):**
```csharp
static Element TodoRow(TodoItem item, Action<TodoAction> dispatch) =>
    HStack(8,
        CheckBox(item.IsCompleted, _ => dispatch(new ToggleItem(item.Id))),
        Text(item.Text) with {
            FontSize = 14,
            Opacity = item.IsCompleted ? 0.5 : 1,
            TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : TextDecorations.None,
            VAlign = VerticalAlignment.Center,
        },
        Spacer(),
        Button("✕", () => dispatch(new DeleteItem(item.Id))) with {
            Padding = Thick(6, 2, 6, 2),
            MinWidth = 0,
            MinHeight = 0,
        }
    ) with { Padding = Thick(12, 6, 12, 6), Key = item.Id };
```

**Option A' (`new` + collection expressions):**
```csharp
static Element TodoRow(TodoItem item, Action<TodoAction> dispatch) =>
    new HStack {
        Key = item.Id,
        Spacing = 8,
        Padding = Thick(12, 6, 12, 6),
        Children = [
            new CheckBox { IsChecked = item.IsCompleted, OnChanged = _ => dispatch(new ToggleItem(item.Id)) },
            new Text(item.Text) {
                FontSize = 14,
                Opacity = item.IsCompleted ? 0.5 : 1,
                TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : TextDecorations.None,
                VAlign = VerticalAlignment.Center,
            },
            new Spacer(),
            new Button("✕") {
                OnClick = () => dispatch(new DeleteItem(item.Id)),
                Padding = Thick(6, 2, 6, 2),
                MinWidth = 0,
                MinHeight = 0,
            },
        ],
    };
```

**Option C (hybrid):**
```csharp
static Element TodoRow(TodoItem item, Action<TodoAction> dispatch) =>
    HStack(8,
        CheckBox(item.IsCompleted, _ => dispatch(new ToggleItem(item.Id))),
        Text(item.Text)
            .FontSize(14)
            .Opacity(item.IsCompleted ? 0.5 : 1)
            .VAlign(VerticalAlignment.Center)
            with { TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : TextDecorations.None },
        Spacer(),
        Button("✕", () => dispatch(new DeleteItem(item.Id)))
            with { Padding = Thick(6, 2, 6, 2), MinWidth = 0, MinHeight = 0 }
    ).Padding(12, 6, 12, 6).WithKey(item.Id);
```

### Scoring Matrix

| Criteria | Current | A: `new`+init | A': `new`+`[..]` | B: factory+`with` | C: hybrid | E: coll expr |
|---|:---:|:---:|:---:|:---:|:---:|:---:|
| Simple element (1-2 props) | **A+** | B | B- | B+ | **A+** | B |
| Complex element (5+ props) | C | **A** | **A** | **A-** | B+ | C |
| Nested container trees | B+ | **A** | B+ | B | B+ | C- |
| Conditional modifiers | **A** | B- | B- | B | **A-** | B- |
| No `.Set()` escape hatch | F | **A+** | **A+** | **A** | B | B |
| No `new` keyword noise | **A+** | D | D | **A+** | **A+** | B |
| API surface size | D | **A+** | **A+** | B | C | B |
| Immutability correctness | **A+** | C | **A+** | **A+** | **A+** | **A** |
| Migration cost | **A+** | D | D | C+ | **A-** | C |
| IntelliSense / discoverability | **A** | B+ | B+ | B | **A-** | B- |
| Learning curve for new devs | B+ | B | B | B | B | C |
| Reconciler perf impact | **A** | C | B+ | B+ | **A** | B+ |
| `.Select().ToArray()` children | **A+** | C | **A+** | **A+** | **A+** | B |
| Switch/ternary as children | **A** | **A** | **A** | **A** | **A** | B- |
| LINQ + `.SelectMany()` + spread | **A** | **A** | **A+** | **A** | **A** | B |
| Imperative `List<>` building | B+ | B+ | B+ | B+ | B+ | C |
| Mixing static + dynamic children | **A+** | C+ | **A+** | **A+** | **A+** | C |

### Legend
- **A+/A/A-** — Excellent / strong
- **B+/B/B-** — Good / acceptable
- **C+/C/C-** — Mediocre / workable but painful
- **D** — Poor
- **F** — Fails / not possible

---

## Part 7B: Stress Tests — Complex Real-World Patterns

The TodoApp examples in Parts 2–6 are deliberately simple. Real Reactor components use LINQ
pipelines, switch expressions, nested conditionals, collection concatenation, and imperative
`List<Element>` building. These patterns expose the true strengths and weaknesses of each
option in ways that simple examples do not.

### 7B.1 LINQ + Conditional Rendering: FolderPane

The Outlook FolderPane partitions a folder list with `.Where()`, maps to elements with
`.Select()`, and renders conditional unread badges with ternaries inside a FlexRow.

**Current (fluent) — from `samples/apps/outlook/Components/Email/FolderPane.cs`:**
```csharp
public override Element Render()
{
    var favorites = Props.Folders.Where(f => f.IsFavorite).ToArray();
    var others = Props.Folders.Where(f => !f.IsFavorite).ToArray();

    return FlexColumn(
        NewMailButton(),

        Text("Favorites").SemiBold().FontSize(13).Foreground(SecondaryText)
            .Padding(18, 6, 18, 6),

        VStack(0, favorites.Select(FolderRow).ToArray()),

        Border(Empty()).Height(1).Background(DividerStroke).Margin(16, 10, 16, 10),

        Text("Folders").SemiBold().FontSize(13).Foreground(SecondaryText)
            .Padding(18, 4, 18, 6),

        ScrollViewer(
            VStack(0, others.Select(FolderRow).ToArray())
        ).Flex(grow: 1, basis: 0)
    );
}

Element FolderRow(MailFolder folder)
{
    var isSelected = folder.Id == Props.SelectedFolderId;
    var bg = isSelected ? SelectedBrush : TransparentBrush;

    return Button(
        (FlexRow(
            MdlIcon(folder.Icon, 16, SecondaryText),
            Text(folder.DisplayName).FontSize(14).Flex(grow: 1),
            folder.UnreadCount > 0
                ? Text(folder.UnreadCount.ToString())
                    .SemiBold().FontSize(13).Foreground(AccentText)
                : Empty()
        ) with { ColumnGap = 10 }).Padding(18, 7, 18, 7),
        () => Props.OnFolderSelected(folder.Id)
    ).Set(b =>
    {
        b.Background = bg;
        b.BorderThickness = new Thickness(0);
        b.Padding = new Thickness(0);
        b.CornerRadius = new CornerRadius(0);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        b.Resources["ButtonBackgroundPointerOver"] = isSelected ? SelectedBrush : HoverBrush;
        b.Resources["ButtonBackgroundPressed"] = SelectedBrush;
    });
}
```

**Option A (`new` + initializers):**
```csharp
public override Element Render()
{
    var favorites = Props.Folders.Where(f => f.IsFavorite).ToArray();
    var others = Props.Folders.Where(f => !f.IsFavorite).ToArray();

    return new FlexColumn {
        NewMailButton(),

        new Text("Favorites") {
            Weight = FontWeights.SemiBold, FontSize = 13,
            Foreground = SecondaryText, Padding = Thick(18, 6, 18, 6),
        },

        // PROBLEM: Can't splat an array into a collection initializer directly.
        // Must wrap in a container or call AddRange (non-standard).
        new VStack { Spacing = 0, Children = favorites.Select(FolderRow).ToArray() },

        new Border(new Empty()) { Height = 1, Background = DividerStroke, Margin = Thick(16, 10, 16, 10) },

        new Text("Folders") {
            Weight = FontWeights.SemiBold, FontSize = 13,
            Foreground = SecondaryText, Padding = Thick(18, 4, 18, 6),
        },

        new ScrollView {
            Flex = new(grow: 1, basis: 0),
            // Same problem: dynamically computed children can't splat inline
            new VStack { Spacing = 0, Children = others.Select(FolderRow).ToArray() },
        },
    };
}

Element FolderRow(MailFolder folder)
{
    var isSelected = folder.Id == Props.SelectedFolderId;
    var bg = isSelected ? SelectedBrush : TransparentBrush;

    return new Button(() => Props.OnFolderSelected(folder.Id)) {
        Background = bg,
        BorderThickness = Thick(0),
        Padding = Thick(0),
        CornerRadius = 0,
        HAlign = HorizontalAlignment.Stretch,
        HContentAlign = HorizontalAlignment.Stretch,
        Resources = r => r
            .Set("ButtonBackgroundPointerOver", isSelected ? SelectedBrush : HoverBrush)
            .Set("ButtonBackgroundPressed", SelectedBrush),

        Content = new FlexRow {
            ColumnGap = 10,
            Padding = Thick(18, 7, 18, 7),

            MdlIcon(folder.Icon, 16, SecondaryText),
            new Text(folder.DisplayName) { FontSize = 14, Flex = new(grow: 1) },
            // Conditional child is clean — null filtered by Add()
            folder.UnreadCount > 0
                ? new Text(folder.UnreadCount.ToString()) {
                    Weight = FontWeights.SemiBold, FontSize = 13, Foreground = AccentText,
                  }
                : null,
        },
    };
}
```

**Option A' (`new` + collection expressions):**
```csharp
public override Element Render()
{
    var favorites = Props.Folders.Where(f => f.IsFavorite).ToArray();
    var others = Props.Folders.Where(f => !f.IsFavorite).ToArray();

    return new FlexColumn {
        Children = [
            NewMailButton(),

            new Text("Favorites") {
                Weight = FontWeights.SemiBold, FontSize = 13,
                Foreground = SecondaryText, Padding = Thick(18, 6, 18, 6),
            },

            // Spread solves the LINQ problem — no Children = needed on inner VStack
            new VStack { Spacing = 0, Children = [..favorites.Select(FolderRow)] },

            new Border { Child = new Empty(), Height = 1, Background = DividerStroke, Margin = Thick(16, 10, 16, 10) },

            new Text("Folders") {
                Weight = FontWeights.SemiBold, FontSize = 13,
                Foreground = SecondaryText, Padding = Thick(18, 4, 18, 6),
            },

            new ScrollView {
                Flex = new(grow: 1, basis: 0),
                Child = new VStack { Spacing = 0, Children = [..others.Select(FolderRow)] },
            },
        ],
    };
}

Element FolderRow(MailFolder folder)
{
    var isSelected = folder.Id == Props.SelectedFolderId;
    var bg = isSelected ? SelectedBrush : TransparentBrush;

    return new Button(() => Props.OnFolderSelected(folder.Id)) {
        Background = bg,
        BorderThickness = Thick(0),
        Padding = Thick(0),
        CornerRadius = 0,
        HAlign = HorizontalAlignment.Stretch,
        HContentAlign = HorizontalAlignment.Stretch,
        Resources = r => r
            .Set("ButtonBackgroundPointerOver", isSelected ? SelectedBrush : HoverBrush)
            .Set("ButtonBackgroundPressed", SelectedBrush),
        Content = new FlexRow {
            ColumnGap = 10,
            Padding = Thick(18, 7, 18, 7),
            Children = [
                MdlIcon(folder.Icon, 16, SecondaryText),
                new Text(folder.DisplayName) { FontSize = 14, Flex = new(grow: 1) },
                folder.UnreadCount > 0
                    ? new Text(folder.UnreadCount.ToString()) {
                        Weight = FontWeights.SemiBold, FontSize = 13, Foreground = AccentText,
                      }
                    : null,
            ],
        },
    };
}
```

**Option B (factory + `with { }`):**
```csharp
public override Element Render()
{
    var favorites = Props.Folders.Where(f => f.IsFavorite).ToArray();
    var others = Props.Folders.Where(f => !f.IsFavorite).ToArray();

    return FlexColumn(
        NewMailButton(),

        Text("Favorites") with {
            Weight = FontWeights.SemiBold, FontSize = 13,
            Foreground = SecondaryText, Padding = Thick(18, 6, 18, 6),
        },

        // .Select().ToArray() in params — identical to current, no friction
        VStack(0, favorites.Select(FolderRow).ToArray()),

        (Border(Empty()) with { Height = 1 }).Background(DividerStroke).Margin(16, 10, 16, 10),

        Text("Folders") with {
            Weight = FontWeights.SemiBold, FontSize = 13,
            Foreground = SecondaryText, Padding = Thick(18, 4, 18, 6),
        },

        ScrollViewer(
            VStack(0, others.Select(FolderRow).ToArray())
        ) with { Flex = new(grow: 1, basis: 0) }
    );
}

Element FolderRow(MailFolder folder)
{
    var isSelected = folder.Id == Props.SelectedFolderId;
    var bg = isSelected ? SelectedBrush : TransparentBrush;

    return Button(
        FlexRow(
            MdlIcon(folder.Icon, 16, SecondaryText),
            Text(folder.DisplayName) with { FontSize = 14, Flex = new(grow: 1) },
            folder.UnreadCount > 0
                ? Text(folder.UnreadCount.ToString()) with {
                    Weight = FontWeights.SemiBold, FontSize = 13, Foreground = AccentText,
                  }
                : Empty()
        ) with { ColumnGap = 10, Padding = Thick(18, 7, 18, 7) },
        () => Props.OnFolderSelected(folder.Id)
    ) with {
        Background = bg,
        BorderThickness = Thick(0),
        Padding = Thick(0),
        CornerRadius = 0,
        HAlign = HorizontalAlignment.Stretch,
        HContentAlign = HorizontalAlignment.Stretch,
        Resources = r => r
            .Set("ButtonBackgroundPointerOver", isSelected ? SelectedBrush : HoverBrush)
            .Set("ButtonBackgroundPressed", SelectedBrush),
    };
}
```

**Analysis:** LINQ pipelines (`.Where().ToArray()` and `.Select().ToArray()`) work identically
in the current model and Option B because children are still passed as `params` arrays. Option A
has friction — you can't splat a `.Select().ToArray()` *inline* in a collection initializer `{ }`.
You must either use a `Children = ...` property assignment or add elements from a loop. This is a
significant ergonomic regression for data-driven UIs.

---

### 7B.2 Switch Expression Routing + LINQ Tabs

A tabbed app where a switch expression picks entirely different subtrees and LINQ builds the
tab buttons.

**Current (fluent) — from `samples/CommandingDemo/App.cs`:**
```csharp
return VStack(
    Text("Reactor Demo").FontSize(24).Bold().Margin(16, 16, 16, 8),
    HStack(8,
        tabs.Select((tab, i) =>
            Button(tab, () => setSelectedTab(i))
                .Background(i == selectedTab ? Accent : SubtleFill)
                .Margin(0, 0, 0, 8)
        ).ToArray()
    ).Margin(16, 0),
    selectedTab switch
    {
        0 => Component<StandardCommandsDemo>(),
        1 => Component<AsyncCommandDemo>(),
        2 => Component<ParameterizedCommandDemo>(),
        3 => Component<CommandHostDemo>(),
        _ => Empty(),
    }
);
```

**Option A (`new` + initializers):**
```csharp
return new VStack {
    new Text("Reactor Demo") { FontSize = 24, Bold = true, Margin = Thick(16, 16, 16, 8) },

    // PROBLEM: Can't put .Select().ToArray() inline as children.
    // Must assign to Children property, losing the inline feel.
    new HStack {
        Spacing = 8,
        Margin = Thick(16, 0),
        Children = tabs.Select((tab, i) =>
            new Button(tab) {
                OnClick = () => setSelectedTab(i),
                Background = i == selectedTab ? Accent : SubtleFill,
                Margin = Thick(0, 0, 0, 8),
            }
        ).ToArray(),
    },

    // Switch works fine — each arm returns a single element
    selectedTab switch
    {
        0 => Component<StandardCommandsDemo>(),
        1 => Component<AsyncCommandDemo>(),
        2 => Component<ParameterizedCommandDemo>(),
        3 => Component<CommandHostDemo>(),
        _ => new Empty(),
    },
};
```

**Option A' (`new` + collection expressions):**
```csharp
return new VStack {
    Children = [
        new Text("Reactor Demo") { FontSize = 24, Bold = true, Margin = Thick(16, 16, 16, 8) },

        // Spread handles LINQ inline — same as current ergonomics
        new HStack {
            Spacing = 8,
            Margin = Thick(16, 0),
            Children = [
                ..tabs.Select((tab, i) =>
                    new Button(tab) {
                        OnClick = () => setSelectedTab(i),
                        Background = i == selectedTab ? Accent : SubtleFill,
                        Margin = Thick(0, 0, 0, 8),
                    }),
            ],
        },

        // Switch works fine — each arm is an element in the collection expression
        selectedTab switch
        {
            0 => Component<StandardCommandsDemo>(),
            1 => Component<AsyncCommandDemo>(),
            2 => Component<ParameterizedCommandDemo>(),
            3 => Component<CommandHostDemo>(),
            _ => new Empty(),
        },
    ],
};
```

**Option B (factory + `with { }`):**
```csharp
return VStack(
    Text("Reactor Demo") with { FontSize = 24, Bold = true, Margin = Thick(16, 16, 16, 8) },

    // LINQ inline in params — works perfectly, same as current
    HStack(8,
        tabs.Select((tab, i) =>
            Button(tab, () => setSelectedTab(i))
                with { Background = i == selectedTab ? Accent : SubtleFill,
                       Margin = Thick(0, 0, 0, 8) }
        ).ToArray()
    ) with { Margin = Thick(16, 0) },

    // Switch identical to current
    selectedTab switch
    {
        0 => Component<StandardCommandsDemo>(),
        1 => Component<AsyncCommandDemo>(),
        2 => Component<ParameterizedCommandDemo>(),
        3 => Component<CommandHostDemo>(),
        _ => Empty(),
    }
);
```

**Analysis:** Switch expressions work equally well across all options — each arm returns
a single `Element`. The real differentiator is the LINQ `.Select().ToArray()` pipeline.
Option A' fixes Option A's splat problem — `[..tabs.Select(...)]` works natively.
Option B handles it identically to the current model. Option A (without collection
expressions) forces a `Children = expr` assignment that prevents mixing static and
dynamic children.

---

### 7B.3 Nested Conditionals + Switch: ConditionalDemo

Deep conditional nesting where checkbox state controls which subtrees exist, and a switch
expression picks between completely different view modes.

**Current (fluent) — from `samples/Reactor.TestApp/Demos/ConditionalDemo.cs`:**
```csharp
return ScrollViewer(VStack(16,
    Heading("Conditional UI"),

    CheckBox(showAdvanced, setShowAdvanced, label: "Show advanced options"),

    showAdvanced
        ? Border(
            VStack(8,
                Text("Advanced Settings").SemiBold(),
                CheckBox(enableFeatureA, setFeatureA, label: "Enable Feature A"),
                CheckBox(enableFeatureB, setFeatureB, label: "Enable Feature B"),

                enableFeatureA
                    ? Border(
                        VStack(4,
                            Text("Feature A Configuration").SemiBold(),
                            Slider(50, 0, 100).Width(200)
                        )
                      ).CornerRadius(4).Background(SubtleFill).Padding(12)
                    : null,

                enableFeatureB
                    ? Border(
                        VStack(4,
                            Text("Feature B Configuration").SemiBold(),
                            ToggleSwitch(false, null, onContent: "On", offContent: "Off")
                        )
                      ).CornerRadius(4).Background(SubtleFill).Padding(12)
                    : null
            )
          ).CornerRadius(8).Background(SubtleFill).Padding(16)
        : Text("Check the box above.").Foreground(TertiaryText),

    viewMode switch
    {
        ViewMode.Simple => VStack(4,
            Text("Simple view — just a summary."),
            Text($"{itemCount} items in the list.")
        ),
        ViewMode.Detailed => VStack(4,
            Text("Detailed view:").SemiBold(),
            ForEach(Enumerable.Range(1, itemCount),
                i => HStack(4, Text($"Item {i}").Width(80), Progress(i * 100.0 / itemCount).Width(150)))
        ),
        ViewMode.Custom => VStack(8,
            Text("Custom view:").SemiBold(),
            HStack(8,
                Text("Item count:"),
                Slider(itemCount, 1, 10, v => setItemCount((int)v)).Width(200),
                Text($"{itemCount}")
            ),
            ForEach(Enumerable.Range(1, itemCount),
                i => Border(Text($"Custom item {i}")).CornerRadius(4).Background(SubtleFill).Padding(8, 4))
        ),
        _ => Empty()
    },

    When(showAdvanced && enableFeatureA && enableFeatureB,
        () => Border(Text("Warning: conflicts possible."))
                .CornerRadius(4).Background(CautionBackground).Padding(12))
));
```

**Option A (`new` + initializers):**
```csharp
return new ScrollView {
    new VStack {
        Spacing = 16,

        new Heading("Conditional UI"),

        new CheckBox { IsChecked = showAdvanced, OnChanged = setShowAdvanced, Label = "Show advanced options" },

        // Nested ternaries work — each arm returns an element for Add()
        showAdvanced
            ? new Border {
                CornerRadius = 8, Background = SubtleFill, Padding = Thick(16),
                new VStack {
                    Spacing = 8,
                    new Text("Advanced Settings") { Weight = FontWeights.SemiBold },
                    new CheckBox { IsChecked = enableFeatureA, OnChanged = setFeatureA, Label = "Enable Feature A" },
                    new CheckBox { IsChecked = enableFeatureB, OnChanged = setFeatureB, Label = "Enable Feature B" },

                    enableFeatureA
                        ? new Border {
                            CornerRadius = 4, Background = SubtleFill, Padding = Thick(12),
                            new VStack {
                                Spacing = 4,
                                new Text("Feature A Configuration") { Weight = FontWeights.SemiBold },
                                new Slider { Value = 50, Min = 0, Max = 100, Width = 200 },
                            },
                          }
                        : null,

                    enableFeatureB
                        ? new Border {
                            CornerRadius = 4, Background = SubtleFill, Padding = Thick(12),
                            new VStack {
                                Spacing = 4,
                                new Text("Feature B Configuration") { Weight = FontWeights.SemiBold },
                                new ToggleSwitch { OnContent = "On", OffContent = "Off" },
                            },
                          }
                        : null,
                },
              }
            : (Element)new Text("Check the box above.") { Foreground = TertiaryText },

        // Switch inside collection initializer — works, each arm is one Add() call
        viewMode switch
        {
            ViewMode.Simple => new VStack {
                Spacing = 4,
                new Text("Simple view — just a summary."),
                new Text($"{itemCount} items in the list."),
            },
            ViewMode.Detailed => new VStack {
                Spacing = 4,
                new Text("Detailed view:") { Weight = FontWeights.SemiBold },
                // ForEach returns a GroupElement — Add() handles it
                ForEach(Enumerable.Range(1, itemCount),
                    i => new HStack {
                        Spacing = 4,
                        new Text($"Item {i}") { Width = 80 },
                        new Progress(i * 100.0 / itemCount) { Width = 150 },
                    }),
            },
            ViewMode.Custom => new VStack {
                Spacing = 8,
                new Text("Custom view:") { Weight = FontWeights.SemiBold },
                new HStack {
                    Spacing = 8,
                    new Text("Item count:"),
                    new Slider { Value = itemCount, Min = 1, Max = 10, OnChanged = v => setItemCount((int)v), Width = 200 },
                    new Text($"{itemCount}"),
                },
                ForEach(Enumerable.Range(1, itemCount),
                    i => new Border {
                        CornerRadius = 4, Background = SubtleFill, Padding = Thick(8, 4),
                        new Text($"Custom item {i}"),
                    }),
            },
            _ => new Empty(),
        },

        // When() still works — returns Element
        When(showAdvanced && enableFeatureA && enableFeatureB,
            () => new Border {
                CornerRadius = 4, Background = CautionBackground, Padding = Thick(12),
                new Text("Warning: conflicts possible."),
            }),
    },
};
```

**Option A' (`new` + collection expressions):**
```csharp
return new ScrollView { Child = new VStack { Spacing = 16, Children = [
    new Heading("Conditional UI"),

    new CheckBox { IsChecked = showAdvanced, OnChanged = setShowAdvanced, Label = "Show advanced options" },

    // Nested ternaries: each arm is an element in the [...]
    showAdvanced
        ? new Border {
            CornerRadius = 8, Background = SubtleFill, Padding = Thick(16),
            Child = new VStack { Spacing = 8, Children = [
                new Text("Advanced Settings") { Weight = FontWeights.SemiBold },
                new CheckBox { IsChecked = enableFeatureA, OnChanged = setFeatureA, Label = "Enable Feature A" },
                new CheckBox { IsChecked = enableFeatureB, OnChanged = setFeatureB, Label = "Enable Feature B" },

                // Conditional children via spread: empty array when false
                ..(enableFeatureA ? [
                    new Border {
                        CornerRadius = 4, Background = SubtleFill, Padding = Thick(12),
                        Child = new VStack { Spacing = 4, Children = [
                            new Text("Feature A Configuration") { Weight = FontWeights.SemiBold },
                            new Slider { Value = 50, Min = 0, Max = 100, Width = 200 },
                        ]},
                    }
                ] : Array.Empty<Element>()),

                ..(enableFeatureB ? [
                    new Border {
                        CornerRadius = 4, Background = SubtleFill, Padding = Thick(12),
                        Child = new VStack { Spacing = 4, Children = [
                            new Text("Feature B Configuration") { Weight = FontWeights.SemiBold },
                            new ToggleSwitch { OnContent = "On", OffContent = "Off" },
                        ]},
                    }
                ] : Array.Empty<Element>()),
            ]},
          }
        : (Element)new Text("Check the box above.") { Foreground = TertiaryText },

    // Switch works identically
    viewMode switch
    {
        ViewMode.Simple => new VStack { Spacing = 4, Children = [
            new Text("Simple view — just a summary."),
            new Text($"{itemCount} items in the list."),
        ]},
        ViewMode.Detailed => new VStack { Spacing = 4, Children = [
            new Text("Detailed view:") { Weight = FontWeights.SemiBold },
            ..Enumerable.Range(1, itemCount).Select(i =>
                new HStack { Spacing = 4, Children = [
                    new Text($"Item {i}") { Width = 80 },
                    new Progress(i * 100.0 / itemCount) { Width = 150 },
                ]}),
        ]},
        ViewMode.Custom => new VStack { Spacing = 8, Children = [
            new Text("Custom view:") { Weight = FontWeights.SemiBold },
            new HStack { Spacing = 8, Children = [
                new Text("Item count:"),
                new Slider { Value = itemCount, Min = 1, Max = 10, OnChanged = v => setItemCount((int)v), Width = 200 },
                new Text($"{itemCount}"),
            ]},
            ..Enumerable.Range(1, itemCount).Select(i =>
                new Border {
                    CornerRadius = 4, Background = SubtleFill, Padding = Thick(8, 4),
                    Child = new Text($"Custom item {i}"),
                }),
        ]},
        _ => new Empty(),
    },

    When(showAdvanced && enableFeatureA && enableFeatureB,
        () => new Border {
            CornerRadius = 4, Background = CautionBackground, Padding = Thick(12),
            Child = new Text("Warning: conflicts possible."),
        }),
]}};
```

**Option B (factory + `with { }`):**
```csharp
return ScrollViewer(VStack(16,
    Heading("Conditional UI"),

    CheckBox(showAdvanced, setShowAdvanced, label: "Show advanced options"),

    showAdvanced
        ? (Border(
            VStack(8,
                Text("Advanced Settings") with { Weight = FontWeights.SemiBold },
                CheckBox(enableFeatureA, setFeatureA, label: "Enable Feature A"),
                CheckBox(enableFeatureB, setFeatureB, label: "Enable Feature B"),

                enableFeatureA
                    ? Border(
                        VStack(4,
                            Text("Feature A Configuration") with { Weight = FontWeights.SemiBold },
                            Slider(50, 0, 100) with { Width = 200 }
                        )
                      ) with { CornerRadius = 4, Background = SubtleFill, Padding = Thick(12) }
                    : null,

                enableFeatureB
                    ? Border(
                        VStack(4,
                            Text("Feature B Configuration") with { Weight = FontWeights.SemiBold },
                            ToggleSwitch(false, null, onContent: "On", offContent: "Off")
                        )
                      ) with { CornerRadius = 4, Background = SubtleFill, Padding = Thick(12) }
                    : null
            )
          ) with { CornerRadius = 8, Background = SubtleFill, Padding = Thick(16) })
        : (Element)(Text("Check the box above.") with { Foreground = TertiaryText }),

    viewMode switch
    {
        ViewMode.Simple => VStack(4,
            Text("Simple view — just a summary."),
            Text($"{itemCount} items in the list.")
        ),
        ViewMode.Detailed => VStack(4,
            Text("Detailed view:") with { Weight = FontWeights.SemiBold },
            ForEach(Enumerable.Range(1, itemCount),
                i => HStack(4,
                    Text($"Item {i}") with { Width = 80 },
                    Progress(i * 100.0 / itemCount) with { Width = 150 }))
        ),
        ViewMode.Custom => VStack(8,
            Text("Custom view:") with { Weight = FontWeights.SemiBold },
            HStack(8,
                Text("Item count:"),
                Slider(itemCount, 1, 10, v => setItemCount((int)v)) with { Width = 200 },
                Text($"{itemCount}")
            ),
            ForEach(Enumerable.Range(1, itemCount),
                i => Border(Text($"Custom item {i}"))
                    with { CornerRadius = 4, Background = SubtleFill, Padding = Thick(8, 4) })
        ),
        _ => Empty()
    },

    When(showAdvanced && enableFeatureA && enableFeatureB,
        () => Border(Text("Warning: conflicts possible."))
                with { CornerRadius = 4, Background = CautionBackground, Padding = Thick(12) })
));
```

**Analysis:** Nested conditionals work in all options. The key observation:

- **Current**: fluent chains after ternary branches read well (`.CornerRadius(4).Background(...).Padding(12)`).
- **Option A**: `new` + initializer nesting is *deep* but has clear `{ }` scoping. The structure
  reads like a data literal. `new` noise accumulates in proportion to nesting depth.
- **Option A'**: Same structure as A but uses `Children = [...]` throughout. Conditional children
  can use **spread with ternary**: `..(condition ? [element] : [])`. This is more explicit than
  `condition ? element : null` but avoids the null-handling issue entirely. The nesting gets deep:
  `new VStack { Spacing = 4, Children = [...]}` at every level. The `ForEach` equivalent uses
  spread: `..Enumerable.Range(1, n).Select(...)` — arguably cleaner than wrapping in `ForEach()`.
- **Option B**: `with { }` after ternary branches requires parenthesizing the outer expression —
  `(Border(...)) with { ... }` — or using the rule "put `with` last." The ternary arms need
  explicit `(Element)` casts when the two arms have different concrete types.

Switch expressions work equally well across all options. `ForEach` (which returns a GroupElement)
works as a child in both `params` and collection initializer contexts. In A', the spread operator
replaces `ForEach` entirely — `..Enumerable.Range(1, n).Select(...)` is a direct LINQ spread.

---

### 7B.4 Imperative Collection Building + LINQ: AllDayRow

The calendar's AllDayRow uses a `for` loop with imperative `children.Add()`, nested `.Where()`,
`.Select().ToArray()`, `.Concat()`, and `.Set()` for border styling. This is the hardest pattern
to translate.

**Current (fluent) — from `samples/apps/outlook/Components/Calendar/AllDayRow.cs`:**
```csharp
public override Element Render()
{
    var columns = new[] { "60" }.Concat(Enumerable.Repeat("*", 7)).ToArray();

    var children = new List<Element>
    {
        Text("").FontSize(11).Foreground(TertiaryText)
            .Grid(row: 0, column: 0).Padding(4, 2, 4, 2)
    };

    for (int d = 0; d < 7; d++)
    {
        var day = Props.WeekStart.AddDays(d).Date;
        var dayEvents = Props.AllDayEvents
            .Where(e => e.Start.Date <= day && e.End.Date > day)
            .ToArray();

        if (dayEvents.Length > 0)
        {
            var stack = VStack(1,
                dayEvents.Select(e =>
                {
                    var color = Props.SourceColors.GetValueOrDefault(e.CalendarSourceId, "#0078D4");
                    return (Element)Border(
                        Text(e.Title).FontSize(10)
                            .Set(t => { t.TextTrimming = TextTrimming.CharacterEllipsis; t.MaxLines = 1; })
                    )
                    .Background(color + "30")
                    .WithBorder(color, 1)
                    .CornerRadius(2)
                    .Padding(4, 1, 4, 1)
                    .Set(b => b.BorderThickness = new Thickness(2, 0, 0, 0));
                }).ToArray()
            ).Padding(2);
            children.Add(stack.Grid(row: 0, column: d + 1));
        }
    }

    return Grid(columns, ["Auto"], children.ToArray())
        .Set(g =>
        {
            g.BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 224, 224, 224));
            g.BorderThickness = new Thickness(0, 0, 0, 1);
        });
}
```

**Option A (`new` + initializers):**
```csharp
public override Element Render()
{
    var columns = new[] { "60" }.Concat(Enumerable.Repeat("*", 7)).ToArray();

    // Imperative building is identical — Option A's collection initializer only helps
    // with static trees. For imperative loops, you fall back to List<Element>.Add().
    var children = new List<Element>
    {
        new Text("") {
            FontSize = 11, Foreground = TertiaryText,
            Grid = new(0, 0), Padding = Thick(4, 2, 4, 2),
        }
    };

    for (int d = 0; d < 7; d++)
    {
        var day = Props.WeekStart.AddDays(d).Date;
        var dayEvents = Props.AllDayEvents
            .Where(e => e.Start.Date <= day && e.End.Date > day)
            .ToArray();

        if (dayEvents.Length > 0)
        {
            var stack = new VStack {
                Spacing = 1, Padding = Thick(2),
                // PROBLEM: Can't splat dayEvents.Select(...).ToArray() into { }.
                // Must use Children = ... property
                Children = dayEvents.Select(e =>
                {
                    var color = Props.SourceColors.GetValueOrDefault(e.CalendarSourceId, "#0078D4");
                    return (Element)new Border {
                        Background = BrushHelper.Parse(color + "30"),
                        BorderBrush = BrushHelper.Parse(color),
                        BorderThickness = Thick(2, 0, 0, 0),
                        CornerRadius = 2,
                        Padding = Thick(4, 1, 4, 1),

                        new Text(e.Title) {
                            FontSize = 10,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            MaxLines = 1,
                        },
                    };
                }).ToArray(),
            };
            children.Add(stack with { Grid = new(0, d + 1) });
        }
    }

    return new Grid(columns, ["Auto"]) {
        Children = children.ToArray(),
        BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 224, 224, 224)),
        BorderThickness = Thick(0, 0, 0, 1),
    };
}
```

**Option A' (`new` + collection expressions):**
```csharp
public override Element Render()
{
    var columns = new[] { "60" }.Concat(Enumerable.Repeat("*", 7)).ToArray();

    // Imperative building is identical to Option A — for loops can't become collection expressions.
    // But inner VStack children use spread instead of Children = .ToArray().
    var children = new List<Element>
    {
        new Text("") {
            FontSize = 11, Foreground = TertiaryText,
            Grid = new(0, 0), Padding = Thick(4, 2, 4, 2),
        }
    };

    for (int d = 0; d < 7; d++)
    {
        var day = Props.WeekStart.AddDays(d).Date;
        var dayEvents = Props.AllDayEvents
            .Where(e => e.Start.Date <= day && e.End.Date > day)
            .ToArray();

        if (dayEvents.Length > 0)
        {
            var stack = new VStack {
                Spacing = 1, Padding = Thick(2),
                // Spread works — no need for Children = .ToArray()
                Children = [..dayEvents.Select(e =>
                {
                    var color = Props.SourceColors.GetValueOrDefault(e.CalendarSourceId, "#0078D4");
                    return (Element)new Border {
                        Background = BrushHelper.Parse(color + "30"),
                        BorderBrush = BrushHelper.Parse(color),
                        BorderThickness = Thick(2, 0, 0, 0),
                        CornerRadius = 2,
                        Padding = Thick(4, 1, 4, 1),
                        Child = new Text(e.Title) {
                            FontSize = 10,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            MaxLines = 1,
                        },
                    };
                })],
            };
            children.Add(stack with { Grid = new(0, d + 1) });
        }
    }

    return new Grid(columns, ["Auto"]) {
        Children = [..children],
        BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 224, 224, 224)),
        BorderThickness = Thick(0, 0, 0, 1),
    };
}
```

**Option B (factory + `with { }`):**
```csharp
public override Element Render()
{
    var columns = new[] { "60" }.Concat(Enumerable.Repeat("*", 7)).ToArray();

    // Imperative building — identical pattern, with { } replaces .Set()
    var children = new List<Element>
    {
        Text("") with {
            FontSize = 11, Foreground = TertiaryText,
            Grid = new(0, 0), Padding = Thick(4, 2, 4, 2),
        }
    };

    for (int d = 0; d < 7; d++)
    {
        var day = Props.WeekStart.AddDays(d).Date;
        var dayEvents = Props.AllDayEvents
            .Where(e => e.Start.Date <= day && e.End.Date > day)
            .ToArray();

        if (dayEvents.Length > 0)
        {
            var stack = VStack(1,
                dayEvents.Select(e =>
                {
                    var color = Props.SourceColors.GetValueOrDefault(e.CalendarSourceId, "#0078D4");
                    return (Element)Border(
                        Text(e.Title) with { FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 }
                    ) with {
                        Background = BrushHelper.Parse(color + "30"),
                        BorderBrush = BrushHelper.Parse(color),
                        BorderThickness = Thick(2, 0, 0, 0),
                        CornerRadius = 2,
                        Padding = Thick(4, 1, 4, 1),
                    };
                }).ToArray()
            ) with { Padding = Thick(2) };
            children.Add(stack with { Grid = new(0, d + 1) });
        }
    }

    return Grid(columns, ["Auto"], children.ToArray()) with {
        BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 224, 224, 224)),
        BorderThickness = Thick(0, 0, 0, 1),
    };
}
```

**Analysis:** Imperative `List<Element>` building + `for` loop is identical across all
options — none of them can collection-initialize a dynamic loop. The interesting differences
are:

- **`.Set()` elimination** is the biggest win: compare `Border(...).Background(...).WithBorder(...).CornerRadius(...).Set(b => b.BorderThickness = ...)`
  (current, 5 chained calls + 1 Set) vs `Border(...) with { Background = ..., BorderBrush = ..., BorderThickness = ..., CornerRadius = ..., Padding = ... }`
  (Option B, 1 with block).
- **Option A's `Children = ...`** property assignment is forced here because the children come
  from `.Select().ToArray()`. This means you can't mix static children with the dynamic list
  in one `{ }` block.
- **`.Concat()` on the column array** is untouched by any option — it's pure data, not UI.

---

### 7B.5 Collection Spread + SelectMany: D3 Stacked Bar Chart

The D3 chart samples use C# 12 spread operators `[..]` and `.SelectMany()` to build flat
element arrays from nested data structures. This is the most aggressive LINQ pattern in
the codebase.

**Current (fluent) — from `samples/ReactorCharting.Gallery/Samples/StackedBarChart.cs`:**
```csharp
return D3Canvas(W, H,
    [.. D3Grid(ys, left, plotW),

     .. series.SelectMany((s, si) =>
     {
         var fill = Brush(Palette[si]);
         return months.Select((month, j) =>
         {
             var pt = s.Points[j];
             return D3Rect(left + band.Map(month), ys.Map(pt.Y1),
                 band.Bandwidth, ys.Map(pt.Y0) - ys.Map(pt.Y1))
                 with { Fill = fill };
         });
     }),

     .. months.Select((m, i) =>
         D3Text(left + band.Map(m) + band.Bandwidth / 2, H - marginBottom + 14,
             m, 10, Gray(80)) with { TextAnchor = "middle" }),

     .. series.Select((s, si) =>
         D3Rect(legendX, legendY + si * 20, 14, 14)
             with { Fill = Brush(Palette[si]) }),

     .. series.Select((s, si) =>
         D3Text(legendX + 20, legendY + si * 20 + 2, s.Name, 11, Gray(60)))
    ]);
```

**Option A (`new` + initializers):**
```csharp
// D3Canvas takes params Element[] — but these are spread collection expressions, not
// collection initializers. The two features are orthogonal.
// This pattern is UNCHANGED in Option A because D3Canvas is a factory method, and the
// children use [...] collection expression syntax, not { } initializer syntax.
// You'd only see a difference if D3Rect/D3Text used initializers for properties:

return D3Canvas(W, H,
    [.. D3Grid(ys, left, plotW),

     .. series.SelectMany((s, si) =>
     {
         var fill = Brush(Palette[si]);
         return months.Select((month, j) =>
         {
             var pt = s.Points[j];
             return new D3Rect(left + band.Map(month), ys.Map(pt.Y1),
                 band.Bandwidth, ys.Map(pt.Y0) - ys.Map(pt.Y1)) { Fill = fill };
         });
     }),

     // ... rest identical, with { } replaced by { } initializers on new ...
    ]);
```

**Option A' (`new` + collection expressions):**
```csharp
// Identical to Option A. D3Canvas takes params Element[] — children are passed via
// collection expression [..] spread, which is the same mechanism A' uses for Children.
// D3Rect uses { Fill = fill } object initializer — same syntax in A and A'.
// No difference between A and A' for this pattern.
```

**Option B (factory + `with { }`):**
```csharp
// Identical to current — with { } is already used for D3Rect properties.
// No change required. SelectMany, spread, LINQ all untouched.
return D3Canvas(W, H,
    [.. D3Grid(ys, left, plotW),

     .. series.SelectMany((s, si) =>
     {
         var fill = Brush(Palette[si]);
         return months.Select((month, j) =>
         {
             var pt = s.Points[j];
             return D3Rect(left + band.Map(month), ys.Map(pt.Y1),
                 band.Bandwidth, ys.Map(pt.Y0) - ys.Map(pt.Y1))
                 with { Fill = fill };
         });
     }),

     // ... rest identical
    ]);
```

**Analysis:** Collection expressions (`[..]`) and `.SelectMany()` are **orthogonal to the
initializer question**. They operate on `params` arrays or `IEnumerable<T>`, not on collection
initializer `{ }` blocks. All options handle them identically. The D3 chart pattern already
uses `with { Fill = fill }` on records — this is the current model's strongest validation of
Option B's direction.

---

### 7B.6 GroupBy + Nested LINQ: Gallery Landing Page

Grouping items by category, mapping each group to a section with a header, and wrapping
grouped items in a flex panel.

**Current (fluent) — from `samples/ReactorCharting.Gallery/Program.cs`:**
```csharp
var categories = SampleRegistry.All
    .GroupBy(s => s.Category)
    .OrderBy(g => CategoryOrder(g.Key));

var sections = categories.Select(group =>
    VStack(8,
        SubHeading(group.Key).Foreground(PrimaryText),
        new FlexElement(
            group.Select(sample =>
                Button(
                    VStack(6,
                        SampleIcon(sample, 36),
                        Text(sample.Title) with { FontSize = 12 }
                    ).MaxWidth(100).HAlign(HorizontalAlignment.Center),
                    () => navigate(sample)
                ).Width(130).Height(90)
            ).ToArray()
        )
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            ColumnGap = 8,
            RowGap = 8,
        }
    )
).ToArray();

return FlexColumn(
    HStack(12,
        Heading("Gallery").Foreground(PrimaryText).Flex(grow: 1),
        ThemeToggle(isDark, setIsDark)
    ).Padding(24, 24, 24, 0),
    Caption($"{SampleRegistry.All.Length} samples").Foreground(SecondaryText).Padding(24, 0),
    ScrollViewer(
        VStack(24, sections).Padding(24, 12, 24, 24)
    ).Flex(grow: 1, basis: 0)
);
```

Note: this code *already* uses `new FlexElement(...) { Direction = ..., Wrap = ... }` — mixing
`new` with initializer `{ }` alongside factory methods. The current API is already hybrid!

**Option A (`new` + initializers):**
```csharp
var sections = categories.Select(group =>
    (Element)new VStack {
        Spacing = 8,
        new SubHeading(group.Key) { Foreground = PrimaryText },
        new FlexPanel {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            ColumnGap = 8,
            RowGap = 8,
            Children = group.Select(sample =>
                new Button(() => navigate(sample)) {
                    Width = 130, Height = 90,
                    Content = new VStack {
                        Spacing = 6,
                        MaxWidth = 100,
                        HAlign = HorizontalAlignment.Center,
                        SampleIcon(sample, 36),
                        new Text(sample.Title) { FontSize = 12 },
                    },
                }
            ).ToArray(),
        },
    }
).ToArray();

return new FlexColumn {
    new HStack {
        Spacing = 12,
        Padding = Thick(24, 24, 24, 0),
        new Heading("Gallery") { Foreground = PrimaryText, Flex = new(grow: 1) },
        ThemeToggle(isDark, setIsDark),
    },
    new Caption($"{SampleRegistry.All.Length} samples") { Foreground = SecondaryText, Padding = Thick(24, 0) },
    new ScrollView {
        Flex = new(grow: 1, basis: 0),
        new VStack { Spacing = 24, Padding = Thick(24, 12, 24, 24), Children = sections },
    },
};
```

**Option A' (`new` + collection expressions):**
```csharp
var sections = categories.Select(group =>
    (Element)new VStack {
        Spacing = 8,
        Children = [
            new SubHeading(group.Key) { Foreground = PrimaryText },
            new FlexPanel {
                Direction = FlexDirection.Row,
                Wrap = FlexWrap.Wrap,
                ColumnGap = 8,
                RowGap = 8,
                // Spread works inside Children = [...] — this is the key A' improvement
                Children = [..group.Select(sample =>
                    new Button(() => navigate(sample)) {
                        Width = 130, Height = 90,
                        Content = new VStack {
                            Spacing = 6,
                            MaxWidth = 100,
                            HAlign = HorizontalAlignment.Center,
                            Children = [
                                SampleIcon(sample, 36),
                                new Text(sample.Title) { FontSize = 12 },
                            ],
                        },
                    })],
            },
        ],
    }
).ToArray();

return new FlexColumn {
    Children = [
        new HStack {
            Spacing = 12,
            Padding = Thick(24, 24, 24, 0),
            Children = [
                new Heading("Gallery") { Foreground = PrimaryText, Flex = new(grow: 1) },
                ThemeToggle(isDark, setIsDark),
            ],
        },
        new Caption($"{SampleRegistry.All.Length} samples") { Foreground = SecondaryText, Padding = Thick(24, 0) },
        new ScrollView {
            Flex = new(grow: 1, basis: 0),
            Child = new VStack { Spacing = 24, Padding = Thick(24, 12, 24, 24), Children = [..sections] },
        },
    ],
};
```

**Option B (factory + `with { }`):**
```csharp
var sections = categories.Select(group =>
    (Element)VStack(8,
        SubHeading(group.Key) with { Foreground = PrimaryText },
        FlexRow(
            group.Select(sample =>
                Button(
                    VStack(6,
                        SampleIcon(sample, 36),
                        Text(sample.Title) with { FontSize = 12 }
                    ) with { MaxWidth = 100, HAlign = HorizontalAlignment.Center },
                    () => navigate(sample)
                ) with { Width = 130, Height = 90 }
            ).ToArray()
        ) with { Wrap = FlexWrap.Wrap, ColumnGap = 8, RowGap = 8 }
    )
).ToArray();

return FlexColumn(
    HStack(12,
        Heading("Gallery") with { Foreground = PrimaryText, Flex = new(grow: 1) },
        ThemeToggle(isDark, setIsDark)
    ) with { Padding = Thick(24, 24, 24, 0) },
    Caption($"{SampleRegistry.All.Length} samples")
        with { Foreground = SecondaryText, Padding = Thick(24, 0) },
    ScrollViewer(
        VStack(24, sections) with { Padding = Thick(24, 12, 24, 24) }
    ) with { Flex = new(grow: 1, basis: 0) }
);
```

**Analysis:** This is the most interesting example because the current codebase *already* uses
`new FlexElement(...) { ... }` with a collection initializer for the flex panel. This proves
the pattern works today for `new`-constructed elements. The key findings:

- **`.GroupBy().OrderBy()` pipeline** is pure data transformation — identical across all options.
- **Nested `.Select()` inside `.Select()`** is the LINQ stress test. Option A forces `Children = group.Select(...).ToArray()` inside the `{ }` block, while Option B puts the array in `FlexRow(group.Select(...).ToArray())` as a positional param.
- **The existing codebase already mixes `new` initializers with factory methods** — this validates
  that a hybrid model is natural, not a theoretical construct.
- **Option B `with { }`** cascading: `FlexRow(...) with { Wrap = ..., ColumnGap = ... }` reads
  cleanly for container-level properties while children stay in the positional `params`.

---

### 7B.7 Updated Scoring (Including Complex Patterns)

| Criteria | Current | A: `new`+init | A': `new`+`[..]` | B: factory+`with` | C: hybrid |
|---|:---:|:---:|:---:|:---:|:---:|
| `.Select().ToArray()` in children | **A+** | C | **A+** | **A+** | **A+** |
| `.SelectMany()` + spread `[..]` | **A** | **A** | **A+** | **A** | **A** |
| Switch expressions as children | **A** | **A** | **A** | **A** | **A** |
| Nested ternary conditionals | **A-** | **A-** | B+ | B+ | **A-** |
| Imperative `List<>` + loop | B+ | B+ | B+ | B+ | B+ |
| `.GroupBy().Select()` pipelines | **A** | B | **A** | **A** | **A** |
| `.Concat()` / collection building | **A** | B | **A+** | **A** | **A** |
| `.Where()` filter → `.Select()` map | **A+** | B+ | **A+** | **A+** | **A+** |
| Deep nesting (4+ levels) | B | **A-** | B- | B | B |
| Mixing static + dynamic children | **A+** | C+ | **A** | **A+** | **A+** |

**Key takeaways:**

**Option A's** biggest weakness — `.Select().ToArray()` inline — is **fully solved by A'**.
The collection expression spread `[..items.Select(Render)]` works natively inside
`Children = [...]`, mixing freely with static elements. A' also eliminates the immutability
hack (no `Add()` mutation).

**A' trades one problem for another:** it fixes LINQ splatting but introduces `Children = [`
boilerplate at every container level. Deep nesting (4+ levels, as in ConditionalDemo) gets
visually heavy: `new VStack { Spacing = 8, Children = [` is a lot of ceremony per level
compared to `VStack(8,`. The conditional-children pattern also shifts from clean `null`
filtering to `...(condition ? [element] : [])` spread, which is more explicit but verbose.

**Options B and C** handle all LINQ patterns identically to the current model because children
remain in `params` arrays. The `with { }` block is purely additive — it replaces `.Set()` and
augments fluent chains, without touching the child-passing mechanism.

---

## Part 8: Deep Dive — Specific Problem Areas

### 8.1 The `.Set()` Problem

`.Set()` exists because the fluent API can't expose every WinUI property. It accounts for ~30%
of real-world component code (see MessageRow, TodoApp, DirectoryTree). Each option addresses it
differently:

| What `.Set()` is used for | Current | Option A / A' | Option B | Option C |
|---|---|---|---|---|
| FontWeight on TextBlock | `.Set(t => t.FontWeight = ...)` | `Weight = ...` in init | `with { Weight = ... }` | `with { Weight = ... }` |
| TextTrimming | `.Set(t => t.TextTrimming = ...)` | `TextTrimming = ...` | `with { TextTrimming = ... }` | `with { TextTrimming = ... }` |
| Button.Padding / MinWidth | `.Set(b => { b.Padding = ...; b.MinWidth = 0; })` | `Padding = ..., MinWidth = 0` | `with { Padding = ..., MinWidth = 0 }` | `.Padding(...) with { MinWidth = 0 }` |
| Resources dictionary | `.Set(b => { b.Resources["X"] = Y; })` | `Resources = r => r.Set(...)` | `with { Resources = r => r.Set(...) }` | Same as B |
| Arbitrary WinUI property | `.Set(c => c.SomeObscureProperty = ...)` | Need init property or fallback | Need init property or fallback | Still need `.Set()` fallback |

**Key insight:** Options A and B eliminate `.Set()` *for properties modeled on the element record*.
But any WinUI property NOT on the record still needs an escape hatch. The question is: how many
properties does the record model, and how do you handle the rest?

**Proposed solution for any option:** Keep `.Set()` as a last-resort escape hatch but make it
unnecessary for the top ~50 properties used in real code. The element records already model most
of these (FontSize, IsEnabled, Spacing, etc.) — the gap is primarily FontWeight, TextDecorations,
TextTrimming, Padding/MinWidth on Button, and resource dictionary access.

### 8.2 The Immutability Problem (Option A Only — Solved by A')

Collection initializers require `Add()`, which is a mutating operation. Reactor elements are immutable
records. These are fundamentally at odds.

**Approach 1: Internal mutable builder, freeze on read.** The record has a private `List<Element>`
that `Add()` appends to, and `Children` returns a frozen array. Risk: the record is mutable during
construction, which violates the assumption that records are immutable.

**Approach 2: `[CollectionBuilder]` attribute.** C# 12 allows `[CollectionBuilder(typeof(X), "Create")]`
to specify a factory that receives `ReadOnlySpan<T>`. But this only works with collection expressions
(`[a, b, c]`), not with `{ a, b, c }` collection initializers. Different feature, different syntax.

**Approach 3: Separate mutable builder type.** `new VStackBuilder { child1, child2 } .Build()`
returns an immutable `StackElement`. But this adds a `.Build()` call and a parallel type hierarchy.

**Approach 4: Accept the internal mutability.** The record is mutable during construction via `Add()`,
but C# guarantees sequential execution within an expression. No other code can observe the partially-
constructed state. This is the same pattern `List<T>` uses with collection initializers. It's safe
in practice but aesthetically impure.

**Approach 5 (Option A'): Use `Children = [...]` instead of `Add()`.** The `Children` property is
a pure `{ get; init; }` on the record, set via a C# 12 collection expression. No mutation at all —
the collection expression builds the array, then the init property receives it. This is the cleanest
solution and eliminates the entire problem.

**Recommendation:** If pursuing the `new` + initializer model, use Option A' (Approach 5). It
preserves full immutability with zero compromise. Option A's `Add()` hack (Approach 4) is only
needed if you insist on bare children in `{ }` without the `Children = [` prefix.

### 8.3 The Conditional Children Problem

Reactor's current `params Element?[]` + `FilterChildren()` elegantly handles conditional rendering:

```csharp
VStack(
    Text("Always visible"),
    condition ? Text("Sometimes visible") : null,
    showExtra ? ExtraSection() : null
)
```

Each option handles this differently:

**Option A:** `Add(null)` must be a no-op. The `Add()` method filters nulls. Works, but every
call site pays a null check.

**Option A':** Two approaches:
1. Type `Children` as `Element?[]` — nulls are allowed, reconciler filters them. Clean:
   `Children = [header, condition ? extra : null, footer]`.
2. Type `Children` as `Element[]` — must use spread: `Children = [header, ..(condition ? [extra] : []), footer]`.
   More explicit but verbose. For the `Element?[]` approach, the reconciler's `FilterChildren` logic
   handles null stripping identically to the current model.

**Option B:** No change — `params Element?[]` still handles this. The `with { }` block is only
for properties, not children.

**Option C:** Same as current.

**Option E:** Collection expressions don't support null elements by default. You'd need
`[a, b, ...(condition ? [c] : [])]` which is ugly.

### 8.4 The Conditional Modifier Problem

Today:
```csharp
Text("Hello")
    .FontSize(14)
    .Apply(isHighlighted, e => e.Background("yellow").FontWeight(FontWeights.Bold))
    .Margin(8)
```

With `with { }` (Options B/C):
```csharp
// Can't conditionally add properties to a with block
var el = Text("Hello") with { FontSize = 14, Margin = Thick(8) };
if (isHighlighted)
    el = el with { Background = BrushHelper.Parse("yellow"), Weight = FontWeights.Bold };
```

Or with ternary:
```csharp
Text("Hello") with {
    FontSize = 14,
    Margin = Thick(8),
    Background = isHighlighted ? BrushHelper.Parse("yellow") : null,
    Weight = isHighlighted ? FontWeights.Bold : null,
}
```

The ternary approach works but duplicates the condition. The multi-statement approach loses
the single-expression ergonomics.

**Possible extension: `.Apply()` helper**
```csharp
Text("Hello")
    with { FontSize = 14, Margin = Thick(8) }
    .Apply(isHighlighted, e => e with { Background = yellow, Weight = FontWeights.Bold })
```

This chains a `with { }` block followed by a fluent `.Apply()` — mixing styles, but it works.

### 8.5 The Performance Problem

Each fluent modifier call today does:
1. Create a new `ElementModifiers` record with one property set
2. Call `Merge()` to combine with existing modifiers (copies ~40 fields)
3. Return `el with { Modifiers = merged }` (copies all element fields + new Modifiers)

For a 5-modifier chain, that's 5 `ElementModifiers` allocations + 5 merge operations + 5 element
copies.

With `with { }` (Option B), a 5-property `with { }` block does:
1. One element copy with 5 fields overwritten

This is **significantly cheaper** — one allocation instead of five, no intermediate merge objects.
The reconciler's `ShallowEquals` also gets simpler: compare properties directly on the element
instead of drilling into a nested `Modifiers` record.

With `new` + initializer (Option A), the cost is:
1. One allocation of the element
2. N `Add()` calls for children (list append, amortized O(1) each)
3. Properties set directly on the init-properties

Also cheaper than the current model.

With `new` + collection expression (Option A'), the cost is:
1. One array allocation for `Children = [...]` (the collection expression builds a single array)
2. One allocation of the element with all properties set at init time
3. No intermediate copies, no merge operations

This is the **cheapest option** — one array + one record allocation, no intermediate objects.
The collection expression compiler can even stack-allocate the span for small child counts.

**Performance verdict:** Options A, A', and B are all strictly better than the current fluent
model for elements with multiple modifiers. A' is marginally best (no `Add()` overhead, no
`with { }` copy). The current model's per-modifier allocation + merge cost is its worst
performance characteristic.

---

## Part 9: Recommendations

### Short Term (Low Risk)

**Adopt Option C (Hybrid)** as an incremental improvement:

1. Promote the top ~20 `.Set()` targets to init properties on their element records:
   - `TextElement`: `TextDecorations`, `MaxLines`, `TextTrimming` (already has `TextWrapping`)
   - `ButtonElement`: `Padding` (as init property, not just via Modifiers)
   - `TextFieldElement`: `PlaceholderText` (already `Placeholder` in constructor)
   - Common for all: `MinWidth`, `MinHeight` (already on `ElementModifiers` but needed as
     escape-hatch-free properties)

2. Document the convention: "Use fluent methods for layout and styling that applies to any element.
   Use `with { }` for type-specific configuration."

3. Deprecate `.Set()` usage for properties that have init-property equivalents. Keep `.Set()` as
   a documented last-resort for truly obscure WinUI properties.

### Medium Term (Consider Carefully)

**Evaluate Option B** as the primary API style for Reactor v2:

1. Move all `ElementModifiers` properties to init properties on the `Element` base record.
2. Make `with { }` the standard way to configure elements beyond positional factory parameters.
3. Keep fluent extension methods as **convenience aliases** for the most common properties
   (`.Margin()`, `.Padding()`, `.Background()`, `.Width()`, `.Height()`), but they become thin
   wrappers over `with { }`.
4. This gives developers a choice: use fluent for quick one-liners, `with { }` for complex config.

### Long Term (Consider A' or Wait for C# Evolution)

**Option A' is viable today** and merits a prototype. Its strengths — full immutability, LINQ
spread, unified `{ }` syntax — address the current model's deepest structural problems. The
`Children = [...]` boilerplate and `new` noise are real costs, but they're consistent and
predictable. A prototype branch migrating 2–3 production components (Outlook MessageRow,
FolderPane, calendar AllDayRow) would reveal whether the boilerplate cost is acceptable in
practice or just tolerable in a spec.

**Watch for factory method initializers** ([csharplang #6602](https://github.com/dotnet/csharplang/discussions/6602)):
If C# ever allows `VStack(16) { child1, child2 }` syntax (collection initializer after factory
method calls), this unlocks the best possible Reactor syntax — no `new`, `{ }` for children,
properties inline. This is the "Option A without the `new`" future that Spec 008 §5 describes.
A' would transition cleanly to this future — replace `new VStack { Children = [...] }` with
`VStack() { children }` — since the property-setting side is identical.

**Update (2026-04):** A working compiler prototype of this feature now exists on `dotnet/roslyn`
branch `features/reactor-extensions`, with an LDM-facing request targeting the next C# release
cycle. See **Part 6B: Option F — Factory Initializers** above for the full analysis. Important
caveat: the prototype's v1 scope **does not** include the bare-children form
(`VStack { child1, child2 }`); callers must still write `VStack { Children = [...] }`. The
bare-children sugar is called out as a follow-up feature. So "Option F today" is best understood
as "Option A' minus the `new` keyword" — a real ergonomic win, but not the full SwiftUI/JSX
shape. The strategic implication for Reactor is unchanged from above: **adopt Option A' now**,
shape the element types so that adding `factory` modifiers later is mechanical, and feed Reactor's
preference for bare-children into the `features/reactor-extensions` LDM discussion while v1 scope
is still open.

### What NOT to Do

1. **Don't pursue Option A (mutable `Add()` initializers) today.** A' is strictly better — same
   visual style, no immutability hack, spread works. There is no reason to choose A over A'.

2. **Don't pursue Option E (collection expressions) as a primary model.** It solves half the problem
   (children) while creating a new split (children in `[ ]`, properties in `with { }`).

3. **Don't try to eliminate fluent methods entirely.** They're superior for simple, common cases.
   The goal is to reduce the cases where `.Set()` is the only option, not to replace a working
   pattern.

---

## Part 10: Migration Path for Option C (Hybrid)

### Phase 1: Add init properties (non-breaking)

```diff
 public record TextElement(string Content) : Element
 {
     public double? FontSize { get; init; }
     public FontWeight? Weight { get; init; }
+    public TextDecorations? TextDecorations { get; init; }
+    public int? MaxLines { get; init; }
+    public TextTrimming? TextTrimming { get; init; }
     // ...
 }

 public record ButtonElement(string Label, Action? OnClick = null) : Element
 {
     public bool IsEnabled { get; init; } = true;
+    public Thickness? Padding { get; init; }
+    public double? MinWidth { get; init; }
+    public double? MinHeight { get; init; }
     // ...
 }
```

### Phase 2: Update reconciler to read new properties

The reconciler already reads init properties from element records (FontSize, Weight, etc.).
New properties follow the same pattern: check if non-null, apply to WinUI control.

### Phase 3: Migrate samples from `.Set()` to `with { }`

```diff
 // Before
 Text(item.Text)
     .FontSize(14)
     .Opacity(item.IsCompleted ? 0.5 : 1)
-    .Set(t => { if (item.IsCompleted) t.TextDecorations = TextDecorations.Strikethrough; })
+    with { TextDecorations = item.IsCompleted ? TextDecorations.Strikethrough : TextDecorations.None }
     .VAlign(VerticalAlignment.Center),

 // Before
 Button("✕", () => dispatch(new DeleteItem(item.Id)))
-    .Set(b => { b.Padding = new Thickness(6, 2, 6, 2); b.MinWidth = 0; b.MinHeight = 0; })
+    with { Padding = Thick(6, 2, 6, 2), MinWidth = 0, MinHeight = 0 }
```

### Phase 4: Deprecate `.Set()` for migrated properties

Add `[Obsolete]` or analyzer warnings when `.Set()` is used for properties that have init-property
equivalents.

---

## Appendix A: What Other Frameworks Do

| Framework | Element construction | Properties | Children |
|---|---|---|---|
| **SwiftUI** | View structs | ViewModifier chain | `body: some View` / `@ViewBuilder` |
| **Jetpack Compose** | `@Composable` functions | `Modifier` chain + named params | Trailing lambda `{ }` |
| **Flutter** | `new Widget(...)` constructors | Named constructor params | `children: [...]` param |
| **React JSX** | `<Component />` | JSX attributes | JSX children |
| **Avalonia.Markup.Declarative** | Factory methods | Fluent `.Prop(value)` chain | `.Content(child)` / `.Items(...)` |
| **MAUI (C# Markup)** | `new Label()` | Object initializer `{ }` | `.Content()` / `.Children()` |
| **Fabulous (F#)** | View functions | Fluent modifiers | Computation expression `{ }` |
| **Reactor (current)** | Factory methods | Fluent + `.Set()` + `with { }` | `params Element?[]` |
| **Reactor (Option A')** | `new` constructors | Object initializer `{ }` | `Children = [...]` collection expr |
| **Reactor (Option B)** | Factory methods | `with { }` record copy | `params Element?[]` |
| **Reactor (Option F)** | `factory` methods + trailing `{ }` | Object initializer `{ }` | `Children = [...]` collection expr (bare-children deferred) |

**Key observation:** Every successful declarative UI framework lands on one of two models:
1. **Function + modifier chain** (SwiftUI, Compose, Reactor current) — construction is a function call,
   configuration is chained modifiers.
2. **Constructor + properties** (Flutter, MAUI, Reactor A') — construction is `new`, configuration is named
   params or object initializer.

No framework successfully mixes both as equals. Reactor's `with { }` usage on FlexElement is already
a sign of model #2 leaking into model #1. Option A' commits fully to model #2 while Option B
commits fully to model #1 (with `with { }` replacing fluent chains). The question is whether to
commit to one or deliberately operate in the hybrid space.

**Flutter parallel:** Option A' is structurally closest to Flutter's model — `new Widget(...)` with
named constructor params and `children: [...]`. The difference is that Flutter uses constructor
parameters while A' uses init properties, and Flutter doesn't have collection expression spread
(`..`). Reactor A' with C# 12 spread is arguably more powerful than Flutter's child-passing model.

## Appendix B: Detailed C# Syntax Limitations

### `with { }` After Method Calls

`with { }` works after any expression of a record type. This means:

```csharp
// All valid C# today:
var a = Text("hi") with { FontSize = 14 };
var b = VStack(child1, child2) with { Spacing = 0 };
var c = FlexRow(items) with { ColumnGap = 8, JustifyContent = FlexJustify.Center };
```

But `with { }` is a **copy operation** — it creates a new record with the specified properties
changed. This means the factory method runs first (allocating the record with children), then
`with { }` copies the entire record to change one property. For records with array fields
(Children), the copy is shallow — the array reference is copied, not the contents.

### Chaining After `with { }`

```csharp
// Valid C# — .Margin() is called on the result of `with { }`
var x = Text("hi") with { FontSize = 14 }.Margin(8);

// But this binds as: Text("hi") with { FontSize = (14).Margin(8) }
// which fails because int has no Margin method.
// Must parenthesize:
var x = (Text("hi") with { FontSize = 14 }).Margin(8);
```

This is a **real syntax gotcha**. The `with { }` expression has lower precedence than `.` member
access. So `expr with { P = V }.Method()` is parsed as `expr with { P = V.Method() }`, not
`(expr with { P = V }).Method()`.

**Impact:** In Option B and C, you CANNOT chain fluent methods after `with { }` without
parentheses. This makes hybrid usage ugly:

```csharp
// WRONG — parses as with { Placeholder = "...".HAlign(Stretch) }
TextField(text, setter) with { Placeholder = "..." }.HAlign(HorizontalAlignment.Stretch)

// RIGHT — but ugly
(TextField(text, setter) with { Placeholder = "..." }).HAlign(HorizontalAlignment.Stretch)

// BETTER — put fluent first, with last
TextField(text, setter).HAlign(HorizontalAlignment.Stretch) with { Placeholder = "..." }
```

**Practical rule for Options B/C:** Put `with { }` as the **last** thing on the expression. Fluent
methods go before, `with { }` goes after. This works because fluent methods return the same record
type, which is still valid as the left operand of `with { }`.

### Collection Initializer Constraints

Collection initializers require:
1. The type implements `IEnumerable` (or `IEnumerable<T>`)
2. The type has one or more `Add()` instance methods
3. The expression uses `new` (not a factory method — this is the key limitation)

`Add()` can have multiple overloads:
```csharp
void Add(Element child)        // Add one child
void Add(Element[] children)   // Add an array (spread)
void Add(string text)          // Implicit conversion: Add(new Text(text))
```

### `init` Properties on Records

All properties shown in this spec are `{ get; init; }`, meaning they can only be set:
1. In a constructor/factory method
2. In an object initializer (`new Foo { Prop = val }`)
3. In a `with` expression (`foo with { Prop = val }`)

They CANNOT be set via `foo.Prop = val` after construction. This preserves the immutability
guarantee that the reconciler depends on.
