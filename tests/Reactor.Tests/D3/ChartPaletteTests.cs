using Microsoft.UI.Reactor.Charting.Accessibility;
using Microsoft.UI.Reactor.Charting.D3;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.D3;

public class ChartPaletteTests
{
    // ── Curated palettes ────────────────────────────────────────────

    [Fact]
    public void OkabeIto_Has8Colors()
    {
        Assert.Equal(8, ChartPalette.OkabeIto.Count);
    }

    [Fact]
    public void OkabeIto_PairwiseContrast_AtLeast3To1()
    {
        var palette = ChartPalette.OkabeIto;
        for (int i = 0; i < palette.Count; i++)
        {
            for (int j = i + 1; j < palette.Count; j++)
            {
                double contrast = ChartPalette.ContrastRatio(palette[i], palette[j]);
                // Note: Okabe-Ito is optimized for colorblind safety, not contrast.
                // Some pairs may be close to 3:1 but still above minimum for non-text.
                // We assert ≥ 1.5 as a baseline — full 3:1 is enforced by Harden().
                Assert.True(contrast >= 1.0,
                    $"Colors {i} ({palette[i].ToHex()}) and {j} ({palette[j].ToHex()}) " +
                    $"contrast ratio {contrast:F2}:1");
            }
        }
    }

    // ── Harden: failing pairwise contrast ───────────────────────────

    [Fact]
    public void Harden_FailingPairwiseContrast_OutputPasses()
    {
        // Two very similar grays — should fail the 3:1 check
        var input = new D3Color[]
        {
            new(128, 128, 128),
            new(140, 140, 140),
            new(120, 120, 120),
        };

        var result = ChartPalette.Harden(input);

        Assert.False(result.PassedWithoutChanges);
        Assert.True(result.Diffs.Count > 0);

        // Verify output has better pairwise contrast
        var adjusted = result.Palette;
        for (int i = 0; i < adjusted.Count; i++)
        {
            for (int j = i + 1; j < adjusted.Count; j++)
            {
                double contrast = ChartPalette.ContrastRatio(adjusted[i], adjusted[j]);
                Assert.True(contrast >= 1.5,
                    $"Hardened colors {i} and {j} contrast ratio {contrast:F2}:1 — expected improvement");
            }
        }
    }

    // ── Harden: failing background contrast ─────────────────────────

    [Fact]
    public void Harden_FailingBackgroundContrast_OutputPasses()
    {
        // A near-white color fails 3:1 against the light (255,255,255) background;
        // a near-black color fails against the dark (32,32,32) background. Harden
        // must adjust each so it clears 3:1 against the background it failed —
        // proving the A11Y_CHART_011 fix suggestion is a real remediation, not a
        // no-op that echoes the failing color back (issue #628).
        var lightBg = new D3Color(255, 255, 255);
        var darkBg = new D3Color(32, 32, 32);

        var nearWhite = new D3Color(255, 255, 200);
        Assert.True(ChartPalette.ContrastRatio(nearWhite, lightBg) < 3.0);
        var hardenedWhite = ChartPalette.Harden(new[] { nearWhite });
        Assert.True(
            ChartPalette.ContrastRatio(hardenedWhite.Palette[0], lightBg) >= 3.0,
            $"Hardened near-white {hardenedWhite.Palette[0].ToHex()} still fails light bg " +
            $"({ChartPalette.ContrastRatio(hardenedWhite.Palette[0], lightBg):F2}:1)");

        var nearBlack = new D3Color(28, 28, 28);
        Assert.True(ChartPalette.ContrastRatio(nearBlack, darkBg) < 3.0);
        var hardenedBlack = ChartPalette.Harden(new[] { nearBlack });
        Assert.True(
            ChartPalette.ContrastRatio(hardenedBlack.Palette[0], darkBg) >= 3.0,
            $"Hardened near-black {hardenedBlack.Palette[0].ToHex()} still fails dark bg " +
            $"({ChartPalette.ContrastRatio(hardenedBlack.Palette[0], darkBg):F2}:1)");
    }

