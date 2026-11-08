// ReactorRegedit — a registry editor built with Reactor + WinUI 3.
//
// Shell (menus, address bar, InfoBar, dialogs, status bar) is declarative Reactor.
// The two dense panes are native "islands" hosted with XamlHost: a flattened
// ListView tree (Native/RegistryTreeView) and a multi-select value list
// (Native/ValueListView). Registry access lives in Services/, pure logic in Core/.

using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ReactorRegedit;
using ReactorRegedit.Components;
using ReactorRegedit.Core;
using ReactorRegedit.Models;
using ReactorRegedit.Native;
using ReactorRegedit.Services;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using DispatcherQueuePriority = Microsoft.UI.Dispatching.DispatcherQueuePriority;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

// Crash breadcrumbs for testers: %LOCALAPPDATA%\ReactorRegedit\crash.log.
ReactorApplication.OnUnhandledException = ex =>
{
    try
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReactorRegedit");
        Directory.CreateDirectory(dir);
        File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.Now:O}] {ex}\r\n\r\n");
    }
    catch (IOException) { }
    return false;
};

ReactorApp.Run<RegeditApp>(
    new WindowSpec { Title = "Registry Editor", Width = 1280, Height = 800 }.WithPersistence("ReactorRegedit.Main"),
    configure: host =>
    {
        AppTheme.ApplyCompactDensity();
        CursorBorderRegistration.Register(host.Reconciler);
    });

internal enum Pane { Tree, Values }

internal sealed record InfoMessage(int Id, InfoBarSeverity Severity, string Message, string? ActionText = null, Action? OnAction = null);


internal sealed record DialogSlot(int Id, Element Element);

internal sealed record LoadedValues(string Path, RegValue[] Values);

// ─── Root component ───────────────────────────────────────────────────────────

