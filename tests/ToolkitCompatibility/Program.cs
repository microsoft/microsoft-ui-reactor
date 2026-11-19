using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using static Microsoft.UI.Reactor.Factories;

namespace ToolkitCompatibility;

internal static class Program
{
#if COMPATIBILITY_NATIVE_BASELINE
    internal const string CompiledVariant = "direct-native";
#else
    internal const string CompiledVariant = "sample-mappings";
#endif
    internal static RunOptions Options { get; private set; } = null!;
    internal static ResultWriter Results { get; private set; } = null!;
    internal static PendingRenderError? LastRenderError { get; private set; }
    // Errors are counted, never cleared, so a later render cannot erase one already reported.
    internal static int RenderErrorCount { get; private set; }
    internal static int ScenarioErrorBaseline { get; set; }

    internal static void ReportRenderError(Exception exception, string source)
    {
        LastRenderError = new(exception, source);
        RenderErrorCount++;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--page")
        {
            string route = args[1];
            Program.Check(WctControls.SamplePages.Routes.Any(r => r.Id == route), "Unknown sample route.");
            Program.Check(route != "camera", "Camera requires the explicitly gated camera probe, not --page.");
            Console.WriteLine($"# interactive sample page={route}; dynamic-code={RuntimeFeature.IsDynamicCodeSupported}; variant={CompiledVariant}");
            ReactorApp.Run("Toolkit compatibility input", _ => WctControls.SamplePages.CreatePage(route),
                width: 900, height: 700);
            return 0;
        }
        if (args.SequenceEqual(["--list-camera-probe"]))
        {
            Console.WriteLine("environment/identity-runtime-location");
            Console.WriteLine("CameraPreview/camera-frame-lifecycle");
            return 0;
        }
        if (args.SequenceEqual(["--list-scenarios"]))
        {
            Console.WriteLine("environment/identity-runtime-location");
            foreach (var name in Scenarios.Names)
                foreach (var operation in Scenarios.Operations(name))
                    Console.WriteLine($"{name}/{operation}");
            return 0;
        }
        if (args.SequenceEqual(["--protocol-self-test"]))
            return ProtocolSelfTest.Run().GetAwaiter().GetResult();
        Options = RunOptions.Parse(args);
        if (Options.List)
        {
            foreach (var name in Scenarios.Names)
                Console.WriteLine(name);
            return 0;
        }

        Results = new ResultWriter(Options.Output);
        Results.Start(Options);
        ReactorApp.DefaultRenderErrorHandler = error =>
        {
            ReportRenderError(error.Exception, error.Source.ToString());
            Results.Note($"Reactor {error.Source} error: {error.Exception}");
            return null;
        };
#if COMPATIBILITY_NATIVE_BASELINE
        Results.Note("variant=direct-native; explicit providers; not automatic-registration acceptance");
        NativeBaseline.Register();
#else
        Results.Note("variant=sample-mappings; automatic metadata registration");
#endif
        ReactorApp.Run<CompatibilityRoot>("Toolkit compatibility", width: 900, height: 700);
        return Results.ExitCode;
    }

    internal static void Check([DoesNotReturnIf(false)] bool condition, string detail)
    {
        if (!condition)
            throw new InvalidOperationException(detail);
    }
}

internal sealed record RunOptions(
    string Output, string Deployment, string Runtime, string ExpectedRoot,
    string ExpectedIdentity, string Filter, bool List, bool AllowCamera, bool CameraProbeOnly, string ExpectedVariant)
{
    internal static RunOptions Parse(string[] args)
    {
        if (args.SequenceEqual(["--list"]))
            return new("", "", "", "", "", "", true, false, false, "");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 == args.Length || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Expected unique --name value pairs.");
        }
        string Required(string key) => values.Remove(key, out var value)
            ? value : throw new ArgumentException($"Missing {key}.");
        var result = new RunOptions(
            Required("--output"), Required("--deployment"), Required("--runtime"),
            Required("--expected-root"), Required("--expected-identity"),
            values.Remove("--filter", out var filter) ? filter : "", false,
            values.Remove("--allow-camera", out var allow) && bool.Parse(allow),
            values.Remove("--camera-probe-only", out var probeOnly) && bool.Parse(probeOnly),
            Required("--expected-variant"));
        if (values.Count != 0 ||
            result.Deployment is not ("unpackaged" or "loose" or "msix") ||
            result.Runtime is not ("jit" or "aot"))
            throw new ArgumentException("Unknown option, deployment or runtime.");
        return result;
    }
}

