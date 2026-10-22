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
        => InAFreshLoad(sourceMap, diagnostics, devtoolsSupport, static assembly =>
            (bool)assembly.GetType(typeof(ReactorSourceMap).FullName!, throwOnError: true)!
                .GetProperty(nameof(ReactorSourceMap.Enabled))!.GetValue(null)!);

    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: loads a second copy of Reactor.dll into a collectible load context so its static initializers run again. Intentional and JIT-only (this host is never trimmed); behaviour-neutral.")]
    private static T InAFreshLoad<T>(string? sourceMap, string? diagnostics, bool devtoolsSupport, Func<global::System.Reflection.Assembly, T> probe)
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
            return probe(assembly);
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

    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: reflects known members on a fresh copy of a type this assembly references directly. Intentional and JIT-only (this host is never trimmed); behaviour-neutral.")]
    [global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Test-only: reflects known members on a fresh copy of a type this assembly references directly. Intentional and JIT-only (this host is never trimmed); behaviour-neutral.")]
    [Fact]
    public void HotReload_InvalidatesStaticFacts_WhateverTheDevtoolsSwitch()
    {
        // SourceLocation.DeclaredName reads the static tables in any source-mapped build, so a
        // hot-reload update must invalidate them with Reactor.DevtoolsSupport off too. The
        // switch is cached at startup, so each case runs on a fresh copy of Reactor.dll.
        static (bool Before, bool After) Probe(global::System.Reflection.Assembly assembly)
        {
            const global::System.Reflection.BindingFlags Any =
                global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic;
            var map = assembly.GetType(typeof(ReactorSourceMap).FullName!, throwOnError: true)!;
            var invalidated = map.GetProperty("StaticFactsInvalidatedByHotReload", Any)!;
            bool before = (bool)invalidated.GetValue(null)!;
            assembly.GetType("Microsoft.UI.Reactor.Hosting.HotReloadService", throwOnError: true)!
                .GetMethod("UpdateApplication", Any)!.Invoke(null, [null]);
            return (before, (bool)invalidated.GetValue(null)!);
        }

        Assert.Equal((false, true), InAFreshLoad(null, null, devtoolsSupport: false, Probe));
        Assert.Equal((false, true), InAFreshLoad(null, null, devtoolsSupport: true, Probe));
    }
}