using Windows.Storage;
using Windows.Storage.Pickers;

namespace Microsoft.UI.Reactor.Core;

/// <summary>Options for single-file and multiple-file open pickers.</summary>
public sealed record FilePickerOptions(
    IReadOnlyList<string>? FileTypeFilter = null,
    PickerLocationId SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
    string CommitButtonText = "")
{
    /// <summary>The native view mode; null preserves the platform default.</summary>
    public PickerViewMode? ViewMode { get; init; }

    /// <summary>Identifies the picker state remembered by Windows.</summary>
    public string SettingsIdentifier { get; init; } = "";
}

/// <summary>Options for <see cref="RenderContext.UseFolderPickerAsync"/>.</summary>
public sealed record FolderPickerOptions(
    PickerLocationId SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
    string CommitButtonText = "")
{
    /// <summary>Displayed file types; null or empty lists default to all types.</summary>
    public IReadOnlyList<string>? FileTypeFilter { get; init; }

    /// <summary>The native view mode; null preserves the platform default.</summary>
    public PickerViewMode? ViewMode { get; init; }

    /// <summary>Identifies the picker state remembered by Windows.</summary>
    public string SettingsIdentifier { get; init; } = "";
}

/// <summary>Options for <see cref="RenderContext.UseSaveFilePickerAsync"/>.</summary>
/// <param name="FileTypeChoices">Display labels mapped to allowed extensions, such as ".txt". At least one choice is required.</param>
/// <param name="SuggestedFileName">The name suggested to the user, without requiring an extension.</param>
/// <param name="SuggestedStartLocation">The initial location suggested to Windows.</param>
/// <param name="CommitButtonText">The commit button label; empty uses the platform default.</param>
public sealed record SaveFilePickerOptions(
    IReadOnlyDictionary<string, IReadOnlyList<string>> FileTypeChoices,
    string SuggestedFileName = "",
    PickerLocationId SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
    string CommitButtonText = "")
{
    /// <summary>Identifies the picker state remembered by Windows.</summary>
    public string SettingsIdentifier { get; init; } = "";

    /// <summary>An existing file suggested as the save target.</summary>
    public StorageFile? SuggestedSaveFile { get; init; }
}

internal interface IPickerService
{
    Task<StorageFile?> PickFileAsync(nint hwnd, FilePickerOptions options);
    Task<IReadOnlyList<StorageFile>> PickMultipleFilesAsync(nint hwnd, FilePickerOptions options);
    Task<StorageFolder?> PickFolderAsync(nint hwnd, FolderPickerOptions options);
    Task<StorageFile?> PickSaveFileAsync(nint hwnd, SaveFilePickerOptions options);
}

internal sealed class DefaultPickerService : IPickerService
{
    public async Task<StorageFile?> PickFileAsync(nint hwnd, FilePickerOptions options)
        => await CreateFileOpenPicker(hwnd, options).PickSingleFileAsync();

    public async Task<IReadOnlyList<StorageFile>> PickMultipleFilesAsync(nint hwnd, FilePickerOptions options)
        // Preserve the WinRT projection instead of materializing another collection of
        // StorageFile objects, which would require extra ABI generation in AOT apps.
        => await CreateFileOpenPicker(hwnd, options).PickMultipleFilesAsync();

    public async Task<StorageFolder?> PickFolderAsync(nint hwnd, FolderPickerOptions options)
        => await CreateFolderPicker(hwnd, options).PickSingleFolderAsync();

    public async Task<StorageFile?> PickSaveFileAsync(nint hwnd, SaveFilePickerOptions options)
        => await CreateFileSavePicker(hwnd, options).PickSaveFileAsync();

    internal static FileOpenPicker CreateFileOpenPicker(nint hwnd, FilePickerOptions options)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = options.SuggestedStartLocation,
            CommitButtonText = options.CommitButtonText ?? string.Empty,
            SettingsIdentifier = options.SettingsIdentifier ?? string.Empty,
        };
        if (options.ViewMode is { } viewMode)
            picker.ViewMode = viewMode;
        var filters = options.FileTypeFilter is { Count: > 0 } ? options.FileTypeFilter : ["*"];
        foreach (var filter in filters)
            picker.FileTypeFilter.Add(filter);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return picker;
    }

    internal static FolderPicker CreateFolderPicker(nint hwnd, FolderPickerOptions options)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = options.SuggestedStartLocation,
            CommitButtonText = options.CommitButtonText ?? string.Empty,
            SettingsIdentifier = options.SettingsIdentifier ?? string.Empty,
        };
        if (options.ViewMode is { } viewMode)
            picker.ViewMode = viewMode;
        var filters = options.FileTypeFilter is { Count: > 0 } ? options.FileTypeFilter : ["*"];
        foreach (var filter in filters)
            picker.FileTypeFilter.Add(filter);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return picker;
    }

    internal static FileSavePicker CreateFileSavePicker(nint hwnd, SaveFilePickerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options.FileTypeChoices);
        if (options.FileTypeChoices.Count == 0)
            throw new ArgumentException("At least one file type choice is required.", nameof(options));

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = options.SuggestedStartLocation,
            SuggestedFileName = options.SuggestedFileName ?? string.Empty,
            CommitButtonText = options.CommitButtonText ?? string.Empty,
            SettingsIdentifier = options.SettingsIdentifier ?? string.Empty,
        };
        foreach (var (label, extensions) in options.FileTypeChoices)
            picker.FileTypeChoices.Add(label, new List<string>(extensions));
        if (options.SuggestedSaveFile is { } saveFile)
            picker.SuggestedSaveFile = saveFile;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return picker;
    }
}
