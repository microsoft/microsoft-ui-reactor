using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DemoScriptTool.App.Components;
using DemoScriptTool.App.Models;
using DemoScriptTool.App.Services;
using Windows.System;
using static Microsoft.UI.Reactor.Factories;
using IoPath = System.IO.Path;

namespace DemoScriptTool.App;

/// <summary>
/// Top-level shell: title bar with command buttons, demo-prompt panel, scrollable
/// step cards, plus the parse/auth banner and toast surface (spec §UI Layout).
/// All long-lived services (store, watcher, pipeline) hang off the shell so
/// teardown happens cleanly when the window closes.
/// </summary>
public sealed class DemoScriptShell : Component
{
    /// <summary>
    /// Optional folder to auto-load on first mount, set by <c>Program.cs</c> from
    /// the first non-flag CLI argument (e.g. <c>dotnet run -- C:\dev\my-demo</c>).
    /// Static because <see cref="ReactorApp.Run{T}"/> instantiates the shell
    /// itself and we have no constructor seam.
    /// </summary>
    public static string? InitialFolder { get; set; }

    readonly DemoScriptStore _store = new();
    readonly StepFileWriter _writer = new();
    readonly DotnetRunner _runner = new();
    readonly GhAuth _auth = new();
    readonly SpeakerNotesExporter _exporter = new();
    readonly StatusReporter _status = new();
    readonly CopilotSdkClient _client;
    readonly GenerationPipeline _pipeline;

    public DemoScriptShell()
    {
        // Copilot SDK rides the user's logged-in Copilot subscription; the
        // earlier raw-HTTP GithubModelsClient kept tripping github.com's anti-
        // scraping 429 when the Authorization header alone wasn't enough to
        // attribute the traffic. The SDK proxies through the bundled Copilot
        // CLI which sends the proper UA + session metadata.
        _client = new CopilotSdkClient();
        _pipeline = new GenerationPipeline(_client, _runner, _writer, _auth, _status);
    }

