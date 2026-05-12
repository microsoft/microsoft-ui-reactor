// Phase 1.2 — CompilationLoader tests. Spec 038 §5.

using System.Diagnostics;
using Microsoft.UI.Reactor.Cli.Check;
using Xunit;

namespace Microsoft.UI.Reactor.Tests.CheckCommandTests;

public class CompilationLoaderTests
{
    [Fact]
    public void Empty_path_returns_EmptyCompilation()
    {
        Assert.Same(CompilationLoader.EmptyCompilation, new CompilationLoader().Load(""));
    }

    [Fact]
    public void Nonexistent_path_returns_EmptyCompilation()
    {
        var loader = new CompilationLoader();
        var bogus = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "definitely-not-a-project-" + Guid.NewGuid());
        Assert.Same(CompilationLoader.EmptyCompilation, loader.Load(bogus));
    }

    [Fact]
    public void Directory_with_no_csproj_returns_EmptyCompilation()
    {
        using var tmp = TempProject.CreateBare();
        Assert.Same(CompilationLoader.EmptyCompilation, new CompilationLoader().Load(tmp.Root));
    }

    [Fact]
    public void Loads_csproj_with_a_single_cs_file()
    {
        using var tmp = TempProject.CreateMinimal();
        var compilation = new CompilationLoader().Load(tmp.Csproj);

        Assert.NotSame(CompilationLoader.EmptyCompilation, compilation);
        Assert.Single(compilation.SyntaxTrees);
        Assert.Contains(compilation.SyntaxTrees, t => t.FilePath.EndsWith("Program.cs"));
    }

    [Fact]
    public void Excludes_obj_and_bin_subtrees()
    {
        using var tmp = TempProject.CreateMinimal();
        // Plant a file under obj/ and bin/ that should be ignored.
        global::System.IO.Directory.CreateDirectory(global::System.IO.Path.Combine(tmp.Root, "obj"));
        global::System.IO.File.WriteAllText(global::System.IO.Path.Combine(tmp.Root, "obj", "Generated.cs"), "class G {}");
        global::System.IO.Directory.CreateDirectory(global::System.IO.Path.Combine(tmp.Root, "bin"));
        global::System.IO.File.WriteAllText(global::System.IO.Path.Combine(tmp.Root, "bin", "Output.cs"), "class O {}");

        var compilation = new CompilationLoader().Load(tmp.Csproj);
        Assert.DoesNotContain(compilation.SyntaxTrees, t => t.FilePath.Contains("Generated.cs"));
        Assert.DoesNotContain(compilation.SyntaxTrees, t => t.FilePath.Contains("Output.cs"));
    }

    [Fact]
    public void Warm_load_returns_cached_instance_when_files_unchanged()
    {
        using var tmp = TempProject.CreateMinimal();
        var loader = new CompilationLoader();

        var c1 = loader.Load(tmp.Csproj);
        var c2 = loader.Load(tmp.Csproj);
        Assert.Same(c1, c2);
    }

    [Fact]
    public void Cache_invalidates_when_file_mtime_changes()
    {
        using var tmp = TempProject.CreateMinimal();
        var loader = new CompilationLoader();
        var c1 = loader.Load(tmp.Csproj);

        // Touch with a clearly different timestamp.
        var program = global::System.IO.Path.Combine(tmp.Root, "Program.cs");
        global::System.IO.File.WriteAllText(program, "class Program { void X() {} }");
        global::System.IO.File.SetLastWriteTimeUtc(program, DateTime.UtcNow.AddSeconds(2));

        var c2 = loader.Load(tmp.Csproj);
        Assert.NotSame(c1, c2);
    }

    [Fact]
    public void Resolves_ProjectReference_built_dll_from_project_assets_json()
    {
        // Regression: without this path the rule registry self-disables every
        // Tier-3 rule whose DeclaredTargets point at a Reactor type, because
        // ResolveType returns null against a compilation built from
        // CompilationLoader output. Real project.assets.json carries project
        // references in `libraries.<id>` with type="project" and a `path`
        // pointing at the referenced .csproj; the built dll lives under that
        // project's bin/ tree. This test mirrors that shape end-to-end:
        //  - referenced project: a temp dir with FakeLib.csproj + a built
        //    FakeLib.dll under bin/Debug/net10.0/.
        //  - consumer project: a temp dir with a project.assets.json that
        //    points at FakeLib.csproj via `libraries.FakeLib/0.0.0.path`.
        //  - assertion: ResolveReferences includes the built dll.
        using var refProj = TempProject.CreateMinimal();
        var refProjDir = refProj.Root;
        // Move the csproj to a known-name file so the AssemblyName fallback
        // (csproj basename) finds the dll under bin/.
        var fakeLibCsproj = global::System.IO.Path.Combine(refProjDir, "FakeLib.csproj");
        global::System.IO.File.Move(refProj.Csproj, fakeLibCsproj);
        // Plant a bin tree with a dll whose name matches AssemblyName fallback.
        var binDir = global::System.IO.Path.Combine(refProjDir, "bin", "Debug", "net10.0");
        global::System.IO.Directory.CreateDirectory(binDir);
        var fakeDll = global::System.IO.Path.Combine(binDir, "FakeLib.dll");
        global::System.IO.File.WriteAllBytes(fakeDll, MinimalEmptyDll());

        // Build the consumer project + an assets.json that references FakeLib.
        using var consumer = TempProject.CreateMinimal();
        var consumerObj = global::System.IO.Path.Combine(consumer.Root, "obj");
        global::System.IO.Directory.CreateDirectory(consumerObj);
        // The path in assets.json is relative to the consumer csproj's directory.
        var rel = global::System.IO.Path.GetRelativePath(consumer.Root, fakeLibCsproj).Replace('\\', '/');
        var assetsJson = $@"{{
  ""packageFolders"": {{}},
  ""targets"": {{ ""net10.0"": {{}} }},
  ""libraries"": {{
    ""FakeLib/0.0.0"": {{
      ""type"": ""project"",
      ""path"": ""{rel}"",
      ""msbuildProject"": ""{rel}""
    }}
  }}
}}";
        global::System.IO.File.WriteAllText(global::System.IO.Path.Combine(consumerObj, "project.assets.json"), assetsJson);

        var refs = CompilationLoader.ResolveReferences(consumer.Csproj);
        var hit = refs.OfType<Microsoft.CodeAnalysis.PortableExecutableReference>()
            .Any(r => string.Equals(global::System.IO.Path.GetFileName(r.FilePath), "FakeLib.dll", StringComparison.OrdinalIgnoreCase));
        Assert.True(hit, "ProjectReference's built dll must be resolvable from project.assets.json");
    }

    /// <summary>
    /// Returns the bytes of a minimal valid .NET assembly. Used by the
    /// ProjectReference resolution test — we need a real PE+CLI header so
    /// `MetadataReference.CreateFromFile` succeeds, but the assembly's
    /// contents don't matter (the test only checks that a reference was
    /// added, not that any specific type resolved).
    /// </summary>
    static byte[] MinimalEmptyDll()
    {
        // Compile an empty assembly in-memory and return its bytes.
        var src = "namespace FakeLib { internal class _Probe {} }";
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(src);
        // Pull a minimal system reference set from the host so the empty
        // compilation can resolve `object` etc.
        var systemRefs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .Select(a => { try { return a.Location; } catch { return null; } })
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(p!))
            .Cast<Microsoft.CodeAnalysis.MetadataReference>()
            .ToArray();
        var c = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "FakeLib",
            new[] { tree },
            systemRefs,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using var ms = new global::System.IO.MemoryStream();
        var emit = c.Emit(ms);
        Assert.True(emit.Success, "minimal empty dll must emit cleanly for test setup");
        return ms.ToArray();
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void Cold_load_under_500ms_warm_under_50ms_for_minimal_project()
    {
        using var tmp = TempProject.CreateMinimal();
        var loader = new CompilationLoader();

        var sw = Stopwatch.StartNew();
        loader.Load(tmp.Csproj);
        sw.Stop();
        var coldMs = sw.Elapsed.TotalMilliseconds;
        Assert.True(coldMs <= 1500,
            $"cold load took {coldMs:F1} ms (budget 500 ms; allow 3× for CI noise on a minimal fixture).");

        sw.Restart();
        loader.Load(tmp.Csproj);
        sw.Stop();
        var warmMs = sw.Elapsed.TotalMilliseconds;
        Assert.True(warmMs <= 200,
            $"warm load took {warmMs:F1} ms (budget 50 ms; allow 4× for CI noise).");
    }

    sealed class TempProject : IDisposable
    {
        public string Root { get; }
        public string Csproj { get; }

        TempProject(string root, string csproj) { Root = root; Csproj = csproj; }

        public static TempProject CreateMinimal()
        {
            var root = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "mur-loader-" + Guid.NewGuid());
            global::System.IO.Directory.CreateDirectory(root);
            var csproj = global::System.IO.Path.Combine(root, "Tiny.csproj");
            global::System.IO.File.WriteAllText(csproj, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            global::System.IO.File.WriteAllText(global::System.IO.Path.Combine(root, "Program.cs"), "class Program { static void Main() {} }");
            return new TempProject(root, csproj);
        }

        public static TempProject CreateBare()
        {
            var root = global::System.IO.Path.Combine(global::System.IO.Path.GetTempPath(), "mur-loader-bare-" + Guid.NewGuid());
            global::System.IO.Directory.CreateDirectory(root);
            return new TempProject(root, "");
        }

        public void Dispose()
        {
            try { global::System.IO.Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