internal sealed class RegeditApp : Component
{
    public override Element Render()
    {
        // ── State ────────────────────────────────────────────────────
        var (currentPath, setCurrentPath) = UseState("");
        var (loaded, setLoaded) = UseState(new LoadedValues("", []));
        var (valuesVersion, setValuesVersion) = UseState(0);
        var (addressText, setAddressText) = UseState(RegPath.ComputerLabel);
        var (info, setInfo) = UseState<InfoMessage?>(null);
        var (dialog, setDialog) = UseState<DialogSlot?>(null);
        var (selectedValueCount, setSelectedValueCount) = UseState(0);
        var (favorites, setFavorites) = UseState<Favorite[]>([]);
        var (findText, setFindText) = UseState("");
        var (findFlags, setFindFlags) = UseState(FindFlags.Keys | FindFlags.Values | FindFlags.Data);
        var searchCts = UseRef<CancellationTokenSource?>(null);
        var (showAddressBar, setShowAddressBar) = UsePersisted("showAddressBar", true, PersistedScope.Window);
        var (showStatusBar, setShowStatusBar) = UsePersisted("showStatusBar", true, PersistedScope.Window);

        var treeRef = UseRef<RegistryTreeView?>(null);
        var listRef = UseRef<ValueListView?>(null);
        var addressRef = UseRef<TextBox?>(null);
        var activePane = UseRef(Pane.Tree);
        var idCounter = UseRef(0);
        var versionCounter = UseRef(0);
        var afterValuesLoaded = UseRef<Action<ValueListView>?>(null);
        var currentPathRef = UseRef("");
        currentPathRef.Current = currentPath;

        var dispatcher = DispatcherQueue.GetForCurrentThread();
        var window = UseWindow();
        var settingsRef = UseRef<SessionSettings?>(null);
        var settings = settingsRef.Current ??= SessionSettings.Load();

        // ── Session: restore classic regedit's LastKey on launch, write it back on close ─
        UseEffect(() =>
        {
            var last = SessionSettings.ReadLastKey();
            if (last is { Length: > 0 })
                dispatcher.TryEnqueue(DispatcherQueuePriority.Low, async () =>
                {
                    if (treeRef.Current is not { } tree) return;
                    // Like classic regedit, fall back to the deepest ancestor that still exists.
                    for (var p = last; p.Length > 0; p = RegPath.Parent(p))
                    {
                        var resolved = await Task.Run(() =>
                        {
                            try { return RegistryService.KeyExists(p) ? RegistryService.ResolveCase(p) : null; }
                            catch (RegistryOperationException) { return null; }
                        });
                        if (resolved is not null && await tree.RevealAsync(resolved, focus: true)) return;
                    }
                });
        }, "restore-last-key");
        UseClosingGuard(() =>
        {
            SessionSettings.WriteLastKey(currentPathRef.Current);
            settings.Save();
            return true;
        });

        // ── Favorites: shared with classic regedit, reloaded when either editor changes them ─
        UseEffect(() =>
        {
            setFavorites(FavoritesStore.Load());
            var watch = FavoritesStore.Watch(() => dispatcher.TryEnqueue(() => setFavorites(FavoritesStore.Load())));
            return watch.Dispose;
        }, "favorites");

        // ── Value loading (re-runs on key change or explicit reload) ─
        UseEffect(() =>
        {
            var path = currentPath;
            var cancelled = false;
            if (path.Length == 0)
            {
                setLoaded(new LoadedValues("", []));
                return () => { };
            }
            Task.Run(() =>
            {
                try
                {
                    var values = RegistryService.GetValues(path);
                    dispatcher.TryEnqueue(() => { if (!cancelled) setLoaded(new LoadedValues(path, values)); });
                }
                catch (Exception ex)
                {
                    dispatcher.TryEnqueue(() =>
                    {
                        if (cancelled) return;
                        setLoaded(new LoadedValues(path, []));
                        if (ex is RegistryOperationException { IsNotFound: true } && treeRef.Current is { } t)
                            _ = t.HandleVanishedAsync(path);
                        else
                            ShowError(ErrorText(ex));
                    });
                }
            });
            return () => cancelled = true;
        }, currentPath, valuesVersion);

        // ── Helpers ──────────────────────────────────────────────────
        void ShowMessage(InfoBarSeverity severity, string message) =>
            setInfo(new InfoMessage(++idCounter.Current, severity, message));
        void ShowError(string message) => ShowMessage(InfoBarSeverity.Error, message);

        static string ErrorText(Exception ex) => ex switch
        {
            RegistryOperationException r => r.Message,
            UnauthorizedAccessException or System.Security.SecurityException => "Access is denied.",
            _ => ex.Message,
        };

        async Task<(bool Ok, T Result)> TryRun<T>(Func<T> work)
        {
            try { return (true, await Task.Run(work)); }
            catch (Exception ex) { ShowError(ErrorText(ex)); return (false, default!); }
        }

        void ShowDialog(Func<Action, Element> build)
        {
            var id = ++idCounter.Current;
            setDialog(new DialogSlot(id, build(() => setDialog(null))));
        }

        void ReloadValues(Action<ValueListView>? after = null)
        {
            afterValuesLoaded.Current = after;
            setValuesVersion(++versionCounter.Current);
        }

        void OnTreeSelection(string path)
        {
            if (path == currentPathRef.Current) return;
            currentPathRef.Current = path;
            setCurrentPath(path);
            setAddressText(RegPath.ToDisplay(path));
        }

        bool ValuesPaneActive() =>
            activePane.Current == Pane.Values && listRef.Current is { } l && l.SelectedValues.Count > 0;

        // ── Navigation ───────────────────────────────────────────────
        async void NavigateTo(string input)
        {
            var normalized = RegPath.Normalize(input);
            if (normalized is null)
            {
                ShowError($"Cannot find the key \"{input.Trim()}\". Check the hive name (HKLM, HKCU, HKCR, HKU or HKCC).");
                setAddressText(RegPath.ToDisplay(currentPathRef.Current));
                return;
            }
            var (ok, resolved) = await TryRun(() => RegistryService.ResolveCase(normalized));
            if (!ok)
            {
                setAddressText(RegPath.ToDisplay(currentPathRef.Current));
                return;
            }
            if (treeRef.Current is { } tree && !await tree.RevealAsync(resolved, focus: true))
            {
                ShowError($"Cannot find the key {RegPath.ToDisplay(resolved)}.");
                setAddressText(RegPath.ToDisplay(currentPathRef.Current));
                return;
            }
            setAddressText(RegPath.ToDisplay(resolved));
        }

        async void RefreshAll()
        {
            if (treeRef.Current is { } tree) await tree.RefreshAllAsync();
            ReloadValues();
        }

        void CopyKeyName(string? keyPath = null)
        {
            var path = keyPath ?? currentPathRef.Current;
            if (path.Length == 0) return;
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(path);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            ShowMessage(InfoBarSeverity.Informational, $"Copied \"{path}\" to the clipboard.");
        }

        nint WindowHandle() => window is null ? 0 : Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);

