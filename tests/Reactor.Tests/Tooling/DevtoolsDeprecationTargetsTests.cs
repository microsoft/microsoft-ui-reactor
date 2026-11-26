using System.Diagnostics;
using System.Xml.Linq;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// <c>REACTOR_DEVTOOLS_001</c> from <c>src/Reactor.Devtools/build/Microsoft.UI.Reactor.Devtools.targets</c>.
/// In-repo projects reference the package by ProjectReference, so the targets file is never
/// imported by the repo's own builds; these tests run the real file in a throwaway project with
/// <c>dotnet msbuild</c>, the way NuGet imports it after a PackageReference consumer's body.
/// </summary>
public sealed class DevtoolsDeprecationTargetsTests : IDisposable
{
    private const string Code = "REACTOR_DEVTOOLS_001";

    private readonly string _dir = global::System.IO.Path.Join(
        global::System.IO.Path.GetTempPath(), $"reactor-devtools-targets-{Guid.NewGuid():N}");

    public DevtoolsDeprecationTargetsTests() => global::System.IO.Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            global::System.IO.Directory.Delete(_dir, recursive: true);
        }
        catch (global::System.IO.IOException)
        {
            // Best-effort temp cleanup: a file still held by the build must not fail the test.
        }
    }

    private static string RepoRoot()
    {
        var dir = new global::System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !global::System.IO.File.Exists(global::System.IO.Path.Join(dir.FullName, "Reactor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string DevtoolsDir() => global::System.IO.Path.Join(RepoRoot(), "src", "Reactor.Devtools");

    private const string DirectReference = """<ItemGroup><PackageReference Include="Microsoft.UI.Reactor.Devtools" /></ItemGroup>""";

    /// <summary>
    /// Runs a stand-in <c>CoreCompile</c> and returns the build output. The project references
    /// the package directly unless <paramref name="body"/> says otherwise.
    /// </summary>
    private string Build(string configuration, string body = DirectReference, params string[] properties)
    {
        var targets = global::System.IO.Path.Join(DevtoolsDir(), "build", "Microsoft.UI.Reactor.Devtools.targets");
        var project = $"""
            <Project>
              {body}
              <Import Project="{targets}" />
              <Target Name="CoreCompile" />
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
        psi.ArgumentList.Add("-v:m");
        psi.ArgumentList.Add("-t:CoreCompile");
        psi.ArgumentList.Add($"-p:Configuration={configuration}");
        foreach (var property in properties) psi.ArgumentList.Add($"-p:{property}");

        using var process = Process.Start(psi)!;
        // Drain both pipes concurrently and bound the wait, so a hung msbuild fails this test
        // instead of blocking the whole unit run.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("dotnet msbuild did not finish within 120 s.");
        }
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        Assert.True(process.ExitCode == 0, $"dotnet msbuild failed ({process.ExitCode}):\n{stdout}\n{stderr}");
        return stdout + stderr;
    }

    private static bool Warns(string output) => output.Contains($"warning {Code}", StringComparison.Ordinal);

    private static string NoWarn(string value) => DirectReference + $"<PropertyGroup><NoWarn>{value}</NoWarn></PropertyGroup>";

    [Fact]
    public void Release_Warns_WithCopyPasteableSuppression()
    {
        var output = Build("Release");
        Assert.True(Warns(output), output);
        // The hint must survive MSBuild expansion verbatim, so pasting it appends to NoWarn.
        Assert.Contains("<NoWarn>$(NoWarn);REACTOR_DEVTOOLS_001</NoWarn>", output, StringComparison.Ordinal);
        Assert.Contains("<ReactorSuppressDevtoolsDeprecationWarning>true</ReactorSuppressDevtoolsDeprecationWarning>", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Debug_DoesNotWarn()
    {
        // Debug-only references are the supported preview setup (the dotnet new reactor templates).
        var output = Build("Debug");
        Assert.False(Warns(output), output);
    }

    [Theory]
    [InlineData("CS1591;REACTOR_DEVTOOLS_001")]
    [InlineData("CS1591, REACTOR_DEVTOOLS_001")]
    [InlineData("REACTOR_DEVTOOLS_001")]
    public void Release_NoWarn_Suppresses(string noWarn)
    {
        var output = Build("Release", NoWarn(noWarn));
        Assert.False(Warns(output), output);
    }

    [Fact]
    public void Release_NoWarnWithALookalikeCode_StillWarns()
    {
        // Positive control for the NoWarn match: only the exact code suppresses.
        var output = Build("Release", NoWarn("CS1591;REACTOR_DEVTOOLS_0010"));
        Assert.True(Warns(output), output);
    }

    [Fact]
    public void Release_AfterADebugRestore_DebugOnlyReference_DoesNotWarn()
    {
        // NuGet imports the targets whenever the restore resolved the package, even for a
        // configuration whose evaluation has no PackageReference (restore in Debug, build -c Release).
        var output = Build("Release", """<ItemGroup Condition="'$(Configuration)' == 'Debug'"><PackageReference Include="Microsoft.UI.Reactor.Devtools" /></ItemGroup>""");
        Assert.False(Warns(output), output);
    }

    [Fact]
    public void Release_OptOutProperty_Suppresses()
    {
        var output = Build("Release", properties: "ReactorSuppressDevtoolsDeprecationWarning=true");
        Assert.False(Warns(output), output);
    }

    [Fact]
    public void Release_DesignTimeBuild_DoesNotWarn()
    {
        var output = Build("Release", properties: "DesignTimeBuild=true");
        Assert.False(Warns(output), output);
    }

    [Fact]
    public void Package_PacksTheTargetsToBuildOnly()
    {
        // build\ (not buildTransitive\) warns the project that references the package directly,
        // not every project above it.
        var csproj = XDocument.Load(global::System.IO.Path.Join(DevtoolsDir(), "Reactor.Devtools.csproj"));
        var packed = csproj.Descendants()
            .Where(static e => (string?)e.Attribute("Pack") == "true"
                && ((string?)e.Attribute("Include") ?? "").EndsWith("Microsoft.UI.Reactor.Devtools.targets", StringComparison.Ordinal))
            .Select(static e => (string?)e.Attribute("PackagePath"))
            .ToList();
        Assert.Equal([@"build\"], packed);
    }
}
