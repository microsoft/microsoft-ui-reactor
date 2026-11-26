using Microsoft.UI.Reactor;
using WctControls;

if (args.Length != 0)
{
    if (args.Length != 2 || args[0] != "--page" || !SamplePages.Routes.Any(r => r.Id == args[1]))
        throw new ArgumentException($"Usage: wct-controls [--page <{string.Join('|', SamplePages.Routes.Select(r => r.Id))}>]");

    App.InitialPage = args[1];
}

ReactorApp.Run<App>("WCT × Reactor — control gallery", width: 980, height: 720);