internal sealed class CompatibilityRoot : Component
{
    private Action<(int Revision, Element Element)>? _setElement;
    private FrameworkElement? _subject;
    private int _unmountNotifications;
    private int _revision;
    internal int RenderedRevision { get; private set; }

    public override Element Render()
    {
        var (state, setElement) = UseState((Revision: 0, Element: (Element)TextBlock("Starting compatibility scenarios")));
        _setElement = setElement;
        RenderedRevision = state.Revision;
        UseEffect(() => { _ = RunAsync(); }, Array.Empty<object>());
        return Border(state.Element).Padding(24);
    }

    internal async Task ShowAsync(Element element)
    {
        int revision = ++_revision;
        _setElement!((revision, element));
        await WaitAsync(() => RenderedRevision == revision, "render revision");
        await Task.Delay(100);
        ThrowRenderError();
        (ReactorApp.PrimaryWindow!.NativeWindow.Content as FrameworkElement)?.UpdateLayout();
    }

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i)))
                yield return child;
    }

    internal static bool IsAttached(FrameworkElement control) =>
        control.XamlRoot is not null &&
        Descendants(ReactorApp.PrimaryWindow!.NativeWindow.Content)
            .Any(child => ReferenceEquals(child, control));

    private Element TrackSubject(Element element) =>
        element.AutomationId("subject").OnMountAdd(control => _subject = control)
            .OnUnmountAdd(_ => _unmountNotifications++);

    internal FrameworkElement CurrentSubject => _subject ?? throw new InvalidOperationException("No tracked native subject.");

    internal Task ShowScenarioAsync(Scenario scenario, int phase, string? name = null, bool focusSentinel = false)
    {
        Element element = scenario.Build(phase);
        if (name is not null) element = element.AutomationName(name);
        element = scenario.InContext(TrackSubject(element), phase);
        if (focusSentinel)
            element = VStack(Button("Focus sentinel", () => { }).AutomationId("focus-sentinel"), element);
        return ShowAsync(element);
    }

    internal static async Task WaitAsync(Func<bool> predicate, string description)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate())
        {
            ThrowRenderError();
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(description);
            await Task.Delay(25);
        }
        // A satisfied predicate must not hide a render or effect error reported meanwhile.
        ThrowRenderError();
    }

    internal static bool HasScenarioRenderError =>
        Program.RenderErrorCount > Program.ScenarioErrorBaseline && Program.LastRenderError is not null;

    internal static void ThrowRenderError()
    {
        if (HasScenarioRenderError && Program.LastRenderError is { } error)
            throw new InvalidOperationException(
                $"Reactor {error.Source} failed ({Program.RenderErrorCount - Program.ScenarioErrorBaseline} error(s) in this scenario).",
                error.Exception);
    }

    private async Task RunAsync()
    {
        var results = Program.Results;
        try
        {
            await Task.Delay(100);
            Microsoft.UI.Xaml.Application.Current.UnhandledException += (_, error) =>
                results.Note($"WinUI unhandled exception: {error.Exception}");
            results.Run("environment", "identity-runtime-location", CheckEnvironment);
            if (results.Failed != 0)
                return;
            if (Program.Options.CameraProbeOnly)
            {
                if (Program.Options.AllowCamera)
                    await results.RunAsync("CameraPreview", "camera-frame-lifecycle",
                        () => CameraSampleChecks.RunAsync(this, true));
                else
                    results.Block("CameraPreview", "camera-frame-lifecycle",
                        "Camera access has not been consented. No device was opened.", "approval");
                return;
            }

            var selected = Scenarios.Names.Where(n => Program.Options.Filter.Length == 0 ||
                n.Equals(Program.Options.Filter, StringComparison.Ordinal)).ToArray();
            Program.Check(WctControls.SamplePages.Routes.Select(r => r.ControlName).Order()
                .SequenceEqual(Scenarios.Names.Order()), "Scenario catalog differs from actual sample routes.");
            Program.Check(selected.Length != 0, "Filter selected zero controls.");
            foreach (var name in selected)
            {
                if (name == "CameraPreview" && !Program.Options.AllowCamera)
                {
                    foreach (var operation in Scenarios.Operations(name))
                        results.Block(name, operation, "Camera access requires an explicitly consented opt-in run.", "approval");
                    continue;
                }
                var scenario = Scenarios.Create(name);
#if COMPATIBILITY_NATIVE_BASELINE
                scenario = NativeBaseline.Replace(name, scenario);
#endif
                FrameworkElement? first = null;
                int unmountBaseline = _unmountNotifications;
                _subject = null;
                await results.RunAsync(name, "mount", async () =>
                {
                    await ShowScenarioAsync(scenario, 0);
                    results.Note($"mount reference={_subject?.GetType().FullName ?? "(none)"}; IsLoaded={_subject?.IsLoaded}; parent={_subject?.Parent?.GetType().FullName ?? "(none)"}");
                    await WaitAsync(() => _subject is { ActualWidth: > 0, ActualHeight: > 0 } control &&
                        IsAttached(control), $"{name} attached and arranged");
                    first = _subject!;
                    results.Note($"mounted AutomationId='{AutomationProperties.GetAutomationId(first)}'");
                    CheckType(first, name);
                    scenario.Check(first, 0);
                });
                await results.RunAsync(name, "update", async () =>
                {
                    Program.Check(first is not null, "Mount failed; update cannot be established.");
                    await ShowScenarioAsync(scenario, 1);
                    var updated = _subject!;
                    Program.Check(ReferenceEquals(first, updated), "Update replaced the native control.");
                    scenario.Check(updated, 1);
                });
                await results.RunAsync(name, "remove", async () =>
                {
                    Program.Check(first is not null, "Mount failed; removal cannot be established.");
                    await ShowAsync(TextBlock("Removed").AutomationId("removed"));
                    await WaitAsync(() => !IsAttached(first!), $"{name} detached");
                    Program.Check(_unmountNotifications == unmountBaseline + 1,
                        $"Expected one public OnUnmount callback, got {_unmountNotifications - unmountBaseline}.");
                });
                await results.RunAsync(name, "remount", async () =>
                {
                    _subject = null;
                    await ShowScenarioAsync(scenario, 0);
                    await WaitAsync(() => _subject is { ActualWidth: > 0, ActualHeight: > 0 } control &&
                        IsAttached(control), $"{name} remounted and arranged");
                    var remounted = _subject!;
                    Program.Check(first is not null && !ReferenceEquals(first, remounted),
                        "Remount must establish a fresh native control after removal.");
                    CheckType(remounted, name);
                    scenario.Check(remounted, 0);
                });
                foreach (var operation in Scenarios.PendingOperations(name))
                {
#if !COMPATIBILITY_NATIVE_BASELINE
                    if (operation == "uia-name-role-update")
                        await results.RunAsync(name, operation, () => LocalEvidenceChecks.NameRoleAsync(this, scenario));
                    else if (operation == "uia-value-patterns")
                        await results.RunAsync(name, operation, () => LocalEvidenceChecks.PatternValuesAsync(CurrentSubject));
                    else if (operation == "programmatic-focus")
                        await results.RunAsync(name, operation, () => LocalEvidenceChecks.FocusAsync(this, scenario));
                    else if (operation == "light-dark-render-capture")
                        await results.RunAsync(name, operation, () => LocalEvidenceChecks.ThemesAsync(CurrentSubject, name));
                    else if (operation == "rtl-render-capture")
                        await results.RunAsync(name, operation, () => LocalEvidenceChecks.RtlAsync(CurrentSubject, name));
                    else if (operation == "high-contrast-render-capture")
                        await results.RunAsync(name, operation, () => LocalEvidenceChecks.HighContrastAsync(CurrentSubject, name));
                    else if (operation == "os-text-scaling")
                        await results.RunAsync(name, operation, LocalEvidenceChecks.TextScalingAsync);
                    else if (name == "ImageCropper" && operation == "crop-export-dimensions-pixels")
                        await results.RunAsync(name, operation, () => ImageCropperSampleChecks.RunAsync(this));
                    else if (name == "CameraPreview" && operation == "actual-frames-stop-reentry")
                        await results.RunAsync(name, operation, () => CameraSampleChecks.RunAsync(this, Program.Options.AllowCamera));
                    else
#endif
                        results.Block(name, operation, "Not yet exercised by the isolated harness; not a pass.");
                }
#if COMPATIBILITY_NATIVE_BASELINE
                foreach (var operation in Scenarios.AdditionalOperations(name))
                    results.Block(name, operation, "Direct-native diagnostic does not run mapped sample scenarios.", "diagnostic-only");
#else
                if (name is "SettingsCard" or "SettingsExpander")
                {
                    await results.RunAsync(name, "settings-rich-string-slots-and-naming",
                        () => SettingsSampleChecks.RunAsync(this));
                    await results.RunAsync(name, "settings-borrowed-header-ownership",
                        () => HeaderOwnershipChecks.RunAsync(this));
                }
                if (name == "ContentSizer")
                    await results.RunAsync(name, "sample-target-lifecycle", () => SizersSampleChecks.ContentAsync(this));
                if (name == "PropertySizer")
                    await results.RunAsync(name, "sample-native-binding-target", () => SizersSampleChecks.PropertyAsync(this));
                if (name == "RichSuggestBox")
                {
                    await results.RunAsync(name, "sample-native-suggestion-filtering", () => RichSuggestSampleChecks.RunAsync(this));
                    await results.RunAsync(name, "sample-native-suggestion-choice", () => RichSuggestSampleChecks.ChooseAsync(this));
                    await results.RunAsync(name, "sample-deferred-choice-burst", () => RichSuggestSampleChecks.DeferredChoicesAsync(this));
                }
                if (name == "SwitchPresenter")
                    await results.RunAsync(name, "sample-native-case-selection", () => SwitchPresenterSampleChecks.RunAsync(this));
                if (name == "TokenizingTextBox")
                    await results.RunAsync(name, "sample-filtering-tokens", () => TokenizingSampleChecks.RunAsync(this));
                if (name == "RangeSelector")
                    await results.RunAsync(name, "sample-silent-drift-reassertion", () => RangeSampleChecks.RunAsync(this));
                if (name == "TabbedCommandBar")
                {
                    await results.RunAsync(name, "sample-native-tabs-commands", () => TabbedCommandBarSampleChecks.SampleAsync(this));
                    results.Inapplicable(name, "inherited-content-visibility",
                        "Pinned Toolkit template presents SelectedItem, not inherited Content. Equivalent direct-native baseline confirms this native contract; actual sample composes its document outside.");
                }
#endif
                results.Note($"Removing final remount of {name}");
                await ShowAsync(TextBlock("Between scenarios"));
                results.Note($"Removed final remount of {name}");
            }
        }
        catch (Exception ex)
        {
            results.Failure("harness", "unhandled", ex);
        }
        finally
        {
            results.Complete();
            ReactorApp.PrimaryWindow?.Close();
        }
    }

    private static void CheckType(FrameworkElement control, string name)
    {
        Program.Results.Note($"realized-type={control.GetType().FullName}");
        Program.Check(control.GetType().FullName == $"CommunityToolkit.WinUI.Controls.{name}",
            $"Expected actual Toolkit {name}; realized {control.GetType().FullName}.");
    }

    private static void CheckEnvironment()
    {
        var options = Program.Options;
        Program.Check(options.ExpectedVariant == Program.CompiledVariant,
            $"Compiled variant {Program.CompiledVariant} differs from expected {options.ExpectedVariant}.");
        Program.Check(RuntimeInformation.ProcessArchitecture == Architecture.X64, "Process is not x64.");
        Program.Check(RuntimeFeature.IsDynamicCodeSupported == (options.Runtime == "jit"),
            $"Runtime mismatch: dynamic-code={RuntimeFeature.IsDynamicCodeSupported}.");
        string? identity = null;
        string? location = null;
        bool development = false;
        try
        {
            var package = Windows.ApplicationModel.Package.Current;
            identity = package.Id.Name;
            location = package.InstalledLocation.Path;
            development = package.IsDevelopmentMode;
        }
        catch (InvalidOperationException) when (options.Deployment == "unpackaged") { }
        catch (COMException ex) when (options.Deployment == "unpackaged" &&
            ex.HResult == unchecked((int)0x80073D54)) { }

        Program.Results.Note($"dynamic-code={RuntimeFeature.IsDynamicCodeSupported}; " +
            $"identity={identity ?? "(none)"}; installed-location={location ?? "(none)"}; " +
            $"development={development}; executable={Environment.ProcessPath}");
        Program.Check(options.Deployment == "unpackaged"
            ? identity is null
            : identity == options.ExpectedIdentity, "Package identity mismatch.");
        Program.Check(options.Deployment == "unpackaged" ||
            development == (options.Deployment == "loose"), "Deployment form mismatch.");
        string root = Path.GetFullPath(options.ExpectedRoot).TrimEnd('\\') + "\\";
        Program.Check(Environment.ProcessPath?.StartsWith(root, StringComparison.OrdinalIgnoreCase) == true,
            $"Executable is outside expected root {root}.");
        if (options.Deployment != "unpackaged")
            Program.Check(string.Equals(Path.GetFullPath(location!).TrimEnd('\\'), root.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase), "Actual package location differs from expected location.");
    }
}

