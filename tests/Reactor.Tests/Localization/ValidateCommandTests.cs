using Microsoft.UI.Reactor.Cli.Loc;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Localization;

[Collection("ConsoleTests")]
public class ValidateCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _stringsDir;

    public ValidateCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"reactor-validate-{Guid.NewGuid():N}");
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

    private static string Resw(params (string key, string value)[] entries)
    {
        var data = string.Join("\n", entries.Select(e =>
            $"  <data name=\"{e.key}\" xml:space=\"preserve\"><value>{e.value}</value></data>"));
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <root>
            {data}
            </root>
            """;
    }

    // --- ICU syntax validation ---

    [Fact]
    public void ValidateIcuSyntax_ValidSimple_ReturnsNull()
    {
        Assert.Null(ReswReader.ValidateIcuSyntax("Hello, {name}!"));
    }

    [Fact]
    public void ValidateIcuSyntax_ValidPlural_ReturnsNull()
    {
        Assert.Null(ReswReader.ValidateIcuSyntax("{count, plural, one {# item} other {# items}}"));
    }

    [Fact]
    public void ValidateIcuSyntax_UnmatchedOpen_ReturnsError()
    {
        var error = ReswReader.ValidateIcuSyntax("Hello {name");
        Assert.NotNull(error);
        Assert.Contains("unmatched opening brace", error);
    }

    [Fact]
    public void ValidateIcuSyntax_UnmatchedClose_ReturnsError()
    {
        var error = ReswReader.ValidateIcuSyntax("Hello name}");
        Assert.NotNull(error);
        Assert.Contains("unmatched closing brace", error);
    }

    [Fact]
    public void ValidateIcuSyntax_QuotedBraces_Valid()
    {
        Assert.Null(ReswReader.ValidateIcuSyntax("Use single quotes '{'like this'}'"));
    }

    // --- Parameter extraction ---

    [Fact]
    public void ExtractIcuParameters_Simple()
    {
        var result = ReswReader.ExtractIcuParameters("Hello, {name}! You have {count} items.");
        Assert.Equal(new HashSet<string> { "name", "count" }, result);
    }

    [Fact]
    public void ExtractIcuParameters_Plural()
    {
        var result = ReswReader.ExtractIcuParameters("{count, plural, one {# item} other {# items}}");
        Assert.Contains("count", result);
        Assert.DoesNotContain("#", result);
    }

    [Fact]
    public void ExtractIcuParameters_NoParams()
    {
        var result = ReswReader.ExtractIcuParameters("Hello, World!");
        Assert.Empty(result);
    }

    // --- Validate command: parameter consistency ---

    [Fact]
    public void Validate_ParameterMismatch_ReportsWarning()
    {
        WriteResw("en-US", "Common", Resw(("Greeting", "Hello, {name}!")));
        WriteResw("fr-FR", "Common", Resw(("Greeting", "Bonjour, {nom}!")));

        var (output, error) = CaptureOutput(() =>
            ValidateCommand.Run(["--resources", _stringsDir]));

        Assert.Contains("{name}", error);
        Assert.Contains("{nom}", error);
    }

    [Fact]
    public void Validate_AllValid_PassesClean()
    {
        WriteResw("en-US", "Common", Resw(("Save", "Save"), ("Cancel", "Cancel")));
        WriteResw("fr-FR", "Common", Resw(("Save", "Enregistrer"), ("Cancel", "Annuler")));

        var (output, _) = CaptureOutput(() =>
            ValidateCommand.Run(["--resources", _stringsDir]));

        Assert.Contains("no errors or warnings", output);
    }

    [Fact]
    public void Validate_MissingKey_ReportsWarning()
    {
        WriteResw("en-US", "Common", Resw(("Save", "Save"), ("NewFeature", "New Feature")));
        WriteResw("fr-FR", "Common", Resw(("Save", "Enregistrer")));

        var (_, error) = CaptureOutput(() =>
            ValidateCommand.Run(["--resources", _stringsDir]));

        Assert.Contains("missing key: NewFeature", error);
    }

    [Fact]
    public void Validate_BrokenIcu_ReportsError()
    {
        WriteResw("en-US", "Common", Resw(("Save", "Save")));
        WriteResw("fr-FR", "Common", Resw(("Save", "Enregistrer {oops")));

        var exitCode = 0;
        var (_, error) = CaptureOutput(() =>
            exitCode = ValidateCommand.Run(["--resources", _stringsDir]));

        Assert.Equal(1, exitCode);
        Assert.Contains("ICU parse error", error);
    }

    // --- Validate command: returns non-zero on errors ---

    [Fact]
    public void Validate_ErrorsPresent_ReturnsNonZero()
    {
        WriteResw("en-US", "Common", Resw(("Save", "Save")));
        WriteResw("fr-FR", "Common", Resw(("Save", "Test {bad")));

        int exitCode = 0;
        CaptureOutput(() => exitCode = ValidateCommand.Run(["--resources", _stringsDir]));

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Validate_WarningsOnly_ReturnsZero()
    {
        WriteResw("en-US", "Common", Resw(("Save", "Save"), ("Extra", "Extra")));
        WriteResw("fr-FR", "Common", Resw(("Save", "Enregistrer")));

        int exitCode = 0;
        CaptureOutput(() => exitCode = ValidateCommand.Run(["--resources", _stringsDir]));

        Assert.Equal(0, exitCode);
    }

    private static (string stdout, string stderr) CaptureOutput(Func<int> action)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try
        {
            action();
            return (outWriter.ToString(), errWriter.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }
}
