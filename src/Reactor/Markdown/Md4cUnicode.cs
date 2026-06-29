// C# port of Martin Mitas's md4c Markdown parser Unicode support.
// Ported from md4c/src/md4c.c (Unicode Support section)
//
// AI-HINT: Unicode case-folding property table (multi-codepoint folds the BCL
// cannot express). Whitespace and punctuation/symbol classification now defer to
// System.Text.Rune + UnicodeCategory; only the fold map uses the packed range
// encoding (high bits = range markers) searched via UnicodeBsearch().

using System.Globalization;
using System.Text;

namespace Microsoft.UI.Reactor.Markdown
{
    internal struct UnicodeFoldInfo
    {
        public uint[] Codepoints; // length 3
        public int Count;
    }

    internal static class Md4cUnicode
    {
        // Flag constants for range encoding in map arrays.
        private const uint RangeMinFlag = 0x40000000;
        private const uint RangeMaxFlag = 0x80000000;
        private const uint CodepointMask = 0x00ffffff;

        /// <summary>
        /// Binary search over sorted "map" of codepoints. Consecutive sequences
        /// of codepoints may be encoded in the map by just using the
        /// (MIN_CODEPOINT | 0x40000000) and (MAX_CODEPOINT | 0x80000000).
        ///
        /// Returns index of the found record in the map (in the case of ranges,
        /// the minimal value is used); or -1 on failure.
        /// </summary>
        private static int UnicodeBsearch(uint codepoint, uint[] map)
        {
            int beg = 0;
            int end = map.Length - 1;
            while (beg <= end)
            {
                // Pivot may be a range, not just a single value.
                int pivotBeg, pivotEnd;
                pivotBeg = pivotEnd = (beg + end) / 2;
                if ((map[pivotEnd] & RangeMinFlag) != 0)
                    pivotEnd++;
                if ((map[pivotBeg] & RangeMaxFlag) != 0)
                    pivotBeg--;

                if (codepoint < (map[pivotBeg] & CodepointMask))
                    end = pivotBeg - 1;
                else if (codepoint > (map[pivotEnd] & CodepointMask))
                    beg = pivotEnd + 1;
                else
                    return pivotBeg;
            }

            return -1;
        }

        // ---------------------------------------------------------------
        // Whitespace detection
        // ---------------------------------------------------------------

        private static bool IsUnicodeWhitespaceImpl(uint codepoint)
        {
            // The ASCII ones are the most frequently used ones, also CommonMark
            // specification requests few more in this range.
            if (codepoint <= 0x7f)
                return IsWhitespace((char)codepoint);

            // Non-ASCII: CommonMark's "Unicode whitespace" is exactly the Zs
            // (Space_Separator) general category. Defer to the BCL's Unicode data
            // rather than a generated table.
            return Rune.TryCreate(codepoint, out Rune rune)
                && Rune.GetUnicodeCategory(rune) == UnicodeCategory.SpaceSeparator;
        }

        // ---------------------------------------------------------------
        // Punctuation detection
        // ---------------------------------------------------------------

        private static bool IsUnicodePunctImpl(uint codepoint)
        {
            // The ASCII ones are the most frequently used ones, also CommonMark
            // specification requests few more in this range.
            if (codepoint <= 0x7f)
                return IsPunct((char)codepoint);

            // Non-ASCII: md4c classifies both the Unicode "P" (punctuation) and
            // "S" (symbol) general categories as punctuation-like for delimiter
            // run analysis. Defer to the BCL's Unicode data rather than a table.
            if (!Rune.TryCreate(codepoint, out Rune rune))
                return false;

            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.ConnectorPunctuation:
                case UnicodeCategory.DashPunctuation:
                case UnicodeCategory.OpenPunctuation:
                case UnicodeCategory.ClosePunctuation:
                case UnicodeCategory.InitialQuotePunctuation:
                case UnicodeCategory.FinalQuotePunctuation:
                case UnicodeCategory.OtherPunctuation:
                case UnicodeCategory.MathSymbol:
                case UnicodeCategory.CurrencySymbol:
                case UnicodeCategory.ModifierSymbol:
                case UnicodeCategory.OtherSymbol:
                    return true;
                default:
                    return false;
            }
        }

