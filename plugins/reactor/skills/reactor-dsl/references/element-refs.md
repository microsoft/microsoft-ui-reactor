---
description: "Reactor ElementRef and reference-property quick reference — UseElementRef, .Ref, .Target, accessibility relationships, XYFocus, and custom descriptor Reference entries."
---

# Element refs and reference props

Use `UseElementRef<T>()` for relationships between realized WinUI
elements. Attach the cell to the source element with `.Ref(ref)`, then
pass the same cell to a reference prop. The edge is reactive: it resolves
after late mount, clears on source unmount, and re-resolves when either
side is recreated.

```csharp
var target = UseElementRef<FrameworkElement>();

return HStack(
    Button("Show tip", () => setOpen(true)).Ref(target),
    TeachingTip("Hint", target: target) with { IsOpen = open });
```

Common reference props:

- `TeachingTip(..., target: ref)` or `TeachingTip(...).Target(ref)`
- `.LabeledBy(ref)`
- `.DescribedBy(refs...)`
- `.FlowsTo(refs...)` / `.FlowsFrom(refs...)`
- `.XYFocusUp(ref)` / `.XYFocusDown(ref)` / `.XYFocusLeft(ref)` / `.XYFocusRight(ref)`

Custom control authors should declare reference properties in the
descriptor:

```csharp
descriptor.Reference<FrameworkElement>(
    get: e => e.Target,
    set: (control, target) => control.Target = target);

descriptor.ReferenceList<FrameworkElement>(
    get: e => e.Related,
    apply: (control, targets) =>
    {
        control.Related.Clear();
        foreach (var target in targets)
            control.Related.Add(target);
    });
```

Hand-coded handlers use `binding.Reference` / `binding.ReferenceList`.
Do not read `ref.Current` in a mount/update handler to assign a
relationship property; that is a non-reactive snapshot and is flagged by
`REACTOR_REF_001`.
