using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Elements;

/// <summary>
/// Spec 039 Phase 11.2 — naming-alignment guard.
///
/// The §0.3 list of factories whose names diverge from their WinUI target
/// (Reactor-original convenience wrappers, or shorter alternative spellings)
/// are required to carry an XML <c>&lt;remarks&gt;</c> element explaining the
/// deviation. The remarks were added in Phase 6.2.
///
/// This test loads the doc-comment XML emitted next to <c>Reactor.dll</c> and
/// asserts each named factory has a non-empty <c>&lt;remarks&gt;</c> entry.
/// Failures are collected so adding/renaming several factories surfaces every
/// missing remark in one shot.
/// </summary>
public class NamingAlignmentGuardTests
{
    /// <summary>
    /// Factories whose names diverge from their WinUI target and therefore
    /// require an XML &lt;remarks&gt; with the rationale (spec §0.3).
    ///
    /// Each entry is the doc-comment <c>cref</c>-style ID prefix; we match
    /// against any <c>&lt;member name="M:Microsoft.UI.Reactor.Factories.{Name}(...)"&gt;</c>
    /// so all overloads share the requirement.
    /// </summary>
    private static readonly string[] DivergentFactoryNames =
    {
        "VStack",
        "HStack",
        "Heading",
        "SubHeading",
        "Caption",
        "Flex",
        "FlexRow",
        "FlexColumn",
        "LazyVStack",
        "LazyHStack",
        // The generic peer of WinUI ListView; the divergence is the generic
        // element record name (TemplatedListViewElement<T>) being reached via
        // a short `ListView<T>` factory — only the generic overload diverges,
        // so we match its open-generic doc-ID below.
        "ListView",
    };

    [Fact]
    public void DivergentFactoriesHaveRemarks()
    {
        var docXml = LoadDocXml();
        var failures = new List<string>();

        foreach (var name in DivergentFactoryNames)
        {
            // Match any Factories method whose simple name matches. For ListView
            // we only require the generic overload (the open-generic `ListView<T>`)
            // because the untyped ListView matches WinUI's spelling 1:1.
            var members = docXml.Root!
                .Element("members")!
                .Elements("member")
                .Where(m => IsFactoryOverload(m.Attribute("name")?.Value, name))
                .ToList();

            if (members.Count == 0)
            {
                failures.Add($"`{name}`: no doc-comment members found for any overload — " +
                             "factory missing or doc XML not regenerated.");
                continue;
            }

            // Require at least one overload to carry a non-empty <remarks>.
            // Convenience overloads (e.g. VStack(double spacing, params)) reference
            // back to the canonical overload via `see cref` rather than restating
            // the rationale; that's intentional.
            var withRemarks = members.Where(HasNonEmptyRemarks).ToList();
            if (withRemarks.Count == 0)
            {
                failures.Add(
                    $"`{name}`: {members.Count} overload(s) but none carry a non-empty <remarks> " +
                    "element explaining the WinUI deviation (spec §0.3 / Phase 6.2).");
            }
        }

        if (failures.Count > 0)
        {
            var msg = new StringBuilder();
            msg.AppendLine($"Phase 11.2 naming-alignment guard: {failures.Count} factory(ies) missing required <remarks>.");
            msg.AppendLine();
            foreach (var f in failures)
                msg.AppendLine("  - " + f);
            msg.AppendLine();
            msg.AppendLine("Fix: add an XML <remarks> to at least one overload of each factory");
            msg.AppendLine("noting the rationale for the divergence from WinUI's name. See");
            msg.AppendLine("Dsl.cs:VStack/HStack/Flex for the established pattern.");
            Assert.Fail(msg.ToString());
        }
    }

    /// <summary>
    /// Matches doc-comment IDs of the form
    /// <c>M:Microsoft.UI.Reactor.Factories.{Name}({sig})</c> or
    /// <c>M:Microsoft.UI.Reactor.Factories.{Name}\`1({sig})</c> for the
    /// generic-overload case. The simple-name match is anchored so
    /// <c>VStack</c> doesn't shadow some hypothetical <c>VStackInner</c>.
    /// </summary>
    private static bool IsFactoryOverload(string? id, string simpleName)
    {
        if (id is null) return false;
        // Factories is the static partial class hosting the DSL factories.
        const string prefix = "M:Microsoft.UI.Reactor.Factories.";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)) return false;

        var rest = id[prefix.Length..];
        // rest looks like: "VStack(System.Element[])"
        //          or:     "ListView`1(...)"
        var openParen = rest.IndexOf('(');
        if (openParen < 0) return false;
        var nameSig = rest[..openParen];

        // Strip generic-arity suffix `n
        var tick = nameSig.IndexOf('`');
        var bareName = tick >= 0 ? nameSig[..tick] : nameSig;

        if (!string.Equals(bareName, simpleName, StringComparison.Ordinal)) return false;

        // For ListView we ONLY want the generic overload (where the divergence lives).
        // The non-generic overloads use the WinUI-matching spelling and need no remark.
        if (simpleName == "ListView" && tick < 0) return false;

        return true;
    }

    private static bool HasNonEmptyRemarks(XElement memberEl)
    {
        var remarks = memberEl.Element("remarks");
        if (remarks is null) return false;
        var text = string.Concat(remarks.DescendantNodes()
            .OfType<XText>()
            .Select(t => t.Value)).Trim();
        return text.Length > 0;
    }

    private static XDocument LoadDocXml()
    {
        // The doc XML is emitted alongside Reactor.dll by GenerateDocumentationFile.
        var reactorAsm = typeof(Element).Assembly;
        var dllPath = reactorAsm.Location;
        Assert.False(string.IsNullOrEmpty(dllPath),
            "Reactor assembly has no on-disk Location — doc-XML lookup needs a file path.");

        var xmlPath = Path.ChangeExtension(dllPath, ".xml");
        if (!File.Exists(xmlPath))
            throw new FileNotFoundException(
                $"Reactor doc-comment XML not found next to Reactor.dll. " +
                $"Expected: {xmlPath}. Ensure <GenerateDocumentationFile> is enabled.");

        return XDocument.Load(xmlPath);
    }
}
