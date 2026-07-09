using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Reactor.Docking;
using static Microsoft.UI.Reactor.Factories;

namespace WidgetCreator;

/// <summary>
/// Top-level Widget Creator shell. Generates widgets, runs them in MXC, and
/// keeps monitoring each sandboxed process so a later runtime crash can resume
/// the Copilot session that created the widget and ask for a repair.
/// </summary>
public sealed class WidgetCreatorShell : Component
{
    const int TextTailLimit = 80_000;

    const string RefineFaster =
        "Make the app noticeably faster and more responsive. Move any heavy per-pixel or " +
        "per-frame computation off the UI thread (Task.Run), parallelize hot loops " +
        "(Parallel.For), cache results and only recompute when inputs actually change, and " +
        "keep interaction (pan/zoom/drag) instant by transforming the cached output while " +
        "recomputing lazily. Do not change what the app does.";

    const string RefineDesign =
        "Improve the visual design to feel like a polished, modern Windows 11 app: spacing on " +
        "the 4px grid, clear typography hierarchy (Heading/SubHeading/Caption), theme tokens for " +
        "all colors (never hardcoded), tasteful cards/borders/corner radius, and a clean, " +
        "well-aligned layout. Keep the core functionality unchanged.";

    const string RefineFix =
        "Review the app for bugs and broken behavior and fix them. Make sure every interaction " +
        "works end to end, state updates correctly, and the --selftest checks meaningfully verify " +
        "the real behavior (strengthen them if they were trivial).";

    const string RefineA11y =
        "Improve accessibility: add automation names/labels to interactive controls, ensure full " +
        "keyboard operability with visible focus and a sensible tab order, and sufficient color " +
        "contrast using theme tokens. Keep the existing functionality and design.";

    readonly WidgetLibrary _library = new();
    readonly WidgetWorkspace _workspace = new();
    readonly WidgetBuilder _builder = new();
    readonly MxcSandbox _sandbox = new();
    readonly CopilotSdkClient _client;
    readonly GenerationPipeline _pipeline;
    readonly SemaphoreSlim _operationGate = new(1, 1);
    readonly object _sourceGate = new();
    readonly object _logGate = new();
    string _sourceText = "";
    string _logText = "";

    public WidgetCreatorShell()
    {
        _client = new CopilotSdkClient();
        _pipeline = new GenerationPipeline(_client, _workspace, _builder, _sandbox);
        _logText = $"Session log: {SessionLog.Path}{Environment.NewLine}";
    }

