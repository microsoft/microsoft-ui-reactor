using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Pins the harness contract every selftest leans on (issue #1679): <see cref="Harness.Render"/>
/// waits for the hosts a fixture made with <see cref="Harness.CreateHost"/>, not only the
/// primary window's host.
/// </summary>
internal static class HarnessRenderFixtures
{
    // Each step re-renders on a later dispatcher tick (an effect sets state), and costs at least
    // StepMs. Without a drain of this host, Render's own dispatcher yields plus its 16ms trailing
    // delay cover only a few steps, so the chain is still running when Render returns.
    private const int ChainLength = 20;
    private const int StepMs = 5;

    internal class DrainsCreatedHosts(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            // The runner forgets the previous fixture's hosts. Meaningful in a corpus run, where
            // earlier fixtures leave their never-disposed CreateHost hosts alive.
            H.Check("HarnessRender_StartsWithNoTrackedHosts", Harness.TrackedCreatedHostCount == 0,
                $"tracked={Harness.TrackedCreatedHostCount}");

            var host = H.CreateHost();
            Action<int>? setCount = null;
            int renders = 0;
            host.Mount(ctx =>
            {
                var (count, set) = ctx.UseState(0);
                setCount = set;
                renders++;
                ctx.UseEffect(() =>
                {
                    if (count > 0 && count < ChainLength) set(count + 1);
                }, count);
                if (count > 0) global::System.Threading.Thread.Sleep(StepMs);
                return TextBlock($"Chain:{count}").AutomationId("HarnessRenderChain");
            });
            await Harness.Render();
            H.Check("HarnessRender_InitialMounted", ChainText() == "Chain:0", $"text={ChainText()}");

            int rendersBefore = renders;
            setCount!(1);
            H.Check("HarnessRender_CreatedHostBusyAfterSetState", !host.IsIdle);

            await Harness.Render();
            H.Check("HarnessRender_CreatedHostIdleAfterRender", host.IsIdle,
                $"renders={renders - rendersBefore} text={ChainText()}");
            H.Check("HarnessRender_CreatedHostChainFinished", ChainText() == $"Chain:{ChainLength}",
                $"renders={renders - rendersBefore} text={ChainText()}");

            host.Dispose();
            H.SetContent(null);
        }

        private string? ChainText() =>
            H.FindControl<TextBlock>(t => AutomationProperties.GetAutomationId(t) == "HarnessRenderChain")?.Text;
    }
}
