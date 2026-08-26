using System.Xml.Linq;
using Microsoft.UI.Reactor.Cli.Loc;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Localization;

public class ReswWriterTests : IDisposable
{
    private readonly string _tempDir;

    public ReswWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"reactor-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void LoadExisting_EmptyDir_ReturnsEmpty()
    {
        var entries = ReswWriter.LoadExisting(_tempDir);
        Assert.Empty(entries);
    }

    [Fact]
    public void LoadExisting_WithReswFile_ReturnsEntries()
    {
        var resw = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Save" xml:space="preserve"><value>Save</value></data>
              <data name="Cancel" xml:space="preserve"><value>Cancel</value></data>
            </root>
            """;
        File.WriteAllText(Path.Combine(_tempDir, "Settings.resw"), resw);

        var entries = ReswWriter.LoadExisting(_tempDir);

        Assert.Equal(2, entries.Count);
        Assert.Equal("Save", entries[("Settings", "Save")]);
        Assert.Equal("Cancel", entries[("Settings", "Cancel")]);
    }

    [Fact]
    public void Write_NewEntries_CreatesReswFile()
    {
        var source = new LocalizableString
        {
            FilePath = "App.cs", ClassName = "App", Context = "Text",
            Value = "Hello", SpanStart = 0, SpanLength = 7,
        };

        var newEntries = new List<KeyedLocString>
        {
            new()
            {
                ReswFileName = "App", Key = "Hello", Value = "Hello",
                Source = source,
            },
            new()
            {
                ReswFileName = "App", Key = "Save", Value = "Save",
                Source = source,
            },
        };

        ReswWriter.Write(_tempDir, newEntries);

        var filePath = Path.Combine(_tempDir, "App.resw");
        Assert.True(File.Exists(filePath));

        var doc = XDocument.Load(filePath);
        var dataElements = doc.Root!.Elements("data").ToList();

        Assert.Equal(2, dataElements.Count);
        // Should be alphabetically sorted
        Assert.Equal("Hello", dataElements[0].Attribute("name")?.Value);
        Assert.Equal("Save", dataElements[1].Attribute("name")?.Value);
    }

    [Fact]
    public void Write_ExistingKeys_Preserved()
    {
        // Pre-populate a .resw file
        var existingResw = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Cancel" xml:space="preserve"><value>Cancel</value></data>
            </root>
            """;
        File.WriteAllText(Path.Combine(_tempDir, "App.resw"), existingResw);

        var existing = ReswWriter.LoadExisting(_tempDir);

        var source = new LocalizableString
        {
            FilePath = "App.cs", ClassName = "App", Context = "Text",
            Value = "Save", SpanStart = 0, SpanLength = 6,
        };

        var newEntries = new List<KeyedLocString>
        {
            new()
            {
                ReswFileName = "App", Key = "Save", Value = "Save",
                Source = source,
            },
        };

        ReswWriter.Write(_tempDir, newEntries);

        var doc = XDocument.Load(Path.Combine(_tempDir, "App.resw"));
        var dataElements = doc.Root!.Elements("data").ToList();

        Assert.Equal(2, dataElements.Count);
        Assert.Equal("Cancel", dataElements[0].Attribute("name")?.Value);
        Assert.Equal("Save", dataElements[1].Attribute("name")?.Value);
    }

    [Fact]
    public void Write_WithComment_IncludesComment()
    {
        var source = new LocalizableString
        {
            FilePath = "Cart.cs", ClassName = "Cart", Context = "Text",
            Value = "You have {count} items", SpanStart = 0, SpanLength = 30,
            IsInterpolation = true,
        };

        var newEntries = new List<KeyedLocString>
        {
            new()
            {
                ReswFileName = "Cart", Key = "YouHaveItems",
                Value = "You have {count} items",
                Comment = "auto-extracted from interpolation; consider adding plural support",
                Source = source,
            },
        };

        ReswWriter.Write(_tempDir, newEntries);

        var doc = XDocument.Load(Path.Combine(_tempDir, "Cart.resw"));
        var data = doc.Root!.Elements("data").Single();
        var comment = data.Element("comment")?.Value;

        Assert.Contains("auto-extracted", comment);
        Assert.Contains("plural support", comment);
    }
}
