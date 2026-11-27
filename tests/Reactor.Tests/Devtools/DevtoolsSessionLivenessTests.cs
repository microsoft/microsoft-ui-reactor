using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

[Collection("ConsoleTests")] // Start and AnnounceReady write to Console.Out.
public sealed class DevtoolsSessionLivenessTests
{
    [Fact]
    public void HttpSession_WithAnnouncedToken_IsActiveAndPreservesLockfile()
    {
        var projectId = Path.Combine(Path.GetTempPath(), $"reactor-liveness-{Guid.NewGuid():N}.csproj");
        var path = LockfileRegistry.PathFor(projectId);
        // GET /mcp is served on the listener thread pool and needs no WinUI dispatcher.
        using var server = new DevtoolsMcpServer(null!, null!, projectIdentifier: projectId);
        server.Start();
        server.AnnounceReady();
        var originalLockfile = File.ReadAllText(path);

        Assert.True(DevtoolsMcpServer.IsAnotherSessionActive(projectId, out var existing));
        Assert.NotNull(existing);
        Assert.Equal(Environment.ProcessId, existing.Pid);
        Assert.Equal($"http://127.0.0.1:{server.Port}/mcp", existing.Endpoint);
        Assert.True(string.Equals(originalLockfile, File.ReadAllText(path), StringComparison.Ordinal),
            "The active session's lockfile should be preserved.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrong-token")]
    public void HttpSession_WithoutValidToken_IsStale(string token)
    {
        var projectId = Path.Combine(Path.GetTempPath(), $"reactor-liveness-{Guid.NewGuid():N}.csproj");
        var path = LockfileRegistry.PathFor(projectId);
        using var server = new DevtoolsMcpServer(null!, null!, projectIdentifier: projectId);
        server.Start();
        server.AnnounceReady();
        Assert.True(LockfileRegistry.TryRead(path, out var entry));
        Assert.NotNull(entry);
        entry.Token = token;
        LockfileRegistry.Write(path, entry);

        Assert.False(DevtoolsMcpServer.IsAnotherSessionActive(projectId, out var existing));
        Assert.Null(existing);
        Assert.False(File.Exists(path));
    }
}
