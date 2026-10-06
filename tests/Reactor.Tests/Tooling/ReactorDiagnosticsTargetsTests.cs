using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// <c>$(ReactorDiagnostics)</c> in <c>build/Reactor.targets</c>: the MSBuild switch that turns
/// on inspector diagnostics (<c>ReactorDiagnostics.SourceProperty</c>). Evaluates the real
/// targets file in a throwaway project with <c>dotnet msbuild -getProperty / -getItem</c>,
/// after running the switch-normalization target, so what is asserted is exactly what the
/// SDK writes into <c>runtimeconfig.json</c> and hands ILC as a feature setting.
///
/// <para>The project-declared switch is placed BEFORE the import (a PackageReference
/// consumer: NuGet imports the targets after the project body) and AFTER it (an in-repo
/// project, which imports them from Directory.Build.props), because the explicit-wins rule
/// has to hold in both evaluation orders.</para>
/// </summary>
public sealed class ReactorDiagnosticsTargetsTests : IDisposable
{
    private readonly string _dir = global::System.IO.Path.Join(
        global::System.IO.Path.GetTempPath(), $"reactor-diag-targets-{Guid.NewGuid():N}");

    public ReactorDiagnosticsTargetsTests() => global::System.IO.Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { global::System.IO.Directory.Delete(_dir, recursive: true); } catch (global::System.IO.IOException) { }
    }

    private static string TargetsPath()
    {
        var dir = new global::System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !global::System.IO.File.Exists(global::System.IO.Path.Join(dir.FullName, "Reactor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return global::System.IO.Path.Join(dir!.FullName, "src", "Reactor", "build", "Reactor.targets");
    }

    private sealed record Result(string? Diagnostics, string? SourceMap, IReadOnlyList<(string Value, string Trim)> Switch);

    private Result Evaluate(string configuration, string? projectSwitch = null, bool switchAfterImport = false, params string[] properties)
        => EvaluateWithBody(configuration, projectSwitch, switchAfterImport, bodyAfterImport: null, properties);

    private Result EvaluateWithBody(string configuration, string? projectSwitch, bool switchAfterImport, string? bodyAfterImport, params string[] properties)
    {
        var item = projectSwitch is null
            ? string.Empty
            : $"""<ItemGroup><RuntimeHostConfigurationOption Include="Reactor.DevtoolsSupport" Value="{projectSwitch}" Trim="true" /></ItemGroup>""";
        var import = $"""<Import Project="{TargetsPath()}" />""";
        var project = $"""
            <Project>
              {(switchAfterImport ? string.Empty : item)}
              {import}
              {(switchAfterImport ? item : string.Empty)}
              {bodyAfterImport ?? string.Empty}
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
        psi.ArgumentList.Add($"-p:Configuration={configuration}");
        foreach (var property in properties) psi.ArgumentList.Add($"-p:{property}");
        psi.ArgumentList.Add("-t:_ReactorApplyLateDiagnosticsOptIn;_NormalizeReactorDevtoolsSupportSwitch");
        psi.ArgumentList.Add("-getProperty:ReactorDiagnostics");
        psi.ArgumentList.Add("-getProperty:ReactorSourceMap");
        psi.ArgumentList.Add("-getItem:RuntimeHostConfigurationOption");

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"dotnet msbuild failed ({process.ExitCode}):\n{stdout}\n{stderr}");

        using var json = JsonDocument.Parse(stdout);
        var props = json.RootElement.GetProperty("Properties");
        var items = json.RootElement.GetProperty("Items").GetProperty("RuntimeHostConfigurationOption")
            .EnumerateArray()
            .Where(static i => i.GetProperty("Identity").GetString() == "Reactor.DevtoolsSupport")
            .Select(static i => (i.GetProperty("Value").GetString() ?? "", i.TryGetProperty("Trim", out var t) ? t.GetString() ?? "" : ""))
            .ToList();
        return new Result(
            NullIfEmpty(props.GetProperty("ReactorDiagnostics").GetString()),
            NullIfEmpty(props.GetProperty("ReactorSourceMap").GetString()),
            items);
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    [Fact]
    public void Debug_DefaultsOn_SwitchOnAndTrimmable_SourceMapOn()
    {
        var r = Evaluate("Debug");
        Assert.Equal("true", r.Diagnostics);
        Assert.Equal("true", r.SourceMap);
        Assert.Equal([("true", "true")], r.Switch);
    }

    [Fact]
    public void Release_DefaultsOff()
    {
        var r = Evaluate("Release");
        Assert.Null(r.Diagnostics);
        Assert.Null(r.SourceMap);
        Assert.Equal([("false", "true")], r.Switch);
    }

    [Fact]
    public void Release_ExplicitOptIn_TurnsOnSwitchAndSourceMap()
    {
        // What winapp passes for `run --aot --devtools`.
        var r = Evaluate("Release", properties: ["ReactorDiagnostics=true"]);
        Assert.Equal("true", r.SourceMap);
        Assert.Equal([("true", "true")], r.Switch);
    }

    [Fact]
    public void Debug_ExplicitOff_Wins()
    {
        var r = Evaluate("Debug", properties: ["ReactorDiagnostics=false"]);
        Assert.Equal("false", r.Diagnostics);
        Assert.Equal([("false", "true")], r.Switch);
    }

    [Fact]
    public void Debug_ExplicitSourceMapOff_Wins()
        => Assert.Equal("false", Evaluate("Debug", properties: ["ReactorSourceMap=false"]).SourceMap);

    [Theory]
    [InlineData(false)] // PackageReference order: project body, then the package's targets
    [InlineData(true)]  // in-repo order: Directory.Build.props imports the targets first
    public void Debug_ProjectDeclaredSwitchOff_BeatsTheDefault(bool switchAfterImport)
        => Assert.Equal([("false", "true")], Evaluate("Debug", "false", switchAfterImport).Switch);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitOptIn_BeatsAProjectDeclaredSwitchOff(bool switchAfterImport)
        => Assert.Equal(
            [("true", "true")],
            Evaluate("Release", "false", switchAfterImport, "ReactorDiagnostics=true").Switch);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitOff_BeatsAProjectDeclaredSwitchOn(bool switchAfterImport)
        // A scaffolded Debug app declares Reactor.DevtoolsSupport=true itself; opting out of
        // diagnostics must still turn the switch off (explicit wins, both ways).
        => Assert.Equal(
            [("false", "true")],
            Evaluate("Debug", "true", switchAfterImport, "ReactorDiagnostics=false").Switch);

    [Theory]
    [InlineData(null)]
    [InlineData("true")]
    public void ProjectBodyOffAfterTheImport_Wins(string? projectSwitch)
    {
        // In-repo order: Directory.Build.props imported the targets (and their Debug default)
        // before the project body set the property; the final value must decide.
        var r = EvaluateWithBody("Debug", projectSwitch, switchAfterImport: true,
            bodyAfterImport: "<PropertyGroup><ReactorDiagnostics>false</ReactorDiagnostics></PropertyGroup>");
        Assert.Equal("false", r.Diagnostics);
        Assert.Equal([("false", "true")], r.Switch);
    }
    [Fact]
    public void ReleaseProjectBodyOptInAfterTheImport_TurnsOnSwitchAndSourceMap()
    {
        // In-repo order again, opting IN from the body of a Release project: the switch item
        // sees the final value at evaluation, and the source-map implication is applied from
        // the final value at build time (_ReactorApplyLateDiagnosticsOptIn).
        var r = EvaluateWithBody("Release", projectSwitch: null, switchAfterImport: true,
            bodyAfterImport: "<PropertyGroup><ReactorDiagnostics>true</ReactorDiagnostics></PropertyGroup>");
        Assert.Equal("true", r.Diagnostics);
        Assert.Equal("true", r.SourceMap);
        Assert.Equal([("true", "true")], r.Switch);
    }
}