internal sealed record Result(string Control, string Scenario, string Status, string Detail, string? ReasonKind = null);

internal sealed class ScenarioBlockedException(string message, string reasonKind = "not-implemented") : Exception(message)
{
    internal string ReasonKind { get; } = reasonKind;
}

internal sealed class NativeContractException(string message) : Exception(message);

[JsonSerializable(typeof(Result))]
internal sealed partial class ResultJsonContext : JsonSerializerContext;

internal sealed class ResultWriter
{
    private readonly StreamWriter _json;
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private int _count;
    internal int Failed { get; private set; }
    internal int Blocked { get; private set; }
    internal int ExitCode => Failed != 0 ? 1 : Blocked != 0 ? 2 : 0;

    internal ResultWriter(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _json = new StreamWriter(path, append: false) { AutoFlush = true };
    }

    internal void Start(RunOptions options)
    {
        Console.WriteLine("TAP version 13");
        Note($"deployment={options.Deployment}; runtime={options.Runtime}; filter={options.Filter}");
        var selected = Scenarios.Names.Where(n => options.Filter.Length == 0 || n == options.Filter).ToArray();
        Program.Check(selected.Length != 0, "Zero controls selected.");
        Console.WriteLine($"1..{(options.CameraProbeOnly ? 2 : 1 + selected.Sum(n => Scenarios.Operations(n).Count()))}");
    }

