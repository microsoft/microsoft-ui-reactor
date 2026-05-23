// Dock Showcase — exercises every feature in the WinUI.Dock matrix
// surfaced by Reactor's Phase 1 docking wrapper. Drives the human-in-the-
// loop review for spec 045 §4.7 (sit it next to Example.WinUI and run
// down the 8-item script).
//
// Six scenes mirrored from the spec's review script:
//   A — IDE layout: solution / editor / properties / log
//   B — Floating tear-out
//   C — Side pin / auto-hide
//   D — Compact + bottom tabs
//   E — Persistence menu (Save / Load via PersistenceId)
//   F — Programmatic dock (button issues DockTo)
//
// Each scene is its own component so a reviewer can switch between them
// via the side menu without relaunching the app.

using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Docking;
using Microsoft.UI.Reactor.Docking.Diagnostics;
using Microsoft.UI.Reactor.Docking.Native;
using Microsoft.UI.Reactor.Docking.Persistence;
using Microsoft.UI.Reactor.Hooks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Microsoft.UI.Reactor.Factories;

// Phase 2 native renderer (spec 045 §5.1 / §2.16). The Phase-1
// WinUI.Dock A/B flip was removed at the §2.19 chrome-removal pass —
// the native renderer is now the only path.
ReactorApp.Run<DockShowcaseRoot>(
    title: "Reactor Docking Showcase",
    width: 1200,
    height: 800,
    devtools: true,
    configure: host => DockingNativeInterop.Register(host.Reconciler));

// ════════════════════════════════════════════════════════════════════════
//  Root — side menu to switch between scenes
// ════════════════════════════════════════════════════════════════════════

class DockShowcaseRoot : Component
{
    public override Element Render()
    {
        var (scene, setScene) = UseState("ide");

        var menu = VStack(0,
            // Title row with the devtools ⚡ trigger. Click ⚡ → flyout with
            // "Highlight reconcile changes" toggle (red = mounted,
            // yellow = modified). Built into ReactorApp when
            // devtools:true is passed at startup.
            HStack(6,
                TextBlock("Reactor Docking Showcase").SemiBold().Flex(grow: 1),
                DevtoolsMenu()
            ).Margin(8, 8, 8, 12),
            SceneButton("ide",          "Scene A — IDE Layout",        scene, setScene),
            SceneButton("floating",     "Scene B — Floating Tear-Out", scene, setScene),
            SceneButton("sidepin",      "Scene C — Side Pin",          scene, setScene),
            SceneButton("compact",      "Scene D — Compact / Bottom",  scene, setScene),
            SceneButton("persist",      "Scene E — Persistence",       scene, setScene),
            SceneButton("programmatic", "Scene F — Programmatic Dock", scene, setScene),
            SceneButton("sliders",      "Scene G — Slider Resize",     scene, setScene),
            SceneButton("droptargets",  "Scene H — Drop Targets",      scene, setScene),
            SceneButton("tabstyles",    "Scene I — Tab Styles",        scene, setScene)
        ).Width(240).Padding(8);

        Element body = scene switch
        {
            "ide"          => Component<SceneAIde>(),
            "floating"     => Component<SceneBFloating>(),
            "sidepin"      => Component<SceneCSidePin>(),
            "compact"      => Component<SceneDCompact>(),
            "persist"      => Component<SceneEPersistence>(),
            "programmatic" => Component<SceneFProgrammatic>(),
            "sliders"      => Component<SceneGSliders>(),
            "droptargets"  => Component<SceneHDropTargets>(),
            "tabstyles"    => Component<SceneITabStyles>(),
            _              => TextBlock("Unknown scene"),
        };

        return Grid(
            new[] { GridSize.Auto, GridSize.Star(1) },
            new[] { GridSize.Star(1) },
            menu.Grid(column: 0),
            body.Grid(column: 1));
    }

    static Element SceneButton(string id, string label, string current, Action<string> set)
        => Button(label, () => set(id))
            .HAlign(HorizontalAlignment.Stretch)
            .Margin(0, 2, 0, 2);
}

// ════════════════════════════════════════════════════════════════════════
//  Scene A — IDE layout
// ════════════════════════════════════════════════════════════════════════

