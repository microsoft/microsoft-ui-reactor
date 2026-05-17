using System;
using System.IO;
using Microsoft.UI.Reactor.Cli.Docs;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

public class SourceSnippetTests : IDisposable
{
    private readonly string _root;

    public SourceSnippetTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "reactor-doc-snippet-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* test teardown best-effort */ }
    }

    private string WriteFile(string relative, string content)
    {
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    [Theory]
    [InlineData("source:src/Foo.cs#demo", "src/Foo.cs", "demo")]
    [InlineData("source:nested/path/file.cs#alpha", "nested/path/file.cs", "alpha")]
    [InlineData("source:src/Foo.cs#has-dashes", "src/Foo.cs", "has-dashes")]
    public void Parses_source_reference(string input, string path, string region)
    {
        Assert.True(SnippetExtractor.TryParseSourceReference(input, out var p, out var r));
        Assert.Equal(path, p);
        Assert.Equal(region, r);
    }

    [Theory]
    [InlineData("topic/id")]              // legacy form
    [InlineData("source:no-hash")]        // missing #region
    [InlineData("source:#region")]        // missing path
    [InlineData("source:path#")]          // missing region
    public void Rejects_non_source_references(string input)
    {
        Assert.False(SnippetExtractor.TryParseSourceReference(input, out _, out _));
    }

    [Fact]
    public void Happy_path_extracts_between_markers()
    {
        WriteFile("src/Foo.cs", """
            namespace X;
            public static class Foo
            {
                // <snippet:demo>
                public static int Square(int x) => x * x;
                public static int Cube(int x) => x * x * x;
                // </snippet:demo>
            }
            """);

        var snip = SnippetExtractor.ExtractFromSource(_root, "src/Foo.cs", "demo");
        Assert.Contains("Square", snip.Code);
        Assert.Contains("Cube", snip.Code);
        // Markers themselves are NOT in the extracted body.
        Assert.DoesNotContain("<snippet:demo>", snip.Code);
    }

    [Fact]
    public void Handles_html_comment_markers()
    {
        WriteFile("src/Sample.md", """
            <!-- <snippet:html> -->
            hello world
            <!-- </snippet:html> -->
            """);
        var snip = SnippetExtractor.ExtractFromSource(_root, "src/Sample.md", "html");
        Assert.Equal("hello world", snip.Code.Trim());
    }

    [Fact]
    public void Handles_vb_single_quote_markers()
    {
        WriteFile("src/Sample.vb", """
            Module M
                ' <snippet:vb>
                Sub Hello() : End Sub
                ' </snippet:vb>
            End Module
            """);
        var snip = SnippetExtractor.ExtractFromSource(_root, "src/Sample.vb", "vb");
        Assert.Contains("Sub Hello()", snip.Code);
    }

    [Fact]
    public void File_not_found_raises_001()
    {
        var ex = Assert.Throws<DocPipelineException>(() =>
            SnippetExtractor.ExtractFromSource(_root, "src/Missing.cs", "x"));
        Assert.Equal("REACTOR_DOC_SNIPPET_001", ex.Code);
    }

    [Fact]
    public void Region_not_found_raises_002()
    {
        WriteFile("src/Foo.cs", """
            // no markers here
            public class Foo {}
            """);
        var ex = Assert.Throws<DocPipelineException>(() =>
            SnippetExtractor.ExtractFromSource(_root, "src/Foo.cs", "demo"));
        Assert.Equal("REACTOR_DOC_SNIPPET_002", ex.Code);
    }

    [Fact]
    public void Unterminated_region_raises_003()
    {
        WriteFile("src/Foo.cs", """
            // <snippet:demo>
            public int X;
            // no closer.
            """);
        var ex = Assert.Throws<DocPipelineException>(() =>
            SnippetExtractor.ExtractFromSource(_root, "src/Foo.cs", "demo"));
        Assert.Equal("REACTOR_DOC_SNIPPET_003", ex.Code);
    }

    [Fact]
    public void Nested_same_name_region_raises_004()
    {
        WriteFile("src/Foo.cs", """
            // <snippet:demo>
            // <snippet:demo>
            inner
            // </snippet:demo>
            // </snippet:demo>
            """);
        var ex = Assert.Throws<DocPipelineException>(() =>
            SnippetExtractor.ExtractFromSource(_root, "src/Foo.cs", "demo"));
        Assert.Equal("REACTOR_DOC_SNIPPET_004", ex.Code);
    }
}
