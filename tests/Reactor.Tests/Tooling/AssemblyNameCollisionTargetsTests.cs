using System.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// Issue #1664. An app whose assembly name matches a reference (a file-based app saved as
/// <c>microsoft.ui.reactor.cs</c> is the <c>microsoft.ui.reactor</c> assembly) shadows that
/// reference at run time (simple names are case-insensitive) and crashes in its module
/// initializer. <c>build/Reactor.targets</c> turns that into a build error. Evaluates the real
/// targets file in a throwaway project with a synthetic <c>ReferencePath</c>.
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

    private const string References = """
        <ReferencePath Include="C:\nuget\microsoft.ui.reactor\lib\net10.0-windows10.0.22621.0\Microsoft.UI.Reactor.dll" />
        <ReferencePath Include="C:\nuget\other\lib\net10.0\Other.Library.dll" />
        """;

    private (int ExitCode, string Output) Build(string assemblyName, string extraProperties = "")
    {
        var project = $"""
            <Project>
              <PropertyGroup><AssemblyName>{assemblyName}</AssemblyName>{extraProperties}</PropertyGroup>
              <ItemGroup>
                {References}
              </ItemGroup>
              <Import Project="{TargetsPath()}" />
            </Project>
            """;
        return Run(project, "_ReactorCheckAssemblyNameCollision");
    }

    private (int ExitCode, string Output) Run(string project, string target)
    {
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
        psi.ArgumentList.Add($"-t:{target}");

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout + stderr);
    }

    [Theory]
    [InlineData("microsoft.ui.reactor")]
    [InlineData("Microsoft.UI.Reactor")]
    [InlineData("MICROSOFT.UI.REACTOR")]
    public void AppNamedLikeReactorDll_FailsWithReactorBuild001(string assemblyName)
    {
        var (exitCode, output) = Build(assemblyName);
        Assert.NotEqual(0, exitCode);
        Assert.Contains("REACTOR_BUILD_001", output, StringComparison.Ordinal);
        Assert.Contains("'Microsoft.UI.Reactor.dll'", output, StringComparison.Ordinal);
    }

    [Fact]
    public void AppNamedLikeAnyOtherReference_Fails()
    {
        var (exitCode, output) = Build("other.library");
        Assert.NotEqual(0, exitCode);
        Assert.Contains("'Other.Library.dll'", output, StringComparison.Ordinal);
    }

    // "reactor" is the #1664 name. It collided with the pre-rename Reactor.dll and is safe now.
    [Theory]
    [InlineData("MyApp")]
    [InlineData("reactor")]
    [InlineData("Microsoft.UI")]
    [InlineData("Reactor.Sample")]
    public void NonCollidingName_Builds(string assemblyName)
    {
        var (exitCode, output) = Build(assemblyName);
        Assert.True(exitCode == 0, output);
        Assert.DoesNotContain("REACTOR_BUILD_001", output, StringComparison.Ordinal);
    }

    [Fact]
    public void OptOutProperty_SkipsTheCheck()
    {
        var (exitCode, output) = Build("microsoft.ui.reactor", "<ReactorSkipAssemblyNameCollisionCheck>true</ReactorSkipAssemblyNameCollisionCheck>");
        Assert.True(exitCode == 0, output);
        Assert.DoesNotContain("REACTOR_BUILD_001", output, StringComparison.Ordinal);
    }

    // Builds the normal compile entry point, not the check target, so this fails if the target
    // loses its hook into the build (AfterTargets/BeforeTargets).
    [Fact]
    public void CompileOfCollidingApp_RunsTheCheckBeforeCoreCompile()
    {
        var project = $"""
            <Project>
              <PropertyGroup><AssemblyName>microsoft.ui.reactor</AssemblyName></PropertyGroup>
              <Target Name="ResolveAssemblyReferences">
                <ItemGroup>
                  {References}
                </ItemGroup>
              </Target>
              <Target Name="CoreCompile" DependsOnTargets="ResolveAssemblyReferences">
                <Message Importance="high" Text="CORE_COMPILE_RAN" />
              </Target>
              <Import Project="{TargetsPath()}" />
            </Project>
            """;

        var (exitCode, output) = Run(project, "CoreCompile");
        Assert.NotEqual(0, exitCode);
        Assert.Contains("REACTOR_BUILD_001", output, StringComparison.Ordinal);
        Assert.DoesNotContain("CORE_COMPILE_RAN", output, StringComparison.Ordinal);
    }
}

public sealed class ProjectNameRulesTests
{
    [Theory]
    [InlineData("Microsoft.UI.Reactor")]
    [InlineData("microsoft.ui.reactor")]
    [InlineData("MICROSOFT.UI.REACTOR.ADVANCED")]
    [InlineData("Reactor.Wrappers.Abstractions")]
    [InlineData("Microsoft.UI.Reactor.Devtools")]
    public void ReactorAssemblyName_IsAnAssemblyCollision(string name) =>
        Assert.Equal(Microsoft.UI.Reactor.Cli.ProjectNameRules.Collision.AssemblyName, Microsoft.UI.Reactor.Cli.ProjectNameRules.Check(name));

    // The shipped csprojs are the source of truth; the reserved list must keep up with them.
    [Theory]
    [InlineData("Reactor", "Reactor.csproj")]
    [InlineData("Reactor.Advanced", "Reactor.Advanced.csproj")]
    [InlineData("Reactor.Devtools", "Reactor.Devtools.csproj")]
    [InlineData("Reactor.Wrappers.Abstractions", "Reactor.Wrappers.Abstractions.csproj")]
    public void ShippedAssemblyName_IsReserved(string projectDir, string projectFile)
    {
        var dir = new global::System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !global::System.IO.File.Exists(global::System.IO.Path.Join(dir.FullName, "Reactor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var csproj = global::System.Xml.Linq.XDocument.Load(global::System.IO.Path.Join(dir!.FullName, "src", projectDir, projectFile));
        var assemblyName = csproj.Descendants("AssemblyName").Select(e => e.Value.Trim()).First(v => v.Length > 0);

        Assert.Equal(Microsoft.UI.Reactor.Cli.ProjectNameRules.Collision.AssemblyName, Microsoft.UI.Reactor.Cli.ProjectNameRules.Check(assemblyName));
    }

    // The generated .sln already lists ..\src\Reactor\Reactor.csproj as project "Reactor".
    [Theory]
    [InlineData("Reactor")]
    [InlineData("reactor")]
    public void GeneratedSolutionProjectName_IsASolutionCollision(string name) =>
        Assert.Equal(Microsoft.UI.Reactor.Cli.ProjectNameRules.Collision.SolutionProject, Microsoft.UI.Reactor.Cli.ProjectNameRules.Check(name));

    // Reactor.Advanced was an assembly name before the shipped names matched the package ids.
    [Theory]
    [InlineData("MyApp")]
    [InlineData("ReactorApp")]
    [InlineData("Reactor.Sample")]
    [InlineData("Reactor.Advanced")]
    public void OtherName_IsAllowed(string name) =>
        Assert.Equal(Microsoft.UI.Reactor.Cli.ProjectNameRules.Collision.None, Microsoft.UI.Reactor.Cli.ProjectNameRules.Check(name));
}