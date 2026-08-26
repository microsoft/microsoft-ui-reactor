using System.Xml.Linq;
using Microsoft.UI.Reactor.Cli.Loc;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Localization;

/// <summary>
/// Mock provider that returns predictable translations for testing.
/// </summary>
internal sealed class MockTranslationProvider : ITranslationProvider
{
    public string Name => "Mock";

    private readonly Func<TranslationBatch, TranslationResult> _translate;

    public MockTranslationProvider(Func<TranslationBatch, TranslationResult>? translate = null)
    {
        _translate = translate ?? DefaultTranslate;
    }

    public Task<TranslationResult> TranslateAsync(TranslationBatch batch, CancellationToken ct = default)
    {
        return Task.FromResult(_translate(batch));
    }

    private static TranslationResult DefaultTranslate(TranslationBatch batch)
    {
        var translations = new Dictionary<string, string>();
        foreach (var entry in batch.Entries)
        {
            translations[entry.Key] = $"[{batch.TargetLocale}] {entry.Value}";
        }
        return new TranslationResult { Translations = translations };
    }
}

public class TranslationPromptTests
{
    [Fact]
    public void BuildSystemPrompt_ContainsIcuInstructions()
    {
        var prompt = TranslationPrompt.BuildSystemPrompt("en-US", "fr-FR");

        Assert.Contains("ICU Message Format", prompt);
        Assert.Contains("{variableName}", prompt);
        Assert.Contains("plural", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_ContainsLocaleHint_French()
    {
        var prompt = TranslationPrompt.BuildSystemPrompt("en-US", "fr-FR");

        Assert.Contains("vous", prompt);
        Assert.Contains("French punctuation", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_ContainsLocaleHint_Japanese()
    {
        var prompt = TranslationPrompt.BuildSystemPrompt("en-US", "ja-JP");

        Assert.Contains("polite form", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_ContainsLocaleHint_Arabic()
    {
        var prompt = TranslationPrompt.BuildSystemPrompt("en-US", "ar-SA");

        Assert.Contains("RTL", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_NoHint_ForUnknownLocale()
    {
        var prompt = TranslationPrompt.BuildSystemPrompt("en-US", "xx-XX");

        Assert.DoesNotContain("LOCALE-SPECIFIC", prompt);
    }

    [Fact]
    public void BuildUserMessage_ContainsEntries()
    {
        var batch = new TranslationBatch
        {
            SourceLocale = "en-US",
            TargetLocale = "fr-FR",
            Entries =
            [
                new TranslationEntry { Key = "Save", Value = "Save" },
                new TranslationEntry { Key = "Cancel", Value = "Cancel" },
            ],
        };

        var message = TranslationPrompt.BuildUserMessage(batch);

        Assert.Contains("Save=Save", message);
        Assert.Contains("Cancel=Cancel", message);
    }

    [Fact]
    public void BuildUserMessage_IncludesExistingTranslations()
    {
        var batch = new TranslationBatch
        {
            SourceLocale = "en-US",
            TargetLocale = "fr-FR",
            Entries = [new TranslationEntry { Key = "New", Value = "New" }],
            ExistingTranslations = new() { ["Save"] = "Enregistrer" },
        };

        var message = TranslationPrompt.BuildUserMessage(batch);

        Assert.Contains("consistency context", message);
        Assert.Contains("Save=Enregistrer", message);
    }

    [Fact]
    public void ParseResponse_SimpleKeyValue()
    {
        var response = "Save=Enregistrer\nCancel=Annuler";
        var result = TranslationPrompt.ParseResponse(response, ["Save", "Cancel"]);

        Assert.Equal("Enregistrer", result["Save"]);
        Assert.Equal("Annuler", result["Cancel"]);
    }

    [Fact]
    public void ParseResponse_IgnoresUnexpectedKeys()
    {
        var response = "Save=Enregistrer\nExtra=Bonus";
        var result = TranslationPrompt.ParseResponse(response, ["Save"]);

        Assert.Single(result);
        Assert.Equal("Enregistrer", result["Save"]);
    }

    [Fact]
    public void ParseResponse_HandlesIcuValues()
    {
        var response = "ItemCount={count, plural, one {# article} other {# articles}}";
        var result = TranslationPrompt.ParseResponse(response, ["ItemCount"]);

        Assert.Equal("{count, plural, one {# article} other {# articles}}", result["ItemCount"]);
    }

    [Fact]
    public void ParseResponse_SkipsEmptyLines()
    {
        var response = "\nSave=Enregistrer\n\n\nCancel=Annuler\n";
        var result = TranslationPrompt.ParseResponse(response, ["Save", "Cancel"]);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParseResponse_PreservesValueWithEquals()
    {
        // Values can contain = signs
        var response = "Formula=2+2=4";
        var result = TranslationPrompt.ParseResponse(response, ["Formula"]);

        Assert.Equal("2+2=4", result["Formula"]);
    }
}

public class TranslationProviderTests
{
    [Fact]
    public async Task MockProvider_TranslatesAllEntries()
    {
        var provider = new MockTranslationProvider();

        var batch = new TranslationBatch
        {
            SourceLocale = "en-US",
            TargetLocale = "fr-FR",
            Entries =
            [
                new TranslationEntry { Key = "Save", Value = "Save" },
                new TranslationEntry { Key = "Cancel", Value = "Cancel" },
            ],
        };

        var result = await provider.TranslateAsync(batch, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Translations.Count);
        Assert.Equal("[fr-FR] Save", result.Translations["Save"]);
        Assert.Equal("[fr-FR] Cancel", result.Translations["Cancel"]);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task MockProvider_WritesToReswCorrectly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"reactor-translate-{Guid.NewGuid():N}");
        try
        {
            var enUsDir = Path.Combine(tempDir, "Strings", "en-US");
            var frFrDir = Path.Combine(tempDir, "Strings", "fr-FR");
            Directory.CreateDirectory(enUsDir);
            Directory.CreateDirectory(frFrDir);

            // Write source .resw
            File.WriteAllText(Path.Combine(enUsDir, "Common.resw"), """
                <?xml version="1.0" encoding="utf-8"?>
                <root>
                  <data name="Save" xml:space="preserve"><value>Save</value></data>
                  <data name="Cancel" xml:space="preserve"><value>Cancel</value></data>
                </root>
                """);

            // Run translate command manually using the mock provider
            var sourceFiles = ReswReader.ReadLocale(enUsDir);
            var provider = new MockTranslationProvider();

            foreach (var sourceFile in sourceFiles)
            {
                var batch = new TranslationBatch
                {
                    SourceLocale = "en-US",
                    TargetLocale = "fr-FR",
                    Entries = sourceFile.Entries.Select(e => new TranslationEntry
                    {
                        Key = e.Key,
                        Value = e.Value,
                    }).ToList(),
                };

                var result = await provider.TranslateAsync(batch, TestContext.Current.CancellationToken);

                // Write to .resw with pending-review markers
                var doc = new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    new XElement("root"));

                foreach (var (key, value) in result.Translations)
                {
                    doc.Root!.Add(new XElement("data",
                        new XAttribute("name", key),
                        new XAttribute(XNamespace.Xml + "space", "preserve"),
                        new XElement("value", value),
                        new XElement("comment", "ai-translated: pending-review")));
                }

                doc.Save(Path.Combine(frFrDir, $"{sourceFile.Namespace}.resw"));
            }

            // Verify output
            var frFiles = ReswReader.ReadLocale(frFrDir);
            Assert.Single(frFiles);
            Assert.Equal(2, frFiles[0].Entries.Count);
            Assert.All(frFiles[0].Entries, e => Assert.True(e.IsAiDraft));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
