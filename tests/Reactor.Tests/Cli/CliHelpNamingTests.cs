using System;
using System.IO;
using Microsoft.UI.Reactor.Cli.Docs;
using Microsoft.UI.Reactor.Cli.Loc;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Cli;

/// <summary>
/// Guards the CLI's user-facing help text against tool-name drift.
///
/// The tool shipped for a while printing "duct loc extract — ..." in response to
/// `mur loc extract --help`, naming a command that does not exist. The Duct →
/// Reactor rename missed these strings and nothing asserted on help output, so
/// the drift was invisible to CI.
/// </summary>
[Collection("ConsoleTests")]
public class CliHelpNamingTests
{
    /// <summary>
    /// The shipped command name, read from the CLI assembly itself
    /// (<c>AssemblyName</c> is <c>mur</c>, the same value as <c>ToolCommandName</c>).
    /// Deriving it instead of hardcoding "mur" makes this a differential check:
    /// it fails if the help text drifts from the binary *or* the binary is renamed
    /// without updating the help text.
    /// </summary>
    private static readonly string Tool = typeof(LocCommand).Assembly.GetName().Name!;

    [Theory]
    [InlineData("")]
    [InlineData("extract")]
    [InlineData("translate")]
    [InlineData("validate")]
    [InlineData("status")]
    [InlineData("prune")]
    public void LocHelp_UsageLineNamesShippedToolCommand(string subcommand)
    {
        string[] args = subcommand.Length == 0 ? ["--help"] : [subcommand, "--help"];

        var (exitCode, stdout, _) = Capture(() => LocCommand.Run(args));

        var expected = subcommand.Length == 0
            ? $"Usage: {Tool} loc "
            : $"Usage: {Tool} loc {subcommand} ";

        Assert.Equal(0, exitCode);
        Assert.Contains(expected, stdout);
    }

    [Fact]
    public void DocsHelp_UsageLineNamesShippedToolCommand()
    {
        var (exitCode, stdout, _) = Capture(() => DocsCommand.Run(["--help"]));

        Assert.Equal(0, exitCode);
        Assert.Contains($"Usage: {Tool} docs ", stdout);
    }

    [Fact]
    public void UnknownLocSubcommand_ErrorNamesShippedToolCommand()
    {
        var (exitCode, _, stderr) = Capture(() => LocCommand.Run(["no-such-subcommand"]));

        Assert.Equal(1, exitCode);
        Assert.Contains($"Unknown command: {Tool} loc no-such-subcommand", stderr);
    }

    [Fact]
    public void UnknownDocsSubcommand_ErrorNamesShippedToolCommand()
    {
        var (exitCode, _, stderr) = Capture(() => DocsCommand.Run(["no-such-subcommand"]));

        Assert.Equal(1, exitCode);
        Assert.Contains($"Unknown command: {Tool} docs no-such-subcommand", stderr);
    }

    /// <summary>
    /// The specific regression: no help or error surface may name the retired
    /// <c>duct</c> / <c>duct-loc</c> command. Matched as exact command prefixes so
    /// ordinary words containing "duct" in future help text can't trip this.
    /// </summary>
    [Fact]
    public void NoHelpSurface_NamesTheRetiredDuctCommand()
    {
        var surfaces = new (string Label, Func<int> Invoke)[]
        {
            ("loc --help", () => LocCommand.Run(["--help"])),
            ("loc extract --help", () => LocCommand.Run(["extract", "--help"])),
            ("loc translate --help", () => LocCommand.Run(["translate", "--help"])),
            ("loc validate --help", () => LocCommand.Run(["validate", "--help"])),
            ("loc status --help", () => LocCommand.Run(["status", "--help"])),
            ("loc prune --help", () => LocCommand.Run(["prune", "--help"])),
            ("loc <unknown>", () => LocCommand.Run(["no-such-subcommand"])),
            ("docs --help", () => DocsCommand.Run(["--help"])),
            ("docs <unknown>", () => DocsCommand.Run(["no-such-subcommand"])),
        };

        string[] retired = ["duct loc", "duct docs", "duct-loc"];

        foreach (var (label, invoke) in surfaces)
        {
            var (_, stdout, stderr) = Capture(invoke);
            var combined = stdout + stderr;

            foreach (var name in retired)
            {
                Assert.False(
                    combined.Contains(name, StringComparison.OrdinalIgnoreCase),
                    $"`{Tool} {label}` output names the retired command '{name}'.");
            }
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Capture(Func<int> action)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        // Disposed at method exit, i.e. after the finally below has already
        // restored Console — so the console never holds a disposed writer.
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
