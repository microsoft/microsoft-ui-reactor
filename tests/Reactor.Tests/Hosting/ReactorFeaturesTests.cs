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
            AppContext.SetSwitch(SwitchName, false);
        }
    }

    [Fact]
    public void IsDevtoolsSupported_DefaultsOff()
    {
        AppContext.SetSwitch(SwitchName, false);

        Assert.False(ReactorFeatures.IsDevtoolsSupported);
    }

    [Fact]
    public void DevtoolsCliParser_RecognizesDevtoolsVerbs_WithoutLoadingHandlers()
    {
        var options = DevtoolsCliParser.Parse(["app.exe", "--devtools", "run", "--mcp-port", "1234"]);

        Assert.Equal(DevtoolsSubverb.Run, options.Subverb);
        Assert.Equal(1234, options.McpPort);
        Assert.False(options.PreviewAndDevtoolsConflict);
        Assert.DoesNotContain(
            options.GetType().GetProperties().Select(p => p.PropertyType),
            t => t.FullName == "Microsoft.UI.Reactor.Hosting.Devtools.DevtoolsMcpServer");
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
                ["app.exe", "--devtools", "run", "--mcp-port", "1234"],
                title: "Spec051",
                width: 320,
                height: 240);

            Assert.True(consumed);
            Assert.Contains(
                "RuntimeHostConfigurationOption Include=\"Reactor.DevtoolsSupport\"",
                stderr.ToString());
            Assert.False(ReactorApp.DevtoolsEnabled);
        }
        finally
        {
            Console.SetError(originalError);
            AppContext.SetSwitch(SwitchName, false);
            ReactorApp.ResetDevtoolsEnabledForTests();
        }
    }
}
