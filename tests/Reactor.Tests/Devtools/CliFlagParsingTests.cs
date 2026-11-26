using System;
using System.IO;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

public class CliFlagParsingTests
{
    [Fact]
    public void NoFlags_ReturnsNullSubverb()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe"]);
        Assert.Null(opts.Subverb);
        Assert.False(opts.PreviewAndDevtoolsConflict);
    }

    // --width/--height let the docs screenshot harness declare the capture size
    // in doc-manifest.yaml, so doc apps can omit width/height from their own
    // ReactorApp.Run call without their screenshots changing size.
    [Fact]
    public void WidthAndHeight_AreParsed()
    {
        var opts = DevtoolsCliParser.Parse(
            ["app.exe", "--devtools", "run", "--width", "640", "--height", "480"]);
        Assert.Equal(640d, opts.WindowWidth);
        Assert.Equal(480d, opts.WindowHeight);
    }

    [Fact]
    public void WidthAndHeight_DefaultToNullWhenAbsent()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run"]);
        Assert.Null(opts.WindowWidth);
        Assert.Null(opts.WindowHeight);
    }

    // Null means "keep the size the app declared", so an unusable value must not
    // be mistaken for an explicit request to resize — least of all to zero.
    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("not-a-number")]
    // Same overflow class as coordinates: DipToPhysicalScalar multiplies by DPI and
    // casts to int, so an oversized-but-finite extent becomes an int-max resize
    // rather than falling back to the app's own size.
    [InlineData("1e308")]
    [InlineData("70000")]
    public void Width_RejectsNonPositiveOrNonFinite(string value)
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--width", value]);
        Assert.Null(opts.WindowWidth);
    }

    // Positive control for the bound above: a genuinely large but usable extent —
    // an 8K-wide window at the limit — must still be accepted, or the guard would
    // be satisfied by rejecting everything.
    [Theory]
    [InlineData("65536")]
    [InlineData("7680")]
    public void Width_AcceptsLargeButRepresentableDimensions(string value)
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--width", value]);
        Assert.NotNull(opts.WindowWidth);
    }

    [Fact]
    public void Width_MissingValueAtEndOfArgs_IsIgnored()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--width"]);
        Assert.Null(opts.WindowWidth);
    }

    [Fact]
    public void XAndY_AreParsed()
    {
        var opts = DevtoolsCliParser.Parse(
            ["app.exe", "--devtools", "run", "--x", "2600", "--y", "120"]);
        Assert.Equal(2600d, opts.WindowX);
        Assert.Equal(120d, opts.WindowY);
    }

    // A coordinate is not a dimension. A monitor positioned left of or above the
    // primary has a negative origin, and 0 is the primary's own origin, so the
    // positive-only rule that guards --width/--height would silently discard
    // perfectly valid positions. This is the case that would regress if someone
    // "simplified" the two parsers into one.
    [Theory]
    [InlineData("0", 0d)]
    [InlineData("-2560", -2560d)]
    [InlineData("-1080.5", -1080.5d)]
    public void X_AcceptsZeroAndNegativeCoordinates(string value, double expected)
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--x", value]);
        Assert.Equal(expected, opts.WindowX);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("over-there")]
    public void X_RejectsNonFinite(string value)
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--x", value]);
        Assert.Null(opts.WindowX);
    }

    // Finite is not sufficient. ReactorWindow.DipToPhysicalPoint computes
    // dip * dpi / 96 and casts to int; 1e308 * 144 / 96 overflows to infinity and
    // the cast saturates to int.MaxValue, so the window is moved to the far edge
    // of the coordinate space and capture fails. Measured, not assumed.
    [Theory]
    [InlineData("1e308")]
    [InlineData("-1e308")]
    [InlineData("70000")]
    [InlineData("-70000")]
    public void X_RejectsFiniteButUnrepresentableCoordinates(string value)
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--x", value]);
        Assert.Null(opts.WindowX);
    }

    // The bound must not shut out real multi-monitor layouts: a 4-wide 4K array
    // reaches ~7,680 DIPs, well inside it.
    [Theory]
    [InlineData("65536")]
    [InlineData("-65536")]
    [InlineData("7680")]
    public void X_AcceptsCoordinatesWithinRepresentableRange(string value)
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--x", value]);
        Assert.NotNull(opts.WindowX);
    }

    [Fact]
    public void XAndY_DefaultToNullWhenAbsent()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run"]);
        Assert.Null(opts.WindowX);
        Assert.Null(opts.WindowY);
    }

    // The harness formats these invariantly; parsing must match, or a
    // comma-decimal machine would read "600.5" as 6005.
    [Fact]
    public void Width_ParsesInvariantlyRegardlessOfCurrentCulture()
    {
        var original = global::System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            global::System.Globalization.CultureInfo.CurrentCulture =
                new global::System.Globalization.CultureInfo("nl-NL");
            var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--width", "600.5"]);
            Assert.Equal(600.5d, opts.WindowWidth);
        }
        finally
        {
            global::System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void DevtoolsRun_ParsesAsRun()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run"]);
        Assert.Equal(DevtoolsSubverb.Run, opts.Subverb);
    }

    [Fact]
    public void DevtoolsList_ParsesAsList()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "list"]);
        Assert.Equal(DevtoolsSubverb.List, opts.Subverb);
    }

    [Theory]
    [InlineData("screenshot", "'--devtools screenshot' was removed", "winapp ui screenshot")]
    [InlineData("tree", "'--devtools tree' was removed", "winapp devtools inspect")]
    [InlineData("TREE", "'--devtools tree' was removed", "winapp devtools inspect")]
    [InlineData("on", "'--devtools on' is a winapp option", "winapp run . --devtools on")]
    [InlineData("off", "'--devtools off' is a winapp option", "winapp run . --devtools off")]
    [InlineData("headless", "'--devtools headless' is a winapp option", "winapp run . --devtools headless")]
    public void RemovedValues_ReportTheWinappCommand_InsteadOfRunningAComponentNamedAfterThem(string verb, string message, string winapp)
    {
        string[] args = ["app.exe", "--devtools", verb, "CounterDemo"];
        var opts = DevtoolsCliParser.Parse(args);

        Assert.Null(opts.Subverb);
        Assert.Null(opts.ComponentName);
        var error = DevtoolsCliParser.RemovedSubverbMessage(args);
        Assert.NotNull(error);
        Assert.Contains(message, error);
        Assert.Contains(winapp, error);
        Assert.Null(DevtoolsCliParser.RemovedAppVerbNotice(args));
    }

    [Theory]
    [InlineData("app")]
    [InlineData("APP")]
    public void AppVerb_IsANotice_NotAnError_AndRunsNothing(string verb)
    {
        // `--devtools app` used to run the app with the dev menu. It now runs the app normally.
        string[] args = ["app.exe", "--devtools", verb];
        var opts = DevtoolsCliParser.Parse(args);

        Assert.Null(opts.Subverb);
        Assert.Null(DevtoolsCliParser.RemovedSubverbMessage(args));
        var notice = DevtoolsCliParser.RemovedAppVerbNotice(args);
        Assert.NotNull(notice);
        Assert.Contains("winapp run . --devtools on", notice);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("list")]
    [InlineData("Counter")]
    public void KeptSubverbsAndComponentNames_AreNotReportedAsRemoved(string token)
    {
        // Positive control for the theory above: the same position holding a kept verb
        // or a component name parses normally.
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", token]);

        Assert.NotNull(opts.Subverb);
        Assert.Null(DevtoolsCliParser.RemovedSubverbMessage(["app.exe", "--devtools", token]));
        Assert.Null(DevtoolsCliParser.RemovedAppVerbNotice(["app.exe", "--devtools", token]));
    }

    [Theory]
    [InlineData("--mcp-port", "1234")]
    [InlineData("--mcp-transport", "stdio")]
    [InlineData("--devtools-log-level", "trace")]
    [InlineData("--devtools-logs", "off")]
    [InlineData("--devtools-project", "MyProj")]
    public void RemovedMcpFlags_AreIgnored_AndDoNotBecomeTheComponentName(string flag, string value)
    {
        // The MCP server's flags went with it. An old launch profile that still passes
        // one must run the named component, not fail or read the value as a component.
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "Counter", flag, value]);

        Assert.Equal(DevtoolsSubverb.Run, opts.Subverb);
        Assert.Equal("Counter", opts.ComponentName);
        Assert.Null(opts.EmbedValidationError);
    }

    [Theory]
    [InlineData("--mcp-port")]
    [InlineData("--mcp-transport")]
    [InlineData("--devtools-log-level")]
    [InlineData("--devtools-logs")]
    [InlineData("--devtools-logs-capacity")]
    [InlineData("--devtools-project")]
    public void RemovedMcpFlags_AreNamedInTheNotice(string flag)
    {
        var notice = DevtoolsCliParser.RemovedFlagsNotice(["app.exe", "--devtools", "run", flag, "x"]);

        Assert.NotNull(notice);
        Assert.StartsWith($"Ignoring {flag}:", notice);
        Assert.Contains("winapp run . --devtools on", notice);
    }

    [Fact]
    public void RemovedFlagsNotice_ListsEveryRemovedFlag_AndIsNullWithoutThem()
    {
        Assert.Equal(
            "Ignoring --mcp-port, --mcp-transport: the in-app MCP server was removed. Inspect the app with the WinUI DevTools: winapp run . --devtools on",
            DevtoolsCliParser.RemovedFlagsNotice(["app.exe", "--devtools", "run", "--mcp-transport", "stdio", "--mcp-port", "1"]));
        // Lookalikes and surviving flags are not removed flags.
        Assert.Null(DevtoolsCliParser.RemovedFlagsNotice(["app.exe", "--devtools", "run", "--vscode", "--fps", "5", "--mcp-portx", "1"]));
    }

    [Fact]
    public void RemovedSubverbName_AsAComponentNameAfterRun_StillRuns()
    {
        // Only the token right after --devtools is a verb; `run tree` previews a component called "tree".
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "tree"]);

        Assert.Equal(DevtoolsSubverb.Run, opts.Subverb);
        Assert.Equal("tree", opts.ComponentName);
        Assert.Null(DevtoolsCliParser.RemovedSubverbMessage(["app.exe", "--devtools", "run", "tree"]));
    }

    [Fact]
    public void DevtoolsBareFlag_DefaultsToRun()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools"]);
        Assert.Equal(DevtoolsSubverb.Run, opts.Subverb);
    }

    [Fact]
    public void DevtoolsRun_WithComponentName()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "MyComponent"]);
        Assert.Equal(DevtoolsSubverb.Run, opts.Subverb);
        Assert.Equal("MyComponent", opts.ComponentName);
    }

    [Fact]
    public void DevtoolsList_WithOutputPath()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "list", "c:/tmp/components.txt"]);
        Assert.Equal(DevtoolsSubverb.List, opts.Subverb);
        Assert.Equal("c:/tmp/components.txt", opts.ListOutputPath);
    }

    [Fact]
    public void PreviewAlias_MapsToRun()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--preview", "MyComp"]);
        Assert.Equal(DevtoolsSubverb.Run, opts.Subverb);
        Assert.Equal("MyComp", opts.ComponentName);
    }

    [Fact]
    public void PreviewListAlias_MapsToList()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--preview-list"]);
        Assert.Equal(DevtoolsSubverb.List, opts.Subverb);
    }

    [Fact]
    public void DevtoolsListLegacyAlias_MapsToList()
    {
        // --devtools-list is the one-step alias used by tools that pre-date the subverb form.
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools-list"]);
        Assert.Equal(DevtoolsSubverb.List, opts.Subverb);
    }

    [Fact]
    public void BothPreviewAndDevtools_IsConflict()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--preview", "--devtools", "run"]);
        Assert.True(opts.PreviewAndDevtoolsConflict);
    }

    [Fact]
    public void BothPreviewAndDevtools_WithEmbed_DoesNotHonorEmbed()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--preview", "--devtools", "run", "--embed", "--embed-host-pid", "1234"]);

        Assert.True(opts.PreviewAndDevtoolsConflict);
        Assert.False(opts.EmbedRequested);
        Assert.Null(opts.EmbedValidationError);
    }

    [Fact]
    public void BothPreviewListAndDevtoolsList_IsConflict()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--preview-list", "--devtools", "list"]);
        Assert.True(opts.PreviewAndDevtoolsConflict);
    }

    [Fact]
    public void VsCodeFlag_IsPickedUp()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--vscode"]);
        Assert.True(opts.VsCodeMode);
    }

    [Fact]
    public void FpsFlag_IsClampedToRange()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--fps", "9999"]);
        Assert.Equal(30, opts.Fps);

        var opts2 = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--fps", "0"]);
        Assert.Equal(1, opts2.Fps);

        var opts3 = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--fps", "15"]);
        Assert.Equal(15, opts3.Fps);
    }

    [Fact]
    public void Fps_Default_Is10()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run"]);
        Assert.Equal(10, opts.Fps);
    }

    [Fact]
    public void ComponentName_LeadingDashIsNotTreatedAsName()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--vscode"]);
        Assert.Null(opts.ComponentName);
    }

    [Fact]
    public void UnknownDevtoolsVerb_FallsBackToRun()
    {
        // Defensive: an unknown trailing token is treated as a component name, not a verb.
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "MyComponent"]);
        Assert.Equal(DevtoolsSubverb.Run, opts.Subverb);
        Assert.Equal("MyComponent", opts.ComponentName);
    }

    [Fact]
    public void Embed_Flag_RequiresRunSubverb()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "list", "--embed", "--embed-host-pid", "1234"]);
        Assert.Equal("--embed requires '--devtools run'", opts.EmbedValidationError);
        Assert.Null(opts.Subverb);
    }

    [Fact]
    public void Embed_Flag_RequiresHostPid()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--embed"]);
        Assert.Equal("--embed requires --embed-host-pid <pid>", opts.EmbedValidationError);
        Assert.Null(opts.EmbedHostPid);
    }

    [Fact]
    public void Embed_Flag_ImpliesVscode()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--embed", "--embed-host-pid", "1234"]);
        Assert.True(opts.VsCodeMode);
        Assert.True(opts.EmbedAutoEnabledVsCode);
    }

    [Fact]
    public void Embed_Flag_WithExplicitVscode_NoAutoFlag()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--vscode", "--embed", "--embed-host-pid", "1234"]);
        Assert.True(opts.VsCodeMode);
        Assert.False(opts.EmbedAutoEnabledVsCode);
    }

    [Fact]
    public void EmbedMode_Child_Default()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--embed", "--embed-host-pid", "1234"]);
        Assert.Equal(WindowEmbedStyle.Child, opts.EmbedStyle);
    }

    [Fact]
    public void EmbedMode_Owner_Parses()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--embed", "--embed-mode", "owner", "--embed-host-pid", "1234"]);
        Assert.Equal(WindowEmbedStyle.Owner, opts.EmbedStyle);
    }

    [Fact]
    public void EmbedMode_InvalidValue_DefaultsToChild()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--embed", "--embed-mode", "bogus", "--embed-host-pid", "1234"]);
        Assert.Equal(WindowEmbedStyle.Child, opts.EmbedStyle);
    }

    [Fact]
    public void EmbedHostPid_Parses_Decimal()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--embed", "--embed-host-pid", "9999"]);
        Assert.Equal(9999, opts.EmbedHostPid);
    }

    [Fact]
    public void EmbedHostPid_NonInteger_ReportsParseError()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--embed", "--embed-host-pid", "xyz"]);

        Assert.Null(opts.EmbedHostPid);
        Assert.Equal("--embed-host-pid value 'xyz' is not a valid process id", opts.EmbedValidationError);
    }

    [Fact]
    public void NoEmbed_NoErrors()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run"]);
        Assert.False(opts.EmbedRequested);
        Assert.Null(opts.EmbedValidationError);
    }
}