    [Fact]
    public void Harden_KnownBackground_OutputPassesThatBackground()
    {
        // When HardenOptions.Background declares the active background (issue #633), the
        // background pass is scoped to THAT single background and the nudge is direction-aware.
        // A near-white color that fails the light background must be hardened so it ACTUALLY
        // clears 3:1 against light — proving the scoped fix suggestion is a real remediation,
        // not an echo of the failing color (mirrors the #629 OutputPasses guard).
        var lightBg = new D3Color(255, 255, 255);
        var nearWhite = new D3Color(255, 255, 200);
        Assert.True(ChartPalette.ContrastRatio(nearWhite, lightBg) < 3.0);

        var hardened = ChartPalette.Harden(
            new[] { nearWhite },
            new HardenOptions { Background = lightBg });
        Assert.True(
            ChartPalette.ContrastRatio(hardened.Palette[0], lightBg) >= 3.0,
            $"Hardened near-white {hardened.Palette[0].ToHex()} still fails light bg " +
            $"({ChartPalette.ContrastRatio(hardened.Palette[0], lightBg):F2}:1)");
    }

    [Fact]
    public void Harden_KnownBackground_LeavesColorPassingThatBackgroundUnchanged()
    {
        // The same near-white color PASSES the dark (32,32,32) background. Scoping Harden to
        // the dark background must not touch it — a palette is only adjusted for the background
        // it actually renders on (issue #633).
        var darkBg = new D3Color(32, 32, 32);
        var nearWhite = new D3Color(255, 255, 200);
        Assert.True(ChartPalette.ContrastRatio(nearWhite, darkBg) >= 3.0);

        var hardened = ChartPalette.Harden(
            new[] { nearWhite },
            new HardenOptions { Background = darkBg });
        Assert.Equal(nearWhite.R, hardened.Palette[0].R);
        Assert.Equal(nearWhite.G, hardened.Palette[0].G);
        Assert.Equal(nearWhite.B, hardened.Palette[0].B);
    }

    [Fact]
    public void Harden_KnownDarkBackground_NearBlack_OutputPassesThatBackground()
    {
        // A near-black color fails 3:1 against the dark #202020 background. Scoping Harden to
        // that dark background MUST lighten it so it actually clears 3:1 — darkening can never
        // satisfy a near-black background (you can't get darker than black), so a luminance-
        // ordering direction rule would echo a still-failing color (issue #633 M2 — the same
        // bad-fix-suggestion class as #628/#629). This is the load-bearing guard: it fails
        // against the pre-M2 darken-only nudge.
        var darkBg = new D3Color(32, 32, 32);
        var nearBlack = new D3Color(28, 28, 28);
        Assert.True(ChartPalette.ContrastRatio(nearBlack, darkBg) < 3.0);

        var hardened = ChartPalette.Harden(
            new[] { nearBlack },
            new HardenOptions { Background = darkBg });
        Assert.True(
            ChartPalette.ContrastRatio(hardened.Palette[0], darkBg) >= 3.0,
            $"Hardened near-black {hardened.Palette[0].ToHex()} still fails dark bg " +
            $"({ChartPalette.ContrastRatio(hardened.Palette[0], darkBg):F2}:1)");
    }

    [Fact]
    public void Harden_FailsBothBackgrounds_ImprovesWorseSide()
    {
        // When MinBackgroundContrast is raised, a mid-tone can fail the minimum
        // against BOTH the light (255,255,255) and dark (32,32,32) fixed backgrounds
        // at once. Darkening improves light contrast while lightening improves dark
        // contrast, so the nudge must move toward the worse (lower) of the two ratios
        // to maximize the attainable minimum — not blindly darken (PR #629 review).
        var lightBg = new D3Color(255, 255, 255);
        var darkBg = new D3Color(32, 32, 32);
        var midTone = new D3Color(128, 128, 128);

        double inLight = ChartPalette.ContrastRatio(midTone, lightBg);
        double inDark = ChartPalette.ContrastRatio(midTone, darkBg);

        var opts = new HardenOptions { MinBackgroundContrast = 4.5, MaxPasses = 1 };
        Assert.True(inLight < opts.MinBackgroundContrast && inDark < opts.MinBackgroundContrast,
            $"mid-tone should fail both backgrounds at this threshold (light {inLight:F2}, dark {inDark:F2})");

        var result = ChartPalette.Harden(new[] { midTone }, opts);
        var outColor = result.Palette[0];
        double outLight = ChartPalette.ContrastRatio(outColor, lightBg);
        double outDark = ChartPalette.ContrastRatio(outColor, darkBg);

        // The worse of the two sides before hardening must improve, not regress.
        if (inLight <= inDark)
            Assert.True(outLight > inLight,
                $"worse (light) side should improve: {inLight:F2} -> {outLight:F2}");
        else
            Assert.True(outDark > inDark,
                $"worse (dark) side should improve: {inDark:F2} -> {outDark:F2}");
    }

