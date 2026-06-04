using System.IO;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Source-level guard for spec 050 Phase 7: pool cleanup must clear controlled
/// dependency properties instead of writing concrete values that would persist
/// through an Optional&lt;T&gt;.Unset remount.
/// </summary>
public class ElementPoolOptionalResetTests
{
    [Fact]
    public void TextBox_CleanElement_Clears_Text_DependencyProperty()
    {
        var source = ReadElementPoolSource();

        Assert.Contains("textBox.ClearValue(TextBox.TextProperty);", source);
        Assert.DoesNotContain("textBox.Text = \"\";", source);
    }

    [Fact]
    public void ToggleSwitch_CleanElement_Clears_IsOn_DependencyProperty()
    {
        var source = ReadElementPoolSource();

        Assert.Contains("toggle.ClearValue(WinUI.ToggleSwitch.IsOnProperty);", source);
        Assert.DoesNotContain("toggle.IsOn = false;", source);
    }

    private static string ReadElementPoolSource()
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Join(root!, "src", "Reactor", "Core", "ElementPool.cs");
        Assert.True(File.Exists(path), $"ElementPool.cs not found at {path}");
        return File.ReadAllText(path);
    }
}