    internal void Note(string detail) => Console.WriteLine("# " + detail.Replace("\r", "").Replace("\n", "\n# "));

    internal void Run(string control, string scenario, Action action)
    {
        Program.ScenarioErrorBaseline = Program.RenderErrorCount;
        try { action(); CompatibilityRoot.ThrowRenderError(); Write(control, scenario, "passed", "Assertions satisfied."); }
        catch (Exception ex) { Failure(control, scenario, ex); }
        finally { Program.ScenarioErrorBaseline = Program.RenderErrorCount; }
    }

    internal async Task RunAsync(string control, string scenario, Func<Task> action)
    {
        // Errors reported before this scenario are excluded; any reported during it, even
        // between two renders, turn an otherwise satisfied result into a failure.
        Program.ScenarioErrorBaseline = Program.RenderErrorCount;
        try { await action(); CompatibilityRoot.ThrowRenderError(); Write(control, scenario, "passed", "Assertions satisfied."); }
        // Blocked and inapplicable are only honest when Reactor reported no error meanwhile.
        catch (ScenarioBlockedException ex) when (!CompatibilityRoot.HasScenarioRenderError)
        { Block(control, scenario, ex.Message, ex.ReasonKind); }
        catch (NativeContractException ex) when (!CompatibilityRoot.HasScenarioRenderError)
        { Inapplicable(control, scenario, ex.Message); }
        catch (Exception ex) when (ex is ScenarioBlockedException or NativeContractException)
        {
            Failure(control, scenario, new InvalidOperationException(
                $"Reactor {Program.LastRenderError!.Source} failed before the scenario was classified as {ex.GetType().Name}: {ex.Message}",
                Program.LastRenderError.Exception));
        }
        catch (Exception ex) { Failure(control, scenario, ex); }
        finally { Program.ScenarioErrorBaseline = Program.RenderErrorCount; }
    }

