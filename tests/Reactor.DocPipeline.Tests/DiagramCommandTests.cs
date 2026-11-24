using System.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Cli.Docs.Tests;

public sealed class DiagramCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("reactor-diagram-command-").FullName;

    public DiagramCommandTests() =>
        File.WriteAllText(Path.Join(_root, "Reactor.slnx"), "<Solution />");

    public void Dispose() => FixtureCleanup.DeleteTree(_root);

    [Fact]
    public async Task Render_reports_drive_less_rooted_topic_without_writing_images()
    {
        const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>";
        var topic = Directory.CreateDirectory(Path.Join(_root, "docs", "_pipeline", "diagrams", "arch"));
        File.WriteAllText(Path.Join(topic.FullName, "overview.svg"), svg);
        var images = Directory.CreateDirectory(Path.Join(_root, "docs", "guide", "images"));

        var rootedTopic = Path.DirectorySeparatorChar + "arch";
        var rejected = await RunAsync("render-diagrams", "--topic", rootedTopic);

        Assert.Equal(1, rejected.ExitCode);
        Assert.StartsWith("Error: Diagram topic", rejected.Stderr, StringComparison.Ordinal);
        Assert.Contains("must be a relative path", rejected.Stderr, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(images.FullName));

        var accepted = await RunAsync("render-diagrams", "--topic", "arch");

        Assert.Equal(0, accepted.ExitCode);
        Assert.Empty(accepted.Stderr);
        Assert.Equal(svg, File.ReadAllText(Path.Join(images.FullName, "arch", "overview.svg")));
    }

    [Fact]
    public async Task New_diagram_reports_absolute_topic_without_creating_directory()
    {
        var outsideTopic = Path.Join(_root, "outside");
        var result = await RunAsync("new-diagram", outsideTopic, "overview");

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("Error: Diagram topic", result.Stderr, StringComparison.Ordinal);
        Assert.Contains("must be a relative path", result.Stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outsideTopic));
        Assert.False(Directory.Exists(Path.Join(_root, "docs", "_pipeline", "diagrams")));
    }

    [Theory]
    [InlineData(false, "would escape")]
    [InlineData(true, "must be a relative path")]
    public async Task New_diagram_rejects_outside_id_before_creating_topic(bool rooted, string diagnostic)
    {
        var diagrams = Path.Join(_root, "docs", "_pipeline", "diagrams");
        var topicDirectory = Path.Join(diagrams, "arch");
        var outsideId = Path.Join(rooted ? _root : diagrams, "outside");
        var id = rooted ? outsideId : Path.GetRelativePath(topicDirectory, outsideId);

        var result = await RunAsync("new-diagram", "arch", id);

        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("Error: Diagram id", result.Stderr, StringComparison.Ordinal);
        Assert.Contains(diagnostic, result.Stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(outsideId + ".mmd"));
        Assert.False(Directory.Exists(topicDirectory));
    }

    private async Task<(int ExitCode, string Stderr)> RunAsync(params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = _root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { Path.Join(AppContext.BaseDirectory, "Reactor.DocPipeline.dll") },
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)!;
        var cancellationToken = TestContext.Current.CancellationToken;
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        await stdout;
        return (process.ExitCode, await stderr);
    }
}
