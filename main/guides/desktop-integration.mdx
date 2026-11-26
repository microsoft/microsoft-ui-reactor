---
title: "Desktop integration"
description: "A desktop app does more than draw inside its windows. It can put an icon in the notification area, show progress on its taskbar button, offer shortcuts in a jump list, and start when the user opens a file or a link. It also shows the standard file dialogs and moves data through the clipboard and drag and drop."
---

Reactor wraps most of these shell surfaces in small, typed APIs. Some
APIs are hooks that follow a component's lifetime, such as `UseTrayIcon`.
Others are plain methods that you call from an event handler, such as
`PickFileAsync`. A few surfaces, like the clipboard, have no Reactor wrapper,
and you call the WinRT API directly. This page covers each surface, says
which scope it has, and lists the caveats that matter.

![A Reactor app reaches four shell surfaces: the tray icon, the taskbar button, the jump list and the file dialogs. Picking a jump-list entry, or Windows opening a file or link for the app, starts a new process that reads ctx.LaunchActivation](/microsoft-ui-reactor/main/images/desktop-integration/shell-surfaces.svg)

Window basics (size, chrome, icons, displays, shutdown) are on
[Windows](/microsoft-ui-reactor/main/guides/windows). The code on this page comes from two compiled doc apps,
[`desktop-integration`](https://github.com/microsoft/microsoft-ui-reactor/tree/main/docs/_pipeline/apps/desktop-integration)
and [`windows`](https://github.com/microsoft/microsoft-ui-reactor/tree/main/docs/_pipeline/apps/windows).

## Reference [#reference]

| Surface | Reactor API | Scope |
| --- | --- | --- |
| Notification area icon | `UseTrayIcon`, `ReactorApp.OpenTrayIcon`, `ReactorTrayIcon` | The process. `UseTrayIcon` closes the icon when the component unmounts. |
| Tray menu | `ReactorTrayIcon.ShowFlyout(element)` | One flyout per process |
| Taskbar button | `ReactorWindow.TaskbarItem` | One per window |
| Jump list | `JumpList`, `JumpListItem` | One per app identity |
| Launch activation | `ReactorAppContext.LaunchActivation` | Read once, when the process starts |
| File and folder dialogs | `PickFileAsync`, `PickFolderAsync`, `PickSaveFileAsync`, … | Modal to the component's window |
| Clipboard | WinRT `Windows.ApplicationModel.DataTransfer.Clipboard` | No Reactor wrapper |
| Drag and drop | `.OnDragStart`, `.OnDrop`, and related modifiers | One element |

## Tray icons [#tray-icons]

`ReactorApp.OpenTrayIcon` registers a notification-area icon for the process;
`UseTrayIcon` scopes one to a component and closes it on unmount. Both take a
`TrayIconSpec`, and mutating `Icon`, `Tooltip` or `IsVisible` re-applies through
`Shell_NotifyIcon`.

```csharp
class TrayHost : Component
{
    public override Element Render()
    {
        var icon = UseMemo(() => WindowIcon.FromPath("Assets/TrayIcon.ico"));
        var tray = UseTrayIcon(new TrayIconSpec(
            Icon: icon,
            Tooltip: "My App",
            Key: WindowKey.Of("main-tray")));

        UseEffect(() =>
        {
            if (tray is null) return () => { };
            void onClick(object? s, EventArgs e)
                => ReactorApp.PrimaryWindow?.Activate();
            tray.Click += onClick;
            return () => tray.Click -= onClick;
        }, tray ?? (object)"no-tray");

        return TextBlock("Tray icon registered while this component is mounted.");
    }
}
```

`UseTrayIcon` returns `null` when there is no UI dispatcher, for example in a
headless test. Check for `null` before you subscribe to events, as the
example does.

The icon does not have to be a file. `WindowIcon.FromBytes` takes encoded `.ico`
or PNG data, and `WindowIcon.FromRgba` takes a raw pixel buffer — so an embedded
resource, a downloaded asset, or a badge you draw at runtime reaches the shell
without a temporary file on disk:

```csharp
// A tray icon can also come from bytes already in memory — no temporary file.
class BinaryTrayHost : Component
{
    public override Element Render()
    {
        // Encoded .ico or PNG data: an embedded resource, a download, a database blob.
        var embedded = UseMemo(() => WindowIcon.FromBytes(LoadEmbeddedIcon()));

        // Or a raw RGBA8 buffer you drew yourself, for a badge that changes at runtime.
        var drawn = UseMemo(() => WindowIcon.FromRgba(UnreadBadge(16, 16), 16, 16));

        var tray = UseTrayIcon(new TrayIconSpec(
            Icon: embedded,
            Tooltip: "My App",
            Key: WindowKey.Of("binary-tray")));

        UseEffect(() =>
        {
            // Swapping the source reloads the shell bitmap.
            if (tray is not null) tray.Icon = drawn;
            return () => { };
        }, drawn);

        return TextBlock("Tray icon built from in-memory data.");
    }

    static byte[] LoadEmbeddedIcon()
    {
        var assembly = typeof(BinaryTrayHost).Assembly;
        using var stream = assembly.GetManifestResourceStream("MyApp.TrayIcon.ico")
            ?? throw new InvalidOperationException(
                "Embedded resource 'MyApp.TrayIcon.ico' not found — check the file's Build Action.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // width * height * 4 bytes, top-down, one pixel as R, G, B, A.
    static byte[] UnreadBadge(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0xE8;     // R
            pixels[i + 1] = 0x11; // G
            pixels[i + 2] = 0x23; // B
            pixels[i + 3] = 0xFF; // A
        }
        return pixels;
    }
}
```

### Tray menu [#tray-menu]

A tray icon raises `Click`, `DoubleClick` and `RightClick`. To show a menu,
call `ShowFlyout` with any Reactor element, usually from `RightClick`. Call
`HideFlyout` to close it from code.

```csharp
class TrayMenuHost : Component
{
    public override Element Render()
    {
        var icon = UseMemo(() => WindowIcon.FromPath("Assets/TrayIcon.ico"));
        var tray = UseTrayIcon(new TrayIconSpec(
            Icon: icon,
            Tooltip: "Contoso Sync",
            Key: WindowKey.Of("sync-tray")));

        UseEffect(() =>
        {
            if (tray is null) return () => { };

            void onClick(object? s, EventArgs e) => BringBack();

            // The flyout is a Reactor element. It opens at the cursor and
            // closes when it loses focus, for example on a click outside it.
            void onRightClick(object? s, EventArgs e) => tray.ShowFlyout(
                VStack(4,
                    Button("Open Contoso Sync", () =>
                    {
                        tray.HideFlyout();
                        BringBack();
                    }),
                    Button("Quit", () => ReactorApp.Exit())
                ).Padding(8));

            tray.Click += onClick;
            tray.RightClick += onRightClick;
            return () =>
            {
                tray.Click -= onClick;
                tray.RightClick -= onRightClick;
            };
        }, tray ?? (object)"no-tray");

        return TextBlock("Right-click the tray icon to open its menu.");
    }

    // Show() undoes a Hide(); Activate() brings the window to the front.
    static void BringBack()
    {
        var window = ReactorApp.PrimaryWindow;
        window?.Show();
        window?.Activate();
    }
}
```

How the flyout behaves:

- There is one flyout window per process. Showing a flyout from any tray icon
  replaces the one that is open.
- Each show mounts the element in a new host, so hook state inside the flyout
  starts fresh every time.
- The flyout is not a window component. `UseWindow()` returns `null` inside
  it.
- It has a fixed size of 280 × 360 DIPs, scaled to the monitor's DPI. It
  opens at the cursor and stays inside the monitor's work area.
- It closes when it loses focus, for example when the user clicks outside it.

Caveats:

- Ship a multi-size `.ico` (16, 20, 24, 32, 40 px) where you can. The tray asks for
  the DPI-aware small-icon size, and both the file loader and the in-memory loader
  pick the closest frame rather than rescaling a single one.
- `ms-appx:///` resources are not usable here. See
  [where an icon can come from](/microsoft-ui-reactor/main/guides/windows#where-an-icon-can-come-from). A
  packaged app should ship a sidecar `.ico` or embed the bytes.
- Tooltips are truncated to the shell's 127-character `szTip` buffer, and surface to
  Narrator as the icon's accessible name.
- **Tray clicks are not authenticated.** The callback arrives as a Win32 message any
  process at the same integrity level can synthesise. Use click handlers for
  reversible UI actions; gate anything destructive behind an in-app confirmation.
- Under `ShutdownPolicy.OnLastSurfaceClosed`, an open tray icon keeps the
  process alive after the last window closes. See
  [shutdown policy](/microsoft-ui-reactor/main/guides/windows#shutdown-policy).

## Taskbar integration [#taskbar-integration]

`TaskbarItem` groups the per-window taskbar features while keeping the older
shortcuts on `ReactorWindow` for compatibility. The jump list is the one
taskbar surface that is *not* on `TaskbarItem`, because it is per-process
rather than per-window — see [Jump list](#jump-list) below.

```csharp
var taskbar = UseWindow()!.TaskbarItem;
taskbar.Description = "Build in progress";
taskbar.Progress.State = TaskbarProgressState.Normal;
taskbar.Progress.Value = 0.42;
taskbar.SetThumbnailToolbar([
    new ThumbnailToolbarButton("pause", WindowIcon.FromPath("pause.ico"), "Pause", () => Pause())
]);
```

Facade members:

- `Progress` — same instance as `ReactorWindow.Progress`.
- `Overlay` — same instance as `ReactorWindow.Overlay`.
- `Description` — forwards to `ITaskbarList3.SetThumbnailTooltip`.
- `SetThumbnailToolbar` / `ClearThumbnailToolbar` — same toolbar pipeline as the
  `ReactorWindow` shortcut methods.

Caveats:

- Shell COM calls are best-effort; Reactor keeps last-set managed state where relevant.
- Thumbnail toolbars support at most seven buttons.
- Overlay and thumbnail icons need HICON-compatible sources: a `FromPath` file or
  `FromBytes` / `FromRgba` data. Resource URIs are not overlay HICONs.

### Jump list [#jump-list]

`JumpList` is a process-wide static: the shell attaches one list per
application identity, not per window. `JumpList.UpdateAsync` replaces the
whole list, and `JumpList.ClearAsync` removes it.

Activating an entry re-launches the process with the entry's `Arguments`
string. Reactor surfaces that as `LaunchKind.JumpList` on the
`ReactorAppContext` handed to the `ReactorApp.Run(Action<ReactorAppContext>)`
startup callback. The recommended convention is to put a deep-link URI in
`Arguments` (that is what `JumpListItem.ForUri` is for) and resolve it through
a [`DeepLinkMap<TRoute>`](/microsoft-ui-reactor/main/guides/navigation):

```csharp
// In a real app, load this list from the app's tracked destinations.
private static readonly List<JumpListItem> Destinations = [
    JumpListItem.ForUri("New document", "contoso://new"),
    JumpListItem.ForUri("Open dashboard", "contoso://dashboard",
        description: "Jump straight to the dashboard"),
    new JumpListItem("Report a bug", "contoso://bug",
        Kind: JumpListItemKind.Custom, GroupCategory: "Help"),
];

// Unpackaged apps must set an AppUserModelId once, before the first
// UpdateAsync — the shell has no other stable identity to hang the
// jump list off. Packaged apps inherit it from the manifest.
public static async Task PublishAsync()
{
    JumpList.AppUserModelId = "Contoso.Reactor.Demo";
    JumpList.ShowRecent = true;

    var removed = await JumpList.GetRemovedArgumentsAsync();
    PruneDestinations(removed);
    removed = await JumpList.UpdateAsync(Destinations);
    // Also process removals made after the query. A successful update
    // clears the OS records, even for entries absent from this batch.
    PruneDestinations(removed);
    // Persist the updated tracked destinations in your app's data store.
    // New user activity can explicitly add an eligible destination again.
}

private static void PruneDestinations(IReadOnlyList<string> removed) =>
    Destinations.RemoveAll(item => item.Kind != JumpListItemKind.Separator
        && removed.Contains(item.Arguments, StringComparer.Ordinal));

// Entries come back as a plain process re-launch. Resolve the argument
// string through the same DeepLinkMap the app already uses for routes;
// never act on it unvalidated. DeepLinkResult.Routes is the resolved
// back stack, deepest route last.
public static void Start(DeepLinkMap<string> routes) =>
    ReactorApp.Run(ctx =>
    {
        if (ctx.LaunchActivation.Kind == LaunchKind.JumpList &&
            ctx.LaunchActivation.TryResolve(routes, out var deepLink))
        {
            ReactorApp.OpenWindow(
                new WindowSpec { Title = deepLink.Routes[^1] },
                () => new SettingsWindow());
        }
    });
```

| API | Purpose |
| --- | --- |
| `JumpList.AppUserModelId` | Shell identity. Required before the first `UpdateAsync` on unpackaged apps; ignored under MSIX. |
| `JumpList.ShowRecent` / `ShowFrequent` | Toggle the OS-managed categories. Contents are shell-owned. |
| `JumpList.UpdateAsync(items)` | Replace the whole list and return user removals observed in that transaction. UI thread only. |
| `JumpList.ClearAsync()` | Remove the app's entries and return the transaction's observed removals. |
| `JumpList.GetRemovedArgumentsAsync()` | Read user-removed entries' argument strings without updating the list. UI thread only. |
| `JumpListItem.ForUri(...)` | Deep-link entry — `Arguments` is the URI. |
| `JumpListItem.ForCommandLine(...)` | argv-style entry; escapes each value for `CommandLineToArgvW`. |
| `JumpListItemKind` | `Task`, `Custom` (needs `GroupCategory`), `Separator` |

Caveats:

- **Argument strings round-trip through the shell into the next process
  launch.** Reactor never auto-executes them. Validate through `DeepLinkMap`
  before acting, and build entries carrying non-literal data with
  `JumpListItem.ForCommandLine` so a hostile value cannot break out into a
  neighbouring argv slot.
- Jump-list entries, tray "Open", and thumbnail-toolbar buttons are
  indistinguishable at the WinUI activation surface — all three arrive as
  `LaunchKind.JumpList`. Encode any finer distinction in the URI itself.
- Icons on the packaged path require `WindowIcon.FromResource`
  (`ms-appx:///…`); `FromPath` values are silently ignored there.
- `UpdateAsync` validates the whole batch before touching the shell, so one
  bad entry never leaves a half-populated list behind. Non-separator entries
  must have a non-empty `Title`.
- **Respect user removals.** Before updating, query `GetRemovedArgumentsAsync()`
  and prune matching arguments from the app's destination tracking data (not the
  underlying files). Also retain the arguments returned by `UpdateAsync` or
  `ClearAsync`: they capture removals observed inside that transaction, including
  removals arriving after the earlier query and removals absent from the new batch.
  A successful update clears Windows' removal records. Without updating its own
  tracking data, an app can reintroduce a removed destination on its next refresh.
  Only offer it again after new user activity; compare arguments ordinally.
  Results report observations, not commit success; logged shell HRESULT failures
  keep the existing non-throwing behavior. The return type is now
  `Task<IReadOnlyList<string>>`; await-only source callers continue to compile,
  but existing consumers must rebuild their binaries.
- On unpackaged apps, removal queries cover shell links targeting the current
  executable, which is the shape this wrapper creates, not `IShellItem` file
  destinations or links to other executables. Removal filtering applies to
  custom categories, not the non-removable User Tasks group. The packaged path
  uses each loaded item's `RemovedByUser` flag, including ungrouped entries.

## Launch activation [#launch-activation]

Windows can start your app in several ways: from the Start menu, from a jump
list, or because the user opened a file or a link that your app handles.
Reactor reads how the process started once, at launch, and passes it to the
`ReactorApp.Run(Action<ReactorAppContext>)` startup callback as
`ctx.LaunchActivation`.

```csharp
ReactorApp.Run(ctx =>
{
    var launch = ctx.LaunchActivation;

    // Arguments and Files come from outside the app. Validate them before use.
    string status = launch.Kind switch
    {
        LaunchKind.File => $"Opened with {launch.Files.Count} file(s)",
        LaunchKind.Protocol => $"Opened from a link: {launch.Arguments}",
        LaunchKind.Toast => "Opened from a notification",
        LaunchKind.JumpList => $"Opened from the taskbar: {launch.Arguments}",
        _ => "Normal launch",
    };

    ReactorApp.OpenWindow(
        new WindowSpec { Title = "Desktop integration", Width = 600, Height = 520 },
        _ => Component<DesktopShell, string>(status));
});
```

| `LaunchKind` | When | Data |
| --- | --- | --- |
| `Normal` | A launch with no arguments | none |
| `JumpList` | A launch with command-line arguments: a jump-list entry, a tray "Open", a thumbnail-toolbar button, or any other command line | `Arguments` |
| `File` | Windows opened a file type registered to the app | `Files` (paths) |
| `Protocol` | Windows opened a URI scheme registered to the app | `Arguments` (the URI) |
| `Toast` | Windows reported a toast notification activation | `Arguments` (the toast argument) |

`LaunchActivation.TryResolve(map, out var result)` resolves `Arguments`
through a [`DeepLinkMap<TRoute>`](/microsoft-ui-reactor/main/guides/navigation), as the jump-list example
above does.

Caveats:

- **Activation data comes from outside the app.** Any process can start your
  app with any arguments, and Reactor does not check them. Validate
  `Arguments` and `Files` before you act on them.
- Reading activation is best effort. If Windows reports an activation that
  Reactor cannot read, `LaunchActivation` is `Normal`, and startup goes on.
- Reactor does not register file types or URI schemes. A packaged app
  declares them in its `Package.appxmanifest`. An unpackaged app registers
  them with the Windows App SDK. See
  [rich activation with the app lifecycle API](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-rich-activation).
- Reactor has no single-instance support. Each activation starts a new
  process with its own `LaunchActivation`.
- Reactor has no API to send notifications. `LaunchKind.Toast` only reports
  that one started the process. To show one, call the Windows App SDK
  `AppNotificationManager` directly. The
  [chat sample](https://github.com/microsoft/microsoft-ui-reactor/tree/main/samples/apps/chat)
  registers it in the `configure` callback of `ReactorApp.Run` and shows a
  notification when a reply finishes while its window does not have focus.

## File and folder pickers [#file-and-folder-pickers]

Picker methods create `Microsoft.Windows.Storage.Pickers` dialogs owned by the
component's window, so each dialog is modal to the right window without any
HWND interop. They also work in elevated (administrator) apps, unlike the UWP
`Windows.Storage.Pickers` dialogs. `Component` and `RenderContext` expose the
same operations:

| Method | Options | Returns | Cancellation |
| --- | --- | --- | --- |
| `PickFileAsync` | `FilePickerOptions` | `PickFileResult?` | `null` |
| `PickMultipleFilesAsync` | `FilePickerOptions` | `IReadOnlyList<PickFileResult>` | Empty list |
| `PickFolderAsync` | `FolderPickerOptions` | `PickFolderResult?` | `null` |
| `PickMultipleFoldersAsync` | `FolderPickerOptions` | `IReadOnlyList<PickFolderResult>` | Empty list |
| `PickSaveFileAsync` | `SaveFilePickerOptions` | `PickFileResult?` | `null` |

Each result exposes the chosen `Path`. Open the file with `System.IO`, or with
`StorageFile.GetFileFromPathAsync` when you need a `StorageFile`.

All three option records share a suggested start location, commit-button text,
a dialog `Title`, a `SettingsIdentifier` that Windows uses to remember dialog state,
`SuggestedFolder` (always open there), and `SuggestedStartFolder` (open there until
Windows remembers a location). Both folder properties take absolute paths. Open
and folder options add an optional `ViewMode`. File options add:

- `FileTypeFilter`: extensions such as `".txt"`. Null or empty shows all files.
- `FileTypeChoices`: an ordered list of `FileTypeChoice(label, extensions)` groups.
  When non-empty, it replaces `FileTypeFilter`.
- `InitialFileTypeIndex`: the entry selected first. Null lets Windows choose.

Save options require at least one `FileTypeChoice`. The list order is the order
Windows shows, and the first entry is the default unless `InitialFileTypeIndex`
says otherwise. Labels must be unique and non-blank, and every choice needs at
least one extension. Save options also take a suggested file name,
`DefaultFileExtension`, and `ShowOverwritePrompt` (default `true`). The save
method only selects a target path; the caller must create and write the file.

Both `Windows.Storage.Pickers` and `Microsoft.Windows.Storage.Pickers` define
`PickerLocationId` and `PickerViewMode`, so import only the
`Microsoft.Windows.Storage.Pickers` namespace in files that build picker options.

```csharp
// Pickers are not hooks: call them directly from UI-thread event handlers.
return VStack(
    Button("Open one...", async () =>
    {
        var file = await PickFileAsync(new FilePickerOptions(FileTypeFilter: [".txt", ".md"]));
        if (file is null) return; // User cancelled.
        var text = await File.ReadAllTextAsync(file.Path);
    }),
    Button("Open files...", async () =>
    {
        var files = await PickMultipleFilesAsync(new FilePickerOptions
        {
            Title = "Choose notes",
            FileTypeChoices = [new("Notes", [".txt", ".md"]), new("JSON", [".json"])],
        });
        if (files.Count == 0) return; // User cancelled.
    }),
    Button("Select folders...", async () =>
    {
        var folders = await PickMultipleFoldersAsync(new FolderPickerOptions());
        if (folders.Count == 0) return;
    }),
    Button("Save as...", async () =>
    {
        var target = await PickSaveFileAsync(new SaveFilePickerOptions(
            [new("Text", [".txt"]), new("Markdown", [".md"])],
            SuggestedFileName: "report"));
        if (target is null) return;
        // Selecting a target is not a save: write the content yourself.
        await File.WriteAllTextAsync(target.Path, "report body");
    }));
```

Caveats:

- These methods are not hooks: they reserve no hook slot and open a dialog when
  called. Call them from UI-thread event handlers, not during `Render()` or in
  `Task.Run`. Their `Pick*` names keep the hook-rules analyzer quiet in handlers.
- The owner is the window whose host rendered the component (including nested and
  virtualized-row components), not whichever window is active when the handler
  runs. That is the `ReactorWindow`, the `Window` a `ReactorHost` was constructed
  over, or for `ReactorHostControl` the window the control is loaded in when the
  method is called (a reparented control follows its new window). Once the
  component unmounts (including when its root is replaced or its
  `ReactorHostControl` is disposed) or its window closes, the call throws
  `InvalidOperationException`. It also throws while a `ReactorHostControl` is
  unloaded (not in any window).
- Option validation (missing or duplicate file-type choices) throws `ArgumentException`
  before any dialog opens. Windows validates extension syntax and folder paths.
- Picker errors propagate to the caller; cancellation is not an error.
- Framework tests use an internal picker-service seam instead of opening modal dialogs.

## Clipboard [#clipboard]

Reactor has no clipboard wrapper. Call the WinRT
`Windows.ApplicationModel.DataTransfer.Clipboard` API from a UI-thread event
handler. Put the data in a `DataPackage` to copy it. To paste, read
`Clipboard.GetContent()` and check the format first.

```csharp
// Reactor has no clipboard wrapper. Call the WinRT Clipboard API
// from a UI-thread event handler.
class ClipboardDemo : Component
{
    public override Element Render()
    {
        var (text, setText) = UseState("Hello from Reactor");
        var (status, setStatus) = UseState("");

        return VStack(8,
            TextBox(text, setText, header: "Text to copy"),
            HStack(8,
                Button("Copy", () =>
                {
                    var package = new DataPackage();
                    package.SetText(text);
                    try
                    {
                        Clipboard.SetContent(package);
                        setStatus("Copied.");
                    }
                    catch (COMException)
                    {
                        // Another process can hold the clipboard open.
                        setStatus("The clipboard is busy. Try again.");
                    }
                }),
                Button("Paste", async () =>
                {
                    var content = Clipboard.GetContent();
                    if (content.Contains(StandardDataFormats.Text))
                        setText(await content.GetTextAsync());
                })),
            TextBlock(status));
    }
}
```

Caveats:

- Another process can hold the clipboard open. Clipboard calls can then throw
  a `COMException`. Catch it and tell the user, as the example does.
- Text controls such as `TextBox` already support Ctrl+C, Ctrl+X and Ctrl+V.
  You only need this API for your own copy and paste commands.

Related pages:

- [Standard commands](/microsoft-ui-reactor/main/guides/commanding#standard-commands) gives you `Cut`,
  `Copy` and `Paste` commands with the usual keyboard shortcuts and icons.
  You supply the action.
- [TextBlock modifiers](/microsoft-ui-reactor/main/controls/text-and-media#textblock-modifiers) include
  `.IsTextSelectionEnabled()`, which lets users select and copy read-only
  text.
- [Selecting across an assembled message](/microsoft-ui-reactor/main/controls/text-and-media#selecting-across-an-assembled-message)
  covers copy for Markdown content.

## Drag and drop [#drag-and-drop]

Drag and drop is part of the input system, so it is documented on
[Input and gestures](/microsoft-ui-reactor/main/guides/input-and-gestures#drag-and-drop). Start there for:

- [Typed in-process payloads](/microsoft-ui-reactor/main/guides/input-and-gestures#typed-in-process-payloads)
  to move objects inside your app.
- [Dropped files](/microsoft-ui-reactor/main/guides/input-and-gestures#dropped-files-prefer-trygetsafelocalfiles)
  from File Explorer. Use `TryGetSafeLocalFiles`, because a file drop from
  another process is untrusted input.
- [Drag to reorder](/microsoft-ui-reactor/main/guides/recipes/drag-reorder), a complete recipe for sortable
  lists. [Collections](/microsoft-ui-reactor/main/controls/collections#drag-to-reorder) covers reorder in list
  controls.

> **Caveat:** Three facts about this page are easy to get wrong. First, `UseTrayIcon` is a
> hook, but the `Pick*` methods are not: call pickers only from event handlers,
> never from `Render()`. Second, a tray icon, a jump list and a file dialog each
> need a different icon or source kind. Tray and overlay icons cannot load
> `ms-appx:///` resources, and packaged jump lists need them. Third, Reactor
> reads launch activation once at startup and has no single-instance support,
> no file-type registration and no API to send notifications. Do not invent a
> Reactor API for these. Use the Windows App SDK directly.

## Patterns [#patterns]

### Close to the tray [#close-to-the-tray]

Many tray apps keep running when the user closes the main window. Cancel a
user close in the window's `Closing` event and hide the window instead. The
tray menu then shows it again with `Show()` and `Activate()`, as `BringBack`
does in the tray menu example.

```csharp
class DesktopShell : Component<string>
{
    public override Element Render()
    {
        var window = UseWindow();

        UseEffect(() =>
        {
            if (window is null) return () => { };

            // Only a user close (caption button, Alt+F4) becomes a hide.
            // A close from app code (Reason == AppClosed) goes through.
            void onClosing(object? s, WindowClosingEventArgs e)
            {
                if (e.Reason != WindowCloseReason.UserClosed) return;
                e.Cancel = true;
                window.Hide();
            }

            window.Closing += onClosing;
            return () => window.Closing -= onClosing;
        }, window ?? (object)"no-window");

        return VStack(16,
            TextBlock(Props),
            Component<TrayMenuHost>(),
            Component<ClipboardDemo>()
        ).Padding(24);
    }
}
```

`WindowClosingEventArgs.Reason` tells you who closed the window. `UserClosed`
means the caption button, the system menu or Alt+F4. `AppClosed` means app
code called `Close()`, so code can still close the window for real. Give the
user a real way to quit, such as the "Quit" button in the tray menu, which
calls `ReactorApp.Exit()`.

### One deep-link format for every entry point [#one-deep-link-format-for-every-entry-point]

Use the same URI format for jump-list entries, protocol links and tray
commands. Then one `DeepLinkMap` in the startup callback handles all of them,
and validation lives in one place. See [Navigation](/microsoft-ui-reactor/main/guides/navigation) for
`DeepLinkMap`.

### Keep the tray icon in the root component [#keep-the-tray-icon-in-the-root-component]

Call `UseTrayIcon` in a component that stays mounted for the whole session,
such as the root of the primary window. A tray icon in a page component
disappears when the user navigates away. Use `ReactorApp.OpenTrayIcon` when
the icon must outlive every window.

## Common Mistakes [#common-mistakes]

### Calling a picker during render [#calling-a-picker-during-render]

```csharp
// Don't: Render() runs often. This opens a dialog on every render.
var file = PickFileAsync(new FilePickerOptions());
```

Call pickers from a button or command handler, as the
[pickers example](#file-and-folder-pickers) does.

### Acting on activation arguments without checking them [#acting-on-activation-arguments-without-checking-them]

`ctx.LaunchActivation.Arguments` can hold any string. Do not pass it to
`Process.Start`, a file path API or a database query as is. Resolve it
through a `DeepLinkMap`, or parse it and reject what you do not expect.

### Loading a tray icon from an ms-appx URI [#loading-a-tray-icon-from-an-ms-appx-uri]

`WindowIcon.FromResource("ms-appx:///Assets/Tray.ico")` does not work for a
tray icon, because the tray needs a raw `HICON`. Use `FromPath` with a file
next to the app, or `FromBytes` with embedded data.

### Forgetting the AppUserModelId in an unpackaged app [#forgetting-the-appusermodelid-in-an-unpackaged-app]

An unpackaged app must set `JumpList.AppUserModelId` before the first
`UpdateAsync` or `GetRemovedArgumentsAsync`. Without it, both throw
`InvalidOperationException`. A packaged app gets its identity from the
manifest.

## Tips [#tips]

**Unsubscribe in the effect cleanup.** Tray and window events are .NET
events. Subscribe in `UseEffect` and remove the handler in the cleanup, as
every example on this page does.

**Give tray icons a `WindowKey`.** A key lets `UseTrayIcon` reuse the same
icon across renders, and lets other code find it with
`ReactorApp.FindTrayIcon(key)`.

**Keep the flyout small.** The tray flyout has a fixed size and does not grow
with its content. Put a few actions in it and open a full window for
anything larger.

**Test without the shell.** `UseTrayIcon` returns `null` in headless tests,
and the pickers use an internal test seam. Keep the logic that runs on a
click in plain methods that you can test on their own. See
[Testing](/microsoft-ui-reactor/main/guides/testing).

## Next Steps [#next-steps]

- **[Windows](/microsoft-ui-reactor/main/guides/windows)** — window lifecycle, icons, displays and shutdown policy
- **[Navigation](/microsoft-ui-reactor/main/guides/navigation)** — `DeepLinkMap` for jump lists and links
- **[Input and gestures](/microsoft-ui-reactor/main/guides/input-and-gestures)** — drag and drop and pointer input
- **[Commanding](/microsoft-ui-reactor/main/guides/commanding)** — standard commands for menus and the tray
- **[Text and media](/microsoft-ui-reactor/main/controls/text-and-media)** — text selection and copy
- **[Packaging](/microsoft-ui-reactor/main/guides/packaging)** — package identity, which changes icons and jump lists