class SceneAIde : Component
{
    public override Element Render()
    {
        // Mirrors WinUI.Dock's Example.WinUI/Views/MainView.xaml layout so the
        // §1.9 side-by-side review is apples-to-apples: outer vertical split
        // (top fills, bottom = 200dip), each half is a horizontal split, each
        // leaf is a DockTabGroup. The bottom row carries TabPosition.Bottom
        // DocumentGroups (Error List + Output/Terminal).

        // Live layout state — mirrors the host's effective layout via
        // OnLiveLayoutChanged so the JSON viewer panel can serialize it
        // on every render. The starting tree is the IDE-style layout
        // below; once the user drops a tab, setLiveLayout fires and
        // subsequent renders feed the new tree back through the host.
        var (liveLayout, setLiveLayout) = UseState<DockNode?>(BuildInitialLayout());

        // Shared ratios dict supplied via SplitRatios so the JSON viewer
        // sees splitter-drag results. The host mutates this in place;
        // OnSplitterDragCompleted bumps the tick to force a re-render.
        var ratiosRef = UseRef<Dictionary<string, double[]>>(new Dictionary<string, double[]>());
        var (_, bumpTick) = UseReducer(0);

        // Spec 045 — per-scene operation log + replay cursor. UseRef
        // holds the log instance across renders. The replay cursor is
        // tracked separately via UseState so scrubbing forces a
        // re-render that shows the snapshotted state in the JSON panel.
        var logRef = UseRef<DockOperationLog>(new DockOperationLog());
        var log = logRef.Current;

        var dock = new DockManager
        {
            PersistenceId = "dock-showcase:ide",
            SplitRatios = ratiosRef.Current,
            OperationLog = log,
            OnLiveLayoutChanged = newLayout => setLiveLayout(newLayout),
            OnSplitterDragCompleted = () => bumpTick(t => t + 1),
            Layout = liveLayout,
        };

        // Replay helper: apply the cursor's current snapshot back into
        // local state. The docking renderer will re-render with the
        // snapshotted layout + ratios.
        void ApplyCurrent()
        {
            var cur = log.Current;
            if (cur is null) return;
            // Restore ratios from the snapshot (clone so mutations
            // by the renderer don't corrupt the recorded one).
            ratiosRef.Current.Clear();
            if (cur.Ratios is not null)
                foreach (var kvp in cur.Ratios)
                {
                    var copy = new double[kvp.Value.Length];
                    Array.Copy(kvp.Value, copy, kvp.Value.Length);
                    ratiosRef.Current[kvp.Key] = copy;
                }
            setLiveLayout(cur.Layout);
            bumpTick(t => t + 1);
        }

        // Build the JSON viewer panel. Serialize the live layout via
        // the same DockLayoutSerializer used for WindowPersistedScope —
        // so what's shown is byte-identical to what would be saved.
        var layoutJson = SafeSerialize(liveLayout);
        var ratiosJson = SerializeRatios(ratiosRef.Current);
        var jsonPanel = VStack(8,
            TextBlock("Layout JSON").SemiBold(),
            TextBlock("(updates on drag / drop / splitter release)")
                .FontSize(11).Opacity(0.6),
            new ScrollViewElement(
                TextBlock(layoutJson)
                    .FontFamily("Consolas, Courier New, monospace")
                    .FontSize(11))
            {
                HorizontalScrollMode = ScrollingScrollMode.Auto,
                HorizontalScrollBarVisibility = ScrollingScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollingScrollBarVisibility.Auto,
            }.Height(360),
            TextBlock("Split ratios").SemiBold().Margin(0, 12, 0, 0),
            TextBlock(ratiosJson)
                .FontFamily("Consolas, Courier New, monospace")
                .FontSize(11)
                .Opacity(0.85),
            Button("Reset layout", () =>
            {
                setLiveLayout(BuildInitialLayout());
                ratiosRef.Current.Clear();
                log.Reset();
                bumpTick(t => t + 1);
            }).Margin(0, 12, 0, 0),

            // Spec 045 operation log + replay scrubber. Each event from
            // the dock host (drag, splitter, layout mutation) appends a
            // snapshot; Rewind/Play move the cursor and re-apply the
            // snapshot under the cursor so the UI shows the historical
            // state. Mirrored to Debug.WriteLine — DebugView / VS Output
            // window captures the live stream.
            TextBlock("Operation log").SemiBold().Margin(0, 16, 0, 0),
            TextBlock($"cursor {log.Cursor} / {log.Count}  (last 1K kept)")
                .FontSize(11).Opacity(0.7),
            HStack(6,
                Button("« Rewind", () => { log.Rewind(); ApplyCurrent(); }),
                Button("Play »",   () => { log.StepForward(); ApplyCurrent(); }),
                Button("Reset log", () => log.Reset()),
                Button("Copy log", () => CopyLogToClipboard(log))
            ),
            new ScrollViewElement(
                VStack(2, RecentOpLines(log).ToArray())
            )
            {
                HorizontalScrollBarVisibility = ScrollingScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollingScrollBarVisibility.Auto,
            }.Height(200)
        ).Padding(12).Width(360);

        return Grid(
            new[] { GridSize.Star(1), GridSize.Auto },
            new[] { GridSize.Auto, GridSize.Auto, GridSize.Star(1) },
            TextBlock("Scene A — IDE layout").FontSize(20).SemiBold()
                .Grid(row: 0, column: 0, columnSpan: 2),
            TextBlock(
                "Mirrors WinUI.Dock's Example.WinUI/MainView.xaml: vertical split, " +
                "two horizontal halves, bottom row uses TabPosition.Bottom. Drag tabs " +
                "between groups; resize splitters; Esc cancels an in-flight drag."
            ).Opacity(0.8).Margin(0, 0, 0, 8)
                .Grid(row: 1, column: 0, columnSpan: 2),
            dock.Grid(row: 2, column: 0),
            jsonPanel.Grid(row: 2, column: 1)
        ).Padding(16);
    }

