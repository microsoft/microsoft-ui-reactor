using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using HeadTrax;
using HeadTrax.Components;

// Parse --db and --graphql-url from command line
var cliArgs = Environment.GetCommandLineArgs();
var dbIdx = Array.IndexOf(cliArgs, "--db");
if (dbIdx >= 0 && dbIdx + 1 < cliArgs.Length)
    AppConfig.SqliteDbPath = cliArgs[dbIdx + 1];

var urlIdx = Array.IndexOf(cliArgs, "--graphql-url");
if (urlIdx >= 0 && urlIdx + 1 < cliArgs.Length)
    AppConfig.GraphQLUrl = cliArgs[urlIdx + 1];

// Phase-3 dogfood: DataGrid reads through UseInfiniteResource / UseDataSource by
// default on this sample. Pass --legacy-paging to revert to the DataPageCache path
// for a side-by-side comparison — the reversion path kept alive for one release
// cycle per the async-resources-implementation plan (§3.4 / §3.5).
bool legacyPaging = Array.IndexOf(cliArgs, "--legacy-paging") >= 0;
ReactorFeatureFlags.UseHookBasedPaging = !legacyPaging;

ReactorApp.Run<App>("HeadTrax – Employee Database", width: 1400, height: 900);
