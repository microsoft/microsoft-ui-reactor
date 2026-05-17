---
name: animated-list
description: >
  Canonical recipe for animated list edits — Animations.Animate(Spring, …)
  wrapping a state setter, with IReactorKeyed identity-on-data and the
  reduced-motion bypass. Add / remove / shuffle pick up the ambient
  Spring without a per-element transition modifier. Use when a list
  should animate insert/move/remove without manual storyboards.
---

# Animated list (spec 042)

Wrap a state mutation in `Animations.Animate(kind, action)` and every
**structural** change to a keyed list (insert, move, remove) that comes
out of that mutation picks up the kind — without a per-element modifier
in sight. This is the SwiftUI `withAnimation { … }` analog.

```csharp
// #:package Microsoft.UI.Reactor@0.0.0-local
// #:package Microsoft.WindowsAppSDK@2.0.1
// #:property OutputType=WinExe
// #:property TargetFramework=net10.0-windows10.0.22621.0
// #:property UseWinUI=true
// #:property WindowsPackageType=None

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Animated list", width: 520, height: 600);

// Identity-on-data — drops `keySelector:` from every call site that
// accepts `T : IReactorKeyed`. (Spec 042 §5.)
record Todo(string Id, string Text) : IReactorKeyed
{
    string IReactorKeyed.Key => Id;
}

class App : Component
{
    public override Element Render()
    {
        var (items, setItems) = UseState<IReadOnlyList<Todo>>([
            new(Guid.NewGuid().ToString(), "Buy milk"),
            new(Guid.NewGuid().ToString(), "Walk the dog"),
        ]);
        var reduceMotion = UseReducedMotion();

        // Chokepoint — either commit directly or wrap in Animate. WCAG
        // 2.3.3: bypass entirely when the OS opts the user out.
        void Mutate(Func<IReadOnlyList<Todo>, IReadOnlyList<Todo>> change)
        {
            void commit() => setItems(change(items));
            if (reduceMotion) { commit(); return; }
            Animations.Animate(AnimationKind.Spring, commit);
        }

        return VStack(12,
            HStack(8,
                Button("+ Top",    () => Mutate(list => [new Todo(Guid.NewGuid().ToString(), "New (top)"), .. list])),
                Button("+ Bottom", () => Mutate(list => [.. list, new Todo(Guid.NewGuid().ToString(), "New (bottom)")])),
                Button("Shuffle",  () => { var r = new Random(); Mutate(list => list.OrderBy(_ => r.Next()).ToList()); }),
                Button("Reverse",  () => Mutate(list => list.Reverse().ToList()))),

            // 2-arg overload — keySelector defaults to t => t.Key because
            // Todo : IReactorKeyed. The diff produces incremental Add /
            // Move / Remove ops on ItemsSource; the Animate ambient
            // tags each op with Spring.
            ListView<Todo>(items, (t, _) => HStack(8,
                    TextBlock(t.Text).Flex(grow: 1),
                    Button("✕", () => Mutate(list => list.Where(x => x.Id != t.Id).ToList())))
                .Padding(horizontal: 8, vertical: 4))
                .Flex(grow: 1)
        ).Padding(16);
    }
}
```

`AnimationKind`: `Spring` (default for transactional UI), `EaseIn`,
`EaseOut`, `EaseInOut`, `Default`, `None`. Passing `None` is meaningful —
it explicitly suppresses any *outer* `Animate` for the duration of the
inner block (nested calls stack like `using`).

## Common mistakes

### 1. Mutating an `ObservableCollection<T>` from `UseState`

```csharp
// ❌ Doesn't re-render. Same reference → state comparer sees no change.
var (items, _) = UseState(new ObservableCollection<Todo>());
items.Add(new Todo(...));
```

Reactor compares state by reference. Mutating an OC in-place leaves the
reference unchanged and no re-render fires. Use an immutable shape:

```csharp
// ✓ New reference each commit, diff produces incremental WinUI ops.
var (items, setItems) = UseState<IReadOnlyList<Todo>>([]);
setItems([.. items, new Todo(...)]);
```

Reactor's reconciler computes the OC delta *internally* against the
prior render — your state stays immutable, WinUI still gets incremental
`Add` / `Move` / `Remove` notifications.

### 2. Forgetting `keySelector` on a non-`IReactorKeyed` type

```csharp
// ⚠️ Compiles, but every insert at top re-renders every visible row.
ListView<Repo>(repos, (r, _) => RepoRow(r))   // Repo doesn't implement IReactorKeyed
```

The 2-arg overload only exists on `T : IReactorKeyed`. If your type
doesn't implement the interface, you'll resolve to the unkeyed
positional overload (or get a compile error, depending on which factory
you're calling). Add the explicit selector:

```csharp
// ✓ Explicit selector — works for types you don't own.
ListView<Repo>(repos, keySelector: r => r.Id.ToString(), (r, _) => RepoRow(r))
```

### 3. Wrapping a non-structural change in `Animate`

```csharp
// ⚠️ Does nothing useful — Animate is scoped to structural list ops.
Animations.Animate(AnimationKind.Spring, () => setColor(Red));
```

`Animate` only tags `Add` / `Move` / `Remove` ops on keyed lists and
mount / move / unmount on `ChildReconciler`. A property change (color,
size, text) on a *surviving* leaf does **not** animate — that's the job
of per-element modifiers like `.WithImplicitTransition(...)` or
`AnimationScope.WithAnimation(...)`. The two channels are deliberately
independent. (Spec 042 §6 — scope discipline.)

### 4. Forgetting reduced-motion

```csharp
// ⚠️ Ignores the OS accessibility setting (WCAG 2.3.3).
Button("Add", () => Animations.Animate(AnimationKind.Spring, () => setItems(...)));
```

Read `UseReducedMotion()` at the call site and drop the wrapper when the
user has opted out. The pattern shown in the recipe above (single
chokepoint, `if (reduceMotion) commit()`) is the canonical shape.

### 5. Capturing `items` in a stale closure

```csharp
// ❌ items captured at render time; if multiple clicks fire fast,
// each one reads the stale list and overwrites the previous insert.
Button("+", () => Mutate(_ => [.. items, new Todo(...)]));
```

Use the change function's incoming `list` parameter, **not** the
captured `items`:

```csharp
// ✓ Always reads the latest committed list.
Button("+", () => Mutate(list => [.. list, new Todo(...)]));
```

This isn't specific to animated lists — it's the standard reducer
discipline for any list state — but it bites harder under `Animate`
because the visible animation makes the "lost insert" symptom obvious.

## See also

- Design: [spec 042 — Keyed-list reconciliation & ListView animation](../../../../../docs/specs/042-keyed-list-reconciliation-design.md)
- Reference: [`reactor-dsl/references/keyed-lists.md`](../../reactor-dsl/references/keyed-lists.md)
- Guide: [`animation.md` — Transactional animation](../../../../../docs/guide/animation.md#transactional-animation--animationsanimate)
- Recipe: [`list-add-delete.cs`](list-add-delete.cs) — non-animated baseline
- Sample: `samples/apps/animated-list-demo/` — runnable demo
