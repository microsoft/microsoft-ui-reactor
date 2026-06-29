using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

/// <summary>
/// Issue #682 — UI-thread coverage for <see cref="BrushHelper.Parse"/>. The
/// headless unit tests (<c>PerfDiffSkipPathTests.ParseColor_*</c>) can only
/// assert the cached <see cref="Color"/>; constructing/inspecting a
/// <see cref="SolidColorBrush"/> needs a UI thread. This fixture verifies that
/// <see cref="BrushHelper.Parse"/> yields a correct brush AND locks the
/// non-aliasing invariant: two parses of the same input must return DISTINCT
/// instances. That guard exists so any future reintroduction of brush-instance
/// caching (#168, reverted) must first prove it is safe under a <c>.Set</c>
/// mutation shared across elements — a <see cref="SolidColorBrush"/> is a
/// thread-affine, mutable <c>DependencyObject</c> and cannot be safely shared.
/// </summary>
internal static class BrushHelperParseFixture
{
    internal sealed class Execution(Harness h) : SelfTestFixtureBase(h)
    {
        public override Task RunAsync()
        {
            // ── 1. Hex #RRGGBB → correct fully-opaque Color + default Opacity ──
            var hex = BrushHelper.Parse("#3366CC");
            H.Check(
                "BrushHelperParse_Hex_Color",
                hex.Color == Color.FromArgb(255, 0x33, 0x66, 0xCC));
            H.Check(
                "BrushHelperParse_Hex_DefaultOpacity",
                hex.Opacity == 1.0);

            // ── 2. Hex #AARRGGBB → alpha is honored ──
            var hexAlpha = BrushHelper.Parse("#80112233");
            H.Check(
                "BrushHelperParse_HexAlpha_Color",
                hexAlpha.Color == Color.FromArgb(0x80, 0x11, 0x22, 0x33));

            // ── 3. Named color (case-insensitive) → correct Color ──
            var red = BrushHelper.Parse("red");
            H.Check(
                "BrushHelperParse_Named_Color",
                red.Color == Color.FromArgb(255, 255, 0, 0));
            var redMixedCase = BrushHelper.Parse("ReD");
            H.Check(
                "BrushHelperParse_Named_CaseInsensitive",
                redMixedCase.Color == red.Color);

            // ── 4. Non-aliasing: two parses of the SAME input → DISTINCT brushes ──
            // Locks the invariant behind the reverted #168 shared-brush cache.
            var a = BrushHelper.Parse("#3366CC");
            var b = BrushHelper.Parse("#3366CC");
            H.Check(
                "BrushHelperParse_NonAliasing_DistinctInstances",
                !ReferenceEquals(a, b));
            H.Check(
                "BrushHelperParse_NonAliasing_SameColorValue",
                a.Color == b.Color);

            // Mutating one brush must not leak into the other (the reason a
            // SolidColorBrush cannot be shared across controls).
            a.Color = Color.FromArgb(255, 0, 0, 0);
            H.Check(
                "BrushHelperParse_NonAliasing_MutationIsolated",
                b.Color == Color.FromArgb(255, 0x33, 0x66, 0xCC));

            return Task.CompletedTask;
        }
    }
}