[Collection("ConsoleTests")]
public class DevtoolsHostCliTests
{
    [Fact]
    public void TryRunDevtoolsForTest_InvalidEmbed_PrintsValidationErrorToStderr()
    {
        using var stderr = new StringWriter();
        var originalError = Console.Error;
        var originalExitCode = Environment.ExitCode;

        try
        {
            Environment.ExitCode = 0;
            Console.SetError(stderr);

            var handled = ReactorApp.TryRunDevtoolsForTest(
                ["app.exe", "--devtools", "list", "--embed", "--embed-host-pid", "1234"],
                title: "Preview",
                width: 800,
                height: 600);

            Assert.True(handled);
            Assert.Equal(2, Environment.ExitCode);
        }
        finally
        {
            Console.SetError(originalError);
            Environment.ExitCode = originalExitCode;
        }

        Assert.Contains("[reactor] --embed requires '--devtools run'", stderr.ToString());
    }

    [Fact]
    public void TryHandleCommandLine_InvalidEmbed_PrintsValidationErrorToStderr()
    {
        var options = DevtoolsCliParser.Parse(["app.exe", "--devtools", "list", "--embed", "--embed-host-pid", "1234"]);
        using var stderr = new StringWriter();
        var originalError = Console.Error;

        try
        {
            Console.SetError(stderr);
            var handled = new DevtoolsHost().TryHandleCommandLine(new ReactorDevtoolsBootRequest(
                options,
                Title: "Preview",
                Width: 800,
                Height: 600,
                FullScreen: false,
                HostRoot: null,
                HostRootFactory: null,
                RootRenderFunc: null,
                Configure: null));

            Assert.True(handled);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Contains("[reactor] --embed requires '--devtools run'", stderr.ToString());
    }

    [Theory]
    [InlineData("screenshot", "'--devtools screenshot' was removed", "winapp ui screenshot")]
    [InlineData("tree", "'--devtools tree' was removed", "winapp devtools inspect")]
    [InlineData("on", "'--devtools on' is a winapp option", "winapp run . --devtools on")]
    public void TryRunDevtoolsForTest_RemovedSubverb_PrintsTheWinappCommandAndExits2_BeforeTheSupportGate(string verb, string message, string winapp)
    {
        const string switchName = "Reactor.DevtoolsSupport";
        using var stderr = new StringWriter();
        var originalError = Console.Error;
        var originalExitCode = Environment.ExitCode;

        bool handled;
        try
        {
            // Switch off: the removed-verb message must win over "enable Reactor.DevtoolsSupport".
            AppContext.SetSwitch(switchName, false);
            Environment.ExitCode = 0;
            Console.SetError(stderr);

            handled = ReactorApp.TryRunDevtoolsForTest(["app.exe", "--devtools", verb], title: "App", width: 800, height: 600);

            Assert.Equal(2, Environment.ExitCode);
        }
        finally
        {
            Console.SetError(originalError);
            Environment.ExitCode = originalExitCode;
            AppContext.SetSwitch(switchName, TestSetup.ConfiguredDevtoolsSupport);
        }

        var text = stderr.ToString();
        Assert.True(handled);
        Assert.Contains($"[reactor] {message}", text);
        Assert.Contains(winapp, text);
        Assert.DoesNotContain("RuntimeHostConfigurationOption", text);
    }

    [Theory]
    [InlineData(new[] { "app.exe", "--devtools", "on", "--preview", "X" }, "'--devtools on' is a winapp option")]
    [InlineData(new[] { "app.exe", "--devtools", "run", "--preview", "X" }, "pass either --devtools or --preview, not both.")]
    public void TryRunDevtoolsForTest_WithPreview_ReportsTheSpecificError_AndExits2(string[] args, string message)
    {
        // A removed value wins over the generic --devtools/--preview conflict, and the conflict
        // itself is an error (exit 2), not a silent exit 0.
        using var stderr = new StringWriter();
        var originalError = Console.Error;
        var originalExitCode = Environment.ExitCode;
        bool handled;
        int exitCode;
        try
        {
            Environment.ExitCode = 0;
            Console.SetError(stderr);
            handled = ReactorApp.TryRunDevtoolsForTest(args, title: "App", width: 800, height: 600);
            exitCode = Environment.ExitCode;
        }
        finally
        {
            Console.SetError(originalError);
            Environment.ExitCode = originalExitCode;
        }

        Assert.True(handled);
        Assert.Equal(2, exitCode);
        Assert.Contains($"[reactor] {message}", stderr.ToString());
    }

    [Fact]
    public void AppVerb_WithPreview_IsIgnored_AndThePreviewRuns()
    {
        var opts = DevtoolsCliParser.Parse(["app.exe", "--devtools", "app", "--preview", "Counter"]);

        Assert.False(opts.PreviewAndDevtoolsConflict);
        Assert.Equal(DevtoolsSubverb.Run, opts.Subverb);
        Assert.Equal("Counter", opts.ComponentName);
    }

    [Fact]
    public void TryRunDevtoolsForTest_AppVerb_PrintsANotice_AndLetsTheAppRun()
    {
        // A launch profile that still passes `--devtools app` must start the app, not exit.
        using var stderr = new StringWriter();
        var originalError = Console.Error;
        var originalExitCode = Environment.ExitCode;

        bool handled;
        try
        {
            Environment.ExitCode = 0;
            Console.SetError(stderr);

            handled = ReactorApp.TryRunDevtoolsForTest(["app.exe", "--devtools", "app"], title: "App", width: 800, height: 600);

            Assert.Equal(0, Environment.ExitCode);
        }
        finally
        {
            Console.SetError(originalError);
            Environment.ExitCode = originalExitCode;
        }

        Assert.False(handled);
        Assert.Contains("[reactor] Ignoring '--devtools app'", stderr.ToString());
        Assert.Contains("winapp run . --devtools on", stderr.ToString());
    }

    [Theory]
    [InlineData(new[] { "app.exe", "--devtools", "run", "--mcp-transport", "stdio" }, true)]
    [InlineData(new[] { "app.exe", "--devtools", "run", "--mcp-port", "1234" }, true)]
    [InlineData(new[] { "app.exe", "--devtools", "run" }, false)]
    public void TryRunDevtools_RemovedMcpFlags_PrintANotice_AndStillRunThePreview(string[] args, bool expectNotice)
    {
        const string switchName = "Reactor.DevtoolsSupport";
        var host = new CapturingDevtoolsHost();
        var previous = ReactorDevtoolsBootstrap.CurrentForTests;
        using var stderr = new StringWriter();
        var originalError = Console.Error;
        ReactorDevtoolsBootstrap.Register(host);
        try
        {
            AppContext.SetSwitch(switchName, true);
            Console.SetError(stderr);

            Assert.True(ReactorApp.TryRunDevtoolsForTest(args, title: "Preview", width: 800, height: 600));
            Assert.NotNull(host.Captured);
        }
        finally
        {
            Console.SetError(originalError);
            AppContext.SetSwitch(switchName, TestSetup.ConfiguredDevtoolsSupport);
            ReactorDevtoolsBootstrap.RestoreForTests(previous);
        }

        Assert.Equal(expectNotice, stderr.ToString().Contains("[reactor] Ignoring --mcp-", StringComparison.Ordinal));
    }

    // The whole manifest-driven capture size rests on one expression in
    // TryRunDevtoolsCore -- `options.WindowWidth ?? width` -- so it gets a
    // direct test rather than only the end-to-end screenshot evidence.
    // Reaching it needs both the feature switch on and a registered host, so a
    // fake host captures the boot request the real devtools package would get.
    private sealed class CapturingDevtoolsHost : IReactorDevtoolsHost
    {
        public ReactorDevtoolsBootRequest? Captured { get; private set; }

        public bool TryHandleCommandLine(ReactorDevtoolsBootRequest request)
        {
            Captured = request;
            return true;
        }

    }

    [Theory]
    // CLI overrides the size the app declared in code.
    [InlineData(new[] { "app.exe", "--devtools", "run", "--width", "1024", "--height", "768" }, 1024d, 768d)]
    // No CLI size: the app's own Run arguments survive.
    [InlineData(new[] { "app.exe", "--devtools", "run" }, 800d, 600d)]
    // Per-axis override: the unspecified axis must fall back, not reset.
    [InlineData(new[] { "app.exe", "--devtools", "run", "--width", "1024" }, 1024d, 600d)]
    // A rejected value must not be mistaken for an explicit resize.
    [InlineData(new[] { "app.exe", "--devtools", "run", "--width", "0" }, 800d, 600d)]
    public void TryRunDevtools_CliSizeOverridesRunSize(string[] args, double expectedWidth, double expectedHeight)
    {
        const string switchName = "Reactor.DevtoolsSupport";
        var host = new CapturingDevtoolsHost();
        // Register writes a process-global static with no public reset, so the
        // prior value is restored below; otherwise every later test in this
        // process would resolve the fake instead of the real devtools host.
        var previous = ReactorDevtoolsBootstrap.CurrentForTests;
        ReactorDevtoolsBootstrap.Register(host);
        try
        {
            AppContext.SetSwitch(switchName, true);
            var handled = ReactorApp.TryRunDevtoolsForTest(args, title: "Preview", width: 800, height: 600);

            Assert.True(handled);
            Assert.NotNull(host.Captured);
            Assert.Equal(expectedWidth, host.Captured!.Width);
            Assert.Equal(expectedHeight, host.Captured.Height);
        }
        finally
        {
            AppContext.SetSwitch(switchName, TestSetup.ConfiguredDevtoolsSupport);
            ReactorDevtoolsBootstrap.RestoreForTests(previous);
        }
    }

    private sealed class AppRoot : Component
    {
        public override Element Render() => Factories.TextBlock("app root");
    }

    private sealed class OtherComponent : Component
    {
        public override Element Render() => Factories.TextBlock("other");
    }

    // Run<T> always passes its own factory. `--preview Other` must mount Other, not T; only T
    // itself uses Run's factory (and reports Run's mount site).
    [Fact]
    public void PreviewRootFactory_MountsTheNamedComponent_NotRunsRoot()
    {
        Func<Component> factory = () => new AppRoot();

#pragma warning disable IL2026
        Assert.IsType<OtherComponent>(DevtoolsHost.PreviewRootFactory(typeof(AppRoot), factory, typeof(OtherComponent))());
        Assert.Same(factory, DevtoolsHost.PreviewRootFactory(typeof(AppRoot), factory, typeof(AppRoot)));
        Assert.IsType<AppRoot>(DevtoolsHost.PreviewRootFactory(typeof(AppRoot), hostRootFactory: null, typeof(AppRoot))());
#pragma warning restore IL2026
    }

    // The preview reports Run's call site only for the root that call mounts.
    [Fact]
    public void PreviewRootMountSite_KeepsRunSiteOnlyForTheAppsOwnRoot()
    {
        var site = new SourceLocation("App.cs", 12, 0);

        Assert.Equal(site, DevtoolsHost.PreviewRootMountSite(site, typeof(AppRoot), typeof(AppRoot)));
        Assert.Null(DevtoolsHost.PreviewRootMountSite(site, typeof(AppRoot), typeof(OtherComponent)));
        Assert.Null(DevtoolsHost.PreviewRootMountSite(site, hostRoot: null, typeof(OtherComponent)));
        Assert.Null(DevtoolsHost.PreviewRootMountSite(runSite: null, typeof(AppRoot), typeof(AppRoot)));
    }

    // An older Devtools package can't JIT against this core, so core compares the builds first.
    [Fact]
    public void DevtoolsPackageMismatch_ReportsBothVersions_AndTheVersionToReference()
    {
        Assert.Null(ReactorApp.DevtoolsPackageMismatchMessage("0.2.0+abc", "0.2.0+abc"));

        var message = ReactorApp.DevtoolsPackageMismatchMessage("0.2.0+abc", "0.1.1+def");
        Assert.NotNull(message);
        Assert.Contains("0.1.1+def", message);
        Assert.Contains("Version=\"0.2.0\"", message);

        // Only the real package is checked: a host from any other assembly (a test fake) is not.
        Assert.Null(ReactorApp.DevtoolsPackageMismatchMessage(typeof(DevtoolsHostCliTests).Assembly));
        Assert.Null(ReactorApp.DevtoolsPackageMismatchMessage(typeof(DevtoolsHost).Assembly));
        Assert.NotNull(ReactorApp.DevtoolsPackageMismatchMessage("0.2.0", "0.2.0-local"));
    }

    // A host from an assembly named like the package but built at another version: the gate must
    // stop before TryHandleCommandLine (which throws here, as a stale package would fail to JIT).
    [Fact]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("AotAnalysis", "IL3050", Justification = "Emits a stand-in Devtools assembly for a stale-package test; Reactor.Tests runs on the JIT only.")]
    public void TryRunDevtools_StaleDevtoolsPackage_ReportsBothVersions_Exits2_WithoutCallingTheHost()
    {
        const string switchName = "Reactor.DevtoolsSupport";
        var host = EmitPackageNamedHost("0.0.1+stale");
        Assert.Contains("0.0.1+stale", ReactorApp.DevtoolsPackageMismatchMessage(host.GetType().Assembly) ?? "");

        var previous = ReactorDevtoolsBootstrap.CurrentForTests;
        var originalExitCode = Environment.ExitCode;
        var originalError = Console.Error;
        using var stderr = new StringWriter();
        ReactorDevtoolsBootstrap.Register(host);
        try
        {
            AppContext.SetSwitch(switchName, true);
            Console.SetError(stderr);

            Assert.True(ReactorApp.TryRunDevtoolsForTest(["app.exe", "--devtools", "run"], title: "Preview", width: 800, height: 600));
            Assert.Equal(2, Environment.ExitCode);
        }
        finally
        {
            Console.SetError(originalError);
            Environment.ExitCode = originalExitCode;
            AppContext.SetSwitch(switchName, TestSetup.ConfiguredDevtoolsSupport);
            ReactorDevtoolsBootstrap.RestoreForTests(previous);
        }

        Assert.Contains("Microsoft.UI.Reactor.Devtools: 0.0.1+stale", stderr.ToString());
    }

