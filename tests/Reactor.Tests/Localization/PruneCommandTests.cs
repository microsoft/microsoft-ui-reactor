using System.Xml.Linq;
using Microsoft.UI.Reactor.Cli.Loc;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Localization;

[Collection("ConsoleTests")]
public class PruneCommandTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _stringsDir;
    private readonly string _enUsDir;

    public PruneCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"reactor-prune-{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_tempDir, "src");
        _stringsDir = Path.Combine(_tempDir, "Strings");
        _enUsDir = Path.Combine(_stringsDir, "en-US");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_enUsDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void WriteSource(string filename, string content)
    {
        File.WriteAllText(Path.Combine(_sourceDir, filename), content);
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

    // --- ScanSourceReferences ---

    [Fact]
    public void ScanSourceReferences_FindsNamespacedRefs()
    {
        WriteSource("Page.cs", """
            var t = UseIntl();
            t.Message(Loc.Common.Save);
            t.Message(Loc.Settings.Theme);
            """);

        var refs = PruneCommand.ScanSourceReferences(_sourceDir);

        Assert.Contains(("Common", "Save"), refs);
        Assert.Contains(("Settings", "Theme"), refs);
    }

    [Fact]
    public void ScanSourceReferences_NoRefs_ReturnsEmpty()
    {
        WriteSource("Page.cs", """
            var x = "Hello";
            Console.WriteLine(x);
            """);

        var refs = PruneCommand.ScanSourceReferences(_sourceDir);
        Assert.Empty(refs);
    }

    // --- Prune: unused key detected ---

    [Fact]
    public void Prune_UnusedKey_Detected()
    {
        WriteSource("Page.cs", "t.Message(Loc.Common.Save);");
        WriteResw("en-US", "Common", Resw(
            ("Save", "Save"),
            ("OldFeature", "Old Feature")));

        var output = CaptureStdout(() =>
            PruneCommand.Run(["--source", _sourceDir, "--resources", _enUsDir, "--dry-run"]));

        Assert.Contains("UNUSED: Common.OldFeature", output);
        Assert.DoesNotContain("Common.Save", output);
    }

    // --- Prune: referenced key preserved ---

    [Fact]
    public void Prune_ReferencedKey_Preserved()
    {
        WriteSource("Page.cs", """
            t.Message(Loc.Common.Save);
            t.Message(Loc.Common.Cancel);
            """);
        WriteResw("en-US", "Common", Resw(
            ("Save", "Save"),
            ("Cancel", "Cancel")));

        int exitCode = 0;
        var output = CaptureStdout(() =>
            exitCode = PruneCommand.Run(["--source", _sourceDir, "--resources", _enUsDir, "--dry-run"]));

        Assert.Contains("No unused keys", output);
    }

    // --- Prune: dry-run does not delete ---

    [Fact]
    public void Prune_DryRun_DoesNotDelete()
    {
        WriteSource("Page.cs", "t.Message(Loc.Common.Save);");
        WriteResw("en-US", "Common", Resw(("Save", "Save"), ("Unused", "Unused")));

        CaptureStdout(() =>
            PruneCommand.Run(["--source", _sourceDir, "--resources", _enUsDir, "--dry-run"]));

        // File should still have both keys
        var doc = XDocument.Load(Path.Combine(_enUsDir, "Common.resw"));
        var keys = doc.Root!.Elements("data").Select(d => d.Attribute("name")?.Value).ToList();
        Assert.Contains("Unused", keys);
    }

    // --- Prune: actual removal across locales ---

    [Fact]
    public void Prune_WithoutDryRun_RemovesFromAllLocales()
    {
        WriteSource("Page.cs", "t.Message(Loc.Common.Save);");
        WriteResw("en-US", "Common", Resw(("Save", "Save"), ("OldFeature", "Old Feature")));
        WriteResw("fr-FR", "Common", Resw(("Save", "Enregistrer"), ("OldFeature", "Ancienne fonctionnalité")));

        CaptureStdout(() =>
            PruneCommand.Run(["--source", _sourceDir, "--resources", _enUsDir]));

        // en-US should only have Save
        var enDoc = XDocument.Load(Path.Combine(_stringsDir, "en-US", "Common.resw"));
        var enKeys = enDoc.Root!.Elements("data").Select(d => d.Attribute("name")?.Value).ToList();
        Assert.Single(enKeys);
        Assert.Equal("Save", enKeys[0]);

        // fr-FR should also only have Save
        var frDoc = XDocument.Load(Path.Combine(_stringsDir, "fr-FR", "Common.resw"));
        var frKeys = frDoc.Root!.Elements("data").Select(d => d.Attribute("name")?.Value).ToList();
        Assert.Single(frKeys);
        Assert.Equal("Save", frKeys[0]);
    }

    // --- Prune: dry-run returns non-zero for CI ---

    [Fact]
    public void Prune_DryRun_ReturnsNonZeroWhenUnused()
    {
        WriteSource("Page.cs", "t.Message(Loc.Common.Save);");
        WriteResw("en-US", "Common", Resw(("Save", "Save"), ("Dead", "Dead")));

        int exitCode = 0;
        CaptureStdout(() =>
            exitCode = PruneCommand.Run(["--source", _sourceDir, "--resources", _enUsDir, "--dry-run"]));

        Assert.Equal(1, exitCode);
    }

    private static string CaptureStdout(Action action)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        var outWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(new StringWriter());
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
