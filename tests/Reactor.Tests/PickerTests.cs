using Microsoft.UI.Reactor.Core;
using Microsoft.Windows.Storage.Pickers;
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
        // A context that never rendered under a host has no owner.
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

    // The status is passed as an int: ContentOwnerStatus is internal and theory methods are public.
    public static TheoryData<string, int, string> UnavailableOwners()
    {
        var data = new TheoryData<string, int, string>();
        foreach (var operation in new[] { "PickFileAsync", "PickMultipleFilesAsync", "PickFolderAsync", "PickMultipleFoldersAsync", "PickSaveFileAsync" })
        {
            data.Add(operation, (int)ContentOwnerStatus.Closed, $"{operation} was called after the component's window closed or its host was disposed.");
            data.Add(operation, (int)ContentOwnerStatus.Detached, $"{operation} was called while the component's ReactorHostControl is not loaded in a window.");
            data.Add(operation, (int)ContentOwnerStatus.WrongThread, $"{operation} must be called on the owning window's UI thread.");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(UnavailableOwners))]
    public void UnavailableOwnerIsReportedAndNeverReachesThePicker(string operation, int status, string message)
    {
        var owner = new FakeOwner { Status = (ContentOwnerStatus)status };
        var service = new RecordingPickerService();
        var context = new RenderContext();
        context.BeginRender(static () => { }, owner);
        WithService(service, () =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => Invoke(context, operation, validOptions: true));
            Assert.Equal(message, error.Message);
        });
        Assert.Equal(1, owner.Lookups);
        Assert.Null(service.Operation);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void AvailableOwnerRoutesTheResolvedWindowId(string operation)
    {
        // The id is resolved at call time, not captured at render: a host control moved to
        // another window after its last render must route to the new window.
        var owner = new FakeOwner { Status = ContentOwnerStatus.Available, Id = new WindowId(41) };
        var service = new RecordingPickerService();
        var context = new RenderContext();
        context.BeginRender(static () => { }, owner);
        owner.Id = new WindowId(42);
        WithService(service, () => Invoke(context, operation, validOptions: true));
        Assert.Equal(operation, service.Operation);
        Assert.Equal(new WindowId(42), service.Owner);
    }

    [Fact]
    public void UnmountAndRootRetirementReleaseTheOwner()
    {
        var owner = new FakeOwner { Status = ContentOwnerStatus.Available, Id = new WindowId(7) };
        var service = new RecordingPickerService();
        var unmounted = new RenderContext();
        unmounted.BeginRender(static () => { }, owner);
        unmounted.RunCleanups();
        var retired = new RenderContext();
        retired.BeginRender(static () => { }, owner);
        retired.DetachFromHost();
        WithService(service, () =>
        {
            foreach (var context in new[] { unmounted, retired })
            {
                var error = Assert.Throws<InvalidOperationException>(() => { _ = context.PickFileAsync(new()); });
                Assert.Equal("PickFileAsync was called from a component that is no longer mounted.", error.Message);
            }
        });
        Assert.Equal(0, owner.Lookups);
        Assert.Null(service.Operation);

        // A context that renders again (hot-reload reset) re-records its owner.
        unmounted.BeginRender(static () => { }, owner);
        WithService(service, () => { _ = unmounted.PickFileAsync(new()); });
        Assert.Equal(new WindowId(7), service.Owner);
    }

    [Fact]
    public void OwningReactorWindowIsNullWithoutAWindowOwner()
    {
        var context = new RenderContext();
        Assert.Null(context.OwningReactorWindow);
        context.BeginRender(static () => { }, new FakeOwner());
        Assert.Null(context.OwningReactorWindow);
    }

    private static void WithService(IPickerService service, Action action)
    {
        var previous = RenderContext.PickerService;
        RenderContext.PickerService = service;
        try { action(); }
        finally { RenderContext.PickerService = previous; }
    }

    private sealed class FakeOwner : IContentOwner
    {
        public ContentOwnerStatus Status;
        public WindowId Id;
        public int Lookups;
        public ReactorWindow? ReactorWindow => null;

        public ContentOwnerStatus TryGetWindowId(out WindowId id)
        {
            Lookups++;
            id = Status == ContentOwnerStatus.Available ? Id : default;
            return Status;
        }
    }

    private sealed class RecordingPickerService : IPickerService
    {
        public string? Operation;
        public WindowId Owner;

        private Task<T> Record<T>(WindowId owner, string operation, T result)
        {
            Owner = owner;
            Operation = operation;
            return Task.FromResult(result);
        }

        public Task<PickFileResult?> PickFileAsync(WindowId owner, FilePickerOptions options)
            => Record<PickFileResult?>(owner, "PickFileAsync", null);
        public Task<IReadOnlyList<PickFileResult>> PickMultipleFilesAsync(WindowId owner, FilePickerOptions options)
            => Record<IReadOnlyList<PickFileResult>>(owner, "PickMultipleFilesAsync", []);
        public Task<PickFolderResult?> PickFolderAsync(WindowId owner, FolderPickerOptions options)
            => Record<PickFolderResult?>(owner, "PickFolderAsync", null);
        public Task<IReadOnlyList<PickFolderResult>> PickMultipleFoldersAsync(WindowId owner, FolderPickerOptions options)
            => Record<IReadOnlyList<PickFolderResult>>(owner, "PickMultipleFoldersAsync", []);
        public Task<PickFileResult?> PickSaveFileAsync(WindowId owner, SaveFilePickerOptions options)
            => Record<PickFileResult?>(owner, "PickSaveFileAsync", null);
    }

    // A context that never rendered under a host (no reconciler-supplied owner).
    private static string NoOwnerMessage(string operation) =>
        $"{operation} requires a component rendered by a Reactor host (a ReactorWindow, ReactorHostControl or ReactorHost).";
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
