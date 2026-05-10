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
