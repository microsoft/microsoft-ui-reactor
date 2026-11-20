using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.UI.Reactor.Core;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Tooling;

/// <summary>
/// The shipped libraries must keep compiling against the Windows SDK projection that
/// apps on the .NET 10 SDK resolve. Each .NET SDK bundles its own default
/// <c>Microsoft.Windows.SDK.NET.Ref</c>; the 11.0.100 SDK's (10.0.22621.87) references
/// <c>Microsoft.Windows.SDK.NET</c> 10.0.22621.86 and <c>WinRT.Runtime</c> 2.3.1, and an
/// app on the .NET 10 SDK then fails to reference Reactor with NETSDK1148. The repo pins
/// <c>WindowsSdkPackageVersion</c> in <c>Directory.Build.targets</c>; this guards that pin
/// against a future SDK bump or an accidental removal.
/// </summary>
public sealed class BuiltLibrariesProjectionTests
{
    // What Microsoft.Windows.SDK.NET.Ref 10.0.22621.57 carries, and what
    // 0.1.0-preview.16/.17 shipped against. Raising either is a consumer-facing change.
    private static readonly global::System.Version WindowsSdkNet = new(10, 0, 22621, 38);
    private static readonly global::System.Version WinRTRuntime = new(2, 2, 0, 0);

    [Fact]
    public void Reactor_references_the_pinned_Windows_SDK_projection()
        => AssertPinned(ReferencedVersions(typeof(Element)));

    [Fact]
    public void ReactorAdvanced_references_the_pinned_Windows_SDK_projection()
        => AssertPinned(ReferencedVersions(typeof(global::Microsoft.UI.Reactor.Advanced.Factories)));

    private static void AssertPinned(IReadOnlyDictionary<string, global::System.Version> refs)
    {
        Assert.Equal(WindowsSdkNet, refs["Microsoft.Windows.SDK.NET"]);
        Assert.Equal(WinRTRuntime, refs["WinRT.Runtime"]);
    }

    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Test-only: reads the on-disk Location of Reactor.dll / Reactor.Advanced.dll to feed the metadata scanner (PEReader). IL3000 only affects single-file publish (Location is empty there); this metadata-scanning test cannot run single-file and this host is not single-file-published. Behaviour-neutral.")]
    private static Dictionary<string, global::System.Version> ReferencedVersions(Type typeInAssembly)
    {
        var path = typeInAssembly.Assembly.Location;
        Assert.False(string.IsNullOrEmpty(path), "Could not locate the assembly on disk.");
        using var stream = global::System.IO.File.OpenRead(path);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var result = new Dictionary<string, global::System.Version>(StringComparer.Ordinal);
        foreach (var reference in md.AssemblyReferences.Select(md.GetAssemblyReference))
        {
            result[md.GetString(reference.Name)] = reference.Version;
        }
        return result;
    }
}