        // ---------------------------------------------------------------
        // Case folding
        // ---------------------------------------------------------------

        // Unicode full case-folding maps (retained by design — issue #433 item 4).
        // The BCL exposes only simple lower-casing / ignore-case comparison, not
        // full Unicode case folding, which CommonMark requires for reference-label
        // matching. Full folding includes multi-codepoint expansions the BCL cannot
        // produce (e.g. U+00DF ß -> "ss", U+0130 İ -> "i" + combining dot, ligatures
        // such as U+FB03 ﬃ -> "ffi"). C# port of md4c's fold maps; do not hand-edit.

        private static readonly uint[] FoldMap1 =
        {
            0x0041 | RangeMinFlag, 0x005a | RangeMaxFlag,
            0x00b5,
            0x00c0 | RangeMinFlag, 0x00d6 | RangeMaxFlag,
            0x00d8 | RangeMinFlag, 0x00de | RangeMaxFlag,
            0x0100 | RangeMinFlag, 0x012e | RangeMaxFlag,
            0x0132 | RangeMinFlag, 0x0136 | RangeMaxFlag,
            0x0139 | RangeMinFlag, 0x0147 | RangeMaxFlag,
            0x014a | RangeMinFlag, 0x0176 | RangeMaxFlag,
            0x0178,
            0x0179 | RangeMinFlag, 0x017d | RangeMaxFlag,
            0x017f, 0x0181, 0x0182, 0x0184, 0x0186, 0x0187, 0x0189, 0x018a, 0x018b,
            0x018e, 0x018f, 0x0190, 0x0191, 0x0193, 0x0194, 0x0196, 0x0197, 0x0198,
            0x019c, 0x019d, 0x019f,
            0x01a0 | RangeMinFlag, 0x01a4 | RangeMaxFlag,
            0x01a6, 0x01a7, 0x01a9, 0x01ac, 0x01ae, 0x01af, 0x01b1, 0x01b2,
            0x01b3, 0x01b5, 0x01b7, 0x01b8, 0x01bc, 0x01c4, 0x01c5, 0x01c7, 0x01c8,
            0x01ca,
            0x01cb | RangeMinFlag, 0x01db | RangeMaxFlag,
            0x01de | RangeMinFlag, 0x01ee | RangeMaxFlag,
            0x01f1, 0x01f2, 0x01f4, 0x01f6, 0x01f7,
            0x01f8 | RangeMinFlag, 0x021e | RangeMaxFlag,
            0x0220,
            0x0222 | RangeMinFlag, 0x0232 | RangeMaxFlag,
            0x023a, 0x023b, 0x023d, 0x023e, 0x0241, 0x0243, 0x0244, 0x0245,
            0x0246 | RangeMinFlag, 0x024e | RangeMaxFlag,
            0x0345, 0x0370, 0x0372, 0x0376, 0x037f,
            0x0386,
            0x0388 | RangeMinFlag, 0x038a | RangeMaxFlag,
            0x038c, 0x038e, 0x038f,
            0x0391 | RangeMinFlag, 0x03a1 | RangeMaxFlag,
            0x03a3 | RangeMinFlag, 0x03ab | RangeMaxFlag,
            0x03c2, 0x03cf, 0x03d0, 0x03d1, 0x03d5, 0x03d6,
            0x03d8 | RangeMinFlag, 0x03ee | RangeMaxFlag,
            0x03f0, 0x03f1, 0x03f4, 0x03f5, 0x03f7, 0x03f9, 0x03fa,
            0x03fd | RangeMinFlag, 0x03ff | RangeMaxFlag,
            0x0400 | RangeMinFlag, 0x040f | RangeMaxFlag,
            0x0410 | RangeMinFlag, 0x042f | RangeMaxFlag,
            0x0460 | RangeMinFlag, 0x0480 | RangeMaxFlag,
            0x048a | RangeMinFlag, 0x04be | RangeMaxFlag,
            0x04c0,
            0x04c1 | RangeMinFlag, 0x04cd | RangeMaxFlag,
            0x04d0 | RangeMinFlag, 0x052e | RangeMaxFlag,
            0x0531 | RangeMinFlag, 0x0556 | RangeMaxFlag,
            0x10a0 | RangeMinFlag, 0x10c5 | RangeMaxFlag,
            0x10c7, 0x10cd,
            0x13f8 | RangeMinFlag, 0x13fd | RangeMaxFlag,
            0x1c80, 0x1c81, 0x1c82, 0x1c83, 0x1c84, 0x1c85, 0x1c86, 0x1c87, 0x1c88,
            0x1c90 | RangeMinFlag, 0x1cba | RangeMaxFlag,
            0x1cbd | RangeMinFlag, 0x1cbf | RangeMaxFlag,
            0x1e00 | RangeMinFlag, 0x1e94 | RangeMaxFlag,
            0x1e9b,
            0x1ea0 | RangeMinFlag, 0x1efe | RangeMaxFlag,
            0x1f08 | RangeMinFlag, 0x1f0f | RangeMaxFlag,
            0x1f18 | RangeMinFlag, 0x1f1d | RangeMaxFlag,
            0x1f28 | RangeMinFlag, 0x1f2f | RangeMaxFlag,
            0x1f38 | RangeMinFlag, 0x1f3f | RangeMaxFlag,
            0x1f48 | RangeMinFlag, 0x1f4d | RangeMaxFlag,
            0x1f59, 0x1f5b, 0x1f5d, 0x1f5f,
            0x1f68 | RangeMinFlag, 0x1f6f | RangeMaxFlag,
            0x1fb8, 0x1fb9, 0x1fba, 0x1fbb, 0x1fbe,
            0x1fc8 | RangeMinFlag, 0x1fcb | RangeMaxFlag,
            0x1fd8, 0x1fd9, 0x1fda, 0x1fdb,
            0x1fe8, 0x1fe9, 0x1fea, 0x1feb, 0x1fec,
            0x1ff8, 0x1ff9, 0x1ffa, 0x1ffb,
            0x2126, 0x212a, 0x212b, 0x2132,
            0x2160 | RangeMinFlag, 0x216f | RangeMaxFlag,
            0x2183,
            0x24b6 | RangeMinFlag, 0x24cf | RangeMaxFlag,
            0x2c00 | RangeMinFlag, 0x2c2f | RangeMaxFlag,
            0x2c60, 0x2c62, 0x2c63, 0x2c64,
            0x2c67 | RangeMinFlag, 0x2c6b | RangeMaxFlag,
            0x2c6d, 0x2c6e, 0x2c6f, 0x2c70, 0x2c72, 0x2c75, 0x2c7e, 0x2c7f,
            0x2c80 | RangeMinFlag, 0x2ce2 | RangeMaxFlag,
            0x2ceb, 0x2ced, 0x2cf2,
            0xa640 | RangeMinFlag, 0xa66c | RangeMaxFlag,
            0xa680 | RangeMinFlag, 0xa69a | RangeMaxFlag,
            0xa722 | RangeMinFlag, 0xa72e | RangeMaxFlag,
            0xa732 | RangeMinFlag, 0xa76e | RangeMaxFlag,
            0xa779, 0xa77b, 0xa77d,
            0xa77e | RangeMinFlag, 0xa786 | RangeMaxFlag,
            0xa78b, 0xa78d, 0xa790, 0xa792,
            0xa796 | RangeMinFlag, 0xa7a8 | RangeMaxFlag,
            0xa7aa, 0xa7ab, 0xa7ac, 0xa7ad, 0xa7ae, 0xa7b0, 0xa7b1, 0xa7b2,
            0xa7b3,
            0xa7b4 | RangeMinFlag, 0xa7c2 | RangeMaxFlag,
            0xa7c4, 0xa7c5, 0xa7c6, 0xa7c7, 0xa7c9, 0xa7d0, 0xa7d6, 0xa7d8, 0xa7f5,
            0xab70 | RangeMinFlag, 0xabbf | RangeMaxFlag,
            0xff21 | RangeMinFlag, 0xff3a | RangeMaxFlag,
            0x10400 | RangeMinFlag, 0x10427 | RangeMaxFlag,
            0x104b0 | RangeMinFlag, 0x104d3 | RangeMaxFlag,
            0x10570 | RangeMinFlag, 0x1057a | RangeMaxFlag,
            0x1057c | RangeMinFlag, 0x1058a | RangeMaxFlag,
            0x1058c | RangeMinFlag, 0x10592 | RangeMaxFlag,
            0x10594, 0x10595,
            0x10c80 | RangeMinFlag, 0x10cb2 | RangeMaxFlag,
            0x118a0 | RangeMinFlag, 0x118bf | RangeMaxFlag,
            0x16e40 | RangeMinFlag, 0x16e5f | RangeMaxFlag,
            0x1e900 | RangeMinFlag, 0x1e921 | RangeMaxFlag,
        };

