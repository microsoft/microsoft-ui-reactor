using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Reactor.Cli.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// `mur devtools` is deprecated in favour of the WinUI DevTools in the winapp CLI
/// (`winapp run . --devtools`). Every invocation prints a notice that names the
/// winapp equivalent, to stderr only, and then keeps working.
/// </summary>
[Collection("ConsoleTests")]
public class DevtoolsDeprecationTests
{
    private static readonly Func<string, string?> NoEnv = _ => null;

    [Fact]
    public void EveryKnownVerb_HasItsOwnMappingRow()
    {
        // A verb added to the router without a row would silently fall back to the
        // launch row and tell the user the wrong replacement.
        var missing = DevtoolsVerbs.KnownVerbs
            .Where(v => DevtoolsDeprecation.Find(v) is null)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public void MappingKeys_AreUnique_AndEveryRowNamesAReplacementOrSaysWhyNot()
    {
        var keys = DevtoolsDeprecation.Mappings.Select(m => m.Key).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var m in DevtoolsDeprecation.Mappings)
        {
            if (m.HasEquivalent)
                Assert.StartsWith("winapp ", m.WinappForm);
            else
                Assert.DoesNotContain("TODO", m.WinappForm);
            Assert.False(string.IsNullOrWhiteSpace(m.WinappForm), m.Key);
        }
    }

    [Theory]
    [InlineData(new string[0], "winapp run . --devtools")]
    [InlineData(new[] { "MyApp.csproj" }, "winapp run . --devtools")]
    [InlineData(new[] { "MyApp.csproj", "--mcp-port", "9000" }, "winapp run . --devtools")]
    [InlineData(new[] { "--print-config" }, "the MCP server is not carried over")]
    [InlineData(new[] { "--component", "Counter" }, "component preview host")]
    [InlineData(new[] { "session" }, "winapp devtools list")]
    [InlineData(new[] { "session", "list", "--pretty" }, "winapp devtools list")]
    [InlineData(new[] { "session", "clean" }, "not lockfiles")]
    [InlineData(new[] { "tree", "--pretty" }, "winapp devtools inspect")]
    [InlineData(new[] { "TREE" }, "winapp devtools inspect")]
    [InlineData(new[] { "ancestors", "x" }, "winapp devtools inspect <selector> --ancestors")]
    [InlineData(new[] { "state" }, "winapp devtools get-state <selector>")]
    [InlineData(new[] { "fire", "App.OnClick" }, "winapp devtools invoke-handler <selector> <event>")]
    [InlineData(new[] { "click", "Submit" }, "winapp ui click <selector>")]
    [InlineData(new[] { "toggle", "x" }, "winapp ui invoke <selector> --action toggle")]
    [InlineData(new[] { "wait", "x" }, "winapp ui wait-for <selector>")]
    [InlineData(new[] { "screenshot", "--launch" }, "winapp ui screenshot")]
    [InlineData(new[] { "set-resource", "k", "v" }, "winapp devtools set-resource")]
    [InlineData(new[] { "windows.list" }, "winapp ui list-windows")]
    [InlineData(new[] { "logs" }, "`winapp devtools logs [--follow]`")]
    [InlineData(new[] { "reload" }, "`winapp run . --devtools --watch`")]
    [InlineData(new[] { "version" }, "`winapp devtools call DevTools.negotiate`")]
    [InlineData(new[] { "call", "references", "--pretty" }, "`winapp devtools get-property <selector>` (References section)")]
    [InlineData(new[] { "call", "tree" }, "use the matching winapp verb")]
    public void Notice_NamesTheWinappEquivalentForThisInvocation(string[] args, string expected)
    {
        var notice = DevtoolsDeprecation.BuildNotice(args);

        Assert.Contains("DEPRECATED", notice);
        Assert.Contains("Microsoft.UI.Reactor.Devtools", notice);
        Assert.Contains("`winapp run . --devtools`", notice);
        Assert.Contains("winui-devtools", notice);
        Assert.Contains(expected, notice);
        Assert.All(notice.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => Assert.StartsWith("[mur devtools] ", line));
    }

    [Fact]
    public void Notice_ForAVerb_DiffersFromTheLaunchNotice()
    {
        // Differential: the per-verb line must actually come from the verb's row.
        var launch = DevtoolsDeprecation.BuildNotice([]);
        var tree = DevtoolsDeprecation.BuildNotice(["tree"]);

        Assert.NotEqual(launch, tree);
        Assert.Contains("Instead of `mur devtools tree`: `winapp devtools inspect`", tree);
        Assert.Contains("Instead of `mur devtools [project]`: `winapp run . --devtools`", launch);
        Assert.Contains("--watch", launch);
    }

