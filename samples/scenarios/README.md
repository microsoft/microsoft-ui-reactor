# Reactor Sample Catalogue

Curated, compilable single-file Reactor scenarios indexed by `mur find`.

## Authoring contract

Every scenario folder contains exactly two files:

- **`Scenario.cs`** — a complete single-file Reactor app
- **`scenario.json`** — sidecar metadata for the search index

### `Scenario.cs` format

```csharp
// id: <scenario-id>
// intent: <one-line description>
#:package Microsoft.UI.Reactor@0.0.0-local
#:property Platform=ARM64

using Microsoft.UI.Reactor;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("Title", width: 400, height: 200);

class App : Component
{
    public override Element Render() { /* ... */ }
}
```

### `scenario.json` schema

```json
{
  "id": "kebab-case-name",
  "category": "hooks|layout|inputs|...",
  "title": "Human-readable title",
  "intent": "search-friendly description of what this demonstrates",
  "tags": ["keyword1", "keyword2"],
  "factoryAnchors": ["FactoryName1", "FactoryName2"],
  "notesKey": "FactoryOrHookName",
  "relatedIds": ["other-scenario-id"],
  "priority": "P0"
}
```

The folder name IS the scenario id.
