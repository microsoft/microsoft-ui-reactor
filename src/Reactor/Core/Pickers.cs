using Microsoft.UI;
using Microsoft.Windows.Storage.Pickers;

namespace Microsoft.UI.Reactor.Core;

/// <summary>A labelled group of file extensions offered by a file picker, such as <c>("Text", [".txt", ".md"])</c>.</summary>
/// <param name="Label">The display label shown in the picker's file-type list.</param>
/// <param name="Extensions">The extensions in the group, each including the leading dot.</param>
public sealed record FileTypeChoice(string Label, IReadOnlyList<string> Extensions);

/// <summary>Options for <see cref="RenderContext.PickFileAsync"/> and <see cref="RenderContext.PickMultipleFilesAsync"/>.</summary>
/// <param name="FileTypeFilter">Extensions shown, such as ".txt"; null or empty shows all files. Ignored when <see cref="FileTypeChoices"/> is non-empty.</param>
/// <param name="SuggestedStartLocation">The initial location suggested to Windows.</param>
/// <param name="CommitButtonText">The commit button label; empty uses the platform default.</param>
public sealed record FilePickerOptions(
    IReadOnlyList<string>? FileTypeFilter = null,
    PickerLocationId SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
    string CommitButtonText = "")
{
    /// <summary>The native view mode; null preserves the platform default.</summary>
    public PickerViewMode? ViewMode { get; init; }

    /// <summary>Identifies the picker state remembered by Windows.</summary>
    public string SettingsIdentifier { get; init; } = "";

    /// <summary>The dialog title; empty uses the platform default.</summary>
    public string Title { get; init; } = "";

    /// <summary>An absolute folder path that the dialog always opens in; empty lets Windows decide.</summary>
    public string SuggestedFolder { get; init; } = "";

    /// <summary>An absolute folder path used only until Windows remembers a location for this picker; empty lets Windows decide.</summary>
    public string SuggestedStartFolder { get; init; } = "";

    /// <summary>Ordered, labelled file-type groups. When non-empty they replace <see cref="FileTypeFilter"/>.</summary>
    public IReadOnlyList<FileTypeChoice>? FileTypeChoices { get; init; }

    /// <summary>The zero-based file-type entry selected initially; null preserves the platform default.</summary>
    public int? InitialFileTypeIndex { get; init; }
}

/// <summary>Options for <see cref="RenderContext.PickFolderAsync"/> and <see cref="RenderContext.PickMultipleFoldersAsync"/>.</summary>
/// <param name="SuggestedStartLocation">The initial location suggested to Windows.</param>
/// <param name="CommitButtonText">The commit button label; empty uses the platform default.</param>
public sealed record FolderPickerOptions(
    PickerLocationId SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
    string CommitButtonText = "")
{
    /// <summary>The native view mode; null preserves the platform default.</summary>
    public PickerViewMode? ViewMode { get; init; }

    /// <summary>Identifies the picker state remembered by Windows.</summary>
    public string SettingsIdentifier { get; init; } = "";

    /// <summary>The dialog title; empty uses the platform default.</summary>
    public string Title { get; init; } = "";

    /// <summary>An absolute folder path that the dialog always opens in; empty lets Windows decide.</summary>
    public string SuggestedFolder { get; init; } = "";

    /// <summary>An absolute folder path used only until Windows remembers a location for this picker; empty lets Windows decide.</summary>
    public string SuggestedStartFolder { get; init; } = "";
}

/// <summary>Options for <see cref="RenderContext.PickSaveFileAsync"/>.</summary>
/// <param name="FileTypeChoices">Ordered, labelled file-type groups. At least one choice is required.</param>
/// <param name="SuggestedFileName">The name suggested to the user, without requiring an extension.</param>
/// <param name="SuggestedStartLocation">The initial location suggested to Windows.</param>
/// <param name="CommitButtonText">The commit button label; empty uses the platform default.</param>
public sealed record SaveFilePickerOptions(
    IReadOnlyList<FileTypeChoice> FileTypeChoices,
    string SuggestedFileName = "",
    PickerLocationId SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
    string CommitButtonText = "")
{
    /// <summary>Identifies the picker state remembered by Windows.</summary>
    public string SettingsIdentifier { get; init; } = "";

    /// <summary>The dialog title; empty uses the platform default.</summary>
    public string Title { get; init; } = "";

    /// <summary>An absolute folder path that the dialog always opens in; empty lets Windows decide.</summary>
    public string SuggestedFolder { get; init; } = "";

    /// <summary>An absolute folder path used only until Windows remembers a location for this picker; empty lets Windows decide.</summary>
    public string SuggestedStartFolder { get; init; } = "";

    /// <summary>The extension, such as ".txt", appended when the user types a name without one; empty uses the platform default.</summary>
    public string DefaultFileExtension { get; init; } = "";

    /// <summary>The zero-based file-type choice selected initially; null preserves the platform default.</summary>
    public int? InitialFileTypeIndex { get; init; }

    /// <summary>Whether Windows asks before overwriting an existing file. Defaults to true.</summary>
    public bool ShowOverwritePrompt { get; init; } = true;
}

internal static class PickerOptionsValidation
{
    public static void Validate(FilePickerOptions options)
    {
        if (options.FileTypeChoices is { } choices)
            ValidateChoices(choices);
    }