    public override Element Render()
    {
        // threadSafe: true on every state cell — services raise events from
        // Task.Run threads (file watcher, generation pipeline, dotnet runner)
        // and would otherwise throw cross-thread on setState.
        var (projectRoot, setProjectRoot) = UseState<string?>(null, threadSafe: true);
        var (model, setModel) = UseState(DemoScriptModel.Empty(), threadSafe: true);
        var (parseError, setParseError) = UseState<DemoScriptParseError?>(null, threadSafe: true);
        var (banner, setBanner) = UseState<string?>(null, threadSafe: true);
        var (toast, setToast) = UseState<(string Message, StatusSeverity Severity)?>(null, threadSafe: true);
        var (generationStatus, setGenerationStatus) = UseState<string?>(null, threadSafe: true);
        var (isGenerating, setIsGenerating) = UseState(false, threadSafe: true);
        var watcherRef = UseRef<DemoScriptWatcher?>(null);
        var generationCtsRef = UseRef<CancellationTokenSource?>(null);
        var saveDebounceRef = UseRef<CancellationTokenSource?>(null);
        // Last-click-fired timestamp guards against Generate / Re-gen / etc.
        // entry handlers running multiple times per user click. We've observed
        // (logged at SessionLog) the OnGenerateAll handler firing 3+ times
        // within 1 ms of a single click — which previously kicked off a run
        // and then immediately cancelled it. Until the framework-level cause
        // is found (suspected leaky button click subscription across
        // re-renders, see PoolableWireFlags + EnsureButtonWiring) a cheap
        // 200 ms debounce stops the immediate-cancel symptom without changing
        // the user's perceived behavior on legitimate clicks.
        var lastGenerateClickRef = UseRef<long>(0);
        var lastRegenClickRef = UseRef<long>(0);
        // SHA-256 of the bytes of our most recent save (or load). When the
        // file watcher fires for a write WE just made, the disk hash equals
        // this value and we suppress the reload — otherwise our own debounced
        // save round-trips through the watcher, replaces the model with a new
        // instance, re-syncs every TextField's local buffer, and resets the
        // user's caret to position 0 mid-keystroke.
        var lastSyncedHashRef = UseRef<string?>(null);
        var announce = UseAnnounce();

        // Wire StatusReporter once + auto-load the CLI-supplied folder if any.
        // The auto-load runs as a fire-and-forget Task because LoadAsync awaits
        // and we want render to return immediately; the resulting setState calls
        // will land on the dispatcher and trigger the next render.
        UseEffect(() =>
        {
            void OnToast(string message, StatusSeverity severity)
            {
                setToast((message, severity));
                _ = Task.Delay(4000).ContinueWith(_ => setToast(null));
            }
            void OnGenerating(string? msg)
            {
                SessionLog.Write($"[Shell] OnGenerating '{msg ?? "(null)"}'");
                setGenerationStatus(msg);
                if (msg is not null) SafeAnnounce(msg);
            }
            void OnBanner(string? msg) => setBanner(msg);

            _status.Toast += OnToast;
            _status.Generating += OnGenerating;
            _status.Banner += OnBanner;

            if (InitialFolder is not null && projectRoot is null)
            {
                SessionLog.Write($"[demo-script] auto-loading CLI folder: {InitialFolder}");
                _ = LoadFolderAsync(InitialFolder);
            }

            return () =>
            {
                _status.Toast -= OnToast;
                _status.Generating -= OnGenerating;
                _status.Banner -= OnBanner;
            };
        });

        // Filesystem watcher lifecycle, scoped to projectRoot.
        UseEffect(() =>
        {
            watcherRef.Current?.Dispose();
            watcherRef.Current = null;

            if (projectRoot is null) return () => { };

            async void Reload()
            {
                try
                {
                    var path = IoPath.Combine(projectRoot, DemoScriptStore.FileName);
                    if (!File.Exists(path)) return;

                    var diskBytes = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false);
                    var diskHash = HashBytes(diskBytes);
                    if (diskHash == lastSyncedHashRef.Current)
                    {
                        // Watcher fired for a write WE just made; ignore.
                        return;
                    }
                    lastSyncedHashRef.Current = diskHash;

                    var diskText = System.Text.Encoding.UTF8.GetString(diskBytes);
                    var (loaded, err) = DemoScriptParser.Parse(diskText);
                    setParseError(err);
                    if (loaded is not null)
                    {
                        setModel(loaded);
                        setBanner(null);
                        _status.ShowToast("Reloaded demo-script.md after external change.");
                    }
                    else if (err is not null)
                    {
                        setBanner($"demo-script.md is malformed — {err}");
                    }
                }
                catch (Exception ex)
                {
                    _status.ShowToast($"Reload failed: {ex.Message}", StatusSeverity.Error);
                }
            }
            void OnDeleted()
            {
                setModel(DemoScriptModel.Empty());
                setParseError(null);
                _status.ShowToast("demo-script.md was deleted — reset to empty scaffold.", StatusSeverity.Warning);
            }

            watcherRef.Current = new DemoScriptWatcher(projectRoot, Reload, OnDeleted);
            return () =>
            {
                watcherRef.Current?.Dispose();
                watcherRef.Current = null;
            };
        }, projectRoot ?? "");