    private static DockNode BuildInitialLayout()
    {
        return new DockSplit(
                Orientation.Vertical,
                new DockNode[]
                {
                    // Top half — editor on the left, solution/git tabs on the right.
                    new DockSplit(
                        Orientation.Horizontal,
                        new DockNode[]
                        {
                            new DockTabGroup(
                                Documents: new[]
                                {
                                    new DockableContent(
                                        Title: "MainView.xaml",
                                        Key: "doc:mainview-xaml",
                                        Content: EditorPane(
                                            "// MainView.xaml",
                                            "<Page xmlns=\"...\">\n  <Grid>\n    <!-- edit me -->\n  </Grid>\n</Page>"),
                                        CanClose: true),
                                    new DockableContent(
                                        Title: "MainViewModel.cs",
                                        Key: "doc:mainviewmodel-cs",
                                        Content: EditorPane(
                                            "// MainViewModel.cs",
                                            "public sealed class MainViewModel\n{\n    // type here\n}"),
                                        CanClose: true),
                                },
                                ShowWhenEmpty: true),

                            new DockTabGroup(
                                Documents: new[]
                                {
                                    new DockableContent(
                                        Title: "Solution Explorer",
                                        Key: "tool:solution-explorer",
                                        Content: FilterPane(
                                            "📁 MyApp.sln",
                                            "Filter (Ctrl+;)",
                                            new[] { "📂 src", "    📄 main.cs", "    📄 App.razor" }),
                                        CanClose: true,
                                        CanPin: true),
                                    new DockableContent(
                                        Title: "Git Changes",
                                        Key: "tool:git-changes",
                                        Content: GitChangesPane(),
                                        CanClose: true,
                                        CanPin: true),
                                },
                                TabPosition: TabPosition.Bottom,
                                CompactTabs: true,
                                Width: 240),
                        }),

                    // Bottom half — Error List + Output/Terminal, both with
                    // tabs at the bottom (the "missing" docking windows the
                    // upstream sample shows by default).
                    new DockSplit(
                        Orientation.Horizontal,
                        new DockNode[]
                        {
                            new DockTabGroup(
                                Documents: new[]
                                {
                                    new DockableContent(
                                        Title: "Error List",
                                        Key: "tool:error-list",
                                        Content: ErrorListPane(),
                                        CanClose: true,
                                        CanPin: true),
                                },
                                TabPosition: TabPosition.Bottom),

                            new DockTabGroup(
                                Documents: new[]
                                {
                                    new DockableContent(
                                        Title: "Output",
                                        Key: "tool:output",
                                        Content: OutputPane(),
                                        CanClose: true,
                                        CanPin: true),
                                    new DockableContent(
                                        Title: "Terminal",
                                        Key: "tool:terminal",
                                        Content: TerminalPane(),
                                        CanClose: true,
                                        CanPin: true),
                                },
                                TabPosition: TabPosition.Bottom,
                                CompactTabs: true),
                        },
                        Height: 200),
                });
    }

    // ── Editable pane content factories ────────────────────────────────
    //
    // Each docked pane gets a TextField (or AcceptsReturn=multiline) so
    // we can observe keyboard focus / input routing through the docking
    // host. Spec 045 §2.10 + §2.14 invariants we're stressing here:
    //   • Clicking a TextField inside a pane gives it keyboard focus.
    //   • Typing into the focused TextField does NOT trigger chord
    //     accelerators (Ctrl+Tab / Ctrl+W must reach the editor when it
    //     owns focus, not the host's KeyboardAccelerator surface).
    //   • Tab inside a TextField should advance focus within the pane,
    //     not skip to the next docked group.
    //   • A drag of the pane preserves the TextField's current value
    //     (controlled-input pattern through the §2.30 shape-only
    //     override + Memo-component state slot).
    //
    // Each pane uses Memo(ctx => …) so it holds its own UseState slot;
    // local edits survive parent re-renders + docking layout swaps.

    private static Element EditorPane(string banner, string initial) =>
        Memo(ctx =>
        {
            var (text, setText) = ctx.UseState(initial);
            // INTENTIONALLY MINIMAL: a single-line controlled TextField,
            // no .Set, no AcceptsReturn, no typography modifiers. This
            // isolates whether the focus-loss-on-keystroke bug is in
            // the controlled-TextField/Memo-state base case or in one
            // of the optional modifiers / multi-line config.
            return VStack(6,
                TextBlock(banner).SemiBold(),
                TextField(text, setText, placeholder: "edit me…")
            ).Padding(12);
        });

