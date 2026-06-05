using Windows.Storage;
using Windows.Storage.Pickers;

namespace Microsoft.UI.Reactor.Core;

/// <summary>Options for <see cref="RenderContext.UseFilePickerAsync"/>.</summary>
public sealed record FilePickerOptions(
    IReadOnlyList<string>? FileTypeFilter = null,
    PickerLocationId SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
    string CommitButtonText = "");

/// <summary>Options for <see cref="RenderContext.UseFolderPickerAsync"/>.</summary>
public sealed record FolderPickerOptions(
    PickerLocationId SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
    string CommitButtonText = "");

internal interface IPickerService
{
    Task<StorageFile?> PickFileAsync(nint hwnd, FilePickerOptions options);
    Task<StorageFolder?> PickFolderAsync(nint hwnd, FolderPickerOptions options);
}

internal sealed class DefaultPickerService : IPickerService
{
    public async Task<StorageFile?> PickFileAsync(nint hwnd, FilePickerOptions options)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = options.SuggestedStartLocation,
            CommitButtonText = options.CommitButtonText ?? string.Empty,
        };
        var filters = options.FileTypeFilter is { Count: > 0 } ? options.FileTypeFilter : ["*"];
        foreach (var filter in filters)
            picker.FileTypeFilter.Add(filter);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return await picker.PickSingleFileAsync();
    }

    public async Task<StorageFolder?> PickFolderAsync(nint hwnd, FolderPickerOptions options)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = options.SuggestedStartLocation,
            CommitButtonText = options.CommitButtonText ?? string.Empty,
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return await picker.PickSingleFolderAsync();
    }
}