        private static readonly uint[] FoldMap1Data =
        {
            0x0061, 0x007a, 0x03bc, 0x00e0, 0x00f6, 0x00f8, 0x00fe, 0x0101, 0x012f, 0x0133, 0x0137, 0x013a, 0x0148,
            0x014b, 0x0177, 0x00ff, 0x017a, 0x017e, 0x0073, 0x0253, 0x0183, 0x0185, 0x0254, 0x0188, 0x0256, 0x0257,
            0x018c, 0x01dd, 0x0259, 0x025b, 0x0192, 0x0260, 0x0263, 0x0269, 0x0268, 0x0199, 0x026f, 0x0272, 0x0275,
            0x01a1, 0x01a5, 0x0280, 0x01a8, 0x0283, 0x01ad, 0x0288, 0x01b0, 0x028a, 0x028b, 0x01b4, 0x01b6, 0x0292,
            0x01b9, 0x01bd, 0x01c6, 0x01c6, 0x01c9, 0x01c9, 0x01cc, 0x01cc, 0x01dc, 0x01df, 0x01ef, 0x01f3, 0x01f3,
            0x01f5, 0x0195, 0x01bf, 0x01f9, 0x021f, 0x019e, 0x0223, 0x0233, 0x2c65, 0x023c, 0x019a, 0x2c66, 0x0242,
            0x0180, 0x0289, 0x028c, 0x0247, 0x024f, 0x03b9, 0x0371, 0x0373, 0x0377, 0x03f3, 0x03ac, 0x03ad, 0x03af,
            0x03cc, 0x03cd, 0x03ce, 0x03b1, 0x03c1, 0x03c3, 0x03cb, 0x03c3, 0x03d7, 0x03b2, 0x03b8, 0x03c6, 0x03c0,
            0x03d9, 0x03ef, 0x03ba, 0x03c1, 0x03b8, 0x03b5, 0x03f8, 0x03f2, 0x03fb, 0x037b, 0x037d, 0x0450, 0x045f,
            0x0430, 0x044f, 0x0461, 0x0481, 0x048b, 0x04bf, 0x04cf, 0x04c2, 0x04ce, 0x04d1, 0x052f, 0x0561, 0x0586,
            0x2d00, 0x2d25, 0x2d27, 0x2d2d, 0x13f0, 0x13f5, 0x0432, 0x0434, 0x043e, 0x0441, 0x0442, 0x0442, 0x044a,
            0x0463, 0xa64b, 0x10d0, 0x10fa, 0x10fd, 0x10ff, 0x1e01, 0x1e95, 0x1e61, 0x1ea1, 0x1eff, 0x1f00, 0x1f07,
            0x1f10, 0x1f15, 0x1f20, 0x1f27, 0x1f30, 0x1f37, 0x1f40, 0x1f45, 0x1f51, 0x1f53, 0x1f55, 0x1f57, 0x1f60,
            0x1f67, 0x1fb0, 0x1fb1, 0x1f70, 0x1f71, 0x03b9, 0x1f72, 0x1f75, 0x1fd0, 0x1fd1, 0x1f76, 0x1f77, 0x1fe0,
            0x1fe1, 0x1f7a, 0x1f7b, 0x1fe5, 0x1f78, 0x1f79, 0x1f7c, 0x1f7d, 0x03c9, 0x006b, 0x00e5, 0x214e, 0x2170,
            0x217f, 0x2184, 0x24d0, 0x24e9, 0x2c30, 0x2c5f, 0x2c61, 0x026b, 0x1d7d, 0x027d, 0x2c68, 0x2c6c, 0x0251,
            0x0271, 0x0250, 0x0252, 0x2c73, 0x2c76, 0x023f, 0x0240, 0x2c81, 0x2ce3, 0x2cec, 0x2cee, 0x2cf3, 0xa641,
            0xa66d, 0xa681, 0xa69b, 0xa723, 0xa72f, 0xa733, 0xa76f, 0xa77a, 0xa77c, 0x1d79, 0xa77f, 0xa787, 0xa78c,
            0x0265, 0xa791, 0xa793, 0xa797, 0xa7a9, 0x0266, 0x025c, 0x0261, 0x026c, 0x026a, 0x029e, 0x0287, 0x029d,
            0xab53, 0xa7b5, 0xa7c3, 0xa794, 0x0282, 0x1d8e, 0xa7c8, 0xa7ca, 0xa7d1, 0xa7d7, 0xa7d9, 0xa7f6, 0x13a0,
            0x13ef, 0xff41, 0xff5a, 0x10428, 0x1044f, 0x104d8, 0x104fb, 0x10597, 0x105a1, 0x105a3, 0x105b1, 0x105b3,
            0x105b9, 0x105bb, 0x105bc, 0x10cc0, 0x10cf2, 0x118c0, 0x118df, 0x16e60, 0x16e7f, 0x1e922, 0x1e943,
        };

