#nullable enable

using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Reactor.VsExtension.Embed;
using Xunit;

namespace Reactor.VsExtension.Tests
{
    public sealed class ProjectContextResolverTests : IDisposable
    {
        private readonly string _root = Path.Combine(FindRepositoryRoot(), ".test-artifacts", "ProjectContextResolverTests", Guid.NewGuid().ToString("N"));

        [Fact]
        public void ComponentRegex_FindsBasicComponent()
        {
            var result = ProjectContextResolver.FindComponentClasses("public class Foo : Component { }");

            Assert.Equal(new[] { "Foo" }, result);
        }

        [Fact]
        public void ComponentRegex_FindsGenericComponent()
        {
            var result = ProjectContextResolver.FindComponentClasses("public class Foo<T> : Component<T> { }");

            Assert.Equal(new[] { "Foo" }, result);
        }

        [Fact]
        public void ComponentRegex_IgnoresUnrelatedClasses()
        {
            var result = ProjectContextResolver.FindComponentClasses("public class Bar : IDisposable { }");

            Assert.Empty(result);
        }

        [Fact]
        public void ComponentRegex_HandlesNestedNamespaces()
        {
            var source = @"
namespace A
{
    public class First : Component { }
    namespace B
    {
        internal class Second : Component<int> { }
    }
}";

            var result = ProjectContextResolver.FindComponentClasses(source);

            Assert.Equal(new[] { "First", "Second" }, result);
        }

        [Fact]
        public void ComponentRegex_HandlesPartialClass()
        {
            var result = ProjectContextResolver.FindComponentClasses("public partial class Foo : Component { }");

            Assert.Equal(new[] { "Foo" }, result);
        }

        [Fact]
        public void ComponentRegex_IgnoresCommentsAndStrings()
        {
            var source = @"
// public class Foo : Component { }
var text = ""public class Bar : Component { }"";
/* public class Baz : Component { } */
public class Real : Component { }";

            var result = ProjectContextResolver.FindComponentClasses(source);

            Assert.Equal(new[] { "Real" }, result);
        }

        [Fact]
        public void ProjectResolver_WalksToParentCsproj()
        {
            var project = WriteFile("proj.csproj", "<Project />");
            var file = WriteFile(Path.Combine("sub", "sub2", "file.cs"), string.Empty);

            var result = ProjectContextResolver.FindContainingCsproj(file);

            Assert.Equal(project, result);
        }

        [Fact]
        public void ProjectResolver_ReturnsNull_ForOrphanFile()
        {
            var file = WriteFile("file.cs", string.Empty);

            var result = ProjectContextResolver.FindContainingCsproj(file);

            Assert.Null(result);
        }

        [Fact]
        public void FindAllComponentsInProject_ExcludesObjBin()
        {
            var project = WriteFile("proj.csproj", "<Project />");
            WriteFile(Path.Combine("src", "A.cs"), "public class A : Component { }");
            WriteFile(Path.Combine("obj", "B.cs"), "public class B : Component { }");
            WriteFile(Path.Combine("bin", "C.cs"), "public class C : Component { }");
            WriteFile(Path.Combine(".git", "D.cs"), "public class D : Component { }");
            WriteFile(Path.Combine(".vs", "E.cs"), "public class E : Component { }");

            var result = ProjectContextResolver.FindAllComponentsInProject(project);

            Assert.Equal(new[] { "A" }, result.ToArray());
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private static string FindRepositoryRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Reactor.slnx")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not locate Reactor.slnx from test output directory.");
        }

        private string WriteFile(string relativePath, string contents)
        {
            var path = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
            return path;
        }
    }
}
