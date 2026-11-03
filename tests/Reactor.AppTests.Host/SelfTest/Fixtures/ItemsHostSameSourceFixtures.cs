using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class ItemsHostSameSourceFixtures
{
    private sealed class Probe
    {
        internal object[] Items = null!;
        internal Action<int> SetRevision = null!;
        internal int BodyVersion;
    }

    private sealed class Root : Component<Probe>
    {
        public override Element Render()
        {
            var (revision, set) = UseState(0);
            Props.SetRevision = set;
            return ListBoxWrapperElement.ListBox(items: Props.Items)
                .Margin(revision + Props.BodyVersion);
        }
    }

    private sealed class Counter : Component<Probe>
    {
        public override Element Render()
        {
            var (count, set) = UseState(0);
            return Button($"same-source:{count}:v{Props.BodyVersion}", () => set(count + 1));
        }
    }

    internal sealed class StateAndForce(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var probe = new Probe();
            probe.Items = [Component<Counter, Probe>(probe)];
            using var host = H.CreateHost();
            host.Mount(_ => Component<Root, Probe>(probe));
            H.Check("ItemsHost_SameSource_InitiallyRealized", await Harness.WaitFor(() =>
                H.FindControl<Button>(b => b.Content as string == "same-source:0:v0") is not null));
            var original = H.FindControl<Button>(b => b.Content as string == "same-source:0:v0");

            // A host-only property change may skip the stable items wholesale.
            probe.SetRevision(1);
            await Harness.Render();
            H.Check("ItemsHost_SameSource_StableLiveIdentity", ReferenceEquals(original,
                H.FindControl<Button>(b => b.Content as string == "same-source:0:v0")));
            H.ClickButton("same-source:0:v0");
            H.Check("ItemsHost_SameSource_DescendantStateNotSwallowed", await Harness.WaitFor(() =>
                ReferenceEquals(original, H.FindControl<Button>(b => b.Content as string == "same-source:1:v0"))));

            // Simulate a changed component body without changing its Element/props.
            // The host Margin changes, but no child state is dirty on this pass.
            probe.BodyVersion = 1;
            host.Reconciler.ForceFullRenderPending = true;
            host.RequestRender(force: true);
            H.Check("ItemsHost_SameSource_ForcedBodyRefresh", await Harness.WaitFor(() =>
                ReferenceEquals(original, H.FindControl<Button>(b => b.Content as string == "same-source:1:v1"))));

            // A replacement source must never enter the same-source fast path.
            probe.Items = [TextBlock("replacement-source")];
            probe.SetRevision(2);
            H.Check("ItemsHost_SameSource_ReplacementApplied", await Harness.WaitFor(() =>
                H.FindText("replacement-source") is not null
                && H.FindControl<Button>(b => b.Content as string == "same-source:1:v1") is null));
        }
    }

    internal sealed class ThemeCache(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            const string sourceKey = "ItemsHost_SameSource_SourceBrush";
            const string targetKey = "ItemsHost_SameSource_TargetBrush";
            var red = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 220, 0, 0));
            var blue = new SolidColorBrush(global::Windows.UI.Color.FromArgb(255, 0, 0, 220));
            var resources = new ResourceDictionary { [sourceKey] = red };
            Application.Current.Resources.MergedDictionaries.Add(resources);
            ThemeRef.InvalidateResolutionCache();
            try
            {
                var probe = new Probe { Items = [TextBlock("same-source-theme")] };
                using var host = H.CreateHost();
                host.Mount(_ => Component<Root, Probe>(probe));
                await Harness.Render();
                var original = H.FindText("same-source-theme");

                // Same keys/count, fresh source: update the cached theme flag on
                // the in-place path, not just mount/structural reconciliation.
                probe.Items = [TextBlock("same-source-theme")
                    .Resources(r => r.Set(targetKey, Theme.Ref(sourceKey)))];
                probe.SetRevision(1);
                await Harness.Render();
                H.Check("ItemsHost_SameSource_ThemeAddedInPlace", ReferenceEquals(original,
                    H.FindText("same-source-theme")) && ReferenceEquals(original?.Resources[targetKey], red));

                // Deterministic equivalent of a theme resolution invalidation:
                // the native concrete resource snapshot must be recomputed.
                resources[sourceKey] = blue;
                ThemeRef.InvalidateResolutionCache();
                probe.SetRevision(2);
                await Harness.Render();
                H.Check("ItemsHost_SameSource_ThemeSnapshotRefreshed", ReferenceEquals(original,
                    H.FindText("same-source-theme")) && ReferenceEquals(original?.Resources[targetKey], blue));
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(resources);
                ThemeRef.InvalidateResolutionCache();
            }
        }
    }
}
