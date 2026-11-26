using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Reactor.Cli.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// `mur devtools` has been removed in favour of the WinUI DevTools in the winapp CLI
/// (`winapp run . --devtools on`). Every invocation only tells the user what to run instead,
/// on stderr, and exits with <see cref="DevtoolsRemoved.ExitCode"/>.
/// </summary>
public class DevtoolsRemovedTests
{
    // Every verb the removed `mur devtools` accepted (its former KnownVerbs set), frozen here
    // so a row can't silently disappear: the user would get the launch row instead.
    private static readonly string[] FormerVerbs =
    {
        "version", "windows", "components", "switch", "tree", "screenshot",
        "state", "click", "type", "focus", "invoke", "toggle", "select",
        "scroll", "expand", "collapse", "wait", "fire", "reload", "shutdown",
        "call", "logs", "properties", "set-property", "resources",
        "set-resource", "styles", "ancestors",
        "windows.list", "windows.activate", "windows.close", "windows.open",
    };

    [Fact]
    public void EveryFormerVerb_HasItsOwnRow()
    {
        var missing = FormerVerbs.Where(v => DevtoolsRemoved.Find(v) is null).ToArray();
        Assert.Empty(missing);

        foreach (var v in FormerVerbs)
            Assert.Equal(v, DevtoolsRemoved.Resolve([v]).Key, ignoreCase: true);
    }

