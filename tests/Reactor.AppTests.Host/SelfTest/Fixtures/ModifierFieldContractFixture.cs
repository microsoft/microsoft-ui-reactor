using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Spec 067 §10.1, layer 0: the brush and FontFamily half of
/// <c>ModifierFieldContractTests</c> (Reactor.Tests). Those fields can't be constructed
/// headless, so their merge round trip and two-way skip-equality matrix run here on the UI
/// thread. The covered names must equal <see cref="ModifierFieldContractSelfTestFields.Names"/>,
/// which the headless table checks against its own brush / FontFamily declarations.
/// </summary>
internal static class ModifierFieldContractFixture
{
    internal class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        private readonly List<string> _covered = [];

        public override Task RunAsync()
        {
            static SolidColorBrush Brush(byte r, double opacity = 1) => new(Color.FromArgb(255, r, 0, 0)) { Opacity = opacity };

            // BrushesEqual compares colour and opacity, so each brush gets a colour-only and an
            // opacity-only "different" sample.
            foreach (var (name, with, read) in new (string, Func<Brush, ElementModifiers>, Func<ElementModifiers, object?>)[]
            {
                ("Background", v => new ElementModifiers { Background = v }, m => m.Background),
                ("Foreground", v => new ElementModifiers { Foreground = v }, m => m.Foreground),
                ("BorderBrush", v => new ElementModifiers { BorderBrush = v }, m => m.BorderBrush),
            })
            {
                CheckField(name, Brush(1), [("DifferentColor", Brush(2)), ("DifferentOpacity", Brush(1, 0.5))], () => Brush(1),
                    v => with((Brush)v), read, new ElementModifiers { Visual = new VisualModifiers() });
            }

            CheckField("FontFamily", new FontFamily("Segoe UI"), [("Different", new FontFamily("Consolas"))], () => new FontFamily("Segoe UI"),
                v => new ElementModifiers { FontFamily = (FontFamily)v }, m => m.FontFamily, new ElementModifiers());

            H.Check("ModifierFieldContract_CoversTheSharedFieldList",
                _covered.Order(StringComparer.Ordinal).SequenceEqual(ModifierFieldContractSelfTestFields.Names.Order(StringComparer.Ordinal)));
            return Task.CompletedTask;
        }

        // Value-compared by the skip gate (BrushesEqual / FontFamiliesEqual), so a fresh but
        // value-equal instance must still let the element skip.
        private void CheckField(string name, object v1, (string Label, object Value)[] different, Func<object> fresh,
            Func<object, ElementModifiers> with, Func<ElementModifiers, object?> read, ElementModifiers emptyBucket)
        {
            _covered.Add(name);
            var again = fresh();
            var v2 = different[0].Value;
            H.Check($"ModifierFieldContract_{name}_SamplesDiffer",
                different.All(d => !ReferenceEquals(v1, d.Value)) && !ReferenceEquals(v1, again));

            var ours = with(v1);
            H.Check($"ModifierFieldContract_{name}_Merge_OtherWins", ReferenceEquals(read(ours.Merge(with(v2))), v2));
            H.Check($"ModifierFieldContract_{name}_Merge_KeepsOursWhenBucketUnset", ReferenceEquals(read(ours.Merge(emptyBucket)), v1));
            H.Check($"ModifierFieldContract_{name}_Merge_KeepsOursWhenNoModifiers", ReferenceEquals(read(ours.Merge(new ElementModifiers())), v1));
            H.Check($"ModifierFieldContract_{name}_Merge_TakesOtherWhenOursUnset", ReferenceEquals(read(emptyBucket.Merge(with(v2))), v2));

            // A null bag never equals a set one in full mode; visible mode reads it as empty.
            H.Check($"ModifierFieldContract_{name}_NullVsSet",
                !Element.ModifiersEqual(null, ours) && !Element.ModifiersEqual(ours, null)
                && !Element.ModifiersVisiblyEqual(null, ours) && !Element.ModifiersVisiblyEqual(ours, null));

            var pairs = new List<(string Pair, ElementModifiers A, ElementModifiers B, bool Equal)>
            {
                ("UnsetVsSet", new ElementModifiers(), ours, false),
                ("SameInstance", ours, with(v1), true),
                ("FreshEqual", ours, with(again), true),
            };
            foreach (var (label, value) in different)
                pairs.Add((label, ours, with(value), false));

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