        private static readonly uint[] FoldMap2 =
        {
            0x00df, 0x0130, 0x0149, 0x01f0, 0x0587, 0x1e96, 0x1e97, 0x1e98, 0x1e99,
            0x1e9a, 0x1e9e, 0x1f50,
            0x1f80 | RangeMinFlag, 0x1f87 | RangeMaxFlag,
            0x1f88 | RangeMinFlag, 0x1f8f | RangeMaxFlag,
            0x1f90 | RangeMinFlag, 0x1f97 | RangeMaxFlag,
            0x1f98 | RangeMinFlag, 0x1f9f | RangeMaxFlag,
            0x1fa0 | RangeMinFlag, 0x1fa7 | RangeMaxFlag,
            0x1fa8 | RangeMinFlag, 0x1faf | RangeMaxFlag,
            0x1fb2, 0x1fb3, 0x1fb4, 0x1fb6, 0x1fbc, 0x1fc2,
            0x1fc3, 0x1fc4, 0x1fc6, 0x1fcc, 0x1fd6, 0x1fe4, 0x1fe6, 0x1ff2, 0x1ff3,
            0x1ff4, 0x1ff6, 0x1ffc, 0xfb00, 0xfb01, 0xfb02, 0xfb05, 0xfb06, 0xfb13,
            0xfb14, 0xfb15, 0xfb16, 0xfb17,
        };

