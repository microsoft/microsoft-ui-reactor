using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class PickerHookTests
{
    [Theory]
    [InlineData("UseFilePickerAsync")]
    [InlineData("UseMultipleFilesPickerAsync")]
    [InlineData("UseFolderPickerAsync")]
    [InlineData("UseSaveFilePickerAsync")]
    public void NullOptionsAreRejectedBeforeWindowLookup(string method)
    {
        var context = new RenderContext();
        var error = Assert.Throws<ArgumentNullException>(() => Invoke(context, method, null));
        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData("UseFilePickerAsync")]
    [InlineData("UseMultipleFilesPickerAsync")]
    [InlineData("UseFolderPickerAsync")]
    [InlineData("UseSaveFilePickerAsync")]
    public void MissingWindowReportsTheRequestedOperation(string method)
    {
        var context = new RenderContext();
        var error = Assert.Throws<InvalidOperationException>(() => Invoke(context, method, new FilePickerOptions()));
        Assert.Equal($"{method} requires an owning ReactorWindow.", error.Message);
    }

    [Fact]
    public void SavePickerRejectsMissingFileTypeChoices()
    {
        var missing = Assert.Throws<ArgumentNullException>(() => DefaultPickerService.CreateFileSavePicker(0, new(null!)));
        Assert.Equal("options.FileTypeChoices", missing.ParamName);
        var empty = Assert.Throws<ArgumentException>(() => DefaultPickerService.CreateFileSavePicker(0, new(new Dictionary<string, IReadOnlyList<string>>())));
        Assert.Equal("options", empty.ParamName);
        Assert.StartsWith("At least one file type choice is required.", empty.Message);
    }

    private static void Invoke(RenderContext context, string method, FilePickerOptions? options)
    {
        _ = method switch
        {
            "UseFilePickerAsync" => (Task)context.UseFilePickerAsync(options!),
            "UseMultipleFilesPickerAsync" => context.UseMultipleFilesPickerAsync(options!),
            "UseFolderPickerAsync" => context.UseFolderPickerAsync(options is null ? null! : new()),
            "UseSaveFilePickerAsync" => context.UseSaveFilePickerAsync(options is null ? null! : new(
                new Dictionary<string, IReadOnlyList<string>> { ["Text"] = [".txt"] })),
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };
    }
}
