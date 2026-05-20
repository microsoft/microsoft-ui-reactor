# Docking — API surface (Phase 1)

> Source authoring notes for the docking API reference. The rendered
> `docs/guide/docking.md` is generated; edit this file and the
> companion `overview.md`.

## Namespace

```csharp
using Microsoft.UI.Reactor.Docking;
```

Ship vehicle: `Microsoft.UI.Reactor.Docking.Xaml` NuGet package
(separate from the core `Microsoft.UI.Reactor` package so apps that
don't need docking aren't forced to take the vendored XAML
dependency).

## `DockManager` — the host element

```csharp
public sealed record DockManager : Element
{
    public DockNode? Layout { get; init; }
    public IReadOnlyList<DockableContent>? LeftSide { get; init; }
    public IReadOnlyList<DockableContent>? TopSide { get; init; }
    public IReadOnlyList<DockableContent>? RightSide { get; init; }
    public IReadOnlyList<DockableContent>? BottomSide { get; init; }
    public DockableContent? ActiveDocument { get; init; }
    public IDockAdapter? Adapter { get; init; }
    public IDockBehavior? Behavior { get; init; }
    public string? PersistenceId { get; init; }
    public int LayoutSchemaVersion { get; init; } = 1;
}
```

Inherits `Element`. Reconciles to a single vendored
`WinUI.Dock.DockManager` XAML control in Phase 1. Re-renders that
produce a new `DockManager` instance with a structurally different
`Layout` cause the wrapper to diff the previous tree against the new
one and apply minimum mutations to the underlying control.

## `DockNode` — the algebra

```csharp
public abstract record DockNode;

public sealed record DockSplit(
    Orientation Orientation,
    IReadOnlyList<DockNode> Children,
    double? Width = null, double? Height = null,
    double? MinWidth = null, double? MinHeight = null,
    double? MaxWidth = null, double? MaxHeight = null) : DockNode;

public sealed record DockTabGroup(
    IReadOnlyList<DockableContent> Documents,
    TabPosition TabPosition = TabPosition.Top,
    bool CompactTabs = false,
    bool ShowWhenEmpty = false,
    int SelectedIndex = -1,
    double? Width = null, double? Height = null) : DockNode;

public sealed record DockableContent(
    string Title,
    Element? Content = null,
    object? Key = null,
    bool CanClose = false,
    bool CanPin = false,
    double? Width = null, double? Height = null,
    string? PersistenceState = null) : DockNode;
```

`DockableContent` is the leaf of the tree — the actual pane. Its
`Content` is the Reactor element subtree that renders inside the
pane's body. The wrapper hosts that subtree in a `ContentControl`
inside the vendored `Document.Content` slot, so Reactor's normal
reconciler keeps reconciling the subtree as data changes.

The non-leaf nodes (`DockSplit`, `DockTabGroup`) carry layout
metadata but no Reactor content of their own.

### Keys

`DockableContent.Key` is **required** for panes whose content state
should survive reorderings, tear-outs, and tab moves. Per
[spec 042](reconciliation.md), keyed reconciliation matches panes by
`Key` and preserves the vendored `Document` instance (and the
Reactor element subtree inside it) across tree rebuilds. Without a
key, a pane is mounted fresh on every render — its content tree
loses any `useState` / `useEffect` state.

There is **no implicit Title-as-key fallback** (unlike upstream
WinUI.Dock, which uses `Title` with a `##` namespace hack). Always
supply an explicit `Key`:

```csharp
new DockableContent(
    Title: "Solution Explorer",
    Content: SolutionExplorer(),
    Key: "tool:solution-explorer")
```

Keys can be any equatable, hashable value: strings, GUIDs, enums,
domain identifiers (e.g., a tab's document ID).

## Enums

```csharp
public enum TabPosition { Top, Bottom }

public enum DockTarget
{
    Center,
    SplitLeft, SplitTop, SplitRight, SplitBottom,
    DockLeft, DockTop, DockRight, DockBottom,
}
```

- `Center` — add as a tab in the destination group.
- `Split*` — split the destination group's parent panel; new pane on
  the specified side.
- `Dock*` — dock at the manager's edge (not inside a sub-panel).

## `IDockAdapter`

```csharp
public interface IDockAdapter
{
    Element? OnContentCreated(DockableContent content);
    void     OnGroupCreated(DockTabGroupContext group,
                            DockableContent? draggedSource);
    Element? GetFloatingWindowTitleBar(DockableContent? draggedSource);
}
```

The wrapper calls into the adapter at three points the declarative
`Layout` can't express:

- **`OnContentCreated`** — fires when a pane is reconstituted from
  layout JSON (`LoadLayout`). The adapter looks up the pane's
  identity from `content.Key` and returns the Reactor subtree to
  render inside.
- **`OnGroupCreated`** — fires when a new tab group is created at the
  tail of a tear-out. Apps can react (e.g., emit telemetry, wire a
  group-level toolbar) but cannot block.
- **`GetFloatingWindowTitleBar`** — supplies the title-bar element
  for a freshly-spawned floating window. Returning `null` falls back
  to the vendored default.

## `IDockBehavior`

```csharp
public interface IDockBehavior
{
    void OnDocked(DockableContent src, DockTarget target);
    void OnFloating(DockableContent content);
}
```

Informational lifecycle callbacks. Phase 2 introduces a
cancellable-event surface on the renamed `DockHost` (each `*ing`
event carries `Cancel`); this Phase 1 interface remains as a one-
release `[Obsolete]` forwarder.

## End-to-end example

A four-pane IDE layout — solution explorer + center editors +
properties + bottom log:

```csharp
class IdeShell : Component<IdeShellProps>
{
    public override Element Render()
    {
        var (docs, setDocs) = UseState(ImmutableList.Create(
            new DocVm(Id: "main.cs", Title: "main.cs"),
            new DocVm(Id: "App.razor", Title: "App.razor")));

        return new DockManager
        {
            PersistenceId = "main-shell",
            Adapter = new MyAdapter(setDocs),
            Layout = new DockSplit(
                Orientation.Horizontal,
                new DockNode[]
                {
                    // Left tool — solution explorer
                    new DockableContent(
                        Title: "Solution Explorer",
                        Key: "tool:solution",
                        Content: SolutionExplorer(),
                        Width: 240),

                    // Center: vertical split of editor tabs + bottom log
                    new DockSplit(
                        Orientation.Vertical,
                        new DockNode[]
                        {
                            new DockTabGroup(
                                Documents: docs
                                    .Select(d => new DockableContent(
                                        Title: d.Title,
                                        Key: d.Id,
                                        Content: Editor(d.Id),
                                        CanClose: true))
                                    .ToImmutableList()),

                            new DockableContent(
                                Title: "Output",
                                Key: "tool:output",
                                Content: OutputPane(),
                                Height: 180),
                        }),

                    // Right tool — properties
                    new DockableContent(
                        Title: "Properties",
                        Key: "tool:properties",
                        Content: Properties(),
                        Width: 280),
                }),
        };
    }
}
```

The collection-to-pane mapping is just `.Select` — no
`DocumentsSource` binding API needed (spec 045 §3.2 lesson #3).

## See also

- [Overview](overview.md) — what docking is, four-phase plan, Phase 1
  capabilities + limitations.
- [Spec 045](../../../specs/045-docking-windows-design.md) — the full
  design surface, including Phase 2's added types.
- [Spec 042 — keyed reconciliation](reconciliation.md) — how
  `DockableContent.Key` interacts with the reconciler.
