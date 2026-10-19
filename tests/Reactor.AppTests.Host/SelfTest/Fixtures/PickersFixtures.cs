using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Core;
using Microsoft.Windows.Storage.Pickers;
using static Microsoft.UI.Reactor.Factories;

namespace Microsoft.UI.Reactor.AppTests.Host.SelfTest.Fixtures;

internal static class PickersFixtures
{
    private static readonly string[] OperationNames = ["single", "multiple", "folder", "folders", "save"];

    private sealed class PickerComponent : Component
    {
        public FilePickerOptions OpenOptions { get; } = new([".txt", ".md"], PickerLocationId.Downloads, "Read")
        {
            ViewMode = PickerViewMode.Thumbnail,
            SettingsIdentifier = "reactor-open-test",
            Title = "Open a note",
            SuggestedFolder = global::System.IO.Path.GetTempPath(),
            // Deliberately not alphabetical, so a sorted native map would be visible.
            FileTypeChoices = [new("Text", [".txt", ".md"]), new("JSON", [".json"]), new("Archive", [".zip"])],
            InitialFileTypeIndex = 1,
        };
        public FolderPickerOptions FolderOptions { get; } = new(PickerLocationId.Desktop, "Select")
        {
            ViewMode = PickerViewMode.Thumbnail,
            SettingsIdentifier = "reactor-folder-test",
            Title = "Choose a folder",
            SuggestedStartFolder = global::System.IO.Path.GetTempPath(),
        };
        public SaveFilePickerOptions SaveOptions { get; } = new(
            [new("Text", [".txt", ".md"]), new("JSON", [".json"]), new("Archive", [".zip"])],
            "report", PickerLocationId.PicturesLibrary, "Export")
        {
            SettingsIdentifier = "reactor-save-test",
            Title = "Export the report",
            SuggestedFolder = global::System.IO.Path.GetTempPath(),
            DefaultFileExtension = ".txt",
            InitialFileTypeIndex = 2,
            ShowOverwritePrompt = false,
        };

        // One delegate per operation, in OperationNames order.
        public Func<Task>[] Operations = [];
        public Func<Task<PickFileResult?>> PickSingle = null!;
        public Func<Task<IReadOnlyList<PickFileResult>>> PickMultiple = null!;
        public Func<Task<PickFolderResult?>> PickFolder = null!;
        public Func<Task<IReadOnlyList<PickFolderResult>>> PickFolders = null!;
        public Func<Task<PickFileResult?>> PickSave = null!;

        public override Element Render()
        {
            // Component forwarding for files, the RenderContext entry point for folders.
            PickSingle = () => PickFileAsync(OpenOptions);
            PickMultiple = () => PickMultipleFilesAsync(OpenOptions);
            PickFolder = () => Context.PickFolderAsync(FolderOptions);
            PickFolders = () => Context.PickMultipleFoldersAsync(FolderOptions);
            PickSave = () => PickSaveFileAsync(SaveOptions);
            Operations = [() => PickSingle(), () => PickMultiple(), () => PickFolder(), () => PickFolders(), () => PickSave()];
            return TextBlock("pickers");
        }
    }

    private sealed class StubPickerService : IPickerService
    {
        public WindowId Owner;
        public object? Options;
        public string? Operation;
        public int Calls;
        public IReadOnlyList<PickFileResult> Files = new List<PickFileResult>();
        public IReadOnlyList<PickFolderResult> Folders = new List<PickFolderResult>();
        public Exception? Error;

        private Task<T> Record<T>(WindowId owner, object options, string operation, T result)
        {
            Owner = owner;
            Options = options;
            Operation = operation;
            Calls++;
            return Error is null ? Task.FromResult(result) : Task.FromException<T>(Error);
        }

        // PickFileResult/PickFolderResult have no public constructor, so single picks can only
        // be stubbed as cancelled; multiple picks return a distinct list instance to prove the
        // collection is passed through rather than copied.
        public Task<PickFileResult?> PickFileAsync(WindowId owner, FilePickerOptions options)
            => Record<PickFileResult?>(owner, options, "single", null);
        public Task<IReadOnlyList<PickFileResult>> PickMultipleFilesAsync(WindowId owner, FilePickerOptions options)
            => Record(owner, options, "multiple", Files);
        public Task<PickFolderResult?> PickFolderAsync(WindowId owner, FolderPickerOptions options)
            => Record<PickFolderResult?>(owner, options, "folder", null);
        public Task<IReadOnlyList<PickFolderResult>> PickMultipleFoldersAsync(WindowId owner, FolderPickerOptions options)
            => Record(owner, options, "folders", Folders);
        public Task<PickFileResult?> PickSaveFileAsync(WindowId owner, SaveFilePickerOptions options)
            => Record<PickFileResult?>(owner, options, "save", null);
    }