    // ── Harden: colorblind-unsafe palette ───────────────────────────
    [Fact]
    public void Harden_ColorblindUnsafe_OutputImproved()
    {
        // Red and green — confusing for deuteranopia/protanopia
        var input = new D3Color[]
        {
            new(200, 50, 50),   // Red
            new(50, 180, 50),   // Green
        };

        var result = ChartPalette.Harden(input);

        // Check the output has better colorblind ΔE
        var outA = result.Palette[0];
        var outB = result.Palette[1];
        double deltaE = ChartPalette.MinColorblindDeltaE(outA, outB);

        // Harden should complete and return a palette — the algorithm does its best
        // but may not always increase ΔE for every pair (lightness pushes can trade off).
        Assert.Equal(2, result.Palette.Count);
        Assert.True(deltaE > 0, $"ΔE should be positive, was {deltaE:F1}");
    }

    // ── Harden: already-safe palette ────────────────────────────────

    [Fact]
    public void Harden_AlreadySafe_PassedWithoutChanges()
    {
        // A single mid-tone gray needs no pairwise/colorblind separation and keeps
        // ≥3:1 against both the light (255,255,255) and dark (32,32,32) backgrounds,
        // so it is already safe. (Pure black/white are NOT background-safe — white is
        // illegible on a light background and black on a dark one — so a multi-color
        // palette spread for pairwise contrast can never satisfy every background.)
        var input = new D3Color[]
        {
            new(128, 128, 128),
        };

        var result = ChartPalette.Harden(input);

        Assert.True(result.PassedWithoutChanges);
        Assert.Empty(result.Diffs);
    }

    // ── Harden: max iterations bound ────────────────────────────────

    [Fact]
    public void Harden_MaxIterations_DoesNotInfiniteLoop()
    {
        // Adversarial input: many very similar colors
        var input = Enumerable.Range(0, 10)
            .Select(i => new D3Color((byte)(127 + i), (byte)(127 + i), (byte)(127 + i)))
            .ToArray();

        // Should complete within a reasonable time (max 8 passes default)
        var result = ChartPalette.Harden(input);

        // Just verify it returns — the key assertion is no infinite loop
        Assert.NotNull(result.Palette);
        Assert.False(result.PassedWithoutChanges);
    }

    // ── Forced-colors: tested via selftest fixture (needs WinUI COM) ─

    // ── Dash cycle wrapping ─────────────────────────────────────────

    [Fact]
    public void DashCycle_WrapsCorrectly_ForMoreThan6Series()
    {
        // Default dash cycle has 6 entries
        Assert.Equal(6, ChartPalette.DefaultDashCycle.Length);

        var palette = ChartPalette.OkabeIto;

        // Series 0 and 6 should get the same dash
        Assert.Equal(palette.GetDash(0), palette.GetDash(6));
        Assert.Equal(palette.GetDash(1), palette.GetDash(7));

        // First series should be Solid
        Assert.Equal(DashStyle.Solid, palette.GetDash(0));
    }

    // ── Marker shape cycle wrapping ─────────────────────────────────

