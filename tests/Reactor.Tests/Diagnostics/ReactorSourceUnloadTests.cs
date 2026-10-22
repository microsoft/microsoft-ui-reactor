using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Core.Diagnostics;
using Microsoft.UI.Reactor.Diagnostics;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.Diagnostics;

/// <summary>
/// A source-mapped plug-in in a collectible <see cref="AssemblyLoadContext"/> that shares the
/// host's Reactor must still unload after ReactorSource diagnostics have seen it: its static-info
/// registration (before and after the first table lookup), and its element types formatted
/// into a published value. Each scenario compiles a fresh plug-in, uses it, unloads it, and
/// requires the load context to be collected.
/// </summary>
// Mutates the process-wide static-info registry.
[Collection("ReactorSourcePublisherGlobals")]
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only: compiles a plug-in with Roslyn, loads it into a collectible load context and drives it by reflection. JIT-only host, never trimmed or single-file published.")]
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Test-only: compiles a plug-in with Roslyn, loads it into a collectible load context and drives it by reflection. JIT-only host, never trimmed or single-file published.")]
[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Test-only: compiles a plug-in with Roslyn, loads it into a collectible load context and drives it by reflection. JIT-only host, never trimmed or single-file published.")]
public sealed class ReactorSourceUnloadTests
{
    private const string PluginSource = """
        using Microsoft.UI.Reactor.Core;
        using Microsoft.UI.Reactor.Diagnostics;

        namespace UnloadPlugin
        {
            public sealed record PluginElement : Element;

            public sealed class PluginComponent : Component
            {
                public override Element Render() => new PluginElement();
            }

            public static class Entry
            {
                // What the generated module initializer does.
                public static void Register() => ReactorSourceMap.RegisterStaticInfo(typeof(Entry).Assembly, Fill);

                private static void Fill(ReactorStaticInfoBuilder b)
                {
                    b.ComponentHooks("UnloadPlugin.PluginComponent", "0:count@7");
                    b.Name("/_/UnloadPlugin/Page.cs", 3, 9, "title");
                }

                public static Element Create() => new PluginElement();
            }
        }
        """;

    private static readonly Lazy<byte[]> s_plugin = new(Compile);

    private static byte[] Compile()
    {
        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        var references = tpa.Split(global::System.IO.Path.PathSeparator)
            .Where(static p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && global::System.IO.File.Exists(p))
            .Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(Element).Assembly.Location))
            .ToImmutableArray();
        var compilation = CSharpCompilation.Create(
            $"UnloadPlugin{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText(PluginSource)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var stream = new global::System.IO.MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
        return stream.ToArray();
    }

    // Resolves nothing itself, so Reactor (and everything else) comes from the default context:
    // the plug-in shares the host's Reactor, which is the retaining configuration.
    private sealed class PluginContext() : AssemblyLoadContext($"reactor-unload-{Guid.NewGuid():N}", isCollectible: true);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Run(Action<global::System.Reflection.Assembly> use)
    {
        var context = new PluginContext();
        using (var image = new global::System.IO.MemoryStream(s_plugin.Value))
            use(context.LoadFromStream(image));
        context.Unload();
        return new WeakReference(context);
    }

    private static bool Unloaded(WeakReference context)
    {
        for (int i = 0; i < 20 && context.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return !context.IsAlive;
    }

    private static object? Invoke(global::System.Reflection.Assembly plugin, string method)
        => plugin.GetType("UnloadPlugin.Entry", throwOnError: true)!.GetMethod(method)!.Invoke(null, null);

    [Fact]
    public void RegisteredPlugin_UnloadsBeforeTheFirstLookup()
    {
        var context = Run(static plugin => Invoke(plugin, "Register"));
        Assert.True(Unloaded(context), "the pending static-info registration kept the plug-in alive");
        // The registry still works after the plug-in is gone.
        Assert.Null(ReactorSourceMap.GetComponentHooks(typeof(ReactorSourceUnloadTests)));
    }

    [Fact]
    public void RegisteredPlugin_UnloadsAfterTheTableIsBuilt()
    {
        string? hooks = null;
        var context = Run(plugin =>
        {
            Invoke(plugin, "Register");
            hooks = ReactorSourceMap.GetComponentHooks(plugin.GetType("UnloadPlugin.PluginComponent", throwOnError: true)!);
        });
        // Positive control: the plug-in's facts were really in the built table.
        Assert.Equal("0:count@7", hooks);
        Assert.True(Unloaded(context), "the built static-info table kept the plug-in alive");
    }

    [Fact]
    public void FormattedPluginElement_DoesNotKeepThePluginAlive()
    {
        string? value = null;
        var context = Run(plugin =>
        {
            var element = (Element)Invoke(plugin, "Create")!;
            value = ReactorSourcePublisher.Format(element with { CallSite = new SourceLocation("/_/UnloadPlugin/Page.cs", 5, 1) }, "Owner");
        });
        // Positive control: the plug-in's element really went through the value cache.
        Assert.Contains("|element=Plugin", value, StringComparison.Ordinal);
        Assert.True(Unloaded(context), "the published-value cache kept the plug-in's element type alive");
    }
}
