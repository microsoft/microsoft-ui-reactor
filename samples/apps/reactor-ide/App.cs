// Reactor IDE — a single cohesive sample app built on the Reactor docking
// system. It boots straight into a Visual-Studio-shaped layout:
//
//   ┌───────────── menu bar ─────────────────────────────────────────┐
//   │ Solution    │            editor well            │  Properties / │
//   │ Explorer    │        (open document tabs)       │  Git Changes  │
//   │             ├───────────────────────────────────┴───────────────┤
//   │             │      Output · Terminal · Error List (bottom)        │
//   └───────────── status bar ───────────────────────────────────────┘
//
// Everything is wired so it feels like one app, not a feature gallery:
//   • Solution Explorer files open as editor documents in the DocumentArea.
//   • The editor well is a DocumentArea — close every tab and it stays visible.
//   • Tool windows (Solution Explorer / Properties / Git / Output …) live in
//     ToolWindowStrip groups and can be dragged, floated, and re-docked.
//   • View ▸ Reset Layout restores the default arrangement.
//
// Ownership model (spec 045 §2.30 — the important bit): the APP owns
// CONTENT (which files are open, which is active) and the dock HOST owns
// the user's drag-modified SHAPE internally. We declare `manager.Layout`
// fresh each render from our own state and never feed the host's live
// layout back into it. See the docking agent-kit skill for why round-
// tripping OnLiveLayoutChanged into state breaks float/redock + selection.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;
using static Microsoft.UI.Reactor.Core.Theme;

ReactorApp.Run<IdeApp>(
    title: "Reactor IDE",
    width: 1280,
    height: 820,
    devtools: true,
    configure: host => DockingNativeInterop.Register(host.Reconciler));

// ════════════════════════════════════════════════════════════════════════
//  Root — menu bar + dock host + status bar
// ════════════════════════════════════════════════════════════════════════

class IdeApp : Component
{
    // The default set of open editor documents (the editor well's tabs).
    static readonly ImmutableList<ProjectFile> DefaultDocs =
        ImmutableList.Create(ProjectFiles.AppCs, ProjectFiles.MainView);

