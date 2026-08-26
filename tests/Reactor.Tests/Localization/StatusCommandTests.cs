using Microsoft.UI.Reactor.Cli.Loc;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Localization;

[Collection("ConsoleTests")]
public class StatusCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _stringsDir;

    public StatusCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"reactor-status-{Guid.NewGuid():N}");
        _stringsDir = Path.Combine(_tempDir, "Strings");
        Directory.CreateDirectory(_stringsDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void WriteResw(string locale, string ns, string content)
    {
        var dir = Path.Combine(_stringsDir, locale);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{ns}.resw"), content);
    }

    private static string Resw(params (string key, string value, string? comment)[] entries)
    {
        var data = string.Join("\n", entries.Select(e =>
        {
            var commentXml = e.comment != null ? $"<comment>{e.comment}</comment>" : "";
            return $"  <data name=\"{e.key}\" xml:space=\"preserve\"><value>{e.value}</value>{commentXml}</data>";
        }));
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <root>
            {data}
            </root>
            """;
    }

    [Fact]
    public void Status_SourceLocale_Shows100Percent()
    {
        WriteResw("en-US", "Common", Resw(
            ("Save", "Save", null),
            ("Cancel", "Cancel", null)));

        var output = CaptureStdout(() =>
            StatusCommand.Run(["--resources", _stringsDir]));

        Assert.Contains("en-US", output);
        Assert.Contains("100.0%", output);
    }

    [Fact]
    public void Status_PartialTranslation_ShowsCorrectCoverage()
    {
        WriteResw("en-US", "Common", Resw(
            ("Save", "Save", null),
            ("Cancel", "Cancel", null),
            ("Delete", "Delete", null)));

        WriteResw("fr-FR", "Common", Resw(
            ("Save", "Enregistrer", null),
            ("Cancel", "Annuler", null)));

        var output = CaptureStdout(() =>
            StatusCommand.Run(["--resources", _stringsDir]));

        // fr-FR has 2/3 = 66.7%
        Assert.Contains("fr-FR", output);
        Assert.Contains("66.7%", output);
    }

    [Fact]
    public void Status_AiDraftKeys_CountedSeparately()
    {
        WriteResw("en-US", "Common", Resw(
            ("Save", "Save", null),
            ("Cancel", "Cancel", null)));

        WriteResw("fr-FR", "Common", Resw(
            ("Save", "Enregistrer", null),
            ("Cancel", "Annuler", "ai-translated: pending-review")));

        var output = CaptureStdout(() =>
            StatusCommand.Run(["--resources", _stringsDir]));

        Assert.Contains("fr-FR", output);
        // 1 translated, 1 AI-draft, 0 missing = 100% coverage
        Assert.Contains("100.0%", output);
    }

    [Fact]
    public void Status_NoTargetLocale_ShowsOnlySource()
    {
        WriteResw("en-US", "Common", Resw(
            ("Save", "Save", null)));

        var output = CaptureStdout(() =>
            StatusCommand.Run(["--resources", _stringsDir]));

        Assert.Contains("en-US", output);
        Assert.Contains("100.0%", output);
    }

    [Fact]
    public void Status_MultipleNamespaces_CountsAllKeys()
    {
        WriteResw("en-US", "Common", Resw(("Save", "Save", null)));
        WriteResw("en-US", "Settings", Resw(("Theme", "Theme", null)));
        WriteResw("ja-JP", "Common", Resw(("Save", "保存", null)));
        // ja-JP missing Settings/Theme

        var output = CaptureStdout(() =>
            StatusCommand.Run(["--resources", _stringsDir]));

        Assert.Contains("ja-JP", output);
        Assert.Contains("50.0%", output);
    }

    private static string CaptureStdout(Action action)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        var outWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(new StringWriter()); // suppress errors
        try
        {
            action();
            return outWriter.ToString();
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }
}
