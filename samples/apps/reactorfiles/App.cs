// ReactorFiles — A read-only file explorer showcasing Reactor + WinUI performance.
// No XAML. No data binding. Virtualized lists, lazy-loading TreeView, filesystem watching.

using System.Diagnostics;
using System.Diagnostics.Tracing;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using ReactorFiles.Components;
using ReactorFiles.Models;
using ReactorFiles.Services;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<ReactorFilesApp>("ReactorFiles", width: 1200, height: 800,
    configure: host => CursorBorderRegistration.Register(host.Reconciler));

// ─── Root component ───────────────────────────────────────────────────────────

class ReactorFilesApp : Component
{
    public override Element Render()
    {
        // ── State ──────────────────────────────────────────────────────
        var (currentPath, setCurrentPath) = UseState("");
        var (files, setFiles) = UseState<FileEntry[]>([]);
        var (viewMode, setViewMode) = UseState(ViewMode.Details);
        var (filter, setFilter) = UseState("");
        var (sortField, setSortField) = UseState(SortField.Name);
        var (sortDirection, setSortDirection) = UseState(SortDirection.Ascending);
        var (isLoading, setIsLoading) = UseState(false);

        // Tree state: combined into a single record so expand is one state update → one render
        var (treeState, setTreeState) = UseState((
            Expanded: new HashSet<string>(),
            Children: new Dictionary<string, FileEntry[]>()
        ));
        var expandedPaths = treeState.Expanded;
        var treeChildren = treeState.Children;

        // Watcher ref for cleanup
        var watcherRef = UseRef<FileWatcherService?>(null);

        // ── Enumerate directory on path change ─────────────────────────
        UseEffect((Action)(() =>
        {
            if (string.IsNullOrEmpty(currentPath)) return;

            setIsLoading(true);
            var syncContext = SynchronizationContext.Current;

            Task.Run(async () =>
            {
                var result = await FileSystemService.EnumerateDirectoryAsync(currentPath);
                syncContext?.Post(_ =>
                {
                    setFiles(result);
                    setIsLoading(false);
                }, null);
            });
        }), currentPath);

        // ── File watcher on path change ────────────────────────────────
        UseEffect((Func<Action>)(() =>
        {
            watcherRef.Current?.Dispose();
            watcherRef.Current = null;

            if (!string.IsNullOrEmpty(currentPath))
            {
                try
                {
                    var syncContext = SynchronizationContext.Current;
                    watcherRef.Current = new FileWatcherService(currentPath, () =>
                    {
                        // Re-enumerate on change, marshal to UI thread
                        Task.Run(async () =>
                        {
                            var result = await FileSystemService.EnumerateDirectoryAsync(currentPath);
                            syncContext?.Post(_ => setFiles(result), null);
                        });
                    });
                }
                catch
                {
                    // Watcher can fail on network paths, etc.
                }
            }

            return () =>
            {
                watcherRef.Current?.Dispose();
                watcherRef.Current = null;
            };
        }), currentPath);

        // ── Filtered + sorted file list (memoized) ────────────────────
        var displayFiles = UseMemo<IReadOnlyList<FileEntry>>(() =>
        {
            IEnumerable<FileEntry> result = files;

            // Filter
            if (!string.IsNullOrEmpty(filter))
            {
                result = result.Where(f =>
                    f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            // Sort: directories first, then by selected field
            result = (sortField, sortDirection) switch
            {
                (SortField.Name, SortDirection.Ascending) =>
                    result.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase),
                (SortField.Name, SortDirection.Descending) =>
                    result.OrderByDescending(f => f.IsDirectory).ThenByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase),
                (SortField.Size, SortDirection.Ascending) =>
                    result.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Size),
                (SortField.Size, SortDirection.Descending) =>
                    result.OrderByDescending(f => f.IsDirectory).ThenByDescending(f => f.Size),
                (SortField.Modified, SortDirection.Ascending) =>
                    result.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Modified),
                (SortField.Modified, SortDirection.Descending) =>
                    result.OrderByDescending(f => f.IsDirectory).ThenByDescending(f => f.Modified),
                (SortField.Type, SortDirection.Ascending) =>
                    result.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.TypeDescription, StringComparer.OrdinalIgnoreCase),
                (SortField.Type, SortDirection.Descending) =>
                    result.OrderByDescending(f => f.IsDirectory).ThenByDescending(f => f.TypeDescription, StringComparer.OrdinalIgnoreCase),
                _ =>
                    result.OrderByDescending(f => f.IsDirectory).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            };