    public static void Validate(SaveFilePickerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options.FileTypeChoices, "options.FileTypeChoices");
        if (options.FileTypeChoices.Count == 0)
            throw new ArgumentException("At least one file type choice is required.", nameof(options));
        ValidateChoices(options.FileTypeChoices);
    }

    // The native map keys choices by label, so a duplicate would silently replace the
    // earlier entry in place and change which group the dialog offers.
    private static void ValidateChoices(IReadOnlyList<FileTypeChoice> choices)
    {
        var labels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var choice in choices)
        {
            if (choice is null)
                throw new ArgumentException("File type choices cannot contain null entries.", "options");
            if (string.IsNullOrWhiteSpace(choice.Label))
                throw new ArgumentException("Each file type choice requires a label.", "options");
            if (choice.Extensions is not { Count: > 0 })
                throw new ArgumentException($"File type choice '{choice.Label}' requires at least one extension.", "options");
            if (!labels.Add(choice.Label))
                throw new ArgumentException($"File type choice '{choice.Label}' is listed more than once.", "options");
        }
    }
}

internal interface IPickerService
{
    Task<PickFileResult?> PickFileAsync(WindowId owner, FilePickerOptions options);
    Task<IReadOnlyList<PickFileResult>> PickMultipleFilesAsync(WindowId owner, FilePickerOptions options);
    Task<PickFolderResult?> PickFolderAsync(WindowId owner, FolderPickerOptions options);
    Task<IReadOnlyList<PickFolderResult>> PickMultipleFoldersAsync(WindowId owner, FolderPickerOptions options);
    Task<PickFileResult?> PickSaveFileAsync(WindowId owner, SaveFilePickerOptions options);
}

internal sealed class DefaultPickerService : IPickerService
{
    public async Task<PickFileResult?> PickFileAsync(WindowId owner, FilePickerOptions options)
        => await CreateFileOpenPicker(owner, options).PickSingleFileAsync();

    // Return the WinRT projection as-is instead of copying it into another collection,
    // which would require extra ABI generation in AOT apps.
    public async Task<IReadOnlyList<PickFileResult>> PickMultipleFilesAsync(WindowId owner, FilePickerOptions options)
        => await CreateFileOpenPicker(owner, options).PickMultipleFilesAsync() ?? [];

    public async Task<PickFolderResult?> PickFolderAsync(WindowId owner, FolderPickerOptions options)
        => await CreateFolderPicker(owner, options).PickSingleFolderAsync();

    public async Task<IReadOnlyList<PickFolderResult>> PickMultipleFoldersAsync(WindowId owner, FolderPickerOptions options)
        => await CreateFolderPicker(owner, options).PickMultipleFoldersAsync() ?? [];

    public async Task<PickFileResult?> PickSaveFileAsync(WindowId owner, SaveFilePickerOptions options)
        => await CreateFileSavePicker(owner, options).PickSaveFileAsync();

    internal static FileOpenPicker CreateFileOpenPicker(WindowId owner, FilePickerOptions options)
    {
        var picker = new FileOpenPicker(owner)
        {
            SuggestedStartLocation = options.SuggestedStartLocation,
            CommitButtonText = options.CommitButtonText ?? string.Empty,
            SettingsIdentifier = options.SettingsIdentifier ?? string.Empty,
            Title = options.Title ?? string.Empty,
            SuggestedFolder = options.SuggestedFolder ?? string.Empty,
            SuggestedStartFolder = options.SuggestedStartFolder ?? string.Empty,
        };
        if (options.ViewMode is { } viewMode)
            picker.ViewMode = viewMode;
        // An empty filter already means "All Files" in the native picker.
        if (options.FileTypeFilter is { } filters)
        {
            foreach (var filter in filters)
                picker.FileTypeFilter.Add(filter);
        }
        if (options.FileTypeChoices is { } choices)
            AddChoices(picker.FileTypeChoices, choices);
        if (options.InitialFileTypeIndex is { } index)
            picker.InitialFileTypeIndex = index;
        return picker;
    }

    internal static FolderPicker CreateFolderPicker(WindowId owner, FolderPickerOptions options)
    {
        var picker = new FolderPicker(owner)
        {
            SuggestedStartLocation = options.SuggestedStartLocation,
            CommitButtonText = options.CommitButtonText ?? string.Empty,
            SettingsIdentifier = options.SettingsIdentifier ?? string.Empty,
            Title = options.Title ?? string.Empty,
            SuggestedFolder = options.SuggestedFolder ?? string.Empty,
            SuggestedStartFolder = options.SuggestedStartFolder ?? string.Empty,
        };
        if (options.ViewMode is { } viewMode)
            picker.ViewMode = viewMode;
        return picker;
    }

    internal static FileSavePicker CreateFileSavePicker(WindowId owner, SaveFilePickerOptions options)
    {
        var picker = new FileSavePicker(owner)
        {
            SuggestedStartLocation = options.SuggestedStartLocation,
            SuggestedFileName = options.SuggestedFileName ?? string.Empty,
            CommitButtonText = options.CommitButtonText ?? string.Empty,
            SettingsIdentifier = options.SettingsIdentifier ?? string.Empty,
            Title = options.Title ?? string.Empty,
            SuggestedFolder = options.SuggestedFolder ?? string.Empty,
            SuggestedStartFolder = options.SuggestedStartFolder ?? string.Empty,
            DefaultFileExtension = options.DefaultFileExtension ?? string.Empty,
            ShowOverwritePrompt = options.ShowOverwritePrompt,
        };
        AddChoices(picker.FileTypeChoices, options.FileTypeChoices);
        if (options.InitialFileTypeIndex is { } index)
            picker.InitialFileTypeIndex = index;
        return picker;
    }

    // The native map preserves insertion order, which sets the order of the dialog's
    // file-type list and therefore its default entry.
    private static void AddChoices(IDictionary<string, IList<string>> target, IReadOnlyList<FileTypeChoice> choices)
    {
        foreach (var choice in choices)
            target.Add(choice.Label, new List<string>(choice.Extensions));
    }
}