        private static readonly uint[] FoldMap2Data =
        {
            0x0073, 0x0073, 0x0069, 0x0307, 0x02bc, 0x006e, 0x006a, 0x030c, 0x0565, 0x0582, 0x0068, 0x0331, 0x0074, 0x0308,
            0x0077, 0x030a, 0x0079, 0x030a, 0x0061, 0x02be, 0x0073, 0x0073, 0x03c5, 0x0313, 0x1f00, 0x03b9, 0x1f07, 0x03b9,
            0x1f00, 0x03b9, 0x1f07, 0x03b9, 0x1f20, 0x03b9, 0x1f27, 0x03b9, 0x1f20, 0x03b9, 0x1f27, 0x03b9, 0x1f60, 0x03b9,
            0x1f67, 0x03b9, 0x1f60, 0x03b9, 0x1f67, 0x03b9, 0x1f70, 0x03b9, 0x03b1, 0x03b9, 0x03ac, 0x03b9, 0x03b1, 0x0342,
            0x03b1, 0x03b9, 0x1f74, 0x03b9, 0x03b7, 0x03b9, 0x03ae, 0x03b9, 0x03b7, 0x0342, 0x03b7, 0x03b9, 0x03b9, 0x0342,
            0x03c1, 0x0313, 0x03c5, 0x0342, 0x1f7c, 0x03b9, 0x03c9, 0x03b9, 0x03ce, 0x03b9, 0x03c9, 0x0342, 0x03c9, 0x03b9,
            0x0066, 0x0066, 0x0066, 0x0069, 0x0066, 0x006c, 0x0073, 0x0074, 0x0073, 0x0074, 0x0574, 0x0576, 0x0574, 0x0565,
            0x0574, 0x056b, 0x057e, 0x0576, 0x0574, 0x056d,
        };

