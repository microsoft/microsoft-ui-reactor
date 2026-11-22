using System.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// Issue #1664. A file-based app saved as <c>reactor.cs</c> is the <c>reactor</c> assembly, which
/// shadows <c>Reactor.dll</c> at run time (simple names are case-insensitive) and crashes in its
/// module initializer. <c>build/Reactor.targets</c> turns that into a build error. Evaluates the
/// real targets file in a throwaway project with a synthetic <c>ReferencePath</c>.
/// </summary>
public sealed class AssemblyNameCollisionTargetsTests : IDisposable
{
    private readonly string _dir = global::System.IO.Path.Join(
        global::System.IO.Path.GetTempPath(), $"reactor-asmname-targets-{Guid.NewGuid():N}");

    public AssemblyNameCollisionTargetsTests() => global::System.IO.Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            global::System.IO.Directory.Delete(_dir, recursive: true);
        }
        catch (global::System.IO.IOException)
        {
            // Best-effort temp cleanup.
        }
    }

    private static string TargetsPath()
    {
        var dir = new global::System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !global::System.IO.File.Exists(global::System.IO.Path.Join(dir.FullName, "Reactor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return global::System.IO.Path.Join(dir!.FullName, "src", "Reactor", "build", "Reactor.targets");
    }

    private (int ExitCode, string Output) Build(string assemblyName)
    {
        var project = $"""
            <Project>
              <PropertyGroup><AssemblyName>{assemblyName}</AssemblyName></PropertyGroup>
              <ItemGroup>
                <ReferencePath Include="C:\nuget\microsoft.ui.reactor\lib\net10.0-windows10.0.22621.0\Reactor.dll" />
                <ReferencePath Include="C:\nuget\other\lib\net10.0\Other.Library.dll" />
              </ItemGroup>
              <Import Project="{TargetsPath()}" />
            </Project>
            """;
        var path = global::System.IO.Path.Join(_dir, $"p{Guid.NewGuid():N}.proj");
        global::System.IO.File.WriteAllText(path, project);

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _dir,
        };
        psi.ArgumentList.Add("msbuild");
        psi.ArgumentList.Add(path);
        psi.ArgumentList.Add("-nologo");
        psi.ArgumentList.Add("-t:_ReactorCheckAssemblyNameCollision");

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout + stderr);
    }

    [Theory]
    [InlineData("reactor")]
    [InlineData("Reactor")]
    [InlineData("REACTOR")]
    public void AppNamedLikeReactorDll_FailsWithReactorBuild001(string assemblyName)
    {
        var (exitCode, output) = Build(assemblyName);
        Assert.NotEqual(0, exitCode);
        Assert.Contains("REACTOR_BUILD_001", output, StringComparison.Ordinal);
        Assert.Contains("'Reactor.dll'", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AppNamedLikeAnyOtherReference_Fails()
    {
        var (exitCode, output) = Build("other.library");
        Assert.NotEqual(0, exitCode);
        Assert.Contains("'Other.Library.dll'", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MyApp")]
    [InlineData("ReactorApp")]
    [InlineData("Reactor.Sample")]
    public void NonCollidingName_Builds(string assemblyName)
    {
        var (exitCode, output) = Build(assemblyName);
        Assert.True(exitCode == 0, output);
        Assert.DoesNotContain("REACTOR_BUILD_001", output, StringComparison.Ordinal);
    }
}
