// Regeneration / staleness CLI for the ReactorGallery search index.
//
//   dotnet run --project tools/Reactor.SearchIndex            # rewrite the committed file
//   dotnet run --project tools/Reactor.SearchIndex -- --check # exit 1 if it is stale
//
// All logic lives in SearchIndexCli.Run (unit-tested by SearchIndexCliTests); the gate test
// drives SearchIndexGenerator.Generate(...) in-process instead of shelling out.

using Microsoft.UI.Reactor.SearchIndex;

return SearchIndexCli.Run(args);