    public override Element Render()
    {
        // Idiomatic docking ownership (spec 045 §2.30): the *app* owns
        // CONTENT — here, which files are open in the editor well and which
        // is active — while the dock host owns the user's drag-modified
        // SHAPE internally. We never round-trip the host's live layout back
        // into our state; instead, opening/closing a file changes the set of
        // pane Keys in manager.Layout, which the host detects and merges.
        // Reset is a `.WithKey(...)` remount that clears the host's shape.
        var (openDocs, setOpenDocs) = UseState(DefaultDocs);
        var (activeKey, setActiveKey) = UseState<object?>(ProjectFiles.MainView.Key);
        var (status, setStatus) = UseState("Ready");
        var (epoch, bumpEpoch) = UseReducer(0);

        // ── Open a project file as an editor document ────────────────────
        // Shared by File ▸ Open and the Solution Explorer. Re-opening an
        // already-open file just activates its tab (as a real IDE would).
        void OpenFile(ProjectFile file)
        {
            if (openDocs.Any(d => Equals(d.Key, file.Key)))
            {
                setActiveKey(file.Key);
                setStatus($"{file.Name} is already open");
                return;
            }
            setOpenDocs(openDocs.Add(file));
            setActiveKey(file.Key);
            setStatus($"Opened {file.Name}");
        }

        void ResetLayout()
        {
            setOpenDocs(DefaultDocs);
            setActiveKey(ProjectFiles.MainView.Key);
            bumpEpoch(e => e + 1);   // remount the host → clears drag shape
            setStatus("Layout reset");
        }

        // The editor well holds one tab per open document. SelectedIndex
        // tracks the active file so opening one focuses it; clicking a tab
        // switches natively (the host doesn't re-render on tab clicks).
        var activeIndex = openDocs.FindIndex(d => Equals(d.Key, activeKey));
        var editorWell = new DockTabGroup(
            openDocs.Select(FileDocument).ToArray(),
            SelectedIndex: activeIndex,
            ShowWhenEmpty: true,
            Role: DockGroupRole.DocumentArea);

        var layout = new DockSplit(
            Orientation.Vertical,
            new DockNode[]
            {
                new DockSplit(
                    Orientation.Horizontal,
                    new DockNode[]
                    {
                        new DockTabGroup(
                            new DockableContent[] { SolutionExplorerTool(OpenFile) },
                            Width: 260,
                            Role: DockGroupRole.ToolWindowStrip),

                        editorWell,

                        new DockTabGroup(
                            new DockableContent[] { PropertiesTool(), GitChangesTool() },
                            Width: 300,
                            TabPosition: TabPosition.Bottom,
                            CompactTabs: true,
                            Role: DockGroupRole.ToolWindowStrip),
                    }),

                new DockTabGroup(
                    new DockableContent[] { OutputTool(), TerminalTool(), ErrorListTool() },
                    Height: 220,
                    TabPosition: TabPosition.Bottom,
                    CompactTabs: true,
                    Role: DockGroupRole.ToolWindowStrip),
            });

        var dock = new DockManager
        {
            Layout = layout,
            // Keep our open-doc state in sync when the host closes a tab via
            // its X button. The host removes the pane from its own shape; we
            // drop it from openDocs so the key set converges and re-opening
            // works.
            OnDocumentClosing = args =>
            {
                setOpenDocs(openDocs.RemoveAll(d => Equals(d.Key, args.Document.Key)));
                setStatus($"Closed {args.Document.Title}");
            },
        }.WithKey($"dock-{epoch}");

        return Grid(
            new[] { GridSize.Star(1) },
            new[] { GridSize.Auto, GridSize.Star(1), GridSize.Auto },
            BuildMenuBar(OpenFile, ResetLayout, setStatus).Grid(row: 0),
            dock.Grid(row: 1),
            StatusBar(status).Grid(row: 2));
    }

    // ── Menu bar ─────────────────────────────────────────────────────────

    static Element BuildMenuBar(Action<ProjectFile> openFile, Action resetLayout, Action<string> setStatus)
    {
        Microsoft.UI.Reactor.Core.MenuFlyoutItemBase OpenItem(ProjectFile f) => MenuItem(f.Name, () => openFile(f));

        return MenuBar(
            Menu("File",
                MenuSubItem("Open",
                    OpenItem(ProjectFiles.AppCs),
                    OpenItem(ProjectFiles.MainView),
                    OpenItem(ProjectFiles.MainViewModel),
                    OpenItem(ProjectFiles.Readme)),
                MenuSeparator(),
                MenuItem("Save", () => setStatus("Saved")) with
                {
                    KeyboardAccelerators = new[]
                    {
                        Accelerator(global::Windows.System.VirtualKey.S, global::Windows.System.VirtualKeyModifiers.Control),
                    },
                },
                MenuItem("Save All", () => setStatus("Saved all documents")) with
                {
                    KeyboardAccelerators = new[]
                    {
                        Accelerator(global::Windows.System.VirtualKey.S,
                            global::Windows.System.VirtualKeyModifiers.Control | global::Windows.System.VirtualKeyModifiers.Shift),
                    },
                },
                MenuSeparator(),
                MenuItem("Exit", () => Application.Current.Exit())),

            Menu("Edit",
                MenuItem("Undo", () => setStatus("Undo")),
                MenuItem("Redo", () => setStatus("Redo")),
                MenuSeparator(),
                MenuItem("Cut", () => setStatus("Cut")),
                MenuItem("Copy", () => setStatus("Copy")),
                MenuItem("Paste", () => setStatus("Paste"))),

            Menu("View",
                MenuItem("Reset Layout", resetLayout),
                MenuSeparator(),
                MenuItem("Solution Explorer", () => setStatus("Solution Explorer")),
                MenuItem("Properties", () => setStatus("Properties")),
                MenuItem("Terminal", () => setStatus("Terminal"))),

            Menu("Git",
                MenuItem("Commit…", () => setStatus("Open the Git Changes panel to commit")),
                MenuItem("Pull", () => setStatus("Pulling origin/main…")),
                MenuItem("Push", () => setStatus("Pushing to origin…"))),

            Menu("Help",
                MenuItem("About Reactor IDE", () =>
                    setStatus("Reactor IDE — a docking sample built on Microsoft.UI.Reactor"))));
    }