        private static readonly uint[] FoldMap3 =
        {
            0x0390, 0x03b0, 0x1f52, 0x1f54, 0x1f56, 0x1fb7, 0x1fc7, 0x1fd2, 0x1fd3,
            0x1fd7, 0x1fe2, 0x1fe3, 0x1fe7, 0x1ff7, 0xfb03, 0xfb04,
        };

        private static readonly uint[] FoldMap3Data =
        {
            0x03b9, 0x0308, 0x0301, 0x03c5, 0x0308, 0x0301, 0x03c5, 0x0313, 0x0300, 0x03c5, 0x0313, 0x0301,
            0x03c5, 0x0313, 0x0342, 0x03b1, 0x0342, 0x03b9, 0x03b7, 0x0342, 0x03b9, 0x03b9, 0x0308, 0x0300,
            0x03b9, 0x0308, 0x0301, 0x03b9, 0x0308, 0x0342, 0x03c5, 0x0308, 0x0300, 0x03c5, 0x0308, 0x0301,
            0x03c5, 0x0308, 0x0342, 0x03c9, 0x0342, 0x03b9, 0x0066, 0x0066, 0x0069, 0x0066, 0x0066, 0x006c,
        };

        // Combined fold map list entries: map, data, nCodepoints.
        private struct FoldMapEntry
        {
            public uint[] Map;
            public uint[] Data;
            public int NCodepoints;
        }

        private static readonly FoldMapEntry[] FoldMapList =
        {
            new FoldMapEntry { Map = FoldMap1, Data = FoldMap1Data, NCodepoints = 1 },
            new FoldMapEntry { Map = FoldMap2, Data = FoldMap2Data, NCodepoints = 2 },
            new FoldMapEntry { Map = FoldMap3, Data = FoldMap3Data, NCodepoints = 3 },
        };

        public static void GetUnicodeFoldInfo(uint codepoint, ref UnicodeFoldInfo info)
        {
            if (info.Codepoints == null)
                info.Codepoints = new uint[3];

            // Fast path for ASCII characters.
            if (codepoint <= 0x7f)
            {
                info.Codepoints[0] = codepoint;
                if (codepoint >= 'A' && codepoint <= 'Z')
                    info.Codepoints[0] += 'a' - 'A';
                info.Count = 1;
                return;
            }

            // Try to locate the codepoint in any of the maps.
            for (int i = 0; i < FoldMapList.Length; i++)
            {
                int index = UnicodeBsearch(codepoint, FoldMapList[i].Map);
                if (index >= 0)
                {
                    // Found the mapping.
                    int nCodepoints = FoldMapList[i].NCodepoints;
                    uint[] map = FoldMapList[i].Map;
                    int dataOffset = index * nCodepoints;

                    for (int j = 0; j < nCodepoints; j++)
                        info.Codepoints[j] = FoldMapList[i].Data[dataOffset + j];
                    info.Count = nCodepoints;

                    if (map[index] != codepoint)
                    {
                        // The found mapping maps whole range of codepoints,
                        // i.e. we have to offset info.Codepoints[0] accordingly.
                        uint mapEntry = map[index] & CodepointMask;
                        if (mapEntry + 1 == FoldMapList[i].Data[dataOffset])
                        {
                            // Alternating type of the range.
                            info.Codepoints[0] = codepoint + ((codepoint & 0x1) == (map[index] & 0x1) ? 1u : 0u);
                        }
                        else
                        {
                            // Range to range kind of mapping.
                            info.Codepoints[0] += codepoint - mapEntry;
                        }
                    }

                    return;
                }
            }

            // No mapping found. Map the codepoint to itself.
            info.Codepoints[0] = codepoint;
            info.Count = 1;
        }