    internal void Failure(string control, string scenario, Exception ex) =>
        Write(control, scenario, "failed", ex.ToString());

    internal void Block(string control, string scenario, string detail, string reasonKind = "not-implemented") =>
        Write(control, scenario, "blocked", detail, reasonKind);

    internal void Inapplicable(string control, string scenario, string detail) =>
        Write(control, scenario, "inapplicable", detail, "native-contract");

    private void Write(string control, string scenario, string status, string detail, string? reasonKind = null)
    {
        Program.Check(_keys.Add(control + "/" + scenario), "Duplicate scenario result.");
        _json.WriteLine(JsonSerializer.Serialize(new Result(control, scenario, status, detail, reasonKind),
            ResultJsonContext.Default.Result));
        if (status == "failed") Failed++;
        if (status == "blocked") Blocked++;
        string skip = status == "inapplicable" ? " # SKIP native contract" : "";
        Console.WriteLine($"{(status is "passed" or "inapplicable" ? "ok" : "not ok")} {++_count} - {control}/{scenario}{skip}");
        Note($"{status}: {detail}");
        Console.Out.Flush();
    }

    internal void Complete()
    {
        Note($"Total results: {_count}");
        Note($"Total failures: {Failed}");
        Note($"Total blocked: {Blocked}");
        Note("COMPLETE");
        Console.Out.Flush();
        _json.Dispose();
    }
}