            return result.ToArray();
        }, files, filter, sortField, sortDirection);

        // ── Handlers ───────────────────────────────────────────────────
        void Navigate(string path)
        {
            if (Directory.Exists(path))
                setCurrentPath(path);
        }

        void ExpandTreeNode(string path)
        {
            if (expandedPaths.Contains(path)) return;

            Trace.WriteLine($"[ReactorFiles] ExpandTreeNode START: {path}");
            var sw = Stopwatch.StartNew();

            var syncContext = SynchronizationContext.Current;
            Task.Run(async () =>
            {
                var swEnum = Stopwatch.StartNew();
                var subdirs = await FileSystemService.EnumerateSubdirsAsync(path);
                swEnum.Stop();
                Trace.WriteLine($"[ReactorFiles] EnumerateSubdirs: {swEnum.ElapsedMilliseconds}ms, {subdirs.Length} items");

                syncContext?.Post(_ =>
                {
                    var swState = Stopwatch.StartNew();

                    var newChildren = new Dictionary<string, FileEntry[]>(treeChildren)
                    {
                        [path] = subdirs
                    };
                    var newExpanded = new HashSet<string>(expandedPaths) { path };

                    // Single state update → single render cycle
                    setTreeState((Expanded: newExpanded, Children: newChildren));

                    swState.Stop();
                    Trace.WriteLine($"[ReactorFiles] SetState (UI thread): {swState.ElapsedMilliseconds}ms");
                    Trace.WriteLine($"[ReactorFiles] ExpandTreeNode TOTAL: {sw.ElapsedMilliseconds}ms");
                }, null);
            });
        }

        void OnItemActivated(FileEntry file)
        {
            if (file.IsDirectory)
                Navigate(file.FullPath);
        }

        // ── Layout ─────────────────────────────────────────────────────
        var toolbar = Component<Toolbar, ToolbarProps>(new ToolbarProps(
            CurrentPath: currentPath,
            ViewMode: viewMode,
            Filter: filter,
            SortField: sortField,
            SortDirection: sortDirection,
            IsLoading: isLoading,
            OnViewModeChanged: setViewMode,
            OnFilterChanged: setFilter,
            OnSortFieldChanged: setSortField,
            OnToggleSortDirection: () => setSortDirection(
                sortDirection == SortDirection.Ascending
                    ? SortDirection.Descending
                    : SortDirection.Ascending),
            OnNavigate: Navigate
        ));

        var tree = Component<DirectoryTree, DirectoryTreeProps>(new DirectoryTreeProps(
            ExpandedPaths: expandedPaths,
            TreeChildren: treeChildren,
            CurrentPath: currentPath,
            OnNavigate: Navigate,
            OnExpand: ExpandTreeNode
        ));

        var fileList = Component<FileListPane, FileListPaneProps>(new FileListPaneProps(
            Files: displayFiles,
            ViewMode: viewMode,
            OnItemActivated: OnItemActivated
        ));

        // The outer VStack must not stretch — use a Grid so toolbar gets Auto height
        // and the content area fills remaining space.
        return Grid(
            [GridSize.Star()],
            [GridSize.Auto, GridSize.Star()],
            toolbar.Grid(row: 0, column: 0),
            Component<SplitPanel, SplitPanelProps>(new SplitPanelProps(
                Left: tree,
                Right: fileList
            )).Grid(row: 1, column: 0)
        )
        // Spec 033 §6 — Mica window backdrop fits a file-manager surface.
        .Backdrop(BackdropKind.Mica);
    }
}

// ─── ETW EventSource for VS profiler integration ──────────────────────────────

[EventSource(Name = "ReactorFiles")]
sealed class ReactorFilesEvents : EventSource
{
    public static readonly ReactorFilesEvents Log = new();

    [Event(1, Level = EventLevel.Informational, Opcode = EventOpcode.Start)]
    public void ExpandTreeStart(string path) => WriteEvent(1, path);

    [Event(2, Level = EventLevel.Informational, Opcode = EventOpcode.Stop)]
    public void ExpandTreeStop(string path, int itemCount, long elapsedMs) => WriteEvent(2, path, itemCount, elapsedMs);

    [Event(3, Level = EventLevel.Informational, Opcode = EventOpcode.Start)]
    public void EnumerateStart(string path) => WriteEvent(3, path);

    [Event(4, Level = EventLevel.Informational, Opcode = EventOpcode.Stop)]
    public void EnumerateStop(string path, int itemCount, long elapsedMs) => WriteEvent(4, path, itemCount, elapsedMs);

    [Event(5, Level = EventLevel.Informational, Opcode = EventOpcode.Start)]
    public void SetStateStart() => WriteEvent(5);

    [Event(6, Level = EventLevel.Informational, Opcode = EventOpcode.Stop)]
    public void SetStateStop(long elapsedMs) => WriteEvent(6, elapsedMs);

    [Event(7, Level = EventLevel.Informational, Opcode = EventOpcode.Start)]
    public void BuildNodesStart(int expandedCount) => WriteEvent(7, expandedCount);

    [Event(8, Level = EventLevel.Informational, Opcode = EventOpcode.Stop)]
    public void BuildNodesStop(int rootNodeCount, long elapsedMs) => WriteEvent(8, rootNodeCount, elapsedMs);
}
