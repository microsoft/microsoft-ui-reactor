using System;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for BrushHelper color parsing.
/// Uses the internal ParseHex method directly to avoid creating SolidColorBrush
/// (which requires a XAML Application context / UI thread).
/// </summary>
public class BrushHelperTests
{
    // ── Hex #RRGGBB ──────────────────────────────────────────────

    [Fact]
    public void ParseHex_RRGGBB()
    {
        var color = BrushHelper.ParseHex("#FF8800");
        Assert.Equal(255, color.A);
        Assert.Equal(255, color.R);
        Assert.Equal(0x88, color.G);
        Assert.Equal(0, color.B);
    }

    [Fact]
    public void ParseHex_RRGGBB_Lowercase()
    {
        var color = BrushHelper.ParseHex("#ff0000");
        Assert.Equal(255, color.A);
        Assert.Equal(255, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(0, color.B);
    }

    [Fact]
    public void ParseHex_RRGGBB_AllZeros()
    {
        var color = BrushHelper.ParseHex("#000000");
        Assert.Equal(255, color.A);
        Assert.Equal(0, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(0, color.B);
    }

    [Fact]
    public void ParseHex_RRGGBB_AllMax()
    {
        var color = BrushHelper.ParseHex("#FFFFFF");
        Assert.Equal(255, color.A);
        Assert.Equal(255, color.R);
        Assert.Equal(255, color.G);
        Assert.Equal(255, color.B);
    }

    // ── Hex #AARRGGBB ────────────────────────────────────────────

    [Fact]
    public void ParseHex_AARRGGBB()
    {
        var color = BrushHelper.ParseHex("#80FF0000");
        Assert.Equal(0x80, color.A);
        Assert.Equal(255, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(0, color.B);
    }

    [Fact]
    public void ParseHex_AARRGGBB_Transparent()
    {
        var color = BrushHelper.ParseHex("#00000000");
        Assert.Equal(0, color.A);
        Assert.Equal(0, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(0, color.B);
    }

    // ── Invalid / edge cases → gray fallback ─────────────────────

    [Fact]
    public void ParseHex_Invalid_Characters_Returns_Gray()
    {
        var color = BrushHelper.ParseHex("#GGHHII");
        Assert.Equal(128, color.R);
        Assert.Equal(128, color.G);
        Assert.Equal(128, color.B);
    }

    [Fact]
    public void ParseHex_Empty_After_Hash_Returns_Gray()
    {
        var color = BrushHelper.ParseHex("#");
        Assert.Equal(128, color.R);
        Assert.Equal(128, color.G);
        Assert.Equal(128, color.B);
    }

    [Fact]
    public void ParseHex_Short_Hex_3_Chars_Returns_Gray()
    {
        var color = BrushHelper.ParseHex("#FFF");
        Assert.Equal(128, color.R);
    }

    [Fact]
    public void ParseHex_Too_Long_Returns_Gray()
    {
        var color = BrushHelper.ParseHex("#AABBCCDDEE");
        Assert.Equal(128, color.R);
    }

    [Fact]
    public void ParseHex_5_Chars_Returns_Gray()
    {
        var color = BrushHelper.ParseHex("#AABBC");
        Assert.Equal(128, color.R);
    }

    [Fact]
    public void ParseHex_No_Hash_6_Chars()
    {
        // ParseHex trims # so passing without # should still work
        var color = BrushHelper.ParseHex("FF0000");
        Assert.Equal(255, color.A);
        Assert.Equal(255, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(0, color.B);
    }

    // ── Mixed case ───────────────────────────────────────────────

    [Fact]
    public void ParseHex_MixedCase()
    {
        var color = BrushHelper.ParseHex("#aAbBcC");
        Assert.Equal(0xAA, color.R);
        Assert.Equal(0xBB, color.G);
        Assert.Equal(0xCC, color.B);
    }

    // ── Case-insensitive color cache (Fix A) ─────────────────────

    [Fact]
    public void ParseColor_IsCaseInsensitive()
    {
        // Named colors and hex resolve identically regardless of input casing.
        Assert.Equal(BrushHelper.ParseColor("Red"), BrushHelper.ParseColor("red"));
        Assert.Equal(BrushHelper.ParseColor("RED"), BrushHelper.ParseColor("red"));
        Assert.Equal(BrushHelper.ParseColor("#FF0000"), BrushHelper.ParseColor("#ff0000"));
        Assert.Equal(BrushHelper.ParseColor("TRANSPARENT"), BrushHelper.ParseColor("transparent"));
    }

    [Fact]
    public void ParseColor_Cache_Dedupes_Across_Casing()
    {
        // Teeth for Fix A: _colorCache uses OrdinalIgnoreCase, so the many
        // casings of one color name collapse to a SINGLE cache entry. Value
        // equality alone can't prove this (ParseColor lowercases before the
        // switch, so even a case-sensitive cache would return equal colors) —
        // the observable difference is the dedupe, which we prove by allocation:
        // once one casing is cached, every other casing is a hit ⇒ no new entry
        // and no factory ToLowerInvariant alloc. Case-sensitive keying would
        // instead insert one entry per distinct casing (tens of KB here).
        const string word = "transparent"; // 11 letters ⇒ >500 distinct casings
        var variants = new string[500];
        for (int i = 0; i < variants.Length; i++) variants[i] = CasingOf(word, i);

        BrushHelper.ParseColor(word); // warm the single shared entry
        foreach (var v in variants) BrushHelper.ParseColor(v); // also warm + JIT

        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var v in variants) BrushHelper.ParseColor(v); // all cache hits
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 10_000,
            $"case-insensitive ParseColor over 500 distinct casings allocated {allocated} bytes " +
            "(expected ~0 — all should dedupe to one entry; case-sensitive keying would be far larger)");
    }

    private static string CasingOf(string word, int mask)
    {
        var chars = word.ToCharArray();
        for (int b = 0; b < chars.Length && b < 31; b++)
            chars[b] = ((mask >> b) & 1) == 1
                ? char.ToUpperInvariant(chars[b])
                : char.ToLowerInvariant(chars[b]);
        return new string(chars);
    }
}
