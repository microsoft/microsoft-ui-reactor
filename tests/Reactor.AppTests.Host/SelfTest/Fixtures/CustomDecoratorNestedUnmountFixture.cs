using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.V1Protocol;
using Microsoft.UI.Xaml;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>Checks outer decorator teardown when its target shares a flyout's native control.</summary>
internal sealed class CustomDecoratorNestedUnmountFixture(Harness h) : SelfTestFixtureBase(h)
{
    public override async Task RunAsync()
    {
        ControlRegistry.RegisterDecorator<Forwarder>(static () => new ForwarderHandler());

        foreach (var (nested, update) in new[] { (false, false), (false, true), (true, false), (true, true) })
        {
            int initialUnmounts = 0, updatedUnmounts = 0;
            string name = (nested ? "NestedFlyout" : "PlainButton") + (update ? "_Updated" : "_MountOnly");
            Action? rerender = null;
            using var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (version, setVersion) = ctx.UseState(0);
                rerender = () => setVersion(1);
                return new Forwarder(
                    nested ? Flyout(Button(name), TextBlock($"flyout content {version}")) : Button(name),
                    version,
                    version == 0 ? () => initialUnmounts++ : () => updatedUnmounts++);
            });
            await Harness.Render();

            var button = H.FindButton(name);
            H.Check($"CustomDecorator_{name}_TargetMounted", button is not null);
            if (button is null) continue;
            H.Check($"CustomDecorator_{name}_OuterTagWritten",
                Reconciler.GetElementTag(button) is Forwarder { Version: 0 });

            if (update)
            {
                rerender!();
                await Harness.Render();
                H.Check($"CustomDecorator_{name}_SameTarget", ReferenceEquals(button, H.FindButton(name)));
                H.Check($"CustomDecorator_{name}_CurrentOuterTag",
                    Reconciler.GetElementTag(button) is Forwarder { Version: 1 });
            }
            H.Check($"CustomDecorator_{name}_NoEarlyUnmount", initialUnmounts == 0 && updatedUnmounts == 0);

            host.Mount(_ => TextBlock("removed"));
            await Harness.Render();
            H.Check($"CustomDecorator_{name}_OuterUnmountRunsOnce",
                initialUnmounts == (update ? 0 : 1) && updatedUnmounts == (update ? 1 : 0),
                $"initial = {initialUnmounts}, updated = {updatedUnmounts}");
        }
    }

    private sealed record Forwarder(Element Target, int Version, Action OnUnmount) : Element;

    private sealed class ForwarderHandler : IDecoratorElementHandler<Forwarder>
    {
        public UIElement Mount(MountContext ctx, Forwarder element)
            => ctx.MountChild(element.Target)!;

        public UIElement Update(UpdateContext ctx, Forwarder oldElement, Forwarder newElement, UIElement control)
            => ctx.Reconciler.Update(oldElement.Target, newElement.Target, control, ctx.RequestRerender) ?? control;

        public V1UnmountDisposition Unmount(UnmountContext ctx, Forwarder? element, UIElement control)
        {
            element?.OnUnmount();
            return V1UnmountDisposition.ContinueDefaultTraversal;
        }

        public Element? GetSourceTarget(Forwarder element) => element.Target;
    }
}
