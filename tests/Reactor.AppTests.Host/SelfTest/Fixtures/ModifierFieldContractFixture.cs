using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 067 §10.1, layer 0: the brush and FontFamily half of
/// <c>ModifierFieldContractTests</c> (Reactor.Tests). Those fields can't be constructed
/// headless, so their merge round trip and two-way skip-equality matrix run here on the UI
/// thread.
/// </summary>
internal static class ModifierFieldContractFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            static SolidColorBrush Brush(byte r) => new(Color.FromArgb(255, r, 0, 0));

            CheckField("Background",
                Brush(1), Brush(2), () => Brush(1),
                v => new ElementModifiers { Background = (Brush)v }, m => m.Background,
                new ElementModifiers { Visual = new VisualModifiers() });
            CheckField("Foreground",
                Brush(1), Brush(2), () => Brush(1),
                v => new ElementModifiers { Foreground = (Brush)v }, m => m.Foreground,
                new ElementModifiers { Visual = new VisualModifiers() });
            CheckField("BorderBrush",
                Brush(1), Brush(2), () => Brush(1),
                v => new ElementModifiers { BorderBrush = (Brush)v }, m => m.BorderBrush,
                new ElementModifiers { Visual = new VisualModifiers() });
            CheckField("FontFamily",
                new FontFamily("Segoe UI"), new FontFamily("Consolas"), () => new FontFamily("Segoe UI"),
                v => new ElementModifiers { FontFamily = (FontFamily)v }, m => m.FontFamily,
                new ElementModifiers());

            return Task.CompletedTask;
        }

        // Value-compared by the skip gate (BrushesEqual / FontFamiliesEqual), so a fresh but
        // value-equal instance must still let the element skip.
        private void CheckField(string name, object v1, object v2, Func<object> fresh,
            Func<object, ElementModifiers> with, Func<ElementModifiers, object?> read, ElementModifiers emptyBucket)
        {
            var again = fresh();
            H.Check($"ModifierFieldContract_{name}_SamplesDiffer", !ReferenceEquals(v1, v2) && !ReferenceEquals(v1, again));

            var ours = with(v1);
            H.Check($"ModifierFieldContract_{name}_Merge_OtherWins", ReferenceEquals(read(ours.Merge(with(v2))), v2));
            H.Check($"ModifierFieldContract_{name}_Merge_KeepsOursWhenBucketUnset", ReferenceEquals(read(ours.Merge(emptyBucket)), v1));
            H.Check($"ModifierFieldContract_{name}_Merge_KeepsOursWhenNoModifiers", ReferenceEquals(read(ours.Merge(new ElementModifiers())), v1));
            H.Check($"ModifierFieldContract_{name}_Merge_TakesOtherWhenOursUnset", ReferenceEquals(read(emptyBucket.Merge(with(v2))), v2));

            (string Pair, ElementModifiers A, ElementModifiers B, bool Equal)[] pairs =
            [
                ("UnsetVsSet", new ElementModifiers(), ours, false),
                ("Different", ours, with(v2), false),
                ("SameInstance", ours, with(v1), true),
                ("FreshEqual", ours, with(again), true),
            ];
            foreach (var (pair, a, b, equal) in pairs)
            {
                H.Check($"ModifierFieldContract_{name}_{pair}_ModifiersEqual",
                    Element.ModifiersEqual(a, b) == equal && Element.ModifiersEqual(b, a) == equal);
                H.Check($"ModifierFieldContract_{name}_{pair}_ModifiersVisiblyEqual",
                    Element.ModifiersVisiblyEqual(a, b) == equal && Element.ModifiersVisiblyEqual(b, a) == equal);
            }
        }
    }
}
