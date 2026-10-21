using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

public class PickerTests
{
    private static readonly IReadOnlyList<FileTypeChoice> TextChoice = [new("Text", [".txt"])];

    public static TheoryData<string> Operations => new()
    {
        "PickFileAsync", "PickMultipleFilesAsync", "PickFolderAsync", "PickMultipleFoldersAsync", "PickSaveFileAsync",
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public void NullOptionsAreRejectedBeforeWindowLookup(string operation)
    {
        var context = new RenderContext();
        var error = Assert.Throws<ArgumentNullException>(() => Invoke(context, operation, validOptions: false));
        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void MissingWindowReportsTheRequestedOperation(string operation)
    {
        // A context that never rendered inside a ReactorWindow host has no owner.
        var context = new RenderContext();
        var error = Assert.Throws<InvalidOperationException>(() => Invoke(context, operation, validOptions: true));
        Assert.Equal(NoOwnerMessage(operation), error.Message);
    }

    [Fact]
    public void SavePickerRejectsMissingFileTypeChoices()
    {
        var context = new RenderContext();
        var missing = Assert.Throws<ArgumentNullException>(() => { _ = context.PickSaveFileAsync(new(null!)); });
        Assert.Equal("options.FileTypeChoices", missing.ParamName);
        var empty = Assert.Throws<ArgumentException>(() => { _ = context.PickSaveFileAsync(new([])); });
        Assert.Equal("options", empty.ParamName);
        Assert.StartsWith("At least one file type choice is required.", empty.Message);
    }

    public static TheoryData<string, FileTypeChoice[]> InvalidChoices => new()
    {
        { "File type choices cannot contain null entries.", [new("Text", [".txt"]), null!] },
        { "Each file type choice requires a label.", [new(" ", [".txt"])] },
        { "Each file type choice requires a label.", [new(null!, [".txt"])] },
        { "File type choice 'Text' requires at least one extension.", [new("Text", [])] },
        { "File type choice 'Text' requires at least one extension.", [new("Text", null!)] },
        { "File type choice 'Text' is listed more than once.", [new("Text", [".txt"]), new("JSON", [".json"]), new("Text", [".md"])] },
    };

    // Validation runs before the owner lookup, so these reach the option checks headlessly.
    [Theory]
    [MemberData(nameof(InvalidChoices))]
    public void SavePickerRejectsInvalidChoices(string message, FileTypeChoice[] choices)
    {
        var error = Assert.Throws<ArgumentException>(() => { _ = new RenderContext().PickSaveFileAsync(new(choices)); });
        Assert.Equal("options", error.ParamName);
        Assert.StartsWith(message, error.Message);
    }

    [Theory]
    [MemberData(nameof(InvalidChoices))]
    public void OpenPickersRejectInvalidChoices(string message, FileTypeChoice[] choices)
    {
        var options = new FilePickerOptions { FileTypeChoices = choices };
        var single = Assert.Throws<ArgumentException>(() => { _ = new RenderContext().PickFileAsync(options); });
        var multiple = Assert.Throws<ArgumentException>(() => { _ = new RenderContext().PickMultipleFilesAsync(options); });
        Assert.StartsWith(message, single.Message);
        Assert.StartsWith(message, multiple.Message);
    }

    [Fact]
    public void OpenPickersAcceptMissingOrEmptyChoices()
    {
        // Choices are optional for open pickers, so these get past validation to the owner lookup.
        foreach (var options in new FilePickerOptions[] { new(), new() { FileTypeChoices = [] } })
        {
            var error = Assert.Throws<InvalidOperationException>(() => { _ = new RenderContext().PickFileAsync(options); });
            Assert.Equal(NoOwnerMessage("PickFileAsync"), error.Message);
        }
    }

    [Fact]
    public void LabelsAreComparedOrdinally()
    {
        // "Text" and "text" are distinct native map keys, so they are not duplicates.
        var options = new SaveFilePickerOptions([new("Text", [".txt"]), new("text", [".md"])]);
        var error = Assert.Throws<InvalidOperationException>(() => { _ = new RenderContext().PickSaveFileAsync(options); });
        Assert.Equal(NoOwnerMessage("PickSaveFileAsync"), error.Message);
    }

    [Fact]
    public void SaveOptionsDefaultToShowingTheOverwritePrompt()
    {
        var options = new SaveFilePickerOptions(TextChoice);
        Assert.True(options.ShowOverwritePrompt);
        Assert.Null(options.InitialFileTypeIndex);
        Assert.Equal("", options.SuggestedFolder);
        Assert.Equal("", options.DefaultFileExtension);
    }

    // A context that never rendered in a ReactorWindow (ReactorHostControl content, or no host
    // at all) gets the message that names the hosting limitation and its workaround.
    private static string NoOwnerMessage(string operation) =>
        $"{operation} requires a component rendered in a ReactorWindow. Content hosted by ReactorHostControl, or by a ReactorHost you construct, has no owning window: construct the Microsoft.Windows.Storage.Pickers picker from the hosting window's AppWindow.Id instead.";
    private static void Invoke(RenderContext context, string operation, bool validOptions)
    {
        FilePickerOptions file = validOptions ? new() : null!;
        FolderPickerOptions folder = validOptions ? new() : null!;
        SaveFilePickerOptions save = validOptions ? new(TextChoice) : null!;
        _ = operation switch
        {
            "PickFileAsync" => (Task)context.PickFileAsync(file),
            "PickMultipleFilesAsync" => context.PickMultipleFilesAsync(file),
            "PickFolderAsync" => context.PickFolderAsync(folder),
            "PickMultipleFoldersAsync" => context.PickMultipleFoldersAsync(folder),
            "PickSaveFileAsync" => context.PickSaveFileAsync(save),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }
}
