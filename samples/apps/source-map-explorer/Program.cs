using Microsoft.UI.Reactor;

namespace SourceMapExplorer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Normally the devtools verb flips this (ReactorApp.DevtoolsEnabled mirrors
        // into it). Setting it directly keeps the sample runnable with a plain
        // `dotnet run`, and the in-app toggle lets you flip it live.
        Microsoft.UI.Reactor.Diagnostics.ReactorSourceMap.Enabled = true;

        ReactorApp.Run<App>("Reactor source map explorer", width: 1000, height: 680);
    }
}