    [Fact]
    public void Mapping_MatchesTheShippedWinappVerbs()
    {
        // Pinned to what winapp actually ships (checked against winapp P4).
        Assert.Equal("winapp devtools logs [--follow]", DevtoolsDeprecation.Find("logs")!.WinappForm);
        Assert.True(DevtoolsDeprecation.Find("logs")!.HasEquivalent);
        Assert.StartsWith("winapp run . --devtools --watch", DevtoolsDeprecation.Find("reload")!.WinappForm);
        Assert.True(DevtoolsDeprecation.Find("reload")!.HasEquivalent);
        Assert.StartsWith("winapp devtools call DevTools.negotiate", DevtoolsDeprecation.Find("version")!.WinappForm);
        Assert.StartsWith("winapp devtools invoke-handler <selector> <event>", DevtoolsDeprecation.Find("fire")!.WinappForm);
        Assert.StartsWith("winapp devtools get-property <selector>", DevtoolsDeprecation.Find("references")!.WinappForm);
        Assert.Contains("References section", DevtoolsDeprecation.Find("references")!.WinappForm);
        Assert.Contains("--watch", DevtoolsDeprecation.Find(DevtoolsDeprecation.LaunchKey)!.WinappForm);
        // Preview-host rows stay "no equivalent yet" until Reactor hosting owns --preview.
        foreach (var key in new[] { "--component", "components", "switch", "windows.open" })
            Assert.False(DevtoolsDeprecation.Find(key)!.HasEquivalent, key);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData(" YES ", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void WriteNotice_HonoursTheSuppressionVariable(string? value, bool suppressed)
    {
        using var err = new StringWriter();
        DevtoolsDeprecation.WriteNotice(err, ["tree"],
            name => name == DevtoolsDeprecation.SuppressEnvVar ? value : null);

        Assert.Equal(suppressed, err.ToString().Length == 0);
    }

    [Fact]
    public void MappingTable_ListsEveryRow()
    {
        var table = DevtoolsDeprecation.BuildMappingTable();

        foreach (var m in DevtoolsDeprecation.Mappings)
            Assert.Contains(m.WinappForm, table);
        Assert.Contains("winapp run . --devtools", table);
    }

    [Fact]
    public void Run_PrintConfig_WritesNoticeToStderr_AndKeepsStdoutParseable()
    {
        var (exit, stdout, stderr) = WithoutSuppression(() =>
            Capture(() => DevtoolsSupervisor.Run(["--print-config", "--mcp-port", "5123"])));

        Assert.Equal(0, exit);
        Assert.Contains("DEPRECATED", stderr);
        Assert.Contains("the MCP server is not carried over", stderr);
        Assert.DoesNotContain("DEPRECATED", stdout);
        Assert.DoesNotContain("[mur devtools]", stdout);
        Assert.Equal(DevtoolsSupervisor.BuildPrintConfigPayload(5123), stdout);
    }

    [Fact]
    public void Run_Help_PrintsTheMappingTable_AndStillPrintsUsage()
    {
        var (exit, stdout, stderr) = WithoutSuppression(() =>
            Capture(() => DevtoolsSupervisor.Run(["--help"])));

        Assert.Equal(0, exit);
        Assert.Contains("DEPRECATED", stderr);
        Assert.Contains(DevtoolsDeprecation.BuildMappingTable(), stdout);
        Assert.Contains("mur devtools [project] [--component Name] [--mcp-port N]", stdout);
    }

    [Fact]
    public void Run_Suppressed_PrintsNoNotice()
    {
        var prior = Environment.GetEnvironmentVariable(DevtoolsDeprecation.SuppressEnvVar);
        Environment.SetEnvironmentVariable(DevtoolsDeprecation.SuppressEnvVar, "1");
        try
        {
            var (exit, _, stderr) = Capture(() => DevtoolsSupervisor.Run(["--print-config", "--mcp-port", "5124"]));
            Assert.Equal(0, exit);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DevtoolsDeprecation.SuppressEnvVar, prior);
        }
    }

    private static T WithoutSuppression<T>(Func<T> action)
    {
        var prior = Environment.GetEnvironmentVariable(DevtoolsDeprecation.SuppressEnvVar);
        Environment.SetEnvironmentVariable(DevtoolsDeprecation.SuppressEnvVar, null);
        try { return action(); }
        finally { Environment.SetEnvironmentVariable(DevtoolsDeprecation.SuppressEnvVar, prior); }
    }

    private static (int ExitCode, string Stdout, string Stderr) Capture(Func<int> action)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        Console.SetOut(outWriter);
        Console.SetError(errWriter);
        try
        {
            var exitCode = action();
            return (exitCode, outWriter.ToString(), errWriter.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }
}
