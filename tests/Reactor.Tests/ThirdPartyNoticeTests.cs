using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Each NuGet package packs its own ThirdPartyNoticeText.txt holding only the notices for the
/// code it ports (issue #1535). Those files are cut from the repo-root ThirdPartyNoticeText.txt,
/// so these tests keep them from drifting from it.
/// </summary>
public class ThirdPartyNoticeTests
{
    private static readonly string[] PackageNoticeFiles =
    [
        "src/Reactor/ThirdPartyNoticeText.txt",
        "src/Reactor.Advanced/ThirdPartyNoticeText.txt",
    ];

    private static readonly Regex SectionStart = new(@"^(?=-{19} \S)", RegexOptions.Multiline);
    private static readonly Regex SectionName = new(@"^-+ (\S+)");

    [Fact]
    public void PackageNotices_AreVerbatimSectionsOfTheRootNotice()
    {
        var (rootHeader, rootSections) = Parse(Read("ThirdPartyNoticeText.txt"));

        foreach (var file in PackageNoticeFiles)
        {
            var (header, sections) = Parse(Read(file));
            Assert.Equal(rootHeader, header);
            Assert.NotEmpty(sections);
            foreach (var (name, text) in sections)
            {
                Assert.True(rootSections.TryGetValue(name, out var rootText), $"{file} has a '{name}' section the root notice does not.");
                Assert.Equal(rootText, text);
            }
        }
    }

    [Fact]
    public void EveryRootNotice_ShipsInExactlyOnePackage()
    {
        var (_, rootSections) = Parse(Read("ThirdPartyNoticeText.txt"));
        var packaged = PackageNoticeFiles.SelectMany(file => Parse(Read(file)).Sections.Keys).ToList();

        Assert.Equal(packaged.Count, packaged.Distinct().Count());
        Assert.Equal(rootSections.Keys.Order(), packaged.Order());
    }

    private static string Read(string relativePath)
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        return File.ReadAllText(Path.Join(root, relativePath)).ReplaceLineEndings("\n");
    }

    private static (string Header, Dictionary<string, string> Sections) Parse(string text)
    {
        var parts = SectionStart.Split(text);
        var sections = parts.Skip(1).ToDictionary(
            part => SectionName.Match(part).Groups[1].Value,
            part => part.TrimEnd());
        return (parts[0], sections);
    }
}
