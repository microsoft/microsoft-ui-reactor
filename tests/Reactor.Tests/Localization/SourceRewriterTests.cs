using Microsoft.UI.Reactor.Cli.Loc;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Localization;

public class SourceRewriterTests : IDisposable
{
    private readonly string _tempDir;

    public SourceRewriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"duct-rewrite-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void Rewrite_BareString_ReplacedWithMessageCall()
    {
        var sourceFile = Path.Combine(_tempDir, "App.cs");
        var source = """
            class App : Component {
                public override Element Render() {
                    return TextBlock("Hello");
                }
            }
            """;
        File.WriteAllText(sourceFile, source);

        // Scan to get span info
        var extractions = LocalizableStringScanner.Scan(source, sourceFile);
        var keyed = KeyNamer.AssignKeys(extractions);

        var count = SourceRewriter.Rewrite(keyed);

        Assert.Equal(1, count);
        var result = File.ReadAllText(sourceFile);
        Assert.Contains("t.Message(Loc.App.Hello)", result);
        Assert.DoesNotContain("\"Hello\"", result);
    }

    [Fact]
    public void Rewrite_Interpolation_ReplacedWithMessageCallAndArgs()
    {
        var sourceFile = Path.Combine(_tempDir, "App.cs");
        var source = """
            class App : Component {
                public override Element Render() {
                    return TextBlock($"Hello, {user.Name}");
                }
            }
            """;
        File.WriteAllText(sourceFile, source);

        var extractions = LocalizableStringScanner.Scan(source, sourceFile);
        var keyed = KeyNamer.AssignKeys(extractions);

        var count = SourceRewriter.Rewrite(keyed);

        Assert.Equal(1, count);
        var result = File.ReadAllText(sourceFile);
        Assert.Contains("t.Message(Loc.App.Hello", result);
        Assert.Contains("(\"name\", user.Name)", result);
    }

    [Fact]
    public void Rewrite_InterpolationWithSimpleVariable_IncludesArgument()
    {
        var sourceFile = Path.Combine(_tempDir, "App.cs");
        var source = """
            class App : Component {
                public override Element Render() {
                    return TextBlock($"Current count: {count}");
                }
            }
            """;
        File.WriteAllText(sourceFile, source);

        var extractions = LocalizableStringScanner.Scan(source, sourceFile);
        var keyed = KeyNamer.AssignKeys(extractions);

        var count = SourceRewriter.Rewrite(keyed);

        Assert.Equal(1, count);
        var result = File.ReadAllText(sourceFile);
        Assert.Contains("(\"count\", count)", result);
    }

    [Fact]
    public void Rewrite_InterpolationWithMixedArgs_IncludesAllArguments()
    {
        var sourceFile = Path.Combine(_tempDir, "App.cs");
        var source = """
            class App : Component {
                public override Element Render() {
                    return TextBlock($"Hello {user.Name}, you have {count} items");
                }
            }
            """;
        File.WriteAllText(sourceFile, source);

        var extractions = LocalizableStringScanner.Scan(source, sourceFile);
        var keyed = KeyNamer.AssignKeys(extractions);

        SourceRewriter.Rewrite(keyed);

        var result = File.ReadAllText(sourceFile);
        Assert.Contains("(\"name\", user.Name)", result);
        Assert.Contains("(\"count\", count)", result);
    }

    [Fact]
    public void Rewrite_UseIntlDeclaration_InsertedWhenMissing()
    {
        var sourceFile = Path.Combine(_tempDir, "App.cs");
        var source = """
            class App : Component {
                public override Element Render() {
                    return TextBlock("Hello");
                }
            }
            """;
        File.WriteAllText(sourceFile, source);

        var extractions = LocalizableStringScanner.Scan(source, sourceFile);
        var keyed = KeyNamer.AssignKeys(extractions);

        SourceRewriter.Rewrite(keyed);

        var result = File.ReadAllText(sourceFile);
        Assert.Contains("var t = UseIntl();", result);
    }

    [Fact]
    public void Rewrite_UseIntlAlreadyPresent_NotDuplicated()
    {
        var sourceFile = Path.Combine(_tempDir, "App.cs");
        var source = """
            class App : Component {
                public override Element Render() {
                    var t = UseIntl();
                    return TextBlock("Hello");
                }
            }
            """;
        File.WriteAllText(sourceFile, source);

        var extractions = LocalizableStringScanner.Scan(source, sourceFile);
        var keyed = KeyNamer.AssignKeys(extractions);

        SourceRewriter.Rewrite(keyed);

        var result = File.ReadAllText(sourceFile);
        // Count occurrences of UseIntl — should be exactly 1
        var count = result.Split("UseIntl()").Length - 1;
        Assert.Equal(1, count);
    }

    [Fact]
    public void Rewrite_MultipleStrings_AllReplaced()
    {
        var sourceFile = Path.Combine(_tempDir, "InboxPage.cs");
        var source = """
            class InboxPage : Component {
                public override Element Render() {
                    return VStack(
                        Heading("Inbox"),
                        Button("Compose", OnCompose)
                    );
                }
            }
            """;
        File.WriteAllText(sourceFile, source);

        var extractions = LocalizableStringScanner.Scan(source, sourceFile);
        var keyed = KeyNamer.AssignKeys(extractions);

        var count = SourceRewriter.Rewrite(keyed);

        Assert.Equal(2, count);
        var result = File.ReadAllText(sourceFile);
        Assert.Contains("t.Message(Loc.Inbox.Inbox)", result);
        Assert.Contains("t.Message(Loc.Inbox.Compose)", result);
        Assert.DoesNotContain("\"Inbox\"", result);
        Assert.DoesNotContain("\"Compose\"", result);
    }
}
