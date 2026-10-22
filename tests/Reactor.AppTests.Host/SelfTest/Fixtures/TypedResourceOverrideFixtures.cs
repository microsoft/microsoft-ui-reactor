using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class TypedResourceOverrideFixtures
{
    internal sealed class MountUpdateAndRemoval(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var family = new FontFamily("Consolas");
            var updatedFamily = new FontFamily("Segoe UI");
            Action<int>? setPhase = null;
            var host = H.CreateHost();
            host.Mount(ctx =>
            {
                var (phase, update) = ctx.UseState(0);
                setPhase = update;
                var element = Button("typed resource overrides");
                if (phase < 2)
                    element = element.Resources(r => r
                        .Set("TypedPadding", Padding(phase))
                        .Set("TypedWeight", new global::Windows.UI.Text.FontWeight((ushort)(phase == 0 ? 700 : 400)))
                        .Set("TypedFont", phase == 0 ? family : updatedFamily)
                        .Set("TypedBoolean", phase == 0)
                        .SetColor("TypedColor", Color(phase))
                        .Set("TypedGridLength", Length(phase))
                        .Set("TypedBrush", "#123456"));
                return element;
            });

            await Harness.Render();
            var control = H.FindControl<Button>(b => b.Content is "typed resource overrides")
                ?? throw new InvalidOperationException("Typed resource test button was not mounted.");
            CheckValues(control, 0, family);
            H.Check("TypedResource_StringStillCreatesBrush", control.Resources["TypedBrush"] is
                SolidColorBrush { Color: var color } && color == global::Windows.UI.Color.FromArgb(255, 18, 52, 86));
            control.Resources["UnmanagedResource"] = 17d;

            setPhase!(1);
            await Harness.Render();
            var updated = H.FindControl<Button>(b => b.Content is "typed resource overrides")
                ?? throw new InvalidOperationException("Typed resource test button disappeared on update.");
            H.Check("TypedResource_ControlReused", ReferenceEquals(control, updated));
            CheckValues(updated, 1, updatedFamily);

            setPhase(2);
            await Harness.Render();
            var cleared = H.FindControl<Button>(b => b.Content is "typed resource overrides")
                ?? throw new InvalidOperationException("Typed resource test button disappeared on removal.");
            H.Check("TypedResource_ControlReusedAfterRemoval", ReferenceEquals(updated, cleared));
            foreach (var key in new[] { "TypedPadding", "TypedWeight", "TypedFont", "TypedBoolean", "TypedColor", "TypedGridLength", "TypedBrush" })
                H.Check($"TypedResource_Removed_{key}", !cleared.Resources.ContainsKey(key));
            H.Check("TypedResource_UnmanagedPreserved", cleared.Resources["UnmanagedResource"] is double value && value == 17d);
        }

        private void CheckValues(Button control, int phase, FontFamily family)
        {
            H.Check($"TypedResource_Padding_{phase}", control.Resources["TypedPadding"] is Thickness padding && padding == Padding(phase));
            H.Check($"TypedResource_Weight_{phase}", control.Resources["TypedWeight"] is global::Windows.UI.Text.FontWeight weight
                && weight.Weight == (phase == 0 ? 700 : 400));
            H.Check($"TypedResource_Font_{phase}", control.Resources["TypedFont"] is FontFamily actualFamily
                && actualFamily.Source == family.Source);
            H.Check($"TypedResource_Boolean_{phase}", control.Resources["TypedBoolean"] is bool boolean && boolean == (phase == 0));
            H.Check($"TypedResource_Color_{phase}", control.Resources["TypedColor"] is global::Windows.UI.Color color && color == Color(phase));
            H.Check($"TypedResource_GridLength_{phase}", control.Resources["TypedGridLength"] is GridLength length && length == Length(phase));
        }

        private static Thickness Padding(int phase) => phase == 0 ? new(8, 4, 6, 2) : new(1, 3, 5, 7);
        private static global::Windows.UI.Color Color(int phase) => phase == 0
            ? global::Windows.UI.Color.FromArgb(127, 12, 34, 56)
            : global::Windows.UI.Color.FromArgb(255, 65, 43, 21);
        private static GridLength Length(int phase) => phase == 0 ? new(2, GridUnitType.Star) : new(12, GridUnitType.Pixel);
    }

    internal sealed class BreadcrumbResources(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            var padding = new Thickness(8, 4, 8, 0);
            var weight = new global::Windows.UI.Text.FontWeight(700);
            var host = H.CreateHost();
            host.Mount(ctx => VStack(
                BreadcrumbBar([Breadcrumb("Receive"), Breadcrumb("History")], _ => { }).Width(600).AutomationId("baseline-breadcrumb"),
                BreadcrumbBar([Breadcrumb("Receive"), Breadcrumb("History")], _ => { }).Width(600).AutomationId("styled-breadcrumb")
                .Resources(r => r
                    .Set("BreadcrumbBarItemThemeFontSize", 28)
                    .Set("BreadcrumbBarChevronFontSize", 16d)
                    .Set("BreadcrumbBarItemFontWeight", weight)
                    .Set("BreadcrumbBarChevronPadding", padding))));
            await Harness.Render();
            var styled = H.FindControl<BreadcrumbBar>(b => AutomationProperties.GetAutomationId(b) == "styled-breadcrumb")!;
            var baseline = H.FindControl<BreadcrumbBar>(b => AutomationProperties.GetAutomationId(b) == "baseline-breadcrumb")!;
            await Harness.WaitFor(() => Probe(styled) is { Text: not null, Chevron: not null }
                && Probe(baseline) is { Text: not null, Chevron: not null });
            var plain = Probe(baseline);
            var actual = Probe(styled);
            H.Check("TypedResource_BreadcrumbBaseline", plain.Text is not null && plain.Chevron is not null
                && plain.Text.FontWeight.Weight != weight.Weight && plain.Chevron.Padding != padding);
            H.Check("TypedResource_BreadcrumbRenderedWeight", actual.Text?.FontWeight.Weight == weight.Weight);
            H.Check("TypedResource_BreadcrumbRenderedPadding", actual.Chevron?.Padding == padding);
            H.Check("TypedResource_BreadcrumbRenderedSize", actual.Text?.FontSize == 28d);
        }

        private static (TextBlock? Text, TextBlock? Chevron) Probe(BreadcrumbBar bar)
        {
            var text = Descendants(bar).OfType<TextBlock>().ToArray();
            return (text.FirstOrDefault(t => t.Text == "Receive"),
                text.FirstOrDefault(t => t.Name == "PART_ChevronTextBlock"));
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (var descendant in Descendants(child)) yield return descendant;
            }
        }
    }

    internal sealed class FontFamilyEquality(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            static Element Font(string source) => TextBlock("font")
                .Resources(r => r.Set("ContentControlThemeFontFamily", new FontFamily(source)));
            H.Check("TypedResource_FreshFontFamiliesEqual", Element.ShallowEquals(Font("Consolas"), Font("Consolas")));
            H.Check("TypedResource_DifferentFontFamiliesDiffer", !Element.ShallowEquals(Font("Consolas"), Font("Segoe UI")));
            return Task.CompletedTask;
        }
    }
}
