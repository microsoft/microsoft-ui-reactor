// ReactorRegedit — A registry editor built with Reactor + WinUI 3.
// Ports the native Win32 regedit.exe to a declarative component model.

using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using ReactorRegedit;
using ReactorRegedit.Components;
using ReactorRegedit.Components.Dialogs;
using ReactorRegedit.Models;
using ReactorRegedit.Services;
using Microsoft.UI.Xaml;
using Microsoft.Win32;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

ReactorApp.Run<RegeditApp>("Registry Editor", width: 1000, height: 600,
    configure: host => CursorBorderRegistration.Register(host.Reconciler));

// ─── Root component ───────────────────────────────────────────────────────────

class RegeditApp : Component
{
    public override Element Render()
    {
        // ── Core state ───────────────────────────────────────────────
        var (currentKeyPath, setCurrentKeyPath) = UseState("");
        var (values, setValues) = UseState<RegistryValueEntry[]>([]);
        var (selectedValueIndex, setSelectedValueIndex) = UseState(-1);
        var (showAddressBar, setShowAddressBar) = UsePersisted("showAddressBar", true, PersistedScope.Window);
        var (showStatusBar, setShowStatusBar) = UsePersisted("showStatusBar", true, PersistedScope.Window);
        var (addressBarText, setAddressBarText) = UseState("");
        var (isLoading, setIsLoading) = UseState(false);

        // Tree state: combined for single-render updates
        var (treeState, setTreeState) = UseState((
            Expanded: new HashSet<string>(),
            Children: new Dictionary<string, RegistryKeyEntry[]>()
        ));

        // Dialog state
        var (activeDialog, setActiveDialog) = UseState(ActiveDialog.None);
        var (editValueName, setEditValueName) = UseState("");
        var (editValueData, setEditValueData) = UseState("");
        var (editNumberBase, setEditNumberBase) = UseState(NumberBase.Hexadecimal);
        var (editValueKind, setEditValueKind) = UseState(RegistryValueKind.String);

        // Find state
        var (searchText, setSearchText) = UseState("");
        var (findFlags, setFindFlags) = UseState(FindFlags.Keys | FindFlags.Values | FindFlags.Data);
        var searchCtsRef = UseRef<CancellationTokenSource?>(null);

        // Favorites state
        var (favorites, setFavorites) = UseState(new Dictionary<string, string>());
        var (favoriteName, setFavoriteName) = UseState("");
        var (selectedFavoriteIndex, setSelectedFavoriteIndex) = UseState(-1);

        // Export state
        var (exportAll, setExportAll) = UseState(false);

        // Status bar
        var (statusText, setStatusText) = UseState(Strings.Ready);

        // ── Load values on key path change ───────────────────────────
        UseEffect((Action)(() =>
        {
            if (string.IsNullOrEmpty(currentKeyPath)) return;

            setAddressBarText(currentKeyPath);
            setIsLoading(true);
            var syncContext = SynchronizationContext.Current;

            Task.Run(async () =>
            {
                var result = await RegistryService.GetValuesAsync(currentKeyPath);
                syncContext?.Post(_ =>
                {
                    setValues(result);
                    setSelectedValueIndex(-1);
                    setIsLoading(false);
                }, null);
            });
        }), currentKeyPath);

        // ── Handlers ─────────────────────────────────────────────────
        void NavigateToKey(string path)
        {
            setCurrentKeyPath(path);
        }

        void ExpandTreeNode(string path)
        {
            if (treeState.Expanded.Contains(path)) return;

            var syncContext = SynchronizationContext.Current;
            Task.Run(async () =>
            {
                var subkeys = await RegistryService.GetSubKeysAsync(path);
                syncContext?.Post(_ =>
                {
                    var newChildren = new Dictionary<string, RegistryKeyEntry[]>(treeState.Children)
                    {
                        [path] = subkeys
                    };
                    var newExpanded = new HashSet<string>(treeState.Expanded) { path };
                    setTreeState((Expanded: newExpanded, Children: newChildren));
                }, null);
            });
        }

        void RefreshCurrentKey()
        {
            if (string.IsNullOrEmpty(currentKeyPath)) return;

            var syncContext = SynchronizationContext.Current;
            setIsLoading(true);

            Task.Run(async () =>
            {
                var newValues = await RegistryService.GetValuesAsync(currentKeyPath);
                // Also refresh subkeys
                var subkeys = await RegistryService.GetSubKeysAsync(currentKeyPath);
                syncContext?.Post(_ =>
                {
                    setValues(newValues);
                    var newChildren = new Dictionary<string, RegistryKeyEntry[]>(treeState.Children)
                    {
                        [currentKeyPath] = subkeys
                    };
                    setTreeState((Expanded: treeState.Expanded, Children: newChildren));
                    setIsLoading(false);
                }, null);
            });
        }

        void AddressBarSubmit(string path)
        {
            if (!string.IsNullOrEmpty(path))
                NavigateToKey(path);
        }

        // ── New key/value handlers ───────────────────────────────────
        void CreateNewKey()
        {
            if (string.IsNullOrEmpty(currentKeyPath)) return;
            var syncContext = SynchronizationContext.Current;
            Task.Run(async () =>
            {
                // Find unique name
                var baseName = "New Key #";
                var idx = 1;
                var name = baseName + idx;
                using var parent = RegistryService.OpenKey(currentKeyPath);
                if (parent is not null)
                {
                    while (parent.OpenSubKey(name) is not null) { idx++; name = baseName + idx; }
                }
                var result = await RegistryService.CreateKeyAsync(currentKeyPath, name);
                syncContext?.Post(_ => RefreshCurrentKey(), null);
            });
        }

        void CreateNewValue(RegistryValueKind kind)
        {
            if (string.IsNullOrEmpty(currentKeyPath)) return;
            var syncContext = SynchronizationContext.Current;
            Task.Run(async () =>
            {
                var baseName = "New Value #";
                var idx = 1;
                var name = baseName + idx;
                using var key = RegistryService.OpenKey(currentKeyPath);
                if (key is not null)
                {
                    var existing = key.GetValueNames();
                    while (existing.Contains(name)) { idx++; name = baseName + idx; }
                }
                await RegistryService.CreateValueAsync(currentKeyPath, name, kind);
                syncContext?.Post(_ => RefreshCurrentKey(), null);
            });
        }

        // ── Edit value handlers ──────────────────────────────────────
        void OpenEditDialog(RegistryValueEntry value)
        {
            setEditValueName(value.DisplayName);
            setEditValueKind(value.Kind);
            setEditNumberBase(NumberBase.Hexadecimal);

            switch (value.Kind)
            {
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString:
                    setEditValueData(value.Data?.ToString() ?? "");
                    setActiveDialog(ActiveDialog.EditString);
                    break;
                case RegistryValueKind.MultiString:
                    setEditValueData(string.Join("\r\n", (string[])(value.Data ?? Array.Empty<string>())));
                    setActiveDialog(ActiveDialog.EditMultiString);
                    break;
                case RegistryValueKind.DWord:
                    setEditValueData(((int)(value.Data ?? 0)).ToString("x"));
                    setActiveDialog(ActiveDialog.EditDword);
                    break;
                case RegistryValueKind.QWord:
                    setEditValueData(((long)(value.Data ?? 0L)).ToString("x"));
                    setActiveDialog(ActiveDialog.EditQword);
                    break;
                case RegistryValueKind.Binary:
                default:
                    setEditValueData(EditBinaryDialog.FormatHexDump(value.Data is byte[] b ? b : []));
                    setActiveDialog(ActiveDialog.EditBinary);
                    break;
            }
        }

        void OpenBinaryEditDialog(RegistryValueEntry value)
        {
            setEditValueName(value.DisplayName);
            setEditValueKind(value.Kind);
            byte[] bytes = value.Kind switch
            {
                RegistryValueKind.Binary => (byte[])(value.Data ?? Array.Empty<byte>()),
                RegistryValueKind.DWord => BitConverter.GetBytes((int)(value.Data ?? 0)),
                RegistryValueKind.QWord => BitConverter.GetBytes((long)(value.Data ?? 0L)),
                _ => global::System.Text.Encoding.Unicode.GetBytes(value.Data?.ToString() ?? "")
            };
            setEditValueData(EditBinaryDialog.FormatHexDump(bytes));
            setActiveDialog(ActiveDialog.EditBinary);
        }

        void SaveEditedValue()
        {
            if (string.IsNullOrEmpty(currentKeyPath)) return;

            var valueName = editValueName == "(Default)" ? "" : editValueName;
            var syncContext = SynchronizationContext.Current;

            object? data = null;
            var kind = editValueKind;

            switch (activeDialog)
            {
                case ActiveDialog.EditString:
                    data = editValueData;
                    kind = editValueKind == RegistryValueKind.ExpandString
                        ? RegistryValueKind.ExpandString : RegistryValueKind.String;
                    break;
                case ActiveDialog.EditMultiString:
                    data = editValueData.Split("\r\n");
                    kind = RegistryValueKind.MultiString;
                    break;
                case ActiveDialog.EditDword:
                    if (editNumberBase == NumberBase.Hexadecimal)
                    {
                        if (int.TryParse(editValueData, global::System.Globalization.NumberStyles.HexNumber, null, out var hexVal))
                            data = hexVal;
                    }
                    else
                    {
                        if (int.TryParse(editValueData, out var decVal))
                            data = decVal;
                    }
                    kind = RegistryValueKind.DWord;
                    break;
                case ActiveDialog.EditQword:
                    if (editNumberBase == NumberBase.Hexadecimal)
                    {
                        if (long.TryParse(editValueData, global::System.Globalization.NumberStyles.HexNumber, null, out var hexVal))
                            data = hexVal;
                    }
                    else
                    {
                        if (long.TryParse(editValueData, out var decVal))
                            data = decVal;
                    }
                    kind = RegistryValueKind.QWord;
                    break;
                case ActiveDialog.EditBinary:
                    data = EditBinaryDialog.ParseHexDump(editValueData);
                    kind = RegistryValueKind.Binary;
                    break;
            }

            if (data is not null)
            {
                Task.Run(async () =>
                {
                    await RegistryService.SetValueAsync(currentKeyPath, valueName, data, kind);
                    syncContext?.Post(_ =>
                    {
                        setActiveDialog(ActiveDialog.None);
                        RefreshCurrentKey();
                    }, null);
                });
            }
            else
            {
                setActiveDialog(ActiveDialog.None);
            }
        }

        // ── Delete handlers ──────────────────────────────────────────
        void DeleteSelectedValue(RegistryValueEntry value)
        {
            if (string.IsNullOrEmpty(currentKeyPath)) return;
            var syncContext = SynchronizationContext.Current;
            Task.Run(async () =>
            {
                await RegistryService.DeleteValueAsync(currentKeyPath, value.Name);
                syncContext?.Post(_ => RefreshCurrentKey(), null);
            });
        }

        void DeleteCurrentKey()
        {
            if (string.IsNullOrEmpty(currentKeyPath)) return;
            // Don't allow deleting root keys
            if (!currentKeyPath.Contains('\\')) return;

            var parentPath = currentKeyPath[..currentKeyPath.LastIndexOf('\\')];
            var syncContext = SynchronizationContext.Current;
            Task.Run(async () =>
            {
                var success = await RegistryService.DeleteKeyAsync(currentKeyPath);
                syncContext?.Post(_ =>
                {
                    if (success)
                        NavigateToKey(parentPath);
                }, null);
            });
        }

        // ── Rename handlers ─────────────────────────────────────────
        void StartRenameValue(RegistryValueEntry value)
        {
            setEditValueName(value.Name);
            setEditValueData(value.Name);
            setActiveDialog(ActiveDialog.RenameValue);
        }

        void PerformRenameValue()
        {
            if (string.IsNullOrEmpty(currentKeyPath)) return;
            var syncContext = SynchronizationContext.Current;
            var oldName = editValueName;
            var newName = editValueData;
            Task.Run(async () =>
            {
                await RegistryService.RenameValueAsync(currentKeyPath, oldName, newName);
                syncContext?.Post(_ =>
                {
                    setActiveDialog(ActiveDialog.None);
                    RefreshCurrentKey();
                }, null);
            });
        }

        void StartRenameKey()
        {
            if (string.IsNullOrEmpty(currentKeyPath) || !currentKeyPath.Contains('\\')) return;
            var name = currentKeyPath[(currentKeyPath.LastIndexOf('\\') + 1)..];
            setEditValueName(name);
            setEditValueData(name);
            setActiveDialog(ActiveDialog.RenameKey);
        }

        void PerformRenameKey()
        {
            if (string.IsNullOrEmpty(currentKeyPath)) return;
            var syncContext = SynchronizationContext.Current;
            var newName = editValueData;
            var parentPath = currentKeyPath[..currentKeyPath.LastIndexOf('\\')];
            Task.Run(async () =>
            {
                var success = await RegistryService.RenameKeyAsync(currentKeyPath, newName);
                syncContext?.Post(_ =>
                {
                    setActiveDialog(ActiveDialog.None);
                    if (success)
                        NavigateToKey($"{parentPath}\\{newName}");
                }, null);
            });
        }

        // ── Find handlers ────────────────────────────────────────────
        void PerformFind()
        {
            if (string.IsNullOrEmpty(searchText)) return;
            var startPath = string.IsNullOrEmpty(currentKeyPath) ? "HKEY_LOCAL_MACHINE" : currentKeyPath;
            setActiveDialog(ActiveDialog.None);
            setStatusText($"Searching...");

            searchCtsRef.Current?.Cancel();
            var cts = new CancellationTokenSource();
            searchCtsRef.Current = cts;

            var syncContext = SynchronizationContext.Current;
            Task.Run(async () =>
            {
                try
                {
                    var result = await SearchService.FindNextAsync(startPath, null, searchText, findFlags, cts.Token);
                    syncContext?.Post(_ =>
                    {
                        if (result is not null)
                        {
                            NavigateToKey(result.KeyPath);
                            setStatusText(Strings.Ready);
                        }
                        else
                        {
                            setStatusText("Finished searching through the registry.");
                        }
                    }, null);
                }
                catch (OperationCanceledException) { }
            });
        }

        void FindNext()
        {
            if (string.IsNullOrEmpty(searchText)) { setActiveDialog(ActiveDialog.Find); return; }
            PerformFind();
        }

        // ── Copy key name ────────────────────────────────────────────
        void CopyKeyName()
        {
            if (string.IsNullOrEmpty(currentKeyPath)) return;
            var dataPackage = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(currentKeyPath);
            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }

        // ── Export handler ───────────────────────────────────────────
        void PerformExport()
        {
            var exportPath = exportAll ? "HKEY_LOCAL_MACHINE" : currentKeyPath;
            if (string.IsNullOrEmpty(exportPath)) return;

            var syncContext = SynchronizationContext.Current;
            setActiveDialog(ActiveDialog.None);
            setStatusText("Exporting...");

            Task.Run(async () =>
            {
                var content = await RegFileService.ExportAsync(exportPath);
                syncContext?.Post(_ =>
                {
                    var dataPackage = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
                    dataPackage.SetText(content);
                    global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
                    setStatusText("Exported to clipboard.");
                }, null);
            });
        }

        // ── Favorites handlers ───────────────────────────────────────
        void AddFavorite()
        {
            if (string.IsNullOrEmpty(currentKeyPath)) return;
            var newFavs = new Dictionary<string, string>(favorites)
            {
                [favoriteName] = currentKeyPath
            };
            setFavorites(newFavs);
            setActiveDialog(ActiveDialog.None);
        }

        void RemoveSelectedFavorite()
        {
            var keys = favorites.Keys.ToArray();
            if (selectedFavoriteIndex < 0 || selectedFavoriteIndex >= keys.Length) return;
            var newFavs = new Dictionary<string, string>(favorites);
            newFavs.Remove(keys[selectedFavoriteIndex]);
            setFavorites(newFavs);
            setActiveDialog(ActiveDialog.None);
        }

        // ── Permissions (P/Invoke native security dialog) ────────────
        void ShowPermissions()
        {
            // Out of scope for initial implementation — would require P/Invoke to aclui.dll EditSecurity
        }

        // ── Number base change handler ───────────────────────────────
        void OnDwordBaseChanged(NumberBase newBase)
        {
            // Convert display value between hex and decimal
            if (activeDialog == ActiveDialog.EditDword)
            {
                if (newBase == NumberBase.Decimal && editNumberBase == NumberBase.Hexadecimal)
                {
                    if (int.TryParse(editValueData, global::System.Globalization.NumberStyles.HexNumber, null, out var val))
                        setEditValueData(val.ToString());
                }
                else if (newBase == NumberBase.Hexadecimal && editNumberBase == NumberBase.Decimal)
                {
                    if (int.TryParse(editValueData, out var val))
                        setEditValueData(val.ToString("x"));
                }
            }
            else if (activeDialog == ActiveDialog.EditQword)
            {
                if (newBase == NumberBase.Decimal && editNumberBase == NumberBase.Hexadecimal)
                {
                    if (long.TryParse(editValueData, global::System.Globalization.NumberStyles.HexNumber, null, out var val))
                        setEditValueData(val.ToString());
                }
                else if (newBase == NumberBase.Hexadecimal && editNumberBase == NumberBase.Decimal)
                {
                    if (long.TryParse(editValueData, out var val))
                        setEditValueData(val.ToString("x"));
                }
            }
            setEditNumberBase(newBase);
        }

        // ── Build menu bar ───────────────────────────────────────────
        var menuBar = MenuBar([
            // File
            Menu(Strings.FileMenu, [
                MenuItem(Strings.Import, () =>
                {
                    // For now, import from clipboard
                    var syncContext = SynchronizationContext.Current;
                    Task.Run(async () =>
                    {
                        try
                        {
                            var content = await global::Windows.ApplicationModel.DataTransfer.Clipboard.GetContent().GetTextAsync();
                            if (!string.IsNullOrEmpty(content))
                            {
                                var count = await RegFileService.ImportAsync(content);
                                syncContext?.Post(_ =>
                                {
                                    setStatusText($"Imported {count} key(s).");
                                    RefreshCurrentKey();
                                }, null);
                            }
                        }
                        catch { }
                    });
                }),
                MenuItem(Strings.Export, () => setActiveDialog(ActiveDialog.Export)) with
                    { KeyboardAccelerators = [Accelerator(global::Windows.System.VirtualKey.E, global::Windows.System.VirtualKeyModifiers.Control)] },
                MenuSeparator(),
                MenuItem(Strings.LoadHive, () => { /* requires elevation */ }),
                MenuItem(Strings.UnloadHive, () => { /* requires elevation */ }),
                MenuSeparator(),
                MenuItem(Strings.ConnectNetworkRegistry, () => { }),
                MenuItem(Strings.DisconnectNetworkRegistry, () => { }),
                MenuSeparator(),
                MenuItem(Strings.Print, () => { }) with
                    { KeyboardAccelerators = [Accelerator(global::Windows.System.VirtualKey.P, global::Windows.System.VirtualKeyModifiers.Control)] },
                MenuSeparator(),
                MenuItem(Strings.Exit, () => Application.Current.Exit()),
            ]),

            // Edit
            Menu(Strings.EditMenu, [
                MenuSubItem(Strings.NewMenu, [
                    MenuItem(Strings.NewKey, CreateNewKey),
                    MenuSeparator(),
                    MenuItem(Strings.NewStringValue, () => CreateNewValue(RegistryValueKind.String)),
                    MenuItem(Strings.NewBinaryValue, () => CreateNewValue(RegistryValueKind.Binary)),
                    MenuItem(Strings.NewDwordValue, () => CreateNewValue(RegistryValueKind.DWord)),
                    MenuItem(Strings.NewQwordValue, () => CreateNewValue(RegistryValueKind.QWord)),
                    MenuItem(Strings.NewMultiStringValue, () => CreateNewValue(RegistryValueKind.MultiString)),
                    MenuItem(Strings.NewExpandableStringValue, () => CreateNewValue(RegistryValueKind.ExpandString)),
                ]),
                MenuSeparator(),
                MenuItem(Strings.Permissions, ShowPermissions),
                MenuSeparator(),
                MenuItem(Strings.Delete, DeleteCurrentKey) with
                    { KeyboardAccelerators = [Accelerator(global::Windows.System.VirtualKey.Delete)] },
                MenuItem(Strings.Rename, StartRenameKey) with
                    { KeyboardAccelerators = [Accelerator(global::Windows.System.VirtualKey.F2)] },
                MenuSeparator(),
                MenuItem(Strings.CopyKeyName, CopyKeyName),
                MenuSeparator(),
                MenuItem(Strings.Find, () => setActiveDialog(ActiveDialog.Find)) with
                    { KeyboardAccelerators = [Accelerator(global::Windows.System.VirtualKey.F, global::Windows.System.VirtualKeyModifiers.Control)] },
                MenuItem(Strings.FindNext, FindNext) with
                    { KeyboardAccelerators = [Accelerator(global::Windows.System.VirtualKey.F3)] },
            ]),

            // View
            Menu(Strings.ViewMenu, [
                ToggleMenuItem(Strings.AddressBar, showAddressBar, _ => setShowAddressBar(!showAddressBar)),
                ToggleMenuItem(Strings.StatusBar, showStatusBar, _ => setShowStatusBar(!showStatusBar)),
                MenuSeparator(),
                MenuItem(Strings.Split, () => { }),
                MenuSeparator(),
                MenuItem(Strings.Refresh, RefreshCurrentKey) with
                    { KeyboardAccelerators = [Accelerator(global::Windows.System.VirtualKey.F5)] },
            ]),

            // Favorites
            Menu(Strings.FavoritesMenu, BuildFavoritesMenu(favorites, favoriteName, currentKeyPath,
                setFavoriteName, setActiveDialog, setSelectedFavoriteIndex, NavigateToKey)),

            // Help
            Menu(Strings.HelpMenu, [
                MenuItem(Strings.About, () => setActiveDialog(ActiveDialog.About)),
            ]),
        ]);

        // ── Address bar ──────────────────────────────────────────────
        var addressBar = When(showAddressBar, () =>
            HStack(8,
                TextBlock(Strings.Computer)
                    .VAlign(VerticalAlignment.Center)
                    .SemiBold(),
                AutoSuggestBox(addressBarText, setAddressBarText, text => AddressBarSubmit(text))
                    .Set(asb => { asb.PlaceholderText = "HKEY_LOCAL_MACHINE\\SOFTWARE"; })
                    .HAlign(HorizontalAlignment.Stretch)
            ).Margin(4, 2, 4, 2)
        );

        // ── Tree pane ────────────────────────────────────────────────
        var tree = Component<RegistryTree, RegistryTreeProps>(new RegistryTreeProps(
            ExpandedPaths: treeState.Expanded,
            TreeChildren: treeState.Children,
            CurrentKeyPath: currentKeyPath,
            OnNavigate: NavigateToKey,
            OnExpand: ExpandTreeNode,
            OnContextMenu: _ => { }
        ));

        // ── Value list pane ──────────────────────────────────────────
        var valueList = Component<ValueList, ValueListProps>(new ValueListProps(
            Values: values,
            SelectedIndex: selectedValueIndex,
            OnSelectionChanged: setSelectedValueIndex,
            OnModify: OpenEditDialog,
            OnModifyBinary: OpenBinaryEditDialog,
            OnDelete: DeleteSelectedValue,
            OnRename: StartRenameValue
        ));

        // ── Status bar ───────────────────────────────────────────────
        var statusBar = When(showStatusBar, () =>
            HStack(8,
                TextBlock(string.IsNullOrEmpty(currentKeyPath) ? Strings.Computer : currentKeyPath)
                    .Set(tb =>
                    {
                        tb.TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis;
                        tb.TextWrapping = Microsoft.UI.Xaml.TextWrapping.NoWrap;
                    }),
                When(isLoading, () => ProgressRing().Width(16).Height(16).IsActive(true))
            )
            .Padding(8, 4, 8, 4)
            .WithBorder(DividerStroke)
        );

        // ── Dialogs ─────────────────────────────────────────────────
        var editStringDialog = Component<EditStringDialog, EditStringDialogProps>(new EditStringDialogProps(
            IsOpen: activeDialog == ActiveDialog.EditString,
            Title: editValueKind == RegistryValueKind.ExpandString ? "Edit Expandable String" : Strings.EditStringTitle,
            ValueName: editValueName,
            ValueData: editValueData,
            OnValueDataChanged: setEditValueData,
            OnSave: SaveEditedValue,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var editMultiStringDialog = Component<EditMultiStringDialog, EditMultiStringDialogProps>(new EditMultiStringDialogProps(
            IsOpen: activeDialog == ActiveDialog.EditMultiString,
            ValueName: editValueName,
            ValueData: editValueData,
            OnValueDataChanged: setEditValueData,
            OnSave: SaveEditedValue,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var editDwordDialog = Component<EditDwordDialog, EditDwordDialogProps>(new EditDwordDialogProps(
            IsOpen: activeDialog == ActiveDialog.EditDword,
            ValueName: editValueName,
            ValueData: editValueData,
            NumberBase: editNumberBase,
            OnValueDataChanged: setEditValueData,
            OnBaseChanged: OnDwordBaseChanged,
            OnSave: SaveEditedValue,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var editQwordDialog = Component<EditQwordDialog, EditQwordDialogProps>(new EditQwordDialogProps(
            IsOpen: activeDialog == ActiveDialog.EditQword,
            ValueName: editValueName,
            ValueData: editValueData,
            NumberBase: editNumberBase,
            OnValueDataChanged: setEditValueData,
            OnBaseChanged: OnDwordBaseChanged,
            OnSave: SaveEditedValue,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var editBinaryDialog = Component<EditBinaryDialog, EditBinaryDialogProps>(new EditBinaryDialogProps(
            IsOpen: activeDialog == ActiveDialog.EditBinary,
            ValueName: editValueName,
            ValueData: editValueData,
            OnValueDataChanged: setEditValueData,
            OnSave: SaveEditedValue,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var findDialog = Component<FindDialog, FindDialogProps>(new FindDialogProps(
            IsOpen: activeDialog == ActiveDialog.Find,
            SearchText: searchText,
            Flags: findFlags,
            OnSearchTextChanged: setSearchText,
            OnFlagsChanged: setFindFlags,
            OnFind: PerformFind,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var addFavoriteDialog = Component<AddFavoriteDialog, AddFavoriteDialogProps>(new AddFavoriteDialogProps(
            IsOpen: activeDialog == ActiveDialog.AddFavorite,
            FavoriteName: favoriteName,
            OnNameChanged: setFavoriteName,
            OnSave: AddFavorite,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var removeFavoriteDialog = Component<RemoveFavoriteDialog, RemoveFavoriteDialogProps>(new RemoveFavoriteDialogProps(
            IsOpen: activeDialog == ActiveDialog.RemoveFavorite,
            FavoriteNames: favorites.Keys.ToArray(),
            SelectedIndex: selectedFavoriteIndex,
            OnSelectionChanged: setSelectedFavoriteIndex,
            OnRemove: RemoveSelectedFavorite,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var exportDialog = Component<ExportDialog, ExportDialogProps>(new ExportDialogProps(
            IsOpen: activeDialog == ActiveDialog.Export,
            ExportAll: exportAll,
            SelectedBranch: currentKeyPath,
            OnExportAllChanged: setExportAll,
            OnExport: PerformExport,
            OnCancel: () => setActiveDialog(ActiveDialog.None)
        ));

        var renameKeyDialog = ContentDialog(
            Strings.RenameKeyTitle,
            VStack(8,
                TextBlock(Strings.NewKeyName),
                TextBox(editValueData, setEditValueData)
            ).Width(350),
            Strings.OK
        ) with
        {
            IsOpen = activeDialog == ActiveDialog.RenameKey,
            SecondaryButtonText = Strings.Cancel,
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            OnClosed = result =>
            {
                if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                    PerformRenameKey();
                else
                    setActiveDialog(ActiveDialog.None);
            },
        };

        var renameValueDialog = ContentDialog(
            Strings.RenameValueTitle,
            VStack(8,
                TextBlock(Strings.NewKeyName),
                TextBox(editValueData, setEditValueData)
            ).Width(350),
            Strings.OK
        ) with
        {
            IsOpen = activeDialog == ActiveDialog.RenameValue,
            SecondaryButtonText = Strings.Cancel,
            DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
            OnClosed = result =>
            {
                if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                    PerformRenameValue();
                else
                    setActiveDialog(ActiveDialog.None);
            },
        };

        var aboutDialog = ContentDialog(
            Strings.AboutTitle,
            TextBlock(Strings.AboutText),
            Strings.OK
        ) with
        {
            IsOpen = activeDialog == ActiveDialog.About,
            OnClosed = _ => setActiveDialog(ActiveDialog.None),
        };

        // ── Layout ───────────────────────────────────────────────────
        return Grid(
            [GridSize.Star()],
            [GridSize.Auto, GridSize.Auto, GridSize.Star(), GridSize.Auto],
            menuBar.Grid(row: 0, column: 0),
            addressBar.Grid(row: 1, column: 0),
            Component<SplitPanel, SplitPanelProps>(new SplitPanelProps(
                Left: tree,
                Right: VStack(0,
                    valueList,
                    // Dialogs rendered here so they're part of the visual tree
                    editStringDialog,
                    editMultiStringDialog,
                    editDwordDialog,
                    editQwordDialog,
                    editBinaryDialog,
                    findDialog,
                    addFavoriteDialog,
                    removeFavoriteDialog,
                    exportDialog,
                    renameKeyDialog,
                    renameValueDialog,
                    aboutDialog
                )
            )).Grid(row: 2, column: 0),
            statusBar.Grid(row: 3, column: 0)
        )
        // Spec 033 §6 — Mica window backdrop on a primary tool surface.
        .Backdrop(BackdropKind.Mica);
    }

    /// <summary>
    /// Builds the dynamic Favorites menu items (Add/Remove + saved favorites).
    /// </summary>
    private static MenuFlyoutItemBase[] BuildFavoritesMenu(
        Dictionary<string, string> favorites,
        string favoriteName,
        string currentKeyPath,
        Action<string> setFavoriteName,
        Action<ActiveDialog> setActiveDialog,
        Action<int> setSelectedFavoriteIndex,
        Action<string> navigateToKey)
    {
        var items = new List<MenuFlyoutItemBase>
        {
            MenuItem(Strings.AddToFavorites, () =>
            {
                // Default favorite name to last segment of current key path
                var name = string.IsNullOrEmpty(currentKeyPath) ? ""
                    : currentKeyPath.Contains('\\')
                        ? currentKeyPath[(currentKeyPath.LastIndexOf('\\') + 1)..]
                        : currentKeyPath;
                setFavoriteName(name);
                setActiveDialog(ActiveDialog.AddFavorite);
            }),
            MenuItem(Strings.RemoveFromFavorites, () =>
            {
                setSelectedFavoriteIndex(-1);
                setActiveDialog(ActiveDialog.RemoveFavorite);
            }),
        };

        if (favorites.Count > 0)
        {
            items.Add(MenuSeparator());
            foreach (var (name, path) in favorites)
            {
                items.Add(MenuItem(name, () => navigateToKey(path)));
            }
        }

        return items.ToArray();
    }

}
