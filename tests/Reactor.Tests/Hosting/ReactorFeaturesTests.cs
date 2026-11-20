using Microsoft.UI.Reactor.Hosting;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Hosting;

[Collection("ConsoleTests")]
public class ReactorFeaturesTests
{
    private const string SwitchName = "Reactor.DevtoolsSupport";

    [Fact]
    public void IsDevtoolsSupported_ReadsAppContextSwitch()
    {
        try
        {
            AppContext.SetSwitch(SwitchName, true);
            Assert.True(ReactorFeatures.IsDevtoolsSupported);

            AppContext.SetSwitch(SwitchName, false);
            Assert.False(ReactorFeatures.IsDevtoolsSupported);
        }
        finally
        {
            AppContext.SetSwitch(SwitchName, TestSetup.ConfiguredDevtoolsSupport);
        }
    }

    [Fact]
    public void IsDevtoolsSupported_DefaultsOff()
        => TestSetup.WithDevtoolsSupportSwitch(false, static () => Assert.False(ReactorFeatures.IsDevtoolsSupported));

    [Fact]
    public void DevtoolsCliParser_RecognizesDevtoolsVerbs()
    {
        var options = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "Counter", "--vscode"]);

        Assert.Equal(DevtoolsSubverb.Run, options.Subverb);
        Assert.Equal("Counter", options.ComponentName);
        Assert.True(options.VsCodeMode);
        Assert.False(options.PreviewAndDevtoolsConflict);
    }

    [Fact]
    public void ReactorApp_Run_SwitchOnButNoPackage_PointsAtThePackage_NotTheSwitch()
    {
        // The switch is on by default in Debug, so there the missing piece is the package.
        var originalError = Console.Error;
        using var stderr = new StringWriter();
        // Spend the one-time lazy load of the Devtools package first. Otherwise, when this test
        // is the first to read Current, the load would register the real host after the null
        // below, and it would try to start a WinUI preview.
        _ = ReactorDevtoolsBootstrap.Current;
        var previousHost = ReactorDevtoolsBootstrap.CurrentForTests;
        var originalExitCode = Environment.ExitCode;

        AppContext.SetSwitch(SwitchName, true);
        ReactorDevtoolsBootstrap.RestoreForTests(null);
        Console.SetError(stderr);

        try
        {
            Assert.True(ReactorApp.TryRunDevtoolsForTest(["app.exe", "--devtools", "run"], title: "NoPackage", width: 320, height: 240));
        }
        finally
        {
            Console.SetError(originalError);
            ReactorDevtoolsBootstrap.RestoreForTests(previousHost);
            AppContext.SetSwitch(SwitchName, TestSetup.ConfiguredDevtoolsSupport);
            Environment.ExitCode = originalExitCode;
            ReactorApp.ResetDevtoolsEnabledForTests();
        }

        Assert.Contains("PackageReference Include=\"Microsoft.UI.Reactor.Devtools\" Version=\"", stderr.ToString());
        Assert.DoesNotContain("RuntimeHostConfigurationOption", stderr.ToString());
    }

    [Fact]
    public void ReactorApp_Run_SwitchOff_WithDevtoolsArg_EmitsActionableError()
    {
        var originalError = Console.Error;
        using var stderr = new StringWriter();

        AppContext.SetSwitch(SwitchName, false);
        ReactorApp.ResetDevtoolsEnabledForTests();
        Console.SetError(stderr);

        try
        {
            var consumed = ReactorApp.TryRunDevtoolsForTest(
                ["app.exe", "--devtools", "run", "--vscode"],
                title: "Spec051",
                width: 320,
                height: 240);

            Assert.True(consumed);
            // Outside Debug, lead with the build that works, and name both requirements at once.
            var text = stderr.ToString();
            Assert.Contains("dotnet run -c Debug", text);
            Assert.Contains("PackageReference Include=\"Microsoft.UI.Reactor.Devtools\"", text);
            Assert.Contains("<ReactorDiagnostics>true</ReactorDiagnostics>", text);
            Assert.False(ReactorApp.DevtoolsEnabled);
        }
        finally
        {
            Console.SetError(originalError);
            AppContext.SetSwitch(SwitchName, TestSetup.ConfiguredDevtoolsSupport);
            ReactorApp.ResetDevtoolsEnabledForTests();
        }
    }
}
