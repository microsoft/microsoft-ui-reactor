using System.Xml.Linq;
using Microsoft.UI.Reactor.Cli.Loc;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Localization;

/// <summary>
/// Integration tests that exercise the full extract pipeline:
/// scan → key naming → .resw generation → source rewrite.
/// </summary>
public class ExtractIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _outputDir;

    public ExtractIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"reactor-integ-{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_tempDir, "src");
        _outputDir = Path.Combine(_tempDir, "Strings", "en-US");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_outputDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void FullPipeline_ComponentFile_CorrectReswAndRewrite()
    {
        var sourceFile = Path.Combine(_sourceDir, "InboxPage.cs");
        var source = """
            using static Microsoft.UI.Reactor.Factories;

            class InboxPage : Component
            {
                public override Element Render()
                {
                    return VStack(
                        Heading("Inbox"),
                        TextBlock($"You have {messages.Count} messages"),
                        Button("Compose", OnCompose)
                    );
                }
            }
            """;
        File.WriteAllText(sourceFile, source);

        // Step 1: Scan
        var extractions = LocalizableStringScanner.Scan(source, sourceFile);
        Assert.Equal(3, extractions.Count);

        // Step 2: Assign keys
        var keyed = KeyNamer.AssignKeys(extractions);
        Assert.Equal(3, keyed.Count);
        Assert.All(keyed, k => Assert.Equal("Inbox", k.ReswFileName));

        // Step 3: Write .resw
        ReswWriter.Write(_outputDir, keyed);

        var reswFile = Path.Combine(_outputDir, "Inbox.resw");
        Assert.True(File.Exists(reswFile));

        var doc = XDocument.Load(reswFile);
        var dataElements = doc.Root!.Elements("data").ToList();
        Assert.Equal(3, dataElements.Count);

        // Verify the interpolation has ICU format
        var messageEntry = dataElements.FirstOrDefault(d =>
            d.Element("value")?.Value?.Contains("{count}") == true);
        Assert.NotNull(messageEntry);

        // Step 4: Rewrite source
        var rewriteCount = SourceRewriter.Rewrite(keyed);
        Assert.Equal(3, rewriteCount);

        var rewritten = File.ReadAllText(sourceFile);
        Assert.Contains("t.Message(Loc.Inbox.", rewritten);
        Assert.Contains("var t = UseIntl();", rewritten);
        Assert.DoesNotContain("\"Inbox\"", rewritten);
        Assert.DoesNotContain("\"Compose\"", rewritten);
    }

    [Fact]
    public void Idempotency_SecondExtractProducesNoChanges()
    {
        var sourceFile = Path.Combine(_sourceDir, "App.cs");
        var source = """
            class App : Component
            {
                public override Element Render()
                {
                    return TextBlock("Hello");
                }
            }
            """;
        File.WriteAllText(sourceFile, source);

        // First extract + rewrite
        var extractions1 = LocalizableStringScanner.Scan(source, sourceFile);
        var keyed1 = KeyNamer.AssignKeys(extractions1);
        ReswWriter.Write(_outputDir, keyed1);
        SourceRewriter.Rewrite(keyed1);

        // Second extract on the rewritten source
        var rewrittenSource = File.ReadAllText(sourceFile);
        var extractions2 = LocalizableStringScanner.Scan(rewrittenSource, sourceFile);

        // Should find no new bare strings (they've been wrapped in t.Message())
        Assert.Empty(extractions2);
    }

    [Fact]
    public void DryRun_ReturnsNonZero_WhenUnextractedStringsExist()
    {
        // This tests the logic of the extract command's dry-run behavior
        var sourceFile = Path.Combine(_sourceDir, "App.cs");
        var source = """
            class App : Component
            {
                public override Element Render()
                {
                    return TextBlock("Hello");
                }
            }
            """;
        File.WriteAllText(sourceFile, source);

        var extractions = LocalizableStringScanner.Scan(source, sourceFile);
        var keyed = KeyNamer.AssignKeys(extractions);
        var existing = ReswWriter.LoadExisting(_outputDir);

        // Count new entries (simulating dry-run check)
        int newCount = 0;
        foreach (var entry in keyed)
        {
            if (!existing.ContainsKey((entry.ReswFileName, entry.Key)))
                newCount++;
        }

        Assert.True(newCount > 0, "Should have unextracted strings");
    }

    [Fact]
    public void TernaryExpression_ExtractsBothBranches()
    {
        var sourceFile = Path.Combine(_sourceDir, "Toggle.cs");
        var source = """
            class ToggleComponent : Component
            {
                public override Element Render()
                {
                    return TextBlock(isVisible ? "Show" : "Hide");
                }
            }
            """;
        File.WriteAllText(sourceFile, source);

        var extractions = LocalizableStringScanner.Scan(source, sourceFile);

        Assert.Equal(2, extractions.Count);
        Assert.Contains(extractions, e => e.Value == "Show" && e.TernaryBranch == 0);
        Assert.Contains(extractions, e => e.Value == "Hide" && e.TernaryBranch == 1);

        var keyed = KeyNamer.AssignKeys(extractions);
        Assert.Equal(2, keyed.Count);
        // Keys should be distinct
        Assert.NotEqual(keyed[0].Key, keyed[1].Key);
    }

    [Fact]
    public void FormatSpecifiers_CorrectIcuConversions()
    {
        var sourceFile = Path.Combine(_sourceDir, "Report.cs");
        var source = """
            class ReportPage : Component
            {
                public override Element Render()
                {
                    return VStack(
                        TextBlock($"Total: {price:C}"),
                        TextBlock($"Due: {dueDate:D}"),
                        TextBlock($"Score: {pct:P0}")
                    );
                }
            }
            """;
        File.WriteAllText(sourceFile, source);

        var extractions = LocalizableStringScanner.Scan(source, sourceFile);

        Assert.Equal(3, extractions.Count);
        Assert.Contains(extractions, e => e.Value == "Total: {price, number, currency}");
        Assert.Contains(extractions, e => e.Value == "Due: {dueDate, date, long}");
        Assert.Contains(extractions, e => e.Value == "Score: {pct, number, percent}");
    }
}
