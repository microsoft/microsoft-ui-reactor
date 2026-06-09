using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using WinUI = Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 057 §10 first-party proof: TeachingTip.Target is a reactive
/// ElementRef edge, not an imperative setter escape hatch.
/// </summary>
internal static class TeachingTipTargetFixtures
{
    internal sealed class TargetReferenceResolvesBothMountOrders(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            await AssertTargetResolves("TargetBeforeTip", targetFirst: true);
            await AssertTargetResolves("TipBeforeTarget", targetFirst: false);
        }

        private async Task AssertTargetResolves(string suffix, bool targetFirst)
        {
            var host = H.CreateHost();
            var targetName = $"TeachingTip_{suffix}_Button";
            var tipName = $"TeachingTip_{suffix}_Tip";

            host.Mount(ctx =>
            {
                var targetRef = ctx.UseElementRef<FrameworkElement>();
                var target = Border(
                    Button($"Target {suffix}", () => { })
                        .Set(b => b.Name = targetName)
                        .Ref(targetRef));
                var tip = Border(
                    TeachingTip($"Tip {suffix}", "Anchored through ElementRef.", target: targetRef)
                        .Set(t => t.Name = tipName));

                return targetFirst ? VStack(target, tip) : VStack(tip, target);
            });

            await Harness.Render();

            var resolved = await Harness.WaitFor(() =>
            {
                var target = H.FindControl<WinUI.Button>(b =>
                    b.Name == targetName &&
                    Reconciler.GetElementTag(b) is ButtonElement);
                var tip = H.FindControl<WinUI.TeachingTip>(t =>
                    t.Name == tipName &&
                    Reconciler.GetElementTag(t) is TeachingTipElement e &&
                    e.Target is not null);

                return target is not null &&
                       tip is not null &&
                       ReferenceEquals(tip.Target, target);
            });

            H.Check($"TeachingTip_{suffix}_TargetReferenceEqualsButton", resolved);
        }
    }
}
