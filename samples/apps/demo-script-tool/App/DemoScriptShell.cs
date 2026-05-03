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
    readonly DemoScriptStore _store = new();
    readonly StepFileWriter _writer = new();
    readonly DotnetRunner _runner = new();
    readonly GhAuth _auth = new();
    readonly SpeakerNotesExporter _exporter = new();
    readonly StatusReporter _status = new();
    readonly GithubModelsClient _client;
    readonly GenerationPipeline _pipeline;

    public DemoScriptShell()
    {
        _client = new GithubModelsClient(_auth);
        _pipeline = new GenerationPipeline(_client, _runner, _writer, _auth, _status);
    }

    public override Element Render()
    {
        var (projectRoot, setProjectRoot) = UseState<string?>(null);
        var (model, setModel) = UseState(DemoScriptModel.Empty());
        var (parseError, setParseError) = UseState<DemoScriptParseError?>(null);
        var (banner, setBanner) = UseState<string?>(null);
        var (toast, setToast) = UseState<(string Message, StatusSeverity Severity)?>(null);
        var (generationStatus, setGenerationStatus) = UseState<string?>(null);
        var (isGenerating, setIsGenerating) = UseState(false);
        var watcherRef = UseRef<DemoScriptWatcher?>(null);
        var generationCtsRef = UseRef<CancellationTokenSource?>(null);
        var saveDebounceRef = UseRef<CancellationTokenSource?>(null);
        var announce = UseAnnounce();

        // Wire StatusReporter once. Channel its events to React state.
        UseEffect(() =>
        {
            void OnToast(string message, StatusSeverity severity)
            {
                setToast((message, severity));
                _ = Task.Delay(4000).ContinueWith(_ => setToast(null));
            }
            void OnGenerating(string? msg)
            {
                setGenerationStatus(msg);
                if (msg is not null) announce.Announce(msg, assertive: false);
            }
            void OnBanner(string? msg) => setBanner(msg);

            _status.Toast += OnToast;
            _status.Generating += OnGenerating;
            _status.Banner += OnBanner;
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
                    var (loaded, err) = await _store.LoadAsync(projectRoot, CancellationToken.None);
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
                    await _store.SaveAsync(model, projectRoot, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _status.ShowToast($"Save failed: {ex.Message}", StatusSeverity.Error);
                }
            });
        }

        // ── Commands ────────────────────────────────────────────────────
        async void OnOpenFolder()
        {
            try
            {
                var picker = new global::Windows.Storage.Pickers.FolderPicker();
                picker.FileTypeFilter.Add("*");
                InitPicker(picker);
                var folder = await picker.PickSingleFolderAsync();
                if (folder is null) return;

                var root = folder.Path;
                var (loaded, err) = await _store.LoadAsync(root, CancellationToken.None);
                setProjectRoot(root);
                setParseError(err);
                if (loaded is not null)
                {
                    setModel(loaded);
                    setBanner(null);
                    _status.ShowToast(loaded.Steps.Count == 0
                        ? $"Opened {IoPath.GetFileName(root)} — add a demo prompt to get started."
                        : $"Opened {IoPath.GetFileName(root)} ({loaded.Steps.Count} steps).");
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

        void OnGenerateAll()
        {
            if (projectRoot is null)
            {
                _status.ShowToast("Open a folder first (Ctrl+O).", StatusSeverity.Warning);
                return;
            }
            if (isGenerating)
            {
                generationCtsRef.Current?.Cancel();
                return;
            }

            var cts = new CancellationTokenSource();
            generationCtsRef.Current = cts;
            setIsGenerating(true);
            announce.Announce($"Generating {model.Steps.Count} steps…", assertive: false);

            _ = Task.Run(async () =>
            {
                try
                {
                    await _pipeline.GenerateAllAsync(model, projectRoot, cts.Token).ConfigureAwait(false);
                }
                finally
                {
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

        void OnCopyDelta(StepModel step)
        {
            if (string.IsNullOrEmpty(step.Delta)) return;
            try
            {
                var dp = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
                dp.SetText(step.Delta);
                global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dp);
                _status.ShowToast($"Step {step.Number} delta copied to clipboard.", StatusSeverity.Success);
                announce.Announce($"Step {step.Number} delta copied.", assertive: false);
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
            announce.Announce($"Added step {added.Number}.", assertive: false);
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
            MenuItem("Log model snapshot",
                () => System.Diagnostics.Debug.WriteLine($"[demo-script] title='{model.Title}' steps={model.Steps.Count} multiFile={model.IsMultiFile}")),
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
        var body = VStack(0,
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
                    new StepsPanelProps(model, OnPromptChanged, OnTitleChanged, OnRunStep, OnCopyDelta, OnAddStep, OnDeleteStep))
                : Empty()))
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
                .Padding(12, 8)
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