    // ── Status bar ─────────────────────────────────────────────────────────

    static Element StatusBar(string status) =>
        HStack(16,
            TextBlock(status).FontSize(12).Flex(grow: 1),
            TextBlock("⎇ feat/docking").FontSize(12).Opacity(0.8),
            TextBlock("Ln 1, Col 1").FontSize(12).Opacity(0.8),
            TextBlock("Spaces: 4").FontSize(12).Opacity(0.8),
            TextBlock("UTF-8").FontSize(12).Opacity(0.8))
        .Padding(12, 4, 12, 4)
        .Background(LayerFill);

    // ════════════════════════════════════════════════════════════════════
    //  Tool windows
    // ════════════════════════════════════════════════════════════════════

    static ToolWindow SolutionExplorerTool(Action<ProjectFile> openFile) => new()
    {
        Title = "Solution Explorer",
        Key = "tool:solution",
        CanPin = true,
        Content = Memo(ctx =>
        {
            var (filter, setFilter) = ctx.UseState(string.Empty);

            bool Matches(ProjectFile f) =>
                filter.Length == 0 || f.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);

            Element FileRow(ProjectFile f, int indent) =>
                HyperlinkButton($"📄 {f.Name}", onClick: () => openFile(f))
                    .Margin(indent * 14.0, 0, 0, 0);

            var srcFiles = new[] { ProjectFiles.AppCs, ProjectFiles.MainView, ProjectFiles.MainViewModel }
                .Where(Matches)
                .Select(f => FileRow(f, 2))
                .ToArray();

            var rootFiles = new[] { ProjectFiles.Readme, ProjectFiles.Csproj }
                .Where(Matches)
                .Select(f => f.Key == ProjectFiles.Csproj.Key
                    ? (Element)TextBlock($"📄 {f.Name}").Margin(14, 0, 0, 0).Opacity(0.7)
                    : FileRow(f, 1))
                .ToArray();

            return VStack(2,
                TextBox(filter, setFilter, placeholderText: "Search files… (Ctrl+;)"),
                TextBlock("📂 ReactorIde").SemiBold().Margin(0, 6, 0, 0),
                TextBlock("📂 src").Margin(14, 0, 0, 0).Opacity(0.85),
                VStack(2, srcFiles),
                VStack(2, rootFiles)
            ).Padding(8);
        }),
    };

    static ToolWindow PropertiesTool() => new()
    {
        Title = "Properties",
        Key = "tool:properties",
        CanPin = true,
        Content = Memo(ctx =>
        {
            Element Row(string name, string value) =>
                HStack(8,
                    TextBlock(name).Opacity(0.7).Width(110).FontSize(12),
                    TextBlock(value).FontSize(12));

            return VStack(4,
                TextBlock("DockManager").SemiBold(),
                Row("Build Action", "C# compile"),
                Row("Copy to Output", "Do not copy"),
                Row("Namespace", "ReactorIde"),
                Row("Persistence Id", "reactor-ide:main"),
                Row("Encoding", "UTF-8")
            ).Padding(10);
        }),
    };

    static ToolWindow GitChangesTool() => new()
    {
        Title = "Git Changes",
        Key = "tool:git",
        CanPin = true,
        Content = Memo(ctx =>
        {
            var (message, setMessage) = ctx.UseState(string.Empty);
            var (history, setHistory) = ctx.UseState<List<string>>(new List<string>());

            void Commit()
            {
                if (string.IsNullOrWhiteSpace(message)) return;
                setHistory(new List<string>(history) { message.Trim() });
                setMessage(string.Empty);
            }

            var changes = new[]
            {
                "  M  src/App.cs",
                "  M  src/MainView.xaml",
                "  A  README.md",
            };
            var changeRows = changes
                .Select(c => (Element)TextBlock(c).FontFamily("Consolas, monospace").FontSize(12))
                .ToArray();
            var historyRows = history.Count == 0
                ? new Element[] { TextBlock("(no commits yet)").Opacity(0.5).FontSize(11) }
                : history.Select(h => (Element)TextBlock($"✓ {h}").Opacity(0.75).FontSize(12)).ToArray();

            return VStack(6,
                TextBlock("Changes").SemiBold(),
                VStack(2, changeRows),
                TextBox(message, setMessage, placeholderText: "Commit message…")
                    .Set(tb => { tb.AcceptsReturn = true; tb.MinHeight = 56; })
                    .Margin(0, 6, 0, 0),
                Button("Commit", Commit),
                TextBlock("History").SemiBold().Margin(0, 8, 0, 0),
                VStack(2, historyRows)
            ).Padding(10);
        }),
    };

    static ToolWindow OutputTool() => new()
    {
        Title = "Output",
        Key = "tool:output",
        CanPin = true,
        Content = Memo(ctx =>
        {
            var (log, setLog) = ctx.UseState<List<string>>(new List<string>
            {
                "[12:34:01] ------ Build started: Project: ReactorIde ------",
                "[12:34:18] Build succeeded.",
                "[12:34:18] ========== Build: 1 succeeded, 0 failed ==========",
            });
            var (entry, setEntry) = ctx.UseState(string.Empty);

            void Append()
            {
                if (string.IsNullOrWhiteSpace(entry)) return;
                setLog(new List<string>(log) { $"[{DateTime.Now:HH:mm:ss}] {entry.Trim()}" });
                setEntry(string.Empty);
            }

            var lines = log
                .Select(l => (Element)TextBlock(l).FontFamily("Consolas, monospace").FontSize(12).Opacity(0.85))
                .ToArray();

            return VStack(6,
                VStack(2, lines),
                HStack(6,
                    TextBox(entry, setEntry, placeholderText: "Append output line… (Enter)")
                        .Set(tb => tb.KeyDown += (_, e) =>
                        {
                            if (e.Key == global::Windows.System.VirtualKey.Enter) { e.Handled = true; Append(); }
                        })
                        .Flex(grow: 1),
                    Button("Append", Append))
            ).Padding(10);
        }),
    };

    static ToolWindow TerminalTool() => new()
    {
        Title = "Terminal",
        Key = "tool:terminal",
        CanPin = true,
        Content = Memo(ctx =>
        {
            var (history, setHistory) = ctx.UseState<List<string>>(new List<string>
            {
                "PS C:\\code\\reactor-ide> git status",
                "On branch feat/docking",
                "nothing to commit, working tree clean",
            });
            var (input, setInput) = ctx.UseState(string.Empty);

            void Run()
            {
                if (string.IsNullOrWhiteSpace(input)) return;
                setHistory(new List<string>(history)
                {
                    $"PS C:\\code\\reactor-ide> {input}",
                    $"(simulated output for: {input.Trim()})",
                });
                setInput(string.Empty);
            }

            var lines = history
                .Select(l => (Element)TextBlock(l).FontFamily("Consolas, monospace").FontSize(12))
                .ToArray();

            return VStack(4,
                VStack(2, lines),
                HStack(6,
                    TextBlock("PS>").FontFamily("Consolas, monospace").SemiBold(),
                    TextBox(input, setInput, placeholderText: "Type a command and press Enter…")
                        .Set(tb =>
                        {
                            tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, monospace");
                            tb.KeyDown += (_, e) =>
                            {
                                if (e.Key == global::Windows.System.VirtualKey.Enter) { e.Handled = true; Run(); }
                            };
                        })
                        .Flex(grow: 1))
            ).Padding(10);
        }),
    };

    static ToolWindow ErrorListTool() => new()
    {
        Title = "Error List",
        Key = "tool:errors",
        CanPin = true,
        Content = Memo(ctx =>
        {
            var (filter, setFilter) = ctx.UseState(string.Empty);
            var entries = new[]
            {
                "CS8602  Possible null dereference   MainViewModel.cs(42,17)",
                "CS0618  'Foo' is obsolete            App.cs(13,5)",
                "IL2080  Reflection mismatch          MainView.xaml.cs(297,21)",
            };
            var visible = filter.Length == 0
                ? entries
                : entries.Where(e => e.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
            var rows = visible
                .Select(e => (Element)TextBlock(e).FontFamily("Consolas, monospace").FontSize(12))
                .ToArray();

            return VStack(6,
                TextBlock($"⚠ 0 Errors    ⚠ {entries.Length} Warnings    ℹ 1 Message").Opacity(0.8),
                TextBox(filter, setFilter, placeholderText: "Filter by code, file, or message…"),
                VStack(2, rows)
            ).Padding(10);
        }),
    };

    // ════════════════════════════════════════════════════════════════════
    //  Editor documents
    // ════════════════════════════════════════════════════════════════════

    static Document FileDocument(ProjectFile file) => new()
    {
        Title = file.Name,
        Key = file.Key,
        CanClose = true,
        Content = EditorPane(file),
    };

    // A controlled multi-line editor. Memo holds its own UseState slot so
    // local edits survive parent re-renders and docking layout swaps.
    // The TextBox stretches to fill the pane (otherwise it collapses to a
    // single content line inside the dock host's content presenter).
    static Element EditorPane(ProjectFile file) =>
        Memo(ctx =>
        {
            var (text, setText) = ctx.UseState(file.Body);
            return FlexColumn(
                TextBox(text, setText)
                    // AcceptsReturn / TextWrapping MUST be element props, not
                    // a .Set(...) lambda: the descriptor applies them before
                    // Text (single-line mode truncates Text at the first
                    // newline). A .Set lambda runs *after* Text is assigned,
                    // so the multi-line body would collapse to one line.
                    .AcceptsReturn()
                    .TextWrapping(TextWrapping.NoWrap)
                    .Set(tb =>
                    {
                        tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Code, Consolas, monospace");
                        tb.FontSize = 13;
                        tb.HorizontalAlignment = HorizontalAlignment.Stretch;
                        tb.VerticalAlignment = VerticalAlignment.Stretch;
                        tb.VerticalContentAlignment = VerticalAlignment.Top;
                    })
                    .Flex(grow: 1, basis: 0))
                .Flex(grow: 1);
        });
}

// ════════════════════════════════════════════════════════════════════════
//  Project files — the "solution" the IDE edits
// ════════════════════════════════════════════════════════════════════════

record ProjectFile(string Name, object Key, string Body);

static class ProjectFiles
{
    public static readonly ProjectFile AppCs = new(
        "App.cs", "file:app-cs",
        "using Microsoft.UI.Reactor;\n\n" +
        "ReactorApp.Run<MainView>(title: \"ReactorIde\");\n");

    public static readonly ProjectFile MainView = new(
        "MainView.xaml", "file:mainview",
        "<Page xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">\n" +
        "  <Grid>\n    <!-- edit me -->\n  </Grid>\n</Page>\n");

    public static readonly ProjectFile MainViewModel = new(
        "MainViewModel.cs", "file:mainvm",
        "public sealed class MainViewModel\n{\n    public string Title => \"ReactorIde\";\n}\n");

    public static readonly ProjectFile Readme = new(
        "README.md", "file:readme",
        "# ReactorIde\n\nA sample IDE built on the Reactor docking system.\n");

    public static readonly ProjectFile Csproj = new(
        "ReactorIde.csproj", "file:csproj", string.Empty);
}
