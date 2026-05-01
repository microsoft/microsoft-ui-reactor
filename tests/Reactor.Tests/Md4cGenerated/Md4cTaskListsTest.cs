using Microsoft.UI.Reactor.Markdown;
using System.Text;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Md4cGenerated;

/// <summary>
/// Ported from md4c/test/spec-tasklists.txt
/// </summary>
public class Md4cTaskListsTest
{
    [Fact]
    public void Example_0001()
    {
        var md = " * [x] foo\n * [X] bar\n * [ ] baz";
        var expected = "<ul>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled checked>foo</li>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled checked>bar</li>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled>baz</li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.TaskLists);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0002()
    {
        var md = " 1. [x] foo\n 2. [X] bar\n 3. [ ] baz";
        var expected = "<ol>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled checked>foo</li>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled checked>bar</li>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled>baz</li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.TaskLists);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0003()
    {
        var md = " * xxx:\n   * [x] foo\n   * [x] bar\n   * [ ] baz\n * yyy:\n   * [ ] qux\n   * [x] quux\n   * [ ] quuz";
        var expected = "<ul>\n<li>xxx:\n<ul>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled checked>foo</li>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled checked>bar</li>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled>baz</li>\n</ul></li>\n<li>yyy:\n<ul>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled>qux</li>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled checked>quux</li>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled>quuz</li>\n</ul></li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.TaskLists);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0004()
    {
        var md = " 1. [x] xxx:\n    * [x] foo\n    * [x] bar\n    * [ ] baz\n 2. [ ] yyy:\n    * [ ] qux\n    * [x] quux\n    * [ ] quuz";
        var expected = "<ol>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled checked>xxx:\n<ul>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled checked>foo</li>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled checked>bar</li>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled>baz</li>\n</ul></li>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled>yyy:\n<ul>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled>qux</li>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled checked>quux</li>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled>quuz</li>\n</ul></li>\n</ol>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.TaskLists);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

    [Fact]
    public void Example_0005()
    {
        var md = " * [x] xxx:\n   * foo\n   * bar\n   * baz\n * [ ] yyy:\n   * qux\n   * quux\n   * quuz";
        var expected = "<ul>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled checked>xxx:\n<ul>\n<li>foo</li>\n<li>bar</li>\n<li>baz</li>\n</ul></li>\n<li class=\"task-list-item\"><input type=\"checkbox\" class=\"task-list-item-checkbox\" disabled>yyy:\n<ul>\n<li>qux</li>\n<li>quux</li>\n<li>quuz</li>\n</ul></li>\n</ul>\n";
        var actual = MarkdownHtml.ToHtml(md, MarkdownParserFlags.TaskLists);
        Assert.Equal(Md4cTestHelper.NormalizeHtml(expected), Md4cTestHelper.NormalizeHtml(actual));
    }

}