        // Debounced save when the model mutates.
        void ScheduleSave()
        {
            if (projectRoot is null) return;
            saveDebounceRef.Current?.Cancel();
            saveDebounceRef.Current?.Dispose();
            var cts = new CancellationTokenSource();
            saveDebounceRef.Current = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, cts.Token).ConfigureAwait(false);
                    // Serialize once so we can hash the exact bytes we're about
                    // to write; the watcher's reload compares to this hash and
                    // skips the (caret-resetting) reload for our own save.
                    var serialized = DemoScriptParser.Serialise(model);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(serialized);
                    lastSyncedHashRef.Current = HashBytes(bytes);
                    var path = IoPath.Combine(projectRoot, DemoScriptStore.FileName);
                    await File.WriteAllBytesAsync(path + ".tmp", bytes, cts.Token).ConfigureAwait(false);
                    File.Move(path + ".tmp", path, overwrite: true);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _status.ShowToast($"Save failed: {ex.Message}", StatusSeverity.Error);
                }
            });
        }

        // ── Commands ────────────────────────────────────────────────────
        async Task LoadFolderAsync(string root)
        {
            try
            {
                // Seed the hash so a watcher fire for the file we just read
                // (the OS often reports a Changed event on close even without
                // a write) doesn't spuriously trigger a reload.
                var path = IoPath.Combine(root, DemoScriptStore.FileName);
                if (File.Exists(path))
                {
                    var diskBytes = await File.ReadAllBytesAsync(path, CancellationToken.None).ConfigureAwait(false);
                    lastSyncedHashRef.Current = HashBytes(diskBytes);
                }
                else
                {
                    lastSyncedHashRef.Current = null;
                }

                var (loaded, err) = await _store.LoadAsync(root, CancellationToken.None);
                setProjectRoot(root);
                setParseError(err);
                if (loaded is not null)
                {
                    // Restore previously-generated step artifacts from disk so
                    // the UI shows the existing code instead of "No code
                    // generated yet" after a re-open. Build state and Delta
                    // are NOT persisted; they reset to NotBuilt and null —
                    // the user can click ▶ Run on a step that already has
                    // code without re-generating.
                    int restored = 0;
                    foreach (var step in loaded.Steps)
                        if (await TryRestoreStepArtifactAsync(step, root, loaded.IsMultiFile, CancellationToken.None).ConfigureAwait(false))
                            restored++;

                    setModel(loaded);
                    setBanner(null);
                    var stepsLine = loaded.Steps.Count == 0
                        ? "add a demo prompt to get started."
                        : $"{loaded.Steps.Count} step{(loaded.Steps.Count == 1 ? "" : "s")}{(restored > 0 ? $", {restored} with existing code" : "")}.";
                    _status.ShowToast($"Opened {IoPath.GetFileName(root)} — {stepsLine}");
                }
                else if (err is not null)
                {
                    setBanner($"demo-script.md is malformed — {err}");
                }
            }
            catch (Exception ex)
            {
                _status.ShowToast($"Could not open folder: {ex.Message}", StatusSeverity.Error);
            }
        }

        async void OnOpenFolder()
        {
            try
            {
                var picker = new global::Windows.Storage.Pickers.FolderPicker();
                picker.FileTypeFilter.Add("*");
                InitPicker(picker);
                var folder = await picker.PickSingleFolderAsync();
                if (folder is null) return;

                await LoadFolderAsync(folder.Path);
            }
            catch (Exception ex)
            {
                _status.ShowToast($"Could not open folder: {ex.Message}", StatusSeverity.Error);
            }
        }

        // Wraps a click handler so any exception (e.g. UseAnnounce hitting an
        // unattached automation peer, a stale UseRef, an SDK call surfacing
        // mid-handler) is logged and surfaced as a banner instead of being
        // swallowed by the WinUI Click event chain. The repro that motivated
        // this: OnRegenFromStep entered (entry log fired), exited the click
        // event with no further log, no Task.Run, no toast — a silent throw
        // somewhere between the entry log and the kickoff log was the most
        // likely cause but invisible without this scaffolding.
        void SafeClickHandler(string name, Action body)
        {
            try { body(); }
            catch (Exception ex)
            {
                SessionLog.Write($"[Shell] {name} threw: {ex}");
                _status.SetBanner($"{name} crashed: {ex.GetType().Name} — {ex.Message}");
            }
        }

        // SafeAnnounce — never throws. UseAnnounce.Announce ultimately calls
        // FrameworkElementAutomationPeer.FromElement, which is UI-thread-only
        // and surfaces RPC_E_WRONG_THREAD (COMException 0x8001010E) when
        // invoked from a threadpool thread (we hit this from
        // UseCommand-wrapped click handlers — see framework #130). Marshals
        // to the UI dispatcher when needed and swallows any other automation
        // peer flake — losing one screen-reader announcement is fine, leaving
        // the caller mid-state-setup is not.
        void SafeAnnounce(string message, bool assertive = false)
        {
            try
            {
                var dq = ReactorApp.ActiveHost?.Window?.DispatcherQueue;
                if (dq is null || dq.HasThreadAccess)
                    announce.Announce(message, assertive);
                else
                    dq.TryEnqueue(() =>
                    {
                        try { announce.Announce(message, assertive); }
                        catch (Exception ex) { SessionLog.Write($"[Shell] SafeAnnounce (dispatched) swallowed: {ex.Message}"); }
                    });
            }
            catch (Exception ex)
            {
                SessionLog.Write($"[Shell] SafeAnnounce swallowed: {ex.Message}");
            }
        }

        void OnGenerateAll() => SafeClickHandler("OnGenerateAll", OnGenerateAllCore);
        void OnGenerateAllCore()
        {
            var now = Environment.TickCount64;
            var delta = now - lastGenerateClickRef.Current;
            SessionLog.Write($"[Shell] OnGenerateAll projectRoot='{projectRoot ?? "(null)"}' ctsCurrent={(generationCtsRef.Current is null ? "null" : "non-null")} isGenerating={isGenerating} steps={model.Steps.Count} sinceLastClick={delta}ms");
            if (lastGenerateClickRef.Current != 0 && delta < 200)
            {
                SessionLog.Write($"[Shell] OnGenerateAll → debounce drop ({delta}ms since last invocation)");
                return;
            }
            lastGenerateClickRef.Current = now;
            if (projectRoot is null)
            {
                _status.ShowToast("Open a folder first (Ctrl+O).", StatusSeverity.Warning);
                return;
            }

            // The CTS ref is the source of truth for "actually in flight" — it's
            // mutated synchronously and never lags behind a UseState dispatch.
            // We previously gated on the isGenerating UseState, which ended up
            // stuck true on at least one repro after a normal completion (the
            // Cancel button never reverted to Generate All), blocking subsequent
            // runs. Falling back to the ref means the user can always start /
            // cancel a run regardless of any UseState-vs-render drift.
            if (generationCtsRef.Current is not null)
            {
                SessionLog.Write("[Shell] OnGenerateAll → cancelling in-flight gen");
                generationCtsRef.Current.Cancel();
                return;
            }

            // Defensively reset the UI flag in case it's stuck out of sync with
            // the ref. setIsGenerating(true) below schedules a render either way.
            if (isGenerating) setIsGenerating(false);

            var cts = new CancellationTokenSource();
            generationCtsRef.Current = cts;
            setIsGenerating(true);
            SafeAnnounce($"Generating {model.Steps.Count} steps…");
            SessionLog.Write("[Shell] Generate-All kicking off Task.Run");

            _ = Task.Run(async () =>
            {
                SessionLog.Write("[Shell] Generate-All Task.Run entered");
                try
                {
                    await _pipeline.GenerateAllAsync(model, projectRoot, cts.Token).ConfigureAwait(false);
                    SessionLog.Write("[Shell] Generate-All Task.Run pipeline returned normally");
                }
                catch (Exception ex)
                {
                    SessionLog.Write($"[Shell] Generate task crashed: {ex}");
                    _status.SetBanner($"Generation crashed: {ex.GetType().Name} — {ex.Message}");
                }
                finally
                {
                    SessionLog.Write("[Shell] Generate finally — clearing isGenerating + cts");
                    setIsGenerating(false);
                    generationCtsRef.Current?.Dispose();
                    generationCtsRef.Current = null;
                }
            });
        }

        async void OnExportSpeakerNotes()
        {
            if (projectRoot is null)
            {
                _status.ShowToast("Open a folder first.", StatusSeverity.Warning);
                return;
            }
            try
            {
                var path = await _exporter.ExportAsync(model, projectRoot, CancellationToken.None);
                _status.ShowToast($"Speaker notes exported to {IoPath.GetFileName(path)}", StatusSeverity.Success);
            }
            catch (Exception ex)
            {
                _status.ShowToast($"Export failed: {ex.Message}", StatusSeverity.Error);
            }
        }

        void OnRunStep(StepModel step)
        {
            if (projectRoot is null) return;
            _ = Task.Run(async () =>
            {
                var (spawned, err) = await _runner.RunAsync(step, projectRoot, model.IsMultiFile, CancellationToken.None);
                if (!spawned)
                    _status.ShowToast($"Run failed for step {step.Number} — {err}", StatusSeverity.Error);
            });
        }

        void OnRegenFromStep(StepModel step) => SafeClickHandler($"OnRegenFromStep(step={step.Number})", () => OnRegenFromStepCore(step));
        void OnRegenFromStepCore(StepModel step)
        {
            var now = Environment.TickCount64;
            var delta = now - lastRegenClickRef.Current;
            SessionLog.Write($"[Shell] OnRegenFromStep step={step.Number} '{step.Title}' projectRoot='{projectRoot ?? "(null)"}' ctsCurrent={(generationCtsRef.Current is null ? "null" : "non-null")} isGenerating={isGenerating} sinceLastClick={delta}ms");
            if (lastRegenClickRef.Current != 0 && delta < 200)
            {
                SessionLog.Write($"[Shell] OnRegenFromStep → debounce drop ({delta}ms since last invocation)");
                return;
            }
            lastRegenClickRef.Current = now;
            if (projectRoot is null)
            {
                _status.ShowToast("Open a folder first.", StatusSeverity.Warning);
                return;
            }
            // CTS-ref-as-truth (see OnGenerateAll). Gating on UseState's
            // isGenerating directly used to leave Re-gen permanently disabled
            // when the flag got stuck after a normal completion.
            if (generationCtsRef.Current is not null)
            {
                _status.ShowToast("Generation already running — cancel it first.", StatusSeverity.Warning);
                return;
            }
            if (isGenerating) setIsGenerating(false);

            // Locate this step's index in the live steps list. Comparing by
            // reference is robust to renumbering after Add/Delete.
            int idx = -1;
            for (int i = 0; i < model.Steps.Count; i++)
            {
                if (ReferenceEquals(model.Steps[i], step)) { idx = i; break; }
            }
            if (idx < 0) { _status.ShowToast("Step is no longer in the script.", StatusSeverity.Warning); return; }

            var cts = new CancellationTokenSource();
            generationCtsRef.Current = cts;
            setIsGenerating(true);
            SafeAnnounce($"Re-generating from step {step.Number}…");
            SessionLog.Write($"[Shell] Re-gen kicking off Task.Run startIndex={idx}");

            _ = Task.Run(async () =>
            {
                SessionLog.Write($"[Shell] Re-gen Task.Run entered startIndex={idx}");
                try
                {
                    await _pipeline.GenerateFromAsync(model, projectRoot, idx, cts.Token).ConfigureAwait(false);
                    SessionLog.Write("[Shell] Re-gen Task.Run pipeline returned normally");
                }
                catch (Exception ex)
                {
                    SessionLog.Write($"[Shell] Re-gen task crashed: {ex}");
                    _status.SetBanner($"Re-gen crashed: {ex.GetType().Name} — {ex.Message}");
                }
                finally
                {
                    SessionLog.Write("[Shell] Re-gen finally — clearing isGenerating + cts");
                    setIsGenerating(false);
                    generationCtsRef.Current?.Dispose();
                    generationCtsRef.Current = null;
                }
            });
        }

        void OnCopyDelta(StepModel step)
        {
            if (string.IsNullOrEmpty(step.Delta)) return;
            try
            {
                var dp = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText(step.Delta);
                global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                _status.ShowToast($"Step {step.Number} delta copied to clipboard.", StatusSeverity.Success);
                SafeAnnounce($"Step {step.Number} delta copied.");
            }
            catch (Exception ex)
            {
                _status.ShowToast($"Clipboard error: {ex.Message}", StatusSeverity.Error);
            }
        }

        void OnPromptChanged(int stepNumber, string newPrompt)
        {
            foreach (var step in model.Steps)
                if (step.Number == stepNumber) { step.UpdatePrompt(newPrompt); break; }
            ScheduleSave();
        }

        void OnTitleChanged(int stepNumber, string newTitle)
        {
            foreach (var step in model.Steps)
                if (step.Number == stepNumber) { step.UpdateTitle(newTitle); break; }
            ScheduleSave();
        }

        void OnDemoPromptChanged(string v) { model.UpdateDemoPrompt(v); ScheduleSave(); }
        void OnDemoTitleChanged(string v) { model.UpdateTitle(v); ScheduleSave(); }

        void OnAddStep()
        {
            var added = model.AddStep(title: "", prompt: "");
            ScheduleSave();
            SafeAnnounce($"Added step {added.Number}.");
        }

        void OnDeleteStep(StepModel step)
        {
            if (model.RemoveStep(step.Number))
            {
                ScheduleSave();
                _status.ShowToast($"Deleted step {step.Number}.", StatusSeverity.Info);
            }
        }

        // ── Commands & accelerators ─────────────────────────────────────
        var openCmd = new Command
        {
            Label = "Open Folder",
            Execute = (Action)OnOpenFolder,
            Icon = SymbolIcon("OpenLocal"),
            Accelerator = Accelerator(VirtualKey.O, VirtualKeyModifiers.Control),
        };
        var generateCmd = new Command
        {
            Label = isGenerating ? "Cancel" : "Generate All",
            Execute = OnGenerateAll,
            CanExecute = projectRoot is not null,
            Icon = SymbolIcon(isGenerating ? "Stop" : "Play"),
            Accelerator = Accelerator(VirtualKey.G, VirtualKeyModifiers.Control),
        };
        var exportCmd = new Command
        {
            Label = "Export Speaker Notes",
            Execute = (Action)OnExportSpeakerNotes,
            CanExecute = projectRoot is not null && AnyDelta(model),
            Icon = SymbolIcon("Save"),
            Accelerator = Accelerator(VirtualKey.E, VirtualKeyModifiers.Control),
        };

        // Generate-All gets accent-color resource overrides so hover/pressed/disabled
        // states stay correct (spec §Buttons / §Theming).
        var generateButton = Button(generateCmd)
            .Resources(r => r
                .Set("ButtonBackground", new ThemeRef("AccentFillColorDefaultBrush"))
                .Set("ButtonBackgroundPointerOver", new ThemeRef("AccentFillColorSecondaryBrush"))
                .Set("ButtonBackgroundPressed", new ThemeRef("AccentFillColorTertiaryBrush"))
                .Set("ButtonBackgroundDisabled", new ThemeRef("AccentFillColorDisabledBrush"))
                .Set("ButtonForeground", new ThemeRef("TextOnAccentFillColorPrimaryBrush"))
                .Set("ButtonForegroundPointerOver", new ThemeRef("TextOnAccentFillColorPrimaryBrush"))
                .Set("ButtonForegroundPressed", new ThemeRef("TextOnAccentFillColorSecondaryBrush"))
                .Set("ButtonForegroundDisabled", new ThemeRef("TextOnAccentFillColorDisabledBrush")));

        var devMenu = DevtoolsMenu(() => new Microsoft.UI.Reactor.Core.MenuFlyoutItemBase[]
        {
            MenuItem("Reveal demo-script.md…",
                () =>
                {
                    if (projectRoot is null) return;
                    try
                    {
                        var path = IoPath.Combine(projectRoot, DemoScriptStore.FileName);
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                    }
                    catch (Exception ex) { _status.ShowToast($"Reveal failed: {ex.Message}", StatusSeverity.Error); }
                }),
            MenuItem("Reveal log folder…",
                () =>
                {
                    try
                    {
                        // Prefer selecting the current session's file (highlights it
                        // in Explorer); fall back to opening the folder if Init
                        // hasn't run or the file is gone.
                        var arg = SessionLog.CurrentPath is { } p && System.IO.File.Exists(p)
                            ? $"/select,\"{p}\""
                            : $"\"{SessionLog.LogDirectory}\"";
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", arg) { UseShellExecute = true });
                    }
                    catch (Exception ex) { _status.ShowToast($"Reveal log failed: {ex.Message}", StatusSeverity.Error); }
                }),
            MenuItem("Log model snapshot",
                () => SessionLog.Write($"[demo-script] title='{model.Title}' steps={model.Steps.Count} multiFile={model.IsMultiFile}")),
            MenuItem("Log available Copilot models…",
                () => _ = Task.Run(async () =>
                {
                    var s = await _client.DescribeAvailableModelsAsync(CancellationToken.None);
                    SessionLog.Write("[demo-script] available models:\n" + s);
                    _status.ShowToast("Available Copilot models written to debug log.", StatusSeverity.Info);
                })),
            MenuItem("Force banner: dummy auth error",
                () => _status.SetBanner("Dummy auth banner — testing recovery UX. Click Open Folder to clear.")),
        });

        var headerActions = HStack(8,
            Button(openCmd),
            generateButton,
            Button(exportCmd),
            devMenu)
            .Landmark(Microsoft.UI.Xaml.Automation.Peers.AutomationLandmarkType.Navigation);

        // ── Title bar ───────────────────────────────────────────────────
        var titleBarSubtitle = string.IsNullOrEmpty(model.Title)
            ? (projectRoot is null ? "Open a folder to begin" : IoPath.GetFileName(projectRoot))
            : model.Title;

        var rightHeader = generationStatus is { } gs
            ? (Element)TextBlock(gs).FontSize(12).Opacity(0.7).VAlign(VerticalAlignment.Center)
            : Empty();

        var titleBar = TitleBar("Demo Script Tool") with
        {
            Subtitle = titleBarSubtitle,
            Content = headerActions,
            RightHeader = rightHeader,
        };

        // ── Body ────────────────────────────────────────────────────────
        // FlexColumn (not VStack) so we can mark the steps panel `Flex(grow:1)`.
        // Without that, StepsPanel's inner ScrollView gets unbounded height and
        // simply overflows the window — the user can't scroll past the first
        // step or two. The banner / DemoPromptPanel above stay at natural height
        // (Flex shrink is 1 by default for non-grow children).
        var body = (FlexColumn(
            announce.Region,
            banner is not null
                ? InlineBanner.Render(banner, BannerKind.Error)
                : (parseError is not null
                    ? InlineBanner.Render($"demo-script.md parse error — {parseError}", BannerKind.Error)
                    : Empty()),
            Component<DemoPromptPanel, DemoPromptPanelProps>(
                new DemoPromptPanelProps(model, OnDemoPromptChanged, OnDemoTitleChanged))
                .Margin(0, banner is null && parseError is null ? 0 : 12, 0, 0),
            (parseError is null
                ? (Element)Component<StepsPanel, StepsPanelProps>(
                    new StepsPanelProps(model, isGenerating, OnPromptChanged, OnTitleChanged, OnRunStep, OnCopyDelta, OnAddStep, OnDeleteStep, OnRegenFromStep))
                    .Flex(grow: 1, basis: 0)
                : Empty()))
            with { RowGap = 0 })
            .Padding(16)
            .Flex(grow: 1);

        var toastBanner = toast is { } t
            ? (Element)Border(
                    HStack(8,
                        TextBlock(SymbolFor(t.Severity)).FontSize(14).VAlign(VerticalAlignment.Center),
                        TextBlock(t.Message).Foreground(Theme.PrimaryText).VAlign(VerticalAlignment.Center)))
                .Background(BackgroundFor(t.Severity))
                .WithBorder(BorderFor(t.Severity), 1)
                .CornerRadius(8)
                .Padding(horizontal: 12, vertical: 8)
                .HAlign(HorizontalAlignment.Right)
                .VAlign(VerticalAlignment.Bottom)
                .Margin(0, 0, 24, 24)
                .AutomationName(t.Message)
            : Empty();

        var rootGrid = Grid(
            columns: [GridSize.Star()],
            rows: [GridSize.Auto, GridSize.Star()],
            titleBar.Grid(row: 0),
            FlexColumn(body).Grid(row: 1));

        // Layer the toast above the main body without affecting layout.
        return CommandHost(
            [openCmd, generateCmd, exportCmd],
            Grid(
                columns: [GridSize.Star()],
                rows: [GridSize.Star()],
                rootGrid.Grid(row: 0),
                toastBanner.Grid(row: 0)))
            .Backdrop(BackdropKind.Mica);
    }

    static bool AnyDelta(DemoScriptModel m)
    {
        foreach (var s in m.Steps)
            if (!string.IsNullOrWhiteSpace(s.Delta)) return true;
        return false;
    }

    static string HashBytes(byte[] bytes)
    {
        var h = System.Security.Cryptography.SHA256.HashData(bytes);
        var sb = new System.Text.StringBuilder(64);
        for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
        return sb.ToString();
    }

    /// <summary>
    /// Restore the on-disk artifact for a step into <see cref="StepModel.Code"/>
    /// and <see cref="StepModel.OutputPath"/> so the UI shows the existing code
    /// after re-opening a folder. Single-file mode reads <c>step-NN.cs</c>
    /// directly; multi-file mode prefers <c>step-NN/Program.cs</c> for the
    /// viewer body and points OutputPath at the step directory. Returns true
    /// if anything was restored.
    /// </summary>
    static async Task<bool> TryRestoreStepArtifactAsync(StepModel step, string projectRoot, bool multiFile, CancellationToken ct)
    {
        try
        {
            string? primary = null;

            if (multiFile)
            {
                var stepDir = IoPath.Combine(projectRoot, $"step-{step.Number:D2}");
                if (Directory.Exists(stepDir))
                {
                    var program = IoPath.Combine(stepDir, "Program.cs");
                    if (File.Exists(program)) primary = program;
                }
            }
            else
            {
                var path = IoPath.Combine(projectRoot, $"step-{step.Number:D2}.cs");
                if (File.Exists(path)) primary = path;
            }

            // Restore the delta sidecar even if no primary code file is present
            // — a step might have a hand-written delta the user wants to view
            // before generating any code. Frontmatter (model id + generated
            // timestamp + content hash) round-trips through the parser so the
            // per-card provenance footer + stale indicator survive a restart.
            string? storedHash = null;
            var deltaPath = GenerationPipeline.DeltaSidecarPath(step.Number, projectRoot);
            if (File.Exists(deltaPath))
            {
                try
                {
                    var raw = await File.ReadAllTextAsync(deltaPath, ct).ConfigureAwait(false);
                    var (body, generatedBy, generatedAt, contentHash) = GenerationPipeline.ParseDeltaSidecar(raw);
                    step.ReplaceDelta(body);
                    if (generatedBy is not null || generatedAt is not null)
                        step.SetGenerationProvenance(generatedBy, generatedAt);
                    storedHash = contentHash;
                }
                catch { /* best-effort restore */ }
            }

            if (primary is null) return false;
            var bytes = await File.ReadAllBytesAsync(primary, ct).ConfigureAwait(false);
            step.ReplaceCode(System.Text.Encoding.UTF8.GetString(bytes));
            step.SetOutputPath(primary);

            // Stale check: re-hash the live disk bytes and compare against the
            // hash captured at generate time. Mismatch means the file moved
            // (hand edit, git checkout, sed/find replace, fix-mode regenerate
            // that didn't update the sidecar). We always store the LIVE hash
            // on the step so the next regenerate's compare is against current
            // disk state, not stale state — but the StaleSinceLoad flag
            // remembers what we found so the UI can flag it.
            var liveHash = GenerationPipeline.ComputeBytesHash(bytes);
            step.SetSourceHash(liveHash);
            step.SetStaleSinceLoad(storedHash is not null && !string.Equals(storedHash, liveHash, StringComparison.Ordinal));
            return true;
        }
        catch
        {
            return false;
        }
    }

    static string SymbolFor(StatusSeverity s) => s switch
    {
        StatusSeverity.Success => "✓",
        StatusSeverity.Warning => "⚠",
        StatusSeverity.Error => "✕",
        _ => "ⓘ",
    };

    static ThemeRef BackgroundFor(StatusSeverity s) => s switch
    {
        StatusSeverity.Success => Theme.SystemSuccessBackground,
        StatusSeverity.Warning => Theme.SystemCautionBackground,
        StatusSeverity.Error => Theme.SystemCriticalBackground,
        _ => Theme.SystemNeutralBackground,
    };

    static ThemeRef BorderFor(StatusSeverity s) => s switch
    {
        StatusSeverity.Success => Theme.SystemSuccess,
        StatusSeverity.Warning => Theme.SystemCaution,
        StatusSeverity.Error => Theme.SystemCritical,
        _ => Theme.SystemNeutral,
    };

    static void InitPicker(object picker)
    {
        var window = ReactorApp.ActiveHost?.Window;
        if (window is null) return;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }
}