    [global::System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Emits a stand-in for a stale package; JIT-only test.")]
    private static IReactorDevtoolsHost EmitPackageNamedHost(string informationalVersion)
    {
        var versionAttribute = new global::System.Reflection.Emit.CustomAttributeBuilder(
            typeof(global::System.Reflection.AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!,
            [informationalVersion]);
        var assembly = global::System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new global::System.Reflection.AssemblyName("Microsoft.UI.Reactor.Devtools"),
            global::System.Reflection.Emit.AssemblyBuilderAccess.Run,
            [versionAttribute]);
        var type = assembly.DefineDynamicModule("StaleDevtools").DefineType(
            "StaleDevtoolsHost",
            global::System.Reflection.TypeAttributes.Public | global::System.Reflection.TypeAttributes.Sealed,
            typeof(object),
            [typeof(IReactorDevtoolsHost)]);
        var contract = typeof(IReactorDevtoolsHost).GetMethod(nameof(IReactorDevtoolsHost.TryHandleCommandLine))!;
        var method = type.DefineMethod(
            contract.Name,
            global::System.Reflection.MethodAttributes.Public | global::System.Reflection.MethodAttributes.Virtual
                | global::System.Reflection.MethodAttributes.Final | global::System.Reflection.MethodAttributes.HideBySig
                | global::System.Reflection.MethodAttributes.NewSlot,
            typeof(bool),
            [typeof(ReactorDevtoolsBootRequest)]);
        var il = method.GetILGenerator();
        il.Emit(global::System.Reflection.Emit.OpCodes.Ldstr, "the stale host must not be called");
        il.Emit(global::System.Reflection.Emit.OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)])!);
        il.Emit(global::System.Reflection.Emit.OpCodes.Throw);
        type.DefineMethodOverride(method, contract);
        return (IReactorDevtoolsHost)Activator.CreateInstance(type.CreateType())!;
    }

    // --x/--y place the capture window on a monitor whose scale matches the
    // repo's capture convention when that is not the primary display. Capture is
    // PrintWindow over the live HWND in physical pixels, so the monitor's DPI is
    // what ends up baked into the PNG.
    [Fact]
    public void BuildPositionedWindowSpec_WithBothAxes_SetsManualPosition()
    {
        var options = DevtoolsCliParser.Parse(
            ["app.exe", "--devtools", "run", "--x", "2600", "--y", "0"]);

        var spec = DevtoolsHost.BuildPositionedWindowSpec(options, "Preview", width: 520, height: 360);

        Assert.NotNull(spec);
        Assert.Equal(WindowStartPosition.Manual, spec!.StartPosition);
        Assert.Equal((2600d, 0d), spec.ManualPosition);
        Assert.Equal(520d, spec.Width);
        Assert.Equal(360d, spec.Height);
        // Persistence would restore a saved rect over the position just requested.
        Assert.False(spec.PersistPlacement);
    }

    // Returning a non-null spec flips BuildInitialWindowSpec to its pass-through
    // branch, so any flat option not restated on the spec is silently dropped.
    // fullScreen is the one that bites: before --x/--y existed this path always
    // passed InitialWindowSpec: null, and a preview of a full-screen app opened
    // full-screen. Composing both halves is what catches that — testing the
    // builder alone cannot, because the loss happens at the seam.
    [Theory]
    [InlineData(true, PresenterKind.FullScreen)]
    [InlineData(false, PresenterKind.Overlapped)]
    public void PositionedSpec_PreservesFullScreenThroughBuildInitialWindowSpec(
        bool fullScreen, PresenterKind expected)
    {
        var options = DevtoolsCliParser.Parse(
            ["app.exe", "--devtools", "run", "--x", "2600", "--y", "0"]);
        var positioned = DevtoolsHost.BuildPositionedWindowSpec(
            options, "Preview", width: 520, height: 360, fullScreen: fullScreen);

        var resolved = ReactorApp.BuildInitialWindowSpec(new ReactorAppOptions(
            WindowTitle: "Preview",
            WindowWidth: 520,
            WindowHeight: 360,
            FullScreen: fullScreen,
            InitialWindowSpec: positioned));

        Assert.Equal(expected, resolved.Presenter);
        Assert.Equal((2600d, 0d), resolved.ManualPosition);
    }

    // The same composition without an origin must keep taking the synthesis
    // branch — a positive control proving the assertion above is about the
    // positioned spec and not about BuildInitialWindowSpec in general.
    [Fact]
    public void WithoutOrigin_FullScreenStillFlowsThroughFlatOptions()
    {
        var options = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run"]);
        var positioned = DevtoolsHost.BuildPositionedWindowSpec(
            options, "Preview", width: 520, height: 360, fullScreen: true);

        Assert.Null(positioned);

        var resolved = ReactorApp.BuildInitialWindowSpec(new ReactorAppOptions(
            WindowTitle: "Preview",
            WindowWidth: 520,
            WindowHeight: 360,
            FullScreen: true,
            InitialWindowSpec: positioned));

        Assert.Equal(PresenterKind.FullScreen, resolved.Presenter);
    }

    [Theory]
    [InlineData((object)new[] { "app.exe", "--devtools", "run" })]
    [InlineData((object)new[] { "app.exe", "--devtools", "run", "--x", "2600" })]
    [InlineData((object)new[] { "app.exe", "--devtools", "run", "--y", "0" })]
    public void BuildPositionedWindowSpec_WithoutBothAxes_ReturnsNull(string[] args)
    {
        var options = DevtoolsCliParser.Parse(args);

        Assert.Null(DevtoolsHost.BuildPositionedWindowSpec(options, "Preview", width: 520, height: 360));
    }
}
