using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace Microsoft.UI.Reactor.Localization.Generator;

/// <summary>
/// Represents a single string entry from a .resw file.
/// </summary>
internal sealed class ReswEntry
{
    public string Key { get; }
    public string Value { get; }
    public string? Comment { get; }

    public ReswEntry(string key, string value, string? comment)
    {
        Key = key;
        Value = value;
        Comment = comment;
    }
}

/// <summary>
/// Parses .resw XML files into structured data.
/// </summary>
internal static class ReswParser
{
    /// <summary>
    /// Parses a .resw file's XML content and returns its entries.
    /// </summary>
    public static List<ReswEntry> Parse(string xmlContent)
    {
        var entries = new List<ReswEntry>();
        var doc = new XmlDocument { XmlResolver = null };
        // SECURITY: prohibit DTD processing and external entities to block billion-laughs
        // and XXE on attacker-controlled .resw inputs at build time.
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            // .resw files are bounded by repo realities; 16 MiB is more than enough.
            MaxCharactersInDocument = 16 * 1024 * 1024,
        };
        using (var stringReader = new StringReader(xmlContent))
        using (var xmlReader = XmlReader.Create(stringReader, settings))
        {
            doc.Load(xmlReader);
        }

        var dataNodes = doc.SelectNodes("/root/data");
        if (dataNodes == null) return entries;

        foreach (XmlNode node in dataNodes)
        {
            var name = node.Attributes?["name"]?.Value;
            if (name == null) continue;

            var valueNode = node.SelectSingleNode("value");
            var commentNode = node.SelectSingleNode("comment");

            var value = valueNode?.InnerText ?? "";
            var comment = commentNode?.InnerText;

            entries.Add(new ReswEntry(name, value, comment));
        }

        return entries;
    }

    /// <summary>
    /// Determines if this is a flat layout (single Resources.resw) or multi-file layout.
    /// In flat layout, we don't nest under a "Resources" class.
    /// </summary>
    public static bool IsFlatLayout(IReadOnlyList<string> reswFileNames)
    {
        return reswFileNames.Count == 1 &&
               string.Equals(reswFileNames[0], "Resources", System.StringComparison.OrdinalIgnoreCase);
    }
}