    private static async Task<ReactorWindow> Open(string title, Component component)
    {
        var window = ReactorApp.OpenWindow(new WindowSpec { Title = title, Width = 320, Height = 220 }, () => component);
        await window.Host.WaitForIdleAsync();
        await Harness.Render(100);
        return window;
    }

    private static object ExpectedOptions(PickerComponent component, int operation) => operation switch
    {
        0 or 1 => component.OpenOptions,
        2 or 3 => component.FolderOptions,
        _ => component.SaveOptions,
    };

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
            try
            {
                window = await Open("Pickers", component);
                var owner = window.AppWindow.Id;

                for (int i = 0; i < component.Operations.Length; i++)
                {
                    await component.Operations[i]();
                    H.Check($"Pickers_Routes_{OperationNames[i]}", stub.Operation == OperationNames[i]
                        && stub.Owner == owner && ReferenceEquals(stub.Options, ExpectedOptions(component, i)));
                }

                H.Check("Pickers_Single_Cancel", await component.PickSingle() is null);
                H.Check("Pickers_Folder_Cancel", await component.PickFolder() is null);
                H.Check("Pickers_Save_Cancel", await component.PickSave() is null);
                var files = await component.PickMultiple();
                H.Check("Pickers_MultipleFiles_PassesCollectionThrough", ReferenceEquals(files, stub.Files) && files.Count == 0);
                var folders = await component.PickFolders();
                H.Check("Pickers_MultipleFolders_PassesCollectionThrough", ReferenceEquals(folders, stub.Folders) && folders.Count == 0);

                for (int i = 0; i < component.Operations.Length; i++)
                {
                    var calls = stub.Calls;
                    bool rejected = false;
                    try { await Task.Run(component.Operations[i]); }
                    catch (InvalidOperationException ex) { rejected = ex.Message.Contains("UI thread", StringComparison.Ordinal); }
                    H.Check($"Pickers_OffUiThread_{OperationNames[i]}", rejected && stub.Calls == calls);
                    var error = new InvalidOperationException("picker failure");
                    stub.Error = error;
                    Exception? actual = null;
                    try { await component.Operations[i](); }
                    catch (Exception ex) { actual = ex; }
                    H.Check($"Pickers_Error_{OperationNames[i]}", ReferenceEquals(actual, error));
                    stub.Error = null;
                }

                // Inspect the real pickers without opening OS dialogs. Both open operations
                // (and both folder operations) share one factory, so options cannot drift.
                var temp = global::System.IO.Path.GetTempPath();
                var open = DefaultPickerService.CreateFileOpenPicker(owner, component.OpenOptions);
                H.Check("Pickers_NativeOpenOptions", open.FileTypeFilter.SequenceEqual([".txt", ".md"])
                    && open.SuggestedStartLocation == PickerLocationId.Downloads && open.CommitButtonText == "Read"
                    && open.ViewMode == PickerViewMode.Thumbnail && open.SettingsIdentifier == "reactor-open-test"
                    && open.Title == "Open a note" && open.SuggestedFolder == temp && open.SuggestedStartFolder == ""
                    && open.InitialFileTypeIndex == 1);
                H.Check("Pickers_NativeOpenChoicesOrdered",
                    open.FileTypeChoices.Select(c => c.Key).SequenceEqual(["Text", "JSON", "Archive"])
                    && open.FileTypeChoices["Text"].SequenceEqual([".txt", ".md"]));
                var nativeFolder = DefaultPickerService.CreateFolderPicker(owner, component.FolderOptions);
                H.Check("Pickers_NativeFolderOptions", nativeFolder.SuggestedStartLocation == PickerLocationId.Desktop
                    && nativeFolder.CommitButtonText == "Select" && nativeFolder.ViewMode == PickerViewMode.Thumbnail
                    && nativeFolder.SettingsIdentifier == "reactor-folder-test" && nativeFolder.Title == "Choose a folder"
                    && nativeFolder.SuggestedStartFolder == temp && nativeFolder.SuggestedFolder == "");
                var nativeSave = DefaultPickerService.CreateFileSavePicker(owner, component.SaveOptions);
                H.Check("Pickers_NativeSaveOptions", nativeSave.SuggestedFileName == "report"
                    && nativeSave.CommitButtonText == "Export" && nativeSave.SuggestedStartLocation == PickerLocationId.PicturesLibrary
                    && nativeSave.SettingsIdentifier == "reactor-save-test" && nativeSave.Title == "Export the report"
                    && nativeSave.SuggestedFolder == temp && nativeSave.DefaultFileExtension == ".txt"
                    && nativeSave.InitialFileTypeIndex == 2 && !nativeSave.ShowOverwritePrompt);
                H.Check("Pickers_NativeSaveChoicesOrdered",
                    nativeSave.FileTypeChoices.Select(c => c.Key).SequenceEqual(["Text", "JSON", "Archive"])
                    && nativeSave.FileTypeChoices["Text"].SequenceEqual([".txt", ".md"])
                    && nativeSave.FileTypeChoices["Archive"].SequenceEqual([".zip"]));

                var defaultOpen = DefaultPickerService.CreateFileOpenPicker(owner, new());
                var defaultSave = DefaultPickerService.CreateFileSavePicker(owner, new([new("Text", [".txt"])]));
                H.Check("Pickers_NativeOpenDefaults", defaultOpen.FileTypeFilter.Count == 0
                    && defaultOpen.FileTypeChoices.Count == 0 && defaultOpen.ViewMode == PickerViewMode.List
                    && defaultOpen.SuggestedStartLocation == PickerLocationId.DocumentsLibrary);
                H.Check("Pickers_NativeSaveDefaults", defaultSave.ShowOverwritePrompt && defaultSave.DefaultFileExtension == "");
            }
            finally
            {
                RenderContext.PickerService = previous;
                window?.Close();
                await Harness.Render(100);
            }
        }
    }

    /// <summary>
    /// Issue #1374: the owner is the window that rendered the component, not whichever host
    /// <c>ReactorApp.ActiveHostInternal</c> names when the handler runs. With A then B open,
    /// that field names B; once B is disposed it is null.
    /// </summary>
    internal sealed class OwningWindow(Harness h) : SelfTestFixtureBase(h)
    {
        public override async Task RunAsync()
        {
            ReactorApp.UIDispatcher ??= DispatcherQueue.GetForCurrentThread();
            ReactorApp.ShutdownPolicy = ShutdownPolicy.Explicit;
            var previous = RenderContext.PickerService;
            var stub = new StubPickerService();
            RenderContext.PickerService = stub;
            var a = new PickerComponent();
            var b = new PickerComponent();
            ReactorWindow? windowA = null, windowB = null;
            try
            {
                windowA = await Open("Pickers A", a);
                windowB = await Open("Pickers B", b);
                var ownerA = windowA.AppWindow.Id;
                var ownerB = windowB.AppWindow.Id;
                // Oracle guards: the two owners differ, and the process-wide host is B, so
                // routing A's calls to A is not something a call-time lookup could produce.
                H.Check("Pickers_OwningWindow_DistinctOwners", ownerA != ownerB);
                H.Check("Pickers_OwningWindow_ActiveHostIsB", ReferenceEquals(ReactorApp.ActiveHostInternal?.OwningWindow, windowB));

                for (int i = 0; i < a.Operations.Length; i++)
                {
                    await a.Operations[i]();
                    H.Check($"Pickers_OwningWindow_BeforeClose_{OperationNames[i]}", stub.Operation == OperationNames[i] && stub.Owner == ownerA);
                }
                await b.Operations[0]();
                H.Check("Pickers_OwningWindow_BRoutesToB", stub.Owner == ownerB);

                windowB.Close();
                await Harness.Render(100);
                H.Check("Pickers_OwningWindow_ActiveHostClearedByClose", ReactorApp.ActiveHostInternal is null);

                for (int i = 0; i < a.Operations.Length; i++)
                {
                    var calls = stub.Calls;
                    Exception? error = null;
                    try { await a.Operations[i](); }
                    catch (Exception ex) { error = ex; }
                    H.Check($"Pickers_OwningWindow_AfterClose_{OperationNames[i]}",
                        error is null && stub.Calls == calls + 1 && stub.Operation == OperationNames[i] && stub.Owner == ownerA);
                }

                // B's component outlived its window: its pickers must refuse rather than
                // parent a dialog to a closed (or unrelated) window.
                for (int i = 0; i < b.Operations.Length; i++)
                {
                    var calls = stub.Calls;
                    string? message = null;
                    try { await b.Operations[i](); }
                    catch (InvalidOperationException ex) { message = ex.Message; }
                    H.Check($"Pickers_OwningWindow_ClosedOwnerRejected_{OperationNames[i]}",
                        message is not null && message.Contains("owning ReactorWindow", StringComparison.Ordinal) && stub.Calls == calls);
                }
            }
            finally
            {
                RenderContext.PickerService = previous;
                windowB?.Close();
                windowA?.Close();
                await Harness.Render(100);
            }
        }
    }
}
