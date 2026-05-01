using System.Text;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Security-regression tests for spec 025 lockfile hardening (TASK-001 / 005 /
/// 031 / 032). The bearer-token, schema, endpoint, and size validations live in
/// <see cref="LockfileRegistry"/> on the server side; the CLI side mirrors them
/// in <c>LockfileReader</c> (see Reactor.Cli tests).
/// </summary>
public class LockfileSecurityTests
{
    [Fact]
    public void TryRead_RejectsOversizedFile()
    {
        // TASK-005: cap lockfile size to 8 KiB before parsing.
        var path = Path.Combine(Path.GetTempPath(), $"reactor-test-big-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, new string('x', LockfileRegistry.MaxLockfileBytes + 1));
            Assert.False(LockfileRegistry.TryRead(path, out var e));
            Assert.Null(e);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void TryRead_RejectsWrongSchemaTag()
    {
        // TASK-031: enforce the schema constant. A planted lockfile with the
        // wrong schema field must be rejected, not parsed and trusted.
        var path = Path.Combine(Path.GetTempPath(), $"reactor-test-schema-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path,
                "{\"schema\":\"someone-elses-format/1\",\"endpoint\":\"http://127.0.0.1:55555/mcp\",\"transport\":\"http\",\"port\":55555,\"pid\":1}");
            Assert.False(LockfileRegistry.TryRead(path, out var e));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Theory]
    [InlineData("http://attacker.example.com/mcp")]                 // off-machine
    [InlineData("https://127.0.0.1:55555/mcp")]                     // wrong scheme
    [InlineData("http://user:pwd@127.0.0.1:55555/mcp")]             // userinfo
    [InlineData("http://[::1]:55555/mcp")]                          // raw v6 brackets accepted as ::1
    public void TryRead_RejectsNonLoopbackEndpoint(string endpoint)
    {
        // TASK-031: only loopback HTTP without userinfo is acceptable.
        var path = Path.Combine(Path.GetTempPath(), $"reactor-test-ep-{Guid.NewGuid():N}.json");
        try
        {
            var json = $"{{\"schema\":\"{LockfileRegistry.SchemaTag}\",\"endpoint\":\"{endpoint}\",\"transport\":\"http\",\"port\":55555,\"pid\":1}}";
            File.WriteAllText(path, json);
            var read = LockfileRegistry.TryRead(path, out var e);
            // [::1] is an accepted loopback form and should pass; everything else rejects.
            if (endpoint.Contains("[::1]"))
            {
                Assert.True(read);
                Assert.NotNull(e);
            }
            else
            {
                Assert.False(read);
                Assert.Null(e);
            }
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void ReadCappedBody_ThrowsOnOversize()
    {
        // TASK-006: request body must be capped at MaxRequestBodyBytes.
        var oversized = new MemoryStream(new byte[DevtoolsMcpServer.MaxRequestBodyBytes + 1]);
        Assert.Throws<InvalidDataException>(() =>
            DevtoolsMcpServer.ReadCappedBody(oversized, Encoding.UTF8, DevtoolsMcpServer.MaxRequestBodyBytes));
    }

    [Fact]
    public void ReadCappedBody_AcceptsExactLimit()
    {
        // Boundary check: exactly cap bytes is fine.
        var atLimit = new MemoryStream(new byte[DevtoolsMcpServer.MaxRequestBodyBytes]);
        var s = DevtoolsMcpServer.ReadCappedBody(atLimit, Encoding.UTF8, DevtoolsMcpServer.MaxRequestBodyBytes);
        Assert.Equal(DevtoolsMcpServer.MaxRequestBodyBytes, s.Length);
    }

    [Fact]
    public void IsLoopbackHttpEndpoint_RejectsNonLoopback()
    {
        Assert.True(LockfileRegistry.IsLoopbackHttpEndpoint("http://127.0.0.1:1234/mcp"));
        Assert.True(LockfileRegistry.IsLoopbackHttpEndpoint("http://localhost:1234/mcp"));
        Assert.True(LockfileRegistry.IsLoopbackHttpEndpoint("http://[::1]:1234/mcp"));
        Assert.False(LockfileRegistry.IsLoopbackHttpEndpoint("https://127.0.0.1:1234/mcp"));
        Assert.False(LockfileRegistry.IsLoopbackHttpEndpoint("http://attacker.com/mcp"));
        Assert.False(LockfileRegistry.IsLoopbackHttpEndpoint("http://1.1.1.1:1234/mcp"));
        Assert.False(LockfileRegistry.IsLoopbackHttpEndpoint("http://x:y@127.0.0.1:1234/mcp"));
        Assert.False(LockfileRegistry.IsLoopbackHttpEndpoint(""));
        Assert.False(LockfileRegistry.IsLoopbackHttpEndpoint("not a url"));
    }
}
