using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Microsoft.UI.Reactor.Cli.Loc;

/// <summary>
/// Reads and writes .resw (XML resource) files.
/// Idempotent: preserves existing keys/values/comments, only adds new entries.
/// Keys are kept in alphabetical order.
/// </summary>
internal static class ReswWriter
{
    /// <summary>
    /// Loads all existing .resw entries from the output directory.
    /// Returns a dictionary keyed by (reswFileName, key).
    /// </summary>
    public static Dictionary<(string reswFileName, string key), string> LoadExisting(string outputDir)
    {
        var entries = new Dictionary<(string, string), string>();

        if (!Directory.Exists(outputDir)) return entries;

        foreach (var file in Directory.GetFiles(outputDir, "*.resw"))
        {
            var reswName = Path.GetFileNameWithoutExtension(file);
            try
            {
                var doc = XDocument.Load(file);
                var root = doc.Root;
                if (root == null) continue;

                foreach (var data in root.Elements("data"))
                {
                    var name = data.Attribute("name")?.Value;
                    var value = data.Element("value")?.Value;
                    if (name != null && value != null)
                    {
                        entries[(reswName, name)] = value;
                    }
                }
            }
            catch (XmlException)
            {
                // Skip malformed .resw files
            }
        }

        return entries;
    }

    /// <summary>
    /// Writes new entries to .resw files, preserving existing content.
    /// Groups entries by ReswFileName and writes one .resw per group.
    /// </summary>
    public static void Write(string outputDir, List<KeyedLocString> newEntries)
    {
        if (newEntries.Count == 0) return;

        Directory.CreateDirectory(outputDir);

        // Group new entries by .resw file name
        var groups = newEntries.GroupBy(e => e.ReswFileName);

        foreach (var group in groups)
        {
            var reswFileName = group.Key;
            var filePath = Path.Combine(outputDir, $"{reswFileName}.resw");

            // Load or create the XML document
            XDocument doc;
            XElement root;

            if (File.Exists(filePath))
            {
                doc = XDocument.Load(filePath);
                root = doc.Root!;
            }
            else
            {
                root = new XElement("root",
                    new XElement("resheader",
                        new XAttribute("name", "resmimetype"),
                        new XElement("value", "text/microsoft-resx")),
                    new XElement("resheader",
                        new XAttribute("name", "version"),
                        new XElement("value", "2.0")),
                    new XElement("resheader",
                        new XAttribute("name", "reader"),
                        new XElement("value", "System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")),
                    new XElement("resheader",
                        new XAttribute("name", "writer"),
                        new XElement("value", "System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"))
                );
                doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    root);
            }

            // Add new entries
            foreach (var entry in group)
            {
                // SECURITY (TASK-038): scrub XML-1.0-invalid control chars from
                // LLM output before they hit XElement; an unescaped \x00-\x08
                // would otherwise abort `doc.Save` and corrupt the batch.
                var dataElement = new XElement("data",
                    new XAttribute("name", XmlSafe(entry.Key)),
                    new XAttribute(XNamespace.Xml + "space", "preserve"),
                    new XElement("value", XmlSafe(entry.Value)));

                if (entry.Comment != null)
                {
                    dataElement.Add(new XElement("comment", XmlSafe(entry.Comment)));
                }

                root.Add(dataElement);
            }

            // Sort all <data> elements alphabetically by name
            var headers = root.Elements("resheader").ToList();
            var dataElements = root.Elements("data")
                .OrderBy(d => d.Attribute("name")?.Value, StringComparer.Ordinal)
                .ToList();

            root.RemoveNodes();
            foreach (var h in headers) root.Add(h);
            foreach (var d in dataElements) root.Add(d);

            try
            {
                doc.Save(filePath);
            }
            catch (ArgumentException)
            {
                // Belt-and-braces: if anything still slipped through XmlSafe
                // and XElement rejects it, surface a single warning rather
                // than aborting the whole batch.
                Console.Error.WriteLine($"  Warning: skipped malformed XML save for '{filePath}'");
            }
        }
    }

    /// <summary>
    /// Strips XML-1.0-invalid control characters from <paramref name="value"/>.
    /// Tab, LF, CR are kept; everything else in 0x00-0x1F and 0x7F-0x9F is
    /// removed; surrogates and non-characters are preserved (XML 1.0 §2.2).
    /// TASK-038.
    /// </summary>
    internal static string XmlSafe(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (c == '\t' || c == '\n' || c == '\r') { sb.Append(c); continue; }
            if (c < 0x20) continue;
            if (c == 0x7F) continue;
            if (c >= 0x80 && c <= 0x9F) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