    [Fact]
    public void MappingKeys_AreUnique_AndEveryRowNamesAReplacementOrSaysWhyNot()
    {
        var keys = DevtoolsRemoved.Mappings.Select(m => m.Key).ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var m in DevtoolsRemoved.Mappings)
        {
            Assert.False(string.IsNullOrWhiteSpace(m.WinappForm), m.Key);
            if (m.HasEquivalent)
                Assert.StartsWith("winapp ", m.WinappForm);
            else
                Assert.DoesNotContain("TODO", m.WinappForm);
        }
    }

    [Fact]
    public void Mapping_MatchesTheShippedWinappCommands()
    {
        Assert.Equal("winapp devtools logs [--follow]", Row("logs").WinappForm);
        Assert.StartsWith("winapp run . --devtools on --watch", Row("reload").WinappForm);
        Assert.StartsWith("winapp devtools call DevTools.negotiate", Row("version").WinappForm);
        Assert.StartsWith("winapp devtools invoke-handler <selector> <event>", Row("fire").WinappForm);
        Assert.StartsWith("winapp devtools get-property <selector>", Row("references").WinappForm);
        Assert.Contains("References section", Row("references").WinappForm);
        Assert.Contains("--watch", Row(DevtoolsRemoved.LaunchKey).WinappForm);
        Assert.Equal("winapp devtools list", Row("session list").WinappForm);
    }

    [Theory]
    [InlineData("--component")]
    [InlineData("components")]
    [InlineData("switch")]
    [InlineData("windows.open")]
    public void PreviewRows_PointAtTheInAppPreviewHost(string key)
    {
        var row = Row(key);
        Assert.StartsWith("winapp run . --devtools on -- --preview <Name>", row.WinappForm);
        Assert.Contains("Microsoft.UI.Reactor.Devtools package", row.WinappForm);
    }

    [Theory]
    [InlineData("--print-config")]
    [InlineData("call")]
    public void McpRows_SayNotCarriedOver(string key)
    {
        var row = Row(key);
        Assert.False(row.HasEquivalent);
        Assert.Contains("not carried over", row.WinappForm);
        Assert.Contains("--json", row.WinappForm);
        Assert.Contains("winui-devtools", row.WinappForm);
    }

    [Theory]
    [InlineData("docking.list")]
    [InlineData("docking.snapshot")]
    [InlineData("docking.dock")]
    [InlineData("windows.close")]
    [InlineData("shutdown")]
    public void AppBehaviorRows_SayNoToolEquivalent(string key)
    {
        var row = Row(key);
        Assert.False(row.HasEquivalent);
        Assert.StartsWith("no tool equivalent: this is app behavior", row.WinappForm);
    }

    [Theory]
    [InlineData(new[] { "MyApp.csproj" }, "mur devtools [project]", "winapp run . --devtools on")]
    [InlineData(new[] { "MyApp.csproj", "--mcp-port", "9000" }, "mur devtools --print-config / --mcp-port", "not carried over")]
    [InlineData(new[] { "--print-config" }, "mur devtools --print-config / --mcp-port", "not carried over")]
    [InlineData(new[] { "--component", "Counter" }, "mur devtools --component <Name>", "winapp run . --devtools on -- --preview <Name>")]
    [InlineData(new[] { "MyApp.csproj", "--component", "Counter" }, "mur devtools --component <Name>", "--preview <Name>")]
    [InlineData(new[] { "--component", "Counter", "--mcp-port", "9000" }, "mur devtools --component <Name>", "--preview <Name>")]
    [InlineData(new[] { "session" }, "mur devtools session list", "winapp devtools list")]
    [InlineData(new[] { "session", "clean", "--dry-run" }, "mur devtools session clean", "not lockfiles")]
    [InlineData(new[] { "tree", "--pretty" }, "mur devtools tree", "winapp devtools inspect")]
    [InlineData(new[] { "TREE" }, "mur devtools tree", "winapp devtools inspect")]
    [InlineData(new[] { "screenshot", "--launch" }, "mur devtools screenshot", "winapp ui screenshot")]
    [InlineData(new[] { "fire", "App.OnClick" }, "mur devtools fire <Component>.<event>", "winapp devtools invoke-handler <selector> <event>")]
    [InlineData(new[] { "toggle", "x" }, "mur devtools toggle <selector>", "winapp ui invoke <selector> --action toggle")]
    [InlineData(new[] { "logs" }, "mur devtools logs", "winapp devtools logs [--follow]")]
    [InlineData(new[] { "call", "references", "--pretty" }, "mur devtools call references", "(References section)")]
    [InlineData(new[] { "call", "docking.list" }, "mur devtools call docking.list", "no tool equivalent")]
    [InlineData(new[] { "call", "tree" }, "mur devtools call <tool>", "use the matching winapp command with --json")]
    public void RemovedMessage_NamesTheFormAndItsReplacement(string[] args, string murForm, string expected)
    {
        var message = DevtoolsRemoved.BuildRemovedMessage(args);

        var row = DevtoolsRemoved.Resolve(args);
        // Rows with no replacement don't say "use:" before "not needed" / "no tool equivalent".
        Assert.Contains(row.HasEquivalent ? $"`{murForm}` has been removed; use: " : $"`{murForm}` has been removed; ", message);
        Assert.Equal(row.HasEquivalent, message.Contains("; use: ", StringComparison.Ordinal));
        Assert.Contains(expected, message);
        Assert.Contains("winui-devtools", message);
        Assert.All(message.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            line => Assert.StartsWith("[mur devtools] ", line));
    }

    [Fact]
    public void RemovedMessage_ComesFromTheResolvedRow()
    {
        // Differential: a verb gets its own line, not the launch line.
        var launch = DevtoolsRemoved.BuildRemovedMessage(["MyApp.csproj"]);
        var tree = DevtoolsRemoved.BuildRemovedMessage(["tree"]);

        Assert.NotEqual(launch, tree);
        Assert.StartsWith("[mur devtools] `mur devtools tree` has been removed; use: winapp devtools inspect", tree);
        Assert.DoesNotContain("winapp devtools inspect", launch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("tree --help")]
    public void Run_HelpOrNoArgs_PrintsTheWholeTable_AndFails(string argv)
    {
        var args = Split(argv);
        using var err = new StringWriter();

        var exit = DevtoolsRemoved.Run(args, err);

        Assert.Equal(1, exit);
        Assert.Equal(DevtoolsRemoved.BuildMappingTable(), err.ToString());
        foreach (var m in DevtoolsRemoved.Mappings)
            Assert.Contains(m.WinappForm, err.ToString());
    }

    [Theory]
    [InlineData("tree")]
    [InlineData("MyApp.csproj")]
    [InlineData("--print-config --mcp-port 5123")]
    [InlineData("session list")]
    public void Run_AnyCommand_PrintsOnlyTheRemovedMessage_AndFails(string argv)
    {
        var args = Split(argv);
        using var err = new StringWriter();

        var exit = DevtoolsRemoved.Run(args, err);

        Assert.Equal(1, exit);
        Assert.Equal(DevtoolsRemoved.BuildRemovedMessage(args), err.ToString());
    }

    [Theory]
    [InlineData("devtools", "tree", "--pretty")]
    [InlineData("devtools")]
    public async Task MurBinary_RoutesDevtoolsToTheStub_StderrOnly_Exit1(params string[] argv)
    {
        // The real `mur` binary: exit code, stderr text, and nothing on stdout.
        var mur = global::System.IO.Path.Combine(AppContext.BaseDirectory, "mur.exe");
        Assert.True(File.Exists(mur), mur);

        var psi = new global::System.Diagnostics.ProcessStartInfo(mur)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in argv) psi.ArgumentList.Add(a);
        using var proc = global::System.Diagnostics.Process.Start(psi)!;
        // Drain both pipes concurrently: the help table is larger than a pipe buffer.
        var ct = TestContext.Current.CancellationToken;
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        Assert.True(proc.WaitForExit(30_000), "mur did not exit");
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        Assert.Equal(1, proc.ExitCode);
        Assert.Equal(string.Empty, stdout);
        Assert.Equal(DevtoolsRemoved.Run(argv.Skip(1).ToArray(), new StringWriter()), proc.ExitCode);
        var expected = argv.Length == 1
            ? DevtoolsRemoved.BuildMappingTable()
            : DevtoolsRemoved.BuildRemovedMessage(argv.Skip(1).ToArray());
        Assert.Equal(expected.ReplaceLineEndings(), stderr.ReplaceLineEndings());
    }

    private static string[] Split(string argv) =>
        argv.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static DevtoolsVerbMapping Row(string key) =>
        DevtoolsRemoved.Find(key) ?? throw new InvalidOperationException($"no row '{key}'");

    // ─── Removed mur devtools verbs still name their winapp command ────

    [Theory]
    [InlineData("properties")]
    [InlineData("set-property")]
    [InlineData("resources")]
    [InlineData("set-resource")]
    [InlineData("styles")]
    [InlineData("ancestors")]
    public void RemovedPropertyVerbs_MapToAWinappCommand(string verb)
    {
        var row = DevtoolsRemoved.Find(verb);
        Assert.NotNull(row);
        Assert.True(row!.HasEquivalent, verb);
        Assert.StartsWith("winapp devtools ", row.WinappForm);
    }
}
