using System.Text.RegularExpressions;
using Microsoft.UI.Reactor.Cli.Pack;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Microsoft.UI.Reactor.Tests.Docs;

public sealed class CiBuildReproductionTests
{
    private const string WorkflowPath = ".github/workflows/ci.yml";

    [Theory]
    [InlineData(".github/PULL_REQUEST_TEMPLATE.md")]
    [InlineData("CONTRIBUTING.md")]
    [InlineData("AGENTS.md")]
    public void Documented_commands_match_the_build_solution_job(string path)
    {
        var root = RepoRootFinder.FindRepoRoot();
        Assert.NotNull(root);

        var expected = ReadCiCommands(root).Select(NormalizeCommand).ToArray();
        var markdown = File.ReadAllText(Path.Combine(root, path)).ReplaceLineEndings("\n");
        var documented = path == "CONTRIBUTING.md"
            ? ReadFencedCommands(markdown)
            : ReadInlineCommands(markdown, path);

        Assert.Equal(expected, documented.Select(NormalizeCommand).ToArray());
    }

    private static string[] ReadCiCommands(string root)
    {
        using var reader = File.OpenText(Path.Combine(root, WorkflowPath));
        var workflow = new YamlStream();
        workflow.Load(reader);

        var workflowRoot = Assert.IsType<YamlMappingNode>(workflow.Documents.Single().RootNode);
        var jobs = Assert.IsType<YamlMappingNode>(workflowRoot["jobs"]);
        var buildJob = Assert.IsType<YamlMappingNode>(jobs["build-solution"]);
        var steps = Assert.IsType<YamlSequenceNode>(buildJob["steps"])
            .Children.Select(step => Assert.IsType<YamlMappingNode>(step))
            .ToArray();
        var buildSteps = steps
            .Where(step => StepName(step) is "Restore" or "Build")
            .ToArray();

        Assert.Equal(["Restore", "Build"], buildSteps.Select(StepName).ToArray());

        var interveningRuns = steps
            .SkipWhile(step => StepName(step) != "Restore")
            .Skip(1)
            .TakeWhile(step => StepName(step) != "Build")
            .Where(step => step.Children.ContainsKey("run"))
            .Select(step => StepName(step) ?? "(unnamed step)")
            .ToArray();
        Assert.True(interveningRuns.Length == 0,
            $"Unexpected run step(s) between Restore and Build in {WorkflowPath} ({string.Join(", ", interveningRuns)}).");

        return buildSteps.Select(step =>
        {
            var run = Assert.IsType<YamlScalarNode>(step["run"]).Value?.Trim();
            Assert.True(!string.IsNullOrEmpty(run) && !run.Contains('\r') && !run.Contains('\n'),
                $"Expected a single command in {WorkflowPath}, build-solution → {StepName(step)}.");
            return run!;
        }).ToArray();
    }

    private static string? StepName(YamlMappingNode step) =>
        step.Children.TryGetValue("name", out var name) ? (name as YamlScalarNode)?.Value : null;

    private static IEnumerable<string> ReadInlineCommands(string markdown, string path)
    {
        // The PR template and AGENTS put the instructions in the Build solution bullet.
        // The prose that follows also mentions a one-shot build as a counterexample.
        var bullet = Regex.Matches(markdown, @"(?m)^- [^\n]*(?:\n(?![-+*] |#{1,6} |```|~~~|-->|[ \t]*$)[^\n]+)*")
            .FirstOrDefault(item => Regex.IsMatch(item.Value, """`Build\s+solution`"""));
        Assert.True(bullet is not null, $"Could not find the Build solution bullet in {path}.");

        var spans = Regex.Matches(bullet.Value, """`([^`]+)`""")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        // The first two dotnet spans bound the recipe, including any commands between them.
        var boundaries = spans.Index()
            .Where(span => Regex.IsMatch(span.Item, @"^\s*dotnet\s+\S"))
            .Take(2)
            .Select(span => span.Index)
            .ToArray();
        Assert.True(boundaries.Length == 2, $"Expected two dotnet commands in the Build solution bullet in {path}.");
        return spans[boundaries[0]..(boundaries[1] + 1)];
    }

    private static IEnumerable<string> ReadFencedCommands(string markdown)
    {
        var section = Regex.Match(markdown, @"(?m)^### Reproducing the CI build gate[ \t]*\n([\s\S]*?)(?=^#{1,3} |\z)");
        var block = Regex.Match(section.Groups[1].Value, @"(?m)^```[^\n]*\n([\s\S]*?)^```[ \t]*$");
        Assert.True(block.Success, "Could not find the CI build reproduction code block in CONTRIBUTING.md.");
        return block.Groups[1].Value.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith('#'));
    }

    private static string NormalizeCommand(string command) =>
        // Keep quoted arguments intact. The docs use the short configuration switch.
        Regex.Replace(command.Trim(), """
            "[^"]*"|'[^']*'|\s+|(?<!\S)--configuration(?=\s|$)
            """,
            match => match.Value switch
            {
                "--configuration" => "-c",
                var whitespace when char.IsWhiteSpace(whitespace[0]) => " ",
                var argument => argument,
            });
}
