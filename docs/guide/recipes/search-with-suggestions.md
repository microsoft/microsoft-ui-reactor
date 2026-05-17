
# Recipe: Search with suggestions

The pattern is two slots: an input that owns the query string, and a
dropdown that shows zero or more suggestions filtered against a
catalog. `UseMemo` keys the filter on the query, so a re-render
that didn't touch the query is free — important for the keyboard-up-
arrow-down-arrow loop where the suggestion index changes but the
query doesn't.

## Primitives

| Concern | API |
|---|---|
| Query state | `UseState<string>` |
| Filter caching | `UseMemo<T[]>` keyed on the query |
| Dropdown render | `Border` wrapping `VStack` |
| Production wrapper | [`AutoSuggestBox`](../forms.md) |
| Async source | [`async-resources`](../async-resources.md) |

### Catalog

```csharp
class SearchBox : Component
{
    private static readonly string[] Catalog = new[] {
        "Account settings", "Accessibility", "Animation",
        "Buttons", "Backdrop", "Charts",
        "Components", "Commanding", "Context",
        "Effects", "Forms", "Hooks",
        "Localization", "Navigation", "Persistence",
        "Styling", "Testing", "Theming tokens",
    };
```

A static array stands in for the data source. A real app reaches for
[`async-resources`](../async-resources.md) once the catalog is on
the network.

### Filter

```csharp
// UseMemo on the dependency array means the filter runs only when
// query changes — typing fast doesn't refilter mid-keystroke, and
// a re-render that didn't touch the query is free.
var (query, setQuery) = UseState("");
var suggestions = UseMemo(
    () => string.IsNullOrWhiteSpace(query)
        ? new string[0]
        : Catalog.Where(c => c.Contains(query,
            System.StringComparison.OrdinalIgnoreCase))
            .Take(5).ToArray(),
    query);
```

`UseMemo` caches the filtered array between renders that don't change
`query`. The empty-query branch returns an empty array so the
dropdown closes — collapsing the suggestion surface when the user
clears the input is essential.

### Render

```csharp
return VStack(8,
    TextField(query, setQuery, placeholder: "Search topics…").Width(300),
    suggestions.Length == 0
        ? Empty()
        : Border(
            VStack(2,
                suggestions.Select(s =>
                    TextBlock(s).Padding(8)).ToArray()
            ).Background("#FFFFFF")
        ).WithBorder("#E0E0E0").Width(300)
).Padding(20);
```

![Suggestion dropdown](images/recipe-search-with-suggestions/search.png)

The dropdown is a `Border` wrapping a `VStack` — same conditional
render shape as the [modal-dialog recipe](modal-dialog.md). When
`suggestions.Length == 0`, the `Empty()` factory drops the dropdown
out of the tree entirely.

For a production search box, swap the manual dropdown for
[`AutoSuggestBox`](../forms.md) — it wires keyboard navigation,
selection events, and the screen-reader contract. The recipe shows
the primitives so the composition is visible.

## Tips

**Cap the suggestion count.** `Take(5)` here; the catalog can be huge
but the dropdown only ever shows a handful. An open-ended list
forces a `ScrollView`, which forces keyboard handling, which forces
focus management — all real work to avoid for the common case.

**`UseMemo` is the load-bearing call.** Without it, a slow filter
runs on every render — including renders triggered by the
suggestion-keyboard navigation that didn't change the query. With
it, the filter runs only when the query actually changes.

**Plan for async early.** A client-side filter is one line; a network
filter needs cancellation, debounce, and an error pane. The
[`async-resources`](../async-resources.md) page covers the upgrade
path when the catalog moves off the local machine.

## Next Steps

- **[Forms](../forms.md)** — Promote to `AutoSuggestBox` once you
  need keyboard nav and the accessibility peer.
- **[Async Resources](../async-resources.md)** — Network-backed
  suggestions with cancellation.
- **[Hooks](../hooks.md)** — `UseMemo` semantics — when the cache
  invalidates vs. survives.
- **[Recipe: Master-detail](master-detail.md)** — Pair the search box
  with a master list of matched records.
- **[Recipes index](index.md)** — Back to the gallery.
