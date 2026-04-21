using Microsoft.UI.Reactor;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest;

/// <summary>
/// Runs all self-test fixtures in sequence, mounts each in a ReactorHost,
/// calls RunAsync(), captures TAP output, exits with 0/1.
/// </summary>
internal static class SelfTestRunner
{
    public static string? Filter { get; set; }

    public static void RunAll()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new ReactorApplication();
            var dispatcher = DispatcherQueue.GetForCurrentThread();

            var window = new Window { Title = "Reactor Self-Test" };
            window.AppWindow.Resize(new global::Windows.Graphics.SizeInt32(800, 600));
            var harness = new Harness(window);

            dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    var allFixtures = SelfTestFixtureRegistry.AllFixtures;
                    var fixtures = Filter is not null
                        ? allFixtures.Where(f => f.Contains(Filter, StringComparison.OrdinalIgnoreCase)).ToArray()
                        : allFixtures;
                    harness.SetupTitleBar(fixtures.Length);
                    window.Activate();
                    await Harness.Render(); // wait for initial layout

                    Console.WriteLine($"TAP version 14");
                    Console.WriteLine($"1..{fixtures.Length}");

                    int testIndex = 0;
                    foreach (var fixtureName in fixtures)
                    {
                        testIndex++;
                        harness.UpdateProgress(testIndex, fixtureName);
                        int failuresBefore = harness.Failures;
                        bool crashed = false;
                        try
                        {
                            var fixture = SelfTestFixtureRegistry.Create(fixtureName, harness);
                            if (fixture is null)
                            {
                                Console.WriteLine($"not ok {testIndex} {fixtureName} - fixture not found");
                                crashed = true;
                            }
                            else
                            {
                                Console.WriteLine($"# Running: {fixtureName}");
                                await fixture.RunAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            crashed = true;
                            Console.WriteLine($"not ok {testIndex} {fixtureName}_CRASH - {ex.GetType().Name}: {ex.Message}");
                            Console.Error.WriteLine(ex.ToString());
                        }
                        harness.MarkFixtureResult(testIndex - 1,
                            !crashed && harness.Failures == failuresBefore);
                    }

                    Console.WriteLine($"# Total failures: {harness.Failures}");
                    harness.FinalizeTaskbarProgress();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Bail out! {ex.GetType().Name}: {ex.Message}");
                    Console.Error.WriteLine(ex.ToString());
                }
                finally
                {
                    Console.Out.Flush();
                    Environment.Exit(harness.Failures > 0 ? 1 : 0);
                }
            });
        });
    }
}