        // ---------------------------------------------------------------
        // Character classification helpers (work on C# chars / UTF-16)
        // ---------------------------------------------------------------

        public static bool IsAscii(char ch) => ch <= 127;

        public static bool IsBlank(char ch) => ch == ' ' || ch == '\t';

        public static bool IsNewline(char ch) => ch == '\r' || ch == '\n';

        public static bool IsWhitespace(char ch) => IsBlank(ch) || ch == '\v' || ch == '\f';

        public static bool IsCntrl(char ch) => ch <= 31 || ch == 127;

        public static bool IsPunct(char ch) =>
            (ch >= 33 && ch <= 47) ||
            (ch >= 58 && ch <= 64) ||
            (ch >= 91 && ch <= 96) ||
            (ch >= 123 && ch <= 126);

        public static bool IsUpper(char ch) => ch >= 'A' && ch <= 'Z';

        public static bool IsLower(char ch) => ch >= 'a' && ch <= 'z';

        public static bool IsAlpha(char ch) => IsUpper(ch) || IsLower(ch);

        public static bool IsDigit(char ch) => ch >= '0' && ch <= '9';

        public static bool IsXDigit(char ch) =>
            IsDigit(ch) || (ch >= 'A' && ch <= 'F') || (ch >= 'a' && ch <= 'f');

        public static bool IsAlNum(char ch) => IsAlpha(ch) || IsDigit(ch);

        public static bool IsUnicodeWhitespace(uint codepoint) => IsUnicodeWhitespaceImpl(codepoint);

        public static bool IsUnicodePunct(uint codepoint) => IsUnicodePunctImpl(codepoint);

        // ---------------------------------------------------------------
        // UTF-16 decode helpers (C# strings are UTF-16)
        // ---------------------------------------------------------------

        /// <summary>
        /// Decodes a Unicode codepoint at the given offset in a string.
        /// Handles surrogate pairs. Returns the codepoint and sets charSize
        /// to the number of chars consumed (1 or 2).
        /// </summary>
        public static uint DecodeUnicode(string str, int off, int size, out int charSize)
        {
            if (off < size && char.IsHighSurrogate(str[off]))
            {
                if (off + 1 < size && char.IsLowSurrogate(str[off + 1]))
                {
                    charSize = 2;
                    return (uint)(0x10000 + (((str[off] & 0x3ff) << 10) | (str[off + 1] & 0x3ff)));
                }
            }

            charSize = 1;
            return off < size ? (uint)str[off] : 0u;
        }

        /// <summary>
        /// Decodes the Unicode codepoint immediately before the given offset.
        /// Handles surrogate pairs.
        /// </summary>
        public static uint DecodeUnicodeBefore(string str, int off)
        {
            if (off >= 2 && char.IsHighSurrogate(str[off - 2]) && char.IsLowSurrogate(str[off - 1]))
                return (uint)(0x10000 + (((str[off - 2] & 0x3ff) << 10) | (str[off - 1] & 0x3ff)));

            if (off >= 1)
                return str[off - 1];

            return 0;
        }
    }
}
