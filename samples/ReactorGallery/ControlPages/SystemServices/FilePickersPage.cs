using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using static Microsoft.UI.Reactor.Factories;
using static WinUIGalleryReactor.SamplePageHost;

namespace WinUIGalleryReactor.ControlPages.SystemServices;

class FilePickersPage : Component
{
    public override Element Render()
    {
        var (file, setFile) = UseState("No file picked yet.");
        var (files, setFiles) = UseState("No files picked yet.");
        var (folders, setFolders) = UseState("No folder picked yet.");
        var (saved, setSaved) = UseState("Nothing saved yet.");

        return ScrollView(VStack(16,
            PageHeader("File pickers",
                "Open the native Windows file, folder, and save dialogs from an event handler. Pickers are owned by the component's window and return plain paths; cancelling returns null or an empty list rather than throwing."),

            SampleCard("Pick a file",
                VStack(8,
                    Button("Open a text file...", () => Guard(setFile, async () =>
                    {
                        var picked = await PickFileAsync(new FilePickerOptions(FileTypeFilter: [".txt", ".md", ".json"]));
                        if (picked is null) { setFile("Cancelled."); return; }
                        var length = new global::System.IO.FileInfo(picked.Path).Length;
                        setFile($"{picked.Path} ({length:N0} bytes)");
                    })),
                    TextBlock(file).Foreground(Theme.SecondaryText).TextWrapping()),
                sourceCode: @"
var (file, setFile) = UseState(""No file picked yet."");

Button(""Open a text file..."", async () =>
{
    var picked = await PickFileAsync(new FilePickerOptions(FileTypeFilter: ["".txt"", "".md"", "".json""]));
    if (picked is null) { setFile(""Cancelled.""); return; }
    try
    {
        var length = new FileInfo(picked.Path).Length;
        setFile($""{picked.Path} ({length:N0} bytes)"");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        // An async lambda passed to Button is async void: handle file-access failures here.
        setFile($""Failed: {ex.Message}"");
    }
})
"),

            SampleCard("Pick multiple files with labelled file types",
                VStack(8,
                    Button("Choose images...", () => Guard(setFiles, async () =>
                    {
                        var picked = await PickMultipleFilesAsync(new FilePickerOptions
                        {
                            Title = "Choose images",
                            FileTypeChoices =
                            [
                                new("Images", [".png", ".jpg", ".jpeg", ".gif"]),
                                new("Icons", [".ico"]),
                            ],
                        });
                        setFiles(picked.Count == 0
                            ? "Cancelled."
                            : string.Join("\n", picked.Select(f => global::System.IO.Path.GetFileName(f.Path))));
                    })),
                    TextBlock(files).Foreground(Theme.SecondaryText).TextWrapping()),
                sourceCode: @"
var (files, setFiles) = UseState(""No files picked yet."");

Button(""Choose images..."", async () =>
{
    var picked = await PickMultipleFilesAsync(new FilePickerOptions
    {
        Title = ""Choose images"",
        // Ordered, labelled groups; they replace FileTypeFilter when present.
        FileTypeChoices =
        [
            new(""Images"", ["".png"", "".jpg"", "".jpeg"", "".gif""]),
            new(""Icons"", ["".ico""]),
        ],
    });
    setFiles(picked.Count == 0   // cancel returns an empty list
        ? ""Cancelled.""
        : string.Join(""\n"", picked.Select(f => Path.GetFileName(f.Path))));
})
"),

            SampleCard("Pick one or more folders",
                VStack(8,
                    HStack(8,
                        Button("Select a folder...", () => Guard(setFolders, async () =>
                        {
                            var picked = await PickFolderAsync(new FolderPickerOptions(CommitButtonText: "Use this folder"));
                            setFolders(picked?.Path ?? "Cancelled.");
                        })),
                        Button("Select folders...", () => Guard(setFolders, async () =>
                        {
                            var picked = await PickMultipleFoldersAsync(new FolderPickerOptions());
                            setFolders(picked.Count == 0 ? "Cancelled." : string.Join("\n", picked.Select(f => f.Path)));
                        }))),
                    TextBlock(folders).Foreground(Theme.SecondaryText).TextWrapping()),
                sourceCode: @"
var (folders, setFolders) = UseState(""No folder picked yet."");

HStack(8,
    Button(""Select a folder..."", async () =>
    {
        var picked = await PickFolderAsync(new FolderPickerOptions(CommitButtonText: ""Use this folder""));
        setFolders(picked?.Path ?? ""Cancelled."");
    }),
    Button(""Select folders..."", async () =>
    {
        var picked = await PickMultipleFoldersAsync(new FolderPickerOptions());
        setFolders(picked.Count == 0 ? ""Cancelled."" : string.Join(""\n"", picked.Select(f => f.Path)));
    }))
"),

            SampleCard("Save a file",
                VStack(8,
                    Button("Save as...", () => Guard(setSaved, async () =>
                    {
                        var target = await PickSaveFileAsync(new SaveFilePickerOptions(
                            [new("Text", [".txt"]), new("Markdown", [".md"])],
                            SuggestedFileName: "reactor-notes"));
                        if (target is null) { setSaved("Cancelled."); return; }
                        await global::System.IO.File.WriteAllTextAsync(target.Path, $"Saved from the Reactor gallery at {DateTime.Now:t}.");
                        setSaved($"Wrote {target.Path}");
                    })),
                    TextBlock(saved).Foreground(Theme.SecondaryText).TextWrapping(),
                    Caption("The save picker only chooses a path. Writing the file is up to you.")
                        .Foreground(Theme.SecondaryText)),
                sourceCode: @"
var (saved, setSaved) = UseState(""Nothing saved yet."");

Button(""Save as..."", async () =>
{
    // At least one FileTypeChoice is required; the first is the default.
    var target = await PickSaveFileAsync(new SaveFilePickerOptions(
        [new(""Text"", ["".txt""]), new(""Markdown"", ["".md""])],
        SuggestedFileName: ""reactor-notes""));
    if (target is null) { setSaved(""Cancelled.""); return; }

    // Selecting a target is not a save: write the content yourself.
    try
    {
        await File.WriteAllTextAsync(target.Path, $""Saved from the Reactor gallery at {DateTime.Now:t}."");
        setSaved($""Wrote {target.Path}"");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        // An async lambda passed to Button is async void: handle failures here.
        setSaved($""Failed: {ex.Message}"");
    }
})
")
        ).Margin(36, 24, 36, 36));
    }

    /// <summary>
    /// Runs a picker handler and reports failures in the card instead of letting them escape
    /// an <c>async void</c> handler. Cancellation is not an error; real picker failures are.
    /// </summary>
    static async void Guard(Action<string> report, Func<Task> handler)
    {
        try
        {
            await handler();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException
            or global::System.Runtime.InteropServices.COMException
            or global::System.IO.IOException or UnauthorizedAccessException)
        {
            report($"Failed: {ex.Message}");
        }
    }
}
