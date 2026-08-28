using System.Reflection;
using Xunit;

namespace Microsoft.UI.Reactor.Tests;

/// <summary>
/// Issue #1143 — the `mur --create` scaffold writes its placeholder app icon out of an
/// embedded resource in the CLI assembly.
/// </summary>
/// <remarks>
/// The failure mode this guards is real and silent: the resource is contributed by an
/// <c>&lt;EmbeddedResource&gt;</c> item whose <c>LogicalName</c> must match the string
/// <c>TryWriteAppIcon</c> looks up. A rename, a moved asset, or a dropped csproj item
/// makes <c>GetManifestResourceStream</c> return null, at which point scaffolding
/// silently degrades to a project with no icon — exactly what issue #1143 was about.
/// Nothing else in the build fails.
/// </remarks>
public class ReactorCliAppIconResourceTests
{
    private const string ResourceName = "ReactorAppIcon.ico";

    private static Assembly CliAssembly =>
        // Any type from the CLI assembly anchors it; the scaffolder itself lives in
        // top-level statements and is not directly referenceable.
        typeof(Microsoft.UI.Reactor.Cli.Check.Rules.RuleRegistry).Assembly;

    [Fact]
    public void Embedded_AppIcon_Resource_Is_Present()
    {
        using var stream = CliAssembly.GetManifestResourceStream(ResourceName);

        Assert.True(
            stream is not null,
            $"'{ResourceName}' is missing from {CliAssembly.GetName().Name}. " +
            $"Available: {string.Join(", ", CliAssembly.GetManifestResourceNames())}");
    }

    [Fact]
    public void Embedded_AppIcon_Resource_Is_A_Valid_Icon()
    {
        using var stream = CliAssembly.GetManifestResourceStream(ResourceName);
        Assert.NotNull(stream);

        var header = new byte[6];
        Assert.Equal(6, stream!.ReadAtLeast(header, 6, throwOnEndOfStream: false));

        // ICONDIR: reserved(0), type(1 = icon), count(>0). A .png or a truncated copy
        // would satisfy "resource exists" but produce a project whose icon never loads.
        Assert.Equal(0, header[0] | header[1]);
        Assert.Equal(1, header[2] | (header[3] << 8));

        var frameCount = header[4] | (header[5] << 8);
        Assert.True(frameCount > 0, $"icon declares {frameCount} frames");
    }
}
