using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Windows.Storage;
using Windows.Storage.Pickers;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class PickerHooksFixtures
{
    private sealed class PickerComponent : Component
    {
        public FilePickerOptions OpenOptions { get; } = new([".txt", ".md"], PickerLocationId.Downloads, "Read")
        {
            ViewMode = PickerViewMode.Thumbnail,
            SettingsIdentifier = "reactor-open-test",
        };
        public FolderPickerOptions FolderOptions { get; } = new(PickerLocationId.Desktop, "Select")
        {
            FileTypeFilter = [".txt"],
            ViewMode = PickerViewMode.Thumbnail,
            SettingsIdentifier = "reactor-folder-test",
        };
        public SaveFilePickerOptions SaveOptions { get; } = new(
            new Dictionary<string, IReadOnlyList<string>> { ["Text"] = [".txt", ".md"], ["JSON"] = [".json"] },
            "report", PickerLocationId.PicturesLibrary, "Export")
        {
            SettingsIdentifier = "reactor-save-test",
        };
        public Func<Task<StorageFile?>> PickSingle = null!;
        public Func<Task<IReadOnlyList<StorageFile>>> PickMultiple = null!;
        public Func<Task<StorageFolder?>> PickFolder = null!;
        public Func<Task<StorageFile?>> PickSave = null!;

        public override Element Render()
        {
            PickSingle = () => UseFilePickerAsync(OpenOptions);
            PickMultiple = () => UseMultipleFilesPickerAsync(OpenOptions);
            // Exercise the RenderContext entry point as well as Component forwarding.
            PickFolder = () => Context.UseFolderPickerAsync(FolderOptions);
            PickSave = () => UseSaveFilePickerAsync(SaveOptions);
            return TextBlock("picker hooks");
        }
    }

    private sealed class StubPickerService : IPickerService
    {
        public nint Hwnd;
        public object? Options;
        public string? Operation;
        public int Calls;
        public StorageFile? File;
        public StorageFolder? Folder;
        public IReadOnlyList<StorageFile> Files = [];
        public Exception? Error;

        private Task<T> Record<T>(nint hwnd, object options, string operation, T result)
        {
            Hwnd = hwnd;
            Options = options;
            Operation = operation;
            Calls++;
            return Error is null ? Task.FromResult(result) : Task.FromException<T>(Error);
        }

        public Task<StorageFile?> PickFileAsync(nint hwnd, FilePickerOptions options)
            => Record(hwnd, options, "single", File);
        public Task<IReadOnlyList<StorageFile>> PickMultipleFilesAsync(nint hwnd, FilePickerOptions options)
            => Record(hwnd, options, "multiple", Files);
        public Task<StorageFolder?> PickFolderAsync(nint hwnd, FolderPickerOptions options)
            => Record(hwnd, options, "folder", Folder);
        public Task<StorageFile?> PickSaveFileAsync(nint hwnd, SaveFilePickerOptions options)
            => Record(hwnd, options, "save", File);
    }

    internal sealed class Suite(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ReactorApp.UIDispatcher ??= DispatcherQueue.GetForCurrentThread();
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            var previous = RenderContext.PickerService;
            var stub = new StubPickerService();
            var component = new PickerComponent();
            RenderContext.PickerService = stub;
            ReactorWindow? window = null;
            StorageFolder? directory = null;
            try
            {
                window = ReactorApp.OpenWindow(new WindowSpec { Title = "Picker Hooks", Width = 320, Height = 220 }, () => component);
                await window.Host.WaitForIdleAsync();
                await Harness.Render(100);
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window.NativeWindow);
                var temp = await StorageFolder.GetFolderFromPathAsync(global::System.IO.Path.GetTempPath());
                directory = await temp.CreateFolderAsync("ReactorPickerHooks-" + Guid.NewGuid().ToString("N"));
                var first = await directory.CreateFileAsync("first.txt");
                var second = await directory.CreateFileAsync("second.md");
                stub.File = first;
                stub.Folder = directory;
                stub.Files = new List<StorageFile> { first, second };

                var single = await component.PickSingle();
                H.Check("PickerHooks_Single_Routes", stub.Operation == "single" && stub.Hwnd == hwnd && ReferenceEquals(stub.Options, component.OpenOptions));
                H.Check("PickerHooks_Single_Result", ReferenceEquals(single, first));
                var multiple = await component.PickMultiple();
                H.Check("PickerHooks_Multiple_Routes", stub.Operation == "multiple" && stub.Hwnd == hwnd && ReferenceEquals(stub.Options, component.OpenOptions));
                H.Check("PickerHooks_Multiple_PreservesCollection", ReferenceEquals(multiple, stub.Files) && multiple.Count == 2 && multiple[0].Path == first.Path && multiple[1].Path == second.Path);
                var folder = await component.PickFolder();
                H.Check("PickerHooks_Folder_Routes", stub.Operation == "folder" && stub.Hwnd == hwnd && ReferenceEquals(stub.Options, component.FolderOptions));
                H.Check("PickerHooks_Folder_Result", ReferenceEquals(folder, directory));
                var save = await component.PickSave();
                H.Check("PickerHooks_Save_Routes", stub.Operation == "save" && stub.Hwnd == hwnd && ReferenceEquals(stub.Options, component.SaveOptions));
                H.Check("PickerHooks_Save_Result", ReferenceEquals(save, first));

                stub.File = null;
                stub.Folder = null;
                stub.Files = [];
                H.Check("PickerHooks_Single_Cancel", await component.PickSingle() is null);
                H.Check("PickerHooks_Multiple_Cancel", (await component.PickMultiple()).Count == 0);
                H.Check("PickerHooks_Folder_Cancel", await component.PickFolder() is null);
                H.Check("PickerHooks_Save_Cancel", await component.PickSave() is null);

                Func<Task>[] operations = [() => component.PickSingle(), () => component.PickMultiple(), () => component.PickFolder(), () => component.PickSave()];
                for (int i = 0; i < operations.Length; i++)
                {
                    var calls = stub.Calls;
                    bool rejected = false;
                    try { await Task.Run(operations[i]); }
                    catch (InvalidOperationException ex) { rejected = ex.Message.Contains("UI thread", StringComparison.Ordinal); }
                    H.Check($"PickerHooks_OffUiThread_{i}", rejected && stub.Calls == calls);
                    var error = new InvalidOperationException("picker failure");
                    stub.Error = error;
                    Exception? actual = null;
                    try { await operations[i](); }
                    catch (Exception ex) { actual = ex; }
                    H.Check($"PickerHooks_Error_{i}", ReferenceEquals(actual, error));
                    stub.Error = null;
                }

                // Inspect the real pickers without opening OS dialogs. Both open
                // operations use this factory, so options cannot drift between them.
                var open = DefaultPickerService.CreateFileOpenPicker(hwnd, component.OpenOptions);
                H.Check("PickerHooks_NativeOpenOptions", open.FileTypeFilter.SequenceEqual([".txt", ".md"])
                    && open.SuggestedStartLocation == PickerLocationId.Downloads && open.CommitButtonText == "Read"
                    && open.ViewMode == PickerViewMode.Thumbnail && open.SettingsIdentifier == "reactor-open-test");
                var nativeFolder = DefaultPickerService.CreateFolderPicker(hwnd, component.FolderOptions);
                H.Check("PickerHooks_NativeFolderOptions", nativeFolder.FileTypeFilter.SequenceEqual([".txt"])
                    && nativeFolder.SuggestedStartLocation == PickerLocationId.Desktop && nativeFolder.CommitButtonText == "Select"
                    && nativeFolder.ViewMode == PickerViewMode.Thumbnail && nativeFolder.SettingsIdentifier == "reactor-folder-test");
                var nativeSave = DefaultPickerService.CreateFileSavePicker(hwnd, component.SaveOptions);
                H.Check("PickerHooks_NativeSaveOptions", nativeSave.FileTypeChoices.Count == 2
                    && nativeSave.FileTypeChoices["Text"].SequenceEqual([".txt", ".md"])
                    && nativeSave.FileTypeChoices["JSON"].SequenceEqual([".json"])
                    && nativeSave.SuggestedFileName == "report" && nativeSave.CommitButtonText == "Export"
                    && nativeSave.SuggestedStartLocation == PickerLocationId.PicturesLibrary && nativeSave.SettingsIdentifier == "reactor-save-test");
                var suggestedSave = DefaultPickerService.CreateFileSavePicker(hwnd, component.SaveOptions with { SuggestedSaveFile = first });
                H.Check("PickerHooks_NativeSuggestedSaveFile", suggestedSave.SuggestedSaveFile?.Path == first.Path);
                var defaultOpen = DefaultPickerService.CreateFileOpenPicker(hwnd, new());
                var emptyOpen = DefaultPickerService.CreateFileOpenPicker(hwnd, new([]));
                var defaultFolder = DefaultPickerService.CreateFolderPicker(hwnd, new());
                var emptyFolder = DefaultPickerService.CreateFolderPicker(hwnd, new() { FileTypeFilter = [] });
                H.Check("PickerHooks_NativeViewDefault", defaultOpen.ViewMode == PickerViewMode.List);
                H.Check("PickerHooks_NativeFilterDefaults", defaultOpen.FileTypeFilter.SequenceEqual(["*"])
                    && emptyOpen.FileTypeFilter.SequenceEqual(["*"])
                    && defaultFolder.FileTypeFilter.SequenceEqual(["*"])
                    && emptyFolder.FileTypeFilter.SequenceEqual(["*"]));
            }
            finally
            {
                RenderContext.PickerService = previous;
                if (window is not null) window.Close();
                if (directory is not null) await directory.DeleteAsync(StorageDeleteOption.PermanentDelete);
                await Harness.Render(100);
            }
        }
    }
}