        // ── Load / Unload Hive (HKLM / HKU roots, elevated only) ──────
        async void LoadHive()
        {
            var parent = currentPathRef.Current;
            if (HiveRules.LoadBlockedReason(parent, HiveService.IsElevated) is { } reason)
            {
                ShowError($"Load Hive is not available: {reason}.");
                return;
            }
            Microsoft.Windows.Storage.Pickers.PickFileResult? picked;
            try
            {
                picked = await PickFileAsync(new FilePickerOptions
                {
                    Title = Strings.LoadHive.TrimEnd('.'),
                    SettingsIdentifier = "ReactorRegedit.Hives",
                    FileTypeChoices = [new FileTypeChoice(Strings.AllFilesFilter, ["*"])],
                });
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
            {
                ShowError("Cannot open the file dialog: " + ex.Message);
                return;
            }
            if (picked is null) return;
            var file = picked.Path;
            ShowDialog(close => Component<PromptDialog, PromptProps>(new PromptProps(
                Strings.LoadHive.TrimEnd('.'), "Key Name:", "", HiveRules.ValidateHiveName,
                async name =>
                {
                    var (ok, loadedPath) = await TryRun(() => HiveService.Load(parent, name, file));
                    if (!ok || treeRef.Current is not { } tree) return;
                    await tree.RefreshKeyAsync(parent);
                    await tree.RevealAsync(loadedPath, focus: true);
                    ShowMessage(InfoBarSeverity.Success, $"Loaded {file} as {loadedPath}.");
                },
                close)));
        }

        void UnloadHive()
        {
            var path = currentPathRef.Current;
            if (HiveRules.UnloadBlockedReason(path, HiveService.IsElevated, HiveService.LoadedHiveRoots()) is { } reason)
            {
                ShowError($"Unload Hive is not available: {reason}.");
                return;
            }
            ShowDialog(close => Component<ConfirmDialog, ConfirmProps>(new ConfirmProps(
                "Confirm Unload Hive", "Are you sure you want to unload the current key and all of its subkeys?",
                async () =>
                {
                    var (ok, _) = await TryRun(() => { HiveService.Unload(path); return true; });
                    if (!ok || treeRef.Current is not { } tree) return;
                    var parent = RegPath.Parent(path);
                    await tree.RefreshKeyAsync(parent);
                    await tree.RevealAsync(parent, focus: true);
                    ShowMessage(InfoBarSeverity.Success, $"Unloaded {path}.");
                },
                close)));
        }

        // ── Permissions (native security editor, like classic regedit) ─
        void EditPermissions(string? keyPath = null)
        {
            var path = keyPath ?? currentPathRef.Current;
            if (path.Length == 0) return;
            if (KeyPermissions.Edit(WindowHandle(), path) is { } error) ShowError(error);
        }

        // ── Values: modify ───────────────────────────────────────────
        void Modify(RegValue? value, bool binary)
        {
            var path = currentPathRef.Current;
            if (value is null || path.Length == 0) return;
            {
                // Edit what is in the registry now, not the cached row (it may have changed or vanished).
                RegValue? fresh;
                try { fresh = RegistryService.GetValue(path, value.Name); }
                catch (RegistryOperationException ex) { ShowError(ex.Message); ReloadValues(); return; }
                if (fresh is null && !value.IsUnsetDefault)
                {
                    ShowError($"Cannot edit {value.DisplayName}: The value no longer exists.");
                    ReloadValues();
                    return;
                }
                value = fresh ?? value;
            }
            var name = value.Name;
            var type = value.IsUnsetDefault ? RegType.Sz : value.Type;

            async void Save(uint t, byte[] data, string? warning = null)
            {
                var (ok, _) = await TryRun(() =>
                {
                    if (value.IsUnsetDefault) RegistryService.SetValue(path, name, t, data);
                    else RegistryService.ModifyValue(path, name, t, data);
                    return true;
                });
                if (ok && warning is not null) ShowMessage(InfoBarSeverity.Warning, warning);
                ReloadValues(l => l.SelectByName(name, focus: true));
            }

            if (binary || type is not (RegType.Sz or RegType.ExpandSz or RegType.MultiSz or RegType.Dword or RegType.Qword))
            {
                ShowDialog(close => Component<EditBinaryDialog, EditBinaryProps>(new EditBinaryProps(
                    value.DisplayName, RegType.Name(type), value.Data, bytes => Save(type, bytes), close)));
                return;
            }

            switch (type)
            {
                case RegType.Sz:
                case RegType.ExpandSz:
                    ShowDialog(close => Component<EditStringDialog, EditStringProps>(new EditStringProps(
                        value.DisplayName, ValueFormat.DecodeString(value.Data), type == RegType.ExpandSz,
                        text => Save(type, ValueFormat.EncodeString(text)), close)));
                    break;
                case RegType.MultiSz:
                    ShowDialog(close => Component<EditMultiStringDialog, EditMultiStringProps>(new EditMultiStringProps(
                        value.DisplayName, ValueFormat.DecodeMultiString(value.Data),
                        text =>
                        {
                            var lines = ValueFormat.ParseMultiStringEditor(text, out var removed);
                            Save(type, ValueFormat.EncodeMultiString(lines), removed ? Strings.MultiStringEmptyRemoved : null);
                        }, close)));
                    break;
                case RegType.Dword:
                case RegType.Qword:
                    var isQword = type == RegType.Qword;
                    ShowDialog(close => Component<EditNumberDialog, EditNumberProps>(new EditNumberProps(
                        value.DisplayName, isQword, ValueFormat.ReadNumber(value.Data, type),
                        n => Save(type, isQword ? BitConverter.GetBytes(n) : BitConverter.GetBytes((uint)n)), close)));
                    break;
            }
        }

        // ── Values: delete / rename / new ────────────────────────────
        void DeleteValues(IReadOnlyList<RegValue> selection)
        {
            var path = currentPathRef.Current;
            var targets = selection.Where(v => !v.IsUnsetDefault).ToList();
            if (path.Length == 0 || targets.Count == 0) return;
            ShowDialog(close => Component<ConfirmDialog, ConfirmProps>(new ConfirmProps(
                Strings.ConfirmValueDeleteTitle,
                targets.Count == 1 ? Strings.ConfirmDeleteValue : Strings.ConfirmDeleteValues,
                async () =>
                {
                    var (_, failures) = await TryRun(() =>
                    {
                        var errors = new List<string>();
                        foreach (var v in targets)
                        {
                            try { RegistryService.DeleteValue(path, v.Name); }
                            catch (Exception ex) { errors.Add(ErrorText(ex)); }
                        }
                        return errors;
                    });
                    if (failures is { Count: > 0 })
                        ShowError(failures.Count == 1 ? failures[0]
                            : $"Unable to delete all specified values. {failures.Count} of {targets.Count} failed: {failures[0]}");
                    ReloadValues(l => l.FocusList());
                },
                close)));
        }

        async Task<bool> RenameValue(string oldName, string newName)
        {
            var path = currentPathRef.Current;
            var (ok, _) = await TryRun(() => { RegistryService.RenameValue(path, oldName, newName); return true; });
            ReloadValues(l => l.SelectByName(ok ? newName : oldName, focus: true));
            return ok;
        }

        async void NewValue(uint type)
        {
            var path = currentPathRef.Current;
            if (path.Length == 0) return;
            var (ok, name) = await TryRun(() =>
            {
                var n = RegistryService.UniqueValueName(path);
                RegistryService.CreateValue(path, n, type);
                return n;
            });
            if (ok) ReloadValues(l => l.BeginRename(name));
        }

        // ── Keys: new / delete / rename ──────────────────────────────
        async void NewKey()
        {
            var parent = currentPathRef.Current;
            if (parent.Length == 0 || treeRef.Current is not { } tree) return;
            var (ok, created) = await TryRun(() =>
            {
                var n = RegistryService.UniqueKeyName(parent);
                return RegistryService.CreateKey(parent, n);
            });
            if (!ok) return;
            await tree.RefreshKeyAsync(parent);
            if (await tree.RevealAsync(created)) tree.BeginRename(created);
        }

        void DeleteKey(string path)
        {
            if (path.Length == 0) return;
            if (RegPath.IsHive(path))
            {
                ShowError($"Cannot delete {path}: root keys cannot be deleted.");
                return;
            }
            ShowDialog(close => Component<ConfirmDialog, ConfirmProps>(new ConfirmProps(
                Strings.ConfirmKeyDeleteTitle, Strings.ConfirmDeleteKey,
                async () =>
                {
                    var (ok, _) = await TryRun(() => { RegistryService.DeleteKey(path); return true; });
                    var parent = RegPath.Parent(path);
                    if (treeRef.Current is { } tree)
                    {
                        await tree.RefreshKeyAsync(parent);
                        if (ok) await tree.RevealAsync(parent, focus: true);
                    }
                },
                close)));
        }

        async Task<string?> RenameKey(string oldPath, string newName)
        {
            var (ok, newPath) = await TryRun(() => RegistryService.RenameKey(oldPath, newName));
            if (!ok)
            {
                if (treeRef.Current is { } t) await t.RefreshKeyAsync(RegPath.Parent(oldPath));
                return null;
            }
            treeRef.Current?.RenameNode(oldPath, newPath);
            var cur = currentPathRef.Current;
            if (RegPath.IsSameOrDescendant(cur, oldPath))
            {
                var moved = newPath + cur[oldPath.Length..];
                currentPathRef.Current = moved;
                setCurrentPath(moved);
                setAddressText(RegPath.ToDisplay(moved));
            }
            return newPath;
        }

        // ── Find (baseline: Find dialog + F3; F12 adds progress UI) ──
        async void StartSearch(string text, FindFlags flags)
        {
            searchCts.Current?.Cancel();
            var cts = new CancellationTokenSource();
            searchCts.Current = cts;
            var startKey = currentPathRef.Current;
            var startValue = activePane.Current == Pane.Values ? listRef.Current?.PrimarySelection?.Name : null;
            var progressId = ++idCounter.Current;
            long visited = 0;
            string currentKey = "";
            void ShowProgress()
            {
                var n = Interlocked.Read(ref visited);
                setInfo(new InfoMessage(progressId, InfoBarSeverity.Informational,
                    n == 0 ? "Searching the registry…" : $"Searching the registry… {n:N0} keys  {Volatile.Read(ref currentKey)}",
                    Strings.Cancel, () => cts.Cancel()));
            }
            ShowProgress();
            var timer = dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(250);
            timer.Tick += (_, _) => { if (ReferenceEquals(searchCts.Current, cts)) ShowProgress(); };
            timer.Start();
            SearchHit? hit;
            try
            {
                hit = await SearchService.FindNextAsync(startKey, startValue, text, flags, cts.Token,
                    (n, p) => { Interlocked.Exchange(ref visited, n); Volatile.Write(ref currentKey, p); });
            }
            catch (OperationCanceledException)
            {
                if (ReferenceEquals(searchCts.Current, cts)) ShowMessage(InfoBarSeverity.Informational, "Search cancelled.");
                return;
            }
            finally { timer.Stop(); }
            if (!ReferenceEquals(searchCts.Current, cts)) return;
            searchCts.Current = null;
            if (hit is null)
            {
                ShowMessage(InfoBarSeverity.Informational, "Finished searching through the registry.");
                return;
            }
            setInfo(null);
            if (hit.ValueName is { } valueName)
            {
                if (string.Equals(hit.KeyPath, currentPathRef.Current, StringComparison.OrdinalIgnoreCase))
                    listRef.Current?.SelectByName(valueName, focus: true);
                else
                    afterValuesLoaded.Current = l => l.SelectByName(valueName, focus: true);
                activePane.Current = Pane.Values;
                if (treeRef.Current is { } t) await t.RevealAsync(hit.KeyPath);
            }
            else if (treeRef.Current is { } tree)
            {
                activePane.Current = Pane.Tree;
                await tree.RevealAsync(hit.KeyPath, focus: true);
            }
        }

        void OpenFind() => ShowDialog(close => Component<FindDialog, FindProps>(new FindProps(findText, findFlags,
            (text, flags) =>
            {
                setFindText(text);
                setFindFlags(flags);
                StartSearch(text, flags);
            }, close)));

        void FindNext()
        {
            if (findText.Length == 0) OpenFind();
            else StartSearch(findText, findFlags);
        }

        // ── Favorites (persisted in Applets\Regedit\Favorites, shared with classic regedit) ─
        void AddFavorite()
        {
            var path = currentPathRef.Current;
            if (path.Length == 0) return;
            ShowDialog(close => Component<AddFavoriteDialog, AddFavoriteProps>(new AddFavoriteProps(
                RegPath.Leaf(path),
                async name =>
                {
                    var (ok, _) = await TryRun(() => { FavoritesStore.Add(name, path); return true; });
                    if (ok) setFavorites(FavoritesStore.Load());
                },
                close)));
        }

        void RemoveFavorite() => ShowDialog(close => Component<RemoveFavoriteDialog, RemoveFavoriteProps>(new RemoveFavoriteProps(
            favorites.Select(f => f.Name).ToArray(),
            async name =>
            {
                var (ok, _) = await TryRun(() => { FavoritesStore.Remove(name); return true; });
                if (ok) setFavorites(FavoritesStore.Load());
            },
            close)));

        // ── Export (.reg file) ───────────────────────────────────────
        void Export(string? branch = null) => ShowDialog(close => Component<ExportDialog, ExportProps>(new ExportProps(
            branch ?? currentPathRef.Current,
            ExportToFile,
            close)));

        async void ExportToFile(bool all, string branchText)
        {
            string branch = "";
            if (!all)
            {
                var normalized = RegPath.Normalize(branchText);
                try
                {
                    if (normalized is null || normalized.Length == 0) throw new RegistryOperationException("");
                    branch = RegistryService.ResolveCase(normalized);
                    RegistryService.OpenKey(branch).Dispose();
                }
                catch (RegistryOperationException ex)
                {
                    ShowError(ex.IsAccessDenied
                        ? ex.Message
                        : $"Cannot export {branchText.Trim()}: The key does not exist.");
                    return;
                }
            }

            Microsoft.Windows.Storage.Pickers.PickFileResult? picked;
            try
            {
                picked = await PickSaveFileAsync(new SaveFilePickerOptions(
                    [new FileTypeChoice(Strings.RegFilesFilter, [".reg"])],
                    SuggestedFileName: all ? "Registry" : RegPath.Leaf(branch))
                {
                    Title = Strings.ExportTitle,
                    DefaultFileExtension = ".reg",
                    SettingsIdentifier = "ReactorRegedit.RegFiles",
                });
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                ShowError("Cannot open the file dialog: " + ex.Message);
                return;
            }
            if (picked is null) return;
            var file = picked.Path;

            var roots = all ? RegPath.RootNames.ToList() : [branch];
            var cts = new CancellationTokenSource();
            var progressId = ++idCounter.Current;
            int keyCount = 0;
            string currentKey = "";
            void ShowProgress() => setInfo(new InfoMessage(progressId, InfoBarSeverity.Informational,
                $"Exporting… {Volatile.Read(ref keyCount):N0} keys  {Volatile.Read(ref currentKey)}", Strings.Cancel, () => cts.Cancel()));
            ShowProgress();
            var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(250);
            timer.Tick += (_, _) => ShowProgress();
            timer.Start();
            var skipped = new List<string>();
            int written;
            try
            {
                written = await Task.Run(() => RegFileService.ExportToFile(file, roots, cts.Token,
                    (n, p) => { Volatile.Write(ref keyCount, n); Volatile.Write(ref currentKey, p); }, skipped));
            }
            catch (OperationCanceledException)
            {
                ShowMessage(InfoBarSeverity.Informational, "Export cancelled. No file was written.");
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowError($"Cannot export to {file}: {ex.Message}");
                return;
            }
            finally { timer.Stop(); }
            ShowMessage(skipped.Count > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                $"Exported {written:N0} key(s) from {(all ? "the whole registry" : branch)} to {file}." +
                (skipped.Count > 0 ? $" {skipped.Count:N0} key(s) could not be read and were skipped (first: {skipped[0]})." : ""));
        }

        // ── Import (.reg file) ───────────────────────────────────────
        async void Import()
        {
            Microsoft.Windows.Storage.Pickers.PickFileResult? picked;
            try
            {
                picked = await PickFileAsync(new FilePickerOptions
                {
                    Title = Strings.ImportTitle,
                    SettingsIdentifier = "ReactorRegedit.RegFiles",
                    FileTypeChoices =
                    [
                        new FileTypeChoice(Strings.RegFilesFilter, [".reg"]),
                        new FileTypeChoice(Strings.AllFilesFilter, ["*"]),
                    ],
                });
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
            {
                ShowError("Cannot open the file dialog: " + ex.Message);
                return;
            }
            if (picked is null) return;
            var file = picked.Path;
            var fileName = System.IO.Path.GetFileName(file);

            RegFileParseResult parsed;
            try { parsed = await Task.Run(() => RegFileParser.Parse(RegFileParser.Decode(File.ReadAllBytes(file)))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowError($"Cannot import {fileName}: Error opening the file. {ex.Message}");
                return;
            }
            if (parsed.Ops.Count == 0)
            {
                ShowError($"Cannot import {fileName}: " + (parsed.Errors.FirstOrDefault() ?? "The file contains no registry changes."));
                return;
            }

            var keyCount = parsed.Ops.Count(o => o is CreateKeyOp or DeleteKeyOp);
            var valueCount = parsed.Ops.Count(o => o is SetValueOp or DeleteValueOp);
            ShowDialog(close => Component<ConfirmDialog, ConfirmProps>(new ConfirmProps(
                Strings.ImportTitle,
                string.Format(Strings.ConfirmImport, file, keyCount, valueCount),
                () => ApplyImport(fileName, parsed),
                close)));
        }

        async void ApplyImport(string fileName, RegFileParseResult parsed)
        {
            setInfo(new InfoMessage(++idCounter.Current, InfoBarSeverity.Informational, $"Importing {fileName}…"));
            var (ok, result) = await TryRun(() => RegFileService.Apply(parsed.Ops));
            if (!ok) return;
            var failures = parsed.Errors.Concat(result.Failures).ToList();
            if (failures.Count == 0)
            {
                ShowMessage(InfoBarSeverity.Success,
                    $"The keys and values contained in {fileName} have been successfully added to the registry ({result.Applied:N0} change(s)).");
            }
            else
            {
                var details = string.Join("\n", failures.Take(30)) + (failures.Count > 30 ? $"\n… and {failures.Count - 30:N0} more." : "");
                setInfo(new InfoMessage(++idCounter.Current, InfoBarSeverity.Warning,
                    $"Cannot import {fileName}: Not all data was successfully written to the registry. {result.Applied:N0} change(s) applied, {failures.Count:N0} failed. First: {failures[0]}",
                    Strings.Details,
                    () => ShowDialog(close => Component<MessageDialog, MessageProps>(new MessageProps(
                        Strings.ImportErrorsTitle, details, close)))));
            }
            RefreshAll();
        }
        // ── Pane-aware Edit commands ─────────────────────────────────
        void DeleteCommand()
        {
            if (ValuesPaneActive()) DeleteValues(listRef.Current!.SelectedValues);
            else DeleteKey(currentPathRef.Current);
        }

        void RenameCommand()
        {
            if (ValuesPaneActive() && listRef.Current!.PrimarySelection is { } v) listRef.Current.BeginRename(v.Name);
            else if (RegPath.IsHive(currentPathRef.Current) || currentPathRef.Current.Length == 0)
                ShowError("Root keys cannot be renamed.");
            else treeRef.Current?.BeginRename(currentPathRef.Current);
        }

        MenuSpec[] NewMenuSpecs(bool enabled) =>
        [
            new(Strings.NewKey, NewKey, enabled),
            MenuSpec.Separator,
            new(Strings.NewStringValue, () => NewValue(RegType.Sz), enabled),
            new(Strings.NewBinaryValue, () => NewValue(RegType.Binary), enabled),
            new(Strings.NewDwordValue, () => NewValue(RegType.Dword), enabled),
            new(Strings.NewQwordValue, () => NewValue(RegType.Qword), enabled),
            new(Strings.NewMultiStringValue, () => NewValue(RegType.MultiSz), enabled),
            new(Strings.NewExpandableStringValue, () => NewValue(RegType.ExpandSz), enabled),
        ];

        // ── Island handlers ──────────────────────────────────────────
        var treeHandlers = new TreeHandlers
        {
            SelectionChanged = OnTreeSelection,
            DeleteRequested = node => DeleteKey(node.Path),
            RenameRequested = RenameKey,
            Error = ShowError,
            FocusEntered = () => activePane.Current = Pane.Tree,
            BuildContextMenu = node =>
            {
                var isKey = node.Kind == TreeNodeKind.Key;
                var canHaveNew = node.Kind is TreeNodeKind.Key or TreeNodeKind.Hive;
                var path = node.Path;
                return
                [
                    new(node.IsExpanded ? Strings.Collapse : Strings.Expand,
                        () => treeRef.Current?.Toggle(node), node.HasChildren && node.Kind != TreeNodeKind.Computer),
                    new(Strings.NewMenu, Children: NewMenuSpecs(canHaveNew), Enabled: canHaveNew),
                    new(Strings.Find, OpenFind, KeyText: "Ctrl+F"),
                    MenuSpec.Separator,
                    new(Strings.Delete, () => DeleteKey(path), isKey, KeyText: "Del"),
                    new(Strings.Rename, () => treeRef.Current?.BeginRename(path), isKey, KeyText: "F2"),
                    MenuSpec.Separator,
                    new(Strings.Export, () => Export(path)),
                    new(Strings.Permissions, () => EditPermissions(path), node.Kind != TreeNodeKind.Computer),
                    MenuSpec.Separator,
                    new(Strings.CopyKeyName, () => CopyKeyName(path), node.Kind != TreeNodeKind.Computer),
                ];
            },
        };

        var valueHandlers = new ValueListHandlers
        {
            ModifyRequested = Modify,
            DeleteRequested = DeleteValues,
            RenameRequested = RenameValue,
            SelectionChanged = setSelectedValueCount,
            FocusEntered = () => activePane.Current = Pane.Values,
            ColumnsResized = (name, type) => { settings.NameWidth = name; settings.TypeWidth = type; settings.Save(); },
            BuildItemMenu = sel =>
            [
                new(Strings.Modify, () => Modify(sel.FirstOrDefault(), false), sel.Count == 1),
                new(Strings.ModifyBinaryData, () => Modify(sel.FirstOrDefault(), true), sel.Count == 1),
                MenuSpec.Separator,
                new(Strings.Delete, () => DeleteValues(sel), sel.Any(v => !v.IsUnsetDefault), KeyText: "Del"),
                new(Strings.Rename, () => listRef.Current?.BeginRename(sel[0].Name),
                    sel.Count == 1 && !sel[0].IsDefault, KeyText: "F2"),
            ],
            BuildBackgroundMenu = () =>
            [
                new(Strings.NewMenu, Children: NewMenuSpecs(currentPathRef.Current.Length > 0),
                    Enabled: currentPathRef.Current.Length > 0),
            ],
        };

        // ── Menu bar ─────────────────────────────────────────────────
        var hasKey = currentPath.Length > 0;
        var loadHiveReason = HiveRules.LoadBlockedReason(currentPath, HiveService.IsElevated);
        var unloadHiveReason = UseMemo(() => HiveRules.UnloadBlockedReason(currentPath, HiveService.IsElevated,
            RegPath.Parent(currentPath) is "HKEY_LOCAL_MACHINE" or "HKEY_USERS" ? HiveService.LoadedHiveRoots() : []), currentPath, valuesVersion);
        var isRegularKey = hasKey && !RegPath.IsHive(currentPath);
        var menuBar = MenuBar([
            Menu(Strings.FileMenu, [
                MenuItem(Strings.Import, Import),
                MenuItem(Strings.Export, () => Export()),
                MenuSeparator(),
                MenuItem(loadHiveReason is null ? Strings.LoadHive : $"{Strings.LoadHive} ({loadHiveReason})", LoadHive) with { IsEnabled = loadHiveReason is null },
                MenuItem(unloadHiveReason is null ? Strings.UnloadHive : $"{Strings.UnloadHive} ({unloadHiveReason})", UnloadHive) with { IsEnabled = unloadHiveReason is null },
                MenuSeparator(),
                MenuItem(Strings.ConnectNetworkRegistry, () => { }) with { IsEnabled = false },
                MenuItem(Strings.DisconnectNetworkRegistry, () => { }) with { IsEnabled = false },
                MenuSeparator(),
                MenuItem(Strings.Print, () => { }) with { IsEnabled = false },
                MenuSeparator(),
                MenuItem(Strings.Exit, () => Application.Current.Exit()),
            ]),
            Menu(Strings.EditMenu, [
                MenuItem(Strings.Modify, () => Modify(listRef.Current?.PrimarySelection, false)) with
                    { IsEnabled = selectedValueCount == 1 },
                MenuItem(Strings.ModifyBinaryData, () => Modify(listRef.Current?.PrimarySelection, true)) with
                    { IsEnabled = selectedValueCount == 1 },
                MenuSeparator(),
                MenuSubItem(Strings.NewMenu, [
                    MenuItem(Strings.NewKey, NewKey) with { IsEnabled = hasKey },
                    MenuSeparator(),
                    MenuItem(Strings.NewStringValue, () => NewValue(RegType.Sz)) with { IsEnabled = hasKey },
                    MenuItem(Strings.NewBinaryValue, () => NewValue(RegType.Binary)) with { IsEnabled = hasKey },
                    MenuItem(Strings.NewDwordValue, () => NewValue(RegType.Dword)) with { IsEnabled = hasKey },
                    MenuItem(Strings.NewQwordValue, () => NewValue(RegType.Qword)) with { IsEnabled = hasKey },
                    MenuItem(Strings.NewMultiStringValue, () => NewValue(RegType.MultiSz)) with { IsEnabled = hasKey },
                    MenuItem(Strings.NewExpandableStringValue, () => NewValue(RegType.ExpandSz)) with { IsEnabled = hasKey },
                ]),
                MenuSeparator(),
                MenuItem(Strings.Permissions, () => EditPermissions()) with { IsEnabled = hasKey },
                MenuSeparator(),
                MenuItem(Strings.Delete, DeleteCommand) with { IsEnabled = isRegularKey || selectedValueCount > 0 },
                MenuItem(Strings.Rename, RenameCommand) with { IsEnabled = isRegularKey || selectedValueCount == 1 },
                MenuSeparator(),
                MenuItem(Strings.CopyKeyName, () => CopyKeyName()) with { IsEnabled = hasKey },
                MenuSeparator(),
                MenuItem(Strings.Find, OpenFind) with
                    { KeyboardAccelerators = [Accelerator(VirtualKey.F, VirtualKeyModifiers.Control)] },
                MenuItem(Strings.FindNext, FindNext) with
                    { KeyboardAccelerators = [Accelerator(VirtualKey.F3)] },
            ]),
            Menu(Strings.ViewMenu, [
                ToggleMenuItem(Strings.AddressBar, showAddressBar, _ => setShowAddressBar(!showAddressBar)),
                ToggleMenuItem(Strings.StatusBar, showStatusBar, _ => setShowStatusBar(!showStatusBar)),
                MenuSeparator(),
                MenuItem(Strings.Refresh, RefreshAll) with
                    { KeyboardAccelerators = [Accelerator(VirtualKey.F5)] },
                MenuItem(Strings.GoToAddressBar, () => { addressRef.Current?.Focus(FocusState.Keyboard); addressRef.Current?.SelectAll(); }) with
                    { KeyboardAccelerators = [Accelerator(VirtualKey.L, VirtualKeyModifiers.Control)] },
            ]),
            Menu(Strings.FavoritesMenu, [
                MenuItem(Strings.AddToFavorites, AddFavorite) with { IsEnabled = hasKey },
                MenuItem(Strings.RemoveFromFavorites, RemoveFavorite) with { IsEnabled = favorites.Length > 0 },
                .. (favorites.Length > 0 ? new Microsoft.UI.Reactor.Core.MenuFlyoutItemBase[] { MenuSeparator() } : []),
                .. favorites.Select(f => (Microsoft.UI.Reactor.Core.MenuFlyoutItemBase)MenuItem(f.Name, () => NavigateTo(f.Path))),
            ]),
            Menu(Strings.HelpMenu, [
                MenuItem(Strings.About, () => ShowDialog(close => Component<MessageDialog, MessageProps>(
                    new MessageProps(Strings.AboutTitle, Strings.AboutText, close)))),
            ]),
        ]).MinHeight(0).Height(32);

        // ── Address bar ──────────────────────────────────────────────
        var addressBar = When(showAddressBar, () =>
            TextBox(addressText, setAddressText)
                .AutomationName(Strings.AddressBarName)
                .FontSize(13)
                .Margin(4, 0, 4, 2)
                .Set(tb => addressRef.Current = tb)
                .OnKeyDown((_, e) =>
                {
                    if (e.Key == VirtualKey.Enter)
                    {
                        e.Handled = true;
                        NavigateTo(addressText);
                    }
                    else if (e.Key == VirtualKey.Escape)
                    {
                        e.Handled = true;
                        setAddressText(RegPath.ToDisplay(currentPathRef.Current));
                        treeRef.Current?.FocusTree();
                    }
                })
        );

        // ── Info bar (non-modal errors / notices) ────────────────────
        var infoBar = info is null
            ? Empty()
            : (InfoBar(null, info.Message) with
            {
                Severity = info.Severity,
                IsOpen = true,
                ActionButtonContent = info.ActionText,
                OnActionButtonClick = info.OnAction is { } act ? () => { act(); } : null,
                OnClosed = () => setInfo(null),
            }).Margin(4, 0, 4, 2).WithKey($"info-{info.Id}");

        // ── Panes ────────────────────────────────────────────────────
        var tree = XamlHost(
            () => treeRef.Current = new RegistryTreeView(),
            c => ((RegistryTreeView)c).Handlers = treeHandlers,
            typeKey: "registry-tree");

        var valueList = XamlHost(
            () =>
            {
                var created = new ValueListView();
                created.SetColumnWidths(settings.NameWidth, settings.TypeWidth);
                return listRef.Current = created;
            },
            c =>
            {
                var list = (ValueListView)c;
                list.Handlers = valueHandlers;
                list.SetValues(loaded.Path, loaded.Values);
                if (afterValuesLoaded.Current is { } after && string.Equals(list.KeyPath, currentPath, StringComparison.Ordinal))
                {
                    afterValuesLoaded.Current = null;
                    dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () => after(list));
                }
            },
            typeKey: "value-list");

        // ── Status bar ───────────────────────────────────────────────
        var statusBar = When(showStatusBar, () =>
            TextBlock(RegPath.ToDisplay(currentPath))
                .FontSize(12)
                .TextTrimming(TextTrimming.CharacterEllipsis)
                .TextWrapping(TextWrapping.NoWrap)
                .Padding(8, 3, 8, 3)
                .WithBorder(DividerStroke)
        );

        var dialogElement = dialog is null ? Empty() : dialog.Element.WithKey($"dialog-{dialog.Id}");

        // ── Layout ───────────────────────────────────────────────────
        return Grid(
            [GridSize.Star()],
            [GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Star(), GridSize.Auto],
            menuBar.Grid(row: 0, column: 0).WithKey("menu"),
            addressBar.Grid(row: 1, column: 0).WithKey("address"),
            infoBar.Grid(row: 2, column: 0),
            Component<SplitPanel, SplitPanelProps>(new SplitPanelProps(Left: tree, Right: valueList, InitialWidth: settings.TreeWidth,
                OnWidthChanged: w => { settings.TreeWidth = w; settings.Save(); })).Grid(row: 3, column: 0).WithKey("panes"),
            statusBar.Grid(row: 4, column: 0).WithKey("status"),
            dialogElement.Grid(row: 0, column: 0)
        )
        // Spec 033 §6 — Mica window backdrop on a primary tool surface.
        .Backdrop(BackdropKind.Mica);
    }
}
