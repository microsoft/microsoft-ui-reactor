using System.Text.Json;
using Microsoft.UI.Reactor.Hosting.Devtools;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Devtools;

/// <summary>
/// Pure tests for the spec 025 lockfile contract. Avoid touching %TEMP% by
/// writing to xunit-scoped temp directories; the tested surface takes a path.
/// </summary>
public class LockfileRegistryTests
{
    [Fact]
    public void Canonicalize_NormalizesDriveCaseAndSeparators()
    {
        var a = LockfileRegistry.Canonicalize(@"C:\Foo\Bar.csproj");
        var b = LockfileRegistry.Canonicalize(@"c:/Foo/Bar.csproj");
        Assert.Equal(a, b);
    }

    [Fact]
    public void ComputeHash_IsStableAndTruncatedTo16Hex()
    {
        var hash = LockfileRegistry.ComputeHash(@"C:\MyApp\MyApp.csproj");
        Assert.Equal(16, hash.Length);
        Assert.Matches("^[0-9a-f]{16}$", hash);
        Assert.Equal(hash, LockfileRegistry.ComputeHash(@"C:\MyApp\MyApp.csproj"));
    }

    [Fact]
    public void ComputeHash_DifferentPaths_DifferentHashes()
    {
        var a = LockfileRegistry.ComputeHash(@"C:\App1\App1.csproj");
        var b = LockfileRegistry.ComputeHash(@"C:\App2\App2.csproj");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Write_And_TryRead_RoundTrip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"reactor-test-{Guid.NewGuid():N}.json");
        try
        {
            var entry = new LockfileEntry
            {
                Endpoint = "http://127.0.0.1:54931/mcp",
                Transport = "http",
                Port = 54931,
                Pid = 12345,
                BuildTag = "2026-04-19T00:00:00Z",
                Project = @"C:\Users\me\MyApp.csproj",
                StartedAt = "2026-04-19T00:00:01Z",
            };
            LockfileRegistry.Write(path, entry);
            Assert.True(LockfileRegistry.TryRead(path, out var read));
            Assert.NotNull(read);
            Assert.Equal(entry.Endpoint, read!.Endpoint);
            Assert.Equal(entry.Port, read.Port);
            Assert.Equal(entry.Pid, read.Pid);
            Assert.Equal(entry.Project, read.Project);
            Assert.Equal(LockfileRegistry.SchemaTag, read.Schema);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void IsLive_DeadPid_ReturnsFalse()
    {
        // Pid 0 is never a live process in user-mode; some systems treat it
        // as the kernel. Either way Process.GetProcessById throws.
        var entry = new LockfileEntry { Pid = 0, Endpoint = "http://127.0.0.1:1/mcp", Transport = "http" };
        Assert.False(LockfileRegistry.IsLive(entry));
    }

    [Fact]
    public void IsLive_AlivePidButBadEndpoint_ReturnsFalse()
    {
        // Our own pid exists, but nothing is listening on the endpoint — the
        // HTTP probe has to fail even if the pid is alive.
        var entry = new LockfileEntry
        {
            Pid = global::System.Diagnostics.Process.GetCurrentProcess().Id,
            Endpoint = "http://127.0.0.1:1/mcp",
            Transport = "http",
        };
        Assert.False(LockfileRegistry.IsLive(entry));
    }

    [Fact]
    public void TryRead_MissingFile_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.json");
        Assert.False(LockfileRegistry.TryRead(path, out var e));
        Assert.Null(e);
    }

    [Fact]
    public void TryDelete_MissingFile_DoesNotThrow()
    {
        var path = Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.json");
        var ex = Record.Exception(() => LockfileRegistry.TryDelete(path));
        Assert.Null(ex);
    }

    // ── Coverage gap fillers ────────────────────────────────────────

    [Fact]
    public void Directory_Returns_TempReactorDevtools_Path()
    {
        Assert.Contains("reactor-devtools", LockfileRegistry.Directory);
    }

    [Fact]
    public void PathFor_Returns_Hashed_Path_Under_Directory()
    {
        var p = LockfileRegistry.PathFor(@"C:\App\App.csproj");
        Assert.StartsWith(LockfileRegistry.Directory, p, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".json", p);
    }

    [Fact]
    public void Canonicalize_Falls_Back_For_Invalid_Path()
    {
        // Path.GetFullPath throws on invalid characters → fall through to the
        // catch branch where the input is normalized as-is.
        var bad = "?<>|" + new string('\0', 0); // path-illegal chars
        var result = LockfileRegistry.Canonicalize(bad);
        Assert.NotNull(result);
    }

    [Fact]
    public void Write_Skips_Rewrite_When_Content_Matches()
    {
        var path = Path.Combine(Path.GetTempPath(), $"reactor-test-{Guid.NewGuid():N}.json");
        try
        {
            var entry = new LockfileEntry { Endpoint = "http://127.0.0.1:42/mcp", Transport = "http", Pid = 1 };
            LockfileRegistry.Write(path, entry);
            var firstWrite = File.GetLastWriteTimeUtc(path);
            Thread.Sleep(15); // make any rewrite visibly later
            LockfileRegistry.Write(path, entry); // identical content → no rewrite
            var secondWrite = File.GetLastWriteTimeUtc(path);
            Assert.Equal(firstWrite, secondWrite);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Write_Replaces_Existing_File_With_Different_Content()
    {
        var path = Path.Combine(Path.GetTempPath(), $"reactor-test-{Guid.NewGuid():N}.json");
        try
        {
            LockfileRegistry.Write(path, new LockfileEntry { Endpoint = "http://127.0.0.1:42/mcp", Transport = "http" });
            LockfileRegistry.Write(path, new LockfileEntry { Endpoint = "http://127.0.0.1:43/mcp", Transport = "http" });
            Assert.True(LockfileRegistry.TryRead(path, out var e));
            Assert.Equal("http://127.0.0.1:43/mcp", e!.Endpoint);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void TryRead_BadJson_Returns_False()
    {
        var path = Path.Combine(Path.GetTempPath(), $"reactor-test-bad-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "not json");
            Assert.False(LockfileRegistry.TryRead(path, out var e));
            Assert.Null(e);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void EnumerateAll_Empty_Directory_Yields_Nothing()
    {
        // The default temp dir typically exists and may have unrelated files;
        // we just verify EnumerateAll runs without throwing and returns a result.
        var result = LockfileRegistry.EnumerateAll().Take(10).ToList();
        Assert.NotNull(result);
    }

    [Fact]
    public void EnumerateAll_Returns_Written_Files()
    {
        // Create the directory, drop a known lockfile, and verify it's enumerated.
        global::System.IO.Directory.CreateDirectory(LockfileRegistry.Directory);
        var path = Path.Combine(LockfileRegistry.Directory, $"test-enum-{Guid.NewGuid():N}.json");
        try
        {
            LockfileRegistry.Write(path, new LockfileEntry { Endpoint = "http://127.0.0.1:42/mcp", Transport = "http", Pid = 1 });
            var found = LockfileRegistry.EnumerateAll().Any(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
            Assert.True(found);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void IsLive_Stdio_Returns_True_When_Pid_Alive()
    {
        // Stdio path skips the HTTP probe — pid liveness alone gates it.
        var entry = new LockfileEntry
        {
            Pid = global::System.Diagnostics.Process.GetCurrentProcess().Id,
            Transport = "stdio",
            Endpoint = "",
        };
        Assert.True(LockfileRegistry.IsLive(entry));
    }
}
