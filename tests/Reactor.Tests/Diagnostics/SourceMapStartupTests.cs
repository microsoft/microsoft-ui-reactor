using System.Runtime.Loader;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// <c>ReactorSourceMap.Enabled</c>'s value at startup, before any host exists. Elements an
/// app builds before its first host (a page's field initializers run before
/// <c>new ReactorHostControl(page)</c>) are stamped only if stamping is already on, so the
/// diagnostics launch opt-in has to apply when the gate initializes, not when a host does.
///
/// <para>The default load context initialized the gate long ago, so each case loads a fresh
/// copy of Reactor.dll into a collectible <see cref="AssemblyLoadContext"/>: its static
/// initializers run again, under the environment the case sets. A fresh load also reads the
/// live <c>Reactor.DevtoolsSupport</c> AppContext switch, which other tests flip, so the case
/// sets it to the configured value for the load, and runs in the same collection as those
/// tests so none of them can flip it meanwhile.</para>
/// </summary>
[Collection("ConsoleTests")]
public sealed class SourceMapStartupTests
{
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: loads a second copy of Reactor.dll into a collectible load context so its static initializers run again, then reads one known public property. Intentional and JIT-only (this host is never trimmed); behaviour-neutral.")]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Test-only: reflects the known public Enabled property on the freshly loaded copy of a type this assembly references directly. Intentional and JIT-only (this host is never trimmed); behaviour-neutral.")]
    private static bool EnabledInAFreshLoad(string? sourceMap, string? diagnostics, bool devtoolsSupport = true)
    {
        var previousMap = global::System.Environment.GetEnvironmentVariable("REACTOR_SOURCEMAP");
        var previousDiagnostics = global::System.Environment.GetEnvironmentVariable("REACTOR_DIAGNOSTICS");
        var context = new AssemblyLoadContext($"reactor-startup-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            global::System.Environment.SetEnvironmentVariable("REACTOR_SOURCEMAP", sourceMap);
            global::System.Environment.SetEnvironmentVariable("REACTOR_DIAGNOSTICS", diagnostics);
            AppContext.SetSwitch("Reactor.DevtoolsSupport", devtoolsSupport);
            var path = global::System.IO.Path.Join(AppContext.BaseDirectory, typeof(ReactorSourceMap).Assembly.GetName().Name + ".dll");
            var assembly = context.LoadFromAssemblyPath(path);
            Assert.NotSame(typeof(ReactorSourceMap).Assembly, assembly);
            var type = assembly.GetType(typeof(ReactorSourceMap).FullName!, throwOnError: true)!;
            return (bool)type.GetProperty(nameof(ReactorSourceMap.Enabled))!.GetValue(null)!;
        }
        finally
        {
            global::System.Environment.SetEnvironmentVariable("REACTOR_SOURCEMAP", previousMap);
            global::System.Environment.SetEnvironmentVariable("REACTOR_DIAGNOSTICS", previousDiagnostics);
            AppContext.SetSwitch("Reactor.DevtoolsSupport", TestSetup.ConfiguredDevtoolsSupport);
            context.Unload();
        }
    }

    [Fact]
    public void DiagnosticsLaunchOptIn_EnablesStampingAtStartup()
    {
        Assert.True(TestSetup.ConfiguredDevtoolsSupport, "precondition: this suite runs with Reactor.DevtoolsSupport on");
        Assert.False(EnabledInAFreshLoad(sourceMap: null, diagnostics: null));
        Assert.True(EnabledInAFreshLoad(sourceMap: null, diagnostics: "1"));
        Assert.True(EnabledInAFreshLoad(sourceMap: "1", diagnostics: null));
        Assert.False(EnabledInAFreshLoad(sourceMap: null, diagnostics: "true"));
        // A build without the switch ignores the diagnostics opt-in, but not REACTOR_SOURCEMAP.
        Assert.False(EnabledInAFreshLoad(sourceMap: null, diagnostics: "1", devtoolsSupport: false));
        Assert.True(EnabledInAFreshLoad(sourceMap: "1", diagnostics: null, devtoolsSupport: false));
    }
}
