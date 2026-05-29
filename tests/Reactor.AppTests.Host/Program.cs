// Reactor AppTests Host — unified test host app for Appium/WinAppDriver UI tests.
// Normal mode: Shows fixture navigator UI for manual testing and Appium interactive tests.
// Self-test mode (--self-test): Runs all non-interactive fixtures in-process at CPU speed
//   using VisualTreeHelper, outputs TAP results to stdout, exits.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.AppTests.Host;
using Microsoft.UI.Reactor.AppTests.Host.DevtoolsStress;
using Microsoft.UI.Reactor.AppTests.Host.SelfTest;

if (args.Contains("--list-fixtures"))
{
    // Fast path: emit the selftest fixture registry, one name per line, and exit.
    // Used by Reactor.SelfTests to discover fixtures without launching WinUI.
    foreach (var name in SelfTestFixtureRegistry.AllFixtures)
        Console.WriteLine(name);
    return;
}

if (args.Contains("--self-test"))
{
    var filterIdx = Array.IndexOf(args, "--filter");
    if (filterIdx >= 0 && filterIdx + 1 < args.Length)
        SelfTestRunner.Filter = args[filterIdx + 1];
    if (args.Contains("--no-aot-skip"))
        SelfTestRunner.SkipAotPatterns = false;
    SelfTestRunner.RunAll();
}
else if (args.Contains("--devtools-stress"))
{
    DevtoolsStressRunner.Run(args);
}
else if (args.Contains("--devtools-stress-e2e"))
{
    DevtoolsStressE2ERunner.Run(args);
}
else if (args.Contains("--stress-child"))
{
    // Child process spawned by the E2E stress parent. Goes through the real
    // ReactorApp.Run<T>(devtools: true) path so that --devtools run on the
    // command line triggers TryRunDevtools → RunRunSubverb → full MCP init
    // (tool registration + AnnounceReady). That is the same code the
    // customer hits when they launch their app with --devtools.
    ReactorApp.Run<Microsoft.UI.Reactor.AppTests.Host.DevtoolsStress.StressChild>(
        "Stress Child", width: 320, height: 160, devtools: true);
}
else
    ReactorApp.Run<TestHost>(
        "Reactor Test Host",
        width: 1200,
        height: 800,
        // Spec 045 — register the native docking interop so docking E2E
        // fixtures (DockingInput_*) realize their tab/splitter chrome.
        configure: host => Microsoft.UI.Reactor.Docking.Native.DockingNativeInterop.Register(host.Reconciler));