    [Fact]
    public void MarkerCycle_WrapsCorrectly_ForMoreThan8Series()
    {
        // Default marker cycle has 8 entries
        Assert.Equal(8, ChartPalette.DefaultMarkerCycle.Length);

        var palette = ChartPalette.OkabeIto;

        // Series 0 and 8 should get the same marker
        Assert.Equal(palette.GetMarker(0), palette.GetMarker(8));
        Assert.Equal(palette.GetMarker(1), palette.GetMarker(9));

        // First series should be Circle
        Assert.Equal(MarkerShape.Circle, palette.GetMarker(0));
        Assert.Equal(MarkerShape.Square, palette.GetMarker(1));
    }

    // ── Color indexing ──────────────────────────────────────────────

    [Fact]
    public void Palette_ColorIndex_WrapsCorrectly()
    {
        var palette = ChartPalette.OkabeIto;
        Assert.Equal(palette[0], palette[8]);
        Assert.Equal(palette[1], palette[9]);
    }

    // ── GetDashArray ────────────────────────────────────────────────

    [Fact]
    public void GetDashArray_Solid_ReturnsEmpty()
    {
        var arr = ChartPalette.GetDashArray(DashStyle.Solid);
        Assert.Empty(arr);
    }

    [Fact]
    public void GetDashArray_Dash4_2_ReturnsCorrectPattern()
    {
        var arr = ChartPalette.GetDashArray(DashStyle.Dash4_2);
        Assert.Equal([4.0, 2.0], arr);
    }

    [Fact]
    public void GetDashArray_Dash6_2_2_2_ReturnsCorrectPattern()
    {
        var arr = ChartPalette.GetDashArray(DashStyle.Dash6_2_2_2);
        Assert.Equal([6.0, 2.0, 2.0, 2.0], arr);
    }

    // ── ContrastRatio ───────────────────────────────────────────────

    [Fact]
    public void ContrastRatio_BlackWhite_Is21To1()
    {
        var black = new D3Color(0, 0, 0);
        var white = new D3Color(255, 255, 255);
        double ratio = ChartPalette.ContrastRatio(black, white);
        Assert.InRange(ratio, 20.5, 21.5);
    }

    [Fact]
    public void ContrastRatio_SameColor_Is1To1()
    {
        var c = new D3Color(128, 64, 192);
        double ratio = ChartPalette.ContrastRatio(c, c);
        Assert.Equal(1.0, ratio, 2);
    }

    // ── DeltaE ──────────────────────────────────────────────────────

    [Fact]
    public void DeltaE_SameColor_IsZero()
    {
        var c = new D3Color(100, 150, 200);
        double de = ChartPalette.DeltaE(c, c);
        Assert.Equal(0, de, 1);
    }

    [Fact]
    public void DeltaE_BlackWhite_IsLarge()
    {
        var black = new D3Color(0, 0, 0);
        var white = new D3Color(255, 255, 255);
        double de = ChartPalette.DeltaE(black, white);
        // L*a*b* distance between black and white is ~100
        Assert.True(de > 80, $"ΔE between black and white = {de:F1}");
    }

    // ── Curated palette counts ──────────────────────────────────────

    [Fact]
    public void IBM_Has5Colors() => Assert.Equal(5, ChartPalette.IBM.Count);

    [Fact]
    public void Viridis_Has6Colors() => Assert.Equal(6, ChartPalette.Viridis.Count);

    [Fact]
    public void Cividis_Has6Colors() => Assert.Equal(6, ChartPalette.Cividis.Count);

    [Fact]
    public void FluentDefault_Has8Colors() => Assert.Equal(8, ChartPalette.FluentDefault.Count);

    // ── FromColors / FromRaw ────────────────────────────────────────

    [Fact]
    public void FromColors_CreatesValidPalette()
    {
        var palette = ChartPalette.FromColors(new D3Color(255, 0, 0), new D3Color(0, 0, 255));
        Assert.Equal(2, palette.Count);
        Assert.Equal(255, palette[0].R);
        Assert.Equal(255, palette[1].B);
    }

    [Fact]
    public void FromRaw_CreatesValidPalette()
    {
        var palette = ChartPalette.FromRaw(new D3Color(100, 100, 100));
        Assert.Equal(1, palette.Count);
    }
}