    public override Element Render()
    {
        var (prompt, setPrompt) = UseState("A compact focus timer with start, pause, reset, and a simple progress indicator.", threadSafe: true);
        var (apps, setApps) = UseState<IReadOnlyList<WidgetApp>>(_library.LoadAll(), threadSafe: true);
        var (selectedId, setSelectedId) = UseState<string?>(null, threadSafe: true);
        var (editingId, setEditingId) = UseState<string?>(null, threadSafe: true);
        var (source, setSource) = UseState("", threadSafe: true);
        var (refineText, setRefineText) = UseState("", threadSafe: true);
        var (log, setLog) = UseState(_logText, threadSafe: true);
        var (status, setStatus) = UseState("Ready", threadSafe: true);
        var (banner, setBanner) = UseState<string?>(null, threadSafe: true);
        var (isWorking, setIsWorking) = UseState(false, threadSafe: true);
        var operationCtsRef = UseRef<CancellationTokenSource?>(null);

        // Per-widget MXC permissions dialog state.
        var (permApp, setPermApp) = UseState<WidgetApp?>(null, threadSafe: true);
        var (policyJson, setPolicyJson) = UseState("", threadSafe: true);
        var (policyAdvanced, setPolicyAdvanced) = UseState(false, threadSafe: true);

        UseEffect(() =>
        {
            void OnTurnStart() => ReplaceSource("", setSource);
            void OnToken(string token) => AppendSource(token, setSource);
            void OnLog(string line) => AppendLog(line, setLog);
            void OnPhase(string phase)
            {
                SessionLog.Write($"[Shell] phase: {phase}");
                setStatus(phase);
            }

            _pipeline.OnTurnStart += OnTurnStart;
            _pipeline.OnToken += OnToken;
            _pipeline.OnLog += OnLog;
            _pipeline.OnPhase += OnPhase;

            return () =>
            {
                _pipeline.OnTurnStart -= OnTurnStart;
                _pipeline.OnToken -= OnToken;
                _pipeline.OnLog -= OnLog;
                _pipeline.OnPhase -= OnPhase;
                operationCtsRef.Current?.Cancel();
                _ = Task.Run(async () =>
                {
                    try { await _client.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception ex) { SessionLog.Write($"[Shell] dispose failed: {ex.Message}"); }
                });
            };
        });

        var selected = apps.FirstOrDefault(a => a.Id == selectedId) ?? apps.FirstOrDefault();

        void RefreshApps(string? select = null)
        {
            var loaded = _library.LoadAll();
            setApps(loaded);
            if (select is not null)
                setSelectedId(select);
        }

        void SelectApp(WidgetApp app)
        {
            setSelectedId(app.Id);
            setEditingId(app.Id);
            setPrompt(app.Prompt);
            setRefineText("");
            ReplaceSource(app.ReadSource(), setSource);
            setStatus($"Editing '{app.Title}'. Update the prompt, or use Refine to iterate.");
            setBanner(null);
        }

        void StartNewWidget()
        {
            setEditingId(null);
            setSelectedId(null);
            setPrompt("");
            setRefineText("");
            ReplaceSource("", setSource);
            setBanner(null);
            setStatus("Ready for a new widget.");
        }

        void Reveal(string path)
        {
            try
            {
                if (File.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                else if (Directory.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                else
                    setBanner($"Path no longer exists: {path}");
            }
            catch (Exception ex)
            {
                SessionLog.Write($"[Shell] reveal failed: {ex}");
                setBanner($"Reveal failed: {ex.Message}");
            }
        }

        void RunPipelineOperation(string headerLabel, WidgetApp? editing, string promptForSave, Func<CancellationToken, Task<PipelineResult>> run)
        {
            var cts = new CancellationTokenSource();
            operationCtsRef.Current = cts;
            setIsWorking(true);
            setBanner(null);
            ReplaceSource("", setSource);
            ReplaceLog($"# {headerLabel} - {DateTime.Now:G}{Environment.NewLine}", setLog);

            _ = Task.Run(async () =>
            {
                try
                {
                    await _operationGate.WaitAsync(cts.Token).ConfigureAwait(false);
                    try
                    {
                        var result = await run(cts.Token).ConfigureAwait(false);
                        ReplaceSource(result.Source, setSource);

                        WidgetApp app;
                        string error;
                        var built = editing is null
                            ? TryCreateApp(promptForSave, result, out app, out error)
                            : TryUpdateApp(editing, promptForSave, result, out app, out error);
                        if (!built)
                        {
                            setStatus("Build failed.");
                            setBanner(error);
                            return;
                        }

                        await _library.SaveAsync(app).ConfigureAwait(false);
                        RefreshApps(app.Id);
                        setEditingId(app.Id);
                        if (result.SelfTestPassed)
                        {
                            setStatus($"Self-test passed — launching '{app.Title}'...");
                            AppendLog($"# Saved '{app.Title}' with Copilot session {Short(app.SessionId)}. Self-test passed.", setLog);
                        }
                        else
                        {
                            setStatus($"Launching '{app.Title}' (self-test did not pass)...");
                            setBanner($"'{app.Title}' was saved and launched, but its self-test did not pass after {GenerationPipeline.MaxSelfTestAttempts} repair attempts — it may not work correctly. Refine it and run again.");
                            AppendLog($"# Saved '{app.Title}' with Copilot session {Short(app.SessionId)}. Self-test FAILED:{Environment.NewLine}{result.SelfTestReport}", setLog);
                        }
                        StartMonitor(app);
                    }
                    finally
                    {
                        _operationGate.Release();
                    }
                }
                catch (OperationCanceledException)
                {
                    setStatus("Operation cancelled.");
                    AppendLog("# Operation cancelled.", setLog);
                }
                catch (Exception ex)
                {
                    SessionLog.Write($"[Shell] operation failed: {ex}");
                    setStatus("Operation failed.");
                    setBanner($"Operation failed: {ex.GetType().Name} - {ex.Message}");
                }
                finally
                {
                    setIsWorking(false);
                    operationCtsRef.Current?.Dispose();
                    operationCtsRef.Current = null;
                }
            });
        }

        bool CancelIfRunning(string what)
        {
            if (operationCtsRef.Current is not null)
            {
                operationCtsRef.Current.Cancel();
                setStatus($"Cancelling current {what}...");
                return true;
            }
            if (isWorking)
            {
                setStatus("A crash repair is already running.");
                return true;
            }
            return false;
        }

        void StartGenerate()
        {
            if (CancelIfRunning("generation")) return;

            if (string.IsNullOrWhiteSpace(prompt))
            {
                setBanner("Describe the widget first.");
                return;
            }

            var editing = editingId is not null ? apps.FirstOrDefault(a => a.Id == editingId) : null;
            var trimmedPrompt = prompt.Trim();
            var label = editing is null ? "Generate & Run" : $"Update & Run '{editing.Title}'";
            RunPipelineOperation(label, editing, trimmedPrompt,
                ct => editing is null
                    ? _pipeline.GenerateAsync(trimmedPrompt, ct)
                    : _pipeline.RegenerateAsync(editing, trimmedPrompt, ct));
        }

        void StartRefine(string instruction)
        {
            if (CancelIfRunning("operation")) return;

            var editing = editingId is not null ? apps.FirstOrDefault(a => a.Id == editingId) : null;
            if (editing is null)
            {
                setBanner("Select a saved widget to refine.");
                return;
            }
            if (string.IsNullOrWhiteSpace(instruction))
            {
                setBanner("Describe the change you want, or pick a quick action.");
                return;
            }

            var trimmed = instruction.Trim();
            setRefineText("");
            // Preserve the widget's original prompt; a refine only changes behavior.
            RunPipelineOperation($"Refine '{editing.Title}': {trimmed}", editing, editing.Prompt,
                ct => _pipeline.RefineAsync(editing, trimmed, ct));
        }

        void RunApp(WidgetApp app)
        {
            setBanner(null);
            ReplaceSource(app.ReadSource(), setSource);
            AppendLog($"# Launching '{app.Title}' from {app.PublishDir}", setLog);
            setStatus($"Launching '{app.Title}'...");
            StartMonitor(app);
        }

        void DeleteApp(WidgetApp app)
        {
            _library.Delete(app.Id);
            RefreshApps();
            if (selectedId == app.Id)
            {
                setSelectedId(null);
                ReplaceSource("", setSource);
            }
            if (editingId == app.Id)
            {
                setEditingId(null);
                setPrompt("");
            }
            setStatus($"Deleted '{app.Title}'.");
        }

        void OpenPermissions(WidgetApp app)
        {
            setBanner(null);
            setPolicyJson(_library.ReadPolicy(app) ?? MxcPolicy.DefaultJson);
            setPolicyAdvanced(false);
            setPermApp(app);
        }

        void ClosePermissions() => setPermApp(null);

        void SavePermissions(WidgetApp app)
        {
            // Normalize and decide: a policy equal to the default reverts to "no
            // stored policy" (absent file) so the app keeps using today's behavior.
            var normalized = MxcPolicy.TryParse(policyJson, out var obj, out _)
                ? MxcPolicy.Prettify(obj)
                : policyJson;
            var isDefault = string.Equals(normalized.Trim(), MxcPolicy.DefaultJson.Trim(), StringComparison.Ordinal);

            _ = Task.Run(async () =>
            {
                try
                {
                    if (isDefault)
                        _library.ResetPolicy(app);
                    else
                        await _library.SavePolicyAsync(app, normalized).ConfigureAwait(false);

                    setStatus(isDefault
                        ? $"Reset '{app.Title}' to the default permissions (UI + internet)."
                        : $"Saved custom permissions for '{app.Title}'. They apply next time it runs.");
                }
                catch (Exception ex)
                {
                    SessionLog.Write($"[Shell] save policy for {app.Id} failed: {ex}");
                    setBanner($"Could not save permissions for '{app.Title}': {ex.Message}");
                }
            });
            ClosePermissions();
        }

        void StartMonitor(WidgetApp app)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await _sandbox.RunAsync(
                        app.ExePath,
                        app.PublishDir,
                        line => AppendLog(line, setLog),
                        CancellationToken.None,
                        policyTemplateJson: _library.ReadPolicy(app)).ConfigureAwait(false);

                    if (result.LaunchFailed)
                    {
                        var detail = result.LaunchErrorMessage
                            ?? "the sandbox could not be set up with the current permissions";
                        setStatus($"'{app.Title}' couldn't launch under its permissions.");
                        setBanner($"'{app.Title}' couldn't start — this is a sandbox/permissions problem, not an "
                            + $"app bug, so it was NOT sent for repair. {detail}. Open Permissions to adjust it "
                            + "(e.g. a read-write or read-only grant on a folder you don't own, like C:\\temp, "
                            + "needs WRITE_DAC the sandbox can't get on this host — pick a folder under your "
                            + "profile, or remove the grant).");
                        AppendLog($"# '{app.Title}' did not launch — MXC sandbox/permissions error: {detail}", setLog);
                        return;
                    }

                    if (!result.Crashed)
                    {
                        setStatus($"'{app.Title}' closed cleanly.");
                        AppendLog($"# '{app.Title}' exited normally.", setLog);
                        return;
                    }

                    var message =
                        $"'{app.Title}' crashed ({result.ExitCodeHex}). Restoring Copilot session {Short(app.SessionId)} and asking the agent to repair it.";
                    setBanner(message);
                    setStatus($"Repairing '{app.Title}' after crash...");
                    AppendLog($"# {message}", setLog);
                    await RepairCrashAsync(app, result).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    SessionLog.Write($"[Shell] monitor failed for {app.Id}: {ex}");
                    setStatus("Sandbox run failed.");
                    setBanner($"Sandbox run failed for '{app.Title}': {ex.Message}");
                }
            });
        }

        async Task RepairCrashAsync(WidgetApp app, SandboxResult crash)
        {
            await _operationGate.WaitAsync().ConfigureAwait(false);
            setIsWorking(true);
            try
            {
                var result = await _pipeline.FixCrashAsync(app, crash, CancellationToken.None).ConfigureAwait(false);
                ReplaceSource(result.Source, setSource);

                if (!result.Success || result.Project is null || result.Build?.ExePath is null || result.Build.PublishDir is null)
                {
                    setStatus($"Repair failed for '{app.Title}'.");
                    setBanner($"'{app.Title}' crashed ({crash.ExitCodeHex}), but the repair build did not succeed.");
                    return;
                }

                var repaired = app with
                {
                    ExePath = result.Build.ExePath,
                    PublishDir = result.Build.PublishDir,
                    SessionId = result.SessionId,
                };

                await _library.SaveAsync(repaired).ConfigureAwait(false);
                RefreshApps(repaired.Id);
                setStatus($"Relaunching repaired '{repaired.Title}'...");
                setBanner($"'{repaired.Title}' crashed ({crash.ExitCodeHex}); Copilot repaired it from the original creation session and relaunched it.");
                AppendLog($"# Repair succeeded. Persisted session {Short(repaired.SessionId)} and relaunching.", setLog);
                StartMonitor(repaired);
            }
            catch (Exception ex)
            {
                SessionLog.Write($"[Shell] repair failed for {app.Id}: {ex}");
                setStatus($"Repair failed for '{app.Title}'.");
                setBanner($"Crash repair failed for '{app.Title}': {ex.GetType().Name} - {ex.Message}");
            }
            finally
            {
                setIsWorking(false);
                _operationGate.Release();
            }
        }

        var promptBox = (TextBox(
                prompt,
                setPrompt,
                placeholderText: "Describe a small Win11-style widget to generate...")
            with
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
            })
            .MinHeight(118)
            .AutomationName("Widget prompt");

        var editingApp = editingId is not null ? apps.FirstOrDefault(a => a.Id == editingId) : null;

        var generateButton = Button(isWorking ? "Cancel" : (editingApp is null ? "Generate & Run" : "Update & Run"), StartGenerate)
            .AccentButton()
            .IsEnabled(isWorking || !string.IsNullOrWhiteSpace(prompt));

        var refineBox = (TextBox(
                refineText,
                setRefineText,
                placeholderText: "Describe a change, e.g. 'make pan and zoom instant' or 'use a dark theme'...")
            with
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
            })
            .MinHeight(64)
            .AutomationName("Refine instruction");

        Element refineSection = editingApp is null ? Empty() : Border(
            FlexColumn(
                Caption("Refine — iterate with follow-up changes; keeps this widget's Copilot session and re-saves in place.")
                    .Foreground(Theme.SecondaryText),
                refineBox,
                HStack(8,
                    Button("Refine & Run", () => StartRefine(refineText))
                        .AccentButton()
                        .IsEnabled(!isWorking && !string.IsNullOrWhiteSpace(refineText))),
                (FlexRow(
                    Button("⚡ Faster", () => StartRefine(RefineFaster)).SubtleButton().IsEnabled(!isWorking),
                    Button("🎨 Better design", () => StartRefine(RefineDesign)).SubtleButton().IsEnabled(!isWorking),
                    Button("🐞 Fix issues", () => StartRefine(RefineFix)).SubtleButton().IsEnabled(!isWorking),
                    Button("♿ Accessibility", () => StartRefine(RefineA11y)).SubtleButton().IsEnabled(!isWorking))
                    with { Wrap = Microsoft.UI.Reactor.Layout.FlexWrap.Wrap, ColumnGap = 8, RowGap = 8 }))
            with { RowGap = 8 })
            .Background(Theme.SubtleFill)
            .WithBorder(Theme.CardStroke)
            .CornerRadius(8)
            .Padding(12);

        var appCards = apps.Count == 0
            ? [(Element)EmptyLibraryCard()]
            : apps.Select(AppCard).ToArray();

        // Pane 1 — Edit: prompt + generate + refine. Scrolls when the refine
        // section makes the body taller than the pane.
        var editPaneBody = (ScrollViewer(
                (FlexColumn(
                    Caption(editingApp is null
                        ? "Copilot generates a single-file Reactor app, publishes it, and runs it in MXC. If it later crashes, this shell resumes the creating Copilot session and sends the crash back for repair."
                        : "Editing a saved widget: tweak the prompt and Update & Run to regenerate it in place (same library entry and Copilot session). Use New widget to start fresh instead."),
                    promptBox,
                    HStack(8,
                        generateButton,
                        editingApp is null ? Empty() : Button("New widget", StartNewWidget).SubtleButton(),
                        Button("Open library", () => Reveal(_library.Root))),
                    refineSection)
                with { RowGap = 12 })
                .Padding(12))
            with
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            })
            .Flex(grow: 1, basis: 0);

        // Pane 2 — Saved widgets list.
        var savedPaneBody = (FlexColumn(
                Caption($"{apps.Count} saved - crashes repair through the original Copilot session"),
                (ScrollViewer(VStack(8, appCards))
                    with
                    {
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    })
                .Flex(grow: 1, basis: 0))
            with { RowGap = 10 })
            .Padding(12)
            .Flex(grow: 1, basis: 0);

        var sourceBox = (TextBox(source, _ => { }, placeholderText: "Generated source appears here...")
            with
            {
                AcceptsReturn = true,
                IsReadOnly = true,
                TextWrapping = TextWrapping.NoWrap,
            })
            .FontFamily("Cascadia Mono")
            .MinHeight(260)
            .Flex(grow: 1, basis: 0)
            .Set(tb =>
            {
                Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(tb, ScrollBarVisibility.Visible);
                Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(tb, ScrollBarVisibility.Visible);
            })
            .AutomationName("Generated source");

        var logBox = (TextBox(log, _ => { }, placeholderText: "Build, sandbox, crash, and repair logs appear here...")
            with
            {
                AcceptsReturn = true,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
            })
            .FontFamily("Cascadia Mono")
            .MinHeight(180)
            .Flex(grow: 1, basis: 0)
            .Set(tb =>
            {
                Microsoft.UI.Xaml.Controls.ScrollViewer.SetVerticalScrollBarVisibility(tb, ScrollBarVisibility.Visible);
                Microsoft.UI.Xaml.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(tb, ScrollBarVisibility.Disabled);
            })
            .AutomationName("Run log");

        var selectedSummary = selected is null
            ? (Element)Caption("No widget selected yet. Generate one or choose a saved widget.")
                .Foreground(Theme.SecondaryText)
            : Caption($"{selected.Icon} {selected.Title} - model {selected.Model} - session {Short(selected.SessionId)}")
                .Foreground(Theme.SecondaryText);

        // Pane 3 — Generated source header + body.
        var sourceHeader = selected is null
            ? (Element)selectedSummary
            : HStack(8,
                selectedSummary.Flex(grow: 1, basis: 0),
                Button("Reveal", () => Reveal(selected.SourcePath)).SubtleButton());
        var sourcePaneBody = (FlexColumn(sourceHeader, sourceBox)
                with { RowGap = 8 })
            .Padding(12)
            .Flex(grow: 1, basis: 0);

        // Pane 4 — Build & sandbox log header + body.
        var logPaneBody = (FlexColumn(
                HStack(8,
                    Caption("Build, sandbox, crash, and repair output")
                        .Foreground(Theme.SecondaryText)
                        .Flex(grow: 1, basis: 0),
                    Button("Reveal session log", () => Reveal(SessionLog.Path)).SubtleButton()),
                logBox)
            with { RowGap = 8 })
            .Padding(12)
            .Flex(grow: 1, basis: 0);

        // Four docked panes. Locked down (no tear-off, float, pin, or reorder);
        // only the splitters between panes are draggable, giving free resizing.
        // Splitter positions persist across launches via PersistenceId.
        static DockableContent LockedPane(string title, object key, Element content) => new ToolWindow
        {
            Title = title,
            Key = key,
            Content = content,
            CanClose = false,
            CanPin = false,
            CanFloat = false,
            CanMove = false,
            CanHide = false,
            CanAutoHide = false,
            CanDockAsDocument = false,
        };

        var dock = new DockManager
        {
            PersistenceId = "widget-creator-dock",
            Layout = new DockSplit(Orientation.Horizontal, new DockNode[]
            {
                new DockSplit(Orientation.Vertical, new DockNode[]
                {
                    new DockTabGroup(new[]
                    {
                        LockedPane(editingApp is null ? "Create" : $"Edit '{editingApp.Title}'", "edit", editPaneBody),
                    }, Height: 360),
                    new DockTabGroup(new[]
                    {
                        LockedPane("Saved widgets", "saved", savedPaneBody),
                    }),
                }, Width: 420),
                new DockSplit(Orientation.Vertical, new DockNode[]
                {
                    new DockTabGroup(new[]
                    {
                        LockedPane("Generated source", "source", sourcePaneBody),
                    }, Height: 460),
                    new DockTabGroup(new[]
                    {
                        LockedPane("Build & sandbox log", "log", logPaneBody),
                    }),
                }),
            }),
        }.Flex(grow: 1, basis: 0);

        var titleBar = TitleBar("Widget Creator") with
        {
            Subtitle = status,
            RightHeader = Caption(MxcSandbox.WxcExecPath).Foreground(Theme.TertiaryText),
        };

        return Grid(
            [GridSize.Star()], [GridSize.Star()],
            (FlexColumn(
                titleBar,
                (FlexColumn(
                    banner is null ? Empty() : Banner(banner),
                    dock)
                    with { RowGap = 12 })
                .Padding(16)
                .Flex(grow: 1, basis: 0))),
            permApp is null ? null : PermissionsOverlay(permApp))
            .Backdrop(BackdropKind.Mica);

        // Inline modal overlay (rendered in the root Grid so it reconciles on
        // every render — unlike Reactor's ContentDialog, whose content is a
        // one-shot snapshot taken when it opens).
        Element PermissionsOverlay(WidgetApp app)
        {
            var valid = MxcPolicy.TryParse(policyJson, out var policy, out var parseError);

            Element body;
            if (policyAdvanced)
            {
                var jsonBox = (TextBox(policyJson, setPolicyJson,
                        placeholderText: "MXC ContainerConfig policy JSON...")
                    with { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap })
                    .FontFamily("Cascadia Mono")
                    .MinHeight(280)
                    .AutomationName("Policy JSON");

                body = FlexColumn(
                    Caption("Edit the raw MXC ContainerConfig policy. The process command line, working "
                        + "directory, and a read grant on the app's own folder are added automatically at launch.")
                        .Foreground(Theme.SecondaryText)
                        .TextWrapping(TextWrapping.Wrap),
                    jsonBox,
                    valid
                        ? (Element)Empty()
                        : Caption($"Invalid JSON: {parseError}").Foreground(Theme.SystemCaution).TextWrapping(TextWrapping.Wrap))
                    with { RowGap = 8 };
            }
            else
            {
                var showWindow = MxcPolicy.GetShowWindow(policy);
                var injection = MxcPolicy.GetInjection(policy);
                var clipboard = MxcPolicy.GetClipboard(policy);
                var network = MxcPolicy.GetNetwork(policy);
                var allowLocal = MxcPolicy.GetAllowLocalNetwork(policy);
                var leastPrivilege = MxcPolicy.GetLeastPrivilege(policy);
                var fileEntries = MxcPolicy.GetFileEntries(policy);

                string[] clipLevels = ["none", "read", "readwrite"];
                string[] clipLabels = ["No access", "Read only", "Read & write"];
                var clipIndex = Array.IndexOf(clipLevels, clipboard);
                Optional<int> clipSelected = clipIndex >= 0 ? clipIndex : default;

                string[] accessLabels = ["Read only", "Read & write", "Denied"];

                Element fileList = fileEntries.Length == 0
                    ? Caption("No extra folders yet.").Foreground(Theme.TertiaryText)
                    : (FlexColumn(fileEntries.Select(e => (Element)
                        HStack(8,
                            Caption(e.Path)
                                .FontFamily("Cascadia Mono")
                                .TextWrapping(TextWrapping.Wrap)
                                .VAlign(VerticalAlignment.Center)
                                .Flex(grow: 1, basis: 0),
                            ComboBox(accessLabels, (int)e.Access,
                                i => setPolicyJson(MxcPolicy.WithFileAccess(policyJson, e.Path, (PathAccess)i)))
                                .Width(150),
                            Button("Remove", () => setPolicyJson(MxcPolicy.WithoutPath(policyJson, e.Path)))
                                .SubtleButton()))
                        .ToArray())
                        with { RowGap = 8 });

                body = FlexColumn(
                    PermSection("Window & input",
                        ToggleSwitch(showWindow,
                            v => setPolicyJson(MxcPolicy.WithShowWindow(policyJson, v)),
                            header: "Show the app window"),
                        CheckBox(injection,
                            v => setPolicyJson(MxcPolicy.WithInjection(policyJson, v)),
                            "Allow simulated keyboard / mouse input (injection)"),
                        HStack(8,
                            TextBlock("Clipboard").VAlign(VerticalAlignment.Center).MinWidth(80),
                            ComboBox(clipLabels, clipSelected,
                                i => setPolicyJson(MxcPolicy.WithClipboard(policyJson, clipLevels[i])))
                                .Width(180))),

                    PermSection("Network",
                        RadioButtons(["No network access", "Internet access"],
                            network == NetworkAccess.Internet ? 1 : 0,
                            i => setPolicyJson(MxcPolicy.WithNetwork(policyJson,
                                i == 1 ? NetworkAccess.Internet : NetworkAccess.None))),
                        CheckBox(allowLocal,
                            v => setPolicyJson(MxcPolicy.WithAllowLocalNetwork(policyJson, v)),
                            "Allow local network (localhost / LAN)")),

                    PermSection("Isolation",
                        CheckBox(leastPrivilege,
                            v => setPolicyJson(MxcPolicy.WithLeastPrivilege(policyJson, v)),
                            "Run with least privilege (stricter AppContainer)")),

                    PermSection("File access",
                        Caption("The app can always read its own folder. Add folders below and choose each "
                            + "one's access level. (Add individual files via Advanced.)")
                            .Foreground(Theme.SecondaryText)
                            .TextWrapping(TextWrapping.Wrap),
                        fileList,
                        HStack(8,
                            Button("Add folder…", AddFolder).SubtleButton())))
                    with { RowGap = 16 };

                void AddFolder() => _ = AddFolderAsync();

                async Task AddFolderAsync()
                {
                    try
                    {
                        var window = ReactorApp.PrimaryWindow?.NativeWindow;
                        if (window is null)
                        {
                            setBanner("Folder picker unavailable (no active window).");
                            return;
                        }
                        var picker = new Windows.Storage.Pickers.FolderPicker();
                        picker.FileTypeFilter.Add("*");
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                        var folder = await picker.PickSingleFolderAsync();
                        if (folder is not null)
                            setPolicyJson(MxcPolicy.WithFileAccess(policyJson, folder.Path, PathAccess.ReadOnly));
                    }
                    catch (Exception ex)
                    {
                        SessionLog.Write($"[Shell] folder picker failed: {ex}");
                        setBanner($"Folder picker failed: {ex.Message}");
                    }
                }
            }

            var card = Border(
                (FlexColumn(
                    HStack(8,
                        SubHeading("Adjust permissions").Flex(grow: 1, basis: 0),
                        TextBlock("Advanced").VAlign(VerticalAlignment.Center),
                        ToggleSwitch(policyAdvanced, setPolicyAdvanced)),
                    Caption($"Permissions for '{app.Title}' — applied the next time it runs.")
                        .Foreground(Theme.SecondaryText)
                        .TextWrapping(TextWrapping.Wrap),
                    body,
                    HStack(8,
                        Button("Reset to default", () => setPolicyJson(MxcPolicy.DefaultJson))
                            .SubtleButton()
                            .Flex(grow: 1, basis: 0),
                        Button("Cancel", ClosePermissions).SubtleButton(),
                        Button("Save", () => SavePermissions(app)).AccentButton().IsEnabled(valid)))
                    with { RowGap = 12 }))
                .Background(Theme.SolidBackground)
                .WithBorder(Theme.CardStroke)
                .CornerRadius(10)
                .Padding(20)
                .Width(560);

            // Full-bleed scrim dimming the app; a flex column centers the
            // content-sized card both axes (Border-child alignment isn't honored
            // by the layout here, so center via flex instead).
            return Border(
                (FlexColumn(card)
                    with
                    {
                        JustifyContent = Microsoft.UI.Reactor.Layout.FlexJustify.Center,
                        AlignItems = Microsoft.UI.Reactor.Layout.FlexAlign.Center,
                    })
                .Padding(24))
                .Background(Theme.SmokeFill);
        }

        static Element PermSection(string title, params Element[] children) =>
            FlexColumn(
                new Element[] { TextBlock(title).Bold() }
                    .Concat(children)
                    .ToArray())
            with { RowGap = 8 };

        Element AppCard(WidgetApp app)
        {
            var isSelected = selectedId == app.Id;
            var sessionLabel = string.IsNullOrWhiteSpace(app.SessionId)
                ? "no saved session"
                : $"session {Short(app.SessionId)}";

            return Border(
                FlexColumn(
                    HStack(8,
                        TextBlock(app.Icon).FontSize(20),
                        FlexColumn(
                            TextBlock(app.Title).Bold(),
                            Caption($"{app.CreatedAt:g} - {sessionLabel}")
                                .Foreground(Theme.SecondaryText))
                        .Flex(grow: 1, basis: 0)),
                    Caption(app.Prompt).Foreground(Theme.SecondaryText).TextWrapping(TextWrapping.Wrap),
                    HStack(8,
                        Button(isSelected ? "Selected" : "Select", () => SelectApp(app)).IsEnabled(!isSelected),
                        Button("Run", () => RunApp(app)).IsEnabled(app.IsRunnable),
                        Button("Permissions", () => OpenPermissions(app)).SubtleButton(),
                        Button("Delete", () => DeleteApp(app)).SubtleButton()))
                with { RowGap = 8 })
            .Background(isSelected ? Theme.SubtleFill : Theme.LayerFill)
            .WithBorder(isSelected ? Theme.Accent : Theme.CardStroke)
            .CornerRadius(8)
            .Padding(12);
        }

        Element EmptyLibraryCard() =>
            Border(
                FlexColumn(
                    TextBlock("No widgets yet").Bold(),
                    Caption("Generated widgets are saved here with their Copilot session IDs so crash repair can resume the right conversation."))
                with { RowGap = 6 })
            .Background(Theme.LayerFill)
            .WithBorder(Theme.CardStroke)
            .CornerRadius(8)
            .Padding(12);
    }

    bool TryCreateApp(string prompt, PipelineResult result, out WidgetApp app, out string error)
    {
        if (!result.Success || result.Project is null || result.Build is null)
        {
            app = null!;
            error = "Copilot generated code, but the build did not succeed.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(result.Build.ExePath) ||
            string.IsNullOrWhiteSpace(result.Build.PublishDir))
        {
            app = null!;
            error = "Build succeeded, but the published widget executable was not found.";
            return false;
        }

        app = new WidgetApp(
            result.Project.Id,
            result.Title,
            result.Icon,
            prompt,
            _pipeline.ModelId,
            DateTime.Now,
            result.Project.Dir,
            result.Build.ExePath,
            result.Build.PublishDir,
            result.SessionId);
        error = "";
        return true;
    }

    bool TryUpdateApp(WidgetApp existing, string prompt, PipelineResult result, out WidgetApp app, out string error)
    {
        if (!result.Success || result.Build is null)
        {
            app = null!;
            error = "Copilot updated the code, but the build did not succeed.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(result.Build.ExePath) ||
            string.IsNullOrWhiteSpace(result.Build.PublishDir))
        {
            app = null!;
            error = "Build succeeded, but the updated widget executable was not found.";
            return false;
        }

        app = existing with
        {
            Title = result.Title,
            Icon = result.Icon,
            Prompt = prompt,
            Model = _pipeline.ModelId,
            ExePath = result.Build.ExePath,
            PublishDir = result.Build.PublishDir,
            SessionId = result.SessionId,
        };
        error = "";
        return true;
    }

    void ReplaceSource(string text, Action<string> setSource)
    {
        lock (_sourceGate)
        {
            _sourceText = Tail(text);
            setSource(_sourceText);
        }
    }

    void AppendSource(string text, Action<string> setSource)
    {
        lock (_sourceGate)
        {
            _sourceText = Tail(_sourceText + text);
            setSource(_sourceText);
        }
    }

    void ReplaceLog(string text, Action<string> setLog)
    {
        lock (_logGate)
        {
            _logText = Tail(text);
            setLog(_logText);
        }
    }

    void AppendLog(string text, Action<string> setLog)
    {
        lock (_logGate)
        {
            var line = text.EndsWith(Environment.NewLine, StringComparison.Ordinal) ? text : text + Environment.NewLine;
            _logText = Tail(_logText + line);
            setLog(_logText);
        }
    }

    static Element Banner(string message) =>
        Border(
            HStack(10,
                TextBlock("!").FontSize(16).VAlign(VerticalAlignment.Center),
                TextBlock(message)
                    .Foreground(Theme.PrimaryText)
                    .TextWrapping(TextWrapping.Wrap)
                    .VAlign(VerticalAlignment.Center)
                    .Flex(grow: 1, basis: 0)))
        .Background(Theme.SystemCautionBackground)
        .WithBorder(Theme.SystemCaution)
        .CornerRadius(8)
        .Padding(horizontal: 12, vertical: 10);

    static string Short(string s) =>
        string.IsNullOrWhiteSpace(s) ? "(none)" : (s.Length <= 8 ? s : s[..8]);

    static string Tail(string text) =>
        text.Length <= TextTailLimit ? text : text[^TextTailLimit..];
}
