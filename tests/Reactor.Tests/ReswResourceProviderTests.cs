using Microsoft.UI.Reactor.Localization;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Tests for ReswResourceProvider's .resw XML parsing and caching logic.
/// Validates that the resource provider correctly reads, caches, and retrieves
/// localized strings from .resw files without relying on MRT/PRI.
/// </summary>
public class ReswResourceProviderTests : IDisposable
{
    private readonly string _tempDir;

    public ReswResourceProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "reactor_resw_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void WriteReswFile(string locale, string ns, string content)
    {
        var dir = Path.Combine(_tempDir, locale);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{ns}.resw"), content);
    }

    private const string ValidResw = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <data name="Greeting" xml:space="preserve">
            <value>Hello</value>
          </data>
          <data name="Farewell" xml:space="preserve">
            <value>Goodbye</value>
          </data>
          <data name="Empty" xml:space="preserve">
            <value></value>
          </data>
        </root>
        """;

    private const string SpanishResw = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <data name="Greeting" xml:space="preserve">
            <value>Hola</value>
          </data>
          <data name="Farewell" xml:space="preserve">
            <value>Adiós</value>
          </data>
        </root>
        """;

    // ════════════════════════════════════════════════════════════════
    //  Basic lookup
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetString_Returns_Value_For_Existing_Key()
    {
        WriteReswFile("en-US", "Resources", ValidResw);
        var provider = new ReswResourceProvider("en-US", _tempDir);

        Assert.Equal("Hello", provider.GetString("en-US", "Resources", "Greeting"));
        Assert.Equal("Goodbye", provider.GetString("en-US", "Resources", "Farewell"));
    }

    [Fact]
    public void GetString_Returns_Null_For_Missing_Key()
    {
        WriteReswFile("en-US", "Resources", ValidResw);
        var provider = new ReswResourceProvider("en-US", _tempDir);

        Assert.Null(provider.GetString("en-US", "Resources", "NonExistent"));
    }

    [Fact]
    public void GetString_Returns_EmptyString_For_Empty_Value()
    {
        WriteReswFile("en-US", "Resources", ValidResw);
        var provider = new ReswResourceProvider("en-US", _tempDir);

        Assert.Equal("", provider.GetString("en-US", "Resources", "Empty"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Locale handling
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetString_Different_Locales_Return_Different_Values()
    {
        WriteReswFile("en-US", "Resources", ValidResw);
        WriteReswFile("es-ES", "Resources", SpanishResw);
        var provider = new ReswResourceProvider("en-US", _tempDir);

        Assert.Equal("Hello", provider.GetString("en-US", "Resources", "Greeting"));
        Assert.Equal("Hola", provider.GetString("es-ES", "Resources", "Greeting"));
    }

    [Fact]
    public void GetString_Returns_Null_For_Missing_Locale()
    {
        WriteReswFile("en-US", "Resources", ValidResw);
        var provider = new ReswResourceProvider("en-US", _tempDir);

        Assert.Null(provider.GetString("fr-FR", "Resources", "Greeting"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Namespace handling
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetString_Returns_Null_For_Missing_Namespace()
    {
        WriteReswFile("en-US", "Resources", ValidResw);
        var provider = new ReswResourceProvider("en-US", _tempDir);

        Assert.Null(provider.GetString("en-US", "NonExistentNamespace", "Greeting"));
    }

    [Fact]
    public void GetString_Different_Namespaces_Are_Independent()
    {
        WriteReswFile("en-US", "Resources", ValidResw);
        WriteReswFile("en-US", "Errors", """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Greeting" xml:space="preserve">
                <value>Error greeting</value>
              </data>
            </root>
            """);
        var provider = new ReswResourceProvider("en-US", _tempDir);

        Assert.Equal("Hello", provider.GetString("en-US", "Resources", "Greeting"));
        Assert.Equal("Error greeting", provider.GetString("en-US", "Errors", "Greeting"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Caching
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetString_Caches_Results_After_First_Load()
    {
        WriteReswFile("en-US", "Resources", ValidResw);
        var provider = new ReswResourceProvider("en-US", _tempDir);

        // First call — loads from disk
        Assert.Equal("Hello", provider.GetString("en-US", "Resources", "Greeting"));

        // Delete the file — second call should still work from cache
        File.Delete(Path.Combine(_tempDir, "en-US", "Resources.resw"));
        Assert.Equal("Hello", provider.GetString("en-US", "Resources", "Greeting"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Error handling
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void GetString_Returns_Null_For_Malformed_Xml()
    {
        WriteReswFile("en-US", "Bad", "this is not xml <><>");
        var provider = new ReswResourceProvider("en-US", _tempDir);

        Assert.Null(provider.GetString("en-US", "Bad", "Greeting"));
    }

    [Fact]
    public void GetString_Handles_Missing_Value_Element()
    {
        WriteReswFile("en-US", "NoValue", """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="KeyOnly">
              </data>
            </root>
            """);
        var provider = new ReswResourceProvider("en-US", _tempDir);

        // Missing <value> should produce empty string (not crash)
        Assert.Equal("", provider.GetString("en-US", "NoValue", "KeyOnly"));
    }

    [Fact]
    public void GetString_Handles_Missing_Name_Attribute()
    {
        WriteReswFile("en-US", "NoName", """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data>
                <value>orphan</value>
              </data>
              <data name="Valid">
                <value>OK</value>
              </data>
            </root>
            """);
        var provider = new ReswResourceProvider("en-US", _tempDir);

        // Name-less entry is skipped; valid entry works
        Assert.Equal("OK", provider.GetString("en-US", "NoName", "Valid"));
    }

    // ════════════════════════════════════════════════════════════════
    //  Constructor defaults
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_Defaults_To_EnUS()
    {
        var provider = new ReswResourceProvider();
        // Can create without arguments (uses defaults)
        Assert.NotNull(provider);
    }
}