    private static Element FilterPane(string banner, string filterPlaceholder, string[] items) =>
        Memo(ctx =>
        {
            var (filter, setFilter) = ctx.UseState(string.Empty);
            var rows = filter.Length == 0
                ? items
                : items.Where(s => s.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
            var rowElements = rows
                .Select(r => (Element)TextBlock(r).Margin(8, 0, 0, 0))
                .ToArray();
            return VStack(4,
                TextBlock(banner).SemiBold(),
                TextField(filter, setFilter, placeholder: filterPlaceholder),
                VStack(2, rowElements)
            ).Padding(8);
        });

    private static Element GitChangesPane() =>
        Memo(ctx =>
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
                "  M  samples/apps/dock-showcase/App.cs",
                "   M src/Reactor/Docking/Native/DockHostNativeComponent.cs",
            };
            var changeElements = changes
                .Select(c => (Element)TextBlock(c).FontFamily("Consolas, Courier New, monospace").FontSize(11))
                .ToArray();
            var historyElements = history.Count == 0
                ? new Element[] { TextBlock("(no commits yet)").Opacity(0.5).FontSize(10) }
                : history.Select(h => (Element)TextBlock($"✓ {h}").Opacity(0.75).FontSize(11)).ToArray();
            return VStack(6,
                TextBlock("Branch: feat/045-docking-windows-p2").Opacity(0.8),
                VStack(2, changeElements),
                TextBlock("Commit message").SemiBold().Margin(0, 8, 0, 0),
                TextField(message, setMessage, placeholder: "Describe the change…")
                    .Set(tb => { tb.AcceptsReturn = true; tb.MinHeight = 60; }),
                Button("Commit", Commit),
                TextBlock("History").SemiBold().Margin(0, 8, 0, 0),
                VStack(2, historyElements)
            ).Padding(8);
        });

    private static Element ErrorListPane() =>
        Memo(ctx =>
        {
            var (filter, setFilter) = ctx.UseState(string.Empty);
            var entries = new[]
            {
                "CS8602  Possible null dereference  ViewModel.cs(42,17)",
                "CS0618  'Foo' is obsolete           Bar.cs(13,5)",
                "IL2080  Reflection mismatch         PreviewCaptureServerTests.cs(297,21)",
            };
            var visible = filter.Length == 0
                ? entries
                : entries.Where(e => e.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
            var rowElements = visible
                .Select(e => (Element)TextBlock(e).FontFamily("Consolas, Courier New, monospace").FontSize(11))
                .ToArray();
            return VStack(6,
                TextBlock($"⚠ 0 Errors    ⚠ {entries.Length} Warnings    ℹ 1 Message").Opacity(0.8),
                TextField(filter, setFilter, placeholder: "Filter by code, file, or message…"),
                VStack(2, rowElements)
            ).Padding(8);
        });

    private static Element OutputPane() =>
        Memo(ctx =>
        {
            var (log, setLog) = ctx.UseState<List<string>>(new List<string>
            {
                "[12:34:01] Build started.",
                "[12:34:18] Build succeeded.",
            });
            var (entry, setEntry) = ctx.UseState(string.Empty);
            void Append()
            {
                if (string.IsNullOrWhiteSpace(entry)) return;
                setLog(new List<string>(log) { $"[{DateTime.Now:HH:mm:ss}] {entry}" });
                setEntry(string.Empty);
            }
            var lines = log
                .Select(l => (Element)TextBlock(l).FontFamily("Consolas, Courier New, monospace").FontSize(11).Opacity(0.85))
                .ToArray();
            return VStack(6,
                VStack(2, lines),
                HStack(6,
                    TextField(entry, setEntry, placeholder: "Append output line… (Enter)")
                        .Set(tb =>
                        {
                            tb.KeyDown += (s, e) =>
                            {
                                if (e.Key == Windows.System.VirtualKey.Enter)
                                {
                                    e.Handled = true;
                                    Append();
                                }
                            };
                        })
                        .Flex(grow: 1),
                    Button("Append", Append)
                )
            ).Padding(8);
        });

    private static Element TerminalPane() =>
        Memo(ctx =>
        {
            var (history, setHistory) = ctx.UseState<List<string>>(new List<string>
            {
                "PS C:\\code\\reactor2> git status",
                "On branch feat/045-docking-windows-p2",
                "nothing to commit, working tree clean",
            });
            var (input, setInput) = ctx.UseState(string.Empty);
            void RunCommand()
            {
                if (string.IsNullOrWhiteSpace(input)) return;
                var next = new List<string>(history)
                {
                    $"PS C:\\code\\reactor2> {input}",
                    $"(simulated output for: {input.Trim()})",
                };
                setHistory(next);
                setInput(string.Empty);
            }
            var lines = history
                .Select(l => (Element)TextBlock(l).FontFamily("Consolas, Courier New, monospace").FontSize(11))
                .ToArray();
            return VStack(4,
                VStack(2, lines),
                HStack(6,
                    TextBlock("PS&gt;").FontFamily("Consolas, Courier New, monospace").SemiBold(),
                    TextField(input, setInput, placeholder: "Type a command and press Enter…")
                        .Set(tb =>
                        {
                            tb.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas, Courier New, monospace");
                            tb.KeyDown += (s, e) =>
                            {
                                if (e.Key == Windows.System.VirtualKey.Enter)
                                {
                                    e.Handled = true;
                                    RunCommand();
                                }
                            };
                        })
                        .Flex(grow: 1)
                )
            ).Padding(8);
        });

    private static string SafeSerialize(DockNode? root)
    {
        if (root is null) return "(empty layout)";
        var json = DockLayoutSerializer.Save(root);
        // Re-parse + pretty-print so the panel is readable. The
        // serializer emits compact JSON for storage efficiency.
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Serialize the entire operation log as text and put it on the
    /// system clipboard so users can paste into a bug report / log
    /// inspector. Includes every kind including SplitterTrace MOVE
    /// events (so jump-back math is fully visible).
    /// </summary>
    private static void CopyLogToClipboard(DockOperationLog log)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"# DockOperationLog  count={log.Count}  cursor={log.Cursor}\n");
        foreach (var op in log.Operations)
        {
            var ts = op.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");
            sb.Append($"{ts}  {op.Kind,-15}  {op.Description}");
            if (op.PaneKey is not null) sb.Append($"  pane={op.PaneKey}");
            if (op.Target is not null) sb.Append($"  target={op.Target}");
            sb.Append('\n');
        }
        var pkg = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
        pkg.SetText(sb.ToString());
        global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
    }

    /// <summary>
    /// Render the most-recent operation entries as TextBlocks. The
    /// entry currently under the cursor gets a marker so the scrubber's
    /// position is visible. Capped to last 40 entries to keep the
    /// scroll viewer responsive (the ring still holds 1K).
    /// </summary>
    private static IEnumerable<Element> RecentOpLines(DockOperationLog log)
    {
        const int Tail = 40;
        var ops = log.Operations;
        var start = Math.Max(0, ops.Count - Tail);
        for (int i = start; i < ops.Count; i++)
        {
            var op = ops[i];
            var ts = op.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff");
            var marker = (i + 1) == log.Cursor ? "▶ " : "   ";
            var line = $"{marker}{ts}  {op.Kind,-15}  {op.Description}";
            yield return TextBlock(line)
                .FontFamily("Consolas, Courier New, monospace")
                .FontSize(10)
                .Opacity((i + 1) == log.Cursor ? 1.0 : 0.75);
        }
    }

    private static string SerializeRatios(Dictionary<string, double[]> ratios)
    {
        if (ratios.Count == 0) return "(none — drag a splitter to populate)";
        var lines = new List<string>(ratios.Count);
        foreach (var kvp in ratios.OrderBy(k => k.Key))
        {
            var formatted = string.Join(", ", kvp.Value.Select(v => v.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));
            lines.Add($"\"{kvp.Key}\": [{formatted}]");
        }
        return string.Join("\n", lines);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Scene B — Floating tear-out
// ════════════════════════════════════════════════════════════════════════

class SceneBFloating : Component
{
    public override Element Render() => Grid(
        new[] { GridSize.Star(1) },
        new[] { GridSize.Auto, GridSize.Auto, GridSize.Star(1) },
        TextBlock("Scene B — Floating tear-out").FontSize(20).SemiBold().Grid(row: 0),
        TextBlock(
            "Drag a tab's title into open space — a floating window appears " +
            "at the pointer with a custom title bar from " +
            "IDockAdapter.GetFloatingWindowTitleBar. Drop back into a tab " +
            "group to re-dock; the floating window auto-closes when its " +
            "last document leaves."
        ).Opacity(0.8).Margin(0, 0, 0, 8).Grid(row: 1),

        new DockManager
        {
            Adapter = new ShowcaseAdapter(),
            Layout = new DockTabGroup(new[]
            {
                new DockableContent("Tab A", TextBlock("body-a").Padding(16), Key: "b:a"),
                new DockableContent("Tab B", TextBlock("body-b").Padding(16), Key: "b:b"),
                new DockableContent("Tab C", TextBlock("body-c").Padding(16), Key: "b:c"),
            }),
        }.Grid(row: 2)
    ).Padding(16);

    sealed class ShowcaseAdapter : IDockAdapter
    {
        public Element? OnContentCreated(DockableContent content) => null;
        public void OnGroupCreated(DockTabGroupContext g) { }
        public Element? GetFloatingWindowTitleBar(DockableContent? draggedSource) =>
            HStack(8,
                TextBlock("📌").Opacity(0.7),
                TextBlock(draggedSource?.Title ?? "Floating Window").SemiBold(),
                TextBlock(" — Reactor Docking Showcase").Opacity(0.5)
            ).Padding(12, 6, 12, 6);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Scene C — Side pin
// ════════════════════════════════════════════════════════════════════════

class SceneCSidePin : Component
{
    public override Element Render()
    {
        var tool = new DockableContent(
            Title: "Pinned Tool",
            Key: "c:tool",
            Content: VStack(4,
                TextBlock("This panel is pinned to the right side.").SemiBold(),
                TextBlock("Click the side icon to expand it as a popup."),
                TextBlock("Drag the right edge of the popup to resize.")
            ).Padding(12),
            CanPin: true);

        return Grid(
            new[] { GridSize.Star(1) },
            new[] { GridSize.Auto, GridSize.Auto, GridSize.Star(1) },
            TextBlock("Scene C — Side pin / auto-hide").FontSize(20).SemiBold().Grid(row: 0),
            TextBlock(
                "Pin a tab via its pin button — the tab collapses onto the right edge. " +
                "Click the side icon to expand the popup. Re-pin from the popup " +
                "(thumbtack icon) to restore it to its tab group."
            ).Opacity(0.8).Margin(0, 0, 0, 8).Grid(row: 1),

            new DockManager
            {
                Layout = new DockableContent(
                    Title: "Document",
                    Key: "c:doc",
                    Content: TextBlock("Main document area — try pinning the right-side tool.")
                        .Padding(16)),
                RightSide = new[] { tool },
            }.Grid(row: 2)
        ).Padding(16);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Scene D — Compact + bottom tabs
// ════════════════════════════════════════════════════════════════════════

class SceneDCompact : Component
{
    public override Element Render() => VStack(8,
        TextBlock("Scene D — Compact + bottom tabs").FontSize(20).SemiBold(),
        TextBlock(
            "TabPosition.Bottom + CompactTabs together. Matches Office's tool-pane shape."
        ).Opacity(0.8),

        new DockManager
        {
            Layout = new DockTabGroup(
                Documents: new[]
                {
                    new DockableContent("Errors",        TextBlock("errors").Padding(12), Key: "d:err"),
                    new DockableContent("Warnings",      TextBlock("warnings").Padding(12), Key: "d:warn"),
                    new DockableContent("Build Output",  TextBlock("build").Padding(12), Key: "d:build"),
                },
                TabPosition: TabPosition.Bottom,
                CompactTabs: true),
        }.Flex(grow: 1).Height(200),

        TextBlock("(Compare with TabPosition.Top below)").Opacity(0.6).Margin(0, 24, 0, 0),

        new DockManager
        {
            Layout = new DockTabGroup(
                Documents: new[]
                {
                    new DockableContent("Errors",        TextBlock("errors 2").Padding(12), Key: "d2:err"),
                    new DockableContent("Warnings",      TextBlock("warnings 2").Padding(12), Key: "d2:warn"),
                    new DockableContent("Build Output",  TextBlock("build 2").Padding(12), Key: "d2:build"),
                },
                TabPosition: TabPosition.Top,
                CompactTabs: false),
        }.Flex(grow: 1).Height(200)
    ).Padding(16);
}

// ════════════════════════════════════════════════════════════════════════
//  Scene E — Persistence
// ════════════════════════════════════════════════════════════════════════

class SceneEPersistence : Component
{
    public override Element Render()
    {
        var (status, setStatus) = UseState("");

        return Grid(
            new[] { GridSize.Star(1) },
            new[] { GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Star(1) },
            TextBlock("Scene E — Persistence").FontSize(20).SemiBold().Grid(row: 0),
            TextBlock(
                "DockManager.PersistenceId routes the JSON through " +
                "WindowPersistedScope. Rearrange the panes, quit the app, " +
                "restart — the saved layout restores."
            ).Opacity(0.8).Margin(0, 0, 0, 8).Grid(row: 1),

            HStack(8,
                Button("Note layout-restore status", () =>
                    setStatus("Layout is auto-saved on unmount; reload by relaunching."))
            ).Grid(row: 2),

            TextBlock(status).Opacity(0.7).Margin(0, 4, 0, 8).Grid(row: 3),

            new DockManager
            {
                PersistenceId = "dock-showcase:persistence-demo",
                Layout = new DockSplit(
                    Orientation.Horizontal,
                    new DockNode[]
                    {
                        new DockableContent("Pane 1", TextBlock("p1").Padding(16), Key: "e:1"),
                        new DockableContent("Pane 2", TextBlock("p2").Padding(16), Key: "e:2"),
                        new DockableContent("Pane 3", TextBlock("p3").Padding(16), Key: "e:3"),
                    }),
            }.Grid(row: 4)
        ).Padding(16);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Scene F — Programmatic dock
// ════════════════════════════════════════════════════════════════════════

class SceneFProgrammatic : Component
{
    public override Element Render()
    {
        var (visibleTools, setVisibleTools) = UseState(ImmutableHashSet<string>.Empty);
        var allTools = new[] { "Properties", "Output", "Console", "Watch" };

        var toolButtons = allTools.Select(t =>
            (Element)Button(
                visibleTools.Contains(t) ? $"Close {t}" : $"Open {t}",
                () => setVisibleTools(visibleTools.Contains(t)
                    ? visibleTools.Remove(t)
                    : visibleTools.Add(t)))).ToArray();

        var dockChildren = new List<DockNode>
        {
            new DockableContent(
                "Editor",
                TextBlock("Main editor — open tools from the toolbar above.").Padding(16),
                Key: "f:editor"),
        };
        foreach (var t in visibleTools.OrderBy(t => t))
        {
            dockChildren.Add(new DockableContent(
                Title: t,
                Key: $"f:tool:{t}",
                Content: TextBlock($"{t} pane body").Padding(16),
                Width: 220,
                CanClose: true));
        }

        return Grid(
            new[] { GridSize.Star(1) },
            new[] { GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Star(1) },
            TextBlock("Scene F — Programmatic dock").FontSize(20).SemiBold().Grid(row: 0),
            TextBlock(
                "Click a tool button to open the pane. The pane joins the " +
                "split as a new sibling. Reactor's functional composition " +
                "(state + .Select) replaces upstream's DocumentsSource binding."
            ).Opacity(0.8).Margin(0, 0, 0, 8).Grid(row: 1),

            HStack(8, toolButtons).Margin(0, 0, 0, 8).Grid(row: 2),

            new DockManager
            {
                Layout = new DockSplit(Orientation.Horizontal, dockChildren),
            }.Grid(row: 3)
        ).Padding(16);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  Scene G — Slider Resize
//
//  Isolates the splitter render-and-resize pipeline from pointer / capture
//  handling. The Scene owns a Dictionary<string, double[]> mapping tree-
//  position paths ("0", "0/0", "0/1") to per-child ratios. Sliders mutate
//  the dict directly and bump scene state; the DockManager element's
//  SplitRatios prop hands the same dict to the native renderer, which
//  reads the latest values on each render.
//
//  If sliders move the panes smoothly while pointer drag fails, the bug
//  is exclusively in pointer/capture wiring. If sliders fail too, the
//  bug is in the ratio→render path.
// ════════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════════
//  Scene H — Drop Targets (spec 045 §2.3)
//
//  Exercises the Reactor-native drop-target overlay end-to-end without
//  the drag pipeline. The "Show drop targets" button flips
//  DockManager.ShowDropTargets to true; the overlay paints 9 targets +
//  preview rectangle over the dock subtree. Hovering each target updates
//  the preview rect; clicking confirms — the scene reacts by docking a
//  new pane at the chosen target. Esc dismisses.
//
//  Drag-triggered activation lands with §2.4: the gesture recognizer
//  flips the same flag mid-drag, and the §2.3 overlay you see here is
//  exactly what's painted at that point.
// ════════════════════════════════════════════════════════════════════════

class SceneHDropTargets : Component
{
    public override Element Render()
    {
        var (show, setShow) = UseState(false);
        var (hoverLabel, setHoverLabel) = UseState("(none)");
        var (extraPanes, setExtraPanes) = UseState(ImmutableHashSet<DockTarget>.Empty);
        var (log, setLog) = UseState(ImmutableList<string>.Empty);

        void AddLog(string msg)
        {
            // Cap log to 8 lines so the scene doesn't grow unbounded.
            var next = log.Insert(0, msg);
            if (next.Count > 8) next = next.RemoveAt(8);
            setLog(next);
        }

        // The base pane the overlay paints over. On confirm, we append
        // a new pane at the chosen target so the layout visibly changes.
        var basePane = new DockableContent(
            Title: "Document A",
            Key: "h:doc-a",
            Content: VStack(4,
                TextBlock("Document A — base pane.").SemiBold(),
                TextBlock("Press 'Show drop targets' below to overlay the 9 targets."),
                TextBlock("Hover a target to see the preview rectangle."),
                TextBlock("Click a target to dock a new pane there.")
            ).Padding(16));

        DockNode layout = basePane;
        foreach (var target in extraPanes.OrderBy(t => (int)t))
        {
            var newPane = new DockableContent(
                Title: $"Pane @ {target}",
                Key: $"h:dock-{target}",
                Content: TextBlock($"Docked at {target} via §2.3 overlay click.")
                    .Padding(16));
            layout = target switch
            {
                DockTarget.Center        => new DockTabGroup(new[] { (DockableContent)layout, newPane }),
                DockTarget.SplitLeft     => new DockSplit(Orientation.Horizontal, new DockNode[] { newPane, layout }),
                DockTarget.SplitRight    => new DockSplit(Orientation.Horizontal, new DockNode[] { layout, newPane }),
                DockTarget.SplitTop      => new DockSplit(Orientation.Vertical,   new DockNode[] { newPane, layout }),
                DockTarget.SplitBottom   => new DockSplit(Orientation.Vertical,   new DockNode[] { layout, newPane }),
                DockTarget.DockLeft      => new DockSplit(Orientation.Horizontal, new DockNode[] { newPane, layout }),
                DockTarget.DockRight     => new DockSplit(Orientation.Horizontal, new DockNode[] { layout, newPane }),
                DockTarget.DockTop       => new DockSplit(Orientation.Vertical,   new DockNode[] { newPane, layout }),
                DockTarget.DockBottom    => new DockSplit(Orientation.Vertical,   new DockNode[] { layout, newPane }),
                _ => layout,
            };
        }

        var dock = new DockManager
        {
            Layout = layout,
            ShowDropTargets = show,
            OnDropTargetHovered = t =>
            {
                setHoverLabel(t?.ToString() ?? "(none)");
            },
            OnDropTargetConfirmed = t =>
            {
                AddLog($"Confirmed {t} — pane appended.");
                setExtraPanes(extraPanes.Add(t));
                setShow(false);
            },
            OnDropTargetsDismissed = () =>
            {
                AddLog("Dismissed (Esc).");
                setShow(false);
            },
        };

        var logLines = log.Count == 0
            ? (Element)TextBlock("(no events yet)").Opacity(0.5).FontSize(11)
            : VStack(2, log.Select(l => (Element)TextBlock(l).FontSize(11).Opacity(0.8)).ToArray());

        return Grid(
            new[] { GridSize.Star(1) },
            new[] { GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Star(1) },

            TextBlock("Scene H — Drop Targets (§2.3)").FontSize(20).SemiBold().Grid(row: 0),
            TextBlock(
                "The Reactor-native drop-target overlay. Click the button to " +
                "show the 9 targets (5 split + 4 edge, each ≥ 44×44 DIP). " +
                "Hover a target to see the preview rectangle; click to dock a " +
                "new pane there. Esc dismisses. Drag-triggered activation " +
                "lands with §2.4 (the drag pipeline)."
            ).Opacity(0.8).Margin(0, 0, 0, 8).Grid(row: 1),

            HStack(8,
                Button(show ? "Hide drop targets" : "Show drop targets",
                    () => setShow(!show)),
                Button("Reset",
                    () => { setExtraPanes(ImmutableHashSet<DockTarget>.Empty); setLog(ImmutableList<string>.Empty); }),
                TextBlock($"Hovered: {hoverLabel}").Opacity(0.7).Margin(12, 0, 0, 0)
            ).Margin(0, 0, 0, 8).Grid(row: 2),

            logLines.Margin(0, 0, 0, 8).Grid(row: 3),

            dock.Grid(row: 4)
        ).Padding(16);
    }
}

class SceneGSliders : Component
{
    public override Element Render()
    {
        var (ratiosRef, _) = UseState<Dictionary<string, double[]>>(new()
        {
            ["0"]   = new[] { 0.5, 0.5 },
            ["0/0"] = new[] { 0.5, 0.5 },
            ["0/1"] = new[] { 0.5, 0.5 },
        });

        // Slider value mirrors the leading ratio (0..1). On change we
        // mutate the shared dict in place + bump tick to force re-render.
        var (rowLeading, setRowLeading) = UseState(0.5);
        var (col0Leading, setCol0Leading) = UseState(0.5);
        var (col1Leading, setCol1Leading) = UseState(0.5);

        void Apply(string path, double leading)
        {
            ratiosRef[path] = new[] { leading, 1.0 - leading };
        }

        // Live mutate before render to ensure renderer sees the latest.
        Apply("0",   rowLeading);
        Apply("0/0", col0Leading);
        Apply("0/1", col1Leading);

        Element MakeSlider(string label, double value, Action<double> setter) =>
            VStack(2,
                TextBlock($"{label}  {value:F2}").FontSize(11),
                (new SliderElement(Value: value, Min: 0.05, Max: 0.95,
                                   OnValueChanged: v => setter(v))
                {
                    StepFrequency = 0.01,
                }).Width(220));

        var dock = new DockManager
        {
            SplitRatios = ratiosRef,
            Layout = new DockSplit(
                Orientation.Vertical,
                new DockNode[]
                {
                    new DockSplit(
                        Orientation.Horizontal,
                        new DockNode[]
                        {
                            new DockableContent("Editor",
                                VStack(8,
                                    TextBlock("editor body").SemiBold(),
                                    TextBlock("Slider-driven resize — no pointer involved.")),
                                Key: "k:editor"),
                            new DockableContent("Tools",
                                VStack(8,
                                    TextBlock("tools body").SemiBold(),
                                    TextBlock("Outline / properties.")),
                                Key: "k:tools"),
                        }),
                    new DockSplit(
                        Orientation.Horizontal,
                        new DockNode[]
                        {
                            new DockableContent("Output",
                                VStack(8,
                                    TextBlock("output body").SemiBold(),
                                    TextBlock("Build output.")),
                                Key: "k:output"),
                            new DockableContent("Terminal",
                                VStack(8,
                                    TextBlock("terminal body").SemiBold(),
                                    TextBlock("PS> _")),
                                Key: "k:terminal"),
                        }),
                }),
        };

        return Grid(
            new[] { GridSize.Star(1) },
            new[] { GridSize.Auto, GridSize.Auto, GridSize.Auto, GridSize.Star(1) },
            TextBlock("Scene G — Slider Resize").FontSize(20).SemiBold().Grid(row: 0),
            TextBlock(
                "Each slider drives one splitter's leading-pane ratio. They " +
                "mutate the same dictionary the native renderer reads from, " +
                "bypassing the pointer/capture path entirely."
            ).Opacity(0.8).Margin(0, 0, 0, 8).Grid(row: 1),
            HStack(16,
                MakeSlider("Outer row",    rowLeading,  setRowLeading),
                MakeSlider("Top columns",  col0Leading, setCol0Leading),
                MakeSlider("Bottom cols",  col1Leading, setCol1Leading)
            ).Margin(0, 0, 0, 8).Grid(row: 2),
            dock.Grid(row: 3)
        ).Padding(16);
    }
}


// ════════════════════════════════════════════════════════════════════════
//  Scene I — Tab Styles (spec 045 §4.6)
// ════════════════════════════════════════════════════════════════════════
//
//  Three tab-chrome presets side-by-side, plus the spec-documented
//  fallback for TabPosition.Bottom (renders as Top in WinUI 3 — see
//  microsoft-ui-xaml#7395; the upstream workaround requires subclassing
//  TabViewItem to counter-scale the template parts).

class SceneITabStyles : Component
{
    public override Element Render() => Grid(
        new[] { GridSize.Star(1), GridSize.Star(1) },
        new[] { GridSize.Auto, GridSize.Auto, GridSize.Star(1), GridSize.Star(1), GridSize.Auto, GridSize.Auto },

        TextBlock("Scene I — Tab Styles").FontSize(20).SemiBold()
            .Grid(row: 0, column: 0, columnSpan: 2),

        TextBlock(
            "Three preset chromes for DockTabGroup.TabChrome. Same underlying " +
            "WinUI TabView (full accessibility/keyboard parity) — only theme " +
            "resources differ. Pick whichever matches your app's identity."
        ).Opacity(0.8).Margin(0, 0, 0, 12)
            .Grid(row: 1, column: 0, columnSpan: 2),

        // Top-left — Win11 default
        StyleCard(
            "Win11 (default)",
            "Rounded corners, theme background. The shipping Win11 TabView look.",
            new DockTabGroup(
                Documents: new[]
                {
                    new DockableContent("Welcome.md",  ChromeBody("# Welcome",         "Win11"),  Key: "i:w11:welcome"),
                    new DockableContent("App.cs",      ChromeBody("class App {…}",     "Win11"),  Key: "i:w11:appcs"),
                    new DockableContent("README",      ChromeBody("Project readme.",    "Win11"), Key: "i:w11:readme"),
                },
                TabChrome: TabChrome.Win11))
            .Grid(row: 2, column: 0),

        // Top-right — Flat (VS Code-style)
        StyleCard(
            "Flat (VS Code style)",
            "Zero corner radius, tighter header padding. Modeled on the modern IDE document strip.",
            new DockTabGroup(
                Documents: new[]
                {
                    new DockableContent("Welcome.md",  ChromeBody("# Welcome",         "Flat"),   Key: "i:flat:welcome"),
                    new DockableContent("App.cs",      ChromeBody("class App {…}",     "Flat"),   Key: "i:flat:appcs"),
                    new DockableContent("README",      ChromeBody("Project readme.",    "Flat"),  Key: "i:flat:readme"),
                },
                TabChrome: TabChrome.Flat))
            .Grid(row: 2, column: 1),

        // Bottom-left — TitleBar (mica-ish; tab strip uses the title-bar brush)
        StyleCard(
            "TitleBar (chromeless feel)",
            "Tab strip uses the system TitleBarBackgroundFillBrush so the strip " +
            "blends with the window chrome (effective when ExtendsContentIntoTitleBar is on).",
            new DockTabGroup(
                Documents: new[]
                {
                    new DockableContent("Welcome.md",  ChromeBody("# Welcome",         "TitleBar"), Key: "i:tb:welcome"),
                    new DockableContent("App.cs",      ChromeBody("class App {…}",     "TitleBar"), Key: "i:tb:appcs"),
                    new DockableContent("README",      ChromeBody("Project readme.",    "TitleBar"), Key: "i:tb:readme"),
                },
                TabChrome: TabChrome.TitleBar))
            .Grid(row: 3, column: 0),

        // Bottom-right — Flat + CompactTabs (full classic VS feel)
        StyleCard(
            "Flat + CompactTabs",
            "Same flat chrome paired with tightly-packed tabs — the closest match " +
            "to the dense tool-window strip in classic Visual Studio.",
            new DockTabGroup(
                Documents: new[]
                {
                    new DockableContent("Errors",     ChromeBody("0 errors.",           "Flat compact"), Key: "i:flatc:err"),
                    new DockableContent("Warnings",   ChromeBody("12 warnings.",        "Flat compact"), Key: "i:flatc:warn"),
                    new DockableContent("Output",     ChromeBody("Build complete.",     "Flat compact"), Key: "i:flatc:out"),
                    new DockableContent("Terminal",   ChromeBody("PS> _",               "Flat compact"), Key: "i:flatc:term"),
                },
                TabChrome: TabChrome.Flat,
                CompactTabs: true))
            .Grid(row: 3, column: 1),

        // Position note
        TextBlock(
            "Note — TabPosition.Bottom currently renders as Top under WinUI 3. " +
            "Microsoft.UI.Xaml.Controls.TabView has no TabStripPlacement property " +
            "(microsoft-ui-xaml#7395). Spec 045 §4.6 / DockTabGroupRenderer line 196 " +
            "documents the deferral; a real bottom strip lands when a custom " +
            "TabViewItem subclass replaces the shared WinUI template."
        ).Opacity(0.65).FontSize(11).Margin(0, 12, 0, 4)
            .Grid(row: 4, column: 0, columnSpan: 2),

        // Persistence note
        TextBlock(
            "Persistence — TabChrome serializes via the DockLayoutSerializer JSON; " +
            "legacy layouts without the tabChrome field default to Win11."
        ).Opacity(0.55).FontSize(11)
            .Grid(row: 5, column: 0, columnSpan: 2)
    ).Padding(16);

    // ── helpers ──────────────────────────────────────────────────────────

    static Element StyleCard(string heading, string body, DockTabGroup group)
        => VStack(6,
            TextBlock(heading).SemiBold(),
            TextBlock(body).Opacity(0.7).FontSize(11),
            new DockManager { Layout = group }
                .Height(180)
                .Margin(0, 4, 0, 0)
        ).Margin(0, 0, 8, 16);

    static Element ChromeBody(string title, string chromeLabel)
        => VStack(4,
            TextBlock(title).SemiBold(),
            TextBlock($"(rendered under TabChrome.{chromeLabel})").Opacity(0.55).FontSize(11)
        ).Padding(12);
}
