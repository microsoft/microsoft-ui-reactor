#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Reactor.VsExtension.Embed;
using Xunit;
using Xunit.Abstractions;

namespace Reactor.VsExtension.Tests
{
    public sealed class DotnetResolverTests
    {
        private readonly ITestOutputHelper _output;

        public DotnetResolverTests(ITestOutputHelper output)
        {
            _output = output;
        }
        [Fact]
        public void Resolver_RejectsWorkspaceLocal()
        {
            using (var dirs = TestDirectories.Create())
            {
                var fakeDotnet = Path.Combine(dirs.Workspace, "dotnet.exe");
                File.WriteAllText(fakeDotnet, "not dotnet");

                var result = DotnetResolver.Resolve(dirs.Workspace, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PATH"] = dirs.Workspace,
                    ["ProgramFiles"] = dirs.MissingProgramFiles,
                });

                Assert.Null(result);
            }
        }

        [Fact]
        [Trait("Category", "RequiresSymlinkPrivilege")]
        public void Resolver_RejectsSymlinkEscape()
        {
            using (var dirs = TestDirectories.Create())
            {
                var target = Path.Combine(dirs.Workspace, "evil.exe");
                var link = Path.Combine(dirs.Outside, "dotnet.exe");
                File.WriteAllText(target, "not dotnet");

                if (!TryCreateFileSymlink(link, target))
                {
                    _output.WriteLine("WARNING: Resolver_RejectsSymlinkEscape requires SeCreateSymbolicLinkPrivilege or Windows Developer Mode; symlink assertion was not exercised.");
                    return;
                }

                var result = DotnetResolver.Resolve(dirs.Workspace, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PATH"] = dirs.Outside,
                    ["ProgramFiles"] = dirs.MissingProgramFiles,
                });

                Assert.Null(result);
            }
        }

        [Fact]
        public void Resolver_ReturnsSystemPath_OnHappyPath()
        {
            using (var dirs = TestDirectories.Create())
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var systemDotnet = Path.Combine(programFiles, "dotnet", "dotnet.exe");
                if (!File.Exists(systemDotnet))
                {
                    return;
                }

                var result = DotnetResolver.Resolve(dirs.Workspace, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PATH"] = Path.GetDirectoryName(systemDotnet)!,
                    ["ProgramFiles"] = programFiles,
                });

                Assert.NotNull(result);
                Assert.Equal("PATH", result!.Source);
                Assert.Equal(systemDotnet, result.Path);
            }
        }

        private static bool TryCreateFileSymlink(string link, string target)
        {
            try
            {
                return CreateSymbolicLinkW(link, target, 0);
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateSymbolicLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateSymbolicLinkW(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

        private sealed class TestDirectories : IDisposable
        {
            private TestDirectories(string root)
            {
                Root = root;
                Workspace = Path.Combine(root, "workspace");
                Outside = Path.Combine(root, "outside");
                MissingProgramFiles = Path.Combine(root, "missing-program-files");
                Directory.CreateDirectory(Workspace);
                Directory.CreateDirectory(Outside);
            }

            public string Root { get; }

            public string Workspace { get; }

            public string Outside { get; }

            public string MissingProgramFiles { get; }

            public static TestDirectories Create()
            {
                var root = Path.Combine(Directory.GetCurrentDirectory(), "resolver-test-work", Guid.NewGuid().ToString("N"));
                return new TestDirectories(root);
            }

            public void Dispose()
            {
                try
                {
                    if (Directory.Exists(Root))
                    {
                        Directory.Delete(Root, recursive: true);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
